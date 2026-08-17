using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield airbase / carrier deck locator (0.0.9.57).
    /// Pure acquisition — callers own runway request state.
    /// </summary>
    internal static class AirbaseLocator
    {
        internal static Airbase Resolve(Aircraft ac, bool carrierOnly, Airbase preferred)
        {
            try
            {
                FactionHQ hq = null;
                try { hq = ac != null ? ac.NetworkHQ : null; }
                catch { hq = null; }
                if (hq == null)
                {
                    try { GameManager.GetLocalHQ(out hq); }
                    catch { }
                }

                AircraftParameters parms = null;
                try { parms = ac != null ? ac.GetAircraftParameters() : null; }
                catch { }

                if (!carrierOnly)
                {
                    if (hq == null)
                        return null;
                    if (preferred != null && !preferred.disabled)
                    {
                        bool preferOk = false;
                        try
                        {
                            if (!IsCarrierAirbase(preferred) && !preferred.AttachedAirbase)
                                preferOk = preferred.IsSuitable(BuildLandQuery(ac, parms));
                        }
                        catch { preferOk = false; }
                        if (preferOk)
                            return preferred;
                    }

                    RunwayQuery q = BuildLandQuery(ac, parms);
                    Airbase bestLand = null;
                    float bestLandD = float.MaxValue;
                    Airbase bestAny = null;
                    float bestAnyD = float.MaxValue;
                    foreach (Airbase ab in hq.GetAirbases())
                    {
                        if (ab == null || ab.disabled)
                            continue;
                        if (!ab.IsSuitable(q))
                            continue;
                        Vector3 p = ab.center != null ? ab.center.position : ab.transform.position;
                        float d = (p - ac.transform.position).sqrMagnitude;
                        if (d < bestAnyD)
                        {
                            bestAnyD = d;
                            bestAny = ab;
                        }
                        if (!ab.AttachedAirbase && !IsCarrierAirbase(ab) && d < bestLandD)
                        {
                            bestLandD = d;
                            bestLand = ab;
                        }
                    }
                    return bestLand != null ? bestLand : bestAny;
                }

                if (preferred != null && !preferred.disabled && IsCarrierAirbase(preferred))
                    return preferred;

                Vector3 from = ac != null ? ac.transform.position : Vector3.zero;
                Airbase deck = FindNearestCarrierAirbase(hq, from, ac, parms);
                if (deck != null && Plugin.Log != null)
                    Plugin.Log.LogInfo("LAND CV deck: " + FormatAirbaseName(deck, true));
                else if (Plugin.Log != null)
                    Plugin.Log.LogWarning("LAND CV: no carrier deck found (scene scan failed)");
                return deck;
            }
            catch
            {
                return null;
            }
        }

        internal static Airbase FindNearestCarrierAirbase(FactionHQ hq, Vector3 from,
            Aircraft ac, AircraftParameters parms)
        {
            Airbase bestFriendly = null;
            float bestFriendlyD = float.MaxValue;
            Airbase bestAny = null;
            float bestAnyD = float.MaxValue;

            try
            {
                if (hq != null)
                {
                    foreach (Airbase ab in hq.GetAirbases())
                        ConsiderCarrierCandidate(ab, from, hq, ref bestFriendly, ref bestFriendlyD,
                            ref bestAny, ref bestAnyD);
                }
            }
            catch { }

            try
            {
                if (hq != null)
                {
                    RunwayQuery loose = BuildCarrierLandQuery(ac, parms);
                    Airbase near = hq.GetNearestAirbase(from, loose);
                    ConsiderCarrierCandidate(near, from, hq, ref bestFriendly, ref bestFriendlyD,
                        ref bestAny, ref bestAnyD);
                    Airbase near2 = hq.GetNearestAirbase(from, float.MaxValue, default(RunwayQuery));
                    ConsiderCarrierCandidate(near2, from, hq, ref bestFriendly, ref bestFriendlyD,
                        ref bestAny, ref bestAnyD);
                }
            }
            catch { }

            try
            {
                if (hq != null)
                {
                    Ship ship;
                    float dist;
                    if (hq.TryGetNearestShip(from.ToGlobalPosition(), out ship, out dist) && ship != null
                        && IsCarrierShip(ship))
                    {
                        Airbase deck = FindAirbaseForShip(ship);
                        ConsiderCarrierCandidate(deck, from, hq, ref bestFriendly, ref bestFriendlyD,
                            ref bestAny, ref bestAnyD);
                    }
                }
            }
            catch { }

            try
            {
                List<Unit> units = UnitRegistry.allUnits;
                if (units != null)
                {
                    for (int i = 0; i < units.Count; i++)
                    {
                        Ship ship = units[i] as Ship;
                        if (ship == null || !IsCarrierShip(ship))
                            continue;
                        try
                        {
                            if (ship.disabled)
                                continue;
                        }
                        catch { }
                        Airbase deck = FindAirbaseForShip(ship);
                        ConsiderCarrierCandidate(deck, from, hq, ref bestFriendly, ref bestFriendlyD,
                            ref bestAny, ref bestAnyD);
                    }
                }
            }
            catch { }

            try
            {
                Airbase[] all = UnityEngine.Object.FindObjectsOfType<Airbase>();
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                        ConsiderCarrierCandidate(all[i], from, hq, ref bestFriendly, ref bestFriendlyD,
                            ref bestAny, ref bestAnyD);
                }
            }
            catch { }

            if (bestFriendly != null)
                return bestFriendly;
            return bestAny;
        }

        private static void ConsiderCarrierCandidate(Airbase ab, Vector3 from, FactionHQ preferHq,
            ref Airbase bestFriendly, ref float bestFriendlyD,
            ref Airbase bestAny, ref float bestAnyD)
        {
            if (ab == null || ab.disabled || !IsCarrierAirbase(ab))
                return;
            Vector3 p;
            try { p = ab.center != null ? ab.center.position : ab.transform.position; }
            catch { return; }
            float d = (p - from).sqrMagnitude;
            if (d < bestAnyD)
            {
                bestAnyD = d;
                bestAny = ab;
            }
            bool friendly = false;
            try
            {
                if (preferHq != null && ab.CurrentHQ != null
                    && object.ReferenceEquals(ab.CurrentHQ, preferHq))
                    friendly = true;
            }
            catch { }
            if (friendly && d < bestFriendlyD)
            {
                bestFriendlyD = d;
                bestFriendly = ab;
            }
        }

        internal static Airbase FindAirbaseForShip(Ship ship)
        {
            if (ship == null)
                return null;
            try
            {
                Airbase[] kids = ship.GetComponentsInChildren<Airbase>(true);
                if (kids != null)
                {
                    for (int i = 0; i < kids.Length; i++)
                    {
                        if (kids[i] != null && !kids[i].disabled)
                            return kids[i];
                    }
                }
            }
            catch { }

            try
            {
                Airbase onShip = ship.GetComponent<Airbase>();
                if (onShip != null && !onShip.disabled)
                    return onShip;
            }
            catch { }

            try
            {
                Airbase[] all = UnityEngine.Object.FindObjectsOfType<Airbase>();
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        Airbase ab = all[i];
                        if (ab == null || ab.disabled)
                            continue;
                        Unit u;
                        if (ab.TryGetAttachedUnit(out u) && object.ReferenceEquals(u, ship))
                            return ab;
                        if (NameLooksLikeCarrier(ab.name)
                            && ab.transform != null
                            && (ab.transform.IsChildOf(ship.transform)
                                || ship.transform.IsChildOf(ab.transform)
                                || (ab.transform.position - ship.transform.position).sqrMagnitude < 250000f))
                            return ab;
                    }
                }
            }
            catch { }
            return null;
        }

        internal static bool IsCarrierAirbase(Airbase ab)
        {
            if (ab == null)
                return false;
            try
            {
                Unit u;
                if (ab.TryGetAttachedUnit(out u))
                {
                    Ship ship = u as Ship;
                    if (IsCarrierShip(ship))
                        return true;
                }
            }
            catch { }

            try
            {
                Ship parent = ab.GetComponentInParent<Ship>();
                if (IsCarrierShip(parent))
                    return true;
            }
            catch { }

            if (NameLooksLikeCarrier(ab.name))
                return true;

            try
            {
                if (ab.AttachedAirbase && NameLooksLikeCarrier(ab.name))
                    return true;
            }
            catch { }
            return false;
        }

        internal static bool IsHyperionAirbase(Airbase ab)
        {
            if (ab == null)
                return false;
            try
            {
                Unit u;
                if (ab.TryGetAttachedUnit(out u))
                {
                    Ship ship = u as Ship;
                    if (IsHyperionShip(ship))
                        return true;
                }
            }
            catch { }

            try
            {
                Ship parent = ab.GetComponentInParent<Ship>();
                if (IsHyperionShip(parent))
                    return true;
            }
            catch { }

            try
            {
                if (NameLooksLikeHyperion(ab.name))
                    return true;
            }
            catch { }
            return false;
        }

        internal static bool IsHyperionShip(Ship ship)
        {
            if (ship == null)
                return false;
            try
            {
                UnitDefinition ud = ship.definition;
                if (ud != null)
                {
                    string k = ud.jsonKey;
                    if (!string.IsNullOrEmpty(k))
                    {
                        if (k.IndexOf("AssaultCarrier", StringComparison.OrdinalIgnoreCase) >= 0)
                            return false;
                        if (k.IndexOf("FleetCarrier", StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
                    }
                    if (!string.IsNullOrEmpty(ud.unitName)
                        && ud.unitName.IndexOf("Hyperion", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                    ShipDefinition sd = ud as ShipDefinition;
                    if (sd != null)
                    {
                        if (sd.shipType == ShipType.LHA)
                            return false;
                        if (sd.shipType == ShipType.CV)
                            return true;
                    }
                }
            }
            catch { }
            try
            {
                string n = ship.name;
                if (!string.IsNullOrEmpty(n))
                {
                    if (n.IndexOf("AssaultCarrier", StringComparison.OrdinalIgnoreCase) >= 0)
                        return false;
                    if (NameLooksLikeHyperion(n))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static bool NameLooksLikeHyperion(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            return name.IndexOf("FleetCarrier", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Hyperion", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        internal static bool IsAircraftOnCarrier(Aircraft ac)
        {
            if (ac == null)
                return false;
            try
            {
                Ship parent = ac.GetComponentInParent<Ship>();
                if (IsCarrierShip(parent))
                    return true;
            }
            catch { }

            Vector3 from;
            try { from = ac.transform.position; }
            catch { return false; }

            const float deckRangeSq = 360000f;
            try
            {
                List<Unit> units = UnitRegistry.allUnits;
                if (units != null)
                {
                    for (int i = 0; i < units.Count; i++)
                    {
                        Ship ship = units[i] as Ship;
                        if (ship == null || !IsCarrierShip(ship))
                            continue;
                        try
                        {
                            if (ship.disabled)
                                continue;
                        }
                        catch { }
                        try
                        {
                            if ((ship.transform.position - from).sqrMagnitude < deckRangeSq)
                                return true;
                        }
                        catch { }
                    }
                }
            }
            catch { }

            try
            {
                FactionHQ hq = null;
                try { hq = ac.NetworkHQ; }
                catch { hq = null; }
                if (hq == null)
                {
                    try { GameManager.GetLocalHQ(out hq); }
                    catch { }
                }
                Airbase deck = FindNearestCarrierAirbase(hq, from, ac, null);
                if (deck != null)
                {
                    Vector3 p = deck.center != null ? deck.center.position : deck.transform.position;
                    if ((p - from).sqrMagnitude < deckRangeSq)
                        return true;
                }
            }
            catch { }
            return false;
        }

        internal static bool IsCarrierShip(Ship ship)
        {
            if (ship == null)
                return false;
            try
            {
                UnitDefinition ud = ship.definition;
                if (ud != null && !string.IsNullOrEmpty(ud.jsonKey))
                {
                    string k = ud.jsonKey;
                    if (k.IndexOf("FleetCarrier", StringComparison.OrdinalIgnoreCase) >= 0
                        || k.IndexOf("AssaultCarrier", StringComparison.OrdinalIgnoreCase) >= 0
                        || string.Equals(k, "FleetCarrier1", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(k, "AssaultCarrier1", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                ShipDefinition sd = ud as ShipDefinition;
                if (sd != null && (sd.shipType == ShipType.CV || sd.shipType == ShipType.LHA))
                    return true;
            }
            catch { }
            try
            {
                if (NameLooksLikeCarrier(ship.name))
                    return true;
            }
            catch { }
            return false;
        }

        internal static bool NameLooksLikeCarrier(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            return name.IndexOf("FleetCarrier", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("AssaultCarrier", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Hyperion", StringComparison.OrdinalIgnoreCase) >= 0
                || (name.IndexOf("Carrier", StringComparison.OrdinalIgnoreCase) >= 0
                    && name.IndexOf("airbase_", StringComparison.OrdinalIgnoreCase) < 0
                    && name.IndexOf("navalbase", StringComparison.OrdinalIgnoreCase) < 0);
        }

        internal static string FormatAirbaseName(Airbase ab, bool carrierMode)
        {
            string n = "BASE";
            try
            {
                if (ab != null && !string.IsNullOrEmpty(ab.name))
                    n = ab.name.Replace("(Clone)", "").Trim();
            }
            catch { }
            if (!carrierMode)
                return n;
            try
            {
                Unit u;
                if (ab != null && ab.TryGetAttachedUnit(out u) && u != null)
                {
                    string sn = u.name;
                    if (!string.IsNullOrEmpty(sn))
                        n = sn.Replace("(Clone)", "").Trim();
                    try
                    {
                        if (u.definition != null && !string.IsNullOrEmpty(u.definition.unitName))
                            n = u.definition.unitName;
                    }
                    catch { }
                }
            }
            catch { }
            if (n.IndexOf("CV", StringComparison.OrdinalIgnoreCase) < 0
                && n.IndexOf("Carrier", StringComparison.OrdinalIgnoreCase) < 0
                && n.IndexOf("航母", StringComparison.OrdinalIgnoreCase) < 0)
                n = "CV " + n;
            return n;
        }

        internal static RunwayQuery BuildLandQuery(Aircraft ac, AircraftParameters parms)
        {
            RunwayQuery q = new RunwayQuery();
            q.RunwayType = RunwayQueryType.Landing;
            bool vert = parms != null && parms.verticalLanding;
            float takeoff = parms != null ? parms.takeoffDistance : 800f;
            float land = parms != null ? parms.landingSpeed : 70f;
            q.MinSize = vert ? (ac != null && ac.definition != null ? ac.definition.length : 20f) : takeoff;
            q.LandingSpeed = vert ? 0f : land;
            q.TailHook = false;
            try
            {
                if (ac != null && ac.weaponManager != null)
                    q.TailHook = ac.weaponManager.HasTailHook();
            }
            catch { }
            return q;
        }

        internal static RunwayQuery BuildCarrierLandQuery(Aircraft ac, AircraftParameters parms)
        {
            RunwayQuery q = new RunwayQuery();
            q.RunwayType = RunwayQueryType.Landing;
            float len = 20f;
            try
            {
                if (ac != null && ac.definition != null)
                    len = Mathf.Max(12f, ac.definition.length);
            }
            catch { }
            q.MinSize = len;
            q.LandingSpeed = parms != null ? parms.landingSpeed : 70f;
            q.TailHook = false;
            try
            {
                if (ac != null && ac.weaponManager != null)
                    q.TailHook = ac.weaponManager.HasTailHook();
            }
            catch { }
            return q;
        }
    }
}
