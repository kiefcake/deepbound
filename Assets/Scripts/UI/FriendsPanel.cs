using Deepbound.Networking;
using UnityEngine;

namespace Deepbound.UI
{
    /// <summary>
    /// Steam friends inside the game's own menus (2026-10-26), so inviting and joining work even where the Steam
    /// overlay does not: JOIN on a friend hosting a Deepbound party (title screen), INVITE on an online friend
    /// (the host's pause menu), and any invites waiting for an answer. GUILayout pieces for an open area.
    /// </summary>
    public static class FriendsPanel
    {
        private static Vector2 scroll;
        private static readonly System.Collections.Generic.Dictionary<ulong, float> invitedAt = new System.Collections.Generic.Dictionary<ulong, float>();

        private static GUIStyle row, dim;
        private static void Styles()
        {
            if (row != null) return;
            row = new GUIStyle(UiSkin.Body) { fontSize = 15, alignment = TextAnchor.MiddleLeft, wordWrap = false, clipping = TextClipping.Clip };
            dim = new GUIStyle(UiSkin.Small) { alignment = TextAnchor.MiddleLeft, wordWrap = false };
        }

        /// <summary>Invites from friends with a JOIN button each. Returns true if any were drawn.</summary>
        public static bool DrawInvites()
        {
            if (!SteamPlatform.Ready || SteamPlatform.Instance.Invites.Count == 0) return false;
            Styles();
            foreach (var inv in SteamPlatform.Instance.Invites.ToArray())
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(inv.Name + " invited you", new GUIStyle(row) { normal = { textColor = UiSkin.Gold } }, GUILayout.Height(30));
                if (GUILayout.Button("JOIN", UiSkin.Button, GUILayout.Width(90), GUILayout.Height(30))) SteamPlatform.Join(inv.Lobby);
                GUILayout.EndHorizontal();
            }
            return true;
        }

        /// <summary>Title screen: friends hosting a Deepbound party first (JOIN), then who else is about.</summary>
        public static void DrawJoinList(float height)
        {
            Styles();
            var list = SteamPlatform.Friends();
            int hosting = 0;
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(height));
            foreach (var f in list)
            {
                if (!f.Online) continue;
                GUILayout.BeginHorizontal();
                if (f.Lobby != 0 && f.InDeepbound)
                {
                    hosting++;
                    GUILayout.Label(f.Name, row, GUILayout.Height(30));
                    if (GUILayout.Button("JOIN", UiSkin.Button, GUILayout.Width(90), GUILayout.Height(30))) SteamPlatform.Join(f.Lobby);
                }
                else GUILayout.Label(f.Name + (f.InDeepbound ? "   - in Deepbound" : "   - online"), dim, GUILayout.Height(24));
                GUILayout.EndHorizontal();
            }
            if (list.Count == 0) GUILayout.Label("No Steam friends to show.", dim);
            GUILayout.EndScrollView();
            if (hosting == 0) GUILayout.Label("Nobody is hosting yet: ask a friend to press PLAY, then invite you from their pause menu.", new GUIStyle(UiSkin.Small) { wordWrap = true });
        }

        /// <summary>Host's pause menu: online friends with an INVITE button each.</summary>
        public static void DrawInviteList(float height)
        {
            Styles();
            var list = SteamPlatform.Friends();
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(height));
            int shown = 0;
            foreach (var f in list)
            {
                if (!f.Online) continue;
                shown++;
                GUILayout.BeginHorizontal();
                GUILayout.Label(f.Name + (f.InDeepbound ? "   - in Deepbound" : ""), row, GUILayout.Height(30));
                bool sent = invitedAt.TryGetValue(f.Id, out float at) && Time.unscaledTime - at < 30f;
                if (GUILayout.Button(sent ? "SENT" : "INVITE", UiSkin.Button, GUILayout.Width(100), GUILayout.Height(30)) && !sent && SteamPlatform.InviteFriend(f.Id))
                    invitedAt[f.Id] = Time.unscaledTime;
                GUILayout.EndHorizontal();
            }
            if (shown == 0) GUILayout.Label("None of your Steam friends are online.", dim);
            GUILayout.EndScrollView();
        }
    }
}
