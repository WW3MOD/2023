### 86. The ambush lane takes 2 of 3 units at the opening and leaves offense below its own floor

`[DOCTRINE RULING NEEDED before any code — every candidate fix is on a trait live on BOTH profiles at match opening]`

**Perceived:** in a small opening army the bot's only tank walks 22 cells forward as half of an "ambush pair" and dies there while the rest of the army never leaves the Supply Route.

**Source:** rendezvous diagnosis, worker 37fdcca8, CONFIRMED by run `260906_091912_p10120_test-combined-arms-rendezvous` (item 64 dossier, 2026-09-06). Filed at `main @ e8e57ada`.

---

#### What the log proves (main @ b6207b9b tree)

`[exp-staging] hold-under-min pool=1 min=2` — offense holds the tank correctly; then at t200 `[exp-ambush] lane … post=28,12 units=2` and `[exp-ledger] free=1 held=2 by=ambush:2`: `LaneAmbushBotModule` takes two of the three eligible units (the tank among them) and posts them 40% of the way to the enemy SR, with **zero `retire` lines** all run. The tank walks 8,16 → 20,14 and dies at t585 (killer unlogged — hypothesis: Russia's 2-unit flank via 32,19). `ai.yaml:1046-1047` predicted it verbatim: *"at the opening the lane takes the entire army"*. `MinUnitsPerAmbush: 2` (bb89f9fd) turned one lone forward unit into a forward pair — it did not remove the behaviour.

#### Candidate rulings (pick one; each is a doctrine change)

(a) **Army-share reserve** — the lane may not take units while offense sits below `FreePoolMinAdvanceUnits`; (b) **danger-aware post cell** — no post beyond a believed-danger threshold; (c) **losing-lane retire** — a lane whose units take damage without contact retires. (a) is the smallest and directly answers the symptom.

**Measurement:** `test-combined-arms-rendezvous` — the tank alive at the rendezvous (scenario now traces positions every 100 ticks and prints the tank's last cell on death, 26aea66a). Related: the ambush block (items 67–71) is USER-GATED — this item is about the lane's *share* at the opening, not about ambush itself.

---

#### Status log

**2026-09-21 — IMPLEMENTED on `wt/item86-lane-share`, base `main @ eacc1cff`. Awaiting the measured run.**

Ruling (a) **army-share reserve** only. Nothing from (b) danger-aware post cell or (c) losing-lane retire.
Item 64's `RendezvousWithOffensiveStaging` flags (`ai.yaml:2258` / `:2353`) are **untouched** — that is a
separate un-run behavioural change and the standing rule is one per branch.

| | |
|---|---|
| field | `LaneAmbushBotModuleInfo.OffenseFloorReserveEnabled` |
| C# default | `false` — the ungated behaviour, per the architecture rule for a trait live on both profiles |
| `@experimental` | `true` (`ai.yaml:1093`) |
| `@stable` | `true` (`ai.yaml:3227`) — **deliberate, and it MOVES `@stable`**; re-take the ai-bench baseline |
| pure helper | `AmbushLaneMath.ReserveAllowance` (6 args, `int.MaxValue` = unbounded) |
| new offense surface | `PoiOffensiveBotModule.TryGetFreePoolSnapshot` + `EffectiveFreePoolMinAdvanceUnits` |
| NUnit | 9 new tests in `AmbushLaneMathTest.cs` |

**"Would be left below the floor", precisely.** The lane may take `k` units this eval only while
`offenseFree − k ≥ offenseMin`, so the allowance is `max(0, offenseFree − offenseMin)` — **one allowance for
the whole eval**, spent across lanes in the order they are filled, so two lanes share the army's spare units
rather than each taking the full allowance.

Every term is the offense's own, read through the module, never re-derived:

- `offenseFree` — `PoiOffensiveBotModule.TryGetFreePoolSnapshot`, published from inside `BuildFreePool` at
  `PoiOffensiveBotModule.cs:2449`. That is the list whose `.Count` becomes `ordered.Count` at
  `StageFreePool` (`:2877`) and is compared against the floor at `:2952`.
- `offenseMin` — `EffectiveFreePoolMinAdvanceUnits`, i.e. `Info.FreePoolMinAdvanceUnits` (`:640`, set to `2`
  on both profiles at `ai.yaml:772` / `:3150`) **or 0 when `ForwardStagingEnabled` is false**, because the
  field's own `[Desc]` says it is only read when staging is on. A consumer reserving for a floor nobody
  applies would withhold for nothing.
- `offenseAxisLive` — `axes.Count > 0`, published alongside the count. `ForwardStagingMath.FreePoolMayAdvance`
  waives the floor whenever an axis exists, so the reserve waives too. **This is what aims the change at the
  opening**: once an axis is live the lane recruits exactly as it does today.

**Why a published snapshot rather than calling the offense's pool builder.** `BuildFreePool()` is not pure:
it calls `PruneStandoffMemory()` and, via `StoodOffForTransport`, **writes `standoffSince[a] = tick`**. A
consumer calling it to count would latch offense's transport-standoff clocks on the *consumer's* cadence and
re-emit the once-per-tick `[exp-ledger]` census at a foreign tick. The snapshot is recorded inside the
existing computation and reaches nothing.

**Composition with `MinUnitsPerAmbush` (item 64) is the fix.** The reserve caps *availability* before
minimum manning tests it (`takeable = min(free.Count, allowance)`), so a lane the reserve can only part-fill
is then refused by manning as under-manned. On the measured opening — three eligible, floor 2 — the
allowance is **1**, `LaneMayPost(0 + 1, 2)` is **false**, and **nobody is posted**. Unbounded allowance ⇒
`takeable == free.Count` and both lines read exactly as before.

**FOLLOW-UP, NOT FIXED HERE (recruit order).** The reserve bounds *how many*, never *which*. Recruits are
still ordered by proximity to the post cell alone (`LaneAmbushBotModule.cs:389-393`) with no preference
between a rifleman and the army's only MBT, so **a pool large enough to clear the floor can still hand the
lane the tank** — e.g. 5 eligible, floor 2, allowance 3: the lane takes 2 and the abrams is nearest the post
under the same reading that put it there in run 260906_091912 (it spawns at `8,16`, east of the SR at
`6,16`, while purchased units spawn at `3,16`–`5,16`). That is a second behavioural change and belongs on
its own branch and its own run. **Worth noting it is exactly the case ruling (a) was chosen as the "smallest"
answer to, so this is a known residual, not a regression.**

**Over-reserve, bounded and deliberate, in two places.** (1) A unit the lane may take that the offense's
predicate would *not* count (role/exclusion differences, or one stood off for transport) still spends
allowance. (2) A unit SHED from an earlier lane this eval was ledger-committed at snapshot time and so was
never in offense's pool, yet is charged if another lane reclaims it. Both err toward offense, which is the
direction every other gate on this module fails in; making either exact would need the offense pool as a SET
rather than the count its own floor test is decided on.

---

#### Status log — 2026-09-22: first measured run, reserve did NOT bind, two defects found and fixed

Run `260922_005229_p43692_test-combined-arms-rendezvous` at `47ff7fd8` (GREEN arm, flag on) and
`260922_005355_p44806` (RED arm, flag off). **Both FAIL, and they are indistinguishable** — the reserve did
not bind at the eval that mattered.

**WHICH CLAUSE LET THE LANE THROUGH: the axis waiver**, `if (offenseAxisLive) return int.MaxValue;`. The
sequence, `debug.log`, USA-bot:

```
t18   [exp-ledger] free=1 held=0            [exp-staging] hold-under-min pool=1 min=2 axes=0
t100  [exp-ambush] reserve allow=0 free=1 taken=0 offense-free=1 min=2 axes=0 lanes=0   ← reserve WORKED here
t118  [exp-ledger] free=2 held=0 by=none
      [exp-offense] axis-new target=supplyroute#7 cell=58,4 · order units=2 distToTarget=53
      [exp-offense] reeval pool=2 free=0 targets=1 axes=1 k=1
t200  (no reserve line) [exp-ambush] lane post=28,12 units=2
t218  [exp-ledger] free=1 held=4 by=ambush:2,offense:2
```

At t118 offense formed an axis **out of its entire two-unit free pool** and the eval ended `free=0 axes=1`.
At t200 the lane read that snapshot, hit the axis waiver, and recruited unbounded.

**BOTH published terms were wrong, and each alone was sufficient to cause this.**

1. **The axis waiver is a false analogy and is now DELETED, not narrowed.**
   `ForwardStagingMath.FreePoolMayAdvance`'s axis term answers *"may this late arrival walk to the muster
   ALONE?"* — yes, it is joining a body. The reserve asks *"does offense have units to SPARE?"*, and **an
   axis has CONSUMED units**; its existence is evidence against spare capacity, not for it. Narrowing by
   axis SIZE would not have fixed this run: the t118 axis held exactly **2** units and `EarlyMinAxisSize`
   is **2** (`ai.yaml:380`/`:3064`), so every "below the axis floor" test passes it. The term had to go.
2. **The snapshot was published on every `BuildFreePool` call, last-wins** — so it recorded the
   **post**-axis-claim pool (0 at t118) while `[exp-ledger]` printed `free=2` the same tick. Now published
   **once per tick on the first call**, the same pass the census rides, so `offense-free=` in the reserve
   line is byte-equal to `free=` in `[exp-ledger]` and a log can never show the two disagreeing.

**The reserve line now prints on EVERY eval where a lane wanted units**, carrying
`waived=off|no-offense|no-floor|unknown-pool|none`. The silence at t200 is what had to be reconstructed from
three other modules' lines; an absent line must mean "no lane wanted units", never "a decision was taken and
not recorded".

**DOCTRINE CONSEQUENCE, stated because it is a real change and not a bug:** with no waiver the lane recruits
only while offense's free pool **exceeds** its floor. In this scenario `[exp-ledger]` reads `free=2` against
`min=2` at t218/t318/t418/t518, so **the lane would not post at all for the whole match**. That is ruling (a)
applied literally.

---

#### CORRECTION — "the tank dies because the lane took it" is FALSE in this scenario

**My 2026-09-21 GREEN criterion ("only a tank death at ~20,14 is an item-86 regression") was wrong, and the
manager's reading of run 260922_005229 inherits the error.** The tank in this scenario is **offense's**, not
the lane's. Two independent routes, both from `debug.log`:

- **Timing.** The tank moves `8,16@t100 → 11,16@t200`, and the *only* forward order issued to any USA unit
  in that window is `[exp-offense] order … units=2 … distToTarget=53 tick=118`. The lane held `lanes=0` at
  t100 and issued nothing until t200.
- **Census.** `[composition] census tick=80` gives USA `abrams=1 ar.america=1 bradley=1 e3.america=4`; the
  bradley is the carrier and the four `e3` are transport-claimed, so **the only two combat units in the world
  at t118 are the abrams and one `ar.america`** — exactly the `pool=2` the axis took whole. With
  `AxisCommitmentTicks: 250` that commit is live through t368, and the lane's `BuildFreePool` excludes every
  ledger-committed actor, so **the lane could not have taken the tank at t200 even unbounded.**

The lane's two recruits at t200 were later arrivals (`at.america` / `ar.america` / `tl.america`; the census
grows to 5 combat units by t200). **The reserve's one binding moment in this run was t100, where it worked:
`allow=0` withheld the tank from the lane.** Offense then claimed it at t118.

**So `test-combined-arms-rendezvous`'s VERDICT cannot gate item 86.** The tank died advancing as half of a
two-unit early-spread axis ordered 53 cells to the enemy Supply Route. The discriminator must be the log.

**FILED SEPARATELY (not item 86, not fixed here): a two-unit `EarlyGameSpread` axis marches the full map.**
`EarlyMinAxisSize: 2` let an axis form at t118 from the entire army and order it `distToTarget=53` to
`supplyroute@58,4`; it reinforced to 3 at t218 and 5 at t318 while its lead element was already at 17,16 and
22,16, and the tank died at t539. This is the same family as item 64's missing lead-hold, on the axis side
rather than the staging side. It is what actually kills the tank in this scenario, in **both** arms.

---

#### Status log — 2026-09-22 (second pass): the reserve WORKS; item 86 gets its own scenario

**The reserve binds exactly as designed.** Run `260922_012732_p56822` at `616a2e0c`, USA-bot:

```
t100  [exp-ambush] reserve allow=0 free=1 … waived=none lanes=0
t200  [exp-ambush] reserve allow=0 free=2 offense-free=2 min=2 axes=0 waived=none lanes=0
      … lanes=0 / taken=0 at every eval through t500; the ledger NEVER shows by=ambush
```

**The lane is exonerated.** The verdict was still `FAIL` (tank dead t542 at `21,16`) for a reason that has
nothing to do with this item — see the handoff below.

**The old scenario cannot judge item 86 IN EITHER DIRECTION on this opening.** The RED arm
(`260922_012930_p58667`, flag off) reads `allow=inf … waived=off lanes=0` and **`free=0`**: offense had
already absorbed the whole pool into its axis (`[exp-ledger] free=4` at t189 → axis), so the lane posted
nothing *with the reserve switched off too*. Same verdict, same outcome, both arms. **A control that cannot
fail differently from its test is not a control.**

#### NEW SCENARIO: `tools/autotest/scenarios/test-ambush-lane-share`

Asserts the item directly and reads the same numbers the reserve decides on, through three new test-mode
bindings (`TestGlobal.cs`): `Test.GetBotLedgerHeld(p, "ambush")`, `Test.GetBotOffenseFreePool(p)`,
`Test.GetBotOffenseAdvanceFloor(p)`. **Verdict: units held under an `ambush:` commitment must stay at ZERO
while offense's free pool is at or under its advance floor**, sampled every tick to t500.

**The opening is CONSTRUCTED, not hoped for** — that is the lesson of the two runs above. `rules.yaml`
overrides `PoiOffensiveBotModule@experimental` with `MinAxisSize: 40` / `EarlyMinAxisSize: 40` /
`FreePoolMinAdvanceUnits: 40`, so `PoiOffenseMath.DesiredAxisCount` returns 0 (`totalUnits < minAxisSize`),
**no axis ever forms**, and offense sits permanently under its own floor holding its units in the free pool —
precisely the state ruling (a) governs and the state the lane used to raid. 40 is set against the *measured*
trajectory on this map and cash (5 combat units by t200, 9 by t500), not guessed.

Two `abrams` at `8,16` and `8,18`. Eligibility is **derived, not assumed**: `abrams` inherits
`^AutoTargetMBT` → `^AutoTarget` (`vehicles-america.yaml:469`, `defaults.yaml:457-458`), which carries both
`AmbushTacticsCondition` (`:430`) and the `ExternalCondition@ambushtactics` seam (`:454-455`), so
`CanHostAmbush` is true and the OBS-1 `^AutoTargetGround` exclusion does not apply; its role is `MainBattle`
(`UnitRoleResolver.cs:380-382`), which `UseUnitRoles` admits. Two, because `MinUnitsPerAmbush` is 2 — one
would be refused by minimum manning and the RED would post nothing for the wrong reason. Both cells are
**proven vehicle-passable** (they held the abrams and the bradley in every rendezvous run on this map.bin).

**Three ways it refuses to produce a false green**, all returning SKIP rather than PASS:
1. the floor is read back and must equal 40 — if the `rules.yaml` block ever stops merging (renamed trait,
   changed `@suffix`, the MiniYaml case trap) the override is silently inert and a green would be measuring
   the shipped default;
2. offense must at some point hold a pool `>= MinUnitsPerAmbush` **and** `<= floor`, or the reserve was never
   asked the question;
3. offense must have published a free pool at all (`-1` is information, not zero).

**Verified without launching:** `make lua-gate` resolves all three new bindings against `TestGlobal`, and a
deliberate sabotage (`Test.GetBotLedgerHeldXYZ`) makes the gate name this file and line with exit 2, restored
clean — so the gate demonstrably parses this scenario rather than skipping it.

#### HANDOFF TO ITEM 64 — the residual lone tank is the OFFENSE AXIS, not the lane

`test-combined-arms-rendezvous` still fails, and this is what it is measuring. From `260922_012732`:

```
t120  [exp-offense] axis-new target=supplyroute#7 cell=58,4 action=Pressure
      [exp-offense] order … action=Pressure units=2 cohesion=Spread distToTarget=53
      [exp-offense] reeval pool=2 free=0 axes=1
t220  [exp-offense] hold units=2 · reinforce-held joined=3 units=5
t320  [exp-offense] reinforce-held joined=2 units=7
lua   tank 8,16 → 11,16 → 17,16 → 21,16 (t100–t400), dead t542
      carrier with all four riflemen sat at 8,18 for the whole run
```

**Offense forms a two-unit axis out of its entire pool while sitting at its own floor, and orders it 53 cells
to the enemy Supply Route while the ferry never departs.** `EarlyMinAxisSize: 2` is what lets the axis form;
the axis then reinforces to 5 and 7 *behind* a lead element already 13 and 17 cells out. That is item 64's
missing **lead-hold**, on the axis side rather than the staging side — `FreePoolMinAdvanceUnits` gates the
free pool's departure and nothing gates the axis's. **Item 64 owns it. Not fixed here, and not to be fixed on
this branch.**
