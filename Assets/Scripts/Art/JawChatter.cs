using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// A skeleton's jaw working on its hinge: idle clacks now and then, and it gapes while the rig
    /// winds up an attack or dies. Pure local animation.
    /// </summary>
    public class JawChatter : MonoBehaviour
    {
        private HumanoidRig rig;
        private float open, nextClack, clackUntil, seed;

        private void Start()
        {
            rig = GetComponentInParent<HumanoidRig>();
            seed = Random.value * 50f;
            nextClack = Time.time + Random.Range(0.5f, 4f);
        }

        private void Update()
        {
            float want = 0.08f + (Mathf.PerlinNoise(Time.time * 0.6f, seed) - 0.5f) * 0.08f;
            if (Time.time >= nextClack)
            {
                clackUntil = Time.time + Random.Range(0.25f, 0.6f);
                nextClack = clackUntil + Random.Range(1.5f, 5f);
            }
            if (Time.time < clackUntil) want = Mathf.Abs(Mathf.Sin(Time.time * 26f + seed)) * 0.7f;
            if (rig != null && (rig.Current == RigAction.Windup || rig.Current == RigAction.WindHeavy || rig.Current == RigAction.WindSweep || rig.Current == RigAction.Charge || rig.Current == RigAction.Summon || rig.Current == RigAction.Channel || rig.Current == RigAction.Death || rig.Current == RigAction.Cast)) want = 1f;
            open = Mathf.MoveTowards(open, want, Time.deltaTime * 9f);
            transform.localRotation = Quaternion.Euler(open * 28f, 0f, 0f);
        }
    }
}
