using System;

namespace WeXon
{
    /// <summary>
    /// Greenfield gun-shell / cruise / rocket name classify (extends 0.0.9.89; 0.0.9.94).
    /// Plugin owns seeker/component scans around these name checks.
    /// </summary>
    internal static class MissileClassifyGateService
    {
        /// <summary>Rockets / unguided nuke rockets — keep vanilla; MM free-hunt breaks them.</summary>
        internal static bool IsRocketOrUnguidedName(string n)
        {
            if (string.IsNullOrEmpty(n))
                return false;
            string s = n.ToLowerInvariant();
            if (s.IndexOf("rocket", StringComparison.Ordinal) >= 0)
                return true;
            if (s.IndexOf("agr-", StringComparison.Ordinal) >= 0 || s.IndexOf("agr18", StringComparison.Ordinal) >= 0)
                return true;
            if (s.IndexOf("genie", StringComparison.Ordinal) >= 0 || s.IndexOf("air-2", StringComparison.Ordinal) >= 0)
                return true;
            if (s.IndexOf("unguided", StringComparison.Ordinal) >= 0)
                return true;
            return false;
        }

        internal static bool IsRocketOrUnguidedMissile(
            string missileName,
            string jsonKey,
            string unitName,
            string defName)
        {
            return IsRocketOrUnguidedName(missileName)
                || IsRocketOrUnguidedName(jsonKey)
                || IsRocketOrUnguidedName(unitName)
                || IsRocketOrUnguidedName(defName);
        }

        /// <summary>
        /// Piledriver TBM / BallisticMissileGuidance loft INS names.
        /// MM GuideTo + forcing gLimit crushes steering at high speed.
        /// </summary>
        internal static bool IsBallisticMissileName(string n)
        {
            if (string.IsNullOrEmpty(n))
                return false;
            string s = n.ToLowerInvariant();
            return s.IndexOf("ballistic", StringComparison.Ordinal) >= 0
                || s.IndexOf("piledriver", StringComparison.Ordinal) >= 0
                || s.IndexOf("tbm", StringComparison.Ordinal) >= 0;
        }

        /// <summary>TGM-85 / Kh85MT must never classify as Piledriver TBM.</summary>
        internal static bool IsKh85MtExcludedFromBallistic(
            string definitionJsonKey,
            string definitionUnitName,
            string missileName)
        {
            if (!string.IsNullOrEmpty(definitionJsonKey)
                && definitionJsonKey.StartsWith("Kh85MT", StringComparison.OrdinalIgnoreCase))
                return true;
            if (!string.IsNullOrEmpty(definitionUnitName)
                && definitionUnitName.IndexOf("TGM-85", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (!string.IsNullOrEmpty(missileName)
                && missileName.IndexOf("Kh85MT", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        internal static bool IsGunShellName(string n)
        {
            if (string.IsNullOrEmpty(n))
                return false;
            string s = n.ToLowerInvariant();
            if (s.IndexOf("shell_", StringComparison.Ordinal) >= 0)
                return true;
            if (s.IndexOf("shell ", StringComparison.Ordinal) >= 0)
                return true;
            if (s.EndsWith("shell", StringComparison.Ordinal))
                return true;
            if (s.IndexOf("guided shell", StringComparison.Ordinal) >= 0)
                return true;
            if (s.IndexOf("mm_guided", StringComparison.Ordinal) >= 0)
                return true;
            if (s.IndexOf("mm guided", StringComparison.Ordinal) >= 0)
                return true;
            if (s.IndexOf("127mm", StringComparison.Ordinal) >= 0
                || s.IndexOf("76mm", StringComparison.Ordinal) >= 0
                || s.IndexOf("155mm", StringComparison.Ordinal) >= 0)
            {
                if (s.IndexOf("shell", StringComparison.Ordinal) >= 0
                    || s.IndexOf("gun", StringComparison.Ordinal) >= 0
                    || s.IndexOf("he-", StringComparison.Ordinal) >= 0
                    || s.IndexOf("casing", StringComparison.Ordinal) >= 0)
                    return true;
            }
            return false;
        }

        internal static bool IsCruiseMissileName(string n)
        {
            if (string.IsNullOrEmpty(n))
                return false;
            string s = n.ToLowerInvariant();
            if (s.IndexOf("cruise", StringComparison.Ordinal) >= 0)
                return true;
            if (s.IndexOf("tomahawk", StringComparison.Ordinal) >= 0)
                return true;
            if (s.IndexOf("scm", StringComparison.Ordinal) >= 0)
                return true;
            if (s.IndexOf("land-attack", StringComparison.Ordinal) >= 0)
                return true;
            if (s.IndexOf("terrain", StringComparison.Ordinal) >= 0)
                return true;
            return false;
        }

        /// <summary>
        /// Eyeball Mk.II / AGM_scanner recon pods — TargetDetector + OpticalSeeker.
        /// MM free-hunt / FeedSeekerLock fights panoramic scan; IAL nuke clones are wrong.
        /// </summary>
        internal static bool IsScannerReconName(string n)
        {
            if (string.IsNullOrEmpty(n))
                return false;
            string s = n.ToLowerInvariant();
            return s.IndexOf("agm_scanner", StringComparison.Ordinal) >= 0
                || s.IndexOf("scanner", StringComparison.Ordinal) >= 0
                || s.IndexOf("eyeball", StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// Optical / Laser seeker family — use light MM (0.7-style), not heavy GuideTo/Feed.
        /// </summary>
        internal static bool IsOpticalOrLaserSeeker(object seeker)
        {
            if (seeker == null)
                return false;
            Type t = seeker.GetType();
            string n = t.Name;
            if (n.IndexOf("Optical", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("Laser", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        /// <summary>Obsolete alias — optical is light-mode, not stripped (except scanner/cruise/shell).</summary>
        internal static bool IsOpticalFamilyMmExcludedSeeker(object seeker)
        {
            return IsOpticalOrLaserSeeker(seeker);
        }

        internal static bool ShouldStripIncompatibleBrain(
            bool gunShell,
            bool ballistic,
            bool cruise,
            bool agmT,
            bool scannerRecon = false,
            bool opticalFamily = false)
        {
            // opticalFamily no longer strips — kept for signature compat; ignored.
            return gunShell || ballistic || cruise || agmT || scannerRecon;
        }

        /// <summary>RAM-45 display / GO names (spaces optional).</summary>
        internal static bool LooksLikeRam45Name(string n)
        {
            if (string.IsNullOrEmpty(n))
                return false;
            if (n.IndexOf("RAM-45", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("RAM45", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        /// <summary>Vanilla VLS SARH family: SAM_Radar1 = RAM-45, SAM_Radar2 = R9.</summary>
        internal static bool IsSamRadarKey(string jsonKey)
        {
            if (string.IsNullOrEmpty(jsonKey))
                return false;
            return jsonKey.StartsWith("SAM_Radar", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Ship-only SAMs (RAM-45 / StratoLance R9 / SAM_Radar*).</summary>
        internal static bool LooksLikeNavalExclusiveName(string n)
        {
            if (LooksLikeRam45Name(n))
                return true;
            if (IsSamRadarKey(n))
                return true;
            if (string.IsNullOrEmpty(n))
                return false;
            if (n.IndexOf("StratoLance", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("R9", StringComparison.OrdinalIgnoreCase) >= 0
                && (n.IndexOf("SAM", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Radar", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("Strato", StringComparison.OrdinalIgnoreCase) >= 0))
                return true;
            return false;
        }
    }
}
