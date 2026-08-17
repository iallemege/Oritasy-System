using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Per-family schematic cells (0–1 in the diagram). Different airframes
    /// place intake / core / nozzle / props / lift fans in different spots.
    /// </summary>
    internal static class AirframeWearSchematic
    {
        internal static Color HealthColor(float health)
        {
            float h = Mathf.Clamp01(health);
            if (h >= 0.55f)
                return Color.Lerp(new Color(0.95f, 0.82f, 0.15f), new Color(0.2f, 0.85f, 0.28f), (h - 0.55f) / 0.45f);
            if (h >= 0.25f)
                return Color.Lerp(new Color(0.95f, 0.35f, 0.12f), new Color(0.95f, 0.82f, 0.15f), (h - 0.25f) / 0.3f);
            return Color.Lerp(new Color(0.45f, 0.06f, 0.06f), new Color(0.95f, 0.35f, 0.12f), h / 0.25f);
        }

        internal static bool TryBankBox(int bank, int banks, AirframeWearService.Family family,
            out float x, out float y, out float w, out float h)
        {
            BankBox(banks, bank, family, out x, out y, out w, out h);
            return true;
        }

        internal static bool TryBankHeader(int bank, int banks, AirframeWearService.Family family,
            out float x, out float y, out float w, out float h)
        {
            float ox, oy, sx, sy;
            BankBox(banks, bank, family, out ox, out oy, out sx, out sy);
            x = ox;
            y = Mathf.Max(0.005f, oy - 0.04f);
            w = sx;
            h = 0.045f;
            return true;
        }

        internal static bool TryCell(AirframeWearService.Part p,
            AirframeWearService.Family family, int banks,
            out float x, out float y, out float w, out float h)
        {
            x = 0f;
            y = 0f;
            w = 0.1f;
            h = 0.1f;
            string key = Suffix(p.Id);
            if (p.Bank < 0)
                return SharedCell(key, family, banks, out x, out y, out w, out h);

            float ox, oy, sx, sy;
            BankBox(banks, p.Bank, family, out ox, out oy, out sx, out sy);
            float lx, ly, lw, lh;
            if (!LocalCell(key, family, p.Prop, out lx, out ly, out lw, out lh))
                return false;
            x = ox + lx * sx;
            y = oy + ly * sy;
            w = lw * sx;
            h = lh * sy;
            return true;
        }

        private static string Suffix(string id)
        {
            if (string.IsNullOrEmpty(id))
                return "";
            int d = id.LastIndexOf('.');
            if (d < 0 || d >= id.Length - 1)
                return id;
            return id.Substring(d + 1);
        }

        private static void BankBox(int banks, int bank, AirframeWearService.Family family,
            out float ox, out float oy, out float sx, out float sy)
        {
            if (banks <= 1)
            {
                ox = 0.06f;
                oy = 0.10f;
                sx = 0.88f;
                sy = 0.78f;
                return;
            }
            if (banks == 2)
            {
                ox = bank == 0 ? 0.03f : 0.51f;
                oy = 0.12f;
                sx = 0.46f;
                sy = 0.76f;
                return;
            }

            int lo = banks / 2;
            if (lo < 1)
                lo = 1;
            int slot = bank;
            bool hi = false;
            if (bank >= lo)
            {
                hi = true;
                slot = bank - lo;
            }
            int cols = lo;
            if (cols > 4)
                cols = 4;
            if (slot < 0)
                slot = 0;
            if (slot >= cols)
                slot = cols - 1;
            float gap = 0.02f;
            float x0 = 0.02f;
            float span = 0.96f;
            if (family == AirframeWearService.Family.Stovl)
            {
                x0 = 0.36f;
                span = 0.62f;
            }
            sx = (span - gap * (cols - 1)) / cols;
            ox = x0 + slot * (sx + gap);
            oy = hi ? 0.56f : 0.06f;
            sy = 0.34f;
        }

        private static bool LocalCell(string key, AirframeWearService.Family family, bool prop,
            out float x, out float y, out float w, out float h)
        {
            x = 0.4f;
            y = 0.4f;
            w = 0.18f;
            h = 0.18f;
            if (family == AirframeWearService.Family.Prop || prop)
                return PropLocal(key, out x, out y, out w, out h);
            if (family == AirframeWearService.Family.Helo)
                return HeloLocal(key, out x, out y, out w, out h);
            if (family == AirframeWearService.Family.Tilt)
                return TiltLocal(key, out x, out y, out w, out h);
            if (family == AirframeWearService.Family.Vtol)
                return VtolLocal(key, out x, out y, out w, out h);
            if (family == AirframeWearService.Family.Stovl)
                return StovlLocal(key, out x, out y, out w, out h);
            return JetLocal(key, family == AirframeWearService.Family.JetAb, out x, out y, out w, out h);
        }

        private static bool JetLocal(string key, bool ab, out float x, out float y, out float w, out float h)
        {
            // Left = intake, right = nozzle (airflow).
            if (key == "intake")
                return Cell(0.01f, 0.28f, 0.13f, 0.34f, out x, out y, out w, out h);
            if (key == "compressor")
                return Cell(0.15f, 0.22f, 0.14f, 0.42f, out x, out y, out w, out h);
            if (key == "combustor")
                return Cell(0.30f, 0.18f, 0.14f, 0.46f, out x, out y, out w, out h);
            if (key == "core")
                return Cell(0.30f, 0.18f, 0.22f, 0.46f, out x, out y, out w, out h);
            if (key == "turbine")
                return Cell(ab ? 0.45f : 0.53f, 0.22f, 0.14f, 0.42f, out x, out y, out w, out h);
            if (key == "ab")
                return Cell(0.60f, 0.16f, 0.16f, 0.50f, out x, out y, out w, out h);
            if (key == "nozzle")
                return Cell(0.77f, 0.24f, 0.21f, 0.38f, out x, out y, out w, out h);
            if (key == "inject")
                return Cell(0.01f, 0.72f, 0.16f, 0.22f, out x, out y, out w, out h);
            if (key == "oil")
                return Cell(0.18f, 0.72f, 0.30f, 0.22f, out x, out y, out w, out h);
            if (key == "mounts")
                return Cell(0.52f, 0.72f, 0.30f, 0.22f, out x, out y, out w, out h);
            return Cell(0.40f, 0.40f, 0.16f, 0.16f, out x, out y, out w, out h);
        }

        private static bool PropLocal(string key, out float x, out float y, out float w, out float h)
        {
            // Left = propeller disc, right = engine + accessories.
            if (key == "spin")
                return Cell(0.01f, 0.36f, 0.08f, 0.20f, out x, out y, out w, out h);
            if (key == "blades")
                return Cell(0.09f, 0.10f, 0.14f, 0.62f, out x, out y, out w, out h);
            if (key == "hub")
                return Cell(0.24f, 0.32f, 0.09f, 0.22f, out x, out y, out w, out h);
            if (key == "gov")
                return Cell(0.23f, 0.58f, 0.14f, 0.16f, out x, out y, out w, out h);
            if (key == "crank")
                return Cell(0.40f, 0.26f, 0.20f, 0.32f, out x, out y, out w, out h);
            if (key == "carb")
                return Cell(0.40f, 0.06f, 0.16f, 0.18f, out x, out y, out w, out h);
            if (key == "magneto")
                return Cell(0.57f, 0.06f, 0.16f, 0.18f, out x, out y, out w, out h);
            if (key == "turbo")
                return Cell(0.74f, 0.06f, 0.24f, 0.22f, out x, out y, out w, out h);
            if (key == "waste")
                return Cell(0.74f, 0.30f, 0.24f, 0.16f, out x, out y, out w, out h);
            if (key == "exhaust")
                return Cell(0.74f, 0.48f, 0.24f, 0.16f, out x, out y, out w, out h);
            if (key == "oil")
                return Cell(0.40f, 0.60f, 0.16f, 0.16f, out x, out y, out w, out h);
            if (key == "inter")
                return Cell(0.57f, 0.60f, 0.16f, 0.16f, out x, out y, out w, out h);
            if (key == "radiator")
                return Cell(0.40f, 0.78f, 0.33f, 0.16f, out x, out y, out w, out h);
            if (key == "mounts")
                return Cell(0.74f, 0.66f, 0.24f, 0.28f, out x, out y, out w, out h);
            if (key == "pfan")
                return Cell(0.09f, 0.10f, 0.16f, 0.62f, out x, out y, out w, out h);
            if (key == "gear")
                return Cell(0.40f, 0.26f, 0.20f, 0.32f, out x, out y, out w, out h);
            if (key == "core")
                return Cell(0.62f, 0.26f, 0.20f, 0.32f, out x, out y, out w, out h);
            if (key == "inject")
                return Cell(0.40f, 0.06f, 0.16f, 0.18f, out x, out y, out w, out h);
            return Cell(0.50f, 0.40f, 0.16f, 0.16f, out x, out y, out w, out h);
        }

        private static bool HeloLocal(string key, out float x, out float y, out float w, out float h)
        {
            if (key == "shaft")
                return Cell(0.04f, 0.40f, 0.24f, 0.22f, out x, out y, out w, out h);
            if (key == "inject")
                return Cell(0.04f, 0.22f, 0.24f, 0.16f, out x, out y, out w, out h);
            if (key == "oil")
                return Cell(0.04f, 0.66f, 0.24f, 0.20f, out x, out y, out w, out h);
            return Cell(0.10f, 0.40f, 0.20f, 0.20f, out x, out y, out w, out h);
        }

        private static bool VtolLocal(string key, out float x, out float y, out float w, out float h)
        {
            if (key == "fan")
                return Cell(0.12f, 0.08f, 0.76f, 0.52f, out x, out y, out w, out h);
            if (key == "duct")
                return Cell(0.06f, 0.64f, 0.42f, 0.28f, out x, out y, out w, out h);
            if (key == "drive")
                return Cell(0.52f, 0.64f, 0.42f, 0.28f, out x, out y, out w, out h);
            if (key == "lift")
                return Cell(0.01f, 0.02f, 0.98f, 0.14f, out x, out y, out w, out h);
            return JetLocal(key, true, out x, out y, out w, out h);
        }

        private static bool TiltLocal(string key, out float x, out float y, out float w, out float h)
        {
            if (key == "rotor")
                return Cell(0.08f, 0.06f, 0.84f, 0.20f, out x, out y, out w, out h);
            if (key == "shaft")
                return Cell(0.08f, 0.30f, 0.40f, 0.28f, out x, out y, out w, out h);
            if (key == "tilt")
                return Cell(0.52f, 0.30f, 0.40f, 0.28f, out x, out y, out w, out h);
            if (key == "inject")
                return Cell(0.08f, 0.64f, 0.40f, 0.18f, out x, out y, out w, out h);
            if (key == "oil")
                return Cell(0.52f, 0.64f, 0.40f, 0.18f, out x, out y, out w, out h);
            return Cell(0.30f, 0.40f, 0.40f, 0.20f, out x, out y, out w, out h);
        }

        private static bool StovlLocal(string key, out float x, out float y, out float w, out float h)
        {
            // Cruise engine on the right; lift kit is shared (left).
            if (key == "intake")
                return Cell(0.40f, 0.22f, 0.12f, 0.34f, out x, out y, out w, out h);
            if (key == "core")
                return Cell(0.53f, 0.16f, 0.14f, 0.44f, out x, out y, out w, out h);
            if (key == "turbine")
                return Cell(0.68f, 0.20f, 0.12f, 0.38f, out x, out y, out w, out h);
            if (key == "vector")
                return Cell(0.81f, 0.24f, 0.16f, 0.36f, out x, out y, out w, out h);
            if (key == "oil")
                return Cell(0.50f, 0.70f, 0.22f, 0.20f, out x, out y, out w, out h);
            if (key == "inject")
                return Cell(0.26f, 0.70f, 0.22f, 0.20f, out x, out y, out w, out h);
            if (key == "mounts")
                return Cell(0.74f, 0.70f, 0.22f, 0.20f, out x, out y, out w, out h);
            return Cell(0.55f, 0.30f, 0.16f, 0.16f, out x, out y, out w, out h);
        }

        private static bool SharedCell(string key, AirframeWearService.Family family, int banks,
            out float x, out float y, out float w, out float h)
        {
            if (key == "apu")
            {
                if (family == AirframeWearService.Family.Helo)
                    return Cell(0.38f, 0.90f, 0.24f, 0.08f, out x, out y, out w, out h);
                if (family == AirframeWearService.Family.Tilt)
                    return Cell(0.02f, 0.90f, 0.28f, 0.08f, out x, out y, out w, out h);
                if (family == AirframeWearService.Family.Stovl)
                    return Cell(0.04f, 0.86f, 0.30f, 0.12f, out x, out y, out w, out h);
                if (family == AirframeWearService.Family.Vtol)
                    return Cell(0.38f, 0.90f, 0.24f, 0.08f, out x, out y, out w, out h);
                if (banks >= 4)
                    return Cell(0.38f, 0.42f, 0.24f, 0.12f, out x, out y, out w, out h);
                return Cell(0.38f, 0.90f, 0.24f, 0.08f, out x, out y, out w, out h);
            }
            if (family == AirframeWearService.Family.Helo)
            {
                if (key == "mast")
                    return Cell(0.02f, 0.41f, 0.22f, 0.14f, out x, out y, out w, out h);
                if (key == "swash")
                    return Cell(0.26f, 0.41f, 0.22f, 0.14f, out x, out y, out w, out h);
                if (key == "mgb")
                    return Cell(0.50f, 0.41f, 0.24f, 0.14f, out x, out y, out w, out h);
                if (key == "tgb")
                    return Cell(0.76f, 0.41f, 0.22f, 0.14f, out x, out y, out w, out h);
            }
            if (family == AirframeWearService.Family.Vtol)
            {
                if (key == "mix")
                    return Cell(0.38f, 0.42f, 0.24f, 0.14f, out x, out y, out w, out h);
                if (key == "mounts")
                    return Cell(0.32f, 0.88f, 0.36f, 0.08f, out x, out y, out w, out h);
            }
            if (family == AirframeWearService.Family.Tilt)
            {
                if (key == "combine")
                    return Cell(0.32f, 0.46f, 0.36f, 0.08f, out x, out y, out w, out h);
                if (key == "mounts")
                    return Cell(0.32f, 0.90f, 0.36f, 0.08f, out x, out y, out w, out h);
            }
            if (family == AirframeWearService.Family.Stovl)
            {
                if (key == "doors")
                    return Cell(0.04f, 0.08f, 0.30f, 0.14f, out x, out y, out w, out h);
                if (key == "lift")
                    return Cell(0.04f, 0.26f, 0.30f, 0.36f, out x, out y, out w, out h);
                if (key == "bleed")
                    return Cell(0.04f, 0.66f, 0.30f, 0.18f, out x, out y, out w, out h);
            }
            return Cell(0.40f, 0.86f, 0.20f, 0.10f, out x, out y, out w, out h);
        }

        private static bool Cell(float x0, float y0, float w0, float h0,
            out float x, out float y, out float w, out float h)
        {
            x = x0;
            y = y0;
            w = w0;
            h = h0;
            return true;
        }

        internal static void DrawSilhouette(Rect area, AirframeWearService.Family family, int banks)
        {
            Color prev = GUI.color;
            GUI.color = new Color(0.16f, 0.20f, 0.24f, 0.55f);
            if (family == AirframeWearService.Family.Helo)
            {
                Fill(area, 0.28f, 0.28f, 0.44f, 0.42f);
                Fill(area, 0.62f, 0.44f, 0.32f, 0.10f);
            }
            else if (family == AirframeWearService.Family.Vtol
                || family == AirframeWearService.Family.Tilt)
            {
                Fill(area, 0.30f, 0.30f, 0.40f, 0.40f);
            }
            else if (family == AirframeWearService.Family.Prop)
            {
                Fill(area, 0.34f, 0.22f, 0.62f, 0.52f);
                Fill(area, 0.08f, 0.18f, 0.20f, 0.54f);
            }
            else if (family == AirframeWearService.Family.Stovl)
            {
                Fill(area, 0.36f, 0.28f, 0.58f, 0.32f);
                Fill(area, 0.06f, 0.24f, 0.26f, 0.40f);
            }
            else if (family == AirframeWearService.Family.Bomber || banks >= 4)
            {
                Fill(area, 0.38f, 0.36f, 0.24f, 0.28f);
                Fill(area, 0.04f, 0.42f, 0.92f, 0.10f);
            }
            else
            {
                Fill(area, 0.12f, 0.34f, 0.76f, 0.24f);
            }
            GUI.color = prev;
        }

        private static void Fill(Rect area, float nx, float ny, float nw, float nh)
        {
            GUI.DrawTexture(new Rect(
                area.x + nx * area.width,
                area.y + ny * area.height,
                nw * area.width,
                nh * area.height), Texture2D.whiteTexture);
        }
    }
}
