using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Gamepad right stick is Rewired Pan View / Tilt View (Absolute).
    /// Vanilla virtual-joystick mode spends those axes on the mouse stick and
    /// zeroes cockpit look unless Free Look is held. Also, look is gated on
    /// !Cursor.visible — a desynced cursor after IMGUI menus kills the stick.
    /// Mouse fly uses the same Pan/Tilt actions: never steal VJ for mouse, or
    /// cockpit pan/tilt snaps 0 every other frame (camera jump).
    /// </summary>
    internal static class GamepadLookService
    {
        private const float Dead = 0.12f;
        private const float MouseDeltaGate = 0.02f;

        private static FieldInfo _playerInputField;
        private static MethodInfo _getAxis;
        private static MethodInfo _getMode;
        private static MethodInfo _getSources;
        private static PropertyInfo _sourceControllerType;
        private static readonly object[] AxisArgs = new object[1];
        private static bool _resolved;
        private static bool _sourcesResolved;

        internal static void Tick()
        {
            OritasyCursor.SyncIfDesynced();
        }

        internal static void ClearImguiFocusIfIdle()
        {
            if (OritasyCursor.Held)
                return;
            try
            {
                GUIUtility.keyboardControl = 0;
            }
            catch { }
        }

        internal static bool StickWantsLook()
        {
            if (OritasyCursor.Held)
                return false;
            try
            {
                if (RadialMenuMain.IsInUse())
                    return false;
                if (DynamicMap.mapMaximized)
                    return false;
            }
            catch { }

            if (MouseIsDrivingView())
                return false;

            float pan = ReadAxis("Pan View");
            float tilt = ReadAxis("Tilt View");
            if (Mathf.Abs(pan) < Dead && Mathf.Abs(tilt) < Dead)
                return false;
            if (!IsAbsolute("Pan View") && !IsAbsolute("Tilt View"))
                return false;
            return ActionHasJoystick("Pan View") || ActionHasJoystick("Tilt View");
        }

        private static bool MouseIsDrivingView()
        {
            try
            {
                if (Mathf.Abs(Input.GetAxisRaw("Mouse X")) > MouseDeltaGate)
                    return true;
                if (Mathf.Abs(Input.GetAxisRaw("Mouse Y")) > MouseDeltaGate)
                    return true;
            }
            catch { }
            return false;
        }

        private static bool ActionHasJoystick(string action)
        {
            object pi = PlayerInput();
            if (pi == null)
                return false;
            EnsureSources(pi);
            if (_getSources == null)
                return true;
            try
            {
                AxisArgs[0] = action;
                object list = _getSources.Invoke(pi, AxisArgs);
                IEnumerable e = list as IEnumerable;
                if (e == null)
                    return true;
                bool any = false;
                foreach (object src in e)
                {
                    if (src == null)
                        continue;
                    any = true;
                    if (SourceIsJoystick(src))
                        return true;
                }
                if (!any)
                    return false;
            }
            catch
            {
                return true;
            }
            return false;
        }

        private static bool SourceIsJoystick(object src)
        {
            if (src == null)
                return false;
            try
            {
                if (_sourceControllerType == null)
                    _sourceControllerType = src.GetType().GetProperty("controllerType");
                if (_sourceControllerType == null)
                    return false;
                object t = _sourceControllerType.GetValue(src, null);
                if (t == null)
                    return false;
                string n = t.ToString();
                if (string.Equals(n, "Joystick", StringComparison.OrdinalIgnoreCase))
                    return true;
                int v = Convert.ToInt32(t);
                return v == 2;
            }
            catch
            {
                return false;
            }
        }

        private static void EnsureSources(object pi)
        {
            if (_sourcesResolved || pi == null)
                return;
            _sourcesResolved = true;
            try
            {
                _getSources = pi.GetType().GetMethod("GetCurrentInputSources",
                    new Type[] { typeof(string) });
            }
            catch
            {
                _getSources = null;
            }
        }

        internal static void PrepareVanillaLook()
        {
            if (OritasyCursor.Held)
                return;
            OritasyCursor.SyncIfDesynced();
        }

        private static void EnsureRewired()
        {
            if (_resolved)
                return;
            _resolved = true;
            try
            {
                _playerInputField = typeof(GameManager).GetField("playerInput",
                    BindingFlags.Public | BindingFlags.Static);
                object pi = _playerInputField != null ? _playerInputField.GetValue(null) : null;
                if (pi == null)
                    return;
                Type t = pi.GetType();
                _getAxis = t.GetMethod("GetAxis", new Type[] { typeof(string) });
                _getMode = t.GetMethod("GetAxisCoordinateMode", new Type[] { typeof(string) });
            }
            catch { }
        }

        private static object PlayerInput()
        {
            EnsureRewired();
            if (_playerInputField == null)
                return null;
            try { return _playerInputField.GetValue(null); }
            catch { return null; }
        }

        private static float ReadAxis(string name)
        {
            object pi = PlayerInput();
            if (pi == null || _getAxis == null)
                return 0f;
            try
            {
                AxisArgs[0] = name;
                object v = _getAxis.Invoke(pi, AxisArgs);
                if (v is float)
                    return (float)v;
            }
            catch { }
            return 0f;
        }

        private static bool IsAbsolute(string name)
        {
            object pi = PlayerInput();
            if (pi == null || _getMode == null)
                return false;
            try
            {
                AxisArgs[0] = name;
                object mode = _getMode.Invoke(pi, AxisArgs);
                if (mode == null)
                    return false;
                return Convert.ToInt32(mode) == 0;
            }
            catch { }
            return false;
        }
    }

    [HarmonyPatch(typeof(CameraCockpitState), "UpdateState")]
    [HarmonyPriority(Priority.First)]
    internal static class Patch_Cockpit_GamepadLook
    {
        private static bool _restoreVj;

        private static void Prefix()
        {
            _restoreVj = false;
            GamepadLookService.PrepareVanillaLook();
            if (!GamepadLookService.StickWantsLook())
                return;
            if (!PlayerSettings.virtualJoystickEnabled)
                return;
            PlayerSettings.virtualJoystickEnabled = false;
            _restoreVj = true;
        }

        private static void Postfix()
        {
            if (_restoreVj)
                PlayerSettings.virtualJoystickEnabled = true;
        }
    }

    [HarmonyPatch(typeof(CameraOrbitState), "UpdateState")]
    [HarmonyPriority(Priority.First)]
    internal static class Patch_Orbit_GamepadLook
    {
        private static void Prefix()
        {
            GamepadLookService.PrepareVanillaLook();
        }
    }

    [HarmonyPatch(typeof(PilotPlayerState), "PlayerAxisControls")]
    [HarmonyPriority(Priority.First)]
    internal static class Patch_PlayerAxis_GamepadLook
    {
        private static bool _restoreVj;

        private static void Prefix()
        {
            _restoreVj = false;
            if (!GamepadLookService.StickWantsLook())
                return;
            if (!PlayerSettings.virtualJoystickEnabled)
                return;
            PlayerSettings.virtualJoystickEnabled = false;
            _restoreVj = true;
        }

        private static void Postfix()
        {
            if (_restoreVj)
                PlayerSettings.virtualJoystickEnabled = true;
        }
    }
}
