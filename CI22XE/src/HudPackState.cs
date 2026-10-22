using System;
using System.Reflection;
using BepInEx.Configuration;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Local leftover flags only. Oritasy Hud is a separate plugin and is not
    /// ticked or drawn from here.
    /// </summary>
    internal static class HudPackState
    {
        internal const string RwrDisplayName = "Oritasy RWR";

        internal static bool Present;
        internal static bool ManualActive;
        internal static Missile MitlFollowMissile;
        internal static bool LayoutMenuOpen;
        internal static bool WearGuiOpen;
        internal static bool WearConsumedF8;
        internal static bool IlsEnabled;
        internal static bool RocketCcipStandalone;

        internal static ConfigEntry<bool> ShowStickDisplay;
        internal static ConfigEntry<bool> RocketCcipEnabled;
        internal static ConfigEntry<bool> RocketCcipCepEnabled;
        internal static ConfigEntry<bool> RadarMfdEnabled;
        internal static ConfigEntry<float> RadarClutter;
        internal static ConfigEntry<float> RadarPersistence;
        internal static ConfigEntry<KeyCode> IlsOverlayKey;

        internal static Action TickCritical;
        internal static Action TickSlot0;
        internal static Action FixedTick;
        internal static Action DrawModule;
        internal static Action DrawRadar;
        internal static Action DrawFlightOverlays;
        internal static Action DrawGMeter;
        internal static Action DrawWearGui;
        internal static Action DrawMitl;
        internal static Action Shutdown;
        internal static Action CloseWearGui;
        internal static Action CloseRwrLayout;
        internal static Action DrawWearProfileToggle;
        internal static Func<bool> HasRwrLock;
        internal static Func<bool> HasRwrSearch;

        /// <summary>Hud plugin owns the drawn stick panel; F1 in Oritasy must write that same entry.</summary>
        internal static ConfigEntry<bool> StickDisplayEntry()
        {
            if (ShowStickDisplay == null)
                ShowStickDisplay = FindForeignStick();
            if (ShowStickDisplay != null)
                return ShowStickDisplay;
            return Plugin.ShowStickDisplay;
        }

        private static ConfigEntry<bool> FindForeignStick()
        {
            try
            {
                Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < asms.Length; i++)
                {
                    Type t = asms[i].GetType("Oritasy.Plugin");
                    if (t == null || object.ReferenceEquals(t, typeof(Plugin)))
                        continue;
                    FieldInfo f = t.GetField("ShowStickDisplay",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (f == null)
                        continue;
                    ConfigEntry<bool> e = f.GetValue(null) as ConfigEntry<bool>;
                    if (e != null)
                        return e;
                }
            }
            catch { }
            return null;
        }

        internal static void Run(Action action)
        {
            if (action != null)
                action();
        }

        internal static void CloseWear()
        {
            if (CloseWearGui != null)
                CloseWearGui();
        }

        internal static void CloseRwr()
        {
            if (CloseRwrLayout != null)
                CloseRwrLayout();
        }

        internal static bool HasRwrLockSafe()
        {
            return HasRwrLock != null && HasRwrLock();
        }

        internal static bool HasRwrSearchSafe()
        {
            return HasRwrSearch != null && HasRwrSearch();
        }
    }
}
