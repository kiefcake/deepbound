using Deepbound.Combat;
using Deepbound.Player;
using UnityEngine;

namespace Deepbound.Enemies
{
    public static class Aim
    {
        public static Vector3 Lead(Health target, Vector3 origin, float speed, float skill)
        {
            var p = target.Center;
            if (skill <= 0f || speed <= 0f || !target.TryGetComponent<PlayerLife>(out var life)) return p;
            var v = life.ServerVelocity;
            v.y = 0f;

            float t = Vector3.Distance(origin, p) / speed;
            t = Vector3.Distance(origin, p + v * t) / speed;
            return p + v * Mathf.Min(t, 1.6f) * skill;
        }
    }
}
