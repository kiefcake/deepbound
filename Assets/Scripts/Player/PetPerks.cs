using System.Collections.Generic;
using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Enemies;
using Deepbound.Loot;
using Deepbound.World;
using UnityEngine;

namespace Deepbound.Player
{
    public class PetPerks : MonoBehaviour
    {
        public const float RavenSight = 22f, HoundEvery = 5f, HoundReach = 2.8f, QuietRadius = 12f, PurrPerSecond = 0.004f,
            ToadEvery = 10f, ImpReach = 6f, MothReach = 16f, BatBehind = 5.5f, RatCoins = 3f;

        public enum Cue : byte { Bite, Gulp, Kindle, Coins, Behind, Elite, Purr, Scorch, Cough }
        public const float SalamanderEvery = 3f, MimicPupChance = 0.35f;
        private float nextScorch;

        public static string Short(PetCompanion.Kind k) => k switch
        {
            PetCompanion.Kind.Raven => "Reveals elites at range",
            PetCompanion.Kind.Rat => "Finds extra gold in cleared rooms",
            PetCompanion.Kind.BoneHound => "Bites enemies attacking you",
            PetCompanion.Kind.Kitten => "Slowly heals you out of combat",
            PetCompanion.Kind.Wisp => "Provides light",
            PetCompanion.Kind.Bat => "Warns of enemies behind you; detects traps",
            PetCompanion.Kind.Toad => "Removes poison every 10 s",
            PetCompanion.Kind.CandleImp => "Lights hearths; provides light",
            PetCompanion.Kind.Moth => "Locates chests and ignores mimics",
            PetCompanion.Kind.Salamander => "Scorches enemies that hit you",
            PetCompanion.Kind.MimicPup => "Chests may yield an extra potion",
            _ => "",
        };

        private NetworkPlayer np;
        private PlayerLife life;
        private PlayerDebuffs debuffs;
        private PetCompanion pet;
        private Light lamp;
        private float nextTick, nextBite, nextGulp, nextBehind, nextLocal, nextMoth;
        private readonly HashSet<EnemyBrain> called = new HashSet<EnemyBrain>();
        private PetCompanion.Kind lastKind = (PetCompanion.Kind)255;

        private PetCompanion.Kind Kind => np != null ? (PetCompanion.Kind)np.PetKind.Value : PetCompanion.Kind.None;
        private static bool InDungeon => !DungeonDirector.InLobby && DungeonDirector.Layout != null;

        public void Init(NetworkPlayer owner)
        {
            np = owner;
            life = owner.GetComponent<PlayerLife>();
            debuffs = owner.GetComponent<PlayerDebuffs>();
            pet = owner.GetComponent<PetCompanion>();

            if (owner.TryGetComponent<Health>(out var hp))
                hp.DamagedServer += (lost, by) =>
                {
                    if (Kind != PetCompanion.Kind.Salamander || !InDungeon || Time.time < nextScorch || by == ulong.MaxValue) return;
                    var nm = Unity.Netcode.NetworkManager.Singleton;
                    if (nm == null || !nm.SpawnManager.SpawnedObjects.TryGetValue(by, out var o) || !o.TryGetComponent<Health>(out var foe) || foe.Team != Team.Enemies || !foe.IsAlive) return;
                    if ((foe.transform.position - transform.position).sqrMagnitude > 4f * 4f) return;
                    nextScorch = Time.time + SalamanderEvery;
                    foe.ServerScorch(5f, np.NetworkObjectId);
                    np.PetCueRpc((byte)Cue.Scorch, foe.transform.position);
                };
        }

        public static void ServerChestOpened(Vector3 at)
        {
            var dd = DungeonDirector.Instance;
            if (dd == null) return;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || p.Downed.Value || !p.TryGetComponent<NetworkPlayer>(out var who)) continue;
                if ((PetCompanion.Kind)who.PetKind.Value != PetCompanion.Kind.MimicPup || Vector3.Distance(p.transform.position, at) > 12f) continue;
                if (Random.value >= (ForceChance ? 1f : MimicPupChance)) continue;
                var spot = p.transform.position + p.transform.forward * 0.8f;
                dd.ServerDropLoot(spot, LootKind.PotionHP, 1);
                who.PetCueRpc((byte)Cue.Cough, spot);
            }
        }

        public static bool ForceChance;

        private void Update()
        {
            if (np == null) return;
            var k = Kind;
            if (k != lastKind) { lastKind = k; called.Clear(); BuildLamp(k); }
            if (lamp != null) lamp.enabled = InDungeon && !(life != null && life.Downed.Value);
            if (!InDungeon || k == PetCompanion.Kind.None) return;
            if (np.IsServer && Time.time >= nextTick) { nextTick = Time.time + 0.5f; ServerTick(k, 0.5f); }
            if (np.IsOwner && Time.time >= nextLocal) { nextLocal = Time.time + 0.4f; OwnerTick(k); }
        }

        private void BuildLamp(PetCompanion.Kind k)
        {
            if (lamp != null) { Destroy(lamp.gameObject); lamp = null; }
            if (Application.isBatchMode || (k != PetCompanion.Kind.Wisp && k != PetCompanion.Kind.CandleImp)) return;
            lamp = new GameObject("PetLamp").AddComponent<Light>();
            lamp.transform.SetParent(transform, false);
            lamp.transform.localPosition = new Vector3(0.6f, 2.1f, -0.5f);
            lamp.type = LightType.Point;
            lamp.shadows = LightShadows.None;
            bool wisp = k == PetCompanion.Kind.Wisp;
            lamp.color = wisp ? new Color(0.65f, 0.85f, 1f) : new Color(1f, 0.7f, 0.4f);
            lamp.range = wisp ? 9f : 7f;
            lamp.intensity = wisp ? 1.3f : 1.1f;
        }

        private void ServerTick(PetCompanion.Kind k, float dt)
        {
            if (life == null || life.Downed.Value || !life.Health.IsAlive) return;
            var at = transform.position;
            switch (k)
            {
                case PetCompanion.Kind.Kitten:
                    if (life.Health.Fraction < 1f && NearestFoe(at, QuietRadius) == null)
                        life.Health.ServerHeal(life.Health.Max.Value * PurrPerSecond * dt);
                    break;
                case PetCompanion.Kind.BoneHound:
                    if (Time.time < nextBite) break;
                    foreach (var e in EnemyBrain.All)
                    {
                        if (e == null || e.IsDead || e.CurrentTarget != life.Health) continue;
                        if ((e.transform.position - at).sqrMagnitude > HoundReach * HoundReach || !e.TryGetComponent<Health>(out var eh) || !eh.IsAlive) continue;
                        nextBite = Time.time + HoundEvery;
                        float tier = DungeonDirector.Tier(DungeonDirector.Layout.Depth);
                        eh.Tag("PetPerks").ServerDamage(6f + 4f * tier, at, np.NetworkObjectId);
                        np.PetCueRpc((byte)Cue.Bite, e.transform.position);
                        break;
                    }
                    break;
                case PetCompanion.Kind.Toad:
                    if (Time.time < nextGulp || debuffs == null || !debuffs.Poisoned) break;
                    nextGulp = Time.time + ToadEvery;
                    debuffs.PoisonUntil.Value = 0;
                    np.PetCueRpc((byte)Cue.Gulp, at);
                    break;
                case PetCompanion.Kind.CandleImp:
                    foreach (var h in HearthBrazier.All)
                    {
                        if (h == null || h.Lit.Value || (h.transform.position - at).sqrMagnitude > ImpReach * ImpReach) continue;
                        h.ServerLight();
                        np.PetCueRpc((byte)Cue.Kindle, h.transform.position);
                        break;
                    }
                    break;
            }
        }

        public static void ServerRoomCleared(Vector3 roomAt)
        {
            var dd = DungeonDirector.Instance;
            if (dd == null) return;
            foreach (var p in PlayerLife.All)
            {
                if (p == null || p.Downed.Value || !p.TryGetComponent<NetworkPlayer>(out var who)) continue;
                if ((PetCompanion.Kind)who.PetKind.Value != PetCompanion.Kind.Rat || Vector3.Distance(p.transform.position, roomAt) > 40f) continue;
                int tier = Mathf.RoundToInt(DungeonDirector.Tier(DungeonDirector.Layout != null ? DungeonDirector.Layout.Depth : 0));
                var spot = p.transform.position - p.transform.forward * 0.8f;
                dd.ServerDropLoot(spot, LootKind.Gold, Mathf.RoundToInt(RatCoins + 2f * tier));
                who.PetCueRpc((byte)Cue.Coins, spot);
            }
        }

        private static EnemyBrain NearestFoe(Vector3 at, float within)
        {
            EnemyBrain best = null;
            float bd = within * within;
            foreach (var e in EnemyBrain.All)
            {
                if (e == null || e.IsDead) continue;
                float d = (e.transform.position - at).sqrMagnitude;
                if (d < bd) { bd = d; best = e; }
            }
            return best;
        }

        private void OwnerTick(PetCompanion.Kind k)
        {
            var at = transform.position;
            if (k == PetCompanion.Kind.Raven)
            {
                foreach (var e in EnemyBrain.All)
                {
                    if (e == null || e.IsDead || !e.Elite.Value || called.Contains(e)) continue;
                    if ((e.transform.position - at).sqrMagnitude > RavenSight * RavenSight) continue;
                    called.Add(e);
                    Show(Cue.Elite, e.transform.position + Vector3.up * 2.4f);
                    pet?.Visit(e.transform.position + Vector3.up * 3f, 1.2f);
                    break;
                }
            }
            else if (k == PetCompanion.Kind.Bat && Time.time >= nextBehind)
            {
                var fwd = np.AimForward; fwd.y = 0f; fwd.Normalize();
                foreach (var e in EnemyBrain.All)
                {
                    if (e == null || e.IsDead) continue;
                    var d = e.transform.position - at; d.y = 0f;
                    if (d.magnitude > BatBehind || Vector3.Dot(fwd, d.normalized) > -0.35f) continue;
                    nextBehind = Time.time + 6f;
                    Show(Cue.Behind, at + Vector3.up * 1.4f - fwd * 0.6f);
                    break;
                }
            }
            else if (k == PetCompanion.Kind.Moth && Time.time >= nextMoth)
            {
                nextMoth = Time.time + 2.5f;
                Chest best = null;
                float bd = MothReach * MothReach;
                foreach (var c in FindObjectsByType<Chest>(FindObjectsSortMode.None))
                {
                    if (c == null || c.Opened.Value || c.Mimic.Value) continue;
                    float d = (c.transform.position - at).sqrMagnitude;
                    if (d < bd) { bd = d; best = c; }
                }
                if (best != null && bd > 2.5f * 2.5f) pet?.Visit(best.transform.position + Vector3.up * 1.3f, 2.2f);
            }
        }

        public void Show(Cue cue, Vector3 at)
        {
            if (Application.isBatchMode) return;
            switch (cue)
            {
                case Cue.Bite:
                    pet?.Visit(at, 0.5f);
                    Sfx.Play(Sound.Growl, at, 0.6f, 1.4f);
                    UI.GameHud.ShowCombo(at + Vector3.up * 1.2f, "SNAP", new Color(0.9f, 0.85f, 0.7f));
                    break;
                case Cue.Gulp:
                    Sfx.Play(Sound.Slime, at, 0.6f, 1.6f);
                    if (np.IsOwner) UI.GameHud.ShowCombo(at + Vector3.up * 1.8f, "GULP - poison gone", new Color(0.6f, 0.85f, 0.35f));
                    break;
                case Cue.Kindle:
                    pet?.Visit(at + Vector3.up * 1.2f, 1f);
                    UI.GameHud.ShowCombo(at + Vector3.up * 2f, "KINDLED", new Color(1f, 0.7f, 0.35f));
                    break;
                case Cue.Coins:
                    pet?.Visit(at, 0.8f);
                    Sfx.Play(Sound.Squeak, at, 0.6f, 1.2f);
                    if (np.IsOwner) UI.GameHud.ShowCombo(at + Vector3.up * 1f, "DUG UP COINS", new Color(1f, 0.85f, 0.4f));
                    break;
                case Cue.Behind:
                    Sfx.Play2D(Sound.Squeak, 0.8f);
                    UI.GameHud.ShowCombo(at, "BEHIND!", new Color(1f, 0.55f, 0.4f));
                    break;
                case Cue.Scorch:
                    pet?.Visit(at, 0.4f);
                    Sfx.Play(Sound.Fire, at, 0.5f, 1.5f);
                    UI.GameHud.ShowCombo(at + Vector3.up * 1.4f, "SCORCHED", new Color(1f, 0.55f, 0.2f));
                    break;
                case Cue.Cough:
                    pet?.Visit(at, 0.6f);
                    Sfx.Play(Sound.Chest, at, 0.45f, 1.6f);
                    if (np.IsOwner) UI.GameHud.ShowCombo(at + Vector3.up * 1f, "+1 HEALTH POTION", new Color(0.95f, 0.35f, 0.3f));
                    break;
                case Cue.Elite:
                    Sfx.Play2D(Sound.Caw, 0.6f);
                    UI.GameHud.ShowCombo(at, "ELITE", new Color(1f, 0.75f, 0.3f));
                    break;
            }
        }
    }
}
