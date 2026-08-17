using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield missile-camera chase / nose pose math (0.0.9.95).
    /// MissileCameraHud owns camera GameObjects and SyncSecondaryCameraFromMain.
    /// </summary>
    internal static class MissileCameraChaseMathService
    {
        internal const float ChaseBackM = 4.5f;
        internal const float ChaseUpM = 1.1f;
        internal const float NoseForwardM = 1.15f;
        internal const float NoseUpM = 0.08f;
        internal const float MinVelSqrForFwd = 25f;

        internal static Vector3 ResolveChaseForward(Transform mt, Rigidbody rb)
        {
            Vector3 fwd = mt != null ? mt.forward : Vector3.forward;
            try
            {
                if (rb != null && rb.velocity.sqrMagnitude > MinVelSqrForFwd)
                    fwd = rb.velocity.normalized;
            }
            catch { }
            if (fwd.sqrMagnitude < 0.01f && mt != null)
                fwd = mt.forward;
            return fwd;
        }

        internal static Vector3 ResolveChaseUp(Transform mt, Vector3 fwd)
        {
            Vector3 up = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(fwd, up)) > 0.95f && mt != null)
                up = mt.up;
            return up;
        }

        internal static void ChasePose(Transform mt, Rigidbody rb, out Vector3 pos, out Quaternion rot)
        {
            Vector3 fwd = ResolveChaseForward(mt, rb);
            Vector3 up = ResolveChaseUp(mt, fwd);
            Vector3 origin = mt != null ? mt.position : Vector3.zero;
            pos = origin - fwd * ChaseBackM + up * ChaseUpM;
            rot = Quaternion.LookRotation(fwd, up);
        }

        internal static void NosePose(Transform mt, out Vector3 pos, out Quaternion rot)
        {
            if (mt == null)
            {
                pos = Vector3.zero;
                rot = Quaternion.identity;
                return;
            }
            // Ahead of nose so body/flame FX behind the lens stay out of the near clip
            pos = mt.position + mt.forward * NoseForwardM + mt.up * NoseUpM;
            rot = mt.rotation;
        }
    }
}
