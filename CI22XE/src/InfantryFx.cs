using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Oritasy
{
    /// <summary>Hitscan tracers, muzzle flash, and impact sparks (no ParticleSystem module).</summary>
    internal static class InfantryFx
    {
        private static Material _unlit;
        private static Material _add;
        private static bool _tried;
        private static GameObject _g11Ghost;

        internal static Material UnlitMat()
        {
            Ensure();
            return _unlit;
        }

        internal static Material AddMat()
        {
            Ensure();
            return _add != null ? _add : _unlit;
        }

        internal static Material Tint(Color c)
        {
            Material src = UnlitMat();
            if (src == null)
                return null;
            Material m = new Material(src);
            m.color = c;
            return m;
        }

        internal static void DressSlug(GameObject go, Color color, float length)
        {
            DressSlug(go, color, length, false);
        }

        internal static void DressSlug(GameObject go, Color color, float length, bool bright)
        {
            if (go == null)
                return;
            if (length < 0.6f)
                length = 0.6f;
            if (bright && length < 2.4f)
                length = 2.4f;
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = 2;
            lr.SetPosition(0, new Vector3(0f, 0f, 0.04f));
            lr.SetPosition(1, new Vector3(0f, 0f, -length));
            lr.startWidth = bright ? 0.028f : 0.012f;
            lr.endWidth = bright ? 0.006f : 0.0025f;
            lr.numCapVertices = 1;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            Material add = AddMat();
            if (add != null)
                lr.material = add;
            else
            {
                Material mat = UnlitMat();
                if (mat != null)
                    lr.material = mat;
            }
            lr.startColor = color;
            lr.endColor = new Color(color.r, color.g * 0.45f, color.b * 0.12f, 0f);

            TrailRenderer tr = go.AddComponent<TrailRenderer>();
            tr.time = bright ? 0.16f : 0.07f;
            tr.startWidth = bright ? 0.055f : 0.018f;
            tr.endWidth = 0.002f;
            tr.minVertexDistance = 0.08f;
            tr.shadowCastingMode = ShadowCastingMode.Off;
            if (add != null)
                tr.material = add;
            tr.startColor = color;
            tr.endColor = new Color(color.r, color.g * 0.35f, 0.05f, 0f);

            GameObject tip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            tip.name = "Tip";
            tip.transform.SetParent(go.transform, false);
            tip.transform.localPosition = Vector3.zero;
            tip.transform.localScale = Vector3.one * (bright ? 0.055f : 0.028f);
            Collider col = tip.GetComponent<Collider>();
            if (col != null)
                UnityEngine.Object.Destroy(col);
            Renderer r = tip.GetComponent<Renderer>();
            Material tm = Tint(color);
            if (r != null && tm != null)
            {
                r.sharedMaterial = tm;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
        }

        internal static void DressG11Dart(GameObject go, Color color)
        {
            if (go == null)
                return;
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.name = "Dart";
            body.transform.SetParent(go.transform, false);
            body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            body.transform.localScale = new Vector3(0.05f, 0.16f, 0.05f);
            Collider col = body.GetComponent<Collider>();
            if (col != null)
                UnityEngine.Object.Destroy(col);
            Renderer r = body.GetComponent<Renderer>();
            Material tm = Tint(color);
            if (r != null && tm != null)
            {
                r.sharedMaterial = tm;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            TrailRenderer tr = go.AddComponent<TrailRenderer>();
            tr.time = 2.6f;
            tr.startWidth = 0.1f;
            tr.endWidth = 0.016f;
            tr.minVertexDistance = 0.08f;
            tr.shadowCastingMode = ShadowCastingMode.Off;
            Material add = AddMat();
            if (add != null)
                tr.material = add;
            tr.startColor = color;
            tr.endColor = new Color(color.r, color.g * 0.22f, color.b * 0.12f, 0.18f);
        }

        internal static void DressShellSlug(GameObject go, Color color, Mesh mesh, Material srcMat)
        {
            if (go == null)
                return;
            if (mesh == null)
            {
                DressSlug(go, color, 1.1f, true);
                return;
            }
            MeshFilter mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            Material mat = srcMat != null ? new Material(srcMat) : Tint(color);
            if (mat != null)
            {
                try { mat.color = color; }
                catch { }
                r.sharedMaterial = mat;
            }
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            go.transform.localScale = new Vector3(0.16f, 0.16f, 0.16f);
            TrailRenderer tr = go.AddComponent<TrailRenderer>();
            tr.time = 2.4f;
            tr.startWidth = 0.085f;
            tr.endWidth = 0.012f;
            tr.minVertexDistance = 0.08f;
            tr.shadowCastingMode = ShadowCastingMode.Off;
            Material add = AddMat();
            if (add != null)
                tr.material = add;
            tr.startColor = color;
            tr.endColor = new Color(color.r, color.g * 0.25f, color.b * 0.15f, 0.15f);
        }

        internal static void ShowG11Path(List<Vector3> pts)
        {
            if (_g11Ghost != null)
            {
                UnityEngine.Object.Destroy(_g11Ghost);
                _g11Ghost = null;
            }
            if (pts == null || pts.Count < 2)
                return;
            GameObject go = new GameObject("OritasyG11Path");
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = pts.Count;
            for (int i = 0; i < pts.Count; i++)
                lr.SetPosition(i, pts[i]);
            lr.startWidth = 0.14f;
            lr.endWidth = 0.045f;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            Material mat = Tint(new Color(1f, 0.08f, 0.04f, 1f));
            if (mat == null)
                mat = AddMat();
            if (mat != null)
                lr.material = mat;
            Color red = new Color(1f, 0.08f, 0.04f, 0.95f);
            lr.startColor = red;
            lr.endColor = new Color(1f, 0.22f, 0.08f, 0.4f);
            UnityEngine.Object.Destroy(go, 8f);
            _g11Ghost = go;
        }

        internal static void Muzzle(Vector3 pos)
        {
            Vector3 fwd = Vector3.forward;
            Camera cam = Camera.main;
            if (cam != null)
                fwd = cam.transform.forward;
            Muzzle(pos, fwd);
        }

        internal static void Muzzle(Vector3 pos, Vector3 fwd)
        {
            if (fwd.sqrMagnitude < 0.01f)
                fwd = Vector3.forward;
            fwd.Normalize();
            Vector3 hint = Mathf.Abs(fwd.y) < 0.92f ? Vector3.up : Vector3.right;
            Vector3 right = Vector3.Cross(hint, fwd);
            if (right.sqrMagnitude < 0.001f)
                right = Vector3.right;
            right.Normalize();
            Vector3 up = Vector3.Cross(fwd, right);
            float life = 0.04f;
            FlameLine(pos, pos + fwd * 0.32f, 0.038f, 0.005f, new Color(1f, 0.97f, 0.78f, 1f), life);
            FlameLine(pos, pos + fwd * 0.20f, 0.062f, 0.010f, new Color(1f, 0.55f, 0.10f, 1f), life);
            float spin = UnityEngine.Random.Range(0f, 60f);
            for (int i = 0; i < 5; i++)
            {
                float ang = (spin + i * 72f) * Mathf.Deg2Rad;
                Vector3 tip = fwd * UnityEngine.Random.Range(0.14f, 0.26f)
                    + (right * Mathf.Cos(ang) + up * Mathf.Sin(ang)) * 0.08f;
                FlameLine(pos, pos + tip, 0.028f, 0.003f,
                    i % 2 == 0 ? new Color(1f, 0.62f, 0.12f, 1f) : new Color(1f, 0.88f, 0.42f, 1f),
                    life);
            }
        }

        internal static void Sparks(Vector3 pos, Vector3 normal)
        {
            Vector3 n = normal.sqrMagnitude > 0.01f ? normal.normalized : Vector3.up;
            SpawnFlash(pos + n * 0.02f, 0.07f, new Color(1f, 0.78f, 0.28f, 1f), 0.06f);
            int nBits = 16;
            for (int i = 0; i < nBits; i++)
            {
                Vector3 rand = UnityEngine.Random.insideUnitSphere;
                Vector3 dir = (n + rand * 0.85f).normalized;
                float spd = UnityEngine.Random.Range(4f, 11f);
                SpawnBit(pos, dir * spd, UnityEngine.Random.Range(0.016f, 0.032f),
                    UnityEngine.Random.value > 0.35f
                        ? new Color(1f, 0.72f, 0.18f)
                        : new Color(1f, 0.92f, 0.55f),
                    UnityEngine.Random.Range(0.12f, 0.28f));
            }
        }

        internal static void Blast(Vector3 pos)
        {
            SpawnFlash(pos, 0.55f, new Color(1f, 0.45f, 0.12f, 1f), 0.12f);
            SpawnFlash(pos, 0.22f, new Color(1f, 0.85f, 0.4f, 1f), 0.08f);
            int nBits = 28;
            for (int i = 0; i < nBits; i++)
            {
                Vector3 dir = UnityEngine.Random.onUnitSphere;
                float spd = UnityEngine.Random.Range(6f, 18f);
                SpawnBit(pos, dir * spd, UnityEngine.Random.Range(0.02f, 0.045f),
                    new Color(1f, UnityEngine.Random.Range(0.35f, 0.75f), 0.08f),
                    UnityEngine.Random.Range(0.18f, 0.4f));
            }
        }

        internal static void DressBolt(GameObject go, Color color)
        {
            if (go == null)
                return;
            Renderer r = go.GetComponent<Renderer>();
            Material m = Tint(color);
            if (r != null && m != null)
            {
                r.sharedMaterial = m;
                r.shadowCastingMode = ShadowCastingMode.Off;
            }
            TrailRenderer tr = go.AddComponent<TrailRenderer>();
            tr.time = 0.18f;
            tr.startWidth = 0.09f;
            tr.endWidth = 0.01f;
            tr.shadowCastingMode = ShadowCastingMode.Off;
            Material tm = AddMat();
            if (tm != null)
                tr.material = tm;
            tr.startColor = color;
            tr.endColor = new Color(color.r, color.g, color.b, 0f);
        }

        private static void FlameLine(Vector3 a, Vector3 b, float startW, float endW, Color c, float life)
        {
            GameObject go = new GameObject("OritasyFlame");
            LineRenderer lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.positionCount = 2;
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
            lr.startWidth = startW;
            lr.endWidth = endW;
            lr.numCapVertices = 2;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            Material add = AddMat();
            if (add != null)
                lr.material = add;
            lr.startColor = c;
            lr.endColor = new Color(c.r, c.g * 0.35f, c.b * 0.08f, 0f);
            UnityEngine.Object.Destroy(go, life);
        }

        private static void SpawnFlash(Vector3 pos, float size, Color color, float life)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "OritasyInfFlash";
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * size;
            Collider col = go.GetComponent<Collider>();
            if (col != null)
                UnityEngine.Object.Destroy(col);
            Renderer r = go.GetComponent<Renderer>();
            Material m = Tint(color);
            if (r != null && m != null)
            {
                r.sharedMaterial = m;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            UnityEngine.Object.Destroy(go, life);
        }

        private static void SpawnBit(Vector3 pos, Vector3 vel, float size, Color color, float life)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "OritasyInfSpark";
            go.transform.position = pos;
            go.transform.localScale = Vector3.one * size;
            go.transform.rotation = UnityEngine.Random.rotation;
            Collider col = go.GetComponent<Collider>();
            if (col != null)
                UnityEngine.Object.Destroy(col);
            Renderer r = go.GetComponent<Renderer>();
            Material m = Tint(color);
            if (r != null && m != null)
            {
                r.sharedMaterial = m;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            InfantryFxBit bit = go.AddComponent<InfantryFxBit>();
            bit.Vel = vel;
            bit.DieAt = Time.time + life;
        }

        private static void Ensure()
        {
            if (_tried)
                return;
            _tried = true;
            Shader sh = Shader.Find("Hidden/Internal-Colored");
            if (sh == null)
                sh = Shader.Find("Unlit/Color");
            if (sh == null)
                sh = Shader.Find("Sprites/Default");
            if (sh == null)
                sh = Shader.Find("Standard");
            if (sh == null)
                return;
            _unlit = new Material(sh);
            _unlit.hideFlags = HideFlags.HideAndDontSave;
            _unlit.color = Color.white;
            try
            {
                _unlit.SetInt("_Cull", 0);
                _unlit.SetInt("_ZWrite", 1);
            }
            catch { }
            _add = new Material(sh);
            _add.hideFlags = HideFlags.HideAndDontSave;
            _add.color = new Color(1f, 0.85f, 0.35f, 1f);
            try
            {
                _add.SetInt("_SrcBlend", 5);
                _add.SetInt("_DstBlend", 10);
                _add.SetInt("_Cull", 0);
                _add.SetInt("_ZWrite", 0);
            }
            catch { }
        }
    }

    internal sealed class InfantryFxBit : MonoBehaviour
    {
        public Vector3 Vel;
        public float DieAt;

        private void Update()
        {
            float dt = Time.deltaTime;
            Vel += Physics.gravity * dt * 1.4f;
            transform.position += Vel * dt;
            if (Time.time >= DieAt)
                UnityEngine.Object.Destroy(gameObject);
        }
    }
}
