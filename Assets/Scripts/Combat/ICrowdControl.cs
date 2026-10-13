using UnityEngine;

namespace Deepbound.Combat
{
    /// <summary>
    /// Crowd control an enemy can receive. Implemented by enemy brains, called by player
    /// kits on the server. An interface rather than a concrete brain reference so kits do
    /// not depend on enemy code — the eight classes will all need it.
    /// </summary>
    public interface ICrowdControl
    {
        /// <summary>Interrupts any attack in progress and stops the enemy acting.</summary>
        void ServerStun(float seconds);

        /// <summary>Forces the enemy to target <paramref name="taunter"/>, ignoring everyone else.</summary>
        void ServerTaunt(Health taunter, float seconds);

        /// <summary>Pushes the enemy away. Ignored by anything too heavy to move (bosses).</summary>
        void ServerKnockback(Vector3 velocity);
    }
}
