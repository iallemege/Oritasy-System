using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using NuclearOption.MissionEditorScripts;
using UnityEngine;
using UnityEngine.UI;

namespace Oritasy
{
    /// <summary>
    /// Reverse thrust on the throttle axis below 0%.
    /// 0% to -1% is airbrake; -1% to -100% is reverse (same 0–100% scale as forward).
    /// Shift/Ctrl are the throttle axis — not a separate toggle.
    /// Reducing through idle snaps to 0% (vanilla airbrake) until the
    /// decrease key/axis is released and pressed again for reverse.
    /// While already in reverse, another Ctrl / decrease press toggles the airbrake.
    /// Increasing out of reverse has no detent.
    /// Prop / PropFan (A-19 / CI-22) and VL-49 rotors/fans spin the other way
    /// while reversing. Helicopters cannot reverse except VL-49.
    /// AI uses reverse on landing rollout to shorten the ground run.
    /// </summary>
    internal static class ReverseThrustService
    {
        internal const float AirbrakeBand = 0.01f;
        /// <summary>Snap to exact 0% here so vanilla airbrake (throttle == 0) opens.</summary>
        private const float IdleSnap = 0.01f;

        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> Fraction;
        internal static ConfigEntry<float> AxisMaxPercent;

        private static readonly FieldInfo NozzleAircraft =
            AccessTools.Field(typeof(JetNozzle), "aircraft");
        private static readonly FieldInfo NozzlePart =
            AccessTools.Field(typeof(JetNozzle), "part");
        private static readonly FieldInfo NozzleThrustXf =
            AccessTools.Field(typeof(JetNozzle), "thrustTransform");
        private static readonly FieldInfo NozzleTotalThrust =
            AccessTools.Field(typeof(JetNozzle), "totalThrust");
        private static readonly FieldInfo PropAircraft =
            AccessTools.Field(typeof(ConstantSpeedProp), "aircraft");
        private static readonly FieldInfo PropPart =
            AccessTools.Field(typeof(ConstantSpeedProp), "unitPart");
        private static readonly FieldInfo PropHubVisible =
            AccessTools.Field(typeof(ConstantSpeedProp), "hubVisible");
        private static readonly FieldInfo PropTurnDir =
            AccessTools.Field(typeof(ConstantSpeedProp), "turnDirection");
        private static readonly FieldInfo PropBladeMin =
            AccessTools.Field(typeof(ConstantSpeedProp), "bladeMinPitch");
        private static readonly FieldInfo PropPitchRate =
            AccessTools.Field(typeof(ConstantSpeedProp), "pitchRate");
        private static readonly FieldInfo PropForceTorque =
            AccessTools.Field(typeof(ConstantSpeedProp), "forceAndTorque");
        private static readonly FieldInfo PropFanAircraft =
            AccessTools.Field(typeof(PropFan), "aircraft");
        private static readonly FieldInfo PropFanPart =
            AccessTools.Field(typeof(PropFan), "part");
        private static readonly FieldInfo PropFanThrustXf =
            AccessTools.Field(typeof(PropFan), "thrustTransform");
        private static readonly FieldInfo PropFanThrust =
            AccessTools.Field(typeof(PropFan), "thrust");
        private static readonly FieldInfo PropFanRpmRatio =
            AccessTools.Field(typeof(PropFan), "rpmRatio");
        private static readonly FieldInfo PropDisc =
            AccessTools.Field(typeof(ConstantSpeedProp), "propDisc");
        private static readonly FieldInfo DuctedAircraft =
            AccessTools.Field(typeof(DuctedFan), "aircraft");
        private static readonly FieldInfo DuctedPart =
            AccessTools.Field(typeof(DuctedFan), "unitPart");
        private static readonly FieldInfo DuctedRotator =
            AccessTools.Field(typeof(DuctedFan), "rotator");
        private static readonly FieldInfo DuctedRpm =
            AccessTools.Field(typeof(DuctedFan), "rpm");
        private static readonly FieldInfo DuctedThrustXf =
            AccessTools.Field(typeof(DuctedFan), "thrustVector");
        private static readonly FieldInfo DuctedCurrentThrust =
            AccessTools.Field(typeof(DuctedFan), "currentThrust");
        private static readonly FieldInfo RotorHub =
            AccessTools.Field(typeof(RotorShaft), "hubRotator");
        private static readonly FieldInfo RotorAngularPos =
            AccessTools.Field(typeof(RotorShaft), "angularPosition");
        private static readonly FieldInfo RotorAngularSpeed =
            AccessTools.Field(typeof(RotorShaft), "angularSpeed");
        private static readonly FieldInfo RotorDirMult =
            AccessTools.Field(typeof(RotorShaft), "directionMult");
        private static readonly FieldInfo SimThrottleField =
            AccessTools.Field(typeof(PilotPlayerState), "simulatedThrottle");
        private static readonly FieldInfo PilotField =
            AccessTools.Field(typeof(PilotBaseState), "pilot");
        private static readonly FieldInfo InputsField =
            AccessTools.Field(typeof(PilotBaseState), "controlInputs");
        private static readonly FieldInfo GearAircraft =
            AccessTools.Field(typeof(LandingGear), "aircraft");
        private static readonly FieldInfo GearInputs =
            AccessTools.Field(typeof(LandingGear), "controlInputs");

        private static readonly HashSet<int> Capability = new HashSet<int>();
        private static readonly HashSet<int> AiReverseDecided = new HashSet<int>();
        private static readonly HashSet<int> AiReverseUse = new HashSet<int>();
        private static int _heloCacheId;
        private static bool _heloCache;

        private static float _signed;
        private static int _boundAircraftId;
        private static int _band = 0;
        private static bool _hasAirbrake;
        private static int _nozzleFlipFrame = -1;
        private static int _nozzleFlipId;
        private static bool _visualReverse;
        private static bool _airbrakeHold;
        private static bool _airbrakeForceOpen;
        private static float _airbrakeSavedThrottle;
        private static bool _gearHold;
        private static float _gearSavedThrottle;
        private static bool _reverseAirbrake;
        private static bool _wasInReverse;
        private static bool _decreaseHeldPrev;
        private const int IdleGateOpen = 0;
        private const int IdleGateLocked = 1;
        private const int IdleGateArmed = 2;
        private static int _idleGate;
        private static FieldInfo _playerInputField;
        private static MethodInfo _getAxisRaw;
        private static readonly object[] ThrottleAxisArgs = new object[1];

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Flight", "ReverseThrustEnabled", true,
                "Reverse thrust on the throttle axis below 0% (Shift/Ctrl). 0 to -1% is airbrake only if the airframe has one.");
            AxisMaxPercent = config.Bind("Flight", "ReverseThrustAxisMaxPercent", 100f,
                "Negative throttle travel for full reverse (1–100). Same 0–100% scale as forward.");
            Fraction = config.Bind("Flight", "ReverseThrustFraction", 0.4f,
                "Reverse strength vs max thrust at the axis end (0.2–0.8).");
            // Old builds capped this at 20 (the previous default/max). Open the full 0–100% band.
            if (AxisMaxPercent.Value <= 20f)
                AxisMaxPercent.Value = 100f;
        }

        internal static bool IsOn()
        {
            return Enabled == null || Enabled.Value;
        }

        internal static float SignedThrottle()
        {
            return _signed;
        }

        internal static float AxisMax()
        {
            float p = AxisMaxPercent != null ? AxisMaxPercent.Value : 100f;
            if (p < 1f)
                p = 1f;
            if (p > 100f)
                p = 100f;
            return p * 0.01f;
        }

        internal static float ReverseFraction()
        {
            float f = Fraction != null ? Fraction.Value : 0.4f;
            if (f < 0.15f)
                f = 0.15f;
            if (f > 0.55f)
                f = 0.55f;
            return f;
        }

        /// <summary>0% to -1% is airbrake only if this airframe has one.</summary>
        internal static bool CurrentHasAirbrake()
        {
            return _hasAirbrake;
        }

        internal static float Deadband()
        {
            return _hasAirbrake ? AirbrakeBand : 0f;
        }

        internal static bool DetectAirbrake(Aircraft ac)
        {
            if (ac == null)
                return false;
            try
            {
                return ac.GetComponentInChildren<Airbrake>(true) != null;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>0 at the airbrake band (or 0% if no airbrake), 1 at the negative end.</summary>
        internal static float ReverseAmount()
        {
            float signed = _signed;
            float band = Deadband();
            if (signed >= 0f)
                return 0f;
            if (signed > -band)
                return 0f;
            float max = AxisMax();
            if (max <= band)
                return 1f;
            float t = (-signed - band) / (max - band);
            if (t < 0f)
                t = 0f;
            if (t > 1f)
                t = 1f;
            return t;
        }

        internal static bool InAirbrakeBand()
        {
            if (!_hasAirbrake)
                return false;
            if (ReverseAmount() > 0f)
                return _reverseAirbrake;
            if (_idleGate == IdleGateLocked || _idleGate == IdleGateArmed)
                return true;
            return _signed <= 0f && _signed > -AirbrakeBand;
        }

        internal static bool ReverseAirbrakeOn()
        {
            return _reverseAirbrake && ReverseAmount() > 0f;
        }

        internal static void PollAirbrakeToggle()
        {
            if (!IsOn())
                return;
            Aircraft ac = LocalPlayerAircraft();
            if (!_hasAirbrake)
                _hasAirbrake = DetectAirbrake(ac);
            bool inReverse = ReverseAmount() > 0f;
            bool decrease = DecreaseHeld();
            bool keyDown = Input.GetKeyDown(KeyCode.LeftControl)
                || Input.GetKeyDown(KeyCode.RightControl);
            if (!inReverse)
            {
                _reverseAirbrake = false;
                _wasInReverse = false;
                _decreaseHeldPrev = decrease;
                return;
            }
            bool axisEdge = decrease && !_decreaseHeldPrev;
            if (_wasInReverse && (keyDown || axisEdge))
                ToggleReverseAirbrake(ac);
            _wasInReverse = true;
            _decreaseHeldPrev = decrease;
        }

        private static bool DecreaseHeld()
        {
            if (PlayerThrottleAxis() < -0.45f)
                return true;
            return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        }

        internal static bool IsVl49(Aircraft ac)
        {
            if (ac == null)
                return false;
            try { return AircraftIdentity.IsVl49(AircraftIdentity.GetKey(ac)); }
            catch { return false; }
        }

        /// <summary>Helicopters cannot reverse, except VL-49.</summary>
        internal static bool AllowsReverse(Aircraft ac)
        {
            if (ac == null || !IsOn())
                return false;
            if (IsVl49(ac))
                return true;
            return !IsHelicopterAirframe(ac);
        }

        internal static bool IsHelicopterAirframe(Aircraft ac)
        {
            if (ac == null)
                return false;
            int id = ac.GetInstanceID();
            if (id == _heloCacheId)
                return _heloCache;
            _heloCacheId = id;
            _heloCache = DetectHelicopter(ac);
            return _heloCache;
        }

        private static bool DetectHelicopter(Aircraft ac)
        {
            if (ac == null)
                return false;
            try
            {
                string key = AircraftIdentity.GetKey(ac);
                if (AircraftIdentity.IsRotorcraft(key))
                    return true;
                // Lift fans / STOVL / VL-49 are not helicopters.
                if (AircraftIdentity.IsVl49(key)
                    || AircraftIdentity.IsEw25(key)
                    || AircraftIdentity.IsFs20(key)
                    || AircraftIdentity.IsVt7(key))
                    return false;
            }
            catch { }
            try
            {
                if (ac.autopilot is AutopilotHelo)
                    return true;
            }
            catch { }
            try
            {
                if (ac.GetComponentInChildren<HeloControlsFilter>(true) != null)
                    return true;
                if (ac.GetComponentInChildren<CompoundHeloController>(true) != null)
                    return true;
            }
            catch { }
            try
            {
                Pilot[] pilots = ac.pilots;
                if (pilots != null)
                {
                    for (int i = 0; i < pilots.Length; i++)
                    {
                        if (pilots[i] != null && pilots[i].pilotType == Pilot.PilotType.Helo)
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        internal static bool IsPlayerFlown(Aircraft ac)
        {
            if (ac == null)
                return false;
            try
            {
                Pilot[] pilots = ac.pilots;
                if (pilots != null)
                {
                    for (int i = 0; i < pilots.Length; i++)
                    {
                        if (pilots[i] != null && pilots[i].playerControlled)
                            return true;
                    }
                }
            }
            catch { }
            return IsLocalAircraft(ac);
        }

        internal static void EnableCapability(Aircraft ac)
        {
            if (ac == null || !Plugin.IsRuntimeInstance(ac))
                return;
            if (!AllowsReverse(ac))
                return;
            if (!IsPlayerFlown(ac))
                return;
            Capability.Add(ac.GetInstanceID());
            ControlsFilter filter = FlightEnvelopeService.GetControlsFilter(ac);
            if (filter != null)
                filter.ReverseThrust = true;
        }

        internal static bool IsLocalAircraft(Aircraft ac)
        {
            if (ac == null || !ac.LocalSim)
                return false;
            try
            {
                Aircraft local;
                if (!GameManager.GetLocalAircraft(out local) || local == null)
                    return false;
                return object.ReferenceEquals(local, ac);
            }
            catch
            {
                return false;
            }
        }

        internal static bool EngineRunning(Aircraft ac)
        {
            if (ac == null)
                return false;
            try { return ac.Ignition; }
            catch { return false; }
        }

        internal static bool IsActive(Aircraft ac)
        {
            if (!IsOn() || !AllowsReverse(ac))
                return false;
            if (!IsLocalAircraft(ac) || !IsPlayerFlown(ac))
                return false;
            if (!EngineRunning(ac))
                return false;
            return ReverseAmount() > 0f;
        }

        internal static bool VisualReverse
        {
            get { return _visualReverse; }
        }

        internal static void SetVisualReverse(bool on)
        {
            _visualReverse = on;
        }

        private static void ClearSignedThrottleState()
        {
            _signed = 0f;
            _visualReverse = false;
            _band = 0;
            _reverseAirbrake = false;
            _wasInReverse = false;
            _idleGate = IdleGateOpen;
            _airbrakeHold = false;
            _airbrakeForceOpen = false;
            _gearHold = false;
        }

        internal static void ApplySignedThrottle(PilotPlayerState state)
        {
            if (!IsOn() || state == null)
                return;
            if (SimThrottleField == null)
                return;
            Aircraft gate = ReadAircraft(state);
            if (!AllowsReverse(gate))
            {
                // Helos (except VL-49) never write signed reverse. A leftover
                // value from a previous jet would lock the collective bar at
                // 100% reverse because the gauge reads this static field.
                if (gate != null)
                    ClearSignedThrottleState();
                return;
            }
            EnableCapability(gate);

            float sim = 0f;
            try { sim = (float)SimThrottleField.GetValue(state); }
            catch { return; }

            Aircraft ac = gate;
            int id = ac != null ? ac.GetInstanceID() : 0;
            if (id != _boundAircraftId)
            {
                _boundAircraftId = id;
                _band = 0;
                _idleGate = IdleGateOpen;
                _reverseAirbrake = false;
                _wasInReverse = false;
                _decreaseHeldPrev = false;
                _hasAirbrake = DetectAirbrake(ac);
                if (sim < 0f)
                {
                    sim = 0f;
                    try { SimThrottleField.SetValue(state, 0f); }
                    catch { }
                }
            }

            float prev = _signed;
            float axis = ReadThrottleAxis();
            float gated = ApplyIdleGate(prev, sim, axis);
            if (gated != sim)
            {
                sim = gated;
                try { SimThrottleField.SetValue(state, sim); }
                catch { }
            }

            float max = AxisMax();
            if (sim < -max)
            {
                sim = -max;
                try { SimThrottleField.SetValue(state, sim); }
                catch { }
            }

            // APU stays on after engine start — only block reverse while the engine is still off.
            if (!EngineRunning(ac) && sim < 0f)
            {
                float floor = _hasAirbrake ? -AirbrakeBand : 0f;
                if (sim < floor)
                {
                    sim = floor;
                    try { SimThrottleField.SetValue(state, sim); }
                    catch { }
                }
            }

            ControlInputs inputs = ReadInputs(state);
            if (inputs != null)
            {
                if (sim == 0f || _idleGate == IdleGateLocked || _idleGate == IdleGateArmed)
                    inputs.throttle = 0f;
                else
                    inputs.throttle = Mathf.Clamp01(sim);
            }

            _signed = sim;
            ReportBand(ac, sim);
        }

        private static void ToggleReverseAirbrake(Aircraft ac)
        {
            if (ac != null && !_hasAirbrake)
                _hasAirbrake = DetectAirbrake(ac);
            _reverseAirbrake = !_reverseAirbrake;
            ReportAirbrakeToggle(ac);
        }

        private static Aircraft LocalPlayerAircraft()
        {
            try
            {
                Aircraft ac;
                if (!GameManager.GetLocalAircraft(out ac))
                    return null;
                return ac;
            }
            catch
            {
                return null;
            }
        }

        private static void ReportAirbrakeToggle(Aircraft ac)
        {
            if (ac == null)
                return;
            try
            {
                if (SceneSingleton<CombatHUD>.i == null
                    || SceneSingleton<CombatHUD>.i.aircraft != ac)
                    return;
                if (SceneSingleton<AircraftActionsReport>.i == null)
                    return;
                SceneSingleton<AircraftActionsReport>.i.ReportText(
                    _reverseAirbrake
                        ? UiLang.T("Airbrake ON", "减速板 开")
                        : UiLang.T("Airbrake OFF", "减速板 关"),
                    2.5f);
            }
            catch { }
        }

        /// <summary>
        /// One-way idle detent: first decrease snaps to exact 0% (airbrake).
        /// Release then decrease again to enter reverse. Increase never stops.
        /// </summary>
        private static float ApplyIdleGate(float prev, float sim, float axis)
        {
            bool decrease = axis < -0.45f;
            bool increase = axis > 0.45f;
            if (increase)
            {
                if (sim > IdleSnap)
                    _idleGate = IdleGateOpen;
                return sim;
            }
            if (decrease)
            {
                if (_idleGate == IdleGateArmed)
                {
                    _idleGate = IdleGateOpen;
                    return sim;
                }
                if (_idleGate == IdleGateLocked)
                {
                    if (sim < IdleSnap)
                        return 0f;
                    return sim;
                }
                if (prev >= 0f && sim < IdleSnap)
                {
                    _idleGate = IdleGateLocked;
                    return 0f;
                }
                return sim;
            }
            if (_idleGate == IdleGateLocked)
                _idleGate = IdleGateArmed;
            if (_idleGate == IdleGateArmed && sim >= 0f && sim < IdleSnap)
                return 0f;
            if (sim > IdleSnap)
                _idleGate = IdleGateOpen;
            return sim;
        }

        internal static float PlayerThrottleAxis()
        {
            return ReadThrottleAxis();
        }

        private static float ReadThrottleAxis()
        {
            try
            {
                if (_playerInputField == null)
                    _playerInputField = typeof(GameManager).GetField("playerInput",
                        BindingFlags.Public | BindingFlags.Static);
                object pi = _playerInputField != null ? _playerInputField.GetValue(null) : null;
                if (pi != null)
                {
                    if (_getAxisRaw == null)
                        _getAxisRaw = pi.GetType().GetMethod("GetAxisRaw", new Type[] { typeof(string) });
                    if (_getAxisRaw != null)
                    {
                        ThrottleAxisArgs[0] = "Throttle";
                        object v = _getAxisRaw.Invoke(pi, ThrottleAxisArgs);
                        if (v is float)
                            return (float)v;
                    }
                }
            }
            catch { }
            bool down = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            bool up = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (down && !up)
                return -1f;
            if (up && !down)
                return 1f;
            return 0f;
        }

        private static void ReportBand(Aircraft ac, float sim)
        {
            int band = sim < -Deadband() ? 2 : 0;
            if (band == _band)
                return;
            _band = band;
            if (ac == null)
                return;
            try
            {
                if (SceneSingleton<CombatHUD>.i == null
                    || SceneSingleton<CombatHUD>.i.aircraft != ac)
                    return;
                if (SceneSingleton<AircraftActionsReport>.i == null)
                    return;
                SceneSingleton<AircraftActionsReport>.i.ReportText(
                    band == 2
                        ? UiLang.T("Reverse thrust ON", "反推 开")
                        : UiLang.T("Reverse thrust OFF", "反推 关"),
                    2.5f);
            }
            catch { }
        }

        private static Aircraft ReadAircraft(PilotPlayerState state)
        {
            if (PilotField == null)
                return null;
            try
            {
                Pilot pilot = PilotField.GetValue(state) as Pilot;
                return pilot != null ? pilot.aircraft : null;
            }
            catch
            {
                return null;
            }
        }

        private static ControlInputs ReadInputs(PilotPlayerState state)
        {
            if (InputsField == null)
                return null;
            try { return InputsField.GetValue(state) as ControlInputs; }
            catch { return null; }
        }

        internal static void NoteNozzleFlip(Aircraft ac)
        {
            if (ac == null)
                return;
            _nozzleFlipFrame = Time.frameCount;
            _nozzleFlipId = ac.GetInstanceID();
        }

        internal static bool NozzleFlippedThisFrame(Aircraft ac)
        {
            return ac != null
                && _nozzleFlipFrame == Time.frameCount
                && _nozzleFlipId == ac.GetInstanceID();
        }

        internal static bool IsAirborne(Aircraft ac)
        {
            if (ac == null)
                return false;
            try
            {
                if (ac.IsLanded())
                    return false;
                float floor = 3f;
                if (ac.definition != null)
                    floor = ac.definition.spawnOffset.y + 2.5f;
                return ac.radarAlt > floor;
            }
            catch
            {
                try { return ac.radarAlt > 3f; }
                catch { return false; }
            }
        }

        internal static float AiLandingReverseAmount(Aircraft ac)
        {
            if (ac == null || IsPlayerFlown(ac))
                return 0f;
            if (!AllowsReverse(ac))
                return 0f;
            if (!IsAiReverseReady(ac))
            {
                ClearAiReverseRoll(ac);
                return 0f;
            }
            if (!AiUsesReverseThisLanding(ac))
                return 0f;
            return 1f;
        }

        private static bool AiUsesReverseThisLanding(Aircraft ac)
        {
            int id = ac.GetInstanceID();
            if (AiReverseDecided.Add(id))
            {
                if (UnityEngine.Random.value < 0.5f)
                    AiReverseUse.Add(id);
            }
            return AiReverseUse.Contains(id);
        }

        private static void ClearAiReverseRoll(Aircraft ac)
        {
            if (ac == null)
                return;
            int id = ac.GetInstanceID();
            AiReverseDecided.Remove(id);
            AiReverseUse.Remove(id);
        }

        internal static bool IsAiReverseReady(Aircraft ac)
        {
            if (ac == null)
                return false;
            if (IsAiInTakeoff(ac))
                return false;
            float ralt = 999f;
            try { ralt = ac.radarAlt; }
            catch { return false; }
            if (ralt >= 50f)
                return false;
            if (!GearDown(ac))
                return false;
            float spd = 0f;
            try { spd = ac.speed; }
            catch { return false; }
            return spd > 3f;
        }

        private static bool GearDown(Aircraft ac)
        {
            if (ac == null)
                return false;
            try
            {
                if (ac.gearDeployed)
                    return true;
            }
            catch { }
            try
            {
                LandingGear.GearState gs = ac.gearState;
                return gs == LandingGear.GearState.LockedExtended
                    || gs == LandingGear.GearState.Extending;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsAiInTakeoff(Aircraft ac)
        {
            try
            {
                Pilot[] pilots = ac.pilots;
                if (pilots == null)
                    return false;
                for (int i = 0; i < pilots.Length; i++)
                {
                    Pilot p = pilots[i];
                    if (p == null || p.playerControlled || p.currentState == null)
                        continue;
                    if (p.currentState is AIPilotTakeoffState
                        || p.currentState is AIHeloTakeoffState)
                        return true;
                }
            }
            catch { }
            return false;
        }

        internal static void ApplyAirframeReverse(Aircraft ac)
        {
            if (ac == null || ac.rb == null || !IsOn() || !AllowsReverse(ac))
                return;
            float amt = 0f;
            if (IsLocalAircraft(ac) && IsPlayerFlown(ac))
            {
                if (!EngineRunning(ac))
                    return;
                amt = ReverseAmount();
            }
            else
                amt = AiLandingReverseAmount(ac);
            if (amt < 0.02f)
                return;
            float maxT = 0f;
            bool got = false;
            try { got = ac.GetMaxThrust(out maxT); }
            catch { got = false; }
            if (!got || maxT < 50f)
            {
                if (IsVl49(ac) && ac.rb != null)
                    maxT = ac.rb.mass * 8f;
                else
                    return;
            }

            float pull = maxT * ReverseFraction();
            float cap = ac.rb.mass * 6f;
            if (pull > cap)
                pull = cap;
            ac.rb.AddForce(-ReverseAxis(ac) * pull * amt);
        }

        /// <summary>
        /// On the ground keep reverse horizontal through the CG so a high
        /// tractor prop (CI-22) cannot pitch the nose up around the gear.
        /// </summary>
        internal static Vector3 ReverseAxis(Aircraft ac)
        {
            if (ac == null)
                return Vector3.forward;
            Vector3 axis = ac.transform.forward;
            if (!IsAirborne(ac))
            {
                axis.y = 0f;
                if (axis.sqrMagnitude < 0.0001f)
                    axis = ac.transform.forward;
                else
                    axis.Normalize();
                return axis;
            }
            if (ac.rb != null && ac.rb.velocity.sqrMagnitude > 225f)
                return ac.rb.velocity.normalized;
            return axis;
        }

        internal static void CancelForwardThrust(Rigidbody rb, Vector3 forwardDir, float thrust)
        {
            if (rb == null || thrust <= 1f)
                return;
            rb.AddForce(-forwardDir.normalized * thrust);
        }

        internal static void ApplyOpposingForce(Rigidbody rb, Vector3 forwardDir, float thrust)
        {
            if (rb == null || thrust <= 1f)
                return;
            float amt = ReverseAmount();
            if (amt <= 0f)
                return;
            rb.AddForce(-forwardDir.normalized * thrust * (1f + ReverseFraction()) * amt);
        }

        internal static void ApplyCgReverse(Aircraft ac, float thrust)
        {
            if (ac == null || ac.rb == null || thrust <= 1f)
                return;
            float amt = ReverseAmount();
            if (amt <= 0f)
                return;
            Vector3 axis = ReverseAxis(ac);
            float mag = thrust * ReverseFraction() * amt;
            float cap = ac.rb.mass * 6f * amt;
            if (mag > cap)
                mag = cap;
            ac.rb.AddForceAtPosition(-axis * mag, ac.rb.worldCenterOfMass);
        }

        /// <summary>
        /// Blade force/torque is applied at the hub. On the ground that yaws
        /// a tractor prop (CI-22) around the gear — cancel it, then push at CG.
        /// </summary>
        internal static void NeutralizeGroundPropAero(ConstantSpeedProp prop, Aircraft ac)
        {
            if (prop == null || ac == null || IsAirborne(ac))
                return;
            if (PropForceTorque == null)
                return;
            ForceAndTorque ft;
            try { ft = (ForceAndTorque)PropForceTorque.GetValue(prop); }
            catch { return; }
            UnitPart part = ReadPropPart(prop);
            Rigidbody rb = part != null && part.rb != null ? part.rb : ac.rb;
            if (rb == null)
                return;
            if (ft.force.sqrMagnitude > 0.01f)
                rb.AddForce(-ft.force);
            if (ft.torque.sqrMagnitude > 0.01f)
                rb.AddTorque(-ft.torque);
        }

        internal static void NeutralizeGroundPropFan(PropFan fan, Aircraft ac)
        {
            if (fan == null || ac == null || IsAirborne(ac))
                return;
            UnitPart part = ReadPropFanPart(fan);
            Rigidbody rb = part != null && part.rb != null ? part.rb : ac.rb;
            Transform xf = ReadPropFanThrustXf(fan);
            float thrust = ReadPropFanForwardThrust(fan);
            if (rb == null || thrust <= 1f)
                return;
            Vector3 fwd = xf != null ? xf.forward : ac.transform.forward;
            CancelForwardThrust(rb, fwd, thrust);
            ApplyCgReverse(ac, thrust);
        }

        internal static void HoldThrottleForProp(ControlInputs inputs, ref float saved)
        {
            saved = -1f;
            if (inputs == null)
                return;
            float amt = ReverseAmount();
            if (amt <= 0f)
                return;
            saved = inputs.throttle;
            float pwr = 0.35f + 0.65f * amt;
            if (inputs.throttle < pwr)
                inputs.throttle = pwr;
        }

        internal static void RestoreThrottle(ControlInputs inputs, float saved)
        {
            if (inputs == null || saved < 0f)
                return;
            inputs.throttle = saved;
        }

        internal static ControlInputs InputsOf(Aircraft ac)
        {
            if (ac == null)
                return null;
            try { return ac.GetInputs(); }
            catch { return null; }
        }

        internal static void ReversePropSpin(ConstantSpeedProp prop)
        {
            if (prop == null || PropHubVisible == null)
                return;
            GameObject hub = null;
            try { hub = PropHubVisible.GetValue(prop) as GameObject; }
            catch { return; }
            if (hub == null)
                return;
            float td = 1f;
            if (PropTurnDir != null)
            {
                try
                {
                    object v = PropTurnDir.GetValue(prop);
                    if (v != null)
                        td = (float)Convert.ToInt32(v);
                }
                catch { td = 1f; }
            }
            // Vanilla: RPM * -6 * turnDir * dt. Extra 12* flips the spin.
            float extra = prop.RPM * 12f * td * Time.deltaTime;
            hub.transform.Rotate(0f, 0f, extra, Space.Self);
            if (PropDisc == null)
                return;
            try
            {
                MeshFilter disc = PropDisc.GetValue(prop) as MeshFilter;
                if (disc != null && disc.transform != null
                    && disc.transform != hub.transform)
                    disc.transform.Rotate(0f, 0f, extra, Space.Self);
            }
            catch { }
        }

        internal static void ReverseDuctedSpin(DuctedFan fan)
        {
            if (fan == null || DuctedRotator == null || DuctedRpm == null)
                return;
            try
            {
                Transform rot = DuctedRotator.GetValue(fan) as Transform;
                if (rot == null)
                    return;
                float rpm = (float)DuctedRpm.GetValue(fan);
                rot.localEulerAngles -= Vector3.forward * rpm * 12f * Time.deltaTime;
            }
            catch { }
        }

        internal static void ReverseRotorSpin(RotorShaft shaft)
        {
            if (shaft == null || RotorHub == null)
                return;
            try
            {
                Transform hub = RotorHub.GetValue(shaft) as Transform;
                if (hub == null)
                    return;
                float speed = 0f;
                int dir = 1;
                if (RotorAngularSpeed != null)
                    speed = (float)RotorAngularSpeed.GetValue(shaft);
                if (RotorDirMult != null)
                    dir = Convert.ToInt32(RotorDirMult.GetValue(shaft));
                if (RotorAngularPos != null)
                {
                    float pos = (float)RotorAngularPos.GetValue(shaft);
                    hub.localEulerAngles = new Vector3(0f, -pos * 57.29578f, 0f);
                }
                hub.Rotate(new Vector3(0f, -2f * speed * (float)dir * 57.29578f * Time.deltaTime, 0f), Space.Self);
            }
            catch { }
        }

        internal static Aircraft ReadDuctedAircraft(DuctedFan f)
        {
            if (f == null || DuctedAircraft == null)
                return null;
            try { return DuctedAircraft.GetValue(f) as Aircraft; }
            catch { return null; }
        }

        internal static UnitPart ReadDuctedPart(DuctedFan f)
        {
            if (f == null || DuctedPart == null)
                return null;
            try { return DuctedPart.GetValue(f) as UnitPart; }
            catch { return null; }
        }

        internal static Transform ReadDuctedThrustXf(DuctedFan f)
        {
            if (f == null || DuctedThrustXf == null)
                return null;
            try { return DuctedThrustXf.GetValue(f) as Transform; }
            catch { return null; }
        }

        internal static float ReadDuctedThrust(DuctedFan f)
        {
            if (f == null)
                return 0f;
            try { return f.GetThrust(); }
            catch
            {
                if (DuctedCurrentThrust == null)
                    return 0f;
                try
                {
                    object v = DuctedCurrentThrust.GetValue(f);
                    return v != null ? (float)v : 0f;
                }
                catch { return 0f; }
            }
        }

        internal static void DriveReversePitch(ConstantSpeedProp prop)
        {
            if (prop == null || PropBladeMin == null || PropPitchRate == null)
                return;
            try
            {
                float minP = (float)PropBladeMin.GetValue(prop);
                float rate = (float)PropPitchRate.GetValue(prop);
                prop.PropPitch = Mathf.MoveTowards(
                    prop.PropPitch, minP, rate * Time.fixedDeltaTime);
            }
            catch { }
        }

        internal static Aircraft ReadNozzleAircraft(JetNozzle n)
        {
            if (n == null || NozzleAircraft == null)
                return null;
            try { return NozzleAircraft.GetValue(n) as Aircraft; }
            catch { return null; }
        }

        internal static UnitPart ReadNozzlePart(JetNozzle n)
        {
            if (n == null || NozzlePart == null)
                return null;
            try { return NozzlePart.GetValue(n) as UnitPart; }
            catch { return null; }
        }

        internal static Transform ReadNozzleThrustXf(JetNozzle n)
        {
            if (n == null || NozzleThrustXf == null)
                return null;
            try { return NozzleThrustXf.GetValue(n) as Transform; }
            catch { return null; }
        }

        internal static float ReadNozzleTotalThrust(JetNozzle n)
        {
            if (n == null || NozzleTotalThrust == null)
                return 0f;
            try
            {
                object v = NozzleTotalThrust.GetValue(n);
                return v != null ? (float)v : 0f;
            }
            catch { return 0f; }
        }

        internal static Aircraft ReadPropAircraft(ConstantSpeedProp p)
        {
            if (p == null || PropAircraft == null)
                return null;
            try { return PropAircraft.GetValue(p) as Aircraft; }
            catch { return null; }
        }

        internal static UnitPart ReadPropPart(ConstantSpeedProp p)
        {
            if (p == null || PropPart == null)
                return null;
            try { return PropPart.GetValue(p) as UnitPart; }
            catch { return null; }
        }

        internal static Aircraft ReadPropFanAircraft(PropFan p)
        {
            if (p == null || PropFanAircraft == null)
                return null;
            try { return PropFanAircraft.GetValue(p) as Aircraft; }
            catch { return null; }
        }

        internal static UnitPart ReadPropFanPart(PropFan p)
        {
            if (p == null || PropFanPart == null)
                return null;
            try { return PropFanPart.GetValue(p) as UnitPart; }
            catch { return null; }
        }

        internal static Transform ReadPropFanThrustXf(PropFan p)
        {
            if (p == null || PropFanThrustXf == null)
                return null;
            try { return PropFanThrustXf.GetValue(p) as Transform; }
            catch { return null; }
        }

        internal static float ReadPropFanForwardThrust(PropFan p)
        {
            if (p == null || PropFanThrust == null)
                return 0f;
            try
            {
                float thrust = (float)PropFanThrust.GetValue(p);
                float rpm = 1f;
                if (PropFanRpmRatio != null)
                    rpm = (float)PropFanRpmRatio.GetValue(p);
                return thrust * rpm;
            }
            catch { return 0f; }
        }

        internal static bool BeginAirbrakeHold(ControlInputs inputs)
        {
            _airbrakeHold = false;
            _airbrakeForceOpen = false;
            if (inputs == null)
                return false;
            if (ReverseAirbrakeOn())
            {
                _airbrakeSavedThrottle = inputs.throttle;
                inputs.throttle = 0f;
                _airbrakeForceOpen = true;
                return true;
            }
            if (ReverseAmount() <= 0f)
                return false;
            if (inputs.throttle != 0f)
                return false;
            inputs.throttle = 0.02f;
            _airbrakeHold = true;
            return true;
        }

        internal static void EndAirbrakeHold(ControlInputs inputs)
        {
            if (inputs == null)
                return;
            if (_airbrakeForceOpen)
            {
                inputs.throttle = _airbrakeSavedThrottle;
                _airbrakeForceOpen = false;
                return;
            }
            if (!_airbrakeHold)
                return;
            inputs.throttle = 0f;
            _airbrakeHold = false;
        }

        /// <summary>
        /// Vanilla locks wheel brakes at idle when speed &lt; 1. Reverse writes
        /// throttle 0, so that parking lock blocks taxi-back. Lift it only
        /// while reverse is on; player brake input still works.
        /// </summary>
        internal static void BeginGearReverseHold(LandingGear gear)
        {
            _gearHold = false;
            if (gear == null || ReverseAmount() <= 0f)
                return;
            Aircraft ac = null;
            if (GearAircraft != null)
            {
                try { ac = GearAircraft.GetValue(gear) as Aircraft; }
                catch { ac = null; }
            }
            if (ac == null || !IsLocalAircraft(ac) || !IsPlayerFlown(ac))
                return;
            ControlInputs inputs = null;
            if (GearInputs != null)
            {
                try { inputs = GearInputs.GetValue(gear) as ControlInputs; }
                catch { inputs = null; }
            }
            if (inputs == null || inputs.throttle >= 0.1f)
                return;
            _gearSavedThrottle = inputs.throttle;
            inputs.throttle = 0.15f;
            _gearHold = true;
        }

        internal static void EndGearReverseHold(LandingGear gear)
        {
            if (!_gearHold)
                return;
            ControlInputs inputs = null;
            if (gear != null && GearInputs != null)
            {
                try { inputs = GearInputs.GetValue(gear) as ControlInputs; }
                catch { inputs = null; }
            }
            if (inputs != null)
                inputs.throttle = _gearSavedThrottle;
            _gearHold = false;
        }
    }

    [HarmonyPatch(typeof(PilotPlayerState), "PlayerThrottleAxis1Controls")]
    internal static class Patch_ThrottleAxis_Reverse
    {
        [HarmonyPostfix]
        private static void Postfix(PilotPlayerState __instance)
        {
            ReverseThrustService.ApplySignedThrottle(__instance);
        }
    }

    [HarmonyPatch(typeof(JetNozzle), "Thrust")]
    internal static class Patch_JetNozzle_ReverseThrust
    {
        [HarmonyPrefix]
        private static void Prefix(JetNozzle __instance, ref bool allowAfterburner)
        {
            Aircraft ac = ReverseThrustService.ReadNozzleAircraft(__instance);
            if (!ReverseThrustService.IsActive(ac))
                return;
            allowAfterburner = false;
        }

        [HarmonyPostfix]
        private static void Postfix(JetNozzle __instance)
        {
            Aircraft ac = ReverseThrustService.ReadNozzleAircraft(__instance);
            if (!ReverseThrustService.IsActive(ac))
                return;
            Transform xf = ReverseThrustService.ReadNozzleThrustXf(__instance);
            UnitPart part = ReverseThrustService.ReadNozzlePart(__instance);
            if (xf == null || part == null || part.rb == null)
                return;
            float along = Vector3.Dot(xf.forward, ac.transform.forward);
            if (along < 0.25f)
                return;
            float thrust = ReverseThrustService.ReadNozzleTotalThrust(__instance);
            if (thrust <= 1f)
                return;
            ReverseThrustService.CancelForwardThrust(part.rb, xf.forward, thrust);
            ReverseThrustService.NoteNozzleFlip(ac);
        }
    }

    [HarmonyPatch(typeof(ConstantSpeedProp), "FixedUpdate")]
    internal static class Patch_Prop_ReverseThrust
    {
        [HarmonyPrefix]
        private static void Prefix(ConstantSpeedProp __instance, ref float __state)
        {
            __state = -1f;
            Aircraft ac = ReverseThrustService.ReadPropAircraft(__instance);
            if (!ReverseThrustService.IsActive(ac))
                return;
            if (!ReverseThrustService.IsAirborne(ac))
                return;
            ReverseThrustService.HoldThrottleForProp(
                ReverseThrustService.InputsOf(ac), ref __state);
        }

        [HarmonyPostfix]
        private static void Postfix(ConstantSpeedProp __instance, float __state)
        {
            Aircraft ac = ReverseThrustService.ReadPropAircraft(__instance);
            ReverseThrustService.RestoreThrottle(
                ReverseThrustService.InputsOf(ac), __state);
            if (!ReverseThrustService.IsActive(ac))
                return;
            ReverseThrustService.NeutralizeGroundPropAero(__instance, ac);
        }
    }

    [HarmonyPatch(typeof(ConstantSpeedProp), "AutoPropPitch")]
    internal static class Patch_Prop_ReversePitch
    {
        [HarmonyPostfix]
        private static void Postfix(ConstantSpeedProp __instance)
        {
            Aircraft ac = ReverseThrustService.ReadPropAircraft(__instance);
            if (!ReverseThrustService.IsActive(ac))
                return;
            if (!ReverseThrustService.IsAirborne(ac))
                return;
            ReverseThrustService.DriveReversePitch(__instance);
        }
    }

    [HarmonyPatch(typeof(ConstantSpeedProp), "PropAnimate")]
    internal static class Patch_Prop_ReverseSpin
    {
        [HarmonyPostfix]
        private static void Postfix(ConstantSpeedProp __instance)
        {
            Aircraft ac = ReverseThrustService.ReadPropAircraft(__instance);
            if (!ReverseThrustService.IsActive(ac))
                return;
            ReverseThrustService.ReversePropSpin(__instance);
        }
    }

    [HarmonyPatch(typeof(PropFan), "FixedUpdate")]
    internal static class Patch_PropFan_ReverseThrust
    {
        [HarmonyPrefix]
        private static void Prefix(PropFan __instance, ref float __state)
        {
            __state = -1f;
            Aircraft ac = ReverseThrustService.ReadPropFanAircraft(__instance);
            if (!ReverseThrustService.IsActive(ac))
                return;
            if (!ReverseThrustService.IsAirborne(ac))
                return;
            ReverseThrustService.HoldThrottleForProp(
                ReverseThrustService.InputsOf(ac), ref __state);
        }

        [HarmonyPostfix]
        private static void Postfix(PropFan __instance, float __state)
        {
            Aircraft ac = ReverseThrustService.ReadPropFanAircraft(__instance);
            ReverseThrustService.RestoreThrottle(
                ReverseThrustService.InputsOf(ac), __state);
            if (!ReverseThrustService.IsActive(ac))
                return;
            if (!ReverseThrustService.IsAirborne(ac))
            {
                ReverseThrustService.NeutralizeGroundPropFan(__instance, ac);
                return;
            }
        }
    }

    [HarmonyPatch(typeof(PropFan), "Update")]
    internal static class Patch_PropFan_VisualReverse
    {
        [HarmonyPrefix]
        private static void Prefix(PropFan __instance)
        {
            Aircraft ac = ReverseThrustService.ReadPropFanAircraft(__instance);
            ReverseThrustService.SetVisualReverse(ReverseThrustService.IsActive(ac));
        }

        [HarmonyPostfix]
        private static void Postfix()
        {
            ReverseThrustService.SetVisualReverse(false);
        }
    }

    [HarmonyPatch]
    internal static class Patch_PropFanProp_AnimateReverse
    {
        private static MethodBase TargetMethod()
        {
            Type t = AccessTools.Inner(typeof(PropFan), "Prop");
            if (t == null)
                return null;
            return AccessTools.Method(t, "Animate", new Type[] { typeof(float), typeof(float) });
        }

        [HarmonyPrefix]
        private static void Prefix(ref float rpm)
        {
            if (ReverseThrustService.VisualReverse)
                rpm = -rpm;
        }
    }

    [HarmonyPatch(typeof(DuctedFan), "Update")]
    internal static class Patch_DuctedFan_ReverseSpin
    {
        [HarmonyPostfix]
        private static void Postfix(DuctedFan __instance)
        {
            Aircraft ac = ReverseThrustService.ReadDuctedAircraft(__instance);
            if (!ReverseThrustService.IsActive(ac))
                return;
            ReverseThrustService.ReverseDuctedSpin(__instance);
        }
    }

    [HarmonyPatch(typeof(RotorShaft), "AnimateRotor")]
    internal static class Patch_Rotor_ReverseSpin
    {
        [HarmonyPostfix]
        private static void Postfix(RotorShaft __instance)
        {
            Aircraft ac = __instance != null ? __instance.aircraft : null;
            if (!ReverseThrustService.IsActive(ac))
                return;
            ReverseThrustService.ReverseRotorSpin(__instance);
        }
    }

    [HarmonyPatch(typeof(RotorShaft), "RotorPhysics")]
    internal static class Patch_Rotor_ReverseHub
    {
        [HarmonyPostfix]
        private static void Postfix(RotorShaft __instance)
        {
            Aircraft ac = __instance != null ? __instance.aircraft : null;
            if (!ReverseThrustService.IsActive(ac))
                return;
            ReverseThrustService.ReverseRotorSpin(__instance);
        }
    }

    [HarmonyPatch(typeof(Aircraft), "FixedUpdate")]
    internal static class Patch_Aircraft_ReverseThrustAir
    {
        [HarmonyPostfix]
        private static void Postfix(Aircraft __instance)
        {
            ReverseThrustService.ApplyAirframeReverse(__instance);
        }
    }

    [HarmonyPatch(typeof(LandingGear), "FixedUpdate")]
    internal static class Patch_LandingGear_UnlockOnReverse
    {
        [HarmonyPrefix]
        private static void Prefix(LandingGear __instance)
        {
            ReverseThrustService.BeginGearReverseHold(__instance);
        }

        [HarmonyPostfix]
        private static void Postfix(LandingGear __instance)
        {
            ReverseThrustService.EndGearReverseHold(__instance);
        }
    }

    [HarmonyPatch(typeof(Airbrake), "Update")]
    internal static class Patch_Airbrake_CloseOnReverse
    {
        private static readonly FieldInfo BrakeInputs =
            AccessTools.Field(typeof(Airbrake), "controlInputs");

        [HarmonyPrefix]
        private static void Prefix(Airbrake __instance)
        {
            ControlInputs inputs = null;
            if (BrakeInputs != null)
            {
                try { inputs = BrakeInputs.GetValue(__instance) as ControlInputs; }
                catch { inputs = null; }
            }
            ReverseThrustService.BeginAirbrakeHold(inputs);
        }

        [HarmonyPostfix]
        private static void Postfix(Airbrake __instance)
        {
            ControlInputs inputs = null;
            if (BrakeInputs != null)
            {
                try { inputs = BrakeInputs.GetValue(__instance) as ControlInputs; }
                catch { inputs = null; }
            }
            ReverseThrustService.EndAirbrakeHold(inputs);
        }
    }

    [HarmonyPatch(typeof(ThrottleGauge), "Refresh")]
    internal static class Patch_ThrottleGauge_Signed
    {
        private static readonly FieldInfo Reading =
            AccessTools.Field(typeof(ThrottleGauge), "throttleReading");
        private static readonly FieldInfo ReadingPivot =
            AccessTools.Field(typeof(ThrottleGauge), "throttleReadingPivot");
        private static readonly FieldInfo Bar =
            AccessTools.Field(typeof(ThrottleGauge), "throttleBar");
        private static readonly FieldInfo Pointer =
            AccessTools.Field(typeof(ThrottleGauge), "throttlePointer");
        private static readonly FieldInfo ThrottlePrev =
            AccessTools.Field(typeof(ThrottleGauge), "throttlePrev");
        private static readonly FieldInfo GaugeAircraft =
            AccessTools.Field(typeof(ThrottleGauge), "aircraft");
        private static readonly FieldInfo GaugeAirbrake =
            AccessTools.Field(typeof(ThrottleGauge), "airbrake");

        [HarmonyPrefix]
        private static bool Prefix(ThrottleGauge __instance)
        {
            if (!ReverseThrustService.IsOn() || __instance == null)
                return true;
            Aircraft ac = null;
            if (GaugeAircraft != null)
            {
                try { ac = GaugeAircraft.GetValue(__instance) as Aircraft; }
                catch { ac = null; }
            }
            if (ac == null || !ReverseThrustService.AllowsReverse(ac))
                return true;
            float s = ReverseThrustService.SignedThrottle();
            if (s >= 0f)
                return true;
            DrawSigned(__instance, s);
            if (ThrottlePrev != null)
            {
                try { ThrottlePrev.SetValue(__instance, -1f); }
                catch { }
            }
            return false;
        }

        private static void DrawSigned(ThrottleGauge gauge, float signed)
        {
            // 0–100% of the reverse band (not raw signed travel, which used to top out at 20%).
            float fill = ReverseThrustService.ReverseAmount();
            if (fill < 0f)
                fill = 0f;
            if (fill > 1f)
                fill = 1f;

            if (Bar != null)
            {
                try
                {
                    Image bar = Bar.GetValue(gauge) as Image;
                    if (bar != null)
                        bar.fillAmount = fill;
                }
                catch { }
            }

            float z = fill * 26f - 13f;
            SetLocalEuler(ReadingPivot, gauge, z);
            SetLocalEuler(Pointer, gauge, z);

            object reading = null;
            if (Reading != null)
            {
                try { reading = Reading.GetValue(gauge); }
                catch { reading = null; }
            }

            bool hasBrake = ReverseThrustService.CurrentHasAirbrake();
            if (!hasBrake && GaugeAirbrake != null)
            {
                try
                {
                    object flag = GaugeAirbrake.GetValue(gauge);
                    if (flag is bool && (bool)flag)
                        hasBrake = true;
                }
                catch { }
            }

            bool reverse = !hasBrake || signed <= -ReverseThrustService.AirbrakeBand;
            if (hasBrake && !reverse)
                SetReadingText(reading, "AIRBRAKE");
            else
            {
                string pct = Mathf.RoundToInt(fill * 100f) + "%";
                string label = UiLang.T("Reverse Thrust", "反推");
                if (hasBrake && ReverseThrustService.ReverseAirbrakeOn())
                    label = label + "+" + UiLang.T("Airbrake", "减速板");
                SetReadingText(reading, pct + "  " + label);
            }

            KeepReadingUpright(gauge, AsTransform(reading));
        }

        private static void SetLocalEuler(FieldInfo field, ThrottleGauge gauge, float z)
        {
            if (field == null)
                return;
            try
            {
                object v = field.GetValue(gauge);
                Transform xf = v as Transform;
                if (xf == null)
                {
                    Component c = v as Component;
                    if (c != null)
                        xf = c.transform;
                }
                if (xf != null)
                    xf.localEulerAngles = new Vector3(0f, 0f, z);
            }
            catch { }
        }

        private static Transform AsTransform(object obj)
        {
            if (obj == null)
                return null;
            Transform xf = obj as Transform;
            if (xf != null)
                return xf;
            Component c = obj as Component;
            return c != null ? c.transform : null;
        }

        private static void KeepReadingUpright(ThrottleGauge gauge, Transform readingXf)
        {
            if (readingXf == null)
                return;
            try
            {
                Aircraft ac = GaugeAircraft != null
                    ? GaugeAircraft.GetValue(gauge) as Aircraft
                    : null;
                if (ac == null || ac.cockpit == null
                    || SceneSingleton<CameraStateManager>.i == null
                    || SceneSingleton<CameraStateManager>.i.mainCamera == null)
                    return;
                float z = SceneSingleton<CameraStateManager>.i.mainCamera.transform.eulerAngles.z;
                float z2 = ac.cockpit.transform.eulerAngles.z;
                readingXf.eulerAngles = new Vector3(0f, 0f, 0f - (z - z2));
            }
            catch { }
        }

        private static void SetReadingText(object reading, string value)
        {
            if (reading == null)
                return;
            Text ui = reading as Text;
            if (ui != null)
            {
                ui.text = value;
                return;
            }
            try
            {
                PropertyInfo p = reading.GetType().GetProperty("text");
                if (p != null)
                    p.SetValue(reading, value, null);
            }
            catch { }
        }
    }
}
