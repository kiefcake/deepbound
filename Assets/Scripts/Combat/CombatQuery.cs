using System.Collections.Generic;
using UnityEngine;

namespace Deepbound.Combat
{
    /// <summary>
    /// Server-side hit detection for melee arcs and area attacks.
    ///
    /// Scans <see cref="Health.All"/> rather than physics overlaps: there are at most a few
    /// dozen combatants, the shapes (arc, circle) are trivial maths, and it sidesteps
    /// collider layering entirely. A linecast still rejects hits through walls.
    /// </summary>
    public static class CombatQuery
    {
        // Each query returns its own list: a hit can trigger another query (a thorn, a
        // shockwave from armour) while the caller is still walking the first one.

        /// <summary>Targets of <paramref name="team"/> within a horizontal arc in front of <paramref name="origin"/>.</summary>
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

                // Reach the body's edge, not its middle: a giant can be struck at arm's length.
                if (dist - h.BodyRadius > range || Mathf.Abs(vertical) > 2.5f + h.CenterHeight * 0.5f) continue;
                // Point-blank targets count regardless of angle — at 0.5m you are hitting
                // them even if their centre is slightly behind your shoulder.
                if (dist > 0.8f && Vector3.Angle(forward, to) > halfArc) continue;
                if (!ClearLine(origin, h)) continue;

                Results.Add(h);
            }

            return Results;
        }

        /// <summary>Targets of <paramref name="team"/> within <paramref name="radius"/> of a point.</summary>
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

        /// <summary>Attacks aimed at enemies also break props in the way; attacks aimed at players also strike their summoned allies.</summary>
        public static bool Matches(Health h, Team team) => h.Team == team || (team == Team.Enemies && h.Team == Team.Props) || (team == Team.Players && h.Team == Team.Allies);

        /// <summary>
        /// True if nothing static stands between the attacker and the target. Hitting any
        /// combatant's collider counts as clear, so a crowd does not shield itself.
        /// </summary>
        public static bool ClearLine(Vector3 from, Health target)
        {
            if (!Physics.Linecast(from, target.Center, out var hit, ~0, QueryTriggerInteraction.Ignore)) return true;
            return hit.collider.GetComponentInParent<Health>() != null;
        }
    }
}
