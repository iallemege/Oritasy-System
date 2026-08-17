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
    /// <summary>Boot splash, first-run welcome, XE HUD brand watermark.</summary>
    internal static class OritasyPresentation
    {
        private const string WelcomePrefKey = "Oritasy.FirstRunWelcome.v1";

        private static bool _splashArmed;
        private static bool _splashStarted;
        private static float _splashDuration = 2f;
        private static float _splashUntil = -1f;
        private static bool _welcomePending;
        private static bool _welcomeOpen;
        private static bool _welcomeChecked;
        /// <summary>Top-left Help chip — shown after the welcome dialog is dismissed once.</summary>
        private static bool _helpChipEnabled;
        private static bool _changelogOpen;
        private static Vector2 _welcomeScroll;
        private static Vector2 _changelogScroll;
        private static MainMenu _cachedMainMenu;
        private static float _nextMainMenuProbe;
        private static bool _cachedIsMainMenu;
        private static bool _cursorHeld;
        private static GUIStyle _splashStyle;
        private static GUIStyle _splashCreditStyle;
        private static GUIStyle _splashLoadingStyle;
        private static GUIStyle _hudStyle;
        private static GUIStyle _welcomeTitleStyle;
        private static GUIStyle _welcomeBodyStyle;
        private static GUIStyle _welcomeBtnStyle;
        private static GUIStyle _helpChipStyle;
        private static GUIStyle _helpChipBtnStyle;
        private static readonly Color BrandGreen = new Color(0.15f, 1f, 0.35f, 1f);
        private static readonly Color CreditGreen = new Color(0.2f, 0.85f, 0.4f, 0.9f);

        internal static bool SplashActive
        {
            get { return _splashStarted && _splashUntil > 0f && Time.realtimeSinceStartup < _splashUntil; }
        }

        internal static bool WelcomeActive
        {
            get { return _welcomeOpen; }
        }

        /// <summary>Splash / welcome / changelog / mission tip — suppress other mod HUD.</summary>
        internal static bool BlocksHud
        {
            get
            {
                return SplashActive || _welcomeOpen || _changelogOpen
                    || BeginnerAssist.MissionTipActive;
            }
        }

        /// <summary>True while boot splash / welcome / changelog overlays own the screen.</summary>
        internal static bool OverlayActive
        {
            get { return SplashActive || _welcomeOpen || _changelogOpen; }
        }

        /// <summary>Queue splash; countdown begins on the first real OnGUI/Repaint frame.</summary>
        internal static void ArmSplash(float seconds)
        {
            _splashArmed = true;
            _splashStarted = false;
            _splashDuration = Mathf.Max(0.1f, seconds);
            _splashUntil = -1f;
        }

        internal static void Draw()
        {
            // Start timer only when Unity is actually painting UI (plugin Awake is too early).
            if (_splashArmed && !_splashStarted
                && Event.current != null
                && Event.current.type == EventType.Repaint
                && Screen.width > 64 && Screen.height > 64)
            {
                _splashStarted = true;
                _splashUntil = Time.realtimeSinceStartup + _splashDuration;
                if (Plugin.Log != null)
                    Plugin.Log.LogInfo("Oritasy System splash (" + _splashDuration.ToString("0.0") + "s)");
            }

            if (SplashActive)
            {
                DrawSplash();
                return;
            }

            // After splash (or if splash disabled): show English intro once per install.
            if (!_welcomeChecked)
            {
                _welcomeChecked = true;
                _helpChipEnabled = true;
                try
                {
                    if (PlayerPrefs.GetInt(WelcomePrefKey, 0) == 0)
                        _welcomePending = true;
                }
                catch
                {
                    _welcomePending = true;
                }
            }
            if (_welcomePending && !_welcomeOpen)
                OpenWelcome(false);

            if (_welcomeOpen)
                DrawWelcome();
            else if (_changelogOpen)
            {
                DrawChangelogPanel();
                if (IsMainMenuScene())
                    DrawVersionChip();
            }
            else
            {
                if (Plugin.AllowThirdPersonUi)
                    DrawHudBrand();
                if (IsMainMenuScene())
                    DrawVersionChip();
            }

            // Help is the launch guide — always on after splash, not overlay chrome.
            DrawHelpChip();
        }

        private static void OpenWelcome(bool fromChip)
        {
            _welcomePending = false;
            _welcomeOpen = true;
            _welcomeScroll = Vector2.zero;
            CaptureCursor();
            if (!fromChip && Plugin.Log != null)
                Plugin.Log.LogInfo("Oritasy first-run welcome shown");
        }

        private static void CloseWelcome()
        {
            _welcomeOpen = false;
            _helpChipEnabled = true;
            try
            {
                PlayerPrefs.SetInt(WelcomePrefKey, 1);
                PlayerPrefs.Save();
            }
            catch { }
            ReleaseCursor();
        }

        private static void ToggleWelcomeFromChip()
        {
            if (_welcomeOpen)
                CloseWelcome();
            else
                OpenWelcome(true);
        }

        private static void ToggleChangelogFromChip()
        {
            if (_changelogOpen)
                CloseChangelog();
            else
                OpenChangelog();
        }

        private static void OpenChangelog()
        {
            _changelogOpen = true;
            _changelogScroll = Vector2.zero;
            if (_welcomeOpen)
                CloseWelcome();
            CaptureCursor();
        }

        private static void CloseChangelog()
        {
            _changelogOpen = false;
            ReleaseCursor();
        }

        private static bool IsMainMenuScene()
        {
            if (Time.unscaledTime < _nextMainMenuProbe)
                return _cachedIsMainMenu;
            _nextMainMenuProbe = Time.unscaledTime + (_cachedIsMainMenu ? 0.5f : 2f);
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
            if (_splashStyle == null)
            {
                _splashStyle = new GUIStyle(GUI.skin.label);
                _splashStyle.alignment = TextAnchor.MiddleCenter;
                _splashStyle.fontSize = PresentationLayoutService.SplashTitleFont((int)UiScaleService.Height);
                _splashStyle.fontStyle = FontStyle.Bold;
                _splashStyle.normal.textColor = BrandGreen;
                _splashStyle.hover.textColor = BrandGreen;
                _splashStyle.active.textColor = BrandGreen;
            }
            else
            {
                _splashStyle.fontSize = PresentationLayoutService.SplashTitleFont((int)UiScaleService.Height);
            }

            if (_splashCreditStyle == null)
            {
                _splashCreditStyle = new GUIStyle(GUI.skin.label);
                _splashCreditStyle.alignment = TextAnchor.MiddleCenter;
                _splashCreditStyle.fontSize = PresentationLayoutService.SplashCreditFont((int)UiScaleService.Height);
                _splashCreditStyle.fontStyle = FontStyle.Normal;
                _splashCreditStyle.normal.textColor = CreditGreen;
                _splashCreditStyle.hover.textColor = CreditGreen;
                _splashCreditStyle.active.textColor = CreditGreen;
                _splashCreditStyle.clipping = TextClipping.Overflow;
                _splashCreditStyle.wordWrap = false;
            }
            else
            {
                _splashCreditStyle.fontSize = PresentationLayoutService.SplashCreditFont((int)UiScaleService.Height);
                _splashCreditStyle.clipping = TextClipping.Overflow;
            }

            if (_splashLoadingStyle == null)
            {
                _splashLoadingStyle = new GUIStyle(GUI.skin.label);
                _splashLoadingStyle.alignment = TextAnchor.LowerLeft;
                _splashLoadingStyle.fontSize = PresentationLayoutService.SplashLoadingFont((int)UiScaleService.Height);
                _splashLoadingStyle.fontStyle = FontStyle.Normal;
                _splashLoadingStyle.normal.textColor = CreditGreen;
                _splashLoadingStyle.hover.textColor = CreditGreen;
                _splashLoadingStyle.active.textColor = CreditGreen;
                _splashLoadingStyle.clipping = TextClipping.Overflow;
            }
            else
            {
                _splashLoadingStyle.fontSize = PresentationLayoutService.SplashLoadingFont((int)UiScaleService.Height);
                _splashLoadingStyle.clipping = TextClipping.Overflow;
            }

            if (_hudStyle == null)
            {
                _hudStyle = new GUIStyle(GUI.skin.label);
                _hudStyle.alignment = TextAnchor.UpperCenter;
                _hudStyle.fontSize = 16;
                _hudStyle.fontStyle = FontStyle.Bold;
                _hudStyle.normal.textColor = BrandGreen;
                _hudStyle.hover.textColor = BrandGreen;
                _hudStyle.active.textColor = BrandGreen;
            }

            if (_welcomeTitleStyle == null)
            {
                _welcomeTitleStyle = new GUIStyle(GUI.skin.label);
                _welcomeTitleStyle.alignment = TextAnchor.MiddleLeft;
                _welcomeTitleStyle.fontSize = 18;
                _welcomeTitleStyle.fontStyle = FontStyle.Bold;
                _welcomeTitleStyle.normal.textColor = BrandGreen;
                _welcomeTitleStyle.clipping = TextClipping.Overflow;
            }

            if (_welcomeBodyStyle == null)
            {
                _welcomeBodyStyle = new GUIStyle(GUI.skin.label);
                _welcomeBodyStyle.alignment = TextAnchor.UpperLeft;
                _welcomeBodyStyle.fontSize = 14;
                _welcomeBodyStyle.wordWrap = true;
                _welcomeBodyStyle.richText = true;
                _welcomeBodyStyle.normal.textColor = new Color(0.88f, 0.95f, 0.9f, 1f);
            }

            if (_welcomeBtnStyle == null)
            {
                _welcomeBtnStyle = new GUIStyle(GUI.skin.button);
                _welcomeBtnStyle.fontSize = 15;
                _welcomeBtnStyle.fontStyle = FontStyle.Bold;
                _welcomeBtnStyle.alignment = TextAnchor.MiddleCenter;
                _welcomeBtnStyle.normal.textColor = Color.white;
            }

            if (_helpChipStyle == null)
            {
                _helpChipStyle = new GUIStyle(GUI.skin.label);
                _helpChipStyle.fontSize = 10;
                _helpChipStyle.fontStyle = FontStyle.Bold;
                _helpChipStyle.alignment = TextAnchor.MiddleLeft;
                _helpChipStyle.clipping = TextClipping.Overflow;
                _helpChipStyle.normal.textColor = new Color(0.8f, 0.95f, 0.85f, 0.95f);
            }

            if (_helpChipBtnStyle == null)
            {
                // Invisible hit target — visuals match F1/F2 tip chips.
                _helpChipBtnStyle = new GUIStyle();
                _helpChipBtnStyle.normal.background = null;
                _helpChipBtnStyle.hover.background = null;
                _helpChipBtnStyle.active.background = null;
                _helpChipBtnStyle.focused.background = null;
                _helpChipBtnStyle.border = new RectOffset(0, 0, 0, 0);
                _helpChipBtnStyle.margin = new RectOffset(0, 0, 0, 0);
                _helpChipBtnStyle.padding = new RectOffset(0, 0, 0, 0);
            }
        }

        private static void DrawSplash()
        {
            EnsureStyles();
            int prevDepth = GUI.depth;
            GUI.depth = -1000;
            Color prev = GUI.color;
            GUI.color = Color.black;
            GUI.DrawTexture(new Rect(0f, 0f, UiScaleService.Width, UiScaleService.Height), Texture2D.whiteTexture);
            GUI.color = prev;

            // Explicit screen-center title + smaller credit under it
            const string title = "Oritasy System";
            const string credit = "By IAllemege";
            Vector2 titleSize = _splashStyle.CalcSize(new GUIContent(title));
            Vector2 creditSize = _splashCreditStyle.CalcSize(new GUIContent(credit));
            float creditH = PresentationLayoutService.CreditLineHeight(_splashCreditStyle, creditSize.y);
            float titleH = Mathf.Max(titleSize.y, _splashStyle.fontSize * 1.35f);
            float gap = PresentationLayoutService.BrandGap(UiScaleService.Height);
            float blockH = titleH + gap + creditH;
            float titleY = (UiScaleService.Height - blockH) * 0.5f;
            float titleX = (UiScaleService.Width - titleSize.x) * 0.5f;
            float creditX = (UiScaleService.Width - creditSize.x) * 0.5f;
            float creditY = titleY + titleH + gap;
            GUI.Label(new Rect(titleX, titleY, titleSize.x + 8f, titleH), title, _splashStyle);
            GUI.Label(new Rect(creditX, creditY, creditSize.x + 12f, creditH), credit, _splashCreditStyle);

            const string loading = "Loading Missile Config.......";
            const string disclaimer = "(Bug might be a lot)";
            float pad = PresentationLayoutService.BrandPad(UiScaleService.Height);
            Vector2 loadSize = _splashLoadingStyle.CalcSize(new GUIContent(loading));
            Vector2 discSize = _splashLoadingStyle.CalcSize(new GUIContent(disclaimer));
            float loadH = PresentationLayoutService.CreditLineHeight(_splashLoadingStyle, loadSize.y);
            float discH = PresentationLayoutService.CreditLineHeight(_splashLoadingStyle, discSize.y);
            float lineGap = PresentationLayoutService.BrandLineGap(UiScaleService.Height);
            float cornerH = loadH + lineGap + discH;
            float baseY = UiScaleService.Height - pad - cornerH;
            GUI.Label(new Rect(pad, baseY, loadSize.x + 16f, loadH),
                loading, _splashLoadingStyle);
            GUI.Label(new Rect(pad, baseY + loadH + lineGap, discSize.x + 16f, discH),
                disclaimer, _splashLoadingStyle);
            GUI.depth = prevDepth;
        }

        private static void DrawWelcome()
        {
            EnsureStyles();
            HoldCursor();

            int prevDepth = GUI.depth;
            GUI.depth = -999;
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(new Rect(0f, 0f, UiScaleService.Width, UiScaleService.Height), Texture2D.whiteTexture);
            GUI.color = prev;

            Rect box = PresentationLayoutService.WelcomeBox(UiScaleService.Width, UiScaleService.Height);

            GUI.color = new Color(0.04f, 0.07f, 0.06f, 0.96f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = BrandGreen;
            GUI.DrawTexture(new Rect(box.x, box.y, box.width, 3f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(box.x + 20f, box.y + 12f, box.width - 40f, 28f),
                "Welcome to Oritasy  " + PluginInfo.DisplayRelease, _welcomeTitleStyle);
            float guideCreditH = PresentationLayoutService.CreditLineHeight(_splashCreditStyle, 22f);
            GUI.Label(new Rect(box.x + 20f, box.y + 40f, box.width - 40f, guideCreditH),
                "Guide & changelog  ·  By IAllemege", _splashCreditStyle);

            Rect scrollR = new Rect(box.x + 16f, box.y + 40f + guideCreditH + 10f,
                box.width - 32f, box.height - (40f + guideCreditH + 10f + 58f));
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(scrollR, Texture2D.whiteTexture);
            GUI.color = Color.white;

            _welcomeScroll = GUI.BeginScrollView(scrollR, _welcomeScroll,
                new Rect(0f, 0f, scrollR.width - 22f, 1600f));
            GUI.Label(new Rect(12f, 8f, scrollR.width - 36f, 1580f), WelcomeBodyText(), _welcomeBodyStyle);
            GUI.EndScrollView();

            float bw = PresentationLayoutService.DialogButtonWidth(box.width);
            if (GUI.Button(new Rect(box.x + (box.width - bw) * 0.5f, box.yMax - 48f, bw, 34f),
                "GOT IT", _welcomeBtnStyle))
                CloseWelcome();

            GUI.depth = prevDepth;
        }

        private static string YellowLines()
        {
            // Unity IMGUI rich-text yellow callouts
            return
                "<color=#FFD84A>• Aircraft, Vehicle, Buildings in this mod were modified and fully customizable.</color>\n"
                + "<color=#FFD84A>• Because of GUID difference from original version of the game, the skin from workshop may not work.</color>\n";
        }

        private static string WelcomeBodyText()
        {
            return
                "CHANGE LOG\n"
                + "• Latest release: Oritasy " + PluginInfo.DisplayRelease + "\n"
                + YellowLines()
                + (PluginInfo.EnglishOnlyEdition
                    ? "• English-only Special Edition (no Chinese language toggle).\n"
                    : "• Standard edition (EN/ZH kill-tip language on Profile).\n")
                + "• Dynamic music BETA: optional — enable in Oritasy Profile → Experimental.\n"
                + "• Branding: aircraft XE / [Oritasy], ships NE / [Thanos], vehicles TE / [Unitas], buildings [Bexur].\n"
                + "• First mission tip: press F3 for Beginner Assist (takeoff / land / crash guardian).\n"
                + "• Help chip (top-left) reopens this guide after the first dismiss.\n\n"
                + "Oritasy merges aircraft / unit modifications, the WeXon weapon suite, and TGM-85 in one DLL.\n\n"
                + "WHAT IT CHANGES\n"
                + YellowLines()
                + "• Extra weapons (including ACM-119 / ACNM-118 / AAM-2CV), multi-mode seekers, and strategic support options.\n"
                + "• Optional unrestricted weapon mounts (toggle in Career Profile).\n\n"
                + "KEYBINDS (in flight)\n"
                + "• F1 — Oritasy System: per-aircraft G-limits, speed, and FBW tuning.\n"
                + "• F2 — Autopilot menu (straight / orbit / land + optional Missile evade TEST).\n"
                + "• F3 — Beginner Assist: auto takeoff, crash guardian, terrain avoidance (land via F2).\n"
                + "• F4 — ILS Settings (glideslope 3–8°).\n"
                + "• F5 — Private message + fund transfer (pick a player; Message / Transfer tabs).\n"
                + "• F6 — Kill boost: mystery picks (hidden until chosen; reshuffled each open).\n"
                + "• F7 — Host-only fund grant (any player, no deduct / no cap).\n"
                + "• F8 — Engine Component Monitor (in-flight schematic). Main-menu F8 is Career Profile.\n"
                + "• Insert — Manual missile (MITL); was F6.\n"
                + "• \\ — Missile-pilot HUD toggle (was F7/Home; Home kept free for Eject).\n"
                + "• F9 — Support (buy/spawn salvos; match-only unlocks from kill streaks).\n"
                + "• F10 — Aerial resupply + Repair Parts tab.  Backspace cancels resupply.\n"
                + "• F11 — Oritasy RWR layout.\n"
                + "• Throttle below 0% (Ctrl / axis down) — first press stops at 0% and opens the airbrake; release and press again for reverse 0–100%. While in reverse, another Ctrl press toggles the airbrake. Increasing through idle has no detent.\n"
                + "• Delete — Missile camera picture-in-picture.\n\n"
                + "BEGINNER SAFETY\n"
                + "Crash guardian: low inverted dives, post-stall, spins, and high-alt falling-leaf (thin-air thrust recovery ~14km). STOVL jets also vector nozzles during recovery.\n\n"
                + "CAREER\n"
                + "Open Career Profile from the main menu to track XP, kills, badges, and arsenal unlock progress.\n\n"
                + "Have fun — and expect a few bugs.";
        }

        private static void DrawHudBrand()
        {
            if (Plugin.ShowHudBrand != null && !Plugin.ShowHudBrand.Value)
                return;
            // Paint-only chip — skip Layout/input events.
            if (Event.current != null && Event.current.type != EventType.Repaint)
                return;
            // Local player in an XE airframe
            if (!Plugin.IsUsableXeAircraft(Plugin.ResolveGuiAircraft()))
                return;

            EnsureStyles();
            float w = 320f;
            float h = 26f;
            // Original top-center placement (pre-3000 offset)
            float y = Plugin.HudBrandOffsetY != null ? Plugin.HudBrandOffsetY.Value : 8f;
            Rect r = new Rect((UiScaleService.Width - w) * 0.5f, y, w, h);
            GUI.Label(r, "Oritasy System", _hudStyle);
        }

        /// <summary>
        /// Top-left tip chip (same look as F1/F2 stack): toggles the English guide dialog.
        /// Mouse-down only — GUI.Button would steal gamepad look focus.
        /// </summary>
        private static void DrawHelpChip()
        {
            if (!_helpChipEnabled)
                return;
            EnsureStyles();
            float chipW = 168f;
            float chipH = PlayerAutopilot.CornerChipH;
            float y = Plugin.HudBrandOffsetY != null ? Plugin.HudBrandOffsetY.Value : 8f;
            Rect chip = new Rect(18f, y, chipW, chipH);

            int prevDepth = GUI.depth;
            GUI.depth = -997;

            // Mouse-only hit — GUI.Button steals gamepad focus / look.
            Event ev = Event.current;
            if (ev != null && ev.type == EventType.MouseDown && ev.button == 0
                && chip.Contains(ev.mousePosition))
            {
                ToggleWelcomeFromChip();
                ev.Use();
            }

            if (Event.current != null && Event.current.type != EventType.Repaint)
            {
                GUI.depth = prevDepth;
                return;
            }

            Color prev = GUI.color;
            GUI.color = new Color(0.05f, 0.07f, 0.1f, 0.78f);
            GUI.DrawTexture(chip, Texture2D.whiteTexture);
            GUI.color = _welcomeOpen
                ? new Color(0.95f, 0.85f, 0.35f, 0.95f)
                : new Color(0.45f, 0.9f, 0.6f, 0.95f);
            GUI.DrawTexture(new Rect(chip.x, chip.y, chip.width, 2f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            string line = _welcomeOpen ? "Help | ON" : "Help | Guide";
            _helpChipStyle.fontSize = 10;
            _helpChipStyle.normal.textColor = _welcomeOpen
                ? new Color(1f, 0.92f, 0.55f, 0.98f)
                : new Color(0.8f, 0.95f, 0.85f, 0.95f);
            GUI.Label(new Rect(chip.x + 8f, chip.y, chip.width - 12f, chip.height), line, _helpChipStyle);
            GUI.color = prev;
            GUI.depth = prevDepth;
        }

        /// <summary>
        /// Main-menu bottom-left chip (Help style), just above the vanilla EXIT GAME button.
        /// Shows release version; click opens What's New.
        /// </summary>
        private static void DrawVersionChip()
        {
            EnsureStyles();
            float chipW = 188f;
            float chipH = 28f;
            // EXIT GAME: bottom-center, y=16 from bottom, height 50 → sit a bit above that band.
            float exitBand = 16f + 50f;
            float gap = 12f;
            float y = UiScaleService.Height - exitBand - gap - chipH;
            if (y < 8f)
                y = 8f;
            Rect chip = new Rect(18f, y, chipW, chipH);

            if (GUI.Button(chip, GUIContent.none, _helpChipBtnStyle))
                ToggleChangelogFromChip();

            if (Event.current != null && Event.current.type != EventType.Repaint)
                return;

            Color prev = GUI.color;
            GUI.color = new Color(0.05f, 0.07f, 0.1f, 0.78f);
            GUI.DrawTexture(chip, Texture2D.whiteTexture);
            GUI.color = _changelogOpen
                ? new Color(0.95f, 0.85f, 0.35f, 0.95f)
                : new Color(0.45f, 0.9f, 0.6f, 0.95f);
            GUI.DrawTexture(new Rect(chip.x, chip.y, chip.width, 2f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            string line = _changelogOpen
                ? ("v" + PluginInfo.ReleaseVersion + " | ON")
                : ("v" + PluginInfo.ReleaseVersion + " | Notes");
            _helpChipStyle.fontSize = 10;
            _helpChipStyle.normal.textColor = _changelogOpen
                ? new Color(1f, 0.92f, 0.55f, 0.98f)
                : new Color(0.8f, 0.95f, 0.85f, 0.95f);
            GUI.Label(new Rect(chip.x + 8f, chip.y, chip.width - 12f, chip.height), line, _helpChipStyle);
            GUI.color = prev;
        }

        private static void DrawChangelogPanel()
        {
            EnsureStyles();
            HoldCursor();

            int prevDepth = GUI.depth;
            GUI.depth = -998;
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(new Rect(0f, 0f, UiScaleService.Width, UiScaleService.Height), Texture2D.whiteTexture);
            GUI.color = prev;

            Rect box = PresentationLayoutService.ChangelogBox(UiScaleService.Width, UiScaleService.Height);

            GUI.color = new Color(0.04f, 0.07f, 0.06f, 0.96f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = BrandGreen;
            GUI.DrawTexture(new Rect(box.x, box.y, box.width, 3f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(box.x + 20f, box.y + 12f, box.width - 40f, 28f),
                "Oritasy " + PluginInfo.DisplayRelease + "  —  What's New", _welcomeTitleStyle);
            float notesCreditH = PresentationLayoutService.CreditLineHeight(_splashCreditStyle, 22f);
            GUI.Label(new Rect(box.x + 20f, box.y + 40f, box.width - 40f, notesCreditH),
                "Latest release notes  ·  By IAllemege", _splashCreditStyle);

            Rect scrollR = new Rect(box.x + 16f, box.y + 40f + notesCreditH + 10f,
                box.width - 32f, box.height - (40f + notesCreditH + 10f + 58f));
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(scrollR, Texture2D.whiteTexture);
            GUI.color = Color.white;

            _changelogScroll = GUI.BeginScrollView(scrollR, _changelogScroll,
                new Rect(0f, 0f, scrollR.width - 22f, 720f));
            GUI.Label(new Rect(12f, 8f, scrollR.width - 36f, 700f), ChangelogBodyText(), _welcomeBodyStyle);
            GUI.EndScrollView();

            float bw = PresentationLayoutService.DialogButtonWidth(box.width);
            if (GUI.Button(new Rect(box.x + (box.width - bw) * 0.5f, box.yMax - 48f, bw, 34f),
                "CLOSE", _welcomeBtnStyle))
                CloseChangelog();

            GUI.depth = prevDepth;
        }

        private static string ChangelogBodyText()
        {
            // English-only; keep the three most recent releases.
            return
                "Oritasy " + PluginInfo.DisplayRelease + "\n"
                + "Latest three releases\n\n"
                + YellowLines()
                + "0.0.9.193C\n"
                + "• Every aircraft has a low-alt and high-alt engine set on the F8 monitor.\n"
                + "• Idle engines are gray with Not running. Starting the high-alt set shuts down the low-alt set.\n"
                + "• Switching to high-alt engines flashes a HUD caution 5 times.\n"
                + "• High-alt engines that are simply offline are no longer treated as damaged.\n"
                + "• F8 layouts follow each airframe (AB-4 four-engine AB, tiltrotor combiner, helo gearbox, and so on).\n\n"
                + "0.0.9.187C\n"
                + "• Pilot health and per-plant engine wear (prop turbo/radiator, VTOL / STOVL / 4-engine).\n"
                + "• In-flight F8 Engine Component Monitor: click parts to queue a 2s repair (no cap drop); Repair All is instant and lowers the cap.\n"
                + "• Engine-part damage flashes a HUD caution 10 times.\n"
                + "• F8 tip chip sits with the other corner hints; F9 chip width matches the stack.\n"
                + "• F8 Engine Component Monitor can be toggled on Career Profile (main-menu F8 stays Profile).\n\n"
                + "0.0.9.175C\n"
                + "• Kill feed shows XP beside shot down / destroyed / sunk.\n"
                + "• Combat XP: hit +1 (cap 25 per aircraft), module +25, missile +10, ground +4, navy +100, carrier +500.\n"
                + "• High-skill AI climbs out after takeoff instead of beaming away at deck height.\n"
                + "• Helicopter collective / thrust bar no longer sticks at 100% reverse.\n"
                + "• F10 Repair All restores dead engines (nozzle thrust + prop / rotor condition).\n\n"
                + "Tip: press F1 for Oritasy System; Help | Guide (top-left) for the full keybind overview.";
        }
    }
}
