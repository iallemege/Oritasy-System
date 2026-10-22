using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Optional heavy Harmony wall-clock sampler. Installs temporary Prefix+Finalizer
    /// timers on the hottest patched methods for a short window, then UnpatchAll.
    /// Default OFF — enable from PerfProbe menu (Harmony tab).
    /// </summary>
    internal static class HarmonyTimingSample
    {
        private const string HarmonyId = "oritasy.perf.timing.sample";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, TimingBucket> Buckets =
            new Dictionary<string, TimingBucket>(64);

        private static Harmony _harmony;
        private static MethodBase[] _installed = new MethodBase[0];
        private static bool _active;
        private static float _until;
        private static int _topN = 12;
        private static float _durationSec = 5f;
        private static string _status = "";
        private static int _installAttempts;
        private static int _installOk;

        private sealed class TimingBucket
        {
            public string Key;
            public long TotalTicks;
            public int Calls;
            public long PeakTicks;
        }

        internal static bool Active
        {
            get { return _active; }
        }

        internal static string Status
        {
            get { return _status ?? ""; }
        }

        internal static float RemainingSec
        {
            get
            {
                if (!_active)
                    return 0f;
                return Mathf.Max(0f, _until - Time.unscaledTime);
            }
        }

        internal static int InstalledCount
        {
            get { return _installed != null ? _installed.Length : 0; }
        }

        internal static void Tick()
        {
            if (!_active)
                return;
            if (Time.unscaledTime >= _until)
                StopSample("auto-stop (window elapsed)");
        }

        /// <summary>Start a short heavy sample. Unpatches any prior sample first.</summary>
        internal static void StartSample(int topN, float durationSec)
        {
            if (topN < 1)
                topN = 1;
            if (topN > 32)
                topN = 32;
            if (durationSec < 1f)
                durationSec = 1f;
            if (durationSec > 30f)
                durationSec = 30f;

            StopSample("restart");
            lock (Gate)
            {
                Buckets.Clear();
            }

            _topN = topN;
            _durationSec = durationSec;
            _installAttempts = 0;
            _installOk = 0;

            List<MethodBase> targets = PickHotMethods(topN);
            if (targets.Count == 0)
            {
                _status = "no patched methods to sample";
                return;
            }

            try
            {
                _harmony = new Harmony(HarmonyId);
            }
            catch (Exception ex)
            {
                _status = "harmony create failed: " + ex.Message;
                return;
            }

            MethodInfo prefix = typeof(HarmonyTimingSample).GetMethod(
                "TimingPrefix", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            MethodInfo finalizer = typeof(HarmonyTimingSample).GetMethod(
                "TimingFinalizer", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (prefix == null || finalizer == null)
            {
                _status = "timing hooks missing";
                return;
            }

            List<MethodBase> ok = new List<MethodBase>(targets.Count);
            for (int i = 0; i < targets.Count; i++)
            {
                MethodBase mb = targets[i];
                if (mb == null)
                    continue;
                _installAttempts++;
                try
                {
                    _harmony.Patch(
                        mb,
                        prefix: new HarmonyMethod(prefix),
                        finalizer: new HarmonyMethod(finalizer));
                    ok.Add(mb);
                    _installOk++;
                }
                catch (Exception ex)
                {
                    // Skip methods Harmony refuses (abstract, generic open, etc.)
                    if (Plugin.Log != null)
                        Plugin.Log.LogWarning("HarmonyTiming skip " + MethodKey(mb) + ": " + ex.Message);
                }
            }

            _installed = ok.ToArray();
            if (_installed.Length == 0)
            {
                SafeUnpatch();
                _status = "install failed (0/" + _installAttempts + ")";
                return;
            }

            _active = true;
            _until = Time.unscaledTime + _durationSec;
            _status = string.Format(Inv, "sampling {0} methods for {1:0.0}s",
                _installed.Length, _durationSec);
            if (Plugin.Log != null)
                Plugin.Log.LogInfo("HarmonyTimingSample: " + _status);
        }

        internal static void StopSample(string reason)
        {
            if (!_active && (_harmony == null) && (_installed == null || _installed.Length == 0))
            {
                if (!string.IsNullOrEmpty(reason) && reason != "restart")
                    _status = reason;
                return;
            }
            SafeUnpatch();
            _active = false;
            _until = 0f;
            _installed = new MethodBase[0];
            _status = string.IsNullOrEmpty(reason) ? "stopped" : reason;
            if (Plugin.Log != null)
                Plugin.Log.LogInfo("HarmonyTimingSample stopped: " + _status);
        }

        private static void SafeUnpatch()
        {
            try
            {
                if (_harmony != null)
                    _harmony.UnpatchSelf();
            }
            catch { }
            _harmony = null;
        }

        private static List<MethodBase> PickHotMethods(int topN)
        {
            List<ScoredMethod> scored = new List<ScoredMethod>(256);
            try
            {
                IEnumerable<MethodBase> en = Harmony.GetAllPatchedMethods();
                if (en == null)
                    return new List<MethodBase>();
                foreach (MethodBase mb in en)
                {
                    if (mb == null)
                        continue;
                    if (IsOurHook(mb))
                        continue;
                    int score = ScoreMethod(mb);
                    if (score <= 0)
                        continue;
                    ScoredMethod sm = new ScoredMethod();
                    sm.Method = mb;
                    sm.Score = score;
                    scored.Add(sm);
                }
            }
            catch (Exception ex)
            {
                _status = "scan failed: " + ex.Message;
                return new List<MethodBase>();
            }

            scored.Sort(CompareScoreDesc);
            List<MethodBase> outList = new List<MethodBase>(topN);
            for (int i = 0; i < scored.Count && outList.Count < topN; i++)
            {
                MethodBase mb = scored[i].Method;
                if (mb == null)
                    continue;
                // Prefer concrete instance/static methods Harmony can patch.
                try
                {
                    if (mb.IsAbstract)
                        continue;
                    MethodInfo mi = mb as MethodInfo;
                    if (mi != null && mi.ContainsGenericParameters)
                        continue;
                }
                catch { continue; }
                outList.Add(mb);
            }
            return outList;
        }

        private sealed class ScoredMethod
        {
            public MethodBase Method;
            public int Score;
        }

        private static int CompareScoreDesc(ScoredMethod a, ScoredMethod b)
        {
            return b.Score.CompareTo(a.Score);
        }

        private static bool IsOurHook(MethodBase mb)
        {
            if (mb == null || mb.DeclaringType == null)
                return false;
            return mb.DeclaringType == typeof(HarmonyTimingSample);
        }

        private static int ScoreMethod(MethodBase mb)
        {
            int patchCount = 0;
            try
            {
                Patches info = Harmony.GetPatchInfo(mb);
                if (info != null)
                {
                    patchCount += CountPatches(info.Prefixes);
                    patchCount += CountPatches(info.Postfixes);
                    patchCount += CountPatches(info.Transpilers);
                    patchCount += CountPatches(info.Finalizers);
                }
            }
            catch { }

            if (patchCount <= 0)
                return 0;

            int hot = 0;
            string name = mb.Name ?? "";
            if (string.Equals(name, "Update", StringComparison.Ordinal)
                || string.Equals(name, "LateUpdate", StringComparison.Ordinal)
                || string.Equals(name, "FixedUpdate", StringComparison.Ordinal)
                || string.Equals(name, "OnGUI", StringComparison.Ordinal)
                || string.Equals(name, "OnRenderObject", StringComparison.Ordinal)
                || name.IndexOf("Update", StringComparison.Ordinal) >= 0)
                hot = 1000;

            // Slight preference for methods with more owners (contention / stack cost).
            return hot + patchCount * 10 + Math.Min(name.Length, 20);
        }

        private static int CountPatches(System.Collections.IEnumerable list)
        {
            if (list == null)
                return 0;
            int n = 0;
            foreach (object o in list)
            {
                if (o != null)
                    n++;
            }
            return n;
        }

        // Harmony injects __originalMethod and __state across Prefix/Finalizer.
        private static void TimingPrefix(MethodBase __originalMethod, ref long __state)
        {
            __state = Stopwatch.GetTimestamp();
        }

        private static Exception TimingFinalizer(MethodBase __originalMethod, long __state, Exception __exception)
        {
            try
            {
                long elapsed = Stopwatch.GetTimestamp() - __state;
                if (elapsed > 0)
                    Accrue(__originalMethod, elapsed);
            }
            catch { }
            return __exception;
        }

        private static void Accrue(MethodBase mb, long ticks)
        {
            string key = MethodKey(mb);
            lock (Gate)
            {
                TimingBucket b;
                if (!Buckets.TryGetValue(key, out b) || b == null)
                {
                    b = new TimingBucket();
                    b.Key = key;
                    Buckets[key] = b;
                }
                b.TotalTicks += ticks;
                b.Calls++;
                if (ticks > b.PeakTicks)
                    b.PeakTicks = ticks;
            }
        }

        private static string MethodKey(MethodBase mb)
        {
            if (mb == null)
                return "?";
            try
            {
                Type t = mb.DeclaringType;
                string typeName = t != null ? t.FullName : "?";
                return typeName + "::" + mb.Name;
            }
            catch
            {
                return mb.Name ?? "?";
            }
        }

        internal static List<string> SnapshotLines()
        {
            List<KeyValuePair<string, float>> rows = new List<KeyValuePair<string, float>>(64);
            lock (Gate)
            {
                foreach (KeyValuePair<string, TimingBucket> kv in Buckets)
                {
                    TimingBucket b = kv.Value;
                    if (b == null)
                        continue;
                    float ms = PerfProbeService.TicksToMs(b.TotalTicks);
                    if (ms < 0.0001f && b.Calls == 0)
                        continue;
                    rows.Add(new KeyValuePair<string, float>(b.Key, ms));
                }
            }
            rows.Sort(CompareMsDesc);
            List<string> lines = new List<string>(rows.Count + 4);
            lines.Add(string.Format(Inv,
                "heavy_sample active={0}  installed={1}  remain={2:0.0}s  status={3}",
                _active ? "Y" : "n", InstalledCount, RemainingSec, _status));
            lines.Add(string.Format(Inv,
                "last_install ok={0}/{1}  topN={2}  duration={3:0.0}s",
                _installOk, _installAttempts, _topN, _durationSec));
            lines.Add("NOTE: wall-clock includes original + ALL patches on the method (not owner-attributed).");
            if (rows.Count == 0)
            {
                lines.Add("(no timing samples yet — start heavy sample and play a few seconds)");
                return lines;
            }
            for (int i = 0; i < rows.Count; i++)
            {
                TimingBucket b;
                lock (Gate) { Buckets.TryGetValue(rows[i].Key, out b); }
                int calls = b != null ? b.Calls : 0;
                float peak = b != null ? PerfProbeService.TicksToMs(b.PeakTicks) : 0f;
                float avg = calls > 0 ? rows[i].Value / calls : 0f;
                lines.Add(string.Format(Inv,
                    "{0,-56}  sum={1,8:0.00}ms  n={2,6}  avg={3,7:0.000}ms  peak={4,6:0.00}ms",
                    Trunc(rows[i].Key, 56), rows[i].Value, calls, avg, peak));
            }
            return lines;
        }

        private static int CompareMsDesc(KeyValuePair<string, float> a, KeyValuePair<string, float> b)
        {
            return b.Value.CompareTo(a.Value);
        }

        private static string Trunc(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max)
                return s ?? "";
            return s.Substring(0, max - 2) + "..";
        }

        internal static void AppendReport(StringBuilder sb)
        {
            if (sb == null)
                return;
            List<string> lines = SnapshotLines();
            for (int i = 0; i < lines.Count; i++)
                sb.AppendLine(lines[i]);
        }

        internal static void ClearBuckets()
        {
            lock (Gate)
            {
                Buckets.Clear();
            }
        }
    }
}
