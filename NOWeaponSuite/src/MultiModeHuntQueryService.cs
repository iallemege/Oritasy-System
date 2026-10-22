using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// 175C LOAL candidate set: every Unit in range, then MultiModeBrain.ConsiderHuntList
    /// / TBM Consider apply IFF and family filters. Do not pre-drop ships, neutrals, or
    /// unknown Unit subclasses — that changed lock-on versus 175C.
    /// Snapshot is only a cheaper stand-in for BattlefieldGrid.GetUnitsInRangeNonAlloc.
    /// Hunt cadence: TickSlow 0.08s while acquiring, hunt 0.12s, 16/32 queries per frame.
    /// </summary>
    internal static class MultiModeHuntQueryService
    {
        private struct Snap
        {
            public Unit Unit;
            public Vector3 Pos;
            public bool Surface;
        }

        private static readonly List<Snap> Snapshot = new List<Snap>(512);
        private static int _snapFrame = -1;
        private static int _huntFrame = -1;
        private static int _huntUsed;
        private static PropertyInfo _isLowProp;
        private static bool _perfResolved;

        /// <summary>175C packed LOAL: 16 (Low) / 32 hunts per frame so salvos still acquire.</summary>
        internal static bool TryConsume()
        {
            int f = Time.frameCount;
            if (_huntFrame != f)
            {
                _huntFrame = f;
                _huntUsed = 0;
            }
            int cap = IsPerfLow() ? 16 : 32;
            if (_huntUsed >= cap)
                return false;
            _huntUsed++;
            return true;
        }

        private static bool IsPerfLow()
        {
            if (!_perfResolved)
            {
                _perfResolved = true;
                try
                {
                    Type perf = Type.GetType("Oritasy.PerfMode, Oritasy")
                        ?? Type.GetType("Oritasy.PerfMode");
                    if (perf != null)
                    {
                        _isLowProp = perf.GetProperty("IsLow",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    }
                }
                catch { }
            }
            if (_isLowProp == null)
                return false;
            try
            {
                object v = _isLowProp.GetValue(null, null);
                return v is bool && (bool)v;
            }
            catch
            {
                return false;
            }
        }

        internal static void FillHuntUnits(
            Missile missile,
            Vector3 mpos,
            float radius,
            bool surfaceOnly,
            List<Unit> dest)
        {
            if (dest == null)
                return;
            dest.Clear();
            if (missile == null || radius < 1f)
                return;
            EnsureSnapshot();
            float r2 = radius * radius;
            int n = Snapshot.Count;
            for (int i = 0; i < n; i++)
            {
                Snap s = Snapshot[i];
                if (s.Unit == null)
                    continue;
                if (surfaceOnly && !s.Surface)
                    continue;
                Vector3 d = s.Pos - mpos;
                if (d.sqrMagnitude > r2)
                    continue;
                dest.Add(s.Unit);
            }
        }

        private static void EnsureSnapshot()
        {
            int f = Time.frameCount;
            if (_snapFrame == f)
                return;
            _snapFrame = f;
            Snapshot.Clear();
            List<Unit> all = null;
            try { all = UnitRegistry.allUnits; }
            catch { return; }
            if (all == null)
                return;
            int count = all.Count;
            for (int i = 0; i < count; i++)
            {
                Unit u = all[i];
                if (u == null)
                    continue;
                Snap s;
                s.Unit = u;
                try { s.Pos = u.transform.position; }
                catch { continue; }
                s.Surface = u is Building || u is GroundVehicle || u is Ship;
                Snapshot.Add(s);
            }
        }
    }
}
