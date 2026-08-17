using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Greenfield strategic arsenal spawn math (0.0.9.65).
    /// StrategicArsenal owns menu, funds, and Spawner calls.
    /// </summary>
    internal static class StrategicArsenalMathService
    {
        internal const float DefaultCooldown = 300f;
        /// <summary>CallingSupport-style orbital drop: 100 km AGL, nose down.</summary>
        internal const float DefaultAltitude = 100000f;
        internal const float DefaultSpread = 2800f;
        internal const float LegacyAltitude = 14000f;
        internal const float MachToMs = 340.3f;
        /// <summary>
        /// Below 20 km F9 drops bleed to this so they can still pull (Mach 10 at low alt cannot turn).
        /// </summary>
        internal const float TerminalTurnSpeedMs = 700f;
        /// <summary>
        /// F9 TBM is a downward drop. gLimit 0 (unlimited) + vanilla loft = in-place loops.
        /// </summary>
        internal const float F9TbmGLimit = 10f;
        internal const float F9DiveAimBelowM = 80f;
        internal const float F9MaxAimPitchY = -0.12f;
        internal const float HostileScanRangeM = 80000f;

        internal static int ClampSalvoCount(int count, bool allowAboveFive)
        {
            if (!allowAboveFive)
                return Mathf.Clamp(count, 1, 5);
            return Mathf.Clamp(count, 1, 30);
        }

        internal static Vector3 SalvoOffset(int index, int count, float spread)
        {
            float ang = (360f / Mathf.Max(1, count)) * index * Mathf.Deg2Rad;
            float ring = spread * (0.35f + 0.65f * ((index % 5) / 4f));
            return new Vector3(Mathf.Cos(ang) * ring, 0f, Mathf.Sin(ang) * ring);
        }

        internal static Vector3 SpawnPosition(Vector3 center, Vector3 offset, float altitude)
        {
            Vector3 p = center + offset;
            p.y = altitude;
            return p;
        }

        /// <summary>F9 salvos drop straight down from spawn altitude.</summary>
        internal static Vector3 DownwardAim()
        {
            return Vector3.down;
        }

        internal static Vector3 AimDirection(Vector3 from, Vector3 aimPoint, Vector3 fallbackFwd)
        {
            return DownwardAim();
        }

        internal static float SpeedMsFromMach(float speedMach)
        {
            if (speedMach > 0.1f)
                return speedMach * MachToMs;
            return 420f;
        }

        internal static float TerminalSpeedMs(float launchSpeedMs)
        {
            if (launchSpeedMs <= TerminalTurnSpeedMs)
                return launchSpeedMs;
            return TerminalTurnSpeedMs;
        }

        internal static Vector3 CapSpeed(Vector3 velocity, float maxMs)
        {
            float cap = maxMs > 40f ? maxMs : TerminalTurnSpeedMs;
            float sq = cap * cap;
            if (velocity.sqrMagnitude <= sq + 1f)
                return velocity;
            float mag = velocity.magnitude;
            if (mag < 0.01f)
                return velocity;
            return velocity * (cap / mag);
        }

        /// <summary>Keep F9 TBM aimed at the ground target; never loft back through 20 km.</summary>
        internal static Vector3 F9TbmDiveAim(Vector3 missilePos, Vector3 targetPos)
        {
            Vector3 aim = targetPos;
            if (aim.y > missilePos.y - F9DiveAimBelowM)
                aim.y = missilePos.y - F9DiveAimBelowM;
            Vector3 dir = aim - missilePos;
            if (dir.sqrMagnitude < 1f)
                return missilePos + Vector3.down * 1000f;
            dir.Normalize();
            if (dir.y > F9MaxAimPitchY)
            {
                Vector3 hz = new Vector3(dir.x, 0f, dir.z);
                if (hz.sqrMagnitude < 0.0001f)
                    return missilePos + Vector3.down * 1000f;
                hz.Normalize();
                float hy = F9MaxAimPitchY;
                float horizMag = Mathf.Sqrt(Mathf.Max(0f, 1f - hy * hy));
                dir = hz * horizMag + Vector3.up * hy;
                aim = missilePos + dir * 1000f;
            }
            return aim;
        }

        internal static string FormatCostM(float costM)
        {
            float r = Mathf.Round(costM * 10f) / 10f;
            if (Mathf.Abs(r - Mathf.Round(r)) < 0.05f)
                return r.ToString("0");
            return r.ToString("0.0");
        }

        internal static float CooldownSeconds(float configured)
        {
            return Mathf.Max(1f, configured);
        }

        internal static int WrapIndex(int index, int delta, int length)
        {
            if (length <= 0)
                return 0;
            int n = (index + delta) % length;
            if (n < 0)
                n += length;
            return n;
        }
    }
}
