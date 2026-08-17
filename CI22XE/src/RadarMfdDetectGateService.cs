namespace Oritasy
{
    /// <summary>
    /// Greenfield radar-MFD page detect path FSM (0.0.9.96).
    /// RadarMfdOverlay owns Unity field reads, rect capture, and MarkRadarOn.
    /// </summary>
    internal static class RadarMfdDetectGateService
    {
        internal enum Path
        {
            Off = 0,
            TacRadarOn = 1,
            TacWidgets = 2,
            VirtualMfd = 3,
            RadarPlusMfd = 4,
            RadarPlusTacRender = 5
        }

        /// <summary>
        /// Priority matches DetectRadarDisplay: Tac flag → widgets → MFD page →
        /// radar.activated + loose MFD → radar.activated + Tac render.
        /// </summary>
        internal static Path Resolve(
            bool tacRadarOn,
            bool tacWidgets,
            bool virtualMfdRadarPage,
            bool radarActivated,
            bool activeMfdLooksRadarLoose,
            bool tacRenderVisible)
        {
            if (tacRadarOn)
                return Path.TacRadarOn;
            if (tacWidgets)
                return Path.TacWidgets;
            if (virtualMfdRadarPage)
                return Path.VirtualMfd;
            if (radarActivated)
            {
                if (activeMfdLooksRadarLoose)
                    return Path.RadarPlusMfd;
                if (tacRenderVisible)
                    return Path.RadarPlusTacRender;
            }
            return Path.Off;
        }

        internal static string ReasonLabel(Path path, string mfdShortName)
        {
            switch (path)
            {
                case Path.TacRadarOn: return "TacScreen.radarOn";
                case Path.TacWidgets: return "TacScreen.widgets";
                case Path.VirtualMfd: return "VirtualMFD:" + (mfdShortName ?? "");
                case Path.RadarPlusMfd: return "radar.activated+MFD";
                case Path.RadarPlusTacRender: return "radar.activated+TacRender";
                default: return null;
            }
        }
    }
}
