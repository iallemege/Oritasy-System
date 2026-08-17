using System.Collections.Generic;
using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Tracks local-player hit / module XP for kill-feed suffix and career grants.
    /// </summary>
    internal static class CombatKillXpTracker
    {
        private const float GunHitGuardSec = 0.15f;
        private const float BlastHitDebounceSec = 0.2f;

        private static readonly Dictionary<uint, int> HitXpByUnit = new Dictionary<uint, int>();
        private static readonly Dictionary<uint, int> TotalXpByUnit = new Dictionary<uint, int>();
        private static readonly Dictionary<uint, float> LastHitAt = new Dictionary<uint, float>();
        private static readonly HashSet<uint> LocalDamaged = new HashSet<uint>();
        private static readonly HashSet<int> AwardedParts = new HashSet<int>();
        private static readonly HashSet<uint> DestroyAwarded = new HashSet<uint>();

        private static uint _gunHitKey;
        private static float _gunHitAt = -10f;

        private static bool _feedActive;
        private static bool _feedShowXp;
        private static int _feedXp;

        internal static void ResetMatch()
        {
            HitXpByUnit.Clear();
            TotalXpByUnit.Clear();
            LastHitAt.Clear();
            LocalDamaged.Clear();
            AwardedParts.Clear();
            DestroyAwarded.Clear();
            _gunHitKey = 0;
            _gunHitAt = -10f;
            EndFeed();
        }

        internal static void BeginFeed(PersistentID killerID, PersistentID killedID, KillType killedType)
        {
            _feedActive = true;
            _feedShowXp = false;
            _feedXp = 0;
            if (killedType != KillType.Aircraft && killedType != KillType.Vehicle
                && killedType != KillType.Ship && killedType != KillType.Building
                && killedType != KillType.Missile)
                return;

            Unit killer = null;
            try { killerID.TryGetUnit(out killer); }
            catch { }
            if (!IsLocalKiller(killer))
                return;

            _feedShowXp = true;
            Unit killed = null;
            try { killedID.TryGetUnit(out killed); }
            catch { }
            if (killed != null)
                NoteUnitDestroyed(killed);
            else if (killedType == KillType.Missile)
            {
                try { PlayerCareer.TryGrantCombatXp(CombatKillXpMathService.XpPerMissile); }
                catch { }
                _feedXp = CombatKillXpMathService.XpPerMissile;
                return;
            }

            int xp = 0;
            if (killedID.IsValid && TotalXpByUnit.TryGetValue(killedID.Id, out xp))
                _feedXp = xp;
        }

        internal static void EndFeed()
        {
            _feedActive = false;
            _feedShowXp = false;
            _feedXp = 0;
        }

        internal static void TryAppendFeedXp(ref string message)
        {
            if (!_feedActive || !_feedShowXp || _feedXp <= 0)
                return;
            if (string.IsNullOrEmpty(message))
                return;
            message = message + CombatKillXpMathService.FormatFeedSuffix(_feedXp);
        }

        internal static void NoteUnitDestroyed(Unit victim)
        {
            if (victim == null)
                return;
            uint key;
            if (!TryUnitKey(victim, out key))
                return;
            if (DestroyAwarded.Contains(key))
                return;
            int xp = CombatKillXpMathService.ResolveUnitDestroyXp(victim);
            if (xp <= 0)
                return;
            DestroyAwarded.Add(key);
            Award(xp, key);
        }

        internal static string FormatPeek(Unit victim)
        {
            uint key;
            if (!TryUnitKey(victim, out key))
                return "";
            int xp = 0;
            if (!TotalXpByUnit.TryGetValue(key, out xp) || xp <= 0)
                return "";
            return CombatKillXpMathService.FormatFeedSuffix(xp);
        }

        internal static void OnGunHit(Unit shooter, Unit hitUnit)
        {
            if (!IsLocalShooter(shooter) || hitUnit == null)
                return;
            uint key;
            if (!TryUnitKey(hitUnit, out key))
                return;
            _gunHitKey = key;
            _gunHitAt = Time.unscaledTime;
            NoteWeaponHit(hitUnit, false);
        }

        internal static void OnDealtDamage(PersistentID dealerID, Unit victim)
        {
            if (victim == null || !IsLocalDealer(dealerID))
                return;
            uint key;
            if (!TryUnitKey(victim, out key))
                return;
            if (key == _gunHitKey && Time.unscaledTime - _gunHitAt < GunHitGuardSec)
            {
                MarkDamaged(key);
                return;
            }
            NoteWeaponHit(victim, true);
        }

        internal static void OnLocalMissileTouched(Missile missile, Unit victim)
        {
            if (missile == null || victim == null || !IsLocalMissile(missile))
                return;
            NoteWeaponHit(victim, true);
        }

        internal static void OnPartDestroyed(UnitPart part)
        {
            if (part == null)
                return;
            int partId = 0;
            try { partId = part.GetInstanceID(); }
            catch { return; }
            if (partId == 0 || AwardedParts.Contains(partId))
                return;

            Unit parent = null;
            try { parent = part.parentUnit; }
            catch { }
            if (parent == null || parent is Missile)
                return;
            if (IsSelfOrFriendly(parent))
                return;

            uint key;
            if (!TryUnitKey(parent, out key))
                return;
            if (!LocalDamaged.Contains(key))
                return;

            AwardedParts.Add(partId);
            Award(CombatKillXpMathService.XpPerModule, key);
        }

        private static void NoteWeaponHit(Unit victim, bool debounce)
        {
            if (victim == null || victim is Missile)
                return;
            if (IsSelfOrFriendly(victim))
                return;

            uint key;
            if (!TryUnitKey(victim, out key))
                return;
            MarkDamaged(key);

            Aircraft ac = victim as Aircraft;
            if (ac == null)
                return;

            if (debounce)
            {
                float last = -10f;
                if (LastHitAt.TryGetValue(key, out last)
                    && Time.unscaledTime - last < BlastHitDebounceSec)
                    return;
            }
            LastHitAt[key] = Time.unscaledTime;

            int already = 0;
            HitXpByUnit.TryGetValue(key, out already);
            int grant = CombatKillXpMathService.GrantForAircraftHit(already);
            if (grant <= 0)
                return;
            HitXpByUnit[key] = already + grant;
            Award(grant, key);
        }

        private static void Award(int amount, uint unitKey)
        {
            if (amount <= 0 || unitKey == 0)
                return;
            int cur = 0;
            TotalXpByUnit.TryGetValue(unitKey, out cur);
            TotalXpByUnit[unitKey] = cur + amount;
            try { PlayerCareer.TryGrantCombatXp(amount); }
            catch { }
        }

        private static void MarkDamaged(uint key)
        {
            if (key != 0)
                LocalDamaged.Add(key);
        }

        private static bool IsLocalShooter(Unit shooter)
        {
            if (shooter == null)
                return false;
            try
            {
                if (GameManager.IsLocalAircraft(shooter))
                    return true;
            }
            catch { }
            return IsLocalMissile(shooter as Missile);
        }

        private static bool IsLocalDealer(PersistentID dealerID)
        {
            if (!dealerID.IsValid)
                return false;
            Unit dealer = null;
            try
            {
                if (!dealerID.TryGetUnit(out dealer) || dealer == null)
                    return false;
            }
            catch { return false; }
            return IsLocalShooter(dealer);
        }

        internal static bool IsLocalKiller(Unit killer)
        {
            return IsLocalShooter(killer);
        }

        private static bool IsLocalMissile(Missile missile)
        {
            if (missile == null)
                return false;
            Aircraft local = null;
            try
            {
                if (!GameManager.GetLocalAircraft(out local) || local == null)
                    return false;
            }
            catch { return false; }
            try
            {
                if (object.ReferenceEquals(missile.owner, local))
                    return true;
            }
            catch { }
            try
            {
                Aircraft ownerAc = missile.owner as Aircraft;
                if (ownerAc != null && ownerAc.Player != null
                    && Plugin.IsLocalHumanPlayer(ownerAc.Player))
                    return true;
            }
            catch { }
            return false;
        }

        private static bool IsSelfOrFriendly(Unit victim)
        {
            Aircraft local = null;
            try
            {
                if (!GameManager.GetLocalAircraft(out local) || local == null)
                    return true;
            }
            catch { return true; }
            if (object.ReferenceEquals(victim, local))
                return true;
            try
            {
                if (Plugin.IsSameFaction(local, victim))
                    return true;
            }
            catch { }
            return false;
        }

        private static bool TryUnitKey(Unit unit, out uint key)
        {
            key = 0;
            if (unit == null)
                return false;
            try
            {
                PersistentID id = unit.persistentID;
                if (id.IsValid)
                {
                    key = id.Id;
                    return true;
                }
            }
            catch { }
            try
            {
                int iid = unit.GetInstanceID();
                if (iid == 0)
                    return false;
                key = unchecked((uint)iid);
                return true;
            }
            catch { return false; }
        }
    }
}
