# WW3MOD - Agent Instructions

WW3MOD is a **total conversion** of OpenRA Red Alert (`release-20230225`, engine in-repo, ~264 C# files modified) into a modern World War 3 RTS. NATO/America vs BRICS/Russia. Solution `WW3MOD.sln`; engine compiles to `engine/bin/`; mod content in `mods/ww3mod/`.

## Hard rules

- **Do not assume any Red Alert mechanic still applies — the gameplay model is rebuilt.** No factories, no tech tree: units are called in as reinforcements from off-map reserves via the **Supply Route** — a fixed, indestructible, non-buildable beachhead (one per player). Costs are budget allocation, not manufacturing. **The design intends additional Supply Routes via capturing neutral ones, but that is NOT wired** — `SUPPLYROUTE` carries no `Capturable` and no `CaptureManager`, and both reference docs flag it as designed-but-unimplemented. Treat "one per player" as the shipped reality; do not write code, tests or player-facing copy that assumes capture works. **What DOES ship, and what players call "capturing the SR", is `SupplyRouteContestation`** (`engine/OpenRA.Mods.Common/Traits/SupplyRouteContestation.cs`, on `SUPPLYROUTE` at `mods/ww3mod/rules/ingame/structures.yaml:303`, inside the `SUPPLYROUTE:` actor that opens at `:222`): enemy units near an SR deplete a contestation bar, which slows production past `SlowdownThreshold` and eventually freezes the player out entirely. **Ownership never transfers** — contesting is not capturing, and the two are separate mechanics. Do not read "capture is not wired" as "nothing happens at an SR". Note its real timings: `BaseTicks: 1500` is 90 s, `MinTicks: 500` is 30 s, `BaseRecoveryTicks: 3000` is 180 s — the file's duration comments used to assert 25 tps and understate all three by 1.5×, and were corrected in-tree on 2026-08-22, but the same error is still live at ten other sites (see [`DOCS/reference/conventions.md`](DOCS/reference/conventions.md) §"A change believed made, documented as made, and inert"). Engine code still contains RA-era assumptions — verify how WW3MOD actually uses a system before trusting old logic. Full model: [`DOCS/reference/game-model.md`](DOCS/reference/game-model.md).
- **Workers NEVER push to remote.** Commit to your branch and stop; the manager merges and pushes. _(Superseded 2026-08-11: the previous blanket "the user pushes manually" no longer holds. The user now tests from a different computer and asked for finished work to be pushed as it lands: "Push everything when it is finished going forward, I will be testing from a different computer." **The manager pushes `main` after a merge is verified — build clean and NUnit green — and never before.** The worker half of the rule is unchanged and still load-bearing: it is what guarantees nothing reaches origin unreviewed and unverified.)_
- Commit finished work with descriptive messages; don't leave uncommitted changes behind. No co-author/attribution trailers.
- **No autonomous multi-test runs.** Autotests take minutes each and steal window focus. Explicit goahead in the current turn required before: `run-batch.sh`, `run-tournament.sh`/`loop-tournament.sh`, any compound command invoking `run-test.sh` more than once, or a third rerun of the same test. One `run-test.sh <test>` for the bug at hand is the normal flow. Narrating a plan is not a goahead.
- **MiniYaml: blank lines between top-level entries are significant** — adjacent entries silently merge. If a template "isn't taking effect," check blank lines first.
- **Building while the game runs:** fine on macOS/Linux (`engine/Directory.Build.targets` unlinks outputs — never disable it). On Windows the build fails fast on locked DLLs — move on or wait quietly, don't alarm the user.
- **`@stable` inherits improvements; it is never gated OFF on purpose.** Don't work on the stable bot directly — improving it is not a goal in itself. But when work aimed at `@experimental` also improves `@stable`, **let it through**: never spend extra effort building a gate whose only purpose is to withhold a fix from `@stable`. Do not ask about this per-change; it is settled policy. The one thing that still holds is the existing rule against *silent* drift — a new behavioural Info field on a trait shared by both profiles must still default to baseline so `@stable` never changes without anyone noticing (see [`DOCS/reference/architecture.md`](DOCS/reference/architecture.md) §"Adding a behavioural field to a trait shared by both bot profiles"). Deliberate, visible improvement flowing to `@stable` is fine; accidental mutation of the benchmark control is not. If `@stable` behaviour does change, say so in the commit message so the next benchmark baseline is re-taken knowingly.
- Apply confirmed rules from `C:\Users\fredr\Desktop\ClaudeRules\confirmed\`.

## Build & Run

**Prerequisite: a `6.0.4xx` .NET SDK must be installed.** `global.json` pins `6.0.428` with
`rollForward: latestFeature`, which **cannot cross a major version** — a machine carrying only 8.x/10.x
fails every project with *"A compatible .NET SDK was not found"*, and a 6.0.1xx band is rejected too.
`winget install Microsoft.DotNet.SDK.6` on Windows, else <https://dotnet.microsoft.com/download/dotnet/6.0>.
Side-by-side installation is safe; the pin governs which SDK *compiles*, not which runtime executes.
.NET 6 is EOL and this is a deliberate tradeoff for analyzer determinism between CI and `make check` —
reasoning in commit `e4453e6b`; read it before proposing a bump.

```bash
./make.ps1 all          # Windows build (targets net6, runs on .NET 8+); `make all` on Linux/macOS
./launch-game.cmd       # Windows: builds, then runs (aborts without launching if the build fails)
./launch-game.sh        # Linux/macOS: runs an ALREADY-BUILT tree; does NOT build first
make test               # YAML validation (needs .NET 6 runtime specifically). Fails on lint errors that
                        # are NOT in mods/ww3mod/lint-baseline.txt — and also when a recorded one stops
                        # occurring, because you fixed something and the floor must drop with it:
                        # LINT_BASELINE_PRUNE=true ./utility.sh --check-yaml, then commit the file.
                        # Never hand-add a line to that file to make a red run green without saying why.
dotnet test engine/OpenRA.Test/OpenRA.Test.csproj --configuration Release   # unit tests (NUnit 3)
./ww3-dev.ps1           # dev helper: build, run, test, pre-flight, log cleanup
```

## Read before you work — routing table

Pull only what your task needs. Indexes: [`DOCS/README.md`](DOCS/README.md), [`WORKSPACE/README.md`](WORKSPACE/README.md).

| If your task involves… | Read first |
|---|---|
| Editing YAML or engine C# | [`DOCS/reference/conventions.md`](DOCS/reference/conventions.md) — WDist, WAngle (counterclockwise!), YAML idioms, PITFALL comments, engine code rules |
| AI / strategic layer | [`DOCS/reference/supply-route.md`](DOCS/reference/supply-route.md) + [`DOCS/reference/game-model.md`](DOCS/reference/game-model.md) — the recurring trap |
| Belief / danger / territory fields, @experimental fog-respecting AI (Stages 0, A–F) | [`DOCS/reference/influence-stack.md`](DOCS/reference/influence-stack.md) — invariants (zero RNG, byte-identity) + consumer map |
| A specific engine system (aircraft, suppression, stances, scenarios, AI config, shadows, audio/music) | [`DOCS/reference/architecture.md`](DOCS/reference/architecture.md) — that section only |
| Behavioral bug fix or feature | [`DOCS/recipes/AUTOTEST.md`](DOCS/recipes/AUTOTEST.md) — test-driven loop, **applies by default** |
| Visual work (UI, palette, lobby, sprites, formations) | [`DOCS/recipes/SCREENSHOT.md`](DOCS/recipes/SCREENSHOT.md) — capture + multimodal eval, **applies by default** |
| Staging something for the user to see | [`DOCS/recipes/DEMO.md`](DOCS/recipes/DEMO.md) — no verdict, never `Test.Pass`/`Fail` in a demo |
| Balance / unit tuning | [`DOCS/recipes/BALANCE.md`](DOCS/recipes/BALANCE.md) — combat-sim (`tools/combat-sim/`) |
| Economy / ammo / resupply | [`DOCS/reference/economy.md`](DOCS/reference/economy.md) |
| Movement/blocking rules, locomotors, or terrain & actor edits to the **10 shipped maps** in `mods/ww3mod/maps` | [`tools/nav-guard/README.md`](tools/nav-guard/README.md) — run `make nav-guard` (static, no build). Fails if a map lost reachable ground; a blocking change can seal off a region nobody thinks to look at |
| Terrain, `Bounds` or actor edits to an **autotest scenario** under `tools/autotest/scenarios/` | **`make nav-guard` does NOT cover these and cannot fail for them.** Its baseline is `mods/ww3mod/maps` only, so its green is byte-identical before and after your edit — that misread cost two workers on 2026-09-01. Inspect one with `nav_guard.py report --scenarios --map <name>`, and see [`DOCS/recipes/AUTOTEST.md`](DOCS/recipes/AUTOTEST.md) §"Verify before you ask for a slot" for the connectivity and binding checks that DO apply |
| Current status, what's in flight | `WORKSPACE/RELEASE_V1.md` + `WORKSPACE/HOTBOARD.md` + `git log --oneline -20` |
| What comes next, roadmap order | `WORKSPACE/PIPELINE.md` — living queue of **stubs only**, top item = next to start. Read it whole; it is short by design. The full dossier for the item you pick is `WORKSPACE/pipeline/items/<NN>-<slug>.md`, linked from its stub — **do not read the others.** Finished work, with its vocabulary rulings and traps intact, is under `WORKSPACE/pipeline/archive/` (map: [`WORKSPACE/pipeline/README.md`](WORKSPACE/pipeline/README.md)) |
| Picking up ANY queue item | **Spend one `git log -S <symbol>` or one grep on the item's central premise before you start.** In the week to 2026-08-19, five queue items described already-merged work and two cost a worker dispatched at nothing. It costs seconds; it is the single most expensive recurring mistake in this project |
| A user-typed trigger word (`PLAN`, `TRIAGE`, `FINALIZE`, …) | [`DOCS/recipes/README.md`](DOCS/recipes/README.md) — these are docs to read, NOT harness Skills; never call the `Skill` tool for them |

Modes: RELEASE (default, scope-locked v1) vs EXPERIMENTAL — [`DOCS/modes/`](DOCS/modes/README.md).

## Knowledge bank

`DOCS/reference/` is curated — its claims are trusted, so protect that: **fix verifiably-wrong statements on sight, but don't add new knowledge to it directly.** New insights go to `WORKSPACE/DISCOVERIES.md` (dated, with code refs); a curation pass promotes them. Rules: [`DOCS/reference/README.md`](DOCS/reference/README.md). Incidental bugs → `WORKSPACE/bugs/discovered.md`.
