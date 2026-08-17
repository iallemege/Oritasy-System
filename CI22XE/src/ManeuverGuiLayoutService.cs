using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield F1 System panel layout + slider ranges (0.0.9.72).
    /// AircraftManeuverGui owns UI state, apply, and config write-back.
    /// </summary>
    internal static class ManeuverGuiLayoutService
    {
        internal const float DeferredApplySec = 0.12f;

        // Slider ranges — keep in sync with DrawPanel EditSlider calls
        internal const float AircraftGMin = 2f;
        internal const float AircraftGMax = 20f;
        internal const float MaxSpeedMin = 80f;
        internal const float MaxSpeedMax = 600f;
        internal const float CornerSpeedFloor = 40f;
        internal const float ApproachMin = 30f;
        internal const float ApproachMax = 200f;
        internal const float LandingMin = 25f;
        internal const float LandingMax = 180f;
        internal const float TakeoffMin = 30f;
        internal const float TakeoffMax = 220f;
        internal const float TurnRadiusMin = 100f;
        internal const float TurnRadiusMax = 4000f;
        internal const float PilotGMin = 2f;
        internal const float PilotGMax = 20f;
        internal const float PilotStrengthMin = 0.25f;
        internal const float PilotStrengthMax = 3f;
        internal const float PitchMulMin = 0.5f;
        internal const float PitchMulMax = 4f;
        internal const float RollMulMin = 0.5f;
        internal const float RollMulMax = 4f;
        internal const float AlphaMulMin = 0.5f;
        internal const float AlphaMulMax = 3f;
        internal const float ThrustMulMin = 0.25f;
        internal const float ThrustMulMax = 12f;
        internal const float FuelBurnMulMin = 0.1f;
        internal const float FuelBurnMulMax = 2f;
        internal const float FuelCapMulMin = 0.5f;
        internal const float FuelCapMulMax = 5f;

        internal static Rect PanelRect(float screenW, float screenH)
        {
            float w = Mathf.Min(480f, screenW * 0.92f);
            float h = Mathf.Min(620f, screenH * 0.86f);
            return new Rect((screenW - w) * 0.5f, (screenH - h) * 0.5f, w, h);
        }

        internal static float DeferredDeadline(float nowUnscaled)
        {
            return nowUnscaled + DeferredApplySec;
        }

        internal static float CornerSliderMax(float maxSpeedDraft)
        {
            return Mathf.Max(CornerSpeedFloor, maxSpeedDraft);
        }
    }
}
