using System;
using System.Text;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Central EN/ZH switch for all Oritasy air UI (F1–F11, HUD chips, CCIP, etc.).
    /// Reads the same PlayerPrefs key as WeXon.ModUiLang ("WeXon.UiLang").
    /// Edition D (EnglishOnlyEdition) always returns English.
    /// ZH mode strips parenthetical hint suffixes (…)/(…) so labels stay short.
    /// </summary>
    internal static class UiLang
    {
        private const string PrefKey = "WeXon.UiLang"; // 0 = EN, 1 = ZH

        internal static bool IsChinese
        {
            get
            {
                try
                {
                    if (PluginInfo.EnglishOnlyEdition)
                        return false;
                    if (PlayerPrefs.HasKey(PrefKey))
                        return PlayerPrefs.GetInt(PrefKey, 0) != 0;
                }
                catch { }
                return false;
            }
        }

        /// <summary>Pick English or Chinese string. ZH omits (hint) / （说明） segments.</summary>
        internal static string T(string en, string zh)
        {
            if (!IsChinese)
                return en ?? "";
            return StripParenHints(zh ?? en ?? "");
        }

        /// <summary>Apply ZH paren-strip when Chinese; pass-through otherwise.</summary>
        internal static string Zh(string text)
        {
            if (string.IsNullOrEmpty(text) || !IsChinese)
                return text ?? "";
            return StripParenHints(text);
        }

        /// <summary>
        /// Remove ASCII/fullwidth parenthetical segments used as secondary hints.
        /// EN mode keeps them; call only for ZH display strings.
        /// </summary>
        internal static string StripParenHints(string s)
        {
            if (string.IsNullOrEmpty(s))
                return s ?? "";
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '(' || c == '（')
                {
                    char close = c == '(' ? ')' : '）';
                    int depth = 1;
                    int j = i + 1;
                    while (j < s.Length && depth > 0)
                    {
                        if (s[j] == c) depth++;
                        else if (s[j] == close) depth--;
                        j++;
                    }
                    // Drop the whole (…) / （…） including a single leading space.
                    if (sb.Length > 0 && sb[sb.Length - 1] == ' ')
                        sb.Length--;
                    i = j - 1;
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        internal static string Code
        {
            get { return IsChinese ? "ZH" : "EN"; }
        }
    }
}
