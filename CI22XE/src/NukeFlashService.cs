using System.Collections.Generic;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Looking at a nuclear fireball: 30 s English BSOD visor, nearby-target strip, radar BSOD.
    /// </summary>
    internal static class NukeFlashService
    {
        internal const float BlindSec = 30f;
        internal const float BigKt = 200f;
        internal const float TacKt = 15f;
        private const float ContactRangeM = 25000f;
        private const int MaxContacts = 10;

        private static Texture2D _px;
        private static GUIStyle _center;
        private static GUIStyle _sub;
        private static GUIStyle _row;
        private static GUIStyle _face;
        private static GUIStyle _code;
        private static float _until;
        private static float _started;
        private static float _styleH;

        internal static float Until
        {
            get { return _until; }
        }

        internal static bool Active
        {
            get { return Time.unscaledTime < _until; }
        }

        internal static void TryBlind(Shockwave sw, float yieldKt)
        {
            if (sw == null)
                return;
            if (yieldKt < TacKt)
                return;
            Camera cam = Camera.main;
            if (cam == null)
                return;
            Vector3 origin = sw.transform.position;
            Vector3 to = origin - cam.transform.position;
            float dist = to.magnitude;
            if (dist < 1f)
                dist = 1f;
            float range = 12000f * Mathf.Pow(yieldKt, 1f / 3f);
            if (range < 8000f)
                range = 8000f;
            if (dist > range)
                return;
            float look = Vector3.Dot(cam.transform.forward, to / dist);
            float radius = 90f * Mathf.Pow(yieldKt / 20f, 1f / 3f);
            if (radius < 80f)
                radius = 80f;
            float halfAng = Mathf.Atan2(radius * 1.8f, dist);
            float off = Mathf.Acos(Mathf.Clamp(look, -1f, 1f));
            float need = 0.55f;
            if (yieldKt >= BigKt)
                need = 0.22f;
            if (off > halfAng + need)
                return;
            float now = Time.unscaledTime;
            _started = now;
            _until = now + BlindSec;
            EmpPulseService.ForceLocalHudBsod(BlindSec);
        }

        internal static void Draw()
        {
            if (!Active)
                return;
            Event e = Event.current;
            if (e == null)
                return;
            if (e.type != EventType.Repaint && e.type != EventType.Layout)
                return;
            Ensure();
            int prevDepth = GUI.depth;
            GUI.depth = -3000;
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0.47f, 0.84f, 1f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _px);
            GUI.color = Color.white;
            DrawContactStrip();
            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            GUI.Label(new Rect(cx - 420f, cy - 120f, 840f, 64f), ":(", _face);
            GUI.Label(new Rect(cx - 420f, cy - 40f, 840f, 36f),
                "NUCLEAR DETECTION", _center);
            GUI.Label(new Rect(cx - 420f, cy + 4f, 840f, 52f),
                "Your visor ran into a problem and needs to restart.\nWe're collecting some error info, and then we'll restart for you.",
                _sub);
            int left = Mathf.CeilToInt(_until - Time.unscaledTime);
            if (left < 0)
                left = 0;
            GUI.Label(new Rect(cx - 420f, cy + 64f, 840f, 22f),
                left.ToString() + " seconds remaining", _sub);
            GUI.Label(new Rect(cx - 420f, cy + 90f, 840f, 20f),
                "Stop code: NUKE_OPTICAL_FLASH", _code);
            GUI.color = prev;
            GUI.depth = prevDepth;
            EmpPulseService.DrawGoggleRadarBsod();
        }

        private static void DrawContactStrip()
        {
            Vector3 origin;
            float yaw;
            Observer(out origin, out yaw);
            FactionHQ hq = LocalHq();
            GUI.color = new Color(0f, 0.28f, 0.55f, 0.95f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, 92f), _px);
            GUI.color = Color.white;
            GUI.Label(new Rect(10f, 4f, 480f, 18f), "NEARBY CONTACTS", _row);
            int n = 0;
            try
            {
                List<Unit> all = UnitRegistry.allUnits;
                if (all != null)
                {
                    for (int i = 0; i < all.Count && n < MaxContacts; i++)
                    {
                        Unit u = all[i];
                        if (u == null || u.disabled)
                            continue;
                        if (!(u is Aircraft) && !(u is GroundVehicle) && !(u is Ship))
                            continue;
                        Vector3 dp = u.transform.position - origin;
                        float dist = dp.magnitude;
                        if (dist > ContactRangeM || dist < 8f)
                            continue;
                        Vector3 flat = dp;
                        flat.y = 0f;
                        float brg = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg - yaw;
                        while (brg < 0f)
                            brg += 360f;
                        while (brg >= 360f)
                            brg -= 360f;
                        bool hostile = IsHostile(u, hq);
                        string kind = u is Aircraft ? "A" : (u is Ship ? "S" : "V");
                        string name = UnitLabel(u);
                        string line = kind + "  "
                            + Mathf.RoundToInt(brg).ToString("000") + "°  "
                            + (dist / 1000f).ToString("0.0") + " km  "
                            + name;
                        GUI.color = hostile
                            ? new Color(1f, 0.35f, 0.28f, 1f)
                            : new Color(0.35f, 1f, 0.45f, 1f);
                        float col = n < 5 ? 10f : (Screen.width * 0.5f + 8f);
                        float row = n < 5 ? (22f + n * 13f) : (22f + (n - 5) * 13f);
                        GUI.Label(new Rect(col, row, Screen.width * 0.48f, 16f), line, _row);
                        n++;
                    }
                }
            }
            catch { }
            GUI.color = Color.white;
        }

        private static void Observer(out Vector3 origin, out float yaw)
        {
            origin = Vector3.zero;
            yaw = 0f;
            if (InfantryModeService.Active)
            {
                origin = InfantryModeService.EyePos;
                yaw = InfantryModeService.LookYaw;
                return;
            }
            Vector3 gaEye;
            float gaYaw;
            bool gaDead;
            if (EmpPulseService.TryGaInfantry(out gaEye, out gaYaw, out gaDead) && !gaDead)
            {
                origin = gaEye;
                yaw = gaYaw;
                return;
            }
            Camera cam = Camera.main;
            if (cam != null)
            {
                origin = cam.transform.position;
                yaw = cam.transform.eulerAngles.y;
            }
        }

        private static FactionHQ LocalHq()
        {
            FactionHQ hq = null;
            try { GameManager.GetLocalHQ(out hq); }
            catch { }
            return hq;
        }

        private static bool IsHostile(Unit u, FactionHQ local)
        {
            if (u == null || local == null)
                return true;
            try
            {
                FactionHQ other = u.NetworkHQ;
                if (other == null)
                    return true;
                return !object.ReferenceEquals(other, local);
            }
            catch
            {
                return true;
            }
        }

        private static string UnitLabel(Unit u)
        {
            if (u == null)
                return "?";
            try
            {
                if (u.definition != null)
                {
                    if (!string.IsNullOrEmpty(u.definition.unitName))
                        return u.definition.unitName;
                    if (!string.IsNullOrEmpty(u.definition.jsonKey))
                        return u.definition.jsonKey;
                }
            }
            catch { }
            return u.name ?? "?";
        }

        private static void Ensure()
        {
            if (_px == null)
            {
                _px = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _px.SetPixel(0, 0, Color.white);
                _px.Apply();
            }
            float h = Screen.height;
            if (_center != null && Mathf.Abs(_styleH - h) < 2f)
                return;
            _styleH = h;
            _center = new GUIStyle(GUI.skin.label);
            _center.fontSize = 22;
            _center.fontStyle = FontStyle.Bold;
            _center.alignment = TextAnchor.MiddleCenter;
            _center.normal.textColor = Color.white;
            _center.wordWrap = true;
            _face = new GUIStyle(GUI.skin.label);
            _face.fontSize = 54;
            _face.fontStyle = FontStyle.Bold;
            _face.alignment = TextAnchor.MiddleCenter;
            _face.normal.textColor = Color.white;
            _sub = new GUIStyle(GUI.skin.label);
            _sub.fontSize = 14;
            _sub.alignment = TextAnchor.MiddleCenter;
            _sub.normal.textColor = Color.white;
            _sub.wordWrap = true;
            _code = new GUIStyle(GUI.skin.label);
            _code.fontSize = 13;
            _code.alignment = TextAnchor.MiddleCenter;
            _code.normal.textColor = Color.white;
            _row = new GUIStyle(GUI.skin.label);
            _row.fontSize = 12;
            _row.alignment = TextAnchor.MiddleLeft;
            _row.normal.textColor = Color.white;
            Font en = EnglishFont(16);
            if (en != null)
            {
                _center.font = en;
                _face.font = en;
                _sub.font = en;
                _code.font = en;
                _row.font = en;
            }
        }

        private static Font EnglishFont(int size)
        {
            try
            {
                return Font.CreateDynamicFontFromOSFont(
                    new string[] { "Segoe UI", "Arial", "Tahoma", "Calibri" }, size);
            }
            catch
            {
                return null;
            }
        }
    }
}
