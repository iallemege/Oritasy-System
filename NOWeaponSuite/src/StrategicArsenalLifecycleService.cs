using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Greenfield strategic arsenal cooldown / ready lifecycle (0.0.9.84).
    /// StrategicArsenal owns menu UI and Spawner orchestration.
    /// </summary>
    internal static class StrategicArsenalLifecycleService
    {
        internal static bool IsOnCooldown(float now, float readyAt)
        {
            return now < readyAt;
        }

        internal static int RemainingCooldownSec(float now, float readyAt)
        {
            if (now >= readyAt)
                return 0;
            return Mathf.CeilToInt(readyAt - now);
        }

        internal static float ScheduleReadyAt(float now, float cooldownSeconds)
        {
            float cd = StrategicArsenalMathService.CooldownSeconds(cooldownSeconds);
            return now + cd;
        }

        internal static bool CanLaunch(float now, float readyAt, bool enabled)
        {
            return enabled && !IsOnCooldown(now, readyAt);
        }
    }
}
