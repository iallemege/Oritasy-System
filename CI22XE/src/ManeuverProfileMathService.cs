using System;

namespace Oritasy
{
    /// <summary>
    /// Greenfield maneuver profile key / value bounds (0.0.9.72).
    /// ManeuverProfile owns ConfigEntry bind + Reset/Copy/Write.
    /// </summary>
    internal static class ManeuverProfileMathService
    {
        internal static string SanitizeSection(string key)
        {
            if (string.IsNullOrEmpty(key))
                return "Unknown";
            char[] chars = key.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' || c == '/'))
                    chars[i] = '_';
            }
            return new string(chars);
        }

        /// <summary>Corner speed may not exceed max speed.</summary>
        internal static float ClampCornerToMax(float cornerSpeed, float maxSpeed)
        {
            if (cornerSpeed > maxSpeed)
                return maxSpeed;
            return cornerSpeed;
        }
    }
}
