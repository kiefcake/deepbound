using Deepbound.Art;
using Deepbound.Combat;
using UnityEngine;
using Unity.Netcode;

namespace Deepbound.Enemies
{
    /// <summary>
    /// Ossric, the Hollow King: the final boss, on floor 3.
    ///
    /// <list type="bullet">
    /// <item><b>Royal Cleave</b> — slow, very wide melee swing. Answer: block it, or step out.</item>
    /// <item><b>Grave Slam</b> — a red circle fills on the floor around him; when it is full,
    ///   everything inside takes heavy damage and is thrown back. Answer: get out of the
    ///   circle, or put a pillar between you. Shield Bash during the wind-up cancels it.</item>
    /// <item><b>Raise the Dead</b> — at 66% and 33% health he calls skeletons out of the
    ///   floor. Answer: the tank War Cries to hold them while the party burns the King.</item>
    /// </list>
    /// Below 25% he enrages: every wind-up is 30% shorter.
    /// </summary>
    public class HollowKingBrain : BossBrain
    {
        [SerializeField] private float slamRadius = 6.5f;
        [SerializeField] private float slamWindup = 1.5f;
        [SerializeField] private float slamDamage = 48f;
        [SerializeField] private float slamCooldown = 9f;
        [SerializeField] private float cleaveWindup = 0.85f;

        public override string DisplayName => "Ossric, the Hollow King";
        public override string Epithet => "Sovereign of the Empty Throne";
        public override string LastWords => "The morning... was it always this cold? Take the crown. It was never mine.";
        public override Progression.Deeds.Deed Deed => Progression.Deeds.Deed.HollowKing;
        public override string Hint => "When a player is marked by the crown, everyone stands in the circle to share the blow.";

        private float nextSlam;
        private bool slamming;
        private float slamAt;
        private bool summoned66, summoned33, guardCalled;

        // ---- Crown's Judgement: a marked player is crushed; the blow is shared by all who stand with them. ----
        private float nextJudgement;
        private Player.PlayerLife judged;
        private float judgeTelegraphAt = -1f, judgeAt = -1f;
        private Vector3 judgeSpot;
        private const float JudgeRadius = 3.4f, JudgeDamage = 130f;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Rig.StrideLength = 2.4f;
            nextSlam = Time.time + 6f;
            nextJudgement = Time.time + 8f;
        }

        private bool Enraged => health.Fraction < 0.25f;

        // ---- Phases ----
        protected override float[] PhaseGates => new[] { 0.5f, 0.25f };

        protected override void OnPhaseGate(int index)
        {
            if (index == 0)
            {
                var guards = guardPrefab != null ? SummonOf(guardPrefab, 2, 3.5f, 1.4f) : new System.Collections.Generic.List<EnemyBrain>();
                guardCalled = true;
                GuardFxRpc();
                InvulnerableWhile("Throne Ward", null, "The Royal Guard wards the King: kick their shields aside and cut them down", guards, 25f,
                    () => Taunt("Traitors. All of you. Then I will do it myself."));
            }
            else
                Transition("The Last Crown", "I was a king before your gods were born!", 2.2f, () => nextBlades = Time.time + 2f);
        }
        private float WindupScale => Enraged ? 0.7f : 1f;

        // ---- Crown of Blades: spectral swords wheel out of him while he keeps walking at you ----
        private float nextBlades = -1f;
        protected override ProjectileKind? QuickKind => ProjectileKind.Smite;

        private void TickBlades()
        {
            if (nextBlades < 0f || Time.time < nextBlades || Immune || PatternRunning) return;
            nextBlades = Time.time + 13f;
            BladesRpc();
            StartPattern(new Pattern
            {
                Arms = 5, Interval = 0.14f, Speed = 11f, Spin = 9f, FlipEvery = 2f, Damage = damage * 0.4f,
                Kind = ProjectileKind.Smite, Duration = 4.2f, Range = 26f, Origin = t => transform.position + Vector3.up * 1.6f,
            });
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BladesRpc()
        {
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 4.4f, "CROWN OF BLADES", new Color(1f, 0.85f, 0.45f));
            Sfx.Play(Sound.Swing, transform.position + Vector3.up * 2f, 1f, 0.5f);
        }

        protected override void Update()
        {
            if (IsServer && !IsDead) { TickJudgement(); TickBlades(); }
            if (IsServer && !IsDead && slamming)
            {
                agent.isStopped = true;
                if (Time.time >= slamAt) ResolveSlam();
                return;
            }
            base.Update();
        }

        // Crown's Fall: the King hammers the dais and a shockwave rolls out; faster when enraged.
        protected override float ShockwaveCooldown => health.Fraction < 0.25f ? 7f : 11f;
        protected override Color ShockwaveColor => new Color(0.45f, 0.75f, 1f);

        protected override bool ChooseSpecialAttack(float distanceToTarget)
        {
            if (TryShockwave()) return true;
            if (!summoned66 && health.Fraction < 0.66f) { summoned66 = true; Summon(2 + scaledPlayers, 4f); return true; }
            if (!summoned33 && health.Fraction < 0.33f) { summoned33 = true; Summon(2 + scaledPlayers, 4f); return true; }
            if (judged == null && Time.time >= nextJudgement && BeginJudgement()) return false;

            if (Time.time >= nextSlam && distanceToTarget < slamRadius + 2f)
            {
                BeginSlam();
                return true;
            }
            return false;
        }

        protected override void BeginAttack()
        {
            // Royal Cleave replaces the basic swing: wider, slower, harder.
            windup = cleaveWindup * WindupScale;
            attackArc = 170f;
            base.BeginAttack();
        }

        private void BeginSlam()
        {
            slamming = true;
            slamAt = Time.time + slamWindup * WindupScale;
            nextSlam = Time.time + slamCooldown;
            busyUntil = slamAt + 1.2f;
            PlayActionRpc(RigAction.Slam, slamWindup * WindupScale + 0.3f);
            CircleTelegraphRpc(transform.position, slamRadius, slamWindup * WindupScale, true);
        }

        private void ResolveSlam()
        {
            slamming = false;
            foreach (var victim in CombatQuery.Circle(transform.position, slamRadius, Team.Players))
            {
                // Pillars are cover: a slam does not go through stone.
                if (!CombatQuery.ClearLine(transform.position + Vector3.up * 0.8f, victim)) continue;

                victim.ServerDamage(slamDamage, transform.position, NetworkObjectId);
                var push = victim.transform.position - transform.position;
                push.y = 0f;
                KnockPlayer(victim, push.normalized * 14f + Vector3.up * 5f);
            }
            ImpactFxRpc(transform.position, 18f);
        }

        private bool BeginJudgement()
        {
            var pool = Player.PlayerLife.All.FindAll(p => p != null && p.Targetable);
            if (pool.Count == 0) return false;
            judged = pool[Random.Range(0, pool.Count)];
            nextJudgement = Time.time + (Enraged ? 12f : 17f);
            judgeTelegraphAt = Time.time + 1.2f;
            judgeAt = Time.time + 2.9f;
            JudgementMarkRpc(judged.NetworkObjectId);
            Debug.Log("[Boss] Hollow King judges " + judged.name);
            return true;
        }

        private void TickJudgement()
        {
            if (judged == null) return;
            if (judgeTelegraphAt > 0f && Time.time >= judgeTelegraphAt)
            {
                judgeTelegraphAt = -1f;
                judgeSpot = judged.transform.position;
                CircleTelegraphRpc(judgeSpot, JudgeRadius, judgeAt - Time.time, false);
            }
            if (judgeAt > 0f && Time.time >= judgeAt)
            {
                judgeAt = -1f;
                judged = null;
                var inside = new System.Collections.Generic.List<Health>(CombatQuery.Circle(judgeSpot, JudgeRadius, Team.Players));
                float share = JudgeDamage * Difficulty.Level.Damage / Mathf.Max(1, inside.Count);
                foreach (var v in inside) v.ServerDamage(share, judgeSpot + Vector3.up, NetworkObjectId);
                JudgementFallsRpc(judgeSpot, inside.Count);
                Debug.Log("[Boss] Hollow King judgement shared by " + inside.Count);
            }
        }

        [Unity.Netcode.Rpc(Unity.Netcode.SendTo.ClientsAndHost)]
        private void JudgementMarkRpc(ulong who)
        {
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(who, out var obj)) return;
            bool me = Player.NetworkPlayer.Local != null && obj.gameObject == Player.NetworkPlayer.Local.gameObject;
            UI.GameHud.Feed((me ? "YOU are" : obj.name + " is") + " judged by the Crown. Stand together to share the blow!", new Color(0.55f, 0.75f, 1f));
            UI.GameHud.ShowCombo(obj.transform.position + Vector3.up * 2.6f, "JUDGED", new Color(0.55f, 0.75f, 1f));
            Sfx.Play(Sound.Summon, obj.transform.position + Vector3.up * 2f, 1f, 1.3f);
            if (me) Sfx.Play2D(Sound.WarCry, 0.6f);
        }

        [Unity.Netcode.Rpc(Unity.Netcode.SendTo.ClientsAndHost)]
        private void JudgementFallsRpc(Vector3 at, int sharedBy)
        {
            var c = new Color(0.55f, 0.75f, 1f);
            Sfx.Play(Sound.Slam, at, 1f, 0.7f);
            Vfx.Flash(at + Vector3.up * 3f, c, 8f, 10f, 0.4f);
            Vfx.Ring(at, JudgeRadius, c, 0.45f);
            for (int i = 0; i < 5; i++) Vfx.Fireball(at + Vector3.up * (0.5f + i * 0.9f), 1.2f - i * 0.15f, c);
            if (sharedBy > 1) UI.GameHud.ShowCombo(at + Vector3.up * 1.5f, "SHARED x" + sharedBy, c);
            var local = Player.NetworkPlayer.Local;
            if (local != null && Vector3.Distance(local.transform.position, at) < 10f) local.AddShake(0.5f);
        }

        [Unity.Netcode.Rpc(Unity.Netcode.SendTo.ClientsAndHost)]
        private void GuardFxRpc() => UI.GameHud.Feed("\"Guards! To your king!\"", new Color(0.55f, 0.75f, 1f));

        public override void ServerStun(float seconds)
        {
            // Heavy: a bash only staggers him briefly — but it does cancel the slam.
            if (slamming)
            {
                slamming = false;
                CancelTelegraphRpc();
            }
            base.ServerStun(Mathf.Min(seconds, 0.5f));
        }
    }
}
