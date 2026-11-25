using Deepbound.World;
using UnityEngine;

namespace Deepbound.Art
{
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

                if (DungeonDirector.InLobby) want = 0.5f;
            }
            level = Mathf.MoveTowards(level, want, Time.deltaTime * 1.5f);
            glow.intensity = Intensity * level;
            glow.enabled = level > 0.001f;
        }
    }
}
