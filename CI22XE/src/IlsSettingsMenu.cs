using BepInEx.Configuration;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// F4 ILS settings: glideslope angle 3–8° (default 5°). Drives AP + HUD needles.
    /// </summary>
    internal static class IlsSettingsMenu
    {
        private static ConfigEntry<KeyCode> _menuKey;
        private static ConfigEntry<float> _glideSlopeDeg;
        private static bool _menuOpen;

        private static GUIStyle _titleStyle;
        private static GUIStyle _labelStyle;
        private static GUIStyle _btnStyle;
        private static GUIStyle _chipStyle;
        private static bool _cursorHeld;

        internal static bool MenuOpen
        {
            get { return _menuOpen; }
        }

        internal static float GlideSlopeDeg
        {
            get
            {
                float d = _glideSlopeDeg != null
                    ? _glideSlopeDeg.Value
                    : IlsApproachMathService.DefaultGlideSlopeDeg;
                return IlsApproachMathService.ClampDeg(d);
            }
        }

        internal static void Bind(ConfigFile config)
        {
            _menuKey = config.Bind("IlsSettings", "MenuKey", KeyCode.F4,
                "F4 ILS settings menu (glideslope angle).");
            _glideSlopeDeg = config.Bind("IlsSettings", "GlideSlopeDeg",
                IlsApproachMathService.DefaultGlideSlopeDeg,
                "ILS glideslope angle in degrees (3–8). Used by autopilot LAND and ILS HUD.");
            SyncMath();
        }

        internal static void CloseMenuFromOutside()
        {
            CloseMenu();
        }

        internal static void Tick()
        {
            SyncMath();
            KeyCode menu = _menuKey != null ? _menuKey.Value : KeyCode.F4;
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

        internal static void DrawGui()
        {
            EnsureStyles();
            if (Plugin.AllowThirdPersonUi)
                DrawCornerHint();
            if (!_menuOpen)
                return;
            HoldCursor();
            DrawMenu();
        }

        private static void SyncMath()
        {
            IlsApproachMathService.GlideSlopeDeg = GlideSlopeDeg;
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
            if (BeginnerAssist.MenuOpen)
                BeginnerAssist.CloseMenuFromOutside();
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
            SyncMath();
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
            Aircraft ac = null;
            try { GameManager.GetLocalAircraft(out ac); }
            catch { }
            if (ac == null)
                return;

            Rect chip = PlayerAutopilot.CornerChipRect(AssistMenuLayoutService.SlotF4);
            Color prev = GUI.color;
            GUI.color = new Color(0.05f, 0.07f, 0.1f, 0.78f);
            GUI.DrawTexture(chip, Texture2D.whiteTexture);
            GUI.color = _menuOpen
                ? new Color(0.95f, 0.8f, 0.35f, 0.95f)
                : new Color(0.45f, 0.9f, 0.95f, 0.9f);
            GUI.DrawTexture(new Rect(chip.x, chip.y, chip.width, 2f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            string line = _menuOpen
                ? UiLang.T("F4 ILS  |  OPEN  " + GlideSlopeDeg.ToString("0.0") + "°",
                    "F4 ILS  |  已打开  " + GlideSlopeDeg.ToString("0.0") + "°")
                : UiLang.T("F4 ILS  |  " + GlideSlopeDeg.ToString("0.0") + "° GS",
                    "F4 ILS  |  " + GlideSlopeDeg.ToString("0.0") + "° 下滑道");
            _chipStyle.normal.textColor = new Color(0.8f, 0.95f, 1f, 0.95f);
            GUI.Label(new Rect(chip.x + 6f, chip.y, chip.width - 12f, chip.height), line, _chipStyle);
            GUI.color = prev;
        }

        private static void DrawMenu()
        {
            Rect box = AssistMenuLayoutService.IlsMenuRect(UiScaleService.Width, UiScaleService.Height);
            Color prev = GUI.color;
            GUI.color = new Color(0.06f, 0.08f, 0.1f, 0.92f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = new Color(0.45f, 0.9f, 0.95f, 0.95f);
            GUI.DrawTexture(new Rect(box.x, box.y, box.width, 3f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(box.x + 16f, box.y + 12f, box.width - 32f, 26f),
                UiLang.T("ILS SETTINGS  (F4)", "ILS 设置（F4）"), _titleStyle);
            GUI.Label(new Rect(box.x + 16f, box.y + 40f, box.width - 32f, 36f),
                UiLang.T("Glideslope for autopilot LAND + ILS needles (3–8°).",
                    "自动驾驶着陆与 ILS 指针使用的下滑角（3–8°）。"), _labelStyle);

            float y = box.y + 90f;
            float bw = box.width - 32f;
            float deg = GlideSlopeDeg;
            GUI.Label(new Rect(box.x + 16f, y, bw, 22f),
                UiLang.T("Glideslope  " + deg.ToString("0.0") + "°",
                    "下滑角  " + deg.ToString("0.0") + "°"), _labelStyle);
            y += 28f;

            float slider = GUI.HorizontalSlider(
                new Rect(box.x + 16f, y, bw, 22f),
                deg,
                IlsApproachMathService.MinGlideSlopeDeg,
                IlsApproachMathService.MaxGlideSlopeDeg);
            // Snap to 0.5° steps for clean settings.
            float snapped = Mathf.Round(slider * 2f) * 0.5f;
            snapped = IlsApproachMathService.ClampDeg(snapped);
            if (_glideSlopeDeg != null && Mathf.Abs(snapped - _glideSlopeDeg.Value) > 0.01f)
            {
                _glideSlopeDeg.Value = snapped;
                SyncMath();
            }
            y += 36f;

            GUI.Label(new Rect(box.x + 16f, y, bw, 18f), "3°", _labelStyle);
            GUI.Label(new Rect(box.x + box.width - 40f, y, 28f, 18f), "8°", _labelStyle);
            y += 28f;

            // Preset buttons
            float btnW = (bw - 24f) / 4f;
            float[] presets = { 3f, 4f, 5f, 6f };
            for (int i = 0; i < presets.Length; i++)
            {
                float p = presets[i];
                string label = p.ToString("0") + "°";
                if (GUI.Button(new Rect(box.x + 16f + i * (btnW + 8f), y, btnW, 32f), label, _btnStyle))
                {
                    if (_glideSlopeDeg != null)
                        _glideSlopeDeg.Value = p;
                    SyncMath();
                }
            }
            y += 44f;
            if (GUI.Button(new Rect(box.x + 16f, y, (bw - 8f) * 0.5f, 32f), "7°", _btnStyle))
            {
                if (_glideSlopeDeg != null)
                    _glideSlopeDeg.Value = 7f;
                SyncMath();
            }
            if (GUI.Button(new Rect(box.x + 16f + (bw - 8f) * 0.5f + 8f, y, (bw - 8f) * 0.5f, 32f), "8°", _btnStyle))
            {
                if (_glideSlopeDeg != null)
                    _glideSlopeDeg.Value = 8f;
                SyncMath();
            }
            y += 48f;

            if (GUI.Button(new Rect(box.x + 16f, y, bw, 34f),
                UiLang.T("CLOSE", "关闭"), _btnStyle))
                CloseMenu();

            GUI.color = prev;
        }

        private static void EnsureStyles()
        {
            if (_titleStyle != null)
                return;
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            _titleStyle.normal.textColor = new Color(0.85f, 0.98f, 1f, 1f);
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true,
                alignment = TextAnchor.MiddleLeft
            };
            _labelStyle.normal.textColor = new Color(0.75f, 0.88f, 0.92f, 0.95f);
            _btnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold
            };
            _chipStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight
            };
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
