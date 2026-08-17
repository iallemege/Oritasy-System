using System;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// EN/ZH preference for kill-accolade tips (and related feeds).
    /// Toggle lives on the Oritasy Profile main-menu panel.
    /// Oritasy 0.0.9.13D Special Edition locks to English-only.
    /// ZH mode strips parenthetical hint suffixes via T()/Zh().
    /// </summary>
    internal static class ModUiLang
    {
        private const string PrefKey = "WeXon.UiLang"; // 0 = EN, 1 = ZH

        private static bool _loaded;
        private static bool _chinese;
        private static bool _editionResolved;
        private static bool _englishOnlyEdition;

        internal static bool EnglishOnlyEdition
        {
            get
            {
                ResolveEdition();
                return _englishOnlyEdition;
            }
        }

        internal static bool IsChinese
        {
            get
            {
                EnsureLoaded();
                if (EnglishOnlyEdition)
                    return false;
                return _chinese;
            }
        }

        internal static string Code
        {
            get { return IsChinese ? "ZH" : "EN"; }
        }

        /// <summary>Pick EN/ZH; ZH strips (hint) / （说明） segments.</summary>
        internal static string T(string en, string zh)
        {
            if (!IsChinese)
                return en ?? "";
            return StripParenHints(zh ?? en ?? "");
        }

        /// <summary>Strip parentheticals when Chinese.</summary>
        internal static string Zh(string text)
        {
            if (string.IsNullOrEmpty(text) || !IsChinese)
                return text ?? "";
            return StripParenHints(text);
        }

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
                    if (sb.Length > 0 && sb[sb.Length - 1] == ' ')
                        sb.Length--;
                    i = j - 1;
                    continue;
                }
                sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        internal static void EnsureLoaded()
        {
            ResolveEdition();
            if (_loaded)
            {
                if (_englishOnlyEdition)
                    _chinese = false;
                return;
            }
            _loaded = true;
            if (_englishOnlyEdition)
            {
                _chinese = false;
                try
                {
                    PlayerPrefs.SetInt(PrefKey, 0);
                    PlayerPrefs.Save();
                }
                catch { }
                return;
            }
            if (PlayerPrefs.HasKey(PrefKey))
                _chinese = PlayerPrefs.GetInt(PrefKey, 0) != 0;
            else
                _chinese = DetectSystemChinese();
        }

        internal static void SetChinese(bool chinese)
        {
            EnsureLoaded();
            if (EnglishOnlyEdition)
            {
                _chinese = false;
                return;
            }
            if (_chinese == chinese)
                return;
            _chinese = chinese;
            PlayerPrefs.SetInt(PrefKey, chinese ? 1 : 0);
            PlayerPrefs.Save();
            NotifyGameZhLocalizer();
        }

        /// <summary>Soft-call Oritasy.GameZhLocalizer when Profile language flips.</summary>
        private static void NotifyGameZhLocalizer()
        {
            try
            {
                Type t = Type.GetType("Oritasy.GameZhLocalizer, Oritasy")
                    ?? Type.GetType("Oritasy.GameZhLocalizer");
                if (t == null)
                    return;
                MethodInfo m = t.GetMethod("OnLanguageChanged",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (m != null)
                    m.Invoke(null, null);
            }
            catch { }
        }

        internal static void DrawToggleRow()
        {
            EnsureLoaded();
            if (EnglishOnlyEdition)
            {
                GUILayout.Label("Language  [EN]  —  Oritasy Special Edition D (English only)",
                    GUILayout.ExpandWidth(true));
                GUILayout.Label(
                    "This D build ships English mod UI only.",
                    GUILayout.ExpandWidth(true));
                return;
            }

            GUILayout.BeginHorizontal();
            GUILayout.Label(_chinese ? "界面语言" : "UI language", GUILayout.Width(140f));
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = !_chinese ? new Color(0.45f, 0.85f, 0.55f) : Color.white;
            if (GUILayout.Button("English", GUILayout.Width(90f), GUILayout.Height(26f)))
                SetChinese(false);
            GUI.backgroundColor = _chinese ? new Color(0.45f, 0.85f, 0.55f) : Color.white;
            if (GUILayout.Button(_chinese ? "中文" : "Chinese", GUILayout.Width(90f), GUILayout.Height(26f)))
                SetChinese(true);
            GUI.backgroundColor = prev;
            GUILayout.Label("  [" + Code + "]", GUILayout.Width(48f));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label(
                _chinese
                    ? "已选中文：游戏界面与 Oritasy 菜单/HUD/档案均为中文。主菜单 Help/Guide 说明仍为英文。"
                    : "English selected: game UI, Oritasy menus, HUD, Profile, Notes, and Guide are English.",
                GUILayout.ExpandWidth(true));
        }

        private static void ResolveEdition()
        {
            if (_editionResolved)
                return;
            _editionResolved = true;
            _englishOnlyEdition = false;
            try
            {
                Type t = Type.GetType("Oritasy.PluginInfo, Oritasy")
                    ?? Type.GetType("Oritasy.PluginInfo");
                if (t == null)
                    return;
                FieldInfo f = t.GetField("EnglishOnlyEdition",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (f != null && f.FieldType == typeof(bool))
                    _englishOnlyEdition = (bool)f.GetValue(null);
            }
            catch { }
        }

        private static bool DetectSystemChinese()
        {
            try
            {
                SystemLanguage lang = Application.systemLanguage;
                return lang == SystemLanguage.Chinese
                    || lang == SystemLanguage.ChineseSimplified
                    || lang == SystemLanguage.ChineseTraditional;
            }
            catch { return false; }
        }
    }
}
