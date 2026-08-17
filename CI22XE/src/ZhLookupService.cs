using System;
using System.Collections.Generic;

namespace Oritasy
{
    /// <summary>
    /// Greenfield ZH dictionary lookup helpers (0.0.9.70).
    /// GameZhLocalizer owns dictionary seed, TMP scan, and Harmony patches.
    /// </summary>
    internal static class ZhLookupService
    {
        internal static readonly string[] PackSuffixes = new string[] { " XE", " NE", " TE", " Bexur" };

        internal static bool TryStripPackSuffix(string key, out string stripped, out string suffix)
        {
            stripped = key;
            suffix = "";
            if (string.IsNullOrEmpty(key))
                return false;
            for (int i = 0; i < PackSuffixes.Length; i++)
            {
                string pack = PackSuffixes[i];
                if (key.EndsWith(pack, StringComparison.Ordinal))
                {
                    stripped = key.Substring(0, key.Length - pack.Length).TrimEnd();
                    suffix = pack;
                    return stripped.Length > 0;
                }
            }
            return false;
        }

        /// <summary>Match "Prefix (…)" dynamic labels. Null if no match.</summary>
        internal static string ExpandIfPrefixParen(string key, string enPrefix, string zhPrefix)
        {
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(enPrefix) || zhPrefix == null)
                return null;
            if (key.Length <= enPrefix.Length)
                return null;
            if (key.StartsWith(enPrefix, StringComparison.Ordinal)
                && key[enPrefix.Length] == ' '
                && key[enPrefix.Length + 1] == '(')
                return zhPrefix + key.Substring(enPrefix.Length);
            return null;
        }

        internal static string LookupExactOrPack(
            string key,
            IDictionary<string, string> map)
        {
            if (string.IsNullOrEmpty(key) || map == null)
                return key;
            string zh;
            if (map.TryGetValue(key, out zh) && !string.IsNullOrEmpty(zh))
                return zh;
            string stripped;
            string suffix;
            if (TryStripPackSuffix(key, out stripped, out suffix)
                && map.TryGetValue(stripped, out zh)
                && !string.IsNullOrEmpty(zh))
                return zh + suffix;

            // "Early Access Version 0.xx" → 抢先体验版 0.xx
            string ea = TryEarlyAccessVersion(key, map);
            if (!string.IsNullOrEmpty(ea))
                return ea;
            return null;
        }

        /// <summary>
        /// Prefix expand for splash "Early Access Version N.NN" using dict stem or built-in ZH.
        /// </summary>
        internal static string TryEarlyAccessVersion(string key, IDictionary<string, string> map)
        {
            const string enStem = "Early Access Version";
            if (string.IsNullOrEmpty(key) || !key.StartsWith(enStem, StringComparison.Ordinal))
                return null;
            if (key.Length == enStem.Length)
            {
                string stemZh;
                if (map != null && map.TryGetValue(enStem, out stemZh) && !string.IsNullOrEmpty(stemZh))
                    return stemZh;
                return "抢先体验版";
            }
            if (key[enStem.Length] != ' ')
                return null;
            string rest = key.Substring(enStem.Length); // includes leading space + version
            string prefixZh;
            if (map != null && map.TryGetValue(enStem, out prefixZh) && !string.IsNullOrEmpty(prefixZh))
                return prefixZh + rest;
            return "抢先体验版" + rest;
        }
    }
}
