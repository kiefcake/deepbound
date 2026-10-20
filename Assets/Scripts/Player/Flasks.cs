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
    /// The potion pouch, for every class: up to <see cref="Max"/> of each kind. Health and mana
    /// potions are drunk; frost and holy flasks are thrown at the crosshair. Potions drop from the
    /// dead, come out of chests, and are sold by the merchant. There is no passive regeneration:
    /// the pouch is how you heal between fights.
    ///
    /// <list type="bullet">
    /// <item><b>1 — Health Potion.</b> Mends 45% of your health over 3 seconds.</item>
    /// <item><b>2 — Mana Potion.</b> Refills 70% of your class resource (mana, stamina, focus, energy).</item>
    /// <item><b>3 — Frost Flask.</b> Thrown: chills everything it touches (Shatter!), freezes what stands in water.</item>
    /// <item><b>4 — Holy Water.</b> Thrown: the dead reel (stunned) and burn; allies in the splash are cleansed and mended.</item>
    /// </list>
    /// </summary>
    public class Flasks : NetworkBehaviour
    {
        public enum Kind : byte { Health, Mana, Frost, Holy }
        public static readonly Kind[] All = { Kind.Health, Kind.Mana, Kind.Frost, Kind.Holy };
        public const int Max = 5;
        public const float HealFraction = 0.45f, HealSeconds = 3f, ManaFraction = 0.7f;
        private const float Range = 16f, Flight = 0.55f, ThrowCooldown = 0.8f, DrinkCooldown = 1.2f;

        public readonly NetworkVariable<byte> Health = new NetworkVariable<byte>();
        public readonly NetworkVariable<byte> Mana = new NetworkVariable<byte>();
        public readonly NetworkVariable<byte> Frost = new NetworkVariable<byte>();
        public readonly NetworkVariable<byte> Holy = new NetworkVariable<byte>();

        private PlayerLife life;
        private FirstPersonMotor motor;
        private NetworkPlayer player;
        private PlayerProgress progress;
        private float readyAt;
        private float healingUntil;

        public static Color ColorOf(Kind k) => k == Kind.Health ? new Color(0.95f, 0.2f, 0.2f) : k == Kind.Mana ? new Color(0.3f, 0.5f, 1f)
            : k == Kind.Frost ? new Color(0.55f, 0.85f, 1f) : new Color(1f, 0.92f, 0.6f);
        public static string NameOf(Kind k) => k == Kind.Health ? "Health" : k == Kind.Mana ? "Mana" : k == Kind.Frost ? "Frost" : "Holy";
        public static string FullName(Kind k) => k == Kind.Health ? "Health Potion" : k == Kind.Mana ? "Mana Potion" : k == Kind.Frost ? "Frost Flask" : "Holy Water";
        public static bool Drunk(Kind k) => k == Kind.Health || k == Kind.Mana;

        private NetworkVariable<byte> Slot(Kind k) => k == Kind.Health ? Health : k == Kind.Mana ? Mana : k == Kind.Frost ? Frost : Holy;
        public int Count(Kind k) => Slot(k).Value;
        public bool IsFull(Kind k) => Count(k) >= Max;

        /// <summary>Is a health potion mending you right now? (HUD.)</summary>
        public bool Healing => Time.time < healingUntil;

        private void Awake()
        {
            life = GetComponent<PlayerLife>();
            motor = GetComponent<FirstPersonMotor>();
            player = GetComponent<NetworkPlayer>();
            progress = GetComponent<PlayerProgress>();
        }

        /// <summary>Server: put potions in the pouch. Returns how many fit (the rest stay where they were).</summary>
        public int ServerAdd(Kind k, int count)
        {
            if (!IsServer || count <= 0) return 0;
            var slot = Slot(k);
            int fit = Mathf.Min(count, Max - slot.Value);
            if (fit <= 0) return 0;
            slot.Value = (byte)(slot.Value + fit);
            AddedRpc((byte)k, (byte)slot.Value);
            return fit;
        }

        [Rpc(SendTo.Owner)]
        private void AddedRpc(byte kind, byte now)
        {
            var k = (Kind)kind;
            UI.GameHud.Feed("+1 " + FullName(k) + "  (" + now + " / " + Max + ")", ColorOf(k));
            Sfx.Play2D(Sound.Potion, 0.5f);
        }

        /// <summary>Server: a new run's pouch: two health potions, one of everything else.</summary>
        public void ServerStartingPouch()
        {
            Health.Value = 2; Mana.Value = 1; Frost.Value = 1; Holy.Value = 1;
        }

        /// <summary>Server: run over, the pouch empties with everything else.</summary>
        public void ServerReset() { Health.Value = 0; Mana.Value = 0; Frost.Value = 0; Holy.Value = 0; }

        private void Update()
        {
            if (!IsOwner) return;
            var kb = Keyboard.current;
            if (kb == null || Time.time < readyAt || !life.CanAct || !motor.ActionsReady) return;
            Kind? k = kb.digit1Key.wasPressedThisFrame ? Kind.Health : kb.digit2Key.wasPressedThisFrame ? Kind.Mana
                : kb.digit3Key.wasPressedThisFrame ? Kind.Frost : kb.digit4Key.wasPressedThisFrame ? Kind.Holy : (Kind?)null;
            // Number keys pick a class at camp before a run; there the pouch stays shut.
            if (k == null || (World.DungeonDirector.InLobby && PlayerProgress.ClassChangeAllowed)) return;
            if (Count(k.Value) <= 0) { UI.GameHud.Feed("No " + FullName(k.Value) + " in your pouch", new Color(0.8f, 0.6f, 0.5f)); readyAt = Time.time + 0.3f; return; }
            if (k == Kind.Health && life.Health.Current.Value >= life.Health.Max.Value - 0.5f) { UI.GameHud.Feed("You are already at full health", new Color(0.8f, 0.7f, 0.6f)); readyAt = Time.time + 0.3f; return; }
            var kit = player.ActiveKit;
            if (k == Kind.Mana && kit != null && kit.Resource >= kit.ResourceMax - 0.5f) { UI.GameHud.Feed("Your " + kit.ResourceName.ToLowerInvariant() + " is already full", new Color(0.6f, 0.7f, 0.9f)); readyAt = Time.time + 0.3f; return; }

            if (Drunk(k.Value))
            {
                readyAt = Time.time + DrinkCooldown;
                DrinkRpc((byte)k.Value);
                return;
            }
            readyAt = Time.time + ThrowCooldown;
            var origin = player.AimOrigin;
            var dir = player.AimForward;
            Vector3 aim;
            if (Physics.Raycast(origin, dir, out var hit, Range, ~0, QueryTriggerInteraction.Ignore)) aim = hit.point;
            else aim = Physics.Raycast(origin + dir * Range, Vector3.down, out var down, 30f, ~0, QueryTriggerInteraction.Ignore) ? down.point : origin + dir * Range;
            ThrowRpc((byte)k.Value, aim);
        }

        // ---- Drinking ----

        [Rpc(SendTo.Server)]
        private void DrinkRpc(byte kind)
        {
            var k = (Kind)kind;
            if (!life.CanAct || Count(k) <= 0 || !Drunk(k)) return;
            Slot(k).Value--;
            if (k == Kind.Health) StartCoroutine(Mend());
            else ManaOwnerRpc();
            DrinkFxRpc(kind);
        }

        /// <summary>Server: the health potion's mend, a little at a time (going down ends it).</summary>
        private IEnumerator Mend()
        {
            const int ticks = 12;
            float each = life.Health.Max.Value * HealFraction / ticks;
            healingUntil = Time.time + HealSeconds;
            for (int i = 0; i < ticks; i++)
            {
                if (life.Downed.Value) yield break;
                life.Health.ServerHeal(each);
                yield return new WaitForSeconds(HealSeconds / ticks);
            }
        }

        [Rpc(SendTo.Owner)]
        private void ManaOwnerRpc() => player.ActiveKit?.RestoreResource(ManaFraction);

        [Rpc(SendTo.ClientsAndHost)]
        private void DrinkFxRpc(byte kind)
        {
            var k = (Kind)kind;
            Sfx.Play(Sound.Drink, transform.position + Vector3.up * 1.5f, 0.8f);
            Vfx.Embers(transform.position + Vector3.up * 1.2f, 8, ColorOf(k), 1.5f, 0.05f, 0.9f);
            if (k == Kind.Health) healingUntil = Time.time + HealSeconds;
            if (IsOwner) player.ViewModelCast(false);
        }

        /// <summary>Tests: drink as the owner would (host only).</summary>
        public void ServerDebugDrink(Kind k) => DrinkRpc((byte)k);

        // ---- Throwing ----

        /// <summary>Tests: throw as the owner would (host only).</summary>
        public void ServerDebugThrow(Kind k, Vector3 at)
        {
            if (Count(k) <= 0) ServerAdd(k, 1);
            ThrowRpc((byte)k, at);
        }

        [Rpc(SendTo.Server)]
        private void ThrowRpc(byte kind, Vector3 aim)
        {
            var k = (Kind)kind;
            if (!life.CanAct || Count(k) <= 0 || Drunk(k)) return;
            Slot(k).Value--;
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
                    foreach (var ally in CombatQuery.Circle(at, 4f, Team.Players).ToArray())
                    {
                        if (ally.TryGetComponent<PlayerDebuffs>(out var deb)) deb.ServerCleanse();
                        ally.ServerHeal(ally.Max.Value * 0.2f);
                    }
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
