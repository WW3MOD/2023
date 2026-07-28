# Recon: deploy-to-prone — current state + stance-governance proposals

**Date:** 2026-07-28
**Type:** Research + verify (READ-only). No code/YAML changed. Backlog idea 2026-07-21: "Deploy-to-prone → stance-governed."
**Scope note:** every `file:line` below verified against the working tree at write time (`main`). Cite these when implementing; do not trust memory. **PROPOSAL section is options only — no implementation.**

---

## 0. TL;DR

- **Deploy grants exactly one condition: `deployed`** (`GrantConditionOnDeploy` on `^CamoSoldier`, `infantry.yaml:258-259`). It is a manual per-unit toggle order (`GrantConditionOnDeploy` order string, hotkey/deploy cursor). Nothing auto-issues it.
- **`deployed` does nothing on its own** — it is one of four OR-clauses feeding `InfantryStates.ProneCondition` (`infantry.yaml:261`). It gates **prone**, which is the real payload.
- **Prone's payload:** −40% speed, per-damage-type damage reduction (down to 10–80%), a **smaller hitshape** (circle r20 vs r30), a **+1 concealment tier**, prone muzzle offset, and the `prone-` sequence. File:lines in §1.
- **Deploy is almost entirely redundant.** A *stationary* unit is already prone via the `!moving` clause and already dug-in (+1 concealment) via `GrantConditionOnMovement` after 200 ticks. **The ONLY behaviour `deployed` uniquely adds is crawl — staying prone *while moving* (60% speed, low profile, −40% damage, harder to spot/hit).** That is a genuine tactical advance mode nobody uses because it is a manual toggle.
- **No AI ever deploys infantry.** No bot module or squad FSM issues the deploy order (§3).
- **Phase-3 tactical positioning reads `deployed` as a hard opt-out** (`StancePositioningExecutor.cs:106,266-271`): a deployed unit is never repositioned. Changing deploy semantics **must** revisit this (§4).
- **⚠ Two curated docs are verifiably WRONG on current `main`:** both `architecture.md:251` and `plans/260722_ambush_undetected_design.md §3.1` state prone gives *no* detection/concealment benefit. It does: `DetectableAddativeModifier@Prone` (`infantry.yaml:684-686`) adds +1 to the required-vision tier, consumed inside the exact `Detectable.IsVisibleInner` the ambush doc cites (`Detectable.cs:99-108`). Flagged for a curation pass (§7). This materially strengthens the case for stance-governed prone — prone is *not* cosmetic.
- **Recommended shape: C-lite** — keep deploy's `deployed` condition/token as the low-level "forced-prone" primitive, but (a) stop exposing it as a manual per-unit deploy button, and (b) drive it from the Ambush fire-stance (auto-prone while in Ambush + stationary, honest crawl while Ambush + attack-moving). Rationale + blast radius in §6.

---

## 1. What deploy-prone grants today — the full condition chain

### 1.1 Where it lives (backlog said `^Soldier`; it is actually `^CamoSoldier`)
- `^Soldier` (`infantry.yaml:167`) is the base; **`^CamoSoldier` (`infantry.yaml:249`, `Inherits@Soldier: ^Soldier`) is where deploy is declared.** Every real soldier inherits `^CamoSoldier` (`Inherits@Type: ^CamoSoldier` at `:274,1092,1158,1276,1341,1409,1496,1568,1640,1712,1781,1929,2104,2214,2318`), and crew inherit it too (`crew.yaml:12`). So "every soldier has deploy" is correct; the declaration site is `^CamoSoldier`, not `^Soldier`.

### 1.2 Step 1 — the grantor
```
GrantConditionOnDeploy:                # infantry.yaml:258-259
    DeployedCondition: deployed
```
- Engine trait `GrantConditionOnDeploy` (`engine/.../Conditions/GrantConditionOnDeploy.cs`). On a `GrantConditionOnDeploy` order it queues `DeployForGrantedCondition` and, on completion, `GrantCondition("deployed")` (`:307-313`); a second deploy order revokes it (`:315-329`).
- **No `UndeployedCondition`, no `AllowedTerrainTypes`, no `Facing`, no `UndeployOnMove`, no deploy animation** on the infantry use — so deploy is instant, works anywhere, and (critically) **does NOT auto-undeploy on move** (`UndeployOnMove` defaults false, `GrantConditionOnDeploy.cs:60-61,144-145`). That is what enables crawl: a deployed unit keeps `deployed` while it walks.
- Order exposed via `DeployOrderTargeter("GrantConditionOnDeploy", …)` (`:168`) + `IIssueDeployOrder` (`:181-186`) — i.e. the standard deploy button / deploy key. Manual only.

### 1.3 Step 2 — `deployed` feeds prone
```
InfantryStates:                        # infantry.yaml:260-269 (^CamoSoldier)
    ProneCondition: deployed || suppressed > 30 || !moving || critical-damage
    ProneGrantsCondition: prone
    ProneSpeedModifier: 60
    ProneDamageModifiers: { Prone10Percent:10, Prone20Percent:20, Prone30Percent:30, Prone50Percent:50, Prone80Percent:80 }
```
- `^AmphibiousSoldier` overrides the clause to exclude water (`ProneCondition: !inwater && (deployed || …)`, `:280`).
- `deployed` is **one disjunct of four**. The other three fire automatically: `suppressed>30` (suppression), `!moving` (any stationary unit), `critical-damage`.

### 1.4 Step 3 — what `prone` actually does (the payload; engine `InfantryStates.cs`)
`InfantryStates : ISpeedModifier, IDamageModifier, IRenderInfantrySequenceModifier`:
| Effect | Where | Value |
|---|---|---|
| **Speed** | `InfantryStates.cs:182-193` (`ISpeedModifier`) | `ProneSpeedModifier` = **60%** while prone |
| **Damage reduction** | `InfantryStates.cs:195-205` (`IDamageModifier`) | per-damage-type from `ProneDamageModifiers` (10–80% of normal, only for matching warhead damage types) |
| **Muzzle offset** | `InfantryStates.cs:214` | `ProneOffset` default `(500,0,0)` |
| **Sequence** | `InfantryStates.cs:146-150` | `prone-` prefix (`ProneSequencePrefix`) |
| **`prone` condition granted** | `InfantryStates.cs:207-215` | → downstream consumers below |

### 1.5 Step 4 — downstream consumers of the `prone` condition (all in `infantry.yaml`)
| Consumer | Line | Effect of prone |
|---|---|---|
| `HitShape@Cover` (`RequiresCondition: prone`) | `142-145` | hitshape = **Circle r20** (vs standing `HitShape@Standing` r30, `:146-149`) → **smaller target, fewer shots connect** |
| `DetectableAddativeModifier@Prone` (`RequiresCondition: prone`) | `684-686` | **`VisionModifier: +1`** → +1 required-vision tier → **harder to detect** (see §1.7 — this is the doc-contradiction) |

Also note two **other** grantors of `prone` unrelated to deploy:
- `GrantCondition@HeavyDamageProne` (`:1038-1040`): grants `prone` while `heavy-damage-attained` (wounded units go prone).
- (and the `!moving` / `suppressed>30` / `critical-damage` clauses inside `ProneCondition` itself).

### 1.6 The `deployed` token is *reused* — the mine-clearing engineer
`infantry.yaml:1885-1889` (engineer, inherits `^CamoSoldier`):
```
ExternalCondition@MineProximity: { Condition: mine-proximity }
GrantCondition: { Condition: deployed, RequiresCondition: mine-proximity }
```
- A **second, automatic** grantor of `deployed` on this one unit type: near a mine → `deployed` → prone. **Anything that removes/renames `deployed` breaks this auto-prone-near-mines too**, and (see §4) auto-opts the engineer out of tactical repositioning while near mines.

### 1.7 ⚠ Prone DOES reduce detection (contradicts two curated docs)
- `DetectableAddativeModifier` "Modifies the required vision to see this actor" (`DetectableAddativeModifier.cs:14-19`); `GetDetectableVisionAddativeModifier()` returns `VisionModifier` (`:29-32`).
- `Detectable.IsVisibleInner` sums these into `detectable = Vision + Σmodifiers`, clamps to `[1, VisionLayers-1]`, then requires `byPlayer.MapLayers.AnyVisible(occupiedCells, detectable)` (`Detectable.cs:99-108`). **Higher `detectable` = a higher vision tier the enemy must have at that cell = harder to see.**
- The concealment stack is coherent and directional: cover `+2/+3` (`:678-683`), **prone `+1`** (`:684-686`), dug-in `+1` (`:687-689`), **firing `-2`** (`:695-697`), **moving `-1`** (`:698-700`).
- Therefore prone is worth **+1 vision tier of concealment** — modest but real, and it is *inside the same `IsVisibleInner`* that `architecture.md:251` and `260722_ambush_undetected_design.md §3.1` claim is posture-independent. **Both are stale/wrong on current `main`.** (Likely the `@Prone` modifier was added after the 2026-07-22 ambush write-up.) See §7.

---

## 2. Interaction with the existing prone / suppression system

**Same condition, single funnel — not parallel, not conflicting.** There is exactly one prone state (`InfantryStates.IsProne` / the `prone` condition). `deployed` is simply one of four OR-triggers into it. So:

- **Deploy is redundant with `!moving`.** A stationary undeployed soldier is *already* prone (via `!moving`) and already gets the smaller hitshape, damage reduction, and +1 concealment. Deploying it changes **nothing** while it stands still.
- **Deploy is redundant with dug-in for concealment.** `GrantConditionOnMovement` (`infantry.yaml:138-141`) grants `moving` while moving and `dugin` after `TimeToBeStill:200` ticks still; `dugin` adds another +1 concealment (`:687-689`). So a long-stationary unit auto-stacks prone(+1)+dugin(+1) with no deploy.
- **Deploy's ONE unique effect = crawl (prone while moving).** Because `deployed` holds prone true even when `moving` is true, a deployed unit that moves stays prone: **60% speed, r20 hitshape, prone damage reduction, prone `+1` concealment (net 0 vs the `moving −1`), `prone-` crawl anim.** An *undeployed* walker is not prone (moving, unsuppressed): 100% speed, r30 hitshape, `−1` concealment. So crawl is a real "slow, low, hard-to-hit, harder-to-spot advance" — deploy's only reason to exist, and the piece no other trigger reproduces.
- **Suppression path is independent and additive.** `suppressed>30` reaches the same prone state; the two never conflict (OR). Note the Phase-3 executor already refuses to move a unit above the prone-suppression threshold (`MaxSuppressionToMove:30`, `StancePositioningExecutor.cs:95-97,256-257`) precisely so it never stands a prone unit up — the engine already treats "prone" as a do-not-disturb positional state.

---

## 3. AI usage — does any bot deploy infantry?

**No.** Verified:
- Grep of `engine/.../Traits/BotModules/**` for `Deploy`/`deployed`: only hit is a comment about MCV deploy (`McvManagerBotModule.cs:167`) — not infantry.
- Grep of `engine/.../Traits/BotModules/Squads/**` for `Deploy`: **no matches.**
- No `AutoDeploy`-style trait or `IIssueDeployOrder` caller targets `^CamoSoldier`; deploy is issued only by the player (`DeployOrderTargeter` / deploy hotkey).
- Consequence: deploy-prone is **100% player-manual, and unused by the strategic layer.** Removing it costs the AI nothing.

---

## 4. Phase-3 interaction — what reads `deployed` and what breaks

**`StancePositioningExecutor` (Phase-2/3 tactical positioning) treats `deployed` as a hard opt-out.**
- Info field `DeployedVariable = "deployed"` (`StancePositioningExecutor.cs:102-106`), read via a variable observer into `currentDeployed` (`:138-139,202,210-213`) — deliberately **not** `RequiresCondition`, so units that never grant it stay inert.
- The opt-out (`TickIdle`, `:262-271`): `if (currentDeployed > 0) { ReleaseManagement(); State = None; return; }` — a deployed unit is **never repositioned**; the executor relinquishes any prior claim. Rationale in-code: "a deployed unit has expressed a stronger positional intent than a move order — a move would force an undeploy."
- Wired on `^Combatant` (`defaults.yaml:27-28`), gated `enable-tactical-positioning || enable-ai-experimental` (default-off for @stable/@normal; **default-ON for humans in Phase 3** via `GrantConditionOnHumanOwner@tacpos`, `defaults.yaml:44-45`).

**What breaks if deploy semantics change:**
- **If `deployed` is deleted/renamed:** `currentDeployed` is always 0 → the opt-out becomes dead code. Units the player *intended* to pin lose their "don't reposition me" signal — but note this opt-out is human-facing (executor is on for humans in Phase 3). Also the **mine-proximity engineer** (§1.6) silently loses its "don't walk me around while I'm clearing mines" protection.
- **If `deployed` is auto-granted by a stance (e.g. Ambush+stationary):** every such unit becomes a tacpos opt-out. This is mostly **already handled** — the executor *also* opts out `Stance < FireAtWill` (i.e. Ambush + HoldFire) at `:286-291` (the "un-ambush" fix, PIPELINE item 8). So an Ambush unit is *already* excluded from repositioning regardless of `deployed`; auto-granting `deployed` in Ambush would be **redundant with an existing opt-out, not a new conflict** — a point in favour of stance-governed prone (§6).
- **Non-issue:** vehicles' `deployed` (MSAR artillery deploy, `vehicles.yaml:411-442`) shares the token *name* but conditions are per-actor at runtime — no cross-contamination. A rename would just need to touch infantry.yaml + the executor's `DeployedVariable` default.

---

## 5. Interaction with the Ambush stance — is "prone while Ambush + stationary" a natural home?

**Yes, and it is nearly free — the plumbing already lines up.** From `DOCS/reference/architecture.md` (stances) + `WORKSPACE/plans/260722_ambush_undetected_design.md`:

- **Ambush is a real, orthogonal fire-stance** (`stance-ambush`, `AutoTarget`, `defaults.yaml:308`): idle Ambush units silently pre-aim and hold fire until spotted/damaged, then group-volley (`AutoTarget.cs` `AmbushTickIdle`). Fire discipline (HoldFire/Ambush/FireAtWill) is independent of positioning.
- **Ambush units are already stationary-by-intent and already opted out of repositioning** (`StancePositioningExecutor.cs:286-291`). So "prone while Ambush + stationary" adds prone to a state that is *already* "hold this cell." A stationary Ambush unit is *already* prone via `!moving` — so for the idle case, **auto-prone-in-Ambush grants nothing new that `!moving` doesn't already give.**
- **Where Ambush would genuinely want deploy's unique effect: attack-move.** The ambush design doc's Sub-behaviour A ("halt before contact", §5.1) and OBS on moving Ambush units note that a *moving* Ambush unit has no special behaviour today (`AttackFollow.cs:156`). An honest "crawl while Ambush + attack-moving toward contact" is exactly deploy's crawl primitive — the low-profile approach that keeps `+1` concealment and −40% damage on the advance. **That is the natural, non-redundant home.**
- **Honesty caveat (now updated by §1.7):** the ambush doc sold prone as "cosmetic, no concealment." That was wrong — prone is +1 concealment tier. So folding prone into Ambush is *more* valuable than the doc assumed: an Ambush crawl is measurably harder to spot than a normal walk (net 0 vs −1 vision tier).

---

## 6. PROPOSAL — candidate shapes (no implementation)

All three keep the low-level `prone`/`InfantryStates` payload untouched; they differ in **who drives `deployed` / prone-while-moving** and whether the manual deploy button survives.

### Shape A — Stance-governed auto-prone (Ambush drives prone)
Drop the manual deploy button; grant a prone-force from the Ambush fire-stance. Idle Ambush → prone (already true via `!moving`, so a no-op there); Ambush + attack-move → crawl (the new, valuable part). Implemented by making the `ProneCondition` include `stance-ambush` (or a new `ambush-crawl` token granted while Ambush+moving) instead of `deployed`.
- **Blast radius:**
  - Phase-3 executor opt-out on `deployed` (§4) goes dead — but Ambush units are *already* opted out via the `Stance < FireAtWill` branch (`:286-291`), so **no positioning regression** for the intended case.
  - **Mine-proximity engineer (§1.6) loses auto-prone** unless its `mine-proximity → deployed` grantor is repointed at the `prone` token directly (easy; keep `GrantCondition: Condition: prone`).
  - Removes a (barely-used) manual capability: players can no longer force-crawl a FireAtWill unit. Acceptable if crawl is reframed as an Ambush behaviour.
  - Must confirm no desync: `ProneCondition` is a `BooleanExpression` consuming a synced stance condition — fine (same class already consumes `suppressed`).
- **Revisit:** `architecture.md` stances section; the ambush design doc (its §5.1 already wants exactly this).

### Shape B — Keep deploy, gate visuals/UI only (minimal)
Leave the mechanic; just stop advertising it (hide the deploy button unless some "advanced controls" toggle), and/or relabel it "crawl." No condition-graph change.
- **Blast radius:** essentially none — `deployed`, the executor opt-out, and the engineer all keep working unchanged. Purely a discoverability/UX change.
- **Downside:** does not satisfy the user's actual intent ("forced-prone is a *behaviour* that should be stance/trait-governed, not a manual toggle"). Parks the idea rather than resolving it.

### Shape C — Delete manual deploy; fold prone into suppression/ambush as a pure behaviour (most aligned)
Remove `GrantConditionOnDeploy` from `^CamoSoldier` entirely. Prone becomes *only* an emergent behaviour: `!moving` (stationary), `suppressed>30`, `critical-damage`, `heavy-damage`, **and a new stance clause for the crawl case** (Ambush + attack-moving). Repoint the engineer's mine grantor to `prone`.
- **Blast radius:** superset of Shape A. Additionally removes the `deployed` token from infantry, so:
  - Executor `DeployedVariable` opt-out (§4) is fully dead — remove the field or leave inert. Human "pin this unit here" intent must be re-expressed via HoldPosition engagement-stance (which the executor *does* honour, `:295-301`) — arguably the correct home anyway.
  - Any map/scenario Lua or editor init that sets `DeployState` on infantry would break (grep before implementing — none found in infantry rules, but scenarios not audited here).
- **Revisit:** executor field cleanup, engineer grantor, docs, and a scan of maps/scenarios for infantry deploy inits.

### Recommendation — **C-lite (Shape A with the token kept as the primitive)**
Keep the `deployed`/`prone` condition *tokens* as the low-level forced-prone primitive (so the engineer's mine grantor and any future consumer keep a stable hook), but **remove the manual deploy button and drive prone-while-moving from the Ambush fire-stance** (Shape A's condition change), leaving idle prone to the existing `!moving` trigger.

Reasoning:
1. **Deploy's only non-redundant effect is crawl** (§2); the natural owner of "crawl toward contact" is the Ambush attack-move behaviour the ambush design doc already wants to build (§5). Governing it by stance is exactly the user's stated intent.
2. **The positioning opt-out already aligns** — Ambush units are already excluded from repositioning (`:286-291`), so stance-driven prone introduces *no* new Phase-3 conflict, unlike a naive delete.
3. **Prone is not cosmetic** (§1.7, +1 concealment) — an Ambush crawl is genuinely stealthier than a walk, so this is a real tactical feature, not a relabel.
4. **Keeping the token** avoids the engineer/mine regression and preserves a stable seam, so the change is smaller and reversible vs full Shape C.
5. Sequencing: this is a natural **rider on the Ambush-widening work** (`260722_ambush_undetected_design.md`, Sub-behaviour A). Do it *with* that, not before — the crawl-on-attack-move path is the same code touch.

**Verify-before-build checklist:** (a) confirm no scenario/editor infantry `DeployState` inits exist (only infantry rules audited here); (b) repoint the engineer mine grantor to keep auto-prone-near-mines; (c) confirm the new stance clause consumes only synced conditions (desync-safe); (d) fix the two stale concealment docs (§7).

---

## 7. Curation flag (do NOT edit here — out of task scope)

Two curated statements are verifiably wrong on current `main` and should be corrected by a curation pass:
- `DOCS/reference/architecture.md:251` — "Prone grants NO detection or concealment reduction." **Wrong:** `DetectableAddativeModifier@Prone` (`infantry.yaml:684-686`) = +1 vision tier, consumed in `Detectable.IsVisibleInner` (`Detectable.cs:99-108`).
- `WORKSPACE/plans/260722_ambush_undetected_design.md §3.1` (and its §1.4 line "Prone gives no detection benefit") — same correction. Likely the `@Prone` modifier post-dates the 2026-07-22 write-up.

(Per this task's hard rules — READ + one doc file — these are flagged, not edited.)
