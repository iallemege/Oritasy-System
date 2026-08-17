using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield crash-guardian recovery inputs + temporary FBW boost (0.0.9.59).
    /// Threat classify stays in CrashThreatClassifier; hold/handback stays on BeginnerAssist.
    /// </summary>
    internal static class GuardianRecoveryService
    {
        internal static void CaptureBaselines(Aircraft ac)
        {
            if (BeginnerAssist._boostCaptured || ac == null)
                return;
            float g, pitch, roll, alpha;
            if (Plugin.TryReadFbwLimits(ac, out g, out pitch, out roll, out alpha))
            {
                BeginnerAssist._savedG = g > 0.1f ? g : 9f;
                BeginnerAssist._savedPitchVel = pitch > 0.01f ? pitch : 1.2f;
                BeginnerAssist._savedRollVel = roll > 0.01f ? roll : 1.5f;
                BeginnerAssist._savedAlpha = alpha > 0.01f ? alpha : 20f;
                BeginnerAssist._boostCaptured = true;
            }
            BeginnerAssist._hasSavedPilotG = false;
            try
            {
                if (ac.pilots != null && ac.pilots.Length > 0 && ac.pilots[0] != null)
                {
                    PilotPlayerState pps = ac.pilots[0].playerState;
                    float pg;
                    if (pps != null && Plugin.TryReadPilotMaxG(pps, out pg) && pg > 0.1f)
                    {
                        BeginnerAssist._savedPilotG = pg;
                        BeginnerAssist._hasSavedPilotG = true;
                    }
                }
            }
            catch { }
        }

        internal static void ApplyLimitBoost(Aircraft ac, bool inverted)
        {
            if (ac == null)
                return;
            CaptureBaselines(ac);
            if (!BeginnerAssist._boostCaptured)
                return;

            float gTgt = inverted
                ? Mathf.Max(BeginnerAssist._savedG * 1.85f, 16f)
                : Mathf.Max(BeginnerAssist._savedG * 1.35f, 12f);
            float pitchTgt = inverted
                ? BeginnerAssist._savedPitchVel * 2.4f
                : BeginnerAssist._savedPitchVel * 1.55f;
            float rollTgt = inverted
                ? BeginnerAssist._savedRollVel * 2.1f
                : BeginnerAssist._savedRollVel * 1.35f;
            float alphaTgt = inverted
                ? BeginnerAssist._savedAlpha * 1.6f
                : BeginnerAssist._savedAlpha * 1.25f;
            gTgt = Mathf.Clamp(gTgt, 10f, 22f);
            pitchTgt = Mathf.Clamp(pitchTgt, 1f, 8f);
            rollTgt = Mathf.Clamp(rollTgt, 1f, 10f);
            alphaTgt = Mathf.Clamp(alphaTgt, 15f, 55f);
            Plugin.WriteGuardianPullUpLimits(ac, gTgt, pitchTgt, rollTgt, alphaTgt);

            try
            {
                if (ac.pilots != null && ac.pilots.Length > 0 && ac.pilots[0] != null)
                {
                    PilotPlayerState pps = ac.pilots[0].playerState;
                    if (pps != null)
                    {
                        float pg = inverted ? 18f : 14f;
                        if (BeginnerAssist._hasSavedPilotG)
                            pg = Mathf.Max(pg, BeginnerAssist._savedPilotG * (inverted ? 1.5f : 1.2f));
                        Plugin.WriteGuardianPilotG(pps, Mathf.Clamp(pg, 9f, 20f));
                    }
                }
            }
            catch { }
        }

        internal static void RestoreBaselines(Aircraft ac)
        {
            if (!BeginnerAssist._boostCaptured)
            {
                BeginnerAssist._hasSavedPilotG = false;
                if (ac != null)
                    Plugin.RestoreLimitsAfterGuardian(ac);
                return;
            }
            if (ac != null)
            {
                Plugin.WriteGuardianPullUpLimits(ac,
                    BeginnerAssist._savedG,
                    BeginnerAssist._savedPitchVel,
                    BeginnerAssist._savedRollVel,
                    BeginnerAssist._savedAlpha);
                try
                {
                    if (BeginnerAssist._hasSavedPilotG && ac.pilots != null && ac.pilots.Length > 0
                        && ac.pilots[0] != null)
                    {
                        PilotPlayerState pps = ac.pilots[0].playerState;
                        if (pps != null)
                            Plugin.WriteGuardianPilotG(pps, BeginnerAssist._savedPilotG);
                    }
                }
                catch { }
                Plugin.RestoreLimitsAfterGuardian(ac);
            }
            BeginnerAssist._boostCaptured = false;
            BeginnerAssist._hasSavedPilotG = false;
        }

        internal static void ApplyInvertedPullUp(Aircraft ac, BeginnerAssist.AirframeTune tune)
        {
            ControlInputs inputs = ac.GetInputs();
            if (inputs == null)
                return;

            float ralt = 0f;
            try { ralt = ac.radarAlt; }
            catch { }
            float hold = Mathf.Max(500f, ralt + 400f);

            Vector3 track = FlatTrack(ac);
            Vector3 aim = ac.transform.position + track * 8000f + Vector3.up * 3500f;
            AutoAim(ac, aim, hold, true);

            inputs.pitch = -1f;
            float rightY = 0f;
            try { rightY = ac.transform.right.y; }
            catch { }
            inputs.roll = Mathf.Clamp(-rightY * 2.5f, -1f, 1f);
            inputs.yaw = 0f;
            inputs.brake = 0f;
            if (tune.StovlNozzle)
                ApplyStovlNozzleThrust(ac, true);
            else
                inputs.throttle = 1f;
        }

        internal static void ApplyPostStallRecovery(Aircraft ac, BeginnerAssist.AirframeTune tune)
        {
            ControlInputs inputs = ac.GetInputs();
            if (inputs == null)
                return;

            float ralt = 0f;
            try { ralt = ac.radarAlt; }
            catch { }
            float hold = Mathf.Max(400f, ralt + 250f);
            Vector3 track = FlatTrack(ac);
            Vector3 aim = ac.transform.position + track * 6000f;
            aim.y = ac.transform.position.y - 200f;
            AutoAim(ac, aim, hold, false);

            inputs.pitch = -0.85f;
            float rightY = 0f;
            try { rightY = ac.transform.right.y; }
            catch { }
            inputs.roll = Mathf.Clamp(-rightY * 2f, -1f, 1f);
            inputs.yaw = 0f;
            inputs.brake = 0f;
            float spd = Mathf.Max(1f, ac.speed);
            float corner = BeginnerAssist.ResolveCorner(ac);
            if (tune.StovlNozzle && ralt < tune.InvertAgl * 1.2f)
                ApplyStovlNozzleThrust(ac, true);
            else
                inputs.throttle = spd < corner * 0.7f ? 1f : 0.55f;
        }

        internal static void ApplySpinRecovery(Aircraft ac, BeginnerAssist.AirframeTune tune)
        {
            ControlInputs inputs = ac.GetInputs();
            if (inputs == null)
                return;

            float yaw = BeginnerAssist.ReadYawRateDeg(ac);
            float roll = BeginnerAssist.ReadRollRateDeg(ac);
            float ralt = 0f;
            try { ralt = ac.radarAlt; }
            catch { }
            bool highAlt = ralt >= 2500f;
            float hold = highAlt
                ? Mathf.Clamp(ralt - 200f, 1500f, ralt)
                : Mathf.Max(450f, ralt + 300f);
            Vector3 track = FlatTrack(ac);
            Vector3 aim = ac.transform.position + track * 5000f
                + Vector3.up * (highAlt ? -400f : 800f);
            AutoAim(ac, aim, hold, !highAlt);

            inputs.pitch = highAlt ? -1f : -0.9f;
            if (Mathf.Abs(roll) > 45f && Mathf.Abs(yaw) < tune.SpinYawRate * 0.9f)
                inputs.roll = roll > 0f ? -0.55f : 0.55f;
            else
                inputs.roll = 0f;
            inputs.yaw = yaw > 0f ? -1f : 1f;
            inputs.brake = 0f;
            if (tune.StovlNozzle && ralt < tune.InvertAgl * 1.3f)
                ApplyStovlNozzleThrust(ac, true);
            else
                inputs.throttle = highAlt ? 1f : 0.45f;
        }

        internal static void ApplyStovlNozzleThrust(Aircraft ac, bool climb)
        {
            if (PlayerAutopilot.IsLandingMode)
                return;
            ControlInputs inputs = ac.GetInputs();
            if (inputs == null)
                return;
            inputs.customAxis1 = climb ? 1f : 0.35f;
            inputs.throttle = 1f;
            inputs.brake = 0f;
        }

        private static Vector3 FlatTrack(Aircraft ac)
        {
            Vector3 track = Vector3.forward;
            try
            {
                if (ac.rb != null && ac.rb.velocity.sqrMagnitude > 25f)
                    track = ac.rb.velocity;
                else
                    track = ac.transform.forward;
            }
            catch { track = ac.transform.forward; }
            track.y = 0f;
            if (track.sqrMagnitude < 0.01f)
                track = Vector3.forward;
            return track.normalized;
        }

        private static void AutoAim(Aircraft ac, Vector3 aimLocal, float holdAgl, bool followTerrain)
        {
            Autopilot ap = ac.autopilot;
            if (ap == null)
                return;
            AutopilotPlane plane = ap as AutopilotPlane;
            if (plane != null)
            {
                plane.AutoAim(aimLocal.ToGlobalPosition(), true, false, false, 0.95f, 180f,
                    followTerrain, holdAgl, Vector3.zero);
            }
            else
            {
                Vector3 dir = aimLocal - ac.transform.position;
                if (dir.sqrMagnitude < 0.01f)
                    dir = ac.transform.forward;
                ap.AutoAim(aimLocal.ToGlobalPosition(), holdAgl, dir.normalized, Vector3.zero, followTerrain);
            }
        }
    }
}
