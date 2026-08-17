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
    /// Drop-in missile SFX overrides from plugins/OritasyMissileAudio/.
    /// Swaps AudioClips on spawn/fire when matching files exist; vanilla otherwise.
    /// </summary>
    internal static class MissileAudio
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<string> FolderPath;
        internal static ConfigEntry<float> VolumeScale;

        private static readonly FieldInfo FlightSoundField =
            AccessTools.Field(typeof(Missile), "flightSound");
        private static readonly FieldInfo NearbyDetonationField =
            AccessTools.Field(typeof(Missile), "nearbyDetonationClip");
        private static readonly FieldInfo MotorsField =
            AccessTools.Field(typeof(Missile), "motors");
        private static readonly FieldInfo InfoField =
            AccessTools.Field(typeof(Missile), "info");
        private static readonly FieldInfo DeploySoundField =
            AccessTools.Field(typeof(MountedMissile), "deploySound");

        private static readonly Type MotorType =
            AccessTools.Inner(typeof(Missile), "Motor");
        private static readonly FieldInfo MotorAudioSourcesField =
            MotorType != null ? AccessTools.Field(MotorType, "audioSources") : null;
        private static readonly FieldInfo MotorStartupField =
            MotorType != null ? AccessTools.Field(MotorType, "startupSource") : null;

        private static readonly string[] Categories = MissileAudioKeyService.Categories;

        // "normalizedKey|category" → clip
        private static readonly Dictionary<string, AudioClip> ClipIndex =
            new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, AudioClip> LoadedByPath =
            new Dictionary<string, AudioClip>(StringComparer.OrdinalIgnoreCase);

        private static MissileAudioHost _host;
        private static int _loadGen;
        private static string _audioRoot;
        private static string _status = "idle";
        private static int _clipCount;

        internal static string StatusLine { get { return _status; } }
        internal static string AudioRoot { get { return _audioRoot; } }
        internal static int ClipCount { get { return _clipCount; } }

        internal static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("MissileAudio", "Enabled", false,
                "Replace in-game missile SFX when matching files exist. Off by default — enable in F1 → MISSILE AUDIO. WAV decode only runs while ON.");
            FolderPath = cfg.Bind("MissileAudio", "FolderPath", "",
                "Optional absolute/relative override. Empty = BepInEx/plugins/OritasyMissileAudio/");
            VolumeScale = cfg.Bind("MissileAudio", "VolumeScale", 1f,
                "Volume multiplier for custom missile clips (0.1–2.0).");
            try
            {
                ConfigEntry<int> optIn = cfg.Bind("MissileAudio", "OptInRevision", 0,
                    "Internal: 1 = custom audio is opt-in (WAV decode only while Enabled).");
                if (optIn.Value < 1)
                {
                    Enabled.Value = false;
                    optIn.Value = 1;
                }
            }
            catch { }

            _audioRoot = ResolveAudioRoot();
            EnsureFolderAndReadme();
            _status = Enabled != null && Enabled.Value
                ? "on · " + _audioRoot
                : "off";
            if (Enabled != null && Enabled.Value)
            {
                EnsureHost();
                QueueReload();
            }
        }

        internal static void Tick()
        {
            // Reserved — loading is coroutine-based; no per-frame disk IO.
        }

        /// <summary>F1 toggle row (EN/ZH via UiLang).</summary>
        internal static void DrawGuiToggles()
        {
            if (Enabled == null)
                return;

            GUILayout.Label(UiLang.T("MISSILE AUDIO", "导弹音效"), AircraftManeuverGuiStyles.Section);

            bool on = GUILayout.Toggle(Enabled.Value,
                UiLang.T(" Custom missile audio", " 自定义导弹音效"));
            if (on != Enabled.Value)
            {
                Enabled.Value = on;
                if (on)
                {
                    EnsureHost();
                    QueueReload();
                    _status = UiLang.T("enabled", "已启用");
                }
                else
                    _status = UiLang.T("disabled", "已关闭");
            }

            GUILayout.Label(UiLang.T(
                "Folder: plugins/OritasyMissileAudio/  (.ogg .wav .mp3)",
                "目录：plugins/OritasyMissileAudio/（.ogg .wav .mp3）"),
                AircraftManeuverGuiStyles.Label);
            GUILayout.Label(UiLang.T(
                "Name: {jsonKey|weapon}_{launch|motor|loop|explode|proximity}",
                "命名：{jsonKey|武器}_{launch|motor|loop|explode|proximity}"),
                AircraftManeuverGuiStyles.Label);
            GUILayout.Label(UiLang.T("Status: ", "状态：") + _status
                + UiLang.T("  |  clips: ", "  |  音频数：") + _clipCount,
                AircraftManeuverGuiStyles.Label);

            if (VolumeScale != null)
            {
                GUILayout.Label(UiLang.T("Volume scale  ", "音量倍率  ")
                    + VolumeScale.Value.ToString("0.00"),
                    AircraftManeuverGuiStyles.Label);
                float v = GUILayout.HorizontalSlider(VolumeScale.Value, 0.1f, 2f);
                if (!Mathf.Approximately(v, VolumeScale.Value))
                    VolumeScale.Value = v;
            }

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reload audio", AircraftManeuverGuiStyles.Button, GUILayout.Height(28f)))
                QueueReload();
            if (GUILayout.Button("Open folder", AircraftManeuverGuiStyles.Button, GUILayout.Height(28f)))
                OpenAudioFolder();
            GUILayout.EndHorizontal();
        }

        internal static void QueueReload()
        {
            if (Enabled == null || !Enabled.Value)
                return;
            EnsureHost();
            if (_host != null)
                _host.StartCoroutine(LoadRoutine(++_loadGen));
        }

        internal static void OpenAudioFolder()
        {
            try
            {
                EnsureFolderAndReadme();
                if (!string.IsNullOrEmpty(_audioRoot) && Directory.Exists(_audioRoot))
                    Application.OpenURL("file:///" + _audioRoot.Replace('\\', '/'));
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("Open missile audio folder: " + ex.Message);
            }
        }

        private static void EnsureHost()
        {
            if (_host != null)
                return;
            try
            {
                GameObject go = new GameObject("OritasyMissileAudio");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _host = go.AddComponent<MissileAudioHost>();
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("MissileAudio host: " + ex.Message);
            }
        }

        private static string ResolveAudioRoot()
        {
            try
            {
                if (FolderPath != null && !string.IsNullOrEmpty(FolderPath.Value))
                {
                    string custom = FolderPath.Value.Trim();
                    if (!string.IsNullOrEmpty(custom))
                    {
                        if (!Path.IsPathRooted(custom))
                            custom = Path.Combine(Directory.GetCurrentDirectory(), custom);
                        return custom;
                    }
                }
            }
            catch { }

            try
            {
                string plugins = Paths.PluginPath;
                if (!string.IsNullOrEmpty(plugins))
                    return Path.Combine(plugins, "OritasyMissileAudio");
            }
            catch { }
            try
            {
                string loc = Path.GetDirectoryName(typeof(Plugin).Assembly.Location);
                if (!string.IsNullOrEmpty(loc))
                    return Path.Combine(loc, "OritasyMissileAudio");
            }
            catch { }
            return Path.Combine(Directory.GetCurrentDirectory(), "BepInEx", "plugins", "OritasyMissileAudio");
        }

        private static void EnsureFolderAndReadme()
        {
            try
            {
                if (string.IsNullOrEmpty(_audioRoot))
                    _audioRoot = ResolveAudioRoot();
                if (!Directory.Exists(_audioRoot))
                    Directory.CreateDirectory(_audioRoot);

                for (int i = 0; i < Categories.Length; i++)
                {
                    string sub = Path.Combine(_audioRoot, Categories[i]);
                    if (!Directory.Exists(sub))
                        Directory.CreateDirectory(sub);
                }

                string readme = Path.Combine(_audioRoot, "README.txt");
                if (!File.Exists(readme))
                {
                    File.WriteAllText(readme,
                        "Oritasy missile audio overrides\r\n"
                        + "================================\r\n"
                        + "Formats: .ogg  .wav  .mp3\r\n\r\n"
                        + "Naming (pick one style):\r\n"
                        + "  1) Flat:   {key}_{category}.ogg\r\n"
                        + "            e.g. AGM_heavy_launch.wav\r\n"
                        + "                 AAM2_motor.ogg\r\n"
                        + "                 ARH1_explode.ogg\r\n"
                        + "  2) Folder: {category}/{key}.ogg\r\n"
                        + "            e.g. launch/AGM_heavy.wav\r\n"
                        + "  3) Per missile: {key}/{category}.ogg\r\n"
                        + "            e.g. AGM_heavy/motor.ogg\r\n\r\n"
                        + "Key = MissileDefinition jsonKey, WeaponInfo shortName/weaponName,\r\n"
                        + "      or unit/prefab name (spaces → _, case-insensitive).\r\n\r\n"
                        + "Categories:\r\n"
                        + "  launch     - rail deploy + motor startup\r\n"
                        + "  motor      - motor burn AudioSources\r\n"
                        + "  loop       - in-flight flightSound loop\r\n"
                        + "  explode    - nearby detonation clip\r\n"
                        + "  proximity  - alias of explode\r\n\r\n"
                        + "Global fallback (all missiles): default_{category}.ogg\r\n"
                        + "Missing files keep vanilla audio.\r\n"
                        + "In-game: F1 → MISSILE AUDIO → Reload audio after adding files.\r\n");
                }
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("MissileAudio folder: " + ex.Message);
            }
        }

        private static IEnumerator LoadRoutine(int gen)
        {
            if (Enabled == null || !Enabled.Value)
            {
                _status = "off";
                yield break;
            }
            _audioRoot = ResolveAudioRoot();
            EnsureFolderAndReadme();
            _status = "loading…";

            List<AudioClip> old = new List<AudioClip>();
            foreach (KeyValuePair<string, AudioClip> kv in LoadedByPath)
            {
                if (kv.Value != null)
                    old.Add(kv.Value);
            }
            LoadedByPath.Clear();
            ClipIndex.Clear();

            List<string> files = new List<string>();
            CollectAudioFiles(_audioRoot, files);

            int loaded = 0;
            for (int fi = 0; fi < files.Count; fi++)
            {
                if (gen != _loadGen)
                    yield break;

                string path = files[fi];
                string ext = Path.GetExtension(path);
                if (string.IsNullOrEmpty(ext))
                    continue;
                ext = ext.ToLowerInvariant();
                if (ext != ".ogg" && ext != ".wav" && ext != ".mp3")
                    continue;

                string key;
                string category;
                if (!TryParseFile(path, _audioRoot, out key, out category))
                    continue;

                AudioClip clip = null;
                if (ext == ".wav")
                {
                    if (Enabled == null || !Enabled.Value)
                        yield break;
                    IEnumerator wavLoad = OritasyWavPcm.LoadClip(path, delegate(AudioClip c) { clip = c; });
                    while (wavLoad.MoveNext())
                    {
                        if (gen != _loadGen)
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
                        if (gen != _loadGen)
                            yield break;
                        bool ok = false;
                        try { ok = req.result == UnityWebRequest.Result.Success; }
                        catch { ok = string.IsNullOrEmpty(req.error); }
                        if (ok)
                        {
                            try { clip = DownloadHandlerAudioClip.GetContent(req); }
                            catch { clip = null; }
                        }
                        else if (Plugin.Log != null)
                        {
                            Plugin.Log.LogWarning("Missile audio fail " + Path.GetFileName(path) + ": " + req.error);
                        }
                    }
                }

                if (clip == null)
                    continue;

                clip.name = "OritasyMissile_" + key + "_" + category;
                LoadedByPath[path] = clip;
                IndexClip(key, category, clip);
                loaded++;
                yield return null;
            }

            for (int i = 0; i < old.Count; i++)
            {
                try { UnityEngine.Object.Destroy(old[i]); }
                catch { }
            }

            _clipCount = loaded;
            _status = "loaded: " + loaded + " file(s)";
            if (Plugin.Log != null)
                Plugin.Log.LogInfo("Oritasy missile audio: " + _status + " @ " + _audioRoot);
        }

        private static void CollectAudioFiles(string root, List<string> files)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return;
            try
            {
                string[] top = Directory.GetFiles(root);
                for (int i = 0; i < top.Length; i++)
                    files.Add(top[i]);
            }
            catch { }

            try
            {
                string[] dirs = Directory.GetDirectories(root);
                for (int d = 0; d < dirs.Length; d++)
                {
                    string dirName = Path.GetFileName(dirs[d]);
                    if (string.IsNullOrEmpty(dirName) || dirName.StartsWith("."))
                        continue;
                    try
                    {
                        string[] nested = Directory.GetFiles(dirs[d]);
                        for (int i = 0; i < nested.Length; i++)
                            files.Add(nested[i]);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static bool TryParseFile(string path, string root, out string key, out string category)
        {
            return MissileAudioKeyService.TryParseFile(path, root, out key, out category);
        }

        private static bool TrySplitKeyCategory(string fileNorm, out string key, out string category)
        {
            return MissileAudioKeyService.TrySplitKeyCategory(fileNorm, out key, out category);
        }

        private static bool IsCategory(string s)
        {
            return MissileAudioKeyService.IsCategory(s);
        }

        private static string CanonicalCategory(string cat)
        {
            return MissileAudioKeyService.CanonicalCategory(cat);
        }

        private static void IndexClip(string key, string category, AudioClip clip)
        {
            if (clip == null || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(category))
                return;
            string idx = MissileAudioKeyService.IndexKey(key, category);
            ClipIndex[idx] = clip;
            // proximity files also fill explode
            if (string.Equals(category, "explode", StringComparison.OrdinalIgnoreCase))
                ClipIndex[MissileAudioKeyService.IndexKey(key, "proximity")] = clip;
        }

        private static string NormalizeKey(string raw)
        {
            return MissileAudioKeyService.NormalizeKey(raw);
        }

        private static AudioClip Resolve(string[] keys, string category)
        {
            if (Enabled == null || !Enabled.Value || keys == null)
                return null;
            string cat = CanonicalCategory(category);
            if (string.IsNullOrEmpty(cat))
                return null;

            for (int i = 0; i < keys.Length; i++)
            {
                string k = keys[i];
                if (string.IsNullOrEmpty(k))
                    continue;
                AudioClip clip;
                if (ClipIndex.TryGetValue(k + "|" + cat, out clip) && clip != null)
                    return clip;
            }

            AudioClip fallback;
            if (ClipIndex.TryGetValue("default|" + cat, out fallback) && fallback != null)
                return fallback;
            if (ClipIndex.TryGetValue("all|" + cat, out fallback) && fallback != null)
                return fallback;
            return null;
        }

        private static string[] CollectIdentityKeys(Missile missile, WeaponInfo info)
        {
            List<string> keys = new List<string>(8);
            AddKey(keys, missile != null && missile.definition != null ? missile.definition.jsonKey : null);
            AddKey(keys, missile != null && missile.definition != null ? missile.definition.unitName : null);
            AddKey(keys, missile != null && missile.definition != null ? missile.definition.code : null);
            if (info != null)
            {
                AddKey(keys, info.shortName);
                AddKey(keys, info.weaponName);
            }
            if (missile != null)
            {
                AddKey(keys, missile.name);
                try
                {
                    if (missile.gameObject != null)
                        AddKey(keys, missile.gameObject.name);
                }
                catch { }
            }
            return keys.ToArray();
        }

        private static string[] CollectIdentityKeysFromWeapon(Weapon weapon)
        {
            List<string> keys = new List<string>(8);
            if (weapon == null)
                return keys.ToArray();
            WeaponInfo info = null;
            try { info = weapon.info; }
            catch { }
            if (info != null)
            {
                AddKey(keys, info.shortName);
                AddKey(keys, info.weaponName);
            }
            try
            {
                FieldInfo mountField = AccessTools.Field(typeof(Weapon), "mount");
                WeaponMount mount = mountField != null
                    ? mountField.GetValue(weapon) as WeaponMount
                    : null;
                if (mount != null)
                {
                    AddKey(keys, mount.jsonKey);
                    AddKey(keys, mount.mountName);
                    if (mount.prefab != null)
                    {
                        AddKey(keys, mount.prefab.name);
                        try
                        {
                            Missile prefabMissile = mount.prefab.GetComponentInChildren<Missile>(true);
                            if (prefabMissile != null)
                            {
                                if (prefabMissile.definition != null)
                                {
                                    AddKey(keys, prefabMissile.definition.jsonKey);
                                    AddKey(keys, prefabMissile.definition.unitName);
                                }
                                AddKey(keys, prefabMissile.name);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }
            AddKey(keys, weapon.name);
            return keys.ToArray();
        }

        private static void AddKey(List<string> keys, string raw)
        {
            string n = NormalizeKey(raw);
            if (string.IsNullOrEmpty(n))
                return;
            for (int i = 0; i < keys.Count; i++)
            {
                if (string.Equals(keys[i], n, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            keys.Add(n);
        }

        private static float Vol()
        {
            if (VolumeScale == null)
                return 1f;
            return Mathf.Clamp(VolumeScale.Value, 0.05f, 3f);
        }

        internal static void ApplyToMissile(Missile missile)
        {
            if (Enabled == null || !Enabled.Value || missile == null)
                return;
            if (_clipCount <= 0 && ClipIndex.Count == 0)
                return;

            WeaponInfo info = null;
            try
            {
                if (InfoField != null)
                    info = InfoField.GetValue(missile) as WeaponInfo;
            }
            catch { }

            string[] keys = CollectIdentityKeys(missile, info);
            float scale = Vol();

            // flight loop
            try
            {
                AudioClip loop = Resolve(keys, "loop");
                if (loop == null)
                    loop = Resolve(keys, "motor");
                if (loop != null && FlightSoundField != null)
                {
                    AudioSource src = FlightSoundField.GetValue(missile) as AudioSource;
                    if (src != null)
                        src.clip = loop;
                    // flightSound volume is driven by Missile.FixedUpdate — leave curve intact.
                }
            }
            catch { }

            // nearby detonation / proximity
            try
            {
                AudioClip boom = Resolve(keys, "explode");
                if (boom == null)
                    boom = Resolve(keys, "proximity");
                if (boom != null && NearbyDetonationField != null)
                    NearbyDetonationField.SetValue(missile, boom);
            }
            catch { }

            // motors: startup (launch) + burn loops (motor)
            try
            {
                if (MotorsField == null || MotorAudioSourcesField == null)
                    return;
                Array motors = MotorsField.GetValue(missile) as Array;
                if (motors == null)
                    return;

                AudioClip launch = Resolve(keys, "launch");
                AudioClip motor = Resolve(keys, "motor");
                if (motor == null)
                    motor = Resolve(keys, "loop");

                for (int i = 0; i < motors.Length; i++)
                {
                    object m = motors.GetValue(i);
                    if (m == null)
                        continue;

                    if (launch != null && MotorStartupField != null)
                    {
                        AudioSource startup = MotorStartupField.GetValue(m) as AudioSource;
                        if (startup != null)
                        {
                            startup.clip = launch;
                            startup.volume = Mathf.Clamp01(startup.volume * scale);
                        }
                    }

                    if (motor != null)
                    {
                        AudioSource[] sources = MotorAudioSourcesField.GetValue(m) as AudioSource[];
                        if (sources != null)
                        {
                            for (int s = 0; s < sources.Length; s++)
                            {
                                if (sources[s] == null)
                                    continue;
                                sources[s].clip = motor;
                                sources[s].volume = Mathf.Clamp01(sources[s].volume * scale);
                            }
                        }
                    }
                }
            }
            catch { }
        }

        internal static void ApplyToMountedLaunch(MountedMissile mounted)
        {
            if (Enabled == null || !Enabled.Value || mounted == null || DeploySoundField == null)
                return;
            if (_clipCount <= 0 && ClipIndex.Count == 0)
                return;

            string[] keys = CollectIdentityKeysFromWeapon(mounted);
            AudioClip launch = Resolve(keys, "launch");
            if (launch == null)
                return;
            try
            {
                DeploySoundField.SetValue(mounted, launch);
            }
            catch { }
        }

        /// <summary>
        /// After detonation vanilla forces volume=1; re-apply scale once (not every FixedUpdate).
        /// </summary>
        internal static void ScaleDetonationVolume(Missile missile)
        {
            if (Enabled == null || !Enabled.Value || missile == null || FlightSoundField == null)
                return;
            float scale = Vol();
            if (Mathf.Approximately(scale, 1f))
                return;
            try
            {
                AudioSource src = FlightSoundField.GetValue(missile) as AudioSource;
                if (src != null && src.isPlaying && !src.loop)
                    src.volume = Mathf.Clamp01(scale);
            }
            catch { }
        }

    }

    internal sealed class MissileAudioHost : MonoBehaviour
    {
    }

    [HarmonyPatch(typeof(Missile), "StartMissile")]
    internal static class Patch_Missile_StartMissile_Audio
    {
        [HarmonyPrefix]
        private static void Prefix(Missile __instance)
        {
            MissileAudio.ApplyToMissile(__instance);
        }
    }

    [HarmonyPatch(typeof(Missile), "UserCode_RpcDetonate_897349600")]
    internal static class Patch_Missile_Detonate_Audio
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance)
        {
            MissileAudio.ScaleDetonationVolume(__instance);
        }
    }

    [HarmonyPatch(typeof(MountedMissile), "PlayLaunchSound")]
    internal static class Patch_MountedMissile_PlayLaunchSound_Audio
    {
        [HarmonyPrefix]
        private static void Prefix(MountedMissile __instance)
        {
            MissileAudio.ApplyToMountedLaunch(__instance);
        }
    }
}
