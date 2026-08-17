using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Lightweight in-process sampler: subsystem Stopwatch buckets, plugin inventory,
    /// Harmony patch owners, frame/GC stats. Feeds PerfProbeMenu + on-disk reports.
    /// </summary>
    internal static class PerfProbeService
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly Dictionary<string, Bucket> Buckets =
            new Dictionary<string, Bucket>(64);
        private static readonly List<Spike> Spikes = new List<Spike>(32);
        private static readonly object Gate = new object();

        private static bool _sampling = true;
        private static float _windowStart;
        private static int _framesInWindow;
        private static double _frameDtSumMs;
        private static float _peakDtMs;
        private static float _lastReportAt;
        private static string _lastReportPath = "";
        private static int _hitchCount;
        private static double _hitchMsSum;
        private static float _lastDtMs;
        private const int DtRingSize = 256;
        private static readonly float[] DtRing = new float[DtRingSize];
        private static readonly float[] DtSortScratch = new float[DtRingSize];
        private static int _dtRingCount;
        private static int _dtRingWrite;
        private static float _p1Cached;
        private static int _p1Frame;
        private static long _gcCached;
        private static float _gcCachedAt;
        private static List<string> _harmCache;
        private static float _harmCacheAt = -100f;
        private static List<string> _plugCache;
        private static float _plugCacheAt = -100f;

        private sealed class Bucket
        {
            public string Name;
            public long TotalTicks;
            public int Calls;
            public long PeakTicks;
            public long WindowTicks;
            public int WindowCalls;
        }

        private sealed class Spike
        {
            public float Time;
            public float DtMs;
            public string Hint;
        }

        internal static bool Sampling
        {
            get { return _sampling; }
            set { _sampling = value; }
        }

        internal static string LastReportPath
        {
            get { return _lastReportPath ?? ""; }
        }

        internal static void TickFrame()
        {
            float dtMs = Time.unscaledDeltaTime * 1000f;
            _lastDtMs = dtMs;
            _framesInWindow++;
            _frameDtSumMs += dtMs;
            if (dtMs > _peakDtMs)
                _peakDtMs = dtMs;
            DtRing[_dtRingWrite] = dtMs;
            _dtRingWrite++;
            if (_dtRingWrite >= DtRingSize)
                _dtRingWrite = 0;
            if (_dtRingCount < DtRingSize)
                _dtRingCount++;
            if (dtMs >= 100f)
            {
                _hitchCount++;
                _hitchMsSum += dtMs;
            }
            if (dtMs >= 40f)
                NoteSpike(dtMs, dtMs >= 500f ? "hitch" : "frame");
            if (_windowStart <= 0f)
                _windowStart = Time.unscaledTime;
            if ((Time.frameCount & 7) == 0)
                _p1Cached = PercentileFps(0.99f);
            if (Time.unscaledTime - _gcCachedAt >= 0.5f)
            {
                _gcCachedAt = Time.unscaledTime;
                _gcCached = MonoUsedBytesUncached();
            }
        }

        internal static void NoteSpike(float dtMs, string hint)
        {
            lock (Gate)
            {
                Spike s = new Spike();
                s.Time = Time.unscaledTime;
                s.DtMs = dtMs;
                s.Hint = hint ?? "";
                Spikes.Add(s);
                if (Spikes.Count > 40)
                    Spikes.RemoveAt(0);
            }
        }

        internal static void Measure(string name, Action action)
        {
            if (action == null)
                return;
            if (!_sampling)
            {
                action();
                return;
            }
            long t0 = Stopwatch.GetTimestamp();
            try { action(); }
            finally
            {
                long elapsed = Stopwatch.GetTimestamp() - t0;
                Accrue(name, elapsed);
            }
        }

        internal static void Accrue(string name, long ticks)
        {
            if (string.IsNullOrEmpty(name) || ticks < 0)
                return;
            lock (Gate)
            {
                Bucket b;
                if (!Buckets.TryGetValue(name, out b) || b == null)
                {
                    b = new Bucket();
                    b.Name = name;
                    Buckets[name] = b;
                }
                b.TotalTicks += ticks;
                b.Calls++;
                b.WindowTicks += ticks;
                b.WindowCalls++;
                if (ticks > b.PeakTicks)
                    b.PeakTicks = ticks;
            }
        }

        internal static void ResetWindow()
        {
            lock (Gate)
            {
                foreach (KeyValuePair<string, Bucket> kv in Buckets)
                {
                    if (kv.Value == null)
                        continue;
                    kv.Value.WindowTicks = 0;
                    kv.Value.WindowCalls = 0;
                }
                ResetFrameCounters();
            }
        }

        internal static void ClearAll()
        {
            lock (Gate)
            {
                Buckets.Clear();
                Spikes.Clear();
                ResetFrameCounters();
            }
        }

        private static void ResetFrameCounters()
        {
            _windowStart = Time.unscaledTime;
            _framesInWindow = 0;
            _frameDtSumMs = 0;
            _peakDtMs = 0f;
            _hitchCount = 0;
            _hitchMsSum = 0;
            _lastDtMs = 0f;
            _dtRingCount = 0;
            _dtRingWrite = 0;
        }

        internal static float TicksToMs(long ticks)
        {
            if (ticks <= 0)
                return 0f;
            return (float)(ticks * 1000.0 / Stopwatch.Frequency);
        }

        internal static List<string> SnapshotBucketLines(bool windowOnly)
        {
            List<KeyValuePair<string, float>> rows = new List<KeyValuePair<string, float>>(64);
            lock (Gate)
            {
                foreach (KeyValuePair<string, Bucket> kv in Buckets)
                {
                    Bucket b = kv.Value;
                    if (b == null)
                        continue;
                    float ms = windowOnly
                        ? TicksToMs(b.WindowTicks)
                        : TicksToMs(b.TotalTicks);
                    if (ms < 0.001f && (windowOnly ? b.WindowCalls : b.Calls) == 0)
                        continue;
                    rows.Add(new KeyValuePair<string, float>(b.Name, ms));
                }
            }
            rows.Sort(CompareMsDesc);
            List<string> lines = new List<string>(rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                Bucket b;
                lock (Gate) { Buckets.TryGetValue(rows[i].Key, out b); }
                int calls = 0;
                float peak = 0f;
                float avg = 0f;
                if (b != null)
                {
                    calls = windowOnly ? b.WindowCalls : b.Calls;
                    peak = TicksToMs(b.PeakTicks);
                    if (calls > 0)
                        avg = rows[i].Value / calls;
                }
                lines.Add(string.Format(Inv,
                    "{0,-28}  sum={1,8:0.00}ms  n={2,5}  avg={3,6:0.000}ms  peak={4,6:0.00}ms",
                    rows[i].Key, rows[i].Value, calls, avg, peak));
            }
            return lines;
        }

        private static int CompareMsDesc(KeyValuePair<string, float> a, KeyValuePair<string, float> b)
        {
            return b.Value.CompareTo(a.Value);
        }

        internal static void GetFrameStats(out float avgFps, out float avgDtMs, out float peakDtMs,
            out int frames, out float windowSec)
        {
            float instant, p1, stall;
            int hitches;
            GetFrameStatsEx(out avgFps, out avgDtMs, out peakDtMs, out frames, out windowSec,
                out instant, out p1, out hitches, out stall);
        }

        /// <summary>
        /// avgFps is 1000/mean(dt) — same definition as avgDt. instantFps is the last frame only
        /// (do not treat it as average). p1Fps is 1% low from the recent ring. stallSec is sum of dt≥100ms.
        /// </summary>
        internal static void GetFrameStatsEx(out float avgFps, out float avgDtMs, out float peakDtMs,
            out int frames, out float windowSec, out float instantFps, out float p1Fps,
            out int hitchCount, out float stallSec)
        {
            float now = Time.unscaledTime;
            windowSec = _windowStart > 0f ? Mathf.Max(0.001f, now - _windowStart) : 0.001f;
            frames = _framesInWindow;
            avgDtMs = frames > 0 ? (float)(_frameDtSumMs / frames) : 0f;
            avgFps = avgDtMs > 0.05f ? 1000f / avgDtMs : 0f;
            peakDtMs = _peakDtMs;
            hitchCount = _hitchCount;
            stallSec = (float)(_hitchMsSum / 1000.0);
            instantFps = _lastDtMs > 0.05f ? 1000f / _lastDtMs : 0f;
            p1Fps = _p1Cached;
        }

        /// <summary>1% low ≈ percentile 0.99 of frame times (slowest 1%).</summary>
        private static float PercentileFps(float slowFrac)
        {
            int n = _dtRingCount;
            if (n <= 0)
                return 0f;
            int start = (_dtRingCount < DtRingSize) ? 0 : _dtRingWrite;
            for (int i = 0; i < n; i++)
                DtSortScratch[i] = DtRing[(start + i) % DtRingSize];
            Array.Sort(DtSortScratch, 0, n);
            int idx = Mathf.Clamp(Mathf.FloorToInt(slowFrac * (n - 1)), 0, n - 1);
            float dt = DtSortScratch[idx];
            if (dt < 0.1f)
                return 0f;
            return 1000f / dt;
        }

        internal static long MonoUsedBytes()
        {
            if (_gcCached > 0 && Time.unscaledTime - _gcCachedAt < 0.6f)
                return _gcCached;
            return MonoUsedBytesUncached();
        }

        private static long MonoUsedBytesUncached()
        {
            try { return GC.GetTotalMemory(false); }
            catch { return 0; }
        }

        internal static List<string> ListPlugins()
        {
            if (_plugCache != null && Time.unscaledTime - _plugCacheAt < 2f)
                return _plugCache;
            List<string> lines = ListPluginsUncached();
            _plugCache = lines;
            _plugCacheAt = Time.unscaledTime;
            return lines;
        }

        private static List<string> ListPluginsUncached()
        {
            List<string> lines = new List<string>(16);
            try
            {
                if (Chainloader.PluginInfos == null)
                    return lines;
                List<BepInEx.PluginInfo> infos = new List<BepInEx.PluginInfo>();
                foreach (KeyValuePair<string, BepInEx.PluginInfo> kv in Chainloader.PluginInfos)
                {
                    if (kv.Value != null)
                        infos.Add(kv.Value);
                }
                infos.Sort(ComparePluginName);
                lines.Add(string.Format(Inv,
                    "{0} BepInEx plugins. Oritasy already includes WeXon + TGM-85 — extra WeXon.dll / Kh85MT.dll stack hangar + missile patches.",
                    infos.Count));
                for (int i = 0; i < infos.Count; i++)
                {
                    BepInEx.PluginInfo p = infos[i];
                    string guid = p.Metadata != null ? p.Metadata.GUID : "?";
                    string name = p.Metadata != null ? p.Metadata.Name : "?";
                    string ver = p.Metadata != null ? p.Metadata.Version.ToString() : "?";
                    string loc = "";
                    try
                    {
                        if (p.Instance != null)
                            loc = p.Instance.GetType().Assembly.GetName().Name;
                    }
                    catch { }
                    string hint = PluginOverlapHint(guid, name);
                    if (string.IsNullOrEmpty(hint))
                        lines.Add(string.Format(Inv, "{0}  v{1}  [{2}]  asm={3}", name, ver, guid, loc));
                    else
                        lines.Add(string.Format(Inv, "{0}  v{1}  [{2}]  asm={3}  {4}",
                            name, ver, guid, loc, hint));
                }
            }
            catch (Exception ex)
            {
                lines.Add("plugin list error: " + ex.Message);
            }
            return lines;
        }

        private static string PluginOverlapHint(string guid, string name)
        {
            string g = guid != null ? guid.ToLowerInvariant() : "";
            string n = name != null ? name.ToLowerInvariant() : "";
            if (g.IndexOf("unrestrictedweapon", StringComparison.Ordinal) >= 0
                || n.IndexOf("unrestricted weapon", StringComparison.Ordinal) >= 0)
                return "<< overlaps Oritasy hangar unrestricted — disable this DLL";
            if ((g.IndexOf("wexon", StringComparison.Ordinal) >= 0
                    || n.IndexOf("wexon", StringComparison.Ordinal) >= 0)
                && g.IndexOf("oritasy", StringComparison.Ordinal) < 0)
                return "<< already hosted inside Oritasy.dll — disable standalone WeXon";
            if (g.IndexOf("kh85", StringComparison.Ordinal) >= 0
                || n.IndexOf("kh85", StringComparison.Ordinal) >= 0
                || n.IndexOf("tgm-85", StringComparison.Ordinal) >= 0)
                return "<< already hosted inside Oritasy.dll — disable standalone Kh85MT";
            return "";
        }

        private static int ComparePluginName(BepInEx.PluginInfo a, BepInEx.PluginInfo b)
        {
            string na = a != null && a.Metadata != null ? a.Metadata.Name : "";
            string nb = b != null && b.Metadata != null ? b.Metadata.Name : "";
            return string.Compare(na, nb, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Count Harmony patches grouped by owner GUID / assembly. Cached 2s — live scan hitches IMGUI.</summary>
        internal static List<string> ListHarmonyOwners()
        {
            if (_harmCache != null && Time.unscaledTime - _harmCacheAt < 2f)
                return _harmCache;
            List<string> lines = ListHarmonyOwnersUncached();
            _harmCache = lines;
            _harmCacheAt = Time.unscaledTime;
            return lines;
        }

        private static List<string> ListHarmonyOwnersUncached()
        {
            Dictionary<string, int> counts = new Dictionary<string, int>(32);
            int methods = 0;
            try
            {
                MethodBase[] patched = new MethodBase[0];
                try
                {
                    IEnumerable<MethodBase> en = Harmony.GetAllPatchedMethods();
                    List<MethodBase> tmp = new List<MethodBase>(256);
                    if (en != null)
                    {
                        foreach (MethodBase mb in en)
                            tmp.Add(mb);
                    }
                    patched = tmp.ToArray();
                }
                catch { }

                for (int i = 0; i < patched.Length; i++)
                {
                    MethodBase mb = patched[i];
                    if (mb == null)
                        continue;
                    methods++;
                    Patches info = Harmony.GetPatchInfo(mb);
                    if (info == null)
                        continue;
                    AddOwners(counts, info.Prefixes);
                    AddOwners(counts, info.Postfixes);
                    AddOwners(counts, info.Transpilers);
                    AddOwners(counts, info.Finalizers);
                }
            }
            catch (Exception ex)
            {
                List<string> err = new List<string>(1);
                err.Add("harmony scan error: " + ex.Message);
                return err;
            }

            List<KeyValuePair<string, int>> rows = new List<KeyValuePair<string, int>>(counts.Count);
            foreach (KeyValuePair<string, int> kv in counts)
                rows.Add(kv);
            rows.Sort(CompareCountDesc);
            List<string> lines = new List<string>(rows.Count + 2);
            lines.Add(string.Format(Inv, "patched methods: {0}", methods));
            lines.Add("Oritasy+WeXon share one Harmony id in the combined DLL (legacy wexon/oritasy ids fold here).");
            for (int i = 0; i < rows.Count; i++)
                lines.Add(string.Format(Inv, "{0,-40}  patches≈{1}", rows[i].Key, rows[i].Value));
            return lines;
        }

        private static void AddOwners(Dictionary<string, int> counts, System.Collections.IEnumerable list)
        {
            if (list == null || counts == null)
                return;
            foreach (object o in list)
            {
                Patch p = o as Patch;
                if (p == null)
                    continue;
                string owner = FoldHarmonyOwner(p.owner);
                if (string.IsNullOrEmpty(owner))
                    owner = "?";
                int n;
                counts.TryGetValue(owner, out n);
                counts[owner] = n + 1;
            }
        }

        private static string FoldHarmonyOwner(string owner)
        {
            if (string.IsNullOrEmpty(owner))
                return "?";
            string o = owner.ToLowerInvariant();
            if (o.IndexOf("oritasy", StringComparison.Ordinal) >= 0
                || o.IndexOf("wexon", StringComparison.Ordinal) >= 0
                || o.IndexOf("veyrnacm", StringComparison.Ordinal) >= 0)
                return "Oritasy pack (" + PluginInfo.GUID + ")";
            return owner;
        }

        private static int CompareCountDesc(KeyValuePair<string, int> a, KeyValuePair<string, int> b)
        {
            return b.Value.CompareTo(a.Value);
        }

        internal static List<string> ListRecentSpikes(int max)
        {
            if (max < 1)
                max = 8;
            List<string> lines = new List<string>(max);
            lock (Gate)
            {
                int start = Spikes.Count - max;
                if (start < 0)
                    start = 0;
                for (int i = start; i < Spikes.Count; i++)
                {
                    Spike s = Spikes[i];
                    lines.Add(string.Format(Inv, "t={0:0.0}s  {1:0.0}ms  {2}",
                        s.Time, s.DtMs, s.Hint));
                }
            }
            return lines;
        }

        internal static string WriteReport(bool zh)
        {
            float avgFps, avgDt, peakDt, winSec, instant, p1, stallSec;
            int frames, hitches;
            GetFrameStatsEx(out avgFps, out avgDt, out peakDt, out frames, out winSec,
                out instant, out p1, out hitches, out stallSec);

            StringBuilder sb = new StringBuilder(4096);
            sb.AppendLine("=== Oritasy Performance Report ===");
            sb.AppendLine("time_utc: " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", Inv) + "Z");
            sb.AppendLine("release: " + PluginInfo.DisplayRelease + "  asm=" + PluginInfo.Version);
            sb.AppendLine("perf_tier: " + PerfMode.TierName
                + "  lowEnd=" + (PerfMode.LowEndMode != null && PerfMode.LowEndMode.Value));
            sb.AppendLine(string.Format(Inv,
                "window: {0:0.0}s  frames={1}  avgFps={2:0.1} (1000/avgDt)  instantFps={3:0.0}  p1Low={4:0.0}  avgDt={5:0.00}ms  peakDt={6:0.00}ms",
                winSec, frames, avgFps, instant, p1, avgDt, peakDt));
            sb.AppendLine(string.Format(Inv,
                "hitches(>=100ms): {0}  stallSec={1:0.00}",
                hitches, stallSec));
            sb.AppendLine(string.Format(Inv, "mono_gc_bytes: {0}", MonoUsedBytes()));
            sb.AppendLine(string.Format(Inv, "unity: {0}  screen={1}x{2}  vSync={3}  targetFps={4}  cpu={5}",
                Application.unityVersion, Screen.width, Screen.height,
                QualitySettings.vSyncCount, Application.targetFrameRate,
                Environment.ProcessorCount));
            sb.AppendLine();

            sb.AppendLine("--- Probe / worker occupancy ---");
            sb.AppendLine("sampling=" + (_sampling ? "ON" : "OFF")
                + "  menuOpen=" + PerfProbeMenu.IsOpen
                + "  " + OritasyWorker.SnapshotLine());
            sb.AppendLine(PerfFrameGate.SnapshotLine());
            sb.AppendLine("Harmony/plugin lists cached 2s on main thread (Harmony is not worker-safe).");
            sb.AppendLine("Report file write / WAV decode queued to 4 OritasyWorkers (no Unity APIs).");
            sb.AppendLine("Missile hunt / Harmony patches stay on the main thread; hunt pauses 2 frames after hitch.");
            sb.AppendLine();

            sb.AppendLine("--- Loaded BepInEx plugins ---");
            List<string> plugs = ListPlugins();
            for (int i = 0; i < plugs.Count; i++)
                sb.AppendLine(plugs[i]);
            sb.AppendLine();

            sb.AppendLine("--- Harmony patch owners ---");
            List<string> harm = ListHarmonyOwners();
            for (int i = 0; i < harm.Count; i++)
                sb.AppendLine(harm[i]);
            sb.AppendLine();

            sb.AppendLine("--- Subsystem timing (window sum) ---");
            List<string> bucks = SnapshotBucketLines(true);
            if (bucks.Count == 0)
                sb.AppendLine("(no samples — leave menu open / fly for a few seconds)");
            for (int i = 0; i < bucks.Count; i++)
                sb.AppendLine(bucks[i]);
            sb.AppendLine();

            sb.AppendLine("--- Recent frame spikes (≥40ms) ---");
            List<string> spikes = ListRecentSpikes(20);
            if (spikes.Count == 0)
                sb.AppendLine("(none)");
            for (int i = 0; i < spikes.Count; i++)
                sb.AppendLine(spikes[i]);
            sb.AppendLine();

            sb.AppendLine("--- Notes ---");
            sb.AppendLine("avgFps = 1000/mean(frame dt). instantFps is the LAST frame only — not an average.");
            sb.AppendLine("p1Low = 1% low from the last 256 frames (includes hitches).");
            sb.AppendLine("Buckets measure Oritasy/WeXon instrumented Update groups only.");
            sb.AppendLine("OritasyWorker: up to 4 below-normal threads. Unity/Harmony/hunt stay main-thread.");
            sb.AppendLine("PerfFrameGate: dt≥33.5ms → 2-frame recover (skip hunt + polish, not HUD draws). dt≥20ms skips polish only.");
            sb.AppendLine("Oritasy combined DLL hosts WeXon + TGM-85 under one Harmony id — not extra plugins.");
            sb.AppendLine("Aryx / Blueprinter / Unrestricted Weapons / WT Mouse Aim cannot be merged here.");
            sb.AppendLine("Disable standalone WeXon.dll, Kh85MT.dll, and UnrestrictedWeapons if those DLLs are still in plugins.");
            sb.AppendLine("Other mods appear via plugin list + Harmony owner counts (not wall-clock).");
            sb.AppendLine("Toggle key: backtick (`). Sampling defaults ON.");

            string dir = ReportDir();
            try
            {
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
            catch { }

            string name = "perf_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", Inv) + ".txt";
            string path = Path.Combine(dir, name);
            string payload = sb.ToString();
            _lastReportPath = path;
            _lastReportAt = Time.unscaledTime;
            bool[] wrote = new bool[1];
            bool queued = OritasyWorker.TryEnqueue(
                delegate
                {
                    File.WriteAllText(path, payload, Encoding.UTF8);
                    wrote[0] = true;
                },
                delegate
                {
                    if (!wrote[0])
                    {
                        _lastReportPath = "";
                        return;
                    }
                    if (Plugin.Log != null)
                        Plugin.Log.LogInfo("PerfProbe report: " + path);
                });
            if (!queued)
            {
                try
                {
                    File.WriteAllText(path, payload, Encoding.UTF8);
                    if (Plugin.Log != null)
                        Plugin.Log.LogInfo("PerfProbe report: " + path);
                }
                catch (Exception ex)
                {
                    _lastReportPath = "";
                    if (Plugin.Log != null)
                        Plugin.Log.LogWarning("PerfProbe write failed: " + ex.Message);
                    return "";
                }
            }
            return path;
        }

        internal static string ReportDir()
        {
            try { return Path.Combine(Paths.PluginPath, "OritasyPerf"); }
            catch { return Path.Combine(Application.dataPath, "OritasyPerf"); }
        }
    }
}
