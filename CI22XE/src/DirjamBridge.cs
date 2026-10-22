using System;
using System.Reflection;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Soft host for standalone Dirjam.dll (定向干扰).
    /// Type.GetType once — no AppDomain walk, no per-frame TypeByName.
    /// </summary>
    internal static class DirjamBridge
    {
        private static bool _tried;
        private static PropertyInfo _isActive;
        private static PropertyInfo _blocks;
        private static MethodInfo _isActiveFor;
        private static MethodInfo _suppress;
        private static MethodInfo _apply;
        private static MethodInfo _abort;
        private static readonly object[] OneArg = new object[1];

        internal static bool IsActive
        {
            get
            {
                Ensure();
                if (_isActive == null)
                    return false;
                try
                {
                    object v = _isActive.GetValue(null, null);
                    return v is bool && (bool)v;
                }
                catch
                {
                    return false;
                }
            }
        }

        internal static bool BlocksControls
        {
            get
            {
                Ensure();
                if (_blocks == null)
                    return IsActive;
                try
                {
                    object v = _blocks.GetValue(null, null);
                    return v is bool && (bool)v;
                }
                catch
                {
                    return false;
                }
            }
        }

        internal static bool IsActiveFor(Aircraft ac)
        {
            Ensure();
            if (_isActiveFor == null || ac == null)
                return false;
            try
            {
                OneArg[0] = ac;
                object v = _isActiveFor.Invoke(null, OneArg);
                return v is bool && (bool)v;
            }
            catch
            {
                return false;
            }
        }

        internal static bool ShouldSuppressHold(Aircraft ac)
        {
            Ensure();
            if (_suppress == null || ac == null)
                return false;
            try
            {
                OneArg[0] = ac;
                object v = _suppress.Invoke(null, OneArg);
                return v is bool && (bool)v;
            }
            catch
            {
                return false;
            }
        }

        internal static void ApplyLevelFlight(Aircraft ac)
        {
            Ensure();
            if (_apply == null || ac == null)
                return;
            try
            {
                OneArg[0] = ac;
                _apply.Invoke(null, OneArg);
            }
            catch { }
        }

        internal static void AbortForManualEject()
        {
            Ensure();
            if (_abort == null)
                return;
            try { _abort.Invoke(null, null); }
            catch { }
        }

        private static void Ensure()
        {
            if (_tried)
                return;
            _tried = true;
            try
            {
                Type t = Type.GetType("Dirjam.DirjamApi, Dirjam");
                if (t == null)
                    return;
                BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
                _isActive = t.GetProperty("IsActive", flags);
                _blocks = t.GetProperty("BlocksControls", flags);
                _isActiveFor = t.GetMethod("IsActiveFor", flags, null, new Type[] { typeof(Aircraft) }, null);
                _suppress = t.GetMethod("ShouldSuppressHold", flags, null, new Type[] { typeof(Aircraft) }, null);
                _apply = t.GetMethod("ApplyLevelFlight", flags, null, new Type[] { typeof(Aircraft) }, null);
                _abort = t.GetMethod("AbortForManualEject", flags, null, Type.EmptyTypes, null);
            }
            catch { }
        }
    }
}
