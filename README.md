# OritasySS

OritasySS 0.1.3β. This is the latest rework of Oritasy.

本仓库是 Oritasy 的源码。飞机包在 `CI22XE`，武器包在 `NOWeaponSuite`。不包含 TGM-85。

## 功能介绍

- 本模组中的飞机、车辆和建筑经过修改，并且可以完全自定义。
- 由于与游戏原版的 GUID 不同，创意工坊涂装可能无法使用。
- PHOBOS 是界面：可缩放面板、全名按键、筛选和预留槽位。
- M 地图叠层是 GRS：网络、卫星、机库、电子战、情报网、设置。
- 电子战：扫描、突破、植入、干扰、外传；追踪并反制敌方入侵；特工与扇区情报。地图上有数据包、弧线和脉冲环。
- 特殊装备与舰船（卫星和航母，无冷却）在 F1 菜单的装备页。
- 菜单需要足够带宽。左侧 HUD 是原版字体的带宽矩阵，并带入侵警报。
- 无线电呼号带有军种：▲ 空军，■ 陆军，● 海军。
- 中文界面覆盖按键和菜单；无线电、剧情和新闻默认保持英文。
- 敌方卫星升空后即可看见并锁定。
- 命名：飞机 XE / [Oritasy]，舰船 NE / [Thanos]，车辆 TE / [Unitas]，建筑 [Bexur]。
- 左上角的帮助按钮可以再次打开本说明。

### 按键（飞行中）

- F1 是 Oritasy 菜单：自动驾驶、发动机、装备。F2 是 PHOBOS。F3 是无线电编解码。
- 主菜单上的 F1 是生涯档案，含战役存档和新战役设置。
- 右侧提示条打开同样的面板。M 在原版地图上打开 GRS。
- C 是僚机指令环。COVER / FORM / PUSH / HOLD 会真正飞行和射击。
- Insert 是手动导引。Delete 是导弹相机画中画。

### 档案

经验、击杀、徽章、语言和性能。战役页可以开始新战役（开局、呼号和乘员每局选定一次）、继续，或把任意存档、章节直接载入任务。存档键与旧包的 WeXon.Career 相同。

## Features

- Aircraft, vehicles and buildings in this mod are modified and fully customizable.
- Because the GUID differs from the original game, a workshop skin may not apply.
- PHOBOS is the UI: scaled panels, full-name keys, filters, reserved slots.
- The M map overlay is GRS: Network, Satellite, Hangars, Electronic Warfare, Intelligence Network, Settings.
- Electronic warfare: sweep, scan, breach, implant, disrupt, exfiltrate; trace and counter enemy intrusions; agents and sector intelligence. Packets, arcs and pulse rings are drawn on the map.
- Special equipment and ships (satellites and carriers, no cooldown) are the Equipment page of the F1 menu.
- Menus need enough bandwidth. The left HUD is a vanilla-font bandwidth matrix with an intrusion alert.
- Radio names carry their branch: ▲ air, ■ army, ● navy.
- The Chinese interface covers keys and menus. Radio, story and news stay in English unless Chinese story text is turned on.
- Enemy satellites are visible and lockable as soon as they are up.
- Branding: aircraft XE / [Oritasy], ships NE / [Thanos], vehicles TE / [Unitas], buildings [Bexur].
- The help chip at the top left reopens this guide after the first dismiss.

### Keybinds (in flight)

- F1 is the Oritasy menu: Autopilot, Engine, Equipment. F2 is PHOBOS. F3 is the radio codec.
- F1 on the main menu is the Career Profile, with campaign saves and the new-campaign setup.
- The tip strip on the right opens the same panels. M opens GRS on the vanilla map.
- C is the wing command ring. COVER / FORM / PUSH / HOLD actually fly and shoot.
- Insert is manual missile guidance. Delete is the missile camera picture-in-picture.

### Profile

XP, kills, badges, language and performance. The Campaign tab starts a new campaign (opening, callsign and crew are picked once per run), continues, or loads any save or chapter load point straight into the mission. It uses the same WeXon.Career save keys as the old pack.

## 美术素材

所有美术素材都是已获授权的素材，或作者自制的素材。这些文件不放在本仓库里。

All art assets are either used with authorization or made by the author. Those files are not in this repository.

已单独注明来源的部分：

- 色彩分级、.cube LUT 和 RCAS 锐化改编自 Boscali Summer（MIT，GrabowMar）。
- EOS-02 MRISA 卫星模型和贴图来自模组作者提供的 VTE AirObjects。
- 中文界面字体 Noto Sans SC 由 Adobe 与 Google 以 SIL Open Font License 1.1 发布。

## 更新说明

下面是 OritasySS 自带说明里的更新记录。


# OritasySS v0.1.3β

Pre-release. OritasyCommander calls the breaking sector. A negative reward corrects the weights at once and the order you hear is the corrected direction. After a multiplayer match, or when the game closes after one, the tail of the BepInEx log is uploaded and the link is kept in BepInEx/cache/OritasySS/log-link.txt. The call starts with "Commander calling" and the speaker name is Commander. A meteor rain is a rain of the game's bombs, after the clock runs forward to evening, and the sky fills until the sun is covered. On the M map the network, satellites and hangars are one Theater button in the same dark tabs as the other menus. Space War is UESC against EUC for the last planet, which both navies treat as the Proving Ground (Drain) and shell without picking sides. Hulls read like UESCN BB Nordin. The chart shows each ship's heading. Ships sit at or above 160 km. Inertia is heavy, so reverse thrust is slow and turns are wide. Fleets trade lasers you can see by looking up at night, and they sweep and gatling the ground. The tactics network and Commander have their own Neural Network page. Reverse thrust again cuts the nozzle's forward push, holds a prop at fine pitch in the air, and cancels prop pull on the ground.

# OritasySS v0.1.3

OritasyCommander reads the whole front and calls the player, by heading and range, to the sector that is breaking and hard to reach. Friendlies are tasked there. Enemy depots push a mass of air defence into that same ground, factory output rises with the player's kill rate and the value of the jet they are flying, and those guns prefer the player.

# OritasySS v0.1.2

The hidden-settings code is gone. Flight tuning, supply, landing, display and the profile switches are on the menu from the start. Lighting, the sky, the clock and hiding the UI also live in the separate Oritasy's Shader mod (F2). When that mod is loaded it owns those effects, so they are not applied twice.

# OritasySS v0.1.1

Full release. Everything from the 0.1.1β and 0.1.2δ pre-releases is in it, plus the changes below. Unzip into `BepInEx/plugins/` (the zip holds BIA Runtime 1.0.29, the new version of BIA Runtime 1.0.25, and BIA 1.0.25(SS); delete an old BIA.Runtime_1.0.25.dll). The hidden settings still open with ↑ ↑ ↓ ↓ ← → ← → B A B A in F1 › Hidden settings or on the Profile page.

New:
- XE / TE / NE modified switches at the top of F1 and of the Profile page. XE turns the modified aircraft (XE names, thrust, airframe strength, fly-by-wire tuning) on or off, TE the modified ground vehicles (TE names, [Unitas], faster driving), NE the modified ships (NE names, [Thanos], faster ships). A change holds from the next start of the game.
- Ground vehicles carry cold-gas RCS, after the new Tesla Roadster: ten thrusters round the hull, four under the corners, a pair at each end and one each side. In the air they hold the vehicle level with small, fast corrections and let it down gently from a short fall. The white jets fire the way the vehicle is actually accelerating and turning, with a hiss and a ring of dust near the ground. No two vehicles fire alike: jet length and width, pulse rate, the push of each thruster and the tint of the gas are their own.
- Rapid move. Vehicles travelling together form a team, led by the one farthest along. When the way is clear (no enemy vehicle within 3 km), the leader calls a rapid move on the radio and lifts off, flying low along its route for up to 6 km and landing at least 2.5 km short of the nearest enemy ahead. Each member decides for itself: most follow a few seconds later, some only after a while, and some stay on the road. Height, speed and any trick in the air are rolled per vehicle: a flat spin nearly on the spot, a barrel roll, or neither. Friendly teams within 40 km are heard on the radio.
- Some vehicles break when a missile closes on them: they jump across its path and roll, then land on their thrusters. One already flying rolls where it is.
- The AOA warning reads true angle of attack: from airspeed, in the pitch plane, signed. Sideslip no longer counts, and below 30 m/s or on the ground it is not shown.
- Emergency chutes leave vehicles on thrusters alone.
- More light and shadow on the F1 Display page. Scene light scales the game's own sunlight, sky light and reflections, and fog as the day goes round, and sets how dark sun shadows are and how far they are drawn; a button puts it all back. The colour grade gains shadow, midtone and highlight brightness, a shadow tint and a highlight tint with their own hues and a balance, a tone mapping choice (the game's, Neutral or ACES), and bloom threshold and spread when the bloom is your own.
- Hide all UI: F10 in flight (rebindable on the F1 Display page, also a switch there) hides the game's HUD, markers, chat and kill feed and everything Oritasy draws, for screenshots and film. Cockpit instruments stay. The UI comes back by itself while an Oritasy menu, the game's menu or the M map is open, and for good on F10 again.
- Ace squadrons are not moved by the weather: no storm wind, no cell wind, no turbulence, no icing, and a tornado leaves them alone.
- In the campaign a squadron that is shot down or leaves can come back, after 20 to 40 minutes. One squadron can be followed by another a minute later, and a wiped-out squadron is often replaced.
- Thirty more squadrons, among them VL-49 gunships, Aryx MC-260s where that mod is installed, and a flight logged only as ???. Four of the new squadrons fly eight aircraft.
- An ace's tail carries the squadron badge, one decal each side of the fin.
- Ace jets arrive fast, at least 260 m/s, and hold that speed through the entrance. A squadron marked for post-stall work performs a Cobra on the way in, one aircraft at a time from the left, and only above 1500 m. In the fight the Cobra and the Herbst are available again, and neither starts below 800 m.
- Seven squadrons from the Red Ace set, all of them post-stall: Red Ace, Unicorn, Owlbait, Kingfisher, Fallen Angels, Foxhound and Shear. Their badges are on the tail. Only Mantis still flies the FS-20. The squadron list is the full book again: a copy sitting in BepInEx/config/OritasySS/text was hiding every squadron added after the first 31.
- Wingtip vapour comes off each wing's own tip. A part that stuck out past the right wing no longer steals that trail.
- Each ace squadron can have its own entrance music. Tracks ship in the ssbag (`music/<key>.mp3`). A file of the same name in BepInEx/config/OritasySS/music replaces the packed one. It plays once, under that squadron's name, when the squadron arrives.
- Rapid move thrusters stay silent. The white jets still show. A missile break and a vehicle already in the air still hiss.
- Wingtip vortices leave from the wingtips: the tip is found from the airframe's own vertices, at the trailing edge of the outermost point, and the COFFIN, HUD, MFD and exhaust effects no longer count as wing.
- Campaign: the story hours are now the time until the Endgame. At the default 50 the Endgame begins after 50 hours and lasts one chapter more (about 62.5 hours in all).
- Reset met squadrons: the Squadron Codex (PHOBOS › Escalation) and Squadron Relations (Campaign) each have a button, pressed twice, that forgets the squadrons you have met.
- Multiplayer menu, top-left: OritasySS, BIA and the other loaded plugins are white lines under the packs Blueprinter already prints. The game version string is not changed, so those packs are not marked outdated.
- Player-hosted room search looks worldwide, and the pack list in the version is sorted, so two people with the same packs still match when the files are not in the same order.
- Multiplayer menus list every player from the game's own player table, including remote clients. Private messages and transfers use that list. A client request is matched to the sender the same way the game does, so a drop, a squadron call and a flight tune from a client run on the host. F1 is marked host menu or client menu: weather, ground avoidance and cook-off stay on the host menu.
- The F1, PHOBOS, wingmen and Codec tip strip hides while the game's camera-controls panel is open.
- Red Ace entrance music is the new track. The previous Red Ace track plays while the hangar aircraft-selection screen is open, and fades out when that screen closes.
- Dragon Squadron is now Draken Squadron. Its entrance track is Draken. The badge is unchanged.
- Tarus entrance music is Tarus.
- Profile server records and the last 15 sorties now close when you return to the menu, so a multiplayer match is written down. The server line uses the lobby name. Kills are taken from the kill feed, so a client records its own aircraft and player kills as well as the host.
- The profile screen shows an update bar underneath, with pause and resume. A whole asset pack starts the game even with no network. A missing or damaged pack with no network says OritasySS is incomplete and needs a network update, then quits. With a network it downloads. Several lines are tried at once and a slow one is dropped for a faster line. A stalled bar asks before downloading again. Addresses in BepInEx/config/OritasySS/update.txt are tried first (a Gitee update.txt URL belongs on the first line). The built-in GitHub address is the fallback. When the running program itself changes, the game quits and starts again with the new files.
- Mission editor: the Profile chip and the Help chip stay hidden.
- Hide all UI (F10) leaves the cockpit MFD on. The panel is a canvas drawn into the screen texture; fading it blanks the display.

Ace squadrons:
- They no longer throw themselves about from the moment they appear. Several pilots of the AI were steering an ace in the same instant and each one jolted its controls; now a squadron flies its entrance alone, and in the fight only the last order of each moment moves the stick.
- The entrance is flown level in close formation. While the camera films them, the jets roll one at a time, from the left of the formation to the right.
- Harder to bring down: a part loses at most a fifth of its strength to one missile, so an ace takes about five. Near-miss blasts and fires do far less, and a damaged wing is not torn off by the ace's own turns.
- Always guarded against flying into the ground, with a higher floor and an earlier pull-up than other AI, whatever the ground-collision setting says.
- A squadron called up with the test button in the hidden settings leaves no record: it does not count as met, downed, spared or anything else in the campaign record, and the codex does not log it.

Settings (BepInEx config): Theater › GroundRcs switches the RCS off, Theater › TankHop switches rapid move off, Modified › XE / TE / NE are the switches above.

# OritasySS v0.1.2δ

Pre-release: Hortensia's new weather. The hidden features still open with ↑ ↑ ↓ ↓ ← → ← → B A B A in F1 › Hidden settings or on the Profile page.

For everyone:
- Cloud you can fly through. Four decks are drawn as real volumes: heaped cumulus that towers into cumulonimbus with anvils over storm cells, low sheets of stratus and fog, middle altocumulus and altostratus, and high cirrus. A change of weather eases the decks over a couple of minutes. The game's particle clouds and starfield step aside while they draw.
- Hortensia's sky replaces the game's skybox with its own atmosphere, worked out from your real altitude, so it darkens towards space as you climb. Each time of day has two skies: night, dawn, morning, noon, afternoon and dusk. One is drawn each time the period comes round, more often the hazy one in fronts, fog and storms. Fronts, gales, storms and fog close the sky over with their own overcast.
- Clouds do not flicker or break into squares. Distant cloud is read at the right detail, high cirrus is drawn as fibres combed out by the wind and bent into hooks, and thick cloud glows inside. Cloud fields build up in some places and thin out and break up in others as they drift, and the tops churn slowly.
- Aircraft flying through cloud leave a tunnel that widens and closes up again over about a minute. They drag vapour out behind them and throw a spray of it where they break out. Inside a cloud, wisps stream past the camera.
- At night the stars turn with the sky, with the Milky Way, meteors and, on some clear nights, aurora. Light follows the time of day: golden hour, blue hour and deep blue nights. Fronts, storms and fog darken the world and turn it grey.
- Turbulence: in cloud near a cyclone or in a strong wind the airframe buffets and the view shakes. In a storm's eyewall it is violent anywhere, and at speed the loads can tear wings, tails and stabilisers off.
- Icing: in cloud between 0 and −20 °C ice builds up on the airframe, adds drag and weight and frosts the canopy. It goes again in warmer or clear air.
- Contrails stream behind every jet engine, mod aircraft too, above the contrail level. They form lower and last longer in moist air.
- Wingtip vortices: on a hard pull in humid air, vapour twists off both wingtips, mod aircraft included. They come more easily low down, in fronts, storms and fog, and inside cloud.
- Storms. The strongest cells grow a clear eye inside an eyewall, and contract boards offer a Storm Eye Survey: hold inside the eye for 45 s for 22M. Tornadoes touch down in thunderstorms. They are opaque grey wedges, half a kilometre and more across, that spiral upwards and hang from a turning wall cloud. They drag in vehicles and parked aircraft, lift them and throw them out. Lightning is the game's own bolt, aimed at the ground under the storm cells, and its thunder arrives at the speed of sound. Rain falls in grey shafts under the wet cells, and round you when you fly through one.
- Ground vehicles thrown or dropped at speed open emergency parachutes and fire retro motors before they land.
- The autopilot is the old Oritasy one again.
- Colour grade defaults are now contrast 44, saturation 32, warm/cool 9 and sharpen 0.95 (applied once over your old values). New filters: tint, hue shift, vignette, film grain, bloom and chromatic aberration. New cloud sliders: cover, density, light, draw distance and quality.
- Chromatic aberration climbs as G closes in on a blackout, holds while you are out and settles back to your own setting when you come round.
- Ace squadrons enter the way they do in Ace Combat. The entrance is filmed for the pilot they hunt and anyone within 80 km. It waits up to 30 s for a moment with no missile inbound and no map or panel open, and ends on the squadron emblem, name, lead, aircraft and motto in the lower left. While any member speaks, their emblem and callsign sit in the lower-left corner. In the fight, one of them talks every 22–40 s and one of your wingmen (or HORIZON when you fly alone) answers back, until the last of them is shot down. The lines change once they lose someone and again when one is left. A squadron called up while you are on the ground waits up to five minutes for you to take off instead of being lost, and every launch and entrance is written to the BepInEx log.
- Ace squadrons arrive in finger-four. An eight-ship squadron flies as two finger-fours, the second back and to the right of the first, and keeps that shape on the way in even after the lead is shot down.

Beta features (↑ ↑ ↓ ↓ ← → ← → B A B A):
- Gunline aircraft take off one per frame instead of all at once, so there is no hitch when they launch.
- The host's Weather test (F1 › Display) sets the weather, its severity and the time of day. It puts a tornado, a storm eye or a lightning strike in front of you, shows which sky is up and can hold either one. Ace squadron test calls a squadron in on you.

# OritasySS v0.1.1β

Pre-release. Everything below "Beta features" stays off until you enter the code ↑ ↑ ↓ ↓ ← → ← → B A B A: open F1 › Hidden settings in flight (or the Profile page on the main menu), click the code box, press the arrow keys, then type BABA (either case). It stays on until you close the game.

For everyone:
- Orbital drop. On the new Orbital Drop page of the Electronic Warfare panel (F2), pick a package (armour, mechanised, air defence, artillery or supply), click the drop point on the M map or type its grid in km, and call it down. Each vehicle falls on its own from 100 km up, glowing through re-entry with a plasma trail you can see from far off and from altitude, and can be selected and locked on your side's HUD on the way down. Low down the canopies open, retro motors under the belly fire for the last metres and kick up a ring of dust, and it settles unhurt under your orders. Under one of your PHOBOS satellites it lands within 60 m, otherwise within 450 m. The other side is warned of an insertion over the sector.
- Satellites are bought on the Electronic Warfare panel: PHOBOS reconnaissance satellites and the TLS attack satellite (bought or hijacked) are on the Space & Satellites page, after the intelligence pages. F1 Equipment keeps the carriers and links there.
- TLS satellites track their target through the whole three-second lock and fire at where it is. Turning no longer shakes them; only a fast roll at the moment of the shot can throw the beam off (from about 90°/s, missing most of the time near 270°/s).
- The old F4–F11 menus are back in F1. A new Comms page sends a private message or 1, 5 or 20M to another player, claims the kill reward (experience, repair and 5M at 2, 6, 12, 20… air kills) and shows your career score; host funds are behind the code. Flight Tuning has every slider of the old System window again, the Display page rebinds the manual missile and missile camera keys and has the unrestricted mounts switch, and aerial resupply works the old F10 way (fuel and battery targets, slow / medium / fast, level flight held, Backspace stops and refunds the rest).
- Messages, transfers, the kill reward, aerial resupply and carrier purchases go through the host, so they work for clients in multiplayer.
- F1 has a Hidden settings tab with the same code box as the Profile page.
- AI aircraft stop flying into the ground: they look ahead along their flight path and pull up in time. The tactics network now flies AI fighters of both sides, up to 24 at once.
- The radar warning box in the bottom left of third person is gone. The low performance tier is lighter.

Beta features (↑ ↑ ↓ ↓ ← → ← → B A B A):
- Gunlines in Oritasy Part.1[Drain side]: every large airfield sends twelve VL-49s (or Aryx MC-260s) in line abreast to orbit the far end of the enemy field across from it as gunships for eight minutes.
- Night vision in the cockpit looks like an image intensifier: monochrome green or white phosphor and grain that grows in the dark; the round tube only while you zoom in. Outside views keep the game's own night vision.
- Orders need bandwidth: wing orders, requests, cell orders, satellite purchases, TLS and orbital drops book bandwidth for 20 s, and with too little left they are refused.
- Hortensia only forecasts: the weather changes on its own, every forecast has its own skill and stated confidence, some are falsified, and each call is graded.
- STRIL / AWACS air picture: numbered tracks that coast after radar loses them, drawn on the map, and a bearing, range, altitude and intercept vector for the nearest hostile.
- PHOBOS satellites fly off the map and come back the way they left eight to fourteen minutes later, each at its own altitude from 100 to 180 km.
- Parachute flares: a Star Flare station in the countermeasure cycle of every aircraft with IR flares, one flare per press. At night it shows enemy units within 1.6 km to your side.
- Combat light: rocket motors light their surroundings, warheads and shell strikes flash. Destroyed vehicles and aircraft with fuel cook off and can set off the next one; main battle tanks run on reactors and sometimes breach into the game's smallest nuclear burst.
- Runway lights go dark one by one as you fly away from a field and come back on as you return.

# OritasySS v0.1

This is the latest rework of Oritasy.

A Chinese font, Noto Sans SC, now ships inside OritasySS.ssbag. It is only used on machines that have no Chinese system font; everywhere else the fonts are unchanged. The FM music stays in BepInEx/plugins/OritasySS/Music/<station> so you can add your own tracks. OritasyUpdater 2.0 updates OritasySS, BIA 1.0.25 and BIA 1.0.25(SS) from GitHub.

AI aircraft hold control again. XE thrust used to multiply the steering that comes from the engines too: vectored nozzles and split throttle now give stock moments, a helicopter's fuselage never feels more rotor torque than its stock tail was built for, and a propeller's extra torque above stock power is cancelled. The fly-by-wire keeps each aircraft's own tuning instead of being rewritten from the AI's numbers. Thrust and the x3 airframe strength stay. The Voice & AI page with the custom AI provider is back on the Profile page without the code. The boot animation plays in full instead of flashing past while the game loads.

Fixes: CCIP now finds an impact point from any release height (it stopped after 11 seconds of fall, so above about 600 m there was none), which also made the startup self-check pass. Campaign air defence spread works again, ducted-fan aircraft get their XE thrust, and the fuel readout on the GRS board no longer floods the log with warnings.

# Oritasy System Sanctuary 0.9.30

Oritasy Escalation is now Oritasy Part.1[Drain side] (Drain is the Proving Ground). Its mission description reads: This is the main line story of Oritasy world. Saves carry over.

TLS satellites no longer wait for the last chapter. A side can buy one for 40M for 15 minutes on the F1 Equipment page, or hijack one for 5 minutes by backdooring the enemy Command Host and an enemy satellite or airborne relay in PHOBOS (the TLS Uplink box on the Operations page; hijacking uses both backdoors up and the enemy notices). Owned satellites only fire on the other side's aircraft in flight, one per side at a time, and a card on the left shows how long yours has left. Veyrn's own still turn up in the last chapter and fire on everyone.

The story and dialogue text can be edited without rebuilding. Put a copy of any text file in BepInEx/config/OritasySS/text and it replaces the shipped one, reloading within two seconds of saving (squadrons and crew after a restart). The shipped copies are kept in text/_original, and TEXT_README.txt there explains the format. With the Chinese UI, the new Locale.StoryChinese setting shows radio, captions and news from the Chinese half of the text.

# Oritasy System Sanctuary 0.9.29

The satellites come back armed. From the last chapter of Oritasy Escalation, two Veyrn TLS satellites (the EOS-02 MRISA, three in the epilogue) circle the Proving Ground at 30 km and fire a laser at anything that is flying and moving, on both sides. A lock lasts three seconds and comes with a tone and a TLS LOCK warning. The shot goes where you would be if you held your course, so a hard break during the lock makes it miss; a hit almost always kills. Ground units, landed aircraft and hovering helicopters are never targeted, and nobody is credited for a laser kill.

The story now follows what you did as well as the aces. Kills, ships and fields taken push Spearhead; deaths and fields lost push Ember; contracts push Ledger with the bounties; cyber operations and IFF hacks push Ghost with sparing and turning aces; steady ground work holds the Line. The last chapter is no longer only a destination: how it goes picks the ending.

Colour grade adapted from Boscali Summer: a URP grade above the game's post-processing (contrast, saturation, white balance, an optional .cube LUT from BepInEx/config/OritasySS/luts) and AMD RCAS sharpening. It is on by default and tuned on the F1 Display page. When Boscali Summer is loaded its own grade takes precedence and OritasySS leaves sharpening to it.

# Oritasy System Sanctuary 0.9.28

Oritasy Escalation now starts at 23:00 and the clock runs from there; the opening briefings were rewritten for the night. Your opening, callsign and crew are picked once per run, on the main menu (F1 Profile > Campaign > New campaign) or in PHOBOS > Campaign. Starting a new campaign in the mission restarts the whole mission instead of only resetting the save. Changing the opening mid-run is gone.

Ace squadrons decide the story. The branch of the next chapter follows what happened with them: shoot them down (Spearhead), get shot down by them (Ember), hunt them for bounties (Ledger), spare them or turn them (Ghost); if nothing decisive happens, the front holds (Line). From chapter 2, every new chapter ends its briefing by putting you in the air at the front, then sends that chapter's ace squadron (two from chapter 4) straight at you with its entrance on film. Every fighter in a hostile squadron goes for you first. Air defence never targets ace aircraft; only fighters can bring them down.

Keys: F1 is the Oritasy menu, F2 PHOBOS and F3 the Codec, and that is all. On the main menu F1 opens the Profile. The page shortcuts, the K, N and Tab keys and the career box are gone. The F1 menu uses the PHOBOS panel style and keeps three pages: Autopilot, Engine and Equipment. Player messages, money transfers, host funds and the kill-reward claim were removed.

Settings are hidden. Click the Code box in the F1 menu or on the Profile page and type the code to reveal flight tuning, resupply, landing and display options, the campaign settings, the wingman display options, the AI and voice setup and the system toggles. The GRS map no longer has a Settings button.

PHOBOS always opens in the same place, on the left. The Squad page now shows all three teammates and lays the skill trees out two by two.

# Oritasy System Sanctuary 0.9.27

One menu: F1 now holds everything the F-keys used to open, as pages: Flight (G limit, top speed, thrust, roll rate, fuel capacity, reset), Autopilot (with the anti-crash guardian), Landing, Players (messages, money, host funds), Supply (resupply and kill reward), Engine (component wear as a list, click to repair), Equipment (satellites and carriers) and Display (radar warning position and overlays). F2 to F11 open their page directly and close it again. Every page scrolls.

The tip strip has been reset. It shows the F1 menu, PHOBOS, wingmen and the Codec, plus autopilot, the campaign chapter and escalation only while they are live. The in-flight HUD now uses the first-person visor style in third person as well.

Wingmen you ask for now really come. Friendly aircraft already flying nearby join first. The rest take off from the nearest friendly field in your type, livery and loadout, and join the formation once airborne (air start if no hangar delivers). Request them from PHOBOS > Wingmen, the V ring (flight or tower) or by setting the wingman count. Mod mounts the network cannot send are left off their loadout.

Radio names carry their branch after them: ▲ air, ■ army, ● navy. The V ring marks contacts the same way.

Language: the Chinese interface now covers keys and menus only. Radio, story captions, news and callsigns stay in English.

The GRS board on the M map scrolls with the mouse wheel, and its Hangars and Network pages list every field and node instead of stopping at 16 and 9. Oritasy Escalation now ships its own mission file inside OritasySS. If a game update brings a newer Escalation format, the game's copy is used instead.

# Oritasy System Sanctuary 0.9.26

Campaign saves: the Autosave slot is written every 5 minutes, at every new chapter, when you leave the mission and when you quit the game. Slots 1 to 6 are now manual and only change when you press Save here in PHOBOS > Campaign > Saves. The start of every chapter is kept as a load point you can rewind to. On the main menu, F8 Profile has a new Campaign tab: Continue, or load any slot or load point, and it starts Oritasy Escalation and puts you back into the story.

GRS Command now runs operations. It names each one and gives the objective and H-hour on the radio (take a field, hold a field, deep strike, coastal sweep, air defence suppression or a network operation), warns two minutes out and calls it at H-hour. ANVIL and TRIDENT acknowledge on their own nets. While an operation runs, command traffic loads every friendly PHOBOS node, so bandwidth drops, most of all during network operations. A card under the bandwidth matrix shows the operation and its countdown. Take and hold operations are judged on who owns the field at the end.

JESTER only rides in two-seat aircraft. Answer his flare prompt with Left (yes) or Right (no); saying no keeps him quiet for 12 seconds. The prompt uses the old Oritasy WSO bar again. The U-key HMD readout is gone.

The story text has been rewritten as plain briefings, radio traffic, news copy and soldier talk, and the rumours and command lines on the army and navy nets with it. Chapter captions are now single-line white subtitles with no box behind them; long briefings are split into lines and timed to their length.

OritasySS now requires BIA 1.0.25(SS) (BIA.SS_1.0.25.dll), a sub framework that sits on an unchanged BIA 1.0.25 and needs it installed. It is the pre-launcher: before OritasySS loads it checks the install (the OritasySS dll and bag, a single BIA runtime file, no old Oritasy core) and shows each check in the BIA load panel. The OritasySS boot starts when that panel closes.

# Oritasy System Sanctuary 0.9.25

New mission, Oritasy Escalation: the vanilla Escalation map run as one long campaign on the Proving Ground, from 17 January to 17 April 2072. The story is 50 hours by default, split into five chapters (story length and clock pace are in PHOBOS > Campaign > Settings). Chapters arrive on the clock and the mission never ends on objectives. Each chapter after the first branches five ways on how the last one went: Spearhead (taking ground), Line (holding), Ember (losing fields), Ledger (Versus contracts) or Ghost (network warfare). That makes 3125 paths, each with its own briefing, HQ orders, enemy intercepts, UESC broadcast, rumours and epilogue. At every chapter you are brought back to a friendly field for a captioned briefing. Pick one of five openings, your callsign and three of six offered teammates (15 in all). Teammates have their own voices and four skill trees: Air Combat, Electronic Warfare (ECM pods, spoofing missiles near you), Strike and Cyber (IFF intrusion). You and your crew rise from Second Lieutenant to Major General and fly better as you do. Campaigns save to six slots plus an autosave.

Ace squadrons: there are now 31, each with named wingmen who have their own lines, last words and reactions. The new CI-22 squadron, Fieldsong, flies through the Veyrn story. Squadrons that need a mod aircraft (FS-41 and others) only appear if it is installed. Their emblems use the VTE squadron art, and aces carry a red TGT tag. Some of them, and your top-ranked crew, fly assisted Cobra and Herbst post-stall manoeuvres. You can add your own squadrons in BepInEx/config/OritasySS/squadrons (a template is written there). How a squadron treats you depends on your history with it.

Hold V for the comms ring. The centre shows who you are talking to and how friendly they are. Pick the nearest ace squadron, any nearby contact, your flight or the nearest tower, then choose what to say: warn, surrender demand, taunt, salute, bribe, duel, ask who they fly for, request support, or cut the channel. Answers depend on mood and can make an ace squadron leave. Your crew comments on what you said. After takeoff the tower can send three wingmen of your type and livery. It chats on approach, grades your landing on airframe damage and lets you answer back. Army (ANVIL) and navy (TRIDENT) run their own command nets. Enemy nets bleed through, and jammers and nuclear bursts put static on the air. You can hack the enemy IFF so they fire on each other, until they notice, rotate their crypto and hit back. Campaign air defence is heavier and more accurate so the front lasts until the next chapter. Radio lines follow the live picture (BRAA, clock position, aspect, group size, weapon, type) in Project Wingman brevity style. The WSO is now JESTER.

Voice and AI: every line has a stable voice id. Recordings dropped into BepInEx/config/OritasySS/voice play in place of the subtitle, with an optional radio filter, and Profile > Voice & AI exports the full line list. Optionally, an OpenAI-compatible provider (base URL, key and model set in the Profile page) can write idle crew and tower chatter and speak lines that have no recording. The key is stored in plain text in the BepInEx config.

Weather: Hortensia now moves through regimes (fair, breezy, front, gale, thunderstorm, fog). Each brings gusts, wind shifts, turbulence, storm downdrafts and updrafts, and is announced on the radio. Strength is set in PHOBOS > Campaign > Settings (0 calm to 2 brutal). New boot screen: a Linux-style boot log with your real hardware, then the OritasySS load bar over a backdrop that fades from black to clear. Any key skips it.

# Oritasy System Sanctuary 0.9.24

New Escalation: every match now moves through five time periods instead of days (Opening Moves, First Contact, Escalation, Total War, Endgame). Each one arrives after a set number of minutes, or sooner if the fighting heats up: kills, ships, aces and finished contracts all add heat. Each period raises enemy AI skill and contract pay, and opens slots for ace squadrons. GRS Command announces the change, and if you have found the enemy's tactical channel you also hear their side. A small phase card sits in the left HUD column. New Escalation page in PHOBOS (GRS rail or the Escalation flight chip) with Timeline, Ace Squadrons, AI Mind and Settings. There are 22 named ace squadrons with their own colours, procedural emblems, leaders, mottos, flying styles and radio chatter: 16 appear in the later periods, and 6 hunt you personally once your mercenary chain reaches 2, 4, 6, 9, 12 and 16 contracts. They spawn as a real formation (vee, finger four or wedge) and stay in it until they reach you. Then they break and fight; the lead speaks when one of them goes down, and again when the last one falls. Ace bounties can show up on the Contracts board. The squadron codex keeps the ones you have met and shot down. Cinematic camera: a period arriving and a squadron entering both play as letterboxed shots in the Ace Combat style, with title banners. The shot never starts with a missile inbound or low over the ground, and Esc, Enter or Backspace skips it. With the camera off, or when it can't play, the same moment shows as a title card. Oritasy now has its own look across the HUD: the cockpit view draws hollow amber visor glyphs, the external view draws dark glass cards, and both share brackets, scan lines and emblems. The bandwidth box and wing roster use it already. The Tips menu is unchanged for now. Enemy fighters can be flown by a small neural network (16 inputs, 20 hidden, 7 manoeuvres: press, climb, beam, extend, bracket, go low, or leave it to the native AI). It runs on the host, learns online from kills and losses, and saves its weights between matches. It stands aside whenever Boscali Summer is loaded.

# Oritasy System Sanctuary 0.9.23

New PHOBOS Radio Codec in the Metal Gear style (K, the Radio Codec chip, or the GRS rail): a green 140.00 - 142.99 dial with a signal meter, two portrait frames that clear from static as you lock on, tune and scan buttons, a transcript of the tuned channel and a memory list. Fixed channels: PHOBOS Operations 140.15, your flight 140.85, Horizon 140.96, GRS Command 141.12, Hortensia 141.52 and the three civilian stations; whatever you tune in also plays as a radio subtitle. Every hostile faction talks on a hidden tactical channel that changes each match. Scan skips it, so you have to tune by hand on faint signals, or follow the numbers station that keeps reading one out. Once found, you overhear their patrols (type, position off the nearest field, altitude), scrambles when you approach their fields, and their reaction to your kills. Clean intercepts also land in the EW log. Decryption starts at 30% and rises when PHOBOS has breached or backdoored that faction; garbled words show as dashes. Listen too long, especially while they are on alert, and they move to a backup frequency.

# Oritasy System Sanctuary 0.9.22

Screens no longer pile up. The flight HUD only draws in flight with the M map closed, and centred menus (F-keys, F9, PHOBOS) always sit on top. The bandwidth box, intrusion and datalink alerts, wing roster and missile camera stack down the left in their own slots, stopping above the radar warning display. The helmet display, stall warning and career board moved out from under the subtitles. The top-left camera no longer follows PHOBOS satellites. Civilian Radio is a TV news desk now: an ON AIR anchor reads the latest stories and a LIVE crawl scrolls along the bottom of the page; the in-flight ticker is gone. New radio dialogue in the Ace Combat style, in the same place, font and colours as OritasyRadio: callsign over a white << quote >>, one line at a time, by priority. OritasyRadio's own lines play through it so the two never stack. Horizon (AWACS) calls bandits with bearing, range and angels, warns of missiles and confirms kills; GRS Command briefs at spawn and reports airbases won or lost; Hortensia comments when you fly into one of its cells; PHOBOS notices and old system callouts become radio lines too. New Radio Log page keeps the last 120 transmissions. Wingmen: friendly AI aircraft within 30 km join your flight (automatically after takeoff, or on any order). They hold a real formation slot (Echelon Right/Left, Finger Four, Line Abreast, Trail, Wedge) using the AI's own autopilot plus a throttle loop, and take targets for Cover Me, Attack My Target, Engage at Will and Ground Strike. Each wingman has a callsign and a personality (Gauge, Pixie, Hound, Moth, Rook) who talks on the radio. Hold C for the command ring: move the mouse toward an order and let go, scroll the wheel to change formation, press 1–8 to pick directly. New Wingmen page with orders, formation, spacing, flight size, roster and callsign tags. Wingmen are host-run and work in multiplayer.

# Oritasy System Sanctuary 0.9.21

PHOBOS satellites now wear their real texture (no more solid purple), keeping the game's own lit shader. Enemy satellites are never drawn as a picture or footprint; they just become lockable once all three gates pass (above 20 km, their network breached, intelligence knows the bird). PHOBOS now runs in multiplayer: the host is authoritative, players with the mod join a quiet side channel (vanilla players are not affected), satellites sync and can be bought from any seat, and hacking a faction flown by people sends a live intrusion to them that they can trace, block, isolate or counter, with the result coming back to the attacker. New Infiltration page: plant spy cells in hostile fields with fresh intelligence, build influence, then black out the datalink, turn nearby units, start a mutiny or make the whole airbase surrender, as long as the cell is not caught and the enemy has not noticed. Counter-intelligence shows awareness and sweeps at our own fields; datalink outages blink on the bandwidth HUD. New Contracts page: strike, bounty, capture, hold, reconnaissance, weather sampling, anti-satellite and cyber contracts pay into your allocation, scaled by standing. New Hortensia Weather page: drifting flower-named cells add real rotating wind, the host nudges live wind and cloud, bulletins are always 97% confident and graded against what happened, and a regional wind table shows direction changes at every field; the cells also draw on the M map. New Civilian Radio page: Radio Lantern, Valley FM and Harbour Gossip report captures, falling satellites, weather and rumours, with an optional one-line ticker in flight.

# Oritasy System Sanctuary 0.9.20

PHOBOS Electronic Warfare replaces the old Intrusion / Intelligence pages, using the Bifrost NetWar panel and map animation. Real nodes (airbases, command host, radars, ships, airborne relays, own datalink, cloud server, comm vehicles, PHOBOS satellites) share an offense / defense / intelligence bandwidth split (Balanced, Assault, Hold, Espionage). Network Sweep, Scan, Breach, Implant Backdoor, Disrupt 60 s, Exfiltrate Data; the enemy traces back, and a full trace aborts everything and exposes the exit node. Enemy intrusions (denial, data theft, sabotage, radar spoofing) can be traced hop by hop, then blocked, countered or isolated. Sector intelligence per airbase comes from own radar, satellites, backdoors, agents and exfiltration, and decays over time. Disruption and enemy hits load the PHOBOS bandwidth grid. Enemy satellites lock above 20 km once a node of that faction is breached and intelligence knows the satellite. On the M map the panel docks into the GRS board, with packets on breach routes, progress arcs, pulsing intrusion rings and confidence-coloured sector circles. In flight it opens from the Electronic Warfare / Intelligence Network chips. The bandwidth HUD blinks on intrusion. The standalone BifrostNetWar plugin is turned off (it duplicated this and also took F9).

If the old Oritasy_0.0.9.*.dll core is still in plugins, SS stays off and says which file to remove, instead of both patching the game and fighting over F1–F11.

# Oritasy System Sanctuary 0.9.19

PHOBOS is the whole UI chrome: scaled centre panels, full-name keys, filters, reserved slots. Theater is GRS (Network, Satellite, Hangars, Intrusion, Intelligence, Settings). F9 is Special Equipment and Ships (Hyperion SpawnShip, no cooldown). Menus need bandwidth. Left HUD is a vanilla-font 12-dot matrix. Cloud server + random AFV comm vehicles. Enemy satellites lock only above 20 km after intrusion once Intelligence knows them.

# Oritasy System Sanctuary 0.9.18

PHOBOS: network + satellite as one system. Buy birds on F9 (10M, axis X+/X-/Z+/Z-, max 3 per faction). Each is a 100 km / 100 km/h target with a 20 km square footprint. Shot down drops that picture. Dump still spends 16 Mb on a ground node.

# Oritasy System Sanctuary 0.9.17

Satellites no longer circle HQ. Two birds run west-to-east ground tracks across this small theater (a slice of a planet orbit), leave to the east, then re-enter from the west. Coverage and dump only paint while OVER.

# Oritasy System Sanctuary 0.9.16

F1–F11 tips only draw in a match while you are in an aircraft. Map pages are NET / SAT / FIELD / SET, not a Boscali index or a green Met Office window. NET is a ground graph with real bandwidth; SAT birds dump into one node and spend 16 Mb. Uplink leftover gates shared picture, hunt range and F9. Palette is slate / amber / violet, not mint.

# Oritasy System Sanctuary 0.9.15

Map pages use the Boscali index rail (STR/ENV, WMC/OPS, SET) with two-line keys. STR lists every friendly hangar and paints them on the map; spawn is click-the-field then SELECT AIRCRAFT (vanilla picker no longer covered). OPS SPACE is a station: SAT-1/2/3 footprints, ground tracks, SAR picture and track file. Hostile flights cluster as BANDIT/ACE on the map and on STR/WMC. ENV is a Met Office window plus synoptic cells/isobars/fronts.

# Oritasy System Sanctuary 0.9.14

Map pages match Boscali layout: left glass board, right WMC/STR/OPS/SET/ENV index, overlays on the vanilla M map (front line, coverage rings, synoptic cells / isobars — not filled squares). Compact board keys, not a right-side tip rail. F1–F11 chips sit on the ChargeIndicator stack (248×20, gap 3, gold / olive). Hangar and aircraft-select stay vanilla.

# Oritasy System Sanctuary 0.9.13

Awake no longer dies on TurbineEngine (game has OnEnable, not Awake). Harmony patches that miss a method are skipped instead of killing the plugin. Overlay canvas only runs on the M map / C ring, so looking around does not rebuild UI every frame.

# Oritasy System Sanctuary 0.9.12

Map and spawn UI are a real overlay canvas on top of the vanilla M map (IMGUI was invisible behind it). C wing ring always ticks, not only on high-end PCs.

# Oritasy System Sanctuary 0.9.11

Vanilla M opens the map; SS chips overlay that same map. Death spawn uses the same chips (airbase list + SELECT AIRCRAFT). C is only the wing command ring (COVER / FORM / PUSH / HOLD) and those orders actually fly and shoot. No radio pack, music, extra weapons, gliders or MFD.

# Oritasy System Sanctuary 0.9.10

Theater lives on the maximized map. Right rail is Oritasy chips: MET CLEAR / HAZE / STORM / HOLD, SITUATION STR / FRONT, TASKING WING / OPS, SYSTEM SET. The map itself draws weather cells, the HQ–HQ front line, STR contacts, and WING rings. TAC strip shows threat pips and ILS ticks. Fog follows MET.

# Oritasy System Sanctuary 0.9.9

Theater is one Oritasy sheet (left chip, gold/mint top bar, dark panel, STATUS / WING / MET / FRONT / OPS). No radio, music, extra weapons, gliders or MFD. C / J open it; Ctrl+O jumps to MET.

# Oritasy System Sanctuary 0.9.8

Boscali Summer theater remade in Oritasy style (no Boscali DLL). 0.9.9 dropped radio / music / extra weapons and unified the UI.

# Oritasy System Sanctuary 0.9.7

多模猎敌 / 瞄准点夹角 / 近炸只作用于 SS 自己的 [IAL] 弹，不再劫持 Ash 和原版半主动弹。

# Oritasy System Sanctuary 0.6.0

XE / NE / TE / Bexur 整套机队改动写回 OritasySS。数表在 `Math`（Identity / Envelope / XeText），Unity 接线在 `Play.Xe`。克隆、推力、油耗、起落架、机体强度、FBW 都是一次性写入，机库只换当前列表，不每帧扫场景。
