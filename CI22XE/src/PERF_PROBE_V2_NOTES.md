# Oritasy Perf Probe v2

## Files
| File | Action |
|------|--------|
| `PerfProbeService.cs` | **replaced** — GC deltas, scene census, report v2, CompareReports |
| `PerfProbeMenu.cs` | **replaced** — tabs: Subsystems / Harmony / GC-Alloc / Scene / Compare |
| `HarmonyTimingSample.cs` | **new** — optional heavy Prefix+Finalizer wall-clock sample |
| `PerfFrameGate.cs` | unchanged |
| `PerfBudgetService.cs` | unchanged |
| `PerfMode.cs` / `OritasyWorker.cs` | not modified (existing APIs used) |

## Deploy paths (user PC)
Copy all three changed/new `.cs` files into:
`G:\8\模板项目-034\ExportedProject\BepInExMods\CI22XE\src\`

Also keep a mirror at:
`C:\Users\iallemmege\agent-tools\oritasy-perf-src\`

**Do not touch** `UnityRuntimeBridge`.

## Build
Use the existing CI22XE build script / csproj. Ensure `HarmonyTimingSample.cs` is included
(wildcard `*.cs` compiles usually pick it up automatically).

## Harmony timing
- **Default**: owner counts only (cheap, cached 2s) + existing `Measure()` subsystem buckets (wall-clock for instrumented Oritasy paths).
- **Heavy sample (OFF by default)**: menu → Harmony tab → "Start 5s ×12" or "Start 10s ×20".
  - Picks hottest patched methods (Update/LateUpdate/FixedUpdate/OnGUI preferred, then patch count).
  - Installs temporary Harmony Prefix+Finalizer Stopwatch wrappers under id `oritasy.perf.timing.sample`.
  - Accrues wall-clock (original method **+ all patches** on that method — not per-owner).
  - Auto-UnpatchSelf when the window ends (or Stop button).

## GC / alloc
Per-frame `GC.CollectionCount(0/1/2)` deltas and `GetTotalMemory(false)` deltas.
Window sum of positive mem deltas ≈ allocation; gen2+hitch correlation on spikes.

## Reports
Written to `BepInEx\plugins\OritasyPerf\perf_v2_YYYYMMDD_HHmmss.txt` with
`### SECTION:<name>` / `### /SECTION` markers (`format: perf_report_v2`).
Menu: **Diff last two** compares previous vs latest (or newest two on disk).

## Residual risks
- Heavy Harmony sample can hitch FPS and may fail to patch some methods (abstract/generic).
- Timing is whole-method, not per-owner GUID.
- Scene census uses `FindObjectsOfType` (hitchy); cached ~2.5s / on report only when forced.
- Alloc estimate is heuristic (heap delta), not a true profiler allocation stream.
- Nested/re-entrant patched calls attribute full inclusive time per invocation.
