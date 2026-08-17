namespace WeXon
{
    /// <summary>
    /// Greenfield MultiModeBrain attach eligibility (0.0.9.94).
    /// Plugin.SetupMultiMode owns AddComponent / brain.Setup.
    /// </summary>
    internal static class MultiModeSetupGateService
    {
        internal enum Path
        {
            Deny = 0,
            StripAgmT = 1,
            AllowAttach = 2
        }

        internal static Path Resolve(
            bool enableMultiMode,
            bool missileAllowed,
            bool gunShellMissile,
            bool agmTBusOrMissile,
            bool gunShellSeeker,
            bool ballisticOrBallisticSeeker,
            bool cruiseOrCruiseSeeker,
            bool rocketOrUnguidedName,
            bool infoRocketOrBallistic,
            bool seekerCallerMismatch,
            bool scannerOrRecon = false)
        {
            if (!enableMultiMode || !missileAllowed || gunShellMissile)
                return Path.Deny;
            // Scanner recon pods only — Optical AGM keeps light MM (0.7-style).
            if (agmTBusOrMissile || scannerOrRecon)
                return Path.StripAgmT;
            if (gunShellSeeker || ballisticOrBallisticSeeker || cruiseOrCruiseSeeker)
                return Path.Deny;
            if (rocketOrUnguidedName || infoRocketOrBallistic)
                return Path.Deny;
            if (seekerCallerMismatch)
                return Path.Deny;
            return Path.AllowAttach;
        }
    }
}
