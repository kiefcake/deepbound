using UnityEngine;

namespace Deepbound.Art
{
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
