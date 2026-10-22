using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Two-seat eject except OA-27C / MiG-15S: first press punches the rear
    /// seat and jettisons the canopy (OA-27C-style lure); second press ejects
    /// the player. OA-27D is handled by OA27C.dll.
    /// </summary>
    internal static class WsoEjectService
    {
        private const float LureRadiusM = 7500f;
        private const float LureSec = 18f;
        private const float LurePulse = 0.2f;

        private static readonly FieldInfo AircraftEjected =
            AccessTools.Field(typeof(Aircraft), "ejected");
        private static readonly FieldInfo MissileTargetField =
            AccessTools.Field(typeof(Missile), "target");
        private static readonly HashSet<int> RearPunched = new HashSet<int>();
        private static readonly List<Missile> Scratch = new List<Missile>(64);

        private static bool _lureOn;
        private static Aircraft _lureAc;
        private static Transform _lureXf;
        private static Unit _lureUnit;
        private static float _lureUntil;
        private static float _lureNext;

        private struct PendingYank
        {
            public Aircraft ac;
            public Transform xf;
            public float at;
        }

        private static readonly List<PendingYank> SeatYanks = new List<PendingYank>(4);

        internal static void Tick()
        {
            YankStuckSeats();
            TickLure();
        }

        internal static bool IsOaOrMigHandled(Aircraft ac)
        {
            if (ac == null)
                return false;
            AircraftDefinition def = ac.definition as AircraftDefinition;
            if (def == null)
                return false;
            string key = def.jsonKey;
            if (!string.IsNullOrEmpty(key))
            {
                if (string.Equals(key, "Aryx_OA27_C", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(key, "Aryx_OA27_D", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(key, "Aryx_MiG15_KAM", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            string n = def.unitName != null ? def.unitName : string.Empty;
            if (n.IndexOf("OA-27C", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("OA-27D", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (n.IndexOf("MIG-15S", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("MiG-15S", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        internal static bool TryRearEject(Aircraft ac)
        {
            if (ac == null || !Plugin.IsRuntimeInstance(ac))
                return false;
            if (IsOaOrMigHandled(ac))
                return false;
            if (!ReverseThrustService.IsPlayerFlown(ac))
                return false;
            int id = ac.GetInstanceID();
            if (RearPunched.Contains(id))
                return false;
            if (!CopilotCrewService.HasCopilot(ac))
                return false;
            if (CountSeats(ac) < 2)
                return false;
            Pilot rear = FindRearPilot(ac);
            if (rear == null)
                return false;
            EjectionSeat seat = FindRearSeat(ac, rear);
            if (seat == null || IsPlayerSeat(ac, seat) || !SeatIsDistinctFromPlayer(ac, seat))
                return false;
            RearPunched.Add(id);
            PunchRear(ac, rear, seat);
            return true;
        }

        internal static bool SkipAlreadyEjectedPilot(Aircraft ac, int pilotNumber)
        {
            if (ac == null || ac.pilots == null)
                return false;
            if (pilotNumber < 0 || pilotNumber >= ac.pilots.Length)
                return false;
            Pilot p = ac.pilots[pilotNumber];
            return p != null && p.ejected;
        }

        private static void PunchRear(Aircraft ac, Pilot rear, EjectionSeat seat)
        {
            JettisonCanopy(ac);
            if (seat != null && IsPlayerSeat(ac, seat))
                seat = null;
            Transform visual = null;
            if (seat != null)
                visual = seat.transform;
            else if (rear != null)
                visual = rear.transform;

            UnitPart part = null;
            if (rear != null)
            {
                try { part = rear.GetUnitPart(); }
                catch { part = null; }
            }
            if (seat != null)
            {
                try { seat.Fire(part); }
                catch { }
                try { seat.Detach(); }
                catch { }
            }
            Vector3 vel = EjectVelocity(ac);
            if (rear != null && !rear.playerControlled)
            {
                try { rear.SetEjected(); }
                catch { rear.ejected = true; }
                try { rear.Detach(vel, rear.transform.localPosition); }
                catch { }
                try { rear.TogglePilotVisibility(false); }
                catch { }
            }
            ScheduleYank(ac, visual, 0.25f);
            ScheduleYank(ac, visual, 1.4f);
            if (visual != null && StillOnAircraft(ac, visual))
                LaunchLoose(ac, visual, vel);
            KeepPlayerFlying(ac);
            ArmLure(ac, visual, rear);
        }

        private static int CountSeats(Aircraft ac)
        {
            EjectionSeat[] seats = null;
            try { seats = ac.GetComponentsInChildren<EjectionSeat>(true); }
            catch { seats = null; }
            if (seats == null)
                return 0;
            int n = 0;
            for (int i = 0; i < seats.Length; i++)
            {
                if (seats[i] != null)
                    n++;
            }
            return n;
        }

        private static Vector3 EjectVelocity(Aircraft ac)
        {
            Vector3 vel = Vector3.up * 24f;
            try
            {
                if (ac != null && ac.rb != null)
                    vel = ac.rb.velocity + ac.transform.up * 24f + ac.transform.forward * 5f;
            }
            catch { }
            return vel;
        }

        private static bool IsPlayerSeat(Aircraft ac, EjectionSeat seat)
        {
            if (ac == null || seat == null)
                return false;
            Pilot player = FindPlayerPilot(ac);
            if (player == null)
                return false;
            EjectionSeat front = null;
            try { front = player.GetComponentInChildren<EjectionSeat>(true); }
            catch { front = null; }
            if (front != null && object.ReferenceEquals(front, seat))
                return true;
            if (player.transform != null && seat.transform != null
                && (seat.transform.position - player.transform.position).sqrMagnitude < 0.35f * 0.35f)
                return true;
            return false;
        }

        private static bool SeatIsDistinctFromPlayer(Aircraft ac, EjectionSeat seat)
        {
            if (ac == null || seat == null)
                return false;
            Pilot player = FindPlayerPilot(ac);
            if (player == null || player.transform == null || seat.transform == null)
                return false;
            float d2 = (seat.transform.position - player.transform.position).sqrMagnitude;
            if (d2 >= 0.5f * 0.5f)
                return true;
            float dz = 0f;
            try
            {
                Vector3 seatLocal = ac.transform.InverseTransformPoint(seat.transform.position);
                Vector3 playerLocal = ac.transform.InverseTransformPoint(player.transform.position);
                dz = playerLocal.z - seatLocal.z;
            }
            catch { return false; }
            return dz >= 0.2f;
        }

        private static void ScheduleYank(Aircraft ac, Transform xf, float delay)
        {
            if (ac == null || xf == null)
                return;
            PendingYank y;
            y.ac = ac;
            y.xf = xf;
            y.at = Time.time + delay;
            SeatYanks.Add(y);
        }

        private static void YankStuckSeats()
        {
            if (SeatYanks.Count == 0)
                return;
            for (int i = SeatYanks.Count - 1; i >= 0; i--)
            {
                PendingYank y = SeatYanks[i];
                if (Time.time < y.at)
                    continue;
                SeatYanks.RemoveAt(i);
                if (y.ac == null || y.xf == null)
                    continue;
                if (!StillOnAircraft(y.ac, y.xf))
                    continue;
                LaunchLoose(y.ac, y.xf, EjectVelocity(y.ac));
            }
        }

        private static bool StillOnAircraft(Aircraft ac, Transform xf)
        {
            if (ac == null || xf == null)
                return false;
            Transform c = xf;
            int guard = 0;
            while (c != null && guard < 24)
            {
                guard++;
                if (object.ReferenceEquals(c, ac.transform))
                    return true;
                try { c = c.parent; }
                catch { break; }
            }
            return false;
        }

        private static void LaunchLoose(Aircraft ac, Transform xf, Vector3 vel)
        {
            if (ac == null || xf == null)
                return;
            Pilot player = FindPlayerPilot(ac);
            if (player != null && (object.ReferenceEquals(xf, player.transform)
                || StillUnder(xf, player.transform)))
                return;
            if (object.ReferenceEquals(xf, ac.transform))
                return;
            try { xf.SetParent(null, true); }
            catch { return; }
            Rigidbody rb = null;
            try { rb = xf.GetComponent<Rigidbody>(); }
            catch { rb = null; }
            if (rb == null)
            {
                try { rb = xf.gameObject.AddComponent<Rigidbody>(); }
                catch { rb = null; }
            }
            if (rb == null)
                return;
            try
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                if (rb.mass < 40f)
                    rb.mass = 80f;
                rb.velocity = vel;
                if (ac.transform != null)
                    rb.angularVelocity = ac.transform.right * 0.35f;
            }
            catch { }
        }

        private static bool StillUnder(Transform xf, Transform root)
        {
            if (xf == null || root == null)
                return false;
            Transform c = xf;
            int guard = 0;
            while (c != null && guard < 24)
            {
                guard++;
                if (object.ReferenceEquals(c, root))
                    return true;
                try { c = c.parent; }
                catch { break; }
            }
            return false;
        }

        private static void JettisonCanopy(Aircraft ac)
        {
            if (ac == null)
                return;
            Canopy[] cans = null;
            try { cans = ac.GetComponentsInChildren<Canopy>(true); }
            catch { cans = null; }
            if (cans == null)
                return;
            for (int i = 0; i < cans.Length; i++)
            {
                if (cans[i] == null)
                    continue;
                try { cans[i].Eject(); }
                catch { }
            }
        }

        private static Pilot[] AllPilots(Aircraft ac)
        {
            if (ac == null)
                return null;
            Pilot[] pilots = null;
            try { pilots = ac.pilots; }
            catch { pilots = null; }
            if (pilots != null && pilots.Length > 0)
                return pilots;
            try { return ac.GetComponentsInChildren<Pilot>(true); }
            catch { return null; }
        }

        private static Pilot FindPlayerPilot(Aircraft ac)
        {
            Pilot[] pilots = AllPilots(ac);
            if (pilots == null)
                return null;
            for (int i = 0; i < pilots.Length; i++)
            {
                if (pilots[i] != null && pilots[i].playerControlled)
                    return pilots[i];
            }
            if (pilots.Length > 0)
                return pilots[0];
            return null;
        }

        private static Pilot FindRearPilot(Aircraft ac)
        {
            Pilot[] pilots = AllPilots(ac);
            if (pilots == null)
                return null;
            Pilot player = FindPlayerPilot(ac);
            for (int i = 0; i < pilots.Length; i++)
            {
                Pilot p = pilots[i];
                if (p == null || object.ReferenceEquals(p, player) || p.playerControlled)
                    continue;
                return p;
            }
            return null;
        }

        private static EjectionSeat FindRearSeat(Aircraft ac, Pilot rear)
        {
            EjectionSeat[] seats = null;
            try { seats = ac.GetComponentsInChildren<EjectionSeat>(true); }
            catch { seats = null; }
            if (rear != null)
            {
                EjectionSeat on = null;
                try { on = rear.GetComponentInChildren<EjectionSeat>(true); }
                catch { on = null; }
                if (on != null && !IsPlayerSeat(ac, on))
                    return on;
            }
            if (seats == null || seats.Length == 0)
                return null;
            EjectionSeat aft = null;
            float bestZ = float.MaxValue;
            for (int i = 0; i < seats.Length; i++)
            {
                EjectionSeat s = seats[i];
                if (s == null || IsPlayerSeat(ac, s))
                    continue;
                float z = 0f;
                try { z = ac.transform.InverseTransformPoint(s.transform.position).z; }
                catch { continue; }
                if (z < bestZ)
                {
                    bestZ = z;
                    aft = s;
                }
            }
            return aft;
        }

        private static void KeepPlayerFlying(Aircraft ac)
        {
            if (ac == null)
                return;
            if (AircraftEjected != null)
            {
                try { AircraftEjected.SetValue(ac, false); }
                catch { }
            }
            try { GameManager.flightControlsEnabled = true; }
            catch { }
            Pilot[] pilots = AllPilots(ac);
            if (pilots != null)
            {
                for (int i = 0; i < pilots.Length; i++)
                {
                    Pilot p = pilots[i];
                    if (p == null || !p.playerControlled)
                        continue;
                    try { p.ejected = false; }
                    catch { }
                    try { p.dead = false; }
                    catch { }
                }
            }
            try
            {
                Player player;
                if (GameManager.GetLocalPlayer(out player) && player != null)
                {
                    if (player.Aircraft == null || !object.ReferenceEquals(player.Aircraft, ac))
                        player.SetAircraft(ac);
                }
            }
            catch { }
            try
            {
                FlightHud hud = SceneSingleton<FlightHud>.i;
                if (hud != null)
                    hud.SetAircraft(ac);
            }
            catch { }
            try
            {
                CombatHUD combat = SceneSingleton<CombatHUD>.i;
                if (combat != null)
                    combat.SetAircraft(ac);
            }
            catch { }
            try { CameraStateManager.cameraMode = CameraMode.cockpit; }
            catch { }
        }

        private static void ArmLure(Aircraft ac, Transform xf, Pilot rear)
        {
            _lureOn = true;
            _lureAc = ac;
            _lureXf = xf;
            _lureUnit = null;
            if (rear != null)
            {
                try { _lureUnit = rear.GetComponent<Unit>(); }
                catch { _lureUnit = null; }
            }
            if (_lureXf == null && ac != null)
                _lureXf = ac.transform;
            _lureUntil = Time.time + LureSec;
            _lureNext = 0f;
        }

        private static void TickLure()
        {
            if (!_lureOn)
                return;
            if (_lureAc == null || Time.time > _lureUntil)
            {
                _lureOn = false;
                _lureAc = null;
                return;
            }
            try
            {
                if (_lureAc.disabled)
                {
                    _lureOn = false;
                    _lureAc = null;
                    return;
                }
            }
            catch { }
            if (Time.time < _lureNext)
                return;
            _lureNext = Time.time + LurePulse;
            if (_lureXf == null && _lureUnit != null)
                _lureXf = _lureUnit.transform;
            Vector3 origin = _lureAc.transform.position;
            if (_lureXf != null)
                origin = _lureXf.position;
            float r2 = LureRadiusM * LureRadiusM;
            CollectMissiles();
            for (int i = 0; i < Scratch.Count; i++)
            {
                Missile m = Scratch[i];
                if (m == null)
                    continue;
                try
                {
                    if (m.disabled)
                        continue;
                    if ((m.transform.position - origin).sqrMagnitude > r2)
                        continue;
                    if (object.ReferenceEquals(m.owner, _lureAc))
                        continue;
                    Aircraft parent = m.GetComponentInParent<Aircraft>();
                    if (parent != null)
                        continue;
                    if (_lureUnit != null)
                    {
                        m.SetTarget(_lureUnit);
                        if (MissileTargetField != null)
                            MissileTargetField.SetValue(m, _lureUnit);
                    }
                    if (_lureXf != null)
                    {
                        Rigidbody rb = null;
                        try { rb = _lureXf.GetComponent<Rigidbody>(); }
                        catch { }
                        m.SetProxyFuse(_lureXf, rb);
                    }
                }
                catch { }
            }
        }

        private static void CollectMissiles()
        {
            Scratch.Clear();
            List<Unit> units = null;
            try { units = UnitRegistry.allUnits; }
            catch { units = null; }
            if (units == null)
                return;
            for (int i = 0; i < units.Count; i++)
            {
                Missile m = units[i] as Missile;
                if (m != null)
                    Scratch.Add(m);
            }
        }
    }

    [HarmonyPatch(typeof(Aircraft), "SpawnEjectingPilot")]
    internal static class Patch_WsoTwoSeat_SkipEjectedSpawn
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.High)]
        private static bool Prefix(Aircraft __instance, int pilotNumber)
        {
            if (WsoEjectService.SkipAlreadyEjectedPilot(__instance, pilotNumber))
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(Aircraft), "StartEjectionSequence")]
    internal static class Patch_WsoTwoSeat_StartEjection
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.High)]
        private static bool Prefix(Aircraft __instance)
        {
            if (WsoEjectService.TryRearEject(__instance))
                return false;
            if (WsoEjectService.IsOaOrMigHandled(__instance))
                return true;
            WsoCrewOps.NoteEjection(__instance);
            return true;
        }
    }

    [HarmonyPatch(typeof(Aircraft), "CmdStartEjectionSequence")]
    internal static class Patch_WsoTwoSeat_CmdEjection
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.High)]
        private static bool Prefix(Aircraft __instance)
        {
            if (WsoEjectService.TryRearEject(__instance))
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(Pilot), "CommandEjection")]
    internal static class Patch_WsoTwoSeat_PilotEject
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.High)]
        private static bool Prefix(Pilot __instance)
        {
            if (__instance == null || !__instance.playerControlled)
                return true;
            Aircraft ac = null;
            try { ac = __instance.aircraft; }
            catch { ac = null; }
            if (ac == null)
            {
                try { ac = __instance.GetComponentInParent<Aircraft>(); }
                catch { ac = null; }
            }
            if (WsoEjectService.TryRearEject(ac))
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(RadialMenuAction), "TriggerAction")]
    internal static class Patch_WsoTwoSeat_RadialEject
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.High)]
        private static bool Prefix(RadialMenuAction __instance, Aircraft aircraft)
        {
            if (__instance == null)
                return true;
            RadialMenuAction.ActionType t = RadialMenuAction.ActionType.Gear;
            try { t = __instance.GetActionType(); }
            catch { return true; }
            if (t != RadialMenuAction.ActionType.Eject)
                return true;
            if (WsoEjectService.TryRearEject(aircraft))
                return false;
            return true;
        }
    }
}
