using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// A looped sound at a spot (2026-10-24: the fountain's running water), played only while the listener is
    /// within <see cref="Range"/> so a floor of them costs nothing. Client-side; added at runtime.
    /// </summary>
    public class AmbientLoop : MonoBehaviour
    {
        public float Range = 12f, Volume = 0.3f;
        private AudioSource source;
        private float nextCheck;

        public static AmbientLoop Attach(GameObject on, string resource, float range, float volume, Vector3 offset)
        {
            var clip = Resources.Load<AudioClip>(resource);
            if (clip == null) return null;
            var go = new GameObject("AmbientLoop");
            go.transform.SetParent(on.transform, false);
            go.transform.localPosition = offset;
            var loop = go.AddComponent<AmbientLoop>();
            loop.Range = range;
            loop.Volume = volume;
            var s = go.AddComponent<AudioSource>();
            s.clip = clip; s.loop = true; s.playOnAwake = false;
            s.spatialBlend = 1f; s.minDistance = 1.5f; s.maxDistance = range;
            s.rolloffMode = AudioRolloffMode.Linear; s.dopplerLevel = 0f; s.volume = 0f;
            loop.source = s;
            return loop;
        }

        private void Update()
        {
            if (source == null) return;
            if (Time.time >= nextCheck)
            {
                nextCheck = Time.time + 0.5f;
                var cam = ViewCamera.Current;
                bool near = cam != null && (cam.transform.position - transform.position).sqrMagnitude < Range * Range;
                if (near && !source.isPlaying) { source.time = Random.value * Mathf.Max(0f, source.clip.length - 0.1f); source.Play(); }
                else if (!near && source.isPlaying) source.Stop();
            }
            if (source.isPlaying) source.volume = Mathf.MoveTowards(source.volume, Volume * UI.Settings.SfxVolume, Time.deltaTime * 0.5f);
            else source.volume = 0f;
        }
    }
}
