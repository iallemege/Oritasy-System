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
    /// <summary>Optional right-side G-force meter (signed +Gz along aircraft up).</summary>
    internal static class GMeterHud
    {
        private static bool _overlayOn = true;
        private static float _peakPos = 1f;
        private static float _peakNeg = 1f;
        private static float _peakHoldUntil;
        private static Aircraft _lastAc;
        private static GUIStyle _valueStyle;
        private static GUIStyle _labelStyle;
        private static readonly Color MeterGreen = new Color(0.15f, 1f, 0.35f, 0.9f);
        private static readonly Color MeterAmber = new Color(1f, 0.75f, 0.15f, 0.95f);
        private static readonly Color MeterRed = new Color(1f, 0.25f, 0.2f, 0.95f);

        internal static void Tick()
        {
            if (Plugin.GMeterKey != null
                && Plugin.GMeterKey.Value != KeyCode.None
                && Input.GetKeyDown(Plugin.GMeterKey.Value))
                _overlayOn = !_overlayOn;

            if (Plugin.ShowGMeter == null || !Plugin.ShowGMeter.Value || !_overlayOn)
                return;

            Aircraft ac = ResolveLocalAircraft();
            if (ac == null)
                return;

            if (!object.ReferenceEquals(ac, _lastAc))
            {
                _lastAc = ac;
                _peakPos = 1f;
                _peakNeg = 1f;
                _peakHoldUntil = 0f;
            }

            float g = AircraftGLoadService.ReadSignedG(ac);
            if (g > _peakPos)
            {
                _peakPos = g;
                _peakHoldUntil = Time.unscaledTime + 4f;
            }
            if (g < _peakNeg)
            {
                _peakNeg = g;
                _peakHoldUntil = Time.unscaledTime + 4f;
            }
            if (Time.unscaledTime > _peakHoldUntil && Mathf.Abs(g - 1f) < 0.35f)
            {
                _peakPos = Mathf.Lerp(_peakPos, Mathf.Max(1f, g), Time.unscaledDeltaTime * 0.35f);
                _peakNeg = Mathf.Lerp(_peakNeg, Mathf.Min(1f, g), Time.unscaledDeltaTime * 0.35f);
            }
        }

        internal static void Draw()
        {
            if (!Plugin.AllowThirdPersonUi)
                return;
            if (Plugin.ShowGMeter == null || !Plugin.ShowGMeter.Value || !_overlayOn)
                return;
            if (Event.current != null && Event.current.type != EventType.Repaint)
                return;

            Aircraft ac = ResolveLocalAircraft();
            if (ac == null)
                return;

            EnsureStyles();

            float g = AircraftGLoadService.ReadSignedG(ac);
            float limPos = AircraftGLoadService.ResolvePositiveLimit(ac);
            float scaleMin = Mathf.Min(-4f, -limPos * 0.4f);
            float scaleMax = Mathf.Max(limPos + 1f, 10f);

            float pad = Mathf.Max(8f, UiScaleService.Height * 0.012f);
            float meterH = UiScaleService.Height * 0.28f;
            float barW = 14f;
            float frameW = 72f;
            // Far right: overlap the lower-right damage silhouette; chips stay top-right.
            float x = UiScaleService.Width - pad - frameW;
            if (x < pad)
                x = pad;
            float y = ResolveMeterTopY(meterH);
            Rect frame = new Rect(x - 4f, y - 22f, frameW + 8f, meterH + 44f);
            Rect track = new Rect(x + frameW - barW - 10f, y, barW, meterH);

            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(frame, Texture2D.whiteTexture);
            GUI.color = MeterGreen;
            GUI.DrawTexture(new Rect(frame.x, frame.y, frame.width, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(frame.x, frame.yMax - 2f, frame.width, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(frame.x, frame.y, 2f, frame.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(frame.xMax - 2f, frame.y, 2f, frame.height), Texture2D.whiteTexture);

            GUI.color = new Color(1f, 1f, 1f, 0.12f);
            GUI.DrawTexture(track, Texture2D.whiteTexture);

            // Fill from 1G baseline toward current G
            float y1 = GToY(1f, scaleMin, scaleMax, y, meterH);
            float yg = GToY(g, scaleMin, scaleMax, y, meterH);
            float fillTop = Mathf.Min(y1, yg);
            float fillH = Mathf.Abs(yg - y1);
            if (fillH < 2f)
                fillH = 2f;
            GUI.color = ColorForG(g, limPos);
            GUI.DrawTexture(new Rect(track.x, fillTop, track.width, fillH), Texture2D.whiteTexture);

            // Peak marks
            DrawTick(track.x - 6f, GToY(_peakPos, scaleMin, scaleMax, y, meterH), track.width + 12f, MeterAmber);
            if (_peakNeg < 0.85f)
                DrawTick(track.x - 6f, GToY(_peakNeg, scaleMin, scaleMax, y, meterH), track.width + 12f, MeterAmber);

            // Scale ticks
            int gStart = Mathf.CeilToInt(scaleMin);
            int gEnd = Mathf.FloorToInt(scaleMax);
            for (int gi = gStart; gi <= gEnd; gi++)
            {
                float ty = GToY(gi, scaleMin, scaleMax, y, meterH);
                bool major = gi % 2 == 0 || gi == 1;
                GUI.color = gi == 1
                    ? new Color(1f, 1f, 1f, 0.75f)
                    : new Color(1f, 1f, 1f, major ? 0.35f : 0.18f);
                float tw = major ? 10f : 6f;
                GUI.DrawTexture(new Rect(track.x - tw - 2f, ty, tw, 1f), Texture2D.whiteTexture);
                if (major)
                    GUI.Label(new Rect(x + 2f, ty - 8f, 36f, 16f), gi.ToString("+0;-0;0"), _labelStyle);
            }

            // Needle
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(track.x - 3f, yg - 1f, track.width + 6f, 3f), Texture2D.whiteTexture);

            GUI.color = prev;
            string sign = g >= 0f ? "+" : string.Empty;
            GUI.Label(new Rect(x, y - 20f, frameW, 18f), "G  " + sign + g.ToString("0.0"), _valueStyle);
            GUI.Label(new Rect(x, y + meterH + 4f, frameW, 16f),
                "Pk +" + _peakPos.ToString("0.0") + " / " + _peakNeg.ToString("0.0"), _labelStyle);
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

        private static float ResolveMeterTopY(float meterH)
        {
            // Sit over the lower-right damage silhouette (covering it is intended).
            float y = UiScaleService.Height - meterH - 36f;
            if (y < 8f)
                y = 8f;
            return y;
        }

        private static float GToY(float g, float min, float max, float top, float height)
        {
            float t = Mathf.InverseLerp(min, max, g);
            return top + height - t * height;
        }

        private static Color ColorForG(float g, float limPos)
        {
            float abs = Mathf.Abs(g);
            if (abs >= limPos * 0.95f || g <= -3.5f)
                return MeterRed;
            if (abs >= limPos * 0.75f || g <= -2.5f)
                return MeterAmber;
            return MeterGreen;
        }

        private static void DrawTick(float x, float y, float w, Color c)
        {
            Color prev = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(x, y, w, 2f), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        private static void EnsureStyles()
        {
            if (_valueStyle == null)
            {
                _valueStyle = new GUIStyle(GUI.skin.label);
                _valueStyle.alignment = TextAnchor.MiddleLeft;
                _valueStyle.fontSize = 13;
                _valueStyle.fontStyle = FontStyle.Bold;
                _valueStyle.normal.textColor = MeterGreen;
            }
            if (_labelStyle == null)
            {
                _labelStyle = new GUIStyle(GUI.skin.label);
                _labelStyle.alignment = TextAnchor.MiddleLeft;
                _labelStyle.fontSize = 11;
                _labelStyle.normal.textColor = new Color(0.7f, 0.95f, 0.75f, 0.85f);
            }
        }
    }
}
