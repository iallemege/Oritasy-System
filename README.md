# Oritasy

**Nuclear Option** gameplay pack by **IAllemege** / qiaochen  
BepInEx + Harmony · Release **0.0.9.7**

Oritasy is a single-DLL mod that combines aircraft / unit tuning, flight assists, HUD tools, and the WeXon weapon suite. The in-game mod UI is **English**.

> Aircraft, vehicles, and buildings modified by this pack are fully customizable.  
> Because of GUID differences from the stock game, some Workshop skins may not apply.

---

## Features

### Aircraft & units
- Stock airframes become modified **XE** types with encyclopedia tag `[Oritasy]`
- Ships **NE** / `[Thanos]`, ground vehicles **TE** / `[Unitas]`, buildings `[Bexur]`
- Per-airframe thrust, fuel, gear, and flight-envelope tuning (F1)
- Optional **J-35A** hangar clone of the FS-12 with Draken visual (`J35AAssets`)
- Nuke shock / blast resist tiers for aircraft, ships, buildings, and vehicles

### Flight tools
- **F1** — Oritasy System (G limits, speeds, FBW, pack toggles)
- **F2** — Autopilot (straight / orbit / land)
- **F3** — Beginner Assist (auto takeoff / land, crash guardian, terrain help)
- **F9** — Strategic Support arsenal (unlocks via career kills)
- **F10** — Aerial resupply
- **F11** — Aircraft RWR layout
- **Delete** — Missile picture-in-picture · **F4** cycles tracked missiles
- **F5** — G-meter · **F6** — manual missile pilot · **F7** — missile-pilot HUD / RWR

### Weapons (WeXon, included)
- Multi-mode seekers, free-hunt, player lock stickiness
- IFF / friendly-fire lock block
- Optional unrestricted hardpoints (Career Profile toggle)
- IAL / nuclear variants where appropriate
- **ACM-119 [IAL]** cluster bus + **ACNM-118 [IAL]** nuclear cluster (Veyrn Aeronautics lore)
- AAM-IV bus visual from `WeXonAssets`

### Career Profile (main menu)
- Local XP / prestige showcase, kill stats, badges (does not change vanilla ranks)
- Unrestricted mounts toggle
- **Dynamic music (BETA)** — optional situation-based BGM; enable under **Experimental**  
  Custom tracks: `BepInEx/plugins/OritasyMusic/<mood>/` (`.ogg` / `.wav` / `.mp3`)

---

## Requirements

- [Nuclear Option](https://store.steampowered.com/app/2168680/Nuclear_Option/) on Steam  
- [BepInEx 5](https://docs.bepinex.dev/) (x64 / IL2CPP or the pack your game uses — this mod targets the game’s managed assemblies)

---

## Install

1. Install BepInEx for Nuclear Option and launch the game once.
2. Copy the release DLL into:

```text
<Steam>\steamapps\common\Nuclear Option\BepInEx\plugins\
```

Recommended filename: `Oritasy.dll`  
(Do **not** also load `WeXon.dll`, `OritasyAir.dll`, or legacy `VeyrnAcm.dll`.)

3. Optional asset folders (same `plugins` directory):

```text
WeXonAssets\     AAM-IV.obj + textures (ACM bus skin)
J35AAssets\      j35.obj + hull textures (J-35A visual)
OritasyMusic\    optional custom BGM folders (menu, combat, …)
```

4. Restart the game fully after updating the DLL.

Config (created on first run):

```text
BepInEx\config\com.qiaochen.oritasy.cfg
BepInEx\config\com.qiaochen.wexon.cfg
```

---

## Keybinds

| Key | Action |
|-----|--------|
| **F1** | Oritasy System |
| **F2** | Autopilot |
| **F3** | Beginner Assist |
| **F4** | Cycle missile camera target |
| **F5** | G-meter |
| **F6** | Manual missile pilot (enter / exit) |
| **F7** | Missile-pilot HUD / circular RWR |
| **F8** | Career Profile (main menu) / chase HUD (in flight) |
| **F9** | Strategic Support |
| **F10** | Aerial resupply |
| **F11** | RWR layout |
| **Delete** | Missile PiP camera |
| **Esc** | Close menus / exit manual missile |

**Manual missile (F6):** W/S pitch · A/D yaw · E / Shift throttle up · Q / Ctrl throttle down.  
Release stick to hand guidance back to the stock seeker (terrain-following cruise continues).

---

## Beginner Assist notes

Crash guardian does **not** fight normal high inverted flight. It intervenes on low inverted dives, post-stall, and spins, with per-aircraft thresholds. STOVL jets (EW-25, VT-7, FS-20) also get nozzle / thrust help near the ground.

---

## Known limitations

- Manual throttle does nothing on burned-out / unpowered missiles.
- In multiplayer, guidance and detonations are host-authoritative; client takeover can desync.
- Circular RWR depends on aircraft radar-warning events — denser than nothing, not a full radar scope.
- Always **fully restart** the game after swapping the DLL or major config changes.
- Expect bugs. Have fun anyway.

---

## Credits

**Oritasy System** — IAllemege / qiaochen  

Nuclear Option and all original game assets belong to their respective owners.  
This mod is for personal and multiplayer entertainment use.
