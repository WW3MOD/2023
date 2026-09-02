# Discovered Bugs

> Bugs found while working on something else. Captured here so they don't get lost.
> Format: `- [DATE] [severity] description (found while working on: X)`

---

- [2026-09-02] [HIGH] **Capturing a building leaves its vision with the PREVIOUS owner, permanently,
  and gives the captor none.** `Vision` derives from `AffectsMapLayer` (`Vision.cs:29`), which
  implements `INotifyAddedToWorld` / `INotifyRemovedFromWorld` / `INotifyCenterPositionChanged` /
  `INotifyMoving` / `ITick` but **not `INotifyOwnerChanged`** (`AffectsMapLayer.cs:42-43`). Its
  `UpdateCells` — the only thing that re-evaluates `AddCellsToPlayerMapLayer`, and hence the only
  thing that re-runs `Vision`'s `self.Owner.RelationshipWith(p)` gate (`Vision.cs:48-53`) — runs on
  add-to-world, on a position change, or on `ITick` **only when the range or trait-disabled state
  changes** (`AffectsMapLayer.cs:136-159`). A captured building does none of those: it never moves,
  its range never changes, and `^BasicBuilding`'s `Vision@3/@2/@1` carry no `RequiresCondition`.
  Upstream this is harmless because `Actor.ChangeOwnerSync` momentarily removes and re-adds the
  actor (`Actor.cs:577-592`), firing the add/remove hooks and rebuilding the sources. **But both
  real capture paths deliberately skip that cycle for buildings**: `CaptureActor.cs:140-143`
  (engineer capture) and `ProximityCapturable.cs:222-227` both branch on
  `HasTraitInfo<BuildingInfo>()` to `ChangeOwnerInPlaceSync`, whose doc-comment names the skipped
  work as *"the expensive shroud/vision recalc cascade ... (causes a ~0.5s freeze on capture)"*. The
  perf motive is presumably real; the correctness cost appears not to have been noticed.
  **Effect:** every building you have ever lost keeps feeding you vision out to 3c0 forever, and the
  player who took it gains nothing from it. A standing fog leak on both sides of every capture,
  considerably larger than any tooltip.
  **Interaction with `wt/tooltip-owner`:** it also means the old owner still SEES the captured cell,
  so their ghost never freezes and the tooltip-identity leak that branch fixes is currently
  unreachable *through the in-place capture paths*. It stays reachable through every caller that
  uses the full `ChangeOwner` — Lua `actor.Owner =` (`GeneralProperties.cs:63`),
  `ChangeOwnerWarhead`, `OwnerLostAction`, `TemporaryOwnerManager`, `BaseSpawnerSlave` — and would
  become reachable through engineer capture the moment this is fixed. The tooltip fix is therefore
  correct and forward-safe rather than dead, but do not expect to reproduce the leak by walking an
  engineer into a building until this is resolved.
  **Not fixed here** — the obvious repair (have `AffectsMapLayer` implement `INotifyOwnerChanged`
  and call `UpdateCells`) reintroduces exactly the cascade those two call sites were written to
  avoid, so it needs a perf answer and a decision, not a drive-by. **No launch taken; read off the
  traits.**
  (found while working on: withholding the captor's identity in the frozen tooltip, `wt/tooltip-owner`)

---

- [2026-09-02] [low] **`test-frozen-owner-snapshot` cannot fail on the owner-change path anyone is
  actually worried about, and its name does not say so.** `FrozenUnderFog.OnOwnerChanged` (`:217-224`)
  refreshes `frozenStates[oldOwnerIndex]` and nothing else — **only the old owner's ghost moves.** The
  scenario deliberately makes USA a *third party* (`test-frozen-owner-snapshot.lua:24-25`: "USA saw the
  building while it was Russian, and USA must still believe it is Russian after it changes hands"),
  which is precisely the ghost this path leaves alone. So it asserts the correct behaviour of the
  untouched case and is structurally incapable of observing the leak. It is also the **only** scenario
  of 276 that calls the frozen Lua bindings at all.
  **Not a code defect — a naming/coverage one.** The risk is that a future worker reads a green
  `test-frozen-owner-snapshot` as assurance over the old-owner-as-viewer path and plans against it.
  Fix is either a second scenario making the old owner the viewer, or a header line in the existing
  one stating what it does not cover (its `RED-ARM.md` already documents adjacent limits, so that is
  the cheap half). **No launch was taken to confirm; this is read off the Lua and the engine path.**
  (found while working on: verifying the frozen-actor owner-change claims, `wt/frozen-capture`)

---

- [2026-08-30] [med] **The production tooltip overlaps the production sidebar, and its panel is not
  opaque, so anything drawn behind it shows through.** Observed on `wt/tooltip-elements` at 3584x2240:
  the tall rifleman tooltip lands across the sidebar's icon column and unit portraits are legible
  *through* the panel, behind the tooltip's own text.
  **The geometry is not new.** `ProductionTooltipLogic` computes
  `leftWidth = Math.Clamp(..., MaxTooltipWidth, MaxTooltipWidth)` — both bounds equal, so the left
  column is unconditionally 350 and `widget.Bounds.Width` is identical to what it was before the
  typed-row work. `TooltipContainerWidget.GetAnchoredPosition` then places it at
  `anchor.X - tooltipWidth - AnchorGap` (`:154`), left of the sidebar, and clamps only vertically.
  What changed is that content now reaches the panel's right edge: the old single label was
  left-aligned and narrow, so it never occupied the region where the overlap shows.
  **Not fixed here, deliberately** — the fix is either an opaque tooltip background or a narrower
  right column, and both change the appearance of every tooltip in the game, which is a visual
  decision and not this branch's to make. **Comparison against `main` was by reading the width
  arithmetic, NOT by screenshot**, so "pre-existing" is reasoned, not observed.
  (found while working on: the typed-element tooltip rewrite, `wt/tooltip-elements`)

---

> ### Reading note for the 2026-08-20 ambush/concealment/cover block below
>
> The eighteen entries immediately below were extracted from the nine-strand research programme in
> `WORKSPACE/ambush-programme/` and the ground-truth audit
> `WORKSPACE/recon/260820-ambush-cover-detection-audit.md`. **Every one was re-verified against
> source at `main @ 57822b4e` before being written here**, and several corrections to the source
> documents are recorded inline — read the `Established:` line on each entry, not the design doc.
>
> **Not one of them was measured. No strand in that programme launched the game, and neither did
> this filing pass.** Where an entry says DERIVED, it is arithmetic over verified code sites and
> nobody has watched the symptom occur. Where it says READ, the code was read at HEAD but the
> player-visible consequence is still inferred. Treat the `Confirm by:` line as work not yet done.
>
> **None of these are fixed, and none may be fixed without the user's say-so** — their standing
> instruction is that nothing on stances, ambush, concealment or cover is implemented until they
> say. Filed so the defects survive independently of the design conversation that produced them.
>
> **Two entries already in this file, filed earlier the same day from `wt/ambush-legibility`, carry
> claims this pass overturned** — the "burnt trees" cover-bonus entry and the "maximum concealment
> draws nothing" entry. Both now carry dated correction blocks pointing back here. If you find them
> by search rather than by reading top-down, read the correction before acting.

---

## 2026-08-30: [low] OPEN — `IskanderTargeter`'s zeroing `Versus` table omits `Kevlar` and `Unarmored`, so the dummy trigger warhead delivers FULL damage to soldiers (found while: combat-feedback recon, branch `wt/recon-battle-feedback`, `main @ 5a985337`)

**Established by reading; NOT measured in game.** No launch was spent on this.

`IskanderTargeter` (`mods/ww3mod/rules/weapons/weapons-missiles.yaml:394-401`) is a dummy launch
trigger — an `InstantHit` designator whose `Versus` table zeroes `None`, `Wood`, `Concrete`, `Light`,
`Medium`, `Heavy` and `Brick` so it damages nothing. Two defects in that list:

- **`Brick` is not an armour class in this mod.** The mod's population is exactly nine — `Concrete`,
  `Heavy`, `Indestructable`, `Kevlar`, `Light`, `Medium`, `None`, `Unarmored`, `Wood`
  (`conventions.md` §`Versus`). Zeroing `Brick` accomplishes nothing.
- **`Kevlar` and `Unarmored` are omitted, and an omitted class is FULL damage, not zero.**
  `DamageVersus` filters the victim's armours to the classes the table *lists*
  (`DamageWarhead.cs:105`); an unlisted class matches nothing, `ApplyPercentageModifiers` runs over
  an empty sequence, and the victim takes an unmodified 100%. `^Soldier` is `Type: Kevlar`
  (`infantry.yaml:174-175`) and sets no `Thickness`, so there is no penetration division either.

**Consequence:** the warhead delivers its full `Damage: 50` to any soldier at the aim point. This is
the exact shape `conventions.md` warns about — *"a table that zeroes `None` but omits `Kevlar` is
harmless to a civilian and lethal at full damage to every soldier"*.

**Scope is small and that is why it has survived:** `TargetDamageWarhead.Spread` defaults to
`WDist(1)` (`TargetDamageWarhead.cs:24`) and this warhead sets none, so only a hitshape edge within
one world unit is admitted. Real, near-unobservable, one-line fix (add `Kevlar: 0` and
`Unarmored: 0`; drop `Brick`).

**Confirm by:** firing an Iskander with a soldier standing exactly on the designated cell and
checking for a 50-damage tick, or an NUnit assertion over the resolved weapon.

---

## 2026-08-30: [low] OPEN — `Bullet.MinInaccuracy` OVERWRITES the computed scatter instead of flooring it, so it is a fixed inaccuracy and not a minimum (found while: combat-feedback recon, branch `wt/recon-battle-feedback`, `main @ 5a985337`)

**Established by reading; NOT measured in game.**

`engine/OpenRA.Mods.Common/Projectiles/Bullet.cs:206-209`:

```cs
if (info.MinInaccuracy != WDist.Zero)
    maxInaccuracyOffset = info.MinInaccuracy.Length;
```

It assigns rather than taking `Math.Max`, which contradicts the field's own `[Desc]` — *"The minimum
inaccuracy regardless of distance to target"* (`:33-34`). At any range where the distance-scaled
scatter would exceed `MinInaccuracy`, the weapon becomes **more** accurate than its `Inaccuracy`
setting asks for, rather than less.

**Blast radius is one weapon:** `MinInaccuracy: 32` at
`mods/ww3mod/rules/weapons/weapons-ballistics.yaml:217`. Grep before fixing — a fix changes that
weapon's long-range scatter, so it is a balance change, not just a correctness fix.

**Confirm by:** a resolved-stats read plus an NUnit case over the two branches; no launch needed.

---

## 2026-08-30: [medium] OPEN, NOT FIXED — a DOCKED vehicle can drain a Logistics Centre past the restock reserve the push arm respects, starving the infantry aura arm (found while: auditing drain rates after `9e46f141`, branch `wt/postmerge-fallout`, `main @ 9e46f141`)

**Established by reading; NOT measured in game.** No launch was spent on this — it is arithmetic over
two code sites that disagree, and the symptom below is derived, not watched.

**The asymmetry.** The push arm refuses to serve once supply falls under the restock reserve:
`CanServeNow` consults `ReservesRemainderForRestock` (`SupplyProvider.cs:1145`, `:1424`), and
`RestockThreshold` defaults to **50** (`SupplyProvider.cs:38`) with LOGISTICSCENTER setting no
`RestockActors`. The pull arm consults **no such thing** — `Rearmable.RearmTick` tests only
`provider.CurrentSupply < ammoPool.Info.SupplyValue` (`Rearmable.cs:105-106`) and otherwise serves.

**So a docked vehicle keeps taking batches through the band the aura arm has already stood down in,
and can take the Centre to 0.** Concretely: below 50 supply the Centre serves no infantry at all
(that dead band is pre-existing and documented in this file's 2026-08-11 SUPPLYCACHE entry), while a
docked `humvee` at `SupplyValue: 1` can keep drawing down to zero. Infantry standing beside the same
building are frozen out by a reserve the vehicle is not subject to.

**Why it is not obviously the wrong behaviour**, which is why this is filed rather than fixed: a
building has no drive-home trip to reserve supply for, so arguably the *push* arm's reserve is the
side that is wrong on LOGISTICSCENTER, not the pull arm's absence of one. That is the same shape as
the SUPPLYCACHE entry below, which was resolved by setting `RestockThreshold: 0` on the crate rather
than by changing engine logic. **The parallel fix would be `RestockThreshold: 0` on LOGISTICSCENTER**
— one YAML line, and it closes the dead band in the direction that makes both arms agree.

**Not taken here** because it is a live behavioural change to a shipped building on a branch whose
mandate is an engineer fix and a read-only report, and because the user owns tuning decisions in this
area and has not seen the drain-rate report yet.

**Confirm by:** stage a docked `humvee` and an infantryman both in range of a Centre held at 60
supply; the humvee should keep drawing past 50 while the infantryman gains nothing.

## 2026-08-23: [medium] OPEN, NOT FIXED — turning on classic mouse style makes a click on ANY ally select him instead of ordering the selected medic (found while: making the explicit heal order lock its patient, branch `wt/medic-order`, `main @ 7f6e6460`)

**Trigger, exactly: Settings → classic mouse style ON.** `Settings.cs:280` ships
`UseClassicMouseStyle = false` and nothing in the mod overrides it, so this is unreachable in a
default install — which is why it has never been reported. It is one checkbox away for any player who
prefers left-click-to-order, and there is no lint, no log line and no visual difference to warn them.

**Symptom.** With a medic selected, left-clicking a friendly soldier selects that soldier. The medic
receives no order. The `heal` cursor still displays beforehand, because `GetCursor` resolves through
`CursorForOrders` (`UnitOrderGenerator.cs:135`), which never consults the selection-override rule — so
the pointer promises an order the click does not deliver.

**Mechanism, established by reading rather than inferred.** `WorldInteractionControllerWidget.cs:110`
gates the whole order-instead-of-select branch on `useClassicMouseStyle`, so this rule is dead in the
default configuration and live in the other one. When it IS live, `InputOverridesSelection`
(`:178-182`) asks each resolved order's `TargetOverridesSelection`, and `AttendAlly`'s
(`AttendAlly.cs:101-110`) returns true only when the ally is ALREADY selected or ForceMove is held.
Neither holds for a plain click on an unselected ally, so the click falls through to selection.

**The obvious reading of why is wrong, and the wrong reading is the expensive part.** It is natural to
assume `AttackBase`'s targeter — whose `TargetOverridesSelection` returns true unconditionally
(`AttackBase.cs:783`) — rescues the click on a *wounded* ally, leaving only undamaged allies broken.
It cannot. `OrderForUnit` (`UnitOrderGenerator.cs:286-306`) returns the **first** accepting targeter in
descending priority and stops, and `AttendAlly` sits at 7 against `AttackBase`'s 6. `AttackBase`'s
targeter is never in the list `InputOverridesSelection` iterates. **The defect therefore affects every
ally equally, hurt or not** — the damage state has nothing to do with it.

**Confirm by:** setting classic mouse style, selecting a medic, left-clicking a wounded ally. Expect
the ally to become selected and the medic not to move.

**Fix shape when someone takes it:** `AttendAllyOrderTargeter.TargetOverridesSelection` returning true
for an allied actor is the one-line version, but it changes what a click on a friendly means for every
actor carrying `AttendAlly`, so it wants the same "does a plain click still select my own units"
check by hand. Not attempted here — the branch that found it was scoped to ordering and target
selection, and this is unreachable in the shipped configuration.

---

## 2026-08-21: [low] CLOSED — RESOLVED BY DECISION, NOT BY BUG FIX — `AmmoPool.ChooseResupplier`'s comment named SUPPLYCACHE as a seekable host while no `RearmActors` list contained it (found while: fixing the crate rearm defect, branch `wt/cache-rearm`, `main @ c8848496`)

`AmmoPool.cs:438` labels its second query "SupplyProvider hosts (TRUK, SUPPLYCACHE) with supply
remaining", and the filter immediately under it is `rearmInfo.RearmActors.Contains(a.Info.Name)`.
Every `RearmActors` declaration in the corpus is either `truk, logisticscenter` (infantry) or
`logisticscenter` (vehicles) — **none names `supplycache`** — so the cache branch of that comment is
unreachable. `AutoSeekSupplies` routes solely through this method (`AutoSeekSupplies.cs:281`), so no
unit ever walks to a dropped crate of its own accord; crates are push-only, serving whoever happens
to stand inside the aura.

**This was documented behaviour, not a defect**: `economy.md:47` stated the push-only property
correctly and deliberately. It was filed at [low] only because the CODE COMMENT contradicted the
corpus and would mislead the next reader. Two resolutions were possible and the choice was a design
call, not a cleanup: correct the comment to match the intent, or add `supplycache` to infantry
`RearmActors` so dropped crates become a destination.

> **CLOSED 2026-08-21 — the user took the second option.** All 14 infantry templates carrying a
> `Rearmable` now name `supplycache`, so soldiers path to their own dropped crates. **Nothing here
> was a bug and nothing was repaired**: the old behaviour was a working implementation of a design
> the user has since changed their mind about, and this entry is closed as superseded rather than
> fixed. The comment at `AmmoPool.cs` is now TRUE, so it was kept and extended (with the
> corpus-dependence of the actor names, and the strict `a.Owner == self.Owner` filter that stops
> "seek ammo" from also meaning "take enemy loot") rather than deleted. `economy.md:47` records the
> supersession with its date and reason. Pinned by `SupplyCacheSeekTest`, which enumerates the
> templates from the file so a NEW one that misses the list fails rather than silently not seeking.

## 2026-08-20: [high] PARTIALLY FIXED on `wt/aa-autotarget` — one AA soldier's overkill claim is EXACTLY the overkill threshold, so a single committed AA hard-skips the whole rest of the battery off a healthy aircraft (found while: diagnosing the user's live "my AA won't autotarget until I click" report, branch `wt/aa-autotarget`, `main @ e3a5250d`)

> **CORRECTION 2026-08-21 (`wt/aa-claim`) — the closing paragraph of this entry, "NOT FIXED HERE,
> deliberately … Needs the user's call", IS NOW STALE. Read this block before acting on it.**
> The user gave the call and the second layer — the missing release, described below under
> *"Nothing releases the claim when the shot resolves"* — is fixed. The claim is now owned by the
> attacker (`Actor.ClaimForAttack`, `Actor.cs:98`; `OverkillClaim` in
> `engine/OpenRA.Game/OverkillClaim.cs`) and handed back when the trigger resolves
> (`Armament.cs:483`). The `@stable` exposure this entry flagged is real and was accepted knowingly,
> not overlooked — `AutoTarget` has no per-profile gating, so this moves both bot profiles;
> the commit message says so, and the next benchmark baseline needs re-taking.
> `EstimatePercentDamage` itself is unchanged, so the `FiresEvGate` caller at
> `PoiOffensiveBotModule.cs:3586` sees no difference.
> **Still unreleased, deliberately out of scope:** a claim whose holder dies before firing (the
> t40-killed-marker-suppresses-to-t206 measurement below still stands) and one whose holder is
> ordered elsewhere before firing. Both remain bounded by the 60-tick decay, as before.
> Reasoning and the two placement traps: `WORKSPACE/DISCOVERIES.md`, 2026-08-21.

> **UPDATE 2026-08-20, and the entry below is now MEASURED rather than derived.**
> Run `260820_033930_p76804` of `test-aa-battery-volleys`, unfixed stock code:
>
> | lane | fired | spread | first shots |
> |---|---|---|---|
> | TEST (stock) | 4/4 | **173** | AA1=144 AA2=35 AA3=84 AA4=208 |
> | CTRL (`OverkillThreshold: -1`) | 4/4 | **7** | AA1=43 AA2=47 AA3=41 AA4=40 |
>
> Both helicopters ended 600/600. Consecutive first shots in the stock lane are 49, 60 and 64 ticks
> apart — **one decay period each**, the arithmetic confirming itself. The user-facing shape is
> therefore *"the battery fires one at a time instead of together"*, not *"fewer AA fire"*: all four
> engage in both arms, and only the spacing differs.
>
> **The comparison `>=` is now `>` (`AutoTarget.cs:1436`).** A single shooter's claim exactly
> equalling the threshold is the minimum possible commitment, not overkill; under `>=` the threshold
> could only be tripped, never approached.
>
> **This is expected to be a PARTIAL fix, and the residual is predicted rather than measured.**
> `AverageDamagePercent` is additive, so `>` lets exactly TWO shooters through per window: the first
> commits (0 → 100), the second passes `100 > 100` and commits (→ 200), and the third is blocked
> until a halving. Four AA should therefore engage in pairs across roughly one to two decay periods —
> a predicted spread near **60–130 ticks**, down from 173 but still far above the control's 7.
> **`test-aa-battery-volleys` is likely to stay RED after this fix**, which is the documented
> multi-layer pattern in `AUTOTEST.md` ("fix what I can, leave the test RED for the unfixed parts"),
> not a sign the change did nothing. The remaining layer is the release defect described below, left
> unfixed by explicit instruction so this run gives a clean before/after for exactly one change.

**This is an INCOMPLETE FIX, not a regression.** `afa18718` (2026-08-12) is intact and nothing in
this path has changed since — the only commits touching `AutoTarget.cs`/`Actor.cs` after it are
`3d9500e2`, `be6a2ed0`, `8afcbbf8`, `61546a51`, none of which touch the accumulator or the threshold.
That commit reduced the magnitude of the defect and left its structure standing, and its own message
says so: it treats a residual 55-tick stand-down as the correct cost of "a 100 claim" and ships
`test-aa-overkill-suppression` with `MaxSuppressionTicks = 90`, so the shipped test passes on the
residue.

The residue is a boundary equality. Three shipped constants meet exactly:

1. `EstimatePercentDamage` clamps ONE shooter's claim at **100** (`AutoTarget.cs:1654`,
   `Math.Min(totalDamage * 100 / health.MaxHP, 100)`).
2. `OverkillThreshold` defaults to **100** (`AutoTarget.cs:217`).
3. `ChooseTarget` skips on **`>=`** (`AutoTarget.cs:1436`).

So the smallest possible unit of commitment is precisely sufficient to trip the hard skip. Against
aircraft an AA soldier always reaches the clamp and never sits below it: MANPAD `Damage: 3000`,
`Penetration: 15` against the Halo's `Armor.Thickness: 3` (`^Airborne`, `aircraft.yaml:22-23`) so the
`penetration < thickness` branch is not taken, and no `Versus` table —
`min(3000 * 100 / 600, 100) = 100`. There is no aircraft in the roster for which an AA soldier claims
less.

Consequences, and they line up with every clause of the user's report:

- **A second AA will not autotarget a completely healthy helicopter** while the first one's claim
  stands. Nothing is wrong with the target: full health, in range, clear line, no fog.
- **A manual order fires immediately**, because `AttackBase.AttackTarget` never consults
  `ChooseTarget`. Already measured in `test-aa-overkill-suppression` lane B (fired at t38, inside a
  window where the auto lane was still standing down).
- **It looks intermittent**, because release is only the `WorldTick % 60` halving
  (`Actor.cs:309-310`) — a scan landing just before the boundary waits nearly a full period, one
  landing just after does not, and `^CamoSoldier` re-scans on a per-unit random 16–32 tick interval
  (`infantry.yaml:289-290`) on top of that.
- **The battery serialises rather than volleys.** Each new joiner re-loads the accumulator, so AA
  engage one at a time. `b47cdf7a` measured this shape directly at the pre-clamp magnitude: four AA,
  one helicopter, nobody ordered, first shots at **t37, t200, t386, t571** — 34 real seconds for four
  units to engage one target. The clamp divides the per-joiner spacing by roughly three; it does not
  make them volley.

**Nothing releases the claim when the shot resolves.** The accumulator models "damage is coming" and
is never told the missile hit, missed, or was never fired. `test-aa-overkill-suppression` measured
that a marker **killed at tick 40** still suppressed its neighbour to t206 — only decay clears it.
With MANPAD `Inaccuracy: 256` against moving aircraft, "the shot missed and the battery stayed blind
anyway" is ordinary play, not an edge case.

**Established:** READ at HEAD for every constant and every call site, then **DERIVED** by arithmetic
over them, plus the two prior MEASURED runs cited above (`b47cdf7a`, and the lane figures in
`afa18718`). **Nothing was launched for this entry.**
**Confirm by:** `tools/autotest/scenarios/test-aa-battery-volleys`, added on this branch and
**COMMITTED UNRUN** — two identical 4-AA batteries differing only in `OverkillThreshold`, which fails
when the stock battery fields fewer shooters than the control.

**NOT FIXED HERE, deliberately.** Every candidate fix — relaxing `>=` to `>`, or dropping the clamp
below the threshold — sits on shared behaviour. `AutoTarget` has no per-profile gating, and
`EstimatePercentDamage` has exactly one caller outside itself: `PoiOffensiveBotModule.cs:3586`, the
fires-economy gate, with `FiresEvGate: true` on **both** bot profiles —
`PoiOffensiveBotModule@experimental` (`ai.yaml:586`) and `PoiOffensiveBotModule@stable`
(`ai.yaml:2184`). `afa18718` disclosed both of those as `@stable` moves when it changed the same
function; its line references have since drifted (it cited `:3375`, `ai.yaml:515` and `:1960`) and
the numbers above are re-derived at HEAD. Needs the user's call.

## 2026-08-20: [medium] FIXED (unrun) on `wt/aa-autotarget` — `test-aa-overkill-cadence` had been measuring nothing since 2026-08-12: the fix for the estimate's over-count silently disarmed the scenario that exploited it (found while: the AA autotarget diagnosis above, branch `wt/aa-autotarget`, `main @ e3a5250d`)

The scenario decoupled the overkill mark from the damage by splitting MANPAD into a real Air warhead
(`Damage: 20`) and a `ValidTargets: Ground` warhead (`Damage: 3000`) that the damage path would
refuse to apply to an airborne target but that `EstimatePercentDamage` would count anyway. Its
`weapons.yaml` stated the premise plainly: the estimate *"does NOT check ValidTargets at all"*.

That was true when written and **stopped being true four days later.** `afa18718` added
`if (!warhead.IsValidAgainst(target.Actor, attacker)) continue;` to the estimate's warhead walk
(`AutoTarget.cs:1623`) — the exact over-count the split depended on. The Ground warhead is now
skipped, the estimate collapses from 3020 to 20, and the mark against a 600-HP Halo is
`20 * 100 / 600 = 3` against a threshold of 100. **Suppression is simply off.**

**The failure mode is a false green of the shape the file itself documented.** Its own PITFALL block
warned that a 1/10 scaling had once produced *"a clean pass with all four AA firing, which looks
exactly like 'suppression does not sustain' and actually meant 'suppression was never switched on'"*.
The same trap was then re-armed from the other side, by the fix. `afa18718` touched
`test-aa-overkill-suppression.lua` and not this scenario, and `git log` shows no commit to it since
`b47cdf7a` — so it has not been run since it was disarmed. It is a pure diagnostic that cannot fail
on behaviour, so a bad setup here reports a confident wrong answer rather than an error.

Repaired on this branch by dropping the payload split for a projectile `RangeLimit` cut (24c0 → 4c0):
missiles expire ~16 cells short, nothing takes damage, and the warheads are left stock so the
estimate is the real shipped arithmetic. That decoupling depends on no divergence between the
estimate and the damage model, so a future fix to either cannot silently disarm it the same way.

**Established:** READ at HEAD — both the scenario's premise and the commit that invalidated it.
**DERIVED** for the collapsed estimate arithmetic. **The repair is COMMITTED UNRUN.**
**Confirm by:** running `test-aa-overkill-cadence` and checking the first-shot spacing is not the
prompt all-four-fire pattern that the disarmed setup would produce.
**Also corrected in passing:** the scenario's commentary states the Halo's `Armor.Thickness` as 10;
the value on `^Airborne` at HEAD is 3. Conclusion unaffected (Penetration 15 exceeds both), but the
10 should not be carried forward.

## 2026-08-20: [high] OPEN, NOT FIXED — the `object-proximity` cover ladder is geometrically unreachable: the three largest concealment bonuses in the game can never be granted (found while: extracting defects from the ambush/concealment research programme, branch `wt/bug-filing`, `main @ 57822b4e`)

Standing next to cover contributes **exactly zero** concealment. `@InCover1/2/3`
(`infantry.yaml:707-715`, worth **+1/+2/+3** — bigger than prone and dug-in combined) consume
`object-proximity`, and the **only** emitter in the mod is
`ProximityExternalCondition@ObjectProximity` on `^TreeHusk` — a *burnt* tree (`husks.yaml:117-121`).
Living trees emit nothing.

**The reason it cannot fire is geometric, not the earlier and widely-repeated "you must burn the
forest down first".** That version was believed by three separate parties and is wrong:

1. `Range: 384` is 0.375 cells; the 20 per-actor overrides go as low as **182** (0.18 cells) and as
   high as 640 (`husks.yaml:120,131,141,…,322`).
2. The trigger point is the husk's own cell centre (`ProximityExternalCondition.cs:72-78,104`).
3. Containment is a strict `<` on horizontal distance (`ActorMap.cs:143-144`).
4. `^TreeHusk` carries `Building: Footprint: x` with **no `Passable`** (`husks.yaml:105-107`), so it
   **blocks infantry** — unlike a living `^Tree`, which carries `Passable: PassClasses: tree`
   (`decoration.yaml:12-14`) and is walkable. Nobody can stand on the husk's cell.
5. Infantry occupy quantised sub-cell offsets (`MapGrid.cs:117-125`, not overridden by the mod).

Nearest occupiable sub-cell is therefore **244–771 WDist** against radii of **182–640**, and across
all **23** husk types (`^TreeHusk` + 22 inheritors) **zero are reachable**. Burning the forest down
does not help; the ladder is dead outright, not merely misdirected.

One residual accident: a soldier standing under a live tree when it burns gets the husk spawned on
his own cell (`decoration.yaml:108-109`), and `Building` does not eject occupants — so 12 of 23
types become reachable from inside, until he takes one step.

**Established:** READ at HEAD for every premise (radii, impassability, the strict `<`, sub-cell
quantisation), then **DERIVED** by arithmetic over those four sites. **Nobody has watched a soldier
fail to receive the condition.**
**Confirm by:** a scenario that places a rifleman adjacent to each husk type and asserts
`object-proximity` is never granted. No such scenario exists. Cheaper still: the condition is
synced, so a debug readout of `ExternalCondition@ObjectProximity`'s grant count would settle it in
one run.
**First documented 2026-07-28** in `WORKSPACE/recon/260728-trees-concealment.md`, restated
2026-08-19 in `260819-infantry-visibility-stances.md` (*"almost certainly not intended"*), never
acted on.

## 2026-08-20: [high] OPEN, NOT FIXED — the widened ambush (halt-before-contact + the smart spring table) is granted only to bot-posted units; no human opt-in path ships (found while: extracting defects from the ambush/concealment research programme, branch `wt/bug-filing`, `main @ 57822b4e`)

A human clicking the Ambush button gets the narrow stance. The bots get the feature. Stages 2–4 —
the attack-move halt that stops a squad walking into contact while it is still unseen, and the
four-trigger spring table — sit behind `AutoTargetInfo.AmbushTacticsCondition`, wired to
`enable-ambush-tactics` (`defaults.yaml:320`).

**Exactly one thing grants it:** `LaneAmbushBotModule`, per-unit, only to the ≤4 units it posts
itself (`LaneAmbushBotModule.cs:451,474`), on both bot profiles. The trait's own header states the
gap outright at `LaneAmbushBotModule.cs:48`: *"humans / Normal / Rush / Turtle never instantiate
it, and `enable-ambush-tactics` is granted ONLY by this module and only to its own posted units."*
Meanwhile `AutoTarget.cs:93` advertises a path that does not exist: *"a human opt-in / bot ledger
commit / test map grants."*

**Five passing autotests do not contradict this — they pass *because* they grant the gate by hand
in Lua.** `test-ambush-detection`, `test-ambush-enemy-stops`, `test-ambush-convoy`,
`test-ambush-fast-convoy` and `test-case01-forest-ambush` all call
`GrantCondition("enable-ambush-tactics")`; two of them name "comment out the GrantCondition line"
as their own RED baseline. **Every green ambush test validates a configuration no player can
reach.**

**Established:** READ at HEAD — grant sites, gate name and the module header all verified directly.
The *consequence* (a human never sees the behaviour) is DERIVED from "these are the only grant
sites", which is a grep-completeness argument over `mods/**` and `engine/**`.
**Confirm by:** this needs no run; it is settled by grep and is listed here only so nobody
schedules one. What *would* need a run is whether granting it to humans is desirable —
`AmbushMinSpringThreshold` / `AmbushHighSpringThreshold` have never been tuned against human play
(`AutoTarget.cs:104` says they are "meant to be tuned in autotest").
**Note for whoever acts on it:** the grantor seam already exists and is lint-clean
(`ExternalCondition@ambushtactics`, `defaults.yaml:344-345`), and there is an established idiom for
the exact grant shape next door — `GrantConditionOnHumanOwner@tacpos` (`defaults.yaml:44-45`). The
gate was authored default-off for benchmark byte-identity, so turning it on is a behaviour change
needing sign-off.

## 2026-08-20: [medium] OPEN, NOT FIXED — two `dugin` timer bugs: a unit that has never moved never digs in, and `dugin` can be granted mid-stride (found while: extracting defects from the ambush/concealment research programme, branch `wt/bug-filing`, `main @ 57822b4e`)

Both in `GrantConditionOnMovement` (`GrantConditionOnMovement.cs:44,52-58,68-80`), which drives
`dugin` / `moving` for all infantry (`infantry.yaml:139-142`).

**(a) A map-placed soldier never reaches the dug-in tier.** `cooldown` initialises to `0` (`:44`)
and `Tick` decrements it unconditionally while `dugin` is ungranted (`:54-56`), so it goes to `−1`
on tick 1 and never equals `0` again. It is re-armed **only** in the stop branch (`:71`), which
requires the unit to have been moving first. A soldier placed by the map editor or spawned by a
scenario and never ordered anywhere **sits one tier below its intended concealment forever**.

**(b) `dugin` and `moving` are not mutually exclusive.** If a unit stops (arming `cooldown = 200`)
and moves again before it expires, the movement branch (`:73-79`) grants `moving` and revokes the
still-condition but **does not reset `cooldown`** — and `Tick` has no "am I actually still?" guard.
The countdown completes mid-stride and grants `+1` to a running soldier, held until the next
stop→move transition. So a runner can be carrying both `moving` (−1) and `dugin` (+1).

Also: `TimeToBeStill: 200` at `Timestep: 60` ms (`mod.yaml:380-383`) is **12.0 s**, not the 8 s
several documents and one autotest assume.

**Established:** READ from code — both control-flow paths traced line by line at HEAD. The
*consequences* stated (map-placed soldier stuck a tier low; runner carrying `dugin`) are DERIVED
from that trace, **not observed**.
**Confirm by:** `test-visual-gauge-truth` already reasons correctly about bug (a) and is the
cheapest existing probe — but see the separate entry below, it is mis-calibrated for an unrelated
reason and would fail for the wrong cause. A clean confirmation is a scenario asserting a
never-ordered rifleman's `CurrentVisibility` after 300 ticks.

## 2026-08-20: [medium] OPEN, NOT FIXED — the −2 firing detectability penalty applies to the `primary` armament only, so RPGs, grenade launchers and garrison fire are free (found while: extracting defects from the ambush/concealment research programme, branch `wt/bug-filing`, `main @ 57822b4e`)

`GrantConditionOnAttackInfo.ArmamentNames` defaults to `{ "primary" }`
(`GrantConditionOnAttack.cs:25`) and is filtered at `:133-134`. `GrantConditionOnAttack@Firing`
(`infantry.yaml:722-726`) **does not override it**.

Verified armament names that therefore cost nothing: `secondary` carrying the RPG
(`infantry.yaml:1154-1156`), `secondary` carrying the GrenadeLauncher (`:1411-1413`), and
`garrisoned` — the armament used from inside a building (`:1271-1274`, `:1577-1580`). A soldier
firing an RPG from a window pays **zero** detectability, while the same soldier firing his rifle
pays −2 for 12 ticks.

Whether that is intended is a design call; the code is unambiguous.

**Established:** READ at HEAD — the default, the filter and the four armament `Name:` fields all
read directly.
**Confirm by:** no run needed to establish the code fact. A run would only tell you whether players
notice.

## 2026-08-20: [medium] OPEN, NOT FIXED — prone has given zero damage reduction since February 2024: exactly one of 109 `DamageTypes:` declarations carries a `Prone*` token, and that weapon is unreferenced (found while: extracting defects from the ambush/concealment research programme, branch `wt/bug-filing`, `main @ 57822b4e`)

`^CamoSoldier` still configures five tiers of `ProneDamageModifiers` (`infantry.yaml:297-302`,
`Prone10Percent` … `Prone80Percent`) and prone is reached constantly (`ProneCondition: deployed ||
suppressed > 30 || !moving || critical-damage`, `infantry.yaml:294`). But
`InfantryStates.GetDamageModifier` returns `100` unless the incoming warhead declares a matching
string (`InfantryStates.cs:195-205`, the `Where(x => damage.DamageTypes.Contains(x.Key))` at
`:203`).

**Counted at HEAD: 109 `DamageTypes:` declarations across `mods/ww3mod/rules/weapons/`, of which
exactly one carries a `Prone*` token** — `weapons-superweapons.yaml:399`, `DamageTypes:
Prone30Percent`, on `EmpBomb`, which no actor in the repo references. Every bullet is
`BulletDeath`, every shell `ExplosionDeath`. **Prone reduces damage from nothing.**

Cause: `1802191e` (2024-02-13, *"Remove all ProneXXPercent DamageTypes"*) stripped them from six
weapon files and missed the one dead line. Empty commit body — no rationale recorded.

Prone does still protect by a route that never appears in damage arithmetic: the hitshape shrinks
from radius 30 to 20 (`infantry.yaml:143-150`), roughly a 56% smaller cross-section, at a cost of
40% speed. That is a hit-probability effect, not damage reduction.

**Established:** READ and **counted** at HEAD (`grep -c` over the weapons directory; the 109/1 split
is a direct count, not an estimate). The claim that no live weapon reaches the modifier is READ.
**Confirm by:** no run needed for the code fact. Note this also means
`WORKSPACE/recon/260728-deploy-prone.md:13,52` over-credits prone with a benefit it lost in 2024 —
flagged there for curation, not edited.

## 2026-08-20: [medium] OPEN, NOT FIXED — the Ambush tooltip promises "Zero aim delay" and a 15-tick (infantry) / 30–50-tick (vehicle) aim delay is charged in full when the trap springs (found while: extracting defects from the ambush/concealment research programme, branch `wt/bug-filing`, `main @ 57822b4e`)

`mods/ww3mod/chrome/ingame-player.yaml:373` reads *"Zero aim delay — turrets are already aimed when
firing begins."* It is false.

`Armament.AimingDelay` defaults to **15 ticks** (`Armament.cs:45`) and is overridden to **30–50** on
nine vehicles (`vehicles-america.yaml:387,635,761,1056`;
`vehicles-russia.yaml:216,458,576,702,963`). `CheckFire` **resets it to the full value whenever the
target changes** (`Armament.cs:345-350`), and `Armament.cs:327` blocks the shot outright while
`IsAiming` (`:677`, `AimingDelay > 0`). Ambush's pre-aim never touches it: `PreAimAtTarget`
(`AutoTarget.cs:752-753`) rotates facing and never reaches an armament. So the delay is paid **in
full after the trap springs** — around 3 s of an MBT standing in the open not shooting.

> **This entry corrects the ground-truth audit, which is normally the ranking document.**
> `260820-ambush-cover-detection-audit.md` §2.5 concludes *"there is no aim delay for anyone"* — it
> examined `FireDelay` (3 ticks, negligible) and `FacingTolerance` and **never looked at
> `AimingDelay`**. `260820-coordinated-ambush.md` is the one that is right here. The audit outranks
> the design docs generally; it does not on this point.

**Established:** READ at HEAD — the default, the nine YAML overrides, the reset-on-target-change and
the fire gate all read directly. The **~3 s felt cost is DERIVED** (50 ticks × 60 ms) and not
observed.
**Confirm by:** `test-case01b-detect` was authored specifically to measure time-to-first-shot spread
and **has never been run once** — it is the highest-value single run available and settles this
entry and the next one together.

## 2026-08-20: [medium] OPEN, NOT FIXED — the "simultaneous" ambush volley smears over 1–2 seconds: allies are latched, not fired (found while: extracting defects from the ambush/concealment research programme, branch `wt/bug-filing`, `main @ 57822b4e`)

`TriggerNearbyAmbushAllies` (`AutoTarget.cs:976-993`) sets `ambushTriggered = true` on each nearby
Ambush-stance ally and **never makes any of them shoot**. Each fires on its own next idle scan, and
WW3MOD overrides the infantry scan interval to **16–32 ticks** on `^CamoSoldier`
(`infantry.yaml:286-290`) against an engine default of 3–8 (`AutoTarget.cs:199,202`), drawn
randomly per unit from `SharedRandom` (`:1157`). At 60 ms/tick that is a spread of roughly
**1.0–1.9 s**.

This stacks with the aim delay in the entry above; the two are the same order of magnitude, so
fixing either alone leaves about half the lag.

> **Two claims in the design docs are wrong here and should not be carried forward.**
> `260820-ambush-failure-modes.md` F2 and the audit §2.2 both state the latch is terminal for
> humans ("cleared only by changing stance"). **The code clears it on the human path**:
> `AutoTarget.cs:746` sets `ambushTriggered = false` in the `else` of the `stage3` branch when a
> scan finds no target. The "SPRUNG is terminal, DO NOT clear" comment they quote sits in the
> **gated Stage-3 branch only** (`:740-745`) and applies to bot-posted units.
> `260820-coordinated-ambush.md` §5 is right. Do not "fix" a human re-arm bug that does not exist.

**Established:** READ at HEAD for every mechanism (the latch-only trigger, the scan-interval
override, the engine default, the RNG draw). The **1–2 s figure is DERIVED** from the interval and
`Timestep: 60`; nobody has observed the spread. The MiniYaml merge was **not** traced to confirm
nothing later overrides the scan-interval fields — note four map `rules.yaml`/`scenarios.yaml` files
also set 16/32, which is consistent but not proof of the general case.
**Confirm by:** `test-case01b-detect` — exists, never run, authored for exactly this measurement.
**Trap for whoever fixes it:** the obvious repair (force allies to scan immediately) routes through
`ScanForTarget`, which re-arms its timer off `SharedRandom` and would shift the shared RNG stream,
breaking the frozen `@stable` baseline. The codebase already solved this for target preemption —
copy that pattern rather than inventing one.

## 2026-08-20: [medium] OPEN, NOT FIXED — map density is frozen at load, so a forest shelled flat still grants full cover, full concealment and still refuses rifle shots (found while: extracting defects from the ambush/concealment research programme, branch `wt/bug-filing`, `main @ 57822b4e`)

`Map.UpdateDensityForBuilding` and the shadow-update queue both exist, and **all three call sites
are commented out**: `Building.AddedToWorld` (`Building.cs:372-383`),
`Building.RemovedFromWorld` (`:391-397`) and the per-tick flush in `World.cs:512-517`.

The disable was deliberate — the comments date it to 260503 and give the reason (recalc was too
expensive mid-game, visible lag on building destruction). **The defect is the downstream
consequence, which nothing accounts for:** `Map.DensityLayer` and `map.ShadowLayer` keep their
map-load values for the whole match, so after a treeline is destroyed the ground beneath it still
grants `DensityModifiesDamage` reduction, still subtracts from observer strength via
`Map.ForestGroundShadow`, and still trips `ClearSightThreshold` so rifles decline the shot through
foliage that is no longer there.

**This is a live trap for the Take Cover feature under design**: a best-protected-cell search reads
the frozen layer, so it would confidently march a squad onto burnt ground and report success.

**Established:** READ at HEAD — all three commented call sites read directly, including their
dated rationale. The **consequences are DERIVED** from "the layer is never mutated after load"
plus the consumer list; **no one has shelled a forest and re-measured**.
**Confirm by:** a scenario that records `CurrentVisibility` for a soldier in trees, destroys the
trees, and re-records. No such scenario exists.

## 2026-08-20: [medium] FIXED 2026-08-20 (`wt/invisibility-fix`) — detectability level 10 is unreachable only because the two units that could reach it are `~disabled`; re-enabling the Sniper makes total invisibility live immediately (found while: extracting defects from the ambush/concealment research programme, branch `wt/bug-filing`, `main @ 57822b4e`)

**This resolves the contradiction flagged as unresolved in `WORKSPACE/ambush-programme/README.md`
§3.2a**, where the audit said infantry CV tops out at 9 and the legibility strand said 10 is
reachable. Both were reasoning from incomplete premises.

The clamp is `[1, MapLayers.VisionLayers - 1]` = **[1, 10]** (`VisionLayers = 11` at
`MapLayers.cs:75`; clamp at `Detectable.cs:92-93,114-115`) — the legibility strand is right about
the clamp. Level 10 is undetectable by standard vision because no band carries strength 11 and
reveal is strictly greater (`MapLayers.cs:579`).

**Reachability, counted at HEAD.** `mods/ww3mod/rules/ingame/infantry.yaml` contains exactly three
`Detectable.Vision` declarations: base **3** (`:97`), and **5** on `^SN` (`:1542`) and `^SF`
(`:1988`). Live modifiers are prone +1, dugin +1, and veterancy +1/+2/+3/+4
(`defaults.yaml:211-222` — the largest live concealment lever in the game, and nothing in the UI
says so).

- Base infantry: `3 + 4 + 1 + 1 = 9`. The audit's ceiling is right **for base infantry**.
- `^SN` / `^SF`: `5 + 4 + 1 + 1 = 11 → clamps to 10`. **Level 10 is arithmetically reachable
  without the cover ladder at all** — the legibility strand's `5+3+1+1` route via the dead `+3`
  cover term was the wrong route to a right answer.
- Both `^SN` and `^SF` carry `Prerequisites: ~disabled` (`:1535`, `:1980`), so neither can be
  fielded today.

**So the ruling recorded in README §7 — clamp minimum detectability to 1, "land it BEFORE fixing
§3.2" — is under-scoped.** Total invisibility goes live the moment **either** the cover ladder is
repaired **or** the Sniper is made buildable, and the second is a one-word YAML edit that nobody
would connect to concealment.

**Established:** READ and counted at HEAD (the three `Detectable.Vision` sites, the two
`~disabled` prerequisites, the clamp constants, the veterancy block). The ceilings are **DERIVED**
by addition over those sites. **Not measured.**
**Confirm by:** a scenario granting `rank-veteran == 4` to a stationary rifleman and asserting
`CurrentVisibility == 9`; the same with `^SN` re-enabled asserting 10.

> **FIXED 2026-08-20 on `wt/invisibility-fix`, both halves, on the user's explicit ruling.** The
> ceiling is now `VisionLayers - 2` = 9, so concealment cannot reach the top vision band and
> standard vision always wins somewhere; and reveal is non-strict (`MapLayers.IsDetected`), so a
> matching observer strength reveals. Note the entry above is ALSO wrong about fieldability in the
> way `260820-synthesis.md` §1.1 records: `^SN`/`^SF` are templates, and the faction variants
> (`SN.america` etc.) override `Buildable`, so both units ship. That made this live rather than
> latent, which is why it was fixed rather than queued. Reveal moving out one band is a global
> balance change and the `@stable` benchmark control changed with it — the next baseline must be
> re-taken knowingly. Not launched: verified by NUnit plus a new scenario,
> `tools/autotest/scenarios/test-detect-no-invisibility`, which has not been run.

## 2026-08-20: [medium] OPEN, NOT FIXED — one tree gives exactly zero damage reduction and one rock gives the maximum; the shipped comment saying otherwise is wrong, and no building carries density at all (found while: extracting defects from the ambush/concealment research programme, branch `wt/bug-filing`, `main @ 57822b4e`)

`DensityModifiesDamage` on `^Infantry` (`infantry.yaml:37-45`) sums `Map.DensityLayer` over a 3×3
window and picks the highest threshold ≤ that sum. Thresholds are `15: 94`, `30: 88`, `50: 80`
(`:42-45`) — a 20% maximum.

Counted at HEAD, **`Building.Density` is declared in exactly one file in the entire mod** —
`mods/ww3mod/rules/ingame/decoration.yaml`, 33 declarations:

- **Trees** `T01`–`T07`: `10` on one cell of a 2×2 footprint (`:104,117,130,143,156,169,182`);
  `T08` gives `5` (`:195`).
- **Rocks** `ROCK1`–`ROCK7`: **50 per occupied cell** (`:469,475,481,487,493,499,505`).
- **Tank traps**: `20` (`:531,546`).

So: **one tree beside you = 10, below the 15 floor = zero protection.** One rock cell anywhere in
your 3×3 = 50 = the full 20% instantly. **One rock outperforms five trees.** The shipped comment at
`infantry.yaml:41` claims *"a lone treeline barely helps"*; it does not help at all.

> **Correction to the design docs.** `WORKSPACE/ambush-programme/README.md` §3.5 and
> `260820-cover-protection-and-take-cover.md` §1.6 both claim *"a single density-50 building
> neighbour clears all three tiers — standing next to a house is maximal forest cover."* **No
> building, defence or civilian structure carries `Building.Density` anywhere in the mod** —
> `grep '^\s*Density:' mods/ww3mod/` returns `decoration.yaml` and nothing else. The house claim is
> wrong; the rock claim is right and is the one worth acting on.

`^CivField` (`civilian.yaml:129`) is a related instance: it carries a `Building:` block with no
`Density:`, so fields — authored to read as cover — contribute nothing.

**Established:** READ and counted at HEAD (`grep` over all mod YAML for `Density:` declarations,
then each value read). The threshold consequences are **DERIVED** arithmetic. **Whether maps
actually place `ROCK*` near contested ground is unexamined** — that is a map-data question nobody
has opened, and it decides whether this matters in play.
**Confirm by:** map inspection (no run needed) for the placement question; two equal squads, one in
deep trees and one in the open, for whether 20% is perceptible at all — but 20% sits inside normal
combat variance, so that needs several seeds.

## 2026-08-20: [low] OPEN, NOT FIXED — springing an ambush force-deploys every nearby garrison regardless of its stance, and the code comment says it checks (found while: extracting defects from the ambush/concealment research programme, branch `wt/bug-filing`, `main @ 57822b4e`)

In `TriggerNearbyAmbushAllies` (`AutoTarget.cs:976-993`) the infantry branch correctly filters on
`allyAutoTarget.Stance == UnitStance.Ambush` (`:985`). The garrison branch immediately below does
not:

```
// Also trigger garrisoned buildings in Ambush stance
var gm = ally.TraitOrDefault<GarrisonManager>();
if (gm != null)
    gm.TriggerAmbush();
```

(`:987-991`.) Every friendly `GarrisonManager` within the 10-cell coordination radius is triggered
whether or not it was ever put in Ambush — and `GarrisonManager.TriggerAmbush` force-deploys
shelter occupants to firing ports. **The comment asserts a stance filter that the code does not
apply.**

**Established:** READ at HEAD. The consequence (a garrison you never set to Ambush pops its
occupants into ports) is **DERIVED** from the call, and `TriggerAmbush`'s internals were **not**
traced end to end by this pass.
**Confirm by:** `TriggerNearbyAmbushAllies` has **zero NUnit coverage** — `AmbushTacticsTest.cs`
pins the pure trigger predicate only. A unit test placing a non-Ambush garrison inside the radius
is the cheapest confirmation and needs no game launch.

## 2026-08-20: [low] OPEN, NOT FIXED — a bot ambusher that halts before contact never resumes its attack-move: `haltedForAmbush` is never cleared and the cached destination is never re-issued (found while: extracting defects from the ambush/concealment research programme, branch `wt/bug-filing`, `main @ 57822b4e`)

`AttackMoveActivity` sets `haltedForAmbush = true` at `:167` and cancels the move child at `:169`.
The flag is declared at `:36`, drained at `:84`, and **never set back to false anywhere in the
file**. `OriginalDestination` is faithfully cached (`:44,57,59`) and **nothing re-issues it**. So a
squad that halts to ambush stops permanently and never arrives.

Reachable today **only through the bot-only gate** (the Stage-2 halt), so it affects bot ambushers
and not players — but it becomes a player-facing bug the instant the gate in the entry above is
opened to humans. Sequence accordingly.

**Established:** READ at HEAD — the set, the drain and the absence of any clear were all read
directly; `OriginalDestination` has no reader. **Not observed in a running game.**
**Confirm by:** a scenario giving a gated Ambush unit an attack-move across a contact, then
asserting it reaches the destination after the engagement ends. No such scenario exists;
`test-ambush-enemy-stops` asserts the halt, not the resume.

## 2026-08-20: [low] OPEN, NOT FIXED — `StancePositioningExecutor`'s file header claims humans are byte-identical; humans have been granted the token since Phase 3 shipped (found while: extracting defects from the ambush/concealment research programme, branch `wt/bug-filing`, `main @ 57822b4e`)

`StancePositioningExecutor.cs:50-52` reads: *"Gated `enable-tactical-positioning ||
enable-ai-experimental`: default-off everywhere except experimental bots (the former is granted by
nothing in Phase 2; humans get it in Phase 3). @stable/@normal/humans are byte-identical."*

`defaults.yaml:44-45` grants `enable-tactical-positioning` to every human-owned combatant via
`GrantConditionOnHumanOwner@tacpos`, with a comment describing it as *"Phase-3 human enablement
(RATIFIED default-ON)"*. Phase 3 shipped. **Humans are not byte-identical, and the header is the
first thing a reader sees.**

The same file's *body* comment (`:305-323`) is current and correct, which is exactly why the stale
header is dangerous — a reader who trusts the header stops before reaching the body. The
`06f0605a` staleness sweep, which corrected byte-identity claims elsewhere, missed this file.

**Established:** READ at HEAD — both the header text and the grant read directly. This is a
documentation defect with no runtime symptom.
**Confirm by:** nothing to run. One-line fix when someone is next in the file; not fixed here
because this branch is filing only.

## 2026-08-20: [low] OPEN, NOT FIXED — `Makefile`'s `clean` target still uses `find -exec`, the exact exit-code-swallowing hole that was fixed in `all` the same day (found while: extracting defects from the ambush/concealment research programme, branch `wt/bug-filing`, `main @ 57822b4e`)

`Makefile:190` runs `@find . -maxdepth 1 -name '*.sln' -exec $(DOTNET) clean \;`. **`find` exits 0
whatever the command it ran returned**, so a failed clean reports success and every consumer
downstream believes it. (Still OPEN as of 2026-09-02. This entry originally cited *two* such lines,
`:192` mono and `:194` dotnet; the mono branch was deleted with `RUNTIME=mono` support on
`wt/mono-removal`, so there is now one line, renumbered. The defect itself is untouched.)

The `all` target at `:182-186` was repaired for precisely this and carries a comment naming the
consequence: *"NOT `find -exec`: find exits 0 whatever the command it ran returned, so a failed
mod-solution build reported success here and every consumer downstream believed it — including the
launchers, which then started the game on stale binaries."* `clean` was not given the same
treatment.

Lower severity than the `all` case: a failed clean leaves stale artefacts rather than shipping
them, and the next build usually overwrites. But it is the identical defect and the fix is the
identical `@set -e; for sln in $(MOD_SOLUTION_FILES); do …; done` loop.

**Established:** READ at HEAD — both targets read and compared directly. The `find` exit-code
behaviour is POSIX-specified, not tested here.
**Confirm by:** `make clean` with a deliberately broken solution file; expect exit 0. Not run —
this branch is filing only.

## 2026-08-20: [low] RESOLVED 2026-08-20 as a side effect of `wt/invisibility-fix` — `WithSpottedDecoration.VisionCovers` accepts an observer band one strength short of actually revealing (masked today by the truth gate) (found while: extracting defects from the ambush/concealment research programme, branch `wt/bug-filing`, `main @ 57822b4e`)

`VisionCovers` skips a band only when `visionInfo.Strength < requiredStrength`
(`WithSpottedDecoration.cs:142`), i.e. it **accepts `Strength == required`**. Actual reveal is
strictly greater: `ResolvedVisibility[puv] > visibility` (`MapLayers.cs:579`). So the pre-filter is
optimistic by one band — roughly three cells of approach.

**No wrong badge can reach the screen today.** `IsSpotted` checks the authoritative
`self.CanBeViewedByPlayer(owner)` last (`:115`), and the file's own comment says the truth gate is
what stops the optimism becoming a false positive. Filed because the looseness is real, undocumented
as an off-by-one, and would surface immediately if anyone reordered the checks for performance —
which the comment invites by explaining the gate is "checked last because it is the most expensive."

> **RESOLVED 2026-08-20, accidentally and in the right direction.** Reveal is now non-strict
> (`MapLayers.IsDetected`), so accepting `Strength == required` is exactly what reveal does —
> `VisionCovers` was pre-emptively correct for a comparison that did not exist yet. **Do not
> "fix" it back to `<=`**, and if the reveal comparison is ever reverted to strict, this entry
> reopens with it. The two are one fact.

**Established:** READ at HEAD — the comparison, the reveal comparison and the ordering of the truth
gate all read directly. **DERIVED:** that the two comparisons differ by one band. Not observed, and
by design not observable.
**Confirm by:** nothing to run while the gate stands. Relevant only as a note on the file.

## 2026-08-20: [low] OPEN, NOT FIXED — `test-visual-gauge-truth` and `test-visual-concealment-gauge` are calibrated one tier low: both omit `prone`, which is granted on `!moving` (found while: extracting defects from the ambush/concealment research programme, branch `wt/bug-filing`, `main @ 57822b4e`)

Both scenarios merged in `88170a30` (2026-08-11) and derive their whole geometry from a **tier 3**
rifleman:

- `test-visual-gauge-truth.lua:10-16` reasons *"map-placed and never ordered anywhere, so not moving
  (no −1) and never gets `dugin` … He sits on tier 3"* and predicts a **22c** ring.
- `test-visual-concealment-gauge.lua:21-27,57-59` asserts *"stopped ⇒ tier 3; dug in ⇒ tier 4"*.

`ProneCondition` includes `!moving` (`infantry.yaml:294`), so **a stationary soldier is prone from
spawn** — `+1`. Correct tiers are **stopped 4** (19c) and **dug in 5** (16c).

Note `test-visual-gauge-truth` reasons *correctly* about the `dugin` timer bug filed above and then
misses prone, which is exactly why it reads convincingly.

`test-visual-concealment-gauge.lua:44-45` separately derives 200 ticks as 8.0 s from 25 ticks/s; the
mod runs `Timestep: 60` ms, so it is 12.0 s. Its 13-second wait still clears the timer, so that
error is harmless in effect.

**Established:** READ at HEAD (the `ProneCondition` clause and both Lua premise blocks). The
predicted failure is **DERIVED** — **neither scenario has been run, so this predicts a failure
rather than reporting one.** If they pass, this entry is wrong and something else is compensating.
**Confirm by:** one run of either. Worth spending **before** anyone trusts their output, since both
are capture scenarios whose screenshots are the evidence for gauge correctness.

## 2026-08-20: [low] OPEN, NOT FIXED — `stance-ambush` and `stance-holdfire` are granted in five places and consumed by nothing (found while: extracting defects from the ambush/concealment research programme, branch `wt/bug-filing`, `main @ 57822b4e`)

Granted at `defaults.yaml:309-310,570-571,670-671,682-683` and `aircraft-russia.yaml:115`; grepping
`mods/` for `RequiresCondition:.*stance-ambush` or `!stance-ambush` returns **no matches**. They are
markers only. **No stance modifies detectability** — every `DetectableAddativeModifier` keys on
`object-proximity`, `prone`, `dugin`, `firinganyweapon`, `moving`, `rank-veteran` or `!airborne`.

This part of the user's original complaint ("in ambush stance staying hidden should take care of
itself") is **literally true of the code**, and it is filed here rather than as a design item
because the dangling tokens read to any future maintainer as a wired feature.

**Important non-defect:** the *Ambush stance itself* is consumed heavily — `UnitStance.Ambush`
(`AutoTarget.cs:22`) drives the whole `AutoTarget` state machine, `GarrisonManager`,
`CohesionMoveModifier.cs:1079` and `AttackMoveActivity.cs:156`. And
`StancePositioningExecutor.cs:318` **honours** Ambush by refusing to reposition, in C# rather than
YAML. A prior pass concluded the executor deliberately ignores Ambush; it does not. Do not delete
the stance on the strength of the dead tokens.

**Established:** READ at HEAD — grant sites and the absence of consumers established by grep, which
is a completeness argument over `mods/**`.
**Confirm by:** nothing to run.

## 2026-08-20: [low] OPEN, NOT FIXED — vehicles carry a bare `Detectable:` with no modifiers, so nothing a vehicle does changes its visibility — while the Ambush stance and its concealment-flavoured tooltip remain on offer (found while: extracting defects from the ambush/concealment research programme, branch `wt/bug-filing`, `main @ 57822b4e`)

`vehicles.yaml:66` declares `Detectable:` with no `Vision:` and no `DetectableAddativeModifier`
anywhere on the template. Stopping, being prone-equivalent, digging in and firing all change a
vehicle's detectability by **zero** — it sits at level 2 (seen from 25c) permanently. Meanwhile the
Ambush button remains available on vehicles, draws the amber `A`, and its tooltip
(`ingame-player.yaml:373`) describes pre-aim and spotting behaviour that reads as concealment.

Whether vehicles *should* have posture modifiers is a design question. The defect filed here is the
mismatch between what the stance UI implies and what a vehicle's detection model can do.

**Established:** READ at HEAD for the bare trait. **Not exhaustively audited:** individual vehicle
actors were **not** each checked for a per-actor `Detectable` override, so a specific vehicle may
differ from the template. Treat the "all vehicles" scope as unverified.
**Confirm by:** `grep -n 'Detectable' mods/ww3mod/rules/ingame/vehicles-*.yaml` closes the scope gap
without a run.
## 2026-08-20: [low — LATENT, unreachable today] OPEN, NOT FIXED — `GarrisonPortOccupant.PortIndex` goes stale on any port→port move, inverting which firing arc can target the soldier (found while: curation pass over `DISCOVERIES.md`, branch `wt/curation-260820`, `main @ 57822b4e`)

Filed here rather than promoted to the bank: it is an unfixed defect, and `DOCS/reference/README.md`
§"Reference ≠ tracker" puts that in `WORKSPACE/`. Re-derived from code in this pass, not taken from the
2026-08-19 entry that reported it.

`PortIndex` has exactly two writers — `GarrisonPortOccupant.SetPort` (`:43-47`) and `ClearPort`
(`:49-53`) — called from `GarrisonManager.DeployToPort` (`:355-356`) and `RevokePortCondition`
(`:437-438`) respectively. **Neither runs when a soldier moves between two ports of the same building**,
because that path goes through the static `SwapPortOccupants` (`GarrisonManager.cs:1175-1191`), which
swaps `DeployedSoldier` / `CachedArmaments` / `ConditionToken` and touches no actor trait.

The field is not bookkeeping. `GarrisonPortOccupant.TargetableBy` reads `gm.PortStates[PortIndex].Port`
(`:70`) and returns true only for attackers inside that port's `Yaw ± Cone` (`:83-90`). So a soldier
standing at the south port stays targetable **only through the north port's arc** — shootable from a
direction he is not visible from, and immune from the direction he is.

**The slot's identity is really a quadruple, not the triple the PITFALL at `GarrisonManager.cs:1169-1174`
and `GarrisonPortSwapTest` describe:** `DeployedSoldier`, `CachedArmaments`, `ConditionToken`, **and the
occupant's own `PortIndex`**.

**Why it is deliberately not a drive-by fix.** `SwapPortOccupants` is `static` over two `PortState`s with
no access to the building actor or the port indices, and that is exactly what lets it be unit-tested from
`OpenRA.Test` (`Actor` has no constructor reachable from that assembly). Fixing it means either passing
the indices + building in — losing the test seam — or calling `SetPort` at the two order-handler call
sites, which leaves the helper still able to produce the bad state. Wants a deliberate decision.

**Unreachable in any shipped build today.** All four garrison orders (`AssignGarrisonPort`,
`SwapGarrisonPorts`, `SetGarrisonPortTarget`, `ClearGarrisonPortTarget`) still grep to exactly one hit
each — the `case` label at `GarrisonManager.cs:1432`, `:1490`, `:1504`, `:1520`. Nothing issues them.
**This is a prerequisite for wiring the port UI, not a live defect** — and it is the second such
prerequisite found in that unreachable code in two days, which is itself the argument for auditing the
whole port-order family before exposing any of it.

## 2026-08-19: [medium] OPEN, NOT FIXED — reopening the unload menu discards EVERY queued unload, not just the one mid-wait (found while: auditing why the cargo passenger rows were deleted, branch `wt/cargo-parity`, `main @ de78a1ed`)

Confirmed by reading the whole chain; **not yet observed in a running game** — the branch report
carries a MANAGER block for it. Filed as unverified at `cargo-garrison-status-260819.md:147-149`; the
chain now resolves, and it is **worse than that entry and worse than the code's own comment claim**.

`CargoUnloadMenuLogic.Open` resets `hasDropped = false` (`:103`) on every open, including a reopen of
the same transport. `Drop` sends `queued: hasDropped` (`:236`). So the first click after any reopen
issues an UNQUEUED order, and `Actor.QueueActivity(false, …)` calls `CancelActivity()` first
(`Actor.cs:381-387`).

The comment at `CargoUnloadMenuLogic.cs:48-51` describes the cost as one man lost inside his
`BeforeUnloadDelay` wait. The cost is the whole queue:

- `CancelActivity()` → `CurrentActivity.Cancel(self)` with `keepQueue: false` (`Actor.cs:400-403`).
- `Activity.Cancel` sets `NextActivity = null` **before** the `IsInterruptible` check
  (`Activity.cs:233-236`), and that setter writes the `nextActivity` field (`:71-76`).
- Queued unloads live on exactly that chain: `Activity.Queue` walks `nextActivity` and appends
  (`:222-228`).
- `UnloadCargo` never sets `IsInterruptible = false`, so it also flips to `Canceling`, and its `Tick`
  returns `true` on `IsCanceling` before dropping anyone (`UnloadCargo.cs:149-151`).

So: drop five men, ESC, press J, click once — the four still queued are severed along with the one in
flight, and only the newly clicked man dismounts. Nothing on screen reports it; the queued-unload
waypoint markers vanish with the activities that emitted them.

**Not fixed here deliberately.** The obvious patch (never reset `hasDropped`) is wrong: once the
transport has been given a move order, a fresh drop *should* cancel the move. The sound predicate is
"queue if this transport already has a pending `UnloadCargo`", which is a behavioural change needing a
run, and this branch's remit was legibility.

## 2026-08-19: [low] OPEN, NOT FIXED — past 18 classes the unload menu lists `Driver`, `Gunner` and `Commander` TWICE, with nothing distinguishing the pair (found while: photographing the menu at 24 classes, branch `wt/run-verify`, `main @ 815804f1`)

Observed, not inferred: the 24-row capture in `260819_173016_p8981_test-unload-menu-classes` ends
`… Civilian, Scientist, Driver, Gunner, Commander, Driver, Gunner, Commander`. The three repeats are
the America and Russia crew.

**It is the exact failure `GroupByClass` documents itself as avoiding.** Its docstring rejects
grouping on actor type because the veteran variants "inherit their base Tooltip verbatim, so the
player would just see two rows both reading 'Rifleman' with no way to tell them apart". `^CrewMember`
sets no `Selectable.Class`, so `GroupKey` falls back to `p.Info.Name` — six distinct keys — while the
row LABEL comes from `DisplayName`, which reads `Tooltip.Name`. `crew.driver.america` and
`crew.driver.russia` are different keys with the same tooltip, so the rejected shape returns through
the fallback path rather than through the grouping rule.

Harmless in practice today: it needs both factions' ejected crews in one hold, and either row drops
the men you would expect. Filed rather than fixed because the fix is a product decision, not a bug
fix — either give `^CrewMember` a `Selectable.Class` so the six collapse to one `Crew` row, or put a
faction word in the crew tooltips. Left alone because this branch's remit was to verify the height
fix, which does not depend on which is chosen.

## 2026-08-19: [low] OPEN, NOT FIXED — `ReplayMetadata.Read` cannot fail safely under NUnit: its catch block crashes the test host (found while: RED-testing the replay-file round trip, branch `wt/run-verify`, `main @ 815804f1`)

`ReplayMetadata.Read` swallows every exception into `Log.Write("debug", ex.ToString())`
(`ReplayMetadata.cs:102-105`). Under NUnit no `debug` channel is registered, so the logging thread
throws `ArgumentException: Tried logging to non-existent channel debug` (`Log.cs:140`) on a
BACKGROUND thread and takes the process with it.

Measured: with `Write` sabotaged to emit `dataLength - 1`, the run died at `Test host process
crashed` after 585 tests instead of failing an assertion; restored, 1602 pass.

**The consequence is a coverage hole, not a product bug.** Well-formed files never enter that catch,
so the committed tests are unaffected — but no test can assert what a *malformed or truncated*
replay does, which is precisely the input the compatibility dialog exists to be graceful about.
Anyone wanting that coverage has to register a log channel in the fixture first; the failure path is
unobservable from a test today.

## 2026-08-19: [low] OPEN, NOT FIXED — a stray Italian phrase sits in the root `.editorconfig` analyzer block (found while: auditing the `make check` analyzer gate, branch `wt/build-gate`, `main @ 08b255f7`)

`.editorconfig:167` reads `pagare qui sotto` — an accidental paste, landed in `c6b0232f` (2025-04-22,
commit message `1`), sitting between the `RCS1080` and `RCS1170` severity settings.

**Almost certainly harmless, which is the only reason it is filed rather than fixed.** The
EditorConfig spec says parsers ignore lines that are not a section header, comment or `key = value`
pair, and the analyzer severities around it demonstrably still apply (`make check` enforces the
`RCS*` rules on both sides of it). Left alone here because this branch's remit was the gate itself
and deleting a line from a shared config is not free of the risk that *some* reader is stricter than
the spec. A one-line deletion is the whole fix if someone wants it; there is nothing to preserve.

## 2026-08-17: [medium] CLOSED 2026-08-19 by deleting the CI job — `Linux (mono)` CI had not compiled since 2026-08-11: `Convert.ToHexString` is net5.0+ and the mono lane targets netstandard2.1 (found while: CI reporting-integrity audit, branch `wt/ci-integrity`, `main @ 8656bd3c`)

> **Resolved 2026-08-19 (branch `wt/gate-coverage`, `main @ bc168d8b`) by deleting the `linux-mono` job
> from `.github/workflows/ci.yml`, NOT by making the code mono-compatible.** Read the closure note at the
> bottom of this entry before acting on anything above it: **the source-level incompatibility described
> below is still present and is now unmonitored.** The diagnosis is preserved because it is correct and is
> the cost estimate for anyone who wants mono back.

**Three call sites, one API.** `engine/OpenRA.Game/Network/BuildFingerprint.cs:246,308` and
`engine/OpenRA.Game/Graphics/SequenceIntegrity.cs:91` call `Convert.ToHexString`, added in `bedf18e0` and
`d836bd07` (both 2026-08-11, refined by `d068f4ae`). CI run `31997060463` job `Linux (mono)` fails at **36s**
with three `CS0117: 'Convert' does not contain a definition for 'ToHexString'`.

**Why, precisely:** `engine/Directory.Build.props:23` switches the whole engine to `netstandard2.1` when
`MSBuildRuntimeType == Mono`. `Convert.ToHexString` is net5.0+, so it is not on that surface. This is a
target-framework mismatch, not a stale-mono-install problem, and it dies in the `engine` prerequisite before
the analyzer build is ever reached.

**Not a one-liner, which is why this is filed rather than fixed.** The obvious rewrite
(`BitConverter.ToString(b).Replace("-", "")`) is blocked by our own config: `engine/.editorconfig:943` sets
`dotnet_diagnostic.CA1872.severity = warning` — *"prefer `Convert.ToHexString` over call chains based on
`BitConverter.ToString`"* — and `check` builds `-warnaserror`. So the naive fix trades 3 mono errors for 3
new analyzer errors **on every platform**. A correct fix is a shared helper plus one scoped suppression.
**And it would still not make the lane green**, because mono then proceeds to the same `-warnaserror` Debug
build and the same analyzer errors as the other lanes. Sequence the analyzer burn-down first.

**Before anyone proposes deleting the job:** `packaging/functions.sh:26-31` still has a live `RUNTIME = mono`
branch, and `mod.config` still references `PACKAGING_OSX_MONO_SOURCE` / `PACKAGING_APPIMAGE_DEPENDENCIES_SOURCE`.
Today's `packaging.yml` uses `make engine` (net6) so releases do not currently hit that path — but it exists.

**Closure note, 2026-08-19.** The caution directly above is sound and was *honoured*, not overruled: only the
**CI job** was deleted (`.github/workflows/ci.yml`, the `linux-mono` job). **`RUNTIME=mono` build support is
untouched** — `packaging/functions.sh:26-31`, the `mod.config` variables, the `RUNTIME=mono` branches in both
Makefiles and the `netstandard2.1` switch at `engine/Directory.Build.props:23` all remain exactly as they were.
Deleting the *job* and deleting *mono support* are separable, and only the first was done.

Why the job went, on four checks:

1. **It was contributing zero coverage.** It died in the `engine` prerequisite at 36s, before any analyzer
   build. A lane that does not compile gates nothing.
2. **Nothing downstream consumed it.** `.github/workflows/ci.yml` has no `needs:`, no `upload-artifact` and no
   `download-artifact` anywhere in the file — the job produced no output any other job read.
3. **It is not a shipping configuration.** `.github/workflows/packaging.yml:38,88,132` invokes only
   `packaging/{linux,macos,windows}/buildpackage.sh`, and all four `install_assemblies` call sites in those
   pass the literal `"net6"`. The single `"mono"` call site is `engine/packaging/macos/buildpackage.sh:80`,
   reachable only from `engine/.github/workflows/packaging.yml` — a vendored upstream workflow that GitHub
   never runs, because Actions reads workflows only from the repository root.
4. **The lane cost coverage rather than adding it.** Under Mono, `engine/Directory.Build.props:62-63` drops
   both Roslynator packages, so the mono rule set was a strict *subset* of the net6 one — it could not catch
   anything the net6 lane missed. Meanwhile three `CA` rules are globally `severity = none` in the shared
   `engine/.editorconfig` *solely because mono cannot satisfy them*: `CA1845` (`:872`, "Not available on
   mono"), `CA1850` (`:884`, "once supported by mono") and `CA2263` (`:1047`, "once mono is dropped").
   Those are off on **every** lane. See the follow-up item below.

**What is still broken, and is now unwatched.** `make RUNTIME=mono all` still fails locally with the same
three `CS0117`. Nothing above this note was fixed; the lane that used to report it is simply gone. If mono
support is ever wanted back, the diagnosis above is the cost estimate and is still accurate.

**Follow-up this unblocks, and it is nearly free — MEASURED, not estimated.** `CA1845`, `CA1850` and `CA2263`
can now be raised from `none` to `warning` in `engine/.editorconfig`. All three were flipped to `warning` and
all 10 projects rebuilt (`-t:Rebuild -c Debug`, no `-warnaserror`, so nothing is hidden by an early exit).
**Total backlog across the whole engine: one violation.**

| Rule | violations | where |
|---|---|---|
| `CA1845` | **1** | `engine/OpenRA.Mods.Cnc/UtilityCommands/ImportTiberianDawnLegacyMapCommand.cs:168` |
| `CA1850` | 0 | — |
| `CA2263` | 0 | — |

Not done here only because it is a separate concern from the gate's *shape* and wants its own commit. Note
`CA1850`'s comment also gates on ".NET 7 or later", which net6 does not satisfy — so that one may be a no-op
until the TFM moves, and its 0 may mean "cannot fire" rather than "nothing to fix".

> **DONE 2026-08-19 in `e5f03d73` (merged).** Two of the three were flipped and the one violation fixed.
> The caution in the paragraph directly above was borne out on both counts, so read the outcome, not the
> prediction:
>
> - `CA1845` → `warning`, and `ImportTiberianDawnLegacyMapCommand.cs:168` fixed. That was the whole bill.
> - `CA2263` → `warning`, but **INERT on the pinned SDK**. A canonical violation planted in a
>   provably-compiled file produced nothing on `6.0.428`, in the same build where `CA1839`, `CA1845` and
>   `RCS1041` all fired. Do not count it as coverage until `global.json` moves off `6.0.x`.
> - `CA1850` **stays at `none`** — correctly. Its original TODO read "once using .NET 7 or later AND once
>   supported by mono", i.e. *two* preconditions, and dropping mono satisfies one. Item 4 above lists it
>   among rules disabled "solely because mono cannot satisfy them"; that is item 4's one inaccuracy.
>   `CA1850` was never mono-only.
>
> Also landed there: `OpenRA.WindowsLauncher` gained its missing `Debug|Any CPU.Build.0` line, taking the
> analyzer gate to 10 of 10 projects.
>
> **Full `RUNTIME=mono` support removal is still NOT done, and the recommendation is to keep it that way.**
> Scoped and costed in [`WORKSPACE/plans/260819_mono_removal_scope.md`](../plans/260819_mono_removal_scope.md).
> Short form: the coverage payoff was those three CA rules and it is now banked, so removal buys tidiness
> only, against real vendored-engine delta. Read that plan's consumer table before touching `mod.config` —
> `PACKAGING_APPIMAGE_DEPENDENCIES_TEMP_ARCHIVE_NAME` has "mono" in its *value* and a live consumer in the
> Linux AppImage build, while the neighbouring `MONO`-named variables have none. The names do not predict
> which is which.

> **SUPERSEDED 2026-09-02 on branch `wt/mono-removal`: the removal WAS done, at the user's direction and
> over the plan's recommendation.** Every statement above about mono still being present is now history —
> do not act on it. `RUNTIME` is gone from both Makefiles, the `RUNTIME` positional parameter is gone from
> `install_assemblies` (7 args → 6) and `install_mod_assemblies` (5 → 3) with all 13 call sites updated,
> the mono launcher branch is gone from all six `.sh` launchers, the macOS bundle's mono slice and
> `apphost-mono.c` / `checkmono.c` are deleted, and `Directory.Build.props` no longer has a
> `netstandard2.1` branch. The plan's consumer table was re-derived before use and was exactly right,
> including the `PACKAGING_APPIMAGE_DEPENDENCIES_TEMP_ARCHIVE_NAME` trap.
>
> **`make RUNTIME=mono all` no longer fails with three `CS0117`** — it fails at `Makefile:73` with an
> explicit "no longer supported" `$(error)`. That guard is deliberate: without it the flag would be
> silently ignored and yield a net6 build the caller believed was mono. It matches only the literal
> `mono`, so an unrelated exported `RUNTIME` in the environment cannot break the build.
>
> The cost of restoring mono is now the estimate recorded above *plus* reverting this removal.

## 2026-08-16: [high] UNTRIAGED — LIVE MONEY PUMP: buy an LCCV for 1200, deploy it, sell the Logistics Centre for 3500. +2300 per cycle, unlimited (found while: economy audit, `main @ d919c81a`)

**The loop, entirely in shipped UI:** `LCCV` is buildable (`vehicles.yaml:612-617`,
`Queue: Vehicle`, `Prerequisites: ~techlevel.low`, `Cost: 1200`) and the Supply Route produces the
Vehicle queue (`structures.yaml:318-319`). Deploy it — `Transforms: IntoActor: logisticscenter`
(`vehicles.yaml:630-631`). `LOGISTICSCENTER` is `Valued: Cost: 3500` (`structures.yaml:372-373`),
inherits `^Building` and so carries `Sellable` at the default `RefundPercent: 100`
(`structures.yaml:115`, `Sellable.cs:24`) with no `-Sellable` anywhere in the mod. The new LC
spawns at full supply (`SupplyProvider.cs:259` defaults `currentSupply` to `TotalSupply`), so
`MissingSupplyValue` is 0 and `GetSellValue()` returns the full 3500. Click the sell button
(`ingame-player.yaml:1223` → `SellOrderGenerator`), click the LC. **Net +2300, plus a free `tecn`
technician from `SpawnActorsOnSell` (`structures.yaml:107-108`).** Repeatable with no cooldown,
no tech gate, no unit limit.

**Why the `~disabled` guard doesn't catch it.** `LOGISTICSCENTER` does carry
`Buildable.Prerequisites: ~disabled` (`structures.yaml:367`), which is what
`DOCS/reference/economy.md:13` reasons from when it says LCs are "**not** buildable ... the only
ones in a match are the Neutral pre-placed ones you can capture". That reasoning is sound for the
build queue and wrong for the actor: `Transforms.CanDeploy()` (`Transforms.cs:93-99`) checks only
`IsTraitPaused`/`IsTraitDisabled` and `World.CanPlaceBuilding` — **it never consults
`Buildable.Prerequisites`.** `~disabled` gates the sidebar icon, not existence.

**Tech level cannot save it either:** `MapOptions.TechLevel` defaults to `"unrestricted"`
(`MapOptions.cs:52`) and WW3MOD hides the dropdown (`world.yaml:435`
`TechLevelDropdownVisible: false`), so `ProvidesTechPrerequisite@unrestricted`
(`player.yaml:222-224`) always grants `techlevel.low`. LCCV is available in every match.

**NOT VERIFIED AT RUNTIME** — traced statically; no game launched (read-only brief). One playtest
settles it: build LCCV, deploy, sell, watch the cash counter.

**Candidate fixes** (design call, not made here): drop `LOGISTICSCENTER`'s `Cost` to ≈1200 so the
round-trip is value-neutral; or set `Sellable.RefundPercent` on it to ~34%; or remove `Sellable`
from the LC; or gate `Transforms` behind the same `disabled` condition. Note the third also
removes the doc's `Sell building with supply (LC)` cash-flow row (`economy.md:146`).

## 2026-08-16: [medium] UNTRIAGED — two automatic evacuation paths bypass the handicap refund adjustment that the Evacuate button applies (found while: economy audit)

Same shape as the UI-path bug fixed earlier today, still live in two places. `DeliversCash`
computes an evac refund as `info.Payload == -1 ? GetSellValue() : info.Payload` and then, for
`Type: Rotation`, scales it by `100/(100 - handicap)` (`DeliversCash.cs:96-106`). That scaling is
correct and load-bearing: `HandicapProductionMultiplier` is attached at `defaults.yaml:914` and
inflates purchase cost by exactly the same factor, so refund and cost stay symmetric.

**Two callers queue `RotateToEdge` with a raw `GetSellValue()` and skip it:**
- `AmmoPool.cs:264-265` — the `ResupplyBehavior.Evacuate` branch, i.e. a unit that runs dry and
  self-evacuates. Reaches `m270`, `grad`, `tos` (`vehicles-america.yaml:698-699`,
  `vehicles-russia.yaml:524-525`, `:648-649`), whose whole design is evacuate-when-dry.
- `DropsSupplyCache.cs:524-525` — the empty-truck evacuation, i.e. every TRUK
  (`vehicles.yaml:529-530`).

**Effect:** a handicapped player who presses the Evacuate button is paid correctly, but the same
unit evacuating *automatically* pays the un-inflated amount — under-refunded by the handicap
factor (at 50% handicap, half). Both paths also skip the `info.Payload` override, which is
currently unset everywhere so that half is latent.

Zero effect at handicap 0, which is the default and almost certainly every match played so far.

## 2026-08-16: [low] UNTRIAGED — `m109` and `giatsint` refund 4 free shells at evacuation (`Ammo: 39` is not a multiple of `ReloadCount: 5`) (found while: economy audit)

`CustomSellValue.cs:43` deducts `floor(missingRounds / ReloadCount)` batches, so a remainder is
never charged. Both artillery pieces carry `Ammo: 39, ReloadCount: 5, SupplyValue: 60`
(`vehicles-america.yaml:629`, `vehicles-russia.yaml:459`): fully empty deducts 7 batches, not 7.8
— **48 credits of free shells per unit**. The tooltip disagrees with the deduction, because
`AmmoPoolInfo.BatchCount` rounds *up* (`AmmoPool.cs:68`) and advertises a 480 budget the sell math
caps at 432.

`A10.Airstrike` has the same shape (`Ammo: 40` vs inherited `ReloadCount: 25`, 15 free rounds =
5 credits). All other 60 live pools divide exactly. Cheapest fix is `Ammo: 40` on the two
artillery pieces — a 1-shell buff, not a balance event.

## 2026-08-16: [medium] UNTRIAGED — faction tooltips render a literal `\n`; the descriptions written today use an escape the faction picker never unescapes (found while: netcode audit, `main @ 8b4ae9cd`)

`75ac6941` (2026-08-16) wrote faction descriptions into `mods/ww3mod/rules/world.yaml:241,245,254`
in the form `Description: America\nNATO's lead power. ...`.

That style is correct for `Buildable.Description`, because `ProductionTooltipLogic.cs:191` calls
`.Replace("\\n", "\n")`. **The faction dropdown does not.** The engine has no central unescaping;
six consumers each do it themselves, and `LobbyUtils.cs:235-238` is not among them — it passes the
raw string to `SplitOnFirstToken`, which looks for a real newline (`:206-215`), finds none, and
returns `first` = the entire blob, `second` = `null`.

**Expected on screen:** the faction tooltip is one long line containing a visible `\n`, with an
empty description body. Affects the faction picker every single-player and multiplayer match
passes through.

**Fix:** one `.Replace("\\n", "\n")` at `LobbyUtils.cs:235`, matching the six existing sites.
**NOT VERIFIED AT RUNTIME** — traced statically only; one lobby screenshot settles it.

## 2026-08-16: [medium] UNTRIAGED — the dedicated-server launch scripts disable sync reports, which will silently disarm desync diagnosis the moment an official server ships (found while: netcode audit)

`Settings.cs:97` deliberately defaults `EnableSyncReports = true` (a WW3MOD divergence from
upstream, with a `// PITFALL: do not "restore" this` comment), so a stranger hosting in-game
records reports on both machines.

But `launch-dedicated.sh:63`, `launch-dedicated.cmd:16` and the `engine/` copies all hard-default
`EnableSyncReports=False`. Because the value is read from the **host's** lobby globals by every
client (`Server.cs:349` → `OrderManager.cs:118`), standing up an official dedicated server — the
top recommendation for fixing multiplayer discovery — would **turn sync reporting off for every
game played on it**, reproducing exactly the host trap PIPELINE item 42 names.

**Fix:** pass `Server.EnableSyncReports=True` when the server is stood up, or change the script
defaults. Cross-reference: item 42(iv), item 53's dedicated-server bullet.

## 2026-08-16: [low] UNTRIAGED — dead Fluent key block is larger than item 53 records (found while: netcode audit)

Item 53 reports "~38 dead Fluent keys" at `mods/ww3mod/languages/en.ftl:84–129`. Verified still
present, and the same defect extends at least to `:619-620` (`search-status-failed`,
`search-status-no-games`), where the engine looks up `label-search-status-*`
(`ServerListLogic.cs:30,33`). The block uses pre-`notification-` / pre-`label-` names the engine
no longer resolves. Inert — engine strings win — but the count and range in item 53 understate it.
Item 53's reason for deferring (a blind rename changes ~40 lobby strings at once) still holds.

## 2026-08-15: [critical] FIXED (wt/heli-gun) — the littlebird's weapons all deal exactly 0 damage: no Gunner crew slot, and `^Airborne` pins `FirepowerMultiplier@NoGunner` to 0 forever (found while: diagnosing "littlebird strafing kills nothing", branch `wt/heli-gun`)

Measured on `main @ 4c4d8a49`, scenario `test-littlebird-strafe`, trace `WW3_GUNTRACE=1`. The gun fires,
the rounds land dead on target, both warheads find the victim and report a full-strength hit, and
`InflictDamage` computes `FINAL=0` because the shooter contributes a `0` firepower modifier:

```
firepowerModifiers=[100, 100, 100, 100, 100, 0, 100]      <- index 5 is @NoGunner
InflictDamage victim=e1 rawDamage=250 ... versus=100 FINAL=0 hpBefore=200
```

`FirepowerMultiplier@NoGunner` (`rules/ingame/aircraft.yaml:278-280`, `Modifier: 0`,
`RequiresCondition: !has-gunner`) is meant to punish a helicopter whose gunner has bailed out at <50% HP.
`VehicleCrew` only grants a slot condition for a slot the actor declares (`VehicleCrew.cs:140-153`), and
the littlebird declares `CrewSlots: Pilot` only (`rules/ingame/aircraft-america.yaml:103-108`). So the
condition is never granted, `!has-gunner` is true from `Created`, and the modifier never lifts.

**Affects both armaments** — the minigun AND the Hellfire rack, since the modifier is on the shooter, not
the weapon. Any measurement of littlebird missile damage taken before this is void.

**Only actor affected**: a sweep of armed actors with a `VehicleCrew` block found no other `CrewSlots`
missing `Gunner`.

**Fixed** on `wt/heli-gun` by the general route: `VehicleCrew.SlotPresentConditions` grants
`has-gunner-seat` for a DECLARED slot only, and the gate became `has-gunner-seat && !has-gunner`. The
alternative — giving the littlebird a Gunner slot — was rejected because it invents a crew member who
could then bail at `DamageState.Heavy` and re-zero the guns, smuggling a new gameplay behaviour in as a
bug fix. Whether a Little Bird should have a two-man crew stays a separate, deliberate content decision.

**Superseded 260816 — the gate is gone entirely.** `has-gunner-seat` was consumed by all `^Helicopter`
actors but granted only by the three declaring a Gunner slot, so `littlebird`/`tran`/`halo` failed lint.
User ruling: the mechanism is dead weight, because crew never re-board and a helicopter whose crew has
ejected is burning and about to be destroyed. `FirepowerMultiplier@NoGunner`,
`VehicleCrew.SlotPresentConditions` and the three grants are deleted. The littlebird's damage is
unaffected — it stays unzeroed, now because no zeroing gate exists rather than because it passes one.

## 2026-08-15: [medium] OPEN — Restart drops out of any harness scenario instead of restarting it, and the run ends (found while: user mid-session in demo-heli-lanes, branch `wt/heli-gun`)

Reported from live use: "I clicked restart from the menu and it closed? It seemed better but I wasnt
done testing." The scenario did not restart; the process ended and the testing session was lost.

Both restart paths go through the same call — the in-game menu
(`IngameMenuLogic.cs:382`, `Game.RunAfterDelay(exitDelay, Game.RestartGame)`) and the harness's own
button (`TestModeLogic.cs:43`, `restart.OnClick = Game.RestartGame`). So the "press End to restart" line
in the demo headers is describing a path with the same defect, not a safe alternative.

`Game.RestartGame` (`Game.cs:237-255`) re-resolves the map before restarting:

```csharp
lobbyInfo.GlobalSettings.Map = ModData.MapCache.GetUpdatedMap(lobbyInfo.GlobalSettings.Map);
if (lobbyInfo.GlobalSettings.Map == null)
{
    Disconnect();
    Ui.ResetAll();
    LoadShellMap();
    return;
}
```

**NOT VERIFIED, and this is the part to check first:** the likely cause is that a harness scenario is a
staged map (`Visibility: MissionSelector`, loaded from `tools/autotest/scenarios/<name>` rather than the
mod's map list), so `GetUpdatedMap` cannot find it by UID, returns null, and the branch above disconnects
to the shell map — after which the run has no game and ends. I read the code but did not instrument the
lookup, so the null could equally be coming from a UID change caused by the harness rewriting the
scenario between runs.

Impact is worst for demos specifically, because a demo is a long human viewing session: losing it costs
the user everything they were part-way through observing, and the header actively invites the click.

Workaround until fixed: do not use Restart in a harness scenario; relaunch the demo instead.

## 2026-08-15: [medium] OPEN — every demo is killed after exactly 300s by a watchdog that waits for a verdict demos are designed never to write (found while: showing the user demo-heli-weapons, branch `wt/heli-gun`)

`run-demo.sh` delegates to `run-test.sh --visible --audio "$@"` (`run-demo.sh:50`) and inherits its
`TIMEOUT_SECS=300` default (`run-test.sh:150`). That watchdog kills the game and synthesizes a FAIL when
no verdict has been written in time (`run-test.sh:729-764`). But `run-demo.sh`'s own header states demos
"do NOT write a result file — the user closes the window when done", so the verdict the watchdog waits
for can never arrive. **Every demo therefore dies at the five-minute mark, mid-viewing**, and prints
`TIMEOUT-FAIL` for a scenario that has no pass/fail concept:

```
==> TIMEOUT: no verdict after 300s — killing the game.
==> VERDICT: TIMEOUT-FAIL   (exit 1)     # for demo-heli-weapons
```

`run-demo.sh` maps run-test.sh's exit 3 ("no result") to 0 precisely because verdict-less is the point —
but it does not neutralise the timeout that fires first, so the exit code it translates is 1, not 3.

Workaround: pass a large `--timeout` (`./tools/autotest/run-demo.sh --timeout 7200 demo-heli-weapons`);
the flag forwards through. Real fix is for `run-demo.sh` to default the watchdog off, or to a value that
reflects a human viewing session, rather than reusing the unattended-test default.

Note this also makes the timeout FAIL misleading in the other direction: the message tells the reader to
go looking in `debug.log` for a hang or a rules-load failure, when nothing is wrong at all.

**SECOND, INDEPENDENT DEFECT IN THE SAME FILE — `set -e` makes the success mapping unreachable.**
`run-demo.sh` ends with:

```sh
set -e                                              # line 17
...
./tools/autotest/run-test.sh --visible --audio "$@" # line 50
rc=$?
if [ ${rc} -eq 3 ]; then exit 0; fi                 # "verdict-less is the demo's whole point"
exit ${rc}
```

Under `set -e` the script dies on line 50 the moment run-test.sh returns non-zero, so `rc=$?` and the
mapping below it **never execute**. Verified with a minimal repro: the same shape prints nothing and exits
3 with `set -e`, and prints the mapping line and exits 0 without it. So closing a demo window by hand —
the documented, intended way to end a demo — always reports `NO-RESULT (exit 3)` and surfaces as a failed
command. Both halves of this file's error handling are therefore dead: the timeout fires before the
mapping could help, and the mapping could not run anyway.

Fix is to capture the status without tripping the errexit, e.g. `if ./tools/autotest/run-test.sh ... ;
then rc=0; else rc=$?; fi`, plus a demo-appropriate timeout default.

## 2026-08-15: [medium] OPEN, UNMEASURED — a ground vehicle whose crew ejected may stay permanently crippled after being repaired to full HP (found while: fixing the littlebird's zero damage, branch `wt/heli-gun`)

**Inferred from code, NOT tested — do not treat the behaviour as confirmed.** `VehicleCrew` revokes a
slot's occupied condition when that crew member ejects, and nothing observed re-grants it: the crew are
spawned as separate infantry actors and there is no re-boarding path (`VehicleCrew.cs:56` records
"EjectionSurvivalRate removed — vehicle death is now total loss"). The `^CrewedVehicle2` / `^CrewedVehicle3`
degradation ladder (`vehicles.yaml:266-310`) keys off those conditions: no driver -> `SpeedMultiplier` 0,
no gunner -> `TurretTurnSpeedMultiplier` 0.

So repairing such a hull back to full HP should yield a vehicle at 100% health that still cannot move or
traverse its turret, permanently. That may well be intended — the crew are gone and the design says the
wreck is a total loss — but if so, being repairable at all is the odd part, which is what prompted the
question. Worth an autotest before anyone acts on it: damage a Bradley past `EjectionDamageState`, let the
crew bail, repair it, and check whether it moves.

## 2026-08-15: [high] BOUND ADDED 2026-08-19, FLAG STILL OFF — `RendezvousMath.AnchorAcceptable` had NO LOWER BOUND, so the combined-arms rendezvous dragged the drop-off BACKWARDS to the Supply Route and the carrier shuttled in place (found while: offensive transport standoff, branch `wt/offense-standoff`)

**Measured, not reasoned.** Run `260815_202509`, seed 1017, `wip-transport-delivers`, with
`RendezvousWithOffensiveStaging: true` on `MountedTransportBotModule@experimental`:

```
[exp-transport] rendezvous player=USA-bot anchor=7,17 lerp=32,10 → drop=7,17 tick=65
[exp-transport] task-created boarding=5 of 5 drop=7,17 tick=65      (own SR is at 6,16)
[exp-transport] delivered   at=6,16 drop=7,17 pax=5 tick=515
... and again at 565/965, 1015/1365, 1415
```

The transport abandoned a **26-cell** forward delivery (`lerp=32,10`) for a **one-cell** one
(`anchor=7,17`, adjacent to its own SR at 6,16), then looped: load five, drive one cell, unload, reload —
four task creations and three departures inside 1400 ticks.

**Cause.** `AnchorAcceptable` (`RendezvousMath.cs:78`) tests only

```csharp
return anchorReach <= fallbackReach + margin;
```

so it rejects an anchor too far **forward** and accepts without limit one that is **behind** the cell the
transport would have picked for itself. Not an edge case: before contact the frontier descent has nothing
to descend toward, so `ForwardStagingAnchor` sits on the SR (measured `7,17` for the whole match) and is
*always* behind the lerp. **The rendezvous is backwards-biased in exactly the pre-contact opening that
`DeliverBeforeContact` exists to serve.**

The file header argues for a one-sided bound ("the anchor ADVANCES as the believed front moves, so a
transport that chased it unconditionally could be walked steadily deeper") — right about the danger it
names, and it left the opposite direction unguarded.

**Not fixed here, deliberately: no run budget remained to verify a fix, and shipping an unmeasured
behavioural change is what this project's discipline forbids.** `RendezvousWithOffensiveStaging` is left
`false` on both twins (`ai.yaml:1599`), so nothing regressed. The fix is a second comparison in the same
function — reject an anchor materially nearer our own SR than the fallback — plus a scenario in which the
armour actually musters forward, which `wip-transport-delivers` cannot provide because it contains no
combat units at all.

**UPDATE 2026-08-19 (`wt/rendezvous`): the second comparison now exists; the flag does NOT.**
`AnchorAcceptable` is two-sided — `anchorReach >= fallbackReach - withdraw`, config
`RendezvousMaxWithdrawCells` (default 6) — RED-pinned on the exact measured geometry above
(`AnchorParkedOnOurOwnSupplyRoute_IsRejected`: SR 6,16 / anchor 7,17 / lerp 32,10 resolved to `7` before
the fix, `32` after). Full suite 1640/1640.

Two things this update does NOT claim. **(1) The flag is still `false`** — the bound removes the known
blocker, it is not evidence the rendezvous helps, and flipping it is a behavioural change to
`@experimental` that needs its own measured run. Both profiles remain byte-identical on this path.
**(2) The scenario gap named above is untouched** — `wip-transport-delivers` still has no combat units, so
it still cannot show the armour mustering forward, and it is still the scenario that went green on a
walked distance (see the entry below). A run that flips the flag on *that* scenario would re-measure the
shuttle, not the rendezvous. Whoever flips it needs a scenario with armour first.

The deeper lesson, banked in DISCOVERIES 2026-08-19: the guard that was *supposed* to cover this —
`ResolveStagingAnchor` returning null on a degenerate anchor — is **grid**-granular, so a single grid step
publishes an anchor a map-cell off the SR and sails through. A grid-granular null check cannot stand in
for a map-cell distance bound, and the header comment asserting a nearer anchor was "unconditionally
safe" was reasoning that the fixture then pinned as truth.

## 2026-08-15: [medium] OPEN — `wip-transport-delivers` can go GREEN on a one-cell carry: its "moved ≥ 10 cells" clause is satisfiable by the passenger WALKING after it is set down (found while: offensive transport standoff, branch `wt/offense-standoff`)

The scenario defines a delivery as carried + returned + at least `DeliveredCells` (10) from where the
passenger started. Clause (c) is evaluated **whenever** the returned passenger is far enough away — not at
the moment of unload — so distance covered *after* the drop counts toward it.

Run `260815_202509` passed on exactly that. With the rendezvous bug above active, the carrier set its
passengers down at `7,17`, ~3 cells from their start; the verdict fired at tick ~1481 with the delivered
rifleman at `17,19`, which it reached **on foot** after `StageFreePool` re-recruited it post-unload. The
carry was real — `everCarried=5` is honest, five riflemen were genuinely out of world in a `Cargo` — but
the *distance* was walked.

This is the AUTOTEST.md §"who ELSE could satisfy your predicate" shape one level down: the mechanism under
test produced the carry, a different mechanism produced the displacement the assertion actually measured.
**The scenario therefore stays `wip-*` despite its first-ever PASS.** Fix: latch each passenger's position
at the tick it re-enters the world and measure clause (c) against *that*, so the distance credited is the
distance the carrier moved it.

## 2026-08-15: [low] OPEN — a transport's top-up passengers are ledger-committed and still fail to arrive: `still-coming=0 reason=NobodyElseComing` with four of five seats unfilled (found while: offensive transport standoff, branch `wt/offense-standoff`)

Baseline run `260815_201640`, seed 1017. The USA carrier's task reached `target=5` through three
`topup-added` lines, each of which calls `CommitTopUpPassenger` (`MountedTransportBotModule.cs:1094`), so
all of them held a `transport:<carrierId>` claim that `BuildFreePool` honours. It nevertheless departed
`aboard=1 still-coming=0 reason=NobodyElseComing` at tick 1015: the four committed top-ups neither boarded
nor remained outstanding.

Poaching by another bot module is **ruled out** by that commit. Candidates not distinguished here: the
ledger TTL (`DefaultCommitmentTicks`) lapsing mid-walk, or one of the traits that call `Actor.QueueActivity`
directly and emit no `Order` at all — `ModularBot.cs:137-145` names `StancePositioningExecutor`,
`AutoSeekSupplies`, `CohesionSlotMemory` and `DropsSupplyCache` as invisible to the gate, so any of them can
cancel a `RideTransport` with nothing in the order stream to show for it. Telling those apart needs a
per-passenger trace on `RideTransport` cancellation.

## 2026-08-15: [medium] OPEN — UNVERIFIED: `^ArtilleryRound`'s damage radii are smaller than its own inaccuracy, so a shell aimed at infantry may routinely do nothing at all (found while: field shell-swallowing bug, branch `wt/field-impact`)

Noticed while ruling out a damage cause for the field bug (which turned out to be effect-only — see
`DISCOVERIES.md` same date). **This is arithmetic off the YAML plus the warhead sources; it has NOT
been measured in play, and it is balance rather than a defect, so it wants a combat-sim run
(`DOCS/recipes/BALANCE.md`) before anyone acts on it.**

`^ArtilleryRound` (`weapons-ballistics.yaml:613-640`) has `Inaccuracy: 2c0` (`InaccuracyType: Absolute`),
so rounds scatter up to ~2 cells from the aim point. Its three damage warheads reach:

- `Warhead@Target: TargetDamage`, `Damage: 15000` — `Spread` is not restated, so it takes the engine
  default `new WDist(1)` (`TargetDamageWarhead.cs:24`) ≈ 1/1024 of a cell, and
  `TargetDamageWarhead.cs:64-65` skips any victim further than that. Effectively **direct hit only**.
- `Warhead@Spread: SpreadDamage`, `Spread: 64`, `Damage: 3000` — default falloff `{100,37,14,5,0}`
  (`SpreadDamageWarhead.cs:28`) over steps of 64, so damage is **zero at 256 WDist (1/4 cell)**.
- `Warhead@Shrapnel: SpreadDamage` (inherited from `^LargeExplosionEffects`,
  `weapons-effects.yaml:570-578`), `Spread: 256`, `Damage: 200` — zero at **1024 (1 cell)**, and only
  200 damage at best. **But it is `ValidTargets: Infantry, Unarmored`**, so it is the only warhead
  reaching a full cell *and it does not apply to vehicles at all*.

So the effective radius past which an artillery round does **nothing** is **1 cell against infantry
and ¼ cell against a vehicle**, while its own inaccuracy places the shell up to 2 cells off the aim
point. Against spread-out infantry the expected damage per shell may be near zero *independently of
fields*, and against vehicles the window is four times tighter still. That is the most likely
explanation for the "and no damage" half of the original live-play report — the field bug only ever
removed the explosion and the sound. Worth a combat-sim check before treating artillery-vs-anything
as working as intended.

## 2026-08-14: [high] OPEN — the `@experimental` bot runs at cash=0 for the entire match after its opening, so any demand-gated purchase is unaffordable exactly when it is finally justified (found while: PIPELINE 57 bot composition, branch `wt/composition`)

Surfaced by the new unconditional `[composition] census` line, which now carries `cash`, `starving`,
`trucks-desired` and `ammo-need`. Live `@experimental` vs `@experimental`, 6 sim-minutes, arena map:

- The supply gate works exactly as designed. At tick 40: `starving=0 trucks-desired=0
  ammo-need=False` — no truck, correctly. From tick 1240 onward: `starving` climbs to 6,
  `ammo-need=True` on **383 of 450** snapshots, `trucks-desired=2`. The demand path IS asking.
- **No truck is ever bought.** Across the 195 USA snapshots where `trucks-desired>0`, `cash=0` on
  **194** of them; the single exception reads `cash=40`. Peak cash after tick 600 is **40**, against
  a truck cost of 1000.

So the bot spends to zero continuously and never re-accumulates a four-figure sum. Consequences that
reach past supply: **every affordability-gated mechanism silently becomes opening-only.** That
includes the new `UnitFloors` (the AA floor at 300/head did not fire live for this reason) and any
future demand-driven purchase. The opening was the only moment the bot could afford anything
expensive — which is also the honest cost of setting `SupplyTruckFloor: 0`: the old floor bought its
two trucks at t=0 while 7,460 cash was on hand, and nothing later in the match can.

**Not a defect in the composition work and deliberately not fixed there** — it is an income /
spend-rate property of the profile, and it wants its own measurement (income per tick vs call-in
rate) rather than a knob nudged from here. Worth ruling on early because it silently caps the
usefulness of every gate that reads a budget.

## 2026-08-14: [medium] OPEN — nothing ever REQUESTS a combat engineer, so `e6` is procured only by an argmax that measurably never reaches it (found while: PIPELINE 57 bot composition, branch `wt/composition`)

`EngineerRouteOpenBotModule` (`:160`) and `LayeredDefenceBotModule` (`:188`) both list
`e6`/`e6.america`/`e6.russia` and both CONSUME engineers — but each implements
`ConditionalTrait<…>, IBotTick` **only**. Neither implements `IBotRequestUnitProduction`, so neither
can ever ask for one. Sweeping the modules that DO implement a production-request interface
(`AdaptiveProductionBotModule`, `CaptureCoordinatorBotModule`, `UnitBuilderBotModule`,
`HarvesterBotModule`, `McvManagerBotModule`) finds **no reference to `e6` in any of them** — every
apparent textual hit is a substring of the commit SHA `b8d2e601`.

So the engineer's ONLY procurement path is `UnitBuilderBotModule`'s composition argmax at an 8
per-mille target share. Measured on `--composition-plan` (200 cycles, `@experimental` America):
`e6.america` is bought **ZERO** times at 1-in-40 attrition and zero at 1-in-15 — the same
never-replaced shape as the medic, but without the medic's remedy, because the two modules that want
engineers cannot pull production the way `CaptureCoordinatorBotModule.MaintainTecnFloor` does for
technicians.

**Deliberately NOT fixed on `wt/composition`.** A `UnitFloors: e6: N` entry would produce engineers
and mask this — a standing floor is the wrong shape for a consumable a specific module wants on
demand, exactly as it is for the technician. The right fix is one of: (a) implement
`IBotRequestUnitProduction` on whichever module owns the need and request against demand (the
`MaintainTecnFloor` pattern), or (b) decide the engineer has no live role on either profile and drop
it from the two type lists so dead configuration stops looking load-bearing. Needs a ruling before
code. **Medium, not high, because it is unclear anything currently depends on engineers existing** —
both consumers degrade to doing nothing rather than failing.

## 2026-08-14: [medium] OPEN — `humvee` declares `RenderSprites` twice, so no map can override anything on it (found while: building the Javelin §6 measurement rig, branch `wt/javelin-probe`)

`vehicles-america.yaml:28` (`RenderSprites: Scale: 0.9`) and `vehicles-america.yaml:156`
(`RenderSprites: Image: humvee`) are two sibling nodes with the same key under the same actor.

The mod loads today because nothing forces that node through a merge. **The moment a second rules
source mentions `humvee` — which is what any map's `Rules:` section does — `MiniYaml.Merge` rejects
the duplicate and the map fails to load** with

```
MiniYaml.Merge, duplicate values found for the following keys: RenderSprites:
  [RenderSprites (at ww3mod|rules/ingame/vehicles-america.yaml:28),
   RenderSprites (at ww3mod|rules/ingame/vehicles-america.yaml:156)]
```

The behaviour is the engine's own, covered by `MiniYamlTest.TestMergeConflictsNoMerge` and friends
(`engine/OpenRA.Test/OpenRA.Game/MiniYamlTest.cs:531-578`). The symptom is a rules-load failure with
no verdict, so a scenario that touches the Humvee looks like a hung game rather than a YAML error.

**Not fixed here, deliberately.** `ActorInfo` builds traits by `traits.Add` per node
(`ActorInfo.cs:44-58`) into a `TypeDictionary`, which accepts duplicates — so the shipped Humvee
currently carries **two** RenderSprites traits. Collapsing the two blocks into one would take it to
one, which is a live rendering change to a shipped unit, and that is a call for whoever owns the
unit's appearance rather than for a measurement rig. Whoever takes it should check in game whether
the sprite is currently double-drawn and whether `Scale: 0.9` is in force, because those two
questions decide what the merged node should say.

Worked around in `mods/ww3mod/scripts/javelin-probe-lib.lua` by leaving the Humvee at its stock
8000 HP and respawning it after each kill instead of overriding `Health`.

## 2026-08-14: [high] OPEN — a bot cannot recover from having its base cleared: `CaptureCoordinatorBotModule` never reclaims its own neutralised structures, and technicians are capped at 3 (found while: widening soldier-clears-to-Neutral to all buildings, branch `wt/clear-all-buildings`)
## 2026-08-14: [low] OPEN, DEFERRED BY DECISION — a dispatched reclaim capturer never aborts, however hot the target turns while it walks (found while: bot reclaim review, branch `wt/bot-reclaim`)

Recorded so it is not rediscovered as a defect. `ReconcileGuardCommitments` releases a capturer's commitment only when the target is captured or gone, so nothing re-evaluates between dispatch and arrival — including the moment the technician's OWN vision finally reveals whatever is standing in the base. The believed fields that gated the dispatch are, for a reclaim target specifically, anti-correlated with the threat (the evicted building was the vision source), so the arrival is the first honest read anyone gets.

**Why it is LOW and not medium, which is the part worth carrying.** Its value is inversely proportional to how good the dispatch-time guard is. With the escort pre-check landed (`13a37573`) a reclaim is not dispatched at all unless an escort is recruitable and the tier is floored at Light, so the technician that walks in is accompanied and the abort would be saving a smaller loss. Had the dispatch-time gap been closed by *dispatching anyway* — the tempting shape, since it keeps recovery moving — this item would jump to medium immediately, because the lone technician would then be the normal case rather than the excluded one. Treat its severity as a reading of the dispatch guard, not a fixed property.

**Shape of the fix, when someone takes it:** add a third release condition to `ReconcileGuardCommitments` — believed danger at the committed target now above `ReclaimMaxDangerUnits` — as a pure predicate alongside the existing captured/gone tests, ~15 lines. What makes it a design change rather than a mechanical one, and why it was deferred twice: a released capturer needs a disposition (the reserve muster at `StageIdleCapturersReserve` is the obvious home) and a hysteresis story, or it oscillates between dispatch and abort as belief flickers across the threshold. Both of those are decisions, not plumbing.

## 2026-08-14: [med] OPEN — the bot never captures a Logistics Centre: the whole `CaptureSupplyDepots` tier sits below an early return that WW3MOD always takes (found while: verifying the capture path for bot reclaim, branch `wt/bot-reclaim`)

**Four config lines, a bool, an Info field and a scoring tier, all inert in the shipped mod.** `CaptureSupplyDepots: true`, `SupplyDepotActorTypes: logisticscenter` and `SupplyDepotIncomeWeight: 25` are set on `CaptureCoordinatorBotModule@experimental.tecn` (`ai/ai.yaml:143-146`), and `logisticscenter` is in `CapturableActorTypes` on both twins (`:122`, `:1928`). None of it can run.

Two independent reasons, either sufficient:

1. **The tier's only consumer is on a dead path.** `SupplyDepotIncomeWeight` is applied in `GetIncomeWeight` (`CaptureCoordinatorBotModule.cs:1709`), which has exactly two callers: `:795` and `ScoreTarget` at `:1682`, itself called only from `:800`. Both sit in the legacy per-target scan, *below* `if (poiMap != null) { QueueCaptureOrdersFromPoiMap(…); return; }` at `:743`. `world.yaml:311` declares the `PoiMap:` world trait, so `poiMap` is never null and that branch is unreachable in WW3MOD.
2. **`logisticscenter` is not a POI in the first place.** The live path's targets come from `PoiMap.GetCaptureTargets`, and `PoiMap.Discover` (`PoiMap.cs:212-215`) only admits names present in `PoiMapInfo.IncomeWeights` — `world.yaml:314-319`, which lists `oilb, fcom, bio, miss, hosp` and **not** `logisticscenter`. So even if (1) were fixed the actor would never enter the candidate set. `CapturableActorTypes` cannot rescue it either: that field is not consulted on the PoiMap path at all.

**Why this matters more than a dead config line.** `DOCS/reference/economy.md` has the Logistics Centre as the only thing a ground vehicle can rearm at, and the yaml comment at `ai/ai.yaml:143-146` explains at length that the tier exists precisely so a dry armoured force can take one and keep fighting. The bot has never been able to do it. Any read of "@experimental doesn't seem to resupply its armour" that assumed the depot-capture tier was contributing was reasoning about code that does not execute.

**Fix shape:** add `logisticscenter` to `PoiMapInfo.IncomeWeights` with a low weight so it is discovered, and move the supply-depot tier out of `GetIncomeWeight` into the PoiMap-path scoring (or into `PoiMap.TryScore`). Note the PITFALL at `world.yaml:312-313` — that dictionary is a *value* in a shared score and a $0 building listed there outbids real income POIs, which is the trap `SupplyDepotIncomeWeight` was invented to dodge in the first place. Not a one-liner; it needs the tier expressed where the live scorer can see it.

**Not established:** static reading only. No game was run, and I have not confirmed by observation that a bot never takes a Logistics Centre — only that no code path exists by which it could.

## 2026-08-14: [high] ADDRESSED on branch `wt/bot-reclaim` (unmerged) — a bot cannot recover from having its base cleared: `CaptureCoordinatorBotModule` never reclaims its own neutralised structures, and technicians are capped at 3 (found while: widening soldier-clears-to-Neutral to all buildings, branch `wt/clear-all-buildings`)

> **2026-08-14 follow-up, branch `wt/bot-reclaim` off `68e7c09f`.** The premise was checked against the code before anything was built. Reason (1) is real and **understated** — the mechanism is one level lower than described. Reason (2) is **wrong as written**. Both corrections are inline below, marked CORRECTION. The reclaim half is fixed by the `ReclaimNeutralisedStructures` lever on that branch; this entry stays open in spirit until someone watches a bot actually take its base back in play, which no run has yet done.

**Not a defect in the change that files it — a gameplay gap the change exposes, and the reason the previous, narrower version of this rule existed.** Under the uniform clear rule one enemy rifleman can walk a bot's base turning every AA gun, SAM, airfield, silo, Logistics Centre and derrick Neutral, surviving each time and paying only the 1000-tick (~40 s) delay per building. The bot has no answer, for two independent reasons:

1. **No reclaim logic anywhere.** `CaptureCoordinatorBotModule` scores *acquisition* targets, and its candidate pass is income-shaped — `CountReachableNeutralMoneyPois` (`:919-943`) explicitly restricts to "Neutral income structures only … IncomeStructure kind". Nothing watches for an own-structure→Neutral transition, and a cleared SAM or airfield is not an income structure, so it does not enter the target list on that path at all. A cleared derrick does, but competes on equal footing with any untouched neutral derrick — there is no "this was mine" bonus.

   **CORRECTION (2026-08-14, `wt/bot-reclaim`): true, and the cause is one level below the coordinator.** `CountReachableNeutralMoneyPois` is a *floor input*, not the target filter — the live target list is `PoiMap.GetCaptureTargets`. `PoiMap.Discover` (`PoiMap.cs:212-215`) only admits an actor as a POI candidate at all if its name is a key in `PoiMapInfo.IncomeWeights` (`world.yaml:314-319`: `oilb, fcom, bio, miss, hosp`) or is the Supply Route, and `TryScore` gates again on the same dictionary (`PoiMap.cs:491-493`). So a neutralised `afld`/`sam` is **not a low-priority target, it is not a target** — and the same blindness applies to the offense and garrison layers, which walk the same candidate list. (Not `pbox`/`hbox`/`gtwr`: those strip `-CaptureManager`/`-Capturable` outright at `structures-defenses.yaml:81-83`, `:171-173`, `:257-259`, so they cannot be evicted OR reclaimed and are out of scope for a different reason. AA defences inherit `^Defense` → `^Building` and keep theirs.) Widening the coordinator alone could never have fixed this. Note also that `CaptureCoordinatorBotModuleInfo.CapturableActorTypes` — which looks like the target whitelist and is the obvious place to add a name — is **never consulted on the live PoiMap path**; it only applies in the legacy no-PoiMap branch. See `WORKSPACE/DISCOVERIES.md`, same date.

2. **Three technicians, hard cap, consumed on use.** `UnitLimits` sets `tecn.america: 3` / `tecn.russia: 3` (`ai-america.yaml:41,102`, `ai-russia.yaml:40`) — the `tecn.america: 8` at `ai-america.yaml:200` is a production weight and is explicitly "additionally bounded by UnitLimits above". A successful capture **consumes** the technician (`^CapturesNeutralBuildings`, `ConsumedByCapture: true`), so reclaim rate is one building per technician built. Reclaiming a dozen cleared structures is arithmetically out of reach in any relevant window.

   **CORRECTION (2026-08-14, `wt/bot-reclaim`): there is no hard cap of 3 on the path that actually buys capturers.** `UnitLimits` is enforced in the *lottery* filter (`UnitBuilderBotModule.cs:1177-1179`), which governs blind production. The capture floor does not go through it: `MaintainTecnFloor` calls `RequestUnitProduction` / `RequestPriorityUnitProduction`, both of which land on the single-name overload `BuildUnit(IBot, string)` (`:816-842`) — thirty lines that check `BuildableInfo` and an empty queue and **nothing else**. No `UnitsToBuild`, no `UnitDelays`, no `UnitLimits`. The module's own comment at `:439-441` says so explicitly. The real bound on capturers is therefore `CaptureSupplyMath.EffectiveFloor` clamped to `TecnFloorMax` — **5**, not 3 (`ai/ai.yaml:172-174`) — with `ShouldRequestTecn` refusing to request once `alive >= floor`. The lottery may add up to 3 more on top. The consumed-on-capture half of the claim is correct and is the part that matters: a backlog of N buildings genuinely costs N technicians. **Consequence for anyone tuning this:** lowering `UnitLimits.tecn.*` to move budget toward combat will not do what `ai-america.yaml:60-63` says it does — that comment claims UnitLimits is "the REAL cap on simultaneous capturers", and for the floor path it is not. `TecnFloorMax` is the dial.

**Why high rather than medium.** A Neutral defence never fires but keeps its footprint, so the bot can neither use the building nor rebuild on the ground it occupies. The attacker converts an entire defensive network into permanent dead terrain at zero unit cost. Against a human it is a fair trade — they can clear yours back. Against a bot it is closer to a one-sided disable, and it scales with base size: the better the bot has built up, the more it loses.

**Not established:** never observed in play — game launches are user-gated and no match was run for this. This is static reading of the coordinator's candidate selection plus the unit cap. The *magnitude* in a real match is unmeasured; what is certain is that no code path exists to reclaim.

**Shape of the fix, when someone takes it:** the cheap half is a recency-weighted bonus in the coordinator's target score for a Neutral structure whose previous owner was the bot — which needs an owner-transition record the module does not keep, and needs the candidate pass widened beyond income structures. That does not touch the cap. A serious fix probably also needs the technician limit raised or a cheaper reclaim path for a structure nobody else took ownership of. Worth deciding first whether the intended answer is "the bot reclaims" or "the bot defends so this never happens" — they lead to different code.

## 2026-08-13: [low] OPEN, AND SELF-INFLICTED — men the staggered bail already dropped can block the exits `Cargo.Killed` needs, leaving the rest of the squad `Dispose()`d with no corpse and no kill credit (found while: pacing the emergency bail, branch `wt/bail-pacing`)

**Introduced by the change that files it**, so this is a cost of the staggered bail rather than a pre-existing defect. Recorded rather than fixed because it is out of scope for a pacing change and the fix is a real one, not a one-liner.

`INotifyKilled.Killed`'s eject loop is gated on `while (!IsEmpty() && CanUnload(BlockedByActor.All))` (`Cargo.cs`, `EjectOnDeath` branch). It stops the moment no exit is free, and passengers still in cargo at that point are never placed — `INotifyActorDisposing.Disposing` then calls `Dispose()` on each, which removes them with **no death, no corpse, and no kill credit to the attacker**.

**Why the stagger creates the exposure.** While the bail was atomic, a transport at Heavy emptied inside one tick: by the time it died the hold was already empty and the men were clear. The paced bail leaves an intermediate state that could not previously exist — some men out and *standing in the adjacent cells* with queued scatter orders, the rest still aboard. Those earlier bailers are exactly the actors `CanUnload(BlockedByActor.All)` now trips over. The window is the length of the stagger, ~32 ticks for a stick of five at shipped defaults.

**Needs a genuine choke to bite** — a bridge, a treeline gap, a transport wedged against a building — because the bail searches eight adjacent cells plus the hull's own, and on open ground the scatter orders clear those cells quickly. So: real, new, and low.

**Not established:** never reproduced in game. This is static reading of the two code paths; no autotest was run and nobody has confirmed the timing actually overlaps in play.

**Shape of the fix, when someone takes it:** give `Killed`'s loop the same per-passenger passability search `EmergencyBailOut` uses (adjacent cells shuffled, then the hull's own cell, each checked with `GetAvailableSubCell`) instead of the single `CanUnload` gate, so one blocked man does not end the loop for everyone behind him. That mirrors what the bail path already does, and would close the pre-existing version of this hole too.

## 2026-08-12: [medium] OPEN — the autotest single-instance lock covers `run-test.sh` only; `run-tournament.sh` and `loop-tournament.sh` ignore it entirely (found while: making `run-test.sh` incapable of losing a verdict, branch `wt/harness`)

`run-test.sh` acquires `~/.ww3mod-tests/run.lock` before launching. Neither tournament script contains any reference to it — grepping `run.lock` under `tools/` returns hits in `run-test.sh` and `selftest.sh` and nowhere else. So a tournament and a single test can run **two games at once**, and the lock's other job — serialising access to the one engine support directory — is not done for them.

**Verdicts themselves are no longer at risk from this.** `run-tournament.sh` already writes per-match files under `tools/autotest/tournament-results/<ts>_<scenario>/match_N.json` (`run-tournament.sh:262`), and `run-test.sh` is per-run as of this branch, so there is no shared verdict destination left for them to fight over. What is still shared and unprotected:

- **`debug.log` / `exception-*.log` / `syncreport-*.log`.** One support dir, both games writing into it. `run-test.sh` attributes a crash log to a run purely by its mtime being newer than that run's marker, so a concurrent tournament match that crashes would be reported as the single test's crash. A misattribution, not a lost verdict — but it points the reader at the wrong build.
- **`settings.yaml`.** The two scripts back it up to different paths, so they do not clobber each other's backup, but they both restore onto the same live file; interleaved runs can restore a stale copy.
- **The machine.** Two OpenRA instances competing for screen, focus and GPU.

**Not fixed here** — out of scope for the verdict-integrity work, and the right fix is not obvious: a 30-minute tournament holding a lock that blocks every single test is not clearly the behaviour anyone wants. Filed so the choice is made deliberately rather than by omission.

## 2026-08-12: [medium] UNTRIAGED — `halo` consumes four crash/autorotation conditions the lint says nothing grants, and `rotor-stopped` genuinely has no grantor anywhere in the mod (found while: establishing the post-`2fedd71b` `make test` baseline, branch `wt/lint-clear`, off `4d3c8f90`)

`make test` reports, three times in one run: ``Error: Actor type `halo` consumes conditions that are not granted: crash-disabled, autorotation, crash-landing, rotor-stopped.``

**This is the exact inverse of the class `2fedd71b` just fixed**, and the inverse is the dangerous direction. Granted-but-unconsumed (GTWR/PBOX/HBOX, `being-captured`) is cosmetic — a condition nobody reads. Consumed-but-ungranted means a `RequiresCondition` that can never become true, so the trait it guards is permanently off — or, in the `!condition` form, permanently *on*. Prior art in this repo: that is how the medic's suppression gate silently disabled him.

**NOT ESTABLISHED: whether any of this is behaviourally live.** I did not launch the game, did not run an autotest, and did not trace whether the guarded traits matter in play. Everything below is static reading of the YAML, filed so the next person starts from evidence rather than from the error string.

- **The three crash conditions ARE granted on the template**, which is what makes the error puzzling rather than obvious. `HeliEmergencyLanding` on `^Helicopter` sets `AutorotationCondition: autorotation`, `CrashLandingCondition: crash-landing`, `DisabledCondition: crash-disabled` (`rules/ingame/aircraft.yaml:175-177`). `HALO` does inherit it (`Inherits@Type: ^Helicopter`, `rules/ingame/aircraft-russia.yaml:4`) and overrides `HeliEmergencyLanding:` with only `SpinsOnCrash: false` (`:13-14`) — and a MiniYaml trait override *merges*, it does not replace. So why the lint treats them as ungranted for this one actor is unexplained.
- **`rotor-stopped`, by contrast, is really ungranted.** Every occurrence of that string under `mods/ww3mod/rules/` is a `RequiresCondition`; nothing assigns it. It is consumed by **six** helicopters in paired `!airborne && !rotor-stopped` / `rotor-stopped` guards — the shape of a spinning-rotor vs stopped-rotor idle overlay pair: `TRAN` (`aircraft-america.yaml:55,67`), `littlebird` (`:251`), `HELI` (`:392`), `HALO` (`aircraft-russia.yaml:64`), `HIND` (`:231`), `MI28` (`:408`). If it is never granted, the stopped-rotor half of each pair can never display and the spinning half never turns off.
- **The lint flags only `halo`, and that is the part that should worry us most.** The other five consume the same ungranted `rotor-stopped` and produce no error *and no warning* anywhere in the run — grepping the full 40k-line output for these four condition names returns exactly the three `halo` errors and nothing else. Either the check does not reach the other five, or something distinguishes HALO that I did not find. **If `make test` is ever promoted to a merge gate this coverage question matters more than the bug itself**: a condition lint that reports one of six apparently identical offenders is not yet trustworthy as a gate.

**Repro:** `make test` at `4d3c8f90` or `4d19a8e4`, then grep the output for `consumes conditions`. No build state or map is needed beyond the standard lint run.

## 2026-08-12: [low] LATENT — an unsatisfiable `Resupply` arrival test does not time out; it spins on no-op approaches forever (found while: fixing the subcell dock bug directly below, branch `wt/lc-rearm`)

Not a live defect after that fix, but the shape that made it so damaging is untouched and worth knowing before anyone adds a rearm host. When `isCloseEnough` is false and the unit is *already standing in* the cell the approach aims at, `MoveOnto.CalculatePathToTarget` returns `AlreadyAtDestination`, the child move completes instantly, `Resupply.Tick` re-queues another `MoveOntoTarget`, and that repeats every tick with **no attempt counter and no deadline**. The unit is never idle, so nothing upstream notices; it simply stops being a unit until the player gives it another order. That is why the subcell bug presented as "he just stands at the depot" rather than as an error.

The one configuration that can still reach it is an **even-dimensioned rearm host**. `BuildingInfo.CenterOffset` puts an even footprint's `CenterPosition` on a cell CORNER, which no unit can stand on, so a `WDist.Zero` tolerance is unsatisfiable there for vehicles as well as infantry — and the subcell fix deliberately does not paper over that (see the comment at `Resupply.cs`). Every ground rearm host today is `logisticscenter`, which is 3×3, so nothing reaches it. **If a rearm host with an even footprint is ever added, expect this bug back with vehicles included**, and fix the geometry rather than widening the tolerance.

## 2026-08-12: [high] FIXED — when two soldiers rearmed at the same Logistics Centre, the second one could never dock: `Resupply`'s arrival gate is `WDist.Zero`, and he was standing on a different SUBCELL of the same cell (found while: settling the `closeEnough = WDist.Zero` claim filed against `3e139294`, branch `wt/lc-rearm`, off `4d3c8f90`)

**Fixed on that branch** by measuring a ground unit's arrival from its CELL rather than its body: `Resupply` now subtracts `MapGrid.OffsetOfSubCell(mobile.ToSubCell)` from `self.CenterPosition` before the `closeEnough` comparison. `test-lc-rearm-partial-order` is green with `PairA` still holding a non-centre subcell and now taking a 50-round dock batch. The mechanism and the reasoning behind the scope are kept below because the trap generalises.

**Not the claim that was filed, and the difference matters.** The filed claim was that the dock-and-rearm pull "can never complete for anyone" because the unit is asked to reach the building's own centre cell, which the building occupies. That premise is **false**: the LC footprint is `=+= +++ =+=` (`structures.yaml:361`), which is `OccupiedPassable` and `OccupiedPassableTransitOnly` throughout with **no `Occupied` cell at all** (`FootprintCellType`, `Building.cs:20-27`), so its centre cell is walk-on-able. For an odd 3×3 the building's `CenterPosition` *is* that cell's centre (`BuildingInfo.CenterOffset`, `:207-211`), so a visitor standing there coincides with it exactly and the gate passes. Measured, not argued: a Bradley and a lone rifleman both reported offset `(0,0)`, took real batch-sized deliveries (100 and 50 rounds in a single tick, i.e. `Rearmable.RearmTick`, not the 1-round trickle), filled, and ended their errands.

**What survives is the ZERO tolerance**, and it bites the moment two infantrymen want the same depot. `AmmoPool.cs:374` reads `CloseEnough` off a `RearmsUnits` trait; the LC has `RepairsUnits` but **no `RearmsUnits`**, so `closeEnough` falls to the `WDist.Zero` default and `Resupply.cs:164` demands exact horizontal coincidence. A lone soldier survives that only by luck of allocation: this mod's `MapGrid.DefaultSubCell` resolves to `SubCellOffsets` index 3, whose offset is `(0,0,0)` (`MapGrid.cs:117-124,140-142`), and `Move` carries a unit's subcell along unchanged (`Move.cs:341/352/362` pass `FromSubCell` as the preference; `ActorMap.FreeSubCell` honours a free preference before anything else, `:323-324`). Two soldiers cannot both hold index 3 on one cell — so whichever arrives second is pushed to another offset and **his gate can never pass, however long he stands there**.

**Reproduction, `test-lc-rearm-partial-order` (currently RED on purpose):** `PairA` and `PairB` walk to one LC from equal distances. `PairB` lands on `(0,0)`, takes 50-round batches, fills at tick 666, errand finished. `PairA` lands on a non-centre subcell (`(10,-256)` at closest approach), takes **1 round at a time and never a whole batch**, ends 412/500, and the errand is **still running** at the 40 s deadline. The 1-at-a-time drip is the LC's own `replenish-soldiers` aura driving `ReloadAmmoPool` — free, no docking involved — which is why this degrades rather than denies: he does eventually fill, roughly 100× slower, and meanwhile never returns and never reports done.

**Why the fix went where it did.** `closeEnough` is a **shared** parameter — the same constructor serves aircraft at a pad, both repair orders, the minelayer and the Lua binding — so changing the *number* was never right. The asymmetry is that a cell-sharing unit's `CenterPosition` carries an offset it neither chose nor can shed, which is a property of the ground/subcell model and not of any caller's tolerance. Removing that offset is a no-op for full-cell units (`SubCellOffsets[0]` is zero, so every vehicle is unchanged), a no-op for `Repairable`/`LayMines` (their 512 already exceeds the ~393 subcell reach), and a no-op for aircraft twice over — they are not `Mobile`, and the approach test excludes them anyway, so on that path `isCloseEnough` only ever reaches the cancel branch. Giving the LC a `RearmsUnits` trait was rejected: it would have made the range lookup succeed while asserting something about the building that is not true.

**The other half of the damage is recorded separately** as the latent no-timeout spin, directly above.

**Staging note for whoever picks this up:** spawning a soldier directly onto the depot's centre cell does *not* reproduce it. At world init the building's influence is not yet registered when a map actor picks its subcell, so he still gets `DefaultSubCell` and comes out at `(0,0)`. Genuine arrival-order contention is what forces the offset.

## 2026-08-11: [high] REPRODUCED, mechanism confirmed — a saved game restored at real-match scale fails its sync-hash check, which latches `IsGameOver` and leaves the world permanently paused (found while: fixing the saved-game loadscreen + black viewport, branch `auto/saved-game-load`; reproduced on `auto/saved-game-pause`)

**2026-08-12 — STILL UNFIXED after the `Detectable` sync fix (`e1bbf244`) merged. Re-verified RED at `main @ 4d3c8f90`, 3 runs out of 3 lifetime.** Same command as the RED baseline (`--speed 8 --timeout 900 --sync-reports`), same failure (`paused=True predictedpaused=True gameover=True worldtick=3003 netframe=1007`). The fix took effect but was insufficient: `visionDetectableConditionToken` is now hashed **0** times on both sides and `CurrentVisibility` 21-22 times, yet the restore still fails its single validating comparison (`Out of sync frame: 1003`). **The remaining divergence is a different animal** — ~821 differing lines across ~30 trait types, and `SharedRandom` now differs (restored `#8202` vs recorded `#8695`, ~493 fewer draws), where the pre-fix run had exactly 3 differing traits and byte-identical RNG. So the paragraph below's "RNG drift is ruled out by evidence" **no longer holds on current main**. Not a frame-alignment artifact (no recorded frame in the 994-1005 window matches) and not a confound (`git diff --stat e1bbf244 4d3c8f90 -- engine/` is empty). **The divergence is DETERMINISTIC — confirmed by a seed-pinned pair at `main @ c440906e` (5 riverzeta runs lifetime, all RED).** Two runs at `--seed -324877760` produce restored states that are **byte-identical to each other** (0 differing lines, same `SharedRandom #8202`, same 821-line divergence), and the recording side is byte-identical too apart from the per-run `Game ID` and the ring's frame-membership list. So the RNG finding is real, not seed noise, and the earlier "`SharedRandom` identical, RNG drift ruled out" measurement was characterising a different fault. The only thing that floats is **which** frame the desync is detected on (1003/1004/1003), because the probe counts `PauseSettleFrames` in wall-clock-dependent render frames, shifting `LastSyncFrame` — do not misread that as nondeterminism. **2026-08-16 (SECOND LEAK — CAUSE NAMED; visibility REFUTED). Same defect class as the first: a bot module grants a condition directly instead of issuing an order.** At tick 2128 both lives agree on every visibility input (same target `4717`, `selfvisible=False` both, `groupdetected=False` both, `allies=[]` both) — **visibility is not the cause**. The only differing field is the ambush gate: recording `gatecount=1 tactics=True` (halts), replay `gatecount=0 tactics=False` (engages). Cause: `LaneAmbushBotModule.EnsureGatedAmbusher` (`:465-489`) calls `ec.GrantCondition(u, this)` directly from a bot tick — no order, so `GameSave` never records it and the replay never grants it. **The same method sets the stance correctly via `bot.QueueOrder(new Order("SetUnitStance", …))` four lines later, and that one survives the restore** — one method, two mutations, only the unordered one desyncs. **`@stable` is affected** (module header `:40-51`: the `@stable` twin runs at full parity and grants the condition). **Also invalidates my earlier "class closed" claim a second way:** the whole-match `SyncHash` sweep cannot see a condition grant any more than an activity write, so its zero result was never evidence of absence. Bound this class by STATIC audit of bot modules for direct mutation (`GrantCondition`/`RevokeCondition`, `QueueActivity`/`CancelActivity`, direct trait writes), not by the dynamic sweep. Full write-up: `WORKSPACE/DISCOVERIES.md` 2026-08-16.

**2026-08-16 (GREEN — FIXED, first pass in the scenario's history).** `test-savegame-resume-riverzeta` **passes**: `status=pass`, runner exit 0, `worldticks-since-resume=140 gameover=False paused=False`. Fix: all three `LaneAmbushBotModule` condition mutations now travel as `new Order("SetAmbushGate", u, false)` (ExtraData 1/0), resolved by `AutoTarget.SetAmbushGate` which owns the token — moved together, since ordering only the grant would desync the other way. Green verified non-vacuous: the probe paused before saving (`requesting save — paused=True`), **no `syncreport-*` bears this run's timestamp** (that file is written only from `OutOfSync()`), and `[exp-ambush]` posted lanes with units from tick 100 so the gate was genuinely being granted. Behavioural delta: the gate is now queued rather than instant, but live behaviour is unchanged because the halt already waited on the queued `SetUnitStance` beside it. **@stable affected and deliberately not gated off — re-take the ai-bench baseline.** Build clean, NUnit 1499/1499. **NOT closed in general:** one seed, one scenario, spectator + 2 bots rather than the user's human-vs-bot config, and the reverse hazard (*synced code reading state only bot ticks refresh*) remains unbounded with no detector. Full write-up: `WORKSPACE/DISCOVERIES.md` 2026-08-16 GREEN.

**2026-08-16 (STATIC AUDIT — the bot→world mutation class is BOUNDED at 3 sites).** Full sweep of `Traits/BotModules/**` for direct world-state mutation: **3 sites, all in `LaneAmbushBotModule`, all condition grants/revokes** — `:479` (the confirmed leak), `:501` and `:219` (revokes of the same gate, same class, lower rank). **Zero** activity-queue writes remain (the three prior ones became orders in `d5a4a42b`), **zero** other mutation verbs, **zero** direct trait-field writes (all 113 member-assignments target bot-local records), **zero** mutating trait-method calls. Checked the reader side too: the whole influence stack (`DangerFieldLayer`, `ControlField`, `BeliefStore`, `ThreatMapManager`, `SightingThreatLayer`, `PoiMap`) are self-ticking `ITick` **world** traits, so they update identically on the replay and are not leak vectors; `StancePositioningExecutor` only *writes* the bot ledger and never reads it for a decision. **Does NOT change the saved-game verdict** — the audit bounds *bot mutates → synced reads* but not the reverse hazard (*synced code reads state only bot ticks refresh*), which has no detector, and the scenario has never gone green. Full list, ranks and limits: `WORKSPACE/audit/260816-bot-direct-mutation.md`.

**2026-08-12 (SECOND LEAK — MECHANISM NAMED: the activity ends itself on a VISIBILITY predicate).** Inputs identical through world tick 2127; at 2128 the recording's `AttackMoveActivity` has ENDED (`act=none facing=256 aiming=False`) while the replay's still runs (facing 256→118, `aiming=True`), same cell on both. **Killed by measurement:** a missing order (both lives receive byte-identical orders — only `SetUnitStance` at wt 507 and `EnterTransport` at wt 2208, neither near 2128, so the save's order stream is complete for this unit) and an external cancel (`Actor.QueueActivity`/`CancelActivity` stack-traced for this actor across ticks 2118-2135: **zero calls on either life**, with the instrument validated by a parallel probe firing 46× under the same gating). **So the activity ended itself**, via `AttackMoveActivity`'s Stage-2 halt-before-contact (`:138-155`, gated on `AmbushTacticsCondition`): an Ambush unit that scans an enemy *while its group is still UNSEEN* ends its march and drops to idle. The unseen test is `GroupDetectedBy` (`:201-230`) = `CanBeViewedByPlayer(targetOwner)` for self, else for any Ambush ally within `AmbushCoordinationRadius` — **a visibility query**. **LEAD, not a conclusion:** visibility is the same domain as the original `Detectable` diagnosis, and nothing has established that visibility state reconstructs identically across a restore. Next instrument: `CanBeViewedByPlayer(targetOwner)` for 4712 and its ambush-radius allies at ticks 2126-2129 on both lives. **This path IS live on `@stable`** (`AttackMoveActivity.cs:148-151`: `LaneAmbushBotModule@stable` posts ambushers, sets Ambush stance and grants the gate).

**2026-08-12 (SECOND LEAK LOCATED — frame/actor/fields named, cause NOT named).** Onset is **net frame 711, world tick 2130**: frames 709/710 byte-identical, 711 differs by exactly 8 lines, and `SharedRandom` is still identical there (count 6022 both) with the RNG only diverging at 712 — so again RNG is downstream. The actor is `4712 at.america` (owner `Russia-bot`): `Mobile.Facing` 256 on the recording vs 118 on the replay, and `AttackFrontal` appears in the replay with `IsAiming: True` while absent from the recording's dump (the report omits a trait hashing to nothing, so absent reads as *not aiming*). I.e. on the replay the unit has turned and is aiming; on the recording it has not. **This is NOT another bot synced-write** — the whole-match per-bot-tick sweep is clean on the fixed tree — so do not assume the shape of the previous defect. Cause deliberately unnamed: budget went on locating it, and a guess would be worth less than the named frame. Next: instrument target-selection inputs for actor 4712 across frames 708-712 on both lives and diff. Instrument archived at `~/ww3-savegame-verify-artifacts/onset2-instrumentation.patch`.

**2026-08-12 (FIXED — the class; scenario STILL RED on a second leak).** All three sites now issue `new Order("Evacuate", actor, false)` via `bot.QueueOrder` instead of writing the activity queue directly; `"Evacuate"` is an existing order resolved by `DeliversCash@Rotation` into the identical `RotateToEdge` with the same `GetSellValue()` refund, unqueued so the cancel semantics are preserved. Build clean, NUnit 1402/1402. **Class verified closed**: a whole-match per-bot-tick `SyncHash` sweep reports **zero** mutations (it fired at tick 1536 before the fix). **But `test-savegame-resume-riverzeta` still fails** on a *different* divergence — 1436 differing lines (was 821), `SharedRandom` 644 apart (was 493) — which is the standing bisection caveat landing: only the first divergence was ever visible. The second leak is **not** a bot synced-write (ruled out by the sweep, by measurement). Next: re-apply `~/ww3-savegame-verify-artifacts/onset-instrumentation.patch`, bisect the new onset, dump both lives around it (~2 runs). **@stable is affected** by the helicopter site — see below. Not chased further: out of authorised scope.

**2026-08-12 (CAUSE NAMED) — a bot module writes synced state directly instead of issuing an order, and bot logic runs on the HOST ONLY, so this is a multiplayer desync mechanism and not a saved-game bug.** With `Debug.SyncCheckBotModuleCode = true` the guard fires at **world tick 1536 — the independently-measured onset tick** — `InvalidOperationException: RunUnsynced: sync-changing code may not run here` from `ModularBot.cs:211`. The site is `PoiOffensiveBotModule.SweepEjectedCrew` (`:2638-2652`): `crew.QueueActivity(false, new RotateToEdge(crew, true, sellValue))`, which cancels the crew's in-flight ejection move — hence `Mobile.ToCell` being the field that moves. Not an order ⇒ never recorded by `GameSave` ⇒ absent on replay (bot ticks early-return while `IsLoadingGameSave`), so recording `52,48` vs replay `52,49`. Gate is `EvacuateEjectedCrew: true` (`mods/ww3mod/rules/ai/ai.yaml:439`), **@experimental-only**; USA-bot is that profile and its `[exp-offense]` lines are in the log at `tick=1536`. **Multiplayer reach:** `Player.cs:224-232` enables bot logic `if (IsBot && Game.IsHost)` — host only — so the host cancels that crew's move and no other client does. Any game with a bot player is exposed; whether the four reported 2-human desyncs had one is NOT established. **Two sibling sites of the same defect class, gating unverified:** `PoiOffensiveBotModule.cs:2566` and `HelicopterSquadBotModule.cs:1741`. **The guard named the loop, not the module — the module was identified statically** (the throw lands after `currentModuleTag` is cleared at `:236`); wrapping each `t.BotTick(this)` in its own `RunUnsynced` would make it definitive in one run. Full write-up: `WORKSPACE/DISCOVERIES.md` 2026-08-12 "CAUSE NAMED".

**2026-08-12 (earlier) — the ~493 draws are a CONSEQUENCE, not the cause; onset bisected to net frame 513.** Measured causal order: frame 512 byte-identical → 513 the synced hash diverges with `SharedRandom` byte-identical (count 4389, `Last=2129567167` both sides) → 515 the RNG stream first diverges. Onset is one actor, one field: `crew.gunner.america` (USA-bot) `Mobile.ToCell` `52,48` vs `52,49`, same `FromCell`/`Facing`/`CenterPosition` — a different *next cell chosen*, not a different position. Upstream of it, **`LocalRandom` is out of step from frame 2** (52 vs 18 draws; 634 vs 25 by frame 512) because every one of its consumers is a bot module under `ModularBot.ITick.Tick`, which early-returns while `World.IsLoadingGameSave` (`ModularBot.cs:206`, `:304`) — and `LocalRandom` is not in the sync hash, so nothing detects it. **The leak path from that unsynced drift into `Mobile.ToCell` is still unnamed, and the instrument to name it already exists:** both suppression sites wrap their loops in `Sync.RunUnsynced(Game.Settings.Debug.SyncCheckBotModuleCode, …)`, which recomputes the sync hash around the block and throws if unsynced code wrote synced state — and it **defaults to `false`** (`Settings.cs:189`). Set it true and re-run. Killed en route (do not re-run): draw reordering (draws #4384-4392 identical in value, caller and order; no crew draw in the window) and bot-orders-not-recorded (`ModularBot` uses non-immediate `world.IssueOrder`, and `GameSave.ParseOrders` remaps them). Full write-up: `WORKSPACE/DISCOVERIES.md` 2026-08-12 "ONSET FOUND". The 900-tick arena `test-savegame-resume` still passes, so the scale dependence is unchanged. Full write-up: `WORKSPACE/DISCOVERIES.md` 2026-08-12 "VERIFIED RED".

**2026-08-12 — confirmed, 2 runs out of 2, on the canonical River Zeta map. UNFIXED.** `test-savegame-resume-riverzeta` (98×82, two bots, save at tick 3000) verdicts `paused=True predictedpaused=True gameover=True worldtick=3003`. The restore itself completes clean — `restore complete — gameover=False … netframe=1007` — and the latch arrives one net frame later (`netframe=1008`), when the single validating sync-hash comparison lands. Confirmed as a desync rather than the objectives path **independently of the probe's own claim**: the run writes `syncreport-…-0.log` containing `Out of sync frame: 1004`, and `SyncReport.DumpSyncReport` has exactly one call site — `OrderManager.cs:90`, inside `OutOfSync(frame)`. Chain: divergence at the validation frame → `OutOfSync` → `World.OutOfSync()` (`World.cs:681-686`) → `EndGame()` (`:74-87`) → `SetPauseState(true)` + `IsGameOver` latched, after which `SetPauseState` early-returns (`:455`) and `UnitOrders` drops inbound unpause orders (`UnitOrders.cs:230-231`). Exactly "the game seems ended, I cant unpause it". Both River Zeta runs desynced (frames 1003 and 1004); the 900-tick arena scenario passes, so the fault is **scale/content dependent** — which is why the first round of testing missed it and why the user hit it on a real match.

**The diverging field is named: `Detectable.visionDetectableConditionToken` (`Detectable.cs:149-150`) — a WW3MOD trait, not stock engine behaviour.** An instrumented run (`--sync-reports`, which arms sync reporting for the single-client case and dumps the recording side on the `GameSaved` acknowledgement) let the two sides be diffed at the desync frame: 34,084 lines each, **exactly 3 traits differ**, all `Detectable`, all that one field, each off by exactly one — `4692 e3.america USA-bot 144→143`, `4693 e2.america Russia-bot 42→41`, `4698 mt.america Russia-bot 41→40`. `SharedRandom` is identical on both sides (`379693090 (#7958)`), so RNG drift is ruled out by evidence rather than by argument. A condition token is an opaque allocation handle whose numeric value counts that actor's grant history, and `DetectableVisionChanged` (`:152-158`) revokes and re-grants on **every** visibility change — so `[Sync]` on it makes handle *identity* a determinism requirement, and one missed visibility transition anywhere in the replay fails the restore's single validating comparison even when nothing observable differs. Its three token fields (`:150`, `:162`, `:193`) are the **only** `[Sync]` condition tokens in the entire engine — added by `4eed77af` "Progressive fog (#5)" — so no stock OpenRA trait does this and it is not upstream's to fix.

**Still open: why the restored replay performs one fewer `DetectableVisionChanged` for those three actors.** Off-by-exactly-one on each means one missed transition apiece, not accumulated drift. **Do not assume the fix is dropping `[Sync]`** — that would stop the restore failing, but if a visibility transition really is being missed, removing the sync only stops *detecting* a real divergence. **Left unfixed deliberately**, pending that answer and a review.

**Repro:** `./tools/autotest/run-test.sh --speed 8 --timeout 420 test-savegame-resume-riverzeta`. Evidence archived under `~/.ww3mod-tests/screenshots/260812_003105_test-savegame-resume-riverzeta/` (verdict + debug.log + syncreport).

### Original triage (superseded — kept for the reasoning)

First-ever user test of load-game reported the restored session stuck on `Paused` with the pause control having no effect ("the game seems ended, I cant unpause it"). The all-black viewport it was seen alongside **is** fixed (`MenuPaletteEffect` never faded in — see `WORKSPACE/DISCOVERIES.md` 2026-08-11), and that fix may well dissolve this report too: with terrain and every actor rendered black, an *unpaused* game is indistinguishable from a paused one, so "I can't unpause it" was never confirmed to be a pause fault at all.

**2026-08-12 — now exercised automatically, and it did not reproduce.** `test-savegame-resume` + the `GameSaveRoundTripProbe` world trait drive a full save→restore round trip in one process (details in `WORKSPACE/DISCOVERIES.md` 2026-08-12). With a bot opponent, a 900-tick match and a genuine trailing `Pause` order in the save, the restore comes back paused as designed and resumes cleanly the moment the auto-opened options menu is dismissed — `paused=False`, world ticking. So the round-trip mechanism is sound **at that scale**. The user's session was a `Conquest: River Zeta WW3` skirmish whose replay took ~5 minutes, i.e. plausibly 20–30× more ticks on a real map; this test does not reach there, so it does not close the report.

**Correction to the previous elimination — `IsGameOver` is NOT ruled out.** *(Written before the River Zeta run; the hypothesis below is now confirmed.)* The earlier argument (`MissionObjectives.cs:174`'s predicate cannot be satisfied while the player reads "Mission: In progress") is sound, but it only covers *one* of the two callers of `World.EndGame()`. The other is `World.OutOfSync()` (`World.cs:681-686`), and the saved-game restore is validated by exactly one sync-hash comparison — `GameSave.ParseOrders` deliberately ends by replaying the recorded sync packet (`GameSave.cs:262-263`), and `OrderManager.ReceiveSync` calls `OutOfSync` on mismatch (`:174-181`). A restore that fails that check lands in `EndGame()` → `SetPauseState(true)` + `IsGameOver` latched (`World.cs:74-87`), after which `SetPauseState` early-returns (`:455`) and `UnitOrders` drops inbound unpause orders (`UnitOrders.cs:230-231`). That is unrecoverable-by-design and matches the report word for word. **Status: reachable and symptom-exact, but no trigger demonstrated** — the obvious one (synced-RNG draws inside the bot ticks the restore suppresses) was checked and does not exist in WW3MOD today. Treat as the leading open hypothesis, not as the cause.

**Next step if it recurs:** the probe already reports `gameover=` in its verdict, so a desync would show up as a FAIL with `gameover=True`. To chase it, raise `SaveAtTick` and run the scenario on a real Conquest map with production, so the recorded match accumulates the bot and unit state a short arena run never does.

## 2026-08-11: [low] Upstream `IngameMenuLogic` only knows about `MenuPostProcessEffect`, so any mod using the older `MenuPaletteEffect` loses every menu fade (found while: fixing the saved-game black viewport, branch `auto/saved-game-load`)

`IngameMenuLogic.cs:187` does `world.WorldActor.TraitOrDefault<MenuPostProcessEffect>()`, and `:188` / `:291` / `:316` / `:378` / `:572` all drive fades through that one handle. The two traits are parallel implementations of the same feature (`MenuPaletteEffect` for palettised rendering, `MenuPostProcessEffect` for the shader path) and neither shares an interface, so a mod registering only the palette variant — WW3MOD does, `mods/ww3mod/rules/palettes.yaml:144` — silently gets no menu darkening at all.

That is cosmetic on its own, but it is what made the saved-game path render an all-black world: `MenuPaletteEffect.GameLoaded` deliberately delegated its fade-in to "the menu opening", which for this trait never happens. That delegation is removed on this branch; the missing *menu* fade is left as-is because WW3MOD's `MenuEffect` is `None` anyway, so nothing is currently lost. **Fix shape if it ever matters:** give both traits a shared interface (`IMenuFadeEffect { void Fade(EffectType) }`) and have `IngameMenuLogic` resolve that instead.
## 2026-08-11: [high] Creating ANY veteran actor hard-crashes the game — all 13 `UpdatesPlayerStatistics.OverrideActor` values are capitalised, and the actor lookup is case-sensitive (found while: select-by-type from the build menu, branch `auto/select-by-type`)

Placing a single `e3r1.america` on a test map killed the game on world load:

```
KeyNotFoundException: The given key 'E3.america' was not present in the dictionary.
  at OpenRA.ActorInfoDictionary.get_Item(String key)      ActorInfoDictionary.cs:36
  at PlayerStatistics.<>c__DisplayClass25_0.<.ctor>b__0   PlayerStatistics.cs:74
  at UpdatesPlayerStatistics.Created                      PlayerStatistics.cs:346
```

`ActorInfoDictionary`'s string indexer is a raw `dict[key]` (`ActorInfoDictionary.cs:36`) over a dictionary keyed by **lowercased** actor names — note the sibling `SystemActors` indexer on the very next line explicitly calls `.ToLowerInvariant()`, so the omission is visible in place. Every veteran variant in the mod feeds it a capitalised name: `grep -c "OverrideActor: [A-Z]"` over `mods/ww3mod/rules/ingame/*.yaml` returns **13 of 13** — every single `OverrideActor` entry is affected (`E3.america`, `E1.america`, `E2.america`, …). `UpdatesPlayerStatistics.Created` runs on actor creation, so the crash fires for *any* veteran that ever enters the world, not just map-placed ones.

Why it has stayed latent: the veteran variants all carry `-Buildable`, so nothing produces them through the normal queue, and no shipped map appears to place one. It is a loaded gun rather than a live failure — but anything that promotes a unit into a veteran variant, or any map that places one, dies instantly.

**Fix shape** (deliberately NOT done here — out of scope for a UI branch, and it touches player statistics for 13 actors at once): lowercase the 13 YAML values, *or* make `ActorInfoDictionary`'s string indexer lowercase its key the way the `SystemActors` overload already does. The engine-side fix is one line and closes the whole class, but it changes a shared lookup used well beyond statistics, so it wants its own change with its own verification rather than being smuggled in. Either way this needs a test that actually creates a veteran — there is currently none.
## 2026-08-11: [HIGH] The same move-interrupt still stalls a PARTIALLY dry unit, and there the attack guard does not catch it — THIS IS THE STANDARD RIFLEMAN, NOT AN EDGE CASE (found while: fixing the out-of-ammo move wedge, branch `auto/ooa-wedge`; severity raised from med by adversarial review)

The fix on that branch gates `SmartMoveActivity`'s opportunistic-fire interrupt on `AmmoPool.CannotFight`, which requires **every** pool empty. One pool short of that, the old shape survives: a rifleman with a spent rifle and a loaded RPG, facing infantry, still gets the paused rifle back from `ChooseArmamentsForTarget` (`AttackBase.cs:438-442` — filters `IsTraitDisabled`, and an empty armament is *paused*), still reports a weapon in range (`SmartMoveActivity.cs:94`), still cancels his own move child and queues an attack.

What differs is the ending, and it is not obviously better. `CannotFight` is **false** for this unit, so the guard at `Attack.cs:117` does not fire — the attack activity persists rather than ending, and the man stops mid-move and aims a weapon he cannot fire until the target dies or leaves range. That is the original `68c2527a` symptom ("keeps closing to range, aiming, and never firing") reproduced through a different door.

**Why it was not fixed with the main bug:** the honest fix is probably to filter `interruptingArmaments` on something ammo-aware per-armament rather than per-actor, and `AmmoPool.cs:210-214` explicitly warns off the obvious candidate (`IsTraitPaused` also carries `garrisoned-at-port`, which would call a garrisoned man with a full magazine dry). That needs its own predicate, its own scenario pair and its own measurement — riding it along would have made the primary fix harder to review and to revert.

**Reproduction shape:** clone `test-dry-move-order-obeyed` and set `InitialAmmo: 0` on only ONE of a two-pool class (`^E3`, `^TL`, `^SF` all carry two), with a Bait the empty weapon is valid against.

**Severity raised to HIGH by adversarial review (2026-08-11), with the reasoning that matters:** this is not a rare
combination, it is `^E3` — the Rifleman, the mod's standard infantryman — in a routine post-firefight state. `^E3`
carries a primary DMR (`Ammo: 100`) and a secondary RPG (`Ammo: 1`), and the RPG declares `InvalidTargets: Infantry`.
So after any infantry-vs-infantry engagement the DMR is spent while the RPG is still loaded because there was never a
valid target for it. `CannotFight` requires EVERY pool empty, so it returns false, the new guard does not fire, the
move interrupt still cancels the move, and `Attack.cs`'s guard does not end the attack either. Both `ReloadAmmoPool`s
are gated `RequiresCondition: replenish-soldiers`, so the state does not self-heal in the field.

The result is worse than the bug that was just fixed: the man aims a weapon he cannot fire, indefinitely, ignoring his
Move order, and he has **no recovery path at all** — he never goes idle, and `AutoRearmIfAllEmpty` needs all pools
empty. **Expect the user to reproduce half of the original complaint by testing with a rifleman.**

**FIXED on `auto/ooa-rifleman`.** The move interrupt now asks whether an armament is *usable* rather than whether the
actor is *empty*: `SmartMoveActivity`'s `interruptingArmaments` filter gained `&& !a.IsTraitPaused` alongside the
existing `NoSelfDefenseInterrupt` test. `ChooseArmamentsForTarget` already answers "valid against THIS target", so the
two together are the per-armament, per-target, ammo-aware predicate this entry asked for. The `AmmoPool.CannotFight`
gate above stays as a cheap early-out for the wholly dry unit — it also skips the scan, which keeps this branch's
`SharedRandom` draw count identical to `main`'s.

`IsTraitPaused` was chosen over an ammo-only test because it is exactly the condition `Armament.CanFire` refuses to
fire on (`Armament.cs:327`), so it also covers the suppressed / EMP'd / heavily-damaged weapon — `^AT`'s
`PauseOnCondition: !ammo-primary || suppressed >= 10` wedges a move by the identical route. The warning above against
`IsTraitPaused` (`AmmoPool.cs:210-214`, `garrisoned-at-port`) applies to `CannotFight`'s question — "send this man to
resupply?" — not to this one: a garrisoned man with a full magazine is still a man who cannot fire *now*, which is all
the move interrupt is asking. The fix was deliberately NOT put in `ChooseArmamentsForTarget`: nine callers, and
`AttackBase.AbandonWhenArmamentsPaused` exists precisely because "all armaments paused" must not end an attack by
default, so filtering there would silently flip that opt-in on for every unit.

Pinned by `test-partial-dry-move-order-obeyed` (RED before, GREEN after). Its Lua asserts primary=0 AND secondary>0 up
front, so the scenario cannot silently decay into the already-fixed all-empty case and pass on the old guard.

**Not fixed, same class, deliberately left:** `AutoTarget.ActiveAttackBases` filters `IsTraitDisabled` only, so a
paused *AttackBase* (as distinct from paused armaments) still wedges a move the same way — the identical observation
is already recorded at `Attack.cs:246-252`.

## 2026-08-11: [med] `SeekSupplyProvider` latches `moveQueued` and never clears it, so an errand whose move ends without arriving spins forever with no child (found while: fixing the out-of-ammo move wedge, branch `auto/ooa-wedge`)

`SeekSupplyProvider.Tick` queues its move once and sets `moveQueued = true` (`SeekSupplyProvider.cs:121-126`). The flag is cleared in exactly two places, both of which are explicit cancels (`:91`, `:114`). If the child `MoveWithinRange` instead **ends on its own without the unit being inside `rearmRange`** — no path to the truck, or the path fails — then `ChildActivity` goes null, `moveQueued` stays `true`, the out-of-range branch at `:121` declines to re-queue, and `Tick` returns `false` unconditionally at `:129`.

The result is an activity that is never finished and has nothing to run: the unit stands still, is never idle (so no `INotifyBecomingIdle` retry), and `AmmoPool.IsSeekingRearm` keeps reporting true — which means `StarvingRecruitGate` and the bot censuses withhold it permanently. Precisely the "unit deleted from the game in all but name" that `AutoSeekSuppliesInfo.ReturnErrandStallTicks` was written to prevent.

**And the stall guard does not cover this path** — see the next entry. Not fixed here because it is a second, independent behavioural change and this branch was scoped to one.

**Fix shape:** clear `moveQueued` when `TickChild` reports the child finished, or drop the latch and let `QueueChild` be idempotent on a null child.

**Still open — and explicitly NOT the mechanism behind the "errand outlives its reason" report of the same day** (branch `auto/supply-errands`). Worth recording because it was the leading hypothesis going in: this latch wedges a unit standing **still**, and that report was a unit that kept **walking**. The mechanism there turned out to be a different defect in the same file — `SeekSupplyProvider` never set `ChildHasPriority = false`, so `Activity.TickOuter` (`Activity.cs:112`) skipped the parent's `Tick` for as long as the move child was alive, and *none* of the activity's per-tick re-evaluation had ever run mid-route. That is now fixed; this latch is untouched and its symptom stands.

The two do interact, slightly in the latch's favour: with `Tick` now running every tick the periodic retarget is live for the first time, so a latched errand gets an escape hatch whenever `FindBest` returns a different provider (it clears `moveQueued` and re-queues). That is not a fix — a latched errand whose closest provider is stable still spins forever.

## 2026-08-11: [low] `AutoSeekSupplies`' errand stall guard watches only the errands it dispatched itself — which is the LESS common half in real play (found while: fixing the out-of-ammo move wedge, branch `auto/ooa-wedge`)

`TickErrand` (`AutoSeekSupplies.cs:317-355`) is the abandon-a-doomed-walk guard, and it runs only when `onErrand` is set — set exclusively by `BeginWatching()` at `:299`, i.e. only for errands dispatched by this trait's own `ITick`. The in-code rationale (`:255-256`) is that cancelling *a player's* order is not this trait's call, which is right.

But the same exemption silently covers `AmmoPool`'s own dispatches, which are not player orders: `INotifyAttack.Attacking` → `AutoRearmIfAllEmpty` on the shot that empties the pool (`AmmoPool.cs:298-299`), and `INotifyBecomingIdle` (`:303-306`). **The shot that empties the pool is how most units in a real match enter resupply**, so the common path is the unwatched one, and the guard covers mainly the periodic re-check that fires when that first dispatch did not happen.

Low rather than medium only because a wedged errand of this kind needs the previous entry's latch (or an unreachable host) to actually stick. The two should be fixed together and measured with one scenario: dry unit, host with no route, assert the unit is released within `ReturnErrandStallTicks` regardless of which dispatcher sent it.

## 2026-08-11: [low] `Mobile.ResolveOrder`'s `ForceMove` branch skips the target-validity check that `Move` performs (found while: fixing the out-of-ammo move wedge, branch `auto/ooa-wedge`)

The `"Move"` branch opens with `if (!order.Target.IsValidFor(self)) return;` (`Mobile.cs:1014-1015`). The `"ForceMove"` branch immediately below (`:1024-1033`) has no equivalent — it goes straight to clamping the cell. Both then apply the same shroud check, so the exposure is narrow, but the asymmetry is unintentional-looking rather than commented, and `ForceMove` is the fork's own addition (stock OpenRA emits one order string for both).

Worth noting because this branch pair is now load-bearing for a diagnostic: "reproduces on Move but not Force-Move" localises a defect to the `IWrapMove` wrappers, and that inference is only as good as the two branches being otherwise identical.

## 2026-08-11: [med] `test-stance-optout` is a FALSE GREEN — it silences its own units with the very stance whose opt-out would mask the two opt-outs it claims to test (found while: fixing the three-scenario stance regression cluster, branch `auto/stance-reds`)

`test-stance-optout` asserts that a `HoldPosition` unit and a `deployed` unit are never repositioned by `StancePositioningExecutor`. But its setup puts **both units on `HoldFire`** (`test-stance-optout.lua:23-25`) to silence combat — and since `174075e9` the executor relinquishes management of anything below `FireAtWill` (`StancePositioningExecutor.cs:318`) **before** it ever reaches the `HoldPosition` check (`:327`) or the `deployed` check (`:298`). The fire-stance opt-out alone holds both units still, so the scenario passes whether or not the two opt-outs under test work at all. It has been passing on the wrong mechanism since 2026-07-25, and its GREEN currently carries **no information** about `HoldPosition` or `deployed`.

Exactly the same setup convenience — "silence the unit by fire stance" — is what turned the three sibling scenarios RED (see `WORKSPACE/DISCOVERIES.md`, 2026-08-11). The siblings failed loudly because they assert movement; this one fails *silently* because it asserts stillness, which is the more dangerous half of the same defect: a positive scenario that stops testing goes red, a negative scenario that stops testing stays green.

**Fix shape** (deliberately NOT done on `auto/stance-reds`): put both units on `FireAtWill` and silence combat from the enemy side instead — `EnemyA`/`EnemyB` stay `HoldFire`, and give the `t90` `Targetable: TargetTypes: NoAutoTarget` in `rules.yaml`, which is exactly the repair applied to the three siblings there. Then `HoldPosition` and `deployed` become the only things holding the units, which is what the scenario is for. **This needs its own autotest run to land** — it is a behavioural measurement (the scenario could legitimately go RED, which would mean one of the two opt-outs is actually broken and has been masked for weeks), and `auto/stance-reds` was scoped to three named scenarios. Do not fix it blind.

The corpus guard added on that branch (`StancePositioningFireStanceTest.ExecutorScenariosDoNotSilenceTheUnitUnderTestByFireStance`) carries `test-stance-optout` in its `ExcludedScenarios` list, with a comment pointing here. **Delete that exclusion as part of the fix** — once the scenario silences from the enemy side, it belongs under the guard like its siblings.
## 2026-08-11: [med] `IskanderTargeter`/`HIMARSTargeter` deal their full 50 damage to essentially the whole infantry roster — their `Versus` tables zero an armor class that does not exist in this ruleset and omit three that do (found while: the danger-field durability rescale, branch `auto/danger-durability`)

**THE FIX IS A DATA CHANGE TO TWO `Versus:` BLOCKS IN `weapons-missiles.yaml`. There is no engine bug here — `DamageWarhead` behaves as documented. Do not go looking in C#.**

Both force-fire spotter weapons (`mods/ww3mod/rules/weapons/weapons-missiles.yaml:284-306`; `HIMARSTargeter` inherits `IskanderTargeter`) declare `Damage: 50` with `Versus:` zeroing `None, Wood, Concrete, Light, Medium, Heavy, Brick`. The evident intent is "marks a target, harms nothing". The data does not say that:

- **`Brick` is not an armor class in WW3MOD.** The nine that exist are `Concrete, Heavy, Indestructable, Kevlar, Light, Medium, None, Unarmored, Wood` (`OpenRA.Utility ww3mod --danger-reference` prints the set). The `Brick: 0` line modifies nothing.
- **`Kevlar`, `Unarmored` and `Indestructable` are unlisted**, and an unlisted class takes the *unmodified 100%* — `DamageWarhead.DamageVersus` filters to the classes the table lists (`DamageWarhead.cs:105`), so **omission is the opposite of a zero**.

**Who is affected: `Kevlar` is `^Soldier`'s armor (`mods/ww3mod/rules/ingame/infantry.yaml:173-174`), and 16 templates inherit it via `^CamoSoldier`** — `^AR`, `^SN`, `^AT`, `^AA`, `^E1`, `^E2`, `^E3`, `^E4`, `^E6`, `^MT`, `^TL`, `^MEDI`, `^DR`, `^AmphibiousSoldier`, `^PILOT`, `^CrewMember`. **Two revert to `None`**: `^PILOT` (`infantry.yaml:2404`) and `^CrewMember` (`crew.yaml:16-17`). **So 14 resolve to Kevlar**, and each fans out to its bare concrete key plus both faction variants.

_(Enumeration corrected 2026-08-11 by adversarial review. The original list reached the right headline of 14 through two cancelling errors: it omitted `^DR` — `infantry.yaml:2292`, inherits `^CamoSoldier` with no armor override, concrete actors in both factions at `infantry-russia.yaml:117` and `infantry-america.yaml:117` — and it omitted `^CrewMember` entirely while counting `^PILOT` as the sole reverter. Anyone acting on this list should use the corrected one; the count was right by luck.)_ That is effectively the entire combat-infantry roster plus medics. Designating a target with either weapon therefore does 50 damage to every one of them in the blast.

Small in absolute terms against 200-HP infantry, but it is damage the design does not intend, it is attributable to the spotter rather than the missile it designates, and a player would feel it — an Iskander/HIMARS designation quietly chips every friendly-adjacent squad it paints. It also means the danger field is **right** to keep counting these weapons as threats, which is how this surfaced (see `DISCOVERIES.md` 2026-08-11).

The bug is **selective**, which is why it survived: `^Infantry`'s own `Armor: Type: None` (`infantry.yaml:34`) is what non-`^Soldier` actors keep, and the table *does* zero `None` — so a spot-check against one such unit reads as correct.

**Not fixed here** — it is a data/balance change in a file this branch does not otherwise touch, and this branch is deliberately one measurable change. Fix shape: in both `Versus:` blocks add `Kevlar: 0`, `Unarmored: 0`, `Indestructable: 0` and drop the dead `Brick: 0`. Verify with `OpenRA.Utility ww3mod --danger-reference`: both should then report `harmless=True`, the ground-contributing population should fall 92 → 90, and `min` should rise off the targeters' 21.

**Worth a sweep, but not on this branch:** a `Versus` table naming a class this ruleset does not have, while omitting three it does, is the signature of a table **copied from another mod and never re-validated**. Other weapons may share it. A cheap detector already exists — `--danger-reference` prints `unlisted-armor-classes=[…]` for every warhead whose listed values are all zero; widening that to every warhead with a non-empty `Versus` would find the rest statically.

## 2026-08-11: [medium] An idle unit re-issues its attack order roughly every tick, marking the target for overkill accounting ~30x per decay window, so healthy targets read as saturated and other units skip them (found while: target preemption, branch `wt/autotarget-preempt`)

Pre-existing on `main`; the preemption branch neither introduced nor fixed it. Adjudicated out of scope in review.

- `ScanForTarget`'s override loop returns the incumbent target **before** the `nextScanTime` re-arm (the `foreach (var oat in overrideAutoTarget)` block sits above the `SharedRandom.Next` assignment in `AutoTarget.cs`). So when the override answers — the normal case for an idle unit holding a persistent opportunity target — `nextScanTime` is never reset and stays `<= 0`.
- `TickIdle` therefore calls `ScanAndAttack` again on the next tick, and every tick after, re-issuing an attack order on the same target.
- Every `AttackBase.AttackTarget` call runs `AutoTarget.MarkTargetForAttack`, and that mark's decay is tuned to outlive a reload (60 ticks, `Actor.cs`). So a target engaged by a single idle unit accumulates marks roughly an order of magnitude faster than the tuning assumes.
- Consequence: `AverageDamagePercent` reads far above the real incoming damage, so `ChooseTarget`'s hard `OverkillThreshold` skip and its soft-overkill penalty make *other* units avoid a target that is not actually saturated — the opposite of the intent, and it compounds with the number of engaged units.

**Fix shape (not attempted):** either re-arm `nextScanTime` on the override path too, or make the mark idempotent per (attacker, target) within a decay window. The first is nearly a one-liner but changes scan cadence for every unit in the game, so it wants its own branch and its own benchmark; the second is more surgical but touches accounting that several systems read.

**Note the shape.** Same class as the `HasValidTargetPriority` / `OnlyTargets` bug that branch surfaced: an accounting rule silently wrong for a long time because nothing asserts on it directly and the symptom is diffuse ("units sometimes ignore a good target") rather than a visible failure.

## 2026-08-10: [low] `VehicleCrew` is edge-triggered and `Cargo` is level-triggered, so a transport already burning when it receives passengers evacuates them but never its crew (found while: the vehicle-occupant pass, branch `wt/vehicle-occupants`)

Both occupant systems now bail on the same damage state (`Heavy`, HP <50%), but they decide *when to look* in incompatible ways:

- `VehicleCrew` uses `INotifyDamageStateChanged` and requires a **transition** — `e.DamageState >= info.EjectionDamageState && e.PreviousDamageState < info.EjectionDamageState` (`VehicleCrew.cs:163`). No crossing, no ejection, ever.
- `Cargo` uses `INotifyDamage` and tests the **level** on each hit, latched (`Cargo.cs`, `Cargo.ShouldEmergencyBail`).

So wherever the threshold is not crossed while the crew trait is watching, the passengers leave and the crew rides the wreck down:

- a transport placed into the world already below 50% (map-authored damaged actor, or a scripted spawn);
- a transport **loaded while already burning** — `Cargo.Load` deliberately re-arms its latch so a new stick bails on the next hit, while `VehicleCrew` saw its crossing long ago and will not re-fire;
- (a transport repaired above 50% and shot back down *does* re-cross, so that case is fine — this is specifically about never having crossed since the crew trait started watching).

**Deferred deliberately, not overlooked.** The fix belongs in `VehicleCrew.cs`, which was out of scope for the branch that found this — the review scoped the bail-delay knob to `Cargo` only — and `main` churned that file twice during the work, including reverting `b3591ef5`. It wants to be its own change against current `main`. Fix shape: give `VehicleCrew` a latched level test alongside the edge one, so it asks "am I below the line and still crewed?" rather than "did I just cross it?".
## 2026-08-10: [high] `Mobile.MoveResult` is declared and read but NEVER ASSIGNED, so "the move finished" and "the move gave up" are both unreachable engine-wide — a unit sent somewhere it cannot path to repaths forever and is effectively deleted from the game (found while: adding the infantry out-of-ammo return path)

`Mobile.MoveResult` (`engine/OpenRA.Mods.Common/Traits/Mobile.cs:265`) is a plain `{ get; set; }` auto-property. Engine-wide it has **three readers and zero writers**:

| site | branch that is dead |
|---|---|
| `Activities/Move/MoveCooldownHelper.cs:69` | `CompleteCanceled` / `CompleteDestinationReached` ⇒ stop retrying |
| `Activities/Move/MoveCooldownHelper.cs:76` | `CompleteDestinationBlocked` ⇒ give up |
| `Activities/Move/MoveAdjacentTo.cs:107` | `CompleteDestinationReached` |

`InProgress` is the zero value of the enum (`engine/OpenRA.Mods.Common/TraitsInterfaces.cs:853-859`), so the field never changes and every one of those comparisons is permanently false. `MoveCooldownHelper.Tick` always falls through to `cooldownTicks = world.SharedRandom.Next(20, 31)`.

**Symptom:** any activity that moves toward a destination with no route neither completes nor fails. The unit stands still, repathing every 20–31 ticks, indefinitely. It never goes idle, so every idle-triggered recovery is unreachable for it too. If anything gates on "is this unit busy" — `AmmoPool.IsSeekingRearm`, `StarvingRecruitGate` — the unit is also withheld from all tasking permanently.

**Not fixed here.** Assigning the field correctly means auditing every `Move` completion path and deciding the right result per exit; getting that wrong changes pathing for every unit in the game, which is far beyond the branch that found it. Mitigation applied on the one path this branch newly routes infantry into: `AutoSeekSupplies.ReturnErrandStallTicks` cancels an errand that gains no ammo and changes no cell for 300 ticks, then cools down before retrying. `SeekSuppliesAndReturn` already had `MaxStalledTicks`; **`SeekSupplyProvider` and `Resupply` still have no guard**, so the bot's dry-vehicle sweep (`PoiOffensiveBotModule.SweepOutOfAmmoUnits`) remains exposed.

## 2026-08-10: [high] Nothing weighs AMMO STATE against recruitment or dispersal — the bot tears a starving platoon apart and streams half of it at enemy artillery while it holds 10 rounds a man (found while: `test-supply-under-danger`, the supply-doctrine work)

Five riflemen drained to 10/100 primary — every one below the `HuntStarvingThresholdPerMille: 250` bar the supply layer itself uses for "starving" — were recruited and split in half by two modules within ~100 ticks of spawn, while a supply truck was already en route to them. Their own log lines:

```
[exp-offense] axis-new player=USA-bot target=supplyroute#7 cell=62,16 action=Pressure tick=115
[exp-offense] order player=USA-bot target=supplyroute@62,16 action=Pressure units=3 cohesion=Spread clumpRadius=3 distToTarget=26 tick=115
[exp-ambush]  lane player=USA-bot anchor=supplyroute#7 post=28,17 units=2 tick=100
[exp-poi]     disperse player=USA-bot pool=5 centroid=(38,15) clumpRadiusCells=8 tick=545
[exp-poi]     disperse player=USA-bot pool=5 centroid=(40,13) clumpRadiusCells=9 tick=770
```

Three men were streamed EAST at the enemy Supply Route under `action=Pressure`, two were posted WEST to a `LaneAmbushBotModule` lane, and the clump radius went 1 → 9 — a platoon at 10% ammo pulled apart across ~30 cells, with the eastward half advancing toward believed artillery it had no ammunition to fight.

- **The defect is a missing term, not any one module.** `PoiOffensiveBotModule` and `LaneAmbushBotModule` both build a free pool from idle eligible units and neither consults `AmmoPool` state — although the ruleset already carries an agreed definition of "too low to fight" (`SupplyTruckHuntMath.IsStarving` against `HuntStarvingThresholdPerMille`), which `SupplyFollowerBotModule` uses in three places. The offense module does have `SkipOutOfAmmoUnits`, but that is an EMPTY test (0 rounds) and does nothing at 10%.
- **It fights the supply system directly.** The truck's drop point is derived from the platoon's own centroid; the platoon then scattered, so the centroid tracked the scatter rather than the front. That is the mechanism behind a drop anchor chasing a moving target, and it is a plausible contributor to the historical "supply-truck oscillation" reports.
- **Doctrinally it is backwards.** A platoon that cannot shoot should consolidate and wait for resupply, not open an axis. The supply layer treats these same five men as an emergency worth sending a truck 30 cells through 300,000+ believed danger for.
- ~~Not fixed here~~ — **FIXED 2026-08-10**, and the census was wider than two modules: six ground-tasking paths could pick up a starving man. `StarvingRecruitGate` (`engine/OpenRA.Mods.Common/Traits/BotModules/StarvingRecruitGate.cs`) is the one shared predicate, delegating the threshold comparison to `SupplyHuntMath.BelowSeekThreshold` so it cannot drift from the supply layer's reading; `StarvingRecruitThresholdPerMille: 250` is set on the six `@experimental` blocks in `ai.yaml` (0 = off on every engine class, so the `@stable` twins are byte-identical). **Exclusion, not de-prioritisation** — a weight loses exactly when the bot most wants bodies, which is when the army is starving. `PoiOffensiveBotModule.PruneAxes` also drops a unit that runs dry *mid*-axis, so the gate is not limited to platoons that were already starving when the axis formed. Note the correction to the analysis above: `[exp-poi] disperse` is a **diagnostic**, not a mover (`LayeredDefenceBotModule.LogPoiDispersionDiagnostic`) — the clump-radius growth it reported was caused by the reserve-spread pass in the same module, plus the offense axes and the ambush lane.

## ~~2026-08-09: [med] Eight locomotors declare `Crushes: fence` … so the pathfinder treats 384 placed fence/wire actors as solid walls for every vehicle~~ — **NOT A BUG, premise inverted (retracted 2026-08-19, curation pass)**

**`Info.PassableClasses` is not "the `Passes:` field" — it is `Passes.Union(Crushes)`, computed in `LocomotorInfo.RulesetLoaded` (`Locomotor.cs:110`).** So the crush classes below **are** pass classes: `IsBlockedBy` hands the union to `passable.PassableBy(...)` (`:433-441`), `Passable.PassableInner` tests `Info.PassClasses.Overlaps(passClasses)` (`Passable.cs:162`) and lets the vehicle in, and `Passable.OnBeingPassed` then re-tests `Crushes` separately before firing `OnBeingCrushed` (`Passable.cs:92-96`). Vehicles drive through and crush the fence/wire exactly as the YAML reads. Nothing below needs fixing, and applying either of the "two possible fixes" would have been a change to working configuration.

The report cited a real line that does not do the work it was credited with — README §Four-shapes #3. The durable half is now banked at [`DOCS/reference/conventions.md`](../../DOCS/reference/conventions.md) §Engine behaviors that surprise. **The third-order consequence at the foot of this entry is also void**: the diagonal-squeeze rule's connectivity-neutrality is *not* explained by "no vehicle could enter the `barb` cells anyway", so if that neutrality still matters, re-derive it.

*(Original text preserved below for the record.)*

`Locomotor.IsBlockedBy` decides enterability from `Info.PassableClasses` — the `Passes:` field — and never reads `Crushes:` (`engine/OpenRA.Mods.Common/Traits/World/Locomotor.cs:434-444`). `Crushes` is consumed only after entry, by `Passable`/`INotifyBeingPassed` (`Traits/Passable.cs`). So a crush class with no matching pass class can never fire: the unit is refused the cell, and therefore never crushes what is in it.

Every ww3mod vehicle locomotor declares `Passes: field` and nothing else (`mods/ww3mod/rules/world.yaml:88-196`), while listing crush classes it does not pass:

| locomotor | `Crushes` classes absent from `Passes`, that exist on maps |
|---|---|
| `heavytracked` | `barbedwire`, `fence` |
| `wheeled`, `heavywheeled`, `lighttracked`, `lighttracked-amphibious`, `tracked`, `tracked-amphibious`, `walker` | `fence` |

Placed on the ten shipped maps: **310 `fence`-class actors** (`fenc`, `wood` — 130 on x-lake, 67 on siberian-pass) and **74 `barbedwire`** (`barb` — 54 on river-zeta). All are immobile, so all are hard walls to every vehicle as far as pathing is concerned. `sandbag` is listed by four locomotors too but no sandbag actor is currently placed on any map.

Whether this is a bug depends on intent, which is why it is here rather than fixed: "wire stops vehicles until it is shot" is a defensible design, but then `Crushes: fence`/`barbedwire` is dead configuration that reads as if the opposite were true — `heavytracked` in particular looks deliberately specified as the one thing that can drive through wire, and cannot. Two possible fixes: add the classes to `Passes` (vehicles drive through and crush, which is what the `Crushes` list implies), or delete them from `Crushes` (so the yaml stops claiming a behaviour that cannot occur).

Third-order consequence, already load-bearing: this is *why* the shipped diagonal-squeeze rule (`be036370`) measures as connectivity-neutral. All 13 corner-to-corner tank-trap gaps on river-zeta are plugged with `barb`, so the rule denies steps between cells no vehicle could enter anyway. If `barbedwire` is ever added to a `Passes` list, re-run `./tools/nav-guard/nav_guard.py check` — the squeeze rule starts biting the same day.

## 2026-08-09: [med] A tree becoming a husk both loses `Passable: tree` and, for six tree types, occupies MORE cells than the tree did — so burning woodland silently closes ground to infantry (found while: building `tools/nav-guard/`, static analysis only)

Two independent changes happen at `SpawnActorOnDeath`, and neither is visible in any single yaml file:

1. **`^Tree` carries `Passable: PassClasses: tree` (`rules/ingame/decoration.yaml:12-14`); `^TreeHusk` carries no `Passable` trait at all (`rules/husks/husks.yaml:91-108`).** Foot locomotors list `tree` under `Passes`, so infantry walk through live trees and are hard-blocked by husks. Every tree on every map is a cell infantry lose on death.
2. **Six husks occupy a larger or differently-placed footprint than the tree they replace.** Verified in yaml, not inferred: `T14` `___ _x_` → `T14.Husk` `___ xx_` (1 → 2 blocking cells); same for `T15`. `TC02` `==_ x=_` → `_x_ xx_` (1 → 3, since `=` is `OccupiedPassable` and pathable). `TC03` 3 → 4, `TC05` 5 → 6, and `TC04` keeps 4 cells but moves one.

Measured with `nav_guard.py --state dead` (every destructible map actor replaced by its husk), against the authored state: **154 of 190 map/locomotor pairs lose reachable ground**, worst cases

| map / locomotor | largest region | pocketed cells |
|---|---|---|
| woodland-warfare / `foot-mountainer` | 9534 → 7042 (−2492, −26%) | 0 → 79 |
| woodland-warfare / `foot` | 8557 → 6197 (−2360) | 69 → 118 |
| river-zeta / `foot` | 7205 → 4929 (−2276) | **0 → 390** |

River-zeta is the sharpest: infantry currently have a single fully-connected region, and once the treeline burns 390 cells are stranded in pockets. This is an all-or-nothing worst case (it assumes every map actor dies) so the real in-match figure is smaller, but the direction is one-way and the effect is invisible while playtesting a fresh map.

Item 1 may well be intended — a burnt stump being solid where a leafy tree was passable is arguable. Item 2 is much harder to defend: a husk cannot plausibly be *wider* than the tree, and the 1→3 growth on `TC02` looks like footprint art copied without matching it to the source actor. `nav-guard`'s `check` reports the all-husks state as an advisory (exit 1) rather than a hard failure for exactly this reason — it is a real signal, but not one where every delta is a defect.

## 2026-08-09: [low] Seven of ten `map.png` previews are stale: their pixel size predates a hand-edit of `Bounds:` in `map.yaml`, and they carry actors the map no longer places (found while: building `tools/nav-guard/`, static analysis only)

`./tools/nav-guard/nav_guard.py validate` renders decoded terrain and diffs it against each checked-in preview. Terrain agreement is 100.00% on all ten maps, so the decode is sound; the disagreements are all in the preview.

- **Six maps** (`nuclear-winter`, `polar-disorder`, `seventh-woods`, `siberian-pass`, `twin-rivers`, `x-lake`) ship a preview 2 cells smaller than `MapSize` in each axis, aligning at offset `1,1`. `shellmap-open-field` ships a full-`MapSize` preview against a current `Bounds: 1,1,90,60`. In both directions the image was generated under a different `Bounds` than `map.yaml` now declares, i.e. `Bounds:` was edited by hand without re-saving through the editor. `twin-rivers` is the worst: a 112×112 preview against `MapSize: 128,128`, so ~23% of the map has no preview coverage at all.
- Consequently those previews also predate later actor edits — 42 to 593 pixels per map show an actor on one side and not the other.
- **River-zeta's preview is bounds-current but still carries 6 dead pixels**: the seven `t14`/`t15` trees removed in `0fa152f1` (footprint offset `(1,1)`, so `(68,y)` paints `(69,y+1)`). That commit touched `map.png`, so the regeneration in it did not take.
- All ten maps still carry RA-era ore in the `map.bin` resource plane (`twin-rivers` 38 cells, `x-lake` 24, `seventh-woods` 8, `siberian-pass` 1). ww3mod has no resource layer, so it is inert for gameplay, but the older previews were saved while a `ResourceRenderer` was still painting it.

Cosmetic — the lobby map thumbnail is wrong, nothing else reads `map.png`. Fix is a `--refresh-map` pass per map (`RefreshMapCommand`), which would also normalise `Bounds`. Worth doing before anyone uses the previews as evidence for anything; `nav-guard validate`'s `align` column will read `bounds` for all ten once it is done.

## 2026-08-09: [med, doc-in-doc, FIXED] `DOCS/bots/04-perception-and-fields.md` §3.2's danger table is unreachable for its heavy-weapon rows, and states the ranking inversion backwards (found while: consolidating the bot-doc set into `DOCS/bots/06-inherited-misfits.md`, static read only)

**FIXED at `main @ af36e686` (documentation pass, narrowing the danger quarantine).** `04` §3.2 was re-derived from the merged `DangerFieldLayer.SustainedThroughput` (`:794-831`) and `DangerKernelMath.Compute` (`:154-187`), and now publishes reachable figures — `abrams` **521,914**, `bmp2` **184,966**, `AR` **7,820**, `AT` **7,560**, `e3` **2,237** — with the ranking stated the right way round (**armour above infantry**). The old inverted headline is retained in place as an explicit correction rather than deleted. The three test-pinned rows were re-derived a third time in that pass and match `DangerFieldKernelTest`.

**One directive in this entry turned out wrong and is retracted:** *"Do not mark the `[high]` entry fixed when that branch merges."* That was correct for `auto/danger-scale` as it stood at `3a7a10a3`, but the branch went back and fixed the cadence defect before merging (`1092573d`, *"fix the weapon fire-cycle model first, then re-derive the danger unit on top of it"*) — so the `[high]` entry below **is** legitimately closed, and its own FIXED note is right. The reasoning here was sound; only its premise about that branch's final scope was stale.

Doc 04 §3.2 publishes `intensity = 67,850,000` and an outermost-ring value of `2,423,214` for a believed `abrams`, and rests its headline on the second number. That is the value the formula yields in **exact** arithmetic. `DangerKernelMath.Compute` evaluates `throughput * durabilityWeight / DurabilityBase * confidencePercent / 100` (`Traits/World/DangerFieldLayer.cs:170`) entirely in `int`, left to right, and `2,300,000 × 2,950 ≈ 6.8e9` exceeds `int.MaxValue`. It wraps negative, falls through the `if (intensity < 1)` guard at `:171-172`, and is clamped to the FLOOR OF 1. **A believed main battle tank paints exactly one cell, at value 1.** Verified at `main @ dcc2f7c5`: `DangerKernelFacts` (`:55-65`) and `DangerKernelParams` (`:82-96`) are all `int`.

**Only the Abrams row overflows.** Rifleman `162,626`, BMP2 `2,197,440` and ATGM `151,200,000` all compute below the wrap, so their published intensities stand. The affected row is the one the headline uses.

**The direction matters and doc 04 has it inverted.** Doc 04 §3.2(a) concludes the field over-ranks heavy weapons — "an Abrams is 3,100× more dangerous than a BMP2, when the true ratio is under 2×" — and the `[high]` entry below reproduces those ratios. As executed the Abrams reads **1** and the BMP2 reads **21,974**, so the field ranks the **BMP2 ≈22,000× above the Abrams**. Anyone using that table or those ratios to pick a threshold or predict a behaviour gets the sign wrong. Doc 04's §5 threshold verdicts are unaffected (the thresholds are unjustifiable either way); its §3.2 ratios are not sound for the heavy-vehicle class.

**Second half of this entry, and the operationally important one: `auto/danger-scale` does NOT close the `[high]` entry below.** That branch (`3a7a10a3`, under review) fixes the overflow at three sites with `long` + saturation, and adds a derived `ReferenceIntensity` unit with 13 renamed thresholds — but its diff leaves `WeaponThroughput` (`:521-533`) untouched, acknowledging the `ReloadDelay`/`BurstWait` defect in a new comment rather than fixing it. **No unit denominator can correct a ranking inversion**, because the inversion is in the ordering of contacts, not the scale of the answer. Do not mark the `[high]` entry fixed when that branch merges.

One qualification to that entry's stated sequencing ("fix the formula first, or the retune is fitted to the broken field"): correct for a hand-tuned retune, but `ReferenceIntensity` is a median over the ruleset's own types and so re-derives itself when throughput changes — that is the branch's explicit design claim and it holds. The thresholds survive a later throughput fix; the **benchmark baseline** does not. Landing them separately costs two baseline re-takes instead of one.

Not fixed here (documentation/audit task). Fix shape for the doc half: re-derive doc 04 §3.2's heavy rows against the executed arithmetic, and re-state §3.2(a)'s ratios once `WeaponThroughput` is fixed rather than before. Full argument: `DOCS/bots/06-inherited-misfits.md` §5.1.

## 2026-08-09: [med, FIXED on auto/danger-scale] `DangerFieldKernelTest` pins the danger kernel at Red Alert magnitudes with ordinal-only assertions, and the ruleset→field conversion function has zero coverage — this is *why* the overflow above and the `[high]` cadence bug below both survived a green suite (found while: writing `DOCS/bots/README.md`, static read only)
Companion to the `[med, doc-in-doc]` overflow entry at the top of this file: that one establishes the arithmetic is wrong at this mod's magnitudes, this one establishes why nobody noticed for the feature's whole life. Three gaps compound.

**FIXED on `auto/danger-scale` (commit d1591b35, rebased 1092573d).** The fixtures no longer hard-code magnitudes at all: they transcribe real weapon PARAMETERS from `mods/ww3mod/rules/weapons/*.yaml` and compute throughput through `DangerFieldLayer.SustainedThroughput` itself, so a cadence regression moves every dependent assertion instead of being ratified by them. `SustainedThroughput` was split out public precisely so the cycle model is pinnable (a warhead list is not constructible in NUnit — which is how this went unexamined). The conversion function now has direct coverage: `ReferenceIntensity` (median/order-independence/non-contributing types), `DangerUnitsToField` (sentinels, no-reference fail-closed, clamp), and the saturating clamp in `Compute`. This entry's diagnosis was exactly right and is left in full — it is the reason the branch stopped trusting its own green suite.

**(1) The fixtures are RA-scale.** `DangerFieldKernelTest.cs:68-71` declares four synthetic `DangerKernelFacts` as "representative ground-domain facts": `Sniper` (throughput 30, health 100, cost 300), `Humvee` (300 / 300 / 600), `Tank` (400 / 1,000 / 1,500), `Truck` (0 / 250 / 400). The mod's real `abrams` computes throughput **2,300,000**, health **28,000**, cost 2,500. The test's tank is ~5,750× less lethal and 28× less durable than the real one — these are Red Alert numbers.

**(2) No test can reach the overflow regime.** The wrap is in `throughput * durabilityWeight` (`DangerFieldLayer.cs:170`). For the test's `Tank`: weight = `100 + 1000/10 + 1500/50` = 230, product = **92,000**. For the real `abrams`: weight `100 + 28000/10 + 2500/50` = 2,950, product = **6,785,000,000**, against `int.MaxValue` = 2,147,483,647 — so the real value is **3.16× over** the limit while **the fixture sits ~23,300× under it**. The defect is unreachable from the suite by construction — not missed, unreachable.

**(3) Every intensity assertion is ordinal or relative; none is absolute.** The complete set in the file: `humvee > sniper` (`:86`), `tank >= humvee` (`:96`), `== 0` for an unarmed truck (`:109`), `half < full` (`:144`), `half == full/2 ± 1` (`:147`). Orderings and ratios are scale-invariant by construction, so they hold identically whether the field spans 0–1,000 or 0–10⁸ — while every consumer threshold sits between 0 and 120 (`DOCS/bots/04` §5: 14 of 26 unjustifiable). A suite that only pins ordering cannot fail on a scale problem. Note the ordinal pins would also have stayed green *through the wrap* if the fixtures had been realistic-but-below-threshold, since `tank >= humvee` is exactly the assertion the wrap inverts.

**(4) `WeaponThroughput` and `ExtractKernelFacts` are never called by any test.** Grepped across `engine/`: the only references are `DangerFieldLayer.cs:287`, `:496` and the definitions at `:482`, `:521`. The tests inject `groundThroughput` as a hand-written integer, bypassing the one function that translates the ruleset into the field's units — the function carrying the `[high]` `ReloadDelay`/`BurstWait` inversion. **The bug lives in the untested seam between the ruleset and the well-tested pure math.**

**Why file it rather than shrug.** The kernel *reads as verified* — dedicated NUnit file, sensible-looking cases — which is precisely why nobody re-derived its scale. Same family as the already-recorded slider that was "live, documented, correctly clamped and NUnit-pinned at its endpoints" and still identical at three of five points of the parked sweep grid (`DISCOVERIES.md:205-207`), whose rule was *pin the value at every planned measurement point, not at the extremes*. The equivalent rule here: **pin at real ruleset magnitudes and assert at least one absolute value, or the pins cannot see a rescale — and cannot see an overflow either.**

Fix shape (NOT applied — documentation pass): (a) add a fixture built from the real `abrams`/`AR`/`ATGM` numbers and assert absolute intensity and per-cell step, so any future weapon or HP rescale breaks a test instead of silently moving the field; (b) add a case at the `int` boundary specifically, which would have caught the wrap; (c) give `WeaponThroughput` direct cases over the four cadence shapes (`BurstWait` only, `ReloadDelay` only, both, neither) — note (c) **will fail on current code**, which is the point, so write it alongside the `[high]` fix rather than before it. Worth checking whether `auto/danger-scale`'s `long`+saturation fix added coverage of (b); if it did not, the same class can recur at the saturation boundary.

## 2026-08-09: [low, doc-in-doc] Three inaccuracies in `DOCS/bots/03-module-catalogue.md` and `05-squads-and-combat-states.md`, found by cross-checking the set against itself (found while: writing `DOCS/bots/README.md`, static read only)
Recorded, not edited — several doc workers share this checkout and those files may still be open. All three verified at `main @ dcc2f7c5`.

1. **`03` §3-B3 gives the fixed-wing squad cadence as "Squad FSM update 5 t"; it is 75 t — wrong by 15×.** `Squad.Update()` is called inside `if (--attackForceTicks <= 0)` (`SquadManagerBotModule.cs:274-279`), reset from `Info.AttackForceInterval`, default **75** (`:72`), and **not overridden anywhere in `ai.yaml`** (grepped: the only interval overrides on those four blocks are `RushInterval: 600`). The 5-tick figure is `HelicopterSquadBotModule.SquadUpdateInterval` — a different module. `05` §3 has it right (75 t = 4.5 s). A reader of `03` alone would believe the fixed-wing air squad reacts fifteen times faster than it does.
2. **`05` §0 headlines "Eight of them run. Twelve are unreachable"; on shipped profiles it is 7 and 13.** `HelicopterAttackRunState` is entered from exactly one site, inside `if (!standoff)` (`HelicopterStates.cs:565-573`), and `StandoffEngagement: true` ships on **both** profiles (`ai.yaml:1419`, `:1446`). `05`'s own reachability table marks it `LIVE*` with that caveat and its §3.2/§7.4 argue it never executes — so the headline count contradicts the body. It matters because that state carries `HitAndRunCooldown`, the helicopter trait's most doctrine-flavoured knob (also `06` §1 rank 14).
3. **`03` §E2 claims the two claim registries are "honoured by disjoint sets of modules"; they overlap in at least two.** `HelicopterSquadBotModule.cs:496` resolves `PoiGoalGuard` **unconditionally** — its own comment calls it an availability gate "for every profile" — `GarrisonBotModule.cs:220` resolves it behind `CommitGarrisonedUnits`, and `UnitBuilderBotModule.cs:276` resolves it too; all three are wave-1/support-layer modules that also use the blackboard. `02` §4.2 states this correctly. The narrower claim that *does* survive, and the one to quote: **the POI stack never reads the blackboard** — zero `BotBlackboard` references across `PoiOffensiveBotModule`, `PoiGarrisonBotModule`, `LaneAmbushBotModule`, `LayeredDefenceBotModule`, `MountedTransportBotModule`, `CaptureCoordinatorBotModule` and `PoiGoalGuard` (verified) — and nothing reads both registries as one source of truth. `DOCS/bots/README.md` §7 carries all three with the corrected versions.

## 2026-08-09: [med] `AdaptiveProductionBotModule`'s counter-buy lane is gated on stale, never-decaying blackboard intel while its own fog-legal scanner sits unread behind the gate (found while: writing `DOCS/bots/03-module-catalogue.md`, static read only)
`IBotTick.BotTick` reads three counters from the blackboard — `enemy-vehicles-sighted`, `enemy-infantry-sighted`, `enemy-buildings-sighted` (`AdaptiveProductionBotModule.cs:227-229`) — sums the first two into `totalSightings` (`:231`) and **early-returns** when `totalSightings < Info.MinEnemySightings` (shipped 3, `ai.yaml:939`) at `:255-256`. Only *after* that gate does it run `ScanEnemyComposition()` (`:259`), which is a correct, fog-legal (`CanBeViewedByPlayer`, `:607`), whole-map census. So the accurate sensor can never open the gate that guards it.

**Why the gating source is unreliable.** The counters are written only by `ScoutBotModule.ReportEnemySightings` (`:237-291`), and three properties compound:
1. **Overwrite, not accumulate.** `PostIntel` is `intel[key] = value` (`BotBlackboard.cs:246`) and the reporter loops over scouts writing each scout's own tally (`ScoutBotModule.cs:287-289`). The stored value is whatever the *last* scout in the iteration saw — not a sum across scouts, and not a map total.
2. **No decay and no timestamp check.** Nothing ever clears or ages the intel dictionary (`BotBlackboard.cs:244-262`; the `CleanupInterval`/`TaskStaleTicks` sweep at `:100-108` calls `CleanupStaleTasks` + `CleanupDeadUnitClaims`, neither of which touches `intel`). `last-scout-tick` is posted (`ScoutBotModule.cs:290`) but no consumer reads it. If both scouts die the last numbers stand for the rest of the match — stuck open if they were ≥3, stuck **shut** if they were not.
3. **Tiny, fragile sample.** `MaxScouts: 2` with a single `ScoutTypes` entry (`humvee` / `btr`, `ai.yaml:741`/`:749`) counted within `world.FindActorsInCircle(scout.CenterPosition, ScoutVisionRadius: 8)` (`ScoutBotModule.cs:244`) every `ScanInterval: 200` (12.0 s). The bot must have a live, idle scout standing within 8 cells of ≥3 enemy ground units at the instant of a scan.

**Blast radius.** All four instances tick this path. `@experimental` has two independent lanes ahead of the gate that read `BeliefStore` and bypass `MinEnemySightings` (`SupplyRouteDefenseEnabled` `:241-242`, `CompositionNeedEnabled` `:249-250`), so it degrades rather than stops. **`@stable` has neither** (both flags are absent from `ai.yaml:1733-1769`), so on the benchmark control the entire reactive counter-buy system is downstream of two humvees. Note the scan is also not visibility-filtered — it tests only `RelationshipWith` (`ScoutBotModule.cs:257-258`), so it counts cloaked/shrouded actors inside the radius, unlike `ScanEnemyComposition`.

**Fix shape (not applied — behavioural, and it moves `@stable`).** Cheapest correct change is to compute `ScanEnemyComposition()` *before* the gate and test `totalSightings` against the max of the two sources; the module already does exactly that `Math.Max` merge one line later (`:260-261`), just too late to matter. Failing that, accumulate rather than overwrite in the reporter and expire the intel against `last-scout-tick`. Either way, re-take the ai-bench baseline: `@stable` behaviour changes.

## 2026-08-09: [high, FIXED on auto/danger-scale] `DangerFieldLayer.WeaponThroughput` divides by `ReloadDelay`, which most WW3MOD weapons do not declare — so the danger field ranks weapons by which cadence field their YAML happens to use (found while: writing `DOCS/bots/04-perception-and-fields.md`, static read only)
`WeaponThroughput` (`Traits/World/DangerFieldLayer.cs:521-533`) computes `burstDamage × Burst × ThroughputWindow / ReloadDelay` and substitutes **1** when `ReloadDelay ≤ 0` (`:532`). It never reads `BurstWait`. But WW3MOD replaced the RA firing model: **`BurstWait` is mandatory** — `Armament.cs:128-129` throws a `YamlException` for any weapon that omits it — and is the real inter-burst cadence (`Armament.UpdateBurst`, `:626-647`), while `ReloadDelay` is now only the extra pause after a whole `Magazine` is spent and is applied **only when non-zero** (`Armament.UpdateMagazine`, `:610`). Across `mods/ww3mod/rules/weapons/` there are **14 `ReloadDelay` declarations against 90 `BurstWait` declarations**, so most weapons take the `→ 1` substitution and are modelled as firing their entire burst damage every tick.

**FIXED on `auto/danger-scale` (commit d1591b35, rebased 1092573d).** `WeaponThroughput` now derives the real fire cycle from `Armament`: `CanFire` blocks on `IsReloading || IsWaitingBurst` (`:327`) and both counters tick in parallel (`:283-287`), so the cycle is the MAX of the two; `Magazine` counts SHOTS (`UpdateMagazine` runs per shot at `:380`), so a magazine swap is amortised rather than paid per burst. This entry's call — that the field ranks weapons by which cadence field their YAML uses — was correct, and the resulting error is TWO-SIDED: `BurstWait`-only weapons were over-stated by their whole cycle (`TankRound.Abrams` 2,300,000 vs a true 17,692, ~130x) while `ReloadDelay`+`Magazine` weapons were UNDER-stated (`5.56mm.AR` 1,333 vs ~6,410, ~4.8x). Verified across the full ruleset by inheritance resolution, not adjacency: no `^template` declares `Magazine` or `ReloadDelay` at all, 14/14 declared and resolved, zero weapons with a live `ReloadDelay` and `Magazine <= 1`.

Measured against the weapons' actual sustained output over a full magazine cycle: `5.56mm.AR` reads **4.5× under** (1,333 vs ≈6,060 per 100 ticks), `30mm.BMP2` **7.8× under** (1,440 vs ≈11,250), `ATGM` **200× over** (1,200,000 vs ≈6,000), `TankRound.Abrams` **130× over** (2,300,000 vs ≈17,692). **This is a ranking inversion, not a scale offset:** the field believes an AT specialist is ~900× more dangerous than a light machine gunner (true ratio ≈1:1) and an Abrams 3,100× a BMP2 (true ratio <2×). Every consumer that buckets, sorts or thresholds `GroundDanger`/`AirDanger` is therefore dominated by YAML style rather than by threat. Full worked table with citations: `DOCS/bots/04-perception-and-fields.md` §3.2.

NOT fixed here (documentation pass, and a worker is concurrently retuning consumer thresholds on `auto/danger-scale`). **Fix shape, and note it is not a one-liner:** throughput must be derived from the cycle `Armament` actually runs — `Magazine` shots per (`Magazine/Burst` bursts × (`Burst`×`BurstDelays` + `BurstWait`) + `ReloadDelay`) — which changes every cell value in the field and therefore invalidates every threshold tuned against it, including any set on `auto/danger-scale`. Sequence matters: fix the formula first, then re-derive thresholds against the resulting scale, or the retune is fitted to the broken field. The sustained-output figures above depend on my reading of the firing cycle; the fact that `WeaponThroughput` never reads `BurstWait` needs no model and is direct at `:521-533`.

## 2026-08-09: [med] `DangerFieldLayer`'s durability weight is scaled for RA hit points, so the danger field is dominated by HP rather than lethality (found while: writing `DOCS/bots/04-perception-and-fields.md`, static read only)
`DangerKernelMath.Compute` (`:169`) builds `durabilityWeight = DurabilityBase + HP / HealthDivisor + Cost / CostDivisor` with `DurabilityBase = 100` (`:206`), `HealthDivisor = 10` (`:209`), `CostDivisor = 50` (`:212`). The in-code `[Desc]`/comment at `:167-168` and `:205` describes the result as "~1.0x (DurabilityBase) for a fragile, cheap unit, rising with health and cost" — which is exactly right for an RA unit at 100–400 HP (an infantryman at 200 HP gets 1.22×).

WW3MOD base HP is `200` for infantry (`ingame/infantry.yaml:33`), `10,000` for vehicles (`ingame/vehicles.yaml:20`) and up to `75,000` for structures (`ingame/structures.yaml:252`). So an `abrams` at 28,000 HP / 2,500 cost gets `100 + 2,800 + 50 = 2,950` — a **29.5×** multiplier — and a large structure gets **751×**. The `HP/10` term alone is 14–750× the base it is supposed to be a small correction to. Consequence: a heavily-armoured low-lethality actor outweighs a lethal fragile one by more than any weapon term can offset, and the multiplier compounds the `[high]` throughput bug above. Comment-and-constants only; no behavioural fix attempted. Fix shape: raise `HealthDivisor`/`CostDivisor` to WW3MOD scale (or normalise HP against a mod-wide reference) so the weight lands back in its documented ~1–3× band, and correct the `[Desc]` either way.

## 2026-08-09: [low, doc-in-code, FIXED on auto/danger-scale] `ai.yaml:840-841` states the danger field "steps by tens-to-hundreds per cell near a contact" — true only for small arms (found while: writing `DOCS/bots/04-perception-and-fields.md`)
The `@supply` evac comment block reasons correctly that `EvacReleaseHysteresis: 15` (`:849`) is sub-cell and therefore decorative, but justifies it with a per-cell step of "tens-to-hundreds". The step is `intensity / (radius + 1)` (`DangerFieldLayer.cs:366`): **95** for a rifleman (`5.56mm.AR` on `AR`), **998** for a `bmp2`, **65,739** for an `AT` specialist, **2,423,214** for an `abrams`. The claim holds for the calibration weapon the author used and is wrong by 10²–10⁴ for the mod's heavy weapons. It matters because the same sentence is the tree's most-quoted statement of this field's scale, and `WORKSPACE/recon/260809-truck-loop-from-live-log.md` §6 already flagged it as wrong by three orders of magnitude against the live log. Comment-only; not edited here because `auto/danger-scale` is live in that block.

**FIXED on `auto/danger-scale` (commit 3a7a10a3, rebased 6fc1cfff).** That comment block was rewritten: it no longer claims a tens-to-hundreds per-cell step, the hysteresis is no longer described as decorative (expressed in danger units the band spans several cells of travel), and the knob it documents was renamed `EvacReleaseHysteresisUnits`. The block now also flags its own values as PROVISIONAL, since the only measured distribution predates the cadence fix above and describes a field where every heavy contact stamped a clamped 1.

## 2026-08-09: [low, doc-in-doc] `DOCS/reference/influence-stack.md` carries stale `ControlField.cs` line references (found while: writing `DOCS/bots/04-perception-and-fields.md`)
The curated stack reference cites `CellSize` at `ControlField.cs:177`, `SeedStrength` `:184`, `MaxScore` `:187`, `PresenceGain` `:190`, `GrayBand` `:205` and `AnchorStrength` `:211`. At `main @ 910507c1` those constants are at `:389`, `:396`, `:399`, `:402`, `:417` and `:420` — the whole `ControlFieldInfo` block moved when `FrontlineProfileMath` was inserted ahead of it. `influence-stack.md:105` likewise cites `GarrisonBotModule:102` for the explicit `ExperimentalBotType` conjunct; it is at `:219`. Values are all still correct — only the line anchors rotted. Not edited: three doc workers share this checkout right now and `DOCS/reference/` is curated. Fix during the next curation pass; the verified line numbers are tabulated in `DOCS/bots/04-perception-and-fields.md` §4.1.

## 2026-08-09: [med] `GoalGuardLedger.Release` is keyed on the ACTOR, not on the objective — an ambient `tacpos:` claim can silently delete a `capture-escort:` one (found while: writing `DOCS/bots/02-lifecycle-and-arbitration.md`, static read only)
`GoalGuardLedger<TKey>.Release(TKey unit) => commitments.Remove(unit)` (`PoiGoalGuard.cs:100`). The ledger stores at most one commitment per unit, so a caller that means "release **my** claim" actually deletes **whichever** claim the actor happens to hold at that moment. The symmetric hazard is already on the record — `Commit` with a *different* objective overwrites the incumbent entry outright (`:68-76`), acknowledged in `OrderArbitrationMath.cs:21-27` — but the `Release` half is acknowledged nowhere.

**Why it is reachable rather than theoretical.** `StancePositioningExecutor` is a per-*unit* trait on every `^Combatant` that writes a `tacpos:` claim (`:643`, `ClaimTicks` 150, re-committed every 30 ticks) and calls `ReleaseManagement()` → `Ledger.Release(self)` from five separate sites (`:229`, `:261`, `:272`, `:300`, `:320`). It never reads the ledger, so it cannot tell whether the claim it is about to delete is its own. A unit that is `capture-escort:`-committed by `CaptureCoordinatorBotModule` and then passes through any of those five paths loses the mission claim, and the coordinator is never told.

**The new order gate does not cover this.** Its rank ladder (`OrderArbitrationMath.cs:169-181`) exists precisely so an ambient `tacpos:` claim loses to real tasking — but rank is consulted at the order funnel, never at `Release`. The deletion happens before any order is issued, so there is nothing for the gate to arbitrate.

Fix shape (NOT applied — documentation task, no code changes): give `Release` an objective (or prefix) argument and remove only on match; audit the call sites. Cheap, but it changes ledger behaviour on both profiles, so it wants a benchmark rather than a drive-by.

**Two adjacent, lower-severity notes from the same pass** (also in that doc's §6.2, not filed separately): (1) `BotBlackboard`'s entire task-board API — `PostTask` `:137`, `ClaimTask` `:145`, `UpdateTaskStatus` `:160`, `GetOpenTasks` `:170`, `HasTaskNear` `:184` — has **zero callers anywhere in `engine/` outside `BotBlackboard.cs`**: a half-built second coordination system sitting next to a live one. (2) The order gate's objective-prefix → module rank table (`OrderArbitrationMath.cs:206-226`) is hand-maintained and is not re-read from the modules that emit those prefixes, so it can drift silently; it fails open so drift costs damping and not correctness, but nothing reports it and a `make test` lint would close it.
## 2026-08-09: [med] `TransportDropSiteMath.ScoreDrop` sums three incommensurable scales, so the heli drop-site picker is a pure danger-argmin (found while: danger-unit audit, `auto/danger-scale`) — deliberately NOT fixed
**UPDATE 2026-08-09 (same branch): the SIGN half of this is now fixed — the weight-balance half is not.** `ScoreDrop` accumulated in `int`, so `danger * dangerWeight` wrapped for a raw reading above ~2.1e7 and, since every term is a penalty subtracted from zero, the wrapped term became a BONUS: the picker actively preferred the most dangerous candidate it sampled. It now accumulates in `long` and is pinned by `TransportDropSiteMathTest.MoreDangerousNeverScoresBetter_AtRealFieldMagnitudes`. The scale-mixing below still stands.

`HelicopterSquadBotModule.cs:1805-1817` builds one additive score from three terms whose natural magnitudes differ by 3–5 orders: `enemyDepth` is a **ControlField** score bounded by `MaxScore = 1000`; `danger` is `GroundDanger + AirDanger`, i.e. **10⁴–10⁷** on the raw field scale; `reach` is a **cell count**, ~0–100. The weights all ship at parity (`DropEnemyControlWeight: 100`, `DropDangerWeight: 100`, `DropReachWeight: 5` — `ai.yaml:1523-1525`, live under `RiskWeightedDropSite: true`). At those weights the danger term outweighs control by ~10³–10⁵ and reach by ~10³–10⁶, so **the control-depth and reachability terms are numerically invisible and the "risk-weighted" drop site is simply whichever candidate reads lowest danger**, regardless of how deep behind the enemy SR it sits — the exact failure the lever was introduced to prevent.

Same root cause as the threshold bug fixed on this branch (a constant tuned at one scale meeting a total-conversion-scale field), but it is a **weight-balance** problem rather than a threshold one, and the correction is not derivable statically: it needs a measured relative importance between "how deep in enemy territory" and "how dangerous", which is a doctrine question. Not guessed at here. Fix shape: convert the danger term to danger units before weighting, which puts all three terms in a comparable band and makes the shipped weights mean what they read as.

## 2026-08-09: [low] `ForwardStagingMath.StagingCell` still has three callers with no passability predicate (found while: drop-anchor stall fix, `auto/danger-scale`) — partially fixed
The 24-scan drop-anchor stall was fixed by giving the descent a `passable` predicate so it cannot return a cell its caller is obliged to reject (see DISCOVERIES 2026-08-09). The parameter is optional and defaults to null, and only `SupplyFollowerBotModule.ResolveDropAnchor` — the site with the observed outage, and the only one with a mover handle already in scope — passes one. The other three (`PoiOffensiveBotModule.ResolveStagingAnchor` `:1959`, `.ResolveMusterAnchor` `:2153`, `CaptureCoordinatorBotModule.ResolveReserveAnchor` `:1199`) still walk unguarded and can terminate on water or cliff.

The consequence differs from the drop case: `ResolveStagingAnchor` does **not** re-test passability, so instead of a rejected anchor it issues an `AttackMove` to an unreachable cell — and per DISCOVERIES 2026-08-08 ("an unreachable destination does NOT fail loudly"), the pathfinder returns no path, `Move` treats that as arrival, and the units simply stop. Left alone deliberately: those sites need a representative mover threaded in to bind a locomotor, and `ResolveMusterAnchor`'s self-seed logic enumerates exactly three reasons the walk can return its seed — its correctness argument depends on that enumeration, so adding a fourth is a behaviour change to a shipped `@experimental` lever that wants its own measurement, not a drive-by on a truck branch.

## 2026-08-08: [med] `CheckOwnershipAfterExit` has no "was this originally neutral" guard, so a PLAYER-owned garrisonable building reverts to Neutral when its last soldier leaves (found while: unit-purpose review, `auto/unit-purpose`) — PRE-EXISTING, deliberately not fixed there
`GarrisonManager.CheckOwnershipAfterExit` (`Garrison/GarrisonManager.cs:300`) reverts the building to Neutral whenever `remainingOwners.Count == 0`. It never asks what the building's ownership was *before* anyone garrisoned it, so the revert is unconditional across every `DynamicOwnership` holder — not just the neutral civilian houses the mechanic was written for. `GTWR`, `PBOX` and `HBOX` all carry `GarrisonManager` with `DynamicOwnership` defaulting true and unoverridden, so **a player-owned bunker hands itself to Neutral the moment its garrison walks out**, via the long-standing `OnPassengerExited` path (`:293`) — this is not new behaviour and not reachable only through the bot.

Masked today because those three carry `Prerequisites: ~disabled` and are not buildable in normal play, so no player-owned instance normally exists; a **map-placed** player-owned one would hit it immediately. Deliberately NOT fixed on `auto/unit-purpose`: that branch's FIX-3 makes the port-only exit path *reach* `CheckOwnershipAfterExit` (it previously did not), but the missing-guard defect is independent of it, is older, and belongs to whoever owns the garrison-ownership model — fixing it here would mean inventing an "original owner" concept inside an AI bug-fix branch. Fix shape: record the pre-garrison owner on first entry and revert to that rather than unconditionally to Neutral.

## 2026-08-08: [low] `GarrisonBotModule.baseCenter` is sampled once and frozen for the match (found while: unit-purpose garrison gate, `auto/unit-purpose`)
`Initialize()` sets `baseCenter = bases.Random(world.LocalRandom).Location` (`:114-120`) behind an `if (initialized) return;` guard, and `MaxGarrisonRadius` (25) is measured from it — so the whole building-eligibility geometry hangs off one arbitrary own-building pick taken at the bot's first tick and never revisited. **Staleness is the actual defect:** in WW3MOD the bot owns essentially only its Supply Route at t=0, so it lands on the SR by accident rather than intent; as the bot gains buildings the sample becomes arbitrary, and it never follows the base or the front.

**RETRACTED — an earlier version of this entry also claimed the `LocalRandom` draw was a multiplayer-divergence hazard "feeding an ORDER-producing decision off the unsynced stream". That is wrong for this fork**, on two independent grounds either of which alone settles it. (a) Bot logic is activated on exactly one machine — `Player.cs:225` gates it on `if (IsBot && Game.IsHost)` — so a bot decision is made once and leaves as a networked order like any other; no other client re-derives it. (b) Independently, `LocalRandom` here is **not per-client**: `World.cs:228-231` seeds it `new MersenneTwister(DeriveLocalSeed(localSeed))` from the **shared lobby seed** (`DeriveLocalSeed`, `:286-289`). "Local" in this fork means *decorrelated from `SharedRandom`*, not *per-machine* — that derivation exists precisely to give full replay determinism (DISCOVERIES 2026-07-20). Recorded so the next reader does not "fix" a non-bug. **Caveat carried, not verified:** asserted for skirmish and listen-server; dedicated-server bot hosting was not checked.

The remaining reason to leave the draw alone is about **benchmarks, not correctness**: removing a `LocalRandom` call shifts every subsequent draw in the stream and breaks byte-identity against the recorded A/B baseline, so it is a deliberate, separately-measured act rather than a drive-by. The new `RequireBelievedThreat` gate makes the frozen centre much less load-bearing — a building must now also sit inside a believed weapon envelope — but does not remove it. Fix shape: resolve the SR each scan (mirroring `CaptureCoordinatorBotModule.FindOwnSupplyRoute`).

## 2026-08-08: [low, doc-in-code] `RetreatCapturerWhenDone`'s `[Desc]` gives the wrong reason for a correct conclusion (found while: unit-purpose, `auto/unit-purpose`)
`CaptureCoordinatorBotModule.cs:282` justifies retreat-when-done with "A CaptureSpecialist has no `AttackBase`, so it is EXCLUDED from every combat free pool". The exclusion is real, the mechanism is wrong: `^TECN` inherits `AttackFrontal` + `Armament: Pistol` from `^ArmedCivilian` (`infantry.yaml:349-351`), so a technician **passes** `PoiOffensiveBotModule.IsEligibleCombatUnit`'s `AttackBaseInfo` test (`:2286`) and is excluded one line later by the ROLE filter (`:2380`, `MainBattle || IndirectFire`). It matters because the stated reason makes the exclusion look like an accident of armament that a future "give the technician a rifle" change would silently undo, when it is a deliberate role-class decision such a change would not touch. Comment-only. Same family as the `infantry.yaml:2204` "Unarmed" description the 260808 recon flagged.

## 2026-08-07: [med, FIXED] `GarrisonBotModule` permanently claims — and thereby freezes — supply trucks (found while: supply-truck oscillation fix, `auto/supply-dwell`)
With `GarrisonActorTypes` unset in mod YAML (the live state), `IsGarrisonEligible` falls back to `a.Info.HasTraitInfo<PassengerInfo>()` — and `^WheeledVehicle` grants `Passenger` (`CargoType: Vehicle`, `vehicles.yaml:116-123`), so `truk` qualifies. The module issued `EnterTransport` (a guaranteed no-op: `Passenger.ResolveOrder`'s `IsCorrectCargoType` rejects it, since garrison buildings take `Types: Infantry`) and then took `blackboard.ClaimUnit(unit, "garrison")` — with **no `ReleaseUnit` call anywhere in the file**. The truck was thereafter skipped by every claimant-checking module (including `SupplyFollowerBotModule`) for the rest of the match: a FROZEN truck — visually distinct from the oscillating one, same "trucks never resupply anyone" complaint. **`GarrisonBotModule@defenses` is `RequiresCondition: enable-ai-any` (`rules/ai/ai.yaml:709-710`) with no `Participates` or BotType narrowing on the claim path, so this hit EVERY AI profile** — @experimental, @stable, Normal, Rush, Turtle and legacy alike. Narrower in timing than in audience: the candidate filter requires `a.IsIdle`, so only a PARKED truck is grabbable, and a truck already claimed `supply-follow` is excluded — the window is an early-game idle truck before `SupplyFollower` has clusters to task it against. That window is WIDENED by the supply-truck fix in the same branch (a danger-vetoed truck is more often left unassigned, and an unassigned truck goes idle), which is why this is a prerequisite for that change rather than an incidental extra. **FIXED** on `auto/supply-dwell`: per-building `CanEnter` cargo-type match at the pairing site (mirrors `Passenger.cs:113-121`, so no order is issued that the order layer will discard), plus a real lifecycle — `claimedUnits` set, `ReleaseFinishedClaims()` each scan (releases on dead / left-world / idle-again), and release-all in `TraitDisabled`.

## 2026-08-07: [low] `UnitCluster.AmmoNeed` is a `float` used as a sort key in a synced bot decision (found while: supply-truck oscillation fix, `auto/supply-dwell`)
`SupplyFollowerBotModule.FindUnitClusters` accumulates `ammoNeed += 1f - (float)pool.CurrentAmmoCount / pool.Info.Ammo` and the plain selection path then orders by it (`OrderByDescending(c => c.AmmoNeed)`). The influence-stack invariant is integer math with zero RNG (`DOCS/reference/influence-stack.md`); a float accumulator makes the sort key depend on summation ORDER, and the order comes from `world.ActorsHavingTrait<Mobile>()` enumeration. The `SectorSpread` path already launders it through `NeedScore(...)` to an integer before `AssignSectors`, so the deterministic assignment is safe — it is the non-spread `OrderByDescending` that reads the raw float. Not observed to desync and not touched in this pass (changing the accumulator to integer per-mille shifts tie-breaks on every profile, which wants its own change and its own pins). Fix shape: accumulate shortfall in per-mille integers as `SupplyTruckHuntMath.ShortfallPerMille` already does, and make `AmmoNeed` an `int`.

## 2026-08-04: [low, FIXED] SupplyFollowerBotModule claim leak — trucks dropped from `activeTrucks` are never `ReleaseUnit`-ed (found while: supply-hunt T2 close-out verification)
`SupplyFollowerBotModule.cs:218` prunes the roster with `activeTrucks.RemoveWhere(a => a == null || a.IsDead || !a.IsInWorld || IsLowOnSupply(a))` — no `blackboard.ReleaseUnit` on the removed actors. Claims are taken via `blackboard.ClaimUnit(truck, "supply-follow")` at `:338`/`:395`/`:498`; the ONLY release site is the module-cleanup path `:649–655` (foreach + `activeTrucks.Clear()`). A dead truck's claim is moot, but an `IsLowOnSupply` truck is alive-and-claimed forever: other modules' claimant checks (the `:632-633` pattern — skip when `claimant != null && claimant != "<own key>"`) will skip it permanently, and if it later resupplies, this module re-adopts it only by re-scan, while rival claim-respecting modules never could. Pre-existing shape — T1/T2 changed neither the prune nor the claim sites; found by grep while verifying the T2 merge. Fix shape: `ReleaseUnit` inside the `RemoveWhere` predicate's true branch (or prune via an explicit loop that releases before removing). **FIXED 2026-08-07 (`auto/supply-dwell`)** using the second shape: the prune is now an explicit loop that releases each dropped truck, guarded by `IsUnitClaimedBy(a, "supply-follow")` so it can only ever hand back its own claim. Closed in the same pass as the identical defect in `GarrisonBotModule` (entry above) since the fix touched this line anyway.

## 2026-08-04: [low, FIXED] `SafeFollowDistance` is dead config — declared, set in ai.yaml, zero readers (found while: supply-hunt T2 close-out verification)
`SupplyFollowerBotModule.cs:31` declares `public readonly int SafeFollowDistance = 5;` and `rules/ai/ai.yaml:649` sets it, but no code path reads the field (grep: exactly two hits, declaration + YAML). Either the follow-offset logic it was meant to feed was never written or it was superseded by the leash/aura math. Risk: a tuner edits it expecting behavior change and gets a silent no-op. Fix shape: wire it into the follow-positioning path or delete field + YAML line. **FIXED 2026-08-07 (`auto/supply-dwell`): deleted both — the field and the YAML line.** Re-verified the two-hit grep across all `*.cs` and `*.yaml` before removing, so no map or mod override is orphaned; MiniYAML lint clean afterwards (no "refers to a trait field that does not exist"). Deleted rather than wired up because the follow-offset it implied is the designated-supply-points design, which is deliberately out of scope — a knob promising behaviour nothing intends to build is worse than no knob.

## 2026-08-04: [low] OOA rearm dispatch can loop forever on an existing-but-unreachable supply host (found while: delta re-review of the Wave A fix round `d7c7fac3`)
The engine's `ChooseResupplier` picks by closest-ignoring-path with no reachability check, and Wave A's `AmmoEvacMath` seek budget is a Chebyshev-distance proxy (deliberately cheap, documented as such at the decision site). If the chosen host EXISTS inside the 40-cell budget but is UNREACHABLE (walled-in / blocked terrain), the queued `SeekSupplyProvider`/`Resupply` movement bails, the activity terminates, and the next OOA sweep re-Decides SeekRearm — the host still exists, still inside the budget — and re-dispatches. Loop forever; the unit never falls through to Evacuate. Pre-existing shape: the no-path-check pick predates Wave A, and the sweep neither created nor worsened it (fix round `d7c7fac3` delta-reviewed clean on this point). Fix shape if it ever bites in a real game: bounded per-unit retry counter before Decide returns SeekRearm, or an actual reachability probe. Found by the waves-ab delta re-reviewer; adjudicated documented-latent, not a blocker.

## 2026-08-04: [low, doc-in-code] Three `rules/ai/ai.yaml` comments claim "Default OFF on the @stable twin = byte-identical" for gates the @stable block now SETS (found while: DOCS/reference curation pass)
Post-`stable-0802` parity the `@stable` blocks explicitly enable the same fog-legal gates as `@experimental`, but the `@experimental`-block comments still describe `@stable` as the off/byte-identical control: `StrategicRepointEnabled` (comment at `rules/ai/ai.yaml:313-314`, but `@stable` sets it at `:1262`), `StrategicCaptureRepointEnabled` (`@experimental` `:159` / `@stable` `:1216`), `DefendRepointEnabled` (`@experimental` `:498` / `@stable` `:1294`). The *engine-class* defaults are still off, so the claim holds for `normal`/legacy profiles and is false for `@stable`. Risk: an agent reading those comments assumes `@stable` is a clean A/B control for these levers and mis-attributes a benchmark delta. Comment-only — no behavioural defect. Fix shape: reword to "default OFF on the engine class; the `@stable` twin sets it too since the 2026-08-02 parity promotion". Related open decision (NOT a bug, user-owned): the item-24 A/B (`WORKSPACE/ai-bench/runs/260729_item24_ab_result.md`) recommended **KEEP OFF** for the capture/garrison repoint gates and committed no ai.yaml change, yet both twins ship them ON — now recorded in `DOCS/reference/influence-stack.md` §Known gaps.

## 2026-08-02: [high, FIXED] UnitBuilderBotModule aircraft keys case-mismatch — UPPERCASE UnitsToBuild/UnitLimits/UnitDelays keys made every called-in aircraft UNBUILDABLE (found while: case-sensitivity defect-family sweep)
Root: every actor name is lowercased at ruleset load (`Ruleset.cs:126` `k.Key.ToLowerInvariant()`), but `UnitBuilderBotModuleInfo.UnitsToBuild`/`UnitLimits`/`UnitDelays` are plain `Dictionary<string,int>` (`UnitBuilderBotModule.cs:31/:34/:37`) with the default ORDINAL, case-SENSITIVE key comparer. Both build paths compare a lowercased candidate actor name against those keys: the primary path `ChooseUnitToBuild` at `:327` `buildableThings.Any(b => b.Name == unit.Key)` (then indexes `Rules.Actors[unit.Key]` at `:329/:330`), and `BuildUnit`'s gates at `:191` `!Info.UnitsToBuild.ContainsKey(name)` (random path — rejects any name not present), `:195` `UnitDelays.ContainsKey(name)`, `:200` `UnitLimits.ContainsKey(name)` (`name = unit.Name`, the chosen buildable). So `{"A10":40}.ContainsKey("a10")` → **false**, `b.Name("a10") == unit.Key("A10")` → **false**: the airframe is never selected and never gates → **UNBUILDABLE**. **Blast radius:** every UPPERCASE-keyed aircraft UnitBuilder. `@america.heli` (`HELI`=Apache, `TRAN`=Chinook) had only `littlebird` (already lowercase) match — THIS is the reported "US bots only ever buy the littlebird, never the attack helicopter." `@russia.heli` (`HIND`/`MI28`/`HALO`) → Russia built NO helicopters. `@america.fixedwing` (`A10`/`F16`) and `@russia.fixedwing` (`MIG`/`FROG`) → no fixed-wing at all. Affects ALL fog bots on the `Aircraft` queue — both `@experimental` and `@stable` (all four UnitBuilders are gated `enable-ai-any`), both factions. GROUND UnitBuilders were unaffected (their keys were already lowercase `tecn.america`/`e3.america`/…). **FIX:** keys lowercased to the actor names — `ai.yaml` `@russia.fixedwing` :753-758, `@russia.heli` :795-806, `@america.fixedwing` :817-822, `@america.heli` :856-867. Same family as the AirUnitsTypes defect below. Durable fix (deferred to the reviewed batch): construct the three dictionaries with `StringComparer.OrdinalIgnoreCase` so config case can never silently no-op.

## 2026-08-02: [med, FIXED] SquadManager.AirUnitsTypes case-mismatch — UPPERCASE keys broke air-squad classification + rush own/enemy-aircraft filters (found while: case-sensitivity defect-family sweep; CONFIRMS+CLOSES the 2026-07-24 latent entry below)
`SquadManagerBotModuleInfo.AirUnitsTypes` is a default (ordinal, case-SENSITIVE) `HashSet<string>` (`SquadManagerBotModule.cs:31`) tested against actor names lowercased at load (`Ruleset.cs:126`). Comparison sites: `:351` `Info.AirUnitsTypes.Contains(a.Info.Name)` (`IsAirSquadUnit`), and — critically — `:421` (`TryToRushAttack` own-unit filter `!Info.AirUnitsTypes.Contains(unit.Info.Name)`) and `:430` (don't-rush-enemy-aircraft `!Info.AirUnitsTypes.Contains(unit.Info.Name)`). So `{"A10","F16"}.Contains("a10")` → **always false**. **Blast radius:** the `@experimental`/`@stable` fixed-wing managers now set `UseUnitRoles: true`, which routes `IsAirSquadUnit` through the role resolver and CURES the `:351` classification — but `:421`/`:430` read `AirUnitsTypes` UNCONDITIONALLY (role gate does not cover them). With uppercase keys: own MIG/FROG/A10/F16 were NOT excluded from ground-rush recruitment (own aircraft scooped into a ground `TryToRushAttack`) and enemy aircraft were NOT excluded from rush targeting (the AI would rush enemy planes with ground units). On the pre-`UseUnitRoles` legacy path the same mismatch also meant `IsAirSquadUnit` was always false → NO air squads formed. Profiles: `@experimental` + `@stable` fixed-wing SquadManagers, both NATO and BRICS. (`NavalUnitsTypes` shares the shape but is empty in ww3mod — moot.) **FIX:** keys lowercased — `ai.yaml` `@experimental.russia.fixedwing` :774, `@experimental.america.fixedwing` :833, `@stable.russia.fixedwing` :1174, `@stable.america.fixedwing` :1187. Durable fix (deferred): `StringComparer.OrdinalIgnoreCase` on the HashSet.

## 2026-07-24: [med] test-spread-preserves-prefix fails identically on base e7a5ac96 and tip 91949fe5 — pre-existing test/behavior mismatch, not a spread-orders regression (found while: spread-orders merge verification)
Verified empirically via a throwaway detached worktree built at base `e7a5ac96` (pre-merge): the test fails there with the byte-identical note as on merged tip `91949fe5` — "fail: tanks did not settle at their assigned AMs with prefixes preserved (X>=18, TankA.Y<=12, TankB.Y>=12) — spread re-mixed prefix Moves with the AM suffix". The test predates the wt/spread-orders branch (moved 2026-05 @ `e61f6826`). The spread-orders reviewer's static trace predicted this: the test's comment claims a "longest common suffix" preservation behavior that the order-aggregation code does not implement — queued prefix Moves are re-mixed with the AttackMove suffix during spread resolution. So either the test asserts a behavior that was never implemented, or the behavior regressed long before this window. Companion `test-spread-cargo-no-enter` PASSES on tip. Fix shape: decide which is authoritative — if prefix preservation is the intended contract, implement suffix-only spread substitution in the aggregation path; if not, rewrite the test's predicate to the actual contract. Needs its own work item; merge `91949fe5` stands. **[FIXED on branch `auto/spread-prefix`, 2026-07-29 — not yet merged]** Archaeology confirmed the "longest common suffix" comment was aspirational (that logic never existed in any commit; the global-pool aggregation predates the test by a day at `65ac0e64`), but the design intent is preservation, so **Branch 2** was taken: implemented suffix-only substitution in `GroupScatterHotkeyLogic.PerformGroupScatter` (new `CommonSuffixLength` helper + preserve-prefix path, legacy aggregation kept as the `suffixLen==0`/no-prefix fallback so all passing tests stay byte-identical). NUnit `GroupScatterSuffixTest` pins the contract (530/530 in the worktree). The behavioural `test-spread-preserves-prefix` was NOT run (ladder holds the harness) — expected PASS; interpretation + residual risks in `WORKSPACE/plans/260729_spread_prefix_brief.md`.

## 2026-07-24: [med, latent] SquadManager AirUnitsTypes name-match is a case-sensitivity no-op (found while: Phase-4b role migration set-equality lint)
`SquadManagerBotModuleInfo.AirUnitsTypes` is a default (ordinal, case-SENSITIVE) `HashSet<string>` populated verbatim from YAML — the ai.yaml values are UPPERCASE (`AirUnitsTypes: A10, F16` / `MIG, FROG`, ai.yaml:556/574/625/637/822/833), but actor names are lowercased at ruleset load (`Ruleset.cs:126` `k.Key.ToLowerInvariant()`). So `Info.AirUnitsTypes.Contains(a.Info.Name)` (`SquadManagerBotModule.cs:350`, and the exclusion filters at :420/:429) is `{"A10","F16"}.Contains("a10")` → **always false**. Net effect: the legacy/name-list fixed-wing air-squad path forms NO air squads — the fixed-wing SquadManager's air branch is dead for every `enable-ai-legacy-only`/`@stable` profile. NavalUnitsTypes has the same shape but is empty in ww3mod so is moot. NOT fixed here: correcting the case would change `@stable` byte-identity (forbidden this task) and is out of scope. Phase-4b's `UseUnitRoles` role gate (`role==AttackAir && Buildable && !AIHelicopterRole`) matches the lowercase actor names correctly, so `@experimental` fixed-wing air squads now actually form — an incidental correction, not a regression (the OFF path stays the identical no-op). Fix shape for the manager: lowercase `AirUnitsTypes` in ai.yaml (and/or build the HashSet with `StringComparer.OrdinalIgnoreCase`) so the frozen `@stable` twin's air squads work too — needs a benchmark to confirm the newly-forming air squads help rather than fly A10/F16 into AA unescorted.

## 2026-07-22: [low, cosmetic] Batch-match windows appear on screen as black windows on Windows — minimized launch not holding (found while: Phase-3 @experimental pricing batch)
`run-tournament.sh` Mode-B hidden profile launches each match with `OPENRA_WINDOW_MINIMIZED=1` (`run-tournament.sh:286-300`): the engine calls `SDL_MinimizeWindow` after window creation, rendering suspends (`Renderer.WindowIsSuspended`), and the window should sit in the taskbar for the whole match. User reports the windows are instead VISIBLE on the Windows desktop as solid-black windows during batches (black = rendering suspended, which is intended; visibility is not). Distinct from the fixed shroud-black (`8e32fa01` — TestMode observer full vision), which only applies to windows that actually render. Sim/results are unaffected — purely the focus/clutter annoyance the hidden profile was built to avoid. Fix shape (needs a game launch to verify, so serialized behind any running batch): check whether `SDL_MinimizeWindow` on Windows needs the window shown first / a delay, or whether a `SDL_WINDOW_HIDDEN` creation flag (never show the window at all in test mode) is the more robust profile; alternatively `SDL_MinimizeWindow` may be racing window creation on the SDL event pump. Verify the framerate-cap interaction note (`run-test.sh:300-302`) still holds for whichever mechanism replaces it.

## 2026-07-22: [low, experimental/human-scoped] Residual B1 walk-back: player redirect during executor Adjusting can be dragged back once (found while: merge review of phase3-executor @ e2208d42)
The executor's `ITick` B1 guard (`StancePositioningExecutor.cs`, `ITick.Tick`) skips while `State == Adjusting`, so a player redirect issued *during the executor's own adjustment move* is not caught mid-move: on the unit's next idle, `CohesionSlotMemory` (declared before the executor in `^Combatant`) queues return-to-slot first, the executor bails on `CurrentActivity != null`, and the unit is dragged back to the old cover cell **once**, re-settling `Arrived` with the stale anchor. Trigger window: redirect inside the adjust move (~≤164 ticks) to a cell ~5–14 away (short enough that the trip finishes inside `ForgetAfterTicks`=750 so the slot stays fresh); longer redirects self-heal (slot goes stale), and the *next* redirect is caught by ITick. Bounded, self-healing, only affects executor-managed units (`@experimental` bots + Phase-3 humans). Fix shape: handle `Adjusting && !WithinLeash(+margin)` in ITick, with a margin so the executor's own pathing excursions don't false-abort — needs its own test; deferred from the e2208d42 merge (review verdict: filing acceptable). **FIX (branch `auto/b1-walkback`, off main @ `4efe523f`, NOT merged):** ITick now handles the `Adjusting` case — aborts the stale adjust when the unit strays beyond `LeashRadius + AdjustLeashMargin` (Manhattan; new Info field, default 2), clearing the cohesion slot mid-move so `CohesionSlotMemory` can no longer drag the unit back; the next idle re-anchors at the redirect target. The margin admits the executor's own ≤2-cell obstacle detours (a false-abort is otherwise benign — the move still completes) and catches redirects past the band; near-band (≤6-cell) redirects still walk back by construction. Pure leash predicate extracted (`StancePositioningExecutor.WithinManhattan`) and pinned in `StancePositioningLeashTest`. Authored (NOT run) autotest `tools/autotest/scenarios/test-stance-redirect-midadjust` (RED-on-base / GREEN-on-fix). @stable byte-identical (executor `IsTraitDisabled` there ⇒ ITick returns before the new code; no new RNG, no trait-order change). Do-not-merge brief + deferred-run gate: `WORKSPACE/plans/260729_b1_walkback_brief.md`.

### 2026-07-29 follow-up (post-ladder merge sprint): b1-walkback MERGED to main; deferred behavioral gate now RUN and RED — scenario-side, NOT a fix regression
`auto/b1-walkback` (review tip `864fdb39`) merged into main (merge commit `2bf335cf`). Unit evidence is fully green: `dotnet test … Release` = **537/537** (523 base + 6 spread-prefix + 8 b1 leash pins), build 0 err. The §7 behavioral gate the implementer deferred was executed for the first time this sprint and is **deterministically RED** — but the RED is a scenario/executor-behavior mismatch, not the walk-back logic: (1) `test-stance-redirect-midadjust` FAILs ×2 seeds (786245522, 1398464782) at its **precondition** — "executor never started the zone-A adjustment; Rifle at 13,21": the Rifle never leaves spawn, so the actual `Adjusting`-state redirect path the fix targets is never exercised. (2) `test-stance-anchor-move` (the no-regression check) FAILs post-merge — "Rifle never reached zone-A cover edge; at 13,19" (moves 2 cells N, stalls short of the 13,17 cover edge). **Counterfactual pinned:** ran `test-stance-anchor-move` at pre-b1 main (`23398408`, post-spread-prefix, fix ABSENT) in a throwaway worktree → **identical FAIL** ("at 13,19", seed 10381436). So anchor-move was already RED before b1 — the merge did not regress it (VERIFIED-pre-existing). redirect-midadjust is a new branch-only scenario so no standalone no-b1 run is possible; its precondition failure is the same "executor doesn't drive the Rifle to the cover edge" behavior anchor-move exposes pre-fix, and the b1 logic only fires *after* `State == Adjusting` is reached (which never happens here), so it cannot be the cause. **Net:** b1 walk-back kept on main on unit evidence (8 pins + static/build); the behavioral claim is downgraded to **unit-verified only**. Residual = the stance executor never reaches/starts the north cover-edge adjustment these two scenarios assume — a separate **scenario/executor-calibration** follow-up. Scenarios left untouched (not edited, not deleted) per manager decision.

## 2026-07-22: [med, latent] UnitDefaultsManager writes per-machine state into synced sim fields (found while: Phase-3 executor hardening)
The Ctrl-Alt-click per-type stance/cohesion/resupply defaults are loaded from a **per-machine settings file** `Platform.SupportDir/ww3mod/unit-defaults.yaml` in `UnitDefaultsManager.IWorldLoaded.WorldLoaded` (`UnitDefaultsManager.cs:38-42`), and `AutoTarget.Created` applies them into **synced, unhashed** sim fields (`stance`, `engagementStance`, cohesion, resupply) for every `Owner.Playable && !Owner.IsBot` actor (`AutoTarget.cs:355-388`, fields `:249-252`). Two clients with different `unit-defaults.yaml` diverge silently at first spawn — no OOS hash trips (same failure class as unsynced `LocalRandom`). Replays re-read the file (saved at `IGameOver`, `:44-47`), so replaying the session that changed a default diverges from the live run. **Latent, not live today** — masked by single-human-vs-bot play (one machine's view is the only view); live stance changes are fine because they cross the order stream (`EngagementStanceSelectorLogic.cs:87` → synced `AutoTarget.ResolveOrder` `:426-439`). Only the *persisted-defaults* channel bypasses orders. **Phase 3 does NOT make this load-bearing** — the tactical-positioning opt-out was deliberately decoupled from the persisted stance defaults (it reads the live synced engagement stance + `deployed` condition in the executor, see `260722_phase3_redteam.md` §3 option b), so default-ON does not ride this channel. Correct fix (deferred, re-price with the first MP/replay-correctness consumer): owning client emits a synced `SetUnitTypeDefaults` order at game start (and on each Ctrl-Alt-click); `AutoTarget.Created` reads a per-player synced store instead of the local file — fixes all four default classes, not just positioning. Audit: `WORKSPACE/plans/260722_phase3_redteam.md` B2.

## 2026-08-08: [med] Excluding ejected crew from the offense RE-HOMES them to six other modules, none of which respect an in-flight evac (found while: fix round on `auto/evac-polish`, from a grep the original comment should have done)
`PoiOffensiveBotModule.IsEligibleCombatUnit` now rejects `CrewMemberInfo` actors, and the comment shipped with it claimed "no other module recruits crew". **That claim was false and is now corrected in-code.** Crew resolve to `UnitRole.MainBattle` (armed + mobile, no specialisation), and this module is the only one testing `CrewMemberInfo`, so every role-based recruiter still takes them: `LayeredDefenceBotModule.IsLineEligibleByRole`, `PoiGarrisonBotModule.IsEligibleCombatUnit`, `LaneAmbushBotModule.IsEligibleAmbusher` (all gated on `UseUnitRoles`, which `@experimental` sets true), plus `EngineerRouteOpenBotModule`'s screen pool and `HelicopterSquadBotModule`'s lift filter. Only `ScoutBotModule` and `MountedTransportBotModule` reject them, incidentally — both use actor-name allowlists crew are absent from. `SquadManagerBotModule` would take them but its ground FSMs are dead code on both profiles.
**Two consequences.** (1) Item 36's exclusion is narrower than it reads: crew are not removed from AI tasking, they are re-homed — mostly to defensive/garrison roles, which is arguably *better* than an armoured push, so this is not urgent. (2) **The real defect: none of those modules consult `IsEvacuating`**, so with `EvacuateEjectedCrew: true` (on `@experimental`) a crew member sent to evac can be re-tasked by e.g. LayeredDefence and have its `RotateToEdge` cancelled — the exact free-pool pathology of 2026-07-21, one module over. The new `IsInterruptible = false` covers only the final off-map leg, not the drive to the edge.
**Not fixed here** — the general fix is teaching six modules the same predicate (or hoisting `IsEvacuating` somewhere shared and consulting it in each), which is its own change with its own gating decisions per module. Flagged before `EvacuateEjectedCrew` is benchmarked, because a contended evac will show up as crew wandering rather than leaving.

## 2026-08-08: [med] `test-offense-ammo-guard` is RED at `bd3abacf` — PRE-EXISTING, and the test's predicate contradicts the evac disposition it now runs with (found while: PIPELINE items 36/38 on `auto/evac-polish`)
**Attribution settled by running the scenario at both SHAs.** At base `bd3abacf`: FAIL, `"EmptyTank died before verdict - inconclusive"`. At `auto/evac-polish` (`02fe9eab`): FAIL, `"EmptyTank advanced 24 cells east ... guard failed"`. Different seeds, different message, **same cause — the branch does not cause it.**
**The guard under test actually WORKS**, which the verdict hides. The run log shows `[exp-ooa] sweep player=USA-bot rearm=0 evac=1 banked=2260 evacuating=1 tick=38` and, the same tick, `[exp-offense] reeval player=USA-bot pool=6 free=0` — EmptyTank was swept to evac and kept out of the free pool exactly as `SkipOutOfAmmoUnits` intends. What fails is the test's PROXY: it infers "was recruited" from "moved more than 4 cells east" and requires the tank to still be alive at the verdict. Both assumptions date from before `8fb0c855` shipped `EvacuateOutOfAmmoUnits: true` on `@experimental`; a dry tank no longer stays parked, it **drives to the map edge and sells**. Depending on seed and which edge the pathfinder picks, it either dies before the verdict (base run) or is still en route and has travelled east (branch run). **The scenario cannot pass in its current form on `@experimental` and its RED carries no information about the guard.**
**Fix (not done here — it is a test change, out of scope for an evac branch):** score the guard on what it actually asserts. The tank is excluded iff it is not on an axis — assert `evacuating`/`RotateToEdge` or a non-east heading, or pin the disposition via the `[exp-ooa]` log, and drop the "still alive at verdict" requirement since a completed evac SHOULD dispose it. Note also that the eastward drift is not necessarily wrong: `RotateToEdge` picks the edge cell reachable by the locomotor, which on this map need not be the near-west one.

## 2026-08-08: [med] Autotest triage looks at the wrong debug.log — the run's log is `debug.log.1` whenever another OpenRA instance holds the lock (found while: trying to explain a RED verdict)
`run-test.sh`'s `find_debug_log()` (`tools/autotest/run-test.sh:229-243`) only ever probes `debug.log`, and the timeout path prints the same path to the user (`:537`). But OpenRA falls back to a numbered suffix when `debug.log` is locked by a live instance, so a test launched while the shellmap/menu game is still open writes to **`%APPDATA%\OpenRA\Logs\debug.log.1`**. In this case `debug.log` was stale by 13 minutes (a 07:17 shellmap session) while the 07:30 test run's entire log — including the `[exp-ooa]` sweep lines that explained the verdict — sat in `debug.log.1`. **Anyone triaging a bot scenario from `debug.log` may be reading a different game.** Cheap fix: have `find_debug_log()` pick the NEWEST of `debug.log*` rather than the first that exists.

## 2026-08-08: [low] `make.ps1 test` is RED at `bd3abacf` on three unrelated actors (found while: validating an ai.yaml edit)
`Actor type 'gtwr' / 'pbox' / 'hbox' consumes conditions that are not granted: being-captured.` Confirmed PRE-EXISTING by re-running with the branch's `ai.yaml` change stashed — the same three errors appear. Unrelated to evacuation work; noted so the next person editing YAML does not mistake it for their own breakage.

## 2026-07-21: [high] Mounted transports never dismount — wrong order string; autotest blind to the unload (FIXED, experimental-gated) (found while: live-crash match triage)
`MountedTransportBotModule.AdvanceTask` issued `Order("UnloadCargo", …)` but `Cargo.ResolveOrder` only handles `"Unload"`/`"UnloadCargoPassenger"` (`Cargo.cs:248,255`) — `UnloadCargo` is the *activity* class name, so the order was silently dropped and carriers sat at the drop-off loaded forever (ferry AND generic frontline delivery). Fixed behind default-false `UnloadOnArrival` (issues `"Unload"`), enabled only on `MountedTransportBotModule@experimental` so `@poi`/@stable stay byte-identical; added an idle+CanUnload re-issue guard against the `!CanUnload` first-order drop (`Cargo.cs:250`). **Autotest blind spot (HARDENED @ e842cf60):** the old `test-tecn-ride.lua` passed on carriage+arrival-within-6-cells only — it never checked dismount or derrick capture, so the broken string shipped GREEN. The predicate now stages four latches (`test-tecn-ride.lua:33-58`): mounted → delivered → **dismounted** (`Carrier.HasPassengers` goes true→false at the drop-off — the check the old test lacked) → **captured** (`Derrick.Owner.Name == "USA-bot"`). A carrier that arrives loaded but never unloads keeps `HasPassengers` true, so `dismounted` never latches and the 100s wait times out RED — the "UnloadCargo" no-op can no longer pass. Gating verified: scenario runs `Bot: experimental`, which grants `enable-ai-experimental` (ai.yaml:61-63) and so loads `MountedTransportBotModule@experimental` with `UnloadOnArrival: true` (ai.yaml:521,535) — no map-local rules override needed. Run deferred (benchmark ladder on main); pending harness availability, `./tools/autotest/run-test.sh test-tecn-ride` should go GREEN.

## 2026-07-21: [med] Autotest harness hangs forever when the test map's rules fail to load (found while: diagnosing a 16-min stuck test game)
Two compounding gaps. (1) **Engine side (still open):** when a test map's rules fail to load (`Map.PostInit` → `Ruleset.Load` throws, e.g. a duplicate-key MiniYaml merge), the game logs to `debug.log` ("Failed to load rules for TEST: …") and falls back to the **main menu**, where it idles forever — `Test.Pass`/`Test.Fail` never run, `result.json` is never written. (2) **Harness side (FIXED, 2fa70d11):** `run-test.sh` now has a hard wall-clock watchdog — `--timeout N` (default `TIMEOUT_SECS=300`, `run-test.sh:70`) — that kills the game tree and synthesizes a `result.json` FAIL with "timeout: no verdict … check debug.log" (`:398-446`) when neither Pass nor Fail arrives in time. Remaining guidance: test-runner briefs must still check `debug.log` whenever `result.json` is absent, since the engine-side fallback-to-menu is unchanged. Observed instance: `test-stance-optout` on the phase3-executor worktree, duplicate key `ar.america` in the map rules.

## 2026-03-24: AirstrikePower crash — case-sensitive actor lookup (FIXED)
`Rules.Actors` keys are lowercase but `AirstrikePower.SendAirstrike` looked up `info.UnitType` without lowercasing. Crashed when Russia used Su-25 airstrike (`FROG.Airstrike` → `KeyNotFoundException`). Fixed: added `ToLowerInvariant()` to C# lookup + lowercased YAML UnitType values.

## 2026-07-21: [low] Called-in helis arrive at the SR/map-edge cell and loiter (RallyPoint has no Path) — Bug 2 Part A, OUT OF SCOPE of the rearm fix (found while: implementing fix-evac-heli)
`ProductionFromMapEdge` gives called-in aircraft `hasRallyPoint ? rp.Path : { self.Location }` (`ProductionFromMapEdge.cs:89,173-175`); the SR `RallyPoint` sets no default Path (`structures.yaml:272-274`) and the AI issues no rally order, so a fresh heli is told to move to the SR building's own edge cell and stops. Cosmetic staging only — once a squad forms (the rearm-ready + `SquadHasAmmo` bypass shipped on `fix-evac-heli`) the FSM issues moves and engaged helis leave the corner. A forward staging RallyPoint Path / staging Move on recruit is deferred; not required for helis to fly missions.

### 2026-07-21 follow-up (post-SkipRearmReadyCheck triage): production rally is the SOLE corner source — no degenerate math to fix, staging is a NEW behavior needing a run
User still reports "helis fly to the map corner and stay" after the `SkipRearmReadyCheck` launch fix (`f0a4d229`). Exhaustive code-only trace of every heli destination confirms the corner is the corner-placed **SR itself** (`self.Location` waypoint above), not a degenerate `(0,0)`:
- **FSM `CPos.Zero` targets are all guarded.** `FindWeakestEnemyCell` returns `CPos.Zero` when no enemy is found (`ThreatMapManager.cs:234,272`), but both call sites gate on `!= CPos.Zero` (`HelicopterStates.cs:209` idle-target; `HelicopterSquadBotModule.cs:368` transport drop-zone). `FindSafestRetreatCell` defaults to `from` (the unit's own cell), never `Zero` (`ThreatMapManager.cs:279`).
- **`ReturnToBase` with no hpad does NOT fly to a corner** — with no `Reservable` rearm actor it queues `FlyIdle` and idles *in place* (`ReturnToBase.cs:106-108`). WW3MOD builds no hpad, so every low-ammo/return heli (`SendLowAmmoUnitsHome`, `HelicopterReturnState`) simply idles where it stopped, near the front — not the corner.
- **Therefore the only thing that puts a heli at the corner is the production rally = `self.Location` (the SR).** Helis that never reach the 2-ready squad threshold (`AttackSquadSize`), or sit between missions, park at the SR beachhead, which is at a map corner/edge — hence "corner idle." There is **no buggy coordinate to patch**: `self.Location` is a valid cell.
- **A fix is tractable but is a NEW behavior, not a bug fix — and its safety needs a run.** The clean shape (mirroring `MountedTransportBotModule.PreContactStagingCell` + `DeliverBeforeContact`, `MountedTransportBotModule.cs:521-535`): add an `@experimental`-only, default-off field to `HelicopterSquadBotModule` that issues a `Move` to a forward staging cell (a fraction of SR→top-`PoiMap`-offensive-target) for managed helis that are idle, not in a squad, and still sitting near the SR. Default-off keeps controls/@stable byte-identical. **NOT implemented here (code-only session, no benchmark):** forward-staging idle *ammo-less* attack helis toward the enemy POI can fly them into AA with no target and worsen heli K:D — the exact tradeoff only a benchmark can settle. Verification pass must measure heli survival / K:D with the flag on vs off, confirm staging does not fly ammo-less helis into AA, and confirm it does not starve the 2-ready squad-formation threshold, BEFORE enabling it on `@experimental`.

## 2026-07-21: [high] AI attack helicopters permanently benched with no HPAD (found while: playtest bug triage)
`HelicopterSquadBotModule.IsReadyForMission` (`engine/OpenRA.Mods.Common/Traits/BotModules/HelicopterSquadBotModule.cs:399-408`) requires every AmmoPool `HasFullAmmo`; attack helis only refill while `unit.docked` at an `hpad` (`mods/ww3mod/rules/ingame/aircraft-russia.yaml:178` etc.) and the mod builds no HPAD, so after the first shot no squad ever forms and the heli idles at its edge/rally cell forever. Distinct from the production-side `SkipRearmBuildingCheck`, which does not cover the squad path. Fix options in `WORKSPACE/plans/260721_playtest_bugs_triage.md` (Bug 2).

## 2026-07-21: [med] Out-of-ammo evac units recruited onto offensive axes (found while: playtest bug triage)
`PoiOffensiveBotModule.IsEligibleCombatUnit` (`PoiOffensiveBotModule.cs:403-412`) has no ammo filter, so an evacuating (RotateToEdge) zero-ammo unit re-enters the free pool and its AttackMove cancels the evac. `LayeredDefenceBotModule` already guards this (`SkipOutOfAmmoUnits`/`IsOutOfAmmo`, `:102,:273,:465-471`); PoiOffensive needs the same. Fix in `WORKSPACE/plans/260721_playtest_bugs_triage.md` (Bug 1).

## 2026-03-24: HeliAutorotate/HeliCrashLand build errors
Untracked WIP files `engine/OpenRA.Mods.Common/Activities/Air/HeliAutorotate.cs` and `HeliCrashLand.cs` fail to compile: `IActivity` type not found. These files are interdependent with `HeliEmergencyLanding.cs` trait. Pre-existing issue, not caused by stance rework.

## 2026-08-08: [med] Supply truck `residueUnusable` <-> map-edge loop — SECOND truck loop, untouched by the 2026-08-07 evac fix (found while: diagnosing the user's live-play report)

Recon `WORKSPACE/recon/260808-truck-post-fix-behaviour.md` (`4d747384`) established that TWO distinct loops make a supply truck visibly travel back and forth, with different signatures:

- **Loop A** — the approach-abort cycle, period ~5 scans / ~30 s, amplitude ~23 cells, heading back toward the player's own SR with a HEALTHY supply bar. Diagnosed and being addressed by drop-and-leave on `auto/supply-drop`. **This is the one the user confirmed observing (2026-08-08).**
- **Loop B (this entry)** — a truck whose remaining supply is unusable enters a `residueUnusable` <-> map-edge cycle: RED/empty supply bar, heading for a MAP EDGE rather than the SR. The 2026-08-07 merge (`e79ddd97`) did not touch this path and the drop-and-leave work does not address it.

Kept as a separate entry specifically because the two are easy to conflate by eye and a future report of "trucks still dither" must be discriminated by **bar colour + heading** before any diagnosis is trusted: healthy bar toward the SR is Loop A, red bar toward a map edge is Loop B.

**ROOT-CAUSED AND ADDRESSED 2026-08-08 (`auto/truck-churn`).** The mechanism is the one `260808-order-churn-census.md` §3.2 traced statically: `SupplyProvider.ScanInterval` is an unset C# default of **7 ticks** on TRUK, so `UpdateTarget` re-decided the `residueUnusable` latch every 0.42 s from a `ResidueVerdict` that flips **both** ways; `CountsAsEmpty` reads that latch; and `DropsSupplyCache.ITick` re-checks `CountsAsEmpty` **every tick** and queues `RotateToEdge` — the drive to the map edge and the sale — within one tick of it reading true. Meanwhile re-adoption is owned by `SupplyFollowerBotModule` at 150 ticks. Departure at 0.42 s granularity against recovery at 9 s is the whole asymmetry, and it needs no rare condition: one soldier crossing the 5-cell aura edge is enough. Fixed by requiring `ResidueConfirmScans` (5) consecutive unusable verdicts before the latch is set, while leaving the clear undamped.

**Not closed — this entry stays open until seen in play.** The fix is reasoned + NUnit-pinned at the predicate; it has NOT been observed in a live match (the branch ran no simulations), and the pins do not cover the `UpdateTarget` call site, which no test in this repo can reach. The discriminator above is still the right first move on any new report. What *would* now be new information: a red-bar map-edge run that persists **through** ~35 ticks of the truck holding station, which the damper cannot explain and which would point at `AddSupply`/drain clearing the counter rather than at the verdict.

Relevant if picking this up further: the supply path gained `Log.Write` instrumentation with drop-and-leave, but the **residue latch itself is still uninstrumented** — no line records a verdict flip or a confirmation step, so Loop B remains inferable only from the truck's heading and bar colour, not from `%APPDATA%\OpenRA\Logs\debug.log`.

## 2026-08-09: [med] `AIHelicopterRole.HitAndRunCooldown` counts SQUAD UPDATES, not ticks — and its consuming state is unreachable on both shipped profiles (found while: documenting the squad/combat-state layer, `DOCS/bots/05-squads-and-combat-states.md`)

Two independent defects on the same field, same family as the `EvacDangerThreshold` 60-vs-66,834 constant.

**(1) Wrong unit.** `[Desc]` says "Ticks of engagement before pulling back" (`Traits/Air/AIHelicopterRole.cs:33-34`), but the counter it feeds — `HelicopterAttackRunState.attackTicks` (`Squads/States/HelicopterStates.cs:685`, compared at `:709`) — is incremented once per `Squad.Update()`, and heli squads update every `SquadUpdateInterval` = 5 world ticks (`HelicopterSquadBotModule.cs:146`, `:508-512`). So the Apache's `HitAndRunCooldown: 200` (`mods/ww3mod/rules/ingame/aircraft-america.yaml:277`) is 1000 world ticks ≈ **60 s** at the default `Timestep: 60` (`mods/ww3mod/mod.yaml:369-371`), not 200 ticks ≈ 12 s. Same for `stuckTicks > 200` (`:651`, ≈60 s) and `withdrawTicks < 75` (`:797`, ≈22 s) — those two look deliberately chosen for the real duration, which makes the `[Desc]` the outlier rather than the code.

**(2) Unreachable anyway.** `HelicopterAttackRunState` is entered from exactly one site, `HelicopterApproachState:571`, inside `if (!standoff)` (`:565`). `StandoffEngagement: true` is set on BOTH shipped profiles (`mods/ww3mod/rules/ai/ai.yaml:1419` `@stable`, `:1446` `@experimental`), so the close-range attack run — and with it the entire hit-and-run mechanic — never executes in a shipped match. The field is nonetheless configured on four airframes.

Additionally the `:114` instance (`HitAndRunCooldown: 100`) is on the littlebird's **Scout** role, and `TryLaunchAttackMission` only ever recruits `AttackHeavy`/`AttackLight` (`HelicopterSquadBotModule.cs:788-789`) — scouts never join a squad, so that value is inert twice over.

Not fixed here (documentation task). Fix shape is a decision, not a patch: either delete `HelicopterAttackRunState` + the field, or rebuild hit-and-run inside the standoff path where it can fire. Whichever is chosen, correct the `[Desc]` to say "squad updates" or convert the counter to world ticks.

## 2026-08-09: [low] Five `AIHelicopterRoleInfo` fields are configured in mod YAML and read by no C# code (found while: documenting the squad/combat-state layer)

`EngagementRange` (`Traits/Air/AIHelicopterRole.cs:25`), `PreferSoftTargets` (`:37`), `AvoidAntiAirRange` (`:40`), `AIBuildPriority` (`:43`) and `AIBuildLimit` (`:46`) have **zero consumers** across `engine/` (verified by grep for each identifier). All five are nonetheless set per airframe in `mods/ww3mod/rules/ingame/aircraft-america.yaml` (`:10, :111, :115-116, :274, :278-279`) and `aircraft-russia.yaml` (`:10, :103, :107` and neighbours). Call-in weight/limit is actually driven by `UnitBuilderBotModule`'s own `UnitsToBuild`/`UnitLimits` (`ai.yaml:1268-1275` etc.), so `AIBuildPriority`/`AIBuildLimit` are shadowed rather than merely unread.

Why it matters beyond tidiness: these are the knobs whose NAMES promise exactly the behaviours a tuner would reach for ("how close does the Apache engage", "does the Hind avoid AA"). Editing them changes nothing, silently. Configured-but-inert is worse than absent. Fix shape: delete the fields and their YAML, or implement them — but do not leave them declared.

## 2026-08-09: [low] `Squads/States/GroundDangerNav.cs` is live code filed inside a folder of unreachable state machines (found while: documenting the squad/combat-state layer)

`GroundDangerNav` (influence-stack Stage E, `ab7bd283`) sits in `engine/OpenRA.Mods.Common/Traits/BotModules/Squads/States/` but is neither a state nor used by any state. Its consumers are `PoiOffensiveBotModule.cs:3005, :3628` and `SupplyFollowerBotModule.cs:724, :1420`. Every actual *state* in that directory except `AirStates.cs` and `HelicopterStates.cs` is unreachable (see `DOCS/bots/05-squads-and-combat-states.md` §2), so a reader who has learned "this folder is dead" will mis-file live ground-routing code as dead. Its sibling `HeliDangerNav.cs` IS used by the heli states (`HelicopterStates.cs:584, :587, :812`) and is correctly placed. Fix shape: move `GroundDangerNav.cs` up to `BotModules/` alongside its consumers.

## 2026-08-09: [low, dead-code] `GroundUnitsRegroupState.MaxRegroupTicks` comment is off by ~270× (found while: documenting the squad/combat-state layer)

`const int MaxRegroupTicks = 750; // ~12.5 seconds to regroup before re-engaging or dissolving` (`Squads/States/GroundStates.cs:302`). `regroupTicks` increments once per `Squad.Update()` (`:314`), and ground squads would update every `AttackForceInterval` = 75 world ticks (`SquadManagerBotModule.cs:72`, `:274-279`) — so the real window is 56,250 world ticks ≈ **56 minutes** at the default `Timestep: 60`. Even read as raw ticks it would be 45 s, not 12.5 s.

**Unreachable, and recorded only for the class.** `GroundUnitsRegroupState` cannot execute on either shipped profile (all four `SquadManagerBotModule` instances set `IgnoreGroundUnits: true`; see `DOCS/bots/05-squads-and-combat-states.md` §2.1). Logged because it is the same defect family as `HitAndRunCooldown` above and as `EvacDangerThreshold` — a duration constant written against an assumed per-tick cadence that is actually per-module-update. Anyone porting the regroup idea into `PoiOffensiveBotModule` must re-derive the number rather than carry it across.


## 2026-08-09: [low, doc-in-code] `HelicopterSquadBotModule`'s `goalGuard` field comment claims a gate the code 90 lines later does not apply (found while: the `DOCS/bots/` cross-document reconciliation pass, `main @ 25a8aebd`)

The field declaration at `engine/OpenRA.Mods.Common/Traits/BotModules/HelicopterSquadBotModule.cs:403-406` reads:

> *"Per-unit commitment ledger (shared PoiGoalGuard). Resolved ONLY when CommitTransportPassengers is on, so the frozen/@stable path never looks it up ⇒ byte-identical."*

The resolution at `:496` is **unconditional** — `goalGuard = player.PlayerActor.TraitOrDefault<PoiGoalGuard>();` — with its own (correct, later) comment at `:490-495` explaining that the READ side is deliberately "a real availability gate for every profile" while only the WRITES stay behind `CommitTransportPassengers`.

**The behaviour is correct and deliberate; the comment is the stale half.** No code change is wanted — delete or rewrite the `:403-406` sentence. Filed because it is a live instance of the exact pattern this document set names (`DOCS/bots/06` §2 **P10**, `DOCS/bots/README` §5.8): a comment asserting a gate whose definition was later widened out from under it. It also cost real reviewer time — `DOCS/bots/03` §E2 asserted the two claim registries are "honoured by disjoint sets of modules", and this module is one of the two counter-examples that makes that false.

## 2026-08-11: [med] `Resupply` freezes `activeResupplyTypes` in its CONSTRUCTOR, so a unit topped up on the way to a Logistics Centre walks the whole distance anyway (found while: ending pointless resupply errands, branch `auto/supply-errands`)

`Resupply.cs:85` decides `cannotRearmAtHost` once, at construction — `rearmable.RearmableAmmoPools.All(p => p.HasFullAmmo)` — and sets `ResupplyType.Rearm` from it. Nothing re-asks. `Tick`'s approach branch is gated on `activeResupplyTypes != 0` (`:139`), and the Rearm flag is only ever cleared by `rearmable.RearmTick(self)` (`:174`), which cannot run until the unit has already **arrived**. So a soldier dispatched dry toward an LC who is rearmed to FULL by a passing truck en route keeps walking the whole way, docks, does nothing, and walks back off.

This is the LC half of the user's report *"even with full ammo he kept moving towards the supply actor it was heading for before"* — and it is the half that matches the words most literally, because on the truck path `SeekSupplyProvider` at least *tries* to bail on a full pool. Infantry name both hosts (`RearmActors: truk, logisticscenter`, `infantry.yaml:1160` etc.) and `AmmoPool.ChooseResupplier` picks whichever is closer ignoring path, so a real match reaches this branch whenever the LC is the nearer of the two.

**FIXED 2026-08-12, branch `auto/lc-errand`**, pinned by `test-lc-errand-ends-when-rearmed-en-route` (RED: *"rearmed en route (50 rounds) and still walking to the logistics centre — reached x=22, errand outlived the reason for it"*).

The blast-radius worry above resolved better than expected: the repair question did not need a policy answer, because only the `Rearm` bit is re-asked and the activity's **existing** exit is `activeResupplyTypes == 0`. A unit that came for ammo AND repair therefore keeps walking for the repair on its own, with no branch spent on the case. `dispatchedBecauseDry` reaches `Resupply` from `AmmoPool.cs:373` and gates every line of it, so the other five construction sites (aircraft at a pad, `Repairable`, `RepairableNear`, `LayMines`, the Lua binding) are untouched — none of them is an ammunition errand.

The second half of the root cause was the same `ChildHasPriority` defect that produced the truck half: `Resupply` never cleared it either, so its `Tick` could not run during the approach no matter what the frozen set said. It is now cleared **only** on the dry errand, which also makes the parent responsible for ticking the child and for not appending a second approach on top of a live one.

## 2026-08-11: [med] `SeekSuppliesAndReturn` has the same never-ticks defect that was just fixed in `SeekSupplyProvider` — its whole state machine only runs between legs (found while: ending pointless resupply errands, branch `auto/supply-errands`)

`Activity.TickOuter` runs `lastRun = TickChild(self) && (finishing || Tick(self))` (`Activity.cs:112`) when `ChildHasPriority` is true, which is the default — so the parent's `Tick` is **skipped entirely for as long as a child activity is alive**. `SeekSuppliesAndReturn` never sets it false, yet its body is written as if it ran every tick: it calls `TickChild` itself, it carries an explicit "let a cancelled child from the previous leg unwind before planning this one" guard (`:127-131`), and its entire reason for existing is `SupplyHuntMath.NextState` re-asked continuously.

What actually happens today is that the state machine only advances at the boundaries between legs. The consequences are exactly the ones its own doc comments promise are handled and are not:

- *"Being refilled by someone else while still walking also sends us straight home"* (`SupplyHuntMath.cs:161`) — cannot fire during the approach.
- `ProviderUsable()` re-asked every tick, the symmetry that `AutoSeekSupplies.CanServe` was extracted to guarantee (`SeekSuppliesAndReturn.cs:80-89`) — cannot fire during the approach, so a truck that drains, pauses or drives home mid-approach does NOT release the unit "immediately"; it releases it when the approach ends.
- The `MaxApproachAttempts` re-plan bound (`:150-155`) only counts approaches that *completed*.

Not fixed with `SeekSupplyProvider` because it is a second behavioural change on a second errand system (the idle seek), and the idle seek is the path that already walks home — so it is a correctness/latency defect rather than the reported symptom. **Fix shape:** `ChildHasPriority = false` in the constructor; the body already assumes it. Wants its own scenario, and note that turning per-tick evaluation on also brings that activity's re-planning alive for the first time.

## 2026-08-11: [med] A SUPPLYCACHE below 50 supply serves nobody AND never despawns — `RestockThreshold` gates serving on a crate that has no restock trip, contradicting `economy.md` (found while: ending pointless resupply errands, branch `auto/supply-errands`)

`SupplyProvider.Tick` returns early — no target, no delivery — whenever `currentSupply < Info.RestockThreshold && currentTarget == null && !KeepServingBelowThreshold()` (`:254`, mirrored in `CanServeNow` at `:957`). `RestockThreshold` defaults to **50** (`:38`) and `KeepServingBelowThreshold()` is `Info.EvacuateOnUnusableResidue && !ShouldSelfRestock()` (`:385-388`), and `EvacuateOnUnusableResidue` is true **only on TRUK** (`vehicles.yaml:549`). SUPPLYCACHE (`misc.yaml:408-421`) sets neither, so it inherits the truck-shaped 50-supply reserve while having nothing to reserve it for.

The result is a crate that stops serving at 49 supply and then cannot reach its own `RemoveBelowSupply: 1` despawn either (that check needs `currentSupply < 1`, and supply only falls in `ResupplyTarget`, which is no longer reached). It sits on the map inert and permanent, holding ~49 supply that only an `AbsorbsSupplyCache` LC or an enemy capture can recover.

`economy.md` states the opposite twice and is the authority per its own header: *"Serves down to empty — `RemoveBelowSupply: 1` … A stationary cache has no drive-home trip to reserve supply for (unlike TRUK's `RestockThreshold`), so the threshold is 1"*, and *"serves down to the last usable batch, then despawns or is captured"*. So the doc describes the intent and the code is what needs to change.

Found because a scenario staging a low-supply crate silently measured nothing: the crate served zero rounds and the test failed with a setup diagnostic rather than the behaviour under test.

**Fix shape:** `RestockThreshold: 0` on SUPPLYCACHE (one YAML line, matches what the crate already means), or widen `KeepServingBelowThreshold` to admit any provider with no `RestockActors`. The second is the more general statement — "a provider with nowhere to restock has no trip to reserve for" — and would also cover a future stationary provider. Needs a scenario asserting a sub-50 crate still hands over a batch; `test-errand-ends-when-rearmed-en-route` already overrides the field in its own rules and would be the natural place to stop doing so once fixed.

## 2026-08-11: [high] Cancelling a truck's restock drive latches `SupplyProvider.restocking` TRUE forever, and a latched truck serves nobody for the rest of the match (found while: ending pointless resupply errands, branch `auto/supply-errands`)

`SupplyProvider.TryRestock` sets `restocking = true` (`:774`) and then queues a four-part chain: `QueueActivity(false, move.MoveTo(host))`, `Wait(25)`, a `CallFunc` that transfers supply **and clears the flag** (`:798`), and optional rally-point moves. `restocking = false` appears at exactly one line in the file, inside that `CallFunc`. There is no `INotifyBecomingIdle` reset, no cancel hook, no `ITick` re-check.

`Activity.Cancel(self, keepQueue: false)` nulls `NextActivity` (`Activity.cs:198`), and `Actor.QueueActivity(false, …)` cancels the current activity — so **any** order that pre-empts the drive (a player Move, `DropsSupplyCache`'s `RotateToEdge` evac, a bot re-task) drops the `Wait`/`CallFunc` tail and the flag is never cleared.

The consequence is not cosmetic. `CanServeNow` returns false while `restocking` (`:943-945`), and `CanServeNow` is the provider's whole serving ladder — it is asked by `SupplyProvider.Tick`'s own early-outs and by `AutoSeekSupplies.CanServe`, which is the ONE eligibility predicate both the soldier's provider scan and `SeekSuppliesAndReturn`'s per-tick re-check share. So a truck that was interrupted once on the way to an LC stops serving infantry, stops being selected as a destination, and never recovers — while looking perfectly healthy (it still has supply, its bar is amber not red, and `CountsAsEmpty` is false so the evac path does not dispose of it either).

Same latch shape as the `moveQueued` entry above: state set alongside a queued activity, cleared only on that activity's success path. Worth stating as a class — **a flag that records "an activity is in flight" must be cleared on the activity ENDING, not on it SUCCEEDING**, because cancellation is the common case in an RTS.

**Directly load-bearing for the queued "truck runs dry mid-move → cancel the move so auto-return fires" work.** That item needs to tell "this move is invalidated by being empty" from "this move exists to stop being empty", and `restocking` is the only place a truck's restock intent is recorded today — the drive itself is a **bare `Move`**, with no distinct activity type, so it is indistinguishable by type from a player order. Note what the ammo side does instead: `AmmoPool.IsSeekingRearm` (`AmmoPool.cs:390-397`) answers the same question by walking the whole activity queue for `SeekSupplyProvider | Resupply | RideTransport | SeekSuppliesAndReturn` — TYPE-based, so it cannot latch and cannot survive cancellation. Any cancel-the-move design that gates on `restocking` as it stands will both mis-fire and permanently disable trucks.

**Fix shape:** clear the flag from an activity-end hook rather than the success `CallFunc` (or give the restock drive its own activity type and derive the state from the queue, as the ammo side does). Not fixed here — out of scope for this branch, and it wants a scenario that cancels a restock drive and asserts the truck still serves.

## 2026-08-11: [low] ~38 server/lobby notification strings in `mods/ww3mod/languages/en.ftl` are orphans — the live keys carry a `notification-` prefix and live in the engine's common.ftl (found while: NAT/port-forward diagnostics, branch `wt/nat-diagnostics`)

Every key from `timeout-in` (`mods/ww3mod/languages/en.ftl:84`) through `chat-temp-disabled` (`:129`) is unreferenced: none of those bare names appears as a string literal anywhere in `engine/OpenRA.Game/` or `engine/OpenRA.Mods.Common/`. The strings players actually see come from `engine/mods/common/fluent/common.ftl:60-90`, prefixed `notification-*`, which ww3mod loads via `mod.yaml:220`. Upstream renamed these keys; the mod copy never followed.

Impact is low today because the texts are near-identical to upstream's, but it is a live trap: **any future edit to a lobby/server notification made in the ww3mod file is a silent no-op.** The lint is not blind to it — `CheckFluentReferences.CheckUnusedKey` (`Lint/CheckFluentReferences.cs:447-455`) emits ``Unused key `no-port-forward` in ww3mod|languages/en.ftl`` for exactly this, and it fires on every non-external file (`:437`) — but it is `emitWarning`, so `--check-yaml` passes unless `TREAT_WARNINGS_AS_ERRORS=true` (`UtilityCommands/CheckYaml.cs:51`), and it is one of hundreds of unused-key warnings nobody reads. Eight of them — `banned`, `temp-banned`, `full`, `number-teams`, `blacklisted-title`, `requires-forum-account`, `no-permission`, `timeout-in` — have no same-stem twin because upstream also reworded them (`notification-blacklisted-server-name`, `notification-requires-authentication`, `notification-no-permission-to-join`), so if any of those were deliberate WW3MOD rewrites they have never shipped.

**Fix shape:** delete the dead block, or rename each key to its `notification-*` form so the mod file genuinely overrides. Decide per key whether the ww3mod wording was intentional first — a blind rename would resurrect ~38 strings at once and change lobby text everywhere.

## 2026-08-12: [med] Every `Resupply` the ammo path dispatches uses `closeEnough = WDist.Zero`, and no unit can ever stand on a building's centre — so the dock-and-rearm PULL at a Logistics Centre can never complete (found while: ending the LC half of the resupply-errand report, branch `auto/lc-errand`)

Static reasoning, **not yet observed in play** — recorded as a hypothesis with its derivation, not as a confirmed defect.

`AmmoPool.AutoRearm` picks the activity's arrival tolerance with `var closeEnough = rearmsUnits != null ? rearmsUnits.Info.CloseEnough : WDist.Zero;` (`AmmoPool.cs:371-372`). **`RearmsUnits` appears in no mod YAML file at all** (`git grep RearmsUnits mods/ww3mod/rules/` is empty), so that ternary always takes the `WDist.Zero` branch — for the LC and for anything else this path ever resolves.

`Resupply` then tests arrival as `(host.CenterPosition - self.CenterPosition).HorizontalLengthSquared <= closeEnough.LengthSquared` for any client without `RepairableNear` (`Resupply.cs:112`), i.e. `<= 0`: exact coincidence with the host's centre. The LC is a 3×3 `Building` whose footprint blocks its own centre (`Footprint: =+= +++ =+=`, `structures.yaml:361-363`), so no ground unit can occupy that position. `isCloseEnough` is therefore permanently false, `actualResupplyStarted` is never set, `rearmable.RearmTick` never runs, and the activity cannot reach its `activeResupplyTypes == 0` exit.

Two things make this survivable today and are probably why it has gone unnoticed:

- Units near an LC rearm through a **different mechanism entirely** — the LC's `ProximityExternalCondition@ReplenishSoldiers` grants `replenish-soldiers` within 4c0 (`structures.yaml:381-384`), which is the `RequiresCondition` on infantry's own `ReloadAmmoPool@1` (`infantry.yaml:1162-1164`); vehicles are served by the `SupplyProvider` push gated on `unit.docked` within 2c0. So the ammo does arrive; it is the *activity* that never notices.
- `AutoSeekSupplies.ReturnErrandStallTicks` cancels the resulting non-terminating errand after 300 ticks — but **only** for errands that trait dispatched, not for `AmmoPool`'s own `INotifyBecomingIdle` dispatch (`AutoSeekSupplies.cs`, `TickErrand` is only entered after `BeginWatching`).

The `auto/lc-errand` fix incidentally covers the **dry** instance: once the 4c0 aura hands over a round, `!AllPoolsEmpty` clears the `Rearm` bit and the unit goes home instead of grinding at the footprint edge. It does **not** cover a player's explicit Resupply order on a partially-full unit, which still has no terminating condition at an LC. **Fix shape:** give the LC a `CloseEnough` (dock-adjacent, e.g. 2c0 to match its own `unit.docked` aura) rather than inferring `WDist.Zero` from a trait the mod does not use — but confirm the symptom in play first, since the proximity mechanisms above may make the pull path entirely vestigial, in which case the honest fix is to stop dispatching it.
**FIXED on `auto/truck-doctrine`,** by the second of the two shapes above. The drive is now a named `RestockSupply` activity (drive + settle + transfer, composed with `QueueChild` so cancellation takes the whole thing), and `SupplyProvider.Restocking` walks the activity queue for it instead of reading a field — the `bool restocking` is gone. **The queue walk must cover `NextActivity`, not just the head**, for the same reason `AmmoPool.IsSeekingRearm` does: `QueueActivity(false, …)` *cancels* the current activity rather than removing it, so the dying activity stays HEAD while the newly-queued restock sits behind it, and a head-only test answers "no" during exactly the window that matters. Both restock call sites were converted, not just the one that set the flag — `DropsSupplyCache.QueueDriveAndRestock` built the identical move/wait/CallFunc chain in a second file, and leaving it untyped would have made "is this truck refilling?" answerable for one drive and not the other. Pinned by `test-truck-restock-survives-cancel`; its RED verdict was "The truck never went back to the depot after its restock drive was cancelled". A useful side effect: `TryRestock`'s own re-entrancy guard reads the same property, so a truck can no longer queue a second drive on top of a first.

## 2026-08-12: [med] 402 of the mod's 496 `--check-yaml` errors are one 3-error defect, multiplied by the per-map lint re-run (found while: explaining a +3 error delta on `wt/autotarget-preempt`)

**Verified by four `--check-yaml` runs, not inferred.**

`CheckYaml` re-runs the ENTIRE rules lint once per map that defines custom rules (`Lint/CheckYaml.cs:97-105` enumerates every map; `:133-135` calls `CheckRules(modData, map.Rules)`), and `errors` at `:31` is a flat counter with no deduplication. With 135 rulesets, any error in the mod's default ruleset is counted 135 times.

The defect: `Sellable` on `^Building` consumes `!being-captured` (`mods/ww3mod/rules/ingame/structures.yaml:116`), whose only granter is `CaptureManagerInfo.BeingCapturedCondition` (`engine/OpenRA.Mods.Common/Traits/CaptureManager.cs:34-36`, wired at `structures.yaml:151`). GTWR, PBOX and HBOX each strip the granter with `-CaptureManager:` (`structures-defenses.yaml:81`, `:169`, `:253`) while keeping the inherited consumer. That is 3 errors × 134 map rulesets = **402 of the 496 total**.

**Consequence worth more than the fix:** the error COUNT is not a usable signal and is not stable across branches — *any* branch that adds one autotest scenario with custom rules raises the total by exactly 3, with no code involvement. A `+3` delta was misdiagnosed twice on `wt/autotarget-preempt` (once as map-cordon errors, once as an engine regression) before this was run down. **Diff the error LIST, never the count.**

A related trap that cost a full investigation cycle: the autotest scenario folder is registered as a map source from `tools/`, not `mods/` (`mods/ww3mod/mod.yaml:96`). An isolation experiment that swaps the `mods/` directory to test "is this YAML or C#?" therefore CANNOT remove a scenario map, and will exonerate the map no matter what — a clean result from an experiment with no power to produce a dirty one.

**Fix shape:** override `Sellable.RequiresCondition` on GTWR/PBOX/HBOX to drop the `!being-captured` term (they cannot be captured). Takes the total from 496 to 94. One line per actor.

**FIXED at `4d3c8f90` and the prediction held — but these numbers get quoted as if current, so, as of 2026-08-17, both measured: the mod-wide total is 87 locally and 437 in CI, and `437 = 87 + 350`.** The 350-error gap is `sprite file X not found` for the Red Alert content the CI runner never installs (CI runs `31981227086` / `31978609314`; local `make test` on `wt/lint-baseline`). Same check, same single mod, not comparable counts. The 87 is now recorded entry by entry in `mods/ww3mod/lint-baseline.txt`, which makes **"diff the LIST, never the count"** enforceable rather than advisory: `--check-yaml` does the diff itself and fails on anything unrecorded.

## 2026-08-12: [low] Five `pips` sequences point at `pips.shp`, which this mod does not ship — one of them is live, and the primary-building tag has never rendered (found while: adding the holding-fire marker, PIPELINE 44a)

In `mods/ww3mod/sequences/sequences-misc.yaml` the `pips:` set contains five entries with **no filename after the colon** — `groups`, `medic`, `tag-fake`, `tag-primary`, `tag-hold`. A filename-less entry falls back to the set name, i.e. `pips.shp`. That file exists **only** at `engine/mods/cnc/bits/pips.shp`; WW3MOD's package list mounts `ww3mod|bits/units/pips` (`mod.yaml:54`), which contains `pips2.shp`, `pip-ammo.shp`, ~~`pip-cover.shp`~~ … but **no `pips.shp`**. (**Correction 2026-08-17:** `pip-cover.shp` is not there either — it exists nowhere in `mods/`, `engine/mods/` or the RA content, and a local `make test` reports it missing alongside `b2bomb.shp`, `pip-cloak.shp`, `mslo.int` and `bib3.int`. Those five are the only sprite errors that survive with content installed.) Entries that name a file are fine — the blue ammo pips (`pip-blue` → `pips2`) render correctly.

**Live consequence:** `WithDecoration@primary` on `^PrimaryBuilding` (`rules/ingame/structures.yaml:141-147`) uses `Image: pips` / `Sequence: tag-primary`. The "this is your primary building" tag therefore draws nothing and, as far as this branch can tell, never has. Not verified in play — found by asset inspection.

**The trap:** a missing sprite file here produces **no load error and no crash**. The decoration is simply invisible, which is indistinguishable from the trait not being attached, the condition never being true, or the fix not working.

**Scope correction, so this entry does not overclaim its own origin.** The holding-fire marker was written against `tag-hold` and its screenshots came back empty, which is what sent me looking at the assets — but the missing file was **not** what blanked those screenshots. A trace later showed the shots were firing one tick before the marker went live, and two builds with *different* sequence names had produced byte-identical frames, which is only possible if neither was drawing yet. So the `pips.shp` gap is real and `tag-primary` is genuinely broken by it, but that rests on **asset inspection, not on the blank screenshots**. Two true findings, only one of them causal — worth separating, because "the sprite was missing" is the tidier story and it is the wrong one.

**Fix shape:** either ship a `pips.shp` in `bits/units/pips/`, or repoint `tag-primary` at a sequence backed by an existing file. A PITFALL anchor now sits at the top of the `pips:` block so the next person picking a sequence name sees it before choosing.

## 2026-08-14: [high] Bot map-players have no economy at all — the tournament harness has been benchmarking broke bots since it was written (found while: recon into "@experimental has no money after its opening")

Full measurement and falsification in `WORKSPACE/DISCOVERIES.md`, 2026-08-14. Summary and fix options here.

`PlayerResources.Tick` gates passive income, building income and upkeep on a single `if (self.Owner.Playable)` (`engine/OpenRA.Mods.Common/Traits/Player/PlayerResources.cs:201-202`). `PlayerReference.Playable` defaults to **false** (`engine/OpenRA.Game/Map/PlayerReference.cs:24`) and map players copy it verbatim (`engine/OpenRA.Game/Player.cs:196`), while lobby-slot players keep the `true` field initialiser (`Player.cs:63`). Every one of the 31 `tournament-*` scenarios declares its bots as map players, so **both bots in every tournament match earn nothing all match** and spend only their `DefaultCash: 7500` opening allocation. Measured: `spent` freezes at 7500 by tick 640 and never moves through tick 4800; `earned=0` at every snapshot; the Observer, being `Playable`, banks 8700 in the same match.

**Severity is high because of what it silently invalidates**, not because the shipped game is broken — it is not: no shipped map declares a `Bot:` map player, and skirmish bots sit in lobby slots and do have an economy. What is affected is the measurement apparatus, including all nine `tournament-s1-eco-*` scenarios.

**Fix options, in increasing order of blast radius — none applied here, this was a recon item.**

1. **Scenario-side, narrowest.** Nothing available: setting `Playable: True` on the bot map players is what the harness is deliberately avoiding, and it does not work — tried it, and the local client is immediately assigned into the bot's slot (the run logs `player=FreadyFish` in place of the bot). The scenario header already warns about exactly this. Dead end, recorded so it is not re-tried.
2. **Engine-side, narrow and probably correct.** Change the gate to `self.Owner.Playable || self.Owner.IsBot` (or, more precisely, `!self.Owner.NonCombatant && !self.Owner.Spectating`). Measured effect on `tournament-arena-composition-2p`: `earned` 0 → 6074, `spent` 7500 → 13570 by tick 4800. **Cost:** it also starts charging upkeep to bots that have never paid it, so every tuning constant in the composition layer that was fitted against a no-upkeep, no-income economy is re-opened at once — this is a re-baseline, not a drop-in. Note it would additionally stop paying the `Observer`, which is arguably a separate latent bug (a spectator currently accrues cash).
3. **Decide the intent first.** Worth settling whether a map-player bot *should* have an economy before picking 2 — the gate may have been written to keep Neutral/`Everyone` pseudo-players out of the economy, in which case the right predicate excludes those explicitly rather than testing `Playable`.

**Do not re-baseline any bot benchmark against pre-2026-08-14 tournament numbers.** They were taken in a different economy from the one a real match runs.

**Blast radius, re-derived on review — it is wider than "the 31 tournament scenarios" above.** **88 bot map-player entries across 46 files**, every one `Playable: False` and `NonCombatant: False`. 31 files are `tournament-*`; **15 are ordinary scenarios** (13 graded behavioural tests + 2 non-graded demos). Counting method and its cross-checks are in `DISCOVERIES.md` 2026-08-14.

**FIXED 2026-08-14 on `wt/econ-gate` off `main` @ `2c274589`.** Gate is now `self.Owner.Playable || (self.Owner.IsBot && !self.Owner.NonCombatant)`. Option 3 was taken first and it changed the answer to option 2: `Playable` is **not** a participation flag, it is the flag `CreateMapPlayers` partitions lobby-slot players from map players on (`CreateMapPlayers.cs:93-117`), and on shipped content it is exactly coextensive with `!NonCombatant`. So the recon's `!NonCombatant && !Spectating` alternative was **rejected** — `Player.Spectating` is dynamic (`Player.cs:86`: true once `WinState != Undefined`, and suppressed entirely on `MissionSelector` maps), so it would have stopped paying a human the moment they won or lost, a live behaviour change for a shipped human. The shipped predicate keeps `Playable` as the first disjunct and is therefore **monotone**: it can only open the gate where it was closed, and for any lobby-slot player it short-circuits before the new term is reached. Full before/after measurement in `DISCOVERIES.md` 2026-08-14 (second entry). **@stable gains an economy too — that is intended, and the benchmark control has changed.**

## 2026-08-14: [low] Bot map-players get no starting units either — `SpawnStartingUnits` gates on `Playable`, and `StartingUnitsClass` on a bot map-player is dead config (found while: reviewing the blast radius of the economy gate above)

**Deliberately NOT fixed** — recorded because it is the *same defect in the same blast radius* as the economy gate directly above, and the next person to trip over it should find it here rather than re-derive it.

`SpawnStartingUnits.WorldLoaded` iterates `world.Players` and calls `SpawnUnitsForPlayer` only `if (p.Playable)` (`engine/OpenRA.Mods.Common/Traits/World/SpawnStartingUnits.cs:69-71`). Bot map-players are not `Playable` (see the entry above for why), so **they receive no starting units at all** — and **19 `tournament-*` scenario files declare `StartingUnitsClass: motorized` on exactly those players, which does nothing.**

**Severity is low and the reasoning matters:** under the WW3MOD Supply Route model there is no build-up phase to seed — bots call in everything from their `DefaultCash` allocation — so no bot is missing units it would otherwise have had, and no measurement is invalidated by this the way the economy gate invalidated the benchmark corpus. The cost is purely that a scenario author who writes `StartingUnitsClass` on a bot and expects units will lose an afternoon, and that the config reads as live when it is inert.

**Fix shape if ever wanted:** identical to the economy gate — `p.Playable || (p.IsBot && !p.NonCombatant)`. **Do not apply it casually:** unlike the economy change this one is *not* inert for existing scenarios. It would hand every bot map-player across all 46 files a free opening force it has never had, which moves the opening of every tournament scenario at once and would invalidate any baseline taken between the economy fix and it. If it is done, it should be done deliberately and before a re-baseline, not after.

## 2026-08-14: [high] CANDIDATE — the supply-truck mode selector fires DANGEROUS-front doctrine on a front with zero believed danger, which is PIPELINE item 56's suspect seen from the opposite side (found while: before/after regression sweep for the economy gate)

**This is an OBSERVATION, not a diagnosis. Nobody has traced which code site fired.** Filed at high severity because it bears directly on PIPELINE item 56 — the highest-priority item in the queue, which has had four merges thrown at it without a confirmed mechanism.

`test-supply-safe-front-keeps-cargo` fails, and it fails **identically with and without the economy fix** (paired same-seed runs, `seed 5002`), so this is **pre-existing and unrelated to the economy change**. The scenario's own failure note states the conditions precisely:

> a supplycache was dropped on a front with no believed enemy: first seen at 39,16 after 21s … the truck emptied itself … instead of keeping its remainder for the next platoon. truck went from x=14 to a furthest x=39 and is gone; platoon is at x=44, **no enemy actor exists and believed danger is 0 everywhere**.

The two doctrinal modes (`DOCS/reference/economy.md`; PIPELINE item 56 "The two modes, so nobody re-derives them") are: **dangerous front** → stop `DropShortCells` short, unload the whole 750 as a SUPPLYCACHE, egress. **Quiet front** → close to the aura, serve in place, **keep cargo**. What the run shows — drove nearly onto the platoon, unloaded a cache, emptied itself, left — is the **dangerous-front** branch executing on a front where **no enemy actor exists and the believed-danger field reads 0 everywhere**. There is no input under which the dangerous branch is the correct selection here.

**Why this is worth more than one more red test.** Item 56 is chasing the same selector from the *opposite* direction: its reported symptom is "the truck drives up and does not drop", and its stated leading suspect is site 4, `SupplyDropMath.DangerSelectsDrop` (`SupplyDropMath.cs:388`), whose quiet-front branch is "close to the aura, serve in place, keep cargo". **This run is the same selector producing the inverse error — dropping when it must not.** A selector that can fail in both directions is a much stronger signal than either symptom alone, and it argues the defect is in the *selection*, not in the drop or the follow logic.

**What has NOT been established, and it is the whole next step:**
- **Which of the seven danger sites fired is untraced.** Site 4 is the natural suspect because it owns mode selection, but that is inference from the symptom, not evidence.
- **Site 7 is the trap and no flag reaches it.** `:2152 FindSafeFollowPosition` reads **`ThreatMapManager`, not `DangerFieldLayer`** (item 56's own verified finding). So "believed danger is 0 everywhere" — which is a statement about `DangerFieldLayer` — **does not rule out a non-zero threat reading at site 7**, and anyone who disables "danger awareness" via the documented flags will not touch it. A trace must record *both* fields, or it will produce a confident wrong answer.
- **Next step:** trace which site set the mode on this exact scenario+seed, logging the `DangerFieldLayer` and `ThreatMapManager` values that fed it. The scenario is cheap, deterministic at `seed 5002`, and currently RED — an unusually good instrument for this. Nobody has done it.

**Status change for the test itself (see PIPELINE item 51).** `test-supply-safe-front-keeps-cargo` was filed there as a suspect oracle — "it passes for the right reason but does not assert it". It is now **failing for what looks like a real reason**, which promotes it from suspect-oracle to useful-signal. Item 51's hardening work is still worth doing, but the test should no longer be treated as untrustworthy by default.

---

### [high] Supply trucks are never procured: the fleet is sized by `starving`, but `starving` reads 0 while `ammo-need` reads True — 2026-08-15

**User report (live):** *"There are a lot of soldiers now that are out of ammo but still I see almost no supply trucks being built... when units are out of ammo it should prioritize supply trucks... soldiers out of ammo are useless. That should be the first priority to solve at all times."* **Treat that last sentence as a precedence ruling, not a weight.**

**MEASURED**, `tournament-arena-composition-2p`, @experimental mirror, full 6-minute match, `[composition]` census + the new `[composition] pick lane=` line:

- **ZERO trucks ordered by any lane, all match.** `type=truk` appears in no pick line at all.
- `ammo-need=True` **continuously from tick 1240 to the end of the match** — the demand signal is live and correct.
- `starving=0` and `trucks-desired=0` at **every single snapshot**, including all of the above.
- cash collapses to ~0 by tick 760 and stays there (43 / 121 / 95 / 64 / 40 / 9 / 3).

**Mechanism.** Two predicates that are supposed to describe the same fact use different thresholds:
- `AnyFieldedUnitNeedsResupply()` (`UnitBuilderBotModule.cs:710`) mirrors `SupplyProvider`'s `MinNeedThreshold` — a weighted missing/capacity ratio. This is what sets `ammo-need`, and it is **True**.
- `CountStarvingCustomers()` (`:798`) uses `SupplyHuntMath.BelowSeekThreshold(..., SupplyStarvingThresholdPerMille)` with **`SupplyStarvingThresholdPerMille: 250`** (`ai-america.yaml:223`) — a unit counts only below **25%** ammo.

`SupplyFleetMath.DesiredTrucks` is fed **`starving`**, not `ammo-need`. With `starving = 0` and **`SupplyTruckFloor: 0`** (set at `56bf7355`, correctly, to kill the t=0 truck), `desired = 0`, so the supply pre-empt **never fires**. The truck's only remaining route is the deficit argmax — and `truk` is 40‰ of army VALUE at cost 1000, so V_fit is **25,000**: `ApplyCeilingEligibility` strikes the slot until the army is worth that much, which it never is. **Both routes are closed simultaneously, for unrelated reasons.**

**Reconciling the prior conclusion, which does NOT survive.** `56bf7355` concluded *"the gate is fine; the bot is broke"* from `cash=0` on 194/195 snapshots. That was measured **before** `b91b5a88` gave bot map-players an economy. Post-fix the bot **does earn** (`earned` accrues from tick 80 in this run), yet still buys no truck — so **affordability was never the whole story**, and low cash is now a *consequence* of spending on the argmax's picks rather than the cause. Note also that `b91b5a88` states the shipped game was never affected (skirmish bots occupy lobby slots and are `Playable`), so **the user's own live games always had an economy** — the "bot is broke" finding never explained the user's experience at all.

**Why this is the same defect as the medics, inverted.** The system has a notion of HOW MANY (a fleet size, a share) and no notion of WHEN or of PRECEDENCE. The medic had a floor with no denominator and so arrived first; the truck has a denominator that never becomes non-zero and so never arrives. The user's ruling supplies the missing axis: **dry units are the top procurement priority whenever the need exists.** That wants a pre-empt keyed to `ammo-need` (the same signal `SupplyProvider` acts on), ordering ahead of the composition argmax and exempt from the value-share ceiling — not a larger constant, and explicitly **not** a restored `SupplyTruckFloor`, which is the t=0 bug the user reported first.

**Suggested acceptance bar (the user asked for responsiveness, not a count):** ticks from *first unit below the resupply threshold* to *first truck ordered*, plus dry-unit count over time. A match in which nothing goes dry is instrument failure, not a negative result.

**Not attempted here** — this is a second, separable mechanism from the support-floor scaling on `wt/build-order`, and the test-run budget for that branch was spent. Filed with the measurement so the next branch starts from data.

### [info] First positive live report on delivery conduct (PIPELINE item 56) — 2026-08-15

Same user report, and it should not get lost inside the procurement complaint: *"When I saw it being built it seems like it **correctly went to resupply them**."* Item 56 (truck delivery) is the highest-priority queue item and has previously been described by the user as long-broken. This is the first live statement that the truck's **conduct** is right. It isolates the remaining problem cleanly: **procurement, not delivery.** Not a verdict on item 56 — one observation, no trace of an individual delivery — but it is evidence pointing the encouraging way, consistent with the 5-alive/3-eligible reading already banked in `DISCOVERIES.md` for `b91b5a88`.

### [bug] Transport helicopters have the same half-empty departure asymmetry the ground carriers just had fixed — 2026-08-15

Found on `wt/transport-loading` while fixing the ground module; **not fixed there**, because the test-run
budget covered the ground path and the two share no code beyond the pattern.

`TransportLoadMath.Decide` (`HelicopterSquadBotModule.cs:1862-1871`) dispatches a lift as soon as
`passengersAboard >= minPassengers` (`TransportMinPassengers: 4`, `ai.yaml:1676`/`:1765`). The number
of soldiers actually ordered aboard is `TransportEmploymentMath.LoadCap`
(`TransportEmploymentMath.cs:138-154`) = `min(maxInfantry, cargoMaxWeight)` floored at the minimum —
so whenever the doctrine cap exceeds 4 the heli lifts off with 4 while the rest are still walking,
exactly as `MountedTransportBotModule` did before `FillBeforeDeparture`.

The heli path is **less exposed than the ground one was**, because it already stands its stragglers
down on both task exits (`StandDownStragglers`, `:1229`), so they release their cargo reservations
rather than pinning the airframe's pickup lock. The cost is therefore a thin load, not a stuck
transport.

Fix shape, if picked up: `MountedTransportMath.DecideDeparture` is a pure function and already carries
the seat-target / still-coming / stall / timeout logic with its no-hang invariant NUnit-pinned. The
heli would need the same `SeatTarget` and `LastBoardingTick` bookkeeping on its task record, then a
call swap. Behavioural, so it needs a default-false field per the `@stable` policy.

### [bug] The capture ferry's drop site bypasses the danger standoff entirely — and now carries three more soldiers into it — 2026-08-15

Pre-existing; **stakes raised** by `wt/transport-loading`, which is why it is being recorded rather
than left implicit. Not fixed there.

A frontline delivery runs its drop cell through `ApplyStandoff`
(`MountedTransportBotModule.cs:866-899`), which walks the cell back toward our SR until the believed
anti-ground danger clears, plus a margin. A **capture ferry does not**: `TryReserveCaptureFerry` sets
`DropOff = target.Location` directly and never calls `ApplyStandoff`. The carrier therefore drives to
the capture target's own cell whatever the believed danger there, and the standing user constraint
that transports be **route AND drop-site danger-aware** (2026-08-11) is unmet on this path.

Until now the exposure was one technician. With `CaptureFerryEscortSeats: 3` on both twins it is a
technician plus three riflemen plus the carrier — one AA/AT hit now costs five units instead of two.
The change did not create the gap and does not widen the danger; it widens what is standing in it.

Shape of a fix, if picked up: the ferry cannot simply reuse `ApplyStandoff`, because backing the drop
off toward the SR would defeat the ferry's whole purpose (the capturer must reach the target). More
likely the standoff belongs on the **approach**, not the destination — or the ferry should decline the
ride and let the technician walk when believed danger at the target exceeds a bar. That is a design
call, not a tuning one. Note the danger field is currently mis-scaled (pipeline item 40), so prefer a
formulation robust to its scale being wrong.

### [bug] Ferry escorts and walking escorts are recruited independently, so infantry committed per capture rose by roughly the seat count — unmeasured against starving the offense — 2026-08-15

Pre-existing structure; **new magnitude** from `wt/transport-loading`. Not fixed there.

`IssueCaptureOrder` (`CaptureCoordinatorBotModule.cs:1667-1695`) calls `TryFerryCapture` first and
`DispatchEscort` second, as alternatives for the *capturer*'s movement only — nothing couples the two
for support units. So a capture that takes a ferry now commits **both** the ferry's escort seats
(`CaptureFerryEscortSeats: 3`) **and** a separately recruited walking escort (`EscortSize: 2`, or
`ContestedEscortSize: 4`), where before this change it committed only the walking escort.

There is no double-booking of individual units — `FindIdleSupportersNear` (`:2100-2130`) requires
`IsIdle` and excludes ledger-committed actors, and the ferry's escorts are both non-idle (they hold an
`EnterTransport`) and committed under `transport:<carrierId>` — so the two sets are disjoint. The
concern is not correctness but **total spend**: up to ~5 infantry diverted onto one capture instead of
~2, against an offense that draws from the same free pool.

Unmeasured, and deliberately so: quantifying it needs a full-length match with captures actually
firing, which the branch's run budget did not cover. If the offense looks thin in a benchmark after
this lands, this is the first thing to suspect. The cheap lever is `CaptureFerryEscortSeats`, which is
per-profile and defaults to 0.

### [bug] `test-combined-arms-rendezvous` still fails on the tank-death abort — the two-t90 reduction committed at 7f8c2d41 was unverified, and it did not work — 2026-08-15

Run granted specifically to check whether `FillBeforeDeparture` (`wt/transport-loading`) pushed this
scenario past its 200 s deadline. **It did not, and the failure is unrelated to that branch.**

Verdict: `fail: the bot's tank died before the rendezvous could be judged` (seed 1017, run
`260815_115851_p60033`). That is the scenario's own early-abort at
`test-combined-arms-rendezvous.lua:69`, not a deadline overrun.

**This is the exact failure `7f8c2d41` tried to tune out and shipped without exercising.** Its commit
body records: *"Four t90s placed to populate the control field killed the lead abrams before the
assertion could be judged … Reduced to two at the far corner … This last change is UNVERIFIED tuning;
the run budget was exhausted before it could be exercised."* The map file carries the same note inline
(`map.yaml:127-133`) and asks whoever runs it next to confirm the abrams survives. **It does not.** Two
t90s at the far corner still kill it. The belief source still wins the fight it was only supposed to
populate a field for.

**Why the transport branch is excluded as a cause, on evidence rather than argument.** The whole run
logged `tasks-active=0` — every scan, both players — with zero `[exp-transport] depart`, zero
`ferry-escort` and zero `mounted-transport` lines. No `CarrierTask` was ever created, so the Loading
state that `FillBeforeDeparture` governs was never entered and the lever had no opportunity to delay
anything. The mechanism by which the branch could plausibly have broken this scenario provably did
not occur.

**FALSIFIED the same day — see the next entry.** The hypothesis below was instrumented and tested and
is **wrong**. No boarding order was ever refused; the module never reaches the boarding loop at all,
because it cannot compute a drop-off cell. Kept rather than deleted so the reasoning stays visible:
the signature was real and correctly noticed, but the inference drawn from it was the *available*
explanation rather than the demonstrated one — the eligibility collapse to `0` is offense recruiting
those soldiers, which is true and irrelevant, because it happens after the transport has already
given up for an unrelated reason.

**A second, separate observation from the same log, worth its own look.** The transport reached
`carriers-candidate=1` with `passengers-eligible=4` then `5` — a carrier and enough infantry — and
still created no task. Eligibility then collapsed to `0` for several scans before climbing again as
fresh units spawned, which is the signature of the offensive layer walking those same soldiers out of
the reserve bubble. The likely mechanism is the one `wt/transport-loading` hit and fixed on the ferry
path: the frontline boarding order is `BotOrderDamping.Recurring`, and `BotOrderQueue.Admit`'s dwell
rule (`OrderArbitrationMath.cs:561-572`) drops a Recurring order to any actor holding a recent standing
record — which fresh infantry near the SR always has. `CommitPassengers` cannot help here, because the
commit happens at task creation and no task is created. **If that is right, the ground transport rarely
runs at all, which would explain why the rendezvous has never once been exercised in four runs across
two branches.** Stated as a hypothesis: the refusal path has no log line, so this run cannot prove it.
Cheapest next step is a one-line counter at the `boarding.Count == 0` continue in `TryAssignNewTasks`
— then a single run says yes or no.

### [bug] The ground mounted transport cannot pick a drop-off cell before first contact, so it never runs — and `DeliverBeforeContact` / the combined-arms rendezvous are unreachable dead config — 2026-08-15

**Measured, and it falsifies the dwell-suppression hypothesis in the entry above.** Instrumented run
`260815_121127_p61315` (test-combined-arms-rendezvous, seed 1017) named which of the three silent
exits in `TryAssignNewTasks` fires. Across the whole match, both players:

```
reason=no-drop-cell   15
reason=orders-refused  0
reason=too-few-pax     0
```

Every pass died at `if (!dropOff.HasValue) return;`. Not one boarding order was ever issued, so the
arbitration gate cannot be the blocker — the module gives up two gates earlier than suspected. Sample:
`no-task player=USA-bot reason=no-drop-cell carriers=1 eligible=4 tick=15`, then `eligible=5 tick=65`,
and later `carriers=2 eligible=7 tick=465`. A carrier and seven eligible passengers, and still no task.

**Mechanism, provable statically — no further run needed.** `PickDropOffCell`
(`MountedTransportBotModule.cs:961-1007`) reaches its pre-contact fallback only on two conditions:

1. `influenceMap == null` — the `InfluenceMap` trait is absent from the world entirely; or
2. `frontline == null`.

**Condition 2 can never hold.** `InfluenceMap.GetFrontline` (`InfluenceMap.cs:170-175`) returns
`InfluenceMapMath.DeriveFrontline`, which unconditionally allocates `new bool[w, h]` and returns it
(`:248-262`). It has no null return. So with the trait present — i.e. in every real game — the
frontline is always a non-null array, and **before contact it is simply all-false**. The scoring loop
then never assigns `best`, and the method ends at `return best.HasValue ? ApplyStandoff(...) : best`
— returning **null** rather than falling back. The fallback is only wired to the frontline being
*absent*, never to it being *empty*, which is precisely the pre-contact state it was written for.

**Consequences, in order of how much they cost:**

- **The ground transport does nothing at all until first contact**, no matter how many carriers and
  passengers are available. That is the whole of the observed `tasks-active=0`.
- **`DeliverBeforeContact` is dead config on both twins.** Its `[Desc]` says it exists so that "when
  no frontline contact exists yet, still deliver toward a forward staging cell instead of sitting idle
  until contact" — the exact behaviour it cannot produce. Both twins set it true (`ai.yaml`), and it
  has no effect.
- **`PreContactStagingPct` is likewise dead**, as is everything downstream of it.
- **The combined-arms rendezvous is wired exclusively into the unreachable path.**
  `ResolveRendezvous` is called from exactly one site — `PreContactStagingCell:1037` — and from
  nowhere else. The frontline branch returns `ApplyStandoff(best.Value, srCell)` with no rendezvous at
  all. So `RendezvousWithOffensiveStaging` and `RendezvousMaxAdvanceCells` cannot take effect in a
  normal game **even when switched on**. This is a better explanation of `wt/combined-arms`'s
  difficulties than anything that branch concluded about itself: it published an anchor for a consumer
  that cannot run, and its scenario could never have exercised the feature regardless of tuning.

**NOT FIXED — reported first by instruction.** The obvious repair (fall back to
`PreContactStagingCell` when `best` is null, not only when `frontline` is null) is one line, but it
switches on a transport behaviour that has never actually run on either profile, and it would make
`DeliverBeforeContact`, the pre-contact lerp, the standoff and the rendezvous all live at once on
`@stable`. That is a design decision, not a repair. Note also the second-order effect: the frontline
branch would still have no rendezvous, so a fix should decide whether the rendezvous belongs on both
branches or only pre-contact.

**The instrumentation that produced this is kept** (`[exp-transport] no-task … reason=…`). It is three
counters and one line per blocked pass, and its absence is exactly why this went unexplained across
four runs and two branches.

### [info] `test-combined-arms-rendezvous` is a KNOWN-FAILING committed scenario with a non-discriminating control — do not re-tune it — 2026-08-15

**User ruling, 2026-08-15**, recorded so the next person meets it instead of rediscovering it: the
scenario stays as it is, failing, and its t90 placement must **not** be adjusted again.

Two independent reasons:

1. **The placement was already tuned once, blind.** `7f8c2d41` cut four t90s to two specifically
   because the four-tank version killed the lead abrams before the assertion could be judged, and
   shipped that change unverified (`map.yaml:127-133` asks whoever runs it next to confirm the abrams
   survives). Two runs on 2026-08-15 confirm it still dies. Adjusting the number again without a
   mechanism would be the same guess a second time.
2. **The control cannot discriminate, so surviving longer would not make it useful.** Its earlier
   revision passed with the fix disabled, because `PoiOffensiveBotModule.StageFreePool` walks infantry
   to the same staging anchor whatever the transport does. And per the entry above, the rendezvous it
   is meant to exercise is reachable only through a code path that never executes — so no amount of
   tuning could have made this scenario measure the feature.

Rebuild it around an observable that can attribute — arrival **mode**, or the armour-vs-infantry
arrival **timing gap**, both already written down in `DISCOVERIES.md` — as combined-arms work, not as
a tuning pass.

### [measured] The pre-contact fallback fix WORKS and is attributed — but delivery is blocked one layer further on — 2026-08-15

Two runs on the fixed `PickDropOffCell` (`test-combined-arms-rendezvous`, seed 1017, runs
`260815_121924_p62185` and `260815_122231_p62791`).

**The fix does what it claims, and the log attributes it rather than implying it.** Every task creation
in the second run reads `via=staged-empty-frontline` — the pre-contact staging branch that was
previously unreachable, not the frontline branch. Before/after on the same scenario and seed:

| | tasks created | `no-drop-cell` passes |
|---|---|---|
| before | 0 (`tasks-active=0` all run) | 15 |
| after  | 3 (`tasks-active` peaks at 2) | 1 |

The one survivor is `cause=empty-frontline+no-offensive-targets` at tick 15 — transient, PoiMap simply
had not been populated yet, and it resolves by the next scan. That is the `cause` field earning its
place: the four ways a drop cell can fail to resolve are now distinguishable in the log, which closes
the "reasoning rather than measurement" gap flagged on the previous report.

**DELIVERY WAS NOT ACHIEVED, and that is the honest limit of this result.** No `depart` line in either
run. Two independent blockers sit behind the one that was fixed:

1. **Only one boarding order per task is admitted.** All three tasks logged `boarding=1 of 5`,
   `1 of 2`, `1 of 5`. So the arbitration gate's dwell rule IS refusing frontline boarding orders —
   four of five — exactly the mechanism hypothesised and falsified as the *primary* blocker. It was
   never why no task existed; it is why the loads are tiny. Both facts are true; only the ordering was
   wrong.
2. **The single admitted passenger never arrives.** `aboard=0`, `still-coming=1`, `since-board`
   climbing to 450+ ticks. The soldier is alive and in the world and simply does not board — most
   likely re-tasked mid-walk, which is the case `still-coming` cannot see by construction.

**A weakness in the departure rule shipped earlier today, found by its own diagnostics.** With
`aboard=0` and `minPassengers=2`, the stall release cannot fire, because it is gated on
`aboard >= minPassengers` (`MountedTransportMath.DecideDeparture`). A load that is stalled AND empty
therefore waits the full `LoadingTimeoutTicks` (1500 t ≈ 60 s) before `AbortEmpty`. It is bounded —
the no-hang invariant holds and is unaffected — but the gate was written for "a partial load is not
worth delivering yet", and it does not describe a load of nothing, where waiting achieves nothing at
all. The stall should be able to abandon an empty load at the stall bound rather than the hard bound.
Not fixed: it is a behavioural change to a rule merged today and belongs in its own commit.

### [config] `DeliverBeforeContact` is HELD at false on @stable pending a knowing promotion — 2026-08-15

**User instruction, 2026-08-15:** one un-run behavioural change per branch on the benchmark control.

Both twins set `DeliverBeforeContact: true` before today — `@poi` (`enable-ai-stable`) and
`@experimental`. Since the flag was dead config, that true set had never once changed `@stable`'s
behaviour. Fixing the fallback would have made it live on both twins at the same instant, on top of
the `CommitPassengers` change `@stable` already took today.

`@poi` is therefore set to `false` (`ai.yaml:1505`), pinning `@stable` at exactly the behaviour it has
always had. **This is a hold, not a gate**: it withholds nothing that ever ran, and promotion is one
word in its own commit, so the benchmark baseline is re-taken for one change rather than two.

**Not verified by a run.** `test-combined-arms-rendezvous` runs `Bot: experimental` for BOTH players
(`map.yaml:64`, `:71`), so no scenario exercised the `@poi` twin. The hold rests on config inspection
alone. Anyone promoting it should confirm on a scenario that actually fields a stable bot.

### [measured] Both loading blockers diagnosed: blocker 1 CONFIRMED (dwell), blocker 2 FALSIFIED (the passenger is coming, just slowly) — 2026-08-15

Diagnostic run `260815_123045_p63812`, verification run `260815_123440_p64386`
(test-combined-arms-rendezvous, seed 1017; BOTH players are `Bot: experimental`, verified by reading
`map.yaml:64`/`:71`).

**BLOCKER 1 — CONFIRMED. The dwell rule refuses four boarding orders in five.** Every refused
passenger logged `idle=False` with `activity=AttackMoveActivity` (one `SmartMoveActivity`) — i.e. it
was mid-order from another module when the transport asked, which is exactly the dwell rule's trigger
(`OrderArbitrationMath.cs:561-572`, `ReorderDwellTicks: 120`). The single admitted passenger is the
one that was not already claimed. So `boarding=1 of 5` is the arbitration gate, as hypothesised — this
IS the direct cause of tiny loads, one layer under the departure bar. **Not fixed.**

Fix shape, for a decision rather than a build: `AdvanceTask` never re-issues `EnterTransport` to a
passenger it has already reserved (zero occurrences in the Loading state), while offense re-offers
every eval. The transport asserts its claim ONCE; offense asserts continuously. A top-up pass — while
Loading and under capacity, re-offer to the free pool each scan — would let the load grow as units
become idle. That is a design change to a module that has only just started running at all, so it
wants review before code.

**BLOCKER 2 — FALSIFIED, and the falsification mattered.** The reserved passenger was NOT poached. It
sat on `activity=RideTransport` for the entire task and closed steadily: 7 → 5 → 4 → 3 → 2 → 1 cells.
It is simply walking, at roughly 43 ticks per cell. The hypothesis (re-tasked away, never returning)
was wrong, and the evidence for it — `aboard=0` for 450 ticks — was equally consistent with a
passenger that is coming and has not arrived.

**That falsification exposed a regression in the empty-stall fix committed an hour earlier
(`cad27464`), and it is worth stating as the general trap.** `aboard` is a STEP FUNCTION: it stays at
0 for the whole of a passenger's walk. So "no boarding for 250 ticks" is automatically true early in
every load and carries no information about whether anyone is coming. A 7-cell walk (~300 ticks)
outlasts the 250-tick bound, so the empty-abort tore down a task whose passenger was 3 cells away and
closing — and the carrier looped abort/recreate every 250 ticks, measured as repeated `task-created`
for the same carrier at ticks 65, 315, 415. **A metric that cannot move until the outcome has already
happened is useless as a progress signal for that outcome.**

Fixed by making APPROACH count as progress: a new closest-approach by any still-coming passenger
resets the stall clock, so the bound now means "nobody boarded AND nobody got closer". Verified — one
`task-created` per carrier, no loop, `since-progress` returning to 0 as `closest` ticks 7→1.

**STILL NOT OBSERVED: a completed delivery.** No `depart` line in any run. At the end of the last run
the passenger was 1 cell from the carrier and had not finished boarding. The blocker is now the
SCENARIO, not the code: `test-combined-arms-rendezvous` aborts on its tank-death guard at ~tick 550,
and a load needs perhaps another 50-100 ticks. Verifying delivery needs a scenario that (a) lives
long enough, (b) has no early-abort guard, and (c) ideally fields one `Bot: stable` and one
`Bot: experimental` player so the `@poi` hold gets a real control at the same time. Deliberately NOT
built here: an unrun scenario is exactly what `7f8c2d41` shipped and what today's notes criticise.

### [measured] The ground transport LOADS AND DEPARTS — first ever observed — and the @poi hold is verified by measurement — 2026-08-15

Three runs on the new `test-transport-delivers` scenario (seed 1017). Two results are solid and one
piece of scaffolding is not.

**DEPARTURES ARE PROVEN.** With production on (`DefaultCash: 7500`, run `260815_124529_p66286`) the
module logged **six** departures, every one `reason=Full`:

```
depart carrier=bradley aboard=3 target=3 reason=Full tick=915
depart carrier=m113    aboard=2 target=2 reason=Full tick=1165
depart carrier=bmp2    aboard=3 target=3 reason=Full tick=1536   (Russia, after contact)
depart carrier=m113    aboard=2 target=2 reason=Full tick=2065
depart carrier=bmp2    aboard=3 target=3 reason=Full tick=2436
depart carrier=m113    aboard=3 target=3 reason=Full tick=2565
```

Loads of two and three, departing because they were FULL rather than timing out. Every `task-created`
read `via=staged-empty-frontline`, so all of it is attributed to the pre-contact branch fixed earlier
today — the branch that could not execute at all before. This is the first time this project has
observed the ground transport load and drive.

**THE @poi HOLD IS VERIFIED BY MEASUREMENT, not config inspection.** The stable side logged
`no-task ... cause=empty-frontline+fallback-disabled` **21 times** and created exactly one task, at
tick 1286, `via=frontline` — i.e. only after contact, through the ordinary path, never through the
held pre-contact branch. That closes the gap flagged twice as "verified by config inspection only".

**A COMPLETED DELIVERY IS STILL NOT PROVEN.** The unload — the event that makes it a delivery rather
than a drive — was only ever an `AIUtils.BotDebug` line, which does not reach `debug.log` unless bot
debug is on. So every run so far could prove departure and nothing about arrival. Fixed here: the
module now emits `[exp-transport] delivered ... pax=N` at the Unloading -> Returning edge, which fires
only once the hold is empty. One run will now settle it.

**THE NEW SCENARIO'S LUA PREDICATE IS NOT YET VALID, and is committed marked as such.** With
`DefaultCash: 0` and `carriers-total=1` — so the placed bradley at 8,18 is provably the only carrier
and provably the `BotCarrier` global — the module logged `depart aboard=1` and later `aboard=2` while
the predicate reported `peakPax=0` and `everCarried=0` for the entire run. Both cannot be true. The
closure demonstrably ran (it produced the failure string), the carrier was neither dead nor
duplicated, and both sides read the same `Cargo` trait. Leading suspect is the file-scope
`local Squad = { BotRifle1, ... }` capture — map-actor globals may not be bound when the chunk first
executes, giving `#Squad == 0`, which explains `everCarried=0` exactly but NOT `peakPax=0`, so there
is at least one more fault. Not chased further: the run budget was spent, and guessing at it without
a run is the failure mode this file exists to record.

**Scenario-design lesson worth keeping.** The first version ran at `DefaultCash: 7500` and the
inherited rules comment claimed "the measurement only ever looks at NAMED actors, so incidental units
the bot buys or spawns do not affect it." That is backwards. The bot bought its own carriers and
produced its own infantry and used those, leaving the named actors idle beside the measurement —
six departures in the log, zero in the predicate. **A named-actor predicate is valid only if the named
actors are the ones the system under test actually chooses to use, and production removes that
guarantee.**

### [measured] THE GROUND TRANSPORT COMPLETES DELIVERIES — proven — and the scenario's Lua had three separate faults — 2026-08-15

**THE RESULT, from the module's own log and independent of any Lua predicate.** Run
`260815_130128_p68980`, seed 1017, `test-transport-delivers`. The `delivered` marker added at the
Unloading -> Returning edge (which fires only once the hold is empty) recorded **three completed
deliveries**:

```
delivered carrier=bradley at=29,10 drop=32,10 pax=1 tick=1465
delivered carrier=bradley at=20,14 drop=21,13 pax=3 tick=2815
delivered carrier=bradley at=24,12 drop=24,12 pax=3 tick=3765
```

Arriving AT the drop cell (24,12 exactly; 20,14 against 21,13; 29,10 against 32,10) and unloading.
Combined with the six `reason=Full` departures already banked, the chain the user asked about —
load, fill, drive, set down — is now observed end to end. This is the first time.

**THE SCENARIO'S PREDICATE STILL DOES NOT GO GREEN, and three distinct Lua faults were found.** Two
are fixed and verified, the third is fixed but unverified (no runs left):

1. **File-scope actor capture.** `local Squad = { BotRifle1, ... }` at chunk scope binds before
   map-actor globals exist, giving `#Squad == 0` and a predicate that loops over nothing while
   reporting confident zeros. Fixed by binding inside `WorldLoaded`, plus a setup self-check that
   fails loudly if the squad is not exactly 5. VERIFIED.
2. **`IsDead` is true for a passenger inside a `Cargo`.** The idiom `not r.IsDead and not r.IsInWorld`
   for "was carried" is unsatisfiable for exactly the units it targets. Measured: `peakPax=2` with
   `everCarried` stuck at 0 all match; latching on `not r.IsInWorld` alone made it read 3 immediately.
   VERIFIED. **`test-combined-arms-rendezvous` carries the same unfixed idiom** and is likely
   mis-counting `EverCarried` for the same reason — worth checking when that scenario is rebuilt.
3. **The failure message is evaluated EAGERLY at registration.** `AssertWithin`'s third argument is an
   ordinary Lua expression, concatenated before the predicate runs once, so interpolated counters
   report their initial zeros forever. This is what produced `everCarried=0 peakPax=0` in the verdict
   while the in-closure trace of the SAME RUN read `everCarried=3 peakPax=2` — and it caused a wrong
   diagnosis ("the predicate is not observing the actor it names") that cost a run. Fixed by making
   the message static and directing the reader to the `lua.log` trace. NOT VERIFIED — no run left.

**Remaining unknown for whoever picks this up.** At the end of run `260815_130726_p70843`:
`everCarried=3`, `squadInWorld=2/5`, `peakPax=2`, yet no rifleman satisfied RETURNED + MOVED >= 10
cells. The module logged deliveries at ticks 2815 and 3765, so passengers were set down; the open
question is whether the delivered riflemen died shortly after being dropped (this scenario does reach
contact by ~tick 1300) or whether the returned-and-moved clause is mis-measuring. The `[deliv]` trace
already prints `squadInWorld`; adding each squad member's in-world flag and distance-from-start to
that line should settle it in one run.

**Do not merge this scenario until it goes green** — `run-batch.sh` globs `test-*/`, so a
knowingly-broken scenario auto-joins every future batch and becomes a permanent false signal, which
is the debt `test-combined-arms-rendezvous` already represents.

### [bug, FIXED] `wip-transport-delivers` shipped uncompilable — a missing `end` — so no run of it ever measured anything — 2026-08-15

At `fe692f17` the scenario's Lua is **missing the single `end` that closes `WorldLoaded = function()`**
(opened line 52). Introduced by the previous session's "make the failure message static" edit, which
was committed flagged **NOT VERIFIED — no run left**; that is exactly what went wrong. The engine
reports it only at load, as
`Fatal Lua Error: ... 'end' expected (to close 'function' at line 52) near '<eof>'`, and the harness
records an ordinary `FAIL` — indistinguishable at a glance from a predicate that did not go true.
**It cost a full run to discover.**

Fixed here, plus `tools/autotest/lua-balance.py` — a block-balance check (single left-to-right scan;
a naive regex pass that strips comments before strings corrupts any string containing `--`, which
produced twelve false positives on the first cut). Run it before spending a run on any scenario whose
Lua you touched:

```
python3 tools/autotest/lua-balance.py tools/autotest/scenarios/*/[a-z]*.lua mods/ww3mod/scripts/*.lua
```

144 files balanced at time of writing. It refuses an empty file list rather than passing by scanning
nothing.

### [measured] The offensive layer wins ~55% of the transport's boarding contests — the boundary of the top-up lever — 2026-08-15

Run `260815_192247_p79585`. `TopUpDuringLoading` made **11** boarding offers to the free pool while
carriers loaded: **5 admitted, 6 refused**. Every refusal logged `idle=False
activity=AttackMoveActivity` — the dwell rule (`ReorderDwellTicks: 120`) protecting a standing order
from `PoiOffensiveBotModule.StageFreePool`, which recruits armed infantry from tick 3.

Two separate things, and only one of them is the offensive layer's doing:

* **Contest losses (6 of 11)** — offense holds the unit, the transport cannot have it. Fixing this is
  on the offense side, and is queued as separate work.
* **Contest WINS that still filled no seat (5 of 5, on the clean paired departure)** — the soldiers
  were won, held for 450 ticks without being poached back (`topup-coming=4` at departure), and simply
  did not walk far enough in time. This half **cannot be fixed by re-offering harder**, and is bounded
  by the no-extension constraint; see `DISCOVERIES.md` same date for the argument.

So re-offering mid-load is not the lever that closes the user's complaint. The seats are decided at
task creation, by who is near the carrier and unclaimed at that instant.

### [bug, OPEN] `wip-transport-delivers` still cannot go green: `DefaultCash: 0` does not pin the force — 2026-08-15

Stays `wip-*` (out of `run-batch.sh`'s `test-*/` glob) and is NOT renamed back. Its predicate names
five `e3.america` riflemen; the module was measured boarding eight distinct infantry types, the other
seven being Supply Route reinforcements that zero cash does not prevent. Per-member tracing at tick
4150 reads `r1=w/c0/d40 r2=n/c1/d- r3=n/c1/d- r4=w/c0/d22 r5=w/c0/d31` — the three that moved far were
never carried (they walked, under the offensive layer), and the two that left the world never came
back. The RETURNED+MOVED clause therefore cannot fire, and **neither** of the two explanations offered
in the previous handover (delivered-then-died / clause mis-measuring) was correct.

Rebuilding it wants an observable read from the module rather than from named actors — the
`depart aboard=N` line is already exactly that, and is what the before/after in `DISCOVERIES.md` uses.
### [bug] The Mi-28 has no anti-air weapon at all, and its `secondary-air` armament does not exist — 2026-08-15

Found while establishing an anti-air power ceiling for the Hind. `MI28` lists `Armaments: primary, secondary, secondary-air` (`mods/ww3mod/rules/ingame/aircraft-russia.yaml:319`) and references `secondary-air` again from its ammo pool and a `GrantConditionOnPreparingAttack` (`:329`, `:374`) — but **no `Armament@` named `secondary-air` exists anywhere in the repo**. `AmmoPool.cs:303` matches armaments by name and simply finds nothing, so the reference fails silently.

The consequence is not cosmetic. The Mi-28's two real weapons are `30mm.Heli` (`ValidTargets: Ground`, `weapons-ballistics.yaml:487`) and `Ataka` (`ValidTargets: Vehicle, Defense`, `weapons-missiles.yaml:126`). **Neither can engage an aircraft**, so Russia's 6000-credit attack helicopter currently cannot shoot at helicopters at all, while its American counterpart can (Apache's Hellfire lists `Air`).

**This directly undercuts the design intent recorded on `wt/heli-weapons`.** The user's constraint for the Hind's new last-resort AA gun was that it be *"not nearly as good as an attack helicopter"* — but on the Russian side there is no attack-helicopter benchmark to sit below, so the Hind's gun becomes the only Russian helicopter able to touch aircraft. The ceiling is correctly placed against the *American* Apache and against dedicated AA (Stinger/MANPAD/Tunguska), and is comfortably below both; it is the Russian internal ordering that is inverted, and that inversion predates this branch.

Not fixed here — giving the Mi-28 anti-air is a balance decision beyond the four asks on this branch, and it needs a call on whether the intended weapon was an air-to-air variant of Ataka or a second gun mount. The littlebird carried the identical defect (`AmmoPool@1: Armaments: primary, primary-air` with no such armament, since the actor's first commit `98a4dc09`); that one **is** fixed on this branch, because the missing armament turned out to be exactly the feature being requested.

### [info] Six weapons set `InaccuracyPerProjectile`, which cannot execute — 2026-08-15

`Bullet.cs:213` gates the field on `lastPosIsSet`, a `readonly bool` initialised `false` at `:170` and never assigned. Still set by `weapons-ballistics.yaml:541,565,601,617,760` and `weapons-other.yaml:88`. Removed from `7.62mm.Minigun` on `wt/heli-weapons`; the other six left alone, but anyone tuning burst spread on those weapons is turning a dial connected to nothing. Full detail in `DISCOVERIES.md` (2026-08-15, helicopter guns).

### [high] `make test` (YAML validation) has been RED on main, and nobody noticed — 2026-08-15

Found incidentally while validating an unrelated merge. `make test` fails:

```
Testing map: Siberian Pass WW3
OpenRA.Utility(1,1): Error: This map does not define a valid cordon.
A one cell (or greater) border is required on all four sides between the
playable bounds and the map edges.
make: *** [test] Error 143
```

**Not caused by that merge** — verified: the merge touches no map under `mods/ww3mod/maps/`, and the responsible commit is an ancestor of pre-merge main.

**The cause is deliberate and repo-wide.** `aa0620ea` ("Expand map bounds to full MapSize across all maps (0,0 origin)") set `Bounds` equal to `MapSize` on essentially every shipping map, which by definition leaves no cordon:

| map | MapSize | Bounds |
|---|---|---|
| arena-tank-duel | 66,34 | 0,0,66,34 |
| nuclear-winter-ww3 | 102,72 | 0,0,102,72 |
| polar-disorder-ww3 | 98,98 | 0,0,98,98 |
| river-zeta-ww3 | 98,82 | 0,0,98,82 |
| seventh-woods-ww3 | 123,114 | 0,0,123,114 |
| siberian-pass-ww3 | 97,67 | 0,0,97,67 |
| twin-rivers-ww3 | 128,128 | 0,0,128,128 |
| woodland-warfare-ww3 | 98,98 | 0,0,98,98 |
| x-lake-ww3 | 130,130 | 0,0,130,130 |

`shellmap-open-field` is the only exception (`Bounds: 1,1,90,60`) and is presumably why this was never total.

**Two things need deciding, and this entry does not decide either.** Whether the bounds expansion was right and the lint is simply wrong for this project (in which case the check should be waived deliberately, with a reason), or whether the maps genuinely lost a border they need. The maps do load and play, so this is not visibly broken in-game — which is exactly why it went unnoticed.

**The real damage is the guard rail.** `CLAUDE.md` tells every worker `make test` is the YAML validation step. A check that is already red teaches everyone who runs it to ignore it, and hides the next genuine YAML break — including the blank-line-merge trap that the same file warns about. Whoever picks this up: the goal is getting `make test` green again, not fixing one map.

**Process note attached to the discovery.** The failure was nearly missed because the command was chained through `tail`, so the harness reported exit code 0 while `make` had returned 143. That is the third recorded instance of a verdict being inverted by `tail` — see the standing rule in `DOCS/recipes/AUTOTEST.md`. It applies to `make`, not only to `run-test.sh`.

### [info] `UnloadCargo.cs:108` carries a latent `CA2021` that will turn `Check Code` red the day the runner's SDK advances — 2026-08-17

Found while clearing the 106 analyzer errors (`wt/analyzer-burndown`). Because there is still no `global.json`,
the analyzer set is whatever SDK the runner happens to have. To verify the 5 `CA1862` fixes — which do NOT
reproduce on the local 6.0.428 SDK — I temporarily pinned `Microsoft.CodeAnalysis.NetAnalyzers` 8.0.0 and rebuilt.
`CA1862` was confirmed gone, but the newer pack also reported a rule CI does not currently run:

`engine/OpenRA.Mods.Common/Activities/UnloadCargo.cs(108,11): error CA2021` — *"Do not call `Enumerable.Cast<T>`
or `OfType<T>` with incompatible types"*, i.e. a cast the analyzer can prove always yields an empty sequence.

**Not fixed here, deliberately** — it is outside this branch's scope and is not part of the 106 CI reports today.
Two things make it worth recording. It is a *correctness* rule, not a style one, so the cast may be a real dead
code path worth reading. And it is the concrete demonstration of the SDK-drift hazard the CI-integrity entry in
`DISCOVERIES.md` described in the abstract: the gate is now honest, so the next runner-image bump turns this red
with no repo change at all. Pinning a `global.json` would convert that from a surprise into a decision.

**RESOLVED 2026-08-17 (`wt/sdk-pin`) — the analyzer is WRONG here, and the "obvious fix" is a real bug. Do not
remove the `Cast<T?>`.** CA2021's claim is that the cast always yields an *empty* sequence. Measured false on
the 6.0.428 SDK with a standalone probe mirroring the real shape — a `readonly struct` and an `enum : byte`
in a `ValueTuple`, matching `CPos` (`CPos.cs:19`) and `SubCell` (`TraitsInterfaces.cs:335`):
`.Select(...).Cast<(T,U)?>()` over a 2-element source enumerates **2** elements, not 0. Unboxing a boxed `T`
to `Nullable<T>` is supported by `unbox.any`, which `Enumerable.CastIterator` compiles to; the analyzer does
not model the `Nullable<T>` case. So the code path is live and this is a false positive.

**The cast is load-bearing.** It is what makes `FirstOrDefault` return `null` rather than
`(default(CPos), SubCell.Invalid)` when no adjacent cell has a free subcell, and `UnloadCargo.cs:163` branches
on exactly that `null` to call `NotifyBlocker` and re-queue a `Wait(10)`. Delete the `Cast<T?>` and that branch
becomes unreachable: a fully blocked transport would stop reporting itself blocked and would place passengers
at `SubCell.Invalid`. Anyone who later sees this rule fire should suppress or ignore it, not "simplify" the
chain. Left as-is; a one-line comment now guards it in the source.

The SDK-drift half is fixed: `global.json` pins the 6.0 band, so CA2021 cannot appear without a deliberate bump.

---

## The replay compatibility gate is dead code — WW3MOD's mod version is a frozen literal

**FIXED 2026-08-19 on `wt/replay-version` (against `bc168d8b`)** — by the second of the two candidates
below, with three corrections to it after review. Replay metadata now carries `BuildFingerprint`
(`GameInformation.cs`, stamped at `World.cs:272`) and `ReplayCompatibilityCheck.Resolve` reports a
mismatch.

1. **It WARNS, it does not refuse.** The premise that drove "refuse" — that a stale replay diverges
   silently — is false: recorded sync hashes are replayed back through `OrderManager.ReceiveSync`
   (`ReplayConnection.cs:101-109`, `:117-118`; `OrderManager.cs:225-234`) and a mismatch raises the
   OutOfSync prompt. So the dialog offers "Watch Anyway", matching the settled precedent for the join
   path in `WORKSPACE/closeout/54ab3880.md` §4: a guard that blocks play is worse than the bug it
   diagnoses.
2. **Only the engine-revision and rules-hash segments are compared, never the asset digest** — but NOT
   for the reason first written here. The digest is not machine-specific (`Folder.cs:35-38` hashes leaf
   names only, so identical extractions agree). It is excluded because two installs can legitimately
   hold different content SETS and the digest cannot tell that from real divergence.
3. **The engine-revision segment needed a pathspec first.** It was stamped from bare `rev-parse HEAD`,
   so any commit — including a `WORKSPACE/`-only one — changed it, which would have made a replay
   watchable for roughly one commit. Now `git log -1 ... -- engine mods`.

The first candidate (stamping `mod.yaml` during `make all`) was rejected: `mod.yaml` is itself hashed
into the fingerprint's rules segment (`BuildFingerprint.cs:297-300`), so stamping it every build would
churn the multiplayer fingerprint on commits that changed no rule and make `DescribeDifference` blame
"mod rules" for a version bump. Still unverified end to end — it needs one live run to confirm a
recorded replay round-trips through a real `.orarep`, and the dialog has never been seen on screen.

Found 2026-08-19 while costing out a sync-hash change (`wt/sync-traits`, against `08b255f7`). Recorded
rather than acted on at the time, on the grounds that the fix was a release-process decision.

`ReplayUtils.cs:63` (as it stood then; now `ReplayCompatibility.cs:87`) refuses a replay when
`Game.Mods[mod].Metadata.Version != version`. But
`mods/ww3mod/mod.yaml:3` hardcodes `Version: release-20230225`, and only the manual `version` make target
rewrites it (`Makefile:177-179`) — `all: engine` (`Makefile:157`) never does. Every build reports the same
version, so the comparison is always equal and the gate has never fired.

**Effect:** a replay recorded on any older build opens without complaint and then diverges — the failure is
silent and looks like a replay bug rather than a version mismatch. Every rules edit and every hash-changing
commit (e.g. `473928a2`, which added `ISync` to two traits and shifted every hash value in the game) adds to
the pile of replays that claim to be valid and are not.

**Low urgency today, and the reason is worth stating:** the mod is unreleased, so the population of
older-build replays anyone cares about is roughly nil. This gets worse the moment there are real players.

Two candidate fixes, both cheap, neither obviously right without a call from the user:
- Wire `make all` to stamp a real version (the `VERSION` recipe at `Makefile:46` already derives
  `git-<short-sha>`), which makes the existing gate work as designed. Costs: mod.yaml churns on every build.
- Or record `BuildFingerprint.ForMod` (`BuildFingerprint.cs:99`) into replay metadata and check *that* on
  load — strictly better signal, since it covers rebuild-forgotten and content-mismatch cases the version
  string cannot, and the fingerprint already exists and is already computed for the handshake.

Note the same frozen string is sent in the multiplayer handshake (`UnitOrders.cs:285`), so the version half of
join validation is equally inert; there, `BuildFingerprint` at least logs a mismatch (`Server.cs:557-562`).

---

## `mods/ww3mod/languages/en.ftl` carries a dead copy of the replay dialog strings

Found 2026-08-19 on `wt/replay-version` (against `bc168d8b`) while adding two new strings to that
dialog. Pre-existing, not caused by that work, and cosmetic.

`mods/ww3mod/languages/en.ftl:585-595` defines `incompatible-replay-title`,
`incompatible-replay-prompt`, `incompatible-replay-accept`, `incompatible-replay-unknown-version`,
`incompatible-replay-unknown-mod`, `incompatible-replay-unavailable-mod`,
`incompatible-replay-incompatible-version` and `incompatible-replay-unavailable-map` — an older flat
key scheme. The engine moved to attribute keys (`dialog-incompatible-replay.title` and friends), which
is what `ReplayUtils.cs:19-41` actually references and what `engine/mods/common/fluent/common.ftl:615`
supplies. Nothing in C# reads the flat keys (`grep -rn "incompatible-replay-title" --include="*.cs"`
returns nothing), and `--check-yaml` reports all eight as `Unused key`.

**Effect:** none at runtime — the live strings come from `common.ftl`. The trap is for whoever next
edits the replay dialog wording: editing the ww3mod copy changes nothing on screen, and the eight
warnings are noise in every lint run. They are warnings rather than errors, so they are not in
`lint-baseline.txt`. Deleting the block is the fix; left alone here because it is unrelated to the
gate being fixed and the release is scope-locked.

---

## 2026-08-19 — `mods/ww3mod/chrome/garrison-panel.yaml` is a dead file (found on `wt/widget-symbols`)

It defines `Container@GARRISON_PANEL` (`garrison-panel.yaml:1`) with `X: WINDOW_WIDTH - 240` /
`Y: WINDOW_HEIGHT - 260`, but it is not listed in `mod.yaml`'s `ChromeLayout`, so it is never loaded. The
panel the game actually shows is the identically-named container defined inline at
`ingame-player.yaml:629`. Nothing reads the standalone file — the only other mention of the string
`garrison-panel` in the tree is the file itself.

Not fixed here because deleting it is a judgement call I could not verify by running the game this session:
if it is someone's staging copy for an in-progress panel rework, deleting it destroys that. It is harmless
where it sits (dead weight, not wrong behaviour). Worth deciding deliberately: either delete it, or add it
to `ChromeLayout` and remove the inline copy so there is one definition. Note it is also unchecked by the
unregistered-symbol lint for the same reason (see `DISCOVERIES.md`, 2026-08-19) — its two expressions happen
to be valid today, but nothing would catch it if they stopped being.

---

## 2026-08-19 — `Button@TAKE_COVER` is inert at three separate levels, and its removal is a 12-widget reflow (found on `wt/command-bar`, `main @ 815804f1`)

> **[FIXED on `wt/take-cover`, off `main @ de78a1ed`]** — all three levels re-verified against
> `de78a1ed` before touching anything, and all three still held. The button and its dead C# are gone;
> the 12 numeric edits below were applied exactly as tabled (every value re-derived on the newer ref
> first — none had drifted). Two things this entry did **not** record, both found while verifying:
> the button was a **deliberate placeholder, not an oversight** — `84a1ee69`'s own message reads
> *"New command buttons (dummy): Patrol, Evacuate, Take Cover, Auto-Enter"* — and its three siblings
> have since been wired, leaving it the last unfinished member of that batch. It could not be wired
> the same way because `82f0b8eb` (2023-04-06) renamed `TakeCover` → `InfantryStates` and made prone
> automatic **three years before the button was authored**. So it is *unfinished*, not *cut* and not
> an RA leftover. The `3ae6e473` follow-up ("was always clickable but did nothing") added only the
> greying, which made a dummy look exactly as live as the working buttons.
> **The reflow is still unverified visually** — a capture request is filed with the manager, and
> `tools/autotest/scenarios/demo-command-bar-reflow/` stages the shot.

**Symptom the player sees:** a command-bar button that renders **ungreyed** whenever infantry are selected —
so it looks live — and does nothing at all when clicked. `takeCoverDisabled` keys off `InfantryStatesInfo`
(`CommandBarLogic.cs:444`), which every infantry actor has, so the enabled state is genuine.

**Decision taken 2026-08-19: LEAVE IT for now**, and the reason is the launch budget, not the merits. The
removal below is verifiable only by looking at the rendered bar, and no launch was available. It is being
filed as its own pipeline item with the launch requirement attached. This is **not** a judgement that the
button is acceptable to ship — a control that lights up and does nothing is exactly the class the release
audit promoted from polish to blocker.

### It is inert at three levels, and fixing only the outer one fixes nothing

This distinction matters because "give it a hotkey" is the obvious cheap fix and it would accomplish nothing.

1. **No hotkey definition exists.** The button has no `Key:` in YAML, and there is no `TakeCover` entry in
   any of the nine hotkey files `mod.yaml:261-270` loads. Adding `Key: TakeCover` alone would resolve
   through `HotkeyManager.GetHotkeyReference` (`:48-58`) → not in `keys` → `Hotkey.TryParse("TakeCover")`
   fails → `Hotkey.Invalid`, so the tooltip would show no key and nothing would fire.
2. **No handler.** `CommandBarLogic.cs:262-268` binds `BindButtonIcon` and `IsDisabled` only — it never
   assigns `OnClick`. Note the key *routing* is present by default (`ButtonWidget.cs:96-97` wires
   `OnKeyPress = _ => OnClick()`), so a working binding would successfully route a press into `OnClick`,
   which is still the default no-op `() => { }` (`ButtonWidget.cs:73`).
3. **No receiving trait.** WW3MOD replaced RA's `TakeCover.cs` with `InfantryStates.cs`, where prone is
   automatic and condition-driven (`ProneCondition`, `InfantryStates.cs:25`; `architecture.md:176`). No
   ww3mod actor carries a `TakeCover` trait — the surviving `TakeCover:` lines are all in the bundled
   vanilla `engine/mods/{ra,cnc,ts,d2k}` rules, which ww3mod does not load. There is no order to send and
   nothing that would receive one.

So the only two coherent outcomes are **remove the button** or **design a new orderable behaviour** on top of
the automatic prone system. The latter is gameplay work, not chrome.

### Exact cost of removal — inherit this rather than re-deriving it

All in `mods/ww3mod/chrome/ingame-player.yaml`. Delete the `Button@TAKE_COVER` block at **lines 233–251**
(19 lines, `X: 358` on line 235), then apply **12 numeric edits**. The pitch is 34px throughout; the file's
own PITFALL comment at `:52-57` is the reason the panels move independently — they are **not** parented to
the button containers and each carries its own absolute X.

| Widget | Decl line | Field | From | To |
|---|---|---|---|---|
| `Button@AUTO_ENTER` | 252 | `X` (line 254) | 392 | **358** |
| `Button@EVACUATE` | 333 | `X` (line 335) | 426 | **392** |
| `Container@COMMAND_BAR` | 85 | `Width` | 460 | **426** |
| `Background@CMD_BG_B` | 50 | `Width` | 188 | **154** |
| `Background@FIRE_BG` | 61 | `X` | 483 | **449** |
| `Background@ENGAGE_BG` | 67 | `X` | 603 | **569** |
| `Background@COHESION_BG` | 73 | `X` | 723 | **689** |
| `Background@RESUPPLY_BG` | 79 | `X` | 843 | **809** |
| `Container@STANCE_BAR` | 353 | `X` | 492 | **458** |
| `Container@ENGAGEMENT_STANCE_BAR` | 422 | `X` | 612 | **578** |
| `Container@COHESION_BAR` | 491 | `X` | 732 | **698** |
| `Container@RESUPPLY_BEHAVIOR_BAR` | 560 | `X` | 852 | **818** |

`Background@CMD_BG_A` (line 44, X=5 W=290) is **unchanged** — it covers the eight buttons at container-X
0…272, all of which sit left of the deletion.

**Check the arithmetic holds after editing:** the panel convention is `Width = 9 + content + 9`.
`CMD_BG_B` then spans 295…449 with content 304…440 — four 34px buttons at container-X 290/324/358/392,
absolute 304/338/372/406, ending 440, leaving the 9px right margin. `COMMAND_BAR` at X=14 with Width 426
ends at absolute 440 to match. Every panel from `FIRE_BG` rightward simply shifts −34, preserving the
existing 120px pitch between them.

**Verification this needs and did not get:** a launched game, eyes on the bar. The numbers above are
arithmetic, not evidence — that is precisely why the change was not made blind.

## 2026-08-19 — `test-supply-under-danger`'s drift allowance is read LIVE, so a consumed crate can retro-fail the platoon

`driftAllowance()` in `tools/autotest/scenarios/test-supply-under-danger/test-supply-under-danger.lua`
decides between `MAX_DRIFT` (6) and `HOLD_DRIFT` (1) from `#crateList() > 0` — a **live count at the
moment of the check**, not a latch over the run. SUPPLYCACHE self-removes once drained
(`RemoveBelowSupply: 1`, `rules/misc.yaml:437`), so a crate that was dropped, legitimately walked to,
and then consumed is **gone by verdict time**. The allowance snaps back from 6 to 1 while the peak drift
it authorised (up to 6) stays recorded, and the run reports "THE FRONT COLLAPSED BACKWARDS" for a trip
the doctrine asked for.

Same family as the peak-vs-final-position trap that clause already documents, one level up: the
MEASUREMENT is correctly taken over the whole run, but the THRESHOLD it is compared against is sampled
at an instant, so a transient licensing condition is lost. Direction of the defect is a false FAIL, not
a false PASS — it cannot manufacture a green run.

**Not fixed here.** The fix is a one-line latch (`crateEver`, which is what the sibling
`test-supply-safe-front-keeps-cargo` now uses for exactly this reason), but this session had no game
launches allocated, and this scenario is currently the cheapest deterministic instrument pointing at
PIPELINE item 56's mode selector. Changing the verdict logic of a live instrument without being able to
run it once is not worth the risk of a one-line improvement to a failure direction that cannot fabricate
a pass. Whoever next has a launch slot for this scenario should latch it and re-run.

## 2026-08-19 — `DOCS/gameplay/capturing.md` states an OILB income rate I could not reconcile (found on `wt/neutralise`)

`capturing.md:72` says *"At a 25-tick base interval ($50/sec for OILB), a single Oil Derrick pays for one
Conscript every 4 seconds"*, and `:58` and `:3` build on that ("repay that in 4 seconds of income",
"pay you cash every second"). Two things do not line up and I did **not** change them:

- **The 25-tick interval has no source I can find.** `CashTricklerInfo.Interval` defaults to **60**
  (`engine/OpenRA.Mods.Common/Traits/CashTrickler.cs:26`) and **no `Interval` override exists anywhere in
  `mods/`** — the only `CashTrickler` blocks are `structures-neutral.yaml:19` (OILB, `Amount: 50`), `:51`
  (FCOM, `100`) and `:83` (BIO, `150`), each setting `Amount` alone.
- **60 ticks is 3.6 s at this mod's speed**, not 1 s (`Timestep: 60` ⇒ 16.67 ticks/s), which would make OILB
  roughly $14/s rather than $50/s — a 3.6× difference in a doc that uses the figure to argue build order.

**Deliberately not "corrected", because the naive arithmetic is probably not the real answer either.** The
`Interval` Desc at `CashTrickler.cs:24-25` says it is *"used to normalize the income rate when registering
with the unified economy tick"* — so the trait does not pay on its own cadence, and the true payout rate is
set by that unified economy tick, which I did not trace. Rewriting the number from `Amount / Interval` would
just replace one unverified figure with another.

Whoever owns the economy (`DOCS/reference/economy.md`) should settle it and fix all three lines together.
Flagging rather than guessing; the tick→second half of the same doc's errors *was* fixed on this branch
(see `DISCOVERIES.md` 2026-08-19), so `capturing.md` is now internally consistent on delays but not on income.

---

## 2026-08-19 — five command-bar visibility settings are declared and never read (found on `wt/take-cover`)

> **[FIXED by deletion on `wt/dead-settings`, off `main @ 4f5123dc`]** — finding re-verified (the grep still
> returns only the five declarations), then the five fields and their comment were deleted.
>
> **One claim below is wrong, and it is the reason deletion is free.** They were *not* "written to
> `settings.yaml` and persisted". `Settings.Save()` skips any field still at its default (`Settings.cs:441-443`,
> *"Fields with their default value are not saved"*), and since nothing ever wrote these they never left
> `true` — so they never reached disk at all. Confirmed empirically: the dev's own
> `~/Library/Application Support/OpenRA/settings.yaml` contains none of the five keys. There was no user data
> to migrate. A stale file that *did* contain them is safe anyway, because `Settings` overrides
> `FieldLoader.UnknownFieldAction` to log-and-ignore (`Settings.cs:390`) rather than throw.
>
> **Chose delete over wire** because the entry frames the choice as symmetric and it is not. `ScaleInfantry`
> is the exemplar of a *finished* WW3MOD setting and has three parts: the declaration, a consumer
> (`RenderSprites.cs:220`), and a checkbox binding (`DisplaySettingsLogic.cs:167`). These five have only the
> first — so "wire them" is not finishing a feature, it is building both remaining thirds of one nobody has
> asked for, plus a reflow no other setting here needs. Deletion is six lines and cannot alter a pixel, since
> nothing read them. If configurable bars are ever actually wanted, re-adding five booleans is the cheapest
> part of that job and this commit reverts cleanly.
>
> **Measured cost of the rejected option, for whoever revisits it.** The panels are a 120px pitch of ten
> absolute literals — `CMD_BG_A` X5/W290, `CMD_BG_B` X295/W154, then `FIRE_BG` 449, `ENGAGE_BG` 569,
> `COHESION_BG` 689, `RESUPPLY_BG` 809, each bar container sitting at panel+9. Prior art sets the price:
> `b62ee52f` removed a single 34px button and had to rewrite **13** of those literals. A settings toggle is
> strictly harder, because the reflow becomes *runtime* rather than static and YAML absolute literals cannot
> express it — there is no flow-layout widget in the engine (searched: no `HorizontalLayout`/`FlowLayout`
> type exists). It would have to be hand-packed in a logic class in the style of `IngameMenuLogic.cs:327`.
> Hiding `COMMAND_BAR` is the worst case: it is the leftmost 449px, so the gap opens at the screen edge and
> every remaining panel is stranded mid-bar.

`Settings.cs:341-345` declares `CommandBarVisible`, `FireStanceBarVisible`, `EngagementBarVisible`,
`CohesionBarVisible` and `ResupplyBarVisible`, all defaulting to `true`. **Nothing reads any of them** —
`grep -rn` for all five across every `*.cs` and `*.yaml` in the tree returns only those five declaration
lines. They are written to `settings.yaml` and persisted, and no widget consults them.

Same origin as the TAKE_COVER button: commit `84a1ee69`, whose message says *"Bar visibility settings
added to GameSettings (persisted)"* in the same breath as *"New command buttons (dummy)"*. The settings
were the persistence half of a toggle whose consuming half was never written.

**Effect:** none on screen today — the bars and their background panels are unconditionally visible, which
is why a chrome capture of the command bar is deterministic regardless of the dev's saved settings. The
trap is for whoever adds a settings-menu toggle for these: the checkbox would bind, save, persist, and
change nothing, which reads as a settings-persistence bug rather than a missing consumer.

Left alone here because it is a judgement call the chrome owner should make deliberately — either wire the
five to `IsVisible` on `CMD_BG_*`/`FIRE_BG`/`ENGAGE_BG`/`COHESION_BG`/`RESUPPLY_BG` and their bars, or
delete them. Note that wiring them means deciding what a hidden bar does to the panels *right* of it,
since every panel X is an absolute literal with nothing tying it to its neighbour (`ingame-player.yaml:53-57`
PITFALL) — hiding one bar would leave a hole, not close up.

## 2026-08-19 — the SR defeat system line claims income is frozen; it is not [low] — **FIXED 2026-08-22 (`wt/sr-message`)**

> Fixed alongside the larger defect a user reported against the same line: it also fired for players
> being defeated in the same tick. Both notifications now read "Production frozen", and the whole
> freeze announcement is gated on `HasActiveTeamSupplyRoute()`. This entry's own closing remark —
> that a frozen-income message "would only matter in team games, since in a 1v1 the player is marked
> `Lost` in the same tick anyway" — is exactly the reasoning the bigger fix runs on. See DISCOVERIES
> 2026-08-22.

`SupplyRouteContestation.OnDefeatBarFull` (`engine/OpenRA.Mods.Common/Traits/SupplyRouteContestation.cs:417`)
prints `"<player> has lost their Supply Route! Production and income frozen."` The trait's
interface list (`:101-102`) is `ITick, ISelectionBar, IAlwaysVisibleBar, IProductionSpeedModifier,
INotifyAddedToWorld, INotifyRemovedFromWorld, ISync` — no income hook — and the file contains no
reference to `PlayerResources`, cash or income. So a passive player keeps accruing the passive
income stream (`PlayerResources.cs:63-69`, 100 per 50 ticks) while being told it has stopped.

Production really does halt, so only the second half of the sentence is wrong. Cheapest fix is to
drop "and income" from the message. If the intent was that income *should* stop, that is a design
change and a bigger one — note it would only matter in team games, since in a 1v1 the player is
marked `Lost` in the same tick anyway (`HasActiveTeamSupplyRoute`, `:433`, is unconditionally
false with no teammates).

Found during the DISCOVERIES curation pass; docs-only branch, so not fixed here.
Related: the public `IsPassive` accessor (`:186`) has zero call sites repo-wide.

## 2026-08-19 — "Built:" on the info panels shows today's date, not the build date [low]

`MainMenuLogic.cs:282` and `ModInfoPanelLogic.cs:24` both set the label with
`DateTime.Now.ToString("yyyy-MM-dd")`. That is evaluated when the widget is constructed — i.e. when the
player opens the menu — so it reports **the date the player is playing**, never the date the build was
made. A stranger running a three-month-old download sees today's date and reads it as a fresh build.

The `MainMenuLogic` copy is live — the main-menu **`v`** dropdown (`mainmenu.yaml:87` `INFO_BUTTON`,
`:101` panel), reachable from the first screen a new player touches, though `Visible: false` until the
button is clicked (`mainmenu.yaml:106`). The `ModInfoPanelLogic` copy is **dead**, and
that is worth recording separately: its root `MOD_INFO_PANEL` occurs exactly once repo-wide
(`info-panel.yaml:1`, its own declaration), nothing opens it, and its ctor requires
`Action onExit, string shellmapName` that no caller supplies — so it cannot even be instantiated as a
plain child. The file sits in `ChromeLayout` (`mod.yaml:204`), so it is parsed and never used. Net: a
fix must touch `MainMenuLogic.cs`; editing only the info panel changes nothing on screen. The same trap
applies to the "Pre-Alpha" version string on the line above it (see `AWAITING-USER.md` item 2).

Not fixed here because there is no build date to read. `engine/Directory.Build.targets:135-177` stamps
only `[AssemblyMetadata("BuildRevision", ...)]` (the git revision, consumed by
`BuildFingerprint.ReadRevision`); nothing stamps a timestamp. A real fix means adding a second
`AssemblyAttribute` alongside `BuildRevision` and reading it back the same way — a build-system change,
and out of scope for a release-identity branch that deliberately touched no yaml or build files.
Cheapest alternative if that is unwanted: drop the line, since a wrong date is worse than no date.

## 2026-08-20: [medium] The infantry concealment cover bonus is granted only by BURNT trees, never living ones (found while: designing the ambush legibility readout, branch `wt/ambush-legibility`, `main @ 4bb3fae9`)

> **SUPERSEDED the same day — the headline and the remedy are both wrong. See the entry
> "the `object-proximity` cover ladder is geometrically unreachable" at the top of this file
> (branch `wt/bug-filing`, `main @ 57822b4e`).**
>
> The *conclusion* below survives — standing in a live forest grants `+0` — but the *mechanism* does
> not, and the difference decides what a fix would look like. This entry implies the bonus is
> obtainable once the forest burns down. **It is not.** `^TreeHusk` carries `Building: Footprint: x`
> with no `Passable` (`husks.yaml:105-107`), so it blocks infantry, unlike a living `^Tree` which is
> walkable (`decoration.yaml:12-14`). The trigger point is the husk's own cell centre, containment is
> a strict `<`, and the nearest sub-cell a soldier can occupy is **244–771 WDist** against radii of
> **182–640**. Enumerated across all **23** husk types, **zero are reachable**. Burning the forest
> down does not help; the ladder is dead outright, not merely misdirected.
>
> Two smaller corrections: the granter block is `husks.yaml:117-121`, and there are **22** actors
> inheriting `^TreeHusk` (23 types including the base), not 21.
>
> The sequencing note at the foot of this entry — *"fixing this makes the bug below go from rare to
> common"* — is built on the same wrong model and should not be acted on; see the correction appended
> to that entry.
>
> Recorded as a worked example of why the ground-truth audit exists: a confident grep by three
> separate parties produced a plausible, quotable, wrong story, and only a fourth pass that checked
> the **geometry** rather than the **grant** caught it.

Static read only — **not observed in a running game**; this branch launches nothing.

`^DetectableInfantryStandard` (`mods/ww3mod/rules/ingame/infantry.yaml:703-716`) consumes
`object-proximity` for the largest single term in the concealment stack: `+1` at 1, `+2` at 2, `+3` at
`>= 3`, capped by `ExternalCondition@ObjectProximity  TotalCap: 3`.

`object-proximity` has **exactly one granter repo-wide**:
`ProximityExternalCondition@ObjectProximity` on `^TreeHusk` (`mods/ww3mod/rules/husks/husks.yaml:118-121`,
`Range: 384`), inherited by the 21 `*.Husk` tree actors (`T01.Husk`–`T17.Husk`, `TC01.Husk`–`TC05.Husk`).
The only other repo hits are the consumer above and a commented-out `^DetectionProximity` block at
`defaults.yaml:161-169`.

`mods/ww3mod/rules/ingame/decoration.yaml`, which defines the **living** trees `T01`–`T17` /
`TC01`–`TC05` (`:100`, `:152`, `:315`), contains **zero** `ProximityExternalCondition`. So standing in
a live forest grants `+0` concealment; the bonus appears only once the forest has burned down and been
replaced by husks. Living trees do carry `Density` (`decoration.yaml:101`), but that feeds
`DensityModifiesDamage` — a damage reduction, a separate system with a separate meaning.

Looks inverted by accident. It matters because it is the biggest term in the stack and it is
unobtainable in the terrain built for it, which is a live candidate for the user's report that
concealment "is hard to detect ... I am not sure what is actually active in game".

Not fixed here: research-only branch, no behaviour changes. **Sequencing note** — fixing this makes
the bug below go from rare to common, so fix that one first or in the same change.

## 2026-08-20: [low] At maximum concealment the gauge draws nothing, so "perfectly hidden" and "broken" look identical (found while: designing the ambush legibility readout, branch `wt/ambush-legibility`, `main @ 4bb3fae9`)

Static read only — **not observed in a running game**.

`Detectable` clamps its level to `[1, MapLayers.VisionLayers - 1]` = `[1,10]`
(`Detectable.cs:90-93`; `MapLayers.cs:75` sets `VisionLayers = 11`). The concealment gauge ladder
`^DetectableRangeCircles` (`infantry.yaml:751-840`) defines tiers for `visibility-1`..`visibility-9`
only. `visibility-10` deliberately has no entry, documented at `infantry.yaml:746-747`: no
`^StandardVision` band carries strength 11, so such a unit is undetectable by standard vision.

The *behaviour* is right; the *readout* is not. A selected unit at `visibility-10` draws no ring at
all, which is pixel-identical to the trait being disabled, the unit being unselected, or the feature
being broken — and it happens exactly when the player has done everything right.

It is reachable, and only by the units a player ambushes with. Sniper (`infantry.yaml:1542`) and
Special Forces (`:1988`) both have base `Detectable.Vision: 5`, so
`5 + 3 (cover) + 1 (prone) + 1 (dug in) = 10`. Standard infantry (base `3`, `:97`) tops out at `8` and
always draws a ring.

Currently rare because the `+3` cover term needs burnt husks (see the entry above). Fixing that bug
makes this one common.

Cheapest fix: one more tier drawing a visually distinct innermost marker (dashed, or faintly filled)
meaning "no standard vision can see you at any range", so the state reads as an achievement rather
than an absence. Design discussion: `WORKSPACE/design/260820-ambush-legibility.md` §7.2.

> **CORRECTED the same day (branch `wt/bug-filing`, `main @ 57822b4e`) — the readout problem is real
> and the reachability arithmetic is wrong, in both directions.** Full working in the entry
> "detectability level 10 is unreachable only because the two units that could reach it are
> `~disabled`" at the top of this file.
>
> - **The `5 + 3 (cover) + 1 + 1 = 10` route does not exist.** It spends the `+3` cover term, which
>   is dead outright — see the correction on the entry above.
> - **"Standard infantry tops out at 8" is wrong; it is 9.** This entry omits veterancy, which grants
>   **+1/+2/+3/+4** (`defaults.yaml:211-222`) and is the largest live concealment modifier in the
>   game. Base `3` + rank-4 `4` + prone `1` + dugin `1` = **9**.
> - **Level 10 is reachable without the cover ladder at all** — `^SN` and `^SF` carry
>   `Detectable.Vision: 5` (`infantry.yaml:1542`, `:1988`), so `5 + 4 + 1 + 1 = 11`, clamped to 10.
> - **But neither can be fielded.** Both carry `Prerequisites: ~disabled` (`:1535`, `:1980`). So
>   "reachable, and only by the units a player ambushes with" is wrong twice over: not by those
>   units, because nobody can build them.
>
> **The practical consequence inverts this entry's sequencing note.** It is not "rare until the cover
> bug is fixed" — it is unreachable until **either** the cover ladder is repaired **or** the Sniper is
> made buildable, and the second is a one-word YAML edit nobody would connect to concealment. Both
> the gauge fix proposed above and the user's ruled visibility floor (clamp minimum detectability to
> 1, `WORKSPACE/ambush-programme/README.md` §7) should be sequenced against that wider trigger, not
> against the cover fix alone.

---

## `TestHarness.TicksPerSecond` is 25, but a plain `run-test.sh` runs at 60ms/tick — every balance `ttk=Xs` is understated 1.5×

Found 2026-08-20 on `wt/humvee-balance` (`main @ cccd5f81`) while deriving the AT specialist's
firing cadence for the humvee balance work.

`mods/ww3mod/scripts/test-helpers.lua:9` hardcodes `TestHarness.TicksPerSecond = 25`, which is the
Red Alert 40ms tick. WW3MOD's default game speed is **60ms** (`mods/ww3mod/mod.yaml:379-381`,
`default: Name: normal, Timestep: 60`), i.e. **16.67 simulated ticks per second**.

`BalanceHarness.RunDuel` (`balance-helpers.lua:45-50`) converts elapsed ticks to its reported
`ttk=%.1fs` by dividing by that 25. So the seconds figure in every
`WINNER=X | ttk=Ys | survivors=...` verdict is **1.5× smaller than the real simulated time**. A
verdict reading `ttk=8.0s` is 200 ticks, which is 12.0s at Timestep 60.

Scope — this is the plain-`run-test.sh` path only, and that is the path every `test-balance-*`
uses. `Test.GameSpeed` is set solely by `run-tournament.sh:302` and `run-synchash.sh:48`; nothing
in `run-test.sh` overrides the mod default. `--speed N` divides `world.Timestep` for wall-clock
only and does not change the tick count, so it neither causes nor masks this.

Consequence: the absolute seconds in balance verdicts and in any doc quoting them are wrong by
1.5×. **Relative comparisons between two balance runs are unaffected**, which is presumably why it
has survived — the harness is mostly used to compare A against B, not to read an absolute clock.

Not fixed here deliberately: correcting the constant re-times every existing `test-balance-*`
verdict at once, so it wants its own change with the affected numbers re-taken, rather than riding
along in a humvee tuning commit.

---

## The move-fallback gate also closed the only route to the minefield selector on an occupied cell

Found 2026-08-21 on `wt/minelayer` (`main @ 6fa1d736`) while ruling `08915556` in or out as the
cause of the "minelaying mode flashes and cancels" report. It is **not** that bug — but it is a
real, narrower behaviour change that `08915556` introduced and nobody has looked at.

`BeginMinefieldOrderTargeter.CanTarget` accepts **only** `TargetType.Terrain`
(`Minelayer.cs:379`). So when the cell you Ctrl+Alt-click is occupied by an actor,
`UnitOrderGenerator.OrderForUnit`'s first pass runs against the ACTOR target, the minefield
targeter refuses it, and the selector used to be reached on the **second** pass — the terrain
retry — exactly like a move order was.

`08915556` gates that retry on `OrderFallbackMath.AllowsMoveFallback`, which returns false for a
hostile actor under a force-attack modifier. Consequence: **Ctrl+Alt on a cell occupied by an enemy
the layer cannot shoot no longer opens the minefield selector at all.** For the E6 engineer, whose
MP5 is not valid against armour, that is every enemy tank. The click now produces nothing.

Not fixed here, deliberately, and it should not be fixed by reopening the retry — that is the
behaviour the user asked to remove and it is pinned by `OrderFallbackMathTest` (8 cases) plus
`test-order-no-move-fallback`. If it is worth fixing, the honest fix is at the other end: let
`BeginMinefieldOrderTargeter` accept an actor target and use its cell, since laying a minefield is
a terrain intent that happens to have been clicked on top of something. That is a real design
decision about what Ctrl+Alt-on-an-enemy should mean for a minelayer, so it wants asking rather
than assuming.

Severity: low. The natural gesture is to click bare ground, which is unaffected — verified by
control flow, `OrderForUnit` returns from the FIRST pass at `UnitOrderGenerator.cs:248` and never
consults the gate at `:254`.

## 2026-08-21 — [med] A map-layer range at or above 56 cells CRASHES on actor spawn

`Map.FindTilesInAnnulus` throws `ArgumentOutOfRangeException` when `maxRange` exceeds
`MaximumTileSearchRange` (56). Reached via `AffectsMapLayer.ProjectedCells` ->
`MapLayers.ProjectedCellsInRange` on `INotifyAddedToWorld.AddedToWorld`, so the game dies
the instant the actor spawns -- not at load, not at lint.

Observed: `test-radar-only-targetable` set `Radar.Range: 56c0` and crashed with
"The requested range (57) cannot exceed the value of MaximumTileSearchRange (56)".
Note 56c0 resolves to 57, so the effective ceiling is 55c0, not 56c0.

Nothing shipped crashes today -- radar ranges in the mod are 24c0/30c0/42c0, and the
largest map-layer range anywhere is 50c0 (structures-neutral.yaml:123). But that is only
5 cells of headroom from a hard crash, `--check-yaml` does not catch it, and the failure
presents as a crash rather than a validation error, so the next person to widen a radar
for a scenario or a new unit hits it the same way.

Worth a lint rule bounding any AffectsMapLayer-consuming range below 55c0.

## 2026-08-21 — [high] Any scenario asserting on fog/visibility is VACUOUS under the test harness

`TestModeLogic.cs:31` sets `world.RenderPlayer = null`, and every `World.FogObscures` /
`ShroudObscures` overload is guarded `RenderPlayer != null && ...` (`World.cs:109-115`).
So under Test.Mode **fog obscures nothing and shroud hides nothing** -- every actor on the
map reads as visible and mouse-targetable regardless of actual visibility state.

Found via `test-radar-only-targetable`, whose negative rung ("a helicopter revealed by
NOTHING must not be targetable") failed. The scenario names both candidate causes in its
own failure text, which is what made this diagnosable: it is NOT that the radar fix
widened into a wallhack -- the fix is fine and its NUnit evidence stands -- it is that the
predicate under test is short-circuited before it can be exercised.

Consequence: the POSITIVE rung of such a scenario passes VACUOUSLY. Any existing scenario
that asserts something is visible/targetable under fog has been proving nothing. Sibling
`test-aa-detection-fog` is the first place to check.

Fixable: `World.cs:100` explicitly permits setting RenderPlayer while `TestMode.IsActive`,
so a `Test.SetRenderPlayer(player)` binding would make these scenarios discriminate. Until
then, visibility behaviour must be pinned at the NUnit layer against a World-free core --
which is exactly what the radar fix's own MouseTargetVisibilityTest already does.

## 2026-08-21 — [med] The idle supply seek refuses the Logistics Centre for infantry, on a false premise

`AutoSeekSupplies.CanServe` rejects any provider carrying a `DockedCondition`
(`AutoSeekSupplies.cs:465`) with the comment *"A docking-gated provider (the Logistics Center's
unit.docked) does not resupply by proximity — walking into its aura would achieve nothing"*, and
rejects it again on the `RearmCondition` gate (`:471`) because the LC's SupplyProvider names
`replenish-vehicles` while infantry hold `replenish-soldiers`.

**Walking into its aura achieves a great deal.** `LOGISTICSCENTER` also carries
`ProximityExternalCondition@ReplenishSoldiers` at `Range: 4c0` (`structures.yaml:407-411`), and every
infantry `ReloadAmmoPool` is gated on `replenish-soldiers` (`infantry.yaml:1188`, `:1217`, +12). A
soldier who stands within 4 cells of an LC reloads. The comment is true of the SupplyProvider push it
describes and false of the actor it names.

Effect: a PARTIALLY dry soldier (rifle spent, AT round left) beside an LC never moves — the idle seek
is the only path that can fire for him and it refuses the only host in range. A fully dry soldier is
fine, because the other two paths go through `AmmoPool.ChooseResupplier`, which applies neither gate
and returns the LC (`^E3.RearmActors` names it, `infantry.yaml:1242`).

Not fixed on `wt/resupply-tiers`: relaxing `CanServe` changes the idle seek for every infantryman in
the mod with no measurement behind it, and the widened dispatch predicate reaches the LC through
`ChooseResupplier` anyway once a pool is authored `Essential`. The narrow fix, if wanted, is to admit
a docking-gated provider when it grants a proximity condition the seeker's own `ReloadAmmoPool`
consumes — which is a real query, not a YAML tweak.
---

## 2026-08-21: [medium] `FTUR` Flame Turret is permanently disarmed by one close-range burst (found while: the `Essential` ammo-pool census, branch `wt/essential-census`, `main @ 697be28e`)

`FTUR` carries a single `AmmoPool` of 10 (`mods/ww3mod/rules/ingame/structures-defenses.yaml:932-941`)
shared by both of its armaments. `Armament@2` (`Flamespray.heavy`, the short-range turret) declares
`AmmoUsage: 10` (`:927`) -- one burst consumes the entire pool.

The actor has **no `Rearmable`** and, unlike its two siblings in the same file, **no
`ReloadAmmoPool`**: `CRAM` has one at `:658` and `AGUN` at `:735`, `FTUR` has none. Nothing else
refills a pool in the field -- `AmmoPool.Reload()` has zero callers engine-wide, so `ReloadDelay: 40`
on this pool drives nothing (`DOCS/reference/economy.md`, "An `AmmoPool` never refills itself").

Consequence: after a single secondary shot, a 1000-credit defensive structure is inert scenery for
the remainder of the match, with no player-visible explanation. The primary `FireballLauncher` is
disarmed with it, since both armaments draw the same pool.

READ, not measured -- established from source at `697be28e`; nobody has watched a Flame Turret go
silent in game. Confirm by: place an `FTUR`, walk one unit into melee range, verify it never fires
again.

Not fixed here (this branch is document-only). The likely fix is a `ReloadAmmoPool` mirroring
`CRAM`/`AGUN`, but the `AmmoUsage: 10`-against-a-10-pool sizing looks like the more basic mistake and
should be settled first.

## 2026-08-21: [low] `strykershorad` sidebar description is copy-pasted from the Stryker IFV and never mentions air defence (found while: the `Essential` ammo-pool census, branch `wt/essential-census`, `main @ 697be28e`)

`mods/ww3mod/rules/ingame/vehicles-america.yaml:879` reads
`Description: Wheeled IFV for rapid troop transport.\n\n - Autocannon\n - Carries infantry\n - Armor: Medium`.

The actor's `Tooltip.GenericName` is "Stryker Short Range Air Defense" (`:866`), and it carries
8 `Stinger.quad` SAMs (`:941`) and 4 `Hellfire.strykershorad` (`:974`) -- neither of which the
description mentions, while it advertises troop transport. The player-facing text for the faction's
mobile SAM platform does not tell the player it can shoot aircraft.

Related stale text at the same actor: `Armament@1` is `Weapon: 25mm.Bradley # 30mm.Stryker` (`:912`)
-- the comment names a weapon the armament does not use.

READ. Confirm by: opening the build sidebar and reading the tooltip.

## 2026-08-22: [high] `AirToAirMissile` and `SurfaceToAirMissile` never set `Penetration`, so they do ~1/20th of their printed damage to armoured aircraft (found while: the AA inter-shot-interval sweep, branch `wt/aa-interval`, `main @ 84328dcc`)

`DamageWarhead.cs:24` defaults `Penetration` to **1**, and `:222-231` applies
`damage = damage * penetration / thickness` whenever `penetration < Armor.Thickness`. Neither of these two
weapons declares `Penetration`, so both inherit 1:

- `AirToAirMissile` (`mods/ww3mod/rules/weapons/weapons-missiles.yaml`, `Warhead@Spread` Damage 1000 +
  `RandomDamageAddition: 1000`) — vs the F-16 it actually duels (400 HP, `Armor: Medium/Thickness: 10`,
  `aircraft-america.yaml:575-577`) it lands **100-199 per missile, so 3-4 missiles per kill**. Against a
  Heavy/Thickness-20 aircraft it is 50-99, i.e. 8-16 missiles.
- `SurfaceToAirMissile` / `.double` (Damage 2000 + rand 1000), mounted on a defence structure at
  `mods/ww3mod/rules/ingame/structures-defenses.yaml:799` — vs an 800 HP Heavy/Thickness-20 helicopter it
  lands **100-149, so 6-8 missiles per kill**.

By contrast `Stinger` / `9M311` set `Penetration: 20` and take full 5000 damage against every air target in
the mod. That single field is the whole difference between a one-shot AA missile and one that needs eight.

Consequence beyond the damage itself: these two are the reason the AA double-launch fix
(`9M311` `BurstWait: 40 -> 58`) was NOT applied to them. Their short intervals are currently load-bearing —
a SAM site that needs 6-8 missiles legitimately must fire fast. **If someone raises their `Penetration`,
their intervals must be re-audited against missile lifetime in the same pass** (48 ticks for
`AirToAirMissile`, 55 for `SurfaceToAirMissile`), or the fix will re-introduce the overkill bug on the
weapons currently exempt from it.

Not fixed here: raising `Penetration` is a large unmeasured combat change and needs the combat-sim
(`tools/combat-sim/`) plus a balance pass, not a one-line YAML edit inside a timing branch.

Related, same root: the MiG-29 has `Armor: Type: Medium` with **no `Thickness`**
(`aircraft-russia.yaml:596`) where the F-16 has `Thickness: 10` (`aircraft-america.yaml:577`). Thickness 0
skips the scaling block entirely, so the MiG takes 10-20x more damage from every Pen-1 weapon than the
aircraft it is matched against. Almost certainly an authoring omission rather than a design choice.

READ. Confirm by: `grep -n "Penetration" mods/ww3mod/rules/weapons/weapons-missiles.yaml` and checking
which AA warheads are absent from the output.

---

### 2026-08-22 — found during the knowledge-bank curation pass (branch `wt/curation-0822`)

Four defects surfaced while re-deriving DISCOVERIES entries against code at `main @ 12e0addd`.
None was fixed here — this branch is docs-only. Each was read at HEAD; none was observed in a
running game.

- [2026-08-22] [low] **A shipped YAML comment repeats a wrong burst-timing rule.**
  `mods/ww3mod/rules/weapons/weapons-missiles.yaml:589-594` asserts that `BurstDelays >= BurstWait`
  trips the stale-burst rearm, and hedges "58 or 59 depending on trait order". Both are wrong: the
  per-armament countdown reaches zero exactly `BurstDelays` ticks after the shot under either
  ordering, so the trip condition is `BurstDelays > BurstWait` and an exactly-equal value is SAFE.
  **Nothing is mis-tuned** — `Stinger.quad`'s 58-against-60 is safe under either reading — so this is
  a comment fix, not a behaviour fix. Corrected version banked at `DOCS/reference/missiles.md` §9.
  (found while working on: curating the 2026-08-21 `BurstDelays` entry, which is the source of the
  wrong claim and is now tagged `[rejected]`)

- [2026-08-22] [low] **FIXED (`wt/tooltip-elements`, 2026-08-30) — Three helicopters advertise the
  wrong armour class in player-facing text.** `HELI` (`aircraft-america.yaml:315`), `HIND`
  (`aircraft-russia.yaml:133`) and `MI28` (`:309`) carried UI `Description` strings reading
  `Armor: Medium`, while each actor's actual `Armor: Type:` is **Heavy** (`:328`, `:146`, `:322`
  respectively). The tooltip lied about a stat players use to pick counters. Cosmetic/data only —
  no code reads the description.
  **Two more of the same shape were found alongside them**: `LCCV` (`vehicles.yaml:652`) and `MNLY`
  (`:470`) both said `Armor: None` against a real `Light` (`:663`, `:478`).
  **Fixed by deletion, not by correction.** The armour bullet was removed from all 41 live
  descriptions and armour is now emitted as a typed `StatRow` by `ArmorInfo`, so the number a player
  reads is resolved from the ruleset at load time and cannot drift from it again. All five actors set
  `Armor: Type:` on themselves rather than inheriting it, so the description text was unambiguously
  the wrong half — this is a text fix, not a balance change, and no `Armor:` value was touched.
  (found while working on: enumerating airframe armour classes for the `Targetable@Armor` promotion)

- [2026-08-22] [low] **Ten live sites assert a duration the code does not produce.** The mod runs at
  16.67 tps (`mod.yaml:382`), not the RA-era 25. A tree sweep found ten comments still stating the
  wrong figure: `scripts/test-helpers.lua:7`; `scripts/scenario.lua:31` ("~15 seconds at 25 tps",
  really 22.5 s); `weapons-missiles.yaml:86` ("≈ 2 s", really 3.0 s); `SmartMove.cs:25` ("~3 sec at
  25 tps", really 4.5 s); `CohesionSlotMemory.cs:27` and `:31`; `BotVsBotMatchWatcher.cs:93`;
  `GroundStates.cs:310` (`// ~12.5 seconds` for 750 ticks — a *60*-tps error, really 45 s);
  `Actor.cs:338` ("~1s at 60 TPS" — conflates 60 ms with 60 tps, really 3.6 s);
  `DOCS/recipes/TELEMETRY.md:44`. `SupplyRouteContestation.cs` was corrected in-tree on 2026-08-22
  and is not in this list. **`test-helpers.lua` must not be "fixed" by changing the constant** — that
  tightens every existing scenario deadline by a third at once; see `DOCS/recipes/AUTOTEST.md`.
  (found while working on: promoting the harness tick-rate entry)

- [2026-08-22] [med — LATENT] **The `Essential ⊆ Rearmable.AmmoPools` load guard is necessary but not
  sufficient, and the reasoning banked with it was an incomplete enumeration.** A 2026-08-21 entry
  claimed `ReloadAmmoPool` is the only refill path bypassing `Rearmable`. It is not:
  `EnterCarrierMaster.cs:49-53` refills **every** pool on the actor via
  `self.TraitsImplementing<AmmoPool>()`, and `CarrierMaster` is in use in this mod
  (`infantry.yaml:2323`); the Lua `GiveAmmo` API (`AmmoPoolProperties.cs:62`) is a fourth writer.
  No actor today combines a carrier with a divergent `Rearmable.AmmoPools`, so nothing is broken —
  but the guard's sufficiency argument does not hold for one, and the guard would not complain.
  **Not fixed:** widening it needs a decision about whether carrier refill *should* respect
  `Rearmable`, which is a gameplay question. Corrected text banked at `DOCS/reference/economy.md`.
  (found while working on: verifying the three-refill-sites entry, now tagged
  `[promoted WITH A CORRECTION]`)

- [2026-08-27] [low] **A reachable rearm host can go un-sought, because the nearest host is picked in
  a different metric from the one the leash judges.** `AmmoPool.ChooseResupplier` ends in
  `ClosestToIgnoringPath`, which ranks by `LengthSquared` (Euclidean), while the dry leash is applied
  by `SupplyHuntMath.WithinCellBudget`, which measures `ChessboardCells` — max(|dx|,|dy|). The two
  disagree: against a leash of 30, a host at (31,0) is Euclidean-NEARER than one at (25,25) but
  Chebyshev-OUTSIDE, so the pick returns the one host that fails the leash and the unit does not
  self-dispatch to the (25,25) host it could have reached. Pre-existing; the symptom is a stall, not
  a loss. **Deliberately not fixed here:** the dangerous half was defused on `wt/resupply-fallback` —
  `AnyRearmHostWithinLeash` sweeps every host in the leash's own Chebyshev metric, so this shape can
  no longer be mistaken for "nothing exists nearby" and evacuated — but correcting the SEEK means
  changing `AutoRearm`'s own pick, which was judged too wide to add to a branch already under review.
  (found while working on: the Auto-stance evacuate fallback, review round 2)
- [2026-08-27] [med] **The A10 declares two traits twice, so it is silently mis-configured AND
  un-overridable from any map.** `mods/ww3mod/rules/ingame/aircraft-america.yaml` gives `A10:` a
  `ReloadAmmoPool@1` at `:499` (`AmmoPool: primary-ammo`) and a second `ReloadAmmoPool@1` at `:529`
  (`AmmoPool: secondary-ammo` — evidently meant to be `@2`, which is exactly what `MI28` does
  correctly at `aircraft-russia.yaml:414`), plus `RenderSprites` at `:456` (`PlayerPalette: playertd`)
  and again at `:576` (`Scale: 1.2`). On an ordinary load the later declaration wins with no
  diagnostic, so **the A10's 30mm GAU-8 pool has no reload rule at all** (only the Hellfire pool
  does) and the `playertd` palette is dropped. The duplication only becomes loud when a map overrides
  the actor: that forces `MiniYaml.Merge`, which refuses and throws
  `"duplicate values found for the following keys: ReloadAmmoPool@1 ... RenderSprites ..."`, taking
  the whole map's ruleset down. Reproduce without launching: add any `A10:` block to a scenario's
  `rules.yaml` and run `Mod=ww3mod ./utility.sh --check-yaml <scenario>`.
  **Not fixed here:** deduplicating restores a reload rule and a palette, i.e. a live behavioural
  change to a shipped unit, which wants its own commit and its own test rather than riding along on a
  diagnostic scenario. Whoever takes it should check whether the 30mm was *intended* to be
  non-reloading before "fixing" it.
  (found while working on: `test-aircraft-breakoff-midrun`, where an `A10: AutoTarget: ScanRadius`
  pin would not load)

- [2026-08-27] [med] **A scenario that needs a launch argument cannot be run correctly in a mixed
  batch, and will read as permanently red.** `run-test.sh:735` splices `AUTOTEST_EXTRA_ARGS`
  straight from the environment into the launch command, and there is no per-scenario convention for
  it — no `args` file, no `description.txt` key the harness reads. So a scenario whose correctness
  depends on a flag has no way to declare it. `test-unscouted-building-hidden` is the live example:
  it requires `Test.KeepRenderPlayer=true`, because `TestModeLogic.cs:30` nulls `World.RenderPlayer`
  for a real player slot and every `World.FogObscures` overload returns false when it is null, which
  reports the entire map as clickable and would show a fog leak whether or not one exists. In a mixed
  batch the flag is either absent — that scenario fails — or exported globally, in which case **every
  other scenario in the batch runs with a non-default `RenderPlayer`**, which is a silent change to
  what they measure, not a loud one.
  It fails loudly and its failure text names the cause, so a batch run is not misleading. The risk is
  slower: a permanently-red scenario in a batch is how a guard gets deleted six weeks later by
  someone tidying up.
  **Suggested shape:** an optional `args` file in the scenario directory that `run-test.sh` appends
  to `AUTOTEST_EXTRA_ARGS` for that scenario only. Not attempted here — it is a harness change and
  wants its own review.
  (found while working on: `wt/fog-leak`, verifying the FrozenUnderFog visibility restoration)

- [2026-08-27] [high] **Rearming at the Logistics Centre is FREE — the docking path never consults
  the depot's supply.** `Resupply.cs:131` sets its rearm branch from `Rearmable.RearmActors`
  membership alone (`!rearmable.Info.RearmActors.Contains(host.Info.Name)`), and the rearm itself is
  `Rearmable.RearmTick` (`Resupply.cs:301` → `Rearmable.cs:57-78`), which calls `AmmoPool.GiveAmmo`
  and reads no `SupplyProvider` at all. Nothing downstream charges for it either: `SupplyProvider`
  implements **neither** `INotifyResupply` nor `INotifyDockHost`, the only two hooks `Resupply` fires
  at the host (the sole implementers engine-wide are `WithResupplyAnimation` and `WithRepairOverlay`,
  both render traits). So any actor whose `Rearmable.RearmActors` names `logisticscenter` refills to
  full at a Centre holding **zero** supply.
  The depot's supply is spent only by the *push* path (`SupplyProvider.Tick`, which does deduct at
  `:993`), and that path reaches infantry via the 4c0 aura arm and only `himars`/`iskander` via the
  docked arm — they are the only two vehicles declaring the `replenish-vehicles` ExternalCondition
  that `IsValidTarget`/`MatchClientele` requires. Every other vehicle naming the Centre (`humvee`,
  `m113`, `bradley`, `abrams`, `m109`, `strykershorad`, `btr`, `bmp2`, `t90`, `giatsint`, `tunguska`,
  `t72`, `mnly`) is invisible to the push and rearms **entirely** through the free path.
  **Consequence for anyone touching dispatch:** `AmmoPool.ChooseResupplier`'s `CurrentSupply > 0`
  filter and `ChooseAffordableResupplier`'s `>= SupplyValue` filter are both gating a TRIP against a
  cost that is never charged when the host is a docking host. Swapping the seek path
  (`AutoSeekSupplies.cs:285`) to the affordable chooser therefore looks like a parity fix with
  `AmmoPool.AutoRearmIfDry`'s Auto arm and is a REGRESSION for Logistics Centre trips: it withholds a
  journey that would have succeeded. `e36ab29a` did exactly that in `AutoRearmIfDry`'s Auto arm — see
  the entry below.
  **OBSERVED, not merely derived.** `tools/autotest/scenarios/test-vehicle-rearms-at-empty-depot`
  puts a dry `abrams` six cells from a Centre zeroed with `Test.SetSupply` and sends it there BY
  NAME (`Test.IssueResupplyAt`, which exists because every ordinary route runs `ChooseResupplier`
  first and that filter would decline to send it at all). Both halves asserted: ammunition arrives
  AND supply never moves. GREEN — rearmed inside ~175 ticks with the depot still reading 0. RED
  (`RearmActors: truk`, so the membership test at `Resupply.cs:131` fails and nothing else changes)
  — alive and at zero rounds through tick 1500.
  **Not fixed here:** charging for the docking rearm is an economy change touching every vehicle and
  both bot profiles, and the honest first step is deciding whether free-at-the-LC is the intent
  (`economy.md` prices supply 1:1 and gives the Centre 2250, which reads like it was meant to be
  spent).
  (found while working on: extending the dry-unit resupply re-ask from soldiers to vehicles)

- [2026-08-27] [high] **OBSERVED (was inferred): infantry within 4 cells of an empty Logistics Centre
  refill for free through an ungated proximity condition.** Separate from the entry above and
  reached by a different route. `LOGISTICSCENTER` carries
  `ProximityExternalCondition@ReplenishSoldiers` (`mods/ww3mod/rules/ingame/structures.yaml`, in the
  block beginning at `:389`): `Condition: replenish-soldiers`, `Range: 4c0`, `ValidRelationships:
  Ally`, and `RequiresCondition: !build-incomplete && !disabled` — **no supply term**. Every soldier
  class declares `ReloadAmmoPool@1: RequiresCondition: replenish-soldiers`, and `ReloadAmmoPool` is
  stock OpenRA: a timed `Reload` with no range check and no supply accounting. So the condition
  appears to be granted, and the free trickle to run, irrespective of what the Centre holds.
  This is *distinct from* the Centre's supply-gated `SupplyProvider` aura arm
  (`AuraRearmCondition: replenish-soldiers`, `AuraRange: 4c0`, which does deduct) — the two grant the
  same condition at the same radius, one metered and one not, which is likely how it went unnoticed.
  **CONFIRMED 2026-08-27 by `tools/autotest/scenarios/test-who-pays-for-a-rearm`.** A rifleman three
  cells from a Centre held at zero gained **14 rounds in 700 ticks** — one every 50 ticks, exactly
  `ReloadAmmoPool`'s default `Delay: 50` / `Count: 1`, so the rate identifies the mechanism and not
  merely the effect — while `Test.GetSupply` read 0 at every one of the 28 samples. RED, deleting
  `-ProximityExternalCondition@ReplenishSoldiers:` from the Centre and changing nothing else: the
  same rifleman stayed at **0 rounds** for the whole run, with the verdict text
  "free infantry trickle: false. docked double-serve: true". That second clause is the control on the
  control — the himars half of the same scenario came out byte-identical across both arms, same
  ticks and same numbers, so the removal took away one variable and no others.
  The metered arm provably cannot account for the green arm: `SupplyProvider.cs:968` skips any pool
  whose `SupplyValue` exceeds `currentSupply`, and the rifleman's is 1 against a depot at 0.
  (found while working on: `test-dry-soldier-retry-after-refill`, phase 1 of the vehicle re-ask work)

- [2026-08-27] [high] **`e36ab29a`'s affordability pick is a regression for DOCKING hosts, which is
  the only kind of host a vehicle has.** The merge landed two separable things in
  `AmmoPool.AutoRearmIfDry`'s Auto arm. They do not share a verdict.
  **The drained-versus-absent distinction STANDS.** Its hopelessness inputs (`AnyRearmHostWithinLeash`,
  `AnyMobileRearmHost`) deliberately ignore stock, so a present-but-zeroed depot yields `HoldAndFlag`
  rather than `Evacuate`. Nothing above disturbs that: at exactly zero supply `ChooseResupplier`
  refuses the depot as a destination on every arm, so no unit is dispatched there in real play and
  waiting really is the right disposition. That half is a straight improvement over driving off the
  map permanently.
  **The affordability pick does NOT stand.** `ChooseAffordableResupplier` asks whether a host can
  afford one batch, and that is the wrong question for a host reached through `Resupply` — the rearm
  there is free (see the entry above, now observed). Consequences, worst first:
    * *No affordable host anywhere, one poor host within leash.* Pre-merge: `ChooseResupplier` returns
      the poor depot, the unit drives over and **rearms completely, for nothing**. Post-merge: the
      chooser returns null, `suppliedHostWithinLeash` is false, the hope scan finds the depot within
      leash, and `SupplyHuntMath.DecideAutoDisposition` falls through to `HoldAndFlag` — the unit
      **stands still beside a depot that would have refilled it**, flagged for a truck that, for a
      vehicle, can never come (no truck names a vehicle clientele). Strictly worse.
    * *The merge's own worked case* — nearest depot holds 750, one eight cells further holds 2250.
      Both would have served for free, so the fix buys a longer drive and nothing else. Harmless,
      but not the improvement it was merged as.
  Reach: every vehicle (the Logistics Centre is their only host and it is a docking host), plus
  infantry at a Centre, where it bites the `at`/`aa` specialists whose `SupplyValue: 65` makes
  `> 0` and `>= 65` genuinely different predicates. Infantry at a TRUCK or CACHE are unaffected and
  correctly gated: those have no `DockedCondition`, so `AutoRearm` routes them to
  `SeekSupplyProvider` and the metered push really does pay for the ammunition.
  **Smallest honest correction, NOT built:** the discriminator already exists one call away —
  `AmmoPool.AutoRearm:780` routes on `string.IsNullOrEmpty(supplyProvider.Info.DockedCondition)`.
  Teaching `HostCanAffordSomethingWeNeed` the same split — a docking host can always serve on
  arrival, a push host must be able to afford a batch — fixes both callers at one site and leaves
  the truck/cache behaviour byte-identical. Reverting the pick outright is the cruder alternative
  and also loses the genuine two-depot improvement. Which to take depends on whether the free
  docking rearm is intended, so settle that first.
  (found while working on: extending the dry-unit resupply re-ask from soldiers to vehicles)

- [2026-08-27] [high] **A docked `himars`/`iskander` is served by BOTH arms at once, so one of its two
  rounds is paid for and the other is free.** Measured, with the overlap window shown rather than
  assumed. `tools/autotest/scenarios/test-who-pays-for-a-rearm` sends a dry himars to a Centre
  holding a full 2250. Trajectory: docked at tick 100; at tick 150 ammo 0->1 and supply 2250->750
  (the metered push arm, one 1500 batch); at tick 225 ammo 1->2 with supply **still 750** — and 750
  is less than a 1500 batch, so that round provably was not paid for. Two arms, same unit, 75 ticks
  apart, both while docked.
  The arithmetic is why one run settles it: himars is `Ammo: 2`, `ReloadCount: 1` (default),
  `SupplyValue: 1500`, so the push arm can afford exactly ONE round out of a 2250 depot. Rounds
  gained beyond rounds paid for cannot have been paid for.
  **This refuted a real worry rather than confirming a guess.** The concern going in was that
  `SelfAssignedErrandIsOver` might end the Resupply errand on the first round, so the two arms would
  never overlap and a null result could not be told apart from "could not have happened". The trace
  shows they did overlap — `dispatchedBecauseDry: false` keeps that exit test from firing.
  **Consequence for metering:** these two are the ONLY actors declaring `replenish-vehicles`, so they
  are the only vehicles the push arm can select. Metering `Rearmable.RearmTick` without making the
  two paths mutually exclusive for a docked actor converts today's double-REARM into a
  double-CHARGE, on two independent cadences (`RearmDelay: 25` against the pool's own `ReloadDelay`).
  Being individually correct is not enough; they must not both run.
  (found while working on: metering the dock path, ahead of the design)

- [2026-08-27] [med] **"Nothing is free ever" is not yet literally true: a RESIDUE of free ammunition
  survives the charging change, and it is deliberate scope, not an oversight.** `SupplyProvider`
  grants its `RearmCondition`/`AuraRearmCondition` to the client it is serving
  (`SyncTargetCondition`), and on infantry that condition is what enables the soldier's own
  `ReloadAmmoPool` — a timed trickle with no range check and no supply accounting.
  `ReloadAmmoPool.remainingTicks` is NOT reset when the condition drops (`PausableConditionalTrait`
  merely stops it decrementing), so it accumulates across successive grant windows and eventually
  fires, handing over rounds nobody paid for on top of the metered batch.
  **Bounded, and quantified rather than waved at.** It cannot fire at a depot the provider will not
  serve from — a drained Centre grants nothing, which is why the free-trickle removal measured clean.
  Where it does fire, the rate is negligible against the metered arm it rides on: for an `ar` the
  metered aura delivers `ReloadCount: 50` rounds per `AuraRearmDelay: 6` ticks, against the trickle's
  `Count: 1` per `Delay: 50` — roughly 400x, and the soldier is full in ~60 ticks having spent 10
  supply.
  **Not fixed here on purpose:** closing it means gating `ReloadAmmoPool` on something other than the
  serving condition, or resetting `remainingTicks` on grant — a second unmeasured behavioural change
  riding alongside the charging one, which is exactly what the charging commit must not carry. Filed
  so the user learns it in writing rather than by discovering the ruling is not literally satisfied.
  (found while working on: metering the dock path)

- [2026-08-27] [low] **Legibility regression nobody asked for: the replenishing pip now shows on one
  soldier at a time instead of everyone at the depot.** `WithDecoration@AmmoReplenishing`
  (`mods/ww3mod/rules/ingame/infantry.yaml:218-224`) keys on `replenish-soldiers`. That condition used
  to come from the Logistics Centre's blanket 4c0 `ProximityExternalCondition`, so every soldier in
  range wore the pip simultaneously. With that trait removed (charging change, 2026-08-27) the only
  grant is `SupplyProvider`'s own, which is issued to the ONE client it is currently serving — so a
  squad resupplying at a Centre now blinks the pip between its members rather than showing it on all
  of them.
  Purely visual: nothing gates on the decoration, and the underlying resupply is unchanged and faster
  per-soldier than the trickle it replaced. **Not fixed here** because the honest fix is a decoration
  keyed on "this pool is being refilled" rather than on the serving condition, which is a UI change
  wanting its own look — see `DOCS/recipes/SCREENSHOT.md`, since the verdict is visual.
  (found while working on: metering the dock path)

- [2026-08-27] [med] **KNOWN LIMIT, not bounded by reading: a docked client can be starved by a
  persistently needier neighbour.** `SupplyProvider.CanSelect` deliberately ignores `currentTarget`
  (contention is not a refusal — `UpdateTarget` re-picks by greatest need every `ScanInterval`), so
  `Rearmable.RearmTick` stands down for a docked client the push arm owns but is not currently
  serving. Entered with the deferral in `f8b424f6`; this branch owns it.
  **What CAN be proved.** With a fixed client population and no re-emptying, service terminates:
  `CalculateNeed` is `totalMissing / totalCapacity`, every served batch strictly reduces the served
  client's missing count, and a client reaching full returns `NoDemand` and leaves the candidate set
  altogether. That is a strictly decreasing potential, so the docked client is eventually greatest-need
  and is served.
  **What CANNOT.** The argument fails as soon as a competitor's need REGENERATES — infantry fighting
  beside the Centre, emptying and refilling — because selection is a strict `need > bestNeed` and a
  docked himars sits at a fixed 0.5 (one of two rounds, and it cannot fire while docked:
  `PauseOnCondition: ... unit.docked`). Whether it is ever greatest then reduces to a RATE argument —
  the aura arm fills an `ar` at 50 rounds per 6 ticks, far faster than a rifleman consumes — which
  holds for one neighbour and is not obviously true for a squad of ten cycling, on a Centre kept
  topped up by `AbsorbsSupplyCache`. That is a quantitative claim about unit counts and fire rates,
  not a structural bound, and it is not settled here.
  **Why it matters if it happens:** the starved client stays on its `Resupply` activity, so
  `IsSeekingRearm` is true and `StarvingRecruitGate` withholds it from every bot module — the same
  shape as the wedge fixed in `b29930e4`, but requiring contention rather than being the steady state.
  **And nothing times it out.** `AutoSeekSupplies.ReturnErrandStallTicks` (300) covers only errands
  that trait dispatched via `BeginWatching`, and that trait is on `^Soldier` alone — a vehicle
  dispatched by `AutoRearmIfDry` or by a player order has no stall guard, and `Resupply` itself
  carries no timeout.
  **Deliberately NOT fixed:** a speculative guard here would change behaviour nobody has measured, and
  the obvious ones are worse than the disease — reading `currentTarget` in `CanSelect` would make a
  client abandon the dock merely because someone else was mid-batch, which is a new stranding shape.
  Recorded as a known limit instead.
  (found while working on: metering the dock path — reviewer flagged, reading could not settle it)
- [2026-08-30] [high] **The Logistics Centre's own sizing comment was wrong by 11×, and it is the
  sentence the depot economy was sized against.** `structures.yaml:485-487` claimed an E3 rifleman's
  full refill was 5 supply, so "2250 buys ~450 rifleman refills against the truck's ~150". It counted
  only the DMR pool and ignored `AmmoPool@2 secondary-ammo` — one RPG round at `SupplyValue: 50`
  (`infantry.yaml:1245-1251`) — which **is** in his `Rearmable.AmmoPools` (`infantry.yaml:1282`) and
  so **is** refilled by that aura. True figures: 55 supply, **~41** refills per Centre and **~13** per
  truck. **Comment corrected in this commit; the sizing question it misled is NOT closed.** At
  `AuraRearmDelay: 6` a full Centre is roughly 90 seconds of continuous infantry service, and whoever
  chose 2250 believed it was far more. Anyone re-tuning `TotalSupply` should start here.
  (found while working on: `wt/supply-economics`, the pre-merge audit for paid ammunition)

- [2026-08-30] [med] **`^E6`'s rifle has exactly one refill route and the pending "all ammo costs
  supply" change may remove it.** `AmmoPool@1 primary-ammo` (100-round MP5, `infantry.yaml:1870`) is
  absent from `Rearmable.AmmoPools: secondary-ammo` (`infantry.yaml:1954`), so no host touches it —
  all three host paths iterate the filtered `RearmableAmmoPools` (`Rearmable.cs:44`). Its only refill
  today is the free `ReloadAmmoPool@1` at `infantry.yaml:1878`. If that trait starts charging or is
  gated, **the engineer loses his rifle permanently after one magazine** and never dispatches for it,
  because his listed C4/mine pool keeps him non-dry. Either add `primary-ammo` to his
  `Rearmable.AmmoPools` or confirm the change leaves `ReloadAmmoPool` free.
  (found while working on: `wt/supply-economics`)

- [2026-08-30] [low] **Two expensive pools name armaments that do not exist, so their attack-path dry
  dispatch can never fire.** `^E6.secondary-ammo` declares `Armaments: secondary` (`infantry.yaml:1901`)
  and `^SF.secondary-ammo` declares `Armaments: c4` (`infantry.yaml:2136`); neither actor has an
  `Armament` by that name — both pools are consumed by traits (`Demolition.UseAmmo`,
  `Minelayer.AmmoPoolName`). `AmmoPool`'s `INotifyAttack` dispatch is keyed on
  `Info.Armaments.Contains(a.Info.Name)` (`AmmoPool.cs:658`), so only the idle trigger reaches them.
  Pre-existing and already described generically at `economy.md:53`; recorded here with the numbers
  because these are the *expensive* halves — 150 of E6's 155 and 99 of SF's 104.
  (found while working on: `wt/supply-economics`)

- [2026-08-30] [low] **`A10.Airstrike primary-ammo` has `Ammo: 40` against `ReloadCount: 25`**
  (`aircraft-america.yaml:711-712`) — the one pool of 63 in the mod that is not an exact multiple.
  This is precisely the floor-vs-ceiling pattern the Paladin comment (`vehicles-america.yaml:650-655`)
  was written to prevent: `CustomSellValue` floors missing batches while the tooltip ceilings them.
  No play impact — the actor is a support-power spawn, not buildable, with no `Rearmable` — but it is
  the only live counterexample to an invariant two other comments assert corpus-wide.
  (found while working on: `wt/supply-economics`)
---

### 2026-08-30 — tooltip audit (`wt/tooltip-standard`, base `main @ b3a7564d`). READ, not measured — no launch.

- [2026-08-30] [low] **FIXED on `wt/tooltip-standard`.** **Every production tooltip in the mod draws a stray, empty power row.** *(Corrected on re-reading, before the fix: the icon claim holds; the **height** claim in the first version of this entry did NOT. `leftHeight = 36 + descSize.Y` (`ProductionTooltipLogic.cs:156`) only loses to `rightHeight = 67` when a description renders shorter than ~31px, i.e. under about three lines — and every WW3MOD description plus its auto-blocks is far longer. The tooltip was never actually taller; only the stray sprite was real.)* `PowerManager:` is commented out at `mods/ww3mod/rules/player.yaml:163`, so `pm` is null (`ProductionTooltipLogic.cs:29`) and the `if (pm != null)` block at `:118-127` never runs. That block is the *only* thing that assigns `powerLabel.Visible` / `powerIcon.Visible`. `Widget.Visible` defaults to `true` (`engine/OpenRA.Game/Widgets/Widget.cs:222`) and `Background@PRODUCTION_TOOLTIP` sets no `Visible:` on either widget — unlike `Label@HOTKEY`, which explicitly sets `Visible: false` (`engine/mods/common/chrome/tooltips.yaml:265`). The `production-tooltip-power` sprite therefore renders next to a permanently blank label, and `rightHeight` (`:159`) takes the `powerIcon.Bounds.Bottom` branch (62px) rather than `timeIcon`'s (42px). **Fixed** by giving `ProductionTooltipLogic` an `else` branch that hides both widgets when `pm == null` — the logic only ever handled the `pm != null` case, and the bug is generic to any mod without a `PowerManager`, so it belongs there rather than in a per-mod chrome override. **Still unobserved:** no launch was run; confirm by hovering any sidebar icon. (found while working on: tooltip standardisation audit)

- [2026-08-30] [low] **Five actors state an armour class they do not have.** Description bullet vs resolved `Armor.Type`: `heli` (`aircraft-america.yaml`), `hind`, `mi28` (`aircraft-russia.yaml`) all say `- Armor: Medium` but are **`Heavy`**; `lccv` (`vehicles.yaml:636`) and `mnly` (`vehicles.yaml`) say `- Armor: None` but are **`Light`**. All four of those classes appear in real warhead `Versus:` tables, so these understate real protection and are balance-visible. **Not** in this list: the 28 infantry saying `- No armor` against `Armor.Type: Kevlar`, and `truk` saying `None` against `Unarmored` — both of those types are absent from every `Versus` table, so the prose is mechanically correct (see `WORKSPACE/DISCOVERIES.md` 2026-08-30). (found while working on: tooltip standardisation audit)

- [2026-08-30] [low] **Raw YAML weapon keys leak into the production tooltip.** `FormatWeaponLabel` (`engine/OpenRA.Mods.Common/Traits/AmmoPool.cs:200-209`) builds the auto-generated weapon heading from the ruleset key, stripping only `^`, `-` and `_` — never `.`. Players see `5.56mm.DMR` on the rifleman (two lines under the prose's own `5.56mm rifle`), `TankRound.Abrams` on the abrams, and `HIMARSTargeter` on the HIMARS, which is an internal targeting weapon name. (found while working on: tooltip standardisation audit)

- [2026-08-30] [low] **Single-pool units get no ammo-cost total at all.** The grand-total line is gated on `pools.Length >= 2` (`ProductionTooltipLogic.cs:208`), so a rifleman (2 pools) shows `Total ammo cost: 55` while an abrams (1 pool) shows no total — its 240 appears only as the tail of `Ammo: 40 (8 batches × 5 rounds × 30 supply = 240)`. Two units, two different answers to "what does refilling this cost", one of them unstated. (found while working on: tooltip standardisation audit)

- [2026-08-30] [low] **The Stryker SHORAD fires the Bradley's chaingun, and the weapon it is supposed to fire does not exist.** `vehicles-america.yaml:919` reads `Weapon: 25mm.Bradley # 30mm.Stryker` — the intended weapon is commented out on the same line, and **no weapon named `30mm.Stryker` is defined anywhere under `mods/ww3mod/rules/weapons/`**, so uncommenting it would fail to resolve. The unit is described and named as a 30mm platform but resolves to `25mm.Bradley` (Damage 500, Penetration 60, `weapons-ballistics.yaml:605`). Either the alternative weapon was never written or it was lost; decide which, because the actor's identity currently disagrees with its armament. (found while working on: tooltip weapon-section mockups)

- [2026-08-30] [RETRACTED SAME DAY — NOT A BUG. Kept because the reasoning is the point.] ~~The AT specialist's guided ATGM penetrates less than the rifleman's disposable rocket.~~ I logged this as a question on the raw fields — `ATGM` is `Damage: 10000, Penetration: 100` (`weapons-missiles.yaml:25-27`) against `RPG` at `Damage: 6000, Penetration: 500` (`weapons-ballistics.yaml:516`) — and concluded the specialist under-performed against `abrams` `Thickness: 700`. **That is wrong, because penetration is not compared against `Thickness`; it is compared against `Thickness × ArmorDirectionPercent`.** `ATGM` sets `TopAttack: true` (`weapons-missiles.yaml:6`), and `ArmorDirectionPercent` returns `Distribution[3]` for a top-attack warhead (`DamageWarhead.cs:150-152`). The abrams' `Distribution: 100,40,15,10,10` (`vehicles-america.yaml:501`) makes that **10%**, so effective thickness is 70 and `Penetration: 100` clears it outright — **full 10000 damage**. Against `t90` (`Thickness: 280`, `Distribution: 100,60,40,15,15`) it is 42, also cleared.

  Measured outcome, with the 2026-08-30 re-pricing (ATGM 65/shot, RPG 30/shot):

  | weapon | target | eff. thickness | dmg/shot | shots to kill | supply/kill |
  |---|---|---:|---:|---:|---:|
  | ATGM (top attack) | abrams 28000 HP | 70 | 10000 | 3 | 195 |
  | ATGM (top attack) | t90 24000 HP | 42 | 10000 | 3 | 195 |
  | RPG (direct, frontal) | abrams | 700 | 4285 | 7 | 210 |
  | RPG (direct, frontal) | t90 | 280 | 6000 | 4 | 120 |

  **The design is coherent and `Penetration: 100` is correctly sized against roof armour, not frontal.** The specialist's 3-round magazine does 30000 against an abrams' 28000 HP — it kills exactly one MBT per load, which reads deliberate. The RPG is cheaper per kill against a T-90 and worse against an abrams, which is the tradeoff you would want. **No action.** (found while working on: tooltip weapon-section mockups)

---

## 2026-08-30: [low] `defaults.yaml` declares two templates TWICE, and MiniYaml merges them silently (found while: cursor honesty audit, branch `wt/cursor-honesty`, `main @ 5a985337`)

`^AutoTargetAir:` appears at `mods/ww3mod/rules/defaults.yaml:554` **and** `:706`;
`^AutoTargetGroundAntiInf:` at `:606` **and** `:642`. Adjacent top-level entries merge without a
lint error (CLAUDE.md hard rule: "blank lines between top-level entries are significant"), so the
later block's fields silently win on any key both define.

**Not investigated** — found incidentally while enumerating `AutoTarget` for the cursor audit, and
outside that branch's scope. Whoever picks it up should diff the two halves of each pair before
deleting either; the merge may be load-bearing by accident.

**Confirm by:** reading the four line ranges and comparing the field sets.

---

## 2026-08-30: [high] OPEN — a unit whose every armament is PAUSED accepts an attack order, walks over, aims, and never goes idle again, silencing auto-target / auto-follow / auto-rearm (found while: cursor honesty audit, branch `wt/cursor-honesty`, `main @ 5a985337`)

**Filed separately on purpose.** This surfaced as finding 5 of the cursor audit and was initially
grouped with the cursor decisions. It is **not a cursor decision** — it is a live behavioural bug
that wants fixing whichever way the cursor question goes, and folding it into a UI vote is how it
would have disappeared.

**Mechanism** (already documented at `DOCS/reference/conventions.md:346`; what is new here is the
census). `ChooseArmamentsForTarget` filters `IsTraitDisabled` but not `IsTraitPaused`, and
`AttackOrderTargeter` *deliberately* selects a paused armament (`AttackBase.cs:807`:
`FirstOrDefault(x => !x.IsTraitPaused) ?? armaments.First()`). The order is accepted.
`Armament.CanFire` then declines on pause (`Armament.cs:327`), `TickAttack` still reports
`Attacking`, so **the activity never completes** and the unit is never idle — every `INotifyIdle`
behaviour it owns is silenced for as long as the pause lasts.

**Census, and why this is worse than it reads.** `AttackBaseInfo.AbandonWhenArmamentsPaused`
(`AttackBase.cs:72`, default `false`) is the opt-in escape, and **exactly one actor in the mod sets
it** — `^MEDI` (`infantry.yaml:2308`) — against **64** armament-level `PauseOnCondition` gates in
non-husk rules. The widest live entrance is **every armed vehicle**: they all carry
`|| empdisable || heavy-damage-attained`, and `heavy-damage-attained` is granted at damage state
Heavy **and Critical** (`defaults.yaml:243-245`). So a tank at heavy damage — a normal, common,
late-fight state — accepts an attack order, drives into range, aims, fires nothing, and stops
auto-rearming and auto-targeting until it is given another order.

**Note the cursor question is genuinely separate, and the obvious display "fix" would be its own
lie.** For the ammo cases the order is truly refused (`AttackBase.cs:502`), so a blocked cursor is
honest. For a *paused* armament the order is **accepted** and the unit really does drive over — so
painting a blocked cursor would claim a refusal that does not happen. The honest display answer is
not the same predicate, which is why this is filed on its own.

**Fix shape:** the affected traits opt into `AbandonWhenArmamentsPaused` so the unit at least drops
to idle. **Do NOT widen the flag into "abandon when the unit cannot fire":** `Armament.CanFire` is
also false on `IsReloading`, `IsWaitingBurst` and `IsAiming`, all true on ordinary ticks of a healthy
weapon, so the general form would drop every attack order in the game between shots. Pause is the
only one of those that can *last*.

**SUPPRESSION-ADJACENT, NOT PRE-AUTHORISED:** two of the live gates are `suppressed >= 10` on `^AT`
(`infantry.yaml:1739`) and the engineer's repair armament (`:1956`). Those need specific sign-off.
The vehicle `heavy-damage-attained` cases do not touch suppression.

**Confirm by:** damage a tank to Heavy, order it to attack, and watch it aim without firing and
without ever rearming.

**2026-09-01 — STILL OPEN, and now CONFIRMED DISTINCT from `test-attackmove-dry-breaks-off`'s red,
this time by measurement rather than by reading.** (An intermediate note withdrew this claim as
unproven; the instrumented run has since established it.)

The run reported `cannotFight=true` for both dry men and showed their activity chains moving off
`Attack`/`AttackMoveActivity` onto `RotateToEdge`. **So in the wholly-dry case the ammo guards
demonstrably DO release the unit.** This entry's cases are armaments paused for a reason
`AmmoPool.CannotFight` cannot see — `heavy-damage-attained`, `empdisable`, `suppressed >= 10` — where
`CannotFight` is *false* and no guard fires at all. `AttackBase.cs:452` states the same thing from
the other side: `CannotFight` "is NOT sufficient on its own".

**Consequences, unchanged from the original claim but now evidenced:** those scenarios are NOT the
regression pin this entry lacks, and fixing this will not turn them green — they are already green on
their own account. A pin for *this* entry must pause an armament while leaving the magazine loaded
(damage a tank to Heavy), precisely so `CannotFight` stays false and this path is isolated from the
ammo one. **`TestHarness.HoldsAttackActivity` is the assertion that pin should use** — it reads the
activity queue directly and is exactly the observable this entry's symptom is about ("aims without
firing and never goes idle"), without inheriting the `IsIdle` proxy problem that rotted the dry pair.

---

## 2026-08-30 — `test-poor-depot-still-worth-the-trip` asserts behaviour the codebase deliberately reversed 72 minutes later

**Found while** fixing the affordability disagreement in `AutoSeekSupplies`' break-off arm. Not
touched by that change (it exercises the `AmmoPool.AutoRearmIfDry` Auto arm, on a tank), so it is
filed rather than fixed — **and it was not run**, so "is red" below is derived from reading, not
measured.

**Three commits, all 2026-08-27, in this order:**

| Order | Commit | What it did |
|---|---|---|
| 1 (`ct` 1787844111) | `291ba846` | Wrote the "DO NOT swap this for `ChooseAffordableResupplier`" comment in `AutoSeekSupplies`, premised on a docking rearm being FREE. |
| 2 (+27 min) | `1bfd5e2c` | Added `test-poor-depot-still-worth-the-trip` on the same premise. Its own commit subject says **"UNVERIFIED, arms not yet run"**. |
| 3 (+44 min) | `f8b424f6` | **Metered the dock path**, falsifying the premise of both. |

`Rearmable.RearmTick:106` now skips any pool the provider cannot pay for, exactly as
`AmmoPool.TryServeBatch:446` does on the push side. So a Logistics Centre rearm is charged, and the
scenario's central claim — "a rearm there is FREE ... so the trip is worth it" — is no longer true.

**Two consequences.**

1. **The scenario should be red.** Its abrams has one pool at `SupplyValue: 30` against a depot set
   to 10. `ChooseAffordableResupplier` returns null, `DecideAutoDisposition` falls to `HoldAndFlag`,
   and the tank never moves — so `ammo > 0` never becomes true. Even if it were dispatched,
   `RearmTick` would now refuse the pool on arrival.
2. **Its stated post-fix mechanism does not exist.** The failure message promises "the
   `DockedCondition` early-out in `HostCanAffordSomethingWeNeed`". There is no such early-out —
   `DockedCondition` appears nowhere in that method, and commit 3 went the opposite way.

`test-vehicle-rearms-at-empty-depot` WAS re-pointed for the metering and says so in its header. This
one was missed.

**The question to settle before touching it:** metering the dock path and "a poor depot is still
worth the trip" are contradictory intents, and commit 3 is the later and better-evidenced one. Most
likely the scenario should be re-pointed to assert the tank does NOT set off (mirroring its sibling)
rather than that it does. That is a ruling, not a cleanup — do not just flip the predicate.

**Confirm by:** `./tools/autotest/run-test.sh test-poor-depot-still-worth-the-trip`.

---

## 2026-09-01 — The Resupply button never un-highlights

**Found while** fixing the queued-Evacuate bug (`wt/evac-queue`). Filed, not fixed. **Not run**:
derived from reading, not measured in a game.

`CommandBarLogic.Tick()` counts down `deployHighlighted`, `scatterHighlighted`, `stopHighlighted`,
`patrolHighlighted`, `autoEnterHighlighted` and `evacuateHighlighted` — but **not**
`resupplyHighlighted`, which `:185`/`:193` set to 2. `IsHighlighted` is `resupplyHighlighted > 0`,
so once the Resupply button is pressed **it stays lit for the rest of the match**. Cosmetic, and
almost certainly an omission rather than a decision; the other six are all there.

Left alone deliberately when its neighbour was fixed: the Shift+R dispatch bug below was the *same
mechanism* as the Evacuate bug and rode that fix's proof, but this is a genuinely different
mechanism and deserves its own RED rather than being swept in on someone else's evidence.

**Confirm by:** press R once with an `AmmoPool`-carrying unit selected and watch the button stay
highlighted for the rest of the match.

**Sibling, now FIXED (commit on `wt/evac-queue`, 2026-09-01):** `Shift+R` used to be dead because
`resupplyButton` was absent from the `noShiftButtons` array — identical mechanism to the Evacuate
bug (`HotkeyReference.IsActivatedBy` compares modifiers for equality, so a Shift-bearing event never
matches the unshifted `Resupply: R` binding). It is in the array now and
`test-evac-queued-after-waypoints` asserts the keypress is consumed. **What remains unproven is the
BEHAVIOUR of a queued Resupply** — no scenario anywhere exercises it, and `resupplyButton.OnClick`
has been building queued orders since it was written without the hotkey ever being able to deliver
one. `autoEnterButton` and `patrolButton` are still absent from the array; `stopButton` is absent
correctly, since a Stop must never queue.
## 2026-09-01: [med] `test-attackmove-dry-breaks-off` is RED on `main`, and it is very likely the standing paused-armament bug rather than a new one (found while: the queued-attack-move stale-destination fix, branch `wt/stale-order-state`, `main @ 143a4941`)

**I did not chase this.** It surfaced in the regression pass for an unrelated fix, I established only
that it is not mine, and I stopped there. What follows is the attribution evidence and nothing more.

**Symptom.** `./tools/autotest/run-test.sh test-attackmove-dry-breaks-off` fails with

    A dry man never went idle: he kept an attack order he could not carry out
    (Hunter = attack-move / AttackMoveActivity guard, Shooter = direct attack / Attack.Tick guard)

**NOT caused by the branch it was found on, established by a two-arm control rather than by
argument.** The branch's only behavioural engine change is that `AttackMove.ResolveOrder` resolves
`NearestMoveableCell` when the move starts instead of when the order is issued. Reverting *only*
that — restoring the eager local, leaving `MoveOrderTerms` and the `Mobile` call sites in place —
and rerunning gave a **byte-identical** failure note:

| Arm | Seed | Run dir | Verdict |
|---|---|---|---|
| Deferred (the fix) | `859777250` | `260901_030549_p68940_test-attackmove-dry-breaks-off` | fail |
| Eager (pre-fix control) | `2084951790` | `260901_030752_p69364_test-attackmove-dry-breaks-off` | fail |

The control arm is behaviourally `main`: the two remaining engine edits are substitutions of equal
values (`WDist.FromCells(8)` → `WDist.FromCells(MoveOrderTerms.NearEnoughCells)`, and the clamp
helper is the same expression it replaced). So this fails on `main` too.

**Most likely already filed, one entry up.** The failure text — an attack order kept, the unit never
reaching idle — is the symptom of *"[high] OPEN — a unit whose every armament is PAUSED accepts an
attack order, walks over, aims, and never goes idle again"* (2026-08-30). An empty ammo pool is a
PAUSED armament (`!ammo-primary`), which is exactly that entry's mechanism, and this scenario's whole
subject is a dry man. **I have not verified the linkage** — I did not read the scenario's Lua, did not
check which of its two units (Hunter / Shooter) fails the predicate, and did not bisect for when it
went red. Treat the connection as a strong lead, not a finding.

**Worth knowing before anyone starts:** the scenario was introduced with `68c2527a` /
`27763a9b` ("an attack order given to a unit with nothing to fire is now dropped", then scoped to
unqueued orders), so it is meant to be green and presumably once was. If it turns out to be the
2026-08-30 bug, that entry's fix shape — the affected traits opting into
`AbandonWhenArmamentsPaused` — should turn this green as a side effect, and this scenario is then the
regression pin that entry currently lacks.

**Confirm by:** `./tools/autotest/run-test.sh test-attackmove-dry-breaks-off` on a clean `main`.

### 2026-09-01 — ✅ RESOLVED AND MEASURED. The analysis below is CORRECT; a mid-course retraction of it was itself wrong and is withdrawn.

**Instrumented run, `260901_073202_p95073`, execution markers present (so not a load abort):**

```
Hunter  cell=(2,13) startX=9 ammo=0 cannotFight=true idle=false idleTicks=0
  acts=[AttackMoveActivity>SmartMoveActivity>Move>MoveFirstHalf
      ~ RotateToEdge>SmartMoveActivity>Move>Turn ~ RotateToEdge>…>MoveSecondHalf]
Shooter cell=(6,16) startX=9 ammo=0 cannotFight=true idle=false idleTicks=0
  acts=[Attack>SmartMoveActivity>MoveWithinRange>Move>… ~ RotateToEdge>SmartMoveActivity>Move>…]
```

Every clause of the analysis below is confirmed: `cannotFight=true` (the guards' predicate held),
the chain moved off the attack activity to `RotateToEdge` (the guards fired and the evacuate
disposition took over), the men walked west from x=9, and `idleTicks=0` (the idle edge exists but is
consumed inside the tick). **The engine is behaving correctly and both scenarios were red for a
wrong assertion.**

**Why the first fix attempt failed, and it was NOT evidence against the mechanism.** Pinning
`InitialResupplyBehavior: Hold` in the scenario's `rules.yaml` under `^Combatant` was run and did not
work — which prompted a retraction of this whole analysis. That retraction was an over-correction: the
pin never took effect. `^AR` lists `Inherits@AutoTarget: ^AutoTargetLMG` **after**
`Inherits@Type: ^CamoSoldier`, and `MiniYaml.ResolveInherits` merges parents in source order with
later winning field-by-field — so `^AutoTarget`'s own `InitialResupplyBehavior: Auto`
(`defaults.yaml:390`) overwrote the `Hold` set on `^Combatant`. The sibling `ScanRadius: 30` in the
very same block *does* apply, because `^AutoTarget` never sets it. **A block that is half in force
and half silently overridden is why this was misread as a refutation.** Written up as its own trap
in `WORKSPACE/DISCOVERIES.md` 2026-09-01, because it generalises well past this scenario.

**THE FIX AS SHIPPED — the assertion, not the configuration.** Both scenarios now assert
`not TestHarness.HoldsAttackActivity(unit)` instead of `unit.IsIdle`. That is the observable the
scenarios are named for, and it is strictly *stronger* than the old check: an idle unit holds no
attack activity either, so nothing the old assertion accepted is now rejected. The `Hold` pin stays
reverted — the scenarios run on shipped defaults, and the disposition layer is free to do whatever it
does, which is exactly the point.

**Process lesson, and the expensive one.** The first conclusion was right and was abandoned after a
single failed prediction, without first establishing *why* the fix failed. One failed fix falsifies
**the fix**, not necessarily the diagnosis behind it — and here the fix failed for an unrelated YAML
inheritance reason. The correct move on that run was to instrument, which is what eventually settled
it; retracting first cost a cycle and briefly put a wrong "the engine is broken" reading into this
file. **Before retracting a mechanism, check that the intervention you built on it actually reached
the code path it was aimed at.**

---

### 2026-09-01 — the analysis itself (CONFIRMED by the run above) (branch `wt/stuck-units`, `main @ 1fe106ff`)

**It is NOT the paused-armament `[high]`.** That entry and this failure share a symptom sentence and
nothing else, and the two are separated by a predicate the codebase already documents as distinct:

- The `[high]` is about an armament paused for a reason **other than ammo** — `heavy-damage-attained`,
  `empdisable`, `suppressed >= 10`. `AmmoPool.CannotFight` cannot see any of those, and
  `AttackBase.cs:452` says so in terms: *"AmmoPool.CannotFight is NOT sufficient on its own: it needs
  EVERY pool empty."* A heavy-damaged tank has ammo, so no guard fires and it really does hold the
  order forever. **That entry stays OPEN and unfixed, and this scenario is NOT its regression pin** —
  the speculation above that fixing it "should turn this green as a side effect" is withdrawn.
- This scenario's men are **wholly dry**, which is exactly what `CannotFight` covers. Both guards fire.

**What actually happens, traced end to end.** `ar` resolves to exactly ONE `AmmoPool`
(`primary-ammo`, declared in `^AR` at `infantry.yaml:1342`; no ancestor of `^AR` declares another —
checked across `^CamoSoldier`/`^Soldier`/`^Infantry`/`^AutoTargetLMG`). So after the Lua drain
`AllPoolsEmpty` is true, `AmmoPool.CannotFight` is true, and:

1. `Attack.Tick` returns true at its `CannotFight` guard (`Activities/Attack.cs:117`) — Shooter's
   order ends. `AttackMoveActivity.Tick` does the same at `:128` — Hunter's march ends. **The guards
   work.**
2. The actor is momentarily `CurrentActivity == null`, and `Actor.Tick` raises `INotifyBecomingIdle`
   on that edge (`Actor.cs:321`).
3. `AmmoPool.OnBecomingIdle` (`:876-878`) calls `AutoRearmIfDry`, which reads `ResupplyBehavior`.
   It is `Auto` (`defaults.yaml:390`; the scenario's `AutoTarget` override does not touch it, and
   MiniYaml merges per field).
4. With no rearm host on the map: `whollyDry` true, `namesRearmActors` true (`truk, supplycache,
   logisticscenter`), `leash` 30, `suppliedHostWithinLeash`/`anyHostWithinLeash`/`anyHostCanReachUs`
   all false → `SupplyHuntMath.DecideAutoDisposition` returns **`Evacuate`** (`SupplyHuntMath.cs:296`)
   → `EvacuateForRefund` queues `RotateToEdge` (`AmmoPool.cs:829`).
5. `Actor.Tick` then **runs that newly queued activity in the same tick, deliberately** — *"to avoid
   an 'empty' null tick where the actor will (visibly, if moving) do nothing"* (`Actor.cs:322-325`).

So `Actor.IsIdle` is **never observable as true from Lua**, and the men walk west toward the map edge
to sell themselves. `AttackMoveActivity.cs:126-127` names this handoff outright — *"hands the unit to
AmmoPool.INotifyBecomingIdle -> AutoRearmIfDry, which is what picks the right disposition per resupply
stance (Auto: rearm, Hold: stay put and flag, **Evacuate: rotate out**)"*.

**Why it passed once and went red without anyone touching it.** The scenario was written 2026-08-10
(`68c2527a`), when the no-host `Auto` path was *"raise NeedsResupply and stand still"* — which queues
nothing, so `IsIdle` really did become true. The **2026-08-27 user ruling** (*"'Auto' should mean that
they evacuate if no rearm actor exists"*) replaced that branch with an evacuation. The scenario's
proxy was invalidated seventeen days after it was authored, by a deliberate behaviour change
elsewhere. **Same failure shape as the 2026-08-16 entry on the `@experimental` out-of-ammo sweep** —
*"The guard under test actually WORKS, which the verdict hides. What fails is the test's PROXY."*
That makes two, so it is a class, not an incident.

**Arithmetic consistency check, which is why the note reads as a timeout rather than "died first".**
Infantry `Speed: 25` (confirmed via `--dump-balance-json`) ≈ 41 ticks/cell. Hunter at (8,16) must
cover ~7 cells to the edge cell near (1,16), then a 2-cell `GroundOffMapCells` drive-off: ~370 ticks
after the evacuation starts at ~tick 30. The deadline is 15 × `TicksPerSecond` 25 = **375 ticks**. So
the men are still walking, alive and non-idle, when the assert expires — which is exactly what
`result.json` records (`status: fail`, the timeout note, and no `"fail: Hunter died first"`).

**REJECTED FIX — tried, run, failed, reverted. Recorded so nobody retries it.** `rules.yaml` pinned
`InitialResupplyBehavior: Hold` / `InitialResupplyBehaviorAI: Hold` under `^Combatant`, on the correct
reasoning that `Hold` is the one disposition that queues nothing (`AmmoPool.cs`,
`case ResupplyBehavior.Hold`: flag `NeedsResupply` and return). **The idea was sound and the pin was
inert** — later `Inherits@` wins field-by-field, and `^AR`'s `Inherits@AutoTarget: ^AutoTargetLMG`
re-imports `InitialResupplyBehavior: Auto` from `^AutoTarget`. To pin it for real it would have to go
on the ACTOR (`ar:` / `abrams:`), not on a template the actor re-inherits over. **It is not needed:
the shipped fix is the assertion, not the configuration.**

**SHIPPED FIX — assert the activity, not the idleness.** Both scenarios now assert
`not TestHarness.HoldsAttackActivity(unit)`. Strictly stronger than the old `IsIdle` check (an idle
unit holds no attack activity either), and it survives whatever the resupply disposition layer
decides to do next — which is the whole reason the old assertion rotted.

**RED / GREEN.** GREEN is `status: pass`. The RED that proves non-vacuity is *sabotage the guard, not
the ammo*: delete the `if (AmmoPool.CannotFight(self)) return true;` at `Activities/Attack.cs:117`
**and** the `|| AmmoPool.CannotFight(self)` at `Move/AttackMoveActivity.cs:128`, and the run must fail
with *"A dry man never dropped his attack order"* plus an `acts=[…]` list still showing an
`Attack`/`AttackMoveActivity` component. Removing only one of the two is a weaker RED: Hunter's parent
cancels his attack child before that child's guard runs, so the attack-move arm alone says nothing
about `Activities/Attack.cs`. **The pre-fix instrumented run is itself most of that RED already** —
it recorded the attack chains at the drain and the transition away from them, so the guard is
observed working rather than assumed.

**PREDICTION CONFIRMED: `test-attackfollow-dry-breaks-off` was red on `main`** (*"Dry Abrams never
went idle"*), exactly as predicted from its single pool and absent `logisticscenter`. It has been
re-pointed to the same activity assertion but **has NOT been re-run**, so its green is predicted, not
measured.

**Census of the blast radius.** Eight scenarios both assert `IsIdle` and manipulate ammo:
`test-attackfollow-dry-breaks-off`, `test-attackmove-dry-breaks-off`, `test-dry-inrange-idle-oscillation`,
`test-dry-soldier-retry-after-refill`, `test-poor-depot-still-worth-the-trip`,
`test-tactical-arty-detour-geometry`, `test-vehicle-rearms-at-empty-depot`, `test-who-pays-for-a-rearm`.
The two `*-breaks-off` are handled here. The other six were **not** examined; any of them that drives a
unit wholly dry with no affordable host inside 30 cells is exposed to the same proxy failure.

---

## 2026-09-02: [med] Fixed-wing aircraft are moved TWICE per tick and steered by two controllers that disagree — the `Fly.cs:52` PITFALL's premise stopped being true (found while: the strafe zero-shot investigation, branch `wt/strafe-zero`, `main @ 81140244`)

**Static finding, not run. NOT fixed here** — it changes how every fixed-wing in the mod flies, so it
needs its own measured commit rather than riding along on a diagnosis.

`Fly.cs:52` states the invariant as a PITFALL: *"step-based movement. CanSlide aircraft move via
CurrentVelocity in Aircraft.Tick — calling FlyTick on them double-moves unless CurrentVelocity is
zeroed first."* Its premise is that only `CanSlide` aircraft ever carry a non-zero `CurrentVelocity`.
That is no longer so. `Fly.Tick`'s **non-CanSlide** branch sets `aircraft.RequestedAcceleration` at
`Fly.cs:285-286`, and nothing in `Aircraft.Tick` is gated on `CanSlide`:

- `Aircraft.cs:499-510` integrates `RequestedAcceleration` into `CurrentVelocity` and clamps it to
  `Info.Speed`;
- `:524-527` calls `SetPosition(self, CenterPosition + CurrentVelocity)` — **movement #1**;
- `:530-531` steers `Facing` toward `CurrentVelocity.Yaw` — **controller #1**;
- then `Fly.Tick` reaches `FlyTick`, which moves the actor again by `FlyStep(aircraft.Facing)`
  (`Fly.cs:56`, `Aircraft.cs:800-809`) — **movement #2** — and steers `Facing` toward its own
  `desiredFacing` (`Fly.cs:62`) — **controller #2**.

So a fixed-wing's realised track is the sum of a velocity vector and a facing-derived step that are
not required to agree, under two turn controllers driving the same field in the same tick.
**Population:** every non-`CanSlide` aircraft — `mods/ww3mod/rules/ingame/aircraft.yaml:340` sets
`CanSlide: False` on the fixed-wing base, `:169` sets True on the helicopter base — so A10, F16, MIG,
FROG and both `.Airstrike` variants, on every path that runs `Fly` (attack runs, move orders,
`ReturnToBase`), for humans and both bot profiles.

**Why it is filed as a suspicion and not as the strafe cause:** it reaches the A-10 that *does* fire
(`test-aircraft-breakoff-midrun` lane 1, run `260901_085215_p7281`), so it cannot on its own be what
zeroes the two `AttackType: Strafe` airframes. It matters anyway, and it matters most to that
investigation: `StrafeAttackRun`'s first leg exits only through `Fly.cs:272-276`
(`stopDelta.HorizontalLengthSquared < 512 * 512` against `CalculateStopPosition()`), that predicate
reads `CurrentVelocity`, and a fixed-wing has a `CurrentVelocity` at all only because of this defect.
Reasoning in `WORKSPACE/DISCOVERIES.md` (2026-09-02).

**Do not "fix" it by zeroing `CurrentVelocity` in the fixed-wing branch without measuring.** The
velocity path is also what feeds `CalculateStopPosition`, and `8bffb2ff` ("Aircraft stopping within
512 of destination") put a live arrival test on top of it.

---

## 2026-09-01: [low] `A10` declares `ReloadAmmoPool@1` TWICE, so its primary magazine has no reloader (found while: the fixed-wing rearm investigation, branch `wt/stuck-units`, `main @ 1fe106ff`)

**Static finding, not run, and INERT TODAY** — filed because it is inert only by accident.

Inside the single `A10:` actor, `ReloadAmmoPool@1:` appears at `aircraft-america.yaml:486` (pointed at
`primary-ammo`) and again at `:516` (pointed at `secondary-ammo`). Per
[`DOCS/reference/conventions.md`](../../DOCS/reference/conventions.md) §"A duplicate trait key inside
ONE actor: LAST value wins, at the FIRST key's position", the second wins: **A10 ends up with one
reloader, for `secondary-ammo`, and `primary-ammo` has none.**

It cannot bite at present because both reloaders are gated `RequiresCondition: unit.docked &&
!airborne` and an A10 can never dock — see the entry below on fixed-wing rearm. It becomes live the
moment either that changes or an `afld` is placed. The neighbouring `F16`/`FROG`/`MIG` blocks use
distinct `@` suffixes and are unaffected.

**Fix is one character** — rename the second to `ReloadAmmoPool@2:`. Deliberately NOT done here: this
branch is a read-only assessment of the rearm question and the change is unverifiable without a run
that only matters once the aircraft are reachable at all.

---

## 2026-09-01: [closed — unreachable, documented] `A10`/`FROG`/`MIG` circle forever when dry, and cannot be fielded in normal play (found while: the fixed-wing rearm investigation, branch `wt/stuck-units`, `main @ 1fe106ff`)

**Verdict: not worth fixing. Document and close.** Recorded in full because the finding is "there is
no reachable defect here", and that is the expensive thing to re-derive.

**What a dry `A10`/`FROG`/`MIG` actually does — it circles in place forever, combat-inert, and is
never `IsIdle`.** Traced statically:

1. Aircraft never enter the `AmmoPool` self-dispatch machinery at all: `AutoRearmIfDry` returns
   immediately for anything with `AircraftInfo` (`AmmoPool.cs:637`), and `AmmoPool.CannotFight`
   carves aircraft out by construction (`:619-621`).
2. At zero rounds the `AmmoCondition: ammo-primary` lapses, so every armament is
   `PauseOnCondition: !ammo-primary` paused.
3. `FlyAttack.Tick` sees `rearmable != null && Armaments.All(x => x.IsTraitPaused)`
   (`Activities/Air/FlyAttack.cs:85`) and queues `ReturnToBase`.
4. `ReturnToBase.ChooseResupplier` (`:39-51`) requires `a.Owner == self.Owner` **and**
   `RearmActors.Contains(a.Info.Name)`. With no `afld` owned by the player it returns null, and RTB
   takes its give-up branch: `QueueChild(new FlyIdle(self, NumberOfTicksToVerifyAvailableAirport));
   return true;` (`:127-128`). `NumberOfTicksToVerifyAvailableAirport` is 150 ≈ 9 s. **One-time
   give-up, not a per-tick re-query** — there is no retry loop and no CPU burn.
5. RTB completes → `Aircraft.OnBecomingIdle` → no `IdleBehavior` is set anywhere in mod YAML, so the
   default branch queues `FlyIdle(self)` (`Air/Aircraft.cs:949`) with `ticks = -1`.
6. `FlyIdle.Tick` returns true only on `remainingTicks == 0` or `NextActivity != null &&
   remainingTicks < 0` (`Air/FlyIdle.cs:40`). With `-1` and nothing queued behind it, **neither ever
   holds.** `CanHover` is false on `^Aircraft`, so `isIdleTurner` is true and the plane circles at
   cruise speed indefinitely.
7. It cannot recover: `ReloadAmmoPool` on all three is gated `RequiresCondition: unit.docked &&
   !airborne`, and `EvacuateWhenUnrearmable` is declared once, inside `^Helicopter`
   (`aircraft.yaml:195`; `^Helicopter` opens at `:160`, `^Aircraft` at `:119`). Fixed-wing inherit
   `^Aircraft` and never get it.

**Why it is unreachable, with FOUR independent locks.** The premise handed to this investigation named
only the prerequisite; the stronger lock is the queue.

1. **No production queue at all.** `A10` (`aircraft-america.yaml:445`), `F16` (`:569`),
   `FROG` (`aircraft-russia.yaml:464`) and `MIG` (`:586`) declare a `Buildable:` with **only**
   `Prerequisites: ~disabled` and **no `Queue:` line** — unlike every buildable aircraft above them,
   which all carry `Queue: Aircraft`. `BuildableInfo.Queue` defaults to an empty set
   (`Traits/Buildable.cs:27`) and both `ProductionQueue.AllBuildables` (`:241`) and `ResolveOrder`
   (`:446`) gate on `Queue.Contains(category)`. They are in no queue and no tech tree.
2. **`~disabled` really is unsatisfiable here.** `TechTree.HasPrerequisites` strips the tilde and
   requires the bare token to be owned (`Player/TechTree.cs:143-144`); nothing in `mods/` provides
   `disabled` via `ProvidesPrerequisite` (the single grep hit is an unrelated `PauseOnCondition`).
3. **The AI wants them and still cannot have them.** `ai.yaml:1796/1914` list `mig`/`frog`/`a10`/`f16`
   under `UnitsToBuild`, routed through `queue.BuildableItems()` which is empty for these. The
   `@experimental` `AdaptiveProductionBotModule` scores `AirStrikeUnits: heli, a10` / `mi28, frog`
   *without* a buildability check (`:542-553` only tests `Rules.Actors.ContainsKey`) and will request
   an `a10` — but the request dead-ends in `UnitBuilderBotModule.BuildUnit` (`:1628-1652`), which
   iterates the empty `buildableInfo.Queue` and returns false without issuing an order. **This is the
   closest thing to a live path and it is closed.**
4. **Nothing else spawns them.** Every `AirstrikePower`/`ParatroopersPower` in `player.yaml` is
   commented out (`:106-155`), as is `PowersLobbyOptions`; no shipped map places any of the four; no
   mod Lua references them; no crate action grants units.

**CORRECTION to the premise, and it matters for anyone re-checking: "no map in the mod places an
`afld`" is FALSE.** `tools/autotest/scenarios/test-capture-rules/map.yaml:78` places a **Neutral**
`afld` and `:87` an enemy (Russia) one. Neither changes the verdict — `ChooseResupplier` requires
`Owner == self.Owner`, and that map fields no aircraft — but the statement should not be repeated as
written. (Method note: a `grep ": afld$"` over `--include=map.yaml` returned nothing and was a false
negative; the case-insensitive substring search found both. Do not trust the anchored form here.)

**The `.Airstrike` variants are SEPARATE actors and do NOT share the defect.** Only two exist —
`A10.Airstrike` (`aircraft-america.yaml:676`) and `FROG.Airstrike` (`aircraft-russia.yaml:702`);
**there is no `MIG.Airstrike`.** Both carry `-Rearmable:` and `-ReloadAmmoPool@1:`, so with no
`RearmableInfo` the `rearmable != null` test at `FlyAttack.cs:85` is false and they never queue
`ReturnToBase` at all. Both also carry `-Buildable:`, and their only producer was the commented-out
`AirstrikePower`. **Not traced, and the one thing to check first if those powers are ever
re-enabled:** nothing was found that removes them after a strafe run — no `RemoveSelf`, no
`LeaveMap`, no lifetime trait — and they inherit the same default `IdleBehavior`, so on a static read
they would loiter forever too, for a different reason.

**Why "not worth fixing" rather than "add `EvacuateWhenUnrearmable` to `^Aircraft`":** the fix would be
one line and would be correct, but it is unreachable code today and cannot be verified by any run —
there is no way to field an `A10` to observe it. Writing an unverifiable behaviour change into a
shared template to fix a defect nobody can trigger is worse than recording the defect. **What makes it
worth re-reading later: the whole thing is one YAML line (`Queue: Aircraft`) away from going live, and
the AI already has `UnitsToBuild` and `AirStrikeUnits` entries sitting there waiting for it.** If those
four aircraft are ever re-enabled, fix the loiter and the `A10` duplicate-key bug above in the same
change.

---

## 2026-09-01 — `demo-experimental-capture-coordinator` never runs its own Lua

**Found by** the first clean-tree run of `make lua-gate` (`tools/lua-gate/`). Filed, not fixed.
**Not run**: static finding, no launch spent.

The scenario directory carries `demo-experimental-capture-coordinator.lua`, whose entire body is a
`TestHarness.FocusBetween(NeutralBio, NeutralFcom, NeutralOilb1, NeutralOilb2)` that frames the
camera across the contested neutral capturables. But neither its `rules.yaml` nor its `map.yaml`
declares `LuaScript:` / `Scripts:` anywhere, so the engine never loads the file. `WorldLoaded` has
never been called and the demo has always opened on an unframed camera.

It is the only one of 214 scenarios with this shape — every other scenario declares
`Scripts: test-helpers.lua, <scenario>.lua`. Two things are wrong together, which is why it went
unnoticed: without the `Scripts:` line the scenario's own script is absent *and* `test-helpers.lua`
is absent, so `TestHarness` would be nil even if the file did load.

**Confirm by:** `grep -rn Scripts: tools/autotest/scenarios/demo-experimental-capture-coordinator/`
returns nothing.

**Fix is** adding the standard block to `rules.yaml` under `World:`:

```
	LuaScript:
		Scripts: test-helpers.lua, demo-experimental-capture-coordinator.lua
```

Not applied here because it changes what a demo shows on screen, which wants a look rather than a
silent edit from a tooling branch.

---

## 2026-09-01 — `A10.Airstrike` cannot fire a single shot: its weapons are not valid against `Ground`

**Found by** `test-aircraft-breakoff-midrun` (lane 3, run on `main` the morning of 2026-09-01), then
traced statically. Filed, not fixed. The lane has since been swapped to `frog.airstrike`, which is the
only strafe airframe in the mod that *can* fire, so the scenario can go back to asking its own question.

**Measured**: `SETUP INVALID: A10STRIKE never opened fire within 400 ticks; A10STRIKE target ended at
hp100%`. The airframe was alive, had ammo, was at its own cruise altitude, and had a valid t90 14 cells
away. It acquires the target and flies the attack run. It never shoots.

**Mechanism**, and it is entirely in the interaction between one activity and one weapon field:

1. `AttackType: Strafe` makes `FlyAttack.Tick` queue a `StrafeAttackRun`
   (`engine/OpenRA.Mods.Common/Activities/Air/FlyAttack.cs:187-188`).
2. `StrafeAttackRun.Tick` re-sets the trait's requested target **every tick** to
   `Target.FromTargetPositions(target)` (`FlyAttack.cs:323-325`) — a `TargetType.Terrain` target
   (`engine/OpenRA.Game/Traits/Target.cs:33-35, 86`), force-attack `true`.
3. A terrain target is unconditionally `IsValidFor` (`Target.cs:123-125`), so `AttackFollow.Tick`
   takes the requested-target branch (`Traits/Attack/AttackFollow.cs:188`) and the opportunity-fire
   fallback at `:216` is **unreachable** for the whole run.
4. Firing then needs an armament valid against terrain, and `WeaponInfo.IsValidAgainst` resolves a
   terrain target to the **cell's** `TargetTypes` (`engine/OpenRA.Game/GameRules/WeaponInfo.cs:235-249`)
   — `Ground` on every tile of every shipped tileset.
5. Neither A10 weapon lists `Ground`. `30mm.A10` inherits `^30mm`'s
   `ValidTargets: Infantry, Vehicle, Defense` (`rules/weapons/weapons-ballistics.yaml:582`) and
   `Hellfire` is `ValidTargets: Vehicle, Air, Defense` (`rules/weapons/weapons-missiles.yaml:243`).
   `ChooseArmamentsForTarget` therefore returns nothing (`AttackBase.cs:458-462`), `CanAimAtTarget`
   is false, `DoAttack` is never called.

Both weapons hit the t90 perfectly well as an **actor** (`^Vehicle` is `TargetTypes: Ground, Vehicle`,
`rules/ingame/vehicles.yaml:46-48`), which is exactly why this is invisible from the YAML: the unit
looks armed, acquires normally, and flies a textbook attack run in silence.

**The rule this implies**, and it is worth stating because nothing enforces it: *an `AttackType: Strafe`
airframe's armaments must include `Ground` in `ValidTargets`, or the airframe can never fire.* Upstream
OpenRA's strafing aircraft satisfy it by convention. In WW3MOD `FROG.Airstrike` satisfies it
(`RocketPods`, `ValidTargets: Ground`, `weapons-ballistics.yaml:912`); `A10.Airstrike` does not. No lint
rule catches this — `--check-yaml` has no notion of the pairing.

**Why it has never been seen in a match**: `A10.Airstrike` is spawned only by `AirstrikePower`, and the
airstrike powers are commented out for v1 (`rules/player.yaml:106-144`, `rules/world.yaml:552-557`). It
is latent, not live. Whoever re-enables airstrikes post-v1 inherits an A-10 that flies over and does
nothing.

**Candidate fix** (not applied — it is a live weapon-coverage change and wants its own run): give
`30mm.A10` an explicit `ValidTargets: Ground, Infantry, Vehicle, Defense`. Note the A-10 also cannot be
overridden from map rules at all (the duplicate-key bug filed above), so this cannot be pinned by a
map-rules test the way most weapon changes can — it has to be a change to the shipped weapon.
- [2026-09-01] [med] **`Cargo`'s emergency bail-out has never run for any garrisonable building, and
  it was born that way.** `Cargo.INotifyDamage.Damaged` returns early for any actor carrying
  `GarrisonProtection` (`engine/OpenRA.Mods.Common/Traits/Cargo.cs:830-832`), and the entire
  `EmergencyBailDamageState` implementation sits ~45 lines below that return (`:876-943`). **Every
  garrisonable building carries `GarrisonProtection`**, so passengers never bail out of a burning
  building however far it is knocked down. The guard's own comment still says it skips "legacy damage
  forwarding" — which was true when `c9699af9` (2026-03-20) added it and the method contained nothing
  else, and stopped being true when `4e8e29e2` (2026-08-10) appended the bail beneath it.
  **Fix is narrowing the guard to wrap the forwarding block instead of returning** (~5 lines), but
  that is not a pure bug fix: with the guard fixed, garrisons would bail at `Heavy` (50% HP) by
  default, which is a real combat change and wants a design call plus a slot. Note also that
  `bailedOut` only re-arms below the threshold (`:884-885`), and an `Indestructible` building pinned
  at 1 HP is permanently `Critical` — so the bail would fire **once** and never again. Full analysis
  and ranked options in `WORKSPACE/garrison-destructibility-260901.md` (P1).
  (found while working on: garrison destructibility design audit)

- [2026-09-01] [low] **`RubbleProtection`'s `[Desc]` states something false at its own default.**
  `engine/OpenRA.Mods.Common/Traits/Garrison/GarrisonProtection.cs:27-29` describes the field as
  *"Lower than CriticalProtection"*, but **both C# defaults are 30** — the claim holds only because
  `^CivBuilding` overrides `CriticalProtection` to 70 (`mods/ww3mod/rules/ingame/civilian.yaml:117`).
  GTWR/PBOX/HBOX set `CriticalProtection: 80` and omit `RubbleProtection` entirely
  (`structures-defenses.yaml:153-156`), inheriting the 30 by omission rather than by decision.
  Doc-only fix; no behaviour change. (found while working on: garrison destructibility design audit)

- [2026-09-01] [low] **The beyond-map fog strip is drawn darker than the fog over the map, because its
  copy of the alpha curve omits the fog palette's own alpha.**
  `WorldRenderer.DrawBeyondMapActorFog` rebuilds ShroudRenderer's per-layer curve inline
  (`engine/OpenRA.Game/Graphics/WorldRenderer.cs:466-493`) and composites it straight into an RGBA
  fill. But on the map the same vertex alpha is multiplied by the layer's palette colour — the shader
  does `c *= vTint` (`engine/glsl/combined.frag`), and the solid fog tile is palette index 12
  throughout, which resolves through `MapLayersPalettes`' eight-entry cycle to
  `FogColors[4] = ARGB(160,0,0,0)`. So the strip is missing a 160/255 factor per layer: at visibility 1
  it paints 0.900 opacity where the map itself paints 0.744. The comment at `:466-468` asserts the two
  agree. Blast radius is small — the strip only dims actor sprite pixels overhanging the map boundary —
  which is why it was left alone rather than folded into the FogDarkness change on the same day.
  **Fix is a 160f/255f factor in that loop**, but it lightens the border for every mod in the tree, so
  it wants its own before/after screenshot. (found while working on: fog contrast / FogDarkness)

- [2026-09-02] [MED] **Roughly 60% of tournament losses are credited to nobody: `kills_cost` and
  `deaths_cost` do not reconcile between the two players.** In the only non-void run in the tree
  (`260902_0641_tournament-arena-composition-2p`, verdict v7, 2 matches), USA-bot's `deaths_cost` is
  $14,350 while Russia-bot's `kills_cost` is $4,500; summed both ways across all 4 player-records,
  $49,150 died and only $19,150 was credited as killed. In a 2-player match with no third party those
  two totals should agree. Candidate causes, none tested: crew ejection double-counting (a dying
  vehicle spawns 3 crew that then die separately — crew is 100% loss in this corpus); damage with no
  attributable killer; or the two accumulators in
  `engine/OpenRA.Mods.Common/Traits/World/BotVsBotMatchWatcher.cs` / `Tournament/MatchTypes.cs` simply
  ranging over different actor sets. **This matters beyond bookkeeping**: `deaths_cost` is the headline
  combat-efficiency metric in every bot benchmark, so if it is inflated relative to `kills_cost` then
  every trade ratio ever computed is biased against whichever bot loses more vehicles. Diagnosable
  statically by reading both accumulators — no game run needed. Not mentioned in any existing doc.
  (found while working on: loss mining, WORKSPACE/analysis/0902-loss-mining.md §1.2)
