using Deepbound.Art;
using Deepbound.Progression;
using Deepbound.World;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Loot
{
    /// <summary>
    /// A piece of gear on the floor, rolled for one player. Only that player sees it or can
    /// take it — everyone gets their own gear, so nobody argues over a drop. F equips it
    /// (what you were wearing takes its place on the floor); X breaks it down into embers.
    /// </summary>
    public class GearDrop : Interactable
    {
        /// <summary>Set by the director on the server, so any killer or chest can drop gear.</summary>
        public static NetworkObject Prefab;

        public readonly NetworkVariable<GearItem> Item = new NetworkVariable<GearItem>();
        public readonly NetworkVariable<ulong> For = new NetworkVariable<ulong>();

        private Transform visual;
        private float phase;

        public override float Range => 2.2f;

        private bool Mine(PlayerProgress who) => who != null && who.OwnerClientId == For.Value;

        public override string Prompt(PlayerProgress who)
        {
            if (!Mine(who) || Item.Value.IsEmpty) return null;
            var current = who.Equipped(Gear.SlotOf(Item.Value.Kind));
            return (current.IsEmpty ? "Equip " : "Swap for ") + Gear.Name(Item.Value);
        }

        protected override void ServerUse(PlayerProgress who)
        {
            var old = who.ServerEquip(Item.Value);
            EquipFxRpc();
            if (old.IsEmpty) NetworkObject.Despawn();
            else Item.Value = old;
        }

        /// <summary>Owner → server: break this piece down into embers.</summary>
        [Rpc(SendTo.Server)]
        public void ScrapRpc(NetworkObjectReference user)
        {
            if (!user.TryGet(out var obj) || !obj.TryGetComponent<PlayerProgress>(out var who) || !Mine(who)) return;
            if (Vector3.Distance(obj.transform.position, transform.position) > Range + 1f) return;
            who.ServerAddEmbers(Gear.ScrapValue(Item.Value));
            ScrapFxRpc();
            NetworkObject.Despawn();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void EquipFxRpc() => Sfx.Play(Sound.Block, transform.position + Vector3.up * 0.5f, 0.7f, 1.3f);

        [Rpc(SendTo.ClientsAndHost)]
        private void ScrapFxRpc()
        {
            Sfx.Play(Sound.Shatter, transform.position + Vector3.up * 0.5f, 0.6f, 1.5f);
            BoneBurst.Spawn(transform.position + Vector3.up * 0.4f, 0.4f, false, Mat.Flame);
        }

        /// <summary>
        /// Server: roll gear for each player in the session independently, scattered around
        /// a point. <paramref name="chance"/> is per player; luck 1 for bosses and vaults.
        /// </summary>
        public static void ServerSpawnForParty(Vector3 at, float chance, int luck, Enemies.BossBrain boss = null)
        {
            if (Prefab == null) return;
            var net = NetworkManager.Singleton;
            int depth = Mathf.Max(0, DungeonDirector.Instance != null ? DungeonDirector.Instance.Floor.Value.y : 0);
            int i = 0;
            foreach (ulong client in net.ConnectedClientsIds)
            {
                if (Random.value > chance) continue;
                float a = i++ * 2.1f + Random.value;
                var pos = at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.1f + Vector3.up * 0.05f;
                var item = boss != null && Random.value < 0.35f ? new GearItem(Gear.LegendaryOf(boss), 3, depth) : Gear.Roll(depth, luck);
                ServerSpawn(item, client, pos);
            }
        }

        public static GearDrop ServerSpawn(GearItem item, ulong forClient, Vector3 at)
        {
            var obj = Object.Instantiate(Prefab, at, Quaternion.identity);
            var drop = obj.GetComponent<GearDrop>();
            drop.Item.Value = item;
            drop.For.Value = forClient;
            // Only its player is told it exists. (The host still has it — it is the server —
            // so the visual is also gated below.)
            obj.CheckObjectVisibility = id => id == forClient;
            obj.Spawn(destroyWithScene: true);
            return drop;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            phase = Random.value * 10f;
            Item.OnValueChanged += (_, __) => BuildVisual();
            BuildVisual();
        }

        private void Update()
        {
            if (visual == null) return;
            phase += Time.deltaTime;
            visual.localPosition = new Vector3(0f, 0.45f + Mathf.Sin(phase * 2f) * 0.06f, 0f);
            visual.localRotation = Quaternion.Euler(0f, phase * 50f, 0f);
        }

        private void BuildVisual()
        {
            if (visual != null) Destroy(visual.gameObject);
            visual = null;
            var beamOld = transform.Find("Beam");
            if (beamOld != null) Destroy(beamOld.gameObject);
            if (NetworkManager.LocalClientId != For.Value || Item.Value.IsEmpty) return;

            var item = Item.Value;
            var color = Gear.QualityColor(item.Quality);
            visual = MeshKit.Pivot(transform, "Visual", Vector3.up * 0.45f);
            if (Gear.SlotOf(item.Kind) == GearSlot.Armor)
            {
                // A little breastplate: chest, shoulders, a quality-coloured trim.
                MeshKit.Part(visual, "Chest", MeshKit.Box(new Vector3(0.36f, 0.4f, 0.16f)), Mat.Steel, Vector3.zero);
                MeshKit.Part(visual, "PauldronL", MeshKit.Ball(0.1f, 8, 6), Mat.Steel, new Vector3(-0.2f, 0.17f, 0f));
                MeshKit.Part(visual, "PauldronR", MeshKit.Ball(0.1f, 8, 6), Mat.Steel, new Vector3(0.2f, 0.17f, 0f));
                var trim = MeshKit.Part(visual, "Trim", MeshKit.Box(new Vector3(0.38f, 0.05f, 0.18f)), Mat.Glow, new Vector3(0f, -0.18f, 0f));
                ArtLibrary.SetGlow(trim.GetComponent<Renderer>(), color * 1.4f);
            }
            else
            {
                // A charm: a ring of gold around a glowing stone, on a short chain.
                MeshKit.Part(visual, "Setting", MeshKit.Cylinder(0.11f, 0.11f, 0.04f, 12), Mat.Gold, Vector3.zero, new Vector3(90f, 0f, 0f));
                var stone = MeshKit.Part(visual, "Stone", MeshKit.Ball(0.075f, 10, 8), Mat.Glow, new Vector3(0f, 0f, 0.02f));
                ArtLibrary.SetGlow(stone.GetComponent<Renderer>(), color * 1.8f);
                MeshKit.Part(visual, "Chain", MeshKit.Cylinder(0.01f, 0.01f, 0.2f, 4), Mat.Gold, new Vector3(0f, 0.11f, 0f));
            }

            var beam = MeshKit.Part(transform, "Beam", MeshKit.Cylinder(0.018f, 0.006f, 2.4f, 6), Mat.Glow, Vector3.up * 0.7f);
            ArtLibrary.SetGlow(beam.GetComponent<Renderer>(), color * 0.6f);
            beam.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var light = new GameObject("Glow").AddComponent<Light>();
            light.transform.SetParent(visual, false);
            light.type = LightType.Point;
            light.color = color;
            light.range = 3f;
            light.intensity = 2f;
            light.shadows = LightShadows.None;
        }
    }
}
