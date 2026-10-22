using System.Collections.Generic;
using UnityEngine;

namespace Oritasy
{
    /// <summary>Infantry threat / exo warnings (overheat, hit, lock, spike).</summary>
    internal static class InfantryWarnService
    {
        internal static float ExoHeat;
        internal static float HitUntil;
        internal static float LockUntil;
        internal static float SpikeUntil;
        internal static float FatigueUntil;

        private static float _nextScan;
        private static GUIStyle _big;
        private static GUIStyle _tiny;
        private static Texture2D _px;
        private static bool _styles;

        internal static void Reset()
        {
            ExoHeat = 0f;
            HitUntil = 0f;
            LockUntil = 0f;
            SpikeUntil = 0f;
            FatigueUntil = 0f;
        }

        internal static void NoteHit()
        {
            HitUntil = Time.unscaledTime + 1.8f;
        }

        internal static void NoteFatigue(float fat)
        {
            if (fat >= 0.55f)
                FatigueUntil = Time.unscaledTime + 1.2f;
        }

        internal static void NoteFireHeat()
        {
            ExoHeat += 0.018f;
            if (ExoHeat > 1f)
                ExoHeat = 1f;
        }

        internal static void NoteJump()
        {
            ExoHeat += 0.07f;
            if (ExoHeat > 1f)
                ExoHeat = 1f;
        }

        internal static void Tick(float dt)
        {
            if (!InfantryModeService.Active || InfantryModeService.Dead)
            {
                ExoHeat = Mathf.MoveTowards(ExoHeat, 0f, dt * 0.4f);
                return;
            }
            float add = -0.15f;
            if (InfantryModeService.SprintActive)
                add += 0.34f;
            if (InfantryModeService.MoveSpeed > 5.2f)
                add += 0.08f;
            ExoHeat += add * dt;
            if (ExoHeat < 0f) ExoHeat = 0f;
            if (ExoHeat > 1f) ExoHeat = 1f;

            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 0.7f;
                ScanThreats();
            }
        }

        internal static bool Overheated()
        {
            return ExoHeat >= 0.92f;
        }

        internal static void Draw()
        {
            if (!InfantryModeService.Active || InfantryModeService.Dead)
                return;
            Ensure();
            float now = Time.unscaledTime;
            float y = Screen.height * 0.18f;
            if (ExoHeat >= 0.72f)
            {
                bool blink = ExoHeat >= 0.92f && ((int)(now * 6f) % 2) == 0;
                DrawLine(y, UiLang.T("EXO OVERHEAT", "外骨骼过热"),
                    blink ? new Color(1f, 0.25f, 0.1f) : new Color(1f, 0.55f, 0.12f));
                y += 26f;
            }
            if (now < HitUntil)
            {
                DrawLine(y, UiLang.T("HIT", "被命中"), new Color(1f, 0.18f, 0.14f));
                y += 26f;
            }
            if (now < LockUntil)
            {
                bool blink = ((int)(now * 8f) % 2) == 0;
                DrawLine(y, UiLang.T("LOCKED", "被锁定"),
                    blink ? new Color(1f, 0.08f, 0.08f) : new Color(1f, 0.45f, 0.15f));
                y += 26f;
            }
            else if (now < SpikeUntil)
            {
                DrawLine(y, UiLang.T("SPIKE", "被照射"), new Color(1f, 0.82f, 0.15f));
                y += 26f;
            }
            if (now < FatigueUntil)
            {
                DrawLine(y, UiLang.T("PLATE FATIGUE", "外甲疲劳"), new Color(1f, 0.7f, 0.2f));
            }
        }

        private static void ScanThreats()
        {
            Vector3 me = InfantryModeService.EyePos;
            if (me.sqrMagnitude < 0.01f && InfantryModeService.PawnRoot != null)
                me = InfantryModeService.PawnRoot.position + Vector3.up * 1.1f;
            Unit linked = InfantryModeService.LinkedUnit();
            PersistentID myId = PersistentID.None;
            try
            {
                if (linked != null)
                    myId = linked.persistentID;
            }
            catch { }

            bool locked = false;
            bool spike = false;
            try
            {
                List<Unit> all = UnitRegistry.allUnits;
                if (all == null)
                    return;
                FactionHQ hq = null;
                try { GameManager.GetLocalHQ(out hq); }
                catch { }
                for (int i = 0; i < all.Count; i++)
                {
                    Unit u = all[i];
                    if (u == null || u.disabled)
                        continue;
                    Missile mis = u as Missile;
                    if (mis != null)
                    {
                        bool mine = false;
                        try
                        {
                            PersistentID tid = mis.targetID;
                            if (myId.IsValid && tid.IsValid && tid == myId)
                                mine = true;
                        }
                        catch { }
                        Vector3 to = me - mis.transform.position;
                        float d2 = to.sqrMagnitude;
                        if (!mine && d2 < 2200f * 2200f
                            && Vector3.Angle(mis.transform.forward, to) < 24f)
                            mine = true;
                        if (mine)
                        {
                            if (d2 < 900f * 900f)
                                locked = true;
                            else
                                spike = true;
                        }
                        continue;
                    }
                    Aircraft ac = u as Aircraft;
                    if (ac == null)
                        continue;
                    if (hq != null)
                    {
                        try
                        {
                            if (ac.NetworkHQ != null
                                && object.ReferenceEquals(ac.NetworkHQ, hq))
                                continue;
                        }
                        catch { }
                    }
                    Vector3 toAc = me - ac.transform.position;
                    float dist = toAc.magnitude;
                    if (dist < 40f || dist > 3200f)
                        continue;
                    float ang = Vector3.Angle(ac.transform.forward, toAc);
                    if (ang < 5.5f)
                        spike = true;
                    if (ang < 2.8f && dist < 1600f)
                        locked = true;
                }
            }
            catch { }

            if (locked)
                LockUntil = Time.unscaledTime + 0.55f;
            else if (spike)
                SpikeUntil = Time.unscaledTime + 0.45f;
        }

        private static void DrawLine(float y, string text, Color c)
        {
            float w = 280f;
            float x = Screen.width * 0.5f - w * 0.5f;
            Color prev = GUI.color;
            GUI.color = new Color(c.r * 0.15f, c.g * 0.08f, c.b * 0.05f, 0.62f);
            GUI.DrawTexture(new Rect(x, y, w, 24f), _px);
            GUI.color = c;
            GUI.Label(new Rect(x, y, w, 24f), text, _big);
            GUI.color = prev;
        }

        private static void Ensure()
        {
            if (_styles)
                return;
            _styles = true;
            _big = new GUIStyle(GUI.skin.label);
            _big.fontSize = 18;
            _big.fontStyle = FontStyle.Bold;
            _big.alignment = TextAnchor.MiddleCenter;
            _big.normal.textColor = Color.white;
            _tiny = new GUIStyle(GUI.skin.label);
            _tiny.fontSize = 10;
            _tiny.alignment = TextAnchor.MiddleCenter;
            _tiny.normal.textColor = Color.white;
            _px = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _px.SetPixel(0, 0, Color.white);
            _px.Apply();
        }
    }
}
