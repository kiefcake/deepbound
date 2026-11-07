using Deepbound.Loot;
using Deepbound.Player;
using Deepbound.Progression;
using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// What the Panoply's armour looks like on whoever wears it, on every peer: Rimecloak frost motes and a pale ring,
    /// the Sunward Plate's gold motes, and the Wraithstep Cuirass's ghost trail while it runs. Local and cheap: only
    /// players within 40 m of the camera, a few motes a second.
    /// </summary>
    public class ArmourAuraFx : MonoBehaviour
    {
        private static readonly Color Rime = new Color(0.6f, 0.85f, 1f), Sun = new Color(1f, 0.85f, 0.45f), Ghost = new Color(0.75f, 0.7f, 1f);
        private float next;
        private readonly System.Collections.Generic.Dictionary<PlayerLife, Vector3> lastPos = new System.Collections.Generic.Dictionary<PlayerLife, Vector3>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (Application.isBatchMode) return;
            var go = new GameObject("ArmourAuraFx");
            DontDestroyOnLoad(go);
            go.AddComponent<ArmourAuraFx>();
        }

        private void Update()
        {
            if (Time.time < next) return;
            next = Time.time + 0.18f;
            var cam = ViewCamera.Current;
            if (cam == null) return;
            foreach (var life in PlayerLife.All)
            {
                if (life == null || life.Downed.Value || !life.TryGetComponent<PlayerProgress>(out var p)) continue;
                var feet = life.transform.position;
                if ((feet - cam.transform.position).sqrMagnitude > 40f * 40f) continue;
                bool mine = life.IsOwner;
                // Others' speed, from how far they moved since the last look (0.18 s ago).
                float speed = lastPos.TryGetValue(life, out var was) ? Vector3.Distance(was, feet) / 0.18f : 0f;
                lastPos[life] = feet;
                if (p.Wears(GearId.Rimecloak))
                {
                    var off = Random.insideUnitCircle * 1.6f;
                    Vfx.Mote(feet + new Vector3(off.x, mine ? 0.1f : 0.3f, off.y), Rime);
                    if (Random.value < 0.08f) Vfx.Ring(feet + Vector3.up * 0.05f, 4f, new Color(Rime.r, Rime.g, Rime.b, 0.25f), 0.6f);
                }
                if (p.Wears(GearId.SunwardPlate))
                {
                    var off = Random.insideUnitCircle * 1.2f;
                    Vfx.Mote(feet + new Vector3(off.x, mine ? 0.1f : 1.6f, off.y), Sun);
                }
                if (p.Wears(GearId.WraithstepCuirass) && (mine ? p.Wraithstepping : speed > 9f))
                    Vfx.Embers(feet + Vector3.up * 0.9f, 3, Ghost, 0.6f, 0.06f, 0.5f);
            }
        }
    }
}
