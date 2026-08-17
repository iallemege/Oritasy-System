using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Oritasy
{
    /// <summary>
    /// CJK glyph fallback for Oritasy IMGUI + vanilla uGUI Text.
    /// Prefer plugins/OritasyFonts/*.ttf|otf|ttc (registered via AddFontResourceEx),
    /// else OS fonts (Microsoft YaHei / Noto Sans SC / SimHei).
    /// Edition D keeps English UI but still applies the font so CJK glyphs are not tofu.
    /// </summary>
    internal static class ChineseFontPatch
    {
        private const uint FrPrivate = 0x10;

        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern int AddFontResourceEx(string lpszFilename, uint fl, IntPtr pdv);

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<int> ImguiSize;

        private static bool _bound;
        private static bool _ready;
        private static Font _cjkFont;
        private static string _source = "";
        private static float _nextScanAt;
        private static readonly HashSet<int> PatchedTextIds = new HashSet<int>();
        private static readonly List<string> RegisteredFiles = new List<string>();
        private static int _onEnableFrame = -1;
        private static int _onEnableBudget;

        private static readonly string[] OsFontNames = new string[]
        {
            "Microsoft YaHei UI",
            "Microsoft YaHei",
            "Noto Sans SC",
            "Noto Sans CJK SC",
            "Source Han Sans SC",
            "SimHei",
            "DengXian",
            "Arial Unicode MS"
        };

        internal static Font CjkFont
        {
            get
            {
                EnsureLoaded();
                return _cjkFont;
            }
        }

        internal static string Source
        {
            get
            {
                EnsureLoaded();
                return _source ?? "";
            }
        }

        internal static void Bind(ConfigFile config)
        {
            if (config == null || _bound)
                return;
            _bound = true;
            Enabled = config.Bind("ChineseFont", "Enabled", true,
                "Load CJK font for Oritasy IMGUI and vanilla UI Text (fixes missing Chinese glyphs).");
            ImguiSize = config.Bind("ChineseFont", "ImguiSize", 16,
                "Dynamic OS font size used for IMGUI (12-28).");
        }

        internal static void EnsureLoaded()
        {
            if (_ready)
                return;
            _ready = true;
            if (Enabled != null && !Enabled.Value)
                return;

            try { Directory.CreateDirectory(FontsDir()); }
            catch { }

            _cjkFont = TryLoadOsFont();
            if (_cjkFont == null)
                _cjkFont = TryLoadFromPluginsFolder();

            if (_cjkFont != null && Plugin.Log != null)
                Plugin.Log.LogInfo("ChineseFontPatch: " + _source);
            else if (Plugin.Log != null)
                Plugin.Log.LogWarning(
                    "ChineseFontPatch: no CJK font. Put NotoSansSC / msyh .ttf into plugins/OritasyFonts/");
        }

        internal static void Tick()
        {
            if (Enabled != null && !Enabled.Value)
                return;
            EnsureLoaded();
            if (_cjkFont == null)
                return;

            float now = Time.unscaledTime;
            if (now < _nextScanAt)
                return;
            _nextScanAt = now + PerfMode.ChineseFontScanInterval();
            PatchSceneTexts();
        }

        internal static bool IsPatched(int id)
        {
            return PatchedTextIds.Contains(id);
        }

        internal static bool TryMarkPatched(int id)
        {
            if (PatchedTextIds.Contains(id))
                return false;
            PatchedTextIds.Add(id);
            return true;
        }

        /// <summary>Budget OnEnable patches per frame on LowEnd (Tick scan catches the rest).</summary>
        internal static bool ConsumeOnEnableBudget()
        {
            int f = Time.frameCount;
            if (f != _onEnableFrame)
            {
                _onEnableFrame = f;
                _onEnableBudget = PerfMode.ChineseFontOnEnableBudget();
            }
            if (_onEnableBudget <= 0)
                return false;
            _onEnableBudget--;
            return true;
        }

        internal static void ApplyTo(GUIStyle style)
        {
            if (style == null)
                return;
            Font f = CjkFont;
            if (f != null)
                style.font = f;
        }

        private static bool _guiSkinApplied;
        private static int _guiSkinFrame = -1;

        internal static void ApplyGuiSkin()
        {
            if (Enabled != null && !Enabled.Value)
                return;
            int f = Time.frameCount;
            if (_guiSkinApplied && f == _guiSkinFrame)
                return;
            Font font = CjkFont;
            if (font == null || GUI.skin == null)
                return;
            try
            {
                if (GUI.skin.font != font)
                    GUI.skin.font = font;
                ApplyTo(GUI.skin.label);
                ApplyTo(GUI.skin.button);
                ApplyTo(GUI.skin.toggle);
                ApplyTo(GUI.skin.textField);
                ApplyTo(GUI.skin.textArea);
                ApplyTo(GUI.skin.box);
                _guiSkinApplied = true;
                _guiSkinFrame = f;
            }
            catch { }
        }

        private static string FontsDir()
        {
            try { return Path.Combine(Paths.PluginPath, "OritasyFonts"); }
            catch { return Path.Combine(Application.dataPath, "OritasyFonts"); }
        }

        private static Font TryLoadFromPluginsFolder()
        {
            string dir = FontsDir();
            if (!Directory.Exists(dir))
                return null;

            string[] files;
            try
            {
                List<string> all = new List<string>();
                all.AddRange(Directory.GetFiles(dir, "*.ttf"));
                all.AddRange(Directory.GetFiles(dir, "*.otf"));
                all.AddRange(Directory.GetFiles(dir, "*.ttc"));
                files = all.ToArray();
            }
            catch { return null; }

            if (files.Length == 0)
                return null;

            // Prefer static CJK faces; Unity/TMP often fail on variable fonts (*-VF*).
            Array.Sort(files, (a, b) =>
            {
                int sa = IsVariableFontPath(a) ? 1 : 0;
                int sb = IsVariableFontPath(b) ? 1 : 0;
                if (sa != sb)
                    return sa.CompareTo(sb);
                return string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            });
            int size = ClampSize();

            // Pass 1: non-VF only. Pass 2: allow VF if nothing else works.
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < files.Length; i++)
                {
                    string path = files[i];
                    if (pass == 0 && IsVariableFontPath(path))
                        continue;

                    try
                    {
                        if (AddFontResourceEx(path, FrPrivate, IntPtr.Zero) > 0
                            && !RegisteredFiles.Contains(path))
                            RegisteredFiles.Add(path);
                    }
                    catch { }

                    string stem = Path.GetFileNameWithoutExtension(path);
                    string[] tryNames = new string[]
                    {
                        "Microsoft YaHei UI",
                        "Microsoft YaHei",
                        "Noto Sans SC",
                        stem,
                        stem.Replace("-", " ").Replace("_", " "),
                        "SimHei"
                    };
                    for (int n = 0; n < tryNames.Length; n++)
                    {
                        Font f = TryCreateOs(tryNames[n], size);
                        if (f != null)
                        {
                            _source = "plugins/OritasyFonts/" + Path.GetFileName(path)
                                + " (" + tryNames[n] + ")";
                            return f;
                        }
                    }
                }
            }
            return null;
        }

        private static bool IsVariableFontPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;
            string name = Path.GetFileName(path) ?? "";
            return name.IndexOf("-VF", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Variable", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Font TryLoadOsFont()
        {
            int size = ClampSize();
            // Register common Windows CJK faces so CreateDynamicFontFromOSFont can see them.
            try
            {
                string fonts = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts");
                string[] local = new string[]
                {
                    Path.Combine(fonts, "msyh.ttc"),
                    Path.Combine(fonts, "msyhbd.ttc"),
                    Path.Combine(fonts, "msyh.ttf"),
                    Path.Combine(fonts, "simhei.ttf"),
                    Path.Combine(fonts, "Deng.ttf")
                };
                for (int i = 0; i < local.Length; i++)
                {
                    if (!File.Exists(local[i]))
                        continue;
                    try
                    {
                        if (AddFontResourceEx(local[i], FrPrivate, IntPtr.Zero) > 0
                            && !RegisteredFiles.Contains(local[i]))
                            RegisteredFiles.Add(local[i]);
                    }
                    catch { }
                }
            }
            catch { }

            for (int i = 0; i < OsFontNames.Length; i++)
            {
                Font f = TryCreateOs(OsFontNames[i], size);
                if (f != null)
                {
                    _source = "OS:" + OsFontNames[i] + " @" + size;
                    return f;
                }
            }
            return null;
        }

        private static int ClampSize()
        {
            int size = ImguiSize != null ? ImguiSize.Value : 16;
            return PerfBudgetService.ClampImguiFontSize(size);
        }

        private static Font TryCreateOs(string name, int size)
        {
            if (string.IsNullOrEmpty(name))
                return null;
            try
            {
                return Font.CreateDynamicFontFromOSFont(name, size);
            }
            catch { return null; }
        }

        private static void PatchSceneTexts()
        {
            Text[] texts;
            try { texts = UnityEngine.Object.FindObjectsOfType<Text>(); }
            catch { return; }
            if (texts == null)
                return;

            Font f = _cjkFont;
            for (int i = 0; i < texts.Length; i++)
            {
                Text t = texts[i];
                if (t == null)
                    continue;
                int id = t.GetInstanceID();
                if (PatchedTextIds.Contains(id))
                    continue;
                try
                {
                    if (t.font != f)
                        t.font = f;
                    PatchedTextIds.Add(id);
                }
                catch { }
            }
            if (PatchedTextIds.Count > 4000)
                PatchedTextIds.Clear();
        }
    }

    [HarmonyPatch(typeof(Text), "OnEnable")]
    internal static class ChineseFontTextOnEnablePatch
    {
        [HarmonyPostfix]
        private static void Postfix(Text __instance)
        {
            try
            {
                if (ChineseFontPatch.Enabled != null && !ChineseFontPatch.Enabled.Value)
                    return;
                if (__instance == null)
                    return;
                int id = __instance.GetInstanceID();
                // Already applied — skip. Do not mark before budget, or Tick will never retry.
                if (ChineseFontPatch.IsPatched(id))
                    return;
                if (!ChineseFontPatch.ConsumeOnEnableBudget())
                    return;
                Font f = ChineseFontPatch.CjkFont;
                if (f == null)
                    return;
                if (__instance.font != f)
                    __instance.font = f;
                ChineseFontPatch.TryMarkPatched(id);
            }
            catch { }
        }
    }
}
