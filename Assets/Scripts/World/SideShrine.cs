using System.Collections;
using System.Collections.Generic;
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
    /// The special side rooms (2026-10-20, after The Binding of Isaac's item rooms, beggars and
    /// challenge rooms). One networked kind per room:
    /// <list type="bullet">
    /// <item><b>Pedestal</b> (the Treasury): three relics on pedestals. Every player claims ONE.</item>
    /// <item><b>Beggar</b>: feed him gold; each coin raises the odds he pays out (a relic, potions,
    ///   a Sigil or a fat purse), then he is gone.</item>
    /// <item><b>Bell</b> (the Proving Room): ring it and two waves of the dead pour in; clear them and
    ///   everyone gets a piece of gear, gold and a Sigil.</item>
    /// <item><b>Whetstone</b> (the Armoury): every player sharpens their weapon once: a free temper
    ///   (or embers, at a path choice or the cap).</item>
    /// <item><b>Mirror</b>: a dark bargain. A relic and gold now, but your reflection keeps a fifth of
    ///   your health until you leave the floor.</item>
    /// </list>
    /// </summary>
    public class SideShrine : Interactable
    {
        public enum Kind : byte { Pedestal = 0, Beggar = 1, Bell = 2, Whetstone = 3, Mirror = 4 }

        public readonly NetworkVariable<Kind> Type = new NetworkVariable<Kind>();
        /// <summary>The relic on a pedestal.</summary>
        public readonly NetworkVariable<int> Choice = new NetworkVariable<int>();
        /// <summary>Pedestals of one treasury share a group; each player claims one per group.</summary>
        public readonly NetworkVariable<int> Group = new NetworkVariable<int>();
        /// <summary>Players (by client id bit) who have used this (or this group, for pedestals).</summary>
        public readonly NetworkVariable<byte> Used = new NetworkVariable<byte>();
        public readonly NetworkVariable<bool> Spent = new NetworkVariable<bool>();
        /// <summary>Beggar: coins given so far.</summary>
        public readonly NetworkVariable<byte> Coins = new NetworkVariable<byte>();

        public static readonly List<SideShrine> All = new List<SideShrine>();
        private Transform gem;
        private Light glow;
        private float spin;

        public override float Range => Type.Value == Kind.Bell ? 3.2f : 2.6f;

        private static byte Bit(PlayerProgress who) => (byte)(1 << (int)(who.OwnerClientId % 8));
        private bool HasUsed(PlayerProgress who) => (Used.Value & Bit(who)) != 0;
        private int Tier => Mathf.RoundToInt(DungeonDirector.Tier(DungeonDirector.Layout != null ? DungeonDirector.Layout.Depth : 0));
        public int BeggarPrice => 15 + 6 * Tier;

        public override string Prompt(PlayerProgress who)
        {
            if (Spent.Value) return null;
            switch (Type.Value)
            {
                case Kind.Pedestal:
                    return HasUsed(who) ? "You have chosen your treasure" : "Take " + Relics.Name((Relic)Choice.Value) + "  (one relic each: the others stay for your friends)";
                case Kind.Beggar:
                    return "Give the beggar " + BeggarPrice + " gold" + (who.Gold.Value < BeggarPrice ? "  (you have " + who.Gold.Value + ")" : "  (he may give something back)");
                case Kind.Bell:
                    return "Ring the Proving Bell  (two waves of the dead; a reward for everyone)";
                case Kind.Whetstone:
                    return HasUsed(who) ? "You have sharpened your weapon here" : "Sharpen your weapon on the whetstone  (a free temper)";
                case Kind.Mirror:
                    return HasUsed(who) ? "Your reflection has what it wanted" : "Touch the dark mirror  (a relic and gold; it keeps a fifth of your health this floor)";
            }
            return null;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            All.Add(this);
            BuildVisual();
            Type.OnValueChanged += (_, __) => Rebuild();
            Choice.OnValueChanged += (_, __) => Rebuild();
            Used.OnValueChanged += (_, __) => Refresh();
            Spent.OnValueChanged += (_, __) => Refresh();
            Refresh();
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);
            base.OnNetworkDespawn();
        }

        protected override void ServerUse(PlayerProgress who)
        {
            if (Spent.Value) return;
            switch (Type.Value)
            {
                case Kind.Pedestal: ServerPedestal(who); break;
                case Kind.Beggar: ServerBeggar(who); break;
                case Kind.Bell: StartCoroutine(ServerProving()); break;
                case Kind.Whetstone:
                    if (HasUsed(who)) return;
                    Used.Value |= Bit(who);
                    if (!who.ServerFreeTemper()) who.ServerAddEmbers(30 + 10 * Tier);
                    FxRpc(0, who.name);
                    break;
                case Kind.Mirror:
                    if (HasUsed(who)) return;
                    Used.Value |= Bit(who);
                    who.ServerSetMirrorCursed(true);
                    for (int tries = 0; tries < 30; tries++) { var r = Relics.Random(); if (!who.HasRelic(r)) { who.ServerAddRelic(r); break; } }
                    who.ServerAddGold(80 + 30 * Tier);
                    FxRpc(1, who.name);
                    break;
            }
        }

        private void ServerPedestal(PlayerProgress who)
        {
            if (HasUsed(who)) return;
            var relic = (Relic)Choice.Value;
            if (who.HasRelic(relic)) { RefuseRpc(who.OwnerClientId, "You already carry " + Relics.Name(relic)); return; }
            who.ServerAddRelic(relic);
            foreach (var s in All) if (s != null && s.Type.Value == Kind.Pedestal && s.Group.Value == Group.Value) s.Used.Value |= Bit(who);
            FxRpc(2, who.name);
        }

        private void ServerBeggar(PlayerProgress who)
        {
            int price = BeggarPrice;
            if (who.Gold.Value < price) { RefuseRpc(who.OwnerClientId, "Not enough gold"); return; }
            who.ServerAddGold(-price);
            Coins.Value++;
            // Each coin raises the odds: a quarter at first, then +12% a coin.
            float chance = 0.25f + 0.12f * (Coins.Value - 1);
            if (Random.value > chance) { FxRpc(3, who.name); return; }
            Spent.Value = true;
            var d = DungeonDirector.Instance;
            var front = transform.position + transform.forward * 1.2f + Vector3.up * 0.4f;
            float roll = Random.value;
            if (roll < 0.25f) d?.ServerDropLoot(front, LootKind.Relic, (int)Relics.Random());
            else if (roll < 0.55f) { d?.ServerDropLoot(front, LootKind.PotionHP, 2); d?.ServerDropLoot(front + Vector3.right * 0.6f, LootKind.PotionMP, 1); }
            else if (roll < 0.72f) d?.ServerDropLoot(front, LootKind.Sigil, 1);
            else for (int i = 0; i < 4; i++) d?.ServerDropLoot(front + Quaternion.Euler(0f, i * 90f, 0f) * Vector3.forward * 0.8f, LootKind.Gold, price * 2);
            FxRpc(4, who.name);
        }

        private IEnumerator ServerProving()
        {
            Spent.Value = true;
            FxRpc(5, "");
            var d = DungeonDirector.Instance;
            var room = DungeonDirector.Layout?.RoomAt(transform.position, 0.5f);
            var center = room != null ? room.Center : transform.position;
            for (int wave = 0; wave < 2 && d != null; wave++)
            {
                yield return new WaitForSeconds(wave == 0 ? 1.2f : 0.5f);
                var raised = d.ServerSpawnPackList(center, 3 + wave, wave == 1);
                ProvingWaveRpc(wave + 1);
                float until = Time.time + 75f;
                while (Time.time < until && raised.Exists(b => b != null && !b.IsDead)) yield return new WaitForSeconds(0.4f);
            }
            if (d == null) yield break;
            // The reward: a piece of gear for everyone, a Sigil and a purse on the floor.
            int depth = DungeonDirector.Layout != null ? DungeonDirector.Layout.Depth : 0;
            var rollable = Gear.RollableOf(Random.value < 0.5f ? GearSlot.Armor : GearSlot.Charm);
            foreach (var p in PlayerLife.All)
            {
                if (p == null) continue;
                var item = new GearItem(rollable[Random.Range(0, rollable.Length)], Random.value < 0.4f ? 2 : 1, depth);
                GearDrop.ServerSpawn(item, p.OwnerClientId, center + Random.insideUnitSphere.WithY0() * 1.5f);
            }
            d.ServerDropLoot(center + Vector3.forward, LootKind.Sigil, 1);
            for (int i = 0; i < 5; i++) d.ServerDropLoot(center + Quaternion.Euler(0f, i * 72f, 0f) * Vector3.forward * 1.6f, LootKind.Gold, 20 + 10 * Tier);
            FxRpc(6, "");
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ProvingWaveRpc(int wave) => UI.GameHud.ShowTitleCard("THE PROVING  " + wave + " / 2", wave == 1 ? "Hold the room" : "The last of them", new Color(0.95f, 0.75f, 0.4f));

        [Rpc(SendTo.ClientsAndHost)]
        private void RefuseRpc(ulong client, string why)
        {
            if (NetworkManager.Singleton.LocalClientId == client) UI.GameHud.Feed(why, new Color(0.85f, 0.7f, 0.6f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void FxRpc(byte what, string who)
        {
            var at = transform.position + Vector3.up * 1.2f;
            switch (what)
            {
                case 0: Sfx.Play(Sound.Block, at, 1f, 0.7f); Vfx.Embers(at, 18, new Color(1f, 0.7f, 0.35f), 5f, 0.04f, 0.5f); UI.GameHud.Feed(who + " sharpens their weapon on the whetstone", LootInfo.EmberColor); break;
                case 1: Sfx.Play(Sound.Whisper, at, 1f, 0.5f); Vfx.Flash(at, new Color(0.5f, 0.2f, 0.8f), 4f, 8f, 0.5f); UI.GameHud.Feed(who + "'s reflection smiles back. It keeps a fifth of their health this floor.", new Color(0.75f, 0.5f, 1f)); break;
                case 2: Sfx.Play(Sound.Relic, at, 1f, 1f); Vfx.Embers(at, 14, new Color(1f, 0.9f, 0.6f), 3f, 0.05f, 0.7f); UI.GameHud.Feed(who + " takes a treasure from the pedestals", new Color(1f, 0.9f, 0.6f)); break;
                case 3: Sfx.Play(Sound.Coins, at, 0.8f, 1f); UI.GameHud.ShowCombo(at + Vector3.up * 0.5f, "\"Bless you...\"", new Color(0.85f, 0.8f, 0.65f)); break;
                case 4: Sfx.Play(Sound.Relic, at, 1f, 0.8f); Sfx.Play(Sound.Coins, at, 1f, 0.8f); UI.GameHud.ShowTitleCard("THE BEGGAR PAYS YOU BACK", "and is gone", new Color(0.95f, 0.85f, 0.55f)); break;
                case 5: Sfx.Play(Sound.Slam, at, 1f, 0.45f); Sfx.Play(Sound.Summon, at, 1f, 0.6f); break;
                case 6: Sfx.Play(Sound.Relic, at, 1f, 0.9f); UI.GameHud.ShowTitleCard("PROVEN", "Gear for everyone, a Sigil and gold", new Color(1f, 0.85f, 0.45f)); break;
            }
        }

        private void Refresh()
        {
            bool mine = NetworkPlayer.Local != null && NetworkPlayer.Local.TryGetComponent<PlayerProgress>(out var me) && HasUsed(me);
            bool dim = Spent.Value || (Type.Value != Kind.Beggar && Type.Value != Kind.Bell && mine);
            if (gem != null) gem.gameObject.SetActive(!dim || Type.Value == Kind.Whetstone);
            if (glow != null) glow.intensity = dim ? 0.3f : (Type.Value == Kind.Pedestal ? 3.4f : 2.4f);
            if (Spent.Value && Type.Value == Kind.Beggar)
                if (vis != null) foreach (Transform c in vis) if (c.name.StartsWith("Beggar")) c.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (gem == null) return;
            spin += Time.deltaTime;
            if (Type.Value == Kind.Pedestal)
            {
                gem.localPosition = new Vector3(0f, 1.45f + Mathf.Sin(spin * 2f) * 0.06f, 0f);
                gem.localRotation = Quaternion.Euler(0f, spin * 60f, 0f);
            }
            else if (Type.Value == Kind.Mirror) gem.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(spin) * 2f);
        }

        private Light AddLight(Color c, float range, float y)
        {
            var l = new GameObject("ShrineLight").AddComponent<Light>();
            l.transform.SetParent(vis, false);
            l.transform.localPosition = Vector3.up * y;
            l.type = LightType.Point;
            l.color = c;
            l.range = range;
            l.intensity = 2.2f;
            l.shadows = LightShadows.None;
            return l;
        }

        private Transform vis;

        /// <summary>Any peer: (re)build the look for the current kind (values arrive just after spawn).</summary>
        private void Rebuild()
        {
            if (vis != null) Destroy(vis.gameObject);
            BuildVisual();
            Refresh();
        }

        private void BuildVisual()
        {
            vis = new GameObject("Visual").transform;
            vis.SetParent(transform, false);
            switch (Type.Value)
            {
                case Kind.Pedestal:
                {
                    MeshKit.Part(vis, "Plinth", MeshKit.Lathe("pedestal", new[]
                    {
                        new Vector2(0.42f, 0f), new Vector2(0.42f, 0.12f), new Vector2(0.24f, 0.2f), new Vector2(0.2f, 0.9f), new Vector2(0.34f, 1f), new Vector2(0.34f, 1.08f), new Vector2(0f, 1.08f),
                    }, 10), Mat.Marble, Vector3.zero);
                    var c = Relics.GemColor((Relic)Choice.Value);
                    gem = MeshKit.Pivot(vis, "Gem", Vector3.up * 1.45f);
                    var g = MeshKit.Part(gem, "Stone", MeshKit.Lathe("relicGem", new[] { new Vector2(0f, -0.14f), new Vector2(0.1f, 0f), new Vector2(0f, 0.16f) }, 6), Mat.Glow, Vector3.zero, Vector3.zero, Vector3.one * 1.5f);
                    ArtLibrary.SetGlow(g.GetComponent<Renderer>(), c * 1.4f);
                    glow = AddLight(c, 6.5f, 1.6f);
                    break;
                }
                case Kind.Beggar:
                {
                    // A hunched, hooded figure on a rag, a bowl in front of him.
                    var rag = MeshKit.Part(vis, "BeggarRag", MeshKit.Box(new Vector3(1.4f, 0.03f, 1f)), Mat.Cloth, new Vector3(0f, 0.015f, 0f));
                    ArtLibrary.Tint(rag.GetComponent<Renderer>(), new Color(0.3f, 0.24f, 0.18f));
                    var body = MeshKit.Part(vis, "BeggarBody", MeshKit.Lathe("beggarBody", new[] { new Vector2(0.42f, 0f), new Vector2(0.38f, 0.35f), new Vector2(0.26f, 0.7f), new Vector2(0.12f, 0.85f), new Vector2(0f, 0.88f) }, 9), Mat.Cloth, Vector3.zero, new Vector3(12f, 0f, 0f));
                    ArtLibrary.Tint(body.GetComponent<Renderer>(), new Color(0.22f, 0.19f, 0.16f));
                    MeshKit.Part(vis, "BeggarFace", MeshKit.Ball(0.13f, 8, 6), Mat.Dark, new Vector3(0f, 0.8f, 0.14f));
                    foreach (float x in new[] { -0.04f, 0.04f })
                    {
                        var eye = MeshKit.Part(vis, "BeggarEye", MeshKit.Ball(0.014f, 5, 3), Mat.Glow, new Vector3(x, 0.82f, 0.25f));
                        ArtLibrary.SetGlow(eye.GetComponent<Renderer>(), new Color(1f, 0.8f, 0.45f) * 0.8f);
                    }
                    MeshKit.Part(vis, "BeggarBowl", MeshKit.Lathe("bowl", new[] { new Vector2(0f, 0f), new Vector2(0.14f, 0.02f), new Vector2(0.17f, 0.09f) }, 10, false), Mat.Wood, new Vector3(0f, 0.02f, 0.55f));
                    gem = MeshKit.Pivot(vis, "BeggarCoin", new Vector3(0.03f, 0.06f, 0.55f));
                    MeshKit.Part(gem, "Coin", MeshKit.Cylinder(0.035f, 0.035f, 0.01f, 10), Mat.Gold, Vector3.zero);
                    glow = AddLight(new Color(1f, 0.7f, 0.4f), 4f, 1.4f);
                    break;
                }
                case Kind.Bell:
                {
                    // A bronze bell hung from a black timber frame.
                    foreach (float x in new[] { -0.9f, 0.9f }) MeshKit.Part(vis, "Post", MeshKit.Box(new Vector3(0.18f, 2.6f, 0.18f)), Mat.Wood, new Vector3(x, 1.3f, 0f));
                    MeshKit.Part(vis, "Beam", MeshKit.Box(new Vector3(2.1f, 0.2f, 0.22f)), Mat.Wood, new Vector3(0f, 2.55f, 0f));
                    gem = MeshKit.Pivot(vis, "Bell", new Vector3(0f, 2.45f, 0f));
                    MeshKit.Part(gem, "Bronze", MeshKit.Lathe("bell", new[] { new Vector2(0f, 0f), new Vector2(0.12f, -0.02f), new Vector2(0.2f, -0.3f), new Vector2(0.34f, -0.7f), new Vector2(0.4f, -0.78f), new Vector2(0.36f, -0.8f) }, 14), Mat.Gold, Vector3.zero);
                    MeshKit.Part(gem, "Clapper", MeshKit.Ball(0.07f, 6, 4), Mat.Steel, new Vector3(0f, -0.68f, 0f));
                    glow = AddLight(new Color(1f, 0.75f, 0.4f), 6f, 2f);
                    break;
                }
                case Kind.Whetstone:
                {
                    // A treadle grindstone and a rack of old blades.
                    MeshKit.Part(vis, "Frame", MeshKit.Box(new Vector3(1.1f, 0.7f, 0.5f)), Mat.Wood, new Vector3(0f, 0.35f, 0f));
                    gem = MeshKit.Pivot(vis, "Stone", new Vector3(0f, 0.95f, 0f));
                    MeshKit.Part(gem, "Wheel", MeshKit.Cylinder(0.42f, 0.42f, 0.16f, 16), Mat.StoneWall, Vector3.zero, new Vector3(0f, 0f, 90f));
                    for (int i = 0; i < 4; i++)
                        MeshKit.Part(vis, "OldBlade", MeshKit.Box(new Vector3(0.06f, 1f, 0.015f)), Mat.Steel, new Vector3(-0.9f + i * 0.12f, 0.6f, -0.3f), new Vector3(0f, 0f, 8f - i * 4f));
                    glow = AddLight(new Color(1f, 0.6f, 0.3f), 5f, 1.6f);
                    break;
                }
                case Kind.Mirror:
                {
                    // A tall black-framed mirror whose glass shows a violet nothing.
                    MeshKit.Part(vis, "Frame", MeshKit.Box(new Vector3(1.1f, 2.2f, 0.12f)), Mat.Dark, new Vector3(0f, 1.25f, 0f));
                    gem = MeshKit.Pivot(vis, "Glass", new Vector3(0f, 1.25f, 0.07f));
                    var glass = MeshKit.Part(gem, "Pane", MeshKit.Box(new Vector3(0.9f, 1.95f, 0.02f)), Mat.Glow, Vector3.zero);
                    ArtLibrary.SetGlow(glass.GetComponent<Renderer>(), new Color(0.35f, 0.12f, 0.55f) * 0.8f);
                    MeshKit.Part(vis, "Crest", MeshKit.Lathe("mirrorCrest", new[] { new Vector2(0.18f, 0f), new Vector2(0f, 0.3f) }, 4), Mat.Gold, new Vector3(0f, 2.4f, 0f));
                    glow = AddLight(new Color(0.6f, 0.3f, 1f), 5f, 1.4f);
                    break;
                }
            }
            foreach (var r in GetComponentsInChildren<Renderer>()) if (r.sharedMaterial != null && r.sharedMaterial.name.Contains("Glow")) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Tests: use it as this player.</summary>
        public void ServerDebugUse(PlayerProgress who) => ServerUse(who);
    }

    internal static class SideShrineVectors
    {
        public static Vector3 WithY0(this Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
