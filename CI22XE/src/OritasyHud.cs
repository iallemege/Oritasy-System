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
    /// Dedicated Oritasy flight HUD (SPD / HDG / ACC / G) plus circular RWR.
    /// Bottom-center so it clears left missile cam and right G meter.
    /// </summary>
    internal static class OritasyHud
    {
        private static readonly List<AircraftRwrService.Blip> Blips = new List<AircraftRwrService.Blip>(32);
        private static readonly FieldInfo EmittersField = AccessTools.Field(typeof(VaporEffect), "emitters");
        private static readonly FieldInfo ContrailField = AccessTools.Field(typeof(VaporEmitter), "contrail");
        private static readonly FieldInfo MinAltField = AccessTools.Field(typeof(VaporEmitter), "minAltitude");
        private static readonly FieldInfo MaxAltField = AccessTools.Field(typeof(VaporEmitter), "maxAltitude");

        private static bool _overlayOn = true;
        private static Aircraft _boundAc;
        private static Vector3 _missileVelPrev;
        private static float _missileVelPrevTime;
        private static GUIStyle _stripStyle;
        private static GUIStyle _stripSmall;
        private static GUIStyle _rwrLabel;
        private static readonly Color HudGreen = new Color(0.15f, 1f, 0.35f, 0.95f);
        private static readonly Color HudAmber = new Color(1f, 0.78f, 0.2f, 0.95f);
        private static readonly Color HudRed = new Color(1f, 0.28f, 0.22f, 0.95f);

        internal static void Tick()
        {
            // Never active while flying the aircraft — only F6 manual missile pilot.
            if (!MissileCameraHud.ManualActive)
            {
                UnbindRadar();
                Blips.Clear();
                return;
            }

            if (Plugin.OritasyHudKey != null && Input.GetKeyDown(Plugin.OritasyHudKey.Value))
                _overlayOn = !_overlayOn;

            if (!HudWanted())
            {
                UnbindRadar();
                return;
            }

            Aircraft ac = ResolveLocalAircraft();
            if (ac == null)
            {
                UnbindRadar();
                return;
            }

            BindRadar(ac);
            RefreshMissileBlips(ac);
            PruneBlips();
        }

        internal static void Draw()
        {
            // Independent HUD must not appear while piloting the aircraft.
            if (!MissileCameraHud.ManualActive)
                return;
            if (!HudWanted())
                return;
            if (Event.current != null && Event.current.type != EventType.Repaint)
                return;

            Missile missile = MissileCameraHud.FollowMissile;
            if (missile == null)
                return;

            EnsureStyles();

            bool showStrip = Plugin.ShowOritasyHud == null || Plugin.ShowOritasyHud.Value;
            bool showRwr = Plugin.ShowCircularRwr == null || Plugin.ShowCircularRwr.Value;
            if (!showStrip && !showRwr)
                return;

            float stripH = 54f;
            float stripW = Mathf.Clamp(UiScaleService.Width * 0.46f, 420f, 720f);
            float stripX = (UiScaleService.Width - stripW) * 0.5f;
            float stripY = UiScaleService.Height - stripH - 18f;

            if (showRwr)
            {
                float dia = Mathf.Clamp(Mathf.Min(UiScaleService.Width, UiScaleService.Height) * 0.2f, 140f, 220f);
                float rwrCx = UiScaleService.Width * 0.5f;
                float rwrCy = stripY - dia * 0.55f - 10f;
                Aircraft ac = ResolveLocalAircraft();
                if (ac != null)
                    DrawRwr(new Vector2(rwrCx, rwrCy), dia * 0.5f, ac);
            }

            if (showStrip)
                DrawMissileStrip(new Rect(stripX, stripY, stripW, stripH), missile);
        }

        internal static void Shutdown()
        {
            UnbindRadar();
            Blips.Clear();
        }

        /// <summary>Cached for VaporEmitter.Emit (hot). False on LowEnd / when airflow off.</summary>
        internal static bool AirflowSofteningActive;

        internal static void TickAirflowFlag()
        {
            AirflowSofteningActive = Plugin.EnhancedAirflow != null
                && Plugin.EnhancedAirflow.Value
                && !PerfMode.IsLow;
        }

        internal static void EnhanceVaporEffect(VaporEffect effect)
        {
            if (Plugin.EnhancedAirflow == null || !Plugin.EnhancedAirflow.Value)
                return;
            if (effect == null || EmittersField == null)
                return;
            try
            {
                VaporEmitter[] emitters = EmittersField.GetValue(effect) as VaporEmitter[];
                if (emitters == null)
                    return;
                for (int i = 0; i < emitters.Length; i++)
                {
                    VaporEmitter e = emitters[i];
                    if (e == null)
                        continue;
                    e.opacity = Mathf.Clamp(e.opacity * 1.25f, 0.5f, 1.45f);
                    // Do not raise emitFrequency — 1.55× particles is a High-quality hitch.
                    e.minSpeed = Mathf.Max(8f, e.minSpeed * 0.7f);
                    bool contrail = false;
                    if (ContrailField != null)
                    {
                        try { contrail = (bool)ContrailField.GetValue(e); }
                        catch { }
                    }
                    if (contrail)
                    {
                        if (MinAltField != null)
                            MinAltField.SetValue(e, 5000f);
                        if (MaxAltField != null)
                            MaxAltField.SetValue(e, 16000f);
                    }
                }
            }
            catch { }
        }

        internal static void SoftenVaporEmit(ref float alpha, ref float detail)
        {
            if (!AirflowSofteningActive)
                return;
            detail = Mathf.Max(detail, 0.65f);
            alpha *= 1.2f;
        }

        private static bool HudWanted()
        {
            if (!_overlayOn)
                return false;
            bool strip = Plugin.ShowOritasyHud != null && Plugin.ShowOritasyHud.Value;
            bool rwr = Plugin.ShowCircularRwr != null && Plugin.ShowCircularRwr.Value;
            return strip || rwr;
        }

        private static void DrawMissileStrip(Rect r, Missile missile)
        {
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = HudGreen;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.yMax - 2f, r.width, 2f), Texture2D.whiteTexture);
            GUI.color = prev;

            float speed = 0f;
            Vector3 vel = Vector3.zero;
            try
            {
                if (missile.rb != null)
                    vel = missile.rb.velocity;
                speed = missile.speed;
                if (speed < 1f && vel.sqrMagnitude > 1f)
                    speed = vel.magnitude;
            }
            catch { }
            string spd;
            try { spd = GameUnitDisplayService.Speed(speed); }
            catch { spd = (speed * 3.6f).ToString("0") + "km/h"; }

            float hdg = 0f;
            try { hdg = missile.transform.eulerAngles.y; }
            catch { }
            if (hdg < 0f)
                hdg += 360f;
            int hdgI = Mathf.RoundToInt(hdg) % 360;
            if (hdgI < 0)
                hdgI += 360;

            Vector3 accelG = Vector3.zero;
            float now = Time.unscaledTime;
            float dt = now - _missileVelPrevTime;
            if (dt > 0.001f && dt < 0.5f && _missileVelPrevTime > 0f)
                accelG = (vel - _missileVelPrev) / (dt * 9.81f);
            _missileVelPrev = vel;
            _missileVelPrevTime = now;

            float accMag = accelG.magnitude;
            float accFwd = 0f;
            float accLat = 0f;
            float g = accMag;
            try
            {
                accFwd = Vector3.Dot(accelG, missile.transform.forward);
                accLat = Vector3.Dot(accelG, missile.transform.right);
                g = Vector3.Dot(accelG, missile.transform.up);
            }
            catch { }

            string gSign = g >= 0f ? "+" : string.Empty;

            float colW = r.width * 0.25f;
            float x0 = r.x + 8f;
            float y0 = r.y + 6f;
            float y1 = r.y + 28f;

            GUI.Label(new Rect(x0, y0, colW - 8f, 18f), "SPD", _stripSmall);
            GUI.Label(new Rect(x0, y1, colW - 8f, 22f), spd, _stripStyle);

            GUI.Label(new Rect(x0 + colW, y0, colW - 8f, 18f), "HDG", _stripSmall);
            GUI.Label(new Rect(x0 + colW, y1, colW - 8f, 22f), hdgI.ToString("000") + "°", _stripStyle);

            GUI.Label(new Rect(x0 + colW * 2f, y0, colW - 8f, 18f), "ACC", _stripSmall);
            GUI.Label(new Rect(x0 + colW * 2f, y1, colW - 8f, 22f),
                accMag.ToString("0.0") + "G  F" + accFwd.ToString("+0.0;-0.0") + " L" + accLat.ToString("+0.0;-0.0"),
                _stripStyle);

            Color gPrev = _stripStyle.normal.textColor;
            _stripStyle.normal.textColor = Mathf.Abs(g) >= 20f ? HudRed : (Mathf.Abs(g) >= 12f ? HudAmber : HudGreen);
            GUI.Label(new Rect(x0 + colW * 3f, y0, colW - 8f, 18f), "G", _stripSmall);
            GUI.Label(new Rect(x0 + colW * 3f, y1, colW - 8f, 22f), gSign + g.ToString("0.0"), _stripStyle);
            _stripStyle.normal.textColor = gPrev;
        }

        private static void DrawRwr(Vector2 center, float radius, Aircraft ac)
        {
            Color prev = GUI.color;

            // Background disc (shared soft texture — not span fill)
            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            DrawFilledCircle(center, radius + 4f, 48);

            GUI.color = new Color(0.15f, 1f, 0.35f, 0.55f);
            DrawCircle(center, radius, 2f, 28);
            GUI.color = new Color(0.15f, 1f, 0.35f, 0.25f);
            DrawCircle(center, radius * 0.55f, 1f, 20);

            // Cardinal ticks (nose up)
            GUI.color = HudGreen;
            DrawRadialTick(center, radius, 0f, 10f);
            DrawRadialTick(center, radius, 90f, 7f);
            DrawRadialTick(center, radius, 180f, 7f);
            DrawRadialTick(center, radius, 270f, 7f);

            // Ownship
            GUI.color = HudGreen;
            GUI.DrawTexture(new Rect(center.x - 1.5f, center.y - 8f, 3f, 16f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(center.x - 7f, center.y - 1f, 14f, 3f), Texture2D.whiteTexture);

            for (int i = 0; i < Blips.Count; i++)
            {
                AircraftRwrService.Blip b = Blips[i];
                float r = Mathf.Lerp(radius * 0.35f, radius * 0.92f, Mathf.Clamp01(b.RangeNorm));
                float rad = b.Bearing * Mathf.Deg2Rad;
                Vector2 p = new Vector2(
                    center.x + Mathf.Sin(rad) * r,
                    center.y - Mathf.Cos(rad) * r);

                if (b.Missile)
                {
                    GUI.color = b.Locked ? HudRed : HudAmber;
                    DrawDiamond(p, 6f);
                }
                else
                {
                    GUI.color = b.Locked ? HudAmber : HudGreen;
                    DrawBox(p, 5f);
                }
            }

            GUI.color = prev;
            GUI.Label(new Rect(center.x - 110f, center.y + radius + 2f, 220f, 16f),
                AircraftRwrService.DisplayName, _rwrLabel);

            int missiles = 0;
            int radars = 0;
            for (int i = 0; i < Blips.Count; i++)
            {
                if (Blips[i].Missile)
                    missiles++;
                else
                    radars++;
            }
            if (missiles > 0 || radars > 0)
            {
                string info = (missiles > 0 ? ("M" + missiles + " ") : string.Empty)
                    + (radars > 0 ? ("R" + radars) : string.Empty);
                GUI.Label(new Rect(center.x - 40f, center.y - radius - 18f, 80f, 16f), info.Trim(), _rwrLabel);
            }
        }

        private static void BindRadar(Aircraft ac)
        {
            if (object.ReferenceEquals(ac, _boundAc))
                return;
            UnbindRadar();
            _boundAc = ac;
            try { ac.onRadarWarning += OnRadarWarning; }
            catch { }
        }

        private static void UnbindRadar()
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
                AircraftRwrService.Upsert(Blips, e.emitter.GetInstanceID(), bearing, rangeNorm, false, e.isTarget, 3.5f);
            }
            catch { }
        }

        private static void RefreshMissileBlips(Aircraft ac)
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
                    float rangeNorm = AircraftRwrService.RangeNormFromDistance(dist, 8000f);
                    float bearing = AircraftRwrService.RelativeBearing(ac, m.transform.position);
                    AircraftRwrService.Upsert(Blips, m.GetInstanceID(), bearing, rangeNorm, true, true, 1.2f);
                }
            }
            catch { }
        }

        private static void PruneBlips()
        {
            AircraftRwrService.Prune(Blips);
        }

        private static Aircraft ResolveLocalAircraft()
        {
            try
            {
                Aircraft ac;
                if (GameManager.GetLocalAircraft(out ac) && ac != null && Plugin.IsRuntimeInstance(ac))
                    return ac;
            }
            catch { }
            return Plugin.ResolveGuiAircraft();
        }

        private static void EnsureStyles()
        {
            if (_stripStyle == null)
            {
                _stripStyle = new GUIStyle(GUI.skin.label);
                _stripStyle.fontSize = 15;
                _stripStyle.fontStyle = FontStyle.Bold;
                _stripStyle.alignment = TextAnchor.MiddleLeft;
                _stripStyle.normal.textColor = HudGreen;
            }
            if (_stripSmall == null)
            {
                _stripSmall = new GUIStyle(GUI.skin.label);
                _stripSmall.fontSize = 11;
                _stripSmall.alignment = TextAnchor.MiddleLeft;
                _stripSmall.normal.textColor = new Color(0.55f, 0.9f, 0.65f, 0.85f);
            }
            if (_rwrLabel == null)
            {
                _rwrLabel = new GUIStyle(GUI.skin.label);
                _rwrLabel.fontSize = 11;
                _rwrLabel.fontStyle = FontStyle.Bold;
                _rwrLabel.alignment = TextAnchor.MiddleCenter;
                _rwrLabel.wordWrap = false;
                _rwrLabel.clipping = TextClipping.Overflow;
                _rwrLabel.normal.textColor = HudGreen;
            }
        }

        private static void DrawCircle(Vector2 c, float r, float thickness, int segments)
        {
            for (int i = 0; i < segments; i++)
            {
                float a0 = (i / (float)segments) * Mathf.PI * 2f;
                float a1 = ((i + 1) / (float)segments) * Mathf.PI * 2f;
                Vector2 p0 = new Vector2(c.x + Mathf.Sin(a0) * r, c.y - Mathf.Cos(a0) * r);
                Vector2 p1 = new Vector2(c.x + Mathf.Sin(a1) * r, c.y - Mathf.Cos(a1) * r);
                DrawLine(p0, p1, thickness);
            }
        }

        private static void DrawFilledCircle(Vector2 c, float r, int segments)
        {
            // Reuse WarThunderRwrHud soft disc texture (single DrawTexture).
            float d = r * 2f;
            GUI.DrawTexture(new Rect(c.x - r, c.y - r, d, d), WarThunderRwrHud.SharedDiscTex());
        }

        private static void DrawRadialTick(Vector2 c, float r, float bearingDeg, float len)
        {
            float rad = bearingDeg * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad));
            DrawLine(c + dir * (r - len), c + dir * r, 2f);
        }

        private static void DrawLine(Vector2 a, Vector2 b, float thickness)
        {
            UiScaleService.DrawLine(a, b, thickness);
        }

        private static void DrawBox(Vector2 p, float s)
        {
            GUI.DrawTexture(new Rect(p.x - s * 0.5f, p.y - s * 0.5f, s, s), Texture2D.whiteTexture);
        }

        private static void DrawDiamond(Vector2 p, float s)
        {
            UiScaleService.DrawRotatedQuad(p, s, 45f);
        }
    }
}
