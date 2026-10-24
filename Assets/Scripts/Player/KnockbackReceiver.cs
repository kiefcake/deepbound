using Unity.Netcode;
using UnityEngine;

namespace Deepbound.Player
{
    /// <summary>
    /// Lets the server shove a player. Movement is owner-authoritative, so the server
    /// cannot move a player directly — it asks the owner to apply the impulse itself.
    /// </summary>
    public class KnockbackReceiver : NetworkBehaviour
    {
        [Rpc(SendTo.Owner)]
        public void KnockbackRpc(Vector3 velocity)
        {
            if (TryGetComponent<Flasks>(out var f) && (f.Ironbark || f.InStasis)) return; // rooted like a tree, or stone
            GetComponent<FirstPersonMotor>().AddImpulse(velocity);
        }
    }
}
