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
    /// - Vanilla missiles keep conventional warheads + multimode + 35km free-hunt
    /// - Separate IAL / [10kt] WeaponMount clones carry nuclear warheads
    /// - IFF gating (no OpticalIffSeeker replace �?that path caused dive/self-destruct)
    /// - Encyclopedia pages branded WeXon
    /// </summary>
#if !ORITASY_COMBINED
    [BepInPlugin(PluginInfo.GUID, PluginInfo.Name, PluginInfo.Version)]
    [BepInDependency("bia.runtime")]
#endif
    public partial class Plugin : BaseUnityPlugin
    {
        internal const string PackName = "WeXon";
        internal const string PackDescLine = "[WeXon]";
        internal static ManualLogSource Log;

        internal static ConfigEntry<bool> EnableNukeVariants;
        internal static ConfigEntry<bool> EnableMultiMode;
        internal static ConfigEntry<bool> EnableUnrestricted;
        internal static ConfigEntry<bool> EnableIff;
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

        internal static void OfferExtraMount(WeaponMount m)
        {
            if (m == null)
                return;
            if (CachedMountSet.Add(m))
                CachedMounts.Add(m);
        }

        private static bool _scannedAssets;
        private static bool _encyclopediaReady;
        private static float _nextEncyclopediaRetry;
        private static float _nextAgmTRetry;
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

        /// <summary>Unrestricted hardpoints apply to the human player only — never AI auto-loadout.</summary>
        internal static bool AllowPlayerUnrestricted()
        {
            return LoadoutMountGateService.AllowPlayerUnrestricted(
                EnableUnrestricted.Value, PlayerUnrestrictedDepth);
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

        private static bool _wexonInitDone;

        private void Awake()
        {
#if ORITASY_COMBINED
            return;
#else
            Log = Logger;
            RunInit(Config);
#endif
        }

        internal static void RunInit(ConfigFile config)
        {
            if (config == null || _wexonInitDone)
                return;
            _wexonInitDone = true;
            CacheWarheadReflection();

            EnableNukeVariants = config.Bind("Features", "EnableNukeVariants", true,
                "Oritasy-series nuclear warheads ([IAL]/[10kt] clones). Off: hangar + AI never get those clones. Vanilla nukes (Genie, tacNuke) stay.");
            EnableMultiMode = config.Bind("Features", "EnableMultiMode", true,
                "Dual-seeker multi-mode + free-hunt on all allowed missiles.");
            EnableUnrestricted = config.Bind("Features", "EnableUnrestricted", false,
                "Player loadout only: any weapon on any hardpoint. AI keeps vanilla mount options. Off by default; toggle in Career Profile.");
            EnableIff = config.Bind("Features", "EnableIFF", true,
                "Reject friendly locks (from OpticalIFF). Does not replace vanilla seeker.");
            EnableAgmT = config.Bind("Features", "EnableAgmT", true,
                "ACM-119 [IAL] / ACNM-118 [IAL]: Veyrn Aeronautics cluster bus �?6× GS25 (conv. never nuclear).");
            AgmTSubCount = config.Bind("AGM-T", "SubmunitionCount", 6, "GS25 count per ACM-119 / ACNM-118 (1-24).");
            // Migrate older default (12) �?6
            if (AgmTSubCount.Value == 12)
                AgmTSubCount.Value = 6;
            AgmTMinFlightTime = config.Bind("AGM-T", "MinFlightTime", 5f,
                "Seconds before dispense (hard floor 5s even if lowered).");
            AgmTMaxFlightTime = config.Bind("AGM-T", "MaxFlightTime", 25f, "Force dispense after this many seconds.");
            AgmTDispenseDistance = config.Bind("AGM-T", "DispenseDistance", 2500f, "Dispense when within this range of lock (m).");
            AgmTSearchRadius = config.Bind("AGM-T", "SearchRadius", 35000f, "GS25 target search radius (m).");
            AgmTBusHuntRadius = config.Bind("AGM-T", "BusHuntRadius", 20000f,
                "AGM-T bus self-search radius for dense air/ground packs (m).");
            AgmTEjectSpeed = config.Bind("AGM-T", "EjectSpeed", 45f, "Lateral eject speed added to each GS25 (m/s).");
            AgmTNukeYield = config.Bind("AGM-T", "NukeYield", 1500000f,
                "1.5kt variant base blast yield (1500000 = 1.5kt). Effective = NukeYield * NukeBlastScale.");
            AgmTNukeBlastScale = config.Bind("AGM-T", "NukeBlastScale", 0.45f,
                "ACNM-118 shockwave/blast scale vs NukeYield (0.45 → ~0.675kt effective, smaller radius).");
            AgmTNukeMinTargetValueM = config.Bind("AGM-T", "NukeMinTargetValueM", 25f,
                "ACNM-118 seeker: skip units with UnitDefinition.value below this (millions; 25 = 25M).");
            AgmTNukeProximityM = config.Bind("AGM-T", "NukeProximityM", 500f,
                "ACNM-118: nuclear detonation only within this range of intended target (m). Else fizzle SD.");
            AgmTNukeArmMinFlightTime = config.Bind("AGM-T", "NukeArmMinFlightTime", 6f,
                "ACNM-118: seconds after spawn before nuclear warhead can arm (blocks launch terrain nuke).");
            AgmTNukeArmMinDistance = config.Bind("AGM-T", "NukeArmMinDistance", 2000f,
                "ACNM-118: meters from spawn before nuclear warhead can arm.");
            AgmTDragScale = config.Bind("AGM-T", "DragScale", 0.55f,
                "ACM-119 / ACNM-118 / GS25 dragCurve + supersonicDrag multiplier (<1 = less drag).");
            AgmTGs25Thrust = config.Bind("AGM-T", "Gs25Thrust", 22000f,
                "Synthetic GS25 motor thrust (N). Vanilla GS25 has no motors.");
            AgmTGs25BurnTime = config.Bind("AGM-T", "Gs25BurnTime", 42f,
                "GS25 burn duration (s) for ~35km high-thrust cruise.");
            AgmTGs25MaxRange = config.Bind("AGM-T", "Gs25MaxRange", 35000f,
                "GS25 powered range cap (m). Thrust cuts off past this.");
            AgmTSubNukeImmune = config.Bind("AGM-T", "SubNukeImmune", true,
                "GS25 submunitions ignore nuclear shockwave push and blast damage (no tumble / no sympathetic detonation).");
            AgmTBusCustomVisual = config.Bind("AGM-T", "BusCustomVisual", true,
                "Replace ACM-119 / ACNM-118 bus mesh with AAM-IV (WeXonAssets/). GS25 unchanged.");
            AgmTBusVisualScale = config.Bind("AGM-T", "BusVisualScale", 1f,
                "AAM-IV visual uniform scale.");
            AgmTBusVisualEuler = config.Bind("AGM-T", "BusVisualEuler", "0,0,0",
                "AAM-IV local euler degrees (x,y,z).");
            AgmTBusVisualOffset = config.Bind("AGM-T", "BusVisualOffset", "0,0,0",
                "AAM-IV local position offset (x,y,z).");
            // Migrate older installs that used sub-5s dispense
            if (AgmTMinFlightTime.Value < 5f)
                AgmTMinFlightTime.Value = 5f;
            if (AgmTSearchRadius.Value < 35000f)
                AgmTSearchRadius.Value = 35000f;

            // Game scale: vanilla ~20kt = 20000000 �?10kt = 10000000
            BlastYield = config.Bind("Nuke", "BlastYield", 10000000f, "Nuclear blast yield. 10000000 �?10kt.");
            PierceDamage = config.Bind("Nuke", "PierceDamage", -1f, ">=0 overwrites pierceDamage; -1 keep.");
            SetNuclearFlag = config.Bind("Nuke", "SetNuclearFlag", true, "WeaponInfo.nuclear = true on nuke variants");
            SetStrategicFlag = config.Bind("Nuke", "SetStrategicFlag", false, "WeaponInfo.strategic = true on nuke variants");
            SwapExplosionFx = config.Bind("Nuke", "SwapExplosionFx", true, "Swap warhead FX to nuclear explosion prefab");
            ExplosionFxName = config.Bind("Nuke", "ExplosionFxName", "explosion_20kt",
                "Nuke FX prefab name (game has explosion_20kt; yield is still 10kt).");
            NukeLabelNames = config.Bind("Nuke", "LabelNames", true, "Append nuke tag to nuke-variant names");
            NukeNameTag = config.Bind("Nuke", "NameTag", "[10kt]", "ASCII only");

            // Migrate old 20kt defaults from previous installs
            if (Mathf.Abs(BlastYield.Value - 20000000f) < 1f)
                BlastYield.Value = 10000000f;
            if (string.Equals(NukeNameTag.Value, "[20kt]", StringComparison.OrdinalIgnoreCase))
                NukeNameTag.Value = "[10kt]";

            TerminalRange = config.Bind("MultiMode", "TerminalRange", 8000f, "Range (m) for terminal g-boost");
            SecondaryPrefer = config.Bind("MultiMode", "SecondaryPrefer", "Auto", "Auto | ARH | IR | SARH (only if EnableSecondarySeeker)");
            EnableSecondarySeeker = config.Bind("MultiMode", "EnableSecondarySeeker", false,
                "OFF by default: AddComponent ARH/IR secondary caused Seek() NREs and aimless/self-destruct missiles.");
            TerminalGLimit = config.Bind("MultiMode", "TerminalGLimit", 40f, "0 = do not change gLimit");
            TerminalBoostAmount = config.Bind("MultiMode", "TerminalBoostAmount", 0.35f, "0 = skip ApplyTerminalBoost");
            AllowFreeAttack = config.Bind("MultiMode", "AllowFreeAttack", true,
                "No-lock / lost-lock autonomous hunt for nearest hostile.");
            EnergyAwareGuide = config.Bind("MultiMode", "EnergyAwareGuide", true,
                "Limit retarget turn rate by airspeed so missiles do not stall and hang motionless.");
            SoftRetargetSeconds = config.Bind("MultiMode", "SoftRetargetSeconds", 2.5f,
                "After a lock change, keep turns gentler for this many seconds.");
            MinGuideSpeedMps = config.Bind("MultiMode", "MinGuideSpeedMps", 90f,
                "Below this speed (m/s), command forward coast with a mild loft instead of hard turns.");
            MadModeSearchRadius = config.Bind("MultiMode", "MadModeSearchRadius", 35000f, "Free-hunt search radius (m)");
            MadModeSearchAngle = config.Bind("MultiMode", "MadModeSearchAngle", 110f,
                "Nose FOV half-angle (deg). 360 = nearest enemy in full sphere.");
            MadModeSearchInterval = config.Bind("MultiMode", "MadModeSearchInterval", 0.5f,
                "Seconds between free-hunt scans (lower = heavier CPU).");
            if (MadModeSearchInterval.Value < 0.25f)
                MadModeSearchInterval.Value = 0.25f;
            // 261C: full-sphere LOAL (360) picked rear/side targets → missile stall.
            if (MadModeSearchAngle != null && MadModeSearchAngle.Value >= 350f)
                MadModeSearchAngle.Value = 110f;
            TbmHuntRadius = config.Bind("MultiMode", "TbmHuntRadius", 60000f,
                "Piledriver TBM free-hunt radius (m). Larger than MadModeSearchRadius; does not use MultiMode steering.");
            MmLabelNames = config.Bind("MultiMode", "LabelNames", false,
                "Deprecated: do not use [MM]. Names use [IAL] only; nukes also get [10kt].");
            MmNameTag = config.Bind("MultiMode", "NameTag", "[MM]", "Unused �?kept for old configs");
            if (MmLabelNames.Value)
                MmLabelNames.Value = false;

            EnableLowSpeedSelfDestruct = config.Bind("Missile", "EnableLowSpeedSelfDestruct", true,
                "Detonate missiles that fall too slow (dud / energy-spent) instead of inert ground impact.");
            // Threshold stored as km/h (user request). Converted to m/s at runtime (/3.6).
            LowSpeedSelfDestructMps = config.Bind("Missile", "LowSpeedSelfDestructKmh", 15f,
                "Speed threshold (km/h). Below this for Hold seconds �?self-destruct. Default 15 km/h.");
            // Migrate previous m/s default (55) �?15 km/h
            if (Mathf.Abs(LowSpeedSelfDestructMps.Value - 55f) < 0.05f)
                LowSpeedSelfDestructMps.Value = 15f;
            LowSpeedSelfDestructMinAge = config.Bind("Missile", "LowSpeedSelfDestructMinAge", 3.5f,
                "Minimum flight time (s) before low-speed SD can arm (avoids launch tip-off).");
            LowSpeedSelfDestructHold = config.Bind("Missile", "LowSpeedSelfDestructHold", 0.85f,
                "Must stay below speed threshold this long (s) before detonating.");
            LowSpeedSelfDestructBallisticMinAge = config.Bind("Missile", "LowSpeedSelfDestructBallisticMinAge", 12f,
                "Extra min age for ballistic/TBM so apogee coast does not false-trigger.");
            ShipMissileMinLaunchMps = config.Bind("Missile", "ShipMissileMinLaunchMps", 140f,
                "Ship/VLS soft launches (e.g. RAM45): boost along nose if slower than this (m/s).");

            EnableYieldProximityFuze = config.Bind("Missile", "EnableYieldProximityFuze", true,
                "Proximity fuze range from warhead blastYield (cube-root). Wires ProxyFuse only; no seeker/steer patches.");
            ProximityRefYield = config.Bind("Missile", "ProximityRefYield", 25f,
                "Reference blastYield for the proximity formula (typical conventional HE).");
            ProximityRefRangeM = config.Bind("Missile", "ProximityRefRangeM", 30f,
                "Proximity trigger range (m) at ProximityRefYield.");
            ProximityScale = config.Bind("Missile", "ProximityScale", 1f,
                "Extra multiplier on yield-scaled proximity range.");
            ProximityMinM = config.Bind("Missile", "ProximityMinM", 8f,
                "Minimum proximity trigger range (m).");
            ProximityMaxM = config.Bind("Missile", "ProximityMaxM", 250f,
                "Maximum proximity trigger range (m). Nukes clamp here; ACNM nuclear gate stays separate.");
            ProximityMinAge = config.Bind("Missile", "ProximityMinAge", 0.4f,
                "Minimum flight time (s) before yield proximity can arm (stack with warhead IsArmed).");
            ProximityRequireClosing = config.Bind("Missile", "ProximityRequireClosing", true,
                "Require CPA-pass / closing geometry (vanilla ProxyFuse style) before detonating.");

            IalLabelNames = config.Bind("IAL", "LabelNames", true, "Append [IAL] on IAL / nuke variants");
            IalNameTag = config.Bind("IAL", "NameTag", "[IAL]", "Only mod suffix besides [10kt] on nukes");
            EnableEncyclopediaPages = config.Bind("IAL", "EncyclopediaPages", true,
                "Add encyclopedia missile + mount entries for IAL / nuke variants");
            BlockIalOnShips = config.Bind("IAL", "BlockIalOnShips", true,
                "Naval ships cannot select or fire IAL / [10kt] missile variants.");
            AiNukeChance = config.Bind("IAL", "AiNukeChance", 0.15f,
                "Chance (0-1) that AI PreferNukesFilter keeps nuclear / IAL options. ~0.15 = 15%.");
            IalHalfBlastRange = config.Bind("IAL", "HalfBlastRange", true,
                "IAL nuclear warheads use half BlastYield (half shockwave range).");
            BlockFriendlySetTarget = config.Bind("IFF", "BlockFriendlySetTarget", true,
                "Harmony-block Missile.SetTarget when target is friendly.");

            AffectAllMissiles = config.Bind("Filter", "AffectAllMissiles", true, "MM/IFF/nuke-clone apply to all missiles");
            MissileNameWhitelist = config.Bind("Filter", "MissileNameWhitelist", "AAM,SAM,AGM,Missile,Cruise",
                "Used when AffectAllMissiles=false");
            MountBlacklist = config.Bind("Unrestricted", "MountBlacklist",
                "afv,lcv,hlt,container,hook,flex,turret",
                "Comma substrings excluded from any-hardpoint list.");
            DebugLog = config.Bind("General", "DebugLog", false, "Verbose logging");
            BindOverpen(config);
            StrategicArsenal.Bind(config);
            KillAccolades.Bind(config);
            PlayerCareer.Bind(config);
            FlightAnalysis.Bind(config);
            FriendlyKillHud.Bind(config);
            MatchScoreboard.Bind(config);

            // Only patch this namespace �?safe when compiled into combined Oritasy.dll
            PatchOwnNamespace(new Harmony(PluginInfo.GUID));
            Log.LogInfo(PluginInfo.Name + " v" + PluginInfo.Version
                + " loaded (vanilla + nuke variants, MM, AGM-T, F9 arsenal, career menu).");
        }

        internal static bool IsVeyrnAcmPluginPresent()
        {
            try
            {
                if (Chainloader.PluginInfos != null
                    && Chainloader.PluginInfos.ContainsKey("com.qiaochen.veyrnacm"))
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
#if ORITASY_COMBINED
            // VeyrnWeapons.dll is the standalone pack for ACM-119 / ACNM-118 / AAM-2CV / TGM-85.
            return false;
#else
            return EnableAgmT != null && EnableAgmT.Value;
#endif
        }

        internal static void PatchOwnNamespace(Harmony harmony)
        {
            if (harmony == null)
                return;
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
                if (!HarmonyOriginalsExist(t))
                    continue;
                try { harmony.CreateClassProcessor(t).Patch(); }
                catch (Exception ex)
                {
                    if (Log != null)
                        Log.LogWarning("Harmony patch " + t.Name + ": " + ex.Message);
                }
            }
        }

        /// <summary>
        /// HarmonyX native-crashes when a HarmonyPatch argument list matches no game method.
        /// </summary>
        private static bool HarmonyOriginalsExist(Type t)
        {
            if (t == null)
                return false;
            try
            {
                MethodInfo tm = t.GetMethod("TargetMethod",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                if (tm != null)
                {
                    object mb = tm.Invoke(null, null);
                    if (mb == null)
                    {
                        if (Log != null)
                            Log.LogWarning("Skip Harmony " + t.Name + ": TargetMethod=null");
                        return false;
                    }
                }

                Type classDt = null;
                object[] classAttrs = t.GetCustomAttributes(typeof(HarmonyPatch), true);
                if (classAttrs != null)
                {
                    for (int i = 0; i < classAttrs.Length; i++)
                    {
                        HarmonyPatch hp = classAttrs[i] as HarmonyPatch;
                        if (hp != null && hp.info != null && hp.info.declaringType != null)
                            classDt = hp.info.declaringType;
                    }
                }
                if (classAttrs != null)
                {
                    string classMn = null;
                    Type[] classArgs = null;
                    for (int i = 0; i < classAttrs.Length; i++)
                    {
                        HarmonyPatch hp = classAttrs[i] as HarmonyPatch;
                        if (hp == null || hp.info == null)
                            continue;
                        if (!string.IsNullOrEmpty(hp.info.methodName))
                            classMn = hp.info.methodName;
                        if (hp.info.argumentTypes != null && hp.info.argumentTypes.Length > 0)
                            classArgs = hp.info.argumentTypes;
                    }
                    if (classDt != null && !string.IsNullOrEmpty(classMn)
                        && classArgs != null && AccessTools.Method(classDt, classMn, classArgs) == null)
                    {
                        if (Log != null)
                            Log.LogWarning("Skip Harmony " + t.Name + ": missing "
                                + classDt.Name + "." + classMn + " (" + classArgs.Length + " args)");
                        return false;
                    }
                }
                MethodInfo[] methods = t.GetMethods(
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                for (int m = 0; m < methods.Length; m++)
                {
                    object[] attrs = methods[m].GetCustomAttributes(typeof(HarmonyPatch), true);
                    if (attrs == null)
                        continue;
                    for (int a = 0; a < attrs.Length; a++)
                    {
                        HarmonyPatch hp = attrs[a] as HarmonyPatch;
                        if (hp == null || hp.info == null)
                            continue;
                        Type dt = hp.info.declaringType != null ? hp.info.declaringType : classDt;
                        string mn = hp.info.methodName;
                        Type[] args = hp.info.argumentTypes;
                        if (dt == null || string.IsNullOrEmpty(mn) || args == null || args.Length == 0)
                            continue;
                        if (AccessTools.Method(dt, mn, args) == null)
                        {
                            if (Log != null)
                                Log.LogWarning("Skip Harmony " + t.Name + ": missing "
                                    + dt.Name + "." + mn + " (" + args.Length + " args)");
                            return false;
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                if (Log != null)
                    Log.LogWarning("Skip Harmony " + t.Name + ": " + ex.Message);
                return false;
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

        internal static void RunUpdateBody()
        {
            if (!_wexonInitDone)
                return;
            if (OritasyMenuIdle())
            {
                PlayerCareer.Tick();
                return;
            }
            if (!_scannedAssets)
            {
                _scannedAssets = true;
                ScanLoadedWeaponAssets();
            }

            // Nuke FX resolve is expensive (FindObjectsOfTypeAll GameObject) �?retry slowly
            if (EnableNukeVariants.Value && SwapExplosionFx.Value && CachedNukeFx == null
                && Time.unscaledTime >= _nextNukeFxRetry)
            {
                _nextNukeFxRetry = Time.unscaledTime + 5f;
                ResolveNukeFx(false);
            }

            // Nuke mount clone scan is FindObjectsOfTypeAll �?never every frame
            if (EnableNukeVariants.Value && !NukeMountsInjected
                && Time.unscaledTime >= _nextNukeMountRetry)
            {
                _nextNukeMountRetry = Time.unscaledTime + 3f;
                EnsureNukeMountClones();
            }

            // AGM-T: inject + rare maintenance (injected path must not hammer FindObjectsOfTypeAll)
            if (AgmTOwnedByWeXon() && Time.unscaledTime >= _nextAgmTRetry)
            {
                _nextAgmTRetry = Time.unscaledTime + (AgmTWeapon.IsInjected ? 90f : 2f);
                AgmTWeapon.Ensure();
            }

            // LowEnd: stagger non-critical WeXon ticks (FlightAnalysis keeps own SampleHz).
            bool lowEnd = CachedOritasyIsLow();
            int frame = Time.frameCount;
            if (!lowEnd || (frame % 2) == 0)
                StrategicArsenal.Tick();
            if (!lowEnd || (frame % 2) == 0)
                KillAccolades.Tick();
            if (!lowEnd || (frame % 2) == 1)
                PlayerCareer.Tick();
            if (!lowEnd)
                FlightAnalysis.Tick();
            if (!lowEnd || (frame % 4) == 1)
                FriendlyKillHud.Tick();
            if (!lowEnd || (frame % 4) == 3)
                MatchScoreboard.Tick();

            // Mount cache: only refresh when empty, or rarely for late-loaded assets
            if (EnableUnrestricted.Value && Time.unscaledTime >= NextMountRefresh)
            {
                NextMountRefresh = Time.unscaledTime + 90f;
                if (CachedMounts.Count == 0)
                    RefreshMountCache();
            }

            // Encyclopedia brand once (was every frame �?major hitch)
            if (EnableEncyclopediaPages.Value && !_encyclopediaReady
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

        private void OnGUI()
        {
#if ORITASY_COMBINED
            return;
#else
            RunGuiBody();
#endif
        }

        internal static void RunGuiBody()
        {
            if (!_wexonInitDone)
                return;
            // First-run welcome / boot splash owns the screen
            if (OritasyHudBlocked())
                return;
            if (OritasyMainMenuActive())
            {
                PlayerCareer.DrawGui();
                return;
            }

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

        private static bool _oritasyHudResolved;
        private static System.Reflection.PropertyInfo _oritasyBlocksHudProp;
        private static bool _oritasyMenuResolved;
        private static System.Reflection.PropertyInfo _oritasyMainMenuProp;
        private static bool _oritasyIsLowResolved;
        private static System.Reflection.PropertyInfo _oritasyIsLowProp;

        private static bool CachedOritasyIsLow()
        {
#if ORITASY_COMBINED
            try { return Oritasy.PerfMode.IsLow; }
            catch { return false; }
#else
            try
            {
                if (!_oritasyIsLowResolved)
                {
                    _oritasyIsLowResolved = true;
                    Type perf = Type.GetType("Oritasy.PerfMode");
                    if (perf != null)
                    {
                        _oritasyIsLowProp = perf.GetProperty("IsLow",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    }
                }
                if (_oritasyIsLowProp == null)
                    return false;
                object v = _oritasyIsLowProp.GetValue(null, null);
                return v is bool && (bool)v;
            }
            catch { return false; }
#endif
        }

        internal static bool OritasyMenuIdle()
        {
            return OritasyMainMenuActive();
        }

        internal static bool OritasyMainMenuActive()
        {
#if ORITASY_COMBINED
            try { return Oritasy.OritasyPresentation.MainMenuActive; }
            catch { return false; }
#else
            try
            {
                if (!_oritasyMenuResolved)
                {
                    _oritasyMenuResolved = true;
                    Type t = Type.GetType("Oritasy.OritasyPresentation, Oritasy")
                        ?? Type.GetType("Oritasy.OritasyPresentation");
                    if (t != null)
                    {
                        _oritasyMainMenuProp = t.GetProperty("MainMenuActive",
                            System.Reflection.BindingFlags.Static
                            | System.Reflection.BindingFlags.Public
                            | System.Reflection.BindingFlags.NonPublic);
                    }
                }
                if (_oritasyMainMenuProp == null)
                    return false;
                object v = _oritasyMainMenuProp.GetValue(null, null);
                return v is bool && (bool)v;
            }
            catch { return false; }
#endif
        }

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
        /// Cosmetic tags: all WeXon missiles get [IAL]; only nuke clones also get [10kt].
        /// Identity (ship block / PreferNukes / ApplyNuke) uses jsonKey _IAL / NukeInfoIds �?not the name tag.
        /// </summary>
        internal static void ApplyNameTags(ref string name, ref string shortN, ref string desc, bool nuke)
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

            // Ordinary + nuke clones: [IAL] suffix for branding
            if (IalLabelNames.Value)
            {
                AppendTag(ref name, IalNameTag.Value);
                AppendTag(ref shortN, IalNameTag.Value);
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
            if (info == null || !info.nuclear)
                return false;
            if (IsIalNukeCloneInfo(info))
                return false;
            if (NukeInfoIds.Contains(info.GetInstanceID()))
                return false;
            if (AgmTWeapon.IsAgmTInfo(info) || LooksLikeForeignPackInfo(info))
                return false;
            return true;
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

        /// <summary>Vanilla weapons that already ship nuclear (Genie, 20kt cruise, tacNuke bombs, �?.</summary>
        internal static bool IsVanillaNuclearInfo(WeaponInfo info)
        {
            if (info == null || !info.nuclear)
                return false;
            // Our own AGM-T / IAL clones are not "vanilla nuclear"
            if (AgmTWeapon.IsAgmTInfo(info) || LooksLikeForeignPackInfo(info))
                return false;
            if (IsIalNukeCloneInfo(info) || NukeInfoIds.Contains(info.GetInstanceID()))
                return false;
            if (info.name != null && info.name.IndexOf("_IAL", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
            return true;
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

        internal static bool IsNukeVariantInfo(WeaponInfo info)
        {
            if (info == null)
                return false;
            if (LooksLikeForeignPackInfo(info))
                return false;
            // Prefer clone registry �?do not treat bare "[IAL]" or info.nuclear as proof
            // (old ApplyNukeForced polluted shared vanilla WeaponInfo.nuclear �?false nukes)
            if (NukeInfoIds.Contains(info.GetInstanceID()))
                return true;
            string n = (info.weaponName != null ? info.weaponName : string.Empty)
                + " " + (info.shortName != null ? info.shortName : string.Empty)
                + " " + (info.name != null ? info.name : string.Empty);
            // Yield tag or asset name _IAL �?cosmetic "[IAL]" alone is never nuclear
            if (n.IndexOf("[10kt]", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("[20kt]", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            // Instantiated clone assets are named …_IAL (not the display tag)
            if (info.name != null && info.name.IndexOf("_IAL", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        /// <summary>True only for WeXon IAL nuke clones (never vanilla nuclear like AIR-2 Genie).</summary>
        internal static bool IsIalNukeCloneInfo(WeaponInfo info)
        {
            if (info == null)
                return false;
            if (LooksLikeForeignPackInfo(info))
                return false;
            if (NukeInfoIds.Contains(info.GetInstanceID()))
                return true;
            string n = (info.weaponName != null ? info.weaponName : string.Empty)
                + " " + (info.shortName != null ? info.shortName : string.Empty)
                + " " + (info.name != null ? info.name : string.Empty);
            bool hasYieldTag = n.IndexOf("[10kt]", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("[20kt]", StringComparison.OrdinalIgnoreCase) >= 0;
            bool hasIal = n.IndexOf("[IAL]", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("_IAL", StringComparison.OrdinalIgnoreCase) >= 0;
            return hasYieldTag && hasIal;
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
            if (unit == null)
                return false;
            if (unit is Ship)
                return true;
            try { return unit.GetComponentInParent<Ship>() != null; }
            catch { return false; }
        }

        /// <summary>Missile fired from a ship / naval launcher (RAM45, VLS SAMs, etc.).</summary>
        internal static bool IsShipLaunchedMissile(Missile missile)
        {
            if (missile == null)
                return false;
            try
            {
                if (IsShipUnit(missile.owner))
                    return true;
            }
            catch { }
            try
            {
                if (missile.transform != null && missile.transform.root != null
                    && missile.transform.root.GetComponent<Ship>() != null)
                    return true;
            }
            catch { }
            return false;
        }

        /// <summary>Low-speed SD threshold in m/s (config stored as km/h).</summary>
        internal static float LowSpeedSelfDestructThresholdMps()
        {
            float kmh = LowSpeedSelfDestructMps != null ? LowSpeedSelfDestructMps.Value : 15f;
            return LowSpeedSdGateService.ResolveThresholdMps(kmh);
        }

        /// <summary>
        /// Soft VLS / CIWS rails often spawn with near-zero muzzle velocity; MultiMode then
        /// deploys fins and the missile tumbles. Kick along nose until min launch speed.
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
            try
            {
                MissileSeeker seeker = SeekerField != null
                    ? SeekerField.GetValue(missile) as MissileSeeker
                    : null;
                sarh = seeker is SARHSeeker;
            }
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

        /// <summary>True when Oritasy [IAL]/[10kt] nuke clones may appear for player or AI.</summary>
        internal static bool OritasyNukesAllowed
        {
            get { return EnableNukeVariants == null || EnableNukeVariants.Value; }
        }

        /// <summary>Strip only Oritasy IAL nuclear clones. Vanilla Genie / tacNuke stay.</summary>
        internal static void RemoveIalNukeClonesFromList(List<WeaponMount> list)
        {
            if (list == null || list.Count == 0)
                return;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                WeaponMount m = list[i];
                if (m == null)
                    continue;
                if (IsIalNukeCloneInfo(m.info))
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
            string name = def.unitName != null ? def.unitName : string.Empty;
            return IsIalKey(key) || name.IndexOf("[IAL]", StringComparison.OrdinalIgnoreCase) >= 0;
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
            // mm ignored �?only [IAL] / [10kt]
            ApplyNameTags(ref name, ref shortN, ref desc, nuke);
            info.weaponName = name;
            info.shortName = shortN;
            info.description = desc;
        }

        internal static void LabelMount(WeaponMount mount, bool nuke, bool mm)
        {
            if (mount == null)
                return;
            if (AgmTWeapon.IsAgmTMount(mount) || LooksLikeForeignPackMount(mount))
                return;

            if (mount.info != null)
                ApplyWeaponInfoFlags(mount.info, IsNukeVariantInfo(mount.info));

            int id = mount.GetInstanceID();
            if (TouchedMounts.Contains(id))
                return;
            TouchedMounts.Add(id);

            string n = mount.mountName;
            StripTag(ref n, "[MM]");
            StripTag(ref n, "[20kt]");
            if (MmNameTag != null)
                StripTag(ref n, MmNameTag.Value);
            // All WeXon missile mounts: [IAL] cosmetic; [10kt] only on nuke clones
            if (IalLabelNames.Value)
                AppendTag(ref n, IalNameTag.Value);
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
                if (IsIalKey(src.jsonKey) || IsNukeVariantInfo(src.info))
                    continue;
                // Already nuclear in vanilla (Genie / 20kt cruise / tacNuke / �? �?no IAL twin
                if (IsVanillaNuclearWeaponInfo(src.info))
                    continue;
                if (LooksLikeForeignPackMount(src))
                    continue;
                if (AgmTWeapon.IsAgmTMount(src) || AgmTWeapon.IsAgmTKey(src.jsonKey))
                    continue;
                if (Aam2CvWeapon.IsAam2CvMount(src) || Aam2CvWeapon.IsAam2CvKey(src.jsonKey))
                    continue;
                if (LooksLikeKh85Blob(src.jsonKey) || LooksLikeKh85Blob(src.name)
                    || LooksLikeKh85Blob(src.mountName))
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
                AppendTag(ref mn, NukeNameTag.Value);
                AppendTag(ref mn, IalNameTag.Value);
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
                if (LooksLikeForeignPackDef(src))
                    continue;
                if (AgmTWeapon.IsAgmTKey(src.jsonKey))
                    continue;
                if (Aam2CvWeapon.IsAam2CvKey(src.jsonKey))
                    continue;
                if (LooksLikeKh85Blob(src.jsonKey) || LooksLikeKh85Blob(src.unitName)
                    || LooksLikeKh85Blob(src.name))
                    continue;
                if (IsVanillaNuclearMissileDef(src))
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
        }

        internal static void RefreshEncyclopediaData()
        {
            // Always re-bind AGM-T (cheap) �?early inject can miss AfterLoad
            AgmTWeapon.Ensure();

            // Hangar / encyclopedia UI used to re-run full inject+brand every open �?huge hitch
            if (EncyclopediaDataReady)
                return;

            EnsureNukeMountClones();
            EnsureIalEncyclopedia();
#if ORITASY_COMBINED
            try { Oritasy.KeAmmoCloneService.Ensure(); }
            catch { }
#endif
            BrandEncyclopediaEntries(GetEncyclopedia());

            bool nukesOk = !EnableNukeVariants.Value || NukeMountsInjected;
            bool agmOk = EnableAgmT == null || !EnableAgmT.Value || AgmTWeapon.IsInjected;
            bool encOk = !EnableEncyclopediaPages.Value || EncyclopediaInjected
                || IsEncyclopediaPopulated(GetEncyclopedia());
            if (nukesOk && agmOk && encOk)
                EncyclopediaDataReady = true;
        }

        /// <summary>
        /// Labels + flags. Nuke flags only on real IAL nuke clones; [IAL] name on all allowed missiles.
        /// </summary>
        internal static void ApplyWeaponInfoFlags(WeaponInfo info, bool forceNuke)
        {
            if (info == null || !IsMissileInfoAllowed(info))
                return;
            if (IsKeReplicaInfo(info))
                return;
            // Keep AGM-T / Veyrn / Pulsar display names (no Oritasy [10kt] stamp)
            if (AgmTWeapon.IsAgmTInfo(info) || LooksLikeForeignPackInfo(info))
                return;

            bool nukeClone = forceNuke || IsIalNukeCloneInfo(info) || NukeInfoIds.Contains(info.GetInstanceID());
            bool wantLabel = IalLabelNames.Value || EnableMultiMode.Value || nukeClone;
            if (!wantLabel)
                return;

            int id = info.GetInstanceID();
            if (!TouchedInfos.Add(id))
                return; // already labeled

            // Never promote vanilla WeaponInfo to nuclear
            if (nukeClone)
            {
                if (SetNuclearFlag.Value)
                    info.nuclear = true;
                if (SetStrategicFlag.Value)
                    info.strategic = true;
                info.blastDamage = GetIalNukeBlastYield();
            }

            LabelInfo(info, nukeClone, EnableMultiMode.Value);

            if (DebugLog.Value)
                Log.LogInfo("Labeled WeaponInfo: " + info.weaponName + " nukeClone=" + nukeClone);
        }

        private static void ScanLoadedWeaponAssets()
        {
            if (EnableNukeVariants.Value)
                ResolveNukeFx(false);
            EnsureNukeMountClones();
            AgmTWeapon.Ensure();
            // Strip false [10kt]/nuclear from vanilla; re-apply cosmetic [IAL]
            SanitizeFalseIalLabels();
#if ORITASY_COMBINED
            try { Oritasy.KeAmmoCloneService.Ensure(); }
            catch { }
#endif
            RefreshMountCache();

            int n = 0;
            WeaponInfo[] infos = Resources.FindObjectsOfTypeAll<WeaponInfo>();
            for (int i = 0; i < infos.Length; i++)
            {
                WeaponInfo info = infos[i];
                if (info == null || !IsMissileInfoAllowed(info))
                    continue;
                ApplyWeaponInfoFlags(info, IsIalNukeCloneInfo(info));
                n++;
            }

            WeaponMount[] mounts = Resources.FindObjectsOfTypeAll<WeaponMount>();
            for (int i = 0; i < mounts.Length; i++)
            {
                WeaponMount m = mounts[i];
                if (m == null || m.info == null || !IsMissileInfoAllowed(m.info))
                    continue;
                bool nukeClone = IsIalMount(m) || IsIalNukeCloneInfo(m.info);
                LabelMount(m, nukeClone, EnableMultiMode.Value);
            }

            Log.LogInfo("Scan: infos=" + n + " mounts=" + CachedMounts.Count
                + " nukeClones=" + NukeMountClones.Count
                + " fx=" + (CachedNukeFx != null ? CachedNukeFx.name : "PENDING")
                + " ([IAL] cosmetic on all; identity via _IAL key)");
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
            if (IsKeReplicaMount(mount))
            {
                weapon.info = mount.info;
                return;
            }
            // Only the IAL nuke-clone path needs this sync
            if (!IsIalKey(mount.jsonKey) && !IsIalNukeCloneInfo(mount.info))
                return;
            weapon.info = mount.info;
            ApplyWeaponInfoFlags(mount.info, true);
            if (DebugLog.Value)
                Log.LogInfo("Synced nuke WeaponInfo onto " + weapon.name + " mount=" + mount.mountName);
        }

        internal static bool IsKeReplicaMount(WeaponMount mount)
        {
            if (mount == null)
                return false;
            if (IsKeReplicaKey(mount.jsonKey))
                return true;
            return IsKeReplicaInfo(mount.info);
        }

        internal static bool IsKeReplicaKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
            return key.EndsWith("_KE", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsKeReplicaInfo(WeaponInfo info)
        {
            if (info == null)
                return false;
            try
            {
                string n = (info.weaponName != null ? info.weaponName : string.Empty) + " "
                    + (info.shortName != null ? info.shortName : string.Empty);
                if (n.IndexOf("[KE]", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            catch { }
            return false;
        }

        internal static bool IsKeReplicaMissile(Missile missile)
        {
            if (missile == null)
                return false;
            try
            {
                if (missile.definition != null && IsKeReplicaKey(missile.definition.jsonKey))
                    return true;
            }
            catch { }
            try
            {
                WeaponInfo inf = InfoField != null ? InfoField.GetValue(missile) as WeaponInfo : null;
                if (IsKeReplicaInfo(inf))
                    return true;
            }
            catch { }
            return false;
        }

        internal static bool IsNukeMount(WeaponMount mount)
        {
            if (mount == null)
                return false;
            if (IsIalKey(mount.jsonKey))
                return true;
            return IsIalNukeCloneInfo(mount.info);
        }

        /// <summary>IAL / [10kt] clones ignore faction airbase warhead stockpile limits.</summary>
        internal static bool IsIalExemptFromWarheadQuota(WeaponInfo info)
        {
            if (AgmTWeapon.IsAgmTInfo(info) || LooksLikeForeignPackInfo(info))
                return true;
            return IsIalNukeCloneInfo(info);
        }

        internal static bool IsIalExemptFromWarheadQuota(WeaponMount mount)
        {
            if (mount == null)
                return false;
            if (IsIalKey(mount.jsonKey))
                return true;
            // ACNM-118 uses its own 1.5kt bus �?never consume faction warhead stockpile
            if (AgmTWeapon.IsAgmTMount(mount) || LooksLikeForeignPackMount(mount))
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
        /// Remove false [10kt]/nuclear from vanilla; keep/re-apply cosmetic [IAL].
        /// Clone infos (NukeInfoIds) are left alone. Genie-style vanilla nukes (nuclear, no IAL) kept.
        /// </summary>
        internal static void SanitizeFalseIalLabels()
        {
            RebuildNukeInfoIdsFromClones();
            float ialYield = GetIalNukeBlastYield();

            WeaponInfo[] infos = Resources.FindObjectsOfTypeAll<WeaponInfo>();
            for (int i = 0; i < infos.Length; i++)
            {
                WeaponInfo info = infos[i];
                if (info == null || NukeInfoIds.Contains(info.GetInstanceID()))
                    continue;
                if (AgmTWeapon.IsAgmTInfo(info) || LooksLikeForeignPackInfo(info))
                    continue;
                if (!IsMissileInfoAllowed(info))
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
                if (IalLabelNames.Value)
                {
                    AppendTag(ref name, IalNameTag.Value);
                    AppendTag(ref shortN, IalNameTag.Value);
                }
                info.weaponName = name;
                info.shortName = shortN;
                // Strip WeXon pollution: nuclear flag / IAL yield on non-clone infos.
                // Keep genuine vanilla nukes (e.g. Genie): nuclear without IAL cosmetic / yield tag.
                if (info.nuclear && (hadYield || hasIalCosmetic || yieldLooksIal))
                    info.nuclear = false;
                info.description = desc;
                TouchedInfos.Remove(info.GetInstanceID());
            }

            WeaponMount[] mounts = Resources.FindObjectsOfTypeAll<WeaponMount>();
            for (int i = 0; i < mounts.Length; i++)
            {
                WeaponMount m = mounts[i];
                if (m == null || IsIalKey(m.jsonKey))
                    continue;
                if (LooksLikeForeignPackMount(m))
                    continue;
                if (AgmTWeapon.IsAgmTMount(m))
                    continue;
                if (Aam2CvWeapon.IsAam2CvMount(m))
                    continue;
                if (LooksLikeKh85Blob(m.jsonKey) || LooksLikeKh85Blob(m.mountName))
                    continue;
                if (m.info != null && NukeInfoIds.Contains(m.info.GetInstanceID()))
                    continue;
                string n = m.mountName;
                StripTag(ref n, "[10kt]");
                StripTag(ref n, "[20kt]");
                StripTag(ref n, "[MM]");
                if (IalLabelNames.Value)
                    AppendTag(ref n, IalNameTag.Value);
                m.mountName = n;
                TouchedMounts.Remove(m.GetInstanceID());
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
            // Only real IAL nuke mounts / clone infos �?never bare [IAL] cosmetic or polluted .nuclear
            if (!IsNukeMount(mount) && !IsIalNukeCloneInfo(weapon.info))
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
            // AGM-T bus / GS25 use their own warhead path �?never IAL ApplyNuke
            if (AgmTWeapon.ShouldBlockIalPendingNuke(missile))
                return;
            // Only WeXon IAL clones �?never vanilla nuclear (Genie) or name-tag false positives
            WeaponInfo info = InfoField != null ? InfoField.GetValue(missile) as WeaponInfo : null;
            if (!IsIalNukeCloneInfo(info))
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
                // FX only — do not Arm here (ACNM nuclear arm delay / safe distance).
                ApplyWarheadFx(missile, false);
                ApplyAgmTDragReduction(missile);
                if (DebugLog != null && DebugLog.Value)
                    Log.LogInfo("AGM-T nuke warhead on " + missile.name + " yield=" + y + " (unarmed until safe)");
            }
            catch (Exception ex)
            {
                Log.LogWarning("ApplyAgmTNukeWarhead: " + ex.Message);
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
                    // Never mutate shared vanilla WeaponInfo �?that made [IAL]-only missiles look nuclear.
                    // Instance blastYield + warhead FX above are enough for this missile's detonation.
                    bool cloneInfo = NukeInfoIds.Contains(info.GetInstanceID())
                        || IsIalNukeCloneInfo(info)
                        || (info.name != null && info.name.IndexOf("_IAL", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (cloneInfo)
                    {
                        if (SetNuclearFlag.Value)
                            info.nuclear = true;
                        info.blastDamage = GetIalNukeBlastYield();
                        NukeInfoIds.Add(info.GetInstanceID());
                        ApplyWeaponInfoFlags(info, true);
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
            bool cloneInfo = IsIalNukeCloneInfo(info);
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
                AgmTWeapon.ShouldBlockIalPendingNuke(missile),
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
                return;

            PendingNukeSpawns--;
            ApplyNukeForced(missile, reason + "+pending");
            if (PendingNukeSpawns <= 0)
            {
                PendingNukeWeapon = null;
                PendingNukeOwner = null;
            }
        }

        /// <summary>Strip MultiModeBrain if one was attached to a gun shell / TBM / cruise / AGM-T bus by mistake.</summary>
        internal static void StripIncompatibleBrain(Missile missile)
        {
            if (missile == null)
                return;
            bool shell = IsGunShellMissile(missile);
            bool ballistic = IsBallisticMissile(missile);
            bool cruise = IsCruiseMissile(missile);
            bool agmT = false;
            try
            {
                agmT = LooksLikePackMissile(missile)
                    || AgmTWeapon.HasBusDispenser(missile)
                    || AgmTWeapon.IsAgmTMissile(missile);
            }
            catch { }
            if (!MissileClassifyGateService.ShouldStripIncompatibleBrain(shell, ballistic, cruise, agmT))
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
                // Restore TBM gLimit=0 (unlimited) if MM raised it
                if (ballistic)
                {
                    FieldInfo gLim = AccessTools.Field(typeof(Missile), "gLimit");
                    if (gLim != null)
                        gLim.SetValue(missile, 0f);
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

        /// <summary>TGM-85 is owned by VeyrnWeapons. Name-blob only so IAL / TBM / fuze skip it.</summary>
        internal static bool IsKh85CTerrainMissile(Missile missile)
        {
            return LooksLikeKh85Missile(missile);
        }

        internal static bool IsKh85Missile(Missile missile)
        {
            return LooksLikeKh85Missile(missile);
        }

        internal static bool IsLikelyUnstampedKh85Launch(Missile missile)
        {
            return false;
        }

        internal static bool ShouldDeferKh85GuideTo(Missile missile)
        {
            return false;
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
                assist = missile.gameObject.AddComponent<TbmHuntAssist>();
            assist.Setup(missile);
        }

        /// <summary>
        /// Suppress burnout self-destruct only while brain has a live engageable target.
        /// Coasting / no-target must NOT suppress — otherwise missiles loft forever.
        /// </summary>
        internal static bool ShouldSuppressSeekerSelfDestruct(Missile missile)
        {
            if (missile == null)
                return false;
            bool agmTBus = AgmTWeapon.HasBusDispenser(missile) && !AgmTDispenser.IsSafeDiscard(missile);
            bool kh85 = LooksLikeKh85Missile(missile);
            MultiModeBrain brain = MultiModeBrain.Find(missile);
            bool brainHunt = brain != null && brain.HasActiveHuntTarget;
            if (SeekerIffGateService.ShouldSuppressSeekerSelfDestruct(
                IsGunShellMissile(missile), IsRocketOrUnguidedMissile(missile)))
                return agmTBus || kh85 || brainHunt;
            return false;
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
#if ORITASY_COMBINED
            // Escalation 30fps budget: low-speed SD is polish; skip on LowEnd.
            if (Oritasy.PerfMode.IsLow)
                return;
#endif
            if (missile == null || missile.disabled)
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
                LooksLikePackMissile(missile)
                    || AgmTWeapon.HasBusDispenser(missile)
                    || AgmTWeapon.IsAgmTMissile(missile),
                IsGunShellSeeker(caller),
                IsBallisticMissile(missile) || caller is BallisticMissileGuidance,
                IsCruiseMissile(missile) || caller is OpticalSeekerCruiseMissile,
                IsRocketOrUnguidedName(missile != null ? missile.name : null),
                IsRocketOrUnguidedName(wn) || IsBallisticMissileName(wn),
                seekerMismatch);

            if (path == MultiModeSetupGateService.Path.StripAgmT)
            {
                StripIncompatibleBrain(missile);
                return;
            }
            if (path != MultiModeSetupGateService.Path.AllowAttach)
                return;

            MultiModeBrain brain = MultiModeBrain.Find(missile);
            if (brain == null)
                brain = missile.GetComponent<MultiModeBrain>();
            if (brain == null)
                brain = missile.gameObject.AddComponent<MultiModeBrain>();
            brain.Setup(missile, caller != null ? caller : primary, target, aimpoint);
        }

        internal static FactionHQ GetHq(Unit unit)
        {
            if (unit == null)
                return null;
            // Buildings / ground units often have MapHQ while NetworkHQ is still null.
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
            if (candidate is Missile)
            {
                Missile shot = candidate as Missile;
                if (shot == null)
                    return false;
                if (IsLocalHumanShooter(shooterSide))
                    return false;
                if (!IsPlayerLaunchedMissile(shot))
                    return false;
            }
            if (!IsUnitAlive(candidate))
                return false;
            if (candidate is Scenery)
                return false;

            if (!EnableIff.Value)
                return true;

            // Confirmed same faction — never allow
            return SeekerIffGateService.SoftIffAllows(true, IsSameFaction(shooterSide, candidate));
        }

        /// <summary>
        /// Local player's own missiles must not lock other missiles. Opposing AI
        /// interceptors may lock missiles the local human launched.
        /// </summary>
        internal static bool IsPlayerLaunchedMissile(Missile m)
        {
            if (m == null)
                return false;
            try
            {
                if (m.disabled)
                    return false;
            }
            catch { return false; }

            Unit owner = null;
            try { owner = m.owner; }
            catch { owner = null; }
            if (owner == null)
            {
                try
                {
                    Unit resolved;
                    if (UnitRegistry.TryGetUnit(m.ownerID, out resolved))
                        owner = resolved;
                }
                catch { }
            }
            return IsLocalHumanShooter(owner);
        }

        internal static bool IsLocalHumanShooter(Unit shooter)
        {
            if (shooter == null)
                return false;
            Aircraft ac = shooter as Aircraft;
            if (ac == null)
            {
                try { ac = shooter.GetComponentInParent<Aircraft>(); }
                catch { ac = null; }
            }
            if (ac == null)
                return false;
            Player p = null;
            try { p = ac.Player; }
            catch { p = null; }
            if (p == null)
                return false;
            return IsLocalHumanPlayer(p);
        }

        /// <summary>
        /// Strict hostility for free-hunt: both sides must have HQ and be different factions.
        /// Prevents chasing nearest friendly when missile/owner HQ resolution is incomplete.
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

            if (!EnableIff.Value)
            {
                return SeekerIffGateService.StrictHuntAllows(
                    false,
                    IsAllowedTarget(ResolveShooterSide(missile), candidate),
                    false, false, false);
            }

            FactionHQ shootHq = GetShooterHq(missile);
            FactionHQ tgtHq = GetHq(candidate);
            return SeekerIffGateService.StrictHuntAllows(
                true,
                false,
                shootHq != null,
                tgtHq != null,
                shootHq != null && tgtHq != null && object.ReferenceEquals(shootHq, tgtHq));
        }

        /// <summary>Player/AI lock from launching aircraft (primary target / WM list / missile.target / targetID).</summary>
        /// <summary>Player/AI lock. Player shots: pilot primary wins over stale missile.target (LOAL must not override).</summary>
        internal static Unit ResolveDesignatedTarget(Missile missile)
        {
            if (missile == null)
                return null;

            Unit side = ResolveShooterSide(missile);
            bool playerShot = IsLocalHumanShooter(side) || IsPlayerLaunchedMissile(missile);

            Unit owner = missile.owner;
            Aircraft ac = owner as Aircraft;
            if (ac == null && owner != null)
                ac = owner.GetComponentInParent<Aircraft>();

            // Player: stick/radar lock first so LOAL free-hunt cannot outrank it.
            if (playerShot && ac != null)
            {
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
                            if (pt != null && IsDesignatedEngageable(missile, side, pt))
                                return pt;
                        }
                    }
                }
                catch { }
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
                                if (u != null && IsDesignatedEngageable(missile, side, u))
                                    return u;
                            }
                        }
                    }
                }
                catch { }
            }

            try
            {
                Unit mt = MissileTargetField != null ? MissileTargetField.GetValue(missile) as Unit : null;
                if (mt != null && IsDesignatedEngageable(missile, side, mt))
                    return mt;
            }
            catch { }

            try
            {
                PersistentID tid = missile.targetID;
                if (tid.IsValid)
                {
                    Unit byId;
                    if (UnitRegistry.TryGetUnit(tid, out byId) && IsDesignatedEngageable(missile, side, byId))
                        return byId;
                }
            }
            catch { }

            if (!playerShot && ac != null)
            {
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
                            if (pt != null && IsDesignatedEngageable(missile, side, pt))
                                return pt;
                        }
                    }
                }
                catch { }
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
                                if (u != null && IsDesignatedEngageable(missile, side, u))
                                    return u;
                            }
                        }
                    }
                }
                catch { }
            }

            return null;
        }

        /// <summary>Designated locks: soft IFF (Allowed) or strict hostile — never fail open to LOAL.</summary>
        internal static bool IsDesignatedEngageable(Missile missile, Unit side, Unit candidate)
        {
            if (candidate == null)
                return false;
            if (IsAllowedTarget(side, candidate))
                return true;
            return IsHostileHuntTarget(missile, candidate);
        }

        internal static void EnforceIffOnCurrentSeeker(Missile missile)
        {
            if (missile == null || SeekerField == null)
                return;
            MissileSeeker seeker = SeekerField.GetValue(missile) as MissileSeeker;
            if (seeker == null)
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

        /// <summary>IR / radar AAM — eligible for HOBS rear snap + recover (not AGM/cruise/TBM).</summary>
        internal static bool IsAirToAirMissile(Missile missile, MissileSeeker primary)
        {
            if (missile == null)
                return false;
            if (IsCruiseMissile(missile) || IsGunShellMissile(missile) || IsMotorlessProjectile(missile))
                return false;
            if (IsShipLaunchedMissile(missile))
                return false;
            try { if (AgmTWeapon.IsAgmTMissile(missile)) return false; } catch { }
            if (primary != null && IsSurfaceAttackSeeker(primary))
                return false;
            if (primary != null)
            {
                System.Type t = primary.GetType();
                if (typeof(IRSeeker).IsAssignableFrom(t)
                    || typeof(ARHSeeker).IsAssignableFrom(t)
                    || typeof(SARHSeeker).IsAssignableFrom(t)
                    || t.Name.IndexOf("IR", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || t.Name.IndexOf("ARH", System.StringComparison.OrdinalIgnoreCase) >= 0
                    || t.Name.IndexOf("SARH", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            string n = missile.name ?? "";
            if (missile.definition != null)
            {
                if (!string.IsNullOrEmpty(missile.definition.unitName)) n = missile.definition.unitName;
                else if (!string.IsNullOrEmpty(missile.definition.jsonKey)) n = missile.definition.jsonKey;
            }
            return n.IndexOf("AAM", System.StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("AA-", System.StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("AIM", System.StringComparison.OrdinalIgnoreCase) >= 0;
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
            if (wm == null || !AgmTOwnedByWeXon())
                return;
            AgmTWeapon.Ensure();
            // Only this manager �?never global FindObjectsOfTypeAll on every spawn/Awake.
            AgmTWeapon.InjectIntoWeaponManager(wm);
        }
    }

    internal static class PluginInfo
    {
        public const string GUID = "com.qiaochen.wexon";
        public const string Name = "WeXon";
        public const string Version = "2.5.65";
    }

    [HarmonyPatch(typeof(WeaponMount), "Initialize")]
    internal static class Patch_WeaponMount_Initialize_AgmT
    {
        [HarmonyPostfix]
        private static void Postfix(WeaponMount __instance)
        {
            if (!Plugin.AgmTOwnedByWeXon())
                return;
            if (__instance != null && AgmTWeapon.IsAgmTKey(__instance.jsonKey))
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
            Plugin.SetupMultiMode(missile, __instance, target, aimpoint);
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

            Missile missile = PluginAccess.GetMissile(__instance);
            // Never skip vanilla Seek �?null missile resolve must not hard-block guidance
            if (missile == null)
                return true;
            if (Plugin.IsGunShellMissile(missile) || Plugin.IsBallisticMissile(missile)
                || Plugin.IsCruiseMissile(missile) || __instance is OpticalSeekerCruiseMissile)
                return true;

            FieldInfo ms = Plugin.SeekerMissileField;
            if (ms != null && ms.GetValue(__instance) == null)
                ms.SetValue(__instance, missile);

            // Clears ejected pilots always; other IFF clears when EnableIff.
            Plugin.EnforceIffOnCurrentSeeker(missile);

            // Let Seek run for Arm/DeployFins — aimpoint is restored in Postfix
            // (vanilla Seek overwrites aimpoint to forward-coast when it has no radar lock)
            return true;
        }

        [HarmonyPostfix]
        private static void Postfix(MissileSeeker __instance)
        {
            if (__instance == null)
                return;
            Missile missile = PluginAccess.GetMissile(__instance);
            if (missile == null)
                return;
            // Guidance restored in Missile.Steering Prefix (avoid triple Reapply per frame)
        }
    }

    [HarmonyPatch(typeof(ARHSeeker), "Seek")]
    internal static class Patch_ARHSeeker_Seek_Guard
    {
        private static readonly FieldInfo RadarParamsField = AccessTools.Field(typeof(ARHSeeker), "radarParameters");

        [HarmonyPrefix]
        private static bool Prefix(ARHSeeker __instance)
        {
            if (__instance == null)
                return false;
            Missile missile = PluginAccess.GetMissile(__instance);
            if (missile == null)
                return false;
            // Incomplete AddComponent ARH (no radarParameters) must not run
            if (RadarParamsField != null && RadarParamsField.GetValue(__instance) == null)
            {
                FieldInfo sf = Plugin.SeekerField;
                if (sf != null && object.ReferenceEquals(sf.GetValue(missile), __instance))
                {
                    MissileSeeker primary = missile.GetComponent<MissileSeeker>();
                    if (primary != null && !object.ReferenceEquals(primary, __instance))
                        sf.SetValue(missile, primary);
                }
                return false;
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
                TbmHuntAssist tbm = __instance.GetComponent<TbmHuntAssist>();
                if (SeekerIffGateService.ShouldBlockNullTargetClear(
                        brain != null && brain.HasActiveHuntTarget,
                        tbm != null && tbm.HasActiveTarget))
                    return false;
            }
            // Always block ejected pilots / parachute crew — never re-lock onto them.
            if (target != null && Plugin.IsEjectedPilotUnit(target))
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
                return false;
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
                    || Plugin.IsKeReplicaMount(weaponMount)
                    || AgmTWeapon.IsAgmTMount(weaponMount)
                    || AgmTWeapon.IsAgmTInfo(weapon.info)))
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
#if ORITASY_COMBINED
            Oritasy.KeAmmoCloneService.NoteFire(__instance);
#endif
        }
    }

        [HarmonyPatch(typeof(Missile), "FixedUpdate")]
    internal static class Patch_Missile_FixedUpdate
    {
        [HarmonyPrefix]
        private static void Prefix(Missile __instance, ref MultiModeBrain __state)
        {
            __state = null;
            if (__instance == null)
                return;
            __state = MultiModeBrain.Find(__instance);
            if (__state != null)
            {
                // Escalation/LowEnd: AI multi-mode every other physics frame; player LOAL every frame.
                bool run = true;
#if ORITASY_COMBINED
                if (Oritasy.PerfMode.IsLow
                    && !Plugin.IsPlayerLaunchedMissile(__instance)
                    && (UnityEngine.Time.frameCount & 1) != 0)
                    run = false;
#endif
                if (run)
                    __state.BeforeMissileUpdate();
            }
            YieldProximityFuze.EnsureProxyWired(__instance);
        }

        [HarmonyPostfix]
        private static void Postfix(Missile __instance, MultiModeBrain __state)
        {
            if (__instance == null)
                return;

            float age = 0f;
            try { age = __instance.timeSinceSpawn; }
            catch { }
            if (age <= 1.25f)
                Plugin.BoostShipMissileLaunchVelocity(__instance);

            // Escalation fast path: conventional / no-brain / past boost window → SD only.
            bool needProx = YieldProximityFuze.NeedsYieldProxHot(__instance);
            if (__state == null && age > 1.25f && !needProx)
            {
#if ORITASY_COMBINED
                if (Oritasy.PerfMode.IsLow)
                    return;
#endif
            }

            Plugin.TickLowSpeedSelfDestruct(__instance);
            if (needProx)
                YieldProximityFuze.TickFallback(__instance);
        }
    }

    /// <summary>
    /// Optical/Laser Seek overrides are not covered by MissileSeeker.Seek patches.
    /// They often clear aimpoint / SetTarget(null) without hasVisual — re-apply right before Steering.
    /// </summary>
    [HarmonyPatch(typeof(Missile), "Steering")]
    internal static class Patch_Missile_Steering
    {
        [HarmonyPrefix]
        private static void Prefix(Missile __instance)
        {
            if (__instance == null)
                return;
            MultiModeBrain brain = MultiModeBrain.Find(__instance);
            if (brain != null)
                brain.ReapplyGuidanceAfterSeek();
        }
    }

    [HarmonyPatch(typeof(OpticalSeeker), "Seek")]
    internal static class Patch_OpticalSeeker_Seek
    {
        [HarmonyPrefix]
        private static void Prefix(OpticalSeeker __instance)
        {
            Missile missile = PluginAccess.GetMissile(__instance);
            if (missile == null)
                return;
            Plugin.EnforceIffOnCurrentSeeker(missile);
        }

        [HarmonyPostfix]
        private static void Postfix(OpticalSeeker __instance)
        {
            Missile missile = PluginAccess.GetMissile(__instance);
            if (missile == null)
                return;
            Plugin.EnforceIffOnCurrentSeeker(missile);
            // Guidance restored in Missile.Steering Prefix
        }

        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception)
        {
            return __exception is NullReferenceException ? null : __exception;
        }
    }

    [HarmonyPatch(typeof(LaserSeeker), "Seek")]
    internal static class Patch_LaserSeeker_Seek
    {
        [HarmonyPrefix]
        private static void Prefix(LaserSeeker __instance)
        {
            Missile missile = PluginAccess.GetMissile(__instance);
            if (missile == null)
                return;
            Plugin.EnforceIffOnCurrentSeeker(missile);
        }

        [HarmonyPostfix]
        private static void Postfix(LaserSeeker __instance)
        {
            Missile missile = PluginAccess.GetMissile(__instance);
            if (missile == null)
                return;
            Plugin.EnforceIffOnCurrentSeeker(missile);
            // Guidance restored in Missile.Steering Prefix
        }

        // Game LaserSeeker.SendTargetInfo NREs when designation bus is incomplete.
        // Swallow only NRE so salvo FixedUpdate is not exception-stormed (happy path unchanged).
        [HarmonyFinalizer]
        private static Exception Finalizer(Exception __exception)
        {
            return __exception is NullReferenceException ? null : __exception;
        }
    }

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

            // ACNM-118: nuke only if armed + within 0.5km of intended target; else fizzle SD
            if (AgmTWeapon.IsAcnmNuclearMissile(__instance))
            {
                if (AgmTWeapon.ShouldAllowNuclearDetonation(__instance))
                    Plugin.ArmAcnmNuclearWarhead(__instance);
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
            if (!Plugin.AgmTOwnedByWeXon())
                return;
            if (weapon == null || weaponMount == null || !AgmTWeapon.IsAgmTMount(weaponMount))
                return;
            if (weapon is Gun)
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
        [HarmonyPostfix]
        [HarmonyPatch("SpawnMissile", new Type[] { typeof(MissileDefinition), typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(Unit), typeof(Unit) })]
        private static void PostfixDef(Missile __result)
        {
            if (__result == null)
                return;
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
            Plugin.ConsumePendingNuke(__result, "Spawn(def)");
            Plugin.ApplyNuke(__result, "Spawn(def)");
            AgmTWeapon.OnSpawned(__result);
            Plugin.BoostShipMissileLaunchVelocity(__result);
            Plugin.SetupMultiMode(__result, null, null, __result.transform.position.ToGlobalPosition());
            if (AgmTWeapon.HasBusDispenser(__result))
                MissileCameraBridge.TryNotifySpawn(__result);
#if ORITASY_COMBINED
            Oritasy.KeAmmoCloneService.ConsumeSpawned(__result);
#endif
        }

        [HarmonyPostfix]
        [HarmonyPatch("SpawnMissile", new Type[] { typeof(GameObject), typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(Unit), typeof(Unit) })]
        private static void PostfixGo(Missile __result)
        {
            if (__result == null)
                return;
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
            Plugin.ConsumePendingNuke(__result, "Spawn(go)");
            Plugin.ApplyNuke(__result, "Spawn(go)");
            AgmTWeapon.OnSpawned(__result);
            Plugin.BoostShipMissileLaunchVelocity(__result);
            Plugin.SetupMultiMode(__result, null, null, __result.transform.position.ToGlobalPosition());
            if (AgmTWeapon.HasBusDispenser(__result))
                MissileCameraBridge.TryNotifySpawn(__result);
#if ORITASY_COMBINED
            Oritasy.KeAmmoCloneService.ConsumeSpawned(__result);
#endif
        }
    }

    [HarmonyPatch(typeof(Hardpoint), "SpawnMount")]
    internal static class Patch_Hardpoint_SpawnMount_AgmT
    {
        [HarmonyPostfix]
        private static void Postfix(Hardpoint __instance, Aircraft aircraft, WeaponMount weaponMount, GameObject __result)
        {
            if (!Plugin.AgmTOwnedByWeXon())
                return;
            if (__result == null || weaponMount == null || aircraft == null
                || !AgmTWeapon.IsAgmTMount(weaponMount))
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

    [HarmonyPatch(typeof(WeaponSelector), "Initialize", new Type[] { typeof(Aircraft), typeof(HardpointSet), typeof(FactionHQ), typeof(Airbase) })]
    internal static class Patch_WeaponSelector_Initialize
    {
        [HarmonyPrefix]
        private static void Prefix(Aircraft aircraft)
        {
            if (!Plugin.NukeMountsInjected)
                Plugin.EnsureNukeMountClones();
            // Always Ensure �?repair null prefabs + re-register after AfterLoad
            AgmTWeapon.Ensure();
            AgmTWeapon.RepairBrokenPrefabs();
#if ORITASY_COMBINED
            try
            {
                Oritasy.KeAmmoCloneService.Ensure();
                Oritasy.KeAmmoCloneService.InjectForAircraft(aircraft);
            }
            catch { }
#endif
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
            if (Plugin.EnableUnrestricted.Value && Plugin.IsLocalHumanPlayer(player))
            {
                Plugin.LocalHumanWeaponsQuery = true;
                Plugin.LocalHumanWeaponsUntil = Time.unscaledTime + 5f;
                Plugin.EnterPlayerUnrestricted();
            }
        }

        [HarmonyPostfix]
        private static void Postfix(Player player)
        {
            if (Plugin.EnableUnrestricted.Value && Plugin.IsLocalHumanPlayer(player))
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
            if (Plugin.IsLocalHumanPlayer(player) && AgmTWeapon.IsAgmTMount(requestedMount)
                && !Plugin.IsNavalHardpoint(hardpointSet))
            {
                __result = true;
                failReason = null;
                failCost = 0;
                return false;
            }

            if (!Plugin.EnableUnrestricted.Value || !Plugin.IsLocalHumanPlayer(player))
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
                Plugin.EnableUnrestricted.Value,
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
        /// Only allow that ~AiNukeChance when the faction still has warheads; otherwise keep
        /// conventional so AI pylons are never left empty.
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
                Plugin.RollAiNukePreference(),
                Plugin.OritasyNukesAllowed);

            if (path == PreferNukesGateService.Path.SkipVanillaKeepList)
                return false;

            if (path == PreferNukesGateService.Path.StripNukesKeepConventional)
            {
                Plugin.RemoveNuclearFromList(listToFilter);
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

            if (!Plugin.OritasyNukesAllowed)
                Plugin.RemoveIalNukeClonesFromList(listToFilter);

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
                Plugin.RemoveNuclearFromList(listToFilter);
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
                        // Null/destroyed prefab �?Hardpoint.SpawnMount NRE and empties the whole loadout
                        if (m == null || m.prefab == null || !HaveScratch.Add(m))
                            continue;
                        if (naval && Plugin.IsIalMount(m))
                            continue;
                        outAvailable.Add(m);
                    }
                }
                else if (Plugin.AgmTOwnedByWeXon() && !naval
                    && Plugin.IsLocalHumanPlayer(player))
                {
                    AgmTWeapon.AppendMountsToList(outAvailable, HaveScratch);
                }
#if ORITASY_COMBINED
                Oritasy.KeAmmoCloneService.Ensure();
                Oritasy.KeAmmoCloneService.AppendToAvailable(hardpointSet, outAvailable);
#endif
                if (!Plugin.OritasyNukesAllowed)
                    Plugin.RemoveIalNukeClonesFromList(outAvailable);
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
            if (!Plugin.BlockIalOnShips.Value || __instance == null)
                return true;
            if (!Plugin.IsShipUnit(owner))
                return true;
            if (!Plugin.IsIalWeapon(__instance))
                return true;
            if (Plugin.DebugLog.Value)
                Plugin.Log.LogInfo("Blocked IAL fire from ship: " + __instance.name);
            return false;
        }
    }

    internal static class PluginAccess
    {
        private static readonly FieldInfo SeekerMissileField = AccessTools.Field(typeof(MissileSeeker), "missile");

        internal static Missile GetMissile(MissileSeeker seeker)
        {
            if (seeker == null)
                return null;
            Missile missile = SeekerMissileField != null ? SeekerMissileField.GetValue(seeker) as Missile : null;
            if (missile == null)
                missile = seeker.GetComponentInParent<Missile>();
            return missile;
        }
    }

    /// <summary>
    /// Piledriver TBM free-hunt: larger radius, feed BallisticMissileGuidance knownPos only.
    /// Does not GuideTo / change gLimit (those broke loft steering).
    /// </summary>
    public sealed class TbmHuntAssist : MonoBehaviour
    {
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

        public void Setup(Missile missile)
        {
            _missile = missile;
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

        private void FixedUpdate()
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
            if (!TbmHuntMathService.HuntDue(Time.time, _nextHunt, force))
                return;
            _nextHunt = TbmHuntMathService.ScheduleNextHunt(
                Time.time,
                Plugin.MadModeSearchInterval.Value);

            float radius = TbmHuntMathService.ClampHuntRadius(
                Plugin.TbmHuntRadius != null ? Plugin.TbmHuntRadius.Value : TbmHuntMathService.DefaultRadiusM);

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
        /// <summary>Last commanded aim direction (HOBS slew smoothing).</summary>
        private Vector3 _lastCmdDir;
        private bool _hasLastCmdDir;
        private float _nextFinBoost;
        private int _lastTargetId;
        private int _lastBeforeFrame = -1;
        /// <summary>Frame when GuideTo last wrote aim — skip duplicate Reapply GuideTo same FixedUpdate.</summary>
        private int _aimSyncedFrame = -1;
        /// <summary>Target id that received full seeker warm (bools / IR / ARH); hot path only dirty aim fields.</summary>
        private int _seekerWarmTargetId;
        private readonly List<Unit> _huntBuf = new List<Unit>(128);
        private readonly HashSet<Unit> _huntSeen = new HashSet<Unit>();
        // 259C: hunt score scratch (main copies positions; ThreadPool scores).
        private static float[] s_huntCx = new float[128];
        private static float[] s_huntCy = new float[128];
        private static float[] s_huntCz = new float[128];
        private static float[] s_huntSoft = new float[128];
        private static float[] s_huntScore = new float[128];
        private static Unit[] s_huntUnits = new Unit[128];
        private static int s_huntScratchN;
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

        public bool HasGuidanceOverride
        {
            get { return _guidanceOverride; }
        }

        /// <summary>True while brain is guiding to a live target (blocks seeker SetTarget(null) / LoseLock).</summary>
        public bool HasActiveHuntTarget
        {
            // Player/designated locks stay sticky — do not re-gate every frame with CanEngage
            // (IFF HQ flicker + IR LoseLock was wiping targetID — ARH/IR aim straight ahead).
            get
            {
                bool alive = _target != null && IsAlive(_target);
                return MultiModeHuntGateService.HasActiveHuntTarget(
                    alive,
                    _playerDesignated,
                    _stickyOnly,
                    alive && IsConfirmedFriendly(_target),
                    alive && CanEngage(_target, true));
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
            if (!_playerDesignated && !_stickyOnly)
                _target = null;
            RefreshTargetClaim();
        }

        /// <summary>Fully drop live Unit lock (dead / confirmed friendly). Sticky-only missiles keep coasting last aim.</summary>
        private void DropTarget()
        {
            _guidanceOverride = false;
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
            try
            {
                _missile.SetAimpoint(_lastKnownAim, _lastKnownVel);
                _aimpoint = _lastKnownAim;
                _guidanceOverride = true;
                _aimSyncedFrame = Time.frameCount;
            }
            catch { _guidanceOverride = false; }
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
            {
                BrainsByMissileId.Remove(_missileId);
                YieldProximityFuze.ForgetMissile(_missileId);
            }
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
            _primary = primary;
            _aimpoint = aimpoint;
            _nextHunt = 0f;
            _nextDesignatedCheck = 0f;
            _nextFinBoost = 0f;
            _guidanceOverride = false;
            _playerDesignated = false;
            _stickyOnly = false;
            _hasLastKnownAim = false;
            _aimSyncedFrame = -1;
            _seekerWarmTargetId = 0;
            _deployCalled = false;

            if (_primary == null)
            {
                if (SeekerField != null)
                    _primary = SeekerField.GetValue(missile) as MissileSeeker;
                if (_primary == null)
                    _primary = missile.GetComponent<MissileSeeker>();
            }

            if (SeekerField != null && _primary != null)
                SeekerField.SetValue(missile, _primary);

            if (Plugin.EnableSecondarySeeker.Value)
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

            // 265C: player/designated lock ALWAYS outranks LOAL free-hunt.
            Unit designated = Plugin.ResolveDesignatedTarget(missile);
            if (target != null && Plugin.IsDesignatedEngageable(missile, Plugin.ResolveShooterSide(missile), target))
                _target = target;
            else if (designated != null)
                _target = designated;
            else
                _target = null;
            if (_target != null && !Plugin.IsDesignatedEngageable(missile, Plugin.ResolveShooterSide(missile), _target))
                _target = null;

            bool targetEngage = _target != null;
            bool hadPlayerDesignation = designated != null || targetEngage;
            MultiModeTickGateService.SetupLockAction setup = MultiModeTickGateService.ResolveSetupLock(
                ShipLaunchNeedsCoast(),
                targetEngage || hadPlayerDesignation,
                Plugin.AllowFreeAttack.Value && !hadPlayerDesignation,
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
            if (setup == MultiModeTickGateService.SetupLockAction.StickyGuide)
            {
                if (_target == null)
                    _target = designated;
                if (_target != null)
                {
                    MarkSticky(_target);
                    SyncSeekerTarget(_target);
                    GuideTo(_target);
                    if (Plugin.DebugLog.Value)
                        Plugin.Log.LogInfo("MM player/designated lock: " + _target.name);
                }
            }
            else if (setup == MultiModeTickGateService.SetupLockAction.FreeHuntLoal)
            {
                // Only true no-lock LOAL — never when a designation existed.
                if (!hadPlayerDesignation)
                {
                    _nextHunt = 0f;
                    TryFreeHunt();
                }
                else if (_target != null)
                {
                    MarkSticky(_target);
                    SyncSeekerTarget(_target);
                    GuideTo(_target);
                }
            }
            RefreshTargetClaim();
        }

        /// <summary>Called after vanilla Seek — restores aimpoint Seek just overwrote with forward-coast.</summary>
        public void ReapplyGuidanceAfterSeek()
        {
            MultiModeTickGateService.EarlyOut early = MultiModeTickGateService.ResolveReapplyEarly(
                Plugin.EnableMultiMode.Value,
                _missile != null && (Plugin.IsGunShellMissile(_missile) || Plugin.IsMotorlessProjectile(_missile)),
                Plugin.IsCruiseMissile(_missile));
            if (early == MultiModeTickGateService.EarlyOut.ClearOverride)
            {
                _guidanceOverride = false;
                return;
            }
            if (early == MultiModeTickGateService.EarlyOut.StripIncompatible)
            {
                Plugin.StripIncompatibleBrain(_missile);
                return;
            }

            EnsureFinsAndArm();

            MultiModeTickGateService.ModeBranch branch = MultiModeTickGateService.ResolveModeBranch(
                ShipLaunchNeedsCoast(),
                _playerDesignated || _stickyOnly);

            if (branch == MultiModeTickGateService.ModeBranch.ShipCoast)
            {
                _guidanceOverride = false;
                return;
            }

            if (branch == MultiModeTickGateService.ModeBranch.Sticky)
            {
                MultiModeTickGateService.StickyAction st = MultiModeTickGateService.ResolveStickyReapply(
                    _target != null && Plugin.IsEjectedPilotUnit(_target),
                    _target != null,
                    _target != null && IsAlive(_target),
                    _target != null && IsConfirmedFriendly(_target),
                    MultiModeGuideMathService.AimAlreadySyncedThisFrame(_aimSyncedFrame, Time.frameCount));
                if (st == MultiModeTickGateService.StickyAction.DropCoast)
                {
                    DropTarget();
                    CoastStickyLastKnown();
                    return;
                }
                if (st == MultiModeTickGateService.StickyAction.SyncHotOnly)
                {
                    SyncSeekerTargetHot(_target);
                    return;
                }
                SyncSeekerTarget(_target);
                GuideTo(_target);
                return;
            }

            // Clears ejected pilots always; other IFF clears when EnableIff.
            Plugin.EnforceIffOnCurrentSeeker(_missile);

            if (_target != null && (Plugin.IsEjectedPilotUnit(_target)
                || !IsAlive(_target) || !CanEngage(_target, true)))
                DropTarget();

            MultiModeTickGateService.OpenHuntAction open = MultiModeTickGateService.ResolveOpenReapply(
                _target != null && IsAlive(_target) && CanEngage(_target, true),
                MultiModeGuideMathService.AimAlreadySyncedThisFrame(_aimSyncedFrame, Time.frameCount),
                Plugin.AllowFreeAttack.Value,
                _stickyOnly,
                Time.time >= _nextHunt);
            if (open == MultiModeTickGateService.OpenHuntAction.SyncHotExisting)
            {
                SyncSeekerTargetHot(_target);
                return;
            }
            if (open == MultiModeTickGateService.OpenHuntAction.GuideExisting)
            {
                SyncSeekerTarget(_target);
                GuideTo(_target);
                return;
            }
            if (open == MultiModeTickGateService.OpenHuntAction.TryFreeHunt)
                TryFreeHunt();
            else if (open == MultiModeTickGateService.OpenHuntAction.CoastSticky)
                CoastStickyLastKnown();
            else
                ReleaseGuidanceToVanilla();
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
            return MultiModeTickGateService.ShipLaunchNeedsCoast(
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
                return Plugin.IsHostileHuntTarget(_missile, candidate);
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
            if (_primary == null || target == null || Plugin.IsEjectedPilotUnit(target))
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
                if (opticalFamily)
                {
                    SetSeekerBool(_primary, "hasVisual", true);
                    SetSeekerField(_primary, "lastOpticalCheck", Time.timeSinceLevelLoad);
                }

                // IR: keep IRSource so Seek updates knownPos from the real target
                if (typeof(IRSeeker).IsAssignableFrom(t))
                {
                    try
                    {
                        IRSource ir = target.GetIRSource();
                        if (ir != null)
                            SetSeekerField(_primary, "IRTarget", ir);
                    }
                    catch { }
                }

                // ARH: skip datalink coast; use terminal track toward fed knownPos
                if (typeof(ARHSeeker).IsAssignableFrom(t))
                    SetSeekerBool(_primary, "radarLockEstablished", true);

                if (_secondary != null)
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
            if (!SeekerFieldCache.TryGetValue(key, out f))
            {
                f = AccessTools.Field(seekerType, name);
                SeekerFieldCache[key] = f;
            }
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
            if (id == _lastTargetId)
                return;
            _lastTargetId = id;
            // Player sticky locks: never soft-retarget loft — that looked like "locked but flies away".
            if (_playerDesignated)
            {
                _softRetargetUntil = 0f;
                return;
            }
            float soft = Plugin.SoftRetargetSeconds != null
                ? Plugin.SoftRetargetSeconds.Value
                : MultiModeGuideMathService.SoftRetargetDefaultSec;
            _softRetargetUntil = MultiModeGuideMathService.ScheduleSoftRetargetUntil(
                Time.time, soft, _playerDesignated);
        }

        /// <summary>Max commanded off-boresight (deg) so retargets do not dump all airspeed.</summary>
        private static float MaxOffBoresightDeg(float speedMps, bool softRetarget)
        {
            return MultiModeGuideMathService.MaxOffBoresightDeg(speedMps, softRetarget);
        }

        private void GuideTo(Unit target)
        {
            if (_missile == null || target == null)
                return;
            MultiModeGuideMathService.GuidePath path = MultiModeGuideMathService.ResolveGuidePath(
                Plugin.IsEjectedPilotUnit(target),
                _stickyOnly,
                false);
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
                float dist = Vector3.Distance(mpos, target.transform.position);
                Vector3 leadPos = MultiModeGuideMathService.LeadPosition(
                    target.transform.position, tvel, dist, speed);

                bool energyAware = Plugin.EnergyAwareGuide == null || Plugin.EnergyAwareGuide.Value;
                float minSpd = Plugin.MinGuideSpeedMps != null
                    ? Plugin.MinGuideSpeedMps.Value
                    : MultiModeGuideMathService.DefaultMinGuideSpeedMps;
                bool soft = MultiModeGuideMathService.IsSoftRetargetWindow(
                    _playerDesignated, _stickyOnly, Time.time, _softRetargetUntil);

                float aspect = MultiModeGuideMathService.AspectDeg(fwd, mpos, target.transform.position);
                bool aamHobs = (_playerDesignated || _stickyOnly)
                    && Plugin.IsAirToAirMissile(_missile, _primary);
                MultiModeGuideMathService.HobsPhase hobs =
                    MultiModeGuideMathService.ResolveHobsPhase(aamHobs, aspect, speed, minSpd);

                Vector3 aim;
                float dt = Time.fixedDeltaTime;
                Vector3 lastDir = _hasLastCmdDir ? _lastCmdDir : fwd;
                if (hobs != MultiModeGuideMathService.HobsPhase.None)
                {
                    // Collision-course lead for rear snaps (not naive chase-behind).
                    leadPos = MultiModeGuideMathService.LeadPosition(
                        mpos, fwd, speed, target.transform.position, tvel);
                    aim = MultiModeGuideMathService.ComputeHobsAimPoint(
                        mpos, fwd, speed, leadPos, minSpd, hobs, lastDir, dt);
                    if (MissileGLimitField != null)
                    {
                        float curG = 30f;
                        try { curG = (float)MissileGLimitField.GetValue(_missile); } catch { }
                        float orig = _hasOriginalGLimit ? _originalGLimit : curG;
                        float wantG = MultiModeGuideMathService.CapGLimitForHobs(
                            curG, orig, speed, minSpd, aspect, hobs);
                        try { MissileGLimitField.SetValue(_missile, wantG); } catch { }
                    }
                }
                else
                {
                    bool directChase = _playerDesignated || _stickyOnly || !energyAware;
                    aim = MultiModeGuideMathService.ComputeAimPoint(
                        mpos, fwd, speed, leadPos, directChase, minSpd, soft, lastDir, dt);
                }
                Vector3 cmd = aim - mpos;
                if (cmd.sqrMagnitude > 0.01f)
                {
                    _lastCmdDir = cmd.normalized;
                    _hasLastCmdDir = true;
                }
                GlobalPosition lead = aim.ToGlobalPosition();
                _missile.SetAimpoint(lead, tvel);
                _aimpoint = lead;
                _lastKnownAim = lead;
                _lastKnownVel = tvel;
                _hasLastKnownAim = true;
                _guidanceOverride = true;
                _aimSyncedFrame = Time.frameCount;

                // Sticky hot path: dirty aim fields only after warm; first lock still full warm.
                if (_stickyOnly || _playerDesignated)
                    SyncSeekerTargetHot(target);
                else
                    SyncSeekerTarget(target);
            }
            catch
            {
                try
                {
                    GlobalPosition gp = target.GlobalPosition();
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

            // Player lock always wins over MadMode nearest-hostile.
            Unit des = Plugin.ResolveDesignatedTarget(_missile);
            if (des != null
                && Plugin.IsDesignatedEngageable(_missile, Plugin.ResolveShooterSide(_missile), des))
            {
                MarkSticky(des);
                GuideTo(_target);
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
            _nextHunt = MultiModeGuideMathService.ScheduleNextHunt(
                Time.time, Plugin.MadModeSearchInterval.Value);

            GlobalPosition origin;
            try { origin = _missile.GlobalPosition(); }
            catch { origin = _missile.transform.position.ToGlobalPosition(); }

            float radius = Plugin.MadModeSearchRadius.Value;
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
            bool surfaceOnly = Plugin.IsSurfaceAttackSeeker(_primary);

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
            GuideTo(best);
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
            if (_missile == null)
                return;
            // Always hunt about velocity (LOAL must not yank into stall for rear/side nearest).
            Vector3 fwd = Vector3.forward;
            float spd = 0f;
            try
            {
                if (_missile.rb != null && _missile.rb.velocity.sqrMagnitude > 16f)
                {
                    fwd = _missile.rb.velocity.normalized;
                    spd = _missile.rb.velocity.magnitude;
                }
                else
                    fwd = _missile.transform.forward;
            }
            catch
            {
                try { fwd = _missile.transform.forward; }
                catch { }
            }
            float fov = Plugin.MadModeSearchAngle != null ? Plugin.MadModeSearchAngle.Value : 110f;
            if (fov >= 350f)
                fov = 110f;
            if (fov < 1f)
                fov = 1f;
            float minDot = Mathf.Cos(fov * Mathf.Deg2Rad);
            float maxOff = MultiModeGuideMathService.MaxOffBoresightDeg(spd, true);
            float energyDot = Mathf.Cos(maxOff * Mathf.Deg2Rad);
            if (energyDot > minDot)
                minDot = energyDot;
            int n = 0;
            int cap = s_huntUnits.Length;
            for (int i = 0; i < units.Count; i++)
            {
                Unit u = units[i];
                if (u == null || !_huntSeen.Add(u))
                    continue;
                if (Plugin.IsEjectedPilotUnit(u))
                    continue;
                if (!CanEngage(u, true))
                    continue;
                if (surfaceOnly && !Plugin.IsSurfaceUnit(u))
                    continue;
                if (u is Missile)
                {
                    Missile om = (Missile)u;
                    if (om.owner != null && object.ReferenceEquals(om.owner, _missile.owner))
                        continue;
                }
                if (IsClaimedByStickySibling(u))
                    continue;

                Vector3 p;
                try { p = u.transform.position; }
                catch { continue; }
                if (n >= cap)
                {
                    int nc = cap * 2;
                    System.Array.Resize(ref s_huntCx, nc);
                    System.Array.Resize(ref s_huntCy, nc);
                    System.Array.Resize(ref s_huntCz, nc);
                    System.Array.Resize(ref s_huntSoft, nc);
                    System.Array.Resize(ref s_huntScore, nc);
                    System.Array.Resize(ref s_huntUnits, nc);
                    cap = nc;
                }
                s_huntCx[n] = p.x;
                s_huntCy[n] = p.y;
                s_huntCz[n] = p.z;
                s_huntSoft[n] = IsClaimedBySibling(u) ? HuntScoreMathService.SoftClaimPenaltyM : 0f;
                s_huntUnits[n] = u;
                n++;
            }
            s_huntScratchN = n;
            if (n <= 0)
                return;

            float mx = mpos.x, my = mpos.y, mz = mpos.z;
            float fx = fwd.x, fy = fwd.y, fz = fwd.z;
            float md = minDot, rad = radius;
            Oritasy.MtPureMathBatch.FillScores(n, s_huntScore, (start, end) =>
            {
                for (int i = start; i < end; i++)
                {
                    s_huntScore[i] = HuntScoreMathService.ScoreCandidate(
                        mx, my, mz,
                        s_huntCx[i], s_huntCy[i], s_huntCz[i],
                        fx, fy, fz, md, rad, s_huntSoft[i]);
                }
            });

            if (_missile == null)
                return;
            int bestIdx = Oritasy.MtPureMathBatch.IndexOfMin(s_huntScore, n);
            if (bestIdx < 0)
                return;
            float score = s_huntScore[bestIdx];
            if (float.IsInfinity(score) || score >= bestDist)
                return;
            Unit pick = s_huntUnits[bestIdx];
            if (pick == null)
                return;
            best = pick;
            bestDist = score;
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

        public void BeforeMissileUpdate()
        {
            // No frame-skip here: LOAL re-lock + open-hunt must run every FixedUpdate.
            // Salvo cost is cut in YieldProximityFuze.NeedsYieldProxHot (conventional sticky-skip).

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

            EnsureFinsAndArm();

            if (_primary != null && SeekerField != null)
                SeekerField.SetValue(_missile, _primary);

            MultiModeTickGateService.ModeBranch branch = MultiModeTickGateService.ResolveModeBranch(
                ShipLaunchNeedsCoast(),
                _playerDesignated || _stickyOnly);

            if (branch == MultiModeTickGateService.ModeBranch.ShipCoast)
            {
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
                    GuideTo(_target);
                    ApplyTerminalBoostIfNeeded();
                    return;
                }
                DropTarget();
                CoastStickyLastKnown();
                ApplyTerminalBoostIfNeeded();
                return;
            }

            // 265C: every frame — designation must beat LOAL before TryFreeHunt.
            Unit designatedNow = Plugin.ResolveDesignatedTarget(_missile);
            if (designatedNow != null
                && Plugin.IsDesignatedEngageable(_missile, Plugin.ResolveShooterSide(_missile), designatedNow))
            {
                MarkSticky(designatedNow);
                GuideTo(_target);
                ApplyTerminalBoostIfNeeded();
                return;
            }
            _nextDesignatedCheck = MultiModeTickGateService.ScheduleNextDesignatedCheck(Time.time);

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
                if (Plugin.TerminalGLimit.Value > 0f && MissileGLimitField != null)
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
}
