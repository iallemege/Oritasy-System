using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// F10 subsidiary panel: list damaged / detached airframe parts and repair them.
    /// Uses vanilla UnitPart.Repair / AeroPart.CheckAttachment plus engine & fuel-tank field resets.
    /// </summary>
    internal static class ComponentRepair
    {
        private static readonly FieldInfo CriticalPartField =
            AccessTools.Field(typeof(UnitPart), "criticalPart");
        private static readonly FieldInfo DetachedFromUnitField =
            AccessTools.Field(typeof(UnitPart), "detachedFromUnit");
        private static readonly FieldInfo AttachInfoField =
            AccessTools.Field(typeof(UnitPart), "attachInfo");
        private static readonly FieldInfo TurbojetOperableField =
            AccessTools.Field(typeof(Turbojet), "operable");
        private static readonly FieldInfo TurbojetConditionField =
            AccessTools.Field(typeof(Turbojet), "condition");
        private static readonly FieldInfo TurbineOperableField =
            AccessTools.Field(typeof(TurbineEngine), "operable");
        private static readonly FieldInfo TurbofanOperableField =
            AccessTools.Field(typeof(Turbofan), "operable");
        private static readonly FieldInfo DuctedFanInoperableField =
            AccessTools.Field(typeof(DuctedFan), "inoperable");
        private static readonly FieldInfo FuelOnFireField =
            AccessTools.Field(typeof(FuelTank), "onFire");
        private static readonly FieldInfo FuelRupturedField =
            AccessTools.Field(typeof(FuelTank), "ruptured");
        private static readonly FieldInfo FuelLeakingField =
            AccessTools.Field(typeof(FuelTank), "isLeaking");
        private static readonly FieldInfo FuelFireParticlesField =
            AccessTools.Field(typeof(FuelTank), "fireParticles");
        private static readonly FieldInfo FuelFireEffectSpawnField =
            AccessTools.Field(typeof(FuelTank), "fireEffectSpawn");
        private static readonly FieldInfo FuelFireballSpawnField =
            AccessTools.Field(typeof(FuelTank), "fireballSpawn");
        private static readonly FieldInfo TurbojetNozzlesField =
            AccessTools.Field(typeof(Turbojet), "nozzles");
        private static readonly FieldInfo TurbojetHasFuelField =
            AccessTools.Field(typeof(Turbojet), "hasFuel");
        private static readonly FieldInfo TurbineConditionField =
            AccessTools.Field(typeof(TurbineEngine), "condition");
        private static readonly FieldInfo TurbineHasFuelField =
            AccessTools.Field(typeof(TurbineEngine), "hasFuel");
        private static readonly FieldInfo TurbineStartedField =
            AccessTools.Field(typeof(TurbineEngine), "startedAmount");
        private static readonly FieldInfo TurbofanConditionField =
            AccessTools.Field(typeof(Turbofan), "condition");
        private static readonly FieldInfo TurbofanHasFuelField =
            AccessTools.Field(typeof(Turbofan), "hasFuel");
        private static readonly FieldInfo TurbofanHitPointsField =
            AccessTools.Field(typeof(Turbofan), "hitPoints");
        private static readonly FieldInfo TurbofanNozzlesField =
            AccessTools.Field(typeof(Turbofan), "nozzles");
        private static readonly FieldInfo PropFanConditionField =
            AccessTools.Field(typeof(PropFan), "condition");
        private static readonly FieldInfo PropFanDamageReportedField =
            AccessTools.Field(typeof(PropFan), "damageReported");
        private static readonly FieldInfo PropOperableField =
            AccessTools.Field(typeof(ConstantSpeedProp), "operable");
        private static readonly FieldInfo PropStrikeField =
            AccessTools.Field(typeof(ConstantSpeedProp), "propStrike");
        private static readonly FieldInfo PropHubFrictionField =
            AccessTools.Field(typeof(ConstantSpeedProp), "hubFriction");
        private static readonly FieldInfo RotorConditionField =
            AccessTools.Field(typeof(RotorShaft), "condition");
        private static readonly FieldInfo RotorDetachedField =
            AccessTools.Field(typeof(RotorShaft), "detached");
        private static readonly FieldInfo NozzleThrustPropField =
            AccessTools.Field(typeof(JetNozzle), "thrustProportion");
        private static readonly Dictionary<int, float> NozzleThrustProp =
            new Dictionary<int, float>();
        private static readonly FieldInfo HostedParticlesField =
            AccessTools.Field(typeof(UnitPart), "hostedParticles");
        private static readonly FieldInfo DpFireDamageField =
            AccessTools.Field(typeof(DamageParticles), "fireDamage");
        private static readonly FieldInfo DpFireLifetimeField =
            AccessTools.Field(typeof(DamageParticles), "fireLifetime");
        private static readonly FieldInfo DpFireLightField =
            AccessTools.Field(typeof(DamageParticles), "fireLight");
        private static readonly FieldInfo FragmentedField =
            AccessTools.Field(typeof(UnitPart), "fragmented");
        private static readonly FieldInfo DisintegrateObjectsField =
            AccessTools.Field(typeof(UnitPart), "disintegrateObjects");
        private static readonly FieldInfo WingEffectivenessField =
            AccessTools.Field(typeof(AeroPart), "wingEffectiveness");

        private static readonly Dictionary<int, float> MaxHitPoints =
            new Dictionary<int, float>();

        private static Vector2 _scroll;
        private static float _statusUntil;
        private static string _status = string.Empty;
        private static float _listCacheUntil;
        private static int _listCacheAcId;
        private static readonly List<DamagedEntry> _cached = new List<DamagedEntry>(32);
        private static bool _continuous;
        private static float _nextContinuous;
        private static int _blockFireAcId;
        private static bool _blockFire;
        private static int _rtAcId;
        private static float _rtUntil;
        private static readonly List<UnitPart> _rtParts = new List<UnitPart>(128);
        private static readonly List<Turbojet> _rtJets = new List<Turbojet>(8);
        private static readonly List<TurbineEngine> _rtTurbines = new List<TurbineEngine>(8);
        private static readonly List<Turbofan> _rtFans = new List<Turbofan>(4);
        private static readonly List<DuctedFan> _rtDucted = new List<DuctedFan>(4);
        private static readonly List<PropFan> _rtPropFans = new List<PropFan>(4);
        private static readonly List<ConstantSpeedProp> _rtProps = new List<ConstantSpeedProp>(4);
        private static readonly List<RotorShaft> _rtRotors = new List<RotorShaft>(4);
        private static readonly List<FuelTank> _rtTanks = new List<FuelTank>(16);
        private const float RtCacheSec = 4f;
        private const float ContBusySec = 0.8f;
        private const float ContIdleSec = 2f;

        private static GUIStyle _rowStyle;
        private static GUIStyle _btnStyle;
        private static GUIStyle _hintStyle;
        private static GUIStyle _toggleStyle;
        private static FieldInfo _attachParentField;
        private static FieldInfo _attachDetachedParentField;
        private static FieldInfo _attachLocalPosField;
        private static FieldInfo _attachLocalRotField;

        private enum DamageKind
        {
            Detached = 0,
            HitPoints = 1,
            Engine = 2,
            Fuel = 3,
            Critical = 4,
            Wear = 5,
            Pilot = 6
        }

        private struct DamagedEntry
        {
            public string Label;
            public string Detail;
            public DamageKind Kind;
            public UnitPart Part;
            public Component Extra; // Turbojet / FuelTank / etc.
            public int InstanceId;
            public bool Unrepairable;
        }

        internal static void ResetUi()
        {
            _scroll = Vector2.zero;
            _status = string.Empty;
            _statusUntil = 0f;
            _listCacheUntil = 0f;
            _listCacheAcId = 0;
            _cached.Clear();
            _rtUntil = 0f;
            _rtAcId = 0;
        }

        internal static int RepairAllFromOutside(Aircraft ac)
        {
            return RepairAll(ac, true);
        }

        internal static int RepairFewFromOutside(Aircraft ac, int max)
        {
            if (ac == null || max <= 0)
                return 0;
            InvalidateCache();
            List<DamagedEntry> list = Scan(ac);
            List<DamagedEntry> copy = new List<DamagedEntry>(list);
            int n = 0;
            int cap = copy.Count;
            if (cap > max)
                cap = max;
            for (int i = 0; i < cap; i++)
                n += RepairOne(ac, copy[i], true);
            return n;
        }

        internal static int ExtinguishFromOutside(Aircraft ac)
        {
            return ExtinguishFires(ac);
        }

        internal static int HealEnginesFromOutside(Aircraft ac)
        {
            if (ac == null)
                return 0;
            int n = 0;
            try
            {
                Turbojet[] jets = ac.GetComponentsInChildren<Turbojet>(true);
                if (jets != null)
                {
                    for (int i = 0; i < jets.Length; i++)
                    {
                        if (jets[i] == null)
                            continue;
                        HealEngine(jets[i]);
                        n++;
                    }
                }
            }
            catch { }
            try
            {
                TurbineEngine[] tes = ac.GetComponentsInChildren<TurbineEngine>(true);
                if (tes != null)
                {
                    for (int i = 0; i < tes.Length; i++)
                    {
                        if (tes[i] == null)
                            continue;
                        HealEngine(tes[i]);
                        n++;
                    }
                }
            }
            catch { }
            try
            {
                Turbofan[] fans = ac.GetComponentsInChildren<Turbofan>(true);
                if (fans != null)
                {
                    for (int i = 0; i < fans.Length; i++)
                    {
                        if (fans[i] == null)
                            continue;
                        HealEngine(fans[i]);
                        n++;
                    }
                }
            }
            catch { }
            try
            {
                DuctedFan[] ducted = ac.GetComponentsInChildren<DuctedFan>(true);
                if (ducted != null)
                {
                    for (int i = 0; i < ducted.Length; i++)
                    {
                        if (ducted[i] == null)
                            continue;
                        HealEngine(ducted[i]);
                        n++;
                    }
                }
            }
            catch { }
            n += HealPropAndRotorEngines(ac);
            return n;
        }

        internal static void DrawPanel(Rect box, Aircraft ac, GUIStyle titleStyle, GUIStyle labelStyle)
        {
            EnsureStyles();
            bool zh = UiZh();

            GUI.Label(AerialSupportLayoutService.RepairTitleRect(box),
                zh ? "维修组件 — 损坏 / 脱落 / 引擎 / 油箱 / 飞行员 / 灭火" : "Repair parts — damage / detached / eng / fuel / pilot / fire",
                labelStyle);

            List<DamagedEntry> list = Scan(ac);
            Rect view = AerialSupportLayoutService.RepairListView(box);
            Color prev = GUI.color;
            GUI.color = new Color(0.04f, 0.06f, 0.08f, 0.85f);
            GUI.DrawTexture(view, Texture2D.whiteTexture);
            GUI.color = Color.white;

            if (list.Count == 0)
            {
                GUI.Label(new Rect(view.x + 10f, view.y + 12f, view.width - 20f, 40f),
                    zh ? "无损坏组件" : "No damaged components", labelStyle);
            }
            else
            {
                float rowH = AerialSupportLayoutService.RepairRowH;
                float gap = AerialSupportLayoutService.RepairRowGap;
                float contentH = list.Count * (rowH + gap) + 8f;
                _scroll = GUI.BeginScrollView(view,
                    _scroll,
                    new Rect(0f, 0f, view.width - 22f, contentH));
                float ry = 4f;
                for (int i = 0; i < list.Count; i++)
                {
                    DamagedEntry e = list[i];
                    Rect row = new Rect(4f, ry, view.width - 28f, rowH);
                    GUI.color = KindColor(e.Kind);
                    GUI.DrawTexture(new Rect(row.x, row.y, 4f, row.height), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                    string line = e.Label;
                    if (!string.IsNullOrEmpty(e.Detail))
                        line = line + "  ·  " + e.Detail;
                    GUI.Label(new Rect(row.x + 10f, row.y + 2f, row.width - 88f, row.height - 4f),
                        line, _rowStyle);
                    if (!e.Unrepairable
                        && GUI.Button(new Rect(row.xMax - 78f, row.y + 4f, 74f, row.height - 8f),
                            zh ? "维修" : "REPAIR", _btnStyle))
                    {
                        int n = RepairOne(ac, e, false);
                        InvalidateCache();
                        Flash(zh
                            ? ("已维修 " + n + " 项: " + e.Label)
                            : ("Repaired " + n + ": " + e.Label));
                    }
                    ry += rowH + gap;
                }
                GUI.EndScrollView();
            }

            int fires = CountFires(ac);
            string extLabel = zh
                ? (fires > 0 ? ("灭火 (" + fires + ")") : "灭火")
                : (fires > 0 ? ("EXTINGUISH (" + fires + ")") : "EXTINGUISH");
            if (GUI.Button(AerialSupportLayoutService.RepairExtinguishButton(box), extLabel, _btnStyle))
            {
                int n = ExtinguishFires(ac);
                InvalidateCache();
                Flash(zh
                    ? (n > 0 ? ("已灭火 " + n + " 处") : "未发现火情")
                    : (n > 0 ? ("Extinguished " + n) : "No fire"));
            }

            int repairable = 0;
            for (int i = 0; i < list.Count; i++)
            {
                if (!list[i].Unrepairable)
                    repairable++;
            }
            string allLabel = zh
                ? ("全部维修 (" + repairable + ")")
                : ("REPAIR ALL (" + repairable + ")");
            if (GUI.Button(AerialSupportLayoutService.RepairAllButton(box), allLabel, _btnStyle))
            {
                int n = RepairAll(ac, false);
                InvalidateCache();
                Flash(zh ? ("已维修 " + n + " 项") : ("Repaired " + n + " item(s)"));
            }

            if (GUI.Button(AerialSupportLayoutService.RepairRestoreButton(box),
                zh ? "整机复原（接回脱落件）" : "RESTORE AIRFRAME (reattach)", _btnStyle))
            {
                int n = RestoreAirframe(ac);
                InvalidateCache();
                Flash(zh
                    ? (n > 0 ? ("已接回 " + n + " 件，并修好剩余损伤") : "没有可接回的脱落件（残骸已销毁或父件不在）")
                    : (n > 0 ? ("Reattached " + n + " part(s)") : "Nothing left to reattach"));
            }

            bool wantCont = GUI.Toggle(AerialSupportLayoutService.RepairContinuousRect(box),
                _continuous,
                zh ? "持续维修（关菜单后仍维修 / 灭火）" : "Continuous repair (keeps healing / extinguishing)",
                _toggleStyle);
            if (wantCont != _continuous)
            {
                _continuous = wantCont;
                _nextContinuous = 0f;
                if (!_continuous)
                    _blockFire = false;
                Flash(zh
                    ? (_continuous ? "持续维修已开启" : "持续维修已关闭")
                    : (_continuous ? "Continuous repair ON" : "Continuous repair OFF"));
            }

            if (!string.IsNullOrEmpty(_status) && Time.unscaledTime < _statusUntil)
            {
                GUI.Label(AerialSupportLayoutService.RepairStatusLine(box),
                    _status, _hintStyle);
            }

            GUI.color = prev;
        }

        internal static void Tick()
        {
            if (!_continuous)
                return;
            Aircraft ac = null;
            try
            {
                Aircraft local;
                if (GameManager.GetLocalAircraft(out local) && local != null)
                    ac = local;
            }
            catch { }
            if (ac == null)
                return;
            try
            {
                if (ac.disabled)
                    return;
            }
            catch { return; }
            if (Time.unscaledTime < _nextContinuous)
                return;
            int n = RepairContinuous(ac);
            _nextContinuous = Time.unscaledTime + (n > 0 ? ContBusySec : ContIdleSec);
        }

        /// <summary>
        /// Continuous path: only touch damaged / on-fire items. Full RepairAll snaps every
        /// AeroPart (transform write) and walks the hierarchy several times — that hitch is
        /// why the F10 toggle used to tank FPS after the menu closed.
        /// </summary>
        private static int RepairContinuous(Aircraft ac)
        {
            if (ac == null)
                return 0;
            EnsureRuntimeCache(ac);
            int n = 0;
            bool anyFire = false;
            int acId = 0;
            try { acId = ac.GetInstanceID(); }
            catch { }
            if (acId != 0)
            {
                _blockFireAcId = acId;
                _blockFire = true;
            }

            for (int i = 0; i < _rtJets.Count; i++)
            {
                Turbojet tj = _rtJets[i];
                if (tj == null)
                    continue;
                bool lit = false;
                try { lit = tj.engineFire; }
                catch { }
                if (lit || EngineNeedsHeal(tj))
                {
                    HealEngine(tj);
                    n++;
                    if (lit)
                        anyFire = true;
                }
            }
            for (int i = 0; i < _rtTurbines.Count; i++)
            {
                if (_rtTurbines[i] != null && EngineNeedsHeal(_rtTurbines[i]))
                {
                    HealEngine(_rtTurbines[i]);
                    n++;
                }
            }
            for (int i = 0; i < _rtFans.Count; i++)
            {
                if (_rtFans[i] != null && EngineNeedsHeal(_rtFans[i]))
                {
                    HealEngine(_rtFans[i]);
                    n++;
                }
            }
            for (int i = 0; i < _rtDucted.Count; i++)
            {
                if (_rtDucted[i] != null && EngineNeedsHeal(_rtDucted[i]))
                {
                    HealEngine(_rtDucted[i]);
                    n++;
                }
            }
            for (int i = 0; i < _rtPropFans.Count; i++)
            {
                if (_rtPropFans[i] != null && EngineNeedsHeal(_rtPropFans[i]))
                {
                    HealEngine(_rtPropFans[i]);
                    n++;
                }
            }
            for (int i = 0; i < _rtProps.Count; i++)
            {
                if (_rtProps[i] != null && EngineNeedsHeal(_rtProps[i]))
                {
                    HealEngine(_rtProps[i]);
                    n++;
                }
            }
            for (int i = 0; i < _rtRotors.Count; i++)
            {
                if (_rtRotors[i] != null && EngineNeedsHeal(_rtRotors[i]))
                {
                    HealEngine(_rtRotors[i]);
                    n++;
                }
            }
            for (int i = 0; i < _rtTanks.Count; i++)
            {
                FuelTank tank = _rtTanks[i];
                if (tank == null)
                    continue;
                bool onFire = ReadBool(tank, FuelOnFireField, false);
                bool ruptured = ReadBool(tank, FuelRupturedField, false);
                bool leaking = ReadBool(tank, FuelLeakingField, false);
                if (!onFire && !ruptured && !leaking)
                    continue;
                HealFuel(tank);
                n++;
                if (onFire)
                    anyFire = true;
            }
            for (int i = 0; i < _rtParts.Count; i++)
            {
                UnitPart part = _rtParts[i];
                if (part == null || !PartNeedsHeal(part))
                    continue;
                HealPart(part);
                n++;
            }

            if (anyFire)
                ExtinguishParticles(ac);
            return n;
        }

        private static void EnsureRuntimeCache(Aircraft ac)
        {
            int id = 0;
            try
            {
                if (ac != null)
                    id = ac.GetInstanceID();
            }
            catch { }
            if (id != 0 && id == _rtAcId && Time.unscaledTime < _rtUntil)
                return;

            _rtAcId = id;
            _rtUntil = Time.unscaledTime + RtCacheSec;
            _rtParts.Clear();
            _rtJets.Clear();
            _rtTurbines.Clear();
            _rtFans.Clear();
            _rtDucted.Clear();
            _rtPropFans.Clear();
            _rtProps.Clear();
            _rtRotors.Clear();
            _rtTanks.Clear();
            if (ac == null)
                return;

            try
            {
                List<UnitPart> parts = ac.GetAllParts();
                if (parts != null)
                {
                    for (int i = 0; i < parts.Count; i++)
                    {
                        if (parts[i] != null)
                            _rtParts.Add(parts[i]);
                    }
                }
            }
            catch { }

            try
            {
                if (ac.engines != null)
                {
                    for (int i = 0; i < ac.engines.Count; i++)
                    {
                        Component eng = ac.engines[i] as Component;
                        AddCachedEngine(eng);
                    }
                }
            }
            catch { }

            try
            {
                FuelTank[] tanks = ac.GetComponentsInChildren<FuelTank>(true);
                if (tanks != null)
                {
                    for (int i = 0; i < tanks.Length; i++)
                    {
                        if (tanks[i] != null)
                            _rtTanks.Add(tanks[i]);
                    }
                }
            }
            catch { }

            if (_rtJets.Count == 0)
            {
                try
                {
                    Turbojet[] jets = ac.GetComponentsInChildren<Turbojet>(true);
                    if (jets != null)
                    {
                        for (int i = 0; i < jets.Length; i++)
                        {
                            if (jets[i] != null)
                                _rtJets.Add(jets[i]);
                        }
                    }
                }
                catch { }
            }
        }

        private static void AddCachedEngine(Component eng)
        {
            if (eng == null)
                return;
            Turbojet tj = eng as Turbojet;
            if (tj != null)
            {
                _rtJets.Add(tj);
                return;
            }
            TurbineEngine te = eng as TurbineEngine;
            if (te != null)
            {
                _rtTurbines.Add(te);
                return;
            }
            Turbofan tf = eng as Turbofan;
            if (tf != null)
            {
                _rtFans.Add(tf);
                return;
            }
            DuctedFan df = eng as DuctedFan;
            if (df != null)
            {
                _rtDucted.Add(df);
                return;
            }
            PropFan pf = eng as PropFan;
            if (pf != null)
            {
                _rtPropFans.Add(pf);
                return;
            }
            ConstantSpeedProp prop = eng as ConstantSpeedProp;
            if (prop != null)
            {
                _rtProps.Add(prop);
                return;
            }
            RotorShaft rotor = eng as RotorShaft;
            if (rotor != null)
                _rtRotors.Add(rotor);
        }

        private static bool IsPartDetached(UnitPart part)
        {
            if (part == null)
                return false;
            try
            {
                if (part.IsDetached())
                    return true;
            }
            catch { }
            return ReadBool(part, DetachedFromUnitField, false);
        }

        /// <summary>
        /// Fallen debris has its own rigidbody and is unparented — vanilla has no reattach.
        /// Only a stuck detached flag on a still-mounted part (same RB, still under the airframe,
        /// parent piece still attached) can be cleared.
        /// </summary>
        private static bool CanRestoreDetached(UnitPart part)
        {
            if (part == null || !IsPartDetached(part))
                return false;
            try
            {
                Unit unit = part.parentUnit;
                if (unit == null || unit.rb == null)
                    return false;
                if (part.rb != null && part.rb != unit.rb)
                    return false;
                Transform pt = part.transform;
                Transform ut = unit.transform;
                if (pt == null || ut == null || !pt.IsChildOf(ut))
                    return false;
                if (AttachInfoField == null)
                    return false;
                object info = AttachInfoField.GetValue(part);
                if (info == null)
                    return false;
                if (_attachParentField == null)
                    _attachParentField = AccessTools.Field(info.GetType(), "parentPart");
                if (_attachParentField == null)
                    return false;
                UnitPart parentPart = _attachParentField.GetValue(info) as UnitPart;
                if (parentPart == null || IsPartDetached(parentPart))
                    return false;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool IsUnrestorableDetached(UnitPart part)
        {
            return IsPartDetached(part) && !CanRestoreDetached(part);
        }

        private static void EnsureAttachFields(object info)
        {
            if (info == null)
                return;
            Type t = info.GetType();
            if (_attachParentField == null)
                _attachParentField = AccessTools.Field(t, "parentPart");
            if (_attachDetachedParentField == null)
                _attachDetachedParentField = AccessTools.Field(t, "detachedFromParentPart");
            if (_attachLocalPosField == null)
                _attachLocalPosField = AccessTools.Field(t, "localPosition");
            if (_attachLocalRotField == null)
                _attachLocalRotField = AccessTools.Field(t, "localRotation");
        }

        /// <summary>
        /// Explicit restore: clear vanilla detach flags and MergeWithParent (or reparent).
        /// Continuous / Repair All still skip fallen debris.
        /// </summary>
        private static int RestoreAirframe(Aircraft ac)
        {
            if (ac == null)
                return 0;
            List<UnitPart> parts = null;
            try { parts = ac.GetAllParts(); }
            catch { }
            if (parts == null)
                return 0;

            int n = 0;
            bool progress = true;
            int guard = 0;
            while (progress && guard < 24)
            {
                progress = false;
                guard++;
                for (int i = 0; i < parts.Count; i++)
                {
                    UnitPart part = parts[i];
                    if (part == null || !IsPartDetached(part))
                        continue;
                    if (!TryReattachPart(part))
                        continue;
                    n++;
                    progress = true;
                }
            }

            RepairAll(ac, true);
            try
            {
                if (ac.rb != null)
                {
                    ac.rb.ResetCenterOfMass();
                    ac.rb.ResetInertiaTensor();
                }
            }
            catch { }
            try
            {
                if (ac.disabled)
                    ac.Networkdisabled = false;
            }
            catch { }
            return n;
        }

        private static bool TryReattachPart(UnitPart part)
        {
            if (part == null || AttachInfoField == null)
                return false;
            object info = null;
            try { info = AttachInfoField.GetValue(part); }
            catch { return false; }
            if (info == null)
                return false;
            EnsureAttachFields(info);
            if (_attachParentField == null || _attachDetachedParentField == null)
                return false;

            UnitPart parentPart = null;
            try { parentPart = _attachParentField.GetValue(info) as UnitPart; }
            catch { return false; }
            if (parentPart == null)
                return false;
            try
            {
                if (parentPart.gameObject == null)
                    return false;
            }
            catch { return false; }
            if (IsPartDetached(parentPart))
                return false;

            try { _attachDetachedParentField.SetValue(info, false); }
            catch { return false; }
            WriteBool(part, DetachedFromUnitField, false);

            AeroPart aero = part as AeroPart;
            if (aero != null)
            {
                try { aero.MergeWithParent(); }
                catch { ManualReparent(part, info, parentPart); }
                if (WingEffectivenessField != null)
                {
                    try { WingEffectivenessField.SetValue(aero, 1f); }
                    catch { }
                }
            }
            else
                ManualReparent(part, info, parentPart);

            Unfragment(part);
            try { part.hitPoints = 100f; }
            catch { }
            try
            {
                if (part.gameObject != null && !part.gameObject.activeSelf)
                    part.gameObject.SetActive(true);
            }
            catch { }
            return !IsPartDetached(part);
        }

        private static void ManualReparent(UnitPart part, object info, UnitPart parentPart)
        {
            if (part == null || parentPart == null)
                return;
            try
            {
                Transform xf = part.transform;
                if (xf == null || parentPart.transform == null)
                    return;
                Vector3 lp = Vector3.zero;
                Quaternion lr = Quaternion.identity;
                if (_attachLocalPosField != null)
                    lp = (Vector3)_attachLocalPosField.GetValue(info);
                if (_attachLocalRotField != null)
                    lr = (Quaternion)_attachLocalRotField.GetValue(info);
                xf.SetParent(parentPart.transform, false);
                xf.localPosition = lp;
                xf.localRotation = lr;
            }
            catch { }

            try
            {
                Unit unit = part.parentUnit;
                if (unit != null && unit.rb != null && part.rb != null && part.rb != unit.rb)
                {
                    UnityEngine.Object.Destroy(part.rb);
                    part.rb = unit.rb;
                }
            }
            catch { }
        }

        private static void Unfragment(UnitPart part)
        {
            if (part == null)
                return;
            WriteBool(part, FragmentedField, false);
            if (DisintegrateObjectsField == null)
                return;
            GameObject[] objs = null;
            try { objs = DisintegrateObjectsField.GetValue(part) as GameObject[]; }
            catch { return; }
            if (objs == null)
                return;
            int layer = 0;
            try
            {
                if (part.parentUnit != null && part.parentUnit.gameObject != null)
                    layer = part.parentUnit.gameObject.layer;
            }
            catch { }
            for (int i = 0; i < objs.Length; i++)
            {
                GameObject go = objs[i];
                if (go == null)
                    continue;
                try { go.layer = layer; }
                catch { }
                try
                {
                    Renderer r = go.GetComponent<Renderer>();
                    if (r != null)
                        r.enabled = true;
                }
                catch { }
            }
        }

        private static UnitPart HostPartOf(Component c)
        {
            if (c == null)
                return null;
            try
            {
                UnitPart p = c.GetComponent<UnitPart>();
                if (p == null)
                    p = c.GetComponentInParent<UnitPart>();
                return p;
            }
            catch
            {
                return null;
            }
        }

        private static bool PartNeedsHeal(UnitPart part)
        {
            if (part == null)
                return false;
            if (IsUnrestorableDetached(part))
                return false;
            if (IsPartDetached(part))
                return true;
            float hp = 0f;
            try { hp = part.hitPoints; }
            catch { }
            int id = 0;
            try { id = part.GetInstanceID(); }
            catch { }
            if (id != 0)
                NoteMaxHp(id, hp);
            float maxHp;
            if (id != 0 && MaxHitPoints.TryGetValue(id, out maxHp) && maxHp > 1f && hp < maxHp * 0.995f)
                return true;
            return hp <= 0.01f;
        }

        private static bool EngineNeedsHeal(Component eng)
        {
            if (eng == null)
                return false;
            Turbojet tj = eng as Turbojet;
            if (tj != null)
            {
                try
                {
                    if (tj.engineFire)
                        return true;
                    float df = tj.damageFactor;
                    if (df > 0.001f && df < 0.995f)
                        return true;
                }
                catch { }
                if (!ReadBool(tj, TurbojetOperableField, true))
                    return true;
                return NozzlesNeedRestore(tj);
            }
            TurbineEngine te = eng as TurbineEngine;
            if (te != null)
            {
                try
                {
                    if (!te.IsOperable())
                        return true;
                }
                catch
                {
                    return !ReadBool(te, TurbineOperableField, true);
                }
                return false;
            }
            Turbofan tf = eng as Turbofan;
            if (tf != null)
            {
                if (!ReadBool(tf, TurbofanOperableField, true))
                    return true;
                if (ReadFloat(tf, TurbofanConditionField, 1f) < 0.995f)
                    return true;
                return NozzlesNeedRestore(tf);
            }
            DuctedFan dfan = eng as DuctedFan;
            if (dfan != null)
                return ReadBool(dfan, DuctedFanInoperableField, false);
            PropFan pf = eng as PropFan;
            if (pf != null)
                return ReadFloat(pf, PropFanConditionField, 1f) < 0.995f;
            ConstantSpeedProp prop = eng as ConstantSpeedProp;
            if (prop != null)
                return !ReadBool(prop, PropOperableField, true)
                    || ReadBool(prop, PropStrikeField, false);
            RotorShaft rotor = eng as RotorShaft;
            if (rotor != null)
            {
                if (ReadBool(rotor, RotorDetachedField, false))
                    return true;
                return ReadFloat(rotor, RotorConditionField, 1f) < 0.995f;
            }
            return NozzlesNeedRestore(eng);
        }

        internal static bool ShouldBlockFire(UnitPart part)
        {
            if (!_blockFire || part == null)
                return false;
            try
            {
                Unit u = part.parentUnit;
                if (u == null)
                    return false;
                return u.GetInstanceID() == _blockFireAcId;
            }
            catch { return false; }
        }

        private static Color KindColor(DamageKind k)
        {
            return ComponentRepairMathService.KindColor((int)k);
        }

        private static List<DamagedEntry> Scan(Aircraft ac)
        {
            int id = 0;
            try
            {
                if (ac != null)
                    id = ac.GetInstanceID();
            }
            catch { }

            if (id != 0 && id == _listCacheAcId && Time.unscaledTime < _listCacheUntil)
            {
                StripWearRows(_cached);
                AppendWearRows(_cached);
                return _cached;
            }

            _cached.Clear();
            _listCacheAcId = id;
            _listCacheUntil = Time.unscaledTime + 0.35f;
            if (ac == null)
                return _cached;

            HashSet<int> seen = new HashSet<int>();

            try
            {
                List<UnitPart> parts = null;
                try { parts = ac.GetAllParts(); }
                catch { }
                if (parts != null)
                {
                    for (int i = 0; i < parts.Count; i++)
                        ConsiderPart(parts[i], seen, _cached);
                }
            }
            catch { }

            try
            {
                UnitPart[] all = ac.GetComponentsInChildren<UnitPart>(true);
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                        ConsiderPart(all[i], seen, _cached);
                }
            }
            catch { }

            try
            {
                if (ac.engines != null)
                {
                    for (int i = 0; i < ac.engines.Count; i++)
                        ConsiderEngine(ac.engines[i] as Component, seen, _cached);
                }
            }
            catch { }

            try
            {
                ScanEngineArray(ac.GetComponentsInChildren<Turbojet>(true), seen, _cached);
                ScanEngineArray(ac.GetComponentsInChildren<TurbineEngine>(true), seen, _cached);
                ScanEngineArray(ac.GetComponentsInChildren<Turbofan>(true), seen, _cached);
                ScanEngineArray(ac.GetComponentsInChildren<DuctedFan>(true), seen, _cached);
                ScanEngineArray(ac.GetComponentsInChildren<PropFan>(true), seen, _cached);
                ScanEngineArray(ac.GetComponentsInChildren<ConstantSpeedProp>(true), seen, _cached);
                ScanEngineArray(ac.GetComponentsInChildren<RotorShaft>(true), seen, _cached);
            }
            catch { }

            try
            {
                FuelTank[] tanks = ac.GetComponentsInChildren<FuelTank>(true);
                if (tanks != null)
                {
                    for (int i = 0; i < tanks.Length; i++)
                        ConsiderFuel(tanks[i], seen, _cached);
                }
            }
            catch { }

            AppendWearRows(_cached);
            return _cached;
        }

        private static void StripWearRows(List<DamagedEntry> dst)
        {
            if (dst == null)
                return;
            for (int i = dst.Count - 1; i >= 0; i--)
            {
                if (dst[i].Kind == DamageKind.Wear || dst[i].Kind == DamageKind.Pilot)
                    dst.RemoveAt(i);
            }
        }

        private static void AppendWearRows(List<DamagedEntry> dst)
        {
            if (dst == null)
                return;
            List<string> labels = new List<string>(12);
            List<string> details = new List<string>(12);
            List<int> ids = new List<int>(12);
            AirframeWearService.AppendRepairRows(labels, details, ids);
            for (int i = 0; i < labels.Count; i++)
            {
                DamagedEntry e = new DamagedEntry();
                e.Label = labels[i];
                e.Detail = details[i];
                e.InstanceId = ids[i];
                e.Kind = ids[i] < 0 ? DamageKind.Pilot : DamageKind.Wear;
                e.Unrepairable = false;
                dst.Add(e);
            }
        }

        private static void ConsiderPart(UnitPart part, HashSet<int> seen, List<DamagedEntry> dst)
        {
            if (part == null)
                return;
            int id;
            try { id = part.GetInstanceID(); }
            catch { return; }
            if (id == 0 || seen.Contains(id))
                return;

            bool detached = IsPartDetached(part);
            bool unrestorable = detached && !CanRestoreDetached(part);

            float hp = 0f;
            try { hp = part.hitPoints; }
            catch { }
            NoteMaxHp(id, hp);

            float maxHp;
            bool hpDamaged = false;
            if (MaxHitPoints.TryGetValue(id, out maxHp) && maxHp > 1f && hp < maxHp * 0.995f)
                hpDamaged = true;
            if (hp <= 0.01f)
                hpDamaged = true;

            bool critical = ReadBool(part, CriticalPartField, false);

            if (!detached && !hpDamaged)
                return;

            seen.Add(id);
            DamagedEntry e = new DamagedEntry();
            e.Part = part;
            e.InstanceId = id;
            e.Label = PartName(part);
            if (detached)
            {
                e.Kind = DamageKind.Detached;
                e.Unrepairable = unrestorable;
                e.Detail = unrestorable
                    ? (UiZh() ? "已脱落 · 无法修复" : "DETACHED / UNREPAIRABLE")
                    : (UiZh() ? "已脱落" : "DETACHED");
            }
            else if (critical && hpDamaged)
            {
                e.Kind = DamageKind.Critical;
                e.Detail = HpDetail(hp, maxHp);
            }
            else
            {
                e.Kind = DamageKind.HitPoints;
                e.Detail = HpDetail(hp, maxHp);
            }
            dst.Add(e);
        }

        private static void ConsiderEngine(Component eng, HashSet<int> seen, List<DamagedEntry> dst)
        {
            if (eng == null || IsUnrestorableDetached(HostPartOf(eng)))
                return;
            int id;
            try { id = eng.GetInstanceID(); }
            catch { return; }
            if (id == 0 || seen.Contains(id))
                return;

            if (!EngineNeedsHeal(eng))
                return;
            string detail = EngineHealDetail(eng);

            seen.Add(id);
            DamagedEntry e = new DamagedEntry();
            e.Extra = eng;
            e.InstanceId = id;
            e.Kind = DamageKind.Engine;
            e.Label = eng.GetType().Name + " " + SafeName(eng.gameObject);
            e.Detail = detail;
            try
            {
                UnitPart host = eng.GetComponent<UnitPart>();
                if (host == null)
                    host = eng.GetComponentInParent<UnitPart>();
                e.Part = host;
            }
            catch { }
            dst.Add(e);
        }

        private static void ConsiderFuel(FuelTank tank, HashSet<int> seen, List<DamagedEntry> dst)
        {
            if (tank == null || IsUnrestorableDetached(HostPartOf(tank)))
                return;
            int id;
            try { id = tank.GetInstanceID(); }
            catch { return; }
            if (id == 0 || seen.Contains(id))
                return;

            bool onFire = ReadBool(tank, FuelOnFireField, false);
            bool ruptured = ReadBool(tank, FuelRupturedField, false);
            bool leaking = ReadBool(tank, FuelLeakingField, false);
            if (!onFire && !ruptured && !leaking)
                return;

            seen.Add(id);
            DamagedEntry e = new DamagedEntry();
            e.Extra = tank;
            e.InstanceId = id;
            e.Kind = DamageKind.Fuel;
            e.Label = "FuelTank " + SafeName(tank.gameObject);
            string d = string.Empty;
            if (onFire)
                d = "FIRE";
            if (ruptured)
                d = d.Length > 0 ? d + " RUPTURED" : "RUPTURED";
            if (leaking)
                d = d.Length > 0 ? d + " LEAK" : "LEAK";
            e.Detail = d;
            try
            {
                UnitPart host = tank.GetComponent<UnitPart>();
                if (host == null)
                    host = tank.GetComponentInParent<UnitPart>();
                e.Part = host;
            }
            catch { }
            dst.Add(e);
        }

        private static int RepairAll(Aircraft ac, bool silent)
        {
            if (ac == null)
                return 0;
            ExtinguishFires(ac);
            InvalidateCache();
            List<DamagedEntry> list = Scan(ac);
            // Copy — Repair mutates cache
            List<DamagedEntry> copy = new List<DamagedEntry>(list);
            int n = 0;
            for (int i = 0; i < copy.Count; i++)
            {
                if (copy[i].Unrepairable)
                    continue;
                n += RepairOne(ac, copy[i], silent);
            }

            // Sweep only damaged / detached parts — AeroPart.Repair snaps every surface.
            try
            {
                UnitPart[] all = ac.GetComponentsInChildren<UnitPart>(true);
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        if (all[i] != null && PartNeedsHeal(all[i]))
                            HealPart(all[i]);
                    }
                }
            }
            catch { }

            try
            {
                Turbojet[] jets = ac.GetComponentsInChildren<Turbojet>(true);
                if (jets != null)
                {
                    for (int i = 0; i < jets.Length; i++)
                        HealEngine(jets[i]);
                }
            }
            catch { }

            try
            {
                TurbineEngine[] tes = ac.GetComponentsInChildren<TurbineEngine>(true);
                if (tes != null)
                {
                    for (int i = 0; i < tes.Length; i++)
                        HealEngine(tes[i]);
                }
            }
            catch { }

            try
            {
                Turbofan[] fans = ac.GetComponentsInChildren<Turbofan>(true);
                if (fans != null)
                {
                    for (int i = 0; i < fans.Length; i++)
                        HealEngine(fans[i]);
                }
            }
            catch { }

            try
            {
                DuctedFan[] ducted = ac.GetComponentsInChildren<DuctedFan>(true);
                if (ducted != null)
                {
                    for (int i = 0; i < ducted.Length; i++)
                        HealEngine(ducted[i]);
                }
            }
            catch { }

            HealPropAndRotorEngines(ac);
            RestoreAircraftNozzles(ac);

            try
            {
                FuelTank[] tanks = ac.GetComponentsInChildren<FuelTank>(true);
                if (tanks != null)
                {
                    for (int i = 0; i < tanks.Length; i++)
                        HealFuel(tanks[i]);
                }
            }
            catch { }

            n += AirframeWearService.RepairAll(ac);
            return n;
        }

        private static int RepairOne(Aircraft ac, DamagedEntry e, bool silent)
        {
            if (e.Kind == DamageKind.Pilot)
            {
                AirframeWearService.RepairPilot();
                return 1;
            }
            if (e.Kind == DamageKind.Wear)
            {
                if (AirframeWearService.RepairPartAt(e.InstanceId))
                    return 1;
                return 0;
            }
            if (e.Unrepairable || IsUnrestorableDetached(e.Part))
                return 0;
            int n = 0;
            if (e.Part != null)
            {
                HealPart(e.Part);
                n++;
            }
            if (e.Extra != null)
            {
                if (e.Extra is FuelTank)
                {
                    HealFuel(e.Extra as FuelTank);
                    n++;
                }
                else
                {
                    HealEngine(e.Extra);
                    n++;
                }
            }

            // Engines often live on critical UnitParts — heal linked parts.
            if (e.Kind == DamageKind.Engine && e.Extra != null)
            {
                try
                {
                    UnitPart[] linked = e.Extra.GetComponentsInChildren<UnitPart>(true);
                    if (linked != null)
                    {
                        for (int i = 0; i < linked.Length; i++)
                        {
                            if (linked[i] != null && !IsUnrestorableDetached(linked[i]))
                                HealPart(linked[i]);
                        }
                    }
                }
                catch { }
            }

            if (!silent && ac != null && Plugin.Log != null)
                Plugin.Log.LogInfo("ComponentRepair: " + e.Label + " (" + e.Detail + ")");
            return Mathf.Max(1, n);
        }

        private static void HealPart(UnitPart part)
        {
            if (part == null || IsUnrestorableDetached(part))
                return;

            bool detached = IsPartDetached(part);

            int id = 0;
            try { id = part.GetInstanceID(); }
            catch { }
            float maxHp = 100f;
            float recorded;
            if (id != 0 && MaxHitPoints.TryGetValue(id, out recorded) && recorded > 0.01f)
                maxHp = recorded;

            if (detached)
            {
                try { part.Repair(); }
                catch (Exception ex)
                {
                    if (Plugin.Log != null)
                        Plugin.Log.LogWarning("UnitPart.Repair: " + ex.Message);
                }
                WriteBool(part, DetachedFromUnitField, false);
                ClearAttachDetached(part);
                AeroPart aero = part as AeroPart;
                if (aero != null)
                {
                    try { aero.CheckAttachment(); }
                    catch { }
                    try { aero.Repair(); }
                    catch { }
                }
            }

            try
            {
                if (part.hitPoints < maxHp)
                    part.hitPoints = maxHp;
            }
            catch { }

            try
            {
                if (part.gameObject != null && !part.gameObject.activeSelf)
                    part.gameObject.SetActive(true);
            }
            catch { }
        }

        private static void HealEngine(Component eng)
        {
            if (eng == null || IsUnrestorableDetached(HostPartOf(eng)))
                return;

            Turbojet tj = eng as Turbojet;
            if (tj != null)
            {
                try { tj.engineFire = false; }
                catch { }
                CullEngineFireVisuals(tj);
                try { tj.damageFactor = 1f; }
                catch { }
                WriteBool(tj, TurbojetOperableField, true);
                WriteFloat(tj, TurbojetConditionField, 1f);
                WriteBool(tj, TurbojetHasFuelField, true);
                try { tj.enabled = true; }
                catch { }
                RestoreEngineNozzles(tj, TurbojetNozzlesField);
                return;
            }

            TurbineEngine te = eng as TurbineEngine;
            if (te != null)
            {
                WriteBool(te, TurbineOperableField, true);
                WriteFloat(te, TurbineConditionField, 1f);
                WriteBool(te, TurbineHasFuelField, true);
                WriteFloat(te, TurbineStartedField, 1f);
                try { te.enabled = true; }
                catch { }
                return;
            }

            Turbofan tf = eng as Turbofan;
            if (tf != null)
            {
                WriteBool(tf, TurbofanOperableField, true);
                WriteFloat(tf, TurbofanConditionField, 1f);
                WriteFloat(tf, TurbofanHitPointsField, 100f);
                WriteBool(tf, TurbofanHasFuelField, true);
                try { tf.enabled = true; }
                catch { }
                RestoreEngineNozzles(tf, TurbofanNozzlesField);
                return;
            }

            DuctedFan dfan = eng as DuctedFan;
            if (dfan != null)
            {
                WriteBool(dfan, DuctedFanInoperableField, false);
                try { dfan.enabled = true; }
                catch { }
                return;
            }

            PropFan pf = eng as PropFan;
            if (pf != null)
            {
                WriteFloat(pf, PropFanConditionField, 1f);
                WriteBool(pf, PropFanDamageReportedField, false);
                try { pf.enabled = true; }
                catch { }
                return;
            }

            ConstantSpeedProp prop = eng as ConstantSpeedProp;
            if (prop != null)
            {
                WriteBool(prop, PropOperableField, true);
                WriteBool(prop, PropStrikeField, false);
                float friction = ReadFloat(prop, PropHubFrictionField, 0f);
                if (friction > 500f)
                    WriteFloat(prop, PropHubFrictionField, 0f);
                try { prop.enabled = true; }
                catch { }
                return;
            }

            RotorShaft rotor = eng as RotorShaft;
            if (rotor != null)
            {
                WriteBool(rotor, RotorDetachedField, false);
                WriteFloat(rotor, RotorConditionField, 1f);
                try { rotor.enabled = true; }
                catch { }
            }
        }

        private static void HealFuel(FuelTank tank)
        {
            if (tank == null || IsUnrestorableDetached(HostPartOf(tank)))
                return;
            try { tank.UpdateStatus(false, false); }
            catch { }
            WriteBool(tank, FuelOnFireField, false);
            WriteBool(tank, FuelRupturedField, false);
            WriteBool(tank, FuelLeakingField, false);
            CullTankFireVisuals(tank);
        }

        private static int CountFires(Aircraft ac)
        {
            if (ac == null)
                return 0;
            EnsureRuntimeCache(ac);
            int n = 0;
            for (int i = 0; i < _rtJets.Count; i++)
            {
                try
                {
                    if (_rtJets[i] != null && _rtJets[i].engineFire)
                        n++;
                }
                catch { }
            }
            for (int i = 0; i < _rtTanks.Count; i++)
            {
                if (_rtTanks[i] != null && ReadBool(_rtTanks[i], FuelOnFireField, false))
                    n++;
            }
            return n;
        }

        private static int ExtinguishFires(Aircraft ac)
        {
            if (ac == null)
                return 0;
            int n = 0;
            try { n = ac.GetInstanceID(); }
            catch { n = 0; }
            if (n != 0)
            {
                _blockFireAcId = n;
                _blockFire = true;
            }
            n = 0;

            try
            {
                Turbojet[] jets = ac.GetComponentsInChildren<Turbojet>(true);
                if (jets != null)
                {
                    for (int i = 0; i < jets.Length; i++)
                    {
                        Turbojet tj = jets[i];
                        if (tj == null)
                            continue;
                        bool lit = false;
                        try { lit = tj.engineFire; }
                        catch { }
                        try { tj.engineFire = false; }
                        catch { }
                        CullEngineFireVisuals(tj);
                        if (lit)
                            n++;
                    }
                }
            }
            catch { }

            try
            {
                FuelTank[] tanks = ac.GetComponentsInChildren<FuelTank>(true);
                if (tanks != null)
                {
                    for (int i = 0; i < tanks.Length; i++)
                    {
                        FuelTank tank = tanks[i];
                        if (tank == null)
                            continue;
                        bool lit = ReadBool(tank, FuelOnFireField, false);
                        WriteBool(tank, FuelOnFireField, false);
                        CullTankFireVisuals(tank);
                        if (lit)
                            n++;
                    }
                }
            }
            catch { }

            n += ExtinguishParticles(ac);
            return n;
        }

        private static int ExtinguishParticles(Aircraft ac)
        {
            if (ac == null)
                return 0;
            int n = 0;
            try
            {
                DamageParticles[] dps = ac.GetComponentsInChildren<DamageParticles>(true);
                if (dps != null)
                {
                    for (int i = 0; i < dps.Length; i++)
                    {
                        if (!IsFireParticle(dps[i]))
                            continue;
                        CullFireParticle(dps[i]);
                        n++;
                    }
                }
            }
            catch { }

            try
            {
                for (int i = 0; i < _rtParts.Count; i++)
                    CullHostedFire(_rtParts[i]);
            }
            catch { }

            try
            {
                if (ac.spawnedEffects != null)
                {
                    List<DamageParticles> fx = ac.spawnedEffects;
                    for (int i = 0; i < fx.Count; i++)
                    {
                        if (IsFireParticle(fx[i]))
                            CullFireParticle(fx[i]);
                    }
                }
            }
            catch { }

            return n;
        }

        private static void CullEngineFireVisuals(Turbojet tj)
        {
            if (tj == null || TurbojetNozzlesField == null)
                return;
            try
            {
                JetNozzle[] nozzles = TurbojetNozzlesField.GetValue(tj) as JetNozzle[];
                if (nozzles == null)
                    return;
                for (int i = 0; i < nozzles.Length; i++)
                {
                    if (nozzles[i] != null)
                        nozzles[i].CullDamageParticles();
                }
            }
            catch { }
        }

        private static void CullTankFireVisuals(FuelTank tank)
        {
            if (tank == null)
                return;
            DamageParticles dp = ReadObject(tank, FuelFireParticlesField) as DamageParticles;
            CullFireParticle(dp);
            DestroyGo(ReadObject(tank, FuelFireEffectSpawnField) as GameObject);
            DestroyGo(ReadObject(tank, FuelFireballSpawnField) as GameObject);
            if (FuelFireParticlesField != null)
            {
                try { FuelFireParticlesField.SetValue(tank, null); }
                catch { }
            }
            if (FuelFireEffectSpawnField != null)
            {
                try { FuelFireEffectSpawnField.SetValue(tank, null); }
                catch { }
            }
            if (FuelFireballSpawnField != null)
            {
                try { FuelFireballSpawnField.SetValue(tank, null); }
                catch { }
            }
        }

        private static void CullHostedFire(UnitPart part)
        {
            if (part == null || HostedParticlesField == null)
                return;
            try
            {
                List<DamageParticles> hosted =
                    HostedParticlesField.GetValue(part) as List<DamageParticles>;
                if (hosted == null)
                    return;
                for (int i = 0; i < hosted.Count; i++)
                {
                    if (IsFireParticle(hosted[i]))
                        CullFireParticle(hosted[i]);
                }
            }
            catch { }
        }

        private static bool IsFireParticle(DamageParticles dp)
        {
            if (dp == null)
                return false;
            float fd = 0f;
            if (DpFireDamageField != null)
            {
                try { fd = (float)DpFireDamageField.GetValue(dp); }
                catch { fd = 0f; }
            }
            if (fd > 0f)
                return true;
            if (DpFireLifetimeField != null)
            {
                try
                {
                    float life = (float)DpFireLifetimeField.GetValue(dp);
                    if (life > 0f)
                        return true;
                }
                catch { }
            }
            if (DpFireLightField != null)
            {
                try
                {
                    if (DpFireLightField.GetValue(dp) != null)
                        return true;
                }
                catch { }
            }
            try
            {
                string name = dp.gameObject != null ? dp.gameObject.name : null;
                if (!string.IsNullOrEmpty(name))
                {
                    string low = name.ToLowerInvariant();
                    if (low.IndexOf("fire") >= 0 || low.IndexOf("flame") >= 0)
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static void CullFireParticle(DamageParticles dp)
        {
            if (dp == null)
                return;
            if (DpFireDamageField != null)
            {
                try { DpFireDamageField.SetValue(dp, 0f); }
                catch { }
            }
            if (DpFireLifetimeField != null)
            {
                try { DpFireLifetimeField.SetValue(dp, 0f); }
                catch { }
            }
            try { dp.ParentObjectCulled(); }
            catch { }
            DestroyGo(dp.gameObject);
        }

        private static void DestroyGo(GameObject go)
        {
            if (go == null)
                return;
            try { UnityEngine.Object.Destroy(go); }
            catch { }
        }

        private static object ReadObject(object obj, FieldInfo field)
        {
            if (obj == null || field == null)
                return null;
            try { return field.GetValue(obj); }
            catch { return null; }
        }

        private static void ClearAttachDetached(UnitPart part)
        {
            if (part == null || AttachInfoField == null)
                return;
            try
            {
                object info = AttachInfoField.GetValue(part);
                if (info == null)
                    return;
                FieldInfo f = AccessTools.Field(info.GetType(), "detachedFromParentPart");
                if (f != null && f.FieldType == typeof(bool))
                    f.SetValue(info, false);
            }
            catch { }
        }

        private static void NoteMaxHp(int id, float hp)
        {
            ComponentRepairMathService.NoteMaxHp(MaxHitPoints, id, hp);
        }

        private static string HpDetail(float hp, float maxHp)
        {
            return ComponentRepairMathService.HpDetail(hp, maxHp);
        }

        private static string PartName(UnitPart part)
        {
            if (part == null)
                return "Part";
            return ComponentRepairMathService.PartName(SafeName(part.gameObject), part.GetType().Name);
        }

        private static string SafeName(GameObject go)
        {
            try
            {
                if (go != null)
                    return go.name;
            }
            catch { }
            return "?";
        }

        private static void ScanEngineArray(Component[] engines, HashSet<int> seen, List<DamagedEntry> dst)
        {
            if (engines == null)
                return;
            try
            {
                for (int i = 0; i < engines.Length; i++)
                    ConsiderEngine(engines[i], seen, dst);
            }
            catch { }
        }

        private static int HealPropAndRotorEngines(Aircraft ac)
        {
            if (ac == null)
                return 0;
            int n = 0;
            try
            {
                PropFan[] pfs = ac.GetComponentsInChildren<PropFan>(true);
                if (pfs != null)
                {
                    for (int i = 0; i < pfs.Length; i++)
                    {
                        if (pfs[i] == null)
                            continue;
                        HealEngine(pfs[i]);
                        n++;
                    }
                }
            }
            catch { }
            try
            {
                ConstantSpeedProp[] props = ac.GetComponentsInChildren<ConstantSpeedProp>(true);
                if (props != null)
                {
                    for (int i = 0; i < props.Length; i++)
                    {
                        if (props[i] == null)
                            continue;
                        HealEngine(props[i]);
                        n++;
                    }
                }
            }
            catch { }
            try
            {
                RotorShaft[] rotors = ac.GetComponentsInChildren<RotorShaft>(true);
                if (rotors != null)
                {
                    for (int i = 0; i < rotors.Length; i++)
                    {
                        if (rotors[i] == null)
                            continue;
                        HealEngine(rotors[i]);
                        n++;
                    }
                }
            }
            catch { }
            return n;
        }

        private static string EngineHealDetail(Component eng)
        {
            if (eng == null)
                return "INOP";
            Turbojet tj = eng as Turbojet;
            if (tj != null)
            {
                try
                {
                    if (tj.engineFire)
                        return "FIRE";
                }
                catch { }
                if (!ReadBool(tj, TurbojetOperableField, true))
                    return "INOP";
                if (NozzlesNeedRestore(tj))
                    return "NO THRUST";
                return "DMG";
            }
            if (NozzlesNeedRestore(eng))
                return "NO THRUST";
            return "INOP";
        }

        internal static void NoteNozzleThrust(JetNozzle nozzle)
        {
            if (nozzle == null || NozzleThrustPropField == null)
                return;
            float p = ReadFloat(nozzle, NozzleThrustPropField, 0f);
            if (p <= 0.001f)
                return;
            try { NozzleThrustProp[nozzle.GetInstanceID()] = p; }
            catch { }
        }

        private static bool NozzlesNeedRestore(Component eng)
        {
            JetNozzle[] nozzles = ReadNozzles(eng);
            if (nozzles == null)
                return false;
            for (int i = 0; i < nozzles.Length; i++)
            {
                if (nozzles[i] == null)
                    continue;
                NoteNozzleThrust(nozzles[i]);
                if (ReadFloat(nozzles[i], NozzleThrustPropField, 1f) <= 0.001f)
                    return true;
            }
            return false;
        }

        private static void RestoreEngineNozzles(Component eng, FieldInfo field)
        {
            JetNozzle[] nozzles = ReadNozzleArray(eng, field);
            if (nozzles == null)
                nozzles = ReadChildNozzles(eng);
            RestoreNozzleArray(nozzles);
        }

        private static void RestoreAircraftNozzles(Aircraft ac)
        {
            if (ac == null)
                return;
            try
            {
                RestoreNozzleArray(ac.GetComponentsInChildren<JetNozzle>(true));
            }
            catch { }
        }

        private static JetNozzle[] ReadNozzles(Component eng)
        {
            if (eng is Turbojet)
                return ReadNozzleArray(eng, TurbojetNozzlesField);
            if (eng is Turbofan)
                return ReadNozzleArray(eng, TurbofanNozzlesField);
            return ReadChildNozzles(eng);
        }

        private static JetNozzle[] ReadNozzleArray(Component eng, FieldInfo field)
        {
            if (eng == null || field == null)
                return null;
            try { return field.GetValue(eng) as JetNozzle[]; }
            catch { return null; }
        }

        private static JetNozzle[] ReadChildNozzles(Component eng)
        {
            if (eng == null)
                return null;
            try { return eng.GetComponentsInChildren<JetNozzle>(true); }
            catch { return null; }
        }

        private static void RestoreNozzleArray(JetNozzle[] nozzles)
        {
            if (nozzles == null)
                return;
            for (int i = 0; i < nozzles.Length; i++)
            {
                JetNozzle n = nozzles[i];
                if (n == null || NozzleThrustPropField == null)
                    continue;
                float p = 1f;
                try
                {
                    int id = n.GetInstanceID();
                    float cached;
                    if (NozzleThrustProp.TryGetValue(id, out cached) && cached > 0.001f)
                        p = cached;
                }
                catch { }
                WriteFloat(n, NozzleThrustPropField, p);
            }
        }

        private static float ReadFloat(object obj, FieldInfo field, float defaultValue)
        {
            if (obj == null || field == null)
                return defaultValue;
            try
            {
                object v = field.GetValue(obj);
                if (v is float)
                    return (float)v;
            }
            catch { }
            return defaultValue;
        }

        private static void WriteFloat(object obj, FieldInfo field, float value)
        {
            if (obj == null || field == null)
                return;
            try { field.SetValue(obj, value); }
            catch { }
        }

        private static bool ReadBool(object obj, FieldInfo field, bool defaultValue)
        {
            if (obj == null || field == null)
                return defaultValue;
            try
            {
                object v = field.GetValue(obj);
                if (v is bool)
                    return (bool)v;
            }
            catch { }
            return defaultValue;
        }

        private static void WriteBool(object obj, FieldInfo field, bool value)
        {
            if (obj == null || field == null)
                return;
            try { field.SetValue(obj, value); }
            catch { }
        }

        private static void InvalidateCache()
        {
            _listCacheUntil = 0f;
            _listCacheAcId = 0;
            _cached.Clear();
            _rtUntil = 0f;
        }

        private static void Flash(string msg)
        {
            _status = msg;
            _statusUntil = Time.unscaledTime + 3.5f;
        }

        private static bool UiZh()
        {
            return UiLang.IsChinese;
        }

        private static void EnsureStyles()
        {
            if (_rowStyle != null)
                return;
            _rowStyle = new GUIStyle(GUI.skin.label);
            _rowStyle.fontSize = 12;
            _rowStyle.alignment = TextAnchor.MiddleLeft;
            _rowStyle.normal.textColor = new Color(0.9f, 0.95f, 1f, 0.95f);
            _rowStyle.wordWrap = true;
            _rowStyle.clipping = TextClipping.Clip;

            _btnStyle = new GUIStyle(GUI.skin.button);
            _btnStyle.fontSize = 12;
            _btnStyle.fontStyle = FontStyle.Bold;
            _btnStyle.alignment = TextAnchor.MiddleCenter;
            _btnStyle.normal.textColor = Color.white;

            _hintStyle = new GUIStyle(GUI.skin.label);
            _hintStyle.fontSize = 12;
            _hintStyle.alignment = TextAnchor.MiddleLeft;
            _hintStyle.normal.textColor = new Color(0.7f, 1f, 0.8f, 0.98f);

            _toggleStyle = new GUIStyle(GUI.skin.toggle);
            _toggleStyle.fontSize = 13;
            _toggleStyle.normal.textColor = new Color(0.85f, 0.95f, 1f, 0.95f);
            _toggleStyle.onNormal.textColor = new Color(0.35f, 1f, 0.55f, 1f);
        }
    }

    [HarmonyPatch(typeof(JetNozzle), "Awake")]
    internal static class Patch_JetNozzle_CacheThrustProp
    {
        [HarmonyPostfix]
        private static void Postfix(JetNozzle __instance)
        {
            ComponentRepair.NoteNozzleThrust(__instance);
        }
    }

    [HarmonyPatch(typeof(UnitPart), "TakeDamage")]
    internal static class Patch_UnitPart_TakeDamage_Extinguish
    {
        [HarmonyPrefix]
        private static void Prefix(UnitPart __instance, ref float fireDamage)
        {
            if (fireDamage <= 0f || __instance == null)
                return;
            if (ComponentRepair.ShouldBlockFire(__instance))
                fireDamage = 0f;
        }
    }
}
