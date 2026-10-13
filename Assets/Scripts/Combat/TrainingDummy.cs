using Deepbound.Art;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Combat
{
    /// <summary>
    /// A straw dummy in the lobby for trying out a kit. It counts as an enemy for every
    /// hit query, so swings, bashes and the floating damage numbers behave exactly as they
    /// do in the dungeon — but it never fights back and refills a moment after you stop.
    /// It also implements crowd control as no-ops, so Shield Bash and War Cry land on it.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public class TrainingDummy : NetworkBehaviour, ICrowdControl
    {
        private const float ResetDelay = 2.5f;

        private Health health;
        private Transform body;
        private float lastHit = -100f;
        private float wobble;

        private void Awake() => health = GetComponent<Health>();

        public override void OnNetworkSpawn()
        {
            BuildVisual();
            health.Hurt += _ => wobble = 1f;

            if (!IsServer) return;
            health.ServerSetMax(5000f, refill: true);
            health.DamagedServer += (_, __) => lastHit = Time.time;
        }

        private void Update()
        {
            wobble = Mathf.MoveTowards(wobble, 0f, Time.deltaTime * 2.2f);
            if (body != null) body.localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 22f) * 14f * wobble, 0f, Mathf.Cos(Time.time * 17f) * 8f * wobble);

            if (IsServer && Time.time - lastHit > ResetDelay && health.Current.Value < health.Max.Value)
            {
                health.ServerSetFraction(1f);
            }
        }

        public void ServerStun(float seconds) { }
        public void ServerTaunt(Health taunter, float seconds) { }
        public void ServerKnockback(Vector3 velocity) => wobble = 1f;

        private void BuildVisual()
        {
            MeshKit.Part(transform, "Base", MeshKit.Cylinder(0.35f, 0.3f, 0.12f, 8), Mat.Wood, Vector3.zero);
            MeshKit.Part(transform, "Post", MeshKit.Cylinder(0.05f, 0.05f, 1.0f, 6), Mat.Wood, Vector3.zero);

            body = MeshKit.Pivot(transform, "Body", new Vector3(0f, 0.9f, 0f));
            var straw = new Color(0.75f, 0.62f, 0.35f);
            var torso = MeshKit.Part(body, "Torso", MeshKit.Lathe("dummyTorso", new[]
            {
                new Vector2(0.18f, 0f), new Vector2(0.24f, 0.15f), new Vector2(0.25f, 0.45f), new Vector2(0.2f, 0.62f), new Vector2(0.06f, 0.68f),
            }, 8), Mat.Cloth, Vector3.zero);
            ArtLibrary.Tint(torso.GetComponent<Renderer>(), straw);
            MeshKit.Part(body, "Arms", MeshKit.Box(new Vector3(1.0f, 0.08f, 0.08f)), Mat.Wood, new Vector3(0f, 0.5f, 0f));
            var head = MeshKit.Part(body, "Head", MeshKit.Ball(0.15f, 8, 6), Mat.Cloth, new Vector3(0f, 0.85f, 0f));
            ArtLibrary.Tint(head.GetComponent<Renderer>(), straw * 0.9f);
            MeshKit.Part(body, "Rope", MeshKit.Cylinder(0.21f, 0.21f, 0.04f, 8), Mat.Leather, new Vector3(0f, 0.3f, 0f));
            // A painted target on the chest.
            var target = MeshKit.Part(body, "Target", MeshKit.Cylinder(0.12f, 0.12f, 0.01f, 12), Mat.Cloth, new Vector3(0f, 0.38f, 0.24f), new Vector3(90f, 0f, 0f));
            ArtLibrary.Tint(target.GetComponent<Renderer>(), new Color(0.7f, 0.1f, 0.08f));
        }
    }
}
