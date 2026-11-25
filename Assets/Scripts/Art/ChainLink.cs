using UnityEngine;

namespace Deepbound.Art
{
    public class ChainLink : MonoBehaviour
    {
        private float born, life;
        private Transform[] links;
        private Vector3 from, to;

        private Transform fromT, toT;
        private Vector3 fromOff, toOff;
        public const float ReachSeconds = 0.15f;

        public static void Spawn(Vector3 from, Vector3 to, float life) => Spawn(from, to, life, null, null);

        public static void Spawn(Vector3 from, Vector3 to, float life, Transform fromFollow, Transform toFollow)
        {
            var go = new GameObject("ChainLink");
            var c = go.AddComponent<ChainLink>();
            c.from = from;
            c.to = to;
            c.fromT = fromFollow;
            c.toT = toFollow;
            if (fromFollow != null) c.fromOff = from - fromFollow.position;
            if (toFollow != null) c.toOff = to - toFollow.position;
            c.life = life;
            c.born = Time.time;
            int n = Mathf.Clamp(Mathf.RoundToInt(Vector3.Distance(from, to) / 0.16f), 4, 120);
            c.links = new Transform[n];
            for (int i = 0; i < n; i++)
            {
                var link = MeshKit.Part(go.transform, "Link", MeshKit.Box(new Vector3(0.05f, 0.1f, 0.02f)), Mat.Steel, Vector3.zero);
                link.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                c.links[i] = link.transform;
            }
            c.Update();
        }

        private void Update()
        {
            float t = (Time.time - born) / life;
            if (t >= 1.6f) { Destroy(gameObject); return; }
            if (fromT != null) from = fromT.position + fromOff;
            if (toT != null) to = toT.position + toOff;

            float reach = Mathf.Clamp01((Time.time - born) / ReachSeconds);
            float sag = t > 1f ? (t - 1f) * 2.5f : 0f;
            var dir = to - from;
            var rot = Quaternion.LookRotation(dir.sqrMagnitude > 0.001f ? dir : Vector3.forward) * Quaternion.Euler(90f, 0f, 0f);
            for (int i = 0; i < links.Length; i++)
            {
                float u = (i + 0.5f) / links.Length * reach;
                var p = Vector3.Lerp(from, to, u) + Vector3.down * (Mathf.Sin(u * Mathf.PI) * sag);
                links[i].position = p;
                links[i].rotation = rot * Quaternion.Euler(0f, i % 2 == 0 ? 0f : 90f, 0f);
                links[i].gameObject.SetActive(u <= reach);
            }
        }
    }
}
