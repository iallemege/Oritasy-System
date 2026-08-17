using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>Thin facade — landing geometry lives in LandingGuidance (0.0.9.58).</summary>
    internal static partial class PlayerAutopilot
    {
        private static void ApplyLanding(Aircraft ac, Autopilot ap, ControlInputs inputs,
            AircraftParameters parms, float landSpd, float turnR, float cruise)
        {
            LandingGuidance.Apply(ac, ap, inputs, parms, landSpd, turnR, cruise);
        }

        internal static void ForceCarrierNormalFlight(ControlInputs inputs)
        {
            LandingGuidance.ForceCarrierNormalFlight(inputs);
        }
    }
}
