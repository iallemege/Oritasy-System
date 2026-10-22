using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Hangar overlay: pick the aircraft carry-on, and which guns infantry deploy takes.
    /// Drawn while aircraft-select is open (not the faction join screen).
    /// </summary>
    internal static class InfantryHangarMenu
    {
        private static readonly FieldInfo AirbaseField =
            AccessTools.Field(typeof(AircraftSelectionMenu), "airbase");
        private static readonly MethodInfo HideSelection =
            AccessTools.Method(typeof(AircraftSelectionMenu), "HideSelection");
        private static readonly FieldInfo GameplaySelMenu =
            AccessTools.Field(typeof(GameplayUI), "aircraftSelectionMenu");

        private static Rect _win = new Rect(18f, 120f, 420f, 480f);
        private static GUIStyle _title;
        private static GUIStyle _body;
        private static GUIStyle _btn;
        private static GUIStyle _btnOn;
        private static bool _styles;
        private static bool _docked;

        internal static void Draw()
        {
            if (OritasyPresentation.MainMenuActive || OritasyPresentation.SplashActive)
                return;
            if (!InfantryModeService.IsEnabled())
                return;
            if (InfantryModeService.Active)
                return;
            EnsureStyles();
            Event e = Event.current;
            if (e != null && e.type == EventType.KeyDown)
            {
                int idx = -1;
                if (e.keyCode >= KeyCode.Alpha1 && e.keyCode <= KeyCode.Alpha9)
                    idx = (int)e.keyCode - (int)KeyCode.Alpha1;
                else if (e.keyCode >= KeyCode.Keypad1 && e.keyCode <= KeyCode.Keypad9)
                    idx = (int)e.keyCode - (int)KeyCode.Keypad1;
                if (idx >= 0 && idx < InfantryCombat.GunCount)
                {
                    int km = InfantryCombat.LoadKitMask();
                    km = InfantryCombat.ToggleKitGun(km, (InfantryGun)idx);
                    InfantryCombat.SaveKitMask(km);
                    e.Use();
                }
            }
            if (!_docked)
            {
                _docked = true;
                float w = Screen.width * 0.40f;
                if (w < 560f)
                    w = 560f;
                if (w > 700f)
                    w = 700f;
                float h = Screen.height - 48f;
                if (h > 820f)
                    h = 820f;
                if (h < 480f)
                    h = 480f;
                _win = new Rect(12f, 24f, w, h);
            }
            if (_win.width < 520f)
                _win.width = 520f;
            if (_win.height < 420f)
                _win.height = 420f;
            _win = GUI.Window(0x4F524946, _win, DrawWindow,
                UiLang.T("Infantry", "步兵"));
            if (InfantryHud.CatalogOpen)
                ArmorCatalogService.Draw();
        }

        private static void DrawWindow(int id)
        {
            GUILayout.Label(UiLang.T(
                "Pick up to 4 guns for infantry deploy. Aircraft / hop-out / chute carry only the one Carry-on. Keys 1-9 toggle a gun.",
                "部署最多带 4 把。飞机上、下车、跳伞只带随身那一把。1-9 开关枪。"), _body);
            int mask = InfantryCombat.LoadKitMask();
            InfantryGun carry = InfantryCombat.LoadCarryGun();
            int nKit = InfantryCombat.CountKit(mask);
            GUILayout.Label(UiLang.T("Deploy kit (" + nKit + "/" + InfantryCombat.MaxKit + "): ",
                "部署套装（" + nKit + "/" + InfantryCombat.MaxKit + "）：")
                + InfantryCombat.KitSummary(mask), _title);
            GUILayout.Label(UiLang.T("Carry-on: ", "随身：") + InfantryCombat.GunName(carry)
                + "  PEN " + InfantryCombat.PenGrade(carry), _body);
            InfantryWeaponIcons.Ensure();
            for (int i = 0; i < InfantryCombat.GunCount; i++)
            {
                InfantryGun g = (InfantryGun)i;
                bool inKit = InfantryCombat.KitHas(mask, g);
                bool isCarry = g == carry;
                GUIStyle st = inKit ? _btnOn : _btn;
                string label = (i + 1).ToString() + "  " + InfantryCombat.GunName(g)
                    + "  PEN " + InfantryCombat.PenGrade(g)
                    + (inKit ? UiLang.T("  [TAKE]", "  [带]") : "")
                    + (isCarry ? UiLang.T("  [CARRY]", "  [随身]") : "");
                Rect row = GUILayoutUtility.GetRect(1f, 58f, GUILayout.ExpandWidth(true));
                Texture2D ic = InfantryWeaponIcons.Get(g);
                if (ic != null)
                    GUI.DrawTexture(new Rect(row.x, row.y + 4f, 88f, 50f), ic, ScaleMode.ScaleToFit);
                if (GUI.Button(new Rect(row.x + 92f, row.y, row.width - 188f, 56f), label, st))
                {
                    mask = InfantryCombat.ToggleKitGun(mask, g);
                    InfantryCombat.SaveKitMask(mask);
                }
                if (GUI.Button(new Rect(row.x + row.width - 92f, row.y + 10f, 88f, 36f),
                    UiLang.T("Carry", "随身")))
                    InfantryCombat.SaveCarryGun(g);
            }
            GUILayout.Space(8f);
            if (GUILayout.Button(UiLang.T("DEPLOY INFANTRY  (" + nKit + " guns)",
                "部署步兵（" + nKit + " 把）"), GUILayout.Height(44f)))
            {
                InfantryModeService.QueueHangarDeploy();
            }
            if (GUILayout.Button(UiLang.T("Armor table", "装甲表"), GUILayout.Height(32f)))
            {
                ArmorCatalogService.EnsureScanned();
                InfantryHud.CatalogOpen = true;
            }
            GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
        }

        internal static Hangar NearestFriendlyHangar(Vector3 from)
        {
            Hangar[] all = UnityEngine.Object.FindObjectsOfType<Hangar>();
            if (all == null || all.Length == 0)
                return null;
            FactionHQ hq = null;
            try { GameManager.GetLocalHQ(out hq); }
            catch { hq = null; }
            Hangar best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < all.Length; i++)
            {
                Hangar h = all[i];
                if (h == null)
                    continue;
                try
                {
                    if (h.Disabled)
                        continue;
                }
                catch { }
                if (hq != null)
                {
                    Airbase hab = null;
                    try { hab = h.GetComponentInParent<Airbase>(); }
                    catch { hab = null; }
                    if (hab != null && hab.CurrentHQ != null
                        && !object.ReferenceEquals(hab.CurrentHQ, hq))
                        continue;
                }
                float d = (h.transform.position - from).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = h;
                }
            }
            return best;
        }

        internal static bool TryHangarSpawn(out Vector3 pos, out float yaw)
        {
            pos = Vector3.zero;
            yaw = 0f;
            Hangar hangar = FindHangar();
            if (hangar == null)
                return false;
            if (InfantryAirbaseStations.TryPad(hangar, out pos, out yaw))
                return true;
            Transform t = null;
            try { t = hangar.GetSpawnTransform(); }
            catch { t = null; }
            if (t == null)
            {
                try { t = hangar.transform; }
                catch { t = null; }
            }
            if (t == null)
                return false;
            pos = t.position;
            yaw = t.eulerAngles.y;
            Vector3 start = pos + Vector3.up * 12f;
            RaycastHit hit;
            if (Physics.Raycast(start, Vector3.down, out hit, 80f, ~0, QueryTriggerInteraction.Ignore))
                pos = hit.point + Vector3.up * 0.08f;
            else
                pos = pos + Vector3.up * 0.2f;
            return true;
        }

        internal static void CloseHangarUi()
        {
            AircraftSelectionMenu menu = UnityEngine.Object.FindObjectOfType<AircraftSelectionMenu>();
            if (menu != null)
            {
                try
                {
                    if (HideSelection != null)
                        HideSelection.Invoke(menu, null);
                }
                catch { }
                try { menu.gameObject.SetActive(false); }
                catch { }
            }
            try
            {
                GameplayUI ui = SceneSingleton<GameplayUI>.i;
                if (ui != null && GameplaySelMenu != null)
                {
                    GameObject go = GameplaySelMenu.GetValue(ui) as GameObject;
                    if (go != null)
                        go.SetActive(false);
                }
            }
            catch { }
            try { CursorManager.SetFlag(CursorFlags.SelectionMenu, false); }
            catch { }
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private static Hangar FindHangar()
        {
            Airbase ab = CurrentAirbase();
            Hangar best = PickFromAirbase(ab);
            if (best != null)
                return best;
            Hangar[] all = UnityEngine.Object.FindObjectsOfType<Hangar>();
            if (all == null || all.Length == 0)
                return null;
            FactionHQ hq = null;
            try { GameManager.GetLocalHQ(out hq); }
            catch { hq = null; }
            Camera cam = Camera.main;
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
            float bestD = float.MaxValue;
            for (int i = 0; i < all.Length; i++)
            {
                Hangar h = all[i];
                if (h == null)
                    continue;
                try
                {
                    if (h.Disabled)
                        continue;
                }
                catch { }
                if (hq != null)
                {
                    Airbase hab = null;
                    try { hab = h.GetComponentInParent<Airbase>(); }
                    catch { hab = null; }
                    if (hab != null && hab.CurrentHQ != null
                        && !object.ReferenceEquals(hab.CurrentHQ, hq))
                        continue;
                }
                float d = (h.transform.position - camPos).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = h;
                }
            }
            return best;
        }

        private static Hangar PickFromAirbase(Airbase ab)
        {
            if (ab == null)
                return null;
            List<Hangar> hangars = null;
            try { hangars = ab.hangars; }
            catch { hangars = null; }
            if (hangars == null || hangars.Count == 0)
                return null;
            Camera cam = Camera.main;
            Vector3 camPos = cam != null ? cam.transform.position : Vector3.zero;
            Hangar best = null;
            float bestD = float.MaxValue;
            for (int i = 0; i < hangars.Count; i++)
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
                float d = (h.transform.position - camPos).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = h;
                }
            }
            return best;
        }

        private static Airbase CurrentAirbase()
        {
            AircraftSelectionMenu menu = UnityEngine.Object.FindObjectOfType<AircraftSelectionMenu>();
            if (menu == null || AirbaseField == null)
                return null;
            try { return AirbaseField.GetValue(menu) as Airbase; }
            catch { return null; }
        }

        private static void EnsureStyles()
        {
            if (_styles)
                return;
            _styles = true;
            _title = new GUIStyle(GUI.skin.label);
            _title.fontSize = 16;
            _title.fontStyle = FontStyle.Bold;
            _title.wordWrap = true;
            _title.normal.textColor = Color.white;
            _body = new GUIStyle(GUI.skin.label);
            _body.fontSize = 14;
            _body.wordWrap = true;
            _body.normal.textColor = new Color(0.85f, 0.88f, 0.9f);
            _btn = new GUIStyle(GUI.skin.button);
            _btn.fontSize = 14;
            _btn.wordWrap = true;
            _btn.alignment = TextAnchor.MiddleLeft;
            _btn.clipping = TextClipping.Overflow;
            _btnOn = new GUIStyle(_btn);
            _btnOn.fontStyle = FontStyle.Bold;
            _btnOn.normal.textColor = new Color(0.4f, 1f, 0.45f);
        }
    }
}
