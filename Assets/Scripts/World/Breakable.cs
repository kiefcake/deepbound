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
        public enum Kind : byte { Urn, PowderBarrel }

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
            health.ServerSetMax(Type.Value == Kind.Urn ? 1f : 10f, refill: true);
            health.Died += _ => ServerBreak();
        }

        private void ServerBreak()
        {
            if (broken) return;
            broken = true;
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
            if (Type.Value == Kind.Urn)
            {
                var urn = MeshKit.Part(transform, "Urn", MeshKit.Lathe("urn", new[]
                {
                    new Vector2(0f, 0f), new Vector2(0.16f, 0f), new Vector2(0.26f, 0.22f), new Vector2(0.24f, 0.45f),
                    new Vector2(0.12f, 0.6f), new Vector2(0.15f, 0.68f), new Vector2(0f, 0.68f),
                }, 10, false), Mat.Rubble, Vector3.zero);
                ArtLibrary.Tint(urn.GetComponent<Renderer>(), new Color(1.25f, 0.75f, 0.48f));
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
