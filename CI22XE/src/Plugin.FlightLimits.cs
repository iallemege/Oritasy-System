namespace Oritasy
{
    /// <summary>Thin facade — envelope/limits live in FlightEnvelopeService (0.0.9.56 greenfield).</summary>
    public partial class Plugin
    {
        internal static Aircraft ResolveGuiAircraft()
        {
            return FlightEnvelopeService.ResolveGuiAircraft();
        }

        internal static bool IsUsableXeAircraft(Aircraft ac)
        {
            return FlightEnvelopeService.IsUsableXeAircraft(ac);
        }

        internal static ManeuverProfile GetOrCreateProfile(Aircraft aircraft)
        {
            return FlightEnvelopeService.GetOrCreateProfile(aircraft);
        }

        internal static ControlsFilter GetControlsFilter(Aircraft aircraft)
        {
            return FlightEnvelopeService.GetControlsFilter(aircraft);
        }

        internal static void EnsurePitchBaseline(Aircraft aircraft, ManeuverProfile profile)
        {
            FlightEnvelopeService.EnsurePitchBaseline(aircraft, profile);
        }

        internal static void EnsureControlBaselines(Aircraft aircraft, ManeuverProfile profile)
        {
            FlightEnvelopeService.EnsureControlBaselines(aircraft, profile);
        }

        internal static void ApplyLimitsToAllXe()
        {
            FlightEnvelopeService.ApplyLimitsToAllXe();
        }

        internal static void ApplyLimits(Aircraft aircraft)
        {
            FlightEnvelopeService.ApplyLimits(aircraft);
        }

        internal static void ApplyPilotLimits(PilotPlayerState pps)
        {
            FlightEnvelopeService.ApplyPilotLimits(pps);
        }

        internal static void WriteGuardianPullUpLimits(Aircraft aircraft, float gLimit, float pitchVel, float rollVel, float alpha)
        {
            FlightEnvelopeService.WriteGuardianPullUpLimits(aircraft, gLimit, pitchVel, rollVel, alpha);
        }

        internal static bool TryReadFbwLimits(Aircraft aircraft, out float gLimit, out float pitchVel, out float rollVel, out float alpha)
        {
            return FlightEnvelopeService.TryReadFbwLimits(aircraft, out gLimit, out pitchVel, out rollVel, out alpha);
        }

        internal static void WriteGuardianPilotG(PilotPlayerState pps, float maxG)
        {
            FlightEnvelopeService.WriteGuardianPilotG(pps, maxG);
        }

        internal static bool TryReadPilotMaxG(PilotPlayerState pps, out float maxG)
        {
            return FlightEnvelopeService.TryReadPilotMaxG(pps, out maxG);
        }

        internal static void RestoreLimitsAfterGuardian(Aircraft aircraft)
        {
            FlightEnvelopeService.RestoreLimitsAfterGuardian(aircraft);
        }
    }
}
