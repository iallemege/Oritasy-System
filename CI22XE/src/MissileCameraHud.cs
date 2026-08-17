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
    /// Left PiP chase cam, plus optional F6 fullscreen nose + MCLOS stick guidance.
    /// </summary>
    internal static class MissileCameraHud
    {
        private static readonly List<Missile> Tracked = new List<Missile>(16);
        private static readonly HashSet<int> TrackedIds = new HashSet<int>();
        private static readonly object[] RewiredAxisArgs = new object[1];
        private static readonly FieldInfo SeekerMissileField = AccessTools.Field(typeof(MissileSeeker), "missile");
        private static readonly FieldInfo SeekerField = AccessTools.Field(typeof(Missile), "seeker");
        private static readonly FieldInfo SeekerTargetField = AccessTools.Field(typeof(MissileSeeker), "targetUnit");
        private static readonly FieldInfo MissileTargetField = AccessTools.Field(typeof(Missile), "target");
        private static readonly FieldInfo GLimitField = AccessTools.Field(typeof(Missile), "gLimit");
        private static readonly FieldInfo MotorsField = AccessTools.Field(typeof(Missile), "motors");
        private static readonly FieldInfo TerrainAvoidField = AccessTools.Field(typeof(OpticalSeeker), "terrainAvoidance");
        private static readonly Type MotorType = typeof(Missile).GetNestedType("Motor", BindingFlags.NonPublic);
        private static readonly FieldInfo MotorFuelField = MotorType != null
            ? AccessTools.Field(MotorType, "fuelMass") : null;
        private static RenderTexture _rt;
        private static Camera _cam;
        private static GameObject _camGo;
        /// <summary>F6 top-right: launching aircraft forward view.</summary>
        private static RenderTexture _acRt;
        private static Camera _acCam;
        private static GameObject _acCamGo;
        private static Missile _follow;
        private static bool _overlayOn = true;
        private static bool _manualPilot;
        private static bool _savedFlightControls = true;
        private static bool _hasSavedFlightControls;
        private static bool _steerActive;
        private static float _savedGLimit = -1f;
        private static float _manualThrottle = 1f;
        private static float _fuelMax;
        private static Vector3 _gVelPrev;
        private static float _gVelPrevTime;
        private static float _displayG;
        private static Quaternion _cmdRot = Quaternion.identity;
        private static float _nextScan;
        private static float _nextCamSync;
        private static Camera _cachedMainCam;
        private static int _cachedMainMask = int.MinValue;
        private static float _nextArmPulse;
        private static bool _savedTerrainAvoid;
        private static bool _hasSavedTerrainAvoid;
        private static GUIStyle _labelStyle;
        private static GUIStyle _manualStyle;
        private static GUIStyle _barLabelStyle;
        private static MethodInfo _rewiredGetAxis;
        private static FieldInfo _playerInputField;
        private static readonly Color FuelColor = new Color(0.25f, 0.85f, 1f, 0.95f);
        private static readonly Color ThrottleColor = new Color(0.95f, 0.85f, 0.2f, 0.95f);
        private static readonly Color GColor = new Color(0.15f, 1f, 0.35f, 0.95f);
        private static readonly Color GWarnColor = new Color(1f, 0.75f, 0.15f, 0.95f);
        private static readonly Color GDangerColor = new Color(1f, 0.28f, 0.22f, 0.95f);

        internal static bool ManualActive
        {
            get { return _manualPilot; }
        }

        internal static Missile FollowMissile
        {
            get { return _follow; }
        }

        internal static bool IsManualPiloting(Missile missile)
        {
            return _manualPilot && missile != null && object.ReferenceEquals(missile, _follow)
                && IsMissileAlive(missile);
        }

        internal static bool IsManualPilotingSeeker(MissileSeeker seeker)
        {
            if (!_manualPilot || seeker == null || SeekerMissileField == null)
                return false;
            try
            {
                return IsManualPiloting(SeekerMissileField.GetValue(seeker) as Missile);
            }
            catch
            {
                return false;
            }
        }

        internal static void NotifySpawn(Missile missile)
        {
            if (missile == null || !IsPlayerOwned(missile))
                return;
            int id = missile.GetInstanceID();
            if (!TrackedIds.Add(id))
                return;
            Tracked.Add(missile);
            _follow = missile;
            ResetTelemetry(missile);
            if (_manualPilot)
                BeginManualOn(_follow);
        }

        /// <summary>AGM-T bus discarded — follow first live GS25 and force PiP on.</summary>
        internal static void HandoffCluster(Missile[] children)
        {
            if (children == null || children.Length == 0)
                return;

            if (_manualPilot)
                ExitManual();

            PruneTracked();
            Missile pick = null;
            for (int i = 0; i < children.Length; i++)
            {
                Missile m = children[i];
                if (m == null || !IsMissileAlive(m) || !IsPlayerOwned(m))
                    continue;
                int id = m.GetInstanceID();
                if (TrackedIds.Add(id))
                    Tracked.Add(m);
                if (pick == null)
                    pick = m;
            }

            if (pick == null)
                return;

            _follow = pick;
            _overlayOn = true;
            ResetTelemetry(pick);
            EnsureCamera(false);
            SetCamActive(true);
            UpdateChaseCamera(pick);
        }

        internal static void Tick()
        {
            if (Plugin.ManualMissileKey != null && Input.GetKeyDown(Plugin.ManualMissileKey.Value))
                ToggleManual();

            if (_manualPilot && Input.GetKeyDown(KeyCode.Escape))
                ExitManual();

            if (Plugin.MissileCameraKey != null && Input.GetKeyDown(Plugin.MissileCameraKey.Value)
                && !_manualPilot)
                _overlayOn = !_overlayOn;

            if (Plugin.MissileCameraCycleKey != null
                && Plugin.MissileCameraCycleKey.Value != KeyCode.None
                && Input.GetKeyDown(Plugin.MissileCameraCycleKey.Value))
            {
                CycleNext();
                if (_manualPilot && _follow != null)
                    BeginManualOn(_follow);
            }

            PruneTracked();

            bool followAlive = _follow != null && IsMissileAlive(_follow);
            if (MissileCameraLifecycleService.ShouldExitManual(_manualPilot, followAlive))
            {
                ExitManual();
                return;
            }

            bool featureOn = Plugin.MissileCamera != null && Plugin.MissileCamera.Value;
            MissileCameraLifecycleService.Phase phase = MissileCameraLifecycleService.ResolvePhase(
                _manualPilot, followAlive, featureOn, _overlayOn);

            if (phase == MissileCameraLifecycleService.Phase.ManualFullscreen)
            {
                // Keep aircraft stick/buttons dead — some UI paths re-enable this flag.
                GameManager.flightControlsEnabled = false;
                EnsureCamera(true);
                UpdateNoseCamera(_follow);
                SetCamActive(true);
                EnsureAircraftCamera();
                UpdateAircraftForwardCamera(_follow);
                UpdateManualAim(_follow, Time.unscaledDeltaTime);
                UpdateManualThrottle(_follow, Time.unscaledDeltaTime);
                UpdateTelemetry(_follow);
                // Only override seeker aim while WASD is held.
                // Idle → vanilla Seek keeps cruise terrain-follow / arming / fuse logic.
                if (_steerActive)
                    WriteManualAimpoint(_follow);
                return;
            }

            SetAircraftCamActive(false);

            if (phase == MissileCameraLifecycleService.Phase.Hidden)
            {
                SetCamActive(false);
                return;
            }

            // PipChase — NotifySpawn covers new shots; rare scan is only a fallback
            if (MissileCameraLifecycleService.ShouldRunFallbackScan(Time.unscaledTime, _nextScan))
            {
                _nextScan = MissileCameraLifecycleService.ScheduleNextScan(Time.unscaledTime);
                ScanForMissiles();
            }

            if (_follow == null || !IsMissileAlive(_follow))
                PickFollow();

            if (_follow == null || !IsMissileAlive(_follow))
            {
                SetCamActive(false);
                return;
            }

            EnsureCamera(false);
            UpdateChaseCamera(_follow);
            SetCamActive(true);
            UpdateTelemetry(_follow);
        }

        internal static void FixedTick()
        {
            bool followAlive = _follow != null && IsMissileAlive(_follow);
            if (!MissileCameraLifecycleService.FixedTickApplies(_manualPilot, followAlive))
                return;
            EnsureArmedAndTangible(_follow);
            ApplyManualThrottle(_follow);
            if (_steerActive)
                WriteManualAimpoint(_follow);
        }

        /// <summary>Called from Harmony just before Missile.Steering so Seek/WeXon cannot overwrite.</summary>
        internal static void ApplyGuidanceForSteering(Missile missile)
        {
            if (!IsManualPiloting(missile))
                return;
            SuppressSeekerTerrainAvoid(missile);
            // Re-stamp throttle after Seek (cruise seekers rewrite it each tick).
            ApplyManualThrottle(missile);
            if (_steerActive)
                WriteManualAimpoint(missile);
        }

        internal static void Draw()
        {
            if (_manualPilot)
            {
                DrawManualFullscreen();
                return;
            }

            if (Plugin.MissileCamera == null || !Plugin.MissileCamera.Value || !_overlayOn)
                return;
            if (_rt == null || _follow == null || !IsMissileAlive(_follow))
                return;
            if (Event.current != null && Event.current.type != EventType.Repaint)
                return;

            EnsureStyles();
            float frac = Plugin.MissileCameraWidth != null ? Plugin.MissileCameraWidth.Value : 0.28f;
            MissileTelemetryService.PipLayout layout = MissileTelemetryService.ComputePipLayout(
                UiScaleService.Width, UiScaleService.Height, frac);
            Rect frame = layout.Frame;
            Rect view = layout.View;
            float x = view.x;
            float y = view.y;
            float w = view.width;
            float h = view.height;

            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.65f);
            GUI.DrawTexture(frame, Texture2D.whiteTexture);
            GUI.color = new Color(0.15f, 1f, 0.35f, 0.9f);
            GUI.DrawTexture(new Rect(frame.x, frame.y, frame.width, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(frame.x, frame.yMax - 2f, frame.width, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(frame.x, frame.y, 2f, frame.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(frame.xMax - 2f, frame.y, 2f, frame.height), Texture2D.whiteTexture);
            GUI.color = prev;

            GUI.DrawTexture(view, _rt, ScaleMode.StretchToFill, false);

            // Fuel (left) / G (right) inside the PiP frame
            DrawSideBars(view, 10f, 0.88f);

            string name = MissileDisplayName(_follow);
            string title = "MISSILE CAM  ·  " + name + "  ·  "
                + GameUnitDisplayService.Speed(MissileTelemetryService.SpeedMps(_follow));
            GUI.Label(new Rect(x + 4f, y - 20f, w - 8f, 18f), title, _labelStyle);

            GUI.Label(new Rect(x + 4f, y + h + 2f, w - 8f, 16f), FormatTargetHudLine(_follow), _labelStyle);

            string hint = Tracked.Count > 1
                ? ("[" + (IndexOfFollow() + 1) + "/" + Tracked.Count + "] Del hide · F6 pilot")
                : "Del hide · F6 pilot";
            GUI.Label(new Rect(x + 4f, y + h + 18f, w - 8f, 16f), hint, _labelStyle);
        }

        internal static void Shutdown()
        {
            ExitManual();
            SetCamActive(false);
            SetAircraftCamActive(false);
            DestroyCamera(ref _camGo, ref _cam, ref _rt);
            DestroyCamera(ref _acCamGo, ref _acCam, ref _acRt);
            Tracked.Clear();
            _follow = null;
        }

        private static void DestroyCamera(ref GameObject go, ref Camera cam, ref RenderTexture rt)
        {
            if (go != null)
            {
                UnityEngine.Object.Destroy(go);
                go = null;
                cam = null;
            }
            if (rt != null)
            {
                rt.Release();
                UnityEngine.Object.Destroy(rt);
                rt = null;
            }
        }

        private static void ToggleManual()
        {
            if (Plugin.ManualMissile != null && !Plugin.ManualMissile.Value)
                return;

            if (_manualPilot)
            {
                ExitManual();
                return;
            }

            PruneTracked();
            if (Tracked.Count == 0 || _follow == null || !IsMissileAlive(_follow))
            {
                _nextScan = 0f;
                ScanForMissiles();
            }
            if (_follow == null || !IsMissileAlive(_follow))
                PickFollow();
            bool allowed = Plugin.ManualMissile == null || Plugin.ManualMissile.Value;
            if (!MissileCameraLifecycleService.CanEnterManual(
                    allowed, _follow != null && IsMissileAlive(_follow)))
                return;

            BeginManualOn(_follow);
        }

        private static void BeginManualOn(Missile missile)
        {
            if (missile == null || !IsMissileAlive(missile))
                return;

            _follow = missile;
            _manualPilot = true;
            _overlayOn = true;
            try { _cmdRot = missile.transform.rotation; }
            catch { _cmdRot = Quaternion.identity; }

            if (!_hasSavedFlightControls)
            {
                _savedFlightControls = GameManager.flightControlsEnabled;
                _hasSavedFlightControls = true;
            }
            GameManager.flightControlsEnabled = false;

            try
            {
                missile.DeployFins();
                missile.Arm();
                missile.SetTangible(true);
            }
            catch { }

            _steerActive = false;
            _manualThrottle = 1f;
            _nextArmPulse = 0f;
            _hasSavedTerrainAvoid = false;
            ResetTelemetry(missile);
            EnsureArmedAndTangible(missile);
            ApplyManualThrottle(missile);
            SuppressSeekerTerrainAvoid(missile);

            if (GLimitField != null)
            {
                try
                {
                    _savedGLimit = (float)GLimitField.GetValue(missile);
                    // 0 = unlimited in vanilla; leave alone. Low caps feel soggy for MCLOS.
                    if (_savedGLimit > 0f && _savedGLimit < 35f)
                        GLimitField.SetValue(missile, 40f);
                }
                catch { _savedGLimit = -1f; }
            }

            EnsureCamera(true);
            UpdateNoseCamera(missile);
            SetCamActive(true);

            if (Plugin.Log != null)
                Plugin.Log.LogInfo("Manual missile pilot ON: " + MissileDisplayName(missile));
        }

        private static void ExitManual()
        {
            if (!_manualPilot && !_hasSavedFlightControls)
                return;

            Missile m = _follow;
            _manualPilot = false;

            if (m != null && GLimitField != null && _savedGLimit >= 0f)
            {
                try { GLimitField.SetValue(m, _savedGLimit); }
                catch { }
            }
            _savedGLimit = -1f;
            RestoreSeekerTerrainAvoid(m);

            if (_hasSavedFlightControls)
            {
                GameManager.flightControlsEnabled = _savedFlightControls;
                _hasSavedFlightControls = false;
            }

            SetAircraftCamActive(false);

            if (Plugin.Log != null && m != null)
                Plugin.Log.LogInfo("Manual missile pilot OFF");
        }

        private static OpticalSeeker FindOpticalSeeker(Missile missile)
        {
            if (missile == null)
                return null;
            try
            {
                OpticalSeeker opt = missile.GetComponent<OpticalSeeker>();
                if (opt == null)
                    opt = missile.GetComponentInChildren<OpticalSeeker>(true);
                return opt;
            }
            catch
            {
                return null;
            }
        }

        private static void SuppressSeekerTerrainAvoid(Missile missile)
        {
            if (TerrainAvoidField == null)
                return;
            OpticalSeeker opt = FindOpticalSeeker(missile);
            if (opt == null)
                return;
            try
            {
                if (!_hasSavedTerrainAvoid)
                {
                    object v = TerrainAvoidField.GetValue(opt);
                    _savedTerrainAvoid = v is bool && (bool)v;
                    _hasSavedTerrainAvoid = true;
                }
                TerrainAvoidField.SetValue(opt, false);
            }
            catch { }
        }

        private static void RestoreSeekerTerrainAvoid(Missile missile)
        {
            if (!_hasSavedTerrainAvoid)
                return;
            _hasSavedTerrainAvoid = false;
            if (TerrainAvoidField == null || missile == null)
                return;
            OpticalSeeker opt = FindOpticalSeeker(missile);
            if (opt == null)
                return;
            try { TerrainAvoidField.SetValue(opt, _savedTerrainAvoid); }
            catch { }
        }

        private static void UpdateManualAim(Missile missile, float dt)
        {
            if (missile == null || dt <= 0f)
                return;

            // Keep cmd frame aligned when not steering so the next key input feels local
            if (!_steerActive)
            {
                try { _cmdRot = missile.transform.rotation; }
                catch { }
            }

            float turn = Plugin.ManualMissileTurnRate != null
                ? MissileTelemetryService.ClampTurnRate(Plugin.ManualMissileTurnRate.Value)
                : 55f;

            // WASD: W nose up, S nose down, A yaw left, D yaw right
            float pitch = 0f;
            float yaw = 0f;
            if (Input.GetKey(KeyCode.W))
                pitch += 1f;
            if (Input.GetKey(KeyCode.S))
                pitch -= 1f;
            if (Input.GetKey(KeyCode.A))
                yaw -= 1f;
            if (Input.GetKey(KeyCode.D))
                yaw += 1f;

            _steerActive = Mathf.Abs(pitch) > 0.01f || Mathf.Abs(yaw) > 0.01f;
            if (!_steerActive)
                return;

            Quaternion body = Quaternion.identity;
            try { body = missile.transform.rotation; }
            catch { }
            _cmdRot = MissileTelemetryService.ApplyManualSteer(_cmdRot, body, pitch, yaw, turn, dt);
        }

        private static void UpdateManualThrottle(Missile missile, float dt)
        {
            if (missile == null || dt <= 0f)
                return;

            float rate = Plugin.ManualMissileThrottleRate != null
                ? MissileTelemetryService.ClampThrottleRate(Plugin.ManualMissileThrottleRate.Value)
                : 0.7f;

            float delta = 0f;
            // Q cut / E open; Shift/Ctrl as fast adjust
            if (Input.GetKey(KeyCode.E) || Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
                delta += 1f;
            if (Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                delta -= 1f;

            if (Mathf.Abs(delta) < 0.01f)
            {
                ApplyManualThrottle(missile);
                return;
            }

            float mul = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)
                || Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
                ? 1.75f : 1f;
            _manualThrottle = MissileTelemetryService.StepThrottle(_manualThrottle, delta, rate, mul, dt);
            ApplyManualThrottle(missile);
        }

        private static void ApplyManualThrottle(Missile missile)
        {
            if (missile == null)
                return;
            try { missile.SetThrottle(Mathf.Clamp01(_manualThrottle)); }
            catch { }
        }

        private static void WriteManualAimpoint(Missile missile)
        {
            if (missile == null)
                return;

            Vector3 dir = _cmdRot * Vector3.forward;
            if (dir.sqrMagnitude < 0.01f)
            {
                try { dir = missile.transform.forward; }
                catch { dir = Vector3.forward; }
            }

            float speed = 200f;
            try
            {
                if (missile.rb != null)
                    speed = Mathf.Max(missile.rb.velocity.magnitude, 80f);
                else
                    speed = Mathf.Max(missile.speed, 80f);
            }
            catch { }

            // Keep aim close enough that DetectCollisions proximity/impact paths stay sane
            float lookAhead = MissileTelemetryService.AimLookAheadM(speed);
            try
            {
                Vector3 origin = missile.transform.position;
                Vector3 aimLocal = origin + dir * lookAhead;
                // No terrain floor — player must be able to dive into ground / a target.
                missile.SetAimpoint(aimLocal.ToGlobalPosition(), Vector3.zero);
            }
            catch
            {
                try
                {
                    GlobalPosition origin = missile.GlobalPosition();
                    missile.SetAimpoint(origin + dir * lookAhead, Vector3.zero);
                }
                catch { }
            }
        }

        private static void EnsureArmedAndTangible(Missile missile)
        {
            if (missile == null)
                return;
            if (Time.unscaledTime < _nextArmPulse)
                return;
            _nextArmPulse = Time.unscaledTime + 0.35f;
            try
            {
                if (!missile.IsTangible() && missile.timeSinceSpawn > 0.35f)
                    missile.SetTangible(true);
                if (!missile.IsArmed() && missile.timeSinceSpawn > 0.5f)
                    missile.Arm();
            }
            catch { }
        }

        private static float GetFlightAxis(string axis)
        {
            try
            {
                if (_playerInputField == null)
                    _playerInputField = typeof(GameManager).GetField("playerInput",
                        BindingFlags.Public | BindingFlags.Static);
                object pi = _playerInputField != null ? _playerInputField.GetValue(null) : null;
                if (pi != null)
                {
                    if (_rewiredGetAxis == null)
                        _rewiredGetAxis = pi.GetType().GetMethod("GetAxis", new Type[] { typeof(string) });
                    if (_rewiredGetAxis != null)
                    {
                        RewiredAxisArgs[0] = axis;
                        object v = _rewiredGetAxis.Invoke(pi, RewiredAxisArgs);
                        if (v is float)
                            return (float)v;
                    }
                }
            }
            catch { }

            // Fallback: Unity axes (often unbound in this game)
            if (axis == "Pitch")
                return -Input.GetAxis("Mouse Y") * 0.35f - Input.GetAxis("Vertical") * 0.5f;
            if (axis == "Yaw")
                return Input.GetAxis("Mouse X") * 0.35f + Input.GetAxis("Horizontal") * 0.5f;
            return 0f;
        }

        private static void DrawManualFullscreen()
        {
            if (_rt == null || _follow == null || !IsMissileAlive(_follow))
                return;
            if (Event.current != null && Event.current.type != EventType.Repaint
                && Event.current.type != EventType.Layout)
            {
                // still draw on Repaint only for textures
            }
            if (Event.current != null && Event.current.type != EventType.Repaint)
                return;

            EnsureStyles();
            GUI.DrawTexture(new Rect(0f, 0f, UiScaleService.Width, UiScaleService.Height), _rt, ScaleMode.StretchToFill, false);

            // Crosshair
            Color prev = GUI.color;
            float cx = UiScaleService.Width * 0.5f;
            float cy = UiScaleService.Height * 0.5f;
            GUI.color = new Color(0.15f, 1f, 0.35f, 0.85f);
            GUI.DrawTexture(new Rect(cx - 14f, cy - 1f, 28f, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 1f, cy - 14f, 2f, 28f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 22f, cy - 22f, 8f, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx + 14f, cy - 22f, 8f, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 22f, cy + 20f, 8f, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx + 14f, cy + 20f, 8f, 2f), Texture2D.whiteTexture);
            GUI.color = prev;

            string name = MissileDisplayName(_follow);
            string key = Plugin.ManualMissileKey != null ? Plugin.ManualMissileKey.Value.ToString() : "Insert";
            string cycle = (Plugin.MissileCameraCycleKey != null
                && Plugin.MissileCameraCycleKey.Value != KeyCode.None)
                ? Plugin.MissileCameraCycleKey.Value.ToString() : string.Empty;
            int thrPct = Mathf.RoundToInt(_manualThrottle * 100f);
            string title = "MANUAL  ·  " + name + "  ·  "
                + GameUnitDisplayService.Speed(MissileTelemetryService.SpeedMps(_follow))
                + "  ·  THR " + thrPct + "%";
            string hint = key + "/Esc exit · WASD steer · Q/E throttle";
            if (cycle.Length > 0)
                hint = hint + " · " + cycle + " next";
            if (Tracked.Count > 1)
                hint = "[" + (IndexOfFollow() + 1) + "/" + Tracked.Count + "]  " + hint;

            GUI.Label(new Rect(16f, 12f, UiScaleService.Width - 32f, 22f), title, _manualStyle);
            GUI.Label(new Rect(16f, UiScaleService.Height - 52f, UiScaleService.Width - 32f, 20f), FormatTargetHudLine(_follow), _manualStyle);
            GUI.Label(new Rect(16f, UiScaleService.Height - 34f, UiScaleService.Width - 32f, 20f), hint, _labelStyle);

            // Left: FUEL + THR · Right: G (keep clear of aircraft PiP top-right)
            float sidePad = Mathf.Max(24f, UiScaleService.Width * 0.02f);
            float sideH = UiScaleService.Height * 0.42f;
            float sideY = (UiScaleService.Height - sideH) * 0.5f;
            float sideW = 18f;
            float gap = 10f;
            DrawFuelBar(new Rect(sidePad, sideY, sideW, sideH));
            DrawThrottleBar(new Rect(sidePad + sideW + gap, sideY, sideW, sideH));
            DrawGBar(new Rect(UiScaleService.Width - sidePad - sideW, sideY, sideW, sideH));

            DrawAircraftForwardPip();
            GUI.color = prev;
        }

        private static void DrawAircraftForwardPip()
        {
            if (_acRt == null || _acCamGo == null || _acCam == null || !_acCam.enabled)
                return;

            float frac = 0.22f;
            float pad = Mathf.Max(14f, UiScaleService.Width * 0.012f);
            float w = UiScaleService.Width * frac;
            float h = w * 9f / 16f;
            float x = UiScaleService.Width - pad - w;
            float y = pad + 36f;
            Rect frame = new Rect(x - 3f, y - 20f, w + 6f, h + 26f);
            Rect view = new Rect(x, y, w, h);

            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(frame, Texture2D.whiteTexture);
            GUI.color = new Color(0.35f, 0.85f, 1f, 0.95f);
            GUI.DrawTexture(new Rect(frame.x, frame.y, frame.width, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(frame.x, frame.yMax - 2f, frame.width, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(frame.x, frame.y, 2f, frame.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(frame.xMax - 2f, frame.y, 2f, frame.height), Texture2D.whiteTexture);
            GUI.color = prev;

            GUI.DrawTexture(view, _acRt, ScaleMode.StretchToFill, false);
            GUI.Label(new Rect(x + 4f, y - 18f, w - 8f, 16f), "AIRCRAFT FWD", _labelStyle);
        }

        private static void ResetTelemetry(Missile missile)
        {
            _fuelMax = 0f;
            _displayG = 0f;
            _gVelPrevTime = 0f;
            _gVelPrev = Vector3.zero;
            if (missile != null)
            {
                float fuel = ReadFuelMass(missile);
                _fuelMax = Mathf.Max(fuel, 0.001f);
            }
        }

        private static void UpdateTelemetry(Missile missile)
        {
            if (missile == null)
                return;

            // Capture peak remaining fuel as "full" when first seen / after stage change
            float fuel = ReadFuelMass(missile);
            if (fuel > _fuelMax)
                _fuelMax = fuel;
            if (_fuelMax <= 0f && fuel <= 0f)
                _fuelMax = 0.001f;

            _displayG = MissileTelemetryService.UpdateSignedG(
                missile, _displayG, ref _gVelPrev, ref _gVelPrevTime);
        }

        private static float ReadFuelMass(Missile missile)
        {
            if (missile == null || MotorsField == null || MotorFuelField == null)
                return 0f;
            try
            {
                Array motors = MotorsField.GetValue(missile) as Array;
                if (motors == null || motors.Length == 0)
                    return 0f;
                float sum = 0f;
                for (int i = 0; i < motors.Length; i++)
                {
                    object m = motors.GetValue(i);
                    if (m == null)
                        continue;
                    sum += (float)MotorFuelField.GetValue(m);
                }
                return Mathf.Max(0f, sum);
            }
            catch
            {
                return 0f;
            }
        }

        private static float FuelFraction()
        {
            if (_follow == null)
                return 0f;
            return MissileTelemetryService.FuelFraction(ReadFuelMass(_follow), _fuelMax);
        }

        private static void DrawSideBars(Rect view, float barW, float heightFrac)
        {
            float h = view.height * heightFrac;
            float y = view.y + (view.height - h) * 0.5f;
            float inset = Mathf.Max(6f, view.width * 0.02f);
            float gap = 6f;
            DrawFuelBar(new Rect(view.x + inset, y, barW, h));
            DrawThrottleBar(new Rect(view.x + inset + barW + gap, y, barW, h));
            DrawGBar(new Rect(view.xMax - inset - barW, y, barW, h));
        }

        private static void DrawFuelBar(Rect r)
        {
            EnsureStyles();
            float frac = FuelFraction();
            Color prev = GUI.color;

            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), Texture2D.whiteTexture);

            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);

            float fillH = r.height * frac;
            GUI.color = frac < 0.2f ? GDangerColor : (frac < 0.4f ? GWarnColor : FuelColor);
            GUI.DrawTexture(new Rect(r.x, r.yMax - fillH, r.width, fillH), Texture2D.whiteTexture);

            GUI.color = FuelColor;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.yMax - 2f, r.width, 2f), Texture2D.whiteTexture);
            GUI.color = prev;

            GUI.Label(new Rect(r.x - 4f, r.y - 18f, r.width + 40f, 16f), "FUEL", _barLabelStyle);
            GUI.Label(new Rect(r.x - 8f, r.yMax + 2f, r.width + 48f, 16f),
                Mathf.RoundToInt(frac * 100f) + "%", _barLabelStyle);
        }

        private static void DrawThrottleBar(Rect r)
        {
            EnsureStyles();
            float frac = Mathf.Clamp01(_manualThrottle);
            Color prev = GUI.color;

            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), Texture2D.whiteTexture);

            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);

            float fillH = r.height * frac;
            GUI.color = ThrottleColor;
            GUI.DrawTexture(new Rect(r.x, r.yMax - fillH, r.width, fillH), Texture2D.whiteTexture);

            GUI.color = ThrottleColor;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.yMax - 2f, r.width, 2f), Texture2D.whiteTexture);
            GUI.color = prev;

            GUI.Label(new Rect(r.x - 4f, r.y - 18f, r.width + 40f, 16f), "THR", _barLabelStyle);
            GUI.Label(new Rect(r.x - 8f, r.yMax + 2f, r.width + 48f, 16f),
                Mathf.RoundToInt(frac * 100f) + "%", _barLabelStyle);
        }

        private static void DrawGBar(Rect r)
        {
            EnsureStyles();
            // Scale: -5 .. +20 G mapped to bar (0.5 = 1G baseline)
            float g = _displayG;
            float t = Mathf.InverseLerp(-5f, 20f, g);
            float yNeedle = r.yMax - t * r.height;
            float yOne = r.yMax - Mathf.InverseLerp(-5f, 20f, 1f) * r.height;

            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);

            // Fill from 1G toward current
            float fillTop = Mathf.Min(yOne, yNeedle);
            float fillH = Mathf.Max(2f, Mathf.Abs(yNeedle - yOne));
            Color gc = Mathf.Abs(g) >= 15f ? GDangerColor : (Mathf.Abs(g) >= 9f ? GWarnColor : GColor);
            GUI.color = gc;
            GUI.DrawTexture(new Rect(r.x, fillTop, r.width, fillH), Texture2D.whiteTexture);

            // 1G reference
            GUI.color = new Color(1f, 1f, 1f, 0.65f);
            GUI.DrawTexture(new Rect(r.x - 3f, yOne, r.width + 6f, 1f), Texture2D.whiteTexture);
            // Needle
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(r.x - 3f, yNeedle - 1f, r.width + 6f, 3f), Texture2D.whiteTexture);
            GUI.color = prev;

            string gTxt = (g >= 0f ? "+" : string.Empty) + g.ToString("0.0");
            GUI.Label(new Rect(r.x - 20f, r.y - 18f, r.width + 48f, 16f), "G", _barLabelStyle);
            GUI.Label(new Rect(r.x - 28f, r.yMax + 2f, r.width + 56f, 16f), gTxt, _barLabelStyle);
        }

        private static void EnsureStyles()
        {
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label);
                _labelStyle.fontSize = 12;
                _labelStyle.fontStyle = FontStyle.Bold;
                _labelStyle.normal.textColor = new Color(0.2f, 1f, 0.4f, 0.95f);
                _labelStyle.alignment = TextAnchor.MiddleLeft;
            }
            if (_barLabelStyle == null)
            {
                _barLabelStyle = new GUIStyle(GUI.skin.label);
                _barLabelStyle.fontSize = 11;
                _barLabelStyle.fontStyle = FontStyle.Bold;
                _barLabelStyle.normal.textColor = new Color(0.75f, 0.95f, 0.85f, 0.9f);
                _barLabelStyle.alignment = TextAnchor.MiddleCenter;
            }
            if (_manualStyle == null)
            {
                _manualStyle = new GUIStyle(GUI.skin.label);
                _manualStyle.fontSize = 16;
                _manualStyle.fontStyle = FontStyle.Bold;
                _manualStyle.normal.textColor = new Color(0.2f, 1f, 0.4f, 0.95f);
                _manualStyle.alignment = TextAnchor.MiddleLeft;
            }
        }

        private static void EnsureCamera(bool manual)
        {
            int tw;
            int th;
            PerfMode.MissileCamRtSize(manual, out tw, out th);
            if (_rt == null || _rt.width != tw || _rt.height != th)
            {
                if (_rt != null)
                {
                    _rt.Release();
                    UnityEngine.Object.Destroy(_rt);
                }
                _rt = new RenderTexture(tw, th, 16, RenderTextureFormat.ARGB32);
                _rt.name = "OritasyMissileCamRT";
                _rt.Create();
                if (_cam != null)
                    _cam.targetTexture = _rt;
            }

            if (_camGo != null && _cam != null)
            {
                _cam.fieldOfView = manual ? 65f : 70f;
                SyncSecondaryCameraFromMain(_cam);
                return;
            }

            _camGo = new GameObject("OritasyMissileCam");
            UnityEngine.Object.DontDestroyOnLoad(_camGo);
            _camGo.hideFlags = HideFlags.HideAndDontSave;
            _cam = _camGo.AddComponent<Camera>();
            _cam.enabled = false;
            _cam.targetTexture = _rt;
            _cam.clearFlags = CameraClearFlags.Skybox;
            _cam.fieldOfView = manual ? 65f : 70f;
            _cam.nearClipPlane = 0.08f;
            _cam.farClipPlane = 80000f;
            _cam.depth = -20f;
            SyncSecondaryCameraFromMain(_cam);
        }

        private static void EnsureAircraftCamera()
        {
            int tw;
            int th;
            PerfMode.AircraftCamRtSize(out tw, out th);
            if (_acRt == null || _acRt.width != tw || _acRt.height != th)
            {
                if (_acRt != null)
                {
                    _acRt.Release();
                    UnityEngine.Object.Destroy(_acRt);
                }
                _acRt = new RenderTexture(tw, th, 16, RenderTextureFormat.ARGB32);
                _acRt.name = "OritasyAircraftFwdRT";
                _acRt.Create();
                if (_acCam != null)
                    _acCam.targetTexture = _acRt;
            }

            if (_acCamGo != null && _acCam != null)
            {
                SyncSecondaryCameraFromMain(_acCam);
                return;
            }

            _acCamGo = new GameObject("OritasyAircraftFwdCam");
            UnityEngine.Object.DontDestroyOnLoad(_acCamGo);
            _acCamGo.hideFlags = HideFlags.HideAndDontSave;
            _acCam = _acCamGo.AddComponent<Camera>();
            _acCam.enabled = false;
            _acCam.targetTexture = _acRt;
            _acCam.clearFlags = CameraClearFlags.Skybox;
            _acCam.fieldOfView = 60f;
            _acCam.nearClipPlane = 0.15f;
            _acCam.farClipPlane = 80000f;
            _acCam.depth = -21f;
            SyncSecondaryCameraFromMain(_acCam);
        }

        /// <summary>
        /// Match main camera layers so particles (missile flame) and terrain materials render in RT views.
        /// Throttled — Camera.allCameras every frame was a major hitch with F3/F6 PiP.
        /// </summary>
        private static void SyncSecondaryCameraFromMain(Camera cam)
        {
            if (cam == null)
                return;
            try
            {
                float now = Time.unscaledTime;
                Camera main = _cachedMainCam;
                if (main == null || !main.isActiveAndEnabled || now >= _nextCamSync)
                {
                    _nextCamSync = now + 0.5f;
                    main = Camera.main;
                    if (main == null)
                    {
                        Camera[] cams = Camera.allCameras;
                        if (cams != null)
                        {
                            for (int i = 0; i < cams.Length; i++)
                            {
                                if (cams[i] != null && cams[i].enabled && cams[i].targetTexture == null
                                    && !object.ReferenceEquals(cams[i], _cam)
                                    && !object.ReferenceEquals(cams[i], _acCam))
                                {
                                    main = cams[i];
                                    break;
                                }
                            }
                        }
                    }
                    _cachedMainCam = main;
                }
                if (main == null)
                    return;

                int mask = main.cullingMask;
                if (mask == _cachedMainMask && cam.cullingMask == mask)
                {
                    // Mask unchanged — still refresh far clip occasionally via timer above
                    if (cam.farClipPlane >= main.farClipPlane)
                        return;
                }
                _cachedMainMask = mask;

                cam.cullingMask = mask;
                cam.clearFlags = main.clearFlags;
                cam.backgroundColor = main.backgroundColor;
                cam.farClipPlane = Mathf.Max(cam.farClipPlane, main.farClipPlane);
                try { cam.renderingPath = main.renderingPath; }
                catch { }
                try { cam.allowHDR = main.allowHDR; }
                catch { }
                try { cam.allowMSAA = false; }
                catch { }
            }
            catch { }
        }

        private static void SetCamActive(bool on)
        {
            if (_cam != null)
                _cam.enabled = on;
        }

        private static void SetAircraftCamActive(bool on)
        {
            if (_acCam != null)
                _acCam.enabled = on;
        }

        private static void UpdateChaseCamera(Missile missile)
        {
            if (_camGo == null || missile == null)
                return;
            Rigidbody rb = null;
            try { rb = missile.rb; }
            catch { }
            Vector3 pos;
            Quaternion rot;
            MissileCameraChaseMathService.ChasePose(missile.transform, rb, out pos, out rot);
            _camGo.transform.position = pos;
            _camGo.transform.rotation = rot;
            SyncSecondaryCameraFromMain(_cam);
        }

        private static void UpdateNoseCamera(Missile missile)
        {
            if (_camGo == null || missile == null)
                return;
            Vector3 pos;
            Quaternion rot;
            MissileCameraChaseMathService.NosePose(missile.transform, out pos, out rot);
            _camGo.transform.position = pos;
            _camGo.transform.rotation = rot;
            SyncSecondaryCameraFromMain(_cam);
        }

        private static void UpdateAircraftForwardCamera(Missile missile)
        {
            Aircraft ac = ResolveOwnerAircraft(missile);
            if (ac == null || _acCamGo == null)
            {
                SetAircraftCamActive(false);
                return;
            }

            Transform t = null;
            try
            {
                if (ac.cockpitViewPoint != null)
                    t = ac.cockpitViewPoint;
            }
            catch { }
            if (t == null)
                t = ac.transform;

            // Slightly ahead of cockpit so canopy/nose mesh does not fill the PiP
            _acCamGo.transform.position = t.position + t.forward * 0.35f + t.up * 0.05f;
            _acCamGo.transform.rotation = t.rotation;
            SyncSecondaryCameraFromMain(_acCam);
            SetAircraftCamActive(true);
        }

        private static Aircraft ResolveOwnerAircraft(Missile missile)
        {
            if (missile == null)
                return null;
            try
            {
                Unit owner = missile.owner;
                Aircraft ac = owner as Aircraft;
                if (ac == null && owner != null)
                    ac = owner.GetComponentInParent<Aircraft>();
                if (ac != null)
                    return ac;
            }
            catch { }
            try
            {
                Aircraft local;
                if (GameManager.GetLocalAircraft(out local))
                    return local;
            }
            catch { }
            return null;
        }

        private static string MissileDisplayName(Missile m)
        {
            if (m == null || m.name == null)
                return "Missile";
            return m.name.Replace("(Clone)", string.Empty).Trim();
        }

        /// <summary>Bottom HUD: tracked target name, or 手动操作 while F6 manual pilot.</summary>
        private static string FormatTargetHudLine(Missile missile)
        {
            if (_manualPilot)
                return "TGT  手动操作";

            Unit tgt = ResolveTrackedTarget(missile);
            if (tgt == null)
                return "TGT  —";

            string n = UnitDisplayName(tgt);
            if (string.IsNullOrEmpty(n))
                n = "?";
            return "TGT  " + n;
        }

        private static Unit ResolveTrackedTarget(Missile missile)
        {
            if (missile == null)
                return null;

            try
            {
                if (SeekerField != null && SeekerTargetField != null)
                {
                    MissileSeeker seeker = SeekerField.GetValue(missile) as MissileSeeker;
                    if (seeker != null)
                    {
                        Unit st = SeekerTargetField.GetValue(seeker) as Unit;
                        if (st != null)
                            return st;
                    }
                }
            }
            catch { }

            try
            {
                if (MissileTargetField != null)
                {
                    Unit mt = MissileTargetField.GetValue(missile) as Unit;
                    if (mt != null)
                        return mt;
                }
            }
            catch { }

            try
            {
                PersistentID tid = missile.targetID;
                if (tid.IsValid)
                {
                    Unit byId;
                    if (UnitRegistry.TryGetUnit(tid, out byId) && byId != null)
                        return byId;
                }
            }
            catch { }

            return null;
        }

        private static string UnitDisplayName(Unit u)
        {
            if (u == null)
                return null;
            try
            {
                string n = u.NetworkunitName;
                if (!string.IsNullOrEmpty(n))
                    return n;
            }
            catch { }
            try
            {
                if (!string.IsNullOrEmpty(u.unitName))
                    return u.unitName;
            }
            catch { }
            try
            {
                if (u.name != null)
                    return u.name.Replace("(Clone)", string.Empty).Trim();
            }
            catch { }
            return null;
        }

        private static float SpeedKmh(Missile m)
        {
            return MissileTelemetryService.SpeedKmh(m);
        }

        private static void PruneTracked()
        {
            for (int i = Tracked.Count - 1; i >= 0; i--)
            {
                Missile m = Tracked[i];
                bool alive = IsMissileAlive(m);
                bool owned = IsPlayerOwned(m);
                if (!MissileCameraLifecycleService.ShouldKeepTracked(alive, owned))
                {
                    if (m != null)
                        TrackedIds.Remove(m.GetInstanceID());
                    Tracked.RemoveAt(i);
                }
            }
            if (_follow != null && (!IsMissileAlive(_follow) || !IsPlayerOwned(_follow)))
                _follow = null;
        }

        private static void ScanForMissiles()
        {
            try
            {
                // Fallback only — prefer NotifySpawn path (FindObjectsOfType allocates large arrays).
                Missile[] all = UnityEngine.Object.FindObjectsOfType<Missile>();
                for (int i = 0; i < all.Length; i++)
                {
                    Missile m = all[i];
                    if (!IsMissileAlive(m) || !IsPlayerOwned(m))
                        continue;
                    int id = m.GetInstanceID();
                    if (TrackedIds.Contains(id))
                        continue;
                    TrackedIds.Add(id);
                    Tracked.Add(m);
                }
            }
            catch { }
        }

        private static void PickFollow()
        {
            _follow = null;
            int idx = MissileCameraLifecycleService.PickNewestLiveIndex(
                Tracked.Count, i => IsMissileAlive(Tracked[i]));
            if (idx < 0)
                return;
            _follow = Tracked[idx];
            ResetTelemetry(_follow);
        }

        private static void CycleNext()
        {
            PruneTracked();
            if (Tracked.Count == 0)
                return;
            int idx = IndexOfFollow();
            int next = MissileCameraLifecycleService.NextLiveIndex(
                idx, Tracked.Count, i => IsMissileAlive(Tracked[i]));
            if (next < 0)
                return;
            _follow = Tracked[next];
            _overlayOn = true;
            ResetTelemetry(_follow);
        }

        private static int IndexOfFollow()
        {
            for (int i = 0; i < Tracked.Count; i++)
            {
                if (object.ReferenceEquals(Tracked[i], _follow))
                    return i;
            }
            return -1;
        }

        private static bool IsMissileAlive(Missile m)
        {
            if (m == null)
                return false;
            try
            {
                if (m.disabled || m.Networkdisabled)
                    return false;
            }
            catch { return false; }
            return m.gameObject != null && m.gameObject.activeInHierarchy;
        }

        /// <summary>
        /// Local-player ownership via PersistentID (missile.ownerID), so MP missile cams
        /// never latch onto another client's ACM / GS25 feed.
        /// </summary>
        private static bool IsPlayerOwned(Missile missile)
        {
            if (missile == null)
                return false;

            PersistentID localId;
            if (TryGetLocalPersistentId(out localId) && localId.Id != 0u)
            {
                try
                {
                    PersistentID oid = missile.ownerID;
                    if (oid.Id == 0u)
                        oid = missile.NetworkownerID;
                    if (oid.Id != 0u && oid.Id == localId.Id)
                        return true;
                }
                catch { }

                try
                {
                    Unit owner = missile.owner;
                    if (owner != null)
                    {
                        PersistentID opid = owner.persistentID;
                        if (opid.Id == 0u)
                            opid = owner.NetworkpersistentID;
                        if (opid.Id != 0u && opid.Id == localId.Id)
                            return true;
                    }
                }
                catch { }
            }

            // Fallback when PersistentID not yet synced (rare single-player / first frame).
            try
            {
                Unit owner = missile.owner;
                Aircraft ac = owner as Aircraft;
                if (ac == null && owner != null)
                    ac = owner.GetComponentInParent<Aircraft>();
                if (ac != null)
                {
                    if (ac.Player != null && Plugin.IsLocalHumanPlayer(ac.Player))
                        return true;
                    Aircraft localAc;
                    if (GameManager.GetLocalAircraft(out localAc) && localAc != null
                        && object.ReferenceEquals(localAc, ac))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static bool TryGetLocalPersistentId(out PersistentID id)
        {
            id = PersistentID.None;
            try
            {
                Aircraft ac;
                if (GameManager.GetLocalAircraft(out ac) && ac != null)
                {
                    id = ac.persistentID;
                    if (id.Id == 0u)
                        id = ac.NetworkpersistentID;
                    if (id.Id != 0u)
                        return true;
                }
            }
            catch { }
            try
            {
                Player local;
                if (GameManager.GetLocalPlayer(out local) && local != null
                    && local.UnitID.HasValue)
                {
                    id = local.UnitID.Value;
                    if (id.Id != 0u)
                        return true;
                }
            }
            catch { }
            return false;
        }
    }
}
