using Deepbound.Art;
using Deepbound.Player;
using Deepbound.World;
using UnityEngine;

namespace Deepbound.UI
{
    /// <summary>
    /// The title screen's set: a lone knight squaring up to the Hollow King between two
    /// braziers, one warm and one cold. Built at runtime from the same generated art as
    /// the game, far below the dungeon, and torn down once the local player spawns.
    /// Sits on the menu camera, which it positions.
    /// </summary>
    public class MenuBackdrop : MonoBehaviour
    {
        private static readonly Vector3 Origin = new Vector3(0f, -500f, 0f);
        private GameObject set;
        private HumanoidRig knight, king;
        private float nextKingAction;

        private void Start()
        {
            DungeonBuilder.ApplyAtmosphere();
            set = new GameObject("MenuBackdrop");
            var b = new MeshKit.Batcher();

            b.AddBox(Origin + Vector3.down * 0.2f, new Vector3(24f, 0.4f, 24f), Mat.Flagstone);
            b.AddBox(Origin + new Vector3(0f, 5f, 7f), new Vector3(24f, 10f, 0.8f), Mat.StoneWall);
            Props.Pillar(b, Origin + new Vector3(-4.5f, 0f, 5f), 9f);
            Props.Pillar(b, Origin + new Vector3(4.5f, 0f, 5f), 9f);
            Props.BonePile(b, Origin + new Vector3(-2.5f, 0f, 2.5f), new System.Random(3));
            Props.Rubble(b, Origin + new Vector3(3.2f, 0f, 3.5f), new System.Random(4));
            Props.Throne(b, Origin + new Vector3(0f, 0f, 5.2f), Quaternion.identity);
            b.Build(set.transform, "Set", collide: false);

            Props.Brazier(set.transform, Origin + new Vector3(-3.2f, 0f, 0.5f), new Color(1f, 0.55f, 0.25f), true);
            Props.Brazier(set.transform, Origin + new Vector3(3.2f, 0f, 2.5f), new Color(0.35f, 0.65f, 1f), true);

            var knightRoot = new GameObject("Knight").transform;
            knightRoot.SetParent(set.transform, false);
            knightRoot.SetPositionAndRotation(Origin + new Vector3(-0.8f, 0f, -1.2f), Quaternion.Euler(0f, 20f, 0f));
            knight = CharacterArt.BuildKnight(knightRoot, NetworkPlayer.SlotColors[0]);
            knight.Blocking = true;

            var kingRoot = new GameObject("King").transform;
            kingRoot.SetParent(set.transform, false);
            kingRoot.SetPositionAndRotation(Origin + new Vector3(0.9f, 0f, 2.6f), Quaternion.Euler(0f, 195f, 0f));
            king = CharacterArt.BuildSkeleton(kingRoot, CharacterArt.SkeletonKind.King);

            transform.SetPositionAndRotation(Origin + new Vector3(-2.6f, 1.5f, -6.2f), Quaternion.Euler(4f, 18f, 0f));
            nextKingAction = Time.time + 2.5f;
        }

        private void Update()
        {
            // Hidden while playing, back when you leave a session.
            bool show = NetworkPlayer.Local == null;
            if (set.activeSelf != show)
            {
                set.SetActive(show);
                if (show) DungeonBuilder.ApplyAtmosphere();
            }
            if (!show) return;

            // The King occasionally winds up, as if about to strike. Pure set dressing.
            if (Time.time >= nextKingAction)
            {
                nextKingAction = Time.time + Random.Range(4f, 7f);
                king.Play(RigAction.Windup, 1.6f);
            }

            // Slow drift so the title screen is alive.
            transform.position += transform.right * Mathf.Sin(Time.time * 0.2f) * 0.0015f;
        }
    }
}
