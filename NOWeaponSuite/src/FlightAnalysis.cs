using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Built-in Flight Recorder analysis for Oritasy Profile sessions.
    /// Soft-yields when standalone FlightRecorder.dll is already loaded.
    /// </summary>
    internal static class FlightAnalysis
    {
        private const string StandaloneGuid = "com.qiaochen.flightrecorder";
        private const string StandaloneGuidNew = "com.iallemege.flightrecorder";

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> SampleHz;
        internal static ConfigEntry<bool> AutoAnalyzeOnStop;
        internal static ConfigEntry<bool> ShowRecIndicator;
        internal static ConfigEntry<KeyCode> AnalyzeKey;
        internal static ConfigEntry<bool> EnableFlightScoreXp;
        internal static ConfigEntry<float> MaxFlightScoreXpMul;

        /// <summary>Hard ceiling for match XP multiplier (config cannot exceed this).</summary>
        private static float HardXpMulCap
        {
            get { return FlightScoreMathService.HardXpMulCap; }
        }

        private static bool _bound;
        private static bool _yieldToStandalone;
        private static string _dir;
        private static bool _recording;
        private static long _sessionTicks;
        private static Aircraft _cachedPlayer;
        private static float _nextSampleAt;
        private static float _nextResolveAt;
        private static float _nextFlushAt;
        private static StreamWriter _csv;
        private static StringBuilder _buf = new StringBuilder(4096);
        private static string _csvPath;
        private static string _analysisPath;
        private static float _sessionStartUnscaled;
        private static string _unitName = "";

        private static bool _prevLanded = true;
        private static bool _prevAirborne;
        private static bool _hadAirborne;
        private static Vector3 _prevPos;
        private static bool _hasPrevPos;
        private static float _prevPitchIn, _prevRollIn, _prevYawIn;
        private static bool _hasPrevInputs;

        private static int _sampleCount;
        private static double _distSum;
        private static double _spdSum;
        private static float _maxSpd, _maxAlt, _minAlt, _maxAbsG;
        private static double _pitchSumSq, _rollSumSq, _yawSumSq;
        private static double _thrSum;
        private static int _highDeflCount, _fullThrCount, _idleThrCount;
        private static int _invertedCount;
        private static int _noeCount;          // nap-of-the-earth: low AGL + high speed
        private static int _highGTurnCount;    // sustained high-G banked turns
        private static int _airborneCount;     // samples while airborne (maneuver denom)
        private static float _maxBankAbs, _maxPitchAbs, _minPitch;
        private static double _jitterSumSq;
        private static int _jitterSamples;
        private static int _weaponFires;
        private static string _lastWeapon = "";
        private static bool _takeoffMarked, _landingMarked, _crashMarked;
        private static float _landSink, _landSpd, _landAgl;
        private static bool _landQualityValid;
        private static bool _airframeCompromised;
        private static float _nextIntegrityAt;
        private static int _cleanLandingXpCount;
        private static readonly Dictionary<int, float> PartHpBaseline = new Dictionary<int, float>(64);
        private const int CleanLandingXp = 100;

        private static AnalysisResult _lastAnalysis;
        private static long _lastAnalysisTicks;
        private static GUIStyle _recStyle;
        private static GUIStyle _titleStyle;
        private static GUIStyle _bodyStyle;
        private static GUIStyle _sectionStyle;
        private static GUIStyle _btnStyle;
        private static bool _stylesReady;

        private static bool _hadLocalPlayer;
        private static bool _autoScoreOpened;
        private static bool _prevDisabled;
        private static bool _prevAlive = true;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        internal sealed class AnalysisResult
        {
            public float DurationSec;
            public float DistanceM;
            public float MaxSpeed;
            public float AvgSpeed;
            public float MaxAlt;
            public float MinAlt;
            public float MaxAbsG;
            public float PitchRms, RollRms, YawRms;
            public float HighDeflPct;
            public float AvgThrottle;
            public float FullThrPct;
            public float IdleThrPct;
            public float MaxBank;
            public float MaxPitch;
            public float MinPitch;
            public float InvertedPct;
            public float NoePct;
            public float HighGTurnPct;
            public float Smoothness;
            public int ManeuverBonus;
            public string ManeuverNotes;
            public bool LandingValid;
            public float LandSink, LandSpd, LandAgl;
            public string LandingNote;
            public int WeaponFires;
            public string LastWeapon;
            public bool Takeoff, Landing, Crash;
            public int CleanLandingXp;
            public int Score;
            public string Grade;
            public string Tips;
            public string UnitName;
            public string CsvPath;
            public long SessionTicks;
        }

        internal static void Bind(ConfigFile config)
        {
            if (config == null || _bound)
                return;
            _bound = true;
            Enabled = config.Bind("FlightAnalysis", "Enabled", false,
                "Flight ops recording + analysis report. Off by default — enable in Oritasy Profile. Writes plugins/OritasyReplays/ when ON.");
            SampleHz = config.Bind("FlightAnalysis", "SampleHz", 4.5f,
                "Flight analysis sample rate (Hz). Default 4.5 (LowEnd); keep 4-6 for performance.");
            AutoAnalyzeOnStop = config.Bind("FlightAnalysis", "AutoAnalyzeOnStop", true,
                "Write analysis text when a kept session ends.");
            ShowRecIndicator = config.Bind("FlightAnalysis", "ShowRecIndicator", false,
                "Show REC indicator while flight analysis is recording.");
            AnalyzeKey = config.Bind("FlightAnalysis", "AnalyzeKey", KeyCode.None,
                "Removed. Flight score opens only via F1 → Flight Score tab (or auto on crash/leave). Always None.");
            EnableFlightScoreXp = config.Bind("FlightAnalysis", "EnableFlightScoreXp", true,
                "Multiply that match's career XP by flight score (1.0–MaxFlightScoreXpMul).");
            MaxFlightScoreXpMul = config.Bind("FlightAnalysis", "MaxFlightScoreXpMul", 3.8f,
                "Max XP multiplier from flight score (hard-capped at 3.8). Score 0→1.0×, 100→max.");
            // Clean-landing bonus: +100 career XP per undamaged touchdown (see Sample).

            // One-shot 40C: lower stock 12 Hz installs to 6 Hz.
            // 46C: demote AnalyzeKey F10 so F10 stays Aerial Resupply / Repair.
            // 49C: clear AnalyzeKey F9/F10 (and any other) — F9 Support, F10 Resupply/Repair only.
            try
            {
                ConfigEntry<int> perfRev = config.Bind("FlightAnalysis", "PerfRevision", 0,
                    "Internal: raised once when applying 40C sample defaults.");
                if (perfRev.Value < 40)
                {
                    SampleHz.Value = 6f;
                    perfRev.Value = 40;
                }
                if (perfRev.Value < 46)
                {
                    if (AnalyzeKey != null && AnalyzeKey.Value == KeyCode.F10)
                        AnalyzeKey.Value = KeyCode.None;
                    perfRev.Value = 46;
                }
                if (perfRev.Value < 48)
                {
                    if (SampleHz != null && SampleHz.Value > 4.5f)
                        SampleHz.Value = 4.5f;
                    perfRev.Value = 48;
                }
                if (perfRev.Value < 49)
                {
                    // Flight Score must never steal F9 (Support) or F10 (Resupply/Repair).
                    if (AnalyzeKey != null)
                        AnalyzeKey.Value = KeyCode.None;
                    perfRev.Value = 49;
                }
                if (perfRev.Value < 50)
                {
                    // Opt-in: sampling/report was always-on and cost main-thread time in flight.
                    if (Enabled != null)
                        Enabled.Value = false;
                    perfRev.Value = 50;
                }
            }
            catch { }

            // Oritasy always owns recording. Standalone FlightRecorder.dll soft-yields itself.
            _yieldToStandalone = false;
            try
            {
                _dir = Path.Combine(Paths.PluginPath, "OritasyReplays");
                Directory.CreateDirectory(_dir);
            }
            catch
            {
                _dir = Path.Combine(Application.persistentDataPath, "OritasyReplays");
                try { Directory.CreateDirectory(_dir); }
                catch { }
            }

            if (Plugin.Log != null)
            {
                bool standalonePresent = false;
                try
                {
                    standalonePresent = Chainloader.PluginInfos != null
                        && (Chainloader.PluginInfos.ContainsKey(StandaloneGuid)
                            || Chainloader.PluginInfos.ContainsKey(StandaloneGuidNew));
                }
                catch { }
                Plugin.Log.LogInfo("FlightAnalysis: built-in active → " + _dir
                    + (standalonePresent ? " (standalone FlightRecorder should yield)" : ""));
            }
        }

        internal static bool OwnsRecording
        {
            get
            {
                return Enabled != null && Enabled.Value && !_yieldToStandalone;
            }
        }

        internal static void OnSessionBegin(long startTicks, string mission, string server)
        {
            if (!OwnsRecording)
                return;
            if (_recording)
                StopRecording(false);
            FlightTrackMap.ClearMatch();
            _autoScoreOpened = false;
            _lastAnalysis = null;
            // Recording starts when the player boards an aircraft (see Tick).
        }

        internal static void OnSessionEnd(long startTicks, bool keep)
        {
            if (_recording)
            {
                if (!keep || _sampleCount < 8)
                {
                    StopRecording(false);
                    TryDelete(_sessionTicks);
                }
                else
                    StopRecording(true);
            }
            if (!keep)
                FlightTrackMap.ClearMatch();
        }

        internal static void NoteWeaponFire(string weaponName)
        {
            if (!_recording || !OwnsRecording)
                return;
            _weaponFires++;
            _lastWeapon = string.IsNullOrEmpty(weaponName) ? "WEAPON" : weaponName;
        }

        internal static bool HasAnalysis(long startTicks)
        {
            if (startTicks == 0)
                return false;
            string path = AnalysisPathFor(startTicks);
            return !string.IsNullOrEmpty(path) && File.Exists(path);
        }

        internal static bool TryShowAnalysis(long startTicks)
        {
            if (startTicks == 0)
                return false;
            if (_lastAnalysis != null && _lastAnalysis.SessionTicks == startTicks)
            {
                TryOpenF1FlightScore();
                return true;
            }
            string path = AnalysisPathFor(startTicks);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return false;
            // Minimal stub from file existence — reload summary fields from last if same ticks
            AnalysisResult r = new AnalysisResult();
            r.SessionTicks = startTicks;
            r.CsvPath = CsvPathFor(startTicks);
            r.UnitName = "";
            r.Grade = "?";
            r.Score = 0;
            r.Tips = "Open analysis file:\n" + path;
            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                ParseAnalysisText(text, r);
            }
            catch { }
            _lastAnalysis = r;
            _lastAnalysisTicks = startTicks;
            TryOpenF1FlightScore();
            return true;
        }

        /// <summary>
        /// Linear map: score 0 → 1.0×, score 100 → maxMul (≤ 3.8). Disabled → 1.0×.
        /// </summary>
        internal static float XpMultiplierForScore(int score)
        {
            bool enabled = EnableFlightScoreXp != null && EnableFlightScoreXp.Value;
            float maxMul = MaxFlightScoreXpMul != null ? MaxFlightScoreXpMul.Value : HardXpMulCap;
            return FlightScoreMathService.XpMultiplierForScore(score, enabled, maxMul);
        }

        /// <summary>
        /// After OnSessionEnd(keep), read score/mul for that session. Returns false → use 1.0×.
        /// </summary>
        internal static bool TryGetSessionXpMultiplier(long startTicks, out float mul, out int score, out string grade)
        {
            mul = 1f;
            score = 0;
            grade = "";
            if (EnableFlightScoreXp == null || !EnableFlightScoreXp.Value)
                return false;
            if (_lastAnalysis != null && _lastAnalysis.SessionTicks == startTicks)
            {
                score = _lastAnalysis.Score;
                grade = _lastAnalysis.Grade ?? "";
                mul = XpMultiplierForScore(score);
                return true;
            }
            string path = AnalysisPathFor(startTicks);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return false;
            AnalysisResult r = new AnalysisResult();
            r.SessionTicks = startTicks;
            r.Grade = "";
            r.Score = 0;
            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                ParseAnalysisText(text, r);
            }
            catch { return false; }
            score = r.Score;
            grade = r.Grade ?? "";
            mul = XpMultiplierForScore(score);
            return true;
        }

        internal static string FormatXpMulLabel(float mul, bool chinese)
        {
            return FlightScoreMathService.FormatXpMulLabel(mul, chinese);
        }

        /// <summary>Refresh last/live score snapshot for F1 Flight Score tab or auto panel.</summary>
        internal static void PrepareDisplayScore()
        {
            if (_recording && _sampleCount >= 4)
            {
                AnalysisResult r = BuildAnalysis();
                _lastAnalysis = r;
                _lastAnalysisTicks = _sessionTicks;
            }
        }

        /// <summary>Draw score body inside F1 System tab (caller owns chrome / scroll).</summary>
        internal static void DrawEmbeddedScore(GUIStyle title, GUIStyle label, GUIStyle section, GUIStyle btn)
        {
            PrepareDisplayScore();
            bool zh = ModUiLang.IsChinese;
            FlightTrackMap.DrawMatchUi(section ?? _sectionStyle, label ?? _bodyStyle, btn ?? _btnStyle, zh);
            GUILayout.Space(8f);

            AnalysisResult r = FlightTrackMap.ResolveViewAnalysis(_lastAnalysis);
            if (r == null)
            {
                GUILayout.Label(zh ? "飞行评分" : "FLIGHT SCORE", section ?? _sectionStyle);
                GUILayout.Label(zh
                    ? "尚无评分。上机后记录；离机 / 坠毁后生成评分并保留在本局列表。"
                    : "No score yet. Board to record; leave / crash generates a score kept for this match.",
                    label ?? _bodyStyle);
                if (_recording)
                {
                    GUILayout.Label(zh
                        ? ("记录中… 样本 " + _sampleCount)
                        : ("Recording… samples " + _sampleCount),
                        label ?? _bodyStyle);
                }
                return;
            }
            DrawScoreLabels(r, title ?? _titleStyle, label ?? _bodyStyle, section ?? _sectionStyle, zh);
        }

        internal static void CloseScorePanel()
        {
            // Score is F1-only — nothing to close here.
        }

        internal static bool IsScorePanelOpen
        {
            get { return false; }
        }

        private static void ParseAnalysisText(string text, AnalysisResult r)
        {
            if (string.IsNullOrEmpty(text) || r == null)
                return;
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("Unit:", StringComparison.OrdinalIgnoreCase))
                    r.UnitName = line.Substring(5).Trim();
                else if (line.StartsWith("Grade:", StringComparison.OrdinalIgnoreCase))
                {
                    // Grade: A  (92/100)
                    string rest = line.Substring(6).Trim();
                    int paren = rest.IndexOf('(');
                    if (paren > 0)
                    {
                        r.Grade = rest.Substring(0, paren).Trim();
                        string num = rest.Substring(paren + 1).Replace("/100)", "").Trim();
                        int sc;
                        if (int.TryParse(num, NumberStyles.Integer, Inv, out sc))
                            r.Score = sc;
                    }
                    else
                        r.Grade = rest;
                }
                else if (line.StartsWith("Tips:", StringComparison.OrdinalIgnoreCase))
                    r.Tips = line.Substring(5).Trim();
                else if (line.StartsWith("Duration:", StringComparison.OrdinalIgnoreCase))
                {
                    float v;
                    if (TryFirstFloat(line, out v))
                        r.DurationSec = v;
                }
                else if (line.StartsWith("Distance:", StringComparison.OrdinalIgnoreCase))
                {
                    float v;
                    if (TryFirstFloat(line, out v))
                        r.DistanceM = v;
                }
            }
        }

        private static bool TryFirstFloat(string line, out float v)
        {
            v = 0f;
            string[] parts = line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                if (float.TryParse(parts[i], NumberStyles.Float, Inv, out v))
                    return true;
            }
            return false;
        }

        internal static void Tick()
        {
            try
            {
                // Escape is owned by F1 System when open; no standalone score panel.
            }
            catch { }

            if (!OwnsRecording)
                return;

            float now = Time.unscaledTime;
            if (now >= _nextResolveAt)
            {
                _nextResolveAt = now + 0.5f;
                ResolvePlayer();
            }

            Aircraft ac = _cachedPlayer;
            FlightRecordGateService.RecordAction rec = FlightRecordGateService.ResolveRecordAction(
                true, ac != null, _recording);
            if (rec == FlightRecordGateService.RecordAction.Start)
                StartRecording(DateTime.UtcNow.Ticks);
            else if (rec == FlightRecordGateService.RecordAction.StopAnalyze)
            {
                StopRecording(_sampleCount >= FlightRecordGateService.MinSamplesForAnalyze);
                if (_lastAnalysis != null && !_autoScoreOpened)
                    TriggerAutoScoreOpen();
            }

            if (!_recording)
                return;

            MaybeAutoOpenScore(ac);

            if (ac == null)
                return;

            bool disabled = false;
            try { disabled = ac.disabled; }
            catch { }
            if (disabled)
                _crashMarked = true;

            float hz = SampleHz != null ? SampleHz.Value : 4.5f;
            float perfCap = CachedFlightSampleHzCap();
            hz = FlightRecordGateService.ClampSampleHz(hz, perfCap);
            if (!FlightRecordGateService.SampleDue(now, _nextSampleAt))
            {
                MaybeFlush(now);
                return;
            }
            _nextSampleAt = FlightRecordGateService.ScheduleNextSample(now, hz);
            Sample(ac);
            MaybeFlush(now);
        }

        private static Type _perfModeType;
        private static System.Reflection.MethodInfo _flightSampleHzCapMethod;
        private static bool _perfModeResolved;

        private static float CachedFlightSampleHzCap()
        {
            if (!_perfModeResolved)
            {
                _perfModeResolved = true;
                try
                {
                    _perfModeType = Type.GetType("Oritasy.PerfMode");
                    if (_perfModeType != null)
                    {
                        _flightSampleHzCapMethod = _perfModeType.GetMethod("FlightSampleHzCap",
                            System.Reflection.BindingFlags.Static
                            | System.Reflection.BindingFlags.Public
                            | System.Reflection.BindingFlags.NonPublic);
                    }
                }
                catch { }
            }
            if (_flightSampleHzCapMethod == null)
                return 0f;
            try
            {
                object c = _flightSampleHzCapMethod.Invoke(null, null);
                if (c is float)
                    return (float)c;
            }
            catch { }
            return 0f;
        }

        private static void MaybeAutoOpenScore(Aircraft ac)
        {
            bool crash = false;
            bool left = false;
            bool dead = false;

            if (ac != null)
            {
                try { crash = ac.disabled; }
                catch { }
                try { dead = !Plugin.IsUnitAlive(ac); }
                catch { }
                if (crash && !_prevDisabled)
                    _crashMarked = true;
                if (dead && _prevAlive)
                    _crashMarked = true;
                _prevDisabled = crash;
                _prevAlive = !dead;
                _hadLocalPlayer = true;
            }
            else
            {
                if (_hadLocalPlayer && _hadAirborne)
                    left = true;
                _prevDisabled = false;
                _prevAlive = true;
            }

            FlightRecordGateService.AutoScorePath path = FlightRecordGateService.ResolveAutoScore(
                _autoScoreOpened, _sampleCount, ac != null, crash, dead, left, _hadAirborne);
            if (path == FlightRecordGateService.AutoScorePath.MarkHadPlayer)
            {
                if (ac != null)
                    _hadLocalPlayer = true;
                return;
            }
            if (path != FlightRecordGateService.AutoScorePath.Trigger)
                return;

            TriggerAutoScoreOpen();
        }

        private static void TriggerAutoScoreOpen()
        {
            if (_autoScoreOpened)
                return;
            _autoScoreOpened = true;
            try
            {
                PrepareDisplayScore();
                if (_lastAnalysis == null && _sampleCount >= 4)
                {
                    AnalysisResult r = BuildAnalysis();
                    _lastAnalysis = r;
                    _lastAnalysisTicks = _sessionTicks;
                }
                if (_lastAnalysis == null)
                    return;
                TryOpenF1FlightScore();
            }
            catch { }
        }

        internal static void DrawGui()
        {
            if (!OwnsRecording)
                return;
            EnsureStyles();
            if (ShowRecIndicator != null && ShowRecIndicator.Value && _recording && OwnsRecording
                && OritasyAllowsThirdPersonUi())
            {
                Event e = Event.current;
                if (e != null && e.type == EventType.Repaint)
                {
                    Color prev = GUI.color;
                    bool blink = ((int)(Time.unscaledTime * 2f) % 2) == 0;
                    GUI.color = blink ? new Color(1f, 0.15f, 0.15f, 0.95f) : new Color(0.7f, 0.1f, 0.1f, 0.7f);
                    GUI.Label(new Rect(12f, 36f, 260f, 28f),
                        "FLT REC  " + (Time.unscaledTime - _sessionStartUnscaled).ToString("0.0", Inv) + "s",
                        _recStyle);
                    GUI.color = prev;
                }
            }
            // Flight Score UI is F1-only (no standalone panel).
        }

        /// <summary>Oritasy Profile → Experimental: master switch for sampling + report write.</summary>
        internal static void DrawProfileToggle()
        {
            if (Enabled == null)
                return;

            GUILayout.Label(ModUiLang.T("Flight analysis", "飞行分析"), GUILayout.ExpandWidth(true));
            GUILayout.BeginHorizontal();
            GUILayout.Label(ModUiLang.T("Record + report", "记录与报告"), GUILayout.Width(140f));
            Color prev = GUI.backgroundColor;
            bool on = Enabled.Value;
            GUI.backgroundColor = on ? new Color(0.45f, 0.85f, 0.55f) : Color.white;
            if (GUILayout.Button(on ? ModUiLang.T("ON", "开") : ModUiLang.T("OFF", "关"),
                GUILayout.Width(90f), GUILayout.Height(26f)))
            {
                Enabled.Value = !on;
                on = !on;
                if (!on && _recording)
                    StopRecording(false);
            }
            GUI.backgroundColor = prev;
            GUILayout.Label(on ? ModUiLang.T("  [ON]", "  [开]") : ModUiLang.T("  [OFF]", "  [关]"),
                GUILayout.Width(56f));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label(
                on
                    ? ModUiLang.T(
                        "ON: samples the local aircraft and writes plugins/OritasyReplays/ on session end. Costs main-thread time while flying.",
                        "开：采样本机飞行，结束时写入 plugins/OritasyReplays/。飞行中会占用主线程。")
                    : ModUiLang.T(
                        "OFF (default): no sampling, no analysis report, no flight-score XP multiplier.",
                        "关（默认）：不采样、不写分析报告、无飞行评分 XP 倍率。"),
                GUILayout.ExpandWidth(true));
        }

        /// <summary>Respect Oritasy F1 “Third-person / overlay UI” master switch (REC included).</summary>
        private static bool OritasyAllowsThirdPersonUi()
        {
            try
            {
                Type t = Type.GetType("Oritasy.Plugin, Oritasy") ?? Type.GetType("Oritasy.Plugin");
                if (t == null)
                    return true;
                PropertyInfo p = t.GetProperty("AllowThirdPersonUi",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (p == null)
                    return true;
                return (bool)p.GetValue(null, null);
            }
            catch { return true; }
        }

        private static Type ResolveF1GuiType()
        {
            try
            {
                return Type.GetType("Oritasy.AircraftManeuverGui")
                    ?? Type.GetType("Oritasy.AircraftManeuverGui, Oritasy");
            }
            catch { return null; }
        }

        private static bool F1GuiIsOpen()
        {
            try
            {
                Type t = ResolveF1GuiType();
                if (t == null)
                    return false;
                PropertyInfo p = t.GetProperty("IsOpen", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (p == null)
                    return false;
                return (bool)p.GetValue(null, null);
            }
            catch { return false; }
        }

        private static bool F1GuiIsFlightScoreTab()
        {
            try
            {
                Type t = ResolveF1GuiType();
                if (t == null)
                    return false;
                PropertyInfo p = t.GetProperty("IsFlightScoreTab",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (p == null)
                    return false;
                return (bool)p.GetValue(null, null);
            }
            catch { return false; }
        }

        private static bool TryOpenF1FlightScore()
        {
            try
            {
                Type t = ResolveF1GuiType();
                if (t == null)
                    return false;
                MethodInfo m = t.GetMethod("OpenToFlightScore",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (m == null)
                    return false;
                m.Invoke(null, null);
                return true;
            }
            catch { return false; }
        }

        private static void ApplyCjkFont(GUIStyle style)
        {
            if (style == null)
                return;
            try
            {
                Type t = Type.GetType("Oritasy.ChineseFontPatch")
                    ?? Type.GetType("Oritasy.ChineseFontPatch, Oritasy");
                if (t == null)
                    return;
                MethodInfo m = t.GetMethod("ApplyTo",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new Type[] { typeof(GUIStyle) }, null);
                if (m != null)
                    m.Invoke(null, new object[] { style });
            }
            catch { }
        }

        internal static void Shutdown()
        {
            if (_recording)
                StopRecording(false);
        }

        private static void ResolvePlayer()
        {
            Aircraft ac = null;
            try
            {
                if (GameManager.GetLocalAircraft(out ac) && ac != null)
                {
                    _cachedPlayer = ac;
                    return;
                }
            }
            catch { }
            _cachedPlayer = null;
        }

        private static void StartRecording(long startTicks)
        {
            if (_recording)
                return;
            if (startTicks == 0)
                startTicks = DateTime.UtcNow.Ticks;
            ResetAccumulators();
            _sessionTicks = startTicks;
            _csvPath = CsvPathFor(startTicks);
            _analysisPath = AnalysisPathFor(startTicks);
            _sessionStartUnscaled = Time.unscaledTime;
            _unitName = "";
            ResolvePlayer();
            if (_cachedPlayer != null)
            {
                try { _unitName = _cachedPlayer.unitName ?? ""; }
                catch { }
            }

            try
            {
                _csv = new StreamWriter(_csvPath, false, Encoding.UTF8);
                _csv.WriteLine("t_s,unit,pos_x,pos_y,pos_z,spd,pitch,roll,yaw,throttle,radar_alt,gforce,marker");
                _csv.Flush();
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("FlightAnalysis open failed: " + ex.Message);
                _csv = null;
                return;
            }

            _recording = true;
            _nextSampleAt = 0f;
            _nextFlushAt = Time.unscaledTime + 0.5f;
            _buf.Length = 0;
            _hasPrevPos = false;
            _hasPrevInputs = false;
            _prevLanded = true;
            _prevAirborne = false;
            _hadAirborne = false;
            _hadLocalPlayer = _cachedPlayer != null;
            _autoScoreOpened = false;
            _prevDisabled = false;
            _prevAlive = true;
            FlightTrackMap.BeginLivePath();
            SnapshotAirframeBaseline(_cachedPlayer);

            if (Plugin.Log != null)
                Plugin.Log.LogInfo("FlightAnalysis: recording sortie " + startTicks);
        }

        private static void StopRecording(bool analyze)
        {
            if (!_recording)
                return;
            MaybeFlush(float.MaxValue);
            _recording = false;
            try
            {
                if (_csv != null)
                {
                    _csv.Flush();
                    _csv.Close();
                }
            }
            catch { }
            _csv = null;

            if (FlightRecordGateService.ShouldAnalyzeOnStop(analyze, AutoAnalyzeOnStop == null || AutoAnalyzeOnStop.Value, _sampleCount))
            {
                AnalysisResult r = BuildAnalysis();
                WriteAnalysis(r);
                _lastAnalysis = r;
                _lastAnalysisTicks = _sessionTicks;
                FlightTrackMap.CommitSortie(r, _sessionTicks);
            }
            else
            {
                FlightTrackMap.DiscardLivePath();
            }
        }

        private static void TryDelete(long ticks)
        {
            try
            {
                string c = CsvPathFor(ticks);
                string a = AnalysisPathFor(ticks);
                if (!string.IsNullOrEmpty(c) && File.Exists(c)) File.Delete(c);
                if (!string.IsNullOrEmpty(a) && File.Exists(a)) File.Delete(a);
            }
            catch { }
        }

        private static string CsvPathFor(long ticks)
        {
            if (string.IsNullOrEmpty(_dir) || ticks == 0)
                return null;
            return Path.Combine(_dir, "s" + ticks.ToString(Inv) + "_flight.csv");
        }

        private static string AnalysisPathFor(long ticks)
        {
            if (string.IsNullOrEmpty(_dir) || ticks == 0)
                return null;
            return Path.Combine(_dir, "s" + ticks.ToString(Inv) + "_analysis.txt");
        }

        private static void MaybeFlush(float now)
        {
            if (_csv == null || _buf.Length == 0)
                return;
            // Buffer longer on LowEnd — fewer disk writes (score accuracy unchanged).
            float flushEvery = 1.25f;
            int softCap = 32000;
            int hardCap = 64000;
            try
            {
                Type perf = Type.GetType("Oritasy.PerfMode");
                if (perf != null)
                {
                    System.Reflection.PropertyInfo isLow = perf.GetProperty("IsLow",
                        System.Reflection.BindingFlags.Static
                        | System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.NonPublic);
                    if (isLow != null && isLow.GetValue(null, null) is bool && (bool)isLow.GetValue(null, null))
                    {
                        flushEvery = 2.5f;
                        softCap = 64000;
                        hardCap = 120000;
                    }
                }
            }
            catch { }
            if (now < _nextFlushAt && now < float.MaxValue * 0.5f && _buf.Length < softCap)
                return;
            _nextFlushAt = now + flushEvery;
            try
            {
                _csv.Write(_buf.ToString());
                if (_buf.Length > hardCap || now >= float.MaxValue * 0.5f)
                    _csv.Flush();
                _buf.Length = 0;
            }
            catch { }
        }

        private static void Sample(Aircraft ac)
        {
            Transform t = null;
            try { t = ac.transform; }
            catch { return; }
            if (t == null)
                return;

            Vector3 pos = t.position;
            Vector3 eul = t.eulerAngles;
            Vector3 vel = Vector3.zero;
            try
            {
                if (ac.rb != null)
                    vel = ac.rb.velocity;
            }
            catch { }

            float spd = 0f;
            try { spd = ac.speed; }
            catch { spd = vel.magnitude; }

            float pitchDeg = -Mathf.DeltaAngle(0f, eul.x);
            float rollDeg = Mathf.DeltaAngle(0f, eul.z);

            float ralt = 0f;
            try { ralt = ac.radarAlt; }
            catch { ralt = pos.y; }

            float pitchIn = 0f, rollIn = 0f, yawIn = 0f, thr = 0f;
            try
            {
                ControlInputs ci = ac.GetInputs();
                if (ci != null)
                {
                    pitchIn = ci.pitch;
                    rollIn = ci.roll;
                    yawIn = ci.yaw;
                    thr = ci.throttle;
                }
            }
            catch { }

            bool landed = false;
            bool airborne = false;
            try { landed = ac.IsLanded(); }
            catch { landed = ralt < 5f && spd < 25f; }
            try { airborne = !landed && ralt > 0.2f; }
            catch { airborne = !landed; }

            float g = 0f;
            try { g = ac.gForce; }
            catch { }

            string marker = "";
            if (FlightSampleMathService.ShouldMarkTakeoff(_prevAirborne, airborne))
            {
                marker = "TAKEOFF";
                _takeoffMarked = true;
                _hadAirborne = true;
            }
            if (FlightSampleMathService.ShouldMarkLanding(_prevAirborne, landed, _hadAirborne))
            {
                marker = string.IsNullOrEmpty(marker) ? "LANDING" : (marker + "|LANDING");
                _landingMarked = true;
                _landSink = Mathf.Abs(vel.y);
                _landSpd = spd;
                _landAgl = ralt;
                _landQualityValid = true;
                TryGrantCleanLandingXp(ac);
            }
            bool disabled = false;
            try { disabled = ac.disabled; }
            catch { }
            if (disabled && !_crashMarked)
            {
                marker = string.IsNullOrEmpty(marker) ? "CRASH" : (marker + "|CRASH");
                _crashMarked = true;
                _airframeCompromised = true;
            }

            float tSec = Time.unscaledTime - _sessionStartUnscaled;
            if (_hasPrevPos)
            {
                float d = Vector3.Distance(pos, _prevPos);
                if (d < 500f)
                    _distSum += d;
            }
            _prevPos = pos;
            _hasPrevPos = true;

            // Map path must use GlobalPosition — scene local drifts with floating origin.
            try
            {
                GlobalPosition gp = ac.GlobalPosition();
                FlightTrackMap.SampleLive(gp.x, gp.z);
            }
            catch
            {
                try
                {
                    GlobalPosition gp2 = pos.ToGlobalPosition();
                    FlightTrackMap.SampleLive(gp2.x, gp2.z);
                }
                catch
                {
                    FlightTrackMap.SampleLive(pos);
                }
            }

            if (Time.unscaledTime >= _nextIntegrityAt)
            {
                _nextIntegrityAt = Time.unscaledTime + 1f;
                if (!_airframeCompromised && AirframeDamagedSinceBaseline(ac))
                    _airframeCompromised = true;
            }

            if (_hasPrevInputs)
            {
                float dp = pitchIn - _prevPitchIn;
                float dr = rollIn - _prevRollIn;
                float dy = yawIn - _prevYawIn;
                _jitterSumSq += (double)(dp * dp + dr * dr + dy * dy);
                _jitterSamples++;
            }
            _prevPitchIn = pitchIn;
            _prevRollIn = rollIn;
            _prevYawIn = yawIn;
            _hasPrevInputs = true;

            _sampleCount++;
            _spdSum += spd;
            if (spd > _maxSpd) _maxSpd = spd;
            if (ralt > _maxAlt) _maxAlt = ralt;
            if (_sampleCount == 1 || ralt < _minAlt) _minAlt = ralt;
            float absG = Mathf.Abs(g);
            if (absG > _maxAbsG) _maxAbsG = absG;
            _pitchSumSq += pitchIn * pitchIn;
            _rollSumSq += rollIn * rollIn;
            _yawSumSq += yawIn * yawIn;
            float thrAbs = Mathf.Clamp01(thr);
            _thrSum += thrAbs;
            if (FlightSampleMathService.IsHighDeflection(pitchIn, rollIn, yawIn))
                _highDeflCount++;
            if (FlightSampleMathService.IsFullThrottle(thrAbs)) _fullThrCount++;
            if (FlightSampleMathService.IsIdleThrottle(thrAbs)) _idleThrCount++;
            float bankAbs = Mathf.Abs(rollDeg);
            if (bankAbs > _maxBankAbs) _maxBankAbs = bankAbs;
            if (Mathf.Abs(pitchDeg) > _maxPitchAbs) _maxPitchAbs = Mathf.Abs(pitchDeg);
            if (pitchDeg < _minPitch) _minPitch = pitchDeg;
            if (FlightSampleMathService.IsInverted(bankAbs)) _invertedCount++;

            // Maneuver samples (airborne only).
            if (airborne)
            {
                _airborneCount++;
                if (FlightSampleMathService.IsNoe(true, ralt, spd))
                    _noeCount++;
                if (FlightSampleMathService.IsHighGTurn(true, absG, bankAbs))
                    _highGTurnCount++;
            }

            _prevLanded = landed;
            _prevAirborne = airborne;

            _buf.Append(tSec.ToString("0.###", Inv)).Append(',')
                .Append(Esc(_unitName)).Append(',')
                .Append(pos.x.ToString("0.###", Inv)).Append(',')
                .Append(pos.y.ToString("0.###", Inv)).Append(',')
                .Append(pos.z.ToString("0.###", Inv)).Append(',')
                .Append(spd.ToString("0.##", Inv)).Append(',')
                .Append(pitchIn.ToString("0.###", Inv)).Append(',')
                .Append(rollIn.ToString("0.###", Inv)).Append(',')
                .Append(yawIn.ToString("0.###", Inv)).Append(',')
                .Append(thr.ToString("0.###", Inv)).Append(',')
                .Append(ralt.ToString("0.##", Inv)).Append(',')
                .Append(g.ToString("0.##", Inv)).Append(',')
                .Append(Esc(marker))
                .Append('\n');
        }

        private static void ResetAccumulators()
        {
            _sampleCount = 0;
            _distSum = 0;
            _spdSum = 0;
            _maxSpd = 0f;
            _maxAlt = 0f;
            _minAlt = 0f;
            _maxAbsG = 0f;
            _pitchSumSq = _rollSumSq = _yawSumSq = 0;
            _thrSum = 0;
            _highDeflCount = _fullThrCount = _idleThrCount = 0;
            _invertedCount = 0;
            _noeCount = 0;
            _highGTurnCount = 0;
            _airborneCount = 0;
            _maxBankAbs = _maxPitchAbs = 0f;
            _minPitch = 0f;
            _jitterSumSq = 0;
            _jitterSamples = 0;
            _weaponFires = 0;
            _lastWeapon = "";
            _takeoffMarked = _landingMarked = _crashMarked = false;
            _landSink = _landSpd = _landAgl = 0f;
            _landQualityValid = false;
            _airframeCompromised = false;
            _nextIntegrityAt = 0f;
            _cleanLandingXpCount = 0;
            PartHpBaseline.Clear();
        }

        private static void SnapshotAirframeBaseline(Aircraft ac)
        {
            PartHpBaseline.Clear();
            _airframeCompromised = false;
            _nextIntegrityAt = 0f;
            if (ac == null)
                return;
            try
            {
                if (ac.disabled)
                {
                    _airframeCompromised = true;
                    return;
                }
            }
            catch { }
            try
            {
                UnitPart[] parts = ac.GetComponentsInChildren<UnitPart>(true);
                if (parts == null)
                    return;
                for (int i = 0; i < parts.Length; i++)
                {
                    UnitPart p = parts[i];
                    if (p == null)
                        continue;
                    int id;
                    try { id = p.GetInstanceID(); }
                    catch { continue; }
                    if (id == 0)
                        continue;
                    float hp = 0f;
                    try { hp = p.hitPoints; }
                    catch { continue; }
                    if (hp > 0.01f)
                        PartHpBaseline[id] = hp;
                    try
                    {
                        if (p.IsDetached())
                            _airframeCompromised = true;
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static bool AirframeDamagedSinceBaseline(Aircraft ac)
        {
            if (ac == null)
                return true;
            try
            {
                if (ac.disabled)
                    return true;
            }
            catch { return true; }
            try
            {
                UnitPart[] parts = ac.GetComponentsInChildren<UnitPart>(true);
                if (parts == null)
                    return false;
                for (int i = 0; i < parts.Length; i++)
                {
                    UnitPart p = parts[i];
                    if (p == null)
                        continue;
                    try
                    {
                        if (p.IsDetached())
                            return true;
                    }
                    catch { }
                    int id;
                    try { id = p.GetInstanceID(); }
                    catch { continue; }
                    float hp = 0f;
                    try { hp = p.hitPoints; }
                    catch { continue; }
                    float baseHp;
                    if (PartHpBaseline.TryGetValue(id, out baseHp) && baseHp > 1f
                        && hp < baseHp * 0.995f)
                        return true;
                    if (hp <= 0.01f && baseHp > 1f)
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static void TryGrantCleanLandingXp(Aircraft ac)
        {
            if (_crashMarked || _airframeCompromised)
                return;
            if (AirframeDamagedSinceBaseline(ac))
            {
                _airframeCompromised = true;
                return;
            }
            try
            {
                if (ac != null && ac.disabled)
                {
                    _airframeCompromised = true;
                    return;
                }
            }
            catch
            {
                return;
            }
            if (!PlayerCareer.TryGrantBonusXp(CleanLandingXp))
                return;
            _cleanLandingXpCount++;
            if (Plugin.Log != null)
                Plugin.Log.LogInfo("FlightAnalysis: clean landing +" + CleanLandingXp
                    + " XP (total this sortie " + (_cleanLandingXpCount * CleanLandingXp) + ")");
        }

        private static AnalysisResult BuildAnalysis()
        {
            AnalysisResult r = new AnalysisResult();
            r.UnitName = _unitName;
            r.CsvPath = _csvPath;
            r.SessionTicks = _sessionTicks;
            float wall = Time.unscaledTime - _sessionStartUnscaled;
            r.DurationSec = wall > 0.01f ? wall : 0f;
            r.DistanceM = (float)_distSum;
            r.MaxSpeed = _maxSpd;
            r.AvgSpeed = _sampleCount > 0 ? (float)(_spdSum / _sampleCount) : 0f;
            r.MaxAlt = _maxAlt;
            r.MinAlt = _minAlt;
            r.MaxAbsG = _maxAbsG;
            int n = Mathf.Max(1, _sampleCount);
            r.PitchRms = FlightSampleMathService.Rms(_pitchSumSq, n);
            r.RollRms = FlightSampleMathService.Rms(_rollSumSq, n);
            r.YawRms = FlightSampleMathService.Rms(_yawSumSq, n);
            r.HighDeflPct = FlightSampleMathService.Pct(_highDeflCount, n);
            r.AvgThrottle = (float)(_thrSum / n);
            r.FullThrPct = FlightSampleMathService.Pct(_fullThrCount, n);
            r.IdleThrPct = FlightSampleMathService.Pct(_idleThrCount, n);
            r.MaxBank = _maxBankAbs;
            r.MaxPitch = _maxPitchAbs;
            r.MinPitch = _minPitch;
            r.InvertedPct = FlightSampleMathService.Pct(_invertedCount, n);
            int airN = Mathf.Max(1, _airborneCount);
            r.NoePct = FlightSampleMathService.Pct(_noeCount, airN);
            r.HighGTurnPct = FlightSampleMathService.Pct(_highGTurnCount, airN);
            r.Smoothness = FlightSampleMathService.SmoothnessFromJitter(_jitterSumSq, _jitterSamples);
            r.LandingValid = _landQualityValid;
            r.LandSink = _landSink;
            r.LandSpd = _landSpd;
            r.LandAgl = _landAgl;
            // EN stored for disk .txt; UI localizes via LandingNoteText().
            r.LandingNote = FlightSampleMathService.LandingNoteEn(
                FlightSampleMathService.ClassifyLanding(r.LandingValid, r.LandSink, r.LandSpd));
            r.WeaponFires = _weaponFires;
            r.LastWeapon = _lastWeapon;
            r.Takeoff = _takeoffMarked;
            r.Landing = _landingMarked;
            r.Crash = _crashMarked;
            r.CleanLandingXp = _cleanLandingXpCount * CleanLandingXp;
            ScoreFlight(r);
            return r;
        }

        private static void ScoreFlight(AnalysisResult r)
        {
            FlightScoreMathService.ScoreFlight(r);
        }

        private static string LandingNoteText(AnalysisResult r, bool zh)
        {
            if (r == null)
                return FlightSampleMathService.LandingNoteLocalized(
                    FlightSampleMathService.LandingGrade.None, zh);
            return FlightSampleMathService.LandingNoteLocalized(
                FlightSampleMathService.ClassifyLanding(r.LandingValid, r.LandSink, r.LandSpd), zh);
        }

        private static string TipsText(AnalysisResult r, bool zh)
        {
            if (r == null)
                return "";
            if (!zh)
                return r.Tips ?? "";
            StringBuilder tips = new StringBuilder();
            if (r.Smoothness < 55f)
                tips.Append("平滑杆量，减少高频抖动。");
            if (r.HighDeflPct > 45f)
                tips.Append("减少大偏转时间，提前做小修正。");
            if (r.LandingValid && r.LandSink >= 5f)
                tips.Append("更早拉平，降低接地前下沉率。");
            if (r.Crash)
                tips.Append("机体已失效或严重受损。");
            if (r.NoePct < 5f && r.DurationSec >= 60f && !r.Crash)
                tips.Append("可尝试贴地高速飞行以获取机动加分。");
            if (tips.Length == 0)
                tips.Append("飞行扎实，继续练习航线与柔和着陆。");
            return tips.ToString();
        }

        private static string ManeuverNotesText(AnalysisResult r, bool zh)
        {
            if (r == null)
                return "";
            if (!zh)
                return r.ManeuverNotes ?? "";
            if (r.ManeuverBonus <= 0)
                return "无机动加分";
            StringBuilder sb = new StringBuilder();
            if (r.NoePct >= 8f)
                sb.Append(string.Format(Inv, "贴地飞行 +{0}。",
                    Mathf.Clamp(Mathf.RoundToInt(r.NoePct * 0.35f), 0, 12)));
            if (r.HighGTurnPct >= 4f)
                sb.Append(string.Format(Inv, "持续高过载转弯 +{0}。",
                    Mathf.Clamp(Mathf.RoundToInt(r.HighGTurnPct * 0.4f), 0, 10)));
            if (!r.Crash && r.InvertedPct >= 2f && r.InvertedPct <= 35f)
                sb.Append(string.Format(Inv, "倒飞 +{0}。",
                    Mathf.Clamp(Mathf.RoundToInt(r.InvertedPct * 0.25f), 0, 6)));
            if (r.LandingValid && r.LandSink < 2.5f && r.LandSpd < 80f)
                sb.Append("柔和着陆 +3。");
            if (r.Smoothness >= 75f && r.DurationSec >= 45f)
                sb.Append("杆量平滑 +2。");
            return sb.Length > 0 ? sb.ToString() : "无机动加分";
        }

        private static void WriteAnalysis(AnalysisResult r)
        {
            if (r == null || string.IsNullOrEmpty(_analysisPath))
                return;
            try
            {
                StringBuilder sb = new StringBuilder(2048);
                sb.AppendLine("Oritasy Flight Analysis");
                sb.AppendLine("=======================");
                sb.AppendLine("Unit: " + (r.UnitName ?? ""));
                sb.AppendLine("CSV:  " + (r.CsvPath ?? ""));
                sb.AppendLine("Grade: " + r.Grade + "  (" + r.Score + "/100)");
                sb.AppendLine(string.Format(Inv, "XP multiplier: ×{0:0.00} (flight score → match XP, max {1:0.0})",
                    XpMultiplierForScore(r.Score), HardXpMulCap));
                sb.AppendLine();
                sb.AppendLine(string.Format(Inv, "Duration:     {0:0.0} s", r.DurationSec));
                sb.AppendLine(string.Format(Inv, "Distance:     {0:0} m  ({1:0.00} km)", r.DistanceM, r.DistanceM / 1000f));
                sb.AppendLine(string.Format(Inv, "Speed max/avg:{0:0.0} / {1:0.0} m/s", r.MaxSpeed, r.AvgSpeed));
                sb.AppendLine(string.Format(Inv, "Alt max/min:  {0:0} / {1:0} m (radar)", r.MaxAlt, r.MinAlt));
                sb.AppendLine(string.Format(Inv, "Max |G|:      {0:0.00}", r.MaxAbsG));
                sb.AppendLine();
                sb.AppendLine(string.Format(Inv, "Stick RMS  P/R/Y: {0:0.000} / {1:0.000} / {2:0.000}",
                    r.PitchRms, r.RollRms, r.YawRms));
                sb.AppendLine(string.Format(Inv, "High deflection: {0:0.0}%", r.HighDeflPct));
                sb.AppendLine(string.Format(Inv, "Smoothness:      {0:0.0} / 100", r.Smoothness));
                sb.AppendLine();
                sb.AppendLine(string.Format(Inv, "Throttle avg: {0:0.00}  full {1:0.0}%  idle {2:0.0}%",
                    r.AvgThrottle, r.FullThrPct, r.IdleThrPct));
                sb.AppendLine(string.Format(Inv, "Bank max: {0:0.0} deg   Pitch max/min: {1:0.0} / {2:0.0}",
                    r.MaxBank, r.MaxPitch, r.MinPitch));
                sb.AppendLine(string.Format(Inv, "Time inverted (|roll|>90): {0:0.0}%", r.InvertedPct));
                sb.AppendLine(string.Format(Inv, "NOE / terrain hug: {0:0.0}% airborne", r.NoePct));
                sb.AppendLine(string.Format(Inv, "High-G turns:      {0:0.0}% airborne", r.HighGTurnPct));
                sb.AppendLine(string.Format(Inv, "Maneuver bonus:    +{0} / 25", r.ManeuverBonus));
                sb.AppendLine("Maneuver notes: " + (r.ManeuverNotes ?? ""));
                sb.AppendLine();
                sb.AppendLine("Markers: takeoff=" + r.Takeoff
                    + " landing=" + r.Landing
                    + " crash=" + r.Crash);
                sb.AppendLine(string.Format(Inv, "Weapon fires: {0}  last={1}",
                    r.WeaponFires, r.LastWeapon ?? ""));
                sb.AppendLine();
                if (r.LandingValid)
                {
                    sb.AppendLine(string.Format(Inv,
                        "Landing: sink={0:0.00} m/s  speed={1:0.0} m/s  AGL={2:0.00} m",
                        r.LandSink, r.LandSpd, r.LandAgl));
                    sb.AppendLine("Landing note: " + r.LandingNote);
                }
                else
                    sb.AppendLine("Landing: (none detected)");
                sb.AppendLine();
                sb.AppendLine("Tips: " + r.Tips);
                string payload = sb.ToString();
                string outPath = _analysisPath;
#if ORITASY_COMBINED
                bool queued = Oritasy.OritasyWorker.TryEnqueue(
                    delegate { File.WriteAllText(outPath, payload, Encoding.UTF8); },
                    delegate
                    {
                        if (Plugin.Log != null)
                            Plugin.Log.LogInfo("FlightAnalysis: wrote " + outPath);
                    });
                if (!queued)
                {
                    File.WriteAllText(outPath, payload, Encoding.UTF8);
                    if (Plugin.Log != null)
                        Plugin.Log.LogInfo("FlightAnalysis: wrote " + outPath);
                }
#else
                File.WriteAllText(outPath, payload, Encoding.UTF8);
                if (Plugin.Log != null)
                    Plugin.Log.LogInfo("FlightAnalysis: wrote " + outPath);
#endif
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("FlightAnalysis write failed: " + ex.Message);
            }
        }

        private static void DrawScoreLabels(AnalysisResult r, GUIStyle title, GUIStyle label,
            GUIStyle section, bool zh)
        {
            if (r == null)
                return;
            GUILayout.Label(zh ? "评分结果" : "SCORE", section);
            GUILayout.Label(
                (zh ? "评级 " : "Grade ") + r.Grade + "   "
                + (zh ? "得分 " : "Score ") + r.Score + "/100",
                title);
            float xpMul = XpMultiplierForScore(r.Score);
            GUILayout.Label(FormatXpMulLabel(xpMul, zh), label);
            if (r.CleanLandingXp > 0)
            {
                GUILayout.Label(zh
                    ? ("完好着陆经验 +" + r.CleanLandingXp)
                    : ("Clean landing XP +" + r.CleanLandingXp), label);
            }
            if (!string.IsNullOrEmpty(r.UnitName))
                GUILayout.Label((zh ? "机型：" : "Unit: ") + r.UnitName, label);

            GUILayout.Space(6f);
            GUILayout.Label(zh ? "飞行数据" : "FLIGHT DATA", section);
            GUILayout.Label(string.Format(Inv,
                zh ? "时长 {0:0.0}秒 · 距离 {1:0}米 · 最大速度 {2:0.0} m/s"
                    : "Duration {0:0.0}s · Dist {1:0}m · MaxSpd {2:0.0} m/s",
                r.DurationSec, r.DistanceM, r.MaxSpeed), label);
            GUILayout.Label(string.Format(Inv,
                zh ? "高度 {0:0}/{1:0}米 · 最大|G| {2:0.00} · 平滑度 {3:0}"
                    : "Alt {0:0}/{1:0}m · Max|G| {2:0.00} · Smooth {3:0}",
                r.MaxAlt, r.MinAlt, r.MaxAbsG, r.Smoothness), label);
            GUILayout.Label(string.Format(Inv,
                zh ? "杆量 RMS P/R/Y {0:0.00}/{1:0.00}/{2:0.00} · 大偏转 {3:0}%"
                    : "Stick RMS P/R/Y {0:0.00}/{1:0.00}/{2:0.00} · HighDefl {3:0}%",
                r.PitchRms, r.RollRms, r.YawRms, r.HighDeflPct), label);

            GUILayout.Space(6f);
            GUILayout.Label(zh ? "着陆" : "LANDING", section);
            if (r.LandingValid)
            {
                GUILayout.Label(string.Format(Inv,
                    zh ? "下沉率 {0:0.00} m/s · 速度 {1:0.0} — {2}"
                        : "Sink {0:0.00} m/s · Spd {1:0.0} — {2}",
                    r.LandSink, r.LandSpd, LandingNoteText(r, zh)), label);
            }
            else
                GUILayout.Label(zh ? "未检测到着陆" : "Landing: none detected", label);

            GUILayout.Space(6f);
            GUILayout.Label(zh ? "机动加分" : "MANEUVER BONUS", section);
            GUILayout.Label(string.Format(Inv,
                zh ? "合计 +{0} · 贴地 {1:0}% · 高过载转弯 {2:0}% · 倒飞 {3:0}%"
                    : "Total +{0} · NOE {1:0}% · High-G turn {2:0}% · Inverted {3:0}%",
                r.ManeuverBonus, r.NoePct, r.HighGTurnPct, r.InvertedPct), label);
            GUILayout.Label(ManeuverNotesText(r, zh), label);

            GUILayout.Label(
                (zh ? "开火：" : "Fires: ") + r.WeaponFires
                + (string.IsNullOrEmpty(r.LastWeapon) ? "" : ((zh ? "  最后 " : "  last ") + r.LastWeapon)),
                label);

            if (r.Crash)
                GUILayout.Label(zh ? "标记：坠毁 / 机体失效" : "Marker: crash / aircraft disabled", label);

            GUILayout.Space(6f);
            GUILayout.Label(zh ? "建议" : "TIPS", section);
            GUILayout.Label(TipsText(r, zh), label);
            if (!string.IsNullOrEmpty(r.CsvPath))
                GUILayout.Label((zh ? "文件：" : "File: ") + Path.GetFileName(r.CsvPath), label);
        }

        private static void EnsureStyles()
        {
            if (_stylesReady && _recStyle != null)
            {
                ApplyCjkFont(_titleStyle);
                ApplyCjkFont(_bodyStyle);
                ApplyCjkFont(_sectionStyle);
                ApplyCjkFont(_btnStyle);
                ApplyCjkFont(_recStyle);
                return;
            }

            _recStyle = new GUIStyle(GUI.skin.label);
            _recStyle.fontSize = 16;
            _recStyle.fontStyle = FontStyle.Bold;
            _recStyle.normal.textColor = Color.white;

            // Match F1 Oritasy System chrome.
            _titleStyle = new GUIStyle(GUI.skin.label);
            _titleStyle.fontSize = 18;
            _titleStyle.fontStyle = FontStyle.Bold;
            _titleStyle.alignment = TextAnchor.MiddleLeft;
            _titleStyle.normal.textColor = new Color(0.9f, 0.95f, 0.55f, 1f);
            _titleStyle.wordWrap = true;

            _bodyStyle = new GUIStyle(GUI.skin.label);
            _bodyStyle.fontSize = 13;
            _bodyStyle.alignment = TextAnchor.MiddleLeft;
            _bodyStyle.normal.textColor = new Color(0.85f, 0.9f, 0.95f, 0.95f);
            _bodyStyle.wordWrap = true;

            _sectionStyle = new GUIStyle(GUI.skin.label);
            _sectionStyle.fontSize = 13;
            _sectionStyle.fontStyle = FontStyle.Bold;
            _sectionStyle.alignment = TextAnchor.MiddleLeft;
            _sectionStyle.normal.textColor = new Color(0.55f, 0.9f, 0.7f, 1f);

            _btnStyle = new GUIStyle(GUI.skin.button);
            _btnStyle.fontSize = 13;
            _btnStyle.fontStyle = FontStyle.Bold;
            _btnStyle.alignment = TextAnchor.MiddleCenter;
            _btnStyle.normal.textColor = Color.white;

            _stylesReady = true;
            ApplyCjkFont(_titleStyle);
            ApplyCjkFont(_bodyStyle);
            ApplyCjkFont(_sectionStyle);
            ApplyCjkFont(_btnStyle);
            ApplyCjkFont(_recStyle);
        }

        private static string Esc(string s)
        {
            if (string.IsNullOrEmpty(s))
                return "";
            if (s.IndexOf(',') < 0 && s.IndexOf('"') < 0)
                return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }

    [HarmonyPatch(typeof(WeaponManager), "Fire")]
    internal static class FlightAnalysisFirePatch
    {
        [HarmonyPostfix]
        private static void Postfix(WeaponManager __instance)
        {
            try
            {
                Aircraft ac = null;
                FieldInfoAircraft(__instance, out ac);
                if (ac == null || !GameManager.IsLocalAircraft(ac))
                    return;
                string name = "WEAPON";
                try
                {
                    if (__instance.currentWeaponStation != null
                        && __instance.currentWeaponStation.WeaponInfo != null)
                    {
                        WeaponInfo info = __instance.currentWeaponStation.WeaponInfo;
                        name = !string.IsNullOrEmpty(info.shortName)
                            ? info.shortName
                            : info.weaponName;
                        if (string.IsNullOrEmpty(name))
                            name = "WEAPON";
                    }
                }
                catch { }
                FlightAnalysis.NoteWeaponFire(name);
            }
            catch { }
        }

        private static void FieldInfoAircraft(WeaponManager wm, out Aircraft ac)
        {
            ac = null;
            try
            {
                FieldInfo fi = AccessTools.Field(typeof(WeaponManager), "aircraft");
                if (fi != null)
                    ac = fi.GetValue(wm) as Aircraft;
            }
            catch { }
            if (ac == null)
            {
                try { ac = wm.GetComponentInParent<Aircraft>(); }
                catch { }
            }
        }
    }

    [HarmonyPatch(typeof(WeaponManager), "FireGuns")]
    internal static class FlightAnalysisFireGunsPatch
    {
        [HarmonyPostfix]
        private static void Postfix(WeaponManager __instance)
        {
            try
            {
                Aircraft ac = null;
                try
                {
                    FieldInfo fi = AccessTools.Field(typeof(WeaponManager), "aircraft");
                    if (fi != null)
                        ac = fi.GetValue(__instance) as Aircraft;
                }
                catch { }
                if (ac == null)
                {
                    try { ac = __instance.GetComponentInParent<Aircraft>(); }
                    catch { }
                }
                if (ac == null || !GameManager.IsLocalAircraft(ac))
                    return;
                FlightAnalysis.NoteWeaponFire("GUNS");
            }
            catch { }
        }
    }
}
