using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// WeXon weapon pack:
    /// - Guided dual-mode missiles (MultiMode / free-hunt) carry [IAL] (ex-[MM] tag)
    /// - Separate *_IAL WeaponMount clones carry nuclear warheads + [IAL] [10kt]
    /// - Stock nuclear / rockets / ballistic / cruise / shells: no [IAL] dual-mode branding
    /// - IFF gating (no OpticalIffSeeker replace — that path caused dive/self-destruct)
    /// - Encyclopedia pages branded WeXon
    ///
    /// Combined Oritasy.dll: hosted by Oritasy.Plugin (no second [BepInPlugin]).
    /// Dual BepInPlugin in one DLL doubled Harmony + Update and caused multi-second stalls.
    /// </summary>
#if !ORITASY_COMBINED
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    public class Plugin : BaseUnityPlugin
#else
    public class Plugin : MonoBehaviour
#endif
    {
        internal const string PackName = "WeXon";
        internal const string PackDescLine = "[WeXon]";
        internal static ManualLogSource Log;

        static Plugin()
        {
            ConfigGuidMigrate.CopyLegacy(PluginInfo.GUID, PluginInfo.LegacyGUID);
        }

#if ORITASY_COMBINED
        private static BaseUnityPlugin _host;
        private static ConfigFile _hostConfig;
        private ConfigFile Config
        {
            get { return _hostConfig; }
        }

        /// <summary>Oritasy combined host attaches WeXon lifecycle on the same GameObject.</summary>
        internal static void StartHosted(BaseUnityPlugin host, ManualLogSource log, ConfigFile config)
        {
            if (host == null || config == null)
                return;
            _host = host;
            _hostConfig = config;
            Log = log ?? BepInEx.Logging.Logger.CreateLogSource(PluginInfo.Name);
            try
            {
                RunInit();
            }
            catch (Exception ex)
            {
                if (Log != null)
                    Log.LogError("WeXon init: " + DescribeEx(ex));
            }
            // Do not AddComponent — packed Assembly.Load has no Location and Unity
            // rejects extra MonoBehaviours. Tick/GUI run from Oritasy.Plugin.
        }

        internal static void DrawHostedGui()
        {
            DrawCareerGui();
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
        private static bool _initDone;
#if !ORITASY_COMBINED
        private static ConfigFile _bindConfig;

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

        internal static ConfigEntry<bool> EnableNukeVariants;
        internal static ConfigEntry<bool> EnableMultiMode;
        /// <summary>When false (default), optical/laser missiles skip MultiModeBrain for CPU.</summary>
        internal static ConfigEntry<bool> EnableOpticalMultiMode;
        internal static ConfigEntry<bool> EnableUnrestricted;
        internal static ConfigEntry<bool> EnableIff;
        internal static ConfigEntry<bool> VanillaMissileBackup;
        internal static ConfigEntry<bool> EnableAgmT;
        internal static ConfigEntry<int> AgmTSubCount;
        internal static ConfigEntry<float> AgmTMinFlightTime;
        internal static ConfigEntry<float> AgmTMaxFlightTime;
        internal static ConfigEntry<float> AgmTDispenseDistance;
        internal static ConfigEntry<float> AgmTSearchRadius;
        internal static ConfigEntry<float> AgmTBusHuntRadius;
        internal static ConfigEntry<float> AgmTEjectSpeed;
        internal static ConfigEntry<float> AgmTNukeYield;
        /// <summary>Multiplier on NukeYield for shockwave / blast radius (Warhead uses Pow(yield,1/3)).</summary>
        internal static ConfigEntry<float> AgmTNukeBlastScale;
        /// <summary>ACNM-118: ignore hunt targets with UnitDefinition.value below this (game units = millions).</summary>
        internal static ConfigEntry<float> AgmTNukeMinTargetValueM;
        /// <summary>ACNM-118: nuclear detonation only within this range of intended target (m).</summary>
        internal static ConfigEntry<float> AgmTNukeProximityM;
        /// <summary>ACNM-118: min flight time before nuclear arm (s).</summary>
        internal static ConfigEntry<float> AgmTNukeArmMinFlightTime;
        /// <summary>ACNM-118: min distance from spawn before nuclear arm (m).</summary>
        internal static ConfigEntry<float> AgmTNukeArmMinDistance;
        /// <summary>Scale Missile.dragCurve + supersonicDrag on ACM/ACNM bus and GS25 (lower = less drag).</summary>
        internal static ConfigEntry<float> AgmTDragScale;
        internal static ConfigEntry<float> AgmTGs25Thrust;
        internal static ConfigEntry<float> AgmTGs25BurnTime;
        internal static ConfigEntry<float> AgmTGs25MaxRange;
        internal static ConfigEntry<bool> AgmTSubNukeImmune;
        internal static ConfigEntry<bool> AgmTBusCustomVisual;
        internal static ConfigEntry<float> AgmTBusVisualScale;
        internal static ConfigEntry<string> AgmTBusVisualEuler;
        internal static ConfigEntry<string> AgmTBusVisualOffset;

        internal static ConfigEntry<float> BlastYield;
        internal static ConfigEntry<float> PierceDamage;
        internal static ConfigEntry<bool> SetNuclearFlag;
        internal static ConfigEntry<bool> SetStrategicFlag;
        internal static ConfigEntry<bool> SwapExplosionFx;
        internal static ConfigEntry<string> ExplosionFxName;
        internal static ConfigEntry<bool> NukeLabelNames;
        internal static ConfigEntry<string> NukeNameTag;

        internal static ConfigEntry<float> TerminalRange;
        internal static ConfigEntry<string> SecondaryPrefer;
        internal static ConfigEntry<bool> EnableSecondarySeeker;
        internal static ConfigEntry<float> TerminalGLimit;
        internal static ConfigEntry<float> TerminalBoostAmount;
        internal static ConfigEntry<bool> AllowFreeAttack;
        internal static ConfigEntry<bool> EnergyAwareGuide;
        internal static ConfigEntry<float> SoftRetargetSeconds;
        internal static ConfigEntry<float> MinGuideSpeedMps;
        internal static ConfigEntry<float> MadModeSearchRadius;
        internal static ConfigEntry<float> MadModeSearchAngle;
        internal static ConfigEntry<float> MadModeSearchInterval;
        internal static ConfigEntry<float> TbmHuntRadius;
        internal static ConfigEntry<bool> MmLabelNames;
        internal static ConfigEntry<string> MmNameTag;

        internal static ConfigEntry<bool> EnableLowSpeedSelfDestruct;
        /// <summary>Config value is km/h (key LowSpeedSelfDestructKmh). Use LowSpeedSelfDestructThresholdMps().</summary>
        internal static ConfigEntry<float> LowSpeedSelfDestructMps;
        internal static ConfigEntry<float> LowSpeedSelfDestructMinAge;
        internal static ConfigEntry<float> LowSpeedSelfDestructHold;
        internal static ConfigEntry<float> LowSpeedSelfDestructBallisticMinAge;
        internal static ConfigEntry<float> ShipMissileMinLaunchMps;

        /// <summary>Yield-scaled proximity fuze (cube-root of blastYield). Does not patch guidance.</summary>
        internal static ConfigEntry<bool> EnableYieldProximityFuze;
        internal static ConfigEntry<float> ProximityRefYield;
        internal static ConfigEntry<float> ProximityRefRangeM;
        internal static ConfigEntry<float> ProximityScale;
        internal static ConfigEntry<float> ProximityMinM;
        internal static ConfigEntry<float> ProximityMaxM;
        internal static ConfigEntry<float> ProximityMinAge;
        internal static ConfigEntry<bool> ProximityRequireClosing;

        internal static ConfigEntry<bool> EnableMissileOverpen;
        internal static ConfigEntry<float> OverpenKineticScale;
        internal static ConfigEntry<float> OverpenSphereRadius;
        internal static ConfigEntry<int> OverpenMaxHits;
        internal static ConfigEntry<float> OverpenSpeedKeep;
        internal static ConfigEntry<float> OverpenMinAge;

        /// <summary>Fire→async RailLaunch→SpawnMissile: count of nuke warheads waiting to apply.</summary>
        internal static int PendingNukeSpawns;
        /// <summary>Weapon / unit that last fired an IAL nuke �?pending must match owner to avoid racing conventional spawns.</summary>
        private static Weapon PendingNukeWeapon;
        private static Unit PendingNukeOwner;
        private static float PendingNukeTime;

        internal static ConfigEntry<bool> IalLabelNames;
        internal static ConfigEntry<string> IalNameTag;
        internal static ConfigEntry<bool> EnableEncyclopediaPages;
        internal static ConfigEntry<bool> BlockIalOnShips;
        internal static ConfigEntry<float> AiNukeChance;
        internal static ConfigEntry<bool> BlockFriendlySetTarget;
        /// <summary>When true, IAL nuclear blastYield / blastDamage use half of BlastYield (shockwave range).</summary>
        internal static ConfigEntry<bool> IalHalfBlastRange;

        internal static ConfigEntry<bool> AffectAllMissiles;
        internal static ConfigEntry<string> MissileNameWhitelist;
        internal static ConfigEntry<string> MountBlacklist;
        internal static ConfigEntry<bool> DebugLog;

        private static readonly HashSet<int> TouchedInfos = new HashSet<int>();
        private static readonly HashSet<int> TouchedMissiles = new HashSet<int>();
        private static readonly HashSet<int> TouchedMounts = new HashSet<int>();
        private static readonly HashSet<int> ExpandedManagers = new HashSet<int>();
        private static readonly HashSet<int> NukeInfoIds = new HashSet<int>();
        private static readonly HashSet<string> CreatedNukeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool EncyclopediaInjected;
        internal static bool NukeMountsInjected;

        private static readonly FieldInfo BlastYieldField = AccessTools.Field(typeof(Missile), "blastYield");
        private static readonly FieldInfo PierceField = AccessTools.Field(typeof(Missile), "pierceDamage");
        private static readonly FieldInfo InfoField = AccessTools.Field(typeof(Missile), "info");
        private static readonly FieldInfo WarheadField = AccessTools.Field(typeof(Missile), "warhead");
        private static readonly FieldInfo DragCurveField = AccessTools.Field(typeof(Missile), "dragCurve");
        private static readonly FieldInfo SupersonicDragField = AccessTools.Field(typeof(Missile), "supersonicDrag");
        private static readonly FieldInfo MountDisabledField = AccessTools.Field(typeof(WeaponMount), "disabled");
        private static readonly FieldInfo WeaponMountField = AccessTools.Field(typeof(Weapon), "mount");
        private static readonly HashSet<int> AgmTDragReducedIds = new HashSet<int>();
        internal static readonly FieldInfo WeaponManagerAircraftField = AccessTools.Field(typeof(WeaponManager), "aircraft");
        internal static readonly FieldInfo SeekerField = AccessTools.Field(typeof(Missile), "seeker");
        internal static readonly FieldInfo SeekerMissileField = AccessTools.Field(typeof(MissileSeeker), "missile");
        private static readonly FieldInfo SeekerTargetField = AccessTools.Field(typeof(MissileSeeker), "targetUnit");
        private static readonly FieldInfo MissileTargetField = AccessTools.Field(typeof(Missile), "target");
        private static readonly FieldInfo MotorsField = AccessTools.Field(typeof(Missile), "motors");

        /// <summary>Per-seeker-Type FieldInfo cache for IFF optical clears (no AccessTools each Seek).</summary>
        private static readonly Dictionary<Type, FieldInfo> SeekerTargetTransformFields = new Dictionary<Type, FieldInfo>(16);
        private static readonly Dictionary<Type, FieldInfo> SeekerHasVisualFields = new Dictionary<Type, FieldInfo>(16);

        /// <summary>Kh85 identity resolved once per missile instance (hot paths must not GetComponent-string).</summary>
        internal enum Kh85Kind : byte
        {
            Unresolved = 0,
            NotKh85 = 1,
            MultiMode = 2,
            TerrainAim = 3
        }

        private static readonly Dictionary<int, byte> Kh85KindByMissileId = new Dictionary<int, byte>(128);
        private static float _nextKh85KindPrune;

        private static Type WarheadType;
        private static FieldInfo WhAir;
        private static FieldInfo WhArmor;
        private static FieldInfo WhTerrain;
        private static FieldInfo WhWater;
        private static FieldInfo WhUnder;
        private static FieldInfo WhArmed;

        private static GameObject CachedNukeFx;
        internal static readonly List<WeaponMount> CachedMounts = new List<WeaponMount>();
        internal static readonly HashSet<WeaponMount> CachedMountSet = new HashSet<WeaponMount>();
        internal static readonly List<WeaponMount> NukeMountClones = new List<WeaponMount>();
        private static float NextMountRefresh;

        private static bool _scannedAssets;
        private static bool _encyclopediaReady;
        private static float _nextEncyclopediaRetry;
        private static float _nextAgmTRetry;
        private static float _nextAam2CvRetry;
        private static float _nextNukeFxRetry;
        private static float _nextNukeMountRetry;
        private static bool NuclearIalPurgeDone;
        internal static bool EncyclopediaDataReady;
        internal static Encyclopedia CachedEncyclopedia;
        private static HashSet<string> VanillaNuclearMountKeys;

        /// <summary>Depth &gt; 0 while player loadout UI / GetAvailableWeapons(player) is running.</summary>
        private static int PlayerUnrestrictedDepth;

        /// <summary>Set while local human GetAvailableWeapons is in flight (PreferNukesFilter may nest).</summary>
        public static bool LocalHumanWeaponsQuery;
        /// <summary>Brief latch so PreferNukesFilter called right after GetAvailableWeapons still sees human context.</summary>
        public static float LocalHumanWeaponsUntil;

        internal static void EnterPlayerUnrestricted()
        {
            PlayerUnrestrictedDepth++;
        }

        internal static void ExitPlayerUnrestricted()
        {
            if (PlayerUnrestrictedDepth > 0)
                PlayerUnrestrictedDepth--;
        }

        /// <summary>
        /// Career Profile syncs WeXon EnableUnrestricted + Oritasy UnrestrictedWeapons.
        /// Combined pack still ORs both in case an old cfg only flipped one.
        /// </summary>
        internal static bool UnrestrictedFeatureOn()
        {
            if (EnableUnrestricted != null && EnableUnrestricted.Value)
                return true;
#if ORITASY_COMBINED
            try
            {
                if (Oritasy.Plugin.UnrestrictedWeapons != null
                    && Oritasy.Plugin.UnrestrictedWeapons.Value)
                    return true;
            }
            catch { }
#endif
            return false;
        }

        /// <summary>Unrestricted hardpoints apply to the human player only — never AI auto-loadout.</summary>
        internal static bool AllowPlayerUnrestricted()
        {
            return LoadoutMountGateService.AllowPlayerUnrestricted(
                UnrestrictedFeatureOn(), PlayerUnrestrictedDepth);
        }

        /// <summary>Human loadout / local GetAvailableWeapons �?PreferNukesFilter must not strip IAL.</summary>
        internal static bool IsHumanWeaponContext()
        {
            if (PlayerUnrestrictedDepth > 0 || LocalHumanWeaponsQuery)
                return true;
            try
            {
                if (Time.unscaledTime < LocalHumanWeaponsUntil)
                    return true;
            }
            catch { }
            return false;
        }

        /// <summary>True for the local human client Player (AI faction Players are not local).</summary>
        internal static bool IsLocalHumanPlayer(Player player)
        {
            if (player == null)
                return false;
            try
            {
                if (GameManager.IsLocalPlayer(player))
                    return true;
            }
            catch { }
            try
            {
                if (player.IsLocalPlayer)
                    return true;
            }
            catch { }
            // Hangar before NetIdentity ready: non-zero SteamID = human (AI loadout uses null / 0)
            try
            {
                if (player.SteamID != 0UL)
                {
                    Player local = null;
                    if (GameManager.GetLocalPlayer(out local) && local != null
                        && object.ReferenceEquals(local, player))
                        return true;
                    // Solo / no GameManager local yet �?treat Steam player as human
                    if (local == null)
                        return true;
                }
            }
            catch { }
            return false;
        }

        private void Awake()
        {
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
                    Log.LogError("WeXon Awake: " + DescribeEx(ex));
            }
        }

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
                Log.LogError("WeXon missing Config — abort.");
                return;
            }
            CacheWarheadReflection();

            EnableNukeVariants = cfg.Bind("Features", "EnableNukeVariants", true,
                "Create separate nuclear [IAL]/[10kt] WeaponMount clones. Vanilla mounts stay conventional.");
            // Dual-seeker / free-hunt. Optical light MM = LOAL + lock assist (vanilla Seek steers).
            EnableMultiMode = cfg.Bind("Features", "EnableMultiMode", true,
                "Dual-seeker multi-mode + free-hunt on allowed missiles (see EnableOpticalAssist).");
            EnableOpticalMultiMode = cfg.Bind("MultiMode", "EnableOpticalAssist", true,
                "Optical/laser: light MultiModeBrain for LOAL + sticky lock assist (no GuideTo). Off = vanilla Seek only (saves CPU, kills LOAL).");
            // 109 defaulted this false and wiped LOAL — force restore once for existing cfgs.
            ConfigEntry<bool> opticalLoalRestored = cfg.Bind("MultiMode", "OpticalAssistLoalRestored110", false,
                "Internal: one-shot restore EnableOpticalAssist after 109 CPU default.");
            if (!opticalLoalRestored.Value)
            {
                EnableOpticalMultiMode.Value = true;
                opticalLoalRestored.Value = true;
            }
            EnableUnrestricted = cfg.Bind("Features", "EnableUnrestricted", false,
                "Player loadout only: any weapon on any hardpoint. AI keeps vanilla mount options. Off by default; toggle in Career Profile.");
            EnableIff = cfg.Bind("Features", "EnableIFF", true,
                "Reject friendly locks (from OpticalIFF). Does not replace vanilla seeker.");
            VanillaMissileBackup = cfg.Bind("Features", "VanillaMissileBackup", false,
                "Stock missiles use vanilla seeker, fuze, and collision (no MM / overpen / IFF steal). F9, TGM-85, AGM-T unchanged.");
            EnableAgmT = cfg.Bind("Features", "EnableAgmT", true,
                "ACM-119 [IAL] / ACNM-118 [IAL]: Veyrn Aeronautics cluster bus �?6× GS25 (conv. never nuclear).");
            AgmTSubCount = cfg.Bind("AGM-T", "SubmunitionCount", 6, "GS25 count per ACM-119 / ACNM-118 (1-24).");
            // Migrate older default (12) �?6
            if (AgmTSubCount.Value == 12)
                AgmTSubCount.Value = 6;
            AgmTMinFlightTime = cfg.Bind("AGM-T", "MinFlightTime", 5f,
                "Seconds before dispense (hard floor 5s even if lowered).");
            AgmTMaxFlightTime = cfg.Bind("AGM-T", "MaxFlightTime", 25f, "Force dispense after this many seconds.");
            AgmTDispenseDistance = cfg.Bind("AGM-T", "DispenseDistance", 2500f, "Dispense when within this range of lock (m).");
            AgmTSearchRadius = cfg.Bind("AGM-T", "SearchRadius", 35000f, "GS25 target search radius (m).");
            AgmTBusHuntRadius = cfg.Bind("AGM-T", "BusHuntRadius", 35000f,
                "AGM-T bus self-search radius for dense air/ground packs (m).");
            if (Mathf.Abs(AgmTBusHuntRadius.Value - 20000f) < 1f)
                AgmTBusHuntRadius.Value = 35000f;
            AgmTEjectSpeed = cfg.Bind("AGM-T", "EjectSpeed", 45f, "Lateral eject speed added to each GS25 (m/s).");
            AgmTNukeYield = cfg.Bind("AGM-T", "NukeYield", 1500000f,
                "1.5kt variant base blast yield (1500000 = 1.5kt). Effective = NukeYield * NukeBlastScale.");
            AgmTNukeBlastScale = cfg.Bind("AGM-T", "NukeBlastScale", 0.45f,
                "ACNM-118 shockwave/blast scale vs NukeYield (0.45 → ~0.675kt effective, smaller radius).");
            AgmTNukeMinTargetValueM = cfg.Bind("AGM-T", "NukeMinTargetValueM", 25f,
                "ACNM-118 seeker: skip units with UnitDefinition.value below this (millions; 25 = 25M).");
            AgmTNukeProximityM = cfg.Bind("AGM-T", "NukeProximityM", 500f,
                "ACNM-118: nuclear detonation only within this range of intended target (m). Else fizzle SD.");
            AgmTNukeArmMinFlightTime = cfg.Bind("AGM-T", "NukeArmMinFlightTime", 6f,
                "ACNM-118: seconds after spawn before nuclear warhead can arm (blocks launch terrain nuke).");
            AgmTNukeArmMinDistance = cfg.Bind("AGM-T", "NukeArmMinDistance", 2000f,
                "ACNM-118: meters from spawn before nuclear warhead can arm.");
            AgmTDragScale = cfg.Bind("AGM-T", "DragScale", 0.55f,
                "ACM-119 / ACNM-118 / GS25 dragCurve + supersonicDrag multiplier (<1 = less drag).");
            AgmTGs25Thrust = cfg.Bind("AGM-T", "Gs25Thrust", 22000f,
                "Synthetic GS25 motor thrust (N). Vanilla GS25 has no motors.");
            AgmTGs25BurnTime = cfg.Bind("AGM-T", "Gs25BurnTime", 42f,
                "GS25 burn duration (s) for ~35km high-thrust cruise.");
            AgmTGs25MaxRange = cfg.Bind("AGM-T", "Gs25MaxRange", 35000f,
                "GS25 powered range cap (m). Thrust cuts off past this.");
            AgmTSubNukeImmune = cfg.Bind("AGM-T", "SubNukeImmune", true,
                "GS25 submunitions ignore nuclear shockwave push and blast damage (no tumble / no sympathetic detonation).");
            AgmTBusCustomVisual = cfg.Bind("AGM-T", "BusCustomVisual", true,
                "Replace ACM-119 / ACNM-118 bus mesh with AAM-IV (WeXonAssets/). GS25 unchanged.");
            AgmTBusVisualScale = cfg.Bind("AGM-T", "BusVisualScale", 1f,
                "AAM-IV visual uniform scale.");
            AgmTBusVisualEuler = cfg.Bind("AGM-T", "BusVisualEuler", "0,0,0",
                "AAM-IV local euler degrees (x,y,z).");
            AgmTBusVisualOffset = cfg.Bind("AGM-T", "BusVisualOffset", "0,0,0",
                "Extra AAM-IV offset on top of auto hang-align (x,y,z).");
            // Migrate older installs that used sub-5s dispense
            if (AgmTMinFlightTime.Value < 5f)
                AgmTMinFlightTime.Value = 5f;
            if (AgmTSearchRadius.Value < 35000f)
                AgmTSearchRadius.Value = 35000f;

            // Game scale: vanilla ~20kt = 20000000 �?10kt = 10000000
            BlastYield = cfg.Bind("Nuke", "BlastYield", 10000000f, "Nuclear blast yield. 10000000 �?10kt.");
            PierceDamage = cfg.Bind("Nuke", "PierceDamage", -1f, ">=0 overwrites pierceDamage; -1 keep.");
            SetNuclearFlag = cfg.Bind("Nuke", "SetNuclearFlag", true, "WeaponInfo.nuclear = true on nuke variants");
            SetStrategicFlag = cfg.Bind("Nuke", "SetStrategicFlag", false, "WeaponInfo.strategic = true on nuke variants");
            SwapExplosionFx = cfg.Bind("Nuke", "SwapExplosionFx", true, "Swap warhead FX to nuclear explosion prefab");
            ExplosionFxName = cfg.Bind("Nuke", "ExplosionFxName", "explosion_20kt",
                "Nuke FX prefab name (game has explosion_20kt; yield is still 10kt).");
            NukeLabelNames = cfg.Bind("Nuke", "LabelNames", true, "Append nuke tag to nuke-variant names");
            NukeNameTag = cfg.Bind("Nuke", "NameTag", "[10kt]", "ASCII only");

            // Migrate old 20kt defaults from previous installs
            if (Mathf.Abs(BlastYield.Value - 20000000f) < 1f)
                BlastYield.Value = 10000000f;
            if (string.Equals(NukeNameTag.Value, "[20kt]", StringComparison.OrdinalIgnoreCase))
                NukeNameTag.Value = "[10kt]";

            TerminalRange = cfg.Bind("MultiMode", "TerminalRange", 8000f, "Range (m) for terminal g-boost");
            SecondaryPrefer = cfg.Bind("MultiMode", "SecondaryPrefer", "Auto", "Auto | ARH | IR | SARH (only if EnableSecondarySeeker)");
            EnableSecondarySeeker = cfg.Bind("MultiMode", "EnableSecondarySeeker", false,
                "OFF by default: AddComponent ARH/IR secondary caused Seek() NREs and aimless/self-destruct missiles.");
            TerminalGLimit = cfg.Bind("MultiMode", "TerminalGLimit", 40f, "0 = do not change gLimit");
            TerminalBoostAmount = cfg.Bind("MultiMode", "TerminalBoostAmount", 0.35f, "0 = skip ApplyTerminalBoost");
            AllowFreeAttack = cfg.Bind("MultiMode", "AllowFreeAttack", true,
                "No-lock / lost-lock autonomous hunt for nearest hostile.");
            ConfigEntry<bool> loalHuntRestored = cfg.Bind("MultiMode", "LoalHuntRestored193", false,
                "Internal: one-shot restore aircraft LOAL (multi-mode + free-hunt) after 193.");
            if (!loalHuntRestored.Value)
            {
                EnableMultiMode.Value = true;
                EnableOpticalMultiMode.Value = true;
                AllowFreeAttack.Value = true;
                if (VanillaMissileBackup != null)
                    VanillaMissileBackup.Value = false;
                loalHuntRestored.Value = true;
            }
            EnergyAwareGuide = cfg.Bind("MultiMode", "EnergyAwareGuide", true,
                "Limit retarget / re-lock turn rate by airspeed so missiles do not stall. Player sticky locks also clamp.");
            SoftRetargetSeconds = cfg.Bind("MultiMode", "SoftRetargetSeconds", 2.5f,
                "After a lock change or re-lock, keep turns gentler for this many seconds (player sticky capped ~1.25s).");
            MinGuideSpeedMps = cfg.Bind("MultiMode", "MinGuideSpeedMps", 90f,
                "Below this speed (m/s), command forward coast with a mild loft instead of hard turns.");
            MadModeSearchRadius = cfg.Bind("MultiMode", "MadModeSearchRadius", 35000f, "Free-hunt search radius (m)");
            MadModeSearchAngle = cfg.Bind("MultiMode", "MadModeSearchAngle", 360f,
                "Nose FOV half-angle (deg). 360 = nearest enemy in full sphere.");
            if (MadModeSearchRadius.Value < 25000f)
                MadModeSearchRadius.Value = 35000f;
            if (MadModeSearchAngle.Value < 179f)
                MadModeSearchAngle.Value = 360f;
            MadModeSearchInterval = cfg.Bind("MultiMode", "MadModeSearchInterval", 0.5f,
                "Seconds between free-hunt scans (lower = heavier CPU).");
            if (MadModeSearchInterval.Value < 0.25f)
                MadModeSearchInterval.Value = 0.25f;
            TbmHuntRadius = cfg.Bind("MultiMode", "TbmHuntRadius", 60000f,
                "Piledriver TBM free-hunt radius (m). Larger than MadModeSearchRadius; does not use MultiMode steering.");
            MmLabelNames = cfg.Bind("MultiMode", "LabelNames", false,
                "Deprecated: do not use [MM]. Names use [IAL] only; nukes also get [10kt].");
            MmNameTag = cfg.Bind("MultiMode", "NameTag", "[MM]", "Unused �?kept for old configs");
            if (MmLabelNames.Value)
                MmLabelNames.Value = false;

            EnableLowSpeedSelfDestruct = cfg.Bind("Missile", "EnableLowSpeedSelfDestruct", true,
                "Detonate missiles that fall too slow (dud / energy-spent) instead of inert ground impact.");
            // Threshold stored as km/h (user request). Converted to m/s at runtime (/3.6).
            LowSpeedSelfDestructMps = cfg.Bind("Missile", "LowSpeedSelfDestructKmh", 15f,
                "Speed threshold (km/h). Below this for Hold seconds �?self-destruct. Default 15 km/h.");
            // Migrate previous m/s default (55) �?15 km/h
            if (Mathf.Abs(LowSpeedSelfDestructMps.Value - 55f) < 0.05f)
                LowSpeedSelfDestructMps.Value = 15f;
            LowSpeedSelfDestructMinAge = cfg.Bind("Missile", "LowSpeedSelfDestructMinAge", 3.5f,
                "Minimum flight time (s) before low-speed SD can arm (avoids launch tip-off).");
            LowSpeedSelfDestructHold = cfg.Bind("Missile", "LowSpeedSelfDestructHold", 0.85f,
                "Must stay below speed threshold this long (s) before detonating.");
            LowSpeedSelfDestructBallisticMinAge = cfg.Bind("Missile", "LowSpeedSelfDestructBallisticMinAge", 12f,
                "Extra min age for ballistic/TBM so apogee coast does not false-trigger.");
            ShipMissileMinLaunchMps = cfg.Bind("Missile", "ShipMissileMinLaunchMps", 140f,
                "Ship rail launches: boost along nose if slower than this (m/s). Skipped for VLS/SARH (RAM-45).");

            EnableYieldProximityFuze = cfg.Bind("Missile", "EnableYieldProximityFuze", true,
                "Proximity fuze range from warhead blastYield (cube-root). Wires ProxyFuse only; no seeker/steer patches.");
            ProximityRefYield = cfg.Bind("Missile", "ProximityRefYield", 25f,
                "Reference blastYield for the proximity formula (typical conventional HE).");
            ProximityRefRangeM = cfg.Bind("Missile", "ProximityRefRangeM", 30f,
                "Proximity trigger range (m) at ProximityRefYield.");
            ProximityScale = cfg.Bind("Missile", "ProximityScale", 1f,
                "Extra multiplier on yield-scaled proximity range.");
            ProximityMinM = cfg.Bind("Missile", "ProximityMinM", 8f,
                "Minimum proximity trigger range (m).");
            ProximityMaxM = cfg.Bind("Missile", "ProximityMaxM", 250f,
                "Maximum proximity trigger range (m). Nukes clamp here; ACNM nuclear gate stays separate.");
            ProximityMinAge = cfg.Bind("Missile", "ProximityMinAge", 0.4f,
                "Minimum flight time (s) before yield proximity can arm (stack with warhead IsArmed).");
            ProximityRequireClosing = cfg.Bind("Missile", "ProximityRequireClosing", true,
                "Require CPA-pass / closing geometry (vanilla ProxyFuse style) before detonating.");

            EnableMissileOverpen = cfg.Bind("Missile", "EnableOverpen", true,
                "When a missile flies through a unit without exploding, apply kinetic damage and optionally overpenetrate.");
            OverpenKineticScale = cfg.Bind("Missile", "OverpenKineticScale", 8f,
                "Kinetic impact = massKg * speedMps * scale. Direct hits on aircraft should be lethal.");
            OverpenSphereRadius = cfg.Bind("Missile", "OverpenSphereRadius", 3.5f,
                "Sphere-cast radius (m) so thin airframes are not tunneled through.");
            OverpenMaxHits = cfg.Bind("Missile", "OverpenMaxHits", 2,
                "Max overpens before the warhead detonates (armed missiles).");
            OverpenSpeedKeep = cfg.Bind("Missile", "OverpenSpeedKeep", 0.65f,
                "Speed kept after each overpen (0-1).");
            OverpenMinAge = cfg.Bind("Missile", "OverpenMinAge", 0.35f,
                "Ignore hits this many seconds after spawn (avoid striking the shooter).");

            IalLabelNames = cfg.Bind("IAL", "LabelNames", true,
                "Append [IAL] on dual-mode guided missiles (ex-[MM]) and their [10kt] nuke twins.");
            IalNameTag = cfg.Bind("IAL", "NameTag", "[IAL]", "Dual-mode / seeker branding; nukes also get [10kt]");
            EnableEncyclopediaPages = cfg.Bind("IAL", "EncyclopediaPages", true,
                "Add encyclopedia missile + mount entries for IAL / nuke variants");
            BlockIalOnShips = cfg.Bind("IAL", "BlockIalOnShips", true,
                "Naval ships cannot select or fire IAL / [10kt] missile variants.");
            AiNukeChance = cfg.Bind("IAL", "AiNukeChance", 0.15f,
                "Chance (0-1) AI keeps IAL [10kt] (no warhead stock cost). Independent of warheadsAvailable.");
            IalHalfBlastRange = cfg.Bind("IAL", "HalfBlastRange", true,
                "IAL nuclear warheads use half BlastYield (half shockwave range).");
            BlockFriendlySetTarget = cfg.Bind("IFF", "BlockFriendlySetTarget", true,
                "Harmony-block Missile.SetTarget when target is friendly.");

            AffectAllMissiles = cfg.Bind("Filter", "AffectAllMissiles", true, "MM/IFF/nuke-clone apply to all missiles");
            MissileNameWhitelist = cfg.Bind("Filter", "MissileNameWhitelist", "AAM,SAM,AGM,Missile,Cruise",
                "Used when AffectAllMissiles=false");
            MountBlacklist = cfg.Bind("Unrestricted", "MountBlacklist",
                "afv,lcv,hlt,container,hook,flex,turret",
                "Comma substrings excluded from any-hardpoint list.");
            DebugLog = cfg.Bind("General", "DebugLog", false, "Verbose logging");
            StrategicArsenal.Bind(cfg);
            KillAccolades.Bind(cfg);
            PlayerCareer.Bind(cfg);
            FlightAnalysis.Bind(cfg);
            FriendlyKillHud.Bind(cfg);
            MatchScoreboard.Bind(cfg);

            // Combined pack: share Oritasy Harmony id so probes do not list WeXon as a second plugin.
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
                + " loaded (vanilla + nuke variants, MM, AGM-T, F9 arsenal, career menu)"
#if ORITASY_COMBINED
                + " [Harmony " + Oritasy.PluginInfo.GUID + "]"
#endif
                + ".");
        }

        internal static bool IsVeyrnAcmPluginPresent()
        {
            try
            {
                if (Chainloader.PluginInfos != null
                    && (Chainloader.PluginInfos.ContainsKey("com.qiaochen.veyrnacm")
                        || Chainloader.PluginInfos.ContainsKey("com.iallemege.veyrnacm")))
                    return true;
            }
            catch { }
            try
            {
                Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < asms.Length; i++)
                {
                    Assembly a = asms[i];
                    if (a == null)
                        continue;
                    string n = a.GetName().Name;
                    if (string.Equals(n, "VeyrnAcm", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { }
            return false;
        }

        internal static bool AgmTOwnedByWeXon()
        {
            // Always owned by WeXon/Oritasy �?VeyrnAcm.dll is retired.
            return EnableAgmT != null && EnableAgmT.Value;
        }

        private static bool _harmonyApplied;

        internal static void PatchOwnNamespace(Harmony harmony)
        {
            if (harmony == null || _harmonyApplied)
                return;
            _harmonyApplied = true;
            Assembly asm = Assembly.GetExecutingAssembly();
            string ns = typeof(Plugin).Namespace;
            Type[] types = asm.GetTypes();
            for (int i = 0; i < types.Length; i++)
            {
                Type t = types[i];
                if (t == null || !t.IsClass)
                    continue;
                if (t.Namespace == null || t.Namespace != ns)
                    continue;
                try { harmony.CreateClassProcessor(t).Patch(); }
                catch (Exception ex)
                {
                    if (DebugLog != null && DebugLog.Value)
                        Log.LogWarning("Harmony patch " + t.Name + ": " + ex.Message);
                }
            }
        }

        internal static void HostedTick()
        {
            if (!_initDone)
                return;
            try
            {
#if ORITASY_COMBINED
                if (Oritasy.PerfProbeService.Sampling)
                    Oritasy.PerfProbeService.Measure("WeXon.Update", RunUpdateBody);
                else
                    RunUpdateBody();
#else
                RunUpdateBody();
#endif
            }
            catch (Exception ex)
            {
                if (Log != null)
                    Log.LogWarning("WeXon tick: " + ex.Message);
            }
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
#if ORITASY_COMBINED
            Oritasy.PerfFrameGate.BeginFrame();
            bool recover = Oritasy.PerfFrameGate.Recovering;
#else
            bool recover = false;
#endif
            if (!_scannedAssets)
            {
                _scannedAssets = true;
                ScanLoadedWeaponAssets();
            }

            // Nuke FX resolve is expensive (FindObjectsOfTypeAll GameObject) — retry slowly
            if (!recover && EnableNukeVariants.Value && SwapExplosionFx.Value && CachedNukeFx == null
                && Time.unscaledTime >= _nextNukeFxRetry)
            {
                _nextNukeFxRetry = Time.unscaledTime + 5f;
                ResolveNukeFx(false);
            }

            // Nuke mount clone scan is FindObjectsOfTypeAll — never every frame
            if (!recover && EnableNukeVariants.Value && !NukeMountsInjected
                && Time.unscaledTime >= _nextNukeMountRetry)
            {
                _nextNukeMountRetry = Time.unscaledTime + 3f;
                EnsureNukeMountClones();
            }

            // AGM-T: inject + rare maintenance (injected path must not hammer FindObjectsOfTypeAll)
            if (!recover && AgmTOwnedByWeXon() && Time.unscaledTime >= _nextAgmTRetry)
            {
                _nextAgmTRetry = Time.unscaledTime
                    + (AgmTWeapon.IsInjected && AgmTWeapon.HasUsableClones() ? 90f : 2f);
                AgmTWeapon.Ensure();
            }

            // AAM-2CV: AAM-36 clone + ACM-119 bus mesh; independent of EnableAgmT
            if (!recover && Time.unscaledTime >= _nextAam2CvRetry)
            {
                _nextAam2CvRetry = Time.unscaledTime
                    + (Aam2CvWeapon.IsInjected && Aam2CvWeapon.HasUsableClones() ? 90f : 2f);
                Aam2CvWeapon.Ensure();
            }

            // F9 menu hotkey must run every frame (GetKeyDown is one-frame only).
            StrategicArsenal.Tick();
            if (!recover && FlightAnalysis.OwnsRecording)
                FlightAnalysis.Tick();
            // Overlay HUD ticks always — skipping them flickered kill/career UI.
            // Packed payload MBs may not receive Unity Update; pump LOAL here.
            MultiModeBrain.PumpAll();
            TbmHuntAssist.PumpAll();
            AgmTDispenser.PumpAll();
            AgmTSubBrain.PumpAll();
            int frame = Time.frameCount;
            if ((frame % 2) == 1)
                KillAccolades.Tick();
            if ((frame % 2) == 0)
            {
                PlayerCareer.Tick();
            }
            if ((frame % 3) == 1)
                FriendlyKillHud.Tick();
            if ((frame % 3) == 2)
                MatchScoreboard.Tick();

            // Mount cache: only refresh when empty, or rarely for late-loaded assets
            if (!recover && UnrestrictedFeatureOn() && Time.unscaledTime >= NextMountRefresh)
            {
                NextMountRefresh = Time.unscaledTime + 90f;
                if (CachedMounts.Count == 0)
                    RefreshMountCache();
            }

            // Encyclopedia brand once (was every frame — major hitch)
            if (!recover && EnableEncyclopediaPages.Value && !_encyclopediaReady
                && Time.unscaledTime >= _nextEncyclopediaRetry)
            {
                _nextEncyclopediaRetry = Time.unscaledTime + 3f;
                if (GetEncyclopedia() != null)
                {
                    RefreshEncyclopediaData();
                    if (EncyclopediaInjected)
                        _encyclopediaReady = true;
                }
            }
        }

        private static bool _perfIsLowResolved;
        private static bool _perfIsLow;
        private static PropertyInfo _perfIsLowProp;

        private static bool CachedPerfIsLow()
        {
            if (!_perfIsLowResolved)
            {
                _perfIsLowResolved = true;
                try
                {
                    Type perf = Type.GetType("Oritasy.PerfMode");
                    if (perf != null)
                    {
                        _perfIsLowProp = perf.GetProperty("IsLow",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    }
                }
                catch { }
            }
            if (_perfIsLowProp == null)
                return false;
            try
            {
                object v = _perfIsLowProp.GetValue(null, null);
                _perfIsLow = v is bool && (bool)v;
            }
            catch { }
            return _perfIsLow;
        }

        private static int _huntQueryFrame = -1;
        private static int _huntQueryUsed;

        /// <summary>Grid hunts per frame. Never hitch-gated — LOAL lock is gameplay.</summary>
        internal static bool TryConsumeHuntQuery()
        {
            int f = Time.frameCount;
            if (_huntQueryFrame != f)
            {
                _huntQueryFrame = f;
                _huntQueryUsed = 0;
            }
            // Packed LOAL: 16 / 32 hunts per frame so salvos still acquire.
            int cap = CachedPerfIsLow() ? 16 : 32;
            if (_huntQueryUsed >= cap)
                return false;
            _huntQueryUsed++;
            return true;
        }

        /// <summary>Config floor for free-hunt (default 35 km). Per-missile hunt uses the overload.</summary>
        internal static float EffectiveHuntRadius()
        {
            float want = MadModeSearchRadius != null ? MadModeSearchRadius.Value : HuntRangeGateService.FloorM;
            if (want < 800f)
                want = 800f;
            return want;
        }

        /// <summary>Hunt radius longer than this missile's own range (min config floor, else range * 1.25).</summary>
        internal static float EffectiveHuntRadius(Missile missile)
        {
            return HuntRangeGateService.ResolveHuntRadiusM(
                EffectiveHuntRadius(),
                EstimateMissileRangeM(missile));
        }

        internal static float EstimateMissileRangeM(Missile missile)
        {
            if (missile == null)
                return HuntRangeGateService.FloorM;
            float range = 0f;
            try
            {
                if (Aam2CvWeapon.IsAam2CvMissile(missile))
                    range = Mathf.Max(range, Aam2CvGateService.RangeM);
            }
            catch { }
            try
            {
                if (AgmTWeapon.IsPoweredGs25Sub(missile))
                {
                    float gs = AgmTGs25MaxRange != null ? AgmTGs25MaxRange.Value : HuntRangeGateService.FloorM;
                    range = Mathf.Max(range, gs);
                }
                if (AgmTWeapon.HasBusDispenser(missile))
                    range = Mathf.Max(range, HuntRangeGateService.FloorM);
            }
            catch { }

            float infoMax = 0f;
            WeaponInfo info = InfoField != null ? InfoField.GetValue(missile) as WeaponInfo : null;
            if (info != null && info.maxSpeed > 10f)
                infoMax = info.maxSpeed;

            float top = 0f;
            try { top = missile.GetTopSpeed(0f, 0f); }
            catch { }

            bool cruise = false;
            bool ballistic = false;
            bool kh85 = false;
            try { cruise = IsCruiseMissile(missile); }
            catch { }
            try { ballistic = IsBallisticMissile(missile); }
            catch { }
            try { kh85 = IsKh85Family(missile); }
            catch { }

            float fallback = cruise || kh85
                ? HuntRangeGateService.DefaultCruiseSpeedMps
                : HuntRangeGateService.DefaultSpeedMps;
            float spd = HuntRangeGateService.PickSpeedMps(infoMax, top, fallback);

            float burn = 0f;
            try { burn = missile.GetTotalBurnTime(); }
            catch { }
            float kin = HuntRangeGateService.KinematicRangeM(burn, spd);
            if (kin > range)
                range = kin;

            if (cruise || kh85)
                range = Mathf.Max(range, HuntRangeGateService.CruiseClassMinRangeM);
            if (ballistic)
                range = Mathf.Max(range, HuntRangeGateService.BallisticClassMinRangeM);
            if (range < 1f)
                range = HuntRangeGateService.FloorM;
            return range;
        }

        internal static float EffectiveHuntInterval()
        {
            float want = MadModeSearchInterval != null ? MadModeSearchInterval.Value : 0.5f;
            float min = 0.5f;
            if (want < min)
                want = min;
            return want;
        }

        private void OnGUI()
        {
#if ORITASY_COMBINED
            // Combined pack: Oritasy.Plugin.OnGUI calls DrawHostedGui so Profile
            // still draws if this component failed to attach after Assembly.Load.
            return;
#else
            DrawCareerGui();
#endif
        }

        internal static void DrawCareerGui()
        {
            // First-run welcome / boot splash owns the screen
            if (OritasyHudBlocked())
                return;

            GuiScale.BeginGui();
            try
            {
                Event e = Event.current;
                // F9 support menu needs Layout / Mouse events for select + CONFIRM.
                StrategicArsenal.DrawGui();
                // Overlay HUDs: Repaint only
                if (e == null || e.type == EventType.Repaint)
                {
                    KillAccolades.DrawGui();
                    FriendlyKillHud.DrawGui();
                }
                // Career + Tab board need full IMGUI stream (Layout / drag / scroll).
                PlayerCareer.DrawGui();
                FlightAnalysis.DrawGui();
                MatchScoreboard.DrawGui();
            }
            finally
            {
                GuiScale.EndGui();
            }
        }

        private static bool _oritasyHudResolved;
        private static System.Reflection.PropertyInfo _oritasyBlocksHudProp;

        private static bool OritasyHudBlocked()
        {
            try
            {
                if (!_oritasyHudResolved)
                {
                    _oritasyHudResolved = true;
                    Type t = Type.GetType("Oritasy.OritasyPresentation, Oritasy")
                        ?? Type.GetType("Oritasy.OritasyPresentation");
                    if (t != null)
                    {
                        _oritasyBlocksHudProp = t.GetProperty("BlocksHud",
                            System.Reflection.BindingFlags.Static
                            | System.Reflection.BindingFlags.Public
                            | System.Reflection.BindingFlags.NonPublic);
                    }
                }
                if (_oritasyBlocksHudProp == null)
                    return false;
                object v = _oritasyBlocksHudProp.GetValue(null, null);
                return v is bool && (bool)v;
            }
            catch { return false; }
        }

        private void OnApplicationQuit()
        {
            PlayerCareer.FlushForQuit();
            try { FlightAnalysis.Shutdown(); }
            catch { }
        }

        private void OnDestroy()
        {
            PlayerCareer.FlushForQuit();
            try { FlightAnalysis.Shutdown(); }
            catch { }
        }

        private static void CacheWarheadReflection()
        {
            WarheadType = AccessTools.Inner(typeof(Missile), "Warhead");
            if (WarheadType == null && WarheadField != null)
                WarheadType = WarheadField.FieldType;
            if (WarheadType == null)
                return;
            WhAir = AccessTools.Field(WarheadType, "airEffect");
            WhArmor = AccessTools.Field(WarheadType, "armorEffect");
            WhTerrain = AccessTools.Field(WarheadType, "terrainEffect");
            WhWater = AccessTools.Field(WarheadType, "waterSurfaceEffect");
            WhUnder = AccessTools.Field(WarheadType, "underwaterEffect");
            WhArmed = AccessTools.Field(WarheadType, "Armed");
        }

        internal static void AppendTag(ref string text, string tag)
        {
            if (string.IsNullOrEmpty(tag))
                return;
            if (string.IsNullOrEmpty(text))
            {
                text = tag;
                return;
            }
            if (text.IndexOf(tag, StringComparison.OrdinalIgnoreCase) >= 0)
                return;
            text = text + " " + tag;
        }

        internal static void StripTag(ref string text, string tag)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(tag))
                return;
            int idx = text.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
            while (idx >= 0)
            {
                int end = idx + tag.Length;
                if (idx > 0 && text[idx - 1] == ' ')
                    idx--;
                else if (end < text.Length && text[end] == ' ')
                    end++;
                text = text.Remove(idx, end - idx).Trim();
                idx = text.IndexOf(tag, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Cosmetic tags: dual-mode guided missiles get [IAL] (ex-[MM]); nuke clones also get [10kt].
        /// Identity (ship block / PreferNukes / ApplyNuke) uses jsonKey _IAL / NukeInfoIds — not the name tag.
        /// </summary>
        internal static void ApplyNameTags(ref string name, ref string shortN, ref string desc, bool nuke, bool ial)
        {
            StripTag(ref name, "[MM]");
            StripTag(ref shortN, "[MM]");
            StripTag(ref desc, "[MM]");
            // Relabel old 20kt tags when migrating to 10kt
            StripTag(ref name, "[20kt]");
            StripTag(ref shortN, "[20kt]");
            StripTag(ref desc, "[20kt]");
            if (MmNameTag != null && !string.IsNullOrEmpty(MmNameTag.Value))
            {
                StripTag(ref name, MmNameTag.Value);
                StripTag(ref shortN, MmNameTag.Value);
                StripTag(ref desc, MmNameTag.Value);
            }

            // [IAL] only when dual-mode / seeker-eligible (not rockets, cruise, ballistic, shells)
            if (ial && IalLabelNames.Value)
            {
                AppendTag(ref name, IalNameTag.Value);
                AppendTag(ref shortN, IalNameTag.Value);
            }
            else
            {
                StripTag(ref name, "[IAL]");
                StripTag(ref shortN, "[IAL]");
                if (IalNameTag != null)
                {
                    StripTag(ref name, IalNameTag.Value);
                    StripTag(ref shortN, IalNameTag.Value);
                }
            }

            // Yield tag only on real nuclear IAL clones
            if (nuke && NukeLabelNames.Value)
            {
                AppendTag(ref name, NukeNameTag.Value);
                AppendTag(ref shortN, NukeNameTag.Value);
                AppendTag(ref desc, NukeNameTag.Value);
                EnsureNukeDescriptionLine(ref desc);
            }
            else
            {
                // Keep conventional missiles from carrying leftover yield tags
                StripTag(ref name, "[10kt]");
                StripTag(ref shortN, "[10kt]");
                if (NukeNameTag != null)
                {
                    StripTag(ref name, NukeNameTag.Value);
                    StripTag(ref shortN, NukeNameTag.Value);
                }
            }
        }

        /// <summary>
        /// [IAL] = self-guided dual-mode eligible (historical [MM] rename),
        /// or naval-exclusive SAM (RAM-45 / R9) which ships still brand.
        /// </summary>
        internal static bool ShouldCarryIalLabel(WeaponInfo info)
        {
            if (info == null)
                return false;
            string n = ((info.weaponName != null ? info.weaponName : string.Empty) + " "
                + (info.shortName != null ? info.shortName : string.Empty) + " "
                + (info.name != null ? info.name : string.Empty));
            bool naval = ShipMissileService.IsNavalExclusiveWeaponInfo(info);
            return IalLabelGateService.ShouldCarryIalLabel(
                IsMissileInfoAllowed(info) || naval,
                IsGunShellInfo(info),
                IsScannerReconInfo(info),
                IsRocketOrUnguidedName(n),
                IsBallisticMissileName(n),
                IsCruiseMissileName(n),
                ShouldLeaveStockNuclearAlone(info),
                AgmTWeapon.IsAgmTInfo(info) || Aam2CvWeapon.IsAam2CvInfo(info),
                IsIalNukeCloneInfo(info),
                InfoHasDualModeSeeker(info) || naval);
        }

        /// <summary>Prefab has a seeker MultiMode would attach to (not shell / ballistic / cruise seeker).</summary>
        internal static bool InfoHasDualModeSeeker(WeaponInfo info)
        {
            if (info == null || info.weaponPrefab == null)
                return false;
            try
            {
                Missile m = info.weaponPrefab.GetComponent<Missile>();
                if (m == null)
                    m = info.weaponPrefab.GetComponentInChildren<Missile>(true);
                if (m == null)
                    return false;
                MissileSeeker s = SeekerField != null ? SeekerField.GetValue(m) as MissileSeeker : null;
                if (s == null)
                    s = m.GetComponent<MissileSeeker>();
                if (s == null)
                    s = m.GetComponentInChildren<MissileSeeker>(true);
                if (s == null)
                    return false;
                if (IsGunShellSeeker(s))
                    return false;
                if (s is BallisticMissileGuidance)
                    return false;
                if (s is OpticalSeekerCruiseMissile)
                    return false;
                return true;
            }
            catch { return false; }
        }

        /// <summary>Kh85MT owns TGM-85 display names ([IAL]; C also [10kt]).</summary>
        internal static bool IsKh85WeaponInfo(WeaponInfo info)
        {
            if (info == null)
                return false;
            string n = ((info.weaponName != null ? info.weaponName : string.Empty) + " "
                + (info.shortName != null ? info.shortName : string.Empty) + " "
                + (info.name != null ? info.name : string.Empty));
            return Kh85GuideGateService.WeaponBlobLooksKh85(n);
        }

        internal static bool IsKh85Mount(WeaponMount mount)
        {
            if (mount == null)
                return false;
            if (!string.IsNullOrEmpty(mount.jsonKey)
                && mount.jsonKey.StartsWith("Kh85MT", StringComparison.OrdinalIgnoreCase))
                return true;
            return IsKh85WeaponInfo(mount.info);
        }

        /// <summary>TGM-85C Shardfall racks (all ammo counts) are the 10kt nuclear fit.</summary>
        internal static bool IsTgm85CNuclearInfo(WeaponInfo info)
        {
            if (info == null || !IsKh85WeaponInfo(info))
                return false;
            string n = ((info.weaponName != null ? info.weaponName : string.Empty) + " "
                + (info.shortName != null ? info.shortName : string.Empty) + " "
                + (info.name != null ? info.name : string.Empty));
            if (n.IndexOf("TGM-85C", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("Shardfall", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            // Bare Kh85MT info asset without letter = C family
            if (info.name != null && info.name.StartsWith("Kh85MT_info_", StringComparison.OrdinalIgnoreCase))
            {
                string key = info.name.Substring("Kh85MT_info_".Length);
                return !IsKh85LetterVariantKey(key);
            }
            return false;
        }

        internal static bool IsTgm85CNuclearMount(WeaponMount mount)
        {
            if (mount == null)
                return false;
            if (!string.IsNullOrEmpty(mount.jsonKey)
                && mount.jsonKey.StartsWith("Kh85MT", StringComparison.OrdinalIgnoreCase))
            {
                bool saw;
                Kh85GuideGateService.KindFromJsonKey(mount.jsonKey, out saw);
                // C-family keys (no A/B/D/E/S letter)
                if (saw && !IsKh85LetterVariantKey(mount.jsonKey))
                    return true;
            }
            return IsTgm85CNuclearInfo(mount.info);
        }

        private static bool IsKh85LetterVariantKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            string core = key;
            if (core.EndsWith("_IAL", StringComparison.OrdinalIgnoreCase))
                core = core.Substring(0, core.Length - 4);
            if (!core.StartsWith("Kh85MT", StringComparison.OrdinalIgnoreCase))
                return false;
            string rest = core.Length > 6 ? core.Substring(6) : string.Empty;
            return Kh85GuideGateService.IsLetterToken(rest, "A")
                || Kh85GuideGateService.IsLetterToken(rest, "B")
                || Kh85GuideGateService.IsLetterToken(rest, "D")
                || Kh85GuideGateService.IsLetterToken(rest, "E")
                || Kh85GuideGateService.IsLetterToken(rest, "S");
        }

        internal static bool IsTgm85CNuclearMissile(Missile missile)
        {
            if (missile == null)
                return false;
            WeaponInfo info = InfoField != null ? InfoField.GetValue(missile) as WeaponInfo : null;
            if (IsTgm85CNuclearInfo(info))
                return true;
            string n = (missile.name != null ? missile.name : string.Empty);
            try
            {
                if (missile.definition != null)
                {
                    n = n + " " + (missile.definition.unitName != null ? missile.definition.unitName : string.Empty)
                        + " " + (missile.definition.jsonKey != null ? missile.definition.jsonKey : string.Empty);
                }
            }
            catch { }
            return n.IndexOf("TGM-85C", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Shardfall", StringComparison.OrdinalIgnoreCase) >= 0
                || (n.IndexOf("Kh85MT", StringComparison.OrdinalIgnoreCase) >= 0
                    && !IsKh85LetterVariantKey(missile.definition != null ? missile.definition.jsonKey : null)
                    && GetKh85Kind(missile) == Kh85Kind.TerrainAim);
        }

        private const string NukeDescLine = "This is a nuclear missile";

        internal static void EnsureLinePrefix(ref string desc, string line)
        {
            if (string.IsNullOrEmpty(line))
                return;
            if (!string.IsNullOrEmpty(desc)
                && desc.IndexOf(line, StringComparison.OrdinalIgnoreCase) >= 0)
                return;
            if (string.IsNullOrEmpty(desc))
                desc = line;
            else
                desc = line + "\n\n" + desc;
        }

        internal static void EnsureNukeDescriptionLine(ref string desc)
        {
            EnsureLinePrefix(ref desc, NukeDescLine);
            EnsureLinePrefix(ref desc, PackDescLine);
        }

        /// <summary>Brand description with [WeXon] like other WeXon encyclopedia / weapon texts.</summary>
        internal static void EnsureWexonDescriptionTag(ref string desc)
        {
            EnsureLinePrefix(ref desc, PackDescLine);
        }

        /// <summary>
        /// Vanilla weapons that already ship nuclear (Genie, 20kt cruise, tacNuke, etc.).
        /// Do not create redundant IAL / [10kt] clones for these.
        /// </summary>
        internal static bool IsVanillaNuclearWeaponInfo(WeaponInfo info)
        {
            if (info == null)
                return false;
            if (IsIalNukeCloneInfo(info))
                return false;
            if (NukeInfoIds.Contains(info.GetInstanceID()))
                return false;
            if (AgmTWeapon.IsAgmTInfo(info))
                return false;
            if (Aam2CvWeapon.IsAam2CvInfo(info))
                return false;
            if (info.name != null && info.name.IndexOf("_IAL", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            if (info.nuclear)
                return true;
            // Recover demoted stock nukes (sanitize used to clear .nuclear after adding [IAL])
            return StockNuclearNameHeuristic(info);
        }

        /// <summary>
        /// Stock nuclear munitions: never change yield, never add [10kt]/[IAL] yield branding.
        /// </summary>
        internal static bool ShouldLeaveStockNuclearAlone(WeaponInfo info)
        {
            return IsVanillaNuclearWeaponInfo(info);
        }

        private static bool StockNuclearNameHeuristic(WeaponInfo info)
        {
            if (info == null)
                return false;
            string blob = ((info.weaponName != null ? info.weaponName : string.Empty) + " "
                + (info.shortName != null ? info.shortName : string.Empty) + " "
                + (info.name != null ? info.name : string.Empty)).ToLowerInvariant();
            return blob.IndexOf("genie", StringComparison.Ordinal) >= 0
                || blob.IndexOf("air-2", StringComparison.Ordinal) >= 0
                || blob.IndexOf("air2", StringComparison.Ordinal) >= 0
                || blob.IndexOf("20kt", StringComparison.Ordinal) >= 0
                || blob.IndexOf("tacnuke", StringComparison.Ordinal) >= 0
                || blob.IndexOf("tac_nuke", StringComparison.Ordinal) >= 0
                || blob.IndexOf("tac nuke", StringComparison.Ordinal) >= 0;
        }

        internal static bool IsVanillaNuclearMissileDef(MissileDefinition def)
        {
            if (def == null)
                return false;

            string key = def.jsonKey != null ? def.jsonKey : string.Empty;
            string blob = ((def.unitName != null ? def.unitName : string.Empty) + " "
                + key + " " + (def.name != null ? def.name : string.Empty) + " "
                + (def.code != null ? def.code : string.Empty));
            return blob.IndexOf("Genie", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("AIR-2", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("AIR2", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("20kt", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("tacNuke", StringComparison.OrdinalIgnoreCase) >= 0
                || blob.IndexOf("nuclear", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void EnsureVanillaNuclearMountKeyCache()
        {
            if (VanillaNuclearMountKeys != null)
                return;
            VanillaNuclearMountKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            WeaponMount[] all = Resources.FindObjectsOfTypeAll<WeaponMount>();
            if (all == null)
                return;
            for (int i = 0; i < all.Length; i++)
            {
                WeaponMount src = all[i];
                if (src == null || IsIalKey(src.jsonKey) || !IsVanillaNuclearWeaponInfo(src.info))
                    continue;
                if (!string.IsNullOrEmpty(src.jsonKey))
                    VanillaNuclearMountKeys.Add(src.jsonKey);
            }
        }

        /// <summary>Drop IAL clones that duplicate already-nuclear vanilla munitions (once).</summary>
        internal static void PurgeRedundantNuclearIalClones()
        {
            if (NuclearIalPurgeDone)
                return;
            NuclearIalPurgeDone = true;

            EnsureVanillaNuclearMountKeyCache();
            Encyclopedia enc = GetEncyclopedia();
            int removed = 0;

            for (int i = NukeMountClones.Count - 1; i >= 0; i--)
            {
                WeaponMount m = NukeMountClones[i];
                if (m == null || !ShouldPurgeIalMount(m))
                    continue;
                NukeMountClones.RemoveAt(i);
                CachedMountSet.Remove(m);
                CachedMounts.Remove(m);
                if (m.info != null)
                    NukeInfoIds.Remove(m.info.GetInstanceID());
                if (!string.IsNullOrEmpty(m.jsonKey))
                    CreatedNukeKeys.Remove(m.jsonKey);
                if (enc != null && enc.weaponMounts != null)
                    enc.weaponMounts.Remove(m);
                if (Encyclopedia.WeaponLookup != null && !string.IsNullOrEmpty(m.jsonKey))
                    Encyclopedia.WeaponLookup.Remove(m.jsonKey);
                try { UnityEngine.Object.Destroy(m); }
                catch { }
                removed++;
            }

            if (enc != null && enc.weaponMounts != null)
            {
                for (int i = enc.weaponMounts.Count - 1; i >= 0; i--)
                {
                    WeaponMount m = enc.weaponMounts[i];
                    if (m == null || !ShouldPurgeIalMount(m))
                        continue;
                    enc.weaponMounts.RemoveAt(i);
                    if (Encyclopedia.WeaponLookup != null && !string.IsNullOrEmpty(m.jsonKey))
                        Encyclopedia.WeaponLookup.Remove(m.jsonKey);
                    CachedMountSet.Remove(m);
                    CachedMounts.Remove(m);
                    removed++;
                }
            }

            if (enc != null && enc.missiles != null)
            {
                Dictionary<string, MissileDefinition> byKey =
                    new Dictionary<string, MissileDefinition>(StringComparer.OrdinalIgnoreCase);
                for (int j = 0; j < enc.missiles.Count; j++)
                {
                    MissileDefinition c = enc.missiles[j];
                    if (c != null && !string.IsNullOrEmpty(c.jsonKey) && !IsIalMissileDef(c))
                        byKey[c.jsonKey] = c;
                }
                for (int i = enc.missiles.Count - 1; i >= 0; i--)
                {
                    MissileDefinition d = enc.missiles[i];
                    if (d == null || !IsIalMissileDef(d))
                        continue;
                    string baseKey = StripIalKeySuffix(d.jsonKey);
                    MissileDefinition vanilla;
                    if (string.IsNullOrEmpty(baseKey) || !byKey.TryGetValue(baseKey, out vanilla))
                        continue;
                    if (!IsVanillaNuclearMissileDef(vanilla))
                        continue;
                    enc.missiles.RemoveAt(i);
                    if (Encyclopedia.Lookup != null && !string.IsNullOrEmpty(d.jsonKey))
                        Encyclopedia.Lookup.Remove(d.jsonKey);
                    removed++;
                }
            }

            if (removed > 0)
                Log.LogInfo("Purged " + removed + " redundant IAL clones of vanilla nuclear weapons");
        }

        private static string StripIalKeySuffix(string key)
        {
            if (string.IsNullOrEmpty(key))
                return key;
            if (key.EndsWith("_IAL", StringComparison.OrdinalIgnoreCase))
                return key.Substring(0, key.Length - 4);
            return key;
        }

        private static bool ShouldPurgeIalMount(WeaponMount m)
        {
            if (m == null || !IsIalKey(m.jsonKey))
                return false;
            string baseKey = StripIalKeySuffix(m.jsonKey);
            if (string.IsNullOrEmpty(baseKey))
                return false;
            EnsureVanillaNuclearMountKeyCache();
            return VanillaNuclearMountKeys != null && VanillaNuclearMountKeys.Contains(baseKey);
        }

        /// <summary>Brand description with [WeXon] like other WeXon missiles.</summary>
        internal static void EnsureWexonDescriptionLine(ref string desc)
        {
            EnsureLinePrefix(ref desc, PackDescLine);
        }

        /// <summary>Vanilla weapons that already ship nuclear (Genie, 20kt cruise, tacNuke bombs, …).</summary>
        internal static bool IsVanillaNuclearInfo(WeaponInfo info)
        {
            return IsVanillaNuclearWeaponInfo(info);
        }

        internal static bool MissileDefIsVanillaNuclear(MissileDefinition def)
        {
            if (def == null)
                return false;
            string blob = ((def.unitName != null ? def.unitName : string.Empty) + " "
                + (def.jsonKey != null ? def.jsonKey : string.Empty) + " "
                + (def.name != null ? def.name : string.Empty) + " "
                + (def.code != null ? def.code : string.Empty)).ToLowerInvariant();
            if (blob.IndexOf("genie", StringComparison.Ordinal) >= 0
                || blob.IndexOf("air-2", StringComparison.Ordinal) >= 0
                || blob.IndexOf("air2", StringComparison.Ordinal) >= 0
                || blob.IndexOf("20kt", StringComparison.Ordinal) >= 0
                || blob.IndexOf("tacnuke", StringComparison.Ordinal) >= 0
                || blob.IndexOf("tac_nuke", StringComparison.Ordinal) >= 0
                || blob.IndexOf("nuclear", StringComparison.Ordinal) >= 0)
                return true;

            try
            {
                WeaponInfo[] infos = Resources.FindObjectsOfTypeAll<WeaponInfo>();
                for (int i = 0; i < infos.Length; i++)
                {
                    WeaponInfo info = infos[i];
                    if (!IsVanillaNuclearInfo(info) || info.weaponPrefab == null)
                        continue;
                    Missile m = info.weaponPrefab.GetComponent<Missile>();
                    if (m == null || m.definition == null)
                        continue;
                    if (object.ReferenceEquals(m.definition, def))
                        return true;
                    if (!string.IsNullOrEmpty(def.jsonKey)
                        && string.Equals(m.definition.jsonKey, def.jsonKey, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>Drop IAL [10kt] clones that duplicate already-nuclear vanilla missiles.</summary>
        internal static void PurgeIalClonesOfVanillaNukes()
        {
            Encyclopedia enc = GetEncyclopedia();
            int removed = 0;

            for (int i = NukeMountClones.Count - 1; i >= 0; i--)
            {
                WeaponMount m = NukeMountClones[i];
                if (m == null || !IsIalMountOfVanillaNuclear(m))
                    continue;
                RemoveMountEverywhere(m, enc);
                NukeMountClones.RemoveAt(i);
                if (m.info != null)
                    NukeInfoIds.Remove(m.info.GetInstanceID());
                if (!string.IsNullOrEmpty(m.jsonKey))
                    CreatedNukeKeys.Remove(m.jsonKey);
                removed++;
            }

            // Also scan encyclopedia / all mounts for stale IAL nukes of Genie etc.
            WeaponMount[] all = Resources.FindObjectsOfTypeAll<WeaponMount>();
            if (all != null)
            {
                for (int i = 0; i < all.Length; i++)
                {
                    WeaponMount m = all[i];
                    if (m == null || !IsIalKey(m.jsonKey))
                        continue;
                    if (!IsIalMountOfVanillaNuclear(m))
                        continue;
                    RemoveMountEverywhere(m, enc);
                    if (m.info != null)
                        NukeInfoIds.Remove(m.info.GetInstanceID());
                    CreatedNukeKeys.Remove(m.jsonKey);
                    removed++;
                }
            }

            if (enc != null && enc.missiles != null)
            {
                for (int i = enc.missiles.Count - 1; i >= 0; i--)
                {
                    MissileDefinition d = enc.missiles[i];
                    if (d == null || !IsIalMissileDef(d) || string.IsNullOrEmpty(d.jsonKey))
                        continue;
                    string key = d.jsonKey;
                    if (!key.EndsWith("_IAL", StringComparison.OrdinalIgnoreCase))
                        continue;
                    string baseKey = key.Substring(0, key.Length - 4);
                    MissileDefinition baseDef = FindMissileDefByKey(enc, baseKey);
                    if (baseDef == null || !MissileDefIsVanillaNuclear(baseDef))
                        continue;
                    enc.missiles.RemoveAt(i);
                    if (Encyclopedia.Lookup != null && Encyclopedia.Lookup.ContainsKey(key))
                        Encyclopedia.Lookup.Remove(key);
                    removed++;
                }
            }

            if (removed > 0)
                Log.LogInfo("Purged " + removed + " IAL nuke clones of already-nuclear vanilla weapons");
        }

        private static bool IsIalMountOfVanillaNuclear(WeaponMount m)
        {
            if (m == null || !IsIalKey(m.jsonKey))
                return false;
            string key = m.jsonKey;
            string baseKey = key.Substring(0, key.Length - 4); // strip _IAL
            // Resolve base mount
            WeaponMount[] all = Resources.FindObjectsOfTypeAll<WeaponMount>();
            for (int i = 0; i < all.Length; i++)
            {
                WeaponMount src = all[i];
                if (src == null || IsIalKey(src.jsonKey))
                    continue;
                if (!string.Equals(src.jsonKey, baseKey, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (IsVanillaNuclearInfo(src.info))
                    return true;
            }
            // Name heuristics on the IAL clone itself / base key
            string blob = (baseKey + " " + (m.name != null ? m.name : string.Empty)
                + " " + (m.mountName != null ? m.mountName : string.Empty)).ToLowerInvariant();
            if (blob.IndexOf("genie", StringComparison.Ordinal) >= 0
                || blob.IndexOf("air-2", StringComparison.Ordinal) >= 0
                || blob.IndexOf("20kt", StringComparison.Ordinal) >= 0
                || blob.IndexOf("tacnuke", StringComparison.Ordinal) >= 0
                || blob.IndexOf("nuclear", StringComparison.Ordinal) >= 0)
                return true;
            return false;
        }

        private static void RemoveMountEverywhere(WeaponMount m, Encyclopedia enc)
        {
            if (m == null)
                return;
            CachedMountSet.Remove(m);
            CachedMounts.Remove(m);
            if (enc != null && enc.weaponMounts != null)
                enc.weaponMounts.Remove(m);
            if (Encyclopedia.WeaponLookup != null && !string.IsNullOrEmpty(m.jsonKey)
                && Encyclopedia.WeaponLookup.ContainsKey(m.jsonKey))
                Encyclopedia.WeaponLookup.Remove(m.jsonKey);
            try
            {
                if (enc != null && enc.IndexLookup != null)
                    enc.IndexLookup.Remove(m);
            }
            catch { }
            try { UnityEngine.Object.Destroy(m); }
            catch { }
        }

        private static MissileDefinition FindMissileDefByKey(Encyclopedia enc, string key)
        {
            if (enc == null || enc.missiles == null || string.IsNullOrEmpty(key))
                return null;
            for (int i = 0; i < enc.missiles.Count; i++)
            {
                MissileDefinition d = enc.missiles[i];
                if (d != null && string.Equals(d.jsonKey, key, StringComparison.OrdinalIgnoreCase))
                    return d;
            }
            if (Encyclopedia.Lookup != null && Encyclopedia.Lookup.ContainsKey(key))
                return Encyclopedia.Lookup[key] as MissileDefinition;
            return null;
        }

        internal static bool NameMatchesWhitelist(string n)
        {
            if (IsGunShellName(n))
                return false;
            if (AffectAllMissiles.Value)
                return true;
            if (string.IsNullOrEmpty(n))
                return false;
            string[] parts = MissileNameWhitelist.Value.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length > 0 && n.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Naval / artillery gun shells (76mm, 127mm, 155mm) �?exclude from MM / IFF / IAL.
        /// Game only has three guided shell MissileDefinitions; other guns use BulletSim.
        /// </summary>
        internal static bool IsGunShellName(string n)
        {
            return MissileClassifyGateService.IsGunShellName(n);
        }

        internal static bool IsGunShellSeeker(MissileSeeker seeker)
        {
            if (seeker == null)
                return false;
            return seeker is OpticalSeekerShell || seeker is InertialSeekerShell;
        }

        /// <summary>True when missile has no rocket motors (gun shells, some submunitions).</summary>
        internal static bool IsMotorlessProjectile(Missile missile)
        {
            if (missile == null || MotorsField == null)
                return false;
            try
            {
                Array motors = MotorsField.GetValue(missile) as Array;
                return motors == null || motors.Length == 0;
            }
            catch { return false; }
        }

        internal static bool IsGunShellMissile(Missile missile)
        {
            if (missile == null)
                return false;
            byte flags;
            if (TryGetFlightClass(missile, out flags))
                return (flags & MissileFlightClassCache.GunShell) != 0;
            return ResolveFlightClass(missile, out flags)
                && (flags & MissileFlightClassCache.GunShell) != 0;
        }

        internal static bool IsGunShellMissileUncached(Missile missile)
        {
            if (missile == null)
                return false;
            if (IsGunShellName(missile.name))
                return true;
            try
            {
                // unitName is often "76mm Guided Shell" while GO name may differ after net spawn
                if (missile.definition != null && IsGunShellName(missile.definition.unitName))
                    return true;
                if (missile.definition != null && IsGunShellName(missile.definition.jsonKey))
                    return true;
            }
            catch { }
            try
            {
                MissileSeeker seeker = SeekerField != null ? SeekerField.GetValue(missile) as MissileSeeker : null;
                // OpticalSeekerShell / InertialSeekerShell are used ONLY by 76/127/155 gun shells
                if (IsGunShellSeeker(seeker))
                    return true;
                if (missile.GetComponent<OpticalSeekerShell>() != null
                    || missile.GetComponent<InertialSeekerShell>() != null
                    || missile.GetComponentInChildren<OpticalSeekerShell>(true) != null
                    || missile.GetComponentInChildren<InertialSeekerShell>(true) != null)
                    return true;
            }
            catch { }
            WeaponInfo info = InfoField != null ? InfoField.GetValue(missile) as WeaponInfo : null;
            return IsGunShellInfo(info);
        }

        private static bool TryGetFlightClass(Missile missile, out byte flags)
        {
            flags = 0;
            if (missile == null)
                return false;
            return MissileFlightClassCache.TryGet(missile.GetInstanceID(), out flags);
        }

        private static bool ResolveFlightClass(Missile missile, out byte flags)
        {
            flags = MissileFlightClassCache.Resolved;
            if (missile == null)
                return false;
            if (IsGunShellMissileUncached(missile))
                flags |= MissileFlightClassCache.GunShell;
            if (IsBallisticMissileUncached(missile))
                flags |= MissileFlightClassCache.Ballistic;
            if (IsCruiseMissileUncached(missile))
                flags |= MissileFlightClassCache.Cruise;
            MissileFlightClassCache.Set(missile.GetInstanceID(), flags);
            return true;
        }

        internal static bool IsGunShellInfo(WeaponInfo info)
        {
            if (info == null)
                return false;
            string n = ((info.weaponName != null ? info.weaponName : string.Empty) + " "
                + (info.shortName != null ? info.shortName : string.Empty) + " " + info.name);
            return IsGunShellName(n);
        }

        internal static bool IsMissileInfoAllowed(WeaponInfo info)
        {
            if (info == null || !info.missile)
                return false;
            if (IsGunShellInfo(info))
                return false;
            string n = ((info.weaponName != null ? info.weaponName : string.Empty) + " "
                + (info.shortName != null ? info.shortName : string.Empty) + " " + info.name);
            return NameMatchesWhitelist(n);
        }

        internal static bool IsMissileAllowed(Missile missile)
        {
            if (missile == null)
                return false;
            if (IsGunShellMissile(missile))
                return false;
            return NameMatchesWhitelist(missile.name != null ? missile.name : string.Empty);
        }

        /// <summary>WeXon IAL [10kt] clone only — never stock Genie / 20kt / tacNuke.</summary>
        internal static bool IsNukeVariantInfo(WeaponInfo info)
        {
            return IsIalNukeCloneInfo(info);
        }

        /// <summary>
        /// True only for WeXon IAL nuke clones (never vanilla nuclear like AIR-2 Genie).
        /// Every *_IAL mount/info twin is a nuclear clone by design — do not require [10kt]
        /// text (Sanitize / cosmetic relabel can strip tags without clearing identity).
        /// ACNM-118 is 1.5kt and is handled by AgmTWeapon — not an IAL [10kt] twin.
        /// </summary>
        internal static bool IsIalNukeCloneInfo(WeaponInfo info)
        {
            if (info == null)
                return false;
            if (AgmTWeapon.IsAgmTInfo(info))
                return false;
            if (Aam2CvWeapon.IsAam2CvInfo(info))
                return false;
            if (NukeInfoIds.Contains(info.GetInstanceID()))
                return true;
            // Instantiated clone assets are named …_IAL — never trust display [IAL]/[10kt] alone
            if (info.name != null && info.name.IndexOf("_IAL", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        internal static bool IsNukeVariantMissile(Missile missile)
        {
            if (missile == null)
                return false;
            WeaponInfo info = InfoField != null ? InfoField.GetValue(missile) as WeaponInfo : null;
            return IsNukeVariantInfo(info);
        }

        internal static bool IsIalKey(string key)
        {
            return LoadoutMountGateService.IsIalKey(key);
        }

        internal static bool IsIalMount(WeaponMount mount)
        {
            if (mount == null)
                return false;
            if (IsIalKey(mount.jsonKey))
                return true;
            if (mount.name != null && mount.name.IndexOf("_IAL", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            // Do not use bare "[IAL]" on shared WeaponInfo �?that polluted vanilla mounts
            if (mount.info != null && NukeInfoIds.Contains(mount.info.GetInstanceID()))
                return true;
            return false;
        }

        internal static bool IsIalWeapon(Weapon weapon)
        {
            if (weapon == null)
                return false;
            WeaponMount mount = GetWeaponMount(weapon);
            if (IsIalMount(mount))
                return true;
            return IsIalNukeCloneInfo(weapon.info);
        }

        internal static bool IsShipUnit(Unit unit)
        {
            return ShipMissileService.IsNavalLauncher(unit);
        }

        /// <summary>Ship-edition missile (tag or RAM-45/R9). Aircraft shots never match.</summary>
        internal static bool IsShipLaunchedMissile(Missile missile)
        {
            return ShipMissileService.IsShipEdition(missile);
        }

        internal static bool IsSamRadarFamilyMissile(Missile missile)
        {
            return ShipMissileService.IsNavalExclusiveMunition(missile);
        }

        internal static bool IsVlsSoftLaunchMissile(Missile missile)
        {
            return ShipMissileService.IsVlsEdition(missile);
        }

        internal static void StampShipMissile(Missile missile, Unit spawnOwner)
        {
            ShipMissileService.Stamp(missile, spawnOwner);
            ApplyIalInFlightName(missile);
        }

        /// <summary>HUD / kill-feed name: keep [IAL] on ship SAMs and dual-mode guided.</summary>
        internal static void ApplyIalInFlightName(Missile missile)
        {
            if (missile == null || IalLabelNames == null || !IalLabelNames.Value)
                return;
            try
            {
                string n = missile.NetworkunitName;
                if (string.IsNullOrEmpty(n) && missile.definition != null)
                    n = missile.definition.unitName;
                if (string.IsNullOrEmpty(n))
                    return;
                if (n.IndexOf("[IAL]", StringComparison.OrdinalIgnoreCase) >= 0)
                    return;
                WeaponInfo info = InfoField != null ? InfoField.GetValue(missile) as WeaponInfo : null;
                bool ial = (info != null && ShouldCarryIalLabel(info))
                    || IsSamRadarFamilyMissile(missile);
                if (!ial)
                    return;
                AppendTag(ref n, IalNameTag.Value);
                missile.NetworkunitName = n;
            }
            catch { }
        }

        internal static void NoteShipMissileFire(Unit owner)
        {
            ShipMissileService.NoteFire(owner);
        }

        /// <summary>Low-speed SD threshold in m/s (config stored as km/h).</summary>
        internal static float LowSpeedSelfDestructThresholdMps()
        {
            float kmh = LowSpeedSelfDestructMps != null ? LowSpeedSelfDestructMps.Value : 15f;
            return LowSpeedSdGateService.ResolveThresholdMps(kmh);
        }

        /// <summary>
        /// Soft CIWS / trainable rails often spawn with near-zero muzzle velocity.
        /// VLS / SARH (RAM-45) are skipped: vanilla pitches at ~0 m/s then lights the delayed motor.
        /// </summary>
        internal static void BoostShipMissileLaunchVelocity(Missile missile)
        {
            if (missile == null || missile.rb == null || missile.disabled)
                return;
            float min = ShipMissileMinLaunchMps != null
                ? ShipMissileMinLaunchMps.Value
                : ShipBoostMathService.DefaultMinLaunchMps;
            float age = 0f;
            try { age = missile.timeSinceSpawn; }
            catch { }

            Vector3 dir = missile.transform.forward;
            if (dir.sqrMagnitude < 0.01f)
                dir = Vector3.forward;
            dir.Normalize();

            Vector3 v = missile.rb.velocity;
            float along = Vector3.Dot(v, dir);
            float spd = v.magnitude;
            float noseUpDot = Vector3.Dot(dir, Vector3.up);
            bool sarh = false;
            try { sarh = missile.GetComponent<SARHSeeker>() != null; }
            catch { }
            float need = ShipBoostMathService.ResolveKickDeltaV(
                IsShipLaunchedMissile(missile), true, age, min, along, spd, noseUpDot, sarh);
            if (need <= 0f)
                return;

            missile.rb.velocity = v + dir * need;
            try { missile.startingVelocity = missile.rb.velocity; }
            catch { }
        }

        /// <summary>
        /// Hardpoint sets for naval cells / ship launchers (not aircraft pylons).
        /// Name-based only �?rearmShip heuristics false-positive on dual-role air munitions.
        /// </summary>
        internal static bool IsNavalHardpoint(HardpointSet hs)
        {
            if (hs == null)
                return false;
            return LoadoutMountGateService.IsNavalHardpointName(hs.name);
        }

        internal static void RemoveIalFromList(List<WeaponMount> list)
        {
            if (list == null || list.Count == 0)
                return;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (IsIalMount(list[i]))
                    list.RemoveAt(i);
            }
        }

        /// <summary>Strip nuclear + IAL mounts so AI loadout picks conventional.</summary>
        internal static void RemoveNuclearFromList(List<WeaponMount> list)
        {
            if (list == null || list.Count == 0)
                return;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                WeaponMount m = list[i];
                if (m == null)
                    continue;
                if (IsIalMount(m) || IsIalNukeCloneInfo(m.info) || (m.info != null && m.info.nuclear))
                    list.RemoveAt(i);
            }
        }

        /// <summary>
        /// When AI rolled nuke preference but faction warhead stock is empty: keep IAL
        /// (no stockpile cost) and strip only stockpile vanilla nukes.
        /// </summary>
        internal static void RemoveStockpileNukesKeepIal(List<WeaponMount> list)
        {
            if (list == null || list.Count == 0)
                return;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                WeaponMount m = list[i];
                if (m == null)
                {
                    list.RemoveAt(i);
                    continue;
                }
                if (IsIalExemptFromWarheadQuota(m))
                    continue;
                if (m.info != null && m.info.nuclear)
                    list.RemoveAt(i);
            }
        }

        internal static bool RollAiNukePreference()
        {
            float chance = AiNukeChance != null ? AiNukeChance.Value : 0.15f;
            return LoadoutMountGateService.RollAiNukePreference(chance, UnityEngine.Random.value);
        }

        private static bool IsIalMissileDef(MissileDefinition def)
        {
            if (def == null)
                return false;
            string key = def.jsonKey != null ? def.jsonKey : string.Empty;
            if (IsIalKey(key))
                return true;
            // Identity is *_IAL key / asset name — never trust display [IAL] alone
            if (def.name != null && def.name.IndexOf("_IAL", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        private static bool MissileDefMatchesFilter(MissileDefinition def)
        {
            if (def == null)
                return false;
            string n = ((def.unitName != null ? def.unitName : string.Empty) + " "
                + (def.code != null ? def.code : string.Empty) + " "
                + (def.jsonKey != null ? def.jsonKey : string.Empty) + " " + def.name);
            if (IsGunShellName(n))
                return false;
            return NameMatchesWhitelist(n);
        }

        private static void LabelInfo(WeaponInfo info, bool nuke, bool mm)
        {
            if (info == null)
                return;
            string name = info.weaponName;
            string shortN = info.shortName;
            string desc = info.description;
            bool ial = nuke || ShouldCarryIalLabel(info);
            ApplyNameTags(ref name, ref shortN, ref desc, nuke, ial);
            info.weaponName = name;
            info.shortName = shortN;
            info.description = desc;
        }

        internal static void LabelMount(WeaponMount mount, bool nuke, bool mm)
        {
            if (mount == null)
                return;
            if (AgmTWeapon.IsAgmTMount(mount))
                return;
            if (Aam2CvWeapon.IsAam2CvMount(mount))
                return;
            // Kh85MT owns TGM-85 names ([IAL]; C = [10kt])
            if (IsKh85Mount(mount))
                return;
            // Stock nuclear: leave name + yield untouched (no [IAL] / [10kt] branding)
            if (mount.info != null && ShouldLeaveStockNuclearAlone(mount.info))
                return;

            if (mount.info != null)
                ApplyWeaponInfoFlags(mount.info, IsIalNukeCloneInfo(mount.info));

            int id = mount.GetInstanceID();
            if (TouchedMounts.Contains(id))
                return;
            TouchedMounts.Add(id);

            string n = mount.mountName;
            StripTag(ref n, "[MM]");
            StripTag(ref n, "[20kt]");
            if (MmNameTag != null)
                StripTag(ref n, MmNameTag.Value);
            bool ial = nuke || (mount.info != null && ShouldCarryIalLabel(mount.info));
            if (ial && IalLabelNames.Value)
                AppendTag(ref n, IalNameTag.Value);
            else
            {
                StripTag(ref n, "[IAL]");
                if (IalNameTag != null)
                    StripTag(ref n, IalNameTag.Value);
            }
            if (nuke && NukeLabelNames.Value)
                AppendTag(ref n, NukeNameTag.Value);
            else
            {
                StripTag(ref n, "[10kt]");
                if (NukeNameTag != null)
                    StripTag(ref n, NukeNameTag.Value);
            }
            mount.mountName = n;
        }

        internal static bool IsEncyclopediaPopulated(Encyclopedia enc)
        {
            return enc != null
                && enc.missiles != null && enc.missiles.Count > 0
                && enc.weaponMounts != null && enc.weaponMounts.Count > 0;
        }

        internal static void InvalidateEncyclopediaCache()
        {
            CachedEncyclopedia = null;
            EncyclopediaDataReady = false;
        }

        internal static Encyclopedia GetEncyclopedia()
        {
            // Never pin an empty/pre-load Encyclopedia �?that broke AGM-T registration
            if (CachedEncyclopedia != null && IsEncyclopediaPopulated(CachedEncyclopedia))
                return CachedEncyclopedia;
            CachedEncyclopedia = null;

            try
            {
                PropertyInfo p = AccessTools.Property(typeof(Encyclopedia), "i");
                if (p != null)
                {
                    Encyclopedia viaProp = p.GetValue(null, null) as Encyclopedia;
                    if (IsEncyclopediaPopulated(viaProp))
                    {
                        CachedEncyclopedia = viaProp;
                        return viaProp;
                    }
                    if (viaProp != null)
                        return viaProp;
                }
            }
            catch { }

            Encyclopedia[] all = Resources.FindObjectsOfTypeAll<Encyclopedia>();
            if (all == null || all.Length == 0)
                return null;
            for (int i = 0; i < all.Length; i++)
            {
                if (IsEncyclopediaPopulated(all[i]))
                {
                    CachedEncyclopedia = all[i];
                    return all[i];
                }
            }
            return all[0];
        }

        /// <summary>
        /// Clone each vanilla missile WeaponMount into a nuclear IAL sibling.
        /// Vanilla mounts are left untouched.
        /// </summary>
        internal static void EnsureNukeMountClones()
        {
            if (!EnableNukeVariants.Value || NukeMountsInjected)
                return;

            WeaponMount[] all = Resources.FindObjectsOfTypeAll<WeaponMount>();
            if (all == null || all.Length == 0)
                return;

            // One-time purge of IAL twins for already-nuclear vanilla weapons
            PurgeRedundantNuclearIalClones();

            Encyclopedia enc = GetEncyclopedia();
            int added = 0;

            for (int i = 0; i < all.Length; i++)
            {
                WeaponMount src = all[i];
                if (src == null || src.info == null || !src.info.missile)
                    continue;
                if (!IsMissileInfoAllowed(src.info))
                    continue;
                // Ship-only SAMs keep [IAL] names but never get a [10kt] twin
                if (ShipMissileService.IsNavalExclusiveMount(src))
                    continue;
                if (IsIalKey(src.jsonKey) || IsNukeVariantInfo(src.info))
                    continue;
                // Already nuclear in vanilla (Genie / 20kt cruise / tacNuke / …) — no IAL twin
                if (IsVanillaNuclearWeaponInfo(src.info))
                    continue;
                if (AgmTWeapon.IsAgmTMount(src) || AgmTWeapon.IsAgmTKey(src.jsonKey))
                    continue;
                if (Aam2CvWeapon.IsAam2CvMount(src) || Aam2CvWeapon.IsAam2CvKey(src.jsonKey))
                    continue;
                // Kh85MT owns TGM-85C nuclear racks — no WeXon *_IAL twin
                if (IsKh85Mount(src))
                    continue;
                // Eyeball / AGM_scanner: recon pod — never IAL [10kt] nuke twin
                if (IsScannerReconInfo(src.info)
                    || MissileClassifyGateService.IsScannerReconName(src.jsonKey)
                    || MissileClassifyGateService.IsScannerReconName(src.name)
                    || MissileClassifyGateService.IsScannerReconName(src.mountName))
                    continue;
                // Only dual-mode guided ([IAL]-eligible) get a nuclear twin
                if (!ShouldCarryIalLabel(src.info))
                    continue;
                if (src.name != null && src.name.IndexOf("_IAL", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                string baseKey = !string.IsNullOrEmpty(src.jsonKey) ? src.jsonKey
                    : (!string.IsNullOrEmpty(src.name) ? src.name : src.info.name);
                if (string.IsNullOrEmpty(baseKey))
                    continue;
                string ialKey = baseKey + "_IAL";
                if (CreatedNukeKeys.Contains(ialKey))
                    continue;

                WeaponMount clone = UnityEngine.Object.Instantiate(src);
                clone.name = (src.name != null ? src.name : "Mount") + "_IAL";
                clone.jsonKey = ialKey;
                clone.hideFlags = HideFlags.DontUnloadUnusedAsset;

                WeaponInfo infoClone = UnityEngine.Object.Instantiate(src.info);
                infoClone.name = (src.info.name != null ? src.info.name : "WeaponInfo") + "_IAL";
                infoClone.hideFlags = HideFlags.DontUnloadUnusedAsset;
                if (SetNuclearFlag.Value)
                    infoClone.nuclear = true;
                if (SetStrategicFlag.Value)
                    infoClone.strategic = true;
                // IAL clones are aircraft-loadout only �?ships keep conventional rearmShip mounts
                infoClone.rearmShip = false;
                infoClone.blastDamage = GetIalNukeBlastYield();
                LabelInfo(infoClone, true, false);

                clone.info = infoClone;
                string mn = src.mountName != null && src.mountName.Length > 0 ? src.mountName : clone.name;
                StripTag(ref mn, "[MM]");
                StripTag(ref mn, "[20kt]");
                StripTag(ref mn, "[10kt]");
                // Display: … [IAL] [10kt] (10kt yield branding on IAL twins only)
                AppendTag(ref mn, IalNameTag.Value);
                AppendTag(ref mn, NukeNameTag.Value);
                clone.mountName = mn;

                NukeInfoIds.Add(infoClone.GetInstanceID());
                CreatedNukeKeys.Add(ialKey);
                NukeMountClones.Add(clone);
                if (CachedMountSet.Add(clone))
                    CachedMounts.Add(clone);

                if (enc != null && enc.weaponMounts != null && !enc.weaponMounts.Contains(clone))
                    enc.weaponMounts.Add(clone);
                if (Encyclopedia.WeaponLookup != null && !Encyclopedia.WeaponLookup.ContainsKey(ialKey))
                    Encyclopedia.WeaponLookup[ialKey] = clone;

                // Network / loadout resolution uses LookupIndex on INetworkDefinition
                try
                {
                    if (enc != null && enc.IndexLookup != null && !enc.IndexLookup.Contains(clone))
                    {
                        enc.IndexLookup.Add(clone);
                        INetworkDefinition nd = clone;
                        nd.LookupIndex = enc.IndexLookup.Count - 1;
                    }
                }
                catch (Exception ex)
                {
                    if (DebugLog.Value)
                        Log.LogWarning("IndexLookup register: " + ex.Message);
                }

                added++;
            }

            NukeMountsInjected = true;
            Log.LogInfo("Nuke mount clones: added " + added + " (vanilla mounts unchanged)");
        }

        /// <summary>
        /// Drop mistaken IAL [10kt] twins of Eyeball / AGM_scanner (recon pods, not warheads).
        /// </summary>
        internal static void PurgeScannerReconIalClones()
        {
            Encyclopedia enc = GetEncyclopedia();
            int removed = 0;

            for (int i = NukeMountClones.Count - 1; i >= 0; i--)
            {
                WeaponMount m = NukeMountClones[i];
                if (m == null)
                    continue;
                string baseKey = StripIalKeySuffix(m.jsonKey);
                bool hit = MissileClassifyGateService.IsScannerReconName(baseKey)
                    || MissileClassifyGateService.IsScannerReconName(m.jsonKey)
                    || MissileClassifyGateService.IsScannerReconName(m.name)
                    || MissileClassifyGateService.IsScannerReconName(m.mountName)
                    || IsScannerReconInfo(m.info);
                if (!hit)
                    continue;
                if (enc != null && enc.weaponMounts != null)
                    enc.weaponMounts.Remove(m);
                if (Encyclopedia.WeaponLookup != null && !string.IsNullOrEmpty(m.jsonKey))
                    Encyclopedia.WeaponLookup.Remove(m.jsonKey);
                if (m.info != null)
                    NukeInfoIds.Remove(m.info.GetInstanceID());
                if (!string.IsNullOrEmpty(m.jsonKey))
                    CreatedNukeKeys.Remove(m.jsonKey);
                CachedMountSet.Remove(m);
                CachedMounts.Remove(m);
                NukeMountClones.RemoveAt(i);
                try { UnityEngine.Object.Destroy(m); }
                catch { }
                removed++;
            }

            if (enc != null && enc.missiles != null)
            {
                for (int i = enc.missiles.Count - 1; i >= 0; i--)
                {
                    MissileDefinition d = enc.missiles[i];
                    if (d == null || !IsIalMissileDef(d))
                        continue;
                    string baseKey = StripIalKeySuffix(d.jsonKey);
                    if (!MissileClassifyGateService.IsScannerReconName(baseKey)
                        && !MissileClassifyGateService.IsScannerReconName(d.unitName)
                        && !MissileClassifyGateService.IsScannerReconName(d.name))
                        continue;
                    enc.missiles.RemoveAt(i);
                    if (Encyclopedia.Lookup != null && !string.IsNullOrEmpty(d.jsonKey))
                        Encyclopedia.Lookup.Remove(d.jsonKey);
                    removed++;
                }
            }

            if (removed > 0)
                Log.LogInfo("Purged " + removed + " IAL clones of scanner/recon weapons (Eyeball)");
        }

        internal static void EnsureIalEncyclopedia()
        {
            if (!EnableEncyclopediaPages.Value || EncyclopediaInjected)
                return;

            Encyclopedia enc = GetEncyclopedia();
            if (enc == null || enc.missiles == null)
                return;

            EnsureNukeMountClones();

            HashSet<string> existingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < enc.missiles.Count; i++)
            {
                MissileDefinition d = enc.missiles[i];
                if (d != null && d.jsonKey != null)
                    existingKeys.Add(d.jsonKey);
            }

            int added = 0;
            List<MissileDefinition> snapshot = new List<MissileDefinition>(enc.missiles);
            for (int i = 0; i < snapshot.Count; i++)
            {
                MissileDefinition src = snapshot[i];
                if (src == null || IsIalMissileDef(src) || !MissileDefMatchesFilter(src))
                    continue;
                if (MissileClassifyGateService.LooksLikeNavalExclusiveName(src.jsonKey)
                    || MissileClassifyGateService.LooksLikeNavalExclusiveName(src.unitName)
                    || MissileClassifyGateService.LooksLikeNavalExclusiveName(src.name))
                    continue;
                if (AgmTWeapon.IsAgmTKey(src.jsonKey))
                    continue;
                if (Aam2CvWeapon.IsAam2CvKey(src.jsonKey))
                    continue;
                if (IsVanillaNuclearMissileDef(src))
                    continue;
                if (MissileClassifyGateService.IsScannerReconName(src.jsonKey)
                    || MissileClassifyGateService.IsScannerReconName(src.unitName)
                    || MissileClassifyGateService.IsScannerReconName(src.name))
                    continue;

                string baseKey = src.jsonKey != null && src.jsonKey.Length > 0 ? src.jsonKey : src.name;
                if (baseKey == null || baseKey.Length == 0)
                    continue;
                string ialKey = baseKey + "_IAL";
                if (existingKeys.Contains(ialKey))
                    continue;

                MissileDefinition clone = UnityEngine.Object.Instantiate(src);
                clone.name = (src.name != null ? src.name : "Missile") + "_IAL";
                clone.jsonKey = ialKey;
                clone.code = (src.code != null && src.code.Length > 0 ? src.code : baseKey) + "-IAL";
                string baseName = src.unitName != null && src.unitName.Length > 0 ? src.unitName : clone.name;
                clone.unitName = baseName;
                StripTag(ref clone.unitName, "[MM]");
                AppendTag(ref clone.unitName, NukeNameTag.Value);
                AppendTag(ref clone.unitName, IalNameTag.Value);
                string desc = src.description != null ? src.description : string.Empty;
                EnsureNukeDescriptionLine(ref desc);
                clone.description = desc;
                clone.dontAutomaticallyAddToEncyclopedia = false;

                enc.missiles.Add(clone);
                existingKeys.Add(ialKey);
                added++;

                if (Encyclopedia.Lookup != null && !Encyclopedia.Lookup.ContainsKey(ialKey))
                    Encyclopedia.Lookup[ialKey] = clone;
            }

            EncyclopediaInjected = true;
            BrandEncyclopediaEntries(enc);
            if (added > 0)
                Log.LogInfo(PackName + " encyclopedia: added " + added + " IAL missile pages");
        }

        /// <summary>Re-brand all IAL / nuke encyclopedia missiles + mounts as WeXon.</summary>
        internal static void BrandEncyclopediaEntries(Encyclopedia enc)
        {
            if (enc == null)
                return;

            if (enc.missiles != null)
            {
                for (int i = 0; i < enc.missiles.Count; i++)
                {
                    MissileDefinition d = enc.missiles[i];
                    if (d == null || !IsIalMissileDef(d))
                        continue;
                    string name = d.unitName != null ? d.unitName : string.Empty;
                    StripTag(ref name, "[MM]");
                    if (NukeLabelNames.Value)
                        AppendTag(ref name, NukeNameTag.Value);
                    if (IalLabelNames.Value)
                        AppendTag(ref name, IalNameTag.Value);
                    d.unitName = name;
                    string desc = d.description != null ? d.description : string.Empty;
                    EnsureNukeDescriptionLine(ref desc);
                    d.description = desc;
                    if (Encyclopedia.Lookup != null && !string.IsNullOrEmpty(d.jsonKey))
                        Encyclopedia.Lookup[d.jsonKey] = d;
                }
            }

            if (enc.weaponMounts != null)
            {
                for (int i = 0; i < enc.weaponMounts.Count; i++)
                {
                    WeaponMount m = enc.weaponMounts[i];
                    if (m == null || !IsIalMount(m))
                        continue;
                    LabelMount(m, true, false);
                    if (m.info != null)
                    {
                        string desc = m.info.description != null ? m.info.description : string.Empty;
                        EnsureNukeDescriptionLine(ref desc);
                        m.info.description = desc;
                    }
                    if (Encyclopedia.WeaponLookup != null && !string.IsNullOrEmpty(m.jsonKey))
                        Encyclopedia.WeaponLookup[m.jsonKey] = m;
                }
            }

            LabelMissileDefinitionNames();
        }

        /// <summary>
        /// In-flight / encyclopedia names use MissileDefinition.unitName.
        /// Keep [IAL] on dual-mode guided and naval SAMs (no [10kt] here).
        /// </summary>
        internal static void LabelMissileDefinitionNames()
        {
            if (IalLabelNames == null || !IalLabelNames.Value)
                return;
            MissileDefinition[] defs = Resources.FindObjectsOfTypeAll<MissileDefinition>();
            if (defs == null)
                return;
            for (int i = 0; i < defs.Length; i++)
            {
                MissileDefinition d = defs[i];
                if (d == null)
                    continue;
                if (IsIalMissileDef(d) || IsVanillaNuclearMissileDef(d))
                    continue;
                if (AgmTWeapon.IsAgmTKey(d.jsonKey))
                    continue;
                if (Aam2CvWeapon.IsAam2CvKey(d.jsonKey))
                    continue;
                string blob = ((d.unitName != null ? d.unitName : string.Empty) + " "
                    + (d.code != null ? d.code : string.Empty) + " "
                    + (d.jsonKey != null ? d.jsonKey : string.Empty) + " "
                    + (d.name != null ? d.name : string.Empty));
                if (IsGunShellName(blob)
                    || IsRocketOrUnguidedName(blob)
                    || IsBallisticMissileName(blob)
                    || IsCruiseMissileName(blob)
                    || MissileClassifyGateService.IsScannerReconName(blob))
                    continue;
                bool naval = MissileClassifyGateService.LooksLikeNavalExclusiveName(blob);
                if (!naval && !MissileDefMatchesFilter(d))
                    continue;
                string name = d.unitName != null ? d.unitName : string.Empty;
                StripTag(ref name, "[MM]");
                StripTag(ref name, "[10kt]");
                StripTag(ref name, "[20kt]");
                AppendTag(ref name, IalNameTag.Value);
                d.unitName = name;
            }
        }

        internal static void RefreshEncyclopediaData()
        {
            // Always re-bind AGM-T (cheap) �?early inject can miss AfterLoad
            AgmTWeapon.Ensure();
            Aam2CvWeapon.Ensure();

            // Hangar / encyclopedia UI used to re-run full inject+brand every open �?huge hitch
            if (EncyclopediaDataReady)
                return;

            EnsureNukeMountClones();
            EnsureIalEncyclopedia();
            BrandEncyclopediaEntries(GetEncyclopedia());

            bool nukesOk = !EnableNukeVariants.Value || NukeMountsInjected;
            bool agmOk = EnableAgmT == null || !EnableAgmT.Value || AgmTWeapon.IsInjected;
            bool encOk = !EnableEncyclopediaPages.Value || EncyclopediaInjected
                || IsEncyclopediaPopulated(GetEncyclopedia());
            if (nukesOk && agmOk && encOk)
                EncyclopediaDataReady = true;
        }

        /// <summary>
        /// Labels + flags. [IAL] only on dual-mode guided; [10kt] only on *_IAL nuke clones.
        /// </summary>
        internal static void ApplyWeaponInfoFlags(WeaponInfo info, bool forceNuke)
        {
            if (info == null)
                return;
            if (!IsMissileInfoAllowed(info) && !ShipMissileService.IsNavalExclusiveWeaponInfo(info))
                return;
            // ACM-119 / ACNM-118: own names (ACNM = 1.5kt, never [10kt])
            if (AgmTWeapon.IsAgmTInfo(info))
                return;
            if (Aam2CvWeapon.IsAam2CvInfo(info))
                return;
            // Kh85MT owns TGM-85 names (C = [IAL] [10kt] on every ammo rack)
            if (IsKh85WeaponInfo(info))
                return;
            // Stock nuclear: do not change yield or add yield tags
            if (ShouldLeaveStockNuclearAlone(info))
                return;

            bool nukeClone = forceNuke || IsIalNukeCloneInfo(info) || NukeInfoIds.Contains(info.GetInstanceID());
            int id = info.GetInstanceID();
            if (!TouchedInfos.Add(id))
                return; // already labeled

            // Only IAL *_IAL twins: nuclear + 10kt yield + [10kt] tag
            if (nukeClone)
            {
                if (SetNuclearFlag.Value)
                    info.nuclear = true;
                if (SetStrategicFlag.Value)
                    info.strategic = true;
                info.blastDamage = GetIalNukeBlastYield();
            }

            // LabelInfo applies or strips [IAL] via ShouldCarryIalLabel
            LabelInfo(info, nukeClone, EnableMultiMode.Value);

            if (DebugLog.Value)
                Log.LogInfo("Labeled WeaponInfo: " + info.weaponName + " nukeClone=" + nukeClone
                    + " ial=" + ShouldCarryIalLabel(info));
        }

        private static void ScanLoadedWeaponAssets()
        {
            if (EnableNukeVariants.Value)
                ResolveNukeFx(false);
            EnsureNukeMountClones();
            PurgeScannerReconIalClones();
            AgmTWeapon.Ensure();
            Aam2CvWeapon.Ensure();
            // Strip false [10kt]/nuclear from vanilla; re-apply cosmetic [IAL]
            SanitizeFalseIalLabels();
            // Guarantee *_IAL twins keep nuclear + [10kt] after sanitize/cosmetic passes
            RepairIalNukeCloneLabels();
            RefreshMountCache();

            int n = 0;
            WeaponInfo[] infos = Resources.FindObjectsOfTypeAll<WeaponInfo>();
            for (int i = 0; i < infos.Length; i++)
            {
                WeaponInfo info = infos[i];
                if (info == null)
                    continue;
                if (!IsMissileInfoAllowed(info) && !ShipMissileService.IsNavalExclusiveWeaponInfo(info))
                    continue;
                ApplyWeaponInfoFlags(info, IsIalNukeCloneInfo(info));
                n++;
            }

            WeaponMount[] mounts = Resources.FindObjectsOfTypeAll<WeaponMount>();
            for (int i = 0; i < mounts.Length; i++)
            {
                WeaponMount m = mounts[i];
                if (m == null || m.info == null)
                    continue;
                if (!IsMissileInfoAllowed(m.info) && !ShipMissileService.IsNavalExclusiveMount(m))
                    continue;
                bool nukeClone = IsIalMount(m) || IsIalNukeCloneInfo(m.info);
                LabelMount(m, nukeClone, EnableMultiMode.Value);
            }

            LabelMissileDefinitionNames();

            Log.LogInfo("Scan: infos=" + n + " mounts=" + CachedMounts.Count
                + " nukeClones=" + NukeMountClones.Count
                + " fx=" + (CachedNukeFx != null ? CachedNukeFx.name : "PENDING")
                + " ([IAL]=dual-mode guided; identity via _IAL key)");
        }

        internal static GameObject ResolveNukeFx(bool logFailure)
        {
            if (CachedNukeFx != null)
                return CachedNukeFx;

            string want = ExplosionFxName.Value != null ? ExplosionFxName.Value.Trim() : "explosion_20kt";
            if (want.Length == 0)
                want = "explosion_20kt";

            GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int i = 0; i < all.Length; i++)
            {
                GameObject go = all[i];
                if (go == null)
                    continue;
                if (go.name == want || go.name == want + "(Clone)")
                {
                    if (CachedNukeFx == null || !go.scene.IsValid())
                        CachedNukeFx = go;
                    if (!go.scene.IsValid())
                        break;
                }
            }

            if (CachedNukeFx == null && WarheadField != null && WhAir != null && BlastYieldField != null)
            {
                Missile[] missiles = Resources.FindObjectsOfTypeAll<Missile>();
                for (int i = 0; i < missiles.Length; i++)
                {
                    Missile m = missiles[i];
                    if (m == null)
                        continue;
                    float y;
                    try { y = (float)BlastYieldField.GetValue(m); }
                    catch { continue; }
                    if (y < 1000000f)
                        continue;
                    object wh = WarheadField.GetValue(m);
                    if (wh == null)
                        continue;
                    GameObject fx = WhAir.GetValue(wh) as GameObject;
                    if (fx != null)
                    {
                        CachedNukeFx = fx;
                        break;
                    }
                }
            }

            if (CachedNukeFx != null)
                Log.LogInfo("Nuke FX: " + CachedNukeFx.name);
            else if (logFailure)
                Log.LogWarning("Nuke FX '" + want + "' not found yet.");
            return CachedNukeFx;
        }

        private static void ApplyWarheadFx(Missile missile)
        {
            ApplyWarheadFx(missile, true);
        }

        /// <param name="armWarhead">
        /// When false, swap nuke FX but leave Warhead.Armed=false (ACNM-118 safe arm delay).
        /// </param>
        private static void ApplyWarheadFx(Missile missile, bool armWarhead)
        {
            if (!SwapExplosionFx.Value || WarheadField == null)
                return;
            GameObject fx = ResolveNukeFx(true);
            if (fx == null || missile == null)
                return;

            object wh = WarheadField.GetValue(missile);
            if (wh == null)
                return;

            if (armWarhead && WhArmed != null)
                WhArmed.SetValue(wh, true);
            if (WhAir != null)
                WhAir.SetValue(wh, fx);
            if (WhArmor != null)
                WhArmor.SetValue(wh, fx);
            if (WhTerrain != null)
                WhTerrain.SetValue(wh, fx);
            if (WhUnder != null)
                WhUnder.SetValue(wh, fx);
            if (WhWater != null && WhWater.GetValue(wh) == null)
                WhWater.SetValue(wh, fx);
            WarheadField.SetValue(missile, wh);
        }

        /// <summary>
        /// Hangar selects IAL WeaponMount, but spawned prefab keeps vanilla Weapon.info.
        /// Sync info from mount so HUD + RailLaunch (info.weaponPrefab) + nuclear flag stick.
        /// Never touch Guns �?VL-49 76mm mount.info is shared with the shell and must not
        /// rewrite gun.muzzleVelocity / station WeaponInfo during RegisterWeapon.
        /// </summary>
        internal static void SyncWeaponInfoFromMount(Weapon weapon, WeaponMount mount)
        {
            if (weapon == null || mount == null || mount.info == null)
                return;
            if (weapon is Gun)
                return;
            if (weapon.info != null && weapon.info.gun)
                return;
            AgmTWeapon.SyncFromMount(weapon, mount);
            Aam2CvWeapon.SyncFromMount(weapon, mount);
            // Only the IAL nuke-clone path needs this sync
            if (!IsIalKey(mount.jsonKey) && !IsIalNukeCloneInfo(mount.info))
                return;
            weapon.info = mount.info;
            ApplyWeaponInfoFlags(mount.info, true);
            if (DebugLog.Value)
                Log.LogInfo("Synced nuke WeaponInfo onto " + weapon.name + " mount=" + mount.mountName);
        }

        internal static bool IsNukeMount(WeaponMount mount)
        {
            if (mount == null)
                return false;
            if (IsIalKey(mount.jsonKey))
                return true;
            if (IsTgm85CNuclearMount(mount))
                return true;
            return IsIalNukeCloneInfo(mount.info);
        }

        /// <summary>IAL / [10kt] clones ignore faction airbase warhead stockpile limits.</summary>
        internal static bool IsIalExemptFromWarheadQuota(WeaponInfo info)
        {
            if (AgmTWeapon.IsAgmTInfo(info))
                return true;
            if (Aam2CvWeapon.IsAam2CvInfo(info))
                return true;
            if (IsTgm85CNuclearInfo(info))
                return true;
            return IsIalNukeCloneInfo(info);
        }

        internal static bool IsIalExemptFromWarheadQuota(WeaponMount mount)
        {
            if (mount == null)
                return false;
            if (IsIalKey(mount.jsonKey))
                return true;
            // ACNM-118 uses its own 1.5kt bus — never consume faction warhead stockpile
            if (AgmTWeapon.IsAgmTMount(mount))
                return true;
            if (Aam2CvWeapon.IsAam2CvMount(mount))
                return true;
            // TGM-85C (all ammo racks) — 10kt nuclear fit, no stockpile cost
            if (IsTgm85CNuclearMount(mount))
                return true;
            return IsIalNukeCloneInfo(mount.info);
        }

        /// <summary>Rebuild NukeInfoIds from clone mounts only (drops vanilla infos polluted by old ApplyNuke).</summary>
        internal static void RebuildNukeInfoIdsFromClones()
        {
            NukeInfoIds.Clear();
            for (int i = 0; i < NukeMountClones.Count; i++)
            {
                WeaponMount m = NukeMountClones[i];
                if (m != null && m.info != null)
                    NukeInfoIds.Add(m.info.GetInstanceID());
            }
        }

        /// <summary>
        /// Strip false [10kt]/[IAL] from ineligible mounts; re-apply [IAL] only on dual-mode guided.
        /// Real IAL nuke clones (*_IAL / NukeInfoIds) are left alone.
        /// Stock nuclear (Genie / 20kt / tacNuke): completely untouched (yield + tags).
        /// ACM-119 / ACNM-118: handled by AgmTWeapon (1.5kt, no [10kt]).
        /// </summary>
        internal static void SanitizeFalseIalLabels()
        {
            RebuildNukeInfoIdsFromClones();
            float ialYield = GetIalNukeBlastYield();

            WeaponInfo[] infos = Resources.FindObjectsOfTypeAll<WeaponInfo>();
            for (int i = 0; i < infos.Length; i++)
            {
                WeaponInfo info = infos[i];
                if (info == null || IsIalNukeCloneInfo(info))
                    continue;
                if (AgmTWeapon.IsAgmTInfo(info))
                    continue;
                if (Aam2CvWeapon.IsAam2CvInfo(info))
                    continue;
                // Kh85MT owns TGM-85 [IAL] / TGM-85C [10kt] branding
                if (IsKh85WeaponInfo(info))
                    continue;
                // Never rewrite stock nuclear yield / names / nuclear flag
                if (ShouldLeaveStockNuclearAlone(info))
                    continue;
                if (!info.missile)
                    continue;
                string name = info.weaponName;
                string shortN = info.shortName;
                string desc = info.description;
                bool hadYield = (!string.IsNullOrEmpty(name) && (name.IndexOf("[10kt]", StringComparison.OrdinalIgnoreCase) >= 0
                        || name.IndexOf("[20kt]", StringComparison.OrdinalIgnoreCase) >= 0))
                    || (!string.IsNullOrEmpty(shortN) && (shortN.IndexOf("[10kt]", StringComparison.OrdinalIgnoreCase) >= 0
                        || shortN.IndexOf("[20kt]", StringComparison.OrdinalIgnoreCase) >= 0));
                bool hasIalCosmetic = (!string.IsNullOrEmpty(name) && name.IndexOf("[IAL]", StringComparison.OrdinalIgnoreCase) >= 0)
                    || (!string.IsNullOrEmpty(shortN) && shortN.IndexOf("[IAL]", StringComparison.OrdinalIgnoreCase) >= 0);
                bool yieldLooksIal = info.blastDamage > 0f
                    && Mathf.Abs(info.blastDamage - ialYield) < 1f;
                StripTag(ref name, "[10kt]");
                StripTag(ref shortN, "[10kt]");
                StripTag(ref name, "[20kt]");
                StripTag(ref shortN, "[20kt]");
                StripTag(ref name, "[MM]");
                StripTag(ref shortN, "[MM]");
                bool ial = ShouldCarryIalLabel(info);
                if (ial && IalLabelNames.Value)
                {
                    AppendTag(ref name, IalNameTag.Value);
                    AppendTag(ref shortN, IalNameTag.Value);
                }
                else
                {
                    StripTag(ref name, "[IAL]");
                    StripTag(ref shortN, "[IAL]");
                    if (IalNameTag != null)
                    {
                        StripTag(ref name, IalNameTag.Value);
                        StripTag(ref shortN, IalNameTag.Value);
                    }
                }
                info.weaponName = name;
                info.shortName = shortN;
                // Strip WeXon pollution on conventional missiles only (stock nukes skipped above)
                if (info.nuclear && (hadYield || hasIalCosmetic || yieldLooksIal))
                    info.nuclear = false;
                // Drop stolen nuke blurb from conventional / recon infos (Eyeball pollution).
                if (!string.IsNullOrEmpty(desc)
                    && desc.IndexOf(NukeDescLine, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    desc = desc.Replace(NukeDescLine + "\n\n", string.Empty);
                    desc = desc.Replace(NukeDescLine + "\n", string.Empty);
                    desc = desc.Replace(NukeDescLine, string.Empty).Trim();
                }
                info.description = desc;
                TouchedInfos.Remove(info.GetInstanceID());
            }

            WeaponMount[] mounts = Resources.FindObjectsOfTypeAll<WeaponMount>();
            for (int i = 0; i < mounts.Length; i++)
            {
                WeaponMount m = mounts[i];
                if (m == null || IsIalMount(m))
                    continue;
                if (AgmTWeapon.IsAgmTMount(m))
                    continue;
                if (Aam2CvWeapon.IsAam2CvMount(m))
                    continue;
                if (IsKh85Mount(m))
                    continue;
                if (m.info != null && IsIalNukeCloneInfo(m.info))
                    continue;
                if (m.info != null && ShouldLeaveStockNuclearAlone(m.info))
                    continue;
                string n = m.mountName;
                StripTag(ref n, "[10kt]");
                StripTag(ref n, "[20kt]");
                StripTag(ref n, "[MM]");
                bool ial = m.info != null && ShouldCarryIalLabel(m.info);
                if (ial && IalLabelNames.Value)
                    AppendTag(ref n, IalNameTag.Value);
                else
                {
                    StripTag(ref n, "[IAL]");
                    if (IalNameTag != null)
                        StripTag(ref n, IalNameTag.Value);
                }
                m.mountName = n;
                TouchedMounts.Remove(m.GetInstanceID());
            }
        }

        /// <summary>Re-assert nuclear flags / [10kt] on every IAL nuke clone after sanitize.</summary>
        internal static void RepairIalNukeCloneLabels()
        {
            for (int i = 0; i < NukeMountClones.Count; i++)
            {
                WeaponMount m = NukeMountClones[i];
                if (m == null || m.info == null)
                    continue;
                if (SetNuclearFlag != null && SetNuclearFlag.Value)
                    m.info.nuclear = true;
                if (SetStrategicFlag != null && SetStrategicFlag.Value)
                    m.info.strategic = true;
                m.info.blastDamage = GetIalNukeBlastYield();
                NukeInfoIds.Add(m.info.GetInstanceID());
                TouchedInfos.Remove(m.info.GetInstanceID());
                TouchedMounts.Remove(m.GetInstanceID());
                LabelInfo(m.info, true, false);
                LabelMount(m, true, false);
            }
        }

        internal static WeaponMount GetWeaponMount(Weapon weapon)
        {
            if (weapon == null || WeaponMountField == null)
                return null;
            try { return WeaponMountField.GetValue(weapon) as WeaponMount; }
            catch { return null; }
        }

        internal static void NoteNukeFire(Weapon weapon)
        {
            if (!EnableNukeVariants.Value || weapon == null)
                return;
            WeaponMount mount = GetWeaponMount(weapon);
            // IAL *_IAL clones or TGM-85C nuclear racks — never bare [IAL] cosmetic
            if (!IsNukeMount(mount) && !IsIalNukeCloneInfo(weapon.info)
                && !IsTgm85CNuclearInfo(weapon.info) && !IsTgm85CNuclearMount(mount))
                return;
            PendingNukeSpawns++;
            PendingNukeWeapon = weapon;
            PendingNukeOwner = weapon.attachedUnit;
            PendingNukeTime = Time.time;
            if (DebugLog.Value)
                Log.LogInfo("Pending nuke spawn #" + PendingNukeSpawns + " from " + weapon.name);
        }

        /// <summary>IAL / [10kt] warhead yield; optionally half for reduced shockwave range.</summary>
        internal static float GetIalNukeBlastYield()
        {
            float y = BlastYield != null ? BlastYield.Value : 10000000f;
            if (IalHalfBlastRange == null || IalHalfBlastRange.Value)
                return y * 0.5f;
            return y;
        }

        /// <summary>Re-apply blastDamage on IAL nuke mount infos after GUI toggle.</summary>
        internal static void RefreshIalBlastYield()
        {
            float y = GetIalNukeBlastYield();
            for (int i = 0; i < NukeMountClones.Count; i++)
            {
                WeaponMount m = NukeMountClones[i];
                if (m != null && m.info != null)
                    m.info.blastDamage = y;
            }
        }

        internal static void ApplyNuke(Missile missile, string reason)
        {
            if (!EnableNukeVariants.Value || missile == null)
                return;
            // Cheap early-out: already touched, or never a nuke variant
            if (TouchedMissiles.Contains(missile.GetInstanceID()))
                return;
            if (!IsMissileAllowed(missile) || IsGunShellMissile(missile))
                return;
            // AGM-T bus / GS25 / AAM-2CV use their own warhead path — never IAL ApplyNuke
            if (AgmTWeapon.ShouldBlockIalPendingNuke(missile) || Aam2CvWeapon.IsAam2CvMissile(missile))
                return;
            // WeXon IAL *_IAL clones + TGM-85C 10kt racks (ACNM-118 uses ApplyAgmTNukeWarhead)
            WeaponInfo info = InfoField != null ? InfoField.GetValue(missile) as WeaponInfo : null;
            if (!IsIalNukeCloneInfo(info) && !IsTgm85CNuclearInfo(info) && !IsTgm85CNuclearMissile(missile))
                return;
            ApplyNukeForced(missile, reason);
        }

        internal static MultiModeBrain FindBrain(Missile missile)
        {
            return MultiModeBrain.Find(missile);
        }

        /// <summary>ACNM-118 effective blast yield after NukeBlastScale (shockwave radius follows yield).</summary>
        internal static float GetAgmTNukeBlastYield()
        {
            float y = AgmTNukeYield != null ? AgmTNukeYield.Value : AgmTWeapon.NukeYield15kt;
            float scale = AgmTNukeBlastScale != null ? AgmTNukeBlastScale.Value : 0.45f;
            if (scale < 0.05f)
                scale = 0.05f;
            if (scale > 1f)
                scale = 1f;
            return y * scale;
        }

        /// <summary>AGM-T [1.5kt] bus / GS25 — instance yield + FX only (never mutate shared GS25 WeaponInfo).</summary>
        internal static void ApplyAgmTNukeWarhead(Missile missile)
        {
            if (missile == null)
                return;
            try
            {
                float y = GetAgmTNukeBlastYield();
                if (BlastYieldField != null)
                    BlastYieldField.SetValue(missile, y);
                if (PierceDamage != null && PierceDamage.Value >= 0f && PierceField != null)
                    PierceField.SetValue(missile, PierceDamage.Value);
                // FX only — keep unarmed until GS25/bus arm delay (impact fuse needs a later Arm).
                ApplyWarheadFx(missile, false);
                object wh0 = WarheadField != null ? WarheadField.GetValue(missile) : null;
                if (wh0 != null && WhArmed != null)
                    WhArmed.SetValue(wh0, false);
                ApplyAgmTDragReduction(missile);
                if (DebugLog != null && DebugLog.Value)
                    Log.LogInfo("AGM-T nuke warhead on " + missile.name + " yield=" + y + " (unarmed until safe)");
            }
            catch (Exception ex)
            {
                Log.LogWarning("ApplyAgmTNukeWarhead: " + ex.Message);
            }
        }

        /// <summary>AAM-2CV [5kt] — instance yield + FX, armed immediately (AAM, not ACNM delay).</summary>
        internal static void ApplyAam2CvNukeWarhead(Missile missile)
        {
            if (missile == null)
                return;
            try
            {
                if (BlastYieldField != null)
                    BlastYieldField.SetValue(missile, Aam2CvWeapon.NukeYield5kt);
                if (PierceDamage != null && PierceDamage.Value >= 0f && PierceField != null)
                    PierceField.SetValue(missile, PierceDamage.Value);
                ApplyWarheadFx(missile, true);
                if (DebugLog != null && DebugLog.Value)
                    Log.LogInfo("AAM-2CV 5kt warhead on " + missile.name);
            }
            catch (Exception ex)
            {
                Log.LogWarning("ApplyAam2CvNukeWarhead: " + ex.Message);
            }
        }

        /// <summary>Reduce ACM/ACNM / GS25 aero drag (dragCurve + supersonicDrag).</summary>
        internal static void ApplyAgmTDragReduction(Missile missile)
        {
            if (missile == null || DragCurveField == null)
                return;
            float scale = AgmTDragScale != null ? AgmTDragScale.Value : 0.55f;
            if (scale <= 0f || scale >= 0.999f)
                return;
            int id = missile.GetInstanceID();
            if (!AgmTDragReducedIds.Add(id))
                return;
            try
            {
                AnimationCurve curve = DragCurveField.GetValue(missile) as AnimationCurve;
                if (curve != null && curve.length > 0)
                {
                    Keyframe[] keys = curve.keys;
                    for (int i = 0; i < keys.Length; i++)
                    {
                        Keyframe k = keys[i];
                        k.value *= scale;
                        k.inTangent *= scale;
                        k.outTangent *= scale;
                        keys[i] = k;
                    }
                    curve.keys = keys;
                    DragCurveField.SetValue(missile, curve);
                }
                if (SupersonicDragField != null)
                {
                    float sd = (float)SupersonicDragField.GetValue(missile);
                    SupersonicDragField.SetValue(missile, sd * scale);
                }
            }
            catch (Exception ex)
            {
                if (DebugLog != null && DebugLog.Value)
                    Log.LogWarning("ApplyAgmTDragReduction: " + ex.Message);
            }
        }

        /// <summary>GS25 fallback: conventional HE that still explodes (never silent fizzle).</summary>
        internal static void ForceAcnmConventionalBoom(Missile missile)
        {
            if (missile == null)
                return;
            try
            {
                EnsureConventionalWarhead(missile, 15f);
                object wh = WarheadField != null ? WarheadField.GetValue(missile) : null;
                if (wh != null && WhArmed != null)
                    WhArmed.SetValue(wh, true);
            }
            catch { }
        }

        /// <summary>Miss / early terrain: fizzle self-destruct — Warhead.Armed=false skips nuke FX/Shockwave.</summary>
        internal static void ForceAcnmNonNuclearDetonate(Missile missile)
        {
            if (missile == null)
                return;
            try
            {
                EnsureConventionalWarhead(missile, 15f);
                object wh = WarheadField != null ? WarheadField.GetValue(missile) : null;
                if (wh != null && WhArmed != null)
                    WhArmed.SetValue(wh, false);
                if (DebugLog != null && DebugLog.Value)
                    Log.LogInfo("ACNM-118 non-nuclear SD (fizzle) on " + missile.name);
            }
            catch (Exception ex)
            {
                if (DebugLog != null && DebugLog.Value)
                    Log.LogWarning("ForceAcnmNonNuclearDetonate: " + ex.Message);
            }
        }

        internal static void ArmAcnmNuclearWarhead(Missile missile)
        {
            if (missile == null || WarheadField == null || WhArmed == null)
                return;
            try
            {
                // Refresh scaled yield + nuke FX, then arm
                float y = GetAgmTNukeBlastYield();
                if (BlastYieldField != null)
                    BlastYieldField.SetValue(missile, y);
                ApplyWarheadFx(missile, false);
                object wh = WarheadField.GetValue(missile);
                if (wh != null)
                    WhArmed.SetValue(wh, true);
            }
            catch { }
        }

        internal static bool IsMissileWarheadArmed(Missile missile)
        {
            if (missile == null)
                return false;
            try { return missile.IsArmed(); }
            catch
            {
                try
                {
                    object wh = WarheadField != null ? WarheadField.GetValue(missile) : null;
                    if (wh != null && WhArmed != null)
                        return (bool)WhArmed.GetValue(wh);
                }
                catch { }
            }
            return false;
        }

        /// <summary>Force conventional HE yield — strips accidental IAL / 1.5kt pollution on AGM-T conv.</summary>
        internal static void EnsureConventionalWarhead(Missile missile, float blastYield)
        {
            if (missile == null)
                return;
            try
            {
                if (BlastYieldField != null)
                    BlastYieldField.SetValue(missile, blastYield);
                // Do not swap in nuclear FX for conventional
                TouchedMissiles.Remove(missile.GetInstanceID());
            }
            catch (Exception ex)
            {
                if (DebugLog != null && DebugLog.Value)
                    Log.LogWarning("EnsureConventionalWarhead: " + ex.Message);
            }
        }

        /// <summary>Disarm bus so AAM-29 body discard never blast/fizzle-explodes.</summary>
        internal static void DisarmMissileForDiscard(Missile missile)
        {
            if (missile == null)
                return;
            try
            {
                if (BlastYieldField != null)
                    BlastYieldField.SetValue(missile, 0f);
                object wh = WarheadField != null ? WarheadField.GetValue(missile) : null;
                if (wh != null && WhArmed != null)
                    WhArmed.SetValue(wh, false);
                missile.SetTangible(false);
            }
            catch (Exception ex)
            {
                if (DebugLog != null && DebugLog.Value)
                    Log.LogWarning("DisarmMissileForDiscard: " + ex.Message);
            }
        }

        internal static void ApplyNukeForced(Missile missile, string reason)
        {
            if (missile == null || !EnableNukeVariants.Value)
                return;

            int mid = missile.GetInstanceID();
            if (!TouchedMissiles.Add(mid))
                return; // already applied �?hot path (FixedUpdate) must be free

            try
            {
                if (BlastYieldField != null)
                    BlastYieldField.SetValue(missile, GetIalNukeBlastYield());
                if (PierceDamage.Value >= 0f && PierceField != null)
                    PierceField.SetValue(missile, PierceDamage.Value);
                ApplyWarheadFx(missile);

                WeaponInfo info = InfoField != null ? InfoField.GetValue(missile) as WeaponInfo : null;
                if (info != null)
                {
                    // Never mutate shared vanilla WeaponInfo — that made [IAL]-only missiles look nuclear.
                    // Instance blastYield + warhead FX above are enough for this missile's detonation.
                    bool cloneInfo = NukeInfoIds.Contains(info.GetInstanceID())
                        || IsIalNukeCloneInfo(info)
                        || IsTgm85CNuclearInfo(info)
                        || (info.name != null && info.name.IndexOf("_IAL", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (cloneInfo)
                    {
                        if (SetNuclearFlag.Value)
                            info.nuclear = true;
                        info.blastDamage = GetIalNukeBlastYield();
                        if (!IsTgm85CNuclearInfo(info) && !IsKh85WeaponInfo(info))
                        {
                            NukeInfoIds.Add(info.GetInstanceID());
                            ApplyWeaponInfoFlags(info, true);
                        }
                    }
                }

                if (DebugLog.Value)
                    Log.LogInfo("Nuke FORCED " + missile.name + " via " + reason);
            }
            catch (Exception ex)
            {
                Log.LogError("ApplyNukeForced: " + ex);
            }
        }

        internal static void ConsumePendingNuke(Missile missile, string reason)
        {
            WeaponInfo info = null;
            try
            {
                if (missile != null && InfoField != null)
                    info = InfoField.GetValue(missile) as WeaponInfo;
            }
            catch { }
            bool cloneInfo = IsIalNukeCloneInfo(info)
                || IsTgm85CNuclearInfo(info)
                || IsTgm85CNuclearMissile(missile);
            bool ownerMatch = false;
            try
            {
                if (missile != null && PendingNukeOwner != null && missile.owner != null)
                {
                    if (object.ReferenceEquals(missile.owner, PendingNukeOwner))
                        ownerMatch = true;
                    else if (missile.owner.transform != null && PendingNukeOwner.transform != null
                        && missile.owner.transform.root == PendingNukeOwner.transform.root)
                        ownerMatch = true;
                }
            }
            catch { }

            float age = Time.time - PendingNukeTime;
            PendingNukeGateService.Path path = PendingNukeGateService.Resolve(
                missile == null,
                PendingNukeSpawns,
                IsGunShellMissile(missile),
                AgmTWeapon.ShouldBlockIalPendingNuke(missile) || Aam2CvWeapon.IsAam2CvMissile(missile),
                age,
                cloneInfo,
                ownerMatch,
                PendingNukeGateService.IsFreshFire(age));

            if (path == PendingNukeGateService.Path.ClearStale)
            {
                PendingNukeSpawns = 0;
                PendingNukeWeapon = null;
                PendingNukeOwner = null;
                return;
            }
            if (path != PendingNukeGateService.Path.Consume)
            {
                if (path == PendingNukeGateService.Path.Skip
                    && missile != null
                    && DebugLog != null && DebugLog.Value
                    && PendingNukeSpawns > 0
                    && !IsGunShellMissile(missile))
                {
                    if (AgmTWeapon.ShouldBlockIalPendingNuke(missile))
                        Log.LogInfo("Skip pending nuke on AGM-T related: " + missile.name);
                    else if (!cloneInfo || !(ownerMatch || PendingNukeGateService.IsFreshFire(age)))
                        Log.LogInfo("Skip pending nuke on non-IAL spawn: " + missile.name);
                }
                return;
            }

            PendingNukeSpawns--;
            ApplyNukeForced(missile, reason + "+pending");
            if (PendingNukeSpawns <= 0)
            {
                PendingNukeWeapon = null;
                PendingNukeOwner = null;
            }
        }

        /// <summary>
        /// Eyeball / AGM_scanner recon — TargetDetector datalink pod, not a warhead missile.
        /// </summary>
        internal static bool IsScannerReconMissile(Missile missile)
        {
            if (missile == null)
                return false;
            if (MissileClassifyGateService.IsScannerReconName(missile.name))
                return true;
            try
            {
                if (missile.definition != null)
                {
                    if (MissileClassifyGateService.IsScannerReconName(missile.definition.jsonKey)
                        || MissileClassifyGateService.IsScannerReconName(missile.definition.unitName)
                        || MissileClassifyGateService.IsScannerReconName(missile.definition.name))
                        return true;
                }
            }
            catch { }
            try
            {
                WeaponInfo info = InfoField != null ? InfoField.GetValue(missile) as WeaponInfo : null;
                if (IsScannerReconInfo(info))
                    return true;
            }
            catch { }
            // Do NOT GetComponent<TargetDetector> here — FixedUpdate hot path; name/key is enough.
            return false;
        }

        internal static bool IsScannerReconInfo(WeaponInfo info)
        {
            if (info == null)
                return false;
            string n = ((info.weaponName != null ? info.weaponName : string.Empty) + " "
                + (info.shortName != null ? info.shortName : string.Empty) + " "
                + (info.name != null ? info.name : string.Empty));
            return MissileClassifyGateService.IsScannerReconName(n);
        }

        /// <summary>
        /// OpticalSeeker / LaserSeeker AGMs — use light MM (not strip). Kept for call-site clarity.
        /// </summary>
        internal static bool IsOpticalFamilyMmExcluded(Missile missile)
        {
            // No longer excludes from MM — light path handles hitch. Always false for strip gates.
            return false;
        }

        internal static bool IsOpticalOrLaserMissile(Missile missile)
        {
            if (missile == null)
                return false;
            try
            {
                MissileSeeker s = SeekerField != null ? SeekerField.GetValue(missile) as MissileSeeker : null;
                return MissileClassifyGateService.IsOpticalOrLaserSeeker(s);
            }
            catch { return false; }
        }

        /// <summary>Strip MultiModeBrain if one was attached to a gun shell / TBM / cruise / AGM-T bus / scanner by mistake.</summary>
        internal static void StripIncompatibleBrain(Missile missile)
        {
            if (missile == null)
                return;
            bool shell = IsGunShellMissile(missile);
            bool ballistic = IsBallisticMissile(missile);
            bool cruise = IsCruiseMissile(missile);
            bool scanner = IsScannerReconMissile(missile);
            bool agmT = false;
            try { agmT = AgmTWeapon.HasBusDispenser(missile) || AgmTWeapon.IsAgmTMissile(missile)
                || AgmTWeapon.IsGs25Submunition(missile); }
            catch { }
            if (!MissileClassifyGateService.ShouldStripIncompatibleBrain(shell, ballistic, cruise, agmT, scanner, false))
                return;
            try
            {
                MultiModeBrain brain = missile.GetComponent<MultiModeBrain>();
                if (brain != null)
                    UnityEngine.Object.Destroy(brain);
                // Restore launch velocity if MM fin-boost already dumped it this frame
                if (shell && missile.rb != null && missile.startingVelocity.sqrMagnitude > 100f
                    && missile.rb.velocity.sqrMagnitude < missile.startingVelocity.sqrMagnitude * 0.05f)
                    missile.rb.velocity = missile.startingVelocity;
                // Restore TBM gLimit. F9 drop uses a modest cap; loft TBM stays 0.
                if (ballistic)
                {
                    FieldInfo gLim = AccessTools.Field(typeof(Missile), "gLimit");
                    if (gLim != null)
                    {
                        float g = F9DropMark.HasTbm(missile)
                            ? StrategicArsenalMathService.F9TbmGLimit
                            : 0f;
                        gLim.SetValue(missile, g);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Cruise / terrain-following seekers — MM GuideTo overwrites TerrainWaypoint every tick.
        /// </summary>
        internal static bool IsCruiseMissile(Missile missile)
        {
            if (missile == null)
                return false;
            byte flags;
            if (TryGetFlightClass(missile, out flags))
                return (flags & MissileFlightClassCache.Cruise) != 0;
            return ResolveFlightClass(missile, out flags)
                && (flags & MissileFlightClassCache.Cruise) != 0;
        }

        internal static bool IsCruiseMissileUncached(Missile missile)
        {
            if (missile == null)
                return false;
            try
            {
                if (missile.GetComponent<OpticalSeekerCruiseMissile>() != null)
                    return true;
            }
            catch { }
            try
            {
                MissileSeeker seeker = SeekerField != null ? SeekerField.GetValue(missile) as MissileSeeker : null;
                if (seeker is OpticalSeekerCruiseMissile)
                    return true;
            }
            catch { }
            return IsCruiseMissileName(missile.name)
                || (missile.definition != null && (IsCruiseMissileName(missile.definition.unitName)
                    || IsCruiseMissileName(missile.definition.jsonKey)
                    || IsCruiseMissileName(missile.definition.name)));
        }

        internal static bool IsCruiseMissileName(string n)
        {
            return MissileClassifyGateService.IsCruiseMissileName(n);
        }

        /// <summary>
        /// TGM-85 profiles that own aimpoints (C sea-skim, E loft/dive, S hyper).
        /// MultiMode must not GuideTo — it flattens those trajectories.
        /// A/B/D still use normal MultiMode GuideTo + sticky lock.
        /// Hot path: cached Kh85Kind (no GetComponent-string each call).
        /// </summary>
        internal static bool IsKh85CTerrainMissile(Missile missile)
        {
            return GetKh85Kind(missile) == Kh85Kind.TerrainAim;
        }

        /// <summary>Cached Kh85 classification; resolves once (short Unresolved window for late stamps).</summary>
        internal static Kh85Kind GetKh85Kind(Missile missile)
        {
            if (missile == null)
                return Kh85Kind.NotKh85;
            int id = missile.GetInstanceID();
            byte cached;
            if (Kh85KindByMissileId.TryGetValue(id, out cached))
            {
                if (cached == (byte)Kh85Kind.TerrainAim
                    || cached == (byte)Kh85Kind.MultiMode
                    || cached == (byte)Kh85Kind.NotKh85)
                    return (Kh85Kind)cached;
                // Unresolved: re-resolve until stamp / age window closes.
            }

            Kh85Kind kind = ResolveKh85KindUncached(missile);
            if (kind == Kh85Kind.TerrainAim || kind == Kh85Kind.MultiMode)
            {
                Kh85KindByMissileId[id] = (byte)kind;
                MaybePruneKh85KindCache();
                return kind;
            }

            float age = 0f;
            try { age = missile.timeSinceSpawn; }
            catch { }
            // Late VariantTag / brain stamp — keep probing. Bare "TGM-85" is Unresolved
            // until C/A/B/D/E/S is stamped; never freeze that as MultiMode.
            if (kind == Kh85Kind.Unresolved || age < 0.85f)
            {
                Kh85KindByMissileId[id] = (byte)Kh85Kind.Unresolved;
                return Kh85Kind.Unresolved;
            }

            Kh85KindByMissileId[id] = (byte)Kh85Kind.NotKh85;
            MaybePruneKh85KindCache();
            return Kh85Kind.NotKh85;
        }

        private static void MaybePruneKh85KindCache()
        {
            if (Time.unscaledTime < _nextKh85KindPrune)
                return;
            _nextKh85KindPrune = Time.unscaledTime + 30f;
            if (Kh85KindByMissileId.Count > 256)
                Kh85KindByMissileId.Clear();
        }

        /// <summary>Full string-GetComponent / jsonKey resolve (once per missile, not per frame).</summary>
        private static Kh85Kind ResolveKh85KindUncached(Missile missile)
        {
            if (missile == null)
                return Kh85Kind.NotKh85;
            bool sawKh85 = false;
            try
            {
                // Component type names from Kh85MT.dll — name check avoids a hard reference.
                if (missile.GetComponent("Kh85AEcmBrain") != null
                    || missile.GetComponent("Kh85BEcmBrain") != null
                    || missile.GetComponent("Kh85DArmBrain") != null)
                    return Kh85Kind.MultiMode;
                if (missile.GetComponent("Kh85CFlightBrain") != null
                    || missile.GetComponent("Kh85EDecoyBrain") != null
                    || missile.GetComponent("Kh85SHyperBrain") != null)
                    return Kh85Kind.TerrainAim;
                if (missile.GetComponent("Kh85VariantTag") != null)
                {
                    sawKh85 = true;
                    Component tag = missile.GetComponent("Kh85VariantTag");
                    FieldInfo lf = tag != null ? AccessTools.Field(tag.GetType(), "Letter") : null;
                    if (lf != null)
                    {
                        string letter = lf.GetValue(tag) as string;
                        byte k = Kh85GuideGateService.KindFromLetter(letter);
                        if (k == Kh85GuideGateService.KindMultiMode)
                            return Kh85Kind.MultiMode;
                        if (k == Kh85GuideGateService.KindTerrainAim)
                            return Kh85Kind.TerrainAim;
                    }
                }
            }
            catch { }
            try
            {
                if (missile.definition != null && !string.IsNullOrEmpty(missile.definition.jsonKey))
                {
                    bool saw;
                    byte jk = Kh85GuideGateService.KindFromJsonKey(missile.definition.jsonKey, out saw);
                    if (saw)
                    {
                        sawKh85 = true;
                        if (jk == Kh85GuideGateService.KindMultiMode)
                            return Kh85Kind.MultiMode;
                        if (jk == Kh85GuideGateService.KindTerrainAim)
                            return Kh85Kind.TerrainAim;
                    }
                }
            }
            catch { }
            try
            {
                string n = missile.NetworkunitName;
                bool saw;
                byte nk = Kh85GuideGateService.KindFromDisplayName(n, out saw);
                if (nk == Kh85GuideGateService.KindTerrainAim)
                    return Kh85Kind.TerrainAim;
                if (nk == Kh85GuideGateService.KindMultiMode)
                    return Kh85Kind.MultiMode;
                if (saw)
                    sawKh85 = true;
            }
            catch { }
            if (!sawKh85)
            {
                try
                {
                    string un = missile.definition != null ? missile.definition.unitName : null;
                    bool saw;
                    Kh85GuideGateService.KindFromDisplayName(un, out saw);
                    if (saw)
                        sawKh85 = true;
                }
                catch { }
            }
            if (!sawKh85)
            {
                try
                {
                    WeaponInfo info = InfoField != null ? InfoField.GetValue(missile) as WeaponInfo : null;
                    if (info != null)
                    {
                        string wn = ((info.weaponName != null ? info.weaponName : string.Empty) + " "
                            + (info.shortName != null ? info.shortName : string.Empty) + " "
                            + (info.name != null ? info.name : string.Empty));
                        if (Kh85GuideGateService.WeaponBlobLooksKh85(wn))
                            sawKh85 = true;
                    }
                }
                catch { }
            }
            byte fin = Kh85GuideGateService.FinalizeKind(sawKh85);
            if (fin == Kh85GuideGateService.KindTerrainAim)
                return Kh85Kind.TerrainAim;
            if (fin == Kh85GuideGateService.KindMultiMode)
                return Kh85Kind.MultiMode;
            if (sawKh85)
                return Kh85Kind.Unresolved;
            return Kh85Kind.NotKh85;
        }

        /// <summary>Stamped, unresolved, or fire-window donor — TGM-85 family including pre-stamp.</summary>
        internal static bool IsKh85Family(Missile missile)
        {
            if (missile == null)
                return false;
            Kh85Kind k = GetKh85Kind(missile);
            if (k == Kh85Kind.TerrainAim || k == Kh85Kind.MultiMode || k == Kh85Kind.Unresolved)
                return true;
            return IsLikelyUnstampedKh85Launch(missile);
        }

        private static bool IsKh85LetterToken(string rest, string letter)
        {
            return Kh85GuideGateService.IsLetterToken(rest, letter);
        }

        internal static bool IsKh85Missile(Missile missile)
        {
            if (missile == null)
                return false;
            Kh85Kind k = GetKh85Kind(missile);
            return k == Kh85Kind.TerrainAim || k == Kh85Kind.MultiMode;
        }

        /// <summary>
        /// F1 "vanilla backup": stock AAM/AGM/SAM use vanilla seeker + fuze + collision.
        /// Custom WeXon weapons keep their own guidance.
        /// </summary>
        internal static bool VanillaBackupApplies(Missile missile)
        {
            if (VanillaMissileBackup == null || !VanillaMissileBackup.Value)
                return false;
            if (missile == null)
                return true;
            try
            {
                if (IsKh85Missile(missile) || IsLikelyUnstampedKh85Launch(missile))
                    return false;
                if (AgmTWeapon.HasBusDispenser(missile) || AgmTWeapon.IsAgmTMissile(missile)
                    || AgmTWeapon.IsPoweredGs25Sub(missile))
                    return false;
                if (Aam2CvWeapon.IsAam2CvMissile(missile))
                    return false;
                if (IsBallisticMissile(missile) || IsCruiseMissile(missile))
                    return false;
                if (IsGunShellMissile(missile) || IsMotorlessProjectile(missile))
                    return false;
            }
            catch { }
            return true;
        }

        /// <summary>
        /// Seeker.Initialize often runs inside SpawnMissile.SetTarget BEFORE Kh85MT OnSpawned
        /// stamps VariantTag. Reflect Kh85MT sticky fire context so MM does not GuideTo a
        /// still-AGM donor and slam into terrain (looks like instant vanish).
        /// </summary>
        private static Type _kh85WeaponType;
        private static bool _kh85WeaponTypeResolved;
        private static FieldInfo _kh85LastFireKey;
        private static FieldInfo _kh85LastFireTime;
        private static FieldInfo _kh85LastFireOwner;

        internal static bool IsLikelyUnstampedKh85Launch(Missile missile)
        {
            if (missile == null)
                return false;
            // Fire-context window only — never AccessTools.TypeByName on aged AGMs.
            float age = 0f;
            try { age = missile.timeSinceSpawn; }
            catch { }
            if (age > 2.5f)
                return false;
            if (IsKh85Missile(missile))
                return true;
            try
            {
                if (!_kh85WeaponTypeResolved)
                {
                    _kh85WeaponTypeResolved = true;
                    _kh85WeaponType = AccessTools.TypeByName("Kh85MT.Kh85Weapon");
                    if (_kh85WeaponType != null)
                    {
                        _kh85LastFireKey = AccessTools.Field(_kh85WeaponType, "_lastFireKey");
                        _kh85LastFireTime = AccessTools.Field(_kh85WeaponType, "_lastFireTime");
                        _kh85LastFireOwner = AccessTools.Field(_kh85WeaponType, "_lastFireOwner");
                    }
                }
                if (_kh85WeaponType == null || _kh85LastFireKey == null || _kh85LastFireTime == null)
                    return false;
                string key = _kh85LastFireKey.GetValue(null) as string;
                if (string.IsNullOrEmpty(key)
                    || !key.StartsWith("Kh85MT", StringComparison.OrdinalIgnoreCase))
                    return false;
                float t = 0f;
                try { t = Convert.ToSingle(_kh85LastFireTime.GetValue(null)); }
                catch { return false; }
                if (Time.time > t + 2.5f)
                    return false;
                Unit owner = _kh85LastFireOwner != null
                    ? _kh85LastFireOwner.GetValue(null) as Unit
                    : null;
                Unit mOwner = null;
                try { mOwner = missile.owner; }
                catch { }
                if (owner != null && mOwner != null && !object.ReferenceEquals(owner, mOwner))
                    return false;
                return true;
            }
            catch { return false; }
        }

        /// <summary>TGM-85 LOAL: no free-hunt until 1.6 s after spawn. Player locks skip.</summary>
        internal static bool Kh85LoalHuntDelayActive(Missile missile, bool playerOrSticky)
        {
            if (missile == null || playerOrSticky)
                return false;
            if (!IsKh85Family(missile))
                return false;
            float age = 0f;
            try { age = missile.timeSinceSpawn; }
            catch { return true; }
            return Kh85GuideGateService.LoalHuntDelayActive(true, false, age);
        }

        /// <summary>True when MultiMode must not GuideTo yet (Kh85MT owns aim / stamp pending).</summary>
        internal static bool ShouldDeferKh85GuideTo(Missile missile)
        {
            if (missile == null)
                return false;
            Kh85Kind kind = GetKh85Kind(missile);
            float age = 0f;
            try { age = missile.timeSinceSpawn; }
            catch { age = 0f; }
            float spd = 0f;
            try
            {
                if (missile.rb != null)
                    spd = missile.rb.velocity.magnitude;
            }
            catch { }
            float minSpd = MinGuideSpeedMps != null ? MinGuideSpeedMps.Value : 90f;
            return Kh85GuideGateService.ShouldDeferGuideTo(
                false,
                (byte)kind,
                kind == Kh85Kind.NotKh85 && IsLikelyUnstampedKh85Launch(missile),
                age,
                spd,
                minSpd);
        }

        /// <summary>Rockets / unguided nuke rockets — keep vanilla; MM free-hunt breaks them.</summary>
        internal static bool IsRocketOrUnguidedName(string n)
        {
            return MissileClassifyGateService.IsRocketOrUnguidedName(n);
        }

        /// <summary>
        /// Piledriver TBM / BallisticMissileGuidance loft INS. MM GuideTo + forcing gLimit
        /// (vanilla uses 0 = no cap) crushes steering at high speed — looks like no turn.
        /// </summary>
        internal static bool IsBallisticMissileName(string n)
        {
            return MissileClassifyGateService.IsBallisticMissileName(n);
        }

        internal static bool IsBallisticMissile(Missile missile)
        {
            if (missile == null)
                return false;
            byte flags;
            if (TryGetFlightClass(missile, out flags))
                return (flags & MissileFlightClassCache.Ballistic) != 0;
            return ResolveFlightClass(missile, out flags)
                && (flags & MissileFlightClassCache.Ballistic) != 0;
        }

        internal static bool IsBallisticMissileUncached(Missile missile)
        {
            if (missile == null)
                return false;
            // TGM-85 uses a TBM engine scaffold but is its own munition — never treat as Piledriver.
            try
            {
                string jsonKey = missile.definition != null ? missile.definition.jsonKey : null;
                string unitName = missile.definition != null ? missile.definition.unitName : null;
                if (MissileClassifyGateService.IsKh85MtExcludedFromBallistic(jsonKey, unitName, missile.name))
                    return false;
            }
            catch { }
            if (IsBallisticMissileName(missile.name))
                return true;
            try
            {
                if (missile.definition != null)
                {
                    if (IsBallisticMissileName(missile.definition.unitName)
                        || IsBallisticMissileName(missile.definition.jsonKey)
                        || IsBallisticMissileName(missile.definition.name))
                        return true;
                }
            }
            catch { }
            try
            {
                MissileSeeker seeker = SeekerField != null ? SeekerField.GetValue(missile) as MissileSeeker : null;
                if (seeker is BallisticMissileGuidance)
                    return true;
                if (missile.GetComponent<BallisticMissileGuidance>() != null)
                    return true;
            }
            catch { }
            WeaponInfo info = InfoField != null ? InfoField.GetValue(missile) as WeaponInfo : null;
            if (info != null)
            {
                string n = ((info.weaponName != null ? info.weaponName : string.Empty) + " "
                    + (info.shortName != null ? info.shortName : string.Empty) + " " + info.name);
                if (IsBallisticMissileName(n))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// TBM-only assist: expand free-hunt range, feed BallisticMissileGuidance knownPos.
        /// Never attaches MultiModeBrain (no GuideTo / gLimit).
        /// </summary>
        internal static void SetupTbmHunt(Missile missile)
        {
            if (missile == null
                || !MultiModeHuntGateService.ShouldSetupTbmHunt(
                    AllowFreeAttack.Value, IsBallisticMissile(missile)))
                return;
            TbmHuntAssist assist = missile.GetComponent<TbmHuntAssist>();
            if (assist == null)
                assist = TryAddBehaviour<TbmHuntAssist>(missile.gameObject);
            if (assist == null)
                return;
            assist.Setup(missile);
        }

        /// <summary>
        /// Skip seeker SlowChecks / MissedTarget / LosingGround / low-speed airburst for the
        /// whole guided flight. Hits and proximity still detonate. Shells and unguided rockets
        /// keep vanilla SD.
        /// </summary>
        internal static bool ShouldSuppressSeekerSelfDestruct(Missile missile)
        {
            if (missile == null)
                return false;
            return SeekerIffGateService.ShouldSuppressSeekerSelfDestruct(
                IsGunShellMissile(missile),
                IsUnguidedRocketMissile(missile));
        }

        internal static bool IsUnguidedRocketMissile(Missile missile)
        {
            if (missile == null)
                return false;
            string jsonKey = null;
            string unitName = null;
            string defName = null;
            try
            {
                if (missile.definition != null)
                {
                    jsonKey = missile.definition.jsonKey;
                    unitName = missile.definition.unitName;
                    defName = missile.definition.name;
                }
            }
            catch { }
            return MissileClassifyGateService.IsRocketOrUnguidedMissile(
                missile.name, jsonKey, unitName, defName);
        }

        // Low-speed self-destruct: fills gap when MultiMode suppresses vanilla SlowChecks
        // and the missile bleeds energy into a dud fall.
        private static readonly Dictionary<int, float> LowSpeedBelowSince = new Dictionary<int, float>(128);
        /// <summary>0=unknown, 1=skip forever (shell/motorless/client), 2=armed for SD checks.</summary>
        private static readonly Dictionary<int, byte> LowSpeedSdFlags = new Dictionary<int, byte>(128);
        private static float _nextLowSpeedPrune;

        /// <summary>
        /// If missile has been below LowSpeedSelfDestructMps long enough after min age / motor burnout,
        /// force Detonate (terrain-style). Server-authoritative.
        /// </summary>
        internal static void TickLowSpeedSelfDestruct(Missile missile)
        {
            if (EnableLowSpeedSelfDestruct == null || !EnableLowSpeedSelfDestruct.Value)
                return;
            if (missile == null || missile.disabled)
                return;
            Kh85Kind sdKind = GetKh85Kind(missile);
            if (sdKind == Kh85Kind.TerrainAim || sdKind == Kh85Kind.MultiMode
                || sdKind == Kh85Kind.Unresolved || IsLikelyUnstampedKh85Launch(missile))
                return;
            // LOAL / live hunt: vanilla SlowChecks already skipped; do not airburst
            // after burnout just because speed bled off on a long shot.
            if (ShouldSuppressSeekerSelfDestruct(missile))
                return;

            int id = missile.GetInstanceID();
            byte sdFlag;
            if (LowSpeedSdFlags.TryGetValue(id, out sdFlag) && sdFlag == 1)
                return;

            bool trackingBelow = LowSpeedBelowSince.ContainsKey(id);
            if (LowSpeedSdGateService.ShouldSkipSampleFrame(trackingBelow, Time.frameCount, id))
                return;

            bool isServer;
            try { isServer = missile.IsServer; }
            catch
            {
                LowSpeedSdFlags[id] = 1;
                return;
            }

            LowSpeedSdGateService.Path elig = LowSpeedSdGateService.ResolveEligibility(
                isServer,
                sdFlag,
                IsGunShellMissile(missile) || IsMotorlessProjectile(missile));
            if (elig == LowSpeedSdGateService.Path.MarkDone)
            {
                LowSpeedSdFlags[id] = 1;
                return;
            }
            if (sdFlag != 2)
                LowSpeedSdFlags[id] = 2;

            float age = 0f;
            try { age = missile.timeSinceSpawn; }
            catch { }
            float minAgeBase = LowSpeedSelfDestructMinAge != null ? LowSpeedSelfDestructMinAge.Value : 3.5f;
            // Cheap min-age gate before ballistic name scans.
            if (age < minAgeBase)
                return;
            // RAM-45 / R9 VLS: dummy motor then delayed sustain. Remaining burn can read 0
            // between stages and false-trigger after the 3.5s floor.
            if (age < 12f && IsVlsSoftLaunchMissile(missile))
                return;
            float bMin = LowSpeedSelfDestructBallisticMinAge != null
                ? LowSpeedSelfDestructBallisticMinAge.Value : 12f;
            bool ballistic = IsBallisticMissile(missile);
            float minAge = LowSpeedSdGateService.ResolveMinAgeSec(minAgeBase, ballistic, bMin);

            float burn = 0f;
            try { burn = missile.GetRemainingBurnTime(); }
            catch { burn = 0f; }

            float speed = 0f;
            Vector3 vel = Vector3.zero;
            try
            {
                if (missile.rb != null)
                {
                    vel = missile.rb.velocity;
                    speed = vel.magnitude;
                }
            }
            catch { return; }

            float now = Time.time;
            float since = 0f;
            bool hasBelow = LowSpeedBelowSince.TryGetValue(id, out since);
            float hold = LowSpeedSelfDestructHold != null ? LowSpeedSelfDestructHold.Value : 0.85f;

            LowSpeedSdGateService.Path path = LowSpeedSdGateService.ResolveAfterEligibility(
                AgmTDispenser.IsSafeDiscard(missile),
                age,
                minAge,
                burn,
                speed,
                LowSpeedSelfDestructThresholdMps(),
                ballistic,
                vel.y,
                hasBelow,
                now,
                since,
                hold);

            if (path == LowSpeedSdGateService.Path.ClearBelow)
            {
                LowSpeedBelowSince.Remove(id);
                return;
            }
            if (path == LowSpeedSdGateService.Path.StartBelowTimer)
            {
                LowSpeedBelowSince[id] = now;
                return;
            }
            if (path == LowSpeedSdGateService.Path.WaitHold || path == LowSpeedSdGateService.Path.NoOp)
                return;
            if (path != LowSpeedSdGateService.Path.Detonate)
                return;

            LowSpeedBelowSince.Remove(id);
            try
            {
                if (!missile.IsArmed())
                    missile.Arm();
            }
            catch { }
            try
            {
                // Terrain-style boom so fuse/yield still apply when ground collision never fired.
                missile.Detonate(Vector3.up, false, true);
                if (DebugLog != null && DebugLog.Value && Log != null)
                    Log.LogInfo("Low-speed SD: " + missile.name + " spd=" + speed.ToString("0.0")
                        + " age=" + age.ToString("0.0"));
            }
            catch (Exception ex)
            {
                if (Log != null)
                    Log.LogWarning("Low-speed SD failed: " + ex.Message);
            }

            if (LowSpeedSdGateService.ShouldPruneMaps(Time.unscaledTime, _nextLowSpeedPrune))
            {
                _nextLowSpeedPrune = LowSpeedSdGateService.NextPruneAt(Time.unscaledTime);
                if (LowSpeedSdGateService.ShouldClearBelowMap(LowSpeedBelowSince.Count))
                    LowSpeedBelowSince.Clear();
                if (LowSpeedSdGateService.ShouldClearFlagMap(LowSpeedSdFlags.Count))
                    LowSpeedSdFlags.Clear();
            }
        }

        internal static void SetupMultiMode(Missile missile, MissileSeeker caller, Unit target, GlobalPosition aimpoint)
        {
            if (missile == null)
                return;
            if (VanillaBackupApplies(missile))
            {
                StripIncompatibleBrain(missile);
                return;
            }
            // C/E/S (and unstamped TGM-85) own aim. MM optical GuideTo dives them into
            // terrain — 175C never attached a GuideTo brain to those profiles.
            Kh85Kind setupKind = GetKh85Kind(missile);
            if (setupKind == Kh85Kind.TerrainAim || setupKind == Kh85Kind.Unresolved
                || IsLikelyUnstampedKh85Launch(missile))
            {
                try
                {
                    MultiModeBrain khBrain = MultiModeBrain.Find(missile);
                    if (khBrain == null)
                        khBrain = missile.GetComponent<MultiModeBrain>();
                    if (khBrain != null)
                        UnityEngine.Object.Destroy(khBrain);
                }
                catch { }
                return;
            }
            // Naval missiles keep vanilla seekers (SARH illumination, no MM GuideTo).
            // Aircraft shots must never inherit a ship stamp (carrier / pending VLS fire).
            if (IsShipLaunchedMissile(missile) && !ShipMissileService.IsAircraftSide(missile.owner))
            {
                StripIncompatibleBrain(missile);
                return;
            }
            // SARH needs the illuminator; MM GuideTo / IFF hunt would drop lock and airburst.
            if (caller is SARHSeeker)
            {
                StripIncompatibleBrain(missile);
                return;
            }
            try
            {
                if (missile.GetComponent<SARHSeeker>() != null)
                {
                    StripIncompatibleBrain(missile);
                    return;
                }
            }
            catch { }
            WeaponInfo infoCheck = InfoField != null ? InfoField.GetValue(missile) as WeaponInfo : null;
            string wn = string.Empty;
            if (infoCheck != null)
            {
                wn = ((infoCheck.weaponName != null ? infoCheck.weaponName : string.Empty) + " "
                    + (infoCheck.shortName != null ? infoCheck.shortName : string.Empty));
            }
            MissileSeeker primary = SeekerField != null ? SeekerField.GetValue(missile) as MissileSeeker : null;
            bool seekerMismatch = primary != null && caller != null && primary != caller;

            MultiModeSetupGateService.Path path = MultiModeSetupGateService.Resolve(
                EnableMultiMode.Value,
                IsMissileAllowed(missile),
                IsGunShellMissile(missile),
                AgmTWeapon.HasBusDispenser(missile) || AgmTWeapon.IsAgmTMissile(missile)
                    || AgmTWeapon.IsGs25Submunition(missile),
                IsGunShellSeeker(caller),
                IsBallisticMissile(missile) || caller is BallisticMissileGuidance,
                IsCruiseMissile(missile) || caller is OpticalSeekerCruiseMissile,
                IsRocketOrUnguidedName(missile != null ? missile.name : null),
                IsRocketOrUnguidedName(wn) || IsBallisticMissileName(wn),
                seekerMismatch,
                IsScannerReconMissile(missile) || IsScannerReconInfo(infoCheck));

            if (path == MultiModeSetupGateService.Path.StripAgmT)
            {
                StripIncompatibleBrain(missile);
                return;
            }
            if (path != MultiModeSetupGateService.Path.AllowAttach)
                return;

            // Optional CPU cut: optical/laser skip MultiModeBrain (disables LOAL). Default ON for LOAL.
            MissileSeeker mmSeeker = caller != null ? caller : primary;
            if (EnableOpticalMultiMode != null && !EnableOpticalMultiMode.Value
                && MissileClassifyGateService.IsOpticalOrLaserSeeker(mmSeeker)
                && !(mmSeeker is OpticalSeekerCruiseMissile)
                && !(mmSeeker is OpticalSeekerShell))
            {
                StripIncompatibleBrain(missile);
                return;
            }

            MultiModeBrain brain = MultiModeBrain.Find(missile);
            if (brain == null)
                brain = missile.GetComponent<MultiModeBrain>();
            if (brain == null)
                brain = TryAddBehaviour<MultiModeBrain>(missile.gameObject);
            if (brain == null)
                return;
            brain.Setup(missile, caller != null ? caller : primary, target, aimpoint);
        }

        /// <summary>
        /// Packed Core.bin: Unity may reject extra payload MonoBehaviours.
        /// Catch + log so Seeker.Initialize postfix still finishes (IFF etc.).
        /// </summary>
        internal static T TryAddBehaviour<T>(GameObject go) where T : MonoBehaviour
        {
            if (go == null)
                return null;
            T c = null;
            try { c = go.GetComponent<T>(); }
            catch { c = null; }
            if (c != null)
                return c;
            try
            {
                c = go.AddComponent<T>();
            }
            catch (Exception ex)
            {
                if (Log != null)
                    Log.LogWarning("AddComponent " + typeof(T).Name + ": " + ex.Message);
                return null;
            }
            if (c == null && Log != null)
                Log.LogWarning("AddComponent " + typeof(T).Name + " returned null");
            return c;
        }

        internal static FactionHQ GetHq(Unit unit)
        {
            if (unit == null)
                return null;
            // 175C: buildings / ground often have MapHQ while NetworkHQ is still null.
            FactionHQ hq = null;
            try { hq = unit.NetworkHQ; }
            catch { }
            if (hq != null)
                return hq;
            try { hq = unit.MapHQ; }
            catch { }
            return hq;
        }

        /// <summary>Shooter HQ from owner first, then the missile itself.</summary>
        internal static FactionHQ GetShooterHq(Missile missile)
        {
            if (missile == null)
                return null;
            FactionHQ hq = GetHq(missile.owner);
            if (hq != null)
                return hq;
            return GetHq(missile);
        }

        internal static Unit ResolveShooterSide(Missile missile)
        {
            if (missile == null)
                return null;
            if (missile.owner != null)
                return missile.owner;
            return missile;
        }

        internal static bool IsUnitAlive(Unit unit)
        {
            if (unit == null)
                return false;
            try
            {
                if (unit.disabled || unit.Networkdisabled)
                    return false;
            }
            catch { return false; }
            return true;
        }

        internal static bool IsSameFaction(Unit a, Unit b)
        {
            FactionHQ ha = GetHq(a);
            FactionHQ hb = GetHq(b);
            return ha != null && hb != null && object.ReferenceEquals(ha, hb);
        }

        /// <summary>
        /// Unaligned (null HQ) or Neutral faction. LOAL must not treat these as hostiles
        /// just because they are not the shooter's HQ.
        /// </summary>
        internal static bool IsNeutralHq(FactionHQ hq)
        {
            if (hq == null)
                return true;
            string objName = null;
            string facName = null;
            try { objName = hq.name; }
            catch { }
            try
            {
                if (hq.faction != null)
                    facName = hq.faction.factionName;
            }
            catch { }
            return SeekerIffGateService.IsNeutralFactionLabel(objName, facName);
        }

        /// <summary>
        /// Ejected crew / parachuting pilots (Unit = PilotDismounted). Missiles must never
        /// free-hunt, re-lock, or GuideTo these — chasing pilots after a kill is wrong.
        /// </summary>
        internal static bool IsEjectedPilotUnit(Unit candidate)
        {
            if (candidate == null)
                return false;
            if (candidate is PilotDismounted)
                return true;
            try
            {
                Type t = candidate.GetType();
                if (typeof(PilotDismounted).IsAssignableFrom(t))
                    return true;
                string n = t.Name;
                if (n.IndexOf("PilotDismounted", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                if (n.IndexOf("EjectedPilot", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            catch { }
            return false;
        }

        /// <summary>
        /// TGM-85E decoy: IFF normally rejects missiles as lock targets, which
        /// would undo lock-steal onto the decoy (especially SARH targetTransform).
        /// </summary>
        internal static bool IsKh85EDecoy(Unit unit)
        {
            if (unit == null)
                return false;
            try { return unit.GetComponent("Kh85EDecoyBrain") != null; }
            catch { return false; }
        }

        /// <summary>
        /// Soft IFF for player locks / SetTarget: reject confirmed friendlies (same HQ).
        /// Null HQ does not count as hostile or friendly.
        /// </summary>
        internal static bool IsAllowedTarget(Unit shooterSide, Unit candidate)
        {
            if (candidate == null || shooterSide == null)
                return false;
            if (object.ReferenceEquals(candidate, shooterSide))
                return false;
            // Never chase ejected pilots / parachutes (even with IFF off).
            if (IsEjectedPilotUnit(candidate))
                return false;
            // Never "lock self" via parent aircraft / missile-as-unit confusion.
            try
            {
                Aircraft sa = shooterSide as Aircraft;
                if (sa == null)
                    sa = shooterSide.GetComponentInParent<Aircraft>();
                Aircraft ca = candidate as Aircraft;
                if (ca == null)
                    ca = candidate.GetComponentInParent<Aircraft>();
                if (sa != null && ca != null && object.ReferenceEquals(sa, ca))
                    return false;
            }
            catch { }
            if (candidate is Missile && !IsKh85EDecoy(candidate))
                return false;
            if (!IsUnitAlive(candidate))
                return false;
            if (candidate is Scenery)
                return false;

            if (!EnableIff.Value)
                return true;

            // 175C SetTarget: only reject confirmed same-faction. Incomplete HQ is
            // hunt-gated in IsHostileHuntTarget — blocking it here also blocked
            // TGM-85 ApplyTargetLock and the cruise seeker then self-destructed.
            return SeekerIffGateService.SoftIffAllows(true, IsSameFaction(shooterSide, candidate));
        }

        /// <summary>
        /// Strict hostility for free-hunt (175C): both sides must have HQ and be different HQ objects.
        /// Incomplete HQ is not hunted — that is what kept nearest friendly hangars from winning LOAL.
        /// </summary>
        internal static bool IsHostileHuntTarget(Missile missile, Unit candidate)
        {
            if (missile == null || candidate == null)
                return false;
            if (object.ReferenceEquals(candidate, missile) || object.ReferenceEquals(candidate, missile.owner))
                return false;
            if (IsEjectedPilotUnit(candidate))
                return false;
            if (!IsUnitAlive(candidate))
                return false;
            if (candidate is Scenery)
                return false;
            if (candidate is Missile)
                return false;
            if (IsJunkHuntTarget(candidate))
                return false;

            if (!EnableIff.Value)
            {
                return SeekerIffGateService.StrictHuntAllows(
                    false,
                    IsAllowedTarget(ResolveShooterSide(missile), candidate),
                    false, false, false);
            }

            FactionHQ shootHq = GetShooterHq(missile);
            FactionHQ tgtHq = GetHq(candidate);
            if (IsNeutralHq(shootHq) || IsNeutralHq(tgtHq))
                return false;
            return SeekerIffGateService.StrictHuntAllows(
                true,
                false,
                shootHq != null,
                tgtHq != null,
                shootHq != null && tgtHq != null && object.ReferenceEquals(shootHq, tgtHq));
        }

        /// <summary>
        /// LOAL hunt: only opposing combat HQs. Container, Neutral/unaligned (null HQ),
        /// and friendlies are out of the search set.
        /// </summary>
        internal static bool IsLoalHuntTarget(Missile missile, Unit candidate)
        {
            if (missile == null || candidate == null)
                return false;
            if (candidate is Container)
                return false;
            if (IsJunkHuntTarget(candidate))
                return false;
            return IsHostileHuntTarget(missile, candidate);
        }

        internal static float LoalTargetValueM(Unit u)
        {
            if (u == null)
                return 0f;
            try
            {
                UnitDefinition def = u.definition;
                if (def != null)
                    return def.value;
            }
            catch { }
            return 0f;
        }

        /// <summary>
        /// ACM-119 / ACNM-118 GS25: reject confirmed friendlies only.
        /// Strict hunt (both HQs) dropped incomplete-HQ ships/buildings after dispense,
        /// so subs never SetAimpoint and flew straight.
        /// </summary>
        internal static bool IsAgmTEngageTarget(Missile missile, Unit candidate)
        {
            if (missile == null || candidate == null)
                return false;
            if (object.ReferenceEquals(candidate, missile)
                || object.ReferenceEquals(candidate, missile.owner))
                return false;
            if (IsEjectedPilotUnit(candidate))
                return false;
            if (!IsUnitAlive(candidate))
                return false;
            if (candidate is Scenery || candidate is Missile)
                return false;
            if (IsJunkHuntTarget(candidate))
                return false;
            if (candidate is Container)
                return false;
            return IsHostileHuntTarget(missile, candidate);
        }

        /// <summary>
        /// Camouflage nets / civilian decoy buildings: never auto-lock.
        /// Player TGP on hangars / SAM / factories still works (those are not CIV).
        /// </summary>
        internal static bool IsJunkHuntTarget(Unit candidate)
        {
            if (candidate == null)
                return false;
            if (candidate is Scenery)
                return true;
            Building b = candidate as Building;
            int buildingType = -1;
            float valueM = -1f;
            if (b != null)
            {
                try
                {
                    BuildingDefinition bd = candidate.definition as BuildingDefinition;
                    if (bd != null)
                    {
                        buildingType = (int)bd.buildingType;
                        valueM = bd.value;
                    }
                }
                catch { }
            }
            string blob = UnitHuntNameBlob(candidate);
            if (b != null)
                return HuntJunkGateService.IsJunkHuntBuilding(buildingType, valueM, blob);
            return HuntJunkGateService.IsJunkHuntName(blob);
        }

        internal static string UnitHuntNameBlob(Unit u)
        {
            if (u == null)
                return string.Empty;
            string n = u.name != null ? u.name : string.Empty;
            try
            {
                if (u.definition != null)
                {
                    if (!string.IsNullOrEmpty(u.definition.unitName))
                        n = n + " " + u.definition.unitName;
                    if (!string.IsNullOrEmpty(u.definition.jsonKey))
                        n = n + " " + u.definition.jsonKey;
                    if (!string.IsNullOrEmpty(u.definition.name))
                        n = n + " " + u.definition.name;
                }
            }
            catch { }
            return n;
        }

        /// <summary>
        /// Lock already on the missile (vanilla spawn / datalink targetID).
        /// Does NOT steal the aircraft TGP/radar primary — that was killing LOAL.
        /// </summary>
        internal static Unit ResolveMissileOwnTarget(Missile missile)
        {
            if (missile == null)
                return null;

            Unit side = ResolveShooterSide(missile);

            try
            {
                Unit mt = MissileTargetField != null ? MissileTargetField.GetValue(missile) as Unit : null;
                if (mt != null && IsAllowedTarget(side, mt))
                    return mt;
            }
            catch { }

            try
            {
                PersistentID tid = missile.targetID;
                if (tid.IsValid)
                {
                    Unit byId;
                    if (UnitRegistry.TryGetUnit(tid, out byId) && IsAllowedTarget(side, byId))
                        return byId;
                }
            }
            catch { }

            return null;
        }

        /// <summary>Player/AI lock from launching aircraft (primary target / WM list / missile.target / targetID).</summary>
        internal static Unit ResolveDesignatedTarget(Missile missile)
        {
            if (missile == null)
                return null;

            Unit own = ResolveMissileOwnTarget(missile);
            if (own != null)
                return own;

            Unit side = ResolveShooterSide(missile);

            Unit owner = missile.owner;
            Aircraft ac = owner as Aircraft;
            if (ac == null && owner != null)
                ac = owner.GetComponentInParent<Aircraft>();
            if (ac == null)
                return null;

            // Pilot primary lock (player stick/radar lock)
            try
            {
                if (ac.pilots != null)
                {
                    for (int i = 0; i < ac.pilots.Length; i++)
                    {
                        Pilot p = ac.pilots[i];
                        if (p == null)
                            continue;
                        Unit pt = p.GetPrimaryTarget();
                        if (pt != null && IsAllowedTarget(side, pt))
                            return pt;
                    }
                }
            }
            catch { }

            // WeaponManager shared target list
            try
            {
                if (ac.weaponManager != null)
                {
                    List<Unit> list = ac.weaponManager.GetTargetList();
                    if (list != null)
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            Unit u = list[i];
                            if (u != null && IsAllowedTarget(side, u))
                                return u;
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// True fire-time lock only (missile target / pilot primary). WM contact list is not a lock.
        /// </summary>
        internal static Unit ResolveHardLockTarget(Missile missile)
        {
            if (missile == null)
                return null;

            Unit own = ResolveMissileOwnTarget(missile);
            if (own != null)
                return own;

            Unit side = ResolveShooterSide(missile);
            Unit owner = missile.owner;
            Aircraft ac = owner as Aircraft;
            if (ac == null && owner != null)
                ac = owner.GetComponentInParent<Aircraft>();
            if (ac == null)
                return null;

            try
            {
                if (ac.pilots != null)
                {
                    for (int i = 0; i < ac.pilots.Length; i++)
                    {
                        Pilot p = ac.pilots[i];
                        if (p == null)
                            continue;
                        Unit pt = p.GetPrimaryTarget();
                        if (pt != null && IsAllowedTarget(side, pt))
                            return pt;
                    }
                }
            }
            catch { }

            return null;
        }

        private static readonly Dictionary<int, float> NextIffEnforceAt = new Dictionary<int, float>(64);
        private const float IffEnforceIntervalSec = 1.0f;

        /// <summary>True when IFF enforce is due (also marks next slot). Seek uses this to skip work.</summary>
        internal static bool IffEnforceDue(Missile missile)
        {
            if (missile == null)
                return false;
            int id = missile.GetInstanceID();
            float now = Time.unscaledTime;
            float next;
            if (NextIffEnforceAt.TryGetValue(id, out next) && now < next)
                return false;
            NextIffEnforceAt[id] = now + IffEnforceIntervalSec;
            return true;
        }

        /// <summary>IFF at most 1 Hz per missile — Seek runs every physics frame.</summary>
        internal static void EnforceIffThrottled(Missile missile)
        {
            if (!IffEnforceDue(missile))
                return;
            EnforceIffOnCurrentSeeker(missile);
        }

        internal static void EnforceIffOnCurrentSeeker(Missile missile)
        {
            if (missile == null || SeekerField == null)
                return;
            if (IsShipLaunchedMissile(missile))
                return;
            MissileSeeker seeker = SeekerField.GetValue(missile) as MissileSeeker;
            if (seeker == null || seeker is SARHSeeker)
                return;
            Unit side = ResolveShooterSide(missile);
            Unit tgt = SeekerTargetField != null ? SeekerTargetField.GetValue(seeker) as Unit : null;
            // Always clear ejected pilots (even with IFF off). Other clears need EnableIff.
            bool clear = tgt != null && IsEjectedPilotUnit(tgt);
            if (!clear && EnableIff.Value && tgt != null && !IsAllowedTarget(side, tgt))
                clear = true;
            if (clear)
            {
                if (SeekerTargetField != null)
                    SeekerTargetField.SetValue(seeker, null);
                // Clear optical transform so GetTargetParameters early-returns instead of tracking friendlies
                try
                {
                    Type st = seeker.GetType();
                    FieldInfo tf;
                    if (!SeekerTargetTransformFields.TryGetValue(st, out tf))
                    {
                        tf = AccessTools.Field(st, "targetTransform");
                        SeekerTargetTransformFields[st] = tf;
                    }
                    if (tf != null)
                        tf.SetValue(seeker, null);
                    FieldInfo hv;
                    if (!SeekerHasVisualFields.TryGetValue(st, out hv))
                    {
                        hv = AccessTools.Field(st, "hasVisual");
                        SeekerHasVisualFields[st] = hv;
                    }
                    if (hv != null && hv.FieldType == typeof(bool))
                        hv.SetValue(seeker, false);
                }
                catch { }
            }
        }

        /// <summary>
        /// IR/ARH must not sticky-lock a TGP tank; optical/laser must not hunt aircraft.
        /// Kh85 dual-role: any unit.
        /// </summary>
        internal static bool TargetFitsSeekerFamily(Missile missile, MissileSeeker seeker, Unit unit)
        {
            if (unit == null)
                return false;
            if (missile != null && IsKh85Missile(missile))
                return true;
            if (MissileLooksAirToAir(missile))
            {
                if (unit is GroundVehicle || unit is Building)
                    return false;
                return true;
            }
            if (IsSurfaceAttackSeeker(seeker))
                return IsSurfaceUnit(unit);
            if (unit is GroundVehicle || unit is Building)
                return false;
            return true;
        }

        /// <summary>AGM-style seekers should free-hunt ground/naval units, not nearest aircraft.</summary>
        internal static bool IsSurfaceAttackSeeker(MissileSeeker seeker)
        {
            if (seeker == null)
                return false;
            Type t = seeker.GetType();
            return typeof(OpticalSeeker).IsAssignableFrom(t)
                || typeof(LaserSeeker).IsAssignableFrom(t)
                || t.Name.IndexOf("Optical", StringComparison.OrdinalIgnoreCase) >= 0
                || t.Name.IndexOf("Laser", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool MissileLooksAirToAir(Missile missile)
        {
            if (missile == null)
                return false;
            string n = missile.name != null ? missile.name : string.Empty;
            try
            {
                WeaponInfo info = InfoField != null ? InfoField.GetValue(missile) as WeaponInfo : null;
                if (info != null)
                {
                    n = n + " " + (info.weaponName != null ? info.weaponName : string.Empty)
                        + " " + (info.shortName != null ? info.shortName : string.Empty);
                }
            }
            catch { }
            if (n.IndexOf("AAM", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("AIM", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("air-to-air", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("AGM", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            if (n.IndexOf("TGM", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            return false;
        }

        internal static bool MissileHuntsSurfaceOnly(Missile missile, MissileSeeker seeker)
        {
            if (missile != null && (IsKh85Missile(missile) || IsKh85CTerrainMissile(missile)))
                return false;
            if (MissileLooksAirToAir(missile))
                return false;
            return IsSurfaceAttackSeeker(seeker);
        }

        internal static bool IsSurfaceUnit(Unit u)
        {
            if (u == null)
                return false;
            return u is Building || u is GroundVehicle || u is Ship;
        }

        internal static void RefreshMountCache()
        {
            CachedMounts.Clear();
            CachedMountSet.Clear();
            WeaponMount[] all = Resources.FindObjectsOfTypeAll<WeaponMount>();
            for (int i = 0; i < all.Length; i++)
            {
                WeaponMount m = all[i];
                if (m == null || IsMountDisabled(m) || IsMountBlacklisted(m))
                    continue;
                if (CachedMountSet.Add(m))
                    CachedMounts.Add(m);
            }
            for (int i = 0; i < NukeMountClones.Count; i++)
            {
                WeaponMount m = NukeMountClones[i];
                if (m != null && CachedMountSet.Add(m))
                    CachedMounts.Add(m);
            }
            // AGM-T Instantiated mounts can be missed by FindObjectsOfTypeAll after Clear
            AgmTWeapon.EnsureMountsInCache();
            Aam2CvWeapon.EnsureMountsInCache();
        }

        private static bool IsMountDisabled(WeaponMount m)
        {
            if (m == null || MountDisabledField == null)
                return false;
            try { return (bool)MountDisabledField.GetValue(m); }
            catch { return false; }
        }

        internal static bool IsMountBlacklisted(WeaponMount m)
        {
            if (m == null)
                return true;
            string n = ((m.mountName != null ? m.mountName : string.Empty) + " " + m.name + " "
                + (m.info != null && m.info.weaponName != null ? m.info.weaponName : string.Empty)).ToLowerInvariant();
            string[] parts = MountBlacklist.Value.Split(',');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim().ToLowerInvariant();
                if (p.Length > 0 && n.IndexOf(p, StringComparison.Ordinal) >= 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// ACM-119 / ACNM-118 only (not full unrestricted). Safe for shared weaponOptions.
        /// </summary>
        internal static void ExpandHardpoints(WeaponManager wm)
        {
            if (wm == null)
                return;
            if (AgmTOwnedByWeXon())
            {
                AgmTWeapon.Ensure();
                AgmTWeapon.InjectIntoWeaponManager(wm);
            }
            Aam2CvWeapon.Ensure();
            Aam2CvWeapon.InjectIntoWeaponManager(wm);
        }
    }

    internal static class PluginInfo
    {
        public const string GUID = "com.iallemege.wexon";
        public const string LegacyGUID = "com.qiaochen.wexon";
        public const string Name = "WeXon";
        public const string Version = "2.5.65";
    }

    [HarmonyPatch(typeof(WeaponMount), "Initialize")]
    internal static class Patch_WeaponMount_Initialize_AgmT
    {
        [HarmonyPostfix]
        private static void Postfix(WeaponMount __instance)
        {
            if (__instance == null)
                return;
            if (Aam2CvWeapon.IsAam2CvKey(__instance.jsonKey))
                Aam2CvWeapon.RestoreMountIdentity(__instance);
            if (!Plugin.AgmTOwnedByWeXon())
                return;
            if (AgmTWeapon.IsAgmTKey(__instance.jsonKey))
                AgmTWeapon.RestoreMountIdentity(__instance);
        }
    }

    [HarmonyPatch(typeof(Encyclopedia))]
    internal static class Patch_Encyclopedia_AfterLoad
    {
        [HarmonyPostfix]
        [HarmonyPatch("AfterLoad", new Type[] { typeof(Encyclopedia) })]
        private static void PostfixStatic(Encyclopedia instance)
        {
            Plugin.InvalidateEncyclopediaCache();
            if (instance != null && Plugin.IsEncyclopediaPopulated(instance))
                Plugin.CachedEncyclopedia = instance;
            Plugin.RefreshEncyclopediaData();
        }

        [HarmonyPostfix]
        [HarmonyPatch("AfterLoad", new Type[] { })]
        private static void PostfixInstance(Encyclopedia __instance)
        {
            Plugin.InvalidateEncyclopediaCache();
            if (__instance != null && Plugin.IsEncyclopediaPopulated(__instance))
                Plugin.CachedEncyclopedia = __instance;
            Plugin.RefreshEncyclopediaData();
        }
    }

    [HarmonyPatch(typeof(EncyclopediaBrowser), "SelectMissiles")]
    internal static class Patch_EncyclopediaBrowser_SelectMissiles
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            // Always Ensure AGM-T (cheap re-register); full brand only until ready
            Plugin.RefreshEncyclopediaData();
        }
    }

    [HarmonyPatch(typeof(MissileSeeker), "Initialize")]
    internal static class Patch_Seeker_Initialize
    {
        [HarmonyPostfix]
        private static void Postfix(MissileSeeker __instance, Unit target, GlobalPosition aimpoint)
        {
            if (__instance == null)
                return;
            // Gun shells (76/127/155�?: leave vanilla optical-shell guidance alone
            if (Plugin.IsGunShellSeeker(__instance))
                return;
            Missile missile = PluginAccess.GetMissile(__instance);
            if (missile == null || Plugin.IsGunShellMissile(missile))
                return;
            Plugin.StampShipMissile(missile, null);
            // Ship edition and SARH keep vanilla seekers (no MM / LOAL).
            if (Plugin.IsShipLaunchedMissile(missile) || __instance is SARHSeeker)
            {
                Plugin.StripIncompatibleBrain(missile);
                return;
            }
            Plugin.ApplyNuke(missile, "Seeker.Initialize");
            // TBM / BallisticMissileGuidance: nuke OK, never MM / IFF �?expanded free-hunt only
            if (Plugin.IsBallisticMissile(missile) || __instance is BallisticMissileGuidance)
            {
                Plugin.SetupTbmHunt(missile);
                return;
            }
            // Cruise: never MM �?GuideTo kills TerrainWaypoint / sea-skimming
            if (Plugin.IsCruiseMissile(missile) || __instance is OpticalSeekerCruiseMissile)
            {
                Plugin.StripIncompatibleBrain(missile);
                return;
            }
            // Eyeball / AGM_scanner: TargetDetector owns scan — MM FeedSeekerLock hitch
            if (Plugin.IsScannerReconMissile(missile))
            {
                Plugin.StripIncompatibleBrain(missile);
                return;
            }
            Plugin.SetupMultiMode(missile, __instance, target, aimpoint);
            AgmDirectChaseService.Stamp(missile, __instance);
            Plugin.EnforceIffOnCurrentSeeker(missile);
        }
    }

    [HarmonyPatch(typeof(MissileSeeker), "Seek")]
    internal static class Patch_Seeker_Seek_IffGate
    {
        [HarmonyPrefix]
        private static bool Prefix(MissileSeeker __instance)
        {
            if (__instance == null)
                return true;
            if (Plugin.IsGunShellSeeker(__instance))
                return true;
            if (__instance is BallisticMissileGuidance)
                return true;
            if (__instance is OpticalSeekerCruiseMissile)
                return true;

            Missile missile = PluginAccess.GetMissile(__instance);
            if (missile == null)
                return true;
            if (Plugin.IsShipLaunchedMissile(missile) || __instance is SARHSeeker)
                return true;

            // Cheapest gate: most Seeks skip all classify + IFF until interval elapses.
            if (!Plugin.IffEnforceDue(missile))
                return true;

            if (Plugin.IsGunShellMissile(missile) || Plugin.IsBallisticMissile(missile)
                || Plugin.IsCruiseMissile(missile))
                return true;

            FieldInfo ms = Plugin.SeekerMissileField;
            if (ms != null && ms.GetValue(__instance) == null)
                ms.SetValue(__instance, missile);

            Plugin.EnforceIffOnCurrentSeeker(missile);
            return true;
        }

        [HarmonyPostfix]
        private static void Postfix(MissileSeeker __instance)
        {
            if (__instance == null)
                return;
            if (Plugin.IsGunShellSeeker(__instance)
                || __instance is BallisticMissileGuidance
                || __instance is OpticalSeekerCruiseMissile
                || __instance is SARHSeeker)
                return;
            Missile missile = PluginAccess.GetMissile(__instance);
            if (missile == null)
                return;
            if (Plugin.IsShipLaunchedMissile(missile)
                || Plugin.IsGunShellMissile(missile)
                || Plugin.IsBallisticMissile(missile)
                || Plugin.IsCruiseMissile(missile))
                return;
            // Vanilla Seek can re-lock a friendly after the 1 Hz IFF prefix.
            Plugin.EnforceIffOnCurrentSeeker(missile);
            MultiModeBrain brain = MultiModeBrain.Find(missile);
            if (brain != null)
                brain.PumpOnce();
        }
    }

    [HarmonyPatch(typeof(ARHSeeker), "Seek")]
    internal static class Patch_ARHSeeker_Seek_Guard
    {
        private static readonly FieldInfo RadarParamsField = AccessTools.Field(typeof(ARHSeeker), "radarParameters");
        private static readonly HashSet<int> ArhOkIds = new HashSet<int>();
        private static float _nextArhPrune;

        [HarmonyPrefix]
        private static bool Prefix(ARHSeeker __instance)
        {
            if (__instance == null)
                return false;
            int sid = __instance.GetInstanceID();
            if (ArhOkIds.Contains(sid))
                return true;

            if (RadarParamsField != null && RadarParamsField.GetValue(__instance) == null)
            {
                Missile missile = PluginAccess.GetMissile(__instance);
                if (missile == null)
                    return false;
                FieldInfo sf = Plugin.SeekerField;
                if (sf != null && object.ReferenceEquals(sf.GetValue(missile), __instance))
                {
                    MissileSeeker primary = missile.GetComponent<MissileSeeker>();
                    if (primary != null && !object.ReferenceEquals(primary, __instance))
                        sf.SetValue(missile, primary);
                }
                return false;
            }
            ArhOkIds.Add(sid);
            if (Time.unscaledTime >= _nextArhPrune)
            {
                _nextArhPrune = Time.unscaledTime + 60f;
                if (ArhOkIds.Count > 256)
                    ArhOkIds.Clear();
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Missile), "SetTarget")]
    internal static class Patch_Missile_SetTarget
    {
        [HarmonyPrefix]
        private static bool Prefix(Missile __instance, Unit target)
        {
            if (__instance != null && Plugin.IsGunShellMissile(__instance))
                return true;
            // Optical seekers clear lock without LOS; keep free-hunt / designated target alive
            if (target == null && __instance != null)
            {
                MultiModeBrain brain = MultiModeBrain.Find(__instance);
                TbmHuntAssist tbm = TbmHuntAssist.Find(__instance);
                if (SeekerIffGateService.ShouldBlockNullTargetClear(
                        brain != null && brain.HasActiveHuntTarget,
                        tbm != null && tbm.HasActiveTarget))
                    return false;
            }
            // Always block ejected pilots / parachute crew — never re-lock onto them.
            if (target != null && Plugin.IsEjectedPilotUnit(target))
            {
                if (Plugin.DebugLog.Value)
                    Plugin.Log.LogInfo("Blocked SetTarget on ejected pilot: " + target.name);
                return false;
            }
            if (target != null && Plugin.IsJunkHuntTarget(target)
                && (Plugin.IsKh85Family(__instance)
                    || AgmTWeapon.HasBusDispenser(__instance)
                    || AgmTWeapon.IsGs25Submunition(__instance)))
                return false;
            if (target == null)
                return true;
            Unit side = Plugin.ResolveShooterSide(__instance);
            bool allowed = Plugin.IsAllowedTarget(side, target);
            if (!SeekerIffGateService.AllowFriendlyAwareSetTarget(
                    Plugin.EnableIff.Value,
                    Plugin.BlockFriendlySetTarget.Value,
                    false,
                    allowed))
            {
                if (Plugin.DebugLog.Value)
                    Plugin.Log.LogInfo("IFF blocked SetTarget: " + target.name);
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(Missile), "MissedTarget")]
    internal static class Patch_Missile_MissedTarget
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance, ref bool __result)
        {
            if (!__result || __instance == null)
                return;
            if (Plugin.ShouldSuppressSeekerSelfDestruct(__instance))
                __result = false;
        }
    }

    [HarmonyPatch(typeof(Missile), "LosingGround")]
    internal static class Patch_Missile_LosingGround
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance, ref bool __result)
        {
            if (!__result || __instance == null)
                return;
            // Same geometry trap as MissedTarget �?MM coast/lead can false-positive after burnout
            if (Plugin.ShouldSuppressSeekerSelfDestruct(__instance))
                __result = false;
        }
    }

    /// <summary>
    /// Vanilla IR/ARH/�?SlowChecks: after motor burnout, Detonate if MissedTarget / LosingGround /
    /// speed too low / targetUnit==null. MM free-hunt + IFF often clear targetUnit �?mid-air boom.
    /// </summary>
    internal static class SeekerSelfDestructGuard
    {
        internal static bool ShouldSkipSlowChecks(MissileSeeker seeker)
        {
            if (seeker == null)
                return false;
            Missile missile = PluginAccess.GetMissile(seeker);
            return Plugin.ShouldSuppressSeekerSelfDestruct(missile);
        }
    }

    [HarmonyPatch(typeof(IRSeeker), "SlowChecks")]
    internal static class Patch_IRSeeker_SlowChecks_NoFalseSD
    {
        [HarmonyPrefix]
        private static bool Prefix(IRSeeker __instance)
        {
            return !SeekerSelfDestructGuard.ShouldSkipSlowChecks(__instance);
        }
    }

    [HarmonyPatch(typeof(ARHSeeker), "SlowChecks")]
    internal static class Patch_ARHSeeker_SlowChecks_NoFalseSD
    {
        [HarmonyPrefix]
        private static bool Prefix(ARHSeeker __instance)
        {
            return !SeekerSelfDestructGuard.ShouldSkipSlowChecks(__instance);
        }
    }

    [HarmonyPatch(typeof(SARHSeeker), "SlowChecks")]
    internal static class Patch_SARHSeeker_SlowChecks_NoFalseSD
    {
        [HarmonyPrefix]
        private static bool Prefix(SARHSeeker __instance)
        {
            return !SeekerSelfDestructGuard.ShouldSkipSlowChecks(__instance);
        }
    }

    [HarmonyPatch(typeof(ARMSeeker), "SlowChecks")]
    internal static class Patch_ARMSeeker_SlowChecks_NoFalseSD
    {
        [HarmonyPrefix]
        private static bool Prefix(ARMSeeker __instance)
        {
            return !SeekerSelfDestructGuard.ShouldSkipSlowChecks(__instance);
        }
    }

    [HarmonyPatch(typeof(LaserSeeker), "SlowChecks")]
    internal static class Patch_LaserSeeker_SlowChecks_NoFalseSD
    {
        [HarmonyPrefix]
        private static bool Prefix(LaserSeeker __instance)
        {
            return !SeekerSelfDestructGuard.ShouldSkipSlowChecks(__instance);
        }
    }

    [HarmonyPatch(typeof(OpticalSeeker), "SlowChecks")]
    internal static class Patch_OpticalSeeker_SlowChecks_NoFalseSD
    {
        [HarmonyPrefix]
        private static bool Prefix(OpticalSeeker __instance)
        {
            return !SeekerSelfDestructGuard.ShouldSkipSlowChecks(__instance);
        }
    }

    /// <summary>
    /// 175C: vanilla cruise Seek always runs. Do not skip OpticalSeekerCruiseMissile.Seek.
    /// C/E/S still skip PreTerminalMode (null-target Detonate ~6s).
    /// </summary>
    [HarmonyPatch(typeof(OpticalSeekerCruiseMissile), "PreTerminalMode")]
    internal static class Patch_CruisePreTerminal_Kh85Skip
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(OpticalSeekerCruiseMissile __instance)
        {
            Missile missile = PluginAccess.GetMissile(__instance);
            if (missile == null)
                return true;
            Plugin.Kh85Kind kind = Plugin.GetKh85Kind(missile);
            if (Kh85GuideGateService.ShouldSkipCruisePreTerminal(
                    (byte)kind,
                    kind == Plugin.Kh85Kind.NotKh85 && Plugin.IsLikelyUnstampedKh85Launch(missile)))
                return false;
            // Null-target PreTerminal Detonates ~6s (LOAL vanish). Skip for every cruise missile.
            Unit tu = PluginAccess.GetSeekerTarget(__instance);
            if (tu == null)
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(OpticalSeekerCruiseMissile), "SlowChecks")]
    internal static class Patch_CruiseSlowChecks_NoFalseSD
    {
        [HarmonyPrefix]
        private static bool Prefix(OpticalSeekerCruiseMissile __instance)
        {
            return !SeekerSelfDestructGuard.ShouldSkipSlowChecks(__instance);
        }
    }

    [HarmonyPatch(typeof(Weapon), "AttachToHardpoint")]
    internal static class Patch_Weapon_Attach_SyncInfo
    {
        [HarmonyPostfix]
        private static void Postfix(Weapon __instance, WeaponMount weaponMount)
        {
            Plugin.SyncWeaponInfoFromMount(__instance, weaponMount);
        }
    }

    [HarmonyPatch(typeof(WeaponStation), "RegisterWeapon")]
    internal static class Patch_WeaponStation_RegisterWeapon
    {
        [HarmonyPostfix]
        private static void Postfix(WeaponStation __instance, Weapon weapon, WeaponMount weaponMount)
        {
            // Guns keep their baked WeaponInfo (muzzleVelocity). Only IAL missiles sync from mount.
            if (weapon is Gun)
                return;
            Plugin.SyncWeaponInfoFromMount(weapon, weaponMount);
            if (__instance != null && weapon != null && weapon.info != null
                && (Plugin.IsIalKey(weaponMount != null ? weaponMount.jsonKey : null)
                    || Plugin.IsIalNukeCloneInfo(weapon.info)
                    || AgmTWeapon.IsAgmTMount(weaponMount)
                    || AgmTWeapon.IsAgmTInfo(weapon.info)
                    || Aam2CvWeapon.IsAam2CvMount(weaponMount)
                    || Aam2CvWeapon.IsAam2CvInfo(weapon.info)))
                __instance.WeaponInfo = weapon.info;
        }
    }

    [HarmonyPatch(typeof(MountedMissile), "Fire")]
    internal static class Patch_MountedMissile_Fire
    {
        [HarmonyPrefix]
        private static void Prefix(MountedMissile __instance)
        {
            Plugin.NoteNukeFire(__instance);
            AgmTWeapon.NoteFire(__instance);
            Aam2CvWeapon.NoteFire(__instance);
            try { Plugin.NoteShipMissileFire(__instance.attachedUnit); }
            catch { }
        }
    }

    // Missile.FixedUpdate Postfix removed (106C): Boost is Spawn-only; LowSpeed SD +
    // YieldProx TickFallback run from MultiModeBrain ~4 Hz. Optical/Laser Seek IFF duplicates
    // removed — MissileSeeker.Seek already calls EnforceIffThrottled.

    [HarmonyPatch(typeof(Missile), "Detonate")]
    [HarmonyPatch(new Type[] { typeof(Vector3), typeof(bool), typeof(bool) })]
    internal static class Patch_Missile_Detonate
    {
        [HarmonyPrefix]
        private static bool Prefix(Missile __instance)
        {
            // AGM-T bus body discard after disperse — never explode
            if (AgmTDispenser.IsSafeDiscard(__instance))
                return false;
            // Any detonate attempt on the bus — open cluster instead of HE boom
            if (AgmTDispenser.TryForceDispense(__instance, true))
                return false;

            // ACNM-118 bus: nuke only if armed + within 0.5km of intended target; else fizzle.
            // GS25: never fizzle — impact fuse needs Armed; nuke if armed, else conventional HE.
            if (AgmTWeapon.IsAcnmNuclearMissile(__instance))
            {
                if (AgmTWeapon.ShouldAllowNuclearDetonation(__instance))
                    Plugin.ArmAcnmNuclearWarhead(__instance);
                else if (AgmTWeapon.IsGs25Submunition(__instance))
                    Plugin.ForceAcnmConventionalBoom(__instance);
                else
                    Plugin.ForceAcnmNonNuclearDetonate(__instance);
                return true;
            }

            Plugin.ApplyNuke(__instance, "Detonate");
            return true;
        }
    }

    /// <summary>ACNM-118: block vanilla Missile.Arm until min flight time + safe distance.</summary>
    [HarmonyPatch(typeof(Missile), "Arm")]
    internal static class Patch_Missile_Arm_Acnm
    {
        [HarmonyPrefix]
        private static bool Prefix(Missile __instance)
        {
            if (!AgmTWeapon.IsAcnmNuclearMissile(__instance))
                return true;
            return AgmTWeapon.MeetsNuclearArmConditions(__instance);
        }
    }

    /// <summary>GS25 submunitions: ignore shock impulse (no tumble from nuclear / HE blast wave).</summary>
    [HarmonyPatch(typeof(Missile), "TakeShockwave")]
    internal static class Patch_Missile_TakeShockwave_AgmTSub
    {
        [HarmonyPrefix]
        private static bool Prefix(Missile __instance)
        {
            return !AgmTWeapon.IsShockImmuneSubmunition(__instance);
        }
    }

    /// <summary>GS25 submunitions: ignore blast HP so nuclear overpressure cannot force Detonate.</summary>
    [HarmonyPatch(typeof(Missile), "TakeDamage")]
    internal static class Patch_Missile_TakeDamage_AgmTSub
    {
        [HarmonyPrefix]
        private static void Prefix(Missile __instance, ref float blastDamage)
        {
            if (!AgmTWeapon.IsShockImmuneSubmunition(__instance))
                return;
            blastDamage = 0f;
        }
    }

    /// <summary>
    /// Nuclear Shockwave.InfluencedObject: zero push + blast for AgmT GS25 (sibling nukes included).
    /// </summary>
    [HarmonyPatch]
    internal static class Patch_Shockwave_AgmTSubImmune
    {
        private static readonly Type InfluencedType = AccessTools.Inner(typeof(Shockwave), "InfluencedObject");
        private static readonly FieldInfo DamageableField = InfluencedType != null
            ? AccessTools.Field(InfluencedType, "damageable") : null;
        private static readonly FieldInfo RbField = InfluencedType != null
            ? AccessTools.Field(InfluencedType, "rb") : null;

        static System.Reflection.MethodBase TargetMethod()
        {
            if (InfluencedType == null)
                return null;
            return AccessTools.Method(InfluencedType, "HasShockwaveReached");
        }

        [HarmonyPrefix]
        private static void Prefix(object __instance, ref float overpressure, ref float blastYield, ref float blastPower)
        {
            if (__instance == null || DamageableField == null)
                return;

            IDamageable dmg = null;
            try { dmg = DamageableField.GetValue(__instance) as IDamageable; }
            catch { return; }

            Unit u = null;
            try
            {
                if (dmg != null)
                    u = dmg.GetUnit();
            }
            catch { }

            if (u == null && RbField != null)
            {
                try
                {
                    Rigidbody rb = RbField.GetValue(__instance) as Rigidbody;
                    if (rb != null)
                        u = rb.GetComponentInParent<Unit>();
                }
                catch { }
            }

            if (!AgmTWeapon.IsShockImmuneUnit(u))
                return;

            overpressure = 0f;
            blastYield = 0f;
            blastPower = 0f;
        }
    }

    /// <summary>
    /// Sync AGM-T WeaponInfo BEFORE station lookup. Otherwise each rail registers under
    /// vanilla AAM-29 info �?separate 1-round stations (player only sees one shot).
    /// </summary>
    [HarmonyPatch(typeof(WeaponManager), "RegisterWeapon")]
    internal static class Patch_WeaponManager_RegisterWeapon_AgmT
    {
        [HarmonyPrefix]
        private static void Prefix(Weapon weapon, WeaponMount weaponMount)
        {
            if (weapon == null || weaponMount == null)
                return;
            if (weapon is Gun)
                return;
            if (Aam2CvWeapon.IsAam2CvMount(weaponMount))
            {
                try
                {
                    if (!weapon.gameObject.activeSelf)
                        weapon.gameObject.SetActive(true);
                }
                catch { }
                Aam2CvWeapon.RestoreMountIdentity(weaponMount);
                Aam2CvWeapon.SyncFromMount(weapon, weaponMount);
                return;
            }
            if (!Plugin.AgmTOwnedByWeXon())
                return;
            if (!AgmTWeapon.IsAgmTMount(weaponMount))
                return;
            try
            {
                if (!weapon.gameObject.activeSelf)
                    weapon.gameObject.SetActive(true);
            }
            catch { }
            AgmTWeapon.RestoreMountIdentity(weaponMount);
            AgmTWeapon.SyncFromMount(weapon, weaponMount);
        }
    }

    [HarmonyPatch(typeof(Spawner))]
    internal static class Patch_Spawner_SpawnMissile
    {
        // Run after Kh85MT OnSpawned so VariantTag / letter brains exist before MM GuideTo.
        [HarmonyPostfix]
        [HarmonyAfter(new string[] { "com.iallemege.kh85mt", "com.qiaochen.kh85mt" })]
        [HarmonyPatch("SpawnMissile", new Type[] { typeof(MissileDefinition), typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(Unit), typeof(Unit) })]
        private static void PostfixDef(Missile __result, Unit owner)
        {
            if (__result == null)
                return;
            Plugin.StampShipMissile(__result, owner);
            if (Plugin.IsGunShellMissile(__result) || Plugin.IsBallisticMissile(__result))
            {
                Plugin.StripIncompatibleBrain(__result);
                if (Plugin.IsGunShellMissile(__result))
                    return;
                // TBM nuke clone may still need warhead; never MultiMode — hunt assist only
                Plugin.ConsumePendingNuke(__result, "Spawn(def)");
                Plugin.ApplyNuke(__result, "Spawn(def)");
                Plugin.SetupTbmHunt(__result);
                return;
            }
            if (Plugin.IsScannerReconMissile(__result))
            {
                Plugin.StripIncompatibleBrain(__result);
                return;
            }
            Plugin.ConsumePendingNuke(__result, "Spawn(def)");
            Plugin.ApplyNuke(__result, "Spawn(def)");
            AgmTWeapon.OnSpawned(__result, owner);
            Aam2CvWeapon.OnSpawned(__result, owner);
            Plugin.BoostShipMissileLaunchVelocity(__result);
            // Kh85: Seeker.Initialize already attached MM; refresh after stamp (no early GuideTo).
            Plugin.SetupMultiMode(__result, null, null, __result.transform.position.ToGlobalPosition());
            AgmDirectChaseService.Stamp(__result, null);
            if (AgmTWeapon.HasBusDispenser(__result))
                MissileCameraBridge.TryNotifySpawn(__result);
        }

        [HarmonyPostfix]
        [HarmonyAfter(new string[] { "com.iallemege.kh85mt", "com.qiaochen.kh85mt" })]
        [HarmonyPatch("SpawnMissile", new Type[] { typeof(GameObject), typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(Unit), typeof(Unit) })]
        private static void PostfixGo(Missile __result, Unit owner)
        {
            if (__result == null)
                return;
            Plugin.StampShipMissile(__result, owner);
            if (Plugin.IsGunShellMissile(__result) || Plugin.IsBallisticMissile(__result))
            {
                Plugin.StripIncompatibleBrain(__result);
                if (Plugin.IsGunShellMissile(__result))
                    return;
                Plugin.ConsumePendingNuke(__result, "Spawn(go)");
                Plugin.ApplyNuke(__result, "Spawn(go)");
                Plugin.SetupTbmHunt(__result);
                return;
            }
            if (Plugin.IsScannerReconMissile(__result))
            {
                Plugin.StripIncompatibleBrain(__result);
                return;
            }
            Plugin.ConsumePendingNuke(__result, "Spawn(go)");
            Plugin.ApplyNuke(__result, "Spawn(go)");
            AgmTWeapon.OnSpawned(__result, owner);
            Aam2CvWeapon.OnSpawned(__result, owner);
            Plugin.BoostShipMissileLaunchVelocity(__result);
            Plugin.SetupMultiMode(__result, null, null, __result.transform.position.ToGlobalPosition());
            AgmDirectChaseService.Stamp(__result, null);
            if (AgmTWeapon.HasBusDispenser(__result))
                MissileCameraBridge.TryNotifySpawn(__result);
        }
    }

    [HarmonyPatch(typeof(Hardpoint), "SpawnMount")]
    internal static class Patch_Hardpoint_SpawnMount_AgmT
    {
        [HarmonyPostfix]
        private static void Postfix(Hardpoint __instance, Aircraft aircraft, WeaponMount weaponMount, GameObject __result)
        {
            if (__result == null || weaponMount == null)
                return;
            if (Aam2CvWeapon.IsAam2CvMount(weaponMount))
            {
                Aam2CvWeapon.RestoreMountIdentity(weaponMount);
                if (Plugin.AgmTBusCustomVisual == null || Plugin.AgmTBusCustomVisual.Value)
                    AgmTBusVisual.ApplyToHangarRack(__result, true);
                try
                {
                    Weapon[] rails = __result.GetComponentsInChildren<Weapon>(true);
                    for (int i = 0; i < rails.Length; i++)
                    {
                        Weapon w = rails[i];
                        if (w == null || w is Gun)
                            continue;
                        if (!w.gameObject.activeSelf)
                            w.gameObject.SetActive(true);
                        Aam2CvWeapon.SyncFromMount(w, weaponMount);
                    }
                }
                catch { }
                return;
            }
            if (!Plugin.AgmTOwnedByWeXon())
                return;
            if (aircraft == null || !AgmTWeapon.IsAgmTMount(weaponMount))
                return;
            AgmTWeapon.RestoreMountIdentity(weaponMount);
            if (Plugin.AgmTBusCustomVisual == null || Plugin.AgmTBusCustomVisual.Value)
                AgmTBusVisual.ApplyToHangarRack(__result);
            try
            {
                // Activate inactive rails + ensure every rail is on the AGM-T station
                Weapon[] weapons = __result.GetComponentsInChildren<Weapon>(true);
                int n = 0;
                WeaponStation agmStation = null;
                for (int i = 0; i < weapons.Length; i++)
                {
                    Weapon w = weapons[i];
                    if (w == null || w is Gun)
                        continue;
                    if (!w.gameObject.activeSelf)
                        w.gameObject.SetActive(true);
                    AgmTWeapon.SyncFromMount(w, weaponMount);
                    n++;
                }

                if (aircraft.weaponManager != null)
                {
                    for (int i = 0; i < weapons.Length; i++)
                    {
                        Weapon w = weapons[i];
                        if (w == null || w is Gun)
                            continue;
                        if (!IsWeaponOnAnyStation(aircraft, w))
                            aircraft.weaponManager.RegisterWeapon(w, weaponMount, __instance);
                    }
                }

                // Merge rails that were split across stations (AAM-29 vs AGM-T info race)
                if (aircraft.weaponStations != null && weaponMount.info != null)
                {
                    List<WeaponStation> agmStations = new List<WeaponStation>(4);
                    for (int s = 0; s < aircraft.weaponStations.Count; s++)
                    {
                        WeaponStation st = aircraft.weaponStations[s];
                        if (st == null || st.Weapons == null)
                            continue;
                        for (int w = 0; w < st.Weapons.Count; w++)
                        {
                            Weapon wp = st.Weapons[w];
                            if (wp != null && (AgmTWeapon.IsAgmTInfo(wp.info)
                                || AgmTWeapon.IsAgmTMount(weaponMount)))
                            {
                                // Only merge stations that hold weapons from THIS spawned rack
                                bool onThisRack = false;
                                for (int r = 0; r < weapons.Length; r++)
                                {
                                    if (object.ReferenceEquals(weapons[r], wp))
                                    {
                                        onThisRack = true;
                                        break;
                                    }
                                }
                                if (onThisRack)
                                {
                                    agmStations.Add(st);
                                    break;
                                }
                            }
                        }
                    }

                    if (agmStations.Count > 0)
                    {
                        agmStation = agmStations[0];
                        agmStation.WeaponInfo = weaponMount.info;
                        for (int s = 1; s < agmStations.Count; s++)
                        {
                            WeaponStation extra = agmStations[s];
                            if (extra == null || object.ReferenceEquals(extra, agmStation))
                                continue;
                            for (int w = extra.Weapons.Count - 1; w >= 0; w--)
                            {
                                Weapon wp = extra.Weapons[w];
                                if (wp == null)
                                    continue;
                                bool already = false;
                                for (int a = 0; a < agmStation.Weapons.Count; a++)
                                {
                                    if (object.ReferenceEquals(agmStation.Weapons[a], wp))
                                    {
                                        already = true;
                                        break;
                                    }
                                }
                                if (!already)
                                    agmStation.Weapons.Add(wp);
                                try { wp.SetWeaponStation(agmStation); }
                                catch { }
                                extra.Weapons.RemoveAt(w);
                            }
                            extra.WeaponInfo = weaponMount.info;
                            extra.AccountAmmo();
                        }
                        agmStation.AccountAmmo();
                    }
                }

                if (n > 0)
                    weaponMount.ammo = n;

                int ammo = -1;
                if (agmStation != null)
                    ammo = agmStation.Ammo;
                Plugin.Log.LogInfo("AGM-T SpawnMount rails=" + n
                    + " stationAmmo=" + ammo
                    + " key=" + weaponMount.jsonKey);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("AGM-T SpawnMount fix: " + ex.Message);
            }
        }

        private static bool IsWeaponOnAnyStation(Aircraft aircraft, Weapon weapon)
        {
            if (aircraft == null || weapon == null || aircraft.weaponStations == null)
                return false;
            for (int s = 0; s < aircraft.weaponStations.Count; s++)
            {
                WeaponStation st = aircraft.weaponStations[s];
                if (st == null || st.Weapons == null)
                    continue;
                for (int w = 0; w < st.Weapons.Count; w++)
                {
                    if (object.ReferenceEquals(st.Weapons[w], weapon))
                        return true;
                }
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    internal static class Patch_WeaponManager_Awake
    {
        [HarmonyPostfix]
        private static void Postfix(WeaponManager __instance)
        {
            Plugin.EnsureNukeMountClones();
            // Only ACM-119 / ACNM-118 onto aircraft pylons (not full mount dump)
            Plugin.ExpandHardpoints(__instance);
        }
    }

    [HarmonyPatch(typeof(WeaponSelector), "Initialize")]
    [HarmonyPatch(new Type[] { typeof(Aircraft), typeof(HardpointSet), typeof(FactionHQ), typeof(Airbase) })]
    internal static class Patch_WeaponSelector_Initialize
    {
        [HarmonyPrefix]
        private static void Prefix(Aircraft aircraft)
        {
            if (!Plugin.NukeMountsInjected)
                Plugin.EnsureNukeMountClones();
            // Always Ensure �?repair null prefabs + re-register after AfterLoad
            AgmTWeapon.Ensure();
            Aam2CvWeapon.Ensure();
            AgmTWeapon.RepairBrokenPrefabs();
            Plugin.EnterPlayerUnrestricted();
        }

        [HarmonyPostfix]
        private static void Postfix()
        {
            Plugin.ExitPlayerUnrestricted();
        }
    }

    /// <summary>
    /// Spawn / rearm validates loadout via VetLoadout �?MountAllowed*.
    /// Hangar UI already used unrestricted (GetAvailableWeapons), but VetLoadout ran with
    /// depth=0 and stripped off-hardpoint picks (e.g. AGM-99 on a pylon that only listed AAMs).
    /// </summary>
    [HarmonyPatch(typeof(WeaponChecker), "VetLoadout")]
    internal static class Patch_VetLoadout_Unrestricted
    {
        [HarmonyPrefix]
        private static void Prefix(Player player)
        {
            if (Plugin.UnrestrictedFeatureOn() && Plugin.IsLocalHumanPlayer(player))
            {
                Plugin.LocalHumanWeaponsQuery = true;
                Plugin.LocalHumanWeaponsUntil = Time.unscaledTime + 5f;
                Plugin.EnterPlayerUnrestricted();
            }
        }

        [HarmonyPostfix]
        private static void Postfix(Player player)
        {
            if (Plugin.UnrestrictedFeatureOn() && Plugin.IsLocalHumanPlayer(player))
            {
                Plugin.ExitPlayerUnrestricted();
                Plugin.LocalHumanWeaponsUntil = Time.unscaledTime + 2f;
                Plugin.LocalHumanWeaponsQuery = false;
            }
        }
    }

    /// <summary>Belt-and-suspenders: never reject local unrestricted mounts at VetWeapon.</summary>
    [HarmonyPatch(typeof(WeaponChecker), "VetWeapon")]
    internal static class Patch_VetWeapon_Unrestricted
    {
        [HarmonyPrefix]
        private static bool Prefix(
            WeaponMount requestedMount,
            HardpointSet hardpointSet,
            Player player,
            ref bool __result,
            ref string failReason,
            ref int failCost)
        {
            if (Plugin.IsNavalHardpoint(hardpointSet) && Aam2CvWeapon.IsAam2CvMount(requestedMount))
            {
                __result = false;
                failReason = "AAM-2CV blocked on naval hardpoint";
                failCost = 0;
                return false;
            }
            if (Plugin.IsLocalHumanPlayer(player)
                && (AgmTWeapon.IsAgmTMount(requestedMount)
                    || Aam2CvWeapon.IsAam2CvMount(requestedMount)
                    || Plugin.IsKh85Mount(requestedMount))
                && !Plugin.IsNavalHardpoint(hardpointSet))
            {
                __result = true;
                failReason = null;
                failCost = 0;
                return false;
            }

            if (!Plugin.UnrestrictedFeatureOn() || !Plugin.IsLocalHumanPlayer(player))
                return true;

            if (Plugin.BlockIalOnShips.Value && Plugin.IsIalMount(requestedMount)
                && Plugin.IsNavalHardpoint(hardpointSet))
            {
                __result = false;
                failReason = "IAL blocked on naval hardpoint";
                failCost = 0;
                return false;
            }

            __result = true;
            failReason = null;
            failCost = 0;
            return false;
        }
    }

    [HarmonyPatch(typeof(WeaponChecker), "MountAllowedHardpoint")]
    internal static class Patch_MountAllowedHardpoint
    {
        [HarmonyPrefix]
        private static bool Prefix(WeaponMount mount, HardpointSet hardpointSet, ref bool __result)
        {
            LoadoutMountGateService.HardpointPath path = LoadoutMountGateService.ResolveHardpointPrefix(
                Plugin.BlockIalOnShips.Value,
                Plugin.IsIalMount(mount),
                Plugin.IsNavalHardpoint(hardpointSet),
                AgmTWeapon.IsAgmTMount(mount),
                Aam2CvWeapon.IsAam2CvMount(mount),
                Plugin.IsKh85Mount(mount),
                Plugin.AllowPlayerUnrestricted());
            if (path == LoadoutMountGateService.HardpointPath.ForceDeny)
            {
                __result = false;
                return false;
            }
            if (path == LoadoutMountGateService.HardpointPath.ForceAllow)
            {
                __result = true;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(WeaponChecker), "MountAllowedHQ")]
    internal static class Patch_MountAllowedHQ
    {
        [HarmonyPrefix]
        private static bool Prefix(ref bool __result)
        {
            if (LoadoutMountGateService.ResolveUnrestrictedBypass(Plugin.AllowPlayerUnrestricted())
                == LoadoutMountGateService.UnrestrictedBypassPath.RunVanilla)
                return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(WeaponChecker), "MountAllowedAirbase")]
    internal static class Patch_MountAllowedAirbase
    {
        [HarmonyPrefix]
        private static bool Prefix(ref bool __result)
        {
            if (LoadoutMountGateService.ResolveUnrestrictedBypass(Plugin.AllowPlayerUnrestricted())
                == LoadoutMountGateService.UnrestrictedBypassPath.RunVanilla)
                return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(WeaponChecker), "MountAllowedConflict")]
    internal static class Patch_MountAllowedConflict
    {
        [HarmonyPrefix]
        private static bool Prefix(ref bool __result)
        {
            if (LoadoutMountGateService.ResolveUnrestrictedBypass(Plugin.AllowPlayerUnrestricted())
                == LoadoutMountGateService.UnrestrictedBypassPath.RunVanilla)
                return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(WeaponChecker), "MountAllowedNuclear")]
    internal static class Patch_MountAllowedNuclear
    {
        [HarmonyPrefix]
        private static bool Prefix(WeaponMount mount, Player player, ref bool __result)
        {
            LoadoutMountGateService.NuclearPath path = LoadoutMountGateService.ResolveNuclearPrefix(
                Plugin.IsIalExemptFromWarheadQuota(mount),
                player != null,
                Plugin.UnrestrictedFeatureOn(),
                Plugin.AllowPlayerUnrestricted(),
                Plugin.IsLocalHumanPlayer(player));
            if (path == LoadoutMountGateService.NuclearPath.ForceAllow)
            {
                __result = true;
                return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Faction warhead quota uses GetCurrentWarheads for UI, spawn consume, and rearm.
    /// Exclude IAL clones so they do not block takeoff or drain stockpile.
    /// </summary>
    [HarmonyPatch(typeof(WeaponManager), "GetCurrentWarheads")]
    internal static class Patch_GetCurrentWarheads
    {
        [HarmonyPostfix]
        private static void Postfix(WeaponManager __instance, ref int __result)
        {
            if (__result <= 0 || __instance == null)
                return;
            Aircraft ac = null;
            if (Plugin.WeaponManagerAircraftField != null)
            {
                try { ac = Plugin.WeaponManagerAircraftField.GetValue(__instance) as Aircraft; }
                catch { }
            }
            if (ac == null)
                ac = __instance.GetComponentInParent<Aircraft>();
            if (ac == null || ac.weaponStations == null)
                return;

            int subtract = 0;
            for (int i = 0; i < ac.weaponStations.Count; i++)
            {
                WeaponStation station = ac.weaponStations[i];
                if (station == null || station.WeaponInfo == null)
                    continue;
                if (!Plugin.IsIalExemptFromWarheadQuota(station.WeaponInfo))
                    continue;
                subtract += station.Ammo;
            }
            if (subtract > 0)
                __result = SeekerIffGateService.ApplyWarheadQuotaSubtract(__result, subtract);
        }
    }

    [HarmonyPatch(typeof(WeaponChecker), "PreferNukesFilter")]
    internal static class Patch_PreferNukesFilter
    {
        /// <summary>
        /// Saved conventional mounts when briefly allowing vanilla nuke preference.
        /// Restored if vanilla empties the list (e.g. warheadsAvailable == 0).
        /// </summary>
        private static List<WeaponMount> SavedConventionals;

        /// <summary>
        /// Vanilla PreferNukesFilter strips all conventional options whenever any nuke fits.
        /// AiNukeChance is independent of warhead stock because IAL [10kt] does not consume it.
        /// When stock is empty but the roll succeeds, keep IAL only (strip Genie-style stock nukes).
        /// </summary>
        [HarmonyPrefix]
        private static bool Prefix(int warheadsAvailable, HardpointSet hardpointSet,
            List<WeaponMount> listToFilter, ref bool __state)
        {
            __state = false;
            SavedConventionals = null;
            if (listToFilter == null)
                return true;

            PreferNukesGateService.Path path = PreferNukesGateService.ResolvePrefix(
                Plugin.IsHumanWeaponContext(),
                Plugin.BlockIalOnShips.Value,
                Plugin.IsNavalHardpoint(hardpointSet),
                warheadsAvailable,
                Plugin.RollAiNukePreference());

            if (path == PreferNukesGateService.Path.SkipVanillaKeepList)
                return false;

            if (path == PreferNukesGateService.Path.StripNukesKeepConventional)
            {
                Plugin.RemoveNuclearFromList(listToFilter);
                return false;
            }

            if (path == PreferNukesGateService.Path.KeepIalStripStockNukes)
            {
                Plugin.RemoveStockpileNukesKeepIal(listToFilter);
                return false;
            }

            // Snapshot conventionals before vanilla may wipe them
            SavedConventionals = new List<WeaponMount>(listToFilter.Count);
            for (int i = 0; i < listToFilter.Count; i++)
            {
                WeaponMount m = listToFilter[i];
                if (m == null || m.info == null)
                    continue;
                if (Plugin.IsIalMount(m) || m.info.nuclear)
                    continue;
                SavedConventionals.Add(m);
            }

            __state = true;
            return true;
        }

        [HarmonyPostfix]
        private static void Postfix(HardpointSet hardpointSet, List<WeaponMount> listToFilter, bool __state)
        {
            if (listToFilter == null)
            {
                SavedConventionals = null;
                return;
            }

            if (Plugin.IsHumanWeaponContext())
            {
                SavedConventionals = null;
                return;
            }

            if (Plugin.BlockIalOnShips.Value && Plugin.IsNavalHardpoint(hardpointSet))
            {
                Plugin.RemoveNuclearFromList(listToFilter);
                SavedConventionals = null;
                return;
            }

            if (!__state)
            {
                // Prefix already finalized (strip-all, keep-IAL-only, or human skip).
                SavedConventionals = null;
                return;
            }

            // Nuke path: if vanilla left nothing selectable, fall back to conventional
            int savedCount = SavedConventionals != null ? SavedConventionals.Count : 0;
            if (PreferNukesGateService.ShouldRestoreConventionals(__state, listToFilter.Count, savedCount))
            {
                HashSet<WeaponMount> have = new HashSet<WeaponMount>(listToFilter);
                for (int i = 0; i < SavedConventionals.Count; i++)
                {
                    WeaponMount m = SavedConventionals[i];
                    if (m != null && have.Add(m))
                        listToFilter.Add(m);
                }
            }

            // Do NOT inject every IAL clone onto every hardpoint — that broke AI loadouts
            SavedConventionals = null;
        }
    }

    [HarmonyPatch(typeof(WeaponChecker), "MountAllowedCost")]
    internal static class Patch_MountAllowedCost
    {
        [HarmonyPrefix]
        private static bool Prefix(ref bool __result)
        {
            if (LoadoutMountGateService.ResolveUnrestrictedBypass(Plugin.AllowPlayerUnrestricted())
                == LoadoutMountGateService.UnrestrictedBypassPath.RunVanilla)
                return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(WeaponChecker), "GetAvailableWeaponsNonAlloc")]
    internal static class Patch_GetAvailableWeapons
    {
        // Hangar hits this per hardpoint �?reuse to cut GC spikes.
        private static readonly HashSet<WeaponMount> HaveScratch = new HashSet<WeaponMount>();

        [HarmonyPrefix]
        private static void Prefix(Player player)
        {
            // Local human only �?AI faction Players must not raise unrestricted depth
            bool local = Plugin.IsLocalHumanPlayer(player);
            Plugin.LocalHumanWeaponsQuery = local;
            if (local)
            {
                Plugin.LocalHumanWeaponsUntil = Time.unscaledTime + 2f;
                Plugin.EnterPlayerUnrestricted();
            }
        }

        [HarmonyPostfix]
        private static void Postfix(Player player, HardpointSet hardpointSet, List<WeaponMount> outAvailable)
        {
            try
            {
                if (outAvailable == null)
                    return;

                bool naval = Plugin.BlockIalOnShips.Value && Plugin.IsNavalHardpoint(hardpointSet);
                if (naval)
                    Plugin.RemoveIalFromList(outAvailable);

                // Never re-scan / re-inject here �?hangar calls this per hardpoint
                if (Plugin.CachedMounts.Count == 0 && Plugin.AllowPlayerUnrestricted())
                    Plugin.RefreshMountCache();

                HaveScratch.Clear();
                for (int i = 0; i < outAvailable.Count; i++)
                {
                    WeaponMount existing = outAvailable[i];
                    if (existing != null)
                        HaveScratch.Add(existing);
                }
                if (Plugin.AllowPlayerUnrestricted())
                {
                    for (int i = 0; i < Plugin.CachedMounts.Count; i++)
                    {
                        WeaponMount m = Plugin.CachedMounts[i];
                        // Null/destroyed prefab — Hardpoint.SpawnMount NRE and empties the whole loadout
                        if (m == null || m.prefab == null || !HaveScratch.Add(m))
                            continue;
                        if (naval && Plugin.IsIalMount(m))
                            continue;
                        outAvailable.Add(m);
                    }
                }
                if (Plugin.EnableAgmT != null && Plugin.EnableAgmT.Value && !naval
                    && Plugin.IsLocalHumanPlayer(player))
                {
                    AgmTWeapon.AppendMountsToList(outAvailable, HaveScratch);
                }
                if (!Plugin.IsNavalHardpoint(hardpointSet) && Plugin.IsLocalHumanPlayer(player))
                {
                    Aam2CvWeapon.AppendMountsToList(outAvailable, HaveScratch);
                }
            }
            finally
            {
                if (Plugin.IsLocalHumanPlayer(player))
                {
                    Plugin.ExitPlayerUnrestricted();
                    Plugin.LocalHumanWeaponsUntil = Time.unscaledTime + 2f;
                }
                Plugin.LocalHumanWeaponsQuery = false;
            }
        }
    }

    /// <summary>Last line of defense: ships must not launch IAL / [10kt] variants.</summary>
    [HarmonyPatch(typeof(Weapon), "Fire")]
    internal static class Patch_Weapon_Fire_BlockShipIal
    {
        [HarmonyPrefix]
        private static bool Prefix(Weapon __instance, Unit owner)
        {
            if (Plugin.IsShipUnit(owner) && __instance != null
                && Aam2CvWeapon.IsAam2CvInfo(__instance.info))
            {
                if (Plugin.DebugLog != null && Plugin.DebugLog.Value)
                    Plugin.Log.LogInfo("Blocked AAM-2CV fire from ship: " + __instance.name);
                return false;
            }
            if (Plugin.BlockIalOnShips.Value && __instance != null
                && Plugin.IsShipUnit(owner) && Plugin.IsIalWeapon(__instance))
            {
                if (Plugin.DebugLog.Value)
                    Plugin.Log.LogInfo("Blocked IAL fire from ship: " + __instance.name);
                return false;
            }
            Plugin.NoteShipMissileFire(owner);
            return true;
        }
    }

    internal static class PluginAccess
    {
        private static readonly FieldInfo SeekerMissileField = AccessTools.Field(typeof(MissileSeeker), "missile");
        private static readonly FieldInfo SeekerTargetField = AccessTools.Field(typeof(MissileSeeker), "targetUnit");

        internal static Missile GetMissile(MissileSeeker seeker)
        {
            if (seeker == null)
                return null;
            Missile missile = SeekerMissileField != null ? SeekerMissileField.GetValue(seeker) as Missile : null;
            if (missile == null)
                missile = seeker.GetComponentInParent<Missile>();
            return missile;
        }

        internal static Unit GetSeekerTarget(MissileSeeker seeker)
        {
            if (seeker == null || SeekerTargetField == null)
                return null;
            try { return SeekerTargetField.GetValue(seeker) as Unit; }
            catch { return null; }
        }
    }

    /// <summary>
    /// Piledriver TBM free-hunt: larger radius, feed BallisticMissileGuidance knownPos only.
    /// Does not GuideTo / change gLimit (those broke loft steering).
    /// </summary>
    public sealed class TbmHuntAssist : MonoBehaviour
    {
        private static readonly Dictionary<int, TbmHuntAssist> ByMissileId = new Dictionary<int, TbmHuntAssist>(16);
        private static readonly FieldInfo SeekerTargetField = AccessTools.Field(typeof(MissileSeeker), "targetUnit");
        private static readonly FieldInfo KnownPosField = AccessTools.Field(typeof(BallisticMissileGuidance), "knownPos");
        private static readonly FieldInfo KnownVelField = AccessTools.Field(typeof(BallisticMissileGuidance), "knownVel");

        private Missile _missile;
        private BallisticMissileGuidance _guidance;
        private Unit _target;
        private float _nextHunt;
        private int _lastFrame = -1;
        private readonly List<Unit> _buf = new List<Unit>(128);
        private readonly HashSet<Unit> _seen = new HashSet<Unit>();

        public bool HasActiveTarget
        {
            get { return _target != null && Plugin.IsUnitAlive(_target); }
        }

        internal static TbmHuntAssist Find(Missile missile)
        {
            if (missile == null)
                return null;
            TbmHuntAssist a;
            if (ByMissileId.TryGetValue(missile.GetInstanceID(), out a) && a != null)
                return a;
            return null;
        }

        private static readonly List<TbmHuntAssist> PumpScratch = new List<TbmHuntAssist>(16);

        internal static void PumpAll()
        {
            if (ByMissileId.Count == 0)
                return;
            PumpScratch.Clear();
            Dictionary<int, TbmHuntAssist>.ValueCollection vals = ByMissileId.Values;
            foreach (TbmHuntAssist a in vals)
            {
                if (a != null)
                    PumpScratch.Add(a);
            }
            for (int i = 0; i < PumpScratch.Count; i++)
                PumpScratch[i].PumpOnce();
            PumpScratch.Clear();
        }

        internal void PumpOnce()
        {
            TickHunt();
        }

        public void Setup(Missile missile)
        {
            _missile = missile;
            if (missile != null)
                ByMissileId[missile.GetInstanceID()] = this;
            _guidance = missile != null ? missile.GetComponent<BallisticMissileGuidance>() : null;
            if (_guidance == null && missile != null)
                _guidance = missile.GetComponentInChildren<BallisticMissileGuidance>();
            _nextHunt = 0f;

            Unit designated = Plugin.ResolveDesignatedTarget(missile);
            if (designated != null && Plugin.IsHostileHuntTarget(missile, designated))
            {
                _target = designated;
                ApplyTarget(_target);
            }
            else
                TryHunt(true);
        }

        private void OnDestroy()
        {
            if (_missile != null)
                ByMissileId.Remove(_missile.GetInstanceID());
        }

        private void FixedUpdate()
        {
            TickHunt();
        }

        private void TickHunt()
        {
            if (_missile == null || _missile.disabled)
                return;
            int frame = Time.frameCount;
            if (_lastFrame == frame)
                return;
            _lastFrame = frame;

            if (_target != null && Plugin.IsUnitAlive(_target) && Plugin.IsHostileHuntTarget(_missile, _target))
            {
                ApplyTarget(_target);
                return;
            }

            _target = null;
            Unit designated = Plugin.ResolveDesignatedTarget(_missile);
            if (designated != null && Plugin.IsHostileHuntTarget(_missile, designated))
            {
                _target = designated;
                ApplyTarget(_target);
                return;
            }

            if (Time.time >= _nextHunt)
                TryHunt(false);
            else if (_target != null)
                ApplyTarget(_target);
        }

        private void TryHunt(bool force)
        {
            if (!Plugin.AllowFreeAttack.Value || _missile == null)
                return;
            // F9 drop already has a spawn target; 60 km retarget yanks it into a flat turn.
            if (F9DropMark.Has(_missile))
                return;
            if (!TbmHuntMathService.HuntDue(Time.time, _nextHunt, force))
                return;
            if (!Plugin.TryConsumeHuntQuery())
                return;
            _nextHunt = TbmHuntMathService.ScheduleNextHunt(
                Time.time,
                Plugin.EffectiveHuntInterval());

            float radius = HuntRangeGateService.ResolveTbmHuntRadiusM(
                Plugin.TbmHuntRadius != null ? Plugin.TbmHuntRadius.Value : TbmHuntMathService.DefaultRadiusM,
                Plugin.EffectiveHuntRadius(_missile));

            GlobalPosition origin;
            try { origin = _missile.GlobalPosition(); }
            catch { origin = _missile.transform.position.ToGlobalPosition(); }

            _buf.Clear();
            _seen.Clear();
            try { BattlefieldGrid.GetUnitsInRangeNonAlloc(origin, radius, _buf); }
            catch { }

            Unit best = null;
            float bestDist = float.MaxValue;
            Vector3 mpos = _missile.transform.position;
            // Grid only �?full UnitRegistry.allUnits scans were a major hitch.
            Consider(_buf, mpos, radius, ref best, ref bestDist);

            if (best == null)
                return;

            _target = best;
            ApplyTarget(best);
            if (Plugin.DebugLog.Value)
                Plugin.Log.LogInfo("TBM hunt: " + best.name + " d=" + bestDist.ToString("0") + " r=" + radius.ToString("0"));
        }

        private void Consider(List<Unit> units, Vector3 mpos, float radius, ref Unit best, ref float bestDist)
        {
            if (units == null)
                return;
            for (int i = 0; i < units.Count; i++)
            {
                Unit u = units[i];
                if (u == null || !_seen.Add(u))
                    continue;
                if (!Plugin.IsHostileHuntTarget(_missile, u))
                    continue;
                // TBM: surface targets (ships / buildings / vehicles), not aircraft or other missiles
                if (!TbmHuntMathService.IsSurfaceHuntUnit(
                        u is Missile, u is Aircraft, Plugin.IsSurfaceUnit(u)))
                    continue;

                float dist = Vector3.Distance(mpos, u.transform.position);
                if (!TbmHuntMathService.IsBetterCandidate(dist, radius, bestDist))
                    continue;
                bestDist = dist;
                best = u;
            }
        }

        private void ApplyTarget(Unit target)
        {
            if (_missile == null || target == null)
                return;
            try
            {
                if (SeekerTargetField != null && _guidance != null)
                    SeekerTargetField.SetValue(_guidance, target);
                try { _missile.SetTarget(target); }
                catch { }

                GlobalPosition gp = target.GlobalPosition();
                Vector3 vel = target.rb != null
                    ? Vector3.ClampMagnitude(target.rb.velocity, 20f)
                    : Vector3.zero;
                if (KnownPosField != null && _guidance != null)
                    KnownPosField.SetValue(_guidance, gp);
                if (KnownVelField != null && _guidance != null)
                    KnownVelField.SetValue(_guidance, vel);
            }
            catch { }
        }
    }

    public sealed class MultiModeBrain : MonoBehaviour
    {
        private static readonly Dictionary<int, MultiModeBrain> BrainsByMissileId = new Dictionary<int, MultiModeBrain>(64);
        /// <summary>target instanceId -> brain currently hunting it (same-owner claim checks).</summary>
        private static readonly Dictionary<int, MultiModeBrain> ClaimByTargetId = new Dictionary<int, MultiModeBrain>(64);
        private static readonly Dictionary<string, FieldInfo> SeekerFieldCache = new Dictionary<string, FieldInfo>(32);

        private Missile _missile;
        private int _missileId;
        private int _claimedTargetId;
        private MissileSeeker _primary;
        private MissileSeeker _secondary;
        private Unit _target;
        private GlobalPosition _aimpoint;
        private GlobalPosition _lastKnownAim;
        private Vector3 _lastKnownVel;
        private bool _hasLastKnownAim;
        private bool _terminal;
        private bool _ready;
        private bool _guidanceOverride;
        private bool _playerDesignated;
        /// <summary>
        /// Once a player/designated lock is taken, never free-hunt / MadMode retarget another unit.
        /// Survives target death (coast last known aimpoint).
        /// </summary>
        private bool _stickyOnly;
        private float _originalGLimit;
        private bool _hasOriginalGLimit;
        private float _nextHunt;
        private float _nextDesignatedCheck;
        private float _softRetargetUntil;
        /// <summary>Set when coasting / lost lock so the next GuideTo does not snap 180° and stall.</summary>
        private bool _needsRelockSoft;
        private Vector3 _lastCmdDir;
        private float _lastGuideTime;
        private float _nextFinBoost;
        private float _nextStickyGuide;
        private float _nextLightSync;
        private float _nextSlowTick;
        private float _nextSafetyTick;
        private bool _opticalLight;
        private int _lastTargetId;
        private int _lastBeforeFrame = -1;
        private int _tickFrame = -1;
        /// <summary>Frame when GuideTo last wrote aim — skip duplicate Reapply GuideTo same FixedUpdate.</summary>
        private int _aimSyncedFrame = -1;
        /// <summary>Target id that received full seeker warm (bools / IR / ARH); hot path only dirty aim fields.</summary>
        private int _seekerWarmTargetId;
        private readonly List<Unit> _huntBuf = new List<Unit>(128);
        private readonly HashSet<Unit> _huntSeen = new HashSet<Unit>();
        private static readonly FieldInfo SeekerTargetField = AccessTools.Field(typeof(MissileSeeker), "targetUnit");
        private static readonly FieldInfo SeekerField = AccessTools.Field(typeof(Missile), "seeker");
        private static readonly FieldInfo FinAreaField = AccessTools.Field(typeof(Missile), "finArea");
        private static readonly FieldInfo CurrentFinAreaField = AccessTools.Field(typeof(Missile), "currentFinArea");
        private static readonly FieldInfo GLimitField = AccessTools.Field(typeof(Missile), "gLimit");
        private static readonly FieldInfo MissileGLimitField = AccessTools.Field(typeof(Missile), "gLimit");

        public bool IsHunting
        {
            get
            {
                return MultiModeHuntGateService.IsHunting(
                    Plugin.AllowFreeAttack.Value,
                    _stickyOnly,
                    _playerDesignated,
                    _target != null && IsAlive(_target));
            }
        }

        /// <summary>Fired without a weapon lock — still searching. Aircraft TGP is not this missile's lock.</summary>
        public bool IsOpenLoalSearch
        {
            get
            {
                return Plugin.AllowFreeAttack != null && Plugin.AllowFreeAttack.Value
                    && !_stickyOnly && !_playerDesignated;
            }
        }

        public bool HasGuidanceOverride
        {
            get { return _guidanceOverride; }
        }

        /// <summary>True while brain is guiding to a live target (blocks seeker SetTarget(null) / LoseLock).</summary>
        public bool HasActiveHuntTarget
        {
            // Player/designated locks stay sticky — do not re-gate every frame with CanEngage
            // (IFF HQ flicker + IR LoseLock was wiping targetID — ARH/IR aim straight ahead).
            // Short-circuit: do NOT evaluate CanEngage when sticky (C# always evaluates args).
            get
            {
                if (_target == null || !IsAlive(_target))
                    return false;
                if (_playerDesignated || _stickyOnly)
                    return !IsConfirmedFriendly(_target);
                return CanEngage(_target, true);
            }
        }

        private bool _deployCalled;

        private bool IsConfirmedFriendly(Unit candidate)
        {
            if (!Plugin.EnableIff.Value || candidate == null || _missile == null)
                return false;
            return Plugin.IsSameFaction(Plugin.ResolveShooterSide(_missile), candidate);
        }

        /// <summary>Mark this missile as sticky-only: never MadMode / free-hunt another unit.</summary>
        private void MarkSticky(Unit target)
        {
            _playerDesignated = true;
            _stickyOnly = true;
            if (target != null)
                _target = target;
            RefreshTargetClaim();
        }

        /// <summary>Stop overriding aimpoint for free-hunt; keep sticky player lock identity.</summary>
        private void ReleaseGuidanceToVanilla()
        {
            _guidanceOverride = false;
            _needsRelockSoft = true;
            if (!_playerDesignated && !_stickyOnly)
                _target = null;
            RefreshTargetClaim();
        }

        /// <summary>Fully drop live Unit lock (dead / confirmed friendly). Sticky-only missiles keep coasting last aim.</summary>
        private void DropTarget()
        {
            _guidanceOverride = false;
            _needsRelockSoft = true;
            _target = null;
            _playerDesignated = false;
            _seekerWarmTargetId = 0;
            // Keep _stickyOnly — never open free-hunt after a designated shot loses its Unit.
            RefreshTargetClaim();
        }

        /// <summary>Coast sticky last-known aimpoint; never acquire a different Unit.</summary>
        private void CoastStickyLastKnown()
        {
            if (!_hasLastKnownAim || _missile == null)
            {
                _guidanceOverride = false;
                return;
            }
            if (Plugin.ShouldDeferKh85GuideTo(_missile))
            {
                _guidanceOverride = false;
                return;
            }
            try
            {
                _missile.SetAimpoint(_lastKnownAim, _lastKnownVel);
                _aimpoint = _lastKnownAim;
                _guidanceOverride = true;
                _aimSyncedFrame = Time.frameCount;
                _needsRelockSoft = true;
            }
            catch { _guidanceOverride = false; _needsRelockSoft = true; }
        }

        internal static MultiModeBrain Find(Missile missile)
        {
            if (missile == null)
                return null;
            MultiModeBrain brain;
            if (BrainsByMissileId.TryGetValue(missile.GetInstanceID(), out brain) && brain != null)
                return brain;
            return null;
        }

        private static readonly List<MultiModeBrain> PumpScratch = new List<MultiModeBrain>(64);

        /// <summary>
        /// Packed payload: Unity may skip Update on extra MBs. HostedTick + Seek postfix call this.
        /// </summary>
        internal static void PumpAll()
        {
            if (BrainsByMissileId.Count == 0)
                return;
            PumpScratch.Clear();
            Dictionary<int, MultiModeBrain>.ValueCollection vals = BrainsByMissileId.Values;
            foreach (MultiModeBrain b in vals)
            {
                if (b != null)
                    PumpScratch.Add(b);
            }
            for (int i = 0; i < PumpScratch.Count; i++)
                PumpScratch[i].PumpOnce();
            PumpScratch.Clear();
        }

        internal void PumpOnce()
        {
            TickSlow();
        }

        private void RegisterBrain()
        {
            if (_missile == null)
                return;
            _missileId = _missile.GetInstanceID();
            BrainsByMissileId[_missileId] = this;
        }

        private void OnDestroy()
        {
            ClearTargetClaim();
            if (_missileId != 0)
                BrainsByMissileId.Remove(_missileId);
        }

        private void RefreshTargetClaim()
        {
            int newId = 0;
            if (_target != null)
            {
                try { newId = _target.GetInstanceID(); }
                catch { newId = 0; }
            }
            if (newId == _claimedTargetId)
                return;
            ClearTargetClaim();
            if (newId == 0)
                return;
            // Never steal a claim held by a sticky sibling (different Unit chase / claim stealing).
            MultiModeBrain holder;
            bool holderExists = ClaimByTargetId.TryGetValue(newId, out holder) && holder != null;
            if (MultiModeClaimGateService.ShouldSkipClaimSteal(
                    holderExists,
                    holderExists && object.ReferenceEquals(holder, this),
                    holderExists && holder._stickyOnly,
                    holderExists && SameOwnerAs(holder)))
                return;
            _claimedTargetId = newId;
            ClaimByTargetId[newId] = this;
        }

        private bool SameOwnerAs(MultiModeBrain other)
        {
            if (other == null || _missile == null || other._missile == null)
                return false;
            Unit myOwner = _missile.owner;
            return myOwner != null && other._missile.owner != null
                && object.ReferenceEquals(myOwner, other._missile.owner);
        }

        private void ClearTargetClaim()
        {
            if (_claimedTargetId == 0)
                return;
            MultiModeBrain cur;
            if (ClaimByTargetId.TryGetValue(_claimedTargetId, out cur) && object.ReferenceEquals(cur, this))
                ClaimByTargetId.Remove(_claimedTargetId);
            _claimedTargetId = 0;
        }

        public void Setup(Missile missile, MissileSeeker primary, Unit target, GlobalPosition aimpoint)
        {
            _missile = missile;
            RegisterBrain();
            // Warm Kh85Kind cache once at attach (hot paths use GetKh85Kind).
            if (missile != null)
                Plugin.GetKh85Kind(missile);
            _primary = primary;
            _aimpoint = aimpoint;
            _nextHunt = 0f;
            _nextDesignatedCheck = 0f;
            _nextFinBoost = 0f;
            _nextStickyGuide = 0f;
            _nextLightSync = 0f;
            _nextSlowTick = 0f;
            _nextSafetyTick = 0f;
            _guidanceOverride = false;
            _playerDesignated = false;
            _stickyOnly = false;
            _hasLastKnownAim = false;
            _aimSyncedFrame = -1;
            _seekerWarmTargetId = 0;
            _deployCalled = false;
            _opticalLight = false;

            if (_primary == null)
            {
                if (SeekerField != null)
                    _primary = SeekerField.GetValue(missile) as MissileSeeker;
                if (_primary == null)
                    _primary = missile.GetComponent<MissileSeeker>();
            }

            // 0.7-style light MM for Optical/Laser: lock assist only, vanilla Seek steers.
            _opticalLight = MissileClassifyGateService.IsOpticalOrLaserSeeker(_primary)
                && !(_primary is OpticalSeekerCruiseMissile)
                && !(_primary is OpticalSeekerShell);

            if (SeekerField != null && _primary != null)
                SeekerField.SetValue(missile, _primary);

            // Optical light: never add secondary ARH (0.7 terminal switch was optional; secondary fights Seek).
            if (!_opticalLight && Plugin.EnableSecondarySeeker.Value)
                EnsureSecondary();

            if (MissileGLimitField != null)
            {
                try
                {
                    _originalGLimit = (float)MissileGLimitField.GetValue(_missile);
                    _hasOriginalGLimit = true;
                }
                catch { _hasOriginalGLimit = false; }
            }
            _ready = _primary != null;

            // Weapon lock only: spawn/Initialize target + missile.target/targetID.
            // Do NOT steal aircraft TGP/radar primary (that made AAM sticky on tanks and killed LOAL).
            _target = (target != null && CanEngage(target, false)
                && Plugin.TargetFitsSeekerFamily(missile, _primary, target)) ? target : null;
            if (_target == null)
            {
                Unit own = Plugin.ResolveMissileOwnTarget(missile);
                // LOAL spawn (no Initialize lock): never adopt a friendly vanilla Seek pick.
                bool needHostile = target == null;
                if (own != null && CanEngage(own, needHostile)
                    && Plugin.TargetFitsSeekerFamily(missile, _primary, own))
                    _target = own;
            }
            if (_target != null && !CanEngage(_target, false))
                _target = null;

            // Ship soft-launch: remember lock but do not GuideTo yet (avoids RAM45 spin-out).
            bool targetEngage = _target != null && CanEngage(_target, false);
            bool deferKh = Plugin.ShouldDeferKh85GuideTo(missile);
            MultiModeTickGateService.SetupLockAction setup = MultiModeTickGateService.ResolveSetupLock(
                ShipLaunchNeedsCoast(),
                targetEngage,
                deferKh,
                Plugin.AllowFreeAttack.Value,
                _stickyOnly);
            if (setup == MultiModeTickGateService.SetupLockAction.ShipCoastHold)
            {
                if (targetEngage)
                {
                    MarkSticky(_target);
                    SyncSeekerTarget(_target);
                }
                _guidanceOverride = false;
                RefreshTargetClaim();
                return;
            }
            if (setup == MultiModeTickGateService.SetupLockAction.StickyGuide
                || setup == MultiModeTickGateService.SetupLockAction.StickyDeferKh85)
            {
                MarkSticky(_target);
                if (_opticalLight)
                {
                    SyncSeekerTargetLight(_target);
                    _guidanceOverride = false;
                }
                else
                {
                    SyncSeekerTarget(_target);
                    if (setup == MultiModeTickGateService.SetupLockAction.StickyGuide)
                        GuideTo(_target);
                    else
                        _guidanceOverride = false;
                }
                if (Plugin.DebugLog.Value)
                    Plugin.Log.LogInfo("MM player/designated lock: " + _target.name
                        + (_opticalLight ? " (optical-light)" : string.Empty));
            }
            else if (setup == MultiModeTickGateService.SetupLockAction.FreeHuntLoal)
            {
                _nextHunt = 0f;
                if (_opticalLight)
                    TryFreeHuntOpticalLight();
                else
                    TryFreeHunt();
                if (MultiModeTickGateService.ShouldClearGuidanceOverrideOnKh85Setup(
                    deferKh, Plugin.IsKh85CTerrainMissile(missile)))
                    _guidanceOverride = false;
            }
            RefreshTargetClaim();
        }

        /// <summary>
        /// LEGACY — no longer called from Seek Postfix (was FixedUpdate hitch root).
        /// Slow Update owns sticky GuideTo / hunt.
        /// </summary>
        public void ReapplyGuidanceAfterSeek()
        {
            if (_opticalLight)
            {
                _guidanceOverride = false;
                return;
            }
            BeforeMissileUpdate();
        }

        private void EnsureFinsAndArm()
        {
            if (_missile == null)
                return;
            // Gun shells / motorless ballistic projectiles: never touch fins.
            if (Plugin.IsGunShellMissile(_missile) || Plugin.IsMotorlessProjectile(_missile))
                return;
            try
            {
                bool shipLaunch = Plugin.IsShipLaunchedMissile(_missile);
                float age = _missile.timeSinceSpawn;
                float spd = 0f;
                try
                {
                    if (_missile.rb != null)
                        spd = _missile.rb.velocity.magnitude;
                }
                catch { }

                bool shipBoostOk = MultiModeGuideMathService.ShipFinBoostOk(shipLaunch, age, spd);

                float finArea = 0f;
                if (FinAreaField != null)
                    finArea = (float)FinAreaField.GetValue(_missile);

                float minGuide = Plugin.MinGuideSpeedMps != null ? Plugin.MinGuideSpeedMps.Value : 90f;
                bool energyOk = MultiModeGuideMathService.EnergyOkForFinBoost(spd, minGuide);
                float wantFin = MultiModeGuideMathService.WantFinArea(finArea, shipBoostOk, energyOk);

                if (CurrentFinAreaField != null)
                {
                    float cur = (float)CurrentFinAreaField.GetValue(_missile);
                    if (MultiModeGuideMathService.ShouldWriteCurrentFin(shipBoostOk, wantFin, cur))
                        CurrentFinAreaField.SetValue(_missile, wantFin);
                }

                if (GLimitField != null)
                {
                    float g = (float)GLimitField.GetValue(_missile);
                    if (MultiModeGuideMathService.ShouldRaiseGLimit(shipBoostOk, energyOk, g))
                        GLimitField.SetValue(_missile, 30f);
                }

                float deployDelay = MultiModeGuideMathService.FinDeployDelaySec(shipLaunch);
                if (age < deployDelay)
                    return;

                try { _missile.DeployFins(); }
                catch { }
                if (!_deployCalled)
                {
                    try { _missile.Arm(); }
                    catch { }
                    _deployCalled = true;
                }

                if (shipBoostOk && wantFin > 0.01f && CurrentFinAreaField != null)
                    CurrentFinAreaField.SetValue(_missile, wantFin);
            }
            catch { }
        }

        /// <summary>Ship soft-launch: leave vanilla guidance until the motor builds speed.</summary>
        private bool ShipLaunchNeedsCoast()
        {
            if (_missile == null)
                return false;
            float age = 0f;
            float spd = 0f;
            try { age = _missile.timeSinceSpawn; }
            catch { }
            try
            {
                if (_missile.rb != null)
                    spd = _missile.rb.velocity.magnitude;
            }
            catch { }
            return Kh85GuideGateService.ShipLaunchNeedsCoast(
                Plugin.IsShipLaunchedMissile(_missile), age, spd, 1.0f, 80f);
        }

        private static bool IsAlive(Unit u)
        {
            return Plugin.IsUnitAlive(u);
        }

        /// <summary>
        /// freeHunt=true: both HQs required and opposing (never nearest friendly).
        /// freeHunt=false: soft IFF — only reject confirmed same-faction (player locks).
        /// </summary>
        private bool CanEngage(Unit candidate, bool freeHunt)
        {
            if (_missile == null || candidate == null)
                return false;
            if (Plugin.IsEjectedPilotUnit(candidate))
                return false;
            if (object.ReferenceEquals(candidate, _missile) || object.ReferenceEquals(candidate, _missile.owner))
                return false;
            if (freeHunt)
                return Plugin.IsLoalHuntTarget(_missile, candidate);
            return Plugin.IsAllowedTarget(Plugin.ResolveShooterSide(_missile), candidate);
        }

        private static bool IsHostile(Missile missile, Unit candidate)
        {
            return Plugin.IsHostileHuntTarget(missile, candidate);
        }

        private void SyncSeekerTarget(Unit target)
        {
            if (target != null && Plugin.IsEjectedPilotUnit(target))
                return;
            if (_opticalLight)
            {
                SyncSeekerTargetLight(target);
                return;
            }
            if (SeekerTargetField != null && target != null)
            {
                if (_primary != null)
                    SeekerTargetField.SetValue(_primary, target);
                if (_secondary != null)
                    SeekerTargetField.SetValue(_secondary, target);
            }
            try
            {
                if (target != null)
                    _missile.SetTarget(target);
            }
            catch { }

            FeedSeekerLock(target);
        }

        /// <summary>
        /// 0.7-style optical assist: only wire targetUnit / Missile.target.
        /// Never FeedSeekerLock (hasVisual/lastOpticalCheck forced SlowChecks + Seek fight).
        /// </summary>
        private void SyncSeekerTargetLight(Unit target)
        {
            if (target == null || Plugin.IsEjectedPilotUnit(target) || _primary == null)
                return;
            if (SeekerTargetField != null)
                SeekerTargetField.SetValue(_primary, target);
            try { _missile.SetTarget(target); }
            catch { }
            _guidanceOverride = false;
        }

        private void OpticalLightBeforeTick()
        {
            // Sticky/player: vanilla Seek steers. Open LOAL keeps SetAimpoint override.
            if (_stickyOnly || _playerDesignated)
                _guidanceOverride = false;
            if (_playerDesignated || _stickyOnly)
            {
                if (_target != null && Plugin.IsEjectedPilotUnit(_target))
                {
                    DropTarget();
                    return;
                }
                if (_target != null && IsAlive(_target) && !IsConfirmedFriendly(_target))
                {
                    if (Time.time >= _nextLightSync)
                    {
                        _nextLightSync = Time.time + 0.2f;
                        SyncSeekerTargetLight(_target);
                    }
                }
                else if (_target != null)
                    DropTarget();
                return;
            }

            if (Plugin.Kh85LoalHuntDelayActive(_missile, false))
                return;

            if (MultiModeTickGateService.DesignatedCheckDue(Time.time, _nextDesignatedCheck))
            {
                _nextDesignatedCheck = MultiModeTickGateService.ScheduleNextDesignatedCheck(Time.time);
                Unit designated = Plugin.ResolveMissileOwnTarget(_missile);
                if (designated != null && CanEngage(designated, true)
                    && Plugin.TargetFitsSeekerFamily(_missile, _primary, designated))
                {
                    MarkSticky(designated);
                    SyncSeekerTargetLight(_target);
                    return;
                }
            }

            if (_target != null && (Plugin.IsEjectedPilotUnit(_target)
                || !IsAlive(_target) || !CanEngage(_target, true)))
                DropTarget();

            if (_target == null && Plugin.AllowFreeAttack.Value && !_stickyOnly)
                TryFreeHuntOpticalLight();
            else if (_target != null)
            {
                if (Plugin.ShouldDeferKh85GuideTo(_missile))
                {
                    if (Time.time >= _nextLightSync)
                    {
                        _nextLightSync = Time.time + 0.2f;
                        SyncSeekerTargetLight(_target);
                    }
                    _guidanceOverride = false;
                }
                else
                    GuideTo(_target);
            }
        }

        /// <summary>LOAL for optical: pick a forward hostile and GuideTo (vanilla Seek will not turn off-boresight).</summary>
        private void TryFreeHuntOpticalLight()
        {
            if (!Plugin.AllowFreeAttack.Value || _missile == null || _stickyOnly || _playerDesignated)
                return;
            if (Plugin.Kh85LoalHuntDelayActive(_missile, false))
                return;
            if (Time.time < _nextHunt)
                return;
            if (!Plugin.TryConsumeHuntQuery())
                return;
            bool acquiring = _target == null && !_stickyOnly && !_playerDesignated;
            _nextHunt = Time.time + (acquiring ? 0.12f : Plugin.EffectiveHuntInterval());

            GlobalPosition origin;
            try { origin = _missile.GlobalPosition(); }
            catch { origin = _missile.transform.position.ToGlobalPosition(); }

            float radius = Plugin.EffectiveHuntRadius(_missile);
            _huntBuf.Clear();
            _huntSeen.Clear();
            try { BattlefieldGrid.GetUnitsInRangeNonAlloc(origin, radius, _huntBuf); }
            catch { return; }

            Unit best = null;
            float bestDist = float.MaxValue;
            Vector3 mpos = _missile.transform.position;
            ConsiderHuntList(_huntBuf, mpos, radius, Plugin.MissileHuntsSurfaceOnly(_missile, _primary), ref best, ref bestDist);
            if (best == null)
                return;
            _target = best;
            _playerDesignated = false;
            if (Plugin.ShouldDeferKh85GuideTo(_missile))
            {
                SyncSeekerTargetLight(best);
                _guidanceOverride = false;
            }
            else
                GuideTo(best);
            RefreshTargetClaim();
        }

        /// <summary>Per-frame sticky: only dirty aim/target fields (skip full bool/IR warm).</summary>
        private void SyncSeekerTargetHot(Unit target)
        {
            if (target == null || Plugin.IsEjectedPilotUnit(target) || _primary == null)
                return;
            if (SeekerTargetField != null)
            {
                SeekerTargetField.SetValue(_primary, target);
                if (_secondary != null)
                    SeekerTargetField.SetValue(_secondary, target);
            }
            try { _missile.SetTarget(target); }
            catch { }

            int tid = 0;
            try { tid = target.GetInstanceID(); }
            catch { }
            if (MultiModeGuideMathService.NeedsFullSeekerWarm(tid, _seekerWarmTargetId))
            {
                FeedSeekerLock(target);
                return;
            }

            try
            {
                GlobalPosition gp = target.GlobalPosition();
                Vector3 vel = target.rb != null ? target.rb.velocity : Vector3.zero;
                SetSeekerField(_primary, "knownPos", gp);
                SetSeekerField(_primary, "knownVel", vel);
                // Optical often has no aimPos — CachedSeekerField neg-caches; skip extra work when absent.
                if (CachedSeekerField(_primary.GetType(), "aimPos") != null)
                    SetSeekerField(_primary, "aimPos", gp);
                if (_secondary != null)
                {
                    SetSeekerField(_secondary, "knownPos", gp);
                    SetSeekerField(_secondary, "knownVel", vel);
                }
            }
            catch { }
        }

        /// <summary>
        /// Force seeker internal state to follow our lock. Vanilla IR/ARH otherwise aim
        /// forward*10km when !guidance / IRTarget null / targetUnit null — looks like straight flight.
        /// Full warm once per target; subsequent sticky frames use SyncSeekerTargetHot.
        /// </summary>
        private void FeedSeekerLock(Unit target)
        {
            if (_opticalLight || _primary == null || target == null || Plugin.IsEjectedPilotUnit(target))
                return;

            try
            {
                GlobalPosition gp = target.GlobalPosition();
                Vector3 vel = target.rb != null ? target.rb.velocity : Vector3.zero;
                int tid = 0;
                try { tid = target.GetInstanceID(); }
                catch { }

                // Common: leave coasting / pre-guidance straight-line path
                SetSeekerBool(_primary, "guidance", true);
                SetSeekerBool(_primary, "deployedFins", true);
                // OpticalSeeker lacks finsDeployed / targetOnLaunch / armed / aimPos on many builds —
                // Resolve via cache (null cached); do not AccessTools-spam.
                SetSeekerBool(_primary, "finsDeployed", true);
                SetSeekerBool(_primary, "armed", true);
                SetSeekerBool(_primary, "targetOnLaunch", true);
                SetSeekerField(_primary, "knownPos", gp);
                SetSeekerField(_primary, "knownVel", vel);
                SetSeekerField(_primary, "aimPos", gp);
                SetSeekerField(_primary, "targetTransform", target.transform);

                Type t = _primary.GetType();
                bool opticalFamily = typeof(OpticalSeeker).IsAssignableFrom(t)
                    || typeof(LaserSeeker).IsAssignableFrom(t)
                    || t.Name.IndexOf("Optical", StringComparison.OrdinalIgnoreCase) >= 0;
                // Never force hasVisual / lastOpticalCheck — that re-triggers expensive Optical SlowChecks.
                if (opticalFamily)
                {
                    // knownPos/targetTransform above are enough; leave visual pipeline to vanilla Seek.
                }
                else if (typeof(IRSeeker).IsAssignableFrom(t))
                {
                    try
                    {
                        IRSource ir = target.GetIRSource();
                        if (ir != null)
                            SetSeekerField(_primary, "IRTarget", ir);
                    }
                    catch { }
                }
                else if (typeof(ARHSeeker).IsAssignableFrom(t))
                    SetSeekerBool(_primary, "radarLockEstablished", true);

                if (_secondary != null && !opticalFamily)
                {
                    SetSeekerBool(_secondary, "guidance", true);
                    SetSeekerField(_secondary, "knownPos", gp);
                    SetSeekerField(_secondary, "knownVel", vel);
                }

                _seekerWarmTargetId = tid;
            }
            catch { }
        }

        private static FieldInfo CachedSeekerField(Type seekerType, string name)
        {
            if (seekerType == null || string.IsNullOrEmpty(name))
                return null;
            string key = seekerType.FullName + ":" + name;
            FieldInfo f;
            if (SeekerFieldCache.TryGetValue(key, out f))
                return f;

            // DeclaredOnly walk — never AccessTools.Field (HarmonyX logs every inheritance miss).
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
            SeekerFieldCache[key] = f; // null = known missing
            return f;
        }

        private static void SetSeekerBool(object seeker, string name, bool value)
        {
            FieldInfo f = CachedSeekerField(seeker.GetType(), name);
            if (f != null && f.FieldType == typeof(bool))
                f.SetValue(seeker, value);
        }

        private static void SetSeekerField(object seeker, string name, object value)
        {
            FieldInfo f = CachedSeekerField(seeker.GetType(), name);
            if (f == null || value == null)
                return;
            try
            {
                if (f.FieldType == value.GetType() || f.FieldType.IsInstanceOfType(value))
                    f.SetValue(seeker, value);
            }
            catch { }
        }

        private void NoteTargetChange(Unit target)
        {
            if (target == null)
                return;
            int id = target.GetInstanceID();
            bool newId = id != _lastTargetId;
            bool hadLock = _lastTargetId != 0;
            _lastTargetId = id;
            // First lock: energy clamp is enough. Re-lock / coast-then-acquire must soften
            // or the snap dumps all airspeed.
            bool relock = MultiModeGuideMathService.ShouldOpenSoftRetarget(
                newId && hadLock, _needsRelockSoft);
            _needsRelockSoft = false;
            if (!relock)
                return;
            float soft = Plugin.SoftRetargetSeconds != null
                ? Plugin.SoftRetargetSeconds.Value
                : MultiModeGuideMathService.SoftRetargetDefaultSec;
            _softRetargetUntil = MultiModeGuideMathService.ScheduleSoftRetargetUntil(
                Time.time, soft, _playerDesignated);
        }

        /// <summary>
        /// Optical LOAL: point at the unit (tiny lead). Energy look-ahead used to fly past.
        /// Once the seeker has visual, vanilla Seek steers (loft/jink/terrain already stamped off).
        /// </summary>
        private void GuideOpticalLoal(Unit target)
        {
            if (target == null || _missile == null)
                return;
            SyncSeekerTargetLight(target);
            if (AgmDirectChaseService.SeekerHasVisual(_primary))
            {
                _guidanceOverride = false;
                return;
            }
            try
            {
                Vector3 mpos = _missile.transform.position;
                Vector3 tpos = target.transform.position;
                Vector3 tvel = target.rb != null ? target.rb.velocity : Vector3.zero;
                Vector3 fwd = _missile.rb != null && _missile.rb.velocity.sqrMagnitude > 1f
                    ? _missile.rb.velocity.normalized
                    : _missile.transform.forward;
                Vector3 leadPos = AgmDirectChaseService.DirectAimPoint(mpos, tpos, tvel);
                float speed = _missile.rb != null ? _missile.rb.velocity.magnitude : 0f;
                float minSpd = Plugin.MinGuideSpeedMps != null ? Plugin.MinGuideSpeedMps.Value : 90f;
                bool soft = Time.time < _softRetargetUntil;
                Vector3 aimPos = MultiModeGuideMathService.ComputeAimPoint(
                    mpos, fwd, speed, leadPos, false, minSpd, soft, _lastCmdDir, Time.fixedDeltaTime);
                GlobalPosition lead = aimPos.ToGlobalPosition();
                _missile.SetAimpoint(lead, tvel);
                _aimpoint = lead;
                _lastKnownAim = lead;
                _lastKnownVel = tvel;
                _hasLastKnownAim = true;
                _guidanceOverride = true;
                _aimSyncedFrame = Time.frameCount;
                _lastGuideTime = Time.time;
                _lastCmdDir = (aimPos - mpos).sqrMagnitude > 0.01f ? (aimPos - mpos).normalized : fwd;
            }
            catch { _guidanceOverride = true; }
        }

        private void GuideTo(Unit target)
        {
            if (_missile == null || target == null)
                return;
            if (HighAltMissileFreeze.IsFrozen(_missile))
                return;
            if (_opticalLight)
            {
                if (_stickyOnly || _playerDesignated || Plugin.ShouldDeferKh85GuideTo(_missile))
                {
                    SyncSeekerTargetLight(target);
                    _guidanceOverride = false;
                    return;
                }
                GuideOpticalLoal(target);
                return;
            }
            MultiModeGuideMathService.GuidePath path = MultiModeGuideMathService.ResolveGuidePath(
                Plugin.IsEjectedPilotUnit(target),
                _stickyOnly,
                Plugin.ShouldDeferKh85GuideTo(_missile));
            if (path == MultiModeGuideMathService.GuidePath.Skip)
                return;
            if (path == MultiModeGuideMathService.GuidePath.CoastSticky)
            {
                CoastStickyLastKnown();
                return;
            }
            if (path == MultiModeGuideMathService.GuidePath.SyncLockOnly)
            {
                SyncSeekerTargetHot(target);
                _guidanceOverride = false;
                return;
            }

            NoteTargetChange(target);
            // Sticky: throttle fin/gLimit work; free-hunt keeps full Ensure each aim.
            if (MultiModeGuideMathService.NeedsFinEnsure(_stickyOnly, Time.time, _nextFinBoost))
            {
                EnsureFinsAndArm();
                if (_stickyOnly)
                    _nextFinBoost = MultiModeGuideMathService.ScheduleNextFinBoost(Time.time);
            }
            try
            {
                Vector3 mpos = _missile.transform.position;
                Vector3 mvel = _missile.rb != null ? _missile.rb.velocity : _missile.transform.forward * 200f;
                float speed = Mathf.Max(mvel.magnitude, 1f);
                Vector3 fwd = mvel.sqrMagnitude > 1f ? mvel.normalized : _missile.transform.forward;

                Vector3 tvel = target.rb != null ? target.rb.velocity : Vector3.zero;
                Vector3 tpos = target.transform.position;
                Vector3 leadPos = MultiModeGuideMathService.LeadPosition(
                    mpos, fwd, speed, tpos, tvel);
                float aspect = MultiModeGuideMathService.AspectDeg(fwd, mpos, leadPos);

                bool energyAware = Plugin.EnergyAwareGuide == null || Plugin.EnergyAwareGuide.Value;
                float minSpd = Plugin.MinGuideSpeedMps != null
                    ? Plugin.MinGuideSpeedMps.Value
                    : MultiModeGuideMathService.DefaultMinGuideSpeedMps;
                bool soft = MultiModeGuideMathService.IsSoftRetargetWindow(
                    _playerDesignated, _stickyOnly, Time.time, _softRetargetUntil);

                float dist = Vector3.Distance(mpos, tpos);
                bool terminal = MultiModeGuideMathService.WantTerminal(
                    true, dist, Plugin.TerminalRange != null ? Plugin.TerminalRange.Value : 8000f,
                    speed, minSpd, Time.time, _softRetargetUntil);
                bool directChase = MultiModeGuideMathService.AllowDirectChase(
                    energyAware, terminal, speed, minSpd, aspect);

                float dt = _lastGuideTime > 0.01f
                    ? Mathf.Clamp(Time.time - _lastGuideTime, 0.016f, 0.25f)
                    : 0.02f;
                Vector3 aim = MultiModeGuideMathService.ComputeAimPoint(
                    mpos, fwd, speed, leadPos, directChase, minSpd, soft, _lastCmdDir, dt);

                // aim is local/scene — NEVER new GlobalPosition(local) (far-map 180° miss).
                GlobalPosition lead = aim.ToGlobalPosition();
                _missile.SetAimpoint(lead, tvel);
                _aimpoint = lead;
                _lastKnownAim = lead;
                _lastKnownVel = tvel;
                _hasLastKnownAim = true;
                _guidanceOverride = true;
                _aimSyncedFrame = Time.frameCount;
                _lastCmdDir = (aim - mpos).sqrMagnitude > 0.01f ? (aim - mpos).normalized : fwd;
                _lastGuideTime = Time.time;

                if (energyAware && GLimitField != null && _hasOriginalGLimit)
                {
                    float g = (float)GLimitField.GetValue(_missile);
                    float cap = MultiModeGuideMathService.CapGLimitForEnergy(
                        g, _originalGLimit, speed, minSpd, soft, aspect);
                    if (cap > 0.01f && Mathf.Abs(g - cap) > 0.05f)
                        GLimitField.SetValue(_missile, cap);
                }

                // First lock warms once via NeedsFullSeekerWarm; later frames stay hot.
                // Full SyncSeekerTarget (FeedSeekerLock) every LOAL GuideTo was a multi-second hitch.
                SyncSeekerTargetHot(target);
            }
            catch
            {
                try
                {
                    Vector3 mpos = _missile.transform.position;
                    Vector3 fwd = _missile.rb != null && _missile.rb.velocity.sqrMagnitude > 1f
                        ? _missile.rb.velocity.normalized
                        : _missile.transform.forward;
                    float spd = _missile.rb != null ? _missile.rb.velocity.magnitude : 80f;
                    Vector3 coastAim = mpos + fwd * Mathf.Clamp(spd * 2f, 400f, 1200f);
                    GlobalPosition gp = coastAim.ToGlobalPosition();
                    _missile.SetAimpoint(gp, Vector3.zero);
                    _aimpoint = gp;
                    _lastKnownAim = gp;
                    _lastKnownVel = Vector3.zero;
                    _hasLastKnownAim = true;
                    _guidanceOverride = true;
                    _aimSyncedFrame = Time.frameCount;
                    SyncSeekerTargetHot(target);
                }
                catch { }
            }
        }

        private void TryFreeHunt()
        {
            if (!Plugin.AllowFreeAttack.Value || _missile == null)
                return;
            if (Plugin.Kh85LoalHuntDelayActive(_missile, _stickyOnly || _playerDesignated))
                return;
            // Sticky-only missiles: never MadMode / grid hunt / sibling claim steal.
            if (_stickyOnly || _playerDesignated)
            {
                if (_target != null && IsAlive(_target) && !Plugin.IsEjectedPilotUnit(_target)
                    && CanEngage(_target, false))
                    GuideTo(_target);
                else
                    CoastStickyLastKnown();
                return;
            }

            // Between scans: keep guiding current hostile, else release (no loft coast)
            if (Time.time < _nextHunt)
            {
                if (_target != null && IsAlive(_target) && CanEngage(_target, true))
                    GuideTo(_target);
                else
                    ReleaseGuidanceToVanilla();
                return;
            }
            if (!Plugin.TryConsumeHuntQuery())
            {
                if (_target != null && IsAlive(_target) && CanEngage(_target, true))
                    GuideTo(_target);
                return;
            }
            bool acquiring = _target == null && !_stickyOnly && !_playerDesignated;
            _nextHunt = MultiModeGuideMathService.ScheduleNextHunt(
                Time.time, acquiring ? 0.12f : Plugin.EffectiveHuntInterval());

            GlobalPosition origin;
            try { origin = _missile.GlobalPosition(); }
            catch { origin = _missile.transform.position.ToGlobalPosition(); }

            float radius = Plugin.EffectiveHuntRadius(_missile);
            _huntBuf.Clear();
            _huntSeen.Clear();
            try
            {
                BattlefieldGrid.GetUnitsInRangeNonAlloc(origin, radius, _huntBuf);
            }
            catch { }

            Unit best = null;
            float bestDist = float.MaxValue;
            Vector3 mpos = _missile.transform.position;
            bool surfaceOnly = Plugin.MissileHuntsSurfaceOnly(_missile, _primary);

            // Grid only — avoid full UnitRegistry.allUnits scan every hunt.
            ConsiderHuntList(_huntBuf, mpos, radius, surfaceOnly, ref best, ref bestDist);

            if (best == null)
            {
                ReleaseGuidanceToVanilla();
                return;
            }

            _target = best;
            _playerDesignated = false;
            SyncSeekerTarget(best);
            RefreshTargetClaim();
            // C/E/S (and unstamped Kh85): Kh85MT Steering owns aim — never GuideTo from hunt.
            if (!Plugin.ShouldDeferKh85GuideTo(_missile))
                GuideTo(best);
            else
                _guidanceOverride = false;
            EnsureFinsAndArm();

            if (Plugin.DebugLog.Value)
                Plugin.Log.LogInfo("MM free-hunt nearest: " + best.name + " d=" + bestDist.ToString("0"));
        }

        private bool IsClaimedBySibling(Unit candidate)
        {
            if (candidate == null || _missile == null)
                return false;
            MultiModeBrain other;
            bool exists = ClaimByTargetId.TryGetValue(candidate.GetInstanceID(), out other)
                && other != null && other._missile != null;
            if (!MultiModeClaimGateService.IsClaimedBySibling(
                    exists, exists && object.ReferenceEquals(other, this)))
                return false;
            return SameOwnerAs(other);
        }

        /// <summary>Hard-block hunting a Unit already claimed by a sticky sibling.</summary>
        private bool IsClaimedByStickySibling(Unit candidate)
        {
            if (candidate == null || _missile == null)
                return false;
            MultiModeBrain other;
            bool exists = ClaimByTargetId.TryGetValue(candidate.GetInstanceID(), out other) && other != null;
            if (!MultiModeClaimGateService.IsClaimedByStickySibling(
                    exists,
                    exists && object.ReferenceEquals(other, this),
                    exists && other._stickyOnly))
                return false;
            return SameOwnerAs(other);
        }

        private void ConsiderHuntList(List<Unit> units, Vector3 mpos, float radius, bool surfaceOnly,
            ref Unit best, ref float bestDist)
        {
            if (units == null)
                return;
            Vector3 fwd = Vector3.forward;
            float minDot = -1f;
            float fov = Plugin.MadModeSearchAngle != null ? Plugin.MadModeSearchAngle.Value : 360f;
            if (fov < 359f)
            {
                try
                {
                    if (_missile.rb != null && _missile.rb.velocity.sqrMagnitude > 16f)
                        fwd = _missile.rb.velocity.normalized;
                    else
                        fwd = _missile.transform.forward;
                }
                catch
                {
                    try { fwd = _missile.transform.forward; }
                    catch { }
                }
                if (fov < 1f)
                    fov = 1f;
                minDot = Mathf.Cos(fov * Mathf.Deg2Rad);
            }
            for (int i = 0; i < units.Count; i++)
            {
                Unit u = units[i];
                if (u == null || !_huntSeen.Add(u))
                    continue;
                if (Plugin.IsEjectedPilotUnit(u))
                    continue;
                if (!CanEngage(u, true))
                    continue;
                if (!Plugin.TargetFitsSeekerFamily(_missile, _primary, u))
                    continue;
                if (surfaceOnly && !Plugin.IsSurfaceUnit(u))
                    continue;
                if (u is Missile)
                {
                    Missile om = (Missile)u;
                    if (om.owner != null && object.ReferenceEquals(om.owner, _missile.owner))
                        continue;
                }

                Vector3 to = u.transform.position - mpos;
                float dist = to.magnitude;
                if (dist > radius || dist < 5f)
                    continue;
                if (minDot > -0.999f && Vector3.Dot(fwd, to / dist) < minDot)
                    continue;

                // Sticky sibling owns this Unit — never steal / retarget onto it.
                if (IsClaimedByStickySibling(u))
                    continue;

                // Soft prefer targets not already claimed by a free-hunt sibling.
                if (IsClaimedBySibling(u))
                    dist += 2500f;

                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = u;
                }
            }
        }

        private void EnsureSecondary()
        {
            if (!Plugin.EnableSecondarySeeker.Value || _primary == null || _secondary != null)
                return;

            Type secondaryType = ResolveSecondaryType(_primary.GetType());
            if (secondaryType == null)
                return;

            MissileSeeker[] seekers = _missile.GetComponents<MissileSeeker>();
            for (int i = 0; i < seekers.Length; i++)
            {
                if (seekers[i] != null && secondaryType.IsInstanceOfType(seekers[i])
                    && !object.ReferenceEquals(seekers[i], _primary))
                {
                    _secondary = seekers[i];
                    break;
                }
            }
        }

        private static Type ResolveSecondaryType(Type primaryType)
        {
            string prefer = Plugin.SecondaryPrefer.Value != null ? Plugin.SecondaryPrefer.Value.Trim() : "Auto";
            if (string.Equals(prefer, "ARH", StringComparison.OrdinalIgnoreCase))
                return typeof(ARHSeeker);
            if (string.Equals(prefer, "IR", StringComparison.OrdinalIgnoreCase))
                return typeof(IRSeeker);
            if (string.Equals(prefer, "SARH", StringComparison.OrdinalIgnoreCase))
                return typeof(SARHSeeker);
            if (typeof(IRSeeker).IsAssignableFrom(primaryType))
                return typeof(ARHSeeker);
            if (typeof(ARHSeeker).IsAssignableFrom(primaryType))
                return typeof(IRSeeker);
            return typeof(ARHSeeker);
        }

        /// <summary>
        /// Slow maintenance only — never from Missile.FixedUpdate.
        /// Interval = MadModeSearchInterval (min 0.5s; optical/laser min 1s).
        /// LowSpeed SD + YieldProx: ~4 Hz here (was every FixedUpdate).
        /// </summary>
        private void Update()
        {
            TickSlow();
        }

        private void TickSlow()
        {
            if (!_ready || _missile == null)
                return;
            int f = Time.frameCount;
            if (_tickFrame == f)
                return;
            _tickFrame = f;

            float nowU = Time.unscaledTime;
            if (nowU >= _nextSafetyTick)
            {
                _nextSafetyTick = nowU + 0.25f;
                Plugin.TickLowSpeedSelfDestruct(_missile);
                YieldProximityFuze.TickFallback(_missile);
            }

            float interval = Plugin.MadModeSearchInterval != null
                ? Plugin.MadModeSearchInterval.Value
                : 0.5f;
            bool openLoal = _target == null && !_stickyOnly && !_playerDesignated
                && Plugin.AllowFreeAttack != null && Plugin.AllowFreeAttack.Value;
            if (openLoal)
                interval = 0.08f;
            else if (_opticalLight && (_stickyOnly || _playerDesignated))
                interval = Mathf.Max(1f, interval);
            else if (_opticalLight)
                interval = Mathf.Max(0.25f, interval);
            else
                interval = Mathf.Max(0.5f, interval);
            float now = Time.time;
            if (now < _nextSlowTick)
                return;
            _nextSlowTick = now + interval;
            BeforeMissileUpdate();
        }

        public void BeforeMissileUpdate()
        {

            MultiModeTickGateService.EarlyOut early = MultiModeTickGateService.ResolveBeforeEarly(
                _ready,
                _missile == null,
                _lastBeforeFrame,
                Time.frameCount,
                Plugin.EnableMultiMode.Value,
                _missile != null && (Plugin.IsGunShellMissile(_missile) || Plugin.IsMotorlessProjectile(_missile)));
            if (early == MultiModeTickGateService.EarlyOut.NotReady)
                return;
            if (early == MultiModeTickGateService.EarlyOut.SkipDuplicateFrame)
                return;
            _lastBeforeFrame = Time.frameCount;
            if (early == MultiModeTickGateService.EarlyOut.ClearOverride)
            {
                _guidanceOverride = false;
                return;
            }

            // Was every FixedUpdate — major cost. Throttle; GuideTo path also calls when needed.
            if (MultiModeGuideMathService.NeedsFinEnsure(_stickyOnly || _opticalLight, Time.time, _nextFinBoost))
            {
                EnsureFinsAndArm();
                _nextFinBoost = MultiModeGuideMathService.ScheduleNextFinBoost(Time.time);
            }

            if (_primary != null && SeekerField != null)
                SeekerField.SetValue(_missile, _primary);

            // 0.7 Optical/Laser light path: lock assist only, no GuideTo/FeedSeekerLock fight.
            if (_opticalLight)
            {
                OpticalLightBeforeTick();
                return;
            }

            MultiModeTickGateService.ModeBranch branch = MultiModeTickGateService.ResolveModeBranch(
                ShipLaunchNeedsCoast(),
                Plugin.IsKh85CTerrainMissile(_missile),
                _playerDesignated || _stickyOnly);

            if (branch == MultiModeTickGateService.ModeBranch.ShipCoast)
            {
                _guidanceOverride = false;
                return;
            }

            if (branch == MultiModeTickGateService.ModeBranch.Kh85Terrain)
            {
                if (_playerDesignated || _stickyOnly)
                {
                    MultiModeTickGateService.Kh85StickyAction kh = MultiModeTickGateService.ResolveKh85Sticky(
                        _target != null && Plugin.IsEjectedPilotUnit(_target),
                        _target != null,
                        _target != null && IsAlive(_target),
                        _target != null && IsConfirmedFriendly(_target));
                    if (kh == MultiModeTickGateService.Kh85StickyAction.DropCoast)
                    {
                        DropTarget();
                        CoastStickyLastKnown();
                    }
                    else
                        SyncSeekerTargetHot(_target);
                }
                else if (Plugin.Kh85LoalHuntDelayActive(_missile, false))
                {
                    _guidanceOverride = false;
                }
                else if (MultiModeTickGateService.DesignatedCheckDue(Time.time, _nextDesignatedCheck))
                {
                    _nextDesignatedCheck = MultiModeTickGateService.ScheduleNextDesignatedCheck(Time.time);
                    Unit designatedC = Plugin.ResolveMissileOwnTarget(_missile);
                    if (designatedC != null && CanEngage(designatedC, true)
                        && Plugin.TargetFitsSeekerFamily(_missile, _primary, designatedC))
                    {
                        MarkSticky(designatedC);
                        SyncSeekerTarget(_target);
                    }
                }
                MultiModeTickGateService.OpenHuntAction free = MultiModeTickGateService.ResolveKh85Free(
                    Plugin.AllowFreeAttack.Value,
                    _stickyOnly,
                    _playerDesignated,
                    _target != null && IsAlive(_target) && CanEngage(_target, true),
                    Time.time >= _nextHunt);
                if (free == MultiModeTickGateService.OpenHuntAction.SyncHotExisting)
                    SyncSeekerTarget(_target);
                else if (free == MultiModeTickGateService.OpenHuntAction.TryFreeHunt)
                {
                    TryFreeHunt();
                    _guidanceOverride = false;
                }
                _guidanceOverride = false;
                return;
            }

            if (branch == MultiModeTickGateService.ModeBranch.Sticky)
            {
                MultiModeTickGateService.StickyAction st = MultiModeTickGateService.ResolveStickyBefore(
                    _target != null && Plugin.IsEjectedPilotUnit(_target),
                    _target != null,
                    _target != null && IsAlive(_target),
                    _target != null && IsConfirmedFriendly(_target));
                if (st == MultiModeTickGateService.StickyAction.GuideAlive)
                {
                    // 0.7 never GuideTo'd every physics frame — throttle ~10 Hz.
                    if (Time.time >= _nextStickyGuide)
                    {
                        _nextStickyGuide = Time.time + 0.1f;
                        GuideTo(_target);
                    }
                    ApplyTerminalBoostIfNeeded();
                    return;
                }
                DropTarget();
                CoastStickyLastKnown();
                ApplyTerminalBoostIfNeeded();
                return;
            }

            if (Plugin.Kh85LoalHuntDelayActive(_missile, false))
            {
                ApplyTerminalBoostIfNeeded();
                return;
            }

            if (MultiModeTickGateService.DesignatedCheckDue(Time.time, _nextDesignatedCheck))
            {
                _nextDesignatedCheck = MultiModeTickGateService.ScheduleNextDesignatedCheck(Time.time);
                Unit designated = Plugin.ResolveMissileOwnTarget(_missile);
                if (designated != null && CanEngage(designated, true)
                    && Plugin.TargetFitsSeekerFamily(_missile, _primary, designated))
                {
                    MarkSticky(designated);
                    GuideTo(_target);
                    ApplyTerminalBoostIfNeeded();
                    return;
                }
            }

            if (_target != null && (Plugin.IsEjectedPilotUnit(_target)
                || !IsAlive(_target) || !CanEngage(_target, true)))
                DropTarget();

            if (_target == null && Plugin.AllowFreeAttack.Value && !_stickyOnly)
                TryFreeHunt();

            ApplyTerminalBoostIfNeeded();
        }

        private void ApplyTerminalBoostIfNeeded()
        {
            float spd = 0f;
            try
            {
                if (_missile != null && _missile.rb != null)
                    spd = _missile.rb.velocity.magnitude;
            }
            catch { }
            float dist = 0f;
            bool hasTgt = false;
            try
            {
                if (_target != null)
                {
                    hasTgt = true;
                    dist = Vector3.Distance(_missile.transform.position, _target.transform.position);
                }
            }
            catch { }

            float minSpd = Plugin.MinGuideSpeedMps != null
                ? Plugin.MinGuideSpeedMps.Value
                : MultiModeGuideMathService.DefaultMinGuideSpeedMps;
            bool wantTerminal = MultiModeGuideMathService.WantTerminal(
                hasTgt,
                dist,
                Plugin.TerminalRange.Value,
                spd,
                minSpd,
                Time.time,
                _softRetargetUntil);

            if (wantTerminal == _terminal)
                return;
            _terminal = wantTerminal;

            if (_terminal)
            {
                if (Plugin.TerminalGLimit.Value > 0f && MissileGLimitField != null
                    && !Aam2CvWeapon.IsAam2CvMissile(_missile))
                    MissileGLimitField.SetValue(_missile, Plugin.TerminalGLimit.Value);
                if (Plugin.TerminalBoostAmount.Value > 0f)
                {
                    try { _missile.ApplyTerminalBoost(Plugin.TerminalBoostAmount.Value); }
                    catch { }
                }
            }
            else if (_hasOriginalGLimit && MissileGLimitField != null)
            {
                MissileGLimitField.SetValue(_missile, _originalGLimit);
            }
        }
    }

    /// <summary>
    /// Aircraft pylonOptions bind the vanilla AAM-29 / AAM-36 WeaponMount.
    /// ACM-119 / AAM-2CV are clones, so MatchesMount would hide the adapter.
    /// </summary>
    [HarmonyPatch]
    internal static class Patch_HardpointPylon_MatchesMount_WeXon
    {
        private static readonly FieldInfo BoundMountField;

        static Patch_HardpointPylon_MatchesMount_WeXon()
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
            bool ours = AgmTWeapon.IsAgmTMount(mount) || Aam2CvWeapon.IsAam2CvMount(mount);
            if (!ours)
                return;
            WeaponMount bound = null;
            try { bound = BoundMountField.GetValue(__instance) as WeaponMount; }
            catch { return; }
            if (bound == null)
                return;
            if (AgmTWeapon.IsAgmTMount(bound) || Aam2CvWeapon.IsAam2CvMount(bound))
            {
                __result = true;
                return;
            }
            string sn = bound.info != null && bound.info.shortName != null
                ? bound.info.shortName : string.Empty;
            string wn = bound.info != null && bound.info.weaponName != null
                ? bound.info.weaponName : string.Empty;
            if (AgmTWeapon.IsAgmTMount(mount)
                && AgmTAssetGateService.IsAam29Donor(bound.jsonKey, sn, wn))
                __result = true;
            else if (Aam2CvWeapon.IsAam2CvMount(mount)
                && Aam2CvGateService.IsAam36Donor(bound.jsonKey, sn, wn))
                __result = true;
        }
    }
}
