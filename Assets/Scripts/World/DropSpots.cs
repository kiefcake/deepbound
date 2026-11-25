using Deepbound.Combat;
using UnityEngine;
using UnityEngine.AI;

namespace Deepbound.World
{
    public static class DropSpots
    {
        private static readonly RaycastHit[] hits = new RaycastHit[16];
        private static readonly Collider[] overlaps = new Collider[16];

        public static Vector3 Settle(Vector3 origin, Vector3 want)
        {
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

            if (NavMesh.SamplePosition(want, out var nh, 2.5f, NavMesh.AllAreas)) want = nh.position;

            else if (NavMesh.SamplePosition(want, out var far, 9f, NavMesh.AllAreas)) want = far.position;

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

        public static Vector3 ClearForChest(Vector3 want, Quaternion facing, DungeonLayout.Room room = null)
        {
            room ??= DungeonDirector.Layout?.RoomAt(want, 0.5f);
            float keep = room != null ? Centrepieces.Radius(Centrepieces.Of(room)) + 0.9f : 0f;
            bool InMiddle(Vector3 p) { if (keep <= 0f) return false; var d = p - room.Center; d.y = 0f; return d.magnitude < keep; }
            if (!BoxBlocked(want, facing) && !InMiddle(want)) return want;
            for (int ring = 1; ring <= 10; ring++)
                for (int k = 0; k < 12; k++)
                {
                    float a = k * Mathf.PI / 6f;
                    var p = want + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (ring * 0.5f);
                    if (room != null && !room.Contains(p, -1.0f)) continue;
                    if (NavMesh.SamplePosition(p, out var nh, 0.8f, NavMesh.AllAreas)) p = nh.position;
                    else continue;
                    if (!BoxBlocked(p, facing) && !InMiddle(p)) return p;
                }
            return want;
        }

        public static Vector3 Stand(Vector3 anchor, Vector3 want, float clearance = 0.35f, float wallMargin = 1.3f)
        {
            var layout = DungeonDirector.Layout;
            var room = layout?.RoomAt(anchor, 0.5f);
            if (room != null)
            {
                float hx = Mathf.Max(0.4f, room.Size.x / 2f - wallMargin), hz = Mathf.Max(0.4f, room.Size.y / 2f - wallMargin);
                want.x = Mathf.Clamp(want.x, room.Center.x - hx, room.Center.x + hx);
                want.z = Mathf.Clamp(want.z, room.Center.z - hz, room.Center.z + hz);
            }
            var anchorFloor = NavMesh.SamplePosition(anchor, out var ah, 3f, NavMesh.AllAreas) ? ah.position : anchor;
            for (int step = 0; step <= 6; step++)
            {
                var p = Vector3.Lerp(want, anchorFloor, step / 6f);
                if (!NavMesh.SamplePosition(p, out var h, 1.6f, NavMesh.AllAreas)) continue;
                var at = h.position;
                if (room != null && !room.Contains(at, -0.4f)) continue;
                if (!Reachable(anchorFloor, at)) continue;
                if (clearance > 0f && Blocked(at, clearance)) continue;
                return at;
            }
            return anchorFloor;
        }

        private static readonly NavMeshPath standPath = new NavMeshPath();

        public static bool Reachable(Vector3 from, Vector3 to)
        {
            if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, standPath) || standPath.status != NavMeshPathStatus.PathComplete) return false;
            float len = 0f;
            var c = standPath.corners;
            for (int i = 1; i < c.Length; i++) len += Vector3.Distance(c[i - 1], c[i]);
            return len <= Vector3.Distance(from, to) * 2f + 4f;
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
            int n = Physics.OverlapBoxNonAlloc(at + Vector3.up * 0.6f, new Vector3(0.62f, 0.44f, 0.42f), overlaps, facing, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var c = overlaps[i];
                if (c == null || c.GetComponentInParent<Health>() != null || c.GetComponentInParent<CharacterController>() != null) continue;
                if (c.GetComponentInParent<Interactable>() != null) continue;
                return true;
            }
            return false;
        }
    }
}
