using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// The Pale Cardinal: floor 4's boss, in the Black Cathedral. A towering prelate in
    /// crimson and gold who fights with the cathedral itself.
    ///
    /// <list type="bullet">
    /// <item><b>Toll of the Bell</b> — a golden shockwave rolls out across the nave. Jump
    ///   it or dodge through it.</item>
    /// <item><b>Judgement</b> — circles of holy fire open under and around every player;
    ///   when they fill, pillars of flame erupt. Keep moving.</item>
    /// <item><b>Choir of the Damned</b> — at two thirds and one third health, Wraiths answer
    ///   the hymn. They are Ethereal: pin them down (chill, stun, mark, consecrate).</item>
    /// <item><b>Last Rites</b> — below a third, everything comes faster.</item>
    /// <item><b>Excommunication</b> (2026-10-26) — after the first choir, a golden brand rings one player (whoever
    ///   stands with the most friends); when it bursts it scorches everyone inside the ring, the bearer least. The
    ///   branded walks away from the party (or dodges through the burst).</item>
    /// </list>
    /// </summary>
    public class CardinalBrain : BossBrain
    {
        protected override DashStyle Dash => DashStyle.Leap;
        [SerializeField] private float judgementCooldown = 7.5f, judgementWindup = 1.5f, judgementRadius = 2.4f, judgementDamage = 38f;

        public override string DisplayName => "The Pale Cardinal";
        public override string Epithet => "Last Voice of the Black Cathedral";
        protected override byte EntranceStyle => 4;
        public override string LastWords => "The hymn... ends... and so... they wake...";
        public override Progression.Deeds.Deed Deed => Progression.Deeds.Deed.Cardinal;
        public override string Hint => "Kill the Wraith choir to break its ward. Step out of the burning circles, jump the bell's toll - and if you are BRANDED, walk away from your friends.";

        private float nextJudgement;
        private bool choir66, choir33;

        private struct Pillar { public Vector3 At; public float When; }
        private readonly List<Pillar> pillars = new List<Pillar>();

        private bool LastRites => health.Fraction < 0.34f;

        public const float BrandCooldown = 15f, BrandSeconds = 4.5f, BrandRadius = 4.5f;
        private float nextBrand, brandBurstsAt = -1f;
        private PlayerLife branded;
        /// <summary>For tests: brands that burst, and who the last one caught.</summary>
        public int BrandsBurst { get; private set; }
        public int LastBrandCaught { get; private set; }

        // ---- Phases ----
        protected override float[] PhaseGates => new[] { 0.66f, 0.33f, 0.12f };
        private float nextHymnPillars;

        protected override void OnPhaseGate(int index)
        {
            if (index < 2)
            {
                var choir = Summon((index == 0 ? 1 : 2) + scaledPlayers, 5f, 1.8f);
                if (index == 0) { choir66 = true; nextBrand = Time.time + 6f; } else choir33 = true;
                InvulnerableWhile("Choir of the Damned", index == 0 ? "Sing, my children. Sing them to sleep." : "Louder! Let the whole cathedral hear!",
                    "The Wraith choir wards the Cardinal: pin them (chill, stun, mark) and silence them", choir, 22f,
                    () => Taunt("The hymn is broken... no matter. I am the hymn."));
            }
            else
            {
                Survive("Final Hymn", "Kneel. Kneel, and burn.", "Survive the Final Hymn: never stop moving", 10f, () => Taunt("Still standing? Then come and end it."));
                nextHymnPillars = Time.time + 0.8f;
                // The rose window: four emitters circling the arena, swaying arms of holy fire.
                var c = World.DungeonDirector.Layout != null ? World.DungeonDirector.Layout.BossRoom.Center : transform.position;
                for (int e = 0; e < 4; e++)
                {
                    float phase = e * 90f;
                    StartPattern(new Pattern
                    {
                        Arms = 2, Interval = 0.16f, Speed = 10f, Spin = 8f, Wobble = 7f, Damage = Signature(judgementDamage) * 0.3f,
                        Kind = ProjectileKind.Smite, Duration = 9.5f, Range = 22f,
                        Origin = t => c + Quaternion.Euler(0f, phase + t * 26f, 0f) * Vector3.forward * 7.5f + Vector3.up * 1.2f,
                    });
                }
                LaunchShockwave(transform.position, 9f, 18f, ShockwaveDamage);
            }
        }

        private void TickHymn()
        {
            if (!InSurvival || Time.time < nextHymnPillars) return;
            nextHymnPillars = Time.time + 1.3f;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || !p.Targetable) continue;
                AddPillar(PredictedFeet(p, judgementWindup));
                for (int i = 0; i < 2; i++)
                    AddPillar(p.transform.position + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(2.5f, 4.5f));
            }
        }
        protected override float ShockwaveCooldown => LastRites ? 7f : 10f;
        protected override ProjectileKind? QuickKind => ProjectileKind.Smite;
        protected override Color ShockwaveColor => new Color(1f, 0.8f, 0.35f);

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            nextJudgement = Time.time + 5f;
        }

        protected override void Update()
        {
            if (HeldByStun()) return;
            if (IsServer && !IsDead) { ResolvePillars(); TickHymn(); TickBrand(); }
            base.Update();
        }

        protected override bool ChooseSpecialAttack(float distanceToTarget)
        {
            if (TryShockwave()) return true;

            if (choir66 && !InSurvival && brandBurstsAt < 0f && Time.time >= nextBrand && Brand()) return true;
            if (Time.time >= nextJudgement)
            {
                nextJudgement = Time.time + judgementCooldown * (LastRites ? 0.65f : 1f);
                castUntil = Time.time + 0.9f;
                busyUntil = castUntil;
                PlayActionRpc(RigAction.Cast, 0.9f);
                Debug.Log("[Boss] Cardinal judgement");
                // One circle under each player, and a few scattered near them.
                foreach (var p in PlayerLife.All)
                {
                    if (p == null || !p.Targetable) continue;
                    AddPillar(PredictedFeet(p, judgementWindup));
                    for (int i = 0; i < (LastRites ? 3 : 2); i++)
                        AddPillar(p.transform.position + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * Random.Range(2.5f, 5f));
                }
                return true;
            }
            return false;
        }

        /// <summary>Excommunication: ring the player who stands with the most friends.</summary>
        private bool Brand()
        {
            PlayerLife pick = null;
            int bestCrowd = -1;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || !p.Targetable) continue;
                int crowd = 0;
                foreach (var q in PlayerLife.All)
                    if (q != null && q != p && q.Targetable && Vector3.Distance(q.transform.position, p.transform.position) < BrandRadius * 1.5f) crowd++;
                if (crowd > bestCrowd || (crowd == bestCrowd && Random.value < 0.5f)) { bestCrowd = crowd; pick = p; }
            }
            if (pick == null) return false;
            branded = pick;
            float seconds = Tell(BrandSeconds);
            brandBurstsAt = Time.time + seconds;
            nextBrand = Time.time + BrandCooldown * (LastRites ? 0.75f : 1f) / Cadence;
            castUntil = Time.time + 0.8f;
            busyUntil = castUntil;
            PlayActionRpc(RigAction.Cast, 0.8f);
            BrandRpc(pick.NetworkObjectId, BrandRadius, seconds);
            Taunt(StandingParty > 1 ? "You. You are cast out. Take your sin away from them." : "I cast you out. Burn alone.");
            Debug.Log("[Boss] Cardinal brands " + pick.name);
            return true;
        }

        private void TickBrand()
        {
            if (brandBurstsAt < 0f || Time.time < brandBurstsAt) return;
            brandBurstsAt = -1f;
            if (branded == null) return;
            // Down or hidden in a soul before the burst: the brand goes with them, rather than burning the spot they fell on.
            if (!branded.Targetable) { branded = null; return; }
            var at = branded.transform.position;
            int caught = 0;
            foreach (var victim in CombatQuery.Circle(at, BrandRadius, Team.Players))
            {
                bool bearer = victim == branded.Health;
                victim.ServerDamage(Signature(judgementDamage) * (bearer ? 0.8f : 1.3f), at + Vector3.up, NetworkObjectId);
                if (!bearer) caught++;
            }
            BrandsBurst++;
            LastBrandCaught = caught;
            PillarFxRpc(at);
            BrandBurstRpc(at, BrandRadius);
            branded = null;
        }

        /// <summary>For tests: brand now.</summary>
        public bool ServerDebugBrand() { choir66 = true; brandBurstsAt = -1f; nextBrand = 0f; return Brand(); }

        [Rpc(SendTo.ClientsAndHost)]
        private void BrandRpc(ulong playerObject, float radius, float seconds)
        {
            var gold = new Color(1f, 0.78f, 0.3f);
            if (!Unity.Netcode.NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(playerObject, out var obj)) return;
            FollowRing.Attach(obj.transform, radius, seconds, gold * 1.4f);
            Sfx.Play(Sound.Toll, obj.transform.position + Vector3.up * 2f, 0.7f, 1.3f);
            if (obj.IsOwner) UI.GameHud.ShowCombo(obj.transform.position + Vector3.up * 2.4f, "EXCOMMUNICATED: MOVE AWAY FROM YOUR ALLIES", gold);
            else UI.GameHud.ShowCombo(obj.transform.position + Vector3.up * 2.4f, "BRANDED: STAND CLEAR", gold);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BrandBurstRpc(Vector3 at, float radius)
        {
            var gold = new Color(1f, 0.78f, 0.3f);
            Vfx.Ring(at + Vector3.up * 0.05f, radius, gold, 0.5f);
            Vfx.Flash(at + Vector3.up * 1.5f, gold, 7f, radius + 4f, 0.5f);
        }

        private void AddPillar(Vector3 at)
        {
            if (UnityEngine.AI.NavMesh.SamplePosition(at, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas)) at = hit.position;
            pillars.Add(new Pillar { At = at, When = Time.time + Tell(judgementWindup) });
            JudgementWarnRpc(at, judgementRadius, Tell(judgementWindup));
            GatherRpc(Tell(judgementWindup), 0.6f);
        }

        private void ResolvePillars()
        {
            for (int i = pillars.Count - 1; i >= 0; i--)
            {
                if (Time.time < pillars[i].When) continue;
                var at = pillars[i].At;
                pillars.RemoveAt(i);
                foreach (var victim in CombatQuery.Circle(at, judgementRadius, Team.Players))
                {
                    victim.ServerDamage(Signature(judgementDamage), at + Vector3.up, NetworkObjectId);
                    // In the Last Rites, holy fire burns the mind too.
                    if (LastRites && victim.TryGetComponent<Player.PlayerDebuffs>(out var deb)) deb.ServerMania(1.8f);
                }
                PillarFxRpc(at);
                Debug.Log("[Boss] Cardinal pillar erupted");
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void JudgementWarnRpc(Vector3 at, float radius, float seconds) =>
            Telegraph.Show(at, radius, seconds, null, new Color(1f, 0.75f, 0.3f, 0.4f));

        [Rpc(SendTo.ClientsAndHost)]
        private void PillarFxRpc(Vector3 at)
        {
            var holy = new Color(1f, 0.75f, 0.35f);
            Sfx.Play(Sound.Fire, at + Vector3.up, 0.8f, 1.2f);
            Vfx.Flash(at + Vector3.up * 2f, holy, 6f, 8f, 0.5f);
            for (int i = 0; i < 6; i++)
                Vfx.Fireball(at + Vector3.up * (0.5f + i * 0.7f), 1.4f - i * 0.12f, holy);
            Vfx.Embers(at, 14, holy, 5f, 0.06f, 0.8f);
        }
    }
}
