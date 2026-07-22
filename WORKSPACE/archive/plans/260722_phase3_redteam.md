# Phase-3 red-team audit — human enablement, event bus, role resolver

Date: 260722. Read-only design audit conducted **before any Phase-3 code exists**, against
main @ `ed416722`. Targets: SPEC §7 Phase 3 (`260722_strategic_tactical_split_SPEC.md`,
RATIFIED default-ON human enablement + riders) and the role-resolver design
(`260722_unit_role_resolver_DESIGN.md` @ `2e6b7fd5`). Method mirrors the Phase-2 red-team
(`260722_phase2_redteam.md`): every claim carries a file:line citation against merged code;
negative claims are grep-proven.

Line references: `StancePositioningExecutor.cs` = `engine/OpenRA.Mods.Common/Traits/StancePositioningExecutor.cs`
(merged Phase 2 @ `a88ef596`); `AutoTarget.cs`, `UnitDefaultsManager.cs`, `ExternalCondition.cs`,
`GrantConditionOnBotOwner.cs` under `engine/OpenRA.Mods.Common/Traits/`; YAML under `mods/ww3mod/rules/`.

## Verdict table

| # | Severity | Finding |
|---|----------|---------|
| B1 | BLOCKING | Stale anchor: executor re-anchors only on the Adjusting-interrupt path; after `Arrived`/release, a player move leaves the old anchor live and the executor walks the unit back toward its previous position |
| B2 | BLOCKING | The designated per-type opt-out channel (UnitDefaultsManager) is per-machine unsynced state applied to synced sim fields — latent desync today, live desync at Phase-3 default-ON |
| B3 | BLOCKING | Role resolver rule-5 (Cargo → TransportLift) shadows rules 6–8: humvee, btr, strykershorad, bradley, bmp2, m113 all classify TransportLift; ground Recon is empty and SHORAD loses its vehicle members. Design doc's own worked examples are factually wrong against the YAML |
| S1 | should-fix | `ExternalCondition@tacpos` cannot express "granted by default for humans" — it is grant-by-source only; a new `GrantConditionOnHumanOwner` trait is required |
| S2 | should-fix | Executor moves player-deployed units (GrantConditionOnDeploy exists on infantry and vehicles) — breaks deliberate deploys |
| S3 | should-fix | Cell contention false-abort: no cell-level claims; two humans' units converge on the same edge cell, blocked arrival is misread as player interrupt, anchor churns |
| S4 | should-fix | Ambush stance: precise player placement is silently walked by the executor on threat approach — needs an explicit stance-policy decision (distinct from Phase-2 N1 HoldFire item) |
| S5 | should-fix | Role misclassifications beyond B3: e6 → MainBattle, mt mortar → IndirectFire (design says MainBattle), AA defense structures → ShortRangeAD (no Mobile guard), MSAR/MNLY/LCCV → Logistics via blanket `^Vehicle` Passenger, littlebird → Recon while armed |
| S6 | should-fix | Capture bot→human skips ledger `Release` (owner gate on the release path) — claim lapses only via TTL |
| S7 | should-fix | Event-bus rider has zero prior art and zero Phase-3 consumers — defer (YAGNI); the merged N4 surface is the seam |
| N1 | note | Guarding/escorting units never tick idle → never positioned; inconsistent with "default ON for humans" messaging |
| N2 | note | `Created` draws SharedRandom unconditionally on every profile — already priced into the current baseline; Phase 3 must not add draws or reorder trait construction |
| N3 | note | Claim TTL 150 vs long transits: `TickIdle` never runs while moving, so a claim can lapse mid-transit and another unit can double-book the cell |
| N4 | note | Benchmark byte-identity for bot matches survives Phase 3 *as designed* (human grant inert for bots; resolver is data-only cache) — but any executor code fix (B1, S2, S3) re-prices `@experimental` |
| N5 | note | No double-grant hazard: `RequiresCondition` is a boolean expression over disjoint grantors; granting both tokens is idempotent for the executor |

---

## B1 — Stale anchor: the executor walks units back to abandoned positions

**BLOCKING for human default-ON.** Acceptable-ish for bots (the squad FSM re-tasks units
constantly); catastrophic for humans (violates explicit player movement intent).

The anchor is set exactly once, lazily, on the first idle tick
(StancePositioningExecutor.cs:240-245):

```csharp
if (!hasAnchor)
{
    anchor = slotMemory?.AssignedSlot ?? self.Location;
    hasAnchor = true;
}
```

`hasAnchor` is cleared in exactly **one** place: `ResolveArrivalOrAbort`
(StancePositioningExecutor.cs:290-307), and only when `State == Adjusting &&
self.Location != currentTarget` — i.e. the unit was mid-adjustment and ended up somewhere
other than the executor's own target, which is read as a player interrupt. Neither
`ReleaseManagement()` (:492-503) nor `TraitDisabled` (:195-200) touches `hasAnchor`.

Failure sequence (human owner, default ON):

1. Unit idles at cell A → anchor = A.
2. Threat appears; executor adjusts to an edge cell near A, arrives → `State = Arrived`.
   The arrival path does **not** clear the anchor (:290-307 only fires on the
   Adjusting-mismatch branch).
3. Player orders the unit to cell B, 40 cells away. While moving, `TickIdle` never runs;
   no state transition happens.
4. Unit arrives at B, goes idle. `hasAnchor` is still true, anchor is still A. `State` is
   still `Arrived` with `self.Location != currentTarget` — but `ResolveArrivalOrAbort`
   does not fire in `Arrived` state, so nothing resets.
5. Next threat evaluation: `ChooseTarget` scans candidate edge cells **within
   `LeashRadius` of the stale anchor A** (leash check against `anchor`, :359-375), and
   bearing fallback (b) (:366-371) returns the bearing **toward A**. The executor issues a
   Move that drags the unit back across the map toward its abandoned position.

The same stale state survives ownership capture and condition revocation
(`TraitDisabled` :195-200 releases the claim and slot but keeps `anchor`/`hasAnchor`), so
toggling the opt-out condition off and back on resumes against the fossil anchor.

Phase 2 never surfaced this because bot-owned units are perpetually re-tasked by squad
FSMs (75-tick re-fires) and the ledger claim filter (`StateBase.ExcludeTacticallyCommitted`,
BotModules/Squads/States/StateBase.cs:155-171); a bot unit rarely completes a long
player-style relocation and then sits idle. A human's units do exactly that all game.

**Required fix (design-level, pre-implementation):** treat any externally-caused location
change as an anchor invalidation. Concrete shape: in `TickIdle`, before the threat scan,
detect `hasAnchor && State != Adjusting && self.Location` outside `LeashRadius` of
`anchor` → clear `hasAnchor`, reset `State = Watching`, release claim/slot. Equivalently:
clear `hasAnchor` in `ReleaseManagement()` and make every arrival/idle-at-foreign-location
path call it. Either way the invariant must be: **the anchor is never older than the
unit's last non-executor movement.** This is an executor code change → re-prices
`@experimental` (see N4).

## B2 — The opt-out channel is per-machine state feeding synced simulation

**BLOCKING for the ratified opt-out mechanism as specified.** SPEC §7 designates the
existing Ctrl-Alt-click per-type stance default (UnitDefaultsManager) as the player's
opt-out. The audit question was "check how UnitDefaultsManager avoids desync today." The
answer: **it doesn't — the hazard is latent, masked by single-human-vs-bot play.**

Evidence chain:

- `UnitDefaultsManager` loads from `Path.Combine(Platform.SupportDir, "ww3mod",
  "unit-defaults.yaml")` in `IWorldLoaded.WorldLoaded` (UnitDefaultsManager.cs:38-42) — a
  **per-machine settings file** — and saves it at `IGameOver.GameOver` (:44-47). Plain
  dictionary; no orders; no `[Sync]`.
- `AutoTarget.Created` applies those defaults **into synced simulation fields**
  (`stance`, `engagementStance`, cohesion, resupply behavior) for every
  `Owner.Playable && !Owner.IsBot` actor (AutoTarget.cs:355-388). The stance fields are
  plain, unhashed members (:249-252) — so the divergence is *silent*: no OOS hash
  mismatch, just different sim evolution per client (same failure class as unsynced
  `LocalRandom` use).
- Live changes are fine: the stance selector widget issues synced orders for the current
  selection (`world.IssueOrder(new Order("SetEngagementStance", ...))`,
  EngagementStanceSelectorLogic.cs:87, 98-103), resolved in synced `ResolveOrder`
  (AutoTarget.cs:426-439). Only the **persisted-defaults channel** bypasses the order
  stream.

Today's exposure: in human-vs-human multiplayer, client X spawns player Y's units with
whatever `unit-defaults.yaml` X's machine has — both clients run `Created` for *all*
actors. Two machines with different files diverge at first spawn. Replays likewise: the
file is re-read at replay time and **was saved at GameOver of the recorded game**
(:44-47), so a replay of the very session that changed a default diverges from the live
run. This has not bitten because the mod is played single-human-vs-bots where one
machine's view is the only view.

Phase 3 escalates this from latent to load-bearing twice over:

1. It makes stance defaults the *documented opt-out* for a system that **issues Move
   orders** — divergent stance → divergent movement → units in different cells per
   client, not just different fire behavior.
2. Default-ON multiplies the population of executor-managed actors from
   "experimental-bot units" to "every human combatant."

**Required fix (design-level):** the per-type defaults must cross the order stream before
touching sim state. Minimal shape: keep `UnitDefaultsManager` as the UI/persistence layer,
but have the *owning client only* emit a synced order (e.g. `"SetUnitTypeDefaults"` with
the serialized per-type table, or per-actor default orders at spawn) which all clients
resolve identically; `AutoTarget.Created` reads a per-player synced store instead of the
local file. Alternative (cheaper, blunter): Phase 3's opt-out does not ride stance at all —
see the S1 grant trait, and gate opt-out on a per-*player* synced toggle order. Either
way, shipping default-ON with the current channel bakes a silent-desync footgun into the
flagship human-facing feature.

## B3 — Role resolver: rule-5 Cargo shadowing guts the taxonomy

**BLOCKING for the resolver design as written** (though cheap to fix — it is a rule-order
problem, not an architecture problem). The first-match-wins cascade puts rule 5
(`Cargo` → TransportLift) ahead of rules 6 (AA → ShortRangeAD), 7 (arty), and 8
(fast+light → Recon). Against the actual YAML:

- **humvee** has `Cargo: 8` (vehicles-america.yaml:139-141) → TransportLift. The design
  doc's rule-8 commentary explicitly claims humvee (Speed 150, :62) lands Recon. Wrong.
- **btr** has `Cargo: 8` (vehicles-russia.yaml:103-105) → TransportLift. Same wrong claim
  in the design (Speed 110, :62).
- **strykershorad** has `Cargo: 9` inherited via its Stryker base
  (vehicles-america.yaml:970-972) → TransportLift, shadowing its `Stinger.quad` AA
  armament (:894). A SHORAD vehicle classified as a troop carrier.
- bradley (Cargo 6), bmp2 (Cargo 7), m113 (Cargo 12) → TransportLift, defensible for m113
  but debatable for IFVs that Phase-4 consumers will want in the line of battle.

Net effect: **rule 8 matches zero ground units as written** (grep-verified: every
fast-light candidate carries Cargo), ground Recon is an empty set, and ShortRangeAD loses
its only mobile American member. Two of the design doc's three worked examples for these
rules are false; a third (§8 Q2, "mt derives MainBattle") is also false — mt's
`60mm_Mortar` has Range 25c0 / MinRange 8c0 (infantry.yaml:1508;
weapons-ballistics.yaml:526-527), tripping rule 7 → IndirectFire. ~~Additional errata: the
design references **msta** and **avenger**, neither of which exists in the YAML (the
Russian gun-arty is **giatsint**, vehicles-russia.yaml:450).~~
**[CORRECTION, post-implementation review:** the msta/avenger sub-claim was a false
positive — `git show 2e6b7fd5:…DESIGN.md` contains neither name (grep-clean); the design
already used giatsint. Audit erratum, not a design erratum. Adjudicated during the
phase3-resolver merge review; see DISCOVERIES 2026-07-22.**]**

What *does* survive contact (verified by exhaustive classification pass): artillery
(m109, giatsint, m270, grad, tos, HIMARS, iskander all → IndirectFire, correct — tos via
the MinRange clause alone), tunguska → ShortRangeAD (9M311 `ValidTargets: Air`,
vehicles-russia.yaml:856), MANPADs → ShortRangeAD (infantry.yaml:1721), tecn →
CaptureSpecialist (sole owner of `^CapturesNeutralBuildings`, infantry.yaml:2164,
897-906), truk → Logistics (SupplyProvider, vehicles.yaml:541), heli split via
`AIHelicopterRole` (enum is actually `HelicopterAIRole`, AIHelicopterRole.cs:16 — design
uses the wrong name), fixed-wing → AttackAir, tanks → MainBattle. The
`Air`-vs-`Helicopter` target-type split means rule 6 never false-fires on
helicopter-only weapons (30mm.Tunguska.AA, weapons-ballistics.yaml:452-455) — it
*under*-matches, which is the safe direction.

**Required fix:** reorder the cascade — ShortRangeAD, IndirectFire, and Recon must all be
tested **before** TransportLift — and add a discriminator to TransportLift itself
(e.g. Cargo present **and** no armament beyond self-defense caliber, or an explicit
`AIUnitRole` override on the ~6 ambiguous hulls: humvee, btr, strykershorad, bradley,
bmp2, m113). The `AIUnitRole` override mechanism already in the design is the right
escape hatch; the audit's point is that without reordering, the *derivation* half of the
design produces a taxonomy Phase-4 consumers cannot use. Correct the doc's worked
examples and phantom units in the same pass.

---

## Should-fix findings

### S1 — `ExternalCondition@tacpos` cannot grant a default

The Phase-2 YAML seam says "Phase 3's per-type stance wiring will grant externally"
(defaults.yaml, `ExternalCondition@tacpos` comment). But `ExternalCondition` grants only
via `GrantCondition(Actor self, object source, ...)` (ExternalCondition.cs:110) — every
grant needs a live source object and explicit revocation bookkeeping. There is no
declarative "on by default" path, and grep confirms no existing trait grants a condition
on human ownership (`GrantConditionOnHumanOwner|Playable.*grant` → zero hits;
`GrantConditionOnCombatantOwner` exists but its predicate `!Owner.NonCombatant` includes
bots — wrong tool).

**Recommendation:** new `GrantConditionOnHumanOwner`, a ~40-line mirror of
`GrantConditionOnBotOwner` (GrantConditionOnBotOwner.cs:46 `Created`, :55
`OnOwnerChanged`) with predicate **`Owner.Playable && !Owner.IsBot`**. This exact
predicate matters: it matches the gate `AutoTarget.Created` already uses for human
defaults (AutoTarget.cs:355-388) and it **excludes the scenario garrison players**, which
are `Playable: False, NonCombatant: False` (maps/river-zeta-ww3/scenarios.yaml:1-36) —
`!IsBot` alone would enroll garrisons into tactical positioning and change scenario
behavior. Replace `ExternalCondition@tacpos` with the new trait in defaults.yaml; the
consumed-condition lint stays satisfied.

### S2 — Deployed units get walked

`GrantConditionOnDeploy` is live on infantry (ingame/infantry.yaml:249) and vehicles
(ingame/vehicles.yaml:410). A player who deploys a unit has expressed a stronger
positional intent than a mere move order, yet the executor's gate
(`RequiresCondition: enable-tactical-positioning || enable-ai-experimental`,
defaults.yaml) doesn't know about it — first idle tick after deploy, the unit is eligible
for adjustment and may undeploy-and-move (or try to and thrash, depending on the deploy
trait's Move handling). **Fix:** extend the executor's `RequiresCondition` with
`&& !deployed` on actors that grant a deploy condition, or add an in-code guard. YAML-only
variant keeps the executor byte-identical (N4).

### S3 — Cell contention → false player-abort → anchor churn

`IsUsableCell` (StancePositioningExecutor.cs:457-463) checks `CanStayInCell` +
`CanEnterCell` only — there are **no cell-level claims** (the ledger is actor-keyed,
`tacpos:<actorId>`). Two units evaluating the same threat picture with the deterministic
tie-break can select the same edge cell; the loser is blocked, ends up adjacent, and
`ResolveArrivalOrAbort` (:290-307) reads `self.Location != currentTarget` as a **player
interrupt** — releasing management and (correctly, here) clearing the anchor, then
re-anchoring at the shoved position next idle tick. With many human units packed at
default-ON this is anchor churn and visible jitter, and it also interacts with B1's fix
(the interrupt heuristic cannot distinguish "player moved me" from "I got bumped one
cell"). **Fix direction:** treat arrival within 1 cell of `currentTarget` as arrival, not
abort; optionally register a soft cell reservation via the existing slot-memory machinery
(CohesionSlotMemory already dedupes slots).

### S4 — Ambush placement is silently defeated

Phase-2 N1 flagged the HoldFire question; ambush is the sharper human-facing case. A
player placing units in an Ambush fire-stance behind cover is choosing *exact cells*. At
default-ON, threat approach triggers repositioning that reveals/un-ambushes them. This is
a **policy decision the Phase-3 design must make explicitly**: either specific
fire-stances (Ambush, HoldFire) imply positioning opt-out (cleanest — one more clause in
the grant/`RequiresCondition`, using the `ConditionByEngagementStance` grants already
present at AutoTarget.cs:331), or the documented opt-out story must tell players to
Ctrl-Alt-click those types — which, per B2, is currently the desync channel.

### S5 — Residual role misclassifications (beyond B3)

- **e6 → MainBattle**: design rule 4 claims e6 falls in the heal/repair-only clause; e6
  carries a lethal MP5 (infantry.yaml:1802-1804). Needs `AIUnitRole: Logistics` (or
  rule-4 broadening); medi is correctly caught (Heal armament, infantry.yaml:2125-2127).
- **AA defense structures → ShortRangeAD**: rule 6 has no `Mobile` guard; the three AA
  defenses (structures-defenses.yaml:626, 711, 793) classify as ShortRangeAD. If the
  taxonomy is meant to describe *maneuver* units, add a Mobile requirement; if
  structures-in-taxonomy is intended, document it.
- **Blanket `^Vehicle` Passenger** (vehicles.yaml:60) drags MSAR radar (:361), MNLY
  minelayer (:443) and the LCCV MCV (:562) into rule-4 Logistics. Possibly acceptable;
  should be a decision, not an accident.
- **littlebird → Recon** while armed (`AIHelicopterRole: Scout`,
  aircraft-america.yaml:109-110): fine if Recon may shoot, but consumers assuming Recon ⇒
  don't-engage will misuse it.
- Crew actors and the DR drone operator land MainBattle (crew.yaml:24-26) — harmless
  while consumers are Phase 4, worth a `None` override eventually.

### S6 — Capture bot→human leaks the ledger claim

Both `Commit` (:483-487) and `Release` (:494) sit behind `self.Owner.IsBot`. A bot unit
holding a `tacpos:` claim that is captured by a human skips `Release` in
`ReleaseManagement()` — the claim lapses only via TTL (150 ticks). Bounded, but during
that window `StateBase.ExcludeTacticallyCommitted` (StateBase.cs:155-171) still filters
the actor and the ledger reports a phantom commitment. **Fix:** release unconditionally
(release of a claim the ledger doesn't hold is a no-op) or key the gate on "was
committed," not current ownership.

### S7 — Defer the event-bus rider (YAGNI)

Grep-proven: no event-bus prior art anywhere in the engine (`BotEvent|EventBus|event bus`
→ only the N4 comment at StancePositioningExecutor.cs:115), and **no Phase-3 consumer** —
the event-driven commitment-revision retrofit that would consume it is itself deferred.
The merged N4 surface (`AdjustmentState`/`CurrentTarget` public getters + the `tacpos:`
ledger grammar) *is* the seam, and its only current consumer is the executor itself
(grep-proven). Building a bus now means designing an API against zero callers while B1/B2
fixes are competing for the same budget. **Recommendation:** cut the bus from Phase 3;
re-price it with Phase 4's first real consumer.

---

## Notes

### N1 — Escorting/guarding units are never "idle"

The executor acts only in `TickIdle` with `CurrentActivity == null`
(StancePositioningExecutor.cs:240 region). Units on Guard/escort activities never
qualify, so "default ON for all human combatants" is really "default ON for *stationary
idle* combatants." Not a bug — but the player-facing description and any tutorial text
must say "idle units adjust their position," or players will report escorts as broken.

### N2 — Byte-identity caveat: `Created` already draws SharedRandom everywhere

`Created` runs unconditionally for conditional traits and draws
`SharedRandom.Next(0, EvaluateCooldown)` (StancePositioningExecutor.cs:168-180) on every
profile and owner. This is already priced into the current `@stable` baseline (Phase-2
merge re-baselined), so the Phase-2 red-team's "byte-identical when disabled" claim is
imprecise-but-moot. The Phase-3 discipline it implies: **no new SharedRandom draws in any
`Created`/`IWorldLoaded` path, and no trait-order changes in `^Combatant`** — either
shifts the draw sequence and silently re-baselines `@stable`. The role resolver as
designed (pure rules inspection, no RNG) is safe; keep it that way.

### N3 — Claim TTL vs long transits

`TickIdle` doesn't run while moving, so a claim (TTL 150) heartbeats only while idle. An
executor-issued adjustment normally arrives well within TTL, but pathological paths
(blocked routes, long detours) can outlive the claim, letting another unit double-book
the destination cell — converging with S3. Worth one autotest assertion, not a redesign.

### N4 — Benchmark governance verdict

The byte-identity argument for bot benchmark matches **holds for Phase 3 as designed**:
(a) the human grant (S1 trait) evaluates `Owner.Playable && !Owner.IsBot` — false for
every tournament participant (bot matches confirmed: tournament results carry `bot_type`,
tools/autotest/aggregate-tournament.sh; scenario garrisons are `Playable: False`,
scenarios.yaml:1-36) — so no condition state changes for bots; (b) the role resolver is a
data-only `IWorldLoaded` cache with no consumers (Phase 4, `UseUnitRoles` default-off) and
no RNG. **However**, the B1/S2/S3 fixes are executor *code* changes that alter behavior
for `@experimental` (bots grant the same condition via
`GrantConditionOnBotOwner@tacpos`, defaults.yaml) — those must go through the standard
`@experimental` pricing run before merge, and B1's anchor-invalidation will plausibly
*improve* S2-class metrics (fewer fossil-anchor walks for re-tasked bot units too).

### N5 — No double-grant hazard

`RequiresCondition: enable-tactical-positioning || enable-ai-experimental` is a boolean
expression; grantors are owner-disjoint (human-grant vs `GrantConditionOnBotOwner@tacpos`,
Bots: experimental). A hypothetical future overlap is idempotent for the executor — the
expression is truthy either way. No stacking semantics are consumed.

---

## Hardened implementation brief (Phase 3)

Ordered by dependency, not severity. Items 1–3 are the pre-conditions for flipping
default-ON; the resolver track (item 5) is independent and can land in parallel.

### 1. Anchor lifecycle fix (B1) — executor change, re-prices `@experimental`

In `TickIdle`, before threat evaluation:

```csharp
// Anchor invalidation: any non-executor relocation outside the leash fossilizes
// the anchor; a stale anchor must never out-live the player's last move.
if (hasAnchor && state != AdjustmentState.Adjusting
    && (self.Location - anchor).LengthSquared > leashSq)
    ReleaseManagement();          // now also clears anchor/hasAnchor
```

- Move `hasAnchor = false; anchor = default;` into `ReleaseManagement()` (:492-503) and
  ensure `TraitDisabled` (:195-200) routes through it. Every release path then restores
  the "anchor born on next idle tick" invariant (:240-245).
- Keep the existing Adjusting-interrupt path (:290-307) but soften it per S3: location
  within 1 cell of `currentTarget` ⇒ arrival.

### 2. `GrantConditionOnHumanOwner` (S1) — new trait, ~40 lines

Mirror `GrantConditionOnBotOwner` exactly (Created + INotifyOwnerChanged). Predicate in
both paths: `owner.Playable && !owner.IsBot`. YAML:

```yaml
^Combatant:
	GrantConditionOnHumanOwner@tacpos:
		Condition: enable-tactical-positioning
```

replacing `ExternalCondition@tacpos` in defaults.yaml (blank-line discipline: it is a
sub-node of the existing `^Combatant` entry, not a new top-level entry). Lint stays
green: the condition remains granted and consumed.

### 3. Opt-out channel (B2 + S4) — do NOT ship default-ON on the unsynced path

Two acceptable shapes; pick one, do not blend:

- **(a) Synced defaults (correct fix):** owning client serializes its per-type defaults
  table into one synced order at game start (and on each Ctrl-Alt-click change);
  `ResolveOrder` populates a per-player synced store; `AutoTarget.Created` reads that
  store instead of `UnitDefaultsManager` directly. UnitDefaultsManager becomes pure
  UI/persistence. Fixes the latent MP/replay desync for *all four* default classes, not
  just positioning.
- **(b) Stance-decoupled opt-out (minimal Phase 3):** positioning opt-out doesn't ride
  stance defaults at all. Add the executor gate
  `RequiresCondition: (enable-tactical-positioning || enable-ai-experimental) && !hold-position-stance && !deployed`
  where `hold-position-stance` comes from the `ConditionByEngagementStance` grants
  (AutoTarget.cs:331) and `deployed` from the existing deploy grants (S2). Stance changes
  are already synced via orders (EngagementStanceSelectorLogic.cs:87), so this opt-out is
  desync-free *today* — and it resolves S4 by fiat (HoldPosition/Ambush ⇒ no
  repositioning). The B2 latent bug then remains, but stops being load-bearing for
  Phase 3; file it as a standalone bug in `WORKSPACE/bugs/discovered.md`.

Option (b) is the recommended Phase-3 scope; (a) is the recommended follow-up.

### 4. Small executor hardening

- S3 arrival tolerance (1 cell) — same patch as item 1.
- S6: unconditional ledger `Release` in `ReleaseManagement()`.
- S7: **delete the event-bus rider from the Phase-3 plan.**

### 5. Role resolver (B3 + S5) — data-only track

- Reorder the cascade: CaptureSpecialist → Logistics(unarmed/heal) → **ShortRangeAD →
  IndirectFire → Recon** → TransportLift → AttackAir → MainBattle → None.
- TransportLift discriminator: `Cargo` present **and** not already matched above **and**
  (unarmed or `AIUnitRole` says so). Seed `AIUnitRole` overrides: `e6: Logistics`,
  `bradley/bmp2: MainBattle` (or leave TransportLift — decide with Phase-4 consumers),
  `strykershorad` needs **no** override once AD precedes Cargo.
- Rule 6 gains a `Mobile` requirement unless structures-in-taxonomy is deliberately
  wanted (S5).
- Correct the design doc: humvee/btr worked examples, mt → IndirectFire, enum name
  `HelicopterAIRole`, remove phantom units msta/avenger (giatsint is the real hull).
- Cache stays `IWorldLoaded` (per-world `Map.RuleDefinitions`, Map.cs:176/208/538 —
  verified: ruleset is per-map, per-world caching is required, `IRulesetLoaded` ordering
  is unordered across actors, Ruleset.cs:49-62). No RNG, no consumers, `UseUnitRoles`
  stays default-off → `@stable` untouched (N2).

### 6. Test plan

Single autotest run per bug per the standing rule; batch/tournament only with explicit
goahead.

- **B1 regression (autotest):** spawn human-owned unit, force adjustment to arrival,
  scripted Move 20+ cells, idle, inject threat near the *new* position → assert the
  executor's target is within leash of the new location, never the old anchor.
- **S3 (same test, second assertion):** two units, one threat, converging edge cells →
  assert neither unit oscillates (no second Move order within N ticks of arrival).
- **Opt-out (autotest):** unit with HoldPosition stance under threat → assert zero
  executor Move orders; deployed unit under threat → same.
- **Resolver (unit test, no game launch):** NUnit table test in OpenRA.Test asserting
  the full classification table from this audit (m109 IndirectFire, humvee Recon,
  strykershorad ShortRangeAD, tecn CaptureSpecialist, e6 Logistics-via-override, btr
  Recon, m113 TransportLift, tunguska ShortRangeAD, BADR TransportLift, mt IndirectFire)
  against the loaded ruleset — this is the cheapest high-value test in the whole phase.
- **Benchmark:** one `@experimental` pricing run after items 1+4 (executor changed); no
  `@stable` re-baseline expected — verify byte-identity of one `@stable` replay hash
  before/after as a tripwire (N2).
- **Human-sim scenario:** warranted — a short scripted scenario (player-owned squad,
  scripted threat waves, scripted player Move orders between waves) exercising
  B1/S3/S4 in one map would catch the whole "fights the player" class that autotests
  under-sample. Stage per DEMO recipe, no Test.Pass/Fail.

---

*Audit basis: main @ ed416722 (executor merged @ a88ef596, resolver design @ 2e6b7fd5).
All greps and file reads current as of this commit; no builds, tests, or game launches
performed.*
