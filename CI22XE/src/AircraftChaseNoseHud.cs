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
    /// Nose-pointing HUD for aircraft third-person chase (tail) camera only.
    /// Hidden in cockpit / orbit / missile-pilot modes.
    /// </summary>
    internal static class AircraftChaseNoseHud
    {
        private static bool _overlayOn = true;
        private static GUIStyle _style;
        private static GUIStyle _small;

        internal static void Tick()
        {
            if (Plugin.AircraftChaseHudKey != null
                && Input.GetKeyDown(Plugin.AircraftChaseHudKey.Value)
                && !AirframeWearGui.ConsumedF8
                && IsAircraftTailChase())
                _overlayOn = !_overlayOn;
        }

        internal static void Draw()
        {
            if (!_overlayOn)
                return;
            if (!Plugin.AllowThirdPersonUi)
                return;
            if (Plugin.ShowAircraftChaseHud != null && !Plugin.ShowAircraftChaseHud.Value)
                return;
            if (MissileCameraHud.ManualActive)
                return;
            if (Event.current != null && Event.current.type != EventType.Repaint)
                return;
            if (!IsAircraftTailChase())
                return;

            Aircraft ac = null;
            try { GameManager.GetLocalAircraft(out ac); }
            catch { }
            if (ac == null)
                return;

            // Game view camera is CameraStateManager.mainCamera — Camera.main is often null.
            Camera cam = ResolveViewCamera();

            EnsureStyles();
            Rect view = new Rect(0f, 0f, UiScaleService.Width, UiScaleService.Height);
            DrawHud(view, cam, ac);
        }

        private static Camera ResolveViewCamera()
        {
            try
            {
                CameraStateManager csm = SceneSingleton<CameraStateManager>.i;
                if (csm != null && csm.mainCamera != null && csm.mainCamera.enabled)
                    return csm.mainCamera;
            }
            catch { }
            try
            {
                Camera main = Camera.main;
                if (main != null && main.enabled)
                    return main;
            }
            catch { }
            try
            {
                Camera[] cams = Camera.allCameras;
                if (cams != null)
                {
                    for (int i = 0; i < cams.Length; i++)
                    {
                        Camera c = cams[i];
                        if (c != null && c.enabled && c.targetTexture == null)
                            return c;
                    }
                }
            }
            catch { }
            return null;
        }

        private static bool IsAircraftTailChase()
        {
            Aircraft local = null;
            try { GameManager.GetLocalAircraft(out local); }
            catch { }
            if (local == null)
                return false;

            // Prefer explicit chase state; also allow orbit (external 3rd-person).
            try
            {
                CameraStateManager csm = SceneSingleton<CameraStateManager>.i;
                if (csm != null)
                {
                    if (object.ReferenceEquals(csm.currentState, csm.chaseState))
                        return FollowingLocalOrUnset(csm, local);
                    if (object.ReferenceEquals(csm.currentState, csm.orbitState))
                        return FollowingLocalOrUnset(csm, local);
                }
            }
            catch { }

            try
            {
                CameraMode mode = CameraStateManager.cameraMode;
                if (mode == CameraMode.chase || mode == CameraMode.orbit)
                    return true;
            }
            catch { }

            return false;
        }

        private static bool FollowingLocalOrUnset(CameraStateManager csm, Aircraft local)
        {
            if (csm == null || local == null)
                return false;
            try
            {
                Unit follow = csm.followingUnit;
                if (follow == null)
                    return true;
                if (object.ReferenceEquals(follow, local))
                    return true;
                // Chase may parent to cockpit part — accept same aircraft hierarchy
                try
                {
                    Aircraft fa = follow as Aircraft;
                    if (fa != null && object.ReferenceEquals(fa, local))
                        return true;
                    if (follow.transform != null
                        && local.transform != null
                        && follow.transform.IsChildOf(local.transform))
                        return true;
                }
                catch { }
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static void DrawHud(Rect view, Camera cam, Aircraft ac)
        {
            Transform mt = ac.transform;
            Vector3 noseFwd = mt.forward;
            Vector3 velFwd = noseFwd;
            float speed = 0f;
            try
            {
                speed = ac.speed;
                if (ac.rb != null && ac.rb.velocity.sqrMagnitude > 1f)
                {
                    velFwd = ac.rb.velocity.normalized;
                    if (speed < 0.5f)
                        speed = ac.rb.velocity.magnitude;
                }
            }
            catch { }

            float hdg = ChaseHudMathService.NormalizeHeadingDeg(mt.eulerAngles.y);
            int hdgI = ChaseHudMathService.HeadingInt(mt.eulerAngles.y);
            float pitch = ChaseHudMathService.PitchFromEulerX(mt.eulerAngles.x);
            float alt = 0f;
            try { alt = ac.radarAlt; }
            catch
            {
                try { alt = mt.position.y; }
                catch { }
            }

            // Compact tape near top-center (does not cover whole screen chrome)
            float tapeW = ChaseHudMathService.TapeWidthPx(UiScaleService.Width);
            Rect tapeHost = new Rect((UiScaleService.Width - tapeW) * 0.5f, UiScaleService.Height * 0.08f, tapeW, 22f);
            DrawHeadingTape(tapeHost, hdg);

            float cx = view.x + view.width * 0.5f;
            float cy = view.y + view.height * 0.48f;
            DrawHudCross(cx, cy, 14f, 2f, new Color(1f, 1f, 1f, 0.4f));

            if (cam != null)
            {
                DrawProjectedCaret(view, cam, mt.position + noseFwd * 200f,
                    new Color(0.35f, 1f, 0.4f, 0.95f), true);
                if (Vector3.Angle(noseFwd, velFwd) > 2.5f)
                    DrawProjectedCaret(view, cam, mt.position + velFwd * 200f,
                        new Color(0.3f, 0.9f, 1f, 0.9f), false);
            }

            string spd;
            try { spd = GameUnitDisplayService.Speed(speed); }
            catch { spd = (speed * 3.6f).ToString("0") + " km/h"; }

            string line = "TAIL  ·  HDG " + hdgI.ToString("000")
                + "   PIT " + pitch.ToString("+0.0;-0.0;0.0")
                + "   RALT " + alt.ToString("0")
                + "   " + spd;
            float stripW = ChaseHudMathService.StatusStripWidthPx(UiScaleService.Width);
            Rect strip = new Rect((UiScaleService.Width - stripW) * 0.5f, UiScaleService.Height * 0.86f, stripW, 28f);
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(strip, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(strip.x + 10f, strip.y + 4f, strip.width - 20f, 22f), line, _style);

            GUI.Label(new Rect(strip.x + 10f, strip.y - 18f, 50f, 16f), "NOSE", _small);
            GUI.color = new Color(0.35f, 1f, 0.4f, 0.95f);
            GUI.DrawTexture(new Rect(strip.x + 48f, strip.y - 12f, 12f, 6f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(strip.x + 68f, strip.y - 18f, 40f, 16f), "VEL", _small);
            GUI.color = new Color(0.3f, 0.9f, 1f, 0.9f);
            GUI.DrawTexture(new Rect(strip.x + 100f, strip.y - 12f, 12f, 6f), Texture2D.whiteTexture);
            GUI.color = prev;

            DrawStickThrottlePanel(ac);
            PlayerAutopilot.DrawChaseAlerts(ac);
        }

        /// <summary>Bottom-right stick cross + throttle bar (chase/orbit HUD).</summary>
        private static void DrawStickThrottlePanel(Aircraft ac)
        {
            ControlInputs ci = null;
            try { ci = ac.GetInputs(); }
            catch { }
            if (ci == null)
                return;

            float pitch = Mathf.Clamp(ci.pitch, -1f, 1f);
            float roll = Mathf.Clamp(ci.roll, -1f, 1f);
            float yaw = Mathf.Clamp(ci.yaw, -1f, 1f);
            float thr = Mathf.Clamp01(ci.throttle);

            EnsureStyles();
            float panelW = 156f;
            float panelH = 112f;
            float x = UiScaleService.Width - panelW - 14f;
            float y = UiScaleService.Height - panelH - 14f;
            Rect panel = new Rect(x, y, panelW, panelH);

            Color prev = GUI.color;
            GUI.color = new Color(0.02f, 0.05f, 0.04f, 0.62f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = new Color(0.3f, 0.9f, 0.45f, 0.85f);
            GUI.DrawTexture(new Rect(panel.x, panel.y, panel.width, 2f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(panel.x + 6f, panel.y + 3f, panel.width - 12f, 14f),
                "STICK / THR", _small);

            // Stick pad
            float pad = 64f;
            float padX = panel.x + 10f;
            float padY = panel.y + 22f;
            GUI.color = new Color(0.08f, 0.12f, 0.1f, 0.9f);
            GUI.DrawTexture(new Rect(padX, padY, pad, pad), Texture2D.whiteTexture);
            GUI.color = new Color(0.25f, 0.55f, 0.35f, 0.7f);
            float midX = padX + pad * 0.5f;
            float midY = padY + pad * 0.5f;
            GUI.DrawTexture(new Rect(padX + 2f, midY - 0.5f, pad - 4f, 1f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(midX - 0.5f, padY + 2f, 1f, pad - 4f), Texture2D.whiteTexture);

            // Cursor: roll → X, pitch → Y (up = pull = positive pitch in NO)
            float cx = midX + roll * (pad * 0.42f);
            float cy = midY - pitch * (pad * 0.42f);
            GUI.color = new Color(0.35f, 1f, 0.45f, 0.95f);
            GUI.DrawTexture(new Rect(cx - 3f, cy - 3f, 6f, 6f), Texture2D.whiteTexture);

            // Yaw tick under stick
            float yawBarW = pad;
            float yawBarY = padY + pad + 4f;
            GUI.color = new Color(0.08f, 0.12f, 0.1f, 0.9f);
            GUI.DrawTexture(new Rect(padX, yawBarY, yawBarW, 6f), Texture2D.whiteTexture);
            GUI.color = new Color(0.3f, 0.85f, 1f, 0.95f);
            float yawX = padX + yawBarW * 0.5f + yaw * (yawBarW * 0.42f);
            GUI.DrawTexture(new Rect(yawX - 2f, yawBarY, 4f, 6f), Texture2D.whiteTexture);

            // Throttle vertical bar
            float thrX = padX + pad + 12f;
            float thrW = 14f;
            float thrH = pad;
            GUI.color = new Color(0.08f, 0.12f, 0.1f, 0.9f);
            GUI.DrawTexture(new Rect(thrX, padY, thrW, thrH), Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.75f, 0.25f, 0.95f);
            float fillH = thrH * thr;
            GUI.DrawTexture(new Rect(thrX, padY + thrH - fillH, thrW, fillH), Texture2D.whiteTexture);

            GUI.color = Color.white;
            string vals = "P " + pitch.ToString("+0.00;-0.00")
                + "  R " + roll.ToString("+0.00;-0.00")
                + "\nY " + yaw.ToString("+0.00;-0.00")
                + "  T " + (thr * 100f).ToString("0") + "%";
            GUI.Label(new Rect(panel.x + 6f, panel.yMax - 28f, panel.width - 12f, 26f), vals, _small);
            GUI.color = prev;
        }

        private static void DrawHeadingTape(Rect tape, float hdg)
        {
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(tape, Texture2D.whiteTexture);
            GUI.color = new Color(0.2f, 1f, 0.4f, 0.9f);
            float midX = tape.x + tape.width * 0.5f;
            GUI.DrawTexture(new Rect(midX - 1f, tape.y, 2f, tape.height), Texture2D.whiteTexture);
            for (int d = -40; d <= 40; d += 10)
            {
                float bearing = ChaseHudMathService.Wrap360(hdg + d);
                float px = ChaseHudMathService.TapeTickX(midX, tape.width, d);
                float th = (d % 30 == 0) ? tape.height * 0.75f : tape.height * 0.4f;
                GUI.DrawTexture(new Rect(px - 0.5f, tape.yMax - th, 1.5f, th), Texture2D.whiteTexture);
                if (d % 30 == 0)
                {
                    int label = ChaseHudMathService.HeadingInt(bearing);
                    GUI.Label(new Rect(px - 16f, tape.y - 2f, 32f, 14f),
                        label.ToString("000"), _small);
                }
            }
            GUI.color = prev;
        }

        private static void DrawProjectedCaret(Rect view, Camera cam, Vector3 world, Color color, bool noseStyle)
        {
            Vector3 vp;
            try { vp = cam.WorldToViewportPoint(world); }
            catch { return; }
            if (vp.z <= 0.05f)
                return;
            float x = view.x + Mathf.Clamp01(vp.x) * view.width;
            float y = view.y + (1f - Mathf.Clamp01(vp.y)) * view.height;
            x = Mathf.Clamp(x, view.x + 8f, view.xMax - 8f);
            y = Mathf.Clamp(y, view.y + 8f, view.yMax - 8f);
            if (noseStyle)
                DrawHudDiamond(x, y, 9f, color);
            else
                DrawHudCross(x, y, 11f, 2f, color);
        }

        private static void DrawHudCross(float cx, float cy, float arm, float th, Color color)
        {
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(cx - arm, cy - th * 0.5f, arm * 2f, th), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - th * 0.5f, cy - arm, th, arm * 2f), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        private static void DrawHudDiamond(float cx, float cy, float r, Color color)
        {
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(cx - r, cy - 1.5f, r * 2f, 3f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - 1.5f, cy - r, 3f, r * 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - r * 0.55f, cy - r * 0.55f, r * 1.1f, r * 1.1f), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        private static void EnsureStyles()
        {
            if (_style != null)
                return;
            _style = new GUIStyle(GUI.skin.label);
            _style.fontSize = 15;
            _style.fontStyle = FontStyle.Bold;
            _style.normal.textColor = new Color(0.35f, 1f, 0.45f, 0.95f);
            _style.alignment = TextAnchor.MiddleLeft;

            _small = new GUIStyle(GUI.skin.label);
            _small.fontSize = 11;
            _small.normal.textColor = new Color(0.75f, 0.95f, 0.8f, 0.9f);
            _small.alignment = TextAnchor.UpperLeft;
            _small.wordWrap = true;
        }
    }
}
