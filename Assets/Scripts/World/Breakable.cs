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
        public enum Kind : byte { Urn, PowderBarrel, RitualBrazier, SoulLantern }

        public readonly NetworkVariable<Kind> Type = new NetworkVariable<Kind>();
        [SerializeField] private NetworkObject lootPrefab;

        private const float BlastRadius = 3.8f, BlastDamage = 55f, FuseSeconds = 0.7f;
        private Health health;
        private bool broken;

        public void SetLootPrefab(NetworkObject prefab) => lootPrefab = prefab;

        private void Awake() => health = GetComponent<Health>();

        public override void OnNetworkSpawn()
        {
            BuildVisual();
            if (!IsServer) return;
            health.ServerSetMax(Type.Value == Kind.Urn ? 1f : Type.Value == Kind.RitualBrazier ? 60f + 20f * DungeonDirector.Tier(DungeonDirector.Layout != null ? DungeonDirector.Layout.Depth : 0) : 10f, refill: true);
            health.Died += _ => ServerBreak();
        }

        private void ServerBreak()
        {
            if (broken) return;
            broken = true;
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
            if (Type.Value == Kind.Urn)
            {
                LootDrop.ServerSpawnFromTable(lootPrefab, LootTable.Urn, transform.position, 1f);
                BreakFxRpc();
                NetworkObject.Despawn();
            }
            else
            {
                FuseFxRpc();
                Invoke(nameof(Explode), FuseSeconds);
            }
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
            ExplodeFxRpc(at);
            NetworkObject.Despawn();
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
