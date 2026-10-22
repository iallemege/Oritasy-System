using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Two-seat WSO: in-cockpit assists (flares, target cue, GLOC hold, gear),
    /// and after the local crew punches out, steal a parked jet from an enemy
    /// land base within 15 km, attack, then try to RTB.
    /// </summary>
    internal static class WsoCrewOps
    {
        private const float EnemyBaseRangeM = 15000f;
        private const float ChuteWaitSec = 7f;
        private const float CombatSec = 55f;
        private const float FlareGap = 0.95f;
        private const float CueGap = 2.4f;
        private const float GearGap = 8f;
        private const float GlocHold = 0.34f;
        private const float AskTimeout = 8f;
        private const float AskDenyGap = 12f;
        private const float MissileKillGap = 20f;
        private const float MissileKillRangeM = 10000f;

        private enum AskKind
        {
            None = 0,
            Lock = 1,
            Flares = 2
        }

        private enum Phase
        {
            None = 0,
            WaitChute = 1,
            Steal = 2,
            Takeoff = 3,
            Combat = 4,
            Rtb = 5,
            Done = 6
        }

        private static readonly MethodInfo UnitSetHq = null;
        private static readonly PropertyInfo UnitNetworkHq =
            AccessTools.Property(typeof(Unit), "NetworkHQ");
        private static readonly FieldInfo CombatTarget =
            AccessTools.Field(typeof(AIPilotCombatModes), "currentTarget");
        private static readonly FieldInfo HeloTarget =
            AccessTools.Field(typeof(AIHeloCombatState), "currentTarget");
        private static readonly FieldInfo TakeoffAirbase =
            AccessTools.Field(typeof(AIPilotTakeoffState), "airbase");
        private static readonly FieldInfo LandingAirbase =
            AccessTools.Field(typeof(AIPilotLandingState), "airbase");
        private static readonly FieldInfo LandingRequested =
            AccessTools.Field(typeof(AIPilotLandingState), "requestedLanding");
        private static readonly FieldInfo PilotStrengthField =
            AccessTools.Field(typeof(PilotPlayerState), "pilotStrength");

        private static Phase _phase;
        private static Aircraft _origin;
        private static Aircraft _stolen;
        private static FactionHQ _homeHq;
        private static Airbase _enemyField;
        private static float _phaseAt;
        private static float _combatUntil;
        private static int _stealTries;
        private static string _banner = "";
        private static float _bannerUntil;
        private static GUIStyle _bannerStyle;
        private static GUIStyle _bannerSub;
        private static float _nextFlare;
        private static float _nextCue;
        private static float _nextGear;
        private static float _nextTakeoffNudge;
        private static float _nextCombatAim;
        private static AskKind _ask;
        private static Unit _askUnit;
        private static Aircraft _askAc;
        private static float _askUntil;
        private static float _lockDenyUntil;
        private static float _flareDenyUntil;
        private static GUIStyle _askStyle;
        private static GUIStyle _askSub;
        private static FieldInfo CmAircraftField;
        private static readonly FieldInfo MissileSeekerField =
            AccessTools.Field(typeof(Missile), "seeker");
        private static float _nextMissileKill;
        private static readonly List<Missile> KillScratch = new List<Missile>(64);

        internal static void Tick()
        {
            TickAssist();
            TickHijack();
            WsoEjectService.Tick();
        }

        internal static bool WantsDraw()
        {
            if (_ask != AskKind.None)
                return true;
            if (!string.IsNullOrEmpty(_banner) && Time.unscaledTime <= _bannerUntil)
                return true;
            return false;
        }

        internal static bool BlocksRadio
        {
            get
            {
                return _ask != AskKind.None
                    || (!string.IsNullOrEmpty(_banner) && Time.unscaledTime <= _bannerUntil);
            }
        }

        internal static void Draw()
        {
            if (!WantsDraw())
                return;
            DrawAsk();
            if (_ask != AskKind.None)
                return;
            if (string.IsNullOrEmpty(_banner) || Time.unscaledTime > _bannerUntil)
                return;
            AceRadioChatter.DrawCrewPrompt("WSO", _banner, false);
        }

        private static void EnsureBannerStyles()
        {
            if (_bannerStyle != null)
                return;
            _bannerStyle = new GUIStyle(GUI.skin.label);
            _bannerStyle.fontSize = 18;
            _bannerStyle.fontStyle = FontStyle.Bold;
            _bannerStyle.alignment = TextAnchor.MiddleCenter;
            _bannerStyle.normal.textColor = new Color(1f, 0.86f, 0.28f, 1f);
            _bannerSub = new GUIStyle(GUI.skin.label);
            _bannerSub.fontSize = 14;
            _bannerSub.alignment = TextAnchor.MiddleCenter;
            _bannerSub.normal.textColor = new Color(0.95f, 0.92f, 0.78f, 1f);
        }

        internal static bool IsJesterAircraft(Aircraft ac)
        {
            if (ac == null || _stolen == null)
                return false;
            return object.ReferenceEquals(ac, _stolen);
        }

        internal static bool AllowHqChange(Unit unit, FactionHQ hq)
        {
            Aircraft ac = unit as Aircraft;
            if (!IsJesterAircraft(ac))
                return true;
            if (_homeHq == null)
                return true;
            if (hq == null)
                return false;
            return SameHq(hq, _homeHq);
        }

        internal static void NoteEjection(Aircraft ac)
        {
            if (ac == null || !Plugin.IsRuntimeInstance(ac))
                return;
            if (!IsLocalAircraft(ac))
                return;
            if (_phase != Phase.None && _phase != Phase.Done)
                return;
            if (!CopilotCrewService.HasCopilot(ac))
                return;

            _origin = ac;
            _stolen = null;
            _enemyField = null;
            _homeHq = HqOf(ac);
            _stealTries = 0;
            _phase = Phase.WaitChute;
            _phaseAt = Time.unscaledTime;
            Radio(UiLang.T("Punching with you. If they have a ramp nearby I'll grab a jet.",
                "跟你一起跳。附近要是有他们的停机坪我就去偷一架。"), 6);
        }

        internal static void TryGlocAssist(Pilot pilot)
        {
            if (pilot == null || !pilot.playerControlled)
                return;
            if (PlayerAutopilot.IsEngaged || AerialResupply.IsActive || BeginnerAssist.BlocksControls)
                return;
            if (HudPackState.ManualActive)
                return;
            Aircraft ac = pilot.aircraft;
            if (ac == null || !CopilotCrewService.HasCopilot(ac))
                return;
            if (!ReverseThrustService.IsPlayerFlown(ac))
                return;

            float strength = 1f;
            try
            {
                if (pilot.playerState != null && PilotStrengthField != null)
                {
                    object v = PilotStrengthField.GetValue(pilot.playerState);
                    if (v is float)
                        strength = (float)v;
                }
            }
            catch { return; }
            if (strength > GlocHold)
                return;

            Vector3 pos = ac.transform.position;
            Vector3 fwd = ac.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f)
                fwd = Vector3.forward;
            fwd.Normalize();
            float hold = 200f;
            try { hold = Mathf.Max(80f, ac.radarAlt); }
            catch { }
            Vector3 aim = pos + fwd * 800f;
            try
            {
                AutopilotAim.AutoAim(ac.autopilot, aim.ToGlobalPosition(),
                    true, false, false, 0.9f, AutopilotAim.CruiseBank, false, hold, Vector3.zero);
            }
            catch { }
        }

        private static void TickAssist()
        {
            Aircraft ac = LocalAircraft();
            if (ac == null || ac.disabled)
                return;
            if (!CopilotCrewService.HasCopilot(ac))
                return;
            if (!ReverseThrustService.IsPlayerFlown(ac))
                return;
            TickPendingAsk();
            if (PlayerAutopilot.IsEngaged || AerialResupply.IsActive || BeginnerAssist.BlocksControls)
            {
                TickFlaresOnly(ac);
                TickMissileKill(ac);
                return;
            }
            if (HudPackState.ManualActive)
            {
                TickMissileKill(ac);
                return;
            }

            TickFlaresOnly(ac);
            CueTargets(ac);
            MaybeDropGear(ac);
            TickMissileKill(ac);
        }

        private static void TickPendingAsk()
        {
            if (_ask == AskKind.None)
                return;
            float now = Time.unscaledTime;
            if (now > _askUntil)
            {
                DenyAsk();
                return;
            }
            try
            {
                if (Input.GetKeyDown(KeyCode.Y))
                {
                    AcceptAsk();
                    return;
                }
                if (Input.GetKeyDown(KeyCode.N))
                {
                    DenyAsk();
                    return;
                }
            }
            catch { }
        }

        private static void StartAsk(AskKind kind, Aircraft ac, Unit unit)
        {
            _ask = kind;
            _askAc = ac;
            _askUnit = unit;
            _askUntil = Time.unscaledTime + AskTimeout;
        }

        private static void AcceptAsk()
        {
            Aircraft ac = _askAc;
            Unit unit = _askUnit;
            AskKind kind = _ask;
            ClearAsk();
            if (kind == AskKind.Lock)
                ApplyWsoLock(ac, unit);
            else if (kind == AskKind.Flares)
                AiCombatEvadeService.DumpFlares(ac);
        }

        private static void DenyAsk()
        {
            float now = Time.unscaledTime;
            if (_ask == AskKind.Lock)
                _lockDenyUntil = now + AskDenyGap;
            else if (_ask == AskKind.Flares)
                _flareDenyUntil = now + AskDenyGap;
            ClearAsk();
        }

        private static void ClearAsk()
        {
            _ask = AskKind.None;
            _askAc = null;
            _askUnit = null;
            _askUntil = 0f;
        }

        private static void ApplyWsoLock(Aircraft ac, Unit pick)
        {
            if (ac == null || pick == null)
                return;
            try
            {
                if (ac.weaponManager != null)
                    ac.weaponManager.AddTargetList(pick);
            }
            catch { }
            Pilot wso = CopilotSeat(ac);
            if (wso != null)
            {
                try { wso.SetPrimaryTarget(pick); }
                catch { }
            }
        }

        private static void DrawAsk()
        {
            if (_ask == AskKind.None)
                return;
            string quote;
            if (_ask == AskKind.Lock)
            {
                string name = "";
                try
                {
                    if (_askUnit != null && _askUnit.definition != null)
                        name = _askUnit.definition.unitName;
                }
                catch { }
                if (string.IsNullOrEmpty(name))
                    name = UiLang.T("Hostile", "敌机");
                quote = UiLang.T("Lock " + name + "?", "锁定 " + name + "？");
            }
            else
                quote = UiLang.T("Dump flares?", "投放热诱弹？");
            AceRadioChatter.DrawCrewPrompt("WSO", quote, true);
        }

        private static void EnsureAskStyles()
        {
            if (_askStyle != null)
                return;
            _askStyle = new GUIStyle(GUI.skin.label);
            _askStyle.fontSize = 16;
            _askStyle.fontStyle = FontStyle.Bold;
            _askStyle.alignment = TextAnchor.MiddleCenter;
            _askStyle.normal.textColor = new Color(0.85f, 1f, 0.7f, 1f);
            _askSub = new GUIStyle(GUI.skin.label);
            _askSub.fontSize = 13;
            _askSub.alignment = TextAnchor.MiddleCenter;
            _askSub.normal.textColor = new Color(0.92f, 0.95f, 0.82f, 1f);
        }

        private static void TickFlaresOnly(Aircraft ac)
        {
            if (_ask != AskKind.None)
                return;
            float now = Time.unscaledTime;
            if (now < _nextFlare)
                return;
            if (!MissileInbound(ac))
                return;
            if (now < _flareDenyUntil)
                return;
            _nextFlare = now + FlareGap;
            StartAsk(AskKind.Flares, ac, null);
        }

        private static void CueTargets(Aircraft ac)
        {
            if (_ask != AskKind.None)
                return;
            float now = Time.unscaledTime;
            if (now < _nextCue)
                return;
            if (now < _lockDenyUntil)
                return;
            Unit have = PlayerLock(ac);
            if (have != null)
                return;
            Unit pick = NearestHostile(ac, 12000f, true);
            if (pick == null)
                return;
            _nextCue = now + CueGap;
            StartAsk(AskKind.Lock, ac, pick);
        }

        internal static bool LocalWsoLead()
        {
            Aircraft ac = LocalAircraft();
            if (ac == null || ac.disabled)
                return false;
            if (!ReverseThrustService.IsPlayerFlown(ac))
                return false;
            return CopilotCrewService.HasCopilot(ac);
        }

        private static void TickMissileKill(Aircraft ac)
        {
            float now = Time.unscaledTime;
            if (now < _nextMissileKill)
                return;
            _nextMissileKill = now + MissileKillGap;
            if (ac == null || ac.transform == null)
                return;
            Vector3 origin = ac.transform.position;
            float r2 = MissileKillRangeM * MissileKillRangeM;
            CollectKillMissiles();
            int n = 0;
            for (int i = 0; i < KillScratch.Count; i++)
            {
                Missile m = KillScratch[i];
                if (m == null)
                    continue;
                try
                {
                    if (m.disabled)
                        continue;
                    if ((m.transform.position - origin).sqrMagnitude > r2)
                        continue;
                    if (!IsHostileMissile(ac, m))
                        continue;
                    if (IsGunLikeMissile(m))
                        continue;
                    Aircraft parent = m.GetComponentInParent<Aircraft>();
                    if (parent != null)
                        continue;
                    m.Detonate(Vector3.up, false, true);
                    n++;
                }
                catch { }
            }
            if (n > 0)
            {
                Radio(UiLang.T("Inbound missiles dumped. Ten clicks, splash.",
                    "十公里内敌弹已自毁。"), 4);
            }
        }

        private static void CollectKillMissiles()
        {
            KillScratch.Clear();
            List<Unit> units = null;
            try { units = UnitRegistry.allUnits; }
            catch { units = null; }
            if (units == null)
                return;
            for (int i = 0; i < units.Count; i++)
            {
                Missile m = units[i] as Missile;
                if (m != null)
                    KillScratch.Add(m);
            }
        }

        private static bool IsHostileMissile(Aircraft from, Missile m)
        {
            if (from == null || m == null)
                return false;
            try
            {
                if (object.ReferenceEquals(m.owner, from))
                    return false;
            }
            catch { }
            Unit owner = null;
            try { owner = m.owner; }
            catch { owner = null; }
            if (owner != null)
            {
                if (IsHostile(from, owner))
                    return true;
                try
                {
                    if (object.ReferenceEquals(owner, from))
                        return false;
                }
                catch { }
                return false;
            }
            try
            {
                FactionHQ a = from.NetworkHQ;
                FactionHQ b = m.NetworkHQ;
                if (a != null && b != null)
                    return !SameHq(a, b);
            }
            catch { }
            return false;
        }

        private static bool IsGunLikeMissile(Missile m)
        {
            if (m == null)
                return true;
            string n = m.name != null ? m.name : string.Empty;
            if (n.IndexOf("shell", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("bullet", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("round", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("gunpod", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            try
            {
                if (m.definition != null)
                {
                    string kn = m.definition.unitName != null ? m.definition.unitName : string.Empty;
                    string kk = m.definition.jsonKey != null ? m.definition.jsonKey : string.Empty;
                    if (kn.IndexOf("shell", StringComparison.OrdinalIgnoreCase) >= 0
                        || kk.IndexOf("shell", StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch { }
            if (MissileSeekerField != null)
            {
                try
                {
                    object seeker = MissileSeekerField.GetValue(m);
                    if (seeker == null)
                        return true;
                }
                catch { }
            }
            return false;
        }

        private static void MaybeDropGear(Aircraft ac)
        {
            float now = Time.unscaledTime;
            if (now < _nextGear)
                return;
            if (BeginnerAssist._takeoff != BeginnerAssist.TakeoffPhase.Idle)
                return;
            bool landed = false;
            try { landed = ac.IsLanded(); }
            catch { }
            if (landed)
                return;
            float ralt = 99f;
            float spd = 200f;
            float vy = 0f;
            try
            {
                ralt = ac.radarAlt;
                spd = ac.speed;
            }
            catch { }
            try
            {
                if (ac.rb != null)
                    vy = ac.rb.velocity.y;
            }
            catch { }
            if (ralt > 22f || spd < 22f || spd > 72f || vy > -1.5f)
                return;
            bool down = false;
            try { down = ac.gearDeployed; }
            catch
            {
                try { down = ac.gearState != LandingGear.GearState.LockedRetracted; }
                catch { }
            }
            if (down)
                return;
            _nextGear = now + GearGap;
            try { ac.SetGear(true); }
            catch { }
            Radio(UiLang.T("Gear. You're low and slow.", "放轮。你又低又慢。"), 5);
        }

        private static void TickHijack()
        {
            if (_phase == Phase.None || _phase == Phase.Done)
                return;

            if (_phase == Phase.WaitChute)
            {
                if (ChuteReady())
                {
                    _phase = Phase.Steal;
                    _phaseAt = Time.unscaledTime;
                }
                else if (Time.unscaledTime - _phaseAt > 22f)
                {
                    Radio(UiLang.T("Lost you in the chute. I'm out.", "伞上跟丢了。我撤了。"), 5);
                    _phase = Phase.Done;
                }
                return;
            }

            if (_phase == Phase.Steal)
            {
                TrySteal();
                return;
            }

            if (_stolen == null || !Plugin.IsRuntimeInstance(_stolen) || _stolen.disabled)
            {
                if (_phase == Phase.Combat || _phase == Phase.Takeoff || _phase == Phase.Rtb)
                    Radio(UiLang.T("Jet's gone. I'm on the ground.", "飞机没了。我在地上。"), 5);
                _phase = Phase.Done;
                _stolen = null;
                return;
            }

            HoldStolenHq(_stolen);

            if (_phase == Phase.Takeoff)
            {
                DriveTakeoff(_stolen);
                if (Airborne(_stolen) || Time.unscaledTime - _phaseAt > 40f)
                    EnterCombat();
                return;
            }

            if (_phase == Phase.Combat)
            {
                DriveCombat(_stolen);
                if (Time.unscaledTime >= _combatUntil)
                    EnterRtb();
                return;
            }

            if (_phase == Phase.Rtb)
            {
                if (LandedNow(_stolen) && Time.unscaledTime - _phaseAt > 8f)
                {
                    if (NearHomeField(_stolen))
                    {
                        Radio(UiLang.T("Down at a friendly field. See you.", "落到友军场了。回头见。"), 5);
                        _phase = Phase.Done;
                    }
                    else
                    {
                        Radio(UiLang.T("Still on their ramp. Getting airborne.", "还在他们停机坪。重新起飞。"), 6);
                        _phase = Phase.Takeoff;
                        _phaseAt = Time.unscaledTime;
                        Pilot p = AiPilot(_stolen);
                        if (p != null)
                            StartTakeoff(p, _stolen, _enemyField);
                    }
                }
                else if (Time.unscaledTime - _phaseAt > 180f)
                    _phase = Phase.Done;
            }
        }

        private static bool ChuteReady()
        {
            if (Time.unscaledTime - _phaseAt >= ChuteWaitSec)
                return true;
            PilotDismounted pd = null;
            try
            {
                if (!GameManager.GetLocalPilotDismounted(out pd) || pd == null)
                    return false;
            }
            catch { return false; }
            try
            {
                PilotDismounted.PilotState st = pd.animationState;
                if (st == PilotDismounted.PilotState.parachuting
                    || st == PilotDismounted.PilotState.landing
                    || st == PilotDismounted.PilotState.dead)
                {
                    if (pd.timeSinceSpawn >= 5.5f)
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static void TrySteal()
        {
            if (!IsListenHost())
            {
                Radio(UiLang.T("Can't steal that jet from a client slot.", "客户端没法偷那架飞机。"), 5);
                _phase = Phase.Done;
                return;
            }

            Vector3 from = ChutePos();
            Airbase field = null;
            FactionHQ enemyHq = null;
            Vector3 center = Vector3.zero;
            float radius = 800f;
            if (!FindEnemyLandBase(from, _homeHq, EnemyBaseRangeM, out field, out enemyHq, out center, out radius))
            {
                Radio(UiLang.T("No enemy field inside 15. I'm walking.", "15 公里内没有敌军机场。我走着回去。"), 5);
                _phase = Phase.Done;
                return;
            }

            Aircraft jet = FindParkedAt(field, enemyHq, center, radius, _origin);
            if (jet == null)
            {
                _stealTries++;
                if (_stealTries < 3 && Time.unscaledTime - _phaseAt < 10f)
                    return;
                Radio(UiLang.T("Ramp's empty. No jet to grab.", "停机坪是空的。没飞机可偷。"), 5);
                _phase = Phase.Done;
                return;
            }

            ApplyUnitHq(jet, _homeHq);
            FreePilots(jet);
            _stolen = jet;
            _enemyField = field;
            _phase = Phase.Takeoff;
            _phaseAt = Time.unscaledTime;
            _nextTakeoffNudge = 0f;

            Pilot p = AiPilot(jet);
            if (p != null)
                StartTakeoff(p, jet, field);

            string fieldName = AirbaseName(field);
            string jetName = JetDisplayName(jet);
            Radio(UiLang.T("Stole a jet off " + fieldName + ". Going after them.",
                "从 " + fieldName + " 偷到一架。去打他们。"), 7);
            ShowStealBanner(jetName, fieldName);
            try
            {
                AircraftActionsReport rpt = SceneSingleton<AircraftActionsReport>.i;
                if (rpt != null)
                    rpt.ReportText(UiLang.T("JESTER STOLE A JET", "Jester 偷到一架飞机"), 4f);
            }
            catch { }
            try
            {
                AirframeWearService.FlashHudMessage(
                    UiLang.T("JESTER STOLE A JET", "Jester 偷到一架飞机"), 3);
            }
            catch { }
        }

        private static void EnterCombat()
        {
            _phase = Phase.Combat;
            _phaseAt = Time.unscaledTime;
            _combatUntil = Time.unscaledTime + CombatSec;
            _nextCombatAim = 0f;
            Aircraft jet = _stolen;
            if (jet == null)
                return;
            try { jet.SetGear(false); }
            catch { }
            Pilot p = AiPilot(jet);
            if (p != null)
            {
                EnsureCombat(p, jet);
                Unit tgt = NearestHostile(jet, 18000f, false);
                if (tgt != null)
                    SetCombatTarget(p, jet, tgt);
            }
            Radio(UiLang.T("Airborne. Engaging.", "离地了。接敌。"), 6);
        }

        private static void EnterRtb()
        {
            _phase = Phase.Rtb;
            _phaseAt = Time.unscaledTime;
            Aircraft jet = _stolen;
            if (jet == null)
                return;
            Pilot p = AiPilot(jet);
            Airbase home = FindFriendlyLandBase(jet.transform.position, _homeHq);
            if (p != null)
                StartLanding(p, jet, home);
            string dest = home != null ? AirbaseName(home) : UiLang.T("a friendly field", "友军机场");
            Radio(UiLang.T("RTB " + dest + ".", "返航 " + dest + "。"), 6);
        }

        private static void DriveTakeoff(Aircraft jet)
        {
            float now = Time.unscaledTime;
            if (now < _nextTakeoffNudge)
                return;
            _nextTakeoffNudge = now + 0.35f;
            if (Airborne(jet))
                return;
            try { jet.SetGear(true); }
            catch { }
            try
            {
                ControlInputs inputs = jet.GetInputs();
                if (inputs != null)
                {
                    inputs.throttle = 1f;
                    inputs.brake = 0f;
                }
            }
            catch { }
            Vector3 pos = jet.transform.position;
            Vector3 fwd = jet.transform.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 0.01f)
                fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 aim = pos + fwd * 2500f;
            float hold = 30f;
            try
            {
                if (jet.speed > 55f)
                    hold = 180f;
            }
            catch { }
            try
            {
                AutopilotAim.AutoAim(jet.autopilot, aim.ToGlobalPosition(),
                    true, false, false, 0.95f, AutopilotAim.CruiseBank, false, hold, Vector3.zero);
            }
            catch { }
            Pilot p = AiPilot(jet);
            if (p != null && !(p.currentState is AIPilotTakeoffState) && !(p.currentState is AIHeloTakeoffState))
                StartTakeoff(p, jet, _enemyField);
        }

        private static void DriveCombat(Aircraft jet)
        {
            float now = Time.unscaledTime;
            if (now < _nextCombatAim)
                return;
            _nextCombatAim = now + 1.2f;
            Pilot p = AiPilot(jet);
            if (p == null)
                return;
            EnsureCombat(p, jet);
            Unit tgt = NearestHostile(jet, 18000f, false);
            if (tgt != null)
                SetCombatTarget(p, jet, tgt);
        }

        private static void StartTakeoff(Pilot pilot, Aircraft jet, Airbase field)
        {
            if (pilot == null || jet == null)
                return;
            bool helo = IsHelo(jet);
            try
            {
                if (helo)
                {
                    if (pilot.AIHeloTakeoffState != null)
                        pilot.SwitchState(pilot.AIHeloTakeoffState);
                    return;
                }
                if (pilot.AITakeoffState != null)
                {
                    if (TakeoffAirbase != null && field != null)
                    {
                        try { TakeoffAirbase.SetValue(pilot.AITakeoffState, field); }
                        catch { }
                    }
                    pilot.SwitchState(pilot.AITakeoffState);
                }
            }
            catch { }
        }

        private static void StartLanding(Pilot pilot, Aircraft jet, Airbase home)
        {
            if (pilot == null || jet == null)
                return;
            bool helo = IsHelo(jet);
            try
            {
                if (helo)
                {
                    if (pilot.AIHeloLandingState != null)
                        pilot.SwitchState(pilot.AIHeloLandingState);
                    return;
                }
                if (pilot.AILandingState != null)
                {
                    if (LandingAirbase != null && home != null)
                    {
                        try { LandingAirbase.SetValue(pilot.AILandingState, home); }
                        catch { }
                    }
                    if (LandingRequested != null)
                    {
                        try { LandingRequested.SetValue(pilot.AILandingState, true); }
                        catch { }
                    }
                    pilot.SwitchState(pilot.AILandingState);
                }
            }
            catch { }
        }

        private static void EnsureCombat(Pilot pilot, Aircraft jet)
        {
            if (pilot == null || jet == null)
                return;
            PilotBaseState st = pilot.currentState;
            if (st is AIPilotCombatModes || st is AIHeloCombatState)
                return;
            bool helo = IsHelo(jet);
            try
            {
                if (helo)
                {
                    if (pilot.AIHeloCombatState != null)
                        pilot.SwitchState(pilot.AIHeloCombatState);
                }
                else if (pilot.AICombatState != null)
                    pilot.SwitchState(pilot.AICombatState);
            }
            catch { }
        }

        private static void SetCombatTarget(Pilot pilot, Aircraft jet, Unit target)
        {
            if (pilot == null || target == null)
                return;
            try { pilot.SetPrimaryTarget(target); }
            catch { }
            try
            {
                if (jet != null && jet.weaponManager != null)
                    jet.weaponManager.AddTargetList(target);
            }
            catch { }
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

        private static void FreePilots(Aircraft jet)
        {
            if (jet == null || jet.pilots == null)
                return;
            for (int i = 0; i < jet.pilots.Length; i++)
            {
                Pilot p = jet.pilots[i];
                if (p == null)
                    continue;
                try { p.playerControlled = false; }
                catch { }
            }
        }

        private static bool NearHomeField(Aircraft jet)
        {
            if (jet == null)
                return false;
            Airbase home = FindFriendlyLandBase(jet.transform.position, _homeHq);
            if (home == null)
                return false;
            float r = LandRadius(home);
            if (r < 700f)
                r = 700f;
            return Horizontal(jet.transform.position, CenterOf(home)) <= r * 1.4f;
        }

        private static void HoldStolenHq(Aircraft jet)
        {
            if (jet == null || _homeHq == null)
                return;
            if (SameHq(HqOf(jet), _homeHq))
                return;
            ApplyUnitHq(jet, _homeHq);
        }

        private static void ApplyUnitHq(Unit unit, FactionHQ hq)
        {
            if (unit == null || hq == null)
                return;
            if (UnitSetHq != null)
            {
                try { UnitSetHq.Invoke(unit, new object[] { hq }); return; }
                catch { }
            }
            if (UnitNetworkHq != null && UnitNetworkHq.CanWrite)
            {
                try { UnitNetworkHq.SetValue(unit, hq, null); }
                catch { }
            }
        }

        private static bool FindEnemyLandBase(Vector3 from, FactionHQ myHq, float maxM,
            out Airbase field, out FactionHQ enemyHq, out Vector3 center, out float radius)
        {
            field = null;
            enemyHq = null;
            center = Vector3.zero;
            radius = 800f;
            Airbase[] all = null;
            try { all = UnityEngine.Object.FindObjectsOfType<Airbase>(); }
            catch { }
            if (all == null)
                return false;
            float best = maxM;
            for (int i = 0; i < all.Length; i++)
            {
                Airbase ab = all[i];
                if (!UsableLandBase(ab))
                    continue;
                FactionHQ hq = null;
                try { hq = ab.CurrentHQ; }
                catch { }
                if (hq == null || SameHq(hq, myHq))
                    continue;
                Vector3 c = CenterOf(ab);
                float d = Horizontal(from, c);
                if (d >= best)
                    continue;
                best = d;
                field = ab;
                enemyHq = hq;
                center = c;
                radius = LandRadius(ab);
            }
            return field != null;
        }

        private static Airbase FindFriendlyLandBase(Vector3 from, FactionHQ myHq)
        {
            Airbase[] all = null;
            try { all = UnityEngine.Object.FindObjectsOfType<Airbase>(); }
            catch { }
            if (all == null)
                return null;
            float best = float.MaxValue;
            Airbase pick = null;
            for (int i = 0; i < all.Length; i++)
            {
                Airbase ab = all[i];
                if (!UsableLandBase(ab))
                    continue;
                FactionHQ hq = null;
                try { hq = ab.CurrentHQ; }
                catch { }
                if (hq == null || !SameHq(hq, myHq))
                    continue;
                float d = Horizontal(from, CenterOf(ab));
                if (d >= best)
                    continue;
                best = d;
                pick = ab;
            }
            return pick;
        }

        private static Aircraft FindParkedAt(Airbase field, FactionHQ enemyHq, Vector3 center,
            float radius, Aircraft exclude)
        {
            List<Aircraft> all = null;
            try { all = UnitRegistry.allAircraft; }
            catch { }
            if (all == null)
                return null;
            float max = radius + 450f;
            if (max < 1100f)
                max = 1100f;
            float maxSq = max * max;
            Aircraft best = null;
            float bestSq = maxSq;
            for (int i = 0; i < all.Count; i++)
            {
                Aircraft ac = all[i];
                if (ac == null || object.ReferenceEquals(ac, exclude))
                    continue;
                if (!Plugin.IsRuntimeInstance(ac))
                    continue;
                try
                {
                    if (ac.disabled)
                        continue;
                }
                catch { continue; }
                if (ReverseThrustService.IsPlayerFlown(ac))
                    continue;
                if (!SameHq(HqOf(ac), enemyHq))
                    continue;
                if (!ParkedEnough(ac))
                    continue;
                if (AiPilot(ac) == null)
                    continue;
                float sq = HorizontalSq(ac.transform.position, center);
                if (sq >= bestSq)
                    continue;
                bestSq = sq;
                best = ac;
            }
            return best;
        }

        private static bool ParkedEnough(Aircraft ac)
        {
            try
            {
                if (ac.IsLanded())
                    return true;
            }
            catch { }
            try
            {
                return ac.radarAlt < 8f && ac.speed < 22f;
            }
            catch { return false; }
        }

        private static bool UsableLandBase(Airbase ab)
        {
            if (ab == null)
                return false;
            try
            {
                if (ab.disabled)
                    return false;
            }
            catch { }
            try
            {
                if (ab.AttachedAirbase)
                    return false;
            }
            catch { }
            if (AirbaseLocator.IsCarrierAirbase(ab))
                return false;
            return true;
        }

        private static Unit NearestHostile(Aircraft from, float rangeM, bool aircraftOnly)
        {
            if (from == null)
                return null;
            List<Unit> all = null;
            try { all = UnitRegistry.allUnits; }
            catch { }
            if (all == null)
                return null;
            Vector3 pos = from.transform.position;
            float bestSq = rangeM * rangeM;
            Unit best = null;
            for (int i = 0; i < all.Count; i++)
            {
                Unit u = all[i];
                if (u == null || object.ReferenceEquals(u, from))
                    continue;
                try
                {
                    if (u.disabled)
                        continue;
                }
                catch { continue; }
                if (aircraftOnly && !(u is Aircraft))
                    continue;
                if (!IsHostile(from, u))
                    continue;
                if (!(u is Aircraft) && !(u is GroundVehicle) && !(u is Ship) && !(u is Building))
                    continue;
                float sq = (u.transform.position - pos).sqrMagnitude;
                if (sq >= bestSq)
                    continue;
                bestSq = sq;
                best = u;
            }
            return best;
        }

        private static bool IsHostile(Aircraft from, Unit other)
        {
            if (from == null || other == null)
                return false;
            try
            {
                FactionHQ a = from.NetworkHQ;
                FactionHQ b = other.NetworkHQ;
                if (a == null || b == null)
                    return false;
                return !SameHq(a, b);
            }
            catch { return false; }
        }

        private static bool SameHq(FactionHQ a, FactionHQ b)
        {
            if (a == null || b == null)
                return false;
            if (object.ReferenceEquals(a, b))
                return true;
            try
            {
                if (a.faction != null && b.faction != null)
                {
                    if (object.ReferenceEquals(a.faction, b.faction))
                        return true;
                    string na = a.faction.factionName;
                    string nb = b.faction.factionName;
                    if (!string.IsNullOrEmpty(na) && !string.IsNullOrEmpty(nb)
                        && string.Equals(na, nb, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static FactionHQ HqOf(Aircraft ac)
        {
            if (ac != null)
            {
                try
                {
                    FactionHQ h = ac.NetworkHQ;
                    if (h != null)
                        return h;
                }
                catch { }
            }
            try
            {
                FactionHQ hq;
                if (GameManager.GetLocalHQ(out hq) && hq != null)
                    return hq;
            }
            catch { }
            try
            {
                Player p;
                if (GameManager.GetLocalPlayer(out p) && p != null)
                    return p.HQ;
            }
            catch { }
            return null;
        }

        private static bool MissileInbound(Aircraft ac)
        {
            if (ac == null)
                return false;
            try
            {
                MissileWarning mw = ac.GetMissileWarningSystem();
                if (mw == null)
                    return false;
                Missile nearest = null;
                if (mw.TryGetNearestIncoming(out nearest) && nearest != null)
                    return true;
                if (mw.IsWarning())
                    return true;
                if (mw.knownMissiles != null && mw.knownMissiles.Count > 0)
                    return true;
            }
            catch { }
            return false;
        }

        private static Unit PlayerLock(Aircraft ac)
        {
            try
            {
                CombatHUD hud = SceneSingleton<CombatHUD>.i;
                if (hud != null)
                {
                    List<Unit> list = hud.GetTargetList();
                    if (list != null)
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            Unit u = list[i];
                            if (u != null && !u.disabled)
                                return u;
                        }
                    }
                }
            }
            catch { }
            try
            {
                if (ac != null && ac.weaponManager != null)
                {
                    List<Unit> list = ac.weaponManager.GetTargetList();
                    if (list != null && list.Count > 0)
                        return list[0];
                }
            }
            catch { }
            return null;
        }

        private static Pilot CopilotSeat(Aircraft ac)
        {
            if (ac == null || ac.pilots == null)
                return null;
            for (int i = 0; i < ac.pilots.Length; i++)
            {
                Pilot p = ac.pilots[i];
                if (p == null)
                    continue;
                if (CopilotCrewService.IsCopilot(p))
                    return p;
            }
            return null;
        }

        private static Pilot AiPilot(Aircraft ac)
        {
            if (ac == null || ac.pilots == null)
                return null;
            Pilot fallback = null;
            for (int i = 0; i < ac.pilots.Length; i++)
            {
                Pilot p = ac.pilots[i];
                if (p == null || p.dead || p.ejected)
                    continue;
                fallback = p;
                if (!p.playerControlled)
                    return p;
            }
            return fallback;
        }

        private static bool Airborne(Aircraft ac)
        {
            if (ac == null)
                return false;
            try
            {
                if (ac.IsLanded())
                    return false;
            }
            catch { }
            try
            {
                return ac.radarAlt > 55f || ac.speed > 95f;
            }
            catch { return false; }
        }

        private static bool LandedNow(Aircraft ac)
        {
            if (ac == null)
                return false;
            try { return ac.IsLanded(); }
            catch
            {
                try { return ac.radarAlt < 3f && ac.speed < 20f; }
                catch { return false; }
            }
        }

        private static bool IsHelo(Aircraft ac)
        {
            if (ac == null)
                return false;
            try { return ac.autopilot is AutopilotHelo; }
            catch { return false; }
        }

        private static Vector3 ChutePos()
        {
            PilotDismounted pd = null;
            try
            {
                if (GameManager.GetLocalPilotDismounted(out pd) && pd != null)
                    return pd.transform.position;
            }
            catch { }
            if (_origin != null)
            {
                try { return _origin.transform.position; }
                catch { }
            }
            return Vector3.zero;
        }

        private static Vector3 CenterOf(Airbase ab)
        {
            try
            {
                if (ab.center != null)
                    return ab.center.position;
            }
            catch { }
            try { return ab.transform.position; }
            catch { return Vector3.zero; }
        }

        private static float LandRadius(Airbase ab)
        {
            float r = 0f;
            try { r = ab.GetRadius(); }
            catch { }
            if (r < 400f)
                r = 400f;
            if (r > 8000f)
                r = 8000f;
            return r;
        }

        private static float Horizontal(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static float HorizontalSq(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        private static void ShowStealBanner(string jetName, string fieldName)
        {
            if (string.IsNullOrEmpty(jetName))
                jetName = UiLang.T("a jet", "一架飞机");
            if (string.IsNullOrEmpty(fieldName))
                fieldName = "?";
            _banner = UiLang.T(
                jetName + "  from  " + fieldName,
                jetName + "  ·  " + fieldName);
            _bannerUntil = Time.unscaledTime + 8f;
        }

        private static string JetDisplayName(Aircraft ac)
        {
            if (ac == null)
                return "";
            try
            {
                AircraftDefinition def = ac.definition;
                if (def != null && !string.IsNullOrEmpty(def.unitName))
                    return def.unitName;
            }
            catch { }
            try
            {
                if (!string.IsNullOrEmpty(ac.unitName))
                    return ac.unitName;
            }
            catch { }
            try { return ac.name; }
            catch { return ""; }
        }

        private static string AirbaseName(Airbase ab)
        {
            string name = null;
            try { name = AirbaseLocator.FormatAirbaseName(ab, false); }
            catch { }
            if (string.IsNullOrEmpty(name))
                return "?";
            return name;
        }

        private static bool IsLocalAircraft(Aircraft ac)
        {
            if (ac == null)
                return false;
            Aircraft local = null;
            try
            {
                if (GameManager.GetLocalAircraft(out local) && local != null)
                    return object.ReferenceEquals(local, ac);
            }
            catch { }
            return false;
        }

        internal static bool ShouldMuteCopilotAi(Pilot pilot)
        {
            if (pilot == null)
                return false;
            if (!CopilotCrewService.IsCopilot(pilot))
                return false;
            Aircraft ac = null;
            try { ac = pilot.aircraft; }
            catch { ac = null; }
            if (ac == null)
            {
                try { ac = pilot.GetComponentInParent<Aircraft>(); }
                catch { ac = null; }
            }
            if (ac == null)
                return false;
            if (IsJesterAircraft(ac))
                return false;
            return ReverseThrustService.IsPlayerFlown(ac);
        }

        internal static bool GuardPlayerCmSelect(Aircraft ac)
        {
            if (ac == null)
                return false;
            if (IsJesterAircraft(ac))
                return false;
            if (!ReverseThrustService.IsPlayerFlown(ac))
                return false;
            return CopilotCrewService.HasCopilot(ac);
        }

        internal static Aircraft AircraftOfCm(CountermeasureManager mgr)
        {
            if (mgr == null)
                return null;
            try
            {
                if (CmAircraftField == null)
                    CmAircraftField = AccessTools.Field(typeof(CountermeasureManager), "aircraft");
                if (CmAircraftField != null)
                    return CmAircraftField.GetValue(mgr) as Aircraft;
            }
            catch { }
            return null;
        }

        private static Aircraft LocalAircraft()
        {
            Aircraft ac = null;
            try
            {
                if (GameManager.GetLocalAircraft(out ac) && ac != null && Plugin.IsRuntimeInstance(ac))
                    return ac;
            }
            catch { }
            return null;
        }

        private static bool IsListenHost()
        {
            try
            {
                Player p;
                if (GameManager.GetLocalPlayer(out p) && p != null && p.IsServer)
                    return true;
            }
            catch { }
            try
            {
                NetworkManagerNuclearOption nm = NetworkManagerNuclearOption.i;
                if (nm != null && nm.Server != null && nm.Server.Active)
                    return true;
            }
            catch { }
            return false;
        }

        private static void Radio(string quote, int pri)
        {
            if (string.IsNullOrEmpty(quote))
                return;
            try { AceRadioChatter.TryQueueCrew("Jester", quote, pri); }
            catch { }
        }
    }

    [HarmonyPatch(typeof(Unit), "NetworkHQ", MethodType.Setter)]
    internal static class Patch_Jester_SetHq
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Unit __instance, FactionHQ value)
        {
            return WsoCrewOps.AllowHqChange(__instance, value);
        }
    }

    [HarmonyPatch(typeof(Airbase), "AddControlledAircraft")]
    internal static class Patch_Jester_AddControlled
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Airbase __instance, Aircraft aircraft)
        {
            if (!WsoCrewOps.IsJesterAircraft(aircraft) || __instance == null)
                return true;
            try
            {
                if (!WsoCrewOps.AllowHqChange(aircraft, __instance.CurrentHQ))
                    return false;
            }
            catch { }
            return true;
        }
    }

    [HarmonyPatch(typeof(Aircraft), "SpawnEjectingPilot")]
    internal static class Patch_Wso_SpawnEjectingPilot
    {
        [HarmonyPrefix]
        private static void Prefix(Aircraft __instance)
        {
            WsoCrewOps.NoteEjection(__instance);
        }
    }

    [HarmonyPatch(typeof(PilotPlayerState), "FixedUpdateState")]
    internal static class Patch_Wso_GlocHold
    {
        [HarmonyPostfix]
        private static void Postfix(PilotPlayerState __instance, Pilot pilot)
        {
            WsoCrewOps.TryGlocAssist(pilot);
        }
    }

    [HarmonyPatch(typeof(CountermeasureManager), "ChooseCountermeasure")]
    internal static class Patch_Wso_BlockChooseCm
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(CountermeasureManager __instance)
        {
            Aircraft ac = WsoCrewOps.AircraftOfCm(__instance);
            if (!WsoCrewOps.GuardPlayerCmSelect(ac))
                return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(Aircraft), "Countermeasures", new Type[] { typeof(bool), typeof(byte) })]
    internal static class Patch_Wso_KeepCmIndex
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(Aircraft __instance, ref byte index)
        {
            if (!WsoCrewOps.GuardPlayerCmSelect(__instance))
                return;
            try
            {
                if (__instance.countermeasureManager != null)
                    index = __instance.countermeasureManager.activeIndex;
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(AIPilotCombatModes), "FixedUpdateState")]
    internal static class Patch_Wso_MuteCombat
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Pilot pilot)
        {
            return !WsoCrewOps.ShouldMuteCopilotAi(pilot);
        }
    }

    [HarmonyPatch(typeof(AIPilotCombatModes), "UpdateState")]
    internal static class Patch_Wso_MuteCombatUpdate
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Pilot pilot)
        {
            return !WsoCrewOps.ShouldMuteCopilotAi(pilot);
        }
    }

    [HarmonyPatch(typeof(AIHeloCombatState), "FixedUpdateState")]
    internal static class Patch_Wso_MuteHeloCombat
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Pilot pilot)
        {
            return !WsoCrewOps.ShouldMuteCopilotAi(pilot);
        }
    }

    [HarmonyPatch(typeof(AIHeloTransportState), "FixedUpdateState")]
    internal static class Patch_Wso_MuteHeloTransport
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Pilot pilot)
        {
            return !WsoCrewOps.ShouldMuteCopilotAi(pilot);
        }
    }

    [HarmonyPatch(typeof(AIHeloTransportState), "ChooseCountermeasures")]
    internal static class Patch_Wso_MuteHeloChooseCm
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(AIHeloTransportState __instance)
        {
            Aircraft ac = null;
            try
            {
                FieldInfo f = AccessTools.Field(typeof(PilotBaseState), "aircraft");
                if (f != null)
                    ac = f.GetValue(__instance) as Aircraft;
            }
            catch { }
            return !WsoCrewOps.GuardPlayerCmSelect(ac);
        }
    }

    [HarmonyPatch(typeof(TargetCalc), "TargetLeadTime")]
    internal static class Patch_Wso_LeadTime
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(ref int iterations)
        {
            if (!WsoCrewOps.LocalWsoLead())
                return;
            if (iterations < 12)
                iterations = 12;
        }
    }
}
