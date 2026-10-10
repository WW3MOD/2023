# WW3MOD - Agent Instructions

WW3MOD is a **total conversion** of OpenRA Red Alert (`release-20230225`, engine in-repo, engine extensively modified) into a modern World War 3 RTS. **Two playable factions and only two: America and Russia.** `mods/ww3mod/rules/world.yaml` is the only place a `Faction@` block is defined: `america`, `russia`, and the non-playable `Random` (`RandomFactionMembers: america, russia`). "BRICS" is either a stale synonym for Russia or a live code identifier (`player.brics`, `sidebar-brics`, `FactionSuffix-russia: brics`) that must NOT be renamed — never read it as a third faction. China, Belarus, Ukraine, NATO and Europe are not in the game; a faction string in a rules file that no `Faction@` block defines is not evidence of a faction. Solution `WW3MOD.sln`; engine compiles to `engine/bin/`; mod content in `mods/ww3mod/`.

## Hard rules

- **Do not assume any Red Alert mechanic still applies — the gameplay model is rebuilt.** No factories, no tech tree: units are called in as reinforcements from off-map reserves via the **Supply Route** — a fixed, non-buildable beachhead (one per player). Costs are budget allocation, not manufacturing. Engine code still contains RA-era assumptions — verify how WW3MOD actually uses a system before trusting old logic. Full model: [`DOCS/reference/game-model.md`](DOCS/reference/game-model.md).
- **The Supply Route is UNTARGETABLE, not indestructible, and the difference decides whether a bypass reaches it.** In the `SUPPLYROUTE:` actor (`mods/ww3mod/rules/ingame/structures.yaml`) it has `Health: HP: 75000` and no invulnerability; what protects it is `Targetable: TargetTypes: NoAutoTarget`, a target type no weapon lists, so `Warhead.IsValidAgainst` rejects every warhead before damage is computed. Its `Armor: Type: Indestructable` is inert — no `Versus:` table names that type. Anything that skips the target-type test reaches it: `VaporizeWarhead` does so on purpose, which is why `SUPPLYROUTE` carries an explicit `-Vaporizable:`. **Do not delete that line; it is what stops one nuke being an instant win.**
- **Supply Route capture is NOT wired; contestation is, and it is the win condition.** The design intends extra Supply Routes via capturing neutral ones, but `SUPPLYROUTE` has no `Capturable` and no `CaptureManager`. Treat "one per player" as shipped reality; do not write code, tests or player-facing copy that assumes capture works. What ships — and what players call "capturing the SR" — is `SupplyRouteContestation` (`engine/OpenRA.Mods.Common/Traits/SupplyRouteContestation.cs`): enemy units near an SR deplete a control bar, which slows production below `SlowdownThreshold`, then fills a **defeat** bar. Per the trait's `[Desc]`, at 100% the player **becomes passive if a teammate still holds an active Supply Route (they can be relieved), and is defeated outright if nobody is left to relieve them.** **Ownership never transfers** — contesting is not capturing. Real timings at the default game speed (`mods/ww3mod/mod.yaml`: `DefaultSpeed: default` → `Timestep: 60` = **16.67 ticks/s**): `BaseTicks: 1500` is 90 s, `MinTicks: 500` is 30 s, `BaseRecoveryTicks: 3000` is 180 s. Never convert ticks at RA's 25 tps; duration comments that still do are catalogued in [`DOCS/reference/conventions.md`](DOCS/reference/conventions.md) §"A change believed made, documented as made, and inert" — grep `25 tps` before trusting any duration comment.
- **Workers NEVER push to remote.** Commit to your branch and stop; the manager merges and pushes `main` once the merge gate in [§Who runs what](#who-runs-what) is green, and never before. That is what guarantees nothing reaches origin unreviewed and unverified.
- Commit finished work with descriptive messages; don't leave uncommitted changes behind. No co-author/attribution trailers.
- **No autonomous multi-test runs.** Autotests take minutes each. Who may launch what, and what needs an explicit go-ahead, is in [§Who runs what](#who-runs-what). Narrating a plan is not a go-ahead.
- **MiniYaml: "the override isn't taking effect" has three causes, not one.** Blank lines between top-level entries are significant (adjacent entries silently merge); top-level keys merge **case-sensitively**, so a rules override written `t03:` against a defining `T03:` overrides nothing — actor names only *feel* case-insensitive because the lowercasing runs after the merge; and `Inherits@`/`-Key:` apply **where they appear**, so ordering is load-bearing. The case cause usually stops the mod loading at all, with a message that never mentions case. Symptoms, citations and the detector: [`DOCS/reference/conventions.md`](DOCS/reference/conventions.md) §`"The override isn't taking effect" — blank lines are only the first of three causes`.
- **Building while the game runs:** fine on macOS/Linux (`engine/Directory.Build.targets` unlinks outputs before copying — never disable it). On Windows the build fails fast on locked DLLs — move on or wait quietly, don't alarm the user.
- **`@stable` inherits improvements; it is never gated OFF on purpose.** Don't work on the stable bot directly, but when work aimed at `@experimental` also improves `@stable`, **let it through** — never build a gate whose only purpose is to withhold a fix. Settled policy; do not ask per change. What still holds is the rule against *silent* drift: a new behavioural Info field on a trait shared by both profiles must default to baseline (see [`DOCS/reference/architecture.md`](DOCS/reference/architecture.md) §"Adding a behavioural field to a trait shared by both bot profiles"). If `@stable` behaviour does change, say so in the commit message so the next benchmark baseline is re-taken knowingly.
- **Windows only:** apply confirmed rules from `C:\Users\fredr\Desktop\ClaudeRules\confirmed\`. No copy of that directory exists on the macOS machine.

## Who runs what

Concurrent game launches crash the user's machine, and concurrent YAML lints serialize into a queue. So launches and lints are run by one party at a time; builds and static gates are not contended and stay with whoever wrote the change. Bare target names below are `make <target>` on macOS/Linux and `.\make.ps1 <target>` on Windows.

**1. A worker dispatched by a manager never starts the game and never runs the YAML lint.** None of: `tools/autotest/run-test.sh`, `run-batch.sh`, `run-tournament.sh`, `loop-tournament.sh`, `run-demo.sh`, `run-smoke.sh` / `smoke`, `launch-game.*`, the `tools/autotest/screenshot*.sh` / `start-screenshot-mode.sh` scripts, `utility.sh --check-yaml`, or `test`. The worker writes the scenario plus an explicit statement of what result counts as the answer, and hands it up; the manager runs launches serially. Builds and static gates stay with the worker: `all`, `check`, `dotnet test`, `lua-gate`, `nav-guard`, `worldactor-gate`, `smudge-gate`, `mount-gate`. A worker that touched YAML lists which files, and what it expects lint to say if it got them wrong, so the manager can check the gate run against that claim.

**2. An agent working directly with the user (no manager)** may run one `./tools/autotest/run-test.sh --hidden <test>` for the bug at hand. Explicit go-ahead in the current turn is required before `run-batch.sh`, `run-tournament.sh` / `loop-tournament.sh`, any command invoking `run-test.sh` more than once, or a third rerun of the same test. Every launcher lives in `tools/autotest/`, none at the repo root; run them from the root by that path. `--hidden` never maps a window, but it suspends engine-side rendering and screenshots are taken in the render tick, so **a `--hidden` run writes no PNGs** (detail: [`DOCS/reference/conventions.md`](DOCS/reference/conventions.md) §"`--hidden` autotest runs write NO screenshots, while `result.json` still lists them"). Use the default profile for any run that must capture. `engine/bin` is not in git, so build a fresh worktree (`make all` / `.\make.ps1 all`) before its first launch, even when the diff has no C#.

**3. Exit 126 or 127 from a launcher, or a fast non-zero exit with zero-byte output, is a launch failure — nothing ran, and it is not a test result.** 127 is a launcher run by the wrong path; 126 is a script tracked without the executable bit (`engine/utility.sh` and `tools/autotest/selftest-launch-failure.sh` are mode `100644`). Redirected, either leaves a zero-byte log that looks like a tool still working. Read the exit code, never pipe a verdict through `tail` (you get `tail`'s exit code), and trust the run directory's `result.json`.

**4. Merge gate (manager, at merge, before pushing `main`):** `all` → `check` → `dotnet test` → `test`, plus `smoke` for any merge that changes C# under `engine/`. A change touching only `mods/` or `tools/` still runs `dotnet test`, because NUnit fixtures read shipped YAML; only the builds may be skipped when the C# is provably identical to an already-gated ref. `all` and `dotnet test` are Release, and Release strips every analyzer, so only `check` sees analyzer errors — a green Release build is not evidence about the Debug gate. Workers never push; the manager pushes `main` after the gate is green.

## Build & Run

**Prerequisite: a `6.0.4xx` .NET SDK.** `global.json` pins `6.0.428` with `rollForward: latestFeature`,
which cannot cross a major version (or drop to a 6.0.1xx band): a machine with only 8.x/10.x fails every
project with *"A compatible .NET SDK was not found"*. `winget install Microsoft.DotNet.SDK.6` on Windows,
else <https://dotnet.microsoft.com/download/dotnet/6.0>; side-by-side is safe. The pin governs which SDK
*compiles*; the engine targets `net6.0` with `RollForward: Major`, so it runs on a newer runtime. .NET 6
is EOL — a deliberate tradeoff for analyzer determinism; read commit `e4453e6b` before proposing a bump.

Every target below is `make <target>` on macOS/Linux and `.\make.ps1 <target>` on Windows.

```bash
make all                # Release build (targets net6). engine/Directory.Build.props strips every
                        # analyzer in Release, so a green `all` says NOTHING about analyzer errors --
                        # and neither does `dotnet test`, which also runs Release.
make check              # Debug build with analyzers ON (-warnaserror), plus the explicit-interface and
                        # conditional-trait-override checks; runs worldactor-gate first. THE ONLY
                        # COMMAND HERE THAT SEES AN ANALYZER (e.g. RCS-class) ERROR. Run it before
                        # committing C#.
make test               # Builds Release (`all`) first, then runs nav-guard, lua-gate, smudge-gate and
                        # mount-gate, then `utility.sh --check-yaml` over every map folder in mod.yaml --
                        # the shipped maps AND every autotest scenario. Fails on lint errors not in
                        # mods/ww3mod/lint-baseline.txt, and also when a recorded one stops occurring:
                        # the floor must drop with your fix (LINT_BASELINE_PRUNE=true ./utility.sh
                        # --check-yaml, then commit the file). Never hand-add a line to that file to
                        # make a red run green without saying why.
dotnet test engine/OpenRA.Test/OpenRA.Test.csproj --configuration Release   # unit tests (NUnit 3)
make smoke              # WORLD-CONSTRUCTION GATE: the only gate that starts the game -- every other
                        # one reads files, so a trait throwing in INotifyCreated.Created passes them
                        # all while every match fails to start. Runs the canary (test-world-smoke),
                        # then every map in mods/ww3mod/maps/, --hidden. Does NOT build; refuses to
                        # start without engine/bin/OpenRA.dll. Read the EXIT CODE:
                        #   0  every map reached a `pass` verdict.
                        #   2  a map started and did not pass -- crash, hang, or fail verdict.
                        #      This is the bug class.
                        #   3  LAUNCH FAILURE -- nothing ran, NOTHING WAS PROVEN. Not a result.
                        # A non-PASS in under 8 s is classed as exit 3: a game cannot start, load the
                        # mod and load a map that fast. Last line is machine-readable:
                        # `SMOKE_VERDICT outcome=... exit=... passed=... failed=...`.
                        # Detail: tools/autotest/run-smoke.sh header.
make worldactor-gate    # ~5 s, no build. The STATIC half of the same bug class: a trait marked
                        # [TraitLocation(SystemActors.World)] must not read .WorldActor in its
                        # constructor, in INotifyCreated.Created, or in its Info's Create() -- the
                        # World constructor assigns WorldActor only after CreateActor returns, so it
                        # is null while those run; the correct receiver there is `self`. The same line
                        # is CORRECT on a Player or per-actor trait, so the gate keys on the
                        # TraitLocation. tools/worldactor-gate/README.md
make lua-gate           # ~2 s, no build: scenario wiring a lint cannot see. tools/lua-gate/README.md
make nav-guard          # static map connectivity, shipped maps only. tools/nav-guard/README.md
make smudge-gate        # static: nuclear scars land where a player will look. tools/smudge-gate/README.md
make mount-gate         # static: mod.yaml FileSystem mounts. tools/mount-gate/README.md
./launch-game.cmd       # Windows: builds, then runs (aborts without launching if the build fails)
./launch-game.sh        # Linux/macOS: runs an ALREADY-BUILT tree; does NOT build first
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
| Movement/blocking rules, locomotors, or terrain & actor edits to the shipped maps in `mods/ww3mod/maps` | [`tools/nav-guard/README.md`](tools/nav-guard/README.md) — run `nav-guard` (static, no build). Fails if a map lost reachable ground; a blocking change can seal off a region nobody thinks to look at |
| ANY edit to an **autotest scenario** under `tools/autotest/scenarios/` — terrain, `Bounds`, actors or YAML | `test` lints **every** scenario as well as the shipped maps (scenarios are a `MapFolders` entry in `mod.yaml`). Its `Testing map:` count should equal the directory counts of `mods/ww3mod/maps/` plus `tools/autotest/scenarios/` — recount with `ls -d <dir>/*/ \| wc -l`; never quote a number. `nav-guard` does **not** cover scenarios (`nav_guard.py report --scenarios --map <name>` inspects one); assume any other gate has the same hole until you have read its file list. Single-scenario lint, **from the repo root**: `./utility.sh --check-yaml ../tools/autotest/scenarios/<name>` (Windows: `.\utility.cmd`, same arguments) — the `../` is required because the path resolves from `engine/` — and require `Testing map:` in the output. Never run `engine/utility.sh` (exit 126). Lint proves a scenario well-formed, not that the engine *reads* it: a `rules.yaml` that `map.yaml` never declares is silently ignored (`Map.cs` declares `Rules` with `required: false`), and `Lint/CheckLuaScript.cs` returns early on exactly that state, so the scenario loads and does nothing. `lua-gate` (also part of `test`) checks that and the other inert-but-valid shapes: [`tools/lua-gate/README.md`](tools/lua-gate/README.md) §"The second failure class: a scenario that is SILENTLY INERT". Mechanism: [`DOCS/reference/conventions.md`](DOCS/reference/conventions.md) §"Scenarios and the map-shaped gates". Connectivity and binding checks that do apply: [`DOCS/recipes/AUTOTEST.md`](DOCS/recipes/AUTOTEST.md) §"Verify before you ask for a slot" |
| Current status, what's in flight | `WORKSPACE/RELEASE_V1.md` + `WORKSPACE/HOTBOARD.md` + `git log --oneline -20` |
| What comes next, roadmap order | `WORKSPACE/PIPELINE.md` — living queue of **stubs only**, top item = next to start. Read it whole; it is short by design. The full dossier for the item you pick is `WORKSPACE/pipeline/items/<NN>-<slug>.md`, linked from its stub — **do not read the others.** Finished work, with its vocabulary rulings and traps intact, is under `WORKSPACE/pipeline/archive/` (map: [`WORKSPACE/pipeline/README.md`](WORKSPACE/pipeline/README.md)) |
| Picking up ANY queue item | **Spend one `git log -S <symbol>` or one grep on the item's central premise before you start** — but grep for the *behaviour*, not the defect: the defect being real does not make the item unfinished, and neither answers whether it is done. **A merged branch is not a finished item** (one shipped switched off; another's named branch carried only test hygiene while the fix rode a different one), and **read the file, not the commit message** — two documents agreeing on a number is not evidence. Detail: [`WORKSPACE/pipeline/README.md`](WORKSPACE/pipeline/README.md) §"Working rules" |
| A user-typed trigger word (`PLAN`, `TRIAGE`, `FINALIZE`, …) | [`DOCS/recipes/README.md`](DOCS/recipes/README.md) — these are docs to read, NOT harness Skills; never call the `Skill` tool for them |

Modes: RELEASE (default, scope-locked v1) vs EXPERIMENTAL — [`DOCS/modes/`](DOCS/modes/README.md).

## Knowledge bank

`DOCS/reference/` is curated — its claims are trusted, so protect that: **fix verifiably-wrong statements on sight, but don't add new knowledge to it directly.** New insights go to `WORKSPACE/DISCOVERIES.md` (dated, with code refs); a curation pass promotes them. Rules: [`DOCS/reference/README.md`](DOCS/reference/README.md). Incidental bugs → `WORKSPACE/bugs/discovered.md`.
