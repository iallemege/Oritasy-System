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
    /// In-process sampler: subsystem Stopwatch buckets, plugin inventory,
    /// Harmony owners + optional wall-clock sample, GC/alloc deltas, scene census.
    /// Feeds PerfProbeMenu + on-disk perf_report_v2 files.
    /// </summary>
    internal static class PerfProbeService
    {
        internal const string ReportFormatVersion = "perf_report_v2";

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly Dictionary<string, Bucket> Buckets =
            new Dictionary<string, Bucket>(64);
        private static readonly List<Spike> Spikes = new List<Spike>(32);
        private static readonly object Gate = new object();

        private static bool _sampling = false;
        private static float _windowStart;
        private static int _framesInWindow;
        private static double _frameDtSumMs;
        private static float _peakDtMs;
        private static float _lastReportAt;
        private static string _lastReportPath = "";
        private static string _prevReportPath = "";
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

        // --- GC / alloc (per-frame deltas, window aggregates) ---
        private static bool _gcInited;
        private static int _gc0;
        private static int _gc1;
        private static int _gc2;
        private static long _gcMem;
        private static int _winGc0;
        private static int _winGc1;
        private static int _winGc2;
        private static long _winAllocBytes; // sum of positive per-frame mem deltas
        private static long _winFreedBytes; // sum of |negative| deltas (after GC)
        private static long _peakMem;
        private static float _peakAllocPerFrameMb;
        private static int _gen2WithHitch;
        private static float _lastAllocRateMbPerSec;
        private static float _allocRateCachedAt;
        private static long _allocRateBytesAnchor;
        private static float _allocRateTimeAnchor;
        private const int GcRingSize = 128;
        private static readonly GcSample[] GcRing = new GcSample[GcRingSize];
        private static int _gcRingCount;
        private static int _gcRingWrite;
        private static List<string> _gcCacheLines;
        private static float _gcCacheAt = -100f;

        // --- Scene census (cached; FindObjectsOfType is hitchy) ---
        private static List<string> _sceneCache;
        private static float _sceneCacheAt = -100f;
        private static int _sceneGoTotal;
        private static int _sceneGoActive;
        private static int _sceneCompTotal;
        private static int _sceneCompBehaviours;
        private static string _sceneName = "";

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

        private struct GcSample
        {
            public float Time;
            public float DtMs;
            public int D0;
            public int D1;
            public int D2;
            public long DMem;
            public long Mem;
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

        internal static string PrevReportPath
        {
            get { return _prevReportPath ?? ""; }
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

            TickGc(dtMs);

            if (dtMs >= 100f)
            {
                _hitchCount++;
                _hitchMsSum += dtMs;
            }
            if (dtMs >= 40f)
            {
                string hint = dtMs >= 500f ? "hitch" : "frame";
                // TickGc already recorded this frame into the ring — attribute GC pauses.
                if (_gcInited && _gcRingCount > 0)
                {
                    GcSample last = GcRing[(_gcRingWrite - 1 + GcRingSize) % GcRingSize];
                    if (last.D2 > 0)
                        hint = "frame+gen2";
                    else if (last.D1 > 0)
                        hint = "frame+gen1";
                    else if (last.D0 > 0 && last.DMem < -1024 * 256)
                        hint = "frame+gen0";
                }
                NoteSpike(dtMs, hint);
            }
            if (_windowStart <= 0f)
                _windowStart = Time.unscaledTime;
            if ((Time.frameCount & 7) == 0)
                _p1Cached = PercentileFps(0.99f);

            HarmonyTimingSample.Tick();
        }

        private static void TickGc(float dtMs)
        {
            int g0 = 0, g1 = 0, g2 = 0;
            long mem = 0;
            try
            {
                g0 = GC.CollectionCount(0);
                g1 = GC.CollectionCount(1);
                g2 = GC.CollectionCount(2);
                mem = GC.GetTotalMemory(false);
            }
            catch { return; }

            if (!_gcInited)
            {
                _gcInited = true;
                _gc0 = g0;
                _gc1 = g1;
                _gc2 = g2;
                _gcMem = mem;
                _peakMem = mem;
                _allocRateBytesAnchor = mem;
                _allocRateTimeAnchor = Time.unscaledTime;
                return;
            }

            int d0 = g0 - _gc0;
            int d1 = g1 - _gc1;
            int d2 = g2 - _gc2;
            long dMem = mem - _gcMem;
            if (d0 < 0) d0 = 0;
            if (d1 < 0) d1 = 0;
            if (d2 < 0) d2 = 0;

            _winGc0 += d0;
            _winGc1 += d1;
            _winGc2 += d2;
            if (dMem > 0)
            {
                _winAllocBytes += dMem;
                float mb = dMem / (1024f * 1024f);
                if (mb > _peakAllocPerFrameMb)
                    _peakAllocPerFrameMb = mb;
            }
            else if (dMem < 0)
                _winFreedBytes += -dMem;

            if (mem > _peakMem)
                _peakMem = mem;

            if (d2 > 0 && dtMs >= 40f)
                _gen2WithHitch++;

            GcSample sample = new GcSample();
            sample.Time = Time.unscaledTime;
            sample.DtMs = dtMs;
            sample.D0 = d0;
            sample.D1 = d1;
            sample.D2 = d2;
            sample.DMem = dMem;
            sample.Mem = mem;
            GcRing[_gcRingWrite] = sample;
            _gcRingWrite++;
            if (_gcRingWrite >= GcRingSize)
                _gcRingWrite = 0;
            if (_gcRingCount < GcRingSize)
                _gcRingCount++;

            _gc0 = g0;
            _gc1 = g1;
            _gc2 = g2;
            _gcMem = mem;

            float now = Time.unscaledTime;
            float elapsed = now - _allocRateTimeAnchor;
            if (elapsed >= 0.5f)
            {
                long delta = mem - _allocRateBytesAnchor;
                // Net rate can be negative after GC; report alloc pressure via window sum instead.
                _lastAllocRateMbPerSec = (float)(delta / (1024.0 * 1024.0) / elapsed);
                _allocRateBytesAnchor = mem;
                _allocRateTimeAnchor = now;
                _allocRateCachedAt = now;
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
                ResetGcWindow();
            }
        }

        internal static void ClearAll()
        {
            lock (Gate)
            {
                Buckets.Clear();
                Spikes.Clear();
                ResetFrameCounters();
                ResetGcWindow();
                _gcRingCount = 0;
                _gcRingWrite = 0;
            }
            HarmonyTimingSample.ClearBuckets();
            _sceneCache = null;
            _harmCache = null;
            _plugCache = null;
            _gcCacheLines = null;
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

        private static void ResetGcWindow()
        {
            _winGc0 = 0;
            _winGc1 = 0;
            _winGc2 = 0;
            _winAllocBytes = 0;
            _winFreedBytes = 0;
            _peakAllocPerFrameMb = 0f;
            _gen2WithHitch = 0;
            // Re-anchor so next frame deltas are clean.
            try
            {
                _gc0 = GC.CollectionCount(0);
                _gc1 = GC.CollectionCount(1);
                _gc2 = GC.CollectionCount(2);
                _gcMem = GC.GetTotalMemory(false);
                _peakMem = _gcMem;
                _allocRateBytesAnchor = _gcMem;
                _allocRateTimeAnchor = Time.unscaledTime;
                _gcInited = true;
            }
            catch { }
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
            try { return _gcInited ? _gcMem : GC.GetTotalMemory(false); }
            catch { return 0; }
        }

        internal static void GetGcStats(out int winG0, out int winG1, out int winG2,
            out long memBytes, out long peakMemBytes, out long winAllocBytes, out long winFreedBytes,
            out float allocRateMbPerSec, out float peakAllocPerFrameMb, out int gen2WithHitch,
            out float windowSec)
        {
            winG0 = _winGc0;
            winG1 = _winGc1;
            winG2 = _winGc2;
            memBytes = MonoUsedBytes();
            peakMemBytes = _peakMem;
            winAllocBytes = _winAllocBytes;
            winFreedBytes = _winFreedBytes;
            allocRateMbPerSec = _lastAllocRateMbPerSec;
            peakAllocPerFrameMb = _peakAllocPerFrameMb;
            gen2WithHitch = _gen2WithHitch;
            float now = Time.unscaledTime;
            windowSec = _windowStart > 0f ? Mathf.Max(0.001f, now - _windowStart) : 0.001f;
        }

        internal static List<string> ListGcLines(bool forceRefresh)
        {
            if (!forceRefresh && _gcCacheLines != null && Time.unscaledTime - _gcCacheAt < 0.25f)
                return _gcCacheLines;

            int g0, g1, g2;
            long mem, peak, alloc, freed;
            float rate, peakFrame, winSec;
            int g2hitch;
            GetGcStats(out g0, out g1, out g2, out mem, out peak, out alloc, out freed,
                out rate, out peakFrame, out g2hitch, out winSec);

            float allocMb = alloc / (1024f * 1024f);
            float freedMb = freed / (1024f * 1024f);
            float allocPerSec = winSec > 0.001f ? allocMb / winSec : 0f;

            List<string> lines = new List<string>(16);
            lines.Add(string.Format(Inv,
                "mono_heap now={0:0.00}MB  peak={1:0.00}MB  netRate={2:+0.00;-0.00}MB/s",
                mem / (1024f * 1024f), peak / (1024f * 1024f), rate));
            lines.Add(string.Format(Inv,
                "window {0:0.0}s  collections Δgen0={1}  Δgen1={2}  Δgen2={3}",
                winSec, g0, g1, g2));
            lines.Add(string.Format(Inv,
                "alloc≈{0:0.00}MB (sum +Δmem)  ≈{1:0.00}MB/s  freed≈{2:0.00}MB  peakFrameAlloc={3:0.00}MB",
                allocMb, allocPerSec, freedMb, peakFrame));
            lines.Add(string.Format(Inv,
                "gen2+hitch(≥40ms) frames={0}  (correlate GC pauses)",
                g2hitch));
            lines.Add("lifetime CollectionCount: gen0=" + SafeCollectionCount(0)
                + "  gen1=" + SafeCollectionCount(1)
                + "  gen2=" + SafeCollectionCount(2));

            // Recent GC-active frames from ring.
            lines.Add("--- recent GC-active frames ---");
            int shown = 0;
            if (_gcRingCount > 0)
            {
                int start = (_gcRingCount < GcRingSize) ? 0 : _gcRingWrite;
                // Walk newest-first.
                for (int i = _gcRingCount - 1; i >= 0 && shown < 12; i--)
                {
                    GcSample s = GcRing[(start + i) % GcRingSize];
                    if (s.D0 == 0 && s.D1 == 0 && s.D2 == 0 && Math.Abs(s.DMem) < 64 * 1024)
                        continue;
                    lines.Add(string.Format(Inv,
                        "t={0:0.0}s  dt={1:0.0}ms  dG0={2} dG1={3} dG2={4}  dMem={5:+0.00;-0.00}MB  heap={6:0.00}MB",
                        s.Time, s.DtMs, s.D0, s.D1, s.D2,
                        s.DMem / (1024f * 1024f), s.Mem / (1024f * 1024f)));
                    shown++;
                }
            }
            if (shown == 0)
                lines.Add("(no notable GC activity in recent ring)");

            _gcCacheLines = lines;
            _gcCacheAt = Time.unscaledTime;
            return lines;
        }

        private static int SafeCollectionCount(int gen)
        {
            try { return GC.CollectionCount(gen); }
            catch { return -1; }
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
                    "{0} BepInEx plugins. TGM-85 / ACM-119 / AAM-2CV belong in VeyrnWeapons.dll — extra WeXon.dll / Kh85MT.dll stack hangar + missile patches.",
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
                return "<< already hosted inside VeyrnWeapons.dll — disable standalone Kh85MT";
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
            List<string> lines = new List<string>(rows.Count + 3);
            lines.Add(string.Format(Inv, "patched methods: {0}", methods));
            lines.Add("Owner counts ≠ wall-clock. Use heavy sample (Harmony tab) for timed top-N methods.");
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
            // Hide our own temporary sampler id from owner tallies when present.
            if (o.IndexOf("oritasy.perf.timing", StringComparison.Ordinal) >= 0)
                return "OritasyPerf timing sample (temp)";
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

        /// <summary>Scene GameObject / component census. Hitchy — cached ~2.5s unless force.</summary>
        internal static List<string> ListSceneCensus(bool force)
        {
            if (!force && _sceneCache != null && Time.unscaledTime - _sceneCacheAt < 2.5f)
                return _sceneCache;
            List<string> lines = CaptureSceneCensusUncached();
            _sceneCache = lines;
            _sceneCacheAt = Time.unscaledTime;
            return lines;
        }

        private static List<string> CaptureSceneCensusUncached()
        {
            List<string> lines = new List<string>(24);
            try
            {
                UnityEngine.SceneManagement.Scene scene =
                    UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                _sceneName = scene.IsValid() ? scene.name : "(none)";
                int rootCount = 0;
                try { rootCount = scene.rootCount; }
                catch { }

                GameObject[] allGo = null;
                try { allGo = UnityEngine.Object.FindObjectsOfType<GameObject>(); }
                catch (Exception ex)
                {
                    lines.Add("scene scan error: " + ex.Message);
                    return lines;
                }

                int goTotal = allGo != null ? allGo.Length : 0;
                int goActive = 0;
                int compTotal = 0;
                int behaviours = 0;
                Dictionary<string, int> topTypes = new Dictionary<string, int>(64);

                if (allGo != null)
                {
                    for (int i = 0; i < allGo.Length; i++)
                    {
                        GameObject go = allGo[i];
                        if (go == null)
                            continue;
                        if (go.activeInHierarchy)
                            goActive++;
                        Component[] comps = null;
                        try { comps = go.GetComponents<Component>(); }
                        catch { continue; }
                        if (comps == null)
                            continue;
                        for (int c = 0; c < comps.Length; c++)
                        {
                            Component comp = comps[c];
                            if (comp == null)
                                continue;
                            compTotal++;
                            if (comp is Behaviour)
                                behaviours++;
                            Type t = comp.GetType();
                            string tn = t != null ? t.Name : "?";
                            int n;
                            topTypes.TryGetValue(tn, out n);
                            topTypes[tn] = n + 1;
                        }
                    }
                }

                _sceneGoTotal = goTotal;
                _sceneGoActive = goActive;
                _sceneCompTotal = compTotal;
                _sceneCompBehaviours = behaviours;

                lines.Add(string.Format(Inv,
                    "scene={0}  roots={1}  GameObjects={2} (active={3})",
                    _sceneName, rootCount, goTotal, goActive));
                lines.Add(string.Format(Inv,
                    "components={0}  behaviours={1}  other={2}",
                    compTotal, behaviours, Math.Max(0, compTotal - behaviours)));

                List<KeyValuePair<string, int>> rows = new List<KeyValuePair<string, int>>(topTypes.Count);
                foreach (KeyValuePair<string, int> kv in topTypes)
                    rows.Add(kv);
                rows.Sort(CompareCountDesc);
                int limit = Math.Min(20, rows.Count);
                lines.Add("--- top component types ---");
                for (int i = 0; i < limit; i++)
                    lines.Add(string.Format(Inv, "{0,-36}  n={1}", rows[i].Key, rows[i].Value));
                if (rows.Count == 0)
                    lines.Add("(empty)");
            }
            catch (Exception ex)
            {
                lines.Add("scene census error: " + ex.Message);
            }
            return lines;
        }

        internal static string WriteReport(bool zh)
        {
            float avgFps, avgDt, peakDt, winSec, instant, p1, stallSec;
            int frames, hitches;
            GetFrameStatsEx(out avgFps, out avgDt, out peakDt, out frames, out winSec,
                out instant, out p1, out hitches, out stallSec);

            StringBuilder sb = new StringBuilder(8192);
            sb.AppendLine("=== Oritasy Performance Report v2 ===");
            sb.AppendLine("format: " + ReportFormatVersion);
            sb.AppendLine();

            AppendSection(sb, "meta");
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
            sb.AppendLine(string.Format(Inv, "unity: {0}  screen={1}x{2}  vSync={3}  targetFps={4}  cpu={5}",
                Application.unityVersion, Screen.width, Screen.height,
                QualitySettings.vSyncCount, Application.targetFrameRate,
                Environment.ProcessorCount));
            EndSection(sb);

            AppendSection(sb, "probe");
            sb.AppendLine("sampling=" + (_sampling ? "ON" : "OFF")
                + "  menuOpen=" + PerfProbeMenu.IsOpen
                + "  " + OritasyWorker.SnapshotLine());
            sb.AppendLine(PerfFrameGate.SnapshotLine());
            sb.AppendLine("harmony_heavy=" + (HarmonyTimingSample.Active ? "ON" : "OFF")
                + "  " + HarmonyTimingSample.Status);
            sb.AppendLine("Harmony/plugin lists cached 2s on main thread (Harmony is not worker-safe).");
            sb.AppendLine("Report file write / WAV decode queued to 4 OritasyWorkers (no Unity APIs).");
            EndSection(sb);

            AppendSection(sb, "gc");
            List<string> gcLines = ListGcLines(true);
            for (int i = 0; i < gcLines.Count; i++)
                sb.AppendLine(gcLines[i]);
            EndSection(sb);

            AppendSection(sb, "plugins");
            List<string> plugs = ListPlugins();
            for (int i = 0; i < plugs.Count; i++)
                sb.AppendLine(plugs[i]);
            EndSection(sb);

            AppendSection(sb, "harmony_owners");
            List<string> harm = ListHarmonyOwners();
            for (int i = 0; i < harm.Count; i++)
                sb.AppendLine(harm[i]);
            EndSection(sb);

            AppendSection(sb, "harmony_timing");
            HarmonyTimingSample.AppendReport(sb);
            EndSection(sb);

            AppendSection(sb, "subsystems");
            List<string> bucks = SnapshotBucketLines(true);
            if (bucks.Count == 0)
                sb.AppendLine("(no samples — leave menu open / fly for a few seconds)");
            for (int i = 0; i < bucks.Count; i++)
                sb.AppendLine(bucks[i]);
            EndSection(sb);

            AppendSection(sb, "scene");
            List<string> scene = ListSceneCensus(true);
            for (int i = 0; i < scene.Count; i++)
                sb.AppendLine(scene[i]);
            EndSection(sb);

            AppendSection(sb, "spikes");
            List<string> spikes = ListRecentSpikes(20);
            if (spikes.Count == 0)
                sb.AppendLine("(none)");
            for (int i = 0; i < spikes.Count; i++)
                sb.AppendLine(spikes[i]);
            EndSection(sb);

            AppendSection(sb, "notes");
            sb.AppendLine("avgFps = 1000/mean(frame dt). instantFps is the LAST frame only — not an average.");
            sb.AppendLine("p1Low = 1% low from the last 256 frames (includes hitches).");
            sb.AppendLine("Buckets measure Oritasy/WeXon instrumented Update groups only (always wall-clock when sampling ON).");
            sb.AppendLine("harmony_timing heavy sample wraps top-N patched methods with Prefix+Finalizer Stopwatch (toggleable).");
            sb.AppendLine("GC alloc≈ sum of positive GetTotalMemory deltas (estimate; not a profiler).");
            sb.AppendLine("Scene census uses FindObjectsOfType — hitchy; only on report / Scene tab refresh.");
            sb.AppendLine("Toggle key: backtick (`). Sampling defaults OFF. Heavy Harmony sample defaults OFF.");
            EndSection(sb);

            sb.AppendLine("### END");

            string dir = ReportDir();
            try
            {
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
            }
            catch { }

            string name = "perf_v2_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss", Inv) + ".txt";
            string path = Path.Combine(dir, name);
            string payload = sb.ToString();
            if (!string.IsNullOrEmpty(_lastReportPath))
                _prevReportPath = _lastReportPath;
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

        private static void AppendSection(StringBuilder sb, string name)
        {
            sb.AppendLine("### SECTION:" + name);
        }

        private static void EndSection(StringBuilder sb)
        {
            sb.AppendLine("### /SECTION");
            sb.AppendLine();
        }

        /// <summary>Diff two v2 reports by section. Returns human-readable lines.</summary>
        internal static List<string> CompareReports(string pathA, string pathB)
        {
            List<string> lines = new List<string>(64);
            if (string.IsNullOrEmpty(pathA) || string.IsNullOrEmpty(pathB))
            {
                lines.Add("need two report paths");
                return lines;
            }
            string textA = null;
            string textB = null;
            try { textA = File.ReadAllText(pathA, Encoding.UTF8); }
            catch (Exception ex)
            {
                lines.Add("read A failed: " + ex.Message);
                return lines;
            }
            try { textB = File.ReadAllText(pathB, Encoding.UTF8); }
            catch (Exception ex)
            {
                lines.Add("read B failed: " + ex.Message);
                return lines;
            }

            Dictionary<string, List<string>> secA = ParseSections(textA);
            Dictionary<string, List<string>> secB = ParseSections(textB);

            lines.Add(string.Format(Inv, "COMPARE  A={0}", Path.GetFileName(pathA)));
            lines.Add(string.Format(Inv, "         B={0}", Path.GetFileName(pathB)));
            lines.Add("formatA=" + DetectFormat(textA) + "  formatB=" + DetectFormat(textB));

            string[] order = new string[]
            {
                "meta", "gc", "harmony_owners", "harmony_timing", "subsystems", "scene", "spikes", "probe", "plugins"
            };
            HashSet<string> seen = new HashSet<string>();
            for (int i = 0; i < order.Length; i++)
            {
                DiffSection(lines, order[i], secA, secB);
                seen.Add(order[i]);
            }
            foreach (KeyValuePair<string, List<string>> kv in secA)
            {
                if (!seen.Contains(kv.Key))
                    DiffSection(lines, kv.Key, secA, secB);
            }
            foreach (KeyValuePair<string, List<string>> kv in secB)
            {
                if (!seen.Contains(kv.Key) && !secA.ContainsKey(kv.Key))
                    DiffSection(lines, kv.Key, secA, secB);
            }
            return lines;
        }

        /// <summary>Diff last two written reports (prev vs last).</summary>
        internal static List<string> CompareLastTwoReports()
        {
            if (string.IsNullOrEmpty(_prevReportPath) || string.IsNullOrEmpty(_lastReportPath))
            {
                // Fall back to newest two files on disk.
                string[] pair = FindNewestTwoReports();
                if (pair == null)
                {
                    List<string> miss = new List<string>(1);
                    miss.Add("need two reports — write performance report twice, then Diff last two");
                    return miss;
                }
                return CompareReports(pair[0], pair[1]);
            }
            return CompareReports(_prevReportPath, _lastReportPath);
        }

        private static string[] FindNewestTwoReports()
        {
            try
            {
                string dir = ReportDir();
                if (!Directory.Exists(dir))
                    return null;
                string[] files = Directory.GetFiles(dir, "perf*.txt");
                if (files == null || files.Length < 2)
                    return null;
                Array.Sort(files, CompareFileWriteTimeAsc);
                return new string[] { files[files.Length - 2], files[files.Length - 1] };
            }
            catch { return null; }
        }

        private static int CompareFileWriteTimeAsc(string a, string b)
        {
            DateTime ta = File.GetLastWriteTimeUtc(a);
            DateTime tb = File.GetLastWriteTimeUtc(b);
            return ta.CompareTo(tb);
        }

        private static string DetectFormat(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "?";
            if (text.IndexOf("perf_report_v2", StringComparison.Ordinal) >= 0
                || text.IndexOf("Report v2", StringComparison.Ordinal) >= 0)
                return ReportFormatVersion;
            return "legacy_v1";
        }

        private static Dictionary<string, List<string>> ParseSections(string text)
        {
            Dictionary<string, List<string>> map = new Dictionary<string, List<string>>(16);
            if (string.IsNullOrEmpty(text))
                return map;
            string[] raw = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            string cur = null;
            List<string> buf = null;
            for (int i = 0; i < raw.Length; i++)
            {
                string line = raw[i];
                if (line.StartsWith("### SECTION:", StringComparison.Ordinal))
                {
                    cur = line.Substring("### SECTION:".Length).Trim();
                    buf = new List<string>(32);
                    map[cur] = buf;
                    continue;
                }
                if (line.StartsWith("### /SECTION", StringComparison.Ordinal)
                    || line.StartsWith("### END", StringComparison.Ordinal))
                {
                    cur = null;
                    buf = null;
                    continue;
                }
                // Legacy v1 fallback: --- Title ---
                if (cur == null && line.StartsWith("--- ", StringComparison.Ordinal) && line.EndsWith(" ---"))
                {
                    cur = "legacy:" + line.Trim('-', ' ').ToLowerInvariant().Replace(' ', '_');
                    buf = new List<string>(32);
                    map[cur] = buf;
                    continue;
                }
                if (buf != null)
                    buf.Add(line);
            }
            return map;
        }

        private static void DiffSection(List<string> outLines, string name,
            Dictionary<string, List<string>> a, Dictionary<string, List<string>> b)
        {
            List<string> la;
            List<string> lb;
            a.TryGetValue(name, out la);
            b.TryGetValue(name, out lb);
            if (la == null && lb == null)
                return;
            outLines.Add("");
            outLines.Add("## " + name);
            if (la == null)
            {
                outLines.Add("  (only in B)");
                AppendPrefixed(outLines, lb, "  + ");
                return;
            }
            if (lb == null)
            {
                outLines.Add("  (only in A)");
                AppendPrefixed(outLines, la, "  - ");
                return;
            }

            // Numeric key=value / labeled metric lines: show A→B when numbers differ.
            Dictionary<string, string> na = ExtractNumericFacts(la);
            Dictionary<string, string> nb = ExtractNumericFacts(lb);
            int changes = 0;
            foreach (KeyValuePair<string, string> kv in na)
            {
                string bv;
                if (!nb.TryGetValue(kv.Key, out bv))
                {
                    outLines.Add(string.Format(Inv, "  - {0} = {1}", kv.Key, kv.Value));
                    changes++;
                    continue;
                }
                if (!string.Equals(kv.Value, bv, StringComparison.Ordinal))
                {
                    outLines.Add(string.Format(Inv, "  ~ {0}: {1}  →  {2}", kv.Key, kv.Value, bv));
                    changes++;
                }
            }
            foreach (KeyValuePair<string, string> kv in nb)
            {
                if (!na.ContainsKey(kv.Key))
                {
                    outLines.Add(string.Format(Inv, "  + {0} = {1}", kv.Key, kv.Value));
                    changes++;
                }
            }

            // Line-set diff for non-numeric remainder (capped).
            HashSet<string> setA = new HashSet<string>();
            HashSet<string> setB = new HashSet<string>();
            for (int i = 0; i < la.Count; i++)
            {
                string t = la[i].Trim();
                if (t.Length > 0)
                    setA.Add(t);
            }
            for (int i = 0; i < lb.Count; i++)
            {
                string t = lb[i].Trim();
                if (t.Length > 0)
                    setB.Add(t);
            }
            int lineDiffs = 0;
            foreach (string s in setA)
            {
                if (setB.Contains(s))
                    continue;
                if (LooksNumericFactLine(s))
                    continue;
                if (lineDiffs >= 12)
                    break;
                outLines.Add("  - " + TruncLine(s, 100));
                lineDiffs++;
                changes++;
            }
            foreach (string s in setB)
            {
                if (setA.Contains(s))
                    continue;
                if (LooksNumericFactLine(s))
                    continue;
                if (lineDiffs >= 24)
                    break;
                outLines.Add("  + " + TruncLine(s, 100));
                lineDiffs++;
                changes++;
            }
            if (changes == 0)
                outLines.Add("  (identical)");
        }

        private static void AppendPrefixed(List<string> outLines, List<string> src, string prefix)
        {
            if (src == null)
                return;
            int n = Math.Min(src.Count, 20);
            for (int i = 0; i < n; i++)
            {
                string t = src[i].Trim();
                if (t.Length == 0)
                    continue;
                outLines.Add(prefix + TruncLine(t, 100));
            }
        }

        private static Dictionary<string, string> ExtractNumericFacts(List<string> lines)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(32);
            if (lines == null)
                return map;
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (string.IsNullOrEmpty(line))
                    continue;
                // key: value
                int colon = line.IndexOf(':');
                if (colon > 0 && colon < 48)
                {
                    string key = line.Substring(0, colon).Trim();
                    string rest = line.Substring(colon + 1).Trim();
                    if (ContainsDigit(rest))
                        map[key] = rest;
                }
                // token=number pairs
                string[] parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                for (int p = 0; p < parts.Length; p++)
                {
                    int eq = parts[p].IndexOf('=');
                    if (eq <= 0)
                        continue;
                    string k = parts[p].Substring(0, eq);
                    string v = parts[p].Substring(eq + 1);
                    if (k.Length > 40 || !ContainsDigit(v))
                        continue;
                    map[k] = v;
                }
            }
            return map;
        }

        private static bool LooksNumericFactLine(string s)
        {
            return s != null && (s.IndexOf('=') > 0 || (s.IndexOf(':') > 0 && ContainsDigit(s)));
        }

        private static bool ContainsDigit(string s)
        {
            if (string.IsNullOrEmpty(s))
                return false;
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsDigit(s[i]))
                    return true;
            }
            return false;
        }

        private static string TruncLine(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max)
                return s ?? "";
            return s.Substring(0, max - 2) + "..";
        }

        internal static string ReportDir()
        {
            try { return Path.Combine(Paths.PluginPath, "OritasyPerf"); }
            catch { return Path.Combine(Application.dataPath, "OritasyPerf"); }
        }
    }
}
