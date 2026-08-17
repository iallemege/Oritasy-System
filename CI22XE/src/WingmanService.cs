using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Oritasy
{
    /// <summary>
    /// Lock a friendly aircraft and press P to assign up to 7 wingmen.
    /// First-person marker turns gold with [W]. They form up and attack
    /// the player's current hostile lock first.
    /// </summary>
    internal static class WingmanService
    {
        internal const int MaxWingmen = 7;

        private static readonly Color Gold = new Color(1f, 0.78f, 0.12f, 1f);
        private static readonly float[] SlotLat = { 80f, -80f, 160f, -160f, 50f, -50f, 0f };
        private static readonly float[] SlotBack = { 70f, 70f, 140f, 140f, 210f, 210f, 260f };
        private static readonly float[] SlotDown = { 8f, 8f, 14f, 14f, 18f, 18f, 22f };

        private static readonly FieldInfo MarkerColor =
            AccessTools.Field(typeof(HUDUnitMarker), "color");
        private static readonly FieldInfo HudTargetInfo =
            AccessTools.Field(typeof(CombatHUD), "targetInfo");
        private static readonly FieldInfo HudMarkerLookup =
            AccessTools.Field(typeof(CombatHUD), "markerLookup");
        private static readonly FieldInfo CombatTarget =
            AccessTools.Field(typeof(AIPilotCombatModes), "currentTarget");
        private static readonly FieldInfo HeloTarget =
            AccessTools.Field(typeof(AIHeloCombatState), "currentTarget");

        private static readonly List<Aircraft> Wingmen = new List<Aircraft>();
        private static readonly HashSet<int> WingIds = new HashSet<int>();

        private static ConfigEntry<KeyCode> _key;

        internal static void Bind(ConfigFile config)
        {
            _key = config.Bind("Flight", "WingmanKey", KeyCode.P,
                "Lock a friendly aircraft, then press to assign/dismiss a wingman (max 7).");
        }

        internal static bool IsWingman(Unit unit)
        {
            if (unit == null)
                return false;
            return WingIds.Contains(unit.GetInstanceID());
        }

        internal static void Tick()
        {
            Prune();
            if (_key == null)
                return;
            if (!Input.GetKeyDown(_key.Value))
                return;
            if (JoinMenuFactionFix.JoinMenuOpen())
                return;
            if (OritasyPresentation.BlocksHud)
                return;
            TryToggleFromLock();
        }

        internal static void DriveAfterAi(Pilot pilot)
        {
            if (pilot == null || pilot.playerControlled || pilot.dead)
                return;
            Aircraft ac = pilot.aircraft;
            if (ac == null || !IsWingman(ac))
                return;
            if (ac.disabled)
            {
                Remove(ac, false);
                return;
            }
            Aircraft lead = LocalAircraft();
            if (lead == null)
            {
                ClearAll();
                return;
            }
            Unit attack = PlayerAttackTarget(lead);
            if (attack != null)
                DriveAttack(ac, pilot, attack);
            else
                DriveFormation(ac, lead);
        }

        private static void TryToggleFromLock()
        {
            Aircraft local = LocalAircraft();
            if (local == null)
            {
                Report(null, UiLang.T("Need aircraft", "需要飞机"));
                return;
            }
            Unit locked = PrimaryLock(local);
            if (locked == null)
            {
                Report(local, UiLang.T("Lock a friendly aircraft first", "请先锁定一架友军飞机"));
                return;
            }
            Aircraft mate = locked as Aircraft;
            if (mate == null || !IsFriendlyAi(local, mate))
            {
                Report(local, UiLang.T("Must lock a friendly aircraft", "必须锁定友军飞机"));
                return;
            }
            if (IsWingman(mate))
            {
                Remove(mate, true);
                Report(local, UiLang.T("Wingman dismissed", "已解除僚机"));
                return;
            }
            if (Wingmen.Count >= MaxWingmen)
            {
                Report(local, UiLang.T("Wingman slots full (max 7)", "僚机已满（最多7架）"));
                return;
            }
            Wingmen.Add(mate);
            WingIds.Add(mate.GetInstanceID());
            Report(local, UiLang.T("Wingman assigned [W]", "已编队 [W]"));
        }

        private static void DriveAttack(Aircraft ac, Pilot pilot, Unit target)
        {
            if (ac == null || target == null)
                return;
            EnsureCombat(pilot, ac);
            SetCombatTarget(pilot, target);
            try
            {
                if (ac.weaponManager != null)
                    ac.weaponManager.AddTargetList(target);
            }
            catch { }
            try { pilot.SetPrimaryTarget(target); }
            catch { }
            Vector3 aim = target.transform.position;
            Vector3 vel = Vector3.zero;
            try
            {
                if (target.rb != null)
                    vel = target.rb.velocity;
            }
            catch { }
            aim += vel * 1.2f;
            float hold = 200f;
            try { hold = Mathf.Max(120f, ac.radarAlt); }
            catch { }
            Aim(ac, aim, hold, AutopilotAim.AttackBank, vel);
        }

        private static void DriveFormation(Aircraft ac, Aircraft lead)
        {
            if (ac == null || lead == null || ac.autopilot == null)
                return;
            int slot = IndexOf(ac);
            if (slot < 0)
                slot = 0;
            if (slot >= SlotLat.Length)
                slot = SlotLat.Length - 1;
            Vector3 pos = lead.transform.position;
            Vector3 fwd = lead.transform.forward;
            Vector3 right = lead.transform.right;
            Vector3 up = lead.transform.up;
            try
            {
                if (lead.rb != null && lead.rb.velocity.sqrMagnitude > 25f)
                {
                    Vector3 track = lead.rb.velocity;
                    track.y = 0f;
                    if (track.sqrMagnitude > 0.01f)
                    {
                        fwd = track.normalized;
                        right = Vector3.Cross(Vector3.up, fwd).normalized;
                        up = Vector3.up;
                    }
                }
            }
            catch { }
            Vector3 slotPos = pos
                - fwd * SlotBack[slot]
                + right * SlotLat[slot]
                - up * SlotDown[slot];
            try
            {
                if (lead.rb != null)
                    slotPos += lead.rb.velocity * 1.4f;
            }
            catch { }
            float hold = 80f;
            try { hold = Mathf.Max(40f, lead.radarAlt - SlotDown[slot]); }
            catch { }
            Vector3 leadVel = Vector3.zero;
            try
            {
                if (lead.rb != null)
                    leadVel = lead.rb.velocity;
            }
            catch { }
            Aim(ac, slotPos, hold, AutopilotAim.CruiseBank, leadVel);
            MatchSpeed(ac, lead, slotPos);
        }

        private static void Aim(Aircraft ac, Vector3 worldAim, float altHold, float bank, Vector3 tgtVel)
        {
            if (ac == null || ac.autopilot == null)
                return;
            try
            {
                AutopilotAim.AutoAim(ac.autopilot, worldAim.ToGlobalPosition(),
                    true, false, false, 0.92f, bank, true, altHold, tgtVel);
            }
            catch { }
        }

        private static void MatchSpeed(Aircraft ac, Aircraft lead, Vector3 slotPos)
        {
            ControlInputs inputs = null;
            try { inputs = ac.GetInputs(); }
            catch { return; }
            if (inputs == null)
                return;
            float want = 0f;
            float have = 0f;
            try
            {
                want = lead.speed;
                have = ac.speed;
            }
            catch { return; }
            float dist = Vector3.Distance(ac.transform.position, slotPos);
            float t = 0.5f + (want - have) * 0.035f;
            if (dist > 350f)
                t += 0.2f;
            if (dist < 40f && have > want + 8f)
                t -= 0.25f;
            inputs.throttle = Mathf.Clamp01(t);
        }

        private static void EnsureCombat(Pilot pilot, Aircraft ac)
        {
            if (pilot == null || ac == null)
                return;
            PilotBaseState st = pilot.currentState;
            if (st is AIPilotCombatModes || st is AIHeloCombatState)
                return;
            bool helo = false;
            try { helo = ac.autopilot is AutopilotHelo; }
            catch { }
            try
            {
                if (helo)
                {
                    if (pilot.AIHeloCombatState == null)
                        return;
                    pilot.SwitchState(pilot.AIHeloCombatState);
                }
                else
                {
                    if (pilot.AICombatState == null)
                        return;
                    pilot.SwitchState(pilot.AICombatState);
                }
            }
            catch { }
        }

        private static void SetCombatTarget(Pilot pilot, Unit target)
        {
            if (pilot == null || target == null)
                return;
            PilotBaseState st = pilot.currentState;
            try
            {
                AIPilotCombatModes combat = st as AIPilotCombatModes;
                if (combat != null && CombatTarget != null)
                {
                    CombatTarget.SetValue(combat, target);
                    return;
                }
                AIHeloCombatState helo = st as AIHeloCombatState;
                if (helo != null && HeloTarget != null)
                    HeloTarget.SetValue(helo, target);
            }
            catch { }
        }

        private static Unit PlayerAttackTarget(Aircraft local)
        {
            List<Unit> list = ReadLocks(local);
            if (list == null)
                return null;
            for (int i = 0; i < list.Count; i++)
            {
                Unit u = list[i];
                if (u == null || u.disabled)
                    continue;
                if (IsWingman(u))
                    continue;
                if (IsFriendlyTo(local, u))
                    continue;
                return u;
            }
            return null;
        }

        private static Unit PrimaryLock(Aircraft local)
        {
            List<Unit> list = ReadLocks(local);
            if (list == null || list.Count == 0)
                return null;
            return list[0];
        }

        private static List<Unit> ReadLocks(Aircraft local)
        {
            try
            {
                if (SceneSingleton<CombatHUD>.i != null)
                {
                    List<Unit> hud = SceneSingleton<CombatHUD>.i.GetTargetList();
                    if (hud != null && hud.Count > 0)
                        return hud;
                }
            }
            catch { }
            try
            {
                if (local != null && local.weaponManager != null)
                    return local.weaponManager.GetTargetList();
            }
            catch { }
            return null;
        }

        private static bool IsFriendlyAi(Aircraft local, Aircraft other)
        {
            if (local == null || other == null || other.disabled)
                return false;
            if (object.ReferenceEquals(local, other))
                return false;
            if (ReverseThrustService.IsPlayerFlown(other))
                return false;
            return IsFriendlyTo(local, other);
        }

        private static bool IsFriendlyTo(Aircraft local, Unit other)
        {
            if (local == null || other == null)
                return false;
            try
            {
                FactionHQ a = local.NetworkHQ;
                FactionHQ b = other.NetworkHQ;
                if (a == null || b == null)
                    return false;
                return object.ReferenceEquals(a, b);
            }
            catch
            {
                return false;
            }
        }

        private static void Prune()
        {
            Aircraft lead = LocalAircraft();
            if (lead == null)
            {
                if (Wingmen.Count > 0)
                    ClearAll();
                return;
            }
            for (int i = Wingmen.Count - 1; i >= 0; i--)
            {
                Aircraft w = Wingmen[i];
                if (w == null || w.disabled || !IsFriendlyTo(lead, w))
                    Remove(w, false);
            }
        }

        private static void Remove(Aircraft ac, bool report)
        {
            if (ac == null)
                return;
            int id = ac.GetInstanceID();
            WingIds.Remove(id);
            for (int i = Wingmen.Count - 1; i >= 0; i--)
            {
                if (Wingmen[i] == null || object.ReferenceEquals(Wingmen[i], ac)
                    || Wingmen[i].GetInstanceID() == id)
                    Wingmen.RemoveAt(i);
            }
        }

        private static void ClearAll()
        {
            Wingmen.Clear();
            WingIds.Clear();
        }

        private static int IndexOf(Aircraft ac)
        {
            if (ac == null)
                return -1;
            for (int i = 0; i < Wingmen.Count; i++)
            {
                if (object.ReferenceEquals(Wingmen[i], ac))
                    return i;
            }
            return -1;
        }

        private static Aircraft LocalAircraft()
        {
            try
            {
                Aircraft ac;
                if (!GameManager.GetLocalAircraft(out ac) || ac == null)
                    return null;
                return ac;
            }
            catch
            {
                return null;
            }
        }

        private static void Report(Aircraft ac, string text)
        {
            if (string.IsNullOrEmpty(text))
                return;
            try
            {
                if (SceneSingleton<AircraftActionsReport>.i != null)
                    SceneSingleton<AircraftActionsReport>.i.ReportText(text, 3f);
            }
            catch { }
        }

        internal static void PaintMarkerGold(HUDUnitMarker marker)
        {
            if (marker == null || !IsWingman(marker.unit))
                return;
            if (MarkerColor != null)
            {
                try { MarkerColor.SetValue(marker, Gold); }
                catch { }
            }
            if (marker.image != null)
                marker.image.color = Gold;
        }

        internal static void PaintTargetInfo(CombatHUD hud)
        {
            if (hud == null)
                return;
            Unit u = null;
            try
            {
                List<Unit> list = hud.GetTargetList();
                if (list != null && list.Count > 0)
                    u = list[0];
            }
            catch { }
            if (!IsWingman(u))
                return;
            Text info = null;
            if (HudTargetInfo != null)
            {
                try { info = HudTargetInfo.GetValue(hud) as Text; }
                catch { info = null; }
            }
            if (info != null)
            {
                info.color = Gold;
                string t = info.text;
                if (!string.IsNullOrEmpty(t) && t.IndexOf("[W]") < 0)
                {
                    int nl = t.IndexOf('\n');
                    if (nl >= 0)
                        info.text = t.Substring(0, nl) + " [W]" + t.Substring(nl);
                    else
                        info.text = t + " [W]";
                }
            }
            try
            {
                if (HudMarkerLookup != null && u != null)
                {
                    Dictionary<Unit, HUDUnitMarker> lookup =
                        HudMarkerLookup.GetValue(hud) as Dictionary<Unit, HUDUnitMarker>;
                    HUDUnitMarker marker = null;
                    if (lookup != null)
                        lookup.TryGetValue(u, out marker);
                    if (marker != null && marker.image != null)
                        marker.image.color = Gold;
                }
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(HUDUnitMarker), "UpdateColor")]
    internal static class Patch_HudMarker_WingmanColor
    {
        [HarmonyPostfix]
        private static void Postfix(HUDUnitMarker __instance)
        {
            WingmanService.PaintMarkerGold(__instance);
        }
    }

    [HarmonyPatch(typeof(HUDUnitMarker), "SetNew")]
    internal static class Patch_HudMarker_WingmanSetNew
    {
        [HarmonyPostfix]
        private static void Postfix(HUDUnitMarker __instance)
        {
            WingmanService.PaintMarkerGold(__instance);
        }
    }

    [HarmonyPatch(typeof(CombatHUD), "ShowTargetInfo")]
    internal static class Patch_CombatHud_WingmanName
    {
        [HarmonyPostfix]
        private static void Postfix(CombatHUD __instance, bool __result)
        {
            if (!__result)
                return;
            WingmanService.PaintTargetInfo(__instance);
        }
    }

    [HarmonyPatch(typeof(Pilot), "Pilot_OnAeroInputsApplied")]
    internal static class Patch_Pilot_WingmanDrive
    {
        [HarmonyPostfix]
        private static void Postfix(Pilot __instance)
        {
            WingmanService.DriveAfterAi(__instance);
        }
    }
}
