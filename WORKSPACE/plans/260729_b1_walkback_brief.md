# DO-NOT-MERGE brief — residual B1 walk-back fix (mid-adjust redirect)

Date: 2026-07-29. Branch: `auto/b1-walkback` (off `main` @ `4efe523f`). Author: the agent.
Written for an independent adversarial reviewer. **This branch is NOT to be merged as-is** — the
behavioral autotest was AUTHORED but NOT RUN (a benchmark ladder held the main checkout; the
`@experimental` pricing run and the RED/GREEN autotest are serialized to the manager). Everything
below is a static argument plus a green NUnit + build; treat the deferred-run section as the gate.

---

## 1. The contract

**Bug.** `StancePositioningExecutor` (the Phase-2/3 idle-repositioning trait) skipped its ITick leash
check entirely while `State == Adjusting`. So a player order issued DURING the executor's own
adjustment move was never caught mid-move: the executor's cohesion-slot override (set to the old
cover cell) was left live, and on the unit's next idle `CohesionSlotMemory` — declared before the
executor in `^Combatant` — fired return-to-slot FIRST and dragged the unit back to the abandoned
cover cell ONCE, re-settling `Arrived` against the stale anchor.

**Trigger window (from the discovered.md entry).** Redirect issued inside the adjust move
(~≤164 ticks) to a cell ~5–14 cells away — short enough that the round trip finishes inside
`CohesionSlotMemory.ForgetAfterTicks` (750), so the slot stays FRESH. Longer redirects self-heal (the
slot goes stale and `CohesionSlotMemory` clears it), and a redirect issued AFTER `Arrived` is already
caught by the pre-existing bare-leash ITick branch (the original B1 fix).

**Fix.** While `Adjusting`, ITick now aborts the stale adjust when the unit strays beyond
`LeashRadius + AdjustLeashMargin` (Manhattan) of the anchor. `ReleaseManagement()` clears the
cohesion slot NOW — during the move, before the unit idles — so `CohesionSlotMemory.TryReturnToSlot`
no-ops (`!hasSlot`) instead of dragging the unit back; the next idle re-anchors at the redirect
target. The margin exists so the executor's OWN pathing excursions (its adjustment `Move` targets a
`WithinLeash` cell but the pathfinder may route a couple of cells outside the leash around obstacles)
do not false-abort and drop the mid-adjust ledger claim/slot.

**Invariant restored:** while Adjusting, a stale adjust never survives an external relocation past the
leash band — the anchor/slot never out-live the player's redirect.

---

## 2. Files at file:line (branch tip)

Engine (the only behavioral change):
`engine/OpenRA.Mods.Common/Traits/StancePositioningExecutor.cs`

- **`AdjustLeashMargin` Info field** — `:116-125` (default `2`). New read-only config; no RNG, no
  trait-order change.
- **`ITick.Tick`** — `:233-274`. Restructured:
  - `:238-239` early-return on `IsTraitDisabled` (UNCHANGED gate — see §3).
  - `:246-266` NEW `Adjusting` branch: `if (hasAnchor && !WithinLeash(self.Location, Info.AdjustLeashMargin)) ReleaseManagement()` then `return`. This is the fix.
  - `:268-273` the pre-existing settled-anchor bare-leash branch, unchanged in behavior (only moved
    below the `Adjusting` return).
- **Leash predicates** — `:583-601`:
  - `:585-588` NEW pure static `WithinManhattan(cell, anchor, radius)` — extracted so the threshold is
    unit-testable without a live actor.
  - `:590-593` `WithinLeash(cell)` now delegates to the static (bare leash).
  - `:598-601` NEW `WithinLeash(cell, margin)` overload — the leash+margin band used by the Adjusting
    branch.
- **`ReleaseManagement()`** — `:649-664` UNCHANGED; already clears slot (`slotMemory?.Clear()`),
  ledger claim, `hasTarget`, and `hasAnchor`/`anchor`. The fix simply reaches it from one more path.

Consumer that makes the bug bite (unchanged): `CohesionSlotMemory.TryReturnToSlot`
(`engine/OpenRA.Mods.Common/Traits/CohesionSlotMemory.cs:202-228`) — queues `Move(assignedSlot)` while
`hasSlot` and the slot is fresh; `.Clear()` (`:143-150`) defuses it. Declared before the executor:
`mods/ww3mod/rules/defaults.yaml:20` (`CohesionSlotMemory`) vs `:27` (`StancePositioningExecutor`),
with the ordering requirement spelled out in the `:21-23` comment.

Tests:
- NUnit pin (pure predicate): `engine/OpenRA.Test/OpenRA.Mods.Common/StancePositioningLeashTest.cs`
  (new).
- Autotest (AUTHORED, NOT RUN): `tools/autotest/scenarios/test-stance-redirect-midadjust/`
  (`map.yaml`, `rules.yaml`, `description.txt`, `test-stance-redirect-midadjust.lua`; `map.bin`/
  `map.png` copied verbatim from `test-stance-anchor-move`).

---

## 3. @stable gating evidence (byte-identity)

The executor is a `ConditionalTrait` gated
`RequiresCondition: enable-tactical-positioning || enable-ai-experimental`
(`defaults.yaml:28`). Grantors:
- `enable-ai-experimental` — `GrantConditionOnBotOwner@tacpos`, `Bots: experimental`
  (`defaults.yaml:37-39`). Not granted to `@stable`/`@normal`/control bots.
- `enable-tactical-positioning` — `GrantConditionOnHumanOwner@tacpos` (`defaults.yaml:44-45`),
  predicate `Owner.Playable && !Owner.IsBot`. Not granted to any bot; excludes scenario garrisons
  (`Playable: False`).

Therefore for every non-experimental profile the trait is `IsTraitDisabled`, and:
- `ITick.Tick` returns at `:238-239` (`if (IsTraitDisabled) return;`) BEFORE any new code. The added
  `Adjusting` branch and the margin predicate are unreachable when disabled.
- No new `SharedRandom`/`LocalRandom` draw anywhere (the only draw, `Created` `:193`, is untouched).
- No trait added/removed/reordered in `^Combatant`; the new member is a pure-config `readonly int`.

Conclusion: `@stable`/`@normal`/control-bot matches are byte-identical. The change re-prices only
`@experimental` bots and Phase-3 human-owned units (the executor's active population) — exactly the
N4 governance verdict for any executor code change. **Reviewer must still confirm** the one `@stable`
replay-hash tripwire before/after during the serialized run (N2).

---

## 4. Margin rationale (why `AdjustLeashMargin = 2`)

`LeashRadius = 4` (Manhattan). The executor's adjustment `Move` always targets a `WithinLeash` cell —
`ChooseTarget` only returns cells with `|dx|+|dy| <= LeashRadius` (`:551` loop, `:600` predicate) —
and the unit's start-of-adjust cell is likewise within the leash. So both endpoints of any legitimate
executor move lie in a Manhattan-4 diamond around the anchor. A pathfinding detour around cover-edge
obstacles (tree/tanktrap footprints are typically 1–2 cells thick) bulges only a couple of cells
beyond that diamond in practice.

The margin is a two-sided tradeoff:
- **Too small** → the executor's own detours cross the threshold and false-abort, dropping the
  mid-adjust ledger claim (`tacpos:` — the whole reason `Adjusting` holds a claim: it keeps a bot's
  Poi stack / `StateBase.ExcludeTacticallyCommitted` off the unit) and the cohesion slot. A
  false-abort is otherwise BENIGN — `ReleaseManagement()` does not cancel the executor's own `Move`
  activity, so the unit still completes to `dest` and simply re-anchors there — but frequent
  false-aborts would erode the mid-adjust protection the state exists to provide.
- **Too large** → a nearby redirect (target within the band) is not caught mid-move and the walk-back
  recurs for that case.

`margin = 2` → abort threshold Manhattan `4 + 2 = 6`. This admits a 2-cell obstacle detour (covers
the realistic cover-edge case) while catching redirects whose target is > 6 Manhattan from the
anchor — i.e. the bulk of the recorded 5–14-cell window, since the unit crosses the ring during the
redirect move. It is configurable (Info field) so it can be tuned from the `@experimental` run
without a code change. See §6 for the residual (near-band redirect) it does not catch.

The pure threshold is pinned in `StancePositioningLeashTest`:
- `MarginBandAdmitsBoundedPathingExcursionsBeyondTheLeash` (Manhattan 5, 6 inside the band),
- `MarginBandCatchesAPlayerRedirectBeyondTheBand` (Manhattan 7, 8, 14 outside),
- `MarginBandIsStrictlyWiderThanTheBareLeash`,
- plus Manhattan-not-Chebyshev / boundary-inclusive / sign-symmetry pins on the shared predicate.

---

## 5. The autotest (RED-on-base / GREEN-on-fix semantics)

`tools/autotest/scenarios/test-stance-redirect-midadjust/`. One human-owned AR (`ar.america`, USA),
executor in isolation (no bot module). Zone-A treeline (tanktraps `y=16, x=10..16`) gives a south
cover edge at `y=17`; the AR spawns 4 cells south at `(13,21)`, enemy sighted further south. The
executor (Defensive) relocates the AR NORTH to `(13,17)` (the only cover edge inside the 4-cell leash
of the spawn anchor ⇒ deterministic target). WHILE that move is in flight (Y strictly between 21 and
17 ⇒ `State == Adjusting`), the Lua issues ONE single-unit `Move` to `(22,19)` — ~9 cells, so the
trip (~450 ticks) finishes inside `ForgetAfterTicks` (750) and the slot stays fresh. A single-unit
`Move` does not route through `CohesionMoveModifier`, so it never re-assigns the slot the executor
set to `(13,17)`.

- **RED on base** (`main`, fix absent): ITick skips while Adjusting → slot stays `(13,17)`, fresh →
  on arrival at `(22,19)` `CohesionSlotMemory` return-to-slot drags the AR back west → it comes within
  6 cells of `(13,17)` → `Test.Fail`.
- **GREEN on fix**: ITick aborts once the AR passes Manhattan 6 of the anchor (~`x=19`),
  `ReleaseManagement` clears the slot before the AR idles → it holds at `(22,19)` (no threat there ⇒
  executor disengages) and never returns → `Test.Pass` after a 14 s hold.

Guardrails: fails loudly if the executor never starts the adjustment, if the AR reaches the cover edge
before a redirect could be injected (window missed), or on hard timeout. `leftA` gates the mid-transit
pull-back assertion so it cannot false-trip while the AR is still legitimately near zone A right after
injection.

**Why this is distinct from `test-stance-anchor-move`:** that test issues the redirect AFTER `Arrived`
and exercises the ORIGINAL B1 fix (already green on `main`). This one issues the redirect DURING
`Adjusting` — the gap the original fix left open.

---

## 6. Residual risks — what the reviewer must scrutinize

1. **Near-band redirects still walk back (by construction).** A redirect whose target is within
   Manhattan `6` of the anchor may never cross the abort threshold during the move, so the walk-back
   can still fire for it. This is the deliberate cost of a non-zero margin (§4). Bounded (≤ leash+margin
   cells) and self-healing; if the run shows it matters, lower the margin (accepting more benign
   false-aborts) or add a claim-age / order-source signal. **Confirm the run does not surface a
   short-hop redirect complaint.**
2. **False-abort of the executor's own move.** If a real map produces an obstacle detour > 2 cells
   beyond the leash, ITick aborts the executor's own adjustment. Argued benign (the `Move` still
   completes; the unit re-anchors at `dest`), but it drops the `tacpos:` claim mid-move for a bot,
   briefly exposing the unit to Poi re-tasking. **Reviewer should sanity-check that cover-edge terrain
   detours stay ≤ 2 cells in the actual test/benchmark maps**; if not, bump the margin.
3. **Autotest window robustness.** The injection relies on observing the AR at an intermediate cell of
   a 4-cell move (≈164 ticks) with a 1-tick poll — comfortable, but if a future speed/pathing change
   shortens the move, the `could not inject mid-adjust` guard converts that into a loud fail rather than
   a silent pass. Intended.
4. **Determinism.** No new RNG; the predicate is pure integer Manhattan arithmetic. The added overload
   and static do not touch the `[Sync]` field set or `Created` ordering.

---

## 7. Deferred-run interpretation (the merge gate)

Not run on this branch (main checkout held by a benchmark ladder; no `run-test.sh`/`run-batch.sh` per
the task constraint). Before merge, the manager must:

1. Run `test-stance-redirect-midadjust` on `main` (fix reverted) → expect **RED** (walk-back). Then on
   `auto/b1-walkback` → expect **GREEN**. A GREEN-on-base would mean the scenario does not reproduce
   the bug (e.g. the redirect went stale or was caught post-Arrived) and the test must be retuned
   before it can guard anything.
2. Re-run `test-stance-anchor-move` on the branch → must stay **GREEN** (no regression of the original
   B1 / S3 behavior).
3. One `@experimental` pricing run (executor code changed). No `@stable` re-baseline expected; verify
   one `@stable` replay-hash byte-identity before/after as the N2 tripwire.

Only after (1)-(3) is the fix mergeable. Until then this branch is evidence + a static argument, not a
shipped fix.

---

*Basis: `auto/b1-walkback` off `main` @ `4efe523f`. Build (`make all`) and NUnit
(`dotnet test … Release`) run in the worktree; counts in the final report. No game launch, no
autotest run, no benchmark performed.*
