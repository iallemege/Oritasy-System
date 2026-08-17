using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Reflection bridge to WeXon.FlightAnalysis (merged DLL or optional WeXon pack).
    /// </summary>
    internal static class FlightScoreBridge
    {
        private static bool _resolved;
        private static Type _type;
        private static MethodInfo _prepare;
        private static MethodInfo _drawEmbedded;
        private static MethodInfo _closePanel;

        private static void Resolve()
        {
            if (_resolved && _type != null)
                return;
            try
            {
                Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < asms.Length; i++)
                {
                    try { _type = asms[i].GetType("WeXon.FlightAnalysis"); }
                    catch { _type = null; }
                    if (_type != null)
                        break;
                }
                if (_type == null)
                    return;
                BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                _prepare = _type.GetMethod("PrepareDisplayScore", flags);
                _drawEmbedded = _type.GetMethod("DrawEmbeddedScore", flags);
                _closePanel = _type.GetMethod("CloseScorePanel", flags);
                _resolved = true;
            }
            catch
            {
                _type = null;
            }
        }

        internal static void PrepareDisplay()
        {
            Resolve();
            if (_prepare == null)
                return;
            try { _prepare.Invoke(null, null); }
            catch { }
        }

        internal static void CloseStandalonePanel()
        {
            Resolve();
            if (_closePanel == null)
                return;
            try { _closePanel.Invoke(null, null); }
            catch { }
        }

        internal static bool DrawEmbedded(GUIStyle title, GUIStyle label, GUIStyle section, GUIStyle btn)
        {
            Resolve();
            if (_drawEmbedded == null)
                return false;
            try
            {
                _drawEmbedded.Invoke(null, new object[] { title, label, section, btn });
                return true;
            }
            catch { return false; }
        }
    }
}
