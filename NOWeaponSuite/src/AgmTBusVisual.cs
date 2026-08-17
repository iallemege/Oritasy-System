using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace WeXon
{
    /// <summary>
    /// Visual-only AAM-IV mesh/material swap for ACM-119 / ACNM-118 bus.
    /// Keeps AAM-29 prefab logic/colliders; GS25 submunitions are never touched.
    /// </summary>
    internal static class AgmTBusVisual
    {
        private const string VisualChildName = "WeXon_AAMIV_Visual";
        private const string MarkerName = "WeXon_AAMIV_Applied";

        private static Mesh _mesh;
        private static Material _material;
        private static bool _loadAttempted;
        private static string _lastError;

        internal static void ApplyToBus(Missile missile)
        {
            if (missile == null)
                return;
            if (missile.GetComponent<AgmTSubBrain>() != null)
                return;
            if (!AgmTWeapon.HasBusDispenser(missile) && !AgmTWeapon.IsAgmTMissile(missile))
                return;
            ApplyToRoot(missile.gameObject);
        }

        /// <summary>Same ACM-119 / AAM-IV bus mesh on an arbitrary root (AAM-2CV hangar + flight).</summary>
        internal static void ApplyAcmBusMesh(GameObject root)
        {
            ApplyToRoot(root);
        }

        internal const string TbmExhaustHolderName = "WeXon_TbmExhaust";

        internal sealed class TbmExhaustBind
        {
            public Component[] ParticleSystems;
            public TrailEmitter[] Trails;
            public Light[] Lights;
        }

        /// <summary>
        /// Replace AAM-36 booster/sustainer FX with Piledriver TBM FireParticles + smoke.
        /// Caller must wire the bind into Motor.particleSystems (last motor only).
        /// </summary>
        internal static TbmExhaustBind ApplyTbmExhaust(GameObject root)
        {
            if (root == null)
                return null;
            Transform existing = root.transform.Find(TbmExhaustHolderName);
            if (existing != null)
                return CollectTbmBind(existing);

            GameObject donor = FindTbmPrefab();
            if (donor == null)
            {
                if (Plugin.DebugLog != null && Plugin.DebugLog.Value)
                    Plugin.Log.LogWarning("AAM-2CV TBM exhaust: ballisticMissile1 prefab not found");
                return null;
            }

            Transform srcFire = FindNamedChild(donor.transform, "FireParticles");
            Transform srcSmoke = FindNamedChild(donor.transform, "smokeParticles");
            if (srcFire == null && srcSmoke == null)
                return null;

            DisableDonorExhaust(root.transform);

            GameObject hold = new GameObject(TbmExhaustHolderName);
            hold.transform.SetParent(root.transform, false);
            hold.layer = root.layer;
            Vector3 tail;
            if (!TryGetVisualTailLocal(root.transform, out tail))
                tail = new Vector3(0f, 0f, -1.2f);
            hold.transform.localPosition = tail;
            hold.transform.localRotation = Quaternion.identity;
            hold.transform.localScale = Vector3.one;

            if (srcFire != null)
                CloneExhaustChild(srcFire.gameObject, hold.transform, Vector3.zero, "WeXon_TbmFire");
            if (srcSmoke != null)
                CloneExhaustChild(srcSmoke.gameObject, hold.transform, new Vector3(0f, 0f, -3f), "WeXon_TbmSmoke");

            TbmExhaustBind bind = CollectTbmBind(hold.transform);
            RebindTrailEmitters(bind, root);
            PlayParticleSystems(bind);
            return bind;
        }

        internal static bool TryGetVisualTailLocal(Transform root, out Vector3 local)
        {
            local = Vector3.zero;
            if (root == null || _mesh == null)
                return false;
            Transform vis = root.Find(VisualChildName);
            if (vis == null)
                return false;
            local = VisualTailInRoot(vis);
            return true;
        }

        /// <summary>Hangar rack: style mounted Weapon visuals for AGM-T stations.</summary>
        internal static void ApplyToHangarRack(GameObject rackRoot)
        {
            ApplyToHangarRack(rackRoot, false);
        }

        internal static void ApplyToHangarRack(GameObject rackRoot, bool aam4Pylon)
        {
            if (rackRoot == null)
                return;
            EnsureRackPylon(rackRoot, aam4Pylon);
            Weapon[] weapons = rackRoot.GetComponentsInChildren<Weapon>(true);
            for (int i = 0; i < weapons.Length; i++)
            {
                Weapon w = weapons[i];
                if (w == null || w is Gun)
                    continue;
                ApplyToRoot(w.gameObject, true);
            }
        }

        /// <summary>
        /// WeaponMount.prefab must be a rack (root + pylon + Weapon), not the bare flight body.
        /// If a clone lost its pylon, copy the vanilla AAM-29 / AAM-36 shoe and hang the rails on it.
        /// </summary>
        internal static void EnsureRackPylon(GameObject rackRoot, bool aam4Pylon)
        {
            if (rackRoot == null)
                return;
            if (FindNamedChild(rackRoot.transform, "pylon") != null)
                return;
            string rn = rackRoot.name != null ? rackRoot.name : string.Empty;
            if (rn.IndexOf("internal", StringComparison.OrdinalIgnoreCase) >= 0)
                return;

            Mesh mesh;
            Material[] mats;
            if (!TryGetDonorPylon(aam4Pylon, out mesh, out mats) || mesh == null)
                return;

            GameObject pylon = new GameObject("pylon");
            pylon.transform.SetParent(rackRoot.transform, false);
            pylon.transform.localPosition = new Vector3(0f, -0.083f, 0f);
            pylon.transform.localRotation = Quaternion.identity;
            pylon.transform.localScale = new Vector3(0.9f, 1f, 1f);
            MeshFilter mf = pylon.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            MeshRenderer mr = pylon.AddComponent<MeshRenderer>();
            if (mats != null && mats.Length > 0)
                mr.sharedMaterials = mats;
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.receiveShadows = true;

            Weapon[] weapons = rackRoot.GetComponentsInChildren<Weapon>(true);
            for (int i = 0; i < weapons.Length; i++)
            {
                Weapon w = weapons[i];
                if (w == null || w is Gun || w.transform == null)
                    continue;
                if (object.ReferenceEquals(w.gameObject, rackRoot))
                    continue;
                if (w.transform.parent != rackRoot.transform)
                    continue;
                w.transform.SetParent(pylon.transform, false);
                w.transform.localPosition = new Vector3(0f, -0.171f, 0.187f);
                w.transform.localRotation = Quaternion.identity;
            }
        }

        private static Mesh _donorPylonMeshAam2;
        private static Mesh _donorPylonMeshAam4;
        private static Material[] _donorPylonMatsAam2;
        private static Material[] _donorPylonMatsAam4;
        private static bool _donorPylonTriedAam2;
        private static bool _donorPylonTriedAam4;

        private static bool TryGetDonorPylon(bool aam4, out Mesh mesh, out Material[] mats)
        {
            mesh = aam4 ? _donorPylonMeshAam4 : _donorPylonMeshAam2;
            mats = aam4 ? _donorPylonMatsAam4 : _donorPylonMatsAam2;
            if (mesh != null)
                return true;
            if (aam4 ? _donorPylonTriedAam4 : _donorPylonTriedAam2)
                return false;
            if (aam4)
                _donorPylonTriedAam4 = true;
            else
                _donorPylonTriedAam2 = true;

            string want = aam4 ? "AAM4_single" : "AAM2_single";
            try
            {
                WeaponMount[] all = Resources.FindObjectsOfTypeAll<WeaponMount>();
                if (all == null)
                    return false;
                for (int i = 0; i < all.Length; i++)
                {
                    WeaponMount m = all[i];
                    if (m == null || m.prefab == null || string.IsNullOrEmpty(m.jsonKey))
                        continue;
                    if (!string.Equals(m.jsonKey, want, StringComparison.OrdinalIgnoreCase))
                        continue;
                    Transform pylonXf = FindNamedChild(m.prefab.transform, "pylon");
                    if (pylonXf == null)
                        continue;
                    MeshFilter mf = pylonXf.GetComponent<MeshFilter>();
                    MeshRenderer rend = pylonXf.GetComponent<MeshRenderer>();
                    if (mf == null || mf.sharedMesh == null)
                        continue;
                    mesh = mf.sharedMesh;
                    mats = rend != null ? rend.sharedMaterials : null;
                    if (aam4)
                    {
                        _donorPylonMeshAam4 = mesh;
                        _donorPylonMatsAam4 = mats;
                    }
                    else
                    {
                        _donorPylonMeshAam2 = mesh;
                        _donorPylonMatsAam2 = mats;
                    }
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static GameObject _tbmPrefab;
        private static bool _tbmGoScanTried;

        private static GameObject FindTbmPrefab()
        {
            if (_tbmPrefab != null)
                return _tbmPrefab;
            try
            {
                MissileDefinition[] defs = Resources.FindObjectsOfTypeAll<MissileDefinition>();
                if (defs != null)
                {
                    for (int i = 0; i < defs.Length; i++)
                    {
                        MissileDefinition d = defs[i];
                        if (d == null || d.unitPrefab == null || string.IsNullOrEmpty(d.jsonKey))
                            continue;
                        if (!string.Equals(d.jsonKey, "ballisticMissile1", StringComparison.OrdinalIgnoreCase))
                            continue;
                        if (FindNamedChild(d.unitPrefab.transform, "FireParticles") == null)
                            continue;
                        _tbmPrefab = d.unitPrefab;
                        return _tbmPrefab;
                    }
                }
            }
            catch { }
            if (_tbmGoScanTried)
                return null;
            _tbmGoScanTried = true;
            try
            {
                GameObject[] all = Resources.FindObjectsOfTypeAll<GameObject>();
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        GameObject go = all[i];
                        if (go == null || go.name == null)
                            continue;
                        if (go.name.IndexOf("ballisticMissile1", StringComparison.OrdinalIgnoreCase) < 0)
                            continue;
                        if (FindNamedChild(go.transform, "FireParticles") == null)
                            continue;
                        _tbmPrefab = go;
                        return _tbmPrefab;
                    }
                }
            }
            catch { }
            return null;
        }

        private static void CloneExhaustChild(GameObject src, Transform parent, Vector3 localPos, string name)
        {
            GameObject clone = UnityEngine.Object.Instantiate(src);
            clone.name = name;
            clone.transform.SetParent(parent, false);
            clone.transform.localPosition = localPos;
            clone.transform.localRotation = src.transform.localRotation;
            clone.transform.localScale = src.transform.localScale;
            clone.SetActive(true);
            SetLayerRecursive(clone.transform, parent.gameObject.layer);
        }

        private static void SetLayerRecursive(Transform t, int layer)
        {
            if (t == null)
                return;
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++)
                SetLayerRecursive(t.GetChild(i), layer);
        }

        private static void DisableDonorExhaust(Transform root)
        {
            if (root == null)
                return;
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                Transform t = all[i];
                if (t == null || t == root)
                    continue;
                if (t.name == TbmExhaustHolderName || t.name == VisualChildName)
                    continue;
                if (t.name != null && t.name.StartsWith("WeXon_", StringComparison.Ordinal))
                    continue;
                if (!IsExhaustName(t.name) && (t.name == null
                    || t.name.IndexOf("Fire", StringComparison.OrdinalIgnoreCase) < 0))
                    continue;
                try { t.gameObject.SetActive(false); }
                catch { }
            }
        }

        private static TbmExhaustBind CollectTbmBind(Transform hold)
        {
            TbmExhaustBind bind = new TbmExhaustBind();
            if (hold == null)
                return bind;
            List<Component> ps = new List<Component>(8);
            Component[] comps = hold.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < comps.Length; i++)
            {
                Component c = comps[i];
                if (c == null)
                    continue;
                if (c.GetType().Name == "ParticleSystem")
                    ps.Add(c);
            }
            bind.ParticleSystems = ps.ToArray();
            bind.Trails = hold.GetComponentsInChildren<TrailEmitter>(true);
            bind.Lights = hold.GetComponentsInChildren<Light>(true);
            return bind;
        }

        private static readonly FieldInfo TrailRbField = AccessTools.Field(typeof(TrailEmitter), "rb");
        private static readonly FieldInfo TrailEmitXfField = AccessTools.Field(typeof(TrailEmitter), "emitTransform");

        private static void RebindTrailEmitters(TbmExhaustBind bind, GameObject root)
        {
            if (bind == null || bind.Trails == null || root == null)
                return;
            Rigidbody rb = null;
            try
            {
                Missile m = root.GetComponent<Missile>();
                if (m != null)
                    rb = m.rb;
            }
            catch { }
            if (rb == null)
                rb = root.GetComponent<Rigidbody>();
            for (int i = 0; i < bind.Trails.Length; i++)
            {
                TrailEmitter te = bind.Trails[i];
                if (te == null)
                    continue;
                try
                {
                    if (TrailRbField != null)
                        TrailRbField.SetValue(te, rb);
                    te.rb = rb;
                }
                catch { }
                try
                {
                    if (TrailEmitXfField != null)
                        TrailEmitXfField.SetValue(te, te.transform);
                }
                catch { }
            }
        }

        private static void PlayParticleSystems(TbmExhaustBind bind)
        {
            if (bind == null || bind.ParticleSystems == null)
                return;
            for (int i = 0; i < bind.ParticleSystems.Length; i++)
            {
                Component c = bind.ParticleSystems[i];
                if (c == null)
                    continue;
                try
                {
                    MethodInfo play = c.GetType().GetMethod("Play", Type.EmptyTypes);
                    if (play != null)
                        play.Invoke(c, null);
                }
                catch { }
            }
        }

        private static Transform FindNamedChild(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name))
                return null;
            if (string.Equals(root.name, name, StringComparison.OrdinalIgnoreCase))
                return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform hit = FindNamedChild(root.GetChild(i), name);
                if (hit != null)
                    return hit;
            }
            return null;
        }

        private static void ApplyToRoot(GameObject root)
        {
            ApplyToRoot(root, false);
        }

        private static void ApplyToRoot(GameObject root, bool hangar)
        {
            if (root == null)
                return;
            if (root.transform.Find(MarkerName) != null)
                return;
            if (!EnsureLoaded())
            {
                if (Plugin.DebugLog != null && Plugin.DebugLog.Value && !string.IsNullOrEmpty(_lastError))
                    Plugin.Log.LogWarning("AGM-T visual: " + _lastError);
                return;
            }

            try
            {
                GameObject marker = new GameObject(MarkerName);
                marker.transform.SetParent(root.transform, false);

                GameObject vis = new GameObject(VisualChildName);
                vis.transform.SetParent(root.transform, false);

                float scale = Plugin.AgmTBusVisualScale != null ? Plugin.AgmTBusVisualScale.Value : 1f;
                Vector3 euler = ParseVec3(Plugin.AgmTBusVisualEuler != null
                    ? Plugin.AgmTBusVisualEuler.Value : "0,0,0");
                Vector3 extra = ParseVec3(Plugin.AgmTBusVisualOffset != null
                    ? Plugin.AgmTBusVisualOffset.Value : "0,0,0");
                // AAM-IV.obj origin is the tail, body on +Z / +Y — hang from the top center
                // so the bus sits under the pylon instead of in front of / into the wing.
                vis.transform.localPosition = AutoAlignOffset(hangar) + extra;
                vis.transform.localRotation = Quaternion.Euler(euler);
                vis.transform.localScale = new Vector3(scale, scale, scale);

                MeshFilter filter = vis.AddComponent<MeshFilter>();
                filter.sharedMesh = _mesh;
                MeshRenderer renderer = vis.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _material;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;

                MeshRenderer[] mrs = root.GetComponentsInChildren<MeshRenderer>(true);
                for (int i = 0; i < mrs.Length; i++)
                {
                    MeshRenderer mr = mrs[i];
                    if (mr == null || mr.transform == null || IsKeepVisibleRenderer(mr.transform.name))
                        continue;
                    mr.enabled = false;
                }

                AlignExhaustToVisualTail(root.transform, vis.transform);

                if (Plugin.DebugLog != null && Plugin.DebugLog.Value)
                    Plugin.Log.LogInfo("AGM-T visual applied on " + root.name
                        + (hangar ? " (hangar)" : " (flight)"));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("AGM-T visual apply failed: " + ex.Message);
            }
        }

        private static Vector3 AutoAlignOffset(bool hangar)
        {
            if (_mesh == null)
                return Vector3.zero;
            Bounds b = _mesh.bounds;
            float y = hangar ? (-b.max.y + 0.04f) : -b.center.y;
            return new Vector3(-b.center.x, y, -b.center.z);
        }

        /// <summary>
        /// AAM-29 motor FX stay on the donor nozzle. After the AAM-IV body is recentered,
        /// slide exhaust / flame / motor trails to the visual tail (min-Z of the OBJ).
        /// </summary>
        private static void AlignExhaustToVisualTail(Transform root, Transform vis)
        {
            if (root == null || vis == null || _mesh == null)
                return;
            Vector3 tailLocal = VisualTailInRoot(vis);
            List<Transform> fx = CollectExhaustTransforms(root, vis);
            if (fx.Count == 0)
                return;

            Vector3 centroid = Vector3.zero;
            int n = 0;
            for (int i = 0; i < fx.Count; i++)
            {
                Transform t = fx[i];
                if (t == null)
                    continue;
                centroid += root.InverseTransformPoint(t.position);
                n++;
            }
            if (n <= 0)
                return;
            centroid /= n;
            Vector3 delta = tailLocal - centroid;
            if (delta.sqrMagnitude < 0.0001f)
                return;

            for (int i = 0; i < fx.Count; i++)
            {
                Transform t = fx[i];
                if (t == null)
                    continue;
                t.position = t.position + root.TransformVector(delta);
            }
        }

        private static Vector3 VisualTailInRoot(Transform vis)
        {
            Bounds b = _mesh.bounds;
            // Missile +Z is nose. OBJ tail is min Z; sit the flame just aft of the nozzle.
            Vector3 meshTail = new Vector3(b.center.x, b.center.y, b.min.z - 0.07f);
            return vis.localPosition + vis.localRotation * Vector3.Scale(meshTail, vis.localScale);
        }

        private static List<Transform> CollectExhaustTransforms(Transform root, Transform vis)
        {
            List<Transform> raw = new List<Transform>(16);
            Component[] comps = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < comps.Length; i++)
            {
                Component c = comps[i];
                if (c == null)
                    continue;
                string tn = c.GetType().Name;
                if (tn == "ParticleSystem" || tn == "TrailRenderer")
                    AddExhaustCandidate(raw, c.transform, vis);
            }
            TrailEmitter[] trails = root.GetComponentsInChildren<TrailEmitter>(true);
            for (int i = 0; i < trails.Length; i++)
            {
                if (trails[i] != null)
                    AddExhaustCandidate(raw, trails[i].transform, vis);
            }
            Light[] lights = root.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null)
                    AddExhaustCandidate(raw, lights[i].transform, vis);
            }
            MeshRenderer[] mrs = root.GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < mrs.Length; i++)
            {
                MeshRenderer mr = mrs[i];
                if (mr == null || mr.transform == null)
                    continue;
                if (!IsExhaustName(mr.transform.name))
                    continue;
                AddExhaustCandidate(raw, mr.transform, vis);
            }

            List<Transform> unique = new List<Transform>(raw.Count);
            for (int i = 0; i < raw.Count; i++)
            {
                Transform t = raw[i];
                if (t == null)
                    continue;
                bool childOfOther = false;
                for (int j = 0; j < raw.Count; j++)
                {
                    if (i == j || raw[j] == null)
                        continue;
                    if (t.IsChildOf(raw[j]) && !object.ReferenceEquals(t, raw[j]))
                    {
                        childOfOther = true;
                        break;
                    }
                }
                if (!childOfOther && !unique.Contains(t))
                    unique.Add(t);
            }
            return unique;
        }

        private static void AddExhaustCandidate(List<Transform> list, Transform t, Transform vis)
        {
            if (t == null || vis == null)
                return;
            if (t == vis || t.IsChildOf(vis))
                return;
            if (t == vis.parent)
                return;
            if (IsMountHardwareName(t.name))
                return;
            list.Add(t);
        }

        private static bool IsExhaustName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            return name.IndexOf("Trail", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Flame", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Exhaust", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Nozzle", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Engine", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Boost", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Particle", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsMountHardwareName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            return name.IndexOf("pylon", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("plug", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("rack", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("rail", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("launcher", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsKeepVisibleRenderer(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;
            if (name.IndexOf(VisualChildName, StringComparison.Ordinal) >= 0)
                return true;
            if (name.IndexOf("Trail", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Flame", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Exhaust", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Particle", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (name.IndexOf("pylon", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("plug", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("rack", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("rail", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("launcher", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            return false;
        }

        private static bool EnsureLoaded()
        {
            if (_mesh != null && _material != null)
                return true;
            if (_loadAttempted)
                return false;
            _loadAttempted = true;

            string objPath = ResolveAssetPath("AAM-IV.obj");
            string texPath = ResolveAssetPath("texture.Aircraft_export.png");
            if (string.IsNullOrEmpty(objPath))
            {
                _lastError = "AAM-IV.obj not found (expected BepInEx/plugins/WeXonAssets/)";
                return false;
            }

            try
            {
                _mesh = ObjMeshLoader.Load(objPath);
                if (_mesh == null)
                {
                    _lastError = "OBJ parse failed: " + objPath;
                    return false;
                }
                _mesh.name = "AAM-IV";

                Texture2D tex = null;
                if (!string.IsNullOrEmpty(texPath) && File.Exists(texPath))
                    tex = LoadTexture(texPath);

                Shader shader = Shader.Find("Standard");
                if (shader == null)
                    shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                    shader = Shader.Find("Diffuse");
                if (shader == null)
                    shader = Shader.Find("Unlit/Texture");
                if (shader == null)
                    shader = Shader.Find("Sprites/Default");

                _material = new Material(shader);
                _material.name = "WeXon_AAMIV";
                if (tex != null)
                {
                    _material.mainTexture = tex;
                    if (_material.HasProperty("_BaseMap"))
                        _material.SetTexture("_BaseMap", tex);
                    if (_material.HasProperty("_MainTex"))
                        _material.SetTexture("_MainTex", tex);
                }
                else
                {
                    _material.color = new Color(0.55f, 0.58f, 0.62f, 1f);
                }

                Plugin.Log.LogInfo("AGM-T visual loaded verts=" + _mesh.vertexCount
                    + " tris=" + (_mesh.triangles != null ? (_mesh.triangles.Length / 3) : 0)
                    + " from " + objPath);
                return true;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return false;
            }
        }

        private static Texture2D LoadTexture(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            if (!ImageConversion.LoadImage(tex, bytes, false))
            {
                UnityEngine.Object.Destroy(tex);
                return null;
            }
            tex.name = Path.GetFileNameWithoutExtension(path);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            return tex;
        }

        internal static string ResolveAssetPath(string fileName)
        {
            try
            {
                string p = Path.Combine(Paths.PluginPath, "WeXonAssets", fileName);
                if (File.Exists(p))
                    return p;
            }
            catch { }

            try
            {
                string asm = Assembly.GetExecutingAssembly().Location;
                if (!string.IsNullOrEmpty(asm))
                {
                    string dir = Path.GetDirectoryName(asm);
                    string p1 = Path.Combine(dir, "WeXonAssets", fileName);
                    if (File.Exists(p1))
                        return p1;
                    string p2 = Path.Combine(dir, "assets", fileName);
                    if (File.Exists(p2))
                        return p2;
                }
            }
            catch { }

            try
            {
                // Dev fallback
                string modAssets = Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty,
                    "..", "..", "NOWeaponSuite", "assets", fileName);
                modAssets = Path.GetFullPath(modAssets);
                if (File.Exists(modAssets))
                    return modAssets;
            }
            catch { }

            return null;
        }

        private static Vector3 ParseVec3(string s)
        {
            if (string.IsNullOrEmpty(s))
                return Vector3.zero;
            string[] parts = s.Split(new char[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
                return Vector3.zero;
            float x, y, z;
            if (!float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x))
                x = 0f;
            if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y))
                y = 0f;
            if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out z))
                z = 0f;
            return new Vector3(x, y, z);
        }
    }

    /// <summary>Minimal OBJ → Unity Mesh (fan-triangulated, UVs/normals when present).</summary>
    internal static class ObjMeshLoader
    {
        public static Mesh Load(string path)
        {
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            List<Vector3> positions = new List<Vector3>(8192);
            List<Vector2> uvs = new List<Vector2>(8192);
            List<Vector3> normals = new List<Vector3>(8192);

            List<Vector3> outPos = new List<Vector3>(16384);
            List<Vector2> outUv = new List<Vector2>(16384);
            List<Vector3> outNrm = new List<Vector3>(16384);
            List<int> tris = new List<int>(32768);
            Dictionary<string, int> remap = new Dictionary<string, int>(16384);
            bool anyUv = false;
            bool anyNrm = false;

            for (int li = 0; li < lines.Length; li++)
            {
                string line = lines[li];
                if (string.IsNullOrEmpty(line) || line[0] == '#')
                    continue;
                if (line.Length < 2)
                    continue;

                if (line[0] == 'v' && line[1] == ' ')
                {
                    string[] p = SplitWs(line);
                    if (p.Length >= 4)
                        positions.Add(new Vector3(ParseF(p[1]), ParseF(p[2]), ParseF(p[3])));
                }
                else if (line.StartsWith("vt ", StringComparison.Ordinal))
                {
                    string[] p = SplitWs(line);
                    if (p.Length >= 3)
                        uvs.Add(new Vector2(ParseF(p[1]), ParseF(p[2])));
                }
                else if (line.StartsWith("vn ", StringComparison.Ordinal))
                {
                    string[] p = SplitWs(line);
                    if (p.Length >= 4)
                        normals.Add(new Vector3(ParseF(p[1]), ParseF(p[2]), ParseF(p[3])));
                }
                else if (line[0] == 'f' && line[1] == ' ')
                {
                    string[] p = SplitWs(line);
                    if (p.Length < 4)
                        continue;
                    int[] idx = new int[p.Length - 1];
                    for (int i = 1; i < p.Length; i++)
                    {
                        bool gotUv = false;
                        bool gotNrm = false;
                        idx[i - 1] = AddVertex(p[i], positions, uvs, normals,
                            outPos, outUv, outNrm, remap, out gotUv, out gotNrm);
                        if (gotUv)
                            anyUv = true;
                        if (gotNrm)
                            anyNrm = true;
                    }
                    for (int i = 1; i + 1 < idx.Length; i++)
                    {
                        tris.Add(idx[0]);
                        tris.Add(idx[i]);
                        tris.Add(idx[i + 1]);
                    }
                }
            }

            if (outPos.Count == 0 || tris.Count == 0)
                return null;

            // Pad missing UV/normal channels so array lengths match
            while (outUv.Count < outPos.Count)
                outUv.Add(Vector2.zero);
            while (outNrm.Count < outPos.Count)
                outNrm.Add(Vector3.up);

            Mesh mesh = new Mesh();
            if (outPos.Count > 65535)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = outPos.ToArray();
            if (anyUv)
                mesh.uv = outUv.ToArray();
            if (anyNrm)
                mesh.normals = outNrm.ToArray();
            mesh.triangles = tris.ToArray();
            if (!anyNrm)
                mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            try { mesh.RecalculateTangents(); }
            catch { }
            return mesh;
        }

        private static int AddVertex(
            string token,
            List<Vector3> positions,
            List<Vector2> uvs,
            List<Vector3> normals,
            List<Vector3> outPos,
            List<Vector2> outUv,
            List<Vector3> outNrm,
            Dictionary<string, int> remap,
            out bool gotUv,
            out bool gotNrm)
        {
            gotUv = false;
            gotNrm = false;
            int existing;
            if (remap.TryGetValue(token, out existing))
                return existing;

            int vi = -1;
            int ti = -1;
            int ni = -1;
            string[] bits = token.Split('/');
            if (bits.Length > 0 && bits[0].Length > 0)
                vi = ParseIndex(bits[0], positions.Count);
            if (bits.Length > 1 && bits[1].Length > 0)
                ti = ParseIndex(bits[1], uvs.Count);
            if (bits.Length > 2 && bits[2].Length > 0)
                ni = ParseIndex(bits[2], normals.Count);

            Vector3 pos = (vi >= 0 && vi < positions.Count) ? positions[vi] : Vector3.zero;
            outPos.Add(pos);

            if (ti >= 0 && ti < uvs.Count)
            {
                outUv.Add(uvs[ti]);
                gotUv = true;
            }
            else
            {
                outUv.Add(Vector2.zero);
            }

            if (ni >= 0 && ni < normals.Count)
            {
                outNrm.Add(normals[ni]);
                gotNrm = true;
            }
            else
            {
                outNrm.Add(Vector3.up);
            }

            int id = outPos.Count - 1;
            remap[token] = id;
            return id;
        }

        private static int ParseIndex(string s, int count)
        {
            int v;
            if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                return -1;
            if (v < 0)
                return count + v;
            return v - 1;
        }

        private static float ParseF(string s)
        {
            float f;
            if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out f))
                return f;
            return 0f;
        }

        private static string[] SplitWs(string line)
        {
            return line.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
