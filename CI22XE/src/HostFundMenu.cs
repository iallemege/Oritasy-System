using System;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using NuclearOption.Networking;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// F7 host-only fund grant: pick any player and AddAllocation with no deduct / no cap.
    /// Visible and usable only when this machine is the listen-server host.
    /// </summary>
    internal static class HostFundMenu
    {
        private const float MinGrantM = 0.1f;
        private const int MaxPlayersCached = 48;

        private sealed class FundPeer
        {
            public ulong SteamId;
            public string Name;
            public bool IsLocal;
        }

        private static ConfigEntry<KeyCode> _menuKey;
        private static bool _menuOpen;
        private static readonly List<FundPeer> _peers = new List<FundPeer>(16);
        private static ulong _selectedSteamId;
        private static bool _selectedIsLocal;
        private static Vector2 _playerScroll;
        private static string _amountText = "100";
        private static float _nextPeerRefresh;
        private static string _status = "";
        private static float _statusUntil;

        private static GUIStyle _titleStyle;
        private static GUIStyle _labelStyle;
        private static GUIStyle _btnStyle;
        private static GUIStyle _chipStyle;
        private static GUIStyle _peerStyle;
        private static GUIStyle _fieldStyle;
        private static bool _cursorHeld;

        internal static bool MenuOpen
        {
            get { return _menuOpen; }
        }

        internal static void Bind(ConfigFile config)
        {
            _menuKey = config.Bind("HostFund", "MenuKey", KeyCode.F7,
                "Host-only: open fund grant menu (ignore transfer limits).");
        }

        internal static void CloseMenuFromOutside()
        {
            CloseMenu();
        }

        internal static void Tick()
        {
            if (!IsLocalHost())
            {
                if (_menuOpen)
                    CloseMenu();
                return;
            }

            KeyCode menu = _menuKey != null ? _menuKey.Value : KeyCode.F7;
            if (menu != KeyCode.None && Input.GetKeyDown(menu))
            {
                if (_menuOpen)
                    CloseMenu();
                else
                    OpenMenu();
            }
            if (_menuOpen && Input.GetKeyDown(KeyCode.Escape))
                CloseMenu();
            if (_menuOpen && Time.unscaledTime >= _nextPeerRefresh)
                RefreshPeers();
        }

        internal static void DrawGui()
        {
            EnsureStyles();
            if (!IsLocalHost())
                return;
            if (Plugin.AllowThirdPersonUi)
                DrawCornerHint();
            if (!_menuOpen)
                return;
            HoldCursor();
            DrawMenu();
        }

        private static bool IsLocalHost()
        {
            try
            {
                NetworkManagerNuclearOption nm = NetworkManagerNuclearOption.i;
                return nm != null && nm.Server != null && nm.Server.Active;
            }
            catch
            {
                return false;
            }
        }

        private static void OpenMenu()
        {
            if (!IsLocalHost())
                return;
            if (MissileCameraHud.ManualActive)
                return;
            if (AircraftManeuverGui.IsOpen)
                AircraftManeuverGui.Close();
            if (PlayerAutopilot.MenuOpen)
                PlayerAutopilot.CloseMenuFromOutside();
            if (AerialResupply.MenuOpen)
                AerialResupply.CloseMenuFromOutside();
            if (WarThunderRwrHud.LayoutMenuOpen)
                WarThunderRwrHud.CloseLayoutMenuFromOutside();
            if (BeginnerAssist.MenuOpen)
                BeginnerAssist.CloseMenuFromOutside();
            if (IlsSettingsMenu.MenuOpen)
                IlsSettingsMenu.CloseMenuFromOutside();
            if (PrivateMessageMenu.MenuOpen)
                PrivateMessageMenu.CloseMenuFromOutside();
            if (KillChoiceMenu.MenuOpen)
                KillChoiceMenu.CloseMenuFromOutside();
            AirframeWearGui.CloseFromOutside();
            PlayerAutopilot.CloseWeXonSupportMenu();
            _menuOpen = true;
            CaptureCursor();
            RefreshPeers();
        }

        private static void CloseMenu()
        {
            _menuOpen = false;
            ReleaseCursor();
        }

        private static void DrawCornerHint()
        {
            if (Event.current != null && Event.current.type != EventType.Repaint)
                return;
            Player local = null;
            try { GameManager.GetLocalPlayer(out local); }
            catch { }
            if (local == null)
                return;

            Rect chip = PlayerAutopilot.CornerChipRect(AssistMenuLayoutService.SlotF7);
            Color prev = GUI.color;
            GUI.color = new Color(0.05f, 0.07f, 0.1f, 0.78f);
            GUI.DrawTexture(chip, Texture2D.whiteTexture);
            GUI.color = _menuOpen
                ? new Color(0.95f, 0.8f, 0.35f, 0.95f)
                : new Color(0.55f, 0.85f, 1f, 0.9f);
            GUI.DrawTexture(new Rect(chip.x, chip.y, chip.width, 2f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            string line = _menuOpen
                ? UiLang.T("F7 FUND  |  OPEN", "F7 房主资金  |  已打开")
                : UiLang.T("F7 FUND  |  grant", "F7 房主资金  |  分发");
            _chipStyle.normal.textColor = new Color(0.85f, 0.95f, 1f, 0.95f);
            GUI.Label(new Rect(chip.x + 6f, chip.y, chip.width - 12f, chip.height), line, _chipStyle);
            GUI.color = prev;
        }

        private static void DrawMenu()
        {
            Rect box = AssistMenuLayoutService.HostFundMenuRect(UiScaleService.Width, UiScaleService.Height);
            Color prev = GUI.color;
            GUI.color = new Color(0.06f, 0.08f, 0.1f, 0.92f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = new Color(1f, 0.75f, 0.35f, 0.95f);
            GUI.DrawTexture(new Rect(box.x, box.y, box.width, 3f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(box.x + 16f, box.y + 10f, box.width - 32f, 24f),
                UiLang.T("HOST FUND GRANT  (F7)", "房主资金分发（F7）"), _titleStyle);
            GUI.Label(new Rect(box.x + 16f, box.y + 36f, box.width - 32f, 36f),
                UiLang.T("Host only — grant Allocation to any player (no deduct, no cap).",
                    "仅房主 — 给任意玩家发放资金（不扣自己、无上限）。"),
                _labelStyle);

            float pad = 12f;
            float leftW = 200f;
            float y0 = box.y + 80f;
            float listH = box.height - 80f - 56f;
            Rect listR = new Rect(box.x + pad, y0, leftW, listH);
            GUI.color = new Color(0.04f, 0.05f, 0.07f, 0.85f);
            GUI.DrawTexture(listR, Texture2D.whiteTexture);
            GUI.color = Color.white;

            _playerScroll = GUI.BeginScrollView(listR, _playerScroll,
                new Rect(0f, 0f, leftW - 18f, Mathf.Max(listH, _peers.Count * 28f + 8f)));
            float py = 4f;
            if (_peers.Count == 0)
            {
                GUI.Label(new Rect(6f, py, leftW - 28f, 40f),
                    UiLang.T("No players found.", "未找到玩家。"), _labelStyle);
            }
            for (int i = 0; i < _peers.Count; i++)
            {
                FundPeer peer = _peers[i];
                bool sel = IsSelectedPeer(peer);
                Rect br = new Rect(4f, py, leftW - 26f, 26f);
                if (sel)
                {
                    GUI.color = new Color(0.2f, 0.45f, 0.65f, 0.95f);
                    GUI.DrawTexture(br, Texture2D.whiteTexture);
                    GUI.color = Color.white;
                }
                string label = peer.IsLocal
                    ? (peer.Name + UiLang.T(" (you)", "（自己）"))
                    : peer.Name;
                if (GUI.Button(br, label, _peerStyle))
                {
                    _selectedSteamId = peer.SteamId;
                    _selectedIsLocal = peer.IsLocal;
                }
                py += 28f;
            }
            GUI.EndScrollView();

            float rightX = box.x + pad + leftW + 12f;
            float rightW = box.width - pad - (rightX - box.x);
            GUI.Label(new Rect(rightX, y0, rightW, 22f),
                UiLang.T("Amount (M)", "金额（M）"), _labelStyle);
            _amountText = GUI.TextField(new Rect(rightX, y0 + 26f, rightW, 28f),
                _amountText ?? "", 16, _fieldStyle);

            float by = y0 + 68f;
            float bw = (rightW - 8f) * 0.5f;
            if (GUI.Button(new Rect(rightX, by, bw, 28f), "+50", _btnStyle))
                NudgeAmount(50f);
            if (GUI.Button(new Rect(rightX + bw + 8f, by, bw, 28f), "+100", _btnStyle))
                NudgeAmount(100f);
            by += 36f;
            if (GUI.Button(new Rect(rightX, by, bw, 28f), "+500", _btnStyle))
                NudgeAmount(500f);
            if (GUI.Button(new Rect(rightX + bw + 8f, by, bw, 28f), "+1000", _btnStyle))
                NudgeAmount(1000f);
            by += 44f;

            Player selPlayer = ResolveSelectedPlayer();
            string selName = selPlayer != null ? ResolveName(selPlayer) : FindPeerName(_selectedSteamId);
            float have = 0f;
            if (selPlayer != null)
            {
                try { have = selPlayer.Allocation; }
                catch { }
            }
            GUI.Label(new Rect(rightX, by, rightW, 40f),
                UiLang.T(
                    "Target: " + selName + "\nCurrent funds: " + have.ToString("0.0") + "M",
                    "目标：" + selName + "\n当前资金：" + have.ToString("0.0") + "M"),
                _labelStyle);
            by += 48f;

            if (GUI.Button(new Rect(rightX, by, rightW, 36f),
                UiLang.T("GRANT FUNDS", "发放资金"), _btnStyle))
                TryGrant();

            if (!string.IsNullOrEmpty(_status) && Time.unscaledTime < _statusUntil)
                GUI.Label(new Rect(box.x + 16f, box.y + box.height - 48f, box.width - 140f, 28f),
                    _status, _labelStyle);

            if (GUI.Button(new Rect(box.x + box.width - 116f, box.y + box.height - 44f, 100f, 32f),
                UiLang.T("CLOSE", "关闭"), _btnStyle))
                CloseMenu();

            GUI.color = prev;
        }

        private static void NudgeAmount(float add)
        {
            float cur;
            if (!TryParseAmount(_amountText, out cur))
                cur = 0f;
            cur += add;
            if (cur < MinGrantM)
                cur = MinGrantM;
            _amountText = cur.ToString("0.0", CultureInfo.InvariantCulture);
        }

        private static void TryGrant()
        {
            if (!IsLocalHost())
            {
                SetStatus(UiLang.T("Host only.", "仅房主可用。"));
                return;
            }
            if (!_selectedIsLocal && _selectedSteamId == 0UL)
            {
                SetStatus(UiLang.T("Select a player.", "请选择玩家。"));
                return;
            }
            float amount;
            if (!TryParseAmount(_amountText, out amount))
            {
                SetStatus(UiLang.T("Invalid amount (min " + MinGrantM.ToString("0.0") + "M).",
                    "金额无效（最少 " + MinGrantM.ToString("0.0") + "M）。"));
                return;
            }
            Player target = ResolveSelectedPlayer();
            if (target == null)
            {
                SetStatus(UiLang.T("Player not found.", "找不到玩家。"));
                RefreshPeers();
                return;
            }
            try
            {
                target.AddAllocation(amount);
            }
            catch (Exception ex)
            {
                SetStatus(UiLang.T("Grant failed: " + ex.Message, "发放失败：" + ex.Message));
                return;
            }
            string name = ResolveName(target);
            SetStatus(UiLang.T(
                "Granted +" + amount.ToString("0.0") + "M → " + name,
                "已发放 +" + amount.ToString("0.0") + "M → " + name));
        }

        private static bool TryParseAmount(string raw, out float amount)
        {
            amount = 0f;
            if (string.IsNullOrEmpty(raw))
                return false;
            string s = raw.Trim().Replace(',', '.');
            if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out amount))
                return false;
            amount = Mathf.Floor(amount * 10f + 0.001f) / 10f;
            if (amount < MinGrantM)
                return false;
            if (float.IsNaN(amount) || float.IsInfinity(amount))
                return false;
            return true;
        }

        private static void SetStatus(string s)
        {
            _status = s ?? "";
            _statusUntil = Time.unscaledTime + 3.5f;
        }

        private static void RefreshPeers()
        {
            _nextPeerRefresh = Time.unscaledTime + 1f;
            _peers.Clear();
            // Always include local host first (SteamID may be 0 in listen/solo).
            try
            {
                Player local;
                if (GameManager.GetLocalPlayer(out local) && local != null)
                    TryAddPeer(local);
            }
            catch { }

            try
            {
                NetworkManagerNuclearOption nm = NetworkManagerNuclearOption.i;
                if (nm != null && nm.GamePlayers != null)
                {
                    List<Player> list = nm.GamePlayers;
                    for (int i = 0; i < list.Count; i++)
                        TryAddPeer(list[i]);
                }
            }
            catch { }

            try
            {
                foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
                {
                    if (hq == null)
                        continue;
                    List<Player> players = hq.GetPlayers(false);
                    if (players == null)
                        continue;
                    for (int i = 0; i < players.Count; i++)
                        TryAddPeer(players[i]);
                }
            }
            catch { }

            bool still = false;
            for (int i = 0; i < _peers.Count; i++)
            {
                if (IsSelectedPeer(_peers[i]))
                {
                    still = true;
                    _selectedIsLocal = _peers[i].IsLocal;
                    _selectedSteamId = _peers[i].SteamId;
                    break;
                }
            }
            if (!still && _peers.Count > 0)
            {
                _selectedSteamId = _peers[0].SteamId;
                _selectedIsLocal = _peers[0].IsLocal;
            }
        }

        private static bool IsSelectedPeer(FundPeer peer)
        {
            if (peer == null)
                return false;
            if (_selectedIsLocal)
                return peer.IsLocal;
            return peer.SteamId != 0UL && peer.SteamId == _selectedSteamId;
        }

        private static Player ResolveSelectedPlayer()
        {
            if (_selectedIsLocal)
            {
                try
                {
                    Player local;
                    if (GameManager.GetLocalPlayer(out local) && local != null)
                        return local;
                }
                catch { }
            }
            return FindPlayerBySteam(_selectedSteamId);
        }

        private static void TryAddPeer(Player p)
        {
            if (p == null || _peers.Count >= MaxPlayersCached)
                return;
            bool isLocal = false;
            try { isLocal = GameManager.IsLocalPlayer(p); }
            catch { }
            ulong id = 0UL;
            try { id = p.SteamID; }
            catch { }
            // Non-local with no SteamID cannot be targeted reliably.
            if (id == 0UL && !isLocal)
                return;
            for (int i = 0; i < _peers.Count; i++)
            {
                if (isLocal && _peers[i].IsLocal)
                    return;
                if (id != 0UL && _peers[i].SteamId == id)
                    return;
            }
            FundPeer peer = new FundPeer();
            peer.SteamId = id;
            peer.Name = ResolveName(p);
            peer.IsLocal = isLocal;
            _peers.Add(peer);
        }

        private static string FindPeerName(ulong id)
        {
            for (int i = 0; i < _peers.Count; i++)
            {
                if (_selectedIsLocal && _peers[i].IsLocal)
                    return _peers[i].Name;
                if (_peers[i].SteamId == id)
                    return _peers[i].Name;
            }
            return id == 0UL ? "?" : id.ToString("X");
        }

        private static string ResolveName(Player p)
        {
            if (p == null)
                return "?";
            try
            {
                string n = p.GetDisplayName(PlayerNameContext.ChatOrLeaderboard);
                if (!string.IsNullOrEmpty(n))
                    return n;
            }
            catch { }
            try { return p.SteamID.ToString("X"); }
            catch { }
            return "?";
        }

        private static Player FindPlayerBySteam(ulong steamId)
        {
            if (steamId == 0UL)
                return null;
            try
            {
                NetworkManagerNuclearOption nm = NetworkManagerNuclearOption.i;
                if (nm != null && nm.GamePlayers != null)
                {
                    List<Player> list = nm.GamePlayers;
                    for (int i = 0; i < list.Count; i++)
                    {
                        Player p = list[i];
                        if (p == null)
                            continue;
                        try
                        {
                            if (p.SteamID == steamId)
                                return p;
                        }
                        catch { }
                    }
                }
            }
            catch { }
            try
            {
                foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
                {
                    if (hq == null)
                        continue;
                    List<Player> players = hq.GetPlayers(false);
                    if (players == null)
                        continue;
                    for (int i = 0; i < players.Count; i++)
                    {
                        Player p = players[i];
                        if (p == null)
                            continue;
                        try
                        {
                            if (p.SteamID == steamId)
                                return p;
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return null;
        }

        private static void CaptureCursor()
        {
            if (_cursorHeld)
                return;
            OritasyCursor.Hold();
            _cursorHeld = true;
        }

        private static void HoldCursor()
        {
            if (!_cursorHeld)
                CaptureCursor();
            OritasyCursor.Pulse();
        }

        private static void ReleaseCursor()
        {
            if (!_cursorHeld)
                return;
            OritasyCursor.Release();
            _cursorHeld = false;
        }

        private static void EnsureStyles()
        {
            if (_titleStyle != null)
                return;
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(1f, 0.9f, 0.65f, 1f) }
            };
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                wordWrap = true,
                normal = { textColor = new Color(0.88f, 0.9f, 0.86f, 1f) }
            };
            _btnStyle = new GUIStyle(GUI.skin.button) { fontSize = 13, fontStyle = FontStyle.Bold };
            _chipStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight,
                clipping = TextClipping.Clip
            };
            _peerStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Normal
            };
            _fieldStyle = new GUIStyle(GUI.skin.textField) { fontSize = 14 };
        }
    }
}
