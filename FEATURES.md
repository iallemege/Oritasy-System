# Oritasy 功能说明 / Feature Catalog

> **Oritasy™** — Nuclear Option BepInEx 玩法整合框架（非整包 RDA）  
> GUID: `com.iallemmege.oritasy` · Legacy: `com.qiaochen.oritasy`  
> 主程序集: `Oritasy_*.dll`（合并包，内嵌 WeXon + TGM-85/Kh85MT）  
> 命名空间: `Oritasy` · `WeXon` · `Kh85MT` · `OritasyCdk`

| 项 Item | 值 Value |
|--------|----------|
| 公开源码仓 | [iallemege/Oritasy-System](https://github.com/iallemege/Oritasy-System)（Standard C） |
| 文档仓版本 | `0.0.9.193C`（asm `2.9.3`） |
| 用户机常见安装 | `D:\Steam\steamapps\common\Nuclear Option\BepInEx\plugins\` 下 `Oritasy_0.0.9.239C.dll` 等 |
| 附属目录/资源 | `OritasyFonts/` · `OritasyPerf/` · `OritasyReplays/` · `WeXonAssets/`（及可选 `OritasyHud` / `OritasyExtra` 资源夹） |
| 不要同时加载 | 独立 `WeXon.dll` · `Kh85MT.dll` · `OritasyAir.dll` · `UnrestrictedWeapons` 重叠 DLL |

本文以**功能目录**为主（中英对照）。玩法细则见仓内 [GUIDE.md](GUIDE.md)。**不含 RDA**（`com.iallemmege.RDA` 为独立雷达模组）。

---

## 1. 总览 Overview

Oritasy 是单 DLL 合并框架：改装单位（XE/NE/TE）、飞行辅助、HUD/MFD、武器（WeXon + TGM-85）、生涯档案、战略支援、性能探针、中英 UI、动态音乐与飞行记录等。入口 `Oritasy.Plugin`（`[BepInPlugin]`），Awake 内挂载托管的 `Kh85MT.Plugin.StartHosted` 与 `WeXon.Plugin.StartHosted`，共用 Harmony id = GUID。

---

## 2. 热键一览 Hotkeys

多数菜单 **Esc** 关闭；同一时间通常只开一个功能菜单。

| 键 Key | 功能 Feature |
|--------|----------------|
| **F1** | Oritasy 系统 / 飞行评分（包线·推力·武器开关） |
| **F2** | 自动驾驶 Autopilot（平飞 / 盘旋 / 着陆机场 / 着陆航母） |
| **F3** | 新手辅助 Beginner Assist（起飞 / 坠毁守护 / 地形回避） |
| **F4** | ILS 下滑道设置 |
| **F5** | 私信 / 转账 |
| **F6** | 击杀奖励盲盒 |
| **F7** | 房主发钱（仅主机） |
| **F8** | 主菜单：**生涯档案 Profile**；飞行中还可切尾追 HUD / 机体损伤图相关 |
| **F9** | 战略支援 Strategic Arsenal（WeXon） |
| **F10** | 空中补给 / 维修 |
| **F11** | Oritasy RWR 圆盘布局 |
| **Delete** | 导弹画中画 PiP |
| **Insert** | 手动开导弹（人在回路） |
| **PageDown** | 人操 / 画中画换下一枚导弹 |
| **\\\\** | 导弹手 Oritasy HUD（SPD/HDG/ACC/G + 圆盘 RWR） |
| **`** (BackQuote) | PerfProbe 性能菜单 |
| **P** | 僚机 Wingman 编队/解除（锁友机后，最多 7） |
| **;** / **'** | 自动驾驶模式循环 |
| **[** / **]** | F9 支援条目切换 |
| **Backspace** | 取消空中补给 |
| **Tab** | 本局计分板（K/D/R + 徽章） |
| **Esc** | 关菜单 / 退出手动导弹 |

主菜单隐藏开发面板：**↑↑↓↓←←→→ A A B B**（`OritasyDevMenu`）。

---

## 3. 单位与图鉴 Units & Encyclopedia

| 类别 | 标记 | 说明 |
|------|------|------|
| 飞机 | **XE** · `[Oritasy]` | 推力 / 燃油 / 起落架 / 飞行包线可调（F1） |
| 舰船 | **NE** · `[Thanos]` | 动力等加强 |
| 地面载具 | **TE** · `[Unitas]` | 含 AAA / 155 对空等调参 |
| 建筑 | `[Bexur]` | 图鉴品牌 |

- 核弹冲击 / 爆伤抗性分档（飞机、舰、建筑、车辆）  
- 创意工坊皮肤若绑原版 GUID，可能无法套到改装单位  
- CI-22 另有专用燃油 / 载荷 / 起落架倍率与显示名覆盖  

---

## 4. 菜单与系统 Menus

### 4.1 F1 — Oritasy 系统 `AircraftManeuverGui`

- **系统**页：过载 / 速度 / 拐角 / 进近着陆起飞速度 / 转弯半径、飞行员耐力、俯仰滚转迎角限制、推力与燃油；武器开关（多模式、IFF、自由猎杀、IAL 核半径减半）、导弹镜头 / 人操、HUD 开关；动态音乐 / 导弹音效开关  
- **飞行评分**页：嵌入 `WeXon.FlightAnalysis` 报告（坠毁/离机可自动翻页）  
- 未坐上 XE 时仅能改全局开关  

### 4.2 F2 — 自动驾驶 `PlayerAutopilot`（Autopilot / “WSO” 飞行辅助）

模式：

1. **平飞** — 设定雷达高 + 速度  
2. **盘旋** — 绕圆心 orbit  
3. **着陆机场** — ILS 下滑道，可选最近/指定跑道  
4. **着陆航母** — 甲板进近  

可选试验：**导弹规避**（红外诱饵 / 雷达切口）。接通后视角仍可自由。与 F3 着陆职责分离（着陆请用 F2）。

### 4.3 F3 — 新手辅助 `BeginnerAssist`

- 自动起飞（跑道/障碍检查）  
- 坠毁守护（失速、螺旋、倒扣、高空飘叶、薄空气加力、STOVL 喷口）  
- 地形回避  

### 4.4 F4 — ILS 设置 `IlsSettingsMenu`

下滑道 **3°–8°**（默认约 5°），供 F2 着陆与起落架放下后的 ILS 指针共用。

### 4.5 F5 — 私信 / 转账 `PrivateMessageMenu`

选玩家短消息；经费转账需对方确认（主机侧协议 `#OPM#` / `#OTF#` …）。

### 4.6 F6 — 击杀奖励 `KillChoiceMenu`

空中击杀阈值攒抽取次数；三格盲选。可抽机体折扣、免费导弹、限制 AI 军衔、解锁本局 F9 **高级/战略**档等。

### 4.7 F7 — 房主发钱 `HostFundMenu`

仅主机：给任意玩家加经费，不扣自己。

### 4.8 F8 — 生涯档案 `WeXon.PlayerCareer`

分栏：**档案 / 统计 / 服务器 / 最近15局 / 徽章 / 关于**  

档案内可调：语言、性能档、无限制挂点、动态音乐（实验）、飞行分析、AI Brain 等级、已解锁徽章等。

### 4.9 F9 — 战略支援 `WeXon.StrategicArsenal`

经费购买支援弹或友军航母。支援从高空下落，高高度舵面锁定，入大气后机动。部分条目需本局连杀解锁（常驻 / 高级 / 战略 / 航母）。

### 4.10 F10 — 空中补给 `AerialResupply` + `ComponentRepair`

- 远程补燃油/电池（慢/中/快）  
- 修部件 / 发动机 / 油箱；灭火 / 全修等  
- 补给中保持平飞；**Backspace** 取消  

### 4.11 F11 — RWR 布局 `WarThunderRwrHud` / `AircraftRwrService`

拖放 **Oritasy RWR** 圆盘位置与尺寸。

### 4.12 PerfProbe **`** `PerfProbeMenu` / `PerfProbeService`

页签：子系统 / Harmony / GC-Alloc / Scene / Compare  

- 采样 FPS、尖峰、GC、Harmony owner、场景普查  
- 报告写入 `BepInEx/plugins/OritasyPerf/perf_v2_*.txt`  
- 可选重 Harmony 计时采样（默认关）  
- `PerfMode`：低/中/高/自动（压水汽/引擎画质，**不关**雷达 MFD / 导弹镜头 / 尾追 HUD）  
- `OritasyWorker`：文件/WAV 等后台队列  

---

## 5. HUD · 显示 · 镜头 Presentation

| 模块 | 说明 |
|------|------|
| `OritasyHud` | 仅**手动导弹**时：底栏 SPD/HDG/ACC/G + 可选圆盘 RWR（`\\\\` 开关） |
| `AircraftChaseNoseHud` | 第三人称追逐机头叠加（F8 相关开关） |
| `GMeterHud` | 右侧 G 力表 |
| `StatusDisplayHud` | 强化状态显示 |
| `AirframeWearGui` / Schematic | 右下机体损伤图（绿/黄/红/品红） |
| `AirportIlsHud` | 起落架放下：机场安全圈 + ILS 交叉指针 |
| `RocketCcipHud` | 火箭解保后 CCIP（可选 CEP） |
| `RadarMfdOverlay` | 雷达开时 TacScreen 叠扫描/杂波/回波 MFD |
| `MissileCameraHud` | Delete PiP；Insert 全屏人操（WASD 转向、Q/E 油门） |
| `MatchScoreboard` | Tab 双方空战 KDR + 成就缩写 |
| `FriendlyKillHud` | 误伤相关提示 |
| `OritasyPresentation` | 开机 Splash、Help/Guide、Notes、品牌 chip、UI 缩放 |

---

## 6. 字体 · 中文 Fonts & ZH

| 模块 | 路径 / 行为 |
|------|-------------|
| `ChineseFontPatch` | `plugins/OritasyFonts/*.ttf|otf|ttc`（如 NotoSansSC）注册到 IMGUI |
| `OritasyCjkAssetPack` / `ChineseTmpFontService` | `plugins/OritasyFonts/OritasyCJK` UnityFS → TMP 中文字体 |
| `GameZhLocalizer` + `ZhLookupService` + `UiLang` | 游戏内字符串与模组 UI 中英切换 |
| `JoinMenuFactionFix` | 加入菜单阵营切换修复 |

安装侧常见目录名：**OritasyFonts**（资源，非第二 GUID 插件）。

---

## 7. 音乐 · 电台 Music & Radio

| 模块 | 说明 |
|------|------|
| `DynamicMusic` + MoodService | 实验动态 BGM：menu/start/tactical/strategic/combat/takeoff/victory/defeat；档案里开启 |
| `MissileAudio` | 导弹相关音效开关 |
| `AceRadioChatter` + `AceRadioBank` + `RadioMapPlace` | 王牌战争风格邻近 AI 电台字幕；地标填充 `{L}` |

---

## 8. AI · “NN” 战斗脑 · 僚机

| 模块 | 说明 |
|------|------|
| `AiCombatBrain` | **完整替换** AI 空战脑，Career 可选 **1–5 级**（Hunt/Attack/BreakOff/Evade…）；可只影响敌对 AI、可选直升机 |
| `AiCombatSmarts` | 旧版软调（技能/勇气/拦截等），默认关，建议用 Brain |
| `AiCombatAcm*` / Evade / Engagement / Math | Brain 子系统 |
| `WingmanService` | **P**：锁友机编为僚机（最多 7），金色 `[W]` 标记，跟随并优先打玩家当前敌锁 |

> 用户需求中的 **Autopilot/WSO**：对应 **F2 自动驾驶** + **僚机/武器系统侧辅助**（Wingman + 导弹人操/PiP）。  
> **NN**：对应 **AiCombatBrain** 多级战斗脑（非独立 ML 框架）。

---

## 9. 飞行记录 / Replays

| 模块 | 说明 |
|------|------|
| `WeXon.FlightAnalysis` | 档案开启后采样本机飞行，结束写 **`plugins/OritasyReplays/`**（CSV + 分析报告） |
| `FlightRecordGateService` / `FlightTrackMap` | 启停采样、航迹图、自动评分门槛 |
| `FlightScoreBridge` | F1 嵌入评分 UI；坠毁/离机触发 |

默认 **关**（性能）；在 Profile 打开「飞行分析」。若存在独立 `FlightRecorder.dll`，会 soft-yield。

---

## 10. 武器 Weapons（合并 WeXon + Kh85）

- 多模式寻的、LOAL、IFF、脱锁自由猎杀  
- **ACM-119 [IAL]** 常规集束 · **ACNM-118 [IAL] [1.5kt]** 核集束 · **AAM-2CV** 等  
- **TGM-85** A/B/C/D/E/S（含 Coordinator 箔条/诱骗、Torjan 诱饵等）  
- 舰射导弹、近炸引信、过穿、高空导弹冻结、F9 高空投放飞行律  
- 生涯 **无限制挂点**（仅本地玩家）  
- 资源：`BepInEx/plugins/WeXonAssets/`  

请勿再放独立 `WeXon.dll` / `Kh85MT.dll`。

---

## 11. 其它飞机 / 地面辅助 Aircraft & ground helpers

| 模块 | 说明 |
|------|------|
| `FlightEnvelopeService` / `AircraftPowerService` / `AirframeStrengthService` | 包线与结构 |
| `HighAltEngineAssist` / `EngineStartService` / `ReverseThrustService` / `PropFeatherService` / `AirbrakeDragService` | 动力与气动辅助 |
| `LandingGuidance` / `LandingMath*` / STOVL·VTOL·CV gate | 着陆律 |
| `AirportDefection` / `AirbaseLocator` / `CarrierAb4DeckSpawnService` | 机场/航母 |
| `NukeResist*` | 核冲击抗性 |
| `AaaHitService` / `ArtilleryAaService` / `GroundFleetService` / `ShipFleetService` / `VehicleCookoffService` | 地面与舰 |
| `GamepadLookService` / `PitchStickFilterService` / `MotionInterpService` | 操控与插值 |
| `OritasyWorker` / `OritasyWavPcm` / `OritasyAntiTamper` / `OritasyCursor` | 运行时基建 |

---

## 12. 插件入口与构建 Plugin entry & build

```text
[BepInPlugin("com.iallemege.oritasy", "Oritasy"|"RM278", "2.9.3")]
Oritasy.Plugin  →  SharedHarmony(GUID)
                →  TryStartHostedKh85()
                →  TryStartHostedWeXon()
```

源码布局（Standard C 仓）：

```text
CI22XE/src/          Oritasy 主体（Plugin*.cs、HUD、AP、Perf…）
NOWeaponSuite/src/   WeXon（Career、StrategicArsenal、FlightAnalysis、武器…）
Kh85MT/src/          TGM-85
tools/ + build_c.bat 合并编译 → Oritasy_0.0.9.193C.dll（ORITASY_COMBINED）
```

本地开发树常见路径：`G:\8\模板项目-034\ExportedProject\BepInExMods\`（CI22XE 等）。

---

## 13. 安装侧目录约定 Installed layout

```text
Nuclear Option/BepInEx/plugins/
  Oritasy_0.0.9.xxxC.dll     ← 主插件（版本随构建）
  OritasyFonts/              ← CJK TTF + OritasyCJK bundle
  OritasyPerf/               ← PerfProbe 报告输出
  OritasyReplays/            ← 飞行分析输出（开启时）
  WeXonAssets/               ← TGM-85 等模型贴图
  （可选）OritasyHud / OritasyExtra  ← 资源或实验夹；核心 HUD 逻辑已在主 DLL
```

配置：`BepInEx/config/com.iallemmege.oritasy.cfg`（旧 GUID 会经 `ConfigGuidMigrate` 迁移）。

---

## 14. 明确不在本文范围 Out of scope

- **RDA**（`com.iallemmege.RDA`）独立雷达 — 不是 Oritasy 功能  
- **BGM767** 等其它独立武器 DLL  
- Dagger Directive 版 Oritasy（`iallemmege/Oritasy` 闭源 BepInEx 6）  

---

## 15. 文档版本

- 生成依据：GitHub `Oritasy-System` Standard C 源码 + `GUIDE.md` + `README.md`  
- 用户机若为 **0.0.9.239C**，功能面应兼容本目录，个别热键/实验项以该 DLL 与 cfg 为准  
- 生成时间：2026-09-19（Asia/Shanghai）

---

# English summary

Oritasy is a **combined BepInEx pack** for Nuclear Option: XE/NE/TE unit retunes, F1–F11 menus (system, autopilot, beginner assist, ILS, PM/transfer, kill rewards, host funds, career profile, strategic arsenal, aerial resupply, RWR layout), PerfProbe (`` ` ``), missile PiP/manual pilot, HUDs (chase, G, CCIP, radar MFD, wear schematic), CJK fonts + zh localizer, dynamic music + ace radio, AiCombatBrain tiers + wingmen (P), flight recorder under `OritasyReplays/`, and hosted WeXon/TGM-85 weapons. Do **not** load standalone WeXon/Kh85. This catalog is **not** RDA.
