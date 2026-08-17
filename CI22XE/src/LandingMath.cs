using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield runway / track geometry helpers (0.0.9.58).
    /// </summary>
    internal static class LandingMath
    {
        /// <summary>Horizontal track for heading/stern alignment (velocity preferred).</summary>
        internal static Vector3 FlatTrackDir(Aircraft ac)
        {
            Vector3 track = Vector3.forward;
            try
            {
                if (ac != null && ac.rb != null && ac.rb.velocity.sqrMagnitude > 25f)
                    track = ac.rb.velocity;
                else if (ac != null)
                    track = ac.transform.forward;
            }
            catch
            {
                try { track = ac.transform.forward; }
                catch { track = Vector3.forward; }
            }
            track.y = 0f;
            if (track.sqrMagnitude < 0.01f)
                track = Vector3.forward;
            return track.normalized;
        }

        /// <summary>
        /// along&gt;0 = still astern of threshold; along&lt;0 = past threshold (overshot).
        /// </summary>
        internal static void RunwayAlongLateral(Vector3 acPos, Vector3 touchPos, Vector3 rwyDir,
            out float along, out float lateral)
        {
            Vector3 d = acPos - touchPos;
            d.y = 0f;
            along = -Vector3.Dot(d, rwyDir);
            Vector3 right = Vector3.Cross(Vector3.up, rwyDir);
            if (right.sqrMagnitude < 0.01f)
                right = Vector3.right;
            right.Normalize();
            lateral = Vector3.Dot(d, right);
        }
    }
}
