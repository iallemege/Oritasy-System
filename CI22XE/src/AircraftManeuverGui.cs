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
    /// F1 Oritasy System GUI — draft values, dirty flag, deferred apply, Escape to close.
    /// Each airframe type has its own Maneuver.* config section.
    /// </summary>
    internal static class AircraftManeuverGui
    {
        private enum UiState
        {
            Closed = 0,
            Empty = 1,
            Editing = 2
        }

        private static UiState _state = UiState.Closed;
        private static bool _cursorHeld;

        private static string _boundKey = string.Empty;
        private static ManeuverProfile _profile;
        private static Aircraft _aircraft;

        private static float _dAircraftG;
        private static float _dPilotG;
        private static float _dPilotStrength;
        private static float _dMaxSpeed;
        private static float _dCornerSpeed;
        private static float _dPitchMul;
        private static float _dRollMul;
        private static float _dAlphaMul;
        private static float _dThrustMul;
        private static float _dFuelBurnMul;
        private static float _dFuelCapMul;
        private static float _dApproach;
        private static float _dLanding;
        private static float _dTakeoff;
        private static float _dTurnRadius;
        private static bool _dirty;
        private static float _nextDeferredApply = -1f;
        private static string _status = string.Empty;
        private static float _statusUntil;
        private static Vector2 _scroll;

        private static GUIStyle _titleStyle;
        private static GUIStyle _labelStyle;
        private static GUIStyle _sectionStyle;
        private static GUIStyle _btnStyle;
        private static GUIStyle _f1HintStyle;

        /// <summary>0 = System, 1 = Keys, 2 = Flight Score.</summary>
        private static int _tab;
        private static ConfigEntry<KeyCode> _rebindEntry;

        internal static bool IsOpen
        {
            get { return _state != UiState.Closed; }
        }

        internal static bool IsFlightScoreTab
        {
            get { return IsOpen && _tab == 2; }
        }

        internal static bool IsRebinding
        {
            get { return _rebindEntry != null; }
        }

        internal static void TickInput()
        {
            KeyCode toggle = Plugin.GuiToggleKey != null ? Plugin.GuiToggleKey.Value : KeyCode.F1;
            if (Input.GetKeyDown(KeyCode.Escape) && IsOpen)
            {
                if (_rebindEntry != null)
                {
                    _rebindEntry = null;
                    return;
                }
                Close();
                return;
            }
            if (Input.GetKeyDown(toggle))
            {
                if (_rebindEntry != null)
                    return;
                if (IsOpen)
                    Close();
                else
                    Open();
            }
        }

        /// <summary>Open F1 System on the Flight Score tab (auto crash/leave or legacy analyze key).</summary>
        internal static void OpenToFlightScore()
        {
            FlightScoreBridge.PrepareDisplay();
            if (!IsOpen)
                Open();
            _tab = 2;
            FlightScoreBridge.CloseStandalonePanel();
        }

        internal static void TickDeferredApply()
        {
            if (_nextDeferredApply < 0f || Time.unscaledTime < _nextDeferredApply)
                return;
            _nextDeferredApply = -1f;
            if (_state == UiState.Editing && _dirty)
                Commit(true);
        }

        internal static void Open()
        {
            if (IsOpen)
                return;
            if (PlayerAutopilot.MenuOpen)
                PlayerAutopilot.CloseMenuFromOutside();
            if (AerialResupply.MenuOpen)
                AerialResupply.CloseMenuFromOutside();
            if (HudPackState.LayoutMenuOpen)
                HudPackState.CloseRwr();
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
            HudPackState.CloseWear();
            PlayerAutopilot.CloseWeXonSupportMenu();
            FlightScoreBridge.CloseStandalonePanel();
            CaptureCursor();
            RefreshBinding(true);
            _tab = 0;
            _state = (_aircraft != null && _profile != null) ? UiState.Editing : UiState.Empty;
        }

        internal static void Close()
        {
            if (!IsOpen)
                return;
            if (_dirty && (Plugin.GuiAutoApply == null || !Plugin.GuiAutoApply.Value))
                ReloadDraftFromProfile();
            _state = UiState.Closed;
            _tab = 0;
            _rebindEntry = null;
            _nextDeferredApply = -1f;
            ReleaseCursor();
        }

        internal static void Draw()
        {
            // Tip chip is optional chrome; F1 key always opens Oritasy System.
            if (Plugin.AllowThirdPersonUi)
                DrawCornerHint();

            if (!IsOpen)
                return;

            HoldCursor();
            RefreshBinding(false);
            EnsureMenuStyles();
            PollKeyRebind();
            DrawPanel();
        }

        private static void DrawCornerHint()
        {
            Aircraft ac = null;
            try { GameManager.GetLocalAircraft(out ac); }
            catch { }
            if (ac == null || HudPackState.ManualActive)
                return;
            if (Event.current != null && Event.current.type != EventType.Repaint)
                return;

            EnsureMenuStyles();
            float chipW = 248f;
            float chipH = PlayerAutopilot.CornerChipH;
            // Tip stack: F1 F2 F3 F9 F10 F11
            Rect chip = new Rect(UiScaleService.Width - chipW - 18f, PlayerAutopilot.CornerChipY(0), chipW, chipH);
            Color prev = GUI.color;
            GUI.color = new Color(0.05f, 0.07f, 0.1f, 0.78f);
            GUI.DrawTexture(chip, Texture2D.whiteTexture);
            GUI.color = IsOpen
                ? new Color(0.95f, 0.85f, 0.35f, 0.95f)
                : new Color(0.75f, 0.9f, 0.45f, 0.9f);
            GUI.DrawTexture(new Rect(chip.x, chip.y, chip.width, 2f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            string line = IsOpen
                ? (IsFlightScoreTab
                    ? UiLang.T("F1 System  |  SCORE", "F1 系统  |  评分")
                    : UiLang.T("F1 System  |  OPEN", "F1 系统  |  已打开"))
                : UiLang.T("F1 System  |  limits / score", "F1 系统  |  包线 / 评分");
            GUI.Label(new Rect(chip.x + 6f, chip.y, chip.width - 12f, chip.height), line, _f1HintStyle);
            GUI.color = prev;
        }

        private static void DrawPanel()
        {
            Rect box = ManeuverGuiLayoutService.PanelRect(UiScaleService.Width, UiScaleService.Height);
            Color prev = GUI.color;
            GUI.color = new Color(0.06f, 0.08f, 0.1f, 0.92f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = new Color(0.85f, 0.9f, 0.4f, 0.95f);
            GUI.DrawTexture(new Rect(box.x, box.y, box.width, 3f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            string keyHint = Plugin.GuiToggleKey != null ? Plugin.GuiToggleKey.Value.ToString() : "F1";
            string acName = (_state == UiState.Editing && _profile != null && !string.IsNullOrEmpty(_profile.DisplayLabel))
                ? _profile.DisplayLabel
                : UiLang.T("Global", "全局");
            GUI.Label(new Rect(box.x + 16f, box.y + 12f, box.width - 32f, 26f),
                UiLang.T("ORITASY SYSTEM  (" + keyHint + ")", "ORITASY 系统"), _titleStyle);
            GUI.Label(new Rect(box.x + 16f, box.y + 36f, box.width - 32f, 18f),
                acName, _labelStyle);

            float tabY = box.y + 56f;
            float tabW = (box.width - 48f) / 3f;
            DrawF1Tab(new Rect(box.x + 16f, tabY, tabW, 26f), 0,
                UiLang.T("SYSTEM", "系统"));
            DrawF1Tab(new Rect(box.x + 20f + tabW, tabY, tabW, 26f), 1,
                UiLang.T("KEYS", "键位"));
            DrawF1Tab(new Rect(box.x + 24f + tabW * 2f, tabY, tabW, 26f), 2,
                UiLang.T("SCORE", "评分"));

            Rect body = new Rect(box.x + 12f, box.y + 90f, box.width - 24f, box.height - 102f);
            GUILayout.BeginArea(body);
            _scroll = GUILayout.BeginScrollView(_scroll, false, true);

            if (_tab == 2)
            {
                if (!FlightScoreBridge.DrawEmbedded(_titleStyle, _labelStyle, _sectionStyle, _btnStyle))
                    GUILayout.Label(UiLang.T("Flight score unavailable.", "飞行评分不可用。"), _labelStyle);
                GUILayout.Space(10f);
                if (GUILayout.Button(UiLang.T("Close", "关闭"), _btnStyle, GUILayout.Height(32f)))
                    Close();
                GUILayout.Label(UiLang.T(
                    "Esc / " + keyHint + " closes · auto-opens on crash / leave",
                    "Esc / " + keyHint + " 关闭 · 坠毁或离机时自动打开"), _labelStyle);
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                GUI.color = prev;
                return;
            }

            if (_tab == 1)
            {
                DrawKeysPanel();
                GUILayout.Space(10f);
                if (GUILayout.Button(UiLang.T("Close", "关闭"), _btnStyle, GUILayout.Height(32f)))
                    Close();
                GUILayout.Label(UiLang.T(
                    "Menu keys F1–F11 stay fixed · click a key then press a new one · Esc cancels",
                    "F1–F11 菜单键不改 · 点按键后再按新键 · Esc 取消"), _labelStyle);
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                GUI.color = prev;
                return;
            }

            DrawPackOptions();
            GUILayout.Space(10f);

            if (_state != UiState.Editing || _aircraft == null || _profile == null)
            {
                GUILayout.Label(UiLang.T("AIRCRAFT", "飞机"), _sectionStyle);
                GUILayout.Label(UiLang.T("No local XE aircraft.", "未找到本地 XE 飞机。"), _labelStyle);
                GUILayout.Label(UiLang.T("Enter an Oritasy / XE airframe for per-plane tuning.",
                    "进入 Oritasy / XE 机型后可进行单机调试。"), _labelStyle);
                GUILayout.Space(8f);
                if (GUILayout.Button(UiLang.T("Close", "关闭"), _btnStyle, GUILayout.Height(32f)))
                    Close();
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                GUI.color = prev;
                return;
            }

            Plugin.EnsureControlBaselines(_aircraft, _profile);

            GUILayout.Label(UiLang.T("AIRCRAFT  ·  ", "飞机  ·  ") + _profile.DisplayLabel, _sectionStyle);
            GUILayout.Label(UiLang.T("Profile  ", "配置  ") + _profile.Key, _labelStyle);
            if (_dirty)
                GUILayout.Label(_nextDeferredApply > 0f
                    ? UiLang.T("Status: pending apply…", "状态：等待应用…")
                    : UiLang.T("Status: unsaved changes", "状态：有未保存更改"), _labelStyle);
            else if (!string.IsNullOrEmpty(_status) && Time.unscaledTime < _statusUntil)
                GUILayout.Label(_status, _labelStyle);
            else
                GUILayout.Label(UiLang.T("Status: applied", "状态：已应用"), _labelStyle);

            GUILayout.Space(8f);
            GUILayout.Label(UiLang.T("FLIGHT ENVELOPE", "飞行包线"), _sectionStyle);
            EditSlider(UiLang.T("Aircraft G", "飞机过载"), ref _dAircraftG,
                ManeuverGuiLayoutService.AircraftGMin, ManeuverGuiLayoutService.AircraftGMax, "0.0", " G");
            EditSlider(UiLang.T("Max speed", "最大速度"), ref _dMaxSpeed,
                ManeuverGuiLayoutService.MaxSpeedMin, ManeuverGuiLayoutService.MaxSpeedMax, "0",
                " m/s  (" + GameUnitDisplayService.Speed(_dMaxSpeed) + ")");
            float cornerMax = ManeuverGuiLayoutService.CornerSliderMax(_dMaxSpeed);
            EditSlider(UiLang.T("Corner speed", "拐角速度"), ref _dCornerSpeed,
                ManeuverGuiLayoutService.CornerSpeedFloor, cornerMax, "0", " m/s");
            float clampedCorner = ManeuverProfileMathService.ClampCornerToMax(_dCornerSpeed, _dMaxSpeed);
            if (clampedCorner != _dCornerSpeed)
            {
                _dCornerSpeed = clampedCorner;
                MarkDirty();
            }
            EditSlider(UiLang.T("Approach speed", "进近速度"), ref _dApproach,
                ManeuverGuiLayoutService.ApproachMin, ManeuverGuiLayoutService.ApproachMax, "0", " m/s");
            EditSlider(UiLang.T("Landing speed", "着陆速度"), ref _dLanding,
                ManeuverGuiLayoutService.LandingMin, ManeuverGuiLayoutService.LandingMax, "0", " m/s");
            EditSlider(UiLang.T("Takeoff speed", "起飞速度"), ref _dTakeoff,
                ManeuverGuiLayoutService.TakeoffMin, ManeuverGuiLayoutService.TakeoffMax, "0", " m/s");
            EditSlider(UiLang.T("Turning radius", "转弯半径"), ref _dTurnRadius,
                ManeuverGuiLayoutService.TurnRadiusMin, ManeuverGuiLayoutService.TurnRadiusMax, "0", string.Empty);

            GUILayout.Space(6f);
            GUILayout.Label(UiLang.T("PILOT", "飞行员"), _sectionStyle);
            EditSlider(UiLang.T("Pilot G (maxG)", "飞行员过载上限"), ref _dPilotG,
                ManeuverGuiLayoutService.PilotGMin, ManeuverGuiLayoutService.PilotGMax, "0.0", " G");
            EditSlider(UiLang.T("Pilot strength", "飞行员耐力"), ref _dPilotStrength,
                ManeuverGuiLayoutService.PilotStrengthMin, ManeuverGuiLayoutService.PilotStrengthMax, "0.00", string.Empty);

            GUILayout.Space(6f);
            GUILayout.Label(UiLang.T("CONTROL AUTHORITY", "操纵权限"), _sectionStyle);
            EditSlider(UiLang.T("Pitch rate mul", "俯仰速率倍率"), ref _dPitchMul,
                ManeuverGuiLayoutService.PitchMulMin, ManeuverGuiLayoutService.PitchMulMax, "0.00", "x");
            if (_profile.BaselinePitch > 0.01f)
                GUILayout.Label(UiLang.T(
                    "  pitch vel " + (_profile.BaselinePitch * _dPitchMul).ToString("0.00")
                    + "  (base " + _profile.BaselinePitch.ToString("0.00") + ")",
                    "  俯仰角速度 " + (_profile.BaselinePitch * _dPitchMul).ToString("0.00")
                    + "  基准 " + _profile.BaselinePitch.ToString("0.00")), _labelStyle);
            EditSlider(UiLang.T("Roll rate mul", "滚转速率倍率"), ref _dRollMul,
                ManeuverGuiLayoutService.RollMulMin, ManeuverGuiLayoutService.RollMulMax, "0.00", "x");
            if (_profile.BaselineRoll > 0.01f)
                GUILayout.Label(UiLang.T(
                    "  roll vel " + (_profile.BaselineRoll * _dRollMul).ToString("0.00")
                    + "  (base " + _profile.BaselineRoll.ToString("0.00") + ")",
                    "  滚转角速度 " + (_profile.BaselineRoll * _dRollMul).ToString("0.00")
                    + "  基准 " + _profile.BaselineRoll.ToString("0.00")), _labelStyle);
            EditSlider(UiLang.T("Alpha limiter mul", "迎角限制倍率"), ref _dAlphaMul,
                ManeuverGuiLayoutService.AlphaMulMin, ManeuverGuiLayoutService.AlphaMulMax, "0.00", "x");

            GUILayout.Space(6f);
            GUILayout.Label(UiLang.T("PROPULSION", "动力"), _sectionStyle);
            EditSlider(UiLang.T("Thrust mul", "推力倍率"), ref _dThrustMul,
                ManeuverGuiLayoutService.ThrustMulMin, ManeuverGuiLayoutService.ThrustMulMax, "0.00", "x");
            EditSlider(UiLang.T("Fuel burn mul", "燃油消耗倍率"), ref _dFuelBurnMul,
                ManeuverGuiLayoutService.FuelBurnMulMin, ManeuverGuiLayoutService.FuelBurnMulMax, "0.00", "x");
            EditSlider(UiLang.T("Fuel capacity mul", "燃油容量倍率"), ref _dFuelCapMul,
                ManeuverGuiLayoutService.FuelCapMulMin, ManeuverGuiLayoutService.FuelCapMulMax, "0.00", "x");

            GUILayout.Space(10f);
            bool auto = Plugin.GuiAutoApply == null || Plugin.GuiAutoApply.Value;
            bool newAuto = GUILayout.Toggle(auto, UiLang.T(" Auto-apply while dragging", " 拖动时自动应用"));
            if (Plugin.GuiAutoApply != null && newAuto != auto)
                Plugin.GuiAutoApply.Value = newAuto;

            GUILayout.Space(6f);
            GUILayout.BeginHorizontal();
            GUI.enabled = _dirty || !auto;
            if (GUILayout.Button(UiLang.T("Apply", "应用"), _btnStyle, GUILayout.Height(32f)))
                Commit(true);
            GUI.enabled = true;
            if (GUILayout.Button(UiLang.T("Reset", "重置"), _btnStyle, GUILayout.Height(32f)))
            {
                LoadDefaultsToDraft();
                MarkDirty();
                if (auto)
                    ScheduleDeferredApply();
            }
            if (GUILayout.Button(UiLang.T("Reload", "重载"), _btnStyle, GUILayout.Height(32f)))
            {
                ReloadDraftFromProfile();
                SetStatus(UiLang.T("Reloaded from config", "已从配置重载"));
            }
            if (GUILayout.Button(UiLang.T("Close", "关闭"), _btnStyle, GUILayout.Height(32f)))
                Close();
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label(UiLang.T(
                "Per-airframe limits · pack toggles are global · Esc / " + keyHint + " closes",
                "单机限制 · 全局开关 · Esc / " + keyHint + " 关闭"), _labelStyle);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            GUI.color = prev;
        }

        private static void DrawF1Tab(Rect r, int index, string label)
        {
            bool on = _tab == index;
            Color prev = GUI.color;
            GUI.color = on
                ? new Color(0.35f, 0.45f, 0.2f, 0.95f)
                : new Color(0.18f, 0.22f, 0.28f, 0.9f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
            if (GUI.Button(r, label, _btnStyle))
            {
                _tab = index;
                _rebindEntry = null;
            }
            GUI.color = prev;
        }

        private static void PollKeyRebind()
        {
            if (_rebindEntry == null)
                return;
            Event e = Event.current;
            if (e == null || !e.isKey || e.type != EventType.KeyDown)
                return;
            if (e.keyCode == KeyCode.Escape)
            {
                _rebindEntry = null;
                e.Use();
                return;
            }
            if (e.keyCode == KeyCode.None)
                return;
            _rebindEntry.Value = e.keyCode;
            if (object.ReferenceEquals(_rebindEntry, InfantryModeService.BoardKey)
                && (e.keyCode == KeyCode.I || e.keyCode == KeyCode.None))
                _rebindEntry.Value = KeyCode.U;
            _rebindEntry = null;
            e.Use();
            SaveKeybinds();
        }

        private static void SaveKeybinds()
        {
            try
            {
                if (Plugin.Instance != null)
                    Plugin.Instance.Config.Save();
            }
            catch { }
            try
            {
                foreach (KeyValuePair<string, BepInEx.PluginInfo> kv in BepInEx.Bootstrap.Chainloader.PluginInfos)
                {
                    if (kv.Key == null || kv.Value == null || kv.Value.Instance == null)
                        continue;
                    if (kv.Key.IndexOf("wexon", StringComparison.OrdinalIgnoreCase) < 0
                        && kv.Value.Instance.GetType().FullName != "WeXon.Plugin")
                        continue;
                    kv.Value.Instance.Config.Save();
                }
            }
            catch { }
        }

        private static void DrawKeyRebind(string label, ConfigEntry<KeyCode> entry)
        {
            if (entry == null)
                return;
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _labelStyle, GUILayout.Width(168f));
            bool listening = object.ReferenceEquals(_rebindEntry, entry);
            string shown = listening
                ? UiLang.T("Press a key…", "按一下新键…")
                : (entry.Value == KeyCode.None
                    ? UiLang.T("None", "无")
                    : entry.Value.ToString());
            if (GUILayout.Button(shown, _btnStyle, GUILayout.Height(22f)))
                _rebindEntry = entry;
            if (GUILayout.Button(UiLang.T("None", "无"), _btnStyle, GUILayout.Width(44f), GUILayout.Height(22f)))
            {
                entry.Value = KeyCode.None;
                if (object.ReferenceEquals(_rebindEntry, entry))
                    _rebindEntry = null;
                SaveKeybinds();
            }
            object defObj = entry.DefaultValue;
            if (defObj is KeyCode)
            {
                KeyCode def = (KeyCode)defObj;
                if (GUILayout.Button(UiLang.T("Reset", "默认"), _btnStyle, GUILayout.Width(48f), GUILayout.Height(22f)))
                {
                    entry.Value = def;
                    if (object.ReferenceEquals(_rebindEntry, entry))
                        _rebindEntry = null;
                    SaveKeybinds();
                }
            }
            GUILayout.EndHorizontal();
        }

        private static void DrawKeysPanel()
        {
            GUILayout.Label(UiLang.T("HOTKEYS  (not menu keys)", "热键（不含菜单键）"), _sectionStyle);
            GUILayout.Label(UiLang.T(
                "F1 System · F2 Autopilot · F3 Anti-crash · F4 ILS · F5 PM · F6 Boost · F7 Host fund · F9 Support · F10 Resupply · F11 RWR layout are not listed.",
                "F1 系统 · F2 自动驾驶 · F3 防坠机系统 · F4 ILS · F5 私信 · F6 击杀奖励 · F7 主机资金 · F9 支援 · F10 补给 · F11 RWR 布局 不在此改。"),
                _labelStyle);

            GUILayout.Space(8f);
            GUILayout.Label(UiLang.T("ENGINE / APU", "引擎 / APU"), _sectionStyle);
            DrawApuKeyRebind();

            GUILayout.Space(8f);
            GUILayout.Label("HUD", _sectionStyle);
            GUILayout.Label(UiLang.T(
                "Chase / ILS / RWR / CCIP / MFD keys live in Oritasy Hud (BepInEx/config/com.iallemege.oritasyhud.cfg).",
                "追逐 / ILS / RWR / CCIP / MITL / MFD 热键在独立 Oritasy Hud（BepInEx/config/com.iallemege.oritasyhud.cfg）。"),
                _labelStyle);

            GUILayout.Space(8f);
            GUILayout.Label(UiLang.T("AUTOPILOT", "自动驾驶"), _sectionStyle);
            DrawKeyRebind(UiLang.T("Previous mode", "上一模式"), PlayerAutopilot.PrevModeKey);
            DrawKeyRebind(UiLang.T("Next mode", "下一模式"), PlayerAutopilot.NextModeKey);

            GUILayout.Space(8f);
            GUILayout.Label(UiLang.T("INFANTRY", "步兵"), _sectionStyle);
            DrawKeyRebind(UiLang.T("Hop out / climb in", "下车 / 上车"), InfantryModeService.BoardKey);
            DrawKeyRebind(UiLang.T("Ibis cabin guns", "朱鹮舱内机枪"), InfantryCabinGunService.CabinKey);
            GUILayout.Label(UiLang.T(
                "Default U. Do not bind I — that is vanilla Toggle Engine / APU.",
                "默认 U。不要绑 I，那是原版开关发动机 / APU。"),
                _labelStyle);

            GUILayout.Space(8f);
            GUILayout.Label(UiLang.T("RESUPPLY", "空中补给"), _sectionStyle);
            DrawKeyRebind(UiLang.T("Stop resupply", "停止补给"), AerialResupply.StopKey);

            GUILayout.Space(8f);
            GUILayout.Label(UiLang.T("MUSIC", "音乐"), _sectionStyle);
            DrawKeyRebind(UiLang.T("Reload music", "重载音乐"), DynamicMusic.ReloadKey);

            if (WeXonBridge.Available)
            {
                GUILayout.Space(8f);
                GUILayout.Label(UiLang.T("SUPPORT / SCOREBOARD", "支援 / 计分板"), _sectionStyle);
                DrawKeyRebind(UiLang.T("Arsenal previous", "支援上一档"), WeXonBridge.ArsenalPrevKey);
                DrawKeyRebind(UiLang.T("Arsenal next", "支援下一档"), WeXonBridge.ArsenalNextKey);
                DrawKeyRebind(UiLang.T("Scoreboard", "计分板"), WeXonBridge.ScoreboardKey);
            }

            GUILayout.Space(10f);
            if (GUILayout.Button(UiLang.T("Reset all listed keys to default", "全部热键恢复默认"), _btnStyle, GUILayout.Height(28f)))
                ResetListedKeys();
        }

        private static void ResetListedKeys()
        {
            ResetOneKey(EngineStartService.StartKey);
            ResetOneKey(PlayerAutopilot.PrevModeKey);
            ResetOneKey(PlayerAutopilot.NextModeKey);
            ResetOneKey(InfantryModeService.BoardKey);
            ResetOneKey(InfantryCabinGunService.CabinKey);
            ResetOneKey(AerialResupply.StopKey);
            ResetOneKey(DynamicMusic.ReloadKey);
            ResetOneKey(WeXonBridge.ArsenalPrevKey);
            ResetOneKey(WeXonBridge.ArsenalNextKey);
            ResetOneKey(WeXonBridge.ScoreboardKey);
            _rebindEntry = null;
            SaveKeybinds();
        }

        private static void ResetOneKey(ConfigEntry<KeyCode> entry)
        {
            if (entry == null)
                return;
            object defObj = entry.DefaultValue;
            if (defObj is KeyCode)
                entry.Value = (KeyCode)defObj;
        }

        private static void DrawApuKeyRebind()
        {
            ConfigEntry<KeyCode> entry = EngineStartService.StartKey;
            if (entry == null)
                return;
            GUILayout.BeginHorizontal();
            GUILayout.Label(UiLang.T("APU / engine start", "APU / 引擎启动"), _labelStyle, GUILayout.Width(168f));
            bool listening = object.ReferenceEquals(_rebindEntry, entry);
            string shown;
            if (listening)
                shown = UiLang.T("Press a key…", "按一下新键…");
            else if (entry.Value == KeyCode.None)
            {
                string gameKey = EngineStartService.StartKeyLabel();
                shown = UiLang.T("Game (" + gameKey + ")", "游戏（" + gameKey + "）");
            }
            else
                shown = entry.Value.ToString();
            if (GUILayout.Button(shown, _btnStyle, GUILayout.Height(22f)))
                _rebindEntry = entry;
            if (GUILayout.Button(UiLang.T("None", "无"), _btnStyle, GUILayout.Width(44f), GUILayout.Height(22f)))
            {
                entry.Value = KeyCode.None;
                if (object.ReferenceEquals(_rebindEntry, entry))
                    _rebindEntry = null;
                SaveKeybinds();
            }
            if (GUILayout.Button(UiLang.T("Reset", "默认"), _btnStyle, GUILayout.Width(48f), GUILayout.Height(22f)))
            {
                entry.Value = KeyCode.None;
                if (object.ReferenceEquals(_rebindEntry, entry))
                    _rebindEntry = null;
                SaveKeybinds();
            }
            GUILayout.EndHorizontal();
            GUILayout.Label(UiLang.T(
                "None / Reset = game Toggle Engine. After a custom bind, APU prompts use that key.",
                "无 / 默认 = 游戏「发动机」键。改键后 APU 提示会显示新键。"), _labelStyle);
        }

        private static void DrawPackOptions()
        {
            GUILayout.Label(UiLang.T("ENGINE START / APU", "引擎启动 / APU"), _sectionStyle);
            EngineStartService.DrawProfileToggle();
            DrawApuKeyRebind();
            GUILayout.Label(UiLang.T(
                "Turn OFF to spawn with engines running (no APU hold-to-start).",
                "关掉后出生即开车，不再走 APU 冷车流程。"), _labelStyle);

            GUILayout.Space(8f);
            GUILayout.Label(UiLang.T("WEXON WEAPONS", "WEXON 武器"), _sectionStyle);
            WeXonBridge.DrawToggles();

            GUILayout.Space(6f);
            GUILayout.Label("HUD", _sectionStyle);
            GUILayout.Label(UiLang.T(
                "Missile camera / MITL / G-meter / RWR / CCIP / ILS / MFD are Oritasy Hud. Edit BepInEx/config/com.iallemege.oritasyhud.cfg.",
                "导弹相机 / MITL / G 表 / RWR / CCIP / ILS / MFD 属于独立 Oritasy Hud，改 BepInEx/config/com.iallemege.oritasyhud.cfg。"),
                _labelStyle);

            GUILayout.Space(6f);
            GUILayout.Label(UiLang.T("PERFORMANCE", "性能"), _sectionStyle);
            GUILayout.Label(UiLang.T(
                "Primary control: main-menu Oritasy Profile → Performance (Low / Med / High).",
                "主设置：主菜单 Oritasy 档案 → 性能（低 / 中 / 高）。"), _labelStyle);
            if (PerfMode.LowEndMode != null)
            {
                GUILayout.Label(UiLang.T(
                    "Active: " + PerfMode.TierName
                    + " · Preset " + (PerfMode.Preset != null ? PerfMode.Preset.Value : "Auto")
                    + (PerfMode.LowEndMode.Value ? " · Low-end ON" : ""),
                    "当前: " + PerfMode.TierName
                    + " · 预设 " + (PerfMode.Preset != null ? PerfMode.Preset.Value : "Auto")
                    + (PerfMode.LowEndMode.Value ? " · 低配开" : "")), _labelStyle);
            }

            GUILayout.Space(6f);
            GUILayout.Label("HUD", _sectionStyle);
            if (Plugin.ShowThirdPersonUi != null)
            {
                bool tp = GUILayout.Toggle(Plugin.ShowThirdPersonUi.Value,
                    UiLang.T(" Third-person / overlay UI", " 第三人称 / 叠加 UI"));
                if (tp != Plugin.ShowThirdPersonUi.Value)
                    Plugin.ShowThirdPersonUi.Value = tp;
                GUILayout.Label(UiLang.T(
                    "Off: hide FLT REC, fuel flash, F1–F10 tip chips, and the Oritasy brand. Title-screen Help is unchanged. Hotkeys still work.",
                    "关：隐藏 FLT REC、定时油耗、F1–F10 提示条和 Oritasy 品牌条。主菜单 Help 不受影响。热键仍可用。"),
                    _labelStyle);
            }
            ConfigEntry<bool> stickEntry = HudPackState.StickDisplayEntry();
            if (stickEntry != null)
            {
                bool stick = GUILayout.Toggle(stickEntry.Value,
                    UiLang.T(" Stick / throttle display", " 杆量显示"));
                if (stick != stickEntry.Value)
                    stickEntry.Value = stick;
                if (Plugin.ShowStickDisplay != null && !object.ReferenceEquals(Plugin.ShowStickDisplay, stickEntry))
                    Plugin.ShowStickDisplay.Value = stick;
                GUILayout.Label(UiLang.T(
                    "Bottom-right stick cross and throttle bar. Independent of chase HUD.",
                    "右下角杆量和油门条。与追逐准星分开开关。"),
                    _labelStyle);
            }
            if (Plugin.ShowHudBrand != null)
            {
                bool brand = GUILayout.Toggle(Plugin.ShowHudBrand.Value,
                    UiLang.T(" Bottom-right Canvas watermark", " 右下角 Canvas 水印"));
                if (brand != Plugin.ShowHudBrand.Value)
                    Plugin.ShowHudBrand.Value = brand;
            }
            if (BeginnerAssist.AutoDisableTerrainOnGear != null)
            {
                bool gearTerrain = GUILayout.Toggle(BeginnerAssist.AutoDisableTerrainOnGear.Value,
                    UiLang.T(" Auto-off Terrain pull-up on gear", " 起落架时自动关闭地形拉起"));
                if (gearTerrain != BeginnerAssist.AutoDisableTerrainOnGear.Value)
                    BeginnerAssist.AutoDisableTerrainOnGear.Value = gearTerrain;
                GUILayout.Label(UiLang.T(
                    "Off before gear up · after gear down locked · yields during LAND AP",
                    "收轮前关闭 · 放轮锁定后关闭 · 着陆自动驾驶时让路"), _labelStyle);
            }

            GameUnitDisplayService.DrawF1Section(_sectionStyle, _labelStyle, _btnStyle);

            GUILayout.Space(6f);
            GUILayout.Label(UiLang.T("NUKE RESIST", "核抗性"), _sectionStyle);
            if (Plugin.NukeShockResist != null)
            {
                bool en = GUILayout.Toggle(Plugin.NukeShockResist.Value,
                    UiLang.T(" Nuke resist enabled", " 启用核抗性"));
                if (en != Plugin.NukeShockResist.Value)
                    Plugin.NukeShockResist.Value = en;
            }
            if (Plugin.NavalTripleResist != null)
            {
                bool n = GUILayout.Toggle(Plugin.NavalTripleResist.Value,
                    UiLang.T(" Naval resist ×3 (take 1/3 damage)", " 舰船抗性×3"));
                if (n != Plugin.NavalTripleResist.Value)
                    Plugin.NavalTripleResist.Value = n;
            }
            GUILayout.Label(UiLang.T(
                "Aircraft keep Full resist. Buildings have none. Ground vehicles stay at half.",
                "飞机保持完整抗性。建筑无抗性。地面载具仍为半抗。"), _labelStyle);

            GUILayout.Space(6f);
            DynamicMusic.DrawGuiToggles();

            GUILayout.Space(6f);
            MissileAudio.DrawGuiToggles();
        }

        private static void EditSlider(string label, ref float value, float min, float max, string fmt, string suffix)
        {
            GUILayout.Label(label + "  " + value.ToString(fmt) + suffix, _labelStyle);
            float v = GUILayout.HorizontalSlider(value, min, max);
            if (!Mathf.Approximately(v, value))
            {
                value = v;
                MarkDirty();
                if (Plugin.GuiAutoApply == null || Plugin.GuiAutoApply.Value)
                    ScheduleDeferredApply();
            }
        }

        private static void EnsureMenuStyles()
        {
            if (_titleStyle != null)
            {
                ChineseFontPatch.ApplyTo(_titleStyle);
                ChineseFontPatch.ApplyTo(_labelStyle);
                ChineseFontPatch.ApplyTo(_sectionStyle);
                ChineseFontPatch.ApplyTo(_btnStyle);
                ChineseFontPatch.ApplyTo(_f1HintStyle);
                return;
            }

            _titleStyle = new GUIStyle(GUI.skin.label);
            _titleStyle.fontSize = 18;
            _titleStyle.fontStyle = FontStyle.Bold;
            _titleStyle.alignment = TextAnchor.MiddleLeft;
            _titleStyle.normal.textColor = new Color(0.9f, 0.95f, 0.55f, 1f);

            _labelStyle = new GUIStyle(GUI.skin.label);
            _labelStyle.fontSize = 13;
            _labelStyle.alignment = TextAnchor.MiddleLeft;
            _labelStyle.normal.textColor = new Color(0.85f, 0.9f, 0.95f, 0.95f);
            _labelStyle.wordWrap = true;

            _sectionStyle = new GUIStyle(GUI.skin.label);
            _sectionStyle.fontSize = 13;
            _sectionStyle.fontStyle = FontStyle.Bold;
            _sectionStyle.alignment = TextAnchor.MiddleLeft;
            _sectionStyle.normal.textColor = new Color(0.55f, 0.9f, 0.7f, 1f);

            _btnStyle = new GUIStyle(GUI.skin.button);
            _btnStyle.fontSize = 13;
            _btnStyle.fontStyle = FontStyle.Bold;
            _btnStyle.alignment = TextAnchor.MiddleCenter;
            _btnStyle.normal.textColor = Color.white;

            _f1HintStyle = new GUIStyle(GUI.skin.label);
            _f1HintStyle.fontSize = 11;
            _f1HintStyle.fontStyle = FontStyle.Bold;
            _f1HintStyle.alignment = TextAnchor.MiddleRight;
            _f1HintStyle.normal.textColor = new Color(0.9f, 0.95f, 0.8f, 0.95f);

            ChineseFontPatch.ApplyTo(_titleStyle);
            ChineseFontPatch.ApplyTo(_labelStyle);
            ChineseFontPatch.ApplyTo(_sectionStyle);
            ChineseFontPatch.ApplyTo(_btnStyle);
            ChineseFontPatch.ApplyTo(_f1HintStyle);
        }

        internal static GUIStyle StyleSection()
        {
            EnsureMenuStyles();
            return _sectionStyle;
        }

        internal static GUIStyle StyleLabel()
        {
            EnsureMenuStyles();
            return _labelStyle;
        }

        internal static GUIStyle StyleButton()
        {
            EnsureMenuStyles();
            return _btnStyle;
        }

        private static void MarkDirty()
        {
            _dirty = true;
        }

        private static void ScheduleDeferredApply()
        {
            _nextDeferredApply = ManeuverGuiLayoutService.DeferredDeadline(Time.unscaledTime);
        }

        private static void RefreshBinding(bool force)
        {
            Aircraft ac = Plugin.ResolveGuiAircraft();
            if (ac == null)
            {
                if (_state == UiState.Editing)
                    _state = UiState.Empty;
                _aircraft = null;
                _profile = null;
                _boundKey = string.Empty;
                return;
            }

            ManeuverProfile profile = Plugin.GetOrCreateProfile(ac);
            string key = profile.Key;
            bool changed = force || _state != UiState.Editing || _aircraft == null
                || !string.Equals(_boundKey, key, StringComparison.OrdinalIgnoreCase)
                || !object.ReferenceEquals(_profile, profile);

            Plugin.RegisterLiveXe(ac);
            _aircraft = ac;
            _profile = profile;
            if (_state == UiState.Empty || _state == UiState.Editing)
                _state = UiState.Editing;

            if (changed)
            {
                _boundKey = key;
                // Don't wipe in-progress slider edits when binding refreshes
                if (!_dirty || force)
                {
                    ReloadDraftFromProfile();
                    _nextDeferredApply = -1f;
                }
            }
        }

        private static void LoadDefaultsToDraft()
        {
            if (_profile == null)
                return;
            _dAircraftG = _profile.DefaultAircraftG;
            _dPilotG = _profile.DefaultPilotG;
            _dPilotStrength = _profile.DefaultPilotStrength;
            _dMaxSpeed = _profile.DefaultMaxSpeed;
            _dCornerSpeed = _profile.DefaultCornerSpeed;
            _dPitchMul = _profile.DefaultPitchMul;
            _dRollMul = _profile.DefaultRollMul;
            _dAlphaMul = _profile.DefaultAlphaMul;
            _dThrustMul = _profile.DefaultThrustMul;
            _dFuelBurnMul = _profile.DefaultFuelBurnMul;
            _dFuelCapMul = _profile.DefaultFuelCapMul;
            _dApproach = _profile.DefaultApproachSpeed;
            _dLanding = _profile.DefaultLandingSpeed;
            _dTakeoff = _profile.DefaultTakeoffSpeed;
            _dTurnRadius = _profile.DefaultTurningRadius;
        }

        private static void ReloadDraftFromProfile()
        {
            if (_profile == null)
                return;
            _profile.CopyFromConfig(
                out _dAircraftG, out _dPilotG, out _dPilotStrength,
                out _dMaxSpeed, out _dCornerSpeed,
                out _dPitchMul, out _dRollMul, out _dAlphaMul,
                out _dThrustMul, out _dFuelBurnMul, out _dFuelCapMul,
                out _dApproach, out _dLanding, out _dTakeoff, out _dTurnRadius);
            _dirty = false;
        }

        private static void Commit(bool showStatus)
        {
            if (_profile == null || _aircraft == null)
                return;

            _dCornerSpeed = ManeuverProfileMathService.ClampCornerToMax(_dCornerSpeed, _dMaxSpeed);

            _profile.WriteToConfig(
                _dAircraftG, _dPilotG, _dPilotStrength,
                _dMaxSpeed, _dCornerSpeed,
                _dPitchMul, _dRollMul, _dAlphaMul,
                _dThrustMul, _dFuelBurnMul, _dFuelCapMul,
                _dApproach, _dLanding, _dTakeoff, _dTurnRadius);

            // Ensure this aircraft is tracked, then apply limits + power to IT first
            Plugin.RegisterLiveXe(_aircraft);
            Plugin.EnsureControlBaselines(_aircraft, _profile);
            Plugin.ApplyLimits(_aircraft);
            Plugin.ApplyPowerProfile(_aircraft);

            // Same airframe definition may be shared — refresh other live instances of this key
            Plugin.ApplyLimitsToAllXe();
            for (int i = 0; i < Plugin.LiveXeAircraft.Count; i++)
            {
                Aircraft ac = Plugin.LiveXeAircraft[i];
                if (ac == null || object.ReferenceEquals(ac, _aircraft))
                    continue;
                if (string.Equals(Plugin.GetAircraftKey(ac), _profile.Key, StringComparison.OrdinalIgnoreCase))
                    Plugin.ApplyPowerProfile(ac);
            }

            // Pilot G is reapplied from PilotPlayerState.UpdateState (1 Hz)

            _dirty = false;
            _nextDeferredApply = -1f;
            try
            {
                if (Plugin.Instance != null)
                    Plugin.Instance.Config.Save();
            }
            catch { }
            if (showStatus)
                SetStatus(UiLang.T("Applied · ", "已应用 · ") + _profile.DisplayLabel);
        }

        private static void SetStatus(string msg)
        {
            _status = msg;
            _statusUntil = Time.unscaledTime + 2.5f;
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
    }
}
