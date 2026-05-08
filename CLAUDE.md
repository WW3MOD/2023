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
📁 CLAUDE/RELEASE_V1.md

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
📁 CLAUDE/playtests/260503_1530_garrison.md

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
1. Read `CLAUDE/HOTBOARD.md` and `CLAUDE/RELEASE_V1.md`; scan `CLAUDE/DISCOVERIES.md` for recent entries
2. Glob `CLAUDE/sessions/active_*.md` — if any exist, read them (may be a parallel agent). Note their intended files to avoid conflicts

For multi-session or multi-file work:
- Write `CLAUDE/sessions/active_<YYMMDD_HHMM>_<topic>.md` at the start (task summary, intended files, status: in-progress)
- Promote to `CLAUDE/sessions/<YYMMDD>_<topic>.md` on FINALIZE
- Skip for single-shot bug fixes or trivial edits — the commit history is enough record

During session:
- Unrelated bugs found → append to `CLAUDE/bugs/discovered.md`
- Non-obvious insights → append to `CLAUDE/DISCOVERIES.md` (dated)
- Stable patterns that apply broadly → also add to this CLAUDE.md
- Playtest findings → `CLAUDE/playtests/<YYMMDD_HHMM>_<topic>.md`, then TRIAGE into `RELEASE_V1.md`

### Completion Bell
Ring the terminal bell (`printf "\a"`) when a significant task is complete, so the user knows to check back.

## Release Mode (v1)

The single source of truth for v1 scope is `CLAUDE/RELEASE_V1.md`. Anything in v1 must appear there with a status.

**Status legend:** `[ ]` open · `[~]` in-progress · `[T]` testing · `[x]` done · `[v1.1]` deferred · `[cut]` won't-fix v1

**Scope is locked.** No new feature enters v1 without an explicit "yes, add to v1" from the user. Ideas raised during work go to `RELEASE_V1.md` under "Pending decisions" until triaged, or straight to `BACKLOG.md` if clearly v1.1.

**Three phases (rough order, not strict):**
- **Phase A — Stabilize** — verify everything currently "needs playtesting"
- **Phase B — Tier-1 fixes** — bugs and gameplay gaps that block release
- **Phase C — Polish** — sounds, icons, descriptions, polish threads

**Workflow loop:** PLAYTEST → user plays → user reports → TRIAGE → fix → repeat. Use RELEASE for a status snapshot at any time.

Update `RELEASE_V1.md` continuously as items change status — and commit when you do.

## Commands

User can type these keywords to trigger specific workflows. Commands are **uppercase** for clarity.

### PLAN
Enter planning mode for a new feature or change.
1. Ask the user clarifying questions — keep asking until the user says the plan is sufficient
2. During planning, research relevant code (read files, grep for patterns, check existing systems)
3. Write the final plan to `CLAUDE/plans/<YYMMDD>_<topic>.md` with:
   - Goal, constraints, affected files, step-by-step implementation, risks/open questions
4. Summarize the plan in chat and wait for approval before implementing

### FINALIZE
Mandatory wrap-up routine. Run after completing any feature/fix.
1. `printf "\a"` — ring the bell
2. Double-check work against `CLAUDE/DISCOVERIES.md` — ensure nothing was violated
3. Update `CLAUDE/RELEASE_V1.md` — flip statuses for items touched (e.g. `[~]` → `[T]` or `[x]`); move shipped items to "Recently completed"
4. Update `CLAUDE/HOTBOARD.md` — refresh "Working on" and recent wins
5. Promote session file (if any): rename `active_*.md` → `CLAUDE/sessions/<YYMMDD>_<topic>.md`
6. Update `CLAUDE/BACKLOG.md` — add deferred items, mark completed with `[x]`
7. Auto-commit all changes (descriptive message)
8. Review this CLAUDE.md — new pattern? Structural change? Recurring gotcha? Update if yes

### PLAYTEST [topic?]
Prepare for a focused playtest session.
1. Build the project (`./make.ps1 all`).
2. Pick focus: if a topic is given, scope to it; otherwise pull the highest-risk untested items from `CLAUDE/RELEASE_V1.md` Phase A.
3. Write `CLAUDE/playtests/<YYMMDD_HHMM>_<topic>.md` with: build hash, focus list, what to look for, edge cases to try, expected behavior.
4. Hand back to the user with a `👀` line listing specific things to try.
5. After the user reports findings, run TRIAGE.

### TRIAGE [findings]
Sort raw playtest findings (or any pasted observation list) into the right buckets.
1. For each item, decide: critical-blocker (Phase A/B), v1-fix (Phase B/C), tuning, defer-v1.1, won't-fix, or pending-decision.
2. Update `CLAUDE/RELEASE_V1.md` with new entries, status changes, or "Pending decisions" lines.
3. If clearly off-scope (not v1, not v1.1), file under `CLAUDE/BACKLOG.md`.
4. Bugs found incidentally during other work → also append to `CLAUDE/bugs/discovered.md`.
5. Confirm what was added/updated and where in the end-of-message block.

### RELEASE
Quick view of v1 release status.
1. Read `CLAUDE/RELEASE_V1.md`, recent git log (10 commits).
2. Print: current phase, count by status (open / in-progress / testing / done), top blockers, anything drifting (untouched recently, or sitting in `[T]` for too long).

### STATUS
Quick orientation on where things stand (general, not release-specific).
1. Read `CLAUDE/HOTBOARD.md`, recent git log (5 commits), and any `active_*.md` sessions
2. Print a concise summary: what was last worked on, what's active now, what's next

### BUGFIX <description>
Structured bug investigation.
1. Reproduce understanding — ask user for repro steps if not provided
2. Research: grep for relevant code, read related files, check DISCOVERIES.md for known patterns
3. Form hypothesis, implement fix
4. Add to `CLAUDE/bugs/discovered.md` if found while working on something else
5. If the bug reveals a new gotcha, add to DISCOVERIES.md and consider adding to Common Pitfalls in CLAUDE.md

### REVIEW
Review recent changes for quality.
1. `git diff HEAD~N` (ask user for range, default last commit)
2. Check for: common pitfalls (see below), leftover debug code, YAML formatting issues, missing condition wiring
3. Report findings, fix issues with user approval

## CLAUDE/ Folder

Claude's workspace for session tracking, plans, discoveries, and notes. Primarily for Claude's use across sessions, secondarily for user reference.

```
CLAUDE/
├── RELEASE_V1.md        # Single source of truth for v1 scope and status
├── HOTBOARD.md          # What's in motion right now (max 40 lines)
├── BACKLOG.md           # Deferred tasks & ideas ([ ]/[x]/[dropped])
├── DISCOVERIES.md       # Dated gotchas and insights
├── plans/               # Plan documents (from PLAN command)
├── playtests/           # Raw playtest findings (one file per session)
├── sessions/            # Session logs (active_* = in-progress, multi-session work only)
└── bugs/
    └── discovered.md    # Bugs found incidentally
```

**Rules:**
- RELEASE_V1.md is the v1 source of truth — nothing enters v1 without showing up here
- HOTBOARD.md stays under 40 lines — rotate oldest items out
- Session files: only for multi-session/multi-file work. Promote `active_*` → dated file on FINALIZE
- Never delete another agent's `active_*` file
- DISCOVERIES.md entries are always dated
- BACKLOG.md uses `[ ]` pending, `[x]` done, `[dropped]` irrelevant
- Playtest reports are raw and historical — never edit a past report; triage updates `RELEASE_V1.md`
- No duplication between CLAUDE/ files and auto-memory (`.claude/projects/`)

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

### Developer Test Harness
```bash
./tools/test/list-tests.sh                              # List available tests
./tools/test/run-test.sh <test-folder>                  # Run one test
./tools/test/run-batch.sh <t1> <t2> ...                 # Run several
./tools/test/run-batch.sh --all                         # Run every test-* folder
./tools/test/run-test.sh --position=left <test>         # Force left half
./tools/test/run-test.sh --fullscreen <test>            # Skip sizing/positioning
./tools/test/run-test.sh --help                         # Flag list
```
Drops the game straight into a named map under `mods/ww3mod/maps/<test-folder>/` with a slim title-bar UI: **End** key restarts the scenario; the rest is just title + description text. Verdict happens in chat; the script exits when the game window closes.

**Window placement (windowed only).** Default is `--position=auto` — the script reads the frontmost window via System Events (one-time accessibility grant required, falls back gracefully). Most reliable path is to run `--position=left` or `--position=right` once per terminal; the choice is saved at `~/.ww3mod-tests/position-prefs/<tty-key>` and reused automatically. Window position passes through `OPENRA_WINDOW_X/Y` env vars (engine-side patch in `Sdl2PlatformWindow.cs`).

**Edge-pan disabled** in test+windowed mode (engine-gated on `TestMode.IsActive && Graphics.Mode == Windowed` in `ViewportControllerWidget`). Cursor crossing the window border into the terminal no longer scrolls the camera.

**Gating.** Activated only by `Test.Mode=true` launch arg. Without it, every part of the harness is dormant — no widget, no panel, no file writes, no engine-side overrides. Normal launches are completely unaffected.

**Reusable Lua helpers** (`mods/ww3mod/scripts/test-helpers.lua`):
- `TestHarness.FocusBetween(actor1, actor2, ...)` — center camera on the midpoint of N actors.
- `TestHarness.Select(actor)` — pre-select unit-under-test on world load (saves a click). Wraps `UserInterface.Select(actor)`.
- `TestHarness.AssertWithin(seconds, predicate, failReason)` — poll a predicate every tick; `Test.Pass()` when it returns `true`, `Test.Fail(reason)` on timeout. Predicate may also return a `"fail: <reason>"` string to fail immediately. Tests built on this need no human verdict.
- `TestHarness.AssertAfter(seconds, predicate, failReason)` — wait `seconds`, then assert the predicate is true.

**Auto-asserting Lua globals** (`Test.*`, all no-op outside test mode):
- `Test.Pass()` — write `pass` verdict and exit.
- `Test.Fail(reason)` — write `fail` verdict + reason and exit.
- `Test.Skip(reason)` — write `skip` verdict + reason and exit.

A test that uses `AssertWithin` runs unattended — the script exit code (0/1/2/3) tells the runner the verdict. Pair with `run-batch.sh --all` for a hands-off regression sweep.

**Adding a test:**
1. Copy an existing test folder under `mods/ww3mod/maps/test-<name>/` (e.g. `cp -r mods/ww3mod/maps/test-artillery-turret mods/ww3mod/maps/test-<name>`).
2. **`map.yaml`:** set `Visibility: MissionSelector` and `Categories: Test` so it stays out of the lobby map list. Use lowercase actor names (`e1.russia`, not `E1.russia`).
3. **Only ONE `Playable: True` PlayerReference** — the human slot. All other factions must be `Playable: False`. `Launch.Map` only creates Player objects for slots with a client; an unclaimed `Playable: True` slot drops its actors to Neutral and breaks targeting (no attack cursor, no auto-engage).
4. **Lock colors and factions** on every PlayerReference (`LockColor: True`, `LockFaction: True`) so the visual cue is consistent across runs — human=blue, enemies=red, allies=green — regardless of the dev's personal `settings.yaml`.
5. **`description.txt`** (optional, recommended): one-line description shown in the panel. The runner reads `<map-folder>/description.txt`, first non-empty line wins.
6. **`rules.yaml`:** `LuaScript: Scripts: test-helpers.lua, <test>.lua` (helpers first).
7. **`<test>.lua`:** `WorldLoaded = function() TestHarness.FocusBetween(a, b); TestHarness.Select(a) end` — staging only, no UI text.
8. Run with `./tools/test/run-test.sh test-<name>`.

**Test types:**
- *Manual* — Lua only stages (camera, selection); the human watches and types in chat. Example: `test-artillery-turret`.
- *Auto-asserting* — Lua uses `TestHarness.AssertWithin(...)` to verdict itself. The runner exits with the test's pass/fail status, no human input needed. Example: `test-paladin-fires` (asserts the Paladin's primary ammo drops below max within 8 s of world-load).

## Project Architecture

```
WW3MOD/
├── engine/                         # Modified OpenRA release-20230225
│   ├── OpenRA.Game/                # Core engine (Map, Actor, Graphics, Network)
│   ├── OpenRA.Mods.Common/         # Shared traits, activities, widgets (MOST changes here)
│   │   ├── Traits/                 # Unit behaviors, conditions, targeting
│   │   │   ├── Air/                # Aircraft movement (Aircraft.cs, Fly.cs, Land.cs)
│   │   │   └── ...
│   │   ├── Activities/             # Movement, attack, resupply activities
│   │   ├── Warheads/               # Damage, suppression, effects
│   │   ├── Projectiles/            # Bullet.cs, Missile.cs (bypass system)
│   │   └── Widgets/                # UI widgets (MiniMap, CommandBar)
│   ├── OpenRA.Mods.Cnc/            # C&C-specific (some removed/modified)
│   └── OpenRA.Platforms.Default/
├── mods/ww3mod/                    # Mod content (~178MB)
│   ├── rules/
│   │   ├── ingame/                 # Unit definitions (22 YAML files)
│   │   │   ├── aircraft.yaml       # Base aircraft templates (^Aircraft, ^Helicopter, ^Drone)
│   │   │   ├── aircraft-america.yaml / aircraft-russia.yaml
│   │   │   ├── infantry.yaml       # Base infantry templates
│   │   │   ├── infantry-america.yaml / infantry-russia.yaml
│   │   │   ├── vehicles-america.yaml / vehicles-russia.yaml
│   │   │   ├── structures.yaml / structures-defenses.yaml
│   │   │   ├── defaults.yaml       # Global defaults (^ExistsInWorld, ^GainsExperience, etc.)
│   │   │   └── world.yaml          # World actor, factions, palettes
│   │   ├── weapons/                # Weapon definitions (7 files)
│   │   ├── ai/                     # AI configuration (ai.yaml, ai-america.yaml, ai-russia.yaml)
│   │   └── misc.yaml               # Crates, mines, misc actors
│   ├── maps/                       # 13 maps (snow, temperate, urban)
│   ├── bits/                       # Sprites, sounds, models
│   ├── chrome/                     # UI layouts
│   ├── sequences/                  # Animation definitions
│   └── mod.yaml                    # Mod manifest
├── DOCS/                           # Documentation
│   ├── TODO.md                     # Prioritized v1 release TODO (Tier 1/2/3)
│   ├── KNOWN_BUGS.md               # Active and fixed bugs
│   ├── IDEAS.md                    # Feature ideas (collected from old notes)
│   ├── CLAUDE_IDEAS.md             # Claude's own feature suggestions
│   ├── SPRITE_REFERENCES.md        # Asset references from other OpenRA mods
│   ├── UnitManagement.md           # Group Scatter docs and unit control ideas
│   └── PROJECT_ASSESSMENT.md       # Comprehensive project assessment (March 2026)
├── tools/                          # Development tools
│   └── map-mcp/                    # MCP Map Creation Server (TypeScript/Node.js)
│       ├── src/index.ts            # MCP server with 14 map editing tools
│       ├── src/map-bin.ts          # Binary map.bin reader/writer (format 2)
│       ├── src/map-yaml.ts         # MiniYaml map.yaml reader/writer
│       └── src/tileset.ts          # Tileset definition parser
├── .mcp.json                       # MCP server configuration
├── CLAUDE.md                       # This file
├── WW3MOD.sln                      # Visual Studio solution
├── Makefile / make.ps1             # Build system
└── mod.config                      # Build configuration
```

## Scenario System

Scenarios are scripted map variants that share terrain with a base map but add different units, players, and Lua scripts. They appear in the lobby map chooser under the "Scenario" category.

### How It Works
- A scenario is a **separate map folder** that copies `map.bin` (terrain) from a base map
- Has its own `map.yaml` (different actors, players), `rules.yaml` (LuaScript reference), and `.lua` script
- Uses `Categories: Scenario` to appear in the Scenario filter in the map chooser
- No engine C# changes needed — everything runs on OpenRA's existing Lua scripting API
- Supports multiplayer + bots — human players take specific slots, bots fill the rest

### Creating a Scenario
1. Create a new map folder: `mods/ww3mod/maps/<base-map>-<scenario-name>/`
2. Copy `map.bin`, `shadows.bin`, `map.png` from the base map
3. Write `map.yaml` with:
   - `Categories: Scenario` and `LockPreview: True`
   - Custom players (human playable + non-playable garrison/AI factions)
   - Pre-placed actors (garrison units, supply routes, objectives)
4. Write `rules.yaml` with `LuaScript: Scripts: scenario.lua, <your-script>.lua`
5. Write your scenario `.lua` script using the `Scenario` helper library

### Scenario Lua Library (`mods/ww3mod/scripts/scenario.lua`)
Reusable helpers for scenario scripts:
- **Spawning**: `Scenario.SpawnUnit()`, `Scenario.SpawnGroup()`, `Scenario.ReinforceFromEdge()`
- **Ownership Transfer**: `Scenario.TransferAll(from, to)`, `Scenario.ScheduleTransfer(from, to, delaySec)`
- **Wave Spawning**: `Scenario.ScheduleWave(wave, delaySec)`, `Scenario.ScheduleWaves(waves, base, interval)`
- **Patrol/Defense**: `Scenario.Patrol(actors, waypoints)`, `Scenario.DefendPosition(actors)`
- **Messaging**: `Scenario.Message(text)`, `Scenario.SetBriefing(text)`, `Scenario.PlaySpeech(player, notif)`
- **Objectives**: `Scenario.AddPrimaryObjective(player, desc)`, `Scenario.CompleteObjective(player, id)`
- **Utility**: `Scenario.GetLiving(tag)`, `Scenario.CountLiving(tag)`, `Scenario.OnGroupEliminated(tag, cb)`

### Naming Convention
Scenario titles follow the format **`<Scenario>: <Map Name>`** — scenario name first, then the base map. This lets the same scenario type apply across multiple maps (e.g., "Frontline: River Zeta WW3", "Frontline: Siberian Pass WW3"). Feels like a game mode.

### Example: Frontline: River Zeta WW3 (`maps/river-zeta-frontline/`)
- 2 human player slots (NATO west, Russia east) each with a Supply Route
- NATOGarrison / RussiaGarrison — allied non-playable players with frontline troops
- After 3 minutes, garrison units transfer to human player control
- Enemy reinforcement waves (5 on Normal, scaling with difficulty dropdown)
- Difficulty dropdown: Easy (3 waves, 60% strength), Normal (5 waves), Hard (7 waves, 140% strength)

### Key Lua APIs Used
| API | Purpose |
|---|---|
| `actor.Owner = player` | Transfer unit ownership |
| `Actor.Create(type, true, inits)` | Spawn new actors |
| `Reinforcements.Reinforce(owner, types, path, interval)` | Edge reinforcements |
| `Trigger.AfterDelay(ticks, func)` | Timed events |
| `Trigger.OnAllKilled(actors, func)` | Group elimination triggers |
| `player.AddPrimaryObjective(desc)` | Mission objectives |
| `UserInterface.SetMissionText(text)` | HUD briefing text |
| `Media.DisplayMessage(text, prefix)` | Chat log messages |
| `Media.PlaySpeechNotification(player, notif)` | EVA voice lines |

## Key Engine Modifications

These are the custom systems that set WW3MOD apart from base OpenRA. Understanding these is critical before modifying any engine code.

### Renamed/Rewritten Core Systems
| Original → Custom | Purpose |
|---|---|
| Shroud.cs → MapLayers.cs | Complete vision/shroud rework with graduated visibility |
| ShroudRenderer → MapLayersRenderer | Rendering for new vision system |
| Crushable.cs → Passable.cs | Richer obstacle interaction (fences, mines, trees) |
| TakeCover.cs → InfantryStates.cs | Infantry behavior model (prone at suppression > 30) |
| AffectsRadar → Radar.cs + Detectable.cs | Multi-layer detection/visibility |
| RadarWidget → MiniMapWidget | Renamed + reworked minimap |

### Custom Traits (new files)
| Trait | Purpose |
|---|---|
| Detectable.cs | Graduated visibility (cloaked/spotted/revealed) with additive modifiers |
| BlocksSight.cs | Objects that block line of sight |
| Radar.cs | Custom radar detection with range/conditions |
| ShockwaveDamageWarhead.cs | Explosive blast wave effects |
| InfantryStates.cs | Infantry states replacing TakeCover |
| EjectOnHusk.cs | Crew ejection from destroyed vehicles |
| GarrisonManager.cs | Shelter/port deployment model with IDamageModifier (indestructible at 1HP), dynamic ownership (enter→claim, empty→neutral), suppression-aware ports (duck at 30+, recall at 60+, lockout), ambush integration |
| GarrisonProtection.cs | Damage pass-through to shelter occupants only (port soldiers have DamageMultiplier via garrisoned-at-port condition) |
| GarrisonPortOccupant.cs | ITargetable on infantry: directional port targetability — soldiers only targetable by enemies within port's firing arc |
| WithGarrisonDecoration.cs | Garrison pips (centered bottom) + protection % text overlay (color-coded) + empty port indicators |
| GarrisonPanelLogic.cs | Sidebar panel for garrison management (shows deployed + shelter soldiers) — pending icon rewrite |
| SupplyProvider.cs | Greatest-need resupply: 1 pip per cycle, cycles to unit with most need, limited supply capacity |
| QuickRearm.cs | Enter-truck instant rearm: infantry enters Cargo, auto-ejected after delay with full ammo |
| HealerClaimLayer.cs | World trait: prevents multiple medics targeting same patient (healer→patient 1:1 claims) |
| HealerAutoTarget.cs | IOverrideAutoTarget: smart healer targeting — HP% scoring, critical priority, stabilize-and-switch |
| VehicleCrew.cs | Vehicle crew slots (Driver/Gunner/Commander), eject on critical damage, re-entry, commander substitution |
| CrewMember.cs | Crew infantry trait: IIssueOrder for re-entering vehicles with matching empty slots |
| EnterAsCrew.cs | Activity: crew walks to vehicle, fills slot, gets removed from world |
| SmartMove.cs | IWrapMove + INotifyDamage: wraps Move orders so units selectively fire while moving (self-defense or unsaturated targets). Overkill check via AverageDamagePercent |
| SupplyRouteContestation.cs | Graduated SR control bar: enemy vs friendly value comparison, depletion/recovery, IProductionSpeedModifier for dynamic production slowdown, visual/audio feedback |
| UnitDefaultsManager.cs | World trait: per-type stance defaults persisted to `Platform.SupportDir/ww3mod/unit-defaults.yaml`. Ctrl+Alt+Click stance buttons sets type default for all future units |
| CohesionMoveModifier.cs | World trait (IModifyGroupOrder): offsets group move targets based on CohesionMode (Tight/Loose/Spread). Preserves relative formation shape with capped offsets |
| PatrolOrderGenerator.cs | Order generator for patrol waypoint queuing mode. Click adds waypoints, click Patrol again to confirm |
| Patrol.cs (Activity) | Loops waypoints with attack-move. Circular if last≈first waypoint, otherwise bounce (A→B→C→B→A→...) |
| HeliEmergencyLanding.cs | Helicopter emergency landing: autorotation on heavy damage (steerable, safe landing, crew evacuates to neutral), uncontrolled crash on critical (spinning, destroyed, everyone dies). Integrates with VehicleCrew for crew ejection and AllowForeignCrew for capture |
| CargoSupply.cs | TRUK-only numeric supply pool. Auto-rearms nearby allied AmmoPool units within RearmRange. Pool drains as ammo is given; never regenerates. `IIssueDeployOrder` drops the entire pool as a SUPPLYCACHE on the truck's cell (merges into existing cache if present). `IIssueOrder` lets the player right-click an LC to queue a delivery move. Empty + Auto stance seeks nearest LC for refill (LC's pool drains 1:1); Hold sits; Evacuate rotates to map edge for credit return |
| CargoPanelLogic.cs | Sidebar panel for transport cargo management: individual eject, mark for waypoint unload, rally points, supply drop |
| CargoUnloadOrderGenerator.cs | Click-on-map order generator for waypoint-based selective unloading of marked passengers |
| EjectRallyOrderGenerator.cs | Click-on-map to set per-passenger post-eject rally point (move target on ejection) |

### Heavily Modified Systems
| File | Key Changes |
|---|---|
| DamageWarhead.cs | Suppression hooks, damage falloff, bypass integration |
| AutoTarget.cs | Value-based targeting priorities |
| Armament.cs | Multi-weapon, reload timing, ammo integration |
| AmmoPool.cs | Extended ammo/resupply mechanics, SupplyValue per-ammo cost |
| Bullet.cs | Projectile bypass through obstacles (BlocksProjectiles) |
| Aircraft.cs | Velocity-based movement for helicopters (see Aircraft section) |
| Fly.cs | Acceleration/deceleration for CanSlide aircraft |
| Missile.cs | FlyStraightIfMiss (missiles fly straight after passing target) |
| PlayerResources.cs | Economy/upkeep modifications |
| Map.cs | Map loading, bounds, layer support |
| AttackGarrisoned.cs | Rewritten: per-port firing via GarrisonManager, legacy fallback preserved |

## Aircraft Movement System

The air branch introduced a **dual movement system** that is important to understand:

### Helicopters (CanSlide = true)
Use velocity-based movement with acceleration/deceleration:
- `Aircraft.CurrentVelocity` — current movement vector
- `Aircraft.RequestedAcceleration` — set by Fly activity each tick
- `Aircraft.CalculateAccelerationToWaypoint()` — computes acceleration toward target, includes maintenance accel to prevent speed oscillation
- `Aircraft.CalculateStopPosition()` — predicts stop position using discrete semi-implicit Euler formula
- Movement applied in `Aircraft.Tick()` via `CurrentVelocity` (decel THEN move)
- `Fly.Tick()` has a **fully separate CanSlide code path** — only sets RequestedAcceleration, never calls FlyTick
- Always brakes toward target (stopAtWaypoint=true), even when activities queued after
- Altitude adjustment during flight: gradually climbs/descends toward CruiseAltitude while flying
- On arrival: snaps to exact target position, zeros CurrentVelocity. Skips climb if next is Land
- Landing: smooth speed-proportional descent (fast=high, slow=low), gentle touchdown near ground
- Takeoff: rise to halfway CruiseAltitude, then start moving forward while climbing rest
- Pitch applied during horizontal movement in Aircraft.Tick (FlyTick isn't called for CanSlide)
- **CRITICAL**: Never use FlyTick for CanSlide without zeroing CurrentVelocity first (double movement)

### Fixed-Wing (CanSlide = false)
Use traditional step-based movement:
- `Aircraft.FlyStep()` — returns movement vector for current speed/facing
- `Fly.FlyTick()` — applies movement, handles altitude, roll, pitch
- Turns computed via `Fly.CalculateTurnRadius()`

### Key Aircraft YAML Properties
```yaml
Aircraft:
    Speed: 100                  # Movement speed
    TurnSpeed: 12               # Turn rate (WAngle units)
    IdleTurnSpeed: 8            # Turn rate when idle
    IdleSpeed: 25               # Speed when idle/patrolling
    MaxAcceleration: 5          # Acceleration per tick (for CanSlide)
    RotationAcceleration: 2     # Turn acceleration (for CanSlide)
    CruiseAltitude: 3c768       # Normal flight altitude
    AltitudeVelocity: 100       # Vertical movement speed
    CanSlide: True/False        # Helicopter vs fixed-wing
    CanHover: True/False        # Can stop mid-air
    VTOL: True/False            # Vertical takeoff/landing
    Repulsable: True/False      # Pushed away from other aircraft
    MaximumPitch: 56            # Max climb/dive angle
```

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

## Suppression System (Complete)

**Infantry suppression (10-tier, cap 100, decay 1/5 ticks):**
- `GrantExternalCondition` warheads with Amount/Range for graduated suppression
- Speed/vision/burst/inaccuracy multipliers (90%→0% across tiers)
- InfantryStates triggers prone at suppression > 30
- 10-tier pip display (pip-suppression-1 through pip-suppression-10)

**Vehicle suppression (5-tier, cap 50, decay 1/3 ticks):**
- Only medium caliber (12.7mm+) and explosions suppress vehicles
- Turret turn speed reduced (85%→25%), inaccuracy increased (115%→200%)
- Burst wait increased (105%→150%), NO speed reduction
- Defined in `^VehicleSuppressionEffects` template in vehicles.yaml

**Fire discipline (3 stances — controls WHEN to fire):**
- HoldFire, Ambush, FireAtWill (default)
- Ambush: pre-aim at targets, hold fire until spotted or damaged, coordinate with nearby allies
- FireAtWill: fire at any valid target in range
- Conditions: `stance-fireatwill`, `stance-ambush`, `stance-holdfire`

**Engagement stances (3 stances — controls WHERE to position):**
- HoldPosition, Defensive (default), Hunt
- Separate from fire stances — two independent UI bars (3 buttons each)
- Hunt: chase targets aggressively, even out of range
- Defensive: fire from current position, reposition only if LOS blocked (Phase 2: cover-seeking via ShadowLayer)
- HoldPosition: never auto-reposition, only fire from current cell
- Hotkeys: Alt+A/G/F (fire), Ctrl+Alt+A/D/F (engagement)
- Engagement stance drives `allowMove` in AutoTarget scanning and movement decisions in Attack activity

**Cohesion stances (3 stances — controls HOW close together, Phase 1 UI only):**
- Tight, Loose (default), Spread
- Hotkeys: Ctrl+Alt+1/2/3
- Phase 3 will modify waypoint distribution on group moves (not repulsion — too laggy for ground units)

**Resupply behavior (3 stances — controls WHAT to do when out of ammo):**
- Hold (stay put, flag NeedsResupply for supply truck pickup), Auto (seek nearest supply point, default), Evacuate (leave via Supply Route)
- Only shown for units with AmmoPool trait
- Hotkeys: Ctrl+Alt+4/5/6
- AutoRearmIfAllEmpty checks stance: Auto=seek, Hold=flag+wait, Evacuate=RotateToEdge
- Supply trucks in Hunt stance actively seek NeedsResupply-flagged units map-wide

**Click-modifier meta-system (all 4 stance bars):**
- Click: Set stance for current selection (immediate)
- Ctrl+Click: Set per-unit default (unit remembers even after resets)
- Ctrl+Alt+Click: Set per-type default — all future units of this type spawn with this. Persisted to disk via UnitDefaultsManager
- Alt+Click: "Do Now" order — Fire/Engagement: set stance + cancel all orders. Resupply: immediate action (go resupply/stop/evacuate). Cohesion: set stance + reposition group

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

## Current State & Priorities (March 2026)

### Recently Completed
- P0 bugs fixed (Nuclear Winter crash, River Zeta crash, TECN infiltration)
- Dev branch merged (68 commits: doubled HP, critical at 50%, shroud smoothing)
- Air branch merged with fixes (velocity movement, deceleration, fixed-wing restored)
- Modifiers branch merged (attack-move modifier keys)
- AI improved (balanced builds, aircraft enabled, repair/capture modules)
- Aircraft costs restored (were all set to 1 for testing)
- **Suppression system completed** — vehicle suppression added (turret slow + inaccuracy, medium/large/huge caliber only), infantry suppression tuned
- **Stances system implemented** — 3+3 system: Fire discipline (HoldFire, Ambush, FireAtWill) + Engagement (HoldPosition, Defensive, Hunt). Ambush pre-aims and coordinates nearby allies
- **Vehicle reverse movement** — tracked (50 ticks, 40% speed) and wheeled (30 ticks, 60% speed) can reverse instead of 180° turns
- **Supply Route edge spawning** — units now spawn at map edge and march to rally point; buildings/defenses still spawn locally
- **Bleedout animation fixed** — rot sequences now use existing e1 sprite frames instead of missing rot1-4/corpse1-3 files
- **Helicopter movement precision fixed** — corrected physics formulas (semi-implicit Euler), eliminated double movement in Land/FlyIdle, precise position snapping on arrival, velocity zeroing on all Fly exit paths, maintenance acceleration to prevent speed oscillation
- **Supply Route defeat behavior fixed** — SR turns neutral on owner defeat instead of being killed; ProximityCapturable no longer permanently locks when owner is defeated
- **All buildings turn neutral on owner defeat** — `^Building` template's `OwnerLostAction` changed from `Kill` to `ChangeOwner` (defaults to Neutral). Defeated player's structures persist as capturable Neutral buildings (`Capturable@neutral` already in `^NeutralOrOccupiedCapturable`). Goes with the Supply Route / Logistics Center fixes; now consistent across the whole structure tree
- **GrantConditionOnPrerequisite owner-change crash fixed** — base OpenRA bug. Manager is per-player, but `OnOwnerChanged` only rebound the cached reference instead of moving the registration. Any owner change (capture, OwnerLostAction, garrison transfer, scenario transfer) followed by removal threw `KeyNotFoundException`. Now unregisters from old manager and re-registers with new in `OnOwnerChanged`
- **Rotate/sell to map edge** — units ordered to rotate via Supply Route now walk to the map edge (biased toward SpawnArea), not to the SR building. SR acts as a proxy target
- **Vehicle reverse sliding fixed** — reverse condition re-evaluated at each cell transition; stops reversing when path curves away from behind the unit
- **Group Scatter hotkey (Alt+S)** — distributes queued waypoints among selected units by type (inspired by Supreme Commander FAF). See `DOCS/UnitManagement.md`
- **Garrison system overhaul (Phases 1-6)** — Indestructible buildings (IDamageModifier clamps to 1HP, damaged sprite at critical), dynamic ownership (building changes to garrisoning player, reverts to neutral when empty, allies share), directional port targetability (GarrisonPortOccupant ITargetable with reverse arc check — soldiers only targetable from within port's firing arc), suppression integration (duck at 30+, force-recall at 60+, SuppressionLockoutTicks prevents feeding into suppressed ports), protection % text overlay (color-coded green/yellow/red), pips centered at bottom, force-fire-only building targeting when garrisoned. Phase 4 (sidebar icon panel) pending
- **Supply truck → pure transport** — TRUK uses the `CargoSupply` trait (numeric supply pool, separate from passenger cargo). **TRUK is the *only* unit with `CargoSupply`** — the earlier idea of letting helicopters carry supply has been rejected; CargoSupply was removed from TRAN/HALO/HIND in 260504. SUPPLYCACHE is NoAutoTarget + ProximityCapturable (1.5 cell, sticky ownership transfer). Supply economy is closed-loop: trucks come from map edge with supply, trucks can deposit at LC (refilling LC's pool), LC refills trucks pip-by-pip when in Auto stance. **Nothing auto-regenerates** — no Tick-based supply growth on truck, LC, or cache. Once map-edge trucks stop, the supply pool only shrinks.
- **Cargo management system (Phases 2A-2E)** — Full transport cargo UI: individual passenger eject, mark passengers for waypoint-based selective unload (Deploy Marked → click map → transport moves+unloads), per-passenger rally points (R button → click map → passenger auto-moves there on ejection), supply unload as SUPPLYCACHE with merge at same location, Mark All/Unmark All toggle
- **Medic/Engineer smart auto-targeting** — HealerClaimLayer prevents medic pile-ups (1:1 healer→patient claims). HealerAutoTarget (IOverrideAutoTarget) scores by HP%, prioritizes critical patients (<50%, bleeding out), stabilizes to 50% then switches. Engineers use same trait without claims (multiple can repair same vehicle)
- **Vehicle crew system** — VehicleCrew trait manages Driver/Gunner/Commander slots. On critical damage (<50% HP), crew eject one-by-one as infantry with vehicle's rank. Missing crew progressively disables systems (no driver=immobile, no gunner=turret frozen, no commander=inaccuracy). Commander substitutes at reduced efficiency. Crew can re-enter repaired vehicles via CrewMember trait. 14 vehicles configured (2-crew light vehicles, 3-crew MBTs/IFVs). Crew evacuated via supply route return 100 credits each
- **Infantry mid-cell redirect** — Infantry can now change direction mid-cell instead of finishing their current cell transition before responding to new move orders. MovePart made conditionally interruptible via `CanRedirectMidCell` on MobileInfo. On cancel, reverts cell occupancy to FromCell and starts new move from actual WPos (no visual snap). Sharp direction changes (>90°) apply a speed penalty scaling from 100% to `RedirectSpeedPenalty`% at 180°. Vehicles left unchanged (finish cell transitions as before)
- **Three-mode move system** — Move (right-click): SmartMove wrapping fires only in self-defense (under fire) or when target isn't already saturated with incoming damage (overkill check via AverageDamagePercent). Attack-Move (A+click): unchanged, fires at everything. Force-Move (Ctrl+click): pure movement via "ForceMove" order string, bypasses SmartMove wrapping entirely
- **Stance system consolidated to 3+3** — Removed redundant stances (ReturnFire, Defend, AttackAnything, Balanced). Fire discipline: HoldFire/Ambush/FireAtWill. Engagement: HoldPosition/Defensive/Hunt. 6 total buttons (was 9). All enums, conditions, UI, hotkeys, and YAML updated across engine and all mods. Phase 2 (shadow-based cover seeking for Defensive) planned in `DOCS/SHADOW_LOS_PLAN.md`
- **Control bar overhaul** — Added Cohesion (Tight/Loose/Spread) and Resupply Behavior (Hold/Auto/Evacuate) stance bars. Click-modifier meta-system on all 4 bars: Click=set stance, Ctrl+Click=per-unit default, Ctrl+Alt+Click=per-type default, Alt+Click="Do Now" order (persisted across games via UnitDefaultsManager). Evacuate command button removed (folded into Resupply bar). Tooltips anchor above buttons. Medic/Engineer default to Hunt engagement. Resupply behavior implemented: Auto seeks supply, Hold flags for truck pickup, Evacuate leaves via SR. Supply trucks in Hunt stance seek flagged units map-wide. Cohesion distributes group move targets via IModifyGroupOrder (CohesionMoveModifier). Patrol system with waypoint queuing (PatrolOrderGenerator → PatrolActivity bounce/circular loop)
- **Supply Route contestation system** — Replaced binary ProximityContestable with graduated SupplyRouteContestation trait. Control bar (0-100%) depleted by net enemy value surplus in 10-cell range: 5 infantry (~2500 value) depletes in 60s, full company in 20s min. Production speed scales linearly below 50% bar (100%→0%), halts at 0%. Auto-recovery when enemies leave, 3x faster with friendlies. Full feedback: player-colored selection bar visible to all, building flash, EVA "BaseAttack" notification, text log, minimap ping. New IProductionSpeedModifier interface with accumulator pattern in ProductionQueue for dynamic per-tick speed control. SR is indestructible — enemies can only deny, never capture
- **Helicopter emergency landing system** — Two-tier: Heavy damage triggers controlled autorotation (player steers, helicopter glides forward losing altitude, lands safely on suitable terrain as disabled+repairable unit, crew/passengers evacuate). Critical damage triggers uncontrolled crash (configurable spinning for tail rotor loss, always destroyed on impact, everyone dies). Mid-air destruction = crew dies (suppress-eject condition gates EjectOnDeath). Chinook/HALO: SpinsOnCrash=false for dual rotors. RepairableBuilding activated on crash-disabled condition for ground repair
- **Helicopter crew overhaul** — VehicleCrew added to all helicopters with realistic crew slots: Pilot+Copilot (transports: TRAN, HALO), Pilot+Gunner (attack: HELI, HIND, MI28), Pilot only (littlebird). No pilot = can't fly (SpeedMultiplier 0). No copilot = 75% speed. No gunner = no weapons. Safe landing ejects crew as infantry, helicopter goes Neutral, AllowForeignCrew enables capture-by-pilot-entry (any player's pilot can enter to claim ownership). RepairableBuilding.ValidRelationships field added — neutral crashed helis repairable by anyone. Critical crash = total loss (suppress-eject stays active, self.Kill kills everyone). EnterAlliedActorTargeter extended to allow enemy targets when AllowForeignCrew is set
- **Scenario system** — Map scenario variants sharing terrain (map.bin) with different actors/scripts. Reusable `scenario.lua` Lua library (spawn, ownership transfer, waves, patrol, objectives, messaging). First scenario: "River Zeta — Frontline" — garrison forces + timed transfer + enemy waves + difficulty dropdown. No engine C# changes — pure Lua + YAML. `Categories: Scenario` for map chooser filtering

### Next Priorities
1. **Helicopter crash + crew overhaul playtesting** — verify: critical crash kills all (no crew ejects), safe landing evacuates crew+passengers to neutral, capture by entering pilot, anyone repairs neutral helis, no-pilot gate prevents flight, neutral not auto-targeted
2. **Stance system playtesting** — verify modifier system (Click/Ctrl/Ctrl+Alt/Alt), resupply behavior (Auto seek, Hold flag, Evacuate via SR), medic/engineer Hunt default, tooltips anchored above, cohesion distribution on group moves, patrol waypoint queuing and looping
3. **Supply Route contestation playtesting** — verify bar depletes/recovers, production slowdown, notifications
4. **Shadow falloff + firing LOS** — distance-based shadow falloff, Defensive stance cover-seeking. See `DOCS/SHADOW_LOS_PLAN.md`
5. **Three-mode move playtesting** — tune OverkillThreshold, UnderFireDuration, verify Force-Move never fires
6. **Infantry mid-cell redirect playtesting** — tune RedirectSpeedPenalty (currently 50%), verify no visual glitches
7. **Vehicle crew playtesting** — tune ejection delays, commander substitution values, test re-entry flow
8. **Cargo system playtesting (Phases 2A-2E)** — verify TRUK auto-rearms, supply bar, individual eject, mark+waypoint unload, rally points, supply drop to SUPPLYCACHE, merge at same location
9. **Cargo management Phase 3** — template sidebar for pre-loaded transport purchasing
10. **Garrison system overhaul playtesting** — verify: indestructible buildings (1HP min), dynamic ownership (enter→claim, neutral on empty), directional port targeting (reverse arc), suppression duck/recall/lockout, protection % text, force-fire-only building targeting
11. **Garrison sidebar panel icon rewrite** — Phase 4 pending: full icon-based panel with click-to-select, force-fire from port, exit-move orders
10. **Suppression tuning** — playtest and balance vehicle suppression values, per-weapon fine-tuning

### Remaining Branches
- `skane`/`xavi` — cherry-pick useful parts (map data, sprites)
- `maps` — extract useful maps
- `bypass`, `counterbattery`, `speed` — stale, can likely delete

### Engine Upgrade Consideration
Upgrading to `release-20250330` is possible but major (estimated 12-22 sessions). The recommendation is to finish gameplay features first, then consider a selective backport or full upgrade. See `DOCS/PROJECT_ASSESSMENT.md` Section 5 for full analysis.

## AI Configuration

AI is configured entirely via YAML in `mods/ww3mod/rules/ai/`:
- `ai.yaml` — ModularBot setup, shared modules (BuildingRepairBotModule, CaptureManagerBotModule, SquadManagerBotModule for air, HelicopterSquadBotModule)
- `ai-america.yaml` — America-specific build priorities, unit limits, squad composition
- `ai-russia.yaml` — Russia-specific same

Key AI modules: `UnitBuilderBotModule` (what to build), `SquadManagerBotModule` (how to attack), `HelicopterSquadBotModule` (helicopter attack/scout/transport squads), `CaptureManagerBotModule` (what to capture), `BuildingRepairBotModule` (auto-repair).

**Important for aircraft modules:** Helicopter `UnitBuilderBotModule` uses `SkipRearmBuildingCheck: true` because helicopters are called in via Supply Route and don't need an HPAD to be produced. Without this flag, the old RA check (`HasAdequateAirUnitReloadBuildings`) blocks aircraft production when no rearm building exists.

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

**Note:** Build fails if the game is running (DLLs locked). This is normal — the user often playtests while agents work. If a build fails for this reason, just move on to other work or wait quietly. Do not speculate, alarm, or ask the user to close the game. `launch-game` auto-builds before launching, so the user will get a fresh build on next playtest.

## Common Pitfalls

1. **Don't leave Console.WriteLine in engine code** — fires every tick, causes massive log spam
2. **Aircraft Cost: 1** — test values left in YAML. Always verify costs after air branch changes
3. **CanSlide vs non-CanSlide** — Fly.Tick has fully separate code paths. CanSlide sets RequestedAcceleration only (Aircraft.Tick moves via CurrentVelocity). Fixed-wing uses FlyTick (step-based). NEVER use FlyTick for CanSlide without zeroing CurrentVelocity first — causes double movement
4. **SeedsResource on maps without IResourceLayer** — causes crashes. Disable or remove SeedsResource actors
5. **FrozenActor.Actor can be null** — always null-check before accessing after superweapons
6. **YAML blank lines matter** — templates must be separated by blank lines
7. **Engine changes are in-repo** — no submodule, no separate DLL. Every C# edit touches OpenRA source directly
