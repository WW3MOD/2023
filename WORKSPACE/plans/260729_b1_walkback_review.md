# Adversarial review — `auto/b1-walkback` (residual B1 mid-adjust walk-back)

Reviewer: the agent (independent). Date: 2026-07-29. Under review: commit `818ac2cf`, parent
`4efe523f`. Worktree: `worktrees/ww3mod/b1-walkback`. Method: every brief claim re-derived against the
actual diff at file:line; build + NUnit reproduced INSIDE the worktree; autotest static-traced (NOT
run — deferred to the manager per the task constraint). Register: impersonal.

## Overall verdict: **MERGE-WITH-NITS** — gated on the deferred behavioral run the brief already names

The fix is correct, minimal, and preserves the @stable byte-identity invariant at file:line. Build is
clean, the unit suite is green, and the authored autotest is statically RED-on-base / GREEN-on-fix with
loud (never silent) guards. Nothing here is a merge blocker on its own. The one thing standing between
this branch and merge is the behavioral gate the implementer explicitly deferred (§7 of the brief): the
RED/GREEN run of `test-stance-redirect-midadjust` on base vs branch, the `test-stance-anchor-move`
no-regression re-run, and one `@stable` replay-hash tripwire. The manager owns those. Do not merge
until they are green — but the code, as a static + build + unit-test artifact, is sound.

---

## Reproduction (inside the worktree only; main checkout untouched)

- `make all` → **Build succeeded, 0 Warning(s), 0 Error(s)** (three project groups). Matches the claim.
- `dotnet test … Release` → **Passed! Failed: 0, Passed: 531, Skipped: 0, Total: 531**. Matches the claim.

No game launch, no autotest run, no benchmark. No writes to the main checkout beyond read-only git.

---

## Per-claim verdicts (file:line)

**C1 — `AdjustLeashMargin` Info field, default 2. VERIFIED.**
`StancePositioningExecutor.cs:116-125`, `public readonly int AdjustLeashMargin = 2;` at `:125`.
Pure-config `readonly int`; no RNG, no sync field. (Brief said `:116-125` — exact.)

**C2 — ITick hoists `IsTraitDisabled` above all new code. VERIFIED (this is the byte-identity crux).**
`:238-239` `if (IsTraitDisabled) return;` is now the FIRST statement of `ITick.Tick`. Previously the
disabled test was folded into `if (IsTraitDisabled || State == Adjusting) return;`. For a DISABLED unit
the behavior is identical (immediate return); for an ENABLED unit that is Adjusting the method now
enters the new branch — exactly the intended change. Disabled units never reach the margin predicate.

**C3 — New Adjusting branch aborts past leash+margin and clears the slot mid-move. VERIFIED.**
`:246-266`: `if (State == Adjusting) { if (hasAnchor && !WithinLeash(self.Location, Info.AdjustLeashMargin))
{ ReleaseManagement(); State = None; } return; }`. `ReleaseManagement()` (`:649-665`) sets
`slotMemory?.Clear()`, `committedGuard?.Ledger.Release`, `hasTarget=false`, `hasAnchor=false`. Confirmed
against the consumer: `CohesionSlotMemory.Clear()` (`CohesionSlotMemory.cs:143-150`) sets `hasSlot=false`,
and `TryReturnToSlot` (`:202-228`) no-ops on `if (!hasSlot) return;`. So clearing the slot mid-move
genuinely defuses the return-to-slot drag. `ForgetAfterTicks = 750` (`:28`) — matches the freshness
argument. (Brief `:246-266` — exact.)

**C4 — Settled-anchor bare-leash branch unchanged, only relocated below the Adjusting return. VERIFIED.**
`:270-274`. Behaviorally identical to base (releases on out-of-bare-leash for a non-Adjusting anchor).
(Brief said `:268-273`; actual `:270-274` — a cosmetic off-by-two, no material effect. See NIT-3.)

**C5 — Pure static predicate extracted + two overloads. VERIFIED.**
`WithinManhattan(cell, anchor, radius)` static at `:585-588` (integer Manhattan). `WithinLeash(cell)`
`:590-593` delegates with `Info.LeashRadius`. `WithinLeash(cell, margin)` `:598-601` delegates with
`Info.LeashRadius + margin`. Matches `ChooseTarget`'s leash disk (`:537` `|dx|+|dy| <= lr`) — Manhattan,
not Chebyshev. Pinned in `StancePositioningLeashTest.cs` (8 tests): band = 4+2 admits Manhattan 5/6,
catches 7/8/14; strictly-wider-than-bare-leash; Manhattan-not-Chebyshev; boundary-inclusive; sign-
symmetric. All arithmetic re-checked by hand — correct.

**C6 — @stable byte-identity. VERIFIED at file:line (static portion).**
Gate `RequiresCondition: enable-tactical-positioning || enable-ai-experimental` (`defaults.yaml`
`StancePositioningExecutor`). Grantors in `^Combatant`: `GrantConditionOnBotOwner@tacpos` `Bots:
experimental` and `GrantConditionOnHumanOwner@tacpos` (`Condition: enable-tactical-positioning`, human-
only predicate). Neither is granted to `@stable`/`@normal`/control bots ⇒ `IsTraitDisabled` ⇒ ITick
returns at `:238`. No new `SharedRandom`/`LocalRandom` (only draw is `Created:204`, untouched;
`WithinManhattan` is pure). The diff touches only the executor `.cs` — `defaults.yaml` is NOT in the
commit, so no trait added/removed/reordered in `^Combatant`. Byte-identity holds for the non-executor
population by construction. The live `@stable` replay-hash tripwire (N2) is the manager's to run; it is
belt-and-suspenders, not load-bearing, since disabled traits return at line 1 of the method.

**C7 — Trait ordering that makes the bug bite. VERIFIED.**
`defaults.yaml` `^Combatant`: `CohesionSlotMemory` declared BEFORE `StancePositioningExecutor`, with the
ordering requirement spelled out in the interleaving comment. `CohesionSlotMemory.TickIdle` (`:163-173`)
fires return-to-slot; the executor's `TickIdle` early-returns on `self.CurrentActivity != null` (`:284`),
so on base the return-to-slot wins the idle tick — this is precisely why the walk-back fires and why
clearing the slot in ITick (before idle) is the correct fix locus.

**C8 — discovered.md inline update. VERIFIED.** The 2026-07-22 entry gains a `**FIX (branch … NOT
merged)**` suffix; the original bug text is unchanged. Honest ("near-band (≤6-cell) redirects still walk
back by construction"), no overclaim.

**C9 — map.bin/map.png copied verbatim from `test-stance-anchor-move`. VERIFIED SAFE.**
`cmp` reports map.bin IDENTICAL; both maps are `MapSize: 66,34`. The zone-A treeline is placed as
`tanktrap1` ACTORS overlaid in `map.yaml`, not as terrain tiles, so reusing the open-ground bin is
correct — no terrain/geometry mismatch with what the Lua assumes.

---

## Autotest static trace (`test-stance-redirect-midadjust`) — RED-on-base / GREEN-on-fix

Geometry: anchor/spawn `(13,21)`, cover edge `(13,17)` (Manhattan 4 = leash boundary, the only edge in
leash ⇒ deterministic target), redirect target B `(22,19)` (Manhattan 11 from anchor), enemy t90
`(13,24)`. Enablement is the real Phase-3 human path (USA human ⇒ `enable-tactical-positioning`); no bot
module, no `enable-ai-experimental` — **map-local, nothing experimental enabled mod-wide.** Confirmed.

- **Both builds start the adjustment**: enemy `(13,24)` is within `ThreatScanRadius=8` of the anchor
  (dy=3), south bearing resolves, `ChooseTarget` returns `(13,17)`, `Move` issued, `State=Adjusting`.
- **Injection window**: Lua polls at 1 tick; a 4-cell move (~164 ticks) guarantees an observed
  intermediate `17 < Y < 21`; the `could not inject` guard converts a missed window into a LOUD fail,
  never a silent pass (verdict-latch-safe).
- **RED on base**: ITick skips while Adjusting → slot stays `(13,17)`, fresh (round trip ~450 < 750). AR
  reaches B, idles; `CohesionSlotMemory` (declared first) queues return-to-slot, executor bails on
  `CurrentActivity != null`, AR dragged west → `dist(loc, COVER) <= 6` in `holdB` → `Test.Fail`.
- **GREEN on fix**: crossing Manhattan 6 of `(13,21)` (~`x=18`) trips the new abort; `ReleaseManagement`
  clears the slot before idle. AR completes the redirect Move to B (abort does NOT cancel the activity),
  idles; no slot ⇒ no drag; re-anchors at B; enemy now out of scan range (dx=9 > 8) ⇒ null bearing ⇒
  disengage ⇒ holds 14 s → `Test.Pass`.
- **No false-fail on fix**: `leftA` latches once `dist(loc, COVER) > 6` and the AR only moves further
  east thereafter, so the pull-back assertion cannot trip during legitimate transit.
- **Verdict latch**: `finished` one-shots pass/fail; `poll` and `step` both short-circuit on it — no
  double-verdict, no re-fire. Clean.

Trace holds. The only residual test risk (threat intensity too low to start the adjustment) is itself
guarded by a loud fail, not a false pass — which is why the deferred RED/GREEN run is the correct gate.

---

## Margin scrutiny (item 4) — holds, with one wording overreach

`ChooseTarget` endpoints are provably Manhattan ≤ 4 from the anchor (`:537` loop bound; Hunt's `stepped`
re-checked `WithinLeash` at `:552`). So both ends of any legitimate executor move lie in the leash
diamond; margin 2 admits a 2-cell obstacle detour. **However**, a pathfinder detour around a *large*
obstacle is not bounded to 2 cells in general — the "beyond leash+margin the unit CANNOT be on our own
move" phrasing (Info `:119`, code comment `:256`) is stronger than what the pathfinder guarantees. This
is acceptable because the consequence of a false-abort is benign and self-healing: `ReleaseManagement`
does not cancel the `Move`, the unit completes to `dest` and re-anchors there (`WithinOneCell` →
`Arrived`, no re-issue loop); a human has no `tacpos:` ledger claim to drop at all (`CommitManagement`
`:640` commits only for bots). The near-band (≤ Manhattan 6) residual walk-back is real and by
construction, but bounded, self-healing, and honestly documented. Margin is a tunable Info field, so the
deferred `@experimental` run can retune without a code change. Not a blocker.

---

## Nits (none blocking)

- **NIT-1 (coverage framing).** The 8 new NUnit tests pin ONLY the pure geometry predicate — not the
  ITick behavior (slot clear, abort, re-anchor). The entire behavioral claim rests on the UNRUN autotest.
  "8 new pins" is accurate but should not be read as behavioral coverage; weight the deferred RED/GREEN
  run accordingly before merge.
- **NIT-2 (wording).** Info `:119` / comment `:256` "cannot be on our own move" overstates the pathfinder
  bound; "is very unlikely to be on our own move (obstacle detours are bounded in practice)" is the
  defensible claim. Cosmetic — the logic and the benign-false-abort argument are sound regardless.
- **NIT-3 (brief line refs).** A few brief `§2` refs are off by one or two vs the actual file
  (settled branch `:268-273`→`:270-274`; ITick `:233-274`→`:233-275`; ReleaseManagement
  `:649-664`→`:649-665`). Cosmetic; every referenced construct is present and correct.
- **NIT-4 (title).** "close residual B1" narrows rather than closes it (near-band case persists). The
  commit/brief bodies say so plainly, so this is honest shorthand, not an overclaim.

---

## Required before merge (manager-owned, per brief §7 — not runnable here; benchmark holds main)

1. `test-stance-redirect-midadjust`: **RED on `main`** (fix reverted), **GREEN on `auto/b1-walkback`**. A
   GREEN-on-base means non-repro → retune before it can guard anything.
2. `test-stance-anchor-move`: re-run on branch → must stay **GREEN** (original B1 / S3 no-regression).
3. One `@experimental` pricing run (executor code changed) + one `@stable` replay-hash byte-identity
   check before/after (N2 tripwire).

Until (1)–(3) are green this is evidence + a static argument, not a shipped fix — exactly the posture the
DO-NOT-MERGE brief asserts. Code, build, and unit-test state: sound. Verdict: **MERGE-WITH-NITS**, gated.
