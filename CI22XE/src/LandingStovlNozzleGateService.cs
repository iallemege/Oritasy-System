namespace Oritasy
{
    /// <summary>
    /// Greenfield LAND-CV winged STOVL nozzle axis gate (0.0.9.98).
    /// LandingGuidance owns ControlInputs writes.
    /// </summary>
    internal static class LandingStovlNozzleGateService
    {
        internal enum Path
        {
            Skip = 0,
            WhisperCap = 1,
            HardZero = 2
        }

        /// <summary>
        /// CV winged approach: nozzles stay forward. Only a whisper of lift when
        /// rolling out almost stopped on deck.
        /// </summary>
        internal static Path Resolve(
            bool carrierMode,
            bool stovl,
            bool rollout,
            float ralt,
            float speed)
        {
            if (!carrierMode)
                return Path.Skip;
            if (stovl && rollout && ralt < 5f && speed < 15f)
                return Path.WhisperCap;
            return Path.HardZero;
        }

        internal const float WhisperAxisCap = 0.25f;
    }
}
