using Deepbound.Art;
using Deepbound.Combat;
using Deepbound.Enemies;
using Deepbound.Player;
using Deepbound.World;
using UnityEngine;

namespace Deepbound.UI
{
    /// <summary>
    /// Plays the three music layers in lockstep and cross-fades them by situation:
    /// exploring, enemies on you, or a boss fight. Camp and the title screen get a quieter
    /// mix of the exploration layer.
    ///
    /// The layers are generated on a background frame after startup (they take a moment
    /// to synthesise), started together, and never stopped — only their volumes move, so
    /// a fight starting mid-bar slides the drums in on the beat they would be on anyway.
    /// </summary>
    /// <remarks>
    /// 2026-10-24: recorded CC0 music and room tone (Resources/Audio/Music and Ambience, see docs/CREDITS.md) take the
    /// place of the synthesised layers when present: a tomb ambience piece to explore by, a shrine loop for the camp and
    /// the Black Cathedral, an orchestral battle theme, a looping boss battle, a dramatic sting when an arena seals, and
    /// wind-and-drip or cavern air under it all. Recorded layers loop on their own (they are not one length); only
    /// their volumes move.
    /// </remarks>
    public class AudioDirector : MonoBehaviour
    {
        private AudioSource explore, combat, boss, campAir, deepAir, shrine, cavernAir, sting, vaults, finale, rainAir;
        private float combatHeat;
        private bool started;
        private static AudioDirector instance;

        private void Start() { instance = this; Invoke(nameof(Begin), 0.3f); }

        private static AudioClip Load(string path) => Resources.Load<AudioClip>("Audio/" + path);

        private void Begin()
        {
            var recExplore = Load("Music/explore");
            var recCombat = Load("Music/combat");
            var recBoss = Load("Music/boss");
            bool recorded = recExplore != null && recCombat != null && recBoss != null;
            recordedMusic = recorded;
            // All recorded (2026-11-01: the synthesised fallbacks are gone, "no more coded noise").
            explore = MakeSource(recExplore);
            combat = MakeSource(recCombat);
            boss = MakeSource(recBoss);
            var recShrine = Load("Music/explore_cathedral");
            shrine = MakeSource(recShrine != null ? recShrine : recExplore);
            // Acts two and three (the Drowned Vaults, the Hollow Throne) explore to their own piece, if it is there.
            var recVaults = Load("Music/explore_deep");
            if (recVaults != null) vaults = MakeSource(recVaults);
            // The Unsung fights to its own piece (2026-10-27, skrjablin's "The Final Battle", CC0).
            var recFinale = Load("Music/boss_final");
            if (recFinale != null) finale = MakeSource(recFinale);
            // Camp: wind over the walls (CC0, "Wind Whoosh Loop", OpenGameArt).
            campAir = MakeSource(Load("Ambience/camp"));
            // Rain on the camp when the weather turns (2026-11-08, Ylmir's "Rain (loopable)", CC0).
            var recRain = Load("Ambience/rain");
            if (recRain != null) { rainAir = MakeSource(recRain); rainAir.Play(); }
            var recDeep = Load("Ambience/dungeon");
            deepAir = MakeSource(recDeep);
            var recCavern = Load("Ambience/cavern");
            cavernAir = MakeSource(recCavern != null ? recCavern : recDeep);
            sting = gameObject.AddComponent<AudioSource>();
            sting.spatialBlend = 0f;
            sting.playOnAwake = false;
            campAir.Play();
            deepAir.Play();
            cavernAir.Play();

            double at = AudioSettings.dspTime + 0.2;
            explore.PlayScheduled(at);
            combat.PlayScheduled(at);
            boss.PlayScheduled(at);
            shrine.PlayScheduled(at);
            if (vaults != null) vaults.PlayScheduled(at);
            if (finale != null) finale.PlayScheduled(at);
            started = true;
        }

        /// <summary>The arena seals: a dramatic sting over everything (the boss loop is held back while it plays).</summary>
        public static void BossIntro()
        {
            if (instance == null || instance.sting == null) return;
            var clip = Load("Music/boss_intro");
            if (clip == null) return;
            instance.sting.clip = clip;
            instance.sting.volume = 0.7f * Settings.MusicVolume;
            instance.sting.Play();
            instance.stingUntil = Time.time + Mathf.Min(clip.length, 9f);
        }
        private float stingUntil;
        private bool recordedMusic;

        private AudioSource MakeSource(AudioClip clip)
        {
            var s = gameObject.AddComponent<AudioSource>();
            s.clip = clip;
            s.loop = true;
            s.volume = 0f;
            s.spatialBlend = 0f;
            s.playOnAwake = false;
            return s;
        }

        private void OnApplicationFocus(bool focused) => AudioListener.volume = !focused && Settings.MuteInBackground ? 0f : Settings.Volume;

        private void Update()
        {
            // Kept in step every frame: the focus event alone missed a game started or a setting changed in the background.
            float listenerWant = !Application.isFocused && Settings.MuteInBackground ? 0f : Settings.Volume;
            if (!Mathf.Approximately(AudioListener.volume, listenerWant)) AudioListener.volume = listenerWant;
            if (sting != null && sting.isPlaying) sting.volume = 0.7f * Settings.MusicVolume;
            if (!started) return;

            var local = NetworkPlayer.Local;
            float exploreTarget, combatTarget = 0f, bossTarget = 0f, shrineTarget = 0f, vaultsTarget = 0f;

            if (local == null || DungeonDirector.InLobby)
            {
                // Camp: the shrine loop, low - and it steps aside for the tavern band near the Gutted Lantern.
                exploreTarget = 0f;
                var ear = local != null ? local.transform.position : Lobby.Origin;
                shrineTarget = 0.3f * (1f - World.TavernBand.Loudness(ear));
            }
            else
            {
                exploreTarget = 0.5f;
                int threats = 0;
                foreach (var h in Health.All)
                {
                    if (h == null || h.Team != Team.Enemies || !h.IsAlive || h.GetComponent<TrainingDummy>() != null) continue;
                    if (Vector3.Distance(h.transform.position, local.transform.position) < 16f && Time.time - h.LastHurtTime < 8f) threats++;
                }
                // Being hit counts too: an archer you have not touched is still a fight.
                var myHealth = local.GetComponent<Health>();
                if (myHealth != null && Time.time - myHealth.LastHurtTime < 6f) threats++;

                // Heat builds while you fight and lingers a little after, so the drums do
                // not stutter between swings.
                combatHeat = Mathf.MoveTowards(combatHeat, threats > 0 ? 1f : 0f, Time.deltaTime * (threats > 0 ? 0.8f : 0.15f));
                combatTarget = combatHeat * 0.55f;

                var b = BossBrain.Active;
                var room = DungeonDirector.Layout?.BossRoom;
                if (b != null && !b.IsDead && room != null && room.Contains(local.transform.position, 1f))
                {
                    bossTarget = 0.6f;
                    combatTarget = Mathf.Max(combatTarget, 0.4f);
                    exploreTarget = 0.25f;
                }
            }

            // Recorded tracks are not one key and tempo like the synth layers: one at a time, crossfaded.
            if (recordedMusic)
            {
                exploreTarget *= 1f - combatHeat;
                if (bossTarget > 0f) { exploreTarget = 0f; combatTarget = 0f; }
            }
            // The Black Cathedral explores to the shrine loop instead of the tomb piece.
            int act = DungeonDirector.Layout != null ? DungeonDirector.Act(DungeonDirector.Layout.Depth) : 0;
            if (local != null && !DungeonDirector.InLobby && act == 3) { shrineTarget = exploreTarget; exploreTarget = 0f; }
            else if (vaults != null && local != null && !DungeonDirector.InLobby && (act == 1 || act == 2)) { vaultsTarget = exploreTarget; exploreTarget = 0f; }
            // The sting has the stage for its few seconds.
            if (Time.time < stingUntil) { bossTarget = 0f; combatTarget *= 0.3f; }
            float finaleTarget = 0f;
            if (finale != null && BossBrain.Active is UnsungBrain) { finaleTarget = bossTarget; bossTarget = 0f; }

            float music = Settings.MusicVolume;
            Fade(explore, exploreTarget * music);
            Fade(combat, combatTarget * music);
            Fade(boss, bossTarget * music);
            if (finale != null) Fade(finale, finaleTarget * music);
            Fade(shrine, shrineTarget * music);
            if (vaults != null) Fade(vaults, vaultsTarget * music);
            // Room tone under the music: night air at camp, the dungeon's breath below; the deeper acts are cavern air.
            bool camp = local == null || DungeonDirector.InLobby;
            bool deep = act >= 3;
            float air = camp ? 0f : bossTarget > 0f ? 0.12f : 0.3f;
            Fade(campAir, (camp ? 0.22f : 0f) * music);
            if (rainAir != null)
            {
                // Indoors (the tavern) it is the drum on the roof: quieter.
                float rain = camp ? World.CampWeather.RainK * (local != null && World.Lobby.InTavern(local.transform.position) ? 0.1f : 0.3f) : 0f;
                Fade(rainAir, rain * music);
            }
            Fade(deepAir, (deep ? 0f : air) * music);
            Fade(cavernAir, (deep ? air : 0f) * music);
            if (cavernAir != null) cavernAir.pitch = DungeonDirector.Layout != null && DungeonDirector.Layout.Depth >= DungeonDirector.AbyssDepth ? 0.8f : 1f;
        }

        private static void Fade(AudioSource s, float target)
        {
            s.volume = Mathf.MoveTowards(s.volume, target, Time.deltaTime * 0.4f);
        }
    }
}
