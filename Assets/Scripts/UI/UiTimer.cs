using System.Collections.Generic;
using System.Diagnostics;

namespace Deepbound.UI
{
    public static class UiTimer
    {
        public static readonly bool Enabled = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-uncapped") >= 0;
        private static readonly Stopwatch watch = Stopwatch.StartNew();
        private static double last;
        public static readonly Dictionary<string, double> Totals = new Dictionary<string, double>();

        private static bool Repaint => UnityEngine.Event.current != null && UnityEngine.Event.current.type == UnityEngine.EventType.Repaint;

        public static void Begin() { if (Enabled) last = watch.Elapsed.TotalMilliseconds; }

        public static void Mark(string what)
        {
            if (!Enabled) return;
            double now = watch.Elapsed.TotalMilliseconds;
            if (Repaint) { Totals.TryGetValue(what, out var t); Totals[what] = t + (now - last); }
            last = now;
        }

        public static void Reset() => Totals.Clear();
    }
}
