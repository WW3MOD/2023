# Truck loop, diagnosed from the user's live play log (2026-08-09)

**Repo state:** `main` @ `0eef99d6`, 0 commits behind `origin/main`. Read-only session — no build, no
simulation, no autotest.

**Primary evidence:** `%APPDATA%\OpenRA\Logs\debug.log`, **3.14 MB, 25,318 lines**, final write 02:47
today. Single world, ticks monotonic 0 → **28,628** (~19 min at 25 tps). Bot-vs-bot spectate on *River
Zeta WW3*, scenario `none`:

- **Stable AI 0802** — east, Supply Route at `79,34`
- **Experimental AI** — west, Supply Route at `13,44`

Both have the supply stack live (`debug.log:151-152`): `participates=True dangerField=True evac=True
reroute=True spread=True drop=True` for both; `hunt` only on `@experimental`.

`debug.log.1` (2699 B) is **not** a concurrent second instance — it is from 2026-08-08 07:51 out of the
`worktrees/ww3mod/tank-trap-review` checkout, and contains only mod-load and sprite noise. The known
`.1` harness trap did not fire here.

> ### Method caveat — the log was still being written while it was being read
> OpenRA was **still running and appending** during the first half of this analysis. The file went
> 1.87 MB → 3.14 MB (15,167 → 25,318 lines) across the session, so early passes disagreed with later
> ones purely because they read different amounts of file: an early `grep` found 29 `evac-enter`, a
> later `awk` found 36, and both were correct at the time they ran.
>
> **An earlier revision of this document mis-diagnosed that as `grep`/`wc` under-scanning the file.
> That was wrong and is retracted.** On the settled file `wc -l`, `grep -c ''` and `awk END{NR}` all
> report 25,318 and `grep -c evac-enter` and `awk` both report 36 — the tools agree exactly. **Every
> number below has been re-measured against the final 25,318-line file.**
>
> The real lesson generalises further than the false one did: when reading `debug.log` for a session
> the user may still have open, check `wc -c` twice before trusting any count.

---

## 1. Which loop is the user seeing?

**Loop A family — SR-ward, healthy bar. Not Loop B.** The mechanism, however, is **not** the
approach-abort cycle Loop A was attributed to: it is the **danger-evac branch**, and it is fully logged.

### The cleanest instance in the file — truck 5319, west player

| tick | event | cell | danger | note |
|---|---|---|---|---|
| 16,636 | adopt | `10,46` | — | supply 750 |
| 21,749 | evac-enter | `35,36` | 863,142 | leg `24,41`, sr `13,44` |
| 22,049 | evac-exit | `23,40` | 0 | |
| 22,200 | evac-enter | **`30,36`** | 3,452,576 | leg `19,42` |
| 22,500 | evac-exit | `19,42` | 0 | |
| 23,401 | evac-enter | **`30,36`** | 2,733,285 | leg `21,41` |
| 24,300 | evac-exit | `3,47` | 37 | |
| 27,136 | release | `35,39` | — | supply 7 |

The truck **re-enters evac at the identical cell `30,36` twice**, having been driven back to `19,42` in
between. That is the back-and-forth, on the record, same cell, twice.

- **Heading:** toward its own Supply Route (`13,44`). Every one of the 36 `evac-enter` lines names
  `leg=` within 1–6 cells of its own `sr=`. **No truck is ever sent toward a map edge while adopted.**
- **Bar:** healthy. 5319 was adopted at 750 supply and released 10,500 ticks later at 7 — it was
  delivering the whole time.
- **Period:** 23,401 − 22,200 = **1,201 ticks ≈ 48 s**.
- **Amplitude:** `30,36` → `19,42` ≈ **12.5 cells** — i.e. exactly `EvacRetreatCells: 12`
  (`mods/ww3mod/rules/ai/ai.yaml:831`).

### How that compares to the recorded signatures

`WORKSPACE/bugs/discovered.md:111` puts Loop A at **~30 s / ~23 cells**. The log says **~48 s / ~12
cells**. Same direction and same bar colour — so the discriminator at `discovered.md:114` classifies
this as Loop A — but the period and amplitude do not match, and the amplitude matches `EvacRetreatCells`
exactly. **My reading: this is a third, distinct oscillator that shares Loop A's discriminator.** It is
the danger-evac branch, and the recon's Loop A figures were measured against the approach-abort path,
which is a different thing.

**Loop B is absent.** All **11** `release` lines carry `reason=low-supply` — none `dead`, none
`out-of-world` — and every truck ran its load down to 0–62 before release. No adopted truck went to a
map edge.

> **Caveat the user needs, because it is the easiest possible misread:** after release the truck is
> handed to `DropsSupplyCache`, and under TRUK's `Evacuate` default that means **drive to the map edge
> and sell** — stated in the code at
> `engine/OpenRA.Mods.Common/Traits/BotModules/SupplyFollowerBotModule.cs:443-445`. An *empty* truck
> driving off the map edge is **correct, designed behaviour**, and looks identical by eye to Loop B.

### The whole-match shape

36 `evac-enter` / 32 `evac-exit` over 14 adopted trucks. **Enter→exit is 300 ticks in 18 of 32 cases** (297/301/303
in 5 more) — 300 ticks is exactly `2 × ScanInterval(150)`, which is the *minimum the dwell permits*.
So in ~70% of evacuations, **danger had already fallen below the release level by the first moment the
truck was allowed to re-decide.**

| | n | min | median | max |
|---|---|---|---|---|
| danger at `evac-enter` | 36 | 65 | **66,834** | 3,452,576 |
| danger at `evac-exit` | 32 | 0 | **0** | 38 |

---

## 2. Is drop-and-leave inert?

**No — it fires, but it went dark for the last quarter of the match on the west player, and that window
is exactly when truck 5319 was oscillating.**

- `[supply] drop` — **6** (ticks 6,900 / 7,336 / 8,536 / 11,539 / 15,136 / **28,036**). All `new`, none
  `drop-revoked` / `drop-declined` / `drop-inflight`.
- `anchor-impassable` — **4**; `anchor-impassable-continuing` — **2**; `anchor-recovered` — **4**.

Early episodes at the **east** SR recovered fast (`after=1`, `after=2`, `after=6` scans). The late
episode at the **west** SR did not:

```
19,201  anchor-impassable        sr=13,44 → 33,31 standoff=8 frontier=8 — no anchor this scan
20,549  anchor-impassable-continuing  sr=13,44 consecutive=10 latest=33,31
22,636  anchor-impassable-continuing  sr=13,44 consecutive=20 latest=33,31
26,236  anchor-recovered         sr=13,44 → 41,41 after=24 scans
```

**24 consecutive scans = 3,600 ticks ≈ 2.4 minutes with no drop anchor**, and the descent landed on the
**same cell `33,31` every time** — the deterministic frontier descent re-deriving an unreachable cell,
which is precisely the open question that branch shipped with.

**The timeline closes the loop, in both directions:**

| tick | |
|---|---|
| 15,136 | last drop before the outage |
| 19,201 | anchor goes impassable at `33,31` |
| 21,749 – 24,300 | **truck 5319's oscillation — entirely inside the outage** |
| 26,236 | `anchor-recovered → 41,41 after=24 scans` |
| 28,036 | **first drop after recovery** (`truck=5569 anchor=45,41 load=365`) |

Drops stop when the anchor dies and resume 1,800 ticks after it recovers, with a **3,000-tick hole in
between containing the oscillation**. That is a matched pair of edges, not a coincidence of one.

So the answer is two-part, and both halves matter:

1. **The mode is not inert in general** — 6 drops, the early impassable episodes self-cleared in 1–6
   scans, and it resumed by itself once the anchor came back.
2. **It was inert for the west player for 2.4 minutes**, and during exactly that window the truck fell
   back to the plain follow path, which is what fed it into the evac loop.

The frontier descent re-deriving the same unreachable cell `33,31` for 24 scans is a real bug, and the
log names the cell.

---

## 3. Did yesterday's fixes do anything observable?

**Order-arbitration / follow damping — yes, indirectly.** No truck shows the every-scan destination
churn §3.3 of the census described; the follow-path `RepathThresholdCells: 3` deadband
(`ai.yaml:809`) is in effect and the surviving oscillation is at 48 s, not 9 s.

**Residue dwell (`ResidueConfirmScans`) — cannot be confirmed or refuted from this file, exactly as
predicted.** `discovered.md:120` states the residue latch is uninstrumented, and that is still true: no
line records a verdict flip or a confirmation step. What the log gives is *weak positive* evidence only —
zero `out-of-world`/`dead` releases, and every truck draining to near-zero supply before release, which is
consistent with delivery rather than with a truck latching empty on a full tank. **That is consistent
with the fix working; it is not proof, and it should not be reported to the user as proof.**

---

## 4. The order-gate suppression log

**Structurally impossible in this file. It is not evidence of absence.**

`ordgate` lines are emitted by `UnitLifecycleLogger.LogOrderGate`
(`engine/OpenRA.Mods.Common/Traits/World/UnitLifecycleLogger.cs:396-410`), which returns immediately
unless `enabled`. `enabled` is set only when **both** `TestMode.IsActive` and
`TestMode.UnitLifecycleLogPath` are present (`:144-160`), and the stream is a **separate JSONL file**,
never `debug.log`. `ModularBot` calls it through `lifecycleLogger?.LogOrder`
(`Traits/Player/ModularBot.cs:152`), whose comment at `:147-151` says outright it "self-gates to a no-op
when lifecycle logging is off, so this is free in normal play."

The user's session was normal play. **Nothing about whether `BotOrderDamping` fired in a real match can
be learned from this log, and the same is true of every future ordinary play session.**

**The one line that would settle it**, and it is one line: an unconditional
`Log.Write("debug", $"[ordgate] ...")` roll-up next to the existing `LogOrderGate` call, bounded the same
way the supply channel's `anchor-impassable-continuing` is (emit on the first suppression per
module/reason, then every Nth). Without it, the damper's real-match behaviour is only ever observable
under `Test.Mode=true Test.UnitLifecycleLog=<path>`.

---

## 5. Things nobody had looked for

**`Failed to find closest edge for map cell` × 6, all at ticks 24,650–24,654**, for cells `76,28` and
`75,30` (east side, inland, near the east SR). Source: `engine/OpenRA.Game/Map/Map.cs:1783`, whose own
comment reads *"This shouldn't happen."* — and which then **returns the input cell unchanged**.

Callers include `Activities/RotateToEdge.cs:129,257` (the retire-to-edge path a released truck takes) and
`Traits/ProductionFromMapEdge.cs:110` (reinforcement arrival — the Supply Route call-in). A failure there
means "the closest map edge is your own cell", so a retiring unit told to leave the map is told to leave
by standing still. The east player had **no adopted truck** at tick 24,650 (5465 released at 20,849), so a
released truck on the `Evacuate` path is the most likely subject — but the message carries **no actor id
and no owner**, so this cannot be attributed from the log. Bounded (6 lines, 4 ticks) and not the main
story, but it is a real engine-level "shouldn't happen" firing in ordinary play. Adding the actor id to
that message costs one interpolation.

Also present, cosmetic: missing `b2bomb.shp`, `pip-cloak.shp`, `pip-cover.shp` sprites (14 lines, load
time). One `[exp-route-open] mission FAILED player=Experimental AI`. **No exceptions this session** — no
`exception-*.log` dated 2026-08-09.

---

## 6. The single most probable cause

**The evac danger threshold is on the wrong scale by three to four orders of magnitude, so the evac
branch has no resolution: it fires on the faintest believed contact anywhere in its envelope, including
at the truck's own beachhead.**

`EvacDangerThreshold: 60` (`ai.yaml:830`; engine default `SupplyFollowerBotModule.cs:91`). The field it is
compared against reads a **median of 66,834** at the moment of entry.

**Where that magnitude comes from.** `DangerKernelMath.Compute`
(`Traits/World/DangerFieldLayer.cs:153-175`) sets `intensity = throughput × durabilityWeight /
DurabilityBase × confidence/100`, where `throughput = burstDamage × ThroughputWindow / reload`
(`:521-533`) and `ThroughputWindow = 100` (`:203`). **WW3MOD weapon damage values are 10³–10⁵** (grep of
`mods/ww3mod/**.yaml` yields `Damage: 6750` through `Damage: 200000`), not RA's ~50. Kernels are
**additive across contacts** (`:366-374`) with linear falloff over a radius of `range/1024 + 2` cells
capped at 32 (`:161-163`). A threshold of 60 is an RA-scale number sitting under a field rescaled by
~200×.

**Two independent pieces of log evidence, either of which is sufficient:**

1. **Same cell, danger 66,834 → 0, no movement.** Truck 4855: `evac-enter @79,36 danger=66834` (tick
   6,436) → `evac-exit @79,36 danger=0` (tick 6,736). Identical cell. The field at a fixed cell two cells
   from the SR swings across the threshold within 300 ticks. Trucks 4758 (`@78,33`, danger 133,669),
   4982 (`@80,33`, danger 66,834) and 5082 (`@86,34`, danger 2,369,732 — **the same tick it was
   adopted**) all evacuate while standing on their own beachhead.

2. **A recurring background level of 68 against a threshold of 60.** Three separate trucks, at three
   separate cells within 4 of the west SR, enter evac at **exactly `danger=68`**: 5569 `@10,46`, 5319
   `@11,46`, 5651 `@13,51` (ticks 24,000 / 24,436 / 25,800). All three exit at `danger=0`. The ambient
   field around the west beachhead flickers 0 ↔ 68 and the threshold sits **inside that swing**, so a
   truck at home is evac-eligible on roughly every other scan. Its "evacuation" is a 3-cell Move to
   `14,45` — which cancels whatever delivery run it had just started.

**Confidence:**

| Claim | Confidence | What would raise it |
|---|---|---|
| User is seeing an SR-ward healthy-bar loop, **not** Loop B | **High (~90%)** | Already strong: 36 logged SR-ward legs, 0 map-edge legs while adopted, 10/10 releases `low-supply`. Residual risk is that the user was watching a *released* truck retiring to the edge (§1 caveat) — one question to them settles it. |
| Drop-and-leave is **not inert in general**, but went dark 2.4 min on the west player | **High (~92%)** | Raised from ~85% by the final log: drops stop at the impassable edge and resume 1,800 ticks after `anchor-recovered`, so both edges of the outage are matched, not just its start. Would reach certain by resolving what terrain `33,31` on River Zeta actually is. |
| Scale mismatch on `EvacDangerThreshold` is the primary driver | **Medium-high (~75%)** | The two evidence items above are strong, but the *fix* is not obvious from the log alone — I have not established what threshold value would leave the evac useful rather than merely disabled. That needs the field's distribution over a whole match, which nothing currently logs. |
| The evac branch is the *whole* story | **Low (~35%)** | The approach-abort path is behind `DebugLogging=false` (`SupplyFollowerBotModule.cs:1596`, `:237`) — `evac-hold` and the follow-path decisions emit nothing. A second, faster oscillator could be running underneath every one of these 48 s cycles and this log would not show it. |

**The honest bottom line for the user:** the trucks are not broken — 14 trucks were adopted at 750 supply
and all 11 releases were `low-supply` at 0–62, and 6 caches were dropped, so supply *is* being
delivered. What they are seeing on
top of that is a real 48-second, 12-cell rearward lurch, driven by a danger threshold that cannot tell a
distant rumour of a contact from a tank in the truck's face. **Yesterday's two fixes addressed neither
this nor Loop B-as-observed; they addressed the map-edge loop, which does not appear in this log at
all.** That is not a fourth claim that trucks are fixed — it is a claim about which knob is wrong, and
the two log facts in §6 are the reason to believe it.

**Cheapest next step, and it is not a guess:** one `Log.Write` recording the danger-field value
distribution per scan (min/median/max over active cells), so the threshold can be set against the field
that actually exists rather than against the "tens-to-hundreds per cell" model asserted in
`ai.yaml:840-842` — which this log shows to be wrong by three orders of magnitude.
