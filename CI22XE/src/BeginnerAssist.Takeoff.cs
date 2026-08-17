namespace Oritasy
{
    /// <summary>Thin facade — F3 takeoff drives vanilla taxi then takeoff.</summary>
    internal static partial class BeginnerAssist
    {
        private static void ClearTakeoff()
        {
            TakeoffAssistService.Clear();
        }

        private static void StartTakeoff(Aircraft ac)
        {
            TakeoffAssistService.Start(ac);
        }

        private static void ApplyTakeoff(Aircraft ac)
        {
            TakeoffAssistService.Apply(ac);
        }
    }
}
