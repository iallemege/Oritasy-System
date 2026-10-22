using System;
using System.Collections.Generic;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// Live encyclopedia dump: every WeaponInfo pen grade and every unit facing armor.
    /// Open with ' while infantry, or from the hangar Weapons menu.
    /// </summary>
    internal static class ArmorCatalogService
    {
        private class WepRow
        {
            public string Name;
            public string Kind;
            public int Pen;
            public int CalMm;
            public float Pierce;
        }

        private class UnitRow
        {
            public string Name;
            public string Kind;
            public int Front, Side, Rear, Top, Bottom;
            public float Tier;
        }

        private static readonly List<WepRow> Weapons = new List<WepRow>(256);
        private static readonly List<UnitRow> Units = new List<UnitRow>(256);
        private static bool _scanned;
        private static Vector2 _scroll;
        private static int _tab;
        private static GUIStyle _head;
        private static GUIStyle _cell;
        private static bool _styles;

        internal static void EnsureScanned()
        {
            if (_scanned && Weapons.Count > 0)
                return;
            Scan();
        }

        internal static void Draw()
        {
            EnsureStyles();
            EnsureScanned();
            float w = Mathf.Min(920f, Screen.width - 40f);
            float h = Mathf.Min(560f, Screen.height - 80f);
            Rect box = new Rect((Screen.width - w) * 0.5f, 40f, w, h);
            GUI.Box(box, "");
            GUILayout.BeginArea(new Rect(box.x + 10f, box.y + 8f, box.width - 20f, box.height - 16f));
            GUILayout.BeginHorizontal();
            GUILayout.Label(UiLang.T("AP / ARMOR  0–20", "穿甲 / 装甲  0–20"), GUILayout.Width(260f));
            if (GUILayout.Button(UiLang.T("Weapons", "武器"), GUILayout.Width(100f)))
                _tab = 0;
            if (GUILayout.Button(UiLang.T("Vehicles", "载具"), GUILayout.Width(100f)))
                _tab = 1;
            if (GUILayout.Button(UiLang.T("Facings", "方向/部位"), GUILayout.Width(120f)))
                _tab = 2;
            if (GUILayout.Button(UiLang.T("Close (')", "关闭 (')"), GUILayout.Width(110f)))
                InfantryHud.CatalogOpen = false;
            GUILayout.EndHorizontal();
            _scroll = GUILayout.BeginScrollView(_scroll);
            if (_tab == 0)
                DrawWeapons();
            else if (_tab == 1)
                DrawUnits();
            else
                DrawFacings();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static void DrawWeapons()
        {
            GUILayout.Label(UiLang.T(
                "Infantry (fixed) then encyclopedia WeaponInfo. KE rifles are blast 0. Pen is catalog 0-20 (nukes = 11). Live hits roll ±1. Armor fatigues on repeated hits.",
                "先列步兵固定值，再列图鉴。步枪纯动能无爆破。表列穿甲 0-20（核弹=11）。实弹±1浮动。连续命中装甲会疲劳。"), _cell);
            Header(new string[] { "Name", "Kind", "Pen", "mm", "Pierce" });
            Row(new string[] { "M16", "KE rifle", "4", "5.56", "8" }, true);
            Row(new string[] { "AK-47", "KE rifle", "5", "7.62", "10" }, false);
            Row(new string[] { "Pistol", "KE", "2", "9", "5" }, true);
            Row(new string[] { "Shotgun", "KE", "3", "12g", "6" }, false);
            Row(new string[] { "Sniper", "KE AP", "6", "7.62", "16" }, true);
            Row(new string[] { "AT-4", "HEAT", "18", "84", "180" }, false);
            Row(new string[] { "RPG-7", "HEAT", "16", "85", "140" }, true);
            Row(new string[] { "RBS 70", "HE MANPADS", "15", "106", "110" }, false);
            for (int i = 0; i < Weapons.Count; i++)
            {
                WepRow r = Weapons[i];
                Row(new string[]
                {
                    r.Name,
                    r.Kind,
                    r.Pen.ToString(),
                    r.CalMm > 0 ? r.CalMm.ToString() : "-",
                    r.Pierce.ToString("0")
                }, (i % 2) == 0);
            }
        }

        private static void DrawUnits()
        {
            GUILayout.Label(UiLang.T(
                "Facing grades after vanilla armorTier. Fatigue drops effective plate up to 3 grades. Infantry: outer 10 over body 2. Glance/bounce still deal a damage floor. Modules: turret +2, gun -2, engine -2, tracks -4, ammo -1, fuel -3, sensors -6, command -2.",
                "方向等级含原版 armorTier。疲劳最多降 3 级有效厚度。步兵外层10甲 + 本体2甲。跳弹/擦伤仍有伤害下限。部位：炮塔+2 火炮-2 动力-2 行动-4 弹药-1 燃料-3 传感器-6 指挥-2。"), _cell);
            Header(new string[] { "Unit", "Kind", "F", "S", "R", "T", "B", "Tier" });
            Row(new string[] { "Infantry plate", "outer", "10", "10", "10", "10", "10", "-" }, true);
            Row(new string[] { "Infantry body", "inner", "2", "2", "2", "2", "2", "-" }, false);
            for (int i = 0; i < Units.Count; i++)
            {
                UnitRow r = Units[i];
                Row(new string[]
                {
                    r.Name,
                    r.Kind,
                    r.Front.ToString(),
                    r.Side.ToString(),
                    r.Rear.ToString(),
                    r.Top.ToString(),
                    r.Bottom.ToString(),
                    r.Tier.ToString("0")
                }, (i % 2) == 0);
            }
        }

        private static void DrawFacings()
        {
            GUILayout.Label(UiLang.T(
                "Base facing by class. Every shot rolls pen ±1. KE guns overmatch at pen-armor >= 1, missiles >= 2, HEAT/bombs >= 4. Overpen chains KE into an interior module. Nukes catalog pen 11.",
                "类别基础方向。每发穿甲±1。动能炮过穿门槛差1，导弹差2，破甲/炸弹差4。过穿会把剩余动能打进内侧模块。核弹表列穿深11。"), _cell);
            Header(new string[] { "Class", "Front", "Side", "Rear", "Top", "Bottom" });
            FaceRow("Infantry", ModularArmorMathService.KindInfantry);
            FaceRow("Building", ModularArmorMathService.KindBuilding);
            FaceRow("Fighter", ModularArmorMathService.KindFighter);
            FaceRow("Attack", ModularArmorMathService.KindAttack);
            FaceRow("Bomber", ModularArmorMathService.KindBomber);
            FaceRow("Helo", ModularArmorMathService.KindHelo);
            FaceRow("MBT", ModularArmorMathService.KindMbt);
            FaceRow("IFV", ModularArmorMathService.KindIfv);
            FaceRow("Soft / truck", ModularArmorMathService.KindSoft);
            FaceRow("Warship", ModularArmorMathService.KindWarship);
            FaceRow("Escort", ModularArmorMathService.KindEscort);
            FaceRow("Carrier", ModularArmorMathService.KindCarrier);
            FaceRow("Boat", ModularArmorMathService.KindBoat);
            FaceRow("Super-heavy air", ModularArmorMathService.KindSuperHeavy);
            GUILayout.Space(8f);
            Header(new string[] { "Cannon mm", "Pen" });
            Row(new string[] { "≤20", "6" }, true);
            Row(new string[] { "23", "7" }, false);
            Row(new string[] { "25", "8" }, true);
            Row(new string[] { "27", "9" }, false);
            Row(new string[] { "30–35", "10" }, true);
            Row(new string[] { "40", "12" }, false);
            Row(new string[] { "57", "16" }, true);
            Row(new string[] { "76", "17" }, false);
            Row(new string[] { "105", "18" }, true);
            Row(new string[] { "125+", "19–20" }, false);
            Header(new string[] { "MG mm", "Pen" });
            Row(new string[] { "≤8 / 7.62", "2" }, true);
            Row(new string[] { "12.7 / .50", "4" }, false);
            Row(new string[] { "14.5", "5" }, true);
        }

        private static void FaceRow(string name, int kind)
        {
            int f = ModularArmorMathService.BaseGrade(kind, ModularArmorMathService.FaceFront);
            int s = ModularArmorMathService.BaseGrade(kind, ModularArmorMathService.FaceSide);
            int r = ModularArmorMathService.BaseGrade(kind, ModularArmorMathService.FaceRear);
            int t = ModularArmorMathService.BaseGrade(kind, ModularArmorMathService.FaceTop);
            int b = ModularArmorMathService.BaseGrade(kind, ModularArmorMathService.FaceBottom);
            Row(new string[] { name, f.ToString(), s.ToString(), r.ToString(), t.ToString(), b.ToString() }, false);
        }

        internal static void Scan()
        {
            Weapons.Clear();
            Units.Clear();
            try
            {
                WeaponInfo[] infos = Resources.FindObjectsOfTypeAll<WeaponInfo>();
                if (infos != null)
                {
                    Dictionary<string, bool> seen = new Dictionary<string, bool>();
                    for (int i = 0; i < infos.Length; i++)
                    {
                        WeaponInfo w = infos[i];
                        if (w == null)
                            continue;
                        try
                        {
                            if (w.hideInDisplay)
                                continue;
                        }
                        catch { }
                        string name = w.weaponName;
                        if (string.IsNullOrEmpty(name))
                            name = w.shortName;
                        if (string.IsNullOrEmpty(name))
                            name = w.name;
                        if (string.IsNullOrEmpty(name) || seen.ContainsKey(name))
                            continue;
                        seen[name] = true;
                        WepRow row = new WepRow();
                        row.Name = name;
                        int cls = ModularArmorMathService.ClassifyKinetic(w);
                        row.Kind = ModularArmorMathService.IsNuclearWeapon(w)
                            ? "nuke"
                            : (ModularArmorMathService.IsKeNamed(w)
                                ? "KE replica"
                                : WepKindLabel(cls));
                        row.Pierce = w.pierceDamage;
                        row.CalMm = ModularArmorMathService.ParseCaliberMm(ModularArmorMathService.WeaponBlob(w));
                        row.Pen = ModularArmorMathService.PenGradeForWeapon(w, w.pierceDamage);
                        Weapons.Add(row);
                    }
                }
            }
            catch { }

            Encyclopedia enc = null;
            try { enc = Encyclopedia.i; }
            catch { enc = null; }
            if (enc != null)
            {
                AddDefs(enc.vehicles, "veh");
                AddDefs(enc.ships, "ship");
                AddDefs(enc.aircraft, "air");
            }
            _scanned = true;
        }

        private static void AddDefs(System.Collections.IList list, string hint)
        {
            if (list == null)
                return;
            for (int i = 0; i < list.Count; i++)
            {
                UnitDefinition def = list[i] as UnitDefinition;
                if (def == null)
                    continue;
                string name = def.unitName;
                if (string.IsNullOrEmpty(name))
                    name = def.code;
                if (string.IsNullOrEmpty(name))
                    name = def.jsonKey;
                if (string.IsNullOrEmpty(name))
                    continue;
                int kind = KindFromDef(def, hint);
                int tier = 0;
                try { tier = Mathf.RoundToInt(def.armorTier); }
                catch { }
                UnitRow row = new UnitRow();
                row.Name = name;
                row.Kind = KindLabel(kind);
                row.Front = ModularArmorMathService.ArmorGrade(kind, ModularArmorMathService.FaceFront, tier);
                row.Side = ModularArmorMathService.ArmorGrade(kind, ModularArmorMathService.FaceSide, tier);
                row.Rear = ModularArmorMathService.ArmorGrade(kind, ModularArmorMathService.FaceRear, tier);
                row.Top = ModularArmorMathService.ArmorGrade(kind, ModularArmorMathService.FaceTop, tier);
                row.Bottom = ModularArmorMathService.ArmorGrade(kind, ModularArmorMathService.FaceBottom, tier);
                row.Tier = def.armorTier;
                Units.Add(row);
            }
        }

        private static int KindFromDef(UnitDefinition def, string hint)
        {
            VehicleDefinition vd = def as VehicleDefinition;
            if (vd != null)
            {
                try
                {
                    VehicleType t = vd.vehicleType;
                    if (t == VehicleType.MBT)
                        return ModularArmorMathService.KindMbt;
                    if (t == VehicleType.AFV)
                        return ModularArmorMathService.KindIfv;
                    if (t == VehicleType.TRUCK || t == VehicleType.UGV || t == VehicleType.LCV
                        || t == VehicleType.RDR)
                        return ModularArmorMathService.KindSoft;
                    return ModularArmorMathService.KindIfv;
                }
                catch { }
            }
            string blob = (def.unitName != null ? def.unitName : "") + " "
                + (def.code != null ? def.code : "") + " "
                + (def.jsonKey != null ? def.jsonKey : "") + " " + hint;
            if (hint == "ship")
            {
                if (ModularArmorMathService.NameHas(blob, "carrier"))
                    return ModularArmorMathService.KindCarrier;
                if (ModularArmorMathService.NameHas(blob, "frigate") || ModularArmorMathService.NameHas(blob, "destroyer"))
                    return ModularArmorMathService.KindEscort;
                return ModularArmorMathService.KindWarship;
            }
            if (hint == "air")
            {
                if (ModularArmorMathService.NameHas(blob, "heli") || ModularArmorMathService.NameHas(blob, "uh-")
                    || ModularArmorMathService.NameHas(blob, "sah"))
                    return ModularArmorMathService.KindHelo;
                if (ModularArmorMathService.NameHas(blob, "bomber") || ModularArmorMathService.NameHas(blob, "sfb"))
                    return ModularArmorMathService.KindBomber;
                return ModularArmorMathService.KindFighter;
            }
            return ModularArmorMathService.KindSoft;
        }

        private static string WepKindLabel(int cls)
        {
            if (cls == ModularArmorMathService.WepMg) return "MG";
            if (cls == ModularArmorMathService.WepCannon) return "cannon";
            if (cls == ModularArmorMathService.WepRocket) return "rocket";
            if (cls == ModularArmorMathService.WepMissile) return "missile";
            if (cls == ModularArmorMathService.WepBomb) return "bomb";
            if (cls == ModularArmorMathService.WepEnergy) return "energy";
            return "other";
        }

        private static string KindLabel(int kind)
        {
            if (kind == ModularArmorMathService.KindMbt) return "MBT";
            if (kind == ModularArmorMathService.KindIfv) return "IFV";
            if (kind == ModularArmorMathService.KindSoft) return "soft";
            if (kind == ModularArmorMathService.KindWarship) return "warship";
            if (kind == ModularArmorMathService.KindEscort) return "escort";
            if (kind == ModularArmorMathService.KindCarrier) return "carrier";
            if (kind == ModularArmorMathService.KindBoat) return "boat";
            if (kind == ModularArmorMathService.KindFighter) return "fighter";
            if (kind == ModularArmorMathService.KindAttack) return "attack";
            if (kind == ModularArmorMathService.KindBomber) return "bomber";
            if (kind == ModularArmorMathService.KindHelo) return "helo";
            if (kind == ModularArmorMathService.KindSuperHeavy) return "super";
            if (kind == ModularArmorMathService.KindInfantry) return "infantry";
            if (kind == ModularArmorMathService.KindBuilding) return "building";
            return "?";
        }

        private static void Header(string[] cols)
        {
            GUILayout.BeginHorizontal();
            for (int i = 0; i < cols.Length; i++)
                GUILayout.Label(cols[i], _head, GUILayout.Width(i == 0 ? 220f : 70f));
            GUILayout.EndHorizontal();
        }

        private static void Row(string[] cols, bool dim)
        {
            GUILayout.BeginHorizontal();
            Color prev = GUI.color;
            if (dim)
                GUI.color = new Color(0.85f, 0.85f, 0.85f);
            for (int i = 0; i < cols.Length; i++)
                GUILayout.Label(cols[i], _cell, GUILayout.Width(i == 0 ? 220f : 70f));
            GUI.color = prev;
            GUILayout.EndHorizontal();
        }

        private static void EnsureStyles()
        {
            if (_styles)
                return;
            _styles = true;
            _head = new GUIStyle(GUI.skin.label);
            _head.fontStyle = FontStyle.Bold;
            _head.fontSize = 12;
            _cell = new GUIStyle(GUI.skin.label);
            _cell.fontSize = 11;
            _cell.wordWrap = true;
        }
    }
}
