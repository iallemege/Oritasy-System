using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Nuclear EMP: 2× shockwave radius, 30s vehicle blackout, player MFD bluescreen.
    /// Does not enlarge the blast itself. Cook-off fake nukes are ignored.
    /// </summary>
    internal static class EmpPulseService
    {
        private const float MinYieldKt = 0.01f;
        private const float PulseLockSec = 1.5f;

        private static readonly FieldInfo YieldField =
            AccessTools.Field(typeof(Shockwave), "yieldKilotons");
        private static readonly FieldInfo RadiusField =
            AccessTools.Field(typeof(Shockwave), "blastRadius");
        private static readonly FieldInfo AircraftInputsField =
            AccessTools.Field(typeof(Aircraft), "controlInputs");
        private static readonly FieldInfo ShipInputsField =
            AccessTools.Field(typeof(Ship), "inputs");
        private static readonly FieldInfo WmAircraftField =
            AccessTools.Field(typeof(WeaponManager), "aircraft");
        private static readonly FieldInfo MfdActiveLeft =
            AccessTools.Field(typeof(VirtualMFD), "activeLeft");
        private static readonly FieldInfo MfdActiveRight =
            AccessTools.Field(typeof(VirtualMFD), "activeRight");
        private static readonly FieldInfo SimThrottleField =
            AccessTools.Field(typeof(PilotPlayerState), "simulatedThrottle");

        private static ConfigEntry<bool> Enabled;
        private static ConfigEntry<float> RangeMul;
        private static ConfigEntry<float> DurationSec;

        private static readonly Dictionary<int, StunRec> Stunned = new Dictionary<int, StunRec>();
        private static readonly Dictionary<int, float> PulseLock = new Dictionary<int, float>();
        private static readonly List<int> Expired = new List<int>(32);
        private static readonly List<Rect> MfdRects = new List<Rect>(8);

        private static float _localUntil;
        private static bool _biaPatched;
        private static GUIStyle _faceStyle;
        private static GUIStyle _bodyStyle;
        private static GUIStyle _codeStyle;
        private static float _styleH;

        private sealed class StunRec
        {
            public Unit Unit;
            public float Until;
        }

        internal static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("Emp", "Enabled", true,
                "Nuclear EMP: 2× blast radius, 30s vehicle blackout, player MFD bluescreen.");
            RangeMul = config.Bind("Emp", "RangeMultiplier", 2f,
                "EMP radius = Shockwave.blastRadius × this. Does not change the blast.");
            DurationSec = config.Bind("Emp", "DurationSeconds", 30f,
                "Seconds vehicles stay dead / MFD stays blue.");
        }

        internal static bool IsOn()
        {
            return Enabled != null && Enabled.Value;
        }

        internal static bool LocalPlayerStunned()
        {
            if (NukeFlashService.Active)
                return true;
            return Time.unscaledTime < _localUntil;
        }

        internal static void ForceLocalHudBsod(float seconds)
        {
            if (seconds < 1f)
                seconds = 1f;
            float until = Time.unscaledTime + seconds;
            if (until > _localUntil)
                _localUntil = until;
        }

        internal static void DrawGoggleRadarBsod()
        {
            if (!LocalPlayerStunned())
                return;
            int prevDepth = GUI.depth;
            GUI.depth = -8000;
            Color prev = GUI.color;
            try
            {
                CollectMfdRects();
                for (int i = 0; i < MfdRects.Count; i++)
                    DrawBsodPanel(MfdRects[i]);
                if (InfantryModeService.Active)
                    DrawBsodPanel(InfantryRadar.ScreenRect());
                else
                {
                    Rect ga;
                    if (TryGaRadarRect(out ga))
                        DrawBsodPanel(ga);
                    else if (MfdRects.Count == 0)
                        DrawBsodPanel(new Rect(Screen.width - 208f, Screen.height - 208f, 192f, 192f));
                }
            }
            finally
            {
                GUI.color = prev;
                GUI.depth = prevDepth;
            }
        }

        internal static bool IsStunned(Unit u)
        {
            if (!IsOn() || u == null)
                return false;
            StunRec rec;
            if (!Stunned.TryGetValue(u.GetInstanceID(), out rec) || rec == null)
                return false;
            return Time.unscaledTime < rec.Until;
        }

        internal static bool IsStunned(Aircraft ac)
        {
            return IsStunned((Unit)ac);
        }

        internal static void Tick()
        {
            if (!IsOn())
            {
                if (Stunned.Count > 0)
                    Stunned.Clear();
                if (!NukeFlashService.Active)
                    _localUntil = 0f;
                return;
            }
            PrunePulseLock();
            if (Stunned.Count == 0)
                return;
            float now = Time.unscaledTime;
            Expired.Clear();
            foreach (KeyValuePair<int, StunRec> kv in Stunned)
            {
                StunRec rec = kv.Value;
                if (rec == null || rec.Unit == null || now >= rec.Until)
                {
                    Expired.Add(kv.Key);
                    continue;
                }
                try
                {
                    if (rec.Unit.disabled)
                    {
                        Expired.Add(kv.Key);
                        continue;
                    }
                }
                catch
                {
                    Expired.Add(kv.Key);
                    continue;
                }
                Maintain(rec.Unit);
            }
            for (int i = 0; i < Expired.Count; i++)
                Stunned.Remove(Expired[i]);
            if (now >= _localUntil)
                _localUntil = 0f;
        }

        internal static void FixedTick()
        {
            if (!IsOn() || Stunned.Count == 0)
                return;
            foreach (KeyValuePair<int, StunRec> kv in Stunned)
            {
                StunRec rec = kv.Value;
                if (rec == null || rec.Unit == null)
                    continue;
                FreezeGroundOrShip(rec.Unit);
            }
        }

        internal static void TryPatchBia(Harmony harmony)
        {
            if (_biaPatched || harmony == null)
                return;
            Type t = AccessTools.TypeByName("BIA.BIAMfd");
            if (t == null)
                return;
            MethodInfo m = AccessTools.Method(t, "OnGUI");
            if (m == null)
                return;
            try
            {
                harmony.Patch(m, new HarmonyMethod(typeof(EmpPulseService), "BiaMfdPrefix"));
                _biaPatched = true;
            }
            catch { }
        }

        internal static bool BiaMfdPrefix()
        {
            if (!LocalPlayerStunned())
                return true;
            DrawBiaOverlayBsod();
            return false;
        }

        internal static void OnShockwave(Shockwave sw, bool fromStart)
        {
            float yieldEarly = 0f;
            try
            {
                if (YieldField != null && sw != null)
                    yieldEarly = (float)YieldField.GetValue(sw);
            }
            catch { }
            bool cookoff = false;
            try
            {
                if (sw != null)
                    cookoff = VehicleCookoffService.IsVisualOnly(sw);
            }
            catch { }
            if (!cookoff)
                NukeFlashService.TryBlind(sw, yieldEarly);
            if (!IsOn() || sw == null)
                return;
            if (cookoff)
                return;
            float yield = 0f;
            float radius = 0f;
            try
            {
                if (YieldField != null)
                    yield = (float)YieldField.GetValue(sw);
                if (RadiusField != null)
                    radius = (float)RadiusField.GetValue(sw);
            }
            catch { }
            // Nuclear EMP only (214C+). HE blastRadius>>10 with yt~0 must not allUnits-scan.
            if (yield < MinYieldKt)
                return;
            if (!fromStart && radius < 1f)
                return;
            int sid = 0;
            try { sid = sw.GetInstanceID(); }
            catch { return; }
            float now = Time.unscaledTime;
            float locked;
            if (PulseLock.TryGetValue(sid, out locked) && now < locked)
                return;
            float mul = RangeMul != null ? RangeMul.Value : 2f;
            if (mul < 0.1f)
                mul = 0.1f;
            float empR = radius * mul;
            if (empR < 1f && yield >= MinYieldKt)
                empR = 250f * Mathf.Pow(yield, 1f / 3f) * mul;
            if (empR < 1f)
                return;
            PulseLock[sid] = now + PulseLockSec;
            ApplyPulse(sw.transform.position, empR);
            if (Plugin.DebugLog != null && Plugin.DebugLog.Value && Plugin.Log != null)
                Plugin.Log.LogInfo("EMP r=" + empR.ToString("0") + " yt=" + yield.ToString("0.00")
                    + " blast=" + radius.ToString("0"));
        }

        internal static void ZeroAircraftInputs(Aircraft ac)
        {
            if (ac == null)
                return;
            try
            {
                ControlInputs inputs = ac.GetInputs();
                if (inputs == null && AircraftInputsField != null)
                    inputs = AircraftInputsField.GetValue(ac) as ControlInputs;
                if (inputs != null)
                {
                    inputs.pitch = 0f;
                    inputs.roll = 0f;
                    inputs.yaw = 0f;
                    inputs.throttle = 0f;
                    inputs.brake = 0f;
                    inputs.customAxis1 = 0f;
                }
            }
            catch { }
            try
            {
                Pilot[] pilots = ac.pilots;
                if (pilots != null && SimThrottleField != null)
                {
                    for (int i = 0; i < pilots.Length; i++)
                    {
                        if (pilots[i] == null || pilots[i].playerState == null)
                            continue;
                        SimThrottleField.SetValue(pilots[i].playerState, 0f);
                    }
                }
            }
            catch { }
        }

        internal static void CutIgnition(Aircraft ac)
        {
            if (ac == null)
                return;
            bool on = false;
            try { on = ac.Ignition; }
            catch { return; }
            if (!on)
                return;
            try
            {
                if (ac.IsServer)
                    ac.NetworkIgnition = false;
                else
                    ac.Ignition = false;
            }
            catch
            {
                try { ac.Ignition = false; }
                catch { }
            }
        }

        internal static bool BlocksFire(object instance)
        {
            if (InfantryCabinGunService.BlockingAircraftGuns())
                return true;
            if (!IsOn() || instance == null)
                return false;
            Unit u = OwnerOf(instance);
            return IsStunned(u);
        }

        internal static void Draw()
        {
            if (!LocalPlayerStunned())
                return;
            Event e = Event.current;
            if (e == null)
                return;
            if (e.type != EventType.Repaint && e.type != EventType.Layout)
                return;
            int prevDepth = GUI.depth;
            GUI.depth = -2000;
            Color prev = GUI.color;
            try
            {
                CollectMfdRects();
                for (int i = 0; i < MfdRects.Count; i++)
                    DrawBsodPanel(MfdRects[i]);
            }
            finally
            {
                GUI.color = prev;
                GUI.depth = prevDepth;
            }
        }

        private static void ApplyPulse(Vector3 origin, float empR)
        {
            List<Unit> all = null;
            try { all = UnitRegistry.allUnits; }
            catch { return; }
            if (all == null)
                return;
            float dur = DurationSec != null ? DurationSec.Value : 30f;
            if (dur < 1f)
                dur = 1f;
            float until = Time.unscaledTime + dur;
            float r2 = empR * empR;
            int hit = 0;
            for (int i = 0; i < all.Count; i++)
            {
                Unit u = all[i];
                if (!IsVehicle(u))
                    continue;
                try
                {
                    if (u.disabled || !Plugin.IsRuntimeInstance(u))
                        continue;
                    if ((u.transform.position - origin).sqrMagnitude > r2)
                        continue;
                }
                catch { continue; }
                Stun(u, until);
                hit++;
            }
            MaybeMarkLocal(until);
            MaybeMarkInfantry(origin, r2, until);
            if (hit > 0)
                ReportLocal(UiLang.T("EMP — avionics offline", "电磁脉冲 — 航电离线"));
        }

        private static void Stun(Unit u, float until)
        {
            if (u == null)
                return;
            int id = u.GetInstanceID();
            StunRec rec;
            if (!Stunned.TryGetValue(id, out rec) || rec == null)
            {
                rec = new StunRec();
                Stunned[id] = rec;
            }
            rec.Unit = u;
            if (until > rec.Until)
                rec.Until = until;
            Maintain(u);
        }

        private static void Maintain(Unit u)
        {
            Aircraft ac = u as Aircraft;
            if (ac != null)
            {
                CutIgnition(ac);
                ZeroAircraftInputs(ac);
                return;
            }
            FreezeGroundOrShip(u);
            ZeroShipInputs(u as Ship);
        }

        private static void FreezeGroundOrShip(Unit u)
        {
            if (u == null)
                return;
            if (!(u is GroundVehicle) && !(u is Ship))
                return;
            try
            {
                Rigidbody rb = u.rb;
                if (rb == null)
                    return;
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
            catch { }
        }

        private static void ZeroShipInputs(Ship ship)
        {
            if (ship == null || ShipInputsField == null)
                return;
            try
            {
                ShipInputs inputs = ShipInputsField.GetValue(ship) as ShipInputs;
                if (inputs == null)
                    return;
                inputs.throttle = 0f;
                inputs.steering = 0f;
            }
            catch { }
        }

        private static bool IsVehicle(Unit u)
        {
            if (u == null)
                return false;
            return (u is Aircraft) || (u is GroundVehicle) || (u is Ship);
        }

        private static void MaybeMarkInfantry(Vector3 origin, float r2, float until)
        {
            Vector3 eye = Vector3.zero;
            bool dead = false;
            bool onFoot = false;
            if (InfantryModeService.Active)
            {
                onFoot = true;
                dead = InfantryModeService.Dead;
                eye = InfantryModeService.EyePos;
            }
            else
            {
                float yaw;
                if (TryGaInfantry(out eye, out yaw, out dead))
                    onFoot = true;
            }
            if (!onFoot || dead)
                return;
            if ((eye - origin).sqrMagnitude > r2)
                return;
            if (until > _localUntil)
                _localUntil = until;
        }

        internal static bool TryGaInfantry(out Vector3 origin, out float yaw, out bool dead)
        {
            origin = Vector3.zero;
            yaw = 0f;
            dead = false;
            Type t = FindGaType("GroundArsenal.GaMode");
            if (t == null)
                return false;
            try
            {
                PropertyInfo act = t.GetProperty("Active",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (act == null)
                    return false;
                object av = act.GetValue(null, null);
                if (!(av is bool) || !(bool)av)
                    return false;
                PropertyInfo dp = t.GetProperty("Dead",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (dp != null)
                {
                    object dv = dp.GetValue(null, null);
                    if (dv is bool)
                        dead = (bool)dv;
                }
                PropertyInfo ep = t.GetProperty("EyePos",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                PropertyInfo yp = t.GetProperty("LookYaw",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (ep != null)
                    origin = (Vector3)ep.GetValue(null, null);
                if (yp != null)
                    yaw = (float)yp.GetValue(null, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGaRadarRect(out Rect r)
        {
            r = new Rect(0f, 0f, 0f, 0f);
            Type t = FindGaType("GroundArsenal.GaRadar");
            if (t == null)
                return false;
            try
            {
                MethodInfo m = t.GetMethod("ScreenRect",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                if (m == null)
                    return false;
                object v = m.Invoke(null, null);
                if (!(v is Rect))
                    return false;
                r = (Rect)v;
                return r.width > 8f && r.height > 8f;
            }
            catch
            {
                return false;
            }
        }

        private static Type FindGaType(string fullName)
        {
            Type t = Type.GetType(fullName + ", GroundArsenal", false);
            if (t != null)
                return t;
            Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < all.Length; i++)
            {
                try { t = all[i].GetType(fullName, false); }
                catch { t = null; }
                if (t != null)
                    return t;
            }
            return null;
        }

        private static void MaybeMarkLocal(float until)
        {
            Aircraft local = null;
            try
            {
                if (!GameManager.GetLocalAircraft(out local) || local == null)
                    return;
            }
            catch { return; }
            if (!IsStunned(local))
                return;
            if (until > _localUntil)
                _localUntil = until;
        }

        private static Unit OwnerOf(object instance)
        {
            WeaponManager wm = instance as WeaponManager;
            if (wm != null)
            {
                if (WmAircraftField != null)
                {
                    try
                    {
                        Aircraft ac = WmAircraftField.GetValue(wm) as Aircraft;
                        if (ac != null)
                            return ac;
                    }
                    catch { }
                }
                try { return wm.GetComponentInParent<Unit>(); }
                catch { return null; }
            }
            Pilot pilot = instance as Pilot;
            if (pilot != null)
            {
                try
                {
                    if (pilot.aircraft != null)
                        return pilot.aircraft;
                }
                catch { }
                try { return pilot.GetComponentInParent<Unit>(); }
                catch { return null; }
            }
            Component c = instance as Component;
            if (c == null)
                return null;
            try { return c.GetComponentInParent<Unit>(); }
            catch { return null; }
        }

        private static void PrunePulseLock()
        {
            if (PulseLock.Count == 0)
                return;
            float now = Time.unscaledTime;
            Expired.Clear();
            foreach (KeyValuePair<int, float> kv in PulseLock)
            {
                if (now >= kv.Value)
                    Expired.Add(kv.Key);
            }
            for (int i = 0; i < Expired.Count; i++)
                PulseLock.Remove(Expired[i]);
        }

        private static void ReportLocal(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;
            try
            {
                if (SceneSingleton<AircraftActionsReport>.i == null)
                    return;
                SceneSingleton<AircraftActionsReport>.i.ReportText(text, 3f);
            }
            catch { }
        }

        private static int RemainingLocalSeconds()
        {
            float left = _localUntil - Time.unscaledTime;
            if (NukeFlashService.Active)
            {
                float g = NukeFlashService.Until - Time.unscaledTime;
                if (g > left)
                    left = g;
            }
            if (left < 0f)
                left = 0f;
            int n = Mathf.CeilToInt(left);
            if (n < 0)
                n = 0;
            return n;
        }

        private static void CollectMfdRects()
        {
            MfdRects.Clear();
            Aircraft ac = null;
            try
            {
                if (!GameManager.GetLocalAircraft(out ac) || ac == null)
                    return;
            }
            catch { return; }
            try
            {
                VirtualMFD mfd = ac.GetComponentInChildren<VirtualMFD>(true);
                if (mfd != null)
                {
                    TryAddMfd(MfdActiveLeft != null ? MfdActiveLeft.GetValue(mfd) as MFDScreen : null);
                    TryAddMfd(MfdActiveRight != null ? MfdActiveRight.GetValue(mfd) as MFDScreen : null);
                }
            }
            catch { }
            try
            {
                MFDScreen[] pages = ac.GetComponentsInChildren<MFDScreen>(true);
                if (pages != null)
                {
                    for (int i = 0; i < pages.Length; i++)
                    {
                        if (pages[i] == null)
                            continue;
                        bool active = false;
                        try { active = pages[i].isActive; }
                        catch { }
                        if (active)
                            TryAddMfd(pages[i]);
                    }
                }
            }
            catch { }
            if (MfdRects.Count == 0)
            {
                try
                {
                    MFDScreen[] pages = ac.GetComponentsInChildren<MFDScreen>(true);
                    if (pages != null)
                    {
                        for (int i = 0; i < pages.Length; i++)
                            TryAddMfd(pages[i]);
                    }
                }
                catch { }
            }
            Rect overlay;
            if (TryHudMfdRect(out overlay))
                AddRect(overlay);
        }

        private static MethodInfo _hudRectMethod;
        private static float _hudRectRetryAt;

        private static bool TryHudMfdRect(out Rect rect)
        {
            rect = new Rect();
            if (_hudRectMethod == null && Time.unscaledTime >= _hudRectRetryAt)
            {
                _hudRectRetryAt = Time.unscaledTime + 4f;
                try
                {
                    Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                    for (int i = 0; i < asms.Length; i++)
                    {
                        Type t = asms[i].GetType("Oritasy.RadarMfdOverlay");
                        if (t == null)
                            continue;
                        MethodInfo m = t.GetMethod("TryGetScreenRect",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                        if (m == null)
                            continue;
                        _hudRectMethod = m;
                        break;
                    }
                }
                catch { }
            }
            if (_hudRectMethod == null)
                return false;
            try
            {
                object[] args = new object[] { rect };
                object ok = _hudRectMethod.Invoke(null, args);
                if (ok == null || !(ok is bool) || !(bool)ok)
                    return false;
                rect = (Rect)args[0];
                return rect.width > 12f && rect.height > 12f;
            }
            catch
            {
                return false;
            }
        }

        private static void AddRect(Rect rect)
        {
            if (rect.width < 12f || rect.height < 12f || MfdRects.Count >= 8)
                return;
            for (int i = 0; i < MfdRects.Count; i++)
            {
                if (RectsOverlap(MfdRects[i], rect))
                    return;
            }
            MfdRects.Add(rect);
        }

        private static void TryAddMfd(MFDScreen page)
        {
            if (page == null || MfdRects.Count >= 8)
                return;
            Rect rect;
            if (!TryMfdRect(page, out rect))
                return;
            for (int i = 0; i < MfdRects.Count; i++)
            {
                if (RectsOverlap(MfdRects[i], rect))
                    return;
            }
            MfdRects.Add(rect);
        }

        private static bool RectsOverlap(Rect a, Rect b)
        {
            return a.Overlaps(b, true);
        }

        private static bool TryMfdRect(MFDScreen page, out Rect rect)
        {
            rect = new Rect();
            if (page == null)
                return false;
            try
            {
                if (page.displayPanel != null)
                {
                    RectTransform rt = page.displayPanel.transform as RectTransform;
                    if (rt == null)
                        rt = page.displayPanel.GetComponent<RectTransform>();
                    if (rt != null && RectTransformToGui(rt, out rect))
                        return true;
                    Renderer r = page.displayPanel.GetComponentInChildren<Renderer>(true);
                    Camera cam = Camera.main;
                    if (r != null && cam != null && WorldBoundsToGui(cam, r.bounds, out rect))
                        return true;
                }
            }
            catch { }
            try
            {
                RectTransform self = page.transform as RectTransform;
                if (self != null && RectTransformToGui(self, out rect))
                    return true;
            }
            catch { }
            return false;
        }

        private static void AddFallbackMfdRects()
        {
            float w = UiScaleService.Width * 0.24f;
            float h = w * 0.88f;
            float pad = 18f;
            float y = UiScaleService.Height - h - pad;
            MfdRects.Add(new Rect(pad, y, w, h));
            MfdRects.Add(new Rect(UiScaleService.Width - w - pad, y, w, h));
        }

        private static void DrawBiaOverlayBsod()
        {
            float scale = Mathf.Clamp(Screen.height / 1080f, 0.75f, 1.6f);
            float w = 400f * scale;
            float h = 360f * scale;
            Rect left = new Rect(12f * scale, Screen.height - h - 12f * scale, w, h);
            Rect right = new Rect(Screen.width - w - 12f * scale, Screen.height - h - 12f * scale, w, h);
            DrawBsodPanel(left);
            DrawBsodPanel(right);
        }

        internal static void DrawBsodOn(Rect r)
        {
            DrawBsodPanel(r);
        }

        private static void DrawBsodPanel(Rect r)
        {
            if (r.width < 12f || r.height < 12f)
                return;
            EnsureStyles(r.height);
            Color prev = GUI.color;
            GUI.color = new Color(0f, 0.47f, 0.84f, 1f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
            float pad = Mathf.Max(8f, r.width * 0.055f);
            float x = r.x + pad;
            float y = r.y + pad * 0.6f;
            float tw = r.width - pad * 2f;
            GUI.Label(new Rect(x, y, tw, r.height * 0.28f), ":(", _faceStyle);
            y += r.height * 0.24f;
            string body = "Your aircraft ran into a problem and needs to restart.\n\nWe're collecting some error info, and then we'll restart for you.";
            GUI.Label(new Rect(x, y, tw, r.height * 0.4f), body, _bodyStyle);
            y += r.height * 0.38f;
            int sec = RemainingLocalSeconds();
            GUI.Label(new Rect(x, y, tw, 20f),
                sec.ToString() + " seconds remaining", _bodyStyle);
            y += 18f;
            GUI.Label(new Rect(x, y, tw, 18f), "Stop code: EMP_PULSE_FAULT", _codeStyle);
            GUI.color = prev;
        }

        private static void EnsureStyles(float h)
        {
            if (_faceStyle != null && Mathf.Abs(_styleH - h) < 2f)
                return;
            _styleH = h;
            int face = Mathf.Clamp(Mathf.RoundToInt(h * 0.22f), 28, 96);
            int body = Mathf.Clamp(Mathf.RoundToInt(h * 0.045f), 10, 22);
            int code = Mathf.Clamp(body - 1, 10, 18);
            _faceStyle = new GUIStyle(GUI.skin.label);
            _faceStyle.fontSize = face;
            _faceStyle.fontStyle = FontStyle.Bold;
            _faceStyle.normal.textColor = Color.white;
            _faceStyle.alignment = TextAnchor.UpperLeft;
            _faceStyle.wordWrap = false;
            _bodyStyle = new GUIStyle(GUI.skin.label);
            _bodyStyle.fontSize = body;
            _bodyStyle.normal.textColor = Color.white;
            _bodyStyle.alignment = TextAnchor.UpperLeft;
            _bodyStyle.wordWrap = true;
            _codeStyle = new GUIStyle(GUI.skin.label);
            _codeStyle.fontSize = code;
            _codeStyle.normal.textColor = new Color(0.9f, 0.95f, 1f, 1f);
            _codeStyle.alignment = TextAnchor.UpperLeft;
            _codeStyle.wordWrap = false;
            Font en = EnglishOsFont(body);
            if (en != null)
            {
                _faceStyle.font = en;
                _bodyStyle.font = en;
                _codeStyle.font = en;
            }
        }

        private static Font EnglishOsFont(int size)
        {
            try
            {
                return Font.CreateDynamicFontFromOSFont(
                    new string[] { "Segoe UI", "Arial", "Tahoma", "Calibri" }, size);
            }
            catch
            {
                return null;
            }
        }

        private static bool WorldBoundsToGui(Camera cam, Bounds b, out Rect rect)
        {
            rect = new Rect();
            if (cam == null)
                return false;
            Vector3 c = b.center;
            Vector3 e = b.extents;
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            int ok = 0;
            for (int ix = -1; ix <= 1; ix += 2)
            {
                for (int iy = -1; iy <= 1; iy += 2)
                {
                    for (int iz = -1; iz <= 1; iz += 2)
                    {
                        Vector3 sp = cam.WorldToScreenPoint(c + new Vector3(e.x * ix, e.y * iy, e.z * iz));
                        if (sp.z <= 0.05f)
                            continue;
                        float gx = UiScaleService.FromScreenX(sp.x);
                        float gy = UiScaleService.FromScreenYFlipped(sp.y);
                        if (gx < minX) minX = gx;
                        if (gx > maxX) maxX = gx;
                        if (gy < minY) minY = gy;
                        if (gy > maxY) maxY = gy;
                        ok++;
                    }
                }
            }
            if (ok < 3)
                return false;
            float w = maxX - minX;
            float h = maxY - minY;
            if (w < 24f || h < 24f)
                return false;
            if (w > UiScaleService.Width * 0.9f || h > UiScaleService.Height * 0.9f)
                return false;
            rect = new Rect(minX, minY, w, h);
            return true;
        }

        private static bool RectTransformToGui(RectTransform rt, out Rect rect)
        {
            rect = new Rect();
            if (rt == null)
                return false;
            Vector3[] corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            Camera cam = null;
            try
            {
                bool looksScreen = true;
                for (int i = 0; i < 4; i++)
                {
                    if (corners[i].x < -8f || corners[i].x > UiScaleService.Width + 8f
                        || corners[i].y < -8f || corners[i].y > UiScaleService.Height + 8f)
                    {
                        looksScreen = false;
                        break;
                    }
                }
                if (!looksScreen)
                    cam = Camera.main;
            }
            catch { }
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                float gx;
                float gy;
                if (cam != null)
                {
                    Vector3 p = cam.WorldToScreenPoint(corners[i]);
                    if (p.z <= 0.05f)
                        return false;
                    gx = UiScaleService.FromScreenX(p.x);
                    gy = UiScaleService.FromScreenYFlipped(p.y);
                }
                else
                {
                    gx = corners[i].x;
                    gy = UiScaleService.Height - corners[i].y;
                }
                if (gx < minX) minX = gx;
                if (gx > maxX) maxX = gx;
                if (gy < minY) minY = gy;
                if (gy > maxY) maxY = gy;
            }
            float w = maxX - minX;
            float h = maxY - minY;
            if (w < 20f || h < 20f)
                return false;
            rect = new Rect(minX, minY, w, h);
            return true;
        }
    }

    [HarmonyPatch(typeof(Shockwave), "Start")]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_Emp_ShockwaveStart
    {
        [HarmonyPostfix]
        private static void Postfix(Shockwave __instance)
        {
            EmpPulseService.OnShockwave(__instance, true);
        }
    }

    [HarmonyPatch(typeof(Shockwave), "SetOwner")]
    [HarmonyPriority(Priority.Low)]
    internal static class Patch_Emp_ShockwaveOwner
    {
        [HarmonyPostfix]
        private static void Postfix(Shockwave __instance)
        {
            EmpPulseService.OnShockwave(__instance, false);
        }
    }

    [HarmonyPatch(typeof(Aircraft), "FilterInputs")]
    internal static class Patch_Emp_AircraftFilterInputs
    {
        [HarmonyPrefix]
        private static bool Prefix(Aircraft __instance)
        {
            if (!EmpPulseService.IsStunned(__instance))
                return true;
            EmpPulseService.ZeroAircraftInputs(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(Aircraft), "ApplySetInputs")]
    internal static class Patch_Emp_AircraftApplySetInputs
    {
        [HarmonyPrefix]
        private static bool Prefix(Aircraft __instance)
        {
            return !EmpPulseService.IsStunned(__instance);
        }
    }

    [HarmonyPatch(typeof(Aircraft), "LocalSimFixedUpdate")]
    internal static class Patch_Emp_AircraftLocalSim
    {
        [HarmonyPrefix]
        private static void Prefix(Aircraft __instance)
        {
            if (EmpPulseService.IsStunned(__instance))
                EmpPulseService.ZeroAircraftInputs(__instance);
        }
    }

    [HarmonyPatch(typeof(Aircraft), "CmdToggleIgnition")]
    internal static class Patch_Emp_AircraftIgnition
    {
        [HarmonyPrefix]
        private static bool Prefix(Aircraft __instance)
        {
            return !EmpPulseService.IsStunned(__instance);
        }
    }

    [HarmonyPatch(typeof(Aircraft), "Countermeasures")]
    internal static class Patch_Emp_AircraftCm
    {
        [HarmonyPrefix]
        private static bool Prefix(Aircraft __instance)
        {
            return !EmpPulseService.IsStunned(__instance);
        }
    }

    [HarmonyPatch(typeof(WeaponManager), "Fire")]
    internal static class Patch_Emp_WmFire
    {
        [HarmonyPrefix]
        private static bool Prefix(WeaponManager __instance)
        {
            return !EmpPulseService.BlocksFire(__instance);
        }
    }

    [HarmonyPatch(typeof(WeaponManager), "FireGuns")]
    internal static class Patch_Emp_WmFireGuns
    {
        [HarmonyPrefix]
        private static bool Prefix(WeaponManager __instance)
        {
            return !EmpPulseService.BlocksFire(__instance);
        }
    }

    [HarmonyPatch(typeof(WeaponStation), "Fire")]
    internal static class Patch_Emp_WsFire
    {
        [HarmonyPrefix]
        private static bool Prefix(WeaponStation __instance)
        {
            return !EmpPulseService.BlocksFire(__instance);
        }
    }

    [HarmonyPatch(typeof(Weapon), "Fire")]
    internal static class Patch_Emp_WeaponFire
    {
        [HarmonyPrefix]
        private static bool Prefix(Weapon __instance)
        {
            return !EmpPulseService.BlocksFire(__instance);
        }
    }

    [HarmonyPatch(typeof(Gun), "Fire")]
    internal static class Patch_Emp_GunFire
    {
        [HarmonyPrefix]
        private static bool Prefix(Gun __instance)
        {
            return !EmpPulseService.BlocksFire(__instance);
        }
    }

    [HarmonyPatch(typeof(MissileLauncher), "Fire")]
    internal static class Patch_Emp_MlFire
    {
        [HarmonyPrefix]
        private static bool Prefix(MissileLauncher __instance)
        {
            return !EmpPulseService.BlocksFire(__instance);
        }
    }

    [HarmonyPatch(typeof(Pilot), "Fire")]
    internal static class Patch_Emp_PilotFire
    {
        [HarmonyPrefix]
        private static bool Prefix(Pilot __instance)
        {
            return !EmpPulseService.BlocksFire(__instance);
        }
    }

    [HarmonyPatch(typeof(MountedMissile), "Fire")]
    internal static class Patch_Emp_MountedMissileFire
    {
        [HarmonyPrefix]
        private static bool Prefix(MountedMissile __instance)
        {
            if (EmpPulseService.BlocksFire(__instance))
                return false;
            KeAmmoCloneService.NoteFire(__instance);
            return true;
        }
    }

    [HarmonyPatch(typeof(Laser), "Fire")]
    internal static class Patch_Emp_LaserFire
    {
        [HarmonyPrefix]
        private static bool Prefix(Laser __instance)
        {
            return !EmpPulseService.BlocksFire(__instance);
        }
    }
}
