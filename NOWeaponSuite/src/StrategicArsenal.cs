using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Local-player strategic buy/spawn: F9 opens menu, confirm purchase, [ ] cycle.
    /// AI never uses this (input-gated + local human check).
    /// Costs are in game Allocation units where 1 = 1M displayed.
    /// </summary>
    internal static class StrategicArsenal
    {
        private static float DefaultCooldown { get { return StrategicArsenalMathService.DefaultCooldown; } }
        private static float DefaultAltitude { get { return StrategicArsenalMathService.DefaultAltitude; } }
        private static float DefaultSpread { get { return StrategicArsenalMathService.DefaultSpread; } }
        private static float MachToMs { get { return StrategicArsenalMathService.MachToMs; } }

        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<KeyCode> _menuKey;
        private static ConfigEntry<KeyCode> _prevKey;
        private static ConfigEntry<KeyCode> _nextKey;
        private static ConfigEntry<float> _cooldown;
        private static ConfigEntry<float> _altitude;
        private static ConfigEntry<float> _spread;

        private static readonly ArsenalOption[] Options = BuildOptions();
        private static int _index;
        private static float _readyAt;
        private static string _hudMsg = string.Empty;
        private static float _hudUntil;
        private static GUIStyle _hudStyle;
        private static GUIStyle _hintStyle;
        private static GUIStyle _titleStyle;
        private static GUIStyle _labelStyle;
        private static GUIStyle _btnStyle;
        private static float _nextLocalReadyAt;
        private static bool _cachedLocalReady;
        private static bool _menuOpen;
        private static float _cachedCornerTopY = -1f;
        private static float _nextCornerTopYAt;
        private static int _cachedCornerScreenH;
        private static readonly Vector3[] CornerBuf = new Vector3[4];
        private static System.Reflection.MethodInfo _cornerChipYMethod;
        private static bool _cornerChipYResolved;
        private static System.Reflection.MethodInfo _cornerChipRectMethod;
        private static bool _cornerChipRectResolved;
        private static readonly object[] CornerChipYArgs = new object[1];
        private static float _cachedTipY = -1f;
        private static float _nextTipYAt;
        private static int _cachedTipScreenH;
        private static string _cachedHintLine;
        private static float _nextHintLineAt;
        private static bool _cachedHintMenuOpen;
        private static int _cachedHintCdSec = int.MinValue;
        private static Vector2 _scroll;
        private static CursorLockMode _prevLock;
        private static bool _prevCursorVisible;
        private static bool _cursorHeld;
        // Below top-center "Oritasy System" brand (y≈8, h≈26)
        private const float SelectionHudY = 78f;

        internal static bool MenuOpen
        {
            get { return _menuOpen; }
        }

        internal static void CloseMenuFromOutside()
        {
            CloseMenu();
        }

        /// <summary>F6 late-game prize: clear shared F9 arsenal cooldown.</summary>
        internal static void ClearCooldownFromOutside()
        {
            _readyAt = 0f;
        }

        private enum ArsenalKind
        {
            Missile = 0,
            Carrier = 1
        }

        private sealed class ArsenalOption
        {
            public string Label;
            public ArsenalKind Kind;
            public string JsonKey;
            public int Count;
            public float CostM;
            public float SpeedMach;
            public bool AllowAboveFive;
            /// <summary>KillAccolades unlock key; null/empty = always available.</summary>
            public string RequireUnlock;

            public ArsenalOption(string label, ArsenalKind kind, string jsonKey, int count, float costM, float speedMach, bool allowAboveFive, string requireUnlock)
            {
                Label = label;
                Kind = kind;
                JsonKey = jsonKey;
                Count = count;
                CostM = costM;
                SpeedMach = speedMach;
                AllowAboveFive = allowAboveFive;
                RequireUnlock = requireUnlock;
            }
        }

        private static ArsenalOption[] BuildOptions()
        {
            return new ArsenalOption[]
            {
                new ArsenalOption("30x AAM-29 HE self-home", ArsenalKind.Missile, "AAM2", 30, 0.1f, 0f, true, null),
                new ArsenalOption("5x AAM-29 HE self-home", ArsenalKind.Missile, "AAM2", 5, 0.5f, 0f, false, null),
                new ArsenalOption("5x Heavy AGM", ArsenalKind.Missile, "AGM_heavy", 5, 2.5f, 0f, false, null),
                new ArsenalOption("3x Cruise HE", ArsenalKind.Missile, "CruiseMissile1", 3, 4.5f, 0f, false, null),
                new ArsenalOption("2x TBM HE Mach4", ArsenalKind.Missile, "ballisticMissile1", 2, 8f, 4f, false, null),
                new ArsenalOption("2x Cruise 20kt", ArsenalKind.Missile, "CruiseMissile20kt", 2, 12f, 0f, false, KillAccolades.UnlockAdvanced),
                new ArsenalOption("1x Nuke TBM Mach10", ArsenalKind.Missile, "ballisticMissile1_tacNuke", 1, 15f, 10f, false, KillAccolades.UnlockAdvanced),
                new ArsenalOption("3x Nuke TBM Mach10", ArsenalKind.Missile, "ballisticMissile1_tacNuke", 3, 24f, 10f, false, KillAccolades.UnlockStrategic),
                new ArsenalOption("5x Nuke TBM Mach10", ArsenalKind.Missile, "ballisticMissile1_tacNuke", 5, 30f, 10f, false, KillAccolades.UnlockStrategic),
                new ArsenalOption("Buy allied Hyperion carrier", ArsenalKind.Carrier, "FleetCarrier1", 1, 11.4f, 0f, false, KillAccolades.UnlockCarrier)
            };
        }

        internal static void Bind(ConfigFile config)
        {
            if (config == null)
                return;
            _enabled = config.Bind("StrategicArsenal", "Enabled", true,
                "Local player F9 support menu (支援). AI cannot use.");
            _menuKey = config.Bind("StrategicArsenal", "MenuKey", KeyCode.F9,
                "Open / close support menu.");
            // Legacy FireKey → MenuKey
            try
            {
                ConfigEntry<KeyCode> legacy = config.Bind("StrategicArsenal", "FireKey", KeyCode.F9,
                    "Deprecated — use MenuKey.");
                if (_menuKey.Value == KeyCode.F9 && legacy.Value != KeyCode.F9)
                    _menuKey.Value = legacy.Value;
            }
            catch { }
            _prevKey = config.Bind("StrategicArsenal", "PrevKey", KeyCode.LeftBracket,
                "Previous arsenal option.");
            _nextKey = config.Bind("StrategicArsenal", "NextKey", KeyCode.RightBracket,
                "Next arsenal option.");
            _cooldown = config.Bind("StrategicArsenal", "CooldownSeconds", DefaultCooldown,
                "Shared cooldown after a successful purchase (seconds).");
            _altitude = config.Bind("StrategicArsenal", "SpawnAltitude", DefaultAltitude,
                "F9 missile spawn height (m). 100000 = 100 km, nose-down drop.");
            if (_altitude != null && Mathf.Abs(_altitude.Value - StrategicArsenalMathService.LegacyAltitude) < 1f)
                _altitude.Value = DefaultAltitude;
            _spread = config.Bind("StrategicArsenal", "SpreadRadius", DefaultSpread,
                "Horizontal dispersion radius for multi-missile drops (m).");
        }

        internal static void Tick()
        {
            if (_enabled == null || !_enabled.Value)
                return;
            if (!IsLocalHumanReadyCached())
            {
                if (_menuOpen)
                    CloseMenu();
                return;
            }

            KeyCode menu = _menuKey != null ? _menuKey.Value : KeyCode.F9;
            if (Input.GetKeyDown(menu))
            {
                if (_menuOpen)
                    CloseMenu();
                else
                    OpenMenu();
            }

            if (_menuOpen && Input.GetKeyDown(KeyCode.Escape))
                CloseMenu();

            if (Input.GetKeyDown(_prevKey.Value))
            {
                _index = StrategicArsenalMathService.WrapIndex(_index, -1, Options.Length);
                if (!_menuOpen)
                    ShowHud(FormatSelection());
            }
            if (Input.GetKeyDown(_nextKey.Value))
            {
                _index = StrategicArsenalMathService.WrapIndex(_index, 1, Options.Length);
                if (!_menuOpen)
                    ShowHud(FormatSelection());
            }
        }

        internal static void DrawGui()
        {
            if (_enabled == null || !_enabled.Value)
                return;

            // Tip-stack reminder that F9 killstreak reward menu exists (in-flight / local human)
            if (IsLocalHumanReadyCached() && OritasyAllowsThirdPersonUi())
                DrawCornerHint();

            if (_menuOpen)
            {
                HoldCursor();
                DrawMenu();
            }

            if (string.IsNullOrEmpty(_hudMsg) || Time.unscaledTime > _hudUntil)
                return;
            if (_menuOpen)
                return; // status shown inside menu while open
            if (_hudStyle == null)
            {
                _hudStyle = new GUIStyle(GUI.skin.label);
                _hudStyle.fontSize = 18;
                _hudStyle.normal.textColor = new Color(0.85f, 1f, 0.55f, 1f);
                _hudStyle.alignment = TextAnchor.UpperCenter;
                _hudStyle.wordWrap = true;
            }
            float w = Mathf.Min(720f, GuiScale.Width * 0.7f);
            Rect r = new Rect((GuiScale.Width - w) * 0.5f, SelectionHudY, w, 72f);
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(r.x - 8f, r.y - 4f, r.width + 16f, r.height + 8f), Texture2D.whiteTexture);
            GUI.color = prev;
            GUI.Label(r, _hudMsg, _hudStyle);
        }

        /// <summary>OnGUI Y just below vanilla CAPACITOR / ChargeIndicator (top-right).</summary>
        internal static float CornerStackTopY()
        {
            if (_cachedCornerTopY > 0f
                && Time.unscaledTime < _nextCornerTopYAt
                && _cachedCornerScreenH == (int)GuiScale.Height)
                return _cachedCornerTopY;

            _nextCornerTopYAt = Time.unscaledTime + 0.5f;
            _cachedCornerScreenH = (int)GuiScale.Height;
            float y = CornerStackTopYUncached();
            _cachedCornerTopY = y;
            return y;
        }

        private static float CornerStackTopYUncached()
        {
            try
            {
                ChargeIndicator ci = UnityEngine.Object.FindObjectOfType<ChargeIndicator>();
                if (ci != null && ci.isActiveAndEnabled)
                {
                    RectTransform rt = ci.transform as RectTransform;
                    if (rt == null)
                        rt = ci.GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        rt.GetWorldCorners(CornerBuf);
                        // UI screen space: Y up from bottom → OnGUI Y down from top
                        float guiBottom = GuiScale.FromScreenYFlipped(CornerBuf[0].y);
                        return Mathf.Clamp(guiBottom + 6f, 72f, GuiScale.Height * 0.42f);
                    }
                }
            }
            catch { }
            // Fallback when capacitor HUD is hidden: sit under typical top-right status band
            return Mathf.Clamp(GuiScale.Height * 0.11f, 96f, 140f);
        }

        private static void DrawCornerHint()
        {
            // Drawing-only: skip Layout/input events to cut IMGUI work.
            if (Event.current != null && Event.current.type != EventType.Repaint)
                return;

            if (_hintStyle == null)
            {
                _hintStyle = new GUIStyle(GUI.skin.label);
                _hintStyle.fontSize = 11;
                _hintStyle.alignment = TextAnchor.MiddleRight;
                _hintStyle.normal.textColor = new Color(0.75f, 0.95f, 0.8f, 0.95f);
            }

            int cdSec = StrategicArsenalLifecycleService.RemainingCooldownSec(Time.unscaledTime, _readyAt);
            if (cdSec <= 0)
                cdSec = -1;
            if (_cachedHintLine == null
                || _cachedHintMenuOpen != _menuOpen
                || _cachedHintCdSec != cdSec
                || Time.unscaledTime >= _nextHintLineAt)
            {
                _nextHintLineAt = Time.unscaledTime + 0.25f;
                _cachedHintMenuOpen = _menuOpen;
                _cachedHintCdSec = cdSec;
                string cd = cdSec >= 0
                    ? ModUiLang.T("CD " + cdSec + "s", "冷却 " + cdSec + "s")
                    : ModUiLang.T("READY", "就绪");
                _cachedHintLine = _menuOpen
                    ? ModUiLang.T("F9 SUPPORT  |  OPEN  " + cd, "F9 支援  |  已打开  " + cd)
                    : ModUiLang.T("F9 SUPPORT  |  " + cd, "F9 支援  |  " + cd);
            }

            Rect chip = ResolveTipChip();
            Color prev = GUI.color;
            GUI.color = new Color(0.05f, 0.08f, 0.07f, 0.72f);
            GUI.DrawTexture(chip, Texture2D.whiteTexture);
            GUI.color = _menuOpen
                ? new Color(0.95f, 0.85f, 0.35f, 0.95f)
                : new Color(0.45f, 0.9f, 0.6f, 0.95f);
            GUI.DrawTexture(new Rect(chip.x, chip.y, chip.width, 2f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(chip.x + 6f, chip.y, chip.width - 12f, chip.height), _cachedHintLine, _hintStyle);
            GUI.color = prev;
        }

        private static Rect ResolveTipChip()
        {
            int slot = 8;
            try
            {
                Type layout = Type.GetType("Oritasy.AssistMenuLayoutService, Oritasy")
                    ?? Type.GetType("Oritasy.AssistMenuLayoutService");
                if (layout != null)
                {
                    FieldInfo sf9 = layout.GetField("SlotF9",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (sf9 != null)
                        slot = (int)sf9.GetValue(null);
                }
            }
            catch { }

            try
            {
                if (!_cornerChipRectResolved)
                {
                    _cornerChipRectResolved = true;
                    Type t = Type.GetType("Oritasy.PlayerAutopilot, Oritasy")
                        ?? Type.GetType("Oritasy.PlayerAutopilot");
                    if (t != null)
                    {
                        _cornerChipRectMethod = t.GetMethod("CornerChipRect",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    }
                }
                if (_cornerChipRectMethod != null)
                {
                    CornerChipYArgs[0] = slot;
                    object boxed = _cornerChipRectMethod.Invoke(null, CornerChipYArgs);
                    if (boxed is Rect)
                        return (Rect)boxed;
                }
            }
            catch { }
            return new Rect(GuiScale.Width - 248f - 18f, ResolveTipY(slot, 20f), 248f, 20f);
        }

        private static void OpenMenu()
        {
            CloseOtherModMenus();
            _menuOpen = true;
            CaptureCursor();
        }

        private static void CloseMenu()
        {
            _menuOpen = false;
            ReleaseCursor();
        }

        private static void DrawMenu()
        {
            EnsureMenuStyles();
            float funds = 0f;
            try
            {
                Player p;
                if (GameManager.GetLocalPlayer(out p) && p != null)
                    funds = p.Allocation;
            }
            catch { }

            float w = 460f;
            float h = Mathf.Min(560f, GuiScale.Height * 0.82f);
            Rect box = new Rect((GuiScale.Width - w) * 0.5f, (GuiScale.Height - h) * 0.5f, w, h);
            Color prev = GUI.color;
            GUI.color = new Color(0.06f, 0.08f, 0.1f, 0.92f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = new Color(0.45f, 0.95f, 0.6f, 0.95f);
            GUI.DrawTexture(new Rect(box.x, box.y, box.width, 3f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            string keyHint = _menuKey != null ? _menuKey.Value.ToString() : "F9";
            GUI.Label(new Rect(box.x + 16f, box.y + 12f, box.width - 32f, 26f),
                ModUiLang.T("SUPPORT  (" + keyHint + ")", "支援"), _titleStyle);

            string cd;
            int cdSec = StrategicArsenalLifecycleService.RemainingCooldownSec(Time.unscaledTime, _readyAt);
            if (cdSec > 0)
                cd = ModUiLang.T("CD " + cdSec + "s", "冷却 " + cdSec + "s");
            else
                cd = ModUiLang.T("READY", "就绪");
            GUI.Label(new Rect(box.x + 16f, box.y + 38f, box.width - 32f, 18f),
                ModUiLang.T(
                    "Select support, then Confirm    Funds "
                    + funds.ToString("0.0") + "M    " + cd,
                    "选择支援后确认    资金 "
                    + funds.ToString("0.0") + "M    " + cd), _labelStyle);

            Rect body = new Rect(box.x + 12f, box.y + 62f, box.width - 24f, box.height - 150f);
            GUILayout.BeginArea(body);
            _scroll = GUILayout.BeginScrollView(_scroll, false, true);
            for (int i = 0; i < Options.Length; i++)
            {
                ArsenalOption o = Options[i];
                bool selected = i == _index;
                bool locked = !string.IsNullOrEmpty(o.RequireUnlock)
                    && !KillAccolades.HasUnlock(o.RequireUnlock);
                string qty = o.Kind == ArsenalKind.Carrier ? "x1" : ("x" + o.Count.ToString());
                string line = (selected ? "[*] " : "[ ] ")
                    + o.Label + "  " + qty + "  ·  " + StrategicArsenalMathService.FormatCostM(o.CostM) + "M"
                    + (locked ? ModUiLang.T("  [LOCKED]", "  [锁定]") : "");

                Color bgPrev = GUI.backgroundColor;
                GUI.backgroundColor = selected
                    ? new Color(0.25f, 0.7f, 0.4f, 0.95f)
                    : new Color(0.2f, 0.25f, 0.3f, 0.9f);
                if (GUILayout.Button(line, _btnStyle, GUILayout.Height(30f)))
                    _index = i;
                GUI.backgroundColor = bgPrev;
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            float y = box.yMax - 78f;
            ArsenalOption cur = Options[_index];
            string detail = ModUiLang.T(
                "Selected: " + cur.Label + "  ·  " + StrategicArsenalMathService.FormatCostM(cur.CostM) + "M",
                "已选：" + cur.Label + "  ·  " + StrategicArsenalMathService.FormatCostM(cur.CostM) + "M");
            if (!string.IsNullOrEmpty(cur.RequireUnlock) && !KillAccolades.HasUnlock(cur.RequireUnlock))
            {
                string hint = KillAccolades.UnlockHint(cur.RequireUnlock);
                detail += "\n" + (string.IsNullOrEmpty(hint)
                    ? ModUiLang.T("Locked", "锁定")
                    : hint);
            }
            else if (!string.IsNullOrEmpty(_hudMsg) && Time.unscaledTime <= _hudUntil)
                detail = _hudMsg.Replace("\n", "  ");

            GUI.Label(new Rect(box.x + 16f, y, box.width - 32f, 36f), detail, _labelStyle);
            y = box.yMax - 36f;
            float bw = (box.width - 48f) * 0.5f;
            TextAnchor prevAlign = _btnStyle.alignment;
            _btnStyle.alignment = TextAnchor.MiddleCenter;
            Color btnPrev = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.3f, 0.75f, 0.45f, 1f);
            if (GUI.Button(new Rect(box.x + 16f, y, bw, 30f),
                ModUiLang.T("CONFIRM USE", "确认使用"), _btnStyle))
                TryFire();
            GUI.backgroundColor = new Color(0.35f, 0.4f, 0.45f, 1f);
            if (GUI.Button(new Rect(box.x + 24f + bw, y, bw, 30f),
                ModUiLang.T("Close", "关闭"), _btnStyle))
                CloseMenu();
            GUI.backgroundColor = btnPrev;
            _btnStyle.alignment = prevAlign;

            GUI.color = prev;
        }

        private static void EnsureMenuStyles()
        {
            if (_titleStyle != null)
                return;
            _titleStyle = new GUIStyle(GUI.skin.label);
            _titleStyle.fontSize = 18;
            _titleStyle.fontStyle = FontStyle.Bold;
            _titleStyle.alignment = TextAnchor.MiddleLeft;
            _titleStyle.normal.textColor = new Color(0.75f, 1f, 0.85f, 1f);

            _labelStyle = new GUIStyle(GUI.skin.label);
            _labelStyle.fontSize = 13;
            _labelStyle.alignment = TextAnchor.MiddleLeft;
            _labelStyle.normal.textColor = new Color(0.85f, 0.95f, 0.9f, 0.95f);
            _labelStyle.wordWrap = true;

            _btnStyle = new GUIStyle(GUI.skin.button);
            _btnStyle.fontSize = 12;
            _btnStyle.fontStyle = FontStyle.Bold;
            _btnStyle.alignment = TextAnchor.MiddleLeft;
            _btnStyle.normal.textColor = Color.white;
            _btnStyle.clipping = TextClipping.Clip;
        }

        private static void CaptureCursor()
        {
            if (_cursorHeld)
                return;
#if ORITASY_COMBINED
            Oritasy.OritasyCursor.Hold();
#else
            _prevLock = Cursor.lockState;
            _prevCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
#endif
            _cursorHeld = true;
        }

        private static void HoldCursor()
        {
            if (!_cursorHeld)
                CaptureCursor();
#if ORITASY_COMBINED
            Oritasy.OritasyCursor.Pulse();
#else
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
#endif
        }

        private static void ReleaseCursor()
        {
            if (!_cursorHeld)
                return;
#if ORITASY_COMBINED
            Oritasy.OritasyCursor.Release();
#else
            Cursor.lockState = _prevLock;
            Cursor.visible = _prevCursorVisible;
#endif
            _cursorHeld = false;
        }

        private static float ResolveTipY(int slot, float chipH)
        {
            if (_cachedTipY > 0f
                && Time.unscaledTime < _nextTipYAt
                && _cachedTipScreenH == (int)GuiScale.Height)
                return _cachedTipY;

            _nextTipYAt = Time.unscaledTime + 0.5f;
            _cachedTipScreenH = (int)GuiScale.Height;
            float y = ResolveTipYUncached(slot, chipH);
            _cachedTipY = y;
            return y;
        }

        private static float ResolveTipYUncached(int slot, float chipH)
        {
            try
            {
                if (!_cornerChipYResolved)
                {
                    _cornerChipYResolved = true;
                    Type t = Type.GetType("Oritasy.PlayerAutopilot, Oritasy")
                        ?? Type.GetType("Oritasy.PlayerAutopilot");
                    if (t != null)
                    {
                        _cornerChipYMethod = t.GetMethod("CornerChipY",
                            System.Reflection.BindingFlags.Static
                            | System.Reflection.BindingFlags.Public
                            | System.Reflection.BindingFlags.NonPublic);
                    }
                }
                if (_cornerChipYMethod != null)
                {
                    CornerChipYArgs[0] = slot;
                    return (float)_cornerChipYMethod.Invoke(null, CornerChipYArgs);
                }
            }
            catch { }
            return CornerStackTopY() + slot * (chipH + 3f);
        }

        private static void CloseOtherModMenus()
        {
            TryInvokeStatic("Oritasy.PlayerAutopilot", "CloseMenuFromOutside");
            TryInvokeStatic("Oritasy.AerialResupply", "CloseMenuFromOutside");
            TryInvokeStatic("Oritasy.WarThunderRwrHud", "CloseLayoutMenuFromOutside");
            TryInvokeStatic("Oritasy.AircraftManeuverGui", "Close");
            TryInvokeStatic("Oritasy.BeginnerAssist", "CloseMenuFromOutside");
            TryInvokeStatic("Oritasy.IlsSettingsMenu", "CloseMenuFromOutside");
            TryInvokeStatic("Oritasy.PrivateMessageMenu", "CloseMenuFromOutside");
            TryInvokeStatic("Oritasy.KillChoiceMenu", "CloseMenuFromOutside");
            TryInvokeStatic("Oritasy.HostFundMenu", "CloseMenuFromOutside");
            TryInvokeStatic("Oritasy.AirframeWearGui", "CloseFromOutside");
        }

        private static bool OritasyAllowsThirdPersonUi()
        {
            try
            {
                Type t = Type.GetType("Oritasy.Plugin, Oritasy") ?? Type.GetType("Oritasy.Plugin");
                if (t == null)
                    return true;
                System.Reflection.PropertyInfo p = t.GetProperty("AllowThirdPersonUi",
                    System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic);
                if (p == null)
                    return true;
                return (bool)p.GetValue(null, null);
            }
            catch { return true; }
        }

        private static void TryInvokeStatic(string typeName, string method)
        {
            try
            {
                Type t = Type.GetType(typeName + ", Oritasy") ?? Type.GetType(typeName);
                if (t == null)
                    return;
                System.Reflection.MethodInfo m = t.GetMethod(method,
                    System.Reflection.BindingFlags.Static
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic);
                if (m != null)
                    m.Invoke(null, null);
            }
            catch { }
        }

        private static bool IsLocalHumanReady()
        {
            Player local;
            if (!GameManager.GetLocalPlayer(out local) || local == null)
                return false;
            return Plugin.IsLocalHumanPlayer(local);
        }

        private static bool IsLocalHumanReadyCached()
        {
            if (Time.unscaledTime < _nextLocalReadyAt)
                return _cachedLocalReady;
            _nextLocalReadyAt = Time.unscaledTime + 0.35f;
            _cachedLocalReady = IsLocalHumanReady();
            return _cachedLocalReady;
        }

        private static bool IsServerActive()
        {
            try
            {
                NetworkManagerNuclearOption nm = NetworkManagerNuclearOption.i;
                return nm != null && nm.Server != null && nm.Server.Active;
            }
            catch
            {
                return false;
            }
        }

        private static void TryFire()
        {
            ArsenalOption opt = Options[_index];
            float now = Time.unscaledTime;
            if (StrategicArsenalLifecycleService.IsOnCooldown(now, _readyAt))
            {
                int left = StrategicArsenalLifecycleService.RemainingCooldownSec(now, _readyAt);
                ShowHud(ModUiLang.T(
                    "Cooldown " + left + "s | " + FormatSelection(),
                    "冷却 " + left + "s | " + FormatSelection()));
                return;
            }
            if (!IsServerActive())
            {
                ShowHud(ModUiLang.T(
                    "F9 Support requires host / listen-server.",
                    "F9 支援需主机 / 监听服务器。"));
                return;
            }

            Player local;
            if (!GameManager.GetLocalPlayer(out local) || local == null || !Plugin.IsLocalHumanPlayer(local))
            {
                ShowHud(ModUiLang.T(
                    "Local human player only (AI disabled).",
                    "仅限本地玩家（AI 不可用）。"));
                return;
            }

            if (!string.IsNullOrEmpty(opt.RequireUnlock) && !KillAccolades.HasUnlock(opt.RequireUnlock))
            {
                string hint = KillAccolades.UnlockHint(opt.RequireUnlock);
                ShowHud(ModUiLang.T(
                    "LOCKED | " + opt.Label + "\n" + (hint != null ? hint : "Requires kill accolades."),
                    "锁定 | " + opt.Label + "\n" + (hint != null ? hint : "需击杀成就解锁。")));
                return;
            }

            float cost = opt.CostM;
            if (local.Allocation + 0.001f < cost)
            {
                ShowHud(ModUiLang.T(
                    "Not enough funds: need " + StrategicArsenalMathService.FormatCostM(cost) + "M (have "
                    + local.Allocation.ToString("0.0") + "M)",
                    "资金不足：需要 " + StrategicArsenalMathService.FormatCostM(cost) + "M  现有 "
                    + local.Allocation.ToString("0.0") + "M"));
                return;
            }

            bool ok = false;
            if (opt.Kind == ArsenalKind.Carrier)
                ok = TryBuyCarrier(local, opt);
            else
                ok = TrySpawnMissiles(local, opt);

            if (!ok)
                return;

            try { local.AddAllocation(-cost); }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("StrategicArsenal: AddAllocation failed: " + ex.Message);
                ShowHud(ModUiLang.T("Spawned but fund deduct failed.", "已生成但扣款失败。"));
                return;
            }

            _readyAt = StrategicArsenalLifecycleService.ScheduleReadyAt(
                Time.unscaledTime,
                _cooldown != null ? _cooldown.Value : DefaultCooldown);
            float cd = StrategicArsenalMathService.CooldownSeconds(
                _cooldown != null ? _cooldown.Value : DefaultCooldown);
            ShowHud(ModUiLang.T(
                "OK -" + StrategicArsenalMathService.FormatCostM(cost) + "M | " + opt.Label + " | CD " + cd.ToString("0") + "s",
                "完成 -" + StrategicArsenalMathService.FormatCostM(cost) + "M | " + opt.Label + " | 冷却 " + cd.ToString("0") + "s"));
            if (Plugin.Log != null)
                Plugin.Log.LogInfo("StrategicArsenal: " + opt.Label + " cost=" + cost + "M");
        }

        private static bool TrySpawnMissiles(Player local, ArsenalOption opt)
        {
            int count = StrategicArsenalMathService.ClampSalvoCount(opt.Count, opt.AllowAboveFive);

            MissileDefinition def = FindMissile(opt.JsonKey);
            if (def == null || def.unitPrefab == null)
            {
                ShowHud(ModUiLang.T("Missing missile def: " + opt.JsonKey,
                    "缺少导弹定义：" + opt.JsonKey));
                return false;
            }

            Spawner spawner = null;
            try { spawner = NetworkSceneSingleton<Spawner>.i; }
            catch { }
            if (spawner == null)
            {
                ShowHud(ModUiLang.T("Spawner unavailable.", "生成器不可用。"));
                return false;
            }

            Unit owner = ResolveOwner(local);
            if (owner == null)
            {
                ShowHud(ModUiLang.T(
                    "Need a unit first (spawn / enter an aircraft).",
                    "请先生成或进入飞机。"));
                return false;
            }

            Vector3 center = ResolveCenter(owner);
            float alt = _altitude != null ? _altitude.Value : DefaultAltitude;
            if (alt < 50000f)
                alt = DefaultAltitude;
            float spread = _spread != null ? _spread.Value : DefaultSpread;
            List<Unit> hostiles = CollectHostiles(owner, center, StrategicArsenalMathService.HostileScanRangeM);

            int spawned = 0;
            for (int i = 0; i < count; i++)
            {
                Unit tgt = null;
                if (hostiles.Count > 0)
                    tgt = hostiles[i % hostiles.Count];

                Vector3 drop = tgt != null ? tgt.transform.position : (center + owner.transform.forward * 8000f);
                Vector3 offset = StrategicArsenalMathService.SalvoOffset(i, count, spread);
                Vector3 pos = StrategicArsenalMathService.SpawnPosition(drop, offset, alt);

                Vector3 dir = StrategicArsenalMathService.DownwardAim();
                float speed = StrategicArsenalMathService.SpeedMsFromMach(opt.SpeedMach);
                Vector3 vel = dir * speed;
                Quaternion rot = Quaternion.LookRotation(dir, Vector3.forward);

                try
                {
                    Missile m = spawner.SpawnMissile(def, pos, rot, vel, null, owner);
                    if (m == null)
                        continue;
                    spawned++;
                    StampF9Flight(m, opt, speed);
                    F9DropMark mark = m.gameObject.GetComponent<F9DropMark>();
                    if (mark == null)
                        mark = m.gameObject.AddComponent<F9DropMark>();
                    bool tbmDrop = opt.JsonKey != null
                        && opt.JsonKey.IndexOf("ballistic", StringComparison.OrdinalIgnoreCase) >= 0;
                    mark.Configure(tbmDrop);
                    ForceNoseDown(m, speed, pos);
                    if (tgt != null)
                    {
                        try
                        {
                            if (!tgt.disabled)
                                m.SetTarget(tgt);
                        }
                        catch { }
                    }
                    DropHold hold = m.gameObject.GetComponent<DropHold>();
                    if (hold == null)
                        hold = m.gameObject.AddComponent<DropHold>();
                    hold.Configure(speed, tgt, pos);
                    TerminalSpeedCap cap = m.gameObject.GetComponent<TerminalSpeedCap>();
                    if (cap == null)
                        cap = m.gameObject.AddComponent<TerminalSpeedCap>();
                    cap.Configure(StrategicArsenalMathService.TerminalSpeedMs(speed));
                }
                catch (Exception ex)
                {
                    if (Plugin.DebugLog != null && Plugin.DebugLog.Value)
                        Plugin.Log.LogWarning("StrategicArsenal spawn: " + ex.Message);
                }
            }

            if (spawned <= 0)
            {
                ShowHud(ModUiLang.T("Spawn failed (0 missiles).", "生成失败（0 枚导弹）。"));
                return false;
            }
            return true;
        }

        private static bool TryBuyCarrier(Player local, ArsenalOption opt)
        {
            FactionHQ hq = null;
            try { GameManager.GetLocalHQ(out hq); }
            catch { }
            if (hq == null)
            {
                try { hq = local.HQ; }
                catch { }
            }
            if (hq == null)
            {
                ShowHud(ModUiLang.T("No local FactionHQ.", "无本地阵营总部。"));
                return false;
            }

            int friendly = CountFriendlyCarriers(hq);
            if (friendly >= 3)
            {
                ShowHud(ModUiLang.T(
                    "Allied carriers full " + friendly + "/3",
                    "友军航母已满 " + friendly + "/3"));
                return false;
            }

            ShipDefinition shipDef = FindShip(opt.JsonKey);
            if (shipDef == null || shipDef.unitPrefab == null)
                shipDef = FindAnyCarrierDef();
            if (shipDef == null || shipDef.unitPrefab == null)
            {
                ShowHud(ModUiLang.T("Missing ship def: " + opt.JsonKey,
                    "缺少舰船定义：" + opt.JsonKey));
                return false;
            }

            Spawner spawner = null;
            try { spawner = NetworkSceneSingleton<Spawner>.i; }
            catch { }
            if (spawner == null)
            {
                ShowHud(ModUiLang.T("Spawner unavailable.", "生成器不可用。"));
                return false;
            }

            Unit owner = ResolveOwner(local);
            Vector3 center = owner != null ? ResolveCenter(owner) : Vector3.zero;
            Vector3 spawnPos;
            Vector3 fwd;
            if (!TryFindCarrierWaterSpawn(owner, center, out spawnPos, out fwd))
            {
                Vector3 rearCenter;
                Vector3 frontDir;
                ResolveBattleAxis(owner, center, out rearCenter, out frontDir);
                spawnPos = rearCenter - frontDir * 6000f;
                spawnPos.y = ReadSeaY() + 5.5f;
                fwd = frontDir;
            }

            string unique = "WeXon_CV_" + DateTime.UtcNow.Ticks.ToString();
            try
            {
                Ship ship = spawner.SpawnShip(
                    shipDef.unitPrefab,
                    spawnPos.ToGlobalPosition(),
                    Quaternion.LookRotation(fwd, Vector3.up),
                    hq,
                    unique,
                    0.75f,
                    false);
                if (ship == null)
                {
                    ShowHud(ModUiLang.T("Carrier spawn failed.", "航母生成失败。"));
                    return false;
                }
            }
            catch (Exception ex)
            {
                ShowHud(ModUiLang.T("Carrier spawn error: " + ex.Message,
                    "航母生成错误：" + ex.Message));
                return false;
            }
            return true;
        }

        private static int CountFriendlyCarriers(FactionHQ hq)
        {
            int n = 0;
            List<Unit> all = UnitRegistry.allUnits;
            if (all == null)
                return 0;
            for (int i = 0; i < all.Count; i++)
            {
                Ship ship = all[i] as Ship;
                if (ship == null || !Plugin.IsUnitAlive(ship))
                    continue;
                if (!object.ReferenceEquals(Plugin.GetHq(ship), hq))
                    continue;
                if (IsCarrier(ship))
                    n++;
            }
            return n;
        }

        private static bool IsCarrier(Ship ship)
        {
            if (ship == null)
                return false;
            try
            {
                UnitDefinition ud = ship.definition;
                if (ud != null && !string.IsNullOrEmpty(ud.jsonKey))
                {
                    string k = ud.jsonKey;
                    if (string.Equals(k, "FleetCarrier1", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(k, "AssaultCarrier1", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                ShipDefinition sd = ud as ShipDefinition;
                if (sd != null && (sd.shipType == ShipType.CV || sd.shipType == ShipType.LHA))
                    return true;
            }
            catch { }
            return false;
        }

        private static Unit ResolveOwner(Player local)
        {
            Aircraft ac;
            if (GameManager.GetLocalAircraft(out ac) && ac != null && Plugin.IsUnitAlive(ac))
                return ac;

            FactionHQ hq = null;
            try { GameManager.GetLocalHQ(out hq); }
            catch { }
            if (hq == null && local != null)
            {
                try { hq = local.HQ; }
                catch { }
            }
            if (hq == null || UnitRegistry.allUnits == null)
                return null;

            Unit fallback = null;
            for (int i = 0; i < UnitRegistry.allUnits.Count; i++)
            {
                Unit u = UnitRegistry.allUnits[i];
                if (u == null || u is Missile || !Plugin.IsUnitAlive(u))
                    continue;
                if (!object.ReferenceEquals(Plugin.GetHq(u), hq))
                    continue;
                if (u is Aircraft)
                    return u;
                if (fallback == null)
                    fallback = u;
            }
            return fallback;
        }

        private static Vector3 ResolveCenter(Unit owner)
        {
            if (owner == null)
                return Vector3.zero;
            try { return owner.transform.position; }
            catch { return Vector3.zero; }
        }

        private static List<Unit> CollectHostiles(Unit owner, Vector3 origin, float radius)
        {
            List<Unit> list = new List<Unit>();
            List<Unit> all = UnitRegistry.allUnits;
            if (all == null || owner == null)
                return list;
            float r2 = radius * radius;
            FactionHQ oh = Plugin.GetHq(owner);
            for (int i = 0; i < all.Count; i++)
            {
                Unit u = all[i];
                if (u == null || u is Missile || u is Scenery || !Plugin.IsUnitAlive(u))
                    continue;
                FactionHQ th = Plugin.GetHq(u);
                if (oh == null || th == null || object.ReferenceEquals(oh, th))
                    continue;
                Vector3 d = u.transform.position - origin;
                if (d.sqrMagnitude > r2)
                    continue;
                list.Add(u);
            }
            return list;
        }

        private static MissileDefinition FindMissile(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;
            try
            {
                if (Encyclopedia.Lookup != null && Encyclopedia.Lookup.ContainsKey(key))
                {
                    MissileDefinition via = Encyclopedia.Lookup[key] as MissileDefinition;
                    if (via != null)
                        return via;
                }
            }
            catch { }

            Encyclopedia enc = Plugin.GetEncyclopedia();
            if (enc != null && enc.missiles != null)
            {
                for (int i = 0; i < enc.missiles.Count; i++)
                {
                    MissileDefinition d = enc.missiles[i];
                    if (d != null && string.Equals(d.jsonKey, key, StringComparison.OrdinalIgnoreCase))
                        return d;
                }
            }

            MissileDefinition[] all = Resources.FindObjectsOfTypeAll<MissileDefinition>();
            if (all != null)
            {
                for (int i = 0; i < all.Length; i++)
                {
                    MissileDefinition d = all[i];
                    if (d != null && string.Equals(d.jsonKey, key, StringComparison.OrdinalIgnoreCase))
                        return d;
                }
            }
            return null;
        }

        private static ShipDefinition FindShip(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;
            try
            {
                if (Encyclopedia.Lookup != null && Encyclopedia.Lookup.ContainsKey(key))
                {
                    ShipDefinition via = Encyclopedia.Lookup[key] as ShipDefinition;
                    if (via != null)
                        return via;
                }
            }
            catch { }

            Encyclopedia enc = Plugin.GetEncyclopedia();
            if (enc != null && enc.ships != null)
            {
                for (int i = 0; i < enc.ships.Count; i++)
                {
                    ShipDefinition d = enc.ships[i];
                    if (d != null && string.Equals(d.jsonKey, key, StringComparison.OrdinalIgnoreCase))
                        return d;
                }
            }

            ShipDefinition[] all = Resources.FindObjectsOfTypeAll<ShipDefinition>();
            if (all != null)
            {
                for (int i = 0; i < all.Length; i++)
                {
                    ShipDefinition d = all[i];
                    if (d != null && string.Equals(d.jsonKey, key, StringComparison.OrdinalIgnoreCase))
                        return d;
                }
            }
            return null;
        }

        private static ShipDefinition FindAnyCarrierDef()
        {
            ShipDefinition d = FindShip("FleetCarrier1");
            if (d != null && d.unitPrefab != null)
                return d;
            d = FindShip("AssaultCarrier1");
            if (d != null && d.unitPrefab != null)
                return d;

            Encyclopedia enc = Plugin.GetEncyclopedia();
            if (enc != null && enc.ships != null)
            {
                for (int i = 0; i < enc.ships.Count; i++)
                {
                    ShipDefinition s = enc.ships[i];
                    if (s == null || s.unitPrefab == null)
                        continue;
                    if (s.shipType == ShipType.CV || s.shipType == ShipType.LHA)
                        return s;
                }
            }
            return null;
        }

        private static bool TryFindCarrierWaterSpawn(Unit owner, Vector3 center, out Vector3 spawnPos, out Vector3 fwd)
        {
            spawnPos = Vector3.zero;
            Vector3 friendlyCenter;
            Vector3 frontDir;
            ResolveBattleAxis(owner, center, out friendlyCenter, out frontDir);
            fwd = frontDir;

            Vector3 best = Vector3.zero;
            float bestScore = float.MinValue;
            bool found = false;
            if (TryScoreRearWater(owner, friendlyCenter, frontDir, 12000f, ref best, ref bestScore, ref found)
                || TryScoreRearWater(owner, friendlyCenter, frontDir, 8000f, ref best, ref bestScore, ref found)
                || TryScoreRearWater(owner, friendlyCenter, frontDir, 0f, ref best, ref bestScore, ref found))
            {
                spawnPos = best;
                return true;
            }
            return false;
        }

        private static void ResolveBattleAxis(Unit owner, Vector3 ownerPos, out Vector3 friendlyCenter, out Vector3 frontDir)
        {
            friendlyCenter = Flatten(ownerPos);
            frontDir = Vector3.forward;
            try
            {
                if (owner != null && owner.transform != null)
                    frontDir = Flatten(owner.transform.forward);
            }
            catch { }
            if (frontDir.sqrMagnitude < 0.01f)
                frontDir = Vector3.forward;
            frontDir.Normalize();

            FactionHQ hq = owner != null ? Plugin.GetHq(owner) : null;
            Vector3 fSum = Vector3.zero;
            int fN = 0;
            Vector3 eSum = Vector3.zero;
            int eN = 0;

            if (hq != null)
            {
                try
                {
                    foreach (Airbase ab in hq.GetAirbases())
                    {
                        if (ab == null)
                            continue;
                        try
                        {
                            if (ab.disabled)
                                continue;
                        }
                        catch { }
                        Vector3 p = Vector3.zero;
                        try
                        {
                            p = ab.center != null ? ab.center.position : ab.transform.position;
                        }
                        catch { continue; }
                        fSum += Flatten(p);
                        fN++;
                    }
                }
                catch { }
            }

            List<Unit> all = UnitRegistry.allUnits;
            if (all != null)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    Unit u = all[i];
                    if (u == null || u is Missile || u is Scenery || !Plugin.IsUnitAlive(u))
                        continue;
                    FactionHQ th = Plugin.GetHq(u);
                    Vector3 p;
                    try { p = Flatten(u.transform.position); }
                    catch { continue; }
                    if (hq != null && th != null && object.ReferenceEquals(th, hq))
                    {
                        if (u is Ship || u is Building)
                        {
                            fSum += p;
                            fN++;
                        }
                    }
                    else if (hq != null && th != null && !object.ReferenceEquals(th, hq))
                    {
                        if (u is Ship || u is Building || u is GroundVehicle)
                        {
                            eSum += p;
                            eN++;
                        }
                    }
                }
            }

            if (fN > 0)
                friendlyCenter = fSum / fN;
            if (eN > 0)
            {
                Vector3 axis = Flatten((eSum / eN) - friendlyCenter);
                if (axis.sqrMagnitude > 1f)
                    frontDir = axis.normalized;
            }
        }

        private static bool TryScoreRearWater(
            Unit owner,
            Vector3 friendlyCenter,
            Vector3 frontDir,
            float minEnemyM,
            ref Vector3 best,
            ref float bestScore,
            ref bool found)
        {
            FactionHQ hq = owner != null ? Plugin.GetHq(owner) : null;
            Vector3 right = new Vector3(frontDir.z, 0f, -frontDir.x);
            float[] backs = new float[]
            {
                3000f, 5000f, 7000f, 9000f, 11000f
            };
            float[] sides = new float[]
            {
                0f, 2500f, -2500f, 5000f, -5000f
            };
            for (int b = 0; b < backs.Length; b++)
            {
                for (int s = 0; s < sides.Length; s++)
                {
                    Vector3 cand = friendlyCenter - frontDir * backs[b] + right * sides[s];
                    ConsiderCarrierWater(cand, friendlyCenter, frontDir, hq, minEnemyM,
                        ref best, ref bestScore, ref found);
                }
            }

            List<Unit> all = UnitRegistry.allUnits;
            if (all != null)
            {
                for (int i = 0; i < all.Count; i++)
                {
                    Ship ship = all[i] as Ship;
                    if (ship == null || !Plugin.IsUnitAlive(ship))
                        continue;
                    if (hq != null && !object.ReferenceEquals(Plugin.GetHq(ship), hq))
                        continue;
                    Vector3 p;
                    try { p = Flatten(ship.transform.position); }
                    catch { continue; }
                    ConsiderCarrierWater(p - frontDir * 3500f, friendlyCenter, frontDir, hq, minEnemyM,
                        ref best, ref bestScore, ref found);
                    ConsiderCarrierWater(p - frontDir * 5000f + right * 3000f, friendlyCenter, frontDir, hq, minEnemyM,
                        ref best, ref bestScore, ref found);
                    ConsiderCarrierWater(p - frontDir * 5000f - right * 3000f, friendlyCenter, frontDir, hq, minEnemyM,
                        ref best, ref bestScore, ref found);
                }
            }

            return found;
        }

        private static void ConsiderCarrierWater(
            Vector3 cand,
            Vector3 friendlyCenter,
            Vector3 frontDir,
            FactionHQ hq,
            float minEnemyM,
            ref Vector3 best,
            ref float bestScore,
            ref bool found)
        {
            Vector3 water;
            if (!TryProbeWater(cand, out water))
                return;
            water.y += 5.5f;
            if (TooCloseToFriendlyCarrier(water, hq, 2800f))
                return;

            float enemyD = Mathf.Sqrt(NearestHostileDistSq(water, hq));
            if (minEnemyM > 0.1f && enemyD < minEnemyM)
                return;

            Vector3 fromFriend = Flatten(water - friendlyCenter);
            float rear = -Vector3.Dot(fromFriend, frontDir);
            if (minEnemyM > 0.1f)
            {
                if (rear < 2500f)
                    return;
                if (rear > 12000f)
                    return;
            }

            float friendD = fromFriend.magnitude;
            float rearErr = Mathf.Abs(rear - 7000f);
            float score = -rearErr * 2.2f + Mathf.Min(enemyD, 25000f) * 0.35f;
            if (rear < 2500f)
                score -= 15000f;
            if (rear > 10000f)
                score -= (rear - 10000f) * 2.8f;
            if (friendD > 16000f)
                score -= (friendD - 16000f) * 0.9f;
            if (rear < 0f)
                score -= 25000f;
            if (!found || score > bestScore)
            {
                best = water;
                bestScore = score;
                found = true;
            }
        }

        private static float NearestHostileDistSq(Vector3 pos, FactionHQ hq)
        {
            float best = float.MaxValue;
            if (hq == null)
                return best;
            List<Unit> all = UnitRegistry.allUnits;
            if (all == null)
                return best;
            for (int i = 0; i < all.Count; i++)
            {
                Unit u = all[i];
                if (u == null || u is Missile || u is Scenery || !Plugin.IsUnitAlive(u))
                    continue;
                FactionHQ th = Plugin.GetHq(u);
                if (th == null || object.ReferenceEquals(th, hq))
                    continue;
                if (!(u is Ship) && !(u is Building) && !(u is GroundVehicle))
                    continue;
                Vector3 d = Flatten(u.transform.position) - Flatten(pos);
                float sq = d.sqrMagnitude;
                if (sq < best)
                    best = sq;
            }
            return best;
        }

        private static bool TooCloseToFriendlyCarrier(Vector3 pos, FactionHQ hq, float minM)
        {
            if (hq == null)
                return false;
            float minSq = minM * minM;
            List<Unit> all = UnitRegistry.allUnits;
            if (all == null)
                return false;
            for (int i = 0; i < all.Count; i++)
            {
                Ship ship = all[i] as Ship;
                if (ship == null || !Plugin.IsUnitAlive(ship) || !IsCarrier(ship))
                    continue;
                if (!object.ReferenceEquals(Plugin.GetHq(ship), hq))
                    continue;
                Vector3 d = Flatten(ship.transform.position) - Flatten(pos);
                if (d.sqrMagnitude < minSq)
                    return true;
            }
            return false;
        }

        private static Vector3 Flatten(Vector3 v)
        {
            v.y = 0f;
            return v;
        }

        private static bool TryPickWaterNear(Vector3 center, Vector3 fwd, out Vector3 spawnPos, out Vector3 pickedFwd)
        {
            spawnPos = Vector3.zero;
            pickedFwd = fwd;
            Vector3 water;
            if (TryProbeWater(center + fwd * 2500f, out water))
            {
                spawnPos = water;
                spawnPos.y += 5.5f;
                pickedFwd = fwd;
                return true;
            }
            if (TryProbeWater(center, out water))
            {
                spawnPos = water;
                spawnPos.y += 5.5f;
                pickedFwd = fwd;
                return true;
            }

            float[] dists = new float[]
            {
                800f, 1500f, 2500f, 4000f, 6000f, 8000f,
                12000f, 16000f, 24000f, 32000f, 48000f
            };
            float[] angs = new float[]
            {
                0f, 30f, -30f, 60f, -60f, 90f, -90f,
                120f, -120f, 150f, -150f, 180f
            };
            for (int d = 0; d < dists.Length; d++)
            {
                for (int a = 0; a < angs.Length; a++)
                {
                    Vector3 dir = Quaternion.Euler(0f, angs[a], 0f) * fwd;
                    Vector3 cand = center + dir * dists[d];
                    if (!TryProbeWater(cand, out water))
                        continue;
                    spawnPos = water;
                    spawnPos.y += 5.5f;
                    pickedFwd = dir;
                    return true;
                }
            }
            return false;
        }

        private static bool TryProbeWater(Vector3 xz, out Vector3 water)
        {
            water = xz;
            float seaY = ReadSeaY();
            Vector3 origin = new Vector3(xz.x, Mathf.Max(8000f, seaY + 8000f), xz.z);
            float cast = Mathf.Max(20000f, origin.y - (seaY - 400f));

            RaycastHit waterHit;
            bool hitWater = false;
            try
            {
                hitWater = Physics.Raycast(origin, Vector3.down, out waterHit, cast,
                    PhysicsLayers.WaterMask, QueryTriggerInteraction.Collide);
            }
            catch
            {
                hitWater = false;
                waterHit = default(RaycastHit);
            }

            RaycastHit landHit;
            bool hitLand = RaycastLand(origin, cast, out landHit);
            if (hitLand && LooksLikeWaterCollider(landHit.collider))
            {
                water = landHit.point;
                return true;
            }

            if (hitWater)
            {
                if (hitLand && landHit.point.y > waterHit.point.y + 8f)
                    return false;
                water = waterHit.point;
                return true;
            }

            if (!hitLand || landHit.point.y <= seaY + 4f)
            {
                water = new Vector3(xz.x, seaY, xz.z);
                return true;
            }
            return false;
        }

        private static bool TryPickLowestSurface(Vector3 center, Vector3 fwd, out Vector3 spawnPos, out Vector3 pickedFwd)
        {
            spawnPos = Vector3.zero;
            pickedFwd = fwd;
            float seaY = ReadSeaY();
            float bestY = float.MaxValue;
            Vector3 best = Vector3.zero;
            bool found = false;

            float[] dists = new float[]
            {
                2000f, 5000f, 10000f, 20000f, 40000f, 60000f
            };
            float[] angs = new float[]
            {
                0f, 30f, -30f, 60f, -60f, 90f, -90f,
                120f, -120f, 150f, -150f, 180f
            };
            for (int d = 0; d < dists.Length; d++)
            {
                for (int a = 0; a < angs.Length; a++)
                {
                    Vector3 dir = Quaternion.Euler(0f, angs[a], 0f) * fwd;
                    Vector3 cand = center + dir * dists[d];
                    float y;
                    bool water;
                    if (!TrySampleSurface(cand, out y, out water))
                        continue;
                    if (water)
                    {
                        spawnPos = new Vector3(cand.x, y + 5.5f, cand.z);
                        pickedFwd = dir;
                        return true;
                    }
                    if (y >= bestY)
                        continue;
                    bestY = y;
                    best = new Vector3(cand.x, y + 5.5f, cand.z);
                    pickedFwd = dir;
                    found = true;
                }
            }

            if (!found)
                return false;
            spawnPos = best;
            if (spawnPos.y < seaY + 5.5f)
                spawnPos.y = seaY + 5.5f;
            return true;
        }

        private static bool TrySampleSurface(Vector3 xz, out float y, out bool water)
        {
            y = 0f;
            water = false;
            Vector3 w;
            if (TryProbeWater(xz, out w))
            {
                y = w.y;
                water = true;
                return true;
            }

            float seaY = ReadSeaY();
            Vector3 origin = new Vector3(xz.x, Mathf.Max(8000f, seaY + 8000f), xz.z);
            float cast = Mathf.Max(20000f, origin.y - (seaY - 400f));
            RaycastHit landHit;
            if (!RaycastLand(origin, cast, out landHit))
                return false;
            y = landHit.point.y;
            water = LooksLikeWaterCollider(landHit.collider);
            return true;
        }

        private static bool RaycastLand(Vector3 origin, float cast, out RaycastHit landHit)
        {
            int landMask = 0;
            try { landMask |= PhysicsLayers.StaticsMask.value; }
            catch { }
            try { landMask |= PhysicsLayers.DefaultMask.value; }
            catch { }
            if (landMask == 0)
                landMask = 1;
            return Physics.Raycast(origin, Vector3.down, out landHit, cast,
                landMask, QueryTriggerInteraction.Ignore);
        }

        private static bool LooksLikeWaterCollider(Collider col)
        {
            if (col == null)
                return false;
            try
            {
                if (((1 << col.gameObject.layer) & PhysicsLayers.WaterMask.value) != 0)
                    return true;
            }
            catch { }
            string n = col.name;
            if (string.IsNullOrEmpty(n))
            {
                try { n = col.gameObject.name; }
                catch { n = null; }
            }
            if (string.IsNullOrEmpty(n))
                return false;
            return n.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("ocean", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("sea", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("lake", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("river", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("harbor", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("harbour", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static float ReadSeaY()
        {
            try { return Datum.LocalSeaY; }
            catch { return 0f; }
        }

        private static bool TryCopyNearbyShipWater(Unit owner, Vector3 center, out Vector3 spawnPos, out Vector3 fwd)
        {
            spawnPos = Vector3.zero;
            fwd = Vector3.forward;
            List<Unit> all = UnitRegistry.allUnits;
            if (all == null)
                return false;
            FactionHQ hq = owner != null ? Plugin.GetHq(owner) : null;
            Ship best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < all.Count; i++)
            {
                Ship ship = all[i] as Ship;
                if (ship == null || !Plugin.IsUnitAlive(ship))
                    continue;
                Vector3 p;
                try { p = ship.transform.position; }
                catch { continue; }
                if (p.y > 80f)
                    continue;
                float d = (p - center).sqrMagnitude;
                if (hq != null && object.ReferenceEquals(Plugin.GetHq(ship), hq))
                    d *= 0.25f;
                if (d >= bestD)
                    continue;
                bestD = d;
                best = ship;
            }
            if (best == null)
                return false;

            Vector3 origin;
            try { origin = best.transform.position; }
            catch { return false; }
            try
            {
                fwd = best.transform.forward;
                fwd.y = 0f;
                if (fwd.sqrMagnitude < 0.01f)
                    fwd = Vector3.forward;
                fwd.Normalize();
            }
            catch { }

            float[] dists = new float[] { 3000f, 5000f, 8000f };
            float[] angs = new float[] { 90f, -90f, 0f, 180f, 45f, -45f };
            for (int d = 0; d < dists.Length; d++)
            {
                for (int a = 0; a < angs.Length; a++)
                {
                    Vector3 dir = Quaternion.Euler(0f, angs[a], 0f) * fwd;
                    Vector3 cand = origin + dir * dists[d];
                    Vector3 water;
                    if (!TryProbeWater(cand, out water))
                        continue;
                    spawnPos = water;
                    spawnPos.y = origin.y;
                    fwd = dir;
                    return true;
                }
            }

            spawnPos = origin + fwd * 3000f;
            spawnPos.y = origin.y;
            return true;
        }

        private static string FormatSelection()
        {
            ArsenalOption o = Options[_index];
            string cd;
            int cdSec = StrategicArsenalLifecycleService.RemainingCooldownSec(Time.unscaledTime, _readyAt);
            if (cdSec > 0)
                cd = ModUiLang.T("CD " + cdSec + "s", "冷却 " + cdSec + "s");
            else
                cd = ModUiLang.T("READY", "就绪");
            string qty = o.Kind == ArsenalKind.Carrier
                ? "x1"
                : ("x" + o.Count.ToString());
            string lockTxt = string.Empty;
            if (!string.IsNullOrEmpty(o.RequireUnlock) && !KillAccolades.HasUnlock(o.RequireUnlock))
            {
                string hint = KillAccolades.UnlockHint(o.RequireUnlock);
                lockTxt = ModUiLang.T(" | LOCKED", " | 锁定");
                if (!string.IsNullOrEmpty(hint))
                    lockTxt = " | " + hint;
            }
            return "[" + (_index + 1).ToString() + "/" + Options.Length.ToString() + "] "
                + o.Label + " " + qty + " | " + StrategicArsenalMathService.FormatCostM(o.CostM) + "M | " + cd + lockTxt
                + ModUiLang.T("\nF9 menu   [ / ] cycle", "\nF9 菜单   [ / ] 切换");
        }

        private static void ForceNoseDown(Missile m, float speed, Vector3 spawn)
        {
            if (m == null)
                return;
            Quaternion down = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            try { m.transform.position = spawn; }
            catch { }
            try { m.transform.rotation = down; }
            catch { }
            Rigidbody rb = null;
            try { rb = m.rb; }
            catch { }
            if (rb == null)
                return;
            try
            {
                rb.position = spawn;
                rb.rotation = down;
                rb.angularVelocity = Vector3.zero;
                rb.velocity = Vector3.down * speed;
                m.startingVelocity = rb.velocity;
            }
            catch { }
        }

        private static readonly FieldInfo MissileGLimit =
            AccessTools.Field(typeof(Missile), "gLimit");
        private static readonly FieldInfo MissileInfo =
            AccessTools.Field(typeof(Missile), "info");

        /// <summary>
        /// Label Mach is exoatmospheric. F9 TBM uses a modest gLimit (not 0).
        /// </summary>
        private static void StampF9Flight(Missile m, ArsenalOption opt, float launchSpeedMs)
        {
            if (m == null)
                return;
            bool tbm = opt != null && opt.JsonKey != null
                && opt.JsonKey.IndexOf("ballistic", StringComparison.OrdinalIgnoreCase) >= 0;
            if (tbm && MissileGLimit != null)
            {
                try { MissileGLimit.SetValue(m, StrategicArsenalMathService.F9TbmGLimit); }
                catch { }
            }
            if (launchSpeedMs <= 40f || MissileInfo == null)
                return;
            try
            {
                WeaponInfo info = MissileInfo.GetValue(m) as WeaponInfo;
                if (info == null)
                    return;
                WeaponInfo clone = UnityEngine.Object.Instantiate(info);
                clone.maxSpeed = launchSpeedMs;
                MissileInfo.SetValue(m, clone);
            }
            catch { }
        }

        /// <summary>F9 drop stays nose-down on the spawn vertical until below 20 km.</summary>
        private sealed class DropHold : MonoBehaviour
        {
            private float _speed;
            private float _pinX;
            private float _pinZ;
            private Unit _pending;
            private Rigidbody _rb;
            private Missile _missile;
            private bool _handed;

            public void Configure(float speedMs, Unit pending, Vector3 spawn)
            {
                _speed = speedMs > 40f ? speedMs : 420f;
                _pending = pending;
                _pinX = spawn.x;
                _pinZ = spawn.z;
                _rb = GetComponent<Rigidbody>();
                _missile = GetComponent<Missile>();
                ApplyDown();
            }

            private void FixedUpdate()
            {
                ApplyDown();
            }

            private void ApplyDown()
            {
                if (_handed)
                    return;
                if (_rb == null)
                    _rb = GetComponent<Rigidbody>();
                if (_missile == null)
                    _missile = GetComponent<Missile>();
                if (_rb == null)
                    return;
                float y = 0f;
                try { y = _rb.position.y; }
                catch { return; }
                if (y >= HighAltMissileFreeze.FreezeAboveM)
                {
                    Quaternion down = Quaternion.LookRotation(Vector3.down, Vector3.forward);
                    Vector3 p = _rb.position;
                    p.x = _pinX;
                    p.z = _pinZ;
                    try
                    {
                        _rb.position = p;
                        _rb.rotation = down;
                        transform.rotation = down;
                        _rb.angularVelocity = Vector3.zero;
                        _rb.velocity = Vector3.down * _speed;
                    }
                    catch { }
                    return;
                }
                _handed = true;
                F9DropMark.MarkReleased(_missile);
                try
                {
                    float term = StrategicArsenalMathService.TerminalSpeedMs(_speed);
                    Vector3 v = _rb.velocity;
                    if (v.sqrMagnitude < 1f)
                        v = Vector3.down * term;
                    _rb.velocity = StrategicArsenalMathService.CapSpeed(v, term);
                    _rb.angularVelocity = Vector3.zero;
                }
                catch { }
                if (_pending != null && _missile != null)
                {
                    try
                    {
                        if (!_pending.disabled)
                            _missile.SetTarget(_pending);
                    }
                    catch { }
                }
                Destroy(this);
            }
        }

        /// <summary>Keep F9 missiles at a turnable speed once they are in atmosphere.</summary>
        private sealed class TerminalSpeedCap : MonoBehaviour
        {
            private float _cap;
            private Rigidbody _rb;
            private float _until;
            private bool _keep;

            public void Configure(float capMs)
            {
                _cap = capMs > 40f ? capMs : StrategicArsenalMathService.TerminalTurnSpeedMs;
                _rb = GetComponent<Rigidbody>();
                _until = Time.time + 45f;
                Missile m = GetComponent<Missile>();
                _keep = F9DropMark.HasTbm(m);
            }

            private void FixedUpdate()
            {
                if (!_keep && Time.time > _until)
                {
                    Destroy(this);
                    return;
                }
                if (_rb == null)
                    _rb = GetComponent<Rigidbody>();
                if (_rb == null)
                    return;
                float y = 0f;
                try { y = _rb.position.y; }
                catch { return; }
                if (y >= HighAltMissileFreeze.FreezeAboveM)
                    return;
                try
                {
                    _rb.velocity = StrategicArsenalMathService.CapSpeed(_rb.velocity, _cap);
                }
                catch { }
            }
        }

        private static void ShowHud(string msg)
        {
            _hudMsg = msg != null ? msg : string.Empty;
            _hudUntil = Time.unscaledTime + 5f;
        }
    }
}
