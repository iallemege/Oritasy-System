namespace Oritasy
{
    /// <summary>
    /// F3 auto-takeoff facade. Vanilla taxi-to-runway then vanilla takeoff
    /// lives in PlayerVanillaTakeoff (same drive pattern as F2 LAND CV).
    /// </summary>
    internal static class TakeoffAssistService
    {
        internal static void Clear()
        {
            PlayerVanillaTakeoff.Stop();
        }

        internal static void Start(Aircraft ac)
        {
            PlayerVanillaTakeoff.Start(ac);
        }

        internal static void Apply(Aircraft ac)
        {
            PlayerVanillaTakeoff.Apply(ac);
        }
    }
}
