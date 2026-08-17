using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// HUD marks: horizontal line under the friendly with most air kills,
    /// and above the friendly with fewest. Ranking updates are throttled.
    /// </summary>
    internal static class FriendlyKillHud
    {
        private static ConfigEntry<bool> _enabled;
        private static ConfigEntry<float> _refreshHz;
        private static ConfigEntry<float> _lineWidth;
        private static ConfigEntry<float> _lineThickness;

        private static readonly Dictionary<int, int> AirKills = new Dictionary<int, int>();
        private static readonly List<Candidate> Scratch = new List<Candidate>(32);

        private static float _nextRankAt;
        private static Aircraft _bestAc;
        private static Aircraft _worstAc;
        private static int _bestKills;
        private static int _worstKills;
        private static Camera _cam;
        private static float _nextCamAt;

        private struct Candidate
        {
            public Aircraft Ac;
            public int Score;
        }

        internal static void Bind(ConfigFile config)
        {
            if (config == null)
                return;
            _enabled = config.Bind("FriendlyKillHud", "Enabled", true,
                "Line under top-kill ally icon, line above lowest-kill ally icon.");
            _refreshHz = config.Bind("FriendlyKillHud", "RefreshHz", 1f,
                "How often to re-rank friendlies (Hz). Lower = cheaper. Default 1.");
            _lineWidth = config.Bind("FriendlyKillHud", "LineWidth", 36f,
                "Horizontal line width in pixels.");
            _lineThickness = config.Bind("FriendlyKillHud", "LineThickness", 3f,
                "Horizontal line thickness in pixels.");
        }

        internal static void NoteAirKill(Player killer)
        {
            if (killer == null)
                return;
            int id = killer.GetInstanceID();
            int n;
            if (AirKills.TryGetValue(id, out n))
                AirKills[id] = n + 1;
            else
                AirKills[id] = 1;
            // Force re-rank soon
            _nextRankAt = 0f;
        }

        internal static void Tick()
        {
            if (_enabled == null || !_enabled.Value)
                return;

            float hz = _refreshHz != null
                ? FriendlyKillRankMathService.ClampRefreshHz(_refreshHz.Value)
                : 1f;
            // No tracked kills yet — idle cheaply (avoid full-unit scans every tick)
            if (AirKills.Count == 0)
            {
                _bestAc = null;
                _worstAc = null;
                _nextRankAt = Time.unscaledTime + 2f;
                return;
            }
            if (Time.unscaledTime < _nextRankAt)
                return;
            _nextRankAt = Time.unscaledTime + (1f / hz);

            Aircraft local;
            if (!GameManager.GetLocalAircraft(out local) || local == null)
            {
                _bestAc = null;
                _worstAc = null;
                return;
            }

            RefreshRanking();
        }

        internal static void DrawGui()
        {
            if (_enabled == null || !_enabled.Value)
                return;
            if (_bestAc == null && _worstAc == null)
                return;
            // EventType.Repaint only — avoid layout/layoutEvent cost
            if (Event.current != null && Event.current.type != EventType.Repaint)
                return;

            Camera cam = GetCamera();
            if (cam == null)
                return;

            float w = _lineWidth != null ? _lineWidth.Value : 36f;
            float th = _lineThickness != null ? _lineThickness.Value : 3f;

            if (_bestAc != null && Plugin.IsUnitAlive(_bestAc))
                DrawLineAtUnit(_bestAc, cam, w, th, true, new Color(1f, 0.85f, 0.2f, 0.95f));
            if (_worstAc != null && Plugin.IsUnitAlive(_worstAc)
                && !object.ReferenceEquals(_worstAc, _bestAc))
                DrawLineAtUnit(_worstAc, cam, w, th, false, new Color(0.75f, 0.75f, 0.8f, 0.9f));
        }

        private static void DrawLineAtUnit(Aircraft ac, Camera cam, float width, float thickness, bool below, Color color)
        {
            Vector3 world;
            try { world = ac.transform.position; }
            catch { return; }

            Vector3 sp = cam.WorldToScreenPoint(world);
            if (sp.z <= 0.05f)
                return;

            // Unity screen Y is bottom-up; IMGUI is top-down
            float x = GuiScale.FromScreenX(sp.x);
            float y = GuiScale.FromScreenYFlipped(sp.y);
            // Offset from icon center (~unit marker size)
            float yOff = below ? 18f : -22f;
            float left = x - width * 0.5f;
            float top = y + yOff;

            if (left + width < 0f || left > GuiScale.Width || top + thickness < 0f || top > GuiScale.Height)
                return;

            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(left, top, width, thickness), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        private static void RefreshRanking()
        {
            _bestAc = null;
            _worstAc = null;
            _bestKills = int.MinValue;
            _worstKills = int.MaxValue;

            FactionHQ localHq = null;
            try { GameManager.GetLocalHQ(out localHq); }
            catch { }
            if (localHq == null)
                return;

            List<Unit> all = UnitRegistry.allUnits;
            if (all == null || all.Count == 0)
                return;

            Scratch.Clear();
            for (int i = 0; i < all.Count; i++)
            {
                Aircraft ac = all[i] as Aircraft;
                if (ac == null || !Plugin.IsUnitAlive(ac))
                    continue;
                if (!object.ReferenceEquals(Plugin.GetHq(ac), localHq))
                    continue;

                int score;
                if (!TryScoreForAircraft(ac, out score))
                    continue;
                Candidate c;
                c.Ac = ac;
                c.Score = score;
                Scratch.Add(c);
            }

            if (Scratch.Count < 2)
            {
                // Still mark the single ally if only one exists with kills tracked
                if (Scratch.Count == 1)
                {
                    _bestAc = Scratch[0].Ac;
                    _bestKills = Scratch[0].Score;
                }
                return;
            }

            int bestIdx, worstIdx, bestS, worstS;
            if (!FriendlyKillRankMathService.TryRank(
                    Scratch.Count, i => Scratch[i].Score,
                    out bestIdx, out worstIdx, out bestS, out worstS))
                return;

            _bestAc = Scratch[bestIdx].Ac;
            _worstAc = Scratch[worstIdx].Ac;
            _bestKills = bestS;
            _worstKills = worstS;
        }

        /// <summary>Only pilots with tracked air kills — skips PlayerScore/skill probes.</summary>
        private static bool TryScoreForAircraft(Aircraft ac, out int score)
        {
            score = 0;
            if (ac == null)
                return false;
            Player p = null;
            try { p = ac.Player; }
            catch { }
            if (p == null)
                return false;
            int id = p.GetInstanceID();
            return AirKills.TryGetValue(id, out score);
        }

        private static Camera GetCamera()
        {
            if (_cam != null && Time.unscaledTime < _nextCamAt)
                return _cam;
            _nextCamAt = Time.unscaledTime + 2f;
            try { _cam = Camera.main; }
            catch { _cam = null; }
            return _cam;
        }

        internal static void ClearSession()
        {
            AirKills.Clear();
            _bestAc = null;
            _worstAc = null;
        }
    }

    /// <summary>Track every player's air kills for friendly HUD ranking (not local-only).</summary>
    [HarmonyPatch(typeof(FactionHQ), "ReportKillAction")]
    internal static class Patch_FactionHQ_ReportKillAction_FriendlyHud
    {
        [HarmonyPostfix]
        private static void Postfix(Player player, Unit target)
        {
            if (player == null || !(target is Aircraft))
                return;
            FriendlyKillHud.NoteAirKill(player);
        }
    }
}
