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
    /// <summary>
    /// Oritasy pack:
    /// - Nuke resist: aircraft (stronger) / buildings; half vehicles/ships
    /// - Aircraft: power buffs, name/code +XE, encyclopedia [Oritasy]
    /// - Ships: power ×1.5, suffix NE, encyclopedia [Thanos]
    /// - Ground vehicles: power ×1.1, suffix TE, encyclopedia [Unitas]
    /// - Buildings: encyclopedia [Bexur]
    /// - Per-airframe F1 Oritasy System GUI; CI-22 extras
    /// </summary>
    [BepInPlugin(PluginInfo.GUID, PluginInfo.PluginName, PluginInfo.Version)]
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
        /// <summary>Combined pack: WeXon + TGM-85 reuse this instance so probes show one Harmony owner.</summary>
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
                "Third-person / overlay UI: chase HUD, FLT REC, fuel flash, F1–F11 tip chips, brand, G/RWR. Help chip stays on. Hotkeys remain; F1 System menu is never disabled.");
            ShowHudBrand = Config.Bind("Presentation", "ShowHudBrand", true,
                "Show 'Oritasy System' on the HUD while flying an XE aircraft.");
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
            ShowAircraftRwr = Config.Bind("Presentation", "AircraftRwr", true,
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
            AiCombatSmarts.Bind(Config);
            DynamicMusic.Bind(Config);
            MissileAudio.Bind(Config);
            AceRadioChatter.Bind(Config);
            GameUnitDisplayService.Bind(Config);
            ShowGMeter = Config.Bind("Presentation", "ShowGMeter", true,
                "Show a right-side vertical G-force meter for the local aircraft.");
            // F5 reserved for private messages — G-meter stays on via ShowGMeter (no hotkey).
            GMeterKey = Config.Bind("Presentation", "GMeterKey", KeyCode.None,
                "Toggle G-force meter overlay (None = disabled; F5 opens private messages).");
            if (GMeterKey.Value == KeyCode.F5)
                GMeterKey.Value = KeyCode.None;
            EnhanceStatusDisplay = Config.Bind("Presentation", "EnhanceStatusDisplay", true,
                "Enlarge the lower-right aircraft damage silhouette, keep it visible, and use high-contrast part colors.");
            RocketCcipHud.Bind(Config);
            AirportIlsHud.Bind(Config);
            AirportDefection.Bind(Config);
            ReverseThrustService.Bind(Config);
            EngineStartService.Bind(Config);
            AirframeWearGui.Bind(Config);
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

            NukeShockResist = Config.Bind("NukeResist", "Enabled", true,
                "Nuke resist: aircraft Full; buildings half (opt); ships 3x (opt); vehicles half.");
            // Baseline ~0.8% / 0.6%; BuildingHalfResist → 2x; NavalTripleResist → /3; vehicles 2x
            NukeShockFactor = Config.Bind("NukeResist", "ShockFactor", 0.008f,
                "Baseline shock multiplier (0.008 = ~0.8%). Buildings/ships/vehicles scale from this.");
            NukeBlastFactor = Config.Bind("NukeResist", "BlastDamageFactor", 0.006f,
                "Baseline blast HP multiplier (0.006 = ~0.6%). Buildings/ships/vehicles scale from this.");
            // Aircraft: +0.1% resist vs previous 0.8%/0.6% → take 0.7% / 0.5%
            NukeAircraftShockFactor = Config.Bind("NukeResist", "AircraftShockFactor", 0.007f,
                "Aircraft shock multiplier (0.007 = ~0.7%).");
            NukeAircraftBlastFactor = Config.Bind("NukeResist", "AircraftBlastDamageFactor", 0.005f,
                "Aircraft blast HP multiplier (0.005 = ~0.5%).");
            // Old 50000 never matched Shockwave TakeDamage (overpressure caps ~25000) — use 0
            NukeBlastThreshold = Config.Bind("NukeResist", "BlastThreshold", 0f,
                "Scale blastDamage at or above this. 0 = all blast HP damage on protected units.");
            BuildingHalfResist = Config.Bind("NukeResist", "BuildingHalfResist", true,
                "Buildings: half nuclear resist (2× damage vs Full baseline).");
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
            // Bind Radar/CCIP before PerfMode so one-shot LowEnd gates can touch their entries.
            RadarMfdOverlay.Bind(Config);
            ChineseFontPatch.Bind(Config);
            GameZhLocalizer.Bind();
            PerfMode.Bind(Config);
            PerfProbeMenu.Bind(Config);
            MotionInterpService.Bind(Config);
            EngineQualityService.Bind(Config);
            OritasyWorker.EnsureStarted();

            // Combined pack: one Harmony id for Oritasy + hosted WeXon (no second owner).
            SharedHarmony = new Harmony(PluginInfo.GUID);
            PatchOwnNamespace(SharedHarmony);
            ChineseFontPatch.EnsureLoaded();
            OritasyCjkAssetPack.EnsureLoaded();
            OritasyCjkAssetPack.PatchHarmony(SharedHarmony);
            ChineseTmpFontService.EnsureReady();
            GameZhLocalizer.PatchHarmony(SharedHarmony);
            HookWeXonMissileCameraBridge();
            // Combined pack: host TGM-85 then WeXon on this GameObject (no second BepInPlugin).
            // Kh85 patches first so WeXon SpawnMissile/Steering postfixes still run after Kh85 OnSpawned.
            TryStartHostedKh85();
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

        /// <summary>Wire WeXon AGM-T cluster handoff → missile PiP (reflection keeps Air-only builds compiling).</summary>
        private static void HookWeXonMissileCameraBridge()
        {
            try
            {
                Type bridge = FindMissileCameraBridgeType();
                if (bridge == null)
                    return;
                FieldInfo notify = bridge.GetField("NotifySpawn", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                FieldInfo handoff = bridge.GetField("HandoffCluster", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (notify != null)
                    notify.SetValue(null, new Action<Missile>(MissileCameraHud.NotifySpawn));
                if (handoff != null)
                    handoff.SetValue(null, new Action<Missile[]>(MissileCameraHud.HandoffCluster));
                Log.LogInfo("Missile camera bridged to " + bridge.FullName + " cluster handoff");
            }
            catch (Exception ex)
            {
                if (DebugLog != null && DebugLog.Value)
                    Log.LogWarning("WeXon camera bridge: " + ex.Message);
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

        /// <summary>Combined Oritasy.dll embeds Kh85MT.Plugin — host without a second [BepInPlugin].</summary>
        private void TryStartHostedKh85()
        {
            try
            {
#if ORITASY_COMBINED
                Kh85MT.Plugin.StartHosted(this, Logger);
#else
                Type t = Assembly.GetExecutingAssembly().GetType("Kh85MT.Plugin");
                if (t == null)
                    return;
                MethodInfo m = t.GetMethod("StartHosted",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (m == null)
                    return;
                m.Invoke(null, new object[] { this, Logger });
#endif
            }
            catch (Exception ex)
            {
                Log.LogWarning("TGM-85 StartHosted: " + DescribeHostedEx(ex));
            }
        }

        /// <summary>WeXon / VeyrnAcm / combined Oritasy.dll — search loaded assemblies.</summary>
        private static Type FindMissileCameraBridgeType()
        {
            string[] names = new string[]
            {
                "WeXon.MissileCameraBridge",
                "VeyrnAcm.MissileCameraBridge"
            };
            Assembly self = Assembly.GetExecutingAssembly();
            for (int n = 0; n < names.Length; n++)
            {
                Type t = self.GetType(names[n]);
                if (t != null)
                    return t;
            }
            try
            {
                Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < asms.Length; i++)
                {
                    Assembly a = asms[i];
                    if (a == null)
                        continue;
                    for (int n = 0; n < names.Length; n++)
                    {
                        Type t = a.GetType(names[n]);
                        if (t != null)
                            return t;
                    }
                }
            }
            catch { }
            return null;
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

        private void Update()
        {
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
            MotionInterpService.Tick();
            EngineQualityService.Tick();
            PerfProbeMenu.TickInput();

            if (UnrestrictedWeapons.Value && Time.unscaledTime >= NextMountRefresh)
            {
                NextMountRefresh = Time.unscaledTime + 90f;
                if (AllMountCache.Count == 0)
                    RefreshMountCache();
            }

            // Critical input + HUD every frame; polish (fonts/music/zh) hitch-gated.
            // Never skip OnGUI overlay draws — that flickered in-game UI.
            if (!PerfProbeService.Sampling)
            {
                MeasureCriticalTicks();
                if (PerfMode.AllowSlot(0, 3))
                    MeasureHudSlot0();
                if (PerfFrameGate.AllowPolish() && PerfMode.AllowSlot(1, 5))
                    MeasurePolishSlot1();
            }
            else
            {
                PerfProbeService.Measure("Oritasy.Critical", MeasureCriticalTicks);
                if (PerfMode.AllowSlot(0, 3))
                    PerfProbeService.Measure("Oritasy.HudSlot0", MeasureHudSlot0);
                if (PerfFrameGate.AllowPolish() && PerfMode.AllowSlot(1, 5))
                    PerfProbeService.Measure("Oritasy.PolishSlot1", MeasurePolishSlot1);
            }
            // Limits stick after Awake/GUI — rare refresh (was 6s; reflection-heavy)
            if (Time.unscaledTime >= NextLimitsApply)
            {
                NextLimitsApply = Time.unscaledTime + 20f;
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

        private static void MeasureCriticalTicks()
        {
            OritasyDevMenu.Tick();
            if (JoinMenuFactionFix.JoinMenuOpen())
                return;
            AircraftManeuverGui.TickInput();
            AircraftManeuverGui.TickDeferredApply();
            OritasyHud.TickAirflowFlag();
            MissileCameraHud.Tick();
            // Menu hotkeys MUST run every frame — Input.GetKeyDown is only true for one
            // frame; staggering F2/F3/F10/F11 made those menus need multiple presses.
            PlayerAutopilot.Tick();
            BeginnerAssist.Tick();
            AerialResupply.Tick();
            ComponentRepair.Tick();
            AirframeWearService.Tick();
            AirframeWearGui.Tick();
            IlsSettingsMenu.Tick();
            PrivateMessageMenu.Tick();
            HostFundMenu.Tick();
            KillChoiceMenu.Tick();
            WarThunderRwrHud.Tick();
            AircraftChaseNoseHud.Tick();
            GMeterHud.Tick();
            RocketCcipHud.Tick();
            AirportDefection.Tick();
            EngineStartService.Tick();
            ReverseThrustService.PollAirbrakeToggle();
            WingmanService.Tick();
            GamepadLookService.Tick();
#if ORITASY_COMBINED
            WeXon.Plugin.HostedTick();
            Kh85MT.Plugin.HostedTick();
#endif
        }

        private static void MeasureHudSlot0()
        {
            OritasyHud.Tick();
            AirportIlsHud.Tick();
            RadarMfdOverlay.Tick();
            AceRadioChatter.Tick();
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
            MissileCameraHud.FixedTick();
            BeginnerAssist.FixedTick();
        }

        private void OnGUI()
        {
            // Join / aircraft-select are uGUI. IMGUI chips steal those clicks.
            if (JoinMenuFactionFix.SelectionUiOpen())
                return;
            ChineseFontPatch.ApplyGuiSkin();
            GamepadLookService.ClearImguiFocusIfIdle();
            UiScaleService.BeginGui();
            try
            {
                DrawOritasyGui();
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
            // Perf probe draws even during splash / LowEnd early-out.
            PerfProbeMenu.Draw();
            OritasyPresentation.Draw();
            EngineStartService.Draw();
            AirframeWearService.DrawHud();
            // First-mission F3 tip draws even while BlocksHud suppresses the rest.
            if (BeginnerAssist.MissionTipActive)
            {
                BeginnerAssist.DrawGui();
                RadarMfdOverlay.Draw();
                AceRadioChatter.Draw();
                return;
            }

            bool menusOpen = AircraftManeuverGui.IsOpen
                || WarThunderRwrHud.LayoutMenuOpen
                || IlsSettingsMenu.MenuOpen
                || PrivateMessageMenu.MenuOpen
                || HostFundMenu.MenuOpen
                || KillChoiceMenu.MenuOpen
                || AirframeWearGui.IsOpen
                || OritasyPresentation.BlocksHud
                || PerfProbeMenu.IsOpen;
            // Skip optional overlay pile on every tier when those HUDs are off.
            // High used to draw every overlay every frame and hitch IMGUI.
            if (!menusOpen && !MissileCameraHud.ManualActive
                && PerfMode.OptionalHudAllQuiet())
            {
                // Keep F2/F3/F4/F5/F6/F7/F9/F10 chips + LAND / repair / AP functional.
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
                if (!MissileCameraHud.ManualActive)
                    GMeterHud.Draw();
                return;
            }

            if (!OritasyPresentation.BlocksHud)
            {
                MissileCameraHud.Draw();
                AircraftChaseNoseHud.Draw();
                RocketCcipHud.Draw();
                AirportIlsHud.Draw();
                WarThunderRwrHud.Draw();
                PlayerAutopilot.DrawGui();
                BeginnerAssist.DrawGui();
                if (!MissileCameraHud.ManualActive)
                    GMeterHud.Draw();
                IlsSettingsMenu.DrawGui();
                PrivateMessageMenu.DrawGui();
                HostFundMenu.DrawGui();
                KillChoiceMenu.DrawGui();
                AerialResupply.DrawGui();
                AirframeWearGui.Draw();
                // Independent HUD is missile-pilot only — never while flying the aircraft.
                OritasyHud.Draw();
                AircraftManeuverGui.Draw();
                AceRadioChatter.Draw();
                AirportDefection.Draw();
            }
            // Radar overlay: do not gate on F1/welcome/changelog BlocksHud — only skip boot splash.
            if (!OritasyPresentation.SplashActive)
                RadarMfdOverlay.Draw();
        }

        private void OnDestroy()
        {
            MissileCameraHud.Shutdown();
            OritasyHud.Shutdown();
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
        public const string PluginName = "RM278";
        /// <summary>BepInEx assembly version — bump every release so stale plugin DLLs lose.</summary>
        public const string Version = "2.9.3";

#if ORITASY_EDITION_D
        /// <summary>Special Edition D — English-only mod UI.</summary>
        public const string ReleaseVersion = "0.0.9.193D";
        public const bool EnglishOnlyEdition = true;
        public const string EditionName = "Special Edition";
        public const string DisplayRelease = "0.0.9.193D Special Edition";
#else
        /// <summary>Standard Edition C — bilingual Profile language toggle.</summary>
        public const string ReleaseVersion = "0.0.9.193C";
        public const bool EnglishOnlyEdition = false;
        public const string EditionName = "Standard";
        public const string DisplayRelease = "0.0.9.193C";
#endif
    }
}
