using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace Oritasy
{
    internal static class WeXonBridge
    {
        private static bool _resolved;
        private static bool _available;
        private static ConfigEntry<bool> _multiMode;
        private static ConfigEntry<bool> _iff;
        private static ConfigEntry<bool> _freeHunt;
        private static ConfigEntry<bool> _ialHalfBlast;
        private static MethodInfo _refreshIalBlast;
        private static ConfigEntry<KeyCode> _arsenalPrev;
        private static ConfigEntry<KeyCode> _arsenalNext;
        private static ConfigEntry<KeyCode> _scoreboardKey;

        internal static ConfigEntry<KeyCode> ArsenalPrevKey
        {
            get { Resolve(); return _arsenalPrev; }
        }

        internal static ConfigEntry<KeyCode> ArsenalNextKey
        {
            get { Resolve(); return _arsenalNext; }
        }

        internal static ConfigEntry<KeyCode> ScoreboardKey
        {
            get { Resolve(); return _scoreboardKey; }
        }

        internal static bool Available
        {
            get
            {
                Resolve();
                return _available;
            }
        }

        private static void Resolve()
        {
            // Retry until WeXon.Awake has bound ConfigEntry fields (merged DLL load order varies).
            if (_resolved && _available)
            {
                if (_arsenalPrev == null || _arsenalNext == null || _scoreboardKey == null)
                    TryBindWeXonKeys();
                return;
            }
            try
            {
                Type t = null;
                Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < asms.Length; i++)
                {
                    try { t = asms[i].GetType("WeXon.Plugin"); }
                    catch { t = null; }
                    if (t != null)
                        break;
                }
                if (t == null)
                {
                    _resolved = true;
                    _available = false;
                    return;
                }
                BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                _multiMode = GetEntry<bool>(t, "EnableMultiMode", flags);
                _iff = GetEntry<bool>(t, "EnableIff", flags);
                _freeHunt = GetEntry<bool>(t, "AllowFreeAttack", flags);
                _ialHalfBlast = GetEntry<bool>(t, "IalHalfBlastRange", flags);
                _refreshIalBlast = t.GetMethod("RefreshIalBlastYield", flags);
                TryBindWeXonKeysFrom(t.Assembly);
                _available = _multiMode != null || _iff != null || _freeHunt != null;
                // Only lock failure after WeXon type exists but entries still null for a while —
                // keep retrying while entries are null (Awake not run yet).
                if (_available)
                    _resolved = true;
            }
            catch
            {
                _available = false;
            }
        }

        private static void TryBindWeXonKeys()
        {
            try
            {
                Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < asms.Length; i++)
                {
                    Type t = null;
                    try { t = asms[i].GetType("WeXon.StrategicArsenal"); }
                    catch { t = null; }
                    if (t != null)
                    {
                        TryBindWeXonKeysFrom(t.Assembly);
                        return;
                    }
                }
            }
            catch { }
        }

        private static void TryBindWeXonKeysFrom(Assembly asm)
        {
            if (asm == null)
                return;
            BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            if (_arsenalPrev == null || _arsenalNext == null)
            {
                Type arsenal = asm.GetType("WeXon.StrategicArsenal");
                if (arsenal != null)
                {
                    if (_arsenalPrev == null)
                        _arsenalPrev = GetEntry<KeyCode>(arsenal, "_prevKey", flags);
                    if (_arsenalNext == null)
                        _arsenalNext = GetEntry<KeyCode>(arsenal, "_nextKey", flags);
                }
            }
            if (_scoreboardKey == null)
            {
                Type board = asm.GetType("WeXon.MatchScoreboard");
                if (board != null)
                    _scoreboardKey = GetEntry<KeyCode>(board, "_key", flags);
            }
        }

        private static ConfigEntry<T> GetEntry<T>(Type t, string name, BindingFlags flags)
        {
            FieldInfo f = t.GetField(name, flags);
            if (f == null)
                return null;
            try { return f.GetValue(null) as ConfigEntry<T>; }
            catch { return null; }
        }

        internal static void DrawToggles()
        {
            if (!Available)
            {
                GUILayout.Label("(WeXon not loaded — missile toggles unavailable)");
                return;
            }

            ToggleEntry(UiLangPair("Missile multi-mode", "导弹多模式"), _multiMode);
            ToggleEntry(UiLangPair("IFF (no friendly lock)", "IFF（禁止友军锁定）"), _iff);
            ToggleEntry(UiLangPair("Auto search / free-hunt", "自动搜索 / 自由猎杀"), _freeHunt);
            if (_ialHalfBlast != null)
            {
                bool prev = _ialHalfBlast.Value;
                bool next = GUILayout.Toggle(prev,
                    UiLangPair("IAL nuke blast range ×0.5", "IAL 核弹爆炸半径 ×0.5"));
                if (next != prev)
                {
                    _ialHalfBlast.Value = next;
                    if (_refreshIalBlast != null)
                    {
                        try { _refreshIalBlast.Invoke(null, null); }
                        catch { }
                    }
                }
            }
        }

        private static string UiLangPair(string en, string zh)
        {
            try { return UiLang.T(en, zh); }
            catch { return en; }
        }

        private static void ToggleEntry(string label, ConfigEntry<bool> entry)
        {
            if (entry == null)
                return;
            bool v = GUILayout.Toggle(entry.Value, label);
            if (v != entry.Value)
            {
                entry.Value = v;
                // BepInEx persists ConfigEntry on set; MultiModeBrain reads EnableMultiMode each tick.
                try
                {
                    if (Plugin.Instance != null)
                        Plugin.Instance.Config.Save();
                }
                catch { }
                try
                {
                    // WeXon entries may live in a standalone com.iallemege.wexon.cfg.
                    foreach (KeyValuePair<string, BepInEx.PluginInfo> kv in BepInEx.Bootstrap.Chainloader.PluginInfos)
                    {
                        if (kv.Key == null || kv.Value == null || kv.Value.Instance == null)
                            continue;
                        if (kv.Key.IndexOf("wexon", StringComparison.OrdinalIgnoreCase) < 0
                            && kv.Value.Instance.GetType().FullName != "WeXon.Plugin")
                            continue;
                        kv.Value.Instance.Config.Save();
                    }
                }
                catch { }
            }
        }
    }
}
