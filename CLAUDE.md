# WW3MOD - Agent Instructions

WW3MOD is a **total conversion** of OpenRA Red Alert (`release-20230225`, engine in-repo, ~264 C# files modified) into a modern World War 3 RTS. NATO/America vs BRICS/Russia. Repo: https://github.com/WW3MOD/2023.git

## Game model — hard rules (full doc: [`DOCS/reference/game-model.md`](DOCS/reference/game-model.md))

**Do not assume any Red Alert mechanic still applies.** The rules below prevent the recurring wrong assumptions; read the full doc before designing anything strategic.

- **No factories, no tech tree.** Units are **called in as reinforcements from off-map reserves** via the **Supply Route (SR)** — the single production building. They spawn at the nearest map edge and travel to the rally point. Buildings/defenses are the exception (local `Production@Local` queue).
- **The SR is a fixed, indestructible beachhead — not buildable, not placeable.** Each player starts with exactly one; more only by capturing neutral ones. Before any AI/strategic work touching SRs, read [`DOCS/reference/supply-route.md`](DOCS/reference/supply-route.md). The AI YAML listing `supplyroute` under `ConstructionYardTypes` etc. is trait plumbing, not a factory in the strategic sense.
- **Costs are budget allocation**, not manufacturing: destroyed = budget lost; "rotating out" a unit to the map edge recovers its cost.
- **HPAD/AFLD are rearm/repair support**, not production prerequisites.
- **Engine code still contains RA-era assumptions** (e.g. airpad checks). Verify how WW3MOD actually uses a system before trusting old logic.

## Modes and Recipes

One **mode** at a time; **recipes** run on trigger phrases. **Default mode is RELEASE.** Indexes: [`DOCS/modes/`](DOCS/modes/README.md), [`DOCS/recipes/`](DOCS/recipes/README.md).

> These are docs to READ, not harness-registered Skills — never call the `Skill` tool for them. Recognize triggers from natural language too ("show me X" → DEMO).

| Trigger | Doc | Purpose |
|---|---|---|
| `RELEASE` | [modes/RELEASE.md](DOCS/modes/RELEASE.md) | **Mode (default).** v1 methodology — scope-locked, phase-driven, every commit moves a tracker status |
| `EXPERIMENTAL` | [modes/EXPERIMENTAL.md](DOCS/modes/EXPERIMENTAL.md) | **Mode.** Free exploration outside v1 scope |
| `PLAN <topic>` | [recipes/PLAN.md](DOCS/recipes/PLAN.md) | Design before coding — research, ask, plan doc, await approval |
| `PLAYTEST [topic]` | [recipes/PLAYTEST.md](DOCS/recipes/PLAYTEST.md) | Build, write a focus brief, hand back with eye-list |
| `TRIAGE [findings]` | [recipes/TRIAGE.md](DOCS/recipes/TRIAGE.md) | Sort findings into v1 buckets |
| `AUTOTEST <bug>` | [recipes/AUTOTEST.md](DOCS/recipes/AUTOTEST.md) | Test-driven loop — failing test → fix → green → commit. **Default for behavioral fixes in RELEASE mode** even without the trigger |
| `DEMO <topic>` | [recipes/DEMO.md](DOCS/recipes/DEMO.md) | Stage a scenario for the user — no verdict, no autonomous loop. Any "show me" request |
| `REVIEW [N]` | [recipes/REVIEW.md](DOCS/recipes/REVIEW.md) | Quality pass on last N commits |
| `FINALIZE` | [recipes/FINALIZE.md](DOCS/recipes/FINALIZE.md) | Session wrap-up — bell, tracker, hotboard, commit |
| `CONTEXT <area>` | [recipes/CONTEXT.md](DOCS/recipes/CONTEXT.md) | Quick orientation on an area |
| `BALANCE <a> <b>` | [recipes/BALANCE.md](DOCS/recipes/BALANCE.md) | combat-sim driven tuning (tool: `tools/combat-sim/`) |
| `TELEMETRY <events>` | [recipes/TELEMETRY.md](DOCS/recipes/TELEMETRY.md) | Per-tick gameplay log channel (build-on-first-use) |
| `SCREENSHOT <topic>` | [recipes/SCREENSHOT.md](DOCS/recipes/SCREENSHOT.md) | Capture PNGs, evaluate via multimodal `Read`. **Apply automatically for visual work** (UI, palette, lobby, sprites, formations) |
| `DOCUMENT <topic>` | [recipes/DOCUMENT.md](DOCS/recipes/DOCUMENT.md) | Player-perspective mechanic doc in [`DOCS/gameplay/`](DOCS/gameplay/README.md). **Always-on:** flag non-obvious gameplay discoveries |

If a workflow becomes a recurring pattern, factor it into a recipe.

## Workflow Rules

### Git & Commits
- **NEVER push to remote.** The user pushes manually.
- **Commit after every response** unless explicitly told not to or mid-edit would break compilation. Do not ask, do not batch. **Never end a session with uncommitted changes** — this is the #1 workflow rule.
- **Subagents commit their own work** before returning results.
- Frequent small commits with descriptive messages. **No co-author / attribution trailers** (global rule, see `~/.claude/CLAUDE.md`).

### End-of-message block
End every non-trivial response with a fenced block, read bottom-up (terminal glyph last). Skip it for trivial replies or when it would be bigger than the answer. Full spec + examples: [`DOCS/reference/agent-comms.md`](DOCS/reference/agent-comms.md).

Format: `<category-glyph> [face-glyph] <text>`, same-category lines grouped, blank line between categories.

| Glyph | Use |
|:-----:|:----|
| 📁 | files touched (one path per line) |
| ⏸ | future work noted, not done this turn |
| ⚠️ | tradeoffs/risks worth flagging — not blockers |
| 🔀 | options for the user to pick (label A/B/…) |
| 💡 | unprompted suggestions |
| 🧪 | build/test issues only — omit if everything passed |
| ✅ | work completed this turn |
| 👀 | launch the game and try something specific |
| ❔ | input requested but mostly sure — not blocked |
| ❓ | input needed, blocked until answered |
| 📦 | committed; work continues |
| 🏁 | finished — all done, committed |
| ⏭️ | phase done; awaiting goahead |

Face glyphs (optional prefix on text): 🤔 uncertain · 😬 risky call · 😅 hacky · 🤷 guessed · 🤨 skeptical · 🥳 big win

Terminal line is exactly one of `📦`/`🏁`/`⏭️`/`❓`/`❔`. Canonical order: `📁` → `⏸` → `⚠️` → `🔀` → `💡` → `🧪` → `✅` → `👀` → status. **Less is more** — only categories with something non-trivial to say.

### STOP AND ASK
Never autonomously ship a change that downgrades quality, capability, or UX (removing a working feature, capping a value lower, reducing visual fidelity) — even justified by cleanup/perf. Spell out the downgrade with `⚠️` lines, end with `❓`, wait.

### Self-updating instructions
Update this CLAUDE.md (without asking) when new information makes it obsolete; call the change out with a `✅` line.

### External rules
Apply all confirmed rules from `C:\Users\fredr\Desktop\ClaudeRules\confirmed\`.

### Session workflow
On start: read `WORKSPACE/HOTBOARD.md` + `WORKSPACE/RELEASE_V1.md`; scan `WORKSPACE/DISCOVERIES.md` for recent entries; Glob `WORKSPACE/archive/sessions/active_*.md` and read any (may be a parallel agent — avoid its files).

For multi-session/multi-file work: write `WORKSPACE/archive/sessions/active_<YYMMDD_HHMM>_<topic>.md` (task, intended files, status); promote to `<YYMMDD>_<topic>.md` on FINALIZE. Skip for single-shot fixes.

During session: unrelated bugs → `WORKSPACE/bugs/discovered.md`; non-obvious insights → `WORKSPACE/DISCOVERIES.md` (dated); playtest findings → `WORKSPACE/playtests/`, then TRIAGE. Conventions and folder map: [`WORKSPACE/README.md`](WORKSPACE/README.md).

Ring the terminal bell (`printf "\a"`) when a significant task completes.

## Folders

- **`WORKSPACE/`** — living state: tracker (`RELEASE_V1.md`, source of truth), `HOTBOARD.md`, `BACKLOG.md`, plans, archive. See [`WORKSPACE/README.md`](WORKSPACE/README.md).
- **`DOCS/`** — static reference: modes, recipes, reference, gameplay. See [`DOCS/README.md`](DOCS/README.md).

## Build & Run

```bash
./make.ps1 all          # Windows build (targets net6, runs on .NET 8+); `make all` on Linux/macOS
./launch-game.sh        # run (launch-game.cmd on Windows); auto-builds first
make test               # YAML validation (needs .NET 6 runtime specifically)
dotnet test engine/OpenRA.Test/OpenRA.Test.csproj --configuration Release   # unit tests (NUnit 3)
./ww3-dev.ps1           # dev helper: build, run, test, pre-flight, log cleanup
```

Solution: `WW3MOD.sln`. Engine compiles to `engine/bin/`. WW3MOD-specific unit tests in `engine/OpenRA.Test/`: `AmmoPoolTest.cs`, `SupplyProviderMathTest.cs`, `SuppressionMathTest.cs`.

**Building while the game is running:** safe on macOS/Linux (`engine/Directory.Build.targets` unlinks outputs first — never disable it, in-place overwrite crashes the running game). On Windows the build just fails fast on locked DLLs — move on or wait quietly, don't alarm or ask the user to close the game.

### Tools (read on demand)
- **Autotest harness** — `./tools/autotest/list-tests.sh`, `run-test.sh <test>`. Full details: [`DOCS/recipes/AUTOTEST.md`](DOCS/recipes/AUTOTEST.md).
- **Demos** — `list-demos.sh`, `run-demo.sh demo-<name>`. Never put a `Test.Pass`/`Fail` in a demo. See [`DOCS/recipes/DEMO.md`](DOCS/recipes/DEMO.md).
- **Screenshots** — in-test `TestHarness.Screenshot(...)` or external `start-screenshot-mode.sh` + `screenshot.sh <label> --wait`. Coarse semantic checks only. See [`DOCS/recipes/SCREENSHOT.md`](DOCS/recipes/SCREENSHOT.md).
- **Combat sim** — `tools/combat-sim/` (`node build/index.js duel abrams t90 --range 18c0`). See [`DOCS/recipes/BALANCE.md`](DOCS/recipes/BALANCE.md).
- **MCP map server** — `tools/map-mcp/`, configured in `.mcp.json`; 17 map creation/editing tools appear in the tool list.
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
