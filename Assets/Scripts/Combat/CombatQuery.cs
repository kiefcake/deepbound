using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.Combat
{
    public static class CombatQuery
    {
        public static List<Health> Arc(Vector3 origin, Vector3 forward, float range, float arcDegrees, Team team)
        {
            var Results = new List<Health>();
            forward.y = 0f;
            forward.Normalize();
            float halfArc = arcDegrees * 0.5f;

            foreach (var h in Health.All)
            {
                if (h == null || !Matches(h, team) || !h.IsAlive) continue;

                Vector3 to = h.Center - origin;
                float vertical = to.y;
                to.y = 0f;
                float dist = to.magnitude;

                if (dist - h.BodyRadius > range || Mathf.Abs(vertical) > 2.5f + h.CenterHeight * 0.5f) continue;

                if (dist > 0.8f && Vector3.Angle(forward, to) > halfArc) continue;
                if (!ClearLine(origin, h)) continue;

                Results.Add(h);
            }

            return Results;
        }

        public static List<Health> Circle(Vector3 center, float radius, Team team)
        {
            var Results = new List<Health>();
            foreach (var h in Health.All)
            {
                if (h == null || !Matches(h, team) || !h.IsAlive) continue;
                Vector3 to = h.transform.position - center;
                if (Mathf.Abs(to.y) > 3f) continue;
                to.y = 0f;
                if (to.magnitude <= radius + h.BodyRadius * 0.6f) Results.Add(h);
            }
            return Results;
        }

        public static bool Matches(Health h, Team team) => h.Team == team || (team == Team.Enemies && h.Team == Team.Props) || (team == Team.Players && h.Team == Team.Allies);

        public static bool ClearLine(Vector3 from, Health target)
        {
            if (!Physics.Linecast(from, target.Center, out var hit, ~0, QueryTriggerInteraction.Ignore)) return true;
            return hit.collider.GetComponentInParent<Health>() != null;
        }
    }
}
