using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield performance budget tables (0.0.9.66).
    /// PerfMode owns preset state / auto-detect; this owns the numbers.
    /// </summary>
    internal static class PerfBudgetService
    {
        internal static float FlightSampleHzCap(bool isLow, bool isMedOrLower)
        {
            if (isLow) return 4.5f;
            if (isMedOrLower) return 6f;
            return 6f;
        }

        internal static int RwrStrokeSegments(int want, bool isLow, bool isMedOrLower)
        {
            if (isLow)
                return want > 12 ? 12 : (want < 8 ? 8 : want);
            if (isMedOrLower)
                return want > 20 ? 20 : want;
            return want;
        }

        internal static int RadarRingSegments(int want, bool isLow)
        {
            if (isLow)
                return want > 16 ? 16 : (want < 10 ? 10 : want);
            return want;
        }

        internal static int PersistCap(bool isLow)
        {
            return isLow ? 80 : 140;
        }

        internal static int ClutterCap(bool isLow)
        {
            return isLow ? 40 : 90;
        }

        internal static float ClutterMul(bool isLow)
        {
            return isLow ? 0.35f : 1f;
        }

        internal static bool RadarAscopeAllowed(bool isLow)
        {
            return !isLow;
        }

        internal static void MissileCamRtSize(bool manual, bool isLow, bool isMedOrLower, out int tw, out int th)
        {
            if (manual)
            {
                if (isLow)
                {
                    tw = Mathf.Clamp(Screen.width / 2, 480, 960);
                    th = Mathf.Clamp(Screen.height / 2, 270, 540);
                }
                else
                {
                    tw = Mathf.Clamp(Screen.width, 640, 1920);
                    th = Mathf.Clamp(Screen.height, 360, 1080);
                }
                return;
            }
            if (isLow)
            {
                tw = 320;
                th = 180;
            }
            else if (isMedOrLower)
            {
                tw = 480;
                th = 270;
            }
            else
            {
                tw = 640;
                th = 360;
            }
        }

        internal static void AircraftCamRtSize(bool isLow, out int tw, out int th)
        {
            if (isLow)
            {
                tw = 256;
                th = 144;
            }
            else
            {
                tw = 512;
                th = 288;
            }
        }

        internal static float ChineseFontScanInterval(bool isLow)
        {
            // FindObjectsOfType Text is a hitch — set_text prefix covers new labels.
            return isLow ? 45f : 30f;
        }

        internal static int ChineseFontOnEnableBudget(bool isLow)
        {
            return isLow ? 4 : 12;
        }

        internal static float HuntRadiusCap(bool isLow, bool isMedOrLower)
        {
            if (isLow) return 8000f;
            if (isMedOrLower) return 12000f;
            return 14000f;
        }

        internal static int HuntQueriesPerFrame(bool isLow)
        {
            return isLow ? 2 : 4;
        }

        internal static float HuntIntervalMin(bool isLow)
        {
            return isLow ? 0.5f : 0.5f;
        }

        internal static int ClampImguiFontSize(int size)
        {
            if (size < 12) return 12;
            if (size > 28) return 28;
            return size;
        }

        internal static bool AllowSlot(bool isLow, int frame, int slot, int period)
        {
            // Always stagger — previously Med/High ran every slot every frame (major CPU).
            if (period <= 1)
                return true;
            if (period < 2)
                period = 2;
            return ((frame + slot) % period) == 0;
        }
    }
}
