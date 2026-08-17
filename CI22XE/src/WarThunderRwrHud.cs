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
    /// War Thunder-style circular RWR for the local aircraft:
    /// radar contacts, hard-lock spikes, and missile launch warnings.
    /// </summary>
    internal static class WarThunderRwrHud
    {
        private static readonly List<AircraftRwrService.Blip> Blips = new List<AircraftRwrService.Blip>(32);
        private static bool _overlayOn = true;
        private static bool _layoutMenuOpen;
        private static Aircraft _boundAc;
        private static float _nextMissileRefresh;
        private static GUIStyle _label;
        private static GUIStyle _warnLabel;
        private static GUIStyle _chipHintStyle;
        private static GUIStyle _menuTitle;
        private static GUIStyle _menuLabel;
        private static GUIStyle _menuBtn;
        private static bool _cursorHeld;
        private static readonly Color Green = new Color(0.2f, 0.95f, 0.35f, 0.95f);
        private static readonly Color Amber = new Color(1f, 0.78f, 0.15f, 0.95f);
        private static readonly Color Red = new Color(1f, 0.22f, 0.18f, 0.98f);

        internal static bool LayoutMenuOpen
        {
            get { return _layoutMenuOpen; }
        }

        internal static void CloseLayoutMenuFromOutside()
        {
            CloseLayoutMenu();
        }

        internal static bool HasActiveLock()
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < Blips.Count; i++)
            {
                if (Blips[i].Expires > now && Blips[i].Locked && !Blips[i].Missile)
                    return true;
            }
            return false;
        }

        internal static bool HasActiveSearch()
        {
            float now = Time.unscaledTime;
            for (int i = 0; i < Blips.Count; i++)
            {
                if (Blips[i].Expires > now && !Blips[i].Missile && !Blips[i].Locked)
                    return true;
            }
            return false;
        }

        internal static void Tick()
        {
            if (MissileCameraHud.ManualActive)
            {
                // F6 has its own RWR on OritasyHud
                Unbind();
                Blips.Clear();
                if (_layoutMenuOpen)
                    CloseLayoutMenu();
                return;
            }

            KeyCode key = Plugin.AircraftRwrKey != null ? Plugin.AircraftRwrKey.Value : KeyCode.F11;
            if (Input.GetKeyDown(key))
            {
                if (_layoutMenuOpen)
                    CloseLayoutMenu();
                else
                    OpenLayoutMenu();
            }
            if (_layoutMenuOpen && Input.GetKeyDown(KeyCode.Escape))
                CloseLayoutMenu();

            Aircraft ac = ResolveLocal();
            if (ac == null)
            {
                Unbind();
                Blips.Clear();
                if (_layoutMenuOpen)
                    CloseLayoutMenu();
                return;
            }

            // Keep blips live for chase-HUD alerts even when the RWR disc is hidden.
            Bind(ac);
            // Missile warning scan ~10 Hz is enough for blips (was every frame).
            if (Time.unscaledTime >= _nextMissileRefresh)
            {
                _nextMissileRefresh = Time.unscaledTime + 0.1f;
                RefreshMissiles(ac);
            }
            Prune();
        }

        internal static void Draw()
        {
            if (MissileCameraHud.ManualActive)
                return;

            Aircraft ac = ResolveLocal();
            if (Plugin.AllowThirdPersonUi)
                DrawCornerHint(ac);

            if (_layoutMenuOpen)
            {
                HoldCursor();
                DrawLayoutMenu();
            }

            if (!Plugin.AllowThirdPersonUi)
                return;
            if (!_overlayOn)
                return;
            if (Plugin.ShowAircraftRwr != null && !Plugin.ShowAircraftRwr.Value)
                return;
            if (ac == null)
                return;
            if (Event.current != null && Event.current.type != EventType.Repaint
                && !_layoutMenuOpen)
                return;
            // While layout menu open, still paint scope on Repaint so preview updates.
            if (Event.current != null && Event.current.type != EventType.Repaint)
                return;

            EnsureStyles();
            float radius;
            Vector2 center;
            ResolveLayout(out center, out radius);
            DrawScope(center, radius, ac);
        }

        private static void DrawCornerHint(Aircraft ac)
        {
            if (ac == null)
                return;
            if (Event.current != null && Event.current.type != EventType.Repaint)
                return;
            EnsureStyles();
            Rect chip = PlayerAutopilot.CornerChipRect(AssistMenuLayoutService.SlotF11);
            Color prev = GUI.color;
            GUI.color = new Color(0.05f, 0.07f, 0.1f, 0.78f);
            GUI.DrawTexture(chip, Texture2D.whiteTexture);
            GUI.color = _layoutMenuOpen
                ? new Color(0.95f, 0.75f, 0.3f, 0.95f)
                : new Color(0.4f, 0.9f, 0.55f, 0.9f);
            GUI.DrawTexture(new Rect(chip.x, chip.y, chip.width, 2f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            string line = _layoutMenuOpen
                ? UiLang.T("F11  " + AircraftRwrService.DisplayName + "  |  LAYOUT",
                    "F11  " + AircraftRwrService.DisplayName + "  |  布局")
                : UiLang.T("F11  " + AircraftRwrService.DisplayName,
                    "F11  " + AircraftRwrService.DisplayName);
            _chipHintStyle.normal.textColor = new Color(0.8f, 1f, 0.85f, 0.95f);
            GUI.Label(new Rect(chip.x + 6f, chip.y, chip.width - 12f, chip.height), line, _chipHintStyle);
            GUI.color = prev;
        }

        private static void ResolveLayout(out Vector2 center, out float radius)
        {
            float sizeFrac = 0.18f;
            float nx = 0.12f;
            float ny = 0.88f;
            try
            {
                if (Plugin.AircraftRwrSize != null)
                    sizeFrac = Plugin.AircraftRwrSize.Value;
                if (Plugin.AircraftRwrNormX != null)
                    nx = Plugin.AircraftRwrNormX.Value;
                if (Plugin.AircraftRwrNormY != null)
                    ny = Plugin.AircraftRwrNormY.Value;
            }
            catch { }
            AircraftRwrService.DiscLayout L = AircraftRwrService.ResolveDisc(
                UiScaleService.Width, UiScaleService.Height, sizeFrac, nx, ny);
            center = L.Center;
            radius = L.Radius;
        }

        private static void OpenLayoutMenu()
        {
            if (MissileCameraHud.ManualActive)
                return;
            if (AircraftManeuverGui.IsOpen)
                AircraftManeuverGui.Close();
            if (PlayerAutopilot.MenuOpen)
                PlayerAutopilot.CloseMenuFromOutside();
            if (AerialResupply.MenuOpen)
                AerialResupply.CloseMenuFromOutside();
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
            _layoutMenuOpen = true;
            CaptureCursor();
        }

        private static void CloseLayoutMenu()
        {
            _layoutMenuOpen = false;
            ReleaseCursor();
        }

        private static void DrawLayoutMenu()
        {
            EnsureStyles();
            float w = 380f;
            float h = 280f;
            Rect box = new Rect((UiScaleService.Width - w) * 0.5f, (UiScaleService.Height - h) * 0.5f, w, h);
            Color prev = GUI.color;
            GUI.color = new Color(0.06f, 0.08f, 0.1f, 0.92f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = new Color(0.4f, 0.95f, 0.55f, 0.95f);
            GUI.DrawTexture(new Rect(box.x, box.y, box.width, 3f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(box.x + 16f, box.y + 12f, box.width - 32f, 24f),
                UiLang.T(AircraftRwrService.DisplayName + "  (F11)",
                    AircraftRwrService.DisplayName + "  (F11)"), _menuTitle);

            float y = box.y + 48f;
            if (Plugin.ShowAircraftRwr != null)
            {
                bool show = GUI.Toggle(new Rect(box.x + 16f, y, box.width - 32f, 22f),
                    Plugin.ShowAircraftRwr.Value,
                    UiLang.T(" Show " + AircraftRwrService.DisplayName,
                        " 显示 " + AircraftRwrService.DisplayName));
                if (show != Plugin.ShowAircraftRwr.Value)
                    Plugin.ShowAircraftRwr.Value = show;
                _overlayOn = show;
            }
            y += 28f;

            float nx = Plugin.AircraftRwrNormX != null ? Plugin.AircraftRwrNormX.Value : 0.12f;
            float ny = Plugin.AircraftRwrNormY != null ? Plugin.AircraftRwrNormY.Value : 0.88f;
            float sz = Plugin.AircraftRwrSize != null ? Plugin.AircraftRwrSize.Value : 0.18f;

            GUI.Label(new Rect(box.x + 16f, y, box.width - 32f, 18f),
                UiLang.T("Horizontal  " + (nx * 100f).ToString("0") + "%",
                    "水平  " + (nx * 100f).ToString("0") + "%"), _menuLabel);
            y += 20f;
            nx = GUI.HorizontalSlider(new Rect(box.x + 16f, y, box.width - 32f, 16f), nx, 0.05f, 0.95f);
            y += 24f;

            GUI.Label(new Rect(box.x + 16f, y, box.width - 32f, 18f),
                UiLang.T("Vertical  " + (ny * 100f).ToString("0") + "%",
                    "垂直  " + (ny * 100f).ToString("0") + "%"), _menuLabel);
            y += 20f;
            ny = GUI.HorizontalSlider(new Rect(box.x + 16f, y, box.width - 32f, 16f), ny, 0.05f, 0.95f);
            y += 24f;

            GUI.Label(new Rect(box.x + 16f, y, box.width - 32f, 18f),
                UiLang.T("Size  " + (sz * 100f).ToString("0") + "% screen",
                    "尺寸  " + (sz * 100f).ToString("0") + "% 屏幕"), _menuLabel);
            y += 20f;
            sz = GUI.HorizontalSlider(new Rect(box.x + 16f, y, box.width - 32f, 16f), sz, 0.10f, 0.35f);
            y += 28f;

            if (Plugin.AircraftRwrNormX != null)
                Plugin.AircraftRwrNormX.Value = nx;
            if (Plugin.AircraftRwrNormY != null)
                Plugin.AircraftRwrNormY.Value = ny;
            if (Plugin.AircraftRwrSize != null)
                Plugin.AircraftRwrSize.Value = sz;

            float bw = (box.width - 48f) * 0.5f;
            if (GUI.Button(new Rect(box.x + 16f, y, bw, 30f),
                UiLang.T("Reset", "重置"), _menuBtn))
            {
                if (Plugin.AircraftRwrNormX != null)
                    Plugin.AircraftRwrNormX.Value = 0.12f;
                if (Plugin.AircraftRwrNormY != null)
                    Plugin.AircraftRwrNormY.Value = 0.88f;
                if (Plugin.AircraftRwrSize != null)
                    Plugin.AircraftRwrSize.Value = 0.18f;
            }
            if (GUI.Button(new Rect(box.x + 24f + bw, y, bw, 30f),
                UiLang.T("Close", "关闭"), _menuBtn))
                CloseLayoutMenu();

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

        private static void DrawScope(Vector2 center, float radius, Aircraft ac)
        {
            Color prev = GUI.color;
            bool flash = (Time.unscaledTime % 0.36f) < 0.18f;
            bool anyMissile = false;
            bool anyLock = false;
            for (int i = 0; i < Blips.Count; i++)
            {
                if (Blips[i].Missile)
                    anyMissile = true;
                else if (Blips[i].Locked)
                    anyLock = true;
            }

            // CRT disc (one textured quad — not hundreds of IMGUI spans)
            GUI.color = new Color(0.02f, 0.08f, 0.04f, 0.72f);
            FillCircle(center, radius + 6f);

            // Outer ring — pulse red on missile launch (fewer segments for FPS)
            if (anyMissile && flash)
                GUI.color = Red;
            else if (anyLock)
                GUI.color = Amber;
            else
                GUI.color = new Color(0.15f, 0.85f, 0.3f, 0.7f);
            StrokeCircle(center, radius, 2.2f, 28);

            GUI.color = new Color(0.15f, 0.7f, 0.28f, 0.35f);
            StrokeCircle(center, radius * 0.62f, 1.2f, 20);
            StrokeCircle(center, radius * 0.32f, 1f, 16);

            // Cardinals (nose-up)
            GUI.color = Green;
            RadialTick(center, radius, 0f, 12f);
            RadialTick(center, radius, 90f, 8f);
            RadialTick(center, radius, 180f, 8f);
            RadialTick(center, radius, 270f, 8f);

            // Ownship chevron
            GUI.color = Green;
            GUI.DrawTexture(new Rect(center.x - 2f, center.y - 10f, 4f, 18f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(center.x - 8f, center.y - 2f, 16f, 3f), Texture2D.whiteTexture);

            for (int i = 0; i < Blips.Count; i++)
            {
                AircraftRwrService.Blip b = Blips[i];
                float r = AircraftRwrService.BlipRingRadius(radius, b.RangeNorm, b.Missile);
                Vector2 p = AircraftRwrService.BlipScreenPos(center, r, b.Bearing);

                if (b.Missile)
                {
                    if (!flash)
                        continue;
                    GUI.color = Red;
                    DrawMissileMark(p, 8f);
                }
                else if (b.Locked)
                {
                    GUI.color = flash ? Red : Amber;
                    DrawLockDiamond(p, 7f);
                }
                else
                {
                    GUI.color = Green;
                    DrawRadarBar(p, b.Bearing, 6f);
                }
            }

            GUI.color = Color.white;
            string title = AircraftRwrService.DisplayName;
            if (anyMissile)
                title = flash ? UiLang.T("LAUNCH", "发射") : AircraftRwrService.DisplayName;
            else if (anyLock)
                title = flash ? UiLang.T("LOCK", "锁定") : AircraftRwrService.DisplayName;
            _warnLabel.normal.textColor = anyMissile ? Red : (anyLock ? Amber : Green);
            GUI.Label(new Rect(center.x - 110f, center.y + radius + 4f, 220f, 18f), title, _warnLabel);

            int mCount = 0;
            int lCount = 0;
            int sCount = 0;
            for (int i = 0; i < Blips.Count; i++)
            {
                if (Blips[i].Missile)
                    mCount++;
                else if (Blips[i].Locked)
                    lCount++;
                else
                    sCount++;
            }
            if (mCount + lCount + sCount > 0)
            {
                string info = string.Empty;
                if (mCount > 0)
                    info += "M" + mCount + " ";
                if (lCount > 0)
                    info += "L" + lCount + " ";
                if (sCount > 0)
                    info += "S" + sCount;
                GUI.Label(new Rect(center.x - 48f, center.y - radius - 20f, 96f, 16f),
                    info.Trim(), _label);
            }

            GUI.color = prev;
        }

        private static void Bind(Aircraft ac)
        {
            if (object.ReferenceEquals(ac, _boundAc))
                return;
            Unbind();
            _boundAc = ac;
            try { ac.onRadarWarning += OnRadarWarning; }
            catch { }
        }

        private static void Unbind()
        {
            if (_boundAc != null)
            {
                try { _boundAc.onRadarWarning -= OnRadarWarning; }
                catch { }
            }
            _boundAc = null;
        }

        private static void OnRadarWarning(Aircraft.OnRadarWarning e)
        {
            if (_boundAc == null || e.emitter == null)
                return;
            try
            {
                float bearing = AircraftRwrService.RelativeBearing(_boundAc, e.emitter.transform.position);
                float rangeNorm = AircraftRwrService.RangeNormFromPower(e.power);
                AircraftRwrService.Upsert(Blips, e.emitter.GetInstanceID(), bearing, rangeNorm, false, e.isTarget, 3.2f);
            }
            catch { }
        }

        private static void RefreshMissiles(Aircraft ac)
        {
            try
            {
                MissileWarning mw = ac.GetMissileWarningSystem();
                if (mw == null || mw.knownMissiles == null)
                    return;
                for (int i = 0; i < mw.knownMissiles.Count; i++)
                {
                    Missile m = mw.knownMissiles[i];
                    if (m == null)
                        continue;
                    float dist = Vector3.Distance(ac.transform.position, m.transform.position);
                    float rangeNorm = AircraftRwrService.RangeNormFromDistance(dist, 9000f);
                    float bearing = AircraftRwrService.RelativeBearing(ac, m.transform.position);
                    AircraftRwrService.Upsert(Blips, m.GetInstanceID(), bearing, rangeNorm, true, true, 1.1f);
                }
            }
            catch { }
        }

        private static void Prune()
        {
            AircraftRwrService.Prune(Blips);
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

        private static void EnsureStyles()
        {
            if (_label != null)
                return;
            _label = new GUIStyle(GUI.skin.label);
            _label.fontSize = 11;
            _label.fontStyle = FontStyle.Bold;
            _label.alignment = TextAnchor.MiddleCenter;
            _label.normal.textColor = Green;

            _warnLabel = new GUIStyle(GUI.skin.label);
            _warnLabel.fontSize = 12;
            _warnLabel.fontStyle = FontStyle.Bold;
            _warnLabel.alignment = TextAnchor.MiddleCenter;
            _warnLabel.wordWrap = false;
            _warnLabel.clipping = TextClipping.Overflow;
            _warnLabel.normal.textColor = Green;

            _chipHintStyle = new GUIStyle(GUI.skin.label);
            _chipHintStyle.fontSize = 11;
            _chipHintStyle.fontStyle = FontStyle.Bold;
            _chipHintStyle.alignment = TextAnchor.MiddleRight;
            _chipHintStyle.normal.textColor = new Color(0.8f, 1f, 0.85f, 0.95f);

            _menuTitle = new GUIStyle(GUI.skin.label);
            _menuTitle.fontSize = 18;
            _menuTitle.fontStyle = FontStyle.Bold;
            _menuTitle.alignment = TextAnchor.MiddleLeft;
            _menuTitle.normal.textColor = new Color(0.75f, 1f, 0.85f, 1f);

            _menuLabel = new GUIStyle(GUI.skin.label);
            _menuLabel.fontSize = 13;
            _menuLabel.alignment = TextAnchor.MiddleLeft;
            _menuLabel.normal.textColor = new Color(0.85f, 0.95f, 0.9f, 0.95f);

            _menuBtn = new GUIStyle(GUI.skin.button);
            _menuBtn.fontSize = 13;
            _menuBtn.fontStyle = FontStyle.Bold;
            _menuBtn.alignment = TextAnchor.MiddleCenter;
            _menuBtn.normal.textColor = Color.white;
        }

        private static Texture2D _discTex;

        internal static Texture2D SharedDiscTex()
        {
            return DiscTex();
        }

        private static Texture2D DiscTex()
        {
            if (_discTex != null)
                return _discTex;
            const int s = 64;
            _discTex = new Texture2D(s, s, TextureFormat.ARGB32, false);
            _discTex.wrapMode = TextureWrapMode.Clamp;
            _discTex.filterMode = FilterMode.Bilinear;
            float mid = (s - 1) * 0.5f;
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float dx = x - mid;
                    float dy = y - mid;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / (mid + 0.5f);
                    float a = d <= 0.92f ? 1f : (d >= 1f ? 0f : 1f - (d - 0.92f) / 0.08f);
                    _discTex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            _discTex.Apply(false, true);
            return _discTex;
        }

        private static void FillCircle(Vector2 c, float r)
        {
            float d = r * 2f;
            GUI.DrawTexture(new Rect(c.x - r, c.y - r, d, d), DiscTex());
        }

        private static void StrokeCircle(Vector2 c, float r, float thickness, int segments)
        {
            segments = PerfMode.RwrStrokeSegments(segments);
            if (segments < 8)
                segments = 8;
            if (segments > 32)
                segments = 32;
            for (int i = 0; i < segments; i++)
            {
                float a0 = (i / (float)segments) * Mathf.PI * 2f;
                float a1 = ((i + 1) / (float)segments) * Mathf.PI * 2f;
                Vector2 p0 = new Vector2(c.x + Mathf.Sin(a0) * r, c.y - Mathf.Cos(a0) * r);
                Vector2 p1 = new Vector2(c.x + Mathf.Sin(a1) * r, c.y - Mathf.Cos(a1) * r);
                Line(p0, p1, thickness);
            }
        }

        private static void RadialTick(Vector2 c, float r, float bearingDeg, float len)
        {
            float rad = bearingDeg * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad));
            Line(c + dir * (r - len), c + dir * r, 2f);
        }

        private static void Line(Vector2 a, Vector2 b, float thickness)
        {
            UiScaleService.DrawLine(a, b, thickness);
        }

        private static void DrawRadarBar(Vector2 p, float bearing, float s)
        {
            // Short bar perpendicular to radial (WT search/track tick)
            float rad = bearing * Mathf.Deg2Rad;
            Vector2 tang = new Vector2(Mathf.Cos(rad), Mathf.Sin(rad));
            Line(p - tang * s, p + tang * s, 3f);
        }

        private static void DrawLockDiamond(Vector2 p, float s)
        {
            GUI.DrawTexture(new Rect(p.x - s, p.y - 1.5f, s * 2f, 3f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(p.x - 1.5f, p.y - s, 3f, s * 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(p.x - s * 0.55f, p.y - s * 0.55f, s * 1.1f, s * 1.1f),
                Texture2D.whiteTexture);
        }

        private static void DrawMissileMark(Vector2 p, float s)
        {
            // Inverted caret / arrow toward center urgency
            GUI.DrawTexture(new Rect(p.x - s, p.y - 1f, s * 2f, 3f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(p.x - 1.5f, p.y - s, 3f, s * 1.6f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(p.x - s * 0.7f, p.y - s * 0.2f, s * 1.4f, s * 0.9f),
                Texture2D.whiteTexture);
        }
    }
}
