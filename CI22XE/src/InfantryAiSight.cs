using UnityEngine;

namespace Oritasy
{
    /// <summary>Infantry AI vision: FOV cone, range by target type, no wallhacks.</summary>
    internal static class InfantryAiSight
    {
        internal const float RangeSoldier = 480f;
        internal const float RangeVehicle = 720f;
        internal const float RangeAircraft = 1600f;
        internal const float FovHalfDeg = 48f;
        internal const float MemorySec = 4.5f;

        internal static bool CanSee(
            Vector3 eye, Vector3 fwd, Vector3 target, float maxRange,
            Transform self, Transform targetRoot)
        {
            Vector3 to = target - eye;
            float dist = to.magnitude;
            if (dist < 0.35f)
                return true;
            if (dist > maxRange)
                return false;
            if (fwd.sqrMagnitude < 0.01f)
                fwd = Vector3.forward;
            if (Vector3.Angle(fwd, to) > FovHalfDeg)
                return false;
            return HasLos(eye, target, self, targetRoot);
        }

        internal static bool HasLos(Vector3 from, Vector3 to, Transform self, Transform targetRoot)
        {
            Vector3 delta = to - from;
            float len = delta.magnitude;
            if (len < 0.4f)
                return true;
            Vector3 dir = delta / len;
            RaycastHit hit;
            if (!Physics.Raycast(from, dir, out hit, len, InfantryPerf.SightMask, QueryTriggerInteraction.Ignore))
                return true;
            if (IsUnder(hit.transform, self))
            {
                float rest = len - hit.distance - 0.05f;
                if (rest < 0.2f)
                    return true;
                Vector3 next = hit.point + dir * 0.12f;
                if (!Physics.Raycast(next, dir, out hit, rest, InfantryPerf.SightMask, QueryTriggerInteraction.Ignore))
                    return true;
            }
            if (hit.distance >= len - 0.28f)
                return true;
            if (IsUnder(hit.transform, targetRoot))
                return true;
            if (InfantryModeService.Active && InfantryModeService.PawnRoot != null
                && IsUnder(hit.transform, InfantryModeService.PawnRoot))
                return true;
            return false;
        }

        internal static bool WalkClear(Transform self, Vector3 wish, float ahead)
        {
            if (self == null || wish.sqrMagnitude < 0.01f)
                return true;
            Vector3 dir = wish.normalized;
            Vector3 origin = self.position + Vector3.up * 1.12f + dir * 0.32f;
            RaycastHit hit;
            if (!Physics.SphereCast(origin, 0.20f, dir, out hit, ahead, InfantryPerf.SightMask, QueryTriggerInteraction.Ignore))
                return true;
            if (IsUnder(hit.transform, self))
                return true;
            if (hit.normal.y > 0.58f)
                return true;
            return false;
        }

        private static bool IsUnder(Transform t, Transform root)
        {
            if (t == null || root == null)
                return false;
            Transform x = t;
            int n = 0;
            while (x != null && n < 12)
            {
                if (object.ReferenceEquals(x, root))
                    return true;
                x = x.parent;
                n++;
            }
            return false;
        }
    }
}
