using Deepbound.Art;
using Deepbound.Combat;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    /// <summary>
    /// The Ranger's bear trap: open steel jaws on the floor. The first enemy to step on it is
    /// snapped — hurt, held in place, marked by nothing but its own stupidity. Bosses are held
    /// only briefly. Fades after a while if nothing walks into it.
    /// </summary>
    public class BearTrap : NetworkBehaviour
    {
        private const float Radius = 0.9f, Damage = 30f, Hold = 2.5f, Lifetime = 45f;

        public readonly NetworkVariable<bool> Sprung = new NetworkVariable<bool>();

        private float attackMultiplier = 1f, expiresAt;
        private ulong owner;
        private Transform jawA, jawB;
        private float close;

        private bool marks;

        /// <param name="marksVictim">Rank 3: the catch is also Hunter's-Marked.</param>
        public void ServerArm(float multiplier, ulong ownerId, bool marksVictim = false)
        {
            marks = marksVictim;
            attackMultiplier = multiplier;
            owner = ownerId;
            expiresAt = Time.time + Lifetime;
        }

        public override void OnNetworkSpawn() => BuildVisual();

        private void Update()
        {
            close = Mathf.MoveTowards(close, Sprung.Value ? 1f : 0f, Time.deltaTime * 12f);
            if (jawA != null)
            {
                jawA.localRotation = Quaternion.Euler(Mathf.Lerp(-70f, 0f, close), 0f, 0f);
                jawB.localRotation = Quaternion.Euler(Mathf.Lerp(70f, 0f, close), 0f, 0f);
            }

            if (!IsServer || Sprung.Value) return;
            if (Time.time > expiresAt)
            {
                NetworkObject.Despawn();
                return;
            }

            foreach (var h in Health.All)
            {
                if (h == null || h.Team != Team.Enemies || !h.IsAlive) continue;
                var d = h.transform.position - transform.position;
                if (Mathf.Abs(d.y) > 1f) continue;
                d.y = 0f;
                if (d.magnitude > Radius + (h.TryGetComponent<CapsuleCollider>(out var c) ? c.radius * h.transform.lossyScale.x : 0.3f)) continue;
                Spring(h);
                return;
            }
        }

        private void Spring(Health victim)
        {
            Sprung.Value = true;
            victim.ServerDamage(Damage * attackMultiplier, transform.position, owner);
            if (victim.TryGetComponent<ICrowdControl>(out var cc)) cc.ServerStun(Hold);
            if (marks) victim.ServerMark(6f);
            SnapFxRpc();
            Invoke(nameof(Remove), 3f);
        }

        private void Remove()
        {
            if (NetworkObject != null && NetworkObject.IsSpawned) NetworkObject.Despawn();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void SnapFxRpc() => Sfx.Play(Sound.Block, transform.position + Vector3.up * 0.2f, 1f, 1.4f);

        private void BuildVisual()
        {
            MeshKit.Part(transform, "Base", MeshKit.Cylinder(0.45f, 0.45f, 0.04f, 12), Mat.Steel, Vector3.zero);
            MeshKit.Part(transform, "Plate", MeshKit.Cylinder(0.18f, 0.18f, 0.05f, 8), Mat.Steel, new Vector3(0f, 0.02f, 0f));
            jawA = MeshKit.Pivot(transform, "JawA", new Vector3(0f, 0.04f, 0f));
            jawB = MeshKit.Pivot(transform, "JawB", new Vector3(0f, 0.04f, 0f));
            foreach (var (jaw, z) in new[] { (jawA, 1f), (jawB, -1f) })
            {
                MeshKit.Part(jaw, "Arc", MeshKit.Box(new Vector3(0.8f, 0.04f, 0.04f)), Mat.Steel, new Vector3(0f, 0.2f, 0.02f * z));
                for (int i = 0; i < 6; i++)
                {
                    MeshKit.Part(jaw, "Tooth", MeshKit.Lathe("trapTooth", new[] { new Vector2(0.025f, 0f), new Vector2(0f, 0.08f) }, 4), Mat.Steel,
                        new Vector3(-0.32f + i * 0.13f, 0.2f, 0.02f * z), new Vector3(z > 0 ? -90f : 90f, 0f, 0f));
                }
            }
            foreach (var r in GetComponentsInChildren<Renderer>()) ArtLibrary.Tint(r, new Color(0.65f, 0.6f, 0.55f));
        }
    }
}
