using System.Collections.Generic;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// One infantry pad / gear locker / AD post beside each hangar (helipad, medium, shelter).
    /// </summary>
    internal static class InfantryAirbaseStations
    {
        private const float ScanSec = 16f;
        private const float UseRange = 4.2f;

        private static readonly List<Station> All = new List<Station>(64);
        private static float _nextScan;
        private static bool _kitOpen;
        private static Station _kitAt;
        private static GUIStyle _title;
        private static GUIStyle _body;
        private static GUIStyle _btn;
        private static GUIStyle _btnOn;
        private static GUIStyle _tiny;
        private static Texture2D _px;
        private static bool _styles;
        private static Material _matPad;
        private static Material _matGear;
        private static Material _matAd;

        private sealed class Station
        {
            public Hangar Hangar;
            public Transform Pad;
            public Transform Locker;
            public Transform Ad;
            public GameObject Root;
        }

        internal static bool KitOpen
        {
            get { return _kitOpen && InfantryModeService.Active; }
        }

        internal static void CloseKit()
        {
            _kitOpen = false;
            _kitAt = null;
        }

        internal static void OnPawnSpawned()
        {
            CloseKit();
            _nextScan = Time.unscaledTime + 2.4f;
        }

        internal static void Tick()
        {
            if (!InfantryModeService.IsEnabled())
                return;
            if (OritasyPresentation.MainMenuActive)
            {
                Clear();
                return;
            }
            if (!InfantryModeService.Active)
            {
                if (All.Count > 0)
                    Clear();
                return;
            }
            if (Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + ScanSec;
                Scan();
            }
            PollUse();
        }

        internal static void Clear()
        {
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i] != null && All[i].Root != null)
                    UnityEngine.Object.Destroy(All[i].Root);
            }
            All.Clear();
            _kitOpen = false;
            _kitAt = null;
        }

        internal static bool TryPad(Hangar hangar, out Vector3 pos, out float yaw)
        {
            pos = Vector3.zero;
            yaw = 0f;
            Station st = Find(hangar);
            if (st == null || st.Pad == null)
                return false;
            pos = st.Pad.position + Vector3.up * 0.08f;
            yaw = st.Pad.eulerAngles.y;
            return true;
        }

        internal static Vector3 SpawnNear(Hangar hangar)
        {
            Vector3 pos;
            float yaw;
            if (TryPad(hangar, out pos, out yaw))
                return pos;
            if (hangar == null)
                return Vector3.zero;
            return hangar.transform.position + Vector3.up * 0.2f;
        }

        internal static void Draw()
        {
            EnsureStyles();
            DrawWorldHints();
            if (_kitOpen && _kitAt != null && InfantryModeService.Active)
                DrawKitPanel();
        }

        private static void PollUse()
        {
            if (!InfantryModeService.Active || InfantryModeService.Dead)
            {
                _kitOpen = false;
                return;
            }
            if (!Input.GetKeyDown(KeyCode.F))
                return;
            Station st = Nearest(InfantryModeService.EyePos, UseRange);
            if (st == null)
                return;
            float dLock = Dist(st.Locker);
            float dAd = Dist(st.Ad);
            if (dLock <= UseRange && dLock <= dAd + 0.4f)
            {
                InfantryCombat.Restock();
                _kitOpen = !_kitOpen;
                _kitAt = st;
                InfantryHud.FlashHit(UiLang.T("GEAR LOCKER", "换装点"));
                return;
            }
            if (dAd <= UseRange)
            {
                InfantryCombat.GrantGun(InfantryGun.Rbs70);
                InfantryCombat.Restock();
                InfantryHud.FlashHit(UiLang.T("AD POST  RBS 70", "防空桩  RBS 70"));
            }
        }

        private static void DrawWorldHints()
        {
            if (!InfantryModeService.Active || InfantryModeService.Dead)
                return;
            Station st = Nearest(InfantryModeService.EyePos, 18f);
            if (st == null)
                return;
            string line = "";
            float dLock = Dist(st.Locker);
            float dAd = Dist(st.Ad);
            float dPad = Dist(st.Pad);
            if (dLock < 8f)
                line = UiLang.T("F  gear locker — restock / swap kit", "F  换装点 — 补弹 / 换枪");
            else if (dAd < 8f)
                line = UiLang.T("F  AD post — RBS 70", "F  防空桩 — RBS 70");
            else if (dPad < 8f)
                line = UiLang.T("Infantry pad", "步兵出生点");
            if (string.IsNullOrEmpty(line))
                return;
            GUI.Label(new Rect(Screen.width * 0.5f - 220f, Screen.height * 0.62f, 440f, 22f),
                line, _tiny);
        }

        private static void DrawKitPanel()
        {
            Rect win = new Rect(18f, 90f, 420f, 520f);
            GUI.Box(win, "");
            GUILayout.BeginArea(new Rect(win.x + 8f, win.y + 8f, win.width - 16f, win.height - 16f));
            GUILayout.Label(UiLang.T("Gear locker  (max 4)", "换装点（最多 4 把）"), _title);
            int mask = InfantryCombat.LoadKitMask();
            for (int i = 0; i < InfantryCombat.GunCount; i++)
            {
                InfantryGun g = (InfantryGun)i;
                bool on = InfantryCombat.KitHas(mask, g);
                GUIStyle st = on ? _btnOn : _btn;
                string lab = InfantryCombat.GunName(g)
                    + (on ? UiLang.T("  [TAKE]", "  [带]") : "");
                if (GUILayout.Button(lab, st, GUILayout.Height(32f)))
                {
                    mask = InfantryCombat.ToggleKitGun(mask, g);
                    InfantryCombat.SaveKitMask(mask);
                    InfantryCombat.AllowedMask = mask;
                    InfantryCombat.Restock();
                    InfantryCombat.Gun = InfantryCombat.FirstInMask(mask);
                }
            }
            if (GUILayout.Button(UiLang.T("Close (F)", "关闭（F）"), GUILayout.Height(28f)))
                _kitOpen = false;
            GUILayout.EndArea();
        }

        private static void Scan()
        {
            Hangar[] hangars = null;
            try { hangars = UnityEngine.Object.FindObjectsOfType<Hangar>(); }
            catch { return; }
            if (hangars == null)
                return;
            Prune();
            Vector3 from = InfantryModeService.PawnRoot != null
                ? InfantryModeService.PawnRoot.position
                : InfantryModeService.EyePos;
            Airbase here = NearestAirbase(hangars, from);
            const float maxRange = 700f;
            const float maxRangeSq = maxRange * maxRange;
            for (int i = 0; i < hangars.Length; i++)
            {
                Hangar h = hangars[i];
                if (h == null)
                    continue;
                try
                {
                    if (h.Disabled)
                        continue;
                }
                catch { }
                if (Find(h) != null)
                    continue;
                Airbase hab = null;
                try { hab = h.GetComponentInParent<Airbase>(); }
                catch { }
                bool sameBase = here != null && hab != null && object.ReferenceEquals(here, hab);
                Vector3 hp = h.transform.position;
                if (!sameBase && (hp - from).sqrMagnitude > maxRangeSq)
                    continue;
                Build(h);
            }
        }

        private static Airbase NearestAirbase(Hangar[] hangars, Vector3 from)
        {
            Airbase best = null;
            float bestSq = float.MaxValue;
            if (hangars == null)
                return null;
            for (int i = 0; i < hangars.Length; i++)
            {
                Hangar h = hangars[i];
                if (h == null || h.transform == null)
                    continue;
                float d = (h.transform.position - from).sqrMagnitude;
                if (d >= bestSq)
                    continue;
                Airbase ab = null;
                try { ab = h.GetComponentInParent<Airbase>(); }
                catch { }
                if (ab == null)
                    continue;
                bestSq = d;
                best = ab;
            }
            return best;
        }

        private static void Prune()
        {
            for (int i = All.Count - 1; i >= 0; i--)
            {
                Station st = All[i];
                if (st == null || st.Hangar == null || st.Root == null)
                {
                    if (st != null && st.Root != null)
                        UnityEngine.Object.Destroy(st.Root);
                    All.RemoveAt(i);
                }
            }
        }

        private static void Build(Hangar hangar)
        {
            Transform t = null;
            try { t = hangar.GetSpawnTransform(); }
            catch { }
            if (t == null)
                t = hangar.transform;
            Vector3 right = t.right;
            Vector3 fwd = t.forward;
            Vector3 origin = t.position;
            Vector3 padPos;
            Vector3 gearPos;
            Vector3 adPos;
            if (MorayRuntime.IsPlayerPad(hangar))
            {
                float yawDummy;
                origin = MorayRuntime.PlayerPadPos(hangar, t, out yawDummy);
                padPos = origin;
                gearPos = origin + t.right * 1.6f;
                adPos = origin - t.right * 1.6f;
            }
            else
            {
                padPos = Ground(origin + right * 14f + fwd * 4f);
                gearPos = Ground(origin + right * 14f - fwd * 2.4f);
                adPos = Ground(origin + right * 18f + fwd * 8f);
            }

            GameObject root = new GameObject("OritasyInfStations");
            root.transform.position = padPos;
            Station st = new Station();
            st.Hangar = hangar;
            st.Root = root;
            st.Pad = MakePad(root.transform, padPos, t.eulerAngles.y);
            st.Locker = MakeBox(root.transform, "Locker", gearPos, t.eulerAngles.y,
                new Vector3(1.1f, 1.15f, 1.1f), GearMat());
            st.Ad = MakeAd(root.transform, adPos, t.eulerAngles.y);
            All.Add(st);
        }

        private static Transform MakePad(Transform parent, Vector3 pos, float yaw)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "InfPad";
            go.transform.SetParent(parent, true);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = new Vector3(4.2f, 0.08f, 4.2f);
            Collider c = go.GetComponent<Collider>();
            if (c != null)
                c.enabled = false;
            Renderer r = go.GetComponent<Renderer>();
            if (r != null)
                r.sharedMaterial = PadMat();
            return go.transform;
        }

        private static Transform MakeBox(Transform parent, string name, Vector3 pos, float yaw,
            Vector3 scale, Material mat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, true);
            go.transform.position = pos + Vector3.up * (scale.y * 0.5f);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = scale;
            Collider c = go.GetComponent<Collider>();
            if (c != null)
                c.isTrigger = true;
            Renderer r = go.GetComponent<Renderer>();
            if (r != null)
                r.sharedMaterial = mat;
            return go.transform;
        }

        private static Transform MakeAd(Transform parent, Vector3 pos, float yaw)
        {
            Transform bunker = MakeBox(parent, "AdPost", pos, yaw,
                new Vector3(2.4f, 1.6f, 2.4f), AdMat());
            GameObject tube = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            tube.name = "AdTube";
            tube.transform.SetParent(parent, true);
            tube.transform.position = pos + Vector3.up * 2.1f + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * 0.4f;
            tube.transform.rotation = Quaternion.Euler(55f, yaw, 0f);
            tube.transform.localScale = new Vector3(0.18f, 1.1f, 0.18f);
            Collider c = tube.GetComponent<Collider>();
            if (c != null)
                UnityEngine.Object.Destroy(c);
            Renderer r = tube.GetComponent<Renderer>();
            if (r != null)
                r.sharedMaterial = AdMat();
            return bunker;
        }

        private static Vector3 Ground(Vector3 pos)
        {
            RaycastHit hit;
            if (Physics.Raycast(pos + Vector3.up * 20f, Vector3.down, out hit, 60f, InfantryPerf.SightMask,
                QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * 0.02f;
            return pos;
        }

        private static Station Find(Hangar hangar)
        {
            if (hangar == null)
                return null;
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i] != null && object.ReferenceEquals(All[i].Hangar, hangar))
                    return All[i];
            }
            return null;
        }

        private static Station Nearest(Vector3 from, float max)
        {
            Station best = null;
            float bestD = max;
            for (int i = 0; i < All.Count; i++)
            {
                Station st = All[i];
                if (st == null)
                    continue;
                float d = Dist(st.Pad);
                float a = Dist(st.Locker);
                float b = Dist(st.Ad);
                if (a < d) d = a;
                if (b < d) d = b;
                if (d < bestD)
                {
                    bestD = d;
                    best = st;
                }
            }
            return best;
        }

        private static float Dist(Transform t)
        {
            if (t == null)
                return 9999f;
            Vector3 from = InfantryModeService.PawnRoot != null
                ? InfantryModeService.PawnRoot.position
                : InfantryModeService.EyePos;
            return Vector3.Distance(from, t.position);
        }

        private static Material PadMat()
        {
            if (_matPad == null)
                _matPad = InfantryFx.Tint(new Color(0.18f, 0.38f, 0.22f, 0.95f));
            return _matPad;
        }

        private static Material GearMat()
        {
            if (_matGear == null)
                _matGear = InfantryFx.Tint(new Color(0.72f, 0.58f, 0.16f));
            return _matGear;
        }

        private static Material AdMat()
        {
            if (_matAd == null)
                _matAd = InfantryFx.Tint(new Color(0.22f, 0.26f, 0.18f));
            return _matAd;
        }

        private static void EnsureStyles()
        {
            if (_styles)
                return;
            _styles = true;
            _px = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _px.SetPixel(0, 0, Color.white);
            _px.Apply();
            _title = new GUIStyle(GUI.skin.label);
            _title.fontSize = 16;
            _title.fontStyle = FontStyle.Bold;
            _title.normal.textColor = Color.white;
            _body = new GUIStyle(GUI.skin.label);
            _body.fontSize = 13;
            _body.normal.textColor = new Color(0.85f, 0.88f, 0.9f);
            _tiny = new GUIStyle(GUI.skin.label);
            _tiny.fontSize = 13;
            _tiny.alignment = TextAnchor.MiddleCenter;
            _tiny.normal.textColor = new Color(0.95f, 0.95f, 0.7f);
            _btn = new GUIStyle(GUI.skin.button);
            _btn.fontSize = 13;
            _btnOn = new GUIStyle(_btn);
            _btnOn.fontStyle = FontStyle.Bold;
            _btnOn.normal.textColor = new Color(0.4f, 1f, 0.45f);
        }
    }
}
