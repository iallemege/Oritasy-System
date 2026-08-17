using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Kh85MT
{
    internal static class PluginInfo
    {
        public const string GUID = "com.iallemege.kh85mt";
        public const string LegacyGUID = "com.qiaochen.kh85mt";
        public const string Name = "TGM-85";
        public const string Version = "1.8.24";
    }

    /// <summary>
    /// Combined Oritasy.dll: hosted by Oritasy.Plugin (no second [BepInPlugin]).
    /// Dual BepInPlugin in one DLL doubled Harmony + Update.
    /// </summary>
#if !ORITASY_COMBINED
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
#else
    public class Plugin : MonoBehaviour
#endif
    {
        internal static ManualLogSource Log;
        internal static Plugin Instance;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<bool> DebugLog;
        internal static ConfigEntry<bool> CustomVisual;
        internal static ConfigEntry<float> VisualScale;
        internal static ConfigEntry<string> VisualEuler;
        internal static ConfigEntry<string> VisualOffset;
        internal static ConfigEntry<string> UvMode;
        internal static ConfigEntry<string> DisplayName;
        internal static ConfigEntry<string> DonorKeyPrefix;
        internal static ConfigEntry<float> ThrustMultiplier;
        internal static ConfigEntry<float> FuelMultiplier;

        static Plugin()
        {
            try
            {
                string dir = BepInEx.Paths.ConfigPath;
                if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir))
                    return;
                string dst = System.IO.Path.Combine(dir, PluginInfo.GUID + ".cfg");
                string src = System.IO.Path.Combine(dir, PluginInfo.LegacyGUID + ".cfg");
                if (System.IO.File.Exists(dst) || !System.IO.File.Exists(src))
                    return;
                System.IO.File.Copy(src, dst, false);
            }
            catch { }
        }

#if ORITASY_COMBINED
        private static ConfigFile _hostConfig;
        private ConfigFile Config
        {
            get { return _hostConfig; }
        }

        private static bool _initDone;

        /// <summary>Oritasy combined host attaches TGM-85 lifecycle on the same GameObject.</summary>
        internal static void StartHosted(BaseUnityPlugin host, ManualLogSource log)
        {
            if (host == null)
                return;
            Log = log ?? BepInEx.Logging.Logger.CreateLogSource(PluginInfo.Name);
            if (_hostConfig == null)
            {
                string path = System.IO.Path.Combine(Paths.ConfigPath, PluginInfo.GUID + ".cfg");
                _hostConfig = new ConfigFile(path, true);
            }
            try
            {
                RunInit();
            }
            catch (Exception ex)
            {
                if (Log != null)
                    Log.LogError("TGM-85 init: " + DescribeEx(ex));
            }
            // Do not AddComponent — ticked from Oritasy.Plugin.HostedTick.
        }

        private static string DescribeEx(Exception ex)
        {
            if (ex == null)
                return "";
            string s = "";
            Exception e = ex;
            int n = 0;
            while (e != null && n < 8)
            {
                if (n > 0)
                    s += " --> ";
                s += e.GetType().Name + ": " + e.Message;
                e = e.InnerException;
                n++;
            }
            return s;
        }
#endif

        private void Awake()
        {
            Instance = this;
#if !ORITASY_COMBINED
            Log = Logger;
            _bindConfig = Config;
#endif
            try
            {
                RunInit();
            }
            catch (Exception ex)
            {
                if (Log != null)
                    Log.LogError("TGM-85 Awake: " + DescribeEx(ex));
            }
        }

#if !ORITASY_COMBINED
        private static bool _initDoneStandalone;
        private static ConfigFile _bindConfig;
        private static bool _initDone { get { return _initDoneStandalone; } set { _initDoneStandalone = value; } }

        private static string DescribeEx(Exception ex)
        {
            if (ex == null)
                return "";
            string s = "";
            Exception e = ex;
            int n = 0;
            while (e != null && n < 8)
            {
                if (n > 0)
                    s += " --> ";
                s += e.GetType().Name + ": " + e.Message;
                e = e.InnerException;
                n++;
            }
            return s;
        }
#endif

        private static void RunInit()
        {
            if (_initDone)
                return;
            if (Log == null)
                Log = BepInEx.Logging.Logger.CreateLogSource(PluginInfo.Name);
#if ORITASY_COMBINED
            ConfigFile cfg = _hostConfig;
#else
            ConfigFile cfg = _bindConfig;
#endif
            if (cfg == null)
            {
                Log.LogError("TGM-85 missing Config — abort.");
                return;
            }
            Kh85Advanced.InitStatic();

            Enabled = cfg.Bind("General", "Enabled", true,
                "Clone AGM-68 (AGM_heavy) mounts as TGM-85 and inject onto compatible pylons.");
            DebugLog = cfg.Bind("General", "DebugLog", false, "Verbose logging.");
            CustomVisual = cfg.Bind("Visual", "CustomVisual", true,
                "Apply Kh-85MT OBJ mesh/texture swap on hangar racks and launched missiles.");
            VisualScale = cfg.Bind("Visual", "Scale", 1f, "Local scale of the replacement mesh.");
            // OBJ long axis is +X; Nuclear Option missiles face +Z → yaw -90.
            VisualEuler = cfg.Bind("Visual", "Euler", "0,-90,0",
                "Local euler angles (deg) for the replacement mesh: x,y,z");
            VisualOffset = cfg.Bind("Visual", "Offset", "0,0,0",
                "Local position offset for the replacement mesh: x,y,z");
            // Kh-85MT OBJ already stores V in [-1,0]; Repeat sampling maps that correctly.
            // flipV / unityFlip are only for troubleshooting misaligned textures.
            UvMode = cfg.Bind("Visual", "UvMode", "raw",
                "UV fix: raw | flipV | unityFlip | flipU | flipUV");
            DisplayName = cfg.Bind("Display", "DisplayName", "TGM-85C Shardfall",
                "UI / encyclopedia name for the base (C) variant.");
            DonorKeyPrefix = cfg.Bind("Mount", "DonorKeyPrefix", "AGM_heavy",
                "Vanilla WeaponMount/MissileDefinition jsonKey prefix to clone (AGM-68 family).");
            ThrustMultiplier = cfg.Bind("Performance", "ThrustMultiplier", 1f,
                "Multiply all motor thrust values vs AGM-68 donor.");
            FuelMultiplier = cfg.Bind("Performance", "FuelMultiplier", 1f,
                "Multiply all motor fuelMass values vs AGM-68 donor.");
            Kh85Advanced.BindConfig(cfg);

            Harmony harmony;
#if ORITASY_COMBINED
            harmony = Oritasy.Plugin.SharedHarmony;
            if (harmony == null)
                harmony = new Harmony(Oritasy.PluginInfo.GUID);
#else
            harmony = new Harmony(PluginInfo.GUID);
#endif
            PatchOwnNamespace(harmony);
            _initDone = true;
            Log.LogInfo(PluginInfo.Name + " v" + PluginInfo.Version
                + " loaded (patches ok=" + _patchOk + " fail=" + _patchFail + ")"
#if ORITASY_COMBINED
                + " [Harmony " + Oritasy.PluginInfo.GUID + "]"
#endif
                + ".");
        }

        private static bool _harmonyApplied;
        private static int _patchOk;
        private static int _patchFail;

        internal static void PatchOwnNamespace(Harmony harmony)
        {
            if (harmony == null || _harmonyApplied)
                return;
            _harmonyApplied = true;
            _patchOk = 0;
            _patchFail = 0;
            string ns = typeof(Plugin).Namespace;
            Type[] types = Assembly.GetExecutingAssembly().GetTypes();
            for (int i = 0; i < types.Length; i++)
            {
                Type t = types[i];
                if (t == null || t.IsInterface || t.IsEnum)
                    continue;
                if (t.Namespace == null || t.Namespace != ns)
                    continue;
                object[] attrs = t.GetCustomAttributes(typeof(HarmonyPatch), false);
                if (attrs == null || attrs.Length == 0)
                {
                    bool hasTarget = false;
                    try
                    {
                        MethodInfo[] ms = t.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                        for (int mi = 0; mi < ms.Length; mi++)
                        {
                            if (ms[mi].GetCustomAttributes(typeof(HarmonyTargetMethods), false).Length > 0
                                || ms[mi].GetCustomAttributes(typeof(HarmonyTargetMethod), false).Length > 0)
                            {
                                hasTarget = true;
                                break;
                            }
                        }
                    }
                    catch { }
                    if (!hasTarget)
                        continue;
                }
                try
                {
                    harmony.CreateClassProcessor(t).Patch();
                    _patchOk++;
                }
                catch (Exception ex)
                {
                    _patchFail++;
                    if (Log != null)
                        Log.LogWarning("Kh-85MT patch skip " + t.Name + ": " + ex.Message);
                }
            }
        }

        internal static void HostedTick()
        {
            if (!_initDone)
                return;
            RunUpdateBody();
        }

        private void Update()
        {
#if ORITASY_COMBINED
            return;
#else
            RunUpdateBody();
#endif
        }

        private static void RunUpdateBody()
        {
            if (Enabled == null || !Enabled.Value)
                return;
            try
            {
                Kh85Weapon.Tick();
            }
            catch (Exception ex)
            {
                if (Log != null)
                    Log.LogWarning("Kh-85MT Tick swallowed: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        internal static string ResolveDisplayName()
        {
            string n = DisplayName != null ? DisplayName.Value : null;
            if (string.IsNullOrEmpty(n))
                return "TGM-85C Shardfall";
            return n.Trim();
        }

        internal static string ResolveDonorPrefix()
        {
            string p = DonorKeyPrefix != null ? DonorKeyPrefix.Value : null;
            if (string.IsNullOrEmpty(p))
                return "AGM_heavy";
            return p.Trim();
        }

        internal static Encyclopedia GetEncyclopedia()
        {
            try
            {
                PropertyInfo p = AccessTools.Property(typeof(Encyclopedia), "i");
                if (p != null)
                {
                    Encyclopedia viaProp = p.GetValue(null, null) as Encyclopedia;
                    if (viaProp != null)
                        return viaProp;
                }
            }
            catch { }

            Encyclopedia[] all = Resources.FindObjectsOfTypeAll<Encyclopedia>();
            if (all != null && all.Length > 0)
                return all[0];
            return null;
        }

        private static readonly FieldInfo WeaponMountField =
            AccessTools.Field(typeof(Weapon), "mount")
            ?? AccessTools.Field(typeof(Weapon), "weaponMount");

        internal static WeaponMount GetWeaponMount(Weapon weapon)
        {
            if (weapon == null || WeaponMountField == null)
                return null;
            try { return WeaponMountField.GetValue(weapon) as WeaponMount; }
            catch { return null; }
        }
    }

    /// <summary>
    /// Clone AGM-68 (AGM_heavy*) WeaponMounts into Kh85MT_* variants and wire hardpoints.
    /// Gameplay/prefabs stay shared with the donor; visuals are swapped separately.
    /// </summary>
    internal static class Kh85Weapon
    {
        internal const string PackKey = "Kh85MT";
        /// <summary>Display-name brand tag (not a jsonKey suffix).</summary>
        internal const string IalDisplayTag = "[IAL]";
        /// <summary>TGM-85C racks are the nuclear fit — 10kt yield branding (not ACNM 1.5kt).</summary>
        internal const string NukeDisplayTag = "[10kt]";
        /// <summary>Legacy jsonKey suffix from older builds — stripped when parsing.</summary>
        private const string LegacyIalKeySuffix = "_IAL";
        /// <summary>
        /// Shared Veyrn lore (kept from post-1.8.9 encyclopedia pass):
        /// TGM-85 = 1980s first missile, upgraded through 2072; ACM-119 = lifeline at year 281.
        /// </summary>
        internal const string LoreCore =
            "TGM-85 was Veyrn Aeronautics' first missile in the 1980s. Continuously upgraded across "
            + "decades of service, it remains fielded into 2072—the Oritasy present—on the proven "
            + "Kh-85MT airframe. The ACM-119 series, by contrast, became the company's lifeline two "
            + "hundred eighty-one years after its founding. Identical unmarked TGM-85 lots still reach "
            + "both the Boscali Defense Force and the Primeva Armed Liberation Alliance through brokers, "
            + "without end-user clauses or national insignia.";

        /// <summary>Encyclopedia blurb: display name + shared lore only (no per-variant traits).</summary>
        internal static string EncyclopediaFor(string displayName)
        {
            return WithIalDisplay(displayName) + " — " + LoreCore;
        }

        internal static string EncyclopediaText
        {
            get { return EncyclopediaFor(Plugin.ResolveDisplayName()); }
        }

        private static bool _injected;
        /// <summary>True after Encyclopedia.AfterLoad — safe to mutate weaponMounts/missiles lists.</summary>
        private static bool _encyclopediaReady;
        private static float _nextMissingLog;
        private static float _nextHardpointInject;
        private static float _nextMaintAt;
        private static int _hardpointIdlePasses;
        /// <summary>Once-per-aircraft-key skip log when hardpoint inject fails (modded / empty pylons).</summary>
        private static readonly HashSet<string> HardpointSkipLogged =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private sealed class PendingLaunch
        {
            public string Key;
            public Unit Target;
            public Unit Owner;
            public int WeaponId;
        }

        /// <summary>Queued launches from NoteFire — claimed only by matching Kh85 spawns.</summary>
        private static readonly Queue<PendingLaunch> PendingLaunches = new Queue<PendingLaunch>();
        private static float _pendingExpire;
        /// <summary>Rail launches can take several seconds — keep a sticky last-fire context.</summary>
        private static string _lastFireKey;
        private static Unit _lastFireTarget;
        private static Unit _lastFireOwner;
        private static float _lastFireTime;
        private static int _lastNoteWeaponId;
        private static float _lastNoteTime;

        private static MissileDefinition _encyclopediaDef;
        private static readonly Dictionary<string, MissileDefinition> DefByKey =
            new Dictionary<string, MissileDefinition>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> CreatedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<WeaponMount> MountClones = new List<WeaponMount>();
        /// <summary>Live AGM-68 rack prefabs keyed by i/x + ammo. Prefer persistent, accept any live GO.</summary>
        private static readonly Dictionary<string, GameObject> CachedAgm68Racks =
            new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, WeaponInfo> InfoByKey =
            new Dictionary<string, WeaponInfo>(StringComparer.OrdinalIgnoreCase);
        /// <summary>O(1) IsKh85Info — populated whenever InfoByKey gains a WeaponInfo.</summary>
        private static readonly HashSet<int> InfoIds = new HashSet<int>();
        /// <summary>Confirmed non-Kh85 WeaponInfo ids — stop rescanning names every Steering.</summary>
        private static readonly HashSet<int> NonKh85InfoIds = new HashSet<int>();
        /// <summary>Inactive prefab roots with letter MissileDefinition already baked in.</summary>
        private static readonly Dictionary<string, GameObject> SpawnPrefabByLetter =
            new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<int> ShipWmIds = new HashSet<int>();
        private static readonly HashSet<int> AirWmIds = new HashSet<int>();
        private static readonly HashSet<int> MotorScaledIds = new HashSet<int>();
        /// <summary>Steering hot path: confirmed non-Kh85 missiles skip GetComponent on later frames.</summary>
        private static readonly HashSet<int> NonKh85MissileIds = new HashSet<int>();
        private static float _nextNonKh85Prune;
        private static float _nextIdPrune;
        private static readonly FieldInfo MissileInfoField = AccessTools.Field(typeof(Missile), "info");
        private static readonly FieldInfo WeaponStationField = AccessTools.Field(typeof(Weapon), "weaponStation");
        private static readonly FieldInfo MotorsField = AccessTools.Field(typeof(Missile), "motors");

        internal static bool IsKh85Key(string key)
        {
            return !string.IsNullOrEmpty(key)
                && key.StartsWith(PackKey, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>O(1) reject for vanilla AGM/AAM on Steering / DetectCollisions.</summary>
        internal static bool IsKnownNonKh85Missile(Missile missile)
        {
            return missile != null && NonKh85MissileIds.Contains(missile.GetInstanceID());
        }

        internal static void NoteNonKh85Missile(Missile missile)
        {
            if (missile == null)
                return;
            NonKh85MissileIds.Add(missile.GetInstanceID());
            if (Time.unscaledTime < _nextNonKh85Prune)
                return;
            _nextNonKh85Prune = Time.unscaledTime + 60f;
            if (NonKh85MissileIds.Count > 512)
                NonKh85MissileIds.Clear();
        }

        internal static void NoteKh85Missile(Missile missile)
        {
            if (missile == null)
                return;
            NonKh85MissileIds.Remove(missile.GetInstanceID());
        }

        internal static bool IsDonorKey(string key)
        {
            string prefix = Plugin.ResolveDonorPrefix();
            return !string.IsNullOrEmpty(key)
                && key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// True for vanilla AGM-68 / already-Kh85 definitions.
        /// SAM / AAM / TBM keys must never match — spawn swap used to steal those shots.
        /// </summary>
        internal static bool IsAgm68OrKh85Definition(MissileDefinition d)
        {
            if (d == null)
                return false;
            string key = d.jsonKey;
            if (IsKh85Key(key) || IsDonorKey(key))
                return true;
            string code = d.code != null ? d.code : string.Empty;
            string un = d.unitName != null ? d.unitName : string.Empty;
            if (code.IndexOf("TGM-85", StringComparison.OrdinalIgnoreCase) >= 0
                || un.IndexOf("TGM-85", StringComparison.OrdinalIgnoreCase) >= 0
                || code.IndexOf("Kh-85", StringComparison.OrdinalIgnoreCase) >= 0
                || un.IndexOf("Kh-85", StringComparison.OrdinalIgnoreCase) >= 0
                || code.IndexOf("Kh85", StringComparison.OrdinalIgnoreCase) >= 0
                || un.IndexOf("Kh85", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return code.IndexOf("AGM-68", StringComparison.OrdinalIgnoreCase) >= 0
                || un.IndexOf("AGM-68", StringComparison.OrdinalIgnoreCase) >= 0
                || code.IndexOf("AGM68", StringComparison.OrdinalIgnoreCase) >= 0
                || un.IndexOf("AGM68", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool IsAgm68Mount(WeaponMount m)
        {
            if (m == null)
                return false;
            if (IsDonorKey(m.jsonKey))
                return true;
            if (m.info == null)
                return false;
            string sn = m.info.shortName != null ? m.info.shortName : string.Empty;
            string wn = m.info.weaponName != null ? m.info.weaponName : string.Empty;
            return sn.IndexOf("AGM-68", StringComparison.OrdinalIgnoreCase) >= 0
                || wn.IndexOf("AGM-68", StringComparison.OrdinalIgnoreCase) >= 0
                || sn.IndexOf("AGM68", StringComparison.OrdinalIgnoreCase) >= 0
                || wn.IndexOf("AGM68", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool IsKh85Mount(WeaponMount mount)
        {
            if (mount == null)
                return false;
            if (IsKh85Key(mount.jsonKey))
                return true;
            return IsKh85Info(mount.info);
        }

        internal static bool IsKh85Info(WeaponInfo info)
        {
            if (info == null)
                return false;
            int id = info.GetInstanceID();
            if (InfoIds.Contains(id))
                return true;
            if (NonKh85InfoIds.Contains(id))
                return false;
            // Hangar / late-bind fallback (not Steering hot path once caches are warm).
            string w = info.weaponName != null ? info.weaponName : string.Empty;
            string s = info.shortName != null ? info.shortName : string.Empty;
            string dn = Plugin.ResolveDisplayName();
            bool nameHit = w.IndexOf(dn, StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf(dn, StringComparison.OrdinalIgnoreCase) >= 0
                || w.IndexOf("TGM-85", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("TGM-85", StringComparison.OrdinalIgnoreCase) >= 0
                || w.IndexOf("Kh-85MT", StringComparison.OrdinalIgnoreCase) >= 0
                || s.IndexOf("Kh-85MT", StringComparison.OrdinalIgnoreCase) >= 0;
            if (nameHit)
                InfoIds.Add(id);
            else
                NonKh85InfoIds.Add(id);
            return nameHit;
        }

        private static void RememberInfo(string key, WeaponInfo info)
        {
            if (string.IsNullOrEmpty(key) || info == null)
                return;
            InfoByKey[key] = info;
            InfoIds.Add(info.GetInstanceID());
        }

        internal static bool IsKh85Missile(Missile missile)
        {
            if (missile == null)
                return false;
            try
            {
                if (missile.definition != null && !string.IsNullOrEmpty(missile.definition.jsonKey))
                {
                    if (IsKh85Key(missile.definition.jsonKey))
                        return true;
                    return false;
                }
            }
            catch { }
            return IsKh85Info(GetMissileInfo(missile));
        }

        /// <summary>Strip legacy _IAL jsonKey suffix if present.</summary>
        internal static string StripIal(string key)
        {
            if (string.IsNullOrEmpty(key))
                return key;
            if (key.EndsWith(LegacyIalKeySuffix, StringComparison.OrdinalIgnoreCase))
                return key.Substring(0, key.Length - LegacyIalKeySuffix.Length);
            return key;
        }

        /// <summary>Append [IAL] display tag once (names only — never jsonKey).</summary>
        internal static string WithIalDisplay(string name)
        {
            if (string.IsNullOrEmpty(name))
                return IalDisplayTag;
            if (name.IndexOf(IalDisplayTag, StringComparison.OrdinalIgnoreCase) >= 0)
                return name;
            return name.TrimEnd() + " " + IalDisplayTag;
        }

        /// <summary>
        /// Display branding: all TGM-85 get [IAL]; C-family (all ammo counts) also get [10kt].
        /// </summary>
        internal static string WithVariantDisplay(string name, string letter)
        {
            string n = WithIalDisplay(name);
            bool nukeC = string.IsNullOrEmpty(letter)
                || string.Equals(letter, "C", StringComparison.OrdinalIgnoreCase);
            if (nukeC)
            {
                if (n.IndexOf(NukeDisplayTag, StringComparison.OrdinalIgnoreCase) < 0)
                    n = n.TrimEnd() + " " + NukeDisplayTag;
                return n;
            }
            // Non-C variants: never carry C's yield tag
            if (n.IndexOf(NukeDisplayTag, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                n = n.Replace(" " + NukeDisplayTag, string.Empty);
                n = n.Replace(NukeDisplayTag, string.Empty).Trim();
                n = WithIalDisplay(n);
            }
            return n;
        }

        internal static string RemapKey(string donorKey)
        {
            string prefix = Plugin.ResolveDonorPrefix();
            if (string.IsNullOrEmpty(donorKey))
                return PackKey + "_single";
            if (string.Equals(donorKey, prefix, StringComparison.OrdinalIgnoreCase))
                return PackKey;
            if (donorKey.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return PackKey + donorKey.Substring(prefix.Length);
            return PackKey + "_" + donorKey;
        }

        internal static void Tick()
        {
            try
            {
                TickInner();
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("Kh-85MT TickInner: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void TickInner()
        {
            // Mutating Encyclopedia.weaponMounts/missiles before AfterLoad causes
            // Dictionary.Add duplicate-key crashes and sticks the main menu on Loading.
            if (!_encyclopediaReady)
                return;

            if (_injected)
                ResetDeadInjection();

            if (!_injected)
            {
                WeaponMount[] all = Resources.FindObjectsOfTypeAll<WeaponMount>();
                if (all == null || all.Length == 0)
                {
                    if (Time.unscaledTime >= _nextMissingLog)
                    {
                        _nextMissingLog = Time.unscaledTime + 8f;
                        if (Plugin.Log != null)
                            Plugin.Log.LogWarning("Kh-85MT: waiting for WeaponMount assets…");
                    }
                    return;
                }

                Encyclopedia enc = Plugin.GetEncyclopedia();
                EnsureEncyclopediaDef(enc);
                int added = 0;

                for (int i = 0; i < all.Length; i++)
                {
                    WeaponMount src = all[i];
                    if (src == null || src.info == null || !src.info.missile)
                        continue;
                    if (IsKh85Key(src.jsonKey))
                        continue;
                    if (!IsAgm68Mount(src))
                        continue;
                    // Prefer asset prefabs; any live AGM-68 rack is usable (in-scene is not a hard reject).
                    if (src.prefab == null)
                        continue;

                    string baseKey = !string.IsNullOrEmpty(src.jsonKey) ? src.jsonKey
                        : (!string.IsNullOrEmpty(src.name) ? src.name : Plugin.ResolveDonorPrefix());
                    string khKey = RemapKey(baseKey);
                    if (CreatedKeys.Contains(khKey))
                        continue;
                    try
                    {
                        if (CreateMountVariant(src, enc, khKey, ref added))
                            CreatedKeys.Add(khKey);
                    }
                    catch (Exception ex)
                    {
                        LogHardpointSkipOnce("mount:" + khKey, ex);
                    }
                }

                if (added > 0 || MountClones.Count > 0)
                {
                    _injected = true;
                    RestoreAllMountIdentities();
                    // Variants need live MountClones — never create them before this.
                    Kh85Advanced.TryCreateVariants();
                    BindAllWeaponPrefabs();
                    _nextHardpointInject = 0f;
                    if (Plugin.Log != null)
                        Plugin.Log.LogInfo("Kh-85MT: injected " + added + " mounts (donor "
                            + Plugin.ResolveDonorPrefix() + ")");
                }
                else if (Time.unscaledTime >= _nextMissingLog)
                {
                    _nextMissingLog = Time.unscaledTime + 12f;
                    if (Plugin.Log != null)
                        Plugin.Log.LogWarning("Kh-85MT: no AGM-68 / " + Plugin.ResolveDonorPrefix()
                            + " donor mounts matched yet");
                }
            }

            if (_injected)
            {
                // Cheap no-op when healthy. Must run before hangar SpawnMount, not only every 90s.
                try { RepairBrokenPrefabs(); }
                catch (Exception ex) { LogHardpointSkipOnce("repair", ex); }
                try { Kh85Advanced.TryCreateVariants(); }
                catch (Exception ex) { LogHardpointSkipOnce("variants", ex); }
                if (Time.unscaledTime >= _nextMaintAt)
                {
                    _nextMaintAt = Time.unscaledTime + 90f;
                    try
                    {
                        RegisterWithEncyclopedia(Plugin.GetEncyclopedia());
                    }
                    catch (Exception ex) { LogHardpointSkipOnce("maint", ex); }
                }
                if (Time.unscaledTime >= _nextHardpointInject)
                    InjectIntoAircraftHardpoints();
            }

            if (PendingLaunches.Count > 0 && Time.time > _pendingExpire)
                PendingLaunches.Clear();
            // Sticky last-fire survives rail delay; drop after 20s.
            if (!string.IsNullOrEmpty(_lastFireKey) && Time.time > _lastFireTime + 20f)
            {
                _lastFireKey = null;
                _lastFireTarget = null;
            }
            if (Time.unscaledTime >= _nextIdPrune)
            {
                _nextIdPrune = Time.unscaledTime + 60f;
                PruneIdSets();
            }
        }

        /// <summary>Called from Harmony AfterLoad — marks encyclopedia safe and cleans dup keys.</summary>
        internal static void OnEncyclopediaAfterLoad(Encyclopedia enc)
        {
            DedupeEncyclopediaLists(enc);
            _encyclopediaReady = true;
        }

        /// <summary>
        /// Encyclopedia.AfterLoad uses Dictionary.Add — duplicate jsonKeys hard-crash preload.
        /// </summary>
        internal static void DedupeEncyclopediaLists(Encyclopedia enc)
        {
            if (enc == null)
                return;
            try
            {
                if (enc.weaponMounts != null)
                    DedupeMountList(enc.weaponMounts);
                if (enc.missiles != null)
                    DedupeMissileList(enc.missiles);
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("Kh-85MT encyclopedia dedupe: " + ex.Message);
            }
        }

        private static void DedupeMountList(List<WeaponMount> list)
        {
            if (list == null || list.Count < 2)
                return;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = list.Count - 1; i >= 0; i--)
            {
                WeaponMount m = list[i];
                if (m == null || string.IsNullOrEmpty(m.jsonKey))
                    continue;
                if (!IsKh85Key(m.jsonKey))
                    continue;
                if (!seen.Add(m.jsonKey))
                    list.RemoveAt(i);
            }
            // Second pass: any remaining duplicate keys (incl. non-Kh85) that would still crash AfterLoad
            seen.Clear();
            for (int i = 0; i < list.Count; )
            {
                WeaponMount m = list[i];
                if (m == null || string.IsNullOrEmpty(m.jsonKey))
                {
                    i++;
                    continue;
                }
                if (!seen.Add(m.jsonKey))
                    list.RemoveAt(i);
                else
                    i++;
            }
        }

        private static void DedupeMissileList(List<MissileDefinition> list)
        {
            if (list == null || list.Count < 2)
                return;
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < list.Count; )
            {
                MissileDefinition d = list[i];
                if (d == null || string.IsNullOrEmpty(d.jsonKey))
                {
                    i++;
                    continue;
                }
                if (!seen.Add(d.jsonKey))
                    list.RemoveAt(i);
                else
                    i++;
            }
        }

        internal static void UpsertMount(Encyclopedia enc, WeaponMount mount)
        {
            if (mount == null || string.IsNullOrEmpty(mount.jsonKey))
                return;
            if (enc != null && enc.weaponMounts != null)
            {
                string key = mount.jsonKey;
                for (int i = enc.weaponMounts.Count - 1; i >= 0; i--)
                {
                    WeaponMount existing = enc.weaponMounts[i];
                    if (existing == null)
                        continue;
                    if (object.ReferenceEquals(existing, mount))
                        continue;
                    if (string.Equals(existing.jsonKey, key, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            if (enc.IndexLookup != null)
                                enc.IndexLookup.Remove(existing);
                        }
                        catch { }
                        enc.weaponMounts.RemoveAt(i);
                    }
                }
                if (!enc.weaponMounts.Contains(mount))
                    enc.weaponMounts.Add(mount);
            }
            if (Encyclopedia.WeaponLookup != null)
                Encyclopedia.WeaponLookup[mount.jsonKey] = mount;
            RegisterNetworkLookup(enc, mount);
        }

        internal static void UpsertMissileDef(Encyclopedia enc, MissileDefinition def)
        {
            if (def == null || string.IsNullOrEmpty(def.jsonKey))
                return;
            if (enc != null && enc.missiles != null)
            {
                string key = def.jsonKey;
                for (int i = enc.missiles.Count - 1; i >= 0; i--)
                {
                    MissileDefinition existing = enc.missiles[i];
                    if (existing == null)
                        continue;
                    if (object.ReferenceEquals(existing, def))
                        continue;
                    if (string.Equals(existing.jsonKey, key, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            if (enc.IndexLookup != null)
                                enc.IndexLookup.Remove(existing);
                        }
                        catch { }
                        enc.missiles.RemoveAt(i);
                    }
                }
                if (!enc.missiles.Contains(def))
                    enc.missiles.Add(def);
            }
            if (Encyclopedia.Lookup != null)
                Encyclopedia.Lookup[def.jsonKey] = def;
            RegisterNetworkLookup(enc, def);
        }

        /// <summary>
        /// Loadouts serialize WeaponMount via Encyclopedia.IndexLookup / LookupIndex.
        /// Mounts added after AfterLoad must be registered here or Fly fails / hangar breaks.
        /// </summary>
        internal static void RegisterNetworkLookup(Encyclopedia enc, INetworkDefinition nd)
        {
            if (enc == null || nd == null || enc.IndexLookup == null)
                return;
            try
            {
                // Already indexed?
                int idx = enc.IndexLookup.IndexOf(nd);
                if (idx >= 0)
                {
                    nd.LookupIndex = idx;
                    return;
                }
                enc.IndexLookup.Add(nd);
                nd.LookupIndex = enc.IndexLookup.Count - 1;
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("Kh-85MT IndexLookup: " + ex.Message);
            }
        }

        private static void PruneIdSets()
        {
            if (MotorScaledIds.Count > 256)
                MotorScaledIds.Clear();
            if (ShipWmIds.Count > 128)
                ShipWmIds.Clear();
            if (AirWmIds.Count > 256)
                AirWmIds.Clear();
        }

        private static bool CreateMountVariant(WeaponMount src, Encyclopedia enc, string khKey, ref int added)
        {
            WeaponMount clone = UnityEngine.Object.Instantiate(src);
            clone.name = "Kh85MT_" + (src.name != null ? src.name : "mount");
            clone.jsonKey = khKey;
            clone.hideFlags = HideFlags.DontUnloadUnusedAsset;

            // Base inject is always C-family (nuclear 10kt racks by ammo count)
            string dn = WithVariantDisplay(Plugin.ResolveDisplayName(), "C");
            WeaponInfo infoClone = UnityEngine.Object.Instantiate(src.info);
            infoClone.name = "Kh85MT_info_" + khKey;
            infoClone.hideFlags = HideFlags.DontUnloadUnusedAsset;
            infoClone.weaponName = dn;
            infoClone.shortName = dn;
            infoClone.description = EncyclopediaText;
            infoClone.nuclear = true;
            Sprite icon = Kh85Icon.GetWeaponIcon();
            if (icon != null)
                infoClone.weaponIcon = icon;
            // MUST share vanilla AGM flight prefab — Instantiated DDOL NetworkIdentity
            // templates break Mirage ("already been spawned") and missiles vanish on fire (esp. S).
            if (src.info != null && src.info.weaponPrefab != null)
                infoClone.weaponPrefab = src.info.weaponPrefab;
            clone.prefab = src.prefab;

            clone.info = infoClone;
            if (src.ammo > 0)
                clone.ammo = src.ammo;
            clone.mountName = dn;
            if (clone.ammo > 1)
                clone.mountName = WithVariantDisplay(Plugin.ResolveDisplayName() + " x" + clone.ammo, "C");

            RememberInfo(khKey, infoClone);
            MountClones.Add(clone);
            UpsertMount(enc, clone);

            added++;
            return true;
        }

        internal static void EnsureEncyclopediaDef(Encyclopedia enc)
        {
            if (enc == null || enc.missiles == null)
                return;
            if (_encyclopediaDef == null)
            {
                MissileDefinition src = FindDonorDefinition(enc);
                if (src == null)
                    return;

                MissileDefinition clone = UnityEngine.Object.Instantiate(src);
                clone.name = "Kh85MT";
                clone.jsonKey = PackKey;
                string dn = WithVariantDisplay(Plugin.ResolveDisplayName(), "C");
                clone.code = "85C"; // lock / HUD short code (not AGM "MSL")
                clone.unitName = dn;
                clone.description = EncyclopediaText;
                clone.dontAutomaticallyAddToEncyclopedia = false;
                Sprite icon = Kh85Icon.GetWeaponIcon();
                if (icon != null)
                {
                    clone.friendlyIcon = icon;
                    clone.hostileIcon = icon;
                }

                UpsertMissileDef(enc, clone);

                _encyclopediaDef = clone;
                DefByKey[PackKey] = clone;
            }
            else if (_encyclopediaDef != null && !DefByKey.ContainsKey(PackKey))
                DefByKey[PackKey] = _encyclopediaDef;
        }

        internal static void RegisterDefinition(string key, MissileDefinition def)
        {
            if (string.IsNullOrEmpty(key) || def == null)
                return;
            DefByKey[key] = def;
        }

        internal static void RegisterInfo(string key, WeaponInfo info)
        {
            RememberInfo(key, info);
        }

        /// <summary>True once motor thrust/fuel multipliers have been applied for this instance.</summary>
        internal static bool MotorsScaled(Missile missile)
        {
            return missile != null && MotorScaledIds.Contains(missile.GetInstanceID());
        }

        /// <summary>True when the motors array is missing/empty (S may retry EnsureMotors).</summary>
        internal static bool MotorsMissing(Missile missile)
        {
            if (missile == null || MotorsField == null)
                return true;
            try
            {
                Array motors = MotorsField.GetValue(missile) as Array;
                return motors == null || motors.Length == 0;
            }
            catch { return true; }
        }

        internal static MissileDefinition ResolveDefinition(string key)
        {
            if (string.IsNullOrEmpty(key))
                return _encyclopediaDef;
            MissileDefinition d;
            if (DefByKey.TryGetValue(key, out d) && d != null)
                return d;
            string letter = VariantLetterFromKey(key);
            if (letter != "C")
            {
                string letterDef = PackKey + "_" + letter;
                if (DefByKey.TryGetValue(letterDef, out d) && d != null)
                    return d;
                // Legacy key with _IAL suffix
                if (DefByKey.TryGetValue(letterDef + LegacyIalKeySuffix, out d) && d != null)
                    return d;
            }
            if (DefByKey.TryGetValue(PackKey, out d) && d != null)
                return d;
            return _encyclopediaDef;
        }

        /// <summary>
        /// Resolve a safe shared flight prefab (vanilla AGM). Never Instantiates a
        /// NetworkIdentity into DontDestroyOnLoad — that made S (and others) vanish on fire.
        /// Letter identity is applied via MissileDefinition swap + OnSpawned.
        /// </summary>
        internal static GameObject EnsureSpawnPrefab(string letter, GameObject donorMissilePrefab)
        {
            if (string.IsNullOrEmpty(letter))
                letter = "C";

            DestroyLegacySpawnTemplates();

            GameObject donor = donorMissilePrefab;
            if (donor != null && IsLegacySpawnTemplate(donor))
                donor = null;
            if (donor == null)
                donor = FindSharedAgmFlightPrefab();
            if (donor == null && _encyclopediaDef != null
                && _encyclopediaDef.unitPrefab != null
                && !IsLegacySpawnTemplate(_encyclopediaDef.unitPrefab))
                donor = _encyclopediaDef.unitPrefab;
            if (donor == null)
                return null;

            string defKey = letter == "C" ? PackKey : PackKey + "_" + letter;
            MissileDefinition def = ResolveDefinition(defKey);
            if (def != null)
            {
                string display = Plugin.ResolveDisplayName();
                if (Kh85Advanced.VariantName != null && Kh85Advanced.VariantName.ContainsKey(letter))
                    display = Kh85Advanced.VariantName[letter];
                display = WithIalDisplay(display);
                def.unitName = display;
                def.code = "85" + letter;
                def.jsonKey = defKey;
                // Share vanilla prefab — Spawner Instantiates a fresh network object each shot.
                def.unitPrefab = donor;
            }

            SpawnPrefabByLetter[letter] = donor;
            return donor;
        }

        private static bool IsLegacySpawnTemplate(GameObject go)
        {
            return go != null && go.name != null
                && go.name.IndexOf("Kh85MT_SpawnPrefab", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool _legacyTemplatesCleared;

        /// <summary>Destroy old DDOL Instantiated templates from 1.8.9 that break Mirage spawn.</summary>
        private static void DestroyLegacySpawnTemplates()
        {
            if (_legacyTemplatesCleared)
                return;
            _legacyTemplatesCleared = true;
            try
            {
                GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
                for (int i = 0; i < all.Length; i++)
                {
                    GameObject go = all[i];
                    if (!IsLegacySpawnTemplate(go))
                        continue;
                    try { UnityEngine.Object.Destroy(go); }
                    catch { }
                }
                SpawnPrefabByLetter.Clear();
                if (Plugin.Log != null)
                    Plugin.Log.LogInfo("Kh-85MT: cleared legacy Instantiated spawn templates (Mirage-safe)");
            }
            catch { }
        }

        private static GameObject FindSharedAgmFlightPrefab()
        {
            // Prefer MissileDefinition.unitPrefab (true flight body). weaponPrefab is usually
            // the same for AGM rails, but never trust Instantiated / legacy templates.
            try
            {
                Encyclopedia enc = Plugin.GetEncyclopedia();
                MissileDefinition donor = FindDonorDefinition(enc);
                if (donor != null && donor.unitPrefab != null
                    && !IsLegacySpawnTemplate(donor.unitPrefab)
                    && donor.unitPrefab.GetComponent<Missile>() != null)
                    return donor.unitPrefab;
            }
            catch { }

            for (int i = 0; i < MountClones.Count; i++)
            {
                WeaponMount m = MountClones[i];
                if (m != null && m.info != null && m.info.weaponPrefab != null
                    && !IsLegacySpawnTemplate(m.info.weaponPrefab)
                    && m.info.weaponPrefab.GetComponent<Missile>() != null)
                    return m.info.weaponPrefab;
            }
            try
            {
                WeaponMount[] all = Resources.FindObjectsOfTypeAll<WeaponMount>();
                for (int i = 0; i < all.Length; i++)
                {
                    WeaponMount m = all[i];
                    if (m == null || !IsAgm68Mount(m) || m.info == null || m.info.weaponPrefab == null)
                        continue;
                    if (IsLegacySpawnTemplate(m.info.weaponPrefab))
                        continue;
                    if (m.info.weaponPrefab.GetComponent<Missile>() == null)
                        continue;
                    return m.info.weaponPrefab;
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Launch-frame / low-speed: do not SetAimpoint yet. Early GuideTo-style aim into
        /// terrain/water looks like the missile vanished on fire (standalone, no Oritasy MM).
        /// </summary>
        internal static bool ShouldDeferAim(Missile missile)
        {
            if (missile == null)
                return true;
            try
            {
                if (missile.timeSinceSpawn < 0.45f)
                    return true;
            }
            catch { return true; }
            try
            {
                float spd = 0f;
                if (missile.rb != null)
                    spd = missile.rb.velocity.magnitude;
                else
                    spd = missile.speed;
                if (spd < 50f)
                    return true;
            }
            catch { }
            return false;
        }

        /// <summary>Point all Kh85 WeaponInfo / letter defs at shared vanilla AGM flight prefab.</summary>
        internal static void BindAllWeaponPrefabs()
        {
            EnsureEncyclopediaDef(Plugin.GetEncyclopedia());
            GameObject shared = FindSharedAgmFlightPrefab();
            if (shared == null)
                shared = EnsureSpawnPrefab("C", null);
            if (shared == null)
                return;

            string[] letters = new string[] { "C", "A", "B", "D", "E", "S" };
            for (int i = 0; i < letters.Length; i++)
                EnsureSpawnPrefab(letters[i], shared);

            foreach (KeyValuePair<string, WeaponInfo> kv in InfoByKey)
            {
                if (kv.Value == null)
                    continue;
                if (IsLegacySpawnTemplate(kv.Value.weaponPrefab))
                    kv.Value.weaponPrefab = shared;
                else if (kv.Value.weaponPrefab == null)
                    kv.Value.weaponPrefab = shared;
            }
            for (int i = 0; i < MountClones.Count; i++)
            {
                WeaponMount m = MountClones[i];
                if (m == null || m.info == null)
                    continue;
                if (m.info.weaponPrefab == null || IsLegacySpawnTemplate(m.info.weaponPrefab))
                    m.info.weaponPrefab = shared;
            }
            Kh85Advanced.RebindLetterWeaponPrefabs(shared);
        }

        /// <summary>Never swap to Instantiated templates — share AGM GO; identity via definition + OnSpawned.</summary>
        internal static GameObject ResolveSpawnPrefabSwap(GameObject original)
        {
            if (original != null && IsLegacySpawnTemplate(original))
            {
                GameObject shared = FindSharedAgmFlightPrefab();
                if (shared != null)
                    return shared;
            }
            return null;
        }

        internal static MissileDefinition ResolveSpawnDefinitionSwap(MissileDefinition original)
        {
            // Sticky _lastFireKey used to remap EVERY SpawnMissile for 15s (SAM IRM-S1 → AGM-68).
            if (!IsAgm68OrKh85Definition(original))
                return null;
            string key = !string.IsNullOrEmpty(_lastFireKey) && Time.time <= _lastFireTime + 15f
                ? _lastFireKey : null;
            if (string.IsNullOrEmpty(key) && PendingLaunches.Count > 0)
                key = PendingLaunches.Peek().Key;
            if (string.IsNullOrEmpty(key) || !IsKh85Key(key))
                return null;
            MissileDefinition def = ResolveDefinition(key);
            if (def == null || def == original)
                return null;
            return def;
        }

        /// <summary>Map / lock / kill-feed identity (definition + unitName + PersistentUnit).</summary>
        internal static void ApplyFullIdentity(Missile missile, string letter, string key)
        {
            if (missile == null)
                return;
            if (string.IsNullOrEmpty(letter))
                letter = "C";

            string display = Plugin.ResolveDisplayName();
            if (Kh85Advanced.VariantName != null && Kh85Advanced.VariantName.ContainsKey(letter))
                display = Kh85Advanced.VariantName[letter];
            display = WithIalDisplay(display);

            MissileDefinition def = ResolveDefinition(key);
            if (def != null)
            {
                def.unitName = display;
                def.code = "85" + letter;
                try { missile.definition = def; }
                catch { }
            }

            try { missile.NetworkunitName = display; }
            catch { }
            try { missile.unitName = display; }
            catch { }

            // Kill feed snapshots PersistentUnit.unitName at RegisterUnit — patch it after spawn.
            try
            {
                PersistentUnit pu;
                if (UnitRegistry.TryGetPersistentUnit(missile.persistentID, out pu) && pu != null)
                {
                    pu.unitName = display;
                    if (def != null)
                        pu.definition = def;
                }
            }
            catch { }
        }

        internal static string ResolveFireKey(Weapon weapon, WeaponMount mount, WeaponInfo stationInfo)
        {
            if (mount != null && !string.IsNullOrEmpty(mount.jsonKey) && IsKh85Key(mount.jsonKey))
                return mount.jsonKey;

            WeaponInfo info = null;
            if (weapon != null)
                info = weapon.info;
            if (info != null)
            {
                int id = info.GetInstanceID();
                foreach (KeyValuePair<string, WeaponInfo> kv in InfoByKey)
                {
                    if (kv.Value != null && kv.Value.GetInstanceID() == id)
                        return kv.Key;
                }
            }
            if (stationInfo != null)
            {
                int id = stationInfo.GetInstanceID();
                foreach (KeyValuePair<string, WeaponInfo> kv in InfoByKey)
                {
                    if (kv.Value != null && kv.Value.GetInstanceID() == id)
                        return kv.Key;
                }
            }

            string L = VariantLetterFromInfo(info);
            if (string.IsNullOrEmpty(L))
                L = VariantLetterFromInfo(stationInfo);
            if (!string.IsNullOrEmpty(L) && L != "C")
                return PackKey + "_" + L;
            if (L == "C")
                return PackKey;
            return PackKey;
        }

        /// <summary>
        /// C / A / B / D / E / S from jsonKey (IAL suffix stripped first).
        /// Must not treat rack words like "_single" as letter S.
        /// </summary>
        internal static string VariantLetterFromKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return "C";
            string k = StripIal(key);
            if (!k.StartsWith(PackKey, StringComparison.OrdinalIgnoreCase))
                return "C";
            string rest = k.Substring(PackKey.Length);
            if (IsLetterToken(rest, "A"))
                return "A";
            if (IsLetterToken(rest, "B"))
                return "B";
            if (IsLetterToken(rest, "D"))
                return "D";
            if (IsLetterToken(rest, "E"))
                return "E";
            if (IsLetterToken(rest, "S"))
                return "S";
            return "C";
        }

        /// <summary>
        /// True for "_A", "_A_…", "_Ax2" (legacy glued). False for "_single" vs S.
        /// </summary>
        private static bool IsLetterToken(string rest, string letter)
        {
            if (string.IsNullOrEmpty(rest) || string.IsNullOrEmpty(letter))
                return false;
            string p = "_" + letter;
            if (!rest.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                return false;
            if (rest.Length == p.Length)
                return true;
            char next = rest[p.Length];
            // "_A_x2" / "_A_single" or legacy "_Ax2" / "_A2"
            if (next == '_' || next == 'x' || next == 'X' || (next >= '0' && next <= '9'))
                return true;
            return false;
        }

        internal static bool IsLetterVariantKey(string key)
        {
            string letter = VariantLetterFromKey(key);
            return letter == "A" || letter == "B" || letter == "D" || letter == "E" || letter == "S";
        }

        internal static bool IsInternalKey(string key)
        {
            string k = StripIal(key);
            return !string.IsNullOrEmpty(k)
                && k.IndexOf("internal", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool IsCFamilyKey(string key)
        {
            if (!IsKh85Key(key))
                return false;
            return !IsLetterVariantKey(key);
        }

        internal static bool AllowVariantInHangar(string key)
        {
            string letter = VariantLetterFromKey(key);
            if (letter == "B")
                return Kh85Advanced.EnableB == null || Kh85Advanced.EnableB.Value;
            if (letter == "A")
                return Kh85Advanced.EnableA == null || Kh85Advanced.EnableA.Value;
            if (letter == "E")
                return Kh85Advanced.EnableE == null || Kh85Advanced.EnableE.Value;
            if (letter == "D")
                return Kh85Advanced.EnableD == null || Kh85Advanced.EnableD.Value;
            if (letter == "S")
                return Kh85Advanced.EnableS == null || Kh85Advanced.EnableS.Value;
            return Kh85Advanced.EnableC == null || Kh85Advanced.EnableC.Value;
        }

        /// <summary>Build letter+rack jsonKey (display [IAL] is separate). Form: Kh85MT_A_x2.</summary>
        internal static string MakeLetterRackKey(string letter, string cFamilyKey, int ammo)
        {
            string c = StripIal(cFamilyKey);
            string rack = "";
            if (!string.IsNullOrEmpty(c) && c.StartsWith(PackKey, StringComparison.OrdinalIgnoreCase))
                rack = c.Substring(PackKey.Length);
            while (!string.IsNullOrEmpty(rack) && rack[0] == '_')
                rack = rack.Substring(1);
            if (string.IsNullOrEmpty(rack))
            {
                if (ammo <= 1)
                    rack = "single";
                else if (ammo == 2)
                    rack = "x2";
                else
                    rack = "triple";
            }
            return PackKey + "_" + letter + "_" + rack;
        }

        private static bool IsDonorLikeMissile(Missile missile)
        {
            if (missile == null)
                return false;
            try
            {
                if (missile.definition != null && IsDonorKey(missile.definition.jsonKey))
                    return true;
            }
            catch { }
            WeaponInfo info = GetMissileInfo(missile);
            if (info == null)
                return false;
            string sn = info.shortName != null ? info.shortName : string.Empty;
            string wn = info.weaponName != null ? info.weaponName : string.Empty;
            return sn.IndexOf("AGM-68", StringComparison.OrdinalIgnoreCase) >= 0
                || wn.IndexOf("AGM-68", StringComparison.OrdinalIgnoreCase) >= 0
                || sn.IndexOf("AGM68", StringComparison.OrdinalIgnoreCase) >= 0
                || wn.IndexOf("AGM68", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// AGM-68 donor in the fire window before VariantTag is stamped. Cruise Seek in that
        /// window detonates on null target / nearest hangar.
        /// </summary>
        internal static bool IsPendingKh85Donor(Missile missile)
        {
            if (missile == null || !IsDonorLikeMissile(missile))
                return false;
            if (string.IsNullOrEmpty(_lastFireKey) || !IsKh85Key(_lastFireKey))
                return false;
            if (Time.time > _lastFireTime + 2.5f)
                return false;
            Unit mOwner = null;
            try { mOwner = missile.owner; }
            catch { }
            if (_lastFireOwner != null && mOwner != null
                && !object.ReferenceEquals(_lastFireOwner, mOwner))
                return false;
            return true;
        }

        private static MissileDefinition FindDonorDefinition(Encyclopedia enc)
        {
            string prefix = Plugin.ResolveDonorPrefix();
            if (enc != null && enc.missiles != null)
            {
                for (int i = 0; i < enc.missiles.Count; i++)
                {
                    MissileDefinition d = enc.missiles[i];
                    if (d != null && string.Equals(d.jsonKey, prefix, StringComparison.OrdinalIgnoreCase))
                        return d;
                }
                for (int i = 0; i < enc.missiles.Count; i++)
                {
                    MissileDefinition d = enc.missiles[i];
                    if (d == null)
                        continue;
                    string code = d.code != null ? d.code : string.Empty;
                    string un = d.unitName != null ? d.unitName : string.Empty;
                    if (code.IndexOf("AGM-68", StringComparison.OrdinalIgnoreCase) >= 0
                        || un.IndexOf("AGM-68", StringComparison.OrdinalIgnoreCase) >= 0)
                        return d;
                }
            }
            MissileDefinition[] all = Resources.FindObjectsOfTypeAll<MissileDefinition>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && string.Equals(all[i].jsonKey, prefix, StringComparison.OrdinalIgnoreCase))
                    return all[i];
            }
            return null;
        }

        private static void RegisterWithEncyclopedia(Encyclopedia enc)
        {
            if (enc == null)
                return;
            EnsureEncyclopediaDef(enc);
            for (int i = 0; i < MountClones.Count; i++)
            {
                WeaponMount clone = MountClones[i];
                if (clone == null || string.IsNullOrEmpty(clone.jsonKey))
                    continue;
                UpsertMount(enc, clone);
                RestoreMountIdentity(clone);
            }
            // Letter racks (A/B/D/E/S) live outside MountClones — must stay in IndexLookup too.
            Kh85Advanced.RegisterLetterMounts(enc);
        }

        private static int CountUsableClones()
        {
            int n = 0;
            for (int i = 0; i < MountClones.Count; i++)
            {
                WeaponMount m = MountClones[i];
                if (m != null && m.prefab != null)
                    n++;
            }
            return n;
        }

        /// <summary>
        /// Instantiated WeaponMount clones can Unity-fake-null after scene unload.
        /// Drop _injected so the next tick recreates TGM-85 racks.
        /// </summary>
        private static void ResetDeadInjection()
        {
            for (int i = MountClones.Count - 1; i >= 0; i--)
            {
                if (MountClones[i] == null)
                    MountClones.RemoveAt(i);
            }
            if (MountClones.Count > 0 && CountUsableClones() == 0)
            {
                try { RepairBrokenPrefabs(); }
                catch { }
            }
            if (!_injected || CountUsableClones() > 0)
                return;

            _injected = false;
            CreatedKeys.Clear();
            MountClones.Clear();
            CachedAgm68Racks.Clear();
            Kh85Advanced.ResetVariants();
            _hardpointIdlePasses = 0;
            _nextHardpointInject = 0f;
            if (Plugin.Log != null)
                Plugin.Log.LogWarning("Kh-85MT: mount clones gone — will re-inject");
        }

        /// <summary>Asset prefabs have no loaded scene. Preference only — in-scene racks still spawn.</summary>
        internal static bool IsPersistentAsset(GameObject go)
        {
            if (go == null)
                return false;
            try
            {
                Scene sc = go.scene;
                if (sc.IsValid() && sc.isLoaded)
                    return false;
            }
            catch { }
            return true;
        }

        private static string RackCacheKey(int ammo, bool internalBay)
        {
            return (internalBay ? "i" : "x") + ammo;
        }

        private static bool ListHasBrokenPrefab(List<WeaponMount> list)
        {
            if (list == null)
                return false;
            for (int i = 0; i < list.Count; i++)
            {
                WeaponMount m = list[i];
                if (m == null)
                    continue;
                if (m.prefab == null)
                    return true;
                if (m.info != null && (m.info.weaponPrefab == null
                    || IsLegacySpawnTemplate(m.info.weaponPrefab)))
                    return true;
            }
            return false;
        }

        private static GameObject FindAgm68Rack(int ammo, bool internalBay)
        {
            string ck = RackCacheKey(ammo, internalBay);
            GameObject cached;
            if (CachedAgm68Racks.TryGetValue(ck, out cached) && cached != null)
                return cached;

            GameObject persistentMatch = null;
            GameObject liveMatch = null;
            GameObject persistentAny = null;
            GameObject liveAny = null;
            try
            {
                WeaponMount[] all = Resources.FindObjectsOfTypeAll<WeaponMount>();
                if (all == null)
                    return null;
                for (int i = 0; i < all.Length; i++)
                {
                    WeaponMount src = all[i];
                    if (src == null || !IsAgm68Mount(src) || IsKh85Key(src.jsonKey))
                        continue;
                    if (src.prefab == null)
                        continue;
                    bool srcInternal = IsInternalKey(src.jsonKey);
                    int a = src.ammo > 0 ? src.ammo : 1;
                    bool persist = IsPersistentAsset(src.prefab);
                    if (srcInternal == internalBay && a == ammo)
                    {
                        if (persist)
                        {
                            persistentMatch = src.prefab;
                            break;
                        }
                        if (liveMatch == null)
                            liveMatch = src.prefab;
                    }
                    else if (persist && persistentAny == null)
                        persistentAny = src.prefab;
                    else if (liveAny == null)
                        liveAny = src.prefab;
                }
            }
            catch { }

            GameObject best = persistentMatch != null ? persistentMatch
                : (liveMatch != null ? liveMatch
                : (persistentAny != null ? persistentAny : liveAny));
            if (best != null)
                CachedAgm68Racks[ck] = best;
            return best;
        }

        /// <summary>Re-point a TGM-85 mount at a live AGM-68 rack. Hangar SpawnMount needs this.</summary>
        internal static bool EnsureMountPrefab(WeaponMount mount)
        {
            if (mount == null)
                return false;
            if (mount.prefab != null)
                return true;
            GameObject rack = FindAgm68Rack(
                mount.ammo > 0 ? mount.ammo : 1,
                IsInternalKey(mount.jsonKey));
            if (rack == null)
                return false;
            mount.prefab = rack;
            return true;
        }

        /// <summary>
        /// Older builds / scene unload destroy Instantiated rack GOs → null prefab →
        /// Hardpoint.SpawnMount NRE and the whole hangar loadout vanishes.
        /// </summary>
        internal static void RepairBrokenPrefabs()
        {
            bool anyBroken = ListHasBrokenPrefab(MountClones)
                || Kh85Advanced.LetterPrefabsBroken();
            if (!anyBroken)
                return;

            GameObject missilePrefab = FindSharedAgmFlightPrefab();
            int fixedN = RepairMountList(MountClones, missilePrefab);
            fixedN += Kh85Advanced.RepairLetterPrefabs(missilePrefab);

            if (fixedN > 0 && Plugin.Log != null)
                Plugin.Log.LogInfo("Kh-85MT: repaired " + fixedN + " mounts with null rack prefab");
        }

        private static int RepairMountList(List<WeaponMount> list, GameObject missilePrefab)
        {
            if (list == null)
                return 0;
            int n = 0;
            for (int i = 0; i < list.Count; i++)
            {
                WeaponMount m = list[i];
                if (m == null)
                    continue;
                if (m.prefab == null && EnsureMountPrefab(m))
                    n++;
                if (m.info != null
                    && (m.info.weaponPrefab == null || IsLegacySpawnTemplate(m.info.weaponPrefab))
                    && missilePrefab != null)
                    m.info.weaponPrefab = missilePrefab;
                RestoreMountIdentity(m);
            }
            return n;
        }

        internal static void RestoreMountIdentity(WeaponMount mount)
        {
            if (mount == null || string.IsNullOrEmpty(mount.jsonKey))
                return;
            WeaponInfo info;
            if (!InfoByKey.TryGetValue(mount.jsonKey, out info) || info == null)
                return;

            try
            {
                if (mount.prefab != null)
                {
                    Weapon[] rails = mount.prefab.GetComponentsInChildren<Weapon>(true);
                    int n = 0;
                    for (int i = 0; i < rails.Length; i++)
                    {
                        if (rails[i] != null && !(rails[i] is Gun))
                            n++;
                    }
                    if (n > 0)
                        mount.ammo = n;
                }
            }
            catch { }

            string letter = VariantLetterFromKey(mount.jsonKey);
            string baseName = Plugin.ResolveDisplayName();
            string desc = EncyclopediaText;
            if (Kh85Advanced.VariantName != null && Kh85Advanced.VariantName.ContainsKey(letter))
                baseName = Kh85Advanced.VariantName[letter];
            if (Kh85Advanced.VariantDesc != null && Kh85Advanced.VariantDesc.ContainsKey(letter))
                desc = Kh85Advanced.VariantDesc[letter];
            string wantName = baseName;
            if (mount.ammo > 1)
                wantName = baseName + " x" + mount.ammo;
            wantName = WithVariantDisplay(wantName, letter);

            ApplyIconToInfo(info);
            info.weaponName = WithVariantDisplay(baseName, letter);
            info.shortName = info.weaponName;
            info.description = desc;
            // C-family (x1/x2/x3…) is the nuclear warhead fit
            if (string.Equals(letter, "C", StringComparison.OrdinalIgnoreCase))
                info.nuclear = true;

            mount.info = info;
            mount.mountName = wantName;
        }

        internal static void ApplyIconToInfo(WeaponInfo info)
        {
            if (info == null)
                return;
            Sprite icon = Kh85Icon.GetWeaponIcon();
            if (icon != null)
                info.weaponIcon = icon;
        }

        internal static void RestoreAllMountIdentities()
        {
            for (int i = 0; i < MountClones.Count; i++)
                RestoreMountIdentity(MountClones[i]);
        }

        internal static List<WeaponMount> GetMountClonesSnapshot()
        {
            return new List<WeaponMount>(MountClones);
        }

        internal static WeaponMount FindPrimaryClone()
        {
            WeaponMount best = null;
            for (int i = 0; i < MountClones.Count; i++)
            {
                WeaponMount m = MountClones[i];
                if (m == null || m.prefab == null || string.IsNullOrEmpty(m.jsonKey))
                    continue;
                if (!IsKh85Key(m.jsonKey))
                    continue;
                if (IsLetterVariantKey(m.jsonKey))
                    continue;
                if (string.Equals(StripIal(m.jsonKey), PackKey, StringComparison.OrdinalIgnoreCase))
                    return m;
                if (best == null)
                    best = m;
            }
            return best;
        }

        internal static void AppendClonesToHardpoint(HardpointSet hs)
        {
            if (hs == null || hs.weaponOptions == null)
                return;
            for (int i = 0; i < MountClones.Count; i++)
            {
                WeaponMount m = MountClones[i];
                if (m == null)
                    continue;
                EnsureMountPrefab(m);
                if (m.prefab == null)
                    continue;
                if (!AllowVariantInHangar(m.jsonKey))
                    continue;
                if (!hs.weaponOptions.Contains(m))
                    hs.weaponOptions.Add(m);
            }
        }

        internal static void AppendClonesToList(List<WeaponMount> list, HashSet<WeaponMount> have)
        {
            if (list == null)
                return;
            for (int i = 0; i < MountClones.Count; i++)
            {
                WeaponMount m = MountClones[i];
                if (m == null)
                    continue;
                EnsureMountPrefab(m);
                if (m.prefab == null)
                    continue;
                if (!AllowVariantInHangar(m.jsonKey))
                    continue;
                if (have != null)
                {
                    if (!have.Add(m))
                        continue;
                }
                else if (list.Contains(m))
                    continue;
                list.Add(m);
            }
        }

        internal static void InjectIntoAircraftHardpoints()
        {
            if (!_injected || MountClones.Count == 0)
                return;

            float backoff = _hardpointIdlePasses >= 3 ? 20f : 5f;
            _nextHardpointInject = Time.unscaledTime + backoff;

            WeaponManager[] managers = null;
            try { managers = Resources.FindObjectsOfTypeAll<WeaponManager>(); }
            catch (Exception ex)
            {
                LogHardpointSkipOnce("FindWeaponManagers", ex);
                return;
            }
            if (managers == null || managers.Length == 0)
                return;

            int added = 0;
            for (int i = 0; i < managers.Length; i++)
            {
                try { added += InjectIntoWeaponManager(managers[i]); }
                catch (Exception ex)
                {
                    LogHardpointSkipOnce(WeaponManagerKey(managers[i]), ex);
                }
            }

            if (added <= 0)
                _hardpointIdlePasses++;
            else
            {
                _hardpointIdlePasses = 0;
                if (Plugin.DebugLog != null && Plugin.DebugLog.Value && Plugin.Log != null)
                    Plugin.Log.LogInfo("Kh-85MT: wired " + added + " options onto aircraft hardpoints");
            }
        }

        /// <summary>Stable key for once-per-aircraft skip logs (modded Aryx / empty pylons).</summary>
        private static string WeaponManagerKey(WeaponManager wm)
        {
            if (wm == null)
                return "wm:null";
            try
            {
                Aircraft ac = wm.GetComponentInParent<Aircraft>();
                if (ac != null && ac.gameObject != null && !string.IsNullOrEmpty(ac.gameObject.name))
                    return "ac:" + ac.gameObject.name;
                if (wm.gameObject != null && !string.IsNullOrEmpty(wm.gameObject.name))
                    return "wm:" + wm.gameObject.name;
                if (!string.IsNullOrEmpty(wm.name))
                    return "wm:" + wm.name;
            }
            catch { }
            try { return "wm#" + wm.GetInstanceID(); }
            catch { return "wm:?"; }
        }

        private static void LogHardpointSkipOnce(string key, Exception ex)
        {
            if (string.IsNullOrEmpty(key))
                key = "?";
            if (!HardpointSkipLogged.Add(key))
                return;
            if (Plugin.Log == null)
                return;
            string msg = ex != null ? (ex.GetType().Name + ": " + ex.Message) : "incomplete hardpoints";
            Plugin.Log.LogWarning("Kh-85MT: skip hardpoint inject [" + key + "] — " + msg);
        }

        internal static int InjectIntoWeaponManager(WeaponManager wm)
        {
            if (!_injected || MountClones.Count == 0 || wm == null)
                return 0;
            // Incomplete / modded aircraft: missing hardpointSets → skip, never NRE.
            HardpointSet[] sets;
            try { sets = wm.hardpointSets; }
            catch (Exception ex)
            {
                LogHardpointSkipOnce(WeaponManagerKey(wm), ex);
                return 0;
            }
            if (sets == null || sets.Length == 0)
                return 0;
            if (ShouldSkipNonAircraftWeaponManager(wm))
                return 0;

            int added = 0;
            try
            {
                for (int h = 0; h < sets.Length; h++)
                {
                    HardpointSet hs = sets[h];
                    if (hs == null)
                        continue;
                    if (hs.weaponOptions == null)
                        hs.weaponOptions = new List<WeaponMount>();
                    added += AddKh85ToHardpoint(hs);
                }
                Kh85Advanced.InjectVariants(wm);
            }
            catch (Exception ex)
            {
                LogHardpointSkipOnce(WeaponManagerKey(wm), ex);
            }
            return added;
        }

        /// <summary>
        /// TGM-85 is aircraft-pylon only. Ships, SAM trucks, and buildings must not get clones.
        /// </summary>
        private static bool ShouldSkipNonAircraftWeaponManager(WeaponManager wm)
        {
            if (wm == null)
                return true;
            int id = wm.GetInstanceID();
            if (ShipWmIds.Contains(id))
                return true;
            if (AirWmIds.Contains(id))
                return false;
            try
            {
                if (wm.GetComponentInParent<Aircraft>() != null)
                {
                    AirWmIds.Add(id);
                    return false;
                }
            }
            catch { }
            ShipWmIds.Add(id);
            return true;
        }

        private static int AddKh85ToHardpoint(HardpointSet hs)
        {
            if (hs == null)
                return 0;
            if (hs.weaponOptions == null)
                hs.weaponOptions = new List<WeaponMount>();

            HashSet<WeaponMount> want = new HashSet<WeaponMount>();
            bool matched = false;
            bool hasMissileSlot = false;

            for (int i = 0; i < hs.weaponOptions.Count; i++)
            {
                WeaponMount m = hs.weaponOptions[i];
                if (m == null)
                    continue;
                try
                {
                    if (m.prefab != null && m.prefab.GetComponent<Missile>() != null)
                        hasMissileSlot = true;
                    else if (m.info != null && m.info.missile)
                        hasMissileSlot = true;

                    if (IsKh85Mount(m))
                        continue;
                    if (!IsAgm68Mount(m))
                        continue;
                    matched = true;
                    string baseKey = !string.IsNullOrEmpty(m.jsonKey) ? m.jsonKey
                        : (!string.IsNullOrEmpty(m.name) ? m.name : Plugin.ResolveDonorPrefix());
                    WeaponMount clone = FindCloneByKey(RemapKey(baseKey));
                    if (clone != null)
                    {
                        want.Add(clone);
                        Kh85Advanced.CollectLetterMountsForAmmo(clone.ammo, want);
                    }
                }
                catch
                {
                    // Broken mount entry on modded aircraft — skip this option only.
                    continue;
                }
            }

            // Hangar GetAvailableWeapons already lists every TGM-85 clone. Without the same
            // set on weaponOptions, VetWeapon strips the pick on Fly (empty stations / no model).
            if (hasMissileSlot || matched)
            {
                for (int i = 0; i < MountClones.Count; i++)
                {
                    WeaponMount cm = MountClones[i];
                    if (cm == null)
                        continue;
                    EnsureMountPrefab(cm);
                    if (cm.prefab != null && AllowVariantInHangar(cm.jsonKey))
                    {
                        want.Add(cm);
                        Kh85Advanced.CollectLetterMountsForAmmo(cm.ammo > 0 ? cm.ammo : 1, want);
                    }
                }
            }

            if (!matched && want.Count == 0)
                return 0;

            int added = 0;
            foreach (WeaponMount m in want)
            {
                if (m == null)
                    continue;
                EnsureMountPrefab(m);
                if (m.prefab == null)
                    continue;
                if (!AllowVariantInHangar(m.jsonKey))
                    continue;
                if (hs.weaponOptions.Contains(m))
                    continue;
                hs.weaponOptions.Add(m);
                added++;
            }
            return added;
        }

        private static WeaponMount FindCloneByKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;
            for (int i = 0; i < MountClones.Count; i++)
            {
                WeaponMount m = MountClones[i];
                if (m != null && string.Equals(m.jsonKey, key, StringComparison.OrdinalIgnoreCase))
                    return m;
            }
            return null;
        }

        internal static void SyncFromMount(Weapon weapon, WeaponMount mount)
        {
            if (weapon == null || mount == null || weapon is Gun)
                return;
            if (!IsKh85Mount(mount))
                return;
            RestoreMountIdentity(mount);
            if (mount.info != null)
                weapon.info = mount.info;
        }

        /// <param name="owner">Launching unit from MountedMissile.Fire.</param>
        /// <param name="fireTarget">Unit passed into MountedMissile.Fire — the player's lock.</param>
        internal static void NoteFire(Weapon weapon, Unit owner, Unit fireTarget)
        {
            if (Plugin.Enabled == null || !Plugin.Enabled.Value || weapon == null)
                return;

            WeaponMount mount = Plugin.GetWeaponMount(weapon);
            WeaponInfo stationInfo = null;
            try
            {
                if (WeaponStationField != null)
                {
                    WeaponStation st = WeaponStationField.GetValue(weapon) as WeaponStation;
                    if (st != null)
                        stationInfo = st.WeaponInfo;
                }
            }
            catch { }

            bool isKh = IsKh85Mount(mount) || IsKh85Info(weapon.info) || IsKh85Info(stationInfo);
            if (!isKh)
                return;

            RestoreMountIdentity(mount);
            if (mount != null && mount.info != null)
                weapon.info = mount.info;

            Unit launchOwner = owner;
            if (launchOwner == null)
            {
                try { launchOwner = weapon.GetComponentInParent<Aircraft>(); }
                catch { }
            }

            // Prefer the exact Unit the game handed to Fire() — never invent from WM list.
            Unit tgt = SanitizeLockTarget(launchOwner, fireTarget);
            if (tgt == null)
            {
                try { tgt = SanitizeLockTarget(launchOwner, weapon.GetTarget()); }
                catch { }
            }
            if (tgt == null)
            {
                try
                {
                    Aircraft ac = launchOwner as Aircraft;
                    if (ac == null && launchOwner != null)
                        ac = launchOwner.GetComponentInParent<Aircraft>();
                    if (ac != null && ac.pilots != null)
                    {
                        for (int i = 0; i < ac.pilots.Length; i++)
                        {
                            Pilot p = ac.pilots[i];
                            if (p == null)
                                continue;
                            tgt = SanitizeLockTarget(launchOwner, p.GetPrimaryTarget());
                            if (tgt != null)
                                break;
                        }
                    }
                }
                catch { }
            }

            string key = ResolveFireKey(weapon, mount, stationInfo);
            int wid = 0;
            try { wid = weapon.GetInstanceID(); }
            catch { }

            // Refresh sticky always — Fire() is spammed every frame / per rail.
            if (tgt != null)
                _lastFireTarget = tgt;
            _lastFireOwner = launchOwner;
            _lastFireKey = key;
            _lastFireTime = Time.time;
            _pendingExpire = Time.time + 12f;

            // Debounce enqueue: same weapon within 0.45s only refreshes sticky.
            if (wid != 0 && wid == _lastNoteWeaponId && (Time.time - _lastNoteTime) < 0.45f)
                return;
            // Same key+target already queued — do not flood (x8 rails were filling queue=6).
            if (PendingHasMatch(key, tgt, launchOwner))
            {
                _lastNoteWeaponId = wid;
                _lastNoteTime = Time.time;
                return;
            }
            _lastNoteWeaponId = wid;
            _lastNoteTime = Time.time;

            while (PendingLaunches.Count >= 8)
                PendingLaunches.Dequeue();

            PendingLaunch pending = new PendingLaunch();
            pending.Key = key;
            pending.Target = tgt;
            pending.Owner = launchOwner;
            pending.WeaponId = wid;
            PendingLaunches.Enqueue(pending);
            if (Plugin.Log != null)
                Plugin.Log.LogInfo("Kh-85MT: NoteFire key=" + key
                    + " tgt=" + (tgt != null ? tgt.name : "none")
                    + " queue=" + PendingLaunches.Count);
        }

        private static bool PendingHasMatch(string key, Unit tgt, Unit owner)
        {
            if (PendingLaunches.Count == 0)
                return false;
            foreach (PendingLaunch p in PendingLaunches)
            {
                if (p == null)
                    continue;
                if (!string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (tgt != null && p.Target != null && !object.ReferenceEquals(p.Target, tgt))
                    continue;
                if (owner != null && p.Owner != null && !object.ReferenceEquals(p.Owner, owner))
                    continue;
                return true;
            }
            return false;
        }

        /// <summary>Reject locks on the launching aircraft / missile itself (shows as "lock on self").</summary>
        internal static Unit SanitizeLockTarget(Unit owner, Unit target)
        {
            if (target == null)
                return null;
            if (owner != null && object.ReferenceEquals(owner, target))
                return null;
            try
            {
                Aircraft oa = owner as Aircraft;
                if (oa == null && owner != null)
                    oa = owner.GetComponentInParent<Aircraft>();
                Aircraft ta = target as Aircraft;
                if (ta == null)
                    ta = target.GetComponentInParent<Aircraft>();
                if (oa != null && ta != null && object.ReferenceEquals(oa, ta))
                    return null;
            }
            catch { }
            if (target is Missile)
                return null;
            if (target is Scenery)
                return null;
            // Confirmed same HQ (NetworkHQ or MapHQ). Incomplete HQ is not treated as friendly
            // here — player TGP on an enemy tank must still stick. Hunt uses IsStrictHostile.
            if (Kh85Util.IsConfirmedFriendly(owner, target))
                return null;
            return target;
        }

        /// <summary>Called from Spawner + late Missile bootstrap.</summary>
        internal static void OnSpawned(Missile missile)
        {
            if (missile == null || Plugin.Enabled == null || !Plugin.Enabled.Value)
                return;

            // Already bootstrapped this instance.
            if (missile.GetComponent<Kh85VariantTag>() != null)
            {
                NoteKh85Missile(missile);
                ApplyMotorMultipliers(missile);
                return;
            }

            bool alreadyKh = IsKh85Missile(missile) || IsKh85Info(GetMissileInfo(missile));
            bool donorLike = IsDonorLikeMissile(missile);
            string infoLetter = VariantLetterFromInfo(GetMissileInfo(missile));
            bool infoKh = !string.IsNullOrEmpty(infoLetter);

            Unit mOwner = null;
            try { mOwner = missile.owner; }
            catch { }

            // Claim a pending ONLY for real Kh85 shots. Never let random AGM-68 / AI steal the queue
            // (that converted every donor missile and broke all locks/guidance).
            PendingLaunch pending = null;
            if (PendingLaunches.Count > 0 && Time.time <= _pendingExpire)
            {
                PendingLaunch peek = PendingLaunches.Peek();
                bool keyOk = peek != null && IsKh85Key(peek.Key);
                bool identityOk = alreadyKh || infoKh || (donorLike && keyOk);
                bool ownerOk = peek == null || peek.Owner == null || mOwner == null
                    || object.ReferenceEquals(peek.Owner, mOwner);
                if (keyOk && identityOk && ownerOk)
                    pending = PendingLaunches.Dequeue();
            }

            // Sticky covers delayed rail launches — but NEVER convert bare vanilla AGM-68.
            bool stickyOk = !string.IsNullOrEmpty(_lastFireKey)
                && Time.time <= _lastFireTime + 15f
                && (alreadyKh || infoKh
                    || (donorLike && pending == null && IsKh85Key(_lastFireKey)
                        && (_lastFireOwner == null || mOwner == null
                            || object.ReferenceEquals(_lastFireOwner, mOwner))));

            if (!alreadyKh && pending == null && !stickyOk && !infoKh)
                return;

            string key = pending != null ? pending.Key : null;
            if (string.IsNullOrEmpty(key) && stickyOk)
                key = _lastFireKey;
            if (string.IsNullOrEmpty(key) && missile.definition != null
                && IsKh85Key(missile.definition.jsonKey))
                key = missile.definition.jsonKey;
            // Fallback: identity from WeaponInfo name (letter mounts share AGM prefab defs).
            if (string.IsNullOrEmpty(key) || !IsLetterVariantKey(key))
            {
                if (!string.IsNullOrEmpty(infoLetter) && infoLetter != "C")
                    key = PackKey + "_" + infoLetter;
                else if (!string.IsNullOrEmpty(infoLetter))
                    key = PackKey;
            }
            if (string.IsNullOrEmpty(key))
                key = PackKey;

            string letter = VariantLetterFromKey(key);
            if (!IsLetterVariantKey(key) && !string.IsNullOrEmpty(infoLetter))
                letter = infoLetter;
            // Authoritative stamp BEFORE TryAttach — GetVariant reads this first.
            Kh85Util.StampVariant(missile, letter, key);
            NoteKh85Missile(missile);
            ApplyFullIdentity(missile, letter, key);

            WeaponInfo info = GetMissileInfo(missile);
            if (info == null || !IsKh85Info(info))
            {
                WeaponInfo prefer = null;
                if (!InfoByKey.TryGetValue(key, out prefer) || prefer == null)
                {
                    string letterKey = PackKey + (letter == "C" ? "" : "_" + letter);
                    if (letter != "C")
                        InfoByKey.TryGetValue(letterKey, out prefer);
                }
                if (prefer == null)
                    InfoByKey.TryGetValue(PackKey + "_single", out prefer);
                if (prefer == null)
                    InfoByKey.TryGetValue(PackKey, out prefer);
                if (prefer != null)
                    SetMissileInfo(missile, prefer);
            }

            Unit launchTgt = pending != null ? pending.Target : null;
            bool playerDesignated = launchTgt != null;
            // Queue miss (delayed rail): sticky last-fire is this shot. LOAL pending.Target==null
            // must NOT inherit a previous hangar lock from _lastFireTarget.
            if (launchTgt == null && pending == null && stickyOk)
            {
                launchTgt = _lastFireTarget;
                playerDesignated = launchTgt != null;
            }
            if (launchTgt == null)
            {
                // Only use a target the game already put on this missile — never WM-list invent.
                try
                {
                    if (TargetField != null)
                        launchTgt = TargetField.GetValue(missile) as Unit;
                }
                catch { }
            }
            launchTgt = SanitizeLockTarget(mOwner != null ? mOwner : _lastFireOwner, launchTgt);
            // D: only radar emitters — rewrite / hunt if needed.
            if (Kh85DArm.IsDVariant(missile))
                launchTgt = Kh85DArm.FilterLaunchTarget(missile, launchTgt);
            launchTgt = SanitizeLockTarget(mOwner != null ? mOwner : _lastFireOwner, launchTgt);
            // Hangar auto-pick only. Never wipe a Fire()/spawn lock on ships/vehicles
            // (incomplete HQ used to look like "no target" and restored vanish-on-fire).
            if (!playerDesignated && launchTgt is Container)
                launchTgt = null;
            if (!playerDesignated && launchTgt is Building
                && !Kh85Util.IsStrictHostile(missile, launchTgt))
                launchTgt = null;
#if ORITASY_COMBINED
            if (!playerDesignated && launchTgt != null)
            {
                try
                {
                    if (WeXon.Plugin.IsJunkHuntTarget(launchTgt))
                        launchTgt = null;
                }
                catch { }
            }
#endif
            if (launchTgt != null)
            {
                // Sticky lock: do NOT enable freeHunt — seeker hunt was yanking aim off the lock.
                ApplyTargetLock(missile, launchTgt);
                DisableFreeHunt(missile);
                // Sticky lock for the whole flight (A/B rely on this for tracking).
                Kh85LockHold.Attach(missile, launchTgt);
            }
            else
            {
                Unit cur = null;
                try
                {
                    if (TargetField != null)
                        cur = TargetField.GetValue(missile) as Unit;
                }
                catch { }
                if (cur is Building && !Kh85Util.IsStrictHostile(missile, cur))
                {
                    try { missile.SetTarget(null); }
                    catch { }
                }
                // LOAL / 自锁 only when Guidance.AutoSeek or MultiMode is on.
                // Standalone: Kh85SelfHunt acquires a target; WeXon/Oritasy MM when present.
                bool wantLoal = (Kh85Advanced.AutoSeek != null && Kh85Advanced.AutoSeek.Value)
                    || (Kh85Advanced.MultiMode != null && Kh85Advanced.MultiMode.Value);
                if (wantLoal)
                    EnableSelfLock(missile);
            }

            Kh85Live.Register(missile);

            // Attach ability brains using stamped letter.
            Kh85CFlight.TryAttach(missile);
            Kh85AEcm.TryAttach(missile);
            Kh85BEcm.TryAttach(missile);
            Kh85EDecoy.TryAttach(missile);
            Kh85DArm.TryAttach(missile);
            Kh85SHyper.TryAttach(missile);

            if (Plugin.Log != null)
                Plugin.Log.LogInfo("Kh-85MT: OnSpawned letter=" + letter
                    + " key=" + key
                    + " pending=" + (pending != null)
                    + " sticky=" + stickyOk
                    + " A=" + (missile.GetComponent<Kh85AEcmBrain>() != null)
                    + " B=" + (missile.GetComponent<Kh85BEcmBrain>() != null)
                    + " C=" + (missile.GetComponent<Kh85CFlightBrain>() != null)
                    + " D=" + (missile.GetComponent<Kh85DArmBrain>() != null)
                    + " E=" + (missile.GetComponent<Kh85EDecoyBrain>() != null)
                    + " S=" + (missile.GetComponent<Kh85SHyperBrain>() != null));

            if (Plugin.CustomVisual == null || Plugin.CustomVisual.Value)
                Kh85Visual.ApplyToMissile(missile);

            ApplyMotorMultipliers(missile);
        }

        /// <summary>Public retry for S motors / late bootstrap.</summary>
        internal static void EnsureMotors(Missile missile)
        {
            ApplyMotorMultipliers(missile);
        }

        /// <summary>Infer A–S from WeaponInfo display names when jsonKey is still C/donor.</summary>
        internal static string VariantLetterFromInfo(WeaponInfo info)
        {
            if (info == null)
                return null;
            string w = info.weaponName != null ? info.weaponName : string.Empty;
            string s = info.shortName != null ? info.shortName : string.Empty;
            string n = w + " " + s;
            if (n.IndexOf("TGM-85A", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Coordinator", StringComparison.OrdinalIgnoreCase) >= 0)
                return "A";
            if (n.IndexOf("TGM-85B", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Torch", StringComparison.OrdinalIgnoreCase) >= 0)
                return "B";
            if (n.IndexOf("TGM-85D", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Hegemony", StringComparison.OrdinalIgnoreCase) >= 0)
                return "D";
            if (n.IndexOf("TGM-85E", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Torjan", StringComparison.OrdinalIgnoreCase) >= 0)
                return "E";
            if (n.IndexOf("TGM-85S", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Seaker", StringComparison.OrdinalIgnoreCase) >= 0)
                return "S";
            if (n.IndexOf("TGM-85C", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Shardfall", StringComparison.OrdinalIgnoreCase) >= 0)
                return "C";
            return null;
        }

        private static Unit ResolveFireDesignatedTarget(Weapon weapon, Unit missileOwner)
        {
            try
            {
                Aircraft ac = null;
                if (weapon != null)
                    ac = weapon.GetComponentInParent<Aircraft>();
                if (ac == null && missileOwner != null)
                {
                    ac = missileOwner as Aircraft;
                    if (ac == null)
                        ac = missileOwner.GetComponentInParent<Aircraft>();
                }
                if (ac == null)
                {
                    Aircraft local;
                    if (GameManager.GetLocalAircraft(out local))
                        ac = local;
                }
                if (ac == null)
                    return null;
                if (ac.pilots != null)
                {
                    for (int i = 0; i < ac.pilots.Length; i++)
                    {
                        Pilot p = ac.pilots[i];
                        if (p == null)
                            continue;
                        Unit pt = p.GetPrimaryTarget();
                        if (pt != null)
                            return pt;
                    }
                }
                if (ac.weaponManager != null)
                {
                    List<Unit> list = ac.weaponManager.GetTargetList();
                    if (list != null)
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            if (list[i] != null)
                                return list[i];
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        internal static Unit ResolveMissileDesignatedTarget(Missile missile)
        {
            if (missile == null)
                return null;
            try
            {
                Unit mt = TargetField != null
                    ? TargetField.GetValue(missile) as Unit
                    : null;
                if (mt != null)
                    return mt;
            }
            catch { }
            try
            {
                PersistentID tid = missile.targetID;
                if (tid.Id != 0u)
                {
                    Unit u;
                    if (tid.TryGetUnit(out u) && u != null)
                        return u;
                }
            }
            catch { }
            // Do not invent from the owner's WM target list mid-flight.
            return null;
        }

        private static readonly FieldInfo TargetField = AccessTools.Field(typeof(Missile), "target");

        internal static void ApplyLaunchTargetPublic(Missile missile, Unit target)
        {
            if (Kh85DArm.IsDVariant(missile))
            {
                target = Kh85DArm.FilterLaunchTarget(missile, target);
                if (target == null)
                    return;
            }
            ApplyTargetLock(missile, target);
            // A/B/D: lead aim after launch grace. C/E/S own aim via their Steering prefixes.
            if (ShouldDeferAim(missile))
                return;
            if (!Kh85CFlight.IsCVariant(missile)
                && !Kh85EDecoy.IsEVariant(missile)
                && !Kh85SHyper.IsSVariant(missile))
                ApplyLeadAim(missile, target);
        }

        /// <summary>
        /// LOAL / 自锁: fire without a lock, then actively search and acquire.
        /// Sets activeSearch; attaches Kh85SelfHunt when WeXon/Oritasy MM is absent
        /// (DebugRef has no freeHunt — those writes were dead).
        /// </summary>
        internal static void EnableSelfLock(Missile missile)
        {
            if (missile == null)
                return;
            try { missile.seekerMode = Missile.SeekerMode.activeSearch; }
            catch { }
            try
            {
                FieldInfo seekerField = MissileSeekerField;
                if (seekerField != null)
                {
                    MissileSeeker seeker = seekerField.GetValue(missile) as MissileSeeker;
                    if (seeker != null)
                    {
                        SetSeekerBool(seeker, "guidance", true);
                        SetSeekerBool(seeker, "targetOnLaunch", false);
                        SetSeekerBool(seeker, "deployedFins", true);
                        SetSeekerBool(seeker, "finsDeployed", true);
                        SetSeekerBool(seeker, "armed", true);
                    }
                }
            }
            catch { }
            // Lightweight standalone hunt when MM brain is missing (packed AddComponent can fail).
            bool hasMm = false;
#if ORITASY_COMBINED
            try { hasMm = missile.GetComponent<WeXon.MultiModeBrain>() != null; }
            catch { hasMm = false; }
#endif
            if (!hasMm)
                Kh85SelfHunt.Attach(missile);
        }

        /// <summary>Clear freeHunt when we already have a sticky launch lock.</summary>
        internal static void DisableFreeHunt(Missile missile)
        {
            if (missile == null)
                return;
            try
            {
                FieldInfo seekerField = MissileSeekerField;
                if (seekerField == null)
                    return;
                MissileSeeker seeker = seekerField.GetValue(missile) as MissileSeeker;
                if (seeker == null)
                    return;
                SetSeekerFreeHunt(seeker, false);
                SetSeekerBool(seeker, "guidance", true);
                SetSeekerBool(seeker, "targetOnLaunch", true);
            }
            catch { }
        }

        private static void SetSeekerFreeHunt(MissileSeeker seeker, bool value)
        {
            if (seeker == null)
                return;
            // DeclaredOnly walk + negative cache — NEVER AccessTools.Field (HarmonyX logs every miss).
            FieldInfo hunt = ResolveSeekerField(seeker.GetType(), "freeHunt");
            if (hunt != null && hunt.FieldType == typeof(bool))
                hunt.SetValue(seeker, value);
        }

        /// <summary>Stick seeker/missile target without overwriting C sea-skim aimpoints.</summary>
        internal static void ApplyTargetLock(Missile missile, Unit target)
        {
            if (missile == null || target == null)
                return;
            Unit owner = null;
            try { owner = missile.owner; }
            catch { }
            target = SanitizeLockTarget(owner, target);
            if (target == null)
                return;
            if (object.ReferenceEquals(target, missile))
                return;
            try { missile.SetTarget(target); }
            catch { }
            try
            {
                FieldInfo seekerField = MissileSeekerField;
                if (seekerField == null)
                    return;
                MissileSeeker seeker = seekerField.GetValue(missile) as MissileSeeker;
                if (seeker == null)
                    return;
                FieldInfo tu = AccessTools.Field(typeof(MissileSeeker), "targetUnit");
                if (tu != null)
                    tu.SetValue(seeker, target);
                GlobalPosition gp = target.GlobalPosition();
                Vector3 vel = Vector3.zero;
                try
                {
                    if (target.rb != null)
                        vel = target.rb.velocity;
                }
                catch { }
                SetSeekerField(seeker, "knownPos", gp);
                SetSeekerField(seeker, "aimPos", gp);
                SetSeekerField(seeker, "knownVel", vel);
                SetSeekerField(seeker, "targetTransform", target.transform);
                SetSeekerBool(seeker, "hasVisual", true);
                SetSeekerBool(seeker, "guidance", true);
                SetSeekerBool(seeker, "targetOnLaunch", true);
                SetSeekerBool(seeker, "deployedFins", true);
                SetSeekerBool(seeker, "finsDeployed", true);
                SetSeekerBool(seeker, "armed", true);
            }
            catch { }
        }

        /// <summary>
        /// Scene/local Vector3 → GlobalPosition. NEVER use new GlobalPosition(local):
        /// far from world origin that aims ~180° away while SetTarget stays correct.
        /// </summary>
        internal static GlobalPosition LocalToGlobal(Vector3 local)
        {
            try { return local.ToGlobalPosition(); }
            catch { return new GlobalPosition(local); }
        }

        private const int TerrainProbeMask = 8256;

        /// <summary>Sample ground/sea under a local XY. Used to stop mid-course aim-into-dirt teleports.</summary>
        internal static float SampleFloorY(Vector3 localPos)
        {
            float seaY = 0f;
            try { seaY = Datum.LocalSeaY; }
            catch { }
            RaycastHit hit;
            Vector3 origin = new Vector3(localPos.x, localPos.y + 800f, localPos.z);
            if (origin.y < seaY + 100f)
                origin.y = seaY + 1200f;
            float cast = Mathf.Max(3000f, origin.y - (seaY - 200f));
            if (Physics.Raycast(origin, Vector3.down, out hit, cast, TerrainProbeMask))
            {
                float y = hit.point.y;
                if (y < seaY + 2.5f)
                    return seaY;
                return y;
            }
            return seaY;
        }

        /// <summary>
        /// Raise aim above terrain/sea and rate-limit dive. Sticky SetAimpoint every frame
        /// was killing vanilla loft — missile dove into dirt/water; DetectCollisions then
        /// snapped transform to the hit (slow-mo "teleport") and detonated.
        /// </summary>
        internal static void RaiseAimClearOfTerrain(Vector3 mpos, ref Vector3 aim, float minAgl, float maxDropPerTick)
        {
            if (minAgl < 8f)
                minAgl = 8f;
            float floor = SampleFloorY(aim);
            float floorHere = SampleFloorY(mpos);
            if (floorHere > floor)
                floor = floorHere;
            float minY = floor + minAgl;
            if (aim.y < minY)
                aim.y = minY;
            if (maxDropPerTick > 1f && aim.y < mpos.y - maxDropPerTick)
                aim.y = mpos.y - maxDropPerTick;
            // Never command below sea + margin while mid-air.
            float seaY = 0f;
            try { seaY = Datum.LocalSeaY; }
            catch { }
            if (aim.y < seaY + minAgl)
                aim.y = seaY + minAgl;
        }

        /// <summary>Local aim → SetAimpoint with terrain floor. Prefer this over raw SetAimpoint.</summary>
        internal static void SafeSetAimpoint(Missile missile, Vector3 localAim, Vector3 targetVel, float minAgl, float maxDropPerTick)
        {
            if (missile == null)
                return;
            // Yield to MissileCamera / CI22XE MCLOS while the stick is active.
            if (Kh85MclosGate.ManualActive)
                return;
            Vector3 mpos = missile.transform.position;
            RaiseAimClearOfTerrain(mpos, ref localAim, minAgl, maxDropPerTick);
            try
            {
                missile.SetAimpoint(LocalToGlobal(localAim), targetVel);
            }
            catch
            {
                try { missile.SetAimpoint(LocalToGlobal(localAim), Vector3.zero); }
                catch { }
            }
        }

        /// <summary>
        /// Re-lock / large-aspect: cap commanded off-boresight so Kh85 does not stall itself.
        /// </summary>
        internal static float MaxSteerOffBoresightDeg(float speed, float aspectDeg)
        {
            float maxDeg;
            if (speed < 100f)
                maxDeg = 18f;
            else if (speed < 200f)
                maxDeg = Mathf.Lerp(18f, 48f, (speed - 100f) / 100f);
            else if (speed < 400f)
                maxDeg = Mathf.Lerp(48f, 65f, (speed - 200f) / 200f);
            else
                maxDeg = 72f;
            if (aspectDeg > 70f)
                maxDeg *= Mathf.Lerp(1f, 0.72f, Mathf.InverseLerp(70f, 120f, aspectDeg));
            return Mathf.Clamp(maxDeg, 12f, 80f);
        }

        internal static Vector3 EnergyLead(Vector3 mpos, Vector3 mvel, float speed, Vector3 tpos, Vector3 tvel)
        {
            Vector3 toTgt = tpos - mpos;
            float dist = toTgt.magnitude;
            if (dist < 1f)
                return tpos;
            Vector3 fwd = mvel.sqrMagnitude > 1f ? mvel.normalized : toTgt / dist;
            float closing = Vector3.Dot(toTgt / dist, mvel - tvel);
            float tGo = closing > 25f ? dist / closing : dist / Mathf.Max(speed, 50f);
            tGo = Mathf.Clamp(tGo, 0.12f, 6f);
            float aspect = Vector3.Angle(fwd, toTgt);
            float leadK = aspect > 55f || Vector3.Dot(fwd, toTgt) < 0.15f ? 0.45f : 0.75f;
            return tpos + tvel * (tGo * leadK);
        }

        internal static Vector3 ClampAimOffBoresight(Vector3 mpos, Vector3 vel, float speed, Vector3 aim)
        {
            if (vel.sqrMagnitude < 1f)
                return aim;
            Vector3 toAim = aim - mpos;
            if (toAim.sqrMagnitude < 0.01f)
                return aim;
            Vector3 velDir = vel.normalized;
            Vector3 want = toAim.normalized;
            float aspect = Vector3.Angle(velDir, want);
            float maxDeg = MaxSteerOffBoresightDeg(speed, aspect);
            if (aspect <= maxDeg)
                return aim;
            want = Vector3.RotateTowards(velDir, want, maxDeg * Mathf.Deg2Rad, 0f);
            float look = Mathf.Clamp(speed * 2.4f, 600f, 4500f);
            if (look < speed * 0.55f)
                look = speed * 0.55f;
            Vector3 clamped = mpos + want * look;
            if (aspect < 40f)
                clamped.y = aim.y;
            return clamped;
        }

        /// <summary>Direct lead aim for A/B/D — sticky lock with mid-course loft / terrain floor.</summary>
        internal static void ApplyLeadAim(Missile missile, Unit target)
        {
            if (missile == null || target == null)
                return;
            if (ShouldDeferAim(missile))
                return;
            try
            {
                Vector3 mpos = missile.transform.position;
                Vector3 mvel = missile.rb != null ? missile.rb.velocity : missile.transform.forward * 200f;
                float speed = Mathf.Max(mvel.magnitude, 80f);
                Vector3 tpos = target.transform.position;
                Vector3 tvel = target.rb != null ? target.rb.velocity : Vector3.zero;
                float dist = Vector3.Distance(mpos, tpos);
                Vector3 aim = EnergyLead(mpos, mvel, speed, tpos, tvel);

                // Mid-course loft: sticky aim used to point straight at the target and fly
                // through terrain — DetectCollisions position-snaps that as a teleport.
                float floor = SampleFloorY(mpos);
                float clearAgl = 90f;
                float loft = Mathf.Clamp(dist * 0.12f, 120f, 2800f);
                if (dist > 2200f)
                {
                    float cruiseY = floor + clearAgl + loft * Mathf.Clamp01((dist - 2200f) / 9000f);
                    // Hold height / climb corridor — do not dive while far.
                    if (mpos.y > cruiseY - 40f)
                        aim.y = Mathf.Max(aim.y, cruiseY);
                    else
                        aim.y = Mathf.Max(aim.y, mpos.y + Mathf.Clamp(cruiseY - mpos.y, 80f, 500f));
                }
                else if (dist > 700f)
                {
                    float u = 1f - Mathf.Clamp01((dist - 700f) / 1500f);
                    float minY = floor + Mathf.Lerp(clearAgl, 25f, u * u);
                    if (aim.y < minY)
                        aim.y = minY;
                }
                else
                {
                    float minY = floor + 12f;
                    if (aim.y < minY)
                        aim.y = minY;
                }

                // Soft pitch clamp — never command a near-vertical dirt dive mid-course,
                // and never yank 90°+ on re-lock (that stalled the airframe).
                aim = ClampAimOffBoresight(mpos, mvel, speed, aim);

                float age = 1f;
                try { age = Mathf.Max(missile.timeSinceSpawn, 0.5f); }
                catch { }
                float dropCap = age < 4f ? 120f : (dist > 2200f ? 220f : 450f);
                float minAgl = dist > 700f ? 40f : 12f;
                SafeSetAimpoint(missile, aim, tvel, minAgl, dropCap);
            }
            catch
            {
                if (Kh85MclosGate.ManualActive)
                    return;
                try
                {
                    Vector3 mpos = missile.transform.position;
                    Vector3 fwd = missile.rb != null && missile.rb.velocity.sqrMagnitude > 1f
                        ? missile.rb.velocity.normalized
                        : missile.transform.forward;
                    float spd = missile.rb != null ? missile.rb.velocity.magnitude : 80f;
                    SafeSetAimpoint(missile, mpos + fwd * Mathf.Clamp(spd * 2f, 400f, 1200f),
                        Vector3.zero, 40f, 120f);
                }
                catch { }
            }
        }

        private static readonly Dictionary<string, FieldInfo> SeekerFieldCache =
            new Dictionary<string, FieldInfo>(64);
        private static readonly FieldInfo MissileSeekerField =
            AccessTools.Field(typeof(Missile), "seeker");

        /// <summary>
        /// Resolve seeker instance field once per (runtimeType,name). Null is cached so missing
        /// Optical fields never call AccessTools again (HarmonyX Field-miss logging was a hitch).
        /// </summary>
        private static FieldInfo ResolveSeekerField(Type seekerType, string name)
        {
            if (seekerType == null || string.IsNullOrEmpty(name))
                return null;
            string key = seekerType.FullName + ":" + name;
            FieldInfo f;
            if (SeekerFieldCache.TryGetValue(key, out f))
                return f;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public
                | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            Type st = seekerType;
            while (st != null && st != typeof(object)
                && st != typeof(MonoBehaviour)
                && st != typeof(Behaviour)
                && st != typeof(Component)
                && st != typeof(UnityEngine.Object))
            {
                f = st.GetField(name, flags);
                if (f != null)
                    break;
                st = st.BaseType;
            }
            SeekerFieldCache[key] = f;
            return f;
        }

        private static void SetSeekerBool(object seeker, string name, bool value)
        {
            if (seeker == null)
                return;
            FieldInfo f = ResolveSeekerField(seeker.GetType(), name);
            if (f != null && f.FieldType == typeof(bool))
                f.SetValue(seeker, value);
        }

        private static void SetSeekerField(object seeker, string name, object value)
        {
            if (seeker == null || value == null)
                return;
            FieldInfo f = ResolveSeekerField(seeker.GetType(), name);
            if (f == null)
                return;
            try
            {
                if (f.FieldType.IsInstanceOfType(value) || f.FieldType.IsAssignableFrom(value.GetType()))
                    f.SetValue(seeker, value);
            }
            catch { }
        }

        /// <summary>Scale motor thrust / fuelMass on the spawned instance only (shared prefab stays vanilla).</summary>
        private static void ApplyMotorMultipliers(Missile missile)
        {
            if (missile == null || MotorsField == null)
                return;
            int id = missile.GetInstanceID();
            if (MotorScaledIds.Contains(id))
                return;

            float thrustMul = Plugin.ThrustMultiplier != null ? Plugin.ThrustMultiplier.Value : 1f;
            float fuelMul = Plugin.FuelMultiplier != null ? Plugin.FuelMultiplier.Value : 1f;
            thrustMul *= Kh85SHyper.ExtraThrustMul(missile);
            fuelMul *= Kh85SHyper.ExtraFuelMul(missile);
            thrustMul *= Kh85CFlight.ExtraThrustMul(missile);
            fuelMul *= Kh85CFlight.ExtraFuelMul(missile);
            if (thrustMul <= 0f)
                thrustMul = 1f;
            if (fuelMul <= 0f)
                fuelMul = 1f;

            try
            {
                Array motors = MotorsField.GetValue(missile) as Array;
                if (motors == null || motors.Length == 0)
                    return; // retry later — do not mark scaled

                for (int i = 0; i < motors.Length; i++)
                {
                    object motor = motors.GetValue(i);
                    if (motor == null)
                        continue;
                    Type mt = motor.GetType();
                    FieldInfo fThrust = mt.GetField("thrust", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    FieldInfo fFuel = mt.GetField("fuelMass", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    FieldInfo fTop = mt.GetField("topSpeed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (fThrust != null)
                    {
                        float v = Convert.ToSingle(fThrust.GetValue(motor));
                        fThrust.SetValue(motor, v * thrustMul);
                    }
                    if (fFuel != null)
                    {
                        float v = Convert.ToSingle(fFuel.GetValue(motor));
                        fFuel.SetValue(motor, v * fuelMul);
                    }
                    // AGM-68 donor topSpeed is c (3e8). With C drag cut that becomes
                    // multi-million m/s and aimPoint leaves the map.
                    if (fTop != null)
                    {
                        float top = Convert.ToSingle(fTop.GetValue(motor));
                        float cap = 800f;
                        if (Kh85CFlight.IsCVariant(missile))
                            cap = Kh85CFlight.CruiseSpeedMps();
                        else if (Kh85SHyper.IsSVariant(missile))
                            cap = 2200f;
                        if (top > cap)
                            fTop.SetValue(motor, cap);
                    }
                }

                MotorScaledIds.Add(id);
                if (Plugin.Log != null)
                    Plugin.Log.LogInfo("Kh-85MT motors: thrust x" + thrustMul + " fuel x" + fuelMul
                        + " stages=" + motors.Length);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Kh-85MT motor scale failed: " + ex.Message);
            }
        }

        internal static WeaponInfo GetMissileInfo(Missile missile)
        {
            if (missile == null || MissileInfoField == null)
                return null;
            try { return MissileInfoField.GetValue(missile) as WeaponInfo; }
            catch { return null; }
        }

        private static void SetMissileInfo(Missile missile, WeaponInfo info)
        {
            if (missile == null || info == null || MissileInfoField == null)
                return;
            try { MissileInfoField.SetValue(missile, info); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(Encyclopedia))]
    internal static class Patch_Encyclopedia_AfterLoad_Kh85
    {
        [HarmonyPrefix]
        [HarmonyPatch("AfterLoad", new Type[] { typeof(Encyclopedia) })]
        private static void PrefixStatic(Encyclopedia instance)
        {
            Kh85Weapon.DedupeEncyclopediaLists(instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch("AfterLoad", new Type[] { typeof(Encyclopedia) })]
        private static void PostfixStatic(Encyclopedia instance)
        {
            Kh85Weapon.OnEncyclopediaAfterLoad(instance);
        }

        [HarmonyPrefix]
        [HarmonyPatch("AfterLoad", new Type[] { })]
        private static void PrefixInstance(Encyclopedia __instance)
        {
            Kh85Weapon.DedupeEncyclopediaLists(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPatch("AfterLoad", new Type[] { })]
        private static void PostfixInstance(Encyclopedia __instance)
        {
            Kh85Weapon.OnEncyclopediaAfterLoad(__instance);
        }
    }

    [HarmonyPatch(typeof(WeaponMount), "Initialize")]
    internal static class Patch_WeaponMount_Initialize
    {
        [HarmonyPostfix]
        private static void Postfix(WeaponMount __instance)
        {
            if (__instance != null && Kh85Weapon.IsKh85Key(__instance.jsonKey))
                Kh85Weapon.RestoreMountIdentity(__instance);
        }
    }

    [HarmonyPatch(typeof(Weapon), "AttachToHardpoint")]
    internal static class Patch_Weapon_Attach
    {
        [HarmonyPostfix]
        private static void Postfix(Weapon __instance, WeaponMount weaponMount)
        {
            Kh85Weapon.SyncFromMount(__instance, weaponMount);
        }
    }

    [HarmonyPatch(typeof(WeaponStation), "RegisterWeapon")]
    internal static class Patch_WeaponStation_Register
    {
        [HarmonyPostfix]
        private static void Postfix(WeaponStation __instance, Weapon weapon, WeaponMount weaponMount)
        {
            if (weapon is Gun)
                return;
            Kh85Weapon.SyncFromMount(weapon, weaponMount);
            if (__instance != null && weapon != null && weapon.info != null
                && (Kh85Weapon.IsKh85Mount(weaponMount) || Kh85Weapon.IsKh85Info(weapon.info)))
                __instance.WeaponInfo = weapon.info;
        }
    }

    [HarmonyPatch(typeof(MountedMissile), "Fire")]
    internal static class Patch_MountedMissile_Fire
    {
        [HarmonyPrefix]
        private static void Prefix(MountedMissile __instance, Unit owner, Unit target)
        {
            Kh85Weapon.NoteFire(__instance, owner, target);
        }
    }

    /// <summary>
    /// Sticky player lock for the whole flight. A/B get lead aim every tick;
    /// C only refreshes seeker lock (sea-skim aim is owned by Kh85CFlight).
    /// </summary>
    public class Kh85LockHold : MonoBehaviour
    {
        private Missile _missile;
        private Unit _target;

        internal static void Attach(Missile missile, Unit target)
        {
            if (missile == null || target == null)
                return;
            Kh85LockHold hold = missile.GetComponent<Kh85LockHold>();
            if (hold == null)
            {
                try { hold = missile.gameObject.AddComponent<Kh85LockHold>(); }
                catch (Exception ex)
                {
                    if (Plugin.Log != null)
                        Plugin.Log.LogWarning("Kh85LockHold add: " + ex.Message);
                    return;
                }
            }
            if (hold == null)
                return;
            Unit owner = null;
            try { owner = missile.owner; }
            catch { }
            Unit safe = Kh85Weapon.SanitizeLockTarget(owner, target);
            if (safe == null)
                return;
            hold._missile = missile;
            hold._target = safe;
            hold.enabled = true;
            Kh85Weapon.ApplyLaunchTargetPublic(missile, safe);
        }

        private void FixedUpdate()
        {
            if (_missile == null)
            {
                enabled = false;
                return;
            }
            try
            {
                if (_missile.disabled)
                {
                    enabled = false;
                    return;
                }
            }
            catch { }

            Unit t = _target;
            try
            {
                if (t == null)
                {
                    enabled = false;
                    return;
                }
            }
            catch
            {
                enabled = false;
                return;
            }

            Unit owner = null;
            try { owner = _missile.owner; }
            catch { }
            t = Kh85Weapon.SanitizeLockTarget(owner, t);
            if (t == null || object.ReferenceEquals(t, _missile))
            {
                enabled = false;
                return;
            }
            _target = t;
            // P1: FixedUpdate only re-sticks lock — lead / SafeSetAimpoint runs in Steering Prefix.
            Kh85Weapon.ApplyTargetLock(_missile, t);
            Kh85Weapon.DisableFreeHunt(_missile);
        }

        /// <summary>Re-aim sticky lock immediately before Steering (beats Seek forward-coast).</summary>
        internal void ReapplyBeforeSteer()
        {
            if (_missile == null || _target == null)
                return;
            if (Kh85MclosGate.ManualActive)
                return;
            Unit owner = null;
            try { owner = _missile.owner; }
            catch { }
            Unit t = Kh85Weapon.SanitizeLockTarget(owner, _target);
            if (t == null)
                return;
            _target = t;
            Kh85Weapon.ApplyLaunchTargetPublic(_missile, t);
            Kh85Weapon.DisableFreeHunt(_missile);
        }
    }

    /// <summary>
    /// A/B/D sticky lead before Steering; C/E/S ApplyLaunchTarget skips lead aim.
    /// Priority.VeryLow so CI22XE MCLOS (Priority.Last) wins when ManualActive.
    /// </summary>
    [HarmonyPatch(typeof(Missile), "Steering")]
    internal static class Patch_Kh85_StickyAim_BeforeSteer
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.VeryLow)]
        private static void Prefix(Missile __instance)
        {
            if (__instance == null)
                return;
            if (Kh85Weapon.IsKnownNonKh85Missile(__instance))
                return;
            // Hot path: only tagged Kh85 missiles; MCLOS owns aim while ManualActive.
            if (__instance.GetComponent<Kh85VariantTag>() == null)
                return;
            if (Kh85MclosGate.ManualActive)
                return;
            Kh85LockHold hold = __instance.GetComponent<Kh85LockHold>();
            if (hold != null)
            {
                hold.ReapplyBeforeSteer();
                return;
            }
            // LOAL via WeXon MM never attached LockHold — still lead A/B/D from live target.
            if (Kh85CFlight.IsCVariant(__instance)
                || Kh85EDecoy.IsEVariant(__instance)
                || Kh85SHyper.IsSVariant(__instance))
                return;
            Unit live = Kh85Weapon.ResolveMissileDesignatedTarget(__instance);
            if (live != null)
                Kh85Weapon.ApplyLaunchTargetPublic(__instance, live);
        }
    }

    [HarmonyPatch(typeof(WeaponManager), "RegisterWeapon")]
    internal static class Patch_WeaponManager_RegisterWeapon
    {
        [HarmonyPrefix]
        private static void Prefix(Weapon weapon, WeaponMount weaponMount)
        {
            if (weapon == null || weaponMount == null || !Kh85Weapon.IsKh85Mount(weaponMount))
                return;
            if (weapon is Gun)
                return;
            try
            {
                if (!weapon.gameObject.activeSelf)
                    weapon.gameObject.SetActive(true);
            }
            catch { }
            Kh85Weapon.RestoreMountIdentity(weaponMount);
            Kh85Weapon.SyncFromMount(weapon, weaponMount);
        }
    }

    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    internal static class Patch_WeaponManager_Awake
    {
        [HarmonyPostfix]
        private static void Postfix(WeaponManager __instance)
        {
            if (Plugin.Enabled == null || !Plugin.Enabled.Value || __instance == null)
                return;
            try
            {
                Kh85Weapon.InjectIntoWeaponManager(__instance);
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("Kh-85MT WeaponManager.Awake inject: "
                        + ex.GetType().Name + ": " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(Hardpoint), "SpawnMount")]
    internal static class Patch_Hardpoint_SpawnMount
    {
        [HarmonyPrefix]
        private static bool Prefix(WeaponMount weaponMount, ref GameObject __result)
        {
            if (weaponMount == null || !Kh85Weapon.IsKh85Mount(weaponMount))
                return true;
            Kh85Weapon.RepairBrokenPrefabs();
            if (Kh85Weapon.EnsureMountPrefab(weaponMount) && weaponMount.prefab != null)
                return true;
            // Never NRE vanilla SpawnMount (that empties the whole loadout). Skip this station only.
            __result = null;
            if (Plugin.Log != null)
                Plugin.Log.LogWarning("Kh-85MT: skip SpawnMount, rack prefab missing for "
                    + (weaponMount.jsonKey != null ? weaponMount.jsonKey : "?"));
            return false;
        }

        [HarmonyPostfix]
        private static void Postfix(Hardpoint __instance, Aircraft aircraft, WeaponMount weaponMount, GameObject __result)
        {
            if (__result == null || weaponMount == null || !Kh85Weapon.IsKh85Mount(weaponMount))
                return;
            Kh85Weapon.RestoreMountIdentity(weaponMount);
            if (Plugin.CustomVisual == null || Plugin.CustomVisual.Value)
                Kh85Visual.ApplyToHangarRack(__result);
            try
            {
                Weapon[] weapons = __result.GetComponentsInChildren<Weapon>(true);
                for (int i = 0; i < weapons.Length; i++)
                {
                    Weapon w = weapons[i];
                    if (w == null || w is Gun)
                        continue;
                    if (!w.gameObject.activeSelf)
                        w.gameObject.SetActive(true);
                    Kh85Weapon.SyncFromMount(w, weaponMount);
                }
            }
            catch (Exception ex)
            {
                if (Plugin.DebugLog != null && Plugin.DebugLog.Value)
                    Plugin.Log.LogWarning("Kh-85MT SpawnMount: " + ex.Message);
            }
        }
    }

    [HarmonyPatch(typeof(Spawner))]
    internal static class Patch_Spawner_SpawnMissile
    {
        /// <summary>If a legacy Instantiated template sneaks in, replace with shared AGM prefab.</summary>
        [HarmonyPrefix]
        [HarmonyPatch("SpawnMissile", new Type[] { typeof(GameObject), typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(Unit), typeof(Unit) })]
        private static void PrefixGo(ref GameObject missile)
        {
            GameObject swap = Kh85Weapon.ResolveSpawnPrefabSwap(missile);
            if (swap != null)
                missile = swap;
        }

        [HarmonyPrefix]
        [HarmonyPatch("SpawnMissile", new Type[] { typeof(MissileDefinition), typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(Unit), typeof(Unit) })]
        private static void PrefixDef(ref MissileDefinition missile, Unit owner)
        {
            // Ground / ship launchers never fire TGM-85; do not steal SAM / RAM-45 shots.
            if (owner != null && !(owner is Aircraft))
                return;
            MissileDefinition swap = Kh85Weapon.ResolveSpawnDefinitionSwap(missile);
            if (swap != null)
                missile = swap;
        }

        [HarmonyPostfix]
        [HarmonyPatch("SpawnMissile", new Type[] { typeof(MissileDefinition), typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(Unit), typeof(Unit) })]
        private static void PostfixDef(Missile __result)
        {
            Kh85Weapon.OnSpawned(__result);
        }

        [HarmonyPostfix]
        [HarmonyPatch("SpawnMissile", new Type[] { typeof(GameObject), typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(Unit), typeof(Unit) })]
        private static void PostfixGo(Missile __result)
        {
            Kh85Weapon.OnSpawned(__result);
        }
    }

    /// <summary>
    /// Late bootstrap when Spawner postfix missed a rail / network spawn.
    /// Early-out for non-Kh85 — every missile hits Steering every FixedUpdate.
    /// </summary>
    [HarmonyPatch(typeof(Missile), "Steering")]
    internal static class Patch_Missile_Steering_Bootstrap
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.VeryLow)]
        private static void Prefix(Missile __instance)
        {
            if (__instance == null)
                return;
            if (Kh85Weapon.IsKnownNonKh85Missile(__instance))
                return;
            if (__instance.GetComponent<Kh85VariantTag>() != null)
            {
                if (!Kh85Weapon.MotorsScaled(__instance))
                    Kh85Weapon.EnsureMotors(__instance);
                return;
            }
            // Fast reject: known non-Kh definition key → never GetMissileInfo (AGM hitch root).
            try
            {
                if (__instance.definition != null
                    && !string.IsNullOrEmpty(__instance.definition.jsonKey))
                {
                    if (!Kh85Weapon.IsKh85Key(__instance.definition.jsonKey))
                    {
                        Kh85Weapon.NoteNonKh85Missile(__instance);
                        return;
                    }
                    Kh85Weapon.OnSpawned(__instance);
                    return;
                }
            }
            catch { }
            if (!Kh85Weapon.IsKh85Info(Kh85Weapon.GetMissileInfo(__instance)))
            {
                Kh85Weapon.NoteNonKh85Missile(__instance);
                return;
            }
            Kh85Weapon.OnSpawned(__instance);
        }
    }

    /// <summary>Hangar / aircraft-select weapon schematic panel.</summary>
    [HarmonyPatch(typeof(AircraftSelectionMenu), "DisplayInfo")]
    internal static class Patch_AircraftSelectionMenu_DisplayInfo
    {
        private static readonly FieldInfo WeaponImageField =
            AccessTools.Field(typeof(AircraftSelectionMenu), "weaponImage");

        [HarmonyPostfix]
        private static void Postfix(AircraftSelectionMenu __instance, WeaponInfo weaponInfo)
        {
            if (weaponInfo == null || !Kh85Weapon.IsKh85Info(weaponInfo))
                return;
            Kh85Weapon.ApplyIconToInfo(weaponInfo);
            Sprite icon = Kh85Icon.GetWeaponIcon();
            if (icon == null || WeaponImageField == null || __instance == null)
                return;
            try
            {
                Image img = WeaponImageField.GetValue(__instance) as Image;
                if (img != null)
                    img.sprite = icon;
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(LoadoutSelector), "ShowMountInfo")]
    internal static class Patch_LoadoutSelector_ShowMountInfo
    {
        [HarmonyPostfix]
        private static void Postfix(WeaponMount mount)
        {
            if (mount == null || !Kh85Weapon.IsKh85Mount(mount))
                return;
            Kh85Weapon.RestoreMountIdentity(mount);
            if (mount.info != null)
                Kh85Weapon.ApplyIconToInfo(mount.info);
        }
    }

    /// <summary>
    /// Always surface TGM-85 options in the hangar list (works with/without Oritasy unrestricted).
    /// </summary>
    [HarmonyPatch(typeof(WeaponChecker), "GetAvailableWeaponsNonAlloc")]
    internal static class Patch_GetAvailableWeapons_Kh85
    {
        private static readonly HashSet<WeaponMount> HaveScratch = new HashSet<WeaponMount>();

        [HarmonyPostfix]
        private static void Postfix(Player player, HardpointSet hardpointSet, List<WeaponMount> outAvailable)
        {
            if (outAvailable == null || Plugin.Enabled == null || !Plugin.Enabled.Value)
                return;
            try
            {
                if (player != null && !GameManager.IsLocalPlayer(player))
                    return;
            }
            catch { }

            Kh85Weapon.RepairBrokenPrefabs();
            HaveScratch.Clear();
            for (int i = outAvailable.Count - 1; i >= 0; i--)
            {
                WeaponMount existing = outAvailable[i];
                if (existing == null)
                    continue;
                if (Kh85Weapon.IsKh85Mount(existing) && existing.prefab == null)
                {
                    if (!Kh85Weapon.EnsureMountPrefab(existing))
                    {
                        outAvailable.RemoveAt(i);
                        continue;
                    }
                }
                HaveScratch.Add(existing);
            }
            Kh85Advanced.AppendAllToList(outAvailable, HaveScratch);
        }
    }

    [HarmonyPatch(typeof(WeaponSelector), "Initialize")]
    [HarmonyPatch(new Type[] { typeof(Aircraft), typeof(HardpointSet), typeof(FactionHQ), typeof(Airbase) })]
    internal static class Patch_WeaponSelector_Initialize_Kh85
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            if (Plugin.Enabled == null || !Plugin.Enabled.Value)
                return;
            Kh85Weapon.RepairBrokenPrefabs();
        }
    }

    /// <summary>
    /// Aircraft pylonOptions bind the vanilla AGM-68 WeaponMount asset. TGM-85 is a clone,
    /// so MatchesMount fails and ShowPylon hides the adapter when the missile is selected.
    /// </summary>
    [HarmonyPatch]
    internal static class Patch_HardpointPylon_MatchesMount
    {
        private static readonly FieldInfo BoundMountField;

        static Patch_HardpointPylon_MatchesMount()
        {
            Type nested = AccessTools.Inner(typeof(Hardpoint), "HardpointPylon");
            BoundMountField = nested != null ? AccessTools.Field(nested, "mount") : null;
        }

        [HarmonyTargetMethod]
        private static MethodBase TargetMethod()
        {
            Type nested = AccessTools.Inner(typeof(Hardpoint), "HardpointPylon");
            if (nested == null)
                return null;
            return AccessTools.Method(nested, "MatchesMount", new Type[] { typeof(WeaponMount) });
        }

        [HarmonyPostfix]
        private static void Postfix(object __instance, WeaponMount mount, ref bool __result)
        {
            if (__result || mount == null || __instance == null || BoundMountField == null)
                return;
            if (!Kh85Weapon.IsKh85Mount(mount))
                return;
            WeaponMount bound = null;
            try { bound = BoundMountField.GetValue(__instance) as WeaponMount; }
            catch { return; }
            if (bound == null)
                return;
            if (Kh85Weapon.IsAgm68Mount(bound) || Kh85Weapon.IsKh85Mount(bound))
                __result = true;
        }
    }
}
