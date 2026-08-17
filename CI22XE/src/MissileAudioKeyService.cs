using System;
using System.IO;
using System.Text;

namespace Oritasy
{
    /// <summary>
    /// Greenfield missile-audio key / category parsing (0.0.9.65).
    /// MissileAudio owns loading, indexing, and clip apply.
    /// </summary>
    internal static class MissileAudioKeyService
    {
        internal static readonly string[] Categories = new string[]
        {
            "launch", "motor", "loop", "explode", "proximity"
        };

        internal static string NormalizeKey(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return null;
            StringBuilder sb = new StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                    sb.Append(char.ToLowerInvariant(c));
                else if (char.IsWhiteSpace(c) || c == '/' || c == '\\' || c == '.')
                    sb.Append('_');
            }
            string s = sb.ToString().Trim('_');
            while (s.IndexOf("__", StringComparison.Ordinal) >= 0)
                s = s.Replace("__", "_");
            return string.IsNullOrEmpty(s) ? null : s;
        }

        internal static bool IsCategory(string s)
        {
            if (string.IsNullOrEmpty(s))
                return false;
            for (int i = 0; i < Categories.Length; i++)
            {
                if (string.Equals(s, Categories[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        internal static string CanonicalCategory(string cat)
        {
            if (string.IsNullOrEmpty(cat))
                return null;
            if (string.Equals(cat, "proximity", StringComparison.OrdinalIgnoreCase))
                return "explode";
            for (int i = 0; i < Categories.Length; i++)
            {
                if (string.Equals(cat, Categories[i], StringComparison.OrdinalIgnoreCase))
                    return Categories[i];
            }
            return cat.ToLowerInvariant();
        }

        internal static bool TrySplitKeyCategory(string fileNorm, out string key, out string category)
        {
            key = null;
            category = null;
            if (string.IsNullOrEmpty(fileNorm))
                return false;

            for (int i = 0; i < Categories.Length; i++)
            {
                string suffix = "_" + Categories[i];
                if (fileNorm.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    key = fileNorm.Substring(0, fileNorm.Length - suffix.Length);
                    category = Categories[i];
                    return !string.IsNullOrEmpty(key);
                }
            }
            return false;
        }

        internal static bool TryParseFile(string path, string root, out string key, out string category)
        {
            key = null;
            category = null;
            string file = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(file))
                return false;

            string parent = null;
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !string.Equals(dir, root, StringComparison.OrdinalIgnoreCase))
                    parent = Path.GetFileName(dir);
            }
            catch { }

            string parentNorm = NormalizeKey(parent);
            string fileNorm = NormalizeKey(file);

            if (!string.IsNullOrEmpty(parentNorm) && IsCategory(parentNorm))
            {
                category = CanonicalCategory(parentNorm);
                key = fileNorm;
                return !string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(category);
            }

            if (!string.IsNullOrEmpty(parentNorm) && IsCategory(fileNorm))
            {
                category = CanonicalCategory(fileNorm);
                key = parentNorm;
                return !string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(category);
            }

            string cat;
            if (TrySplitKeyCategory(fileNorm, out key, out cat))
            {
                category = CanonicalCategory(cat);
                return !string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(category);
            }

            return false;
        }

        internal static string IndexKey(string key, string category)
        {
            return key + "|" + category;
        }
    }
}
