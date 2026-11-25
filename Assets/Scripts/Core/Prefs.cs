using System;
using UnityEngine;

namespace Deepbound.Core
{
    public static class Prefs
    {
        private static bool warned;

        public static int GetInt(string key, int fallback = 0)
        {
            try { return PlayerPrefs.GetInt(key, fallback); }
            catch (Exception e) { Warn(e); return fallback; }
        }

        public static float GetFloat(string key, float fallback = 0f)
        {
            try { return PlayerPrefs.GetFloat(key, fallback); }
            catch (Exception e) { Warn(e); return fallback; }
        }

        public static string GetString(string key, string fallback = "")
        {
            try { return PlayerPrefs.GetString(key, fallback); }
            catch (Exception e) { Warn(e); return fallback; }
        }

        public static void SetInt(string key, int value)
        {
            try { PlayerPrefs.SetInt(key, value); }
            catch (Exception e) { Warn(e); }
        }

        public static void SetFloat(string key, float value)
        {
            try { PlayerPrefs.SetFloat(key, value); }
            catch (Exception e) { Warn(e); }
        }

        public static void SetString(string key, string value)
        {
            try { PlayerPrefs.SetString(key, value); }
            catch (Exception e) { Warn(e); }
        }

        public static void DeleteKey(string key)
        {
            try { PlayerPrefs.DeleteKey(key); }
            catch (Exception e) { Warn(e); }
        }

        public static void Save()
        {
            try { PlayerPrefs.Save(); }
            catch (Exception e) { Warn(e); }
        }

        private static void Warn(Exception e)
        {
            if (warned) return;
            warned = true;
            Debug.LogWarning("[Prefs] settings storage failed: " + e.Message);
        }
    }
}
