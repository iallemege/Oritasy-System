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
            if (ac == null || MissileCameraHud.ManualActive)
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
                "F1 System · F2 Autopilot · F3 Beginner · F4 ILS · F5 PM · F6 Boost · F7 Host fund · F9 Support · F10 Resupply · F11 RWR layout are not listed.",
                "F1 系统 · F2 自动驾驶 · F3 新手 · F4 ILS · F5 私信 · F6 击杀奖励 · F7 主机资金 · F9 支援 · F10 补给 · F11 RWR 布局 不在此改。"),
                _labelStyle);

            GUILayout.Space(8f);
            GUILayout.Label(UiLang.T("MISSILE", "导弹"), _sectionStyle);
            DrawKeyRebind(UiLang.T("Missile PiP", "导弹画中画"), Plugin.MissileCameraKey);
            DrawKeyRebind(UiLang.T("Man-in-the-loop", "人在回路"), Plugin.ManualMissileKey);
            DrawKeyRebind(UiLang.T("Next missile", "切换导弹"), Plugin.MissileCameraCycleKey);

            GUILayout.Space(8f);
            GUILayout.Label("HUD", _sectionStyle);
            DrawKeyRebind(UiLang.T("Chase nose HUD", "尾追机头 HUD"), Plugin.AircraftChaseHudKey);
            DrawKeyRebind(UiLang.T("G-meter toggle", "G 力表开关"), Plugin.GMeterKey);
            DrawKeyRebind(UiLang.T("MITL HUD", "人在回路 HUD"), Plugin.OritasyHudKey);
            DrawKeyRebind(UiLang.T("ILS overlay", "ILS 叠加"), AirportIlsHud.OverlayToggleKey);

            GUILayout.Space(8f);
            GUILayout.Label(UiLang.T("AUTOPILOT", "自动驾驶"), _sectionStyle);
            DrawKeyRebind(UiLang.T("Previous mode", "上一模式"), PlayerAutopilot.PrevModeKey);
            DrawKeyRebind(UiLang.T("Next mode", "下一模式"), PlayerAutopilot.NextModeKey);

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
            ResetOneKey(Plugin.MissileCameraKey);
            ResetOneKey(Plugin.ManualMissileKey);
            ResetOneKey(Plugin.MissileCameraCycleKey);
            ResetOneKey(Plugin.AircraftChaseHudKey);
            ResetOneKey(Plugin.GMeterKey);
            ResetOneKey(Plugin.OritasyHudKey);
            ResetOneKey(AirportIlsHud.OverlayToggleKey);
            ResetOneKey(PlayerAutopilot.PrevModeKey);
            ResetOneKey(PlayerAutopilot.NextModeKey);
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

        private static void DrawPackOptions()
        {
            GUILayout.Label(UiLang.T("WEXON WEAPONS", "WEXON 武器"), _sectionStyle);
            WeXonBridge.DrawToggles();

            GUILayout.Space(6f);
            GUILayout.Label(UiLang.T("MISSILE CAMERA", "导弹镜头"), _sectionStyle);
            if (Plugin.MissileCamera != null)
            {
                bool cam = GUILayout.Toggle(Plugin.MissileCamera.Value,
                    UiLang.T(" Left missile camera (PiP)", " 左侧导弹画中画"));
                if (cam != Plugin.MissileCamera.Value)
                    Plugin.MissileCamera.Value = cam;
                string camKey = Plugin.MissileCameraKey != null
                    ? Plugin.MissileCameraKey.Value.ToString() : "Delete";
                GUILayout.Label(UiLang.T(camKey + " missile cam · F4 ILS · F5 PM · F6 boost",
                    camKey + " 导弹相机 · F4 ILS · F5 私信 · F6 击杀奖励"), _labelStyle);
                DrawKeyRebind(UiLang.T("PiP key", "画中画键"), Plugin.MissileCameraKey);
            }
            if (Plugin.ManualMissile != null)
            {
                bool man = GUILayout.Toggle(Plugin.ManualMissile.Value,
                    UiLang.T(" Man-in-the-loop (fullscreen nose)", " 人在回路"));
                if (man != Plugin.ManualMissile.Value)
                    Plugin.ManualMissile.Value = man;
                string mKey = Plugin.ManualMissileKey != null ? Plugin.ManualMissileKey.Value.ToString() : "Insert";
                string cKey = Plugin.MissileCameraCycleKey != null
                    && Plugin.MissileCameraCycleKey.Value != KeyCode.None
                    ? Plugin.MissileCameraCycleKey.Value.ToString() : "—";
                GUILayout.Label(UiLang.T(
                    mKey + " takeover · " + cKey + " next missile · WASD steer · Q/E throttle · Esc exit",
                    mKey + " 接管 · " + cKey + " 换弹 · WASD 转向 · Q/E 油门 · Esc 退出"), _labelStyle);
                DrawKeyRebind(UiLang.T("MITL key", "人操键"), Plugin.ManualMissileKey);
                DrawKeyRebind(UiLang.T("Next missile", "切换导弹"), Plugin.MissileCameraCycleKey);
            }

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
                    "Off: hide chase HUD, FLT REC, fuel flash, ALL tip chips (F1–F11), brand, G/RWR. Help chip stays on. Hotkeys still work; F1 System stays available.",
                    "关：隐藏追逐 HUD、FLT REC、定时油耗、全部提示条（含 F1）、品牌条、G/RWR。左上角 Help 仍显示。热键仍可用；F1 系统菜单始终可开。"),
                    _labelStyle);
            }
            if (Plugin.ShowHudBrand != null)
            {
                bool brand = GUILayout.Toggle(Plugin.ShowHudBrand.Value,
                    UiLang.T(" Top HUD brand", " 顶部 HUD 品牌条"));
                if (brand != Plugin.ShowHudBrand.Value)
                    Plugin.ShowHudBrand.Value = brand;
            }
            if (Plugin.ShowGMeter != null)
            {
                bool gMeter = GUILayout.Toggle(Plugin.ShowGMeter.Value,
                    UiLang.T(" G-force meter (right)", " G 力表"));
                if (gMeter != Plugin.ShowGMeter.Value)
                    Plugin.ShowGMeter.Value = gMeter;
                string gKey = Plugin.GMeterKey != null ? Plugin.GMeterKey.Value.ToString() : "None";
                if (Plugin.GMeterKey != null && Plugin.GMeterKey.Value == KeyCode.None)
                {
                    GUILayout.Label(UiLang.T("Always on when enabled · F5 is private messages",
                        "开启后常显 · F5 为对局私信"), _labelStyle);
                }
                else
                {
                    GUILayout.Label(UiLang.T(gKey + " toggle · signed +Gz / peak hold",
                        gKey + " 开关 · 正负 G / 峰值保持"), _labelStyle);
                }
            }
            if (Plugin.EnhanceStatusDisplay != null)
            {
                bool dmgHud = GUILayout.Toggle(Plugin.EnhanceStatusDisplay.Value,
                    UiLang.T(" Enhanced damage silhouette (lower-right)", " 增强右下角损伤图"));
                if (dmgHud != Plugin.EnhanceStatusDisplay.Value)
                    Plugin.EnhanceStatusDisplay.Value = dmgHud;
                GUILayout.Label(UiLang.T("Larger · always on · green / amber / red / magenta",
                    "放大常显 · 绿/黄/红/品红"), _labelStyle);
            }
            if (Plugin.ShowAircraftChaseHud != null)
            {
                bool chaseHud = GUILayout.Toggle(Plugin.ShowAircraftChaseHud.Value,
                    UiLang.T(" Tail-chase nose HUD (3rd person)", " 尾追机头 HUD"));
                if (chaseHud != Plugin.ShowAircraftChaseHud.Value)
                    Plugin.ShowAircraftChaseHud.Value = chaseHud;
                string cKey = Plugin.AircraftChaseHudKey != null
                    ? Plugin.AircraftChaseHudKey.Value.ToString() : "F8";
                GUILayout.Label(UiLang.T(cKey + " toggle · only in aircraft chase/tail camera",
                    cKey + " 开关 · 仅飞机追逐/尾随视角"), _labelStyle);
            }
            if (RocketCcipHud.Enabled != null && !RocketCcipHud.StandalonePresent)
            {
                bool ccip = GUILayout.Toggle(RocketCcipHud.Enabled.Value,
                    UiLang.T(" Rocket CCIP", " 火箭 CCIP"));
                if (ccip != RocketCcipHud.Enabled.Value)
                    RocketCcipHud.Enabled.Value = ccip;
                GUILayout.Label(UiLang.T("Rockets only · cockpit / chase / orbit · safety off",
                    "仅火箭 · 座舱/追逐/环绕 · 需解除保险"), _labelStyle);
                if (RocketCcipHud.CepEnabled != null)
                {
                    bool cep = GUILayout.Toggle(RocketCcipHud.CepEnabled.Value,
                        UiLang.T(" Rocket CEP", " 火箭 CEP"));
                    if (cep != RocketCcipHud.CepEnabled.Value)
                        RocketCcipHud.CepEnabled.Value = cep;
                    GUILayout.Label(UiLang.T("Scales with ToF/range · same view as CCIP",
                        "随飞行时间/距离缩放 · 与 CCIP 同视角"), _labelStyle);
                }
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
            if (Plugin.ShowAircraftRwr != null)
            {
                bool rwr = GUILayout.Toggle(Plugin.ShowAircraftRwr.Value,
                    UiLang.T(" " + AircraftRwrService.DisplayName,
                        " " + AircraftRwrService.DisplayName));
                if (rwr != Plugin.ShowAircraftRwr.Value)
                    Plugin.ShowAircraftRwr.Value = rwr;
                string rKey = Plugin.AircraftRwrKey != null
                    ? Plugin.AircraftRwrKey.Value.ToString() : "F11";
                GUILayout.Label(UiLang.T(rKey + " layout · position / size",
                    rKey + " 布局 · 位置 / 大小"), _labelStyle);
            }
            if (RadarMfdOverlay.Enabled != null)
            {
                bool mfdRdr = GUILayout.Toggle(RadarMfdOverlay.Enabled.Value,
                    UiLang.T(" Realistic radar MFD overlay", " 写实雷达 MFD 叠加"));
                if (mfdRdr != RadarMfdOverlay.Enabled.Value)
                    RadarMfdOverlay.Enabled.Value = mfdRdr;
                GUILayout.Label(UiLang.T(
                    "Only while vanilla radar MFD is on · PPI + A-scope · clutter",
                    "仅原版雷达 MFD 开启时 · PPI + A 式 · 杂波"), _labelStyle);
            }
            if (Plugin.ShowOritasyHud != null)
            {
                bool oh = GUILayout.Toggle(Plugin.ShowOritasyHud.Value,
                    UiLang.T(" Man-in-the-loop HUD (SPD/HDG/ACC/G)", " 人在回路 HUD"));
                if (oh != Plugin.ShowOritasyHud.Value)
                    Plugin.ShowOritasyHud.Value = oh;
            }
            if (Plugin.ShowCircularRwr != null)
            {
                bool rwr = GUILayout.Toggle(Plugin.ShowCircularRwr.Value,
                    UiLang.T(" " + AircraftRwrService.DisplayName + " (missile-pilot)",
                        " " + AircraftRwrService.DisplayName + "（导弹手）"));
                if (rwr != Plugin.ShowCircularRwr.Value)
                    Plugin.ShowCircularRwr.Value = rwr;
            }
            if (Plugin.ShowOritasyHud != null || Plugin.ShowCircularRwr != null)
            {
                string hKey = Plugin.OritasyHudKey != null ? Plugin.OritasyHudKey.Value.ToString() : "Backslash";
                string mitlKey = Plugin.ManualMissileKey != null
                    ? Plugin.ManualMissileKey.Value.ToString() : "Insert";
                GUILayout.Label(UiLang.T(
                    "Hidden while flying aircraft · " + mitlKey + " MITL + " + hKey + " toggle",
                    "驾驶飞机时隐藏 · " + mitlKey + " 人在回路 + " + hKey + " 开关"), _labelStyle);
            }
            if (Plugin.EnhancedAirflow != null)
            {
                bool air = GUILayout.Toggle(Plugin.EnhancedAirflow.Value,
                    UiLang.T(" Enhanced airflow / vapor / contrails", " 增强气流 / 水汽 / 凝结尾迹"));
                if (air != Plugin.EnhancedAirflow.Value)
                    Plugin.EnhancedAirflow.Value = air;
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
            if (Plugin.BuildingHalfResist != null)
            {
                bool b = GUILayout.Toggle(Plugin.BuildingHalfResist.Value,
                    UiLang.T(" Building resist ×0.5 (take 2× damage)", " 建筑抗性×0.5"));
                if (b != Plugin.BuildingHalfResist.Value)
                    Plugin.BuildingHalfResist.Value = b;
            }
            if (Plugin.NavalTripleResist != null)
            {
                bool n = GUILayout.Toggle(Plugin.NavalTripleResist.Value,
                    UiLang.T(" Naval resist ×3 (take 1/3 damage)", " 舰船抗性×3"));
                if (n != Plugin.NavalTripleResist.Value)
                    Plugin.NavalTripleResist.Value = n;
            }
            GUILayout.Label(UiLang.T(
                "Aircraft keep Full resist. Ground vehicles stay at half.",
                "飞机保持完整抗性。地面载具仍为半抗。"), _labelStyle);

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
