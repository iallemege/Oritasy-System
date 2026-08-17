using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Oritasy-owned Chinese TMP AssetBundle pack (0.0.9.80).
    /// Single UnityFS: plugins/OritasyFonts/OritasyCJK (TMP_FontAsset + atlas textures).
    /// Registers every TMP_FontAsset in that one bundle as TMP_Settings.fallbackFontAssets.
    /// Soft-reflection: no compile-time Unity.TextMeshPro / AssetBundleModule reference required.
    /// </summary>
    internal static class OritasyCjkAssetPack
    {
        private static bool _tried;
        private static bool _ready;
        private static bool _logged;
        private static object _fallbackFont; // primary TMP_FontAsset
        private static readonly List<object> _allFonts = new List<object>(4);
        private static object _bundle; // AssetBundle
        private static Type _tmpFontAssetType;
        private static Type _tmpSettingsType;
        private static Type _assetBundleType;
        private static PropertyInfo _settingsFallbackProp;
        private static PropertyInfo _assetTableProp;
        private static FieldInfo _assetFallbackField;
        private static MethodInfo _loadFromFile;
        private static MethodInfo _loadAllAssetsGeneric;
        private static MethodInfo _hasCharacters;
        private static MethodInfo _readFontDef;

        internal static bool IsReady
        {
            get { EnsureLoaded(); return _ready && _fallbackFont != null; }
        }

        internal static object FallbackFont
        {
            get { EnsureLoaded(); return _fallbackFont; }
        }

        internal static void EnsureLoaded()
        {
            if (_tried)
                return;
            _tried = true;
            ResolveTypes();
            if (_tmpFontAssetType == null || _loadFromFile == null)
            {
                LogWarn("TMP/AssetBundle types missing — cannot load OritasyCJK");
                return;
            }

            try
            {
                string path = FindBundlePath();
                if (string.IsNullOrEmpty(path))
                {
                    LogWarn("OritasyCJK AssetBundle not found under plugins/OritasyFonts/ (or JustInCase/)");
                    return;
                }

                LogInfo("Loading CJK AssetBundle: " + path);
                _bundle = _loadFromFile.Invoke(null, new object[] { path });
                if (_bundle == null)
                {
                    LogWarn("AssetBundle.LoadFromFile returned null");
                    return;
                }

                object fonts = LoadAllFontAssets(_bundle);
                int n = RegisterAllFontAssets(fonts);
                if (n <= 0 || _fallbackFont == null)
                {
                    LogWarn("No TMP_FontAsset inside OritasyCJK bundle");
                    return;
                }

                _ready = true;
                LogInfo("OritasyCJK TMP fallback registered (" + n + " font asset(s) from one bundle)");
            }
            catch (Exception ex)
            {
                LogWarn("OritasyCjkAssetPack: " + ex.Message);
            }
        }

        internal static void PatchHarmony(Harmony harmony)
        {
            if (harmony == null)
                return;
            EnsureLoaded();
            if (!_ready || _fallbackFont == null)
                return;

            try
            {
                // TMP_FontAsset.Awake — attach our fallback to every font's table
                MethodInfo awake = null;
                if (_tmpFontAssetType != null)
                    awake = _tmpFontAssetType.GetMethod("Awake",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (awake != null)
                {
                    harmony.Patch(awake,
                        prefix: new HarmonyMethod(typeof(OritasyCjkTmpFontAwakePatch), "Prefix"));
                }
            }
            catch (Exception ex)
            {
                LogWarn("TMP Awake patch: " + ex.Message);
            }

            try
            {
                // Nuclear Option strips glyphs missing from the primary font — keep Chinese.
                Type sh = AccessTools.TypeByName("StringHelper")
                    ?? Type.GetType("StringHelper, Assembly-CSharp");
                MethodInfo replace = null;
                if (sh != null)
                {
                    replace = AccessTools.Method(sh, "ReplaceCharactersNotInFont")
                        ?? sh.GetMethod("ReplaceCharactersNotInFont",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                }
                if (replace != null)
                {
                    harmony.Patch(replace,
                        prefix: new HarmonyMethod(typeof(OritasyCjkStringHelperPatch), "Prefix"));
                }
            }
            catch (Exception ex)
            {
                LogWarn("StringHelper patch: " + ex.Message);
            }
        }

        internal static void Tick()
        {
            if (!_ready)
                EnsureLoaded();
        }

        internal static void AttachToFontAsset(object tmpFontAsset)
        {
            if (tmpFontAsset == null || _fallbackFont == null)
                return;
            for (int i = 0; i < _allFonts.Count; i++)
            {
                if (ReferenceEquals(tmpFontAsset, _allFonts[i]))
                    return;
            }
            try
            {
                if (_assetTableProp != null)
                {
                    object list = _assetTableProp.GetValue(tmpFontAsset, null);
                    EnsureListContains(list as IList, _fallbackFont);
                }
                if (_assetFallbackField != null)
                {
                    object list = _assetFallbackField.GetValue(tmpFontAsset);
                    EnsureListContains(list as IList, _fallbackFont);
                }
            }
            catch { }
        }

        /// <summary>
        /// Harmony prefix for StringHelper.ReplaceCharactersNotInFont(string, TMP_FontAsset).
        /// Uses the OritasyCJK fallback so Chinese is not stripped to empty/boxes.
        /// </summary>
        internal static bool TryReplaceNotInFont(ref string __result, string text, object fontAsset)
        {
            if (string.IsNullOrEmpty(text))
            {
                __result = text;
                return false;
            }
            EnsureLoaded();
            object useFont = _fallbackFont != null ? _fallbackFont : fontAsset;
            if (useFont == null)
                return true; // run original

            try
            {
                if (_hasCharacters != null)
                {
                    object[] args = new object[] { text, null, true, false };
                    object okObj = _hasCharacters.Invoke(useFont, args);
                    bool ok = okObj is bool && (bool)okObj;
                    uint[] missing = args[1] as uint[];
                    if (ok || missing == null || missing.Length == 0)
                    {
                        __result = text;
                        return false;
                    }
                    // Retry after ReadFontAssetDefinition
                    if (_readFontDef != null)
                    {
                        try { _readFontDef.Invoke(useFont, null); }
                        catch { }
                        args = new object[] { text, null, true, false };
                        okObj = _hasCharacters.Invoke(useFont, args);
                        ok = okObj is bool && (bool)okObj;
                        missing = args[1] as uint[];
                        if (ok || missing == null || missing.Length == 0)
                        {
                            __result = text;
                            return false;
                        }
                    }
                    // Strip only codepoints still missing from the CJK fallback.
                    HashSet<uint> miss = new HashSet<uint>(missing);
                    char[] chars = text.ToCharArray();
                    for (int i = 0; i < chars.Length; i++)
                    {
                        if (miss.Contains(chars[i]))
                            chars[i] = '\0';
                    }
                    __result = new string(chars).Replace("\0", "");
                    return false;
                }
            }
            catch { }
            // Prefer keeping original Chinese rather than tofu-stripping.
            __result = text;
            return false;
        }

        private static void ResolveTypes()
        {
            if (_tmpFontAssetType != null)
                return;
            try
            {
                _tmpFontAssetType = Type.GetType("TMPro.TMP_FontAsset, Unity.TextMeshPro");
                _tmpSettingsType = Type.GetType("TMPro.TMP_Settings, Unity.TextMeshPro");
                _assetBundleType = Type.GetType("UnityEngine.AssetBundle, UnityEngine.AssetBundleModule")
                    ?? Type.GetType("UnityEngine.AssetBundle, UnityEngine");

                if (_tmpSettingsType != null)
                {
                    _settingsFallbackProp = _tmpSettingsType.GetProperty("fallbackFontAssets",
                        BindingFlags.Public | BindingFlags.Static);
                }
                if (_tmpFontAssetType != null)
                {
                    _assetTableProp = _tmpFontAssetType.GetProperty("fallbackFontAssetTable",
                        BindingFlags.Public | BindingFlags.Instance);
                    _assetFallbackField = _tmpFontAssetType.GetField("fallbackFontAssets",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    _hasCharacters = _tmpFontAssetType.GetMethod("HasCharacters",
                        BindingFlags.Public | BindingFlags.Instance,
                        null,
                        new Type[] { typeof(string), typeof(uint[]).MakeByRefType(), typeof(bool), typeof(bool) },
                        null);
                    _readFontDef = _tmpFontAssetType.GetMethod("ReadFontAssetDefinition",
                        BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                }
                if (_assetBundleType != null)
                {
                    _loadFromFile = _assetBundleType.GetMethod("LoadFromFile",
                        BindingFlags.Public | BindingFlags.Static,
                        null, new Type[] { typeof(string) }, null);
                    // LoadAllAssets<TMP_FontAsset>()
                    MethodInfo[] methods = _assetBundleType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                    for (int i = 0; i < methods.Length; i++)
                    {
                        MethodInfo m = methods[i];
                        if (m.Name != "LoadAllAssets" || !m.IsGenericMethodDefinition)
                            continue;
                        if (m.GetParameters().Length != 0)
                            continue;
                        _loadAllAssetsGeneric = m;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                LogWarn("resolve: " + ex.Message);
            }
        }

        private static string FindBundlePath()
        {
            string plugins = null;
            try { plugins = Paths.PluginPath; }
            catch { plugins = null; }
            if (string.IsNullOrEmpty(plugins))
                return null;

            // Preferred: single Oritasy-owned UnityFS. Compat: JustInCase legacy filename only.
            string[] preferred = new string[]
            {
                Path.Combine(plugins, "OritasyFonts", "OritasyCJK"),
                Path.Combine(plugins, "OritasyCJK")
            };
            for (int i = 0; i < preferred.Length; i++)
            {
                if (File.Exists(preferred[i]))
                    return preferred[i];
            }

            // Optional third-party fallback (same UnityFS content historically).
            string justInCase = Path.Combine(plugins, "JustInCase", "NotoSansCJKsc-Regular");
            if (File.Exists(justInCase))
                return justInCase;

            try
            {
                string fontsDir = Path.Combine(plugins, "OritasyFonts");
                if (Directory.Exists(fontsDir))
                {
                    string[] hits = Directory.GetFiles(fontsDir, "OritasyCJK", SearchOption.AllDirectories);
                    if (hits != null && hits.Length > 0 && File.Exists(hits[0]))
                        return hits[0];
                }
            }
            catch { }
            return null;
        }

        private static object LoadAllFontAssets(object bundle)
        {
            if (bundle == null || _loadAllAssetsGeneric == null || _tmpFontAssetType == null)
                return null;
            MethodInfo closed = _loadAllAssetsGeneric.MakeGenericMethod(_tmpFontAssetType);
            return closed.Invoke(bundle, null);
        }

        /// <summary>Register every TMP_FontAsset from the one UnityFS (atlas deps load with them).</summary>
        private static int RegisterAllFontAssets(object fontsArray)
        {
            _allFonts.Clear();
            _fallbackFont = null;
            Array arr = fontsArray as Array;
            if (arr == null || arr.Length == 0)
                return 0;
            int n = 0;
            for (int i = 0; i < arr.Length; i++)
            {
                object font = arr.GetValue(i);
                if (font == null)
                    continue;
                if (_fallbackFont == null)
                    _fallbackFont = font;
                _allFonts.Add(font);
                RegisterGlobalFallback(font);
                n++;
            }
            return n;
        }

        private static void RegisterGlobalFallback(object font)
        {
            if (font == null || _settingsFallbackProp == null)
                return;
            object listObj = _settingsFallbackProp.GetValue(null, null);
            if (listObj == null)
            {
                Type listType = typeof(List<>).MakeGenericType(_tmpFontAssetType);
                listObj = Activator.CreateInstance(listType);
                _settingsFallbackProp.SetValue(null, listObj, null);
            }
            EnsureListContains(listObj as IList, font);
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

        private static void LogInfo(string msg)
        {
            if (_logged && msg != null && msg.StartsWith("Loading", StringComparison.Ordinal))
                return;
            if (Plugin.Log != null)
                Plugin.Log.LogInfo("OritasyCjkAssetPack: " + msg);
        }

        private static void LogWarn(string msg)
        {
            if (Plugin.Log != null)
                Plugin.Log.LogWarning("OritasyCjkAssetPack: " + msg);
        }
    }

    /// <summary>TMP_FontAsset.Awake — attach OritasyCJK as per-font fallback.</summary>
    internal static class OritasyCjkTmpFontAwakePatch
    {
        private static void Prefix(object __instance)
        {
            try
            {
                if (__instance == null || !OritasyCjkAssetPack.IsReady)
                    return;
                OritasyCjkAssetPack.AttachToFontAsset(__instance);
            }
            catch { }
        }
    }

    /// <summary>StringHelper.ReplaceCharactersNotInFont — keep CJK via Oritasy fallback font.</summary>
    internal static class OritasyCjkStringHelperPatch
    {
        // Harmony injects (ref __result, string text, TMP_FontAsset font) by type order.
        private static bool Prefix(ref string __result, string input, object font)
        {
            return OritasyCjkAssetPack.TryReplaceNotInFont(ref __result, input, font);
        }
    }
}
