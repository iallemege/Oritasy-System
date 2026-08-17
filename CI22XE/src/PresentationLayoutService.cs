using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield boot splash / welcome dialog layout (0.0.9.71).
    /// OritasyPresentation owns styles, cursor hold, and draw.
    /// </summary>
    internal static class PresentationLayoutService
    {
        internal static int SplashTitleFont(int screenHeight)
        {
            return Mathf.Clamp(screenHeight / 18, 28, 64);
        }

        internal static int SplashCreditFont(int screenHeight)
        {
            return Mathf.Clamp(screenHeight / 48, 12, 22);
        }

        /// <summary>
        /// IMGUI CalcSize underestimates descenders for dynamic CJK skins (clips "g" in IAllemege).
        /// </summary>
        internal static float CreditLineHeight(GUIStyle style, float calcHeight)
        {
            float fs = style != null ? style.fontSize : 16f;
            return Mathf.Max(calcHeight + 6f, fs * 1.65f);
        }

        internal static int SplashLoadingFont(int screenHeight)
        {
            return Mathf.Clamp(screenHeight / 55, 11, 18);
        }

        internal static float BrandGap(float screenHeight)
        {
            return Mathf.Max(8f, screenHeight * 0.012f);
        }

        internal static float BrandPad(float screenHeight)
        {
            return Mathf.Max(16f, screenHeight * 0.02f);
        }

        internal static float BrandLineGap(float screenHeight)
        {
            return Mathf.Max(2f, screenHeight * 0.004f);
        }

        internal static Rect CenteredBox(float screenW, float screenH, float maxW, float maxH, float wFrac, float hFrac)
        {
            float boxW = Mathf.Min(maxW, screenW * wFrac);
            float boxH = Mathf.Min(maxH, screenH * hFrac);
            return new Rect((screenW - boxW) * 0.5f, (screenH - boxH) * 0.5f, boxW, boxH);
        }

        internal static Rect WelcomeBox(float screenW, float screenH)
        {
            return CenteredBox(screenW, screenH, 1480f, 980f, 0.92f, 0.90f);
        }

        internal static Rect ChangelogBox(float screenW, float screenH)
        {
            return CenteredBox(screenW, screenH, 1280f, 900f, 0.90f, 0.86f);
        }

        internal static float DialogButtonWidth(float boxWidth)
        {
            return Mathf.Min(220f, boxWidth - 40f);
        }
    }
}
