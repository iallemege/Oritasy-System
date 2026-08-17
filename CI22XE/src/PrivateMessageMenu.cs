using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using Mirage;
using NuclearOption.Chat;
using NuclearOption.Networking;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// F5 in-match private messages + fund transfer submenu.
    /// ChatManager wire: #OPM# (PM), #OTF# (transfer request), #OTFOK# / #OTFFAIL# (acks).
    /// Host with Oritasy retargets / executes AddAllocation so vanilla chat stays clean.
    /// </summary>
    internal static class PrivateMessageMenu
    {
        private const string WirePrefix = "#OPM#";
        private const string TransferPrefix = "#OTF#";
        private const string TransferOkPrefix = "#OTFOK#";
        private const string TransferFailPrefix = "#OTFFAIL#";
        private const int MaxWireLen = 128;
        private const int MaxHistory = 80;
        private const int MaxPlayersCached = 32;
        private const float MinTransferM = 0.1f;
        private const float MaxTransferM = 9999f;

        private enum F5Tab
        {
            Message = 0,
            Transfer = 1
        }

        private static ConfigEntry<KeyCode> _menuKey;
        private static bool _menuOpen;
        private static bool _unread;
        private static F5Tab _tab = F5Tab.Message;
        private static string _draft = "";
        private static string _transferDraft = "1";
        private static string _status = "";
        private static float _statusUntil;
        private static ulong _selectedSteamId;
        private static Vector2 _playerScroll;
        private static Vector2 _chatScroll;
        private static readonly List<PmLine> _history = new List<PmLine>(MaxHistory);
        private static readonly List<PmPeer> _peers = new List<PmPeer>(MaxPlayersCached);
        private static float _nextPeerRefresh;
        private static string _lastLocalWire;
        private static float _lastLocalWireAt;

        private static GUIStyle _titleStyle;
        private static GUIStyle _labelStyle;
        private static GUIStyle _btnStyle;
        private static GUIStyle _chipStyle;
        private static GUIStyle _msgStyle;
        private static GUIStyle _peerStyle;
        private static bool _cursorHeld;

        internal static bool MenuOpen
        {
            get { return _menuOpen; }
        }

        internal static void Bind(ConfigFile config)
        {
            _menuKey = config.Bind("PrivateMessage", "MenuKey", KeyCode.F5,
                "Open in-match private message / fund-transfer menu.");
        }

        internal static void CloseMenuFromOutside()
        {
            CloseMenu();
        }

        internal static void Tick()
        {
            KeyCode menu = _menuKey != null ? _menuKey.Value : KeyCode.F5;
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
            if (Plugin.AllowThirdPersonUi)
                DrawCornerHint();
            if (!_menuOpen)
                return;
            HoldCursor();
            DrawMenu();
        }

        internal static void ReceiveWire(string message, Player sender)
        {
            ulong toId;
            string body;
            if (!TryParseWire(message, out toId, out body))
                return;

            Player local = null;
            try { GameManager.GetLocalPlayer(out local); }
            catch { }
            ulong localId = 0UL;
            if (local != null)
            {
                try { localId = local.SteamID; }
                catch { }
            }
            ulong fromId = 0UL;
            string fromName = "?";
            if (sender != null)
            {
                try { fromId = sender.SteamID; }
                catch { }
                fromName = ResolveName(sender);
            }

            bool forMe = toId == localId || fromId == localId;
            if (!forMe || localId == 0UL)
                return;

            // Skip duplicate local echo right after send.
            if (!string.IsNullOrEmpty(_lastLocalWire)
                && string.Equals(message, _lastLocalWire, StringComparison.Ordinal)
                && Time.unscaledTime - _lastLocalWireAt < 1.5f
                && fromId == localId)
            {
                return;
            }

            bool outgoing = fromId == localId;
            ulong peerId = outgoing ? toId : fromId;
            string peerName = outgoing ? FindPeerName(toId) : fromName;
            AddLine(peerId, peerName, body, outgoing);
            if (!_menuOpen)
                _unread = true;
            else if (_selectedSteamId == 0UL)
                _selectedSteamId = peerId;
        }

        private static void OpenMenu()
        {
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
            if (KillChoiceMenu.MenuOpen)
                KillChoiceMenu.CloseMenuFromOutside();
            if (HostFundMenu.MenuOpen)
                HostFundMenu.CloseMenuFromOutside();
            AirframeWearGui.CloseFromOutside();
            PlayerAutopilot.CloseWeXonSupportMenu();
            _menuOpen = true;
            _unread = false;
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
            // Show in-match only (have a local player context).
            Player local = null;
            try { GameManager.GetLocalPlayer(out local); }
            catch { }
            if (local == null)
                return;

            Rect chip = PlayerAutopilot.CornerChipRect(AssistMenuLayoutService.SlotF5);
            Color prev = GUI.color;
            GUI.color = new Color(0.05f, 0.07f, 0.1f, 0.78f);
            GUI.DrawTexture(chip, Texture2D.whiteTexture);
            GUI.color = _menuOpen
                ? new Color(0.95f, 0.8f, 0.35f, 0.95f)
                : (_unread
                    ? new Color(1f, 0.45f, 0.35f, 0.95f)
                    : new Color(0.55f, 0.85f, 1f, 0.9f));
            GUI.DrawTexture(new Rect(chip.x, chip.y, chip.width, 2f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            string line;
            if (_menuOpen)
                line = UiLang.T("F5 PM  |  OPEN", "F5 私信/转账  |  已打开");
            else if (_unread)
                line = UiLang.T("F5 PM  |  NEW", "F5 私信/转账  |  新消息");
            else
                line = UiLang.T("F5 PM  |  msg / transfer", "F5 私信 / 转账");
            _chipStyle.normal.textColor = new Color(0.85f, 0.95f, 1f, 0.95f);
            GUI.Label(new Rect(chip.x + 6f, chip.y, chip.width - 12f, chip.height), line, _chipStyle);
            GUI.color = prev;
        }

        private static void DrawMenu()
        {
            Rect box = AssistMenuLayoutService.PrivateMessageMenuRect(UiScaleService.Width, UiScaleService.Height);
            Color prev = GUI.color;
            GUI.color = new Color(0.06f, 0.08f, 0.1f, 0.92f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = new Color(0.55f, 0.85f, 1f, 0.95f);
            GUI.DrawTexture(new Rect(box.x, box.y, box.width, 3f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(box.x + 16f, box.y + 8f, box.width - 32f, 22f),
                UiLang.T("PLAYER COMMS  (F5)", "玩家通讯（F5）"), _titleStyle);

            float pad = 12f;
            float tabY = box.y + 34f;
            float tabW = (box.width - pad * 2f - 8f) * 0.5f;
            DrawTabButton(new Rect(box.x + pad, tabY, tabW, 26f), F5Tab.Message,
                UiLang.T("MESSAGE", "私信"));
            DrawTabButton(new Rect(box.x + pad + tabW + 8f, tabY, tabW, 26f), F5Tab.Transfer,
                UiLang.T("TRANSFER", "转账"));

            GUI.Label(new Rect(box.x + 16f, tabY + 30f, box.width - 32f, 28f),
                _tab == F5Tab.Transfer
                    ? UiLang.T("Select a player, enter amount (M), Transfer. Host needs Oritasy.",
                        "选定玩家并输入金额（M）后转账。主机需安装 Oritasy。")
                    : UiLang.T("Select a player, type, Send. Host with Oritasy hides payload.",
                        "选定玩家后输入并发送。主机安装 Oritasy 时可隐藏载荷。"),
                _labelStyle);

            float leftW = 168f;
            float y0 = box.y + 96f;
            float listH = box.height - 96f - 96f;
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
                    UiLang.T("No other players\nin this match.", "当前对局无其他玩家。"),
                    _labelStyle);
            }
            for (int i = 0; i < _peers.Count; i++)
            {
                PmPeer peer = _peers[i];
                bool sel = peer.SteamId == _selectedSteamId;
                Rect br = new Rect(4f, py, leftW - 26f, 26f);
                if (sel)
                {
                    GUI.color = new Color(0.2f, 0.45f, 0.65f, 0.95f);
                    GUI.DrawTexture(br, Texture2D.whiteTexture);
                    GUI.color = Color.white;
                }
                if (GUI.Button(br, peer.Name, _peerStyle))
                    _selectedSteamId = peer.SteamId;
                py += 28f;
            }
            GUI.EndScrollView();

            float chatX = box.x + pad + leftW + 8f;
            float chatW = box.width - pad * 2f - leftW - 8f;
            Rect chatR = new Rect(chatX, y0, chatW, listH);
            GUI.color = new Color(0.04f, 0.05f, 0.07f, 0.85f);
            GUI.DrawTexture(chatR, Texture2D.whiteTexture);
            GUI.color = Color.white;

            if (_tab == F5Tab.Transfer)
                DrawTransferPanel(chatR);
            else
                DrawMessagePanel(chatR);

            float inputY = box.y + box.height - 84f;
            if (_tab == F5Tab.Message)
            {
                GUI.SetNextControlName("OritasyPmDraft");
                _draft = GUI.TextField(new Rect(box.x + pad, inputY, box.width - pad * 2f - 100f, 28f),
                    _draft ?? "", 96);

                if (GUI.Button(new Rect(box.x + box.width - pad - 92f, inputY, 92f, 28f),
                    UiLang.T("SEND", "发送"), _btnStyle))
                    TrySend();

                if (Event.current != null
                    && Event.current.type == EventType.KeyDown
                    && (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)
                    && string.Equals(GUI.GetNameOfFocusedControl(), "OritasyPmDraft", StringComparison.Ordinal))
                {
                    TrySend();
                    Event.current.Use();
                }
            }
            else
            {
                if (GUI.Button(new Rect(box.x + pad, inputY, box.width - pad * 2f - 110f, 28f),
                    UiLang.T("TRANSFER FUNDS", "确认转账"), _btnStyle))
                    TryTransfer();
                if (GUI.Button(new Rect(box.x + box.width - pad - 100f, inputY, 100f, 28f),
                    UiLang.T("CLOSE", "关闭"), _btnStyle))
                    CloseMenu();
            }

            string status = "";
            if (!string.IsNullOrEmpty(_status) && Time.unscaledTime < _statusUntil)
                status = _status;
            GUI.Label(new Rect(box.x + pad, inputY + 32f, box.width - pad * 2f - 110f, 20f),
                status, _labelStyle);

            if (_tab == F5Tab.Message
                && GUI.Button(new Rect(box.x + box.width - pad - 100f, inputY + 34f, 100f, 28f),
                    UiLang.T("CLOSE", "关闭"), _btnStyle))
                CloseMenu();

            GUI.color = prev;
        }

        private static void DrawTabButton(Rect r, F5Tab tab, string label)
        {
            bool on = _tab == tab;
            Color prev = GUI.color;
            GUI.color = on
                ? new Color(0.25f, 0.5f, 0.7f, 0.95f)
                : new Color(0.12f, 0.15f, 0.18f, 0.95f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = prev;
            if (GUI.Button(r, label, _btnStyle))
                _tab = tab;
        }

        private static void DrawMessagePanel(Rect chatR)
        {
            List<PmLine> thread = BuildThread(_selectedSteamId);
            float contentH = Mathf.Max(chatR.height, thread.Count * 36f + 8f);
            _chatScroll = GUI.BeginScrollView(chatR, _chatScroll,
                new Rect(0f, 0f, chatR.width - 18f, contentH));
            float cy = 4f;
            if (_selectedSteamId == 0UL)
            {
                GUI.Label(new Rect(8f, cy, chatR.width - 28f, 40f),
                    UiLang.T("Pick a player on the left.", "请先在左侧选择玩家。"),
                    _labelStyle);
            }
            else if (thread.Count == 0)
            {
                GUI.Label(new Rect(8f, cy, chatR.width - 28f, 40f),
                    UiLang.T("No messages yet with this player.", "与该玩家尚无私信。"),
                    _labelStyle);
            }
            for (int i = 0; i < thread.Count; i++)
            {
                PmLine line = thread[i];
                string who = line.Outgoing
                    ? UiLang.T("You", "我")
                    : line.PeerName;
                _msgStyle.normal.textColor = line.Outgoing
                    ? new Color(0.65f, 0.95f, 0.75f, 1f)
                    : new Color(0.85f, 0.9f, 1f, 1f);
                GUI.Label(new Rect(8f, cy, chatR.width - 28f, 16f), who + ":", _msgStyle);
                _msgStyle.normal.textColor = new Color(0.9f, 0.92f, 0.95f, 1f);
                GUI.Label(new Rect(8f, cy + 14f, chatR.width - 28f, 20f), line.Text, _msgStyle);
                cy += 36f;
            }
            GUI.EndScrollView();
        }

        private static void DrawTransferPanel(Rect panel)
        {
            float x = panel.x + 10f;
            float y = panel.y + 10f;
            float w = panel.width - 20f;

            float funds = ReadLocalAllocation();
            string peer = _selectedSteamId != 0UL
                ? FindPeerName(_selectedSteamId)
                : UiLang.T("(none)", "（未选）");

            GUI.Label(new Rect(x, y, w, 20f),
                UiLang.T("Your funds:  " + funds.ToString("0.0") + "M",
                    "你的资金：  " + funds.ToString("0.0") + "M"), _labelStyle);
            y += 22f;
            GUI.Label(new Rect(x, y, w, 20f),
                UiLang.T("Send to:  " + peer, "转给：  " + peer), _labelStyle);
            y += 28f;

            GUI.Label(new Rect(x, y, w, 18f),
                UiLang.T("Amount (millions)", "金额（百万）"), _labelStyle);
            y += 20f;
            GUI.SetNextControlName("OritasyTfDraft");
            _transferDraft = GUI.TextField(new Rect(x, y, w * 0.45f, 26f),
                _transferDraft ?? "", 12);
            y += 34f;

            float btnW = (w - 24f) / 4f;
            float[] presets = { 0.5f, 1f, 5f, 10f };
            for (int i = 0; i < presets.Length; i++)
            {
                string lab = presets[i].ToString("0.#") + "M";
                if (GUI.Button(new Rect(x + i * (btnW + 8f), y, btnW, 28f), lab, _btnStyle))
                    _transferDraft = presets[i].ToString("0.#", CultureInfo.InvariantCulture);
            }
            y += 36f;

            if (GUI.Button(new Rect(x, y, w * 0.48f, 28f),
                UiLang.T("HALF", "一半"), _btnStyle))
            {
                float half = Mathf.Floor(funds * 5f) / 10f;
                if (half < MinTransferM)
                    half = 0f;
                _transferDraft = half.ToString("0.0", CultureInfo.InvariantCulture);
            }
            if (GUI.Button(new Rect(x + w * 0.52f, y, w * 0.48f, 28f),
                UiLang.T("ALL", "全部"), _btnStyle))
            {
                float all = Mathf.Floor(funds * 10f) / 10f;
                _transferDraft = all.ToString("0.0", CultureInfo.InvariantCulture);
            }
            y += 36f;

            GUI.Label(new Rect(x, y, w, 48f),
                UiLang.T("Server-side only. Requires host running Oritasy.",
                    "由主机执行划转；主机需安装 Oritasy。"),
                _labelStyle);
        }

        private static void TrySend()
        {
            if (_selectedSteamId == 0UL)
            {
                SetStatus(UiLang.T("Select a player first.", "请先选择玩家。"));
                return;
            }
            string text = _draft != null ? _draft.Trim() : "";
            if (text.Length == 0)
            {
                SetStatus(UiLang.T("Message is empty.", "消息为空。"));
                return;
            }
            string wire;
            if (!TryBuildWire(_selectedSteamId, text, out wire))
            {
                SetStatus(UiLang.T("Message too long (chat limit 128).", "消息过长（聊天上限 128）。"));
                return;
            }
            try
            {
                if (!ChatManager.CanSend(wire, false, true))
                {
                    SetStatus(UiLang.T("Cannot send (rate limit / not in match).",
                        "无法发送（限速或未在对局中）。"));
                    return;
                }
                ChatManager.SendChatMessage(wire, true);
            }
            catch (Exception ex)
            {
                SetStatus(UiLang.T("Send failed: " + ex.Message, "发送失败：" + ex.Message));
                return;
            }

            _lastLocalWire = wire;
            _lastLocalWireAt = Time.unscaledTime;
            AddLine(_selectedSteamId, FindPeerName(_selectedSteamId), text, true);
            _draft = "";
            SetStatus(UiLang.T("Sent.", "已发送。"));
            _chatScroll.y = 99999f;
        }

        private static void TryTransfer()
        {
            if (_selectedSteamId == 0UL)
            {
                SetStatus(UiLang.T("Select a player first.", "请先选择玩家。"));
                return;
            }
            float amount;
            if (!TryParseAmount(_transferDraft, out amount))
            {
                SetStatus(UiLang.T("Invalid amount.", "金额无效。"));
                return;
            }
            float funds = ReadLocalAllocation();
            if (funds + 0.001f < amount)
            {
                SetStatus(UiLang.T("Not enough funds (" + funds.ToString("0.0") + "M).",
                    "资金不足（现有 " + funds.ToString("0.0") + "M）。"));
                return;
            }
            string wire;
            if (!TryBuildTransferWire(_selectedSteamId, amount, out wire))
            {
                SetStatus(UiLang.T("Transfer request too long.", "转账请求格式错误。"));
                return;
            }
            try
            {
                if (!ChatManager.CanSend(wire, false, true))
                {
                    SetStatus(UiLang.T("Cannot send (rate limit / not in match).",
                        "无法发送（限速或未在对局中）。"));
                    return;
                }
                ChatManager.SendChatMessage(wire, true);
            }
            catch (Exception ex)
            {
                SetStatus(UiLang.T("Transfer failed: " + ex.Message, "转账失败：" + ex.Message));
                return;
            }
            SetStatus(UiLang.T("Transfer requested…", "已提交转账…"));
        }

        private static float ReadLocalAllocation()
        {
            try
            {
                Player local;
                if (GameManager.GetLocalPlayer(out local) && local != null)
                    return local.Allocation;
            }
            catch { }
            return 0f;
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
            if (amount < MinTransferM || amount > MaxTransferM)
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
            Player local = null;
            try { GameManager.GetLocalPlayer(out local); }
            catch { }
            ulong localId = 0UL;
            if (local != null)
            {
                try { localId = local.SteamID; }
                catch { }
            }

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
                        ulong id = 0UL;
                        try { id = p.SteamID; }
                        catch { }
                        if (id == 0UL || id == localId)
                            continue;
                        if (GameManager.IsLocalPlayer(p))
                            continue;
                        AddPeerUnique(id, ResolveName(p));
                    }
                }
            }
            catch { }

            if (_peers.Count == 0)
            {
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
                            ulong id = 0UL;
                            try { id = p.SteamID; }
                            catch { }
                            if (id == 0UL || id == localId)
                                continue;
                            if (GameManager.IsLocalPlayer(p))
                                continue;
                            AddPeerUnique(id, ResolveName(p));
                        }
                    }
                }
                catch { }
            }

            if (_selectedSteamId != 0UL)
            {
                bool still = false;
                for (int i = 0; i < _peers.Count; i++)
                {
                    if (_peers[i].SteamId == _selectedSteamId)
                    {
                        still = true;
                        break;
                    }
                }
                if (!still && _peers.Count > 0)
                    _selectedSteamId = _peers[0].SteamId;
            }
            else if (_peers.Count > 0)
                _selectedSteamId = _peers[0].SteamId;
        }

        private static void AddPeerUnique(ulong id, string name)
        {
            for (int i = 0; i < _peers.Count; i++)
            {
                if (_peers[i].SteamId == id)
                    return;
            }
            if (_peers.Count >= MaxPlayersCached)
                return;
            PmPeer peer = new PmPeer();
            peer.SteamId = id;
            peer.Name = string.IsNullOrEmpty(name) ? id.ToString("X") : name;
            _peers.Add(peer);
        }

        private static string FindPeerName(ulong id)
        {
            for (int i = 0; i < _peers.Count; i++)
            {
                if (_peers[i].SteamId == id)
                    return _peers[i].Name;
            }
            for (int i = _history.Count - 1; i >= 0; i--)
            {
                if (_history[i].PeerId == id && !string.IsNullOrEmpty(_history[i].PeerName))
                    return _history[i].PeerName;
            }
            return id.ToString("X");
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
            try
            {
                return p.SteamID.ToString("X");
            }
            catch { }
            return "?";
        }

        private static void AddLine(ulong peerId, string peerName, string text, bool outgoing)
        {
            PmLine line = new PmLine();
            line.PeerId = peerId;
            line.PeerName = peerName ?? "?";
            line.Text = text ?? "";
            line.Outgoing = outgoing;
            line.Time = Time.unscaledTime;
            _history.Add(line);
            while (_history.Count > MaxHistory)
                _history.RemoveAt(0);
        }

        private static List<PmLine> BuildThread(ulong peerId)
        {
            List<PmLine> list = new List<PmLine>(16);
            if (peerId == 0UL)
                return list;
            for (int i = 0; i < _history.Count; i++)
            {
                if (_history[i].PeerId == peerId)
                    list.Add(_history[i]);
            }
            return list;
        }

        internal static bool TryParseWire(string message, out ulong toSteamId, out string body)
        {
            toSteamId = 0UL;
            body = null;
            if (string.IsNullOrEmpty(message) || !message.StartsWith(WirePrefix, StringComparison.Ordinal))
                return false;
            int sep = message.IndexOf('#', WirePrefix.Length);
            if (sep <= WirePrefix.Length)
                return false;
            string hex = message.Substring(WirePrefix.Length, sep - WirePrefix.Length);
            if (!ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out toSteamId)
                || toSteamId == 0UL)
                return false;
            body = message.Substring(sep + 1);
            return body != null;
        }

        internal static bool TryBuildWire(ulong toSteamId, string body, out string wire)
        {
            wire = null;
            if (toSteamId == 0UL || string.IsNullOrEmpty(body))
                return false;
            string hex = toSteamId.ToString("X");
            wire = WirePrefix + hex + "#" + body;
            return wire.Length <= MaxWireLen;
        }

        internal static bool TryRetargetOnServer(ChatManager mgr, string message, INetworkPlayer sender)
        {
            ulong toId;
            string body;
            if (!TryParseWire(message, out toId, out body) || mgr == null)
                return false;

            Player senderPlayer = null;
            try
            {
                if (sender != null)
                    sender.TryGetPlayer(out senderPlayer);
            }
            catch { }

            Player target = FindPlayerBySteam(toId);
            if (target == null)
                return false;

            INetworkPlayer targetConn = null;
            try { targetConn = target.Owner; }
            catch { }

            try
            {
                if (targetConn != null)
                    mgr.TargetReceiveMessage(targetConn, message, senderPlayer, true);
                if (sender != null && (targetConn == null || !object.ReferenceEquals(sender, targetConn)))
                    mgr.TargetReceiveMessage(sender, message, senderPlayer, true);
            }
            catch
            {
                return false;
            }
            return true;
        }

        internal static bool TryBuildTransferWire(ulong toSteamId, float amount, out string wire)
        {
            wire = null;
            if (toSteamId == 0UL || amount < MinTransferM)
                return false;
            string amt = amount.ToString("0.0", CultureInfo.InvariantCulture);
            wire = TransferPrefix + toSteamId.ToString("X") + "#" + amt;
            return wire.Length <= MaxWireLen;
        }

        internal static bool TryParseTransferWire(string message, out ulong toSteamId, out float amount)
        {
            toSteamId = 0UL;
            amount = 0f;
            if (string.IsNullOrEmpty(message)
                || !message.StartsWith(TransferPrefix, StringComparison.Ordinal))
                return false;
            int sep = message.IndexOf('#', TransferPrefix.Length);
            if (sep <= TransferPrefix.Length)
                return false;
            string hex = message.Substring(TransferPrefix.Length, sep - TransferPrefix.Length);
            if (!ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out toSteamId)
                || toSteamId == 0UL)
                return false;
            return TryParseAmount(message.Substring(sep + 1), out amount);
        }

        /// <summary>
        /// Server/host: move Allocation from sender to target, then ack both players.
        /// Returns true if the chat Cmd should be swallowed (handled or failed with ack).
        /// </summary>
        internal static bool TryProcessTransferOnServer(ChatManager mgr, string message, INetworkPlayer sender)
        {
            if (mgr == null)
                return false;
            ulong toId;
            float amount;
            if (!TryParseTransferWire(message, out toId, out amount))
                return false;

            Player senderPlayer = null;
            try
            {
                if (sender != null)
                    sender.TryGetPlayer(out senderPlayer);
            }
            catch { }
            if (senderPlayer == null)
            {
                SendTransferFail(mgr, sender, null, "NOSENDER");
                return true;
            }

            ulong fromId = 0UL;
            try { fromId = senderPlayer.SteamID; }
            catch { }
            if (fromId == 0UL || fromId == toId)
            {
                SendTransferFail(mgr, sender, senderPlayer, "BADTARGET");
                return true;
            }

            Player target = FindPlayerBySteam(toId);
            if (target == null)
            {
                SendTransferFail(mgr, sender, senderPlayer, "NOTARGET");
                return true;
            }

            float have = 0f;
            try { have = senderPlayer.Allocation; }
            catch { }
            if (have + 0.001f < amount)
            {
                SendTransferFail(mgr, sender, senderPlayer, "NOFUNDS");
                return true;
            }

            try
            {
                senderPlayer.AddAllocation(-amount);
                target.AddAllocation(amount);
            }
            catch
            {
                SendTransferFail(mgr, sender, senderPlayer, "SERVER");
                return true;
            }

            string ok = TransferOkPrefix + fromId.ToString("X") + "#" + toId.ToString("X") + "#"
                + amount.ToString("0.0", CultureInfo.InvariantCulture);
            try
            {
                INetworkPlayer targetConn = null;
                try { targetConn = target.Owner; }
                catch { }
                if (targetConn != null)
                    mgr.TargetReceiveMessage(targetConn, ok, senderPlayer, true);
                if (sender != null)
                    mgr.TargetReceiveMessage(sender, ok, senderPlayer, true);
            }
            catch { }
            return true;
        }

        private static void SendTransferFail(ChatManager mgr, INetworkPlayer conn, Player senderPlayer, string code)
        {
            if (mgr == null || conn == null)
                return;
            string msg = TransferFailPrefix + (code ?? "FAIL");
            try { mgr.TargetReceiveMessage(conn, msg, senderPlayer, true); }
            catch { }
        }

        internal static void ReceiveTransferAck(string message, Player player)
        {
            if (string.IsNullOrEmpty(message))
                return;

            if (message.StartsWith(TransferFailPrefix, StringComparison.Ordinal))
            {
                string code = message.Substring(TransferFailPrefix.Length);
                if (string.Equals(code, "NOFUNDS", StringComparison.Ordinal))
                    SetStatus(UiLang.T("Transfer failed: not enough funds.", "转账失败：资金不足。"));
                else if (string.Equals(code, "NOTARGET", StringComparison.Ordinal))
                    SetStatus(UiLang.T("Transfer failed: player not found.", "转账失败：找不到玩家。"));
                else if (string.Equals(code, "BADTARGET", StringComparison.Ordinal))
                    SetStatus(UiLang.T("Transfer failed: invalid target.", "转账失败：目标无效。"));
                else
                    SetStatus(UiLang.T("Transfer failed (" + code + ").", "转账失败（" + code + "）。"));
                return;
            }

            if (!message.StartsWith(TransferOkPrefix, StringComparison.Ordinal))
                return;

            // #OTFOK#from#to#amount
            string rest = message.Substring(TransferOkPrefix.Length);
            string[] parts = rest.Split('#');
            if (parts == null || parts.Length < 3)
                return;
            ulong fromId;
            ulong toId;
            float amount;
            if (!ulong.TryParse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out fromId)
                || !ulong.TryParse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out toId)
                || !TryParseAmount(parts[2], out amount))
                return;

            Player local = null;
            try { GameManager.GetLocalPlayer(out local); }
            catch { }
            ulong localId = 0UL;
            if (local != null)
            {
                try { localId = local.SteamID; }
                catch { }
            }
            if (localId == 0UL)
                return;

            bool outgoing = fromId == localId;
            ulong peerId = outgoing ? toId : fromId;
            string peerName = FindPeerName(peerId);
            if (player != null && !outgoing)
                peerName = ResolveName(player);

            string note = outgoing
                ? UiLang.T("[Transfer] Sent " + amount.ToString("0.0") + "M",
                    "[转账] 已发送 " + amount.ToString("0.0") + "M")
                : UiLang.T("[Transfer] Received " + amount.ToString("0.0") + "M",
                    "[转账] 已收到 " + amount.ToString("0.0") + "M");
            AddLine(peerId, peerName, note, outgoing);
            if (outgoing)
                SetStatus(UiLang.T("Transferred " + amount.ToString("0.0") + "M to " + peerName,
                    "已转账 " + amount.ToString("0.0") + "M 给 " + peerName));
            else
            {
                SetStatus(UiLang.T("Received " + amount.ToString("0.0") + "M from " + peerName,
                    "收到来自 " + peerName + " 的 " + amount.ToString("0.0") + "M"));
                if (!_menuOpen)
                    _unread = true;
                else if (_selectedSteamId == 0UL)
                    _selectedSteamId = peerId;
            }
        }

        /// <summary>True if Cmd was fully handled (PM retarget or transfer).</summary>
        internal static bool TryHandleServerChatWire(ChatManager mgr, string message, INetworkPlayer sender)
        {
            if (string.IsNullOrEmpty(message) || mgr == null)
                return false;
            if (message.StartsWith(TransferPrefix, StringComparison.Ordinal))
                return TryProcessTransferOnServer(mgr, message, sender);
            if (message.StartsWith(WirePrefix, StringComparison.Ordinal))
                return TryRetargetOnServer(mgr, message, sender);
            return false;
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
            return null;
        }

        private static void EnsureStyles()
        {
            if (_titleStyle != null)
                return;
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            _titleStyle.normal.textColor = new Color(0.85f, 0.98f, 1f, 1f);
            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft
            };
            _labelStyle.normal.textColor = new Color(0.75f, 0.88f, 0.92f, 0.95f);
            _btnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold
            };
            _chipStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleRight
            };
            _msgStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                wordWrap = true,
                alignment = TextAnchor.UpperLeft
            };
            _peerStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold
            };
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

        private sealed class PmPeer
        {
            internal ulong SteamId;
            internal string Name;
        }

        private sealed class PmLine
        {
            internal ulong PeerId;
            internal string PeerName;
            internal string Text;
            internal bool Outgoing;
            internal float Time;
        }

        /// <summary>Server: retarget PM wire to recipient + sender only.</summary>
        [HarmonyPatch]
        private static class CmdSendPmPatch
        {
            private static bool Prepare()
            {
                return FindChatUserCode("UserCode_CmdSendChatMessage") != null;
            }

            private static MethodBase TargetMethod()
            {
                return FindChatUserCode("UserCode_CmdSendChatMessage");
            }

            [HarmonyPrefix]
            private static bool Prefix(ChatManager __instance, string message, bool allChat, INetworkPlayer sender)
            {
                if (__instance == null || string.IsNullOrEmpty(message))
                    return true;
                bool ours = message.StartsWith(WirePrefix, StringComparison.Ordinal)
                    || message.StartsWith(TransferPrefix, StringComparison.Ordinal)
                    || message.StartsWith(TransferOkPrefix, StringComparison.Ordinal)
                    || message.StartsWith(TransferFailPrefix, StringComparison.Ordinal);
                if (!ours)
                    return true;
                // Block client-forged acks; only handle on server / host.
                try
                {
                    if (!__instance.IsServer)
                        return true;
                }
                catch { }

                // Clients must not inject OK/FAIL as chat.
                if (message.StartsWith(TransferOkPrefix, StringComparison.Ordinal)
                    || message.StartsWith(TransferFailPrefix, StringComparison.Ordinal))
                    return false;

                if (TryHandleServerChatWire(__instance, message, sender))
                    return false;
                // Fall through to normal broadcast if retarget failed (target missing).
                return true;
            }
        }

        /// <summary>Client: swallow Oritasy wires from public chat and route to F5 UI.</summary>
        [HarmonyPatch]
        private static class TargetRecvPmPatch
        {
            private static bool Prepare()
            {
                return FindChatUserCode("UserCode_TargetReceiveMessage") != null;
            }

            private static MethodBase TargetMethod()
            {
                return FindChatUserCode("UserCode_TargetReceiveMessage");
            }

            [HarmonyPrefix]
            private static bool Prefix(string message, Player player, bool allChat)
            {
                if (string.IsNullOrEmpty(message))
                    return true;
                if (message.StartsWith(WirePrefix, StringComparison.Ordinal))
                {
                    ReceiveWire(message, player);
                    return false;
                }
                if (message.StartsWith(TransferOkPrefix, StringComparison.Ordinal)
                    || message.StartsWith(TransferFailPrefix, StringComparison.Ordinal))
                {
                    ReceiveTransferAck(message, player);
                    return false;
                }
                // Swallow raw transfer requests if a non-mod host broadcast them.
                if (message.StartsWith(TransferPrefix, StringComparison.Ordinal))
                    return false;
                return true;
            }
        }

        private static MethodBase FindChatUserCode(string nameContains)
        {
            try
            {
                MethodInfo[] methods = typeof(ChatManager).GetMethods(
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo m = methods[i];
                    if (m != null && m.Name != null && m.Name.IndexOf(nameContains, StringComparison.Ordinal) >= 0)
                        return m;
                }
            }
            catch { }
            return null;
        }
    }
}
