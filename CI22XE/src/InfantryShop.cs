using System.Collections.Generic;
using NuclearOption.Networking;
using NuclearOption.SavedMission;
using UnityEngine;

namespace Oritasy
{
    /// <summary>Buy a jet from the nearest friendly hangar, mark it, walk up, U to board.</summary>
    internal static class InfantryShop
    {
        internal const KeyCode MenuKey = KeyCode.B;
        internal const float BoardRange = 12f;

        internal static bool MenuOpen;

        private static readonly List<AircraftDefinition> Catalog = new List<AircraftDefinition>(64);
        private static Vector2 _scroll;
        private static string _status = "";
        private static float _statusUntil;
        private static Hangar _pendingHangar;
        private static AircraftDefinition _pendingDef;
        private static float _pendingUntil;
        private static int _seenCount;
        private static float _fundsBefore;
        private static int _sel;
        private static bool _menuCursorOn;
        private static GUIStyle _title;
        private static GUIStyle _body;
        private static GUIStyle _btn;
        private static GUIStyle _btnOn;
        private static GUIStyle _tiny;
        private static Texture2D _px;
        private static bool _styles;

        internal static void Tick()
        {
            if (!InfantryModeService.Active)
            {
                Close(false);
                _pendingHangar = null;
                return;
            }
            if (Input.GetKeyDown(MenuKey))
            {
                MenuOpen = !MenuOpen;
                if (MenuOpen)
                {
                    RebuildCatalog();
                    _sel = 0;
                }
                ApplyCursor();
            }
            if (MenuOpen && Input.GetKeyDown(KeyCode.Escape))
                Close(true);
            if (MenuOpen)
            {
                HoldCursor();
                PollMenuKeys();
            }
            else if (_menuCursorOn)
                ReleaseCursor();
            TickPending();
        }

        internal static void Close(bool relock)
        {
            MenuOpen = false;
            ReleaseCursor();
            if (relock && InfantryModeService.Active)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        internal static void Draw()
        {
            if (!InfantryModeService.Active)
                return;
            EnsureStyles();
            DrawMarker();
            if (!MenuOpen)
                return;
            float w = Mathf.Min(520f, Screen.width - 40f);
            float h = Mathf.Min(560f, Screen.height - 80f);
            Rect box = new Rect(24f, 40f, w, h);
            Color prev = GUI.color;
            GUI.color = new Color(0.05f, 0.07f, 0.08f, 0.94f);
            GUI.DrawTexture(box, _px);
            GUI.color = Color.white;
            GUILayout.BeginArea(new Rect(box.x + 10f, box.y + 8f, box.width - 20f, box.height - 16f));
            float funds = ReadFunds();
            Hangar hangar = InfantryHangarMenu.NearestFriendlyHangar(InfantryModeService.EyePos);
            string hangarName = hangar != null ? hangar.name : "?";
            GUILayout.Label(UiLang.T("BUY AIRCRAFT", "购买飞机"), _title);
            GUILayout.Label(UiLang.T(
                "Funds " + funds.ToString("0.0") + "M    Hangar: " + hangarName,
                "资金 " + funds.ToString("0.0") + "M    机库：" + hangarName), _body);
            GUILayout.Label(UiLang.T(
                "W/S or arrows move  Enter buy  B/Esc close. No mouse needed.",
                "W/S 或方向键选机  回车购买  B/Esc关闭。不用鼠标。"), _tiny);
            if (!string.IsNullOrEmpty(_status) && Time.unscaledTime < _statusUntil)
                GUILayout.Label(_status, _body);
            _scroll = GUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < Catalog.Count; i++)
            {
                AircraftDefinition d = Catalog[i];
                if (d == null)
                    continue;
                string name = !string.IsNullOrEmpty(d.unitName) ? d.unitName : d.jsonKey;
                float cost = d.value;
                string mark = i == _sel ? "> " : "  ";
                string line = mark + name + "   " + cost.ToString("0.0") + "M";
                GUIStyle st = i == _sel ? _btnOn : _btn;
                if (GUILayout.Button(line, st, GUILayout.Height(28f)))
                {
                    _sel = i;
                    TryBuy(d, hangar);
                }
            }
            GUILayout.EndScrollView();
            if (GUILayout.Button(UiLang.T("Close (B)", "关闭 (B)"), GUILayout.Height(26f)))
                Close(true);
            GUILayout.EndArea();
            GUI.color = prev;
        }

        private static void TryBuy(AircraftDefinition def, Hangar hangar)
        {
            if (def == null)
            {
                Status(UiLang.T("No airframe.", "没有机型。"));
                return;
            }
            if (hangar == null)
            {
                Status(UiLang.T("No friendly hangar nearby.", "附近没有己方机库。"));
                return;
            }
            Player player;
            if (!GameManager.GetLocalPlayer(out player) || player == null)
            {
                Status(UiLang.T("No local player.", "没有本地玩家。"));
                return;
            }
            float cost = def.value;
            if (cost < 0f)
                cost = 0f;
            float funds = 0f;
            try { funds = player.Allocation; }
            catch { }
            if (funds + 0.001f < cost)
            {
                Status(UiLang.T("Not enough funds (" + funds.ToString("0.0") + "M).",
                    "资金不足（现有 " + funds.ToString("0.0") + "M）。"));
                return;
            }

            bool can = true;
            try { can = hangar.CanSpawnAircraft(def); }
            catch { can = true; }
            if (!can)
            {
                Status(UiLang.T("This hangar cannot spawn that airframe.",
                    "这个机库不能出这种飞机。"));
                return;
            }

            _seenCount = CountAircraft();
            _fundsBefore = funds;
            _pendingDef = def;
            bool allowed = false;
            try
            {
                Airbase.TrySpawnResult r = hangar.TrySpawnAircraft(
                    null, def, default(LiveryKey), new NuclearOption.SavedMission.Loadout(), 1f);
                allowed = r.Allowed;
            }
            catch (System.Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("Infantry buy empty spawn: " + ex.Message);
            }

            if (!allowed)
                allowed = TrySpawnerFallback(player, def, hangar);

            if (!allowed)
            {
                try
                {
                    Airbase.TrySpawnResult r2 = hangar.TrySpawnAircraft(
                        player, def, default(LiveryKey), new NuclearOption.SavedMission.Loadout(), 1f);
                    allowed = r2.Allowed;
                    if (allowed)
                        InfantryModeService.UnseatIfInfantry(player);
                }
                catch (System.Exception ex)
                {
                    if (Plugin.Log != null)
                        Plugin.Log.LogWarning("Infantry buy hangar spawn: " + ex.Message);
                }
            }

            if (!allowed)
            {
                _pendingHangar = hangar;
                _pendingDef = def;
                _pendingUntil = Time.time + 4f;
                Status(UiLang.T("Spawn requested… walk to the hangar.",
                    "已请求出机…去机库等着。"));
                Close(true);
                return;
            }

            Aircraft ac = FindNewNear(hangar, def);
            if (ac == null)
            {
                _pendingHangar = hangar;
                _pendingDef = def;
                _pendingUntil = Time.time + 4f;
                Status(UiLang.T("Jet is coming out. Wait a moment.",
                    "飞机正在出库，稍等。"));
            }
            else
                FinishClaim(player, ac);
            Close(true);
        }

        private static void TickPending()
        {
            if (_pendingHangar == null || Time.time > _pendingUntil)
            {
                if (_pendingHangar != null && Time.time > _pendingUntil)
                    Status(UiLang.T("Spawn timed out. Try another hangar.",
                        "出机超时，换个机库再买。"));
                _pendingHangar = null;
                _pendingDef = null;
                return;
            }
            Aircraft ac = FindNewNear(_pendingHangar, _pendingDef);
            if (ac == null)
                return;
            Player player;
            if (!GameManager.GetLocalPlayer(out player) || player == null)
                return;
            FinishClaim(player, ac);
            _pendingHangar = null;
            _pendingDef = null;
        }

        private static void FinishClaim(Player player, Aircraft ac)
        {
            if (ac == null)
                return;
            try
            {
                float after = player.Allocation;
                float cost = _pendingDef != null ? _pendingDef.value : 0f;
                if (after + 0.05f >= _fundsBefore - 0.05f && cost > 0.001f)
                    player.AddAllocation(-cost);
            }
            catch { }
            InfantryModeService.ClaimAircraft(ac);
            string n = AircraftLabel(ac);
            Status(UiLang.T("YOURS: " + n + "  — run to cyan mark, U to board",
                "已标记：" + n + "  — 跑到青色标记按 U 登机"));
            InfantryHud.FlashHit(UiLang.T("AIRCRAFT MARKED", "飞机已标记"));
        }

        private static bool TrySpawnerFallback(Player player, AircraftDefinition def, Hangar hangar)
        {
            if (def == null || def.unitPrefab == null || hangar == null)
                return false;
            Spawner sp = null;
            try { sp = UnityEngine.Object.FindObjectOfType<Spawner>(); }
            catch { }
            if (sp == null)
                return false;
            Transform t = null;
            try { t = hangar.GetSpawnTransform(); }
            catch { }
            if (t == null)
                t = hangar.transform;
            FactionHQ hq = null;
            try { hq = player.HQ; }
            catch { }
            if (hq == null)
            {
                try { GameManager.GetLocalHQ(out hq); }
                catch { }
            }
            Aircraft ac = null;
            try
            {
                ac = sp.SpawnAircraft(null, def.unitPrefab, new NuclearOption.SavedMission.Loadout(), 1f,
                    default(LiveryKey), t.position.ToGlobalPosition(), t.rotation,
                    Vector3.zero, hangar, hq, "OritasyInfBuy", 0.4f, 0.4f);
            }
            catch (System.Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("Infantry buy Spawner: " + ex.Message);
                return false;
            }
            if (ac == null)
                return false;
            InfantryModeService.ClaimAircraft(ac);
            return true;
        }

        private static Aircraft FindNewNear(Hangar hangar, AircraftDefinition def)
        {
            Aircraft local;
            if (GameManager.GetLocalAircraft(out local) && local != null && !local.disabled)
            {
                if (NearHangar(local, hangar))
                    return local;
            }
            List<Aircraft> all = null;
            try { all = UnitRegistry.allAircraft; }
            catch { }
            if (all == null)
                return null;
            Vector3 origin = hangar != null ? hangar.transform.position : InfantryModeService.EyePos;
            Aircraft best = null;
            float bestD = 80f * 80f;
            for (int i = 0; i < all.Count; i++)
            {
                Aircraft ac = all[i];
                if (ac == null || ac.disabled)
                    continue;
                if (def != null)
                {
                    try
                    {
                        if (ac.definition != null && !object.ReferenceEquals(ac.definition, def)
                            && ac.definition.jsonKey != def.jsonKey)
                            continue;
                    }
                    catch { }
                }
                float d = (ac.transform.position - origin).sqrMagnitude;
                if (d < bestD)
                {
                    bestD = d;
                    best = ac;
                }
            }
            return best;
        }

        private static bool NearHangar(Aircraft ac, Hangar hangar)
        {
            if (ac == null || hangar == null)
                return false;
            return (ac.transform.position - hangar.transform.position).sqrMagnitude < 80f * 80f;
        }

        private static int CountAircraft()
        {
            try
            {
                List<Aircraft> all = UnitRegistry.allAircraft;
                return all != null ? all.Count : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static void RebuildCatalog()
        {
            Catalog.Clear();
            Hangar hangar = InfantryHangarMenu.NearestFriendlyHangar(InfantryModeService.EyePos);
            Airbase ab = null;
            if (hangar != null)
            {
                try { ab = hangar.GetComponentInParent<Airbase>(); }
                catch { }
                AircraftDefinition[] arr = null;
                try { arr = hangar.GetAvailableAircraft(); }
                catch { }
                AddMany(arr);
            }
            if (ab != null)
            {
                List<AircraftDefinition> list = null;
                try { list = ab.GetAvailableAircraft(); }
                catch { }
                if (list != null)
                {
                    for (int i = 0; i < list.Count; i++)
                        AddOne(list[i]);
                }
            }
            if (Catalog.Count == 0)
            {
                try
                {
                    Encyclopedia enc = Encyclopedia.i;
                    if (enc != null && enc.aircraft != null)
                    {
                        for (int i = 0; i < enc.aircraft.Count; i++)
                            AddOne(enc.aircraft[i]);
                    }
                }
                catch { }
            }
        }

        private static void AddMany(AircraftDefinition[] arr)
        {
            if (arr == null)
                return;
            for (int i = 0; i < arr.Length; i++)
                AddOne(arr[i]);
        }

        private static void AddOne(AircraftDefinition d)
        {
            if (d == null)
                return;
            try
            {
                if (!d.IsAllowed(false))
                    return;
            }
            catch { }
            for (int i = 0; i < Catalog.Count; i++)
            {
                if (object.ReferenceEquals(Catalog[i], d))
                    return;
            }
            Catalog.Add(d);
        }

        private static void DrawMarker()
        {
            Aircraft ac = InfantryModeService.ClaimedAircraft;
            if (ac == null || ac.disabled)
                return;
            Camera cam = Camera.main;
            if (cam == null)
                return;
            Vector3 pos = ac.transform.position + Vector3.up * 3.2f;
            Vector3 sp = cam.WorldToScreenPoint(pos);
            if (sp.z < 0.4f)
                return;
            float y = Screen.height - sp.y;
            float dist = Vector3.Distance(InfantryModeService.EyePos, ac.transform.position);
            string n = AircraftLabel(ac);
            string line = n + "  " + Mathf.RoundToInt(dist) + "m";
            if (dist <= InfantryModeService.BoardReach())
                line = line + UiLang.T("  U BOARD", "  U 登机");
            Color prev = GUI.color;
            GUI.color = new Color(0.2f, 0.95f, 1f, 0.95f);
            GUI.DrawTexture(new Rect(sp.x - 7f, y - 7f, 14f, 14f), _px);
            GUI.color = Color.white;
            GUI.Label(new Rect(sp.x - 90f, y - 28f, 180f, 20f), line, _tiny);
            GUI.color = prev;
        }

        private static string AircraftLabel(Aircraft ac)
        {
            if (ac == null)
                return "?";
            try
            {
                if (ac.definition != null && !string.IsNullOrEmpty(ac.definition.unitName))
                    return ac.definition.unitName;
            }
            catch { }
            return ac.name;
        }

        private static float ReadFunds()
        {
            try
            {
                Player p;
                if (GameManager.GetLocalPlayer(out p) && p != null)
                    return p.Allocation;
            }
            catch { }
            return 0f;
        }

        private static void Status(string s)
        {
            _status = s;
            _statusUntil = Time.unscaledTime + 4f;
        }

        private static void PollMenuKeys()
        {
            int n = Catalog.Count;
            if (n <= 0)
                return;
            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
                _sel++;
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))
                _sel--;
            if (_sel < 0)
                _sel = n - 1;
            if (_sel >= n)
                _sel = 0;
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)
                || Input.GetKeyDown(KeyCode.Space))
            {
                Hangar hangar = InfantryHangarMenu.NearestFriendlyHangar(InfantryModeService.EyePos);
                if (_sel >= 0 && _sel < Catalog.Count)
                    TryBuy(Catalog[_sel], hangar);
            }
        }

        private static void HoldCursor()
        {
            if (_menuCursorOn)
            {
                OritasyCursor.Pulse();
                return;
            }
            _menuCursorOn = true;
            OritasyCursor.Hold();
        }

        private static void ReleaseCursor()
        {
            if (!_menuCursorOn)
                return;
            _menuCursorOn = false;
            OritasyCursor.Release();
            if (InfantryModeService.Active)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private static void ApplyCursor()
        {
            if (MenuOpen)
                HoldCursor();
            else
                ReleaseCursor();
        }

        private static void EnsureStyles()
        {
            if (_styles)
                return;
            _styles = true;
            _title = new GUIStyle(GUI.skin.label);
            _title.fontSize = 16;
            _title.fontStyle = FontStyle.Bold;
            _title.normal.textColor = new Color(0.45f, 1f, 0.9f);
            _body = new GUIStyle(GUI.skin.label);
            _body.fontSize = 12;
            _body.wordWrap = true;
            _body.normal.textColor = Color.white;
            _tiny = new GUIStyle(GUI.skin.label);
            _tiny.fontSize = 11;
            _tiny.alignment = TextAnchor.MiddleCenter;
            _tiny.normal.textColor = new Color(0.7f, 0.95f, 1f);
            _btn = new GUIStyle(GUI.skin.button);
            _btn.fontSize = 12;
            _btn.alignment = TextAnchor.MiddleLeft;
            _btnOn = new GUIStyle(_btn);
            _btnOn.fontStyle = FontStyle.Bold;
            _btnOn.normal.textColor = new Color(0.25f, 1f, 0.85f);
            _px = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _px.SetPixel(0, 0, Color.white);
            _px.Apply();
        }
    }
}
