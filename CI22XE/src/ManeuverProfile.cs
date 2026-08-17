using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace Oritasy
{
    /// <summary>Per-airframe (jsonKey) tunable profile — each aircraft type is independent.</summary>
    internal sealed class ManeuverProfile
    {
        public string Key;
        public string DisplayLabel;
        public ConfigEntry<float> AircraftG;
        public ConfigEntry<float> PilotG;
        public ConfigEntry<float> PilotStrength;
        public ConfigEntry<float> MaxSpeed;
        public ConfigEntry<float> CornerSpeed;
        public ConfigEntry<float> PitchMul;
        public ConfigEntry<float> RollMul;
        public ConfigEntry<float> AlphaMul;
        public ConfigEntry<float> ThrustMul;
        public ConfigEntry<float> FuelBurnMul;
        public ConfigEntry<float> FuelCapMul;
        public ConfigEntry<float> ApproachSpeed;
        public ConfigEntry<float> LandingSpeed;
        public ConfigEntry<float> TakeoffSpeed;
        public ConfigEntry<float> TurningRadius;
        public float BaselinePitch;
        public float BaselineRoll;
        public float BaselineAlpha;
        public float DefaultAircraftG;
        public float DefaultPilotG;
        public float DefaultPilotStrength;
        public float DefaultMaxSpeed;
        public float DefaultCornerSpeed;
        public float DefaultPitchMul;
        public float DefaultRollMul;
        public float DefaultAlphaMul;
        public float DefaultThrustMul;
        public float DefaultFuelBurnMul;
        public float DefaultFuelCapMul;
        public float DefaultApproachSpeed;
        public float DefaultLandingSpeed;
        public float DefaultTakeoffSpeed;
        public float DefaultTurningRadius;

        public static ManeuverProfile Create(
            Plugin plugin,
            string key,
            string label,
            float defG,
            float defPilotG,
            float defStrength,
            float defMax,
            float defCorner,
            float defPitchMul,
            float defRollMul,
            float defAlphaMul,
            float defThrust,
            float defFuelBurn,
            float defFuelCap,
            float defApproach,
            float defLanding,
            float defTakeoff,
            float defTurnRadius)
        {
            string section = "Maneuver." + ManeuverProfileMathService.SanitizeSection(key);
            ManeuverProfile p = new ManeuverProfile();
            p.Key = key;
            p.DisplayLabel = label;
            p.DefaultAircraftG = defG;
            p.DefaultPilotG = defPilotG;
            p.DefaultPilotStrength = defStrength;
            p.DefaultMaxSpeed = defMax;
            p.DefaultCornerSpeed = defCorner;
            p.DefaultPitchMul = defPitchMul;
            p.DefaultRollMul = defRollMul;
            p.DefaultAlphaMul = defAlphaMul;
            p.DefaultThrustMul = defThrust;
            p.DefaultFuelBurnMul = defFuelBurn;
            p.DefaultFuelCapMul = defFuelCap;
            p.DefaultApproachSpeed = defApproach;
            p.DefaultLandingSpeed = defLanding;
            p.DefaultTakeoffSpeed = defTakeoff;
            p.DefaultTurningRadius = defTurnRadius;
            p.BaselinePitch = 0f;
            p.BaselineRoll = 0f;
            p.BaselineAlpha = 0f;
            p.AircraftG = plugin.Config.Bind(section, "AircraftGLimit", defG, "Airframe / FBW G limit");
            p.PilotG = plugin.Config.Bind(section, "PilotGLimit", defPilotG, "Pilot maxG");
            p.PilotStrength = plugin.Config.Bind(section, "PilotStrength", defStrength, "Pilot strength");
            p.MaxSpeed = plugin.Config.Bind(section, "MaxSpeed", defMax, "Max speed m/s");
            p.CornerSpeed = plugin.Config.Bind(section, "CornerSpeed", defCorner, "Corner / FBW corner speed m/s");
            p.PitchMul = plugin.Config.Bind(section, "PitchRateMultiplier", defPitchMul,
                "FBW maxPitchAngularVel multiplier");
            p.RollMul = plugin.Config.Bind(section, "RollRateMultiplier", defRollMul,
                "FBW maxRollAngularVel multiplier");
            p.AlphaMul = plugin.Config.Bind(section, "AlphaLimiterMultiplier", defAlphaMul,
                "FBW alphaLimiter multiplier");
            p.ThrustMul = plugin.Config.Bind(section, "ThrustMultiplier", defThrust, "Engine thrust / power multiplier");
            p.FuelBurnMul = plugin.Config.Bind(section, "FuelBurnMultiplier", defFuelBurn,
                "Fuel consumption multiplier (lower = longer endurance)");
            p.FuelCapMul = plugin.Config.Bind(section, "FuelCapacityMultiplier", defFuelCap,
                "Internal / tank fuel capacity multiplier");
            p.ApproachSpeed = plugin.Config.Bind(section, "ApproachSpeed", defApproach, "Approach speed m/s");
            p.LandingSpeed = plugin.Config.Bind(section, "LandingSpeed", defLanding, "Landing speed m/s");
            p.TakeoffSpeed = plugin.Config.Bind(section, "TakeoffSpeed", defTakeoff, "Takeoff speed m/s");
            p.TurningRadius = plugin.Config.Bind(section, "TurningRadius", defTurnRadius, "Turning radius");
            return p;
        }

        public void ResetToDefaults()
        {
            AircraftG.Value = DefaultAircraftG;
            PilotG.Value = DefaultPilotG;
            PilotStrength.Value = DefaultPilotStrength;
            MaxSpeed.Value = DefaultMaxSpeed;
            CornerSpeed.Value = DefaultCornerSpeed;
            PitchMul.Value = DefaultPitchMul;
            RollMul.Value = DefaultRollMul;
            AlphaMul.Value = DefaultAlphaMul;
            ThrustMul.Value = DefaultThrustMul;
            FuelBurnMul.Value = DefaultFuelBurnMul;
            FuelCapMul.Value = DefaultFuelCapMul;
            ApproachSpeed.Value = DefaultApproachSpeed;
            LandingSpeed.Value = DefaultLandingSpeed;
            TakeoffSpeed.Value = DefaultTakeoffSpeed;
            TurningRadius.Value = DefaultTurningRadius;
        }

        public void CopyFromConfig(
            out float aircraftG, out float pilotG, out float pilotStrength,
            out float maxSpeed, out float cornerSpeed,
            out float pitchMul, out float rollMul, out float alphaMul,
            out float thrustMul, out float fuelBurnMul, out float fuelCapMul,
            out float approach, out float landing, out float takeoff, out float turnRadius)
        {
            aircraftG = AircraftG.Value;
            pilotG = PilotG.Value;
            pilotStrength = PilotStrength.Value;
            maxSpeed = MaxSpeed.Value;
            cornerSpeed = CornerSpeed.Value;
            pitchMul = PitchMul.Value;
            rollMul = RollMul.Value;
            alphaMul = AlphaMul.Value;
            thrustMul = ThrustMul.Value;
            fuelBurnMul = FuelBurnMul.Value;
            fuelCapMul = FuelCapMul.Value;
            approach = ApproachSpeed.Value;
            landing = LandingSpeed.Value;
            takeoff = TakeoffSpeed.Value;
            turnRadius = TurningRadius.Value;
        }

        public void WriteToConfig(
            float aircraftG, float pilotG, float pilotStrength,
            float maxSpeed, float cornerSpeed,
            float pitchMul, float rollMul, float alphaMul,
            float thrustMul, float fuelBurnMul, float fuelCapMul,
            float approach, float landing, float takeoff, float turnRadius)
        {
            AircraftG.Value = aircraftG;
            PilotG.Value = pilotG;
            PilotStrength.Value = pilotStrength;
            MaxSpeed.Value = maxSpeed;
            CornerSpeed.Value = cornerSpeed;
            PitchMul.Value = pitchMul;
            RollMul.Value = rollMul;
            AlphaMul.Value = alphaMul;
            ThrustMul.Value = thrustMul;
            FuelBurnMul.Value = fuelBurnMul;
            FuelCapMul.Value = fuelCapMul;
            ApproachSpeed.Value = approach;
            LandingSpeed.Value = landing;
            TakeoffSpeed.Value = takeoff;
            TurningRadius.Value = turnRadius;
        }
    }
}
