using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Player F2 LAND CV drives vanilla AIPilotLandingState (tailhook / attached deck)
    /// instead of the custom 五边 LandingGuidance path.
    /// Stays in PilotPlayerState so GLOC / eject / camera keep working.
    /// </summary>
    internal static class PlayerCarrierVanillaLand
    {
        private static readonly FieldInfo AirbaseField =
            AccessTools.Field(typeof(AIPilotLandingState), "airbase");
        private static readonly FieldInfo RunwayField =
            AccessTools.Field(typeof(AIPilotLandingState), "runwayUsage");
        private static readonly FieldInfo ParmsField =
            AccessTools.Field(typeof(AIPilotLandingState), "aircraftParameters");
        private static readonly FieldInfo ModeField =
            AccessTools.Field(typeof(AIPilotLandingState), "landingMode");
        private static readonly FieldInfo AdjSpeedField =
            AccessTools.Field(typeof(AIPilotLandingState), "adjustedLandingSpeed");
        private static readonly FieldInfo RequestedField =
            AccessTools.Field(typeof(AIPilotLandingState), "requestedLanding");
        private static readonly FieldInfo AircraftField =
            AccessTools.Field(typeof(PilotBaseState), "aircraft");

        private static readonly MethodInfo JoinPattern =
            AccessTools.Method(typeof(AIPilotLandingState), "JoinPattern");
        private static readonly MethodInfo FinalTurn =
            AccessTools.Method(typeof(AIPilotLandingState), "FinalTurn");
        private static readonly MethodInfo Stabilized =
            AccessTools.Method(typeof(AIPilotLandingState), "StabilizedApproach");
        private static readonly MethodInfo VerticalTd =
            AccessTools.Method(typeof(AIPilotLandingState), "VerticalTouchdown");
        private static readonly MethodInfo TouchedDown =
            AccessTools.Method(typeof(AIPilotLandingState), "TouchedDown");
        private static readonly MethodInfo Aborting =
            AccessTools.Method(typeof(AIPilotLandingState), "AbortingLanding");
        private static readonly MethodInfo CheckMode =
            AccessTools.Method(typeof(AIPilotLandingState), "LandingState_CheckMode");

        private static bool _active;
        private static bool _entered;
        private static Pilot _pilot;
        private static AIPilotLandingState _landing;
        private static Airbase _forcedAirbase;
        private static float _nextCheckMode;
        private static readonly object[] CheckTrue = new object[] { true };

        internal static bool IsActive
        {
            get { return _active && _entered && _landing != null; }
        }

        internal static bool IsDriving(AIPilotLandingState state)
        {
            return _active && state != null && state == _landing;
        }

        internal static bool TryApply(Aircraft ac)
        {
            if (ac == null)
                return false;
            if (!PlayerAutopilot.IsLandCarrierMode || !PlayerAutopilot.IsEngaged)
            {
                Stop();
                return false;
            }

            if (PlayerAutopilot._landBase == null || PlayerAutopilot._landBase.disabled
                || !PlayerAutopilot.IsCarrierAirbase(PlayerAutopilot._landBase))
            {
                PlayerAutopilot.ResolveLand(ac, true);
            }
            Airbase deck = PlayerAutopilot._landBase;
            if (deck == null || deck.disabled || !PlayerAutopilot.IsCarrierAirbase(deck))
            {
                Stop();
                return false;
            }

            Pilot pilot = PilotOf(ac);
            if (pilot == null)
            {
                Stop();
                return false;
            }

            _active = true;
            if (_forcedAirbase != deck && _entered)
                _entered = false;
            _forcedAirbase = deck;
            if (!_entered || _landing == null || _pilot != pilot)
                Enter(pilot);

            if (_landing == null)
            {
                Stop();
                return false;
            }

            if (Time.unscaledTime >= _nextCheckMode)
            {
                _nextCheckMode = Time.unscaledTime + 2f;
                try
                {
                    if (CheckMode != null)
                        CheckMode.Invoke(_landing, null);
                }
                catch { }
            }

            try { _landing.FixedUpdateState(pilot); }
            catch (Exception ex)
            {
                if (Plugin.DebugLog != null && Plugin.DebugLog.Value && Plugin.Log != null)
                    Plugin.Log.LogWarning("CV vanilla land: " + ex.Message);
            }

            string phase = null;
            try { phase = _landing.stateDisplayName; }
            catch { }
            string name = PlayerAutopilot.FormatAirbaseName(deck, true);
            if (string.IsNullOrEmpty(phase))
                phase = "CV AI";
            PlayerAutopilot._status = "CV AI  " + phase + "  " + name;
            return true;
        }

        internal static void Stop()
        {
            if (_entered && _landing != null)
            {
                try { _landing.LeaveState(); }
                catch { }
            }
            _active = false;
            _entered = false;
            _pilot = null;
            _landing = null;
            _forcedAirbase = null;
            _nextCheckMode = 0f;
        }

        internal static bool TryPinSearch(AIPilotLandingState state)
        {
            if (!IsDriving(state) || AirbaseField == null || RunwayField == null)
                return false;
            Aircraft ac = AircraftField != null
                ? AircraftField.GetValue(state) as Aircraft : null;
            if (ac == null)
                return false;
            Airbase deck = _forcedAirbase;
            if (deck == null || deck.disabled)
                return false;

            AircraftParameters parms = ParmsField != null
                ? ParmsField.GetValue(state) as AircraftParameters : null;
            if (parms == null)
            {
                try { parms = ac.GetAircraftParameters(); }
                catch { }
            }
            float adj = parms != null ? parms.landingSpeed : 70f;
            try
            {
                float mass = ac.GetMass();
                AircraftDefinition def = ac.definition as AircraftDefinition;
                float maxW = def != null && def.aircraftInfo != null
                    ? def.aircraftInfo.maxWeight : 0f;
                if (maxW > 1f && mass > 0f)
                    adj = Mathf.Sqrt(mass / maxW) * (parms != null ? parms.landingSpeed : 70f);
            }
            catch { }
            if (AdjSpeedField != null)
                AdjSpeedField.SetValue(state, adj);

            RunwayQuery q = PlayerAutopilot.BuildCarrierLandQuery(ac, parms);
            Airbase.Runway.RunwayUsage? usage = null;
            try { usage = deck.RequestLanding(ac, q); }
            catch { }
            if (!usage.HasValue)
            {
                try
                {
                    q.LandingSpeed = 0f;
                    q.MinSize = 12f;
                    usage = deck.RequestLanding(ac, q);
                }
                catch { }
            }
            AirbaseField.SetValue(state, deck);
            if (usage.HasValue)
            {
                RunwayField.SetValue(state, usage.Value);
                if (RequestedField != null)
                    RequestedField.SetValue(state, true);
            }
            return true;
        }

        internal static void RunCheckMode(AIPilotLandingState state)
        {
            if (state == null || ModeField == null)
                return;
            int mode = 0;
            try { mode = Convert.ToInt32(ModeField.GetValue(state)); }
            catch { return; }
            try
            {
                if (mode == 0 && JoinPattern != null)
                    JoinPattern.Invoke(state, CheckTrue);
                else if (mode == 1 && FinalTurn != null)
                    FinalTurn.Invoke(state, CheckTrue);
                else if (mode == 2 && Stabilized != null)
                    Stabilized.Invoke(state, CheckTrue);
                else if (mode == 3 && VerticalTd != null)
                    VerticalTd.Invoke(state, CheckTrue);
                else if (mode == 4 && TouchedDown != null)
                    TouchedDown.Invoke(state, CheckTrue);
                else if (mode == 5 && Aborting != null)
                    Aborting.Invoke(state, CheckTrue);
            }
            catch { }
        }

        internal static bool BlockStateSteal(Pilot pilot, PilotBaseState next)
        {
            if (!_active || _pilot == null || pilot != _pilot)
                return false;
            if (next == null)
                return true;
            if (next is AIPilotTaxiState)
                return true;
            if (next is AIPilotCombatModes)
                return true;
            return false;
        }

        internal static bool BlockEject(Aircraft ac)
        {
            if (!_active || ac == null || _pilot == null)
                return false;
            return _pilot.aircraft == ac;
        }

        private static void Enter(Pilot pilot)
        {
            if (_entered && _landing != null && _pilot == pilot)
                return;
            Airbase keepDeck = _forcedAirbase;
            if (_entered && _landing != null)
            {
                try { _landing.LeaveState(); }
                catch { }
            }
            _entered = false;
            _landing = null;
            _pilot = pilot;
            _active = true;
            _forcedAirbase = keepDeck;
            _landing = pilot.AILandingState;
            if (_landing == null)
            {
                _landing = new AIPilotLandingState();
                pilot.AILandingState = _landing;
            }
            try { _landing.EnterState(pilot); }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("CV vanilla EnterState: " + ex.Message);
                _landing = null;
                _active = false;
                _entered = false;
                return;
            }
            _entered = true;
            _nextCheckMode = Time.unscaledTime + 2f;
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

    [HarmonyPatch(typeof(AIPilotLandingState), "LandingState_SearchAirbase")]
    internal static class Patch_LandingSearch_PlayerCv
    {
        [HarmonyPrefix]
        private static bool Prefix(AIPilotLandingState __instance)
        {
            if (!PlayerCarrierVanillaLand.IsDriving(__instance))
                return true;
            PlayerCarrierVanillaLand.TryPinSearch(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(AIPilotLandingState), "LandingState_CheckMode")]
    internal static class Patch_LandingCheckMode_PlayerCv
    {
        [HarmonyPrefix]
        private static bool Prefix(AIPilotLandingState __instance)
        {
            if (!PlayerCarrierVanillaLand.IsDriving(__instance))
                return true;
            PlayerCarrierVanillaLand.RunCheckMode(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(AIPilotLandingState), "EjectionCheck")]
    internal static class Patch_LandingEjectCheck_PlayerCv
    {
        [HarmonyPrefix]
        private static bool Prefix(AIPilotLandingState __instance)
        {
            return !PlayerCarrierVanillaLand.IsDriving(__instance);
        }
    }

    [HarmonyPatch(typeof(Pilot), "SwitchState")]
    internal static class Patch_PilotSwitch_PlayerCv
    {
        [HarmonyPrefix]
        private static bool Prefix(Pilot __instance, PilotBaseState state)
        {
            if (!PlayerCarrierVanillaLand.BlockStateSteal(__instance, state))
                return true;
            PlayerAutopilot.DisengageFromOutside(true);
            return false;
        }
    }

    [HarmonyPatch(typeof(Aircraft), "StartEjectionSequence")]
    internal static class Patch_EjectSeq_PlayerCv
    {
        [HarmonyPrefix]
        private static bool Prefix(Aircraft __instance)
        {
            if (!PlayerCarrierVanillaLand.BlockEject(__instance))
                return true;
            return false;
        }
    }
}
