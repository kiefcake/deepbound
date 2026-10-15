using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Deepbound.Player
{
    /// <summary>
    /// V: a boot to whatever is in front of you, for every class. Little damage, a lot of
    /// shove, and a stagger. Its real use is the room: kick the dead into a chasm (they do
    /// not climb back out), into a swinging pendulum, or off a balcony.
    /// </summary>
    public class Kick : NetworkBehaviour
    {
        public const float Cooldown = 5f, Range = 2.4f, Arc = 80f, Shove = 15f;

        private PlayerLife life;
        private FirstPersonMotor motor;
        private NetworkPlayer player;
        private PlayerProgress progress;
        public float ReadyAt { get; private set; }
        private float lastServerKick;

        private void Awake()
        {
            life = GetComponent<PlayerLife>();
            motor = GetComponent<FirstPersonMotor>();
            player = GetComponent<NetworkPlayer>();
            progress = GetComponent<PlayerProgress>();
        }

        private void Update()
        {
            if (!IsOwner) return;
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.vKey.wasPressedThisFrame || Time.time < ReadyAt || !life.CanAct || !motor.HasControl) return;
            ReadyAt = Time.time + Cooldown;
            motor.AddImpulse(transform.forward * 3f);
            player.ConfirmHit(0.6f);
            KickRpc(player.AimOrigin, transform.forward);
        }

        [Rpc(SendTo.Server)]
        private void KickRpc(Vector3 origin, Vector3 forward)
        {
            if (!life.CanAct || Time.time - lastServerKick < Cooldown * 0.8f) return;
            lastServerKick = Time.time;
            if (Vector3.Distance(origin, transform.position + Vector3.up * 1.5f) > 2f) origin = transform.position + Vector3.up * 1.5f;
            forward.y = 0f;
            forward.Normalize();
            bool hit = false;
            foreach (var target in CombatQuery.Arc(origin, forward, Range, Arc, Team.Enemies))
            {
                target.ServerDamage(8f * progress.AttackMultiplier, transform.position, NetworkObjectId);
                if (target.TryGetComponent<ICrowdControl>(out var cc))
                {
                    var push = target.transform.position - transform.position;
                    push.y = 0f;
                    var v = (push.normalized + forward).normalized * Shove;
                    cc.ServerStun(0.45f);
                    cc.ServerKnockback(v);
                    World.DungeonDirector.Instance?.ServerCheckShove(target, v, NetworkObjectId);
                }
                hit = true;
            }
            KickFxRpc(hit);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void KickFxRpc(bool hit)
        {
            if (!IsOwner) player.Rig?.Play(RigAction.Bash, 0.35f);
            Sfx.Play(hit ? Sound.Bash : Sound.Swing, transform.position + transform.forward + Vector3.up, 0.8f, hit ? 0.8f : 1.3f);
            if (hit) Vfx.Smoke(transform.position + transform.forward * 1.6f, 1, 0.4f, 0.8f);
        }
    }
}
