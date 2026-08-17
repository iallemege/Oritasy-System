using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>Thin facade — acquisition lives in AirbaseLocator (0.0.9.57+).</summary>
    internal static partial class PlayerAutopilot
    {
        internal static void ResolveLand(Aircraft ac, bool carrierOnly)
        {
            _hasRunway = false;
            _landBase = null;
            _reachedApproach = false;
            _vtolHovering = false;
            _cvLeg = CvPatternLeg.None;
            _cvLegWatch = CvPatternLeg.None;
            _cvPatSide = 1f;
            _landBase = AirbaseLocator.Resolve(ac, carrierOnly, _preferredLandBase);
        }

        internal static bool IsCarrierAirbase(Airbase ab)
        {
            return AirbaseLocator.IsCarrierAirbase(ab);
        }

        internal static bool IsCarrierShip(Ship ship)
        {
            return AirbaseLocator.IsCarrierShip(ship);
        }

        internal static string FormatAirbaseName(Airbase ab, bool carrierMode)
        {
            return AirbaseLocator.FormatAirbaseName(ab, carrierMode);
        }

        internal static bool UiZh()
        {
            return UiLang.IsChinese;
        }

        internal static RunwayQuery BuildLandQuery(Aircraft ac, AircraftParameters parms)
        {
            return AirbaseLocator.BuildLandQuery(ac, parms);
        }

        internal static RunwayQuery BuildCarrierLandQuery(Aircraft ac, AircraftParameters parms)
        {
            return AirbaseLocator.BuildCarrierLandQuery(ac, parms);
        }
    }
}
