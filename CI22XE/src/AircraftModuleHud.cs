using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// O: third-person airframe overlay. Low-poly boxes on the outside of each
    /// module, colored like the F8 engine monitor.
    /// </summary>
    internal static class AircraftModuleHud
    {
        private const int MaxBoxes = 32;
        private const float Inflate = 1.18f;
        private const float MinExtent = 0.18f;

        private static readonly FieldInfo DetachedFromUnit =
            AccessTools.Field(typeof(UnitPart), "detachedFromUnit");
        private static readonly Vector3[] Corners = new Vector3[8];
        private static readonly Vector2[] GuiPts = new Vector2[8];
        private static readonly List<OverlayBox> Boxes = new List<OverlayBox>(40);
        private static readonly bool[] CornerVis = new bool[8];
        private static readonly int[] EdgeA = new int[] { 0, 2, 4, 6, 0, 1, 4, 5, 0, 1, 2, 3 };
        private static readonly int[] EdgeB = new int[] { 1, 3, 5, 7, 2, 3, 6, 7, 4, 5, 6, 7 };
        private static readonly int[] FaceV = new int[]
        {
            0, 1, 3, 2,
            4, 6, 7, 5,
            0, 4, 5, 1,
            2, 3, 7, 6,
            0, 2, 6, 4,
            1, 5, 7, 3
        };

        private static Material _fillMat;
        private static bool _fillMatTried;
        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<KeyCode> _key;
        private static bool _open;
        private static bool _restoreCockpit;
        private static GUIStyle _title;
        private static GUIStyle _small;
        private static GUIStyle _cell;

        private struct OverlayBox
        {
            public float Depth;
            public float Hp01;
            public string Label;
            public Vector3 C0, C1, C2, C3, C4, C5, C6, C7;
        }

        internal static void Bind(ConfigFile config)
        {
            _enabled = config.Bind("Combat", "AircraftModuleHud", true,
                "O key: third-person low-poly module overlay on the airframe (F8 damage colors).");
            _key = config.Bind("Combat", "AircraftModuleHudKey", KeyCode.O,
                "Toggle own-aircraft module overlay.");
        }

        internal static void Tick()
        {
            if (_enabled != null && !_enabled.Value)
            {
                Close(true);
                return;
            }
            if (OritasyPresentation.SplashActive || JoinMenuFactionFix.SelectionUiOpen())
            {
                Close(true);
                return;
            }
            if (PrivateMessageMenu.MenuOpen)
                return;
            KeyCode k = _key != null ? _key.Value : KeyCode.O;
            if (k == KeyCode.None)
                return;
            if (Input.GetKeyDown(k))
            {
                if (_open)
                    Close(false);
                else
                    Open();
            }
            if (_open && Input.GetKeyDown(KeyCode.Escape))
                Close(false);
            if (_open)
                KeepThirdPerson();
        }

        private static void KeepThirdPerson()
        {
            try
            {
                CameraStateManager csm = SceneSingleton<CameraStateManager>.i;
                if (csm == null)
                    return;
                if (!object.ReferenceEquals(csm.currentState, csm.cockpitState))
                    return;
                if (csm.chaseState != null)
                    csm.SwitchState(csm.chaseState);
                else if (csm.orbitState != null)
                    csm.SwitchState(csm.orbitState);
            }
            catch { }
        }

        internal static void Draw()
        {
            if (!_open)
                return;
            if (_enabled != null && !_enabled.Value)
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

            Aircraft ac = null;
            try
            {
                if (!GameManager.GetLocalAircraft(out ac) || ac == null)
                    return;
            }
            catch { return; }

            Camera cam = ResolveViewCamera();
            if (cam == null)
                return;

            EnsureStyles();
            BuildBoxes(ac);
            SortFarToNear();

            int prevDepth = GUI.depth;
            GUI.depth = -994;
            Color prev = GUI.color;

            for (int i = 0; i < Boxes.Count; i++)
                DrawOne(cam, Boxes[i]);

            DrawLegend(ac);
            GUI.color = prev;
            GUI.depth = prevDepth;
        }

        private static void Open()
        {
            _open = true;
            _restoreCockpit = false;
            try
            {
                CameraStateManager csm = SceneSingleton<CameraStateManager>.i;
                if (csm == null)
                    return;
                if (object.ReferenceEquals(csm.currentState, csm.cockpitState))
                {
                    _restoreCockpit = true;
                    if (csm.chaseState != null)
                        csm.SwitchState(csm.chaseState);
                    else if (csm.orbitState != null)
                        csm.SwitchState(csm.orbitState);
                }
            }
            catch { }
        }

        private static void Close(bool silent)
        {
            if (!_open && !_restoreCockpit)
                return;
            _open = false;
            if (_restoreCockpit && !silent)
            {
                try
                {
                    CameraStateManager csm = SceneSingleton<CameraStateManager>.i;
                    if (csm != null && csm.cockpitState != null)
                        csm.SwitchState(csm.cockpitState);
                }
                catch { }
            }
            _restoreCockpit = false;
        }

        private static Camera ResolveViewCamera()
        {
            try
            {
                CameraStateManager csm = SceneSingleton<CameraStateManager>.i;
                if (csm != null && csm.mainCamera != null && csm.mainCamera.enabled)
                    return csm.mainCamera;
            }
            catch { }
            try
            {
                Camera main = Camera.main;
                if (main != null && main.enabled)
                    return main;
            }
            catch { }
            return null;
        }

        private static void BuildBoxes(Aircraft ac)
        {
            Boxes.Clear();
            List<UnitPart> parts = null;
            try { parts = ac.GetAllParts(); }
            catch { parts = null; }
            if (parts != null)
            {
                for (int i = 0; i < parts.Count; i++)
                    ConsiderPart(ac, parts[i]);
            }
            else
            {
                UnitPart[] all = null;
                try { all = ac.GetComponentsInChildren<UnitPart>(true); }
                catch { }
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                        ConsiderPart(ac, all[i]);
                }
            }
            TrimToLargest();
        }

        private static void ConsiderPart(Aircraft ac, UnitPart part)
        {
            if (part == null || Boxes.Count >= MaxBoxes * 2)
                return;
            if (IsDetached(part))
                return;
            if (!TryPartCorners(part, Inflate, Corners))
                return;

            float hp = 100f;
            try { hp = part.hitPoints; }
            catch { hp = 100f; }
            float hp01 = hp / 100f;
            if (hp01 > 1f)
                hp01 = 1f;
            if (hp01 < 0f)
                hp01 = 0f;

            int mod = ModularArmorService.ClassifyModule(part, false, false, true);
            if (mod == ModularArmorService.ModEngine)
            {
                float eng = EngineHealthHint(ac, part);
                if (eng < hp01)
                    hp01 = eng;
            }

            OverlayBox box = default(OverlayBox);
            box.C0 = Corners[0];
            box.C1 = Corners[1];
            box.C2 = Corners[2];
            box.C3 = Corners[3];
            box.C4 = Corners[4];
            box.C5 = Corners[5];
            box.C6 = Corners[6];
            box.C7 = Corners[7];
            box.Hp01 = hp01;
            box.Depth = DepthOf(Corners);
            string name = ModularArmorService.ModuleLabel(mod);
            box.Label = name + "\n" + Mathf.RoundToInt(hp01 * 100f) + "%";
            Boxes.Add(box);
        }

        private static float EngineHealthHint(Aircraft ac, UnitPart part)
        {
            float worst = 1f;
            int n = 0;
            try { n = WearView.PartCount(); }
            catch { return 1f; }
            if (n <= 0)
                return 1f;
            Vector3 at = Vector3.zero;
            try { at = part.transform.position; }
            catch { }
            int bank = -1;
            float bestD = 1e9f;
            try
            {
                if (ac.engines != null)
                {
                    for (int i = 0; i < ac.engines.Count; i++)
                    {
                        Component c = ac.engines[i] as Component;
                        if (c == null || c.transform == null)
                            continue;
                        float d = (c.transform.position - at).sqrMagnitude;
                        if (d < bestD)
                        {
                            bestD = d;
                            bank = i;
                        }
                    }
                }
            }
            catch { }
            for (int i = 0; i < n; i++)
            {
                WearView.Part p = WearView.GetPart(i);
                if (bank >= 0 && p.Bank >= 0)
                {
                    int phys = p.Bank;
                    int lo = WearView.LoCount();
                    if (lo > 0 && phys >= lo)
                        phys = phys - lo;
                    if (phys != bank)
                        continue;
                }
                if (p.Health < worst)
                    worst = p.Health;
            }
            return worst;
        }

        private static void TrimToLargest()
        {
            if (Boxes.Count <= MaxBoxes)
                return;
            Boxes.Sort(CompareSizeDesc);
            while (Boxes.Count > MaxBoxes)
                Boxes.RemoveAt(Boxes.Count - 1);
        }

        private static int CompareSizeDesc(OverlayBox a, OverlayBox b)
        {
            float sa = SizeHint(a);
            float sb = SizeHint(b);
            if (sa > sb)
                return -1;
            if (sa < sb)
                return 1;
            return 0;
        }

        private static float SizeHint(OverlayBox b)
        {
            Vector3 d = b.C7 - b.C0;
            return d.sqrMagnitude;
        }

        private static void SortFarToNear()
        {
            Boxes.Sort(CompareDepthDesc);
        }

        private static int CompareDepthDesc(OverlayBox a, OverlayBox b)
        {
            if (a.Depth > b.Depth)
                return -1;
            if (a.Depth < b.Depth)
                return 1;
            return 0;
        }

        private static float DepthOf(Vector3[] c)
        {
            float s = 0f;
            for (int i = 0; i < 8; i++)
                s += c[i].x + c[i].y + c[i].z;
            Vector3 mid = (c[0] + c[7]) * 0.5f;
            try
            {
                Camera cam = ResolveViewCamera();
                if (cam != null)
                    return (mid - cam.transform.position).sqrMagnitude;
            }
            catch { }
            return s;
        }

        private static void DrawOne(Camera cam, OverlayBox box)
        {
            Corners[0] = box.C0;
            Corners[1] = box.C1;
            Corners[2] = box.C2;
            Corners[3] = box.C3;
            Corners[4] = box.C4;
            Corners[5] = box.C5;
            Corners[6] = box.C6;
            Corners[7] = box.C7;
            int vis = 0;
            float minX = 1e9f, maxX = -1e9f, minY = 1e9f, maxY = -1e9f;
            Vector2 centroid = Vector2.zero;
            for (int i = 0; i < 8; i++)
            {
                CornerVis[i] = false;
                Vector3 sp;
                try { sp = cam.WorldToScreenPoint(Corners[i]); }
                catch { return; }
                if (sp.z <= 0.15f)
                    continue;
                float gx = UiScaleService.FromScreenX(sp.x);
                float gy = UiScaleService.FromScreenYFlipped(sp.y);
                GuiPts[i] = new Vector2(gx, gy);
                CornerVis[i] = true;
                vis++;
                if (gx < minX) minX = gx;
                if (gx > maxX) maxX = gx;
                if (gy < minY) minY = gy;
                if (gy > maxY) maxY = gy;
                centroid += GuiPts[i];
            }
            if (vis < 4)
                return;
            centroid /= vis;
            float bw = maxX - minX;
            float bh = maxY - minY;
            if (bw < 6f || bh < 6f)
                return;

            Color col = AirframeWearSchematic.HealthColor(box.Hp01);
            Color fill = new Color(col.r, col.g, col.b, 0.28f);
            Color line = new Color(col.r, col.g, col.b, 0.95f);
            DrawFilledFaces(cam, fill);

            GUI.color = line;
            for (int e = 0; e < EdgeA.Length; e++)
            {
                int ia = EdgeA[e];
                int ib = EdgeB[e];
                if (!CornerVis[ia] || !CornerVis[ib])
                    continue;
                UiScaleService.DrawLine(GuiPts[ia], GuiPts[ib], 1.7f);
            }

            if (bw >= 36f && bh >= 22f)
            {
                GUI.color = col;
                float lw = Mathf.Max(70f, bw * 0.55f);
                GUI.Label(new Rect(centroid.x - lw * 0.5f, centroid.y - 16f, lw, 34f),
                    box.Label, _cell);
            }
            GUI.color = Color.white;
        }

        private static void DrawFilledFaces(Camera cam, Color fill)
        {
            Material mat = FillMat();
            if (mat == null || cam == null)
                return;
            float sc = UiScaleService.Scale;
            Vector3 camPos = cam.transform.position;
            mat.SetPass(0);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, (float)Screen.width, (float)Screen.height, 0f);
            GL.Begin(GL.TRIANGLES);
            GL.Color(fill);
            for (int f = 0; f < 6; f++)
            {
                int ia = FaceV[f * 4];
                int ib = FaceV[f * 4 + 1];
                int ic = FaceV[f * 4 + 2];
                int id = FaceV[f * 4 + 3];
                if (!CornerVis[ia] || !CornerVis[ib] || !CornerVis[ic] || !CornerVis[id])
                    continue;
                Vector3 e1 = Corners[ib] - Corners[ia];
                Vector3 e2 = Corners[id] - Corners[ia];
                Vector3 n = Vector3.Cross(e1, e2);
                Vector3 mid = (Corners[ia] + Corners[ib] + Corners[ic] + Corners[id]) * 0.25f;
                if (Vector3.Dot(n, camPos - mid) >= 0f)
                    continue;
                EmitGui(GuiPts[ia], sc);
                EmitGui(GuiPts[ib], sc);
                EmitGui(GuiPts[ic], sc);
                EmitGui(GuiPts[ia], sc);
                EmitGui(GuiPts[ic], sc);
                EmitGui(GuiPts[id], sc);
            }
            GL.End();
            GL.PopMatrix();
        }

        private static void EmitGui(Vector2 p, float sc)
        {
            GL.Vertex3(p.x * sc, p.y * sc, 0f);
        }

        private static Material FillMat()
        {
            if (_fillMatTried)
                return _fillMat;
            _fillMatTried = true;
            try
            {
                Shader sh = Shader.Find("Hidden/Internal-Colored");
                if (sh == null)
                    return null;
                _fillMat = new Material(sh);
                _fillMat.hideFlags = HideFlags.HideAndDontSave;
                _fillMat.SetInt("_SrcBlend", 5);
                _fillMat.SetInt("_DstBlend", 10);
                _fillMat.SetInt("_Cull", 0);
                _fillMat.SetInt("_ZWrite", 0);
                _fillMat.SetInt("_ZTest", 8);
            }
            catch
            {
                _fillMat = null;
            }
            return _fillMat;
        }

        private static bool TryPartCorners(UnitPart part, float inflate, Vector3[] dst)
        {
            if (part == null || dst == null || dst.Length < 8)
                return false;

            Transform basis;
            Vector3 locC;
            Vector3 ext;
            if (TryColliderObb(part, inflate, out basis, out locC, out ext))
                return WriteObb(basis, locC, ext, dst);

            MeshFilter mf = null;
            try { mf = part.GetComponent<MeshFilter>(); }
            catch { }
            if (mf == null)
            {
                try { mf = part.GetComponentInChildren<MeshFilter>(); }
                catch { }
            }
            if (mf != null && mf.sharedMesh != null && mf.transform != null)
            {
                Bounds lb = mf.sharedMesh.bounds;
                return WriteObb(mf.transform, lb.center, lb.extents * inflate, dst);
            }

            Renderer rd = null;
            try { rd = part.GetComponent<Renderer>(); }
            catch { }
            if (rd == null)
            {
                try { rd = part.GetComponentInChildren<Renderer>(); }
                catch { }
            }
            if (rd == null)
                return false;
            Bounds b = rd.bounds;
            Vector3 c = b.center;
            Vector3 e = b.extents * inflate;
            if (e.magnitude < MinExtent)
                return false;
            dst[0] = c + new Vector3(-e.x, -e.y, -e.z);
            dst[1] = c + new Vector3(e.x, -e.y, -e.z);
            dst[2] = c + new Vector3(-e.x, e.y, -e.z);
            dst[3] = c + new Vector3(e.x, e.y, -e.z);
            dst[4] = c + new Vector3(-e.x, -e.y, e.z);
            dst[5] = c + new Vector3(e.x, -e.y, e.z);
            dst[6] = c + new Vector3(-e.x, e.y, e.z);
            dst[7] = c + new Vector3(e.x, e.y, e.z);
            return true;
        }

        private static bool TryColliderObb(UnitPart part, float inflate,
            out Transform basis, out Vector3 locC, out Vector3 ext)
        {
            basis = null;
            locC = Vector3.zero;
            ext = Vector3.zero;
            try
            {
                BoxCollider box = part.GetComponent<BoxCollider>();
                if (box != null && box.transform != null)
                {
                    basis = box.transform;
                    locC = box.center;
                    ext = box.size * (0.5f * inflate);
                    return ext.magnitude >= MinExtent;
                }
            }
            catch { }

            try
            {
                CapsuleCollider cap = part.GetComponent<CapsuleCollider>();
                if (cap != null && cap.transform != null)
                {
                    float r = cap.radius * inflate;
                    float h = cap.height * 0.5f * inflate;
                    if (h < r)
                        h = r;
                    if (cap.direction == 0)
                        ext = new Vector3(h, r, r);
                    else if (cap.direction == 2)
                        ext = new Vector3(r, r, h);
                    else
                        ext = new Vector3(r, h, r);
                    basis = cap.transform;
                    locC = cap.center;
                    return ext.magnitude >= MinExtent;
                }
            }
            catch { }

            try
            {
                SphereCollider sph = part.GetComponent<SphereCollider>();
                if (sph != null && sph.transform != null)
                {
                    float r = sph.radius * inflate;
                    basis = sph.transform;
                    locC = sph.center;
                    ext = new Vector3(r, r, r);
                    return ext.magnitude >= MinExtent;
                }
            }
            catch { }
            return false;
        }

        private static bool WriteObb(Transform basis, Vector3 locC, Vector3 ext, Vector3[] dst)
        {
            if (basis == null || dst == null)
                return false;
            if (ext.magnitude < MinExtent)
                return false;
            Vector3 xs = basis.TransformVector(new Vector3(ext.x, 0f, 0f));
            Vector3 ys = basis.TransformVector(new Vector3(0f, ext.y, 0f));
            Vector3 zs = basis.TransformVector(new Vector3(0f, 0f, ext.z));
            Vector3 c2 = basis.TransformPoint(locC);
            dst[0] = c2 - xs - ys - zs;
            dst[1] = c2 + xs - ys - zs;
            dst[2] = c2 - xs + ys - zs;
            dst[3] = c2 + xs + ys - zs;
            dst[4] = c2 - xs - ys + zs;
            dst[5] = c2 + xs - ys + zs;
            dst[6] = c2 - xs + ys + zs;
            dst[7] = c2 + xs + ys + zs;
            return true;
        }

        private static bool IsDetached(UnitPart part)
        {
            try
            {
                if (part.IsDetached())
                    return true;
            }
            catch { }
            if (DetachedFromUnit == null)
                return false;
            try { return (bool)DetachedFromUnit.GetValue(part); }
            catch { return false; }
        }

        private static void DrawLegend(Aircraft ac)
        {
            float x = 14f;
            float y = UiScaleService.Height * 0.14f;
            GUI.color = new Color(0.04f, 0.05f, 0.07f, 0.72f);
            GUI.DrawTexture(new Rect(x, y, 268f, 48f), Texture2D.whiteTexture);
            GUI.color = new Color(0.2f, 0.85f, 0.28f, 0.95f);
            GUI.DrawTexture(new Rect(x, y, 268f, 3f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(x + 8f, y + 6f, 252f, 18f),
                UiLang.T("AIRFRAME MODULES  ·  O", "机体模块  ·  O"), _title);

            int af = ModularArmorService.FacingArmor(ac, ModularArmorMathService.FaceFront);
            int asd = ModularArmorService.FacingArmor(ac, ModularArmorMathService.FaceSide);
            int ar = ModularArmorService.FacingArmor(ac, ModularArmorMathService.FaceRear);
            int at = ModularArmorService.FacingArmor(ac, ModularArmorMathService.FaceTop);
            int ab = ModularArmorService.FacingArmor(ac, ModularArmorMathService.FaceBottom);
            string armor = UiLang.T("AR ", "装甲 ")
                + UiLang.T("F", "前") + af + "  "
                + UiLang.T("S", "侧") + asd + "  "
                + UiLang.T("R", "后") + ar + "  "
                + UiLang.T("T", "顶") + at + "  "
                + UiLang.T("B", "底") + ab;
            try
            {
                if (ModularArmorService.ClassifyUnit(ac) == ModularArmorMathService.KindSuperHeavy)
                    armor = armor + UiLang.T("  SH", "  超重");
            }
            catch { }
            GUI.Label(new Rect(x + 8f, y + 24f, 252f, 18f), armor, _small);
        }

        private static void EnsureStyles()
        {
            if (_title != null)
                return;
            _title = new GUIStyle(GUI.skin.label);
            _title.fontSize = 13;
            _title.fontStyle = FontStyle.Bold;
            _title.alignment = TextAnchor.UpperLeft;
            _title.normal.textColor = new Color(0.95f, 0.96f, 0.92f, 1f);
            _small = new GUIStyle(GUI.skin.label);
            _small.fontSize = 11;
            _small.alignment = TextAnchor.MiddleLeft;
            _small.normal.textColor = new Color(0.82f, 0.84f, 0.86f, 0.95f);
            _cell = new GUIStyle(GUI.skin.label);
            _cell.fontSize = 11;
            _cell.fontStyle = FontStyle.Bold;
            _cell.alignment = TextAnchor.MiddleCenter;
            _cell.wordWrap = true;
            _cell.normal.textColor = Color.white;
        }
    }
}
