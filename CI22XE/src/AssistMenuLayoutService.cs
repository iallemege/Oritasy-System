using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Corner-chip + assist menu layout.
    /// Tip stack: 0=F1, 1=F2, 2=F3, 3=F4, 4=F5, 5=F6, 6=F7, 7=F8, 8=F9, 9=F10, 10=F11
    /// </summary>
    internal static class AssistMenuLayoutService
    {
        internal const float ChipW = 248f;
        internal const float ChipH = 20f;
        internal const float ChipGap = 3f;
        internal const float ChipMarginRight = 18f;

        internal const int SlotF1 = 0;
        internal const int SlotF2 = 1;
        internal const int SlotF3 = 2;
        internal const int SlotF4 = 3;
        internal const int SlotF5 = 4;
        internal const int SlotF6 = 5;
        internal const int SlotF7 = 6;
        internal const int SlotF8 = 7;
        internal const int SlotF9 = 8;
        internal const int SlotF10 = 9;
        internal const int SlotF11 = 10;

        internal static Rect HostFundMenuRect(float screenW, float screenH)
        {
            float w = 520f;
            float h = 420f;
            return new Rect((screenW - w) * 0.5f, (screenH - h) * 0.5f, w, h);
        }

        internal static float ChipY(float stackBaseY, int slot)
        {
            if (slot < 0)
                slot = 0;
            return stackBaseY + slot * (ChipH + ChipGap);
        }

        internal static Rect ChipRect(float screenW, float stackBaseY, int slot)
        {
            return new Rect(
                screenW - ChipW - ChipMarginRight,
                ChipY(stackBaseY, slot),
                ChipW,
                ChipH);
        }

        internal static Rect AutopilotMenuRect(float screenW, float screenH, bool straight, bool landMode)
        {
            float w = 420f;
            float h = straight ? 470f : (landMode ? 560f : 370f);
            return new Rect((screenW - w) * 0.5f, (screenH - h) * 0.5f, w, h);
        }

        internal static Rect BeginnerMenuRect(float screenW, float screenH)
        {
            float w = 420f;
            float h = 300f;
            return new Rect((screenW - w) * 0.5f, (screenH - h) * 0.5f, w, h);
        }

        internal static Rect IlsMenuRect(float screenW, float screenH)
        {
            float w = 420f;
            float h = 360f;
            return new Rect((screenW - w) * 0.5f, (screenH - h) * 0.5f, w, h);
        }

        internal static Rect PrivateMessageMenuRect(float screenW, float screenH)
        {
            float w = 560f;
            float h = 460f;
            return new Rect((screenW - w) * 0.5f, (screenH - h) * 0.5f, w, h);
        }

        internal static Rect KillChoiceMenuRect(float screenW, float screenH)
        {
            float w = 520f;
            float h = 420f;
            return new Rect((screenW - w) * 0.5f, (screenH - h) * 0.5f, w, h);
        }

        internal static Rect FlashBannerRect(float screenW, float screenH)
        {
            float w = 480f;
            float h = 28f;
            return new Rect((screenW - w) * 0.5f, screenH * 0.18f, w, h);
        }

        internal static Rect FuelHudRect(float screenW, float screenH)
        {
            float w = 420f;
            float h = 44f;
            return new Rect((screenW - w) * 0.5f, screenH - h - 18f, w, h);
        }
    }
}
