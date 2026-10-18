using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Player;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Enemies
{
    /// <summary>
    /// The Plaguebearer: a bloated, rotting corpse that keeps its distance and spits gobbets
    /// of plague. A hit poisons you for a while. Kill it and it bursts — a green cloud that
    /// poisons anyone who lingers in it, so do not finish it at sword's length.
    /// </summary>
    public class PlaguebearerBrain : EnemyBrain
    {
        [SerializeField] private float preferredRange = 8f, cloudRadius = 3.4f, cloudSeconds = 5f;
        private float nextTickAt;
        private Vector3 cloudAt;
        private float cloudUntil = -1f;

        protected override void Strike()
        {
            if (target == null) return;
            var origin = transform.position + Vector3.up * 1.4f + transform.forward * 0.4f;
            var dir = AimDir(origin, 13f);
            float dmg = damage;
            Arrows.ServerFire(origin, dir, dmg, Team.Players, NetworkObjectId, 13f, hit =>
            {
                hit.ServerDamage(dmg, hit.Center - dir.normalized * 3f, NetworkObjectId);
                if (hit.TryGetComponent<PlayerDebuffs>(out var deb)) deb.ServerPoison(6f, 6f + 2f * depthForLoot);
            });
            SpitRpc(origin, dir);
        }

        protected override bool ChooseSpecialAttack(float distanceToTarget)
        {
            agent.stoppingDistance = preferredRange;
            return false;
        }

        protected override void OnDied(Health h)
        {
            base.OnDied(h);
            cloudAt = transform.position;
            cloudUntil = Time.time + cloudSeconds;
            CloudRpc(cloudAt, cloudRadius, cloudSeconds);
        }

        protected override void Update()
        {
            if (IsServer && cloudUntil > 0f)
            {
                if (Time.time > cloudUntil) cloudUntil = -1f;
                else if (Time.time >= nextTickAt)
                {
                    nextTickAt = Time.time + 0.5f;
                    foreach (var p in CombatQuery.Circle(cloudAt, cloudRadius, Team.Players))
                        if (p.TryGetComponent<PlayerDebuffs>(out var deb)) deb.ServerPoison(3f, 8f + 2f * depthForLoot);
                }
            }
            base.Update();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SpitRpc(Vector3 origin, Vector3 dir)
        {
            Arrows.SpawnVisual(origin, dir, ProjectileKind.Spit, 13f);
            Sfx.Play(Sound.Splash, origin, 0.6f, 1.6f);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void CloudRpc(Vector3 at, float radius, float seconds)
        {
            Telegraph.Show(at, radius, seconds, null, new Color(0.35f, 0.75f, 0.15f, 0.35f));
            Sfx.Play(Sound.Splash, at + Vector3.up, 1f, 0.5f);
            StartCoroutine(CloudFx(at, radius, seconds));
        }

        private System.Collections.IEnumerator CloudFx(Vector3 at, float radius, float seconds)
        {
            var c = new Color(0.45f, 0.85f, 0.2f);
            for (float t = 0f; t < seconds; t += 0.15f)
            {
                Vfx.Mote(at + new Vector3(Random.Range(-radius, radius), 0f, Random.Range(-radius, radius)) * 0.7f, c);
                yield return new WaitForSeconds(0.15f);
            }
        }
    }
}
