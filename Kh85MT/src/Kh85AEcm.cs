using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Kh85MT
{
    /// <summary>
    /// TGM-85A Coordinator onboard ECM:
    /// dumps visible chaff/flares and forces nearby hostile missiles locked onto A to drop lock.
    /// </summary>
    internal static class Kh85AEcm
    {
        internal static ConfigEntry<bool> Enabled;
        internal static ConfigEntry<float> JamAmount;
        internal static ConfigEntry<float> EcmIntensity;
        internal static ConfigEntry<float> HoldSeconds;
        internal static ConfigEntry<float> ProtectRadius;
        internal static ConfigEntry<float> LockPollRange;
        internal static ConfigEntry<float> EjectInterval;

        private static readonly FieldInfo MissileTargetField = AccessTools.Field(typeof(Missile), "target");
        private static readonly FieldInfo SeekerField = AccessTools.Field(typeof(Missile), "seeker");
        private static readonly FieldInfo SeekerTargetField = AccessTools.Field(typeof(MissileSeeker), "targetUnit");
        private static readonly FieldInfo ArhJammed = AccessTools.Field(typeof(ARHSeeker), "isJammed");
        private static readonly FieldInfo SarhJammed = AccessTools.Field(typeof(SARHSeeker), "isJammed");
        private static readonly FieldInfo HomeOnJamField = AccessTools.Field(typeof(ARHSeeker), "homeOnJam");
        private static readonly FieldInfo FlareIrField = AccessTools.Field(typeof(IRFlare), "IR");
        private static readonly FieldInfo ChaffPrefabField = AccessTools.Field(typeof(ChaffEjector), "chaffPrefab");
        private static readonly FieldInfo FlarePrefabField = AccessTools.Field(typeof(FlareEjector), "flarePrefab");
        private static readonly FieldInfo ChaffVelField = AccessTools.Field(typeof(RadarChaff), "velocity");
        private static readonly FieldInfo FlareVelField = AccessTools.Field(typeof(IRFlare), "velocity");
        private static readonly MethodInfo ArhOnChaff = AccessTools.Method(typeof(ARHSeeker), "ARHSeeker_OnChaff");
        private static readonly MethodInfo ArhOnJam = AccessTools.Method(typeof(ARHSeeker), "ARHSeeker_OnJam");
        private static readonly MethodInfo SarhOnChaff = AccessTools.Method(typeof(SARHSeeker), "SARHSeeker_OnChaff");
        private static readonly MethodInfo SarhOnJam = AccessTools.Method(typeof(SARHSeeker), "SARHSeeker_OnJam");
        private static readonly MethodInfo IrOnFlare = AccessTools.Method(typeof(IRSeeker), "IRSeeker_OnTargetFlare");
        private static readonly MethodInfo IrLoseLock = AccessTools.Method(typeof(IRSeeker), "LoseLock");
        private static readonly MethodInfo LaunchChaffMethod = AccessTools.Method(typeof(RadarChaff), "LaunchChaff");
        private static readonly MethodInfo LaunchFlareMethod = AccessTools.Method(typeof(IRFlare), "LaunchFlare");

        private static readonly Collider[] OverlapBuf = new Collider[64];
        private static GameObject _chaffPrefab;
        private static GameObject _flarePrefab;
        private static float _nextPrefabScan;
        private static MethodInfo _particlePlay;

        internal static void BindConfig(ConfigFile config)
        {
            Enabled = config.Bind("EcmA", "Enabled", true,
                "TGM-85A: dump chaff/flares and break hostile missiles locked onto this round.");
            JamAmount = config.Bind("EcmA", "JamAmount", 0.45f,
                "Jam amount applied per ECM pulse to locking radars / hostile seekers (0–1 scale).");
            EcmIntensity = config.Bind("EcmA", "EcmIntensity", 2.5f,
                "Extra GetECMIntensity while ECM is active (harder radar track on this missile).");
            HoldSeconds = config.Bind("EcmA", "HoldSeconds", 3.5f,
                "Keep ECM on this many seconds after the last lock detection.");
            ProtectRadius = config.Bind("EcmA", "ProtectRadius", 4500f,
                "Radius (m) to scan for hostile missiles targeting this A missile.");
            LockPollRange = config.Bind("EcmA", "LockPollRange", 8000f,
                "Radius (m) to scan for ship weapons locking this missile.");
            EjectInterval = config.Bind("EcmA", "EjectInterval", 0.35f,
                "Seconds between visible chaff/flare bursts while ECM is active.");
        }

        internal static bool IsEnabled()
        {
            return Enabled == null || Enabled.Value;
        }

        internal static bool IsAVariant(Missile missile)
        {
            return Kh85Util.IsKh85(missile) && Kh85Util.GetVariant(missile) == "A";
        }

        internal static void TryAttach(Missile missile)
        {
            if (missile == null || !IsEnabled() || !IsAVariant(missile))
                return;
            if (missile.GetComponent<Kh85AEcmBrain>() != null)
                return;
            try { missile.gameObject.AddComponent<Kh85AEcmBrain>(); }
            catch { }
        }

        internal static float ActiveEcmBonus(Missile missile)
        {
            if (!IsEnabled() || !IsAVariant(missile))
                return 0f;
            Kh85AEcmBrain brain = missile.GetComponent<Kh85AEcmBrain>();
            if (brain == null || !brain.IsEcmActive())
                return 0f;
            return EcmIntensity != null ? EcmIntensity.Value : 2.5f;
        }

        internal static void Pulse(Missile self, Kh85AEcmBrain brain)
        {
            if (self == null || brain == null || !brain.IsEcmActive())
                return;

            float jamAmt = JamAmount != null ? JamAmount.Value : 0.45f;
            if (jamAmt < 0.05f)
                jamAmt = 0.05f;

            RadarChaff chaff = null;
            IRFlare flare = null;
            float ejectEvery = EjectInterval != null ? EjectInterval.Value : 0.35f;
            if (ejectEvery < 0.12f)
                ejectEvery = 0.12f;
            if (Time.time >= brain.NextEject)
            {
                brain.NextEject = Time.time + ejectEvery;
                SpawnCountermeasureBurst(self, out chaff, out flare);
            }

            JamLockers(self, brain, jamAmt);
            BreakNearbyMissiles(self, jamAmt, chaff, flare);
        }

        internal static bool BlocksRelock(Missile incoming, Unit target)
        {
            if (!IsEnabled() || incoming == null || target == null)
                return false;
            Missile a = target as Missile;
            if (a == null)
            {
                try { a = target.GetComponent<Missile>(); }
                catch { }
            }
            if (a == null || incoming.GetInstanceID() == a.GetInstanceID())
                return false;
            if (!IsAVariant(a))
                return false;
            if (HasSarhSeeker(incoming) || IsShipLaunched(incoming))
                return false;
            Kh85AEcmBrain brain = a.GetComponent<Kh85AEcmBrain>();
            if (brain == null || !brain.IsEcmActive())
                return false;
            return IsHostile(a, incoming);
        }

        internal static bool AnyIncomingLock(Missile self)
        {
            if (self == null)
                return false;
            float radius = ProtectRadius != null ? ProtectRadius.Value : 4500f;
            float r2 = radius * radius;
            Vector3 pos = self.transform.position;
            int selfId = self.GetInstanceID();
            List<Missile> live = Kh85EDecoy.PeekLiveSceneMissiles();
            if (live == null)
                return false;
            for (int i = 0; i < live.Count; i++)
            {
                Missile other = live[i];
                try
                {
                    if (other == null || other.disabled)
                        continue;
                    if (other.GetInstanceID() == selfId)
                        continue;
                    if ((other.transform.position - pos).sqrMagnitude > r2)
                        continue;
                    if (!IsHostile(self, other))
                        continue;
                    if (MissileTargetsUnit(other, self))
                        return true;
                }
                catch { }
            }
            return false;
        }

        private static void JamLockers(Missile self, Kh85AEcmBrain brain, float jamAmt)
        {
            // Emitters remembered from onRadarPing.
            brain.JamRememberedEmitters(self, jamAmt);

            // Poll ship weapons currently targeting us.
            if (Time.time < brain.NextLockPoll)
                return;
            brain.NextLockPoll = Time.time + 0.25f;

            float radius = LockPollRange != null ? LockPollRange.Value : 8000f;
            int hits = 0;
            try
            {
                hits = Physics.OverlapSphereNonAlloc(self.transform.position, radius, OverlapBuf,
                    ~0, QueryTriggerInteraction.Ignore);
            }
            catch { return; }

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
                if (!IsHostile(self, ship))
                    continue;
                if (ShipWeaponsTracking(ship, self))
                {
                    ApplyJam(ship, self, jamAmt);
                    ClearWeaponsTargeting(ship, self);
                    brain.RememberEmitter(ship);
                }
            }
        }

        private static void BreakNearbyMissiles(Missile self, float jamAmt, RadarChaff chaff, IRFlare flare)
        {
            float radius = ProtectRadius != null ? ProtectRadius.Value : 4500f;
            float r2 = radius * radius;
            Vector3 pos = self.transform.position;
            int selfId = self.GetInstanceID();
            List<Missile> live = Kh85EDecoy.PeekLiveSceneMissiles();
            if (live == null)
                return;

            IRSource flareIr = null;
            try
            {
                if (flare != null && FlareIrField != null)
                    flareIr = FlareIrField.GetValue(flare) as IRSource;
            }
            catch { }

            for (int i = 0; i < live.Count; i++)
            {
                Missile other = live[i];
                try
                {
                    if (other == null || other.disabled)
                        continue;
                    if (other.GetInstanceID() == selfId)
                        continue;
                    if ((other.transform.position - pos).sqrMagnitude > r2)
                        continue;
                    if (!IsHostile(self, other))
                        continue;
                    if (!MissileTargetsUnit(other, self))
                        continue;
                    DisruptHostileMissile(other, self, jamAmt, chaff, flareIr);
                }
                catch { }
            }
        }

        private static bool MissileTargetsUnit(Missile missile, Unit victim)
        {
            if (missile == null || victim == null)
                return false;
            int vid = victim.GetInstanceID();
            try
            {
                if (MissileTargetField != null)
                {
                    Unit t = MissileTargetField.GetValue(missile) as Unit;
                    if (t != null && t.GetInstanceID() == vid)
                        return true;
                }
            }
            catch { }
            try
            {
                PersistentID tid = missile.targetID;
                if (tid.Id != 0u)
                {
                    Unit u;
                    if (tid.TryGetUnit(out u) && u != null && u.GetInstanceID() == vid)
                        return true;
                }
            }
            catch { }
            try
            {
                if (SeekerField != null && SeekerTargetField != null)
                {
                    MissileSeeker seeker = SeekerField.GetValue(missile) as MissileSeeker;
                    if (seeker != null)
                    {
                        Unit tu = SeekerTargetField.GetValue(seeker) as Unit;
                        if (tu != null && tu.GetInstanceID() == vid)
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static void DisruptHostileMissile(Missile hostile, Missile self, float jamAmt,
            RadarChaff chaff, IRSource flareIr)
        {
            if (HasSarhSeeker(hostile) || IsShipLaunched(hostile))
            {
                // Ship / SARH keep vanilla seekers — 85A defeats them by jamming the hull.
                return;
            }
            bool homeOnJam = false;
            MissileSeeker seeker = null;
            try
            {
                seeker = SeekerField != null
                    ? SeekerField.GetValue(hostile) as MissileSeeker
                    : null;
                if (seeker is ARHSeeker && HomeOnJamField != null)
                    homeOnJam = (bool)HomeOnJamField.GetValue(seeker);
            }
            catch { }

            ClearMissileLock(hostile);

            ARHSeeker arh = seeker as ARHSeeker;
            if (arh != null)
            {
                try
                {
                    if (chaff != null && ArhOnChaff != null)
                        ArhOnChaff.Invoke(arh, new object[] { chaff });
                }
                catch { }
                try
                {
                    if (ArhOnJam != null)
                    {
                        Unit.JamEventArgs args = default(Unit.JamEventArgs);
                        args.jammingUnit = self;
                        args.jamAmount = jamAmt;
                        ArhOnJam.Invoke(arh, new object[] { args });
                    }
                }
                catch { }
            }

            SARHSeeker sarh = seeker as SARHSeeker;
            if (sarh != null)
            {
                try
                {
                    if (chaff != null && SarhOnChaff != null)
                        SarhOnChaff.Invoke(sarh, new object[] { chaff });
                }
                catch { }
                try
                {
                    if (SarhOnJam != null)
                    {
                        Unit.JamEventArgs args = default(Unit.JamEventArgs);
                        args.jammingUnit = self;
                        args.jamAmount = jamAmt;
                        SarhOnJam.Invoke(sarh, new object[] { args });
                    }
                }
                catch { }
            }

            IRSeeker ir = seeker as IRSeeker;
            if (ir != null)
            {
                try
                {
                    if (flareIr != null && IrOnFlare != null)
                        IrOnFlare.Invoke(ir, new object[] { flareIr });
                }
                catch { }
                try
                {
                    if (IrLoseLock != null)
                        IrLoseLock.Invoke(ir, null);
                }
                catch { }
            }

            if (!homeOnJam)
                ApplyJam(hostile, self, jamAmt);
            else
            {
                ForceSeekerJammed(hostile);
                DivertAway(hostile, self);
            }

            ForceSeekerJammed(hostile);
        }

        private static void ClearMissileLock(Missile missile)
        {
            if (missile == null)
                return;
            try { missile.SetTarget(null); }
            catch { }
            try
            {
                if (SeekerField != null && SeekerTargetField != null)
                {
                    MissileSeeker seeker = SeekerField.GetValue(missile) as MissileSeeker;
                    if (seeker != null)
                        SeekerTargetField.SetValue(seeker, null);
                }
            }
            catch { }
        }

        private static void ForceSeekerJammed(Missile missile)
        {
            try
            {
                if (SeekerField == null)
                    return;
                MissileSeeker seeker = SeekerField.GetValue(missile) as MissileSeeker;
                if (seeker == null)
                    return;
                if (seeker is ARHSeeker && ArhJammed != null)
                    ArhJammed.SetValue(seeker, true);
                if (seeker is SARHSeeker && SarhJammed != null)
                    SarhJammed.SetValue(seeker, true);
                // Generic bool field for other seekers.
                FieldInfo jam = AccessTools.Field(seeker.GetType(), "isJammed");
                if (jam != null && jam.FieldType == typeof(bool))
                    jam.SetValue(seeker, true);
            }
            catch { }
        }

        private static void DivertAway(Missile hostile, Missile self)
        {
            try
            {
                Vector3 away = hostile.transform.position - self.transform.position;
                away.y = 0f;
                if (away.sqrMagnitude < 1f)
                    away = hostile.transform.forward;
                away.Normalize();
                Vector3 aim = hostile.transform.position + away * 2500f + Vector3.up * 200f;
                hostile.SetAimpoint(Kh85Weapon.LocalToGlobal(aim), Vector3.zero);
            }
            catch { }
        }

        internal static void ApplyJam(Unit victim, Unit jammer, float amount)
        {
            if (victim == null || jammer == null)
                return;
            try
            {
                Unit.JamEventArgs args = default(Unit.JamEventArgs);
                args.jammingUnit = jammer;
                args.jamAmount = amount;
                victim.Jam(args);
            }
            catch { }
        }

        internal static bool HasSarhSeeker(Missile missile)
        {
            if (missile == null)
                return false;
            try
            {
                if (SeekerField != null)
                {
                    MissileSeeker s = SeekerField.GetValue(missile) as MissileSeeker;
                    if (s is SARHSeeker)
                        return true;
                }
            }
            catch { }
            try
            {
                if (missile.GetComponent<SARHSeeker>() != null)
                    return true;
                if (missile.GetComponentInChildren<SARHSeeker>(true) != null)
                    return true;
            }
            catch { }
            return false;
        }

        internal static bool IsShipLaunched(Missile missile)
        {
            if (missile == null)
                return false;
            try
            {
                Unit owner = missile.owner;
                if (owner is Ship)
                    return true;
                if (owner != null && owner.GetComponentInParent<Ship>() != null)
                    return true;
            }
            catch { }
            return false;
        }

        internal static bool IsSpoofed(Missile missile)
        {
            if (missile == null)
                return false;
            try { return missile.GetComponent<Kh85ASpoofed>() != null; }
            catch { return false; }
        }

        private static void MarkSpoofed(Missile missile)
        {
            if (missile == null || IsSpoofed(missile))
                return;
            try { missile.gameObject.AddComponent<Kh85ASpoofed>(); }
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
                    if (t != null && t.GetInstanceID() == mid)
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static void ClearWeaponsTargeting(Unit shipOrUnit, Missile self)
        {
            if (shipOrUnit == null || self == null)
                return;
            try
            {
                Weapon[] weapons = shipOrUnit.GetComponentsInChildren<Weapon>(true);
                if (weapons == null)
                    return;
                int mid = self.GetInstanceID();
                for (int i = 0; i < weapons.Length; i++)
                {
                    Weapon w = weapons[i];
                    if (w == null)
                        continue;
                    Unit t = null;
                    try { t = w.GetTarget(); }
                    catch { }
                    if (t == null || t.GetInstanceID() != mid)
                        continue;
                    // Best-effort: many weapons only expose GetTarget; SetTarget may exist on Missile mounts.
                    // Use GetMethod — AccessTools.Method logs HarmonyX warnings when absent.
                    try
                    {
                        MethodInfo set = w.GetType().GetMethod(
                            "SetTarget",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                            null,
                            new Type[] { typeof(Unit) },
                            null);
                        if (set != null)
                            set.Invoke(w, new object[] { null });
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static void SpawnCountermeasureBurst(Missile self, out RadarChaff chaff, out IRFlare flare)
        {
            chaff = null;
            flare = null;
            if (self == null)
                return;
            Aircraft ac = ResolveOwnerAircraft(self);
            RefreshPrefabs(ac);
            Vector3 pos = self.transform.position;
            Vector3 vel = Vector3.zero;
            try
            {
                if (self.rb != null)
                    vel = self.rb.velocity;
            }
            catch { }
            Vector3 back = -self.transform.forward;
            if (vel.sqrMagnitude > 1f)
                back = -vel.normalized;

            chaff = SpawnChaff(self, ac, pos, vel + back * 32f + UnityEngine.Random.insideUnitSphere * 10f);
            RadarChaff chaff2 = SpawnChaff(self, ac, pos,
                vel + back * 28f + UnityEngine.Random.insideUnitSphere * 14f);
            if (chaff == null)
                chaff = chaff2;

            flare = SpawnFlare(self, ac, pos, vel + back * 26f + UnityEngine.Random.insideUnitSphere * 8f);
            IRFlare flare2 = SpawnFlare(self, ac, pos,
                vel + back * 22f + UnityEngine.Random.insideUnitSphere * 12f);
            if (flare == null)
                flare = flare2;
        }

        private static Aircraft ResolveOwnerAircraft(Missile self)
        {
            if (self == null)
                return null;
            try
            {
                Unit owner = self.owner;
                Aircraft ac = owner as Aircraft;
                if (ac != null)
                    return ac;
                if (owner != null)
                    return owner.GetComponentInParent<Aircraft>();
            }
            catch { }
            return null;
        }

        private static void RefreshPrefabs(Aircraft hint)
        {
            if (_chaffPrefab != null && _flarePrefab != null)
                return;
            if (Time.unscaledTime < _nextPrefabScan)
                return;
            _nextPrefabScan = Time.unscaledTime + 2.5f;
            if (hint != null)
                StealPrefabsFromUnit(hint.gameObject);
            if (_chaffPrefab != null && _flarePrefab != null)
                return;
            try
            {
                ChaffEjector[] ce = Resources.FindObjectsOfTypeAll<ChaffEjector>();
                if (ce != null)
                {
                    for (int i = 0; i < ce.Length; i++)
                    {
                        if (ce[i] == null)
                            continue;
                        GameObject prefab = ReadPrefab(ChaffPrefabField, ce[i]);
                        if (prefab == null)
                            continue;
                        _chaffPrefab = prefab;
                        break;
                    }
                }
            }
            catch { }
            try
            {
                FlareEjector[] fe = Resources.FindObjectsOfTypeAll<FlareEjector>();
                if (fe != null)
                {
                    for (int i = 0; i < fe.Length; i++)
                    {
                        if (fe[i] == null)
                            continue;
                        GameObject prefab = ReadPrefab(FlarePrefabField, fe[i]);
                        if (prefab == null)
                            continue;
                        _flarePrefab = prefab;
                        break;
                    }
                }
            }
            catch { }
        }

        private static void StealPrefabsFromUnit(GameObject root)
        {
            if (root == null)
                return;
            if (_chaffPrefab == null)
            {
                try
                {
                    ChaffEjector[] ce = root.GetComponentsInChildren<ChaffEjector>(true);
                    if (ce != null)
                    {
                        for (int i = 0; i < ce.Length; i++)
                        {
                            if (ce[i] != null)
                            {
                                GameObject prefab = ReadPrefab(ChaffPrefabField, ce[i]);
                                if (prefab != null)
                                {
                                    _chaffPrefab = prefab;
                                    break;
                                }
                            }
                        }
                    }
                }
                catch { }
            }
            if (_flarePrefab == null)
            {
                try
                {
                    FlareEjector[] fe = root.GetComponentsInChildren<FlareEjector>(true);
                    if (fe != null)
                    {
                        for (int i = 0; i < fe.Length; i++)
                        {
                            if (fe[i] != null)
                            {
                                GameObject prefab = ReadPrefab(FlarePrefabField, fe[i]);
                                if (prefab != null)
                                {
                                    _flarePrefab = prefab;
                                    break;
                                }
                            }
                        }
                    }
                }
                catch { }
            }
        }

        private static RadarChaff SpawnChaff(Missile self, Aircraft ac, Vector3 pos, Vector3 worldVel)
        {
            if (_chaffPrefab == null)
                return null;
            GameObject go = null;
            try
            {
                go = UnityEngine.Object.Instantiate(_chaffPrefab, pos, Quaternion.identity);
            }
            catch { return null; }
            if (go == null)
                return null;
            try { go.SetActive(true); }
            catch { }
            try { go.transform.SetParent(null, true); }
            catch { }
            try { go.transform.position = pos; }
            catch { }

            RadarChaff rc = go.GetComponent<RadarChaff>();
            if (rc == null)
                rc = go.GetComponentInChildren<RadarChaff>(true);
            if (rc != null)
            {
                try
                {
                    if (LaunchChaffMethod != null)
                        LaunchChaffMethod.Invoke(rc, new object[] { ac, self.transform, worldVel });
                }
                catch { }
                try
                {
                    if (ChaffVelField != null)
                        ChaffVelField.SetValue(rc, worldVel);
                }
                catch { }
            }
            try { go.transform.SetParent(null, true); }
            catch { }
            try { go.transform.position = pos; }
            catch { }
            PlayParticles(go);
            return rc;
        }

        private static IRFlare SpawnFlare(Missile self, Aircraft ac, Vector3 pos, Vector3 worldVel)
        {
            if (_flarePrefab == null)
                return null;
            GameObject go = null;
            try
            {
                go = UnityEngine.Object.Instantiate(_flarePrefab, pos, Quaternion.identity);
            }
            catch { return null; }
            if (go == null)
                return null;
            try { go.SetActive(true); }
            catch { }
            try { go.transform.SetParent(null, true); }
            catch { }
            try { go.transform.position = pos; }
            catch { }

            IRFlare ir = go.GetComponent<IRFlare>();
            if (ir == null)
                ir = go.GetComponentInChildren<IRFlare>(true);
            if (ir != null)
            {
                try
                {
                    if (LaunchFlareMethod != null)
                        LaunchFlareMethod.Invoke(ir, new object[] { ac, self.transform, worldVel });
                }
                catch { }
                try
                {
                    if (FlareVelField != null)
                        FlareVelField.SetValue(ir, worldVel);
                }
                catch { }
            }
            try { go.transform.SetParent(null, true); }
            catch { }
            try { go.transform.position = pos; }
            catch { }
            PlayParticles(go);
            return ir;
        }

        private static void PlayParticles(GameObject go)
        {
            if (go == null)
                return;
            Component[] comps = null;
            try { comps = go.GetComponentsInChildren<Component>(true); }
            catch { return; }
            if (comps == null)
                return;
            if (_particlePlay == null)
            {
                for (int i = 0; i < comps.Length; i++)
                {
                    if (comps[i] == null)
                        continue;
                    Type t = comps[i].GetType();
                    if (t == null || t.Name != "ParticleSystem")
                        continue;
                    _particlePlay = t.GetMethod("Play", Type.EmptyTypes);
                    break;
                }
            }
            if (_particlePlay == null)
                return;
            for (int i = 0; i < comps.Length; i++)
            {
                Component c = comps[i];
                if (c == null || c.GetType().Name != "ParticleSystem")
                    continue;
                try { _particlePlay.Invoke(c, null); }
                catch { }
            }
        }

        private static GameObject ReadPrefab(FieldInfo field, object ejector)
        {
            if (field == null || ejector == null)
                return null;
            try { return field.GetValue(ejector) as GameObject; }
            catch { return null; }
        }
    }

    public class Kh85AEcmBrain : MonoBehaviour
    {
        private Missile _missile;
        private float _ecmUntil;
        private float _nextPulse;
        private Action<Aircraft.OnRadarWarning> _onPing;
        private readonly List<Unit> _emitters = new List<Unit>(8);
        internal float NextLockPoll;
        internal float NextEject;

        private void Awake()
        {
            _missile = GetComponent<Missile>();
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
            if (!e.isTarget)
                return;
            LatchEcm();
            if (e.emitter != null)
                RememberEmitter(e.emitter);
            try
            {
                if (e.radar != null)
                {
                    Unit ru = e.radar.GetComponentInParent<Unit>();
                    if (ru != null)
                        RememberEmitter(ru);
                }
            }
            catch { }
        }

        /// <summary>Cheap latch — only extend hold window (no scans).</summary>
        internal void LatchEcm()
        {
            bool wasOff = !IsEcmActive();
            float hold = Kh85AEcm.HoldSeconds != null ? Kh85AEcm.HoldSeconds.Value : 3.5f;
            if (hold < 0.5f)
                hold = 0.5f;
            float until = Time.time + hold;
            if (until > _ecmUntil)
                _ecmUntil = until;
            if (wasOff)
                NextEject = 0f;
        }

        internal bool IsEcmActive()
        {
            return Time.time < _ecmUntil;
        }

        internal void RememberEmitter(Unit u)
        {
            if (u == null)
                return;
            for (int i = 0; i < _emitters.Count; i++)
            {
                if (_emitters[i] == u)
                    return;
            }
            if (_emitters.Count > 12)
                _emitters.RemoveAt(0);
            _emitters.Add(u);
        }

        internal void JamRememberedEmitters(Missile self, float jamAmt)
        {
            for (int i = _emitters.Count - 1; i >= 0; i--)
            {
                Unit u = _emitters[i];
                try
                {
                    if (u == null)
                    {
                        _emitters.RemoveAt(i);
                        continue;
                    }
                }
                catch
                {
                    _emitters.RemoveAt(i);
                    continue;
                }
                Kh85AEcm.ApplyJam(u, self, jamAmt);
            }
        }

        private void FixedUpdate()
        {
            if (_missile == null)
                _missile = GetComponent<Missile>();
            if (_missile == null || !Kh85AEcm.IsEnabled())
                return;
            try
            {
                if (_missile.disabled)
                    return;
                if (_missile.timeSinceSpawn < 0.4f)
                    return;
            }
            catch { }

            // P1: Pulse primarily when hard-locked / under threat — not always-on.
            // onRadarPing already Latches; ship-weapon poll only when inactive or refreshing.
            if (Time.time >= NextLockPoll)
            {
                NextLockPoll = Time.time + (IsEcmActive() ? 0.45f : 0.3f);
                if (PollShipLock(_missile) || Kh85AEcm.AnyIncomingLock(_missile))
                    LatchEcm();
            }

            if (!IsEcmActive())
                return;
            if (Time.time < _nextPulse)
                return;
            _nextPulse = Time.time + 0.2f;
            Kh85AEcm.Pulse(_missile, this);
        }

        private bool PollShipLock(Missile self)
        {
            float radius = Kh85AEcm.LockPollRange != null ? Kh85AEcm.LockPollRange.Value : 8000f;
            if (radius > 5000f)
                radius = 5000f;
            int hits = 0;
            try
            {
                hits = Physics.OverlapSphereNonAlloc(self.transform.position, radius,
                    Kh85AEcm_Overlap.Buf, ~0, QueryTriggerInteraction.Ignore);
            }
            catch { return false; }

            for (int i = 0; i < hits; i++)
            {
                Collider c = Kh85AEcm_Overlap.Buf[i];
                if (c == null)
                    continue;
                Ship ship = null;
                try { ship = c.GetComponentInParent<Ship>(); }
                catch { }
                if (ship == null)
                    continue;
                try
                {
                    FactionHQ a = self.NetworkHQ;
                    FactionHQ b = ship.NetworkHQ;
                    if (a != null && b != null && a == b)
                        continue;
                }
                catch { }
                Weapon[] weapons = null;
                try { weapons = ship.GetComponentsInChildren<Weapon>(true); }
                catch { }
                if (weapons == null)
                    continue;
                int mid = self.GetInstanceID();
                for (int w = 0; w < weapons.Length; w++)
                {
                    if (weapons[w] == null)
                        continue;
                    Unit t = null;
                    try { t = weapons[w].GetTarget(); }
                    catch { }
                    if (t != null && t.GetInstanceID() == mid)
                    {
                        RememberEmitter(ship);
                        return true;
                    }
                }
            }
            return false;
        }
    }

    /// <summary>Shared overlap buffer for A-ECM poll (avoid another static on the brain).</summary>
    internal static class Kh85AEcm_Overlap
    {
        internal static readonly Collider[] Buf = new Collider[48];
    }

    [HarmonyPatch(typeof(Missile), "GetECMIntensity")]
    internal static class Patch_Kh85A_GetECMIntensity
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance, ref float __result)
        {
            float bonus = Kh85AEcm.ActiveEcmBonus(__instance);
            if (bonus > 0f)
                __result += bonus;
        }
    }

    [HarmonyPatch(typeof(Missile), "SetTarget")]
    internal static class Patch_Kh85A_BlockRelock
    {
        [HarmonyPrefix]
        private static bool Prefix(Missile __instance, Unit target)
        {
            if (target == null)
                return true;
            if (Kh85AEcm.BlocksRelock(__instance, target))
                return false;
            return true;
        }
    }

    /// <summary>Marker: A spoofed this round — skip SARH SlowChecks airburst.</summary>
    public class Kh85ASpoofed : MonoBehaviour
    {
    }

    [HarmonyPatch(typeof(SARHSeeker), "SlowChecks")]
    internal static class Patch_Kh85A_SarhNoAirburst
    {
        [HarmonyPrefix]
        private static bool Prefix(SARHSeeker __instance)
        {
            if (__instance == null)
                return true;
            Missile m = null;
            try { m = __instance.GetComponentInParent<Missile>(); }
            catch { }
            if (Kh85AEcm.IsSpoofed(m))
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(Missile), "MissedTarget")]
    internal static class Patch_Kh85A_SpoofedMissedTarget
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance, ref bool __result)
        {
            if (!__result || !Kh85AEcm.IsSpoofed(__instance))
                return;
            __result = false;
        }
    }

    [HarmonyPatch(typeof(Missile), "LosingGround")]
    internal static class Patch_Kh85A_SpoofedLosingGround
    {
        [HarmonyPostfix]
        private static void Postfix(Missile __instance, ref bool __result)
        {
            if (!__result || !Kh85AEcm.IsSpoofed(__instance))
                return;
            __result = false;
        }
    }
}
