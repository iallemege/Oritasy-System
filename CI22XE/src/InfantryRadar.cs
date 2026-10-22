using System.Collections.Generic;
using UnityEngine;

namespace Oritasy
{
    /// <summary>Portable PPI for infantry: aircraft / vehicles / ships around the soldier.</summary>
    internal static class InfantryRadar
    {
        internal const float RangeM = 6000f;

        private static Texture2D _px;
        private static GUIStyle _tiny;
        private static float _sweep;
        private static bool _styles;
        private static float _nextMark;
        private static readonly List<Mark> Marks = new List<Mark>(64);

        private struct Mark
        {
            public Vector3 Pos;
            public Color Col;
            public float Size;
        }

        internal static void Tick(float dt)
        {
            _sweep = Mathf.Repeat(_sweep + dt * 150f, 360f);
            if (Time.unscaledTime < _nextMark)
                return;
            _nextMark = Time.unscaledTime + 0.2f;
            RebuildMarks();
        }

        private static void RebuildMarks()
        {
            Marks.Clear();
            Vector3 origin = InfantryModeService.EyePos;
            FactionHQ localHq = LocalHq();
            try
            {
                List<Unit> all = UnitRegistry.allUnits;
                if (all == null)
                    return;
                int n = all.Count;
                for (int i = 0; i < n; i++)
                {
                    Unit u = all[i];
                    if (u == null || u.disabled)
                        continue;
                    bool ac = u is Aircraft;
                    bool ship = !ac && u is Ship;
                    if (!ac && !ship && !(u is GroundVehicle))
                        continue;
                    Vector3 dp = u.transform.position - origin;
                    dp.y = 0f;
                    float dist = dp.magnitude;
                    if (dist > RangeM || dist < 2f)
                        continue;
                    bool claimed = ac
                        && object.ReferenceEquals(u, InfantryModeService.ClaimedAircraft);
                    bool hostile = IsHostile(u, localHq);
                    Mark m = new Mark();
                    m.Pos = u.transform.position;
                    if (claimed)
                        m.Col = new Color(0.2f, 0.95f, 1f, 1f);
                    else if (ac)
                        m.Col = hostile ? new Color(1f, 0.28f, 0.18f, 0.95f) : new Color(0.25f, 1f, 0.4f, 0.95f);
                    else if (ship)
                        m.Col = hostile ? new Color(1f, 0.55f, 0.15f, 0.95f) : new Color(0.3f, 0.75f, 1f, 0.95f);
                    else
                        m.Col = hostile ? new Color(1f, 0.82f, 0.2f, 0.9f) : new Color(0.55f, 0.9f, 0.35f, 0.9f);
                    m.Size = claimed ? 8f : (ac ? 5f : 4f);
                    Marks.Add(m);
                }
            }
            catch { }
        }

        internal static Rect ScreenRect()
        {
            float size = 176f;
            return new Rect(Screen.width - size - 16f, Screen.height - size - 96f, size, size);
        }

        internal static void Draw()
        {
            if (!InfantryModeService.Active)
                return;
            Rect box = ScreenRect();
            if (EmpPulseService.LocalPlayerStunned())
            {
                EmpPulseService.DrawBsodOn(box);
                return;
            }
            Ensure();
            float size = box.width;
            float x = box.x;
            float y = box.y;
            Vector2 c = new Vector2(x + size * 0.5f, y + size * 0.5f);
            float r = size * 0.42f;

            Color prev = GUI.color;
            GUI.color = new Color(0.02f, 0.08f, 0.09f, 0.78f);
            GUI.DrawTexture(new Rect(x, y, size, size), _px);
            GUI.color = new Color(0.2f, 0.9f, 0.78f, 0.32f);
            DrawRing(c, r, 24);
            DrawRing(c, r * 0.66f, 16);
            DrawRing(c, r * 0.33f, 12);

            float yaw = InfantryModeService.LookYaw;
            Vector3 origin = InfantryModeService.EyePos;

            for (int i = 0; i < Marks.Count; i++)
            {
                Mark m = Marks[i];
                Vector3 dp = m.Pos - origin;
                dp.y = 0f;
                float dist = dp.magnitude;
                if (dist > RangeM || dist < 2f)
                    continue;
                float bearing = Mathf.Atan2(dp.x, dp.z) * Mathf.Rad2Deg - yaw;
                Vector2 p = Polar(c, r, bearing, dist / RangeM);
                GUI.color = m.Col;
                GUI.DrawTexture(new Rect(p.x - m.Size * 0.5f, p.y - m.Size * 0.5f, m.Size, m.Size), _px);
            }

            int botN = InfantryAi.Count;
            for (int i = 0; i < botN; i++)
            {
                Vector3 bp;
                bool fr;
                if (!InfantryAi.TryMark(i, out bp, out fr))
                    continue;
                Vector3 dp = bp - origin;
                dp.y = 0f;
                float dist = dp.magnitude;
                if (dist > RangeM || dist < 1f)
                    continue;
                float bearing = Mathf.Atan2(dp.x, dp.z) * Mathf.Rad2Deg - yaw;
                Vector2 p = Polar(c, r, bearing, dist / RangeM);
                GUI.color = fr
                    ? new Color(0.4f, 1f, 0.55f, 0.9f)
                    : new Color(1f, 0.25f, 0.2f, 0.9f);
                GUI.DrawTexture(new Rect(p.x - 2f, p.y - 2f, 4f, 4f), _px);
            }

            GUI.color = new Color(0.3f, 0.95f, 0.82f, 0.5f);
            Vector2 tip = Polar(c, r, _sweep, 1f);
            DrawSeg(c, tip, 2f);
            GUI.color = new Color(0.25f, 0.95f, 0.82f, 0.9f);
            GUI.DrawTexture(new Rect(c.x - 2f, c.y - 2f, 4f, 4f), _px);
            GUI.color = Color.white;
            GUI.Label(new Rect(x + 6f, y + 2f, 140f, 16f),
                UiLang.T("SUIT RADAR  6 km", "动力甲雷达  6 km"), _tiny);
            GUI.color = prev;
        }

        private static FactionHQ LocalHq()
        {
            Unit linked = InfantryModeService.LinkedUnit();
            if (linked != null)
            {
                try { return linked.NetworkHQ; }
                catch { }
            }
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

        private static Vector2 Polar(Vector2 c, float r, float bearingDeg, float rangeNorm)
        {
            float rad = bearingDeg * Mathf.Deg2Rad;
            float rr = Mathf.Clamp01(rangeNorm) * r;
            return new Vector2(c.x + Mathf.Sin(rad) * rr, c.y - Mathf.Cos(rad) * rr);
        }

        private static void DrawRing(Vector2 c, float r, int segs)
        {
            for (int i = 0; i < segs; i++)
            {
                float a = (i / (float)segs) * 360f * Mathf.Deg2Rad;
                float x = c.x + Mathf.Sin(a) * r;
                float y = c.y - Mathf.Cos(a) * r;
                GUI.DrawTexture(new Rect(x - 1f, y - 1f, 2f, 2f), _px);
            }
        }

        private static void DrawSeg(Vector2 a, Vector2 b, float w)
        {
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1f)
                return;
            int n = Mathf.CeilToInt(len / 3f);
            if (n > 48)
                n = 48;
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                float x = a.x + d.x * t;
                float y = a.y + d.y * t;
                GUI.DrawTexture(new Rect(x - w * 0.5f, y - w * 0.5f, w, w), _px);
            }
        }

        private static void Ensure()
        {
            if (_styles)
                return;
            _styles = true;
            _px = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _px.SetPixel(0, 0, Color.white);
            _px.Apply();
            _tiny = new GUIStyle(GUI.skin.label);
            _tiny.fontSize = 10;
            _tiny.normal.textColor = new Color(0.45f, 1f, 0.88f);
        }
    }
}
