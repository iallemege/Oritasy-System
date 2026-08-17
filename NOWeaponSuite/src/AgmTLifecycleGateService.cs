using System;

namespace WeXon
{
    /// <summary>
    /// Greenfield ACM-119 / ACNM-118 inject + spawn + bus tick gates (0.0.9.91).
    /// AgmTWeapon / AgmTDispenser / AgmTSubBrain own Unity mutations and Harmony.
    /// </summary>
    internal static class AgmTLifecycleGateService
    {
        internal const float PendingTimeoutSec = 15f;
        internal const float HardpointBackoffActiveSec = 30f;
        internal const float HardpointBackoffIdleSec = 120f;
        internal const float MaintIntervalSec = 90f;
        internal const float SubHuntIntervalSec = 0.6f;

        internal enum EnsurePath
        {
            Disabled = 0,
            WaitGs25 = 1,
            WaitMounts = 2,
            FirstInject = 3,
            AlreadyInjected = 4
        }

        internal enum MaintPath
        {
            None = 0,
            RepairAndRegister = 1,
            HardpointScan = 2,
            Both = 3
        }

        internal enum WmInjectPath
        {
            Skip = 0,
            InjectAircraft = 1
        }

        internal enum HardpointAddPath
        {
            MatchedAamKeys = 0,
            PreferredFallback = 1
        }

        internal enum SpawnPath
        {
            Skip = 0,
            SetupBus = 1
        }

        internal enum PendingPath
        {
            None = 0,
            ExpiredClear = 1,
            Reject = 2,
            Consume = 3
        }

        internal enum ServerSimPath
        {
            Skip = 0,
            Run = 1
        }

        internal enum SubHuntPath
        {
            KeepCurrent = 0,
            ClearIntended = 1,
            SearchAssign = 2
        }

        internal static EnsurePath ResolveEnsure(
            bool enableAgmT,
            bool alreadyInjected,
            bool gs25Ready,
            bool mountsAvailable)
        {
            if (!enableAgmT)
                return EnsurePath.Disabled;
            if (alreadyInjected)
                return EnsurePath.AlreadyInjected;
            if (!gs25Ready)
                return EnsurePath.WaitGs25;
            if (!mountsAvailable)
                return EnsurePath.WaitMounts;
            return EnsurePath.FirstInject;
        }

        internal static MaintPath ResolveMaint(
            float now,
            float nextMaintAt,
            float nextHardpointInjectAt)
        {
            bool maint = now >= nextMaintAt;
            bool hp = now >= nextHardpointInjectAt;
            if (maint && hp)
                return MaintPath.Both;
            if (maint)
                return MaintPath.RepairAndRegister;
            if (hp)
                return MaintPath.HardpointScan;
            return MaintPath.None;
        }

        internal static float ScheduleNextMaint(float now)
        {
            return now + MaintIntervalSec;
        }

        /// <summary>
        /// Instantiated mount clones can Unity-fake-null after scene unload.
        /// _injected must drop so FirstInject can recreate them.
        /// </summary>
        internal static bool ShouldResetInjection(bool injected, int liveUsableClones)
        {
            return injected && liveUsableClones <= 0;
        }

        internal static float HardpointBackoffSec(int idlePasses)
        {
            return idlePasses >= 2 ? HardpointBackoffIdleSec : HardpointBackoffActiveSec;
        }

        internal static int NextIdlePasses(int idlePasses, int added)
        {
            return added <= 0 ? idlePasses + 1 : 0;
        }

        internal static bool ShouldLogHardpointWire(int added, int lastLoggedAdded)
        {
            return added > 0 && added != lastLoggedAdded;
        }

        internal static WmInjectPath ResolveWmInject(
            bool injected,
            int mountCloneCount,
            bool wmNull,
            bool hardpointSetsNull,
            bool isShipWm)
        {
            if (!injected || mountCloneCount <= 0 || wmNull || hardpointSetsNull || isShipWm)
                return WmInjectPath.Skip;
            return WmInjectPath.InjectAircraft;
        }

        internal static bool ShouldSkipHardpoint(bool hsNull, bool naval, bool acceptsMissiles)
        {
            return hsNull || naval || !acceptsMissiles;
        }

        internal static bool HardpointAcceptsMissiles(bool anyMissileOption)
        {
            return anyMissileOption;
        }

        internal static HardpointAddPath ResolveHardpointAdd(bool matchedAam)
        {
            return matchedAam ? HardpointAddPath.MatchedAamKeys : HardpointAddPath.PreferredFallback;
        }

        internal static SpawnPath ResolveOnSpawn(
            bool enableAgmT,
            bool missileNull,
            bool hasDispenser,
            bool hasSubBrain,
            bool isGs25Child,
            bool pendingConsumed,
            bool isAgmTMissileOrInfo)
        {
            if (missileNull || !enableAgmT)
                return SpawnPath.Skip;
            if (hasDispenser || hasSubBrain || isGs25Child)
                return SpawnPath.Skip;
            if (pendingConsumed || isAgmTMissileOrInfo)
                return SpawnPath.SetupBus;
            return SpawnPath.Skip;
        }

        internal static PendingPath ResolveConsumePending(
            int pendingSpawns,
            bool gunOrBallistic,
            float now,
            float pendingTime,
            bool looksLikeAam29Bus,
            bool ownerMatches)
        {
            if (pendingSpawns <= 0)
                return PendingPath.None;
            if (gunOrBallistic)
                return PendingPath.Reject;
            if (now - pendingTime > PendingTimeoutSec)
                return PendingPath.ExpiredClear;
            if (!looksLikeAam29Bus || !ownerMatches)
                return PendingPath.Reject;
            return PendingPath.Consume;
        }

        internal static bool LooksLikeAam29BusName(string name, string jsonKey, string unitName)
        {
            if (NameMatchesBus(name))
                return true;
            if (NameMatchesBusKey(jsonKey))
                return true;
            if (NameMatchesBusUnit(unitName))
                return true;
            return false;
        }

        internal static bool IsGs25ChildName(string name, string jsonKey, string unitName)
        {
            string n = name != null ? name : string.Empty;
            if (n.IndexOf("Submunition", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("GS25", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            string k = jsonKey != null ? jsonKey : string.Empty;
            string u = unitName != null ? unitName : string.Empty;
            if (k.IndexOf("submunition", StringComparison.OrdinalIgnoreCase) >= 0
                || u.IndexOf("GS25", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        /// <summary>
        /// Bus FixedUpdate authority: run on listen-server IsServer, local-sim, or IsServer fallback.
        /// </summary>
        internal static ServerSimPath ResolveBusServerSim(
            bool networkManagerNull,
            bool serverNonNull,
            bool serverActive,
            bool missileIsServer,
            bool localSim)
        {
            if (!networkManagerNull && serverNonNull && serverActive)
                return missileIsServer ? ServerSimPath.Run : ServerSimPath.Skip;
            if (localSim)
                return ServerSimPath.Run;
            return missileIsServer ? ServerSimPath.Run : ServerSimPath.Skip;
        }

        internal static ServerSimPath ResolveSubServerSim(
            bool serverActive,
            bool missileIsServer,
            bool localSim)
        {
            if (serverActive && missileIsServer)
                return ServerSimPath.Run;
            if (localSim)
                return ServerSimPath.Run;
            return ServerSimPath.Skip;
        }

        internal static SubHuntPath ResolveSubHunt(
            bool currentLockValid,
            bool intendedNonNull,
            bool intendedStillValid)
        {
            if (currentLockValid)
                return SubHuntPath.KeepCurrent;
            if (intendedNonNull && !intendedStillValid)
                return SubHuntPath.ClearIntended;
            return SubHuntPath.SearchAssign;
        }

        internal static float ScheduleSubHunt(float now)
        {
            return now + SubHuntIntervalSec;
        }

        internal static bool ShouldBlockIalPending(
            bool hasDispenser,
            bool hasSubBrain,
            bool pendingSpawnsAndLooksLikeBus,
            bool isAgmTOrGs25)
        {
            return hasDispenser || hasSubBrain || pendingSpawnsAndLooksLikeBus || isAgmTOrGs25;
        }

        private static bool NameMatchesBus(string n)
        {
            if (string.IsNullOrEmpty(n))
                return false;
            return n.IndexOf("AAM2", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("AAM-29", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Scythe", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("ACM-119", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("ACM_119", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("ACNM-118", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("ACNM_118", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("AGM-119", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("AGM_119", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("AGM-T", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("AGM_T", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool NameMatchesBusKey(string k)
        {
            if (string.IsNullOrEmpty(k))
                return false;
            return k.IndexOf("AAM2", StringComparison.OrdinalIgnoreCase) >= 0
                || k.IndexOf("ACM_119", StringComparison.OrdinalIgnoreCase) >= 0
                || k.IndexOf("ACNM_118", StringComparison.OrdinalIgnoreCase) >= 0
                || k.IndexOf("AGM_119", StringComparison.OrdinalIgnoreCase) >= 0
                || k.IndexOf("AGM_T", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool NameMatchesBusUnit(string u)
        {
            if (string.IsNullOrEmpty(u))
                return false;
            return u.IndexOf("AAM-29", StringComparison.OrdinalIgnoreCase) >= 0
                || u.IndexOf("Scythe", StringComparison.OrdinalIgnoreCase) >= 0
                || u.IndexOf("ACM-119", StringComparison.OrdinalIgnoreCase) >= 0
                || u.IndexOf("ACNM-118", StringComparison.OrdinalIgnoreCase) >= 0
                || u.IndexOf("AGM-119", StringComparison.OrdinalIgnoreCase) >= 0
                || u.IndexOf("AGM-T", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
