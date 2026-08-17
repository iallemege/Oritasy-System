using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Pilot health + per-airframe engine / propeller / lift wear.
    /// Multi-engine airframes get one subsystem per powerplant.
    /// Secondary damage flickers thrust and raises other-part wear chance.
    /// Each repair rolls the part's health ceiling down.
    /// </summary>
    internal static class AirframeWearService
    {
        internal enum Family
        {
            JetAb = 0,
            JetDry = 1,
            Prop = 2,
            Helo = 3,
            Vtol = 4,
            Stovl = 5,
            Bomber = 6,
            Tilt = 7
        }

        internal struct Part
        {
            public string Id;
            public string En;
            public string Zh;
            public int Bank;
            public bool Secondary;
            public bool Prop;
            public float Health;
            public float Ceiling;
            public bool GSensitive;
            public bool AbSensitive;
            public float WearMul;
        }

        private static readonly FieldInfo TurbojetCondition =
            AccessTools.Field(typeof(Turbojet), "condition");
        private static readonly FieldInfo TurbineCondition =
            AccessTools.Field(typeof(TurbineEngine), "condition");
        private static readonly FieldInfo TurbofanCondition =
            AccessTools.Field(typeof(Turbofan), "condition");
        private static readonly FieldInfo PropFanCondition =
            AccessTools.Field(typeof(PropFan), "condition");
        private static readonly FieldInfo RotorCondition =
            AccessTools.Field(typeof(RotorShaft), "condition");
        private static readonly FieldInfo TurbojetOperable =
            AccessTools.Field(typeof(Turbojet), "operable");
        private static readonly FieldInfo TurbojetCritical =
            AccessTools.Field(typeof(Turbojet), "criticalParts");
        private static readonly FieldInfo TurbojetMinDensity =
            AccessTools.Field(typeof(Turbojet), "minDensity");
        private static readonly FieldInfo TurbofanOperable =
            AccessTools.Field(typeof(Turbofan), "operable");
        private static readonly FieldInfo TurbofanMinDensity =
            AccessTools.Field(typeof(Turbofan), "minDensity");
        private static readonly FieldInfo TurbofanHitPoints =
            AccessTools.Field(typeof(Turbofan), "hitPoints");
        private static readonly FieldInfo TurbineOperable =
            AccessTools.Field(typeof(TurbineEngine), "operable");
        private static readonly FieldInfo TurbineCritical =
            AccessTools.Field(typeof(TurbineEngine), "criticalParts");
        private static readonly FieldInfo DuctedInoperable =
            AccessTools.Field(typeof(DuctedFan), "inoperable");
        private static readonly FieldInfo PropOperable =
            AccessTools.Field(typeof(ConstantSpeedProp), "operable");
        private static readonly FieldInfo RotorDetached =
            AccessTools.Field(typeof(RotorShaft), "detached");
        private static readonly FieldInfo PartDetached =
            AccessTools.Field(typeof(UnitPart), "detachedFromUnit");
        private static readonly FieldInfo TurbojetHasFuel =
            AccessTools.Field(typeof(Turbojet), "hasFuel");
        private static readonly FieldInfo TurbofanHasFuel =
            AccessTools.Field(typeof(Turbofan), "hasFuel");
        private static readonly FieldInfo TurbineHasFuel =
            AccessTools.Field(typeof(TurbineEngine), "hasFuel");
        private static readonly FieldInfo JetAfterburnersField =
            AccessTools.Field(typeof(JetNozzle), "afterburners");
        private static readonly FieldInfo JetThrustProp =
            AccessTools.Field(typeof(JetNozzle), "thrustProportion");
        private static FieldInfo AfterburnerAmountField;
        private static bool _abReflectTried;

        private const float CeilingFloor = 0.45f;
        private const float DisplayFull = 100f;
        private const float DefaultPartGGate = 7f;
        private const float SoftPartGGate = 4f;
        private const float WearRateMul = 1.25f;
        private const float AbWearAfter = 180f;
        private const float ClickRepairSeconds = 2f;
        private const int MaterialMax = 3;
        private const string MaterialPref = "Oritasy.EngineMat";

        private static int _acId;
        private static string _acKey = "unknown";
        private static Family _family;
        private static int _banks;
        private static int _loCount;
        private static readonly List<Part> _parts = new List<Part>(48);
        private static readonly List<Component> _plants = new List<Component>(8);
        private static JetNozzle[] _nozzles;
        private static float _partGGate = DefaultPartGGate;
        private static bool _mixedAlt;
        private static bool _hiActive;
        private static bool _hiInited;
        private static readonly int[] _altKind = new int[8];
        private static readonly float[] _vanilla = new float[8];
        private static readonly float[] _coreTemp = new float[8];
        private static readonly float[] _abTemp = new float[8];
        private static readonly float[] _oilTemp = new float[8];
        private static float _oilSoak;
        private static float _thPrev;
        private static bool _thInited;
        private static int _tempDir;
        private static float _tempTravel;
        private static float _corePrev;
        private static float _ratedC = 740f;
        private static float _ratedThrottle = 0.92f;
        private static bool _hasAbHw;
        private static float _pilot = 1f;
        private static float _abHold;
        private static float _gHold;
        private static float _applyAt;
        private static float _flickerMul = 1f;
        private static readonly float[] _flickerMulBank = new float[8];
        private static readonly float[] _flickerAtBank = new float[8];
        private static readonly float[] _nozProp0 = new float[8];
        private static float _cascadeAt;
        private static int _hudFlashesLeft;
        private static bool _hudFlashOn;
        private static float _hudFlashAt;
        private static string _hudFlashText;
        private static int _pendingHurt = -1;
        private static float _pendingDrop;
        private static int _hitKind;
        private static GUIStyle _hudStyle;
        private static int _matTier;
        private static bool _matLoaded;
        private static readonly List<int> _repairQ = new List<int>(16);
        private static float _repairLeft;
        private static float _hitAt;
        private static readonly List<object> _flickerAbs = new List<object>(16);
        private static int _flickerAbI = -1;
        private static float _flickerAbAt;
        private static readonly bool[] _fuelCut = new bool[8];
        private static bool _reportSuppressed;
        private static readonly MethodInfo ActionsClear =
            AccessTools.Method(typeof(AircraftActionsReport), "ClearMessages");
        private const int HudFlashTimes = 10;
        private const int SwitchHudFlashTimes = 5;
        private const float HudFlashOn = 0.16f;
        private const float HudFlashOff = 0.12f;
        /// <summary>High-alt engine set starts at 14 km radar; hysteresis off at 12.5 km.</summary>
        private const float HiOnAlt = 14000f;
        private const float HiOffAlt = 12500f;

        internal static float PilotHealth01()
        {
            return _pilot;
        }

        internal static bool PilotNeedsHeal()
        {
            return _pilot < 0.995f;
        }

        internal static Family CurrentFamily()
        {
            return _family;
        }

        internal static int EngineCount()
        {
            return _banks;
        }

        internal static int LoCount()
        {
            return _loCount;
        }

        internal static bool Flickering()
        {
            for (int i = 0; i < _banks && i < 8; i++)
            {
                if (_flickerMulBank[i] < 0.97f)
                    return true;
            }
            return _flickerMul < 0.97f;
        }

        internal static string FamilyLabel()
        {
            if (AircraftIdentity.IsAb4(_acKey))
                return UiLang.T("4-engine afterburning", "四发加力喷气");
            if (AircraftIdentity.IsKr67(_acKey))
                return UiLang.T("Twin afterburning jet", "双发加力喷气");
            if (AircraftIdentity.IsFs12(_acKey))
                return UiLang.T("Afterburning jet", "加力喷气");
            if (AircraftIdentity.IsFs20(_acKey))
                return UiLang.T("VTOL afterburning", "VTOL 加力");
            if (AircraftIdentity.IsVt7(_acKey))
                return UiLang.T("VTOL", "VTOL 垂起");
            if (AircraftIdentity.IsEw25(_acKey))
                return UiLang.T("STOVL", "STOVL 短距垂起");
            if (AircraftIdentity.IsVl49(_acKey))
                return UiLang.T("Quad tiltrotor", "四轴倾转旋翼");
            if (AircraftIdentity.IsUh90(_acKey) || AircraftIdentity.IsSah46(_acKey))
                return UiLang.T("Helicopter", "直升机");
            if (AircraftIdentity.IsA19(_acKey))
                return UiLang.T("Prop fan", "桨扇");
            if (AircraftIdentity.IsCi22(_acKey))
                return UiLang.T("Propeller aircraft", "螺旋桨飞机");
            if (AircraftIdentity.IsTa30(_acKey))
                return UiLang.T("Dry jet / trainer", "无加力喷气");
            switch (_family)
            {
                case Family.JetAb:
                    return UiLang.T("Afterburning jet", "加力喷气");
                case Family.JetDry:
                    return UiLang.T("Dry jet / trainer", "无加力喷气");
                case Family.Prop:
                    return UiLang.T("Propeller aircraft", "螺旋桨飞机");
                case Family.Helo:
                    return UiLang.T("Helicopter", "直升机");
                case Family.Vtol:
                    return UiLang.T("VTOL", "VTOL 垂起");
                case Family.Stovl:
                    return UiLang.T("STOVL", "STOVL 短距垂起");
                case Family.Tilt:
                    return UiLang.T("Quad tiltrotor", "四轴倾转旋翼");
                default:
                    return UiLang.T("Multi-engine bomber", "多发轰炸机");
            }
        }

        internal static int PartCount()
        {
            return _parts.Count;
        }

        internal static Part GetPart(int i)
        {
            if (i < 0 || i >= _parts.Count)
                return default(Part);
            return _parts[i];
        }

        internal static string PartLabel(Part p)
        {
            string name = UiLang.T(p.En, p.Zh);
            if (p.Bank >= 0)
            {
                string prefix = p.Prop
                    ? UiLang.T("P", "桨")
                    : UiLang.T("E", "发");
                string alt = BankAltShort(p.Bank);
                if (!string.IsNullOrEmpty(alt))
                    return prefix + (p.Bank + 1) + " " + alt + " " + name;
                return prefix + (p.Bank + 1) + " " + name;
            }
            return name;
        }

        internal static string BankAltShort(int bank)
        {
            int k = BankAltKind(bank);
            if (k == 2)
                return UiLang.T("HI", "高空");
            if (k == 1)
                return UiLang.T("LO", "低空");
            return "";
        }

        internal static int HealthPct(Part p)
        {
            return Mathf.RoundToInt(p.Health * DisplayFull);
        }

        internal static int CeilingPct(Part p)
        {
            return Mathf.RoundToInt(p.Ceiling * DisplayFull);
        }

        internal static float AbHoldSeconds()
        {
            return _abHold;
        }

        internal static float GHoldSeconds()
        {
            return _gHold;
        }

        internal static int MaterialTier()
        {
            EnsureMaterial();
            return _matTier;
        }

        internal static string MaterialLabel()
        {
            EnsureMaterial();
            switch (_matTier)
            {
                case 1:
                    return UiLang.T("Improved alloy", "改进合金");
                case 2:
                    return UiLang.T("High-temp alloy", "高温合金");
                case 3:
                    return UiLang.T("Single-crystal alloy", "单晶合金");
                default:
                    return UiLang.T("Stock alloy", "标准合金");
            }
        }

        internal static bool TryUpgradeMaterial(out string reveal)
        {
            reveal = null;
            EnsureMaterial();
            if (_matTier >= MaterialMax)
                return false;
            _matTier++;
            PlayerPrefs.SetInt(MaterialPref, _matTier);
            PlayerPrefs.Save();
            reveal = UiLang.T(
                "Engine material → " + MaterialLabel(),
                "引擎材料 → " + MaterialLabel());
            return true;
        }

        internal static bool IsRepairing(int index)
        {
            return _repairQ.Count > 0 && _repairQ[0] == index;
        }

        internal static bool IsQueued(int index)
        {
            for (int i = 0; i < _repairQ.Count; i++)
            {
                if (_repairQ[i] == index)
                    return true;
            }
            return false;
        }

        internal static float RepairProgress01()
        {
            if (_repairQ.Count <= 0 || ClickRepairSeconds <= 0.01f)
                return 0f;
            return Mathf.Clamp01(1f - _repairLeft / ClickRepairSeconds);
        }

        internal static int RepairQueueCount()
        {
            return _repairQ.Count;
        }

        internal static float PartGGate()
        {
            return _partGGate;
        }

        internal static bool HasMixedAltitude()
        {
            return _mixedAlt;
        }

        internal static bool HighAltActive()
        {
            return _hiActive;
        }

        internal static bool BankRunning(int bank)
        {
            if (!AircraftIgnition())
                return false;
            if (bank < 0)
                return true;
            return BankAltitudeActive(bank);
        }

        internal static bool IsApuPart(Part p)
        {
            return p.Id == "s.apu";
        }

        internal static bool PartIsRunning(Part p)
        {
            if (IsApuPart(p))
                return EngineStartService.ApuRunning;
            return BankRunning(p.Bank);
        }

        internal static bool ApuCanStartEngine()
        {
            for (int i = 0; i < _parts.Count; i++)
            {
                if (_parts[i].Id != "s.apu")
                    continue;
                return _parts[i].Health >= 0.35f;
            }
            return true;
        }

        internal static void FlashHudMessage(string text, int times)
        {
            BeginHudFlashText(text, times);
        }

        internal static bool EngineShouldProduceThrust(Component eng)
        {
            if (eng == null)
                return true;
            if (!HasSeparateHiPlants())
                return true;
            int b = PlantIndex(eng);
            if (b < 0)
                return true;
            return BankAltitudeActive(b);
        }

        /// <summary>1 = low-alt, 2 = high-alt, 0 = unmarked.</summary>
        internal static int BankAltKind(int bank)
        {
            if (bank < 0 || bank >= _altKind.Length)
                return 0;
            return _altKind[bank];
        }

        internal static string BankAltLabel(int bank)
        {
            int k = BankAltKind(bank);
            if (k == 2)
                return UiLang.T("High-alt engine", "高空引擎");
            if (k == 1)
                return UiLang.T("Low-alt engine", "低空引擎");
            return "";
        }

        internal static bool HasAfterburner()
        {
            return _hasAbHw;
        }

        internal static float RatedTempC()
        {
            return _ratedC;
        }

        internal static float RatedThrottle01()
        {
            return _ratedThrottle;
        }

        internal static float CoreTempC()
        {
            return Hottest(_coreTemp) * _ratedC;
        }

        internal static float OilTempC()
        {
            return Hottest(_oilTemp) * _ratedC;
        }

        internal static float AbTempC()
        {
            return Hottest(_abTemp) * _ratedC;
        }

        internal static float CoreTempRatio()
        {
            return Hottest(_coreTemp);
        }

        internal static float OilTempRatio()
        {
            return Hottest(_oilTemp);
        }

        internal static string PartTempHint(Part p)
        {
            if (string.IsNullOrEmpty(p.Id))
                return "";
            int d = p.Id.LastIndexOf('.');
            string s = d >= 0 && d < p.Id.Length - 1 ? p.Id.Substring(d + 1) : p.Id;
            if (s == "oil")
                return UiLang.T(
                    "oil " + OilTempC().ToString("0") + "°C / rated " + RatedTempC().ToString("0") + "°C",
                    "滑油 " + OilTempC().ToString("0") + "°C / 标准 " + RatedTempC().ToString("0") + "°C");
            if (s == "ab")
                return UiLang.T(
                    "AB " + AbTempC().ToString("0") + "°C",
                    "加力室 " + AbTempC().ToString("0") + "°C");
            if (s == "inject" || s == "carb")
                return UiLang.T("fuel injection", "注油系统");
            if (s == "radiator" || s == "inter")
                return UiLang.T("cooling", "散热系统");
            if (s == "combine")
                return UiLang.T("thrust combiner", "推力统合系统");
            if (s == "compressor")
                return UiLang.T("compressor", "压气机");
            if (s == "turbine" || s == "core" || s == "combustor" || s == "crank" || s == "shaft")
                return UiLang.T(
                    "EGT " + CoreTempC().ToString("0") + "°C / rated " + RatedTempC().ToString("0") + "°C",
                    "排气温度 " + CoreTempC().ToString("0") + "°C / 标准 " + RatedTempC().ToString("0") + "°C");
            return "";
        }

        internal static void Tick()
        {
            Aircraft ac = LocalAircraft();
            if (ac == null)
            {
                _acId = 0;
                _hiInited = false;
                SuppressReports();
                return;
            }
            int id = 0;
            try { id = ac.GetInstanceID(); }
            catch { return; }
            if (id != _acId)
                BindAircraft(ac, id);
            if (LocalPilotGone(ac))
            {
                SuppressReports();
                return;
            }
            _reportSuppressed = false;

            float dt = Time.deltaTime;
            if (dt <= 0f || dt > 0.25f)
                dt = 0.02f;

            float g = Mathf.Abs(AircraftGLoadService.ReadSignedG(ac));
            bool afterburner = IsAfterburning();
            float gGate = GGate();
            if (g > _partGGate)
                _gHold += dt;
            else
                _gHold = Mathf.Max(0f, _gHold - dt * 2f);

            if (afterburner)
                _abHold += dt;
            else
                _abHold = Mathf.Max(0f, _abHold - dt * 1.5f);

            ApplyPilotDrain(g, dt, gGate);
            ApplyPartDrain(g, afterburner, dt);
            TickTemperature(ac, afterburner, dt);
            ApplyHeatDrain(dt);
            ApplyMountManeuver(ac, g, dt);
            TickAltitudeMode(ac);
            ApplyFuelCutoff();
            SyncVanillaIntoParts();
            TickRepairQueue();
            TickFlicker(dt);
            TickFlickerAbVisual();
            TickCascade(dt);
            FlushHurt();
            TickHudFlash();

            if (Time.unscaledTime >= _applyAt)
            {
                _applyAt = Time.unscaledTime + 0.2f;
                PushToEngines(ac);
            }
        }

        internal static void RepairPilot()
        {
            _pilot = 1f;
        }

        /// <summary>F10 single-part restore. Does not lower the ceiling.</summary>
        internal static bool RepairPartAt(int index)
        {
            return RestoreToCeiling(index);
        }

        /// <summary>F8 click: 2s per part, can queue several. Does not lower the ceiling.</summary>
        internal static bool QueueRepair(int index)
        {
            if (index < 0 || index >= _parts.Count)
                return false;
            Part p = _parts[index];
            if (p.Health >= p.Ceiling * 0.995f)
                return false;
            if (IsQueued(index))
                return false;
            _repairQ.Add(index);
            if (_repairQ.Count == 1)
                _repairLeft = ClickRepairSeconds;
            return true;
        }

        internal static int RepairAllEngines(Aircraft ac)
        {
            _repairQ.Clear();
            _repairLeft = 0f;
            int n = 0;
            for (int i = 0; i < _parts.Count; i++)
            {
                if (_parts[i].Health >= _parts[i].Ceiling * 0.995f)
                    continue;
                if (InstantRepairLowerCap(i))
                    n++;
            }
            if (ac != null)
                PushToEngines(ac);
            return n;
        }

        internal static int RepairAll(Aircraft ac)
        {
            int n = 0;
            if (PilotNeedsHeal())
            {
                RepairPilot();
                n++;
            }
            n += RepairAllEngines(ac);
            return n;
        }

        internal static void ResetAll(Aircraft ac)
        {
            _pilot = 1f;
            _abHold = 0f;
            _gHold = 0f;
            _flickerMul = 1f;
            for (int i = 0; i < 8; i++)
            {
                _flickerMulBank[i] = 1f;
                _flickerAtBank[i] = 0f;
            }
            _hudFlashesLeft = 0;
            _hudFlashOn = false;
            _hudFlashText = null;
            _repairQ.Clear();
            _repairLeft = 0f;
            _flickerAbs.Clear();
            _flickerAbI = -1;
            _thInited = false;
            ResetTempsToIdle();
            for (int i = 0; i < 8; i++)
                _fuelCut[i] = false;
            for (int i = 0; i < _parts.Count; i++)
            {
                Part p = _parts[i];
                p.Health = 1f;
                p.Ceiling = 1f;
                _parts[i] = p;
            }
            for (int i = 0; i < _plants.Count; i++)
                WriteFuel(_plants[i], true);
            if (ac != null)
                PushToEngines(ac);
        }

        internal static void TestHurtG()
        {
            HurtMatching(true, false, 0.12f);
            _pilot = Mathf.Max(0f, _pilot - 0.15f);
        }

        internal static void TestHurtAb()
        {
            HurtMatching(false, true, 0.12f);
        }

        internal static void AppendRepairRows(List<string> labels, List<string> details, List<int> ids)
        {
            if (labels == null || details == null || ids == null)
                return;
            if (PilotNeedsHeal())
            {
                labels.Add(UiLang.T("Pilot", "飞行员"));
                details.Add(Mathf.RoundToInt(_pilot * DisplayFull) + "%");
                ids.Add(-1);
            }
            for (int i = 0; i < _parts.Count; i++)
            {
                Part p = _parts[i];
                if (p.Health >= p.Ceiling * 0.995f && p.Ceiling >= 0.995f)
                    continue;
                labels.Add(PartLabel(p));
                details.Add(HealthPct(p) + "% / " + CeilingPct(p) + "%");
                ids.Add(i);
            }
        }

        internal static Family ResolveFamily(Aircraft ac)
        {
            if (ac == null)
                return Family.JetDry;
            string key = "unknown";
            try { key = AircraftIdentity.GetKey(ac); }
            catch { }
            if (AircraftIdentity.IsUh90(key) || AircraftIdentity.IsSah46(key))
                return Family.Helo;
            if (AircraftIdentity.IsVl49(key))
                return Family.Tilt;
            if (AircraftIdentity.IsEw25(key))
                return Family.Stovl;
            if (AircraftIdentity.IsFs20(key) || AircraftIdentity.IsVt7(key))
                return Family.Vtol;
            if (AircraftIdentity.IsCi22(key) || AircraftIdentity.IsA19(key))
                return Family.Prop;
            if (AircraftIdentity.IsAb4(key) || AircraftIdentity.IsFs12(key)
                || AircraftIdentity.IsKr67(key))
                return Family.JetAb;
            if (AircraftIdentity.IsTa30(key))
                return Family.JetDry;
            if (AircraftIdentity.IsSfb(key))
                return Family.Bomber;
            try
            {
                if (ac.GetComponentInChildren<RotorShaft>(true) != null)
                    return Family.Helo;
                if (ac.GetComponentInChildren<ConstantSpeedProp>(true) != null
                    || ac.GetComponentInChildren<PropFan>(true) != null)
                    return Family.Prop;
                if (ac.GetComponentInChildren<DuctedFan>(true) != null)
                    return Family.Vtol;
            }
            catch { }
            if (HardwareHasAfterburner(ac) || AircraftIdentity.HasFleetAfterburner(key))
                return Family.JetAb;
            return Family.JetDry;
        }

        internal static float GGate()
        {
            switch (_family)
            {
                case Family.Helo:
                    return 3.2f;
                case Family.Vtol:
                    return 4.2f;
                case Family.Tilt:
                    return 4.0f;
                case Family.Stovl:
                    return 5.5f;
                case Family.Prop:
                    return 5.5f;
                case Family.Bomber:
                    return 4.8f;
                default:
                    return 7.0f;
            }
        }

        private static void BindAircraft(Aircraft ac, int id)
        {
            _acId = id;
            try { _acKey = AircraftIdentity.GetKey(ac); }
            catch { _acKey = "unknown"; }
            _family = ResolveFamily(ac);
            CollectPlants(ac);
            _nozzles = null;
            try { _nozzles = ac.GetComponentsInChildren<JetNozzle>(true); }
            catch { _nozzles = null; }
            CacheNozzleProps();
            _loCount = ExpectedEngineCount(_acKey);
            if (_loCount < 1)
                _loCount = _plants.Count;
            if (_loCount < 1)
                _loCount = 1;
            if (_loCount > 4)
                _loCount = 4;
            _banks = _loCount * 2;
            if (_banks > 8)
                _banks = 8;
            _partGGate = DefaultPartGGate;
            try
            {
                if (_family == Family.Helo || AircraftIdentity.IsCi22(_acKey))
                    _partGGate = SoftPartGGate;
            }
            catch
            {
                if (_family == Family.Helo)
                    _partGGate = SoftPartGGate;
            }
            ClassifyAltitude(ac);
            _hiInited = false;
            _hiActive = false;
            BindTemperature(ac);
            _pilot = 1f;
            _abHold = 0f;
            _gHold = 0f;
            _flickerMul = 1f;
            for (int i = 0; i < 8; i++)
            {
                _flickerMulBank[i] = 1f;
                _flickerAtBank[i] = 0f;
            }
            _flickerAbs.Clear();
            _flickerAbI = -1;
            _thInited = false;
            _thPrev = 0f;
            _tempDir = 0;
            _tempTravel = 0f;
            _reportSuppressed = false;
            for (int i = 0; i < 8; i++)
                _fuelCut[i] = false;
            _hudFlashesLeft = 0;
            _hudFlashOn = false;
            _hudFlashText = null;
            _pendingHurt = -1;
            _pendingDrop = 0f;
            _repairQ.Clear();
            _repairLeft = 0f;
            _parts.Clear();
            BuildLayout();
        }

        private static void CollectPlants(Aircraft ac)
        {
            _plants.Clear();
            HashSet<int> seen = new HashSet<int>();
            try
            {
                if (ac.engines != null)
                {
                    for (int i = 0; i < ac.engines.Count; i++)
                    {
                        Component c = ac.engines[i] as Component;
                        AddPlant(c, seen);
                    }
                }
            }
            catch { }
            try { AddPlantArray(ac.GetComponentsInChildren<Turbojet>(true), seen); }
            catch { }
            try { AddPlantArray(ac.GetComponentsInChildren<Turbofan>(true), seen); }
            catch { }
            try { AddPlantArray(ac.GetComponentsInChildren<TurbineEngine>(true), seen); }
            catch { }
            try { AddPlantArray(ac.GetComponentsInChildren<PropFan>(true), seen); }
            catch { }
            try { AddPlantArray(ac.GetComponentsInChildren<ConstantSpeedProp>(true), seen); }
            catch { }
            try { AddPlantArray(ac.GetComponentsInChildren<DuctedFan>(true), seen); }
            catch { }
            PreferEngineCores();
        }

        /// <summary>
        /// CI-22 / A-19 / helos list the propeller or rotor as an extra "engine"
        /// next to the TurbineEngine that actually makes power. Counting those
        /// as a high-alt bank fuel-cuts the turbine on the ground, so the prop
        /// never spins after a successful ignition.
        /// </summary>
        private static void PreferEngineCores()
        {
            bool hasCore = false;
            for (int i = 0; i < _plants.Count; i++)
            {
                if (IsEngineCore(_plants[i]))
                {
                    hasCore = true;
                    break;
                }
            }
            if (!hasCore)
                return;
            for (int i = _plants.Count - 1; i >= 0; i--)
            {
                if (IsDrivenOutput(_plants[i]))
                    _plants.RemoveAt(i);
            }
        }

        private static bool IsEngineCore(Component c)
        {
            if (c == null)
                return false;
            return c is Turbojet || c is Turbofan || c is TurbineEngine;
        }

        private static bool IsDrivenOutput(Component c)
        {
            if (c == null)
                return false;
            return c is ConstantSpeedProp || c is PropFan || c is RotorShaft;
        }

        private static void AddPlantArray(Component[] arr, HashSet<int> seen)
        {
            if (arr == null)
                return;
            try
            {
                for (int i = 0; i < arr.Length; i++)
                    AddPlant(arr[i], seen);
            }
            catch { }
        }

        private static void AddPlant(Component c, HashSet<int> seen)
        {
            if (c == null)
                return;
            int id;
            try { id = c.GetInstanceID(); }
            catch { return; }
            if (id == 0 || seen.Contains(id))
                return;
            seen.Add(id);
            _plants.Add(c);
        }

        private static void ClassifyAltitude(Aircraft ac)
        {
            _mixedAlt = _banks >= 2;
            for (int i = 0; i < _altKind.Length; i++)
                _altKind[i] = 0;
            int lo = _loCount;
            if (lo < 1)
                lo = _banks / 2;
            if (lo < 1)
                lo = 1;
            for (int i = 0; i < _banks && i < 8; i++)
                _altKind[i] = i < lo ? 1 : 2;

            if (_plants.Count <= lo)
                return;

            int n = _plants.Count;
            if (n > _banks)
                n = _banks;
            for (int i = 0; i < n; i++)
            {
                int named = NameAltKind(_plants[i]);
                if (named != 0)
                    _altKind[i] = named;
            }
        }

        private static bool HasSeparateHiPlants()
        {
            int cores = 0;
            for (int i = 0; i < _plants.Count; i++)
            {
                if (IsEngineCore(_plants[i]))
                    cores++;
            }
            return cores > _loCount;
        }

        private static int PlantIndex(Component eng)
        {
            if (eng == null)
                return -1;
            for (int i = 0; i < _plants.Count; i++)
            {
                if (object.ReferenceEquals(_plants[i], eng))
                    return i;
            }
            return -1;
        }

        private static bool AircraftIgnition()
        {
            Aircraft ac = LocalAircraft();
            if (ac == null)
                return false;
            try { return ac.Ignition; }
            catch { return true; }
        }

        private static bool BankAltitudeActive(int bank)
        {
            if (!_mixedAlt)
                return true;
            int k = BankAltKind(bank);
            if (k == 0)
                return !_hiActive;
            if (_hiActive)
                return k == 2;
            return k == 1;
        }

        private static void TickAltitudeMode(Aircraft ac)
        {
            float alt = 0f;
            try
            {
                if (ac != null)
                    alt = ac.radarAlt;
            }
            catch { alt = 0f; }
            bool want = _hiActive ? alt >= HiOffAlt : alt >= HiOnAlt;
            if (!_hiInited)
            {
                _hiActive = want;
                _hiInited = true;
                return;
            }
            if (want == _hiActive)
                return;
            _hiActive = want;
            if (_hiActive)
            {
                BeginHudFlashText(
                    UiLang.T("Switching to high-alt engines", "切换至高空引擎"),
                    SwitchHudFlashTimes);
            }
            PushToEngines(ac);
        }

        private static int ExpectedEngineCount(string key)
        {
            if (AircraftIdentity.IsCi22(key) || AircraftIdentity.IsFs12(key)
                || AircraftIdentity.IsTa30(key) || AircraftIdentity.IsVt7(key)
                || AircraftIdentity.IsFs20(key))
                return 1;
            if (AircraftIdentity.IsA19(key) || AircraftIdentity.IsKr67(key)
                || AircraftIdentity.IsEw25(key) || AircraftIdentity.IsUh90(key)
                || AircraftIdentity.IsSah46(key))
                return 2;
            if (AircraftIdentity.IsSfb(key) || AircraftIdentity.IsVl49(key)
                || AircraftIdentity.IsAb4(key))
                return 4;
            return 0;
        }

        private static int ExpectedVanillaBanks(Aircraft ac)
        {
            if (ac == null)
                return 0;
            string key = "unknown";
            try { key = AircraftIdentity.GetKey(ac); }
            catch { return 0; }
            return ExpectedEngineCount(key);
        }

        private static void CacheNozzleProps()
        {
            for (int i = 0; i < 8; i++)
                _nozProp0[i] = 1f;
            if (_nozzles == null)
                return;
            int n = _nozzles.Length;
            if (n > 8)
                n = 8;
            for (int i = 0; i < n; i++)
            {
                float p = ReadFloat(_nozzles[i], JetThrustProp, 1f);
                if (p < 0.01f)
                    p = 1f;
                _nozProp0[i] = p;
            }
        }

        private static int NameAltKind(Component c)
        {
            if (c == null)
                return 0;
            string n = "";
            try { n = c.gameObject != null ? c.gameObject.name : ""; }
            catch { }
            string p = "";
            try
            {
                if (c.transform != null && c.transform.parent != null)
                    p = c.transform.parent.name;
            }
            catch { }
            string s = (n + " " + p);
            if (ContainsAltToken(s, "HighAlt") || ContainsAltToken(s, "High-Alt")
                || ContainsAltToken(s, "HiAlt") || ContainsAltToken(s, "HAEngine")
                || s.IndexOf("高空") >= 0)
                return 2;
            if (ContainsAltToken(s, "LowAlt") || ContainsAltToken(s, "Low-Alt")
                || s.IndexOf("低空") >= 0)
                return 1;
            return 0;
        }

        private static bool ContainsAltToken(string hay, string needle)
        {
            if (string.IsNullOrEmpty(hay) || string.IsNullOrEmpty(needle))
                return false;
            return hay.IndexOf(needle, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static float ReadMinDensity(Component eng)
        {
            if (eng == null)
                return -1f;
            FieldInfo f = null;
            if (eng is Turbojet)
                f = TurbojetMinDensity;
            else if (eng is Turbofan)
                f = TurbofanMinDensity;
            if (f == null)
                return -1f;
            try
            {
                object v = f.GetValue(eng);
                if (v is float)
                    return (float)v;
            }
            catch { }
            return -1f;
        }

        private static void SyncVanillaIntoParts()
        {
            for (int b = 0; b < _banks; b++)
                _vanilla[b] = 1f;
            for (int i = 0; i < _plants.Count && i < _banks; i++)
            {
                if (_mixedAlt && !BankAltitudeActive(i))
                    _vanilla[i] = 1f;
                else
                    _vanilla[i] = SampleVanilla(_plants[i]);
            }

            float shared = 1f;
            for (int b = 0; b < _banks; b++)
            {
                if (_vanilla[b] < shared)
                    shared = _vanilla[b];
            }

            for (int i = 0; i < _parts.Count; i++)
            {
                Part p = _parts[i];
                float v = p.Bank >= 0 && p.Bank < _vanilla.Length
                    ? _vanilla[p.Bank]
                    : shared;
                if (v >= 0.995f)
                {
                    if (p.Ceiling >= 0.995f && p.Health <= 0.125f && p.Health >= 0.115f)
                    {
                        p.Health = p.Ceiling;
                        _parts[i] = p;
                    }
                    continue;
                }
                if (p.Health <= v + 0.001f)
                    continue;
                float before = p.Health;
                p.Health = v;
                if (p.Health > p.Ceiling)
                    p.Health = p.Ceiling;
                _parts[i] = p;
                NoteHurtIfDropped(i, before, p.Health);
            }
        }

        private static float SampleVanilla(Component eng)
        {
            if (eng == null)
                return 1f;
            float h = 1f;

            Turbojet tj = eng as Turbojet;
            if (tj != null)
            {
                try
                {
                    if (tj.engineFire)
                        h = Mathf.Min(h, 0.22f);
                }
                catch { }
                h = Mathf.Min(h, SampleCriticalParts(tj, TurbojetCritical));
                h = Mathf.Min(h, SampleHostPart(tj));
                return Mathf.Clamp01(h);
            }

            Turbofan tf = eng as Turbofan;
            if (tf != null)
            {
                float hp = ReadFloat(tf, TurbofanHitPoints, 100f);
                if (hp > 1f && hp < 99f)
                    h = Mathf.Min(h, Mathf.Clamp01(hp / 100f));
                h = Mathf.Min(h, SampleHostPart(tf));
                return Mathf.Clamp01(h);
            }

            TurbineEngine te = eng as TurbineEngine;
            if (te != null)
            {
                h = Mathf.Min(h, SampleCriticalParts(te, TurbineCritical));
                h = Mathf.Min(h, SampleHostPart(te));
                return Mathf.Clamp01(h);
            }

            DuctedFan df = eng as DuctedFan;
            if (df != null)
            {
                h = Mathf.Min(h, SampleHostPart(df));
                return Mathf.Clamp01(h);
            }

            PropFan pf = eng as PropFan;
            if (pf != null)
            {
                h = Mathf.Min(h, SampleHostPart(pf));
                return Mathf.Clamp01(h);
            }

            ConstantSpeedProp prop = eng as ConstantSpeedProp;
            if (prop != null)
            {
                h = Mathf.Min(h, SampleHostPart(prop));
                return Mathf.Clamp01(h);
            }

            RotorShaft rotor = eng as RotorShaft;
            if (rotor != null)
            {
                if (ReadBool(rotor, RotorDetached, false))
                    h = Mathf.Min(h, 0.08f);
                h = Mathf.Min(h, SampleHostPart(rotor));
            }
            return Mathf.Clamp01(h);
        }

        private static float SampleCriticalParts(object eng, FieldInfo field)
        {
            if (eng == null || field == null)
                return 1f;
            try
            {
                UnitPart[] parts = field.GetValue(eng) as UnitPart[];
                if (parts == null || parts.Length == 0)
                    return 1f;
                float worst = 1f;
                for (int i = 0; i < parts.Length; i++)
                {
                    float h = PartHealth01(parts[i]);
                    if (h < worst)
                        worst = h;
                }
                return worst;
            }
            catch
            {
                return 1f;
            }
        }

        private static float SampleHostPart(Component eng)
        {
            if (eng == null)
                return 1f;
            UnitPart part = null;
            try { part = eng.GetComponent<UnitPart>(); }
            catch { }
            if (part == null)
            {
                try { part = eng.GetComponentInParent<UnitPart>(); }
                catch { }
            }
            return PartHealth01(part);
        }

        private static float PartHealth01(UnitPart part)
        {
            if (part == null)
                return 1f;
            try
            {
                if (ReadBool(part, PartDetached, false))
                    return 0.05f;
            }
            catch { }
            float hp = 100f;
            try { hp = part.hitPoints; }
            catch { return 1f; }
            if (hp <= 0.01f)
                return 0.05f;
            if (hp >= 99.5f)
                return 1f;
            return Mathf.Clamp01(hp / 100f);
        }

        private static bool ReadBool(object obj, FieldInfo field, bool fallback)
        {
            if (obj == null || field == null)
                return fallback;
            try
            {
                object v = field.GetValue(obj);
                if (v is bool)
                    return (bool)v;
            }
            catch { }
            return fallback;
        }

        private static float ReadFloat(object obj, FieldInfo field, float fallback)
        {
            if (obj == null || field == null)
                return fallback;
            try
            {
                object v = field.GetValue(obj);
                if (v is float)
                    return (float)v;
            }
            catch { }
            return fallback;
        }

        private static void BuildLayout()
        {
            if (AircraftIdentity.IsUh90(_acKey) || AircraftIdentity.IsSah46(_acKey)
                || _family == Family.Helo)
                LayoutHelo();
            else if (AircraftIdentity.IsVl49(_acKey) || _family == Family.Tilt)
                LayoutTilt();
            else if (AircraftIdentity.IsEw25(_acKey) || _family == Family.Stovl)
                LayoutStovl();
            else if (AircraftIdentity.IsFs20(_acKey) || AircraftIdentity.IsVt7(_acKey)
                || _family == Family.Vtol)
                LayoutVtolJet(AircraftIdentity.IsFs20(_acKey) || _hasAbHw);
            else if (AircraftIdentity.IsCi22(_acKey))
                LayoutPiston();
            else if (AircraftIdentity.IsA19(_acKey))
                LayoutPropFan();
            else if (_family == Family.Prop)
                LayoutPiston();
            else
            {
                bool ab = _hasAbHw
                    || AircraftIdentity.HasFleetAfterburner(_acKey)
                    || _family == Family.JetAb;
                LayoutJet(ab);
            }
            AddApu();
        }

        private static void LayoutHelo()
        {
            for (int b = 0; b < _banks; b++)
            {
                AddE(b, "shaft", "Turboshaft", "涡轴", false, false, true);
                AddE(b, "inject", "Fuel injection", "注油系统", true, false, true);
                AddE(b, "oil", "Oil system", "滑油系统", true, true, true);
            }
            AddS("mgb", "Main gearbox", "主减速器", true, true, true);
            AddS("tgb", "Tail drive", "尾桨传动", true, true, false);
            AddS("swash", "Swashplate", "自动倾斜器", true, true, false);
            AddS("mast", "Mast / mounts", "桨毂安装", true, true, false);
        }

        private static void LayoutTilt()
        {
            for (int b = 0; b < _banks; b++)
            {
                AddE(b, "shaft", "Nacelle engine", "短舱动力", false, false, true);
                AddE(b, "rotor", "Proprotor", "倾转旋翼", false, true, false);
                AddE(b, "tilt", "Tilt actuator", "倾转作动", true, true, false);
                AddE(b, "inject", "Fuel injection", "注油系统", true, false, true);
                AddE(b, "oil", "Oil system", "滑油系统", true, true, true);
            }
            AddS("combine", "Thrust combiner", "推力统合系统", true, true, true);
            AddS("mounts", "Airframe mounts", "机体安装节", true, true, false);
        }

        private static void LayoutStovl()
        {
            for (int b = 0; b < _banks; b++)
            {
                AddE(b, "intake", "Intake", "进气道", false, true, false);
                AddE(b, "core", "Core", "核心机", false, false, true);
                AddE(b, "turbine", "Turbine", "涡轮", false, false, true);
                AddE(b, "vector", "Vectoring nozzle", "矢量喷口", false, false, true);
                AddE(b, "inject", "Fuel injection", "注油系统", true, false, true);
                AddE(b, "oil", "Oil system", "滑油系统", true, true, true);
                AddE(b, "mounts", "Engine mounts", "发动机安装节", true, true, false);
            }
            AddS("lift", "Lift fan / hover jet", "升力风扇 / 悬停喷流", true, true, false);
            AddS("bleed", "Bleed duct", "引气管道", true, false, true);
            AddS("doors", "Lift doors", "升力舱门", true, true, false);
        }

        private static void LayoutVtolJet(bool ab)
        {
            for (int b = 0; b < _banks; b++)
            {
                AddE(b, "intake", "Intake", "进气道", true, true, false);
                AddE(b, "compressor", "Compressor", "压气机", false, false, false);
                if (ab)
                {
                    AddE(b, "combustor", "Combustor", "燃烧室", false, false, true);
                    AddE(b, "turbine", "Turbine", "涡轮", false, false, true);
                    AddE(b, "ab", "Afterburner", "加力燃烧室", false, false, true);
                }
                else
                {
                    AddE(b, "core", "Core", "核心机", false, false, true);
                    AddE(b, "turbine", "Turbine", "涡轮", false, false, true);
                }
                AddE(b, "nozzle", "Nozzle", "喷口", false, false, true);
                AddE(b, "inject", "Fuel injection", "注油系统", true, false, true);
                AddE(b, "oil", "Oil system", "滑油系统", true, true, true);
                AddE(b, "lift", "Lift system", "升力系统", true, true, false);
                AddE(b, "mounts", "Engine mounts", "发动机安装节", true, true, false);
            }
        }

        private static void LayoutPiston()
        {
            for (int b = 0; b < _banks; b++)
            {
                AddE(b, "crank", "Crankcase", "发动机机体", false, false, true);
                AddE(b, "magneto", "Magneto", "磁电机", true, false, true);
                AddE(b, "carb", "Carburetor", "化油器", true, false, true);
                AddE(b, "turbo", "Turbocharger", "涡轮增压器", false, false, true);
                AddE(b, "waste", "Wastegate", "废气门", true, false, true);
                AddE(b, "inter", "Intercooler", "中冷器", true, true, true);
                AddE(b, "radiator", "Radiator", "散热器", true, true, true);
                AddE(b, "exhaust", "Exhaust", "排气", true, false, true);
                AddE(b, "oil", "Oil cooler", "滑油散热器", true, true, true);
                AddE(b, "mounts", "Engine mounts", "发动机安装节", true, true, false);
                AddP(b, "blades", "Prop blades", "桨叶", false, true, false);
                AddP(b, "hub", "Prop hub", "桨毂", false, true, false);
                AddP(b, "gov", "Pitch governor", "变距调速器", true, true, false);
                AddP(b, "spin", "Spinner", "桨帽", true, true, false);
            }
        }

        private static void LayoutPropFan()
        {
            for (int b = 0; b < _banks; b++)
            {
                AddE(b, "pfan", "Propulsor fan", "桨扇", false, false, false);
                AddE(b, "core", "Gas generator", "燃气发生器", false, false, true);
                AddE(b, "gear", "Reduction gearbox", "减速齿轮箱", true, true, true);
                AddE(b, "inject", "Fuel injection", "注油系统", true, false, true);
                AddE(b, "oil", "Oil system", "滑油系统", true, true, true);
                AddE(b, "exhaust", "Exhaust", "排气", true, false, true);
                AddE(b, "mounts", "Engine mounts", "发动机安装节", true, true, false);
                AddP(b, "blades", "Fan blades", "桨扇叶片", false, true, false);
                AddP(b, "hub", "Hub", "桨毂", false, true, false);
                AddP(b, "gov", "Pitch governor", "变距调速器", true, true, false);
            }
        }

        private static void LayoutJet(bool ab)
        {
            for (int b = 0; b < _banks; b++)
            {
                AddE(b, "intake", "Intake", "进气道", true, true, false);
                AddE(b, "compressor", "Compressor", "压气机", false, false, false);
                if (ab)
                {
                    AddE(b, "combustor", "Combustor", "燃烧室", false, false, true);
                    AddE(b, "turbine", "Turbine", "涡轮", false, false, true);
                    AddE(b, "ab", "Afterburner", "加力燃烧室", false, false, true);
                }
                else
                {
                    AddE(b, "core", "Core", "核心机", false, false, true);
                    AddE(b, "turbine", "Turbine", "涡轮", false, false, true);
                }
                AddE(b, "nozzle", "Nozzle", "喷口", false, false, true);
                AddE(b, "inject", "Fuel injection", "注油系统", true, false, true);
                AddE(b, "oil", "Oil system", "滑油系统", true, true, true);
                AddE(b, "mounts", "Engine mounts", "发动机安装节", true, true, false);
            }
        }

        private static void AddE(int bank, string id, string en, string zh, bool sec, bool g, bool ab)
        {
            AddPart("e" + bank + "." + id, en, zh, bank, sec, false, g, ab);
        }

        private static void AddP(int bank, string id, string en, string zh, bool sec, bool g, bool ab)
        {
            AddPart("p" + bank + "." + id, en, zh, bank, sec, true, g, ab);
        }

        private static void AddS(string id, string en, string zh, bool sec, bool g, bool ab)
        {
            AddPart("s." + id, en, zh, -1, sec, false, g, ab);
        }

        private static void AddApu()
        {
            AddS("apu", "APU", "APU", true, true, false);
        }

        private static void AddPart(string id, string en, string zh, int bank, bool sec, bool prop, bool g, bool ab)
        {
            Part p = new Part();
            p.Id = id;
            p.En = en;
            p.Zh = zh;
            p.Bank = bank;
            p.Secondary = sec;
            p.Prop = prop;
            p.Health = 1f;
            p.Ceiling = 1f;
            p.GSensitive = g;
            p.AbSensitive = ab;
            p.WearMul = WearMulOf(id);
            _parts.Add(p);
        }

        private static void ApplyPilotDrain(float g, float dt, float gGate)
        {
            if (g <= gGate)
                return;
            float over = g - gGate;
            float rate = 0.004f * over;
            if (g > gGate + 3f)
                rate *= 2.2f;
            _pilot = Mathf.Max(0f, _pilot - rate * dt);
        }

        private static float SecondaryStress()
        {
            float worst = 1f;
            int n = 0;
            float sum = 0f;
            for (int i = 0; i < _parts.Count; i++)
            {
                if (!_parts[i].Secondary)
                    continue;
                n++;
                sum += _parts[i].Health;
                if (_parts[i].Health < worst)
                    worst = _parts[i].Health;
            }
            if (n <= 0)
                return 0f;
            float avg = sum / n;
            return Mathf.Clamp01((1f - avg) * 0.65f + (1f - worst) * 0.35f);
        }

        private static void ApplyPartDrain(float g, bool afterburner, float dt)
        {
            float gRate = 0f;
            if (g > _partGGate && _gHold > 2f)
                gRate = 0.00018f * (g - _partGGate);
            float abRate = 0f;
            if (afterburner && _abHold > AbWearAfter)
            {
                float over = _abHold - AbWearAfter;
                if (over > 300f)
                    over = 300f;
                abRate = 0.00022f + 0.000012f * over;
            }
            float stress = SecondaryStress();
            float mul = (1f + stress * 0.55f) * MaterialWearMul();
            float rad = SuffixHealth("radiator");
            if (rad < 0.85f)
                mul *= 1f + (0.85f - rad) * 0.6f;
            if (gRate <= 0f && abRate <= 0f)
                return;
            for (int i = 0; i < _parts.Count; i++)
            {
                Part p = _parts[i];
                float d = 0f;
                if (p.GSensitive)
                    d += gRate;
                if (p.AbSensitive)
                    d += abRate;
                if (d <= 0f)
                    continue;
                float before = p.Health;
                p.Health = Mathf.Max(0.02f, p.Health - d * mul * p.WearMul * WearRateMul * dt);
                if (p.Health > p.Ceiling)
                    p.Health = p.Ceiling;
                _parts[i] = p;
                NoteHurtIfDropped(i, before, p.Health);
            }
        }

        private static void BindTemperature(Aircraft ac)
        {
            _hasAbHw = AircraftIdentity.HasFleetAfterburner(_acKey) || HardwareHasAfterburner(ac);
            _ratedThrottle = _hasAbHw ? 1f : 0.92f;
            _ratedC = RatedTempFor(ac);
            _oilSoak = 0f;
            ResetTempsToIdle();
        }

        private static float RatedTempFor(Aircraft ac)
        {
            string key = "unknown";
            try { key = AircraftIdentity.GetKey(ac); }
            catch { }
            if (AircraftIdentity.IsTa30(key))
                return 740f;
            if (AircraftIdentity.IsCi22(key) || AircraftIdentity.IsA19(key))
                return 268f;
            if (AircraftIdentity.IsUh90(key) || AircraftIdentity.IsSah46(key))
                return 820f;
            if (AircraftIdentity.IsVl49(key))
                return 890f;
            if (AircraftIdentity.IsVt7(key))
                return 1040f;
            if (AircraftIdentity.IsSfb(key) || AircraftIdentity.IsAb4(key))
                return 960f;
            if (AircraftIdentity.IsKr67(key) || AircraftIdentity.IsEw25(key))
                return 1010f;
            if (AircraftIdentity.IsFs12(key) || AircraftIdentity.IsFs20(key))
                return 980f;
            switch (_family)
            {
                case Family.Prop:
                    return 268f;
                case Family.Helo:
                    return 820f;
                case Family.JetDry:
                    return 740f;
                case Family.Vtol:
                    return 890f;
                case Family.Tilt:
                    return 860f;
                case Family.Stovl:
                    return 1040f;
                case Family.Bomber:
                    return 960f;
                default:
                    return 980f;
            }
        }

        private static void ResetTempsToIdle()
        {
            for (int i = 0; i < 8; i++)
            {
                _coreTemp[i] = 0.64f;
                _oilTemp[i] = 0.60f;
                _abTemp[i] = _hasAbHw ? 0.42f : 0.32f;
            }
            _oilSoak = 0f;
            _corePrev = 0.64f;
            _tempDir = 0;
            _tempTravel = 0f;
        }

        private static void TickTemperature(Aircraft ac, bool afterburner, float dt)
        {
            float th = ReadThrottle01(ac);
            ApplyThrottleSlam(th, dt);
            float ratedTh = _ratedThrottle;
            if (ratedTh < 0.5f)
                ratedTh = 0.92f;
            float idle = 0.64f;
            float tgt;
            if (th <= ratedTh)
                tgt = Mathf.Lerp(idle, 1f, th / ratedTh);
            else
                tgt = 1f + (th - ratedTh) / Mathf.Max(0.02f, 1f - ratedTh) * 0.20f;
            if (afterburner)
                tgt = Mathf.Max(tgt, 1.08f);

            float spd = 0f;
            try { spd = ac != null ? ac.speed : 0f; }
            catch { spd = 0f; }
            float ram = 0f;
            if (spd > 160f)
                ram = (spd - 160f) / 480f;
            if (ram > 0.5f)
                ram = 0.5f;
            tgt += ram * 0.24f;

            float abTgt = 0.40f;
            if (_hasAbHw)
            {
                abTgt = Mathf.Lerp(0.38f, 0.72f, Mathf.Clamp01(th));
                if (afterburner)
                    abTgt = 1.68f;
                abTgt += ram * 0.14f;
            }

            float coolMul = CoolingRateMul();
            float oilTgtBase = Mathf.Max(0.58f, tgt * 0.97f);
            if (th > 0.38f)
                _oilSoak += dt * Mathf.InverseLerp(0.38f, ratedTh, th);
            else
                _oilSoak = Mathf.Max(0f, _oilSoak - dt * 0.18f * coolMul);
            if (_oilSoak > 2800f)
                _oilSoak = 2800f;
            float oilTgt = oilTgtBase + _oilSoak * 0.000038f + ram * 0.10f;
            float rad = SuffixHealth("radiator");
            if (HasSuffix("radiator") && rad < 0.88f)
                oilTgt *= 1f + (0.88f - rad) * 0.55f;
            else if (coolMul > 1f)
                oilTgt *= 1f - (coolMul - 1f) * 0.08f;

            for (int b = 0; b < _banks && b < 8; b++)
            {
                float heat = tgt > _coreTemp[b]
                    ? (tgt > 1f ? 0.011f : 0.038f)
                    : 0.050f * coolMul;
                _coreTemp[b] = Mathf.MoveTowards(_coreTemp[b], tgt, heat * dt);
                float abHeat = abTgt > _abTemp[b]
                    ? 0.065f
                    : 0.040f * Mathf.Lerp(1f, coolMul, 0.45f);
                _abTemp[b] = Mathf.MoveTowards(_abTemp[b], abTgt, abHeat * dt);
                float oilHeat = oilTgt > _oilTemp[b]
                    ? 0.018f
                    : 0.028f * coolMul;
                _oilTemp[b] = Mathf.MoveTowards(_oilTemp[b], oilTgt, oilHeat * dt);
            }
            ApplyThermalCycle();
        }

        private static float CoolingRateMul()
        {
            bool rad = HasSuffix("radiator");
            bool inter = HasSuffix("inter");
            if (!rad && !inter)
                return 1f;
            float h = 1f;
            if (rad)
                h = Mathf.Min(h, SuffixHealth("radiator"));
            if (inter)
                h = Mathf.Min(h, SuffixHealth("inter"));
            return Mathf.Lerp(0.70f, 1.95f, h);
        }

        private static void ApplyThrottleSlam(float th, float dt)
        {
            if (!_thInited)
            {
                _thPrev = th;
                _thInited = true;
                return;
            }
            float dth = Mathf.Abs(th - _thPrev);
            _thPrev = th;
            if (dth < 0.04f)
                return;
            float rate = dth / Mathf.Max(dt, 0.0001f);
            if (rate < 1.35f)
                return;
            float span = Mathf.InverseLerp(1.35f, 7f, rate);
            float hit = (dth - 0.03f) * span * MaterialWearMul();
            DrainSuffix("compressor", hit * 0.0075f);
            DrainSuffix("turbo", hit * 0.0045f);
        }

        private static void ApplyThermalCycle()
        {
            float core = Hottest(_coreTemp);
            float dT = core - _corePrev;
            _corePrev = core;
            int dir = 0;
            if (dT > 0.0025f)
                dir = 1;
            else if (dT < -0.0025f)
                dir = -1;
            if (dir == 0)
                return;
            if (_tempDir != 0 && dir != _tempDir)
            {
                if (_tempTravel > 0.05f)
                {
                    float mat = MaterialWearMul();
                    float pulse = _tempTravel;
                    if (pulse > 0.55f)
                        pulse = 0.55f;
                    DrainSuffix("radiator", pulse * 0.011f * mat);
                    DrainSuffix("inter", pulse * 0.009f * mat);
                    DrainSuffix("oil", pulse * 0.005f * mat);
                }
                _tempTravel = 0f;
            }
            _tempDir = dir;
            _tempTravel += Mathf.Abs(dT);
            if (_tempTravel > 1.2f)
                _tempTravel = 1.2f;
        }

        private static void ApplyHeatDrain(float dt)
        {
            float mat = MaterialWearMul();
            float core = Hottest(_coreTemp);
            float heat = core - 1.04f;
            if (heat > 0f)
            {
                float r = 0.00030f * heat * mat;
                DrainSuffix("combustor", r * dt * 1.45f);
                DrainSuffix("inject", r * dt * 1.30f);
                DrainSuffix("carb", r * dt * 1.25f);
                DrainSuffix("oil", r * dt * 1.20f);
                DrainSuffix("turbine", r * dt * 1.35f);
                DrainSuffix("compressor", r * dt * 1.05f);
                DrainSuffix("core", r * dt * 1.10f);
                DrainSuffix("shaft", r * dt * 0.90f);
                DrainSuffix("nozzle", r * dt * 0.90f);
                DrainSuffix("exhaust", r * dt * 0.95f);
                DrainSuffix("ab", r * dt * 1.15f);
                DrainSuffix("inter", r * dt * 0.85f);
            }
            float oil = Hottest(_oilTemp);
            if (oil > 1.06f)
            {
                float r = 0.00032f * (oil - 1.06f) * mat;
                DrainSuffix("oil", r * dt);
                DrainSuffix("inject", r * dt * 0.55f);
            }
            float ab = Hottest(_abTemp);
            if (_hasAbHw && ab > 1.38f)
            {
                float r = 0.00012f * (ab - 1.38f) * mat;
                DrainSuffix("ab", r * dt);
                DrainSuffix("nozzle", r * dt * 0.7f);
            }
        }

        private static void ApplyMountManeuver(Aircraft ac, float g, float dt)
        {
            float overG = g - 1.25f;
            if (overG < 0f)
                overG = 0f;
            float man = 0f;
            if (overG > 0f)
                man = 0.0000065f * Mathf.Pow(overG, 1.12f);
            float spin = 0f;
            try
            {
                if (ac != null && ac.rb != null)
                    spin = ac.rb.angularVelocity.magnitude * Mathf.Rad2Deg;
            }
            catch { }
            if (spin > 22f)
                man += 0.0000035f * Mathf.Clamp01((spin - 22f) / 90f);
            if (man <= 0f)
                return;
            float mat = MaterialWearMul();
            DrainSuffix("mounts", man * mat * dt);
            DrainSuffix("mast", man * 0.85f * mat * dt);
        }

        private static void DrainSuffix(string suffix, float amount)
        {
            if (amount <= 0.0000001f)
                return;
            for (int i = 0; i < _parts.Count; i++)
            {
                if (!IdSuffix(_parts[i].Id, suffix))
                    continue;
                Part p = _parts[i];
                float before = p.Health;
                p.Health = Mathf.Max(0.02f, p.Health - amount * p.WearMul * WearRateMul);
                if (p.Health > p.Ceiling)
                    p.Health = p.Ceiling;
                _parts[i] = p;
                NoteHurtIfDropped(i, before, p.Health);
            }
        }

        private static float Hottest(float[] arr)
        {
            float m = 0f;
            int n = _banks;
            if (n < 1)
                n = 1;
            if (n > 8)
                n = 8;
            for (int i = 0; i < n; i++)
            {
                if (arr[i] > m)
                    m = arr[i];
            }
            return m;
        }

        private static float ReadThrottle01(Aircraft ac)
        {
            if (ac == null)
                return 0f;
            try
            {
                ControlInputs ci = ac.GetInputs();
                if (ci == null)
                    return 0f;
                return Mathf.Clamp01(ci.throttle);
            }
            catch
            {
                return 0f;
            }
        }

        private static void TickFlicker(float dt)
        {
            float stress = SecondaryStress();
            for (int b = 0; b < _banks && b < 8; b++)
            {
                float h = BankHealthRaw(b);
                if (_fuelCut[b])
                {
                    _flickerMulBank[b] = 0.02f;
                    continue;
                }
                float dmg = 1f - h;
                if (dmg < stress)
                    dmg = stress;
                if (dmg < 0.03f)
                {
                    _flickerMulBank[b] = 1f;
                    continue;
                }
                if (Time.unscaledTime < _flickerAtBank[b])
                    continue;
                float t = Mathf.InverseLerp(0.03f, 0.88f, dmg);
                float amp = Mathf.Lerp(0.04f, 0.70f, Mathf.Pow(t, 1.1f));
                _flickerMulBank[b] = 1f - Random.Range(amp * 0.25f, amp);
                _flickerAtBank[b] = Time.unscaledTime + Random.Range(0.08f, 0.40f);
            }
            _flickerMul = 1f;
            for (int b = 0; b < _banks && b < 8; b++)
            {
                if (_flickerMulBank[b] < _flickerMul)
                    _flickerMul = _flickerMulBank[b];
            }
        }

        private static void TickFlickerAbVisual()
        {
            if (!Flickering())
            {
                _flickerAbI = -1;
                _flickerAbs.Clear();
                return;
            }
            EnsureAbReflect();
            RebuildFlickerAbs();
            if (_flickerAbs.Count <= 0)
                return;
            if (Time.unscaledTime < _flickerAbAt)
                return;
            _flickerAbAt = Time.unscaledTime + Random.Range(0.10f, 0.28f);
            int n = _flickerAbs.Count;
            int next;
            if (_flickerAbI < 0)
                next = Random.Range(0, n);
            else
                next = (_flickerAbI + 1) % n;
            if (_flickerAbI >= 0 && _flickerAbI < n)
                WriteFloat(_flickerAbs[_flickerAbI], AfterburnerAmountField, 0f);
            _flickerAbI = next;
        }

        private static void RebuildFlickerAbs()
        {
            _flickerAbs.Clear();
            if (_nozzles == null || JetAfterburnersField == null)
                return;
            try
            {
                for (int i = 0; i < _nozzles.Length; i++)
                {
                    if (_nozzles[i] == null)
                        continue;
                    object arrObj = JetAfterburnersField.GetValue(_nozzles[i]);
                    System.Array arr = arrObj as System.Array;
                    if (arr == null)
                        continue;
                    for (int j = 0; j < arr.Length; j++)
                    {
                        object ab = arr.GetValue(j);
                        if (ab != null)
                            _flickerAbs.Add(ab);
                    }
                }
            }
            catch { }
        }

        internal static bool IsActiveFlickerAb(object ab)
        {
            if (ab == null)
                return true;
            if (!Flickering())
                return true;
            if (_flickerAbs.Count <= 0)
                return true;
            bool ours = false;
            for (int i = 0; i < _flickerAbs.Count; i++)
            {
                if (object.ReferenceEquals(_flickerAbs[i], ab))
                {
                    ours = true;
                    break;
                }
            }
            if (!ours)
                return true;
            if (_flickerAbI < 0 || _flickerAbI >= _flickerAbs.Count)
                return false;
            return object.ReferenceEquals(_flickerAbs[_flickerAbI], ab);
        }

        private static void ApplyFuelCutoff()
        {
            for (int b = 0; b < _banks && b < _plants.Count && b < 8; b++)
            {
                float inj = BankSuffixHealth(b, "inject");
                float carb = BankSuffixHealth(b, "carb");
                float fuel = inj;
                if (carb < fuel)
                    fuel = carb;
                bool cut = _fuelCut[b];
                if (!cut && fuel < 0.20f)
                    cut = true;
                else if (cut && fuel > 0.30f)
                    cut = false;
                if (cut && !_fuelCut[b])
                {
                    _hudFlashText = UiLang.T(
                        "Fuel injection  CUTOFF",
                        "注油系统  断油");
                    _hudFlashesLeft = HudFlashTimes;
                    _hudFlashOn = true;
                    _hudFlashAt = Time.unscaledTime + HudFlashOn;
                }
                bool was = _fuelCut[b];
                _fuelCut[b] = cut;
                bool run = !cut;
                if (HasSeparateHiPlants() && !BankAltitudeActive(b))
                    run = false;
                if (!run)
                    WriteFuel(_plants[b], false);
                else if (was)
                    WriteFuel(_plants[b], true);
            }
        }

        internal static void EnforceFuelCut(Component eng)
        {
            if (eng == null)
                return;
            int b = PlantIndex(eng);
            if (b < 0)
                return;
            bool cut = b < 8 && _fuelCut[b];
            if (HasSeparateHiPlants() && !BankAltitudeActive(b))
                cut = true;
            if (cut)
                WriteFuel(eng, false);
        }

        private static float BankSuffixHealth(int bank, string suffix)
        {
            float h = 1f;
            bool any = false;
            for (int i = 0; i < _parts.Count; i++)
            {
                if (_parts[i].Bank != bank)
                    continue;
                if (!IdSuffix(_parts[i].Id, suffix))
                    continue;
                any = true;
                if (_parts[i].Health < h)
                    h = _parts[i].Health;
            }
            if (!any)
                return 1f;
            return h;
        }

        private static void WriteFuel(Component eng, bool hasFuel)
        {
            if (eng == null)
                return;
            if (eng is Turbojet)
            {
                WriteBool(eng, TurbojetHasFuel, hasFuel);
                return;
            }
            if (eng is Turbofan)
            {
                WriteBool(eng, TurbofanHasFuel, hasFuel);
                return;
            }
            if (eng is TurbineEngine)
                WriteBool(eng, TurbineHasFuel, hasFuel);
        }

        private static void WriteBool(object obj, FieldInfo field, bool value)
        {
            if (obj == null || field == null)
                return;
            try { field.SetValue(obj, value); }
            catch { }
        }

        private static void TickCascade(float dt)
        {
            if (Time.unscaledTime < _cascadeAt)
                return;
            _cascadeAt = Time.unscaledTime + 0.35f;
            float stress = SecondaryStress();
            if (stress < 0.08f)
                return;
            if (Random.value > stress * 0.08f)
                return;
            int idx = Random.Range(0, _parts.Count);
            Part p = _parts[idx];
            float before = p.Health;
            float drop = Random.Range(0.002f, 0.008f) * p.WearMul * MaterialWearMul() * WearRateMul;
            p.Health = Mathf.Max(0.02f, p.Health - drop);
            if (p.Health > p.Ceiling)
                p.Health = p.Ceiling;
            _parts[idx] = p;
            if (before - p.Health > 0.001f)
                NoteHurt(idx, before - p.Health);
        }

        private static void HurtMatching(bool g, bool ab, float amount)
        {
            for (int i = 0; i < _parts.Count; i++)
            {
                Part p = _parts[i];
                if (!((g && p.GSensitive) || (ab && p.AbSensitive)))
                    continue;
                float before = p.Health;
                p.Health = Mathf.Max(0.02f, p.Health - amount * WearRateMul);
                _parts[i] = p;
                NoteHurtIfDropped(i, before, p.Health);
            }
            FlushHurt();
        }

        private static float BankHealthRaw(int bank)
        {
            float m = 1f;
            for (int i = 0; i < _parts.Count; i++)
            {
                Part p = _parts[i];
                if (p.Bank != bank && p.Bank != -1)
                    continue;
                if (p.Secondary)
                    continue;
                if (p.Health < m)
                    m = p.Health;
            }
            float oil = 1f;
            for (int i = 0; i < _parts.Count; i++)
            {
                if (_parts[i].Bank != bank && _parts[i].Bank != -1)
                    continue;
                if (!IdSuffix(_parts[i].Id, "oil"))
                    continue;
                if (_parts[i].Health < oil)
                    oil = _parts[i].Health;
            }
            float f = Mathf.Min(m, Mathf.Lerp(1f, oil, 0.45f));
            if (HasSuffix("combine"))
            {
                float c = SuffixHealth("combine");
                f *= Mathf.Lerp(0.38f, 1f, c);
            }
            return Mathf.Clamp(f, 0.02f, 1f);
        }

        private static float BankFactor(int bank)
        {
            float f = BankHealthRaw(bank);
            float flicker = 1f;
            if (bank >= 0 && bank < 8)
                flicker = _flickerMulBank[bank];
            f *= flicker;
            if (bank >= 0 && bank < 8 && _fuelCut[bank])
                return 0.02f;
            return Mathf.Clamp(f, 0.02f, 1f);
        }

        private static bool HasSuffix(string suffix)
        {
            for (int i = 0; i < _parts.Count; i++)
            {
                if (IdSuffix(_parts[i].Id, suffix))
                    return true;
            }
            return false;
        }

        private static float SuffixHealth(string suffix)
        {
            float h = 1f;
            bool any = false;
            for (int i = 0; i < _parts.Count; i++)
            {
                if (!IdSuffix(_parts[i].Id, suffix))
                    continue;
                any = true;
                if (_parts[i].Health < h)
                    h = _parts[i].Health;
            }
            if (!any)
                return 1f;
            return h;
        }

        private static bool IdSuffix(string id, string suffix)
        {
            if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(suffix))
                return false;
            int d = id.LastIndexOf('.');
            if (d < 0 || d >= id.Length - 1)
                return id == suffix;
            return id.Substring(d + 1) == suffix;
        }

        private static int HealthBand(float h)
        {
            if (h >= 0.80f)
                return 4;
            if (h >= 0.60f)
                return 3;
            if (h >= 0.40f)
                return 2;
            if (h >= 0.20f)
                return 1;
            return 0;
        }

        private static void NoteHurtIfDropped(int i, float before, float after)
        {
            float drop = before - after;
            if (drop < 0.002f)
                return;
            if (drop >= 0.03f || HealthBand(after) < HealthBand(before))
                NoteHurt(i, drop);
        }

        private static void NoteHurt(int i, float drop)
        {
            if (i < 0 || i >= _parts.Count)
                return;
            if (drop < _pendingDrop)
                return;
            _pendingDrop = drop;
            _pendingHurt = i;
        }

        private static void FlushHurt()
        {
            if (_pendingHurt >= 0 && _pendingHurt < _parts.Count)
                BeginHudFlash(_parts[_pendingHurt]);
            _pendingHurt = -1;
            _pendingDrop = 0f;
        }

        private static void BeginHudFlash(Part p)
        {
            if (LocalPilotGone())
                return;
            string kind = "";
            if (_hitKind == 1)
                kind = UiLang.T("  SHRAPNEL", "  弹片损伤");
            else if (_hitKind == 2)
                kind = UiLang.T("  VIBRATION", "  震动损伤");
            else
                kind = UiLang.T("  DAMAGE", "  损伤");
            BeginHudFlashText(PartLabel(p) + "  " + HealthPct(p) + "%" + kind, HudFlashTimes);
            try
            {
                if (SceneSingleton<AircraftActionsReport>.i != null)
                    SceneSingleton<AircraftActionsReport>.i.ReportText(_hudFlashText, 2.4f);
            }
            catch { }
        }

        private static void BeginHudFlashText(string text, int times)
        {
            if (LocalPilotGone())
                return;
            if (string.IsNullOrEmpty(text))
                return;
            int n = times;
            if (n < 1)
                n = 1;
            _hudFlashText = text;
            _hudFlashesLeft = n;
            _hudFlashOn = true;
            _hudFlashAt = Time.unscaledTime + HudFlashOn;
        }

        private static void TickHudFlash()
        {
            if (_hudFlashesLeft <= 0 && !_hudFlashOn)
                return;
            if (Time.unscaledTime < _hudFlashAt)
                return;
            if (_hudFlashOn)
            {
                _hudFlashOn = false;
                _hudFlashAt = Time.unscaledTime + HudFlashOff;
                _hudFlashesLeft--;
                if (_hudFlashesLeft <= 0)
                    _hudFlashText = null;
                return;
            }
            if (_hudFlashesLeft <= 0)
                return;
            _hudFlashOn = true;
            _hudFlashAt = Time.unscaledTime + HudFlashOn;
        }

        internal static void DrawHud()
        {
            if (_reportSuppressed || LocalPilotGone())
                return;
            if (!_hudFlashOn || string.IsNullOrEmpty(_hudFlashText))
                return;
            if (OritasyPresentation.SplashActive)
                return;
            try
            {
                if (JoinMenuFactionFix.SelectionUiOpen())
                    return;
            }
            catch { }
            if (Event.current != null && Event.current.type != EventType.Repaint)
                return;

            if (_hudStyle == null)
            {
                _hudStyle = new GUIStyle(GUI.skin.label);
                _hudStyle.fontSize = 18;
                _hudStyle.fontStyle = FontStyle.Bold;
                _hudStyle.alignment = TextAnchor.MiddleCenter;
                _hudStyle.normal.textColor = new Color(1f, 0.92f, 0.35f, 1f);
            }

            float w = Mathf.Min(520f, UiScaleService.Width * 0.72f);
            Rect box = new Rect((UiScaleService.Width - w) * 0.5f, UiScaleService.Height * 0.18f, w, 36f);
            int prevDepth = GUI.depth;
            GUI.depth = -997;
            Color prev = GUI.color;
            GUI.color = new Color(0.12f, 0.04f, 0.02f, 0.82f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.45f, 0.12f, 0.98f);
            GUI.DrawTexture(new Rect(box.x, box.y, box.width, 3f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(box.x, box.yMax - 3f, box.width, 3f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(box, _hudFlashText, _hudStyle);
            GUI.color = prev;
            GUI.depth = prevDepth;
        }

        private static void PushToEngines(Aircraft ac)
        {
            if (ac == null)
                return;
            int lo = _loCount;
            if (lo < 1)
                lo = 1;
            bool haveHiPlants = HasSeparateHiPlants();
            bool hi = _hiActive;

            if (haveHiPlants)
            {
                for (int i = 0; i < _plants.Count && i < _banks; i++)
                {
                    bool on = BankAltitudeActive(i);
                    float f = on ? BankFactor(i) : 0.02f;
                    ApplyFactor(_plants[i], f);
                    if (!on || (i < 8 && _fuelCut[i]))
                        WriteFuel(_plants[i], false);
                    else
                        WriteFuel(_plants[i], true);
                }
            }
            else
            {
                for (int i = 0; i < _plants.Count; i++)
                {
                    int slot = i;
                    if (lo > 0)
                        slot = i % lo;
                    int hiBank = slot + lo;
                    float f = BankFactor(slot);
                    if (hi && hiBank < _banks)
                        f = BankFactor(hiBank);
                    ApplyFactor(_plants[i], f);
                }
            }

            if (_nozzles != null)
            {
                int n = _nozzles.Length;
                bool pairNoz = n <= lo && _banks >= lo * 2;
                for (int i = 0; i < n; i++)
                {
                    float f;
                    if (pairNoz)
                    {
                        int hiBank = i + lo;
                        f = BankFactor(i % lo);
                        if (hi && hiBank < _banks)
                            f = BankFactor(hiBank);
                    }
                    else
                    {
                        int b = i;
                        if (_banks > 0)
                            b = i % _banks;
                        f = BankAltitudeActive(b) ? BankFactor(b) : 0.02f;
                    }
                    float baseP = i < 8 ? _nozProp0[i] : 1f;
                    WriteFloat(_nozzles[i], JetThrustProp, baseP * f);
                }
            }
            if (_family == Family.Helo || _family == Family.Tilt)
            {
                try
                {
                    RotorShaft[] rotors = ac.GetComponentsInChildren<RotorShaft>(true);
                    if (rotors != null && rotors.Length > 0)
                    {
                        bool pairR = rotors.Length <= lo && _banks >= lo * 2;
                        for (int i = 0; i < rotors.Length; i++)
                        {
                            float f;
                            if (pairR)
                            {
                                int hiBank = i + lo;
                                f = BankFactor(i % lo);
                                if (hi && hiBank < _banks)
                                    f = BankFactor(hiBank);
                            }
                            else
                            {
                                int b = i;
                                if (_banks > 0)
                                    b = i % _banks;
                                f = BankAltitudeActive(b) ? BankFactor(b) : 0.02f;
                            }
                            WriteFloat(rotors[i], RotorCondition, f);
                        }
                    }
                }
                catch { }
            }
        }

        private static void ApplyFactor(Component eng, float factor)
        {
            if (eng == null)
                return;
            Turbojet tj = eng as Turbojet;
            if (tj != null)
            {
                tj.damageFactor = factor;
                WriteFloat(tj, TurbojetCondition, factor);
                return;
            }
            Turbofan tf = eng as Turbofan;
            if (tf != null)
            {
                WriteFloat(tf, TurbofanCondition, factor);
                return;
            }
            TurbineEngine te = eng as TurbineEngine;
            if (te != null)
            {
                WriteFloat(te, TurbineCondition, factor);
                return;
            }
            PropFan pf = eng as PropFan;
            if (pf != null)
            {
                WriteFloat(pf, PropFanCondition, factor);
                return;
            }
            WriteFloat(eng, RotorCondition, factor);
        }

        private static bool HardwareHasAfterburner(Aircraft ac)
        {
            if (ac == null)
                return false;
            EnsureAbReflect();
            if (JetAfterburnersField == null)
                return false;
            try
            {
                JetNozzle[] nozzles = ac.GetComponentsInChildren<JetNozzle>(true);
                if (nozzles == null)
                    return false;
                for (int i = 0; i < nozzles.Length; i++)
                {
                    if (nozzles[i] == null)
                        continue;
                    object arrObj = JetAfterburnersField.GetValue(nozzles[i]);
                    System.Array arr = arrObj as System.Array;
                    if (arr != null && arr.Length > 0)
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static bool IsAfterburning()
        {
            if (_nozzles == null)
                return false;
            EnsureAbReflect();
            if (JetAfterburnersField == null || AfterburnerAmountField == null)
                return false;
            try
            {
                for (int i = 0; i < _nozzles.Length; i++)
                {
                    if (_nozzles[i] == null)
                        continue;
                    object arrObj = JetAfterburnersField.GetValue(_nozzles[i]);
                    System.Array arr = arrObj as System.Array;
                    if (arr == null)
                        continue;
                    for (int j = 0; j < arr.Length; j++)
                    {
                        object ab = arr.GetValue(j);
                        if (ab == null)
                            continue;
                        object amtObj = AfterburnerAmountField.GetValue(ab);
                        if (amtObj is float && (float)amtObj > 0.12f)
                            return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static void EnsureAbReflect()
        {
            if (_abReflectTried)
                return;
            _abReflectTried = true;
            System.Type nested = AccessTools.Inner(typeof(JetNozzle), "Afterburner");
            if (nested == null)
                return;
            AfterburnerAmountField = AccessTools.Field(nested, "afterburnerAmount");
        }

        internal static void NotifyCombatHit(
            UnitPart part,
            float pierceDamage,
            float blastDamage,
            float amountAffected,
            float impactDamage)
        {
            if (part == null || _parts.Count <= 0)
                return;
            Aircraft local = LocalAircraft();
            if (local == null || LocalPilotGone(local))
                return;
            Unit parent = null;
            try { parent = part.parentUnit; }
            catch { return; }
            if (parent == null || parent != (Unit)local)
                return;

            float blast = blastDamage * Mathf.Max(amountAffected, 0.2f);
            bool missile = blast > 20f && blast > pierceDamage * 1.15f;
            bool gun = pierceDamage > 4f || impactDamage > 6f;
            if (!missile && !gun)
                return;

            float now = Time.unscaledTime;
            if (now < _hitAt)
                return;
            _hitAt = now + (missile ? 0.45f : 0.4f);

            bool shrapnel = Random.value < 0.5f;
            _hitKind = shrapnel ? 1 : 2;
            float mat = MaterialWearMul();
            float scale = missile ? 1f : 0.55f;
            if (shrapnel)
                ApplyShrapnel(scale * mat);
            else
                ApplyVibration(scale * mat);
            FlushHurt();
            _hitKind = 0;
        }

        private static Aircraft LocalAircraft()
        {
            try
            {
                Aircraft ac;
                if (!GameManager.GetLocalAircraft(out ac))
                    return null;
                return ac;
            }
            catch
            {
                return null;
            }
        }

        internal static bool LocalPilotGone()
        {
            return LocalPilotGone(LocalAircraft());
        }

        internal static bool LocalPilotGone(Aircraft ac)
        {
            if (ac == null)
                return true;
            try
            {
                if (ac.disabled)
                    return true;
            }
            catch { }
            try
            {
                if (ac.pilots == null || ac.pilots.Length <= 0)
                    return false;
                bool sawPlayer = false;
                for (int i = 0; i < ac.pilots.Length; i++)
                {
                    Pilot pl = ac.pilots[i];
                    if (pl == null)
                        continue;
                    if (!pl.playerControlled)
                        continue;
                    sawPlayer = true;
                    if (pl.dead || pl.ejected)
                        return true;
                }
                if (!sawPlayer)
                {
                    Pilot first = ac.pilots[0];
                    if (first != null && (first.dead || first.ejected))
                        return true;
                }
            }
            catch { }
            return false;
        }

        private static void SuppressReports()
        {
            _hudFlashesLeft = 0;
            _hudFlashOn = false;
            _hudFlashText = null;
            _pendingHurt = -1;
            _pendingDrop = 0f;
            if (_reportSuppressed)
                return;
            _reportSuppressed = true;
            try
            {
                AircraftActionsReport report = SceneSingleton<AircraftActionsReport>.i;
                if (report != null && ActionsClear != null)
                    ActionsClear.Invoke(report, null);
            }
            catch { }
        }

        private static bool RestoreToCeiling(int index)
        {
            if (index < 0 || index >= _parts.Count)
                return false;
            Part p = _parts[index];
            if (p.Health >= p.Ceiling * 0.995f)
                return false;
            p.Health = p.Ceiling;
            _parts[index] = p;
            return true;
        }

        private static bool InstantRepairLowerCap(int index)
        {
            if (index < 0 || index >= _parts.Count)
                return false;
            Part p = _parts[index];
            if (p.Health >= p.Ceiling * 0.995f && p.Ceiling >= 0.995f)
                return false;
            p.Ceiling = Mathf.Max(CeilingFloor, p.Ceiling - Random.Range(0.04f, 0.12f));
            p.Health = p.Ceiling;
            _parts[index] = p;
            return true;
        }

        private static void TickRepairQueue()
        {
            if (_repairQ.Count <= 0)
            {
                _repairLeft = 0f;
                return;
            }
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f || dt > 0.25f)
                dt = 0.02f;
            _repairLeft -= dt;
            if (_repairLeft > 0f)
                return;
            int idx = _repairQ[0];
            _repairQ.RemoveAt(0);
            RestoreToCeiling(idx);
            _repairLeft = _repairQ.Count > 0 ? ClickRepairSeconds : 0f;
        }

        private static void EnsureMaterial()
        {
            if (_matLoaded)
                return;
            _matLoaded = true;
            _matTier = PlayerPrefs.GetInt(MaterialPref, 0);
            if (_matTier < 0)
                _matTier = 0;
            if (_matTier > MaterialMax)
                _matTier = MaterialMax;
        }

        private static float MaterialWearMul()
        {
            EnsureMaterial();
            switch (_matTier)
            {
                case 1:
                    return 0.72f;
                case 2:
                    return 0.50f;
                case 3:
                    return 0.34f;
                default:
                    return 1f;
            }
        }

        private static float WearMulOf(string id)
        {
            string s = id;
            if (!string.IsNullOrEmpty(id))
            {
                int d = id.LastIndexOf('.');
                if (d >= 0 && d < id.Length - 1)
                    s = id.Substring(d + 1);
            }
            if (s == "intake")
                return 0.40f;
            if (s == "compressor")
                return 0.80f;
            if (s == "combustor")
                return 1.20f;
            if (s == "turbine")
                return 1.50f;
            if (s == "ab")
                return 1.70f;
            if (s == "nozzle")
                return 1.10f;
            if (s == "oil")
                return 1.10f;
            if (s == "inject")
                return 1.22f;
            if (s == "combine")
                return 1.18f;
            if (s == "tilt")
                return 1.12f;
            if (s == "rotor")
                return 1.25f;
            if (s == "pfan")
                return 0.95f;
            if (s == "gear")
                return 1.08f;
            if (s == "mounts")
                return 0.42f;
            if (s == "core")
                return 1.15f;
            if (s == "fan")
                return 0.90f;
            if (s == "duct")
                return 0.70f;
            if (s == "drive")
                return 1.05f;
            if (s == "shaft")
                return 1.10f;
            if (s == "mgb")
                return 1.20f;
            if (s == "tgb")
                return 0.85f;
            if (s == "swash")
                return 0.95f;
            if (s == "mast")
                return 0.75f;
            if (s == "crank")
                return 1.00f;
            if (s == "magneto")
                return 0.70f;
            if (s == "carb")
                return 0.85f;
            if (s == "turbo")
                return 1.35f;
            if (s == "waste")
                return 0.90f;
            if (s == "inter")
                return 0.80f;
            if (s == "radiator")
                return 1.05f;
            if (s == "exhaust")
                return 0.95f;
            if (s == "blades")
                return 1.30f;
            if (s == "hub")
                return 0.90f;
            if (s == "gov")
                return 0.75f;
            if (s == "spin")
                return 0.55f;
            if (s == "vector")
                return 1.15f;
            if (s == "lift")
                return 0.85f;
            if (s == "bleed")
                return 0.70f;
            if (s == "doors")
                return 0.50f;
            if (s == "mix")
                return 1.10f;
            if (s == "apu")
                return 0.90f;
            return 1f;
        }

        private static void ApplyShrapnel(float scale)
        {
            int n = Random.Range(1, 3);
            HurtRandomParts(n, 0.05f * scale, 0.12f * scale, true);
        }

        private static void ApplyVibration(float scale)
        {
            int n = Random.Range(3, 7);
            HurtRandomParts(n, 0.010f * scale, 0.032f * scale, false);
        }

        private static void HurtRandomParts(int count, float minDrop, float maxDrop, bool flashAlways)
        {
            if (_parts.Count <= 0)
                return;
            if (count > _parts.Count)
                count = _parts.Count;
            for (int k = 0; k < count; k++)
            {
                int idx = Random.Range(0, _parts.Count);
                Part p = _parts[idx];
                float before = p.Health;
                float drop = Random.Range(minDrop, maxDrop) * p.WearMul * WearRateMul;
                p.Health = Mathf.Max(0.02f, p.Health - drop);
                if (p.Health > p.Ceiling)
                    p.Health = p.Ceiling;
                _parts[idx] = p;
                if (flashAlways)
                    NoteHurtIfDropped(idx, before, p.Health);
                else
                    NoteHurtIfDropped(idx, before, p.Health);
            }
        }

        private static void WriteFloat(object obj, FieldInfo field, float value)
        {
            if (obj == null || field == null)
                return;
            try { field.SetValue(obj, value); }
            catch { }
        }
    }

    [HarmonyPatch]
    internal static class Patch_Afterburner_FlickerStagger
    {
        public static System.Reflection.MethodBase TargetMethod()
        {
            System.Type t = AccessTools.Inner(typeof(JetNozzle), "Afterburner");
            if (t == null)
                return null;
            return AccessTools.Method(t, "Run");
        }

        [HarmonyPrefix]
        private static void Prefix(object __instance, ref float throttleAmount)
        {
            if (__instance == null)
                return;
            if (!AirframeWearService.Flickering())
                return;
            if (AirframeWearService.IsActiveFlickerAb(__instance))
                return;
            throttleAmount = 0f;
        }
    }

    [HarmonyPatch(typeof(Turbojet), "FixedUpdate")]
    internal static class Patch_Turbojet_FuelCut
    {
        [HarmonyPostfix]
        private static void Postfix(Turbojet __instance)
        {
            AirframeWearService.EnforceFuelCut(__instance);
        }
    }

    [HarmonyPatch(typeof(Turbofan), "SlowUpdate")]
    internal static class Patch_Turbofan_FuelCut
    {
        [HarmonyPostfix]
        private static void Postfix(Turbofan __instance)
        {
            AirframeWearService.EnforceFuelCut(__instance);
        }
    }

    [HarmonyPatch(typeof(TurbineEngine), "Update")]
    internal static class Patch_Turbine_FuelCut
    {
        [HarmonyPostfix]
        private static void Postfix(TurbineEngine __instance)
        {
            AirframeWearService.EnforceFuelCut(__instance);
        }
    }

    [HarmonyPatch(typeof(AircraftActionsReport), "ReportText")]
    internal static class Patch_ActionsReport_AfterDeath
    {
        [HarmonyPrefix]
        private static bool Prefix()
        {
            if (AirframeWearService.LocalPilotGone())
                return false;
            return true;
        }
    }

    [HarmonyPatch(typeof(GLOC), "SimulateGLOC")]
    internal static class Patch_Gloc_PilotHealth
    {
        [HarmonyPrefix]
        private static void Prefix(ref float gForce)
        {
            float h = AirframeWearService.PilotHealth01();
            if (h >= 0.995f)
                return;
            float mul = Mathf.Lerp(1.75f, 1f, h);
            gForce = gForce * mul;
        }
    }
}
