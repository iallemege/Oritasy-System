using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>Fuel HUD + chase alerts + airframe sample</summary>
    internal static partial class PlayerAutopilot
    {
        private static float _cachedStackY = -1f;
        private static float _nextStackYRefresh;
        private static int _cachedStackScreenH;
        private static System.Reflection.MethodInfo _cornerStackTopYMethod;
        private static bool _cornerStackTopYResolved;
        private static readonly Vector3[] StackCorners = new Vector3[4];
        private static readonly object[] EmptyArgs = new object[0];
        private static readonly List<string> AlertLines = new List<string>(8);
        private static readonly List<Color> AlertColors = new List<Color>(8);
        private static float _nextAlertRebuild;

        private static float ResolveCornerStackY()
        {
            // Cache: tip stack Y is stable; avoid Type.GetType / FindObjectOfType every chip every frame.
            if (_cachedStackY > 0f
                && Time.unscaledTime < _nextStackYRefresh
                && _cachedStackScreenH == (int)UiScaleService.Height)
                return _cachedStackY;

            _nextStackYRefresh = Time.unscaledTime + 0.5f;
            _cachedStackScreenH = (int)UiScaleService.Height;
            float y = ResolveCornerStackYUncached();
            _cachedStackY = y;
            return y;
        }

        private static float ResolveCornerStackYUncached()
        {
            // Prefer WeXon helper when merged into Oritasy.dll
            try
            {
                if (!_cornerStackTopYResolved)
                {
                    _cornerStackTopYResolved = true;
                    System.Type t = System.Type.GetType("WeXon.StrategicArsenal, Oritasy")
                        ?? System.Type.GetType("WeXon.StrategicArsenal");
                    if (t != null)
                    {
                        _cornerStackTopYMethod = t.GetMethod("CornerStackTopY",
                            System.Reflection.BindingFlags.Static
                            | System.Reflection.BindingFlags.NonPublic
                            | System.Reflection.BindingFlags.Public);
                    }
                }
                if (_cornerStackTopYMethod != null)
                    return (float)_cornerStackTopYMethod.Invoke(null, EmptyArgs);
            }
            catch { }

            try
            {
                ChargeIndicator ci = UnityEngine.Object.FindObjectOfType<ChargeIndicator>();
                if (ci != null && ci.isActiveAndEnabled)
                {
                    RectTransform rt = ci.transform as RectTransform;
                    if (rt == null)
                        rt = ci.GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        rt.GetWorldCorners(StackCorners);
                        float guiBottom = UiScaleService.FromScreenYFlipped(StackCorners[0].y);
                        return Mathf.Clamp(guiBottom + 6f, 72f, UiScaleService.Height * 0.42f);
                    }
                }
            }
            catch { }
            return Mathf.Clamp(UiScaleService.Height * 0.11f, 96f, 140f);
        }

        /// <summary>Threat + aero warnings for third-person chase/orbit HUD.</summary>
        internal static void DrawChaseAlerts(Aircraft ac)
        {
            if (ac == null)
                return;
            if (AirframeWearService.LocalPilotGone(ac))
                return;
            if (Event.current != null && Event.current.type != EventType.Repaint)
                return;
            EnsureStyles();
            List<string> lines = AlertLines;
            List<Color> colors = AlertColors;

            // Rebuild alert strings ~8 Hz — ToString/allocs were every IMGUI Repaint.
            if (Time.unscaledTime >= _nextAlertRebuild)
            {
                _nextAlertRebuild = Time.unscaledTime + 0.12f;
                RebuildChaseAlertLines(ac, lines, colors);
            }

            if (lines.Count == 0)
                return;

            bool flash = (Time.unscaledTime % 0.4f) < 0.22f;
            float y = UiScaleService.Height * 0.14f;
            for (int i = 0; i < lines.Count; i++)
            {
                string text = lines[i];
                bool critical = text.IndexOf("MISSILE", StringComparison.Ordinal) >= 0
                    || text.IndexOf("STALL", StringComparison.Ordinal) >= 0
                    || text.IndexOf("OVER-G", StringComparison.Ordinal) >= 0
                    || text.IndexOf("TERRAIN", StringComparison.Ordinal) >= 0
                    || text.IndexOf("LOCK", StringComparison.Ordinal) >= 0
                    || text.IndexOf("ENGINE FIRE", StringComparison.Ordinal) >= 0
                    || text.IndexOf("STRUCT", StringComparison.Ordinal) >= 0
                    || text.IndexOf("导弹", StringComparison.Ordinal) >= 0
                    || text.IndexOf("失速", StringComparison.Ordinal) >= 0
                    || text.IndexOf("过载", StringComparison.Ordinal) >= 0
                    || text.IndexOf("地形", StringComparison.Ordinal) >= 0
                    || text.IndexOf("锁定", StringComparison.Ordinal) >= 0
                    || text.IndexOf("起火", StringComparison.Ordinal) >= 0
                    || text.IndexOf("结构", StringComparison.Ordinal) >= 0;
                if (critical && !flash)
                    continue;

                _alertStyle.normal.textColor = colors[i];
                float w = 360f;
                Rect r = new Rect((UiScaleService.Width - w) * 0.5f, y, w, 22f);
                Color prev = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.5f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(r, text, _alertStyle);
                GUI.color = prev;
                y += 24f;
            }
        }

        private static void RebuildChaseAlertLines(Aircraft ac, List<string> lines, List<Color> colors)
        {
            lines.Clear();
            colors.Clear();

            try
            {
                MissileWarning mw = ac.GetMissileWarningSystem();
                if (mw != null && mw.IsWarning())
                {
                    lines.Add(UiLang.T("! MISSILE", "! 导弹"));
                    colors.Add(new Color(1f, 0.2f, 0.15f, 1f));
                }
            }
            catch { }

            try
            {
                if (WarThunderRwrHud.HasActiveLock())
                {
                    lines.Add(UiLang.T("! RADAR LOCK", "! 雷达锁定"));
                    colors.Add(new Color(1f, 0.55f, 0.15f, 1f));
                }
                else if (WarThunderRwrHud.HasActiveSearch())
                {
                    lines.Add(UiLang.T("RADAR PAINT", "雷达照射"));
                    colors.Add(new Color(1f, 0.85f, 0.25f, 1f));
                }
            }
            catch { }

            float aoa = ReadAoA(ac);
            float g = ReadG(ac);
            float lim = 9f;
            try
            {
                AircraftParameters ap = ac.GetAircraftParameters();
                if (ap != null)
                    lim = Mathf.Max(4f, ap.aircraftGLimit);
            }
            catch { }

            if (aoa > 18f)
            {
                lines.Add(UiLang.T(
                    "STALL  AoA " + aoa.ToString("0.0"),
                    "失速  迎角 " + aoa.ToString("0.0")));
                colors.Add(new Color(1f, 0.35f, 0.2f, 1f));
            }
            else if (aoa > 12f)
            {
                lines.Add(UiLang.T(
                    "HIGH AoA  " + aoa.ToString("0.0"),
                    "高迎角  " + aoa.ToString("0.0")));
                colors.Add(new Color(1f, 0.75f, 0.2f, 1f));
            }

            if (Mathf.Abs(g) >= lim * 0.92f)
            {
                lines.Add(UiLang.T(
                    "OVER-G  " + g.ToString("+0.0;-0.0") + " / " + lim.ToString("0"),
                    "过载  " + g.ToString("+0.0;-0.0") + " / " + lim.ToString("0")));
                colors.Add(new Color(1f, 0.25f, 0.2f, 1f));
            }
            else if (Mathf.Abs(g) >= lim * 0.75f)
            {
                lines.Add(UiLang.T(
                    "HIGH-G  " + g.ToString("+0.0;-0.0"),
                    "高过载  " + g.ToString("+0.0;-0.0")));
                colors.Add(new Color(1f, 0.7f, 0.2f, 1f));
            }

            float sink = 0f;
            try
            {
                if (ac.rb != null)
                    sink = -ac.rb.velocity.y;
            }
            catch { }
            float ralt = 0f;
            try { ralt = ac.radarAlt; }
            catch { }
            if (ralt < 120f && sink > 25f && ralt > 0.5f)
            {
                lines.Add(UiLang.T(
                    "TERRAIN  " + ralt.ToString("0") + "m  sink " + sink.ToString("0"),
                    "地形  " + ralt.ToString("0") + "m  下沉 " + sink.ToString("0")));
                colors.Add(new Color(1f, 0.3f, 0.15f, 1f));
            }
            else if (ralt < 40f && ac.speed > 40f && ralt > 0.5f)
            {
                lines.Add(UiLang.T(
                    "LOW ALT  " + ralt.ToString("0") + "m",
                    "低高度  " + ralt.ToString("0") + "m"));
                colors.Add(new Color(1f, 0.65f, 0.2f, 1f));
            }

            try
            {
                AircraftParameters ap = ac.GetAircraftParameters();
                if (ap != null && ac.speed < ap.landingSpeed * 0.95f && ralt > 15f)
                {
                    lines.Add(UiLang.T(
                        "LOW SPEED  " + GameUnitDisplayService.Speed(ac.speed),
                        "低速  " + GameUnitDisplayService.Speed(ac.speed)));
                    colors.Add(new Color(1f, 0.7f, 0.25f, 1f));
                }
            }
            catch { }

            try
            {
                AirframeCondition af = SampleAirframe(ac);
                if (af.EngineFire)
                {
                    lines.Add(UiLang.T("ENGINE FIRE", "发动机起火"));
                    colors.Add(new Color(1f, 0.25f, 0.1f, 1f));
                }
                // Only warn on real structural / engine loss — never from a false default health.
                bool structHurt = af.Detached >= 0.08f || af.WingFrac < 0.92f;
                bool engHurt = af.EngineHealth < 0.85f;
                if (af.Severity >= 0.12f && (structHurt || engHurt || af.EngineFire))
                {
                    string tag;
                    if (structHurt)
                        tag = UiLang.T(
                            "STRUCT  wing " + (af.WingFrac * 100f).ToString("0") + "%",
                            "结构  机翼 " + (af.WingFrac * 100f).ToString("0") + "%");
                    else if (engHurt)
                        tag = UiLang.T(
                            "DAMAGE  eng " + (af.EngineHealth * 100f).ToString("0") + "%",
                            "损伤  引擎 " + (af.EngineHealth * 100f).ToString("0") + "%");
                    else
                        tag = UiLang.T("DAMAGE", "损伤");
                    lines.Add(tag);
                    colors.Add(af.Severity >= 0.35f
                        ? new Color(1f, 0.3f, 0.15f, 1f)
                        : new Color(1f, 0.7f, 0.25f, 1f));
                }
            }
            catch { }
        }

        /// <summary>
        /// Sample detached parts / wing area / engine damageFactor.
        /// Used so F2 LAND raises approach speed and cuts sink/bank when aero is degraded.
        /// </summary>
        internal static AirframeCondition SampleAirframe(Aircraft ac)
        {
            AirframeCondition c = new AirframeCondition();
            c.WingFrac = 1f;
            c.EngineHealth = 1f;
            if (ac == null)
                return c;

            int id = 0;
            try { id = ac.GetInstanceID(); }
            catch { }
            if (id != 0 && id == _airframeAcId && Time.unscaledTime < _airframeNextSample)
                return _airframe;

            _airframeAcId = id;
            _airframeNextSample = Time.unscaledTime + 0.28f;

            try
            {
                if (ac.partDamageTracker != null)
                    c.Detached = Mathf.Clamp01(ac.partDamageTracker.GetDetachedRatio());
            }
            catch { }

            try
            {
                AeroPart[] parts = ac.GetComponentsInChildren<AeroPart>(true);
                float areaAll = 0f;
                float areaLive = 0f;
                int n = 0;
                int det = 0;
                if (parts != null)
                {
                    for (int i = 0; i < parts.Length; i++)
                    {
                        AeroPart p = parts[i];
                        if (p == null)
                            continue;
                        float a = 0f;
                        try { a = p.GetWingArea(); }
                        catch
                        {
                            try { a = p.WingArea; }
                            catch { a = 0f; }
                        }
                        if (a < 0.05f)
                            continue;
                        n++;
                        areaAll += a;
                        bool detached = false;
                        try { detached = p.IsDetached(); }
                        catch { }
                        if (detached)
                            det++;
                        else
                            areaLive += a;
                    }
                }
                c.WingParts = n;
                c.DetachedParts = det;
                if (areaAll > 0.01f)
                    c.WingFrac = Mathf.Clamp01(areaLive / areaAll);
                else if (n > 0)
                    c.WingFrac = 1f - (float)det / (float)n;
            }
            catch { }

            try
            {
                float engSum = 0f;
                int engN = 0;
                bool fire = false;
                if (ac.engines != null)
                {
                    for (int i = 0; i < ac.engines.Count; i++)
                    {
                        IEngine eng = ac.engines[i];
                        if (eng == null)
                            continue;
                        bool engFire;
                        float health = SampleEngineHealth(eng, out engFire);
                        if (engFire)
                            fire = true;
                        engSum += health;
                        engN++;
                    }
                }
                c.EngineFire = fire;
                if (engN > 0)
                    c.EngineHealth = Mathf.Clamp01(engSum / (float)engN);
            }
            catch { }

            float wingLoss = 1f - c.WingFrac;
            float engLoss = 1f - c.EngineHealth;
            c.Severity = Mathf.Clamp01(
                c.Detached * 1.15f
                + wingLoss * 0.95f
                + engLoss * 0.55f
                + (c.EngineFire ? 0.25f : 0f));
            _airframe = c;
            return c;
        }

        /// <summary>
        /// Per-engine health 0..1. Turbojet.damageFactor is a health/thrust multiplier (1 = OK).
        /// Non-turbojet: default healthy; only degrade when inoperable / killed — never from GetMaxThrust threshold.
        /// </summary>
        private static float SampleEngineHealth(IEngine eng, out bool fire)
        {
            fire = false;
            if (eng == null)
                return 1f;

            Turbojet tj = eng as Turbojet;
            if (tj != null)
            {
                if (tj.engineFire)
                    fire = true;
                // damageFactor: performance multiplier, healthy ≈ 1. Uninitialized 0 while operable → treat as OK.
                float df = tj.damageFactor;
                bool operable = ReadBoolField(tj, TurbojetOperableField, true);
                float health;
                if (df > 0.001f)
                    health = Mathf.Clamp01(df);
                else
                    health = operable ? 1f : 0f;
                if (!operable)
                    health = Mathf.Min(health, 0.15f);
                if (fire)
                    health *= 0.3f;
                return Mathf.Clamp01(health);
            }

            TurbineEngine te = eng as TurbineEngine;
            if (te != null)
            {
                bool operable = true;
                try { operable = te.IsOperable(); }
                catch
                {
                    operable = ReadBoolField(te, TurbineOperableField, true);
                }
                return operable ? 1f : 0.15f;
            }

            Turbofan tf = eng as Turbofan;
            if (tf != null)
            {
                bool operable = ReadBoolField(tf, TurbofanOperableField, true);
                return operable ? 1f : 0.15f;
            }

            DuctedFan dfan = eng as DuctedFan;
            if (dfan != null)
            {
                // Field is "inoperable" (inverted).
                bool inop = ReadBoolField(dfan, DuctedFanInoperableField, false);
                return inop ? 0.15f : 1f;
            }

            // Other IEngine implementations: assume healthy. Do not infer damage from GetMaxThrust —
            // many report normalized / zero max thrust even when undamaged (was false "eng 40%").
            return 1f;
        }

        private static bool ReadBoolField(object obj, FieldInfo field, bool defaultValue)
        {
            if (obj == null || field == null)
                return defaultValue;
            try
            {
                object v = field.GetValue(obj);
                if (v is bool)
                    return (bool)v;
            }
            catch { }
            return defaultValue;
        }

        /// <summary>Raise landing / approach TAS when wing/engine damage reduces lift or go-around power.</summary>
        internal static float EffectiveLandSpeed(float baseLandSpd, AirframeCondition af)
        {
            float mul = 1f
                + af.Detached * 0.55f
                + (1f - af.WingFrac) * 0.7f
                + (1f - af.EngineHealth) * 0.18f;
            if (af.EngineFire)
                mul += 0.08f;
            mul = Mathf.Clamp(mul, 1f, 1.85f);
            return Mathf.Max(40f, baseLandSpd * mul);
        }

        internal static string AirframeStatusTag(AirframeCondition af)
        {
            if (af.Severity < 0.08f)
                return "";
            if (af.EngineFire)
                return "  FIRE";
            if (af.Detached >= 0.1f || af.WingFrac < 0.9f)
                return "  DMG wing " + (af.WingFrac * 100f).ToString("0") + "%";
            if (af.EngineHealth < 0.85f)
                return "  ENG " + (af.EngineHealth * 100f).ToString("0") + "%";
            return "  DMG";
        }
    }
}
