namespace WeXon
{
    /// <summary>
    /// Greenfield AI PreferNukesFilter decision tree.
    /// IAL [10kt] clones do not consume faction warhead stock — AiNukeChance is independent
    /// of warheadsAvailable. Vanilla PreferNukesFilter still needs stock for Genie-style nukes.
    /// Patch_PreferNukesFilter owns list mutation / vanilla call.
    /// </summary>
    internal static class PreferNukesGateService
    {
        internal enum Path
        {
            /// <summary>Let vanilla PreferNukesFilter run; snapshot conventionals for restore.</summary>
            AllowVanillaPrefer = 0,
            /// <summary>Skip vanilla entirely; leave list as-is (human hangar).</summary>
            SkipVanillaKeepList = 1,
            /// <summary>Skip vanilla; strip nukes/IAL only (naval block or no roll).</summary>
            StripNukesKeepConventional = 2,
            /// <summary>Skip vanilla; keep IAL (no stock cost), strip stockpile nukes only.</summary>
            KeepIalStripStockNukes = 3
        }

        internal static Path ResolvePrefix(
            bool humanWeaponContext,
            bool blockIalOnShips,
            bool isNavalHardpoint,
            int warheadsAvailable,
            bool rolledPreferNuke)
        {
            if (humanWeaponContext)
                return Path.SkipVanillaKeepList;
            if (blockIalOnShips && isNavalHardpoint)
                return Path.StripNukesKeepConventional;
            if (!rolledPreferNuke)
                return Path.StripNukesKeepConventional;
            // IAL nukes ignore stockpile — still allow them when warheadsAvailable == 0.
            if (warheadsAvailable <= 0)
                return Path.KeepIalStripStockNukes;
            return Path.AllowVanillaPrefer;
        }

        internal static bool ShouldRestoreConventionals(bool prefixAllowedVanilla, int filteredCount, int savedConventionalCount)
        {
            return prefixAllowedVanilla && filteredCount == 0 && savedConventionalCount > 0;
        }
    }
}
