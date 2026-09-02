# Autotest suite audit — how much of this suite is still meaningful?

**Ref: `main @ 97c2fe78`** (worktree `wt/autotest-audit`, clean, 0 behind origin).
**Read-only.** Nothing was launched: no `run-test.sh`, no `run-batch.sh`, no screenshot script,
no `--check-yaml`, no `make test`. Every claim below is from reading files at that ref, and each
one names what was read. Where a claim needs a run to settle, it says so and says what run.

**The central question was: can staleness be determined WITHOUT running the suite?**
Partly. Symbol-level staleness — dangling actors, conditions, traits, Lua APIs — is fully
decidable statically, and **the answer is that there is none** (§A). What is NOT decidable
statically is whether an assertion still discriminates; but a large sub-class of that question
*is* decidable, because a scenario whose only failure path is a staging fault cannot discriminate
anything by construction (§C). That class is 13 scenarios and it is the main finding.

---

## Inventory

259 scenario folders: 210 `test-*`, 17 `demo-*`, 31 `tournament-*`, 1 `wip-transport-delivers`.

The one number worth restating because two in-tree comments disagree with it:
**64 folders contain no verdict call at all** — 21 `test-*`, 12 `demo-*`, all 31 `tournament-*`.
`run-batch.sh --all` globs `test-*` only, so **189 of 210 `test-*` scenarios enter a batch**.
Method: the same predicate `run-batch.sh:154-156` uses — strip `--` comments, then
`grep -E "Test\.(Pass|Fail|Skip)|Assert(Within|After)"`.

---

## §A — Statically-detectable staleness

**Nothing wrong here.** No scenario references a symbol the codebase no longer has, across every
symbol class I could check. This is a verified negative, not an absence of effort — see "what
would have made this fail" below.

Checked, with ground truth built from the tree at this ref:

| Symbol class | Extraction site | Ground truth | Dangling |
|---|---|---|---|
| Actor types | `Actor.Create`, `GetActorsByType`, `ClickProductionIcon`, `QueueProduction`, `PauseProduction`, `GetQueueRemainingTime`, `GetSelectedCountOfType`, `SetUnitTypeFireStance`, and `map.yaml` `Actors:` values | 894 top-level keys in `mods/ww3mod/rules/**` | **0** |
| Condition tokens | `.GrantCondition("…")`, `.RevokeCondition`, `HasCondition` | every `*Condition*:` value in `mods/**/*.yaml` | **0** |
| `Inherits:` targets | every `Inherits`/`Inherits@x` in scenario `rules.yaml` | mod rules top-level keys **+ scenario-local definitions** | **0** |
| Trait names | every one-tab `TraitName:` / `-TraitName:` / `TraitName@x:` in scenario `rules.yaml` | 2512 `class <Name>Info` in `engine/**` | **0** |
| Lua API | `Global.Member` where `Global` is a known `[ScriptGlobal]` or a `mods/ww3mod/scripts/*.lua` helper table | 22 script globals + `ScriptPropertyGroup` members | **0** |

**Four apparent hits were run down and all four are false.** `e1ghost`, `e1hold`, `e1hunt`,
`e1watcher` are not missing actors — each is **defined in the scenario's own `rules.yaml`**
(`test-detect-no-invisibility/rules.yaml:21`, `test-unit-indicators/rules.yaml:19,28,36`,
`test-unit-indicators-before/rules.yaml:46,55,61`, `test-visual-gauge-truth`). A fifth,
`Test.KeepRenderPlayer` at `test-unscouted-building-hidden/test-unscouted-building-hidden.lua:133`,
is inside a *failure-message string literal*, not a call site.

**What would have made this fail.** The checker was proved capable of failing before its green was
believed: appending `fakeactor999: / Inherits: ^NoSuchTemplateXYZ / BogusTraitNameXYZ:` to
`test-evac-suite/rules.yaml` produced both a dangling-`Inherits` and a dangling-trait report; the
file was then restored and `git status` confirmed clean. Without that step this section would be
the exact anti-pattern `DOCS/recipes/AUTOTEST.md` warns about.

**Hypothesis for why it is clean, unverified:** the 26 historical scenario deletions are
overwhelmingly renames (`test-v2-poi-*` → `test-experimental-poi-*`), which suggests symbol
renames have been carried through the suite mechanically rather than left to rot. Confirming this
would need `git log --follow` per scenario and is not worth the time — the static result stands on
its own regardless of the cause.

### A.1 — The one real staleness found is in a *harness comment*, not a scenario

`run-batch.sh:135-139` states: *"Nine such scenarios exist today — `test-artillery-turret` … and
the eight `test-balance-*` scenarios"*.

**Observed: there are 21 verdict-less `test-*` scenarios, and nine (not eight) `test-balance-*`.**
Enumerated by re-running the script's own predicate: `test-balance-{arty-1v1, at-vs-abrams,
at-vs-humvee, at-vs-t90, ifv-1v1, mbt-vs-2ifv, rifle-mirror, tank-1v1, tank-mass}` are
verdict-less; `test-balance-heli-1v1` is the only one that carries a verdict. The other twelve are
`test-artillery-turret`, `test-atgm-humvee-motion`, `test-burn-arena`, `test-burn-compare`,
`test-desync-dialog`, `test-javelin-latch-control`, `test-javelin-loop-probe`,
`test-javelin-reversal-sweep`, `test-minelayer-mode-survives-modifiers`, plus the three counted
above.

**The code is correct and the comment is stale** — the exclusion is detected, not hardcoded
(`run-batch.sh:143-145` says so deliberately), so the extra twelve are already being excluded and
announced. Only the prose count drifted. Cost of the drift: a reader trusting the comment
under-estimates the excluded set by more than half.

### A.2 — Two scenarios require a launch flag no batch supplies (SYMPTOM, with the run that confirms)

`AUTOTEST_EXTRA_ARGS` is consumed at exactly one place, `run-test.sh:735`, and is a plain
environment variable. **`run-batch.sh` never sets it** (grepped: no `EXTRA_ARGS` and no `export` in
that file), and there is no per-scenario launch-args mechanism anywhere in `tools/autotest/`.

Two scenarios document it as mandatory:
- `test-supplyroute-exempt-from-fog/test-supplyroute-exempt-from-fog.lua:65-68`
- `test-unscouted-building-hidden/test-unscouted-building-hidden.lua:50-56`

Both give the same mechanism: `TestModeLogic.cs:30` nulls `World.RenderPlayer` for a real player
slot, and every `World.FogObscures` overload returns false when it is null — so **the whole map
reads as mouse-targetable**.

**I traced what each does in that state by reading the rungs, and the headers are only half right.**
`test-supplyroute-exempt-from-fog`'s header says a null render player "would show this exemption
'working' whether or not it exists" — true of the exemption rung at `:140`, but the scenario has a
**discriminator below it** at `:151` (`if boxClickable then return "fail: an enemy PILLBOX … is
also visible, so the Supply Route rung above proves nothing"`). With everything clickable,
`boxClickable` is true and **the discriminator fires**. So the scenario goes RED, not vacuously
green. `test-unscouted-building-hidden` reaches the same outcome by the opposite polarity: its
control at `:119` passes (everything clickable) and its subject rung at `:125` then fires.

**Confidence: high on the mechanism and the rung structure (read at both ends — `TestModeLogic.cs:30`
and the two `.lua` files); not confirmed by a run.** These two are therefore *predicted* permanent
reds in `--all`, not observed ones.

**The run that would confirm it, and what counts as the answer:**
```
./tools/autotest/run-test.sh test-supplyroute-exempt-from-fog
```
with no `AUTOTEST_EXTRA_ARGS` set. **Confirms** if the verdict note is the `:152` pillbox-
discriminator string ("an enemy PILLBOX on never-scouted ground is also visible"). **Refutes** if
it passes, or fails on any other rung. One run settles it; the `[sr-exempt]` `print` at `:110-116`
dumps every input either way, so even an unexpected outcome is diagnosable from one launch.

---

## §B — Redundancy and supersession

The four suspicious clusters were checked by reading assertion bodies (not `description.txt`).
**Two of the four are clean.** Two contain one supersession each.

### Cluster 1 — the three `test-evac-*`: **GENUINELY DISTINCT.** A name collision, not redundancy.
`test-evac-suite` pins **crew ejection from stricken vehicles** and is not about evacuation-to-edge
at all (five phases asserting crew-actor counts, `test-evac-suite.lua:233, 297, 381, 436, 506`).
`test-evac-queued-after-waypoints` pins the **command-bar widget chain** — whether `Shift+E` is
consumed at all (`:218-226`) and whether the queued Evacuate ran after the waypoints (`:233-239`),
plus a bare-E control at `:194-208` that no sibling has. `test-evac-prefers-affordable-depot` pins
**host selection on affordability** (`:143`, with early-fails at `:116-123` and `:99-106`).
Nothing to do here.

### Cluster 2 — the seven `test-aa-*`: **one supersession, and a bigger problem underneath.**

**SUBSUMED as guards: `test-aa-overkill-cadence` and `test-aa-overkill-pump`, by
`test-aa-battery-volleys`.** Cadence's finding was that the battery serialises
(`test-aa-overkill-cadence.lua:55-60`); `test-aa-battery-volleys` turns exactly that into a failing
rung at `:288-297` (`if test.spread > allowance then Test.Fail("the stock battery fired one at a
time instead of together…")`), with a control battery carrying `OverkillThreshold: -1` at `:44-50`
and a count rung at `:268-273` that guards the degenerate one-shooter case. Cadence and pump have
no such rung — see §C, they cannot fail at all.

**NOT subsumed: `test-aa-overkill-suppression`.** It holds the only asserted cross-unit
suppression-latency rung — `if suppressionTicks > MaxSuppressionTicks` at `:284-289`, bound 90 at
`:102` — and `test-aa-battery-volleys.lua:40-42` explicitly declines to re-measure it.
*(This one contradicted my first automated pass, which mis-classified it as unasserted because its
failure string contains the word "control" — "past the unmarked control". Corrected by reading
`:270-300` directly. Noted because it is the kind of error this audit is supposed to catch.)*

**NOT subsumed, but see §C:** `test-aa-autotarget-thru-trees` (tree-ladder LOS gate),
`test-aa-detection-fog` (its fog-ON companion, stated at `test-aa-detection-fog.lua:1-3, 10-13`),
`test-aa-breakoff-critical` (the `critical-damage` three-way split, `:24-31`). Each covers a filter
the others structurally cannot — **but none of the three asserts anything.**

### Cluster 3 — the `test-dry-*` resupply set: **GENUINELY DISTINCT. There are seven, not six.**
`test-dry-resupply-reaches-truck:38` (dry seek completes to a `truk` under contact);
`test-dry-resupply-reaches-crate:48` (byte-identical map but `Supply: truk` → `supplycache`, a
**deliberate matched pair declared at `:9-11`**, and it carries a supply>0 setup guard at `:33-39`
the truck lacks); `test-dry-move-order-obeyed:74-78` (plain Move must arrive, Force-Move control);
`test-dry-inrange-idle-oscillation:66-69` (duty cycle, fails only if busy >50%);
`test-dry-evac-drops-queued-order:78-84` (a queued order must be *dropped*);
`test-dry-soldier-retry-after-refill:112-134` (the ITick re-ask, with a live control at `:118-121`);
`test-dry-seeks-affordable-cache:125-130,157`. Only the truck/crate pair shares a predicate and its
header says why the duplication is the point. Nothing to do here.

### Cluster 4 — `test-cohesion-*`: **one clean supersession.**

**SUBSUMED: `test-cohesion-cover-bid` by `test-cohesion-cover-redirect`.** Verified directly:
`diff` of the two `map.yaml` files returns **one line, the `Title`**. Same `TrunkCells`, same
`adjacentToTrunk`. Two differences only — the click cell
(`test-cohesion-cover-bid.lua:64` `CPos.New(26, 15)` vs `test-cohesion-cover-redirect.lua:47`
`CPos.New(22, 15)`) and the tolerance (`cover-bid.lua:89` `if #misses <= 1` vs
`cover-redirect.lua:64` `if #misses == 0`). **The redirect header names the relationship itself**
at `:9-11`: *"Compare against test-cohesion-cover-bid (which clicks inside the cluster where
box-formation alone already lands units adjacent — a positive smoke test, not a discrimination
test)"*. Same map, weaker click, weaker predicate, self-declared non-discriminating.

The other four are distinct: `test-cohesion-extent-cap` (two independent rungs, `:82-87` and
`:92-102`), `test-cohesion-slot-leash` (`CohesionSlotMemory.Assign`, incl. a pre-order rung at
`:19-25`), and two diagnostics — `test-cohesion-real-cluster` (sole assertion
`clusterDensity == 0`, `:67`) and `test-cohesion-river-zeta-actual` (unconditional pass, `:61`).

---

## §C — Scenarios that cannot fail

**This is the section with real weight. 13 scenarios enter every `--all` batch, are counted as
passes, and cannot report a regression in the mechanic they are named for.** They are not broken
and most are honest about it in their own headers — the problem is that `run-batch.sh` cannot tell
them apart from a real guard, so they inflate the green count.

### C.1 — Eight scenarios have no failure path whatsoever
No `Test.Fail`, no `AssertWithin`, no `AssertAfter`, no `"fail:"` arm — the only verdict is an
unconditional `Test.Pass` after a timer:

| Scenario | Verdict site |
|---|---|
| `test-game-clock` | `:30` `Test.Pass("clock screenshots captured at ticks 0 / 1000 / 2000")` |
| `test-screenshot-smoke` | `:25` `Test.Pass("smoke test took 3 screenshots")` |
| `test-frontline-reachability` | `:23` `Test.Pass("…observation window elapsed — read debug.log")` |
| `test-experimental-poi-observe` | `:16` `Test.Pass()` |
| `test-cohesion-river-zeta-actual` | `:61` `Test.Pass(… "%d probes issued — see debug.log")` |
| `test-unit-indicators` | `:72` `Test.Pass("captured cluster + asymmetry probe")` |
| `test-unit-indicators-before` | `:66` `Test.Pass("captured BEFORE baseline")` |
| `test-case01b-detect` | `:271` `Test.Pass(note)` |

`test-case01b-detect` states the intent outright at `:29-30`: *"CALIBRATION MODE: … The value is the
fire-lane metrics, not a pass/fail verdict (never `Test.Pass`-less — that would be a demo)."*
**That is a scenario passing specifically to avoid being classified as a demo** — which is the
`run-batch` exclusion being routed around, deliberately and in the open.

### C.2 — Five `test-aa-*` scenarios fail only on staging faults
Every `Test.Fail` in these five is a setup fault; the terminal verdict is `Test.Pass(summary)`
regardless of the measurement. **Verified by reading every failure argument in each file**, not by
pattern match (the pattern match was wrong once — see §B cluster 2):

`test-aa-autotarget-thru-trees` (fails at `:221` SETUP INVALID, `:232` player not found, `:241` AA
actor missing, `:251` could not spawn halo; passes at `:225`) — and identically
`test-aa-breakoff-critical` (`:210,221,230,240`; pass `:214`), `test-aa-detection-fog`
(`:287,298,308,318`; pass `:291`), `test-aa-overkill-cadence` (`:170,180,188,206`; pass `:174`),
`test-aa-overkill-pump` (`:182,197,203,210,227`; pass `:186`).

**The sharpest instance is `test-aa-overkill-pump`.** It computes the decisive quantity at `:169`:
```lua
local suppressedThroughPump = (obs == nil) or (obs > PumpStopTick)
```
concatenates it into the summary string at `:177` — `"suppressedThroughPump" .. (… and "Y" or "N")`
— and then reaches `Test.Pass(summary)` at `:186` **whichever way it came out**. The scenario
measures the exact thing it exists to test and never asserts on it. A regression flipping `Y` to
`N` changes one character in a pass note.

### C.3 — Tick-0 satisfiability: nothing wrong here
`AssertWithin` passes on the *first* tick its predicate is true (`test-helpers.lua:79-82`), so a
predicate true at setup passes vacuously. Six scenarios use `AssertWithin` with no grace counter
and no `"fail:"` discriminator; **all six were read and none is satisfiable at setup.** The closest
call, `test-counterbattery-radar-removed:33`, polls for coverage to *clear* (`:35`) — and is
properly guarded, because `:25-26` fails first if coverage was never present
(`"MSAR alive + deployed but CBR cover missing at probe cell — sanity precondition failed"`).
`test-heli-squad-forms:39` measures displacement from `SPAWN_X`, zero by construction at tick 0.

### C.4 — One scenario reports a graded FAIL for an inconclusive run
`test-offense-ammo-guard` uses **`Test.Skip`** for one setup-precondition failure at `:65`
(*"could not keep EmptyTank at zero ammo"*) but **`Test.Fail`** for two others — `:61`
(*"EmptyTank died during setup — inconclusive"*) and `:72` (*"EmptyTank died before verdict —
inconclusive"*). The word "inconclusive" is in the message; the verdict is a graded negative.

This is not theoretical: `DISCOVERIES.md` (2026-08-15 entry, the `Playable` gate sweep) records
this scenario hitting `:72` on **both** arms of a paired same-seed sweep, and explicitly says
*"That is the harness declining to render a verdict, not a graded failure, and it tells us nothing
in either direction. It should not be counted as a flip or as a pass."* The batch counts it as a
FAIL anyway.

---

## §D — `expected-status` adoption candidates

**Correction to the brief's premise, and to an in-tree entry.** The brief states zero scenarios
declare a status. **One does**, and it is present at this ref:
`tools/autotest/scenarios/test-drone-lost-track/expected-status`, landed same-day in `575e48c8`
(2026-09-01, *"autotest: declare test-drone-lost-track's by-merit fail…"*). `run-batch.sh:180`
(*"which is all 254 of them today"*) and `DISCOVERIES.md:48-49` (*"`ls
tools/autotest/scenarios/*/expected-status` is empty"*) are both now stale by one file. Worth a
one-line fix in each, because `DISCOVERIES.md:45-49` uses that emptiness to argue the timeout hole
is *latent rather than live* — and with a declaration now in place, **it is live.**

### D.1 — `test-supply-safe-front-keeps-cargo` — the one solid candidate, with a caveat that must go in the file

Evidence, at this ref: `PIPELINE.md:287` — *"`test-supply-safe-front-keeps-cargo` is RED (the truck
drops when it must not) and is unrefuted"*. `DISCOVERIES.md` (2026-08-15) records it failing
**identically on both arms** of a paired same-seed sweep and calls it *"pre-existing, and a
genuinely useful finding"*, pointing at `bugs/discovered.md` (2026-08-14) for the dangerous-front
drop branch firing at believed danger 0.

**The caveat is load-bearing and is why this proposal is not a slam dunk.** `PIPELINE.md:287`
continues: *"if the live match looks good while that stays red, the two disagree and the scenario
is the one to trust less."* Declaring `fail` banks a scenario the pipeline has pre-registered as
possibly wrong. The declaration is still right — it stops a known red from reddening every batch,
and `expected_status_grade` will flip it to `STALE`/RED the moment it starts passing — but the
file must say so, or it will read as an endorsement.

Proposed `tools/autotest/scenarios/test-supply-safe-front-keeps-cargo/expected-status`:

```
fail
BY MERIT, and unrefuted: the supply truck drops its cargo on a front believed safe,
which is the dangerous-front drop branch firing at believed danger 0
(bugs/discovered.md 2026-08-14). Fails identically on both arms of the 2026-08-15
paired same-seed Playable-gate sweep (DISCOVERIES.md), so it is stable, not flaky.

READ THIS BEFORE TRUSTING THE RED. PIPELINE.md:287 pre-registers the opposite
reading: if item 56's live match looks good while this stays red, "the two disagree
and the scenario is the one to trust less". This declaration records that the run
fails today; it does NOT ratify the assertion. If item 56's run contradicts it, the
scenario is what changes, and this file goes with it.

REMOVE THIS FILE in the same commit as whatever makes the truck keep its cargo.
A pass against a live `fail` declaration is RED on purpose.

CAVEAT PER expected-status.sh's KNOWN GAP: a `fail` declaration also grades a
watchdog TIMEOUT-FAIL green, so an OK(fail) here cannot distinguish "the truck
dropped" from "the game hung". Re-read the run banner, not the batch tally.
```

**This proposal is not yet safe to commit.** It rests on documents, not on a run at this ref — the
newest evidence is 2026-08-15 and `main` has moved a long way since. `expected-status.sh` requires
the declared outcome to actually occur, and a declaration written against a stale observation is
exactly the "stale note" the mechanism is built to reject. **One run settles it:**
```
./tools/autotest/run-test.sh test-supply-safe-front-keeps-cargo
```
**Commit the file** if the verdict is `fail` with the cargo-drop note. **Do not commit, and update
PIPELINE item 56 instead**, if it passes — that would mean the bug closed and nobody noticed.

### D.2 — Candidates I looked at and am NOT proposing

- **`test-field-swallows-shell`** — pipeline item 65 records it RED *then GREEN* after `db01b0ae`
  shipped the fix. Not a candidate.
- **`test-aa-overkill-suppression`** — item 44 records RED→GREEN at `afa18718`. Not a candidate.
- **`test-autotarget-preempt-air`** — `PIPELINE.md:315` says the test was **redesigned** on
  2026-09-01 so it "discriminates by construction", and is now blocked on a run. Its current status
  is *unknown*, not known-red. Declaring anything here would be guessing.
- **The two `KeepRenderPlayer` scenarios (§A.2)** — these are predicted permanent reds, but the
  failure is a **harness gap, not merit**. A `fail` declaration would bank a missing launch flag as
  an expected result and permanently hide the real assertion. Fix the plumbing (improvement 2), do
  not declare these.
- **`test-offense-ammo-guard`** — reaches a FAIL that is really an inconclusive (§C.4). The fix is
  `Test.Skip` at `:61`/`:72`, not a declaration.
- **The 13 `cannot-fail` scenarios (§C)** — `expected-status` **cannot express these.** They
  produce PASS; declaring `skip` grades PASS as `STOPPED`/RED (`expected-status.sh` decision table,
  `_check "declared skip, STOPS skipping" "skip" PASS STOPPED`). They need a code change first
  (improvement 1), and only then a declaration.

---

## Ranked improvements

Ordered by value per unit of effort. Effort in worker-sessions; risk is the chance of breaking
something that currently works.

**1 — Make the 13 `cannot-fail` scenarios say so, by switching their terminal verdict to
`Test.Skip`.** *(§C.1, §C.2. Effort: small — 13 one-line edits. Risk: low, and it is a
batch-accounting change, not a simulation change.)*
`Test.Skip` is already the harness's word for "ran, produced no graded answer"
(`TestGlobal.cs:55`), `run-batch.sh:216` maps it to its own `SKIP` bucket, and each can then carry
`expected-status: skip` so a scenario that *starts* asserting shows up as `STOPPED`/RED. This
converts 13 meaningless greens into 13 honest skips and is the single highest-value change here.
**It does not delete anything** — every one of these is a working measurement harness whose output
is read from `debug.log`; they keep doing that job under a truthful verdict.

**2 — Give `run-batch.sh` a per-scenario launch-args file.** *(§A.2. Effort: small — read an
optional `extra-args` file next to `expected-status` and prepend it to the `run-test.sh`
invocation. Risk: low; absent file = today's behaviour exactly.)*
Fixes the two `KeepRenderPlayer` scenarios, which today are the only two scenarios in the suite
that **cannot** produce their intended answer in a batch. Do improvement 5 first to confirm they
are actually red.

**3 — Assert what `test-aa-overkill-pump` and `test-aa-overkill-cadence` already measure.**
*(§B, §C.2. Effort: medium — the quantity exists; it needs a bound and a RED-verified run each.
Risk: medium — picking the bound is a judgement call, and a bound set from a single run is how
flaky tests are born.)*
`suppressedThroughPump` at `test-aa-overkill-pump.lua:169` is one `if` away from being a guard.
`test-aa-battery-volleys:288-297` is the worked example of how to bound it, control battery and
all. Until then improvement 1 applies to both.
**Alternative worth considering: retire them into `test-aa-battery-volleys`' shadow instead.**
That scenario already asserts the serialisation these two only measure; if nobody wants to own a
bound, improvement 1 is the honest resting state and this item can be dropped.

**4 — Fix `test-offense-ammo-guard`'s two inconclusive-as-FAIL sites.** *(§C.4. Effort: trivial —
`Test.Fail` → `Test.Skip` at `:61` and `:72`, matching `:65`. Risk: low.)*
Removes one permanent false red that `DISCOVERIES.md` has already had to explain away once in
writing.

**5 — Two confirming runs, when a launch slot is free.** *(Effort: 2 runs. Risk: none — read-only.)*
Both are single runs with a pre-registered answer, and both unblock a decision above:
`run-test.sh test-supplyroute-exempt-from-fog` (confirms §A.2; look for the `:152` pillbox-
discriminator string) and `run-test.sh test-supply-safe-front-keeps-cargo` (gates §D.1; commit the
declaration only on a `fail` with the cargo-drop note).

**6 — Correct three stale in-tree statements.** *(Effort: trivial. Risk: none.)*
`run-batch.sh:135-139` (nine verdict-less scenarios → 21; eight `test-balance-*` → nine);
`run-batch.sh:180` ("which is all 254 of them today" — no longer *all*, one now declares; I did
not reproduce where 254 comes from, so fix the "all", not the number);
`DISCOVERIES.md:48-49` ("`expected-status` is empty" → one exists, so the timeout gap that entry
describes is **live, not latent**). The third is the one that matters — it changes a "latent"
finding into an active one.

**7 — Retire `test-cohesion-cover-bid` into `test-cohesion-cover-redirect`.** *(§B cluster 4.
Effort: trivial. Risk: low, but this is the weakest item here and is listed last on purpose.)*
Same map to the byte bar its `Title`, weaker click cell, weaker tolerance, and its sibling's header
calls it *"a positive smoke test, not a discrimination test"*. **The argument is that it cannot
detect a regression `cover-redirect` would miss — not that the folder is crowded.** Retention is
cheap (13 MB total, 231 of 259 `map.bin` already byte-identical), so **the do-nothing option is
perfectly defensible**; the only cost of keeping it is one run slot per `--all`. If it stays, no
harm done.

---

## What this audit did not establish

- **Whether any scenario still *runs*.** Nothing was launched. A scenario can be symbol-clean and
  well-asserted and still hang on a map change; §A cannot see that, and `make nav-guard`'s baseline
  is `mods/ww3mod/maps` only — it does **not** cover `tools/autotest/scenarios/`
  (`CLAUDE.md`, and it cost two workers on 2026-09-01).
- **Whether the 189 batch-included scenarios that *do* assert still discriminate.** §C rules out
  the structurally-undiscriminating class. A scenario with a real bound whose bound has gone slack
  is invisible to static reading — that needs the RED-control discipline in
  `DOCS/recipes/AUTOTEST.md`, per scenario, and there is no shortcut.
- **The `demo-*` and `tournament-*` folders** were only counted, not audited. All 31 tournaments
  and 12 of 17 demos carry no verdict by design (`DOCS/recipes/DEMO.md`), so §C does not apply to
  them, but nothing here checks that they still stage correctly.
