using UnityEngine;

namespace Deepbound.Combat
{
    public interface ICrowdControl
    {
        void ServerStun(float seconds);

        void ServerTaunt(Health taunter, float seconds);

        void ServerKnockback(Vector3 velocity);
    }
}
