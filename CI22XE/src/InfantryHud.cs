using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Powered-armor visor HUD: compass, suit integrity, helmet reticle, portable radar.
    /// Quote (') opens the armor / weapon grade table (N is vanilla NVG).
    /// </summary>
    internal static class InfantryHud
    {
        internal const KeyCode CatalogKey = KeyCode.Quote;

        internal static bool CatalogOpen;
        internal static bool BlocksLook
        {
            get { return CatalogOpen || InfantryShop.MenuOpen || InfantryAirbaseStations.KitOpen; }
        }

        private static GUIStyle _title;
        private static GUIStyle _body;
        private static GUIStyle _tiny;
        private static bool _styles;
        private static string _hitFlash;
        private static float _hitUntil;
        private static Texture2D _px;
        private static Texture2D _visor;
        private static GUIStyle _hud;
        private static GUIStyle _hudTiny;
        private static GUIStyle _hudTick;
        private static GUIStyle _hudBig;
        private static Texture2D _scopeMask;
        private static bool _hudCursorOn;
        private static float _rngCached;
        private static float _rngAt;

        internal static void Warm()
        {
            EnsureVisor();
            EnsureStyles();
        }

        internal static void Tick()
        {
            if (!InfantryModeService.Active)
            {
                CloseMenus(false);
                return;
            }
            if (Input.GetKeyDown(CatalogKey))
            {
                CatalogOpen = !CatalogOpen;
                if (CatalogOpen)
                    ArmorCatalogService.EnsureScanned();
                ApplyCursor();
            }
            if (Input.GetKeyDown(KeyCode.Escape) && BlocksLook)
            {
                InfantryShop.Close(false);
                InfantryAirbaseStations.CloseKit();
                CloseMenus(true);
            }
            ApplyCursor();
            if (InfantryCombat.Ads < 0.22f && Time.unscaledTime >= _rngAt)
            {
                _rngAt = Time.unscaledTime + 0.12f;
                RaycastHit hit;
                if (Physics.Raycast(InfantryModeService.EyePos, InfantryModeService.EyeFwd,
                    out hit, 2400f, InfantryPerf.SightMask, QueryTriggerInteraction.Ignore))
                    _rngCached = hit.distance;
                else
                    _rngCached = 0f;
            }
        }

        internal static void CloseMenus(bool relock)
        {
            InfantryShop.Close(false);
            InfantryAirbaseStations.CloseKit();
            CatalogOpen = false;
            ReleaseHudCursor();
            if (relock && InfantryModeService.Active)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        internal static void FlashHit(string line)
        {
            _hitFlash = line;
            _hitUntil = Time.unscaledTime + 1.6f;
        }

        internal static void Draw()
        {
            if (!InfantryModeService.Active)
                return;
            Event ev = Event.current;
            if (!BlocksLook && ev != null && ev.type != EventType.Repaint)
                return;
            EnsureStyles();
            if (InfantryModeService.Dead)
            {
                Color prev = GUI.color;
                GUI.color = new Color(0f, 0f, 0f, 0.94f);
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _px);
                GUI.color = Color.white;
                GUI.Label(new Rect(Screen.width * 0.5f - 180f, Screen.height * 0.42f, 360f, 32f),
                    UiLang.T("KIA", "阵亡"), _title);
                GUI.Label(new Rect(Screen.width * 0.5f - 200f, Screen.height * 0.48f, 400f, 24f),
                    UiLang.T("Space / Esc — leave", "空格 / Esc — 离开"), _tiny);
                GUI.color = prev;
                return;
            }
            if (!InfantryModeService.ThirdPerson)
                DrawVisor();
            DrawCompass();
            DrawSuitPanel();
            if (!InfantryModeService.ThirdPerson)
                DrawSysRail();
            DrawWeaponStrip();
            DrawSkillRings();
            if (!InfantryModeService.ThirdPerson)
            {
                DrawHelmetReticle();
                DrawContactPips();
                DrawAds();
            }
            InfantryRadar.Draw();
            InfantryShop.Draw();
            InfantryWarnService.Draw();
            if (!string.IsNullOrEmpty(_hitFlash) && Time.unscaledTime < _hitUntil)
                GUI.Label(new Rect(Screen.width * 0.5f - 220f, Screen.height * 0.22f, 440f, 28f), _hitFlash, _title);
            if (CatalogOpen)
                ArmorCatalogService.Draw();
        }

        private static void DrawVisor()
        {
            EnsureVisor();
            Color prev = GUI.color;
            GUI.color = Color.white;
            if (_visor != null)
                GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _visor, ScaleMode.StretchToFill);
            float brow = 22f;
            float chin = Screen.height * 0.09f;
            float leftCut = 236f;
            float rightCut = AssistMenuLayoutService.ChipW + 188f;
            GUI.color = new Color(0.01f, 0.035f, 0.04f, 0.58f);
            GUI.DrawTexture(new Rect(leftCut, 0f, Screen.width - leftCut - rightCut, brow), _px);
            GUI.DrawTexture(new Rect(0f, Screen.height - chin, Screen.width, chin), _px);
            GUI.DrawTexture(new Rect(0f, brow, 12f, Screen.height - chin - brow), _px);
            GUI.DrawTexture(new Rect(Screen.width - 12f, brow, 12f, Screen.height - chin - brow), _px);
            GUI.color = new Color(0.2f, 0.95f, 0.82f, 0.42f);
            GUI.DrawTexture(new Rect(leftCut, brow - 1f, Screen.width - leftCut - rightCut, 1.5f), _px);
            GUI.DrawTexture(new Rect(0f, Screen.height - chin, Screen.width, 1.5f), _px);
            GUI.color = new Color(0.2f, 0.95f, 0.82f, 0.55f);
            DrawBracket(18f, Screen.height - 18f, 1f, -1f);
            DrawBracket(Screen.width - 18f, Screen.height - 18f, -1f, -1f);
            GUI.color = prev;
        }

        private static void DrawBracket(float x, float y, float sx, float sy)
        {
            float arm = 28f;
            float th = 2f;
            GUI.DrawTexture(new Rect(sx > 0f ? x : x - arm, y - th * 0.5f, arm, th), _px);
            GUI.DrawTexture(new Rect(x - th * 0.5f, sy > 0f ? y : y - arm, th, arm), _px);
        }

        private static void DrawCompass()
        {
            float yaw = InfantryModeService.LookYaw;
            float w = 420f;
            float x = Screen.width * 0.5f - w * 0.5f;
            float y = 10f;
            Color prev = GUI.color;
            GUI.color = new Color(0.02f, 0.08f, 0.09f, 0.62f);
            GUI.DrawTexture(new Rect(x, y, w, 28f), _px);
            GUI.color = new Color(0.25f, 0.95f, 0.82f, 0.9f);
            GUI.DrawTexture(new Rect(Screen.width * 0.5f - 1f, y, 2f, 28f), _px);
            float pxPerDeg = w / 90f;
            for (int d = -50; d <= 50; d += 5)
            {
                float hdg = Mathf.Repeat(yaw + d + 360f, 360f);
                float tx = Screen.width * 0.5f + d * pxPerDeg;
                if (tx < x + 6f || tx > x + w - 6f)
                    continue;
                bool major = (Mathf.RoundToInt(hdg) % 15) == 0;
                float tickH = major ? 10f : 5f;
                GUI.color = new Color(0.3f, 0.95f, 0.85f, major ? 0.9f : 0.4f);
                GUI.DrawTexture(new Rect(tx - 0.5f, y + 28f - tickH, 1.5f, tickH), _px);
                if (!major)
                    continue;
                int ih = Mathf.RoundToInt(hdg);
                if (ih == 360)
                    ih = 0;
                string lab = ih.ToString("000");
                if (ih == 0) lab = "N";
                else if (ih == 90) lab = "E";
                else if (ih == 180) lab = "S";
                else if (ih == 270) lab = "W";
                GUI.Label(new Rect(tx - 14f, y + 2f, 28f, 16f), lab, _hudTick);
            }
            GUI.color = new Color(0.7f, 1f, 0.9f, 0.85f);
            float headX = x + w + 8f;
            float chipLeft = Screen.width - AssistMenuLayoutService.ChipW
                - AssistMenuLayoutService.ChipMarginRight;
            if (headX + 72f < chipLeft - 8f)
            {
                GUI.Label(new Rect(headX, y + 4f, 72f, 20f),
                    Mathf.RoundToInt(Mathf.Repeat(yaw, 360f)).ToString("000") + "°", _hud);
            }
            GUI.color = prev;
        }

        private static void DrawSuitPanel()
        {
            float x = 16f;
            float y = 36f;
            float w = 220f;
            Color prev = GUI.color;
            GUI.color = new Color(0.02f, 0.07f, 0.08f, 0.72f);
            GUI.DrawTexture(new Rect(x, y, w, 132f), _px);
            GUI.color = new Color(0.25f, 0.95f, 0.82f, 0.55f);
            GUI.DrawTexture(new Rect(x, y, w, 2f), _px);
            GUI.DrawTexture(new Rect(x, y, 2f, 132f), _px);
            GUI.color = new Color(0.35f, 1f, 0.88f, 0.95f);
            GUI.Label(new Rect(x + 8f, y + 4f, w - 12f, 20f),
                UiLang.T("EXO-10  POWERED SUIT", "EXO-10  动力甲"), _hud);
            float hp = InfantryModeService.Hp / InfantryModeService.MaxHp;
            if (hp < 0f) hp = 0f;
            if (hp > 1f) hp = 1f;
            DrawSuitBar(x + 8f, y + 24f, w - 16f, hp,
                UiLang.T("INTEGRITY", "完好度") + "  "
                    + Mathf.RoundToInt(InfantryModeService.Hp) + "/"
                    + Mathf.RoundToInt(InfantryModeService.MaxHp),
                hp < 0.35f ? new Color(1f, 0.28f, 0.18f) : new Color(0.3f, 1f, 0.55f));
            float fat = InfantryModeService.ArmorFatigue;
            DrawSuitBar(x + 8f, y + 48f, w - 16f, 1f - fat,
                UiLang.T("PLATE ", "外甲 ") + InfantryModeService.OuterEffective()
                    + "  FAT " + Mathf.RoundToInt(fat * 100f) + "%",
                fat > 0.55f ? new Color(1f, 0.7f, 0.2f) : new Color(0.45f, 0.85f, 1f));
            float heat = InfantryWarnService.ExoHeat;
            DrawSuitBar(x + 8f, y + 72f, w - 16f, heat,
                UiLang.T("SERVO", "伺服") + "  " + Mathf.RoundToInt(heat * 100f) + "%",
                heat >= 0.72f ? new Color(1f, 0.4f, 0.1f) : new Color(0.95f, 0.8f, 0.25f));
            GUI.color = new Color(0.65f, 0.9f, 0.85f, 0.85f);
            GUI.Label(new Rect(x + 8f, y + 100f, w - 16f, 22f),
                UiLang.T("SPD ", "速度 ") + InfantryModeService.MoveSpeed.ToString("0.0")
                    + " m/s   "
                    + UiLang.T("PWR ", "电容 ")
                    + Mathf.RoundToInt(InfantryModeService.SprintFill() * 100f) + "%",
                _hudTiny);
            GUI.color = prev;
        }

        private static void DrawSysRail()
        {
            float w = 176f;
            float x = Screen.width - AssistMenuLayoutService.ChipW
                - AssistMenuLayoutService.ChipMarginRight - w - 12f;
            float y = 36f;
            Color prev = GUI.color;
            GUI.color = new Color(0.02f, 0.07f, 0.08f, 0.72f);
            GUI.DrawTexture(new Rect(x, y, w, 108f), _px);
            GUI.color = new Color(0.25f, 0.95f, 0.82f, 0.55f);
            GUI.DrawTexture(new Rect(x, y, w, 2f), _px);
            GUI.DrawTexture(new Rect(x + w - 2f, y, 2f, 108f), _px);
            GUI.color = new Color(0.35f, 1f, 0.88f, 0.95f);
            GUI.Label(new Rect(x + 8f, y + 4f, w - 12f, 20f),
                UiLang.T("SUIT BUS", "动力总线"), _hud);
            GUI.color = new Color(0.6f, 0.95f, 0.85f, 0.9f);
            string link = EmpPulseService.LocalPlayerStunned()
                ? UiLang.T("LINK  JAMMED", "链路  干扰")
                : UiLang.T("LINK  ACTIVE", "链路  正常");
            GUI.Label(new Rect(x + 8f, y + 26f, w - 12f, 18f), link, _hudTiny);
            GUI.Label(new Rect(x + 8f, y + 46f, w - 12f, 18f),
                UiLang.T("OPTICS  STAB", "光学  稳定"), _hudTiny);
            GUI.Label(new Rect(x + 8f, y + 66f, w - 12f, 18f),
                UiLang.T("SERVO  ", "伺服  ")
                    + (InfantryWarnService.Overheated()
                        ? UiLang.T("LIMIT", "限制")
                        : UiLang.T("READY", "就绪")),
                _hudTiny);
            GUI.Label(new Rect(x + 8f, y + 86f, w - 12f, 18f),
                UiLang.T("PLATE  ", "外甲  ") + InfantryModeService.OuterEffective()
                    + " / " + InfantryModeService.InnerArmor,
                _hudTiny);
            GUI.color = prev;
        }

        private static void DrawSuitBar(float x, float y, float w, float fill, string label, Color col)
        {
            if (fill < 0f) fill = 0f;
            if (fill > 1f) fill = 1f;
            Color prev = GUI.color;
            GUI.color = new Color(0.08f, 0.1f, 0.1f, 0.85f);
            GUI.DrawTexture(new Rect(x, y + 14f, w, 6f), _px);
            GUI.color = col;
            GUI.DrawTexture(new Rect(x, y + 14f, w * fill, 6f), _px);
            GUI.color = new Color(0.8f, 0.95f, 0.9f, 0.9f);
            GUI.Label(new Rect(x, y, w, 18f), label, _hudTiny);
            GUI.color = prev;
        }

        private static void DrawHelmetReticle()
        {
            if (InfantryCombat.Ads >= 0.22f)
                return;
            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            Color prev = GUI.color;
            GUI.color = new Color(0.25f, 0.95f, 0.82f, 0.72f);
            GUI.DrawTexture(new Rect(cx - 14f, cy - 1f, 9f, 2f), _px);
            GUI.DrawTexture(new Rect(cx + 5f, cy - 1f, 9f, 2f), _px);
            GUI.DrawTexture(new Rect(cx - 1f, cy - 14f, 2f, 9f), _px);
            GUI.DrawTexture(new Rect(cx - 1f, cy + 5f, 2f, 9f), _px);
            GUI.DrawTexture(new Rect(cx - 1.5f, cy - 1.5f, 3f, 3f), _px);
            GUI.DrawTexture(new Rect(cx - 22f, cy - 22f, 8f, 1.5f), _px);
            GUI.DrawTexture(new Rect(cx - 22f, cy - 22f, 1.5f, 8f), _px);
            GUI.DrawTexture(new Rect(cx + 14f, cy - 22f, 8f, 1.5f), _px);
            GUI.DrawTexture(new Rect(cx + 20.5f, cy - 22f, 1.5f, 8f), _px);
            GUI.DrawTexture(new Rect(cx - 22f, cy + 20.5f, 8f, 1.5f), _px);
            GUI.DrawTexture(new Rect(cx - 22f, cy + 14f, 1.5f, 8f), _px);
            GUI.DrawTexture(new Rect(cx + 14f, cy + 20.5f, 8f, 1.5f), _px);
            GUI.DrawTexture(new Rect(cx + 20.5f, cy + 14f, 1.5f, 8f), _px);
            float rng = _rngCached;
            GUI.color = new Color(0.45f, 1f, 0.88f, 0.75f);
            GUI.Label(new Rect(cx - 40f, cy + 26f, 80f, 14f),
                rng > 0.4f
                    ? ("RNG " + Mathf.RoundToInt(rng).ToString() + "m")
                    : "RNG ----",
                _hudTiny);
            GUI.color = prev;
        }

        private static void DrawContactPips()
        {
            Camera cam = Camera.main;
            if (cam == null)
                return;
            Color prev = GUI.color;
            int n = InfantryAi.Count;
            for (int i = 0; i < n; i++)
            {
                Vector3 bp;
                bool fr;
                if (!InfantryAi.TryMark(i, out bp, out fr) || fr)
                    continue;
                Vector3 world = bp + Vector3.up * 1.15f;
                Vector3 to = world - InfantryModeService.EyePos;
                if (to.sqrMagnitude > 700f * 700f || to.sqrMagnitude < 4f)
                    continue;
                Vector3 sp = cam.WorldToScreenPoint(world);
                if (sp.z < 0.8f)
                    continue;
                float gx = sp.x;
                float gy = Screen.height - sp.y;
                if (gx < 24f || gx > Screen.width - 24f || gy < 40f || gy > Screen.height - 40f)
                    continue;
                GUI.color = new Color(1f, 0.28f, 0.18f, 0.8f);
                GUI.DrawTexture(new Rect(gx - 6f, gy - 6f, 12f, 1.5f), _px);
                GUI.DrawTexture(new Rect(gx - 6f, gy + 4.5f, 12f, 1.5f), _px);
                GUI.DrawTexture(new Rect(gx - 6f, gy - 6f, 1.5f, 12f), _px);
                GUI.DrawTexture(new Rect(gx + 4.5f, gy - 6f, 1.5f, 12f), _px);
            }
            GUI.color = prev;
        }

        private static void EnsureVisor()
        {
            if (_visor != null)
                return;
            int n = 128;
            _visor = new Texture2D(n, n, TextureFormat.ARGB32, false);
            _visor.wrapMode = TextureWrapMode.Clamp;
            _visor.filterMode = FilterMode.Bilinear;
            Color[] px = new Color[n * n];
            float c = (n - 1) * 0.5f;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float nx = Mathf.Abs(x - c) / c;
                    float ny = Mathf.Abs(y - c) / c;
                    float e = Mathf.Max(nx, ny);
                    float a = 0f;
                    if (e > 0.62f)
                        a = Mathf.Pow(Mathf.InverseLerp(0.62f, 1f, e), 1.35f) * 0.62f;
                    px[y * n + x] = new Color(0.01f, 0.04f, 0.05f, a);
                }
            }
            _visor.SetPixels(px);
            _visor.Apply(false, true);
        }

        private static void DrawWeaponStrip()
        {
            string gun = InfantryCombat.HudLine();
            float x = 18f;
            float barY = Screen.height - 96f;
            Color prev = GUI.color;
            GUI.color = new Color(0.02f, 0.08f, 0.09f, 0.78f);
            GUI.DrawTexture(new Rect(x, barY, 460f, 80f), _px);
            GUI.color = new Color(0.25f, 0.95f, 0.82f, 0.7f);
            GUI.DrawTexture(new Rect(x, barY, 460f, 2f), _px);
            GUI.color = Color.white;
            Texture2D cur = InfantryWeaponIcons.Get(InfantryCombat.Gun);
            float tx = x + 10f;
            if (cur != null)
            {
                GUI.DrawTexture(new Rect(x + 8f, barY + 6f, 140f, 68f), cur, ScaleMode.ScaleToFit);
                tx = x + 156f;
            }
            GUI.Label(new Rect(tx, barY + 6f, 290f, 22f),
                "[" + InfantryCombat.HudSlot().ToString() + "]  " + gun, _hud);
            GUI.Label(new Rect(tx, barY + 28f, 290f, 18f),
                InfantryCombat.KitHint(), _hudTiny);
            string board = InfantryModeService.BoardKeyLabel();
            Aircraft claimed = InfantryModeService.ClaimedAircraft;
            if (claimed != null && !claimed.disabled)
            {
                GUI.Label(new Rect(tx, barY + 48f, 280f, 16f),
                    UiLang.T(board + "  board marked jet (12m)",
                        board + "  登机（靠近青色标记）"), _tiny);
            }
            else
            {
                GUI.Label(new Rect(tx, barY + 48f, 280f, 16f),
                    UiLang.T("B  buy aircraft   L  3rd person", "B  购买飞机   L  第三人称"), _tiny);
            }
            GUI.color = prev;
        }

        private static void DrawAds()
        {
            if (InfantryCombat.Ads < 0.28f)
                return;
            if (InfantryCombat.Gun == InfantryGun.G11)
            {
                DrawG11Optic();
                return;
            }
            EnsureStyles();
            EnsureScopeMask();
            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            float a = InfantryCombat.Ads;
            bool sniper = InfantryCombat.Gun == InfantryGun.Sniper;
            float fill = sniper ? Mathf.Lerp(0.78f, 0.96f, InfantryCombat.Zoom) : Mathf.Lerp(0.48f, 0.70f, InfantryCombat.Zoom);
            float side = Mathf.Min(Screen.width, Screen.height) * fill;
            Rect hole = new Rect(cx - side * 0.5f, cy - side * 0.5f, side, side);
            Color prev = GUI.color;
            GUI.color = new Color(0.01f, 0.015f, 0.02f, 0.93f * a);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, hole.y), _px);
            GUI.DrawTexture(new Rect(0f, hole.yMax, Screen.width, Screen.height - hole.yMax), _px);
            GUI.DrawTexture(new Rect(0f, hole.y, hole.x, hole.height), _px);
            GUI.DrawTexture(new Rect(hole.xMax, hole.y, Screen.width - hole.xMax, hole.height), _px);
            GUI.color = new Color(1f, 1f, 1f, a);
            if (_scopeMask != null)
                GUI.DrawTexture(hole, _scopeMask, ScaleMode.StretchToFill);
            DrawScopeReticle(cx, cy, side * 0.5f, a, sniper);
            if (sniper && InfantryCombat.SniperTarget != null)
            {
                float lk = InfantryCombat.SniperLock;
                GUI.color = lk >= 0.82f
                    ? new Color(0.2f, 1f, 0.45f, 0.92f * a)
                    : new Color(1f, 0.85f, 0.2f, 0.88f * a);
                GUI.Label(new Rect(cx - 80f, cy + side * 0.28f, 160f, 18f),
                    UiLang.T("GUIDE ", "制导 ") + Mathf.RoundToInt(lk * 100f) + "%", _tiny);
            }
            GUI.color = new Color(0.75f, 0.88f, 0.8f, 0.8f * a);
            GUI.Label(new Rect(cx - 50f, cy + side * 0.36f, 100f, 16f),
                InfantryCombat.ZoomPower().ToString("0.0") + "x", _tiny);
            GUI.color = prev;
        }

        private static void DrawG11Optic()
        {
            EnsureStyles();
            float a = InfantryCombat.Ads;
            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;
            float bw = Mathf.Min(Screen.width, Screen.height) * Mathf.Lerp(0.42f, 0.58f, InfantryCombat.Zoom);
            float bh = bw * 0.62f;
            Rect hole = new Rect(cx - bw * 0.5f, cy - bh * 0.5f, bw, bh);
            Color prev = GUI.color;
            GUI.color = new Color(0.01f, 0.03f, 0.04f, 0.94f * a);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, hole.y), _px);
            GUI.DrawTexture(new Rect(0f, hole.yMax, Screen.width, Screen.height - hole.yMax), _px);
            GUI.DrawTexture(new Rect(0f, hole.y, hole.x, hole.height), _px);
            GUI.DrawTexture(new Rect(hole.xMax, hole.y, Screen.width - hole.xMax, hole.height), _px);
            GUI.color = new Color(0.02f, 0.08f, 0.09f, 0.35f * a);
            GUI.DrawTexture(hole, _px);
            float th = 3f;
            GUI.color = new Color(0.25f, 0.95f, 0.82f, 0.88f * a);
            GUI.DrawTexture(new Rect(hole.x, hole.y, hole.width, th), _px);
            GUI.DrawTexture(new Rect(hole.x, hole.yMax - th, hole.width, th), _px);
            GUI.DrawTexture(new Rect(hole.x, hole.y, th, hole.height), _px);
            GUI.DrawTexture(new Rect(hole.xMax - th, hole.y, th, hole.height), _px);
            float inset = 10f;
            GUI.color = new Color(0.2f, 0.9f, 0.78f, 0.45f * a);
            GUI.DrawTexture(new Rect(hole.x + inset, hole.y + inset, hole.width - inset * 2f, 1.5f), _px);
            GUI.DrawTexture(new Rect(hole.x + inset, hole.yMax - inset, hole.width - inset * 2f, 1.5f), _px);
            GUI.DrawTexture(new Rect(hole.x + inset, hole.y + inset, 1.5f, hole.height - inset * 2f), _px);
            GUI.DrawTexture(new Rect(hole.xMax - inset, hole.y + inset, 1.5f, hole.height - inset * 2f), _px);
            GUI.color = new Color(0.3f, 1f, 0.85f, 0.9f * a);
            GUI.DrawTexture(new Rect(cx - 18f, cy - 1.5f, 12f, 3f), _px);
            GUI.DrawTexture(new Rect(cx + 6f, cy - 1.5f, 12f, 3f), _px);
            GUI.DrawTexture(new Rect(cx - 1.5f, cy + 6f, 3f, 14f), _px);
            GUI.DrawTexture(new Rect(cx - 3f, cy - 3f, 6f, 6f), _px);
            GUI.color = new Color(0.55f, 1f, 0.88f, 0.8f * a);
            GUI.Label(new Rect(hole.x + 12f, hole.y + 8f, 180f, 16f),
                UiLang.T("G11  SMART  4.73", "G11  智能弹  4.73"), _hud);
            GUI.Label(new Rect(hole.xMax - 90f, hole.y + 8f, 80f, 16f),
                InfantryCombat.ZoomPower().ToString("0.0") + "x", _hudTiny);
            GUI.Label(new Rect(cx - 70f, hole.yMax - 22f, 140f, 16f),
                UiLang.T("SEEK ON FIRE", "发射后自寻"), _hudTiny);
            GUI.color = prev;
        }

        private static void DrawScopeReticle(float cx, float cy, float r, float a, bool sniper)
        {
            Color line = sniper
                ? new Color(0.12f, 0.95f, 0.55f, 0.88f * a)
                : new Color(0.85f, 0.95f, 0.88f, 0.8f * a);
            GUI.color = line;
            float arm = sniper ? r * 0.72f : r * 0.38f;
            float gap = sniper ? 10f : 7f;
            float th = 1.6f;
            GUI.DrawTexture(new Rect(cx - arm, cy - th * 0.5f, arm - gap, th), _px);
            GUI.DrawTexture(new Rect(cx + gap, cy - th * 0.5f, arm - gap, th), _px);
            GUI.DrawTexture(new Rect(cx - th * 0.5f, cy - arm, th, arm - gap), _px);
            GUI.DrawTexture(new Rect(cx - th * 0.5f, cy + gap, th, arm - gap), _px);
            GUI.DrawTexture(new Rect(cx - 2f, cy - 2f, 4f, 4f), _px);
            if (!sniper)
                return;
            float[] ticks = new float[] { 0.18f, 0.34f, 0.5f };
            for (int i = 0; i < ticks.Length; i++)
            {
                float d = r * ticks[i];
                float tick = i == ticks.Length - 1 ? 9f : 6f;
                GUI.DrawTexture(new Rect(cx - d - 1f, cy - tick * 0.5f, 2f, tick), _px);
                GUI.DrawTexture(new Rect(cx + d - 1f, cy - tick * 0.5f, 2f, tick), _px);
                GUI.DrawTexture(new Rect(cx - tick * 0.5f, cy - d - 1f, tick, 2f), _px);
                GUI.DrawTexture(new Rect(cx - tick * 0.5f, cy + d - 1f, tick, 2f), _px);
            }
        }

        private static void EnsureScopeMask()
        {
            if (_scopeMask != null)
                return;
            int n = 256;
            _scopeMask = new Texture2D(n, n, TextureFormat.ARGB32, false);
            _scopeMask.wrapMode = TextureWrapMode.Clamp;
            _scopeMask.filterMode = FilterMode.Bilinear;
            Color[] px = new Color[n * n];
            float c = (n - 1) * 0.5f;
            float r0 = c * 0.84f;
            float r1 = c * 0.99f;
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float dx = x - c;
                    float dy = y - c;
                    float rr = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha;
                    if (rr < r0)
                        alpha = 0f;
                    else if (rr > r1)
                        alpha = 0.96f;
                    else
                        alpha = Mathf.InverseLerp(r0, r1, rr) * 0.96f;
                    px[y * n + x] = new Color(0.02f, 0.03f, 0.03f, alpha);
                }
            }
            _scopeMask.SetPixels(px);
            _scopeMask.Apply(false, true);
        }

        private static void DrawSkillRings()
        {
            float cx = Screen.width * 0.5f;
            float y = Screen.height - 148f;
            DrawRing(new Vector2(cx - 54f, y), 28f,
                InfantryModeService.SprintFill(),
                InfantryModeService.SprintActive,
                UiLang.T("SPRINT", "冲刺"),
                "SHIFT");
            DrawRing(new Vector2(cx + 54f, y), 28f,
                InfantryModeService.JumpFill(),
                false,
                UiLang.T("JUMP", "跳跃"),
                "SPACE");
        }

        private static void DrawRing(Vector2 c, float r, float fill, bool active, string name, string key)
        {
            Color prev = GUI.color;
            int segs = 36;
            float f = fill;
            if (f < 0f) f = 0f;
            if (f > 1f) f = 1f;
            Color on = active ? new Color(0.35f, 0.95f, 1f, 0.95f) : new Color(0.4f, 1f, 0.45f, 0.95f);
            Color off = new Color(0.15f, 0.18f, 0.16f, 0.7f);
            for (int i = 0; i < segs; i++)
            {
                float t = (i + 0.5f) / segs;
                GUI.color = t <= f ? on : off;
                float a = (i / (float)segs) * 360f * Mathf.Deg2Rad;
                float x = c.x + Mathf.Sin(a) * r;
                float y = c.y - Mathf.Cos(a) * r;
                GUI.DrawTexture(new Rect(x - 2.5f, y - 2.5f, 5f, 5f), _px);
            }
            GUI.color = Color.white;
            GUI.Label(new Rect(c.x - 36f, c.y - 10f, 72f, 16f), name, _tiny);
            GUI.Label(new Rect(c.x - 36f, c.y + 6f, 72f, 14f), key, _tiny);
            GUI.color = prev;
        }

        private static void ApplyCursor()
        {
            if (BlocksLook)
                HoldHudCursor();
            else
                ReleaseHudCursor();
        }

        private static void HoldHudCursor()
        {
            if (_hudCursorOn)
            {
                OritasyCursor.Pulse();
                return;
            }
            _hudCursorOn = true;
            OritasyCursor.Hold();
        }

        private static void ReleaseHudCursor()
        {
            if (!_hudCursorOn)
                return;
            _hudCursorOn = false;
            OritasyCursor.Release();
            if (InfantryModeService.Active)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private static void EnsureStyles()
        {
            if (_styles)
                return;
            _styles = true;
            _title = new GUIStyle(GUI.skin.label);
            _title.fontSize = 16;
            _title.fontStyle = FontStyle.Bold;
            _title.alignment = TextAnchor.MiddleCenter;
            _title.normal.textColor = new Color(0.75f, 1f, 0.8f);
            _body = new GUIStyle(GUI.skin.label);
            _body.fontSize = 13;
            _body.normal.textColor = Color.white;
            _tiny = new GUIStyle(GUI.skin.label);
            _tiny.fontSize = 11;
            _tiny.alignment = TextAnchor.MiddleCenter;
            _tiny.normal.textColor = new Color(0.75f, 0.8f, 0.78f);
            _hud = new GUIStyle(GUI.skin.label);
            _hud.fontSize = 13;
            _hud.fontStyle = FontStyle.Bold;
            _hud.alignment = TextAnchor.UpperLeft;
            _hud.clipping = TextClipping.Overflow;
            _hud.wordWrap = false;
            _hud.normal.textColor = new Color(0.35f, 1f, 0.88f);
            _hudTiny = new GUIStyle(GUI.skin.label);
            _hudTiny.fontSize = 12;
            _hudTiny.alignment = TextAnchor.UpperLeft;
            _hudTiny.clipping = TextClipping.Overflow;
            _hudTiny.wordWrap = false;
            _hudTiny.normal.textColor = new Color(0.55f, 0.95f, 0.85f);
            _hudTick = new GUIStyle(GUI.skin.label);
            _hudTick.fontSize = 11;
            _hudTick.alignment = TextAnchor.MiddleCenter;
            _hudTick.clipping = TextClipping.Overflow;
            _hudTick.wordWrap = false;
            _hudTick.normal.textColor = new Color(0.55f, 0.95f, 0.85f);
            _hudBig = new GUIStyle(GUI.skin.label);
            _hudBig.fontSize = 14;
            _hudBig.fontStyle = FontStyle.Bold;
            _hudBig.normal.textColor = new Color(0.35f, 1f, 0.88f);
            _px = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _px.SetPixel(0, 0, Color.white);
            _px.Apply();
        }
    }
}
