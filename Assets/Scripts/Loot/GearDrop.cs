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

        /// <summary>
        /// <see cref="For"/> on a piece a player put down out of their pack (play-test 2026-11-01: "items should be able
        /// to be dropped to other players"): everyone sees it, and whoever takes it first has it.
        /// </summary>
        public const ulong Anyone = ulong.MaxValue;
        public bool Shared => For.Value == Anyone;
        private static bool ForMe(ulong forClient) => forClient == Anyone || (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == forClient);
        /// <summary>Is this piece on offer to the local player?</summary>
        public bool ForLocal => ForMe(For.Value);

        private Transform visual;
        private float phase;
        private bool fanfarePlayed;
        private Transform legendRing;
        private float lift, nextWaterCheck;

        public override float Range => 2.2f;

        private bool Mine(PlayerProgress who) => who != null && (Shared || who.OwnerClientId == For.Value);

        public override string Prompt(PlayerProgress who)
        {
            if (!Mine(who) || Item.Value.IsEmpty) return null;
            string from = Shared ? "  (left by a friend)" : "";
            return who.BagFull ? Gear.Name(Item.Value) + "  (pack full: wear or drop something, I)" : "Take " + Gear.Name(Item.Value) + from + " (into your pack, I to wear)";
        }

        protected override void ServerUse(PlayerProgress who)
        {
            // An empty slot puts it straight on (the autoplay run, 2026-10-21: a new player picked up a coat
            // and a charm and still wore nothing). Otherwise into the pack: swap from the inventory (I).
            var item = Item.Value;
            if (who.Equipped(Gear.SlotOf((GearId)item.Id)).IsEmpty)
            {
                who.ServerEquip(item);
                EquipFxRpc(Gear.Name(item), who.OwnerClientId, true);
                NetworkObject.Despawn();
                return;
            }
            if (!who.ServerStow(item)) return;
            // Better than what is worn: say so, or a Legendary sits forgotten in the pack.
            var worn = who.Equipped(Gear.SlotOf((GearId)item.Id));
            EquipFxRpc(Gear.Name(item), who.OwnerClientId, false, item.Quality > worn.Quality);
            NetworkObject.Despawn();
        }

        /// <summary>Owner → server: put this piece in your pack without wearing it.</summary>
        [Rpc(SendTo.Server)]
        public void StowRpc(NetworkObjectReference user)
        {
            if (!user.TryGet(out var obj) || !obj.TryGetComponent<PlayerProgress>(out var who) || !Mine(who)) return;
            if (Vector3.Distance(obj.transform.position, transform.position) > Range + 1f) return;
            if (!who.ServerStow(Item.Value)) return;
            EquipFxRpc(Gear.Name(Item.Value), who.OwnerClientId, false);
            NetworkObject.Despawn();
        }

        /// <summary>Owner → server: break this piece down into embers.</summary>
        [Rpc(SendTo.Server)]
        public void ScrapRpc(NetworkObjectReference user)
        {
            if (!user.TryGet(out var obj) || !obj.TryGetComponent<PlayerProgress>(out var who) || !Mine(who)) return;
            if (Vector3.Distance(obj.transform.position, transform.position) > Range + 1f) return;
            if (World.Anvil.Near(obj.transform.position) == null) return; // smith's work
            who.ServerAddEmbers(Gear.ScrapValue(Item.Value));
            who.ServerAddPart(Progression.Part.Iron, 1 + Item.Value.Quality);
            ScrapFxRpc();
            NetworkObject.Despawn();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void EquipFxRpc(string name, ulong taker, bool worn, bool better = false)
        {
            Sfx.Play(Sound.Gear, transform.position + Vector3.up * 0.5f, 0.9f);
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.LocalClientId == taker)
                UI.GameHud.Pickup("gear" + name, 1, " " + name + (worn ? "  (now wearing)" : better ? "  (in your pack - better than yours: I to wear it)" : "  (in your pack)"), Gear.QualityColor(Item.Value.Quality));
        }

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
                var pos = World.DropSpots.Settle(at, at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.1f);
                var legendary = boss != null ? Gear.LegendaryOf(boss) : GearId.None;
                var item = legendary != GearId.None && Random.value < 0.35f ? new GearItem(legendary, 3, depth) : Gear.Roll(depth, luck);
                ServerSpawn(item, client, pos);
            }
        }

        public static GearDrop ServerSpawn(GearItem item, ulong forClient, Vector3 at)
        {
            var obj = Object.Instantiate(Prefab, at, Quaternion.identity);
            var drop = obj.GetComponent<GearDrop>();
            drop.Item.Value = item;
            drop.For.Value = forClient;
            // Only its player is told it exists (a piece put down for the party: everyone). The host still has
            // it - it is the server - so the visual is also gated below.
            obj.CheckObjectVisibility = id => forClient == Anyone || id == forClient;
            obj.Spawn(destroyWithScene: true);
            // The white-bag moment: the whole party hears who got lucky.
            if (item.Quality >= 3 && forClient != Anyone && DungeonDirector.Instance != null)
            {
                string who = "Someone";
                if (NetworkManager.Singleton.ConnectedClients.TryGetValue(forClient, out var cl) && cl.PlayerObject != null) who = cl.PlayerObject.name;
                DungeonDirector.Instance.ServerFeedAll(who + " found a LEGENDARY: " + Gear.BaseName(item.Kind) + "!", Gear.QualityColor(3));
            }
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
            // Under water it floats on the surface, where it can be seen.
            if (Time.time >= nextWaterCheck)
            {
                nextWaterCheck = Time.time + 0.4f;
                var surface = World.DungeonDirector.WaterSurface(transform.position);
                lift = surface.HasValue ? Mathf.Max(0f, surface.Value + 0.3f - (transform.position.y + 0.45f)) : 0f;
            }
            visual.localPosition = new Vector3(0f, 0.45f + Mathf.Sin(phase * 2f) * 0.06f + lift, 0f);
            visual.localRotation = Quaternion.Euler(0f, phase * 50f, 0f);
            if (legendRing != null)
            {
                float k = 1f + 0.12f * Mathf.Sin(phase * 3f);
                legendRing.localScale = new Vector3(k, 1f, k);
            }
        }

        private void BuildVisual()
        {
            if (visual != null) Destroy(visual.gameObject);
            visual = null;
            var beamOld = transform.Find("Beam");
            if (beamOld != null) Destroy(beamOld.gameObject);
            if (!ForMe(For.Value) || Item.Value.IsEmpty) return;

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

            bool legendary = item.Quality >= 3;
            // A Legendary throws a pillar of light you can see from the next room, and a ring on the floor.
            var beam = legendary
                ? MeshKit.Part(transform, "Beam", MeshKit.Cylinder(0.07f, 0.02f, 9f, 8), Mat.Glow, Vector3.up * 0.2f)
                : MeshKit.Part(transform, "Beam", MeshKit.Cylinder(0.018f, 0.006f, 2.4f, 6), Mat.Glow, Vector3.up * 0.7f);
            ArtLibrary.SetGlow(beam.GetComponent<Renderer>(), color * (legendary ? 1.3f : 0.6f));
            beam.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            legendRing = null;
            if (legendary)
            {
                // A child of the beam, so rebuilding the visual removes it too.
                var ring = MeshKit.Part(beam.transform, "BeamRing", MeshKit.Cylinder(0.75f, 0.75f, 0.015f, 24), Mat.Glow, Vector3.down * 0.15f);
                ArtLibrary.SetGlow(ring.GetComponent<Renderer>(), color * 0.5f);
                ring.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                legendRing = ring.transform;
                if (!fanfarePlayed)
                {
                    fanfarePlayed = true;
                    Sfx.Play2D(Sound.Relic, 1f);
                    Sfx.Play(Sound.Revive, transform.position + Vector3.up, 0.9f, 1.25f);
                    UI.GameHud.ShowTitleCard("LEGENDARY", Gear.BaseName(item.Kind));
                }
            }

            var light = new GameObject("Glow").AddComponent<Light>();
            light.transform.SetParent(visual, false);
            light.type = LightType.Point;
            light.color = color;
            light.range = legendary ? 6f : 3f;
            light.intensity = legendary ? 3.5f : 2f;
            light.shadows = LightShadows.None;
        }
    }
}
