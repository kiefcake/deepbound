using System;
using Deepbound.Combat;
using Deepbound.Enemies;
using Deepbound.Loot;
using Deepbound.Player;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>What a quest asks. Append only: synced.</summary>
    public enum Contract : byte
    {
        None, Slayer, ChampionHunt, Untouchable, TreasureHunt, SwiftBlade,
        // 2026-11-08: the camp folk's own work.
        Exorcist, EliteHunter, Salvage, Ironmonger, RoomSweep, GoldRun, UrnBreaker,
        // 2026-11-08 (the user: "way too easy ... be more creative"): things put on the floor for the quest.
        Cargo, Gather, Mark,
    }

    /// <summary>One line of the party's quest log (synced).</summary>
    public struct QuestSlot : IEquatable<QuestSlot>, INetworkSerializeByMemcpy
    {
        public byte Kind, Giver, State;
        public bool Special;
        public int Progress, Goal;
        /// <summary>Gather: finds picked up and held by the party, counted only once they ride the lift out.</summary>
        public byte Held;

        public Contract Contract => (Contract)Kind;
        public FolkId Who => (FolkId)Giver;
        public DungeonDirector.ContractStatus Status => (DungeonDirector.ContractStatus)State;

        public bool Equals(QuestSlot o) => Kind == o.Kind && Giver == o.Giver && State == o.State && Special == o.Special && Progress == o.Progress && Goal == o.Goal && Held == o.Held;
        public override bool Equals(object obj) => obj is QuestSlot o && Equals(o);
        public override int GetHashCode() => Kind | Giver << 8 | State << 16 | Progress << 20;
    }

    /// <summary>
    /// QUESTS (2026-11-08, the user: "they should give out quests that can be found on the quest board or in
    /// dialogue"; party-shared, three at once, favour and unique items). Every camp person who gives work offers
    /// one quest a camp visit (<see cref="OfferOf"/>, the same on every peer); the board shows three of those same
    /// offers. A taken quest rides down with the party, goes live when the next floor starts, and is settled by the
    /// time its guardian falls. Fulfilled: gold and embers for everyone (a Sigil for the hard ones), favour with the
    /// giver (a discount at their stall and new things to say), and from favour 2 their SPECIAL quest, which also
    /// gives every player that person's own relic once a run.
    /// </summary>
    public partial class DungeonDirector
    {
        public enum ContractStatus : byte { None, Taken, Active, Fulfilled, Failed }

        public const int MaxQuests = 3;
        /// <summary>Favour with a giver at which their special quest is offered (once a run).</summary>
        public const int SpecialFavour = 2;
        /// <summary>Price cut per favour level at their stall, up to <see cref="MaxFavourDiscount"/> levels.</summary>
        public const float FavourDiscount = 0.03f;
        public const int MaxFavourDiscount = 5;

        public NetworkList<QuestSlot> Quests = new NetworkList<QuestSlot>();
        /// <summary>Givers who gave out work this camp visit (bit per FolkId).</summary>
        public readonly NetworkVariable<ushort> QuestTakenMask = new NetworkVariable<ushort>();
        /// <summary>Givers whose relic the party had this run (bit per FolkId).</summary>
        public readonly NetworkVariable<ushort> GiftMask = new NetworkVariable<ushort>();
        /// <summary>Favour, 4 bits per FolkId (0..15), the host's save.</summary>
        public readonly NetworkVariable<ulong> FavourBits = new NetworkVariable<ulong>();
        private float questsStartedAt;
        private bool favourLoaded;

        /// <summary>Everyone who hands out work, in board order.</summary>
        public static readonly FolkId[] QuestGivers = { FolkId.Silas, FolkId.Hobb, FolkId.Aldric, FolkId.Vey, FolkId.Corwin, FolkId.Brann, FolkId.Ysolde, FolkId.Marta };

        public static bool GivesQuests(FolkId who) => Array.IndexOf(QuestGivers, who) >= 0;

        private static Contract[] PoolOf(FolkId who) => who switch
        {
            // Each person: their own errand on the floor (cargo or finds), their own mark to hunt, and one harder job of the old kind.
            FolkId.Silas => new[] { Contract.Cargo, Contract.Mark, Contract.Salvage },
            FolkId.Hobb => new[] { Contract.Cargo, Contract.Mark, Contract.Ironmonger },
            FolkId.Aldric => new[] { Contract.Gather, Contract.Mark, Contract.Exorcist },
            FolkId.Vey => new[] { Contract.Cargo, Contract.Mark, Contract.UrnBreaker },
            FolkId.Corwin => new[] { Contract.Cargo, Contract.Mark, Contract.SwiftBlade },
            FolkId.Brann => new[] { Contract.Gather, Contract.Mark, Contract.Untouchable },
            FolkId.Ysolde => new[] { Contract.Gather, Contract.Mark, Contract.TreasureHunt },
            FolkId.Marta => new[] { Contract.Cargo, Contract.Mark, Contract.GoldRun },
            _ => new[] { Contract.Slayer },
        };

        /// <summary>The relic a giver hands over for their special quest.</summary>
        public static Relic SignatureRelic(FolkId who) => who switch
        {
            FolkId.Silas => Relic.GildedSkull,
            FolkId.Hobb => Relic.WhettedFang,
            FolkId.Aldric => Relic.IronPsalter,
            FolkId.Vey => Relic.LongShadow,
            FolkId.Corwin => Relic.PilgrimsPurse,
            FolkId.Brann => Relic.WarlordsHorn,
            FolkId.Ysolde => Relic.EchoStone,
            _ => Relic.HeartOfTheDeep,
        };

        public static string ContractName(Contract c) => c switch
        {
            Contract.Slayer => "Slayer", Contract.ChampionHunt => "Champion Hunt", Contract.Untouchable => "Untouchable",
            Contract.TreasureHunt => "Treasure Hunt", Contract.SwiftBlade => "Swift Blade", Contract.Exorcist => "Exorcist",
            Contract.EliteHunter => "Elite Hunter", Contract.Salvage => "Salvage", Contract.Ironmonger => "Ironmonger",
            Contract.RoomSweep => "Clear the Road", Contract.GoldRun => "Cellar Money", Contract.UrnBreaker => "Grave Dust",
            Contract.Cargo => "Fetch and Carry", Contract.Gather => "Gathering", Contract.Mark => "A Named Mark",
            _ => "",
        };

        /// <summary>The task as the quest log and the panels say it (the new kinds name what and whose).</summary>
        public static string QuestTask(QuestSlot q) => q.Contract switch
        {
            Contract.Cargo => CargoTask(q.Who),
            Contract.Gather => GatherTask(q.Who, q.Goal),
            Contract.Mark => MarkTask(q.Who),
            _ => QuestTask(q.Contract, q.Goal),
        };

        /// <summary>The task, for a goal (counts in the text).</summary>
        public static string QuestTask(Contract c, int goal) => c switch
        {
            Contract.Slayer => "Slay " + goal + " of the dead",
            Contract.ChampionHunt => "Kill the floor's champion",
            Contract.Untouchable => "Clear the floor with nobody going down",
            Contract.TreasureHunt => "Smash open a secret wall",
            Contract.SwiftBlade => "Kill the floor's guardian before the far bell tolls",
            Contract.Exorcist => "Destroy " + goal + " casters (necromancers, wraiths, hexers, choristers)",
            Contract.EliteHunter => "Kill " + goal + " elites or champions",
            Contract.Salvage => "Open " + goal + " chests",
            Contract.Ironmonger => "Break " + goal + " heavies (brutes, shieldbearers, wights, plaguebearers, gargoyles)",
            Contract.RoomSweep => "Clear " + goal + " rooms",
            Contract.GoldRun => "Gather " + goal + " gold",
            Contract.UrnBreaker => "Break " + goal + " urns",
            _ => "",
        };

        public static bool HardContract(Contract c) => c == Contract.Untouchable || c == Contract.SwiftBlade || c == Contract.ChampionHunt || c == Contract.EliteHunter || c == Contract.Mark || c == Contract.Cargo;

        public static string QuestReward(QuestSlot q) => (HardContract(q.Contract) || q.Special ? "gold, embers and a Sigil each" : "gold and embers each")
            + (q.Special ? ", and " + Relics.Name(SignatureRelic(q.Who)) : "");

        private static int GoalFor(Contract c, int depth, bool special)
        {
            float t = Tier(depth), k = special ? 1.4f : 1f;
            return c switch
            {
                // Harder than the first pass (2026-11-08: "way too easy").
                Contract.Slayer => Mathf.RoundToInt((45 + 14 * t) * k),
                Contract.Exorcist => Mathf.RoundToInt((6 + 2 * t) * k),
                Contract.EliteHunter => Mathf.RoundToInt((3 + t * 0.6f) * k),
                Contract.Salvage => Mathf.RoundToInt(4 * k),
                Contract.Ironmonger => Mathf.RoundToInt((5 + 1.5f * t) * k),
                Contract.RoomSweep => Mathf.RoundToInt(6 * k),
                Contract.GoldRun => Mathf.RoundToInt((220 + 90 * t) * k / 10f) * 10,
                Contract.UrnBreaker => Mathf.RoundToInt((10 + 3 * t) * k),
                Contract.Gather => Mathf.RoundToInt((3 + (t >= 2f ? 1 : 0)) * k),
                _ => 1,
            };
        }

        /// <summary>Same numbers on every peer (not System.HashCode: that is seeded per process).</summary>
        private static uint Mix(uint h, uint v) { unchecked { h ^= v; h *= 16777619u; h ^= h >> 13; return h; } }

        /// <summary>What <paramref name="who"/> offers this camp visit.</summary>
        public QuestSlot OfferOf(FolkId who)
        {
            var pool = PoolOf(who);
            uint h = 2166136261u;
            h = Mix(h, (uint)(long)(RunStartedAt.Value * 10.0));
            h = Mix(h, (uint)NextDepth.Value * 7919u);
            h = Mix(h, (uint)who * 104729u + 17u);
            var kind = pool[(int)(h % (uint)pool.Length)];
            bool special = Favour(who) >= SpecialFavour && (GiftMask.Value & (1 << (int)who)) == 0;
            return new QuestSlot { Kind = (byte)kind, Giver = (byte)who, State = (byte)ContractStatus.None, Special = special, Goal = GoalFor(kind, NextDepth.Value, special) };
        }

        public int QuestIndexFrom(FolkId who)
        {
            for (int i = 0; i < Quests.Count; i++) if (Quests[i].Giver == (byte)who) return i;
            return -1;
        }

        /// <summary>Why <paramref name="who"/> cannot hand out work right now (null: they can).</summary>
        public string CannotTakeQuest(FolkId who)
        {
            if (!InLobby) return "Work is handed out at camp.";
            if (!GivesQuests(who)) return "They have no work.";
            if (QuestIndexFrom(who) >= 0) return "You are already on their job.";
            if ((QuestTakenMask.Value & (1 << (int)who)) != 0) return "That was all their work this visit.";
            if (Quests.Count >= MaxQuests) return "Three quests already: finish one first.";
            return null;
        }

        /// <summary>Server: the party takes <paramref name="who"/>'s offer (from the board or in conversation).</summary>
        public bool ServerTakeQuest(FolkId who, string takenBy)
        {
            EnsureFavourLoaded();
            if (CannotTakeQuest(who) != null) return false;
            var q = OfferOf(who);
            q.State = (byte)ContractStatus.Taken;
            Quests.Add(q);
            QuestTakenMask.Value |= (ushort)(1 << (int)who);
            QuestTakenRpc(takenBy, (byte)who, q.Kind);
            return true;
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void QuestTakenRpc(string by, byte who, byte kind)
        {
            Art.Sfx.Play2D(Art.Sound.PageTurn, 0.8f);
            UI.GameHud.Feed(by + " takes " + UI.Dialogue.NameOf((FolkId)who) + "'s quest: " + ContractName((Contract)kind), new Color(0.95f, 0.8f, 0.5f));
        }

        /// <summary>Server: a floor begins - taken quests go live.</summary>
        private void BeginContract(int depth)
        {
            for (int i = 0; i < Quests.Count; i++)
            {
                var q = Quests[i];
                if (q.Status != ContractStatus.Taken) continue;
                q.State = (byte)ContractStatus.Active;
                q.Progress = 0;
                Quests[i] = q;
            }
            questsStartedAt = Time.time;
            SpawnQuestObjects();
        }

        private void Advance(Contract kind, int amount = 1, FolkId? giver = null)
        {
            for (int i = 0; i < Quests.Count; i++)
            {
                var q = Quests[i];
                if (q.Status != ContractStatus.Active || q.Contract != kind) continue;
                if (giver.HasValue && q.Who != giver.Value) continue;
                q.Progress = Mathf.Min(q.Goal, q.Progress + amount);
                Quests[i] = q;
                if (q.Progress >= q.Goal) FulfilQuest(i);
            }
        }

        private void FailQuests(Contract kind)
        {
            for (int i = 0; i < Quests.Count; i++)
                if (Quests[i].Status == ContractStatus.Active && Quests[i].Contract == kind) FailQuest(i);
        }

        private static bool IsCaster(EnemyBrain b) => b is NecromancerBrain || b is WraithBrain || b is HexerBrain || b is ChoirAcolyteBrain || b is CantorBrain || b is BonePiperBrain || b is OrbweaverBrain;
        private static bool IsHeavy(EnemyBrain b) => b is BruteBrain || b is ShieldbearerBrain || b is FrostWightBrain || b is PlaguebearerBrain || b is GargoyleBrain;

        /// <summary>Server: called on every death on a floor (enemies and players).</summary>
        private void ContractOnKill(Health target)
        {
            if (Quests.Count == 0 || target == null) return;
            if (target.Team == Team.Players) { FailQuests(Contract.Untouchable); return; }
            if (target.Team != Team.Enemies) return;
            QuestMarkKilled(target);
            Advance(Contract.Slayer);
            if (!target.TryGetComponent<EnemyBrain>(out var b)) return;
            if (b.IsChampion) Advance(Contract.ChampionHunt);
            if (b.IsChampion || b.Elite.Value) Advance(Contract.EliteHunter);
            if (IsCaster(b)) Advance(Contract.Exorcist);
            if (IsHeavy(b)) Advance(Contract.Ironmonger);
        }

        /// <summary>Server: a secret wall was smashed open.</summary>
        public void ContractOnSecret() => Advance(Contract.TreasureHunt);
        /// <summary>Server: a chest was opened (not a mimic).</summary>
        public void QuestOnChest() => Advance(Contract.Salvage);
        /// <summary>Server: an urn broke.</summary>
        public void QuestOnUrn() => Advance(Contract.UrnBreaker);
        /// <summary>Server: a room's last enemy fell.</summary>
        public void QuestOnRoomClear() => Advance(Contract.RoomSweep);
        /// <summary>Server: gold picked up on a floor.</summary>
        public void QuestOnGold(int amount) { if (amount > 0) Advance(Contract.GoldRun, amount); }

        private void TickContract()
        {
            // Swift Blade: beat the bell (the Tallyman's warning, which comes later on a bigger floor).
            if (Quests.Count > 0 && Time.time - questsStartedAt > TallyWarnSeconds) FailQuests(Contract.SwiftBlade);
        }

        /// <summary>Server: the floor's guardian fell - settle what is still open.</summary>
        private void ContractOnFloorCleared()
        {
            for (int i = 0; i < Quests.Count; i++)
            {
                var q = Quests[i];
                if (q.Status != ContractStatus.Active) continue;
                if (q.Contract == Contract.Untouchable || q.Contract == Contract.SwiftBlade) FulfilQuest(i);
                // Cargo and finds are settled as the grave lift rises (they have to be carried out).
                else if (q.Contract == Contract.Cargo || q.Contract == Contract.Gather) continue;
                else FailQuest(i);
            }
        }

        private void FulfilQuest(int i)
        {
            var q = Quests[i];
            q.State = (byte)ContractStatus.Fulfilled;
            q.Progress = q.Goal;
            Quests[i] = q;
            int depth = Mathf.RoundToInt(Tier(Layout != null ? Layout.Depth : 0));
            float k = q.Special ? 1.5f : 1f;
            bool sigil = HardContract(q.Contract) || q.Special;
            var relic = SignatureRelic(q.Who);
            foreach (var p in PlayerLife.All)
            {
                if (p == null) continue;
                var prog = p.GetComponent<PlayerProgress>();
                prog.ServerAddGold(Difficulty.Gold(Mathf.RoundToInt((50 + 40 * depth) * k)));
                ServerDropEmbers(p.transform.position + Vector3.up * 0.5f, Mathf.RoundToInt((8 + 3 * depth) * k));
                if (sigil) ServerDropLoot(p.transform.position + p.transform.forward + Vector3.up * 0.5f, LootKind.Sigil, 1);
                if (q.Special) prog.ServerAddRelic(relic);
            }
            if (q.Special) GiftMask.Value |= (ushort)(1 << q.Giver);
            ServerAddFavour(q.Who);
            TitleCardRpc("QUEST COMPLETE", UI.Dialogue.NameOf(q.Who) + ": " + ContractName(q.Contract) + "  -  " + QuestReward(q));
        }

        private void FailQuest(int i)
        {
            var q = Quests[i];
            q.State = (byte)ContractStatus.Failed;
            Quests[i] = q;
            ContractFailedRpc(UI.Dialogue.NameOf(q.Who) + "'s quest: " + ContractName(q.Contract));
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void ContractFailedRpc(string name) => UI.GameHud.Feed("Quest failed: " + name, new Color(0.85f, 0.4f, 0.35f));

        /// <summary>Server: back at camp - settled and unfinished quests come off the log; everyone has work again.</summary>
        private void ClearSettledContract(bool keepRun)
        {
            EnsureFavourLoaded();
            for (int i = Quests.Count - 1; i >= 0; i--)
                if (Quests[i].Status != ContractStatus.Taken) Quests.RemoveAt(i);
            QuestTakenMask.Value = 0;
            if (!keepRun) GiftMask.Value = 0;
        }

        /// <summary>Server, tests: a live Cellar Money quest from Marta fed its goal in gold settles, pays favour and leaves the log.</summary>
        public bool ServerDebugQuestFlow(out string why)
        {
            int fav0 = Favour(FolkId.Marta);
            Quests.Add(new QuestSlot { Kind = (byte)Contract.GoldRun, Giver = (byte)FolkId.Marta, State = (byte)ContractStatus.Active, Goal = 50 });
            int i = Quests.Count - 1;
            QuestOnGold(20);
            bool partial = Quests[i].Progress == 20 && Quests[i].Status == ContractStatus.Active;
            QuestOnGold(40);
            bool done = Quests[i].Status == ContractStatus.Fulfilled && Quests[i].Progress == 50;
            bool favoured = Favour(FolkId.Marta) == fav0 + 1;
            Quests.RemoveAt(i);
            ServerDebugFavour(FolkId.Marta, fav0);
            why = "partial " + partial + ", fulfilled " + done + ", favour +1 " + favoured;
            return partial && done && favoured;
        }

        /// <summary>Server, tests: empty the log and the visit's offers.</summary>
        public void ServerDebugClearQuests() { Quests.Clear(); QuestTakenMask.Value = 0; GiftMask.Value = 0; }

        // ---- Favour (the host's save; tests never read or write it) ----
        public int Favour(FolkId who) => (int)((FavourBits.Value >> ((int)who * 4)) & 15);

        public static float FavourPriceScale(FolkId who)
        {
            var d = Instance;
            return d == null ? 1f : 1f - FavourDiscount * Mathf.Min(MaxFavourDiscount, d.Favour(who));
        }

        private static string FavourKey(FolkId who) => "db.favour." + who;

        private void EnsureFavourLoaded()
        {
            if (favourLoaded || !IsServer) return;
            favourLoaded = true;
            if (Cosmetics.TestMode) return;
            ulong bits = 0;
            foreach (var who in QuestGivers) bits |= (ulong)Mathf.Clamp(PlayerPrefs.GetInt(FavourKey(who), 0), 0, 15) << ((int)who * 4);
            FavourBits.Value = bits;
        }

        /// <summary>Server, tests: set one person's favour without touching the save.</summary>
        public void ServerDebugFavour(FolkId who, int level)
        {
            int shift = (int)who * 4;
            FavourBits.Value = (FavourBits.Value & ~(15UL << shift)) | ((ulong)Mathf.Clamp(level, 0, 15) << shift);
        }

        private void ServerAddFavour(FolkId who)
        {
            EnsureFavourLoaded();
            int now = Mathf.Min(15, Favour(who) + 1);
            ServerDebugFavour(who, now);
            if (!Cosmetics.TestMode) { PlayerPrefs.SetInt(FavourKey(who), now); PlayerPrefs.Save(); }
            FavourRpc((byte)who, now);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void FavourRpc(byte who, int level) =>
            UI.GameHud.Feed(UI.Dialogue.NameOf((FolkId)who) + " thinks better of you (favour " + level + ")", new Color(0.95f, 0.85f, 0.55f));
    }
}
