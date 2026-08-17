using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield F10 aerial support / repair menu layout (0.0.9.75+).
    /// OritasyCJK era: repair list geometry also lives here (0.0.9.78).
    /// AerialResupply / ComponentRepair own draw + state.
    /// </summary>
    internal static class AerialSupportLayoutService
    {
        internal const float MenuW = 460f;
        internal const float ResupplyH = 400f;
        internal const float RepairH = 540f;

        internal static Rect MenuRect(float screenW, float screenH, bool repairTab)
        {
            float h = repairTab ? RepairH : ResupplyH;
            return new Rect((screenW - MenuW) * 0.5f, (screenH - h) * 0.5f, MenuW, h);
        }

        internal static Rect RepairContentRect(Rect menuBox)
        {
            return new Rect(menuBox.x, menuBox.y + 20f, menuBox.width, menuBox.height - 20f);
        }

        internal static Rect StatusBannerRect(float screenW, float screenH)
        {
            float w = Mathf.Clamp(screenW * 0.55f, 420f, 780f);
            return new Rect((screenW - w) * 0.5f, screenH * 0.10f, w, 28f);
        }

        internal static Rect ProgressBar(Rect parent, float y, float height)
        {
            return new Rect(parent.x + 16f, y, parent.width - 32f, height);
        }

        // --- Component repair subsidiary (F10 tab) ---
        internal const float RepairTitleY = 48f;
        internal const float RepairListTop = 74f;
        internal const float RepairListBottomReserve = 228f;
        internal const float RepairRowH = 34f;
        internal const float RepairRowGap = 4f;
        internal const float RepairButtonReserve = 148f;

        internal static Rect RepairTitleRect(Rect box)
        {
            return new Rect(box.x + 16f, box.y + RepairTitleY, box.width - 32f, 22f);
        }

        internal static Rect RepairListView(Rect box)
        {
            float listH = box.height - RepairListBottomReserve;
            return new Rect(box.x + 12f, box.y + RepairListTop, box.width - 24f, listH);
        }

        internal static Rect RepairExtinguishButton(Rect box)
        {
            float by = box.yMax - RepairButtonReserve;
            float w = (box.width - 40f) * 0.5f;
            return new Rect(box.x + 16f, by, w, 32f);
        }

        internal static Rect RepairAllButton(Rect box)
        {
            float by = box.yMax - RepairButtonReserve;
            float w = (box.width - 40f) * 0.5f;
            return new Rect(box.x + 24f + w, by, w, 32f);
        }

        internal static Rect RepairRestoreButton(Rect box)
        {
            return new Rect(box.x + 16f, box.yMax - 108f, box.width - 32f, 32f);
        }

        internal static Rect RepairContinuousRect(Rect box)
        {
            return new Rect(box.x + 16f, box.yMax - 62f, box.width - 32f, 22f);
        }

        internal static Rect RepairStatusLine(Rect box)
        {
            return new Rect(box.x + 16f, box.yMax - 32f, box.width - 32f, 20f);
        }
    }
}
