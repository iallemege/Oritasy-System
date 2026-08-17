using System;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Greenfield component-repair presentation helpers (0.0.9.66).
    /// ComponentRepair owns scan / heal reflection.
    /// </summary>
    internal static class ComponentRepairMathService
    {
        internal enum DamageKind
        {
            Detached = 0,
            HitPoints = 1,
            Engine = 2,
            Fuel = 3,
            Critical = 4,
            Wear = 5,
            Pilot = 6
        }

        internal static Color KindColor(DamageKind k)
        {
            switch (k)
            {
                case DamageKind.Detached: return new Color(1f, 0.35f, 0.3f, 0.95f);
                case DamageKind.Engine: return new Color(1f, 0.65f, 0.2f, 0.95f);
                case DamageKind.Fuel: return new Color(1f, 0.45f, 0.15f, 0.95f);
                case DamageKind.Critical: return new Color(0.95f, 0.4f, 0.85f, 0.95f);
                case DamageKind.Wear: return new Color(1f, 0.72f, 0.28f, 0.95f);
                case DamageKind.Pilot: return new Color(0.95f, 0.4f, 0.55f, 0.95f);
                default: return new Color(0.45f, 0.85f, 1f, 0.95f);
            }
        }

        internal static Color KindColor(int kind)
        {
            return KindColor((DamageKind)kind);
        }

        internal static string HpDetail(float hp, float maxHp)
        {
            if (maxHp > 1f)
                return "HP " + Mathf.RoundToInt(hp) + "/" + Mathf.RoundToInt(maxHp);
            return "HP " + hp.ToString("0.#");
        }

        internal static string PartName(string goName, string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                typeName = "Part";
            if (string.IsNullOrEmpty(goName) || goName == "null" || goName == "?")
                return typeName;
            if (string.Equals(goName, typeName, StringComparison.OrdinalIgnoreCase))
                return goName;
            return typeName + " " + goName;
        }

        internal static void NoteMaxHp(System.Collections.Generic.Dictionary<int, float> map, int id, float hp)
        {
            if (map == null || id == 0 || hp <= 0f)
                return;
            float prev;
            if (!map.TryGetValue(id, out prev) || hp > prev)
                map[id] = hp;
        }
    }
}
