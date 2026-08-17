using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Oritasy
{
    /// <summary>
    /// Chinese for Oritasy IMGUI (UiLang) and one-shot encyclopedia display fields.
    /// Vanilla Unity Text/TMP is not intercepted — no live set_text Harmony.
    /// </summary>
    internal static class GameZhLocalizer
    {
        private static bool _bound;
        private static bool _dictReady;
        private static bool _lastZh;
        private static bool _sceneHooked;
        private static float _nextScanAt;
        /// <summary>0 idle, 1 wait then encyclopedia, 2 wait then UI scan.</summary>
        private static int _deferPhase;
        private static float _deferAt;
        private static int _encNotifyFrame = -1;
        private static readonly Dictionary<string, string> Map = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<int, string> OriginalById = new Dictionary<int, string>();
        private static readonly HashSet<int> Touched = new HashSet<int>();
        private static readonly List<PrefixRule> PrefixRules = new List<PrefixRule>();
        private static readonly Dictionary<int, EncSnapshot> EncSnapshots = new Dictionary<int, EncSnapshot>();
        // Soft TMP
        private static Type _tmpType;
        private static PropertyInfo _tmpTextProp;
        private static bool _tmpResolved;

        private struct PrefixRule
        {
            public string EnPrefix;
            public string ZhPrefix;
        }

        private struct EncSnapshot
        {
            public string UnitName;
            public string Code;
            public string Description;
            public string BogeyName;
            public string WeaponName;
            public string ShortName;
        }

        internal static void Bind()
        {
            if (_bound)
                return;
            _bound = true;
            EnsureDict();
            HookSceneLoaded();
            TryActivateExternalZhPacks(UiLang.IsChinese);
            _lastZh = UiLang.IsChinese;
            if (_lastZh)
                ScheduleDeferredZh(0.4f);
        }

        internal static void PatchHarmony(Harmony harmony)
        {
            // Live Text/TMP set_text intercept removed — it hitch every UI update.
            // Encyclopedia names still translate once (deferred). Oritasy IMGUI uses UiLang.
        }

        internal static void OnLanguageChanged()
        {
            try { Plugin.RefreshEncyclopediaAircraft(); }
            catch { }
            bool zh = UiLang.IsChinese;
            _lastZh = zh;
            TryActivateExternalZhPacks(zh);
            Touched.Clear();
            _nextScanAt = 0f;
            if (zh)
                ChineseTmpFontService.EnsureReady();
            if (!zh)
            {
                RestoreAllKnown();
                ApplyEncyclopediaDefs(false);
            }
            else
                ScheduleDeferredZh(0.15f, true);
        }

        internal static void Tick()
        {
            if (JoinMenuFactionFix.JoinMenuOpen())
                return;
            if (!_bound)
                Bind();

            bool zh = UiLang.IsChinese;
            if (zh != _lastZh)
                OnLanguageChanged();

            PumpDeferredZh(Time.unscaledTime);
        }

        /// <summary>
        /// Spread encyclopedia + UI.Text FindObjectsOfType off the scene-load hitch frame.
        /// </summary>
        private static void ScheduleDeferredZh(float delaySec)
        {
            ScheduleDeferredZh(delaySec, false);
        }

        private static void ScheduleDeferredZh(float delaySec, bool force)
        {
            if (!UiLang.IsChinese)
                return;
            if (!force && _deferPhase > 0)
                return;
            _deferPhase = 1;
            _deferAt = Time.unscaledTime + Mathf.Max(0.12f, delaySec);
            _nextScanAt = _deferAt + 8f;
        }

        private static void PumpDeferredZh(float now)
        {
            if (_deferPhase <= 0 || now < _deferAt)
                return;
            if (!UiLang.IsChinese)
            {
                _deferPhase = 0;
                return;
            }
            try
            {
                if (_deferPhase == 1)
                {
                    long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    ApplyEncyclopediaDefs(true);
                    PerfProbeService.Accrue("Zh.Encyclopedia",
                        System.Diagnostics.Stopwatch.GetTimestamp() - t0);
                    _deferPhase = 0;
                    return;
                }
            }
            catch
            {
                _deferPhase = 0;
            }
        }

        /// <summary>Translate a raw English UI string when ZH is active; else pass-through.</summary>
        internal static string T(string english)
        {
            if (string.IsNullOrEmpty(english) || !UiLang.IsChinese)
                return english;
            EnsureDict();
            return TranslateLookup(english.Trim());
        }

        private static string TranslateLookup(string key)
        {
            if (string.IsNullOrEmpty(key))
                return key;
            string hit = ZhLookupService.LookupExactOrPack(key, Map);
            if (!string.IsNullOrEmpty(hit))
                return hit;

            for (int i = 0; i < PrefixRules.Count; i++)
            {
                PrefixRule r = PrefixRules[i];
                string expanded = ZhLookupService.ExpandIfPrefixParen(key, r.EnPrefix, r.ZhPrefix);
                if (!string.IsNullOrEmpty(expanded))
                    return expanded;
            }
            return key;
        }

        private static bool TryStripPackSuffix(string key, out string stripped, out string suffix)
        {
            return ZhLookupService.TryStripPackSuffix(key, out stripped, out suffix);
        }

        private static void HookSceneLoaded()
        {
            if (_sceneHooked)
                return;
            _sceneHooked = true;
            try
            {
                SceneManager.sceneLoaded += OnSceneLoaded;
            }
            catch { }
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            Touched.Clear();
            try
            {
                // Re-brand before ZH apply so stale EncSnapshots cannot wipe XE names.
                Plugin.RefreshEncyclopediaAircraft();
            }
            catch { }
            if (!UiLang.IsChinese)
                return;
            // Do not FindObjectsOfTypeAll on the load hitch frame.
            ScheduleDeferredZh(0.55f, true);
        }

        /// <summary>
        /// Keep encyclopedia snapshot English base aligned with XE brand so later
        /// ApplyEncyclopediaDefs cannot restore vanilla Cricket/Compass/etc.
        /// </summary>
        internal static void NoteBrandedAircraft(AircraftDefinition def)
        {
            if (def == null)
                return;
            UnitBrandingService.XeBrand brand;
            if (!UnitBrandingService.TryResolveBrand(def, out brand))
                return;
            int id = def.GetInstanceID();
            EncSnapshot snap;
            EncSnapshots.TryGetValue(id, out snap);
            snap.UnitName = brand.NameEn;
            snap.Code = brand.Code;
            // Always pin English Veyrn lore so scene/language toggles cannot restore vanilla text.
            snap.Description = brand.DescEn;
            if (snap.BogeyName == null)
                snap.BogeyName = def.bogeyName;
            EncSnapshots[id] = snap;
        }

        private static void EnsureDict()
        {
            if (_dictReady)
                return;
            _dictReady = true;
            SeedCoreDictionary();
            SeedPrefixRules();
            SeedMissionsAndTips();
            SeedKillFeedVerbs();
            UnitBrandingService.SeedZhNames(Add);
            LoadOptionalTsv();
        }

        private static void SeedKillFeedVerbs()
        {
            Add("shot down", "击落");
            Add("destroyed", "摧毁");
            Add("demolished", "拆除");
            Add("intercepted", "拦截");
            Add("sank", "击沉");
            Add("crashed", "坠毁");
            Add("was destroyed", "被摧毁");
            Add("collapsed", "坍塌");
            Add("Teamkilled by ", "被友军击杀：");
        }

        private static void SeedPrefixRules()
        {
            PrefixRules.Clear();
            AddPrefix("Virtual Joystick Sensitivity", "虚拟摇杆灵敏度");
            AddPrefix("Virtual Joystick Centering Force", "虚拟摇杆回中力度");
            AddPrefix("View Motion Sensitivity", "视角移动灵敏度");
            AddPrefix("View Smoothing", "视角平滑");
            AddPrefix("Button Click Delay", "按键单击延迟");
            AddPrefix("Button Hold Delay", "按键长按延迟");
        }

        private static void AddPrefix(string en, string zh)
        {
            PrefixRule r;
            r.EnPrefix = en;
            r.ZhPrefix = zh;
            PrefixRules.Add(r);
            // Also exact without value for dictionary completeness
            Add(en, zh);
        }

        private static void SeedCoreDictionary()
        {
            // Main menu / chrome
            Add("PLAY", "开始游戏");
            Add("Play", "开始游戏");
            Add("FLY >", "起飞 >");
            Add("CAMPAIGN", "战役");
            Add("Campaign", "战役");
            Add("MULTIPLAYER", "多人游戏");
            Add("Multiplayer", "多人游戏");
            Add("SINGLEPLAYER", "单人游戏");
            Add("Singleplayer", "单人游戏");
            Add("Single Player", "单人游戏");
            Add("WORKSHOP", "创意工坊");
            Add("Workshop", "创意工坊");
            Add("OPEN WORKSHOP", "打开创意工坊");
            Add("CLOSE WORKSHOP", "关闭创意工坊");
            Add("OPTIONS", "选项");
            Add("Options", "选项");
            Add("SETTINGS", "设置");
            Add("Settings", "设置");
            Add("CREDITS", "制作人员");
            Add("Credits", "制作人员");
            Add("EXIT GAME", "退出游戏");
            Add("Exit Game", "退出游戏");
            Add("QUIT", "退出");
            Add("Quit", "退出");
            Add("Exit", "退出");
            Add("QUIT TO MENU", "返回主菜单");
            Add("Quit to Menu", "返回主菜单");
            Add("QUIT TO DESKTOP", "退出至桌面");
            Add("Quit to Desktop", "退出至桌面");
            Add("RESUME", "继续");
            Add("Resume", "继续");
            Add("PAUSE", "暂停");
            Add("Pause", "暂停");
            Add("PAUSED", "已暂停");
            Add("Paused", "已暂停");
            Add("MAIN MENU", "主菜单");
            Add("Main Menu", "主菜单");
            Add("BACK", "返回");
            Add("Back", "返回");
            Add("< BACK", "< 返回");
            Add("< BACK TO LIST", "< 返回列表");
            Add("CLOSE", "关闭");
            Add("Close", "关闭");
            Add("APPLY", "应用");
            Add("Apply", "应用");
            Add("CANCEL", "取消");
            Add("Cancel", "取消");
            Add("CONFIRM", "确认");
            Add("Confirm", "确认");
            Add("Confirm Action", "确认操作");
            Add("OK", "确定");
            Add("YES", "是");
            Add("Yes", "是");
            Add("NO", "否");
            Add("No", "否");
            Add("SAVE", "保存");
            Add("Save", "保存");
            Add("LOAD", "读取");
            Add("Load", "读取");
            Add("CONTINUE", "继续");
            Add("Continue", "继续");
            Add("START", "开始");
            Add("Start", "开始");
            Add("READY", "就绪");
            Add("Ready", "就绪");
            Add("DEPLOY", "出动");
            Add("Deploy", "出动");
            Add("LAUNCH", "起飞");
            Add("Launch", "起飞");
            Add("ABORT", "中止");
            Add("Abort", "中止");
            Add("RETRY", "重试");
            Add("Retry", "重试");
            Add("RESPAWN", "重生");
            Add("Respawn", "重生");
            Add("SPECTATE", "观战");
            Add("Spectate", "观战");
            Add("HOST", "主机");
            Add("Host", "主机");
            Add("JOIN", "加入");
            Add("Join", "加入");
            Add("Join Lobby", "加入大厅");
            Add("DISCONNECT", "断开");
            Add("Disconnect", "断开");
            Add("LOBBY", "大厅");
            Add("Lobby", "大厅");
            Add("SERVER", "服务器");
            Add("Server", "服务器");
            Add("SERVERS", "服务器列表");
            Add("Servers", "服务器列表");
            Add("REFRESH", "刷新");
            Add("Refresh", "刷新");
            Add("SEARCH", "搜索");
            Add("Search", "搜索");
            Add("FILTER", "筛选");
            Add("Filter", "筛选");
            Add("FILTERS", "筛选");
            Add("CLEAR FILTER", "清除筛选");
            Add("NEW", "新建");
            Add("CREATE NEW", "新建");
            Add("Done", "完成");
            Add("Delete", "删除");
            Add("Remove", "移除");
            Add("Rename", "重命名");
            Add("Buy", "购买");
            Add("BUY", "购买");
            Add("SELL", "出售");
            Add("Empty", "空");
            Add("Free Flight", "自由飞行");
            Add("Profile", "档案");
            Add("SOCIAL", "社交");
            Add("PUBLIC", "公开");
            Add("Local", "本地");
            Add("LOADING", "加载中");
            Add("Loading...", "加载中…");
            Add("Loading", "加载中");
            Add("Joining Server...", "正在加入服务器…");

            Add("ENCYCLOPEDIA", "百科");
            Add("Encyclopedia", "百科");
            Add("HANGAR", "机库");
            Add("Hangar", "机库");
            Add("LOADOUT", "挂载");
            Add("Loadout", "挂载");
            Add("BRIEFING", "任务简报");
            Add("Briefing", "任务简报");
            Add("DEBRIEF", "任务总结");
            Add("Debrief", "任务总结");
            Add("DEBRIEFING", "任务总结");
            Add("MISSION", "任务");
            Add("Mission", "任务");
            Add("MISSIONS", "任务列表");
            Add("Missions", "任务列表");
            Add("Load Mission", "加载任务");
            Add("New Mission", "新任务");
            Add("Mission Name", "任务名称");
            Add("Mission Description", "任务描述");
            Add("Mission Summary", "任务摘要");
            Add("Mission Title", "任务标题");
            Add("Mission Parameters", "任务参数");
            Add("TRAINING", "训练");
            Add("Training", "训练");
            Add("FACTION", "阵营");
            Add("Faction", "阵营");
            Add("FACTIONS", "阵营");
            Add("Factions", "阵营");
            Add("Faction Settings", "阵营设置");
            Add("MAP", "地图");
            Add("Map", "地图");
            Add("SCOREBOARD", "计分板");
            Add("Scoreboard", "计分板");
            Add("SCORE", "得分");
            Add("Score", "得分");
            Add("STATS", "统计");
            Add("Stats", "统计");
            Add("STATISTICS", "统计");
            Add("Statistics", "统计");
            Add("PLAYER", "玩家");
            Add("Player", "玩家");
            Add("PLAYERS", "玩家");
            Add("Players", "玩家");
            Add("ALLIES", "友军");
            Add("Allies", "友军");
            Add("ENEMIES", "敌军");
            Add("Enemies", "敌军");
            Add("FRIENDLY", "友方");
            Add("Friendly", "友方");
            Add("HOSTILE", "敌对");
            Add("Hostile", "敌对");
            Add("Enemy", "敌军");
            Add("NEUTRAL", "中立");
            Add("Neutral", "中立");

            Add("AIRCRAFT", "飞机");
            Add("Aircraft", "飞机");
            Add("AIRFRAMES", "机体");
            Add("VEHICLES", "载具");
            Add("Vehicles", "载具");
            Add("SHIPS", "舰船");
            Add("Ships", "舰船");
            Add("WEAPONS", "武器");
            Add("Weapons", "武器");
            Add("Weapon", "武器");
            Add("MISSILES", "导弹");
            Add("Missiles", "导弹");
            Add("BOMBS", "炸弹");
            Add("Bombs", "炸弹");
            Add("GUNS", "航炮");
            Add("Guns", "航炮");
            Add("Building", "建筑");
            Add("Buildings", "建筑");
            Add("GROUND FORCES", "地面部队");
            Add("FUEL", "燃油");
            Add("Fuel", "燃油");
            Add("AMMO", "弹药");
            Add("Ammo", "弹药");
            Add("Munitions", "弹药");
            Add("FUNDS", "资金");
            Add("Funds", "资金");
            Add("ARMOR", "装甲");
            Add("Armor", "装甲");
            Add("DAMAGE", "损伤");
            Add("Damage", "损伤");
            Add("REPAIR", "维修");
            Add("Repair", "维修");
            Add("REARM", "补弹");
            Add("Rearm", "补弹");
            Add("REFUEL", "加油");
            Add("Refuel", "加油");
            Add("REQUISITION", "申请出动");
            Add("LOWER GEAR", "放下起落架");
            Add("REDUCE COLLECTIVE", "减小总距");
            Add("COLLECTIVE", "总距");
            Add("THROTTLE", "油门");
            Add("Airspeed", "空速");
            Add("Altitude", "高度");
            Add("UNITS", "单位");
            Add("Units", "单位");

            // Settings categories
            Add("CONTROLS", "操控");
            Add("Controls", "操控");
            Add("GRAPHICS", "画面");
            Add("Graphics", "图形");
            Add("AUDIO", "音频");
            Add("Audio", "音频");
            Add("VIDEO", "视频");
            Add("Video", "视频");
            Add("DISPLAY", "显示");
            Add("Display", "显示");
            Add("QUALITY", "画质");
            Add("Quality", "画质");
            Add("LANGUAGE", "语言");
            Add("Language", "语言");
            Add("GENERAL", "常规");
            Add("General", "常规");
            Add("GAMEPLAY", "游戏性");
            Add("Gameplay", "游戏性");
            Add("INTERFACE", "界面");
            Add("Interface", "界面");
            Add("ACCESSIBILITY", "无障碍");
            Add("HUD", "抬头显示");
            Add("HUD SETTINGS", "抬头显示设置");
            Add("HMD SETTINGS", "头显设置");
            Add("KEYBINDS", "按键");
            Add("Keybinds", "按键");
            Add("KEY BINDINGS", "按键绑定");
            Add("Key Bindings", "按键绑定");
            Add("BINDING SETTINGS", "绑定设置");
            Add("COMMON SETTINGS", "通用设置");
            Add("GLOBAL CONTROL SETTINGS", "全局操控设置");
            Add("AUDIO SETTINGS", "音频设置");
            Add("DISPLAY SETTINGS", "显示设置");
            Add("QUALITY SETTINGS", "画质设置");
            Add("CHAT SETTINGS", "聊天设置");
            Add("ACTIVITY SETTINGS", "活动设置");
            Add("THEME SETTINGS", "主题设置");
            Add("Map Settings", "地图设置");
            Add("Menu Settings", "菜单设置");
            Add("ENVIRONMENT & LIGHTING", "环境与光照");
            Add("Environment Settings", "环境设置");
            Add("MOUSE", "鼠标");
            Add("Mouse", "鼠标");
            Add("KEYBOARD", "键盘");
            Add("Keyboard", "键盘");
            Add("CONTROLLER", "手柄");
            Add("Controller", "手柄");
            Add("Button", "按键");
            Add("Buttons", "按键");
            Add("Axis", "轴向");
            Add("Category", "类别");

            // Controls menu toggles / labels
            Add("INVERT Y", "反转 Y 轴");
            Add("Invert Y", "反转 Y 轴");
            Add("SENSITIVITY", "灵敏度");
            Add("Sensitivity", "灵敏度");
            Add("VOLUME", "音量");
            Add("Volume", "音量");
            Add("MUSIC", "音乐");
            Add("Music", "音乐");
            Add("SFX", "音效");
            Add("Master Volume", "主音量");
            Add("Music Volume", "音乐音量");
            Add("Effects Volume", "音效音量");
            Add("Interface Volume", "界面音量");
            Add("Menu Volume", "菜单音量");
            Add("Fullscreen", "全屏");
            Add("FULLSCREEN", "全屏");
            Add("Windowed", "窗口");
            Add("WINDOWED", "窗口");
            Add("Windowed Display", "窗口显示");
            Add("VSync", "垂直同步");
            Add("VSYNC", "垂直同步");
            Add("Resolution", "分辨率");
            Add("RESOLUTION", "分辨率");
            Add("SCREEN RESOLUTION", "屏幕分辨率");
            Add("Brightness", "亮度");
            Add("BRIGHTNESS", "亮度");
            Add("Reset", "重置");
            Add("RESET", "重置");
            Add("Defaults", "默认");
            Add("DEFAULTS", "默认");
            Add("Default", "默认");
            Add("Enabled", "开启");
            Add("Disabled", "关闭");
            Add("On", "开");
            Add("Off", "关");
            Add("ON", "开");
            Add("OFF", "关");
            Add("None", "无");
            Add("All", "全部");
            Add("ALL", "全部");
            Add("Open Control Bindings Menu", "打开按键绑定菜单");
            Add("Use Virtual Joystick (Mouse Flight Controls)", "使用虚拟摇杆（鼠标飞行）");
            Add("Invert Virtual Joystick Pitch", "反转虚拟摇杆俯仰");
            Add("Invert Pitch for View Controls", "反转视角俯仰");
            Add("Use Throttle Axis Negative Region", "使用油门轴负向区");
            Add("Use Throttle Relative Axis", "使用相对油门轴");
            Add("Allow Controller Menu Navigation", "允许手柄菜单导航");
            Add("Weapon Safety Lock while Map Maximized", "地图最大化时武器保险");
            Add("Invert Throttle Control when used as Collective", "油门用作总距时反转");
            Add("Auto Center", "自动回中");
            Add("Center Stabilization", "回中稳定");
            Add("Chat Enabled", "启用聊天");
            Add("Reset Graphics Settings", "重置图形设置");
            Add("Maximum FPS", "最大帧率");
            Add("TEXTURE QUALITY", "纹理质量");
            Add("SHADOWS QUALITY", "阴影质量");
            Add("Soft Shadows", "柔和阴影");
            Add("Shadow Distance", "阴影距离");
            Add("Anti-Aliasing (MSAA)", "抗锯齿 (MSAA)");
            Add("Anisotropic Filtering", "各向异性过滤");
            Add("Additional Lights", "额外灯光");
            Add("Clouds Detail", "云层细节");
            Add("Tree Distance", "树木距离");
            Add("LOD Distance", "LOD 距离");
            Add("Grass", "草地");
            Add("Roads", "道路");
            Add("Cinematic Mode", "电影模式");
            Add("Discord Rich Presence", "Discord 状态");
            Add("Steam Rich Presence", "Steam 状态");
            Add("Profanity filter", "脏话过滤");
            Add("Show HitMarkers", "显示命中标记");
            Add("Radar Warning", "雷达告警");
            Add("Missile Alert", "导弹告警");
            Add("Target Padlock", "目标锁定视角");
            Add("Landing Camera", "着陆相机");
            Add("Automatic camera NVG", "自动夜视");
            Add("Virtual MFD", "虚拟多功能显示器");
            Add("Zoom in on Boresight Targets", "瞄准线目标放大");
            Add("Time displayed on HUD", "HUD 显示时间");
            Add("Unit Icon Size", "单位图标大小");
            Add("HUD Text Size", "HUD 文字大小");
            Add("HMD Text Size", "头显文字大小");
            Add("Overlay Text Size", "叠加文字大小");
            Add("Cockpit Field of View", "座舱视野");
            Add("External Field of View", "外部视野");
            Add("Cockpit camera inertia", "座舱相机惯性");
            Add("Use Head Tracker inputs for cockpit camera", "座舱相机使用头部追踪");
            Add("Head Tracking Pitch & Yaw Sensitivity", "头部追踪俯仰/偏航灵敏度");
            Add("Head Tracking Position Sensitivity", "头部追踪位置灵敏度");
            Add("Head Tracking Roll Sensitivity", "头部追踪滚转灵敏度");
            Add("Tobii Eye Tracker Settings", "Tobii 眼动设置");
            Add("Eye Tracking Responsiveness", "眼动响应");
            Add("Eye VS Head Tracking Ratio", "眼动/头部追踪比例");
            Add("Distance to hide HMD", "隐藏头显距离");
            Add("Text To Speech Enabled", "启用语音朗读");
            Add("Text To Speech Speed", "朗读速度");
            Add("Text To Speech Volume", "朗读音量");
            Add("Test TTS Speed", "测试朗读速度");
            Add("Metric", "公制");
            Add("Imperial", "英制");
            Add("Swedish SN / kn", "瑞典单位制");
            Add("Swedish SN / kt", "瑞典单位制");
            Add("Swedish SN / kn / SM", "瑞典单位制");
            Add("Swedish SN / kt / SM", "瑞典单位制");
            Add("Sverige metric", "瑞典单位制");
            Add("Allow Event Content", "允许活动内容");
            Add("Allow Player Respawn", "允许玩家重生");
            Add("DESCRIPTION", "描述");
            Add("Description", "描述");
            Add("NAME", "名称");
            Add("Name", "名称");
            Add("Type", "类型");
            Add("Status", "状态");
            Add("Online", "在线");
            Add("Offline", "离线");
            Add("Ping", "延迟");
            Add("Password", "密码");
            Add("Lobby Password", "大厅密码");
            Add("Enter Lobby Password", "输入大厅密码");
            Add("Public", "公开");
            Add("Private", "私有");
            Add("Difficulty", "难度");
            Add("Easy", "简单");
            Add("Normal", "普通");
            Add("Hard", "困难");
            Add("Realistic", "写实");
            Add("Custom", "自定义");
            Add("Next", "下一步");
            Add("NEXT >", "下一步 >");
            Add("Previous", "上一步");
            Add("Finish", "完成");
            Add("Skip", "跳过");
            Add("Help", "帮助");
            Add("Tutorial", "教程");
            Add("Tips", "提示");
            Add("Version", "版本");
            Add("Mods", "模组");
            Add("Mod", "模组");
            Add("Objectives", "目标");
            Add("Parameters", "参数");
            Add("Conditions", "条件");
            Add("Environment", "环境");
            Add("UPDATE", "更新");
            Add("UPDATE ALL", "全部更新");
            Add("SUBSCRIBE", "订阅");
            Add("UPLOAD", "上传");
            Add("UPLOADING", "上传中");
            Add("DONATE", "捐献");
            Add("DONATE TO THE WAR EFFORT", "捐献给战争努力");
            Add("Discard and Exit", "放弃并退出");
            Add("Save and Exit", "保存并退出");
            Add("Save As", "另存为");
            Add("Save Complete", "保存完成");
            Add("Save Error", "保存失败");
            Add("Failed to load", "加载失败");
            Add("NOT FOUND", "未找到");
            Add("Muted", "已静音");
            Add("Banned", "已封禁");
            Add("Blocked", "已屏蔽");
            Add("Block Lists", "屏蔽列表");
            Add("Kicked", "已踢出");
            Add("Leave and Join", "离开并加入");
            Add("Accept game Invite?", "接受游戏邀请？");
            Add("Exit Mission Editor?", "退出任务编辑器？");
            Add("Enter text...", "输入文本…");
            Add("Please wait", "请稍候");
            Add("Game Over", "游戏结束");
            Add("MISSION COMPLETE", "任务完成");
            Add("Mission Complete", "任务完成");
            Add("MISSION FAILED", "任务失败");
            Add("Mission Failed", "任务失败");
            Add("VICTORY", "胜利");
            Add("Victory", "胜利");
            Add("DEFEAT", "失败");
            Add("Defeat", "失败");
            Add("Kills", "击杀");
            Add("Deaths", "阵亡");
            Add("Assists", "助攻");
            Add("Rank", "军衔");
            Add("Level", "等级");
            Add("Experience", "经验");
            Add("Budget", "预算");
            Add("Livery", "涂装");
            Add("Aircraft Livery", "飞机涂装");
            Add("Join Menu", "加入菜单");
            Add("New Lobby Settings", "新建大厅设置");
            Add("Player Stats", "玩家统计");
            Add("Player list", "玩家列表");
            Add("Mission list", "任务列表");
            Add("Mission Preview", "任务预览");
            Add("Mission preview", "任务预览");
            Add("Score Menu", "得分菜单");
            Add("Unit Options", "单位选项");
            Add("Too many players", "玩家过多");
            Add("Waiting for players", "等待玩家");
            Add("Waiting for host", "等待主机");
            Add("Connecting...", "连接中…");
            Add("Connecting", "连接中");
            Add("Disconnected", "已断开");
            Add("Connection lost", "连接丢失");
            Add("STALL WARNING", "失速警告");
            Add("OVERSPEED", "超速");
            Add("VORTEX RING STATE", "涡环状态");
            Add("ANTI-GRAV", "反重力");

            // Flight / HUD
            Add("ALTITUDE", "高度");
            Add("SPEED", "速度");
            Add("Speed", "速度");
            Add("HEADING", "航向");
            Add("Heading", "航向");
            Add("Throttle", "油门");
            Add("GEAR", "起落架");
            Add("Gear", "起落架");
            Add("FLAPS", "襟翼");
            Add("Flaps", "襟翼");
            Add("BRAKES", "刹车");
            Add("Brakes", "刹车");
            Add("RADAR", "雷达");
            Add("Radar", "雷达");
            Add("RWR", "Oritasy RWR");
            Add("WARNING", "警告");
            Add("Warning", "警告");
            Add("CAUTION", "注意");
            Add("Caution", "注意");
            Add("LOCK", "锁定");
            Add("Lock", "锁定");
            Add("TRACK", "跟踪");
            Add("Track", "跟踪");
            Add("TARGET", "目标");
            Add("Target", "目标");
            Add("TARGETS", "目标");
            Add("Targets", "目标");
            Add("SELECTED", "已选");
            Add("Selected", "已选");
            Add("LANDING", "着陆");
            Add("Landing", "着陆");
            Add("TAKEOFF", "起飞");
            Add("Takeoff", "起飞");
            Add("APPROACH", "进近");
            Add("Approach", "进近");
            Add("AUTOPILOT", "自动驾驶");
            Add("Autopilot", "自动驾驶");
            Add("EJECT", "弹射");
            Add("Eject", "弹射");
            Add("BINGO", "燃油告急");
            Add("Bingo", "燃油告急");
            Add("JOKER", "燃油警戒");
            Add("Joker", "燃油警戒");
            Add("STALL", "失速");
            Add("Stall", "失速");
            Add("OVERG", "过载超限");
            Add("OVER-G", "过载超限");
            Add("TERRAIN", "地形");
            Add("Terrain", "地形");
            Add("PULL UP", "拉起");
            Add("Pull Up", "拉起");
            Add("LOW ALTITUDE", "低高度");
            Add("Low Altitude", "低高度");
            Add("MISSILE", "导弹");
            Add("Missile", "导弹");
            Add("INCOMING", "来袭");
            Add("Incoming", "来袭");
            Add("CHAFF", "箔条");
            Add("Chaff", "箔条");
            Add("FLARE", "热焰弹");
            Add("Flare", "热焰弹");
            Add("COUNTERMEASURES", "干扰弹");
            Add("Countermeasures", "干扰弹");
            Add("AoA", "迎角");
            Add("NAV", "导航");
            Add("GND", "地面");
            Add("VTOL", "垂直起降");

            // Encyclopedia type tabs / typeName chrome
            Add("TRUCK", "卡车");
            Add("UGV", "无人地面载具");
            Add("LCV", "轻型战斗车辆");
            Add("AFV", "装甲战车");
            Add("MBT", "主战坦克");
            Add("ART", "火炮");
            Add("AAA", "高炮");
            Add("IR_SAM", "红外防空导弹");
            Add("R_SAM", "雷达防空导弹");
            Add("RDR", "雷达");
            Add("CIV", "民用");
            Add("FAC", "工厂");
            Add("DEP", "仓库");
            Add("HGR", "机库");
            Add("DEF", "防御工事");
            Add("CV", "航母");
            Add("LHA", "两栖攻击舰");
            Add("LFD", "轻型舰队航母");
            Add("DDG", "驱逐舰");
            Add("FFG", "护卫舰");
            Add("FFL", "轻型护卫舰");
            Add("LC", "登陆艇");
            Add("PB", "巡逻艇");

            SeedKeybindDictionary();
        }

        private static void SeedKeybindDictionary()
        {
            // Rewired Control Mapper descriptive names
            Add("Pitch Axis", "俯仰轴");
            Add("Pitch Up", "抬头");
            Add("Pitch Down", "低头");
            Add("Yaw Axis", "偏航轴");
            Add("Yaw Left", "左偏航");
            Add("Yaw Right", "右偏航");
            Add("Roll Axis", "滚转轴");
            Add("Roll Left", "左滚");
            Add("Roll Right", "右滚");
            Add("Throttle Axis", "油门轴");
            Add("Increase Throttle", "增大油门");
            Add("Decrease Throttle", "减小油门");
            Add("Brake Axis", "刹车轴");
            Add("Apply Brakes", "刹车");
            Add("Fire", "射击/发射");
            Add("Next Weapon", "下一武器");
            Add("Previous Weapon", "上一武器");
            Add("Countermeasures", "干扰弹");
            Add("Next Countermeasure", "下一干扰弹");
            Add("Toggle Gear", "起落架");
            Add("Toggle Engine", "发动机");
            Add("Toggle Flight Assist", "飞行辅助");
            Add("Toggle Radar", "雷达开关");
            Add("Toggle Map", "地图");
            Add("Toggle Night Vision", "夜视");
            Add("Toggle Navigation Lights", "航行灯");
            Add("Toggle Turret Control", "炮塔控制");
            Add("Toggle Left Panel", "左侧面板");
            Add("Eject", "弹射");
            Add("Pause", "暂停");
            Add("Chat", "聊天");
            Add("Open Chat", "打开聊天");
            Add("Cancel Chat", "取消聊天");
            Add("Submit Chat", "发送聊天");
            Add("Center View", "视角回中");
            Add("Free Look", "自由观察");
            Add("Switch View", "切换视角");
            Add("Field of View", "视野");
            Add("Increase FoV", "增大视野");
            Add("Decrease FoV", "减小视野");
            Add("Zoom View", "缩放视角");
            Add("Zoom In", "放大");
            Add("Zoom Out", "缩小");
            Add("Pan View", "平移视角");
            Add("Pan Left", "视角左移");
            Add("Pan Right", "视角右移");
            Add("Tilt View", "俯仰视角");
            Add("Tilt Up", "视角上仰");
            Add("Tilt Down", "视角下俯");
            Add("Camera Controls", "相机控制");
            Add("Camera Jump", "相机跳转");
            Add("Flight Controls", "飞行操控");
            Add("Look At Target", "看向目标");
            Add("Look At Enemy Aircraft", "看向敌机");
            Add("Jump to Enemy Aircraft", "跳转到敌机");
            Add("Spectate Previous Aircraft", "观战上一架飞机");
            Add("Target Select", "选择目标");
            Add("Target Cancel", "取消目标");
            Add("Link Guns", "联装航炮");
            Add("Open Weapon Wheel", "武器轮盘");
            Add("Open Radial Menu", "径向菜单");
            Add("Radial Menu", "径向菜单");
            Add("Radial Menu Up", "径向菜单上");
            Add("Radial Menu Down", "径向菜单下");
            Add("Radial Menu Left", "径向菜单左");
            Add("Radial Menu Right", "径向菜单右");
            Add("Radial Menu Horizontal", "径向菜单水平");
            Add("Radial Menu Vertical", "径向菜单垂直");
            Add("Move Forward", "前进");
            Add("Move Backward", "后退");
            Add("Move Left", "左移");
            Add("Move Right", "右移");
            Add("Move Up", "上升");
            Add("Move Down", "下降");
            Add("Move Vertical", "垂直移动");
            Add("Move Lateral", "横向移动");
            Add("Move Longitudinal", "纵向移动");
            Add("Move Map Up", "地图上移");
            Add("Move Map Down", "地图下移");
            Add("Move Map Left", "地图左移");
            Add("Move Map Right", "地图右移");
            Add("Move Map Horizontal", "地图水平");
            Add("Move Map Vertical", "地图垂直");
            Add("Axis Modifier", "轴向修饰键");
            Add("Custom Axis 1", "自定义轴 1");
            Add("Custom Axis 1 Up", "自定义轴 1 上");
            Add("Custom Axis 1 Down", "自定义轴 1 下");
            Add("Vote Kick Yes", "投票踢出：赞成");
            Add("Vote Kick No", "投票踢出：反对");
            Add("Mission Editor", "任务编辑器");
            Add("Select Unit Mode", "选择单位模式");
            Add("Rotate Unit Mode", "旋转单位模式");
            Add("Translate Unit Mode", "平移单位模式");
            Add("Delete Unit", "删除单位");
            Add("Focus Unit", "聚焦单位");
            Add("Toggle mission debug menu", "任务调试菜单");
            Add("Toggle performance numbers", "性能数字");
            Add("Toggle debug graphs", "调试图表");
            Add("Toggle Slow Motion", "慢动作");
            Add("Gameplay", "游戏性");
            Add("Game", "游戏");
            Add("System", "系统");
            Add("Debug", "调试");

            // Nuclear Option keybind labels often still English after ZH toggle
            Add("Flaps", "襟翼");
            Add("Toggle Flaps", "襟翼开关");
            Add("Flaps Up", "襟翼收起");
            Add("Flaps Down", "襟翼放下");
            Add("Airbrake", "减速板");
            Add("Toggle Airbrake", "减速板开关");
            Add("Apply Airbrake", "打开减速板");
            Add("Landing Hook", "着舰钩");
            Add("Toggle Landing Hook", "着舰钩开关");
            Add("Arresting Hook", "拦阻钩");
            Add("Toggle Arresting Hook", "拦阻钩开关");
            Add("Jettison", "抛放");
            Add("Jettison Weapons", "抛放武器");
            Add("Jettison Stores", "抛放外挂");
            Add("Fire Guns", "射击航炮");
            Add("Fire Selected", "发射所选");
            Add("Fire Weapon", "发射武器");
            Add("Select Weapon", "选择武器");
            Add("Cycle Weapon", "循环武器");
            Add("Weapon Bay", "弹舱");
            Add("Toggle Weapon Bay", "弹舱开关");
            Add("Open Weapon Bay", "打开弹舱");
            Add("Close Weapon Bay", "关闭弹舱");
            Add("Afterburner", "加力");
            Add("Toggle Afterburner", "加力开关");
            Add("Reverse Thrust", "反推");
            Add("Toggle Reverse Thrust", "反推开关");
            Add("Parking Brake", "停机刹车");
            Add("Toggle Parking Brake", "停机刹车");
            Add("Wheel Brakes", "机轮刹车");
            Add("Nose Wheel Steering", "前轮转向");
            Add("Canopy", "座舱盖");
            Add("Toggle Canopy", "座舱盖开关");
            Add("Chaff", "箔条");
            Add("Flare", "热焰弹");
            Add("Flares", "热焰弹");
            Add("Deploy Chaff", "投放箔条");
            Add("Deploy Flare", "投放热焰弹");
            Add("Deploy Flares", "投放热焰弹");
            Add("Radar Mode", "雷达模式");
            Add("Cycle Radar Mode", "切换雷达模式");
            Add("Next Radar Mode", "下一雷达模式");
            Add("Previous Radar Mode", "上一雷达模式");
            Add("Radar Range", "雷达距离");
            Add("Increase Radar Range", "增大雷达距离");
            Add("Decrease Radar Range", "减小雷达距离");
            Add("IRST", "红外搜索跟踪");
            Add("Toggle IRST", "红外搜索跟踪开关");
            Add("Lock", "锁定");
            Add("Lock Target", "锁定目标");
            Add("Unlock Target", "解锁目标");
            Add("Next Target", "下一目标");
            Add("Previous Target", "上一目标");
            Add("Closest Target", "最近目标");
            Add("Cycle Target", "循环目标");
            Add("Designate Target", "指定目标");
            Add("Clear Target", "清除目标");
            Add("Sensor Select", "选择传感器");
            Add("Next Sensor", "下一传感器");
            Add("Previous Sensor", "上一传感器");
            Add("MFD", "多功能显示器");
            Add("Next MFD", "下一多功能显示器");
            Add("Previous MFD", "上一多功能显示器");
            Add("MFD Interact", "MFD 交互");
            Add("HMD", "头显");
            Add("Toggle HMD", "头显开关");
            Add("Toggle Labels", "标签开关");
            Add("Toggle Unit Labels", "单位标签开关");
            Add("Toggle HUD", "抬头显示开关");
            Add("Toggle Cockpit", "座舱开关");
            Add("Cockpit Interact", "座舱交互");
            Add("Interact", "交互");
            Add("Use", "使用");
            Add("Reload", "装填");
            Add("Scoreboard", "计分板");
            Add("Toggle Scoreboard", "计分板开关");
            Add("Leaderboard", "排行榜");
            Add("Push to Talk", "按键说话");
            Add("Voice Chat", "语音聊天");
            Add("Screenshot", "截图");
            Add("Photo Mode", "拍照模式");
            Add("Time Scale", "时间倍率");
            Add("Increase Time Scale", "加快时间");
            Add("Decrease Time Scale", "减慢时间");
            Add("Reset Time Scale", "重置时间倍率");
            Add("Pause Menu", "暂停菜单");
            Add("Menu", "菜单");
            Add("Back", "返回");
            Add("Confirm", "确认");
            Add("Cancel", "取消");
            Add("Apply", "应用");
            Add("Reset", "重置");
            Add("Default", "默认");
            Add("Defaults", "默认");
            Add("Restore Defaults", "恢复默认");
            Add("Clear Binding", "清除绑定");
            Add("Listen", "聆听按键");
            Add("Press Button", "请按键");
            Add("Click to bind", "点击绑定");
            Add("Click to change", "点击更改");
            Add("Unbound", "未绑定");
            Add("Not Bound", "未绑定");
            Add("Primary", "主绑定");
            Add("Secondary", "副绑定");
            Add("Modifier", "修饰键");
            Add("Mouse X", "鼠标 X");
            Add("Mouse Y", "鼠标 Y");
            Add("Mouse Horizontal", "鼠标水平");
            Add("Mouse Vertical", "鼠标垂直");
            Add("Look Horizontal", "视角水平");
            Add("Look Vertical", "视角垂直");
            Add("Look Left", "视角向左");
            Add("Look Right", "视角向右");
            Add("Look Up", "视角向上");
            Add("Look Down", "视角向下");
            Add("Trim Pitch Up", "俯仰配平上");
            Add("Trim Pitch Down", "俯仰配平下");
            Add("Trim Roll Left", "滚转配平左");
            Add("Trim Roll Right", "滚转配平右");
            Add("Trim Yaw Left", "偏航配平左");
            Add("Trim Yaw Right", "偏航配平右");
            Add("Trim Reset", "配平复位");
            Add("Reset Trim", "配平复位");
            Add("Autopilot", "自动驾驶");
            Add("Toggle Autopilot", "自动驾驶开关");
            Add("Formation", "编队");
            Add("Wingman", "僚机");
            Add("Give Order", "下达指令");
            Add("Attack My Target", "攻击我的目标");
            Add("Cover Me", "掩护我");
            Add("Return to Base", "返航");
            Add("Orbit", "盘旋");
            Add("Hold Position", "待命");
            Add("Spectate", "观战");
            Add("Spectate Next", "观战下一");
            Add("Spectate Previous", "观战上一");
            Add("Free Camera", "自由相机");
            Add("Orbit Camera", "环绕相机");
            Add("Chase Camera", "追逐相机");
            Add("Cockpit Camera", "座舱相机");
            Add("External Camera", "外部相机");
            Add("Next Camera", "下一相机");
            Add("Previous Camera", "上一相机");
            Add("Camera Mode", "相机模式");
            Add("Cycle Camera", "循环相机");
            Add("Map Zoom In", "地图放大");
            Add("Map Zoom Out", "地图缩小");
            Add("Map Center", "地图回中");
            Add("Toggle Minimap", "小地图开关");
            Add("Minimap", "小地图");
            Add("Strategic Map", "战略地图");
            Add("Unit Info", "单位信息");
            Add("Encyclopedia", "百科");
            Add("Workshop", "创意工坊");
            Add("Multiplayer", "多人游戏");
            Add("Singleplayer", "单人游戏");
            Add("Single Player", "单人游戏");
            Add("Settings", "设置");
            Add("Exit", "退出");
            Add("Quit", "退出");
            Add("Resume", "继续");
            Add("Continue", "继续");
            Add("Start", "开始");
            Add("Join", "加入");
            Add("Host", "主机");
            Add("Disconnect", "断开");
            Add("Respawn", "重生");
            Add("Redeploy", "重新部署");
            Add("Select Spawn", "选择出生点");
            Add("Ready", "就绪");
            Add("Not Ready", "未就绪");

            // Splash / community / mission editor (from missed_zh.txt @ 0.0.9.82)
            Add("Join our Community", "加入我们的社区");
            Add("Early Access Version 0.34", "抢先体验版 0.34");
            Add("Early Access Version", "抢先体验版");
            Add("Change Log", "更新日志");
            Add("Changelog", "更新日志");
            Add("Development Roadmap", "开发路线图");
            Add("Merch Store", "周边商店");
            Add("Control Changes", "操作变更");
            Add("Did you know?", "你知道吗？");
            Add("Did you know", "你知道吗");
            Add("MISSION EDITOR", "任务编辑器");
            Add("Mission Editor", "任务编辑器");
            Add("iallemege?", "IAllemege?");
            Add("IAllemege", "IAllemege");
            Add("By IAllemege", "By IAllemege");
        }

        /// <summary>Built-in mission titles, mode names, and high-visibility briefing / tip lines.</summary>
        private static void SeedMissionsAndTips()
        {
            // Tutorials
            Add("Tutorial 1 - Taxi and Takeoff", "教程 1 - 滑行与起飞");
            Add("Tutorial 2 - Landing", "教程 2 - 着陆");
            Add("Tutorial 3 - Targeting and Weapons", "教程 3 - 瞄准与武器");
            Add("Tutorial 4 - Infrared Countermeasures", "教程 4 - 红外对抗");
            Add("Tutorial 5 - Radar Countermeasures", "教程 5 - 雷达对抗");
            Add("Tutorials", "教程");

            // Numbered story missions (with and without index)
            Add("01. Convoy Attack", "01. 车队奇袭");
            Add("Convoy Attack", "车队奇袭");
            Add("02. Round Up", "02. 围歼行动");
            Add("Round Up", "围歼行动");
            Add("03. Point Blank", "03. 零距突击");
            Add("Point Blank", "零距突击");
            Add("04. Cruise Missile Interception", "04. 巡航导弹拦截");
            Add("Cruise Missile Interception", "巡航导弹拦截");
            Add("05. Furball", "05. 混战狗斗");
            Add("Furball", "混战狗斗");
            Add("06. Bridge Defense", "06. 大桥阻击");
            Add("Bridge Defense", "大桥阻击");
            Add("07. Blackout", "07. 灯火管制");
            Add("Blackout", "灯火管制");
            Add("08. Infiltration", "08. 渗透突袭");
            Add("Infiltration", "渗透突袭");
            Add("09. Depot Strike", "09. 仓库突袭");
            Add("Depot Strike", "仓库突袭");
            Add("10. Dustbowl", "10. 尘暴盆地");
            Add("Dustbowl", "尘暴盆地");
            Add("11. Expedition", "11. 远征部署");
            Add("Expedition", "远征部署");
            Add("12. Shifting Tide", "12. 潮汐逆转");
            Add("Shifting Tide", "潮汐逆转");
            Add("13. Reprisal", "13. 报复打击");
            Add("Reprisal", "报复打击");
            Add("14. To Sink a Carrier", "14. 击沉航母");
            Add("To Sink a Carrier", "击沉航母");
            Add("15. Breaking the Atoll", "15. 环礁破袭");
            Add("Breaking the Atoll", "环礁破袭");

            // Large modes + co-op variants
            Add("Altercation", "冲突");
            Add("Breakout", "突围");
            Add("Carrier Duel", "航母对决");
            Add("Confrontation", "对峙");
            Add("Domination", "主宰");
            Add("Escalation", "全面升级");
            Add("Terminal Control", "终端控制");
            Add("Altercation Co-op as BDF", "冲突协作·BDF");
            Add("Altercation Co-op as PALA", "冲突协作·PALA");
            Add("Confrontation Co-op as BDF", "对峙协作·BDF");
            Add("Confrontation Co-op as PALA", "对峙协作·PALA");
            Add("Domination Co-op as BDF", "主宰协作·BDF");
            Add("Domination Co-op as PALA", "主宰协作·PALA");
            Add("Escalation Co-op as BDF", "全面升级协作·BDF");
            Add("Escalation Co-op as PALA", "全面升级协作·PALA");
            Add("Terminal Control Co-op as BDF", "终端控制协作·BDF");
            Add("Terminal Control Co-op as PALA", "终端控制协作·PALA");

            // Mission list / briefing chrome
            Add("Built-In", "内置");
            Add("BuiltIn", "内置");
            Add("Official", "官方");
            Add("Community", "社区");
            Add("User Missions", "用户任务");
            Add("Select Mission", "选择任务");
            Add("Mission Briefing", "任务简报");
            Add("Start Mission", "开始任务");
            Add("End Mission", "结束任务");
            Add("Leave Mission", "离开任务");
            Add("Restart Mission", "重开任务");
            Add("Objective", "目标");
            Add("Primary Objective", "主要目标");
            Add("Secondary Objective", "次要目标");
            Add("Optional Objective", "可选目标");
            Add("Complete", "完成");
            Add("Failed", "失败");
            Add("Incomplete", "未完成");
            Add("Mission Successful!", "任务成功！");
            Add("Mission Complete!", "任务完成！");
            Add("Mission Failed.", "任务失败。");
            Add("All Objectives are complete! Leave the area and RTB.", "全部目标已完成！撤离并返航。");
            Add("Priority targets have been added to your HUD.", "优先目标已添加到抬头显示。");
            Add("Excellent work! Return safely to base to complete the mission.", "出色！安全返航以完成任务。");
            Add("Mission Successful! The vehicle depot has been neutralized and your aircraft has returned to base.", "任务成功！车辆仓库已清除，飞机已返航。");
            Add("The enemy installation has been removed, mission accomplished!", "敌方设施已清除，任务完成！");
            Add("Enemy aircraft production facilities are destroyed. Mission accomplished.", "敌方飞机制造设施已摧毁。任务完成。");
            Add("Enemy aircraft production is destroyed - mission accomplished.", "敌方飞机制造已摧毁——任务完成。");
            Add("The enemy has capitulated. We are victorious!", "敌军已投降。我们胜利了！");
            Add("Naval reinforcements are enroute. ETA 10 minutes.", "海军增援正在途中。预计 10 分钟。");
            Add("Naval reinforcements have arrived.", "海军增援已抵达。");
            Add("Enemy carrier spotted.", "发现敌方航母。");
            Add("PALA's carrier is sinking. BDF is victorious!", "PALA 航母正在沉没。BDF 胜利！");
            Add("BDF's carrier is sinking. PALA is victorious!", "BDF 航母正在沉没。PALA 胜利！");
            Add("The BDF Fleet Carrier has been sunk.", "BDF 舰队航母已被击沉。");
            Add("The PALA Fleet Carrier has been sunk.", "PALA 舰队航母已被击沉。");
            Add("Well done, the threat has been neutralized.", "干得好，威胁已被清除。");
            Add("The base has been hit!", "基地遭到打击！");
            Add("Good shooting. Now get back to friendly territory intact.", "射击出色。现在完整返回友方空域。");
            Add("Form up on me and I'll lead us to the target.", "跟我编队，我带你们去目标。");
            Add("Headed to the target now.", "正在前往目标。");
            Add("Convoy has been spotted. Lets get to work.", "发现车队。开始行动。");
            Add("Excellent work, every convoy vehicle has been destroyed.", "出色，车队车辆已全部摧毁。");
            Add("SAM Site 1 is neutralised.", "1 号防空导弹阵地已清除。");
            Add("SAM Site 2 is neutralised.", "2 号防空导弹阵地已清除。");
            Add("The MLRS battery is out of action. Our base should be safe!", "多管火箭炮阵地已失效。基地应该安全了！");
            Add("The airbase's supplies have been destroyed. PALA forces won't be able to launch aircraft!", "机场补给已摧毁。PALA 将无法起飞！");
            Add("The naval base is down, good work!", "海军基地已瘫痪，干得好！");
            Add("Enemy tanks cresting the ridge to the south!", "敌坦克正越过南侧山脊！");
            Add("Enemy tanks, south east, 3 kilometers out!", "东南方向敌坦克，距离 3 公里！");
            Add("Enemy tanks to the east, 5 kilometers out!", "东侧敌坦克，距离 5 公里！");
            Add("Enemy tanks to the north east!", "东北方向敌坦克！");
            Add("Enemy troop transports spotted on the highway to the south.", "南侧公路发现敌运兵车。");
            Add("Excellent work, enemy anti-air launchers are destroyed.", "出色，敌防空发射器已摧毁。");
            Add("PALA losses are heavy. Units are retreating.", "PALA 损失惨重。部队正在撤退。");
            Add("Targets marked. Bombers are now inbound.", "目标已标记。轰炸机正在进场。");
            Add("Excellent work. The enemy airfield has been razed once again.", "出色。敌机场再次被夷平。");
            Add("Did you know?", "你知道吗？");
            Add("Hint", "提示");
            Add("Hints", "提示");
            Add("Tip", "提示");
            Add("Loading Tip", "加载提示");

            // Briefing descriptions (exact English from built-in missions)
            Add("Fly a T/A-30 Compass on a ground attack mission. Use missiles, rockets, bombs and guns to destroy a BDF convoy of lightly defended vehicles.",
                "驾驶 T/A-30 Compass 执行对地攻击。用导弹、火箭、炸弹与航炮摧毁防护薄弱的 BDF 车队。");
            Add("Prevent a column of vehicles from crossing the bridge to Boscali Island, before neutralizing the attack at its source.",
                "阻止车队越过通往 Boscali 岛的桥梁，再摧毁进攻源头。");
            Add("Use the FS-12 Revoker to intercept a volley of cruise missiles launched at your base from Primeva.",
                "驾驶 FS-12 Revoker 拦截从 Primeva 射向基地的巡航导弹齐射。");
            Add("Fly the T/A-30 Compass in an intense dogfight - your side is outnumbered 8 to 5.",
                "驾驶 T/A-30 Compass 进行激烈缠斗——己方以 5 对 8 处于劣势。");
            Add("Use various SEAD/DEAD abilities unique to the EW-25 Medusa to facilitate an attack against a heavily defended ammo dump.",
                "运用 EW-25 Medusa 独有的 SEAD/DEAD 能力，为打击严密设防的弹药库创造条件。");
            Add("Fly an SAH-46 Chicane under the radar to eliminate an enemy forward arming and refueling point.",
                "驾驶 SAH-46 Chicane 贴地突防，清除敌前进补给点。");
            Add("Fly the SFB-81 Darkreach on a bombing mission to destroy a PALA vehicle depot on a heavily defended airbase.",
                "驾驶 SFB-81 Darkreach 轰炸严密设防机场上的 PALA 车辆仓库。");
            Add("Launch from an aircraft carrier and tackle a diverse set of targets in a versatile aircraft.",
                "从航母起飞，驾驶多用途飞机打击多种目标。");
            Add("After the success of operation Point Blank, the BDF looks to take ground inside Agrapol and establish a new forward base.  Use the versatile VL-49 Tarantula - first in delivering units to capture a location, and then as a gunship to provide heavy fire support.",
                "近距突击成功后，BDF 意图在 Agrapol 推进并建立前进基地。先用 VL-49 Tarantula 投送部队占领要点，再作为武装直升机提供火力支援。");
            Add("An opportunity arises to take possession of a valuable piece of sea mining infrastructure. PALA and BDF try to beat each other to the punch.",
                "争夺宝贵的海上开采设施。PALA 与 BDF 竞相抢先。");
            Add("Fly the A-19 Brawler in a demanding, high tempo close air support mission as BDF narrowly defend Maris Airport against a massive PALA armoured assault.",
                "驾驶 A-19 Brawler 执行高强度近距空中支援，协助 BDF 在 Maris 机场抗击 PALA 大规模装甲突击。");
            Add("Use the Alkyon AB-4 to penetrate heavy enemy air defences and strike a BDF naval base.",
                "驾驶 Alkyon AB-4 突破严密防空，打击 BDF 海军基地。");
            Add("Learn how to manage throttle, brake and rudder to safely taxi to the runway, before conducting your first take off. For this lesson you will be piloting the T/A-30 Compass trainer aircraft.",
                "学习油门、刹车与方向舵，安全滑行至跑道并完成首次起飞。本课驾驶 T/A-30 Compass 教练机。");
            Add("Learn how to prepare for and perform a landing. For this lesson you will be piloting the T/A-30 Compass trainer aircraft.",
                "学习准备与执行着陆。本课驾驶 T/A-30 Compass 教练机。");
            Add("Learn how to designate targets and use missiles to destroy them. For this lesson you will be piloting the T/A-30 Compass trainer aircraft.",
                "学习指定目标并用导弹摧毁它们。本课驾驶 T/A-30 Compass 教练机。");
            Add("Learn how to use flares to defeat IR missiles. For this lesson you will be piloting the T/A-30 Compass trainer aircraft.",
                "学习使用热焰弹对抗红外导弹。本课驾驶 T/A-30 Compass 教练机。");
            Add("Learn how to defeat semi-active radar homing missiles. For this lesson you will be piloting the T/A-30 Compass trainer aircraft.",
                "学习对抗半主动雷达制导导弹。本课驾驶 T/A-30 Compass 教练机。");
            Add("An enormous, high intensity war of attrition with all airbases active and using all available assets.",
                "高强度消耗战：全部机场启用，动用一切可用资产。");
            Add("A campaign of island hopping, in which BDF and PALA fight for control of the Ignus Archipelago.",
                "岛屿跃进战役：BDF 与 PALA 争夺 Ignus 群岛控制权。");
            Add("A pure air & sea battle between two carrier groups. Boscali's Annex Class Assault Carrier vs Primeva's Hyperion Class Fleet Carrier, with their respective compliments of aircraft.",
                "两支航母编队的纯海空对决：Boscali 的 Annex 级攻击航母对阵 Primeva 的 Hyperion 级舰队航母。");
            Add("A battle for air superiority fought between two large airbases, using high-end assets. Rank 3 aircraft available at start.",
                "两大机场之间的制空权争夺，使用高端装备。开局即可使用 3 级飞机。");
            Add("A combined-arms battle on the eastern half of the map, limited to smaller aircraft. Each side has one main airbase and one forward airbase, with naval assets watching the flanks.",
                "地图东半部的联合作战，限用较小飞机。各方一座主机场与一座前进机场，海军监视侧翼。");
            Add("Back to basics with bombs & guns on a CI-22 Cricket in a low-intensity engagement.",
                "回归基础：驾驶 CI-22 Cricket，在低强度交战中用炸弹与航炮作战。");
            Add("Infiltrate and destroy a PALA island stronghold under the cover of a raging storm, using the the VT-7 Vagrant.",
                "在暴风掩护下，驾驶 VT-7 Vagrant 渗透并摧毁 PALA 岛屿据点。");
            Add("In preparation for a large air raid, use the SAH-46 Chicane to neutralise several long range SAM systems.",
                "大规模空袭前，驾驶 SAH-46 Chicane 清除多处远程防空导弹系统。");
            Add("Challenging co-op mission where PALA provokes the BDF navy, and must defend Vigil Cay airbase from a naval attack.",
                "高难度协作：PALA 挑衅 BDF 海军，并需防守 Vigil Cay 机场免遭海上进攻。");
        }

        private static void Add(string en, string zh)
        {
            if (string.IsNullOrEmpty(en) || string.IsNullOrEmpty(zh))
                return;
            Map[en] = zh;
        }

        private static void LoadOptionalTsv()
        {
            try
            {
                string dir = Path.Combine(Paths.PluginPath, "OritasyZh");
                string path = Path.Combine(dir, "game_zh.tsv");
                if (!File.Exists(path))
                {
                    try { Directory.CreateDirectory(dir); }
                    catch { }
                    // Fallback: ship-side assets next to plugin assembly (dev)
                    try
                    {
                        string asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                        string alt = Path.Combine(asmDir ?? "", "OritasyZh", "game_zh.tsv");
                        if (File.Exists(alt))
                            path = alt;
                        else
                            return;
                    }
                    catch { return; }
                }
                int loaded = 0;
                string[] lines = File.ReadAllLines(path, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrEmpty(line) || line[0] == '#')
                        continue;
                    int tab = line.IndexOf('\t');
                    if (tab <= 0 || tab >= line.Length - 1)
                        continue;
                    string en = line.Substring(0, tab).Trim();
                    string zh = line.Substring(tab + 1).Trim();
                    // Allow escaped tab/newline in descriptions
                    zh = zh.Replace("\\t", "\t").Replace("\\n", "\n");
                    if (en.Length > 0 && zh.Length > 0)
                    {
                        Map[en] = zh;
                        loaded++;
                    }
                }
                if (Plugin.Log != null)
                    Plugin.Log.LogInfo("GameZhLocalizer: loaded " + loaded + " overrides from OritasyZh/game_zh.tsv (dict=" + Map.Count + ")");
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("GameZhLocalizer TSV: " + ex.Message);
            }
        }

        private static void TryActivateExternalZhPacks(bool zh)
        {
            try
            {
                if (Chainloader.PluginInfos == null)
                    return;
                foreach (var kv in Chainloader.PluginInfos)
                {
                    if (kv.Value == null || kv.Value.Instance == null)
                        continue;
                    string id = kv.Key ?? "";
                    string name = kv.Value.Metadata != null ? (kv.Value.Metadata.Name ?? "") : "";
                    string blob = (id + " " + name).ToLowerInvariant();
                    bool looksZh = blob.IndexOf("i18n", StringComparison.Ordinal) >= 0
                        || blob.IndexOf("translat", StringComparison.Ordinal) >= 0
                        || blob.IndexOf("chinese", StringComparison.Ordinal) >= 0
                        || blob.IndexOf("汉化", StringComparison.Ordinal) >= 0
                        || blob.IndexOf("中文", StringComparison.Ordinal) >= 0;
                    if (!looksZh)
                        continue;
                    TryInvokeLangToggle(kv.Value.Instance, zh);
                }
            }
            catch { }
        }

        private static void TryInvokeLangToggle(object pluginInstance, bool zh)
        {
            if (pluginInstance == null)
                return;
            Type t = pluginInstance.GetType();
            string[] methods = new string[]
            {
                "SetChinese", "SetLanguage", "SetLang", "EnableChinese", "ApplyLanguage", "SetZh"
            };
            for (int i = 0; i < methods.Length; i++)
            {
                try
                {
                    MethodInfo m = t.GetMethod(methods[i],
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (m == null)
                        continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType == typeof(bool))
                    {
                        m.Invoke(pluginInstance, new object[] { zh });
                        return;
                    }
                    if (ps.Length == 1 && ps[0].ParameterType == typeof(string))
                    {
                        m.Invoke(pluginInstance, new object[] { zh ? "zh" : "en" });
                        return;
                    }
                    if (ps.Length == 0 && zh)
                    {
                        m.Invoke(pluginInstance, null);
                        return;
                    }
                }
                catch { }
            }
        }

        private static void ScanAndApply(bool force)
        {
            EnsureDict();
            Text[] texts;
            try { texts = UnityEngine.Object.FindObjectsOfType<Text>(); }
            catch { return; }
            if (texts != null)
            {
                for (int i = 0; i < texts.Length; i++)
                    ApplyToUnityText(texts[i], force);
            }

            ApplyTmpScan(force);
            ApplyDropdownScan(force);

            if (Touched.Count > 8000)
                Touched.Clear();
            if (OriginalById.Count > 8000)
                OriginalById.Clear();
        }

        private static bool IsOritasyEnglishChrome(Component c)
        {
            try
            {
                if (c == null || c.gameObject == null)
                    return false;
                string n = c.gameObject.name ?? "";
                if (string.IsNullOrEmpty(n))
                    return false;
                string nl = n.ToLowerInvariant();
                return nl.IndexOf("oritasy_note", StringComparison.Ordinal) >= 0
                    || nl.IndexOf("oritasy_guide", StringComparison.Ordinal) >= 0
                    || nl.IndexOf("oritasy_changelog", StringComparison.Ordinal) >= 0;
            }
            catch { return false; }
        }

        private static void ApplyToUnityText(Text t, bool force)
        {
            if (t == null)
                return;
            int id = t.GetInstanceID();
            if (!force && Touched.Contains(id))
                return;
            if (IsOritasyEnglishChrome(t))
            {
                Touched.Add(id);
                return;
            }

            string cur;
            try { cur = t.text; }
            catch { return; }
            if (string.IsNullOrEmpty(cur))
                return;

            string key = cur.Trim();
            string zh = TranslateLookup(key);
            if (zh == key)
            {
                Touched.Add(id);
                return;
            }
            if (!OriginalById.ContainsKey(id))
                OriginalById[id] = cur;
            if (cur != zh)
            {
                try { t.text = zh; }
                catch { return; }
            }
            Touched.Add(id);
        }

        private static void ApplyDropdownScan(bool force)
        {
            Dropdown[] drops;
            try { drops = UnityEngine.Object.FindObjectsOfType<Dropdown>(); }
            catch { return; }
            if (drops == null)
                return;
            for (int i = 0; i < drops.Length; i++)
            {
                Dropdown d = drops[i];
                if (d == null || d.options == null)
                    continue;
                int id = d.GetInstanceID();
                if (!force && Touched.Contains(id))
                    continue;
                bool any = false;
                for (int o = 0; o < d.options.Count; o++)
                {
                    Dropdown.OptionData opt = d.options[o];
                    if (opt == null || string.IsNullOrEmpty(opt.text))
                        continue;
                    string zh = TranslateLookup(opt.text.Trim());
                    if (zh != opt.text)
                    {
                        opt.text = zh;
                        any = true;
                    }
                }
                if (any)
                {
                    try { d.RefreshShownValue(); }
                    catch { }
                }
                Touched.Add(id);
            }
        }

        private static void RestoreAllKnown()
        {
            Text[] texts;
            try { texts = UnityEngine.Object.FindObjectsOfType<Text>(); }
            catch { texts = null; }
            if (texts != null)
            {
                for (int i = 0; i < texts.Length; i++)
                {
                    Text t = texts[i];
                    if (t == null)
                        continue;
                    int id = t.GetInstanceID();
                    string orig;
                    if (!OriginalById.TryGetValue(id, out orig))
                        continue;
                    try { t.text = orig; }
                    catch { }
                }
            }
            RestoreTmpAll();
            OriginalById.Clear();
            Touched.Clear();
        }

        private static void ResolveTmp()
        {
            if (_tmpResolved)
                return;
            _tmpResolved = true;
            try
            {
                _tmpType = Type.GetType("TMPro.TMP_Text, Unity.TextMeshPro")
                    ?? Type.GetType("TMPro.TextMeshProUGUI, Unity.TextMeshPro");
                if (_tmpType != null)
                    _tmpTextProp = _tmpType.GetProperty("text",
                        BindingFlags.Instance | BindingFlags.Public);
            }
            catch { }
        }

        private static void ApplyTmpScan(bool force)
        {
            ResolveTmp();
            if (_tmpType == null || _tmpTextProp == null)
                return;
            ChineseTmpFontService.EnsureReady();
            UnityEngine.Object[] objs;
            try { objs = UnityEngine.Object.FindObjectsOfType(_tmpType); }
            catch { return; }
            if (objs == null)
                return;
            for (int i = 0; i < objs.Length; i++)
            {
                UnityEngine.Object o = objs[i];
                if (o == null)
                    continue;
                Component c = o as Component;
                if (c != null && IsOritasyEnglishChrome(c))
                {
                    Touched.Add(o.GetInstanceID());
                    continue;
                }
                int id = o.GetInstanceID();
                if (!force && Touched.Contains(id))
                    continue;
                string cur;
                try { cur = _tmpTextProp.GetValue(o, null) as string; }
                catch { continue; }
                if (string.IsNullOrEmpty(cur))
                    continue;
                string key = cur.Trim();
                string zh = TranslateLookup(key);
                if (zh == key)
                {
                    Touched.Add(id);
                    continue;
                }
                if (!OriginalById.ContainsKey(id))
                    OriginalById[id] = cur;
                if (cur != zh)
                {
                    try { _tmpTextProp.SetValue(o, zh, null); }
                    catch { continue; }
                }
                ChineseTmpFontService.ApplyToTmpInstance(o);
                Touched.Add(id);
            }
        }

        private static void RestoreTmpAll()
        {
            ResolveTmp();
            if (_tmpType == null || _tmpTextProp == null)
                return;
            UnityEngine.Object[] objs;
            try { objs = UnityEngine.Object.FindObjectsOfType(_tmpType); }
            catch { return; }
            if (objs == null)
                return;
            for (int i = 0; i < objs.Length; i++)
            {
                UnityEngine.Object o = objs[i];
                if (o == null)
                    continue;
                int id = o.GetInstanceID();
                string orig;
                if (!OriginalById.TryGetValue(id, out orig))
                    continue;
                try { _tmpTextProp.SetValue(o, orig, null); }
                catch { }
            }
        }

        /// <summary>
        /// Mutate Encyclopedia UnitDefinition / WeaponInfo display fields so hangar,
        /// selection lists, and encyclopedia browser show ZH without waiting on UI scan.
        /// </summary>
        private static void ApplyEncyclopediaDefs(bool zh)
        {
            EnsureDict();
            if (!zh)
            {
                RestoreEncyclopediaDefs();
                // Restore may put vanilla names back — re-apply XE brands in English.
                try { Plugin.RefreshEncyclopediaAircraft(); }
                catch { }
                return;
            }
            try
            {
                ApplyUnitDefinitionList();
                ApplyWeaponInfoList();
            }
            catch (Exception ex)
            {
                if (Plugin.Log != null)
                    Plugin.Log.LogWarning("GameZhLocalizer encyclopedia: " + ex.Message);
            }
        }

        private static void ApplyUnitDefinitionList()
        {
            UnitDefinition[] all;
            try { all = Resources.FindObjectsOfTypeAll<UnitDefinition>(); }
            catch { return; }
            if (all == null)
                return;
            for (int i = 0; i < all.Length; i++)
            {
                UnitDefinition def = all[i];
                if (def == null)
                    continue;
                int id = def.GetInstanceID();
                EncSnapshot snap;
                if (!EncSnapshots.TryGetValue(id, out snap))
                {
                    snap.UnitName = def.unitName;
                    snap.Code = def.code;
                    snap.Description = def.description;
                    snap.BogeyName = def.bogeyName;
                    snap.WeaponName = null;
                    snap.ShortName = null;
                    EncSnapshots[id] = snap;
                }

                AircraftDefinition ad = def as AircraftDefinition;
                UnitBrandingService.XeBrand brand;
                if (ad != null && UnitBrandingService.TryResolveBrand(ad, out brand))
                {
                    // Brand owns title, code, and full Veyrn lore (EN/ZH).
                    snap.UnitName = brand.NameEn;
                    snap.Code = brand.Code;
                    snap.Description = brand.DescEn;
                    EncSnapshots[id] = snap;
                    UnitBrandingService.ApplyBrandFields(ad, brand);
                    if (!string.IsNullOrEmpty(snap.BogeyName))
                    {
                        string t = TranslateLookup(snap.BogeyName.Trim());
                        if (t != snap.BogeyName)
                            def.bogeyName = t;
                    }
                    continue;
                }

                string baseName = snap.UnitName ?? def.unitName;
                string baseDesc2 = snap.Description ?? def.description;
                string baseBogey = snap.BogeyName ?? def.bogeyName;

                if (!string.IsNullOrEmpty(baseName))
                {
                    string t = TranslateLookup(baseName.Trim());
                    if (t != baseName)
                        def.unitName = t;
                }
                if (!string.IsNullOrEmpty(snap.Code))
                    def.code = snap.Code;
                if (!string.IsNullOrEmpty(baseDesc2))
                {
                    string bare = baseDesc2;
                    EncyclopediaBrandService.StripXeBrandLines(ref bare);
                    string next = TranslateLookup(bare.Trim());
                    def.description = next;
                }
                if (!string.IsNullOrEmpty(baseBogey))
                {
                    string t = TranslateLookup(baseBogey.Trim());
                    if (t != baseBogey)
                        def.bogeyName = t;
                }
            }
        }

        private static void ApplyWeaponInfoList()
        {
            WeaponInfo[] all;
            try { all = Resources.FindObjectsOfTypeAll<WeaponInfo>(); }
            catch { return; }
            if (all == null)
                return;
            for (int i = 0; i < all.Length; i++)
            {
                WeaponInfo w = all[i];
                if (w == null)
                    continue;
                int id = w.GetInstanceID();
                EncSnapshot snap;
                if (!EncSnapshots.TryGetValue(id, out snap))
                {
                    snap.UnitName = null;
                    snap.Description = w.description;
                    snap.BogeyName = null;
                    snap.WeaponName = w.weaponName;
                    snap.ShortName = w.shortName;
                    EncSnapshots[id] = snap;
                }

                if (!string.IsNullOrEmpty(snap.WeaponName))
                {
                    string t = TranslateLookup(snap.WeaponName.Trim());
                    if (t != snap.WeaponName)
                        w.weaponName = t;
                }
                if (!string.IsNullOrEmpty(snap.ShortName))
                {
                    string t = TranslateLookup(snap.ShortName.Trim());
                    if (t != snap.ShortName)
                        w.shortName = t;
                }
                if (!string.IsNullOrEmpty(snap.Description))
                {
                    string t = TranslateLookup(snap.Description.Trim());
                    if (t != snap.Description)
                        w.description = t;
                }
            }
        }

        private static void RestoreEncyclopediaDefs()
        {
            if (EncSnapshots.Count == 0)
                return;
            try
            {
                UnitDefinition[] units = Resources.FindObjectsOfTypeAll<UnitDefinition>();
                if (units != null)
                {
                    for (int i = 0; i < units.Length; i++)
                    {
                        UnitDefinition def = units[i];
                        if (def == null)
                            continue;
                        EncSnapshot snap;
                        if (!EncSnapshots.TryGetValue(def.GetInstanceID(), out snap))
                            continue;
                        if (snap.UnitName != null)
                            def.unitName = snap.UnitName;
                        if (snap.Code != null)
                            def.code = snap.Code;
                        if (snap.Description != null)
                            def.description = snap.Description;
                        if (snap.BogeyName != null)
                            def.bogeyName = snap.BogeyName;
                    }
                }
                WeaponInfo[] weapons = Resources.FindObjectsOfTypeAll<WeaponInfo>();
                if (weapons != null)
                {
                    for (int i = 0; i < weapons.Length; i++)
                    {
                        WeaponInfo w = weapons[i];
                        if (w == null)
                            continue;
                        EncSnapshot snap;
                        if (!EncSnapshots.TryGetValue(w.GetInstanceID(), out snap))
                            continue;
                        if (snap.WeaponName != null)
                            w.weaponName = snap.WeaponName;
                        if (snap.ShortName != null)
                            w.shortName = snap.ShortName;
                        if (snap.Description != null)
                            w.description = snap.Description;
                    }
                }
            }
            catch { }
        }

        internal static void NotifyEncyclopediaLoaded()
        {
            // AfterLoad + ApplyAll both fire this; one deferred pass is enough.
            int f = Time.frameCount;
            if (f == _encNotifyFrame)
                return;
            _encNotifyFrame = f;
            if (UiLang.IsChinese)
                ScheduleDeferredZh(0.35f);
        }
    }

    // Live Unity UI intercept removed (was a hitch on every Text/TMP assignment).
    // Oritasy menus still use UiLang / GameZhLocalizer.T; encyclopedia defs translate once.
}
