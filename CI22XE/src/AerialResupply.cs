using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Aerial remote energy top-up: F10 menu for fuel/battery targets + speed tier.
    /// Subsidiary tab: 维修组件 / Repair Parts (damaged UnitPart / engines / fuel).
    /// Max price 1M. Holds level flight until done or Stop.
    /// </summary>
    internal static class AerialResupply
    {
        private static readonly FieldInfo ChargeField =
            AccessTools.Field(typeof(PowerSupply), "charge");
        private static readonly FieldInfo MaxChargeField =
            AccessTools.Field(typeof(PowerSupply), "maxCharge");

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<KeyCode> _menuKey;
        private static ConfigEntry<KeyCode> _stopKey;

        private static bool _menuOpen;
        /// <summary>0 = resupply, 1 = repair components.</summary>
        private static int _tab;
        private static bool _active;
        private static float _targetFuel = 1f;
        private static float _targetBatt = 1f;
        private static int _speedTier; // 0 slow, 1 mid, 2 fast
        private static float _paidCost;
        private static float _progress; // 0..1 work done toward targets
        private static float _startFuel;
        private static float _startBatt;
        private static string _status = string.Empty;
        private static float _statusUntil;
        private static bool _savedFlight = true;
        private static bool _hasSavedFlight;
        private static bool _wasApEngaged;
        private static bool _cursorHeld;

        private static GUIStyle _titleStyle;
        private static GUIStyle _labelStyle;
        private static GUIStyle _bannerStyle;
        private static GUIStyle _btnStyle;
        private static GUIStyle _chipHintStyle;

        // Rate = fraction of full tank per second; priceMul scales toward 1M
        private static readonly float[] Rates = AerialResupplyMathService.Rates;
        private static readonly float[] PriceMul = AerialResupplyMathService.PriceMul;
        private static readonly string[] SpeedLabels = { "SLOW", "MED", "FAST" };

        internal static bool IsActive
        {
            get { return _active; }
        }

        internal static bool MenuOpen
        {
            get { return _menuOpen; }
        }

        internal static ConfigEntry<KeyCode> StopKey
        {
            get { return _stopKey; }
        }

        internal static void CloseMenuFromOutside()
        {
            CloseMenu();
        }

        internal static void AbortActiveFromOutside()
        {
            if (_active)
                StopResupply(true, "ABORTED");
            CloseMenu();
        }

        internal static void Bind(ConfigFile config)
        {
            if (config == null)
                return;
            _enabled = config.Bind("AerialResupply", "Enabled", true,
                "F10 aerial remote fuel/battery top-up.");
            _menuKey = config.Bind("AerialResupply", "MenuKey", KeyCode.F10,
                "Open/close aerial resupply menu.");
            _stopKey = config.Bind("AerialResupply", "StopKey", KeyCode.Backspace,
                "Stop active aerial resupply.");
        }

        internal static void Tick()
        {
            if (_enabled == null || !_enabled.Value)
                return;

            KeyCode menu = _menuKey != null ? _menuKey.Value : KeyCode.F10;
            KeyCode stop = _stopKey != null ? _stopKey.Value : KeyCode.Backspace;

            if (Input.GetKeyDown(menu))
            {
                if (_menuOpen)
                    CloseMenu();
                else
                    OpenMenu();
            }

            if (_active && Input.GetKeyDown(stop))
            {
                StopResupply(true, "STOPPED");
            }

            if (_menuOpen && Input.GetKeyDown(KeyCode.Escape))
                CloseMenu();

            Aircraft ac = ResolveLocal();
            if (_active)
            {
                if (ac == null || ac.disabled || MissileCameraHud.ManualActive)
                    StopResupply(true, "ABORTED");
                else
                    TickResupply(ac);
            }
        }

        internal static void ApplyLevelFlight(Aircraft ac)
        {
            if (!_active || ac == null || ac.autopilot == null)
                return;
            try
            {
                ControlInputs inputs = ac.GetInputs();
                AircraftParameters parms = ac.GetAircraftParameters();
                float cruise = parms != null ? parms.cruiseThrottle : 0.7f;
                float corner = parms != null ? parms.cornerSpeed : 120f;
                float hold = Mathf.Max(200f, ac.radarAlt > 50f ? ac.radarAlt : 600f);

                // Locked ground track — never chase banked nose (causes left/right rocking).
                Vector3 track = ac.rb != null && ac.rb.velocity.sqrMagnitude > 25f
                    ? ac.rb.velocity : ac.transform.forward;
                track.y = 0f;
                if (track.sqrMagnitude < 0.01f)
                    track = Vector3.forward;
                track.Normalize();
                Vector3 aim = ac.transform.position + track * 15000f;
                aim.y = ac.transform.position.y;

                try
                {
                    if (!ac.flightAssist)
                        ac.SetFlightAssist(true);
                }
                catch { }

                Autopilot ap = ac.autopilot;
                AutopilotPlane plane = ap as AutopilotPlane;
                if (plane != null)
                {
                    plane.AutoAim(aim.ToGlobalPosition(), true, false, false, 0.95f,
                        AutopilotAim.CruiseBank, true, hold, Vector3.zero);
                }
                else
                {
                    ap.AutoAim(aim.ToGlobalPosition(), hold, track, Vector3.zero, true);
                }

                if (inputs != null)
                {
                    float err = ac.speed - corner * 0.85f;
                    inputs.throttle = Mathf.Clamp(0.55f - err * 0.02f, 0.3f, cruise);
                    inputs.brake = 0f;
                }
                GameManager.flightControlsEnabled = false;
            }
            catch { }
        }

        internal static void DrawGui()
        {
            if (_enabled == null || !_enabled.Value)
                return;

            EnsureStyles();
            if (Plugin.AllowThirdPersonUi)
                DrawCornerHint();
            if (Plugin.AllowThirdPersonUi)
                DrawBanner();

            if (_menuOpen)
            {
                HoldCursor();
                DrawMenu();
            }
        }

        private static void DrawCornerHint()
        {
            Aircraft ac = ResolveLocal();
            if (ac == null || MissileCameraHud.ManualActive)
                return;
            if (Event.current != null && Event.current.type != EventType.Repaint)
                return;

            // Tip stack: F1 F2 F3 F9 F10 F11
            Rect chip = PlayerAutopilot.CornerChipRect(AssistMenuLayoutService.SlotF10);
            Color prev = GUI.color;
            GUI.color = new Color(0.05f, 0.07f, 0.1f, 0.78f);
            GUI.DrawTexture(chip, Texture2D.whiteTexture);
            GUI.color = _active
                ? new Color(0.35f, 0.95f, 0.55f, 0.95f)
                : new Color(0.55f, 0.85f, 1f, 0.9f);
            GUI.DrawTexture(new Rect(chip.x, chip.y, chip.width, 2f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            string line = AssistStatusFormatService.ResupplyChip(
                _active, Mathf.RoundToInt(_progress * 100f));
            if (_chipHintStyle == null)
            {
                _chipHintStyle = new GUIStyle(GUI.skin.label);
                _chipHintStyle.fontSize = 11;
                _chipHintStyle.fontStyle = FontStyle.Bold;
                _chipHintStyle.alignment = TextAnchor.MiddleRight;
                _chipHintStyle.normal.textColor = new Color(0.8f, 0.95f, 1f, 0.95f);
            }
            _chipHintStyle.normal.textColor = _active
                ? new Color(0.7f, 1f, 0.8f, 0.98f)
                : new Color(0.8f, 0.95f, 1f, 0.95f);
            GUI.Label(new Rect(chip.x + 6f, chip.y, chip.width - 12f, chip.height), line, _chipHintStyle);
            GUI.color = prev;
        }

        private static void OpenMenu()
        {
            if (MissileCameraHud.ManualActive)
            {
                Flash(UiLang.T("Exit man-in-the-loop first", "请先退出人在回路"));
                return;
            }
            Aircraft ac = ResolveLocal();
            if (ac == null)
            {
                Flash(UiLang.T("Need aircraft", "需要飞机"));
                return;
            }
            if (PlayerAutopilot.MenuOpen)
                PlayerAutopilot.CloseMenuFromOutside();
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
            PlayerAutopilot.CloseWeXonSupportMenu();
            _menuOpen = true;
            CaptureCursor();
            // Prefill targets to full; keep speed
            try
            {
                float fuel = ac.GetFuelLevel();
                float batt = ReadBattery(ac);
                if (_targetFuel < fuel)
                    _targetFuel = 1f;
                if (_targetBatt < batt)
                    _targetBatt = 1f;
            }
            catch { }
        }

        private static void CloseMenu()
        {
            _menuOpen = false;
            ComponentRepair.ResetUi();
            ReleaseCursor();
        }

        private static void DrawMenu()
        {
            Aircraft ac = ResolveLocal();
            Rect box = AerialSupportLayoutService.MenuRect(UiScaleService.Width, UiScaleService.Height, _tab == 1);
            Color prev = GUI.color;
            GUI.color = new Color(0.06f, 0.08f, 0.1f, 0.92f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = new Color(0.35f, 0.85f, 1f, 0.95f);
            GUI.DrawTexture(new Rect(box.x, box.y, box.width, 3f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(box.x + 16f, box.y + 8f, box.width - 32f, 24f),
                UiLang.T("F10 AERIAL SUPPORT", "F10 空中补给"), _titleStyle);

            // Tabs: Resupply | 维修组件
            float tabY = box.y + 36f;
            float tabW = (box.width - 40f) * 0.5f;
            DrawTab(new Rect(box.x + 16f, tabY, tabW, 26f), 0,
                UiLang.T("RESUPPLY", "空中补给"));
            DrawTab(new Rect(box.x + 24f + tabW, tabY, tabW, 26f), 1,
                UiLang.T("REPAIR PARTS", "维修组件"));

            if (_tab == 1)
            {
                // Shift content down under tabs — ComponentRepair uses box.y+48 as content start.
                Rect repairBox = AerialSupportLayoutService.RepairContentRect(box);
                ComponentRepair.DrawPanel(repairBox, ac, _titleStyle, _labelStyle);
                GUI.color = prev;
                return;
            }

            float curFuel = 0f;
            float curBatt = 0f;
            float funds = 0f;
            if (ac != null)
            {
                try { curFuel = ac.GetFuelLevel(); }
                catch { }
                curBatt = ReadBattery(ac);
            }
            try
            {
                Player p;
                if (GameManager.GetLocalPlayer(out p) && p != null)
                    funds = p.Allocation;
            }
            catch { }

            float cost = EstimateCost(curFuel, curBatt);
            float y = box.y + 72f;
            GUI.Label(new Rect(box.x + 16f, y, box.width - 32f, 20f),
                UiLang.T(
                    "Fuel target  " + Mathf.RoundToInt(_targetFuel * 100f) + "%  (now "
                    + Mathf.RoundToInt(curFuel * 100f) + "%)",
                    "燃油目标  " + Mathf.RoundToInt(_targetFuel * 100f) + "%  当前 "
                    + Mathf.RoundToInt(curFuel * 100f) + "%"), _labelStyle);
            y += 22f;
            _targetFuel = AerialResupplyMathService.SnapTarget01(
                GUI.HorizontalSlider(new Rect(box.x + 16f, y, box.width - 32f, 18f),
                _targetFuel, 0f, 1f)); // 5% steps
            y += 28f;

            GUI.Label(new Rect(box.x + 16f, y, box.width - 32f, 20f),
                UiLang.T(
                    "Battery target  " + Mathf.RoundToInt(_targetBatt * 100f) + "%  (now "
                    + Mathf.RoundToInt(curBatt * 100f) + "%)",
                    "电池目标  " + Mathf.RoundToInt(_targetBatt * 100f) + "%  当前 "
                    + Mathf.RoundToInt(curBatt * 100f) + "%"), _labelStyle);
            y += 22f;
            _targetBatt = AerialResupplyMathService.SnapTarget01(
                GUI.HorizontalSlider(new Rect(box.x + 16f, y, box.width - 32f, 18f),
                _targetBatt, 0f, 1f));
            y += 32f;

            GUI.Label(new Rect(box.x + 16f, y, box.width - 32f, 20f),
                UiLang.T("Speed  (faster = more expensive, max 1.0M)",
                    "速度  越快越贵，上限 1.0M"), _labelStyle);
            y += 24f;
            float bw = (box.width - 48f) / 3f;
            for (int i = 0; i < 3; i++)
            {
                Rect br = new Rect(box.x + 16f + i * (bw + 8f), y, bw, 28f);
                Color c = _speedTier == i
                    ? new Color(0.25f, 0.7f, 0.4f, 0.95f)
                    : new Color(0.2f, 0.25f, 0.3f, 0.9f);
                GUI.color = c;
                GUI.DrawTexture(br, Texture2D.whiteTexture);
                GUI.color = Color.white;
                if (GUI.Button(br, SpeedLabel(i) + "  x" + PriceMul[i].ToString("0.00"), _btnStyle))
                    _speedTier = i;
            }
            y += 40f;

            GUI.Label(new Rect(box.x + 16f, y, box.width - 32f, 20f),
                UiLang.T(
                    "Est. cost  " + cost.ToString("0.00") + "M   |   Funds  " + funds.ToString("0.0") + "M",
                    "预计  " + cost.ToString("0.00") + "M   |   资金  " + funds.ToString("0.0") + "M"),
                _labelStyle);
            y += 28f;

            if (_active)
            {
                if (GUI.Button(new Rect(box.x + 16f, y, box.width - 32f, 36f),
                    UiLang.T("STOP RESUPPLY  (Backspace)", "停止补给"), _btnStyle))
                    StopResupply(true, "STOPPED");
            }
            else
            {
                if (GUI.Button(new Rect(box.x + 16f, y, box.width - 32f, 36f),
                    UiLang.T("START  (-" + cost.ToString("0.00") + "M)",
                        "开始  (-" + cost.ToString("0.00") + "M)"), _btnStyle))
                    TryStart(ac, cost);
            }
            y += 44f;

            string hint = _active
                ? UiLang.T("Level flight locked · remote top-up in progress",
                    "已锁定平飞 · 远程补给中")
                : UiLang.T("Start holds level flight until done / Backspace",
                    "开始后锁定平飞至完成 / Backspace 停止");
            GUI.Label(new Rect(box.x + 16f, y, box.width - 32f, 36f), hint, _labelStyle);

            if (!string.IsNullOrEmpty(_status) && Time.unscaledTime < _statusUntil)
            {
                GUI.Label(new Rect(box.x + 16f, box.yMax - 28f, box.width - 32f, 20f),
                    _status, _labelStyle);
            }

            GUI.color = prev;
        }

        private static void DrawTab(Rect r, int index, string label)
        {
            bool on = _tab == index;
            Color prev = GUI.color;
            GUI.color = on
                ? new Color(0.2f, 0.55f, 0.4f, 0.95f)
                : new Color(0.18f, 0.22f, 0.28f, 0.9f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
            if (GUI.Button(r, label, _btnStyle))
                _tab = index;
            GUI.color = prev;
        }

        private static bool UiZh()
        {
            return UiLang.IsChinese;
        }

        private static string SpeedLabel(int i)
        {
            switch (i)
            {
                case 0: return UiLang.T("SLOW", "慢");
                case 1: return UiLang.T("MED", "中");
                case 2: return UiLang.T("FAST", "快");
                default: return "?";
            }
        }

        private static string ReasonLabel(string reason)
        {
            if (reason == "ABORTED")
                return UiLang.T("ABORTED", "已中止");
            if (reason == "STOPPED")
                return UiLang.T("STOPPED", "已停止");
            if (reason == "DONE")
                return UiLang.T("DONE", "完成");
            if (reason == "COMPLETE")
                return UiLang.T("COMPLETE", "补给完成");
            return reason != null ? reason : "";
        }

        private static void DrawBanner()
        {
            if (!_active && (string.IsNullOrEmpty(_status) || Time.unscaledTime >= _statusUntil))
                return;
            if (Event.current != null && Event.current.type != EventType.Repaint)
                return;

            EnsureStyles();
            string text;
            Color col;
            if (_active)
            {
                text = UiLang.T(
                    "REMOTE RESUPPLY  "
                    + Mathf.RoundToInt(_progress * 100f) + "%  ·  FUEL/BATT  ·  LEVEL HOLD  ·  Backspace STOP",
                    "空中补给  "
                    + Mathf.RoundToInt(_progress * 100f) + "%  ·  燃油/电池  ·  平飞锁定  ·  Backspace 停止");
                col = new Color(0.25f, 0.95f, 0.55f, 0.98f);
                bool flash = (Time.unscaledTime % 0.8f) < 0.45f;
                if (!flash)
                    col = new Color(0.9f, 1f, 0.95f, 0.95f);
            }
            else
            {
                text = _status;
                col = new Color(0.85f, 0.9f, 1f, 0.95f);
            }

            Rect r = AerialSupportLayoutService.StatusBannerRect(UiScaleService.Width, UiScaleService.Height);
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = col;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 2f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            _bannerStyle.normal.textColor = col;
            GUI.Label(r, text, _bannerStyle);
            GUI.color = prev;
        }

        private static void TryStart(Aircraft ac, float cost)
        {
            if (ac == null)
            {
                Flash(UiLang.T("Need aircraft", "需要飞机"));
                return;
            }
            if (_active)
                return;
            if (MissileCameraHud.ManualActive)
            {
                Flash(UiLang.T("Exit man-in-the-loop first", "请先退出人在回路"));
                return;
            }

            float curFuel = 0f;
            float curBatt = 0f;
            try { curFuel = ac.GetFuelLevel(); }
            catch { }
            curBatt = ReadBattery(ac);

            float fuelNeed = Mathf.Max(0f, _targetFuel - curFuel);
            float battNeed = Mathf.Max(0f, _targetBatt - curBatt);
            if (fuelNeed < 0.005f && battNeed < 0.005f)
            {
                Flash(UiLang.T("Already at / above targets", "已达或超过目标"));
                return;
            }

            cost = EstimateCost(curFuel, curBatt);
            bool coupon = KillChoiceRewardService.HasFreeResupply;
            if (coupon)
                cost = 0f;
            else if (cost < 0.001f)
            {
                Flash(UiLang.T("Nothing to buy", "无需购买"));
                return;
            }

            Player local;
            if (!GameManager.GetLocalPlayer(out local) || local == null)
            {
                Flash(UiLang.T("No local player", "无本地玩家"));
                return;
            }
            if (!coupon && local.Allocation + 0.001f < cost)
            {
                Flash(UiLang.T(
                    "Need " + cost.ToString("0.00") + "M (have " + local.Allocation.ToString("0.0") + "M)",
                    "需要 " + cost.ToString("0.00") + "M  现有 " + local.Allocation.ToString("0.0") + "M"));
                return;
            }

            if (!coupon)
            {
                try
                {
                    // Host/listen: AddAllocation; clients may fail — try anyway
                    local.AddAllocation(-cost);
                }
                catch (Exception ex)
                {
                    Flash(UiLang.T("Pay failed: " + ex.Message, "支付失败：" + ex.Message));
                    if (Plugin.Log != null)
                        Plugin.Log.LogWarning("AerialResupply pay: " + ex.Message);
                    return;
                }
            }
            else
                KillChoiceRewardService.ConsumeFreeResupply();

            _paidCost = cost;
            _startFuel = curFuel;
            _startBatt = curBatt;
            _progress = 0f;
            _active = true;
            _wasApEngaged = PlayerAutopilot.IsEngaged;

            if (!_hasSavedFlight)
            {
                _savedFlight = GameManager.flightControlsEnabled;
                _hasSavedFlight = true;
            }
            GameManager.flightControlsEnabled = false;
            if (coupon)
            {
                Flash(UiLang.T("STARTED (F6 coupon)", "已开始（F6 券）"));
            }
            else
            {
                Flash(UiLang.T("STARTED -" + cost.ToString("0.00") + "M",
                    "已开始 -" + cost.ToString("0.00") + "M"));
            }
            if (Plugin.Log != null)
                Plugin.Log.LogInfo("AerialResupply start cost=" + cost + "M tier=" + _speedTier);
        }

        private static void TickResupply(Aircraft ac)
        {
            float rate = AerialResupplyMathService.FillRate(_speedTier);
            float dt = Time.deltaTime;
            float fuelNeed0 = Mathf.Max(0f, _targetFuel - _startFuel);
            float battNeed0 = Mathf.Max(0f, _targetBatt - _startBatt);
            float totalNeed = fuelNeed0 + battNeed0;
            if (totalNeed < 0.0001f)
            {
                StopResupply(false, "DONE");
                return;
            }

            // Advance fuel
            float curFuel = 0f;
            try { curFuel = ac.GetFuelLevel(); }
            catch { }
            if (curFuel < _targetFuel - 0.001f)
            {
                float next = Mathf.MoveTowards(curFuel, _targetFuel, rate * dt);
                ApplyFuelRatio(ac, next);
            }

            // Advance battery
            float curBatt = ReadBattery(ac);
            if (curBatt < _targetBatt - 0.001f)
                ApplyBatteryRatio(ac, Mathf.MoveTowards(curBatt, _targetBatt, rate * dt));

            try { curFuel = ac.GetFuelLevel(); }
            catch { }
            curBatt = ReadBattery(ac);

            float fuelDone = AerialResupplyMathService.ProgressFraction(curFuel, _startFuel, fuelNeed0);
            float battDone = AerialResupplyMathService.ProgressFraction(curBatt, _startBatt, battNeed0);
            _progress = (fuelDone + battDone) * 0.5f;

            bool fuelOk = AerialResupplyMathService.TargetReached(curFuel, _targetFuel, fuelNeed0);
            bool battOk = AerialResupplyMathService.TargetReached(curBatt, _targetBatt, battNeed0);
            if (fuelOk && battOk)
                StopResupply(false, "COMPLETE");
        }

        private static void StopResupply(bool refundPartial, string reason)
        {
            if (!_active)
            {
                Flash(reason);
                return;
            }

            float refund = 0f;
            if (refundPartial && _paidCost > 0f && _progress < 0.99f)
            {
                refund = _paidCost * (1f - Mathf.Clamp01(_progress));
                try
                {
                    Player local;
                    if (GameManager.GetLocalPlayer(out local) && local != null && refund > 0.001f)
                        local.AddAllocation(refund);
                }
                catch { }
            }

            _active = false;
            if (_hasSavedFlight && !PlayerAutopilot.IsEngaged)
            {
                GameManager.flightControlsEnabled = _savedFlight;
                _hasSavedFlight = false;
            }
            else if (PlayerAutopilot.IsEngaged)
            {
                GameManager.flightControlsEnabled = false;
                _hasSavedFlight = false;
            }

            string msg = ReasonLabel(reason);
            if (refund > 0.001f)
                msg += UiLang.T("  refund +" + refund.ToString("0.00") + "M",
                    "  退款 +" + refund.ToString("0.00") + "M");
            Flash(msg);
            _paidCost = 0f;
            _wasApEngaged = false;
        }

        private static float EstimateCost(float curFuel, float curBatt)
        {
            return AerialResupplyMathService.EstimateCost(
                curFuel, curBatt, _targetFuel, _targetBatt, _speedTier);
        }

        internal static void FillFuelFromOutside(Aircraft ac)
        {
            ApplyFuelRatio(ac, 1f);
        }

        internal static void FillBatteryFromOutside(Aircraft ac)
        {
            ApplyBatteryRatio(ac, 1f);
        }

        private static void ApplyFuelRatio(Aircraft ac, float ratio)
        {
            if (ac == null)
                return;
            ratio = Mathf.Clamp01(ratio);
            try
            {
                List<FuelTank> tanks = ac.GetFuelTanks();
                if (tanks == null)
                    return;
                for (int i = 0; i < tanks.Count; i++)
                {
                    FuelTank t = tanks[i];
                    if (t == null)
                        continue;
                    try { t.Refuel(ratio); }
                    catch { }
                }
                try { ac.NetworkfuelLevel = ratio; }
                catch
                {
                    try { ac.fuelLevel = ratio; }
                    catch { }
                }
            }
            catch { }
        }

        private static void ApplyBatteryRatio(Aircraft ac, float ratio)
        {
            if (ac == null || ChargeField == null || MaxChargeField == null)
                return;
            ratio = Mathf.Clamp01(ratio);
            try
            {
                PowerSupply ps = ac.GetPowerSupply();
                if (ps == null)
                    return;
                if (ratio >= 0.999f)
                {
                    ps.SetFullyCharged();
                    return;
                }
                float max = (float)MaxChargeField.GetValue(ps);
                if (max <= 0f)
                    return;
                ChargeField.SetValue(ps, max * ratio);
                try { ps.enabled = true; }
                catch { }
            }
            catch { }
        }

        private static float ReadBattery(Aircraft ac)
        {
            try
            {
                PowerSupply ps = ac.GetPowerSupply();
                if (ps != null)
                    return Mathf.Clamp01(ps.GetCharge());
            }
            catch { }
            return 1f;
        }

        private static void Flash(string msg)
        {
            _status = msg;
            _statusUntil = Time.unscaledTime + 3.5f;
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
            if (_titleStyle != null)
                return;
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

            _bannerStyle = new GUIStyle(GUI.skin.label);
            _bannerStyle.fontSize = 14;
            _bannerStyle.fontStyle = FontStyle.Bold;
            _bannerStyle.alignment = TextAnchor.MiddleCenter;
            _bannerStyle.normal.textColor = Color.white;

            _btnStyle = new GUIStyle(GUI.skin.button);
            _btnStyle.fontSize = 13;
            _btnStyle.fontStyle = FontStyle.Bold;
            _btnStyle.alignment = TextAnchor.MiddleCenter;
            _btnStyle.normal.textColor = Color.white;
        }
    }
}
