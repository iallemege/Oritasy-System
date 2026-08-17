using System;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield encyclopedia branding helpers (0.0.9.63).
    /// Plugin partial owns encyclopedia cache + refresh orchestration.
    /// </summary>
    internal static class EncyclopediaBrandService
    {
        internal const string XeZhBlurb = "XE版本的性能修改的同时也对名称进行了修改";
        internal const string XeEnBlurb = "[Oritasy] modified airframe.";

        internal static void EnsureBrandDescription(ref string desc, string brand, string emptyFallback)
        {
            if (string.IsNullOrEmpty(brand))
                return;
            if (!string.IsNullOrEmpty(desc)
                && desc.IndexOf(brand, StringComparison.OrdinalIgnoreCase) >= 0)
                return;
            if (string.IsNullOrEmpty(desc))
                desc = string.IsNullOrEmpty(emptyFallback) ? brand : emptyFallback;
            else
                desc = brand + "\n\n" + desc;
        }

        /// <summary>Strip prior EN/ZH XE brand lines so language toggle can re-prefix cleanly.</summary>
        internal static void StripXeBrandLines(ref string desc)
        {
            if (string.IsNullOrEmpty(desc))
                return;
            desc = StripLeadingBlock(desc, Plugin.PackDescLine);
            desc = StripLeadingBlock(desc, XeEnBlurb);
            desc = StripLeadingBlock(desc, XeZhBlurb);
            desc = StripLeadingBlock(desc, "[Oritasy] modified airframe.");
            desc = desc.TrimStart();
        }

        private static string StripLeadingBlock(string desc, string brand)
        {
            if (string.IsNullOrEmpty(desc) || string.IsNullOrEmpty(brand))
                return desc;
            string t = desc.TrimStart();
            if (t.StartsWith(brand, StringComparison.OrdinalIgnoreCase))
            {
                t = t.Substring(brand.Length).TrimStart();
                if (t.StartsWith("\n\n", StringComparison.Ordinal))
                    t = t.Substring(2);
                else if (t.StartsWith("\n", StringComparison.Ordinal))
                    t = t.Substring(1);
                return t;
            }
            return desc;
        }

        internal static string AppendSuffix(string s, string suffix)
        {
            if (string.IsNullOrEmpty(s) || string.IsNullOrEmpty(suffix))
                return s;
            string t = s.TrimEnd();
            if (t.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return t;
            return t + suffix;
        }

        internal static void UpdateLookup(UnitDefinition def)
        {
            if (def == null)
                return;
            if (Encyclopedia.Lookup != null && !string.IsNullOrEmpty(def.jsonKey))
                Encyclopedia.Lookup[def.jsonKey] = def;
        }

        /// <summary>Returns true if refresh is allowed; caller should bump nextAt when true.</summary>
        internal static bool AllowRefresh(float now, float nextAt, float cooldownSec, out float newNextAt)
        {
            if (now < nextAt)
            {
                newNextAt = nextAt;
                return false;
            }
            newNextAt = now + Mathf.Max(0.1f, cooldownSec);
            return true;
        }
    }
}
