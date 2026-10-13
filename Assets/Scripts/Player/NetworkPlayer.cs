using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace Deepbound.Player
{
    /// <summary>
    /// Decides, per peer, what a spawned player object is: the local player you control,
    /// or someone else's avatar you only see.
    ///
    /// <para>
    /// Every peer gets an instance of the same prefab for every player. On the owner this
    /// turns on the camera, the audio listener and <see cref="FirstPersonMotor"/>, and hides
    /// the body from its own camera (it still casts a shadow, which grounds you in the
    /// world). On everyone else it leaves all of that off, so the transform is driven purely
    /// by the owner-authoritative <c>NetworkTransform</c>.
    /// </para>
    ///
    /// <para>
    /// Movement is owner-authoritative for M0: the client moves itself and the server
    /// accepts it. That is the right call for a co-op game — it feels instant with no
    /// prediction code — but it does mean a modified client can move anywhere. Anything
    /// that matters for fairness (damage, loot, potions) stays server-authoritative.
    /// </para>
    /// </summary>
    public class NetworkPlayer : NetworkBehaviour
    {
        [SerializeField] private Camera playerCamera;
        [SerializeField] private AudioListener audioListener;
        [SerializeField] private FirstPersonMotor motor;
        [SerializeField] private Renderer[] bodyRenderers;

        /// <summary>Radius of the ring players are spread around when they spawn.</summary>
        private const float SpawnRingRadius = 3f;

        /// <summary>
        /// One colour per player slot, so in a four-player session everyone can tell who is
        /// who before there are names or class models.
        /// </summary>
        private static readonly Color[] SlotColors =
        {
            new Color(0.90f, 0.30f, 0.25f), // red
            new Color(0.25f, 0.55f, 0.95f), // blue
            new Color(0.35f, 0.85f, 0.40f), // green
            new Color(0.95f, 0.80f, 0.25f), // yellow
        };

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        /// <summary>The player this machine controls, or null before spawning.</summary>
        public static NetworkPlayer Local { get; private set; }

        public override void OnNetworkSpawn()
        {
            ApplySlotColor();

            bool isLocal = IsOwner;
            playerCamera.enabled = isLocal;
            audioListener.enabled = isLocal;

            foreach (var body in bodyRenderers)
            {
                body.shadowCastingMode = isLocal ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            }

            if (!isLocal) return;

            Local = this;

            // Spread players around the origin by client id so two people joining do not
            // spawn inside each other. The owner places itself; the owner-authoritative
            // NetworkTransform then carries that position to everyone else.
            float angle = OwnerClientId * Mathf.PI * 0.5f;
            var spawn = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * SpawnRingRadius + Vector3.up * 0.1f;
            var facing = Quaternion.LookRotation(new Vector3(-spawn.x, 0f, -spawn.z));

            motor.Teleport(spawn, facing);
            motor.enabled = true;
        }

        public override void OnNetworkDespawn()
        {
            if (Local == this) Local = null;
            motor.enabled = false;
        }

        private void ApplySlotColor()
        {
            // Derived from the client id rather than synced: every peer computes the same
            // answer, so there is no NetworkVariable to keep in step.
            var color = SlotColors[(int)(OwnerClientId % (ulong)SlotColors.Length)];

            var block = new MaterialPropertyBlock();
            foreach (var body in bodyRenderers)
            {
                body.GetPropertyBlock(block);
                block.SetColor(BaseColorId, color);
                body.SetPropertyBlock(block);
            }
        }
    }
}
