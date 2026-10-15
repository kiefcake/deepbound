using System.Collections;
using System.Linq;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deepbound.Player
{
    /// <summary>
    /// Alchemist's flasks, thrown with 1 / 2 / 3, for every class. Bought at the merchant
    /// (a satchel: one of each), carried up to three of a kind, lobbed at the crosshair.
    ///
    /// <list type="bullet">
    /// <item><b>1 — Fire Flask.</b> A pool of burning pitch: the dead in it burn for 4 s. Sets off powder barrels.</item>
    /// <item><b>2 — Frost Flask.</b> A burst of rime that chills everything it touches (Shatter!), and
    ///   freezes solid whatever stands in water.</item>
    /// <item><b>3 — Holy Water.</b> The dead reel (stunned) and burn; allies in the splash are mended.</item>
    /// </list>
    /// </summary>
    public class Flasks : NetworkBehaviour
    {
        public enum Kind : byte { Fire, Frost, Holy }
        public const int Max = 3;
        private const float Range = 16f, Flight = 0.55f, Cooldown = 0.8f;

        public readonly NetworkVariable<byte> Fire = new NetworkVariable<byte>();
        public readonly NetworkVariable<byte> Frost = new NetworkVariable<byte>();
        public readonly NetworkVariable<byte> Holy = new NetworkVariable<byte>();

        private PlayerLife life;
        private FirstPersonMotor motor;
        private NetworkPlayer player;
        private PlayerProgress progress;
        private float readyAt;

        public static Color ColorOf(Kind k) => k == Kind.Fire ? new Color(1f, 0.45f, 0.15f) : k == Kind.Frost ? new Color(0.55f, 0.85f, 1f) : new Color(1f, 0.92f, 0.6f);
        public static string NameOf(Kind k) => k == Kind.Fire ? "Fire" : k == Kind.Frost ? "Frost" : "Holy";

        public int Count(Kind k) => k == Kind.Fire ? Fire.Value : k == Kind.Frost ? Frost.Value : Holy.Value;
        public bool Full => Fire.Value >= Max && Frost.Value >= Max && Holy.Value >= Max;

        private void Awake()
        {
            life = GetComponent<PlayerLife>();
            motor = GetComponent<FirstPersonMotor>();
            player = GetComponent<NetworkPlayer>();
            progress = GetComponent<PlayerProgress>();
        }

        /// <summary>Server: a satchel — one more of each, up to the cap.</summary>
        public void ServerAddSatchel()
        {
            if (Fire.Value < Max) Fire.Value++;
            if (Frost.Value < Max) Frost.Value++;
            if (Holy.Value < Max) Holy.Value++;
        }

        /// <summary>Server: run over — the satchel empties with everything else.</summary>
        public void ServerReset() { Fire.Value = 0; Frost.Value = 0; Holy.Value = 0; }

        private void Update()
        {
            if (!IsOwner) return;
            var kb = Keyboard.current;
            if (kb == null || Time.time < readyAt || !life.CanAct || !motor.HasControl) return;
            Kind? k = kb.digit1Key.wasPressedThisFrame ? Kind.Fire : kb.digit2Key.wasPressedThisFrame ? Kind.Frost : kb.digit3Key.wasPressedThisFrame ? Kind.Holy : (Kind?)null;
            if (k == null || Count(k.Value) <= 0) return;
            readyAt = Time.time + Cooldown;
            var origin = player.AimOrigin;
            var dir = player.AimForward;
            Vector3 aim;
            if (Physics.Raycast(origin, dir, out var hit, Range, ~0, QueryTriggerInteraction.Ignore)) aim = hit.point;
            else aim = Physics.Raycast(origin + dir * Range, Vector3.down, out var down, 30f, ~0, QueryTriggerInteraction.Ignore) ? down.point : origin + dir * Range;
            ThrowRpc((byte)k.Value, aim);
        }

        /// <summary>Tests: throw as the owner would (host only).</summary>
        public void ServerDebugThrow(Kind k, Vector3 at)
        {
            if (Count(k) <= 0) ServerAddSatchel();
            ThrowRpc((byte)k, at);
        }

        [Rpc(SendTo.Server)]
        private void ThrowRpc(byte kind, Vector3 aim)
        {
            var k = (Kind)kind;
            if (!life.CanAct || Count(k) <= 0) return;
            if (k == Kind.Fire) Fire.Value--; else if (k == Kind.Frost) Frost.Value--; else Holy.Value--;
            var from = transform.position + Vector3.up * 1.5f;
            if (Vector3.Distance(aim, from) > Range + 2f) aim = from + (aim - from).normalized * Range;
            // Land on the floor under the aim point (a wall hit drops to its foot).
            if (Physics.Raycast(aim + Vector3.up * 0.5f, Vector3.down, out var ground, 6f, ~0, QueryTriggerInteraction.Ignore)) aim = ground.point;
            ThrowFxRpc(kind, from, aim);
            StartCoroutine(Land(k, aim));
        }

        private IEnumerator Land(Kind k, Vector3 at)
        {
            yield return new WaitForSeconds(Flight);
            float att = progress.AttackMultiplier;
            switch (k)
            {
                case Kind.Fire:
                    for (int tick = 0; tick < 8; tick++)
                    {
                        foreach (var foe in CombatQuery.Circle(at, 3.2f, Team.Enemies).ToArray())
                            foe.ServerDamage(11f * att, at, NetworkObjectId);
                        yield return new WaitForSeconds(0.5f);
                    }
                    break;
                case Kind.Frost:
                    foreach (var foe in CombatQuery.Circle(at, 3.6f, Team.Enemies).ToArray())
                    {
                        foe.ServerDamage(10f * att, at, NetworkObjectId);
                        foe.ServerChill(4f);
                    }
                    break;
                case Kind.Holy:
                    foreach (var foe in CombatQuery.Circle(at, 4f, Team.Enemies).ToArray())
                    {
                        foe.ServerDamage(24f * att, at, NetworkObjectId);
                        if (foe.TryGetComponent<ICrowdControl>(out var cc)) cc.ServerStun(1.8f);
                    }
                    foreach (var ally in CombatQuery.Circle(at, 4f, Team.Players).ToArray()) ally.ServerHeal(ally.Max.Value * 0.2f);
                    break;
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ThrowFxRpc(byte kind, Vector3 from, Vector3 to)
        {
            if (!IsOwner) player.Rig?.Play(RigAction.Cast, 0.3f);
            Sfx.Play(Sound.Swing, from, 0.6f, 1.4f);
            FlaskFlight.Spawn((Kind)kind, from, to, Flight);
        }
    }
}
