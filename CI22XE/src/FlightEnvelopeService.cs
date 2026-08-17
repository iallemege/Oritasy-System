using System;
using System.Reflection;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield flight envelope / FBW limits service.
    /// Written from scratch for 0.0.9.56.
    /// </summary>
    internal static class FlightEnvelopeService
    {
        internal static Aircraft ResolveGuiAircraft()
        {
            Aircraft ac = Plugin.ActivePlayerAircraft;
            if (IsUsableXeAircraft(ac))
                return ac;

            try
            {
                if (GameManager.GetLocalAircraft(out ac) && IsUsableXeAircraft(ac))
                {
                    Plugin.ActivePlayerAircraft = ac;
                    AircraftPowerService.RegisterLiveXe(ac);
                    return ac;
                }
            }
            catch { }

            for (int i = 0; i < Plugin.LiveXeAircraft.Count; i++)
            {
                ac = Plugin.LiveXeAircraft[i];
                if (!IsUsableXeAircraft(ac))
                    continue;
                try
                {
                    if (ac.Player != null && Plugin.IsLocalHumanPlayer(ac.Player))
                    {
                        Plugin.ActivePlayerAircraft = ac;
                        return ac;
                    }
                }
                catch { }
            }

            return null;
        }

        internal static bool IsUsableXeAircraft(Aircraft ac)
        {
            return ac != null && Plugin.IsRuntimeInstance(ac) && AircraftIdentity.IsXeAircraft(ac);
        }

        internal static ManeuverProfile GetOrCreateProfile(Aircraft aircraft)
        {
            string key = AircraftIdentity.GetKey(aircraft);
            ManeuverProfile existing;
            if (Plugin.Profiles.TryGetValue(key, out existing))
            {
                if (existing != null && Plugin.PendingFleet114)
                {
                    float th = AircraftIdentity.DefaultThrustMul(key);
                    float fuelCap = existing.DefaultFuelCapMul > 0.05f ? existing.DefaultFuelCapMul : 1f;
                    if (fuelCap > 1.001f)
                        th *= fuelCap;
                    float g = existing.DefaultAircraftG > 0.1f ? existing.DefaultAircraftG : 9f;
                    // Undo G×StrengthMul (COIN 19.5, trainer 27, …).
                    if (g > 12.5f)
                        g = Mathf.Clamp(g / Mathf.Max(1f, AirframeStrengthService.StrengthMul), 4f, 12f);
                    g = Mathf.Clamp(g * 1.1f, 4f, 14f);
                    float pg = Mathf.Clamp(Mathf.Max(g + 2f, 9f), g, 16f);
                    ApplyFleet114Defaults(existing, key, g, pg, th);
                }
                return existing;
            }

            float baseG = 9f;
            float baseMax = 300f;
            float baseCorner = 150f;
            float baseApproach = 80f;
            float baseLanding = 70f;
            float baseTakeoff = 80f;
            float baseTurnRadius = 800f;
            string label = key;
            AircraftDefinition def = aircraft != null ? aircraft.definition as AircraftDefinition : null;
            if (def != null)
            {
                if (!string.IsNullOrEmpty(def.unitName))
                    label = def.unitName;
                else if (!string.IsNullOrEmpty(def.code))
                    label = def.code;
                if (def.aircraftParameters != null)
                {
                    AircraftParameters ap = def.aircraftParameters;
                    if (ap.aircraftGLimit > 0.1f)
                        baseG = ap.aircraftGLimit;
                    if (ap.maxSpeed > 1f)
                        baseMax = ap.maxSpeed;
                    if (ap.cornerSpeed > 1f)
                        baseCorner = ap.cornerSpeed;
                    if (ap.approachSpeed > 1f)
                        baseApproach = ap.approachSpeed;
                    if (ap.landingSpeed > 1f)
                        baseLanding = ap.landingSpeed;
                    if (ap.takeoffSpeed > 1f)
                        baseTakeoff = ap.takeoffSpeed;
                    if (ap.turningRadius > 1f)
                        baseTurnRadius = ap.turningRadius;
                }
            }

            // StrengthMul only hardens joints/impact — do NOT scale G limits by it.
            // ×3 G with unchanged thrust made energy bleed feel like worse T/W.
            float defG = Mathf.Clamp(baseG * 1.1f, 4f, 14f);
            float defPilotG = Mathf.Clamp(Mathf.Max(9f, baseG + 2f) * 1.1f, defG, 16f);
            float defStrength = 1f;
            float defMax = baseMax;
            float defCorner = baseCorner;
            float defPitchMul = 1f;
            float defRollMul = 1f;
            float defAlphaMul = 1f;
            float defFuelBurn = AircraftIdentity.WantsFuelEconomy(aircraft)
                ? (Plugin.FuelMultiplier != null ? Plugin.FuelMultiplier.Value : 0.5f)
                : 1f;
            float defFuelCap = AircraftIdentity.IsCoinAircraft(aircraft)
                ? (Plugin.PayloadMultiplier != null ? Plugin.PayloadMultiplier.Value : 2f)
                : 1f;
            float defThrust = AircraftIdentity.DefaultThrustMul(key);
            // Extra tankage (CI-22) must not tank full-fuel T/W — scale thrust with fuel cap.
            if (defFuelCap > 1.001f)
                defThrust *= defFuelCap;
            float defApproach = baseApproach;
            float defLanding = baseLanding;
            float defTakeoff = baseTakeoff;
            float defTurnRadius = baseTurnRadius;

            if (AircraftIdentity.IsCoinAircraft(aircraft))
            {
                defG = 7f;
                defPilotG = 10f;
                defMax = 175f;
                defCorner = 90f;
                defPitchMul = 1f;
                defApproach = 55f;
                defLanding = 45f;
                defTakeoff = 55f;
            }
            else if (AircraftIdentity.IsFs12(key))
            {
                defPitchMul = 2.2f;
                defCorner = Mathf.Max(80f, baseCorner * 0.7f);
                defG = Mathf.Clamp(Mathf.Max(baseG * 1.15f, baseG + 1f), 4f, 14f);
                defPilotG = Mathf.Clamp(Mathf.Max(defPilotG, defG + 2f), defG, 16f);
            }
            else if (AircraftIdentity.IsSfb(key))
            {
                defPitchMul = 1.5f;
            }
            else if (AircraftIdentity.IsTa30(key))
            {
                // Trainer + thrust: modest G headroom; joints already ×StrengthMul.
                defG = Mathf.Max(defG, 10f);
                defPilotG = Mathf.Max(defPilotG, defG + 2f);
            }

            ManeuverProfile created = ManeuverProfile.Create(
                Plugin.Instance, key, label,
                defG, defPilotG, defStrength, defMax, defCorner,
                defPitchMul, defRollMul, defAlphaMul,
                defThrust, defFuelBurn, defFuelCap,
                defApproach, defLanding, defTakeoff, defTurnRadius);
            Plugin.Profiles[key] = created;
            ApplyFleet114Defaults(created, key, defG, defPilotG, defThrust);
            if (Plugin.DebugLog != null && Plugin.DebugLog.Value)
                Plugin.Log.LogInfo("Maneuver profile: " + key + " pitchMul=" + defPitchMul + " corner=" + defCorner
                    + " thrust=" + defThrust + " g=" + defG);
            return created;
        }

        /// <summary>Push thrust defaults (SFB×8 / AB4×6 / …); revision 120 one-shot doubles saved thrust.</summary>
        private static void ApplyFleet114Defaults(
            ManeuverProfile profile, string key, float defG, float defPilotG, float defThrust)
        {
            if (profile == null)
                return;
            profile.DefaultAircraftG = defG;
            profile.DefaultPilotG = defPilotG;
            profile.DefaultThrustMul = defThrust;
            profile.DefaultPilotStrength = 1f;

            try
            {
                // Keep known presets at least at current defaults.
                if (profile.ThrustMul != null)
                {
                    float t = profile.ThrustMul.Value;
                    if (AircraftIdentity.IsSfb(key) && t < 7.99f)
                        profile.ThrustMul.Value = Mathf.Max(t, 8f);
                    else if (AircraftIdentity.IsAb4(key) && t < 5.99f)
                        profile.ThrustMul.Value = Mathf.Max(t, 6f);
                    else if (t + 0.05f < defThrust)
                        profile.ThrustMul.Value = defThrust;
                }

                if (!Plugin.PendingFleet114)
                    return;

                if (profile.ThrustMul != null)
                {
                    // One-shot ×2 from saved value, never below new aircraft default
                    float doubled = Mathf.Max(profile.ThrustMul.Value * 2f, defThrust);
                    profile.ThrustMul.Value = Mathf.Min(doubled, ManeuverGuiLayoutService.ThrustMulMax);
                }
                if (profile.AircraftG != null)
                    profile.AircraftG.Value = defG;
                if (profile.PilotG != null)
                    profile.PilotG.Value = defPilotG;
                if (profile.PilotStrength != null)
                    profile.PilotStrength.Value = 1f;
            }
            catch { }
        }

        internal static ControlsFilter GetControlsFilter(Aircraft aircraft)
        {
            if (aircraft == null)
                return null;
            ControlsFilter filter = null;
            if (EngineReflection.ControlsFilter != null)
            {
                try { filter = EngineReflection.ControlsFilter.GetValue(aircraft) as ControlsFilter; }
                catch { }
            }
            if (filter == null)
                filter = aircraft.GetComponentInChildren<ControlsFilter>(true);
            return filter;
        }

        internal static void EnsurePitchBaseline(Aircraft aircraft, ManeuverProfile profile)
        {
            if (profile == null || profile.BaselinePitch > 0.01f)
                return;
            ControlsFilter filter = GetControlsFilter(aircraft);
            if (filter == null || EngineReflection.FlyByWire == null || EngineReflection.FbwMaxPitch == null)
                return;
            try
            {
                object fbw = EngineReflection.FlyByWire.GetValue(filter);
                if (fbw == null)
                    return;
                float cur = (float)EngineReflection.FbwMaxPitch.GetValue(fbw);
                if (cur > 0.01f)
                    profile.BaselinePitch = cur;
            }
            catch { }
        }

        internal static void EnsureControlBaselines(Aircraft aircraft, ManeuverProfile profile)
        {
            EnsurePitchBaseline(aircraft, profile);
            if (profile == null)
                return;
            if (profile.BaselineRoll > 0.01f && profile.BaselineAlpha > 0.01f)
                return;
            ControlsFilter filter = GetControlsFilter(aircraft);
            if (filter == null || EngineReflection.FlyByWire == null)
                return;
            try
            {
                object fbw = EngineReflection.FlyByWire.GetValue(filter);
                if (fbw == null)
                    return;
                if (profile.BaselineRoll <= 0.01f && EngineReflection.FbwMaxRollVel != null)
                {
                    float roll = (float)EngineReflection.FbwMaxRollVel.GetValue(fbw);
                    if (roll > 0.01f)
                        profile.BaselineRoll = roll;
                }
                if (profile.BaselineAlpha <= 0.01f && EngineReflection.FbwAlpha != null)
                {
                    float alpha = (float)EngineReflection.FbwAlpha.GetValue(fbw);
                    if (alpha > 0.01f)
                        profile.BaselineAlpha = alpha;
                }
            }
            catch { }
        }

        internal static void ApplyLimitsToAllXe()
        {
            if (Plugin.LiveXeAircraft.Count == 0)
            {
                Aircraft[] all = Resources.FindObjectsOfTypeAll<Aircraft>();
                for (int i = 0; i < all.Length; i++)
                {
                    Aircraft ac = all[i];
                    if (ac == null || !Plugin.IsRuntimeInstance(ac) || !AircraftIdentity.IsXeAircraft(ac))
                        continue;
                    AircraftPowerService.RegisterLiveXe(ac);
                    ApplyLimits(ac);
                }
                return;
            }

            for (int i = Plugin.LiveXeAircraft.Count - 1; i >= 0; i--)
            {
                Aircraft ac = Plugin.LiveXeAircraft[i];
                if (ac == null)
                {
                    Plugin.LiveXeAircraft.RemoveAt(i);
                    continue;
                }
                ApplyLimits(ac);
            }
        }

        internal static void ApplyLimits(Aircraft aircraft)
        {
            if (aircraft == null || !AircraftIdentity.IsXeAircraft(aircraft))
                return;

            ManeuverProfile profile = GetOrCreateProfile(aircraft);
            EnsureControlBaselines(aircraft, profile);
            if (profile.BaselinePitch <= 0.01f || profile.BaselineRoll <= 0.01f)
                EnsureControlBaselines(aircraft, profile);

            float gAir = profile.AircraftG.Value + KillChoiceRewardService.GOverlayAdd();
            float maxSpd = profile.MaxSpeed.Value * KillChoiceRewardService.SpeedOverlayMul();
            float corner = Mathf.Min(profile.CornerSpeed.Value, maxSpd);
            float approach = profile.ApproachSpeed.Value;
            float landing = profile.LandingSpeed.Value;
            float takeoff = profile.TakeoffSpeed.Value;
            float turnRadius = profile.TurningRadius.Value;
            float pitch = profile.BaselinePitch > 0.01f
                ? profile.BaselinePitch * profile.PitchMul.Value
                : 0f;
            float roll = profile.BaselineRoll > 0.01f
                ? profile.BaselineRoll * profile.RollMul.Value
                : 0f;
            float alpha = profile.BaselineAlpha > 0.01f
                ? profile.BaselineAlpha * profile.AlphaMul.Value
                : 0f;

            AircraftDefinition def = aircraft.definition as AircraftDefinition;
            if (def != null && def.aircraftParameters != null)
            {
                AircraftParameters ap = def.aircraftParameters;
                ap.aircraftGLimit = gAir;
                ap.maxSpeed = maxSpd;
                ap.cornerSpeed = corner;
                ap.approachSpeed = approach;
                ap.landingSpeed = landing;
                ap.takeoffSpeed = takeoff;
                ap.turningRadius = turnRadius;
            }

            if (def != null && def.aircraftInfo != null)
                def.aircraftInfo.maxSpeed = maxSpd;

            ControlsFilter filter = GetControlsFilter(aircraft);
            if (filter != null)
            {
                if (EngineReflection.CfParams != null)
                {
                    try
                    {
                        AircraftParameters p = EngineReflection.CfParams.GetValue(filter) as AircraftParameters;
                        if (p != null)
                        {
                            p.aircraftGLimit = gAir;
                            p.maxSpeed = maxSpd;
                            p.cornerSpeed = corner;
                            p.approachSpeed = approach;
                            p.landingSpeed = landing;
                            p.takeoffSpeed = takeoff;
                            p.turningRadius = turnRadius;
                        }
                    }
                    catch { }
                }

                if (EngineReflection.FlyByWire != null && EngineReflection.FbwGLimit != null)
                {
                    try
                    {
                        object fbw = EngineReflection.FlyByWire.GetValue(filter);
                        if (fbw != null)
                        {
                            EngineReflection.FbwGLimit.SetValue(fbw, gAir);
                            if (EngineReflection.FbwCorner != null)
                                EngineReflection.FbwCorner.SetValue(fbw, corner);
                            if (EngineReflection.FbwMaxRoll != null)
                                EngineReflection.FbwMaxRoll.SetValue(fbw, Mathf.Max(corner, maxSpd * 0.55f));
                            if (EngineReflection.FbwPostStall != null)
                                EngineReflection.FbwPostStall.SetValue(fbw, Mathf.Max(40f, corner * 0.9f));
                            if (EngineReflection.FbwTakeoff != null)
                                EngineReflection.FbwTakeoff.SetValue(fbw, takeoff);
                            if (pitch > 0.01f && EngineReflection.FbwMaxPitch != null)
                                EngineReflection.FbwMaxPitch.SetValue(fbw, pitch);
                            if (roll > 0.01f && EngineReflection.FbwMaxRollVel != null)
                                EngineReflection.FbwMaxRollVel.SetValue(fbw, roll);
                            if (alpha > 0.01f && EngineReflection.FbwAlpha != null)
                                EngineReflection.FbwAlpha.SetValue(fbw, alpha);
                            EngineReflection.FlyByWire.SetValue(filter, fbw);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (Plugin.DebugLog != null && Plugin.DebugLog.Value)
                            Plugin.Log.LogWarning("FBW limits: " + ex.Message);
                    }
                }
            }

            if (Plugin.DebugLog != null && Plugin.DebugLog.Value)
                Plugin.Log.LogInfo("Limits " + profile.Key + " G=" + gAir + " Vmax=" + maxSpd
                    + " Vcorner=" + corner + " pitch=" + pitch + " roll=" + roll);

            BeginnerAssist.OverlayGuardianLimitsIfNeeded(aircraft);
        }

        internal static void ApplyPilotLimits(PilotPlayerState pps)
        {
            if (pps == null)
                return;
            Aircraft ac = null;
            if (EngineReflection.PilotAircraft != null)
            {
                try { ac = EngineReflection.PilotAircraft.GetValue(pps) as Aircraft; }
                catch { }
            }
            if (ac == null || !AircraftIdentity.IsXeAircraft(ac))
                return;

            Plugin.ActivePlayerAircraft = ac;
            ManeuverProfile profile = GetOrCreateProfile(ac);
            if (EngineReflection.PilotMaxG != null)
                EngineReflection.PilotMaxG.SetValue(pps, profile.PilotG.Value);
            if (EngineReflection.PilotStrength != null)
                EngineReflection.PilotStrength.SetValue(pps, profile.PilotStrength.Value);
            ApplyLimits(ac);
            BeginnerAssist.OverlayGuardianPilotLimitsIfNeeded(pps, ac);
        }

        internal static void WriteGuardianPullUpLimits(Aircraft aircraft, float gLimit, float pitchVel, float rollVel, float alpha)
        {
            if (aircraft == null)
                return;
            try
            {
                AircraftParameters ap = aircraft.GetAircraftParameters();
                if (ap != null)
                    ap.aircraftGLimit = gLimit;
            }
            catch { }

            try
            {
                AircraftDefinition def = aircraft.definition as AircraftDefinition;
                if (def != null && def.aircraftParameters != null)
                    def.aircraftParameters.aircraftGLimit = gLimit;
            }
            catch { }

            ControlsFilter filter = GetControlsFilter(aircraft);
            if (filter == null)
                return;
            if (EngineReflection.CfParams != null)
            {
                try
                {
                    AircraftParameters p = EngineReflection.CfParams.GetValue(filter) as AircraftParameters;
                    if (p != null)
                        p.aircraftGLimit = gLimit;
                }
                catch { }
            }
            if (EngineReflection.FlyByWire == null)
                return;
            try
            {
                object fbw = EngineReflection.FlyByWire.GetValue(filter);
                if (fbw == null)
                    return;
                if (EngineReflection.FbwGLimit != null)
                    EngineReflection.FbwGLimit.SetValue(fbw, gLimit);
                if (EngineReflection.FbwMaxPitch != null && pitchVel > 0.01f)
                    EngineReflection.FbwMaxPitch.SetValue(fbw, pitchVel);
                if (EngineReflection.FbwMaxRollVel != null && rollVel > 0.01f)
                    EngineReflection.FbwMaxRollVel.SetValue(fbw, rollVel);
                if (EngineReflection.FbwAlpha != null && alpha > 0.01f)
                    EngineReflection.FbwAlpha.SetValue(fbw, alpha);
                EngineReflection.FlyByWire.SetValue(filter, fbw);
            }
            catch { }
        }

        internal static bool TryReadFbwLimits(Aircraft aircraft, out float gLimit, out float pitchVel, out float rollVel, out float alpha)
        {
            gLimit = 0f;
            pitchVel = 0f;
            rollVel = 0f;
            alpha = 0f;
            if (aircraft == null)
                return false;
            try
            {
                AircraftParameters ap = aircraft.GetAircraftParameters();
                if (ap != null)
                    gLimit = ap.aircraftGLimit;
            }
            catch { }
            ControlsFilter filter = GetControlsFilter(aircraft);
            if (filter == null || EngineReflection.FlyByWire == null)
                return gLimit > 0.01f;
            try
            {
                object fbw = EngineReflection.FlyByWire.GetValue(filter);
                if (fbw == null)
                    return gLimit > 0.01f;
                if (EngineReflection.FbwGLimit != null)
                    gLimit = (float)EngineReflection.FbwGLimit.GetValue(fbw);
                if (EngineReflection.FbwMaxPitch != null)
                    pitchVel = (float)EngineReflection.FbwMaxPitch.GetValue(fbw);
                if (EngineReflection.FbwMaxRollVel != null)
                    rollVel = (float)EngineReflection.FbwMaxRollVel.GetValue(fbw);
                if (EngineReflection.FbwAlpha != null)
                    alpha = (float)EngineReflection.FbwAlpha.GetValue(fbw);
                return true;
            }
            catch { return gLimit > 0.01f; }
        }

        internal static void WriteGuardianPilotG(PilotPlayerState pps, float maxG)
        {
            if (pps == null || EngineReflection.PilotMaxG == null)
                return;
            try { EngineReflection.PilotMaxG.SetValue(pps, maxG); }
            catch { }
        }

        internal static bool TryReadPilotMaxG(PilotPlayerState pps, out float maxG)
        {
            maxG = 0f;
            if (pps == null || EngineReflection.PilotMaxG == null)
                return false;
            try
            {
                maxG = (float)EngineReflection.PilotMaxG.GetValue(pps);
                return true;
            }
            catch { return false; }
        }

        internal static void RestoreLimitsAfterGuardian(Aircraft aircraft)
        {
            if (aircraft == null)
                return;
            if (AircraftIdentity.IsXeAircraft(aircraft))
                ApplyLimits(aircraft);
        }
    }
}
