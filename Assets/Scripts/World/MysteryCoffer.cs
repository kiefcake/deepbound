using Deepbound.Art;
using Deepbound.Loot;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// THE LOCKSMITH'S COFFER, in the gambling tent (reworked 2026-11-08, the user: "work on every casino game except
    /// blackjack"). Pay the price and the coffer is yours to crack: three padlocks hang on it. F picks the next lock -
    /// each one opened makes the prize better, each pick is riskier (85% / 70% / 55%), and a snapped pick jams the coffer
    /// and you get NOTHING. Step back from it (or wait) and you take what the locks you opened are worth:
    /// none = a handful of embers, one = a rune or gear, two = a Sigil or FINE gear, all three = a RELIC.
    /// One player at a time; everyone sees the locks fall.
    /// </summary>
    public class MysteryCoffer : Interactable
    {
        public const ulong NoOne = ulong.MaxValue;
        public static readonly float[] PickChance = { 0.85f, 0.7f, 0.55f };
        public const float IdleTake = 25f;

        public readonly NetworkVariable<ulong> Opener = new NetworkVariable<ulong>(NoOne);
        public readonly NetworkVariable<byte> Locks = new NetworkVariable<byte>();

        private Transform lid;
        private readonly Transform[] padlocks = new Transform[3];
        private float lidOpen, openUntil, shakeUntil;
        private float lastAction, busyUntil;

        public static int Price => 120 + 40 * DungeonDirector.GearDepth;
        public override float Range => 2.4f;

        public static string TierPrize(int locks) => locks switch
        {
            0 => "a few embers",
            1 => "a rune or a piece of gear",
            2 => "a Sigil or FINE gear",
            _ => "A RELIC",
        };

        public override string Prompt(PlayerProgress who)
        {
            if (Opener.Value == NoOne)
                return who.Gold.Value < Price ? "Locksmith's Coffer: " + Price + " gold (you have " + who.Gold.Value + ")"
                    : "Buy the Locksmith's Coffer  -  " + Price + " gold  (three locks: each one opened betters the prize)";
            if (Opener.Value != who.OwnerClientId || Locks.Value >= 3) return null;
            int next = Locks.Value;
            return "Pick lock " + (next + 1) + " of 3  (" + Mathf.RoundToInt(PickChance[next] * 100f) + "% - a snap loses it all)   ·   step back to take: " + TierPrize(next);
        }

        protected override void ServerUse(PlayerProgress who)
        {
            if (Time.time < busyUntil) return;
            if (Opener.Value == NoOne)
            {
                if (!who.ServerSpendGold(Price)) return;
                Opener.Value = who.OwnerClientId;
                Locks.Value = 0;
                lastAction = Time.time;
                busyUntil = Time.time + 0.5f;
                BoughtRpc(who.name);
                return;
            }
            if (Opener.Value != who.OwnerClientId || Locks.Value >= 3) return;
            busyUntil = Time.time + 0.7f;
            lastAction = Time.time;
            if (Random.value < PickChance[Locks.Value]) ServerPicked(who, true);
            else ServerPicked(who, false);
        }

        /// <summary>Server: a pick lands (or snaps). Public for tests (forced outcome).</summary>
        public void ServerPicked(PlayerProgress who, bool opened)
        {
            if (opened)
            {
                Locks.Value++;
                PickRpc((byte)(Locks.Value - 1), true);
                if (Locks.Value >= 3) ServerTake(who);
            }
            else
            {
                PickRpc(Locks.Value, false);
                SnappedRpc(who.name, Locks.Value);
                Opener.Value = NoOne;
                Locks.Value = 0;
            }
        }

        /// <summary>Server: the opener takes what the opened locks are worth; the coffer is free again.</summary>
        public void ServerTake(PlayerProgress who)
        {
            int locks = Locks.Value;
            Opener.Value = NoOne;
            Locks.Value = 0;
            if (who == null) return;
            int depth = DungeonDirector.GearDepth;
            var at = transform.position + transform.forward * 1.1f + Vector3.up * 0.05f;
            var dir = DungeonDirector.Instance;
            string prize;
            switch (locks)
            {
                case 0:
                    dir.ServerDropLoot(at, LootKind.Embers, Random.Range(15, 31));
                    prize = "a few embers";
                    break;
                case 1:
                    if (Random.value < 0.55f) { dir.ServerDropLoot(at, LootKind.Rune, (int)Runes.Random()); prize = "a rune"; }
                    else { var item = Gear.Roll(depth, Random.value < 0.3f ? 1 : 0); GearDrop.ServerSpawn(item, who.OwnerClientId, at); prize = Gear.Name(item); }
                    break;
                case 2:
                    if (Random.value < 0.4f) { dir.ServerDropLoot(at, LootKind.Sigil, 1); prize = "a Sigil"; }
                    else { var item = Gear.Roll(depth, 1); GearDrop.ServerSpawn(item, who.OwnerClientId, at); prize = Gear.Name(item); }
                    break;
                default:
                    dir.ServerDropLoot(at, LootKind.Relic, (int)Relics.Random());
                    prize = "A RELIC";
                    break;
            }
            OpenFxRpc(who.name, prize, locks);
        }

        private void Update()
        {
            if (IsServer && Opener.Value != NoOne)
            {
                PlayerProgress opener = null;
                foreach (var p in Player.PlayerLife.All)
                    if (p != null && p.OwnerClientId == Opener.Value) opener = p.GetComponent<PlayerProgress>();
                if (opener == null) { Opener.Value = NoOne; Locks.Value = 0; }
                else if (Vector3.Distance(opener.transform.position, transform.position) > Range + 1.2f || Time.time - lastAction > IdleTake) ServerTake(opener);
            }
            lidOpen = Mathf.MoveTowards(lidOpen, Time.time < openUntil ? 1f : 0f, Time.deltaTime * 3f);
            if (lid != null)
            {
                float shake = Time.time < shakeUntil ? Mathf.Sin(Time.time * 70f) * 3f : 0f;
                lid.localRotation = Quaternion.Euler(-100f * lidOpen + shake, 0f, 0f);
            }
            for (int i = 0; i < 3; i++)
            {
                if (padlocks[i] == null) continue;
                bool open = Opener.Value != NoOne && i < Locks.Value;
                // An opened lock hangs from its hasp, swung down; a closed one sits straight.
                padlocks[i].localRotation = Quaternion.Slerp(padlocks[i].localRotation, Quaternion.Euler(open ? 70f : 0f, 0f, open ? 25f : 0f), Time.deltaTime * 10f);
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void BoughtRpc(string who)
        {
            Sfx.Play(Sound.Chips, transform.position + Vector3.up, 0.8f);
            UI.GameHud.Feed(who + " buys the Locksmith's Coffer...", new Color(0.85f, 0.7f, 1f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PickRpc(byte index, bool opened)
        {
            var at = padlocks[Mathf.Min(index, 2)] != null ? padlocks[Mathf.Min(index, 2)].position : transform.position + Vector3.up * 0.6f;
            if (opened)
            {
                Sfx.Play(Sound.UiClick, at, 0.8f, 0.8f);
                Vfx.Embers(at, 8, new Color(1f, 0.8f, 0.4f), 2f, 0.04f, 0.5f);
                UI.GameHud.ShowCombo(at + Vector3.up * 0.4f, "CLICK", new Color(1f, 0.85f, 0.45f));
            }
            else
            {
                shakeUntil = Time.time + 0.5f;
                Sfx.Play(Sound.Block, at, 1f, 0.7f);
                UI.GameHud.ShowCombo(at + Vector3.up * 0.4f, "SNAP!", new Color(1f, 0.4f, 0.35f));
            }
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SnappedRpc(string who, byte locks)
        {
            UI.GameHud.Feed(who + "'s pick snaps in the coffer" + (locks > 0 ? " after " + locks + " lock" + (locks > 1 ? "s" : "") : "") + ". It keeps everything.", new Color(1f, 0.45f, 0.4f));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void OpenFxRpc(string who, string prize, int locks)
        {
            openUntil = Time.time + 2f;
            Sfx.Play(Sound.Relic, transform.position + Vector3.up, 0.9f, locks >= 3 ? 0.8f : 1.2f);
            var gold = new Color(1f, 0.8f, 0.35f);
            Vfx.Embers(transform.position + Vector3.up * 0.8f, 10 + locks * 10, gold, 4f, 0.06f, 0.9f);
            UI.GameHud.Feed(who + " opens the coffer (" + locks + " lock" + (locks == 1 ? "" : "s") + "): " + prize + (locks >= 3 ? "!" : ""), gold);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            // An iron-bound black coffer on a little plinth, three padlocks on the front, a violet glow.
            MeshKit.Part(transform, "Plinth", MeshKit.Box(new Vector3(1.2f, 0.25f, 0.8f)), Mat.StoneWall, Vector3.up * 0.125f);
            MeshKit.Part(transform, "Body", MeshKit.Box(new Vector3(1f, 0.55f, 0.6f)), Mat.Dark, Vector3.up * 0.53f);
            foreach (float x in new[] { -0.35f, 0.35f })
                MeshKit.Part(transform, "Band", MeshKit.Box(new Vector3(0.07f, 0.57f, 0.62f)), Mat.Gold, new Vector3(x, 0.53f, 0f));
            lid = MeshKit.Pivot(transform, "Hinge", new Vector3(0f, 0.8f, -0.3f));
            MeshKit.Part(lid, "Lid", MeshKit.Box(new Vector3(1.02f, 0.18f, 0.62f)), Mat.Dark, new Vector3(0f, 0.09f, 0.3f));
            var gem = MeshKit.Part(lid, "Gem", MeshKit.Ball(0.07f, 8, 6), Mat.Glow, new Vector3(0f, 0.12f, 0.62f));
            ArtLibrary.SetGlow(gem.GetComponent<Renderer>(), new Color(0.75f, 0.45f, 1f) * 1.6f);
            for (int i = 0; i < 3; i++)
            {
                // Hasp on the body, the lock hanging from a pivot at its top.
                float x = (i - 1) * 0.26f;
                MeshKit.Part(transform, "Hasp" + i, MeshKit.Box(new Vector3(0.05f, 0.08f, 0.03f)), Mat.Steel, new Vector3(x, 0.74f, 0.31f));
                var pl = MeshKit.Pivot(transform, "Padlock" + i, new Vector3(x, 0.72f, 0.33f));
                MeshKit.Part(pl, "Shackle", MeshKit.Cylinder(0.03f, 0.03f, 0.06f, 8), Mat.Steel, new Vector3(0f, -0.04f, 0f));
                MeshKit.Part(pl, "Case", MeshKit.Box(new Vector3(0.1f, 0.1f, 0.05f)), Mat.Gold, new Vector3(0f, -0.1f, 0.01f));
                MeshKit.Part(pl, "Keyhole", MeshKit.Box(new Vector3(0.018f, 0.035f, 0.004f)), Mat.Dark, new Vector3(0f, -0.11f, 0.037f));
                padlocks[i] = pl;
            }
            var glow = new GameObject("Glow").AddComponent<Light>();
            glow.transform.SetParent(transform, false);
            glow.transform.localPosition = new Vector3(0f, 1.2f, 0.5f);
            glow.type = LightType.Point;
            glow.color = new Color(0.75f, 0.45f, 1f);
            glow.range = 3f;
            glow.intensity = 1.2f;
        }
    }
}
