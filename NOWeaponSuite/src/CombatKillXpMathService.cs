using System;
using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Combat XP: aircraft hits, module breaks, plus destroy bounties
    /// (missile / ground / navy / carrier).
    /// </summary>
    internal static class CombatKillXpMathService
    {
        internal static int XpPerHit { get { return CareerXpMathService.XpPerHit; } }
        internal static int HitXpCapPerAircraft { get { return CareerXpMathService.HitXpCapPerAircraft; } }
        internal static int XpPerModule { get { return CareerXpMathService.XpPerModule; } }
        internal static int XpPerMissile { get { return CareerXpMathService.XpPerMissile; } }
        internal static int XpPerGround { get { return CareerXpMathService.XpPerGround; } }
        internal static int XpPerNavy { get { return CareerXpMathService.XpPerNavy; } }
        internal static int XpPerCarrier { get { return CareerXpMathService.XpPerCarrier; } }

        internal static int GrantForAircraftHit(int alreadyGranted)
        {
            if (alreadyGranted >= HitXpCapPerAircraft)
                return 0;
            int next = alreadyGranted + XpPerHit;
            if (next > HitXpCapPerAircraft)
                return HitXpCapPerAircraft - alreadyGranted;
            return XpPerHit;
        }

        internal static string FormatFeedSuffix(int xp)
        {
            if (xp <= 0)
                return "";
            string text = " +" + xp;
            return text.AddColor(new Color(0.55f, 1f, 0.42f, 1f));
        }

        /// <summary>Flat XP for finishing a unit. Aircraft stay on hit/module only.</summary>
        internal static int ResolveUnitDestroyXp(Unit victim)
        {
            if (victim == null)
                return 0;
            if (victim is Missile)
                return XpPerMissile;
            if (victim is GroundVehicle || victim is Building)
                return XpPerGround;
            Ship ship = victim as Ship;
            if (ship == null)
                return 0;
            if (IsCarrierShip(ship))
                return XpPerCarrier;
            return XpPerNavy;
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
                string n = ship.name;
                if (!string.IsNullOrEmpty(n)
                    && (n.IndexOf("FleetCarrier", StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf("AssaultCarrier", StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf("Hyperion", StringComparison.OrdinalIgnoreCase) >= 0
                        || (n.IndexOf("Carrier", StringComparison.OrdinalIgnoreCase) >= 0
                            && n.IndexOf("airbase_", StringComparison.OrdinalIgnoreCase) < 0)))
                    return true;
            }
            catch { }
            return false;
        }
    }
}
