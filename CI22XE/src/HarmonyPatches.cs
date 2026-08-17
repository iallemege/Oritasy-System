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
    /// Combined Oritasy.dll already hosts WeXon loadout gates (IAL ships, AGM-T, unrestricted).
    /// Skipping Oritasy's duplicate WeaponChecker prefixes avoids a second Harmony invoke
    /// and stops Oritasy ForceAllow from skipping WeXon's IAL-on-ships deny.
    /// </summary>
    internal static class CombinedHarmony
    {
        internal static bool SkipOritasyLoadout
        {
            get
            {
#if ORITASY_COMBINED
                return true;
#else
                return false;
#endif
            }
        }
    }

    [HarmonyPatch(typeof(UnitPart), "TakeShockwave")]
    internal static class Patch_UnitPart_TakeShockwave
    {
        [HarmonyPrefix]
        private static void Prefix(UnitPart __instance, ref float overpressure, ref float blastPower)
        {
            NukeResist.ScaleShock((Component)__instance, ref overpressure, ref blastPower);
        }
    }

    [HarmonyPatch(typeof(AeroPart), "TakeShockwave")]
    internal static class Patch_AeroPart_TakeShockwave
    {
        [HarmonyPrefix]
        private static void Prefix(AeroPart __instance, ref float overpressure, ref float blastPower)
        {
            NukeResist.ScaleShock((Component)__instance, ref overpressure, ref blastPower);
        }
    }

    [HarmonyPatch(typeof(UnitPart), "TakeDamage")]
    internal static class Patch_UnitPart_TakeDamage
    {
        [HarmonyPrefix]
        private static void Prefix(UnitPart __instance, ref float blastDamage)
        {
            NukeResist.ScaleBlastDamage((Component)__instance, ref blastDamage);
        }

        [HarmonyPostfix]
        private static void Postfix(
            UnitPart __instance,
            float pierceDamage,
            float blastDamage,
            float amountAffected,
            float impactDamage)
        {
            AirframeWearService.NotifyCombatHit(
                __instance, pierceDamage, blastDamage, amountAffected, impactDamage);
        }
    }

    [HarmonyPatch(typeof(UnitPart), "ApplyDamage")]
    internal static class Patch_UnitPart_ApplyDamage
    {
        [HarmonyPrefix]
        private static void Prefix(UnitPart __instance, ref float netBlastDamage)
        {
            NukeResist.ScaleBlastDamage((Component)__instance, ref netBlastDamage);
        }
    }

    [HarmonyPatch(typeof(Pilot), "TakeShockwave")]
    internal static class Patch_Pilot_TakeShockwave
    {
        [HarmonyPrefix]
        private static void Prefix(Pilot __instance, ref float blastEffectScale, ref float blastPower)
        {
            if (__instance == null || !Plugin.NukeShockResist.Value)
                return;
            Aircraft ac = null;
            try { ac = __instance.aircraft; }
            catch { ac = __instance.GetComponentInParent<Aircraft>(); }
            if (!NukeResist.IsProtectedAircraft(ac))
                return;
            NukeResist.ScaleShock((Unit)ac, ref blastEffectScale, ref blastPower);
        }
    }

    /// <summary>
    /// Real nuke HP damage: Shockwave.InfluencedObject.HasShockwaveReached → TakeDamage(blast=overpressure).
    /// Overpressure peaks around 25000 — old BlastThreshold 50000 never fired.
    /// </summary>
    [HarmonyPatch]
    internal static class Patch_Shockwave_HasShockwaveReached
    {
        static System.Reflection.MethodBase TargetMethod()
        {
            Type t = AccessTools.Inner(typeof(Shockwave), "InfluencedObject");
            if (t == null)
                return null;
            return AccessTools.Method(t, "HasShockwaveReached");
        }

        [HarmonyPrefix]
        private static void Prefix(object __instance, ref float overpressure, ref float blastYield)
        {
            if (__instance == null || !Plugin.NukeShockResist.Value)
                return;
            if (NukeResist.InfluencedDamageableField == null)
                return;
            IDamageable dmg = null;
            try { dmg = NukeResist.InfluencedDamageableField.GetValue(__instance) as IDamageable; }
            catch { return; }
            Unit u = NukeResist.GetUnitFromDamageable(dmg);
            NukeResist.Tier tier = NukeResist.GetTier(u);
            if (tier == NukeResist.Tier.None)
                return;
            NukeResist.ScaleShockwaveHit(u, ref overpressure, ref blastYield);
            if (Plugin.DebugLog.Value)
                Plugin.Log.LogInfo("Nuke resist (" + tier + ") shockwave: op=" + overpressure + " yield=" + blastYield);
        }
    }

    internal sealed class HashSetTouched
    {
        private readonly HashSet<int> defs = new HashSet<int>();
        private readonly HashSet<int> engines = new HashSet<int>();
        private readonly HashSet<int> props = new HashSet<int>();
        private readonly HashSet<int> propFans = new HashSet<int>();
        private readonly HashSet<int> rotors = new HashSet<int>();
        private readonly HashSet<int> ducted = new HashSet<int>();
        private readonly HashSet<int> turbofans = new HashSet<int>();
        private readonly HashSet<int> turbojets = new HashSet<int>();
        private readonly HashSet<int> gears = new HashSet<int>();
        private readonly HashSet<int> tanks = new HashSet<int>();
        private readonly HashSet<int> aircraft = new HashSet<int>();
        private readonly HashSet<int> shipDefs = new HashSet<int>();
        private readonly HashSet<int> vehicleDefs = new HashSet<int>();
        private readonly HashSet<int> buildingDefs = new HashSet<int>();
        private readonly HashSet<int> shipPropulsions = new HashSet<int>();
        private readonly HashSet<int> groundVehicles = new HashSet<int>();

        public bool AddDef(int id) { return defs.Add(id); }
        public bool AddEngine(int id) { return engines.Add(id); }
        public bool AddProp(int id) { return props.Add(id); }
        public bool AddPropFan(int id) { return propFans.Add(id); }
        public bool AddRotor(int id) { return rotors.Add(id); }
        public bool AddDucted(int id) { return ducted.Add(id); }
        public bool AddTurbofan(int id) { return turbofans.Add(id); }
        public bool AddTurbojet(int id) { return turbojets.Add(id); }
        public bool AddGear(int id) { return gears.Add(id); }
        public bool AddTank(int id) { return tanks.Add(id); }
        public bool AddAircraft(int id) { return aircraft.Add(id); }
        public bool HasAircraft(int id) { return aircraft.Contains(id); }
        public void RemoveAircraft(int id) { aircraft.Remove(id); }
        public void RemoveEngine(int id) { engines.Remove(id); }
        public void RemoveProp(int id) { props.Remove(id); }
        public void RemovePropFan(int id) { propFans.Remove(id); }
        public void RemoveRotor(int id) { rotors.Remove(id); }
        public void RemoveDucted(int id) { ducted.Remove(id); }
        public void RemoveTurbofan(int id) { turbofans.Remove(id); }
        public void RemoveTurbojet(int id) { turbojets.Remove(id); }
        public void RemoveGear(int id) { gears.Remove(id); }
        public void RemoveTank(int id) { tanks.Remove(id); }
        public bool AddShipDef(int id) { return shipDefs.Add(id); }
        public bool AddVehicleDef(int id) { return vehicleDefs.Add(id); }
        public bool AddBuildingDef(int id) { return buildingDefs.Add(id); }
        public bool AddShipPropulsion(int id) { return shipPropulsions.Add(id); }
        public bool AddGroundVehicle(int id) { return groundVehicles.Add(id); }
    }

    [HarmonyPatch(typeof(GraphicsHelper), "ApplyAll")]
    internal static class Patch_GraphicsHelper_ApplyAll
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            EngineQualityService.ApplyNow();
        }
    }

    [HarmonyPatch(typeof(Aircraft), "Awake")]
    internal static class Patch_Aircraft_Awake
    {
        [HarmonyPostfix]
        private static void Postfix(Aircraft __instance)
        {
            if (__instance == null)
                return;
            Plugin.TrySetupAircraft(__instance);
            ReverseThrustService.EnableCapability(__instance);
            MotionInterpService.ApplyUnit(__instance);
        }
    }

    [HarmonyPatch(typeof(Spawner))]
    internal static class Patch_Spawner_MissileCamera
    {
        [HarmonyPostfix]
        [HarmonyPatch("SpawnMissile", new Type[] { typeof(MissileDefinition), typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(Unit), typeof(Unit) })]
        private static void PostfixDef(Missile __result)
        {
            MissileCameraHud.NotifySpawn(__result);
            MotionInterpService.ApplyUnit(__result);
        }

        [HarmonyPostfix]
        [HarmonyPatch("SpawnMissile", new Type[] { typeof(GameObject), typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(Unit), typeof(Unit) })]
        private static void PostfixGo(Missile __result)
        {
            MissileCameraHud.NotifySpawn(__result);
            MotionInterpService.ApplyUnit(__result);
        }
    }

    /// <summary>Stamp MCLOS aimpoint after Seek / WeXon, only while the stick is active.</summary>
    [HarmonyPatch(typeof(Missile), "Steering")]
    [HarmonyPriority(Priority.Last)]
    internal static class Patch_Missile_ManualSteering
    {
        [HarmonyPrefix]
        private static void Prefix(Missile __instance)
        {
            // P0: every missile hits Steering — bail before any work when F6 manual is off.
            if (!MissileCameraHud.ManualActive)
                return;
            MissileCameraHud.ApplyGuidanceForSteering(__instance);
        }
    }

    /// <summary>
    /// While manually piloting, suppress airburst self-destruct from MissedTarget/LosingGround.
    /// Seek still runs (arm / tangible / cruise terrain). Impact Detonate is unchanged.
    /// </summary>
    [HarmonyPatch(typeof(Missile), "MissedTarget")]
    internal static class Patch_Missile_ManualMissed
    {
        [HarmonyPrefix]
        private static bool Prefix(Missile __instance, ref bool __result)
        {
            if (!MissileCameraHud.IsManualPiloting(__instance))
                return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Missile), "LosingGround")]
    internal static class Patch_Missile_ManualLosingGround
    {
        [HarmonyPrefix]
        private static bool Prefix(Missile __instance, ref bool __result)
        {
            if (!MissileCameraHud.IsManualPiloting(__instance))
                return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(VaporEffect), "OnEnable")]
    internal static class Patch_VaporEffect_OnEnable
    {
        [HarmonyPostfix]
        private static void Postfix(VaporEffect __instance)
        {
            OritasyHud.EnhanceVaporEffect(__instance);
        }
    }

    [HarmonyPatch(typeof(VaporEmitter), "Emit")]
    internal static class Patch_VaporEmitter_Emit
    {
        [HarmonyPrefix]
        private static void Prefix(ref float alpha, ref float detail)
        {
            if (!OritasyHud.AirflowSofteningActive)
                return;
            OritasyHud.SoftenVaporEmit(ref alpha, ref detail);
        }
    }

    [HarmonyPatch(typeof(Ship), "Awake")]
    internal static class Patch_Ship_Awake
    {
        [HarmonyPostfix]
        private static void Postfix(Ship __instance)
        {
            if (__instance == null)
                return;
            Plugin.TrySetupShip(__instance);
            MotionInterpService.ApplyUnit(__instance);
        }
    }

    [HarmonyPatch(typeof(GroundVehicle), "Awake")]
    internal static class Patch_GroundVehicle_Awake
    {
        [HarmonyPostfix]
        private static void Postfix(GroundVehicle __instance)
        {
            if (__instance == null)
                return;
            Plugin.TrySetupGroundVehicle(__instance);
            MotionInterpService.ApplyUnit(__instance);
        }
    }

    [HarmonyPatch(typeof(Missile), "StartMissile")]
    internal static class Patch_Missile_StartMissile_Interp
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance)
        {
            MotionInterpService.ApplyUnit(__instance);
        }
    }

    [HarmonyPatch(typeof(ShipPropulsion), "Awake")]
    internal static class Patch_ShipPropulsion_Awake
    {
        [HarmonyPostfix]
        private static void Postfix(ShipPropulsion __instance)
        {
            Plugin.TryBuffShipPropulsion(__instance);
        }
    }

    [HarmonyPatch(typeof(Aircraft), "OnDestroy")]
    internal static class Patch_Aircraft_OnDestroy
    {
        [HarmonyPrefix]
        private static void Prefix(Aircraft __instance)
        {
            if (__instance == null)
                return;
            Plugin.UnregisterLiveXe(__instance);
        }
    }

    [HarmonyPatch(typeof(WeaponManager), "Awake")]
    internal static class Patch_WeaponManager_Awake
    {
        [HarmonyPostfix]
        private static void Postfix(WeaponManager __instance)
        {
            if (__instance == null)
                return;
            Aircraft ac = __instance.GetComponentInParent<Aircraft>();
            if (ac != null && Plugin.IsCoinAircraft(ac))
                Plugin.RegisterHardpointSets(__instance);
        }
    }

    [HarmonyPatch(typeof(WeaponSelector), "Initialize")]
    [HarmonyPatch(new Type[] { typeof(Aircraft), typeof(HardpointSet), typeof(FactionHQ), typeof(Airbase) })]
    internal static class Patch_WeaponSelector_Initialize
    {
        [HarmonyPrefix]
        private static void Prefix(Aircraft aircraft)
        {
            Plugin.ActiveLoadoutAircraft = aircraft;
            Plugin.EnterPlayerUnrestricted();
            if (aircraft != null && Plugin.IsCoinAircraft(aircraft) && aircraft.weaponManager != null)
                Plugin.RegisterHardpointSets(aircraft.weaponManager);
        }

        [HarmonyPostfix]
        private static void Postfix()
        {
            Plugin.ExitPlayerUnrestricted();
        }
    }

    [HarmonyPatch(typeof(PilotPlayerState), "EnterState")]
    internal static class Patch_PilotPlayer_Enter
    {
        [HarmonyPostfix]
        private static void Postfix(PilotPlayerState __instance, Pilot pilot)
        {
            Plugin.ApplyPilotLimits(__instance);
            Aircraft ac = null;
            if (AccessTools.Field(typeof(PilotBaseState), "aircraft") != null)
            {
                try { ac = AccessTools.Field(typeof(PilotBaseState), "aircraft").GetValue(__instance) as Aircraft; }
                catch { }
            }
            if (ac != null)
                Plugin.ApplyLimits(ac);
        }
    }

    [HarmonyPatch(typeof(PilotPlayerState), "UpdateState")]
    internal static class Patch_PilotPlayer_Update
    {
        private static float _next;

        [HarmonyPostfix]
        private static void Postfix(PilotPlayerState __instance, Pilot pilot)
        {
            if (Time.unscaledTime < _next)
                return;
            _next = Time.unscaledTime + 1f;
            Plugin.ApplyPilotLimits(__instance);
        }
    }

    /// <summary>
    /// Vanilla only gates PlayerControls with flightControlsEnabled — stick axes still move the jet.
    /// While F6 manual missile view or autopilot is active, freeze aircraft pitch/roll/yaw.
    /// </summary>
    [HarmonyPatch(typeof(PilotPlayerState), "PlayerAxisControls")]
    internal static class Patch_PilotPlayer_AxisWhileMissileCam
    {
        private static readonly FieldInfo AircraftField =
            AccessTools.Field(typeof(PilotBaseState), "aircraft");

        [HarmonyPrefix]
        private static bool Prefix(PilotPlayerState __instance)
        {
            if (__instance == null)
                return true;
            if (!MissileCameraHud.ManualActive && !PlayerAutopilot.BlocksPlayerControls)
                return true;

            try
            {
                __instance.pitchInput = 0f;
                __instance.rollInput = 0f;
                __instance.yawInput = 0f;

                // AP / resupply write stick via AutoAim after FixedUpdateState — do not zero inputs.
                if (PlayerAutopilot.BlocksPlayerControls && !MissileCameraHud.ManualActive)
                    return false;

                Aircraft ac = AircraftField != null
                    ? AircraftField.GetValue(__instance) as Aircraft : null;
                if (ac != null)
                {
                    ControlInputs ci = ac.GetInputs();
                    if (ci != null)
                    {
                        ci.pitch = 0f;
                        ci.roll = 0f;
                        ci.yaw = 0f;
                        ci.brake = 0f;
                        ci.customAxis1 = 0f;
                        // Leave throttle frozen at last value so the jet does not cut power.
                    }
                }
            }
            catch { }

            return false;
        }
    }

    [HarmonyPatch(typeof(PilotPlayerState), "PlayerControls")]
    internal static class Patch_PilotPlayer_ControlsWhileMissileCam
    {
        private static readonly FieldInfo PilotField =
            AccessTools.Field(typeof(PilotBaseState), "pilot");
        private static FieldInfo _playerInputField;
        private static MethodInfo _rewiredGetButtonDown;
        private static readonly object[] RewiredButtonArgs = new object[1];

        [HarmonyPrefix]
        private static bool Prefix(PilotPlayerState __instance)
        {
            // Block weapons / gear / CM / throttle while missile-pilot, AP, or aerial resupply.
            // Still allow Eject — otherwise AP/resupply/MITL soft-locks bail-out.
            if (MissileCameraHud.ManualActive || PlayerAutopilot.BlocksPlayerControls)
            {
                TryEjectWhileControlsBlocked(__instance);
                return false;
            }
            return true;
        }

        private static void TryEjectWhileControlsBlocked(PilotPlayerState state)
        {
            if (state == null || !GameManager.flightControlsEnabled)
                return;
            if (!RewiredButtonDown("Eject"))
                return;
            try
            {
                Pilot pilot = PilotField != null ? PilotField.GetValue(state) as Pilot : null;
                if (pilot == null || pilot.aircraft == null)
                    return;
                Aircraft ac = pilot.aircraft;
                // Drop AP so ejection is not fighting stick locks.
                if (PlayerAutopilot.BlocksPlayerControls)
                    PlayerAutopilot.DisengageFromOutside(false);
                ac.StartEjectionSequence();
                if (ac.IsLanded() && pilot.parkedState != null)
                    pilot.SwitchState(pilot.parkedState);
            }
            catch { }
        }

        private static bool RewiredButtonDown(string action)
        {
            try
            {
                if (_playerInputField == null)
                    _playerInputField = typeof(GameManager).GetField("playerInput",
                        BindingFlags.Public | BindingFlags.Static);
                object pi = _playerInputField != null ? _playerInputField.GetValue(null) : null;
                if (pi == null)
                    return false;
                if (_rewiredGetButtonDown == null)
                    _rewiredGetButtonDown = pi.GetType().GetMethod("GetButtonDown",
                        new Type[] { typeof(string) });
                if (_rewiredGetButtonDown == null)
                    return false;
                RewiredButtonArgs[0] = action;
                object v = _rewiredGetButtonDown.Invoke(pi, RewiredButtonArgs);
                return v is bool && (bool)v;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Replace player stick path with AutoAim (same timing as AI FixedUpdateState).
    /// Skipping the extra FilterInputs-on-stale-stick pass stops left/right rocking.
    /// </summary>
    [HarmonyPatch(typeof(PilotPlayerState), "FixedUpdateState")]
    internal static class Patch_PilotPlayer_AutopilotAfterAxes
    {
        private static readonly FieldInfo AircraftField =
            AccessTools.Field(typeof(PilotBaseState), "aircraft");
        private static readonly FieldInfo PilotStrengthField =
            AccessTools.Field(typeof(PilotPlayerState), "pilotStrength");
        private static readonly FieldInfo GlocField =
            AccessTools.Field(typeof(PilotPlayerState), "gloc");

        [HarmonyPrefix]
        private static bool Prefix(PilotPlayerState __instance, Pilot pilot)
        {
            if (__instance == null || pilot == null)
                return true;
            if (!PlayerAutopilot.IsEngaged && !AerialResupply.IsActive && !BeginnerAssist.BlocksControls)
                return true;
            if (MissileCameraHud.ManualActive)
                return true;

            try
            {
                GLOC gloc = GlocField != null ? GlocField.GetValue(__instance) as GLOC : null;
                if (gloc != null && PilotStrengthField != null)
                    PilotStrengthField.SetValue(__instance, gloc.SimulateGLOC(pilot.gForce));

                Aircraft ac = AircraftField != null
                    ? AircraftField.GetValue(__instance) as Aircraft : null;
                if (ac == null)
                    ac = pilot.aircraft;
                PlayerAutopilot.ApplyAfterPlayer(ac);
            }
            catch { }

            return false; // skip PlayerAxisControls + extra FilterInputs
        }
    }

    [HarmonyPatch(typeof(WeaponChecker), "MountAllowedHardpoint")]
    internal static class Patch_MountAllowedHardpoint
    {
        static bool Prepare()
        {
            return !CombinedHarmony.SkipOritasyLoadout;
        }

        [HarmonyPrefix]
        private static bool Prefix(HardpointSet hardpointSet, ref bool __result)
        {
            if (!Plugin.AllowPlayerUnrestricted())
                return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(WeaponChecker), "MountAllowedHQ")]
    internal static class Patch_MountAllowedHQ
    {
        static bool Prepare()
        {
            return !CombinedHarmony.SkipOritasyLoadout;
        }

        [HarmonyPrefix]
        private static bool Prefix(ref bool __result)
        {
            if (!Plugin.AllowPlayerUnrestricted())
                return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(WeaponChecker), "MountAllowedAirbase")]
    internal static class Patch_MountAllowedAirbase
    {
        static bool Prepare()
        {
            return !CombinedHarmony.SkipOritasyLoadout;
        }

        [HarmonyPrefix]
        private static bool Prefix(ref bool __result)
        {
            if (!Plugin.AllowPlayerUnrestricted())
                return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(WeaponChecker), "MountAllowedConflict")]
    internal static class Patch_MountAllowedConflict
    {
        static bool Prepare()
        {
            return !CombinedHarmony.SkipOritasyLoadout;
        }

        [HarmonyPrefix]
        private static bool Prefix(HardpointSet hardpointSet, ref bool __result)
        {
            if (!Plugin.AllowPlayerUnrestricted())
                return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(WeaponChecker), "MountAllowedNuclear")]
    internal static class Patch_MountAllowedNuclear
    {
        static bool Prepare()
        {
            return !CombinedHarmony.SkipOritasyLoadout;
        }

        [HarmonyPrefix]
        private static bool Prefix(HardpointSet hardpointSet, Player player, ref bool __result)
        {
            if (player == null || !Plugin.AllowPlayerUnrestricted())
                return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(WeaponChecker), "MountAllowedCost")]
    internal static class Patch_MountAllowedCost
    {
        static bool Prepare()
        {
            return !CombinedHarmony.SkipOritasyLoadout;
        }

        [HarmonyPrefix]
        private static bool Prefix(HardpointSet hardpointSet, ref bool __result)
        {
            if (!Plugin.AllowPlayerUnrestricted())
                return true;
            __result = true;
            return false;
        }
    }

    [HarmonyPatch(typeof(WeaponChecker), "GetAvailableWeaponsNonAlloc")]
    internal static class Patch_GetAvailableWeapons
    {
        static bool Prepare()
        {
            return !CombinedHarmony.SkipOritasyLoadout;
        }

        // Reused across hangar hardpoint scans — avoids allocating a HashSet per pylon.
        private static readonly HashSet<WeaponMount> HaveScratch = new HashSet<WeaponMount>();

        [HarmonyPrefix]
        private static void Prefix(Player player)
        {
            if (Plugin.IsLocalHumanPlayer(player))
                Plugin.EnterPlayerUnrestricted();
        }

        [HarmonyPostfix]
        private static void Postfix(Player player, HardpointSet hardpointSet, List<WeaponMount> outAvailable)
        {
            try
            {
                if (!Plugin.AllowPlayerUnrestricted() || outAvailable == null)
                    return;
                if (Plugin.AllMountCache.Count == 0)
                    Plugin.RefreshMountCache();
                HaveScratch.Clear();
                for (int i = 0; i < outAvailable.Count; i++)
                {
                    WeaponMount existing = outAvailable[i];
                    if (existing != null)
                        HaveScratch.Add(existing);
                }
                for (int i = 0; i < Plugin.AllMountCache.Count; i++)
                {
                    WeaponMount m = Plugin.AllMountCache[i];
                    if (m != null && m.prefab != null && HaveScratch.Add(m))
                        outAvailable.Add(m);
                }
            }
            finally
            {
                if (Plugin.IsLocalHumanPlayer(player))
                    Plugin.ExitPlayerUnrestricted();
            }
        }
    }

    [HarmonyPatch(typeof(Encyclopedia))]
    internal static class Patch_Encyclopedia_AfterLoad
    {
        [HarmonyPostfix]
        [HarmonyPatch("AfterLoad", new Type[] { typeof(Encyclopedia) })]
        private static void PostfixStatic(Encyclopedia instance)
        {
            Plugin.ApplyAll();
        }

        [HarmonyPostfix]
        [HarmonyPatch("AfterLoad", new Type[] { })]
        private static void PostfixInstance()
        {
            Plugin.ApplyAll();
        }
    }

    [HarmonyPatch(typeof(EncyclopediaBrowser), "SelectAircraft")]
    internal static class Patch_EncyclopediaBrowser_SelectAircraft
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            if (Plugin.AllowEncyclopediaRefresh())
                Plugin.RefreshEncyclopediaAircraft();
        }
    }

    /// <summary>Ensure XE name/code are on the definition right before the title TMP is set.</summary>
    [HarmonyPatch(typeof(EncyclopediaBrowser), "SpawnUnit")]
    internal static class Patch_EncyclopediaBrowser_SpawnUnit
    {
        [HarmonyPrefix]
        private static void Prefix(UnitDefinition definition)
        {
            AircraftDefinition ad = definition as AircraftDefinition;
            if (ad == null)
                return;
            UnitBrandingService.TryApplyBrand(ad);
        }
    }

    [HarmonyPatch(typeof(EncyclopediaBrowser), "SelectShips")]
    internal static class Patch_EncyclopediaBrowser_SelectShips
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            if (Plugin.AllowEncyclopediaRefresh())
                Plugin.RefreshEncyclopediaShips();
        }
    }

    [HarmonyPatch(typeof(EncyclopediaBrowser), "SelectVehicles")]
    internal static class Patch_EncyclopediaBrowser_SelectVehicles
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            if (Plugin.AllowEncyclopediaRefresh())
                Plugin.RefreshEncyclopediaVehicles();
        }
    }

    /// <summary>
    /// Second FilterInputs in the same physics tick re-filters already-smoothed stick
    /// and walks the jet left/right while holding pitch. Skip it for the local player.
    /// </summary>
    [HarmonyPatch(typeof(Aircraft), "FilterInputs")]
    internal static class Patch_Aircraft_FilterInputs_PitchOnly
    {
        [HarmonyPrefix]
        private static bool Prefix(Aircraft __instance)
        {
            return PitchStickFilterService.AllowFilter(__instance);
        }

        [HarmonyPostfix]
        private static void Postfix(Aircraft __instance)
        {
            PitchStickFilterService.AfterFilter(__instance);
        }
    }

    [HarmonyPatch(typeof(EncyclopediaBrowser), "SelectBuildings")]
    internal static class Patch_EncyclopediaBrowser_SelectBuildings
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            if (Plugin.AllowEncyclopediaRefresh())
                Plugin.RefreshEncyclopediaBuildings();
        }
    }
}
