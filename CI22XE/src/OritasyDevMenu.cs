using System;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Hidden main-menu developer panel.
    /// Sequence: Up Up Down Down Left Left Right Right A A B B
    /// </summary>
    internal static class OritasyDevMenu
    {
        private static readonly int[] Code = new int[] { 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5 };
        private const float IdleReset = 2.5f;

        private static bool _open;
        private static int _progress;
        private static float _lastStepAt = -10f;
        private static Vector2 _scroll;
        private static string _note = "";
        private static MainMenu _cachedMainMenu;
        private static float _nextMenuProbe;
        private static bool _cachedIsMainMenu;
        private static bool _cursorHeld;
        private static GUIStyle _title;
        private static GUIStyle _body;
        private static GUIStyle _btn;
        private static GUIStyle _warn;

        internal static bool IsOpen
        {
            get { return _open; }
        }

        internal static void Tick()
        {
            if (!IsMainMenu())
            {
                _progress = 0;
                if (_open)
                    Close();
                return;
            }

            if (_open && Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
                return;
            }

            if (_open)
                return;

            if (_progress > 0 && Time.unscaledTime - _lastStepAt > IdleReset)
                _progress = 0;

            int step = ReadStep();
            if (step < 0)
                return;

            if (step == Code[_progress])
            {
                _progress++;
                _lastStepAt = Time.unscaledTime;
                if (_progress >= Code.Length)
                {
                    _progress = 0;
                    Open();
                }
            }
            else
            {
                _progress = (step == Code[0]) ? 1 : 0;
                _lastStepAt = Time.unscaledTime;
            }
        }

        internal static void Draw()
        {
            if (!_open)
                return;
            if (!IsMainMenu())
            {
                Close();
                return;
            }

            HoldCursor();
            EnsureStyles();
            bool zh = UiLang.IsChinese;

            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.62f);
            GUI.DrawTexture(new Rect(0f, 0f, UiScaleService.Width, UiScaleService.Height),
                Texture2D.whiteTexture);
            GUI.color = prev;

            float w = Mathf.Min(620f, UiScaleService.Width - 40f);
            float h = Mathf.Min(560f, UiScaleService.Height - 40f);
            Rect box = new Rect((UiScaleService.Width - w) * 0.5f, (UiScaleService.Height - h) * 0.5f, w, h);

            GUI.color = new Color(0.05f, 0.07f, 0.08f, 0.96f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = new Color(0.95f, 0.75f, 0.2f, 1f);
            GUI.DrawTexture(new Rect(box.x, box.y, box.width, 3f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(box.x + 16f, box.y + 12f, box.width - 32f, box.height - 24f));
            GUILayout.Label(zh ? "Oritasy 开发者入口" : "Oritasy Developer", _title);
            GUILayout.Label(zh ? "仅主菜单 · Esc 关闭" : "Main menu only · Esc closes", _body);

            _scroll = GUILayout.BeginScrollView(_scroll);

#if ORITASY_COMBINED
            DrawCareer(zh);
            GUILayout.Space(10f);
            DrawPremium(zh);
            GUILayout.Space(10f);
#endif
            DrawToggles(zh);

            if (!string.IsNullOrEmpty(_note))
            {
                GUILayout.Space(8f);
                GUILayout.Label(_note, _warn);
            }

            GUILayout.EndScrollView();
            GUILayout.Space(8f);
            if (GUILayout.Button(zh ? "关闭" : "Close", _btn, GUILayout.Height(32f)))
                Close();
            GUILayout.EndArea();
        }

#if ORITASY_COMBINED
        private static void DrawCareer(bool zh)
        {
            GUILayout.Label(zh ? "生涯" : "Career", _title);
            int xp = 0;
            int lvl = 1;
            int pres = 0;
            try
            {
                xp = WeXon.PlayerCareer.DebugXp();
                lvl = WeXon.PlayerCareer.GetLevel();
                pres = WeXon.PlayerCareer.DebugPrestige();
            }
            catch { }
            GUILayout.Label(zh
                ? ("等级 " + lvl + "  ·  经验 " + xp + "  ·  威望 " + pres)
                : ("Lv " + lvl + "  ·  XP " + xp + "  ·  Prestige " + pres), _body);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button(zh ? "经验 +100" : "XP +100", _btn, GUILayout.Height(26f)))
                AddXp(100, zh);
            if (GUILayout.Button(zh ? "经验 +1000" : "XP +1000", _btn, GUILayout.Height(26f)))
                AddXp(1000, zh);
            if (GUILayout.Button(zh ? "经验 +10000" : "XP +10000", _btn, GUILayout.Height(26f)))
                AddXp(10000, zh);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(zh ? "威望 +1" : "Prestige +1", _btn, GUILayout.Height(26f)))
                AddPrestige(1, zh);
            if (GUILayout.Button(zh ? "威望 -1" : "Prestige -1", _btn, GUILayout.Height(26f)))
                AddPrestige(-1, zh);
            GUILayout.EndHorizontal();
        }

        private static void DrawPremium(bool zh)
        {
            GUILayout.Label(zh ? "高级账号" : "Premium", _title);
            string st = "";
            try { st = WeXon.CareerPremiumService.StatusLine(zh); }
            catch { }
            GUILayout.Label(st, _body);
            GUILayout.Label(zh
                ? "开发者设置天数（不消耗 CDK，从现在起算）"
                : "Set days from now (does not consume a CDK)", _body);
            GUILayout.BeginHorizontal();
            DevDayBtn(1, zh);
            DevDayBtn(3, zh);
            DevDayBtn(5, zh);
            DevDayBtn(7, zh);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DevDayBtn(14, zh);
            DevDayBtn(30, zh);
            if (GUILayout.Button(zh ? "清除高级" : "Clear premium", _btn, GUILayout.Height(26f)))
            {
                try { WeXon.CareerPremiumService.DevSetDays(0); }
                catch { }
                _note = zh ? "已清除高级账号" : "Premium cleared";
            }
            GUILayout.EndHorizontal();
        }

        private static void DevDayBtn(int days, bool zh)
        {
            if (GUILayout.Button(days + (zh ? " 天" : "d"), _btn, GUILayout.Height(26f)))
            {
                try { WeXon.CareerPremiumService.DevSetDays(days); }
                catch { }
                _note = zh ? ("高级账号设为 " + days + " 天") : ("Premium set to " + days + "d");
            }
        }

        private static void AddXp(int n, bool zh)
        {
            try { WeXon.PlayerCareer.DebugAddXp(n); }
            catch { }
            _note = zh ? ("已加经验 " + n) : ("Added XP " + n);
        }

        private static void AddPrestige(int n, bool zh)
        {
            try { WeXon.PlayerCareer.DebugAddPrestige(n); }
            catch { }
            _note = zh ? ("威望变化 " + n) : ("Prestige " + n);
        }
#endif

        private static void DrawToggles(bool zh)
        {
            GUILayout.Label(zh ? "开关" : "Toggles", _title);
            ToggleRow(zh ? "无限制挂载" : "Unrestricted mounts", Plugin.UnrestrictedWeapons);
            ToggleRow(zh ? "调试日志" : "Debug log", Plugin.DebugLog);
            ToggleRow(zh ? "第三人称 UI" : "Third-person UI", Plugin.ShowThirdPersonUi);
            ToggleRow(zh ? "启动闪屏" : "Boot splash", Plugin.BootSplash);
            ToggleRow(zh ? "HUD 品牌" : "HUD brand", Plugin.ShowHudBrand);
        }

        private static void ToggleRow(string label, BepInEx.Configuration.ConfigEntry<bool> entry)
        {
            if (entry == null)
                return;
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, _body, GUILayout.Width(200f));
            bool on = entry.Value;
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = on ? new Color(0.35f, 0.8f, 0.45f) : Color.white;
            if (GUILayout.Button(on ? "ON" : "OFF", _btn, GUILayout.Width(80f), GUILayout.Height(24f)))
                entry.Value = !on;
            GUI.backgroundColor = prev;
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
        }

        private static int ReadStep()
        {
            if (Input.GetKeyDown(KeyCode.UpArrow))
                return 0;
            if (Input.GetKeyDown(KeyCode.DownArrow))
                return 1;
            if (Input.GetKeyDown(KeyCode.LeftArrow))
                return 2;
            if (Input.GetKeyDown(KeyCode.RightArrow))
                return 3;
            if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.JoystickButton0)
                || Input.GetKeyDown(KeyCode.Joystick1Button0))
                return 4;
            if (Input.GetKeyDown(KeyCode.B) || Input.GetKeyDown(KeyCode.JoystickButton1)
                || Input.GetKeyDown(KeyCode.Joystick1Button1))
                return 5;
            return -1;
        }

        private static void Open()
        {
            _open = true;
            _note = "";
            _scroll = Vector2.zero;
            HoldCursor();
        }

        private static void Close()
        {
            _open = false;
            ReleaseCursor();
        }

        private static void HoldCursor()
        {
            if (_cursorHeld)
            {
                OritasyCursor.Pulse();
                return;
            }
            OritasyCursor.Hold();
            _cursorHeld = true;
        }

        private static void ReleaseCursor()
        {
            if (!_cursorHeld)
                return;
            OritasyCursor.Release();
            _cursorHeld = false;
        }

        private static bool IsMainMenu()
        {
            if (JoinMenuFactionFix.SelectionUiOpen())
                return false;
            if (Time.unscaledTime < _nextMenuProbe)
                return _cachedIsMainMenu;
            _nextMenuProbe = Time.unscaledTime + (_cachedIsMainMenu ? 0.4f : 1.5f);
            try
            {
                if (_cachedMainMenu == null || !_cachedMainMenu)
                    _cachedMainMenu = UnityEngine.Object.FindObjectOfType<MainMenu>();
                _cachedIsMainMenu = _cachedMainMenu != null && _cachedMainMenu.isActiveAndEnabled;
            }
            catch
            {
                _cachedMainMenu = null;
                _cachedIsMainMenu = false;
            }
            return _cachedIsMainMenu;
        }

        private static void EnsureStyles()
        {
            if (_title != null)
                return;
            _title = new GUIStyle(GUI.skin.label);
            _title.fontSize = 16;
            _title.fontStyle = FontStyle.Bold;
            _title.normal.textColor = new Color(1f, 0.88f, 0.4f);
            _body = new GUIStyle(GUI.skin.label);
            _body.fontSize = 13;
            _body.wordWrap = true;
            _body.normal.textColor = new Color(0.85f, 0.9f, 0.88f);
            _btn = new GUIStyle(GUI.skin.button);
            _btn.fontSize = 13;
            _warn = new GUIStyle(GUI.skin.label);
            _warn.fontSize = 13;
            _warn.normal.textColor = new Color(0.95f, 0.85f, 0.35f);
        }
    }
}
