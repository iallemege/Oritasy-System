using System;
using BepInEx.Configuration;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Gear-down HUD: nearest airbase CaptureRange ring (vanilla near-airbase crash exempt)
    /// plus simulated ILS localizer / glideslope needles (0.0.9.68).
    /// </summary>
    internal static class AirportIlsHud
    {
        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<bool> _showSafeRange;
        private static ConfigEntry<bool> _showIls;
        private static ConfigEntry<KeyCode> _toggleKey;
        private static bool _overlayOn = true;

        private static GUIStyle _label;
        private static GUIStyle _small;
        private static float _nextScan;
        private static Airbase _cachedBase;
        private static Airbase.Runway _cachedRwy;
        private static Vector3 _touchPos;
        private static Vector3 _rwyDir;

        internal static bool Enabled
        {
            get { return _enabled != null && _enabled.Value && _overlayOn; }
        }

        internal static ConfigEntry<KeyCode> OverlayToggleKey
        {
            get { return _toggleKey; }
        }

        internal static void Bind(ConfigFile config)
        {
            _enabled = config.Bind("AirportIlsHud", "Enabled", true,
                "When landing gear is down: draw nearest airbase safe radius + ILS needles.");
            _showSafeRange = config.Bind("AirportIlsHud", "ShowSafeRange", true,
                "Draw CaptureRange circle (vanilla: landed inside = disable not counted as crash).");
            _showIls = config.Bind("AirportIlsHud", "ShowIls", true,
                "Draw simulated ILS localizer / glideslope cross (angle from F4 ILS settings).");
            _toggleKey = config.Bind("AirportIlsHud", "ToggleKey", KeyCode.None,
                "Optional hotkey to toggle this overlay. None = always follow Enabled.");
        }

        internal static void Tick()
        {
            if (_toggleKey != null && _toggleKey.Value != KeyCode.None
                && Input.GetKeyDown(_toggleKey.Value))
                _overlayOn = !_overlayOn;
        }

        internal static void Draw()
        {
            Aircraft ac = null;
            try { GameManager.GetLocalAircraft(out ac); }
            catch { }
            bool gearDown = ac != null && GearIsDown(ac);
            Camera cam = ResolveViewCamera();
            bool isRepaint = Event.current == null || Event.current.type == EventType.Repaint;
            if (!Plugin.AllowThirdPersonUi)
                return;
            if (AirportIlsHudGateService.ResolveDraw(
                    Enabled,
                    isRepaint,
                    MissileCameraHud.ManualActive,
                    OritasyPresentation.BlocksHud,
                    ac != null,
                    gearDown,
                    cam != null) != AirportIlsHudGateService.Path.Draw)
                return;

            EnsureScan(ac);
            if (_cachedBase == null)
                return;

            EnsureStyles();

            Vector3 center = ResolveCenter(_cachedBase);
            float radius = 0f;
            try { radius = _cachedBase.GetRadius(); }
            catch { radius = 0f; }
            radius = AirportSafeRangeMathService.ResolveRadiusM(radius);

            Vector3 acPos = ac.transform.position;
            float horiz = AirportSafeRangeMathService.HorizontalDistanceM(acPos, center);
            bool inside = AirportSafeRangeMathService.InsideRadius(horiz, radius);
            float remain = AirportSafeRangeMathService.RemainingToEdgeM(horiz, radius);

            if (AirportIlsHudGateService.ShouldPaintSafeRange(
                    _showSafeRange == null || _showSafeRange.Value))
                DrawSafeRing(cam, center, radius, inside);

            float ralt = 0f;
            try { ralt = ac.radarAlt; }
            catch { }

            IlsApproachMathService.Result ils = default(IlsApproachMathService.Result);
            bool haveIls = _cachedRwy != null;
            if (haveIls)
                ils = IlsApproachMathService.Evaluate(acPos, ralt, _touchPos, _rwyDir);
            if (AirportIlsHudGateService.ShouldPaintIls(
                    _showIls == null || _showIls.Value, haveIls))
            {
                DrawIlsInstrument(ils);
                DrawGlideAim(cam, ils);
            }

            DrawStatusChip(ac, center, radius, horiz, remain, inside, haveIls, ils);
        }

        private static bool GearIsDown(Aircraft ac)
        {
            try
            {
                if (ac.gearDeployed)
                    return true;
            }
            catch { }
            try
            {
                LandingGear.GearState gs = ac.gearState;
                return gs != LandingGear.GearState.LockedRetracted;
            }
            catch { }
            return false;
        }

        private static void EnsureScan(Aircraft ac)
        {
            if (Time.unscaledTime < _nextScan && _cachedBase != null)
                return;
            _nextScan = Time.unscaledTime + 0.35f;

            Airbase ab = null;
            try
            {
                FactionHQ hq = null;
                try { hq = ac.NetworkHQ; }
                catch { }
                if (hq == null)
                    GameManager.GetLocalHQ(out hq);
                if (hq != null)
                {
                    Airbase near;
                    if (hq.AnyNearAirbase(ac.transform.position, out near) && near != null)
                        ab = near;
                    if (ab == null)
                    {
                        AircraftParameters parms = null;
                        try { parms = ac.GetAircraftParameters(); }
                        catch { }
                        RunwayQuery q = AirbaseLocator.BuildLandQuery(ac, parms);
                        ab = hq.GetNearestAirbase(ac.transform.position, q);
                    }
                }
            }
            catch { }

            if (ab == null)
                ab = AirbaseLocator.Resolve(ac, false, null);

            _cachedBase = ab;
            _cachedRwy = null;
            if (ab == null)
                return;

            PickBestRunway(ac, ab);
        }

        private static void PickBestRunway(Aircraft ac, Airbase ab)
        {
            Airbase.Runway[] runways = null;
            try { runways = ab.runways; }
            catch { }
            if (runways == null || runways.Length == 0)
                return;

            Vector3 acPos = ac.transform.position;
            float bestScore = float.MaxValue;
            Airbase.Runway best = null;
            Vector3 bestTouch = Vector3.zero;
            Vector3 bestDir = Vector3.forward;

            for (int i = 0; i < runways.Length; i++)
            {
                Airbase.Runway rwy = runways[i];
                if (rwy == null)
                    continue;
                bool landing = true;
                try { landing = rwy.Landing; }
                catch { }
                if (!landing)
                    continue;

                Transform start = null;
                Transform end = null;
                try { start = rwy.Start; }
                catch { }
                try { end = rwy.End; }
                catch { }
                if (start == null)
                    continue;

                Vector3 dir = Vector3.forward;
                try
                {
                    dir = rwy.GetDirection(false);
                    dir.y = 0f;
                    if (dir.sqrMagnitude > 0.01f)
                        dir.Normalize();
                    else if (end != null)
                    {
                        dir = end.position - start.position;
                        dir.y = 0f;
                        if (dir.sqrMagnitude > 0.01f)
                            dir.Normalize();
                    }
                }
                catch
                {
                    try
                    {
                        if (end != null)
                        {
                            dir = end.position - start.position;
                            dir.y = 0f;
                            if (dir.sqrMagnitude > 0.01f)
                                dir.Normalize();
                        }
                    }
                    catch { }
                }

                float along, lat;
                LandingMath.RunwayAlongLateral(acPos, start.position, dir, out along, out lat);
                float score = IlsApproachMathService.ScoreApproach(
                    along, lat, Vector3.Distance(acPos, start.position));
                if (score < bestScore)
                {
                    bestScore = score;
                    best = rwy;
                    bestTouch = start.position;
                    bestDir = dir;
                }

                bool revOk = false;
                try { revOk = rwy.Reversable && end != null; }
                catch { revOk = end != null; }
                if (revOk && end != null)
                {
                    Vector3 rdir = -dir;
                    LandingMath.RunwayAlongLateral(acPos, end.position, rdir, out along, out lat);
                    score = IlsApproachMathService.ScoreApproach(
                        along, lat, Vector3.Distance(acPos, end.position));
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = rwy;
                        bestTouch = end.position;
                        bestDir = rdir;
                    }
                }
            }

            _cachedRwy = best;
            _touchPos = bestTouch;
            _rwyDir = bestDir;
        }

        private static Vector3 ResolveCenter(Airbase ab)
        {
            try
            {
                if (ab.center != null)
                    return ab.center.position;
            }
            catch { }
            try { return ab.transform.position; }
            catch { return Vector3.zero; }
        }

        private static void DrawSafeRing(Camera cam, Vector3 center, float radiusM, bool inside)
        {
            Color col = inside
                ? new Color(0.25f, 0.95f, 0.45f, 0.85f)
                : new Color(0.95f, 0.75f, 0.2f, 0.75f);
            Color prev = GUI.color;
            GUI.color = col;

            int segs = AirportSafeRangeMathService.RingSegments;
            Vector2 prevPt = Vector2.zero;
            bool hasPrev = false;
            for (int i = 0; i <= segs; i++)
            {
                Vector3 world = AirportSafeRangeMathService.RingPoint(center, radiusM, i, segs);
                Vector3 sp;
                try { sp = cam.WorldToScreenPoint(world); }
                catch { hasPrev = false; continue; }
                if (sp.z <= 0.08f)
                {
                    hasPrev = false;
                    continue;
                }
                Vector2 p = new Vector2(UiScaleService.FromScreenX(sp.x), UiScaleService.FromScreenYFlipped(sp.y));
                if (hasPrev)
                    DrawLine(prevPt, p, 1.6f);
                prevPt = p;
                hasPrev = true;
            }

            // Center tick
            Vector3 csp;
            try { csp = cam.WorldToScreenPoint(center); }
            catch { GUI.color = prev; return; }
            if (csp.z > 0.08f)
            {
                float cx = UiScaleService.FromScreenX(csp.x);
                float cy = UiScaleService.FromScreenYFlipped(csp.y);
                GUI.DrawTexture(new Rect(cx - 5f, cy - 1f, 10f, 2f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cx - 1f, cy - 5f, 2f, 10f), Texture2D.whiteTexture);
            }
            GUI.color = prev;
        }

        private static void DrawIlsInstrument(IlsApproachMathService.Result ils)
        {
            float size = Mathf.Min(UiScaleService.Width, UiScaleService.Height) * 0.16f;
            float cx = UiScaleService.Width * 0.5f;
            float cy = UiScaleService.Height * 0.72f;
            Rect box = new Rect(cx - size * 0.5f, cy - size * 0.5f, size, size);

            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.35f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = new Color(0.55f, 0.9f, 0.7f, 0.55f);
            // Outer frame
            GUI.DrawTexture(new Rect(box.x, box.y, box.width, 1.5f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(box.x, box.yMax - 1.5f, box.width, 1.5f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(box.x, box.y, 1.5f, box.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(box.xMax - 1.5f, box.y, 1.5f, box.height), Texture2D.whiteTexture);

            // Reference cross
            float midX = box.x + box.width * 0.5f;
            float midY = box.y + box.height * 0.5f;
            GUI.color = new Color(0.4f, 0.7f, 0.55f, 0.45f);
            GUI.DrawTexture(new Rect(midX - 1f, box.y + 8f, 2f, box.height - 16f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(box.x + 8f, midY - 1f, box.width - 16f, 2f), Texture2D.whiteTexture);

            // Dot marks at ±1 / ±2
            float half = box.width * 0.5f - 10f;
            for (int d = -2; d <= 2; d++)
            {
                if (d == 0)
                    continue;
                float ox = midX + (d / 2f) * half;
                float oy = midY + (d / 2f) * half;
                GUI.DrawTexture(new Rect(ox - 2f, midY - 2f, 4f, 4f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(midX - 2f, oy - 2f, 4f, 4f), Texture2D.whiteTexture);
            }

            // Localizer = vertical needle (moves left/right with lateral)
            float locX = midX + (ils.LocDots / IlsApproachMathService.MaxNeedleDots) * half;
            GUI.color = new Color(0.95f, 0.95f, 0.35f, 0.95f);
            GUI.DrawTexture(new Rect(locX - 1.5f, box.y + 10f, 3f, box.height - 20f), Texture2D.whiteTexture);

            // Glideslope = horizontal needle
            if (ils.GsValid)
            {
                float gsY = midY - (ils.GsDots / IlsApproachMathService.MaxNeedleDots) * half;
                GUI.color = new Color(0.35f, 0.95f, 1f, 0.95f);
                GUI.DrawTexture(new Rect(box.x + 10f, gsY - 1.5f, box.width - 20f, 3f), Texture2D.whiteTexture);
            }

            GUI.color = new Color(0.8f, 0.95f, 0.85f, 0.9f);
            string deg = IlsApproachMathService.GlideSlopeDeg.ToString("0.0");
            string title = UiLang.T("ILS  " + deg + "°", "ILS  " + deg + "°下滑道");
            GUI.Label(new Rect(box.x, box.y - 18f, box.width, 18f), title, _small);
            GUI.color = prev;
        }

        private static void DrawGlideAim(Camera cam, IlsApproachMathService.Result ils)
        {
            if (!IlsApproachMathService.ShouldDrawGlideAim(ils.OnFinalCorridor, ils.AlongM))
                return;
            float look = IlsApproachMathService.GlideAimLookAheadM(ils.AlongM);
            Vector3 aim = IlsApproachMathService.GlideAimPoint(_touchPos, _rwyDir, look);
            Vector3 sp;
            try { sp = cam.WorldToScreenPoint(aim); }
            catch { return; }
            if (sp.z <= 0.08f)
                return;
            float x = UiScaleService.FromScreenX(sp.x);
            float y = UiScaleService.FromScreenYFlipped(sp.y);
            Color prev = GUI.color;
            GUI.color = new Color(0.3f, 0.95f, 1f, 0.8f);
            float arm = 10f;
            GUI.DrawTexture(new Rect(x - arm, y - 1f, arm * 2f, 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x - 1f, y - arm, 2f, arm * 2f), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        private static void DrawStatusChip(
            Aircraft ac,
            Vector3 center,
            float radius,
            float horiz,
            float remain,
            bool inside,
            bool haveIls,
            IlsApproachMathService.Result ils)
        {
            string name = AirbaseLocator.FormatAirbaseName(_cachedBase, AirbaseLocator.IsCarrierAirbase(_cachedBase));
            string line1;
            string line2;
            if (UiLang.IsChinese)
            {
                line1 = name + "  安全半径 " + radius.ToString("0") + "m"
                    + (inside ? "  [圈内]" : "  [圈外 " + Mathf.Abs(remain).ToString("0") + "m]");
                line2 = "近场着陆免计坠毁（同跳伞安全圈）  距中心 " + horiz.ToString("0") + "m";
                if (haveIls && ils.OnFinalCorridor)
                {
                    line2 += "  |  LOC " + FormatDots(ils.LocDots)
                        + (ils.GsValid ? ("  GS " + FormatDots(ils.GsDots)) : "")
                        + "  距门 " + ils.AlongM.ToString("0") + "m";
                }
            }
            else
            {
                line1 = name + "  SAFE R " + radius.ToString("0") + "m"
                    + (inside ? "  [INSIDE]" : "  [OUT " + Mathf.Abs(remain).ToString("0") + "m]");
                line2 = "Near-airbase safe zone (CaptureRange)  dist " + horiz.ToString("0") + "m";
                if (haveIls && ils.OnFinalCorridor)
                {
                    line2 += "  |  LOC " + FormatDots(ils.LocDots)
                        + (ils.GsValid ? ("  GS " + FormatDots(ils.GsDots)) : "")
                        + "  DME~ " + ils.AlongM.ToString("0") + "m";
                }
            }

            Color prev = GUI.color;
            float w = Mathf.Min(UiScaleService.Width * 0.55f, 520f);
            Rect chip = new Rect(UiScaleService.Width * 0.5f - w * 0.5f, 56f, w, 40f);
            GUI.color = new Color(0f, 0f, 0f, 0.4f);
            GUI.DrawTexture(chip, Texture2D.whiteTexture);
            GUI.color = inside
                ? new Color(0.3f, 1f, 0.5f, 0.95f)
                : new Color(1f, 0.82f, 0.3f, 0.95f);
            GUI.Label(new Rect(chip.x + 8f, chip.y + 2f, chip.width - 16f, 18f), line1, _label);
            GUI.color = new Color(0.85f, 0.92f, 0.88f, 0.9f);
            GUI.Label(new Rect(chip.x + 8f, chip.y + 20f, chip.width - 16f, 18f), line2, _small);
            GUI.color = prev;
        }

        private static string FormatDots(float dots)
        {
            if (dots > 0.05f)
                return "+" + dots.ToString("0.0");
            return dots.ToString("0.0");
        }

        private static void DrawLine(Vector2 a, Vector2 b, float thickness)
        {
            UiScaleService.DrawLine(a, b, thickness);
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
            return null;
        }

        private static void EnsureStyles()
        {
            if (_label != null)
                return;
            _label = new GUIStyle(GUI.skin.label);
            _label.fontSize = 13;
            _label.fontStyle = FontStyle.Bold;
            _label.alignment = TextAnchor.UpperLeft;
            _label.normal.textColor = Color.white;
            _small = new GUIStyle(GUI.skin.label);
            _small.fontSize = 11;
            _small.alignment = TextAnchor.UpperLeft;
            _small.normal.textColor = Color.white;
        }
    }
}
