# Oritasy

**Nuclear Option** gameplay pack by **IAllemege** / qiaochen
BepInEx + Harmony · Release **0.0.9.193C** (Standard Edition C, EN/ZH)

Oritasy is a single-DLL mod that combines aircraft / unit tuning, flight assists, HUD tools, WeXon weapons, and TGM-85.

This repository is the **Standard C** source (unpacked bilingual pack). Copy `Oritasy_0.0.9.193C.dll` into `BepInEx/plugins` after building.

> Aircraft, vehicles, and buildings modified by this pack are fully customizable.
> Because of GUID differences from the stock game, some Workshop skins may not apply.

In-game guide (Chinese): see [GUIDE.md](GUIDE.md).

---

## Features

### Aircraft & units
- Stock airframes become modified **XE** types with encyclopedia tag `[Oritasy]`
- Ships **NE** / `[Thanos]`, ground vehicles **TE** / `[Unitas]`, buildings `[Bexur]`
- Per-airframe thrust, fuel, gear, and flight-envelope tuning (F1)
- Nuke shock / blast resist tiers for aircraft, ships, buildings, and vehicles

### Flight tools
- **F1** — Oritasy System
- **F2** — Autopilot
- **F3** — Beginner Assist
- **F9** — Strategic Support
- **F10** — Aerial resupply
- **F11** — Oritasy RWR layout
- **Delete** — Missile picture-in-picture
- **Insert** — Manual missile pilot

### Weapons
- Multi-mode seekers, LOAL free-hunt, IFF
- **ACM-119 [IAL]** cluster · **ACNM-118 [IAL] [1.5kt]** nuclear cluster
- **TGM-85** A/B/C/D/E/S family (included)

---

## Requirements

- [Nuclear Option](https://store.steampowered.com/app/2168680/Nuclear_Option/) on Steam
- [BepInEx 5](https://docs.bepinex.dev/) x64
- To **build**: .NET Framework 4 csc (`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`) and the game's `Managed` + `BepInEx\core` assemblies (see `tools\write_csc_rsp.ps1`)

---

## Install

1. Install BepInEx for Nuclear Option and launch the game once.
2. Copy `Oritasy_0.0.9.193C.dll` into:

```text
<Steam>\steamapps\common\Nuclear Option\BepInEx\plugins\
```

Do **not** also load `WeXon.dll`, `OritasyAir.dll`, or standalone `Kh85MT.dll`.

3. Fully quit Steam / the game after swapping the DLL.

---

## Build Standard C

From this repo root:

```bat
build_c.bat
```

That regenerates `build_combined_sources.rsp` from `NOWeaponSuite\src`, `Kh85MT\src`, and `CI22XE\src`, then compiles `Oritasy_0.0.9.193C.dll` with `ORITASY_COMBINED`.

Edit the game / BepInEx paths in `tools\write_csc_rsp.ps1` if your install is not on `d:\Steam\steamapps\common\Nuclear Option`.

---

## Credits

**Oritasy System** — IAllemege / qiaochen

Nuclear Option and all original game assets belong to their respective owners.
This mod is for personal and multiplayer entertainment use.
