using Deepbound.World;
using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// ROOM ACOUSTICS (2026-11-16, the audio pass): one reverb zone rides with the listener and eases between spaces as
    /// you walk, so the same sword swing rings long in a great hall, comes back tight in a corridor, booms in a cave and is
    /// almost dry under the open sky at camp. Music and UI clicks opt out (<c>reverbZoneMix</c> 0); every positioned
    /// sound in the world goes through it. Client-side; made at startup.
    /// </summary>
    public class Acoustics : MonoBehaviour
    {
        /// <summary>What the room sounds like (the I3DL2 numbers Unity's reverb zone takes; levels in millibels).</summary>
        public struct Space
        {
            public string Name;
            public float Room, RoomHF, Decay, DecayHFRatio, Reflections, ReflectionsDelay, Reverb, ReverbDelay;

            public static Space Lerp(Space a, Space b, float t) => new Space
            {
                Name = t < 0.5f ? a.Name : b.Name,
                Room = Mathf.Lerp(a.Room, b.Room, t), RoomHF = Mathf.Lerp(a.RoomHF, b.RoomHF, t),
                Decay = Mathf.Lerp(a.Decay, b.Decay, t), DecayHFRatio = Mathf.Lerp(a.DecayHFRatio, b.DecayHFRatio, t),
                Reflections = Mathf.Lerp(a.Reflections, b.Reflections, t), ReflectionsDelay = Mathf.Lerp(a.ReflectionsDelay, b.ReflectionsDelay, t),
                Reverb = Mathf.Lerp(a.Reverb, b.Reverb, t), ReverbDelay = Mathf.Lerp(a.ReverbDelay, b.ReverbDelay, t),
            };
        }

        // Levels sit a little under the classic presets: the recordings already carry some room, and a dungeon full of
        // swings should ring, not wash.
        public static readonly Space Outdoors = new Space { Name = "outdoors", Room = -2600, RoomHF = -2000, Decay = 1.2f, DecayHFRatio = 0.5f, Reflections = -2500, ReflectionsDelay = 0.12f, Reverb = -2400, ReverbDelay = 0.08f };
        public static readonly Space Corridor = new Space { Name = "corridor", Room = -1200, RoomHF = -260, Decay = 1.9f, DecayHFRatio = 0.75f, Reflections = -1200, ReflectionsDelay = 0.011f, Reverb = 250, ReverbDelay = 0.018f };
        public static readonly Space Chamber = new Space { Name = "chamber", Room = -1200, RoomHF = -320, Decay = 1.7f, DecayHFRatio = 0.64f, Reflections = -800, ReflectionsDelay = 0.012f, Reverb = 0, ReverbDelay = 0.017f };
        public static readonly Space Hall = new Space { Name = "hall", Room = -1300, RoomHF = -480, Decay = 3.2f, DecayHFRatio = 0.6f, Reflections = -900, ReflectionsDelay = 0.02f, Reverb = -250, ReverbDelay = 0.03f };
        public static readonly Space Nave = new Space { Name = "nave", Room = -1300, RoomHF = -520, Decay = 4.6f, DecayHFRatio = 0.62f, Reflections = -1100, ReflectionsDelay = 0.024f, Reverb = -300, ReverbDelay = 0.04f };
        public static readonly Space Cave = new Space { Name = "cave", Room = -1200, RoomHF = 0, Decay = 2.9f, DecayHFRatio = 1.2f, Reflections = -650, ReflectionsDelay = 0.015f, Reverb = -350, ReverbDelay = 0.022f };

        /// <summary>The space the listener is in now (eased), and the one it is heading for.</summary>
        public static Space Current { get; private set; } = Outdoors;
        public static Space Target { get; private set; } = Outdoors;

        private static Acoustics instance;
        private AudioReverbZone zone;
        private float nextLook, blend = 1f;
        private Space from = Outdoors;
        private float appliedEcho = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (instance != null) return;
            var go = new GameObject("Acoustics");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<Acoustics>();
        }

        private void Awake()
        {
            zone = gameObject.AddComponent<AudioReverbZone>();
            // The zone follows the ear, so the ear is always well inside it.
            zone.minDistance = 500f;
            zone.maxDistance = 600f;
            zone.reverbPreset = AudioReverbPreset.User;
            Apply(Current);
        }

        /// <summary>What a spot sounds like: by the room it is in, its size and what is built in it.</summary>
        public static Space SpaceAt(Vector3 at)
        {
            var layout = DungeonDirector.Layout;
            if (DungeonDirector.Instance == null || DungeonDirector.InLobby || layout == null) return Outdoors;
            if (DungeonDirector.InPit) return Cave;
            var room = layout.RoomAt(at, 0f);
            if (room == null) return Corridor;
            switch (room.Feature)
            {
                case RoomFeature.Undercroft:
                case RoomFeature.Chasm:
                case RoomFeature.Cistern:
                    return Cave;
            }
            float area = room.Size.x * room.Size.y;
            if (room.Kind == RoomKind.Boss || area >= 900f || room.Feature == RoomFeature.Belfry) return Nave;
            if (area >= 420f || room.Feature == RoomFeature.GreatHall || room.Feature == RoomFeature.Mezzanine) return Hall;
            return Chamber;
        }

        private void Update()
        {
            var cam = ViewCamera.Current;
            if (cam != null) transform.position = cam.transform.position;
            if (Time.unscaledTime >= nextLook)
            {
                nextLook = Time.unscaledTime + 0.25f;
                var want = SpaceAt(transform.position);
                if (want.Name != Target.Name) { from = Current; Target = want; blend = 0f; }
            }
            float echo = UI.Settings.RoomEcho;
            if (!Mathf.Approximately(echo, appliedEcho)) { appliedEcho = echo; Apply(Current); }
            if (blend < 1f)
            {
                // About a second to walk out of one space's sound into the next.
                blend = Mathf.Min(1f, blend + Time.unscaledDeltaTime * 1.1f);
                Current = Space.Lerp(from, Target, Mathf.SmoothStep(0f, 1f, blend));
                Apply(Current);
            }
        }

        private void Apply(Space s)
        {
            if (zone == null) return;
            // The Room echo setting scales the whole wet level (millibels: 2000 log10 of the gain); 0 turns it off.
            float echo = UI.Settings.RoomEcho;
            zone.room = echo <= 0.01f ? -10000 : Mathf.Clamp(Mathf.RoundToInt(s.Room + 2000f * Mathf.Log10(echo)), -10000, 0);
            zone.roomHF = Mathf.RoundToInt(s.RoomHF);
            zone.decayTime = s.Decay;
            zone.decayHFRatio = s.DecayHFRatio;
            zone.reflections = Mathf.RoundToInt(s.Reflections);
            zone.reflectionsDelay = s.ReflectionsDelay;
            zone.reverb = Mathf.RoundToInt(s.Reverb);
            zone.reverbDelay = s.ReverbDelay;
            zone.diffusion = 100f;
            zone.density = 100f;
        }
    }
}
