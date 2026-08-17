namespace Oritasy
{
    /// <summary>Thin facade — evade lives in MissileEvadeService (0.0.9.59).</summary>
    internal static partial class PlayerAutopilot
    {
        private static void ResetEvadeState()
        {
            MissileEvadeService.Reset();
        }

        private static void TickCountermeasurePulse(Aircraft ac)
        {
            MissileEvadeService.TickCountermeasurePulse(ac);
        }

        private static bool TryApplyMissileEvade(Aircraft ac)
        {
            return MissileEvadeService.TryApply(ac);
        }
    }
}
