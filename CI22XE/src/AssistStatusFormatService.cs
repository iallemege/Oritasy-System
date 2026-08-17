namespace Oritasy
{
    /// <summary>
    /// Greenfield F2/F3/F10 corner-chip status strings (0.0.9.73).
    /// Callers pass already-localized mode labels where needed.
    /// </summary>
    internal static class AssistStatusFormatService
    {
        internal static string AutopilotChip(bool engaged, string modeLabel)
        {
            if (engaged)
            {
                return UiLang.T(
                    "F2 Autopilot  |  ON  " + modeLabel,
                    "F2 自动驾驶  |  开  " + modeLabel);
            }
            return UiLang.T(
                "F2 Autopilot  |  " + modeLabel,
                "F2 自动驾驶  |  " + modeLabel);
        }

        internal static string BeginnerChip(bool takeoffActive, bool menuOpen)
        {
            if (takeoffActive)
                return UiLang.T("F3 Beginner mode  |  TAKEOFF", "F3 新手模式  |  起飞中");
            if (menuOpen)
                return UiLang.T("F3 Beginner mode  |  OPEN", "F3 新手模式  |  已打开");
            return UiLang.T("F3 Beginner mode", "F3 新手模式");
        }

        internal static string ResupplyChip(bool active, int progressPct)
        {
            if (active)
            {
                return UiLang.T(
                    "F10 Resupply  |  ACTIVE  " + progressPct + "%",
                    "F10 空中补给  |  进行中  " + progressPct + "%");
            }
            return UiLang.T("F10 Resupply / Repair", "F10 空中补给 / 维修组件");
        }

        internal static string EngineChip(bool menuOpen)
        {
            if (menuOpen)
                return UiLang.T("F8 Monitor  |  OPEN", "F8 引擎监视  |  开");
            return UiLang.T("F8 Engine Component Monitor", "F8 引擎组件监视系统");
        }
    }
}
