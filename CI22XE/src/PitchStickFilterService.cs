using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Skip a duplicate FilterInputs on the same physics tick for conventional jets
    /// (stale already-smoothed stick → left/right walk).
    /// Do not rewrite FBW/SAS outputs, and never drop FilterInputs on helos / hover /
    /// STOVL / autopilot — those passes ARE the flight control.
    /// </summary>
    internal static class PitchStickFilterService
    {
        private static int _acId;
        private static float _fixedTime = -1f;
        private static int _calls;
        private static int _everyId;
        private static bool _every;

        internal static bool AllowFilter(Aircraft ac)
        {
            if (!IsLocalPlayer(ac))
                return true;
            if (PlayerAutopilot.BlocksPlayerControls || HudPackState.ManualActive)
                return true;
            if (NeedsEveryFilterPass(ac))
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
            return _calls <= 1;
        }

        private static bool NeedsEveryFilterPass(Aircraft ac)
        {
            int id = ac.GetInstanceID();
            if (id == _everyId)
                return _every;
            _everyId = id;
            _every = DetectNeedsEvery(ac);
            return _every;
        }

        private static bool DetectNeedsEvery(Aircraft ac)
        {
            try
            {
                if (ac.IsAutoHoverEnabled())
                    return true;
            }
            catch { }
            try
            {
                string key = AircraftIdentity.GetKey(ac);
                if (AircraftIdentity.IsRotorcraft(key)
                    || AircraftIdentity.IsVl49(key)
                    || AircraftIdentity.IsStovlNozzle(ac))
                    return true;
            }
            catch { }
            try
            {
                if (ac.GetComponentInChildren<HeloControlsFilter>(true) != null)
                    return true;
                if (ac.GetComponentInChildren<RelaxedStabilityController>(true) != null)
                    return true;
                if (ac.GetComponentInChildren<TiltWingController>(true) != null)
                    return true;
            }
            catch { }
            return false;
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
