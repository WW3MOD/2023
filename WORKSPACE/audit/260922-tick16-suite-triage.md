# Full-suite triage of the 16.667 tps flip — 2026-09-22

**Suite run:** `wt/tick-rate @ e6732446` (pre-merge), batch log `/tmp/tick-rate-batch.log`,
run dirs `~/.ww3mod-tests/screenshots/260921_1911…260922_0052`.
**Result:** Pass 198 · Fail 39 · Skip 21 · Error 1 (259 scenarios).
**VERIFIED 2026-09-22** (see the section at the end): of the 13 rows called tick-caused below,
**7 were**, 1 was flaky and 5 are real findings now re-classified to C. Final counts:
**A 32 · B 7 · C 21.** The per-row buckets in the tables below are the ORIGINAL triage and are
left as written so the reasoning that produced them stays auditable; the verification section is
authoritative where they disagree.
**Triage performed at:** `wt/tick-rate @ e6b218aa` (after merging `main @ d69e6883`).
**Fixes committed at:** see §Re-run list.

The flip changes `TestHarness.TicksPerSecond` 25 → 16.667 and routes the three consumers through
`TestHarness.TicksForSeconds` (multiply before divide, +1e-9). It changes **tick budgets only** —
`TicksPerSecond` is a Lua constant consumed by `AssertWithin`/`AssertAfter`/`ScreenshotAfter`; it
does not touch engine pacing, so no wall-clock behaviour moves because of it.

## The two mechanical discriminators used throughout

Both are static and decide most rows without guessing.

**1. Exposure.** A scenario is *exposed* only if it reaches the flip through
`TestHarness.AssertWithin/AssertAfter/ScreenshotAfter(seconds)` or `… * TestHarness.TicksPerSecond`.
`DateTime.Seconds(n)` is the ENGINE converter (`DateTimeGlobal.cs:52-55` → `TickTime.TicksForSeconds`),
was fixed on 2026-09-19 and is **untouched by this branch** (`git log main..HEAD -- '*DateTimeGlobal*'`
is empty). A scenario timing exclusively on `DateTime.Seconds` or raw tick literals is **structurally
immune** and cannot be bucket B. Eight non-passes are immune this way.
The `Ticks / TestHarness.TicksPerSecond` round-trip idiom is also immune — exactly, thanks to the
epsilon (verified: all 21 budgets below round-trip to their original integer, 0 mismatches).

**2. Which string wrote the verdict.** `AssertWithin` fails with either the predicate's own
`fail: …` string (returned on the merits, deadline irrelevant) or with its `timeoutReason`
(deadline expired). Matching the recorded note against the scenario's source separates them
unambiguously. A `fail:` verdict cannot have been caused by a shorter window.

## Bucket A — pre-existing / structural, not timing (13 fails + 19 skips)

Do not "fix" these. None is attributable to the flip.

### A-i · The batch never supplies a precondition the scenario requires (9)

These fail identically at 25 tps, 16.667 tps, or any other rate. They are batch-invocation gaps.

| Scenario | Evidence | Action |
|---|---|---|
| test-javelin-stationary-tail | Needs `--missile-trace`. Own gate `test-javelin-stationary-tail.lua:42-44` emits the verdict verbatim. `run-test.sh:217` `MISSILE_TRACE=0`; `run-batch.sh` contains no `--missile-trace`. | none |
| test-missile-hellfire-probe | "rig produced only 0 missile records … an empty trace is NOT evidence". Same cause. `description.txt` declares the flag. | none |
| test-missile-hover-only | as above | none |
| test-missile-latch-probe | as above | none |
| test-missile-range-sweep | "sweep incomplete: 0 records … a missing weapon is a broken rig" | none |
| test-missile-user-reports | "only 0 missile records" — ammo counters show the lanes DID fire (8→0), so only the trace is missing | none |
| test-supplyroute-exempt-from-fog | `description.txt:1` + `.lua:65`: "MUST be run with `AUTOTEST_EXTRA_ARGS=\"Test.KeepRenderPlayer=true\"` or a null RenderPlayer reports the whole map clickable" — which is the verdict produced. `run-batch.sh` never sets `AUTOTEST_EXTRA_ARGS`. | none |
| test-unscouted-building-hidden | `description.txt:1` + `.lua:50`, identical requirement; its verdict names the null-RenderPlayer branch as one of two hypotheses. | none |
| test-radar-only-targetable | Same family. `.lua:62-63`: "DarkHeli also catches a nil RenderPlayer: `World.FogObscures` returns false for every actor when RenderPlayer is nil, which would report the whole map as clickable" — the verdict says exactly that. | none, but its `description.txt` does **not** declare the env var its two siblings do — a doc gap worth closing |

### A-ii · Documented reds (2)

| Scenario | Evidence | Action |
|---|---|---|
| test-combined-arms-rendezvous | `WORKSPACE/bugs/discovered.md:3217` — "[info] … is a KNOWN-FAILING committed scenario with a non-discriminating control — **do not re-tune it**"; `:3337` "aborts on its tank-death guard at ~tick 550". Both runs this batch aborted at tick **544** and **539**. | none |
| test-supply-safe-front-keeps-cargo | `WORKSPACE/bugs/discovered.md:2999` — "fails **identically with and without** the economy fix (paired same-seed runs, `seed 5002`), so this is **pre-existing**". | none |

### A-iii · Declared expected-status (1)

| Scenario | Evidence | Action |
|---|---|---|
| test-aa-detection-fog | `expected-status` declares `skip`: "MEASUREMENT HARNESS, no graded answer: **every `Test.Fail` in the file is a STAGING fault**, not a finding … SETUP INVALID (`:287`)". Verdict was "SETUP INVALID: lane0 AA died" — precisely the declared class. | none. **But flag:** the declaration says `skip` and the run produced `fail`, which `expected-status.sh`'s third bullet grades RED by design. The declaration is imprecise, not the scenario. |

### A-iv · Pre-existing scenario defect, rate-independent (1) — and this one corrects the branch

| Scenario | Evidence | Action |
|---|---|---|
| test-depot-vacate-phantom | TIMEOUT, no verdict in 300 s, zero-byte `lua.log`. `debug.log` ends with **98 consecutive identical `2-vacated` screenshots** (98 PNGs on disk). `TestHarness.Screenshot("2-vacated", …)` at `.lua:226` sits **inside the per-tick `AssertWithin` predicate with no latch**, so it fires every tick while the predicate waits on the later Tank2 rung. The PNG writes exhaust the 300 s wall clock before the tick budget can expire. | none — but see below |

**This overturns the branch's own in-tree prediction.** `test-depot-vacate-phantom.lua:66-73` says
"If this starts timing out, re-derive it rather than widening it blindly", attributing a future
timeout to the budget. It did time out — but **not for that reason, and the flip made it less
likely, not more**: the budget fell 1875 → 1250 ticks, i.e. *fewer* iterations of the screenshot
loop before the deadline. The wall-clock timeout is rate-independent. Left unfixed here because
fixing it is a scenario-logic change (latch the capture) outside this triage's remit; filed in
`WORKSPACE/bugs/discovered.md`.

### A-v · The 19 intentional skips

Every one of the 21 skips carries at least one `Test.Skip` call (counted per file: 1–16 each).
Seventeen are measurement harnesses or capture scenarios that skip **by design** and reported
their payload in the note (`test-aa-overkill-cadence`, `test-game-clock`, `test-screenshot-smoke`,
`test-minimap-stance-shades`, `test-crew-*`, `test-unit-indicators*`, `test-frontline-reachability`,
`test-cohesion-river-zeta-actual`, `test-case01b-detect`, `test-aa-*`, `test-experimental-poi-observe`,
`test-fog-darkness-ruler`, `test-evac-refund-indicator`). Two more skip on **state**, not time:
`test-garrison-force-move-eject` ("R1 ended up in the tower's shelter rather than at a port") and
`test-offense-ammo-guard` ("EmptyTank died before verdict — inconclusive").

**Two are NOT intentional in this sense and are in bucket B** — see B-12 and B-13.

## Bucket B — tick-caused (11 fails + 2 skips)

Every row below is *exposed* by discriminator 1 **and** (for the fails) its recorded verdict is the
scenario's `AssertWithin` `timeoutReason`, not a `fail:` returned on the merits — i.e. the deadline
expired. Budgets shown are ticks at 25 tps (as authored and validated) → ticks at 16.667 (as run).

| # | Scenario · run dir | Budget lost | Evidence |
|---|---|---|---|
| 1 | test-tunguska-missile-standoff · `260922_002159_p15819` | 500 → 333 | Verdict is `.lua:107` verbatim. **Predicted in-tree** at `.lua:25-28`: "if this scenario starts timing out, that is the reason and the budget needs re-deriving." |
| 2 | test-ambush-fast-convoy · `260921_192946_p56188` | 350 → 233 | Verdict is `.lua:29` verbatim ("AT ambush did not spring … within 14s") |
| 3 | test-crate-force-attack · `260921_203018_p14419` | 500 → 333 | Verdict is `.lua:42` verbatim |
| 4 | test-pathfinder-tree-pass · `260921_231938_p60226` | 2250 → 1500 | Verdict is `.lua:37` verbatim ("Infantry did not reach (50,16) within 90s") |
| 5 | test-spread-no-autotarget · `260921_234801_p84699` | 500 → 333 | Verdict is `.lua:57` verbatim |
| 6 | test-truck-halts-to-serve · `260922_001830_p12747` | 1375 → 916 | Verdict is `.lua:63` verbatim |
| 7 | test-bot-defcon-wall · `260921_195334_p79693` | window OPEN moved 1250 → 833 | **Not a plain timeout.** `.lua` opened the hold window at `(RUN_SECONDS − HOLD_WINDOW_SECONDS) * TicksPerSecond`; shrinking both moved the sample start into the stretch where the axis is still walking to the border, so the "drift during the hold window" it measured was approach, not hold. |
| 8 | test-case01-forest-ambush · `260921_201402_p97864` | 2250 → 1500 | `deadlineTicks = math.floor(MEASURE_SECS * TPS)`, `MEASURE_SECS = 90`. Note reports `t=90.0s` — the *label* is unchanged because it is `elapsed / TPS`; the ticks behind it fell by a third. |
| 9 | test-lc-rearm-partial-order · `260921_223551_p20658` | 1000 → 666 | Three of four units "NEVER filled; errand STILL RUNNING"; the one that did fill needed **387 ticks** on a trickle path |
| 10 | test-wgm-accuracy · `260922_004313` | 250 → 166 | `shots_fired=8/8` but `dmg=31146/80000` — all shots away, damage accumulated over a window a third shorter |
| 11 | test-wgm-tree-density-ladder · `260922_004951` | 200 → 133 | Rungs read `-(a8)` (never fired) where the ladder expects FIRE/DENY |
| 12 | test-garrison-ownership-flip-evacuation · SKIP | 500 → 333 | **The dangerous shape.** `SetupWithin = 20` gates a `Test.Skip`, not a `Test.Fail`, so the scenario went off the air without going red: "one or both houses never became USA-owned within 20s, so the garrison never formed and **nothing under test was reached**." |
| 13 | test-garrison-port-arc-highpriority · SKIP | 1125 → 750 | Same shape: `MoveWithin = 45` gates a skip — "a mover did not reach its derived cell within 45s" |

### What was changed, and why this shape

Each budget is now stated in **ticks** — the unit it was actually authored and validated in — and
converted through the harness, which is the idiom `test-helpers.lua`'s own header prescribes for new
scenarios ("budget in TICKS and convert with `ticks / TestHarness.TicksPerSecond`"). That
round-trips exactly under the epsilon and is **rate-independent**: if the timestep ever moves again,
these budgets do not.

No deadline was re-tuned by hand to 16.667, and **no assertion was weakened or strengthened** — each
restores the identical tick budget its author measured against. Verified arithmetically: all 21
budgets round-trip to their original integer under `TicksForSeconds`, 0 mismatches.

Two incidental correctness fixes rode along where the new constants met old code:
`math.floor(seconds * TicksPerSecond)` in the two garrison helpers became
`TestHarness.TicksForSeconds(seconds)` (the former loses a tick on most budgets — it has no epsilon),
and sites that multiplied a derived seconds value back into ticks now take the tick constant
directly, because `ticks / tps * tps` is not guaranteed to land on the same integer.

## Bucket C — new / unexplained (15)

Not attributable to A or B by reading. **Every one is structurally immune to the flip** (discriminator
1) or failed on a `fail:` returned on the merits (discriminator 2) — so none is a tick casualty, but
none has a documented prior red either. Listed with the exact verdict line. No guesses offered.

| Scenario | Why not B | Verdict line |
|---|---|---|
| test-power-buy-loop (**CRASH**) | Zero harness timing constructs | *(no `result.json`; see below)* |
| test-escalation-full-match (**TIMEOUT**) | All budgets raw ticks (`DEADLINE = 24000`); the only `TicksPerSecond` mention is a comment at `.lua:7` | "timeout: no verdict after 300s" |
| test-autotarget-preempt-air | Branch converted it to an exact round-trip (`OuterTicks = 225`) | "fail: SHORAD engaged the band-5 helicopter 60 ticks after its arrival, but only after its commitment to the band-3 t90 lapsed (1 uncommitted scan(s) …). That is the unaided re-acquisition via AttackFollow.cs:176 …, not target preemption" |
| test-defcon-wall | `TicksPerSecond` assigned at `.lua:20` and **never used** | "THE WALL WAS CROSSED: the Russia airframe reached x=44 (wall at 44) on tick 120 … LAYER 1 IS ONE-DIRECTIONAL: the USA probe was refused but the Russia one travelled 30 cells west" |
| test-dry-inrange-idle-oscillation | Zero timing constructs; window is a raw 250 ticks | "dry in-range unit is busy most of the time: idle 0/250 ticks (0%), busy 250, transitions 0, ammo 0" |
| test-sam-intercepts-iskander | Zero timing constructs | "the missile reached 0 cells of its aim point (needs >= 6 to read as an interception), so the SAM did not stop it … prereqs=true" |
| test-visual-command-bar | `DateTime.Seconds` only | "selection is 1 actors, not the 3 riflemen" |
| test-visual-concealment-gauge | `DateTime.Seconds` only | "shot 02 (stopped): Detectable.CurrentVisibility is 5, expected 3" |
| test-visual-gauge-truth | `DateTime.Seconds` only | "Rifle is on tier 4, not 3 — his ring is not the 25 cells these captures are predicated on" |
| test-visual-radar-circles | `DateTime.Seconds` only | "selection is 1 actors, not 2" |
| test-wgm-burning-launcher-drops-guidance | Raw tick window | "test lane never got a missile airborne within 500 ticks (ammo=7)" |
| test-lc-drains-mid-errand | `fail:` on the merits | "fail: reached NearDepot (chebyshev 3) with 0 rounds after it was drained to 5 — the in-flight errand was never abandoned (peak ammo 0)" |
| test-poor-depot-still-worth-the-trip | `fail:` on the merits | "fail: the tank left the world — it evacuated for a refund rather than driving to the depot" |
| test-experimental-lccv-logistics | Round-trip idiom (`WindowTicks / TicksPerSecond`, `.lua:50`) | "deployed a Logistics Center at (6,14), only 2 cells from the Supply Route at (6,16) — needed >= 8" |
| test-experimental-msar-deploy | Round-trip idiom (`.lua:29`); budget stated in raw ticks | "bot never bought an MSAR within 6000 ticks — floor lane never fired" |

Four of these cluster and are likely one defect each rather than four:
the two `selection is N actors` visual failures, and the two `tier N, not M` visual failures.

### The crash, as asked

`test-power-buy-loop` · `260921_232523_p65425`. No `result.json`, zero-byte `lua.log`,
`result.launchstamp` present, 4379-byte `debug.log`.

**It is not an engine exception.** The four `Exception` matches in that log are the standard
mod-probe failures (`Load mod '…/engine/mods/cnc': InvalidDataException: FileSystem section is not
defined`) for cnc/d2k/all/ts, which appear in **every** run in this batch including passing ones.
There is no stack trace and no managed exception. The log ends mid-stream after normal
`[danger] pct player=FreadyFish n=2 …` output, i.e. the process died without unwinding.

Per the brief: it is **not** a scenario timing crash — the scenario contains no
`AssertWithin`/`AssertAfter`/`ScreenshotAfter` and no `TicksPerSecond`, so the flip cannot reach it.
It is therefore bucket **C** and is filed in `WORKSPACE/bugs/discovered.md`.

### The two timeouts, as asked — what each was waiting for

- **test-depot-vacate-phantom** was waiting for the Tank2 rung of its `AssertWithin` predicate,
  taking a fresh `2-vacated` screenshot on every one of those ticks (98 PNGs). It never reached a
  verdict because the captures, not the budget, consumed the 300 s wall clock. Bucket A-iv.
- **test-escalation-full-match** was waiting for its own tick-24000 deadline verdict
  (`DEADLINE = 24000`, `verdict("deadline")`). `debug.log`'s last line is `tick=8431` — it reached
  roughly a third of the way when the 300 s wall clock expired. Its budgets are raw ticks and did
  not move; this is throughput on a machine running 259 scenarios back to back. It **passed
  pre-flip** on 2026-09-15 (`260915_043213`, "stop=deadline tick=24001") as a solo run. Bucket C,
  and the suspicion is the 300 s default timeout rather than any defect — a `--timeout` bump is the
  cheap discriminator.

## The silent casualties — nothing red, coverage gone

The branch warned that the worse failure is a scenario that keeps passing while an inner budget
becomes unreachable. Two confirmed instances are B-12 and B-13 above, which reported **SKIP** and
would have been read as benign.

**A watchlist, deliberately NOT changed here.** Four scenarios that **passed** this batch carry the
same shrunken `math.floor(DeadlineSeconds * TestHarness.TicksPerSecond)` idiom:
`test-wgm-deny-thru-5-trees:15`, `test-wgm-accuracy-moving:27`, `test-wgm-target-dies-midflight:33`,
`test-wgm-no-fall-short:29`. Their windows are a third shorter than authored and they may now be
passing without enforcing. Editing a passing scenario's budget with no run to compare against would
be an unmeasured behavioural change, so they are recorded rather than touched.

## Re-run list — bucket B only

Run these to verify the fixes. Suggested: `./tools/autotest/run-test.sh --hidden <name>`.

| Scenario | Expected GREEN note |
|---|---|
| test-tunguska-missile-standoff | A pass — the predicate returns `true` on `AmmoCount("secondary-ammo") < startSecondary`, so the note is `AssertWithin: predicate true at tick N of 500 (30.0s budget) …` |
| test-ambush-fast-convoy | `AssertWithin: predicate true at tick N of 350 (21.0s budget) …` |
| test-crate-force-attack | `AssertWithin: predicate true at tick N of 500 …` |
| test-pathfinder-tree-pass | `AssertWithin: predicate true at tick N of 2250 …` |
| test-spread-no-autotarget | `AssertWithin: predicate true at tick N of 500 …` |
| test-truck-halts-to-serve | `AssertWithin: predicate true at tick N of 1375 …` |
| test-bot-defcon-wall | Its own pass note; the decisive change is that the hold window now opens at tick 1250, not 833 |
| test-case01-forest-ambush | Its own census note with `t=135.0s` (was `t=90.0s` for the same 2250 ticks) |
| test-lc-rearm-partial-order | All four units reporting `full after N ticks; errand FINISHED` within the 1000-tick deadline |
| test-wgm-accuracy | Accuracy at or above its threshold with `shots_fired=8/8` over 250 ticks |
| test-wgm-tree-density-ladder | A full ladder with no `-(a8)` rungs |
| test-garrison-ownership-flip-evacuation | **Must stop being a SKIP.** A real PASS or FAIL is the signal; another skip on `SetupWithin` means the precondition was never the budget |
| test-garrison-port-arc-highpriority | Same — it must reach a verdict rather than skipping on `MoveWithin` |

**Is a RED warranted? No, and here is why.** The standing rule is to sabotage the mechanism and
confirm the specific fail text before banking a green. It does not apply to these thirteen, because
**the RED has already been observed** — that is exactly what the e6732446 suite run is. Each of
these scenarios has just been seen failing with its own deadline string at the shrunken budget; the
fix restores the budget and the re-run is the green half of a RED/GREEN pair whose red is already
on disk with a run directory behind it. Manufacturing a second red would re-prove the same thing.

Two qualifications the manager should hold:

1. **A timeout proves the deadline expired; it does not prove the deadline was the only problem.**
   None of these eleven has a pre-flip baseline run (`~/.ww3mod-tests/screenshots` holds no earlier
   run for any of them) and ten of the eleven wrote a zero-byte `lua.log`, so there is no progress
   trace showing the subject was *about* to succeed. Restoring the authored budget is the
   information-preserving move; the re-run is what discriminates "the window was cut" from "this is
   also broken on the merits". **A scenario that still fails after this is a genuine finding, not a
   failed fix** — and should be re-classified into C rather than re-tuned.
2. **B-12 and B-13 are the ones to read carefully**, because their pass condition is "stopped
   skipping". A second skip there means the precondition was never about time.

---

# VERIFICATION — re-run of the 13 at `wt/tick-rate @ b4747687`, 2026-09-22 01:35–02:05

Manager-run, serial. Run dirs `~/.ww3mod-tests/screenshots/260922_0135…0202`.
**8 PASS · 5 FAIL.** Per this audit's own rule ("a scenario that still fails after this is a
genuine finding, not a failed fix"), the five re-classify from B to **C**.

## The 8 that the budget fixed — and one that did not need fixing

| Scenario | Evidence it was the budget |
|---|---|
| test-ambush-fast-convoy | Passed at **tick 261 of 350**, against a shrunken 233. 261 > 233, so it could not have passed at the flip's budget. **Decisive.** |
| test-pathfinder-tree-pass | Passed at **tick 1819 of 2250**, against a shrunken 1500. 1819 > 1500. **Decisive.** |
| test-wgm-accuracy | 81 % damage over 250 ticks against 38 % over 166, `shots_fired=8/8` both times. Consistent. |
| test-bot-defcon-wall | Own census note. The hold window now opens at tick 1250 rather than 833, which was the diagnosis. |
| test-lc-rearm-partial-order | Own note. |
| test-garrison-ownership-flip-evacuation | **SKIP → PASS with a note.** The precondition was the budget, as diagnosed. |
| test-garrison-port-arc-highpriority | **SKIP → PASS with a note.** Same. |
| test-spread-no-autotarget | **NOT evidence, and this corrects a row above.** It passed at **tick 228 of 500** — but the shrunken budget was **333**, and 228 < 333, so it would have passed at the shrunken budget too. Its earlier failure is therefore **not** explained by the flip; the two runs differ only in seed. Re-classify: a flaky/seed-sensitive scenario, bucket **C**, not B. The restored budget is still correct (it is the authored one) but it fixed nothing here. |

So the honest count is **7 confirmed budget casualties**, not 8 — and of those seven, two
(`ambush-fast-convoy`, `pathfinder-tree-pass`) are proven by arithmetic on the recorded tick,
two more by a SKIP→PASS flip, and three by consistent-but-not-decisive movement in their own notes.

## The 5 that remain red — classification as asked

**First, a correction to how these were characterised.** Only ONE of the five carries a predicate
`fail:` message. Two are `AssertWithin` **timeouts at the restored, authored budget**, which is a
stronger result than a merits-failure: the scenario was given exactly the window its author
validated against and still ran out of time. The recorded note tells them apart with no ambiguity —
a predicate failure is written with its `fail: ` prefix intact (see truck-halts below), a
`timeoutReason` is not.

| Scenario | Verdict class | Finding |
|---|---|---|
| test-tunguska-missile-standoff | **timeout** at 500 ticks (`.lua:107`) | **(i) real behaviour defect.** Filed. It neither fired nor closed — the `Location.X > ClosingCol` abort branch was not taken either, so a permitted-to-move unit chose to do nothing for 30 s. |
| test-crate-force-attack | **timeout** at 500 ticks (`.lua:42`) | **(i) real behaviour defect.** Filed. Identical at 333 and 500 ticks; the tank survived and the crate's health never moved. |
| test-wgm-tree-density-ladder | bespoke `Test.Fail` | **(i) real behaviour defect.** Filed. Budget **proven irrelevant**: all seven rungs classified identically at 133 and 200 ticks; the extra ticks only bought each *firing* lane one more round (ammo 7 → 6). Ladder is non-monotonic — 3t and 4t deny, 5t and 6t fire. |
| test-case01-forest-ambush | bespoke `Test.Fail` | **(i), with an instrumentation gap.** Filed. The restoration is visible (`defDmgTot` 256 → 461, `defDmgd` 3/5 → 4/5, a defender death at tick 2398 — outside the flip's 1500-tick window) but `attKilled` stayed 0 both times, and the trend runs the wrong way for a time explanation. **What would show it is still budget-shaped:** an `attDmgTot` counter trending toward a kill. The scenario records defender damage only, so it cannot currently distinguish "too slow to kill" from "landing nothing". |
| test-truck-halts-to-serve | predicate `fail:` | **(ii) threshold / setup question.** Not filed as a bug. Pre-fix it timed out; post-fix the extra 459 ticks let the run reach the real behaviour: the truck passed `DrovePastLine` (x=34) with all four riflemen at 492-or-better of 500. **More budget cannot help** — the fail is gated on the truck's POSITION (`.lua:57-59`), not on the deadline, so once x ≥ 34 the verdict is sealed whatever time remains. The open question is whether `short` (`.lua:51`, any `ammo < FullAmmo`) should demand a literal top-off when the men are within 1.6 % of full, or whether the last aura batch genuinely never lands. That is a ruling, not a fix. |

**(iii) — still plausibly budget-shaped despite the message: none of the five.** `case01` is the
only one with any claim to it, and its own data argues against: extending the window produced more
defender casualties, not attacker ones.
