using System;
using BepInEx.Configuration;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Low-end FPS profile: Auto/Low/Med/High + F1 LowEndMode toggle.
    /// Gates optional HUD cost; never touches LAND / F10 repair / sticky missile track.
    /// </summary>
    internal static class PerfMode
    {
        internal static ConfigEntry<string> Preset;
        /// <summary>F1 toggle — when true, force Low tier regardless of Preset.</summary>
        internal static ConfigEntry<bool> LowEndMode;
        internal static ConfigEntry<int> PerfRevision;

        private static bool _bound;
        private static int _cachedTier = -1;
        private static float _nextAutoAt;
        private static bool _autoLow;
        private static int _frame;

        /// <summary>0=High, 1=Med, 2=Low</summary>
        internal static int Tier
        {
            get
            {
                if (_cachedTier < 0)
                    RefreshTier(true);
                return _cachedTier;
            }
        }

        internal static bool IsLow
        {
            get { return Tier >= 2; }
        }

        internal static bool IsMedOrLower
        {
            get { return Tier >= 1; }
        }

        internal static string TierName
        {
            get
            {
                int t = Tier;
                if (t >= 2) return "Low";
                if (t == 1) return "Med";
                return "High";
            }
        }

        internal static void Bind(ConfigFile config)
        {
            if (config == null || _bound)
                return;
            _bound = true;

            Preset = config.Bind("Performance", "Preset", "Auto",
                "Performance preset: Auto, Low, Med, High. Auto picks Low on weak hardware. LowEndMode forces Low.");
            LowEndMode = config.Bind("Presentation", "LowEndMode", true,
                "Low-end performance mode (F1). Lighter ticks / engine quality. Radar MFD, missile camera, chase HUD stay on.");
            PerfRevision = config.Bind("Performance", "PerfRevision", 0,
                "Internal: raised when applying one-shot LowEnd defaults.");

            RefreshTier(true);
            ApplyOneShotDefaults();
            if (Plugin.Log != null)
            {
                Plugin.Log.LogInfo("PerfMode: Preset=" + (Preset != null ? Preset.Value : "?")
                    + " LowEndMode=" + (LowEndMode != null && LowEndMode.Value)
                    + " → " + TierName
                    + (Preset != null && IsAuto(Preset.Value) ? (" (autoLow=" + _autoLow + ")") : ""));
            }
        }

        internal static void Tick()
        {
            _frame++;
            if (Time.unscaledTime >= _nextAutoAt)
            {
                _nextAutoAt = Time.unscaledTime + 30f;
                RefreshTier(false);
            }
        }

        /// <summary>Skip non-critical work on Low: true every <paramref name="period"/> frames (staggered by slot).</summary>
        internal static bool AllowSlot(int slot, int period)
        {
            return PerfBudgetService.AllowSlot(IsLow, _frame, slot, period);
        }

        internal static void SetLowEndMode(bool on)
        {
            if (LowEndMode == null)
                return;
            if (LowEndMode.Value == on)
            {
                RefreshTier(true);
                return;
            }
            LowEndMode.Value = on;
            RefreshTier(true);
            if (on)
                ApplyRuntimeLowGates(false);
            EngineQualityService.ApplyNow();
        }

        /// <summary>Profile UI: Low / Med / High / Auto. Low also sets LowEndMode.</summary>
        internal static void SetPresetTier(string tier)
        {
            if (string.IsNullOrEmpty(tier))
                tier = "Auto";
            string t = tier.Trim();
            bool low = EqualsIgnore(t, "Low");
            if (Preset != null)
            {
                if (EqualsIgnore(t, "Med") || EqualsIgnore(t, "Medium"))
                    Preset.Value = "Med";
                else if (EqualsIgnore(t, "High"))
                    Preset.Value = "High";
                else if (low)
                    Preset.Value = "Low";
                else
                    Preset.Value = "Auto";
            }
            SetLowEndMode(low);
            RefreshTier(true);
        }

        /// <summary>Oritasy Profile → Performance (primary control). ZH via UiLang.</summary>
        internal static void DrawProfileSection()
        {
            if (Preset == null && LowEndMode == null)
                return;

            GUILayout.Label(UiLang.T("Performance", "性能"), GUILayout.ExpandWidth(true));
            GUILayout.Label(UiLang.T(
                "Low-end mode cuts vapor / OnGUI polish / engine quality. Radar MFD, missile camera, and chase HUD stay on.",
                "低配降低水汽 / OnGUI 抛光 / 引擎画质。雷达 MFD、导弹镜头、尾追 HUD 保持开启。"),
                GUILayout.ExpandWidth(true));
            GUILayout.Label(UiLang.T(
                "4 worker threads (disk / WAV). After a hitch, hunt/polish pause — HUD still draws every frame.",
                "4 条工作线程处理磁盘/WAV。卡顿后暂停寻敌与抛光，游戏内 HUD 仍每帧绘制，避免闪烁。"),
                GUILayout.ExpandWidth(true));

            string active = TierName;
            if (LowEndMode != null && LowEndMode.Value)
                active = "Low";
            else if (Preset != null && !string.IsNullOrEmpty(Preset.Value))
            {
                string p = Preset.Value.Trim();
                if (EqualsIgnore(p, "Auto"))
                    active = "Auto→" + TierName;
                else if (EqualsIgnore(p, "Med") || EqualsIgnore(p, "Medium"))
                    active = "Med";
                else if (EqualsIgnore(p, "High"))
                    active = "High";
                else if (EqualsIgnore(p, "Low"))
                    active = "Low";
            }

            GUILayout.BeginHorizontal();
            DrawTierButton("Low", "低", active);
            DrawTierButton("Med", "中", active);
            DrawTierButton("High", "高", active);
            DrawTierButton("Auto", "自动", active);
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();

            if (LowEndMode != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(UiLang.T("Low-end performance mode", "低配性能模式"), GUILayout.Width(180f));
                Color prev = GUI.backgroundColor;
                bool on = LowEndMode.Value;
                GUI.backgroundColor = on ? new Color(0.45f, 0.85f, 0.55f) : Color.white;
                if (GUILayout.Button(on ? UiLang.T("ON", "开") : UiLang.T("OFF", "关"),
                    GUILayout.Width(90f), GUILayout.Height(26f)))
                {
                    SetLowEndMode(!on);
                    if (!on && Preset != null)
                        Preset.Value = "Low";
                    else if (on && Preset != null && EqualsIgnore(Preset.Value, "Low"))
                        Preset.Value = "Med";
                }
                GUI.backgroundColor = prev;
                GUILayout.Label(on ? UiLang.T("  [ON]", "  [开]") : UiLang.T("  [OFF]", "  [关]"),
                    GUILayout.Width(56f));
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            }

            string presetVal = Preset != null ? Preset.Value : "Auto";
            string presetZh = presetVal;
            if (EqualsIgnore(presetVal, "Low")) presetZh = "低";
            else if (EqualsIgnore(presetVal, "Med") || EqualsIgnore(presetVal, "Medium")) presetZh = "中";
            else if (EqualsIgnore(presetVal, "High")) presetZh = "高";
            else if (EqualsIgnore(presetVal, "Auto")) presetZh = "自动";
            string tierZh = TierName;
            if (EqualsIgnore(TierName, "Low")) tierZh = "低";
            else if (EqualsIgnore(TierName, "Med") || EqualsIgnore(TierName, "Medium")) tierZh = "中";
            else if (EqualsIgnore(TierName, "High")) tierZh = "高";

            GUILayout.Label(UiLang.T(
                "Active: " + TierName
                + " · Preset " + presetVal
                + " · FlightAnalysis ≤" + FlightSampleHzCap().ToString("0.0") + " Hz",
                "当前: " + tierZh
                + " · 预设 " + presetZh
                + " · 飞行分析 ≤" + FlightSampleHzCap().ToString("0.0") + " Hz"),
                GUILayout.ExpandWidth(true));
            MotionInterpService.DrawProfileToggle();
            EngineQualityService.DrawProfileToggle();
        }

        private static void DrawTierButton(string en, string zh, string activeKey)
        {
            bool on = activeKey == en
                || (en == "Auto" && activeKey != null && activeKey.StartsWith("Auto", StringComparison.Ordinal));
            // When LowEnd forces Low, highlight Low even if Preset is Auto.
            if (en == "Low" && LowEndMode != null && LowEndMode.Value)
                on = true;
            if (en != "Low" && LowEndMode != null && LowEndMode.Value)
                on = false;

            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = on ? new Color(0.35f, 0.7f, 0.45f) : Color.white;
            string label = UiLang.T(en, zh);
            if (GUILayout.Button(label, GUILayout.Width(72f), GUILayout.Height(26f)))
                SetPresetTier(en);
            GUI.backgroundColor = prev;
        }

        internal static float FlightSampleHzCap()
        {
            return PerfBudgetService.FlightSampleHzCap(IsLow, IsMedOrLower);
        }

        internal static int RwrStrokeSegments(int want)
        {
            return PerfBudgetService.RwrStrokeSegments(want, IsLow, IsMedOrLower);
        }

        internal static int RadarRingSegments(int want)
        {
            return PerfBudgetService.RadarRingSegments(want, IsLow);
        }

        internal static int PersistCap()
        {
            return PerfBudgetService.PersistCap(IsLow);
        }

        internal static int ClutterCap()
        {
            return PerfBudgetService.ClutterCap(IsLow);
        }

        internal static float ClutterMul()
        {
            return PerfBudgetService.ClutterMul(IsLow);
        }

        internal static bool RadarAscopeAllowed()
        {
            return PerfBudgetService.RadarAscopeAllowed(IsLow);
        }

        internal static void MissileCamRtSize(bool manual, out int tw, out int th)
        {
            PerfBudgetService.MissileCamRtSize(manual, IsLow, IsMedOrLower, out tw, out th);
        }

        internal static void AircraftCamRtSize(out int tw, out int th)
        {
            PerfBudgetService.AircraftCamRtSize(IsLow, out tw, out th);
        }

        internal static float ChineseFontScanInterval()
        {
            return PerfBudgetService.ChineseFontScanInterval(IsLow);
        }

        internal static int ChineseFontOnEnableBudget()
        {
            return PerfBudgetService.ChineseFontOnEnableBudget(IsLow);
        }

        /// <summary>True when optional presentation HUD may early-out OnGUI work.</summary>
        internal static bool OptionalHudAllQuiet()
        {
            if (LowEndMode == null)
                return false;
            bool cam = Plugin.MissileCamera != null && Plugin.MissileCamera.Value;
            bool chase = Plugin.ShowAircraftChaseHud != null && Plugin.ShowAircraftChaseHud.Value;
            bool rwr = Plugin.ShowAircraftRwr != null && Plugin.ShowAircraftRwr.Value;
            bool g = Plugin.ShowGMeter != null && Plugin.ShowGMeter.Value;
            bool mfd = RadarMfdOverlay.Enabled != null && RadarMfdOverlay.Enabled.Value;
            bool brand = Plugin.ShowHudBrand != null && Plugin.ShowHudBrand.Value;
            bool ccip = RocketCcipHud.Enabled != null && RocketCcipHud.Enabled.Value
                && !RocketCcipHud.StandalonePresent;
            bool ils = AirportIlsHud.Enabled;
            return !(cam || chase || rwr || g || mfd || brand || ccip || ils);
        }

        private static void ApplyOneShotDefaults()
        {
            if (PerfRevision == null)
                return;
            try
            {
                if (PerfRevision.Value < 49)
                {
                    RefreshTier(true);
                    if (IsLow)
                        ApplyRuntimeLowGates(true);
                    PerfRevision.Value = 49;
                }
                // 51: Low-end used to force-off Radar MFD / missile camera / chase HUD. Restore them.
                if (PerfRevision.Value < 51)
                {
                    if (Plugin.MissileCamera != null)
                        Plugin.MissileCamera.Value = true;
                    if (Plugin.ShowAircraftChaseHud != null)
                        Plugin.ShowAircraftChaseHud.Value = true;
                    if (RadarMfdOverlay.Enabled != null)
                        RadarMfdOverlay.Enabled.Value = true;
                    PerfRevision.Value = 51;
                }
            }
            catch { }
        }

        /// <summary>
        /// Push Low defaults that do not disable core HUDs.
        /// Radar MFD / missile camera / chase HUD stay at the player's choice.
        /// </summary>
        private static void ApplyRuntimeLowGates(bool force)
        {
            try
            {
                if (RadarMfdOverlay.ClutterIntensity != null
                    && RadarMfdOverlay.ClutterIntensity.Value > 0.3f)
                    RadarMfdOverlay.ClutterIntensity.Value = 0.25f;
                if (RadarMfdOverlay.PersistenceSec != null
                    && RadarMfdOverlay.PersistenceSec.Value > 1.3f)
                    RadarMfdOverlay.PersistenceSec.Value = 1.2f;
                if (Plugin.EnhancedAirflow != null && (force || Plugin.EnhancedAirflow.Value))
                    Plugin.EnhancedAirflow.Value = false;
                FlightAnalysisBridge.ApplyLowSampleHz(4.5f);
            }
            catch { }
        }

        private static void RefreshTier(bool forceAutoDetect)
        {
            if (LowEndMode != null && LowEndMode.Value)
            {
                _cachedTier = 2;
                return;
            }

            string p = Preset != null ? Preset.Value : "Auto";
            if (string.IsNullOrEmpty(p))
                p = "Auto";
            p = p.Trim();

            if (EqualsIgnore(p, "Low"))
            {
                _cachedTier = 2;
                return;
            }
            if (EqualsIgnore(p, "Med") || EqualsIgnore(p, "Medium"))
            {
                _cachedTier = 1;
                return;
            }
            if (EqualsIgnore(p, "High"))
            {
                _cachedTier = 0;
                return;
            }

            // Auto
            if (forceAutoDetect || Time.unscaledTime >= _nextAutoAt - 29f)
                _autoLow = DetectLowEndHardware();
            _cachedTier = _autoLow ? 2 : 1;
        }

        private static bool IsAuto(string p)
        {
            return string.IsNullOrEmpty(p) || EqualsIgnore(p, "Auto");
        }

        private static bool EqualsIgnore(string a, string b)
        {
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        private static bool DetectLowEndHardware()
        {
            try
            {
                int ram = SystemInfo.systemMemorySize;
                int vram = SystemInfo.graphicsMemorySize;
                int cores = SystemInfo.processorCount;
                // Weak laptop / iGPU class: under 9 GB RAM, under 3 GB VRAM, or <=4 cores.
                if (ram > 0 && ram < 9000)
                    return true;
                if (vram > 0 && vram < 3000)
                    return true;
                if (cores > 0 && cores <= 4)
                    return true;
            }
            catch { }
            return false;
        }
    }

    /// <summary>
    /// Soft bridge so CI22XE can nudge WeXon.FlightAnalysis without a hard type ref
    /// when building OritasyAir-only (FlightAnalysis may be absent).
    /// </summary>
    internal static class FlightAnalysisBridge
    {
        private static bool _resolved;
        private static Type _type;
        private static System.Reflection.PropertyInfo _sampleHzProp;
        private static System.Reflection.FieldInfo _sampleHzField;

        private static void Resolve()
        {
            if (_resolved)
                return;
            _resolved = true;
            try
            {
                _type = Type.GetType("WeXon.FlightAnalysis, " + typeof(Plugin).Assembly.FullName);
                if (_type == null)
                    _type = Type.GetType("WeXon.FlightAnalysis");
                if (_type == null)
                {
                    System.Reflection.Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                    for (int i = 0; i < asms.Length; i++)
                    {
                        try { _type = asms[i].GetType("WeXon.FlightAnalysis"); }
                        catch { _type = null; }
                        if (_type != null)
                            break;
                    }
                }
                if (_type == null)
                    return;
                _sampleHzField = _type.GetField("SampleHz",
                    System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic);
            }
            catch { }
        }

        internal static void ApplyLowSampleHz(float hz)
        {
            Resolve();
            if (_sampleHzField == null)
                return;
            try
            {
                ConfigEntry<float> entry = _sampleHzField.GetValue(null) as ConfigEntry<float>;
                if (entry != null && entry.Value > hz)
                    entry.Value = hz;
            }
            catch { }
        }

        internal static float CapSampleHz(float hz)
        {
            float cap = PerfMode.FlightSampleHzCap();
            if (hz > cap)
                return cap;
            return hz;
        }
    }
}
