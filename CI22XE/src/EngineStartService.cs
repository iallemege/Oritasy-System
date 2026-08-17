using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// QOL: two-step start. First press starts the APU (taxi at 2% APU thrust).
    /// Second press cranks the engine: ground magneto (7% fail) or air APU
    /// restart (70% fail, 3s). APU stays running after engine start and supplies
    /// 2% backup thrust when the engines have no power. Air shutdown auto-enables the APU.
    /// Master switch lives on Career Profile.
    /// </summary>
    internal static class EngineStartService
    {
        private const float MagnetoFailChance = 0.07f;
        private const float ApuFailChance = 0.70f;
        private const float ApuThrustFrac = 0.02f;
        private const float ApuWait = 1f;
        private const float MagnetoWait = 1f;
        private const float EngineWait = 1f;
        private const float FailWait = 3f;
        private const float ApuFailWait = 3f;
        private const float ApuSuccessWait = 2.5f;
        private const float BlinkHz = 2.2f;
        private const float ThrottleRelease = 0.45f;

        private enum Phase
        {
            None = 0,
            Apu = 1,
            Magneto = 2,
            Engine = 3,
            MagnetoFail = 4,
            Success = 5,
            ApuFail = 6
        }

        private static readonly FieldInfo SimThrottleField =
            AccessTools.Field(typeof(PilotPlayerState), "simulatedThrottle");
        private static readonly HashSet<int> Completed = new HashSet<int>();
        private static ConfigEntry<bool> Enabled;
        private static bool _passThrough;
        private static Phase _phase;
        private static bool _magnetoFailed;
        private static float _nextAt;
        private static int _seqAircraftId;
        private static bool _holdUntilThrottle;
        private static int _holdAircraftId;
        private static bool _apuOn;
        private static int _apuAircraftId;
        private static float _apuSuccessUntil;
        private static float _cachedMaxThrust;
        private static GUIStyle _promptStyle;

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Flight", "QOL", true,
                "Cold-start QOL: APU then magneto/engine. Toggle in Career Profile.");
        }

        internal static bool IsOn()
        {
            return Enabled == null || Enabled.Value;
        }

        internal static bool IsStarting
        {
            get { return IsOn() && _phase != Phase.None; }
        }

        internal static bool HoldsMovement
        {
            get { return IsOn() && _holdUntilThrottle; }
        }

        internal static bool ApuActive
        {
            get { return IsOn() && _apuOn; }
        }

        internal static bool ApuRunning
        {
            get { return IsOn() && (_apuOn || _phase == Phase.Apu); }
        }

        internal static void DrawProfileToggle()
        {
            GUILayout.Label(UiLang.T("QOL", "QOL"), GUILayout.ExpandWidth(true));
            GUILayout.BeginHorizontal();
            GUILayout.Label(UiLang.T("Engine start", "引擎启动"), GUILayout.Width(140f));
            Color prev = GUI.backgroundColor;
            bool on = IsOn();
            GUI.backgroundColor = on ? new Color(0.45f, 0.85f, 0.55f) : Color.white;
            if (GUILayout.Button(on ? UiLang.T("ON", "开") : UiLang.T("OFF", "关"),
                GUILayout.Width(90f), GUILayout.Height(26f)))
            {
                SetEnabled(!on);
                on = !on;
            }
            GUI.backgroundColor = prev;
            GUILayout.Label(on ? UiLang.T("  [ON]", "  [开]") : UiLang.T("  [OFF]", "  [关]"),
                GUILayout.Width(56f));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label(
                on
                    ? UiLang.T(
                        "ON: APU then magneto/engine. APU stays on as 2% backup when engines have no power.",
                        "开：先APU再磁电机/引擎。APU保持运转，引擎没动力时提供2%备份推力。")
                    : UiLang.T(
                        "OFF: vanilla ignition. Engine is on at spawn; no APU sequence.",
                        "关：原版点火。出生即开车，无APU流程。"),
                GUILayout.ExpandWidth(true));
        }

        internal static void SetEnabled(bool on)
        {
            if (Enabled != null)
                Enabled.Value = on;
            if (!on)
                ReleaseToVanilla();
            else
                MarkRunningEngineComplete();
        }

        internal static void Tick()
        {
            if (!IsOn())
                return;
            Aircraft ac = LocalXe();
            if (ac == null)
            {
                _holdUntilThrottle = false;
                _holdAircraftId = 0;
                DisableApu();
                ClearSequence();
                return;
            }

            int id = ac.GetInstanceID();
            if (_phase != Phase.None && id != _seqAircraftId)
                ClearSequence();
            if (_apuOn && id != _apuAircraftId)
                DisableApu();

            if (!Completed.Contains(id) && !IsStarting)
            {
                if (ShouldSpawnHot(ac))
                    CompleteHot(ac);
                else
                    ForceEngineOff(ac);
            }

            SyncApu(ac);
            if (_phase != Phase.None)
                AdvanceSequence(ac);
            if (_holdUntilThrottle)
                TickThrottleHold(ac);
        }

        internal static void Draw()
        {
            if (!IsOn())
                return;
            string text = null;
            if (ShouldFlashStartApu())
                text = UiLang.T("Please start the APU", "请启动APU");
            else if (ShouldFlashStartEngine())
                text = UiLang.T(
                    "Please start the engine, thrust is from the APU",
                    "请启动引擎，现在推力由APU提供");
            else if (_holdUntilThrottle)
                text = UiLang.T("Advance the throttle to move", "请推节流阀");
            if (text == null)
                return;
            if (Event.current != null && Event.current.type != EventType.Repaint)
                return;
            float pulse = Mathf.Abs(Mathf.Sin(Time.unscaledTime * BlinkHz * 3.1415926f));
            if (pulse < 0.22f)
                return;
            EnsurePromptStyle();
            float w = 720f;
            float h = 36f;
            float x = (UiScaleService.Width - w) * 0.5f;
            float y = UiScaleService.Height * 0.16f;
            Rect r = new Rect(x, y, w, h);
            Color prev = GUI.color;
            float a = 0.35f + 0.65f * pulse;
            GUI.color = new Color(0.08f, 0.05f, 0.02f, 0.55f * a);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.82f, 0.2f, a);
            GUI.Label(r, text, _promptStyle);
            GUI.color = prev;
        }

        private static bool EngineIsOff()
        {
            Aircraft ac = LocalXe();
            if (ac == null)
                return false;
            try
            {
                if (ac.Ignition)
                    return false;
            }
            catch
            {
                return false;
            }
            return true;
        }

        private static bool ShouldFlashStartApu()
        {
            if (IsStarting || _apuOn)
                return false;
            return EngineIsOff();
        }

        private static bool ShouldFlashStartEngine()
        {
            if (IsStarting || !_apuOn)
                return false;
            if (Time.unscaledTime < _apuSuccessUntil)
                return false;
            return EngineIsOff();
        }

        private static void EnsurePromptStyle()
        {
            if (_promptStyle != null)
                return;
            _promptStyle = new GUIStyle(GUI.skin.label);
            _promptStyle.alignment = TextAnchor.MiddleCenter;
            _promptStyle.fontSize = 22;
            _promptStyle.fontStyle = FontStyle.Bold;
            _promptStyle.normal.textColor = Color.white;
            _promptStyle.wordWrap = false;
        }

        internal static bool BlockToggle(Aircraft ac)
        {
            if (!IsOn())
                return false;
            if (ac == null || _passThrough)
                return false;
            if (!AppliesTo(ac))
                return false;
            if (!IsLocalPlayerAircraft(ac))
                return false;
            if (ac.Ignition)
                return false;
            if (_phase != Phase.None)
                return true;
            if (_apuOn && !AirframeWearService.ApuCanStartEngine())
            {
                DenyEngineStart(ac);
                return true;
            }
            BeginSequence(ac);
            return true;
        }

        internal static void OnPlayerAircraftReady(Aircraft ac)
        {
            if (!IsOn())
                return;
            if (!AppliesTo(ac) || !IsPlayerAircraft(ac))
                return;
            if (Completed.Contains(ac.GetInstanceID()))
                return;
            if (ShouldSpawnHot(ac))
            {
                CompleteHot(ac);
                return;
            }
            ForceEngineOff(ac);
        }

        private static bool ShouldSpawnHot(Aircraft ac)
        {
            if (ac == null || !IsOnGround(ac))
                return false;
            return AirbaseLocator.IsAircraftOnCarrier(ac);
        }

        private static void CompleteHot(Aircraft ac)
        {
            if (ac == null)
                return;
            Completed.Add(ac.GetInstanceID());
            DisableApu();
            ClearSequence();
            _holdUntilThrottle = false;
            _holdAircraftId = 0;
            RestoreIgnition(ac);
        }

        private static void BeginSequence(Aircraft ac)
        {
            if (ac == null)
                return;
            _seqAircraftId = ac.GetInstanceID();
            if (_apuOn)
            {
                BeginEngineFromApu(ac);
                return;
            }
            _phase = Phase.Apu;
            _nextAt = Time.unscaledTime + ApuWait;
            Report(ac, UiLang.T("Starting APU", "启动APU"), ApuWait + 0.4f);
        }

        private static void DenyEngineStart(Aircraft ac)
        {
            string text = UiLang.T(
                "APU damaged, cannot start engine",
                "APU损坏，无法再启动引擎");
            AirframeWearService.FlashHudMessage(text, 8);
            Report(ac, text, 3.2f);
        }

        private static void BeginEngineFromApu(Aircraft ac)
        {
            if (!AirframeWearService.ApuCanStartEngine())
            {
                DenyEngineStart(ac);
                return;
            }
            if (ReverseThrustService.IsAirborne(ac))
            {
                BeginApuRestart(ac);
                return;
            }
            _magnetoFailed = UnityEngine.Random.value < MagnetoFailChance;
            _phase = Phase.Magneto;
            _nextAt = Time.unscaledTime + MagnetoWait;
            Report(ac, UiLang.T("Starting magneto", "正在启动磁电机"), MagnetoWait + 0.4f);
        }

        private static void BeginApuRestart(Aircraft ac)
        {
            if (UnityEngine.Random.value < ApuFailChance)
            {
                _phase = Phase.ApuFail;
                _nextAt = Time.unscaledTime + ApuFailWait;
                Report(ac,
                    UiLang.T("APU failure, engine start failed", "APU故障引擎启动失败"),
                    ApuFailWait);
                return;
            }
            BeginEngineCrank(ac);
        }

        private static void AdvanceSequence(Aircraft ac)
        {
            if (ac == null || Time.unscaledTime < _nextAt)
                return;
            if (_phase == Phase.Apu)
            {
                FinishApuStart(ac);
                return;
            }
            if (_phase == Phase.ApuFail)
            {
                ClearSequence();
                return;
            }
            if (_phase == Phase.Magneto)
            {
                if (_magnetoFailed)
                {
                    _phase = Phase.MagnetoFail;
                    _nextAt = Time.unscaledTime + FailWait;
                    Report(ac, MagnetoFailText(), FailWait + 0.3f);
                    return;
                }
                BeginEngineCrank(ac);
                return;
            }
            if (_phase == Phase.MagnetoFail)
            {
                BeginEngineCrank(ac);
                return;
            }
            if (_phase == Phase.Engine)
            {
                _phase = Phase.Success;
                Report(ac, UiLang.T("Engine start successful", "成功启动引擎"), 3f);
                FinishStart(ac);
            }
        }

        private static string MagnetoFailText()
        {
            if (UnityEngine.Random.value < 0.5f)
            {
                return UiLang.T(
                    "Magneto failure, forcing backup generator",
                    "磁电机故障，强制启动备用发电机");
            }
            return UiLang.T(
                "Magneto failed, blank-cartridge force start",
                "磁电机失效，正在空包弹强制启动");
        }

        private static void BeginEngineCrank(Aircraft ac)
        {
            _phase = Phase.Engine;
            _nextAt = Time.unscaledTime + EngineWait;
            Report(ac, UiLang.T("Starting engine", "正在启动引擎"), EngineWait + 0.4f);
        }

        private static void FinishStart(Aircraft ac)
        {
            if (ac == null)
            {
                ClearSequence();
                return;
            }
            Completed.Add(ac.GetInstanceID());
            ClearSequence();
            if (IsOnGround(ac))
                BeginThrottleHold(ac);
            try
            {
                if (ac.IsServer)
                {
                    ac.NetworkIgnition = true;
                    return;
                }
                _passThrough = true;
                ac.CmdToggleIgnition();
            }
            catch
            {
                try { ac.NetworkIgnition = true; }
                catch { ac.Ignition = true; }
            }
            finally
            {
                _passThrough = false;
            }
        }

        private static void ForceEngineOff(Aircraft ac)
        {
            if (ac == null || !ac.Ignition)
                return;
            try
            {
                if (ac.IsServer)
                    ac.NetworkIgnition = false;
                else
                    ac.Ignition = false;
            }
            catch
            {
                ac.Ignition = false;
            }
        }

        private static void BeginThrottleHold(Aircraft ac)
        {
            if (ac == null)
                return;
            _holdUntilThrottle = true;
            _holdAircraftId = ac.GetInstanceID();
            SnapPlayerThrottle(ac, 0f);
        }

        private static void TickThrottleHold(Aircraft ac)
        {
            if (ac == null || ac.GetInstanceID() != _holdAircraftId)
            {
                _holdUntilThrottle = false;
                _holdAircraftId = 0;
                return;
            }
            float axis = ReverseThrustService.PlayerThrottleAxis();
            if (axis > ThrottleRelease || axis < -ThrottleRelease)
            {
                _holdUntilThrottle = false;
                _holdAircraftId = 0;
                return;
            }
            SnapPlayerThrottle(ac, 0f);
            HoldOnGround(ac);
        }

        private static void SnapPlayerThrottle(Aircraft ac, float value)
        {
            if (ac == null)
                return;
            try
            {
                Pilot[] pilots = ac.pilots;
                if (pilots != null && SimThrottleField != null)
                {
                    for (int i = 0; i < pilots.Length; i++)
                    {
                        if (pilots[i] == null || pilots[i].playerState == null)
                            continue;
                        SimThrottleField.SetValue(pilots[i].playerState, value);
                    }
                }
            }
            catch { }
            try
            {
                ControlInputs inputs = ac.GetInputs();
                if (inputs != null)
                    inputs.throttle = Mathf.Clamp01(value);
            }
            catch { }
        }

        private static void HoldOnGround(Aircraft ac)
        {
            if (ac == null || ac.rb == null)
                return;
            try
            {
                if (!ac.IsLanded())
                    return;
            }
            catch
            {
                return;
            }
            try
            {
                Vector3 v = ac.rb.velocity;
                v.x = 0f;
                v.z = 0f;
                ac.rb.velocity = v;
                ac.rb.angularVelocity = Vector3.zero;
            }
            catch { }
        }

        private static void SyncApu(Aircraft ac)
        {
            if (ac == null)
                return;
            bool ignition = false;
            try { ignition = ac.Ignition; }
            catch { return; }
            if (ignition)
            {
                CacheMaxThrust(ac);
                return;
            }
            if (IsStarting)
                return;
            if (!Completed.Contains(ac.GetInstanceID()))
                return;
            if (!ReverseThrustService.IsAirborne(ac))
                return;
            EnableApu(ac, true);
        }

        private static void FinishApuStart(Aircraft ac)
        {
            EnableApu(ac, true);
            ClearSequence();
        }

        private static void EnableApu(Aircraft ac, bool announce)
        {
            if (ac == null)
                return;
            bool first = !_apuOn;
            _apuOn = true;
            _apuAircraftId = ac.GetInstanceID();
            CacheMaxThrust(ac);
            if (!first)
                return;
            if (!announce)
                return;
            _apuSuccessUntil = Time.unscaledTime + ApuSuccessWait;
            Report(ac, UiLang.T("APU start successful", "APU启动成功"), ApuSuccessWait);
        }

        private static void DisableApu()
        {
            _apuOn = false;
            _apuAircraftId = 0;
            _apuSuccessUntil = 0f;
        }

        private static void CacheMaxThrust(Aircraft ac)
        {
            if (ac == null)
                return;
            float maxT = 0f;
            bool got = false;
            try { got = ac.GetMaxThrust(out maxT); }
            catch { got = false; }
            if (got && maxT > 50f)
                _cachedMaxThrust = maxT;
        }

        internal static void ApplyApuThrust(Aircraft ac)
        {
            if (!IsOn() || !_apuOn || ac == null || ac.rb == null)
                return;
            if (ac.GetInstanceID() != _apuAircraftId)
                return;
            if (EnginesHavePower(ac))
                return;
            if (ReverseThrustService.ReverseAmount() > 0.02f)
                return;
            if (ReverseThrustService.SignedThrottle() < 0f)
                return;
            float maxT = ResolveApuMaxThrust(ac);
            float frac = ApuThrustFrac;
            if (!ReverseThrustService.IsAirborne(ac))
            {
                float throttle = ReadThrottle01(ac);
                if (throttle < 0.02f)
                    return;
                frac *= throttle;
            }
            ac.rb.AddForce(ac.transform.forward * maxT * frac);
        }

        private static bool EnginesHavePower(Aircraft ac)
        {
            if (ac == null)
                return false;
            try
            {
                if (!ac.Ignition)
                    return false;
            }
            catch
            {
                return false;
            }
            try
            {
                List<IEngine> list = ac.engines;
                if (list == null || list.Count == 0)
                    return false;
                for (int i = 0; i < list.Count; i++)
                {
                    Component c = list[i] as Component;
                    if (c == null)
                        continue;
                    if (AirframeWearService.EngineShouldProduceThrust(c))
                        return true;
                }
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static float ResolveApuMaxThrust(Aircraft ac)
        {
            if (_cachedMaxThrust > 50f)
                return _cachedMaxThrust;
            float maxT = 0f;
            bool got = false;
            try { got = ac.GetMaxThrust(out maxT); }
            catch { got = false; }
            if (got && maxT > 50f)
            {
                _cachedMaxThrust = maxT;
                return maxT;
            }
            return ac.rb.mass * 8f;
        }

        private static float ReadThrottle01(Aircraft ac)
        {
            if (ac == null)
                return 0f;
            try
            {
                ControlInputs inputs = ac.GetInputs();
                if (inputs != null)
                    return Mathf.Clamp01(inputs.throttle);
            }
            catch { }
            return 0f;
        }

        private static bool IsOnGround(Aircraft ac)
        {
            if (ac == null)
                return true;
            try { return ac.IsLanded(); }
            catch
            {
                return !ReverseThrustService.IsAirborne(ac);
            }
        }

        private static void ReleaseToVanilla()
        {
            DisableApu();
            ClearSequence();
            _holdUntilThrottle = false;
            _holdAircraftId = 0;
            Aircraft ac = LocalXe();
            if (ac == null)
                return;
            Completed.Add(ac.GetInstanceID());
            RestoreIgnition(ac);
        }

        private static void MarkRunningEngineComplete()
        {
            Aircraft ac = LocalXe();
            if (ac == null)
                return;
            try
            {
                if (ac.Ignition)
                    Completed.Add(ac.GetInstanceID());
            }
            catch { }
        }

        private static void RestoreIgnition(Aircraft ac)
        {
            if (ac == null)
                return;
            try
            {
                if (ac.Ignition)
                    return;
            }
            catch { }
            try
            {
                if (ac.IsServer)
                {
                    ac.NetworkIgnition = true;
                    return;
                }
                _passThrough = true;
                ac.CmdToggleIgnition();
            }
            catch
            {
                try { ac.NetworkIgnition = true; }
                catch { ac.Ignition = true; }
            }
            finally
            {
                _passThrough = false;
            }
        }

        private static void ClearSequence()
        {
            _phase = Phase.None;
            _magnetoFailed = false;
            _seqAircraftId = 0;
        }

        private static bool AppliesTo(Aircraft ac)
        {
            if (ac == null || !Plugin.IsRuntimeInstance(ac))
                return false;
            return AircraftIdentity.IsXeAircraft(ac);
        }

        private static bool IsLocalPlayerAircraft(Aircraft ac)
        {
            if (ac == null)
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

        private static bool IsPlayerAircraft(Aircraft ac)
        {
            if (ac == null)
                return false;
            try
            {
                if (ac.Player != null)
                    return true;
            }
            catch { }
            return ReverseThrustService.IsPlayerFlown(ac);
        }

        private static Aircraft LocalXe()
        {
            try
            {
                Aircraft ac;
                if (!GameManager.GetLocalAircraft(out ac) || ac == null)
                    return null;
                if (!AppliesTo(ac))
                    return null;
                return ac;
            }
            catch
            {
                return null;
            }
        }

        private static void Report(Aircraft ac, string text, float seconds)
        {
            if (ac == null || string.IsNullOrEmpty(text))
                return;
            if (!IsLocalPlayerAircraft(ac))
                return;
            try
            {
                if (SceneSingleton<AircraftActionsReport>.i == null)
                    return;
                SceneSingleton<AircraftActionsReport>.i.ReportText(text, seconds);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(Aircraft), "FixedUpdate")]
    internal static class Patch_Aircraft_ApuThrust
    {
        [HarmonyPostfix]
        private static void Postfix(Aircraft __instance)
        {
            EngineStartService.ApplyApuThrust(__instance);
        }
    }

    [HarmonyPatch(typeof(Aircraft), "CmdToggleIgnition")]
    internal static class Patch_Aircraft_EngineStartToggle
    {
        [HarmonyPrefix]
        private static bool Prefix(Aircraft __instance)
        {
            return !EngineStartService.BlockToggle(__instance);
        }
    }

    [HarmonyPatch(typeof(Aircraft), "OnStartServer")]
    internal static class Patch_Aircraft_EngineStartServer
    {
        [HarmonyPostfix]
        private static void Postfix(Aircraft __instance)
        {
            EngineStartService.OnPlayerAircraftReady(__instance);
        }
    }

    [HarmonyPatch(typeof(Aircraft), "SetupLocalPlayerAndUI")]
    internal static class Patch_Aircraft_EngineStartLocalUi
    {
        [HarmonyPostfix]
        private static void Postfix(Aircraft __instance)
        {
            EngineStartService.OnPlayerAircraftReady(__instance);
        }
    }

    [HarmonyPatch(typeof(Pilot), "SwitchState")]
    internal static class Patch_Pilot_EngineStartSeat
    {
        [HarmonyPostfix]
        private static void Postfix(Pilot __instance, PilotBaseState state)
        {
            if (__instance == null || state == null)
                return;
            if (!(state is PilotPlayerState))
                return;
            EngineStartService.OnPlayerAircraftReady(__instance.aircraft);
        }
    }
}
