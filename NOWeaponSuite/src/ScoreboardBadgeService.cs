using System;
using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Greenfield Tab-board badge presentation math (0.0.9.62).
    /// </summary>
    internal static class ScoreboardBadgeService
    {
        internal static string UnlockKeyToCode(string unlockKey)
        {
            if (string.IsNullOrEmpty(unlockKey))
                return null;
            if (string.Equals(unlockKey, KillAccolades.UnlockCarrier, StringComparison.OrdinalIgnoreCase))
                return "CV";
            if (string.Equals(unlockKey, KillAccolades.UnlockAdvanced, StringComparison.OrdinalIgnoreCase))
                return "NU";
            if (string.Equals(unlockKey, KillAccolades.UnlockStrategic, StringComparison.OrdinalIgnoreCase))
                return "ST";
            return null;
        }

        internal static string BadgeTitle(string code, bool chinese)
        {
            if (string.IsNullOrEmpty(code))
                return chinese ? "成就" : "Achievement";
            switch (code.ToUpperInvariant())
            {
                case "GS": return chinese ? "神挡杀神" : "God Slayer";
                case "SF": return chinese ? "强敌击破" : "Strong Foe";
                case "DK": return chinese ? "双杀" : "Double Kill";
                case "TK": return chinese ? "三杀" : "Triple Kill";
                case "QK": return chinese ? "四杀" : "Quad Kill";
                case "AC": return chinese ? "空中王牌" : "Ace";
                case "AP": return chinese ? "空战先锋" : "Air Pioneer";
                case "BD": return chinese ? "战场主宰" : "Battlefield Dominator";
                case "CV": return chinese ? "航母解锁" : "Carrier Unlock";
                case "NU": return chinese ? "高级解锁" : "Advanced Unlock";
                case "ST": return chinese ? "战略解锁" : "Strategic Unlock";
                default: return chinese ? "成就" : "Achievement";
            }
        }

        internal static Color BadgeColor(string code)
        {
            if (string.IsNullOrEmpty(code))
                return Color.white;
            switch (code.ToUpperInvariant())
            {
                case "GS": return new Color(1f, 0.32f, 0.18f, 1f);
                case "SF": return new Color(1f, 0.72f, 0.25f, 1f);
                case "DK": return new Color(1f, 0.88f, 0.3f, 1f);
                case "TK": return new Color(1f, 0.62f, 0.2f, 1f);
                case "QK": return new Color(1f, 0.42f, 0.18f, 1f);
                case "AC": return new Color(1f, 0.22f, 0.15f, 1f);
                case "AP": return new Color(0.7f, 1f, 0.45f, 1f);
                case "BD": return new Color(1f, 0.4f, 0.75f, 1f);
                case "CV": return new Color(0.45f, 0.85f, 1f, 1f);
                case "NU": return new Color(0.95f, 0.55f, 1f, 1f);
                case "ST": return new Color(0.55f, 1f, 0.7f, 1f);
                case "FTK": return new Color(1f, 0.2f, 0.2f, 1f);
                default: return new Color(0.85f, 0.9f, 0.88f, 1f);
            }
        }

        internal static string PadName(string name, int width)
        {
            if (string.IsNullOrEmpty(name))
                name = "?";
            if (name.Length > width)
                return name.Substring(0, width);
            return name.PadRight(width);
        }
    }
}
