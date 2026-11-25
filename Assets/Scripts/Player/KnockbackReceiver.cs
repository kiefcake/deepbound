using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Player
{
    public class KnockbackReceiver : NetworkBehaviour
    {
        [Rpc(SendTo.Owner)]
        public void KnockbackRpc(Vector3 velocity)
        {
            if (TryGetComponent<Flasks>(out var f) && (f.Ironbark || f.InStasis)) return;
            GetComponent<FirstPersonMotor>().AddImpulse(velocity);
        }
    }
}
