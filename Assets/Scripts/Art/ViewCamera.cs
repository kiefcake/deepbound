using UnityEngine;

namespace Deepbound.Art
{
    /// <summary>
    /// The camera the player is looking through: the local player's own camera in play (it
    /// is not tagged MainCamera), else whatever Unity calls main (menus, the class screen).
    /// </summary>
    public static class ViewCamera
    {
        public static Camera Current
        {
            get
            {
                var local = Player.NetworkPlayer.Local;
                if (local != null && local.Camera != null && local.Camera.enabled) return local.Camera;
                return Camera.main;
            }
        }
    }
}
