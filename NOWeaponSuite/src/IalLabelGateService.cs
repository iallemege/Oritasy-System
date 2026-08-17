namespace WeXon
{
    /// <summary>
    /// [IAL] display tag = dual-mode / free-hunt eligible guided missiles
    /// (historical replacement for deprecated [MM]), plus naval-exclusive SAMs
    /// (RAM-45 / R9) which keep the name even without MultiMode.
    /// Nuclear twins add [10kt] on top; stock nukes never get either tag.
    /// </summary>
    internal static class IalLabelGateService
    {
        internal static bool ShouldCarryIalLabel(
            bool missileAllowed,
            bool gunShell,
            bool scannerRecon,
            bool rocketOrUnguided,
            bool ballistic,
            bool cruise,
            bool stockNuclear,
            bool agmTBranded,
            bool ialNukeClone,
            bool hasGuidedSeeker)
        {
            // ACM-119 / ACNM-118 and *_IAL [10kt] twins keep branding
            if (agmTBranded || ialNukeClone)
                return true;
            if (!missileAllowed || gunShell || scannerRecon || stockNuclear)
                return false;
            // Same deny set as MultiModeSetupGateService (no dual-mode attach)
            if (rocketOrUnguided || ballistic || cruise)
                return false;
            // Must be self-guided (seeker present and not shell/ballistic/cruise seeker)
            return hasGuidedSeeker;
        }
    }
}
