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
    public class AudioDirector : MonoBehaviour
    {
        private AudioSource explore, combat, boss;
        private float combatHeat;
        private bool started;

        private void Start() => Invoke(nameof(Begin), 0.3f);

        private void Begin()
        {
            explore = MakeSource(MusicSynth.Explore());
            combat = MakeSource(MusicSynth.Combat());
            boss = MakeSource(MusicSynth.Boss());

            double at = AudioSettings.dspTime + 0.2;
            explore.PlayScheduled(at);
            combat.PlayScheduled(at);
            boss.PlayScheduled(at);
            started = true;
        }

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

        private void Update()
        {
            if (!started) return;

            var local = NetworkPlayer.Local;
            float exploreTarget, combatTarget = 0f, bossTarget = 0f;

            if (local == null || DungeonDirector.InLobby)
            {
                exploreTarget = 0.35f;
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

            Fade(explore, exploreTarget);
            Fade(combat, combatTarget);
            Fade(boss, bossTarget);
        }

        private static void Fade(AudioSource s, float target)
        {
            s.volume = Mathf.MoveTowards(s.volume, target, Time.deltaTime * 0.4f);
        }
    }
}
