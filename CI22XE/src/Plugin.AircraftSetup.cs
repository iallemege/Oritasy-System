namespace Oritasy
{
    /// <summary>Thin facade — setup/power live in AircraftPowerService (0.0.9.56 greenfield).</summary>
    public partial class Plugin
    {
        internal static void RegisterLiveXe(Aircraft aircraft)
        {
            AircraftPowerService.RegisterLiveXe(aircraft);
        }

        internal static void UnregisterLiveXe(Aircraft aircraft)
        {
            AircraftPowerService.UnregisterLiveXe(aircraft);
        }

        internal static void TrySetupAircraft(Aircraft aircraft)
        {
            AircraftPowerService.TrySetupAircraft(aircraft);
        }

        internal static void ApplyPowerProfile(Aircraft aircraft)
        {
            AircraftPowerService.ApplyPowerProfile(aircraft);
        }
    }
}
