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
        private static int _menuScanCount;

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

            _cjkFont = TryLoadFromPluginsFolder();
            if (_cjkFont == null)
                _cjkFont = TryLoadOsFont();

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

            bool menu = false;
            try { menu = OritasyPresentation.MainMenuActive; }
            catch { }
            if (menu)
            {
                // 210C retried FindObjectsOfType every 2.5s on the title screen and
                // hitch the menu. Patch twice (load + late widgets), then stop.
                // Text.OnEnable Harmony covers anything spawned later.
                if (_menuScanCount >= 2)
                    return;
                float nowMenu = Time.unscaledTime;
                if (_menuScanCount == 1 && nowMenu < _nextScanAt)
                    return;
                PatchSceneTexts(true);
                _menuScanCount++;
                _nextScanAt = nowMenu + 1.2f;
                return;
            }

            _menuScanCount = 0;
            // In-mission FindObjectsOfType<Text> scales with nametags / combat HUD
            // and freezes for seconds near enemies. Text.OnEnable Harmony covers
            // new labels; do not rescan the whole scene.
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
            string[] dirs = FontSearchDirs();
            for (int i = 0; i < dirs.Length; i++)
            {
                Font f = TryLoadFromDir(dirs[i]);
                if (f != null)
                    return f;
            }
            return null;
        }

        private static string[] FontSearchDirs()
        {
            List<string> dirs = new List<string>(4);
            string root = FontsDir();
            if (!string.IsNullOrEmpty(root))
                dirs.Add(root);
            try
            {
                string plugins = Paths.PluginPath;
                if (!string.IsNullOrEmpty(plugins) && Directory.Exists(plugins))
                {
                    string[] kids = Directory.GetDirectories(plugins);
                    if (kids != null)
                    {
                        for (int i = 0; i < kids.Length; i++)
                        {
                            string nested = Path.Combine(kids[i], "OritasyFonts");
                            if (Directory.Exists(nested) && dirs.IndexOf(nested) < 0)
                                dirs.Add(nested);
                        }
                    }
                }
            }
            catch { }
            return dirs.ToArray();
        }

        private static Font TryLoadFromDir(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
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

            // Pass 1 only: never use variable fonts. Unity HUD / IMGUI turn VF faces into black blobs.
            for (int pass = 0; pass < 1; pass++)
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

        private static void PatchSceneTexts(bool mainMenu)
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
                if (!mainMenu && ShouldSkipHudText(t))
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

        internal static bool ShouldSkipHudComponent(Component c)
        {
            if (c == null)
                return false;
            try
            {
                if (c.GetComponentInParent<FlightHud>() != null)
                    return true;
                if (c.GetComponentInParent<HUDApp>() != null)
                    return true;
                if (c.GetComponentInParent<CombatHUD>() != null)
                    return true;
                if (c.GetComponentInParent<StatusDisplay>() != null)
                    return true;
                if (c.GetComponentInParent<HeadMountedDisplay>() != null)
                    return true;
                if (c.GetComponentInParent<HUDAppManager>() != null)
                    return true;
                if (c.GetComponentInParent<ChargeIndicator>() != null)
                    return true;
                if (c.GetComponentInParent<WeaponIndicator>() != null)
                    return true;
                if (c.GetComponentInParent<CountermeasureIndicator>() != null)
                    return true;
            }
            catch { }
            Transform x = null;
            try { x = c.transform; }
            catch { return false; }
            int hops = 0;
            while (x != null && hops < 12)
            {
                string n = x.name;
                if (!string.IsNullOrEmpty(n))
                {
                    string l = n.ToLowerInvariant();
                    if (l.IndexOf("flighthud") >= 0 || l.IndexOf("hudextras") >= 0
                        || l.IndexOf("hmd") >= 0 || l == "hud" || l.StartsWith("hud"))
                        return true;
                    if (l.IndexOf("oritasy_note", StringComparison.Ordinal) >= 0
                        || l.IndexOf("oritasy_guide", StringComparison.Ordinal) >= 0
                        || l.IndexOf("oritasy_changelog", StringComparison.Ordinal) >= 0)
                        return true;
                }
                try { x = x.parent; }
                catch { break; }
                hops++;
            }
            return false;
        }

        internal static bool ShouldSkipHudText(Text t)
        {
            if (t == null)
                return false;
            if (ShouldSkipHudComponent(t))
                return true;
            Font font = null;
            try { font = t.font; }
            catch { }
            if (font != null && !font.dynamic)
                return true;
            if (font != null && !string.IsNullOrEmpty(font.name))
            {
                string fn = font.name.ToLowerInvariant();
                if (fn.IndexOf("hud") >= 0 || fn.IndexOf("digital") >= 0)
                    return true;
            }
            return false;
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
                bool menu = false;
                try { menu = OritasyPresentation.MainMenuActive; }
                catch { }
                if (!menu && ChineseFontPatch.ShouldSkipHudText(__instance))
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
