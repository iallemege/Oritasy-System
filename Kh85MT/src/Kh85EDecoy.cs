using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Kh85MT
{
    /// <summary>
    /// TGM-85E Torjan — air-launched powered decoy.
    /// Huge radar signature; hostile missiles within 6 km that are locked onto
    /// other friendlies are pulled onto the E decoy.
    /// </summary>
    internal static class Kh85EDecoy
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> RadarReturnMul;
        internal static ConfigEntry<float> SiblingReturnMul;
        internal static ConfigEntry<float> RcsBoost;
        internal static ConfigEntry<float> SalvoRadius;
        internal static ConfigEntry<float> AttractRadius;
        internal static ConfigEntry<float> AttractInterval;
        internal static ConfigEntry<float> LoftAltitude;
        internal static ConfigEntry<float> TerminalRange;
        internal static ConfigEntry<float> DiveStartRange;

        private static readonly List<Missile> LiveDecoys = new List<Missile>(16);
        private static readonly List<Missile> LiveSceneMissiles = new List<Missile>(96);
        private static readonly FieldInfo MissileTargetField = AccessTools.Field(typeof(Missile), "target");
        private static readonly FieldInfo SeekerField = AccessTools.Field(typeof(Missile), "seeker");
        private static readonly FieldInfo SeekerTargetField = AccessTools.Field(typeof(MissileSeeker), "targetUnit");
        private static readonly FieldInfo SarhTargetTransform = AccessTools.Field(typeof(SARHSeeker), "targetTransform");
        private static readonly FieldInfo SarhKnownPos = AccessTools.Field(typeof(SARHSeeker), "knownPos");
        private static readonly FieldInfo SarhKnownVel = AccessTools.Field(typeof(SARHSeeker), "knownVel");
        private static readonly FieldInfo SarhTimeWithoutTrack = AccessTools.Field(typeof(SARHSeeker), "timeWithoutTrack");
        private static readonly Collider[] OverlapBuf = new Collider[512];
        internal const float DefaultLockStealRadiusM = 6000f;

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind("DecoyE", "Enabled", true,
                "TGM-85E Torjan: powered decoy — inflate RCS and steal hostile locks from nearby friendlies.");
            RadarReturnMul = config.Bind("DecoyE", "RadarReturnMul", 12f,
                "Multiply GetRadarReturn for the E decoy (enemy radars prefer it).");
            SiblingReturnMul = config.Bind("DecoyE", "SiblingReturnMul", 0.2f,
                "Multiply GetRadarReturn for other TGM-85s when a friendly E is nearby in the salvo.");
            RcsBoost = config.Bind("DecoyE", "RcsBoost", 80f,
                "Unit.ModifyRCS boost applied once on E spawn.");
            SalvoRadius = config.Bind("DecoyE", "SalvoRadius", 9000f,
                "Radius (m) to count other TGM-85 as the same salvo package.");
            AttractRadius = config.Bind("DecoyE", "AttractRadius", DefaultLockStealRadiusM,
                "Radius (m) to steal hostile missiles locked on other friendlies onto E.");
            if (AttractRadius.Value > 11999f && AttractRadius.Value < 12001f)
                AttractRadius.Value = DefaultLockStealRadiusM;
            AttractInterval = config.Bind("DecoyE", "AttractInterval", 0.2f,
                "Seconds between lock-steal pulses.");
            LoftAltitude = config.Bind("DecoyE", "LoftAltitude", 9500f,
                "Cruise loft altitude (m ASL) while tracking a target — stay high to avoid friendly locks.");
            TerminalRange = config.Bind("DecoyE", "TerminalRange", 4500f,
                "Inside this range (m), begin blending into a steep dive on the target.");
            DiveStartRange = config.Bind("DecoyE", "DiveStartRange", 2200f,
                "Inside this range (m), commit to a steep dive onto the target.");
        }

        internal static bool IsEnabled()
        {
            return Enabled == null || Enabled.Value;
        }

        internal static bool IsEVariant(Missile missile)
        {
            if (missile == null)
                return false;
            Kh85VariantTag tag = missile.GetComponent<Kh85VariantTag>();
            if (tag != null)
                return tag.Letter == "E";
            return Kh85Util.IsKh85(missile) && Kh85Util.GetVariant(missile) == "E";
        }

        internal static void TryAttach(Missile missile)
        {
            if (missile == null || !IsEnabled() || !IsEVariant(missile))
                return;
            if (missile.GetComponent<Kh85EDecoyBrain>() != null)
                return;

            try
            {
                float boost = RcsBoost != null ? RcsBoost.Value : 80f;
                if (boost > 0f)
                    missile.ModifyRCS(boost);
            }
            catch { }

            Register(missile);
            try { missile.gameObject.AddComponent<Kh85EDecoyBrain>(); }
            catch { }
        }

        /// <summary>
        /// High loft while locked, then steep terminal dive. Skips friendly targets.
        /// </summary>
        internal static void ApplyLoftDive(Missile missile)
        {
            if (missile == null || !IsEnabled())
                return;
            if (Kh85Weapon.IsKnownNonKh85Missile(missile))
                return;
            Kh85VariantTag tag = missile.GetComponent<Kh85VariantTag>();
            if (tag == null || tag.Letter != "E")
                return;
            if (Kh85MclosGate.ManualActive)
                return;
            if (missile.disabled)
                return;
            if (Kh85Weapon.ShouldDeferAim(missile))
                return;

            Unit target = null;
            try
            {
                if (MissileTargetField != null)
                    target = MissileTargetField.GetValue(missile) as Unit;
            }
            catch { }
            if (target == null)
                target = Kh85Weapon.ResolveMissileDesignatedTarget(missile);
            if (target == null)
                return;
            // Never guide onto friendlies.
            try
            {
                FactionHQ a = missile.NetworkHQ;
                FactionHQ b = target.NetworkHQ;
                if (a != null && b != null && a == b)
                    return;
            }
            catch { }

            Vector3 mpos = missile.transform.position;
            Vector3 tpos = target.transform.position;
            Vector3 tvel = Vector3.zero;
            try
            {
                if (target.rb != null)
                    tvel = target.rb.velocity;
            }
            catch { }

            float speed = 250f;
            try
            {
                if (missile.rb != null)
                    speed = Mathf.Max(missile.rb.velocity.magnitude, 80f);
            }
            catch { }

            float dist = Vector3.Distance(mpos, tpos);
            float loft = LoftAltitude != null ? LoftAltitude.Value : 9500f;
            float terminal = TerminalRange != null ? TerminalRange.Value : 4500f;
            float dive = DiveStartRange != null ? DiveStartRange.Value : 2200f;
            if (dive > terminal)
                dive = terminal * 0.5f;

            float tGo = dist / Mathf.Max(speed, 50f);
            Vector3 aim = tpos + tvel * (tGo * 0.7f);

            if (dist > terminal)
            {
                // Climb / hold high corridor toward target XY.
                float wantY = Mathf.Max(loft, tpos.y + 2500f);
                if (mpos.y < wantY - 80f)
                    aim.y = Mathf.Max(mpos.y + Mathf.Clamp((wantY - mpos.y) * 0.45f, 200f, 900f), mpos.y + 120f);
                else
                    aim.y = wantY;
                Vector3 horiz = tpos - mpos;
                horiz.y = 0f;
                if (horiz.sqrMagnitude > 1f)
                {
                    horiz.Normalize();
                    Vector3 xy = mpos + horiz * Mathf.Clamp(speed * 2.5f, 600f, 3200f);
                    aim.x = Mathf.Lerp(xy.x, aim.x, 0.35f);
                    aim.z = Mathf.Lerp(xy.z, aim.z, 0.35f);
                }
            }
            else if (dist > dive)
            {
                // Transition: start dropping toward the target.
                float u = 1f - Mathf.Clamp01((dist - dive) / Mathf.Max(terminal - dive, 1f));
                aim.y = Mathf.Lerp(Mathf.Max(loft * 0.85f, mpos.y), tpos.y + 200f, u * u);
            }
            else
            {
                // Terminal dive — aim at target, not under terrain (under-aim caused snap/teleport).
                aim = tpos + tvel * (tGo * 0.5f);
                aim.y = tpos.y + 8f;
            }

            float dropCap = dist > terminal ? 400f : 700f;
            float minAgl = dist > dive ? 40f : 10f;
            Kh85Weapon.SafeSetAimpoint(missile, aim, tvel, minAgl, dropCap);
        }

        internal static void Register(Missile missile)
        {
            if (missile == null)
                return;
            if (!LiveDecoys.Contains(missile))
                LiveDecoys.Add(missile);
        }

        internal static void Unregister(Missile missile)
        {
            if (missile == null)
                return;
            LiveDecoys.Remove(missile);
        }

        internal static void Prune()
        {
            for (int i = LiveDecoys.Count - 1; i >= 0; i--)
            {
                Missile m = LiveDecoys[i];
                try
                {
                    if (m == null || m.disabled)
                        LiveDecoys.RemoveAt(i);
                }
                catch
                {
                    LiveDecoys.RemoveAt(i);
                }
            }
        }

        /// <summary>Nearest live friendly E decoy within salvo radius of a TGM sibling.</summary>
        internal static Missile FindNearbyFriendlyDecoy(Missile sibling)
        {
            if (sibling == null || LiveDecoys.Count == 0)
                return null;
            float radius = SalvoRadius != null ? SalvoRadius.Value : 9000f;
            float best = radius * radius;
            Missile found = null;
            Vector3 pos = sibling.transform.position;
            FactionHQ hq = null;
            try { hq = sibling.NetworkHQ; }
            catch { }

            for (int i = 0; i < LiveDecoys.Count; i++)
            {
                Missile d = LiveDecoys[i];
                try
                {
                    if (d == null || d.disabled)
                        continue;
                    if (d.GetInstanceID() == sibling.GetInstanceID())
                        continue;
                    FactionHQ dh = d.NetworkHQ;
                    if (hq != null && dh != null && hq != dh)
                        continue;
                    float sq = (d.transform.position - pos).sqrMagnitude;
                    if (sq < best)
                    {
                        best = sq;
                        found = d;
                    }
                }
                catch { }
            }
            return found;
        }

        internal static bool HasFriendlySiblingTgm(Missile decoy)
        {
            if (decoy == null)
                return false;
            float radius = SalvoRadius != null ? SalvoRadius.Value : 9000f;
            float r2 = radius * radius;
            Vector3 pos = decoy.transform.position;
            FactionHQ hq = null;
            try { hq = decoy.NetworkHQ; }
            catch { }

            // P1: use Kh85Live list distance — no second large OverlapSphere.
            int decoyId = decoy.GetInstanceID();
            List<Missile> live = Kh85Live.All;
            for (int i = 0; i < live.Count; i++)
            {
                Missile m = live[i];
                try
                {
                    if (m == null || m.disabled)
                        continue;
                    if (m.GetInstanceID() == decoyId)
                        continue;
                    Kh85VariantTag tag = m.GetComponent<Kh85VariantTag>();
                    if (tag == null || tag.Letter == "E")
                        continue;
                    FactionHQ mh = m.NetworkHQ;
                    if (hq != null && mh != null && hq != mh)
                        continue;
                    if ((m.transform.position - pos).sqrMagnitude <= r2)
                        return true;
                }
                catch { }
            }
            return false;
        }

        internal static void AttractPulse(Missile decoy)
        {
            if (decoy == null || !IsEnabled())
                return;

            float radius = AttractRadius != null ? AttractRadius.Value : DefaultLockStealRadiusM;
            if (radius < 500f)
                radius = 500f;
            float r2 = radius * radius;
            Vector3 pos = decoy.transform.position;
            int decoyId = decoy.GetInstanceID();
            PruneSceneMissiles();

            for (int i = 0; i < LiveSceneMissiles.Count; i++)
            {
                Missile hostile = LiveSceneMissiles[i];
                try
                {
                    if (hostile == null || hostile.disabled)
                        continue;
                    if (hostile.GetInstanceID() == decoyId)
                        continue;
                    if ((hostile.transform.position - pos).sqrMagnitude > r2)
                        continue;
                    if (!IsHostile(decoy, hostile))
                        continue;
                    Unit t = ResolveMissileTarget(hostile);
                    if (t == null)
                        continue;
                    if (t.GetInstanceID() == decoyId)
                        continue;
                    if (IsFriendlyLockTarget(decoy, t))
                        RetargetMissile(hostile, decoy);
                }
                catch { }
            }

            int hits = 0;
            try
            {
                hits = Physics.OverlapSphereNonAlloc(pos, radius, OverlapBuf,
                    ~0, QueryTriggerInteraction.Ignore);
            }
            catch { return; }

            HashSet<int> seenShip = null;
            for (int i = 0; i < hits; i++)
            {
                Collider c = OverlapBuf[i];
                if (c == null)
                    continue;

                Unit hostUnit = null;
                try
                {
                    Ship ship = c.GetComponentInParent<Ship>();
                    if (ship != null)
                        hostUnit = ship;
                    else
                    {
                        Aircraft ac = c.GetComponentInParent<Aircraft>();
                        if (ac != null)
                            hostUnit = ac;
                    }
                }
                catch { }
                if (hostUnit == null)
                    continue;
                if (seenShip == null)
                    seenShip = new HashSet<int>();
                if (!seenShip.Add(hostUnit.GetInstanceID()))
                    continue;
                if (!IsHostile(decoy, hostUnit))
                    continue;
                RedirectUnitWeapons(hostUnit, decoy);
            }
        }

        internal static void RegisterSceneMissile(Missile missile)
        {
            if (missile == null)
                return;
            try
            {
                if (!missile.gameObject.activeInHierarchy)
                    return;
            }
            catch { return; }
            if (!LiveSceneMissiles.Contains(missile))
                LiveSceneMissiles.Add(missile);
        }

        internal static void UnregisterSceneMissile(Missile missile)
        {
            if (missile == null)
                return;
            LiveSceneMissiles.Remove(missile);
        }

        /// <summary>Live in-scene missiles (pruned). Used by TGM-85A to break locks without OverlapSphere.</summary>
        internal static List<Missile> PeekLiveSceneMissiles()
        {
            PruneSceneMissiles();
            return LiveSceneMissiles;
        }

        private static void PruneSceneMissiles()
        {
            for (int i = LiveSceneMissiles.Count - 1; i >= 0; i--)
            {
                Missile m = LiveSceneMissiles[i];
                try
                {
                    if (m == null || m.disabled)
                        LiveSceneMissiles.RemoveAt(i);
                }
                catch
                {
                    LiveSceneMissiles.RemoveAt(i);
                }
            }
        }

        private static bool IsFriendlyLockTarget(Missile decoy, Unit candidate)
        {
            if (decoy == null || candidate == null)
                return false;
            try
            {
                if (candidate.GetInstanceID() == decoy.GetInstanceID())
                    return false;
            }
            catch { }
            try
            {
                if (candidate.disabled)
                    return false;
            }
            catch { }
            try
            {
                FactionHQ a = decoy.NetworkHQ;
                FactionHQ b = candidate.NetworkHQ;
                if (a == null || b == null)
                    return false;
                if (a != b)
                    return false;
            }
            catch { return false; }
            return true;
        }

        private static bool IsFriendlyNonETgm(Missile decoy, Unit candidate)
        {
            Missile m = candidate as Missile;
            if (m == null)
            {
                try { m = candidate.GetComponent<Missile>(); }
                catch { }
            }
            if (m == null || !Kh85Util.IsKh85(m) || IsEVariant(m))
                return false;
            try
            {
                FactionHQ a = decoy.NetworkHQ;
                FactionHQ b = m.NetworkHQ;
                if (a != null && b != null && a != b)
                    return false;
            }
            catch { }
            float radius = SalvoRadius != null ? SalvoRadius.Value : 9000f;
            return (m.transform.position - decoy.transform.position).sqrMagnitude
                <= radius * radius;
        }

        private static Unit ResolveMissileTarget(Missile missile)
        {
            if (missile == null)
                return null;
            try
            {
                if (MissileTargetField != null)
                {
                    Unit t = MissileTargetField.GetValue(missile) as Unit;
                    if (t != null)
                        return t;
                }
            }
            catch { }
            try
            {
                PersistentID tid = missile.targetID;
                if (tid.Id != 0u)
                {
                    Unit u;
                    if (tid.TryGetUnit(out u) && u != null)
                        return u;
                }
            }
            catch { }
            try
            {
                if (SeekerField != null && SeekerTargetField != null)
                {
                    MissileSeeker seeker = SeekerField.GetValue(missile) as MissileSeeker;
                    if (seeker != null)
                        return SeekerTargetField.GetValue(seeker) as Unit;
                }
            }
            catch { }
            return null;
        }

        private static void RetargetMissile(Missile hostile, Missile decoy)
        {
            try { hostile.SetTarget(decoy); }
            catch { }
            try
            {
                if (MissileTargetField != null)
                    MissileTargetField.SetValue(hostile, decoy);
            }
            catch { }
            try
            {
                if (SeekerField != null && SeekerTargetField != null)
                {
                    MissileSeeker seeker = SeekerField.GetValue(hostile) as MissileSeeker;
                    if (seeker != null)
                    {
                        SeekerTargetField.SetValue(seeker, decoy);
                        SARHSeeker sarh = seeker as SARHSeeker;
                        if (sarh != null)
                            BindSarhToDecoy(sarh, decoy);
                    }
                }
            }
            catch { }
            try
            {
                Vector3 vel = decoy.rb != null ? decoy.rb.velocity : Vector3.zero;
                hostile.SetAimpoint(decoy.GlobalPosition(), vel);
            }
            catch { }
        }

        /// <summary>
        /// Vanilla SARH Seek() guides via targetTransform, not only targetUnit.
        /// Leaving the original aircraft transform makes the round keep flying at the old lock.
        /// </summary>
        private static void BindSarhToDecoy(SARHSeeker sarh, Missile decoy)
        {
            if (sarh == null || decoy == null)
                return;
            try
            {
                Transform part = null;
                try { part = decoy.GetRandomPart(); }
                catch { }
                if (part == null)
                    part = decoy.transform;
                if (SarhTargetTransform != null)
                    SarhTargetTransform.SetValue(sarh, part);
                if (SarhTimeWithoutTrack != null)
                    SarhTimeWithoutTrack.SetValue(sarh, 0f);
                if (SarhKnownPos != null)
                    SarhKnownPos.SetValue(sarh, decoy.GlobalPosition());
                Vector3 vel = Vector3.zero;
                try
                {
                    if (decoy.rb != null)
                        vel = decoy.rb.velocity;
                }
                catch { }
                if (SarhKnownVel != null)
                    SarhKnownVel.SetValue(sarh, vel);
            }
            catch { }
        }

        private static void RedirectUnitWeapons(Unit host, Missile decoy)
        {
            try
            {
                Weapon[] weapons = host.GetComponentsInChildren<Weapon>(true);
                if (weapons == null)
                    return;
                for (int i = 0; i < weapons.Length; i++)
                {
                    Weapon w = weapons[i];
                    if (w == null)
                        continue;
                    Unit t = null;
                    try { t = w.GetTarget(); }
                    catch { }
                    if (t == null || !IsFriendlyLockTarget(decoy, t))
                        continue;
                    try { w.SetTarget(decoy); }
                    catch { }
                }
            }
            catch { }
        }

        private static bool IsHostile(Unit self, Unit other)
        {
            if (self == null || other == null)
                return false;
            try
            {
                FactionHQ a = self.NetworkHQ;
                FactionHQ b = other.NetworkHQ;
                if (a != null && b != null && a == b)
                    return false;
            }
            catch { }
            return true;
        }

        internal static float AdjustRadarReturn(Missile missile, float value)
        {
            if (missile == null || !IsEnabled() || !Kh85Util.IsKh85(missile))
                return value;

            if (IsEVariant(missile))
            {
                float mul = RadarReturnMul != null ? RadarReturnMul.Value : 12f;
                if (mul < 1f)
                    mul = 1f;
                return value * mul;
            }

            // Sibling strike missiles look smaller while a friendly E is in the package.
            Missile decoy = FindNearbyFriendlyDecoy(missile);
            if (decoy == null)
                return value;
            float sib = SiblingReturnMul != null ? SiblingReturnMul.Value : 0.2f;
            if (sib < 0.01f)
                sib = 0.01f;
            if (sib > 1f)
                sib = 1f;
            return value * sib;
        }
    }

    public class Kh85EDecoyBrain : MonoBehaviour
    {
        private Missile _missile;
        private float _nextAttract;
        private float _nextPrune;

        private void Awake()
        {
            _missile = GetComponent<Missile>();
            Kh85EDecoy.Register(_missile);
        }

        private void OnDestroy()
        {
            Kh85EDecoy.Unregister(_missile);
        }

        private void FixedUpdate()
        {
            if (_missile == null)
                _missile = GetComponent<Missile>();
            if (_missile == null || !Kh85EDecoy.IsEnabled())
                return;
            try
            {
                if (_missile.disabled)
                {
                    Kh85EDecoy.Unregister(_missile);
                    enabled = false;
                    return;
                }
            }
            catch { }

            if (Time.time >= _nextPrune)
            {
                _nextPrune = Time.time + 1.5f;
                Kh85EDecoy.Prune();
            }

            float interval = Kh85EDecoy.AttractInterval != null ? Kh85EDecoy.AttractInterval.Value : 0.2f;
            if (interval < 0.05f)
                interval = 0.05f;
            if (Time.time < _nextAttract)
                return;
            _nextAttract = Time.time + interval;
            Kh85EDecoy.AttractPulse(_missile);
        }
    }

    [HarmonyPatch(typeof(Missile), "OnEnable")]
    internal static class Patch_Kh85E_MissileOnEnable
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance)
        {
            Kh85EDecoy.RegisterSceneMissile(__instance);
        }
    }

    [HarmonyPatch(typeof(Unit), "OnDestroy")]
    internal static class Patch_Kh85E_UnitOnDestroy
    {
        [HarmonyPostfix]
        private static void Postfix(Unit __instance)
        {
            Missile m = __instance as Missile;
            if (m != null)
                Kh85EDecoy.UnregisterSceneMissile(m);
        }
    }

    [HarmonyPatch(typeof(Missile), "GetRadarReturn")]
    internal static class Patch_Kh85E_GetRadarReturn
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance, ref float __result)
        {
            __result = Kh85EDecoy.AdjustRadarReturn(__instance, __result);
        }
    }

    /// <summary>
    /// Prefix loft/dive before Steering.
    /// Priority.VeryLow so CI22XE MCLOS (Priority.Last) wins when ManualActive.
    /// </summary>
    [HarmonyPatch(typeof(Missile), "Steering")]
    internal static class Patch_Kh85E_Steering
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.VeryLow)]
        private static void Prefix(Missile __instance)
        {
            Kh85EDecoy.ApplyLoftDive(__instance);
        }
    }
}
