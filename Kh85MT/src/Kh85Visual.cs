using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using UnityEngine;
using UnityEngine.Rendering;

namespace Kh85MT
{
    /// <summary>
    /// Visual-only Kh-85MT OBJ/material swap on cloned AGM-68 (AGM_heavy) mounts/missiles.
    /// Keeps donor prefab logic/colliders; only MeshRenderers are hidden and replaced.
    /// </summary>
    internal static class Kh85Visual
    {
        private const string VisualChildName = "Kh85MT_Visual";
        private const string MarkerName = "Kh85MT_Applied";

        private static Mesh _mesh;
        private static Material _material;
        private static Texture2D _albedo;
        private static bool _loadAttempted;
        private static string _lastError;

        internal static void ApplyToMissile(Missile missile)
        {
            if (missile == null)
                return;
            ApplyToRoot(missile.gameObject);
        }

        /// <summary>Hangar rack: style mounted Weapon visuals for Kh-85MT stations.</summary>
        internal static void ApplyToHangarRack(GameObject rackRoot)
        {
            if (rackRoot == null)
                return;
            Weapon[] weapons = rackRoot.GetComponentsInChildren<Weapon>(true);
            for (int i = 0; i < weapons.Length; i++)
            {
                Weapon w = weapons[i];
                if (w == null || w is Gun)
                    continue;
                ApplyToRoot(w.gameObject);
            }
        }

        private static void ApplyToRoot(GameObject root)
        {
            if (root == null)
                return;
            if (root.transform.Find(MarkerName) != null)
                return;
            if (!EnsureLoaded())
            {
                if (Plugin.DebugLog != null && Plugin.DebugLog.Value && !string.IsNullOrEmpty(_lastError))
                    Plugin.Log.LogWarning("Kh-85MT visual: " + _lastError);
                return;
            }

            try
            {
                Shader donorShader = null;
                MeshRenderer[] mrs = root.GetComponentsInChildren<MeshRenderer>(true);
                for (int i = 0; i < mrs.Length; i++)
                {
                    MeshRenderer mr = mrs[i];
                    if (mr == null || mr.transform == null || IsKeepVisibleRenderer(mr.transform.name))
                        continue;
                    if (donorShader == null && mr.sharedMaterial != null
                        && !LooksTransparent(mr.sharedMaterial))
                        donorShader = mr.sharedMaterial.shader;
                }

                // Fresh material from an opaque donor shader (never clone glass mats).
                Material useMat = BuildOpaqueMaterial(donorShader);

                GameObject marker = new GameObject(MarkerName);
                marker.transform.SetParent(root.transform, false);

                GameObject vis = new GameObject(VisualChildName);
                vis.transform.SetParent(root.transform, false);

                float scale = Plugin.VisualScale != null ? Plugin.VisualScale.Value : 1f;
                Vector3 euler = ParseVec3(Plugin.VisualEuler != null
                    ? Plugin.VisualEuler.Value : "0,-90,0");
                Vector3 pos = ParseVec3(Plugin.VisualOffset != null
                    ? Plugin.VisualOffset.Value : "0,0,0");
                vis.transform.localPosition = pos;
                vis.transform.localRotation = Quaternion.Euler(euler);
                vis.transform.localScale = new Vector3(scale, scale, scale);

                MeshFilter filter = vis.AddComponent<MeshFilter>();
                filter.sharedMesh = _mesh;
                MeshRenderer renderer = vis.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = useMat != null ? useMat : _material;
                renderer.shadowCastingMode = ShadowCastingMode.On;
                renderer.receiveShadows = true;

                // Hide donor missile meshes only after the replacement exists.
                // Never hide pylon / rack / rail — those are the hangar mount.
                for (int i = 0; i < mrs.Length; i++)
                {
                    MeshRenderer mr = mrs[i];
                    if (mr == null || mr.transform == null || IsKeepVisibleRenderer(mr.transform.name))
                        continue;
                    mr.enabled = false;
                }

                if (Plugin.DebugLog != null && Plugin.DebugLog.Value)
                    Plugin.Log.LogInfo("Kh-85MT visual applied on " + root.name);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Kh-85MT visual apply failed: " + ex.Message);
            }
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

            string objPath = ResolveAssetPath("Kh-85MT.obj");
            string texPath = ResolveAssetPath("su_kh38_mt_missile_c.jpg");
            if (string.IsNullOrEmpty(objPath))
            {
                _lastError = "Kh-85MT.obj not found (expected BepInEx/plugins/WeXonAssets/)";
                return false;
            }

            try
            {
                string mtlPath = ResolveAssetPath("su_kh_38mt.mtl");
                _mesh = ObjMeshLoader.Load(objPath, mtlPath);
                if (_mesh == null)
                {
                    _lastError = "OBJ parse failed: " + objPath;
                    return false;
                }
                _mesh.name = "Kh-85MT";
                // Kh-85MT OBJ winding faces inward — Unity backface cull then shows the
                // opposite wall's interior (see-through body on hangar / aircraft select).
                ReverseWindingAndNormals(_mesh);
                ApplyUvMode(_mesh);

                if (!string.IsNullOrEmpty(texPath) && File.Exists(texPath))
                    _albedo = LoadTexture(texPath, false);
                // Skip normal/spec maps — wrong workflow maps make URP Lit look glassy.

                _material = BuildOpaqueMaterial(null);
                if (_material == null)
                {
                    _lastError = "No usable opaque shader found";
                    return false;
                }

                Plugin.Log.LogInfo("Kh-85MT visual loaded verts=" + _mesh.vertexCount
                    + " tris=" + (_mesh.triangles != null ? (_mesh.triangles.Length / 3) : 0)
                    + " shader=" + (_material.shader != null ? _material.shader.name : "?")
                    + " albedo=" + (_albedo != null)
                    + " from " + objPath);
                return true;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return false;
            }
        }

        private static Material BuildOpaqueMaterial(Shader preferredShader)
        {
            Material mat = null;
            try
            {
                Shader shader = preferredShader;
                if (shader == null)
                    shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null)
                    shader = Shader.Find("Universal Render Pipeline/Simple Lit");
                if (shader == null)
                    shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null)
                    shader = Shader.Find("Unlit/Texture");
                if (shader == null)
                    shader = Shader.Find("Standard");
                if (shader == null)
                    shader = Shader.Find("Diffuse");
                if (shader == null)
                    return _material;
                // Always construct fresh — cloning donor Material copies glass/alpha state.
                mat = new Material(shader);
            }
            catch
            {
                return _material;
            }

            mat.name = "Kh85MT_Mat";
            ForceOpaque(mat);
            ApplyTextures(mat);
            return mat;
        }

        private static bool LooksTransparent(Material mat)
        {
            if (mat == null)
                return true;
            try
            {
                if (mat.renderQueue >= (int)RenderQueue.Transparent)
                    return true;
                if (mat.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"))
                    return true;
                if (mat.IsKeywordEnabled("_ALPHABLEND_ON") || mat.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON"))
                    return true;
                if (mat.HasProperty("_Surface") && mat.GetFloat("_Surface") > 0.5f)
                    return true;
                if (mat.HasProperty("_BaseColor") && mat.GetColor("_BaseColor").a < 0.98f)
                    return true;
                if (mat.HasProperty("_Color") && mat.GetColor("_Color").a < 0.98f)
                    return true;
                string n = mat.name != null ? mat.name : string.Empty;
                if (n.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("trans", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("window", StringComparison.OrdinalIgnoreCase) >= 0
                    || n.IndexOf("canopy", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            catch { }
            return false;
        }

        private static void ForceOpaque(Material mat)
        {
            if (mat == null)
                return;
            try
            {
                if (mat.HasProperty("_Surface"))
                    mat.SetFloat("_Surface", 0f); // 0 = Opaque (URP)
                if (mat.HasProperty("_Blend"))
                    mat.SetFloat("_Blend", 0f);
                if (mat.HasProperty("_AlphaClip"))
                    mat.SetFloat("_AlphaClip", 0f);
                if (mat.HasProperty("_Cutoff"))
                    mat.SetFloat("_Cutoff", 0f);
                if (mat.HasProperty("_ZWrite"))
                    mat.SetFloat("_ZWrite", 1f);
                if (mat.HasProperty("_SrcBlend"))
                    mat.SetInt("_SrcBlend", (int)BlendMode.One);
                if (mat.HasProperty("_DstBlend"))
                    mat.SetInt("_DstBlend", (int)BlendMode.Zero);
                if (mat.HasProperty("_SrcBlendAlpha"))
                    mat.SetInt("_SrcBlendAlpha", (int)BlendMode.One);
                if (mat.HasProperty("_DstBlendAlpha"))
                    mat.SetInt("_DstBlendAlpha", (int)BlendMode.Zero);
                if (mat.HasProperty("_Mode"))
                    mat.SetFloat("_Mode", 0f); // Standard opaque
                if (mat.HasProperty("_Cull"))
                    mat.SetFloat("_Cull", 2f); // Back
                if (mat.HasProperty("_Metallic"))
                    mat.SetFloat("_Metallic", 0f);
                if (mat.HasProperty("_Glossiness"))
                    mat.SetFloat("_Glossiness", 0.25f);
                if (mat.HasProperty("_Smoothness"))
                    mat.SetFloat("_Smoothness", 0.25f);
                mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.DisableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.DisableKeyword("_SPECULAR_SETUP");
                mat.SetOverrideTag("RenderType", "Opaque");
                mat.renderQueue = (int)RenderQueue.Geometry;
                try { mat.SetShaderPassEnabled("Transparent", false); }
                catch { }
                Color white = new Color(1f, 1f, 1f, 1f);
                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", white);
                if (mat.HasProperty("_Color"))
                    mat.SetColor("_Color", white);
                mat.color = white;
            }
            catch { }
        }

        private static void ApplyTextures(Material mat)
        {
            if (mat == null)
                return;
            if (_albedo != null)
            {
                // Ensure opaque alpha on CPU texture (JPEG should already be opaque).
                EnsureTextureOpaqueAlpha(_albedo);
                mat.mainTexture = _albedo;
                if (mat.HasProperty("_BaseMap"))
                {
                    mat.SetTexture("_BaseMap", _albedo);
                    mat.SetTextureScale("_BaseMap", Vector2.one);
                    mat.SetTextureOffset("_BaseMap", Vector2.zero);
                }
                if (mat.HasProperty("_MainTex"))
                {
                    mat.SetTexture("_MainTex", _albedo);
                    mat.SetTextureScale("_MainTex", Vector2.one);
                    mat.SetTextureOffset("_MainTex", Vector2.zero);
                }
            }
            else
            {
                Color fallback = new Color(0.45f, 0.48f, 0.42f, 1f);
                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", fallback);
                if (mat.HasProperty("_Color"))
                    mat.SetColor("_Color", fallback);
                mat.color = fallback;
            }

            // Paint-like, not chrome/glass.
            if (mat.HasProperty("_Metallic"))
                mat.SetFloat("_Metallic", 0f);
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", 0.2f);
            if (mat.HasProperty("_Glossiness"))
                mat.SetFloat("_Glossiness", 0.2f);
        }

        private static void EnsureTextureOpaqueAlpha(Texture2D tex)
        {
            if (tex == null)
                return;
            try
            {
                Color32[] px = tex.GetPixels32();
                bool dirty = false;
                for (int i = 0; i < px.Length; i++)
                {
                    if (px[i].a != 255)
                    {
                        px[i].a = 255;
                        dirty = true;
                    }
                }
                if (dirty)
                {
                    tex.SetPixels32(px);
                    tex.Apply(true, false);
                }
            }
            catch { }
        }

        private static void ApplyUvMode(Mesh mesh)
        {
            if (mesh == null)
                return;
            string mode = Plugin.UvMode != null ? Plugin.UvMode.Value : "raw";
            if (string.IsNullOrEmpty(mode))
                mode = "raw";
            mode = mode.Trim().ToLowerInvariant();
            if (mode == "raw" || mode == "none" || mode == "off")
                return;

            Vector2[] uvs = mesh.uv;
            if (uvs == null || uvs.Length == 0)
                return;

            for (int i = 0; i < uvs.Length; i++)
            {
                float u = uvs[i].x;
                float v = uvs[i].y;
                if (mode == "flipv" || mode == "flip-v")
                    v = -v;
                else if (mode == "unityflip" || mode == "unity")
                    v = 1f - v;
                else if (mode == "flipu" || mode == "flip-u")
                    u = -u;
                else if (mode == "flipuv" || mode == "flip")
                {
                    u = -u;
                    v = -v;
                }
                else if (mode == "oneplusv")
                    v = 1f + v;
                uvs[i] = new Vector2(u, v);
            }
            mesh.uv = uvs;
        }

        private static void ReverseWindingAndNormals(Mesh mesh)
        {
            if (mesh == null)
                return;
            int[] tris = mesh.triangles;
            if (tris == null || tris.Length < 3)
                return;
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                int tmp = tris[i + 1];
                tris[i + 1] = tris[i + 2];
                tris[i + 2] = tmp;
            }
            mesh.triangles = tris;
            // Drop importer normals (also inward); rebuild outward from new winding.
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            try { mesh.RecalculateTangents(); }
            catch { }
        }

        private static Texture2D LoadTexture(string path, bool linear)
        {
            byte[] bytes = File.ReadAllBytes(path);
            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear);
            if (!ImageConversion.LoadImage(tex, bytes, false))
            {
                UnityEngine.Object.Destroy(tex);
                return null;
            }
            tex.name = Path.GetFileNameWithoutExtension(path);
            tex.wrapMode = TextureWrapMode.Repeat;
            tex.filterMode = FilterMode.Bilinear;
            tex.anisoLevel = 4;
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
                string p = Path.Combine(Paths.PluginPath, "Kh85MTAssets", fileName);
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
                    string pLegacy = Path.Combine(dir, "Kh85MTAssets", fileName);
                    if (File.Exists(pLegacy))
                        return pLegacy;
                    string p2 = Path.Combine(dir, "assets", fileName);
                    if (File.Exists(p2))
                        return p2;
                }
            }
            catch { }

            try
            {
                string loc = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
                string wexon = Path.Combine(loc, "..", "..", "NOWeaponSuite", "assets", fileName);
                wexon = Path.GetFullPath(wexon);
                if (File.Exists(wexon))
                    return wexon;
                string kh = Path.Combine(loc, "..", "..", "Kh85MT", "assets", fileName);
                kh = Path.GetFullPath(kh);
                if (File.Exists(kh))
                    return kh;
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

    /// <summary>Loadout / encyclopedia schematic for Kh-85MT (green wireframe PNG).</summary>
    internal static class Kh85Icon
    {
        private static Sprite _sprite;
        private static bool _loadAttempted;

        internal static Sprite GetWeaponIcon()
        {
            if (_sprite != null)
                return _sprite;

            string path = Kh85Visual.ResolveAssetPath("Kh-85MT_icon.png");
            if (string.IsNullOrEmpty(path))
            {
                if (!_loadAttempted)
                    Plugin.Log.LogWarning("Kh-85MT icon: Kh-85MT_icon.png not found in WeXonAssets/");
                _loadAttempted = true;
                return null;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(tex, bytes, false))
                {
                    UnityEngine.Object.Destroy(tex);
                    Plugin.Log.LogWarning("Kh-85MT icon: failed to decode " + path);
                    return null;
                }
                tex.name = "Kh85MT_IconTex";
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                // Schematic: nose / warhead faces left (match vanilla AGM-style icons).
                FlipTextureHorizontal(tex);
                // Keep black background; green line-art matches vanilla schematic style.
                _sprite = Sprite.Create(
                    tex,
                    new Rect(0f, 0f, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f),
                    100f);
                _sprite.name = "Kh85MT_Icon";
                _loadAttempted = true;
                Plugin.Log.LogInfo("Kh-85MT icon loaded " + tex.width + "x" + tex.height
                    + " (flipped H) from " + path);
                return _sprite;
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Kh-85MT icon load failed: " + ex.Message);
                return null;
            }
        }

        private static void FlipTextureHorizontal(Texture2D tex)
        {
            if (tex == null)
                return;
            int w = tex.width;
            int h = tex.height;
            Color[] src = tex.GetPixels();
            Color[] dst = new Color[src.Length];
            for (int y = 0; y < h; y++)
            {
                int row = y * w;
                for (int x = 0; x < w; x++)
                    dst[row + x] = src[row + (w - 1 - x)];
            }
            tex.SetPixels(dst);
            tex.Apply(false, false);
        }
    }

    /// <summary>Minimal OBJ → Unity Mesh (fan-triangulated, UVs/normals when present).</summary>
    internal static class ObjMeshLoader
    {
        public static Mesh Load(string path)
        {
            return Load(path, null);
        }

        public static Mesh Load(string path, string mtlPath)
        {
            HashSet<string> skipMats = LoadTransparentMaterialNames(mtlPath);
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
            bool skipFaces = false;

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
                else if (line.StartsWith("usemtl ", StringComparison.Ordinal))
                {
                    string matName = line.Substring(7).Trim();
                    skipFaces = skipMats != null && skipMats.Contains(matName);
                }
                else if (line[0] == 'f' && line[1] == ' ')
                {
                    if (skipFaces)
                        continue;
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

        /// <summary>MTL materials with dissolve d &lt; 1 (glass shells) are skipped on import.</summary>
        private static HashSet<string> LoadTransparentMaterialNames(string mtlPath)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(mtlPath) || !File.Exists(mtlPath))
                return set;
            try
            {
                string current = null;
                string[] lines = File.ReadAllLines(mtlPath, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrEmpty(line))
                        continue;
                    if (line.StartsWith("newmtl ", StringComparison.Ordinal))
                    {
                        current = line.Substring(7).Trim();
                    }
                    else if (current != null && (line[0] == 'd' || line.StartsWith("Tr ", StringComparison.Ordinal)))
                    {
                        string[] p = SplitWs(line);
                        if (p.Length >= 2)
                        {
                            float d = ParseF(p[1]);
                            // Wavefront: d=1 opaque; Tr=0 opaque (some exporters invert)
                            if (p[0] == "d" && d < 0.99f)
                                set.Add(current);
                            else if (p[0] == "Tr" && d > 0.01f)
                                set.Add(current);
                        }
                    }
                }
            }
            catch { }
            return set;
        }
    }
}
