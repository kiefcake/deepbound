using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// The dead are never quite silent (2026-10-20, first-person tension): every so often a creature makes a
    /// small sound of its own (bones knocking, a wraith's whisper, an acolyte humming, an eye's wet hiss),
    /// quieter than combat and only within earshot, so you hear a room before you see it. Local to each
    /// peer: nothing is sent.
    /// </summary>
    public class EnemyMurmur : MonoBehaviour
    {
        public const float Earshot = 20f;

        private Enemies.EnemyBrain brain;
        private float nextAt;
        private static float globalNext;
        private Sound sound = Sound.Rattle;
        private float pitch = 0.85f, volume = 0.28f, height = 1.2f;

        public void Init(Enemies.EnemyBrain owner)
        {
            brain = owner;
            nextAt = Time.time + Random.Range(3f, 12f);
            switch (owner.GetType().Name)
            {
                case "WraithBrain": sound = Sound.Shade; pitch = 1f; volume = 0.35f; height = 1.6f; break;
                case "ChoirAcolyteBrain": sound = Sound.Revive; pitch = 0.42f; volume = 0.22f; height = 1.6f; break;
                case "AbyssEyeBrain": sound = Sound.Whisper; pitch = 0.6f; volume = 0.3f; height = 1.8f; break;
                case "GhoulBrain": sound = Sound.Whisper; pitch = 0.5f; volume = 0.3f; height = 1f; break;
                case "BruteBrain": sound = Sound.Step; pitch = 0.45f; volume = 0.45f; height = 0.3f; break;
                case "NecromancerBrain": case "HexerBrain": sound = Sound.Whisper; pitch = 0.8f; volume = 0.25f; height = 1.6f; break;
                case "CantorBrain": sound = Sound.Toll; pitch = 1.9f; volume = 0.12f; height = 1.6f; break;
                case "BonePiperBrain": sound = Sound.Pipe; pitch = 0.6f; volume = 0.2f; height = 1.3f; break;
                case "PlaguebearerBrain": sound = Sound.Slime; pitch = 0.8f; volume = 0.35f; height = 1f; break;
                case "MournerBrain": case "GargoyleBrain": case "MimicBrain": sound = Sound.Rattle; volume = 0f; break; // statues and chests keep their silence
                default: sound = Sound.Rattle; pitch = 0.85f; volume = 0.28f; height = 1.2f; break;
            }
        }

        private void Update()
        {
            if (brain == null || volume <= 0f || Time.time < nextAt) return;
            nextAt = Time.time + Random.Range(6f, 14f);
            if (brain.IsDead) return;
            var cam = ViewCamera.Current;
            if (cam == null || Vector3.Distance(cam.transform.position, transform.position) > Earshot) return;
            // A crowded room murmurs, it does not chatter: one at a time across the whole level.
            if (Time.time < globalNext) return;
            globalNext = Time.time + 0.7f;
            Sfx.Play(sound, transform.position + Vector3.up * height, volume, pitch * Random.Range(0.92f, 1.08f));
        }
    }
}
