using System;
using System.Collections.Generic;
using UnityEngine;

namespace WeXon
{
    /// <summary>
    /// Per-match sortie path buffer + F1 map preview (uses LevelInfo MapImage / TerrainColorMap).
    /// </summary>
    internal static class FlightTrackMap
    {
        internal sealed class Sortie
        {
            internal long Id;
            internal string UnitName = "";
            internal float DurationSec;
            internal int Score;
            internal string Grade = "";
            internal FlightAnalysis.AnalysisResult Analysis;
            internal readonly List<Vector2> PathXz = new List<Vector2>(256);
        }

        private static readonly List<Sortie> MatchSorties = new List<Sortie>(8);
        private static List<Vector2> _livePath;
        private static int _viewIndex = -1; // -1 = live / latest
        private static Texture _mapTex;
        private static float _mapHalf;
        private static float _mapCacheAt = -999f;
        private static Texture2D _lineTex;
        private static Texture2D _dotTex;

        internal static int MatchCount
        {
            get { return MatchSorties.Count; }
        }

        internal static void ClearMatch()
        {
            MatchSorties.Clear();
            _livePath = null;
            _viewIndex = -1;
            _mapTex = null;
            _mapHalf = 0f;
            _mapCacheAt = -999f;
        }

        internal static void BeginLivePath()
        {
            _livePath = new List<Vector2>(512);
            _viewIndex = -1;
        }

        /// <summary>
        /// Sample map path in global XZ (not floating-origin scene local).
        /// Pass GlobalPosition.x / .z from Unit.GlobalPosition().
        /// </summary>
        internal static void SampleLive(float globalX, float globalZ)
        {
            if (_livePath == null)
                return;
            if (_livePath.Count > 0)
            {
                Vector2 last = _livePath[_livePath.Count - 1];
                float dx = globalX - last.x;
                float dz = globalZ - last.y;
                if (dx * dx + dz * dz < 2500f) // <50 m
                    return;
            }
            if (_livePath.Count >= 2500)
                return;
            _livePath.Add(new Vector2(globalX, globalZ));
        }

        internal static void SampleLive(Vector3 worldOrGlobalXz)
        {
            SampleLive(worldOrGlobalXz.x, worldOrGlobalXz.z);
        }

        internal static void DiscardLivePath()
        {
            _livePath = null;
            _viewIndex = MatchSorties.Count > 0 ? MatchSorties.Count - 1 : -1;
        }

        internal static void CommitSortie(FlightAnalysis.AnalysisResult analysis, long sortieId)
        {
            Sortie s = new Sortie();
            s.Id = sortieId;
            s.Analysis = analysis;
            if (analysis != null)
            {
                s.UnitName = analysis.UnitName ?? "";
                s.DurationSec = analysis.DurationSec;
                s.Score = analysis.Score;
                s.Grade = analysis.Grade ?? "";
            }
            if (_livePath != null && _livePath.Count > 0)
                s.PathXz.AddRange(_livePath);
            MatchSorties.Add(s);
            _livePath = null;
            _viewIndex = MatchSorties.Count - 1;
        }

        internal static void DrawMatchUi(GUIStyle section, GUIStyle label, GUIStyle btn, bool zh)
        {
            GUILayout.Label(zh ? "本局飞行记录" : "MATCH FLIGHTS", section);
            if (MatchSorties.Count == 0 && (_livePath == null || _livePath.Count == 0))
            {
                GUILayout.Label(zh
                    ? "上机后自动记录轨迹；离机后写入本局列表。"
                    : "Boarding starts track; leaving commits this match list.",
                    label);
                return;
            }

            GUILayout.BeginHorizontal();
            if (_livePath != null && _livePath.Count > 0)
            {
                bool liveOn = _viewIndex < 0;
                if (GUILayout.Toggle(liveOn, zh ? "进行中" : "LIVE", btn, GUILayout.Height(24f)))
                    _viewIndex = -1;
            }
            for (int i = 0; i < MatchSorties.Count; i++)
            {
                Sortie s = MatchSorties[i];
                string tag = "#" + (i + 1).ToString()
                    + " " + (string.IsNullOrEmpty(s.Grade) ? "-" : s.Grade)
                    + " " + s.Score;
                bool on = _viewIndex == i;
                if (GUILayout.Toggle(on, tag, btn, GUILayout.Height(24f), GUILayout.MaxWidth(110f)))
                    _viewIndex = i;
            }
            GUILayout.EndHorizontal();

            List<Vector2> path = ResolveViewPath();
            GUILayout.Space(4f);
            GUILayout.Label(zh ? "飞行轨迹" : "FLIGHT PATH", section);
            Rect r = GUILayoutUtility.GetRect(280f, 220f, GUILayout.ExpandWidth(true));
            DrawMapWithPath(r, path);
            if (path != null)
                GUILayout.Label((zh ? "航点 " : "Points ") + path.Count.ToString(), label);
        }

        internal static FlightAnalysis.AnalysisResult ResolveViewAnalysis(FlightAnalysis.AnalysisResult fallback)
        {
            if (_viewIndex >= 0 && _viewIndex < MatchSorties.Count)
                return MatchSorties[_viewIndex].Analysis ?? fallback;
            return fallback;
        }

        private static List<Vector2> ResolveViewPath()
        {
            if (_viewIndex < 0)
                return _livePath;
            if (_viewIndex < MatchSorties.Count)
                return MatchSorties[_viewIndex].PathXz;
            return _livePath;
        }

        private static void EnsureLineTextures()
        {
            if (_lineTex == null)
            {
                _lineTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _lineTex.SetPixel(0, 0, Color.white);
                _lineTex.Apply(false, true);
            }
            if (_dotTex == null)
            {
                _dotTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _dotTex.SetPixel(0, 0, Color.white);
                _dotTex.Apply(false, true);
            }
        }

        private static void RefreshMapCache()
        {
            float now = Time.unscaledTime;
            if (now - _mapCacheAt < 5f && _mapHalf > 100f)
                return;
            _mapCacheAt = now;
            _mapTex = null;
            _mapHalf = FlightTrackMapMathService.DefaultMapHalfM;
            try
            {
                LevelInfo li = NetworkSceneSingleton<LevelInfo>.i;
                if (li != null)
                {
                    float settingsMax = 0f;
                    MapSettings ms = li.LoadedMapSettings;
                    if (ms != null)
                    {
                        if (ms.MapSize.x > 500f || ms.MapSize.y > 500f)
                            settingsMax = Mathf.Max(ms.MapSize.x, ms.MapSize.y);
                        if (ms.MapImage != null && ms.MapImage.texture != null)
                            _mapTex = ms.MapImage.texture;
                        else if (ms.TerrainColorMap != null)
                            _mapTex = ms.TerrainColorMap;
                    }
                    _mapHalf = FlightTrackMapMathService.ResolveMapHalf(li.mapSize, settingsMax);
                }
            }
            catch { }

            if (_mapTex == null)
            {
                // Avoid DynamicMap.UI Image.sprite (needs UnityEngine.UI in WeXon-only builds).
            }
        }

        private static Vector2 WorldToGui(Rect r, float half, Vector2 xz)
        {
            return FlightTrackMapMathService.WorldToGui(r, half, xz);
        }

        private static void DrawMapWithPath(Rect r, List<Vector2> path)
        {
            EnsureLineTextures();
            RefreshMapCache();
            Color prev = GUI.color;
            GUI.color = new Color(0.12f, 0.14f, 0.16f, 1f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
            // Square content rect: isotropic world↔UV (wide GUI used to squash X vs Z).
            Rect mapR = FlightTrackMapMathService.SquareContentRect(r);
            if (_mapTex != null)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.92f);
                // StretchToFill (not ScaleAndCrop): map edges = ±mapHalf; crop misaligned path.
                GUI.DrawTexture(mapR, _mapTex, ScaleMode.StretchToFill);
                GUI.color = Color.white;
            }

            if (path == null || path.Count < 2 || _mapHalf < 100f)
            {
                GUI.color = prev;
                return;
            }

            float half = _mapHalf;
            GUI.color = new Color(0.35f, 0.95f, 0.55f, 0.95f);
            Vector2 a = WorldToGui(mapR, half, path[0]);
            for (int i = 1; i < path.Count; i++)
            {
                Vector2 b = WorldToGui(mapR, half, path[i]);
                DrawLine(a, b, 2f);
                a = b;
            }
            GUI.color = new Color(1f, 0.85f, 0.25f, 1f);
            Vector2 start = WorldToGui(mapR, half, path[0]);
            GUI.DrawTexture(new Rect(start.x - 3f, start.y - 3f, 6f, 6f), _dotTex);
            GUI.color = new Color(1f, 0.35f, 0.35f, 1f);
            Vector2 end = WorldToGui(mapR, half, path[path.Count - 1]);
            GUI.DrawTexture(new Rect(end.x - 3f, end.y - 3f, 6f, 6f), _dotTex);
            GUI.color = prev;
        }

        private static void DrawLine(Vector2 a, Vector2 b, float width)
        {
            GuiScale.DrawLine(a, b, width, _lineTex);
        }
    }
}
