using Deepbound.Art;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace Deepbound.World
{
    /// <summary>
    /// An iron portcullis in a doorway. Raised, it hides in the lintel; lowered, it blocks the
    /// doorway for players (collider) and for enemy pathing (a carving NavMeshObstacle).
    /// Boss arenas and ambush rooms drop them to lock a fight in.
    /// </summary>
    public class Portcullis : NetworkBehaviour
    {
        public readonly NetworkVariable<bool> Closed = new NetworkVariable<bool>();

        private Transform bars;
        private float drop;
        private BoxCollider block;
        private NavMeshObstacle obstacle;

        public override void OnNetworkSpawn()
        {
            block = GetComponent<BoxCollider>();
            obstacle = GetComponent<NavMeshObstacle>();
            BuildVisual();
            Closed.OnValueChanged += (_, now) => OnChanged(now);
            OnChanged(Closed.Value);
            drop = Closed.Value ? 1f : 0f;
        }

        private void OnChanged(bool closed)
        {
            if (block != null) block.enabled = closed;
            if (obstacle != null) obstacle.enabled = closed;
            Sfx.Play(closed ? Sound.Slam : Sound.Bash, transform.position + Vector3.up * 2f, closed ? 0.9f : 0.6f, closed ? 0.7f : 0.6f);
            Sfx.Play(Sound.Door, transform.position + Vector3.up * 1.5f, 0.8f, 0.7f);
            Sfx.Play(Sound.Chain, transform.position + Vector3.up * 3f, 0.7f, 0.75f);
        }

        private void Update()
        {
            // Slams down fast; grinds up slowly.
            drop = Mathf.MoveTowards(drop, Closed.Value ? 1f : 0f, Time.deltaTime * (Closed.Value ? 6f : 1.2f));
            if (bars != null) bars.localPosition = new Vector3(0f, Mathf.Lerp(DungeonLayout.CorridorHeight, 0f, drop), 0f);
        }

        private void BuildVisual()
        {
            bars = MeshKit.Pivot(transform, "Bars", Vector3.up * DungeonLayout.CorridorHeight);
            float w = DungeonLayout.CorridorWidth, h = DungeonLayout.CorridorHeight;
            for (int i = 0; i < 7; i++)
            {
                float x = -w / 2f + 0.25f + i * (w - 0.5f) / 6f;
                MeshKit.Part(bars, "Bar" + i, MeshKit.Box(new Vector3(0.07f, h, 0.07f)), Mat.Steel, new Vector3(x, h / 2f, 0f));
                MeshKit.Part(bars, "Spike" + i, MeshKit.Lathe("gateSpike", new[] { new Vector2(0.05f, 0f), new Vector2(0f, 0.18f) }, 4), Mat.Steel,
                    new Vector3(x, 0f, 0f), new Vector3(180f, 0f, 0f));
            }
            for (int j = 0; j < 3; j++)
                MeshKit.Part(bars, "Rail" + j, MeshKit.Box(new Vector3(w, 0.07f, 0.09f)), Mat.Steel, new Vector3(0f, 0.6f + j * 1.2f, 0f));
            foreach (var r in bars.GetComponentsInChildren<Renderer>()) ArtLibrary.Tint(r, new Color(0.45f, 0.42f, 0.4f));
        }
    }
}
