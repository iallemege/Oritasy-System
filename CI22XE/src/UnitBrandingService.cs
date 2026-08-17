using System;
using UnityEngine;

namespace Oritasy
{
    /// <summary>
    /// XE rename + Veyrn Aeronautics encyclopedia lore (ACM-119 / TGM-85 voice).
    /// A-19 → NOTA-10XE.
    /// </summary>
    internal static class UnitBrandingService
    {
        internal struct XeBrand
        {
            public string Code;
            public string NameEn;
            public string NameZh;
            public string DescEn;
            public string DescZh;
        }

        internal static void TryRenameDefinition(AircraftDefinition def)
        {
            if (!AircraftIdentity.IsXeDefinition(def))
                return;

            XeBrand brand;
            if (!TryResolveBrand(def, out brand))
            {
                // Fallback: append XE only once
                if (!Plugin.Touched.AddDef(def.GetInstanceID()))
                    return;
                def.unitName = AircraftIdentity.AppendXe(def.unitName);
                def.code = AircraftIdentity.AppendXe(def.code);
                if (def.aircraftParameters != null)
                    def.aircraftParameters.aircraftName = def.unitName;
            }
            else
            {
                Plugin.Touched.AddDef(def.GetInstanceID());
                ApplyBrandFields(def, brand);
            }

            if (Encyclopedia.Lookup != null && !string.IsNullOrEmpty(def.jsonKey))
                Encyclopedia.Lookup[def.jsonKey] = def;

            GameZhLocalizer.NoteBrandedAircraft(def);

            if (Plugin.DebugLog != null && Plugin.DebugLog.Value)
                Plugin.Log.LogInfo(Plugin.PackName + " rename: " + def.jsonKey + " -> " + def.unitName + " / " + def.code);
        }

        /// <summary>Write code + localized display name + Veyrn lore onto the definition.</summary>
        internal static void ApplyBrandFields(AircraftDefinition def, XeBrand brand)
        {
            if (def == null)
                return;
            def.code = brand.Code;
            bool zh = UiLang.IsChinese;
            string display = zh ? brand.NameZh : brand.NameEn;
            def.unitName = display;
            if (def.aircraftParameters != null)
                def.aircraftParameters.aircraftName = display;
            if (!string.IsNullOrEmpty(brand.DescEn))
                def.description = zh ? brand.DescZh : brand.DescEn;
        }

        /// <summary>Force XE brand onto def (encyclopedia open / language toggle).</summary>
        internal static bool TryApplyBrand(AircraftDefinition def)
        {
            if (def == null || !AircraftIdentity.IsXeDefinition(def))
                return false;
            XeBrand brand;
            if (!TryResolveBrand(def, out brand))
                return false;
            ApplyBrandFields(def, brand);
            GameZhLocalizer.NoteBrandedAircraft(def);
            return true;
        }

        /// <summary>Re-apply localized Veyrn lore after language toggle.</summary>
        internal static void RefreshPackBlurb(AircraftDefinition def)
        {
            TryApplyBrand(def);
        }

        internal static bool TryResolveBrand(AircraftDefinition def, out XeBrand brand)
        {
            brand = default(XeBrand);
            if (def == null)
                return false;
            // Match against ALL identity fields — jsonKey alone is often a short stem
            // that misses nickname markers (e.g. unitName "T/A-30 Compass").
            string key = ((def.jsonKey ?? string.Empty) + " "
                + (def.code ?? string.Empty) + " "
                + (def.unitName ?? string.Empty) + " "
                + (def.bogeyName ?? string.Empty) + " "
                + (def.name ?? string.Empty)).Trim();

            if (AircraftIdentity.IsCoinDefinition(def) || AircraftIdentity.IsCi22(key))
            {
                brand = B("CI-22XE", "CI-22XE Super Cricket", "CI-22XE 超级蟋蟀",
                    XeEncyclopediaLore.Ci22En, XeEncyclopediaLore.Ci22Zh);
                return true;
            }
            if (AircraftIdentity.IsTa30(key))
            {
                brand = B("T/A-30XE", "T/A-30XE Super Compass", "T/A-30XE 超罗盘",
                    XeEncyclopediaLore.Ta30En, XeEncyclopediaLore.Ta30Zh);
                return true;
            }
            if (AircraftIdentity.IsVt7(key))
            {
                brand = B("VT-7XE", "VT-7XE Airspace Vagrant", "VT-7XE 空域流浪者",
                    XeEncyclopediaLore.Vt7En, XeEncyclopediaLore.Vt7Zh);
                return true;
            }
            if (AircraftIdentity.IsUh90(key))
            {
                brand = B("UH-90XE", "UH-90XE King Cobra", "UH-90XE 眼镜王蛇",
                    XeEncyclopediaLore.Uh90En, XeEncyclopediaLore.Uh90Zh);
                return true;
            }
            if (IsSah46(key))
            {
                brand = B("SAH-46XE", "SAH-46XE Gulfstream", "SAH-46XE 湾流",
                    XeEncyclopediaLore.Sah46En, XeEncyclopediaLore.Sah46Zh);
                return true;
            }
            if (AircraftIdentity.IsA19(key) || Contains(key, "NOTA-10", "NOTA10", "Brawler", "Warthog"))
            {
                brand = B("NOTA-10XE", "NOTA-10XE Super Warthog", "NOTA-10XE 超疣猪",
                    XeEncyclopediaLore.Nota10En, XeEncyclopediaLore.Nota10Zh);
                return true;
            }
            if (AircraftIdentity.IsFs12(key))
            {
                brand = B("FS-12XE", "FS-12XE 'Special' Liberator", "FS-12XE '特殊'解放者",
                    XeEncyclopediaLore.Fs12En, XeEncyclopediaLore.Fs12Zh);
                return true;
            }
            if (AircraftIdentity.IsFs20(key))
            {
                brand = B("FS-20XE", "FS-20XE Mad Vortex", "FS-20XE 狂涡",
                    XeEncyclopediaLore.Fs20En, XeEncyclopediaLore.Fs20Zh);
                return true;
            }
            if (Contains(key, "VL-49", "VL49", "Tarantula", "Bird-Eating"))
            {
                brand = B("VL-49XE", "VL-49XE Bird-Eating Spider", "VL-49XE 噬鸟蛛",
                    XeEncyclopediaLore.Vl49En, XeEncyclopediaLore.Vl49Zh);
                return true;
            }
            if (AircraftIdentity.IsKr67(key))
            {
                brand = B("KR-67XE", "KR-67XE Fallen Angel", "KR-67XE 堕天使",
                    XeEncyclopediaLore.Kr67En, XeEncyclopediaLore.Kr67Zh);
                return true;
            }
            if (AircraftIdentity.IsEw25(key))
            {
                brand = B("EW-25XE", "EW-25XE Medusa", "EW-25XE 美杜莎",
                    XeEncyclopediaLore.Ew25En, XeEncyclopediaLore.Ew25Zh);
                return true;
            }
            if (AircraftIdentity.IsSfb(key))
            {
                brand = B("SFB-81XE", "SFB-81XE Darkreach", "SFB-81XE 暗域",
                    XeEncyclopediaLore.SfbEn, XeEncyclopediaLore.SfbZh);
                return true;
            }
            if (AircraftIdentity.IsAb4(key))
            {
                brand = B("AB-4XE", "AB-4XE Hummingbird", "AB-4XE 蜂鸟",
                    XeEncyclopediaLore.Ab4En, XeEncyclopediaLore.Ab4Zh);
                return true;
            }
            return false;
        }

        /// <summary>Chinese display name + lore for GameZh dictionary seeding.</summary>
        internal static void SeedZhNames(Action<string, string> add)
        {
            if (add == null)
                return;
            SeedOne(add, "CI-22XE Super Cricket", "CI-22XE 超级蟋蟀");
            SeedOne(add, "CI-22 Cricket", "CI-22XE 超级蟋蟀");
            SeedOne(add, "CI-22XE", "CI-22XE 超级蟋蟀");
            SeedOne(add, "T/A-30XE Super Compass", "T/A-30XE 超罗盘");
            SeedOne(add, "T/A-30 Compass", "T/A-30XE 超罗盘");
            SeedOne(add, "VT-7XE Airspace Vagrant", "VT-7XE 空域流浪者");
            SeedOne(add, "VT-7 Vagrant", "VT-7XE 空域流浪者");
            SeedOne(add, "UH-90XE King Cobra", "UH-90XE 眼镜王蛇");
            SeedOne(add, "UH-90 Ibis", "UH-90XE 眼镜王蛇");
            SeedOne(add, "SAH-46XE Gulfstream", "SAH-46XE 湾流");
            SeedOne(add, "SAH-46 Chicane", "SAH-46XE 湾流");
            SeedOne(add, "NOTA-10XE Super Warthog", "NOTA-10XE 超疣猪");
            SeedOne(add, "A-19 Brawler", "NOTA-10XE 超疣猪");
            SeedOne(add, "A-19Brawler", "NOTA-10XE 超疣猪");
            SeedOne(add, "FS-12XE 'Special' Liberator", "FS-12XE '特殊'解放者");
            SeedOne(add, "FS-12 Revoker", "FS-12XE '特殊'解放者");
            SeedOne(add, "FS-20XE Mad Vortex", "FS-20XE 狂涡");
            SeedOne(add, "FS-20 Vortex", "FS-20XE 狂涡");
            SeedOne(add, "VL-49XE Bird-Eating Spider", "VL-49XE 噬鸟蛛");
            SeedOne(add, "VL-49 Tarantula", "VL-49XE 噬鸟蛛");
            SeedOne(add, "KR-67XE Fallen Angel", "KR-67XE 堕天使");
            SeedOne(add, "KR-67 Ifrit", "KR-67XE 堕天使");
            SeedOne(add, "EW-25XE Medusa", "EW-25XE 美杜莎");
            SeedOne(add, "EW-25 Medusa", "EW-25XE 美杜莎");
            SeedOne(add, "SFB-81XE Darkreach", "SFB-81XE 暗域");
            SeedOne(add, "SFB-81 Darkreach", "SFB-81XE 暗域");
            SeedOne(add, "AB-4XE Hummingbird", "AB-4XE 蜂鸟");
            SeedOne(add, "Alkyon AB-4", "AB-4XE 蜂鸟");
            SeedOne(add, "AB-4", "AB-4XE 蜂鸟");

            // Full encyclopedia lore EN → ZH (TMP / scan fallback)
            AddLore(add, XeEncyclopediaLore.Ci22En, XeEncyclopediaLore.Ci22Zh);
            AddLore(add, XeEncyclopediaLore.Ta30En, XeEncyclopediaLore.Ta30Zh);
            AddLore(add, XeEncyclopediaLore.Vt7En, XeEncyclopediaLore.Vt7Zh);
            AddLore(add, XeEncyclopediaLore.Uh90En, XeEncyclopediaLore.Uh90Zh);
            AddLore(add, XeEncyclopediaLore.Sah46En, XeEncyclopediaLore.Sah46Zh);
            AddLore(add, XeEncyclopediaLore.Nota10En, XeEncyclopediaLore.Nota10Zh);
            AddLore(add, XeEncyclopediaLore.Fs12En, XeEncyclopediaLore.Fs12Zh);
            AddLore(add, XeEncyclopediaLore.Fs20En, XeEncyclopediaLore.Fs20Zh);
            AddLore(add, XeEncyclopediaLore.Vl49En, XeEncyclopediaLore.Vl49Zh);
            AddLore(add, XeEncyclopediaLore.Kr67En, XeEncyclopediaLore.Kr67Zh);
            AddLore(add, XeEncyclopediaLore.Ew25En, XeEncyclopediaLore.Ew25Zh);
            AddLore(add, XeEncyclopediaLore.SfbEn, XeEncyclopediaLore.SfbZh);
            AddLore(add, XeEncyclopediaLore.Ab4En, XeEncyclopediaLore.Ab4Zh);
        }

        private static void AddLore(Action<string, string> add, string enBody, string zhBody)
        {
            string en = XeEncyclopediaLore.Wrap(enBody);
            string zh = XeEncyclopediaLore.WrapZh(zhBody);
            add(en, zh);
            add(enBody, zhBody);
        }

        private static void SeedOne(Action<string, string> add, string en, string zh)
        {
            add(en, zh);
            if (en.IndexOf("XE", StringComparison.OrdinalIgnoreCase) < 0)
                add(AircraftIdentity.AppendXe(en), zh);
        }

        private static XeBrand B(string code, string en, string zh, string descEnBody, string descZhBody)
        {
            XeBrand b;
            b.Code = code;
            b.NameEn = en;
            b.NameZh = zh;
            b.DescEn = XeEncyclopediaLore.Wrap(descEnBody);
            b.DescZh = XeEncyclopediaLore.WrapZh(descZhBody);
            return b;
        }

        private static bool IsSah46(string key)
        {
            return Contains(key, "SAH-46", "SAH46", "Chicane", "Gulfstream");
        }

        private static bool Contains(string hay, params string[] needles)
        {
            return AircraftIdentity.ContainsAny(hay, needles);
        }
    }
}
