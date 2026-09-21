# AssertWithin false-GREEN audit — 2026-09-21

**As of `main` @ `70e63582`** (worktree `wt/assertwithin-audit`, branched from it; `main` was
0 commits behind `origin/main` at the time of branching).

## The defect

`TestHarness.AssertWithin(seconds, predicate, reason)` called `Test.Pass()` — **with no note** —
the instant its predicate returned `true` (`mods/ww3mod/scripts/test-helpers.lua`, the `check`
closure). `Test.Pass` is TERMINAL: `TestGlobal.ExitWhenCapturesFlushed` writes `result.json` and
exits the game (`TestGlobal.cs:73-86`, `TestMode.cs:339`).

So a scenario that latches a flag and then schedules its real verdict a few ticks later **never
runs that verdict**. `result.json` reads `"status":"pass","notes":""`, carries no `screenshots`
key, no PNG reaches disk, and not one of the scenario's assertions has executed. It is a test
that cannot fail, reported as a test that passed.

Observed twice on 2026-09-21 in `test-himars-church-vs-block`: runs `260921_174913` and
`260921_175314`.

## The rule

> A predicate handed to `AssertWithin` must **never become true** in a scenario that has its own
> deferred verdict.

`AssertWithin` is safe in exactly two shapes:

1. **Pure watchdog** — the predicate is always false (or only ever returns a `"fail: "` string),
   used solely for its timeout. The scenario owns every verdict.
2. **Sole verdict authority** — the scenario's whole question is "did X happen in time", and
   nothing is scheduled to run after the predicate goes true.

A scenario that must judge *after* a latch has two accepted fixes: open-code the deadline in a
`Trigger.OnTick` poller (`test-himars-church-vs-block` @ `4e9f7b24` on `wt/himars-scenario`), or
call `Test.Pass(note)` from **inside** the predicate and then `return false` — which four
scenarios in this table already do.

## Scope — how the 46 were selected

Two greps, reproducing the brief's 127/45 exactly at its stated SHA `61d0c1f8`, then re-run here:

```sh
# 1. every scenario script that CALLS the helper -> 128 here, 127 at 61d0c1f8
grep -rl 'TestHarness\.AssertWithin(' tools/autotest/scenarios/*/*.lua > /tmp/aw.txt

# 2. of those, the ones that also carry their own payload verdict -> 46 here, 45 at 61d0c1f8
grep -l 'Test\.Pass([^)]\|Test\.Fail([^)]' $(cat /tmp/aw.txt)
```

The `(` in the first pattern is
load-bearing — it selects actual **calls**. A bare `grep -l AssertWithin` matches 164 files,
because 36 mention the helper only in prose.

**46 − 45 = 1**: `test-supplyroute-exempt-from-fog` was added between `61d0c1f8` and `70e63582`.
It is classified NO below.

**45 is an upper bound, and it was a loose one.** The grep cannot see whether a predicate ever
returns a truthy value, so it also matches every safe watchdog. Each of the 46 scripts was read
in full; none was classified from its name.

## Reconciliation

| verdict | count | meaning |
|---|---:|---|
| **YES** | **1** | predicate can return true before the scenario's own deferred verdict runs |
| **watchdog-only** | 4 | predicate never returns a truthy value; the scenario owns every verdict |
| **NO** | 41 | predicate returns true, but that IS the final verdict — nothing is queued behind it |
| | **46** | |

A caution about "NO": it means *no assertion was lost*, **not** that the scenario was
well-instrumented. All 41 previously reported `"notes":""`. That is fixed at the helper (below),
not per-file.

## The table

| scenario | AssertWithin | can its predicate return true BEFORE the scenario's own deferred verdict runs? | why — the predicate's actual returns | evidence that would have been lost | action taken |
|---|---|---|---|---|---|
| `test-himars-church-vs-block` | `:62` | **YES** | `return Reported` (**:62**). `Reported` is latched `true` in the OnTick poller at the same moment `Trigger.AfterDelay(2, Verdict)` is scheduled — AssertWithin polls every tick, so it wins by ~27 ticks, every time. | **Everything the scenario exists to measure.** Both latched HP readings; the assertion that one HIMARS floors the church to 1 HP; the assertion that the block keeps over half; the `01-after-one-salvo` screenshot; and the `Test.Pass(detail)` note carrying both numbers. | **None here — deliberately.** See "The one YES" below. |
| `test-autotarget-preempt-air` | `:162` | **watchdog-only** | `Test.Pass(...)` at **:202** is called from INSIDE the predicate, which then `return false` (:203). Every return is `false` or a `"fail: "` string. | Nothing. The margin figure and the uncommitted-scan attribution ride in the note it writes itself. **This file diagnosed the trap first** — see its PITFALL block at :123-128, dated 2026-08-12. | None. Already the correct shape. |
| `test-lc-rearm-partial-order` | `:238` | **watchdog-only** | `Test.Pass(reportAll())` at **:258** from inside the predicate, then `return false` (:259). The only non-`false` return is `return staging` (:239), and `staging` is a `"fail: "` string or `nil`. | Nothing. | None. Already the correct shape. |
| `test-lc-refill-gesture` | `:268` | **watchdog-only** | Predicate hands off to a settle window (:377-401) and returns `false`; `passWithReading()` (:111) is the sole Pass. The comment at :377 reads "NOT `return true`" and explains why. | Nothing. | None. Already the correct shape. |
| `test-restock-unreachable-centre` | `:197` | **watchdog-only** | `Test.Pass(state()...)` at **:273**, from inside the predicate. Every return in the body is `false` or a `"fail: "` string. | Nothing. | None. Already the correct shape. |
| `test-bot-damages-garrisoned-building` | `:232` | **NO** | `return true` (**:237**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-bot-defcon2-breaks-peace` | `:86` | **NO** | `return true` (**:91**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-bot-restocks-its-logistics-center` | `:72` | **NO** | `return true` (**:107**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-building-visible-at-spawn` | `:96` | **NO** | `return true` (**:177**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-counterbattery-radar-removed` | `:33` | **NO** | `return true` (**:36**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-crate-proximity-capture` | `:51` | **NO** | `return Crate.Owner.Name == "Me"` (**:60**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-crate-rearm-low` | `:52` | **NO** | `return true` (**:55**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-critical-no-panic` | `:70` | **NO** | `return ticks >= ObserveTicks` (**:93**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-dry-resupply-reaches-crate` | `:42` | **NO** | `return Hunter.AmmoCount("primary-ammo") > 0` (**:48**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-dry-seeks-affordable-cache` | `:112` | **NO** | `return ammo > 0` (**:157**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-dry-soldier-retry-after-refill` | `:75` | **NO** | `return ammo > 0` (**:108**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-evac-prefers-affordable-depot` | `:88` | **NO** | `return ammo > 0 and far < FarLoad` (**:143**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-experimental-buys-special-forces` | `:96` | **NO** | `return true` (**:140**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-experimental-msar-deploy` | `:51` | **NO** | `return true` (**:79**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-field-heli-unload` | `:58` | **NO** | `return Tran.PassengerCount == 0` (**:60**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-frozen-owner-snapshot` | `:88` | **NO** | `return true` (**:213**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-frozen-tooltip-owner-hidden` | `:84` | **NO** | `return true` (**:237**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-garrison-force-move-eject` | `:112` | **NO** | `return true` (**:154**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-garrison-kill-still-kills` | `:115` | **NO** | `return true` (**:117**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-garrisoned-emplacement-under-nuke` | `:170` | **NO** | `return true` (**:174**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-heli-corner-flow` | `:291` | **NO** | `return true` (**:343**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-heli-standoff` | `:64` | **NO** | `return true` (**:76**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-human-autoacquires-garrisoned-house` | `:200` | **NO** | `return true` (**:216**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-husk-corner-slide` | `:215` | **NO** | `return bursts >= burstsExpected` (**:220**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-lc-drains-mid-errand` | `:111` | **NO** | `return ammo > 0 and toFar <= ArrivedAtFar` (**:160**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-lc-salvage-bounded` | `:60` | **NO** | `return true` (**:113**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-poor-depot-still-worth-the-trip` | `:76` | **NO** | `return ammo > 0` (**:117**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-precaptured-structures` | `:84` | **NO** | `return true` (**:143**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-precaptured-structures-off` | `:62` | **NO** | `return true` (**:97**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-queued-attackmove-stale-cell` | `:69` | **NO** | `return true` (**:77**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-radar-only-targetable` | `:120` | **NO** | `return true` (**:178**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-rank-accumulation` | `:354` | **NO** | `return true` (**:558**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-rubbled-garrison-still-bleeds` | `:158` | **NO** | `return true` (**:202**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-strategic-launcher-ignores-depot` | `:88` | **NO** | `return true` (**:106**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-supplyroute-exempt-from-fog` | `:93` | **NO** | `return true` (**:160**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-tactical-arty-detour-geometry` | `:74` | **NO** | `return true` (**:103**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-tunguska-missile-standoff` | `:85` | **NO** | `return true` (**:102**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-unit-defaults-apply` | `:47` | **NO** | `return true` (**:88**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-unload-queued-after-waypoints` | `:199` | **NO** | `return true` (**:232**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-unscouted-building-hidden` | `:81` | **NO** | `return true` (**:136**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |
| `test-vehicle-rearms-at-empty-depot` | `:63` | **NO** | `return errandEnded` (**:128**) — this IS the scenario's intended final verdict. Nothing is scheduled to run after it. | None. No assertion, screenshot or note was ever queued behind this return. The note was empty before this branch; AssertWithin now writes one. | None to the scenario. Its verdict note changes from `""` to the AssertWithin line. |

## The one YES — and why this branch does not edit it

`test-himars-church-vs-block` is the scenario that produced both false GREENs, and **`main` still
carries the broken version**: `:62` reads `TestHarness.AssertWithin(40, function() return Reported
end, ...)`.

**It is already fixed on `wt/himars-scenario` @ `4e9f7b24`**, which replaces the helper call with
an open-coded `DeadlineTicks = 1000` poll inside `Trigger.OnTick`, making `Verdict` the single
verdict authority. That commit also rewrites the file's header, adds a per-tick probe, and moves
the attack order out of `WorldLoaded` — it is a wholesale rewrite of the file.

Editing the same lines here would guarantee a conflict with that rewrite and would duplicate a fix
that already exists, reviewed, on a branch whose whole purpose is this scenario. So this branch
leaves it alone.

> **This is the one thing that does not get fixed by merging `wt/assertwithin-audit`.** Until
> `wt/himars-scenario` lands, `test-himars-church-vs-block` on `main` can still report a green it
> has not earned. After this branch, that green at least carries the `AssertWithin:` note rather
> than an empty one — which is a label on the problem, not a fix for it.

## What changed on this branch

### 1. `AssertWithin` now passes a note (`mods/ww3mod/scripts/test-helpers.lua`)

`Test.Pass()` becomes:

```
AssertWithin: predicate true at tick N of M (Ss budget) — verdict authored by the watchdog,
not by a scenario assertion
```

The signature is unchanged and `TestHarness.TicksPerSecond` and the seconds→ticks arithmetic are
untouched (`wt/tick-rate` owns those lines; this branch does not modify them).

The note cannot resurrect an assertion that never ran. What it buys is two things: a verdict that
says **whose** it is, and — more importantly — it makes the tripwire below viable. Without it, an
empty-note green is the ordinary output of 41 audited scenarios plus 33 more and a tripwire on it would be pure noise.

The rule above is also written into the helper's doc block, where the next author will meet it.

### 2. `PASS-EMPTY` — the runner tripwire

`tools/autotest/run-test.sh` now tests the verdict for `"notes":""` and, on the `pass` branch,
reports **`OUTCOME=PASS-EMPTY`** instead of `PASS`, with a loud `==> SUSPECT PASS:` block.

The match is exact and safe against the writer: `TestMode.WriteResult` emits compact JSON with no
spaces (`TestMode.cs:339-350`).

**Consumers checked before choosing the encoding** — all five:

| consumer | effect |
|---|---|
| `tools/autotest/expected-status.sh` | extended: new grade `EMPTY`, new declarable status `pass-empty` |
| `tools/autotest/run-batch.sh` | extended: separate counter, separate Summary block |
| `tools/autotest/run-smoke.sh` | **unaffected** — `SmokeTestExit.cs:88` writes a real note, so smoke maps stay `PASS` |
| `tools/autotest/selftest.sh` | **unaffected** — its pass fixture writes `"notes":"stub"` |
| `tools/nuke-perf/README.md` | docs only; mentions `TIMEOUT-FAIL`, not `PASS` |

**Three deliberate encoding decisions:**

1. **`result.json` is NOT rewritten.** It is what the *engine* said, and it is the primary
   evidence artifact. Mutating `"status":"pass"` → `"pass-empty"` would break every reader that
   greps for `pass` and would destroy that property for a cosmetic gain. The distinction rides the
   **OUTCOME NAME** through `AUTOTEST_OUTCOME_FILE` — the side channel this harness already
   defines for precisely this problem, and whose header argues at length for a name over a code.
2. **`run-test.sh`'s exit code stays 0.** Its own header states the contract: *"Exit codes were
   left alone deliberately: run-batch, CI and every existing caller depend on 0/1/2/3, and
   widening them would have paid for this fix with a different silent break."* The "an empty green
   is a failed test" rule is enforced where it can be enforced without breaking that contract — in
   the batch, which counts `PASS-EMPTY` toward its non-green tally and so exits non-zero.
3. **`PASS-EMPTY` is graded BEFORE the `PASS|FAIL|SKIP` allowlist**, so it does **not** land in
   `NOTRUN`. `NOTRUN` means "hung, crashed or was killed" and prints a banner telling the reader to
   go and read `debug.log`; a scenario that passed with no note did none of those things and that
   banner would be false about it.

### 3. `pass-empty` as a declarable status

A scenario that legitimately passes with an empty note can declare it, exactly as `fail` and
`skip` already work, with a **required** reason:

```
tools/autotest/scenarios/test-<name>/expected-status
----------------------------------------------------
pass-empty
Liveness-only gate: the verdict cannot go red on the bug, the frames are the evidence.
```

This follows the asymmetry the repo already endorses (`mods/ww3mod/lint-baseline.txt`, and
`expected-status.sh`'s own header): the declared outcome occurring is GREEN, and it goes **STALE
(red)** the moment somebody gives the scenario a note. It can only ever be lowered deliberately.

**No declaration files are written by this branch.** Writing them would mean asserting "this one is
legitimate" about scenarios outside the audit set, which has not been done.

### The blast radius, stated plainly

**33 scenarios outside the 46 (34 call sites) call a bare `Test.Pass()` directly, and will newly
report `PASS-EMPTY` whenever they pass.** Verified: zero overlap with the audited 46, and all 33
have *only* the bare form — none has a payload `Test.Pass` on another branch, so this is "will",
not "may". They are not the AssertWithin defect — a bare `Test.Pass()` is their intended final
verdict — but they do produce a green with no statement of what was measured, which is exactly
what the standing rule is about. They are:

```
test-arty-turret-locked-while-moving      test-nuclear-ender-level
test-arty-turret-locked-while-turning     test-nuclear-exchange
test-bot-nuclear                          test-nuclear-side-cooldown
test-burn-demo                            test-observer-not-a-sighting-participant
test-capture-rules                        test-offense-ammo-guard
test-capture-vision-handover              test-parallel-queue-pause
test-engineer-rifle-refills               test-sr-rally-modifiers
test-experimental-engineer-repairs        test-stance-anchor-move
test-experimental-poi-harness             test-stance-optout
test-garrison-ownership-flip-evacuation   test-stance-positioning
test-garrison-port-arc-highpriority       test-stance-redirect-midadjust
test-heli-evac-unrearmable                test-supply-far-front-reached
test-heli-repairs-at-pad                  test-supply-two-clusters-commit
test-littlebird-strafe                    test-tanktrap-diagonal
test-no-cover-shuffle                     test-wgm-deny-thru-5-trees
test-no-formation-drift                   test-wgm-tree-density-ladder
test-who-pays-for-a-rearm
```

**RESOLVED 2026-09-21, same branch.** All 33 were read and all 34 sites now carry a note; none
needed a `pass-empty` declaration. Eleven already composed the string and discarded it on the green
path (a `summary` used only by `Test.Fail`, or a `Readings()`/`State()` helper) — those were
one-word changes. The rest got a note built from state already in scope at the verdict; only the
turret pair needed a new local, to track peak deflection, because "the turret stayed locked"
without the number it stayed within is the same empty verdict written longhand.

`test-burn-demo` is the single genuine nothing-to-say pass — a demo wearing a `test-` prefix, same
family as `test-burn-arena` and `test-burn-compare`. It carries a one-line comment at the call and
a literal note saying it asserted nothing, rather than a declaration, so the run still says so out
loud.

Verified without launching anything: `luac -p` over the whole scenario tree plus
`mods/ww3mod/scripts` (zero syntax failures), and — the real risk — every changed file compiled at
`HEAD` and at the working tree with its set of global reads diffed. An out-of-scope name is not a
Lua compile error, it is `nil`, and concatenating `nil` throws at the verdict and turns a passing
scenario into a false RED; such a name appears as a NEW global read. Zero new globals across all 33
files. That check was itself RED-tested by planting `WATCH_TICKZ` in `test-no-cover-shuffle`.

## Unsettled

Nothing in the 46 was left unsettled by reading. Every predicate's complete set of `return`
statements was extracted mechanically from the AssertWithin call to its matching `end` and then
read in context, so the "can it go true" column is a property of the source rather than a guess
about game state.

Two things are **true but out of scope**, recorded so they are not rediscovered as new:

* **`AssertWithin`'s internal deadline keeps counting after a Pass-from-inside-the-predicate.**
  In all four watchdog-only scenarios the predicate calls `Test.Pass(note)` and returns `false`;
  `elapsed` keeps incrementing, and if the deferred exit has not flushed by `timeoutTicks` the
  helper calls `Test.Fail(timeoutReason)` — and `ExitWhenCapturesFlushed` is last-one-wins. That
  is a false-**RED** risk, not a false-GREEN, and every one of the four has margin designed in
  (`test-autotarget-preempt-air` documents it at :160-161: "225 > 174").
  `test-lc-refill-gesture` names it as the intended behaviour at :377-381.
* **The tripwire cannot detect the YES class.** Once `AssertWithin` carries a note, a himars-shaped
  false GREEN passes with a non-empty note and `PASS-EMPTY` never fires. The race is a static
  property of the script, invisible to the runner. **BUILT 2026-09-21, same branch:** it is now a
  `lua-gate` check (`tools/lua-gate/README.md` §"The third failure class"), which fires on exactly
  one scenario across 327 scripts in 362 scenarios — the one that had the bug — with the four
  watchdog-only and 41 sole-authority shapes silent. It is a positional heuristic and is documented
  as one.
