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
            if (keyboard == null || !UI.Binds.Down(UI.Bind.Kick) || Time.time < ReadyAt || !life.CanAct || !motor.ActionsReady) return;
            ReadyAt = Time.time + Cooldown;
            bool slide = motor.IsSliding;
            motor.AddImpulse(transform.forward * (slide ? 1f : 3f));
            player.ConfirmHit(slide ? 1.2f : 0.6f);
            KickRpc(player.AimOrigin, transform.forward, slide);
        }

        [Rpc(SendTo.Server)]
        private void KickRpc(Vector3 origin, Vector3 forward, bool slide)
        {
            if (!life.CanAct || Time.time - lastServerKick < Cooldown * 0.8f) return;
            lastServerKick = Time.time;
            if (Vector3.Distance(origin, transform.position + Vector3.up * 1.5f) > 2f) origin = transform.position + Vector3.up * 1.5f;
            forward.y = 0f;
            forward.Normalize();
            bool hit = false;
            // A slide kick sweeps low and wide, and hits like a cart.
            var from = slide ? transform.position + Vector3.up * 0.6f : origin;
            foreach (var target in CombatQuery.Arc(from, forward, Range + (slide ? 0.6f : 0f), slide ? 110f : Arc, Team.Enemies))
            {
                target.ServerDamage((slide ? 20f : 8f) * progress.AttackMultiplier, transform.position, NetworkObjectId);
                if (target.TryGetComponent<ICrowdControl>(out var cc))
                {
                    var push = target.transform.position - transform.position;
                    push.y = 0f;
                    var v = (push.normalized + forward).normalized * (slide ? Shove * 1.5f : Shove);
                    cc.ServerStun(slide ? 1f : 0.45f);
                    // Shielded rank and file: the boot knocks the shield wide open.
                    if (target.TryGetComponent<Enemies.ShieldbearerBrain>(out var sb)) sb.ServerBreakGuard(slide ? 5f : 3.5f);
                    cc.ServerKnockback(v);
                    World.DungeonDirector.Instance?.ServerCheckShove(target, v, NetworkObjectId);
                }
                hit = true;
            }
            KickFxRpc(hit, slide);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void KickFxRpc(bool hit, bool slide)
        {
            if (hit && slide) UI.GameHud.ShowCombo(transform.position + transform.forward * 1.6f + Vector3.up * 1.4f, "SLIDE KICK", new Color(1f, 0.8f, 0.4f));
            if (!IsOwner) player.Rig?.Play(RigAction.Bash, 0.35f);
            Sfx.Play(hit ? Sound.Bash : Sound.Swing, transform.position + transform.forward + Vector3.up, 0.8f, hit ? 0.8f : 1.3f);
            if (hit) Vfx.Smoke(transform.position + transform.forward * 1.6f, 1, 0.4f, 0.8f);
        }
    }
}
