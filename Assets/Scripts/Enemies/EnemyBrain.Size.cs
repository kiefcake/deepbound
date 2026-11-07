using Deepbound.Art;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// Size variety (2026-11-07, the user: "enemies are very similarly sized other than the bosses or elite enemies. i'd
    /// like to see more variety"). Every plain fighter is a little taller or shorter than the next (x0.9-1.1), and a few
    /// are RUNTS (x0.68-0.76: frail, quick) or HULKING (x1.38-1.55: slow, tough, hits harder, reaches further).
    /// The size is a pure function of the NetworkObjectId, so every peer works out the same one with nothing synced.
    /// The server folds it into health, damage, speed, reach and the navmesh agent; every peer scales the rig, the hit
    /// capsule and <see cref="Combat.Health.CenterHeight"/> (bars, numbers and tells sit on the head it really has).
    /// </summary>
    public partial class EnemyBrain
    {
        public const float RuntChance = 0.07f, HulkChance = 0.045f;

        /// <summary>This one's size, x1 for bosses and the odd shapes (statues, mimics, eyes, roots, wraiths).</summary>
        public float SizeScale { get; private set; } = 1f;
        public bool IsRunt => SizeScale < 0.8f;
        public bool IsHulking => SizeScale > 1.3f;
        /// <summary>The word for the focus enemy's tag line, or null.</summary>
        public string SizeWord => IsRunt ? "RUNT" : IsHulking ? "HULKING" : null;

        private static bool Sized(CharacterArt.SkeletonKind k) => k switch
        {
            CharacterArt.SkeletonKind.Warrior or CharacterArt.SkeletonKind.Archer or CharacterArt.SkeletonKind.Ghoul
                or CharacterArt.SkeletonKind.Brute or CharacterArt.SkeletonKind.Necromancer or CharacterArt.SkeletonKind.Shieldbearer
                or CharacterArt.SkeletonKind.Hoarder or CharacterArt.SkeletonKind.Plaguebearer or CharacterArt.SkeletonKind.FrostWight
                or CharacterArt.SkeletonKind.Hexer or CharacterArt.SkeletonKind.Orbweaver or CharacterArt.SkeletonKind.ChoirAcolyte
                or CharacterArt.SkeletonKind.BonePiper or CharacterArt.SkeletonKind.Cantor => true,
            _ => false,
        };

        private static bool CanBeRunt(CharacterArt.SkeletonKind k) => k == CharacterArt.SkeletonKind.Warrior || k == CharacterArt.SkeletonKind.Archer
            || k == CharacterArt.SkeletonKind.Ghoul || k == CharacterArt.SkeletonKind.Necromancer || k == CharacterArt.SkeletonKind.Hoarder;

        private static bool CanBeHulking(CharacterArt.SkeletonKind k) => k == CharacterArt.SkeletonKind.Warrior || k == CharacterArt.SkeletonKind.Shieldbearer
            || k == CharacterArt.SkeletonKind.Ghoul || k == CharacterArt.SkeletonKind.Plaguebearer || k == CharacterArt.SkeletonKind.FrostWight;

        /// <summary>The size for an id and kind (pure; the same on every peer).</summary>
        public static float SizeFor(ulong id, CharacterArt.SkeletonKind kind, bool boss)
        {
            if (boss || !Sized(kind)) return 1f;
            // Two independent fractions from one id (a cheap integer hash).
            ulong h = (id + 0x9E3779B97F4A7C15UL) * 0xBF58476D1CE4E5B9UL;
            h ^= h >> 31;
            float u = (h & 0xFFFF) / 65536f, v = ((h >> 16) & 0xFFFF) / 65536f;
            if (CanBeRunt(kind) && u < RuntChance) return Mathf.Lerp(0.68f, 0.76f, v);
            if (CanBeHulking(kind) && u > 1f - HulkChance) return Mathf.Lerp(1.38f, 1.55f, v);
            return Mathf.Lerp(0.9f, 1.1f, v);
        }

        /// <summary>Every peer, from OnNetworkSpawn once the rig exists: the body follows the size.</summary>
        private void ApplySize()
        {
            SizeScale = SizeFor(NetworkObjectId, visualKind, IsBoss);
            if (Mathf.Approximately(SizeScale, 1f)) return;
            float k = SizeScale;
            if (Rig != null) Rig.transform.localScale *= k;
            var capsule = GetComponent<CapsuleCollider>();
            if (capsule != null)
            {
                capsule.radius *= Mathf.Lerp(1f, k, 0.85f);
                capsule.height *= k;
                capsule.center *= k;
            }
            health.CenterHeight *= k;
            health.InvalidateBody();
            if (agent != null)
            {
                agent.radius *= Mathf.Lerp(1f, k, 0.75f);
                agent.height *= k;
            }
        }

        private bool sizeStatsApplied, championScaled;

        /// <summary>
        /// Champion or elite growth after spawn: the rig, the head height the bars use, and the hit capsule refitted when it
        /// is now big (the host learns the title after spawning, a late joiner before: both must end with one body).
        /// </summary>
        private void Grow(float k)
        {
            if (Rig == null) return;
            Rig.transform.localScale *= k;
            health.CenterHeight *= k;
            if (Rig.transform.lossyScale.y >= 1.12f) FitBodyToRig();
            else health.InvalidateBody();
        }

        private void ScaleAsChampion()
        {
            if (championScaled || Rig == null) return;
            championScaled = true;
            Grow(1.22f);
        }

        /// <summary>Server, before the depth/party scaling: a hulk is tougher, slower and hits harder; a runt the reverse.</summary>
        private void ServerApplySizeStats()
        {
            float k = SizeScale;
            if (Mathf.Approximately(k, 1f)) return;
            maxHealth *= Mathf.Pow(k, 1.6f);
            damage *= Mathf.Lerp(1f, k, 0.45f);
            moveSpeed /= Mathf.Pow(k, 0.55f);
            attackRange *= Mathf.Lerp(1f, k, 0.7f);
            if (agent != null)
            {
                agent.speed = moveSpeed;
                agent.stoppingDistance = attackRange * 0.75f;
            }
        }
    }
}
