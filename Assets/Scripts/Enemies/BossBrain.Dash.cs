using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deepbound.Enemies
{
    /// <summary>
    /// How a guardian closes in, and how it punishes those who hug it (2026-11-13, the user: "Dash variants per boss. The
    /// Gravewarden shield-charges (blockable from the front), the Lich blinks behind you, the Cardinal leaps and slams" and
    /// "add an outer ring attack that punishes hugging, so every class has to move").
    /// <list type="bullet">
    /// <item><see cref="DashStyle.Charge"/>: a red line, then a charge along it (Wardens, the Hollow King, the Unsung).</item>
    /// <item><see cref="DashStyle.ShieldCharge"/>: the same, shield first; a Knight with the shield up in front of it stops
    /// it dead, takes nothing and staggers it (<see cref="ShieldStopStagger"/>).</item>
    /// <item><see cref="DashStyle.Blink"/>: frost gathers at its feet and behind its target, then it is THERE, and a
    /// frost nova follows (<see cref="BlinkNovaDelay"/>).</item>
    /// <item><see cref="DashStyle.Leap"/>: a circle at where its target is going, then it comes down on it.</item>
    /// </list>
    /// REPULSE: stand within <see cref="HugRange"/> of it for <see cref="HugSeconds"/> and a ring telegraph fills round it,
    /// then everything inside <see cref="RepulseRadius"/> is struck and hurled out. Ranged classes get the dash, melee the ring.
    /// All of it runs here in LateUpdate (subclasses return early from Update mid-attack; the old charge sat still for that).
    /// </summary>
    public abstract partial class BossBrain
    {
        public enum DashStyle : byte { Charge, ShieldCharge, Blink, Leap }
        protected virtual DashStyle Dash => DashStyle.Charge;
        public DashStyle DashKind => Dash;

        public const float ShieldStopStagger = 0.3f, BlinkNovaDelay = 0.55f, BlinkNovaRadius = 3.4f, LeapRadius = 3.2f, LeapAir = 0.75f;
        public const float HugRange = 3.6f, HugSeconds = 3f, RepulseRadius = 4.8f, RepulseTell = 1.1f, RepulseCooldown = 13f, RepulseDamage = 1.3f;

        private enum DashPhase { None, Tell, Moving, Nova }
        private DashPhase dashPhase;
        private float dashPhaseAt, dashLength, dashTravelled;
        private Vector3 dashDir, dashFrom, dashTo;
        private readonly System.Collections.Generic.HashSet<Health> dashHit = new System.Collections.Generic.HashSet<Health>();
        private float hugSince = -1f, repulseReadyAt, repulseAt = -1f;
        /// <summary>Server, tests: shield charges a raised shield has stopped; repulses made.</summary>
        public int ShieldStops { get; private set; }
        public int Repulses { get; private set; }

        public bool Dashing => dashPhase != DashPhase.None;

        public override bool ServerBossDash()
        {
            if (target == null) target = PickTarget();
            if (target == null || !agent.enabled || !agent.isOnNavMesh || IsStunned || Dashing) return false;
            dashFrom = transform.position;
            dashDir = target.transform.position - transform.position;
            dashDir.y = 0f;
            if (dashDir.sqrMagnitude < 0.01f) return false;
            float dist = dashDir.magnitude;
            dashDir /= dist;
            transform.rotation = Quaternion.LookRotation(dashDir);
            agent.isStopped = true;
            attacking = false;
            dashHit.Clear();
            dashTravelled = 0f;
            float tell = Tell(BossDashTell);
            bossDashReadyAt = Time.time + BossDashCooldown;
            BossDashCount++;
            switch (Dash)
            {
                case DashStyle.Blink:
                {
                    var tp = target.GetComponent<PlayerLife>();
                    var behind = target.transform.position - target.transform.forward * 2.2f;
                    if (NavMesh.SamplePosition(behind, out var bh, 2.5f, NavMesh.AllAreas)) behind = bh.position;
                    dashTo = behind;
                    tell = Tell(0.65f);
                    CircleTelegraphRpc(dashTo, BlinkNovaRadius, tell + BlinkNovaDelay, false);
                    BlinkTellRpc(transform.position, dashTo, tell);
                    break;
                }
                case DashStyle.Leap:
                {
                    var tp = target.GetComponent<PlayerLife>();
                    var land = tp != null ? PredictedFeet(tp, tell + LeapAir) : target.transform.position;
                    if (NavMesh.SamplePosition(land, out var lh, 3f, NavMesh.AllAreas)) land = lh.position;
                    dashTo = land;
                    tell = Tell(0.9f);
                    CircleTelegraphRpc(dashTo, LeapRadius, tell + LeapAir, false);
                    break;
                }
                default:
                    dashLength = Mathf.Clamp(dist - 1.2f, 3f, BossDashMax);
                    FreeLineTelegraphRpc(transform.position, dashDir, dashLength + 1.5f, 1.4f, tell);
                    break;
            }
            dashPhase = DashPhase.Tell;
            dashPhaseAt = Time.time + tell;
            busyUntil = Time.time + tell + 2.2f;
            PlayActionRpc(RigAction.Windup, tell);
            BossDashRoarRpc(transform.position, (byte)Dash);
            return true;
        }

        /// <summary>Server, tests: dashes begun (any style).</summary>
        public int BossDashCount { get; private set; }

        /// <summary>Server, from LateUpdate.</summary>
        private void TickDash()
        {
            if (dashPhase == DashPhase.None) return;
            if (IsStunned || IsDead) { EndDash(); return; }
            if (dashPhase == DashPhase.Tell)
            {
                if (Time.time < dashPhaseAt) return;
                dashPhase = Dash == DashStyle.Blink ? DashPhase.Nova : DashPhase.Moving;
                dashPhaseAt = Time.time;
                if (Dash == DashStyle.Blink)
                {
                    agent.Warp(dashTo);
                    var face = target != null ? target.transform.position - dashTo : dashDir;
                    face.y = 0f;
                    if (face.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(face);
                    BlinkArriveRpc(dashTo);
                    dashPhaseAt = Time.time + BlinkNovaDelay;
                }
                return;
            }
            if (dashPhase == DashPhase.Nova)
            {
                if (Time.time < dashPhaseAt) return;
                foreach (var v in CombatQuery.Circle(transform.position + Vector3.up * 0.9f, BlinkNovaRadius, Team.Players))
                {
                    v.ServerDamage(Signature(damage * 1.1f), transform.position, NetworkObjectId);
                    v.GetComponent<PlayerDebuffs>()?.ServerFrostbite(2.5f);
                }
                NovaFxRpc(transform.position);
                EndDash();
                return;
            }
            // Moving.
            if (Dash == DashStyle.Leap)
            {
                float k = Mathf.Clamp01((Time.time - dashPhaseAt) / LeapAir);
                var p = Vector3.Lerp(dashFrom, dashTo, k);
                agent.Warp(new Vector3(p.x, Mathf.Lerp(dashFrom.y, dashTo.y, k), p.z));
                if (Rig != null) Rig.transform.localPosition = Vector3.up * Mathf.Sin(k * Mathf.PI) * 3.2f;
                LeapHeightRpcIfNeeded(k);
                if (k >= 1f)
                {
                    if (Rig != null) Rig.transform.localPosition = Vector3.zero;
                    foreach (var v in CombatQuery.Circle(dashTo + Vector3.up * 0.9f, LeapRadius, Team.Players))
                    {
                        v.ServerDamage(Signature(damage * 1.5f), dashTo, NetworkObjectId);
                        var away = v.transform.position - dashTo; away.y = 0f;
                        if (v.TryGetComponent<KnockbackReceiver>(out var kb)) kb.KnockbackRpc(away.normalized * 8f + Vector3.up * 3f);
                    }
                    LeapLandRpc(dashTo);
                    EndDash();
                }
                return;
            }
            float step = BossDashSpeed * Time.deltaTime;
            var before = transform.position;
            agent.Move(dashDir * step);
            float moved = Vector3.Distance(before, transform.position);
            dashTravelled += moved;
            foreach (var victim in CombatQuery.Circle(transform.position + Vector3.up * 0.9f, 1.3f, Team.Players))
            {
                if (!dashHit.Add(victim)) continue;
                if (Dash == DashStyle.ShieldCharge && victim.TryGetComponent<KnightKit>(out var knight) && knight.ShieldRaised && Vector3.Dot(victim.transform.forward, -dashDir) > 0.35f)
                {
                    // Stopped on a raised shield: nothing gets through, and the guardian reels.
                    ShieldStops++;
                    ServerAddStagger(ShieldStopStagger);
                    ShieldStopRpc(victim.transform.position + Vector3.up * 1.2f);
                    EndDash();
                    return;
                }
                victim.ServerDamage(damage * 0.9f, transform.position - dashDir, NetworkObjectId);
                if (victim.TryGetComponent<KnockbackReceiver>(out var dashKb)) dashKb.KnockbackRpc(dashDir * 7f + Vector3.up * 2.5f);
            }
            if (moved < step * 0.3f || dashTravelled >= dashLength + 1f || (dashHit.Count > 0 && Dash == DashStyle.Charge))
            {
                PlayActionRpc(RigAction.Swing, 0.3f);
                EndDash();
            }
        }

        private int leapSent = -1;
        private void LeapHeightRpcIfNeeded(float k)
        {
            int step = Mathf.FloorToInt(k * 6f);
            if (step == leapSent) return;
            leapSent = step;
            LeapArcRpc(dashFrom, dashTo, LeapAir * (1f - k), k);
        }

        private void EndDash()
        {
            dashPhase = DashPhase.None;
            leapSent = -1;
            if (Rig != null && Dash == DashStyle.Leap) Rig.transform.localPosition = Vector3.zero;
            if (agent != null && agent.enabled && agent.isOnNavMesh) agent.isStopped = false;
            busyUntil = Mathf.Min(busyUntil, Time.time + 0.5f);
        }

        // ---------------------------------------------------------------- REPULSE

        private void TickRepulse()
        {
            if (Dormant || Immune || InEntrance || IsStunned) { hugSince = -1f; return; }
            if (repulseAt > 0f)
            {
                if (Time.time < repulseAt) return;
                repulseAt = -1f;
                foreach (var v in CombatQuery.Circle(transform.position + Vector3.up * 0.9f, RepulseRadius, Team.Players))
                {
                    v.ServerDamage(Signature(damage * RepulseDamage), transform.position, NetworkObjectId);
                    var away = v.transform.position - transform.position; away.y = 0f;
                    if (v.TryGetComponent<KnockbackReceiver>(out var kb)) kb.KnockbackRpc(away.normalized * 11f + Vector3.up * 3.5f);
                }
                Repulses++;
                RepulseFxRpc(transform.position);
                return;
            }
            bool hugged = false;
            foreach (var p in PlayerLife.All)
                if (p != null && !p.Downed.Value && FlatDistance(p.transform.position) < HugRange) { hugged = true; break; }
            if (!hugged) { hugSince = -1f; return; }
            if (hugSince < 0f) hugSince = Time.time;
            if (Time.time - hugSince < HugSeconds || Time.time < repulseReadyAt || Dashing || Casting) return;
            ServerRepulse();
        }

        /// <summary>Server: the ring fills round it, then it bursts (tests call this).</summary>
        public void ServerRepulse()
        {
            float tell = Tell(RepulseTell);
            repulseAt = Time.time + tell;
            repulseReadyAt = Time.time + RepulseCooldown;
            hugSince = -1f;
            CircleTelegraphRpc(transform.position, RepulseRadius, tell, false);
            PlayActionRpc(RigAction.Cast, tell);
            RepulseTellRpc(transform.position, tell);
        }

        // ---------------------------------------------------------------- effects

        [Rpc(SendTo.ClientsAndHost)]
        private void BossDashRoarRpc(Vector3 at, byte style)
        {
            Sfx.Play(style == (byte)DashStyle.Blink ? Sound.Frost : Sound.Roar, at + Vector3.up * 1.5f, 0.9f, style == (byte)DashStyle.Leap ? 0.85f : 1.1f);
            Vfx.Smoke(at, 3, 0.5f, 0.7f);
            if (style == (byte)DashStyle.ShieldCharge) UI.Tips.OfferNow("shieldcharge", "THE GRAVEWARDEN CHARGES SHIELD FIRST: get out of the line - or, as a Knight, meet it with your shield up and stop it dead.");
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BlinkTellRpc(Vector3 from, Vector3 to, float seconds)
        {
            Vfx.Ring(from + Vector3.up * 0.05f, 1.4f, new Color(0.5f, 0.8f, 1f), seconds);
            Vfx.Embers(to + Vector3.up * 0.6f, 14, new Color(0.6f, 0.85f, 1f), 1.5f, 0.06f, seconds);
            Sfx.Play(Sound.Whisper, to, 0.8f, 1.3f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BlinkArriveRpc(Vector3 at)
        {
            Vfx.Flash(at + Vector3.up * 1.2f, new Color(0.6f, 0.85f, 1f), 2f, 6f, 0.25f);
            Sfx.Play(Sound.Frost, at, 1f, 0.8f);
            UI.Tips.OfferNow("blink", "THE LICH BLINKS BEHIND YOU and a frost nova follows: when the cold gathers at your back, move.");
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void NovaFxRpc(Vector3 at)
        {
            Vfx.Ring(at + Vector3.up * 0.05f, BlinkNovaRadius, new Color(0.55f, 0.85f, 1f), 0.5f);
            Vfx.Embers(at + Vector3.up, 18, new Color(0.7f, 0.9f, 1f), 4f, 0.07f, 0.8f);
            Sfx.Play(Sound.Shatter, at, 1f, 0.9f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void LeapArcRpc(Vector3 from, Vector3 to, float left, float k)
        {
            if (IsServer || Rig == null) return;
            // Clients see the arc from the same curve (the root moves by the network transform).
            Rig.transform.localPosition = Vector3.up * Mathf.Sin(k * Mathf.PI) * 3.2f;
            if (left <= 0.05f) Rig.transform.localPosition = Vector3.zero;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void LeapLandRpc(Vector3 at)
        {
            if (Rig != null) Rig.transform.localPosition = Vector3.zero;
            Vfx.Ring(at + Vector3.up * 0.05f, LeapRadius, new Color(1f, 0.85f, 0.5f), 0.5f);
            Vfx.Smoke(at, 6, 1f, 1f);
            FxSprite.Scorch(at + Vector3.up * 0.02f, 1.8f);
            Sfx.Play(Sound.Slam, at, 1f, 0.75f);
            CameraShakeNear(at);
        }

        private static void CameraShakeNear(Vector3 at)
        {
            var me = NetworkPlayer.Local;
            if (me != null && (me.transform.position - at).sqrMagnitude < 15f * 15f) Sfx.Play2D(Sound.Slam, 0.3f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ShieldStopRpc(Vector3 at)
        {
            Sfx.Play(Sound.Block, at, 1f, 0.7f);
            Sfx.Play(Sound.Slam, at, 0.8f, 0.9f);
            Vfx.HitImpact(at, Vector3.up, 1f, new Color(1f, 0.85f, 0.5f));
            UI.GameHud.ShowCombo(at + Vector3.up * 0.6f, "SHIELD WALL!", new Color(1f, 0.85f, 0.45f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RepulseTellRpc(Vector3 at, float seconds)
        {
            Vfx.Embers(at + Vector3.up, 16, new Color(1f, 0.5f, 0.3f), 1.5f, 0.06f, seconds);
            Sfx.Play(Sound.Whisper, at, 0.9f, 0.7f);
            UI.Tips.OfferNow("repulse", "HUG A GUARDIAN TOO LONG and it REPULSES: a ring fills round it, then everything inside is struck and thrown. When the ring comes, get out of it.");
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RepulseFxRpc(Vector3 at)
        {
            Vfx.Ring(at + Vector3.up * 0.05f, RepulseRadius, new Color(1f, 0.55f, 0.3f), 0.5f);
            Vfx.Smoke(at, 8, RepulseRadius * 0.5f, 0.9f);
            Sfx.Play(Sound.Slam, at, 1f, 0.65f);
        }
    }
}
