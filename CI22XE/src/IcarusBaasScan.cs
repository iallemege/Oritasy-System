namespace Oritasy
{
    /// <summary>
    /// Retired. A Harmony patch on Directory.GetFiles hit every game file scan
    /// and stalled the main menu. Oritasy now loads *.baas itself
    /// (BaasImportService, plugins root + one child folder, skip fonts/assets).
    /// </summary>
    internal static class IcarusBaasScan
    {
    }
}
