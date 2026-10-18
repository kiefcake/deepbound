using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>Turns its object steadily about local up (a candle halo, a cloud of flies).</summary>
    public class Spin : MonoBehaviour
    {
        public float DegreesPerSecond = 30f;
        private void Update() => transform.Rotate(0f, DegreesPerSecond * Time.deltaTime, 0f, Space.Self);
    }
}
