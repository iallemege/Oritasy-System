namespace Oritasy
{
    /// <summary>Thin facade — spawn buffs live in AircraftPowerService (0.0.9.56 greenfield).</summary>
    public partial class Plugin
    {
        internal static void TryBuffEngine(TurbineEngine engine)
        {
            AircraftPowerService.TryBuffEngine(engine);
        }

        internal static void TryBuffProp(ConstantSpeedProp prop)
        {
            AircraftPowerService.TryBuffProp(prop);
        }

        internal static void TryBuffPropFan(PropFan fan)
        {
            AircraftPowerService.TryBuffPropFan(fan);
        }

        internal static void TryBuffRotor(RotorShaft rotor)
        {
            AircraftPowerService.TryBuffRotor(rotor);
        }

        internal static void TryBuffDucted(DuctedFan fan)
        {
            AircraftPowerService.TryBuffDucted(fan);
        }

        internal static void TryBuffTurbofan(Turbofan fan)
        {
            AircraftPowerService.TryBuffTurbofan(fan);
        }

        internal static void TryBuffTurbojet(Turbojet jet)
        {
            AircraftPowerService.TryBuffTurbojet(jet);
        }

        internal static void TryBuffGear(LandingGear gear)
        {
            AircraftPowerService.TryBuffGear(gear);
        }

        internal static void TryBuffFuel(FuelTank tank, float mul)
        {
            AircraftPowerService.TryBuffFuel(tank, mul);
        }
    }
}
