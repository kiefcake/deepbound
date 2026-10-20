using Deepbound.Combat;
using UnityEngine;
using UnityEngine.AI;

namespace Deepbound.World
{
    /// <summary>
    /// Server: where things may land. A drop flung from a corpse, a chest or an urn must stay on
    /// this side of the wall, on floor you can walk to, and out of the pillars and crates; a chest
    /// must not be spawned inside a column. Both search outward from where they wanted to be.
    /// </summary>
    public static class DropSpots
    {
        private static readonly RaycastHit[] hits = new RaycastHit[16];
        private static readonly Collider[] overlaps = new Collider[16];

        /// <summary>A loot drop thrown from <paramref name="origin"/> toward <paramref name="want"/>: where it actually lands.</summary>
        public static Vector3 Settle(Vector3 origin, Vector3 want)
        {
            // 1. Never through a wall: stop short of the first solid thing between the source and the spot.
            var o = origin + Vector3.up * 0.6f;
            var t = new Vector3(want.x, origin.y, want.z) + Vector3.up * 0.6f;
            var dir = t - o;
            float len = dir.magnitude;
            if (len > 0.01f)
            {
                dir /= len;
                int n = Physics.RaycastNonAlloc(o, dir, hits, len, ~0, QueryTriggerInteraction.Ignore);
                float nearest = len;
                for (int i = 0; i < n; i++)
                {
                    var c = hits[i].collider;
                    if (c == null || c.GetComponentInParent<Health>() != null || c.GetComponentInParent<CharacterController>() != null) continue;
                    if (hits[i].distance < nearest) nearest = hits[i].distance;
                }
                if (nearest < len) want = origin + new Vector3(dir.x, 0f, dir.z) * Mathf.Max(0f, nearest - 0.4f);
            }
            // 2. Onto floor someone can walk to (not the top of a shelf, a ledge or a wall).
            if (NavMesh.SamplePosition(want, out var nh, 2.5f, NavMesh.AllAreas)) want = nh.position;
            // Died somewhere no one walks (a breach tunnel, a perch): the nearest floor anyone can reach.
            else if (NavMesh.SamplePosition(want, out var far, 9f, NavMesh.AllAreas)) want = far.position;
            // 3. Out of props (a crate, a statue): nudge round in a small ring until it is clear.
            if (Blocked(want, 0.22f))
                for (int ring = 1; ring <= 4; ring++)
                    for (int k = 0; k < 8; k++)
                    {
                        float a = k * Mathf.PI / 4f + ring;
                        var p = want + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (ring * 0.45f);
                        if (!NavMesh.SamplePosition(p, out var ph, 0.6f, NavMesh.AllAreas)) continue;
                        if (Blocked(ph.position, 0.22f)) continue;
                        return ph.position + Vector3.up * 0.05f;
                    }
            return want + Vector3.up * 0.05f;
        }

        /// <summary>
        /// A spot for a chest-sized thing near <paramref name="want"/> (facing <paramref name="facing"/>) that is
        /// clear of walls, pillars and dressing, inside <paramref name="room"/> when given.
        /// </summary>
        public static Vector3 ClearForChest(Vector3 want, Quaternion facing, DungeonLayout.Room room = null)
        {
            if (!BoxBlocked(want, facing)) return want;
            for (int ring = 1; ring <= 10; ring++)
                for (int k = 0; k < 12; k++)
                {
                    float a = k * Mathf.PI / 6f;
                    var p = want + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (ring * 0.5f);
                    if (room != null && !room.Contains(p, -1.0f)) continue;
                    if (NavMesh.SamplePosition(p, out var nh, 0.8f, NavMesh.AllAreas)) p = nh.position;
                    else continue;
                    if (!BoxBlocked(p, facing)) return p;
                }
            return want;
        }

        private static bool Blocked(Vector3 at, float r)
        {
            int n = Physics.OverlapSphereNonAlloc(at + Vector3.up * (r + 0.12f), r, overlaps, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (overlaps[i] != null && overlaps[i].GetComponentInParent<Health>() == null && overlaps[i].GetComponentInParent<CharacterController>() == null) return true;
            return false;
        }

        private static bool BoxBlocked(Vector3 at, Quaternion facing)
        {
            // The chest's footprint (1.16 x 0.76), from just above the floor to lid height.
            int n = Physics.OverlapBoxNonAlloc(at + Vector3.up * 0.6f, new Vector3(0.62f, 0.44f, 0.42f), overlaps, facing, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = overlaps[i];
                if (c == null || c.GetComponentInParent<Health>() != null || c.GetComponentInParent<CharacterController>() != null) continue;
                if (c.GetComponentInParent<Interactable>() != null) continue; // another station is a networked object, judged elsewhere
                return true;
            }
            return false;
        }
    }
}
