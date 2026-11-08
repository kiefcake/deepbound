using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Loot;
using Deepbound.Player;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// Breakable dungeon dressing with something inside: clay urns (gold, sometimes a
    /// potion) and powder barrels (they explode — hurting enemies and careless players
    /// alike, and setting off other barrels nearby).
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class Breakable : NetworkBehaviour
    {
        /// <summary>Append only (synced). 2026-11-08 interactables: OilJar (spills a slick), CrackedPillar (topples away
        /// from whoever breaks it, crushing a lane), FireBowl (tips over: burning coals that light oil).</summary>
        public enum Kind : byte { Urn, PowderBarrel, RitualBrazier, SoulLantern, HealPylon, GreatUrn, OilJar, CrackedPillar, FireBowl }

        public readonly NetworkVariable<Kind> Type = new NetworkVariable<Kind>();
        /// <summary>A great urn takes a few blows (plus 12 a tier).</summary>
        public const float GreatUrnHealth = 36f;
        [SerializeField] private NetworkObject lootPrefab;

        private const float BlastRadius = 3.8f, BlastDamage = 55f, FuseSeconds = 0.7f;
        private Health health;
        private bool broken;

        public void SetLootPrefab(NetworkObject prefab) => lootPrefab = prefab;

        private void Awake() => health = GetComponent<Health>();

        /// <summary>Live heal pylons (the HUD marks them).</summary>
        public static readonly System.Collections.Generic.List<Breakable> Pylons = new System.Collections.Generic.List<Breakable>();
        public bool IsPylon => Type.Value == Kind.HealPylon;
        public Health Health => health;

        /// <summary>Synced: is it pouring health into the boss right now (the beam, the HUD warning)?</summary>
        public readonly NetworkVariable<bool> Feeding = new NetworkVariable<bool>();

        public override void OnNetworkDespawn() { Pylons.Remove(this); base.OnNetworkDespawn(); }

        public override void OnNetworkSpawn()
        {
            BuildVisual();
            if (Type.Value == Kind.HealPylon && !Pylons.Contains(this)) Pylons.Add(this);
            if (Type.Value == Kind.HealPylon && TryGetComponent<CapsuleCollider>(out var cap))
            {
                cap.radius = 0.55f; cap.height = 2.8f; cap.center = new Vector3(0f, 1.4f, 0f);
                health.InvalidateBody();
            }
            if (Type.Value == Kind.GreatUrn && TryGetComponent<CapsuleCollider>(out var gcap))
            {
                gcap.radius = 0.62f; gcap.height = 1.9f; gcap.center = new Vector3(0f, 0.95f, 0f);
                health.InvalidateBody();
            }
            if (Type.Value == Kind.CrackedPillar && TryGetComponent<CapsuleCollider>(out var pcap))
            {
                pcap.radius = 0.55f; pcap.height = 4.2f; pcap.center = new Vector3(0f, 2.1f, 0f);
                health.InvalidateBody();
            }
            if (Type.Value == Kind.FireBowl && TryGetComponent<CapsuleCollider>(out var fcap))
            {
                fcap.radius = 0.4f; fcap.height = 1.3f; fcap.center = new Vector3(0f, 0.65f, 0f);
                health.InvalidateBody();
            }
            if (!IsServer) return;
            int players = Mathf.Max(1, NetworkManager.ConnectedClientsIds.Count);
            float tier = DungeonDirector.Tier(DungeonDirector.Layout != null ? DungeonDirector.Layout.Depth : 0);
            health.ServerSetMax(Type.Value == Kind.Urn || Type.Value == Kind.OilJar ? 1f : Type.Value == Kind.FireBowl ? 12f : Type.Value == Kind.CrackedPillar ? PillarHealth + 30f * tier
                : Type.Value == Kind.GreatUrn ? GreatUrnHealth + 12f * tier : Type.Value == Kind.RitualBrazier ? 60f + 20f * tier
                : Type.Value == Kind.HealPylon ? (140f + 70f * tier) * (1f + 0.4f * (players - 1)) : 10f, refill: true);
            health.Died += _ => ServerBreak();
            health.DamagedServer += (lost, by) => lastHitBy = by;
            // A pylon that is being struck stops pouring for a moment: attacking it pays off at once.
            if (Type.Value == Kind.HealPylon) health.DamagedServer += (lost, by) => stunnedUntil = Time.time + PylonHitPause;
        }

        /// <summary>
        /// A pylon pours this fraction of the boss's health back into it every second (2026-10-24, the user: "a boss
        /// that heals from pillars" - three or four at 0.8% each healed faster than a party could hurt it), and
        /// stops for <see cref="PylonHitPause"/> s whenever it is hit.
        /// </summary>
        public const float PylonHeal = 0.0035f, PylonHitPause = 4f;
        private float stunnedUntil;
        private Transform beam;
        private float nextPylonTick;

        private void Update()
        {
            if (Type.Value != Kind.HealPylon || broken) return;
            var boss = Enemies.BossBrain.Active;
            bool feeding = IsServer ? boss != null && !boss.IsDead && !boss.Dormant && boss.Health.Fraction < 0.999f && Time.time >= stunnedUntil : Feeding.Value;
            if (IsServer && Feeding.Value != feeding) Feeding.Value = feeding;
            if (IsServer && feeding && Time.time >= nextPylonTick)
            {
                nextPylonTick = Time.time + 0.5f;
                boss.Health.ServerHeal(boss.Health.Max.Value * PylonHeal * 0.5f);
            }
            // The beam: a thin green-gold line from the crystal to the boss's chest, pulsing.
            if (beam == null)
            {
                beam = MeshKit.Part(transform, "PylonBeam", MeshKit.Box(new Vector3(0.06f, 0.06f, 1f)), Mat.Glow, Vector3.zero).transform;
                beam.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            beam.gameObject.SetActive(feeding);
            if (!feeding) return;
            var from = transform.position + Vector3.up * 2.4f;
            var to = boss.Health.Center;
            beam.position = (from + to) / 2f;
            beam.rotation = Quaternion.LookRotation(to - from);
            float pulse = 0.7f + 0.3f * Mathf.Sin(Time.time * 6f);
            beam.localScale = new Vector3(pulse, pulse, Vector3.Distance(from, to));
            ArtLibrary.SetGlow(beam.GetComponent<Renderer>(), new Color(0.5f, 1f, 0.55f) * (1.2f * pulse));
        }

        private ulong lastHitBy = ulong.MaxValue;
        public const float PillarHealth = 90f, PillarLength = 5.2f, PillarWidth = 1.7f, PillarWarn = 0.6f;

        /// <summary>Server: the way from whoever struck last to this (a pillar falls that way, a fire bowl tips that way).</summary>
        private Vector3 AwayFromHitter()
        {
            Vector3 dir = Vector3.zero;
            if (lastHitBy != ulong.MaxValue && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(lastHitBy, out var hitter))
                dir = transform.position - hitter.transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = transform.forward;
            return dir.normalized;
        }

        private void ServerBreak()
        {
            if (broken) return;
            broken = true;
            if (Type.Value == Kind.OilJar)
            {
                // The slick lights at once if fire is already beside it.
                bool fire = OilSlick.BurningAt(transform.position);
                DungeonDirector.Instance?.ServerSpawnOil(transform.position, fire, lastHitBy);
                OilJarFxRpc();
                NetworkObject.Despawn();
                return;
            }
            if (Type.Value == Kind.FireBowl)
            {
                // Tipped over: the coals spill a pace away from the blow, burning, lighting any oil they reach.
                var dir = AwayFromHitter();
                var at = transform.position + dir * 1.1f;
                DungeonDirector.Instance?.ServerSpawnOil(at, true, lastHitBy, coals: true);
                OilSlick.ServerIgniteAt(at, lastHitBy, OilSlick.Radius);
                FireBowlFxRpc(dir);
                NetworkObject.Despawn();
                return;
            }
            if (Type.Value == Kind.CrackedPillar)
            {
                var dir = AwayFromHitter();
                PillarWarnRpc(dir);
                StartCoroutine(PillarCrash(dir));
                return;
            }
            if (Type.Value == Kind.Urn || Type.Value == Kind.GreatUrn) DungeonDirector.Instance?.QuestOnUrn();
            if (Type.Value == Kind.HealPylon)
            {
                PylonFxRpc();
                NetworkObject.Despawn();
                return;
            }
            if (Type.Value == Kind.RitualBrazier)
            {
                RitualFxRpc();
                NetworkObject.Despawn();
                return;
            }
            if (Type.Value == Kind.SoulLantern)
            {
                // The souls go free: the party is mended, the dead nearby reel.
                var at = transform.position + Vector3.up;
                foreach (var ally in CombatQuery.Circle(at, 6f, Team.Players).ToArray()) ally.ServerHeal(ally.Max.Value * 0.25f);
                foreach (var foe in CombatQuery.Circle(at, 6f, Team.Enemies).ToArray())
                    if (foe.TryGetComponent<ICrowdControl>(out var cc)) cc.ServerStun(2.5f);
                SoulFxRpc();
                NetworkObject.Despawn();
                return;
            }
            if (Type.Value == Kind.GreatUrn)
            {
                // A great urn (2026-11-07, the user: "bigger breakable vases with more loot"): a grave-goods jar.
                float tier = DungeonDirector.Tier(DungeonDirector.Layout != null ? DungeonDirector.Layout.Depth : 0);
                LootDrop.ServerSpawnFromTable(lootPrefab, LootTable.GreatUrn, transform.position + Vector3.up * 0.4f, 1f + 0.4f * tier, Enemies.Difficulty.FixedLootShare(Enemies.Difficulty.Party));
                BreakFxRpc();
                BreakFxRpc();
                NetworkObject.Despawn();
                return;
            }
            if (Type.Value == Kind.Urn)
            {
                LootDrop.ServerSpawnFromTable(lootPrefab, LootTable.Urn, transform.position, 1f, Enemies.Difficulty.FixedLootShare(Enemies.Difficulty.Party));
                BreakFxRpc();
                NetworkObject.Despawn();
            }
            else
            {
                FuseFxRpc();
                Invoke(nameof(Explode), FuseSeconds);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PylonFxRpc()
        {
            var at = transform.position + Vector3.up * 2.4f;
            Sfx.Play(Sound.Shatter, at, 1f, 0.6f);
            Vfx.Flash(at, new Color(0.5f, 1f, 0.55f), 5f, 8f, 0.4f);
            Vfx.Embers(at, 20, new Color(0.5f, 1f, 0.55f), 6f, 0.06f, 0.8f);
            UI.GameHud.ShowCombo(at + Vector3.up * 0.5f, "PYLON BROKEN", new Color(0.6f, 1f, 0.6f));
        }

        private void Explode()
        {
            var at = transform.position + Vector3.up * 0.5f;
            // Enemies (and other barrels, and urns) and players are all caught.
            foreach (var h in CombatQuery.Circle(at, BlastRadius, Team.Enemies).ToArray())
            {
                if (h == health) continue;
                h.ServerDamage(BlastDamage, at, NetworkObjectId);
                if (h.TryGetComponent<ICrowdControl>(out var cc))
                {
                    var push = h.transform.position - at;
                    push.y = 0f;
                    cc.ServerKnockback(push.normalized * 10f);
                }
            }
            foreach (var h in CombatQuery.Circle(at, BlastRadius, Team.Players).ToArray())
            {
                h.ServerDamage(BlastDamage * 0.6f, at, NetworkObjectId);
                var push = h.transform.position - at;
                push.y = 0f;
                if (h.TryGetComponent<KnockbackReceiver>(out var k)) k.KnockbackRpc(push.normalized * 12f + Vector3.up * 5f);
            }
            OilSlick.ServerIgniteAt(at, NetworkObjectId, BlastRadius);
            ExplodeFxRpc(at);
            NetworkObject.Despawn();
        }

        private System.Collections.IEnumerator PillarCrash(Vector3 dir)
        {
            yield return new WaitForSeconds(PillarWarn);
            var from = transform.position;
            float tier = DungeonDirector.Tier(DungeonDirector.Layout != null ? DungeonDirector.Layout.Depth : 0);
            foreach (var h in Health.All.ToArray())
            {
                if (h == null || !h.IsAlive || h == health) continue;
                var d = h.transform.position - from;
                if (Mathf.Abs(d.y) > 2f) continue;
                float along = Vector3.Dot(new Vector3(d.x, 0f, d.z), dir);
                float side = Vector3.Cross(dir, new Vector3(d.x, 0f, d.z)).magnitude;
                if (along < -0.4f || along > PillarLength || side > PillarWidth / 2f + 0.35f) continue;
                if (h.Team == Team.Enemies)
                {
                    var boss = h.GetComponent<Enemies.BossBrain>();
                    float dmg = boss != null ? h.Max.Value * 0.05f : Mathf.Max(h.Max.Value * 0.6f, 40f + 15f * tier);
                    h.Tag("Falling pillar").ServerDamage(dmg, from, lastHitBy);
                    if (boss != null) boss.ServerAddStagger(0.25f);
                    else if (h.TryGetComponent<ICrowdControl>(out var cc)) cc.ServerStun(2f);
                }
                else if (h.Team == Team.Players)
                {
                    h.Tag("Falling pillar").ServerDamage(h.Max.Value * 0.3f + h.Defense * Health.DefenseWeight, from);
                    if (h.TryGetComponent<KnockbackReceiver>(out var k)) k.KnockbackRpc(Vector3.Cross(Vector3.up, dir) * (Vector3.Dot(h.transform.position - from, Vector3.Cross(Vector3.up, dir)) >= 0f ? 7f : -7f) + Vector3.up * 3f);
                }
                else if (h.Team == Team.Props && h.TryGetComponent<Breakable>(out var other) && other.Type.Value != Kind.CrackedPillar)
                    h.ServerDamage(999f, from, lastHitBy);
            }
            // Oil in its path splashes alight if anything was burning; the crash itself sparks none.
            PillarRubbleRpc(dir);
            ServerRubble(dir);
        }

        /// <summary>Every peer: the column lies where it fell (a low wall: cover from shots, a barrier the dead walk round).</summary>
        [Rpc(SendTo.ClientsAndHost)]
        private void PillarRubbleRpc(Vector3 dir)
        {
            var at = transform.position;
            foreach (Transform child in transform) if (child.name == "Column") child.gameObject.SetActive(false);
            if (TryGetComponent<CapsuleCollider>(out var cap)) cap.enabled = false;
            var fallen = new GameObject("FallenColumn").transform;
            fallen.SetParent(transform, false);
            fallen.rotation = Quaternion.LookRotation(dir);
            fallen.position = at;
            for (int i = 0; i < 4; i++)
            {
                // Four drums of the column, a little askew, rolled out along the fall.
                var drum = MeshKit.Part(fallen, "Drum" + i, MeshKit.Cylinder(0.46f, 0.46f, 1.05f, 10), Mat.StoneWall,
                    new Vector3(Random.Range(-0.12f, 0.12f), 0.44f, 0.6f + i * 1.15f), new Vector3(90f, 0f, Random.Range(-8f, 8f)));
                drum.transform.localPosition += new Vector3(0f, 0f, -0.52f);
            }
            for (int i = 0; i < 8; i++)
                MeshKit.Part(fallen, "Chunk", MeshKit.Box(Vector3.one * Random.Range(0.18f, 0.4f)), Mat.StoneWall,
                    new Vector3(Random.Range(-1f, 1f), 0.12f, Random.Range(0f, PillarLength)), new Vector3(Random.Range(0f, 40f), Random.Range(0f, 360f), 0f));
            var col = fallen.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.45f, PillarLength * 0.45f);
            col.size = new Vector3(0.95f, 0.9f, PillarLength * 0.85f);
            Sfx.Play(Sound.Slam, at + dir * 2f, 1f, 0.7f);
            Sfx.Play(Sound.BoneBreak, at + dir * 2f, 1f, 0.6f);
            for (int i = 0; i < 4; i++) Vfx.Smoke(at + dir * (i * 1.3f) + Vector3.up * 0.4f, 3, 0.8f, 2.4f);
            var local = Player.NetworkPlayer.Local;
            if (local != null && Vector3.Distance(local.transform.position, at) < 16f) local.AddShake(0.5f);
            UI.GameHud.ShowCombo(at + dir * 2.5f + Vector3.up * 1.5f, "CRUSHED", new Color(0.85f, 0.8f, 0.7f));
        }

        /// <summary>Server: the dead path round the fallen column.</summary>
        private void ServerRubble(Vector3 dir)
        {
            var obstacle = new GameObject("RubbleObstacle").AddComponent<UnityEngine.AI.NavMeshObstacle>();
            obstacle.transform.SetParent(transform, false);
            obstacle.transform.rotation = Quaternion.LookRotation(dir);
            obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
            obstacle.center = new Vector3(0f, 0.45f, PillarLength * 0.45f);
            obstacle.size = new Vector3(0.95f, 0.9f, PillarLength * 0.85f);
            obstacle.carving = true;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PillarWarnRpc(Vector3 dir)
        {
            Telegraph.ShowLine(transform.position, dir, PillarLength, PillarWidth, PillarWarn, null, new Color(0.85f, 0.75f, 0.55f));
            Sfx.Play(Sound.Creak, transform.position + Vector3.up * 2f, 1f, 0.6f);
            Vfx.Smoke(transform.position + Vector3.up * 3.6f, 3, 0.4f, 1.5f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void OilJarFxRpc()
        {
            Sfx.Play(Sound.Shatter, transform.position + Vector3.up * 0.4f, 0.8f, 1.3f);
            Sfx.Play(Sound.Splash, transform.position, 0.6f, 0.7f);
            BoneBurst.Spawn(transform.position, 0.6f, false, Mat.Rubble);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void FireBowlFxRpc(Vector3 dir)
        {
            var at = transform.position + Vector3.up * 0.8f;
            Sfx.Play(Sound.Block, at, 0.9f, 0.6f);
            Sfx.Play(Sound.Fire, at, 1f, 1f);
            Vfx.Embers(at, 24, new Color(1f, 0.55f, 0.2f), 5f, 0.06f, 1f);
            BoneBurst.Spawn(transform.position + dir * 0.6f, 0.8f, false, Mat.Steel);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BreakFxRpc()
        {
            Sfx.Play(Sound.BoneBreak, transform.position + Vector3.up * 0.4f, 0.8f, 1.6f);
            BoneBurst.Spawn(transform.position, 0.8f, false, Mat.Rubble);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SoulFxRpc()
        {
            var soul = new Color(0.7f, 0.9f, 1f);
            var at = transform.position + Vector3.up * 1.5f;
            Sfx.Play(Sound.Revive, at, 1f, 1.2f);
            Sfx.Play(Sound.Shatter, at, 0.7f, 1.4f);
            Vfx.Flash(at, soul, 8f, 10f, 0.6f);
            Vfx.Ring(transform.position, 6f, soul, 0.5f);
            for (int i = 0; i < 18; i++) Vfx.Mote(transform.position + Random.insideUnitSphere * 1.5f, soul);
            Vfx.Embers(at, 16, soul, 4f, 0.05f, 1f);
            UI.GameHud.ShowCombo(at + Vector3.up * 0.5f, "SOULS FREED", soul);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RitualFxRpc()
        {
            var violet = new Color(0.7f, 0.3f, 1f);
            Sfx.Play(Sound.Shatter, transform.position + Vector3.up, 1f, 0.7f);
            Vfx.Flash(transform.position + Vector3.up * 1.2f, violet, 6f, 8f, 0.5f);
            Vfx.Embers(transform.position + Vector3.up * 1.1f, 20, violet, 5f, 0.06f, 0.8f);
            Vfx.Ring(transform.position, 2.5f, violet, 0.4f);
            BoneBurst.Spawn(transform.position + Vector3.up * 0.6f, 1f, false, Mat.Steel);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void FuseFxRpc() => Sfx.Play(Sound.Fire, transform.position + Vector3.up, 0.8f, 1.8f);

        [Rpc(SendTo.ClientsAndHost)]
        private void ExplodeFxRpc(Vector3 at)
        {
            Sfx.Play(Sound.Slam, at, 1f, 0.9f);
            var fire = new Color(1f, 0.55f, 0.2f);
            Vfx.Flash(at + Vector3.up, fire, 12f, 12f, 0.7f);
            Vfx.Fireball(at, BlastRadius * 0.7f, fire);
            FxSprite.Scorch(at, BlastRadius * 0.7f);
            Vfx.Ring(at - Vector3.up * 0.5f, BlastRadius, fire, 0.4f);
            Vfx.Embers(at, 18, fire, 7f, 0.08f, 0.9f);
            Vfx.Smoke(at - Vector3.up * 0.4f, 5, 0.8f, 2.2f);
            var local = Player.NetworkPlayer.Local;
            if (local != null && Vector3.Distance(local.transform.position, at) < 14f) local.AddShake(0.4f);
            BoneBurst.Spawn(at, 1.2f, false, Mat.Wood);
        }

        private void BuildVisual()
        {
            if (Type.Value == Kind.SoulLantern)
            {
                // An iron post with a cage at the top, a pale soul-light trapped inside.
                var soul = new Color(0.7f, 0.9f, 1f);
                MeshKit.Part(transform, "Post", MeshKit.Cylinder(0.04f, 0.05f, 1.7f, 6), Mat.Steel, Vector3.zero);
                MeshKit.Part(transform, "Foot", MeshKit.Cylinder(0.22f, 0.25f, 0.08f, 8), Mat.Steel, Vector3.zero);
                MeshKit.Part(transform, "CageTop", MeshKit.Lathe("lanternTop", new[] { new Vector2(0.2f, 0f), new Vector2(0f, 0.18f) }, 6), Mat.Steel, Vector3.up * 2.15f);
                MeshKit.Part(transform, "CageBase", MeshKit.Cylinder(0.2f, 0.2f, 0.04f, 6), Mat.Steel, Vector3.up * 1.7f);
                for (int i = 0; i < 6; i++)
                    MeshKit.Part(transform, "Bar" + i, MeshKit.Box(new Vector3(0.02f, 0.45f, 0.02f)), Mat.Steel,
                        Quaternion.Euler(0f, i * 60f, 0f) * new Vector3(0f, 1.93f, 0.19f));
                var orb = MeshKit.Part(transform, "Soul", MeshKit.Ball(0.11f, 8, 6), Mat.Glow, Vector3.up * 1.93f);
                ArtLibrary.SetGlow(orb.GetComponent<Renderer>(), new Color(0.35f, 0.6f, 0.9f) * 0.9f);
                var sl = new GameObject("SoulLight").AddComponent<Light>();
                sl.transform.SetParent(transform, false);
                sl.transform.localPosition = Vector3.up * 1.95f;
                sl.type = LightType.Point;
                sl.color = soul;
                sl.range = 6f;
                sl.intensity = 1.8f;
                sl.shadows = LightShadows.None;
                return;
            }
            if (Type.Value == Kind.RitualBrazier)
            {
                // A black iron brazier on a bone tripod, burning violet: smash it.
                var violet = new Color(0.7f, 0.3f, 1f);
                for (int i = 0; i < 3; i++)
                    MeshKit.Part(transform, "Leg" + i, MeshKit.Box(new Vector3(0.08f, 1.1f, 0.08f)), Mat.Bone,
                        Quaternion.Euler(0f, i * 120f, 0f) * new Vector3(0f, 0.55f, 0.28f), new Vector3(-14f, i * 120f, 0f));
                MeshKit.Part(transform, "Bowl", MeshKit.Lathe("ritualBowl", new[]
                {
                    new Vector2(0.1f, 0f), new Vector2(0.45f, 0.16f), new Vector2(0.55f, 0.34f), new Vector2(0.48f, 0.34f), new Vector2(0f, 0.2f),
                }, 10), Mat.Dark, new Vector3(0f, 1.05f, 0f));
                var fire = MeshKit.Part(transform, "VioletFire", MeshKit.Lathe("ritualFlame", new[]
                {
                    new Vector2(0f, 0f), new Vector2(0.3f, 0.1f), new Vector2(0.25f, 0.45f), new Vector2(0.1f, 0.8f), new Vector2(0f, 1f),
                }, 8, false), Mat.Glow, new Vector3(0f, 1.2f, 0f));
                ArtLibrary.SetGlow(fire.GetComponent<Renderer>(), violet * 1.6f);
                fire.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var l = new GameObject("RitualLight").AddComponent<Light>();
                l.transform.SetParent(transform, false);
                l.transform.localPosition = Vector3.up * 1.8f;
                l.type = LightType.Point;
                l.color = violet;
                l.range = 8f;
                l.intensity = 2.6f;
                l.shadows = LightShadows.None;
                return;
            }
            if (Type.Value == Kind.HealPylon)
            {
                // A black stone obelisk with a green-gold crystal on top, humming.
                MeshKit.Part(transform, "Obelisk", MeshKit.Lathe("pylon", new[] { new Vector2(0.5f, 0f), new Vector2(0.42f, 0.3f), new Vector2(0.3f, 2f), new Vector2(0.12f, 2.25f), new Vector2(0f, 2.3f) }, 6), Mat.Dark, Vector3.zero);
                var crystal = MeshKit.Part(transform, "PylonCrystal", MeshKit.Lathe("pylonCrystal", new[] { new Vector2(0f, -0.25f), new Vector2(0.18f, 0f), new Vector2(0f, 0.3f) }, 6), Mat.Glow, Vector3.up * 2.55f);
                ArtLibrary.SetGlow(crystal.GetComponent<Renderer>(), new Color(0.5f, 1f, 0.55f) * 1.4f);
                for (int i = 0; i < 4; i++)
                {
                    var rune = MeshKit.Part(transform, "PylonRune", MeshKit.Box(new Vector3(0.12f, 0.3f, 0.02f)), Mat.Glow, Quaternion.Euler(0f, i * 90f, 0f) * new Vector3(0f, 1.1f, 0.36f), new Vector3(0f, i * 90f, 0f));
                    ArtLibrary.SetGlow(rune.GetComponent<Renderer>(), new Color(0.5f, 1f, 0.55f) * 0.9f);
                }
                var pl = new GameObject("PylonLight").AddComponent<Light>();
                pl.transform.SetParent(transform, false);
                pl.transform.localPosition = Vector3.up * 2.6f;
                pl.type = LightType.Point;
                pl.color = new Color(0.5f, 1f, 0.55f);
                pl.range = 6f;
                pl.intensity = 2f;
                pl.shadows = LightShadows.None;
                return;
            }
            if (Type.Value == Kind.GreatUrn)
            {
                // A tall grave-goods jar, chest high: dark glaze, a gilt band and a painted skull, wax-sealed lid.
                var jar = MeshKit.Pivot(transform, "Vase", Vector3.zero);
                jar.localScale = Vector3.one * 2.6f;
                var body = MeshKit.Part(jar, "Urn", MeshKit.Lathe("greatUrn", new[]
                {
                    new Vector2(0f, 0f), new Vector2(0.14f, 0f), new Vector2(0.2f, 0.06f), new Vector2(0.27f, 0.28f), new Vector2(0.25f, 0.5f),
                    new Vector2(0.14f, 0.62f), new Vector2(0.12f, 0.66f), new Vector2(0.15f, 0.7f), new Vector2(0f, 0.7f),
                }, 14, false), Mat.Rubble, Vector3.zero);
                ArtLibrary.Tint(body.GetComponent<Renderer>(), new Color(0.42f, 0.24f, 0.2f));
                var gilt = MeshKit.Part(jar, "Band", MeshKit.Cylinder(0.272f, 0.272f, 0.035f, 14), Mat.Gold, new Vector3(0f, 0.36f, 0f));
                ArtLibrary.Tint(gilt.GetComponent<Renderer>(), new Color(0.75f, 0.55f, 0.22f));
                var foot = MeshKit.Part(jar, "Foot", MeshKit.Cylinder(0.205f, 0.205f, 0.03f, 14), Mat.Gold, new Vector3(0f, 0.04f, 0f));
                ArtLibrary.Tint(foot.GetComponent<Renderer>(), new Color(0.6f, 0.44f, 0.18f));
                // A bone-white skull painted on the shoulder, front and back.
                foreach (float s in new[] { -1f, 1f })
                {
                    var skull = MeshKit.Part(jar, "Skull", MeshKit.Ball(0.05f, 8, 6), Mat.Bone, new Vector3(0f, 0.46f, s * 0.215f), Vector3.zero, new Vector3(1f, 1f, 0.35f));
                    ArtLibrary.Tint(skull.GetComponent<Renderer>(), new Color(0.85f, 0.8f, 0.68f));
                }
                MeshKit.Part(jar, "Lid", MeshKit.Cylinder(0.16f, 0.12f, 0.05f, 12), Mat.Rubble, new Vector3(0f, 0.7f, 0f));
                var wax = MeshKit.Part(jar, "Seal", MeshKit.Ball(0.035f, 6, 4), Mat.Cloth, new Vector3(0f, 0.76f, 0f));
                ArtLibrary.Tint(wax.GetComponent<Renderer>(), new Color(0.5f, 0.06f, 0.05f));
                return;
            }
            if (Type.Value == Kind.OilJar)
            {
                // A squat black-glazed jar with a stoppered neck, oil weeping down its side and a dark stain round its foot.
                var jar = MeshKit.Pivot(transform, "Vase", Vector3.zero);
                jar.localScale = Vector3.one * 1.5f;
                var body = MeshKit.Part(jar, "Jar", MeshKit.Lathe("oilJar", new[]
                {
                    new Vector2(0f, 0f), new Vector2(0.2f, 0f), new Vector2(0.28f, 0.16f), new Vector2(0.27f, 0.34f),
                    new Vector2(0.1f, 0.5f), new Vector2(0.08f, 0.6f), new Vector2(0f, 0.6f),
                }, 12, false), Mat.Rubble, Vector3.zero);
                ArtLibrary.Tint(body.GetComponent<Renderer>(), new Color(0.2f, 0.17f, 0.13f));
                var cork = MeshKit.Part(jar, "Stopper", MeshKit.Cylinder(0.075f, 0.065f, 0.07f, 8), Mat.Wood, new Vector3(0f, 0.6f, 0f));
                ArtLibrary.Tint(cork.GetComponent<Renderer>(), new Color(0.55f, 0.4f, 0.25f));
                var drip = MeshKit.Part(jar, "Drip", MeshKit.Box(new Vector3(0.05f, 0.25f, 0.01f)), Mat.Glow, new Vector3(0.06f, 0.36f, 0.25f), new Vector3(-20f, 0f, 0f));
                ArtLibrary.SetGlow(drip.GetComponent<Renderer>(), new Color(0.6f, 0.45f, 0.15f) * 0.25f);
                var stain = MeshKit.Part(transform, "Stain", MeshKit.Cylinder(0.55f, 0.55f, 0.008f, 12), Mat.Dark, new Vector3(0.1f, 0.01f, 0.1f));
                ArtLibrary.Tint(stain.GetComponent<Renderer>(), new Color(0.05f, 0.04f, 0.03f));
                // A painted flame on the shoulder: this one burns.
                var mark = MeshKit.Part(jar, "FlameMark", MeshKit.Box(new Vector3(0.08f, 0.1f, 0.01f)), Mat.Glow, new Vector3(0f, 0.3f, -0.275f));
                ArtLibrary.SetGlow(mark.GetComponent<Renderer>(), new Color(1f, 0.45f, 0.1f) * 0.6f);
                return;
            }
            if (Type.Value == Kind.FireBowl)
            {
                // An iron fire bowl on three legs, coals glowing: knock it over and they spill.
                for (int i = 0; i < 3; i++)
                    MeshKit.Part(transform, "Leg" + i, MeshKit.Box(new Vector3(0.06f, 0.95f, 0.06f)), Mat.Steel,
                        Quaternion.Euler(0f, i * 120f, 0f) * new Vector3(0f, 0.47f, 0.24f), new Vector3(-12f, i * 120f, 0f));
                MeshKit.Part(transform, "Bowl", MeshKit.Lathe("fireBowl", new[]
                {
                    new Vector2(0.08f, 0f), new Vector2(0.36f, 0.12f), new Vector2(0.44f, 0.28f), new Vector2(0.38f, 0.28f), new Vector2(0f, 0.16f),
                }, 12), Mat.Steel, new Vector3(0f, 0.9f, 0f));
                var coals = MeshKit.Part(transform, "Coals", MeshKit.Cylinder(0.36f, 0.3f, 0.06f, 10), Mat.Glow, new Vector3(0f, 1.12f, 0f));
                ArtLibrary.SetGlow(coals.GetComponent<Renderer>(), new Color(1f, 0.35f, 0.08f) * 1.2f);
                Props.AddFlame(transform, new Vector3(0f, 1.2f, 0f), 0.55f, new Color(1f, 0.55f, 0.2f), 1.8f, 6f, false);
                return;
            }
            if (Type.Value == Kind.CrackedPillar)
            {
                // A stone column with a crack climbing it and fallen grit at its foot: it will not take many blows.
                var col = MeshKit.Pivot(transform, "Column", Vector3.zero);
                MeshKit.Part(col, "Base", MeshKit.Box(new Vector3(1.1f, 0.3f, 1.1f)), Mat.StoneWall, new Vector3(0f, 0.15f, 0f));
                MeshKit.Part(col, "Shaft", MeshKit.Cylinder(0.46f, 0.42f, 3.9f, 10), Mat.StoneWall, new Vector3(0f, 0.3f, 0f));
                MeshKit.Part(col, "Capital", MeshKit.Box(new Vector3(1f, 0.3f, 1f)), Mat.StoneWall, new Vector3(0f, 4.2f, 0f));
                for (int i = 0; i < 6; i++)
                {
                    var crack = MeshKit.Part(col, "Crack", MeshKit.Box(new Vector3(0.04f, 0.55f, 0.02f)), Mat.Dark,
                        new Vector3(0.02f * i, 0.8f + i * 0.5f, 0.44f), new Vector3(0f, 0f, i % 2 == 0 ? 18f : -16f));
                    ArtLibrary.Tint(crack.GetComponent<Renderer>(), new Color(0.05f, 0.04f, 0.04f));
                }
                for (int i = 0; i < 5; i++)
                    MeshKit.Part(col, "Grit", MeshKit.Box(Vector3.one * Random.Range(0.08f, 0.16f)), Mat.StoneWall,
                        new Vector3(Random.Range(-0.7f, 0.7f), 0.05f, Random.Range(-0.7f, 0.7f)), new Vector3(0f, Random.Range(0f, 90f), 0f));
                return;
            }
            if (Type.Value == Kind.Urn)
            {
                // Bigger since 2026-10-20 (the user: "make vases larger"): about knee-to-hip high, with a
                // painted band, two handles and a lid, so they read as something to smash.
                var vase = MeshKit.Pivot(transform, "Vase", Vector3.zero);
                vase.localScale = Vector3.one * 1.65f;
                var urn = MeshKit.Part(vase, "Urn", MeshKit.Lathe("urn", new[]
                {
                    new Vector2(0f, 0f), new Vector2(0.16f, 0f), new Vector2(0.26f, 0.22f), new Vector2(0.24f, 0.45f),
                    new Vector2(0.12f, 0.6f), new Vector2(0.15f, 0.68f), new Vector2(0f, 0.68f),
                }, 12, false), Mat.Rubble, Vector3.zero);
                ArtLibrary.Tint(urn.GetComponent<Renderer>(), new Color(1.25f, 0.75f, 0.48f));
                var band = MeshKit.Part(vase, "Band", MeshKit.Cylinder(0.262f, 0.262f, 0.06f, 12), Mat.Cloth, new Vector3(0f, 0.3f, 0f));
                ArtLibrary.Tint(band.GetComponent<Renderer>(), new Color(0.35f, 0.12f, 0.08f));
                foreach (float s in new[] { -1f, 1f })
                    MeshKit.Part(vase, "Handle", MeshKit.Lathe("urnHandle", new[] { new Vector2(0.05f, -0.02f), new Vector2(0.065f, 0f), new Vector2(0.05f, 0.02f) }, 8, false, 200f),
                        Mat.Rubble, new Vector3(s * 0.2f, 0.52f, 0f), new Vector3(0f, s > 0 ? 0f : 180f, 90f), Vector3.one);
                MeshKit.Part(vase, "Lid", MeshKit.Cylinder(0.13f, 0.15f, 0.04f, 10), Mat.Rubble, new Vector3(0f, 0.7f, 0f));
            }
            else
            {
                MeshKit.Part(transform, "Barrel", MeshKit.Lathe("barrel", new[]
                {
                    new Vector2(0f, 0f), new Vector2(0.32f, 0f), new Vector2(0.4f, 0.3f), new Vector2(0.42f, 0.5f),
                    new Vector2(0.4f, 0.7f), new Vector2(0.32f, 1f), new Vector2(0f, 1f),
                }, 10), Mat.Wood, Vector3.zero);
                var paint = MeshKit.Part(transform, "Warning", MeshKit.Cylinder(0.425f, 0.425f, 0.18f, 10), Mat.Cloth, new Vector3(0f, 0.42f, 0f));
                ArtLibrary.Tint(paint.GetComponent<Renderer>(), new Color(0.75f, 0.1f, 0.06f));
                var fuse = MeshKit.Part(transform, "Fuse", MeshKit.Ball(0.04f, 6, 4), Mat.Glow, new Vector3(0.1f, 1.04f, 0f));
                ArtLibrary.SetGlow(fuse.GetComponent<Renderer>(), new Color(1f, 0.5f, 0.1f) * 2f);
            }
        }
    }
}
