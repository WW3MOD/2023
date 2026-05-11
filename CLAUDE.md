# WW3MOD - Agent Instructions

## Identity

WW3MOD is a **total conversion** of OpenRA Red Alert into a modern World War 3 RTS. It is NOT a simple YAML mod — it rewrites 264 C# engine files (~6300 insertions, ~3300 deletions) on top of OpenRA `release-20230225`. The engine lives in-repo (not submodule), with `AUTOMATIC_ENGINE_MANAGEMENT="False"`.

**Authors:** FreadyFish (primary dev) & CmdrBambi
**Repository:** https://github.com/WW3MOD/2023.git
**Factions:** NATO/America vs BRICS/Russia (Ukraine planned as third)

## How WW3MOD Differs from Red Alert (CRITICAL — read before any work)

WW3MOD is NOT Red Alert with new sprites. The entire gameplay model is different. **Do not assume any Red Alert mechanic still applies.** Key differences:

### Reinforcement Model (no factories)
There are **no Construction Yards, Barracks, War Factories, or Naval Yards**. Units are NOT "built" — they are **called in as reinforcements from off-map reserves** via the **Supply Route** building. Think of the Supply Route as a radio/logistics hub that requests reinforcements, not a factory that manufactures units.

- **Supply Route** is the single core production building. It produces ALL unit types (infantry, vehicles, aircraft) via `ProductionFromMapEdge` — units spawn at the map edge and march/fly to the rally point.
- **Buildings and defenses** are the exception — they spawn locally at the Supply Route via a separate `Production@Local` queue.
- **HPAD (Helipad)** and **AFLD (Airfield)** are **rearm/repair support buildings**, not production prerequisites. Helicopters and planes CAN be produced without them. HPADs/AFLDs let aircraft rearm faster on-map instead of flying back to the map edge. Future plans include capturable HPADs on maps.
- **"Buying" a unit** = calling in a reinforcement from reserves. **"Rotating out" a unit** = sending it back to the map edge to recover its budget cost. This is the economy loop.
- **Unit costs represent budget allocation**, not manufacturing cost. A destroyed unit is a permanent loss of that budget.

### No tech tree / building prerequisites
There is no "build barracks → build war factory → build radar → unlock X" progression. Tech levels exist (`~techlevel.low/medium/high`) but they are granted automatically based on game time or other conditions, not by constructing specific buildings. Any unit the player's tech level allows can be called in immediately.

### Map-edge spawning
Units don't appear at the production building — they enter from the map edge nearest to the Supply Route's SpawnArea hint, then walk/fly across the map to the rally point. This means:
- Production has inherent travel time (far Supply Route = slow reinforcements)
- Enemy can ambush reinforcements en route
- Supply Route position matters strategically (closer to friendly edge = safer reinforcements)

### Engine code still has old RA patterns
Many engine files still contain classic RA assumptions (e.g., `HasAdequateAirUnitReloadBuildings` checking for 1 airpad per aircraft). When you encounter these patterns, understand they may not apply. Always check how WW3MOD actually uses the system before assuming the old logic is correct. The `SkipRearmBuildingCheck` YAML property on `UnitBuilderBotModule` was added specifically to bypass one such legacy check.

## Modes and Recipes

I operate in one **mode** at a time and follow documented **recipes** when triggered. Modes live in [`DOCS/modes/`](DOCS/modes/README.md); recipes live in [`DOCS/recipes/`](DOCS/recipes/README.md). Each entry is a single `.md` with the trigger phrase up top and the procedure below.

**Default mode is RELEASE** — assume v1-release methodology unless the user has explicitly switched to EXPERIMENTAL.

> **These are docs to READ, not harness-registered Skills.** When the user says a trigger word (e.g., "DEMO this", "demo this", or "show me ..."), READ the relevant `.md` and follow the procedure. Do **not** call the `Skill` tool — that's Claude Code's separate system and will fail with `Unknown skill`. The leading slash is optional; recognize the trigger from natural language too.

| Trigger | Doc | Purpose |
|---|---|---|
| `RELEASE` | [DOCS/modes/RELEASE.md](DOCS/modes/RELEASE.md) | **Mode (default).** v1 methodology — scope-locked, phase-driven, every commit moves a tracker status |
| `EXPERIMENTAL` | [DOCS/modes/EXPERIMENTAL.md](DOCS/modes/EXPERIMENTAL.md) | **Mode.** Free exploration outside v1 scope — looser, idea-friendly |
| `PLAN <topic>` | [DOCS/recipes/PLAN.md](DOCS/recipes/PLAN.md) | Design before coding — research, ask, plan doc, await approval |
| `PLAYTEST [topic]` | [DOCS/recipes/PLAYTEST.md](DOCS/recipes/PLAYTEST.md) | Build, write a focus brief, hand back with eye-list |
| `TRIAGE [findings]` | [DOCS/recipes/TRIAGE.md](DOCS/recipes/TRIAGE.md) | Sort findings into v1 buckets — RELEASE_V1, BACKLOG, discovered |
| `AUTOTEST <bug>` | [DOCS/recipes/AUTOTEST.md](DOCS/recipes/AUTOTEST.md) | Test-driven loop — failing test → fix → green → regression-check → commit. **Default for behavioral fixes in RELEASE mode** even without the trigger; once stated, the stance applies to the whole batch. |
| `DEMO <topic>` | [DOCS/recipes/DEMO.md](DOCS/recipes/DEMO.md) | Stage a scenario for the user to look at — same harness as AUTOTEST, but **no verdict, no autonomous loop**. Use whenever the user says "show me" / "set this up so I can see". |
| `REVIEW [N]` | [DOCS/recipes/REVIEW.md](DOCS/recipes/REVIEW.md) | Quality pass on last N commits |
| `FINALIZE` | [DOCS/recipes/FINALIZE.md](DOCS/recipes/FINALIZE.md) | Session wrap-up — bell, tracker, hotboard, commit |
| `CONTEXT <area>` | [DOCS/recipes/CONTEXT.md](DOCS/recipes/CONTEXT.md) | Quick orientation on an area — recent commits + open work + file pointers |
| `BALANCE <a> <b>` | [DOCS/recipes/BALANCE.md](DOCS/recipes/BALANCE.md) | combat-sim driven tuning — duels, tier consistency |
| `TELEMETRY <events>` | [DOCS/recipes/TELEMETRY.md](DOCS/recipes/TELEMETRY.md) | Per-tick gameplay log channel for post-mortem analysis (build-on-first-use) |

If a workflow becomes a recurring pattern, factor it into a recipe rather than re-explaining it each session.

## Workflow Rules

### Git & Commits
- **NEVER push to remote.** The user will push manually.
- **Commit after every response.** At the end of every message back to the user, all changes MUST be committed unless explicitly told not to or there is a concrete reason (e.g., mid-edit that would break compilation). Do not ask — just commit. Do not batch — commit what you have NOW.
- **Subagents commit their own work.** Every agent (including spawned subagents) must commit changes before returning results. No agent should leave uncommitted changes for another agent or the user to clean up.
- Frequent small commits are preferred over batched changes. Create descriptive commit messages.
- **ALWAYS commit before ending a session.** Never leave uncommitted changes behind. If you made code changes, commit them — even if you didn't run FINALIZE. This is the #1 most important workflow rule.

### Communication Format

End every non-trivial response with a structured **end-of-message block**. Reading is bottom-up: terminal status glyph at the very bottom, supporting detail above. The user reads the terminal glyph first to identify the tab and what's expected of them.

**Skip the block** for trivial responses — one-line factual answers, pure clarification questions, or any reply where the block would be bigger than the answer itself. Mid-turn narration before tool calls ("Reading the file", "Committing now") stays as plain prose; the block rule applies only at end-of-turn.

**Format.** Single fenced code block. One **category glyph** at column 0, then the text. Group consecutive same-category lines together; blank line between categories. Optional **face glyph** prefix on the text for nuance.

```
<category> [face] <text>
```

**Categories** (only render sections with content):

| Glyph | Use |
|:-----:|:----|
| 📁 | files touched (one path per line) |
| ⏸ | future work noted, not done this turn |
| ⚠️ | tradeoffs, risks, limits worth flagging — not blockers, not errors |
| 🔀 | options for the user to pick (one per line, label A/B/…) |
| 💡 | unprompted suggestions you think the user might want |
| 🧪 | build/test issues only — omit if everything passed |
| ✅ | work completed this turn — list what was done |
| 👀 | launch the game and try something specific |
| ❔ | input requested but mostly sure — not blocked |
| ❓ | input needed, blocked until answered |
| 📦 | committed; work continues, no specific input needed right now |
| 🏁 | finished — all done with the request, committed |
| ⏭️ | phase done; awaiting goahead before next phase |

**Face glyphs** (optional; prefix to the text only, never in the glyph column):

🤔 uncertain · 😬 risky judgment call · 😅 hacky but works · 🤷 had to guess · 🤨 skeptical · 🥳 big win

**Terminal status.** The final line of the block is exactly one of: `📦` · `🏁` · `⏭️` · `❓` · `❔`. Mutually exclusive — pick one. The user reads this first.

**Canonical order** (top → bottom, least → most critical): `📁` → `⏸` → `⚠️` → `🔀` → `💡` → `🧪` → `✅` → `👀` → `❔`/`❓` → `📦`/`🏁`/`⏭️`

**Less is more.** Only include a category when it has something non-trivial to say. Silence means "nothing worth flagging there".

- `🧪` — absence means tests/build passed. Only include on issue.
- `👀` — only for specific behaviors to watch for. "Go try it" is implicit otherwise.
- `💡` — only for genuine new ideas, not restatements of agreed work.
- `⚠️` — only for real tradeoffs, not generic disclaimers.

**Examples**

Triage finished, items added to v1 tracker:

```
📁 WORKSPACE/RELEASE_V1.md

✅ added 4 bugs to Phase B (artillery burst, ATGM lock, drone autotarget, palette)
✅ moved garrison overhaul to [T] (testing) — 5 specific checks listed
✅ deferred ammo-cost-money to v1.1

📦
```

Bug fix shipped, idea floated for later:

```
📁 engine/OpenRA.Mods.Common/Traits/Air/HeliEmergencyLanding.cs:142

✅ 😬 fixed crashed-heli capture — added foreign-crew check before ownership transfer
💡 same logic could give us recoverable wrecks for vehicles in v1.1

🏁
```

Playtest set up, game ready:

```
📁 WORKSPACE/playtests/260503_1530_garrison.md

✅ build clean, focus list written (garrison ports, ownership transfer, suppression duck/recall)

👀 launch a 2v1 on River Zeta, garrison both ports, force-fire from inside, take damage to 60%+ suppression

⏭️
```

If even one line feels like padding, skip the block entirely.

**No co-author / attribution trailers.** Do not add `Co-Authored-By: Claude …` or any equivalent attribution to commit messages, amends, or PR bodies. This is a global rule — see `~/.claude/CLAUDE.md`.

**STOP AND ASK rule:** Never autonomously ship a change that downgrades quality, capability, or user experience — even when justified by build cleanup or perf. Examples: removing a working feature, capping a value lower than current, reducing visual fidelity. Show the block with `⚠️` lines spelling out the downgrade and end with `❓` for sign-off; wait before writing code.

### Self-Updating Instructions
- **Continuously update this CLAUDE.md** when you receive new information that makes current content obsolete.
- Do this without asking first.
- A `✅` line in the end-of-message block calls out the change so the user can spot what shifted.

### External Rules
Apply all confirmed rules from: `C:\Users\fredr\Desktop\ClaudeRules\confirmed\`

### Session Workflow
On session start:
1. Read `WORKSPACE/HOTBOARD.md` and `WORKSPACE/RELEASE_V1.md`; scan `WORKSPACE/DISCOVERIES.md` for recent entries
2. Glob `WORKSPACE/archive/sessions/active_*.md` — if any exist, read them (may be a parallel agent). Note their intended files to avoid conflicts

For multi-session or multi-file work:
- Write `WORKSPACE/archive/sessions/active_<YYMMDD_HHMM>_<topic>.md` at the start (task summary, intended files, status: in-progress)
- Promote to `WORKSPACE/archive/sessions/<YYMMDD>_<topic>.md` on FINALIZE
- Skip for single-shot bug fixes or trivial edits — the commit history is enough record

During session:
- Unrelated bugs found → append to `WORKSPACE/bugs/discovered.md`
- Non-obvious insights → append to `WORKSPACE/DISCOVERIES.md` (dated)
- Stable patterns that apply broadly → also add to this CLAUDE.md
- Playtest findings → `WORKSPACE/playtests/<YYMMDD_HHMM>_<topic>.md`, then TRIAGE into `RELEASE_V1.md`

### Completion Bell
Ring the terminal bell (`printf "\a"`) when a significant task is complete, so the user knows to check back.

## Folders

Two top-level documentation folders:

- **`WORKSPACE/`** — living state. Tracker, hotboard, backlog, plans, archive. Mutable, frequently edited. See [`WORKSPACE/README.md`](WORKSPACE/README.md).
- **`DOCS/`** — static reference. Skills (workflow triggers) and system reference. See [`DOCS/README.md`](DOCS/README.md).

Plus this file (`CLAUDE.md`) at the repo root as the agent entry point.

**Workspace conventions:**
- `RELEASE_V1.md` is the v1 source of truth — nothing enters v1 without showing up here
- `HOTBOARD.md` stays under 40 lines — rotate oldest items out
- `DISCOVERIES.md` entries are always dated
- `BACKLOG.md` uses `[ ]` pending, `[x]` done, `[dropped]` irrelevant
- Playtest reports under `playtests/` are raw and historical — never edit a past report; TRIAGE updates `RELEASE_V1.md`
- Session logs go to `archive/sessions/` directly on FINALIZE — sessions are historical the moment they finish
- No duplication between WORKSPACE/ files and auto-memory (`.claude/projects/`)

## Build & Run

```bash
# Windows (PowerShell)
./make.ps1 all          # Full build (targets net6, but runs on .NET 8+)

# Linux/macOS
make all

# Run the game
./launch-game.sh        # or launch-game.cmd on Windows

# Test (YAML validation)
make test               # Note: requires .NET 6 runtime specifically
```

The solution file is `WW3MOD.sln`. The engine compiles to `engine/bin/`. Mod DLL is `OpenRA.Mods.WW3MOD.dll` (referenced in mod.config but not yet a separate project).

### MCP Map Server
```bash
cd tools/map-mcp && npm install && npx tsc   # Build
```
Configured in `.mcp.json`. Provides 17 tools for map creation/editing: `create_map`, `read_map`, `list_maps`, `fill_terrain`, `paint_terrain`, `get_tileset_info`, `place_actors`, `remove_actors`, `list_actor_types`, `set_players`, `set_spawn_points`, `set_map_rules`, `write_lua_script`, `generate_preview`, `place_template`, `draw_road`, `auto_shore`.

### Combat Balance Simulator
```bash
cd tools/combat-sim && npm install && npx tsc   # Build
node build/index.js run <scenario>               # Run scenario
node build/index.js duel abrams t90 --range 18c0 # Quick 1v1
node build/index.js list                          # List scenarios
node build/index.js units                         # List units
node build/index.js stats <unitId>                # Unit details
```
Tick-by-tick combat simulator for balance analysis. Models damage (penetration, directional armor, range falloff, AoE), weapon firing cycles, suppression (infantry 10-tier/vehicle 5-tier), and formations. Phase 1 uses hardcoded stats; Phase 2 will auto-load from YAML. Phase 5 will export scenarios as playable maps via MCP.

### Developer Test Harness — see `DOCS/recipes/AUTOTEST.md`
Trigger phrase: `AUTOTEST <bug or feature>`. Quick reference:
```bash
./tools/autotest/list-tests.sh                      # what's available
./tools/autotest/run-test.sh <test-folder>          # run one
./tools/autotest/run-batch.sh --all                 # regression sweep
```
Drops the game into a deterministic scenario under `tools/autotest/scenarios/test-*/`, writes a JSON verdict, exit-codes the result back to the runner. Activated only by `Test.Mode=true` launch arg — normal launches are unaffected. The scenarios folder is registered in `mod.yaml` under `MapFolders` with class `Unknown` so it stays out of every in-game chooser. Full details (writing tests, Lua API, gotchas, engine integration points) in [`DOCS/recipes/AUTOTEST.md`](DOCS/recipes/AUTOTEST.md).

### Demo scenarios — see `DOCS/recipes/DEMO.md`
Trigger phrase: `DEMO <topic>` (or any "show me / set this up so I can see" request). Same harness as AUTOTEST, different stance — agent stages, user runs and explores, no verdict expected.
```bash
./tools/autotest/list-demos.sh                      # what's available
./tools/autotest/run-demo.sh demo-<name>            # launch one
```
Demo scenarios live in `tools/autotest/scenarios/demo-*/`. **Never put a `Test.Pass`/`Fail` call in a demo** — if it has a verdict, it's a test; move it to `test-*` and use AUTOTEST.

### Regenerating shadows.bin

Each map keeps a precomputed `shadows.bin` LOS cache. Changes to the shadow compute pipeline (e.g. `CellLayer.IsValidCoordinate`, `RecomputeShadowFrom`, density formulas) invalidate every cached file — the bug stays baked in until the cache is rebuilt. Two ways to refresh:

```bash
./utility.sh --regen-shadows ../mods/ww3mod/maps/<name>   # narrow: only rewrites shadows.bin
./utility.sh --refresh-map ../mods/ww3mod/maps/<name>     # wide: also rewrites map.yaml and map.png
```

Note the `../` — `utility.sh` cd's into `engine/` before running. Saving a map in the in-game editor also triggers a regen. After a shadow-compute fix, refresh every map under `mods/ww3mod/maps/` that has a `shadows.bin` (currently: `river-zeta-ww3`, `woodland-warfare-ww3`).

## Architecture & system reference

Project layout, scenario system, custom traits, aircraft movement, suppression, AI configuration — all in [`DOCS/reference/architecture.md`](DOCS/reference/architecture.md). Read on demand when working on a specific system.

<!-- Scenario System, Key Engine Modifications, Custom Traits, Heavily Modified Systems
     all moved to DOCS/reference/architecture.md -->

## WDist Notation

OpenRA uses `WDist` (World Distance) units throughout. The notation is `NcXXX`:
- `1c0` = 1 cell = 1024 WDist units
- `4c0` = 4 cells
- `1c512` = 1.5 cells (1024 + 512 = 1536)
- `3c768` = 3.75 cells
- `0c512` = 0.5 cells
- Plain numbers like `512` = 512 WDist units (half a cell)

## WAngle Facing Convention

OpenRA uses `WAngle` for facings with **counterclockwise** rotation (0–1024 range). This is the OPPOSITE of typical clockwise conventions — easy to get wrong!

| WAngle | Direction | Screen Direction (top-down) |
|--------|-----------|---------------------------|
| 0      | North     | Up                        |
| 256    | **West**  | **Left**                  |
| 512    | South     | Down                      |
| 768    | **East**  | **Right**                 |

**Quick reference for map placement:**
- Units on the LEFT side facing right: `Facing: 768` (East)
- Units on the RIGHT side facing left: `Facing: 256` (West)
- Conversion: `WAngle.FromFacing(oldFacing)` where old RA facing × 4 = WAngle

## YAML Conventions

### Templates (prefixed with ^)
```yaml
^Aircraft:          # Base template for fixed-wing planes
^Helicopter:        # Base template for helicopters
^Drone:             # Base template for drones
^Airborne:          # Common airborne traits
^NeutralAirborne:   # Airborne without faction-specific traits
^AirRadar:          # Radar trait for aircraft (range 24c0)
```

### Conditions System
Traits grant and consume named conditions:
```yaml
GrantConditionOnDamageState:
    Condition: heavy-damage-attained    # Granted at heavy damage
SpeedMultiplier@HeavyDamage:
    Modifier: 90
    RequiresCondition: heavy-damage-attained
```

Common conditions: `airborne`, `cruising`, `moving`, `empdisable`, `dronedisable`, `heavy-damage-attained`, `critical-damage`, `rank-veteran`, `suppression-*`, `unit.docked`

### Faction-Specific Files
Each unit type has a base template file and two faction files:
- `aircraft.yaml` → `aircraft-america.yaml` + `aircraft-russia.yaml`
- `infantry.yaml` → `infantry-america.yaml` + `infantry-russia.yaml`
- `vehicles-america.yaml` + `vehicles-russia.yaml`

### Blank lines are significant
Templates and top-level entries must be separated by a blank line. The MiniYaml parser silently merges adjacent ones, producing confusing override behavior — not a parse error. If a template "isn't taking effect," check the blank lines first.

## Current state

Live status — read `WORKSPACE/RELEASE_V1.md` (source of truth), `WORKSPACE/HOTBOARD.md` (in-flight), `WORKSPACE/BACKLOG.md` (deferred). For an overview, run `git log --oneline -20`. Engine-upgrade consideration: see `DOCS/reference/project-assessment.md` Section 5.

## Testing

Unit tests live in `engine/OpenRA.Test/` (NUnit 3). Run with:
```bash
dotnet test engine/OpenRA.Test/OpenRA.Test.csproj --configuration Release
```

**WW3MOD-specific tests** (added March 2026):
- `AmmoPoolTest.cs` — GiveAmmo/TakeAmmo clamping, initial ammo, SupplyValue, FullReloadTicks math
- `SupplyProviderMathTest.cs` — distance-based delay formula, supply deduction, selection bar
- `SuppressionMathTest.cs` — infantry/vehicle tier progressions, decay timing, caps, prone threshold

**Dev helper script:** `./ww3-dev.ps1` — build, run, test, pre-flight checks, debug log cleanup

**Building while the game is running.** Safe on both platforms, by different mechanisms:
- **macOS/Linux:** `engine/Directory.Build.targets` unlinks each output before MSBuild's Copy. unlink(2) leaves the running game's mmap'd inode alive while the next build creates a fresh inode at the same path. Build succeeds, game keeps running, next launch picks up the new DLLs. (Without this shim, an in-place overwrite corrupts the mmap and crashes the game with "Cannot print exception string..." + Abort trap 6 — never disable the targets file.)
- **Windows:** the OS locks loaded DLLs at the kernel level, so the build fails fast if the game is running. Just move on to other work or wait quietly — do not speculate, alarm, or ask the user to close the game. `launch-game` auto-builds before launching.

## PITFALL Comments

Recurring traps get a one-line `// PITFALL:` (or `# PITFALL:` in YAML) comment **at the temptation site** in the code — the line a careless reader would actually be looking at when about to fall in. To list every known trap: `git grep PITFALL`.

**The temptation-site rule.** An anchor only helps if the reader's eyes are on it when at risk. So:
- ✅ trap = the file/line I'm editing → anchor at that line
- ⚠️ trap = an API I call from elsewhere → anchor at the API definition (partially works via grep)
- ❌ trap = a universal habit ("don't do X anywhere") → no anchor location helps. Use a hook or a one-liner in this file.

Placing a PITFALL where the broken *code* lives (rather than where the *temptation* arises) is dead weight — see commit history for the Log.cs case where this went wrong.

**Format:**
- Literal tag `PITFALL` (greppable). One line, WHY only — what breaks if you ignore it.
- Link out for longer context: `// PITFALL: facing is counterclockwise — see DOCS/reference/architecture.md#wangle`.
- At the temptation line, not in a function header. Cap ~3 per file — more is a refactor signal.
- Date when tied to an incident: `// PITFALL (2026-03): Cost: 1 shipped to main, broke balance`.

**When to write one:**
- Bug fix where the root cause would surprise a reader.
- Non-local invariant enforced elsewhere ("don't reorder these two lines").
- A trap Claude or the user has hit more than once.
- An OpenRA quirk that bites only WW3MOD's modified usage.

**Don't write one for:**
- "What" descriptions — well-named code is enough.
- Generic best-practice (null checks, input validation) absent a specific incident.
- One-shot fixes — comments are for *recurring* traps.
- **Universal anti-patterns** (don't use X anywhere) — the temptation arises in arbitrary files. Use a pre-commit hook (see `tools/git-hooks/`) or a line under "Engine code rules" below.

**Pruning:** when changing code near a `PITFALL`, re-read it. Outdated → remove or update. A wrong PITFALL is worse than no PITFALL because I'll trust it.

**How the system grows.** New PITFALLs are added *during bug fixes*, not via mass passes. AUTOTEST step 8 prompts for one after every green; RELEASE bug-fix flow does the same; FINALIZE checks for them in the wrap. The compounding happens at the moment of fix when context is freshest — that's the only phase that scales.

**Backfill (occasional, high-precision only):**

```bash
git log --grep='regression\|came back\|still broken\|again\b' --oneline -- '*.cs' '*.yaml'
```

Bug fixes that explicitly note recurrence are real PITFALL candidates. One-shot bugs usually aren't — adding anchors for them creates noise that erodes the signal of the real ones. Avoid exhaustive "walk every file" passes; they over-comment and the bar slips.

### Engine code rules (universal anti-patterns)

These can't be anchored at a single trap site — they apply across the engine. Enforced by `tools/git-hooks/pre-commit` where possible.

- **No `Console.Write`/`WriteLine` in tick-path code.** Use `Log.Write(channel, ...)`. Console output fires every tick and floods stdout. Allowlisted directories: `UtilityCommands/`, `UpdateRules/`, `/Lint/`, `OpenRA.Server/`, `OpenRA.Test/`, `OpenRA.Utility/`, `tools/`.

**Hook install (one-time, per clone):**
```bash
ln -sf ../../tools/git-hooks/pre-commit .git/hooks/pre-commit
```
