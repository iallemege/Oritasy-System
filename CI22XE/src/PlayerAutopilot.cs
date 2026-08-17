using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Player autopilot: F2 opens mode menu; Engage from GUI.
    /// Modes: straight (set AGL + speed), orbit, nearest airbase / carrier land.
    /// Optional TEST overlay: missile evade (IR dump / radar notch) while engaged.
    /// While engaged: stick blocked, mouse-look / Switch View stay free.
    /// </summary>
    internal static partial class PlayerAutopilot
    {
        internal const float CornerChipH = AssistMenuLayoutService.ChipH;
        internal const float CornerChipGap = AssistMenuLayoutService.ChipGap;

        /// <summary>True when stick/throttle should ignore the player (AP / resupply / beginner takeoff).</summary>
        internal static bool BlocksPlayerControls
        {
            get { return _engaged || AerialResupply.IsActive || BeginnerAssist.BlocksControls; }
        }

        private enum ApMode
        {
            Straight = 0,
            Orbit = 1,
            LandBase = 2,
            LandCarrier = 3
        }

        /// <summary>True when F2 mode is LAND CV (used by LandingGuidance).</summary>
        internal static bool IsLandCarrierMode
        {
            get { return _mode == ApMode.LandCarrier; }
        }

        /// <summary>True when F2 mode is LAND BASE.</summary>
        internal static bool IsLandBaseMode
        {
            get { return _mode == ApMode.LandBase; }
        }

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<KeyCode> _menuKey;
        private static ConfigEntry<KeyCode> _prevKey;
        private static ConfigEntry<KeyCode> _nextKey;

        internal static ConfigEntry<KeyCode> PrevModeKey
        {
            get { return _prevKey; }
        }

        internal static ConfigEntry<KeyCode> NextModeKey
        {
            get { return _nextKey; }
        }
        private static ConfigEntry<float> _orbitRadius;
        private static ConfigEntry<float> _orbitRate;
        private static ConfigEntry<float> _holdAlt;
        private static ConfigEntry<float> _straightAlt;
        private static ConfigEntry<float> _straightSpeedKmh;
        private static ConfigEntry<float> _fuelHudInterval;
        private static ConfigEntry<bool> _evadeMissilesTest;

        private static bool _menuOpen;
        private static bool _engaged;
        private static ApMode _mode = ApMode.Straight;
        private static float _orbitAngle;
        private static Vector3 _orbitCenterLocal;
        private static bool _hasOrbitCenter;
        /// <summary>LAND CV rectangular traffic pattern (五边) relative to runway heading.</summary>
        internal enum CvPatternLeg
        {
            None = 0,
            Upwind = 1,
            Crosswind = 2,
            Downwind = 3,
            Base = 4,
            Final = 5
        }

        internal static Airbase _landBase;
        /// <summary>F2 menu pick — ResolveLand prefers this when still valid.</summary>
        internal static Airbase _preferredLandBase;
        private static Vector2 _landPickScroll;
        private static readonly List<Airbase> _landPickList = new List<Airbase>(16);
        private static float _landPickNextRefresh;
        internal static Airbase.Runway.RunwayUsage _runway;
        internal static bool _hasRunway;
        internal static bool _reachedApproach;
        /// <summary>VTOL/STOVL: true once over the pad and using Autopilot.Hover descent.</summary>
        internal static bool _vtolHovering;

        internal static CvPatternLeg _cvLeg = CvPatternLeg.None;
        /// <summary>+1 / −1 pattern side in runway-right coordinates.</summary>
        internal static float _cvPatSide = 1f;
        /// <summary>Stuck-leg watchdog for 五边 (same leg forever → force advance).</summary>
        internal static CvPatternLeg _cvLegWatch = CvPatternLeg.None;
        internal static float _cvLegWatchSince;
        internal static float _cvNextCarrierRescan;
        /// <summary>
        /// Sticky lock while F2 LAND is engaged — Crash Guardian must not EngageStraightHold
        /// even if mode ordinal is briefly wrong mid-tick (26C live LAND↔STRAIGHT flips).
        /// </summary>
        private static bool _landingApProtected;
        private static ApMode _protectedLandMode = ApMode.LandCarrier;
        private static float _holdHeading;   // locked magnetic heading for STRAIGHT
        internal static float _holdAgl;       // locked AGL for terrain follow
        private static float _holdSpeed;     // target TAS (m/s) for STRAIGHT
        internal static string _status = "OFF";
        private static bool _savedFlightAssist;
        private static bool _hasSavedAssist;
        private static bool _cursorHeld;
        private static GUIStyle _chipStyle;
        private static GUIStyle _alertStyle;
        private static GUIStyle _titleStyle;
        private static GUIStyle _labelStyle;
        private static GUIStyle _btnStyle;
        private static GUIStyle _fuelStyle;

        // Missile evade TEST overlay (mirrors AIPilotCombatModes IR / radar paths).
        internal static bool _evadeOn;
        internal static bool _evadeWasActive;
        internal static float _evadeReactAt = -1f;   // unscaledTime when reaction may start
        internal static float _cmHoldUntil = -1f;    // keep Countermeasures(true) until
        internal static float _nextCmAt;             // rate-limit CM pulses
        internal static Vector3 _evadeBeam = Vector3.right;
        internal static int _evadeSign = 1;

        // Fuel HUD sampling
        private static float _fuelSampleQty = -1f;
        private static float _fuelSampleTime;
        private static float _fuelFlowKgPerS;   // EMA
        private static float _fuelHudNextRefresh;
        private static float _fuelHudFlashUntil;
        private static string _fuelHudLine1 = "";
        private static string _fuelHudLine2 = "";

        // Airframe / aero health cache (damage → landing margins).
        private static int _airframeAcId;
        private static float _airframeNextSample;
        private static AirframeCondition _airframe;

        private static readonly FieldInfo TurbojetOperableField =
            typeof(Turbojet).GetField("operable", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo TurbofanOperableField =
            typeof(Turbofan).GetField("operable", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo TurbineOperableField =
            typeof(TurbineEngine).GetField("operable", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo DuctedFanInoperableField =
            typeof(DuctedFan).GetField("inoperable", BindingFlags.Instance | BindingFlags.NonPublic);

        internal struct AirframeCondition
        {
            public float Detached;      // PartDamageTracker 0..1
            public float WingFrac;      // remaining wing area fraction 0..1
            public float EngineHealth;  // 1 healthy .. 0 dead (avg)
            public float Severity;      // combined 0 healthy .. 1 critical
            public bool EngineFire;
            public int DetachedParts;
            public int WingParts;
        }

        internal static bool IsEngaged
        {
            get { return _engaged; }
        }

        /// <summary>F2 LAND BASE / LAND CV — Crash Guardian must not steal to STRAIGHT.</summary>
        internal static bool IsLandingMode
        {
            get
            {
                return _engaged
                    && (_landingApProtected
                        || _mode == ApMode.LandBase
                        || _mode == ApMode.LandCarrier);
            }
        }

        internal static bool MenuOpen
        {
            get { return _menuOpen; }
        }

        internal static void CloseMenuFromOutside()
        {
            CloseMenu();
        }

        /// <summary>Close WeXon F9 support menu when another Oritasy menu opens.</summary>
        internal static void CloseWeXonSupportMenu()
        {
            try
            {
                System.Type t = System.Type.GetType("WeXon.StrategicArsenal, Oritasy")
                    ?? System.Type.GetType("WeXon.StrategicArsenal");
                if (t == null)
                    return;
                System.Reflection.MethodInfo m = t.GetMethod("CloseMenuFromOutside",
                    System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic);
                if (m != null)
                    m.Invoke(null, null);
            }
            catch { }
        }

        /// <summary>
        /// Top-right tip stack under capacitor.
        /// slot 0=F1 … 6=F7, 7=F8, 8=F9, 9=F10, 10=F11
        /// </summary>
        internal static float CornerChipY(int slot)
        {
            return AssistMenuLayoutService.ChipY(ResolveCornerStackY(), slot);
        }

        internal static Rect CornerChipRect(int slot)
        {
            return AssistMenuLayoutService.ChipRect(UiScaleService.Width, ResolveCornerStackY(), slot);
        }

        /// <summary>OnGUI Y of the F2 hint chip.</summary>
        internal static float F2HintTopY()
        {
            return CornerChipY(AssistMenuLayoutService.SlotF2);
        }

        internal static void Bind(ConfigFile config)
        {
            if (config == null)
                return;
            _enabled = config.Bind("Autopilot", "Enabled", true,
                "Player autopilot. F2 opens mode menu.");
            _menuKey = config.Bind("Autopilot", "MenuKey", KeyCode.F2,
                "Open / close autopilot menu.");
            // Legacy key name still accepted if present in cfg
            try
            {
                ConfigEntry<KeyCode> legacy = config.Bind("Autopilot", "ToggleKey", KeyCode.F2,
                    "Deprecated — use MenuKey.");
                if (_menuKey.Value == KeyCode.F2 && legacy.Value != KeyCode.F2)
                    _menuKey.Value = legacy.Value;
            }
            catch { }
            _prevKey = config.Bind("Autopilot", "PrevModeKey", KeyCode.Semicolon,
                "Previous autopilot mode (;).");
            _nextKey = config.Bind("Autopilot", "NextModeKey", KeyCode.Quote,
                "Next autopilot mode (').");
            if (_prevKey.Value == KeyCode.Comma)
                _prevKey.Value = KeyCode.Semicolon;
            if (_nextKey.Value == KeyCode.Period)
                _nextKey.Value = KeyCode.Quote;
            _orbitRadius = config.Bind("Autopilot", "OrbitRadius", 4500f,
                "Orbit mode circle radius (m).");
            _orbitRate = config.Bind("Autopilot", "OrbitRateDeg", 8f,
                "Orbit aim-point rate (deg/s).");
            _holdAlt = config.Bind("Autopilot", "HoldAltitudeAgl", 800f,
                "Fallback AGL for orbit / legacy (m).");
            _straightAlt = config.Bind("Autopilot", "StraightAltitudeAgl", 800f,
                "STRAIGHT mode target AGL (m). Set in F2 menu (max 15000).");
            _straightSpeedKmh = config.Bind("Autopilot", "StraightSpeedKmh", 900f,
                "STRAIGHT mode target speed (km/h). Set in F2 menu.");
            _fuelHudInterval = config.Bind("Autopilot", "FuelHudInterval", 8f,
                "Seconds between fuel HUD flash readouts while flying.");
            _evadeMissilesTest = config.Bind("Autopilot", "MissileEvadeTest", false,
                "TEST: while autopilot is engaged, auto-evade incoming missiles (IR dump / radar notch).");
            _evadeOn = _evadeMissilesTest != null && _evadeMissilesTest.Value;
        }

        internal static void Tick()
        {
            if (_enabled == null || !_enabled.Value)
                return;

            Aircraft ac = ResolveLocal();
            if (ac == null)
            {
                if (_engaged)
                    Disengage(false);
                if (_menuOpen)
                    CloseMenu();
                return;
            }

            if (MissileCameraHud.ManualActive)
            {
                if (_engaged)
                    Disengage(false);
                if (_menuOpen)
                    CloseMenu();
                return;
            }

            KeyCode menu = _menuKey != null ? _menuKey.Value : KeyCode.F2;
            KeyCode prev = _prevKey != null ? _prevKey.Value : KeyCode.Semicolon;
            KeyCode next = _nextKey != null ? _nextKey.Value : KeyCode.Quote;

            if (Input.GetKeyDown(menu))
            {
                if (_menuOpen)
                    CloseMenu();
                else
                    OpenMenu();
            }

            if (_menuOpen && Input.GetKeyDown(KeyCode.Escape))
                CloseMenu();

            // Hotkeys still cycle mode when menus are closed
            if (!_menuOpen && !AerialResupply.MenuOpen && !BeginnerAssist.MenuOpen)
            {
                if (Input.GetKeyDown(prev))
                {
                    _mode = (ApMode)(((int)_mode + 3) % 4);
                    if (_engaged)
                        OnModeChanged(ac);
                    else
                        RefreshIdleStatus();
                }
                if (Input.GetKeyDown(next))
                {
                    _mode = (ApMode)(((int)_mode + 1) % 4);
                    if (_engaged)
                        OnModeChanged(ac);
                    else
                        RefreshIdleStatus();
                }
            }

            if (_engaged && (ac.disabled || !Plugin.IsRuntimeInstance(ac)))
                Disengage(false);

            if (!_engaged)
            {
                _evadeWasActive = false;
                RefreshIdleStatus();
            }

            SampleFuel(ac);
            TickCountermeasurePulse(ac);
        }

        /// <summary>Called after PilotPlayerState.FixedUpdateState so AutoAim wins over stick.</summary>
        internal static void ApplyAfterPlayer(Aircraft ac)
        {
            if (ac == null || ac.autopilot == null)
                return;
            if (MissileCameraHud.ManualActive)
                return;
            // Aerial resupply owns level-hold while active.
            if (AerialResupply.IsActive)
            {
                AerialResupply.ApplyLevelFlight(ac);
                return;
            }
            // Beginner auto-takeoff sequence (before full AP engage).
            if (BeginnerAssist.ApplyFlight(ac))
                return;
            if (!_engaged)
                return;
            try
            {
                if (_evadeOn && TryApplyMissileEvade(ac))
                    return;
                ApplyMode(ac);
                // Belt-and-suspenders: LAND CV must never leave FS-20 in STOVL nozzle mode.
                // Live 25C: customAxis1 stayed 1.0 through SHORT_FINAL for ~300s.
                if (_engaged && _mode == ApMode.LandCarrier
                    && !PlayerCarrierVanillaLand.IsActive)
                {
                    ControlInputs ci = null;
                    try { ci = ac.GetInputs(); }
                    catch { }
                    ForceCarrierNormalFlight(ci);
                }
            }
            catch (Exception ex)
            {
                _status = "ERR";
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("Autopilot: " + ex.Message);
            }
        }

        internal static void DrawGui()
        {
            if (_enabled == null || !_enabled.Value)
                return;
            Aircraft ac = ResolveLocal();
            if (ac == null)
                return;
            if (MissileCameraHud.ManualActive)
                return;

            EnsureStyles();
            // Tip chips + fuel flash are overlay chrome; F2 menu still opens via hotkey.
            if (Plugin.AllowThirdPersonUi)
            {
            DrawCornerHint();
            DrawFuelHud(ac);
            }

            if (_menuOpen)
            {
                HoldCursor();
                DrawMenu(ac);
            }
        }

        private static void DrawCornerHint()
        {
            if (Event.current != null && Event.current.type != EventType.Repaint)
                return;

            Rect chip = CornerChipRect(AssistMenuLayoutService.SlotF2);
            Color prev = GUI.color;
            GUI.color = new Color(0.05f, 0.07f, 0.1f, 0.78f);
            GUI.DrawTexture(chip, Texture2D.whiteTexture);
            GUI.color = _engaged
                ? new Color(0.35f, 0.95f, 0.55f, 0.95f)
                : new Color(0.55f, 0.75f, 0.9f, 0.9f);
            GUI.DrawTexture(new Rect(chip.x, chip.y, chip.width, 2f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            string line = AssistStatusFormatService.AutopilotChip(_engaged, ModeLabel(_mode));
            _chipStyle.normal.textColor = _engaged
                ? new Color(0.7f, 1f, 0.8f, 0.98f)
                : new Color(0.8f, 0.9f, 1f, 0.95f);
            GUI.Label(new Rect(chip.x + 6f, chip.y, chip.width - 12f, chip.height), line, _chipStyle);
            GUI.color = prev;
        }

        private static void OpenMenu()
        {
            if (MissileCameraHud.ManualActive)
                return;
            if (AerialResupply.MenuOpen)
                AerialResupply.CloseMenuFromOutside();
            if (WarThunderRwrHud.LayoutMenuOpen)
                WarThunderRwrHud.CloseLayoutMenuFromOutside();
            if (BeginnerAssist.MenuOpen)
                BeginnerAssist.CloseMenuFromOutside();
            if (IlsSettingsMenu.MenuOpen)
                IlsSettingsMenu.CloseMenuFromOutside();
            if (PrivateMessageMenu.MenuOpen)
                PrivateMessageMenu.CloseMenuFromOutside();
            if (KillChoiceMenu.MenuOpen)
                KillChoiceMenu.CloseMenuFromOutside();
            if (HostFundMenu.MenuOpen)
                HostFundMenu.CloseMenuFromOutside();
            AirframeWearGui.CloseFromOutside();
            CloseWeXonSupportMenu();
            _menuOpen = true;
            CaptureCursor();
        }

        private static void CloseMenu()
        {
            _menuOpen = false;
            ReleaseCursor();
        }

        private static void DrawMenu(Aircraft ac)
        {
            bool land = _mode == ApMode.LandBase || _mode == ApMode.LandCarrier;
            Rect box = AssistMenuLayoutService.AutopilotMenuRect(
                UiScaleService.Width, UiScaleService.Height, _mode == ApMode.Straight, land);
            Color prev = GUI.color;
            GUI.color = new Color(0.06f, 0.08f, 0.1f, 0.92f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = _engaged
                ? new Color(0.35f, 0.95f, 0.55f, 0.95f)
                : new Color(0.45f, 0.8f, 1f, 0.95f);
            GUI.DrawTexture(new Rect(box.x, box.y, box.width, 3f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(box.x + 16f, box.y + 12f, box.width - 32f, 26f),
                UiLang.T("AUTOPILOT  (F2)", "自动驾驶"), _titleStyle);

            float y = box.y + 48f;
            GUI.Label(new Rect(box.x + 16f, y, box.width - 32f, 18f),
                UiLang.T("Select auto-flight mode", "选择自动驾驶模式"), _labelStyle);
            y += 24f;

            float bw = (box.width - 48f) / 2f;
            float bh = 32f;
            DrawModeButton(new Rect(box.x + 16f, y, bw, bh), ApMode.Straight,
                UiLang.T("STRAIGHT", "平飞"));
            DrawModeButton(new Rect(box.x + 24f + bw, y, bw, bh), ApMode.Orbit,
                UiLang.T("ORBIT", "盘旋"));
            y += bh + 8f;
            DrawModeButton(new Rect(box.x + 16f, y, bw, bh), ApMode.LandBase,
                UiLang.T("LAND BASE", "着陆机场"));
            DrawModeButton(new Rect(box.x + 24f + bw, y, bw, bh), ApMode.LandCarrier,
                UiLang.T("LAND CV", "着陆航母"));
            y += bh + 12f;

            if (_mode == ApMode.Straight)
            {
                float alt = _straightAlt != null ? _straightAlt.Value : 800f;
                float spdKmh = _straightSpeedKmh != null ? _straightSpeedKmh.Value : 900f;

                GUI.Label(new Rect(box.x + 16f, y, box.width - 32f, 18f),
                    UiLang.T("STRAIGHT altitude  ", "平飞高度  ") + alt.ToString("0")
                    + UiLang.T(" m AGL", " 米 雷达高"), _labelStyle);
                y += 20f;
                alt = GUI.HorizontalSlider(new Rect(box.x + 16f, y, box.width - 32f, 16f),
                    alt, 100f, 15000f);
                y += 22f;

                GUI.Label(new Rect(box.x + 16f, y, box.width - 32f, 18f),
                    UiLang.T("STRAIGHT speed  ", "平飞速度  ")
                    + GameUnitDisplayService.Speed(spdKmh / 3.6f),
                    _labelStyle);
                y += 20f;
                spdKmh = GUI.HorizontalSlider(new Rect(box.x + 16f, y, box.width - 32f, 16f),
                    spdKmh, 200f, 2500f);
                y += 18f;

                if (GUI.Button(new Rect(box.x + 16f, y, (box.width - 40f) * 0.5f, 24f),
                    UiLang.T("Use current", "使用当前"), _btnStyle))
                {
                    try
                    {
                        float ralt = ac != null ? ac.radarAlt : alt;
                        if (ralt > 50f)
                            alt = ralt;
                        if (ac != null)
                            spdKmh = Mathf.Max(200f, ac.speed * 3.6f);
                    }
                    catch { }
                }
                y += 28f;

                if (_straightAlt != null)
                    _straightAlt.Value = alt;
                if (_straightSpeedKmh != null)
                    _straightSpeedKmh.Value = spdKmh;

                // Live apply while engaged in straight
                if (_engaged && _mode == ApMode.Straight)
                {
                    _holdAgl = Mathf.Clamp(alt, 100f, 15000f);
                    _holdSpeed = Mathf.Max(40f, spdKmh / 3.6f);
                }
            }

            if (_mode == ApMode.LandBase || _mode == ApMode.LandCarrier)
            {
                DrawLandBasePicker(ac, box, ref y);
            }

            bool evade = GUI.Toggle(new Rect(box.x + 16f, y, box.width - 32f, 22f), _evadeOn,
                UiLang.T(" Missile evade  (TEST — IR dump / radar notch)",
                    " 导弹规避（测试 — 红外干扰 / 雷达缺口）"));
            if (evade != _evadeOn)
            {
                _evadeOn = evade;
                if (_evadeMissilesTest != null)
                    _evadeMissilesTest.Value = evade;
                if (!_evadeOn)
                    ResetEvadeState();
            }
            y += 26f;

            GUI.Label(new Rect(box.x + 16f, y, box.width - 32f, 36f),
                UiLang.T("Status: ", "状态：") + (_engaged
                    ? UiLang.T("ON  ", "开  ")
                    : UiLang.T("OFF  ", "关  ")) + ModeLabel(_mode)
                + (_evadeOn ? UiLang.T("  +EVADE", "  +规避") : "")
                + "\n" + _status, _labelStyle);
            y += 44f;

            if (_engaged)
            {
                if (GUI.Button(new Rect(box.x + 16f, y, box.width - 32f, 36f),
                    UiLang.T("DISENGAGE AUTOPILOT", "断开自动驾驶"), _btnStyle))
                {
                    Disengage(true);
                }
            }
            else
            {
                if (GUI.Button(new Rect(box.x + 16f, y, box.width - 32f, 36f),
                    UiLang.T("ENGAGE  —  ", "接通  —  ") + ModeLabel(_mode), _btnStyle))
                {
                    Engage(ac);
                }
            }
            y += 44f;

            GUI.Label(new Rect(box.x + 16f, y, box.width - 32f, 36f),
                UiLang.T("; / ' cycle  ·  Esc close  ·  look/view free while ON",
                    "; / ' 切换  ·  Esc 关闭  ·  接通时视角自由"), _labelStyle);
            GUI.color = prev;
        }

        private static void DrawLandBasePicker(Aircraft ac, Rect box, ref float y)
        {
            RefreshLandPickList(ac, _mode == ApMode.LandCarrier);
            GUI.Label(new Rect(box.x + 16f, y, box.width - 32f, 18f),
                _mode == ApMode.LandCarrier
                    ? UiLang.T("Select carrier deck", "选择航母甲板")
                    : UiLang.T("Select airbase", "选择机场"), _labelStyle);
            y += 20f;

            Rect listR = new Rect(box.x + 16f, y, box.width - 32f, 140f);
            Color prev = GUI.color;
            GUI.color = new Color(0.1f, 0.12f, 0.14f, 0.95f);
            GUI.DrawTexture(listR, Texture2D.whiteTexture);
            GUI.color = Color.white;

            float contentH = 28f + _landPickList.Count * 26f;
            _landPickScroll = GUI.BeginScrollView(listR, _landPickScroll,
                new Rect(0f, 0f, listR.width - 18f, Mathf.Max(listR.height, contentH)));

            float rowY = 2f;
            bool nearestOn = _preferredLandBase == null;
            if (GUI.Toggle(new Rect(4f, rowY, listR.width - 24f, 22f), nearestOn,
                UiLang.T(" Nearest (auto)", " 最近（自动）")))
            {
                if (_preferredLandBase != null)
                {
                    _preferredLandBase = null;
                    if (_engaged)
                        ResolveLand(ac, _mode == ApMode.LandCarrier);
                }
            }
            rowY += 26f;

            for (int i = 0; i < _landPickList.Count; i++)
            {
                Airbase ab = _landPickList[i];
                if (ab == null)
                    continue;
                bool on = object.ReferenceEquals(_preferredLandBase, ab);
                string name = FormatAirbaseName(ab, _mode == ApMode.LandCarrier);
                float km = 0f;
                try
                {
                    if (ac != null)
                    {
                        Vector3 p = ab.center != null ? ab.center.position : ab.transform.position;
                        km = Vector3.Distance(ac.transform.position, p) * 0.001f;
                    }
            }
            catch { }
                string line = " " + name + "  " + GameUnitDisplayService.Distance(km * 1000f);
                if (GUI.Toggle(new Rect(4f, rowY, listR.width - 24f, 22f), on, line))
                {
                    if (!object.ReferenceEquals(_preferredLandBase, ab))
                    {
                        _preferredLandBase = ab;
                        _landBase = ab;
                        _hasRunway = false;
                        if (_engaged)
                            ResolveLand(ac, _mode == ApMode.LandCarrier);
                    }
                }
                rowY += 26f;
            }
            GUI.EndScrollView();
                GUI.color = prev;
            y += 148f;
        }

        private static void RefreshLandPickList(Aircraft ac, bool carrierOnly)
        {
            float now = Time.unscaledTime;
            if (now < _landPickNextRefresh && _landPickList.Count > 0)
                return;
            _landPickNextRefresh = now + 1.25f;
            _landPickList.Clear();
            try
            {
                FactionHQ hq = null;
                try { if (ac != null) hq = ac.NetworkHQ; }
            catch { }
                if (hq == null)
                {
                    try { GameManager.GetLocalHQ(out hq); }
            catch { }
                }
                if (hq == null)
                    return;

                AircraftParameters parms = null;
                try { if (ac != null) parms = ac.GetAircraftParameters(); }
            catch { }

                Vector3 from = ac != null ? ac.transform.position : Vector3.zero;
                if (carrierOnly)
                {
                    foreach (Airbase ab in hq.GetAirbases())
                    {
                        if (ab == null || ab.disabled)
                            continue;
                        if (!IsCarrierAirbase(ab))
                            continue;
                        _landPickList.Add(ab);
                    }
                }
                else
                {
                    RunwayQuery q = BuildLandQuery(ac, parms);
                    foreach (Airbase ab in hq.GetAirbases())
                    {
                        if (ab == null || ab.disabled)
                            continue;
                        if (IsCarrierAirbase(ab) || ab.AttachedAirbase)
                            continue;
                        try
                        {
                            if (!ab.IsSuitable(q))
                                continue;
                        }
                        catch { continue; }
                        _landPickList.Add(ab);
                    }
                }

                _landPickList.Sort((a, b) =>
                {
                    float da = float.MaxValue, db = float.MaxValue;
                    try
                    {
                        Vector3 pa = a.center != null ? a.center.position : a.transform.position;
                        da = (pa - from).sqrMagnitude;
            }
            catch { }
                    try
                    {
                        Vector3 pb = b.center != null ? b.center.position : b.transform.position;
                        db = (pb - from).sqrMagnitude;
                    }
                        catch { }
                    return da.CompareTo(db);
                });
                if (_landPickList.Count > 24)
                    _landPickList.RemoveRange(24, _landPickList.Count - 24);
            }
            catch { }
        }

        private static void DrawModeButton(Rect r, ApMode mode, string label)
        {
            bool on = _mode == mode;
            Color prev = GUI.color;
            GUI.color = on
                ? new Color(0.25f, 0.7f, 0.4f, 0.95f)
                : new Color(0.2f, 0.25f, 0.3f, 0.9f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
            if (GUI.Button(r, label, _btnStyle))
            {
                Aircraft ac = ResolveLocal();
                if (mode == ApMode.LandBase || mode == ApMode.LandCarrier)
                    BeginnerAssist.YieldToAutopilotLand();
                _mode = mode;
                if (_engaged && ac != null)
                    OnModeChanged(ac);
                else
                    RefreshIdleStatus();
            }
            GUI.color = prev;
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


        internal static void EngageStraightHold(float aglMeters, float speedMs)
        {
            // Never let Crash Guardian overwrite an active LAND BASE / LAND CV approach.
            if (_landingApProtected || IsLandingMode)
                return;
            Aircraft ac = ResolveLocal();
            if (ac == null || ac.autopilot == null)
                return;
            _mode = ApMode.Straight;
            if (_straightAlt != null)
                _straightAlt.Value = Mathf.Clamp(aglMeters, 100f, 15000f);
            if (_straightSpeedKmh != null)
                _straightSpeedKmh.Value = Mathf.Clamp(speedMs * 3.6f, 200f, 2500f);
            Engage(ac);
            _holdAgl = Mathf.Clamp(Mathf.Max(100f, aglMeters), 100f, 15000f);
            _holdSpeed = Mathf.Max(40f, speedMs);
        }

        internal static void DisengageFromOutside(bool restoreControls)
        {
            Disengage(restoreControls);
        }

        /// <summary>Crash Guardian: snapshot current AP mode ordinal (0..3).</summary>
        internal static int PeekModeOrdinal()
        {
            return (int)_mode;
        }

        /// <summary>Crash Guardian handback: restore mode without killing an already-on F2 AP.</summary>
        internal static void RestoreModeAfterGuardian(int modeOrdinal)
        {
            // Prefer sticky LAND lock — do not OnModeChanged (would wipe runway / 五边 state).
            if (_landingApProtected && _engaged)
            {
                _mode = _protectedLandMode;
                return;
            }
            if (modeOrdinal < 0 || modeOrdinal > 3)
                modeOrdinal = 0;
            _mode = (ApMode)modeOrdinal;
            Aircraft ac = ResolveLocal();
            if (_engaged && ac != null)
                OnModeChanged(ac);
        }

        private static void Engage(Aircraft ac)
        {
            if (ac == null || ac.autopilot == null)
            {
                _status = "NO AP";
                return;
            }
            if (_mode == ApMode.LandBase || _mode == ApMode.LandCarrier)
            {
                _landingApProtected = true;
                _protectedLandMode = _mode;
                BeginnerAssist.YieldToAutopilotLand();
            }
            else
                _landingApProtected = false;
            _engaged = true;
            // Keep GameManager.flightControlsEnabled true so mouse-look / Switch View still work.
            // Stick / weapons stay blocked via Harmony (BlocksPlayerControls).
            CaptureHoldRefs(ac);
            // AI always flies with flight assist — without it AutoAim banks oscillate.
            try
            {
                if (!_hasSavedAssist)
                {
                    _savedFlightAssist = ac.flightAssist;
                    _hasSavedAssist = true;
                }
                // Only set when off — SetFlightAssist always fires the HUD toast.
                if (!ac.flightAssist)
                    ac.SetFlightAssist(true);
            }
            catch { }
            OnModeChanged(ac);
            if (Plugin.Log != null)
                Plugin.Log.LogInfo("Autopilot ON: " + ModeLabel(_mode));
        }

        private static void Disengage(bool restoreControls)
        {
            PlayerCarrierVanillaLand.Stop();
            Aircraft ac = ResolveLocal();
            _engaged = false;
            _landingApProtected = false;
            _hasRunway = false;
            _landBase = null;
            _hasOrbitCenter = false;
            _reachedApproach = false;
            _vtolHovering = false;
            _cvLeg = CvPatternLeg.None;
            _cvLegWatch = CvPatternLeg.None;
            _cvPatSide = 1f;
            _cvNextCarrierRescan = 0f;
            ResetEvadeState();
            if (ac != null)
                MissileEvadeService.StopCountermeasures(ac);
            if (_hasSavedAssist)
            {
                try
                {
                    if (ac != null && restoreControls && !AerialResupply.IsActive)
                        ac.SetFlightAssist(_savedFlightAssist);
                }
                catch { }
                _hasSavedAssist = false;
            }
            RefreshIdleStatus();
        }


        private static void CaptureHoldRefs(Aircraft ac)
        {
            // Lock heading from ground-track (velocity) so banked nose does not chase itself.
            Vector3 track = Vector3.forward;
            try
            {
                if (ac.rb != null && ac.rb.velocity.sqrMagnitude > 25f)
                    track = ac.rb.velocity;
                else
                    track = ac.transform.forward;
            }
            catch { track = ac.transform.forward; }
            track.y = 0f;
            if (track.sqrMagnitude < 0.01f)
                track = Vector3.forward;
            _holdHeading = Mathf.Atan2(track.x, track.z) * Mathf.Rad2Deg;
            if (_holdHeading < 0f)
                _holdHeading += 360f;

            float minAgl = 200f;
            try
            {
                AircraftParameters p = ac.GetAircraftParameters();
                if (p != null)
                    minAgl = Mathf.Max(minAgl, p.minimumRadarAlt);
            }
            catch { }

            if (_mode == ApMode.Straight)
            {
                float alt = _straightAlt != null ? _straightAlt.Value : 800f;
                float spdKmh = _straightSpeedKmh != null ? _straightSpeedKmh.Value : 900f;
                _holdAgl = Mathf.Clamp(Mathf.Max(minAgl, alt), 100f, 15000f);
                _holdSpeed = Mathf.Max(40f, spdKmh / 3.6f);
            }
            else
            {
                float cfg = _holdAlt != null ? _holdAlt.Value : 800f;
                float ralt = 0f;
                try { ralt = ac.radarAlt; }
                catch { }
                _holdAgl = Mathf.Max(minAgl, cfg, ralt > 50f ? ralt : cfg);
                float corner = 120f;
                try
                {
                    AircraftParameters p = ac.GetAircraftParameters();
                    if (p != null)
                        corner = p.cornerSpeed;
                }
                catch { }
                _holdSpeed = corner;
            }
        }

        private static void OnModeChanged(Aircraft ac)
        {
            _hasRunway = false;
            _landBase = null;
            _hasOrbitCenter = false;
            _reachedApproach = false;
            _vtolHovering = false;
            _cvLeg = CvPatternLeg.None;
            _cvLegWatch = CvPatternLeg.None;
            _cvPatSide = 1f;
            _cvNextCarrierRescan = 0f;
            if (_engaged && (_mode == ApMode.LandBase || _mode == ApMode.LandCarrier))
            {
                _landingApProtected = true;
                _protectedLandMode = _mode;
            }
            else if (!_engaged || (_mode != ApMode.LandBase && _mode != ApMode.LandCarrier))
                _landingApProtected = false;
            CaptureHoldRefs(ac);
            if (_mode == ApMode.Orbit)
            {
                _orbitCenterLocal = ac.transform.position;
                _orbitAngle = _holdHeading * Mathf.Deg2Rad;
                _hasOrbitCenter = true;
            }
            else if (_mode == ApMode.LandBase)
                ResolveLand(ac, false);
            else if (_mode == ApMode.LandCarrier)
                ResolveLand(ac, true);
        }

        private static void ApplyMode(Aircraft ac)
        {
            Autopilot ap = ac.autopilot;
            ControlInputs inputs = ac.GetInputs();
            if (inputs == null)
                return;
            AircraftParameters parms = ac.GetAircraftParameters();
            float cruise = parms != null ? parms.cruiseThrottle : 0.7f;
            float corner = parms != null ? parms.cornerSpeed : 120f;
            float landSpd = parms != null ? parms.landingSpeed : 70f;
            float turnR = parms != null ? Mathf.Max(800f, parms.turningRadius) : 2500f;

            // Do NOT disable GameManager.flightControlsEnabled — that freezes mouse-look / Switch View.
            // Do NOT call SetFlightAssist every tick — it spams "flight stability enabled".

            if (_mode == ApMode.Straight)
            {
                _status = AutopilotFlightLaws.ApplyStraight(ac, ap, inputs,
                    _holdHeading, _holdAgl, _holdSpeed, corner);
            }
            else if (_mode == ApMode.Orbit)
            {
                float radius = _orbitRadius != null ? _orbitRadius.Value : 4500f;
                _status = AutopilotFlightLaws.ApplyOrbit(ac, ap, inputs,
                    ref _orbitCenterLocal, ref _hasOrbitCenter, _holdHeading, _holdAgl,
                    radius, corner, cruise);
            }
            else if (_mode == ApMode.LandCarrier
                && PlayerCarrierVanillaLand.TryApply(ac))
            {
                return;
            }
            else
            {
                if (_mode != ApMode.LandCarrier)
                    PlayerCarrierVanillaLand.Stop();
                ApplyLanding(ac, ap, inputs, parms, landSpd, turnR, cruise);
            }
        }


        internal static void AutoAimAny(Autopilot ap, GlobalPosition dest, bool aimVelocity,
            bool ignoreCollisions, bool runwayAlign, float effort, float bankAllowed,
            bool followTerrain, float altitudeHold, Vector3 targetVelocity)
        {
            AutopilotAim.AutoAim(ap, dest, aimVelocity, ignoreCollisions, runwayAlign,
                effort, bankAllowed, followTerrain, altitudeHold, targetVelocity);
        }


        private static void RefreshIdleStatus()
        {
            _status = UiLang.Zh(ModeLabel(_mode) + "  (F2)");
        }

        private static string ModeLabel(ApMode m)
        {
            switch (m)
            {
                case ApMode.Straight: return UiLang.T("STRAIGHT", "平飞");
                case ApMode.Orbit: return UiLang.T("ORBIT", "盘旋");
                case ApMode.LandBase: return UiLang.T("LAND BASE", "着陆机场");
                case ApMode.LandCarrier: return UiLang.T("LAND CV", "着陆航母");
                default: return "?";
            }
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

        private static float ReadAoA(Aircraft ac)
        {
            try
            {
                Vector3 vel = ac.rb != null ? ac.rb.velocity : Vector3.zero;
                if (vel.sqrMagnitude < 1f)
                    return 0f;
                Vector3 local = ac.transform.InverseTransformDirection(vel.normalized);
                return Mathf.Atan2(local.y, local.z) * -Mathf.Rad2Deg;
            }
            catch { return 0f; }
        }

        private static float ReadG(Aircraft ac)
        {
            try
            {
                if (ac.pilots != null && ac.pilots.Length > 0 && ac.pilots[0] != null && !ac.pilots[0].dead)
                    return ac.pilots[0].gForce;
            }
            catch { }
            try { return ac.gForce; }
            catch { return 0f; }
        }

        private static void SampleFuel(Aircraft ac)
        {
            if (ac == null)
            {
                _fuelSampleQty = -1f;
                return;
            }

            float qty = 0f;
            try { qty = ac.GetFuelQuantity(); }
            catch { return; }

            float now = Time.unscaledTime;
            if (_fuelSampleQty < 0f)
            {
                _fuelSampleQty = qty;
                _fuelSampleTime = now;
                return;
            }

            float dt = now - _fuelSampleTime;
            if (dt < 0.35f)
                return;

            float burned = _fuelSampleQty - qty;
            float inst = burned / Mathf.Max(0.001f, dt);
            // Ignore refuel spikes / tank jank
            if (inst < -0.5f)
                inst = 0f;
            inst = Mathf.Clamp(inst, 0f, 80f);
            if (_fuelFlowKgPerS <= 0.001f)
                _fuelFlowKgPerS = inst;
            else
                _fuelFlowKgPerS = Mathf.Lerp(_fuelFlowKgPerS, inst, 0.35f);

            _fuelSampleQty = qty;
            _fuelSampleTime = now;

            float interval = _fuelHudInterval != null ? Mathf.Max(3f, _fuelHudInterval.Value) : 8f;
            if (now >= _fuelHudNextRefresh)
            {
                _fuelHudNextRefresh = now + interval;
                _fuelHudFlashUntil = now + Mathf.Min(4.5f, interval * 0.55f);
                RefreshFuelHudText(ac, qty);
            }
        }

        private static void RefreshFuelHudText(Aircraft ac, float qtyKg)
        {
            float flow = Mathf.Max(0f, _fuelFlowKgPerS);
            float flowPerMin = flow * 60f;
            float speed = 0f;
            try { speed = Mathf.Max(0f, ac.speed); }
            catch { }

            string range;
            if (flow > 0.02f && speed > 5f)
            {
                float enduranceS = qtyKg / flow;
                float rangeM = speed * enduranceS;
                range = GameUnitDisplayService.Distance(rangeM);
            }
            else
                range = "—";

            _fuelHudLine1 = UiLang.T(
                "FUEL  " + GameUnitDisplayService.Weight(qtyKg)
                + "   FLOW  " + GameUnitDisplayService.MassFlow(flowPerMin, "/min"),
                "燃油  " + GameUnitDisplayService.Weight(qtyKg)
                + "   流量  " + GameUnitDisplayService.MassFlow(flowPerMin, "/min"));
            _fuelHudLine2 = UiLang.T(
                "RANGE  " + range
                + "   BURN  " + GameUnitDisplayService.MassFlow(flow, "/s"),
                "航程  " + range
                + "   消耗  " + GameUnitDisplayService.MassFlow(flow, "/s"));
        }

        private static void DrawFuelHud(Aircraft ac)
        {
            if (ac == null)
                return;
            if (Event.current != null && Event.current.type != EventType.Repaint)
                return;
            if (Time.unscaledTime > _fuelHudFlashUntil || string.IsNullOrEmpty(_fuelHudLine1))
                return;

            Rect box = AssistMenuLayoutService.FuelHudRect(UiScaleService.Width, UiScaleService.Height);
            Color prev = GUI.color;
            GUI.color = new Color(0.04f, 0.06f, 0.08f, 0.78f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = new Color(0.35f, 0.85f, 1f, 0.95f);
            GUI.DrawTexture(new Rect(box.x, box.y, box.width, 2f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            _fuelStyle.normal.textColor = new Color(0.75f, 0.95f, 1f, 0.98f);
            GUI.Label(new Rect(box.x + 10f, box.y + 4f, box.width - 20f, 18f), _fuelHudLine1, _fuelStyle);
            GUI.Label(new Rect(box.x + 10f, box.y + 22f, box.width - 20f, 18f), _fuelHudLine2, _fuelStyle);
            GUI.color = prev;
        }

        private static void EnsureStyles()
        {
            if (_chipStyle != null)
                return;
            _chipStyle = new GUIStyle(GUI.skin.label);
            _chipStyle.fontSize = 11;
            _chipStyle.fontStyle = FontStyle.Bold;
            _chipStyle.alignment = TextAnchor.MiddleRight;
            _chipStyle.normal.textColor = new Color(0.85f, 1f, 0.9f, 0.95f);

            _alertStyle = new GUIStyle(GUI.skin.label);
            _alertStyle.fontSize = 16;
            _alertStyle.fontStyle = FontStyle.Bold;
            _alertStyle.alignment = TextAnchor.MiddleCenter;
            _alertStyle.normal.textColor = Color.red;

            _titleStyle = new GUIStyle(GUI.skin.label);
            _titleStyle.fontSize = 18;
            _titleStyle.fontStyle = FontStyle.Bold;
            _titleStyle.alignment = TextAnchor.MiddleLeft;
            _titleStyle.normal.textColor = new Color(0.75f, 1f, 0.9f, 1f);

            _labelStyle = new GUIStyle(GUI.skin.label);
            _labelStyle.fontSize = 13;
            _labelStyle.alignment = TextAnchor.MiddleLeft;
            _labelStyle.normal.textColor = new Color(0.85f, 0.9f, 0.95f, 0.95f);
            _labelStyle.wordWrap = true;

            _btnStyle = new GUIStyle(GUI.skin.button);
            _btnStyle.fontSize = 13;
            _btnStyle.fontStyle = FontStyle.Bold;
            _btnStyle.alignment = TextAnchor.MiddleCenter;
            _btnStyle.normal.textColor = Color.white;

            _fuelStyle = new GUIStyle(GUI.skin.label);
            _fuelStyle.fontSize = 12;
            _fuelStyle.fontStyle = FontStyle.Bold;
            _fuelStyle.alignment = TextAnchor.MiddleLeft;
            _fuelStyle.normal.textColor = new Color(0.75f, 0.95f, 1f, 0.98f);
        }
    }
}
