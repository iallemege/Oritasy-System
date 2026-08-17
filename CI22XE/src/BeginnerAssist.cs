using System;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// F3 beginner assist: auto takeoff, crash guardian, terrain avoidance.
    /// Landing is F2 (LAND BASE / LAND CV). Tip stack slot 2 (F1 F2 F3 F9 F10 F11).
    /// Terrain pull-up toggle is <see cref="_terrainOn"/> / config BeginnerAssist.TerrainAvoidance
    /// (same field as the F3 "Terrain avoidance" row). Optional auto-off on gear transitions.
    /// </summary>
    internal static partial class BeginnerAssist
    {
        internal enum TakeoffPhase
        {
            Idle = 0,
            Taxi = 1,
            Takeoff = 2
        }

        internal enum GuardianKind
        {
            None = 0,
            InvertDive = 1,
            PostStall = 2,
            Spin = 3,
            Terrain = 4
        }

        internal struct AirframeTune
        {
            public float InvertAgl;
            public float StallAoa;
            public float SpinYawRate;
            public float PostStallAgl;
            public float SpinAgl;
            public float SinkTrigger;
            public bool StovlNozzle;
            public string Key;
        }

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<KeyCode> _menuKey;
        internal static ConfigEntry<float> _takeoffAgl;
        private static ConfigEntry<bool> _guardianDefault;
        private static ConfigEntry<bool> _terrainDefault;
        private static ConfigEntry<bool> _autoDisableTerrainOnGear;
        private static ConfigEntry<float> _guardianCooldown;
        private static ConfigEntry<float> _guardianHandback;
        private static ConfigEntry<float> _invertGroundAgl;

        private const string F3TipPrefKey = "Oritasy.FirstMissionF3Tip.v1";

        private static bool _menuOpen;
        private static bool _guardianOn = true;
        private static bool _terrainOn = true;

        /// <summary>F1 / config: auto-clear TerrainAvoidance on gear up / gear-down-locked.</summary>
        internal static ConfigEntry<bool> AutoDisableTerrainOnGear
        {
            get { return _autoDisableTerrainOnGear; }
        }

        /// <summary>Runtime Terrain pull-up (F3 row / BeginnerAssist.TerrainAvoidance).</summary>
        internal static bool TerrainAvoidanceOn
        {
            get { return _terrainOn; }
        }
        private static bool _f3TipOpen;
        private static bool _f3TipArmed;
        private static float _f3TipShowAt = -1f;
        private static bool _hadLocalAircraft;
        internal static TakeoffPhase _takeoff = TakeoffPhase.Idle;
        internal static Airbase _takeoffBase;
        internal static string _takeoffDetail = "";
        private static float _nextGuardianAt;
        /// <summary>When > 0, auto-disengage AP at this time (guardian / terrain pull-up).</summary>
        private static float _guardianHandbackAt;
        private static bool _guardianHoldActive;
        private static GuardianKind _guardianKind = GuardianKind.None;
        /// <summary>True if F2 AP was already on before guardian stole Straight hold.</summary>
        private static bool _guardianHadUserAp;
        private static int _guardianSavedMode;
        /// <summary>Saved airframe / FBW limits while guardian boost is active.</summary>
        internal static bool _boostCaptured;
        internal static float _savedG;
        internal static float _savedPitchVel;
        internal static float _savedRollVel;
        internal static float _savedAlpha;
        internal static float _savedPilotG;
        internal static bool _hasSavedPilotG;
        private static string _flash = string.Empty;
        private static float _flashUntil;
        private static bool _cursorHeld;
        private static GUIStyle _chipStyle;
        private static GUIStyle _titleStyle;
        private static GUIStyle _labelStyle;
        private static GUIStyle _btnStyle;
        private static GUIStyle _flashStyle;

        internal static bool MenuOpen
        {
            get { return _menuOpen; }
        }

        internal static bool MissionTipActive
        {
            get { return _f3TipOpen; }
        }

        internal static bool BlocksControls
        {
            get { return _takeoff != TakeoffPhase.Idle; }
        }

        /// <summary>
        /// Guardian high-alt spin/stall recovery — engines must keep producing thrust
        /// despite vanilla minDensity kill / altitudeThrust collapse.
        /// </summary>
        internal static bool ForceHighAltThrust(Aircraft ac)
        {
            if (!_guardianHoldActive || ac == null)
                return false;
            if (_guardianKind != GuardianKind.Spin && _guardianKind != GuardianKind.PostStall)
                return false;
            try
            {
                return ac.radarAlt >= 2000f;
            }
            catch { return false; }
        }

        internal static void CloseMenuFromOutside()
        {
            CloseMenu();
        }

        internal static void Bind(ConfigFile config)
        {
            if (config == null)
                return;
            _enabled = config.Bind("BeginnerAssist", "Enabled", true,
                "F3 beginner assist menu (takeoff / guardian / terrain). Use F2 for LAND.");
            _menuKey = config.Bind("BeginnerAssist", "MenuKey", KeyCode.F3,
                "Open / close beginner assist menu.");
            _takeoffAgl = config.Bind("BeginnerAssist", "TakeoffTargetAgl", 500f,
                "Auto-takeoff climb target AGL (m).");
            _guardianDefault = config.Bind("BeginnerAssist", "CrashGuardian", true,
                "Auto-engage AP when about to crash.");
            _terrainDefault = config.Bind("BeginnerAssist", "TerrainAvoidance", true,
                "Terrain closure pull-up / AP engage (F3 Terrain avoidance).");
            _autoDisableTerrainOnGear = config.Bind("BeginnerAssist", "AutoDisableTerrainOnGear", true,
                "Auto turn OFF TerrainAvoidance before gear retract and after gear locked down. Yields during F2 LAND AP.");
            _guardianCooldown = config.Bind("BeginnerAssist", "GuardianCooldown", 2.5f,
                "Seconds after handback before guardian may re-engage (lower = snappier).");
            _guardianHandback = config.Bind("BeginnerAssist", "GuardianHandbackSeconds", 2f,
                "Max seconds of AP recovery before returning control (early exit when recovered).");
            _invertGroundAgl = config.Bind("BeginnerAssist", "InvertedGroundAgl", 190f,
                "Base AGL (m) for inverted-dive guardian. Per-aircraft multipliers apply on top. High safe inverted is ignored.");
            // Migrate previous default 160 → 190
            if (_invertGroundAgl != null && Mathf.Abs(_invertGroundAgl.Value - 160f) < 0.5f)
                _invertGroundAgl.Value = 190f;
            // Migrate slow legacy cooldown / handback defaults
            if (_guardianCooldown != null && _guardianCooldown.Value >= 11.5f)
                _guardianCooldown.Value = 2.5f;
            if (_guardianHandback != null && Mathf.Abs(_guardianHandback.Value - 3f) < 0.05f)
                _guardianHandback.Value = 2f;
            _guardianOn = _guardianDefault != null && _guardianDefault.Value;
            _terrainOn = _terrainDefault != null && _terrainDefault.Value;
        }

        internal static void Tick()
        {
            // First-mission F3 tip can show even if BeginnerAssist config is off.
            TickFirstMissionF3Tip();

            if (_f3TipOpen)
            {
                if (Input.GetKeyDown(KeyCode.Escape)
                    || Input.GetKeyDown(_menuKey != null ? _menuKey.Value : KeyCode.F3))
                    CloseF3Tip();
                return;
            }

            if (_enabled == null || !_enabled.Value)
                return;

            Aircraft ac = ResolveLocal();
            if (ac == null)
            {
                if (_takeoff != TakeoffPhase.Idle)
                    ClearTakeoff();
                EndGuardianHold(null, false);
                _guardianKind = GuardianKind.None;
                if (_menuOpen)
                    CloseMenu();
                return;
            }

            if (MissileCameraHud.ManualActive)
            {
                if (_takeoff != TakeoffPhase.Idle)
                    ClearTakeoff();
                EndGuardianHold(null, false);
                _guardianKind = GuardianKind.None;
                if (_menuOpen)
                    CloseMenu();
                return;
            }

            KeyCode menu = _menuKey != null ? _menuKey.Value : KeyCode.F3;
            if (Input.GetKeyDown(menu))
            {
                if (_menuOpen)
                    CloseMenu();
                else
                    OpenMenu();
            }
            if (_menuOpen && Input.GetKeyDown(KeyCode.Escape))
                CloseMenu();
        }

        /// <summary>
        /// Physics-rate guardian / terrain checks (same cadence as AI FixedUpdateState).
        /// </summary>
        internal static void FixedTick()
        {
            if (_enabled == null || !_enabled.Value)
                return;
            if (_f3TipOpen || MissileCameraHud.ManualActive)
                return;

            Aircraft ac = ResolveLocal();
            if (ac == null)
                return;
            if (_takeoff != TakeoffPhase.Idle)
                return;
            if (AerialResupply.IsActive)
                return;

            // F2 LAND / 五边 owns the approach — never fight it with STRAIGHT pull-ups.
            // 26C live: LAND CV ↔ STRAIGHT flips mid-pattern while axis1 spiked to 1.
            if (PlayerAutopilot.IsLandingMode)
            {
                if (_guardianHoldActive || _boostCaptured)
                    YieldToAutopilotLand();
                return;
            }

            // Hand back as soon as recovered (checked every physics step).
            if (_guardianHoldActive)
            {
                if (IsGuardianRecovered(ac))
                    EndGuardianHold(ac, true);
                else if (_guardianHandbackAt > 0f && Time.unscaledTime >= _guardianHandbackAt)
                    EndGuardianHold(ac, true);
            }

            // High-alt spin / falling-leaf still runs while AP is engaged.
            if (PlayerAutopilot.IsEngaged)
                EvaluateSafety(ac, true);
            else
                EvaluateSafety(ac, false);
        }

        /// <summary>
        /// Release Crash Guardian without rewriting F2 mode — LAND BASE / LAND CV takes over.
        /// </summary>
        internal static void YieldToAutopilotLand()
        {
            if (!_guardianHoldActive && !_boostCaptured)
                return;
            Aircraft ac = null;
            try { ac = ResolveLocal(); }
            catch { }
            _guardianHoldActive = false;
            _guardianHandbackAt = -1f;
            _guardianKind = GuardianKind.None;
            _guardianHadUserAp = false;
            _nextGuardianAt = Time.unscaledTime + 2f;
            RestoreGuardianBaselines(ac);
            Flash(UiLang.T("GUARDIAN  →  LAND AP", "守护  →  着陆自动驾驶"));
        }

        /// <summary>
        /// Called from PlayerAutopilot.ApplyAfterPlayer.
        /// Returns true if beginner assist owns flight this tick (takeoff / guardian recovery).
        /// </summary>
        internal static bool ApplyFlight(Aircraft ac)
        {
            if (ac == null || ac.autopilot == null)
                return false;

            // LAND CV pattern must keep flying — guardian must not own the tick or force axis1.
            if (PlayerAutopilot.IsLandingMode)
            {
                if (_guardianHoldActive || _boostCaptured)
                    YieldToAutopilotLand();
                return false;
            }

            if (_guardianHoldActive)
            {
                AirframeTune tune = ResolveTune(ac);
                bool aggressive = _guardianKind == GuardianKind.InvertDive
                    || _guardianKind == GuardianKind.Spin
                    || _guardianKind == GuardianKind.PostStall;
                ApplyGuardianLimitBoost(ac, aggressive);
                try
                {
                    if (_guardianKind == GuardianKind.InvertDive && IsInvertedThreat(ac, tune))
                    {
                        ApplyInvertedPullUp(ac, tune);
                        return true;
                    }
                    if (_guardianKind == GuardianKind.Spin)
                    {
                        ApplySpinRecovery(ac, tune);
                        return true;
                    }
                    if (_guardianKind == GuardianKind.PostStall)
                    {
                        ApplyPostStallRecovery(ac, tune);
                        return true;
                    }
                    // Terrain / generic: AP owns aim. Never vector nozzles during F2 LAND —
                    // LAND CV winged approach dies if customAxis1 is forced to 1 (25C live).
                    if (tune.StovlNozzle && !PlayerAutopilot.IsLandingMode)
                        ApplyStovlNozzleThrust(ac, true);
                }
                catch (Exception ex)
                {
                    if (Plugin.Log != null)
                        Plugin.Log.LogWarning("Guardian recovery: " + ex.Message);
                }
            }

            if (_takeoff == TakeoffPhase.Idle)
                return false;
            try
            {
                ApplyTakeoff(ac);
                return true;
            }
            catch (Exception ex)
            {
                ClearTakeoff();
                Flash(UiLang.T("Takeoff aborted", "起飞中止"));
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("Beginner takeoff: " + ex.Message);
                return false;
            }
        }

        /// <summary>Re-apply guardian boost after ManeuverProfile / pilot limit stamps.</summary>
        internal static void OverlayGuardianLimitsIfNeeded(Aircraft aircraft)
        {
            if (!_guardianHoldActive || aircraft == null)
                return;
            bool aggressive = _guardianKind == GuardianKind.InvertDive
                || _guardianKind == GuardianKind.Spin
                || _guardianKind == GuardianKind.PostStall;
            ApplyGuardianLimitBoost(aircraft, aggressive);
        }

        internal static void OverlayGuardianPilotLimitsIfNeeded(PilotPlayerState pps, Aircraft aircraft)
        {
            if (!_guardianHoldActive || pps == null)
                return;
            bool aggressive = _guardianKind == GuardianKind.InvertDive
                || _guardianKind == GuardianKind.Spin
                || _guardianKind == GuardianKind.PostStall;
            float tgt = aggressive ? 18f : 14f;
            if (!_hasSavedPilotG)
            {
                float cur;
                if (Plugin.TryReadPilotMaxG(pps, out cur) && cur > 0.1f)
                {
                    _savedPilotG = cur;
                    _hasSavedPilotG = true;
                }
            }
            Plugin.WriteGuardianPilotG(pps, Mathf.Max(tgt, _hasSavedPilotG ? _savedPilotG : tgt));
        }

        internal static void DrawGui()
        {
            EnsureStyles();
            if (_f3TipOpen)
            {
                DrawF3TipPanel();
                return;
            }

            if (_enabled == null || !_enabled.Value)
                return;
            Aircraft ac = ResolveLocal();
            if (ac == null && !_menuOpen)
                return;
            if (MissileCameraHud.ManualActive)
                return;

            if (Plugin.AllowThirdPersonUi)
            DrawCornerHint();
            if (Plugin.AllowThirdPersonUi)
            DrawFlash();

            if (_menuOpen)
            {
                HoldCursor();
                DrawMenu(ac);
            }
        }

        private static void TickFirstMissionF3Tip()
        {
            Aircraft ac = ResolveLocal();
            if (ac != null)
            {
                if (!_hadLocalAircraft)
                {
                    _hadLocalAircraft = true;
                    ArmF3TipIfNeeded();
                }
            }
            else
                _hadLocalAircraft = false;

            if (!_f3TipArmed || _f3TipOpen)
                return;
            if (Time.unscaledTime < _f3TipShowAt)
                return;
            // Wait until boot welcome / changelog overlays are gone.
            if (OritasyPresentation.OverlayActive)
            {
                _f3TipShowAt = Time.unscaledTime + 0.75f;
                return;
            }
            OpenF3Tip();
        }

        private static void ArmF3TipIfNeeded()
        {
            if (_f3TipArmed || _f3TipOpen)
                return;
            try
            {
                if (PlayerPrefs.GetInt(F3TipPrefKey, 0) != 0)
                    return;
            }
            catch { }
            _f3TipArmed = true;
            _f3TipShowAt = Time.unscaledTime + 1.25f;
        }

        private static void OpenF3Tip()
        {
            _f3TipArmed = false;
            _f3TipOpen = true;
            CaptureCursor();
        }

        private static void CloseF3Tip()
        {
            if (!_f3TipOpen)
                return;
            _f3TipOpen = false;
            try
            {
                PlayerPrefs.SetInt(F3TipPrefKey, 1);
                PlayerPrefs.Save();
            }
            catch { }
            ReleaseCursor();
        }

        private static void DrawF3TipPanel()
        {
            HoldCursor();
            int prevDepth = GUI.depth;
            GUI.depth = -997;
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(new Rect(0f, 0f, UiScaleService.Width, UiScaleService.Height), Texture2D.whiteTexture);
            GUI.color = prev;

            float boxW = Mathf.Min(520f, UiScaleService.Width * 0.88f);
            float boxH = 220f;
            Rect box = new Rect((UiScaleService.Width - boxW) * 0.5f, (UiScaleService.Height - boxH) * 0.5f, boxW, boxH);
            GUI.color = new Color(0.04f, 0.07f, 0.06f, 0.96f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = new Color(0.55f, 0.85f, 1f, 0.95f);
            GUI.DrawTexture(new Rect(box.x, box.y, box.width, 3f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(box.x + 18f, box.y + 14f, box.width - 36f, 28f),
                UiLang.T("Beginner Assist", "新手辅助"), _titleStyle);
            GUI.Label(new Rect(box.x + 18f, box.y + 52f, box.width - 36f, 100f),
                UiLang.T(
                    "You can press F3 to open Beginner mode.\n\n"
                    + "Auto takeoff / land, crash guardian, and terrain avoidance "
                    + "help you stay alive while learning the aircraft.",
                    "可按 F3 打开新手模式。\n\n"
                    + "自动起飞/着陆、防坠毁与地形规避，帮助你在熟悉机型时保持安全。"),
                _labelStyle);

            float bw = Mathf.Min(200f, box.width - 40f);
            if (GUI.Button(new Rect(box.x + (box.width - bw) * 0.5f, box.yMax - 48f, bw, 34f),
                UiLang.T("GOT IT", "知道了"), _btnStyle))
                CloseF3Tip();

            GUI.depth = prevDepth;
        }

        private static void OpenMenu()
        {
            if (MissileCameraHud.ManualActive)
                return;
            if (AircraftManeuverGui.IsOpen)
                AircraftManeuverGui.Close();
            if (PlayerAutopilot.MenuOpen)
                PlayerAutopilot.CloseMenuFromOutside();
            if (AerialResupply.MenuOpen)
                AerialResupply.CloseMenuFromOutside();
            if (WarThunderRwrHud.LayoutMenuOpen)
                WarThunderRwrHud.CloseLayoutMenuFromOutside();
            if (IlsSettingsMenu.MenuOpen)
                IlsSettingsMenu.CloseMenuFromOutside();
            if (PrivateMessageMenu.MenuOpen)
                PrivateMessageMenu.CloseMenuFromOutside();
            if (KillChoiceMenu.MenuOpen)
                KillChoiceMenu.CloseMenuFromOutside();
            if (HostFundMenu.MenuOpen)
                HostFundMenu.CloseMenuFromOutside();
            AirframeWearGui.CloseFromOutside();
            PlayerAutopilot.CloseWeXonSupportMenu();
            _menuOpen = true;
            CaptureCursor();
        }

        private static void CloseMenu()
        {
            _menuOpen = false;
            ReleaseCursor();
        }

        private static void DrawCornerHint()
        {
            if (Event.current != null && Event.current.type != EventType.Repaint)
                return;
            Aircraft ac = ResolveLocal();
            if (ac == null)
                return;

            Rect chip = PlayerAutopilot.CornerChipRect(AssistMenuLayoutService.SlotF3);
            Color prev = GUI.color;
            GUI.color = new Color(0.05f, 0.07f, 0.1f, 0.78f);
            GUI.DrawTexture(chip, Texture2D.whiteTexture);
            bool busy = _menuOpen || _takeoff != TakeoffPhase.Idle || _guardianOn || _terrainOn;
            GUI.color = _menuOpen || _takeoff != TakeoffPhase.Idle
                ? new Color(0.95f, 0.8f, 0.35f, 0.95f)
                : new Color(0.55f, 0.85f, 1f, 0.9f);
            GUI.DrawTexture(new Rect(chip.x, chip.y, chip.width, 2f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            string line = AssistStatusFormatService.BeginnerChip(
                _takeoff != TakeoffPhase.Idle, _menuOpen);
            _chipStyle.normal.textColor = busy
                ? new Color(0.85f, 0.95f, 1f, 0.98f)
                : new Color(0.8f, 0.9f, 1f, 0.95f);
            GUI.Label(new Rect(chip.x + 6f, chip.y, chip.width - 12f, chip.height), line, _chipStyle);
            GUI.color = prev;
        }

        private static void DrawFlash()
        {
            if (string.IsNullOrEmpty(_flash) || Time.unscaledTime > _flashUntil)
                return;
            if (Event.current != null && Event.current.type != EventType.Repaint)
                return;
            Rect r = AssistMenuLayoutService.FlashBannerRect(UiScaleService.Width, UiScaleService.Height);
            Color prev = GUI.color;
            GUI.color = new Color(0.05f, 0.06f, 0.08f, 0.75f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(r, _flash, _flashStyle);
            GUI.color = prev;
        }

        private static void DrawMenu(Aircraft ac)
        {
            Rect box = AssistMenuLayoutService.BeginnerMenuRect(UiScaleService.Width, UiScaleService.Height);
            Color prev = GUI.color;
            GUI.color = new Color(0.06f, 0.08f, 0.1f, 0.92f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = new Color(0.55f, 0.85f, 1f, 0.95f);
            GUI.DrawTexture(new Rect(box.x, box.y, box.width, 3f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(box.x + 16f, box.y + 12f, box.width - 32f, 26f),
                UiLang.T("BEGINNER ASSIST  (F3)", "新手辅助（F3）"), _titleStyle);
            GUI.Label(new Rect(box.x + 16f, box.y + 40f, box.width - 32f, 18f),
                UiLang.T("Auto takeoff · crash & terrain safety  (land = F2)",
                    "自动起飞 · 防坠毁与地形安全（着陆用 F2）"), _labelStyle);

            float y = box.y + 70f;
            float bw = box.width - 32f;

            if (GUI.Button(new Rect(box.x + 16f, y, bw, 34f),
                _takeoff == TakeoffPhase.Idle
                    ? UiLang.T("AUTO TAKEOFF  (AI taxi then takeoff)", "自动起飞（滑行到跑道后起飞）")
                    : UiLang.T("CANCEL TAKEOFF", "取消起飞"), _btnStyle))
            {
                if (_takeoff == TakeoffPhase.Idle)
                    StartTakeoff(ac);
                else
                {
                    ClearTakeoff();
                    Flash(UiLang.T("Takeoff cancelled", "起飞已取消"));
                }
            }
            y += 48f;

            bool g = GUI.Toggle(new Rect(box.x + 16f, y, bw, 22f), _guardianOn,
                UiLang.T(" Crash guardian  (stall / spin / falling-leaf / invert)",
                    " 防坠毁（失速/螺旋/落叶飘/倒飞）"));
            if (g != _guardianOn)
            {
                _guardianOn = g;
                if (_guardianDefault != null)
                    _guardianDefault.Value = g;
            }
            y += 28f;

            bool t = GUI.Toggle(new Rect(box.x + 16f, y, bw, 22f), _terrainOn,
                UiLang.T(" Terrain avoidance  (pull-up near ground)",
                    " 地形规避（近地拉起）"));
            if (t != _terrainOn)
            {
                _terrainOn = t;
                if (_terrainDefault != null)
                    _terrainDefault.Value = t;
            }
            y += 36f;

            string status = UiLang.T("Idle", "空闲");
            if (_takeoff != TakeoffPhase.Idle)
            {
                string phase = _takeoff == TakeoffPhase.Taxi
                    ? UiLang.T("taxi", "滑行")
                    : UiLang.T("takeoff", "起飞");
                status = UiLang.T("Takeoff: ", "起飞：") + phase;
                if (!string.IsNullOrEmpty(_takeoffDetail))
                    status = status + "\n" + _takeoffDetail;
            }
            else if (PlayerAutopilot.IsEngaged)
                status = UiLang.T("Autopilot engaged", "自动驾驶已接通");
            GUI.Label(new Rect(box.x + 16f, y, bw, 36f),
                UiLang.T("Status: ", "状态：") + status, _labelStyle);
            y += 40f;

            if (GUI.Button(new Rect(box.x + 16f, y, bw, 32f),
                UiLang.T("Close", "关闭"), _btnStyle))
                CloseMenu();

            GUI.color = prev;
        }


        internal static void Flash(string msg)
        {
            _flash = msg != null ? msg : string.Empty;
            _flashUntil = Time.unscaledTime + 3.5f;
        }

        /// <summary>
        /// Writes the same runtime + config field the F3 "Terrain avoidance" toggle uses.
        /// </summary>
        internal static void SetTerrainAvoidance(bool on, bool persist)
        {
            _terrainOn = on;
            if (persist && _terrainDefault != null)
                _terrainDefault.Value = on;
        }

        private static bool ShouldAutoDisableTerrain(Aircraft ac)
        {
            if (ac == null)
                return false;
            if (_autoDisableTerrainOnGear == null || !_autoDisableTerrainOnGear.Value)
                return false;
            // F2 LAND owns approach / followTerrain — do not fight or rewrite BA toggles mid-LAND.
            if (PlayerAutopilot.IsLandingMode)
                return false;
            try
            {
                if (!GameManager.IsLocalAircraft(ac))
                    return false;
            }
            catch { return false; }
            return true;
        }

        /// <summary>
        /// Gear UP commanded (SetGear false while LockedExtended) — clear Terrain before retract starts.
        /// </summary>
        private static void TryDisableTerrainBeforeGearUp(Aircraft ac)
        {
            if (!ShouldAutoDisableTerrain(ac))
                return;
            if (!_terrainOn)
                return;
            try
            {
                if (ac.gearState != LandingGear.GearState.LockedExtended)
                    return;
            }
            catch { return; }
            SetTerrainAvoidance(false, true);
            Flash("TERRAIN PULL-UP  OFF  (gear up)");
        }

        /// <summary>
        /// Gear finished DOWN (Extending → LockedExtended) — clear Terrain after lock.
        /// Skips spawn Uninitialized→LockedExtended.
        /// </summary>
        private static void TryDisableTerrainAfterGearDownLocked(
            Aircraft ac, LandingGear.GearState previous, LandingGear.GearState next)
        {
            if (!ShouldAutoDisableTerrain(ac))
                return;
            if (!_terrainOn)
                return;
            if (next != LandingGear.GearState.LockedExtended)
                return;
            if (previous != LandingGear.GearState.Extending)
                return;
            SetTerrainAvoidance(false, true);
            Flash("TERRAIN PULL-UP  OFF  (gear down)");
        }

        private static Aircraft ResolveLocal()
        {
            try
            {
                Aircraft ac;
                if (GameManager.GetLocalAircraft(out ac) && ac != null)
                    return ac;
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Prefix SetGear(bool): player gear UP → disable Terrain pull-up before GearStateChanged retracts.
        /// </summary>
        [HarmonyPatch(typeof(Aircraft), "SetGear", new Type[] { typeof(bool) })]
        private static class Patch_Aircraft_SetGearBool
        {
            [HarmonyPrefix]
            private static void Prefix(Aircraft __instance, bool deployed)
            {
                if (deployed)
                    return;
                TryDisableTerrainBeforeGearUp(__instance);
            }
        }

        /// <summary>
        /// Postfix SetGear(GearState): after Extending→LockedExtended, disable Terrain pull-up.
        /// </summary>
        [HarmonyPatch(typeof(Aircraft), "SetGear", new Type[] { typeof(LandingGear.GearState) })]
        private static class Patch_Aircraft_SetGearState
        {
            [HarmonyPrefix]
            private static void Prefix(Aircraft __instance, out LandingGear.GearState __state)
            {
                __state = LandingGear.GearState.Uninitialized;
                try
                {
                    if (__instance != null)
                        __state = __instance.gearState;
                }
                catch { }
            }

            [HarmonyPostfix]
            private static void Postfix(Aircraft __instance, LandingGear.GearState gearState,
                LandingGear.GearState __state)
            {
                TryDisableTerrainAfterGearDownLocked(__instance, __state, gearState);
            }
        }

        private static void CaptureCursor()
        {
            if (_cursorHeld)
                return;
            OritasyCursor.Hold();
            _cursorHeld = true;
        }

        private static void HoldCursor()
        {
            if (!_cursorHeld)
                CaptureCursor();
            OritasyCursor.Pulse();
        }

        private static void ReleaseCursor()
        {
            if (!_cursorHeld)
                return;
            OritasyCursor.Release();
            _cursorHeld = false;
        }

        private static void EnsureStyles()
        {
            if (_chipStyle != null)
                return;
            _chipStyle = new GUIStyle(GUI.skin.label);
            _chipStyle.fontSize = 11;
            _chipStyle.fontStyle = FontStyle.Bold;
            _chipStyle.alignment = TextAnchor.MiddleRight;
            _chipStyle.normal.textColor = new Color(0.8f, 0.95f, 1f, 0.95f);

            _titleStyle = new GUIStyle(GUI.skin.label);
            _titleStyle.fontSize = 18;
            _titleStyle.fontStyle = FontStyle.Bold;
            _titleStyle.alignment = TextAnchor.MiddleLeft;
            _titleStyle.normal.textColor = new Color(0.75f, 0.95f, 1f, 1f);

            _labelStyle = new GUIStyle(GUI.skin.label);
            _labelStyle.fontSize = 13;
            _labelStyle.alignment = TextAnchor.MiddleLeft;
            _labelStyle.normal.textColor = new Color(0.85f, 0.92f, 0.98f, 0.95f);
            _labelStyle.wordWrap = true;

            _btnStyle = new GUIStyle(GUI.skin.button);
            _btnStyle.fontSize = 13;
            _btnStyle.fontStyle = FontStyle.Bold;
            _btnStyle.alignment = TextAnchor.MiddleCenter;
            _btnStyle.normal.textColor = Color.white;

            _flashStyle = new GUIStyle(GUI.skin.label);
            _flashStyle.fontSize = 16;
            _flashStyle.fontStyle = FontStyle.Bold;
            _flashStyle.alignment = TextAnchor.MiddleCenter;
            _flashStyle.normal.textColor = new Color(1f, 0.85f, 0.35f, 1f);
        }
    }
}
