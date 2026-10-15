using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// Dust and ash drifting in the air around the local camera, tinted by the floor: grey
    /// grit in the Ossuary, pale spores in the Drowned Vaults, violet glints under the
    /// Hollow Throne, embers in the Black Cathedral. Pure atmosphere: a few specks a
    /// second, spawned in a shell around the viewer so the air never looks empty.
    /// </summary>
    public class AmbientMotes : MonoBehaviour
    {
        private float next;

        private void Update()
        {
            if (Time.time < next) return;
            next = Time.time + 0.06f;
            var cam = ViewCamera.Current;
            if (cam == null) return;
            var d = World.DungeonDirector.Layout;
            Color c;
            if (World.DungeonDirector.InLobby) c = new Color(1f, 0.6f, 0.3f) * 0.5f;
            else if (d == null) return;
            else c = d.Depth == 0 ? new Color(0.6f, 0.6f, 0.62f) * 0.45f
                : d.Depth == 1 ? new Color(0.5f, 0.85f, 0.75f) * 0.5f
                : d.Depth == 2 ? new Color(0.65f, 0.55f, 1f) * 0.5f
                : new Color(1f, 0.45f, 0.25f) * 0.6f;
            var p = cam.transform.position + Random.onUnitSphere * Random.Range(2.5f, 7f);
            if (p.y < cam.transform.position.y - 2.2f) p.y = cam.transform.position.y - 2.2f + Random.value;
            Vfx.Speck(p, c);
        }
    }
}
