using Deepbound.World;
using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// The presence light (2026-10-31): a faint warm glow that rides with the view camera, the lantern-light of every
    /// dungeon crawler, so the dead in front of you are a face and not a black cut-out against the torches behind them.
    /// Short reach: far rooms keep their dark. It hangs <see cref="Ahead"/> m in front of the eye (pulled in before a
    /// wall): Forward+ ignores a light's culling mask, so at the eye it flooded the first-person hands white. It goes
    /// out in a Lightless Hall, where the carried torch is the point. Local only; booted on load.
    /// </summary>
    public class PresenceLight : MonoBehaviour
    {
        public const float Range = 8f, Intensity = 1.1f, Ahead = 3f;
        private static readonly Color Warm = new Color(1f, 0.83f, 0.64f);

        private Light glow;
        private float level;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Application.isBatchMode) return;
            var go = new GameObject("PresenceLight");
            DontDestroyOnLoad(go);
            go.AddComponent<PresenceLight>();
        }

        private void Awake()
        {
            glow = gameObject.AddComponent<Light>();
            glow.type = LightType.Point;
            glow.range = Range;
            glow.color = Warm;
            glow.intensity = 0f;
            glow.shadows = LightShadows.None;
            glow.cullingMask = ~(1 << Player.NetworkPlayer.ViewModelLayerIndex);
            glow.renderMode = LightRenderMode.ForcePixel;
        }

        private void LateUpdate()
        {
            var cam = ViewCamera.Current;
            float want = 0f;
            if (cam != null && Player.NetworkPlayer.Local != null)
            {
                // Ahead of the eye and a little up, short of any wall: on faces and the floor in front, never on the hands.
                var eye = cam.transform.position;
                var fwd = cam.transform.forward;
                float d = Ahead;
                if (Physics.Raycast(eye, fwd, out var hit, Ahead + 0.4f, ~(1 << Player.NetworkPlayer.ViewModelLayerIndex), QueryTriggerInteraction.Ignore))
                    d = Mathf.Max(1.4f, hit.distance - 0.4f);
                transform.position = eye + fwd * d + Vector3.up * 0.3f;
                want = 1f;
                var layout = DungeonDirector.Layout;
                var room = layout != null && !DungeonDirector.InLobby ? layout.RoomAt(cam.transform.position) : null;
                if (room != null && room.Feature == RoomFeature.Lightless) want = 0f;
                // Camp has its own lamps and the moon.
                if (DungeonDirector.InLobby) want = 0.5f;
            }
            level = Mathf.MoveTowards(level, want, Time.deltaTime * 1.5f);
            glow.intensity = Intensity * level;
            glow.enabled = level > 0.001f;
        }
    }
}
