# Agent-instruction accuracy audit

**Read at `main @ 2f8b5f6d`** (2026-10-07). The brief said `c276679c`; main had moved on by the time this ran. Read-only: nothing was built, launched or linted. Every verdict is from reading the tree.

**Scope:** CLAUDE.md, `.maestro/MAESTRO.md`, `DOCS/recipes/*.md` (all 12), `DOCS/reference/README.md`, `DOCS/reference/conventions.md`, and `tools/autotest/README.md` plus the usage text of the launch scripts. `game-model.md` and `supply-route.md` were skipped because another scout owns them. CLAUDE.md was audited directly; the other files went to five read-only sub-auditors, whose tables are reproduced below.

**Verdicts:** TRUE · STALE (what is true now is given) · CONTRADICTED (both files cited) · UNVERIFIABLE (the run that would settle it is named). Only non-TRUE rows are tabled, apart from CLAUDE.md, which is tabled in full. TRUE checks are summarised as counts.

## Totals

| File | STALE | CONTRADICTED | UNVERIFIABLE | TRUE checked |
|---|---|---|---|---|
| CLAUDE.md | 7 | 3 | 2 | 20 |
| .maestro/MAESTRO.md + DOCS/reference/README.md + tools/autotest usage | 24 | 11 | 5 | ~56 |
| DOCS/reference/conventions.md 1-939 | 47 | 2 | 6 | ~80 |
| DOCS/reference/conventions.md 940-1859 | 34 | 3 | 3 | ~80 |
| DOCS/recipes/AUTOTEST.md | 31 | 4 | 4 | ~70 |
| DOCS/recipes/ (other 11) | 27 | 11 | 6 | ~130 |
| **Total** | **170** | **34** | **26** | **~436** |

Counts come from the table rows. Several rows bundle a group of drifted line cites in one file, so 170 is a floor on stale *citations*, not a ceiling.

### Post-rewrite state (main @ e34436b6, 2026-10-10)

Every file in scope was rewritten in place: CLAUDE.md (`4a4feb7b`), `.maestro/MAESTRO.md` (`d2402b15`), `DOCS/recipes/*.md` + `tools/autotest/README.md` + launcher header text (`d37e3efb`), `DOCS/reference/conventions.md` + `DOCS/reference/README.md` (`e34436b6`). Each rewriting worker re-verified every claim it kept or wrote by reading the code; the manager spot-checked 41 claims across the four merges (11 / 5 / 10 / 10 plus heading links) and found none wrong. **This is not a re-audit** — the residual figures below are what the rewrite reported, not a fresh count.

| File group | STALE | CONTRADICTED | UNVERIFIABLE (now labelled in-text, run named) |
|---|---|---|---|
| CLAUDE.md | 0 | 0 | 2 (`--hidden` no-PNG; Linux `make test` covers scenarios — both read from code, no run) |
| .maestro/MAESTRO.md | 0 | 0 | 1 (unbuilt worktree → `NO-RESULT` exit 3); item-64/R7 specifics dropped |
| DOCS/reference/conventions.md + README.md | 0 known; ~550 cite edits spot-checked, not read one by one | 0 | 8 (listed in the `52479eb7` commit message) |
| DOCS/recipes/ + tools/autotest usage | 0 | 0 | 5 (arty-force-attack red status, Linux scenario lint, top-resolution token costs, DEMO gotcha 7, BALANCE derivation figures) |
| **Total** | **0 known** | **0** | **16** |

Contradictions were resolved by one policy, CLAUDE.md §"Who runs what" (worker launch/lint ban, direct-agent single `--hidden` run, launch-failure exit codes, merge gate `all → check → dotnet test → test` + `smoke` for `engine/` C#). Out of scope and still stale — they are code comments, not instruction files: `SupplyRouteContestation.cs` (`mod.yaml:381` timestep cite), `AutotestTickRateTest.cs` message citing `conventions.md:1107`, `weapons-superweapons.yaml` decoration cites, `GarrisonManager.SwapPortOccupants` PITFALL cites, `world.yaml` "8 scenarios force the powers-sandbox flag" (18), the 25-tps comments catalogued in conventions.md, `lua-gate/README.md` `Map.cs` cite. `shadow-los-plan.md` belongs in `WORKSPACE/` under the bank's rules.

## What matters most (read this if nothing else)

1. **Line-number drift is the dominant failure.** About 120 of the 170 STALE rows are a `file:line` that no longer points at the claimed code, while the claim itself is still true. conventions.md is the worst. Its rule 2 says "the only defensible citation is `mod.yaml:358` and `:382`", but the tick-rate lines are now `:407`/`:431`. Every reader who follows that rule cites the wrong lines. **Proposal:** cite `file` + symbol (e.g. `SupplyRouteContestation.cs` `BaseTicks`), and keep line numbers only where a grep cannot find the spot.
2. **The worker-launch rule is contradicted three ways.** MAESTRO.md says workers never launch anything (`run-test.sh`, screenshots, demos, batches). CLAUDE.md calls one `run-test.sh` "the normal flow". AUTOTEST/SCREENSHOT/DEMO/BALANCE all tell the agent to launch on its own; AUTOTEST step 6 even says `run-batch.sh --all`, which CLAUDE.md itself forbids without go-ahead. Workers load CLAUDE.md and the recipes but not MAESTRO.md, so the rule that is actually enforced is the one they never see. **A single statement of who may launch what, placed in CLAUDE.md, would resolve all of these rows.**
3. **The merge/push gate is defined differently in two places, and smoke is in neither.** CLAUDE.md says "build clean and NUnit green". MAESTRO says `all → check → dotnet test → test`. CLAUDE.md also says smoke is the only gate that proves a World constructs (the 2026-09-10 DefconWall outage passed every other gate).
4. **Substantive falsehoods (not just drift):**
   - CLAUDE.md cites `player.yaml:645` brics/china, a line deleted 2026-09-07.
   - "~264 C# files" is a March 2026 snapshot; the real figure is up to 1293 modified + 872 added.
   - "25 tps live at ten other sites" is now about 6–7, one of them new.
   - conventions.md describes `^5.56mm`/`^7.62mm` as targeting `Helicopter`, and inert-instances 4 and 5 as live; all were fixed by the AirLight change.
   - conventions.md says the `RUNTIME=mono` lane is reachable; `Makefile:82-87` now errors on it.
   - The heal pulse is now 10, not 5 (see bugs below).
   - AUTOTEST.md has two deadline rows still on 25-tps arithmetic.
   - The `LAUNCH-FAIL` outcome is missing from AUTOTEST.md's outcome list.
   - `make test` runs two gates (`smudge-gate`, `mount-gate`) that no instruction mentions.
   - The `--hidden` "prefer" in CLAUDE.md has no caveat, yet two other files say `--hidden` writes no screenshots.
5. **Live tool bugs found along the way.** These belong in `WORKSPACE/bugs/discovered.md`; they were not filed there by this read-only scout.
   - **`run-batch.sh --all` silently drops scenarios whose verdict comes from a shared `*-lib.lua`.** Its filter only greps `scenarios/<name>/*.lua`. Five of the 23 `expected-status` declarations are never graded, and 9 of the 10 `test-balance-*` scenarios are excluded.
   - **`run-demo.sh`** passes no `--timeout`. The 300 s watchdog therefore kills a demo window at 5 minutes, and the script returns 1, not the documented 0.
   - **`run-tournament.sh -v`** is a dead flag (`RUN_TEST_FLAGS` is never read). The script also exits 0 even when no match produced a verdict.
   - **`loop-tournament.sh`'s** awk parser folds inline `# comments` and literal `""` into the values it reads.
   - **`selftest-launch-failure.sh`** is mode 100644 (exit 126 if run as `./`), the same trap as `engine/utility.sh`.
   - **Heal `SwitchMargin: 10` now equals the heal pulse** (`DamagePercent: -10`). The PITFALL at `infantry.yaml:2335` still says 5, and the margin was meant to exceed the pulse.
   - **`SupportPower.LobbyChargeIntervalId`** has no consumer anywhere in the engine (dead field), and its Desc still says "Parsed at 25 ticks/second".
   - **`SupplyRouteContestation.cs:42`** cites `mod.yaml:381` for the timestep; the timestep is at `:431`.
6. **Dead machinery still referenced:**
   - CONTEXT and FINALIZE point to `WORKSPACE/archive/sessions/active_*.md`, which no longer exists.
   - FINALIZE's `[x]`/"Recently completed" convention contradicts `RELEASE_V1.md:27`.
   - REVIEW cites CLAUDE.md sections ("Common Pitfalls", "PITFALL Comments") that no longer exist, and recommends lowercase actor names, which CLAUDE.md's case-sensitivity rule says breaks overrides.
   - TRIAGE routes work to `RELEASE_V1.md` rather than `PIPELINE.md`.
   - TELEMETRY says the channel is unbuilt, but `UnitLifecycleLogger` / `--lifecycle` exist.

## Correction-on-correction prose: the worst offenders

Rewrite proposals sit under each file's section below; none were applied. In rough order of cost to a reader:

- **CLAUDE.md:92**, the scenario row ("CORRECTED 2026-09-04", "an earlier version of this correction", "This figure has now gone stale twice"). See proposal C4.
- **AUTOTEST.md:508-521**, the tick-base cluster ("this paragraph used to say there were three").
- **conventions.md:45/56** and **:1166-1170**, the three-then-one tick bases and a stale census.
- **MAESTRO.md:33 + :52-70**, a struck-out rule followed by two AMENDED blocks.
- **CLAUDE.md:10**, the "⚠️ PATHS CORRECTED 2026-09-11" block.
- **SCREENSHOT.md:356-369**, "PARTLY BUILT, not 'sketched'". It contradicts itself about the `type` verb.

---

### CLAUDE.md

Audited directly (not delegated). The tick rate under every conversion here is `DefaultSpeed: default` (`mods/ww3mod/mod.yaml:407`) → `Timestep: 60` (`:431`) = **16.67 ticks/s**.

| # | Line | Claim (short) | Verdict | Evidence / what's true now |
|---|---|---|---|---|
| 1 | 3 | `world.yaml:257-275` "ships exactly two `Faction@` blocks" | STALE (wording) | The line range is right. There are **three** blocks: `Faction@randomside` (`:257`), `Faction@0` america (`:263`) and `Faction@1` russia (`:272`), with a commented Ukraine block at `:267`. Two are *playable*. `player.yaml:154` repeats "exactly two" while `player.yaml:1135` says "exactly three", so the in-tree comments disagree with each other too. |
| 2 | 3 | `player.yaml:645`'s `Factions: brics, russia, china, belarus` lists faction strings no `Faction@` block defines | **STALE** | That line was deleted on 2026-09-07. `player.yaml:1126-1140` records the removal and the check behind it. `player.yaml:645` is now a nuke `SpawnAltitude`/`Camera*` block. The sentence now sends readers to a line that does not say what is quoted. The warning it was meant to give ("China is not in the game") still stands. |
| 3 | 3 | `player.brics`, `sidebar-brics`, `FactionSuffix-russia: brics` are live identifiers | TRUE | `metrics.yaml:7`; `chrome.yaml`; `player.yaml:1164-1166` |
| 4 | 3 | "~264 C# files modified" | **STALE** | This comes from `DOCS/reference/project-assessment.md:13`, dated March 2026. Against the engine import commit `7362fbc6` ("Starting point"), `git diff --diff-filter=M` lists **1293 modified + 872 added** `.cs` files under `engine/`. That is an upper bound, because formatting-only sweeps count. Either re-derive the number or drop it. |
| 5 | 7 | SR `Health: HP: 75000`; `Targetable: TargetTypes: NoAutoTarget` at `structures.yaml:389-390`; inert `Armor: Indestructable` at `:416-417`; `SupplyRouteContestation` at `:396`; actor opens at `:295` | TRUE | Exact, with HP at `:388`. No weapon lists `NoAutoTarget`. No `Versus:` table names `Indestructable`. |
| 6 | 7 | `SUPPLYROUTE` carries `-Vaporizable:`; no `Capturable`/`CaptureManager` | TRUE | `-Vaporizable:` is at `structures.yaml:318`. `CaptureManager` and `Capturable` are commented out inside the actor. |
| 7 | 7 | Contestation `[Desc]` (passive vs defeated outright) at `SupplyRouteContestation.cs:24-26` | TRUE | Exact |
| 8 | 7 | `BaseTicks 1500` = 90 s, `MinTicks 500` = 30 s, `BaseRecoveryTicks 3000` = 180 s | TRUE | Trait defaults are at `:45/:48/:69`, and YAML repeats them at `structures.yaml:400-406`. Side note: the trait's own comment at `SupplyRouteContestation.cs:42` cites `mod.yaml:381` for the timestep, which is now `:431`. |
| 9 | 7 | The 25-tps error "is still live at ten other sites" | **STALE** | Two of the census sites have been fixed (`EvacDriveOffMath`, `TournamentConfig`). Live-wrong today: `SmartMove.cs:25`, `SupportPower.cs:24`, `scripts/scenario.lua:31`, `test-missile-hellfire-probe.lua:168`, and two stance scenarios that were "fine" until the harness moved to 16.67 tps on 2026-09-21 (`test-stance-anchor-move.lua:46`, `test-stance-redirect-midadjust.lua:50`). One new site is not in the census: `tools/autotest/parse-floor-denominator.py:186`. That makes about 6–7, not ten. See conventions.md (lines 940+) row 7. |
| 10 | 8 | Workers never push; manager pushes after build + NUnit green | CONTRADICTED (gate definition) | `.maestro/MAESTRO.md:64` gives the merge gate as `all → check → dotnet test → test`, while CLAUDE.md says "build clean and NUnit green". **Neither includes `smoke`**, even though CLAUDE.md's own Build block calls smoke "the only command that answers 'does a World still construct?'". `make.ps1:301-302` warns that skipping it "leaves the merge gate believing a World was constructed". |
| 11 | 10 | "One `./tools/autotest/run-test.sh <test>` for the bug at hand is the normal flow" | CONTRADICTED | `.maestro/MAESTRO.md:45` says no worker runs `run-test.sh`. MAESTRO admits at `:37` that it overrides CLAUDE.md and the recipes, but workers only load CLAUDE.md. |
| 12 | 10 | All launchers live in `tools/autotest/`; `--hidden` is the unattended profile | TRUE | `run-test.sh:17-22` |
| 13 | 10 | "Prefer `--hidden`" with no exception | CONTRADICTED | `conventions.md:1004-1008` and `AUTOTEST.md:984-986` say `--hidden` writes **no PNG** while `result.json` still lists one. A capture run needs another profile. Not verified by a run (needs one `--hidden` capture plus `ls` of the run dir). |
| 14 | 11 | The three MiniYaml override causes | TRUE | Anchor resolves as a prefix of `conventions.md:583` |
| 15 | 18 | `global.json` 6.0.428 + `latestFeature`; commit `e4453e6b` | TRUE | Exact |
| 16 | 27-30 | `all` is Release, and `Directory.Build.props` strips analyzers in Release | TRUE | `engine/Directory.Build.props:50-53` (the file is under `engine/`, not at the repo root) |
| 17 | 31-37 | `check` is the Debug + analyzer build plus interface checks | TRUE | `Makefile:226-250`, `make.ps1` `Check-Command` (WorldActorGate first) |
| 18 | 33-36 | "3184 passing", "Errors: 21, unchanged" (2026-09-11) | UNVERIFIABLE | These are dated run outputs. The tree has 3110 `[Test]` plus `[TestCase]` rows. `lint-baseline.txt` has 375 lines. Checking the figures needs one `dotnet test` and one `make test`. |
| 19 | 38-39 | `launch-game.cmd` builds and aborts on failure; `launch-game.sh` does not build | TRUE | `launch-game.cmd:1-5`; `launch-game.sh:40-43` (guard only) |
| 20 | 40-45 | `make test` = "YAML validation" with the lint-baseline floor | **STALE (incomplete)** | `Makefile:306` reads `test: all nav-guard lua-gate smudge-gate mount-gate`, and `make.ps1` `Test-Command` runs `LuaGate`, `SmudgeGate` and `MountGate` before `--check-yaml`. So `make test` builds Release first and runs four gates. **`smudge-gate` and `mount-gate` (`tools/smudge-gate/`, `tools/mount-gate/`) are never mentioned in CLAUDE.md.** Note also that `make check` on Linux depends on `worldactor-gate` (`Makefile:226`). |
| 21 | 46-64 | smoke: 0/2/3, canary + ten shipped maps, 8 s plausibility floor, `SMOKE_VERDICT` last line, refuses without `engine/bin/OpenRA.dll` | TRUE | `run-smoke.sh:69,93,207,220-249`. Nuance: exit 2 also covers a map that reached a *fail* verdict, not only "never reached a verdict". |
| 22 | 65-72 | worldactor-gate; `World.cs:252` assigns `WorldActor` after `CreateActor` | TRUE | `World.cs:252` is exact |
| 23 | 78-96 | Routing table: 24 paths | TRUE | Every path exists. These anchors resolve: architecture.md §"Adding a behavioural field…" (`:1324`), AUTOTEST.md §"Verify before you ask for a slot" (`:323`), lua-gate README §"The second failure class" (`:218`), pipeline README §"Working rules" (`:25`). |
| 24 | 92 | Scenario row anchor §"Scenarios are NOT maps to the tooling" | **STALE (dead anchor)** | The heading is now "Scenarios are NOT maps to SOME tooling — but the Windows merge gate DOES lint every one of them" (`conventions.md:871`). |
| 25 | 92 | "At `e82fe534` there are 320 scenario directories (256/32/31/1)… should report 330" | **STALE** | At `2f8b5f6d` there are **368 = 294 test- + 42 demo- + 31 tournament- + 1 wip-**, so the prediction is 378. The row itself says "recount", which only shows why the number does not belong in it. |
| 26 | 92 | `make nav-guard` is scenario-blind | TRUE | `tools/nav-guard/nav_guard.py:37-41` (`--scenarios` is opt-in) |
| 27 | 92 | `engine/utility.sh` is mode 100644, so running it gives exit 126 and a zero-byte log; the root `utility.sh` cds into `engine/` | TRUE | `git ls-files -s`: `engine/utility.sh` is 100644 and `utility.sh` is 100755. Same trap elsewhere: `tools/autotest/selftest-launch-failure.sh` is also 100644, and `run-test.sh:155` tells readers to "Run it". |
| 28 | 92 | `Map.cs:102-107` silently ignores an undeclared `rules.yaml`; `CheckLuaScript.cs:21-23` returns early | TRUE (incomplete cite) | `Map.cs:100-107` is the generic "absent, non-required field → return" path. The field that makes `Rules` optional is declared at `Map.cs:178` (`new("Rules", …, required: false)`), and the cite should include it. `CheckLuaScript.cs:21-22` is exact. |
| 29 | 27-73 | `make.ps1` targets `all/check/smoke/worldactor-gate/lua-gate/nav-guard/test` | TRUE | `make.ps1:713-734`, `Makefile` same names |
| 30 | 14 | "Apply confirmed rules from `C:\Users\fredr\Desktop\ClaudeRules\confirmed\`" | UNVERIFIABLE on this host | This is a Windows-only path. `~/Desktop/ClaudeRules/confirmed` does not exist on this macOS machine, so a mac worker cannot follow the rule. Say where the rules live per platform, or that they are Windows-only. |
| 31 | 3 | Two playable factions; `RandomFactionMembers: america, russia` | TRUE | `world.yaml:260` |

TRUE: 20 · STALE: 7 · CONTRADICTED: 3 · UNVERIFIABLE: 2 (rows 18, 30; row 13's PNG claim also needs a run).

### Rewrite proposals — CLAUDE.md (not applied; CLAUDE.md is not edited by this scout)

**C1. Line 3, the factions sentence.** Drop the dead `player.yaml:645` cite and the incident narrative:
> **Two playable factions and only two: America and Russia.** `world.yaml:257-275` is the only place a `Faction@` is defined: `america`, `russia`, and the non-playable `Random` (`RandomFactionMembers: america, russia`). "BRICS" is a live code identifier for Russia (`player.brics`, `sidebar-brics`, `FactionSuffix-russia: brics`); never rename it, and never read it as a third faction. China, Belarus, Ukraine, NATO and Europe are not in the game.

**C2. Line 7, the timings sentence.** Replace "the file's duration comments used to assert 25 tps … still live at ten other sites" with:
> Real timings at the default 60 ms timestep (`mod.yaml:431`, 16.67 ticks/s): `BaseTicks: 1500` is 90 s, `MinTicks: 500` is 30 s, `BaseRecoveryTicks: 3000` is 180 s. Never convert at 25 tps. The comments that still do are catalogued in conventions.md §"A change believed made, documented as made, and inert".

**C3. Line 10, the "⚠️ PATHS CORRECTED 2026-09-11" block.** Replace with:
> Every launcher named here lives in `tools/autotest/`. Run them from the repo root as `./tools/autotest/run-test.sh --hidden <test>`. Exit 126 or 127 from a launcher is a **launch failure**: nothing ran, so it is not a test result. `--hidden` never maps a window but writes no screenshots, so use the default profile for capture runs.

**C4. Line 92, the scenario row (the heaviest accretion in the instruction set: "CORRECTED 2026-09-04", "an earlier version of this correction", "This figure has now gone stale twice").** Replace the right-hand cell with:
> `.\make.ps1 test` lints **every** scenario as well as the shipped maps. Its `Testing map:` count equals `ls -d mods/ww3mod/maps/*/ | wc -l` plus `ls -d tools/autotest/scenarios/*/ | wc -l`; recount both, and never quote a number. Whether Linux `make test` covers scenarios is unverified. `make nav-guard` does **not** cover them (its baseline is `mods/ww3mod/maps`). To lint one scenario, run `./utility.sh --check-yaml ../tools/autotest/scenarios/<name>` from the repo root. The `../` is needed because the script cds into `engine/`. Never run `engine/utility.sh`: it is mode 100644, exits 126 and writes a zero-byte log. Require `Testing map:` in the output, since a fast non-zero exit with no output is a launch failure, not a slow tool. Then run `.\make.ps1 lua-gate` (2 s, also part of `test`). Lint cannot see that a `rules.yaml` which `map.yaml` never declares is silently ignored (`Map.cs:178`, `:100-107`; `CheckLuaScript.cs:21-22`). Detail: conventions.md §"Scenarios are NOT maps to SOME tooling", AUTOTEST.md §"Verify before you ask for a slot", tools/lua-gate/README.md §"The second failure class".

**C5. Build block, `make test` comment.** Add one line: "`test` builds Release first, then runs `nav-guard`, `lua-gate`, `smudge-gate`, `mount-gate` and `--check-yaml` (`Makefile:306`)." Separately, state `smoke` in the push rule on line 8 ("build clean, NUnit green, smoke exit 0"), or say explicitly why the push gate omits it.

---

### .maestro/MAESTRO.md

| # | Line | Claim (short) | Verdict | Evidence / what's true now |
|---|---|---|---|---|
| 1 | 33 | Struck-through "ask the user before a batch" rule left in place, with "SUPERSEDED" pointing below | STALE (correction stacked on correction) | The live rule is §"Two standing operating rules". The struck text adds no information. Rewrite A below. |
| 2 | 45 | List of what workers never run: `launch-game.sh`, `run-test.sh`, `run-batch.sh`, `run-tournament.sh`, screenshots | STALE (incomplete) | The list leaves out `run-smoke.sh` / `make smoke`, `run-demo.sh` and `loop-tournament.sh`. All of them launch the game: `run-smoke.sh:179`, `run-demo.sh:54`, `loop-tournament.sh` calls `run-tournament.sh`. CLAUDE.md presents `smoke` to workers as an ordinary gate. |
| 3 | 45 | Workers never run `run-test.sh` | CONTRADICTED | CLAUDE.md hard rule: "One `./tools/autotest/run-test.sh <test>` for the bug at hand is the normal flow". `DOCS/recipes/AUTOTEST.md:27-35` also tells workers to run it. MAESTRO admits this at :37, but every worker loads CLAUDE.md and never sees this file. |
| 4 | 50 vs 83 | :50 says workers never launch, so build before the manager's launch. :83 tells the brief to say "run `make all` … before your first launch" | CONTRADICTED (internally) | :83 is pre-2026-08-19 boilerplate that still assumes workers launch. Rewrite B below. |
| 5 | 54 | 2026-08-19: a validator "never got a turn" (0-byte output) because of contention | CONTRADICTED | CLAUDE.md's scenario row says this was "very likely" `engine/utility.sh` (mode 100644) exiting 126 with a zero-byte log, i.e. a launch failure and not queueing. Half of the evidence for rule 2 is disputed. The ~35-min/8-jobs measurement is unaffected. |
| 6 | 56 | Workers run neither `--check-yaml` nor `make test` | CONTRADICTED | CLAUDE.md's scenario row tells workers to "Lint one with `./utility.sh --check-yaml ../tools/…`" and to "ALSO run `.\make.ps1 lua-gate` … now part of `.\make.ps1 test`". |
| 7 | 64 | Merge gate is `all → check → dotnet test → test` | STALE | It does not include `smoke`. `make.ps1:301-302` says "a skipped smoke gate leaves the merge gate believing a World was constructed". CLAUDE.md calls smoke the only command that constructs a World (the 2026-09-10 DefconWall incident passed every gate on this list). CLAUDE.md's push rule says only "build clean and NUnit green", which also differs. |
| 8 | 83 | `launch-game.sh:42` gates on `OpenRA.dll` + `VERSION` | STALE (line) | The guard is at `launch-game.sh:40`, and `exit 1` is at :43. `AUTOTEST.md:110` cites the same wrong :42. |
| 9 | 83 | `./utility.sh --check-yaml <MAPDIR>` lints a single map | CONTRADICTED (incomplete) | `AUTOTEST.md:110` and CLAUDE.md both say the path must be `../tools/autotest/scenarios/<name>` because `utility.sh` cds into `engine/`. MAESTRO gives the bare form that fails. |
| 10 | 83 | Unbuilt worktree gives `NO-RESULT (exit 3)` | UNVERIFIABLE | `run-test.sh` has no `OpenRA.dll` pre-flight (no grep hit), so this is plausible. Verifying needs one `run-test.sh` from an unbuilt worktree. |
| 11 | 80 | Worktrees live under `C:\Users\fredr\worktrees\ww3mod\<name>` | STALE (platform) | On this host they are at `/Users/fredrik/worktrees/ww3mod/<name>` (this audit's own tree). |
| 12 | 37 | "Workers never see this file" | UNVERIFIABLE | This is a harness property. It would be settled by checking a worker transcript's loaded context. |
| 13 | 94 | Item 64 ships switched OFF; R7 attracted two non-fixing commits | UNVERIFIABLE | Needs the item-64 flag default checked in YAML and the R7 commits identified. Not done. |

TRUE items checked: 17. Highlights:
- Every WORKSPACE path cited exists: `PIPELINE.md`, `pipeline/items/`, `pipeline/archive/`, `pipeline/README.md` (the relative link resolves), `cases/README.md`, `HOTBOARD.md`, `DISCOVERIES.md`, `bugs/discovered.md`.
- Every cited commit exists and its subject matches what MAESTRO says it is: `42bc611c` 09-11, `b0aa900c`/`201df112` 09-19, `ed5ee6b6` (a PIPELINE edit), `7385c055`, `55836dd8`.
- `rules/cameo-captions.yaml` and `VaporizeScopeTest.cs` exist.
- Release strips analyzers (`Directory.Build.props:50-53`).
- `AUTOTEST.md` does tell workers to run tests themselves, as :37 says.

### DOCS/reference/README.md

| # | Line | Claim (short) | Verdict | Evidence / what's true now |
|---|---|---|---|---|
| 1 | 55 | conventions.md catalogues "six worked instances" | STALE | `conventions.md:1124` reads "Six shapes, fourteen instances", with instances dated across four SHAs (:1139). |
| 2 | 12 | `architecture.md` "Covers" list | STALE (incomplete) | The list omits top-level sections that now exist: Escalation endgame (:1668), CustomTerrain vs render (:1744), Fog visibility/`Detectable` (:2105), map-edge band (:2164), combat feedback (:2353), Garrisoning (:2487), Support powers (:949), Production queues (:1081), Order targeter (:862), Directional armor (:766), Resupply docking (:815). |
| 3 | 13 | `missiles.md` = "guidance, launch angles, termination paths" | CONTRADICTED (with :3) | `missiles.md:3-5` says it states INTENT and that "where the shipped code disagrees … the code is wrong". It is a normative spec, not a code-verified description, so "trusted without re-verification" (:3) does not apply in the same sense. The table should say so. |
| 4 | 18, 42 | `shadow-los-plan.md` is a roadmap in reference; "Reference ≠ tracker" | CONTRADICTED (internally) | `shadow-los-plan.md:3` reads "Planned for a future session". By :42 it belongs in `WORKSPACE/`. |
| 5 | 17, 41 | `project-assessment.md`; volatile claims need `(as of …)` | CONTRADICTED (weakly) | It is headed "Date: March 2026" and compares against "Latest OpenRA release-20250330". The whole doc is a dated snapshot sitting in the trusted tier. |
| 6 | 26 | A seeded doc gets a verification-date header | STALE (partially honoured) | Only `influence-stack.md:3` has one, and `missiles.md` has an agreed-date. The other seven do not. |
| 7 | 51 | `b8d2e601` "flipped nine such flags at once" | UNVERIFIABLE | The commit exists and is the @stable parity promotion. Its message lists modules, not a flag count. Verify by counting the changed `@stable` flag keys in `git show b8d2e601 -- mods/ww3mod/rules/ai.yaml`. |

TRUE items checked: 9. Highlights:
- The doc table matches `ls DOCS/reference/` exactly: 10 docs, nothing missing, nothing extra.
- The "Indestructable is inert, `NoAutoTarget` protects" worked example is corrected in both `supply-route.md:12` and `game-model.md:9` as claimed.
- `architecture.md` §Saved games exists, and `World.EndGame()` has four callers (`architecture.md:1965`).
- The `conventions.md` §"A change believed made…" anchor exists (:1114).

### tools/autotest/README.md + script headers

| # | File:Line | Claim (short) | Verdict | Evidence / what's true now |
|---|---|---|---|---|
| 1 | README:7-20 | Layout lists 6 scripts and `test-*`/`demo-*` | STALE | The directory has 24 `.sh` files. Missing from the layout: `run-tournament`, `loop-tournament`, `expected-status`, `selftest`, `selftest-launch-failure`, `screenshot*`, `start-screenshot-mode`, `run-synchash`, `watch-replay`, `compare-batches`, `aggregate-tournament`, `tournament-report`, `poll-copy-logs`, `synchash-launcher`. Scenario prefixes now: **368 = 294 test- + 42 demo- + 31 tournament- + 1 wip-**. |
| 2 | README:22 | "Why a hidden folder?" | STALE (wording) | `tools/autotest` is not a dot-folder. The mechanism described is correct: `mod.yaml:92-96` registers it as Class=Unknown. |
| 3 | run-test.sh:257 | `--help` prints the usage | STALE | `--help` prints lines `2,130` only, which ends at the `--seed` example. It cuts the `--lifecycle` example, the **"Exit code: 0/1/2/3" line (:133)**, and the whole "Reading the verdict" section (`AUTOTEST_VERDICT`, `AUTOTEST_OUTCOME_FILE`, the `| tail` trap). The header now runs to about :205. |
| 4 | run-test.sh:4-10 vs :228,238 | Position shorthand is L/R/F; "no shorthand → centered" | STALE (undocumented flags) | The parser also accepts `C|c|-C|-c|--centered` and the alias `--foreground` (:238), and `-h`. `AUTOTEST.md:30` documents `C`; the header does not. |
| 5 | run-test.sh:268 | Short usage line | STALE (incomplete) | It omits `--mute`, `--missile-trace-summary`, `--sync-reports`, `--fullscreen`, `--windowed`, `--position=`. All exist in the parser. |
| 6 | run-test.sh:155 | "exercised by tools/autotest/selftest-launch-failure.sh … Run it" | STALE | That file is tracked as mode `100644` (not executable). `./tools/autotest/selftest-launch-failure.sh` would exit 126, the same trap as `engine/utility.sh`. It works only as `sh <file>`. |
| 7 | run-batch.sh:20-21 | Exit = count of non-GREEN (cap 99) | CONTRADICTED (by its own usage errors) | Usage and validation errors exit **3** (:115, :121, :129, :136). A batch with exactly 3 reds also exits 3, so exit 3 is ambiguous. |
| 8 | run-batch.sh:135-150 | "TWENTY-ONE" verdict-less scenarios excluded from `--all` | STALE | Applying the script's own predicate at 2f8b5f6d excludes **31**. |
| 9 | run-batch.sh:164 / expected-status.sh:4-5 | `--all` excludes only scenarios with "NO verdict call at all" | CONTRADICTED (and a live bug) | The predicate greps only `scenarios/<name>/*.lua`. Scenarios whose Lua comes from `mods/ww3mod/scripts/*-lib.lua` via `rules.yaml` `Scripts:` (e.g. `test-defcon2-holdfire-*` x6, `test-drone-lost-track*` x2, `test-escalation-banner-*` x2) do emit verdicts (`defcon2-holdfire-ambush-lib.lua` has `Test.Pass/Fail`) but are silently dropped. **5 of the 23 `expected-status` declarations** (`defcon2-*-skirmish` x3, `drone-lost-track`, `drone-lost-track-control`) are therefore never graded by `--all`. |
| 10 | run-tournament.sh:4 | "Wraps run-test.sh" | STALE | It calls `./launch-game.sh` directly (:296), and its own comment at :268-270 says so. |
| 11 | run-tournament.sh:25,107 | `-v/--visible` passes `--visible`; default `--background` | STALE (dead flag) | `RUN_TEST_FLAGS` is set at :93/:107 and never read. Every match launches with `OPENRA_WINDOW_HIDDEN=1` (:296), so `-v` does nothing. |
| 12 | run-tournament.sh:35 | "Exit 0 if any matches ran; 3 on usage error" | STALE | It exits 0 unconditionally after the loop (:377), including when every match is no-verdict (`OK=0`). |
| 13 | run-tournament.sh:37, 290, 281 | Refs to `WORKSPACE/plans/260511_ai_tournament_harness.md`, `WORKSPACE/plans/260721_sim_throughput.md`, `PITFALLS.md §15` | STALE | The plans moved to `WORKSPACE/archive/plans/`. `PITFALLS.md` is now `WORKSPACE/ai/archive/PITFALLS.md` (ambiguous with `DOCS/reference/pitfalls.md`). |
| 14 | run-tournament.sh:110,126 | `--help` / short usage | STALE (minor) | Help prints `2,33`, which omits the exit-code line and limitations. The short usage omits `--max-wall-secs`, `--mirror`, `-v`. |
| 15 | loop-tournament.sh:11-21 | Target schema with `Scenario:` key and inline `# comments` | CONTRADICTED (by its parser) | `Scenario:` is never read; the scenario is positional `$1`. The awk parser (`-F': *'`, :71-78) folds inline comments into the value. Reproduced: `BatchSize: 10  # matches…` gives `10  # matches per round`, and `MirrorScenario: ""` gives literal `""`, which then reaches `--mirror` (:166). `example-target.yaml` is correctly comment-free. |
| 16 | run-demo.sh:11-12, 48-49 | "returns 0 either way"; no-verdict means run-test exit 3 | STALE | `run-demo.sh` forwards no `--timeout`, so run-test's 300 s watchdog (`run-test.sh:215, 994`) kills the demo window at 5 min and synthesizes `TIMEOUT-FAIL`, exit 1. `run-demo.sh:56-59` maps only 3 to 0, so it returns 1. Confirm with one demo left open for more than 300 s. |
| 17 | screenshot.sh:14-16 | `--wait` polls "until `<label>` appears" | STALE (minor) | It waits for the manifest entry count to grow and returns the last path whatever the label (:100-105). Fixed 10 s deadline, timeout exit 2 (:114), errors exit 1: none of this is documented. |
| 18 | expected-status.sh:84-85 | Outcome-name list | STALE (minor) | It omits `LAUNCH-FAIL`, which `run-test.sh:143` lists. It is harmless by design: the allowlist makes unknown outcomes RED. |
| 19 | run-smoke.sh:93 | `SMOKE_VERDICT` counts on launch failure | STALE (minor) | `fail_launch` hard-codes `passed=0 failed=0` even when it fires mid-loop after some maps already passed (:188, 203, 208). |
| 20 | run-smoke.sh:6 | `--quick` takes ~30 s | UNVERIFIABLE | Needs one `run-smoke.sh --quick`. |

**CLAUDE.md claims about these scripts, verified:**
- `run-smoke.sh` exits 0/2/3: TRUE (:237, :249, :93).
- "Canary, then each of the ten shipped maps": TRUE (:220-230). `mods/ww3mod/maps` has **10** dirs, including `shellmap-open-field` and `arena-tank-duel`.
- A non-PASS in under 8 s is a launch failure: TRUE (`MIN_PLAUSIBLE_SECONDS=8`, :69, :207). One addition: `HARNESS-ERROR`/`INTERRUPTED` are always treated as launch failure, whatever the time (:200).
- The last line is `SMOKE_VERDICT outcome=… exit=… passed=… failed=…`: TRUE.
- Exit 2 nuance: it also covers a map that reached a `fail` verdict (:29-30), not only "never reached a verdict".
- `--hidden` is "the unattended/tournament profile" in `run-test.sh`: TRUE (:20-22).
- Scenario count is now **368** (294/42/31/1), against the 320 CLAUDE.md recorded at `e82fe534`.

TRUE items checked: 30. Highlights:
- Every flag in the `run-test.sh` header exists in the parser (:225-255).
- `run-batch.sh`'s documented flags match its parser exactly (:80-87), and `--speed` defaults to 8.
- `run-smoke.sh`'s header, parser and exit semantics all agree.
- The README's exit codes 0/1/2/3, per-run result dir and `"status":"moved"` stub are all confirmed (`run-test.sh:744`).
- `--map` semantics are correct.
- `expected-status.sh --selftest` entrypoint exists.
- `screenshot.sh`'s "~40 ms" is correct for menus/lobby (`Ui.Timestep`, `Game.cs:1008`); an in-match tick at default speed is 60 ms (`mod.yaml:429-431`).

### Rewrite proposals

**A. MAESTRO.md :33 + :52-70. Replace the struck rule and the two AMENDED blocks with one statement:**

> ### 2. The merge gate belongs to the manager
> Workers run `make all`, `make check` and `dotnet test` in their own worktree. All three are uncontended builds, and `check` is the only Debug/analyzer build: Release strips analyzers (`Directory.Build.props:50-53`), so a green `all` or `dotnet test` says nothing about RCS-class errors. Workers do not run `--check-yaml` or `make test`. Lint queues behind every concurrent worker, so it runs once, at merge. Each worker report lists the YAML it touched and what lint would say if it were wrong.
> The manager's merge gate, in order: `all` → `check` → `dotnet test` → `test` → `smoke`. Run `dotnet test` even for `mods/`- or `tools/`-only branches, because NUnit fixtures read shipped YAML. Skip only the builds, and only when the C# is byte-identical. Never `pkill -f OpenRA.Utility`; kill your own pid.

(about 130 words; adds `smoke`, removes the changelog)

**B. MAESTRO.md :50 + :83 "fresh worktree" bullet. Merge them into one consistent rule:**

> **Building is a property of the worktree, not of the change.** `engine/bin` is not in git, so a new worktree has no build. `launch-game.sh:40` refuses to start without `engine/bin/OpenRA.dll` and a matching `VERSION`, and the run ends as `NO-RESULT` with an empty run dir. Workers do not launch, so their brief says "run `make all` before your first commit". The manager launches only from a tree it has built, its own or a worker's after `make all`. The free launch-less check is `./utility.sh --check-yaml ../tools/autotest/scenarios/<name>` from the repo root. The `../` is required, it lints YAML but not Lua, and it is a manager action under rule 2.

(about 110 words; removes the "before your first launch" instruction that conflicts with rule 1, fixes the line cite and the missing `../`)
---

### DOCS/recipes/AUTOTEST.md

| # | Line | Claim (short) | Verdict | Evidence / what's true now |
|---|---|---|---|---|
| 1 | 108 | OUTCOME is one of PASS, PASS-EMPTY, FAIL, SKIP, TIMEOUT-FAIL, CRASH, NO-RESULT, BAD-VERDICT, INTERRUPTED, HARNESS-ERROR | STALE | `run-test.sh:141` also lists **`LAUNCH-FAIL`**: the server refused the client at join, so no world was built. Doc lines 108–110 never mention it, and line 110 sends "the game never launched" to `NO-RESULT`. `tools/autotest/selftest-launch-failure.sh` exists. |
| 2 | 124–128 | `run-test.sh:176` sets `TIMEOUT_SECS=300`; `--timeout` at `:208-209`; validated `:326-335`; watchdog loop `:771-798`; TIMEOUT-FAIL `:983-985` | STALE (cites) | The behaviour is still true. The lines moved: `:215` default, `:250-251` flag, `:414-423` validation, `:962-998` watchdog loop, `:1263-1265` TIMEOUT-FAIL. |
| 3 | 128–129 | "NOT scaled by --speed" comment at `:326-327` | STALE (cite) | The comment is now at `run-test.sh:414-415`. |
| 4 | 133–134 | `--speed` at `:204-205`, range-checked `:291-300`, becomes SpeedMultiplier at `:626-628` | STALE (cites) | Now `:246-247`, `:379-390` and `:814-818`. Behaviour is true. |
| 5 | 135 | `TestModeSpeedMultiplier.cs:39-41` | TRUE (≈) | The assignment is at `:40`. |
| 6 | 143, 148 | `DefconEscalation.cs:415` reads Timestep; its comment is at `:410-413` | STALE | Now `:510` (read) and `:505-509` (comment). `TimeLimitManager.cs:137` and `NuclearUnlockClock.cs:281-284` are still exact. |
| 7 | 149–150 | `Game.cs:1006` (pacing); `DoomsdayStrike.cs:738` (immutable base) | STALE | Pacing is at `Game.cs:1008-1013`. DoomsdayStrike now reads `GameSpeed.Timestep` at `:1000` (PITFALL at `:995`). |
| 8 | 163 | "Audited at `e0674307` across all 343 directories" | STALE (dated count) | There are now **368** scenario directories. The audit has not been redone since then; see #10. |
| 9 | 169 | `test-escalation-full-match` outer `DEADLINE = 24000` at `.lua:64` | STALE (cite) | Now `test-escalation-full-match.lua:77`. `rules.yaml:72` (`TimeLimitTicks: 22000`) is still exact. |
| 10 | 173–174, 186–189 | `combined-arms-rendezvous` `DeadlineSeconds = 200` "= 5000 ticks … exactly the watchdog"; `wip-transport-delivers` 180 "= 4500 ticks"; "the last two rows sit at or just under the watchdog" | STALE (25-tps residue) | Both scenarios pass `DeadlineSeconds` straight to `TestHarness.AssertWithin` (`test-combined-arms-rendezvous.lua:143`, `test-transport-delivers.lua:89`). That function converts through `TicksForSeconds` at 60 ms (`test-helpers.lua:44,119`). So 200 s is **3333 ticks, i.e. 200 s wall**, and 180 s is **3000 ticks, i.e. 180 s**. Both clear the 300 s watchdog by roughly 100 s, which is under the table's own 5000-tick threshold. These two rows, and the paragraph at 186–189, are leftovers from the old 25 tps arithmetic. |
| 11 | 180–183 | `test-escalation-full-match` "has effectively never been run to completion by the default invocation" | STALE / misleading | This holds for a bare `run-test.sh` only. `run-batch.sh` **defaults to `--speed 8`** (`run-batch.sh:7-15`, `:77` `SPEED="${BATCH_SPEED:-8}"`), so inside `--all` the 22000 ticks take about 165 s and fit under 300 s. The doc never says that the batch default differs from `run-test.sh`. |
| 12 | 217 | `Game.cs:1200-1204` warns that an unknown speed key falls back silently | STALE (cite) | The warning comment is now at `Game.cs:1208-1210`. |
| 13 | 259 | Link `SCREENSHOT.md#apply-automatically-no-trigger-required-when` | STALE (dead anchor) | `SCREENSHOT.md:5` is bold body text, not a heading, so the anchor does not exist. |
| 14 | 264, 489 | Loop step 6 is "Regression check: `run-batch.sh --all`"; line 489 says "Pair with `--all` for unattended regression sweeps" | CONTRADICTED | CLAUDE.md Hard rules: "No autonomous multi-test runs… Explicit goahead in the current turn required before: `run-batch.sh`…". `.maestro/MAESTRO.md:45`: "No worker runs … `run-test.sh`, `run-batch.sh` …". `MAESTRO.md:37` itself says AUTOTEST.md "still tell[s] it to run things itself". Steps 3, 5 and 6, and line 13 ("You can walk away while it runs"), conflict with both. |
| 15 | 266 | "See CLAUDE.md 'PITFALL Comments'" | STALE | CLAUDE.md has no such section. The spec is in `DOCS/reference/conventions.md:1286` §"PITFALL comments", which points on to `pitfalls.md`. |
| 16 | 269 | Multi-layer bug: "leave the test RED … document in RELEASE_V1.md" | CONTRADICTED (internal) | The doc's own lines 64–80 say a knowingly-failing scenario must carry an `expected-status` file "or it reds every batch forever". Line 269 never mentions that file. Example: `test-arty-force-attack-during-setup` (line 1167, "currently RED") has **no** `expected-status` file. |
| 17 | 28–36 | Quick reference: default run is "centered, background, muted"; `--hidden` is not listed | CONTRADICTED (guidance) | CLAUDE.md: "Prefer `--hidden`, which the runner's own usage calls the unattended profile … `./tools/autotest/run-test.sh --hidden <test>`". The flag exists (`run-test.sh:17-22,236`). Gotcha 4 (≈line 1134) also omits it. |
| 18 | 110 | `launch-game.sh:42` aborts with "Required engine files not found." | STALE (off by one) | The message is at `:41`. The guard at `:40` also fires on an `engine/VERSION` mismatch, not only on a missing DLL. |
| 19 | 421 | Gate table: `./utility.sh --check-yaml …` is "the only one that types a field"; rows for `make check`, `dotnet test`, `lua-gate`, `nav-guard` | STALE / CONTRADICTED by omission | CLAUDE.md routing row: `.\make.ps1 test` "lints EVERY scenario". That is the same validator run over the whole corpus, and it is the gate a launch-barred worker most likely did run. The table leaves it out, so it implies no scenario-content gate runs at merge. The Linux `make test` (`Makefile:306-308`, bare `--check-yaml`) is UNVERIFIABLE here: checking it needs a captured `make test` log, counting `Testing map:` lines against `ls -d tools/autotest/scenarios/*/`. |
| 20 | 425 | "the four compass names exist only in the Lua binding (`AngleGlobal.cs:23-38`)" | STALE (imprecise) | `AngleGlobal.cs:23-38` defines **eight** names (N, NW, W, SW, S, SE, E, NE). The line range is right. |
| 21 | 453 | `test-drone-targeting`'s 28-cell confound guard | STALE / UNVERIFIABLE | No scenario with this name exists on main or on any ref (`git log --all` finds nothing). Its only mention anywhere is this line. It probably belongs to the held `wt-drone-guard` branch (`.maestro/.../decisions/32-hold-wt-drone-guard…`). |
| 22 | 544 | `Test.*` has the listed members "and ~50 more" | STALE | `TestGlobal.cs` has about **109** public bindings, so roughly 100 more. |
| 23 | 548–551 | `grep -rn "\.Build(" tools/autotest/scenarios/` returns "four confident-looking hits — all a scenario-local helper" | STALE | It now returns **5** hits. Four are `JavelinProbe.Build`, the local helper. The fifth, `test-power-buy-loop.lua:131` `Russia.Build({ ProxyType })`, is the **real** player-level binding (`ProductionProperties.cs:250`). |
| 24 | 561–562 | `FrozenActorState` `TestGlobal.cs:1065`, `:1084`, `:1099`, `FrozenClickCursor` `:1114` | STALE (cites) | Now `:1079`, `:1098`, `:1113`, `:1128`. |
| 25 | 618 | `test-transport-delivers` names a carrier and five riflemen | STALE (name) | The directory was renamed to `wip-transport-delivers` in `fe692f17`. Line 624 already uses the new name. |
| 26 | 620 | `CargoUnloadMenuLogic.cs:180-181` clip is `Math.Min(380/…)`; `Test.GetUnloadMenuGeometry` at `TestGlobal.cs:277` | STALE | The clip arithmetic was refactored into `UnloadMenuGeometry.Measure` (`CargoUnloadMenuLogic.cs:192-196`, setting `ClipHeight`/`MenuHeight`). The binding is at `TestGlobal.cs:311`. The rule itself (assert the geometry) still holds. |
| 27 | 623 | `ClickOrder` delegates to `OrderForUnit` at `TestGlobal.cs:739-747` | STALE (cite) | Now `:745-761`; the call is at `:761`. Behaviour is true. |
| 28 | 652 | Boarding calls `w.Remove(self)` at `RideTransport.cs:85` | STALE (cite) | Now `:87`. `Actor.cs:76` and `World.cs:404-412` are still exact. |
| 29 | 739 | `ai/ai.yaml:1895` (`DropRequiresDanger: true`) and `:1689` (`IgnoreDangerForDelivery: true`) | STALE (cites) | Now `mods/ww3mod/rules/ai/ai.yaml:2023` and `:1813`. `EvacDangerUnits: 50` is at `:1834`. The values are true. `SupplyFollowerBotModule.cs:1662` is still exact. |
| 30 | 816 | `Test.LobbyOption` at `TestGlobal.cs:2141` | STALE (cite) | Now `:2155`. `MapGlobal.cs:112-123` is still correct. |
| 31 | 1011 | `Test.GetVisibilityLevel` `TestGlobal.cs:471-478` | STALE (cite) | Now `:485-492`. The −1 / 0 / ≥1 sentinels are true. `Detectable.cs:131` and `:120-121` are exact. |
| 32 | 1032 | `ConditionCount` guard at `TestGlobal.cs:1016-1017` | STALE (cite) | Now `:1028-1031`, with identical text. |
| 33 | 1042, 1059, ≈1062 | `IsLoadedInto` `:868`; `IsAtGarrisonPort` `:989`; `GarrisonPortOf` `:935`; `TargetableReport` `:961` | STALE (cites) | Now `:882`, `:1003`, `:949`, `:975`. Every name exists. |
| 34 | 1138 | settings.yaml backup at `run-test.sh:633-634`, restore at `:773-774`; `screenshot-lobby.sh:135-136` / `:177-178` | STALE (cites) | `run-test.sh` backs up at `:882-883` and restores in the idempotent `restore_settings` (`:308-321`), which is called from the EXIT trap (`:343`) and at `:1063`. `screenshot-lobby.sh` backs up at `:146-148` and restores at `:191-192`. The whole-file semantics are true. |
| 35 | 1139 | `^StandardVision` at `defaults.yaml:47-`; `Detectable.cs:78-80` recomputes; `^DetectableInfantryStandard` at `infantry.yaml:703-721` | STALE (cites) | Now `defaults.yaml:115` (bands are true: 10→4c0, 9→7c0, 8→10c0) and `infantry.yaml:776-793` (+1 prone, +1 dugin, up to +3 cover, all true). The recompute is in `ITick.Tick` at `Detectable.cs:127-131`; `:78-80` is the constructor. |
| 36 | 1140 | `StancePositioningExecutor.cs:318` opts out below FireAtWill | STALE (cite) | The gate is now `:330` (`FireStanceAllowsRepositioning`, `:599-602`, `stance >= FireAtWill`). The enum `{HoldFire, Ambush, FireAtWill}` makes the claim true. |
| 37 | 1167 | `test-arty-force-attack-during-setup` is "currently RED", Layer 2 open | UNVERIFIABLE | One `run-test.sh --hidden test-arty-force-attack-during-setup` would verify it. See #16 for the missing `expected-status`. |
| 38 | 109 | `selftest.sh` takes about 1 min | UNVERIFIABLE | Needs one timed `./tools/autotest/selftest.sh` run. |
| 39 | 207–209 | Slow host: 15–22 ticks/s; first truck at tick ~4640; a 7,500-tick match needed 337 s | UNVERIFIABLE | Historical measurements. Re-checking needs one `run-tournament.sh` on `tournament-s1-eco-river-zeta` with an explicit `--max-wall-secs`. |
| 40 | 521 | "91 deadlines across 137 scenario files" (2026-08-27 audit) | UNVERIFIABLE (dated) | This is a historical count. Re-checking it would mean re-running the static audit at HEAD. |

TRUE items checked: about 70. Highlights:
- **Tick rate.** 16.67 tps, `Timestep: 60` (`mod.yaml:406-407,431`). `fastest` is 40 ms (`:443`). `TestHarness.TimestepMs = 60` and `TicksForSeconds` (`test-helpers.lua:32-33,44`) are used at `:119/:213/:321`. `DateTime.Seconds` goes through `TickTime.TicksForSeconds`. `AutotestTickRateTest.HarnessAndEngineAgreeTickForTickOnEveryIntegerSecond` exists. "Never write 25 tps" is correct.
- **`run-test.sh` behaviour.** All flags and aliases exist: `--map`, `--speed` (1–16), `--seed` (rejects 0), `--timeout`, `--visible`/`--no-minimize`, `--audio`, `--minimized`, positions L/R/F/C. `--map` is validated at `:573-580`. Seed flag at `:248-249`, validation at `:392-411`, `RandomSeed` at `:821-825`. Exit codes are 0/1/2/3. The EXIT-trap banner, `AUTOTEST_OUTCOME_FILE`, the stderr echo only when stdout is not a tty, the per-run `result.json` path and the "moved" stub are all as described.
- **`run-batch.sh`.** The `--all` filter keeps only scenarios that call a verdict function. `NO-OUTCOME`, `OUTCOME-MISMATCH`, `PASS-EMPTY` and "NEVER REACHED A VERDICT" all exist.
- **Map loading.** `Game.cs:1222` matches maps by UID or directory name and throws on a miss. `Game.LoadMap` hardcodes `"default"` unless `Test.GameSpeed` overrides it. `SmokeTestExit` and `Test.SmokeTicks` exist.
- **`run-tournament.sh`.** `:44`, `:139-145`, `:148`, `:153-166`, `:172-180`, `:262-263`, `:276-277` and `:347-348` are all exact. There are 53 tournament configs: 52 in scenarios plus `tools/autotest/tournament-combat-12min-combatweighted.yaml`. Of these, 42 set `fastest` and 11 plain `tournament.yaml` set no GameSpeed. The S1 ladder dir ships no `tournament.yaml`. `TournamentConfig.cs:8` marks `Matchup` as informational.
- **Engine cites that are exact.** `World.cs:220/252/320-347/404-412`, `Actor.cs:75-76`, `ActorMap.cs:478/649`, `WorldUtils.cs:79`, `CombatProperties.cs:112-116`, `TargetDamageWarhead.cs:89/96`, `SupportPowerManager.cs:377-405`, `PowersLobbyOptions.cs:168`, `MissileStrikePower.cs:624`, `Log.cs:160`, `Immobile.cs:23-27`, `modload.py:300/330`, `TestMode.cs:39/311`, `TestModeLogic.cs:30-31`, `AutoSeekSupplies.cs:202`.
- **Tools and tests.** `lua_gate.py` is executable and `nav_guard.py` is 100644, as stated. Every `nav_guard`/`modload` function used in the inline Python exists. Exit codes 2/1 are right and `make lua-gate` fails only on 2. Every NUnit test named exists. `SupplyDriftClauseTest` has `:55` `ReadScenarioConstant` and `:67` `Assert.Ignore`.
- **Scenario facts.** Every scenario with a `rules.yaml` still carries `Rules:`. There are still 15 sandbox `rules.yaml` files. `test-experimental-poi-observe/map.yaml:96` is right. `^Infantry` `Speed: 25` holds. Lua 5.5.1 is at `/usr/local/bin/lua`.

### Rewrite proposals

**1. Lines 508–521, the tick-base cluster (worst case).** Four paragraphs say "as of 2026-09-21, and it was not before", "this paragraph used to say there were three", "The constant HAS been fixed … this paragraph used to forbid exactly that". The current rule is buried under them.
> **`seconds` means real seconds.** `TestHarness.TicksPerSecond` is derived from `TestHarness.TimestepMs = 60` (`test-helpers.lua:32-33`, mirroring `mod.yaml:431`). `TestHarness.TicksForSeconds` (`:44`) converts the way the engine's `DateTime.Seconds` does: `seconds * 1000 / timestep`, multiplied before dividing. So `AssertWithin(n)` and `DateTime.Seconds(n)` give the same tick count for every integer n. `AutotestTickRateTest` pins the rate, the agreement over 0..600 s, and the arithmetic of `test-autotarget-preempt-air` and `test-critical-no-panic`. `run-test.sh` sets no `Test.GameSpeed`, so this applies to every scenario. **Size deadlines in ticks** and convert with `TicksForSeconds`, not `math.floor(s * TicksPerSecond)`. For tick-domain quantities, poll with `Trigger.AfterDelay(1, …)`. Scenarios whose budget was sized by measurement before 2026-09-21 have a third less time than their author measured; triage reds with the discriminators below.

**2. Line 652, the struck-through `IsDead` claim.** It opens with a strikethrough plus "REFUTED", then spends most of the paragraph on where the old claim came from.
> **`IsDead` is false for a passenger inside a `Cargo`.** `Actor.IsDead` is `Disposed || health.IsDead` (`Actor.cs:76`). Boarding calls `w.Remove(self)` (`RideTransport.cs:87`), which clears `IsInWorld` without disposing the actor or touching its health (`World.cs:404-412`). Run `260906_091912` confirmed it: every aboard rifleman read `dead=false`. To latch "was carried", use `not r.IsInWorld`, paired with a separate clause that requires the unit to return to the world. That form is permissive, since a corpse also latches. For an exact count, add `and not r.IsDead` (as `test-combined-arms-rendezvous`'s `EverCarried` could).

**3. Lines 381–391, the nav-guard marker note.** It opens "FIXED 2026-09-01 — this note used to warn…" and says "Do not reason from the old warning".
> **Map markers occupy nothing, in the game and in the decoder alike.** `mpspawn`, `spawnarea`, `waypoint`, `flare` and the `camera.*` actors carry `Immobile: OccupiesSpace: false`, so `ImmobileInfo.OccupiedCells` returns an empty dictionary (`Immobile.cs:23-27`). `modload.actor_shape` honours the same flag via `_immobile_occupies_space` (`modload.py:300`, branch at `:330`). If a cell holding only a marker reads as blocked, something else is on that cell; investigate it rather than dismissing it as a tool artefact. Model details are in `tools/nav-guard/README.md` §Zero-footprint actors.

**4. Lines 229–241, the tournament clock.** It reads "mis-stated, and FIXED 2026-09-19 — but only for 11 of the 53", then argues with a dated bug entry.
> **A tournament's duration depends on its `GameSpeed:` key; read the key before reasoning about time.** `run-tournament.sh:148` passes the key as `Test.GameSpeed`, and `TournamentConfig` converts `TimeLimitSeconds` with the resolved timestep. 42 of the 53 configs set `GameSpeed: fastest` (40 ms, 25 ticks/s): the `-smoke`, `-sanity`, `-quick`, `-eco-5min`, `-combat-12min` variants and `tournament-arena-composition-2p`. The 11 plain `tournament.yaml` files set none and run at the 60 ms default. Their `TimeLimitSeconds: 1080` is 18000 ticks, or 18 real minutes. An unknown speed key silently falls back to the default (`Game.cs:1208-1210`). `BotVsBotMatchWatcher` therefore logs the timestep it actually resolved at `WorldLoaded`.
---

## Audit: DOCS/recipes/*.md at `main @ 2f8b5f6d` (worktree `/Users/fredrik/worktrees/ww3mod/scout-instructions`)

**Baseline:** the default game speed is `Timestep: 60` (`mods/ww3mod/mod.yaml:429-431`, `DefaultSpeed: default` at `:407`). That is 16.67 ticks/s, so 1500 ticks = 90 s, as CLAUDE.md says. The menu/UI logic tick is a separate 40 ms (`engine/OpenRA.Game/Widgets/Widget.cs:26`).

The biggest problem is in how the recipes relate to other files. SCREENSHOT, DEMO and BALANCE tell the reader to launch the game themselves (screenshots, demos, `run-batch.sh`). `.maestro/MAESTRO.md:37,44-48` says workers must never launch anything, and says outright that it "contradicts the recipes".

### DOCS/recipes/SCREENSHOT.md

| # | Line | Claim (short) | Verdict | Evidence / what's true now |
|---|---|---|---|---|
| 1 | 11, 13-18 | Agent should take screenshots on its own, "without asking" | CONTRADICTED | `.maestro/MAESTRO.md:44` says no worker runs "any screenshot capture", and `:37` names SCREENSHOT.md as the recipe it overrides. CLAUDE.md's routing table still says SCREENSHOT "applies by default". |
| 2 | 20-24, 302 | "Works in three modes" (1, 2, 3 = Ctrl+P); the Tiles-tab trap is "recorded under Mode 3" | STALE | The sections are Mode 1, Mode 2, **Mode 4**, then "Phase 3". There is no Mode 3 section. The Tiles-tab trap is in "Opening the MAP EDITOR" (`:379`). The Mode 4 lobby capture is missing from the list of modes. |
| 3 | 26, 185 | State queries `unit.IsFiring`, `world.Players`, `unit.AmmoCount` | STALE | No `IsFiring` binding exists anywhere in `engine/OpenRA.Mods.Common/Scripting`. `AmmoCount` is a method, `AmmoCount(pool)` (`AmmoPoolProperties.cs:34`), not a property. Players come from `Player.GetPlayers` (`PlayerGlobal.cs:31`). `Test.GetActiveMissileCount` is real (`TestGlobal.cs:1561`). |
| 4 | 78 | Incident in `test-cargo-panel-full` | STALE | No such scenario on `main`. The nearest is `test-visual-cargo-panel`. The name is only used again in `WORKSPACE/audit/260817-mi28-d5-predictions.md:180`. |
| 5 | 84 | `WithDecorationBase.cs:101-105` and default at `:44` | STALE | The `RenderPlayer != null` gate is now at `:165-169`. `ValidRelationships = PlayerRelationship.Ally` is at `:108`. The behaviour described is still correct. |
| 6 | 114 | External command: engine "captures synchronously" | STALE | `PollCommands` → `Capture` → `Game.TakeScreenshot(path)` (`TestModeScreenshots.cs:76`). That only sets the flag, so the pixels are read one frame late, the same trap as Mode 1. `manifest.json` is written at `:89` **before** the PNG exists, so `--wait` can print a path to a file that does not exist yet. |
| 7 | 114 | Command file polled "each tick (~40 ms)" | TRUE (menu only) | 40 ms at the menu (`Widget.cs:26`). In a match the logic tick is the world timestep, 60 ms at default speed. |
| 8 | 130 | "In-match captures carry the real `WorldTick`" | STALE | External captures always pass `-1` (`TestModeScreenshots.cs:211`: `Capture(label, "phase 2 external trigger", -1)`). Only Lua `Test.Screenshot` records the real tick (`TestGlobal.cs:103`). |
| 9 | 147-152 | Mode 4 options table | STALE (incomplete) | `screenshot-lobby.sh:55-61` also accepts `--hover=`, `--set-options=` and `--window=`. |
| 10 | 192-199 | Token cost table (2560×1440 ≈ 4,900 tokens) | UNVERIFIABLE | The w×h÷750 arithmetic is correct. But the API downscales long edges over about 1568 px, so the top two rows probably overstate the cost. Check: `Read` one full-size capture and compare the token use. |
| 11 | 214 | Blank-capture warning names only `--minimized`; suggests `--background`/`--visible` | STALE / CONTRADICTED | Omits `--hidden`, which writes **no PNG at all** while `result.json` still lists one (`DOCS/reference/conventions.md:1002-1008`, `AUTOTEST.md:984-986`). CLAUDE.md's hard-rules line says "Prefer `--hidden`" with no exception for capture runs. |
| 12 | 215 | "(see the plan doc)" | UNVERIFIABLE | No path is given. No plan doc for screenshots was found by name. |
| 13 | 227 | `--size 1280x800` → 2560×1600 PNG on a 2x display | UNVERIFIABLE | Needs one capture on a Retina display. |
| 14 | 143, 112 | Lobby round trip 10-20 s; blank frame 59 KB vs real 1.6 MB | UNVERIFIABLE | Historical measurements. One Mode 4 run would re-check them. |
| 15 | 306, 381 | "`LabelWidget.Draw` is that delegate's only caller" | STALE (overgeneralised) | `LabelWidget.IncreaseHeightToFitCurrentText` (`LabelWidget.cs:76`) and `AnonymousProfileTooltipLogic.cs:24` also call `GetText()`. The claim only holds for `ZONE_SPLIT_LABEL`, and the code's own comment says so (`MapZonesLogic.cs:222`). |
| 16 | 367 | Log text `zone stroke applied: … CHANGED NOTHING` | TRUE | `EditorZoneBrush.cs:197-198`. |
| 17 | 369 | "Still genuinely unbuilt: `text <field-id> <value>`" | STALE | `type <widget-id> [text]` is built (`TestModeScreenshots.cs:240-252`). The file itself says so at `:365`, so the two lines contradict each other. Key events and scrolling really are still unbuilt. |
| 18 | 383 | `screenshot-editor-zones.sh` waits on `editor zone selected:` | STALE | It waits on `zone panel shown: components=2` (`screenshot-editor-zones.sh:249`). Line 379 of the same file already says this, and the script's comment (`:238`) calls the old marker the bug. |
| 19 | 291-316 vs 371-383 | — | Note | The "log from inside `GetText`" lesson and the Zones/Tiles-tab incident are written out twice. |

**TRUE items checked: about 45.** Highlights:
- Label sanitising and `NNN_` naming (`TestModeScreenshots.cs:72,395-409`).
- `TestModeLogic.cs:30-31`.
- `Test.KeepRenderPlayer` parsed at `TestMode.cs:311`; `1` does not work, but `TRUE` does because the match ignores case.
- `World.cs:109-115` fog/shroud short-circuit.
- PNG written synchronously under TestMode (`Renderer.cs:543`).
- "Capture is async" Desc (`TestGlobal.cs:95`).
- `ClickWidget` at `:305-317` and the log line at `:227`.
- `FindVisible` uses `IsVisible()`.
- `hover`, `zone-paint`/`zone-erase` and `quit` verbs.
- `watch-replay.sh` and `screenshot-infopanel.sh` really use `click`.
- `Test.OpenEditorMap` / `EditorTool` miss strings (`MainMenuLogic.cs:626`, `MapToolsLogic.cs:87`).
- Six editor tab containers with `Tiles` as the default (`MapEditorTabsLogic.cs:18,24,89`).
- All 10 integration-table paths exist.
- `test-screenshot-smoke` takes 3 captures then calls `Test.Skip`.
- 7-day cleanup (`run-test.sh:747-749`).
- Ctrl+P (`engine/mods/common/hotkeys/game.yaml:67`).
- `TileSize: 24,24`.
- `Widget.cs:500-508`.

### DOCS/recipes/DEMO.md

| # | Line | Claim (short) | Verdict | Evidence / what's true now |
|---|---|---|---|---|
| 1 | 5, 23, 35 | The agent builds the demo and launches it with `run-demo.sh` | CONTRADICTED | `.maestro/MAESTRO.md:44` says no worker runs `run-test.sh` or launches the game. |
| 2 | 34, 208, 212 | Smoke-verify each iteration with a temporary `Test.Pass`; "saves dozens of round-trips" | CONTRADICTED | CLAUDE.md requires an explicit go-ahead before "a third rerun of the same test". MAESTRO.md:44 also applies. |
| 3 | 35, 111 | `run-demo.sh` "forces/injects `--no-minimize`" | TRUE (wording) | It passes `--visible --audio` (`run-demo.sh:54`). `--visible` is an alias of `--no-minimize` (`run-test.sh:238`). Audio being switched on is not mentioned. |
| 4 | 42-52 | Folder layout and `map.yaml` rules | STALE (incomplete) | Leaves out the top-level `Rules: rules.yaml` line in `map.yaml`. Without it the demo's rules and Lua are never loaded (`AUTOTEST.md:289`; `demo-shake-profiles/map.yaml:155` has it). Also leaves out the one-`supplyroute`-per-side rule (`AUTOTEST.md` item 6). |
| 5 | 139 | Demos filed under `test-*` make `run-batch.sh --all` report "error: no result file" | STALE | `run-batch.sh:139-182` now finds scenarios with no verdict, excludes them, and lists them under "Excluded from --all". |
| 6 | 182 | `Trigger.AfterDelay(50, …) -- 2s respawn delay` | STALE (25 tps assumption) | 50 ticks × 60 ms = **3.0 s**. |
| 7 | 201, 204 | `test-burn-compare` worked example; templates named `^Variant_VN` | TRUE / STALE | 11 variants confirmed, but they are named `^Burn_V1`…`^Burn_V11` (`rules.yaml:20-580`). The example is itself a demo filed under `test-*`, which breaks this recipe's own rule at `:139`. |
| 8 | 210 | Generator template at `/tmp/burn-compare-gen.py` | STALE | File does not exist. `/tmp` files do not last. |
| 9 | 228, 225-226 | Gotcha 7 (`StartingUnits@*` spawns an SR); gotchas 4-5 (sprite looks) | UNVERIFIABLE | Gotcha 7 needs a scenario run with `-SpawnStartingUnits:`. Gotchas 4-5 need a visual run. |
| 10 | 235 | Debug log at `~/Library/Application Support/OpenRA/Logs/debug.log` | TRUE (macOS only) | Conflicts with the memory rule "logs are global; trust only the run dir". Per-run copies are in the run directory. |

**TRUE items checked: about 22.** Highlights:
- `run-demo.sh:56-59` maps exit 3 to 0 under `set -e` (`:54`).
- The `NO-RESULT` wording is exact (`run-test.sh:1149-1151`).
- `run-batch --all` only scans `test-*`.
- L/R/F/C position shortcuts (`run-test.sh:225-228`).
- RESTART button bound to End (`chrome/ingame-testmode.yaml:14`).
- Edge-pan disabled in windowed mode (`ViewportControllerWidget.cs:200`).
- `Camera.Zoom` / `MinZoom` / `MaxZoom` semantics (`CameraGlobal.cs:42-75`).
- `Media.DisplayMessage`, `FloatingText`, `UserInterface.SetMissionText`, `Trigger.OnTick` and `LoadPassenger` all exist.
- `demo-shake-profiles` uses `DisplayMessage`.
- `Taking screenshot` log line (`Game.cs:773`).
- Both AUTOTEST anchors resolve.
- The MiniYaml removal error text is exact (`MiniYaml.cs:483`).

### DOCS/recipes/BALANCE.md

| # | Line | Claim (short) | Verdict | Evidence / what's true now |
|---|---|---|---|---|
| 1 | 28 | "The script warns if you haven't run it after recent YAML changes" | STALE | `dump-stats.sh` has no staleness check. The warning is in the dashboard (`tools/combat-sim/src/data.ts:110-143`). |
| 2 | 33-39 | `cd tools/combat-sim; node build/index.js …` | STALE (missing step) | `build/` is gitignored (`tools/combat-sim/.gitignore`) and absent. `npm install && npm run build` (`tsc`, `package.json`) must run first. `dump-stats.sh` also needs `engine/bin/OpenRA.Utility.dll`. |
| 3 | 54 | `run-batch.sh test-balance-…` given as routine | CONTRADICTED | CLAUDE.md requires an explicit go-ahead in the current turn for `run-batch.sh`. MAESTRO.md:44 forbids workers from running it at all. |
| 4 | 57 | "Verdicts are deterministic per-seed so re-runs are identical" | STALE (misleading) | Without `--seed` the seed comes from `DateTime.Now` (`run-test.sh:821-826`). Re-runs are only identical if `--seed N` is passed. |
| 5 | 57 | Result line `WINNER=X \| ttk=Ys \| …` | TRUE | `mods/ww3mod/scripts/balance-helpers.lua:65-75`. ttk uses `TestHarness.TicksPerSecond`. |
| 6 | (cross-file) | Balance tests produce verdicts | CONTRADICTED by `run-batch.sh:143-144` | That comment calls nine `test-balance-*` "reporting numbers… rather than passing or failing", and the `--all` filter excludes 9 of the 10 (all except `test-balance-heli-1v1`). That is a bug in `run-batch.sh`: `Test.Pass` is called from the shared `balance-helpers.lua`, which the filter's per-scenario Lua grep never reads. |
| 7 | 83-85 | `UnitFloorPer` at `DumpCompositionPlanCommand.cs:156`, `SupplyTruckFloorPer` at `:162` | TRUE (approx.) | `:156` is exact. `:162` is the explanatory comment; the override itself is at `:170`. |
| 8 | 164 | `TransitionRadius` "let ten weapons share one rule" | STALE | 15 weapons now set it (Atomic, AtomicHighYield, four B61 Mod 12 variants, B83, W76, W88, and five Russian warheads including Tsar Bomba). |
| 9 | 117-143 | 2.74/2.67 psi contours; 20 kt → 15.1 vs 11.9 cells; 6 Mt → 101 vs 124; 437 kt crossover | UNVERIFIABLE | Running `dotnet test --filter NuclearYieldTest` (not run, per the constraints) would check these. |

**TRUE items checked: about 20.** Highlights:
- `dump-stats.sh` path, output and JSON check.
- Dashboard verbs `units/compare/actor/weapon/dps/tier-cost` exist (`index.ts:72-78`); the old `run/duel` verbs are retired.
- `--dump-balance-json` and `--composition-plan` commands exist, and every flag listed is real (`DumpCompositionPlanCommand.cs:97-170`).
- `UnitBuilderBotModule.cs` exists.
- `NuclearYieldTest.EveryNuclearCloudIsOneSpriteOnTheYieldLaw` exists (`:1027`).
- The conventions.md anchor exists (`:1233`).
- `SpeedDecayPercent` is superseded (`ShockwaveDamageWarhead.cs:61`).
- `abrams`, `t90` and `TankRound.Abrams` exist.

### DOCS/recipes/README.md

| # | Line | Claim | Verdict | Evidence |
|---|---|---|---|---|
| 1 | 20 | TELEMETRY "not built yet — first invocation builds" | STALE | See TELEMETRY #1. |
| 2 | 38 | "Mention in `CLAUDE.md` under the trigger table" | STALE | CLAUDE.md has no trigger table. It has one routing row that points back to this README. |

**Trigger table:** all 12 listed recipe files exist, and every recipe file in the directory is listed. Nothing is missing either way.

**CLAUDE.md routing table:** all 24 paths it names exist, including the `tools/*/README.md` files, `run-smoke.sh`, `lint-baseline.txt`, `pipeline/items` and `pipeline/archive`.

**TRUE: about 14** (the existence checks plus the "never call the Skill tool" note, which matches CLAUDE.md).

### DOCS/recipes/CONTEXT.md

| # | Line | Claim | Verdict | Evidence |
|---|---|---|---|---|
| 1 | 17 | Check `WORKSPACE/archive/sessions/active_*.md` for work in flight | STALE | There are no `active_*` files. The directory was last touched 2026-07-22. In-flight work is tracked in `HOTBOARD.md`, `PIPELINE.md` and `.maestro/managers/`. |
| 2 | 13-16 | Orient from `RELEASE_V1.md`, DISCOVERIES and plans | CONTRADICTED | CLAUDE.md's status row routes to `RELEASE_V1.md` + `HOTBOARD.md` + `git log`, and `PIPELINE.md` for the queue. `RELEASE_V1.md:4-11` says it is partly stale and "the live queue is not here". |

**TRUE: 3** (`WORKSPACE/plans/` and `DISCOVERIES.md` exist; the git log command is valid).

### DOCS/recipes/DOCUMENT.md

| # | Line | Claim | Verdict | Evidence |
|---|---|---|---|---|
| 1 | 37 | "Technicians capture neutrals in 20 ticks ≈ 0.8 sim-sec" | STALE / CONTRADICTED | 20 × 60 ms = **1.2 s**. `DOCS/gameplay/capturing.md:16` already says "20 ticks (~1.2 s)". |
| 2 | 58 | Code name "(`tecn`)" | TRUE (with a caveat) | The YAML key is `TECN:` (`infantry.yaml:2472`). Per CLAUDE.md's case-sensitivity rule, a rules override must use `TECN`. |

**TRUE: 5** (`DOCS/gameplay/README.md`, `capturing.md`, `WORKSPACE/ai/` and `supply-route.md` exist).

### DOCS/recipes/FINALIZE.md

| # | Line | Claim | Verdict | Evidence |
|---|---|---|---|---|
| 1 | 15 | Flip items to `[x]`; move shipped items to "Recently completed" | CONTRADICTED | `RELEASE_V1.md:27`: "Items pass → removed entirely… No `[x]` graveyard, no 'Recently completed' section." |
| 2 | 16 | Keep HOTBOARD under 40 lines | TRUE as a rule | HOTBOARD itself says "Cap ~40". It is currently 42 lines. |
| 3 | 21 | Promote `active_*.md` session files | STALE | The mechanism is dead (see CONTEXT #1). |
| 4 | 24 | Update CLAUDE.md with new patterns | CONTRADICTED (softly) | CLAUDE.md §Knowledge bank sends new insights to `WORKSPACE/DISCOVERIES.md` and promotes them in a curation pass. |

**TRUE: 4** (BACKLOG `[x]` convention per `BACKLOG.md:4`; `archive/plans` exists; the PITFALL check; commit-only, no push).

### DOCS/recipes/PLAN.md

**TRUE: 3** (the `WORKSPACE/plans/<YYMMDD>_<topic>.md` naming matches the existing files). Nothing stale.

### DOCS/recipes/PLAYTEST.md

| # | Line | Claim | Verdict | Evidence |
|---|---|---|---|---|
| 1 | 16 | Pull focus from `RELEASE_V1.md` Phase A | STALE | `RELEASE_V1.md:4-7` says the "Currently in: Phase A" pointer "has not been re-derived in months". |

**TRUE: 3** (`make all` / `./make.ps1 all`; `WORKSPACE/playtests/` exists; `git rev-parse`).

### DOCS/recipes/REVIEW.md

| # | Line | Claim | Verdict | Evidence |
|---|---|---|---|---|
| 1 | 15, 19 | "CLAUDE.md → 'Common Pitfalls' section"; "per CLAUDE.md instructions" on over-engineering | STALE | Neither exists in CLAUDE.md. Pitfalls now live in `DOCS/reference/conventions.md`. |
| 2 | 17 | Check for "lowercase actor names" | CONTRADICTED | CLAUDE.md's MiniYaml rule: top-level keys merge case-sensitively, so lowercasing a rules override (`t03:` against `T03:`) breaks it. Lowercase is right only for `map.yaml` actor types. |

**TRUE: 2.**

### DOCS/recipes/TELEMETRY.md

| # | Line | Claim | Verdict | Evidence |
|---|---|---|---|---|
| 1 | 13 | Channel is the "Developer Logging" pending decision; not built | STALE | `RELEASE_V1.md` has no "Developer Logging" entry. A JSONL event channel does exist: `UnitLifecycleLogger` (`engine/OpenRA.Mods.Common/Traits/World/UnitLifecycleLogger.cs`, enabled by `run-test.sh --lifecycle` / `Test.UnitLifecycleLog`) logs spawn, order, idle and death events. There is also `--missile-trace`. `DevLog` / `gameplay.log` were never built. |
| 2 | 44 | 16.67 ticks/s at `Timestep: 60` | TRUE | `mod.yaml:431`. |

**TRUE: 2.**

### DOCS/recipes/TRIAGE.md

| # | Line | Claim | Verdict | Evidence |
|---|---|---|---|---|
| 1 | 16-24 | Route findings to `RELEASE_V1.md` and BACKLOG only | STALE | The roadmap queue is `PIPELINE.md` (CLAUDE.md routing; `RELEASE_V1.md:10`). |

**TRUE: 4** (Phase A/B/C, "Pending decisions", "Deferred" sections and the `[cut]` status all exist in `RELEASE_V1.md:23,48-183`; `bugs/discovered.md` exists).

### Rewrite proposals

Out of scope but worse than anything here: the CLAUDE.md row for autotest scenarios ("CORRECTED 2026-09-04", "This figure has now gone stale twice") is the heaviest accretion in the instruction set.

**1. SCREENSHOT.md:356-369 ("Phase 3 — PARTLY BUILT, not 'sketched'"), replace with:**

> ## Phase 3 — driving the UI from the command file
>
> Besides `screenshot`, the command file accepts `click <widget-id>`, `type <widget-id> [text]`, `hover <actor-name>`, `zone-paint`/`zone-erase <x>,<y>[,<size>]` and `quit` (`engine/OpenRA.Game/TestModeScreenshots.cs`, `PollCommands`). `click` runs the first widget whose `IsVisible()` is true through its own `OnClick`. `type` sets a text field and fires `OnTextEdited`. Each logs `→ dispatched` or a miss. A miss is never thrown, and `NO SUCH VISIBLE WIDGET` covers both "not found" and "found but has no `OnClick`" (`:305-317`, `:227`). Grep for it, check it against the world-load lines (a click sent on a timer can land before the world exists), and treat byte-identical frames as NO-RESULT. Worked drivers: `watch-replay.sh`, `screenshot-infopanel.sh`. These cannot be driven: key events, scrolling, and dropdown items (use `Test.EditorTool` / `Test.OpenIngameInfoPanel`).

**2. SCREENSHOT.md:351-354 (`test-screenshot-smoke`), replace with:**

> - `test-screenshot-smoke` exercises the pipeline: three captures at named beats, then `Test.Skip`. It asserts nothing. You check the run directory yourself to see whether three PNGs landed and whether `screenshots[]` lists them. Run it without `--hidden`, which lists captures it never writes.

**3. DEMO.md:70-75 ("Frame the shot yourself"), replace with:**

> A demo that opens at the wrong zoom on the wrong part of the map shows less than you built. At default zoom on a 128x128 map, a 102-cell blast wave shows about a third of itself. Set the camera in `WorldLoaded` as part of staging, and never leave the viewer a `description.txt` instruction to zoom out by hand.
---

### DOCS/reference/conventions.md (lines 1-939)

Audited at `main @ 2f8b5f6d`. I re-derived the tick rate: `mod.yaml:407` has `DefaultSpeed: default`, and that block's `Timestep: 60` sits at `:431`. That gives 1000/60 = **16.67 tps**. CLAUDE.md's 1500 ticks = 90 s is correct. The doc's tick-rate *conclusions* still hold, but nearly every `mod.yaml` line number it cites has shifted by about 49 lines.

| # | Line | Claim (short) | Verdict | Evidence / what's true now |
|---|---|---|---|---|
| 1 | 39, 41, 43, 53 | `Timestep: 40` at `mod.yaml:394`; default is `:358` + `:382`; the list of eleven runs `:362`…`:402`; "the only defensible citation is `:358` and `:382`" | STALE | `DefaultSpeed` is now `:407`, the default `Timestep: 60` is `:431`, `fastest` 40 is `:443`, and the eleven run `:411`…`:451`. Rule 2 now tells readers to cite the wrong lines. |
| 2 | 45 | `logicInterval` at `Game.cs:994-999` | STALE | Now `Game.cs:1008-1013` |
| 3 | 45 | "Three bases coexist (16.67 real, 16 engine-Lua, 25 harness constant)"; `DateTimeGlobal.cs:31` divides as integers and yields 16 | STALE + CONTRADICTED | `DateTimeGlobal.cs:35-40` says the bug "WAS" and now goes through `TickTime.TicksForSeconds`. `test-helpers.lua:32-33` sets the harness to 1000/60. This contradicts `DOCS/recipes/AUTOTEST.md:512` ("There is now ONE tick base") and this file's own `:1166-1168`. |
| 4 | 45 | `AutotestTickRateTest.cs:138` computes `1000 / DefaultTimestepMs()` | STALE | `:138` is a helper. The test now asserts `1000.0 / 60` at `:188` and `TicksForSeconds` agreement at `:173-183`. |
| 5 | 47 | `DefaultTimestepMs()` at `:83-98`; assert equals 60 at `:125` | STALE | `:118` and `:154` |
| 6 | 56 | In-tree comment at `test-helpers.lua:31-33` says "at 25, 16 or both"; `AssertWithin` recovers the budget as `math.floor(seconds * TicksPerSecond)` | STALE | The comment now says "1145 … one tick short at 25" (`:30-31`). `AssertWithin` goes through `TicksForSeconds` (`:44-45`, `:119`). The "Left for a code owner" note is obsolete. |
| 7 | 31 | `Mobile.cs:826-831`, `world.yaml:98-101`, humvee `Speed` at `vehicles-america.yaml:70` | STALE (facts true) | Now `Mobile.cs:857-860`, `world.yaml:112-114` (lightwheeled `Clear: 70`), `:76` |
| 8 | 75 | `Transform.cs:69`, `Sellable.cs:100` | STALE (minor) | `makeAnimation.Reverse` at `Transform.cs:73`; `Sellable.cs:99` |
| 9 | 105, 107 | `^AR` inherits at `infantry.yaml:1327/1329`; `^AutoTarget` at `defaults.yaml:372-414`, `InitialResupplyBehavior` at `:390` | STALE | Now `:1354/:1355`, `:415`ff and `:433` |
| 10 | 115 | `Actor.cs:696`; `AmmoPool.cs:817-821`; Iskander `ammo-primary == 2` at `vehicles-russia.yaml:1041` | STALE | Now `:711`, `:1322-1325`, `:1096` |
| 11 | 117 | `SupplyProvider` fields at `:90/:94/:98/:109/:113` | STALE | Now `:126/:130/:134/:145/:149` |
| 12 | 133 | `CheckYaml.cs:51` (`TREAT_WARNINGS_AS_ERRORS`); dead keys in `en.ftl` run `:84`–`:129` | STALE (count true) | Now `:57`, and `:111`–`:156`. Still 38 keys. |
| 13 | 148 | LOGISTICSCENTER removal note at `structures.yaml:489-502` | STALE | Now `:596-609` |
| 14 | 160 | `SelectionPriorityModifier@Evacuating` at `aircraft.yaml:172` | STALE | Now `:179` |
| 15 | 180 | `techlevel.infonly` granted at `player.yaml:206-225` | STALE | Now `:1095-1115` |
| 16 | 174-176 | List of faction files | STALE (incomplete) | Omits the base `vehicles.yaml` and the `naval*.yaml` trio |
| 17 | 191 | `^7.62mm` is `Infantry, Unarmored, Helicopter` (`weapons-ballistics.yaml:144`); "ground autocannons and MGs list Helicopter" | STALE (substantive) | Now `ValidTargets: Infantry, Unarmored, Vehicle, AirLight` / `InvalidTargets: Medium, Heavy` (`:176-177`, user ruling 2026-08-22, "AirLight REPLACES Helicopter"). `^12.7mm` is at `:377`, Tunguska AA at `:699`. |
| 18 | 191 | SAMs at `weapons-missiles.yaml:481,529,565,599`; Tunguska 9M311 at `vehicles-russia.yaml:860` | STALE | Now `:499,547,583,617` and `:936` |
| 19 | 201, 203 | `^ShootableMissile` at `defaults.yaml:952`; `CreateEffectWarhead.cs` `:67-96`, `:89-92`, `:107`, `:124-125`, `:155`, `:82` | STALE | Now `:1101`; and `:85-113`, `:107-110`, `:125`, `:142`, `:175`, `:100` |
| 20 | 234, 238 | `^ArtilleryRound` at `:874`; `^Tree` comment at `decoration.yaml:143-145` | STALE | Now `:869`; the comment is now `decoration.yaml:75-77` |
| 21 | 288, 294, 297 | `CreateEffectWarhead.cs:166`; `^HugeExplosionEffects` at `weapons-effects.yaml:596`; Atomic `Warhead@Fireball` at `weapons-superweapons.yaml:65` | STALE | Now `:186`, `:608`, and `Warhead@Fireball` at `:249` |
| 22 | 310 | `Map.cs:1729-1739`; `^Bridge` TargetTypes at `civilian.yaml:303-304` | STALE | `GetTerrainIndex` is at `:1720`; TargetTypes at about `:286` |
| 23 | 361 | `IskanderExplosion` TargetDamage at `weapons-explosions.yaml:521` | STALE | Now `:529` |
| 24 | 375 | `GrenadeLauncher` at `weapons-ballistics.yaml:451` | STALE | Now `:485` |
| 25 | 379 | `Targetable@Helicopter` at `aircraft.yaml:218-219`; `Targetable@Drone` at `:389-390` | STALE + internally inconsistent | Line 186 already corrected this to `:225-227`, but line 379 kept the old cite. Drone is at `:435`. |
| 26 | 385 | `^5.56mm` (present tense) `ValidTargets: Infantry, Vehicle, Helicopter` / `InvalidTargets: Light, Medium, Heavy` at `:83-84`; "`^9mm`, `^7.62mm` and `^12.7mm` name `Helicopter`"; `WeaponInfo.cs:218` | STALE (substantive) | Now `Infantry, Vehicle, AirLight` / `Medium, Heavy` at `:103-104`. `^9mm` is `Infantry, Unarmored`, `^7.62mm` uses `AirLight`, and only `^12.7mm` names `Helicopter`. `IsValidTarget` is at `:223`. The paragraph should be framed as history. |
| 27 | 417 | `^CivBuilding` removals at `civilian.yaml:6-10` | STALE | Now `:12-16` |
| 28 | 427 | Kevlar at `infantry.yaml:174-175`; None at `:34-35` | STALE (minor) | Now `:175-176` and `:36-37` |
| 29 | 453, 455, 460, 466, 581 | `ArmorDirectionPercent` at `:140-152`; abrams at `vehicles-america.yaml:499-500`; RPG at `:535`; TopAttack at `880, 974, 1007, 1097`; `Penetration = 1` at `DamageWarhead.cs:24` | STALE | Now `:142`, `:507-508`, `RPG:` at `:514`, `878, 971, 1004, 1094`, and `:25`. Still five TopAttack sites. |
| 30 | 505-507 | "Of the sixteen vehicles that author one, only abrams and t90 … thirteen carry the flat value" | Arithmetic error | 2 + 13 = 15. The YAML has 15 concrete `Distribution:` lines plus the `^Vehicle` fallback. |
| 31 | 518, 532 | Lead branch at `Armament.cs:619/:636`; "Bullets do not lead" at `weapons-ballistics.yaml:711-712` | STALE | Now `:680/:697` and `:709-710` |
| 32 | 563 | `^TreeIndestructible` at `decoration.yaml:135-137` | STALE | Now `:152-154` |
| 33 | 567 | "`grep -n husk …weapons-superweapons.yaml` returns nothing"; "INERT" at `:491`, `:1166` | STALE | The grep now returns 4 hits (`:275`, `:285`, `:996`, …), in a different, correct context. "INERT against every shipped tree" is at `:334`, `:534`, `:1229`. |
| 34 | 573, 581 | `Map.cs:180` (`Weapons`); `WeaponInfo.LoadWarheads` at `:200-206` | STALE | Now `:182` and `:208-210` |
| 35 | 626 | `Map.cs:540-547`, `:698-704`, `:988` | STALE | Now `:605-609`, `:763-766`, `:1056` |
| 36 | 661 | `MapGrid` at `mod.yaml:328-330` (the text says it was "corrected" once already) | STALE again | Now `:377-379` |
| 37 | 663 | `WorldRenderer.cs:749` | STALE | `ScreenPosition` is at `:902` |
| 38 | 715, 730-731 | LOGISTICSCENTER footprint at `structures.yaml:435-436` and offsets at `:413`; SUPPLYROUTE footprint at `:285-286` | STALE | Now `:542`, `:520`, `:378` |
| 39 | 754 | Clump offsets at `decoration.yaml:580, :596, :612, :628` for TC01–TC05 | STALE (incomplete) | TC05's `Offset: 0,512,0` is at `:644`, which is missing from the list |
| 40 | 775 | `Map.cs:176`, `:364` | STALE | Now `:178`, `:392` |
| 41 | 806 | "the other thirty-eight in `Lint/`" | STALE (minor) | 40 `.cs` files in `Lint/` |
| 42 | 817 | `make test` at `Makefile:252-254` | STALE | Now `Makefile:306-308` |
| 43 | 852 | `CheckTraitPrerequisites.cs:42` | STALE | `TraitsInConstructOrder` is at `:36` |
| 44 | 867 | "356 of its 548 lines" | STALE | 356 sprite lines out of **574** |
| 45 | 867, 884, 891 | `mod.yaml:24` (`conquer.mix`), `:90` (System), `:107` (Unknown), `:93-106` (comment) | STALE | Now `:22`, `:89`, `:111`, `:92-110` |
| 46 | 875 | "297 scenario directories at `main @ 95bdffb2`" | STALE (dated count) | 368 now. CLAUDE.md's "320 at e82fe534" is also stale. |
| 47 | 901 | "Same note … at `AUTOTEST.md:74`" | STALE | `AUTOTEST.md:72-76` is now about `expected-status` |
| 48 | CLAUDE.md → 871 | CLAUDE.md cites `§"Scenarios are NOT maps to the tooling"` | CONTRADICTED (anchor does not resolve) | The heading reads "Scenarios are NOT maps to **SOME** tooling — but the Windows merge gate DOES lint every one of them". The other two CLAUDE.md anchors resolve: "The override isn't taking effect" is a prefix of the `:583` heading, and "A change believed made, documented as made, and inert" is at `:1114`. |
| 49 | (1170; checked because CLAUDE.md relies on it) | "25 tps still live at ten other sites"; census at `5eb27755` | STALE | About **7** sites are still wrong today: `SmartMove.cs:25`, `SupportPower.cs:24`, `scenario.lua:31`, `test-missile-hellfire-probe.lua:168`, `tools/autotest/parse-floor-denominator.py:186` (new, not in the census), and `test-stance-anchor-move.lua:46` / `test-stance-redirect-midadjust.lua:50`. The last two were classed "fine, harness base", but the harness moved to 16.667 on 2026-09-21, so they are now wrong. Fixed since the census: `EvacDriveOffMath`, `TournamentConfig`. The `test-autotarget-preempt-air.lua:140` text is gone. `SupplyRouteContestation.cs:42` cites `mod.yaml:381`, which should be `:431`. |
| 50 | 182 | Overriding `ar.america` from map rules throws a duplicate-key error | UNVERIFIABLE | Needs a single-map `--check-yaml` on a scenario that does this |
| 51 | 417 | `mi28` fails and `MI28` resolves under `--resolved-rules` | UNVERIFIABLE | `./utility.sh --resolved-rules mi28` and `… MI28` |
| 52 | 575 | Re-opening a `Projectile:` sub-block merges (the doc itself says this is unreconciled with warhead replacement) | UNVERIFIABLE | `--resolved-rules` / `--dump-balance-json` on a test override |
| 53 | 607 | A completed YAML sweep is "~111,000 lines" | UNVERIFIABLE | Line count of a full `make test` capture |
| 54 | 663 | Sprite squash factor of 0.566 | UNVERIFIABLE | `OpenRA.Utility --png` export plus the fit |
| 55 | 867 | Baseline was recorded without RA content | UNVERIFIABLE | `--check-missing-sprites` on a machine that has the content installed |

**TRUE items checked: about 80.** Highlights:
- **Angles:** WAngle counterclockwise table and the `AngleGlobal.cs:23-37` names; `FieldLoader.ParseWAngle` accepts integers only; `WAngle.Facing = Angle/4` (`:67`); the `ArcTan` `int` overflow at `WAngle.cs:166` is unchanged.
- **Animation:** `Animation.Tick(40)` at `:229-232`.
- **YAML merging:** the MiniYaml precedence mechanics (`:449-490`, `:476-487`, `:538`, `:410`, `:444`, `:497`); the case-sensitive merge plus `Ruleset.cs:125-127/:185` lowercasing; `Exts.cs` duplicate throw at about `:488-491`.
- **Warheads and targeting:** `Warhead.cs:30/36/45/57` defaults; `^Tree`'s inheritors; `Targetable@Airborne` at `aircraft.yaml:60-62`; all 10 `Targetable@Armor` declarations; the `Versus` census holds (42 tables, union `Concrete, Light, Medium, Heavy, None, Wood`, no Kevlar/Unarmored/Indestructable); `Armor.cs:62/:73` key-set check; `WarheadIsHarmless` at `:895`; `Util.ApplyPercentageModifiers` at `:238-246`; `BurstWaitMultiplier` → `Armament.cs:829`.
- **Lint and conditions:** `CheckConditions` error/warning at `:71/:75`; `VariableExpression` whitespace throws at `:532/:538`.
- **Buildings and occupancy:** footprint enum and builders (`Building.cs:20-26`, `:156/180/192/201`, `:342/345/350/370`); `Locomotor.cs:373/:569`; `SubCell` enum at `:335`; proximity test is strict `<` (`ActorMap.cs:143`).
- **Autotest gates:** `CheckYaml` line cites (`:66/74/98/101/106/129/136/141/151`); `MapCache.cs:212` defaults to `System`; `utility.sh:7/32/53/54`; `engine/utility.sh` is mode 100644; the `evacuating` consumer table (all five readers) and `SelectableExts.cs:35` as the only `ISelectionPriorityModifier` reader.

### Rewrite proposals

**1. Lines 45 and 56 (three tick bases, and the "UPDATED … history" paragraph).** Replace both with:

> Every conversion in the tree divides *into* 1000 and now agrees. The engine's Lua `DateTime.Seconds` goes through `TickTime.TicksForSeconds`, which multiplies before dividing, so `Seconds(60)` is exactly 1000 ticks (`DateTimeGlobal.cs:35-40`). The harness derives its rate from `TestHarness.TimestepMs = 60` (`test-helpers.lua:32-33`), and `TestHarness.TicksForSeconds` (`:44-45`) adds a 1e-9 epsilon so every integer tick budget round-trips exactly. `AutotestTickRateTest` (`:154`, `:173-188`) fails `dotnet test` if the default moves off 60 ms, or if a truncating `1000 / Timestep` or a hardcoded 25 returns. **Rule:** budget in ticks, convert once through `TicksForSeconds`, and never materialise an integer ticks-per-second value.

**2. Lines 871-875 (Scenarios section).** Rename the heading to "Scenarios are NOT maps to SOME tooling" so CLAUDE.md's anchor resolves, or change CLAUDE.md to match. Replace the opening with:

> `.\make.ps1 test` lints every autotest scenario as well as the shipped maps. Each gets a `Testing map:` line, and the lint passes iterate the scenario's whole merged ruleset, not only the actors it places. So an error in a scenario's `rules.yaml` reaches the merge gate even for an actor the scenario never spawns. The total should equal the shipped-map directories plus the scenario directories. Recount both rather than trusting any figure written here: `ls -d tools/autotest/scenarios/*/ | wc -l`. **Assume the gate reaches your scenario.**

**3. Line 567 ("The live doc defect this section used to flag is FIXED").** Replace with:

> `weapons-superweapons.yaml` now states this at the attacker: each tree-fire band is marked "INERT against every shipped tree" (`:334`, `:534`, `:1229`). The warheads still fire on every detonation and are still multiplied by zero at the victim. When a comment there claims an effect on trees, check it against `^TreeIndestructible` (`decoration.yaml:152-154`) before believing it.
---

### DOCS/reference/conventions.md (lines 940-1859)

Checked read-only at `main @ 2f8b5f6d`.

**Tick rate.** The default speed is `Timestep: 60`, which is 16.67 ticks per second. `DefaultSpeed: default` is at `mod.yaml:407` and the matching `Timestep: 60` is at `:431`. The Lua harness now derives its rate from that: `test-helpers.lua:32-33` sets `TimestepMs = 60` and `TicksPerSecond = 1000/60`. So CLAUDE.md is right that 1500 ticks is 90 s and that "25 tps" is wrong.

Two problems run through the whole range:
- **Line-number drift is everywhere.** About half of the citations I checked no longer point at what they claim. The worst files are `TestGlobal.cs` (off by roughly 400–700 lines), `Cargo.cs` (~170), `structures.yaml` (~100), `AutoTarget.cs` (~60–150), `Actor.cs` and `World.cs`.
- **Five claims are now false in substance, not just mis-cited.** The `RUNTIME=mono` lane is gone (#15). Inert-instances 4 and 5 have since been fixed (#10). The heal pulse is now 10, not 5 (#24). Line 1307 says CLAUDE.md lacks `check` (#16). The 25-tps census is out of date (#7).

| # | Line | Claim (short) | Verdict | Evidence / what's true now |
|---|---|---|---|---|
| 1 | 982 | The server dropped the client at `Server.cs:617` | STALE (cite) | `:615-617` is the `ClientJoined` call where the throw starts. The "Dropping connection" log line is at `Server.cs:748-750`. |
| 2 | 1006 | `run-test.sh:17` calls `--hidden` the unattended/tournament profile | STALE (cite) | The flag is at `:17`; the "unattended/tournament profile" wording is at `:20`. |
| 3 | 1008 | `--hidden` is wrong for frame captures | CONTRADICTED (open) | CLAUDE.md:10 says "Prefer `--hidden`" with no screenshot caveat. AUTOTEST.md:983-985 agrees with conventions. CLAUDE.md is the outlier. Whether `--hidden` still writes no PNG is UNVERIFIABLE without one `run-test.sh --hidden` capture run plus `ls` of the run directory. |
| 4 | 1012 | `run-test.sh:924` sets `Launch.Map` | STALE | Now at `run-test.sh:936`. |
| 5 | 1024 | Settings erase at `Settings.cs:436-438` | STALE | Now `:441-443`. `:421` (`Save`) and `:408-412` (command-line override) are still correct. |
| 6 | 1033 | "Seven autotest scenarios force `PowersSandboxCheckboxEnabled: true`" | STALE | 16 scenario `rules.yaml` files set it now (`git grep -l` gives 18 files, including two Lua files and one `map.yaml`). `world.yaml:880` says 8 and `player.yaml:1579` says "ALL EIGHT". All three counts are stale. |
| 7 | 1170 | 25-tps census as of `5eb27755` (6 wrong, 3 "fine", plus Map.cs) | STALE | **Fixed since:** `EvacDriveOffMath.cs:32` (now says 30 s) and `TournamentConfig.cs:100` (now `TicksForSeconds`). **Still wrong:** `SmartMove.cs:25`, `SupportPower.cs:24`, `scenario.lua:31`, `test-missile-hellfire-probe.lua:168` (BurstWait 1000 is 60 s, not 40). **No longer "fine":** `test-stance-redirect-midadjust.lua:50` and `test-stance-anchor-move.lua:46` say "Deadlines (25 ticks/sec)", but the harness moved to 16.67 on 2026-09-21. `test-autotarget-preempt-air.lua:140` no longer exists; `:76` still says "TicksPerSecond is 25". `Map.cs:265` is now `Map.cs:283`. `infantry.yaml:142` is now `:143`. **Not in the census:** `tools/autotest/parse-floor-denominator.py:186` ("@ 25 ticks/s"). That one may be correct for tournaments run at `GameSpeed: fastest`; checking it means reading which configs it parses. **CLAUDE.md's "ten other sites" no longer matches anything.** The census itself has ten entries but only six were ever "wrong". Today 6 are live-wrong (4 code/Lua + 2 stance scenarios), plus Map.cs as a non-load-bearing seventh. |
| 8 | 1164 | `Timestep: 60` is at `mod.yaml:382` | STALE | `mod.yaml:431` (DefaultSpeed at `:407`). The same file gets it right at line 1674. |
| 9 | 1141 | Instance 1: `defaults.yaml:13` / `:14-22`, `WithHealFlash.cs:22-31` | STALE (cites) | `WithHealFlash:` is at `defaults.yaml:21`, with `Count: 6 / Interval: 1` at `:46-47` (now `Brightness: 1.4`, no Alpha). C# defaults are at `WithHealFlash.cs:25/:39/:42`. |
| 10 | 1143-1147 | Instances 3–6 cites; instances 4 and 5 described in the present tense | STALE | **Instance 3:** Tunguska `firing-primary` is now `vehicles-russia.yaml:938`, and the `GrantConditionOnPreparingAttack@1` lines are `:907-909`. **Instance 4:** `12.7mm.Hind` comment is at `weapons-ballistics.yaml:403`, `.AA` at `:426`. It is **no longer inert**: airborne helicopters now advertise `AirLight`, not `Light` (`aircraft-america.yaml:43-45`). **Instance 5:** `^5.56mm` is now `ValidTargets: Infantry, Vehicle, AirLight / InvalidTargets: Medium, Heavy` (`:103-104`). The "Helicopter + InvalidTargets Light" defect is fixed. `WeaponInfo.cs:218` is now `:223`. **Instance 6:** `TraitsInterfaces.cs:621-622` is now `:674-675`, and `AmmoPool.cs:155` is now `:173`. |
| 11 | 1149-1160 | Instances 7–14 cites | STALE (partial) | Now: `DamageWarhead.cs:25`, `:128-133`; MSLO `2000` at `structures-defenses.yaml:1111`; `Explodes.cs:132`; Health clamp at `Health.cs:209` (`:258` still correct); `ResetBurst` at `Armament.cs:837`; `BurstRandomize` desc at `WeaponInfo.cs:121`; `CarrierMaster` fields at `:39/:41/:43` (not `:22`); `CarrierSlave.MaxDistance` at `:149`; `HealerAutoTarget.cs:52-54` now `:55`. Still correct: `WeaponInfo.cs:51` and `:307`, `TargetDamageWarhead.cs:31`, `DroneTaskingMath.cs:133`, `RepairsUnits.cs:22`, `Repairable.cs:32`, `vehicles.yaml:64`, `Resupply.cs:616`, `SupportPower.cs:25`. |
| 12 | 1292 | The `[Sync]` type test is at `Actor.cs:206` | STALE | Now `Actor.cs:237`. |
| 13 | 1301 | `Makefile:226` is `check: worldactor-gate engine` | TRUE | (Listed only because it sits next to #14–16.) |
| 14 | 1303 / 1349 | `OpenRA.Test` has ActiveCfg but no Build.0 at sln `:60-61`; WindowsLauncher has no Release Build.0 at `:66-68`; 10 projects, 11 csproj | TRUE | — |
| 15 | 1324 | The `RUNTIME=mono` path "is still reachable"; `Directory.Build.props:61-63` drops Roslynator under Mono; `netstandard2.1` at `:22-23`; four `System.*` refs are load-bearing | STALE | `Makefile:82-87` now raises `$(error RUNTIME=mono is no longer supported…)`. `Directory.Build.props` has no Mono or netstandard condition: `:22` is `net6.0` only and `:59-63` are unconditional package refs. `System.Collections.Immutable` is no longer referenced. The whole bullet should be deleted or reduced to one line of history. |
| 16 | 1307 | "`check` is not in CLAUDE.md's Build & Run block" | CONTRADICTED | CLAUDE.md now gives `./make.ps1 check` a long block ("THE ONLY COMMAND HERE THAT SEES AN RCS-CLASS ERROR"). The RED-on-main counts (28 signatures at `c47c1379`) are UNVERIFIABLE; one `.\make.ps1 check 2>&1 \| Out-String -Width 500` would settle them. |
| 17 | 1319 | "No lint WARNING can fail `make test` … structural … invisible at every step" | CONTRADICTED (same file) | `CheckYaml.cs:57` reads `TREAT_WARNINGS_AS_ERRORS`; at `:89/:162/:180` that variable routes warnings to `EmitError`. The same file's line 133 and `architecture.md:1861` both say this. The claim holds only because nothing sets the variable. `EmitError` at `:33` and `EmitWarning` at `:40` are correct; `Judge` is at `:124`. |
| 18 | 1326 | `mod.yaml:107` registers the scenarios as `Unknown` | STALE | Now `mod.yaml:111`. `MapCache.cs:194/:212` and `MapPreview.cs:35` are correct. |
| 19 | 1527 | Build loop at `Makefile:183` (mono) and `:186`; `clean` still uses `find -exec` at `:192/:194` | STALE | There is one `set -e` loop at `Makefile:199`, and `clean` now uses a `set -e` loop at `:204`. No `find -exec` remains. `launch-game.cmd:5` is correct. |
| 20 | 1529 | `Makefile:163` (`check-dotnet-sdk`); `make.ps1:241` (`CheckForDotnetSdk`), called at `:406` | STALE | `Makefile:182`; `make.ps1:435`, called at `:606`. |
| 21 | 1545 | `utility.sh:62` passes `"${LAUNCH_MOD}"` | STALE | Now `utility.sh:54`. `utility.cmd:53` and `Program.cs:99` are correct. The `.cmd` half is UNVERIFIABLE here; `.\utility.cmd --build-fingerprint` on Windows would settle it. |
| 22 | 1591-1597 | `Map.cs:1994-1995`, `TerrainLighting.cs:207`, `Viewport.cs:363-366` / `:356-358`, `ScreenShaker.cs:409` / `:421-424`, `Game.cs:879` | STALE | Now: `Map.cs:2012`; `Viewport.cs:369`, with `ScrollPx` at `:367`; `ScreenShaker.cs:448/:485-488`; `Game.cs:886`. `MapGrid.cs:113/:129` and `PerfHistory.cs:55` are correct. |
| 23 | 1605-1622 | `HeliEmergencyLanding.cs:22`, `structures.yaml:74-80`, `PathFinder.cs:106-113`, `Mobile.cs:321`, `world.yaml:32/47/64/80`, `decoration.yaml:535/548`, the four `+` footprints, "147 Footprint lines", `Mobile.cs:937-948`, `:1030` | STALE (cites) | Content is still true (4 `foot*` SharesCell, 2 tank traps, 4 `+` actors), but the lines moved. `SharesCell: true` is at `world.yaml:45/60/77/93`. `ToSubCell` assignment is `Mobile.cs:346`. `BlocksDiagonalSqueeze` is at `ingame/decoration.yaml:807/820`. Footprints: SR `structures.yaml:378`, LC `:542`, AFLD `:858`. There are now 149 Footprint lines, not 147. `OnBecomingIdle` is at `Mobile.cs:1012`. `PassClasses: tree` is at `decoration.yaml:118` (was `:13`). |
| 24 | 1648 | Heal warhead `DamagePercent: -5` (`weapons-other.yaml:349`); `BurstWait: 50` at `:342`; `SwitchMargin` at `HealerAutoTarget.cs:297-298` | STALE, and the margin is now equal to the pulse | The warhead is now `DamagePercent: -10` (`weapons-other.yaml:364`, "was -5"), with `BurstWait: 50` at `:351`. `SwitchMargin: 10` (`infantry.yaml:2341`) is now **equal to** the pulse, not above it. The PITFALL at `infantry.yaml:2335-2336` still says "DamagePercent is 5". `SwitchMargin` is applied at `HealerAutoTarget.cs:314`. This may be a live bug and should go to `WORKSPACE/bugs/discovered.md`. |
| 25 | 1647 vs 1650 vs 1722 | `ScanForTarget` at `:1187-1215` (1647) and at `:1052-1100`, rearm `:1087` (1650/1722) | STALE + internally inconsistent | The two bullets disagree with each other and both are wrong. Now `ScanForTarget` is at `AutoTarget.cs:1198/:1206`, rearm at `:1250`, `AmbushTickIdle` at `:742` with `scannedThisTick` at `:754`. Also: `InitialStance` choice `:497`→`:539`; `allowMove` `:635`→`:698/:737/:1141`; `EstimatePercentDamage` cap `:1712`→`:1780`. Still correct: overkill `:1546`, break-off `:1555-1556`, `:244`, `:221`, `:212`, `:68`, `:65`. |
| 26 | 1647, 1721 | `^CamoSoldier` scan intervals at `infantry.yaml:304-305` / `:289-290`; ScanRadius at `:310` and `:2423` | STALE | `^CamoSoldier` opens at `:306`; Min/Max interval are at `:312-313`; ScanRadius at `:311` and `:2492`. |
| 27 | 1689 | `SelectionPriorityModifier@OutOfAmmo` fires on `weapon-primary && !ammo-primary` (`infantry.yaml:245-248`), same as `defaults.yaml:775` | STALE | Now at `infantry.yaml:269-272`, with a three-pool predicate. The medic conclusion still holds. `defaults.yaml:775` no longer matches. |
| 28 | 1607 | `BleedOut` at `infantry.yaml:1098` | STALE | `:1130` (`Delay: 50`, versus the vehicle's 5). |
| 29 | 1634, 1677, 1726 | `TestGlobal.cs:1180-1185`, `:1193-1199`, `:568`, `:324`, `:900`, `:174` | STALE | Now `IsDetectedBy` `:1882`, `IsMouseTargetable` `:1895`, `ClickOrder` `~:745`, `SetZoom` `:175`. `CargoCapacity:900` and `GetTickTimeMs:1553-1556` are correct. |
| 30 | 1642, 1701-1702, 1718 | `Cargo.cs:248,255,519,371-396,412-419,593-595,641,707-711,824-825,855` | STALE | `Cargo.cs` grew about 170 lines: `"Unload"` at `:409+`, `CancelActivity` at `:585`, the `DamageState.Dead` guard at `:779-782`. Every Cargo cite in this range needs re-deriving. |
| 31 | 1701 | Health order: clamp `:189`, Damaged `:199-201`, Killed `:216-220` | STALE | Clamp `:209`, Damaged `:274-276`, Killed `:293-295`. `Health.cs:221` (line 1683) is correct. |
| 32 | 1652, 1649, 1670, 1674 | `World.cs:522`, `:569`, `:499-502`, `:218` | STALE | Now `actors.Values` `:505/:544`, `SharedRandom.Last` `:607`, ITick walk `:508`, `ReplayTimestep` `:220`. `World.cs:41/:43/:50/:372/:556` are correct. |
| 33 | 1661-1666, 1727 | `Actor.cs:430-431`, `:465`, `:636`, `:104`, `:701`, `:469`; `Aircraft.cs:911/:263`; `Activity.cs:132/:220-226/:286-304`; `TraitDictionary.cs:158` | STALE | Now `Actor.cs:431`, `:467`, `:687`, `:135`, `:754`, `:505`; `Aircraft.cs:946/:289`; `Activity.cs:146/:234/:311`; `TraitDictionary.cs:162`. `Actor.cs:75/83/98/345`, `Activity.cs:177/:214`, `AIUtils.cs:45` and `TraitDictionary.cs:84/:175` are correct. |
| 34 | 1651 | `Log.cs:59-68` and `:187-192` | STALE | `IsBackground` at `:64/:68`; `Dispose` at `:204`. |
| 35 | 1675 | `Game.cs:735` (`RunAfterTick`), drain `:822-824`; "six hits" of `AssertUnsynced`, in `TestGlobalSyncTest.cs`; 76 writers in 14 files | STALE | `RunAfterTick` `:744`; `PerformDelayedActions` is the first line of `LogicTick` at `:838-840`. The one-call-site claim is TRUE (`World.cs:170`). The engine hit count is now 8, and the test file is `ScriptGlobalSyncTest.cs`. A writer grep now returns ~81 sites in 28 files, so re-derive. |
| 36 | 1671 | LC chain `structures.yaml:392` → `:69` → `:10` → `:169-176`, `:394-396`; `RepairsUnits :484`, `SupplyProvider :503`, `ResupplyDock :435-436` | STALE | The chain is true but every line moved: `LOGISTICSCENTER:499`, `^Building:88`, `^BasicBuilding:9`, `^NeutralOrOccupiedCapturable:242-250`, `OwnerLostAction:501-503`, `RepairsUnits:591`, `SupplyProvider:610`, `ResupplyDock:550`. `DeliverSupply.cs:104` is correct. |
| 37 | 1674 | Countdown at `TimeLimitManager.cs:131` | STALE | Now `:163-166`. `:57`, `:137`, `:139-142` and `:152` are correct. |
| 38 | 1694 | `Enter.cs:73` sets `RetryIfDestinationBlocked = true` | STALE | Now `Enter.cs:99`. `MoveResult` is still never assigned (TRUE). |
| 39 | 1847-1848 | "`make.ps1 check` has two build stages" | STALE / inconsistent with line 1311 | `make.ps1:601-623` first runs `engine\make.cmd check`, then `Check-Command` builds WW3MOD.sln (`:359`) and OpenRA.Test (`:373`). That is three strata, as line 1311 already says for the Makefile. |
| 40 | 954 | "~20 latent duplicate blocks" | UNVERIFIABLE | Running `DuplicateChildKeyTest` with a census print, or `dotnet test --filter DuplicateChildKey`, would settle it. The test file exists. |
| 41 | 1307, 1311 | Error counts (28 signatures; 33→39 across strata) | UNVERIFIABLE | Needs one `.\make.ps1 check` run. |
| 42 | 1004-1008 | `--hidden` writes no PNG while `result.json` lists one | UNVERIFIABLE | Needs one `--hidden` capture run plus `ls` of the run directory. |

**TRUE items checked: ~80.** Highlights:
- The MiniYaml merge mechanics: `MiniYaml.cs:427/438/443-444/520-539/587`, `Exts.cs:454`, the duplicate-parent and removal throws.
- The `MapLayers` TraitLocation and `ILobbyOptions` cites.
- `TestModeSpeedMultiplier` is `IWorldLoaded` and uses integer division (`--speed 8` on 60 ms gives 7 ms).
- `ConquestVictoryConditions:62/73/93`; `PlayerResources.DefaultCash = 20000` with `player.yaml:1047` commented out.
- `GrantConditionOnLobbyOption.cs:48-50`; `TimeLimitManager` uses `TickTime.TicksForMinutes`.
- `Game.cs:805/824/826/834/886` and `TerrainLighting.cs:318`.
- The crash hunt really is scoped inside the no-result branch, now at `run-test.sh:1107-1158`.
- `AutotestTickRateTest` sweeps 0..600 s; `test-helpers.lua` is at 16.667.
- `OpenRA.sln` ActiveCfg/Build.0 state, 10 projects and 11 csproj; `Makefile:226/230/238/246`; `make.ps1:359/373`.
- `.editorconfig` RCS1058 at `:1098`/`:1396`; `SA1611` none at `:666`.
- `MapCache.cs:194/212` and `MapPreview.cs:35`.
- `MoveResult` never assigned; `ForceMove`/`Move` split at `Mobile.cs:1086`; ScanRadius only on 2 infantry lines plus 3 maps.
- river-zeta has exactly 4544 actors; `SharesCell` is on exactly 4 locomotors; exactly 4 `+` footprints.
- `Health.DamageState` thresholds and the `defaults.yaml:292-300` condition cites.
- `Bullet.cs:170/213/350`; `OverkillClaim.cs:96`; `Armament.cs:656`.

### Rewrite proposals

**1. Lines 1166-1168 (the "THERE USED TO BE A THIRD TICK BASE / THERE IS NOW ONE BASE" pair)**, replace with:

> **There is one tick base: 16.667 ticks/s, everywhere.** The engine converts with `TickTime.TicksForSeconds` / `TicksForMinutes`, which multiply before dividing, so `DateTime.Seconds(60)` is 1000 ticks. `TimeLimitManager` uses the same conversion, so a 90-minute limit runs 90 minutes. The Lua harness derives `TestHarness.TicksPerSecond` from `TestHarness.TimestepMs = 60` (`test-helpers.lua:32-33`) and converts through `TestHarness.TicksForSeconds`, which mirrors the engine. `AssertWithin(n)` and `DateTime.Seconds(n)` are therefore the same tick count. `AutotestTickRateTest` asserts that agreement for every integer second from 0 to 600, and fails on a hardcoded 25 or 16. **Do not "correct" either constant to 16 or 25.** Any seconds-literal deadline written before 2026-09-21 was authored against 25 tps, so it now allows a third less time than its author measured. Treat it as un-audited.

**2. Line 1170 (the census)**, replace with a dated census that states its method:

> **Census, `2f8b5f6d`, from `git grep -nE "25 ?tps|25 ticks ?/ ?s|25 ticks/sec"`. Recall is unverified: a silent division contains no phrase to grep for.** Seconds figures that are still wrong: `SmartMove.cs:25` (~3 s; really 4.5 s), `SupportPower.cs:24` ("Parsed at 25 ticks/second"), `scripts/scenario.lua:31` (375 ticks is 22.5 s, not 15), `test-missile-hellfire-probe.lua:168` (BurstWait 1000 is 60 s, not 40), and `test-stance-redirect-midadjust.lua:50` and `test-stance-anchor-move.lua:46` ("Deadlines (25 ticks/sec)"; the harness is 16.667). `Map.cs:283` is a throughput note and does not matter. The other hits are comments warning *against* 25 tps.

**3. Line 1624 (the "SUPERSEDED 2026-09-05 … REJECTED OPTION" bullet)**, replace with:

> **Keep `LOGISTICSCENTER`'s dock cell transit-only (`+`). Do not make it `=`.** The 2x2 `++ =+` footprint declares `ResupplyDock`, which names the one stoppable `=` cell, and `Resupply` vacates serviced vehicles explicitly. Parking on the dock is unsafe because the docking path has no queue and no reservation. `MoveOnto` waits rather than sidestepping (`MoveOnto.cs:46-48`), and `CanEnterCell` defaults to `BlockedByActor.All`. Both service errands need exact coincidence with the dock: `AmmoPool`'s `isCloseEnough` is `WDist.Zero`, and `Repairable`'s is 512. So one vehicle resting there stalls every vehicle after it. `TransitOnlyServiceHostTest.ServiceHostDockCellsStayTransitOnlyBecauseTheVacateDependsOnIt` pins the `+` cell. If this is ever revisited, vacate on reservation (`Reservable` + `DockHost`), not on idleness.