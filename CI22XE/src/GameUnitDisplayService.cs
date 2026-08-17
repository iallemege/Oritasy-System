using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Oritasy
{
    /// <summary>
    /// Third in-game unit system: Swedish mile (SN) + knots + Swedish meter (SM).
    /// 1 SN = 10.1 km. 1 SM = 2.18 metric mass units (kg in WeightReading).
    /// Vanilla Options dropdown only has Metric / Imperial;
    /// Imperial already uses kt for airspeed.
    /// </summary>
    internal static class GameUnitDisplayService
    {
        internal const float SwedishMileMeters = 10100f;
        internal const float SwedishMeterMetric = 2.18f;
        internal const float KnotsPerMps = 1.94384f;

        private static readonly FieldInfo DropdownField =
            AccessTools.Field(typeof(GameplayMenu), "unitSystemDropdown");

        internal static ConfigEntry<bool> SwedishUnits;

        private static bool _applySwedish;

        internal static bool SwedishActive
        {
            get { return SwedishUnits != null && SwedishUnits.Value; }
        }

        internal static void Bind(ConfigFile cfg)
        {
            if (cfg == null || SwedishUnits != null)
                return;
            SwedishUnits = cfg.Bind("Presentation", "SwedishUnits", false,
                "Swedish miles (SN, 1 SN = 10.1 km), knots, and Swedish meters (SM, 1 SM = 2.18 metric mass).");
        }

        internal static string Speed(float metersPerSecond)
        {
            if (SwedishActive)
                return FormatKnots(metersPerSecond);
            try { return UnitConverter.SpeedReading(metersPerSecond); }
            catch { return (metersPerSecond * 3.6f).ToString("0") + " km/h"; }
        }

        internal static string Distance(float meters)
        {
            if (SwedishActive)
                return FormatSwedishMile(meters);
            try { return UnitConverter.DistanceReading(meters); }
            catch
            {
                if (meters >= 1000f)
                    return (meters * 0.001f).ToString("0.0") + " km";
                return meters.ToString("0") + " m";
            }
        }

        internal static string FormatKnots(float metersPerSecond)
        {
            float kn = metersPerSecond * KnotsPerMps;
            if (kn < 0f)
                kn = -kn;
            return UiLang.T(kn.ToString("0") + " kt", kn.ToString("0") + " 节");
        }

        internal static string Weight(float metricKg)
        {
            if (SwedishActive)
                return FormatSwedishMeter(metricKg, "");
            try { return UnitConverter.WeightReading(metricKg); }
            catch { return metricKg.ToString("0") + " kg"; }
        }

        internal static string MassFlow(float metricKgPerTime, string per)
        {
            if (per == null)
                per = "";
            if (SwedishActive)
                return FormatSwedishMeter(metricKgPerTime, per);
            return metricKgPerTime.ToString(per == "/s" ? "0.00" : "0.0") + " kg" + per;
        }

        internal static string FormatSwedishMile(float meters)
        {
            float abs = meters < 0f ? -meters : meters;
            if (abs < 500f)
                return meters.ToString("0") + " m";
            float sn = meters / SwedishMileMeters;
            if (abs >= SwedishMileMeters * 10f)
                return sn.ToString("0.0") + " SN";
            return sn.ToString("0.00") + " SN";
        }

        internal static string FormatSwedishMeter(float metricKg, string per)
        {
            if (per == null)
                per = "";
            float sm = metricKg / SwedishMeterMetric;
            float abs = sm < 0f ? -sm : sm;
            string num;
            if (abs >= 100f)
                num = sm.ToString("0");
            else if (abs >= 10f)
                num = sm.ToString("0.0");
            else
                num = sm.ToString("0.00");
            return num + " SM" + per;
        }

        internal static void DrawF1Section(GUIStyle sectionStyle, GUIStyle labelStyle, GUIStyle btnStyle)
        {
            GUILayout.Space(6f);
            GUILayout.Label(UiLang.T("UNITS", "单位"), sectionStyle);
            GUILayout.Label(UiLang.T(
                "1 SN = 10.1 km · 1 SM = 2.18 M · speed in knots. Also in Options → Gameplay.",
                "1 瑞典里 SN = 10.1 km · 1 瑞典米 SM = 2.18 M · 速度用节。游戏选项 → 玩法 里也可选。"),
                labelStyle);

            int mode = CurrentMode();
            GUILayout.BeginHorizontal();
            DrawModeButton(0, mode, UiLang.T("Metric", "公制"), btnStyle);
            DrawModeButton(1, mode, UiLang.T("Imperial", "英制"), btnStyle);
            DrawModeButton(2, mode, UiLang.T("Sverige metric", "瑞典单位制"), btnStyle);
            GUILayout.EndHorizontal();
        }

        internal static void EnsureDropdown(GameplayMenu menu)
        {
            Dropdown drop = GetDropdown(menu);
            if (drop == null || drop.options == null)
                return;
            string label = OptionLabel();
            if (drop.options.Count >= 3)
            {
                if (drop.options[2] != null)
                    drop.options[2].text = label;
            }
            else
                drop.options.Add(new Dropdown.OptionData(label));
            try { drop.RefreshShownValue(); }
            catch { }
        }

        internal static void SyncDropdown(GameplayMenu menu)
        {
            EnsureDropdown(menu);
            Dropdown drop = GetDropdown(menu);
            if (drop == null)
                return;
            if (SwedishActive)
                drop.SetValueWithoutNotify(2);
        }

        internal static void CaptureApply(GameplayMenu menu)
        {
            Dropdown drop = GetDropdown(menu);
            if (drop == null)
            {
                _applySwedish = false;
                return;
            }
            EnsureDropdown(menu);
            _applySwedish = drop.value >= 2;
            if (SwedishUnits != null)
                SwedishUnits.Value = _applySwedish;
            if (_applySwedish)
                drop.SetValueWithoutNotify(0);
        }

        internal static void RestoreApply(GameplayMenu menu)
        {
            if (!_applySwedish)
                return;
            Dropdown drop = GetDropdown(menu);
            if (drop != null)
                drop.SetValueWithoutNotify(2);
        }

        internal static void SanitizeLoadedUnitSystem()
        {
            int stored = 0;
            try { stored = PlayerPrefs.GetInt("UnitSystem", 0); }
            catch { return; }
            if (stored < 2)
                return;
            if (SwedishUnits != null)
                SwedishUnits.Value = true;
            PlayerSettings.unitSystem = PlayerSettings.UnitSystem.Metric;
            try { PlayerPrefs.SetInt("UnitSystem", 0); }
            catch { }
        }

        private static void DrawModeButton(int mode, int current, string label, GUIStyle btnStyle)
        {
            string text = mode == current ? ("[" + label + "]") : label;
            if (GUILayout.Button(text, btnStyle, GUILayout.Height(28f)))
                SetMode(mode);
        }

        private static int CurrentMode()
        {
            if (SwedishActive)
                return 2;
            if (PlayerSettings.unitSystem == PlayerSettings.UnitSystem.Imperial)
                return 1;
            return 0;
        }

        private static void SetMode(int mode)
        {
            if (mode == 2)
            {
                if (SwedishUnits != null)
                    SwedishUnits.Value = true;
                PlayerSettings.unitSystem = PlayerSettings.UnitSystem.Metric;
                try { PlayerPrefs.SetInt("UnitSystem", 0); }
                catch { }
            }
            else
            {
                if (SwedishUnits != null)
                    SwedishUnits.Value = false;
                PlayerSettings.unitSystem = mode == 1
                    ? PlayerSettings.UnitSystem.Imperial
                    : PlayerSettings.UnitSystem.Metric;
                try { PlayerPrefs.SetInt("UnitSystem", mode); }
                catch { }
            }
            try { PlayerSettings.ApplyPrefs(); }
            catch { }
        }

        private static string OptionLabel()
        {
            return UiLang.T("Sverige metric", "瑞典单位制");
        }

        private static Dropdown GetDropdown(GameplayMenu menu)
        {
            if (menu == null || DropdownField == null)
                return null;
            try { return DropdownField.GetValue(menu) as Dropdown; }
            catch { return null; }
        }
    }

    [HarmonyPatch(typeof(UnitConverter), "SpeedReading")]
    internal static class Patch_UnitConverter_SpeedReading
    {
        [HarmonyPrefix]
        private static bool Prefix(float speed, ref string __result)
        {
            if (!GameUnitDisplayService.SwedishActive)
                return true;
            __result = GameUnitDisplayService.FormatKnots(speed);
            return false;
        }
    }

    [HarmonyPatch(typeof(UnitConverter), "SpeedReadingGround")]
    internal static class Patch_UnitConverter_SpeedReadingGround
    {
        [HarmonyPrefix]
        private static bool Prefix(float speed, ref string __result)
        {
            if (!GameUnitDisplayService.SwedishActive)
                return true;
            __result = GameUnitDisplayService.FormatKnots(speed);
            return false;
        }
    }

    [HarmonyPatch(typeof(UnitConverter), "DistanceReading")]
    internal static class Patch_UnitConverter_DistanceReading
    {
        [HarmonyPrefix]
        private static bool Prefix(float distance, ref string __result)
        {
            if (!GameUnitDisplayService.SwedishActive)
                return true;
            __result = GameUnitDisplayService.FormatSwedishMile(distance);
            return false;
        }
    }

    [HarmonyPatch(typeof(UnitConverter), "WeightReading")]
    internal static class Patch_UnitConverter_WeightReading
    {
        [HarmonyPrefix]
        private static bool Prefix(float weight, ref string __result)
        {
            if (!GameUnitDisplayService.SwedishActive)
                return true;
            __result = GameUnitDisplayService.FormatSwedishMeter(weight, "");
            return false;
        }
    }

    [HarmonyPatch(typeof(GameplayMenu), "Start")]
    internal static class Patch_GameplayMenu_Start
    {
        [HarmonyPostfix]
        private static void Postfix(GameplayMenu __instance)
        {
            GameUnitDisplayService.SyncDropdown(__instance);
        }
    }

    [HarmonyPatch(typeof(GameplayMenu), "UpdateLabels")]
    internal static class Patch_GameplayMenu_UpdateLabels
    {
        [HarmonyPostfix]
        private static void Postfix(GameplayMenu __instance)
        {
            GameUnitDisplayService.SyncDropdown(__instance);
        }
    }

    [HarmonyPatch(typeof(GameplayMenu), "ApplySettings")]
    internal static class Patch_GameplayMenu_ApplySettings
    {
        [HarmonyPrefix]
        private static void Prefix(GameplayMenu __instance)
        {
            GameUnitDisplayService.CaptureApply(__instance);
        }

        [HarmonyPostfix]
        private static void Postfix(GameplayMenu __instance)
        {
            GameUnitDisplayService.RestoreApply(__instance);
        }
    }

    [HarmonyPatch(typeof(PlayerSettings), "LoadPrefs")]
    internal static class Patch_PlayerSettings_LoadPrefs
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            GameUnitDisplayService.SanitizeLoadedUnitSystem();
        }
    }
}
