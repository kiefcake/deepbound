using UnityEngine;

namespace Deepbound.Art
{
    public class Spin : MonoBehaviour
    {
        public float DegreesPerSecond = 30f;
        private void Update() => transform.Rotate(0f, DegreesPerSecond * Time.deltaTime, 0f, Space.Self);
    }
}
