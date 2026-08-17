using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Networking;

namespace Oritasy
{
    /// <summary>
    /// Dynamic in-game music: switch tracks by situation using Nuclear Option's own
    /// MusicManager clips, with optional overrides from plugins/OritasyMusic/.
    /// </summary>
    internal static class DynamicMusic
    {
        internal enum Mood
        {
            None = 0,
            Menu = 1,
            Start = 2,
            Tactical = 3,
            Strategic = 4,
            Combat = 5,
            Takeoff = 6,
            Victory = 7,
            Defeat = 8
        }

        private static readonly string[] MoodFolderNames = new string[]
        {
            "none", "menu", "start", "tactical", "strategic", "combat", "takeoff", "victory", "defeat"
        };

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> DynamicSwitch;
        internal static ConfigEntry<bool> PreferCustom;
        internal static ConfigEntry<float> FadeOutSec;
        internal static ConfigEntry<float> FadeInSec;
        internal static ConfigEntry<float> CombatRangeM;
        internal static ConfigEntry<float> StrategicAltM;
        internal static ConfigEntry<float> StartWindowSec;
        internal static ConfigEntry<float> TakeoffWindowSec;
        internal static ConfigEntry<float> EvalIntervalSec;
        internal static ConfigEntry<KeyCode> ReloadKey;

        private static readonly FieldInfo MenuMusicField =
            AccessTools.Field(typeof(MusicManager), "menuMusic");
        private static readonly FieldInfo CurrentSourceField =
            AccessTools.Field(typeof(MusicManager), "currentSource");
        private static readonly FieldInfo TakeoffMusicField =
            AccessTools.Field(typeof(AircraftParameters), "takeoffMusic");
        private static readonly FieldInfo KillSongField =
            AccessTools.Field(typeof(KillDisplay), "killsong");

        private static readonly Dictionary<Mood, List<AudioClip>> VanillaPools =
            new Dictionary<Mood, List<AudioClip>>();
        private static readonly Dictionary<Mood, List<AudioClip>> CustomPools =
            new Dictionary<Mood, List<AudioClip>>();
        private static readonly Dictionary<string, AudioClip> CustomByPath =
            new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<AudioClip, Mood> VanillaClipMood =
            new Dictionary<AudioClip, Mood>();

        private static DynamicMusicHost _host;
        private static Mood _currentMood = Mood.None;
        private static AudioClip _currentClip;
        private static float _nextEval;
        private static float _missionEnterTime = -1f;
        private static float _airborneSince = -1f;
        private static GameState _lastGameState = GameState.Uninitialized;
        private static bool _internalPlay;
        private static bool _scanQueued;
        private static int _customLoadGen;
        private static string _status = "idle";
        private static string _musicRoot;

        internal static Mood CurrentMood { get { return _currentMood; } }
        internal static string StatusLine { get { return _status; } }
        internal static string MusicRoot { get { return _musicRoot; } }

        internal static void Bind(ConfigFile cfg)
        {
            // Beta opt-in — primary toggle lives on Oritasy Profile (Experimental).
            Enabled = cfg.Bind("Music", "Enabled", false,
                "BETA: dynamic / custom music. Off by default — enable in Oritasy Profile → Experimental.");
            DynamicSwitch = cfg.Bind("Music", "DynamicSwitch", true,
                "When music beta is on: auto cross-fade by situation (menu/start/tactical/strategic/combat/takeoff/victory/defeat).");
            PreferCustom = cfg.Bind("Music", "PreferCustom", true,
                "If a OritasyMusic/<mood>/ file exists, use it instead of the game's clip for that mood.");
            FadeOutSec = cfg.Bind("Music", "FadeOutSeconds", 2.2f,
                "Cross-fade out duration when switching mood.");
            FadeInSec = cfg.Bind("Music", "FadeInSeconds", 2.8f,
                "Cross-fade in duration when switching mood.");
            CombatRangeM = cfg.Bind("Music", "CombatRangeMeters", 12000f,
                "Hostile unit within this range → combat music.");
            StrategicAltM = cfg.Bind("Music", "StrategicAltitudeMeters", 3500f,
                "Above this altitude (and not in combat) → strategic music.");
            StartWindowSec = cfg.Bind("Music", "StartWindowSeconds", 55f,
                "After entering a mission, play start music for this long.");
            TakeoffWindowSec = cfg.Bind("Music", "TakeoffWindowSeconds", 40f,
                "After becoming airborne, prefer takeoff music for this long.");
            EvalIntervalSec = cfg.Bind("Music", "EvalIntervalSeconds", 1.25f,
                "How often to re-evaluate the music mood.");
            ReloadKey = cfg.Bind("Music", "ReloadKey", KeyCode.None,
                "Optional hotkey to reload custom music folder (None = use F1 menu only; F9 is Support / 支援).");

            _musicRoot = ResolveMusicRoot();
            EnsureMoodFolders();
            _status = Enabled != null && Enabled.Value
                ? "beta ON · " + _musicRoot
                : "beta OFF (enable in Profile)";
            if (Enabled != null && Enabled.Value)
            {
                EnsureHost();
                QueueCustomReload();
            }
        }

        internal static void Tick()
        {
            if (Enabled == null || !Enabled.Value)
                return;

            EnsureHost();

            if (ReloadKey != null && ReloadKey.Value != KeyCode.None && Input.GetKeyDown(ReloadKey.Value))
                QueueCustomReload();

            GameState gs = GameState.Uninitialized;
            try { gs = GameManager.gameState; }
            catch { }
            if (gs != _lastGameState)
            {
                if (IsMissionState(gs) && !IsMissionState(_lastGameState))
                    _missionEnterTime = Time.unscaledTime;
                if (!IsMissionState(gs))
                {
                    _missionEnterTime = -1f;
                    _airborneSince = -1f;
                }
                _lastGameState = gs;
                _scanQueued = true;
            }

            if (_scanQueued && Time.unscaledTime >= _nextEval)
            {
                _scanQueued = false;
                HarvestVanillaPools();
            }

            if (DynamicSwitch == null || !DynamicSwitch.Value)
                return;

            float interval = EvalIntervalSec != null ? Mathf.Max(0.35f, EvalIntervalSec.Value) : 1.25f;
            if (Time.unscaledTime < _nextEval)
                return;
            _nextEval = Time.unscaledTime + interval;

            Mood want = EvaluateMood();
            if (want == Mood.None)
                return;

            AudioClip clip = ResolveClip(want);
            if (clip == null)
            {
                _status = MoodName(want) + " (no clip)";
                return;
            }

            if (want == _currentMood && object.ReferenceEquals(clip, _currentClip) && IsMusicPlaying())
            {
                _status = MoodName(want) + " · " + ClipLabel(clip);
                return;
            }

            PlayMood(want, clip, false);
        }

        /// <summary>Career Profile → Experimental beta master switch.</summary>
        internal static void DrawProfileBetaToggle()
        {
            if (Enabled == null)
                return;

            GUILayout.Label(UiLang.T("Experimental (BETA)", "实验功能·BETA"),
                GUILayout.ExpandWidth(true));
            GUILayout.BeginHorizontal();
            GUILayout.Label(UiLang.T("Dynamic music", "动态音乐"), GUILayout.Width(140f));
            Color prev = GUI.backgroundColor;
            bool on = Enabled.Value;
            GUI.backgroundColor = on ? new Color(0.45f, 0.85f, 0.55f) : Color.white;
            if (GUILayout.Button(on ? UiLang.T("ON", "开") : UiLang.T("OFF", "关"),
                GUILayout.Width(90f), GUILayout.Height(26f)))
            {
                Enabled.Value = !on;
                on = !on;
                if (on)
                {
                    EnsureHost();
                    QueueCustomReload();
                    _nextEval = 0f;
                    _status = "beta enabled";
                }
                else
                    _status = "beta disabled";
            }
            GUI.backgroundColor = prev;
            GUILayout.Label(on ? UiLang.T("  [ON]", "  [开]") : UiLang.T("  [OFF]", "  [关]"),
                GUILayout.Width(56f));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label(
                on
                    ? UiLang.T(
                        "BETA ON: situation BGM + optional plugins/OritasyMusic/ overrides. Fine-tune in F1 → MUSIC.",
                        "BETA 开：情境 BGM + 可选 plugins/OritasyMusic/ 覆盖。可在 F1 → 音乐 微调。")
                    : UiLang.T(
                        "BETA OFF (default): vanilla game music only. Opt in to test dynamic switching.",
                        "BETA 关（默认）：仅原版音乐。开启后可测试动态切换。"),
                GUILayout.ExpandWidth(true));
        }

        /// <summary>F1 tools — only when Profile beta is enabled.</summary>
        internal static void DrawGuiToggles()
        {
            if (Enabled == null)
                return;

            GUILayout.Label(UiLang.T("MUSIC (BETA)", "音乐（BETA）"), AircraftManeuverGuiStyles.Section);
            if (!Enabled.Value)
            {
                GUILayout.Label(
                    UiLang.T(
                        "Disabled. Enable in main-menu Oritasy Profile → Experimental.",
                        "已关闭。请在主菜单 Oritasy 档案 → 实验功能 中开启。"),
                    AircraftManeuverGuiStyles.Label);
                return;
            }

            if (DynamicSwitch != null)
            {
                bool dyn = GUILayout.Toggle(DynamicSwitch.Value,
                    UiLang.T(" Auto switch by situation", " 按战况自动切换"));
                if (dyn != DynamicSwitch.Value)
                    DynamicSwitch.Value = dyn;
            }
            if (PreferCustom != null)
            {
                bool pref = GUILayout.Toggle(PreferCustom.Value, " Prefer folder overrides");
                if (pref != PreferCustom.Value)
                    PreferCustom.Value = pref;
            }

            GUILayout.Label("Folder: plugins/OritasyMusic/[mood]/  (.ogg .wav .mp3)",
                AircraftManeuverGuiStyles.Label);
            GUILayout.Label("moods: menu start tactical strategic combat takeoff victory defeat",
                AircraftManeuverGuiStyles.Label);
            GUILayout.Label("Now: " + MoodName(_currentMood) + "  |  " + _status,
                AircraftManeuverGuiStyles.Label);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reload music", AircraftManeuverGuiStyles.Button, GUILayout.Height(28f)))
                QueueCustomReload();
            if (GUILayout.Button("Open folder", AircraftManeuverGuiStyles.Button, GUILayout.Height(28f)))
                OpenMusicFolder();
            if (GUILayout.Button("Rescan game tracks", AircraftManeuverGuiStyles.Button, GUILayout.Height(28f)))
            {
                HarvestVanillaPools();
                _status = "rescanned vanilla pools";
            }
            GUILayout.EndHorizontal();
            if (ReloadKey != null && ReloadKey.Value != KeyCode.None)
                GUILayout.Label(ReloadKey.Value.ToString() + " also reloads custom files", AircraftManeuverGuiStyles.Label);
        }

        internal static void OpenMusicFolder()
        {
            try
            {
                EnsureMoodFolders();
                if (!string.IsNullOrEmpty(_musicRoot) && Directory.Exists(_musicRoot))
                    Application.OpenURL("file:///" + _musicRoot.Replace('\\', '/'));
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("Open music folder: " + ex.Message);
            }
        }

        internal static void QueueCustomReload()
        {
            if (Enabled == null || !Enabled.Value)
                return;
            EnsureHost();
            if (_host != null)
                _host.StartCoroutine(LoadCustomRoutine(++_customLoadGen));
        }

        private static void EnsureHost()
        {
            if (_host != null)
                return;
            try
            {
                GameObject go = new GameObject("OritasyDynamicMusic");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _host = go.AddComponent<DynamicMusicHost>();
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("DynamicMusic host: " + ex.Message);
            }
        }

        private static string ResolveMusicRoot()
        {
            try
            {
                string plugins = Paths.PluginPath;
                if (!string.IsNullOrEmpty(plugins))
                    return Path.Combine(plugins, "OritasyMusic");
            }
            catch { }
            try
            {
                string loc = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
                if (!string.IsNullOrEmpty(loc))
                    return Path.Combine(loc, "OritasyMusic");
            }
            catch { }
            return Path.Combine(Directory.GetCurrentDirectory(), "BepInEx", "plugins", "OritasyMusic");
        }

        private static void EnsureMoodFolders()
        {
            try
            {
                if (string.IsNullOrEmpty(_musicRoot))
                    _musicRoot = ResolveMusicRoot();
                if (!Directory.Exists(_musicRoot))
                    Directory.CreateDirectory(_musicRoot);

                for (int i = 1; i < MoodFolderNames.Length; i++)
                {
                    string sub = Path.Combine(_musicRoot, MoodFolderNames[i]);
                    if (!Directory.Exists(sub))
                        Directory.CreateDirectory(sub);
                }

                string readme = Path.Combine(_musicRoot, "README.txt");
                if (!File.Exists(readme))
                {
                    File.WriteAllText(readme,
                        "Oritasy dynamic music overrides\r\n"
                        + "==============================\r\n"
                        + "Put .ogg / .wav / .mp3 files into a mood folder:\r\n"
                        + "  menu, start, tactical, strategic, combat, takeoff, victory, defeat\r\n\r\n"
                        + "If a folder has files, those tracks replace the game's music for that situation.\r\n"
                        + "Empty folders = keep Nuclear Option's built-in tracks (auto-switched).\r\n"
                        + "In-game: F1 → MUSIC → Reload music after adding files.\r\n");
                }
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("EnsureMoodFolders: " + ex.Message);
            }
        }

        private static IEnumerator LoadCustomRoutine(int gen)
        {
            if (Enabled == null || !Enabled.Value)
            {
                _status = "beta OFF";
                yield break;
            }
            EnsureMoodFolders();
            _status = "loading custom…";

            // Drop previous custom clips
            List<AudioClip> old = new List<AudioClip>();
            foreach (KeyValuePair<string, AudioClip> kv in CustomByPath)
            {
                if (kv.Value != null)
                    old.Add(kv.Value);
            }
            CustomByPath.Clear();
            CustomPools.Clear();

            int loaded = 0;
            for (int mi = 1; mi < MoodFolderNames.Length; mi++)
            {
                if (gen != _customLoadGen)
                    yield break;

                Mood mood = (Mood)mi;
                string folder = Path.Combine(_musicRoot, MoodFolderNames[mi]);
                if (!Directory.Exists(folder))
                    continue;

                string[] files = null;
                try
                {
                    files = Directory.GetFiles(folder);
                }
                catch { continue; }
                if (files == null)
                    continue;

                for (int fi = 0; fi < files.Length; fi++)
                {
                    if (gen != _customLoadGen)
                        yield break;

                    string path = files[fi];
                    string ext = Path.GetExtension(path);
                    if (string.IsNullOrEmpty(ext))
                        continue;
                    ext = ext.ToLowerInvariant();
                    if (ext != ".ogg" && ext != ".wav" && ext != ".mp3")
                        continue;

                    AudioClip clip = null;
                    if (ext == ".wav")
                    {
                        if (Enabled == null || !Enabled.Value)
                            yield break;
                        IEnumerator wavLoad = OritasyWavPcm.LoadClip(path, delegate(AudioClip c) { clip = c; });
                        while (wavLoad.MoveNext())
                        {
                            if (gen != _customLoadGen)
                                yield break;
                            yield return wavLoad.Current;
                        }
                    }
                    else
                    {
                        string uri = "file:///" + path.Replace('\\', '/');
                        AudioType at = ext == ".ogg" ? AudioType.OGGVORBIS : AudioType.MPEG;
                        using (UnityWebRequest req = UnityWebRequestMultimedia.GetAudioClip(uri, at))
                        {
                            try
                            {
                                DownloadHandlerAudioClip dh = req.downloadHandler as DownloadHandlerAudioClip;
                                if (dh != null)
                                    dh.streamAudio = false;
                            }
                            catch { }
                            yield return req.SendWebRequest();
                            if (gen != _customLoadGen)
                                yield break;
                            bool ok = false;
                            try
                            {
                                // Unity 2020+ Result; older builds expose isNetworkError / isHttpError.
                                ok = req.result == UnityWebRequest.Result.Success;
                            }
                            catch
                            {
                                ok = string.IsNullOrEmpty(req.error);
                            }
                            if (ok)
                            {
                                try { clip = DownloadHandlerAudioClip.GetContent(req); }
                                catch { clip = null; }
                            }
                            else if (Plugin.Log != null)
                            {
                                Plugin.Log.LogWarning("Music load fail " + Path.GetFileName(path) + ": " + req.error);
                            }
                        }
                    }

                    if (clip == null)
                        continue;
                    clip.name = "OritasyMusic_" + MoodFolderNames[mi] + "_" + Path.GetFileNameWithoutExtension(path);
                    CustomByPath[path] = clip;
                    List<AudioClip> pool;
                    if (!CustomPools.TryGetValue(mood, out pool) || pool == null)
                    {
                        pool = new List<AudioClip>();
                        CustomPools[mood] = pool;
                    }
                    pool.Add(clip);
                    loaded++;
                    yield return null;
                }
            }

            for (int i = 0; i < old.Count; i++)
            {
                try { UnityEngine.Object.Destroy(old[i]); }
                catch { }
            }

            _status = "custom loaded: " + loaded + " file(s)";
            if (Plugin.Log != null)
                Plugin.Log.LogInfo("Oritasy music: " + _status + " @ " + _musicRoot);
            _scanQueued = true;
            _nextEval = 0f;
        }

        private static void HarvestVanillaPools()
        {
            ClearVanilla();

            MusicManager mm = GetMusicManager();
            if (mm != null && MenuMusicField != null)
            {
                try
                {
                    AudioClip menu = MenuMusicField.GetValue(mm) as AudioClip;
                    AddVanilla(Mood.Menu, menu);
                }
                catch { }
            }

            Faction faction = null;
            try
            {
                Faction f;
                if (GameManager.GetLocalFaction(out f))
                    faction = f;
            }
            catch { }

            try
            {
                MapSettings[] maps = Resources.FindObjectsOfTypeAll<MapSettings>();
                if (maps != null)
                {
                    for (int i = 0; i < maps.Length; i++)
                    {
                        MapSettings ms = maps[i];
                        if (ms == null)
                            continue;
                        if (faction != null)
                        {
                            AddVanilla(Mood.Start, SafeGetStart(ms, faction));
                            AddVanilla(Mood.Tactical, SafeGetTactical(ms, faction));
                            AddVanilla(Mood.Strategic, SafeGetStrategic(ms, faction));
                        }
                        // Also pull whatever is serialized for other factions.
                        HarvestMapMusicFields(ms);
                    }
                }
            }
            catch { }

            try
            {
                Aircraft ac;
                if (GameManager.GetLocalAircraft(out ac) && ac != null)
                {
                    AircraftParameters ap = null;
                    try { ap = ac.GetAircraftParameters(); }
                    catch { }
                    if (ap != null)
                    {
                        AudioClip takeoff = null;
                        try { takeoff = ap.takeoffMusic; }
                        catch
                        {
                            if (TakeoffMusicField != null)
                                takeoff = TakeoffMusicField.GetValue(ap) as AudioClip;
                        }
                        AddVanilla(Mood.Takeoff, takeoff);
                        AddVanilla(Mood.Start, takeoff);
                    }
                }
            }
            catch { }

            try
            {
                KillDisplay[] kds = Resources.FindObjectsOfTypeAll<KillDisplay>();
                if (kds != null && KillSongField != null)
                {
                    for (int i = 0; i < kds.Length; i++)
                    {
                        if (kds[i] == null)
                            continue;
                        AudioClip ks = KillSongField.GetValue(kds[i]) as AudioClip;
                        AddVanilla(Mood.Victory, ks);
                    }
                }
            }
            catch { }

            // Fallbacks so every mood has something if the game exposed any music at all.
            FillFallback(Mood.Tactical, Mood.Start, Mood.Menu);
            FillFallback(Mood.Strategic, Mood.Tactical, Mood.Start);
            FillFallback(Mood.Combat, Mood.Tactical, Mood.Strategic);
            FillFallback(Mood.Takeoff, Mood.Start, Mood.Tactical);
            FillFallback(Mood.Victory, Mood.Strategic, Mood.Tactical);
            FillFallback(Mood.Defeat, Mood.Menu, Mood.Tactical);
            FillFallback(Mood.Start, Mood.Tactical, Mood.Menu);

            int n = 0;
            foreach (KeyValuePair<Mood, List<AudioClip>> kv in VanillaPools)
            {
                if (kv.Value != null)
                    n += kv.Value.Count;
            }
            if (Plugin.DebugLog != null && Plugin.DebugLog.Value && Plugin.Log != null)
                Plugin.Log.LogInfo("Oritasy music: harvested " + n + " vanilla clip slot(s)");
        }

        private static void HarvestMapMusicFields(MapSettings ms)
        {
            try
            {
                FieldInfo arrField = AccessTools.Field(typeof(MapSettings), "factionMusic");
                if (arrField == null)
                    return;
                Array arr = arrField.GetValue(ms) as Array;
                if (arr == null)
                    return;
                for (int i = 0; i < arr.Length; i++)
                {
                    object entry = arr.GetValue(i);
                    if (entry == null)
                        continue;
                    Type et = entry.GetType();
                    AddVanilla(Mood.Start, AccessTools.Field(et, "startMusic").GetValue(entry) as AudioClip);
                    AddVanilla(Mood.Tactical, AccessTools.Field(et, "tacticalMusic").GetValue(entry) as AudioClip);
                    AddVanilla(Mood.Strategic, AccessTools.Field(et, "strategicMusic").GetValue(entry) as AudioClip);
                }
            }
            catch { }
        }

        private static AudioClip SafeGetStart(MapSettings ms, Faction f)
        {
            try { return ms.GetStartMusic(f); }
            catch { return null; }
        }

        private static AudioClip SafeGetTactical(MapSettings ms, Faction f)
        {
            try { return ms.GetTacticalMusic(f); }
            catch { return null; }
        }

        private static AudioClip SafeGetStrategic(MapSettings ms, Faction f)
        {
            try { return ms.GetStrategicMusic(f); }
            catch { return null; }
        }

        private static void ClearVanilla()
        {
            VanillaPools.Clear();
            VanillaClipMood.Clear();
        }

        private static void AddVanilla(Mood mood, AudioClip clip)
        {
            if (clip == null)
                return;
            List<AudioClip> list;
            if (!VanillaPools.TryGetValue(mood, out list) || list == null)
            {
                list = new List<AudioClip>();
                VanillaPools[mood] = list;
            }
            if (!list.Contains(clip))
                list.Add(clip);
            if (!VanillaClipMood.ContainsKey(clip))
                VanillaClipMood[clip] = mood;
        }

        private static void FillFallback(Mood target, Mood a, Mood b)
        {
            if (PoolCount(VanillaPools, target) > 0)
                return;
            CopyPool(VanillaPools, a, target);
            if (PoolCount(VanillaPools, target) > 0)
                return;
            CopyPool(VanillaPools, b, target);
        }

        private static void CopyPool(Dictionary<Mood, List<AudioClip>> src, Mood from, Mood to)
        {
            List<AudioClip> list;
            if (!src.TryGetValue(from, out list) || list == null)
                return;
            for (int i = 0; i < list.Count; i++)
                AddVanilla(to, list[i]);
        }

        private static int PoolCount(Dictionary<Mood, List<AudioClip>> pools, Mood mood)
        {
            List<AudioClip> list;
            if (!pools.TryGetValue(mood, out list) || list == null)
                return 0;
            return list.Count;
        }

        private static Mood EvaluateMood()
        {
            GameState gs = GameState.Uninitialized;
            try { gs = GameManager.gameState; }
            catch { }

            GameResolution res = GameResolution.Ongoing;
            try { res = GameManager.gameResolution; }
            catch { }

            Aircraft ac = null;
            try { GameManager.GetLocalAircraft(out ac); }
            catch { }

            bool airborne = false;
            float alt = 0f;
            if (ac != null)
            {
                try
                {
                    alt = ac.transform.position.y;
                    Rigidbody rb = ac.GetComponent<Rigidbody>();
                    float spd = rb != null ? rb.velocity.magnitude : 0f;
                    airborne = alt > 25f && spd > 40f;
                }
                catch { }
            }

            if (airborne)
            {
                if (_airborneSince < 0f)
                    _airborneSince = Time.unscaledTime;
            }
            else
                _airborneSince = -1f;

            DynamicMusicMoodService.MoodInput input = new DynamicMusicMoodService.MoodInput();
            input.Victory = res == GameResolution.Victory;
            input.Defeat = res == GameResolution.Defeat;
            input.MenuLike = gs == GameState.Menu || gs == GameState.Encyclopedia || gs == GameState.Editor
                || gs == GameState.ServerWaiting || gs == GameState.Uninitialized;
            input.MissionRunning = IsMissionState(gs);
            input.HasLocalAircraft = ac != null;
            input.CombatNearby = ac != null && IsNearHostile(ac);
            input.Airborne = airborne;
            input.MissionAgeSec = _missionEnterTime > 0f ? (Time.unscaledTime - _missionEnterTime) : -1f;
            input.AirborneAgeSec = _airborneSince > 0f ? (Time.unscaledTime - _airborneSince) : -1f;
            input.StartWindowSec = StartWindowSec != null ? StartWindowSec.Value : 55f;
            input.TakeoffWindowSec = TakeoffWindowSec != null ? TakeoffWindowSec.Value : 40f;
            input.RadarAltM = alt;
            input.StrategicAltM = StrategicAltM != null ? StrategicAltM.Value : 3500f;

            DynamicMusicMoodService.MoodKind kind = DynamicMusicMoodService.Evaluate(input);
            return (Mood)(int)kind;
        }

        private static bool IsNearHostile(Aircraft ac)
        {
            if (ac == null)
                return false;
            FactionHQ myHq = null;
            try { myHq = ac.NetworkHQ; }
            catch { }
            if (myHq == null)
                return false;

            float range = CombatRangeM != null ? CombatRangeM.Value : 12000f;
            float rangeSq = range * range;
            Vector3 myPos = ac.transform.position;

            try
            {
                List<Aircraft> all = UnitRegistry.allAircraft;
                if (all != null)
                {
                    for (int i = 0; i < all.Count; i++)
                    {
                        Aircraft other = all[i];
                        if (other == null || object.ReferenceEquals(other, ac))
                            continue;
                        FactionHQ oh = null;
                        try { oh = other.NetworkHQ; }
                        catch { }
                        if (oh == null || object.ReferenceEquals(oh, myHq))
                            continue;
                        Vector3 d = other.transform.position - myPos;
                        if (d.sqrMagnitude <= rangeSq)
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static bool IsMissionState(GameState gs)
        {
            return gs == GameState.SinglePlayer || gs == GameState.Multiplayer;
        }

        private static AudioClip ResolveClip(Mood mood)
        {
            bool prefer = PreferCustom == null || PreferCustom.Value;
            if (prefer)
            {
                AudioClip c = PickFrom(CustomPools, mood);
                if (c != null)
                    return c;
            }
            AudioClip v = PickFrom(VanillaPools, mood);
            if (v != null)
                return v;
            return PickFrom(CustomPools, mood);
        }

        private static AudioClip PickFrom(Dictionary<Mood, List<AudioClip>> pools, Mood mood)
        {
            List<AudioClip> list;
            if (!pools.TryGetValue(mood, out list) || list == null || list.Count == 0)
                return null;
            if (list.Count == 1)
                return list[0];
            // Stable-ish pick: change when mood changes, otherwise stick.
            if (_currentMood == mood && _currentClip != null && list.Contains(_currentClip))
                return _currentClip;
            int idx = Mathf.Abs((Time.frameCount * 37 + (int)mood * 13)) % list.Count;
            return list[idx];
        }

        private static void PlayMood(Mood mood, AudioClip clip, bool force)
        {
            MusicManager mm = GetMusicManager();
            if (mm == null || clip == null)
                return;

            float fadeOut = FadeOutSec != null ? Mathf.Max(0.1f, FadeOutSec.Value) : 2.2f;
            float fadeIn = FadeInSec != null ? Mathf.Max(0.1f, FadeInSec.Value) : 2.8f;
            float priority = MoodPriority(mood);

            _internalPlay = true;
            try
            {
                mm.CrossFadeMusic(clip, fadeOut, fadeIn, true, true, true, priority);
            }
            catch
            {
                try { mm.PlayMusic(clip, true); }
                catch { }
            }
            _internalPlay = false;

            _currentMood = mood;
            _currentClip = clip;
            _status = MoodName(mood) + " · " + ClipLabel(clip);
            if (Plugin.DebugLog != null && Plugin.DebugLog.Value && Plugin.Log != null)
                Plugin.Log.LogInfo("Oritasy music → " + _status);
        }

        private static float MoodPriority(Mood mood)
        {
            return DynamicMusicMoodService.MoodPriority(
                (DynamicMusicMoodService.MoodKind)(int)mood);
        }

        private static MusicManager GetMusicManager()
        {
            try
            {
                if (MusicManager.i != null)
                    return MusicManager.i;
            }
            catch { }
            try
            {
                if (SoundManager.i != null && SoundManager.i.Music != null)
                    return SoundManager.i.Music;
            }
            catch { }
            try
            {
                return UnityEngine.Object.FindObjectOfType<MusicManager>();
            }
            catch { }
            return null;
        }

        private static bool IsMusicPlaying()
        {
            try
            {
                MusicManager mm = GetMusicManager();
                if (mm == null)
                    return false;
                if (mm.IsPlaying())
                    return true;
                if (CurrentSourceField != null)
                {
                    AudioSource src = CurrentSourceField.GetValue(mm) as AudioSource;
                    if (src != null && src.isPlaying)
                        return true;
                }
            }
            catch { }
            return false;
        }

        internal static bool TryRemapVanillaClip(ref AudioClip clip)
        {
            if (Enabled == null || !Enabled.Value)
                return false;
            if (PreferCustom != null && !PreferCustom.Value)
                return false;
            if (clip == null)
                return false;

            Mood mood = Mood.None;
            if (!VanillaClipMood.TryGetValue(clip, out mood) || mood == Mood.None)
            {
                // Unknown clip — if combat/menu director is active, leave it.
                return false;
            }

            AudioClip custom = PickFrom(CustomPools, mood);
            if (custom == null || object.ReferenceEquals(custom, clip))
                return false;
            clip = custom;
            return true;
        }

        internal static bool ShouldSuppressExternalPlay()
        {
            if (Enabled == null || !Enabled.Value)
                return false;
            if (DynamicSwitch == null || !DynamicSwitch.Value)
                return false;
            if (_internalPlay)
                return false;
            // Until the director has played once, let vanilla music through.
            return _currentMood != Mood.None && _currentClip != null;
        }

        internal static void ObserveExternalClip(AudioClip clip)
        {
            if (clip == null)
                return;
            // Learn unknown mission tracks into tactical pool.
            if (!VanillaClipMood.ContainsKey(clip))
                AddVanilla(Mood.Tactical, clip);
        }

        private static string MoodName(Mood mood)
        {
            int i = (int)mood;
            if (i >= 0 && i < MoodFolderNames.Length)
                return MoodFolderNames[i];
            return "none";
        }

        private static string ClipLabel(AudioClip clip)
        {
            if (clip == null)
                return "?";
            return string.IsNullOrEmpty(clip.name) ? "(unnamed)" : clip.name;
        }

    }

    /// <summary>Coroutine host (DontDestroyOnLoad).</summary>
    internal sealed class DynamicMusicHost : MonoBehaviour
    {
    }

    /// <summary>Shared IMGUI styles accessor so DynamicMusic can draw inside F1 without duplicating fields.</summary>
    internal static class AircraftManeuverGuiStyles
    {
        internal static GUIStyle Section
        {
            get { return AircraftManeuverGui.StyleSection(); }
        }

        internal static GUIStyle Label
        {
            get { return AircraftManeuverGui.StyleLabel(); }
        }

        internal static GUIStyle Button
        {
            get { return AircraftManeuverGui.StyleButton(); }
        }
    }

    [HarmonyPatch(typeof(MusicManager), "CrossFadeMusic")]
    internal static class Patch_MusicManager_CrossFadeMusic
    {
        private static bool Prefix(ref AudioClip audioClip)
        {
            if (audioClip != null)
                DynamicMusic.ObserveExternalClip(audioClip);
            DynamicMusic.TryRemapVanillaClip(ref audioClip);
            if (DynamicMusic.ShouldSuppressExternalPlay())
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(MusicManager), "PlayMusic")]
    internal static class Patch_MusicManager_PlayMusic
    {
        private static bool Prefix(ref AudioClip audioClip)
        {
            if (audioClip != null)
                DynamicMusic.ObserveExternalClip(audioClip);
            DynamicMusic.TryRemapVanillaClip(ref audioClip);
            if (DynamicMusic.ShouldSuppressExternalPlay())
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(MusicManager), "PlayMenuMusic")]
    internal static class Patch_MusicManager_PlayMenuMusic
    {
        private static bool Prefix()
        {
            // Let our director own menu music when dynamic switching is on.
            if (DynamicMusic.ShouldSuppressExternalPlay())
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(MusicManager), "QueueMusicClip")]
    internal static class Patch_MusicManager_QueueMusicClip
    {
        private static bool Prefix(ref AudioClip audioClip)
        {
            if (audioClip != null)
                DynamicMusic.ObserveExternalClip(audioClip);
            DynamicMusic.TryRemapVanillaClip(ref audioClip);
            if (DynamicMusic.ShouldSuppressExternalPlay())
                return false;
            return true;
        }
    }
}
