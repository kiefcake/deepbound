using UnityEngine;

namespace Deepbound.Art
{
    public class RegaliaSwing : MonoBehaviour
    {
        private float phase;

        private void Awake() => phase = Random.value * 6f;

        private void Update() => transform.localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 1.7f + phase) * 9f, 0f, Mathf.Sin(Time.time * 1.1f + phase) * 6f);
    }
}
