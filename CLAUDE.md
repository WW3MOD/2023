# WW3MOD - Agent Instructions

WW3MOD is a **total conversion** of OpenRA Red Alert (`release-20230225`, engine in-repo, ~264 C# files modified) into a modern World War 3 RTS. NATO/America vs BRICS/Russia. Solution `WW3MOD.sln`; engine compiles to `engine/bin/`; mod content in `mods/ww3mod/`.

## Game model — hard rules (full doc: [`DOCS/reference/game-model.md`](DOCS/reference/game-model.md))

**Do not assume any Red Alert mechanic still applies.** The rules below prevent the recurring wrong assumptions; read the full doc before designing anything strategic.

- **No factories, no tech tree.** Units are **called in as reinforcements from off-map reserves** via the **Supply Route (SR)** — the single production building. They spawn at the nearest map edge and travel to the rally point. Buildings/defenses are the exception (local `Production@Local` queue).
- **The SR is a fixed, indestructible beachhead — not buildable, not placeable.** Each player starts with exactly one; more only by capturing neutral ones. Before any AI/strategic work touching SRs, read [`DOCS/reference/supply-route.md`](DOCS/reference/supply-route.md). The AI YAML listing `supplyroute` under `ConstructionYardTypes` etc. is trait plumbing, not a factory in the strategic sense.
- **Costs are budget allocation**, not manufacturing: destroyed = budget lost; "rotating out" a unit to the map edge recovers its cost.
- **HPAD/AFLD are rearm/repair support**, not production prerequisites.
- **Engine code still contains RA-era assumptions** (e.g. airpad checks). Verify how WW3MOD actually uses a system before trusting old logic.

## Recipes (read the doc when triggered — these are NOT harness Skills; never call the `Skill` tool for them)

| Trigger | Doc | Purpose |
|---|---|---|
| `AUTOTEST <bug>` | [recipes/AUTOTEST.md](DOCS/recipes/AUTOTEST.md) | Test-driven loop — failing test → fix → green → commit. **Default for behavioral fixes** even without the trigger |
| `DEMO <topic>` | [recipes/DEMO.md](DOCS/recipes/DEMO.md) | Stage a scenario for the user — no verdict. Any "show me" request |
| `SCREENSHOT <topic>` | [recipes/SCREENSHOT.md](DOCS/recipes/SCREENSHOT.md) | Capture PNGs, evaluate via multimodal `Read`. **Apply automatically for visual work** (UI, palette, lobby, sprites, formations) |
| `BALANCE <a> <b>` | [recipes/BALANCE.md](DOCS/recipes/BALANCE.md) | combat-sim driven tuning (tool: `tools/combat-sim/`) |
| `TELEMETRY <events>` | [recipes/TELEMETRY.md](DOCS/recipes/TELEMETRY.md) | Per-tick gameplay log channel (build-on-first-use) |
| `PLAN` / `PLAYTEST` / `TRIAGE` / `REVIEW` / `CONTEXT` / `FINALIZE` / `DOCUMENT` | [recipes/](DOCS/recipes/README.md) | Session/workflow recipes — read on demand |

Modes: RELEASE (default, scope-locked v1 methodology) vs EXPERIMENTAL — [`DOCS/modes/`](DOCS/modes/README.md).

## Rules

- **NEVER push to remote.** The user pushes manually.
- Commit finished work with descriptive messages; don't leave uncommitted changes behind. No co-author/attribution trailers.
- Apply confirmed rules from `C:\Users\fredr\Desktop\ClaudeRules\confirmed\`.
- Keep this CLAUDE.md current — update it when information here goes stale.

## Project state

- **`WORKSPACE/`** — living state: `RELEASE_V1.md` (v1 source of truth), `HOTBOARD.md` (in-flight), `BACKLOG.md`, plans, discoveries. Conventions + folder map: [`WORKSPACE/README.md`](WORKSPACE/README.md).
- **`DOCS/`** — static reference: recipes, modes, reference, gameplay. Index: [`DOCS/README.md`](DOCS/README.md).
- Non-obvious insights → `WORKSPACE/DISCOVERIES.md` (dated). Incidental bugs → `WORKSPACE/bugs/discovered.md`.

## Build & Run

```bash
./make.ps1 all          # Windows build (targets net6, runs on .NET 8+); `make all` on Linux/macOS
./launch-game.sh        # run (launch-game.cmd on Windows); auto-builds first
make test               # YAML validation (needs .NET 6 runtime specifically)
dotnet test engine/OpenRA.Test/OpenRA.Test.csproj --configuration Release   # unit tests (NUnit 3)
./ww3-dev.ps1           # dev helper: build, run, test, pre-flight, log cleanup
```

WW3MOD-specific unit tests in `engine/OpenRA.Test/`: `AmmoPoolTest.cs`, `SupplyProviderMathTest.cs`, `SuppressionMathTest.cs`.

**Building while the game is running:** safe on macOS/Linux (`engine/Directory.Build.targets` unlinks outputs first — never disable it, in-place overwrite crashes the running game). On Windows the build just fails fast on locked DLLs — move on or wait quietly, don't alarm or ask the user to close the game.

### Tools (read on demand)
- **Autotest harness** — `./tools/autotest/list-tests.sh`, `run-test.sh <test>`. Full details: [`DOCS/recipes/AUTOTEST.md`](DOCS/recipes/AUTOTEST.md).
- **Demos** — `list-demos.sh`, `run-demo.sh demo-<name>`. Never put a `Test.Pass`/`Fail` in a demo. See [`DOCS/recipes/DEMO.md`](DOCS/recipes/DEMO.md).
- **Screenshots** — in-test `TestHarness.Screenshot(...)` or external `start-screenshot-mode.sh` + `screenshot.sh <label> --wait`. Coarse semantic checks only. See [`DOCS/recipes/SCREENSHOT.md`](DOCS/recipes/SCREENSHOT.md).
- **Combat sim** — `tools/combat-sim/` (`node build/index.js duel abrams t90 --range 18c0`). See [`DOCS/recipes/BALANCE.md`](DOCS/recipes/BALANCE.md).
- **MCP map server** — `tools/map-mcp/`, configured in `.mcp.json`; map creation/editing tools appear in the tool list.
- **shadows.bin regen** — after shadow-pipeline changes, see [`DOCS/reference/architecture.md`](DOCS/reference/architecture.md#regenerating-shadowsbin).

**HARD RULE — no autonomous multi-test runs.** Autotests take minutes each and steal window focus. The agent (including subagents and `/loop` iterations) MUST get an explicit goahead in the current turn before: `run-batch.sh` (any flags), `run-tournament.sh`/`loop-tournament.sh`, any compound command invoking `run-test.sh` more than once, or re-running the same `run-test.sh` more than twice in a row. Running **one** `run-test.sh <test>` for the bug at hand is the normal flow and fine. Narrating "I'll run the sweep now" is not a goahead.

## Coding conventions

### WDist notation
`NcXXX`: `1c0` = 1 cell = 1024 units; `1c512` = 1.5 cells; plain `512` = half a cell.

### WAngle facing — counterclockwise (opposite of typical!)
| WAngle | Direction | Screen |
|--------|-----------|--------|
| 0 | North | Up |
| 256 | **West** | **Left** |
| 512 | South | Down |
| 768 | **East** | **Right** |

Left side facing right: `Facing: 768`. Right side facing left: `Facing: 256`. `WAngle.FromFacing(old)` = old RA facing × 4.

### YAML
- Templates prefixed `^` (`^Aircraft`, `^Helicopter`, `^Drone`, `^Airborne`, `^AirRadar`, …).
- Conditions system: traits grant/consume named conditions (`airborne`, `moving`, `empdisable`, `heavy-damage-attained`, `suppression-*`, `rank-veteran`, `unit.docked`, …) via `GrantConditionOn*` + `RequiresCondition`.
- Faction split: base file + `-america.yaml` + `-russia.yaml` per unit type.
- **Blank lines are significant**: adjacent top-level entries silently merge in MiniYaml — if a template "isn't taking effect," check the blank lines first.

### PITFALL comments (full spec: [`DOCS/reference/pitfalls.md`](DOCS/reference/pitfalls.md))
Recurring traps get a one-line `// PITFALL:` (`# PITFALL:` in YAML) **at the temptation site** — the line a careless reader is looking at when about to fall in. List all: `git grep PITFALL`. Add them during bug fixes when the root cause would surprise a reader; prune when stale (a wrong PITFALL is worse than none). Not for one-shot fixes, generic best practice, or universal anti-patterns (those go in a hook or the rules below).

### Engine code rules (enforced by `tools/git-hooks/pre-commit`)
- **No `Console.Write`/`WriteLine` in tick-path code** — use `Log.Write(channel, ...)`. Allowlisted: `UtilityCommands/`, `UpdateRules/`, `/Lint/`, `OpenRA.Server/`, `OpenRA.Test/`, `OpenRA.Utility/`, `tools/`.
- Hook install (once per clone): `ln -sf ../../tools/git-hooks/pre-commit .git/hooks/pre-commit`

## Architecture & current state

Engine layout, scenario system, custom traits, aircraft movement, suppression/stances, AI config: [`DOCS/reference/architecture.md`](DOCS/reference/architecture.md) — read on demand per system. Live status: `WORKSPACE/RELEASE_V1.md` + `WORKSPACE/HOTBOARD.md` + `git log --oneline -20`. Engine-upgrade considerations: [`DOCS/reference/project-assessment.md`](DOCS/reference/project-assessment.md) §5.
