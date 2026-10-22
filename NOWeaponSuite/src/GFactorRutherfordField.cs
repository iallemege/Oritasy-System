using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace WeXon
{
    /// <summary>
    /// Pale-violet Rutherford field around any airframe still hanging a G Factor Container.
    /// Unique grade-21 armor: interior is immune to G-bomb damage, nothing penetrates,
    /// and fuzes that hit around the field dud.
    /// </summary>
    internal static class GFactorRutherfordField
    {
        private const string BubbleName = "WeXon_GFactorRutherford";
        private const float ScanInterval = 0.2f;
        private const float HudPeriod = 60f;
        private const int HudFlashes = 3;
        private const float HullInflate = 1.028f;
        private const float InteriorPad = 1.08f;
        private const float FuzeKillPadM = 22f;
        internal const int ArmorGrade21 = 21;
        private static readonly Color FieldColor = new Color(0.76f, 0.56f, 1f, 0.13f);
        private static readonly Color FieldPulse = new Color(0.86f, 0.68f, 1f, 0.2f);
        private static readonly HashSet<int> ShieldedIds = new HashSet<int>();
        private static readonly HashSet<int> ReleasedWeaponIds = new HashSet<int>();
        private static readonly HashSet<int> DuddedIds = new HashSet<int>();
        private static readonly Dictionary<int, GameObject> Bubbles = new Dictionary<int, GameObject>();
        private static readonly List<int> DeadBubbleIds = new List<int>(8);
        private static readonly List<FieldZone> Zones = new List<FieldZone>(8);
        private static readonly FieldInfo ImpactFuseField = AccessTools.Field(typeof(Missile), "impactFuse");
        private static readonly FieldInfo WarheadField = AccessTools.Field(typeof(Missile), "warhead");
        private struct FieldZone
        {
            public Vector3 Center;
            public Vector3 Extents;
        }
        private static Material _mat;
        private static bool _matTried;
        private static float _nextScan;
        private static Mesh _sphereMesh;
        private static bool _hudArmed;
        private static int _hudAcId;
        private static float _nextHudPulse;

        internal static void Tick()
        {
            float now = Time.unscaledTime;
            if (now >= _nextScan)
            {
                _nextScan = now + ScanInterval;
                Rescan();
            }
            UpdateBubbles();
            TickHud(now);
        }

        internal static void NoteCarrier(Unit u)
        {
            Aircraft ac = u as Aircraft;
            if (ac == null || ac.disabled)
                return;
            try { ShieldedIds.Add(ac.GetInstanceID()); }
            catch { }
            _nextScan = 0f;
        }

        internal static void NoteHullGone()
        {
            _nextScan = 0f;
        }

        internal static void NotifyDrop(Weapon weapon)
        {
            if (weapon != null)
            {
                try { ReleasedWeaponIds.Add(weapon.GetInstanceID()); }
                catch { }
                if (ReleasedWeaponIds.Count > 64)
                    ReleasedWeaponIds.Clear();
            }
            Unit u = null;
            if (weapon != null)
            {
                try { u = weapon.attachedUnit; }
                catch { u = null; }
                if (u == null)
                {
                    try { u = weapon.GetComponentInParent<Unit>(); }
                    catch { u = null; }
                }
            }
            Aircraft ac = u as Aircraft;
            _nextScan = 0f;
            Rescan();
            if (ac != null && !CarriesNow(ac))
            {
                try { ShieldedIds.Remove(ac.GetInstanceID()); }
                catch { }
                DestroyBubbleFor(ac);
            }
            else
                UpdateBubbles();
            TickHud(Time.unscaledTime);
        }

        internal static bool IsShielded(Unit u)
        {
            Aircraft ac = u as Aircraft;
            if (ac == null)
                return false;
            return IsShieldedAircraft(ac);
        }

        internal static bool IsShieldedAircraft(Aircraft ac)
        {
            if (ac == null)
                return false;
            try
            {
                if (ac.disabled)
                    return false;
            }
            catch { return false; }
            int id = 0;
            try { id = ac.GetInstanceID(); }
            catch { return false; }
            return id != 0 && ShieldedIds.Contains(id);
        }

        internal static bool IsShieldedPart(Component c)
        {
            if (c == null || ShieldedIds.Count == 0)
                return false;
            Aircraft ac = null;
            UnitPart part = c as UnitPart;
            if (part != null)
            {
                try { ac = part.parentUnit as Aircraft; }
                catch { ac = null; }
            }
            if (ac == null)
            {
                try { ac = c.GetComponentInParent<Aircraft>(); }
                catch { ac = null; }
            }
            return IsShieldedAircraft(ac);
        }

        internal static bool IsShieldedDamageable(IDamageable dmg)
        {
            if (dmg == null || ShieldedIds.Count == 0)
                return false;
            Unit u = null;
            try { u = dmg.GetUnit(); }
            catch { u = null; }
            if (IsShielded(u))
                return true;
            Component c = dmg as Component;
            return IsShieldedPart(c);
        }

        internal static bool IsInsideField(Unit u)
        {
            if (u == null)
                return false;
            if (IsShielded(u))
                return true;
            Aircraft parent = u as Aircraft;
            if (parent == null)
            {
                try { parent = u.GetComponentInParent<Aircraft>(); }
                catch { parent = null; }
            }
            if (IsShieldedAircraft(parent))
                return true;
            try
            {
                if (u.transform != null && PositionInsideField(u.transform.position))
                    return true;
            }
            catch { }
            return false;
        }

        internal static bool PositionInsideField(Vector3 pos)
        {
            return InAnyZone(pos, InteriorPad, 0f);
        }

        internal static bool PositionNearField(Vector3 pos)
        {
            return InAnyZone(pos, 1f, FuzeKillPadM);
        }

        internal static bool ShouldDudMunition(Missile missile)
        {
            if (missile == null)
                return false;
            try
            {
                if (AmmoDump50TWeapon.IsMissile(missile))
                    return false;
            }
            catch { }
            int id = 0;
            try { id = missile.GetInstanceID(); }
            catch { return false; }
            if (id != 0 && DuddedIds.Contains(id))
                return true;
            if (IsInsideField(missile))
                return true;
            try
            {
                if (missile.transform != null && PositionNearField(missile.transform.position))
                    return true;
            }
            catch { }
            try
            {
                PersistentID tid = missile.targetID;
                Unit tgt = null;
                if (tid.IsValid && tid.TryGetUnit(out tgt) && IsInsideField(tgt))
                    return true;
            }
            catch { }
            return false;
        }

        internal static bool DudMunition(Missile missile)
        {
            if (missile == null)
                return false;
            try
            {
                if (AmmoDump50TWeapon.IsMissile(missile))
                    return false;
            }
            catch { }
            int id = 0;
            try { id = missile.GetInstanceID(); }
            catch { return false; }
            if (id != 0)
            {
                DuddedIds.Add(id);
                if (DuddedIds.Count > 96)
                    DuddedIds.Clear();
            }
            try
            {
                if (ImpactFuseField != null)
                    ImpactFuseField.SetValue(missile, false);
            }
            catch { }
            try
            {
                if (WarheadField != null)
                {
                    Missile.Warhead wh = WarheadField.GetValue(missile) as Missile.Warhead;
                    if (wh != null)
                        wh.Armed = false;
                }
            }
            catch { }
            try
            {
                MissileSeeker[] seekers = missile.GetComponentsInChildren<MissileSeeker>(true);
                if (seekers != null)
                {
                    for (int i = 0; i < seekers.Length; i++)
                    {
                        if (seekers[i] == null)
                            continue;
                        seekers[i].proximityFuse = false;
                    }
                }
            }
            catch { }
            try
            {
                if (missile.rb != null)
                {
                    missile.rb.velocity = Vector3.zero;
                    missile.rb.angularVelocity = Vector3.zero;
                }
            }
            catch { }
            try { missile.DisableUnit(); }
            catch { }
            return true;
        }

        private static bool InAnyZone(Vector3 pos, float scale, float padM)
        {
            if (Zones.Count == 0)
                return false;
            for (int i = 0; i < Zones.Count; i++)
            {
                FieldZone z = Zones[i];
                Vector3 e = z.Extents * scale;
                e.x += padM;
                e.y += padM;
                e.z += padM;
                if (Mathf.Abs(pos.x - z.Center.x) > e.x)
                    continue;
                if (Mathf.Abs(pos.y - z.Center.y) > e.y)
                    continue;
                if (Mathf.Abs(pos.z - z.Center.z) > e.z)
                    continue;
                return true;
            }
            return false;
        }

        private static void RefreshZones()
        {
            Zones.Clear();
            if (ShieldedIds.Count == 0)
                return;
            List<Aircraft> all = null;
            try { all = UnitRegistry.allAircraft; }
            catch { all = null; }
            if (all == null)
                return;
            for (int i = 0; i < all.Count; i++)
            {
                Aircraft ac = all[i];
                if (!IsShieldedAircraft(ac) || ac.transform == null)
                    continue;
                Bounds b = default(Bounds);
                bool have = false;
                AeroPart[] parts = null;
                try { parts = ac.GetComponentsInChildren<AeroPart>(true); }
                catch { parts = null; }
                if (parts != null)
                {
                    for (int p = 0; p < parts.Length; p++)
                    {
                        AeroPart part = parts[p];
                        if (part == null)
                            continue;
                        Renderer r = part.GetComponent<Renderer>();
                        if (r == null)
                            r = part.GetComponentInChildren<Renderer>();
                        if (r == null || !r.enabled)
                            continue;
                        if (r.gameObject != null && r.gameObject.name == BubbleName)
                            continue;
                        if (!have)
                        {
                            b = r.bounds;
                            have = true;
                        }
                        else
                            b.Encapsulate(r.bounds);
                    }
                }
                FieldZone z = new FieldZone();
                if (have)
                {
                    z.Center = b.center;
                    z.Extents = b.extents;
                    if (z.Extents.x < 1.5f) z.Extents.x = 1.5f;
                    if (z.Extents.y < 0.8f) z.Extents.y = 0.8f;
                    if (z.Extents.z < 1.5f) z.Extents.z = 1.5f;
                }
                else
                {
                    z.Center = ac.transform.position;
                    z.Extents = new Vector3(8f, 3f, 8f);
                }
                Zones.Add(z);
            }
        }

        private static void Rescan()
        {
            ShieldedIds.Clear();
            Aircraft local = null;
            try { GameManager.GetLocalAircraft(out local); }
            catch { local = null; }
            if (CarriesNow(local))
            {
                try { ShieldedIds.Add(local.GetInstanceID()); }
                catch { }
            }
            List<Aircraft> all = null;
            try { all = UnitRegistry.allAircraft; }
            catch { all = null; }
            if (all == null)
            {
                RefreshZones();
                return;
            }
            for (int i = 0; i < all.Count; i++)
            {
                Aircraft ac = all[i];
                if (!CarriesNow(ac))
                    continue;
                try { ShieldedIds.Add(ac.GetInstanceID()); }
                catch { }
            }
            RefreshZones();
        }

        private static bool CarriesNow(Aircraft ac)
        {
            return AmmoDump50TWeapon.IsHangingOn(ac);
        }

        private static void TickHud(float now)
        {
            Aircraft local = null;
            try { GameManager.GetLocalAircraft(out local); }
            catch { local = null; }
            int id = 0;
            try
            {
                if (local != null)
                    id = local.GetInstanceID();
            }
            catch { id = 0; }

            bool on = false;
            if (local != null)
                on = IsShieldedAircraft(local) || CarriesNow(local);

            if (id != _hudAcId)
            {
                _hudAcId = id;
                _hudArmed = on;
                if (on)
                {
                    PulseHud();
                    _nextHudPulse = now + HudPeriod;
                }
                return;
            }

            if (!on)
            {
                if (_hudArmed)
                    PulseOffHud();
                _hudArmed = false;
                return;
            }
            if (!_hudArmed)
            {
                _hudArmed = true;
                PulseHud();
                _nextHudPulse = now + HudPeriod;
                return;
            }
            if (now < _nextHudPulse)
                return;
            _nextHudPulse = now + HudPeriod;
            PulseHud();
        }

        private static void PulseOffHud()
        {
#if ORITASY_COMBINED
            try
            {
                if (Oritasy.JoinMenuFactionFix.SelectionUiOpen())
                    return;
            }
            catch { }
            try
            {
                if (Oritasy.AirframeWearService.LocalPilotGone())
                    return;
            }
            catch { }
            string msg = Oritasy.UiLang.T(
                "G-ELEMENT SOURCE DETACHED, RUTHERFORD FIELD ACTUATOR FORCED OFF",
                "G元素来源已经脱离，强制停止卢瑟福立场作动器");
            try { Oritasy.AirframeWearService.FlashHudMessage(msg, 8); }
            catch { }
            try
            {
                AircraftActionsReport rpt = SceneSingleton<AircraftActionsReport>.i;
                if (rpt != null)
                    rpt.ReportText(msg, 3.6f);
            }
            catch { }
#endif
        }

        private static void PulseHud()
        {
#if ORITASY_COMBINED
            string msg = Oritasy.UiLang.T("RUTHERFORD FIELD ACTIVE", "卢瑟福立场激活");
            try { Oritasy.AirframeWearService.FlashHudMessage(msg, HudFlashes); }
            catch { }
            try
            {
                AircraftActionsReport rpt = SceneSingleton<AircraftActionsReport>.i;
                if (rpt != null)
                    rpt.ReportText(msg, 2.8f);
            }
            catch { }
            try
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogInfo("Rutherford field active");
            }
            catch { }
#endif
        }

        private static void DestroyBubbleFor(Aircraft ac)
        {
            if (ac == null)
                return;
            int id = 0;
            try { id = ac.GetInstanceID(); }
            catch { return; }
            Bubbles.Remove(id);
            StripVisuals(ac);
        }

        private static void UpdateBubbles()
        {
            DeadBubbleIds.Clear();
            Dictionary<int, GameObject>.Enumerator en = Bubbles.GetEnumerator();
            while (en.MoveNext())
            {
                int id = en.Current.Key;
                GameObject go = en.Current.Value;
                if (!ShieldedIds.Contains(id) || go == null)
                {
                    DeadBubbleIds.Add(id);
                    Aircraft host = null;
                    if (go != null)
                    {
                        try { host = go.GetComponentInParent<Aircraft>(); }
                        catch { host = null; }
                    }
                    if (host != null)
                        StripVisuals(host);
                    else if (go != null)
                    {
                        try { UnityEngine.Object.Destroy(go); }
                        catch { }
                    }
                }
            }
            for (int i = 0; i < DeadBubbleIds.Count; i++)
                Bubbles.Remove(DeadBubbleIds[i]);

            List<Aircraft> all = null;
            try { all = UnitRegistry.allAircraft; }
            catch { all = null; }
            if (all == null)
                return;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2.4f);
            Color col = Color.Lerp(FieldColor, FieldPulse, pulse);
            TintField(col);
            bool rebuild = Time.unscaledTime >= _nextScan;
            for (int i = 0; i < all.Count; i++)
            {
                Aircraft ac = all[i];
                if (!IsShieldedAircraft(ac) || ac.transform == null)
                    continue;
                int id = ac.GetInstanceID();
                GameObject go = null;
                if (!Bubbles.TryGetValue(id, out go) || go == null)
                {
                    go = MakeBubble(ac);
                    if (go == null)
                        continue;
                    Bubbles[id] = go;
                }
                else if (rebuild)
                    EnsureHull(ac, go);
            }
        }

        private static GameObject MakeBubble(Aircraft ac)
        {
            Material mat = FieldMat();
            if (mat == null)
                return null;
            StripVisuals(ac);
            GameObject root = new GameObject(BubbleName);
            root.transform.SetParent(ac.transform, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            BuildHull(ac, root);
            return root;
        }

        private static void EnsureHull(Aircraft ac, GameObject root)
        {
            if (ac == null || root == null)
                return;
            if (CountShells(ac) > 0)
                return;
            BuildHull(ac, root);
        }

        private static void StripVisuals(Aircraft ac)
        {
            if (ac == null)
                return;
            Transform[] xs = null;
            try { xs = ac.GetComponentsInChildren<Transform>(true); }
            catch { xs = null; }
            if (xs == null)
                return;
            for (int i = 0; i < xs.Length; i++)
            {
                Transform t = xs[i];
                if (t == null || t.gameObject == null)
                    continue;
                if (t.name != BubbleName)
                    continue;
                try { UnityEngine.Object.Destroy(t.gameObject); }
                catch { }
            }
        }

        private static int CountShells(Aircraft ac)
        {
            if (ac == null)
                return 0;
            MeshRenderer[] rs = null;
            try { rs = ac.GetComponentsInChildren<MeshRenderer>(true); }
            catch { rs = null; }
            if (rs == null)
                return 0;
            int n = 0;
            for (int i = 0; i < rs.Length; i++)
            {
                MeshRenderer r = rs[i];
                if (r != null && r.gameObject != null && r.gameObject.name == BubbleName)
                    n++;
            }
            return n;
        }

        private static void BuildHull(Aircraft ac, GameObject root)
        {
            if (ac == null)
                return;
            Material mat = FieldMat();
            if (mat == null)
                return;
            int made = 0;
            MeshFilter[] filters = null;
            try { filters = ac.GetComponentsInChildren<MeshFilter>(true); }
            catch { filters = null; }
            if (filters != null)
            {
                for (int i = 0; i < filters.Length; i++)
                {
                    MeshFilter mf = filters[i];
                    if (!IsHullMesh(mf, ac))
                        continue;
                    if (AddShell(mf, mat))
                        made++;
                }
            }
            if (made == 0)
                AddFallbackEllipsoid(ac, root, mat);
        }

        private static bool AddShell(MeshFilter src, Material mat)
        {
            if (src == null || src.sharedMesh == null || mat == null)
                return false;
            GameObject shell = new GameObject(BubbleName);
            shell.transform.SetParent(src.transform, false);
            shell.transform.localPosition = Vector3.zero;
            shell.transform.localRotation = Quaternion.identity;
            shell.transform.localScale = new Vector3(HullInflate, HullInflate, HullInflate);
            MeshFilter mf = shell.AddComponent<MeshFilter>();
            mf.sharedMesh = src.sharedMesh;
            MeshRenderer mr = shell.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            try { mr.lightProbeUsage = LightProbeUsage.Off; }
            catch { }
            try { mr.reflectionProbeUsage = ReflectionProbeUsage.Off; }
            catch { }
            try { mr.allowOcclusionWhenDynamic = false; }
            catch { }
            return true;
        }

        private static void AddFallbackEllipsoid(Aircraft ac, GameObject root, Material mat)
        {
            if (ac == null || root == null || mat == null)
                return;
            Bounds b = default(Bounds);
            bool have = false;
            AeroPart[] parts = null;
            try { parts = ac.GetComponentsInChildren<AeroPart>(true); }
            catch { parts = null; }
            if (parts != null)
            {
                for (int i = 0; i < parts.Length; i++)
                {
                    AeroPart p = parts[i];
                    if (p == null || p.gameObject == null)
                        continue;
                    if (p.GetComponentInParent<Weapon>() != null)
                        continue;
                    Renderer r = p.GetComponent<Renderer>();
                    if (r == null)
                        r = p.GetComponentInChildren<Renderer>();
                    if (r == null || !r.enabled || r.gameObject.name == BubbleName)
                        continue;
                    if (!have)
                    {
                        b = r.bounds;
                        have = true;
                    }
                    else
                        b.Encapsulate(r.bounds);
                }
            }
            if (!have)
                return;
            Mesh mesh = SphereMesh();
            if (mesh == null)
                return;
            GameObject shell = new GameObject(BubbleName);
            shell.transform.SetParent(root.transform, false);
            Vector3 worldC = b.center;
            shell.transform.position = worldC;
            shell.transform.rotation = ac.transform.rotation;
            Vector3 ext = b.extents;
            float sx = Mathf.Max(0.4f, ext.x * 2.08f);
            float sy = Mathf.Max(0.4f, ext.y * 2.08f);
            float sz = Mathf.Max(0.4f, ext.z * 2.08f);
            Vector3 lossy = ac.transform.lossyScale;
            float dx = lossy.x != 0f ? sx / Mathf.Abs(lossy.x) : sx;
            float dy = lossy.y != 0f ? sy / Mathf.Abs(lossy.y) : sy;
            float dz = lossy.z != 0f ? sz / Mathf.Abs(lossy.z) : sz;
            shell.transform.localScale = new Vector3(dx, dy, dz);
            MeshFilter mf = shell.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            MeshRenderer mr = shell.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        private static bool IsHullMesh(MeshFilter mf, Aircraft ac)
        {
            if (mf == null || mf.sharedMesh == null || mf.gameObject == null)
                return false;
            if (mf.gameObject.name == BubbleName)
                return false;
            Renderer r = mf.GetComponent<Renderer>();
            if (r == null || !r.enabled)
                return false;
            if (r is TrailRenderer || r is LineRenderer)
                return false;
            if (mf.GetComponentInParent<Weapon>() != null)
                return false;
            if (mf.GetComponentInParent<Missile>() != null)
                return false;
            if (mf.GetComponentInParent<Pilot>() != null)
                return false;
            UnitPart part = mf.GetComponentInParent<UnitPart>();
            if (part == null)
                return false;
            try
            {
                if (part.parentUnit != null && !object.ReferenceEquals(part.parentUnit, ac))
                    return false;
            }
            catch { }
            if (NameLooksFx(mf.gameObject.name))
                return false;
            float mag = 0f;
            try { mag = r.bounds.extents.magnitude; }
            catch { return false; }
            if (mag < 0.05f || mag > 40f)
                return false;
            return true;
        }

        private static bool NameLooksFx(string n)
        {
            if (string.IsNullOrEmpty(n))
                return false;
            string s = n.ToLowerInvariant();
            return s.IndexOf("fx", StringComparison.Ordinal) >= 0
                || s.IndexOf("vapor", StringComparison.Ordinal) >= 0
                || s.IndexOf("trail", StringComparison.Ordinal) >= 0
                || s.IndexOf("contrail", StringComparison.Ordinal) >= 0
                || s.IndexOf("shock", StringComparison.Ordinal) >= 0
                || s.IndexOf("flame", StringComparison.Ordinal) >= 0
                || s.IndexOf("exhaust", StringComparison.Ordinal) >= 0
                || s.IndexOf("strobe", StringComparison.Ordinal) >= 0
                || s.IndexOf("navlight", StringComparison.Ordinal) >= 0
                || s.IndexOf("lens", StringComparison.Ordinal) >= 0
                || s.IndexOf("hud", StringComparison.Ordinal) >= 0
                || s.IndexOf("mfd", StringComparison.Ordinal) >= 0;
        }

        private static void TintField(Color col)
        {
            Material mat = FieldMat();
            if (mat == null)
                return;
            try { mat.SetColor("_BaseColor", col); }
            catch { }
            try { mat.SetColor("_Color", col); }
            catch { }
            try { mat.color = col; }
            catch { }
        }

        private static Mesh SphereMesh()
        {
            if (_sphereMesh != null)
                return _sphereMesh;
            GameObject tmp = null;
            try
            {
                tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                MeshFilter mf = tmp.GetComponent<MeshFilter>();
                if (mf != null)
                    _sphereMesh = mf.sharedMesh;
                Collider col = tmp.GetComponent<Collider>();
                if (col != null)
                    UnityEngine.Object.Destroy(col);
            }
            catch { }
            if (tmp != null)
                UnityEngine.Object.Destroy(tmp);
            return _sphereMesh;
        }

        private static Material FieldMat()
        {
            if (_mat != null)
                return _mat;
            _matTried = true;
            Shader sh = FindFieldShader();
            if (sh == null)
                return null;
            try
            {
                _mat = new Material(sh);
                _mat.name = "WeXon_RutherfordField";
                _mat.hideFlags = HideFlags.HideAndDontSave;
                ForceTransparent(_mat);
                Color col = FieldColor;
                try { _mat.SetColor("_BaseColor", col); }
                catch { }
                try { _mat.SetColor("_Color", col); }
                catch { }
                try { _mat.color = col; }
                catch { }
            }
            catch
            {
                _mat = null;
            }
            return _mat;
        }

        private static void ForceTransparent(Material mat)
        {
            if (mat == null)
                return;
            try
            {
                if (mat.HasProperty("_Surface"))
                    mat.SetFloat("_Surface", 1f);
                if (mat.HasProperty("_Blend"))
                    mat.SetFloat("_Blend", 0f);
                if (mat.HasProperty("_AlphaClip"))
                    mat.SetFloat("_AlphaClip", 0f);
                if (mat.HasProperty("_Mode"))
                    mat.SetFloat("_Mode", 3f);
                if (mat.HasProperty("_SrcBlend"))
                    mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                if (mat.HasProperty("_DstBlend"))
                    mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                if (mat.HasProperty("_SrcBlendAlpha"))
                    mat.SetInt("_SrcBlendAlpha", (int)BlendMode.SrcAlpha);
                if (mat.HasProperty("_DstBlendAlpha"))
                    mat.SetInt("_DstBlendAlpha", (int)BlendMode.OneMinusSrcAlpha);
                if (mat.HasProperty("_ZWrite"))
                    mat.SetFloat("_ZWrite", 0f);
                if (mat.HasProperty("_Cull"))
                    mat.SetFloat("_Cull", 2f);
                mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                mat.SetInt("_ZWrite", 0);
                mat.SetInt("_Cull", 2);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.renderQueue = (int)RenderQueue.Transparent;
                try { mat.SetShaderPassEnabled("ShadowCaster", false); }
                catch { }
            }
            catch { }
        }

        private static Shader FindFieldShader()
        {
            string[] names = new string[]
            {
                "Universal Render Pipeline/Particles/Unlit",
                "Universal Render Pipeline/Unlit",
                "Hidden/Internal-Colored",
                "Legacy Shaders/Transparent/Diffuse",
                "Sprites/Default",
                "Unlit/Color"
            };
            for (int i = 0; i < names.Length; i++)
            {
                Shader sh = null;
                try { sh = Shader.Find(names[i]); }
                catch { sh = null; }
                if (sh != null)
                    return sh;
            }
            return null;
        }
    }

    [HarmonyPatch(typeof(UnitPart), "TakeDamage")]
    internal static class Patch_UnitPart_TakeDamage_Rutherford
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(UnitPart __instance)
        {
            return !GFactorRutherfordField.IsShieldedPart(__instance);
        }
    }

    [HarmonyPatch(typeof(UnitPart), "ApplyDamage")]
    internal static class Patch_UnitPart_ApplyDamage_Rutherford
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(UnitPart __instance)
        {
            return !GFactorRutherfordField.IsShieldedPart(__instance);
        }
    }

    [HarmonyPatch(typeof(AeroPart), "ApplyDamage")]
    internal static class Patch_AeroPart_ApplyDamage_Rutherford
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(AeroPart __instance)
        {
            return !GFactorRutherfordField.IsShieldedPart(__instance);
        }
    }

    [HarmonyPatch(typeof(UnitPart), "TakeShockwave")]
    internal static class Patch_UnitPart_TakeShockwave_Rutherford
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(UnitPart __instance)
        {
            return !GFactorRutherfordField.IsShieldedPart(__instance);
        }
    }

    [HarmonyPatch(typeof(AeroPart), "TakeShockwave")]
    internal static class Patch_AeroPart_TakeShockwave_Rutherford
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(AeroPart __instance)
        {
            return !GFactorRutherfordField.IsShieldedPart(__instance);
        }
    }

    [HarmonyPatch(typeof(AeroPart), "OnCollisionEnter")]
    internal static class Patch_AeroPart_CollisionEnter_Rutherford
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(AeroPart __instance)
        {
            return !GFactorRutherfordField.IsShieldedPart(__instance);
        }
    }

    [HarmonyPatch(typeof(AeroPart), "OnCollisionStay")]
    internal static class Patch_AeroPart_CollisionStay_Rutherford
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(AeroPart __instance)
        {
            return !GFactorRutherfordField.IsShieldedPart(__instance);
        }
    }

    [HarmonyPatch(typeof(Pilot), "TakeDamage")]
    internal static class Patch_Pilot_TakeDamage_Rutherford
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Pilot __instance)
        {
            return !GFactorRutherfordField.IsShieldedPart(__instance);
        }
    }

    [HarmonyPatch(typeof(Pilot), "ApplyDamage")]
    internal static class Patch_Pilot_ApplyDamage_Rutherford
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Pilot __instance)
        {
            return !GFactorRutherfordField.IsShieldedPart(__instance);
        }
    }

    [HarmonyPatch(typeof(Pilot), "TakeShockwave")]
    internal static class Patch_Pilot_TakeShockwave_Rutherford
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Pilot __instance)
        {
            return !GFactorRutherfordField.IsShieldedPart(__instance);
        }
    }

    [HarmonyPatch(typeof(FuelTank), "OnCollisionEnter")]
    internal static class Patch_FuelTank_Collision_Rutherford
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(FuelTank __instance)
        {
            return !GFactorRutherfordField.IsShieldedPart(__instance);
        }
    }

    [HarmonyPatch(typeof(Turbofan), "TakeDamage")]
    internal static class Patch_Turbofan_TakeDamage_Rutherford
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Turbofan __instance)
        {
            return !GFactorRutherfordField.IsShieldedPart(__instance);
        }
    }

    [HarmonyPatch(typeof(Turbofan), "ApplyDamage")]
    internal static class Patch_Turbofan_ApplyDamage_Rutherford
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Turbofan __instance)
        {
            return !GFactorRutherfordField.IsShieldedPart(__instance);
        }
    }

    [HarmonyPatch(typeof(SwashRotor), "TakeDamage")]
    internal static class Patch_SwashRotor_TakeDamage_Rutherford
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(SwashRotor __instance)
        {
            return !GFactorRutherfordField.IsShieldedPart(__instance);
        }
    }

    [HarmonyPatch(typeof(SwashRotor), "ApplyDamage")]
    internal static class Patch_SwashRotor_ApplyDamage_Rutherford
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(SwashRotor __instance)
        {
            return !GFactorRutherfordField.IsShieldedPart(__instance);
        }
    }

    [HarmonyPatch(typeof(SoftBodyRotor), "TakeDamage")]
    internal static class Patch_SoftRotor_TakeDamage_Rutherford
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(SoftBodyRotor __instance)
        {
            return !GFactorRutherfordField.IsShieldedPart(__instance);
        }
    }

    [HarmonyPatch(typeof(SoftBodyRotor), "ApplyDamage")]
    internal static class Patch_SoftRotor_ApplyDamage_Rutherford
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(SoftBodyRotor __instance)
        {
            return !GFactorRutherfordField.IsShieldedPart(__instance);
        }
    }

    [HarmonyPatch(typeof(Missile), "PenetrateObject")]
    internal static class Patch_Missile_Penetrate_Rutherford
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Missile __instance, IDamageable damageable, ref bool __result)
        {
            if (!GFactorRutherfordField.IsShieldedDamageable(damageable))
            {
                Unit u = null;
                try
                {
                    if (damageable != null)
                        u = damageable.GetUnit();
                }
                catch { u = null; }
                if (!GFactorRutherfordField.IsInsideField(u))
                    return true;
            }
            GFactorRutherfordField.DudMunition(__instance);
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Missile), "Detonate")]
    [HarmonyPatch(new Type[] { typeof(Vector3), typeof(bool), typeof(bool) })]
    internal static class Patch_Missile_Detonate_Rutherford
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Missile __instance)
        {
            if (!GFactorRutherfordField.ShouldDudMunition(__instance))
                return true;
            GFactorRutherfordField.DudMunition(__instance);
            return false;
        }
    }

    [HarmonyPatch(typeof(Missile.Warhead), "Detonate")]
    internal static class Patch_Warhead_Detonate_Rutherford
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(Vector3 position, bool armed, float blastYield)
        {
            if (blastYield >= 1000000f)
                return true;
            if (!armed)
                return true;
            if (!GFactorRutherfordField.PositionNearField(position)
                && !GFactorRutherfordField.PositionInsideField(position))
                return true;
            return false;
        }
    }

    [HarmonyPatch]
    internal static class Patch_ProxyFuse_Rutherford
    {
        static bool Prepare()
        {
            return TargetMethod() != null;
        }

        static MethodBase TargetMethod()
        {
            Type t = AccessTools.Inner(typeof(Missile), "ProxyFuse");
            if (t == null)
                return null;
            return AccessTools.Method(t, "ConditionsMet", new Type[] { typeof(Vector3), typeof(float) });
        }

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(object __instance, ref bool __result)
        {
            if (__instance == null)
                return true;
            Missile m = null;
            try
            {
                FieldInfo mt = AccessTools.Field(__instance.GetType(), "missileTransform");
                Transform tr = mt != null ? mt.GetValue(__instance) as Transform : null;
                if (tr != null)
                    m = tr.GetComponentInParent<Missile>();
            }
            catch { m = null; }
            if (m == null)
                return true;
            try
            {
                if (AmmoDump50TWeapon.IsMissile(m))
                    return true;
            }
            catch { }
            Unit tgt = null;
            try
            {
                FieldInfo tt = AccessTools.Field(__instance.GetType(), "targetTransform");
                Transform trT = tt != null ? tt.GetValue(__instance) as Transform : null;
                if (trT != null)
                    tgt = trT.GetComponentInParent<Unit>();
            }
            catch { tgt = null; }
            if (!GFactorRutherfordField.IsInsideField(m) && !GFactorRutherfordField.IsInsideField(tgt))
                return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(Weapon), "Fire")]
    internal static class Patch_Weapon_Fire_RutherfordDrop
    {
        [HarmonyPostfix]
        private static void Postfix(Weapon __instance)
        {
            AmmoDump50TWeapon.NoteFire(__instance);
        }
    }
}
