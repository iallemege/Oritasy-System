namespace Oritasy
{
    /// <summary>
    /// Greenfield AirportIlsHud draw eligibility (0.0.9.98).
    /// AirportIlsHud owns scan, camera, and GUI paint.
    /// </summary>
    internal static class AirportIlsHudGateService
    {
        internal enum Path
        {
            Skip = 0,
            Draw = 1
        }

        internal static Path ResolveDraw(
            bool featureEnabled,
            bool isRepaint,
            bool blockedByManualCam,
            bool blockedByPresentation,
            bool hasAircraft,
            bool gearDown,
            bool hasCamera)
        {
            if (!featureEnabled || !isRepaint)
                return Path.Skip;
            if (blockedByManualCam || blockedByPresentation)
                return Path.Skip;
            if (!hasAircraft || !gearDown || !hasCamera)
                return Path.Skip;
            return Path.Draw;
        }

        internal static bool ShouldPaintSafeRange(bool showSafeRangeConfig)
        {
            return showSafeRangeConfig;
        }

        internal static bool ShouldPaintIls(bool showIlsConfig, bool haveRunway)
        {
            return showIlsConfig && haveRunway;
        }
    }
}
