using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Realistic PPI radar overlay (waveform + clutter) shown only while vanilla TacScreen radar is on.
    /// Repaint-only drawing.
    /// </summary>
    internal static class RadarMfdOverlay
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> RangeKm;
        internal static ConfigEntry<float> NormX;
        internal static ConfigEntry<float> NormY;
        internal static ConfigEntry<float> SizeFrac;
        internal static ConfigEntry<float> ClutterIntensity;
        internal static ConfigEntry<bool> ShowAscope;
        internal static ConfigEntry<float> SweepRpm;
        internal static ConfigEntry<float> BeamWidthDeg;
        internal static ConfigEntry<float> PersistenceSec;

        private static bool _bound;
        private static FieldInfo _cockpitTacField;
        private static FieldInfo _radarOnField;
        private static FieldInfo _mfdActiveLeft;
        private static FieldInfo _mfdActiveRight;
        private static FieldInfo _mfdLeftScreens;
        private static FieldInfo _mfdRightScreens;
        private static FieldInfo _tacScreenRenderField;
        private static FieldInfo _scanLineField;
        private static FieldInfo _radarConeField;
        private static FieldInfo _radarCenterField;
        private static FieldInfo _canvasField;
        private static bool _fieldsResolved;
        private static float _nextDetectAt;
        private static bool _radarDisplaying;
        private static bool _loggedRadarOnce;
        private static bool _hasScreenRect;
        private static Rect _screenRect;
        private static float _sweepAngle;
        private static float _prevSweepAngle;
        private static Aircraft _cachedAc;
        private static float _cachedAgl;
        private static readonly List<Contact> _contacts = new List<Contact>(48);
        private static readonly List<PersistHit> _persist = new List<PersistHit>(256);
        private static readonly List<ClutterSpeck> _clutter = new List<ClutterSpeck>(180);
        private static float _nextClutterAt;
        private static int _clutterSeed;
        private static Texture2D _white;
        private static GUIStyle _label;
        private static GUIStyle _tiny;

        private static int PersistCap
        {
            get { return PerfMode.PersistCap(); }
        }
        private static int ClutterCap
        {
            get { return PerfMode.ClutterCap(); }
        }

        private struct Contact
        {
            public float BearingDeg;
            public float RangeM;
            public float Spd;
            public bool Friendly;
            public bool FromRadarList;
            public Vector3 VelFlat;
            public float Strength; // 0-1 return strength
        }

        private struct PersistHit
        {
            public float BearingDeg;
            public float RangeNorm;
            public float Birth;
            public float Strength;
            public bool Friendly;
            public bool IsContact;
            public bool IsClutter;
        }

        private struct ClutterSpeck
        {
            public float BearingDeg;
            public float RangeNorm;
            public float Strength;
            public float Phase;
        }

        internal static void Bind(ConfigFile config)
        {
            if (config == null || _bound)
                return;
            _bound = true;
            Enabled = config.Bind("RadarMfd", "Enabled", true,
                "Overlay a realistic PPI radar on the vanilla aircraft radar MFD when it is displaying.");
            RangeKm = config.Bind("RadarMfd", "RangeKm", 40f,
                "Display range scale in kilometers.");
            NormX = config.Bind("RadarMfd", "NormX", 0.18f,
                "Overlay center X as fraction of screen width (0-1).");
            NormY = config.Bind("RadarMfd", "NormY", 0.78f,
                "Overlay center Y as fraction of screen height (0-1).");
            SizeFrac = config.Bind("RadarMfd", "SizeFrac", 0.22f,
                "Overlay diameter as fraction of screen height.");
            ClutterIntensity = config.Bind("RadarMfd", "ClutterIntensity", 0.65f,
                "Ground/sea/weather clutter intensity (0-1.5).");
            ShowAscope = config.Bind("RadarMfd", "ShowAscope", true,
                "Show a small A-scope (amplitude vs range) strip beside the PPI.");
            SweepRpm = config.Bind("RadarMfd", "SweepRpm", 24f,
                "Antenna sweep rate (RPM).");
            BeamWidthDeg = config.Bind("RadarMfd", "BeamWidthDeg", 4.5f,
                "Main beam width in degrees (paint / highlight).");
            PersistenceSec = config.Bind("RadarMfd", "PersistenceSec", 2.4f,
                "Scan-line persistence / phosphor decay time (seconds).");
        }

        internal static void Tick()
        {
            if (Enabled == null || !Enabled.Value)
            {
                if (_persist.Count > 0)
                    _persist.Clear();
                _radarDisplaying = false;
                return;
            }

            float now = Time.unscaledTime;
            float detectEvery = PerfMode.IsLow ? 0.55f : 0.2f;
            if (now >= _nextDetectAt)
            {
                _nextDetectAt = now + detectEvery;
                DetectRadarDisplay();
            }

            if (!_radarDisplaying)
            {
                if (_persist.Count > 0)
                    _persist.Clear();
                return;
            }

            float rpm = SweepRpm != null ? SweepRpm.Value : 24f;
            rpm = RadarMfdMathService.ClampSweepRpm(rpm, PerfMode.IsLow);
            float degPerSec = RadarMfdMathService.DegPerSecFromRpm(rpm);
            _prevSweepAngle = _sweepAngle;
            _sweepAngle = RadarMfdMathService.AdvanceSweep(
                _sweepAngle, degPerSec, Time.unscaledDeltaTime);

            RefreshContacts();
            UpdateClutter(now);
            PaintSweepHits(now);
            DecayPersist(now);
        }

        internal static void Draw()
        {
            if (Enabled == null || !Enabled.Value || !_radarDisplaying)
                return;
            Event e = Event.current;
            if (e == null || e.type != EventType.Repaint)
                return;
            EnsureStyles();

            RadarMfdMathService.Layout layout = RadarMfdMathService.ComputeLayout(
                UiScaleService.Width,
                UiScaleService.Height,
                SizeFrac != null ? SizeFrac.Value : 0.22f,
                NormX != null ? NormX.Value : 0.18f,
                NormY != null ? NormY.Value : 0.78f,
                _hasScreenRect,
                _screenRect);
            Vector2 center = layout.Center;
            float radius = layout.Radius;
            float rangeM = RadarMfdMathService.RangeMeters(RangeKm != null ? RangeKm.Value : 40f);
            float beamW = RadarMfdMathService.ClampBeamWidthDeg(
                BeamWidthDeg != null ? BeamWidthDeg.Value : 4.5f);

            Color prev = GUI.color;
            float now = Time.unscaledTime;
            float persist = PersistenceSec != null ? PersistenceSec.Value : 2.4f;
            if (persist < 0.4f) persist = 0.4f;

            // CRT face
            GUI.color = new Color(0.015f, 0.05f, 0.03f, 0.94f);
            FillCircle(center, radius + 5f);

            // Faint range rings (fewer segments on Low)
            GUI.color = new Color(0.18f, 0.7f, 0.3f, 0.4f);
            StrokeCircle(center, radius, 1.8f, PerfMode.RadarRingSegments(36));
            GUI.color = new Color(0.12f, 0.55f, 0.22f, 0.28f);
            StrokeCircle(center, radius * 0.75f, 1.1f, PerfMode.RadarRingSegments(28));
            if (!PerfMode.IsLow)
            {
                StrokeCircle(center, radius * 0.5f, 1f, PerfMode.RadarRingSegments(24));
                StrokeCircle(center, radius * 0.25f, 1f, PerfMode.RadarRingSegments(20));
            }
            else
                StrokeCircle(center, radius * 0.5f, 1f, PerfMode.RadarRingSegments(16));

            GUI.color = new Color(0.2f, 0.65f, 0.3f, 0.28f);
            GUI.DrawTexture(new Rect(center.x - 1f, center.y - radius, 2f, radius * 2f), _white);
            GUI.DrawTexture(new Rect(center.x - radius, center.y - 1f, radius * 2f, 2f), _white);

            DrawPersistence(center, radius, now, persist);
            DrawLiveClutter(center, radius, now);
            DrawSweepBeam(center, radius, _sweepAngle, beamW);
            DrawLiveContacts(center, radius, rangeM, beamW, now);

            // Ownship
            GUI.color = new Color(0.45f, 1f, 0.55f, 0.95f);
            GUI.DrawTexture(new Rect(center.x - 2f, center.y - 9f, 4f, 16f), _white);
            GUI.DrawTexture(new Rect(center.x - 7f, center.y - 1f, 14f, 3f), _white);

            if (PerfMode.RadarAscopeAllowed()
                && (ShowAscope == null || ShowAscope.Value))
                DrawAscope(center, radius, rangeM, beamW, now);

            GUI.color = new Color(0.55f, 0.95f, 0.65f, 0.9f);
            float clutter = ClutterIntensity != null ? ClutterIntensity.Value : 0.65f;
            GUI.Label(new Rect(center.x - radius, center.y + radius + 2f, radius * 2f, 16f),
                "RDR  " + GameUnitDisplayService.Distance(rangeM) + "  ·  "
                + _contacts.Count + " ctc  ·  CLT " + clutter.ToString("0.0"),
                _label);
            GUI.color = prev;
        }

        // ---------- waveform / clutter simulation ----------


        private static void UpdateClutter(float now)
        {
            if (now < _nextClutterAt)
                return;
            _nextClutterAt = now + 0.12f;
            _clutter.Clear();
            _clutterSeed++;

            float intensity = ClutterIntensity != null ? ClutterIntensity.Value : 0.65f;
            intensity *= PerfMode.ClutterMul();
            if (intensity <= 0.01f)
                return;
            intensity = Mathf.Clamp(intensity, 0f, 1.5f);

            float agl = _cachedAgl;
            float aglFactor = RadarMfdMathService.ClutterAglFactor(agl);
            float surfaceBump = RadarMfdMathService.ClutterSurfaceBump(agl);

            int cap = ClutterCap;
            int count = RadarMfdMathService.ClutterCount(cap, intensity, aglFactor, surfaceBump, PerfMode.IsLow);
            if (PerfMode.IsLow)
                _nextClutterAt = now + 0.28f;

            System.Random rng = new System.Random(_clutterSeed * 9973 + (int)(now * 10f));
            for (int i = 0; i < count; i++)
            {
                // Bias toward near-center (ground returns) via squared range pick
                float u = (float)rng.NextDouble();
                float rangeNorm = u * u; // denser near center
                // Weather-ish band: occasional mid-range patches
                if (rng.NextDouble() < 0.12)
                    rangeNorm = 0.35f + 0.45f * (float)rng.NextDouble();

                float bearing = (float)(rng.NextDouble() * 360.0);
                float str = RadarMfdMathService.ClutterStrength(
                    (float)rng.NextDouble(), intensity, aglFactor, rangeNorm, agl);

                ClutterSpeck s;
                s.BearingDeg = bearing;
                s.RangeNorm = Mathf.Clamp01(rangeNorm);
                s.Strength = str;
                s.Phase = (float)rng.NextDouble() * 6.28f;
                _clutter.Add(s);
            }
        }

        private static void PaintSweepHits(float now)
        {
            float beamW = RadarMfdMathService.ClampBeamWidthDeg(
                BeamWidthDeg != null ? BeamWidthDeg.Value : 4.5f);
            float half = beamW * 0.5f;

            // Angular sector covered this frame (handle wrap)
            float a0 = _prevSweepAngle;
            float a1 = _sweepAngle;
            float delta = Mathf.DeltaAngle(a0, a1);
            if (Mathf.Abs(delta) < 0.01f)
                delta = 0.5f;

            // Paint contacts under the beam as pulse returns
            for (int i = 0; i < _contacts.Count; i++)
            {
                Contact c = _contacts[i];
                float d = Mathf.Abs(Mathf.DeltaAngle(_sweepAngle, c.BearingDeg));
                if (d > half + 1.5f)
                    continue;
                float beamGain = RadarMfdMathService.BeamGain(d, half + 1.5f);
                float rangeM = RadarMfdMathService.RangeMeters(RangeKm != null ? RangeKm.Value : 40f);
                float rangeNorm = Mathf.Clamp01(c.RangeM / rangeM);
                float falloff = RadarMfdMathService.RangeFalloff(rangeNorm);
                float str = Mathf.Clamp01(c.Strength * beamGain * falloff);

                // Pulse / range-gate: paint a short radial blip (main + nearby gates)
                AddPersist(c.BearingDeg, rangeNorm, now, str, c.Friendly, true, false);
                AddPersist(c.BearingDeg, Mathf.Clamp01(rangeNorm - 0.012f), now, str * 0.45f, c.Friendly, true, false);
                AddPersist(c.BearingDeg, Mathf.Clamp01(rangeNorm + 0.012f), now, str * 0.35f, c.Friendly, true, false);
            }

            // Paint clutter under beam (scan-line persistence)
            float intensity = ClutterIntensity != null ? ClutterIntensity.Value : 0.65f;
            if (intensity > 0.01f)
            {
                for (int i = 0; i < _clutter.Count; i++)
                {
                    ClutterSpeck s = _clutter[i];
                    float d = Mathf.Abs(Mathf.DeltaAngle(_sweepAngle, s.BearingDeg));
                    if (d > half + 2f)
                        continue;
                    float beamGain = 1f - d / (half + 2f);
                    float str = s.Strength * beamGain;
                    if (str < 0.04f)
                        continue;
                    AddPersist(s.BearingDeg, s.RangeNorm, now, str, false, false, true);
                }
            }
        }

        private static void AddPersist(float bearing, float rangeNorm, float now, float strength,
            bool friendly, bool isContact, bool isClutter)
        {
            if (strength < 0.03f)
                return;
            PersistHit h;
            h.BearingDeg = bearing;
            h.RangeNorm = rangeNorm;
            h.Birth = now;
            h.Strength = strength;
            h.Friendly = friendly;
            h.IsContact = isContact;
            h.IsClutter = isClutter;
            _persist.Add(h);
            while (_persist.Count > PersistCap)
                _persist.RemoveAt(0);
        }

        private static void DecayPersist(float now)
        {
            float persist = PersistenceSec != null ? PersistenceSec.Value : 2.4f;
            if (persist < 0.4f) persist = 0.4f;
            for (int i = _persist.Count - 1; i >= 0; i--)
            {
                if (now - _persist[i].Birth > persist)
                    _persist.RemoveAt(i);
            }
        }

        private static void DrawPersistence(Vector2 center, float radius, float now, float persist)
        {
            for (int i = 0; i < _persist.Count; i++)
            {
                PersistHit h = _persist[i];
                float age = (now - h.Birth) / persist;
                if (age < 0f || age > 1f)
                    continue;
                float decay = RadarMfdMathService.PhosphorDecay(age);
                float a = h.Strength * decay;
                if (a < 0.02f)
                    continue;

                Vector2 p = RadarMfdMathService.PolarToScreen(
                    center, radius, h.BearingDeg, h.RangeNorm);

                if (h.IsContact)
                {
                    GUI.color = h.Friendly
                        ? new Color(0.35f, 0.9f, 1f, a)
                        : new Color(1f, 0.4f, 0.28f, a);
                    float mark = 3.5f + 2.5f * a;
                    GUI.DrawTexture(new Rect(p.x - mark * 0.5f, p.y - mark * 0.5f, mark, mark), _white);
                    // Short radial pulse streak
                    Vector2 inward = (center - p).normalized;
                    DrawLine(p, p + inward * (4f + 6f * a), 1.2f);
                }
                else
                {
                    // Clutter phosphor — green speckles
                    GUI.color = new Color(0.25f, 0.85f, 0.4f, a * 0.7f);
                    float mark = 1.5f + 2f * a;
                    GUI.DrawTexture(new Rect(p.x - mark * 0.5f, p.y - mark * 0.5f, mark, mark), _white);
                }
            }
        }

        private static void DrawLiveClutter(Vector2 center, float radius, float now)
        {
            float intensity = ClutterIntensity != null ? ClutterIntensity.Value : 0.65f;
            if (intensity <= 0.01f)
                return;
            for (int i = 0; i < _clutter.Count; i++)
            {
                ClutterSpeck s = _clutter[i];
                // Twinkle
                float tw = 0.65f + 0.35f * Mathf.Sin(now * 9f + s.Phase);
                float a = s.Strength * tw * 0.55f;
                if (a < 0.03f)
                    continue;
                float rad = s.BearingDeg * Mathf.Deg2Rad;
                float r = s.RangeNorm * radius;
                Vector2 p = new Vector2(
                    center.x + Mathf.Sin(rad) * r,
                    center.y - Mathf.Cos(rad) * r);
                GUI.color = new Color(0.2f, 0.75f, 0.35f, a);
                float mark = 1.2f + 1.8f * a;
                GUI.DrawTexture(new Rect(p.x - mark * 0.5f, p.y - mark * 0.5f, mark, mark), _white);
            }
        }

        private static void DrawSweepBeam(Vector2 center, float radius, float angleDeg, float beamW)
        {
            float half = beamW * 0.5f;
            // Soft trailing fan (persistence of beam glow)
            for (int i = 12; i >= 0; i--)
            {
                float ang = angleDeg - i * (half * 0.55f);
                float t = 1f - i / 12f;
                float alpha = 0.08f + 0.42f * t * t;
                GUI.color = new Color(0.3f, 1f, 0.45f, alpha);
                float ar = ang * Mathf.Deg2Rad;
                Vector2 tip = new Vector2(
                    center.x + Mathf.Sin(ar) * radius,
                    center.y - Mathf.Cos(ar) * radius);
                DrawLine(center, tip, i == 0 ? 2.4f : 1.2f);
            }

            // Main beam bright edge
            float a0 = (angleDeg - half) * Mathf.Deg2Rad;
            float a1 = (angleDeg + half) * Mathf.Deg2Rad;
            GUI.color = new Color(0.45f, 1f, 0.55f, 0.55f);
            Vector2 t0 = new Vector2(center.x + Mathf.Sin(a0) * radius, center.y - Mathf.Cos(a0) * radius);
            Vector2 t1 = new Vector2(center.x + Mathf.Sin(a1) * radius, center.y - Mathf.Cos(a1) * radius);
            DrawLine(center, t0, 1.2f);
            DrawLine(center, t1, 1.2f);

            // Range-gate tick marks along the beam (pulse waveform feel)
            GUI.color = new Color(0.4f, 1f, 0.5f, 0.35f);
            float arMain = angleDeg * Mathf.Deg2Rad;
            for (int g = 1; g <= 8; g++)
            {
                float rn = g / 8f;
                Vector2 p = new Vector2(
                    center.x + Mathf.Sin(arMain) * radius * rn,
                    center.y - Mathf.Cos(arMain) * radius * rn);
                float tick = 3f + (g % 2 == 0 ? 2f : 0f);
                Vector2 perp = new Vector2(Mathf.Cos(arMain), Mathf.Sin(arMain));
                DrawLine(p - perp * tick, p + perp * tick, 1f);
            }
        }

        private static void DrawLiveContacts(Vector2 center, float radius, float rangeM, float beamW, float now)
        {
            float half = beamW * 0.5f + 1.5f;
            for (int i = 0; i < _contacts.Count; i++)
            {
                Contact c = _contacts[i];
                float rn = Mathf.Clamp01(c.RangeM / rangeM);
                float rad = c.BearingDeg * Mathf.Deg2Rad;
                float r = rn * radius;
                Vector2 p = new Vector2(
                    center.x + Mathf.Sin(rad) * r,
                    center.y - Mathf.Cos(rad) * r);

                float dBeam = Mathf.Abs(Mathf.DeltaAngle(_sweepAngle, c.BearingDeg));
                bool underBeam = dBeam <= half;
                float falloff = 1f / (0.35f + rn * 1.4f);
                float a = Mathf.Clamp01(c.Strength * falloff * (underBeam ? 1f : 0.35f));

                GUI.color = c.Friendly
                    ? new Color(0.4f, 0.95f, 1f, 0.55f + 0.45f * a)
                    : new Color(1f, 0.38f, 0.28f, 0.55f + 0.45f * a);

                float mark = underBeam ? (5f + 3f * a) : 3.5f;
                GUI.DrawTexture(new Rect(p.x - mark * 0.5f, p.y - mark * 0.5f, mark, mark), _white);

                if (underBeam)
                {
                    // Bright pulse ring when illuminated
                    GUI.color = new Color(GUI.color.r, GUI.color.g, GUI.color.b, 0.35f + 0.4f * a);
                    StrokeCircle(p, mark + 2f, 1.2f, 10);
                }

                // Velocity / aspect tick
                if (c.VelFlat.sqrMagnitude > 1f)
                {
                    Vector2 vdir = new Vector2(c.VelFlat.x, -c.VelFlat.z);
                    if (vdir.sqrMagnitude > 0.01f)
                    {
                        vdir.Normalize();
                        GUI.color = new Color(GUI.color.r, GUI.color.g, GUI.color.b, 0.7f);
                        DrawLine(p, p + vdir * (8f + Mathf.Clamp01(c.Spd / 300f) * 8f), 1.4f);
                    }
                }
            }
        }

        private static void DrawAscope(Vector2 center, float radius, float rangeM, float beamW, float now)
        {
            float ax = center.x + radius + 10f;
            float ay = center.y - radius;
            float aw = 54f;
            float ah = radius * 2f;

            GUI.color = new Color(0.02f, 0.06f, 0.04f, 0.88f);
            GUI.DrawTexture(new Rect(ax - 2f, ay - 2f, aw + 4f, ah + 4f), _white);
            GUI.color = new Color(0.2f, 0.7f, 0.35f, 0.5f);
            GUI.DrawTexture(new Rect(ax, ay, 1f, ah), _white);
            GUI.DrawTexture(new Rect(ax, ay + ah - 1f, aw, 1f), _white);

            GUI.color = new Color(0.55f, 0.95f, 0.65f, 0.85f);
            GUI.Label(new Rect(ax, ay - 14f, aw, 14f), "A-SCP", _tiny);

            float half = beamW * 0.5f + 2f;
            // Baseline noise / clutter waveform along range
            float intensity = ClutterIntensity != null ? ClutterIntensity.Value : 0.65f;
            int bins = 48;
            float[] amp = new float[bins];
            for (int i = 0; i < bins; i++)
            {
                float rn = (i + 0.5f) / bins;
                float noise = 0.04f + 0.08f * intensity * (1f - rn)
                    * (0.6f + 0.4f * Mathf.Sin(now * 17f + i * 0.7f + _cachedAgl * 0.01f));
                // Near-center ground clutter bump
                if (rn < 0.25f)
                    noise += 0.12f * intensity * (1f - rn * 4f) * Mathf.Clamp01(1.2f - _cachedAgl / 800f);
                amp[i] = noise;
            }

            // Add contact returns on current bearing
            for (int i = 0; i < _contacts.Count; i++)
            {
                Contact c = _contacts[i];
                if (Mathf.Abs(Mathf.DeltaAngle(_sweepAngle, c.BearingDeg)) > half)
                    continue;
                float rn = Mathf.Clamp01(c.RangeM / rangeM);
                int bin = Mathf.Clamp(Mathf.FloorToInt(rn * bins), 0, bins - 1);
                float falloff = 1f / (0.35f + rn * 1.4f);
                float peak = Mathf.Clamp01(c.Strength * falloff);
                amp[bin] = Mathf.Max(amp[bin], 0.25f + 0.75f * peak);
                if (bin > 0) amp[bin - 1] = Mathf.Max(amp[bin - 1], amp[bin] * 0.45f);
                if (bin < bins - 1) amp[bin + 1] = Mathf.Max(amp[bin + 1], amp[bin] * 0.45f);
            }

            // Draw waveform (range vertical, amplitude horizontal)
            Vector2 prev = new Vector2(ax + 2f, ay + ah);
            for (int i = 0; i < bins; i++)
            {
                float y = ay + ah - (i + 0.5f) / bins * ah;
                float x = ax + 2f + Mathf.Clamp01(amp[i]) * (aw - 6f);
                Vector2 p = new Vector2(x, y);
                GUI.color = new Color(0.4f, 1f, 0.5f, 0.75f);
                DrawLine(prev, p, 1.3f);
                prev = p;
            }

            // Sweep cursor on A-scope (range gate marker at mid)
            GUI.color = new Color(0.9f, 1f, 0.5f, 0.35f);
            float midY = ay + ah * 0.5f;
            GUI.DrawTexture(new Rect(ax, midY, aw, 1f), _white);
        }

        // ---------- detection / contacts ----------


        private static void DetectRadarDisplay()
        {
            _radarDisplaying = false;
            _hasScreenRect = false;
            Aircraft ac = null;
            try
            {
                if (!GameManager.GetLocalAircraft(out ac) || ac == null)
                {
                    _cachedAc = null;
                    _cachedAgl = 9999f;
                    return;
                }
            }
            catch
            {
                _cachedAc = null;
                return;
            }
            _cachedAc = ac;
            try { _cachedAgl = ac.radarAlt; }
            catch
            {
                try { _cachedAgl = ac.transform.position.y; }
                catch { _cachedAgl = 9999f; }
            }
            ResolveFields();

            Cockpit cockpit = null;
            try { cockpit = ac.GetComponentInChildren<Cockpit>(true); }
            catch { }

            bool tacRadarOn = false;
            bool tacWidgets = false;
            TacScreen ts = null;
            try
            {
                if (cockpit != null && _cockpitTacField != null)
                    ts = _cockpitTacField.GetValue(cockpit) as TacScreen;
                if (ts == null)
                {
                    TacScreen[] screens = ac.GetComponentsInChildren<TacScreen>(true);
                    if (screens != null)
                    {
                        for (int i = 0; i < screens.Length; i++)
                        {
                            if (screens[i] != null)
                            {
                                ts = screens[i];
                                break;
                            }
                        }
                    }
                }
                if (ts != null)
                {
                    tacRadarOn = ReadRadarOn(ts);
                    if (!tacRadarOn)
                        tacWidgets = TacWidgetsLookRadar(ts);
                }
            }
            catch { }

            bool virtualMfdRadar = false;
            MFDScreen radarPage = null;
            string mfdName = "";
            try
            {
                VirtualMFD mfd = ac.GetComponentInChildren<VirtualMFD>(true);
                if (mfd != null && TryGetMfdRadarPage(mfd, out radarPage))
                {
                    virtualMfdRadar = true;
                    mfdName = SafeShortName(radarPage);
                }
            }
            catch { }

            bool radarActivated = false;
            bool mfdLooksRadarLoose = false;
            MFDScreen anyActive = null;
            bool tacRenderVisible = false;
            try
            {
                TargetDetector radar = null;
                try { radar = ac.radar; }
                catch { }
                if (radar != null)
                {
                    try { radarActivated = radar.activated; }
                    catch { radarActivated = false; }
                }
                if (radarActivated)
                {
                    VirtualMFD mfd = ac.GetComponentInChildren<VirtualMFD>(true);
                    anyActive = FirstActiveMfdScreen(mfd);
                    mfdLooksRadarLoose = anyActive != null && ScreenLooksRadarLoose(anyActive);
                    tacRenderVisible = cockpit != null && TacRenderVisible(cockpit);
                }
            }
            catch { }

            RadarMfdDetectGateService.Path path = RadarMfdDetectGateService.Resolve(
                tacRadarOn, tacWidgets, virtualMfdRadar, radarActivated,
                mfdLooksRadarLoose, tacRenderVisible);
            if (path == RadarMfdDetectGateService.Path.Off)
                return;

            string reason = RadarMfdDetectGateService.ReasonLabel(path, mfdName);
            if (path == RadarMfdDetectGateService.Path.TacRadarOn
                || path == RadarMfdDetectGateService.Path.TacWidgets)
                TryCaptureTacRect(ts, cockpit);
            else if (path == RadarMfdDetectGateService.Path.VirtualMfd)
                TryCaptureMfdRect(radarPage);
            else if (path == RadarMfdDetectGateService.Path.RadarPlusMfd)
                TryCaptureMfdRect(anyActive);
            else if (path == RadarMfdDetectGateService.Path.RadarPlusTacRender)
                TryCaptureTacRect(null, cockpit);
            MarkRadarOn(reason);
        }

        private static void MarkRadarOn(string reason)
        {
            _radarDisplaying = true;
            if (_loggedRadarOnce || Plugin.Log == null)
                return;
            _loggedRadarOnce = true;
            Plugin.Log.LogInfo("RadarMfd: radar page detected via " + reason
                + (_hasScreenRect
                    ? (" rect=" + _screenRect.x.ToString("0") + "," + _screenRect.y.ToString("0")
                        + " " + _screenRect.width.ToString("0") + "x" + _screenRect.height.ToString("0"))
                    : " rect=fallback-norm"));
        }

        private static bool ReadRadarOn(TacScreen ts)
        {
            if (ts == null || _radarOnField == null)
                return false;
            try
            {
                object v = _radarOnField.GetValue(ts);
                return v is bool && (bool)v;
            }
            catch { return false; }
        }

        private static bool TacWidgetsLookRadar(TacScreen ts)
        {
            try
            {
                if (_scanLineField != null
                    && BehaviourActive(_scanLineField.GetValue(ts) as Behaviour))
                    return true;
            }
            catch { }
            try
            {
                if (_radarConeField != null
                    && BehaviourActive(_radarConeField.GetValue(ts) as Behaviour))
                    return true;
            }
            catch { }
            try
            {
                if (_radarCenterField != null)
                {
                    Transform t = _radarCenterField.GetValue(ts) as Transform;
                    if (t != null && t.gameObject.activeInHierarchy)
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static bool BehaviourActive(Behaviour b)
        {
            return b != null && b.isActiveAndEnabled && b.gameObject.activeInHierarchy;
        }

        private static bool TacRenderVisible(Cockpit cockpit)
        {
            if (cockpit == null || _tacScreenRenderField == null)
                return false;
            try
            {
                Renderer r = _tacScreenRenderField.GetValue(cockpit) as Renderer;
                return r != null && r.enabled && r.gameObject.activeInHierarchy;
            }
            catch { return false; }
        }

        private static void TryCaptureTacRect(TacScreen ts, Cockpit cockpit)
        {
            _hasScreenRect = false;
            Camera cam = Camera.main;
            if (cam == null)
            {
                try
                {
                    Camera[] cams = UnityEngine.Object.FindObjectsOfType<Camera>();
                    if (cams != null)
                    {
                        for (int i = 0; i < cams.Length; i++)
                        {
                            if (cams[i] != null && cams[i].enabled)
                            {
                                cam = cams[i];
                                break;
                            }
                        }
                    }
                }
                catch { }
            }
            if (cam == null)
                return;

            // Prefer mesh renderer on cockpit tac screen
            try
            {
                if (cockpit != null && _tacScreenRenderField != null)
                {
                    Renderer r = _tacScreenRenderField.GetValue(cockpit) as Renderer;
                    if (r != null && WorldBoundsToGuiRect(cam, r.bounds, out _screenRect))
                    {
                        _hasScreenRect = true;
                        return;
                    }
                }
            }
            catch { }

            try
            {
                if (ts != null && _radarCenterField != null)
                {
                    Transform t = _radarCenterField.GetValue(ts) as Transform;
                    if (t != null)
                    {
                        Vector3 sp = cam.WorldToScreenPoint(t.position);
                        if (sp.z > 0.05f)
                        {
                            float sz = Mathf.Clamp(SizeFrac != null ? SizeFrac.Value : 0.22f, 0.14f, 0.4f)
                                * UiScaleService.Height;
                            float guiX = UiScaleService.FromScreenX(sp.x);
                            float guiY = UiScaleService.FromScreenYFlipped(sp.y);
                            _screenRect = new Rect(guiX - sz * 0.5f, guiY - sz * 0.5f, sz, sz);
                            _hasScreenRect = true;
                            return;
                        }
                    }
                }
            }
            catch { }

            try
            {
                if (ts != null && _canvasField != null)
                {
                    Component canvas = _canvasField.GetValue(ts) as Component;
                    if (canvas != null)
                    {
                        RectTransform rt = canvas.transform as RectTransform;
                        if (rt != null && RectTransformToGui(rt, out _screenRect))
                            _hasScreenRect = true;
                    }
                }
            }
            catch { }
        }

        private static void TryCaptureMfdRect(MFDScreen page)
        {
            _hasScreenRect = false;
            if (page == null)
                return;
            try
            {
                if (page.displayPanel != null)
                {
                    RectTransform rt = page.displayPanel.transform as RectTransform;
                    if (rt == null)
                        rt = page.displayPanel.GetComponent<RectTransform>();
                    if (rt != null && RectTransformToGui(rt, out _screenRect))
                    {
                        _hasScreenRect = true;
                        return;
                    }
                    // 3D panel — world bounds
                    Renderer r = page.displayPanel.GetComponentInChildren<Renderer>(true);
                    Camera cam = Camera.main;
                    if (r != null && cam != null
                        && WorldBoundsToGuiRect(cam, r.bounds, out _screenRect))
                    {
                        _hasScreenRect = true;
                        return;
                    }
                }
            }
            catch { }
            try
            {
                RectTransform self = page.transform as RectTransform;
                if (self != null && RectTransformToGui(self, out _screenRect))
                    _hasScreenRect = true;
            }
            catch { }
        }

        private static bool WorldBoundsToGuiRect(Camera cam, Bounds b, out Rect rect)
        {
            rect = new Rect();
            if (cam == null)
                return false;
            Vector3[] pts = new Vector3[8];
            Vector3 c = b.center;
            Vector3 e = b.extents;
            pts[0] = c + new Vector3(-e.x, -e.y, -e.z);
            pts[1] = c + new Vector3(-e.x, -e.y, e.z);
            pts[2] = c + new Vector3(-e.x, e.y, -e.z);
            pts[3] = c + new Vector3(-e.x, e.y, e.z);
            pts[4] = c + new Vector3(e.x, -e.y, -e.z);
            pts[5] = c + new Vector3(e.x, -e.y, e.z);
            pts[6] = c + new Vector3(e.x, e.y, -e.z);
            pts[7] = c + new Vector3(e.x, e.y, e.z);
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            int ok = 0;
            for (int i = 0; i < 8; i++)
            {
                Vector3 sp = cam.WorldToScreenPoint(pts[i]);
                if (sp.z <= 0.05f)
                    continue;
                float guiX = UiScaleService.FromScreenX(sp.x);
                float guiY = UiScaleService.FromScreenYFlipped(sp.y);
                if (guiX < minX) minX = guiX;
                if (guiX > maxX) maxX = guiX;
                if (guiY < minY) minY = guiY;
                if (guiY > maxY) maxY = guiY;
                ok++;
            }
            if (ok < 3)
                return false;
            float w = maxX - minX;
            float h = maxY - minY;
            if (w < 24f || h < 24f || w > UiScaleService.Width * 0.9f || h > UiScaleService.Height * 0.9f)
                return false;
            rect = new Rect(minX, minY, w, h);
            return true;
        }

        private static bool RectTransformToGui(RectTransform rt, out Rect rect)
        {
            rect = new Rect();
            if (rt == null)
                return false;
            Vector3[] corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            // Overlay canvas corners are already in screen pixels; world/camera canvases need Camera.main.
            Camera cam = null;
            try
            {
                // Heuristic: if corners look like screen-space pixels, skip camera.
                bool looksScreen = true;
                for (int i = 0; i < 4; i++)
                {
                    if (corners[i].x < -8f || corners[i].x > UiScaleService.Width + 8f
                        || corners[i].y < -8f || corners[i].y > UiScaleService.Height + 8f)
                    {
                        looksScreen = false;
                        break;
                    }
                }
                if (!looksScreen)
                    cam = Camera.main;
            }
            catch { }
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                Vector2 sp;
                if (cam != null)
                {
                    Vector3 p = cam.WorldToScreenPoint(corners[i]);
                    sp = new Vector2(UiScaleService.FromScreenX(p.x), UiScaleService.FromScreenYFlipped(p.y));
                }
                else
                    sp = new Vector2(corners[i].x, UiScaleService.Height - corners[i].y);
                if (sp.x < minX) minX = sp.x;
                if (sp.x > maxX) maxX = sp.x;
                if (sp.y < minY) minY = sp.y;
                if (sp.y > maxY) maxY = sp.y;
            }
            float w = maxX - minX;
            float h = maxY - minY;
            if (w < 20f || h < 20f)
                return false;
            rect = new Rect(minX, minY, w, h);
            return true;
        }

        private static bool TryGetMfdRadarPage(VirtualMFD mfd, out MFDScreen page)
        {
            page = null;
            ResolveFields();
            try
            {
                if (_mfdActiveLeft != null)
                {
                    MFDScreen left = _mfdActiveLeft.GetValue(mfd) as MFDScreen;
                    if (ScreenLooksRadar(left))
                    {
                        page = left;
                        return true;
                    }
                }
                if (_mfdActiveRight != null)
                {
                    MFDScreen right = _mfdActiveRight.GetValue(mfd) as MFDScreen;
                    if (ScreenLooksRadar(right))
                    {
                        page = right;
                        return true;
                    }
                }
            }
            catch { }

            // Scan all left/right screen lists — activeLeft may be null while isActive is set.
            page = FindRadarInList(mfd, _mfdLeftScreens);
            if (page != null)
                return true;
            page = FindRadarInList(mfd, _mfdRightScreens);
            return page != null;
        }

        private static MFDScreen FindRadarInList(VirtualMFD mfd, FieldInfo listField)
        {
            if (mfd == null || listField == null)
                return null;
            try
            {
                System.Collections.IList list = listField.GetValue(mfd) as System.Collections.IList;
                if (list == null)
                    return null;
                for (int i = 0; i < list.Count; i++)
                {
                    MFDScreen s = list[i] as MFDScreen;
                    if (ScreenLooksRadar(s))
                        return s;
                }
            }
            catch { }
            return null;
        }

        private static MFDScreen FirstActiveMfdScreen(VirtualMFD mfd)
        {
            if (mfd == null)
                return null;
            ResolveFields();
            try
            {
                if (_mfdActiveLeft != null)
                {
                    MFDScreen left = _mfdActiveLeft.GetValue(mfd) as MFDScreen;
                    if (left != null && left.isActive)
                        return left;
                }
                if (_mfdActiveRight != null)
                {
                    MFDScreen right = _mfdActiveRight.GetValue(mfd) as MFDScreen;
                    if (right != null && right.isActive)
                        return right;
                }
            }
            catch { }
            return null;
        }

        private static bool ScreenLooksRadar(MFDScreen s)
        {
            if (s == null)
                return false;
            try
            {
                if (!s.isActive)
                    return false;
            }
            catch { return false; }
            return ScreenLooksRadarLoose(s);
        }

        private static bool ScreenLooksRadarLoose(MFDScreen s)
        {
            if (s == null)
                return false;
            string n = SafeShortName(s);
            if (NameLooksRadar(n))
                return true;
            try
            {
                if (s.gameObject != null && NameLooksRadar(s.gameObject.name))
                    return true;
            }
            catch { }
            try
            {
                if (s.displayPanel != null && NameLooksRadar(s.displayPanel.name))
                    return true;
            }
            catch { }
            return false;
        }

        private static string SafeShortName(MFDScreen s)
        {
            if (s == null)
                return "";
            try { return s.shortName ?? ""; }
            catch { return ""; }
        }

        private static bool NameLooksRadar(string n)
        {
            return RadarContactMathService.NameLooksRadar(n);
        }

        private static void ResolveFields()
        {
            if (_fieldsResolved)
                return;
            _fieldsResolved = true;
            try { _cockpitTacField = AccessTools.Field(typeof(Cockpit), "tacScreen"); }
            catch { }
            try { _radarOnField = AccessTools.Field(typeof(TacScreen), "radarOn"); }
            catch { }
            try { _mfdActiveLeft = AccessTools.Field(typeof(VirtualMFD), "activeLeft"); }
            catch { }
            try { _mfdActiveRight = AccessTools.Field(typeof(VirtualMFD), "activeRight"); }
            catch { }
            try { _mfdLeftScreens = AccessTools.Field(typeof(VirtualMFD), "leftScreens"); }
            catch { }
            try { _mfdRightScreens = AccessTools.Field(typeof(VirtualMFD), "rightScreens"); }
            catch { }
            try { _tacScreenRenderField = AccessTools.Field(typeof(Cockpit), "tacScreenRender"); }
            catch { }
            try { _scanLineField = AccessTools.Field(typeof(TacScreen), "scanLine"); }
            catch { }
            try { _radarConeField = AccessTools.Field(typeof(TacScreen), "radarCone"); }
            catch { }
            try { _radarCenterField = AccessTools.Field(typeof(TacScreen), "radarCenter"); }
            catch { }
            try { _canvasField = AccessTools.Field(typeof(TacScreen), "canvas"); }
            catch { }
        }

        private static void RefreshContacts()
        {
            _contacts.Clear();
            Aircraft self = _cachedAc;
            if (self == null)
                return;

            Vector3 origin = Vector3.zero;
            Quaternion heading = Quaternion.identity;
            try
            {
                origin = self.transform.position;
                // PPI / contacts oriented to aircraft nose.
                heading = self.transform.rotation;
            }
            catch { return; }

            FactionHQ selfHq = null;
            try { selfHq = self.NetworkHQ; }
            catch { }

            float rangeM = Mathf.Max(5f, RangeKm != null ? RangeKm.Value : 40f) * 1000f;
            float rangeSq = rangeM * rangeM;
            HashSet<int> seen = new HashSet<int>();

            try
            {
                TargetDetector radar = self.radar;
                if (radar != null && radar.detectedTargets != null)
                {
                    List<Unit> dets = radar.detectedTargets;
                    for (int i = 0; i < dets.Count; i++)
                        AddContact(self, selfHq, origin, heading, dets[i], true, rangeSq, seen);
                }
            }
            catch { }

            try
            {
                List<Aircraft> all = UnitRegistry.allAircraft;
                if (all != null)
                {
                    int added = 0;
                    for (int i = 0; i < all.Count && added < 32; i++)
                    {
                        Aircraft ac = all[i];
                        if (ac == null || object.ReferenceEquals(ac, self))
                            continue;
                        if ((ac.transform.position - origin).sqrMagnitude > rangeSq)
                            continue;
                        if (AddContact(self, selfHq, origin, heading, ac, false, rangeSq, seen))
                            added++;
                    }
                }
            }
            catch { }
        }

        private static bool AddContact(Aircraft self, FactionHQ selfHq, Vector3 origin, Quaternion heading,
            Unit u, bool fromRadar, float rangeSq, HashSet<int> seen)
        {
            if (u == null)
                return false;
            int id = 0;
            try { id = u.GetInstanceID(); }
            catch { }
            if (id != 0 && !seen.Add(id))
                return false;

            Vector3 pos;
            try { pos = u.transform.position; }
            catch { return false; }
            Vector3 delta = pos - origin;
            if (delta.sqrMagnitude > rangeSq || delta.sqrMagnitude < 4f)
                return false;

            float bearing = RadarContactMathService.FlatBearingDeg(origin, heading, pos);
            float range = RadarContactMathService.FlatRangeM(origin, pos);

            Vector3 vel = Vector3.zero;
            try
            {
                Aircraft ac = u as Aircraft;
                if (ac != null && ac.rb != null)
                    vel = ac.rb.velocity;
            }
            catch { }
            Vector3 velFlat = vel;
            velFlat.y = 0f;

            bool friendly = false;
            try
            {
                FactionHQ hq = u.NetworkHQ;
                if (selfHq != null && hq != null)
                    friendly = object.ReferenceEquals(selfHq, hq);
            }
            catch { }

            float spd = vel.magnitude;
            try
            {
                Aircraft ac = u as Aircraft;
                if (ac != null)
                    spd = ac.speed;
            }
            catch { }

            float strength = RadarContactMathService.ContactStrength(
                fromRadar, range, Mathf.Sqrt(rangeSq));

            Contact c;
            c.BearingDeg = bearing;
            c.RangeM = range;
            c.Spd = spd;
            c.Friendly = friendly;
            c.FromRadarList = fromRadar;
            c.VelFlat = Quaternion.Inverse(heading) * velFlat;
            c.Strength = strength;
            _contacts.Add(c);
            return true;
        }

        // ---------- primitives ----------

        private static void EnsureStyles()
        {
            if (_white == null)
            {
                _white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
            }
            if (_label == null)
            {
                _label = new GUIStyle(GUI.skin.label);
                _label.fontSize = 11;
                _label.alignment = TextAnchor.UpperCenter;
                _label.normal.textColor = new Color(0.55f, 0.95f, 0.65f, 0.95f);
            }
            if (_tiny == null)
            {
                _tiny = new GUIStyle(GUI.skin.label);
                _tiny.fontSize = 10;
                _tiny.alignment = TextAnchor.UpperLeft;
                _tiny.normal.textColor = new Color(0.55f, 0.95f, 0.65f, 0.9f);
            }
        }

        private static void FillCircle(Vector2 c, float r)
        {
            int rings = 10;
            for (int i = rings; i >= 1; i--)
            {
                float rr = r * i / rings;
                StrokeCircle(c, rr, Mathf.Max(2f, r / rings + 1f), 20 + i);
            }
        }

        private static void StrokeCircle(Vector2 c, float r, float thick, int segs)
        {
            if (segs < 8) segs = 8;
            float step = 360f / segs;
            Vector2 prev = new Vector2(c.x, c.y - r);
            for (int i = 1; i <= segs; i++)
            {
                float a = i * step * Mathf.Deg2Rad;
                Vector2 p = new Vector2(c.x + Mathf.Sin(a) * r, c.y - Mathf.Cos(a) * r);
                DrawLine(prev, p, thick);
                prev = p;
            }
        }

        private static void DrawLine(Vector2 a, Vector2 b, float thick)
        {
            UiScaleService.DrawLine(a, b, thick, _white);
        }
    }
}
