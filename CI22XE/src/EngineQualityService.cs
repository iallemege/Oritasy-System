using BepInEx.Configuration;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Caps Unity QualitySettings while Low-end is on. Does not rewrite the game Graphics save;
    /// restores the pre-cap snapshot when High / toggle off. Not DLSS and not a physics-timestep change.
    /// </summary>
    internal static class EngineQualityService
    {
        internal static ConfigEntry<bool> Enabled;
        private static float _nextTick = -1f;
        private static bool _haveSnap;
        private static bool _applied;
        private static int _snapAa;
        private static int _snapCascades;
        private static int _snapLights;
        private static int _snapParticleRay;
        private static int _snapMip;
        private static float _snapShadowDist;
        private static float _snapLod;
        private static bool _snapSoftParticles;
        private static bool _snapProbes;
        private static AnisotropicFiltering _snapAniso;
        private static ShadowResolution _snapShadowRes;

        internal static void Bind(ConfigFile config)
        {
            if (config == null || Enabled != null)
                return;
            Enabled = config.Bind("Performance", "EngineQualityAssist", true,
                "Cap Unity shadows/AA/LOD/particles while Low-end is on. Restores when High.");
        }

        internal static void Tick()
        {
            float now = Time.unscaledTime;
            if (now < _nextTick)
                return;
            _nextTick = now + 2f;
            ApplyNow();
        }

        internal static void ApplyNow()
        {
            bool want = Enabled != null && Enabled.Value && PerfMode.IsLow;
            if (!want)
            {
                Restore();
                return;
            }
            try
            {
                if (!_haveSnap)
                    Capture();

                if (QualitySettings.antiAliasing > 0)
                    QualitySettings.antiAliasing = 0;
                if (QualitySettings.shadowDistance > 2500f)
                    QualitySettings.shadowDistance = 2500f;
                if (QualitySettings.lodBias > 0.7f)
                    QualitySettings.lodBias = 0.7f;
                if (QualitySettings.pixelLightCount > 2)
                    QualitySettings.pixelLightCount = 2;
                if (QualitySettings.shadowCascades > 2)
                    QualitySettings.shadowCascades = 2;
                if (QualitySettings.particleRaycastBudget > 64)
                    QualitySettings.particleRaycastBudget = 64;
                QualitySettings.softParticles = false;
                QualitySettings.realtimeReflectionProbes = false;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;
                QualitySettings.shadowResolution = ShadowResolution.Low;
                if (QualitySettings.globalTextureMipmapLimit < 1)
                    QualitySettings.globalTextureMipmapLimit = 1;
                _applied = true;
            }
            catch { }
        }

        private static void Capture()
        {
            try
            {
                _snapAa = QualitySettings.antiAliasing;
                _snapCascades = QualitySettings.shadowCascades;
                _snapLights = QualitySettings.pixelLightCount;
                _snapParticleRay = QualitySettings.particleRaycastBudget;
                _snapMip = QualitySettings.globalTextureMipmapLimit;
                _snapShadowDist = QualitySettings.shadowDistance;
                _snapLod = QualitySettings.lodBias;
                _snapSoftParticles = QualitySettings.softParticles;
                _snapProbes = QualitySettings.realtimeReflectionProbes;
                _snapAniso = QualitySettings.anisotropicFiltering;
                _snapShadowRes = QualitySettings.shadowResolution;
                _haveSnap = true;
            }
            catch
            {
                _haveSnap = false;
            }
        }

        private static void Restore()
        {
            if (!_haveSnap || !_applied)
            {
                _applied = false;
                return;
            }
            try
            {
                QualitySettings.antiAliasing = _snapAa;
                QualitySettings.shadowCascades = _snapCascades;
                QualitySettings.pixelLightCount = _snapLights;
                QualitySettings.particleRaycastBudget = _snapParticleRay;
                QualitySettings.globalTextureMipmapLimit = _snapMip;
                QualitySettings.shadowDistance = _snapShadowDist;
                QualitySettings.lodBias = _snapLod;
                QualitySettings.softParticles = _snapSoftParticles;
                QualitySettings.realtimeReflectionProbes = _snapProbes;
                QualitySettings.anisotropicFiltering = _snapAniso;
                QualitySettings.shadowResolution = _snapShadowRes;
            }
            catch { }
            _applied = false;
            _haveSnap = false;
        }

        /// <summary>Oritasy Profile → Performance.</summary>
        internal static void DrawProfileToggle()
        {
            if (Enabled == null)
                return;
            GUILayout.Space(8f);
            GUILayout.Label(UiLang.T("Engine quality assist", "引擎画质辅助"), GUILayout.ExpandWidth(true));
            GUILayout.BeginHorizontal();
            GUILayout.Label(UiLang.T("Cap Unity quality on Low", "低配限制 Unity 画质"), GUILayout.Width(180f));
            Color prev = GUI.backgroundColor;
            bool on = Enabled.Value;
            GUI.backgroundColor = on ? new Color(0.45f, 0.85f, 0.55f) : Color.white;
            if (GUILayout.Button(on ? UiLang.T("ON", "开") : UiLang.T("OFF", "关"),
                GUILayout.Width(90f), GUILayout.Height(26f)))
            {
                Enabled.Value = !on;
                on = !on;
                _nextTick = -1f;
                ApplyNow();
            }
            GUI.backgroundColor = prev;
            GUILayout.Label(on ? UiLang.T("  [ON]", "  [开]") : UiLang.T("  [OFF]", "  [关]"),
                GUILayout.Width(56f));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label(
                on
                    ? UiLang.T(
                        "ON + Low: lower shadows / AA / LOD / mipmaps. Restores when you leave Low. Does not change physics.",
                        "开且低配：降低阴影/抗锯齿/LOD/贴图。离开低配会还原。不改物理。")
                    : UiLang.T(
                        "OFF: use the game Graphics menu as-is.",
                        "关：完全使用游戏本体图形菜单。"),
                GUILayout.ExpandWidth(true));
        }
    }
}
