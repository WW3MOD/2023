# Bounding `test-aa-overkill-pump` and `test-aa-overkill-cadence` — measurement protocol

**Ref: `main @ ee059361`** (verified with `git rev-parse`; the branch is clean and level with
`origin/main`). Worktree `wt/aa-overkill-bound`. Written 2026-09-01, **design only — no test was
run and no game was launched for this document.** Everything below that is not a `git`/`grep`
result is derived from source.

Background: `WORKSPACE/audits/260901-autotest-suite-audit.md` §B cluster 2, §C.2, ranked
improvement 3 (merged `0d2fdb25`); the two `expected-status` files merged `ee059361`.

---

## 0 — Verdict up front

**`test-aa-overkill-pump`: BOUND IT, but not the quantity the audit named, and not in the
direction the scenario's header predicts.**

The header's premise — that a Lua pump can drive `AverageDamagePercent` to a fixed point of 240 and
suppress the observer indefinitely — **was invalidated by `27d25f1c` (2026-08-21, "overkill
prevention: give the claim an owner so it can be handed back")** and the file was never corrected.
Under the shipped claim model one shooter holds one claim and re-committing *replaces* it, so lane
S's pumper contributes a flat 10 against a threshold of 100 no matter how often it re-orders. The
observer should therefore engage at its ordinary latency, and `suppressedThroughPump` should read
**N**, not the Y the file's "MEASURED 2026-08-10" block records.

That makes the honest guard the **inverse** of the one the audit imagined, and a better one:
*an adversarial single shooter re-committing every tick must not delay a neighbour past its own
control lane's latency.* Lane R is already in the scenario and is already the right baseline.

**`test-aa-overkill-cadence`: DO NOT BOUND IT.** Its quantity is definitionally
`test-aa-battery-volleys`' `test.spread`, and it has no control arm to cancel the 16–32 tick rescan
stagger — which `test-aa-battery-volleys:44-50` states in writing is the reason an absolute bound on
this quantity would be tuned rather than derived. Giving cadence a control battery *is* rebuilding
`test-aa-battery-volleys` inside it. Recommendation: leave the `Test.Skip` + declaration as the
resting state, and add **one staging guard** (§4.3) that is not a bound and costs no judgement call.

---

## 1 — Symptoms observed, with `file:line`

Facts. Causal reading is in §2 and is labelled.

| # | Site | Symptom |
|---|---|---|
| S1 | `tools/autotest/scenarios/test-aa-overkill-pump/test-aa-overkill-pump.lua:169` | `local suppressedThroughPump = (obs == nil) or (obs > PumpStopTick)` — computed, concatenated at `:177`, never branched on. Terminal verdict `Test.Skip(summary)` at `:189` is reached either way. |
| S2 | same file `:20-21` | Header states the pump "adds 10 per 5 ticks = 120 per 60 ticks. Fixed point V = V/2 + 120 => V = 240". Duplicated verbatim at `map.yaml:69-70`. |
| S3 | same file `:84` | `local PumpEvery = 1` — one order per **tick**, not "every 5 ticks" as `:20` says. The stale rate and the stale fixed point are two separate errors in the same sentence. |
| S4 | `engine/OpenRA.Game/OverkillClaim.cs:31` and `:50-60` | `Claim()` calls `Release()` as its first statement. Documented invariant: *"ONE SHOOTER IS ONE CLAIM. Re-committing replaces the held claim instead of stacking on it, so a unit that re-acquires the same target every rescan cannot inflate the tally on its own."* |
| S5 | `engine/OpenRA.Game/Actor.cs:91` | `MarkForDestruction` (the ownerless `+=` the pump's arithmetic assumes) has **zero callers** — verified by `grep -rn "MarkForDestruction" engine/ --include=*.cs`, which returns the definition and two comments and nothing else. |
| S6 | `engine/OpenRA.Mods.Common/Traits/AutoTarget.cs:1712` | Every commitment path routes to `attacker.ClaimForAttack(...)`. There is no surviving unowned-bump path for the pump to exploit. |
| S7 | `bff4260d` (2026-08-21) | Commit message: *"adds dated correction headers to the two scenarios whose reasoning asserted there was no release path"*. Its diff against the pump scenario is **one line** — an `Actor.cs:309-310` → `:345-346` line-number fix. The pump was not one of the two corrected; its arithmetic survived the release path landing. |
| S8 | `tools/autotest/scenarios/test-aa-overkill-cadence/test-aa-overkill-cadence.lua:159-161` | The only `setupFault` is `firedCount == 0`. There is no check that the helicopter survived undamaged — `test-aa-battery-volleys:234-238` has exactly that check, and `WORKSPACE/bugs/discovered.md` (2026-08-20 entry) records this scenario silently measuring nothing for eight days after its staging was disarmed. |
| S9 | `tools/autotest/scenarios/test-aa-battery-volleys/test-aa-battery-volleys.lua:47` and `:285` | Both cite `infantry.yaml:289-290` for the rescan interval. The values are at `mods/ww3mod/rules/ingame/infantry.yaml:311-312` at this ref — stale by 22 lines, same drift class as S7. Incidental; noted so the next reader of that derivation does not chase it. |

---

## 2 — What the code says will happen (HYPOTHESIS — nothing here was run)

**H1. Lane S can no longer suppress anything.** MANPAD `Damage: 3000` against the pump scenario's
`HALO: Health: HP: 30000` (`rules.yaml:38-40`) gives
`min(3000 * 100 / 30000, 100) = 10` (`AutoTarget.cs:1692`). The pumper re-orders every tick
(`:84`, `:118-130`); each order reaches `MarkTargetForAttack` (`AttackBase.cs:748`) →
`ClaimForAttack` → `Claim()`, which releases the previous 10 before adding the new 10 (S4).
The pumper is `HoldFire` and its activity is cancelled each tick, so it never fires and never
reaches the `Armament.cs:547` release — but it does not need to, because the replace-on-recommit
is doing the work. **Tally in lane S sits at ~10, rising to ~20 once the observer commits its own
claim, against `OverkillThreshold: 100`** (`AutoTarget.cs:217`). The hard skip at
`AutoTarget.cs:1458` is strictly `>`, so even a full 100 would not trip it.

**H2. The observer should fire in lane R's band.** Geometry, from `map.yaml`: lane R AA at
`(2,28) (4,28) (6,28) (8,28)` with the halo at cell `(5,8)`; lane S observer at `(58,28)` with its
halo at `(56,8)`. Vertical separation is 20 cells in both lanes; horizontal offsets are 3/1/1/3 for
lane R and 2 for the observer. **The observer's range to target lies inside the lane R spread**, so
lane R's first-shot ticks are a valid baseline for the observer's unsuppressed latency. The
2026-08-10 run put lane R at ticks 39/41/46/49.

**H3. Therefore `suppressedThroughPump` should read N, and `observerFire` should land near 40.**
The recorded value at that same seed was 818. If a run reproduces 818, H1 is wrong and that is the
finding — see the abort in §6.

**The dominant RNG in this measurement is bounded by construction.** First-shot latency is driven by
`^CamoSoldier`'s per-unit rescan draw, `MinimumScanTimeInterval: 16` / `MaximumScanTimeInterval: 32`
(`mods/ww3mod/rules/ingame/infantry.yaml:311-312`), taken off `SharedRandom`
(`AutoTarget.cs:1037`). The draw spans 16 ticks, so the seed-to-seed spread of a single unit's
acquisition is *a priori* bounded near one rescan period. This matters for §5: the measurement runs
exist to **confirm an a priori bound**, not to estimate an unknown distribution, which is why four
runs is a defensible number and forty is not.

---

## 3 — `test-aa-overkill-pump`: what to bound

### 3.1 Not the boolean

`suppressedThroughPump` is `(obs == nil) or (obs > 600)`. Asserting it false admits `obs = 599` — an
observer held down for 550 ticks — as a pass. It is a cliff at an arbitrary constant
(`PumpStopTick`), and a regression that halves the suppression still clears it. **A boolean whose
threshold is a staging constant is not a bound; it is the same non-assertion with an `if` in front.**

### 3.2 The bound: observer latency against lane R, derived in-run

Mirror `test-aa-battery-volleys:287-288` exactly — allowance expressed against the run's own control,
so no absolute latency constant is baked in and a future rescan retune moves both arms together:

```lua
-- Lane R is four unsuppressed AA at the same range to their own aircraft (map.yaml:84-98 vs
-- :106-108), so its first-shot ticks ARE this staging's natural acquisition latency, whatever it
-- happens to be on the day. The observer is one more of the same unit at the same range; the only
-- thing different about it is that a neighbour is re-committing against its target every tick.
local ObserverLatencyMultiple = 3
local StaggerFloorTicks = 32   -- MaximumScanTimeInterval, infantry.yaml:312

local rMin, rMax = <min/max of the lane R first-shot ticks already collected at :151-158>
local allowance = rMax + math.max((rMax - rMin) * ObserverLatencyMultiple, StaggerFloorTicks)

if obs == nil or obs > allowance then
    Test.Fail("a single shooter re-committing every tick held a neighbour off a healthy aircraft: "
        .. "the observer engaged at " .. (obs or -1) .. " against an allowance of " .. allowance
        .. " (lane R, four unsuppressed AA at the same range, fired at " .. rMax .. " latest). "
        .. "One shooter is one claim (OverkillClaim.cs:31) — re-committing must replace the held "
        .. "claim, not stack on it, or sustained attention reads as permanent over-commitment. "
        .. "|| " .. summary)
    return
end
```

Against the 2026-08-10 lane R numbers the allowance is `49 + max(30, 32) = 81`. H2 predicts
`obs ≈ 40–50`, i.e. **~30 ticks of headroom**; the pre-fix behaviour was 818, i.e. **failing by ~740
ticks**. That separation is what makes a bound honest here — it is not a close call being adjudicated
by a tuned constant.

`obs == nil` is folded into the failure because "never fired at all" is the most severe form of the
thing being guarded, not a separate case. It is not a staging fault: lane R firing proves the staging
built (and if lane R did *not* fire, `rMax` is nil and §6-C aborts first).

### 3.3 Does this catch anything `test-aa-battery-volleys` would miss?

This is the question that decides whether the work is worth doing, so it gets a direct answer.

`test-aa-battery-volleys:288-297` guards **many shooters, one commitment each**. Its spread is
sensitive to (a) `ReleaseAttackClaim` at `Armament.cs:547` — the hand-back-on-fire half of
`27d25f1c` — and (b) the `>` vs `>=` comparison at `AutoTarget.cs:1458` (`cccd5f81`).

The proposed pump bound guards **one shooter, many commitments**: the leading `Release()` inside
`OverkillClaim.Claim()` (`OverkillClaim.cs:52`), which is the *other* half of `27d25f1c` and the
only thing standing between a re-acquiring unit and an inflated tally.

**Claim (HYPOTHESIS, testable, and §5 run 6 tests it):** deleting `Release();` from `Claim()` leaves
`test-aa-battery-volleys` **green** while turning the pump red. The reason is `b47cdf7a`'s finding —
ordinary firing does not re-commit — so no shooter in `test-aa-battery-volleys` ever holds two claims
in the same window, and the missing replace never manifests there. If that is right, the two guards
cover disjoint halves of one fix and the pump bound is not redundant.

**I have not measured this, and it is the load-bearing claim of the whole item.** §5 run 6 costs one
launch and converts it from an argument into a fact. If run 6 shows `test-aa-battery-volleys` also
going red under that edit, then `test-aa-battery-volleys` genuinely subsumes the pump and the honest
answer flips to the audit's stated alternative — retire the pump into its shadow, keep the
declaration, do not add the guard. **That outcome is a success of this protocol, not a failure of it.**

---

## 4 — `test-aa-overkill-cadence`: why I am not designing a bound for it

### 4.1 The quantity is `test-aa-battery-volleys`' quantity

Cadence's terminal summary carries `gapFirstToSecond` (`:154-157`) and the sorted first-shot list
(`:148-152`). Four AA, one MANPAD each, one Halo, nobody ordered — spread of first shots across a
battery. `test-aa-battery-volleys` measures that exact statistic (`:200-208`) and already bounds it.
These are not adjacent quantities that happen to correlate; they are the same number computed twice.

### 4.2 Cadence has no control arm, and that is disqualifying

`test-aa-battery-volleys:44-50` says it plainly:

> `^CamoSoldier` draws its scan interval randomly per unit from 16-32 ticks
> (`infantry.yaml:289-290`), so four AA never acquire on the same tick even with nothing suppressing
> them. **Any absolute "all four must fire by tick N" assertion would be measuring that stagger, and
> would be tuned rather than derived.**

Cadence has one lane. There is nothing in-run to express an allowance against, so any bound on
`gapFirstToSecond` is exactly the absolute constant that paragraph rules out. The pump escapes this
because lane R is a same-range unsuppressed control that already exists; cadence has no such thing,
and adding one means adding a second battery with `OverkillThreshold: -1` — at which point it *is*
`test-aa-battery-volleys`, on a different map, with a worse staging.

**This is why the two scenarios get different treatment. Symmetry here would be a mistake.**

### 4.3 What cadence should get instead — a staging guard, not a bound

Its distinct observable, `shooterGaps` all equal to 200, is MANPAD's `BurstWait`. Bounding that
guards a weapons.yaml constant, which a lint or a unit test does better and cheaper than a 60-second
launch.

What cadence actually lacks is the check that its staging still works. `WORKSPACE/bugs/discovered.md`
(2026-08-20 entry) records this scenario measuring nothing for eight days because an unrelated fix
disarmed the mechanism it exploited. Its staging now rests on the `RangeLimit: 4c0` cut in
`weapons.yaml:70`, whose whole job is that **no damage is ever dealt**. If that override stops
applying — the same failure shape, and `weapons.yaml:51-58` documents that a composite override
silently reverting to engine defaults has voided a run of this scenario once already — missiles land,
the helicopter dies, and the cadence numbers are meaningless while the verdict looks identical.

Recommended addition, as a **`setupFault`, alongside the existing four and in the same register**
(the file already tracks `haloDeathTick` at `:102-104` and does nothing with it):

```lua
if haloDeathTick ~= nil then
    table.insert(setupFaults, "the helicopter died at t" .. haloDeathTick
        .. " - the RangeLimit cut did not apply, so shots are landing and the cadence measured"
        .. " here is a fight that ended, not a fight that serialised")
end
```

This is not the bound the dispatch asked for and I am not presenting it as one. It is a red that can
fire, it costs no judgement call, and it is the exact failure `bugs/discovered.md` already paid for
once. **Cadence's `expected-status` file stays** — it remains a declared skip, correctly.

Tracking `haloDamaged` as well (health below its first in-world reading, the
`test-aa-battery-volleys:142-148` idiom) would be strictly better than death alone, since a
helicopter that takes damage without dying corrupts the measurement just as thoroughly. Cheap, and
worth doing in the same edit.

---

## 5 — Measurement runs

**All serial, one at a time, manager-executed. Runs 1–4 need no code change at all** — the scenario
already prints every number the bound needs — so they can be taken on `main @ ee059361` as it stands.

> Fresh-worktree note (`DOCS/recipes/AUTOTEST.md:74`): build output is not shared between worktrees.
> If any run is taken in `wt/aa-overkill-bound`, `make all` must run there first or the launch aborts
> with `Required engine files not found.` and burns the slot. Runs 1–4 in the main checkout avoid this.

> `DOCS/recipes/AUTOTEST.md:76`: do not pipe these through `tail` — the pipeline reports `tail`'s
> exit status and has inverted a result twice. Read the verdict banner.

### Runs 1–4 — GREEN baseline, unmodified code

```bash
./tools/autotest/run-test.sh --seed -2058490156 test-aa-overkill-pump
./tools/autotest/run-test.sh --seed 1017        test-aa-overkill-pump
./tools/autotest/run-test.sh --seed 4241        test-aa-overkill-pump
./tools/autotest/run-test.sh --seed -7723       test-aa-overkill-pump
```

**Seed 1 is not arbitrary.** `-2058490156` is the seed of the 2026-08-10 measurement recorded in the
file header at `:35`. Re-running it on today's code is the `AUTOTEST.md:320-326` control: same seed,
same scenario, only the code differs, so a changed `observerFire` is positive proof that `27d25f1c`
is live in this scenario rather than an artefact of a different world. If run 1 returns 818 again,
H1 is simply wrong and §6-A fires.

**Why four.** One run is how flaky tests are born (the audit says so, and so does the
`expected-status` file at `:31-32`). Forty will not happen and is not needed: §2 establishes that the
only RNG feeding this latency is a 16-tick uniform draw per unit, so the runs exist to *confirm* a
bound already implied by the code, not to estimate an unknown spread. Four runs give four observer
samples and sixteen lane-R samples — enough to see a spread wider than one rescan period if one
exists, which is precisely the abort condition in §6-B. Below four there is no spread to speak of;
above four the marginal seed buys nothing the a priori argument does not already give.

**What to extract, per run.** Everything is in the `Test.Skip` reason, which lands in the verdict
banner and in `result.json` (`AUTOTEST.md:82,260`) under the run dir printed as `Run dir:` at the top
of the run. The summary is built at `:171-179` and reads:

```
LANE_R firedOf4=<N> ticks[<t1>,<t2>,<t3>,<t4>] || LANE_S pumps<N> pumpWindow5-600 observerFire<T> suppressedThroughPump<Y|N> pumperFire<T>
```

Record for each run: `firedOf4`, the four lane-R ticks, `observerFire`, `suppressedThroughPump`,
`pumperFire`, `pumps`, and the seed from `result.json`. Derive `rMin`, `rMax`,
`margin = observerFire - rMax`. Nothing needs to be read out of `debug.log`; if a run produces no
verdict at all, `lua.log` at 0 bytes is the first thing to check (`AUTOTEST.md:118`).

### Run 5 — RED control for the pump

Apply **one** edit, `engine/OpenRA.Game/OverkillClaim.cs:52` — delete the leading `Release();` from
`Claim()` — then `make all`, then:

```bash
./tools/autotest/run-test.sh --seed -2058490156 test-aa-overkill-pump
```

### Run 6 — the non-redundancy test, **same edit still applied**

```bash
./tools/autotest/run-test.sh --seed -2058490156 test-aa-battery-volleys
```

Then `git checkout engine/OpenRA.Game/OverkillClaim.cs` and `make all` to revert.

This is the run that decides §3.3, and it is the highest-information single launch in the set. Do not
skip it to save a slot: without it, "the pump bound is not redundant" stays an argument, and the
audit's alternative (retire the pump) stays equally defensible.

### Run 7 — GREEN confirmation, after the guard lands

Taken in the implementation dispatch, on reverted engine code with the guard added and
`test-aa-overkill-pump/expected-status` **deleted in the same commit**:

```bash
./tools/autotest/run-test.sh --seed 1017 test-aa-overkill-pump
```

Must report **PASS**. A declared skip that starts passing grades `STOPPED`, which is red — see §7.3.

**Total: 6 launches for the decision, 1 more to confirm the landing.**

---

## 6 — Pre-registered bound rule and abort conditions

**Written before any of the six runs. Nothing below may be revised after seeing the data; if the data
does not fit, the answer is one of the aborts, not a widened constant.**

Per run *i*: `R_i` = the four lane-R first-shot ticks, `rMax_i = max(R_i)`, `rMin_i = min(R_i)`,
`obs_i = observerFire`, `margin_i = obs_i - rMax_i`.

### The rule

**The bound is not fitted to the data at all.** It is fixed now, in the form given at §3.2 —
`allowance = rMax + max((rMax - rMin) * 3, 32)`, computed in-run from the control lane, with both
constants derived rather than chosen (`3` is `test-aa-battery-volleys:104`'s `SpreadMultiple`
verbatim; `32` is `MaximumScanTimeInterval`, `infantry.yaml:312`, verbatim). **The runs do not set
the bound. The runs decide whether this bound may be used at all.**

That is deliberate, and it is the difference between a bound and a tuned constant: a bound derived
from a mechanism can be falsified by data, while a bound fitted to data can only ever be confirmed by
it.

**PROCEED — write the guard exactly as at §3.2 — only if all four conditions hold across all four
GREEN runs:**

1. `suppressedThroughPump == N` on all four.
2. `obs_i <= rMax_i + max((rMax_i - rMin_i) * 3, 32)` on all four — the pre-registered formula passes
   unmodified, with no edit to either constant.
3. `max(margin_i) - min(margin_i) <= 32` — the observer's latency *relative to its own control* is
   stable to within one rescan period.
4. `firedOf4 == 4` and `rMax_i - rMin_i <= 32` on all four — the baseline itself is sound.

### Aborts — any one of these fires and the guard is NOT written

**A — REGIME ABORT.** Any run reports `suppressedThroughPump == Y`, or `observerFire` beyond
`PumpStopTick`. → **H1 is wrong: sustained re-commitment still inflates the tally despite
`OverkillClaim.cs:31`.** That is a live engine bug, not a test-design question. Stop, do not add any
assertion, file it in `WORKSPACE/bugs/discovered.md` with the run dir, and correct S2/S3 to say the
pump's arithmetic still holds. **This abort is the reason seed `-2058490156` is run first.**

**B — NOISE ABORT.** Condition 3 fails: the margin spread across seeds exceeds one rescan period.
→ The observer's latency is not stable enough relative to its control for any margin-based bound to
mean the same thing twice. Stop and report the four margins. Do not respond by widening the
multiple — a bound widened until the observed data fits is a bound that cannot fail.

**C — BASELINE ABORT.** Condition 4 fails: lane R fields fewer than four shooters, or its own spread
exceeds 32 ticks. → The control is not a control and there is nothing to express an allowance
against. This is also a staging finding about the scenario in its own right and belongs in
`bugs/discovered.md`.

**D — HEADROOM ABORT, and this is the anti-fudge clause.** Condition 2 fails on any seed: healthy
code does not clear the pre-registered allowance. → **Do not raise `ObserverLatencyMultiple`, do not
raise `StaggerFloorTicks`, do not switch the baseline from `rMax` to a mean.** Any of those is fitting
the bound to the data, and the resulting guard would pass by construction rather than by mechanism.
Stop, report the numbers, and hand the judgement back to the user.

**E — REDUNDANCY ABORT.** Run 6 shows `test-aa-battery-volleys` also failing under the run-5 edit.
→ The two guards are not disjoint after all; `test-aa-battery-volleys` covers this regression.
Recommend the audit's stated alternative — retire the pump into its shadow, keep the declaration, add
no guard — and let the user choose. Note that A–D concern the pump's *measurability* while E concerns
its *value*: E can fire even when 1–4 all pass, and it should still stop the work.

### Independent of every outcome above

S2, S3 and S7 are wrong on the page today and should be corrected whichever way the runs go. A
scenario header asserting a fixed point of 240, and a rate of "every 5 ticks" beside a constant of
`1`, will mislead the next reader regardless of whether anyone ever bounds the thing.

---

## 7 — RED controls

`DOCS/recipes/AUTOTEST.md:286` — *"the question to ask of every green is not 'did it pass?' but 'what
would have made this fail?'"*

### 7.1 Pump — primary RED (run 5)

**Edit:** `engine/OpenRA.Game/OverkillClaim.cs:52`, delete the leading `Release();` from `Claim()`.
One line. `git checkout` reverts it.

**Predicted mechanism:** without the release, each pump order is a bare `AddIncomingDamage(10)`. The
pump front-loads 20 orders at tick 5 (`:83`, `:123`) → tally 200 at once, then +10/tick against a
halving every 60 ticks (`Actor.cs:345-346`), fixed point far above 100. The observer is blocked at
`AutoTarget.cs:1458` from tick 5 through the end of the pump.

**Predicted failure message:** the §3.2 string, with `obs` at roughly 780–830 (the 2026-08-10 value
was 818) against an allowance near 81 — failing by an order of magnitude, not by a hair.

**This RED is chosen over a scenario-local one deliberately.** A cheaper red exists — set
`AA: AutoTarget: OverkillThreshold: 5` in the scenario's `rules.yaml`, which makes even the
uninflated claim of 10 suppress — and it would prove the assertion is *reachable*. But it would not
prove the assertion catches the *specific* regression it exists for, and reachability is the weaker
of the two things a RED is for. Note also that no rules-only RED can be built from a single shooter
inflating its own claim: `EstimatePercentDamage` caps one shooter at 100 (`AutoTarget.cs:1692`), the
threshold is 100, and the comparison is strictly `>`, so one shooter can never trip the hard skip
however the target's HP is set. **The mechanism under guard is reachable only from the engine, which
is itself the argument that it is worth guarding.**

### 7.2 Cadence — RED for the §4.3 staging guard

Not a bound, so this is a reachability RED only. **Edit:** in
`tools/autotest/scenarios/test-aa-overkill-cadence/weapons.yaml:70`, restore `RangeLimit: 24c0`.
Missiles then reach the helicopter, the first hit kills a 600-HP Halo outright, and the run must
report `SETUP INVALID: the helicopter died at t<N> - the RangeLimit cut did not apply...`. That is
the same disarm `bugs/discovered.md` records, reproduced on purpose. **Only worth a launch slot if
the §4.3 edit is actually taken**, and it is the lowest-priority run in this document.

### 7.3 The grading trap, for the implementation dispatch

`test-aa-overkill-pump/expected-status` declares `skip`. Grading is
`declared-skip + PASS -> STOPPED`, which is RED. **The guard commit must delete that file**, or the
scenario goes red on success. `test-aa-overkill-cadence/expected-status` is the opposite case: under
§4 it keeps its `Test.Skip` terminal verdict, so its declaration **stays**.

Runs 1–6 are all taken with the declaration still in place, and every one of them should grade
green-as-declared-skip. A `fail` from any of them means the staging broke, not that a finding
appeared — the declaration file says so at `:21-24`.

### 7.4 What stays untouched

Every existing `Test.Fail` in both files is a staging fault and must survive verbatim: pump
`:182, 200, 206, 213, 230`; cadence `:170, 182, 190, 208`. The new assertion is an addition placed
**after** the `setupFaults` block and **before** the terminal verdict, in the
`test-aa-battery-volleys:259-297` order — staging faults first, then the finding. A measurement
failure must never be folded into a `SETUP INVALID` string.
