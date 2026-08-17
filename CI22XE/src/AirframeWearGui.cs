using BepInEx.Configuration;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// In-flight F8 schematic: engine parts as colored blocks on a per-airframe map.
    /// Click a block to queue a 2s repair (no cap drop). Repair All is instant and lowers the cap.
    /// Main-menu F8 stays Career Profile.
    /// </summary>
    internal static class AirframeWearGui
    {
        private static ConfigEntry<bool> Enabled;
        private static bool _open;
        private static bool _cursorHeld;
        private static bool _consumed;
        private static GUIStyle _title;
        private static GUIStyle _body;
        private static GUIStyle _btn;
        private static GUIStyle _small;
        private static GUIStyle _cell;
        private static GUIStyle _chipHint;
        private static GUIStyle _idleMark;
        private static int _hover = -1;

        internal static bool IsOpen
        {
            get { return _open; }
        }

        internal static bool ConsumedF8
        {
            get { return _consumed; }
        }

        internal static bool IsEnabled()
        {
            return Enabled == null || Enabled.Value;
        }

        internal static void Bind(ConfigFile config)
        {
            if (config == null)
                return;
            Enabled = config.Bind("Flight", "F8EngineLayout", true,
                "In-flight F8 Engine Component Monitor + tip chip. Toggle in Career Profile. Main-menu F8 stays Career Profile.");
        }

        internal static void DrawProfileToggle()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(UiLang.T("F8 Engine Component Monitor", "F8 引擎组件监视系统"), GUILayout.Width(220f));
            Color prev = GUI.backgroundColor;
            bool on = IsEnabled();
            GUI.backgroundColor = on ? new Color(0.45f, 0.85f, 0.55f) : Color.white;
            if (GUILayout.Button(on ? UiLang.T("ON", "开") : UiLang.T("OFF", "关"),
                GUILayout.Width(90f), GUILayout.Height(26f)))
            {
                if (Enabled != null)
                    Enabled.Value = !on;
                on = !on;
                if (!on)
                    Close();
            }
            GUI.backgroundColor = prev;
            GUILayout.Label(on ? UiLang.T("  [ON]", "  [开]") : UiLang.T("  [OFF]", "  [关]"),
                GUILayout.Width(56f));
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Label(
                on
                    ? UiLang.T(
                        "ON: in-flight F8 opens the Engine Component Monitor. Main-menu F8 is still Career Profile.",
                        "开：飞行中 F8 打开引擎组件监视系统。主菜单 F8 仍是生涯档案。")
                    : UiLang.T(
                        "OFF: in-flight F8 is unused (chase HUD can use F8). Main-menu F8 is still Career Profile.",
                        "关：飞行中不占用 F8（尾追 HUD 可用 F8）。主菜单 F8 仍是生涯档案。"),
                GUILayout.ExpandWidth(true));
        }

        internal static void CloseFromOutside()
        {
            Close();
        }

        internal static void Tick()
        {
            _consumed = false;
            if (OritasyPresentation.SplashActive)
            {
                Close();
                return;
            }
            if (JoinMenuFactionFix.SelectionUiOpen())
            {
                Close();
                return;
            }

            Aircraft ac = LocalAircraft();
            if (ac == null)
            {
                Close();
                return;
            }
            if (!IsEnabled())
            {
                Close();
                return;
            }

            if (Input.GetKeyDown(KeyCode.F8))
            {
                _consumed = true;
                _open = !_open;
                if (!_open)
                    Close();
                else
                    CloseOtherMenus();
            }
            if (_open && Input.GetKeyDown(KeyCode.Escape))
            {
                Close();
                _consumed = true;
            }
            if (_open)
                HoldCursor();
            else
                ReleaseCursor();
        }

        internal static void Draw()
        {
            if (IsEnabled() && Plugin.AllowThirdPersonUi)
                DrawCornerHint();
            if (!_open)
                return;
            Aircraft ac = LocalAircraft();
            if (ac == null)
            {
                Close();
                return;
            }

            EnsureStyles();
            HoldCursor();

            bool zh = UiLang.IsChinese;
            float w = Mathf.Min(720f, UiScaleService.Width * 0.92f);
            float h = Mathf.Min(560f, UiScaleService.Height * 0.86f);
            Rect box = new Rect((UiScaleService.Width - w) * 0.5f, (UiScaleService.Height - h) * 0.07f, w, h);
            GUI.color = new Color(0.05f, 0.07f, 0.09f, 0.94f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = new Color(0.95f, 0.55f, 0.2f, 0.95f);
            GUI.DrawTexture(new Rect(box.x, box.y, box.width, 3f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(box.x + 14f, box.y + 8f, box.width - 28f, 22f),
                zh ? "引擎组件监视系统  ·  F8" : "ENGINE COMPONENT MONITOR  ·  F8",
                _title);

            float g = Mathf.Abs(AircraftGLoadService.ReadSignedG(ac));
            string acName = "?";
            try { acName = AircraftIdentity.GetKey(ac); }
            catch { }
            string flicker = AirframeWearService.Flickering()
                ? (zh ? "  ·  推力不稳" : "  ·  THRUST FLICKER")
                : "";
            string abHold = AirframeWearService.HasAfterburner()
                ? ((zh ? "  |  加力 " : "  |  AB ")
                    + AirframeWearService.AbHoldSeconds().ToString("0.0") + "s")
                : "";
            string abTemp = AirframeWearService.HasAfterburner()
                ? ((zh ? "  |  加力室 " : "  |  AB ")
                    + AirframeWearService.AbTempC().ToString("0") + "°C")
                : "";
            GUI.Label(new Rect(box.x + 14f, box.y + 30f, box.width - 28f, 50f),
                acName + "  ·  " + AirframeWearService.FamilyLabel()
                + "  ·  " + EngineBankHeader(zh)
                + flicker
                + "\nG " + g.ToString("0.0")
                + "  |  >"
                + AirframeWearService.PartGGate().ToString("0")
                + (zh ? "G 持续 " : "G ")
                + AirframeWearService.GHoldSeconds().ToString("0.0") + "s"
                + abHold
                + "  |  " + (zh ? "飞行员 " : "Pilot ")
                + Mathf.RoundToInt(AirframeWearService.PilotHealth01() * 100f) + "%"
                + "  |  " + (zh ? "材料 " : "Mat ")
                + AirframeWearService.MaterialLabel()
                + "\n"
                + (zh ? "标准 " : "Rated ")
                + AirframeWearService.RatedTempC().ToString("0") + "°C @"
                + Mathf.RoundToInt(AirframeWearService.RatedThrottle01() * 100f) + "%"
                + (zh ? "  |  排气 " : "  |  EGT ")
                + AirframeWearService.CoreTempC().ToString("0") + "°C"
                + (zh ? "  |  滑油 " : "  |  Oil ")
                + AirframeWearService.OilTempC().ToString("0") + "°C"
                + abTemp,
                _small);

            Rect map = new Rect(box.x + 12f, box.y + 86f, box.width - 24f, box.height - 142f);
            GUI.color = new Color(0.08f, 0.10f, 0.12f, 0.96f);
            GUI.DrawTexture(map, Texture2D.whiteTexture);
            GUI.color = Color.white;
            AirframeWearService.Family family = AirframeWearService.CurrentFamily();
            int banks = AirframeWearService.EngineCount();
            AirframeWearSchematic.DrawSilhouette(map, family, banks);
            if (AirframeWearService.HasMixedAltitude())
            {
                for (int b = 0; b < banks; b++)
                {
                    float hx, hy, hw, hh;
                    if (!AirframeWearSchematic.TryBankHeader(b, banks, family, out hx, out hy, out hw, out hh))
                        continue;
                    string alt = AirframeWearService.BankAltLabel(b);
                    if (string.IsNullOrEmpty(alt))
                        continue;
                    Rect cap = new Rect(
                        map.x + hx * map.width,
                        map.y + hy * map.height,
                        hw * map.width,
                        Mathf.Max(14f, hh * map.height));
                    GUI.Label(cap, "E" + (b + 1) + " " + alt, _small);
                }
            }

            _hover = -1;
            int n = AirframeWearService.PartCount();
            Vector2 mouse = Event.current != null ? Event.current.mousePosition : Vector2.zero;
            for (int i = 0; i < n; i++)
            {
                AirframeWearService.Part p = AirframeWearService.GetPart(i);
                float nx, ny, nw, nh;
                if (!AirframeWearSchematic.TryCell(p, family, banks, out nx, out ny, out nw, out nh))
                    continue;
                Rect cell = new Rect(
                    map.x + nx * map.width,
                    map.y + ny * map.height,
                    nw * map.width,
                    nh * map.height);
                if (cell.width < 8f)
                    cell.width = 8f;
                if (cell.height < 8f)
                    cell.height = 8f;
                if (cell.Contains(mouse))
                    _hover = i;

                bool idle = !AirframeWearService.PartIsRunning(p);
                Color col;
                string extra = "";
                string label;
                if (idle)
                {
                    col = new Color(0.40f, 0.42f, 0.45f, 1f);
                    label = "";
                }
                else
                {
                    col = AirframeWearSchematic.HealthColor(p.Health);
                    if (p.Id != null && p.Id.EndsWith(".oil") && AirframeWearService.OilTempRatio() > 1.05f)
                        col = Color.Lerp(col, new Color(1f, 0.45f, 0.08f), 0.35f);
                    if (p.Id != null && p.Id.EndsWith(".ab") && AirframeWearService.HasAfterburner()
                        && AirframeWearService.AbTempC() > AirframeWearService.RatedTempC() * 1.35f)
                        col = Color.Lerp(col, new Color(1f, 0.25f, 0.08f), 0.28f);
                    if (_hover == i)
                        col = Color.Lerp(col, Color.white, 0.18f);
                    if (AirframeWearService.IsRepairing(i))
                        col = Color.Lerp(col, new Color(0.35f, 0.85f, 1f), 0.35f);
                    else if (AirframeWearService.IsQueued(i))
                        col = Color.Lerp(col, new Color(0.95f, 0.85f, 0.35f), 0.28f);
                    if (AirframeWearService.IsRepairing(i))
                        extra = "\n" + Mathf.RoundToInt(AirframeWearService.RepairProgress01() * 100f) + "%";
                    else if (AirframeWearService.IsQueued(i))
                        extra = zh ? "\n排队" : "\nQ";
                    label = (cell.width < 58f || cell.height < 30f)
                        ? (AirframeWearService.HealthPct(p) + "%" + extra)
                        : (ShortName(p, zh) + "\n" + AirframeWearService.HealthPct(p) + "%" + extra);
                }
                GUI.color = new Color(0.04f, 0.05f, 0.06f, 0.85f);
                GUI.DrawTexture(new Rect(cell.x - 1f, cell.y - 1f, cell.width + 2f, cell.height + 2f),
                    Texture2D.whiteTexture);
                GUI.color = col;
                if (GUI.Button(cell, label, _cell))
                    AirframeWearService.QueueRepair(i);
                GUI.color = Color.white;
            }

            if (_idleMark == null)
            {
                _idleMark = new GUIStyle(GUI.skin.label);
                _idleMark.fontSize = 14;
                _idleMark.fontStyle = FontStyle.Bold;
                _idleMark.alignment = TextAnchor.MiddleCenter;
                _idleMark.wordWrap = false;
                _idleMark.normal.textColor = new Color(0.82f, 0.84f, 0.86f, 0.98f);
            }
            for (int b = 0; b < banks; b++)
            {
                if (AirframeWearService.BankRunning(b))
                    continue;
                float bx, by, bw, bh;
                if (!AirframeWearSchematic.TryBankBox(b, banks, family, out bx, out by, out bw, out bh))
                    continue;
                Rect idleBox = new Rect(
                    map.x + bx * map.width,
                    map.y + by * map.height,
                    bw * map.width,
                    bh * map.height);
                GUI.Label(idleBox, zh ? "未运转" : "Not running", _idleMark);
            }
            for (int i = 0; i < n; i++)
            {
                AirframeWearService.Part ap = AirframeWearService.GetPart(i);
                if (!AirframeWearService.IsApuPart(ap))
                    continue;
                if (AirframeWearService.PartIsRunning(ap))
                    break;
                float ax, ay, aw, ah;
                if (!AirframeWearSchematic.TryCell(ap, family, banks, out ax, out ay, out aw, out ah))
                    break;
                Rect apuIdle = new Rect(
                    map.x + ax * map.width,
                    map.y + ay * map.height,
                    aw * map.width,
                    ah * map.height);
                GUI.Label(apuIdle, zh ? "未运转" : "Not running", _idleMark);
                break;
            }

            string hint;
            if (_hover >= 0)
            {
                AirframeWearService.Part hp = AirframeWearService.GetPart(_hover);
                hint = AirframeWearService.PartLabel(hp)
                    + "  " + AirframeWearService.HealthPct(hp) + "%"
                    + (hp.Ceiling < 0.995f
                        ? ("  " + (zh ? "上限 " : "cap ") + AirframeWearService.CeilingPct(hp) + "%")
                        : "");
                if (!AirframeWearService.PartIsRunning(hp))
                    hint += zh ? "  ·  未运转" : "  ·  not running";
                string thint = AirframeWearService.PartTempHint(hp);
                if (!string.IsNullOrEmpty(thint))
                    hint += "  ·  " + thint;
                hint += zh ? "  ·  点击排队维修（2秒，不降上限）" : "  ·  click to queue 2s repair (no cap drop)";
            }
            else
            {
                int qn = AirframeWearService.RepairQueueCount();
                hint = zh
                    ? "绿=健康  红=损伤  ·  点击方块排队维修（每件2秒，不降上限）  ·  全部维修会降上限"
                    : "Green=healthy  Red=damaged  ·  click to queue 2s repair (no cap drop)  ·  Repair All lowers cap";
                if (qn > 0)
                    hint += zh ? ("  ·  队列 " + qn) : ("  ·  queue " + qn);
            }
            GUI.Label(new Rect(box.x + 14f, box.yMax - 58f, box.width - 28f, 18f), hint, _small);

            if (GUI.Button(new Rect(box.x + 12f, box.yMax - 36f, box.width - 24f, 26f),
                zh ? "维修全部组件（秒修，降低上限）" : "REPAIR ALL PARTS (instant, lowers cap)", _btn))
                AirframeWearService.RepairAllEngines(ac);
        }

        private static void DrawCornerHint()
        {
            Aircraft ac = LocalAircraft();
            if (ac == null)
                return;
            try
            {
                if (MissileCameraHud.ManualActive)
                    return;
            }
            catch { }
            if (Event.current != null && Event.current.type != EventType.Repaint)
                return;

            EnsureStyles();
            Rect chip = PlayerAutopilot.CornerChipRect(AssistMenuLayoutService.SlotF8);
            Color prev = GUI.color;
            GUI.color = new Color(0.05f, 0.07f, 0.1f, 0.78f);
            GUI.DrawTexture(chip, Texture2D.whiteTexture);
            GUI.color = _open
                ? new Color(0.95f, 0.55f, 0.25f, 0.95f)
                : new Color(0.55f, 0.85f, 0.45f, 0.9f);
            GUI.DrawTexture(new Rect(chip.x, chip.y, chip.width, 2f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            if (_chipHint == null)
            {
                _chipHint = new GUIStyle(GUI.skin.label);
                _chipHint.fontSize = 11;
                _chipHint.fontStyle = FontStyle.Bold;
                _chipHint.alignment = TextAnchor.MiddleRight;
            }
            _chipHint.normal.textColor = _open
                ? new Color(1f, 0.9f, 0.55f, 0.98f)
                : new Color(0.85f, 1f, 0.8f, 0.95f);
            GUI.Label(new Rect(chip.x + 6f, chip.y, chip.width - 12f, chip.height),
                AssistStatusFormatService.EngineChip(_open), _chipHint);
            GUI.color = prev;
        }

        private static void CloseOtherMenus()
        {
            try { PlayerAutopilot.CloseMenuFromOutside(); }
            catch { }
            try { AerialResupply.CloseMenuFromOutside(); }
            catch { }
            try { BeginnerAssist.CloseMenuFromOutside(); }
            catch { }
            try { IlsSettingsMenu.CloseMenuFromOutside(); }
            catch { }
            try { PrivateMessageMenu.CloseMenuFromOutside(); }
            catch { }
            try { KillChoiceMenu.CloseMenuFromOutside(); }
            catch { }
            try { HostFundMenu.CloseMenuFromOutside(); }
            catch { }
            try { PlayerAutopilot.CloseWeXonSupportMenu(); }
            catch { }
        }

        private static string EngineBankHeader(bool zh)
        {
            int n = AirframeWearService.LoCount();
            if (AirframeWearService.HasMixedAltitude())
            {
                return n
                    + (zh ? " 低空 + " : " LO + ")
                    + n
                    + (zh ? " 高空" : " HI");
            }
            return n + (zh ? " 发" : " eng");
        }

        private static string ShortName(AirframeWearService.Part p, bool zh)
        {
            string n = zh ? p.Zh : p.En;
            if (p.Bank >= 0)
            {
                string pre = p.Prop ? (zh ? "桨" : "P") : (zh ? "发" : "E");
                string alt = AirframeWearService.BankAltShort(p.Bank);
                if (!string.IsNullOrEmpty(alt))
                    return pre + (p.Bank + 1) + alt + " " + n;
                return pre + (p.Bank + 1) + " " + n;
            }
            return n;
        }

        private static void Close()
        {
            _open = false;
            ReleaseCursor();
        }

        private static void HoldCursor()
        {
            if (_cursorHeld)
                OritasyCursor.Pulse();
            else
            {
                OritasyCursor.Hold();
                _cursorHeld = true;
            }
        }

        private static void ReleaseCursor()
        {
            if (!_cursorHeld)
                return;
            OritasyCursor.Release();
            _cursorHeld = false;
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

        private static void EnsureStyles()
        {
            if (_title != null)
                return;
            _title = new GUIStyle(GUI.skin.label);
            _title.fontSize = 16;
            _title.fontStyle = FontStyle.Bold;
            _title.normal.textColor = new Color(1f, 0.82f, 0.45f);
            _body = new GUIStyle(GUI.skin.label);
            _body.fontSize = 11;
            _body.alignment = TextAnchor.MiddleCenter;
            _body.normal.textColor = Color.white;
            _small = new GUIStyle(GUI.skin.label);
            _small.fontSize = 12;
            _small.wordWrap = true;
            _small.normal.textColor = new Color(0.8f, 0.88f, 0.95f);
            _btn = new GUIStyle(GUI.skin.button);
            _btn.fontSize = 12;
            _btn.fontStyle = FontStyle.Bold;
            _cell = new GUIStyle(GUI.skin.button);
            _cell.fontSize = 10;
            _cell.fontStyle = FontStyle.Bold;
            _cell.alignment = TextAnchor.MiddleCenter;
            _cell.wordWrap = true;
            _cell.normal.background = Texture2D.whiteTexture;
            _cell.hover.background = Texture2D.whiteTexture;
            _cell.active.background = Texture2D.whiteTexture;
            _cell.normal.textColor = new Color(0.08f, 0.08f, 0.08f);
            _cell.hover.textColor = new Color(0.08f, 0.08f, 0.08f);
            _cell.active.textColor = new Color(0.08f, 0.08f, 0.08f);
            _cell.padding = new RectOffset(2, 2, 1, 1);
        }
    }
}
