using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Kh85MT
{
    /// <summary>
    /// TGM-85C sea-attack profile:
    /// 1) Lock on sea target → dive to 8–15 m skim
    /// 2) Hold skim altitude while tracking the enemy (no crash, no loft)
    /// 3) Left/right jink only while ship weapons / ship radar hard-lock this missile
    /// </summary>
    internal static class Kh85CFlight
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> TerrainClearAgl;
        internal static ConfigEntry<float> SeaSkimAglMin;
        internal static ConfigEntry<float> SeaSkimAglMax;
        internal static ConfigEntry<float> LookAheadTime;
        internal static ConfigEntry<float> TerminalRange;
        internal static ConfigEntry<float> JinkAmplitude;
        internal static ConfigEntry<float> JinkPeriod;
        internal static ConfigEntry<float> JinkHoldSeconds;
        internal static ConfigEntry<float> JinkMinRange;
        internal static ConfigEntry<float> LockPollRange;

        // Legacy keys still present in older cfg files — remapped in BindConfig.
        internal static ConfigEntry<float> SeaSkimAgl;
        internal static ConfigEntry<float> JinkMinRangeLegacy;
        internal static ConfigEntry<float> JinkMaxRangeLegacy;

        private static readonly FieldInfo SeekerField = AccessTools.Field(typeof(Missile), "seeker");
        private static readonly FieldInfo TargetField = AccessTools.Field(typeof(Missile), "target");
        private static readonly FieldInfo TerrainAvoidField = AccessTools.Field(typeof(OpticalSeeker), "terrainAvoidance");
        private static readonly FieldInfo LoftField = AccessTools.Field(typeof(OpticalSeeker), "loftAmount");
        private static readonly FieldInfo JinkField = AccessTools.Field(typeof(OpticalSeeker), "jinkEvasion");
        private static readonly FieldInfo JinkAmount = AccessTools.Field(typeof(JinkEvasion), "amount");
        private static readonly FieldInfo DragCurveField = AccessTools.Field(typeof(Missile), "dragCurve");
        private static readonly FieldInfo SupersonicDragField = AccessTools.Field(typeof(Missile), "supersonicDrag");

        private static readonly Collider[] OverlapBuf = new Collider[48];
        private const int ProbeMask = 8256;
        internal const float CruiseMach = 4f;
        internal const float MachToMs = 340.3f;
        internal const float DragScale = 0.42f;
        private static readonly HashSet<int> DragReducedIds = new HashSet<int>();

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind("FlightC", "Enabled", true,
                "TGM-85C: sea-skim on naval lock + reactive CIWS jink when ship-locked.");
            TerrainClearAgl = config.Bind("FlightC", "TerrainClearAgl", 55f,
                "Desired AGL (m) over land during terrain-follow cruise.");
            // Defaults raised — AGM_heavy armDelay=0; skim at 8m + wave pitch detonated (teleport vanish).
            SeaSkimAglMin = config.Bind("FlightC", "SeaSkimAglMin", 14f,
                "Minimum sea-skim AGL (m) after dive.");
            SeaSkimAglMax = config.Bind("FlightC", "SeaSkimAglMax", 22f,
                "Maximum sea-skim AGL (m) after dive.");
            // Keep old key so existing cfg does not confuse users; unused by new logic.
            SeaSkimAgl = config.Bind("FlightC", "SeaSkimAgl", 12f,
                "Legacy single skim AGL — ignored if SeaSkimAglMin/Max are set.");
            LookAheadTime = config.Bind("FlightC", "LookAheadTime", 4.5f,
                "Terrain look-ahead time (s) at current speed.");
            TerminalRange = config.Bind("FlightC", "TerminalRange", 1600f,
                "Inside this range (m), blend into a terminal dive on the target.");
            JinkAmplitude = config.Bind("FlightC", "JinkAmplitude", 280f,
                "Horizontal weave amplitude (m) while under ship weapon/radar lock.");
            JinkPeriod = config.Bind("FlightC", "JinkPeriod", 0.95f,
                "Horizontal weave period (s) while jinking.");
            JinkHoldSeconds = config.Bind("FlightC", "JinkHoldSeconds", 2.2f,
                "Keep jinking this many seconds after the last ship lock ping.");
            JinkMinRange = config.Bind("FlightC", "JinkStopRange", 450f,
                "Stop jinking inside this range (m) so the warhead can hit.");
            LockPollRange = config.Bind("FlightC", "LockPollRange", 7000f,
                "Scan radius (m) for ship weapons currently targeting this missile.");
            JinkMinRangeLegacy = config.Bind("FlightC", "JinkMinRange", 600f,
                "Legacy — unused.");
            JinkMaxRangeLegacy = config.Bind("FlightC", "JinkMaxRange", 12000f,
                "Legacy — unused.");
        }

        internal static bool IsEnabled()
        {
            return Enabled == null || Enabled.Value;
        }

        internal static float CruiseSpeedMps()
        {
            return CruiseMach * MachToMs;
        }

        internal static float ExtraThrustMul(Missile missile)
        {
            if (!IsEnabled() || !IsCVariant(missile))
                return 1f;
            return 6.2f;
        }

        internal static float ExtraFuelMul(Missile missile)
        {
            if (!IsEnabled() || !IsCVariant(missile))
                return 1f;
            return 2.6f;
        }

        internal static void ApplyDragReduction(Missile missile)
        {
            if (missile == null || DragCurveField == null)
                return;
            int id = missile.GetInstanceID();
            if (!DragReducedIds.Add(id))
                return;
            try
            {
                AnimationCurve curve = DragCurveField.GetValue(missile) as AnimationCurve;
                if (curve != null && curve.length > 0)
                {
                    Keyframe[] keys = curve.keys;
                    for (int i = 0; i < keys.Length; i++)
                    {
                        Keyframe k = keys[i];
                        k.value *= DragScale;
                        k.inTangent *= DragScale;
                        k.outTangent *= DragScale;
                        keys[i] = k;
                    }
                    curve.keys = keys;
                    DragCurveField.SetValue(missile, curve);
                }
                if (SupersonicDragField != null)
                {
                    float sd = (float)SupersonicDragField.GetValue(missile);
                    SupersonicDragField.SetValue(missile, sd * DragScale);
                }
            }
            catch { }
        }

        internal static void CapCruiseSpeed(Missile missile)
        {
            CapCruiseSpeed(missile, CruiseSpeedMps());
        }

        internal static void CapCruiseSpeed(Missile missile, float capMps)
        {
            if (missile == null || missile.rb == null)
                return;
            if (capMps < 80f)
                capMps = 80f;
            Vector3 v = missile.rb.velocity;
            if (v.sqrMagnitude > capMps * capMps)
                missile.rb.velocity = v.normalized * capMps;
        }

        internal static bool IsCVariant(Missile missile)
        {
            if (missile == null)
                return false;
            Kh85VariantTag tag = missile.GetComponent<Kh85VariantTag>();
            if (tag != null)
                return tag.Letter == "C";
            return Kh85Util.IsKh85(missile) && Kh85Util.GetVariant(missile) == "C";
        }

        internal static void TryAttach(Missile missile)
        {
            if (missile == null || !IsEnabled() || !IsCVariant(missile))
                return;
            if (missile.GetComponent<Kh85CFlightBrain>() != null)
                return;
            ConfigureSeeker(missile);
            ApplyDragReduction(missile);
            try { missile.gameObject.AddComponent<Kh85CFlightBrain>(); }
            catch { }
        }

        internal static void ConfigureSeeker(Missile missile)
        {
            try
            {
                OpticalSeeker opt = missile.GetComponent<OpticalSeeker>();
                if (opt == null)
                    opt = missile.GetComponentInChildren<OpticalSeeker>(true);
                if (opt == null)
                    return;

                if (TerrainAvoidField != null)
                    TerrainAvoidField.SetValue(opt, true);
                // Own altitude profile — kill vanilla loft / continuous jink.
                if (LoftField != null)
                    LoftField.SetValue(opt, 0f);

                object jink = JinkField != null ? JinkField.GetValue(opt) : null;
                if (jink != null && JinkAmount != null)
                    JinkAmount.SetValue(jink, 0f);
            }
            catch (Exception ex)
            {
                if (Plugin.DebugLog != null && Plugin.DebugLog.Value && Plugin.Log != null)
                    Plugin.Log.LogWarning("TGM-85C seeker tune: " + ex.Message);
            }
        }

        internal static void ApplyGuidance(Missile missile)
        {
            if (missile == null || !IsEnabled())
                return;
            if (Kh85Weapon.IsKnownNonKh85Missile(missile))
                return;
            // Hot path early-out: tag letter only (no InfoByKey).
            Kh85VariantTag tag = missile.GetComponent<Kh85VariantTag>();
            if (tag == null || tag.Letter != "C")
                return;
            if (Kh85MclosGate.ManualActive)
                return;
            if (missile.disabled)
                return;
            // Defer sea-skim / terrain aim until clear of the rail — early SetAimpoint into
            // ground/water is the standalone vanish-on-fire path (no Oritasy MM deferral).
            if (Kh85Weapon.ShouldDeferAim(missile))
                return;

            Kh85CFlightBrain brain = missile.GetComponent<Kh85CFlightBrain>();
            if (brain != null)
                brain.Steer();
            else
                SteerOnce(missile, null);
        }

        internal static float ResolveSkimAgl(Kh85CFlightBrain brain)
        {
            if (brain != null && brain.SkimAgl > 0.1f)
                return brain.SkimAgl;
            float min = SeaSkimAglMin != null ? SeaSkimAglMin.Value : 8f;
            float max = SeaSkimAglMax != null ? SeaSkimAglMax.Value : 15f;
            if (max < min)
            {
                float tmp = min;
                min = max;
                max = tmp;
            }
            // Floor at 12 m — armed AGM donor detonates if pitch dips below sea.
            min = Mathf.Clamp(min, 12f, 40f);
            max = Mathf.Clamp(max, min, 45f);
            return 0.5f * (min + max);
        }

        internal static void SteerOnce(Missile missile, Kh85CFlightBrain brain)
        {
            if (Kh85Weapon.ShouldDeferAim(missile))
                return;
            Unit target = ResolveTarget(missile);
            if (target == null)
                return;

            Vector3 mpos = missile.transform.position;
            Vector3 vel = Vector3.zero;
            float speed = 250f;
            try
            {
                if (missile.rb != null)
                {
                    vel = missile.rb.velocity;
                    speed = Mathf.Max(vel.magnitude, 80f);
                }
                else
                    speed = Mathf.Max(missile.speed, 80f);
            }
            catch { }

            Vector3 tpos = target.transform.position;
            Vector3 tvel = Vector3.zero;
            try
            {
                if (target.rb != null)
                    tvel = target.rb.velocity;
            }
            catch { }

            float dist = Vector3.Distance(mpos, tpos);
            Vector3 toTgt = tpos - mpos;
            float horizDist = new Vector3(toTgt.x, 0f, toTgt.z).magnitude;
            float closing = vel.sqrMagnitude > 1f ? Vector3.Dot(toTgt.normalized, vel) : dist;
            bool overshot = closing < 0f && dist > 80f;
            float terminal = TerminalRange != null ? TerminalRange.Value : 1600f;
            bool seaTarget = IsSeaTarget(target, tpos);
            float clearAgl = TerrainClearAgl != null ? TerrainClearAgl.Value : 55f;
            float skimAgl = ResolveSkimAgl(brain);

            Vector3 horiz = new Vector3(toTgt.x, 0f, toTgt.z);
            if (horiz.sqrMagnitude < 0.01f)
            {
                Vector3 fwd = vel.sqrMagnitude > 1f ? vel : missile.transform.forward;
                horiz = new Vector3(fwd.x, 0f, fwd.z);
            }
            if (horiz.sqrMagnitude < 0.01f)
                horiz = Vector3.forward;
            horiz.Normalize();

            float lookT = LookAheadTime != null ? LookAheadTime.Value : 4.5f;
            float lookDist = Mathf.Clamp(speed * lookT, 400f, 3500f);

            Vector3 aim = Kh85Weapon.EnergyLead(mpos, vel, speed, tpos, tvel);

            // P1: cache floor / forward probes at ~12 Hz (was 8–12 Raycast every FixedUpdate).
            float floorY;
            bool overWater;
            float obstacleY;
            ResolveFloor(missile, brain, mpos, horiz, lookDist, seaTarget, vel, speed,
                out floorY, out overWater, out obstacleY);
            bool skimMode = seaTarget;
            float desiredAgl = skimMode ? skimAgl : clearAgl;
            float cruiseY = floorY + desiredAgl;
            if (!skimMode && obstacleY > cruiseY)
                cruiseY = obstacleY;

            float skimY = floorY + skimAgl;
            float floorSafe = floorY + Mathf.Max(12f, skimMode ? skimAgl : 12f);
            float heightAbove = mpos.y - (skimMode ? skimY : cruiseY);
            if (heightAbove < 0f)
                heightAbove = 0f;

            // High + fast: shallow step-down overflies, then Mach-4 turn radius cannot recover.
            bool highDive = !overshot && heightAbove > 280f
                && (heightAbove > horizDist * 0.10f || horizDist < heightAbove * 5.5f + 1200f);
            bool recover = overshot;

            float speedCap = CruiseSpeedMps();
            if (recover)
                speedCap = MachToMs * 1.65f;
            else if (highDive || (dist < 4500f && heightAbove > 180f))
                speedCap = MachToMs * 2.25f;
            CapCruiseSpeed(missile, speedCap);
            try
            {
                if (missile.rb != null)
                {
                    vel = missile.rb.velocity;
                    speed = Mathf.Max(vel.magnitude, 80f);
                }
            }
            catch { }

            if (recover)
            {
                // Point back at the lock — no look-ahead past, no skim hold.
                aim = tpos + tvel * 0.35f;
                aim.y = Mathf.Max(tpos.y + 12f, floorSafe);
            }
            else if (highDive)
            {
                // Dive onto the intercept, not a 90 m/tick staircase that arrives high and late.
                float hSpd = new Vector3(vel.x, 0f, vel.z).magnitude;
                if (hSpd < 40f)
                    hSpd = 40f;
                float tHoriz = horizDist / hSpd;
                float needSink = heightAbove / Mathf.Max(tHoriz, 0.35f);
                aim = tpos + tvel * Mathf.Clamp(tHoriz, 0.12f, 4f) * 0.55f;
                float diveY = mpos.y - Mathf.Clamp(needSink * 0.55f, 180f, 2200f);
                aim.y = Mathf.Max(floorSafe, Mathf.Min(aim.y, diveY));
                if (aim.y > tpos.y + 40f && horizDist < 2500f)
                    aim.y = Mathf.Max(floorSafe, tpos.y + 18f);
            }
            else if (skimMode)
            {
                if (dist > terminal)
                {
                    float above = mpos.y - skimY;
                    float age = 1f;
                    try { age = Mathf.Max(missile.timeSinceSpawn, 0.45f); }
                    catch { }
                    float dropCap = age < 5f ? 90f : (age < 12f ? 160f : 280f);
                    if (above > 400f)
                    {
                        float stepY = Mathf.Max(skimY, mpos.y - dropCap);
                        aim.y = stepY;
                    }
                    else if (above > 80f)
                    {
                        float drop = Mathf.Clamp(above * 0.2f, 40f, dropCap);
                        aim.y = Mathf.Max(skimY, mpos.y - drop);
                    }
                    else if (above > 20f)
                    {
                        aim.y = Mathf.Lerp(mpos.y, skimY, 0.25f);
                    }
                    else
                    {
                        aim.y = skimY;
                    }
                    if (aim.y < floorSafe)
                        aim.y = floorSafe;

                    // Never command XY past the ship (Mach-4 look-ahead used to overshoot).
                    float lookHoriz = Mathf.Min(horizDist * 0.82f, Mathf.Clamp(speed * 1.15f, 400f, 1800f));
                    if (lookHoriz < 200f)
                        lookHoriz = Mathf.Min(horizDist, 200f);
                    Vector3 xy = mpos + horiz * lookHoriz;
                    aim.x = Mathf.Lerp(xy.x, tpos.x, 0.55f);
                    aim.z = Mathf.Lerp(xy.z, tpos.z, 0.55f);
                }
                else
                {
                    float minY = floorY + Mathf.Lerp(10f, skimAgl, Mathf.Clamp01(dist / terminal));
                    if (aim.y < minY)
                        aim.y = minY;
                    if (dist > 350f && mpos.y > skimY + 30f)
                        aim.y = Mathf.Min(aim.y, Mathf.Max(floorSafe, mpos.y - 180f));
                    aim.x = tpos.x;
                    aim.z = tpos.z;
                }
            }
            else if (dist > terminal)
            {
                float blend = Mathf.Clamp01((dist - terminal) / 4000f);
                aim.y = Mathf.Lerp(aim.y, cruiseY, 0.65f + 0.3f * blend);
                if (aim.y < cruiseY)
                    aim.y = cruiseY;
                float lookHoriz = Mathf.Min(horizDist * 0.82f, Mathf.Clamp(speed * 1.15f, 400f, 1800f));
                Vector3 xy = mpos + horiz * lookHoriz;
                aim.x = Mathf.Lerp(xy.x, tpos.x, 0.5f);
                aim.z = Mathf.Lerp(xy.z, tpos.z, 0.5f);
            }
            else
            {
                float minY = floorY + Mathf.Lerp(8f, desiredAgl, Mathf.Clamp01(dist / terminal));
                if (aim.y < minY)
                    aim.y = minY;
                aim.x = tpos.x;
                aim.z = tpos.z;
            }

            // Phase 3: jink ONLY while a ship radar/weapon has hard-locked us.
            float jStop = JinkMinRange != null ? JinkMinRange.Value : 450f;
            bool underLock = skimMode && !recover && !highDive && dist > jStop
                && IsUnderShipWeaponLock(missile, target, brain);
            if (underLock && speed > 150f)
            {
                float jAmp = JinkAmplitude != null ? JinkAmplitude.Value : 280f;
                float jPer = JinkPeriod != null ? JinkPeriod.Value : 0.95f;
                if (jPer < 0.2f)
                    jPer = 0.2f;
                float energy = Mathf.Clamp01((speed - 150f) / 150f);
                float phase = brain != null ? brain.JinkPhase : (missile.GetInstanceID() * 0.013f);
                float wave = Mathf.Sin((Time.time + phase) * (Mathf.PI * 2f / jPer));
                Vector3 side = Vector3.Cross(Vector3.up, horiz);
                if (side.sqrMagnitude > 0.001f)
                {
                    side.Normalize();
                    aim += side * (wave * jAmp * energy);
                }
                if (skimMode && dist > terminal)
                    aim.y = skimY;
            }

            Vector3 mvelDir = vel.sqrMagnitude > 1f ? vel.normalized : missile.transform.forward;
            Vector3 toAim = aim - mpos;
            if (toAim.sqrMagnitude > 0.01f)
            {
                Vector3 want = toAim.normalized;
                float ang = Vector3.Angle(mvelDir, want);
                float maxDeg = Kh85Weapon.MaxSteerOffBoresightDeg(speed, ang);
                if (recover)
                    maxDeg = 88f;
                else if (highDive)
                    maxDeg = Mathf.Max(maxDeg, 70f);
                else if (skimMode && mpos.y > floorY + skimAgl + 80f && !highDive)
                    maxDeg = Mathf.Min(maxDeg, speed < 160f ? 28f : 48f);
                if (ang > maxDeg)
                    want = Vector3.RotateTowards(mvelDir, want, maxDeg * Mathf.Deg2Rad, 0f);
                float look = recover
                    ? Mathf.Clamp(speed * 1.1f, 400f, 1600f)
                    : Mathf.Clamp(speed * 2.0f, 600f, 2800f);
                if (!recover && look > horizDist * 0.9f && horizDist > 80f)
                    look = horizDist * 0.9f;
                Vector3 clamped = mpos + want.normalized * look;

                if (skimMode && !recover && !highDive && dist > terminal)
                {
                    clamped.y = aim.y;
                    if (clamped.y < skimY)
                        clamped.y = skimY;
                    if (mpos.y < skimY + 50f && clamped.y > skimY + 25f)
                        clamped.y = skimY;
                }
                else if (recover || highDive)
                    clamped.y = aim.y;
                aim = clamped;
            }

            float age2 = 1f;
            try { age2 = Mathf.Max(missile.timeSinceSpawn, 0.45f); }
            catch { }
            float dropCap2;
            if (recover || highDive)
                dropCap2 = Mathf.Clamp(heightAbove * 0.55f, 400f, 2400f);
            else if (skimMode)
                dropCap2 = age2 < 5f ? 90f : 220f;
            else
                dropCap2 = age2 < 4f ? 120f : 320f;
            float minAgl = skimMode ? Mathf.Max(12f, skimAgl) : Mathf.Max(25f, desiredAgl * 0.5f);
            if (highDive || recover)
                minAgl = 12f;
            Kh85Weapon.SafeSetAimpoint(missile, aim, tvel, minAgl, dropCap2);
        }

        /// <summary>
        /// True when a ship radar hard-locks us (onRadarPing isTarget) or a ship weapon
        /// currently has this missile as its GetTarget().
        /// </summary>
        internal static bool IsUnderShipWeaponLock(Missile missile, Unit seaTarget, Kh85CFlightBrain brain)
        {
            if (missile == null)
                return false;
            if (brain != null && brain.IsJinkLatchActive())
                return true;

            // Cheap poll of the locked ship first.
            if (ShipWeaponsTracking(seaTarget, missile))
            {
                if (brain != null)
                    brain.LatchJinkFromPoll();
                return true;
            }

            if (brain != null && Time.time < brain.NextLockPoll)
                return false;
            // P1: poll less often / smaller radius — latch covers the gap.
            if (brain != null)
                brain.NextLockPoll = Time.time + 0.45f;

            float radius = LockPollRange != null ? LockPollRange.Value : 7000f;
            if (radius > 4500f)
                radius = 4500f;
            int hits = 0;
            try
            {
                hits = Physics.OverlapSphereNonAlloc(missile.transform.position, radius, OverlapBuf,
                    ~0, QueryTriggerInteraction.Ignore);
            }
            catch
            {
                return false;
            }

            HashSet<int> seen = null;
            for (int i = 0; i < hits; i++)
            {
                Collider c = OverlapBuf[i];
                if (c == null)
                    continue;
                Ship ship = null;
                try { ship = c.GetComponentInParent<Ship>(); }
                catch { }
                if (ship == null)
                    continue;
                if (seen == null)
                    seen = new HashSet<int>();
                int id = ship.GetInstanceID();
                if (!seen.Add(id))
                    continue;
                if (!IsHostileShip(missile, ship))
                    continue;
                if (ShipWeaponsTracking(ship, missile))
                {
                    if (brain != null)
                        brain.LatchJinkFromPoll();
                    return true;
                }
            }
            return false;
        }

        private static bool IsHostileShip(Missile missile, Ship ship)
        {
            if (missile == null || ship == null)
                return false;
            try
            {
                FactionHQ mHq = missile.NetworkHQ;
                FactionHQ sHq = ship.NetworkHQ;
                if (mHq != null && sHq != null && mHq == sHq)
                    return false;
            }
            catch { }
            return true;
        }

        private static bool ShipWeaponsTracking(Unit shipOrUnit, Missile self)
        {
            if (shipOrUnit == null || self == null)
                return false;
            try
            {
                Weapon[] weapons = shipOrUnit.GetComponentsInChildren<Weapon>(true);
                if (weapons == null)
                    return false;
                int mid = self.GetInstanceID();
                for (int i = 0; i < weapons.Length; i++)
                {
                    Weapon w = weapons[i];
                    if (w == null)
                        continue;
                    Unit t = null;
                    try { t = w.GetTarget(); }
                    catch { }
                    if (t == null)
                        continue;
                    if (t.GetInstanceID() == mid)
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static Unit ResolveTarget(Missile missile)
        {
            Unit raw = null;
            try
            {
                if (TargetField != null)
                    raw = TargetField.GetValue(missile) as Unit;
            }
            catch { }

            if (raw == null)
            {
                try
                {
                    if (SeekerField != null)
                    {
                        MissileSeeker seeker = SeekerField.GetValue(missile) as MissileSeeker;
                        if (seeker != null)
                        {
                            FieldInfo tu = AccessTools.Field(typeof(MissileSeeker), "targetUnit");
                            if (tu != null)
                                raw = tu.GetValue(seeker) as Unit;
                        }
                    }
                }
                catch { }
            }

            if (raw == null)
            {
                try
                {
                    PersistentID tid = missile.targetID;
                    if (tid.Id != 0u)
                    {
                        Unit u;
                        if (tid.TryGetUnit(out u))
                            raw = u;
                    }
                }
                catch { }
            }

            if (raw == null)
                return null;
            Unit owner = null;
            try { owner = missile.owner; }
            catch { }
            return Kh85Weapon.SanitizeLockTarget(owner, raw);
        }

        private static bool IsSeaTarget(Unit target, Vector3 tpos)
        {
            if (target is Ship)
                return true;
            try
            {
                if (target != null && target.GetComponentInParent<Ship>() != null)
                    return true;
            }
            catch { }

            // Explicit land unit types — never skim for these.
            try
            {
                if (target is GroundVehicle || target is Building || target is Aircraft)
                    return false;
            }
            catch { }

            float seaY = 0f;
            try { seaY = Datum.LocalSeaY; }
            catch { }

            // High / inland targets are land.
            if (tpos.y > seaY + 55f)
                return false;

            RaycastHit hit;
            Vector3 origin = new Vector3(tpos.x, Mathf.Max(tpos.y + 250f, seaY + 400f), tpos.z);
            if (Physics.Raycast(origin, Vector3.down, out hit, 800f, ProbeMask))
            {
                float gy = hit.point.y;
                // Dry ground well above sea.
                if (gy > seaY + 5f)
                    return false;
                // Sloping shoreline / embankment — treat as land when normal tilts.
                if (gy > seaY + 1.5f && hit.normal.y < 0.82f)
                    return false;
                if (gy <= seaY + 3f)
                    return true;
                return false;
            }

            // No ground hit near sea level → open water.
            return tpos.y < seaY + 45f;
        }

        /// <summary>Throttled floor + obstacle probes (~12 Hz) shared via brain cache.</summary>
        private static void ResolveFloor(Missile missile, Kh85CFlightBrain brain, Vector3 mpos,
            Vector3 horiz, float lookDist, bool seaTarget, Vector3 vel, float speed,
            out float floorY, out bool overWater, out float obstacleY)
        {
            if (brain != null && Time.time < brain.FloorCacheUntil)
            {
                floorY = brain.CachedFloorY;
                overWater = brain.CachedOverWater;
                obstacleY = brain.CachedObstacleY;
                return;
            }

            ProbeSurface(mpos, horiz, lookDist, seaTarget, out floorY, out overWater);
            obstacleY = 0f;
            if (!seaTarget)
                obstacleY = ProbeForwardObstacle(mpos, horiz, vel, speed);

            if (brain != null)
            {
                brain.CachedFloorY = floorY;
                brain.CachedOverWater = overWater;
                brain.CachedObstacleY = obstacleY;
                brain.FloorCacheUntil = Time.time + 0.08f; // ~12.5 Hz
            }
        }

        private static void ProbeSurface(Vector3 mpos, Vector3 horiz, float lookDist,
            bool preferSea, out float floorY, out bool overWater)
        {
            overWater = preferSea;
            float seaY = 0f;
            try { seaY = Datum.LocalSeaY; }
            catch { }

            float best = seaY;
            bool any = false;
            float[] fracs = new float[] { 0.15f, 0.35f, 0.55f, 0.8f, 1f };
            for (int i = 0; i < fracs.Length; i++)
            {
                Vector3 sample = mpos + horiz * (lookDist * fracs[i]);
                float y;
                bool water;
                if (SampleGround(sample, seaY, out y, out water))
                {
                    if (!any || y > best)
                        best = y;
                    any = true;
                    if (water)
                        overWater = true;
                }
            }

            float hereY;
            bool hereWater;
            if (SampleGround(mpos, seaY, out hereY, out hereWater))
            {
                if (!any || hereY > best)
                    best = hereY;
                any = true;
                if (hereWater)
                    overWater = true;
            }

            if (!any)
                best = preferSea ? seaY : (mpos.y - 80f);

            floorY = best;
        }

        private static bool SampleGround(Vector3 pos, float seaY, out float groundY, out bool water)
        {
            groundY = seaY;
            water = false;
            RaycastHit hit;
            // High-altitude launches need a long cast — old 2500 m miss → floorY=sea →
            // cruise aim snapped to ~0 and the missile dove/teleported into the dirt.
            Vector3 origin = new Vector3(pos.x, Mathf.Max(pos.y + 800f, seaY + 1200f), pos.z);
            float cast = Mathf.Max(4000f, origin.y - (seaY - 200f));

            if (Physics.Raycast(origin, Vector3.down, out hit, cast, ProbeMask))
            {
                groundY = hit.point.y;
                if (groundY <= seaY + 2.5f)
                {
                    water = true;
                    groundY = seaY;
                }
                return true;
            }

            water = true;
            groundY = seaY;
            return true;
        }

        private static float ProbeForwardObstacle(Vector3 mpos, Vector3 horiz, Vector3 vel, float speed)
        {
            float seaY = 0f;
            try { seaY = Datum.LocalSeaY; }
            catch { }

            Vector3 dir = horiz;
            if (vel.sqrMagnitude > 4f)
            {
                Vector3 v = vel.normalized;
                dir = Vector3.Lerp(horiz, new Vector3(v.x, 0f, v.z).normalized, 0.5f);
                if (dir.sqrMagnitude < 0.01f)
                    dir = horiz;
                dir.Normalize();
            }

            float maxRaise = seaY;
            float[] ranges = new float[] { 250f, 500f, 900f, 1400f };
            for (int i = 0; i < ranges.Length; i++)
            {
                float range = Mathf.Min(ranges[i], speed * 3.5f);
                Vector3 origin = mpos + Vector3.up * 8f;
                Vector3 rayDir = (dir * range + Vector3.down * (25f + i * 18f)).normalized;
                RaycastHit hit;
                if (Physics.Raycast(origin, rayDir, out hit, range + 80f, ProbeMask))
                {
                    if (hit.point.y <= seaY + 3f)
                        continue;
                    float need = hit.point.y + 40f + i * 8f;
                    if (need > maxRaise)
                        maxRaise = need;
                }
            }
            return maxRaise;
        }
    }

    public class Kh85CFlightBrain : MonoBehaviour
    {
        internal float JinkPhase;
        internal float SkimAgl;
        internal float NextLockPoll;
        internal float CachedFloorY;
        internal float CachedObstacleY;
        internal bool CachedOverWater;
        internal float FloorCacheUntil;
        private Missile _missile;
        private float _jinkUntil;
        private float _nextCfg;
        private Action<Aircraft.OnRadarWarning> _onPing;

        private void Awake()
        {
            _missile = GetComponent<Missile>();
            JinkPhase = UnityEngine.Random.Range(0f, 32f);
            float min = Kh85CFlight.SeaSkimAglMin != null ? Kh85CFlight.SeaSkimAglMin.Value : 8f;
            float max = Kh85CFlight.SeaSkimAglMax != null ? Kh85CFlight.SeaSkimAglMax.Value : 15f;
            if (max < min)
            {
                float tmp = min;
                min = max;
                max = tmp;
            }
            SkimAgl = UnityEngine.Random.Range(min, max);

            _onPing = OnRadarPing;
            try
            {
                if (_missile != null)
                    _missile.onRadarPing += _onPing;
            }
            catch { }
        }

        private void OnDestroy()
        {
            try
            {
                if (_missile != null && _onPing != null)
                    _missile.onRadarPing -= _onPing;
            }
            catch { }
        }

        private void OnRadarPing(Aircraft.OnRadarWarning e)
        {
            // Hard lock from a ship radar / ship-mounted emitter → start reactive jink.
            if (!e.isTarget)
                return;
            if (!IsShipEmitter(e))
                return;
            LatchJinkFromPoll();
        }

        private static bool IsShipEmitter(Aircraft.OnRadarWarning e)
        {
            try
            {
                if (e.emitter is Ship)
                    return true;
                if (e.emitter != null && e.emitter.GetComponentInParent<Ship>() != null)
                    return true;
            }
            catch { }
            try
            {
                if (e.radar != null && e.radar.GetComponentInParent<Ship>() != null)
                    return true;
            }
            catch { }
            return false;
        }

        internal void LatchJinkFromPoll()
        {
            float hold = Kh85CFlight.JinkHoldSeconds != null ? Kh85CFlight.JinkHoldSeconds.Value : 2.2f;
            if (hold < 0.4f)
                hold = 0.4f;
            _jinkUntil = Time.time + hold;
        }

        internal bool IsJinkLatchActive()
        {
            return Time.time < _jinkUntil;
        }

        private void FixedUpdate()
        {
            if (_missile == null)
                _missile = GetComponent<Missile>();
            if (_missile == null || !Kh85CFlight.IsEnabled())
                return;

            Kh85Weapon.EnsureMotors(_missile);
            Kh85CFlight.ApplyDragReduction(_missile);
            Kh85CFlight.CapCruiseSpeed(_missile);

            if (Time.time >= _nextCfg)
            {
                _nextCfg = Time.time + 1.5f;
                Kh85CFlight.ConfigureSeeker(_missile);
            }
        }

        internal void Steer()
        {
            if (_missile == null)
                return;
            Kh85CFlight.SteerOnce(_missile, this);
        }
    }

    /// <summary>
    /// Prefix (not Postfix): aim must be set BEFORE vanilla Steering turns.
    /// Priority.VeryLow so CI22XE MCLOS (Priority.Last) wins when ManualActive.
    /// </summary>
    [HarmonyPatch(typeof(Missile), "Steering")]
    internal static class Patch_Kh85C_Steering
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.VeryLow)]
        private static void Prefix(Missile __instance)
        {
            Kh85CFlight.ApplyGuidance(__instance);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(Missile __instance)
        {
            if (!Kh85CFlight.IsCVariant(__instance))
                return;
            Kh85CFlight.CapCruiseSpeed(__instance);
        }
    }

    /// <summary>
    /// AGM_heavy has armDelay=0. Sea-skim dips below LocalSeaY used to Detonate + zero velocity
    /// (looks like mid-flight teleport). Nudge C back above skim before vanilla water check.
    /// </summary>
    [HarmonyPatch(typeof(Missile), "DetectCollisions")]
    internal static class Patch_Kh85C_WaterSkim
    {
        private static readonly FieldInfo WaterSkimTargetField = AccessTools.Field(typeof(Missile), "target");

        [HarmonyPrefix]
        private static void Prefix(Missile __instance)
        {
            if (__instance == null || Kh85Weapon.IsKnownNonKh85Missile(__instance)
                || !Kh85CFlight.IsEnabled() || !Kh85CFlight.IsCVariant(__instance))
                return;
            try
            {
                if (__instance.disabled)
                    return;
                float seaY = Datum.LocalSeaY;
                Vector3 p = __instance.transform.position;
                if (p.y >= seaY + 2f)
                    return;

                Unit tgt = null;
                try
                {
                    if (WaterSkimTargetField != null)
                        tgt = WaterSkimTargetField.GetValue(__instance) as Unit;
                }
                catch { }
                if (tgt != null)
                {
                    float dist = Vector3.Distance(p, tgt.transform.position);
                    float terminal = Kh85CFlight.TerminalRange != null ? Kh85CFlight.TerminalRange.Value : 1600f;
                    // Only rescue mid-course skim — allow real terminal impacts.
                    if (dist < terminal * 0.55f)
                        return;
                }

                Kh85CFlightBrain brain = __instance.GetComponent<Kh85CFlightBrain>();
                float skim = Kh85CFlight.ResolveSkimAgl(brain);
                p.y = seaY + Mathf.Max(12f, skim);
                __instance.transform.position = p;
                if (__instance.rb != null)
                {
                    __instance.rb.MovePosition(p);
                    Vector3 v = __instance.rb.velocity;
                    if (v.y < 0f)
                    {
                        v.y = Mathf.Max(v.y * 0.15f, -8f);
                        __instance.rb.velocity = v;
                    }
                }
            }
            catch { }
        }
    }
}
