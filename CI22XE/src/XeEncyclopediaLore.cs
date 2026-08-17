namespace Oritasy
{
    /// <summary>
    /// Per-airframe encyclopedia copy in the Veyrn Aeronautics voice used by ACM-119 / TGM-85:
    /// northern OEM, no catalogs, broker sales to both BDF and PALA, distinct airframe stories.
    /// </summary>
    internal static class XeEncyclopediaLore
    {
        internal const string XeNoteEn =
            "XE rebuild: performance changes came with a redesignation.";
        internal const string XeNoteZh =
            "XE版本的性能修改的同时也对名称进行了修改。";

        internal static string Wrap(string bodyEn)
        {
            return XeNoteEn + " " + bodyEn;
        }

        internal static string WrapZh(string bodyZh)
        {
            return XeNoteZh + bodyZh;
        }

        // --- Bodies (without XE note prefix) ---

        internal const string Ci22En =
            "CI-22XE Super Cricket is a Veyrn Aeronautics counter-insurgency remanufacture of the "
            + "old Cricket bush frame: reinforced gear, short-field props, and a hardpoint map "
            + "rewired for mixed guns and light munitions. Through the 2060s Veyrn sold unmarked "
            + "lots as \"agricultural trainers\" via northern brokers; identical crates reached both "
            + "the Boscali Defense Force and the Primeva Armed Liberation Alliance without end-user "
            + "clauses. In 2072 it remains the cheap STOL workhorse neither side will admit buying.";

        internal const string Ci22Zh =
            "CI-22XE 超级蟋蟀是 Veyrn Aeronautics 对旧款蟋蟀丛林机体的反叛乱翻修型：强化起落架、"
            + "短场螺旋桨，并重布轻武器与弹药挂点。2060 年代 Veyrn 经北方掮客以“农用教练机”名义"
            + "出售无标识批次；相同货柜在无最终用户条款下同时抵达博斯卡利国防军（BDF）与普里梅瓦"
            + "武装解放联盟（PALA）。至 2072 年，它仍是双方不愿承认却离不开的廉价短场主力。";

        internal const string Ta30En =
            "T/A-30XE Super Compass is Veyrn's volume jet trainer and light-attack kit—the cash "
            + "engine that funded later missile lines after TGM-85. Dual-role airframes leave the "
            + "factory with ballistic sights and empty pylons; brokers finish them as BDF syllabus "
            + "jets or PALA armed conversions from the same crate. No national insignia, no catalogs: "
            + "only a northern workshop stamp and a ledger that lists both customers as \"civil aviation.\"";

        internal const string Ta30Zh =
            "T/A-30XE 超罗盘是 Veyrn 的批量喷气教练与轻攻改装套件——继 TGM-85 之后养活后续导弹线的"
            + "现金牛。出厂双用途机体只带弹道瞄准与空挂架；掮客再从同一货柜配成 BDF 教学机或 PALA "
            + "武装改装。无国徽、无目录，只有北方工坊钢印，账本把双方都写成“民用航空”。";

        internal const string Vt7En =
            "VT-7XE Airspace Vagrant keeps the three-post ducted STOVL layout that made the type "
            + "a naval trainer, then a budget attack jet. Veyrn's late-century XE pass raised thrust "
            + "and radar margins while leaving the silhouette familiar to carrier decks. BDF bought "
            + "deck batches openly as trainers; PALA took \"surplus instructional airframes\" through "
            + "the same Veyrn brokers who refuse diplomatic inquiries—theater-neutral stock that "
            + "still anchors both sides' vertical ops in 2072.";

        internal const string Vt7Zh =
            "VT-7XE 空域流浪者保留三支柱涵道 STOVL 布局，从海军教练机成长为廉价攻击机。Veyrn "
            + "世纪末的 XE 改进提高推力与雷达余量，甲板轮廓却仍显眼。BDF 以教练机名义公开采购甲板"
            + "批次；PALA 则经同一批拒答外交问询的 Veyrn 掮客吃下“教学剩余机体”——至 2072 年仍是"
            + "双方垂直起降作战的中立库存骨干。";

        internal const string Uh90En =
            "UH-90XE King Cobra began as a PALA-era compound lift concept; Veyrn bought the "
            + "tooling, swapped the civility for gunship stations, and renamed the export face. "
            + "Coaxial rotors and pusher fans still sprint then stop for a vertical landing in under "
            + "half a minute. Through the 2050s–70s identical unmarked helicopters reached BDF "
            + "expeditionary units and PALA air-assault cadres alike—brokered as utility lifts, "
            + "armed after the paint dried.";

        internal const string Uh90Zh =
            "UH-90XE 眼镜王蛇源自 PALA 时代的复合升力构想；Veyrn 买下工装，把民用气质换成武装站，"
            + "并改掉出口面孔。共轴旋翼与推进风扇仍能疾驰后在半分钟内垂直停稳。2050–70 年代，相同"
            + "无标识直升机经掮客同时进入 BDF 远征部队与 PALA 空突干部——账上写多用途运输，漆干后再"
            + "上武器。";

        internal const string Sah46En =
            "SAH-46XE Gulfstream is Veyrn's stealth attack helicopter line: traditional gunship "
            + "geometry wrapped in late-century signature treatments for terrain-masked pop-ups. "
            + "After years of deniable night trials over Ignus-class archipelagos, brokers moved "
            + "matched lots to BDF special aviation and PALA deep-strike cells as \"executive "
            + "utility conversions.\" Neither flag appears on the airframe; Veyrn answers no "
            + "inquiries about who paid for the quiet rotors.";

        internal const string Sah46Zh =
            "SAH-46XE 湾流是 Veyrn 的隐身攻击直升机线：传统武装布局外包世纪末特征处理，专打地形"
            + "遮蔽后的跃升突袭。经 Ignus 类群岛上空多年可否认夜试后，掮客把对等批次作为“行政多用途"
            + "改装”送进 BDF 特航与 PALA 纵深打击单元。机身无旗帜；Veyrn 从不回答是谁为安静旋翼付的钱。";

        internal const string Nota10En =
            "NOTA-10XE Super Warthog is what Veyrn did after the A-19 Brawler line stalled: buy "
            + "the armored CAS design, rename it, and harden propfan endurance for convoy and "
            + "coastal kills. Heavy cockpit armor and isolated engines survive the low passes both "
            + "BDF and PALA demand. Brokers sold the first XE crates in the late 2060s as "
            + "\"maritime patrol trainers\"; by 2072 the Super Warthog name is the only honest "
            + "part of the paperwork.";

        internal const string Nota10Zh =
            "NOTA-10XE 超疣猪是 Veyrn 在 A-19 搏击者产线停滞后的操作：买下装甲近距支援设计、改名，"
            + "并强化螺扇续航以屠杀车队与近海目标。厚重座舱装甲与隔离发动机扛得住 BDF 与 PALA 都爱的"
            + "超低空通场。2060 年代末掮客把首批 XE 货柜写成“海上巡逻教练机”；到 2072 年，文件里唯"
            + "一诚实的大概只剩超疣猪这个名字。";

        internal const string Fs12En =
            "FS-12XE 'Special' Liberator is Veyrn's agility-first fighter rebuild of the Revoker "
            + "family—internal bays, brutal energy retention, and export batches jokingly stamped "
            + "'Special' for clients who needed air superiority without a treaty trail. Through "
            + "the 2060s the same northern crates armed BDF quick-reaction wings and PALA "
            + "liberation squadrons. Cheap compared with stealth peers, hard to attribute after "
            + "the merge, and never listed in a Veyrn catalog.";

        internal const string Fs12Zh =
            "FS-12XE ‘特殊’解放者是 Veyrn 对撤销者族系的机动优先翻修——内置弹舱、凶狠能量保持，并以"
            + "戏谑的‘特殊’钢印供应需要制空却不要条约痕迹的客户。整个 2060 年代，同一批北方货柜武装了"
            + "BDF 快速反应联队与 PALA 解放中队。比隐身同侪便宜，交战后难溯源，也从不出现在 Veyrn 目录里。";

        internal const string Fs20En =
            "FS-20XE Mad Vortex is Veyrn's compact STOVL stealth multirole for small decks and "
            + "hidden pads. Light it stays fast and quiet; heavy loads trade away the magic—pilots "
            + "on both sides learn that the hard way. Premium unmarked lots reached BDF escort "
            + "carriers and PALA island strips through the same brokers who move ACM-119 buses: "
            + "no end-user clause, no national markings, only a northern OEM that publishes nothing.";

        internal const string Fs20Zh =
            "FS-20XE 狂涡是 Veyrn 面向小甲板与隐蔽坪的紧凑 STOVL 隐身多用途机。轻载时又快又静；重载"
            + "则吞噬那些优势——双方飞行员都用血换过这个教训。高价无标识批次经与 ACM-119 同一批掮客"
            + "进入 BDF 护航航母与 PALA 岛礁跑道：无最终用户条款、无国徽，只有从不出版目录的北方制造商。";

        internal const string Vl49En =
            "VL-49XE Bird-Eating Spider is Veyrn's heavy vertical lifter turned gunship: first "
            + "crate in as a logistics airframe, second pass adds troop doors and heavy stations. "
            + "BDF used early lots to seize forward pads; PALA mirrored the trick with broker "
            + "\"civilian heavy lift\" papers. The XE package keeps the devouring payload and "
            + "renames the terror openly—still sold to both flags without asking which island burns.";

        internal const string Vl49Zh =
            "VL-49XE 噬鸟蛛是 Veyrn 的重型垂直升降机改武装型：第一票以后勤机体入场，第二票再开舱门与"
            + "重火力站。BDF 用早期批次抢占前进坪场；PALA 用掮客的“民用重吊”文件如法炮制。XE 包保留"
            + "吞噬级载荷并公开改名——仍不问烧毁哪座岛，把货卖给双方旗帜。";

        internal const string Kr67En =
            "KR-67XE Fallen Angel is Veyrn's large twin-engine stealth fighter after the Ifrit "
            + "prestige line was quietly rebranded. Internal main bays plus side heat-seeker "
            + "cells keep the radar quiet while the airframe still hauls for air superiority or "
            + "strike. Mid-century \"angel\" marketing died in a scandal; brokers kept shipping "
            + "identical airframes to BDF carrier air wings and PALA elite regiments under the "
            + "Fallen Angel stencil—Veyrn's northern shops still deny the paperwork exists.";

        internal const string Kr67Zh =
            "KR-67XE 堕天使是 Veyrn 在伊弗利特声望线被悄悄改牌后的大型双发隐身战机。主内置舱加侧向"
            + "热寻弹舱，雷达保持安静，机体仍能扛空优或打击。世纪中叶的“天使”营销毁于丑闻；掮客继续把"
            + "相同机体以堕天使钢印送进 BDF 舰载联队与 PALA 精锐团——Veyrn 北方工坊仍否认存在这些文件。";

        internal const string Ew25En =
            "EW-25XE Medusa is Veyrn's STOVL electronic-warfare support jet: triangulation, "
            + "jamming, SEAD cues, and a high-energy laser that can kill inbound missiles. Early "
            + "covers called it a civil survey VTOL; by the 2060s both BDF and PALA were flying "
            + "unmarked Medusas bought through Veyrn brokers who treat diplomatic cables as noise. "
            + "One airframe can tilt a sector—hence the export premium and the blank invoice.";

        internal const string Ew25Zh =
            "EW-25XE 美杜莎是 Veyrn 的 STOVL 电子战支援机：三角定位、干扰、SEAD 提示，以及可击落来袭"
            + "导弹的高能激光。早期掩护写成民用测绘垂起；至 2060 年代，BDF 与 PALA 都在飞经 Veyrn 掮客"
            + "购入的无标识美杜莎——他们把外交电报当噪音。单机足以倾斜一个扇区，所以出口溢价，发票空白。";

        internal const string SfbEn =
            "SFB-81XE Darkreach is Veyrn's blended-wing strategic bomber for low penetration and "
            + "standoff dump alike, with four large bays cleared for conventional or nuclear loads. "
            + "It is the airframe counterpart to the quiet missile trade that began with TGM-85 and "
            + "later rode on ACM-119: the most sensitive crates move at night, stamped for neither "
            + "BDF nor PALA, yet both inventories show Darkreach silhouettes by 2072. Veyrn still "
            + "publishes no catalog and answers no inquiries.";

        internal const string SfbZh =
            "SFB-81XE 暗域是 Veyrn 的翼身融合战略轰炸机，兼顾低空突防与远程投放，四个大型弹舱可装常规"
            + "或核载荷。它是自 TGM-85 起、后由 ACM-119 续命的安静导弹贸易在机体上的对应物：最敏感的货柜"
            + "夜里启运，钢印既不写 BDF 也不写 PALA，但到 2072 年双方库存里都有暗域轮廓。Veyrn 仍不出版"
            + "目录，也不回答任何问询。";

        internal const string Ab4En =
            "AB-4XE Hummingbird is Veyrn's tiny high-risk penetrator—Alkyon bones under a new "
            + "export name—built to slip dense air defenses and sting naval yards. Pilots joke that "
            + "the XE pass made the bird angrier; brokers treat it as a courier airframe for "
            + "deniable raids. Matched lots reached PALA strike cells first, then BDF special "
            + "detachments returned the favor with purchases from the same northern middlemen.";

        internal const string Ab4Zh =
            "AB-4XE 蜂鸟是 Veyrn 的袖珍高风险突防机——Alkyon 骨架套新出口名——专为钻严密防空、叮咬海军"
            + "船坞而生。飞行员笑称 XE 让这只鸟更凶；掮客则把它当可否认突袭的信使机体。对等批次先到 PALA "
            + "打击单元，随后 BDF 特遣队又向同一批北方中间人回购礼尚往来。";
    }
}
