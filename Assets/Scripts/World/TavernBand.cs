using Deepbound.Art;
using Deepbound.Progression;
using Unity.Netcode;
using UnityEngine;

namespace Deepbound.World
{
    public class TavernBand : Interactable
    {
        public const int Fee = 15;

        public readonly NetworkVariable<byte> Song = new NetworkVariable<byte>();
        public readonly NetworkVariable<double> SongStart = new NetworkVariable<double>();
        public static TavernBand Instance { get; private set; }

        private AudioSource source;
        private int playing = -1;
        private Transform[] players;
        private Quaternion[] rest;
        private float busyUntil;

        public override float Range => 3f;

        public static string SongName(int i) => TavernMusic.Songs[Mathf.Clamp(i, 0, TavernMusic.Songs.Length - 1)].Name;
        private int Next => (Song.Value + 1) % TavernMusic.Songs.Length;

        public override string Prompt(PlayerProgress who)
        {
            if (who.Gold.Value < Fee) return "The band plays \"" + SongName(Song.Value) + "\"  (" + Fee + " gold for a request)";
            return "Pay the band " + Fee + " gold:  \"" + SongName(Next) + "\"  (now: " + SongName(Song.Value) + ")";
        }

        protected override void ServerUse(PlayerProgress who)
        {
            if (Time.time < busyUntil || !who.ServerSpendGold(Fee)) return;
            busyUntil = Time.time + 1f;
            ServerPlay(Next, who.name);
        }

        public void ServerPlay(int i, string requestedBy)
        {
            Song.Value = (byte)Mathf.Clamp(i, 0, TavernMusic.Songs.Length - 1);
            SongStart.Value = NetworkManager.ServerTime.Time + 0.5;
            RequestFxRpc(requestedBy, Song.Value);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void RequestFxRpc(string who, byte song)
        {
            Sfx.Play(Sound.Coins, transform.position + Vector3.up, 0.6f, 1.2f);
            UI.GameHud.Feed(who + " pays the band: \"" + SongName(song) + "\"", new Color(0.95f, 0.8f, 0.5f));
            UI.GameHud.ShowCombo(transform.position + Vector3.up * 2.2f, "♪ " + SongName(song), new Color(0.95f, 0.8f, 0.5f));
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            Instance = this;
            if (IsServer) SongStart.Value = NetworkManager.ServerTime.Time;
            var go = new GameObject("BandMusic");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 1.6f, -0.8f);
            source = go.AddComponent<AudioSource>();
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0.85f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 7f;
            source.maxDistance = 42f;
            source.dopplerLevel = 0f;

            var face = transform.position + transform.forward * 4f;
            players = new Transform[3];
            rest = new Quaternion[3];
            var kinds = new[] { Props.Folk.Lutist, Props.Folk.Fiddler, Props.Folk.Drummer };
            for (int i = 0; i < 3; i++)
            {
                var at = transform.position - transform.forward * 0.9f + transform.right * (i - 1) * 0.95f + Vector3.up * 0.25f;
                Props.Townsfolk(transform, at, face + transform.right * (i - 1) * 0.6f, kinds[i]);

                var folk = transform.Find(kinds[i].ToString());
                var pivot = new GameObject("Sway" + i).transform;
                pivot.SetParent(transform, false);
                pivot.position = at;
                if (folk != null) folk.SetParent(pivot, true);
                players[i] = pivot;
                rest[i] = pivot.localRotation;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
            base.OnNetworkDespawn();
        }

        public static float Loudness(Vector3 listener)
        {
            if (Instance == null || Instance.source == null || !Instance.source.isPlaying) return 0f;
            float d = Vector3.Distance(listener, Instance.transform.position);
            return Mathf.Clamp01(1f - (d - 8f) / 22f);
        }

        private void Update()
        {
            if (source == null || NetworkManager == null || !NetworkManager.IsListening) return;

            bool camp = DungeonDirector.InLobby;
            if (!camp) { if (source.isPlaying) source.Stop(); return; }
            int want = Song.Value;
            if (want != playing || !source.isPlaying)
            {
                var clip = TavernMusic.Clip(want);
                if (clip != null)
                {
                    playing = want;
                    source.clip = clip;
                    double since = NetworkManager.ServerTime.Time - SongStart.Value;
                    source.time = since > 0 ? (float)(since % clip.length) : 0f;
                    source.Play();
                }
            }
            source.volume = 0.75f * UI.Settings.MusicVolume;

            if (players != null && source.isPlaying)
            {
                float t = Time.time * (TavernMusic.Songs[playing < 0 ? 0 : playing].Eighth > 0.25f ? 1.6f : 3.4f);
                for (int i = 0; i < players.Length; i++)
                {
                    if (players[i] == null) continue;
                    float s = Mathf.Sin(t + i * 1.7f);
                    players[i].localRotation = rest[i] * Quaternion.Euler(i == 2 ? 4f * Mathf.Abs(s) : 2f, 0f, (i == 1 ? 6f : 4f) * s);
                }
            }
        }
    }
}
