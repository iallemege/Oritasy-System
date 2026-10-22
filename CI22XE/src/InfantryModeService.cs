using System;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Oritasy
{
    internal enum InfantryOrigin
    {
        None = 0,
        ParkedAircraft = 1,
        Hangar = 2,
        Chute = 3
    }

    /// <summary>
    /// Local first-person infantry. Hangar spawn uses the saved kit (player-picked guns).
    /// or after parachute landing (1 carry-on). Parked hop-out (U) still works.
    /// </summary>
    internal static class InfantryModeService
    {
        internal const string PrefEnabledKey = "Oritasy.Infantry.Enabled";

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<KeyCode> _boardKey;
        private static ConfigEntry<float> _lookSens;
        private static ConfigEntry<float> _walkSpeed;
        private static ConfigEntry<float> _sprintSpeed;

        private static GameObject _pawn;
        private static CharacterController _cc;
        private static Transform _eye;
        private static Aircraft _parked;
        private static PilotDismounted _chuteDummy;
        private static CameraMode _savedMode;
        private static bool _savedLook;
        private static float _yaw;
        private static float _pitch;
        private static float _vertVel;
        private static float _baseFov = 70f;
        private static float _savedNear = -1f;
        private static bool _savedOcc;
        private static bool _haveSavedCam;
        private static Camera _infCam;
        private static string _deny;
        private static float _denyUntil;
        private static GUIStyle _hudStyle;
        private static Texture2D _px;
        private static bool _hangarQueued;
        private static int _chuteSeenId;
        private static bool _chuteArmed;
        private static float _chuteReadyAt;
        private static float _boardGraceUntil;
        private static FieldInfo _acEjectedField;
        private static bool _airborneHop;
        private static Vector3 _airVel;
        private static bool _keptSeat;
        private static bool _hidVanillaHud;
        private static bool _savedCockpitCamOn;
        private static bool _camDetached;
        private static int _frozeRbId;
        private static Renderer[] _chuteRs;
        private static int _chuteRsId;

        internal static bool Active;
        internal static Transform PawnRoot
        {
            get { return _pawn != null ? _pawn.transform : null; }
        }
        internal static Aircraft ClaimedAircraft
        {
            get { return _parked; }
        }

        internal static void ClaimAircraft(Aircraft ac)
        {
            if (ac == null)
                return;
            _parked = ac;
        }

        internal static void UnseatIfInfantry(Player player)
        {
            if (!Active || player == null)
                return;
            Aircraft ac = null;
            try { ac = player.Aircraft; }
            catch { }
            if (ac == null)
                return;
            ClaimAircraft(ac);
            try { player.RemoveAircraft(ac); }
            catch { }
        }

        internal static bool InBoardGrace()
        {
            return Time.unscaledTime < _boardGraceUntil;
        }

        internal static void TryBoardNearby()
        {
            Aircraft ac = _parked;
            if (ac == null || ac.disabled)
            {
                Deny(UiLang.T("No marked aircraft. Press B to buy one.",
                    "没有标记的飞机。按 B 购买。"));
                return;
            }
            Vector3 from = _pawn != null ? _pawn.transform.position : EyePos;
            float d = Vector3.Distance(from, ac.transform.position);
            if (d > BoardReach())
            {
                Deny(UiLang.T(
                    "Too far (" + Mathf.RoundToInt(d) + "m). Run to the cyan mark.",
                    "太远（" + Mathf.RoundToInt(d) + " m）。跑到青色标记旁。"));
                return;
            }
            BoardInto(ac);
        }

        internal static float BoardReach()
        {
            if (_airborneHop)
                return 48f;
            if (_parked != null)
            {
                float ralt = 0f;
                try { ralt = AiCombatMathService.SafeRalt(_parked); }
                catch { }
                if (ralt > 8f)
                    return 36f;
            }
            return InfantryShop.BoardRange;
        }

        private static void BoardInto(Aircraft ac)
        {
            if (ac == null)
                return;
            DisablePawnColliders();
            Active = false;
            RestoreCameraClip();
            RestoreVanillaHud();
            InfantryCombat.ClearBolts();
            InfantryCombat.DestroyViews();
            InfantryHud.CloseMenus(false);
            InfantryShop.Close(false);
            InfantryCabinGunService.Stop();
            InfantryAirbaseStations.CloseKit();
            TryRestoreCockpitCam();

            Player p = null;
            try { GameManager.GetLocalPlayer(out p); }
            catch { }
            if (p != null)
            {
                try
                {
                    PilotDismounted pd = p.PilotDismounted;
                    if (pd != null)
                        p.RemovePilotDismounted(pd);
                }
                catch { }
            }
            ClearEjected(ac);
            if (_pawn != null)
            {
                UnityEngine.Object.Destroy(_pawn);
                _pawn = null;
            }
            _cc = null;
            _eye = null;
            _chuteDummy = null;
            Origin = InfantryOrigin.None;
            bool keepSeat = _keptSeat;
            _parked = null;
            _airborneHop = false;
            _keptSeat = false;
            MoveSpeed = 0f;
            Sprinting = false;
            Strafe = 0f;
            _chuteArmed = false;
            _chuteSeenId = 0;
            _boardGraceUntil = Time.unscaledTime + 8f;

            try
            {
                if (!keepSeat && p != null)
                    p.SetAircraft(ac);
            }
            catch { }
            ClearEjected(ac);
            ScrubChute(ac);
            try
            {
                FlightHud hud = SceneSingleton<FlightHud>.i;
                if (hud != null)
                    hud.SetAircraft(ac);
            }
            catch { }
            try { CameraStateManager.cameraMode = CameraMode.cockpit; }
            catch { }
            try { CameraStateManager.enableMouseLook = true; }
            catch { }
            try { GameManager.flightControlsEnabled = true; }
            catch { }
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            try { CursorManager.SetFlag(CursorFlags.GameMenu, false); }
            catch { }
            try { CursorManager.SetFlag(CursorFlags.SelectionMenu, false); }
            catch { }
            OritasyCursor.SyncToManager();
        }

        private static void HideLeakedFlightCursor()
        {
            if (Active || OritasyCursor.Held)
                return;
            if (AircraftManeuverGui.IsOpen || OritasyPresentation.BlocksHud)
                return;
            if (JoinMenuFactionFix.JoinMenuOpen())
                return;
            if (InBoardGrace())
            {
                try { CursorManager.SetFlag(CursorFlags.SelectionMenu, false); }
                catch { }
            }
            try { CursorManager.SetFlag(CursorFlags.GameMenu, false); }
            catch { }
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            try { CursorManager.Refresh(); }
            catch { }
        }

        private static void DisablePawnColliders()
        {
            if (_cc != null)
                _cc.enabled = false;
            if (_pawn == null)
                return;
            Collider[] cols = _pawn.GetComponentsInChildren<Collider>(true);
            if (cols == null)
                return;
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i] != null)
                    cols[i].enabled = false;
            }
        }

        private static void ClearEjected(Aircraft ac)
        {
            if (ac == null)
                return;
            if (_acEjectedField == null)
                _acEjectedField = AccessTools.Field(typeof(Aircraft), "ejected");
            try
            {
                if (_acEjectedField != null)
                    _acEjectedField.SetValue(ac, false);
            }
            catch { }
            Pilot[] pilots = null;
            try { pilots = ac.GetComponentsInChildren<Pilot>(true); }
            catch { }
            if (pilots == null)
                return;
            for (int i = 0; i < pilots.Length; i++)
            {
                if (pilots[i] == null)
                    continue;
                pilots[i].ejected = false;
                pilots[i].dead = false;
            }
        }
        internal static float LookYaw
        {
            get { return _yaw; }
        }
        internal static Vector3 EyePos
        {
            get { return _eye != null ? _eye.position : Vector3.zero; }
        }
        internal static Vector3 EyeFwd
        {
            get { return _eye != null ? _eye.forward : Vector3.forward; }
        }
        internal static bool SprintActive
        {
            get { return Sprinting; }
        }
        internal static float SprintFill()
        {
            float fill = 1f - InfantryWarnService.ExoHeat;
            if (fill < 0f)
                fill = 0f;
            if (fill > 1f)
                fill = 1f;
            return fill;
        }
        internal static float JumpFill()
        {
            float now = Time.time;
            float fill = 1f;
            if (now < _jumpReadyAt)
                fill = 1f - (_jumpReadyAt - now) / JumpCd;
            if (fill < 0f)
                fill = 0f;
            if (fill > 1f)
                fill = 1f;
            return fill;
        }
        internal static float MoveSpeed;
        internal static bool Sprinting;
        internal static float Strafe;
        internal static InfantryOrigin Origin;
        internal const float MaxHp = 80f;
        internal const int OuterArmor = 10;
        internal const int InnerArmor = 1;
        internal static float Hp = MaxHp;
        internal static bool Dead;
        internal static bool ThirdPerson;
        /// <summary>True when Ground Arsenal (or another host) owns Tick/Draw.</summary>
        internal static bool ExternalHost;
        internal static float ArmorFatigue;
        private static float _lastArmorHitAt;
        private static float _deadUntil;
        private static bool _sceneHooked;
        private static float _jumpReadyAt;
        internal const float JumpCd = 3f;
        internal const float JumpHeight = 8f;

        internal static void Bind(BepInEx.Configuration.ConfigFile config)
        {
            if (config == null)
                return;
            _enabled = config.Bind("Infantry", "Enabled", false,
                "Off in the main pack. Ground Arsenal owns FPS infantry.");
            _boardKey = config.Bind("Infantry", "BoardKey", KeyCode.U,
                "Hop out / climb back in. Default U. Do not use I (vanilla Toggle Engine).");
            if (_boardKey.Value == KeyCode.I || _boardKey.Value == KeyCode.None)
                _boardKey.Value = KeyCode.U;
            ConfigEntry<KeyCode> legacy;
            if (config.TryGetEntry(new ConfigDefinition("Infantry", "ToggleKey"), out legacy)
                && legacy != null
                && legacy.Value != KeyCode.I
                && legacy.Value != KeyCode.None
                && legacy.Value != KeyCode.U
                && _boardKey.Value == KeyCode.U)
                _boardKey.Value = legacy.Value;
            _lookSens = config.Bind("Infantry", "LookSensitivity", 2.4f, "Mouse look.");
            _walkSpeed = config.Bind("Infantry", "WalkSpeed", 4.6f, "m/s");
            _sprintSpeed = config.Bind("Infantry", "SprintSpeed", 7.2f, "m/s");
            InfantryCabinGunService.Bind(config);
            if (!_sceneHooked)
            {
                _sceneHooked = true;
                try { SceneManager.sceneLoaded += OnSceneLoaded; }
                catch { }
            }
        }

        internal static ConfigEntry<KeyCode> BoardKey
        {
            get { return _boardKey; }
        }

        internal static KeyCode BoardKeyCode()
        {
            KeyCode k = _boardKey != null ? _boardKey.Value : KeyCode.U;
            if (k == KeyCode.I || k == KeyCode.None)
                k = KeyCode.U;
            return k;
        }

        internal static string BoardKeyLabel()
        {
            return BoardKeyCode().ToString();
        }

        internal static bool IsEnabled()
        {
            if (ExternalHost)
                return true;
            return _enabled != null && _enabled.Value;
        }

        internal static void SetEnabled(bool on)
        {
            if (_enabled != null)
                _enabled.Value = on;
            try
            {
                PlayerPrefs.SetInt(PrefEnabledKey, on ? 1 : 0);
                PlayerPrefs.Save();
            }
            catch { }
            if (!on && Active)
                Exit(true);
        }

        internal static void QueueHangarDeploy()
        {
            _hangarQueued = true;
        }

        internal static void Tick()
        {
            if (!IsEnabled())
            {
                if (Active)
                    Exit(false);
                _hangarQueued = false;
                return;
            }
            if (OritasyPresentation.MainMenuActive || OritasyPresentation.SplashActive)
            {
                ShutdownSession();
                return;
            }

            InfantryAirbaseStations.Tick();
            InfantryCabinGunService.Tick();
            InfantryAi.Tick();
            if (!Active)
            {
                InfantryCombat.WarmStep();
                InfantryCombat.WarmStep();
                InfantryBodyVisual.Warm();
                InfantryHud.Warm();
            }

            if (_hangarQueued)
            {
                _hangarQueued = false;
                TryEnterFromHangar();
            }

            if (!Active)
            {
                if (InBoardGrace())
                    ScrubChute(null);
                TickChuteLanding();
                if (InBoardGrace() || Time.unscaledTime < _boardGraceUntil + 1.6f)
                    HideLeakedFlightCursor();
            }

            KeyCode key = BoardKeyCode();
            if (Input.GetKeyDown(key) && !InfantryHud.BlocksLook
                && !AircraftManeuverGui.IsRebinding)
            {
                if (Active)
                    TryBoardNearby();
                else
                    TryEnterFromAircraft();
            }

            if (!Active)
                return;

            HoldFlightControls();
            if (Dead)
            {
                InfantryBodyVisual.EnsureAttached(_pawn != null ? _pawn.transform : null);
                InfantryBodyVisual.Tick(_pawn != null ? _pawn.transform : null, 0f, true);
                if (!InfantryHud.BlocksLook)
                    LookOnly();
                if (Time.unscaledTime >= _deadUntil
                    || Input.GetKeyDown(KeyCode.Space)
                    || Input.GetKeyDown(KeyCode.Escape))
                    FinishDeath();
                return;
            }

            TickArmorFatigue();
            InfantryWarnService.Tick(Time.deltaTime);
            if (Input.GetKeyDown(KeyCode.L) && !InfantryHud.BlocksLook)
                ThirdPerson = !ThirdPerson;
            InfantryHud.Tick();
            InfantryShop.Tick();
            InfantryRadar.Tick(Time.deltaTime);
            InfantryKamikaze.Tick();
            FreezeAircraft();
            if (Origin == InfantryOrigin.Chute && _chuteDummy != null)
                HideDummy(_chuteDummy);
            InfantryBodyVisual.SetLocalVisible(_pawn != null ? _pawn.transform : null, ThirdPerson);
            if (ThirdPerson)
            {
                InfantryBodyVisual.EnsureAttached(_pawn != null ? _pawn.transform : null);
                InfantryBodyVisual.Tick(_pawn != null ? _pawn.transform : null, MoveSpeed, false);
            }
            if (!InfantryHud.BlocksLook)
                LookMove();
            if (_eye != null && !InfantryHud.BlocksLook)
                InfantryCombat.Tick(_eye, Time.deltaTime);
        }

        internal static void ApplyCamera(CameraStateManager mgr)
        {
            if (!Active || _eye == null || mgr == null)
                return;
            Camera cam = mgr.mainCamera;
            if (cam == null)
                cam = Camera.main;
            if (cam == null)
                return;
            PlaceCamera(cam);
            if (!_haveSavedCam || _infCam != cam)
            {
                if (_infCam != null && _infCam != cam)
                    RestoreCameraClip();
                _infCam = cam;
                _savedNear = cam.nearClipPlane;
                _savedOcc = cam.useOcclusionCulling;
                _haveSavedCam = true;
            }
            if (cam.nearClipPlane > 0.035f)
                cam.nearClipPlane = 0.03f;
            float fov = _baseFov * InfantryCombat.FovMul();
            float fovFloor = 5.5f;
            if (InfantryCombat.Gun == InfantryGun.Sniper)
                fovFloor = 3.2f;
            else if (InfantryCombat.Gun == InfantryGun.G11)
                fovFloor = 7.5f;
            if (fov < fovFloor) fov = fovFloor;
            if (fov > 95f) fov = 95f;
            if (Mathf.Abs(cam.fieldOfView - fov) > 0.08f)
                cam.fieldOfView = fov;
            DetachInfantryCamera(mgr);
            if (CameraStateManager.cameraMode != CameraMode.free)
                CameraStateManager.cameraMode = CameraMode.free;
            if (CameraStateManager.enableMouseLook)
                CameraStateManager.enableMouseLook = false;
            InfantryCombat.BindToCamera(cam);
        }

        internal static void Draw()
        {
            if (OritasyPresentation.MainMenuActive || OritasyPresentation.SplashActive)
                return;
            if (!Active)
            {
                if (!string.IsNullOrEmpty(_deny) && Time.unscaledTime < _denyUntil)
                {
                    EnsureGui();
                    GUI.Label(new Rect(20f, Screen.height * 0.55f, 700f, 32f), _deny, _hudStyle);
                }
                InfantryCabinGunService.Draw();
                return;
            }
            EnsureGui();
            InfantryHud.Draw();
            InfantryAirbaseStations.Draw();
            InfantryCabinGunService.Draw();
        }

        internal static void DrawProfileSection()
        {
            GUILayout.Label(UiLang.T("Infantry (vanilla pilot mesh, can die)", "步兵（原版飞行员建模，可阵亡）"), GUILayout.ExpandWidth(true));
            bool on = IsEnabled();
            bool want = GUILayout.Toggle(on,
                UiLang.T(" Enable infantry", " 启用步兵"));
            if (want != on)
                SetEnabled(want);
            GUILayout.Label(UiLang.T(
                "Default first person. L chase cam. RMB ADS, wheel zoom. "
                    + "Hold Shift sprint, Space jump, 1-4/Q/E, B buy jet, ' table. "
                    + InfantryModeService.BoardKeyLabel() + " hop out / rendezook board. "
                    + "F gear/AD at hangar pads. G Ibis cabin guns. Aircraft: 1 gun. Ground kit: max 4. "
                    + "KIA black. Look at a nuke: 30s goggles + radar BSOD.",
                "默认第一人称，L 第三人称。右键开镜，滚轮变焦。"
                    + "按住 Shift 冲刺，空格跳，1-4/Q/E，B买机，' 穿深表。"
                    + InfantryModeService.BoardKeyLabel() + " 下车/跳机再登机。"
                    + "机库旁 F 换装/防空桩。朱鹮 G 舱内开枪。飞机只带 1 把，地面最多 4 把。"
                    + "阵亡黑屏。看核爆：护目罩 30 秒，雷达蓝屏。"),
                GUILayout.ExpandWidth(true));
        }

        internal static Unit LinkedUnit()
        {
            if (_parked != null)
                return _parked;
            return _chuteDummy;
        }

        internal static int OuterEffective()
        {
            float f = ArmorFatigue;
            if (f < 0f) f = 0f;
            if (f > 1f) f = 1f;
            return ArmorFatigueService.EffectiveArmor(OuterArmor, f, 8);
        }

        internal static void TickArmorFatigue()
        {
            if (ArmorFatigue <= 0.0001f)
                return;
            if (Time.time - _lastArmorHitAt < 5.5f)
                return;
            ArmorFatigue -= Time.deltaTime * 0.018f;
            if (ArmorFatigue < 0f)
                ArmorFatigue = 0f;
        }

        internal static void ReceiveHit(float pierce, float blast, float fire, float impact, PersistentID dealer, Vector3 incoming)
        {
            if (!Active || Dead)
                return;
            InfantryWarnService.NoteHit();
            InfantryWarnService.NoteFatigue(ArmorFatigue);
            int pen = ModularArmorService.PenGradeIncoming(dealer, pierce);
            WeaponInfo w = ModularArmorService.WeaponFromDealer(dealer);
            int wep = ModularArmorMathService.ClassifyKinetic(w);
            if (wep == ModularArmorMathService.WepUnknown)
            {
                if (blast > pierce * 0.8f && blast > 8f)
                    wep = ModularArmorMathService.WepRocket;
                else if (pen >= 13)
                    wep = ModularArmorMathService.WepMissile;
                else
                    wep = ModularArmorMathService.WepCannon;
            }

            bool pureKe = ModularArmorMathService.IsPureKeWeapon(wep, w);
            if (impact < 1f)
            {
                impact = ModularArmorMathService.SynthesizeKe(wep, pierce, impact, 0, 0f, 0f);
            }
            _lastArmorHitAt = Time.time;
            pen = ModularArmorMathService.ApplyPenFloat(pen);

            int outer = OuterEffective();
            if (outer >= 1)
            {
                int hit = ModularArmorMathService.ResolveHit(pen, outer, wep);
                float fat = 0.05f;
                if (hit == ModularArmorMathService.HitGlance)
                    fat = 0.09f;
                else if (hit == ModularArmorMathService.HitPen)
                    fat = 0.22f;
                else if (hit == ModularArmorMathService.HitOvermatch)
                    fat = 0.42f;
                int gap = pen - outer;
                if (gap < 0)
                    gap = -gap;
                if (gap <= 1)
                    fat *= 1.7f;
                AddFatigue(fat);
                InfantryWarnService.NoteFatigue(ArmorFatigue);

                if (hit == ModularArmorMathService.HitBounce
                    || hit == ModularArmorMathService.HitGlance)
                {
                    string kind = hit == ModularArmorMathService.HitGlance
                        ? UiLang.T("GLANCE", "跳飞")
                        : UiLang.T("BOUNCE", "跳弹");
                    InfantryHud.FlashHit(kind + "  " + UiLang.T(
                        "plate " + outer + "  0 dmg  fat "
                            + Mathf.RoundToInt(ArmorFatigue * 100f) + "%",
                        "外甲 " + outer + "  未击穿  疲劳 "
                            + Mathf.RoundToInt(ArmorFatigue * 100f) + "%"));
                    return;
                }

                float pass = 0.45f;
                if (hit == ModularArmorMathService.HitOvermatch)
                    pass = 0.82f;
                ApplyInner(pen, wep, pierce * pass, blast * pass, impact * pass, fire * pass, pureKe, hit, true);
                return;
            }

            ApplyInner(pen, wep, pierce, blast, impact, fire, pureKe, ModularArmorMathService.HitPen, false);
        }

        private static void AddFatigue(float d)
        {
            ArmorFatigue += d;
            if (ArmorFatigue > 1f)
                ArmorFatigue = 1f;
        }

        private static void ApplyInner(
            int pen, int wep, float pierce, float blast, float impact, float fire,
            bool pureKe, int outerHit, bool throughPlate)
        {
            int hit = ModularArmorMathService.ResolveHit(pen, InnerArmor, wep);
            if (hit == ModularArmorMathService.HitBounce
                || hit == ModularArmorMathService.HitGlance)
            {
                InfantryHud.FlashHit(UiLang.T("BODY GLANCE  0", "本体未击穿  0"));
                return;
            }
            float pm = ModularArmorMathService.PierceMul(hit);
            float bm = ModularArmorMathService.BlastMul(hit, false, pureKe);
            float im = ModularArmorMathService.ImpactMul(hit);
            float dmg = pierce * pm + blast * bm + impact * im + fire * 0.4f;
            if (dmg < 2f)
                dmg = 2f;
            Hp -= dmg;
            string layer = throughPlate
                ? UiLang.T("OVERPEN plate→body", "过穿 外甲→本体")
                : UiLang.T("BODY HIT", "命中本体");
            if (outerHit == ModularArmorMathService.HitOvermatch && throughPlate)
                layer = UiLang.T("OVERPEN plate→body", "过穿 外甲→本体");
            else if (throughPlate && outerHit == ModularArmorMathService.HitPen)
                layer = UiLang.T("PEN plate→body", "击穿 外甲→本体");
            InfantryHud.FlashHit(layer + "  KE " + Mathf.RoundToInt(impact * im)
                + "  -" + Mathf.RoundToInt(dmg) + " HP");
            if (Hp <= 0f)
            {
                Hp = 0f;
                Die();
            }
        }

        internal static void TryAbsorbWorldHit(Vector3 position, Vector3 velocity, float pierce, float blast, PersistentID dealer)
        {
            if (!Active || _pawn == null)
                return;
            float d = Vector3.Distance(_pawn.transform.position + Vector3.up * 0.9f, position);
            if (d < 0.55f || d > 1.7f)
                return;
            Vector3 incoming = velocity.sqrMagnitude > 0.01f ? velocity : Vector3.forward;
            float ke = velocity.magnitude * 0.12f;
            if (ke < pierce * 0.9f)
                ke = pierce * 0.9f;
            ReceiveHit(pierce, blast, 0f, ke, dealer, incoming);
        }

        internal static void KillFromKamikaze()
        {
            InfantryHud.FlashHit(UiLang.T("KAMIKAZE", "自爆"));
            InfantryHud.CloseMenus(false);
            bool toCockpit = Origin == InfantryOrigin.ParkedAircraft;
            Exit(toCockpit);
            Deny(UiLang.T("Infantry self-destruct (1 kt).", "步兵自爆（1千吨）。"));
        }

        private static void Die()
        {
            if (Dead)
                return;
            Dead = true;
            Hp = 0f;
            _deadUntil = Time.unscaledTime + 8f;
            InfantryHud.FlashHit(UiLang.T("KIA", "阵亡"));
            InfantryHud.CloseMenus(false);
            if (_cc != null)
                _cc.enabled = false;
            InfantryCombat.ClearBolts();
            InfantryBodyVisual.PlayDead(_pawn != null ? _pawn.transform : null);
            Deny(UiLang.T("KIA — Space / Esc to leave.", "阵亡 — 空格或 Esc 离开。"));
        }

        private static void FinishDeath()
        {
            Exit(false);
            Deny(UiLang.T("Infantry KIA.", "步兵阵亡。"));
        }

        internal static void ShutdownSession()
        {
            if (Active)
                Exit(false);
            Dead = false;
            InfantryAi.Clear();
            InfantryAirbaseStations.Clear();
            InfantryCabinGunService.Stop();
            InfantryHud.CloseMenus(true);
            InfantryShop.Close(true);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            string n = scene.name ?? "";
            if (OritasyPresentation.MainMenuActive
                || n.IndexOf("Menu", System.StringComparison.OrdinalIgnoreCase) >= 0)
                ShutdownSession();
        }

        internal static void TryEnterFromHangar()
        {
            if (Active)
                return;
            Vector3 pos;
            float yaw;
            if (!InfantryHangarMenu.TryHangarSpawn(out pos, out yaw))
            {
                Deny(UiLang.T("No hangar spawn point.", "找不到机库出生点。"));
                return;
            }
            InfantryHangarMenu.CloseHangarUi();
            SpawnPawn(pos, yaw, InfantryOrigin.Hangar, null);
            InfantryCombat.ApplySavedKit();
            InfantryCombat.AttachViews(_eye);
            if (Plugin.Log != null)
                Plugin.Log.LogInfo("Infantry hangar spawn");
        }

        private static void TryEnterFromAircraft()
        {
            Aircraft ac;
            if (!GameManager.GetLocalAircraft(out ac) || ac == null)
            {
                Deny(UiLang.T("No local aircraft.", "没有本机。"));
                return;
            }
            float spd = 0f;
            float ralt = 999f;
            try { spd = ac.speed; }
            catch { }
            ralt = AiCombatMathService.SafeRalt(ac);
            bool airHop = spd > 18f || ralt > 10f;

            Vector3 pos = ac.transform.position;
            Vector3 right = ac.transform.right;
            Vector3 spawn;
            if (airHop)
                spawn = pos + right * 4.2f - ac.transform.up * 1.2f;
            else
            {
                Vector3 start = pos + right * 4.2f + Vector3.up * 8f;
                RaycastHit hit;
                spawn = pos + right * 4.2f;
                spawn.y = pos.y;
                if (Physics.Raycast(start, Vector3.down, out hit, 80f,
                    InfantryPerf.SightMask, QueryTriggerInteraction.Ignore))
                    spawn = hit.point + Vector3.up * 0.08f;
            }

            float yaw = ac.transform.rotation.eulerAngles.y;
            InfantryCabinGunService.Stop();
            SpawnPawn(spawn, yaw, InfantryOrigin.ParkedAircraft, ac);
            InfantryCombat.ApplyCarry(InfantryCombat.LoadCarryGun());
            InfantryCombat.AttachViews(_eye);
            _airborneHop = airHop;
            _keptSeat = airHop;
            _airVel = Vector3.zero;
            try
            {
                if (ac.rb != null)
                    _airVel = ac.rb.velocity + right * 2.2f;
                else
                    _airVel = ac.transform.forward * spd + right * 2.2f;
            }
            catch
            {
                _airVel = ac.transform.forward * spd + right * 2.2f;
            }
            _vertVel = _airVel.y;
            if (!airHop)
            {
                try
                {
                    Player p;
                    if (GameManager.GetLocalPlayer(out p) && p != null)
                        p.RemoveAircraft(ac);
                }
                catch { }
            }
            HoldFlightControls();
            SilenceAircraft(ac, !airHop);
            if (Plugin.Log != null)
                Plugin.Log.LogInfo(airHop ? "Infantry rendezook hop" : "Infantry hop-out");
        }

        private static bool SeatedInAircraft()
        {
            try
            {
                Player p;
                if (GameManager.GetLocalPlayer(out p) && p != null)
                {
                    Aircraft seated = p.Aircraft;
                    if (seated != null && !seated.disabled)
                        return true;
                }
            }
            catch { }
            try
            {
                Aircraft ac;
                if (GameManager.GetLocalAircraft(out ac) && ac != null && !ac.disabled)
                    return true;
            }
            catch { }
            return false;
        }

        private static void ScrubChute(Aircraft ac)
        {
            ClearEjected(ac);
            Player p = null;
            try { GameManager.GetLocalPlayer(out p); }
            catch { }
            if (p != null)
            {
                try
                {
                    Aircraft seated = p.Aircraft;
                    if (seated != null)
                        ClearEjected(seated);
                }
                catch { }
                try
                {
                    PilotDismounted leftover = p.PilotDismounted;
                    if (leftover != null)
                    {
                        HideDummy(leftover);
                        p.RemovePilotDismounted(leftover);
                    }
                }
                catch { }
            }
            try
            {
                PilotDismounted pd;
                if (GameManager.GetLocalPilotDismounted(out pd) && pd != null)
                {
                    HideDummy(pd);
                    if (p != null)
                        p.RemovePilotDismounted(pd);
                }
            }
            catch { }
            _chuteArmed = false;
        }

        private static void TickChuteLanding()
        {
            if (InBoardGrace() || SeatedInAircraft())
            {
                if (InBoardGrace())
                    ScrubChute(null);
                return;
            }
            PilotDismounted pd = null;
            try
            {
                if (!GameManager.GetLocalPilotDismounted(out pd) || pd == null)
                {
                    _chuteSeenId = 0;
                    _chuteArmed = false;
                    return;
                }
            }
            catch
            {
                return;
            }

            int id = pd.GetInstanceID();
            if (id != _chuteSeenId)
            {
                _chuteSeenId = id;
                _chuteArmed = false;
                _chuteReadyAt = 0f;
            }

            PilotDismounted.PilotState st = pd.animationState;
            if (st == PilotDismounted.PilotState.dead)
            {
                _chuteArmed = false;
                return;
            }
            if (st != PilotDismounted.PilotState.landing)
                return;
            if (!_chuteArmed)
            {
                _chuteArmed = true;
                _chuteReadyAt = Time.unscaledTime + 0.65f;
                return;
            }
            if (Time.unscaledTime < _chuteReadyAt)
                return;
            EnterFromChute(pd);
        }

        private static void EnterFromChute(PilotDismounted pd)
        {
            if (pd == null || Active)
                return;
            if (InBoardGrace() || SeatedInAircraft())
                return;
            Vector3 pos = pd.transform.position;
            RaycastHit hit;
            if (Physics.Raycast(pos + Vector3.up * 4f, Vector3.down, out hit, 40f,
                InfantryPerf.SightMask, QueryTriggerInteraction.Ignore))
                pos = hit.point + Vector3.up * 0.08f;
            float yaw = pd.transform.rotation.eulerAngles.y;
            HideDummy(pd);
            _chuteDummy = pd;
            SpawnPawn(pos, yaw, InfantryOrigin.Chute, null);
            InfantryCombat.ApplyCarry(InfantryCombat.LoadCarryGun());
            InfantryCombat.AttachViews(_eye);
            if (Plugin.Log != null)
                Plugin.Log.LogInfo("Infantry from parachute");
        }

        private static void HideDummy(PilotDismounted pd)
        {
            if (pd == null)
                return;
            int id = pd.GetInstanceID();
            if (_chuteRs == null || id != _chuteRsId)
            {
                _chuteRsId = id;
                try { _chuteRs = pd.GetComponentsInChildren<Renderer>(true); }
                catch { _chuteRs = null; }
            }
            Renderer[] rs = _chuteRs;
            if (rs == null)
                return;
            for (int i = 0; i < rs.Length; i++)
            {
                if (rs[i] != null && rs[i].enabled)
                    rs[i].enabled = false;
            }
        }

        private static void SpawnPawn(Vector3 spawn, float yaw, InfantryOrigin origin, Aircraft parked)
        {
            if (Active)
                Exit(false);
            _parked = parked;
            Origin = origin;
            _savedMode = CameraStateManager.cameraMode;
            _savedLook = CameraStateManager.enableMouseLook;
            Camera cam = Camera.main;
            if (cam != null)
                _baseFov = cam.fieldOfView;

            _pawn = new GameObject("OritasyInfantryPawn");
            _pawn.transform.position = spawn;
            _yaw = yaw;
            _pitch = 0f;
            _pawn.transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            _cc = _pawn.AddComponent<CharacterController>();
            _cc.height = 1.8f;
            _cc.radius = 0.32f;
            _cc.center = new Vector3(0f, 0.9f, 0f);
            _cc.stepOffset = 0.35f;
            _cc.slopeLimit = 50f;
            InfantryPerf.ApplyWalkCollider(_cc);
            _pawn.AddComponent<InfantryPawn>();
            GameObject hitbox = new GameObject("Hitbox");
            hitbox.transform.SetParent(_pawn.transform, false);
            CapsuleCollider cap = hitbox.AddComponent<CapsuleCollider>();
            cap.height = 1.8f;
            cap.radius = 0.36f;
            cap.center = new Vector3(0f, 0.9f, 0f);
            Hp = MaxHp;
            ArmorFatigue = 0f;
            _lastArmorHitAt = 0f;
            GameObject eyeGo = new GameObject("Eye");
            eyeGo.transform.SetParent(_pawn.transform, false);
            eyeGo.transform.localPosition = new Vector3(0f, 1.64f, 0f);
            _eye = eyeGo.transform;
            Dead = false;
            ThirdPerson = false;
            InfantryWarnService.Reset();
            _vertVel = 0f;
            MoveSpeed = 0f;
            Sprinting = false;
            Strafe = 0f;
            _jumpReadyAt = 0f;
            _camDetached = false;
            _frozeRbId = 0;
            InfantryKamikaze.Reset();
            _airborneHop = false;
            _keptSeat = false;
            _airVel = Vector3.zero;
            Active = true;
            InfantryAirbaseStations.OnPawnSpawned();
            HideVanillaHud();
            HoldFlightControls();
            CameraStateManager.cameraMode = CameraMode.free;
            CameraStateManager.enableMouseLook = false;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private static void Exit(bool toCockpit)
        {
            InfantryCombat.ClearBolts();
            InfantryCombat.DestroyViews();
            RestoreCameraClip();
            RestoreVanillaHud();
            _chuteRs = null;
            _chuteRsId = 0;
            if (_pawn != null)
                UnityEngine.Object.Destroy(_pawn);
            _pawn = null;
            _cc = null;
            _eye = null;
            Active = false;
            Dead = false;
            ThirdPerson = false;
            MoveSpeed = 0f;
            Sprinting = false;
            Strafe = 0f;
            Origin = InfantryOrigin.None;
            _airborneHop = false;
            _keptSeat = false;
            _airVel = Vector3.zero;
            InfantryHud.CloseMenus(false);
            _chuteDummy = null;
            if (toCockpit)
            {
                try { CameraStateManager.cameraMode = _savedMode; }
                catch { CameraStateManager.cameraMode = CameraMode.cockpit; }
                CameraStateManager.enableMouseLook = _savedLook;
                TryRestoreCockpitCam();
            }
            _parked = null;
        }

        private static void LookOnly()
        {
            float sens = _lookSens != null ? _lookSens.Value : 2.4f;
            _yaw += Input.GetAxis("Mouse X") * sens;
            _pitch -= Input.GetAxis("Mouse Y") * sens;
            if (_pitch < -85f) _pitch = -85f;
            if (_pitch > 85f) _pitch = 85f;
            if (_eye != null)
                _eye.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private static void PlaceCamera(Camera cam)
        {
            if (cam == null || _pawn == null || _eye == null)
                return;
            Vector3 look = _eye.forward;
            if (look.sqrMagnitude < 0.01f)
                look = _pawn.transform.forward;
            bool firstPerson = !Dead && !ThirdPerson;
            if (firstPerson)
            {
                cam.transform.position = _eye.position;
                cam.transform.rotation = _eye.rotation;
                return;
            }
            Vector3 pivot = _pawn.transform.position + Vector3.up * 1.58f;
            float dist = Dead ? 3.6f : 3.35f;
            Vector3 shoulder = _pawn.transform.right * 0.58f + Vector3.up * 0.32f;
            Vector3 want = pivot - look * dist + shoulder;
            RaycastHit hit;
            Vector3 delta = want - pivot;
            float len = delta.magnitude;
            if (len > 0.05f
                && Physics.SphereCast(pivot, 0.16f, delta / len, out hit, len,
                    InfantryPerf.SightMask, QueryTriggerInteraction.Ignore))
                want = hit.point + hit.normal * 0.12f;
            cam.transform.position = want;
            cam.transform.rotation = Quaternion.LookRotation(look);
        }

        internal static void NudgeLook(float yawDelta, float pitchDelta)
        {
            _yaw += yawDelta;
            _pitch += pitchDelta;
            if (_pitch < -85f) _pitch = -85f;
            if (_pitch > 85f) _pitch = 85f;
            if (_pawn != null)
                _pawn.transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            if (_eye != null)
                _eye.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private static void LookMove()
        {
            float sens = _lookSens != null ? _lookSens.Value : 2.4f;
            sens *= InfantryCombat.LookSensMul();
            float mx = Input.GetAxis("Mouse X");
            float my = Input.GetAxis("Mouse Y");
            _yaw += mx * sens;
            _pitch -= my * sens;
            InfantryCombat.ApplyRecoilRecover(ref _yaw, ref _pitch, Time.deltaTime);
            if (_pitch < -85f) _pitch = -85f;
            if (_pitch > 85f) _pitch = 85f;
            if (_pawn != null)
                _pawn.transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            if (_eye != null)
                _eye.localRotation = Quaternion.Euler(_pitch, 0f, 0f);

            float walk = _walkSpeed != null ? _walkSpeed.Value : 4.6f;
            float sprint = _sprintSpeed != null ? _sprintSpeed.Value : 7.2f;
            bool wantSprint = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            bool burst = false;
            if (wantSprint)
            {
                if (InfantryWarnService.Overheated())
                {
                    if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift))
                        Deny(UiLang.T("Exo overheat — sprint locked", "外骨骼过热 — 无法冲刺"));
                }
                else
                    burst = true;
            }
            float speed = burst ? sprint : walk;
            Vector3 wish = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) wish += Vector3.forward;
            if (Input.GetKey(KeyCode.S)) wish += Vector3.back;
            if (Input.GetKey(KeyCode.A)) wish += Vector3.left;
            if (Input.GetKey(KeyCode.D)) wish += Vector3.right;
            if (wish.sqrMagnitude > 1f)
                wish.Normalize();
            Vector3 world = _pawn != null
                ? _pawn.transform.TransformDirection(wish) * speed
                : wish * speed;
            MoveSpeed = world.magnitude;
            Sprinting = burst && wish.sqrMagnitude > 0.01f;
            Strafe = 0f;
            if (Input.GetKey(KeyCode.A))
                Strafe -= 1f;
            if (Input.GetKey(KeyCode.D))
                Strafe += 1f;

            if (_cc == null)
                return;
            bool grounded = _cc.isGrounded;
            float dt = Time.deltaTime;
            if (_airborneHop && !grounded)
            {
                _airVel.y += Physics.gravity.y * dt;
                _airVel -= _airVel * (0.1f * dt);
                Vector3 steer = Vector3.zero;
                if (wish.sqrMagnitude > 0.01f)
                    steer = _pawn.transform.TransformDirection(wish) * 8f;
                _cc.Move((_airVel + steer) * dt);
                _vertVel = _airVel.y;
                MoveSpeed = new Vector3(_airVel.x, 0f, _airVel.z).magnitude;
                return;
            }
            if (_airborneHop && grounded)
            {
                float impact = _airVel.y;
                if (impact > _vertVel)
                    impact = _vertVel;
                if (impact < -28f)
                {
                    float crush = (-impact - 28f) * 6f;
                    if (crush > 4f)
                    {
                        Hp -= crush;
                        InfantryHud.FlashHit(UiLang.T("HARD LANDING", "重着陆"));
                        if (Hp <= 0f)
                        {
                            Hp = 0f;
                            Die();
                            return;
                        }
                    }
                }
                _airborneHop = false;
                _airVel = Vector3.zero;
            }
            if (grounded && _vertVel < 0f)
                _vertVel = -2f;
            if (grounded && Input.GetKeyDown(KeyCode.Space) && Time.time >= _jumpReadyAt)
            {
                float g = Mathf.Abs(Physics.gravity.y);
                if (g < 1f)
                    g = 9.81f;
                _vertVel = Mathf.Sqrt(2f * g * JumpHeight);
                _jumpReadyAt = Time.time + JumpCd;
                InfantryWarnService.NoteJump();
            }
            _vertVel += Physics.gravity.y * Time.deltaTime;
            world.y = _vertVel;
            _cc.Move(world * Time.deltaTime);
        }

        internal static bool BlocksAircraftInput
        {
            get { return Active; }
        }

        internal static bool AirHopActive
        {
            get { return _airborneHop; }
        }

        internal static void HoldFlightControls()
        {
            try { GameManager.flightControlsEnabled = false; }
            catch { }
        }

        internal static void SilenceAircraft(Aircraft ac, bool park)
        {
            if (ac == null)
                return;
            try
            {
                ControlInputs inputs = ac.GetInputs();
                if (inputs != null)
                {
                    inputs.pitch = 0f;
                    inputs.roll = 0f;
                    inputs.yaw = 0f;
                    inputs.customAxis1 = 0f;
                    if (park)
                    {
                        inputs.throttle = 0f;
                        inputs.brake = 1f;
                    }
                }
            }
            catch { }
            if (!park)
                return;
            int id = ac.GetInstanceID();
            if (id == _frozeRbId)
                return;
            _frozeRbId = id;
            try
            {
                if (ac.rb != null)
                {
                    ac.rb.velocity = Vector3.zero;
                    ac.rb.angularVelocity = Vector3.zero;
                }
            }
            catch { }
        }

        private static void FreezeAircraft()
        {
            SilenceAircraft(_parked, !_airborneHop);
        }

        private static void DetachInfantryCamera(CameraStateManager mgr)
        {
            if (_camDetached || mgr == null)
                return;
            _camDetached = true;
            try
            {
                if (mgr.cameraPivot != null)
                    mgr.cameraPivot.SetParent(null, true);
                mgr.transform.SetParent(null, true);
                mgr.cameraVelocity = Vector3.zero;
            }
            catch { }
        }

        private static void TryRestoreCockpitCam()
        {
            _camDetached = false;
            try
            {
                CameraStateManager mgr = SceneSingleton<CameraStateManager>.i;
                if (mgr == null)
                    return;
                if (!(mgr.followingUnit is Aircraft))
                    return;
                mgr.SwitchState(mgr.cockpitState);
            }
            catch { }
        }

        private static void RestoreCameraClip()
        {
            if (_infCam != null && _haveSavedCam && _savedNear > 0.001f)
            {
                try
                {
                    _infCam.nearClipPlane = _savedNear;
                    _infCam.useOcclusionCulling = _savedOcc;
                }
                catch { }
            }
            _infCam = null;
            _haveSavedCam = false;
            _savedNear = -1f;
        }

        private static void HideVanillaHud()
        {
            if (_hidVanillaHud)
                return;
            _hidVanillaHud = true;
            try { FlightHud.EnableCanvas(false); }
            catch { }
            try { DynamicMap.EnableCanvas(false); }
            catch { }
            try
            {
                CameraStateManager mgr = SceneSingleton<CameraStateManager>.i;
                if (mgr != null && mgr.cockpitCamRender != null)
                {
                    _savedCockpitCamOn = mgr.cockpitCamRender.enabled;
                    mgr.cockpitCamRender.enabled = false;
                }
            }
            catch { }
        }

        private static void RestoreVanillaHud()
        {
            if (!_hidVanillaHud)
                return;
            _hidVanillaHud = false;
            try
            {
                CameraStateManager mgr = SceneSingleton<CameraStateManager>.i;
                if (mgr != null && mgr.cockpitCamRender != null && _savedCockpitCamOn)
                    mgr.cockpitCamRender.enabled = true;
            }
            catch { }
            _savedCockpitCamOn = false;
            try { FlightHud.EnableCanvas(true); }
            catch { }
        }

        private static void Deny(string msg)
        {
            _deny = msg;
            _denyUntil = Time.unscaledTime + 3f;
        }

        private static void EnsureGui()
        {
            if (_hudStyle != null)
                return;
            _hudStyle = new GUIStyle(GUI.skin.label);
            _hudStyle.fontSize = 16;
            _hudStyle.normal.textColor = Color.white;
            _px = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _px.SetPixel(0, 0, Color.white);
            _px.Apply();
        }

        [HarmonyPatch(typeof(CameraStateManager), "LateUpdate")]
        private static class Patch_InfantryCamera
        {
            [HarmonyPrefix]
            [HarmonyPriority(Priority.First)]
            private static bool Prefix(CameraStateManager __instance)
            {
                if (!Active)
                    return true;
                try
                {
                    if (__instance != null)
                        __instance.CheckOriginShift();
                }
                catch { }
                ApplyCamera(__instance);
                return false;
            }
        }

        [HarmonyPatch(typeof(CameraStateManager), "FixedUpdate")]
        private static class Patch_InfantryCamFixed
        {
            [HarmonyPrefix]
            [HarmonyPriority(Priority.First)]
            private static bool Prefix()
            {
                return !Active;
            }
        }

        [HarmonyPatch(typeof(CombatHUD), "FixedUpdate")]
        private static class Patch_InfantryNoCombatHudFixed
        {
            [HarmonyPrefix]
            [HarmonyPriority(Priority.First)]
            private static bool Prefix()
            {
                return !Active;
            }
        }

        [HarmonyPatch(typeof(CameraFreeState), "UpdateState")]
        private static class Patch_InfantryNoFreeCam
        {
            [HarmonyPrefix]
            [HarmonyPriority(Priority.First)]
            private static bool Prefix()
            {
                return !Active;
            }
        }

        [HarmonyPatch(typeof(CameraControlledState), "UpdateState")]
        private static class Patch_InfantryNoCtrlCam
        {
            [HarmonyPrefix]
            [HarmonyPriority(Priority.First)]
            private static bool Prefix()
            {
                return !Active;
            }
        }

        [HarmonyPatch(typeof(CombatHUD), "LateUpdate")]
        private static class Patch_InfantryNoCombatHud
        {
            [HarmonyPrefix]
            [HarmonyPriority(Priority.First)]
            private static bool Prefix()
            {
                return !Active;
            }
        }

        [HarmonyPatch(typeof(FlightHud), "Update")]
        private static class Patch_InfantryNoFlightHud
        {
            [HarmonyPrefix]
            [HarmonyPriority(Priority.First)]
            private static bool Prefix()
            {
                return !Active;
            }
        }

        [HarmonyPatch(typeof(Aircraft), "StartEjectionSequence")]
        private static class Patch_InfantryBoardNoEject
        {
            [HarmonyPrefix]
            private static bool Prefix(Aircraft __instance)
            {
                if (__instance == null)
                    return true;
                if (InBoardGrace())
                    return false;
                if (Active)
                {
                    Aircraft local = null;
                    try { GameManager.GetLocalAircraft(out local); }
                    catch { }
                    if (local != null && object.ReferenceEquals(local, __instance))
                        return false;
                    if (_parked != null && object.ReferenceEquals(_parked, __instance))
                        return false;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(Aircraft), "SpawnEjectingPilot")]
        private static class Patch_InfantryBoardNoSpawnChute
        {
            [HarmonyPrefix]
            private static bool Prefix()
            {
                if (InBoardGrace())
                    return false;
                return true;
            }
        }
    }
}
