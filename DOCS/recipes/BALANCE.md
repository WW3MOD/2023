# BALANCE — stat dashboard + AUTOTEST-driven tuning

**Trigger:** `BALANCE <unitA> <unitB>` for a duel comparison; `BALANCE <topic>` for broader tuning (e.g. `BALANCE artillery range falloff`, `BALANCE ammo costs T5`).

**Gives you:** data-driven tuning instead of vibes-based YAML edits. Three tools, clean separation:

| Tool | What it answers | Source of truth |
|---|---|---|
| `tools/combat-sim` (dashboard) | "What does this unit/weapon look like? How does it compare?" | live YAML via `--dump-balance-json` |
| `tools/autotest/run-test.sh test-balance-*` (AUTOTEST) | "Who wins this fight? At what HP? How fast?" | the engine itself (in-game scenario) |
| `--composition-plan` (utility command) | "What will the bot BUY, in what order, and what changes that?" | the shipped `UnitBuilderBotModule` path, replayed — see below |

Stat drift between dashboard and game = structurally impossible (the dashboard reads the engine's resolved Ruleset). Combat-outcome drift between dashboard and game = N/A, because the dashboard never simulates combat.

**When *not* to use it:** balance changes you've already decided on with high confidence and just want to apply. Just edit the YAML.

---

## What I do

### 1. Refresh stats (after any YAML edit)

```bash
./tools/combat-sim/scripts/dump-stats.sh
# → tools/combat-sim/data/stats.json regenerated from live YAML
```

It needs a built tree (`engine/bin/OpenRA.Utility.dll`; `make all` / `.\make.ps1 all` first) and a `python3`/`python` for the post-dump sanity check, and refuses to replace a good `stats.json` with an invalid one. The **dashboard** warns at startup when `stats.json` is older than any rules YAML (`isStale` in `tools/combat-sim/src/data.ts`).

### 2. Inspect stats with the dashboard

`build/` is gitignored, so a fresh checkout has no dashboard until it is compiled:

```bash
cd tools/combat-sim
npm install && npm run build               # once per checkout (tsc → build/)
node build/index.js units                  # list combatant actors
node build/index.js compare abrams t90     # side-by-side
node build/index.js actor abrams           # full stat dump
node build/index.js weapon tankround.abrams
node build/index.js dps abrams             # sustained DPS, dmg/credit
node build/index.js tier-cost              # cost-vs-power table
```

Use these to:
- Find stat outliers (cost too high, DPS too low for the cost class)
- Confirm symmetry between faction counterparts
- Compute derived metrics without re-deriving by hand

### 3. Verify with AUTOTEST

For "who wins?" / "how fast?" / "what HP%?" use the in-game test harness. It runs the actual engine, so it catches everything the dashboard's static math can't (positioning, autotarget jitter, projectile travel, suppression, AI behaviour). `ls -d tools/autotest/scenarios/test-balance-*` lists the duels.

```bash
./tools/autotest/run-test.sh --hidden --seed 1017 test-balance-tank-1v1
./tools/autotest/run-batch.sh --seed 1017 test-balance-tank-1v1 test-balance-ifv-1v1 ...
```

**These are launches, and a batch is a multi-run**: who may run them, and the go-ahead a batch needs, are in CLAUDE.md §"Who runs what". A worker dispatched by a manager names the duels and the result line it expects, and hands them up. Run the duels **by name** — their verdict comes from the shared `mods/ww3mod/scripts/balance-helpers.lua`, which `run-batch.sh --all`'s per-scenario Lua grep never reads, so `--all` excludes most of them (filed in `WORKSPACE/bugs/discovered.md`).

Each test reports `WINNER=X | ttk=Ys | survivors=N/M | hp=H/MAX (P%)` (`balance-helpers.lua`; `ttk` uses `TestHarness.TicksPerSecond`). **A run is reproducible only with `--seed N`**: without it the seed comes from the clock (recorded in `result.json`), so two unseeded runs are two different fights. For a before/after comparison pass the same seed to both arms; for variance work, sweep seeds or add tests at several ranges.

### 4. Recommend tuning, then re-test

Write the proposed YAML edit, apply, **re-run dump-stats.sh** (the dashboard would otherwise lie), then re-run the relevant `test-balance-*` at the same seed to confirm the change lands where intended.

---

## The third instrument: `--composition-plan` (what the BOT will buy)

The two tools above answer "what is this unit" and "who wins this fight". Neither answers **"what will
the bot actually build, and in what order"**, which is the question behind most procurement tuning. That
is a utility command, replaying the shipped `UnitBuilderBotModule` code path — the deficit argmax and its
tie-break, `UnitLimits`, `UnitDelays`, `UnitFloors` and their `UnitFloorPer` scaling, the supply-fleet
pre-empt, and with `--cash` the affordability filters and the banking gate.

```
--composition-plan [--faction america|russia] [--cycles N] [--start <class>] [--attrition N]
                   [--floor-per N] [--cash N] [--income N] [--no-bank] [--supply-floor-per N] [--verbose]
```

Two defaults decide what a run means, so state them when you quote a number: **the budget is UNLIMITED
unless `--cash` is given**, and **nothing dies unless `--attrition N` is given**.

### `--floor-per` and `--supply-floor-per` are DIFFERENT KNOBS, and confusing them looks like a weak signal

`--floor-per N` rewrites **`UnitFloorPer`** (`DumpCompositionPlanCommand`, `floorPerOverride`), which
drives the support floor. The supply truck's standing floor is a **separate field**, `SupplyTruckFloorPer`
(overridden by `supplyFloorPerOverride`), read directly by the demand pre-empt. So sweeping `--floor-per` moves the truck's line only
*indirectly*, through the support type's effect on composition — **which reads as a weak-but-real
response and is nothing of the kind.** A sweep of 8/10/12 that way returned 19%/19%/20% and was one step
from being reported as "the truck ratio barely matters". `--supply-floor-per N` exists so the truck ratio
can be moved without a YAML edit.

The reason this is worth a paragraph rather than a footnote: **the misreading is directionally
plausible.** With the economy modelled the truck ratio matters far *more* than the budgetless model
showed — at `--attrition 40`, dry for per = 8/10/12/14 is 6%/7%/9%/9% budgetless but **14%/25%/52%/41%**
with `--cash 20000`, a 3-point spread becoming a 38-point one.

**That last figure is explicitly NOT a mandate to re-tune.** The sweep is non-monotonic (12 worse than
14), which is the signature of an artefact in the metric rather than a clean optimum — and re-tuning a
shipped constant against an instrument built in the same session is precisely how the two constants
before it had to be re-derived. **Build the instrument, land it, and take the tuning question in a
separate pass with the instrument already on the shelf.**

---

## Deriving a scale, rather than picking one

For anything that ladders — yields, radii, sprite sizes, cost tiers — the difference between a law and a
pile of tuned constants shows up late and expensively. Four rules, each earned on this arsenal.

### Anchor on ONE thing, then VALIDATE against a SECOND that was never consulted

Derive the law from published data, anchor it on one quantity already in the mod, then **test it against
a second quantity nobody consulted while deriving it.** If the second agrees, the scale is a real
property of the mod and any disagreement is in the labels. If it does not, the scale is a fit and should
not be trusted outside the point it was anchored on. **Fitting to both points teaches you nothing, and it
is the tempting thing to do.**

Worked example (the figures in this section come from that derivation and were not re-measured for this recipe; `dotnet test engine/OpenRA.Test/OpenRA.Test.csproj --filter NuclearYieldTest` checks the shipped weapons against the law): fitting the standard Glasstone optimum-airburst overpressure table to a single power
law and converting at the mod's blast scale gave `R_cells = 10 * Y[kt]^(1/3) * P[psi]^-0.589`, which
reproduces the whole published table. The check that mattered came after: `Atomic`'s blast radius and
`AtomicHighYield`'s were chosen **years apart by different reasoning** — one to feel tactical, one to
cover the largest shipped map — and run through the law they land on the **2.74 psi** and **2.67 psi**
contours, a ratio of 6.80 against the predicted 6.69. **1.6%, with nothing tuned to make it happen.**

That is also what let a wrong *label* be corrected without touching a right *number*. The brief called
one radius the "1 psi outer edge"; it is not. But because the two radii cross-validate at 2.7 psi, the
figures were demonstrably right and only the pressure they were labelled with was wrong. **Without a
second independent point there is no way to tell those two cases apart**, and the honest move would have
been to change the radius.

### Two effects of one cause that scale with DIFFERENT exponents must not be retuned by one factor

Because the retune does not approximate the physics — **it deletes a sign change.**

`AtomicHighYield` was built by multiplying every one of `Atomic`'s radii by `40^(1/3) = 3.4`: one factor,
applied to blast, thermal, fire, EMP, suppression and smudge alike, documented as the Hopkinson-Cranz
cube-root law. The cube root is right *for blast* and wrong for everything else on that list. Thermal
radius scales as roughly `Y^0.41` against blast's `Y^0.33`; the difference sounds like rounding, but they
are **exponents**, so the ratio between the two radii runs as `Y^0.08` and **passes through 1** — at
~437 kt on this mod's scale. Below it a weapon breaks things further than it burns them; above it the
order inverts. At derivation time: 20 kt gave 15.1 cells of blast against 11.9 of thermal, and 6 Mt
101 against 124. Under the single factor both weapons sat on the same side of that line, so the strategic
weapon was just the tactical one drawn bigger — the qualitative difference that made it worth having had
been scaled away.

**How to spot it elsewhere: look for a comment naming ONE scaling factor above a table of quantities that
all move by it.** The tell is not sloppiness — that table was written out in six rows and read as
unusually rigorous, which is why it stood as long as it did.

### When a model has a transition POINT, make the point the parameter

A rate that happens to put the transition in the right place is the same information **one integration
away from being checkable**, and it becomes per-weapon tuning the moment a second weapon needs it.

The shockwave's supersonic phase decayed geometrically per tick, each weapon carrying its own
`SpeedDecayPercent` derived so the front hit Mach 2 where its own overpressure law puts 51 psi. The ratio
of the two decay times was itself the cube-root-of-yield scaling, which made the pair look thoroughly
principled. Integrate them and the front returns to sound speed at **5.0x the fireball radius on one
weapon and 3.6x on the other** — both meant to encode one physical transition (breakaway, at roughly
twice the fireball radius). Neither is 2, and they are not each other, so the quantity the two constants
jointly expressed was not a property of the model at all.

**A geometric decay cannot express a transition, structurally:** it is asymptotic, so "where does the fast
phase end" has no answer, only a convention applied to a curve whose position depends on the whole
integration. Replacing the rate with a `TransitionRadius` of `2 * StartRadius` lets every nuclear shockwave weapon
share one rule (`grep -c 'TransitionRadius:' mods/ww3mod/rules/weapons/*.yaml`) with no per-weapon constant left to derive, and turned the invariant into something assertable —
every step at or past that radius is exactly the sonic step.

*(A linear-in-radius ramp is not a cop-out here: over the one octave from `StartRadius` to twice it, the
straight line between the endpoints is the CHORD of the `R^-1.7` curve — they agree to within a percent
at the midpoint and are never more than ~0.12 apart. The chord costs nothing in fidelity over that
interval and lands on zero at a finite radius instead of trailing a supersonic tail to the map edge.)*

### An exponent that is right for quantity A is not evidence for quantity B

And the failure is **invisible until the range gets long**. The mushroom cloud's `ScalePercent` borrowed
the FIREBALL's `Y^0.40`. Over 0.3 kt to 50 Mt — 166,667x of yield — that is 123x of sprite, which put the
largest weapon at a cloud 848 cells wide: more than six times the largest shipped map. So the two biggest
weapons were pulled off the curve individually, each with a sensible-sounding local reason. Sorted by
yield the ladder then read **1277, 1542, 700, 1600** — the 6 Mt weapon drawing a smaller cloud than the
1.2 Mt one.

Over one decade the two laws differ by a factor nobody notices; over five, one of them lands off the map.
The cloud is not the fireball — it stops growing spherically at the tropopause — so it never had to share
the exponent, and **the moment two values needed hand-capping to stay usable was the moment to refit
rather than to cap.**

**The user found this by playing, not by reading, and reported it as the weapons being "modeled
differently".** Sorting one column by yield would have shown it instantly, and nothing in the repo sorted
that column: the tests checked each weapon against its own law, **which is precisely the check a
hand-capped value is exempt from.**

### So assert MONOTONICITY across the sorted ladder as well as each value against the law

They are different assertions and they fail on different bugs. A per-weapon law check passes on a ladder
with a hole in it when the hole is a deliberate exception; **a monotonicity check on the same data catches
every hand-tuned exception that inverts the order, and needs no knowledge of what the law is.**
`NuclearYieldTest.EveryNuclearCloudIsOneSpriteOnTheYieldLaw` does both, plus a third neither implies —
that the sprite is drawn exactly once, since a correct size says nothing about how many times it is
composited. The per-quantity form of this rule is at
[`conventions.md` §"Every quantity checked against its own law"](../reference/conventions.md).

---

## When data conflicts with feel

Dashboard says symmetric, AUTOTEST says symmetric, but the user's playtest says lopsided? **Trust the playtest** — the harness is missing context (positioning, fog, AI quirks, multi-unit dynamics). File as a TRIAGE item and dig in.

If dashboard says X, AUTOTEST says Y: that's an interesting finding — the engine's combat math (damage formula, AutoTarget priority, hit calc) is doing something the static stats don't predict. Worth investigating; usually points at a non-obvious engine path.

---

## Why the dashboard does not simulate combat

A re-implementation of the engine's combat math and a hand-copied stat table both drift from the real YAML, and a drifted sim gives confident wrong verdicts. So neither is re-implemented:

- **Stats**: dumped from the engine via `--dump-balance-json`; the dashboard reads the JSON.
- **Combat outcomes**: AUTOTEST runs the engine itself.

The retired `run` / `duel` / `list` verbs in `tools/combat-sim/src/index.ts` are what is left of the old simulator.

The dashboard's only computed numbers (DPS, dmg/credit) are derived directly from the dumped stats using simple cycle math (`burst × damage / cycle_ticks`). No engine fidelity required for that — it's a presentation layer.
