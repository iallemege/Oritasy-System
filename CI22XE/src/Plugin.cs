using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Oritasy pack:
    /// - Nuke resist: aircraft (stronger); buildings none; half vehicles/ships
    /// - Aircraft: power buffs, name/code +XE, encyclopedia [Oritasy]
    /// - Ships: power ×1.5, suffix NE, encyclopedia [Thanos]
    /// - Ground vehicles: power ×1.1, suffix TE, encyclopedia [Unitas]
    /// - Buildings: encyclopedia [Bexur]
    /// - Per-airframe F1 Oritasy System GUI; CI-22 extras
    /// </summary>
    [BepInPlugin(PluginInfo.GUID, PluginInfo.PluginName, PluginInfo.Version)]
    [BepInDependency("bia.runtime")]
    public partial class Plugin : BaseUnityPlugin
    {
        internal const string PackName = "Oritasy";
        internal const string PackDescLine = "[Oritasy]";
        internal const string ShipSuffix = "NE";
        internal const string ShipBrand = "[Thanos]";
        internal const string VehicleSuffix = "TE";
        internal const string VehicleBrand = "[Unitas]";
        internal const string BuildingBrand = "[Bexur]";
        internal static Plugin Instance;
        internal static ManualLogSource Log;
        /// <summary>Combined pack: hosted WeXon systems reuse this Harmony id.</summary>
        internal static Harmony SharedHarmony;

        internal static ConfigEntry<bool> AffectAllAircraft;
        internal static ConfigEntry<float> PowerMultiplier;
        /// <summary>Airframe joint / G / impact durability (mass unchanged).</summary>
        internal static ConfigEntry<float> AirframeStrengthMul;
        internal static ConfigEntry<int> AirframeRevision;
        /// <summary>One-shot migrate F1 thrust defaults (120 = all aircraft thrust ×2 again).</summary>
        internal static bool PendingFleet114;
        private static float _fleet114CommitAt = -1f;
        internal static ConfigEntry<float> FuelMultiplier;
        internal static ConfigEntry<float> GearStrengthMultiplier;
        internal static ConfigEntry<float> PayloadMultiplier;
        internal static ConfigEntry<bool> UnrestrictedWeapons;
        internal static ConfigEntry<KeyCode> GuiToggleKey;
        internal static ConfigEntry<bool> GuiAutoApply;
        internal static ConfigEntry<bool> ShowHudBrand;
        /// <summary>When resolution exceeds UiScaleRef*, multiply whole IMGUI by UiScaleLargeFactor.</summary>
        internal static ConfigEntry<bool> UiScaleEnabled;
        internal static ConfigEntry<float> UiScaleRefWidth;
        internal static ConfigEntry<float> UiScaleRefHeight;
        internal static ConfigEntry<float> UiScaleLargeFactor;
        /// <summary>
        /// Master switch for third-person / overlay chrome (chase HUD, Help, REC, tip chips…).
        /// F1 Oritasy System menu is never gated by this.
        /// </summary>
        internal static ConfigEntry<bool> ShowThirdPersonUi;
        internal static ConfigEntry<float> HudBrandOffsetY;

        /// <summary>False = hide chase/overlay UI; F1 System remains available.</summary>
        internal static bool AllowThirdPersonUi
        {
            get { return ShowThirdPersonUi == null || ShowThirdPersonUi.Value; }
        }
        internal static ConfigEntry<bool> BootSplash;
        internal static ConfigEntry<float> BootSplashSeconds;
        internal static ConfigEntry<bool> MissileCamera;
        internal static ConfigEntry<KeyCode> MissileCameraKey;
        internal static ConfigEntry<KeyCode> MissileCameraCycleKey;
        internal static ConfigEntry<float> MissileCameraWidth;
        internal static ConfigEntry<bool> ShowAircraftChaseHud;
        internal static ConfigEntry<KeyCode> AircraftChaseHudKey;
        internal static ConfigEntry<bool> ShowStickDisplay;
        internal static ConfigEntry<bool> ShowAircraftRwr;
        internal static ConfigEntry<KeyCode> AircraftRwrKey;
        internal static ConfigEntry<float> AircraftRwrNormX;
        internal static ConfigEntry<float> AircraftRwrNormY;
        internal static ConfigEntry<float> AircraftRwrSize;
        internal static ConfigEntry<bool> ShowGMeter;
        internal static ConfigEntry<KeyCode> GMeterKey;
        /// <summary>Enlarge / recolor the vanilla lower-right aircraft damage silhouette.</summary>
        internal static ConfigEntry<bool> EnhanceStatusDisplay;
        internal static ConfigEntry<bool> ManualMissile;
        internal static ConfigEntry<KeyCode> ManualMissileKey;
        internal static ConfigEntry<float> ManualMissileTurnRate;
        internal static ConfigEntry<float> ManualMissileThrottleRate;
        internal static ConfigEntry<bool> ShowOritasyHud;
        internal static ConfigEntry<KeyCode> OritasyHudKey;
        internal static ConfigEntry<bool> ShowCircularRwr;
        internal static ConfigEntry<bool> EnhancedAirflow;
        internal static ConfigEntry<string> Ci22DisplayName;
        internal static ConfigEntry<string> Ci22Code;
        internal static ConfigEntry<bool> NukeShockResist;
        internal static ConfigEntry<float> NukeShockFactor;
        internal static ConfigEntry<float> NukeBlastFactor;
        internal static ConfigEntry<float> NukeAircraftShockFactor;
        internal static ConfigEntry<float> NukeAircraftBlastFactor;
        internal static ConfigEntry<float> NukeBlastThreshold;
        /// <summary>Buildings take 2× Full damage factors (half nuclear resist).</summary>
        internal static ConfigEntry<bool> BuildingHalfResist;
        /// <summary>Ships take 1/3 Full damage factors (3× nuclear resist).</summary>
        internal static ConfigEntry<bool> NavalTripleResist;
        internal static ConfigEntry<float> ShipPowerMultiplier;
        internal static ConfigEntry<float> VehiclePowerMultiplier;
        /// <summary>AI AAA / SPAAG / CIWS hit quality buff.</summary>
        internal static ConfigEntry<bool> AaaHitEnabled;
        internal static ConfigEntry<float> AaaSpreadMul;
        internal static ConfigEntry<float> AaaRakeMul;
        internal static ConfigEntry<float> AaaTrackMul;
        internal static ConfigEntry<float> AaaLockMul;
        internal static ConfigEntry<bool> Artillery155AaEnabled;
        internal static ConfigEntry<bool> DebugLog;

        internal static bool GuiOpen
        {
            get { return AircraftManeuverGui.IsOpen; }
        }

        internal static readonly Dictionary<string, ManeuverProfile> Profiles = new Dictionary<string, ManeuverProfile>(StringComparer.OrdinalIgnoreCase);

        private static readonly FieldInfo MaxFuelField = AccessTools.Field(typeof(TurbineEngine), "maxFuelConsumption");
        private static readonly FieldInfo NominalPowerField = AccessTools.Field(typeof(ConstantSpeedProp), "nominalPower");
        private static readonly FieldInfo PropFanPowerField = AccessTools.Field(typeof(PropFan), "nominalPower");
        private static readonly FieldInfo RotorPowerField = AccessTools.Field(typeof(RotorShaft), "nominalPower");
        private static readonly FieldInfo DuctedMaxThrustField = AccessTools.Field(typeof(DuctedFan), "maxThrust");
        private static readonly FieldInfo DuctedMaxPowerField = AccessTools.Field(typeof(DuctedFan), "maxPower");
        private static readonly FieldInfo DuctedNominalPowerField = AccessTools.Field(typeof(DuctedFan), "nominalPower");
        private static readonly FieldInfo TurbofanThrustField = AccessTools.Field(typeof(Turbofan), "staticThrust");
        private static readonly FieldInfo TurbojetThrustField = AccessTools.Field(typeof(Turbojet), "maxThrust");
        private static readonly FieldInfo TurbofanFuelField = AccessTools.Field(typeof(Turbofan), "fuelConsumptionMax");
        private static readonly FieldInfo TurbojetFuelField = AccessTools.Field(typeof(Turbojet), "fuelConsumptionMax");
        private static readonly FieldInfo MountDisabledField = AccessTools.Field(typeof(WeaponMount), "disabled");
        private static readonly FieldInfo FuelCapacityField = AccessTools.Field(typeof(Aircraft), "fuelCapacity");
        private static readonly FieldInfo ControlsFilterField = AccessTools.Field(typeof(Aircraft), "controlsFilter");
        private static readonly FieldInfo SpringField = AccessTools.Field(typeof(LandingGear), "springRate");
        private static readonly FieldInfo DampingField = AccessTools.Field(typeof(LandingGear), "dampingRate");
        private static readonly FieldInfo AlignField = AccessTools.Field(typeof(LandingGear), "aligningStrength");
        private static readonly FieldInfo TankCapacityField = AccessTools.Field(typeof(FuelTank), "fuelCapacity");

        private static readonly FieldInfo FlyByWireField = AccessTools.Field(typeof(ControlsFilter), "flyByWire");
        private static readonly Type FlyByWireType = ResolveFlyByWireType();
        private static readonly FieldInfo FbwGLimitField = AccessTools.Field(FlyByWireType, "gLimitPositive");
        private static readonly FieldInfo FbwCornerField = AccessTools.Field(FlyByWireType, "cornerSpeed");
        private static readonly FieldInfo FbwMaxRollField = AccessTools.Field(FlyByWireType, "maxRollSpeed");
        private static readonly FieldInfo FbwPostStallField = AccessTools.Field(FlyByWireType, "postStallManeuverSpeed");
        private static readonly FieldInfo FbwMaxPitchField = AccessTools.Field(FlyByWireType, "maxPitchAngularVel");
        private static readonly FieldInfo FbwMaxRollAngularVelField = AccessTools.Field(FlyByWireType, "maxRollAngularVel");
        private static readonly FieldInfo FbwAlphaLimiterField = AccessTools.Field(FlyByWireType, "alphaLimiter");
        private static readonly FieldInfo FbwTakeoffSpeedField = AccessTools.Field(FlyByWireType, "takeoffSpeed");
        private static readonly FieldInfo CfParamsField = AccessTools.Field(typeof(ControlsFilter), "aircraftParameters");

        private static Type ResolveFlyByWireType()
        {
            Type t = AccessTools.Inner(typeof(ControlsFilter), "FlyByWire");
            if (t == null)
                t = typeof(ControlsFilter).GetNestedType("FlyByWire", BindingFlags.Public | BindingFlags.NonPublic);
            if (t == null)
                t = typeof(ControlsFilter).GetNestedType("FlyByWire", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return t;
        }
        private static readonly FieldInfo PilotAircraftField = AccessTools.Field(typeof(PilotBaseState), "aircraft");
        private static readonly FieldInfo PilotMaxGField = AccessTools.Field(typeof(PilotPlayerState), "maxG");
        private static readonly FieldInfo PilotStrengthField = AccessTools.Field(typeof(PilotPlayerState), "pilotStrength");
        private static readonly FieldInfo ShipThrustField = AccessTools.Field(typeof(ShipPropulsion), "thrust");
        private static readonly FieldInfo ShipSteeringThrustField = AccessTools.Field(typeof(ShipPropulsion), "steeringThrust");
        private static readonly FieldInfo GvTopSpeedOnroadField = AccessTools.Field(typeof(GroundVehicle), "topSpeedOnroad");
        private static readonly FieldInfo GvTopSpeedOffroadField = AccessTools.Field(typeof(GroundVehicle), "topSpeedOffroad");
        private static readonly FieldInfo GvAccelerationField = AccessTools.Field(typeof(GroundVehicle), "acceleration");

        internal static readonly HashSetTouched Touched = new HashSetTouched();
        internal static readonly HashSet<int> Ci22HardpointSetIds = new HashSet<int>();
        internal static readonly List<WeaponMount> AllMountCache = new List<WeaponMount>();
        private static readonly HashSet<WeaponMount> AllMountSet = new HashSet<WeaponMount>();
        /// <summary>Runtime XE aircraft tracked from Awake — avoids FindObjectsOfTypeAll every tick.</summary>
        internal static readonly List<Aircraft> LiveXeAircraft = new List<Aircraft>();
        private static readonly HashSet<int> LiveXeIds = new HashSet<int>();
        /// <summary>Last applied power ratios per runtime aircraft instance (for GUI re-scale).</summary>
        private static readonly Dictionary<int, PowerApplyState> PowerState = new Dictionary<int, PowerApplyState>();
        /// <summary>Vanilla (pre-mul) thrust/power baselines keyed by engine component instance id.</summary>
        private static readonly Dictionary<int, float> ThrustBaseline = new Dictionary<int, float>();
        private static readonly Dictionary<int, float> FuelBurnBaseline = new Dictionary<int, float>();
        internal static Aircraft ActiveLoadoutAircraft;
        internal static Aircraft ActivePlayerAircraft;

        internal sealed class PowerApplyState
        {
            public float Thrust = 1f;
            public float FuelBurn = 1f;
            public float FuelCap = 1f;
        }

        private static void CaptureThrustBaseline(int componentId, float vanillaValue)
        {
            if (componentId == 0 || vanillaValue <= 0f)
                return;
            if (!ThrustBaseline.ContainsKey(componentId))
                ThrustBaseline[componentId] = vanillaValue;
        }

        private static void CaptureFuelBurnBaseline(int componentId, float vanillaValue)
        {
            if (componentId == 0 || vanillaValue <= 0f)
                return;
            if (!FuelBurnBaseline.ContainsKey(componentId))
                FuelBurnBaseline[componentId] = vanillaValue;
        }

        private static void SetScaledField(FieldInfo field, object target, int componentId, float mul)
        {
            if (field == null || target == null)
                return;
            try
            {
                float baseline;
                if (!ThrustBaseline.TryGetValue(componentId, out baseline) || baseline <= 0f)
                {
                    baseline = (float)field.GetValue(target);
                    CaptureThrustBaseline(componentId, baseline);
                }
                field.SetValue(target, baseline * Mathf.Max(0.05f, mul));
            }
            catch { }
        }

        /// <summary>Vanilla Nuclear Option airframes (codes / names / jsonKey fragments).</summary>
        private static readonly string[] VanillaMarkers = new string[]
        {
            "COIN", "CI-22", "CI22",
            "SFB-81", "SFB81",
            "FS-12", "FS12", "FS-20", "FS20",
            "VL-49", "VL49",
            "KR-67", "KR67",
            "EW-25", "EW25",
            "UH-90", "UH90",
            "A-19", "A19",
            "Compass", "Chicane", "Darkreach", "Revoker", "Dynamo",
            "Cricket", "Medusa", "Ifrit", "Vortex", "Atlas", "Tarantula",
            "AB-4", "AB4", "VT-7", "VT7"
        };

        private static float NextMountRefresh;
        private static float NextKeEnsure;
        private static float NextLimitsApply;
        private static float NextEncRefresh;
        private static Encyclopedia CachedEncyclopedia;
        private bool _scanned;

        /// <summary>Depth &gt; 0 while player loadout UI / GetAvailableWeapons(player) is running.</summary>
        private static int PlayerUnrestrictedDepth;

        internal static void EnterPlayerUnrestricted()
        {
            PlayerUnrestrictedDepth++;
        }

        internal static void ExitPlayerUnrestricted()
        {
            if (PlayerUnrestrictedDepth > 0)
                PlayerUnrestrictedDepth--;
        }

        internal static bool AllowPlayerUnrestricted()
        {
            return UnrestrictedWeapons.Value && PlayerUnrestrictedDepth > 0;
        }

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
            try
            {
                if (player.SteamID != 0UL)
                {
                    Player local = null;
                    if (GameManager.GetLocalPlayer(out local) && local != null
                        && object.ReferenceEquals(local, player))
                        return true;
                    if (local == null)
                        return true;
                }
            }
            catch { }
            return false;
        }

        static Plugin()
        {
            ConfigGuidMigrate.CopyLegacy(PluginInfo.GUID, PluginInfo.LegacyGUID);
        }

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            AffectAllAircraft = Config.Bind("FleetXE", "AffectAllAircraft", false,
                "If false (default), only vanilla Nuclear Option aircraft. If true, also mod aircraft.");
            PowerMultiplier = Config.Bind("FleetXE", "PowerMultiplier", 4f,
                "Default thrust multiplier (overrides: SFB=8, AB-4=6, A-19=6.2, EW-25=4.34, UH-90=4, KR-67=3.2)");
            AirframeStrengthMul = Config.Bind("FleetXE", "AirframeStrengthMultiplier", 3f,
                "Airframe durability ×N: joint break force + impact threshold only. Does NOT change mass or G limits.");
            AirframeRevision = Config.Bind("FleetXE", "AirframeRevision", 0,
                "Internal: one-shot migrate saved F1 thrust (120 = all aircraft thrust ×2).");
            // One-shot: double saved global default if still on pre-120 value
            if (AirframeRevision.Value < 120 && PowerMultiplier.Value > 0.05f && PowerMultiplier.Value < 3.99f)
                PowerMultiplier.Value = Mathf.Max(PowerMultiplier.Value * 2f, 4f);
            PendingFleet114 = AirframeRevision.Value < 120;
            if (PendingFleet114)
                _fleet114CommitAt = Time.unscaledTime + 8f;

            FuelMultiplier = Config.Bind("CI22XE", "FuelBurnMultiplier", 0.5f,
                "Fuel burn multiplier for CI-22 / AB-4 / VT-7 (0.5 = half)");
            GearStrengthMultiplier = Config.Bind("CI22XE", "GearStrengthMultiplier", 2f, "CI-22 only: landing gear");
            PayloadMultiplier = Config.Bind("CI22XE", "PayloadMultiplier", 2f, "CI-22 only: fuel capacity");
            UnrestrictedWeapons = Config.Bind("CI22XE", "UnrestrictedWeapons", false,
                "Player loadout only: any weapon on hardpoints for all XE/vanilla airframes. AI unchanged. Off by default; toggle in Career Profile.");

            GuiToggleKey = Config.Bind("Limits", "GuiToggleKey", KeyCode.F1,
                "Toggle maneuver GUI for the aircraft you are currently flying");
            GuiAutoApply = Config.Bind("Limits", "GuiAutoApply", true,
                "If true, slider changes apply immediately. If false, use Apply button.");
            ShowThirdPersonUi = Config.Bind("Presentation", "ShowThirdPersonUi", true,
                "Third-person / overlay UI: chase HUD, FLT REC, fuel flash, F1–F11 tip chips, brand, G/RWR. Title-screen Help chip is unchanged. Hotkeys remain; F1 System menu is never disabled.");
            ShowHudBrand = Config.Bind("Presentation", "ShowHudBrand", true,
                "Show the Oritasy Canvas watermark at the bottom-right while in a match.");
            UiScaleEnabled = Config.Bind("Presentation", "UiScaleEnabled", true,
                "When game resolution exceeds UiScaleRefWidth/Height, scale all Oritasy IMGUI by UiScaleLargeFactor.");
            UiScaleRefWidth = Config.Bind("Presentation", "UiScaleRefWidth", 1920f,
                "Design reference width (your screen). Larger → apply UiScaleLargeFactor.");
            UiScaleRefHeight = Config.Bind("Presentation", "UiScaleRefHeight", 1080f,
                "Design reference height (your screen). Larger → apply UiScaleLargeFactor.");
            UiScaleLargeFactor = Config.Bind("Presentation", "UiScaleLargeFactor", 1.5f,
                "IMGUI scale when resolution is larger than the reference (default 1.5).");
            HudBrandOffsetY = Config.Bind("Presentation", "HudBrandOffsetY", 8f,
                "HUD brand Y offset from top of screen (pixels). Default 8 = original top-center.");
            // Migrate accidental 3000px offset back to original top placement
            if (HudBrandOffsetY.Value >= 1000f)
                HudBrandOffsetY.Value = 8f;
            BootSplash = Config.Bind("Presentation", "BootSplash", true,
                "Black screen with green centered 'Oritasy System' when the game first draws UI.");
            BootSplashSeconds = Config.Bind("Presentation", "BootSplashSeconds", 2f,
                "Boot splash duration in seconds (starts on first visible frame, not plugin load).");
            if (BootSplashSeconds.Value > 2.05f)
                BootSplashSeconds.Value = 2f;
            MissileCamera = Config.Bind("Presentation", "MissileCamera", true,
                "Show a left-side picture-in-picture view from your fired missiles.");
            MissileCameraKey = Config.Bind("Presentation", "MissileCameraKey", KeyCode.Delete,
                "Toggle missile camera overlay on/off.");
            // F3/F12 freed for Beginner Assist — migrate old missile-camera bindings.
            if (MissileCameraKey.Value == KeyCode.F3 || MissileCameraKey.Value == KeyCode.F12)
                MissileCameraKey.Value = KeyCode.Delete;
            // F4 reserved for ILS settings — migrate old cycle key off F4.
            MissileCameraCycleKey = Config.Bind("Presentation", "MissileCameraCycleKey", KeyCode.PageDown,
                "Cycle to the next live player missile (PageDown; F4 is ILS settings).");
            if (MissileCameraCycleKey.Value == KeyCode.F4 || MissileCameraCycleKey.Value == KeyCode.None)
                MissileCameraCycleKey.Value = KeyCode.PageDown;
            MissileCameraWidth = Config.Bind("Presentation", "MissileCameraWidth", 0.28f,
                "Missile camera panel width as a fraction of screen width (0.15–0.45).");
            ShowAircraftChaseHud = Config.Bind("Presentation", "AircraftChaseHud", true,
                "Nose-pointing HUD in aircraft third-person chase/orbit (external) camera.");
            AircraftChaseHudKey = Config.Bind("Presentation", "AircraftChaseHudKey", KeyCode.F8,
                "Toggle aircraft external-view nose HUD (chase/orbit).");
            ShowStickDisplay = Config.Bind("Presentation", "StickDisplay", true,
                "Show the bottom-right stick / throttle panel. Independent of chase HUD.");
            ShowAircraftRwr = Config.Bind("Presentation", "AircraftRwr", false,
                "War Thunder-style circular RWR while flying (radar lock + missile launch warnings).");
            AircraftRwrKey = Config.Bind("Presentation", "AircraftRwrKey", KeyCode.F11,
                "Open RWR layout GUI (position / size).");
            // Migrate old default F10 → F11 so F10 can open aerial resupply.
            if (AircraftRwrKey.Value == KeyCode.F10)
                AircraftRwrKey.Value = KeyCode.F11;
            AircraftRwrNormX = Config.Bind("Presentation", "AircraftRwrNormX", 0.12f,
                "RWR disc center X (0=left, 1=right).");
            AircraftRwrNormY = Config.Bind("Presentation", "AircraftRwrNormY", 0.88f,
                "RWR disc center Y (0=top, 1=bottom).");
            AircraftRwrSize = Config.Bind("Presentation", "AircraftRwrSize", 0.18f,
                "RWR diameter as fraction of min(screen W,H) (0.10–0.35).");
            PlayerAutopilot.Bind(Config);
            BeginnerAssist.Bind(Config);
            AerialResupply.Bind(Config);
            AiCombatBrain.Bind(Config);
            AiCombatNnService.Bind(Config);
            InfantryModeService.Bind(Config);
            AiCombatSmarts.Bind(Config);
            DynamicMusic.Bind(Config);
            MissileAudio.Bind(Config);
            AceRadioChatter.Bind(Config);
            GameUnitDisplayService.Bind(Config);
            AirframeWearService.Bind(Config);
            AirframeWearGui.Bind(Config);
            ShowGMeter = Config.Bind("Presentation", "ShowGMeter", true,
                "Show a right-side vertical G-force meter for the local aircraft.");
            // F5 reserved for private messages — G-meter stays on via ShowGMeter (no hotkey).
            GMeterKey = Config.Bind("Presentation", "GMeterKey", KeyCode.None,
                "Toggle G-force meter overlay (None = disabled; F5 opens private messages).");
            if (GMeterKey.Value == KeyCode.F5)
                GMeterKey.Value = KeyCode.None;
            EnhanceStatusDisplay = Config.Bind("Presentation", "EnhanceStatusDisplay", true,
                "Enlarge the lower-right aircraft damage silhouette, keep it visible, and use high-contrast part colors.");
            AirportDefection.Bind(Config);
            ReverseThrustService.Bind(Config);
            EngineStartService.Bind(Config);
            AerialResupply.Bind(Config);
            WingmanService.Bind(Config);
            IlsSettingsMenu.Bind(Config);
            PrivateMessageMenu.Bind(Config);
            HostFundMenu.Bind(Config);
            ManualMissile = Config.Bind("Presentation", "ManualMissile", true,
                "Allow Insert takeover: fullscreen nose camera + stick/mouse MCLOS guidance.");
            // F6 reserved for kill-choice menu — migrate MITL off F6.
            ManualMissileKey = Config.Bind("Presentation", "ManualMissileKey", KeyCode.Insert,
                "Enter/exit manual missile pilot mode (Insert; F6 is kill-choice).");
            if (ManualMissileKey.Value == KeyCode.F6)
                ManualMissileKey.Value = KeyCode.Insert;
            KillChoiceMenu.Bind(Config);
            ManualMissileTurnRate = Config.Bind("Presentation", "ManualMissileTurnRate", 55f,
                "Manual WASD turn rate in degrees/second.");
            ManualMissileThrottleRate = Config.Bind("Presentation", "ManualMissileThrottleRate", 0.7f,
                "Manual throttle change rate per second (Q/E).");
            ShowOritasyHud = Config.Bind("Presentation", "ShowOritasyHud", true,
                "Missile-pilot HUD (SPD/HDG/ACC/G). Hidden while flying the aircraft; shown only in MITL mode.");
            // F7 reserved for host fund grant; Home is vanilla Eject on many binds — use Backslash.
            OritasyHudKey = Config.Bind("Presentation", "OritasyHudKey", KeyCode.Backslash,
                "Toggle missile-pilot HUD + RWR (only while manually piloting a missile; was F7/Home).");
            if (OritasyHudKey.Value == KeyCode.F7 || OritasyHudKey.Value == KeyCode.Home)
                OritasyHudKey.Value = KeyCode.Backslash;
            ShowCircularRwr = Config.Bind("Presentation", "ShowCircularRwr", true,
                "Circular RWR on the missile-pilot HUD (not shown while flying the aircraft).");
            EnhancedAirflow = Config.Bind("Presentation", "EnhancedAirflow", true,
                "Stronger wing vapor / wider contrail band / easier AoA trigger.");

            Ci22DisplayName = Config.Bind("CI22XE", "DisplayName", "CI-22XE Super Cricket",
                "Legacy CI-22 display override; XE brand table usually wins.");
            Ci22Code = Config.Bind("CI22XE", "Code", "CI-22XE", "CI-22 code override");

            ModularArmorService.Bind(Config);
            EmpPulseService.Bind(Config);
            NukeShockResist = Config.Bind("NukeResist", "Enabled", true,
                "Nuke resist: aircraft Full; buildings none; ships 3x (opt); vehicles half.");
            // Baseline ~0.8% / 0.6%; buildings excluded; NavalTripleResist → /3; vehicles 2x
            NukeShockFactor = Config.Bind("NukeResist", "ShockFactor", 0.008f,
                "Baseline shock multiplier (0.008 = ~0.8%). Ships/vehicles scale from this. Buildings excluded.");
            NukeBlastFactor = Config.Bind("NukeResist", "BlastDamageFactor", 0.006f,
                "Baseline blast HP multiplier (0.006 = ~0.6%). Ships/vehicles scale from this. Buildings excluded.");
            // Aircraft: +0.1% resist vs previous 0.8%/0.6% → take 0.7% / 0.5%
            NukeAircraftShockFactor = Config.Bind("NukeResist", "AircraftShockFactor", 0.007f,
                "Aircraft shock multiplier (0.007 = ~0.7%).");
            NukeAircraftBlastFactor = Config.Bind("NukeResist", "AircraftBlastDamageFactor", 0.005f,
                "Aircraft blast HP multiplier (0.005 = ~0.5%).");
            // Old 50000 never matched Shockwave TakeDamage (overpressure caps ~25000) — use 0
            NukeBlastThreshold = Config.Bind("NukeResist", "BlastThreshold", 0f,
                "Scale blastDamage at or above this. 0 = all blast HP damage on protected units.");
            BuildingHalfResist = Config.Bind("NukeResist", "BuildingHalfResist", true,
                "Unused. Buildings have no nuke resist (vanilla shock/blast + stripped armor).");
            NavalTripleResist = Config.Bind("NukeResist", "NavalTripleResist", true,
                "Ships: triple nuclear resist (1/3 damage vs Full baseline). Off = half-resist.");

            ShipPowerMultiplier = Config.Bind("FleetNE", "ShipPowerMultiplier", 1.5f,
                "Ship propulsion thrust / steering thrust multiplier. Names get NE; encyclopedia [Thanos].");
            VehiclePowerMultiplier = Config.Bind("FleetTE", "VehiclePowerMultiplier", 1.1f,
                "Ground vehicle acceleration / top-speed multiplier. Names get TE; encyclopedia [Unitas].");

            AaaHitEnabled = Config.Bind("FleetTE", "AaaHitEnabled", true,
                "Buff AI AAA / SPAAG / CIWS hit quality (tighter spread, lead correction, faster track).");
            AaaSpreadMul = Config.Bind("FleetTE", "AaaSpreadMultiplier", 0.35f,
                "AAA gun bulletSpread multiplier (lower = tighter).");
            AaaRakeMul = Config.Bind("FleetTE", "AaaRakeMultiplier", 0.2f,
                "AAA AimSolver rakeAmount multiplier (lower = less intentional miss wobble).");
            AaaTrackMul = Config.Bind("FleetTE", "AaaTrackMultiplier", 1.6f,
                "AAA turret traverse/elevation rate multiplier.");
            AaaLockMul = Config.Bind("FleetTE", "AaaLockTimeMultiplier", 0.4f,
                "AAA turret lockTime multiplier (lower = locks faster).");
            Artillery155AaEnabled = Config.Bind("FleetTE", "Artillery155AaEnabled", true,
                "Let 155mm guided artillery engage large aircraft only (bombers / VL-49), at low priority vs ground.");
            RadarWarfare.Bind(Config);

            if (Mathf.Abs(NukeShockFactor.Value - 0.04f) < 0.0005f)
                NukeShockFactor.Value = 0.008f;
            if (Mathf.Abs(NukeBlastFactor.Value - 0.03f) < 0.0005f)
                NukeBlastFactor.Value = 0.006f;
            if (Mathf.Abs(NukeBlastThreshold.Value - 50000f) < 1f)
                NukeBlastThreshold.Value = 0f;
            // Migrate previous shared aircraft values (0.8%/0.6%) → +0.1% resist
            if (Mathf.Abs(NukeAircraftShockFactor.Value - 0.008f) < 0.0005f)
                NukeAircraftShockFactor.Value = 0.007f;
            if (Mathf.Abs(NukeAircraftBlastFactor.Value - 0.006f) < 0.0005f)
                NukeAircraftBlastFactor.Value = 0.005f;

            DebugLog = Config.Bind("General", "DebugLog", false, "Verbose logging");
            ChineseFontPatch.Bind(Config);
            GameZhLocalizer.Bind();
            GitHubUpdateService.Bind(Config);
            PerfMode.Bind(Config);
            PerfProbeMenu.Bind(Config);
            MotionInterpService.Bind(Config);
            OritasyWorker.EnsureStarted();

            // Combined pack: one Harmony id for Oritasy + hosted WeXon (no second owner).
            SharedHarmony = new Harmony(PluginInfo.GUID);
            PatchOwnNamespace(SharedHarmony);
            BlueprinterCompat.TryPatch(SharedHarmony);
            ChineseFontPatch.EnsureLoaded();
            OritasyCjkAssetPack.EnsureLoaded();
            OritasyCjkAssetPack.PatchHarmony(SharedHarmony);
            ChineseTmpFontService.EnsureReady();
            GameZhLocalizer.PatchHarmony(SharedHarmony);
            // Combined pack: host WeXon systems (career / F9 / multi-mode) on this GameObject.
            // TGM-85 / ACM-119 / AAM-2CV are standalone VeyrnWeapons.dll — do not StartHosted Kh85.
            TryStartHostedWeXon();
            OritasyAntiTamper.Touch();
            if (Log != null)
                Log.LogInfo(PluginInfo.PluginName + " harmony " + PluginInfo.GUID);
            // Arm only — timer starts on first OnGUI frame (Awake is too early / invisible).
            if (BootSplash == null || BootSplash.Value)
            {
                float sec = BootSplashSeconds != null ? Mathf.Clamp(BootSplashSeconds.Value, 0.5f, 15f) : 2f;
                OritasyPresentation.ArmSplash(sec);
            }
            if (PluginInfo.EnglishOnlyEdition)
            {
                Log.LogInfo(PluginInfo.PluginName + " " + PluginInfo.DisplayRelease
                    + " (asm " + PluginInfo.Version + ") loaded — English-only Special Edition. "
                    + "F1 = System · Profile = music beta.");
            }
            else
            {
                Log.LogInfo(PluginInfo.PluginName + " " + PluginInfo.DisplayRelease
                    + " (asm " + PluginInfo.Version + ") loaded (aircraft XE / ships NE / vehicles TE). "
                    + "F1 = System · Profile = music beta.");
            }
        }

        /// <summary>
        /// Combined Oritasy.dll embeds WeXon.Plugin — must host it explicitly.
        /// Without this, a second [BepInPlugin] double-patched Harmony and ran two Updates.
        /// </summary>
        private static string DescribeHostedEx(Exception ex)
        {
            if (ex == null)
                return "";
            Exception e = ex.InnerException != null ? ex.InnerException : ex;
            string s = e.GetType().Name + ": " + e.Message;
            if (e.InnerException != null)
                s += " --> " + e.InnerException.GetType().Name + ": " + e.InnerException.Message;
            return s;
        }

        private void TryStartHostedWeXon()
        {
            try
            {
#if ORITASY_COMBINED
                WeXon.Plugin.StartHosted(this, Logger, Config);
#else
                Type t = Assembly.GetExecutingAssembly().GetType("WeXon.Plugin");
                if (t == null)
                    return;
                MethodInfo m = t.GetMethod("StartHosted",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (m == null)
                    return;
                m.Invoke(null, new object[] { this, Logger, Config });
#endif
            }
            catch (Exception ex)
            {
                Log.LogWarning("WeXon StartHosted: " + DescribeHostedEx(ex));
            }
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
                if (!TypeHasHarmonyPatch(t))
                    continue;
                if (!HarmonyTypeReady(t))
                    continue;
                StampHarmonyApply(t);
                try { harmony.CreateClassProcessor(t).Patch(); }
                catch (Exception ex)
                {
                    if (Log != null)
                        Log.LogWarning("Harmony patch " + t.Name + ": " + ex.Message);
                }
            }
        }

        private static void StampHarmonyApply(Type t)
        {
            if (t == null)
                return;
            try
            {
                string dir = Paths.BepInExRootPath;
                if (string.IsNullOrEmpty(dir))
                    return;
                File.WriteAllText(Path.Combine(dir, "OritasyHarmonyLast.txt"), t.FullName ?? t.Name);
            }
            catch { }
        }

        /// <summary>
        /// HarmonyX native-crashes in MonoMod (IsValueTypeImpl) when original is
        /// null, generic jobs Ptr&lt;T&gt;, or a Nullable overload.
        /// </summary>
        private static bool HarmonyTypeReady(Type t)
        {
            try
            {
                MethodInfo prepare = t.GetMethod("Prepare",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                if (prepare != null && prepare.ReturnType == typeof(bool))
                {
                    object pv = prepare.Invoke(null, null);
                    if (pv is bool && !(bool)pv)
                        return false;
                }

                MethodInfo tm = t.GetMethod("TargetMethod",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                if (tm != null)
                {
                    object mb = tm.Invoke(null, null);
                    MethodInfo orig = mb as MethodInfo;
                    if (orig == null)
                    {
                        if (Log != null)
                            Log.LogWarning("Skip Harmony " + t.Name + ": TargetMethod=null");
                        return false;
                    }
                    if (MethodUnsafeToDetour(orig))
                    {
                        if (Log != null)
                            Log.LogWarning("Skip Harmony " + t.Name + ": unsafe "
                                + orig.DeclaringType.Name + "." + orig.Name);
                        return false;
                    }
                }

                object[] attrs = t.GetCustomAttributes(typeof(HarmonyPatch), true);
                Type classDt = null;
                string classMn = null;
                Type[] classArgs = null;
                MethodType classMt = MethodType.Normal;
                if (attrs != null)
                {
                    for (int i = 0; i < attrs.Length; i++)
                    {
                        HarmonyPatch hp = attrs[i] as HarmonyPatch;
                        if (hp == null || hp.info == null)
                            continue;
                        if (hp.info.declaringType != null)
                            classDt = hp.info.declaringType;
                        if (!string.IsNullOrEmpty(hp.info.methodName))
                            classMn = hp.info.methodName;
                        if (hp.info.argumentTypes != null && hp.info.argumentTypes.Length > 0)
                            classArgs = hp.info.argumentTypes;
                        if (hp.info.methodType.HasValue)
                            classMt = hp.info.methodType.Value;
                    }
                }

                if (classDt != null && !string.IsNullOrEmpty(classMn))
                {
                    MethodInfo found = ResolveNamedMethod(classDt, classMn, classArgs, classMt);
                    if (found == null)
                    {
                        if (Log != null)
                            Log.LogWarning("Skip Harmony " + t.Name + ": missing "
                                + classDt.Name + "." + classMn);
                        return false;
                    }
                    if (MethodUnsafeToDetour(found))
                    {
                        if (Log != null)
                            Log.LogWarning("Skip Harmony " + t.Name + ": unsafe "
                                + classDt.Name + "." + classMn);
                        return false;
                    }
                }

                MethodInfo[] methods = t.GetMethods(
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                for (int m = 0; m < methods.Length; m++)
                {
                    object[] mattrs = methods[m].GetCustomAttributes(typeof(HarmonyPatch), true);
                    if (mattrs == null)
                        continue;
                    Type dt = classDt;
                    string name = null;
                    Type[] args = null;
                    MethodType mt = MethodType.Normal;
                    for (int a = 0; a < mattrs.Length; a++)
                    {
                        HarmonyPatch hp = mattrs[a] as HarmonyPatch;
                        if (hp == null || hp.info == null)
                            continue;
                        if (hp.info.declaringType != null)
                            dt = hp.info.declaringType;
                        if (!string.IsNullOrEmpty(hp.info.methodName))
                            name = hp.info.methodName;
                        if (hp.info.argumentTypes != null && hp.info.argumentTypes.Length > 0)
                            args = hp.info.argumentTypes;
                        if (hp.info.methodType.HasValue)
                            mt = hp.info.methodType.Value;
                    }
                    if (dt == null || string.IsNullOrEmpty(name) || args == null || args.Length == 0)
                        continue;
                    MethodInfo foundM = ResolveNamedMethod(dt, name, args, mt);
                    if (foundM == null)
                    {
                        if (Log != null)
                            Log.LogWarning("Skip Harmony " + t.Name + ": missing "
                                + dt.Name + "." + name + " (" + args.Length + " args)");
                        return false;
                    }
                    if (MethodUnsafeToDetour(foundM))
                    {
                        if (Log != null)
                            Log.LogWarning("Skip Harmony " + t.Name + ": unsafe "
                                + dt.Name + "." + name);
                        return false;
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

        private static MethodInfo ResolveNamedMethod(Type dt, string mn, Type[] args, MethodType mt)
        {
            if (dt == null || string.IsNullOrEmpty(mn))
                return null;
            if (mt == MethodType.Getter)
                return AccessTools.PropertyGetter(dt, mn);
            if (mt == MethodType.Setter)
                return AccessTools.PropertySetter(dt, mn);
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            MethodInfo[] methods = dt.GetMethods(flags);
            MethodInfo match = null;
            int named = 0;
            int i;
            for (i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name != mn)
                    continue;
                named++;
                if (args != null && args.Length > 0)
                {
                    if (!ParamsMatch(methods[i], args))
                        continue;
                    return methods[i];
                }
                if (match == null)
                    match = methods[i];
            }
            if (args != null && args.Length > 0)
            {
                methods = dt.GetMethods(BindingFlags.Instance | BindingFlags.Static
                    | BindingFlags.Public | BindingFlags.NonPublic);
                for (i = 0; i < methods.Length; i++)
                {
                    if (methods[i].Name == mn && ParamsMatch(methods[i], args))
                        return methods[i];
                }
                return null;
            }
            if (named > 1)
                return null;
            if (match != null)
                return match;
            methods = dt.GetMethods(BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.Public | BindingFlags.NonPublic);
            named = 0;
            match = null;
            for (i = 0; i < methods.Length; i++)
            {
                if (methods[i].Name != mn)
                    continue;
                named++;
                if (match == null)
                    match = methods[i];
            }
            if (named > 1)
                return null;
            return match;
        }

        private static bool ParamsMatch(MethodInfo mi, Type[] args)
        {
            if (mi == null || args == null)
                return false;
            ParameterInfo[] ps = mi.GetParameters();
            if (ps.Length != args.Length)
                return false;
            int p;
            for (p = 0; p < ps.Length; p++)
            {
                Type want = args[p];
                Type have = ps[p].ParameterType;
                if (have.IsByRef)
                    have = have.GetElementType();
                if (want.IsByRef)
                    want = want.GetElementType();
                if (have != want)
                    return false;
            }
            return true;
        }

        private static bool MethodUnsafeToDetour(MethodInfo mi)
        {
            if (mi == null)
                return true;
            if (MethodLooksLikeUnityJob(mi))
                return true;
            if (IsUnsafeHarmonyType(mi.ReturnType))
                return true;
            ParameterInfo[] ps = mi.GetParameters();
            int i;
            for (i = 0; i < ps.Length; i++)
            {
                if (IsUnsafeHarmonyType(ps[i].ParameterType))
                    return true;
            }
            try
            {
                MethodBody body = mi.GetMethodBody();
                if (body != null)
                {
                    System.Collections.Generic.IList<LocalVariableInfo> locs = body.LocalVariables;
                    if (locs != null)
                    {
                        for (i = 0; i < locs.Count; i++)
                        {
                            if (IsUnsafeHarmonyType(locs[i].LocalType))
                                return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private static bool MethodLooksLikeUnityJob(MethodInfo mi)
        {
            if (mi == null || mi.DeclaringType == null)
                return false;
            string n = mi.Name;
            if (n.IndexOf("Job", StringComparison.Ordinal) >= 0)
                return true;
            Type dt = mi.DeclaringType;
            if (n == "Update" || n == "LateUpdate")
            {
                if (typeof(GroundVehicle).IsAssignableFrom(dt) || typeof(Ship).IsAssignableFrom(dt))
                    return true;
            }
            return false;
        }

        private static bool IsUnsafeHarmonyType(Type t)
        {
            if (t == null)
                return true;
            if (t.IsByRef || t.IsPointer)
                t = t.GetElementType();
            if (t == null)
                return true;
            string n = t.Name;
            if (n == "Ptr`1" || n == "PtrAllocation`1" || n == "NativeArray`1"
                || n == "JobHandle" || n == "UniTask" || n == "UniTask`1")
                return true;
            if (!t.IsGenericType)
                return false;
            Type g = t.GetGenericTypeDefinition();
            if (g == typeof(Nullable<>))
                return true;
            string gn = g.Name;
            return gn == "Ptr`1" || gn == "PtrAllocation`1" || gn == "NativeArray`1"
                || gn == "UniTask`1";
        }

        private static bool TypeHasHarmonyPatch(Type t)
        {
            object[] onType = t.GetCustomAttributes(typeof(HarmonyPatch), false);
            if (onType != null && onType.Length > 0)
                return true;
            MethodInfo[] methods = t.GetMethods(
                BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                if (methods[i].IsDefined(typeof(HarmonyPatch), false))
                    return true;
            }
            return false;
        }

        [SkipLocalsInit]
        private void Update()
        {
            bool infantryOn = InfantryModeService.Active;
            if (!infantryOn && Time.frameCount % 30 == 1)
            {
                IcarusHangarSpreadService.TryPatchIcarus(SharedHarmony);
                BlueprinterCompat.TryPatch(SharedHarmony);
                EmpPulseService.TryPatchBia(SharedHarmony);
            }
            BaasImportService.Tick();
            OritasyWorker.EnsureStarted();
            PerfFrameGate.BeginFrame();
            OritasyWorker.PumpMain(PerfFrameGate.PumpLimit);
            PerfProbeService.TickFrame();

            if (!_scanned)
            {
                _scanned = true;
                ApplyAll();
            }

            PerfMode.Tick();
            GitHubUpdateService.Tick();
            if (!infantryOn)
                MotionInterpService.Tick();
            PerfProbeMenu.TickInput();

            if (UnrestrictedWeapons.Value && Time.unscaledTime >= NextMountRefresh)
            {
                NextMountRefresh = Time.unscaledTime + 90f;
                if (AllMountCache.Count == 0)
                    RefreshMountCache();
            }

            if (!infantryOn && Time.unscaledTime >= NextKeEnsure)
            {
                NextKeEnsure = Time.unscaledTime + 12f;
                KeAmmoCloneService.Ensure();
            }

            // Critical input + HUD every frame; polish (fonts/music/zh) hitch-gated.
            // Never skip OnGUI overlay draws in-mission — that flickered HUD.
            bool mainMenu = OritasyPresentation.MainMenuActive
                || OritasyPresentation.SplashActive;
            if (!PerfProbeService.Sampling)
            {
                MeasureCriticalTicks();
                if (!infantryOn && !mainMenu && PerfMode.AllowSlot(0, 3))
                    MeasureHudSlot0();
                if (!infantryOn && PerfFrameGate.AllowPolish() && PerfMode.AllowSlot(1, 5))
                    MeasurePolishSlot1();
            }
            else
            {
                PerfProbeService.Measure("Oritasy.Critical", MeasureCriticalTicks);
                if (!infantryOn && !mainMenu && PerfMode.AllowSlot(0, 3))
                    PerfProbeService.Measure("Oritasy.HudSlot0", MeasureHudSlot0);
                if (!infantryOn && PerfFrameGate.AllowPolish() && PerfMode.AllowSlot(1, 5))
                    PerfProbeService.Measure("Oritasy.PolishSlot1", MeasurePolishSlot1);
            }
            // Limits stick after Awake/GUI — rare refresh (was 6s; reflection-heavy)
            if (!infantryOn && Time.unscaledTime >= NextLimitsApply)
            {
                NextLimitsApply = Time.unscaledTime + 45f;
                PerfProbeService.Measure("Oritasy.ApplyLimits", ApplyLimitsToAllXeAction);
            }

            if (PendingFleet114 && _fleet114CommitAt > 0f && Time.unscaledTime >= _fleet114CommitAt)
            {
                PendingFleet114 = false;
                _fleet114CommitAt = -1f;
                if (AirframeRevision != null)
                    AirframeRevision.Value = 120;
                if (Log != null)
                    Log.LogInfo("FleetXE AirframeRevision=120 (all aircraft thrust ×2).");
            }
        }

        [SkipLocalsInit]
        private static void MeasureCriticalTicks()
        {
            OritasyDevMenu.Tick();
            if (OritasyPresentation.MainMenuActive)
            {
#if ORITASY_COMBINED
                WeXon.Plugin.HostedTick();
#endif
                return;
            }
            if (JoinMenuFactionFix.JoinMenuOpen())
                return;
            if (InfantryModeService.Active)
            {
                EmpPulseService.Tick();
                if (!InfantryModeService.ExternalHost)
                    InfantryModeService.Tick();
                GamepadLookService.Tick();
                return;
            }

            // --- Every frame: edge-triggered input only ---
            AircraftManeuverGui.TickInput();
            AircraftManeuverGui.TickDeferredApply();
            PlayerAutopilot.Tick();
            BeginnerAssist.Tick();
            MissileCameraHud.Tick();
            HudPackState.ManualActive = MissileCameraHud.ManualActive;
            HudPackState.MitlFollowMissile = MissileCameraHud.FollowMissile;
            IlsSettingsMenu.Tick();
            PrivateMessageMenu.Tick();
            HostFundMenu.Tick();
            KillChoiceMenu.Tick();
            AirframeWearGui.Tick();
            ReverseThrustService.PollAirbrakeToggle();
            JammerQte.Tick();
            GamepadLookService.Tick();

            // GetKeyDown paths must stay every frame.
            AerialResupply.Tick();
            EngineStartService.Tick();
            AirframeWearService.Tick(); // internal 20 Hz sim; hold timers every call

            // --- ~10 Hz: no edge-triggered input ---
            if ((Time.frameCount % 3) == 0)
            {
                ComponentRepair.Tick();
                AirportDefection.Tick();
                EmpPulseService.Tick();
                if (!InfantryModeService.ExternalHost)
                    InfantryModeService.Tick();
                WingmanService.Tick();
                WsoCrewOps.Tick();
            }
#if ORITASY_COMBINED
            // WeXon career / arsenal / analysis: every other frame on LowEnd.
            if (!PerfMode.IsLow || (Time.frameCount & 1) == 0)
                WeXon.Plugin.HostedTick();
#endif
        }

        private static void MeasureHudSlot0()
        {
            if (PerfMode.IsLow)
                return;
            AceRadioChatter.Tick();
            CopilotCrewService.Tick();
        }

        private static void MeasurePolishSlot1()
        {
            DynamicMusic.Tick();
            ChineseFontPatch.Tick();
            OritasyCjkAssetPack.Tick();
            ChineseTmpFontService.Tick();
            GameZhLocalizer.Tick();
        }

        private static void ApplyLimitsToAllXeAction()
        {
            ApplyLimitsToAllXe();
        }

        private void FixedUpdate()
        {
            if (!PerfProbeService.Sampling)
                MeasureFixed();
            else
                PerfProbeService.Measure("Oritasy.FixedUpdate", MeasureFixed);
        }

        private static void MeasureFixed()
        {
            if (InfantryModeService.Active)
            {
                EmpPulseService.FixedTick();
                return;
            }
            BeginnerAssist.FixedTick();
            MissileCameraHud.FixedTick();
            EmpPulseService.FixedTick();
        }

        private void OnGUI()
        {
            ChineseFontPatch.ApplyGuiSkin();
            // Main menu / splash: do not pile in-flight IMGUI on vanilla uGUI.
            if (OritasyPresentation.MainMenuActive || OritasyPresentation.SplashActive)
            {
                DrawMainMenuGui();
                return;
            }
            if (WsoCrewOps.WantsDraw())
            {
                UiScaleService.BeginGui();
                try
                {
                    WsoCrewOps.Draw();
                }
                finally
                {
                    UiScaleService.EndGui();
                }
            }
            // Join / aircraft-select are uGUI. IMGUI chips steal those clicks.
            // Infantry hangar overlay is the exception — it lives on that screen.
            if (JoinMenuFactionFix.SelectionUiOpen())
            {
                UiScaleService.BeginGui();
                try
                {
                    if (!InfantryModeService.ExternalHost)
                    {
                        if (InfantryModeService.Active)
                            InfantryModeService.Draw();
                    }
                }
                finally
                {
                    UiScaleService.EndGui();
                }
                return;
            }
            GamepadLookService.ClearImguiFocusIfIdle();
            UiScaleService.BeginGui();
            try
            {
                DrawOritasyGui();
                OritasyDevMenu.Draw();
            }
            finally
            {
                UiScaleService.EndGui();
            }
#if ORITASY_COMBINED
            try
            {
                if (!InfantryModeService.Active)
                    WeXon.Plugin.DrawHostedGui();
            }
            catch
            {
            }
#endif
        }

        private void DrawMainMenuGui()
        {
            UiScaleService.BeginGui();
            try
            {
                PerfProbeMenu.Draw();
                OritasyPresentation.Draw();
            }
            finally
            {
                UiScaleService.EndGui();
            }
#if ORITASY_COMBINED
            try
            {
                WeXon.Plugin.DrawHostedGui();
            }
            catch
            {
            }
#endif
            UiScaleService.BeginGui();
            try
            {
                OritasyDevMenu.Draw();
            }
            finally
            {
                UiScaleService.EndGui();
            }
        }

        private void DrawOritasyGui()
        {
            EmpPulseService.Draw();
            NukeFlashService.Draw();
            if (InfantryModeService.Active && !InfantryModeService.ExternalHost)
            {
                InfantryModeService.Draw();
                return;
            }
            if (!InfantryModeService.ExternalHost)
                InfantryModeService.Draw();
            if (JammerQte.WantsDraw)
                JammerQte.Draw();
            // Perf probe draws even during splash / LowEnd early-out.
            PerfProbeMenu.Draw();
            OritasyPresentation.Draw();
            EngineStartService.Draw();
            AirframeWearService.DrawHud();
            ModularArmorService.DrawHud();
            // First-mission F3 tip draws even while BlocksHud suppresses the rest.
            if (BeginnerAssist.MissionTipActive)
            {
                BeginnerAssist.DrawGui();
                AceRadioChatter.Draw();
                return;
            }

            // LowEnd baseline: skip quiet IMGUI chrome (F-tips / idle overlays).
            // Menus + MITL camera still draw. Restores a large slice of the 120->30 gap.
            if (PerfMode.IsLow
                && !AircraftManeuverGui.IsOpen
                && !IlsSettingsMenu.MenuOpen
                && !PrivateMessageMenu.MenuOpen
                && !HostFundMenu.MenuOpen
                && !KillChoiceMenu.MenuOpen
                && !AirframeWearGui.IsOpen
                && !OritasyDevMenu.IsOpen
                && !PerfProbeMenu.IsOpen
                && !MissileCameraHud.ManualActive
                && !OritasyPresentation.BlocksHud)
            {
                return;
            }

            bool menusOpen = AircraftManeuverGui.IsOpen
                || IlsSettingsMenu.MenuOpen
                || PrivateMessageMenu.MenuOpen
                || HostFundMenu.MenuOpen
                || KillChoiceMenu.MenuOpen
                || AirframeWearGui.IsOpen
                || OritasyDevMenu.IsOpen
                || OritasyPresentation.BlocksHud
                || PerfProbeMenu.IsOpen;
            if (!menusOpen && PerfMode.OptionalHudAllQuiet())
            {
                PlayerAutopilot.DrawGui();
                BeginnerAssist.DrawGui();
                IlsSettingsMenu.DrawGui();
                PrivateMessageMenu.DrawGui();
                HostFundMenu.DrawGui();
                KillChoiceMenu.DrawGui();
                AerialResupply.DrawGui();
                AirframeWearGui.Draw();
                AceRadioChatter.Draw();
                AirportDefection.Draw();
                return;
            }

            // Core MITL camera / stick overlay
            if (!OritasyPresentation.BlocksHud)
                MissileCameraHud.Draw();
            if (!OritasyPresentation.BlocksHud)
            {
                PlayerAutopilot.DrawGui();
                BeginnerAssist.DrawGui();
                IlsSettingsMenu.DrawGui();
                PrivateMessageMenu.DrawGui();
                HostFundMenu.DrawGui();
                KillChoiceMenu.DrawGui();
                AerialResupply.DrawGui();
                AirframeWearGui.Draw();
                AircraftManeuverGui.Draw();
                AceRadioChatter.Draw();
                AirportDefection.Draw();
            }
        }

        private void OnDestroy()
        {
#if ORITASY_COMBINED
            try { WeXon.PlayerCareer.FlushForQuit(); }
            catch { }
            try { WeXon.FlightAnalysis.Shutdown(); }
            catch { }
#endif
        }
    }

    internal static class PluginInfo
    {
        public const string GUID = "com.iallemege.oritasy";
        public const string LegacyGUID = "com.qiaochen.oritasy";
        /// <summary>In-game brand (F1 / encyclopedia). Not the BepInEx plugin title.</summary>
        public const string Name = "Oritasy";
        /// <summary>BepInEx plugin list / log source name — not the in-game brand.</summary>
#if ORITASY_EDITION_D
        public const string PluginName = "RM278";
#else
        public const string PluginName = "Oritasy";
#endif
        /// <summary>BepInEx assembly version — bump every release so stale plugin DLLs lose.</summary>
        public const string Version = "2.12.16";

#if ORITASY_EDITION_D
        /// <summary>Special Edition D — English-only mod UI.</summary>
        public const string ReleaseVersion = "0.0.9.267D";
        public const bool EnglishOnlyEdition = true;
        public const string EditionName = "Special Edition";
        public const string DisplayRelease = "0.0.9.267D Special Edition";
#else
        /// <summary>Standard Edition 240C — bilingual Profile language toggle.</summary>
        public const string ReleaseVersion = "0.0.9.267C";
        public const bool EnglishOnlyEdition = false;
        public const string EditionName = "Standard";
        public const string DisplayRelease = "0.0.9.267C";
#endif
    }
}
