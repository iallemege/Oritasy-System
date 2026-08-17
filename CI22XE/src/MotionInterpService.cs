using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Smooths motion between physics ticks via Unity Rigidbody interpolation.
    /// This is NOT DLSS/FSR frame generation (cannot invent extra rendered frames from BepInEx).
    /// Physics stays ~50 Hz; the camera/meshes lerp between the last two poses at display rate.
    /// </summary>
    internal static class MotionInterpService
    {
        internal static ConfigEntry<bool> Enabled;
        private static float _nextTick = -1f;

        internal static void Bind(ConfigFile config)
        {
            if (config == null || Enabled != null)
                return;
            Enabled = config.Bind("Performance", "MotionInterpolation", true,
                "Smooth aircraft/missile/vehicle motion between physics ticks. Not GPU frame generation.");
        }

        internal static void ApplyRb(Rigidbody rb)
        {
            if (rb == null)
                return;
            bool on = Enabled != null && Enabled.Value;
            try
            {
                if (!on)
                {
                    if (rb.interpolation != RigidbodyInterpolation.None)
                        rb.interpolation = RigidbodyInterpolation.None;
                    return;
                }
                if (rb.isKinematic)
                    return;
                if (rb.interpolation != RigidbodyInterpolation.Interpolate)
                    rb.interpolation = RigidbodyInterpolation.Interpolate;
            }
            catch { }
        }

        internal static void ApplyUnit(Unit unit)
        {
            if (unit == null)
                return;
            if (unit is Building || unit is Scenery)
                return;
            try { ApplyRb(unit.rb); }
            catch { }
        }

        internal static void Tick()
        {
            float now = Time.unscaledTime;
            if (now < _nextTick)
                return;
            _nextTick = now + 2f;
            Aircraft local = null;
            try
            {
                if (GameManager.GetLocalAircraft(out local) && local != null)
                    ApplyUnit(local);
            }
            catch { }

            try
            {
                List<Aircraft> all = UnitRegistry.allAircraft;
                if (all == null)
                    return;
                int n = all.Count;
                if (n > 48)
                    n = 48;
                for (int i = 0; i < n; i++)
                    ApplyUnit(all[i]);
            }
            catch { }
        }

        /// <summary>Oritasy Profile → Performance.</summary>
        internal static void DrawProfileToggle()
        {
            if (Enabled == null)
                return;
            GUILayout.Space(8f);
            GUILayout.Label(UiLang.T("Motion interpolation", "运动插值"), GUILayout.ExpandWidth(true));
            GUILayout.BeginHorizontal();
            GUILayout.Label(UiLang.T("Smooth physics motion", "平滑物理运动"), GUILayout.Width(180f));
            Color prev = GUI.backgroundColor;
            bool on = Enabled.Value;
            GUI.backgroundColor = on ? new Color(0.45f, 0.85f, 0.55f) : Color.white;
            if (GUILayout.Button(on ? UiLang.T("ON", "开") : UiLang.T("OFF", "关"),
                GUILayout.Width(90f), GUILayout.Height(26f)))
            {
                Enabled.Value = !on;
                on = !on;
                _nextTick = -1f;
                Tick();
            }
            GUI.backgroundColor = prev;
            GUILayout.Label(on ? UiLang.T("  [ON]", "  [开]") : UiLang.T("  [OFF]", "  [关]"),
                GUILayout.Width(56f));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label(
                on
                    ? UiLang.T(
                        "ON: Unity interpolates aircraft / missiles / vehicles between physics ticks (smoother at 60–144 Hz). Does not generate extra GPU frames.",
                        "开：在物理节拍之间插值飞机/导弹/载具（60–144Hz 更顺）。不会额外生成 GPU 画面。")
                    : UiLang.T(
                        "OFF: raw physics poses (can look stepped if the monitor is faster than physics).",
                        "关：直接用物理位姿（显示器高于物理频率时可能一顿一顿）。"),
                GUILayout.ExpandWidth(true));
        }
    }
}
