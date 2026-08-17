using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Missiles that tunnel through colliders (high speed, thin airframes, aim look-ahead
    /// outside vanilla speed*0.25 proximity) still apply kinetic damage. Light targets
    /// overpenetrate; ships / buildings / spent missiles detonate.
    /// </summary>
    internal static class MissileOverpen
    {
        private static readonly FieldInfo PierceField = AccessTools.Field(typeof(Missile), "pierceDamage");
        private static readonly FieldInfo InfoField = AccessTools.Field(typeof(Missile), "info");
        private static readonly Dictionary<int, State> States = new Dictionary<int, State>(64);
        private static readonly RaycastHit[] Hits = new RaycastHit[16];
        private static float _nextPruneAt;

        private class State
        {
            public Vector3 LastPos;
            public bool HasLast;
            public int Hits;
            public float GraceUntil;
            public readonly HashSet<int> HitUnits = new HashSet<int>();
        }

        internal static bool Enabled
        {
            get
            {
                return Plugin.EnableMissileOverpen == null || Plugin.EnableMissileOverpen.Value;
            }
        }

        internal static bool TryHandle(Missile missile)
        {
            if (!Enabled || missile == null)
                return false;
            try
            {
                if (missile.disabled)
                    return Forget(missile);
                if (!missile.IsServer)
                    return false;
            }
            catch { return false; }

            if (Plugin.IsGunShellMissile(missile) || Plugin.IsMotorlessProjectile(missile))
                return false;
            if (AgmTDispenser.IsSafeDiscard(missile))
                return false;
            // RAM-45 / R9 / ship VLS: 80m look-ahead detonates on the launching hull after pitch-over.
            if (Plugin.IsShipLaunchedMissile(missile) || Plugin.IsSamRadarFamilyMissile(missile))
                return false;
            // Vanilla AGM-48 / AGM-68 (optical): overpen skips DetectCollisions and the
            // warhead never fuzes on tanks / buildings. Leave impact to vanilla.
            if (LeaveVanillaImpact(missile))
                return false;

            int id = missile.GetInstanceID();
            State st;
            if (!States.TryGetValue(id, out st) || st == null)
            {
                st = new State();
                States[id] = st;
            }

            Vector3 now = missile.transform.position;
            Vector3 vel = Vector3.zero;
            float speed = 1f;
            try
            {
                if (missile.rb != null)
                {
                    vel = missile.rb.velocity;
                    speed = vel.magnitude;
                }
                else
                    speed = missile.speed;
            }
            catch { }

            float dt = Time.fixedDeltaTime > 0.001f ? Time.fixedDeltaTime : 0.02f;
            Vector3 from = st.HasLast ? st.LastPos : now - vel * dt;
            st.LastPos = now;
            st.HasLast = true;

            if (speed < 25f)
                return Time.time < st.GraceUntil;

            float age = 0f;
            try { age = missile.timeSinceSpawn; }
            catch { }
            float minAge = Plugin.OverpenMinAge != null
                ? Plugin.OverpenMinAge.Value
                : MissileOverpenMathService.DefaultMinAge;
            if (age < minAge)
                return false;

            try
            {
                Unit launchOwner = missile.owner;
                if (launchOwner != null && launchOwner.transform != null)
                {
                    float ownR = MissileOverpenMathService.OwnerSafeRangeM;
                    if ((now - launchOwner.transform.position).sqrMagnitude < ownR * ownR)
                        return false;
                }
            }
            catch { }

            Vector3 dir = vel.sqrMagnitude > 1f ? vel.normalized : missile.transform.forward;
            float sweep = MissileOverpenMathService.SweepLength(from, now, speed, dt);
            float radius = Plugin.OverpenSphereRadius != null
                ? Plugin.OverpenSphereRadius.Value
                : MissileOverpenMathService.DefaultSphereRadius;
            if (radius < 1.2f)
                radius = 1.2f;

            int mask = ~0;
            try
            {
                mask = ~PhysicsLayers.ExclusionZonesMask.value;
                mask &= ~PhysicsLayers.IgnoreCollisionsMask.value;
            }
            catch { }

            int n = 0;
            try
            {
                n = Physics.SphereCastNonAlloc(from, radius, dir, Hits, sweep, mask,
                    QueryTriggerInteraction.Collide);
            }
            catch { return Time.time < st.GraceUntil; }

            bool handled = false;
            for (int i = 0; i < n; i++)
            {
                RaycastHit hit = Hits[i];
                if (hit.collider == null)
                    continue;
                if (hit.collider.transform != null
                    && hit.collider.transform.IsChildOf(missile.transform))
                    continue;

                IDamageable dmg = hit.collider.GetComponentInParent<IDamageable>();
                Unit unit = null;
                if (dmg != null)
                {
                    try { unit = dmg.GetUnit(); }
                    catch { }
                }
                if (unit == null)
                    unit = hit.collider.GetComponentInParent<Unit>();
                if (unit != null && object.ReferenceEquals(unit, missile))
                    continue;
                if (dmg == null && unit == null)
                    continue;
                if (missile.owner != null && unit != null
                    && object.ReferenceEquals(unit, missile.owner))
                    continue;
                if (IsLaunchPlatform(missile, unit))
                    continue;

                int uid = unit != null ? unit.GetInstanceID() : hit.collider.GetInstanceID();
                if (st.HitUnits.Contains(uid))
                    continue;
                if (unit != null && Plugin.IsSameFaction(Plugin.ResolveShooterSide(missile), unit))
                    continue;
                if (unit is Scenery)
                    continue;

                bool isBuilding = (dmg is MapBuilding) || (unit is Building);
                bool isGround = unit is GroundVehicle;
                MissileOverpenMathService.Decision dec = MissileOverpenMathService.Decide(
                    Plugin.IsNukeVariantMissile(missile),
                    AgmTWeapon.HasBusDispenser(missile),
                    false,
                    Plugin.IsBallisticMissile(missile) || Plugin.IsCruiseMissile(missile),
                    IsArmedSafe(missile),
                    age,
                    minAge,
                    speed,
                    st.Hits,
                    Plugin.OverpenMaxHits != null ? Plugin.OverpenMaxHits.Value : MissileOverpenMathService.DefaultMaxHits,
                    unit is Aircraft,
                    unit is Ship,
                    isBuilding,
                    isGround,
                    unit is Missile);

                if (unit is GroundVehicle
                    && dec == MissileOverpenMathService.Decision.KineticOverpen
                    && IsArmedSafe(missile))
                {
                    float tm = 0f;
                    try { tm = unit.GetMass(); }
                    catch { }
                    if (MissileOverpenMathService.VehicleStopsOverpen(tm, speed))
                        dec = MissileOverpenMathService.Decision.KineticDetonate;
                }

                if (dec == MissileOverpenMathService.Decision.Skip)
                    continue;

                ApplyKinetic(missile, dmg, unit, speed);
                st.HitUnits.Add(uid);
                st.Hits++;
                handled = true;

                if (dec == MissileOverpenMathService.Decision.KineticOverpen)
                {
                    BleedSpeed(missile, speed, st.Hits);
                    st.GraceUntil = Time.time + MissileOverpenMathService.GraceAfterOverpenSec;
                }
                else
                {
                    try
                    {
                        Vector3 nrm = hit.normal.sqrMagnitude > 0.01f ? hit.normal : -dir;
                        missile.Detonate(nrm, true, false);
                    }
                    catch { }
                    Forget(missile);
                    return true;
                }
            }

            Prune();
            if (handled)
                return true;
            return Time.time < st.GraceUntil;
        }

        /// <summary>AGM-48 / AGM-68 / other optical-laser surface missiles keep vanilla fuzing.</summary>
        private static bool LeaveVanillaImpact(Missile missile)
        {
            if (missile == null)
                return false;
            try
            {
                string n = missile.name;
                if (!string.IsNullOrEmpty(n))
                {
                    string s = n.ToLowerInvariant();
                    if (s.IndexOf("agm1", StringComparison.Ordinal) >= 0
                        || s.IndexOf("agm_heavy", StringComparison.Ordinal) >= 0
                        || s.IndexOf("agm-48", StringComparison.Ordinal) >= 0
                        || s.IndexOf("agm-68", StringComparison.Ordinal) >= 0
                        || s.IndexOf("agm48", StringComparison.Ordinal) >= 0
                        || s.IndexOf("agm68", StringComparison.Ordinal) >= 0)
                        return true;
                }
            }
            catch { }
            try
            {
                MissileSeeker sk = Plugin.SeekerField != null
                    ? Plugin.SeekerField.GetValue(missile) as MissileSeeker
                    : null;
                if (Plugin.IsSurfaceAttackSeeker(sk))
                    return true;
            }
            catch { }
            return false;
        }

        /// <summary>Owner turret, parent Ship, or any part of that same ship.</summary>
        private static bool IsLaunchPlatform(Missile missile, Unit unit)
        {
            if (missile == null || unit == null)
                return false;
            Unit owner = missile.owner;
            if (owner == null)
                return false;
            if (object.ReferenceEquals(unit, owner))
                return true;
            try
            {
                Ship launchShip = owner as Ship;
                if (launchShip == null)
                    launchShip = owner.GetComponentInParent<Ship>();
                if (launchShip == null)
                    return false;
                if (object.ReferenceEquals(unit, launchShip))
                    return true;
                Ship hitShip = unit as Ship;
                if (hitShip == null)
                    hitShip = unit.GetComponentInParent<Ship>();
                return hitShip != null && object.ReferenceEquals(hitShip, launchShip);
            }
            catch
            {
                return false;
            }
        }

        private static void ApplyKinetic(Missile missile, IDamageable dmg, Unit unit, float speed)
        {
            float mass = 80f;
            try
            {
                if (missile.rb != null && missile.rb.mass > 1f)
                    mass = missile.rb.mass;
                else if (InfoField != null)
                {
                    WeaponInfo info = InfoField.GetValue(missile) as WeaponInfo;
                    if (info != null && info.massPerRound > 1f)
                        mass = info.massPerRound;
                }
            }
            catch { }

            float scale = Plugin.OverpenKineticScale != null
                ? Plugin.OverpenKineticScale.Value
                : MissileOverpenMathService.DefaultKineticScale;
            float impact = MissileOverpenMathService.KineticImpact(mass, speed, scale);
            float pierce = 0f;
            try
            {
                if (PierceField != null)
                    pierce = (float)PierceField.GetValue(missile);
            }
            catch { }
            pierce = MissileOverpenMathService.KineticPierce(pierce, impact);

            PersistentID dealer = default(PersistentID);
            try
            {
                if (missile.owner != null)
                    dealer = missile.owner.persistentID;
                else
                    dealer = missile.persistentID;
            }
            catch
            {
                try { dealer = missile.persistentID; }
                catch { }
            }

            if (dmg != null)
            {
                try { dmg.TakeDamage(pierce, 0f, 1f, 0f, impact, dealer); }
                catch { }
            }
            if (unit != null)
            {
                try { unit.RecordDamage(dealer, impact); }
                catch { }
            }
        }

        private static void BleedSpeed(Missile missile, float speed, int hits)
        {
            if (missile == null || missile.rb == null)
                return;
            float keep = Plugin.OverpenSpeedKeep != null
                ? Plugin.OverpenSpeedKeep.Value
                : MissileOverpenMathService.DefaultSpeedKeep;
            float next = MissileOverpenMathService.KeepSpeed(speed, keep, hits - 1);
            Vector3 v = missile.rb.velocity;
            if (v.sqrMagnitude < 1f)
                return;
            missile.rb.velocity = v.normalized * next;
        }

        private static bool IsArmedSafe(Missile missile)
        {
            try { return missile.IsArmed(); }
            catch { return false; }
        }

        private static bool Forget(Missile missile)
        {
            if (missile == null)
                return false;
            States.Remove(missile.GetInstanceID());
            return false;
        }

        private static void Prune()
        {
            if (Time.time < _nextPruneAt)
                return;
            _nextPruneAt = Time.time + 2.5f;
            if (States.Count == 0)
                return;
            List<int> dead = null;
            foreach (KeyValuePair<int, State> kv in States)
            {
                // Instance ids of destroyed missiles linger; cap table.
                if (kv.Value == null || kv.Value.Hits > 8)
                {
                    if (dead == null)
                        dead = new List<int>(8);
                    dead.Add(kv.Key);
                }
            }
            if (dead == null && States.Count < 96)
                return;
            if (States.Count >= 96)
            {
                States.Clear();
                return;
            }
            for (int i = 0; i < dead.Count; i++)
                States.Remove(dead[i]);
        }
    }

    [HarmonyPatch(typeof(Missile), "DetectCollisions")]
    internal static class Patch_Missile_DetectCollisions_Overpen
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Missile __instance)
        {
            try
            {
                if (MissileOverpen.TryHandle(__instance))
                    return false;
            }
            catch { }
            return true;
        }
    }
}
