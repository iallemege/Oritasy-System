using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Player stick: skip the extra FilterInputs pass (stale filtered stick → left/right
    /// walk). Pitch-only: zero leftover roll/yaw, never inject bank-hold roll.
    /// </summary>
    internal static class PitchStickFilterService
    {
        private const float DeadRoll = 0.12f;
        private const float DeadYaw = 0.12f;
        private const float PitchOnly = 0.22f;

        private static int _acId;
        private static float _fixedTime = -1f;
        private static int _calls;
        private static float _rawPitch;
        private static float _rawRoll;
        private static float _rawYaw;

        internal static bool AllowFilter(Aircraft ac)
        {
            if (!IsLocalPlayer(ac))
                return true;
            float t = Time.fixedTime;
            int id = ac.GetInstanceID();
            if (id != _acId || t != _fixedTime)
            {
                _acId = id;
                _fixedTime = t;
                _calls = 0;
            }
            _calls++;
            if (_calls > 1)
                return false;
            CaptureRaw(ac);
            return true;
        }

        internal static void AfterFilter(Aircraft ac)
        {
            if (!IsLocalPlayer(ac))
                return;
            if (_calls != 1)
                return;
            if (PlayerAutopilot.BlocksPlayerControls || MissileCameraHud.ManualActive)
                return;

            ControlInputs inputs = null;
            try { inputs = ac.GetInputs(); }
            catch { }
            if (inputs == null)
                return;

            if (Mathf.Abs(_rawPitch) < PitchOnly)
                return;
            if (Mathf.Abs(_rawRoll) > DeadRoll || Mathf.Abs(_rawYaw) > DeadYaw)
                return;

            // Pitch-only: drop leftover roll/yaw. Do not command roll from bank attitude —
            // that made pull/push feel like left/right roll.
            inputs.roll = 0f;
            inputs.yaw = 0f;
        }

        private static void CaptureRaw(Aircraft ac)
        {
            ControlInputs inputs = null;
            try { inputs = ac.GetInputs(); }
            catch { }
            if (inputs == null)
            {
                _rawPitch = 0f;
                _rawRoll = 0f;
                _rawYaw = 0f;
                return;
            }
            _rawPitch = inputs.pitch;
            _rawRoll = inputs.roll;
            _rawYaw = inputs.yaw;
        }

        private static bool IsLocalPlayer(Aircraft ac)
        {
            if (ac == null || !Plugin.IsRuntimeInstance(ac))
                return false;
            try
            {
                Aircraft local;
                if (!GameManager.GetLocalAircraft(out local) || local == null)
                    return false;
                return object.ReferenceEquals(local, ac);
            }
            catch { return false; }
        }
    }
}
