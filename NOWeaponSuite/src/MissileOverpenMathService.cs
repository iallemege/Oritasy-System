using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Overpenetration + kinetic hit math. MissileOverpen owns SphereCast / TakeDamage / Detonate.
    /// </summary>
    internal static class MissileOverpenMathService
    {
        internal const float DefaultKineticScale = 8f;
        internal const float DefaultSphereRadius = 3.5f;
        internal const float DefaultSpeedKeep = 0.65f;
        internal const float DefaultMinAge = 0.35f;
        internal const int DefaultMaxHits = 2;
        internal const float MinMassKg = 40f;
        internal const float AirOverpenMinSpeed = 200f;
        internal const float VehicleOverpenMinSpeed = 220f;
        internal const float VehicleOverpenMaxMass = 12000f;
        internal const float ExtraLookSec = 0.55f;
        internal const float GraceAfterOverpenSec = 0.16f;
        /// <summary>Do not overpen-detonate on the launching unit while still this close (m).</summary>
        internal const float OwnerSafeRangeM = 400f;

        internal enum Decision
        {
            Skip = 0,
            KineticOverpen = 1,
            KineticDetonate = 2
        }

        internal static float KineticImpact(float massKg, float speedMps, float scale)
        {
            massKg = Mathf.Max(massKg, MinMassKg);
            speedMps = Mathf.Max(speedMps, 1f);
            return massKg * speedMps * Mathf.Max(0.05f, scale);
        }

        internal static float KineticPierce(float warheadPierce, float impact)
        {
            float p = warheadPierce > 0.01f ? warheadPierce : 0f;
            return Mathf.Max(p, impact * 0.45f);
        }

        internal static float KeepSpeed(float speed, float keepFrac, int hitsSoFar)
        {
            float k = Mathf.Clamp(keepFrac, 0.35f, 0.92f);
            if (hitsSoFar > 0)
                k *= Mathf.Pow(0.85f, hitsSoFar);
            return Mathf.Max(40f, speed * k);
        }

        internal static float SweepLength(Vector3 from, Vector3 to, float speed, float dt)
        {
            float d = Vector3.Distance(from, to);
            float min = Mathf.Max(1.2f, speed * Mathf.Max(dt, 0.016f) * ExtraLookSec);
            return Mathf.Min(Mathf.Max(d, min), 80f);
        }

        internal static Decision Decide(
            bool nuke,
            bool clusterBus,
            bool gunShell,
            bool ballisticOrCruise,
            bool armed,
            float age,
            float minAge,
            float speed,
            int hits,
            int maxHits,
            bool targetAir,
            bool targetShip,
            bool targetBuilding,
            bool targetGroundVehicle,
            bool targetMissile)
        {
            if (clusterBus || gunShell)
                return Decision.Skip;
            if (age < minAge)
                return Decision.Skip;
            if (maxHits < 1)
                maxHits = 1;
            if (nuke)
                return armed ? Decision.KineticDetonate : Decision.KineticOverpen;
            if (!armed)
                return Decision.Skip;
            if (targetMissile)
                return Decision.KineticDetonate;
            if (targetShip || targetBuilding || targetGroundVehicle || ballisticOrCruise)
                return Decision.KineticDetonate;
            if (hits >= maxHits)
                return Decision.KineticDetonate;
            if (targetAir)
            {
                if (speed < AirOverpenMinSpeed)
                    return Decision.KineticDetonate;
                return Decision.KineticOverpen;
            }
            if (speed < VehicleOverpenMinSpeed)
                return Decision.KineticDetonate;
            return Decision.KineticOverpen;
        }

        internal static bool VehicleStopsOverpen(float targetMass, float speed)
        {
            if (targetMass >= VehicleOverpenMaxMass)
                return true;
            return speed < VehicleOverpenMinSpeed;
        }
    }
}
