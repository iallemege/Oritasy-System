using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Oritasy
{
    /// <summary>
    /// Enlarge and recolor the vanilla lower-right aircraft damage silhouette
    /// (StatusDisplay). Healthy parts stay a faint green ghost; damaged parts
    /// use vanilla yellow→red from live part HP. Does not touch flight controls.
    /// </summary>
    internal static class StatusDisplayHud
    {
        internal const float ScaleMul = 1.12f;
        /// <summary>Keep vanilla Update enabled without overflowing background alpha (vanilla uses timer*0.1).</summary>
        internal const float HoldTimer = 12f;
        /// <summary>Unused leftover; G-meter now sits on this silhouette by design.</summary>
        internal const float GMeterBottomFraction = 0.46f;

        private static readonly FieldInfo DisplayTimer =
            AccessTools.Field(typeof(StatusDisplay), "displayTimer");
        private static readonly FieldInfo StatusDisplays =
            AccessTools.Field(typeof(StatusDisplay), "statusDisplays");
        private static readonly FieldInfo AircraftBackground =
            AccessTools.Field(typeof(StatusDisplay), "aircraftBackground");
        private static readonly FieldInfo FailureIndicators =
            AccessTools.Field(typeof(StatusDisplay), "failureIndicators");
        private static readonly FieldInfo UnitPartField =
            AccessTools.Field(typeof(PartStatusDisplay), "unitPart");
        private static readonly FieldInfo DetachedFromUnit =
            AccessTools.Field(typeof(UnitPart), "detachedFromUnit");

        private static readonly HashSet<int> Scaled = new HashSet<int>();
        private static readonly HashSet<int> Outlined = new HashSet<int>();
        private static readonly Color Healthy = new Color(0.18f, 1f, 0.42f, 0.22f);
        private static readonly Color Detached = new Color(1f, 0.22f, 0.85f, 0.90f);
        private static readonly Color Backdrop = new Color(0.02f, 0.07f, 0.04f, 0.14f);

        internal static bool Enabled
        {
            get { return Plugin.EnhanceStatusDisplay == null || Plugin.EnhanceStatusDisplay.Value; }
        }

        internal static void Apply(StatusDisplay sd)
        {
            if (!Enabled || sd == null)
                return;
            try
            {
                KeepAlive(sd);
                ApplyScale(sd);
                OutlineParts(sd);
                Paint(sd);
                BoostFailureText(sd);
            }
            catch { }
        }

        internal static void Refresh(StatusDisplay sd)
        {
            if (!Enabled || sd == null)
                return;
            try
            {
                KeepAlive(sd);
                Paint(sd);
            }
            catch { }
        }

        internal static void ClampTimer(StatusDisplay sd)
        {
            if (DisplayTimer == null || sd == null)
                return;
            try
            {
                float t = (float)DisplayTimer.GetValue(sd);
                if (t > HoldTimer)
                    DisplayTimer.SetValue(sd, HoldTimer);
            }
            catch { }
        }

        private static void KeepAlive(StatusDisplay sd)
        {
            if (DisplayTimer != null)
                DisplayTimer.SetValue(sd, HoldTimer);
            sd.enabled = true;
        }

        private static void ApplyScale(StatusDisplay sd)
        {
            int id = sd.GetInstanceID();
            if (Scaled.Contains(id))
                return;
            RectTransform rt = sd.transform as RectTransform;
            if (rt == null)
                return;
            Vector3 s = rt.localScale;
            rt.localScale = new Vector3(ScaleMul, ScaleMul, s.z);
            Vector2 ap = rt.anchoredPosition;
            rt.anchoredPosition = new Vector2(ap.x - 4f, ap.y + 4f);
            Scaled.Add(id);
        }

        private static void OutlineParts(StatusDisplay sd)
        {
            Image bg = AircraftBackground != null
                ? AircraftBackground.GetValue(sd) as Image : null;
            if (bg != null)
                EnsureOutline(bg, new Color(0f, 0f, 0f, 0.40f), 1.4f);
            if (StatusDisplays == null)
                return;
            List<PartStatusDisplay> parts =
                StatusDisplays.GetValue(sd) as List<PartStatusDisplay>;
            if (parts == null)
                return;
            for (int i = 0; i < parts.Count; i++)
            {
                PartStatusDisplay psd = parts[i];
                if (psd == null || psd.partImage == null)
                    continue;
                EnsureOutline(psd.partImage, new Color(0f, 0f, 0f, 0.35f), 1.0f);
            }
        }

        private static void Paint(StatusDisplay sd)
        {
            Image bg = AircraftBackground != null
                ? AircraftBackground.GetValue(sd) as Image : null;
            if (bg != null)
                bg.color = Backdrop;

            if (StatusDisplays == null)
                return;
            List<PartStatusDisplay> parts =
                StatusDisplays.GetValue(sd) as List<PartStatusDisplay>;
            if (parts == null)
                return;
            float t = Time.unscaledTime;
            for (int i = 0; i < parts.Count; i++)
            {
                PartStatusDisplay psd = parts[i];
                if (psd == null || psd.partImage == null)
                    continue;
                psd.partImage.color = ColorForPart(psd, t);
            }
        }

        internal static Color ColorForPart(PartStatusDisplay psd)
        {
            return ColorForPart(psd, Time.unscaledTime);
        }

        private static Color ColorForPart(PartStatusDisplay psd, float time)
        {
            UnitPart part = null;
            if (UnitPartField != null)
            {
                try { part = UnitPartField.GetValue(psd) as UnitPart; }
                catch { part = null; }
            }

            bool detached = false;
            if (part != null && DetachedFromUnit != null)
            {
                try { detached = (bool)DetachedFromUnit.GetValue(part); }
                catch { detached = false; }
            }
            if (detached)
            {
                float pulse = 0.72f + 0.20f * Mathf.PingPong(time * 2.4f, 1f);
                return new Color(Detached.r, Detached.g, Detached.b, pulse);
            }

            // Same formula as vanilla PartStatusDisplay.StatusDisplay_OnDamage:
            // displayCondition = (hitPoints - redStatusThreshold) / (100 - threshold).
            float cond = psd.displayCondition;
            if (part != null)
            {
                float hp = 100f;
                bool haveHp = false;
                try
                {
                    hp = part.hitPoints;
                    haveHp = true;
                }
                catch { haveHp = false; }
                if (haveHp)
                {
                    float thresh = psd.redStatusThreshold;
                    if (thresh >= 99.9f)
                        thresh = 30f;
                    float span = 100f - thresh;
                    if (span < 0.01f)
                        span = 70f;
                    cond = (hp - thresh) / span;
                    if (cond < 0f)
                        cond = 0f;
                    if (cond > 1f)
                        cond = 1f;
                    psd.displayCondition = cond;
                }
            }
            if (cond < 0f)
                cond = 0f;
            if (cond > 1f)
                cond = 1f;

            if (cond >= 0.999f)
                return Healthy;

            // Vanilla part sprites are yellow (1,1,0): G drops toward red as HP falls,
            // alpha is 1 - condition. Healthy ghost stays green; any real hit is yellow/red.
            float g = cond * 2f;
            if (g > 1f)
                g = 1f;
            float a = 1f - cond;
            if (a < 0.32f)
                a = 0.32f;
            if (a > 0.92f)
                a = 0.92f;
            return new Color(1f, g, 0.04f, a);
        }

        private static void BoostFailureText(StatusDisplay sd)
        {
            int id = sd.GetInstanceID();
            if (Outlined.Contains(id))
                return;
            Outlined.Add(id);

            Text[] labels = sd.GetComponentsInChildren<Text>(true);
            if (labels == null)
                return;
            for (int i = 0; i < labels.Length; i++)
            {
                Text tx = labels[i];
                if (tx == null)
                    continue;
                if (tx.fontSize < 28)
                    tx.fontSize = 28;
                tx.color = new Color(1f, 0.18f, 0.12f, 0.72f);
                tx.horizontalOverflow = HorizontalWrapMode.Overflow;
                tx.verticalOverflow = VerticalWrapMode.Overflow;
                Transform tr = tx.transform;
                Vector3 ls = tr.localScale;
                tr.localScale = new Vector3(ls.x * 1.08f, ls.y * 1.08f, ls.z);
                EnsureOutline(tx.gameObject, new Color(0f, 0f, 0f, 0.45f), 1.4f);
            }

            if (FailureIndicators == null)
                return;
            List<GameObject> fails = FailureIndicators.GetValue(sd) as List<GameObject>;
            if (fails == null)
                return;
            for (int i = 0; i < fails.Count; i++)
            {
                GameObject go = fails[i];
                if (go == null)
                    continue;
                Transform tr = go.transform;
                Vector3 ls = tr.localScale;
                tr.localScale = new Vector3(ls.x * 1.2f, ls.y * 1.2f, ls.z);
            }
        }

        private static void EnsureOutline(Image img, Color color, float dist)
        {
            if (img == null)
                return;
            EnsureOutline(img.gameObject, color, dist);
        }

        private static void EnsureOutline(GameObject go, Color color, float dist)
        {
            if (go == null)
                return;
            Outline ol = go.GetComponent<Outline>();
            if (ol == null)
                ol = go.AddComponent<Outline>();
            ol.effectColor = color;
            ol.effectDistance = new Vector2(dist, -dist);
            ol.useGraphicAlpha = true;
        }
    }

    [HarmonyPatch(typeof(StatusDisplay), "Initialize")]
    internal static class Patch_StatusDisplay_Initialize
    {
        [HarmonyPostfix]
        private static void Postfix(StatusDisplay __instance)
        {
            StatusDisplayHud.Apply(__instance);
        }
    }

    [HarmonyPatch(typeof(StatusDisplay), "DisplayDamage")]
    internal static class Patch_StatusDisplay_DisplayDamage
    {
        [HarmonyPostfix]
        private static void Postfix(StatusDisplay __instance)
        {
            StatusDisplayHud.Apply(__instance);
        }
    }

    [HarmonyPatch(typeof(StatusDisplay), "Update")]
    internal static class Patch_StatusDisplay_Update
    {
        [HarmonyPrefix]
        private static void Prefix(StatusDisplay __instance)
        {
            if (!StatusDisplayHud.Enabled || __instance == null)
                return;
            StatusDisplayHud.ClampTimer(__instance);
        }

        [HarmonyPostfix]
        private static void Postfix(StatusDisplay __instance)
        {
            StatusDisplayHud.Refresh(__instance);
        }
    }

    [HarmonyPatch(typeof(PartStatusDisplay), "StatusDisplay_OnDamage")]
    internal static class Patch_PartStatus_OnDamage
    {
        [HarmonyPostfix]
        private static void Postfix(PartStatusDisplay __instance)
        {
            if (!StatusDisplayHud.Enabled || __instance == null)
                return;
            try
            {
                if (__instance.partImage != null)
                    __instance.partImage.color = StatusDisplayHud.ColorForPart(__instance);
            }
            catch { }
        }
    }

    [HarmonyPatch(typeof(PartStatusDisplay), "StatusDisplay_OnDetach")]
    internal static class Patch_PartStatus_OnDetach
    {
        [HarmonyPostfix]
        private static void Postfix(PartStatusDisplay __instance)
        {
            if (!StatusDisplayHud.Enabled || __instance == null || __instance.partImage == null)
                return;
            try
            {
                __instance.displayCondition = 0f;
                __instance.partImage.color = new Color(1f, 0.2f, 0.85f, 1f);
            }
            catch { }
        }
    }
}
