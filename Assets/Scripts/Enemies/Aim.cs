using Deepbound.Combat;
using Deepbound.Player;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// Leading a moving target. The server tracks each player's ground velocity (PlayerLife.ServerVelocity);
    /// a shooter aims where you will be when the shot arrives, scaled by its skill (0 = straight at you,
    /// 1 = a perfect lead). Skill grows with depth (Difficulty.Lead), so the first floors forgive
    /// running in a straight line and the deep ones do not.
    /// </summary>
    public static class Aim
    {
        public static Vector3 Lead(Health target, Vector3 origin, float speed, float skill)
        {
            var p = target.Center;
            if (skill <= 0f || speed <= 0f || !target.TryGetComponent<PlayerLife>(out var life)) return p;
            var v = life.ServerVelocity;
            v.y = 0f;
            // Two passes: time to where it is, then time to where it will be.
            float t = Vector3.Distance(origin, p) / speed;
            t = Vector3.Distance(origin, p + v * t) / speed;
            return p + v * Mathf.Min(t, 1.6f) * skill;
        }
    }
}
