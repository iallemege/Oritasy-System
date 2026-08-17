using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// F3 auto-takeoff drives vanilla AIPilotTaxiState (taxi to runway) then
    /// AIPilotTakeoffState / AIHeloTakeoffState. Stays in PilotPlayerState so
    /// GLOC / eject / camera keep working (same pattern as F2 LAND CV).
    /// </summary>
    internal static class PlayerVanillaTakeoff
    {
        private enum DriveKind
        {
            None = 0,
            Taxi = 1,
            Takeoff = 2,
            Helo = 3
        }

        private static readonly FieldInfo TaxiDisembarking =
            AccessTools.Field(typeof(AIPilotTaxiState), "disembarking");
        private static readonly FieldInfo TaxiAirbase =
            AccessTools.Field(typeof(AIPilotTaxiState), "airbase");
        private static readonly FieldInfo TaxiWaitingClearance =
            AccessTools.Field(typeof(AIPilotTaxiState), "waitingForTakeoffClearance");
        private static readonly FieldInfo TaxiWaitingCrossing =
            AccessTools.Field(typeof(AIPilotTaxiState), "waitingAtRunwayCrossing");
        private static readonly FieldInfo TakeoffTakenOff =
            AccessTools.Field(typeof(AIPilotTakeoffState), "takenOff");
        private static readonly FieldInfo TakeoffAirbase =
            AccessTools.Field(typeof(AIPilotTakeoffState), "airbase");

        private static bool _active;
        private static bool _entered;
        private static bool _transitioning;
        private static DriveKind _kind;
        private static Pilot _pilot;
        private static AIPilotTaxiState _taxi;
        private static AIPilotTakeoffState _takeoff;
        private static AIHeloTakeoffState _helo;
        private static float _phaseAt;

        internal static bool IsActive
        {
            get { return _active && _entered; }
        }

        internal static void Start(Aircraft ac)
        {
            if (ac == null || ac.autopilot == null)
            {
                BeginnerAssist.Flash(UiLang.T("Need aircraft", "需要飞机"));
                return;
            }
            if (AerialResupply.IsActive || AerialResupply.MenuOpen)
                AerialResupply.AbortActiveFromOutside();
            if (PlayerAutopilot.IsEngaged)
                PlayerAutopilot.DisengageFromOutside(false);
            PlayerCarrierVanillaLand.Stop();

            AircraftParameters parms = null;
            try { parms = ac.GetAircraftParameters(); }
            catch { }
            float takeoffSpd = parms != null ? parms.takeoffSpeed : 70f;
            float ralt = 0f;
            try { ralt = ac.radarAlt; }
            catch { }
            if (ralt > 80f && ac.speed > takeoffSpd * 0.5f)
            {
                BeginnerAssist.Flash(UiLang.T("Already airborne", "已在空中"));
                return;
            }
            if (EngineStartService.IsOn()
                && (EngineStartService.IsStarting || !ac.Ignition))
            {
                BeginnerAssist.Flash(EngineStartService.ApuActive
                    ? UiLang.T(
                        "Please start the engine, thrust is from the APU",
                        "请启动引擎，现在推力由APU提供")
                    : UiLang.T("Please start the APU", "请启动APU"));
                return;
            }
            if (EngineStartService.HoldsMovement)
            {
                BeginnerAssist.Flash(UiLang.T("Advance the throttle to move", "请推节流阀"));
                return;
            }

            Pilot pilot = PilotOf(ac);
            if (pilot == null)
            {
                BeginnerAssist.Flash(UiLang.T("Need aircraft", "需要飞机"));
                return;
            }

            try
            {
                if (!ac.flightAssist)
                    ac.SetFlightAssist(true);
            }
            catch { }
            try { ac.SetGear(true); }
            catch { }

            Stop();
            _active = true;
            _pilot = pilot;
            _phaseAt = Time.unscaledTime;

            if (UseHeloTakeoff(ac, pilot))
            {
                if (!EnterHelo(pilot))
                {
                    Stop();
                    BeginnerAssist.Flash(UiLang.T("Takeoff failed", "起飞失败"));
                    return;
                }
                BeginnerAssist._takeoff = BeginnerAssist.TakeoffPhase.Takeoff;
                BeginnerAssist._takeoffDetail = "HELO";
                BeginnerAssist.Flash(UiLang.T("AUTO TAKEOFF  (AI climb)", "自动起飞（原版垂直起飞）"));
            }
            else
            {
                if (!EnterTaxi(pilot))
                {
                    Stop();
                    BeginnerAssist.Flash(UiLang.T("Takeoff failed", "起飞失败"));
                    return;
                }
                BeginnerAssist._takeoff = BeginnerAssist.TakeoffPhase.Taxi;
                BeginnerAssist._takeoffDetail = "TAXI";
                BeginnerAssist.Flash(UiLang.T("AUTO TAKEOFF  (taxi to runway)", "自动起飞（滑行到跑道）"));
            }
            BeginnerAssist.CloseMenuFromOutside();
        }

        internal static void Apply(Aircraft ac)
        {
            if (!_active || !_entered)
                return;
            if (ac == null || _pilot == null || _pilot.dead || _pilot.ejected)
            {
                Stop();
                return;
            }

            PilotBaseState driven = CurrentDriven();
            if (driven == null)
            {
                Stop();
                return;
            }

            try { driven.FixedUpdateState(_pilot); }
            catch (Exception ex)
            {
                if (Plugin.DebugLog != null && Plugin.DebugLog.Value && Plugin.Log != null)
                    Plugin.Log.LogWarning("Vanilla takeoff: " + ex.Message);
            }

            RefreshDetail(ac);
            TryFinishIfAirborne(ac);
        }

        internal static void Stop()
        {
            LeaveDriven();
            _active = false;
            _entered = false;
            _transitioning = false;
            _kind = DriveKind.None;
            _pilot = null;
            _taxi = null;
            _takeoff = null;
            _helo = null;
            _phaseAt = 0f;
            BeginnerAssist._takeoff = BeginnerAssist.TakeoffPhase.Idle;
            BeginnerAssist._takeoffBase = null;
            BeginnerAssist._takeoffDetail = "";
        }

        internal static bool HandleSwitch(Pilot pilot, PilotBaseState next)
        {
            if (!_active || _pilot == null || pilot != _pilot)
                return false;
            if (_transitioning)
                return true;
            if (next == null)
                return true;

            if (_kind == DriveKind.Taxi)
            {
                if (next is AIPilotTakeoffState)
                {
                    TransitionToTakeoff();
                    return true;
                }
                if (next is AIHeloTakeoffState)
                {
                    TransitionToHelo();
                    return true;
                }
                if (next is PilotParkedState)
                {
                    FinishCancel(UiLang.T("Takeoff cancelled", "起飞已取消"));
                    return true;
                }
                return true;
            }

            if (_kind == DriveKind.Takeoff)
            {
                if (next is AIPilotCombatModes)
                {
                    FinishSuccess();
                    return true;
                }
                if (next is PilotParkedState || next is AIPilotTaxiState)
                {
                    FinishCancel(UiLang.T("Takeoff aborted", "起飞中止"));
                    return true;
                }
                return true;
            }

            if (_kind == DriveKind.Helo)
            {
                if (next is AIHeloCombatState || next is AIHeloTransportState
                    || next is AIPilotCombatModes)
                {
                    FinishSuccess();
                    return true;
                }
                if (next is PilotParkedState)
                {
                    FinishCancel(UiLang.T("Takeoff aborted", "起飞中止"));
                    return true;
                }
                return true;
            }

            return false;
        }

        internal static bool BlockEject(Aircraft ac)
        {
            if (!_active || ac == null || _pilot == null)
                return false;
            return _pilot.aircraft == ac;
        }

        internal static bool IsDrivingTaxi(AIPilotTaxiState state)
        {
            return _active && _kind == DriveKind.Taxi && state != null && state == _taxi;
        }

        private static void FinishSuccess()
        {
            Stop();
            BeginnerAssist.Flash(UiLang.T("TAKEOFF  →  PILOT", "起飞完成  →  飞行员"));
        }

        private static void FinishCancel(string msg)
        {
            Stop();
            if (!string.IsNullOrEmpty(msg))
                BeginnerAssist.Flash(msg);
        }

        private static void TransitionToTakeoff()
        {
            if (_pilot == null)
                return;
            _transitioning = true;
            try
            {
                LeaveDriven();
                if (!EnterTakeoff(_pilot))
                {
                    FinishCancel(UiLang.T("Takeoff failed", "起飞失败"));
                    return;
                }
                BeginnerAssist._takeoff = BeginnerAssist.TakeoffPhase.Takeoff;
                BeginnerAssist._takeoffDetail = "TAKEOFF";
            }
            finally
            {
                _transitioning = false;
            }
        }

        private static void TransitionToHelo()
        {
            if (_pilot == null)
                return;
            _transitioning = true;
            try
            {
                LeaveDriven();
                if (!EnterHelo(_pilot))
                {
                    FinishCancel(UiLang.T("Takeoff failed", "起飞失败"));
                    return;
                }
                BeginnerAssist._takeoff = BeginnerAssist.TakeoffPhase.Takeoff;
                BeginnerAssist._takeoffDetail = "HELO";
            }
            finally
            {
                _transitioning = false;
            }
        }

        private static bool EnterTaxi(Pilot pilot)
        {
            _taxi = pilot.AITaxiState;
            if (_taxi == null)
            {
                _taxi = new AIPilotTaxiState();
                pilot.AITaxiState = _taxi;
            }
            if (TaxiDisembarking != null)
                TaxiDisembarking.SetValue(_taxi, false);
            try { _taxi.EnterState(pilot); }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("Vanilla taxi EnterState: " + ex.Message);
                _taxi = null;
                return false;
            }
            if (TaxiDisembarking != null)
                TaxiDisembarking.SetValue(_taxi, false);
            _kind = DriveKind.Taxi;
            _entered = true;
            _phaseAt = Time.unscaledTime;
            return true;
        }

        private static bool EnterTakeoff(Pilot pilot)
        {
            _takeoff = pilot.AITakeoffState;
            if (_takeoff == null)
            {
                _takeoff = new AIPilotTakeoffState();
                pilot.AITakeoffState = _takeoff;
            }
            try { _takeoff.EnterState(pilot); }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("Vanilla takeoff EnterState: " + ex.Message);
                _takeoff = null;
                return false;
            }
            _kind = DriveKind.Takeoff;
            _entered = true;
            _phaseAt = Time.unscaledTime;
            return true;
        }

        private static bool EnterHelo(Pilot pilot)
        {
            _helo = pilot.AIHeloTakeoffState;
            if (_helo == null)
            {
                _helo = new AIHeloTakeoffState();
                pilot.AIHeloTakeoffState = _helo;
            }
            try { _helo.EnterState(pilot); }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("Vanilla helo takeoff EnterState: " + ex.Message);
                _helo = null;
                return false;
            }
            _kind = DriveKind.Helo;
            _entered = true;
            _phaseAt = Time.unscaledTime;
            return true;
        }

        private static void LeaveDriven()
        {
            PilotBaseState driven = CurrentDriven();
            if (driven == null)
                return;
            try { driven.LeaveState(); }
            catch { }
            _taxi = null;
            _takeoff = null;
            _helo = null;
            _entered = false;
            _kind = DriveKind.None;
        }

        private static PilotBaseState CurrentDriven()
        {
            if (_kind == DriveKind.Taxi)
                return _taxi;
            if (_kind == DriveKind.Takeoff)
                return _takeoff;
            if (_kind == DriveKind.Helo)
                return _helo;
            return null;
        }

        private static void RefreshDetail(Aircraft ac)
        {
            PilotBaseState driven = CurrentDriven();
            string phase = null;
            try
            {
                if (driven != null)
                    phase = driven.stateDisplayName;
            }
            catch { }

            if (_kind == DriveKind.Taxi)
            {
                string extra = "";
                try
                {
                    if (TaxiWaitingClearance != null
                        && Convert.ToBoolean(TaxiWaitingClearance.GetValue(_taxi)))
                        extra = "  HOLD";
                    else if (TaxiWaitingCrossing != null
                        && Convert.ToBoolean(TaxiWaitingCrossing.GetValue(_taxi)))
                        extra = "  CROSS";
                }
                catch { }
                if (string.IsNullOrEmpty(phase))
                    phase = "TAXI";
                BeginnerAssist._takeoffDetail = phase + extra;
                if (TaxiAirbase != null)
                    BeginnerAssist._takeoffBase = TaxiAirbase.GetValue(_taxi) as Airbase;
            }
            else if (_kind == DriveKind.Takeoff)
            {
                if (string.IsNullOrEmpty(phase))
                    phase = "TAKEOFF";
                BeginnerAssist._takeoffDetail = phase;
                if (TakeoffAirbase != null)
                    BeginnerAssist._takeoffBase = TakeoffAirbase.GetValue(_takeoff) as Airbase;
            }
            else if (_kind == DriveKind.Helo)
            {
                if (string.IsNullOrEmpty(phase))
                    phase = "HELO";
                BeginnerAssist._takeoffDetail = phase;
            }

            if (_kind == DriveKind.Taxi && Time.unscaledTime - _phaseAt > 12f)
            {
                Airbase ab = BeginnerAssist._takeoffBase;
                if (ab == null || ab.disabled)
                {
                    FinishCancel(UiLang.T("No taxi route / airbase", "找不到滑行路线或机场"));
                }
            }
        }

        private static void TryFinishIfAirborne(Aircraft ac)
        {
            if (ac == null || !_active)
                return;
            float ralt = 0f;
            try { ralt = ac.radarAlt; }
            catch { }

            if (_kind == DriveKind.Takeoff && _takeoff != null)
            {
                bool taken = false;
                try
                {
                    if (TakeoffTakenOff != null)
                        taken = Convert.ToBoolean(TakeoffTakenOff.GetValue(_takeoff));
                }
                catch { }
                if (taken && ralt > 25f)
                    FinishSuccess();
                return;
            }

            if (_kind == DriveKind.Helo && Time.unscaledTime - _phaseAt > 6f && ralt > 50f)
                FinishSuccess();
        }

        private static bool UseHeloTakeoff(Aircraft ac, Pilot p)
        {
            if (p != null)
            {
                try
                {
                    if (p.pilotType == Pilot.PilotType.Helo)
                        return true;
                }
                catch { }
            }
            if (ac == null)
                return false;
            try
            {
                if (ac.autopilot is AutopilotHelo)
                    return true;
            }
            catch { }
            try
            {
                return AircraftIdentity.IsRotorcraft(AircraftIdentity.GetKey(ac));
            }
            catch
            {
                return false;
            }
        }

        private static Pilot PilotOf(Aircraft ac)
        {
            if (ac == null || ac.pilots == null || ac.pilots.Length == 0)
                return null;
            Pilot p = ac.pilots[0];
            if (p == null || p.dead || p.ejected)
                return null;
            return p;
        }
    }

    [HarmonyPatch(typeof(AIPilotTaxiState), "EjectCheck")]
    internal static class Patch_TaxiEject_PlayerTakeoff
    {
        [HarmonyPrefix]
        private static bool Prefix(AIPilotTaxiState __instance)
        {
            return !PlayerVanillaTakeoff.IsDrivingTaxi(__instance);
        }
    }

    [HarmonyPatch(typeof(Pilot), "SwitchState")]
    internal static class Patch_PilotSwitch_PlayerTakeoff
    {
        [HarmonyPrefix]
        private static bool Prefix(Pilot __instance, PilotBaseState state)
        {
            return !PlayerVanillaTakeoff.HandleSwitch(__instance, state);
        }
    }

    [HarmonyPatch(typeof(Pilot), "SwitchStateNew")]
    internal static class Patch_PilotSwitchNew_PlayerTakeoff
    {
        [HarmonyPrefix]
        private static bool Prefix(Pilot __instance, PilotBaseState state)
        {
            return !PlayerVanillaTakeoff.HandleSwitch(__instance, state);
        }
    }

    [HarmonyPatch(typeof(Aircraft), "StartEjectionSequence")]
    internal static class Patch_EjectSeq_PlayerTakeoff
    {
        [HarmonyPrefix]
        private static bool Prefix(Aircraft __instance)
        {
            if (!PlayerVanillaTakeoff.BlockEject(__instance))
                return true;
            return false;
        }
    }
}
