using System;
using System.Collections.Generic;
using NuclearOption.SavedMission;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Map landmark for radio reports. Fills {L} with an airfield, factory, or
    /// compass relative to a named place — never "that jet" / "shadow".
    /// </summary>
    internal static class RadioMapPlace
    {
        private const float CacheTtl = 2.8f;
        private const float AirbaseMaxM = 16000f;
        private const float LandmarkMaxM = 9000f;

        private static float _cacheAt = -99f;
        private static Airbase[] _airbases;
        private static List<Unit> _marks;

        internal static string Describe(Unit around)
        {
            bool zh = UiLang.IsChinese;
            Vector3 pos = Vector3.zero;
            bool airborne = around is Aircraft;
            if (around != null)
            {
                try { pos = around.transform.position; }
                catch { }
            }
            Refresh();
            string named = FromAirbase(pos, airborne, zh);
            if (!string.IsNullOrEmpty(named))
                return named;
            named = FromLandmark(pos, airborne, zh);
            if (!string.IsNullOrEmpty(named))
                return named;
            named = FromGrid(pos, zh);
            if (!string.IsNullOrEmpty(named))
                return named;
            return zh ? "本空域" : "this sector";
        }

        private static void Refresh()
        {
            float now = Time.unscaledTime;
            if (now - _cacheAt < CacheTtl && _airbases != null)
                return;
            _cacheAt = now;
            try { _airbases = UnityEngine.Object.FindObjectsOfType<Airbase>(); }
            catch { _airbases = null; }
            if (_marks == null)
                _marks = new List<Unit>(32);
            else
                _marks.Clear();
            List<Unit> all = null;
            try { all = UnitRegistry.allUnits; }
            catch { }
            if (all == null)
                return;
            int n = all.Count;
            if (n > 400)
                n = 400;
            for (int i = 0; i < n; i++)
            {
                Building b = all[i] as Building;
                if (b == null)
                    continue;
                try
                {
                    if (b.disabled)
                        continue;
                }
                catch { }
                if (IsLandmark(b))
                    _marks.Add(b);
                if (_marks.Count >= 48)
                    break;
            }
        }

        private static string FromAirbase(Vector3 pos, bool airborne, bool zh)
        {
            if (_airbases == null)
                return null;
            Airbase best = null;
            float bestSq = AirbaseMaxM * AirbaseMaxM;
            for (int i = 0; i < _airbases.Length; i++)
            {
                Airbase ab = _airbases[i];
                if (ab == null)
                    continue;
                try
                {
                    if (ab.disabled)
                        continue;
                }
                catch { }
                Vector3 ap = AirbasePos(ab);
                float sq = (ap - pos).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = ab;
                }
            }
            if (best == null)
                return null;
            string name = AirbaseLabel(best, zh);
            if (string.IsNullOrEmpty(name))
                return null;
            return Relative(name, AirbasePos(best), pos, airborne, zh);
        }

        private static string FromLandmark(Vector3 pos, bool airborne, bool zh)
        {
            if (_marks == null || _marks.Count == 0)
                return null;
            Unit best = null;
            float bestSq = LandmarkMaxM * LandmarkMaxM;
            for (int i = 0; i < _marks.Count; i++)
            {
                Unit u = _marks[i];
                if (u == null)
                    continue;
                Vector3 p;
                try { p = u.transform.position; }
                catch { continue; }
                float sq = (p - pos).sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = u;
                }
            }
            if (best == null)
                return null;
            string name = LandmarkLabel(best, zh);
            if (string.IsNullOrEmpty(name))
                return null;
            Vector3 mp;
            try { mp = best.transform.position; }
            catch { return name; }
            return Relative(name, mp, pos, airborne, zh);
        }

        private static string FromGrid(Vector3 pos, bool zh)
        {
            try
            {
                GlobalPosition gp = pos.ToGlobalPosition();
                int gx = 0;
                int gy = 0;
                if (BattlefieldGrid.TryGetGridXY(gp, out gx, out gy))
                {
                    if (zh)
                        return "网格" + gx.ToString() + "-" + gy.ToString();
                    return "grid " + gx.ToString() + "-" + gy.ToString();
                }
            }
            catch { }
            return null;
        }

        private static string Relative(string name, Vector3 mark, Vector3 pos, bool airborne, bool zh)
        {
            Vector3 delta = pos - mark;
            delta.y = 0f;
            float dist = delta.magnitude;
            if (dist < 1600f)
            {
                if (zh)
                    return name + (airborne ? "上空" : "附近");
                return (airborne ? "over " : "near ") + name;
            }
            string dir = Compass8(delta, zh);
            if (zh)
                return name + dir + "面";
            return dir + " of " + name;
        }

        private static string Compass8(Vector3 delta, bool zh)
        {
            float ang = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
            if (ang < 0f)
                ang += 360f;
            int i = Mathf.RoundToInt(ang / 45f);
            if (i < 0)
                i = 0;
            i = i % 8;
            if (zh)
            {
                if (i == 0) return "北";
                if (i == 1) return "东北";
                if (i == 2) return "东";
                if (i == 3) return "东南";
                if (i == 4) return "南";
                if (i == 5) return "西南";
                if (i == 6) return "西";
                return "西北";
            }
            if (i == 0) return "north";
            if (i == 1) return "northeast";
            if (i == 2) return "east";
            if (i == 3) return "southeast";
            if (i == 4) return "south";
            if (i == 5) return "southwest";
            if (i == 6) return "west";
            return "northwest";
        }

        private static Vector3 AirbasePos(Airbase ab)
        {
            try
            {
                if (ab != null && ab.center != null)
                    return ab.center.position;
            }
            catch { }
            try
            {
                if (ab != null)
                    return ab.transform.position;
            }
            catch { }
            return Vector3.zero;
        }

        private static string AirbaseLabel(Airbase ab, bool zh)
        {
            try
            {
                SavedAirbase saved = ab.SavedAirbase;
                if (saved != null && !string.IsNullOrEmpty(saved.DisplayName)
                    && !GenericName(saved.DisplayName))
                    return saved.DisplayName.Trim();
            }
            catch { }
            bool carrier = false;
            try { carrier = AirbaseLocator.IsCarrierAirbase(ab); }
            catch { }
            string n = null;
            try { n = AirbaseLocator.FormatAirbaseName(ab, carrier); }
            catch { }
            if (string.IsNullOrEmpty(n) || GenericName(n))
            {
                if (carrier)
                    return zh ? "航母" : "the carrier";
                return zh ? "机场" : "the airfield";
            }
            return n.Replace("(Clone)", "").Trim();
        }

        private static bool GenericName(string n)
        {
            if (string.IsNullOrEmpty(n))
                return true;
            string u = n.Replace(" ", "");
            if (u.StartsWith("airbase", StringComparison.OrdinalIgnoreCase))
                return true;
            if (u.IndexOf("Airbase_", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (u == "?" || u == "BASE")
                return true;
            return false;
        }

        private static bool IsLandmark(Building b)
        {
            string n = RawName(b);
            if (string.IsNullOrEmpty(n))
                return false;
            string u = n.ToLowerInvariant();
            return u.IndexOf("refinery") >= 0
                || u.IndexOf("factory") >= 0
                || u.IndexOf("radar") >= 0
                || u.IndexOf("harbor") >= 0
                || u.IndexOf("harbour") >= 0
                || u.IndexOf("port") >= 0
                || u.IndexOf("tower") >= 0
                || u.IndexOf("power") >= 0
                || u.IndexOf("fuel") >= 0
                || u.IndexOf("bridge") >= 0
                || u.IndexOf("dock") >= 0
                || n.IndexOf("炼油") >= 0
                || n.IndexOf("工厂") >= 0
                || n.IndexOf("雷达") >= 0
                || n.IndexOf("港口") >= 0
                || n.IndexOf("码头") >= 0
                || n.IndexOf("塔台") >= 0
                || n.IndexOf("电厂") >= 0
                || n.IndexOf("油库") >= 0
                || n.IndexOf("桥梁") >= 0;
        }

        private static string LandmarkLabel(Unit u, bool zh)
        {
            string n = RawName(u);
            if (string.IsNullOrEmpty(n))
                return zh ? "地标" : "the landmark";
            string l = n.ToLowerInvariant();
            if (l.IndexOf("refinery") >= 0 || n.IndexOf("炼油") >= 0)
                return zh ? "炼油厂" : "the refinery";
            if (l.IndexOf("factory") >= 0 || n.IndexOf("工厂") >= 0)
                return zh ? "工厂" : "the factory";
            if (l.IndexOf("radar") >= 0 || n.IndexOf("雷达") >= 0)
                return zh ? "雷达站" : "the radar site";
            if (l.IndexOf("harbor") >= 0 || l.IndexOf("harbour") >= 0
                || l.IndexOf("port") >= 0 || l.IndexOf("dock") >= 0
                || n.IndexOf("港口") >= 0 || n.IndexOf("码头") >= 0)
                return zh ? "港口" : "the harbor";
            if (l.IndexOf("tower") >= 0 || n.IndexOf("塔台") >= 0)
                return zh ? "塔台" : "the tower";
            if (l.IndexOf("power") >= 0 || n.IndexOf("电厂") >= 0)
                return zh ? "电厂" : "the power plant";
            if (l.IndexOf("fuel") >= 0 || n.IndexOf("油库") >= 0)
                return zh ? "油库" : "the fuel depot";
            if (l.IndexOf("bridge") >= 0 || n.IndexOf("桥梁") >= 0 || n.IndexOf("桥") >= 0)
                return zh ? "大桥" : "the bridge";
            return n.Replace("(Clone)", "").Trim();
        }

        private static string RawName(Unit u)
        {
            if (u == null)
                return "";
            try
            {
                if (u.definition != null && !string.IsNullOrEmpty(u.definition.unitName))
                    return u.definition.unitName;
            }
            catch { }
            try
            {
                if (!string.IsNullOrEmpty(u.unitName))
                    return u.unitName;
            }
            catch { }
            try
            {
                if (!string.IsNullOrEmpty(u.name))
                    return u.name;
            }
            catch { }
            return "";
        }
    }
}
