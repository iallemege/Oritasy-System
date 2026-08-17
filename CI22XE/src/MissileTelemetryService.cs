using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield missile-camera telemetry + manual aim math (0.0.9.61→69).
    /// Lifecycle phase FSM: MissileCameraLifecycleService (0.0.9.83).
    /// MissileCameraHud owns camera, tracking list, and GUI.
    /// </summary>
    internal static class MissileTelemetryService
    {
        internal static float SpeedMps(Missile m)
        {
            if (m == null)
                return 0f;
            try { return m.speed; }
            catch
            {
                try { return m.rb != null ? m.rb.velocity.magnitude : 0f; }
                catch { return 0f; }
            }
        }

        internal static float SpeedKmh(Missile m)
        {
            return SpeedMps(m) * 3.6f;
        }

        internal static float FuelFraction(float fuel, float fuelMax)
        {
            if (fuelMax <= 0.001f)
                return fuel > 0f ? 1f : 0f;
            return Mathf.Clamp01(fuel / fuelMax);
        }

        /// <summary>
        /// Updates display G from velocity delta. Returns new displayG; writes vel/time refs.
        /// </summary>
        internal static float UpdateSignedG(
            Missile missile,
            float displayG,
            ref Vector3 gVelPrev,
            ref float gVelPrevTime)
        {
            if (missile == null)
                return displayG;

            Vector3 vel = Vector3.zero;
            try
            {
                if (missile.rb != null)
                    vel = missile.rb.velocity;
            }
            catch { }

            float now = Time.unscaledTime;
            float dt = now - gVelPrevTime;
            if (gVelPrevTime > 0f && dt > 0.001f && dt < 0.5f)
            {
                Vector3 accelG = (vel - gVelPrev) / (dt * 9.81f);
                float signed = accelG.magnitude;
                try { signed = Vector3.Dot(accelG, missile.transform.up); }
                catch { }
                displayG = Mathf.Lerp(displayG, signed, 0.35f);
            }
            gVelPrev = vel;
            gVelPrevTime = now;
            return displayG;
        }

        /// <summary>WASD local pitch/yaw increment on commanded rotation.</summary>
        internal static Quaternion ApplyManualSteer(
            Quaternion cmdRot,
            Quaternion bodyRot,
            float pitch,
            float yaw,
            float turnRateDeg,
            float dt)
        {
            pitch = Mathf.Clamp(pitch, -1f, 1f);
            yaw = Mathf.Clamp(yaw, -1f, 1f);
            if (Mathf.Abs(pitch) <= 0.01f && Mathf.Abs(yaw) <= 0.01f)
                return cmdRot;

            float turn = Mathf.Clamp(turnRateDeg, 10f, 180f);
            Quaternion next = cmdRot * Quaternion.Euler(-pitch * turn * dt, yaw * turn * dt, 0f);
            try
            {
                float ang = Quaternion.Angle(bodyRot, next);
                if (ang > 55f)
                    next = Quaternion.RotateTowards(bodyRot, next, 55f);
            }
            catch { }
            return next;
        }

        internal static float ClampTurnRate(float configured)
        {
            return Mathf.Clamp(configured, 10f, 180f);
        }

        internal static float ClampThrottleRate(float configured)
        {
            return Mathf.Clamp(configured, 0.15f, 3f);
        }

        internal static float StepThrottle(float current, float delta, float rate, float mul, float dt)
        {
            return Mathf.Clamp01(current + delta * rate * mul * dt);
        }

        internal static float AimLookAheadM(float speedMs)
        {
            return Mathf.Clamp(Mathf.Max(speedMs, 80f) * 1.6f, 400f, 3500f);
        }

        internal static float SoftFloorAimY(float aimY, float terrainFloorY, bool hasFloor)
        {
            if (hasFloor && aimY < terrainFloorY)
                return terrainFloorY;
            return aimY;
        }

        internal struct PipLayout
        {
            public Rect Frame;
            public Rect View;
        }

        internal static PipLayout ComputePipLayout(float screenW, float screenH, float widthFrac)
        {
            float frac = Mathf.Clamp(widthFrac, 0.15f, 0.45f);
            float pad = Mathf.Max(8f, screenH * 0.012f);
            float w = screenW * frac;
            float h = w * 9f / 16f;
            float x = pad;
            float y = (screenH - h) * 0.5f;
            PipLayout layout = new PipLayout();
            layout.View = new Rect(x, y, w, h);
            layout.Frame = new Rect(x - 3f, y - 22f, w + 6f, h + 44f);
            return layout;
        }
    }
}
