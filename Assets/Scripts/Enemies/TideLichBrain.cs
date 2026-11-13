using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Deepbound.World;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.AI;

namespace Deepbound.Enemies
{
    /// <summary>
    /// The Tide Lich: floor 2's boss, and the fight that teaches movement.
    ///
    /// <list type="bullet">
    /// <item><b>Bone Volley</b> — the staff glows, then a fan of five shards flies at its
    ///   target. Answer: sidestep, or block it with a shield.</item>
    /// <item><b>Drowning Pools</b> — a red circle fills under every player at once; anyone
    ///   still standing in theirs when it fills is dragged under. Answer: keep moving —
    ///   and do not stack, or one player's pool catches their friend.</item>
    /// <item><b>Undertow Blink</b> — crowd it for too long and it vanishes and reappears
    ///   across the room. Answer: the party has to chase, so whoever has the mobility
    ///   should commit first.</item>
    /// <item><b>Call of the Deep</b> — at half health it raises archers from the water.</item>
    /// </list>
    /// </summary>
    public class TideLichBrain : BossBrain
    {
        protected override DashStyle Dash => DashStyle.Blink;
        [SerializeField] private float preferredRange = 9f;
        [SerializeField] private float volleyCooldown = 3.6f, volleyWindup = 1.0f, boltDamage = 15f, volleySpread = 40f;
        [SerializeField] private int volleyCount = 5;
        [SerializeField] private float poolCooldown = 9f, poolWindup = 1.8f, poolRadius = 2.6f, poolDamage = 34f;
        [SerializeField] private float blinkCooldown = 7f, crowdRange = 3.5f, crowdTolerance = 1.2f;

        public override string DisplayName => "The Tide Lich";
        public override string Epithet => "Who Drowned the Vaults";
        protected override byte EntranceStyle => 2;
        public override string LastWords => "The water... finally... collects.";
        public override Progression.Deeds.Deed Deed => Progression.Deeds.Deed.TideLich;
        public override string Hint => "Keep moving off the pools it lays, and fight the whirlpool's pull toward the centre.";

        // ---- Maelstrom: a whirlpool opens and drags everyone toward its drowning heart. ----
        private float maelstromUntil = -1f, nextMaelstrom, nextPull;
        private Vector3 maelstromAt;
        private const float MaelstromSeconds = 6.5f, MaelstromRadius = 13f, EyeRadius = 2.2f;

        private void BeginMaelstrom()
        {
            var room = World.DungeonDirector.Layout?.BossRoom;
            maelstromAt = room != null ? room.Center : transform.position;
            maelstromUntil = Time.time + MaelstromSeconds;
            nextMaelstrom = Time.time + 20f;
            castUntil = Time.time + 1f;
            busyUntil = castUntil;
            PlayActionRpc(RigAction.Cast, 1f);
            MaelstromRpc(maelstromAt, MaelstromSeconds);
            Debug.Log("[Boss] Tide Lich maelstrom");
        }

        private void TickMaelstrom()
        {
            if (maelstromUntil < 0f) return;
            if (Time.time >= maelstromUntil) { maelstromUntil = -1f; return; }
            if (Time.time < nextPull) return;
            nextPull = Time.time + 0.3f;
            foreach (var p in Player.PlayerLife.All)
            {
                if (p == null || !p.Targetable) continue;
                var to = maelstromAt - p.transform.position;
                to.y = 0f;
                float d = to.magnitude;
                if (d > MaelstromRadius) continue;
                if (d < EyeRadius) p.Health.ServerDamage(Signature(9f), maelstromAt, NetworkObjectId);
                // Stronger the closer you are; a sideways swirl so it reads as a whirlpool.
                var swirl = Vector3.Cross(Vector3.up, to.normalized) * 1.2f;
                KnockPlayer(p.Health, to.normalized * Mathf.Lerp(4.5f, 2f, d / MaelstromRadius) + swirl);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void MaelstromRpc(Vector3 at, float seconds)
        {
            Telegraph.Show(at, EyeRadius, seconds, null, new Color(0.1f, 0.5f, 0.6f, 0.55f));
            UI.GameHud.Feed("The Tide Lich opens a maelstrom. Fight the pull!", new Color(0.3f, 0.9f, 0.75f));
            Sfx.Play(Sound.Splash, at + Vector3.up, 1f, 0.5f);
            StartCoroutine(MaelstromFx(at, seconds));
        }

        private System.Collections.IEnumerator MaelstromFx(Vector3 at, float seconds)
        {
            var c = new Color(0.25f, 0.85f, 0.75f);
            for (float t = 0f; t < seconds; t += 0.35f)
            {
                Vfx.Ring(at, MaelstromRadius * (1f - (t % 1.05f) / 1.05f) + 1f, c * 0.6f, 0.35f);
                for (int i = 0; i < 4; i++)
                {
                    float a = t * 3f + i * Mathf.PI / 2f;
                    Vfx.Speck(at + new Vector3(Mathf.Cos(a), 0.2f, Mathf.Sin(a)) * (EyeRadius + 1.5f), c);
                }
                if ((int)(t / 0.35f) % 3 == 0) Sfx.Play(Sound.Splash, at, 0.5f, 0.6f);
                yield return new WaitForSeconds(0.35f);
            }
        }

        // Tidal Surge, once it is wounded: a wall of black water rolls out from it.
        protected override float ShockwaveCooldown => health.Fraction < 0.6f ? 14f : 0f;
        protected override Color ShockwaveColor => new Color(0.3f, 0.9f, 0.75f);

        // ---- Phases ----
        protected override float[] PhaseGates => new[] { 0.6f, 0.35f };

        protected override void OnPhaseGate(int index)
        {
            if (index == 0)
            {
                Transition("The Tide Turns", "The water remembers every drowned name. Yours is next.");
                nextWhirl = Time.time + 6f;
            }
            else
            {
                Survive("The Drowning Deep", "Breathe while you still can.", "The spiral turns, reverses and follows the Lich: keep moving through its gaps", 12f, () => Taunt("Still breathing? Then drown slowly."));
                pools.Clear();
                volleyAt = -1f;
                PlayActionRpc(RigAction.Cast, 12f);
                // Two emitters circling the Lich, wheeling one way then the other, with aimed bolts at
                // anyone who stands still: the gaps move, so you have to move with them.
                int arms = scaledPlayers <= 1 ? 3 : 4;
                for (int e = 0; e < 2; e++)
                    StartPattern(new Pattern
                    {
                        Arms = arms, Interval = 0.1f, Speed = SpiralSpeed, Spin = 11f, FlipEvery = 3.2f, Damage = Signature(boltDamage) * 0.5f,
                        Kind = ProjectileKind.Frostbolt, Duration = 11.6f, AimedEvery = e == 0 ? 1.4f : 0f, Range = 30f,
                        Origin = Orbit(2.6f, 45f, e * 180f, 1.15f),
                    });
            }
        }

        /// <summary>
        /// Survival: the Lich glides after the party while its spiral turns, so the pattern itself
        /// travels. Wounded (after the first gate) it also throws short, fast whirls between attacks.
        /// </summary>
        private void TickSpiral()
        {
            if (InSurvival)
            {
                var nearest = NearestPlayer();
                if (nearest != null && agent.enabled && agent.isOnNavMesh && FlatDistance(nearest.position) > 5f)
                    agent.Move((nearest.position - transform.position).normalized * 1.8f * Time.deltaTime);
                return;
            }
            if (nextWhirl > 0f && Time.time >= nextWhirl && !PatternRunning && !Dormant && !Immune)
            {
                nextWhirl = Time.time + Mathf.Lerp(18f, 12f, Difficulty.Lead(scaledDepth));
                PlayActionRpc(RigAction.Cast, 1f);
                StartPattern(new Pattern
                {
                    Arms = 2, Interval = 0.08f, Speed = SpiralSpeed + 1f, Spin = 19f, Damage = Signature(boltDamage) * 0.45f,
                    Kind = ProjectileKind.Frostbolt, Duration = 2.6f, Origin = Orbit(1.6f, 120f, 0f, 1.15f),
                });
            }
        }

        private Transform NearestPlayer()
        {
            Transform best = null;
            float bd = float.MaxValue;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || !p.Targetable) continue;
                float d = (p.transform.position - transform.position).sqrMagnitude;
                if (d < bd) { bd = d; best = p.transform; }
            }
            return best;
        }

        private float nextWhirl = -1f;
        private const float SpiralSpeed = 12.5f;

        // Its volleys already reach; it does not need a quick shot as well.
        protected override ProjectileKind? QuickKind => null;

        private float nextVolley, nextPool, nextBlink;
        private float volleyAt = -1f;
        private float crowdedFor;
        private bool summoned;

        private struct Pool { public Vector3 Center; public float At; }
        private readonly List<Pool> pools = new List<Pool>();

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            nextVolley = Time.time + 2.5f;
            nextPool = Time.time + 7f;
        }

        protected override void Update()
        {
            if (HeldByStun()) return;
            if (IsServer && !IsDead) TickSpiral();
            if (IsServer && !IsDead && !InSurvival) TickMaelstrom();
            if (IsServer && !IsDead)
            {
                ResolvePools();

                // Measured every frame, not only when choosing an attack: the Lich's own
                // swings and casts would otherwise pause the clock while it is crowded.
                bool crowded = target != null && target.IsAlive && FlatDistance(target.transform.position) < crowdRange;
                crowdedFor = crowded ? crowdedFor + Time.deltaTime : 0f;

                if (volleyAt > 0f)
                {
                    agent.isStopped = true;
                    FaceTarget(0.25f);
                    if (Time.time >= volleyAt) FireVolley();
                    return;
                }
            }
            base.Update();
        }

        protected override bool ChooseSpecialAttack(float distanceToTarget)
        {
            agent.stoppingDistance = preferredRange;
            if (TryShockwave()) return true;

            if (maelstromUntil < 0f && health.Fraction < 0.6f && Time.time >= nextMaelstrom)
            {
                BeginMaelstrom();
                return true;
            }

            if (!summoned && health.Fraction < 0.5f)
            {
                summoned = true;
                Summon(1 + scaledPlayers, 5f, 1.8f);
                return true;
            }

            // Crowded: tolerate it briefly, then blink away.
            if (crowdedFor > crowdTolerance && Time.time >= nextBlink)
            {
                Blink();
                return true;
            }

            if (Time.time >= nextPool)
            {
                CastPools();
                return true;
            }

            if (Time.time >= nextVolley && CombatQuery.ClearLine(transform.position + Vector3.up * 1.8f, target))
            {
                volleyAt = Time.time + volleyWindup;
                nextVolley = Time.time + volleyCooldown;
                busyUntil = volleyAt + 0.5f;
                PlayActionRpc(RigAction.Cast, volleyWindup + 0.2f);
                return true;
            }

            return false;
        }

        protected override void OnBroken() => volleyAt = -1f;

        private void FireVolley()
        {
            volleyAt = -1f;
            if (target == null) return;

            var origin = transform.position + Vector3.up * 2.2f + transform.forward * 0.6f;
            var aim = AimDir(origin, Arrows.Speed);
            for (int i = 0; i < volleyCount; i++)
            {
                float a = Mathf.Lerp(-volleySpread / 2f, volleySpread / 2f, volleyCount > 1 ? i / (float)(volleyCount - 1) : 0.5f);
                Arrows.ServerFire(origin, Quaternion.Euler(0f, a, 0f) * aim, Signature(boltDamage), Team.Players, NetworkObjectId);
            }
            VolleyRpc(origin, aim, volleyCount, volleySpread);
            Debug.Log("[Boss] Tide Lich volley");
        }

        private void CastPools()
        {
            nextPool = Time.time + poolCooldown;
            castUntil = Time.time + 0.9f;
            busyUntil = castUntil;
            PlayActionRpc(RigAction.Cast, 0.9f);

            foreach (var p in PlayerLife.All)
            {
                if (p == null || !p.Targetable) continue;
                float pw = Tell(poolWindup);
                var c = PredictedFeet(p, pw);
                pools.Add(new Pool { Center = c, At = Time.time + pw });
                Debug.Log("[Boss] Tide Lich pool under " + p.name);
                CircleTelegraphRpc(c, poolRadius, pw, false);
            }
        }

        private void ResolvePools()
        {
            for (int i = pools.Count - 1; i >= 0; i--)
            {
                if (Time.time < pools[i].At) continue;
                var c = pools[i].Center;
                foreach (var victim in CombatQuery.Circle(c, poolRadius, Team.Players))
                {
                    victim.ServerDamage(Signature(poolDamage), c + Vector3.up, NetworkObjectId);
                }
                PoolFxRpc(c);
                pools.RemoveAt(i);
            }
        }

        private void Blink()
        {
            nextBlink = Time.time + blinkCooldown;
            crowdedFor = 0f;

            var room = DungeonDirector.Layout?.BossRoom;
            if (room == null) return;

            // Of a handful of candidate spots in the room, take the one farthest from every player.
            var rng = new System.Random((int)(Time.time * 1000f));
            Vector3 best = transform.position;
            float bestScore = -1f;
            for (int i = 0; i < 10; i++)
            {
                var p = room.Center + new Vector3((float)(rng.NextDouble() - 0.5) * (room.Size.x - 6f), 0f, (float)(rng.NextDouble() - 0.5) * (room.Size.y - 6f));
                if (!NavMesh.SamplePosition(p, out var hit, 2f, NavMesh.AllAreas)) continue;
                float nearest = float.MaxValue;
                foreach (var pl in PlayerLife.All) nearest = Mathf.Min(nearest, Vector3.Distance(pl.transform.position, hit.position));
                if (nearest > bestScore) { bestScore = nearest; best = hit.position; }
            }

            var from = transform.position;
            agent.Warp(best);
            // Teleport, not interpolate: clients should see it vanish and reappear.
            GetComponent<NetworkTransform>().Teleport(best, transform.rotation, transform.localScale);
            BlinkFxRpc(from, best);
            Debug.Log("[Boss] Tide Lich blinked " + Vector3.Distance(from, best).ToString("F1") + "m");
            busyUntil = Time.time + 0.6f;
        }

        public override void ServerStun(float seconds)
        {
            // A bash interrupts the volley.
            volleyAt = -1f;
            base.ServerStun(Mathf.Min(seconds, 0.6f));
        }

        // ------------------------------------------------------------------

        [Rpc(SendTo.ClientsAndHost)]
        private void VolleyRpc(Vector3 origin, Vector3 aim, int count, float spread)
        {
            for (int i = 0; i < count; i++)
            {
                float a = Mathf.Lerp(-spread / 2f, spread / 2f, count > 1 ? i / (float)(count - 1) : 0.5f);
                Arrows.SpawnVisual(origin, Quaternion.Euler(0f, a, 0f) * aim);
            }
            Sfx.Play(Sound.Bow, origin, 1f, 0.7f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PoolFxRpc(Vector3 at)
        {
            Sfx.Play(Sound.Slam, at, 0.7f, 1.4f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BlinkFxRpc(Vector3 from, Vector3 to)
        {
            Sfx.Play(Sound.Summon, from + Vector3.up, 0.6f, 1.6f);
            Sfx.Play(Sound.Summon, to + Vector3.up, 0.6f, 1.6f);
            Telegraph.Show(from, 1.2f, 0.35f, null);
        }
    }
}
