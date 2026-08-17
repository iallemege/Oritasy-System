using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// TMP CJK glyph support (0.0.9.77).
    /// Preferred: JustInCase/ChangeYourName (NotoSansCJKsc UnityFS AssetBundle) as TMP fallback.
    /// Fallback only when that pack is missing: Dynamic atlas patch / CreateFontAsset attempts.
    /// </summary>
    internal static class ChineseTmpFontService
    {
        private static bool _resolved;
        private static bool _loggedPatch;
        private static bool _loggedCreate;
        private static bool _justInCaseChecked;
        private static bool _justInCasePresent;
        private static Type _tmpFontAssetType;
        private static Type _tmpSettingsType;
        private static Type _tmpTextType;
        private static Type _atlasModeType;
        private static Type _glyphRenderType;
        private static Type _fontEngineType;
        private static MethodInfo _createSimple;
        private static MethodInfo _createFull;
        private static MethodInfo _tryAddString;
        private static MethodInfo _fontEngineInit;
        private static MethodInfo _fontEngineLoadPath;
        private static MethodInfo _fontEngineLoadFont;
        private static PropertyInfo _settingsFallbackProp;
        private static PropertyInfo _assetFallbackProp;
        private static PropertyInfo _atlasModeProp;
        private static PropertyInfo _tmpFontProp;
        private static PropertyInfo _tmpTextProp;
        private static FieldInfo _sourceFontField;
        private static object _cjkTmpAsset;
        private static Font _sourceFont;
        private static string _sourceName = "";
        private static float _nextInjectAt;
        private static readonly HashSet<int> PatchedAssetIds = new HashSet<int>();
        private static readonly HashSet<int> AssignedTextIds = new HashSet<int>();
        private static int _patchCount;
        private static int _createFailLogged;

        /// <summary>
        /// True when OritasyCJK AssetBundle or JustInCase/ChangeYourName provides TMP fallback.
        /// Invasive Dynamic sourceFontFile rewrites are skipped in that case.
        /// </summary>
        internal static bool ExternalTmpFallbackPresent
        {
            get
            {
                DetectExternalFallback();
                return _justInCasePresent;
            }
        }

        // Kept name used by older call sites
        internal static bool JustInCasePresent
        {
            get { return ExternalTmpFallbackPresent; }
        }

        private static void DetectExternalFallback()
        {
            if (_justInCaseChecked)
                return;
            _justInCaseChecked = true;
            _justInCasePresent = false;

            // Prefer Oritasy-owned pack
            try
            {
                if (OritasyCjkAssetPack.IsReady)
                {
                    _justInCasePresent = true;
                    if (Plugin.Log != null)
                        Plugin.Log.LogInfo(
                            "ChineseTmpFontService: OritasyCJK AssetBundle active — skipping Dynamic font rewrite");
                    return;
                }
            }
            catch { }

            try
            {
                if (Chainloader.PluginInfos != null)
                {
                    foreach (var kv in Chainloader.PluginInfos)
                    {
                        string id = kv.Key ?? "";
                        string name = kv.Value != null && kv.Value.Metadata != null
                            ? (kv.Value.Metadata.Name ?? "") : "";
                        string blob = (id + " " + name).ToLowerInvariant();
                        if (blob.IndexOf("changeyourname", StringComparison.Ordinal) >= 0
                            || blob.IndexOf("assassin1076", StringComparison.Ordinal) >= 0)
                        {
                            _justInCasePresent = true;
                            break;
                        }
                    }
                }
            }
            catch { }

            if (!_justInCasePresent)
            {
                try
                {
                    // Legacy third-party pack still counts if its DLL is loaded / present.
                    string plugins = Paths.PluginPath;
                    if (File.Exists(Path.Combine(plugins, "JustInCase", "ChangeYourName.dll"))
                        && File.Exists(Path.Combine(plugins, "JustInCase", "NotoSansCJKsc-Regular")))
                        _justInCasePresent = true;
                }
                catch { }
            }

            if (Plugin.Log != null)
            {
                if (_justInCasePresent)
                    Plugin.Log.LogInfo(
                        "ChineseTmpFontService: external TMP CJK fallback present");
                else
                    Plugin.Log.LogWarning(
                        "ChineseTmpFontService: no OritasyCJK / JustInCase — TMP Chinese may show as tofu. Ship one file: plugins/OritasyFonts/OritasyCJK.");
            }
        }

        internal static void Tick()
        {
            if (ChineseFontPatch.Enabled != null && !ChineseFontPatch.Enabled.Value)
                return;

            DetectExternalFallback();
            if (_justInCasePresent)
                return;

            EnsureSourceFont();
            if (_sourceFont == null)
                return;

            float now = Time.unscaledTime;
            if (now < _nextInjectAt)
                return;
            _nextInjectAt = now + Mathf.Max(0.75f, PerfMode.ChineseFontScanInterval() * 0.5f);

            PatchAllFontAssets();
            EnsureCreatedFallbackAsset();
            InjectGlobalFallback();
            AssignFontsOnChineseTmpTexts();
        }

        internal static void EnsureReady()
        {
            DetectExternalFallback();
            if (_justInCasePresent)
                return;
            EnsureSourceFont();
            if (_sourceFont == null)
                return;
            PatchAllFontAssets();
            EnsureCreatedFallbackAsset();
            InjectGlobalFallback();
        }

        /// <summary>After ZH write: ensure glyphs exist on the active TMP font.</summary>
        internal static void ApplyToTmpInstance(object tmpText)
        {
            if (tmpText == null)
                return;
            DetectExternalFallback();
            if (_justInCasePresent)
                return;
            EnsureReady();
            ResolveTypes();
            if (_tmpFontProp == null)
                return;
            try
            {
                object fontAsset = _tmpFontProp.GetValue(tmpText, null);
                string text = null;
                if (_tmpTextProp != null)
                    text = _tmpTextProp.GetValue(tmpText, null) as string;
                if (fontAsset != null)
                {
                    PatchOneFontAsset(fontAsset);
                    TryAddChars(fontAsset, text);
                }

                if (_cjkTmpAsset != null && fontAsset != null
                    && !ReferenceEquals(fontAsset, _cjkTmpAsset))
                {
                    if (!string.IsNullOrEmpty(text) && ContainsCjk(text))
                        AddFallback(fontAsset, _cjkTmpAsset);
                }

                UnityEngine.Object uo = tmpText as UnityEngine.Object;
                if (uo != null)
                    AssignedTextIds.Add(uo.GetInstanceID());
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null && _createFailLogged < 3)
                {
                    _createFailLogged++;
                    Plugin.Log.LogWarning("ChineseTmpFontService ApplyToTmp: " + ex.Message);
                }
            }
        }

        private static void DetectJustInCase()
        {
            DetectExternalFallback();
        }

        private static void ResolveTypes()
        {
            if (_resolved)
                return;
            _resolved = true;
            try
            {
                _tmpFontAssetType = Type.GetType("TMPro.TMP_FontAsset, Unity.TextMeshPro");
                _tmpSettingsType = Type.GetType("TMPro.TMP_Settings, Unity.TextMeshPro");
                _tmpTextType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro")
                    ?? Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
                _atlasModeType = Type.GetType("TMPro.AtlasPopulationMode, Unity.TextMeshPro");
                _glyphRenderType = Type.GetType("UnityEngine.TextCore.LowLevel.GlyphRenderMode, UnityEngine.TextCoreFontEngineModule")
                    ?? Type.GetType("UnityEngine.TextCore.LowLevel.GlyphRenderMode, UnityEngine");
                _fontEngineType = Type.GetType("UnityEngine.TextCore.LowLevel.FontEngine, UnityEngine.TextCoreFontEngineModule");

                if (_fontEngineType != null)
                {
                    _fontEngineInit = _fontEngineType.GetMethod("InitializeFontEngine",
                        BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                    _fontEngineLoadPath = _fontEngineType.GetMethod("LoadFontFace",
                        BindingFlags.Public | BindingFlags.Static, null,
                        new Type[] { typeof(string), typeof(int) }, null);
                    _fontEngineLoadFont = _fontEngineType.GetMethod("LoadFontFace",
                        BindingFlags.Public | BindingFlags.Static, null,
                        new Type[] { typeof(Font), typeof(int) }, null);
                }

                if (_tmpFontAssetType != null)
                {
                    _createSimple = _tmpFontAssetType.GetMethod("CreateFontAsset",
                        BindingFlags.Public | BindingFlags.Static,
                        null, new Type[] { typeof(Font) }, null);
                    if (_glyphRenderType != null && _atlasModeType != null)
                    {
                        _createFull = _tmpFontAssetType.GetMethod("CreateFontAsset",
                            BindingFlags.Public | BindingFlags.Static,
                            null,
                            new Type[]
                            {
                                typeof(Font), typeof(int), typeof(int), _glyphRenderType,
                                typeof(int), typeof(int), _atlasModeType, typeof(bool)
                            },
                            null);
                    }
                    _tryAddString = _tmpFontAssetType.GetMethod("TryAddCharacters",
                        BindingFlags.Public | BindingFlags.Instance,
                        null, new Type[] { typeof(string), typeof(bool) }, null);
                    _assetFallbackProp = _tmpFontAssetType.GetProperty("fallbackFontAssetTable",
                        BindingFlags.Public | BindingFlags.Instance);
                    _atlasModeProp = _tmpFontAssetType.GetProperty("atlasPopulationMode",
                        BindingFlags.Public | BindingFlags.Instance);
                    _sourceFontField = _tmpFontAssetType.GetField("m_SourceFontFile",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                }

                if (_tmpSettingsType != null)
                {
                    _settingsFallbackProp = _tmpSettingsType.GetProperty("fallbackFontAssets",
                        BindingFlags.Public | BindingFlags.Static);
                }

                if (_tmpTextType != null)
                {
                    _tmpFontProp = _tmpTextType.GetProperty("font",
                        BindingFlags.Public | BindingFlags.Instance);
                    _tmpTextProp = _tmpTextType.GetProperty("text",
                        BindingFlags.Public | BindingFlags.Instance);
                }
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("ChineseTmpFontService resolve: " + ex.Message);
            }
        }

        private static void EnsureSourceFont()
        {
            if (_sourceFont != null)
                return;
            ResolveTypes();

            TryInitFontEngine();

            string[] paths = new string[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "msyh.ttc"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "msyhbd.ttc"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "simhei.ttf"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts", "Deng.ttf")
            };
            for (int i = 0; i < paths.Length; i++)
            {
                if (!File.Exists(paths[i]))
                    continue;
                try
                {
                    if (_fontEngineLoadPath != null)
                    {
                        object err = _fontEngineLoadPath.Invoke(null, new object[] { paths[i], 90 });
                        // FontEngineError.Success == 0
                        if (err is Enum && Convert.ToInt32(err) == 0)
                        {
                            _sourceName = "File:" + Path.GetFileName(paths[i]);
                            break;
                        }
                    }
                }
                catch { }
            }

            string[] names = new string[]
            {
                "Microsoft YaHei UI",
                "Microsoft YaHei",
                "SimHei",
                "Noto Sans SC",
                "DengXian"
            };
            for (int i = 0; i < names.Length; i++)
            {
                try
                {
                    Font f = Font.CreateDynamicFontFromOSFont(names[i], 18);
                    if (f == null)
                        continue;
                    if (_fontEngineLoadFont != null)
                    {
                        try { _fontEngineLoadFont.Invoke(null, new object[] { f, 90 }); }
                        catch { }
                    }
                    _sourceFont = f;
                    if (string.IsNullOrEmpty(_sourceName))
                        _sourceName = "OS:" + names[i];
                    else
                        _sourceName = _sourceName + "+" + names[i];
                    break;
                }
                catch { }
            }

            if (_sourceFont == null)
            {
                _sourceFont = ChineseFontPatch.CjkFont;
                _sourceName = "ChineseFontPatch:" + ChineseFontPatch.Source;
            }

            if (Plugin.Log != null && _sourceFont != null)
                Plugin.Log.LogInfo("ChineseTmpFontService source: " + _sourceName);
        }

        private static void TryInitFontEngine()
        {
            if (_fontEngineInit == null)
                return;
            try { _fontEngineInit.Invoke(null, null); }
            catch { }
        }

        private static void PatchAllFontAssets()
        {
            if (_tmpFontAssetType == null || _sourceFont == null)
                return;
            UnityEngine.Object[] all;
            try { all = Resources.FindObjectsOfTypeAll(_tmpFontAssetType); }
            catch { return; }
            if (all == null)
                return;

            int newly = 0;
            for (int i = 0; i < all.Length; i++)
            {
                UnityEngine.Object o = all[i];
                if (o == null || ReferenceEquals(o, _cjkTmpAsset))
                    continue;
                if (PatchOneFontAsset(o))
                    newly++;
            }

            if (!_loggedPatch && Plugin.Log != null && (_patchCount > 0 || newly > 0))
            {
                _loggedPatch = true;
                Plugin.Log.LogInfo("ChineseTmpFontService: Dynamic+YaHei patched TMP fonts="
                    + _patchCount + " (" + _sourceName + ")");
            }
        }

        private static bool PatchOneFontAsset(object fontAsset)
        {
            if (fontAsset == null || _sourceFont == null)
                return false;
            UnityEngine.Object uo = fontAsset as UnityEngine.Object;
            if (uo == null)
                return false;
            int id = uo.GetInstanceID();
            if (PatchedAssetIds.Contains(id))
                return false;

            try
            {
                if (_atlasModeProp != null && _atlasModeType != null)
                {
                    object dynamic = Enum.Parse(_atlasModeType, "Dynamic");
                    object cur = _atlasModeProp.GetValue(fontAsset, null);
                    if (!Equals(cur, dynamic))
                        _atlasModeProp.SetValue(fontAsset, dynamic, null);
                }
                if (_sourceFontField != null)
                    _sourceFontField.SetValue(fontAsset, _sourceFont);

                // Enable multi-atlas if present
                try
                {
                    PropertyInfo multi = _tmpFontAssetType.GetProperty("isMultiAtlasTexturesEnabled",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (multi != null && multi.CanWrite)
                        multi.SetValue(fontAsset, true, null);
                }
                catch { }

                PatchedAssetIds.Add(id);
                _patchCount++;
                // Warm common menu glyphs so first-frame tofu is less likely.
                TryAddChars(fontAsset,
                    "单人游戏多人设置百科创意工坊退出按键绑定操控俯仰偏航滚转油门刹车起落架雷达武器干扰弹视角地图聊天确认取消");
                return true;
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null && _createFailLogged < 5)
                {
                    _createFailLogged++;
                    Plugin.Log.LogWarning("ChineseTmpFontService patch: " + ex.Message);
                }
                return false;
            }
        }

        private static void TryAddChars(object fontAsset, string text)
        {
            if (fontAsset == null || _tryAddString == null || string.IsNullOrEmpty(text))
                return;
            if (!ContainsCjk(text))
                return;
            try
            {
                _tryAddString.Invoke(fontAsset, new object[] { text, true });
            }
            catch { }
        }

        private static void EnsureCreatedFallbackAsset()
        {
            if (_cjkTmpAsset != null || _sourceFont == null || _tmpFontAssetType == null)
                return;
            try
            {
                object asset = null;
                if (_createFull != null && _glyphRenderType != null && _atlasModeType != null)
                {
                    object sdfaa = Enum.Parse(_glyphRenderType, "SDFAA");
                    object dynamic = Enum.Parse(_atlasModeType, "Dynamic");
                    asset = _createFull.Invoke(null, new object[]
                    {
                        _sourceFont, 90, 9, sdfaa, 1024, 1024, dynamic, true
                    });
                }
                if (asset == null && _createSimple != null)
                    asset = _createSimple.Invoke(null, new object[] { _sourceFont });

                if (asset == null)
                {
                    if (!_loggedCreate && Plugin.Log != null)
                    {
                        _loggedCreate = true;
                        Plugin.Log.LogWarning(
                            "ChineseTmpFontService: CreateFontAsset returned null — using Dynamic patch only");
                    }
                    return;
                }

                UnityEngine.Object uo = asset as UnityEngine.Object;
                if (uo != null)
                    uo.hideFlags = HideFlags.DontSave;
                _cjkTmpAsset = asset;
                if (Plugin.Log != null)
                    Plugin.Log.LogInfo("ChineseTmpFontService: CreateFontAsset OK (" + _sourceName + ")");
            }
            catch (Exception ex)
            {
                if (!_loggedCreate && Plugin.Log != null)
                {
                    _loggedCreate = true;
                    Plugin.Log.LogWarning("ChineseTmpFontService CreateFontAsset: " + ex.Message);
                }
            }
        }

        private static void InjectGlobalFallback()
        {
            if (_settingsFallbackProp == null || _cjkTmpAsset == null)
                return;
            try
            {
                object listObj = _settingsFallbackProp.GetValue(null, null);
                if (listObj == null)
                {
                    Type listType = typeof(List<>).MakeGenericType(_tmpFontAssetType);
                    listObj = Activator.CreateInstance(listType);
                    _settingsFallbackProp.SetValue(null, listObj, null);
                }
                EnsureListContains(listObj as IList, _cjkTmpAsset);
            }
            catch { }
        }

        private static void AddFallback(object fontAsset, object fallback)
        {
            if (fontAsset == null || fallback == null || _assetFallbackProp == null)
                return;
            try
            {
                object listObj = _assetFallbackProp.GetValue(fontAsset, null);
                if (listObj == null)
                {
                    Type listType = typeof(List<>).MakeGenericType(_tmpFontAssetType);
                    listObj = Activator.CreateInstance(listType);
                    _assetFallbackProp.SetValue(fontAsset, listObj, null);
                }
                EnsureListContains(listObj as IList, fallback);
            }
            catch { }
        }

        private static void AssignFontsOnChineseTmpTexts()
        {
            if (!UiLang.IsChinese || _tmpTextType == null || _tmpFontProp == null || _tmpTextProp == null)
                return;
            UnityEngine.Object[] objs;
            try { objs = UnityEngine.Object.FindObjectsOfType(_tmpTextType); }
            catch { return; }
            if (objs == null)
                return;
            int budget = 48;
            for (int i = 0; i < objs.Length && budget > 0; i++)
            {
                UnityEngine.Object o = objs[i];
                if (o == null)
                    continue;
                int id = o.GetInstanceID();
                if (AssignedTextIds.Contains(id))
                    continue;
                string t;
                try { t = _tmpTextProp.GetValue(o, null) as string; }
                catch { continue; }
                if (string.IsNullOrEmpty(t) || !ContainsCjk(t))
                    continue;
                ApplyToTmpInstance(o);
                budget--;
            }
            if (AssignedTextIds.Count > 4000)
                AssignedTextIds.Clear();
        }

        private static bool ContainsCjk(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c >= 0x4E00 && c <= 0x9FFF)
                    return true;
            }
            return false;
        }

        private static bool EnsureListContains(IList list, object asset)
        {
            if (list == null || asset == null)
                return false;
            for (int i = 0; i < list.Count; i++)
            {
                if (ReferenceEquals(list[i], asset))
                    return true;
            }
            list.Add(asset);
            return true;
        }
    }
}
