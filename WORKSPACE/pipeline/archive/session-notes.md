# Archive — session snapshots, batch framing and method notes

_Split out of `WORKSPACE/PIPELINE.md` on 2026-08-19 at `main @ de78a1ed`. **Text is verbatim.**_

**Two kinds of content live here, and they age very differently.**

1. **Dated snapshots** — the 2026-08-11 SESSION STATE, the gate/process/grant blocks, the close-out reconciliation table. These were true when written and are **superseded, not corrected** (the standing rule: a reference doc gets corrected, a dated snapshot gets superseded). Treat every commit SHA, count and status claim in them as historical.
2. **Method notes worth reusing** — inside the SESSION STATE block, under *"Method notes worth reusing (all earned the hard way this session)"*. **These do not age.** They are the most reusable content in the whole pipeline archive: prove the diff contains only your intended change; a green test can prove the path it took rather than the mechanism it was written for; a bug that cannot fire looks exactly like a bug that is not there; a measurement coming back negative is still the measurement succeeding; when a check returns "no counterexamples", ask whether the shape makes counterexamples impossible.

Batch framing blocks are here because they explain **why items were kept separate** — e.g. that 63 (procurement) and 64 (what units do after they arrive) must not be merged, and that 56 (delivery conduct) and 57 (what the bot buys) must not be merged. That reasoning is still binding on anyone re-scoping those items.

---

## GATE — lifted 2026-07-23

User played a 2v2 vs three bots. The three previously-gated behaviors (heli standoff `090ad9d0`, /danger overlay `0833b376`, Phase-4a role tasking `acc42ad7`) drew no complaints. The session surfaced four new items, captured as queue items 1–4 below; the queue is unblocked and reordered accordingly.

---

## PROCESS SHIFT — scenario-case model (2026-07-26)

Autoburn retrospective verdict (first window, ~07-20 → 07-25): throughput and review quality held, but **outcome measurement fell away** — later bot changes shipped verified by build/NUnit/scenario-logic, never by bot-vs-bot numbers (the Stage-F benchmark re-baseline is still declared-never-run), and ten tracks piled into needs_review because acceptance was subjective. Fix adopted: **user-authored cases with measurable bars** — see [`WORKSPACE/cases/README.md`](../../cases/README.md) for the model, format, and constraints. Queue items 20–25 below implement the reboot. Standing dependency: case calibration/measurement batches remain **user-gated** (no-autonomous-multi-test rule); a scoped standing grant would be the single highest-leverage unblock for autonomous case iteration.

---

## STANDING GRANT — next autoburn window (user, 2026-07-28)

The user granted the full test ladder for the **next autoburn window**: "When the next autoburn starts you can use whatever tools you need, unless otherwise specified." Covers single autotests, calibration batches (item 22), and the Stage-F full ladder re-baseline + gate (b) benchmark pricing (item 25). **Window now ACTIVE (2026-07-29)** — the item-22 calibration batch has run and the item-25 re-baseline is RUNNING under the grant. Outside autoburn, the per-turn goahead rule still applies.

---

### SESSION STATE 2026-08-11 — read this first if you are picking the work back up

Main is at **`54ea7210`** and **in sync with `origin/main`** (the user pushes manually — never push). **178 commits landed since `b6daaf4b`**, spanning 2026-08-05 → 08-11. This file, `HOTBOARD.md` and `AWAITING-USER.md` had all gone multi-day stale; everything below was re-derived from `git log`, not from the boards' own prior claims.

**Merged in the 08-09 session:** `auto/danger-scale` (`5642d931`, NUnit 1275/1275 — weapon fire-cycle model + danger unit + thresholds) and `auto/aircraft-rearm` (`af36e686`, 1289/1289 — airframe readiness asks the world, not the rules). Also the six-document `DOCS/bots/` set (~4,500 lines) plus a reconciliation pass over it.

**Merged since that snapshot:**

| Branch | SHA | Date | What |
|---|---|---|---|
| `auto/supply-dwell` | `e79ddd97` | 08-07 | close the truck evac control loop, then damp it |
| `auto/posture-veto` | `bd3abacf` | 08-08 | bound `SectorPostureHold` so a flanking axis can commit |
| `auto/evac-polish` | `9ab1b2e2` | 08-08 | **queue items 36 + 38** — crew retire, ground units drive off the map |
| `auto/tank-trap` | `dd3430a8` | 08-08 | **queue item 37** — diagonal corner-to-corner squeeze closed |
| `auto/unit-purpose` | `09877fd5` | 08-08 | an arriving unit gets an owner; garrison stops annexing houses |
| `auto/truck-churn` | `574a3c08` | 08-08 | damp the residue latch and the follow Move |
| `auto/order-gate` | `0eef99d6` | 08-08 | incumbency + dwell at `ModularBot.QueueOrder` |
| `auto/supply-drop` | `8d0ff18b` | 08-08 | trucks drop a cache and leave |
| `auto/nav-guard` | `2754f341` | 08-09 | static per-locomotor map-connectivity gate |
| `wt/vehicle-occupants` | `18838dd7` | 08-10 | gameplay-batch items 1–4 |
| `wt/fx-round1` | `62d8148e` | 08-10 | gameplay-batch item 5 — 161 weapons |
| `wt/ammo-rearm` | `175a4784` | 08-10 | gameplay-batch item 6 |
| `wt/elimination-cascade` | `f49b6aca` | 08-10 | eliminating one player no longer defeats the survivors |
| `wt/medic-autonomy` | `cfcc947b` | 08-11 | medics stop wedging on orders they cannot carry out |
| `wt/ammo-attack-guard` | `bd7b6bb2` | 08-11 | a dry unit drops its attack order |
| `wt/branding-text` | `4836ceed` | 08-11 | shipped text reads as WW3MOD |
| `wt/audio-ogg` | `2f31404e` | 08-11 | Ogg enabled, dead `arabs/` voice set removed |
| `wt/cameo-tool` | `2c110a67` | 08-11 | `tools/cameo` |

Plus a large run of supply and autotest work committed straight to main on 08-10 (`d1f84a30`…`377085db`, no branch), and the release-readiness / asset-licensing docs thread (`17e3ce4c`, `54ea7210`) — captured as **queue item 39** below, where it had never been represented at all.

#### Regression state as of 2026-08-10

68-test tally (`6cddfed8`): **60 pass / 8 fail**, none traceable to the two engine merges. `test-supply-far-front-reached` passes for the first time (`377085db`). The `test-offense-ammo-guard` RED is **pre-existing and its premise is stale** (`418e9c60`) — do not chase it as a regression. `make nav-guard` is now a prerequisite of `make test` on both platforms (`e537df0d`); river-zeta's baseline was re-recorded for the terrain fine-tuning pass (`9bade8c5`).

#### The 08-09 debt: one of the two owed items IS settled — and it did not settle the way the fix wanted

1. **`[danger] reference` — READ 2026-08-10 (`084367b0`). This was the evidence gate, and the evidence is NEGATIVE.** On `2754f341`: `ground=3412`, reproducible across seven logged lines. `evacLevel` resolves to **1,706** against a 2,795 decayed-rumour floor and live median cells of **27,919 (USA)** and **94,010 (Russia)** — so **by the code's own criterion the supply-truck evac guard fires unconditionally once contact exists.** min/max straddle the median by **2.57 orders** against the ~2-order tell the 08-09 note set, and only **92 of 424** types contribute. The verdict is the one that note named as the bad case: **the danger UNIT is the scale error, not the thirteen thresholds** — all thirteen sit below the median cell of the field they gate (highest is 100 units = 3,412 against live medians of 818u and 2,755u). Post-measurement plan for user review: `WORKSPACE/plan-260810-post-measurement.html` (`6cddfed8`). **Nobody may tell the user supply trucks are fixed on the strength of `5642d931`.** It would be the fourth undemonstrated claim, and the measurement that was supposed to license it says the opposite.
2. **`@stable` benchmark re-baseline — STILL OWED, and the debt GREW.** Three further merges declare a knowing `@stable` behaviour change in their own commit messages: `dd3430a8` (PathFinder's adjacent-cell shortcut also stopped skipping the height-discontinuity rule), `175a4784` (`ReturnWhenEmpty` on `^Soldier` plus an unconditional mid-resupply withhold), `18838dd7` (firepower gated at Heavy rather than Critical, passenger evacuation, bot target-selection no longer tasking paused armaments). Still take it ONCE and last — and after the danger-unit question, not before. User-gated (no-autonomous-multi-test).

#### Open user decisions — do not re-ask, do not act unilaterally

- **Where aircraft rearm and repair.** Posted with four options. `HPAD`/`AFLD` carry `Buildable.Prerequisites: ~disabled` — an unsatisfiable *build* prerequisite (nothing provides `disabled`), not a disabled trait — and are pre-placed on zero of ten maps. But **the mod DOES have a working rearm system**: `logisticscenter` has `RepairsUnits` (`structures.yaml:377`) + `SupplyProvider` (`:387`), is pre-placed as a Neutral capturable on three maps, and is named by every infantry template and ground vehicle. **Aircraft alone name only the two impossible actors.** The agent's own highest-rated option is the one no worker proposed: put rearm on the **Supply Route** itself — fixed, indestructible, guaranteed to every player on every map, and on-model for the reinforcement economy, where `logisticscenter` makes the rules differ per map and only post-capture. **Load-bearing caveat for whoever implements the answer:** all three hosted arms of the new `AirframeReadiness` predicates are structurally unreachable today (aircraft `RepairActors` list only `afld`/`hpad`), so they are pinned by unit test and have **never executed**. Wiring any host makes them all go live in the same instant, first-run-in-anger together.
- **One autotest run for the instrumented order log** (`Test.Mode=true Test.UnitLifecycleLog=<path>`) — posted, unanswered, agent proceeded. Still the cheapest way to rank churn sources empirically.
- **Five decisions from the 08-10 post-measurement plan** (`6cddfed8`, `WORKSPACE/plan-260810-post-measurement.html`) — parked in [`AWAITING-USER.md`](../../AWAITING-USER.md). Their binding constraint is stated below.

#### Next work, in the order it should be taken

1. **Durability weight — ANSWERED 2026-08-11 (`f2a31035`), and the sequencing verdict is: fix it FIRST.** The `[med]` claim in `bugs/discovered.md` holds — the weight band is **1.20×–29.50×** against a documented ~1.0× intent — but the effect is not where it was expected. The reference is the median *type*, and that type is a rifleman at 1.22×, so correcting it moves the reference only **3,412 → 2,748 (−19%)** while dividing heavy contacts' stamps by ~20×. **Thresholds derived before the fix would be fitted to a field about to shrink under them**, so the ordering stands even though the headline number barely moves. Two further findings from the same pass: the fix **widens** rather than narrows the type spread (3.89 → 4.07 orders), so median-of-types is a weak denominator regardless; and `WeaponThroughput` ignores `Versus`, so zero-damage targeter weapons register as ground threats. Also — an offline MiniYaml parser now reproduces the logged `[danger]` reference line exactly (`ground=3412 air=3627 92/424 min=164 max=1271250`), which confirms the shipped cadence model and means **the danger unit never needs a live game session to re-derive again**. The `[danger] reference` read is therefore no longer a user-gated dependency for this work.
2. **Per-unit peel-out guard for helicopter squads.** Deliberately NOT built; recorded in `DISCOVERIES.md` with reasoning. `SendDamagedUnitsHome` is now a complete no-op (its `ReturnToBase` orders are refused), so a squad at ~36% average relaunches indefinitely on a ~75-tick cycle with no individual airframe ever pulled out. The proposed fix — drop the unit from the squad rather than ordering it home, so it survives the no-host case as a membership change rather than an order — is probably right but is an untestable behaviour change. **Standing rule adopted: one un-run behavioural change per branch is a measurement; two is a guess.** Wants a live look first.
3. `ScoreDrop` weight imbalance — sign fixed, weighting not. Danger still dwarfs control (≤ ~800) and reach (≤ ~500) by orders of magnitude at 1e5–1e7, so the "risk-weighted" drop site remains close to a danger-argmin. Needs a **measured doctrine call** on the relative importance of "how deep in enemy territory" vs "how dangerous" — deliberately not guessed.
4. Three `StagingCell` callers still walk without the passability predicate. Recorded, unfixed.

#### Method notes worth reusing (all earned the hard way this session)

- **Prove the diff contains ONLY your intended change** — stronger than probing for each thing you hoped survived, because it also catches what you did not think to probe for. This is what made the `ai.yaml` silent-auto-merge safe.
- **`ai.yaml` auto-merges SILENTLY** and blank lines between MiniYaml top-level entries are load-bearing. Verify semantically: `cat -A` the twin separator, diff the key histogram, and confirm the whole-file diff is only yours.
- **Probing a rebase mid-flight tests a tree that does not exist yet.** A fix that lives in a later commit will correctly probe as absent.
- **A reference doc gets CORRECTED; a dated snapshot gets SUPERSEDED.** `DOCS/reference/` is the former, `DOCS/bots/` the latter — do not rewrite a dated snapshot's body to pretend it always said the new thing.
- **A curated doc that declares itself normative over the code cannot be "corrected" to match broken code** — that is a spec violation to file, not a doc error to fix. (`economy.md` says so in its own header.)
- **Parallel doc writers produce good bodies and unreliable headlines.** Every one of eleven errors found across the `DOCS/bots/` set was a correct body with a summary rounded wrong. **Budget a reconciliation pass into any parallel doc batch from the start**, and after writing, re-derive every summary sentence *from the table it summarises*.
- **When two sources disagree on a count, the answer is often a third number** — both counted something adjacent to the thing.
- **Hand a refutation back to the party whose model it refutes.** They are best equipped to find its flaw, and a concession from them is worth far more than a fresh reviewer's agreement.
- **When a check returns "no counterexamples", ask whether the shape makes counterexamples impossible.** One more pass buys an answer that never needs re-running.
- **When removing a wrong dependency, prefer taking the input and visibly discarding it over deleting the parameter** — a plain absence reads as an oversight and gets re-added. That is how the aircraft-rearm defect was created in the first place.
- **`~` in this codebase means an unsatisfiable prerequisite, not a disabled definition.** A reader assuming "disabled trait" reaches the right conclusion by the wrong route.

_Added 2026-08-11, from the 08-07 → 08-11 window:_
- **A green test can prove the path it took rather than the mechanism it was written for.** The tank-trap scenario passed while the escape hatch it existed to exercise was unreachable, because its squeezer reached the same outcome by an ordinary route. Ask what the test would do if the mechanism were deleted.
- **A unit with a running activity is never idle — so every "on idle" hook is silently unreachable for it.** This one root cause produced three separate user-reported bugs in four days (medic wedging, dry unit aiming forever, soldier never rearming). When a behaviour "just never fires", check whether something upstream is reporting Attacking forever because `CheckFire` declines silently.
- **A bug that cannot fire looks exactly like a bug that is not there.** Before concluding a fix worked, confirm the failing path was reachable in the run that passed.
- **A measurement can come back negative and that is still the measurement succeeding.** The `[danger] reference` read was requested to license a claim; it refused the claim instead. Do not re-run it hoping for a different number — act on what it said about the unit.

---

## USER BATCH 2026-08-15 — items 63–65, from the user watching live `@experimental` play **[ALL THREE IN FLIGHT]**

> **What this block is.** Three items the user raised on 2026-08-15 from live observation, dispatched the same turn they were reported. **These are the current top user priorities** and they outrank the 08-13 batch below. Listed in the order the user gave them.
>
> **63 and 64 are both bot behaviour but are NOT one item and must not be merged:** 63 is procurement (what the bot buys and in what order), 64 is what units do after they arrive. They are being worked on separate branches for exactly that reason. **65 is unrelated to both** — a projectile/impact defect on a decoration actor.

## USER BATCH 2026-08-13 — items 56–61, from the user watching a live bot-vs-bot (`@experimental`) match

> **What this block is.** Six items the user raised on 2026-08-13 while watching an `@experimental` bot-vs-bot game. **These are OBSERVED BEHAVIOUR, not theory**, and they are **current user priorities — they outrank the close-out intake below**, which is 22-hour-old inherited residue. Listed highest-priority first, in the order the user gave.
>
> Every "next step" below was checked against the code at `main @ dc899995` before it was written. **Two of the six turned out to be materially narrower than the report implied** (58 and 60) — in both cases most of what the user asked for already exists and only one piece is missing. Those are called out, because an item that overstates its own scope sends someone at the wrong file.
>
> **56 and 57 are separate items and must not be merged: one is procurement (what the bot buys), the other is delivery behaviour (what the truck does once bought).** **60 and 61 are the same subsystem and should be done together; both are chrome/UI only and touch no simulation, so they can run in PARALLEL with the gameplay items rather than queueing behind them.**

## CLOSE-OUT INTAKE 2026-08-12 — items 42–54, folded in from nine archived manager sessions

> **What this block is.** Nine manager sessions were archived on 2026-08-12. Their reports live in [`WORKSPACE/closeout/`](../../closeout/) (index: [`closeout/README.md`](../../closeout/README.md)), committed at `9d11d72f`; **every report validated its own claims against `main` @ `35876332`**, and nothing but documentation has landed since. Items 42–54 below are their open work, transcribed with the file:line specifics the reports state. Each item names the report it came from so a claim can be traced back.
>
> **On ordering — read this before reordering.** These items are ranked **within this block only**, by what the reports themselves say about severity and dependency. **Item 40 keeps `[NEXT]`**, and this block asserts nothing about where 42–54 rank against pre-existing items 34, 35, 39 and 41. That ranking is a manager call that has not been made; making it up here would have been inventing priority. The one dependency that IS stated by the sources is preserved in item 43 and honoured in this block's order.
>
> **Where the reports disagree, the entry says so** rather than picking a winner. There is one substantive numeric disagreement (item 43) and one attribution correction (item 42).
>
> ---
>
> ### RECONCILED 2026-08-13 — `main @ dc899995`, 55 commits and 19 merges after the `35876332` validation baseline
>
> **This block was written against `main @ 35876332` and validated there. It is no longer true as written.** Every item 42–54 has been re-checked by reading the code or the merge commit, not by matching titles. **Per-item verdicts, each citing the commit that justifies it:**
>
> | Item | Verdict | Justified by |
> |---|---|---|
> | 42 | **PARTIAL — rewritten, it was actively wrong** | `c440906e`, `91056894`, `476ddf33` |
> | 43 | **OPEN, debt GREW — five new contributors** | `91056894`, `16eca8e8`, `ddcc5d6c`, `153ab8e6`, `fffad21e` |
> | 44 | **(a) DONE / (b) OPEN, shape changed** | `16eca8e8` / `f910ac7d` |
> | 45 | OPEN — untouched, now in flight on `wt/missile-trace` | zero commits on `Missile.cs` |
> | 46 | OPEN — untouched | — |
> | 47 | retired, unchanged | `25396a33` |
> | 48 | **PARTIAL — onboarding half DONE** | `dd6171cd` |
> | 49 | OPEN — one adjacent check struck | `484eb913` |
> | 50 | OPEN — untouched | zero commits on `rules/weapons/` |
> | 51 | OPEN — untouched, one new caveat | `d0a23b0d` |
> | 52 | **DONE** | `4d3c8f90` |
> | 53 | OPEN — untouched | — |
> | 54 | **PARTIAL — two of eight lines DONE** | `25396a33` |
>
> **Also caught, and it predates this block's own baseline:** item **40**'s stage (a) was tagged `IN FLIGHT` while it had already merged at `ddcc5d6c` on 2026-08-11. It was stale when this block was written, not because of it.
>
> **Found in the log and represented nowhere in this queue — filed as item 62.**
>
> **Method note earned here:** every verdict above rests on a path-scoped `git log 4d3c8f90..HEAD -- <path>` returning empty, or on reading the diff. **Four items were confirmed OPEN by an EMPTY log rather than by argument**, which is the cheaper and stronger check — and it is what stopped item 50 and item 53 being closed on the strength of adjacent-sounding commit titles.

### LIVE-PLAY BATCH 2026-08-08 — seven observations from the user, testing `e79ddd97`
_Captured verbatim in substance so nothing is lost. Binary confirmed current with the merge (`OpenRA.Mods.Common.dll` stamped at the merge minute), so all seven are observations of MERGED code._
**Status 2026-08-11:** of the five queued items, **36, 37 and 38 shipped** (`9ab1b2e2`, `dd3430a8` — moved to SHIPPED). **34 and 35 remain open**; both gained a recon that reshapes them but neither has any implementing commit.
- **[MERGED `bd3abacf`]** _"Attack/capture the POI more — everything routes to the centre while the sides are unprotected and full of capturable POI; a flanking group is constantly ordered back, then forward, stuck in a loop; bots need to commit."_ → **Already root-caused 2026-08-03**, now shipped: `SectorPostureHold` vetoes any axis whose target sector reads `sectorOwn ≈ 0`, which is every deep or flanking axis on every map, and the hold orders to a receding `stagingAnchor ?? rallyCell` — i.e. it marches the flankers home. Three candidate fixes recorded in DISCOVERIES. Branch `auto/posture-veto`.
- **[ROOT-CAUSED, MUCH BUILT, STILL NOT DEMONSTRATED FIXED]** _"Supply truck still ordered forward then back, while out-of-ammo soldiers auto-return to it as nearest resupplier."_ → Diagnosed from the user's live `debug.log` (`WORKSPACE/recon/260809-truck-loop-from-live-log.md`): `EvacDangerThreshold: 60` was an RA-era constant against a field returning ~66,834, so trucks parked at home read as "in danger" every other scan → ~48 s / 12.5-cell oscillation, exactly `EvacRetreatCells: 12`. Underneath it, two deeper arithmetic bugs (int overflow, then the `WeaponThroughput` formula) — all three fixed and merged at `5642d931`. **Five further merges have since attacked the same loop from five different angles**: closing the open control loop (`e79ddd97`), damping the residue latch and follow Move (`574a3c08`), replacing the chase with a static drop (`8d0ff18b`), incumbency/dwell at the order funnel (`0eef99d6`), and the 08-10 direct-to-main run (crate anchored to the platoon, errand destination frozen, fleet sized from starving customers, danger picking drop-and-leave vs serve-in-place). `test-supply-far-front-reached` now passes for the first time (`377085db`). **But the `[danger] reference` read that was set as the evidence gate came back NEGATIVE (`084367b0`)** — `evacLevel` 1,706 against median cells of 27,919 / 94,010, i.e. the evac guard still trips unconditionally once contact exists. **This has been declared fixed to the user three times. There is still no fourth declaration to make.** Recon docs `260808-truck-post-fix-behaviour.md`, `260809-truck-loop-from-live-log.md`; live artifact `WORKSPACE/supply-doctrine-260810.html`.
