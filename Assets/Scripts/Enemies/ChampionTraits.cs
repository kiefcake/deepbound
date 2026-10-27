using Deepbound.Combat;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// Champions' traits (2026-10-27): a champion was a named elite with more of everything; now its epithet is
    /// also what it does, so the name on its tag tells you how it fights. Index = the epithet's place in
    /// <see cref="EnemyBrain"/>'s list (append only, in step with it):
    /// <list type="bullet">
    /// <item>the Unburied - the first killing blow leaves it standing; a moment later it rises at 40%.</item>
    /// <item>Gravefist - its blows daze you and throw you back.</item>
    /// <item>the Hollow - its blows curse you for a few seconds.</item>
    /// <item>Bonegnawer - it heals on what it deals.</item>
    /// <item>the Patient - slower to swing, half again as hard.</item>
    /// <item>Ashenhand - its blows burn (poison).</item>
    /// <item>the Tithe-Taker - its blows steal gold; it all spills back, and half again, when it falls.</item>
    /// <item>Oathbreaker - wounded to half, it calls two of the dead to its side.</item>
    /// <item>the Pale - its blows frostbite.</item>
    /// <item>Marrowgrin - its corpse bursts (get clear of the ring).</item>
    /// </list>
    /// </summary>
    public static class ChampionTraits
    {
        public const int Unburied = 0, Gravefist = 1, Hollow = 2, Bonegnawer = 3, Patient = 4, Ashenhand = 5, TitheTaker = 6, Oathbreaker = 7, Pale = 8, Marrowgrin = 9;

        private static readonly string[] Lines =
        {
            "it will not stay down the first time",
            "its blows daze and throw you",
            "its blows curse you",
            "it heals on every blow it lands",
            "slow to swing, crushing when it lands",
            "its blows burn",
            "its blows steal your gold (it spills back when it falls)",
            "wounded, it calls the dead to its side",
            "its blows frostbite",
            "its corpse bursts: get clear when it falls",
        };

        /// <summary>What a champion with this title does (any peer: the title is synced).</summary>
        public static string Describe(string title)
        {
            int t = TraitOf(title);
            return t >= 0 && t < Lines.Length ? Lines[t] : "";
        }

        private static readonly Color[] Motes =
        {
            new Color(0.75f, 0.7f, 0.55f), new Color(0.9f, 0.8f, 0.5f), new Color(0.6f, 0.25f, 0.85f), new Color(0.85f, 0.2f, 0.2f), new Color(0.8f, 0.8f, 0.85f),
            new Color(1f, 0.5f, 0.15f), new Color(1f, 0.85f, 0.3f), new Color(0.95f, 0.45f, 0.3f), new Color(0.6f, 0.85f, 1f), new Color(0.95f, 0.92f, 0.8f),
        };

        /// <summary>The motes a champion with this title drifts in (any peer), or null.</summary>
        public static Color? MoteColor(string title)
        {
            int t = TraitOf(title);
            return t >= 0 && t < Motes.Length ? Motes[t] : (Color?)null;
        }

        /// <summary>The trait an epithet in this title carries, or -1.</summary>
        public static int TraitOf(string title)
        {
            if (string.IsNullOrEmpty(title)) return -1;
            for (int i = 0; i < EnemyBrain.Epithets.Length; i++)
                if (title.EndsWith(EnemyBrain.Epithets[i])) return i;
            return -1;
        }

        private static bool hooked;

        /// <summary>Server, once: a champion's blow on a player carries its trait.</summary>
        public static void EnsureHook()
        {
            if (hooked) return;
            hooked = true;
            Health.ServerAnyDamaged += (target, dealt, attackerId) =>
            {
                if (target == null || dealt <= 0f || attackerId == ulong.MaxValue) return;
                var net = NetworkManager.Singleton;
                if (net == null || !net.SpawnManager.SpawnedObjects.TryGetValue(attackerId, out var obj)) return;
                if (target.Team == Team.Players)
                {
                    if (!obj.TryGetComponent<EnemyBrain>(out var champ) || champ.IsDead || champ.ChampionTrait < 0) return;
                    OnChampionHit(champ, target, dealt);
                }
                else if (target.Team == Team.Enemies && target.TryGetComponent<EnemyBrain>(out var hurt) && hurt.ChampionTrait == Oathbreaker)
                    hurt.ServerOathbreak();
            };
        }

        private static void OnChampionHit(EnemyBrain champ, Health victim, float dealt)
        {
            var deb = victim.GetComponent<PlayerDebuffs>();
            switch (champ.ChampionTrait)
            {
                case Gravefist:
                    deb?.ServerDaze(1.4f);
                    if (victim.TryGetComponent<KnockbackReceiver>(out var kb))
                    {
                        var push = victim.transform.position - champ.transform.position;
                        push.y = 0f;
                        kb.KnockbackRpc(push.normalized * 8f + Vector3.up * 2.5f);
                    }
                    break;
                case Hollow: deb?.ServerCurse(4f, champ.NetworkObjectId); break;
                case Bonegnawer: champ.GetComponent<Health>().ServerHeal(dealt * 0.3f); break;
                case Ashenhand: deb?.ServerPoison(3f, 4f + 1.5f * World.DungeonDirector.Tier(champ.Depth)); break;
                case Pale: deb?.ServerFrostbite(2.5f); break;
                case TitheTaker:
                    if (victim.TryGetComponent<Progression.PlayerProgress>(out var purse) && purse.Gold.Value > 0)
                    {
                        int take = Mathf.Clamp(Mathf.RoundToInt(purse.Gold.Value * 0.05f), 1, 40);
                        purse.Gold.Value -= take;
                        champ.ServerTithe(take, victim.Center);
                    }
                    break;
            }
        }
    }
}
