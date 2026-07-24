# Discoveries

> Patterns, gotchas, and insights found during work. Dated entries.
> Stable, broadly applicable items should also go into CLAUDE.md.

## 2026-07-24 — Phase-4b role migration: three more consumers, and why each is equivalence-preserving-but-robust (PIPELINE item 10)

Extended the Phase-4a `UseUnitRoles` pattern to the air-squad, capture, and adaptive-production modules. Each consumes `UnitRoleResolver` behind a per-module `UseUnitRoles` flag (true only on `@experimental`); with the flag off the legacy branch is byte-identical. Non-obvious findings:

- **Air squads: the coarse role alone can't separate the two air owners — both fixed-wing strike AND attack helis classify `AttackAir`.** `SquadManagerBotModule.IsAirSquadUnit` (`SquadManagerBotModule.cs`, was `AirUnitsTypes.Contains` at old line 286) must therefore gate on `role == AttackAir && HasTraitInfo<BuildableInfo>() && !HasTraitInfo<AIHelicopterRoleInfo>()`. The `!AIHelicopterRole` guard keeps attack helis (HELI/HIND/MI28 — `AttackHeavy` → `AttackAir`) owned by `HelicopterSquadBotModule` (which already reads the fine-grained `AIHelicopterRole` directly), and the `Buildable` guard drops the `-Buildable` airstrike-power spawns `a10.airstrike`/`frog.airstrike` (`aircraft-{america,russia}.yaml`) — they `Inherit: A10`/`FROG`, keep Armament, and so *also* classify `AttackAir`; without the Buildable filter the role path would scoop a transient support-power plane into a persistent squad. For the current roster this reproduces `{A10,F16}`/`{MIG,FROG}` exactly. `HelicopterSquadBotModule` needed **no** change — it was already role-driven pre-4b.

- **CaptureCoordinator: the capturer pool is one `ActorIndex`, so migrate at the source, not per-query.** `capturingActors` (an `OwnerAndNamesAndTrait<CapturesInfo>` built from `CapturingActorTypes`) feeds *seven* call sites (diagnostic log, idle-dispatch, floor counts, production pull). Rather than bolting a role filter onto each, the index is rebuilt **once** on the first `BotTick` from `resolver.NamesWithRole(UnitRole.CaptureSpecialist)` (`CaptureCoordinatorBotModule.cs`, rebuild block right after the goal-guard resolve). `ActorIndex` seeds from `world.Actors` at construction and then tracks `ActorAdded`/`ActorRemoved`, so a first-tick rebuild loses nothing. It must be lazy (not in the ctor): the world-trait resolver's cache is only guaranteed populated by `IWorldLoaded`, which may run after a player module's `Created`. `CapturingActorTypes` stays in YAML (kept non-empty) both for the `@stable` twin and because the `QueueCaptureOrders` early-return still guards on it.

- **AdaptiveProduction: the role taxonomy has no anti-vehicle/anti-infantry split, so the filter is a class-purity sanity gate, not a re-derivation — and it must add zero RNG draws.** `AdaptiveProductionBotModule.cs` carries a per-request `UnitRole[]` (AntiAir → `{ShortRangeAD}`, AntiVehicle → `{MainBattle,IndirectFire}`, AntiInfantry → `+{Recon}` because the light wheeled scouts humvee/btr are `Recon` yet valid infantry counters) and, when `UseUnitRoles` is on, filters the buildable candidate list by role **before** the existing `candidates.Count == 0` check and the single `candidates.Random(world.LocalRandom)` draw. So the RNG call sequence is untouched (still one draw per non-empty pool). Every currently-configured pool member already classifies into its category, so with the flag on the picks are unchanged for today's roster — the value is pruning a future mis-listed unit (the `ai.yaml:349`-style class defect) rather than changing current behavior.

- **Common thread (same as the Phase-4a IFV-override alignment): for the current roster all three migrations are behavior-equivalent; the payoff is robustness to roster edits + class-driven purity, enforced at build time.** `CheckUnitRoleTable.cs` gained `a10/f16/mig/frog → AttackAir` pins (the capture/AA/ground units it already covered), and `UnitRoleResolverTest.cs` gained the fixed-wing/attack-heli/transport classification rows plus three consumer-predicate tests (air-squad gate, capturer class, adaptive-production category filters). 344 NUnit (was 341), `make.ps1 all` + `make.ps1 test` green (the `gtwr/pbox/hbox` "being-captured" lint errors are pre-existing, unrelated to roles).

## 2026-07-24 — Influence Stage D: the heli danger consumer rides on Stage 0, and the air channel's ground-only baseline is exactly what makes `safeThreshold = 0` mean something

Wiring the attack-heli AA consumer (`wt/stage-d`, base e6e9f1c3) surfaced a few load-bearing facts:

- **The Stage-B air channel carries NO territory baseline** (`DangerFieldLayer.StampBaseline` / `DangerKernelMath.BaselineChannels` return `(contribution, 0)`), so a non-zero `AirDanger(player, cell)` reading means *a believed enemy weapon can actually shoot a heli here* — not "near enemy ground." That is what lets Stage D use `AirDangerSafeThreshold = 0` as a literal "outside every believed AA envelope" test for both the leash target and detour acceptance. If a future air baseline is ever added, that default stops meaning "AA-clear" and the leash will refuse safe ground — the invariant in `DangerFieldLayer` (~line 177) is a Stage-D dependency, not just a Stage-B nicety.
- **Stage D layers on Stage 0, it does not replace it.** The consumer only rewrites the *destination cell* of the existing standoff `AttackMove` (leash to the AA-safe cell nearest the target, then a lateral detour waypoint if the straight approach crosses AA) and adds a withdraw-on-spike transition. The stop-and-fire-at-missile-range mechanic is still Stage 0's `StandoffEngagement`. So `DangerFieldAvoidance` is gated to *ride on* `StandoffEngagement` (`avoid = standoff && DangerFieldAvoidanceEnabled(...)`): with standoff off there is no attack-move destination to steer, so avoidance is inert rather than issuing conflicting orders.
- **Determinism without a determinism class.** The nav decisions (`HeliDangerNav`: path-max sampling, leash ring-search, lateral detour, safest-retreat-ring) are pure integer walks over a `Func<CPos,int>` air sampler with fixed candidate order and iteration-order tie-breaks — zero random draws, matching the influence-stack invariant (decision file 10). Off-map cells are fed as `Impassable = int.MaxValue` by the caller so a leash/detour/retreat search never steers off the playable area, and the pure functions stay world-free (NUnit feeds a synthetic radial envelope). Split from the FSM the same way `DangerKernelMath`/`ControlFieldMath` were, so the logic table is pinned at build time without a game run.
## 2026-07-24 — Unusable supply-truck residue now counts-as-empty and evacuates (PIPELINE item 4)

A near-empty supply truck holding a residue too small to give any nearby soldier a batch would park at the front forever. There are **two** distinct sticking points, both fixed:

- **The only empty-truck evac path is `DropsSupplyCache.OnBecomingIdle`** (`DropsSupplyCache.cs:196`), and it early-returns on `supply.CurrentSupply > 0`. A residue truck never trips it, so it never evacuates. `INotifyBecomingIdle` also only fires on the idle *transition* — a truck that goes idle *then* has its residue become unusable never gets a second call. Fixed by (a) gating on the new `SupplyProvider.CountsAsEmpty` instead of `> 0`, and (b) adding `ITick` to `DropsSupplyCache` that re-checks `self.IsIdle && supply.CountsAsEmpty` each tick and runs the same `EvacuateOrRestock` helper. `RotateToEdge`/the restock `MoveTo` make the actor non-idle, so the tick self-limits (no re-queue spam).

- **Chosen "counts as empty" predicate** (`SupplyProvider.ResidueVerdict`, pure + NUnit-tested in `SupplyResidueTest.cs`, **called live from `UpdateTarget`**). It is fed the two facts a greatest-need scan produces: `serviceableNeedyPresent` = a reachable unit we can afford one batch for cleared `MinNeedThreshold` (`FindGreatestNeedTarget` picked a best target), and `unaffordableNeedyPresent` = a reachable needy unit exists that we can't afford. Returns **true** (unusable → evac), **false** (usable → keep serving), or **null** (no demand → leave the latch unchanged, so an evacuating truck keeps evacuating). This is deliberately `MinNeedThreshold`-aware: a *near-full* affordable unit does NOT keep a residue "usable" (it's below threshold, no best target) — matching the live scan. The earlier pure predicate (`CountsAsEmptyResidue`) ignored `MinNeedThreshold` and gave the *opposite* answer in that mixed case, so it was replaced. `CountsAsEmpty` = `currentSupply <= 0 || residueUnusable`. Latched (`residueUnusable`), cleared on genuine replenish — every refill path (`AddSupply`, and `TryRestock` which now routes its `+= taken` **through** `AddSupply`) clears it, so a full truck never shows a phantom red bar / evac.

- **An Evacuate truck must keep serving below `RestockThreshold`.** `SupplyProvider.Tick`'s low-supply branch exists to *reserve* the last bit of supply for the drive home to restock, so it stops serving and returns before the serve block. But a truck that will never restock (Evacuate stance) has no trip to reserve for — reserving just stranded it, amber-barred, at `~49/750` next to a unit it could still help (the common end-state, since 19 pools cost `SupplyValue: 1`). New `KeepServingBelowThreshold()` = `EvacuateOnUnusableResidue && !ShouldSelfRestock()` skips that reserve-and-return so the truck serves down to the last usable batch; only then does the residue go unusable and `CountsAsEmpty` carry it to evac. The `currentSupply <= 0` (truly drained) branch is now separate and always clears the latch.

- **Behavior-aware self-restock.** `SupplyProvider` used to auto-`TryRestock` whenever it couldn't afford nearby demand, which for an Evacuate-stance truck fought the evac (drove it to the LC, or stuck it when no LC existed). New `ShouldSelfRestock()` returns false when the actor's `AutoTarget.ResupplyBehaviorValue == Evacuate`. TRUK is `InitialResupplyBehavior[AI]: Evacuate` (`vehicles.yaml`), so it now evacuates rather than shuttling. Only TRUK sets `RestockActors`, so LCs/caches are unaffected. A user switching a truck to Auto restores restock-when-low (Auto ⇒ `KeepServingBelowThreshold` false ⇒ reserve + `TryRestock`).

- **Red supply bar** while counts-as-empty-with-residue: `ISelectionBar.GetColor` returns red (`200,0,0`) when `residueUnusable`, else the normal amber. A truly drained truck (`currentSupply == 0`) keeps amber (residue latch is only set when supply > 0).

- **Bot economy layer had to stop fighting it.** `SupplyFollowerBotModule.IsLowOnSupply` (`SupplyFollowerBotModule.cs:237`) gated only on `CurrentSupply < RestockThreshold`; a residue *above* the threshold (e.g. 60 vs 50) but unusable stayed "eligible" and the bot kept issuing forward `Move` orders, re-parking it. Now also returns true on `sp.CountsAsEmpty`. This is truck behavior, so it applies to human- and bot-owned trucks alike; the bot module change is only to stop the bot re-tasking an evacuating truck.

- **Opportunistic hand-off falls out for free.** `SupplyProvider.Tick` still serves affordable in-range targets while the truck evacuates (it grants ammo without queuing movement for in-range units), so a passing soldier who *can* use the remainder still takes it — but the queued `RotateToEdge` is never cancelled, so the truck keeps evacuating and does not re-park. (Edge case: a Hunt-stance provider would `MoveTo` an out-of-range flagged unit and could interrupt evac; supply trucks are not Hunt-stance, so this does not arise in practice.)

- **Same-frame double-queue guard.** On the idle-transition tick, both `INotifyBecomingIdle.OnBecomingIdle` and `ITick.Tick` can reach `EvacuateOrRestock` and double-queue `RotateToEdge`. `DropsSupplyCache` records `lastEvacuateTick` (`self.World.WorldTick`) and no-ops a second call in the same frame.

Gated behind new `SupplyProviderInfo.EvacuateOnUnusableResidue` (default false; true only on TRUK) so the detection/latch/red-bar are scoped to trucks and never surprise an LC or cache.

## 2026-07-24 — Lobby Team column was parked, not deleted; non-editable rows were actively suppressed

The WW3MOD lobby (`engine/mods/common/chrome/lobby-players.yaml` — WW3MOD edits the shared common chrome directly, no mod override; wired via `mod.yaml:193`) had **no visible team column**, but the widgets were never removed:

- **Editable rows:** `TEAM_DROPDOWN` was parked off-screen (`X: -200, Width: 1, Height: 1`) in both `TEMPLATE_EDITABLE_PLAYER` and `TEMPLATE_NONEDITABLE_PLAYER`. The stock handler `LobbyUtils.SetupEditableTeamWidget` (`LobbyUtils.cs:721`) was **still being called** every sync (`LobbyLogic.cs:1088`) — it sets `IsVisible => true`, so simply giving the dropdown real on-screen X/Width/Height un-hides a fully functional control. No new logic needed.
- **Non-editable rows:** these were *actively* suppressed — `LobbyLogic.cs` (old lines 1118-1120) called `HideChildWidget(template, "TEAM_DROPDOWN"/"TEAM")` instead of wiring the read-only label. Fix: call `LobbyUtils.SetupTeamWidget(template, client)` (`LobbyUtils.cs:732`), which unhides `Label@TEAM` and hides the dropdown. Handicap stays hidden (V5 row genuinely drops it).
- **Layout math is forgiving:** the header `LABEL_CONTAINER` (child of `LEFT_COLUMN_PLAYERS` at `X:9, Width: PW-18`) and the row templates (child of the `LOBBY_PLAYERS` ScrollPanel, net `X:9, Width: PW-18`) share the **same reference width and absolute X origin**, so a column placed at `PARENT_WIDTH - 104` lines up between header and rows without fudge factors. Inserted a 52px-wide Team column between Name and the Ready checkbox; Name/`SLOT_OPTIONS`/`PLAYER_ACTION` elastic widths shrank by 64px to make room.

**Screenshot-driven team state without a click driver:** Mode 4 (`tools/autotest/screenshot-lobby.sh`) has no UI-driving verb, so to show *set* teams I seeded the skirmish restore file (`%APPDATA%/OpenRA/skirmish.ww3mod.yaml`). Its schema (`SkirmishLogic.cs:25-56`) persists per-slot `Team` for both the host (`Player:` block → the joining client) and each bot (`Bots:` map, keyed by bot type). `screenshot-lobby.sh` deliberately **moves this file aside** (so its seed map/`Test.LaunchLobbyMap` sticks and no "changed map" churn fires); launching `launch-game.sh` directly with the lobby Test args but leaving the file in place makes `SkirmishLogic.ClientJoined` replay it — restoring a full 2v2 with teams 1/1/2/2. `b2ac2865…` is the River Zeta WW3 uid.
## 2026-07-24 — Cohesion stabilization: the 1eb644de footprint cap only ever covered the Open box; treeline clicks scatter because the classifier tests centroid *offset*, not *shape*

Four root causes found while stabilizing `CohesionMoveModifier` (worktree `wt/cohesion-stab`):

- **The count-aware footprint cap (1eb644de) lives ONLY inside `ComputeBoxSlots`.** The three cover-aware intents lay slots differently: `SpreadInside` is naturally bounded by its `SpreadSearchRadius` window (±4 cells), but `EdgeLine`/`Approach`/`ComputeOpenLine` string a line whose width is `(n-1)*colSpacing` — **uncapped**. For Spread mode (3 cells/slot) a 12-unit line spans 33 cells. Since the river-zeta probe already showed "Open is rare" (most clicks on a cover-dense map classify EdgeLine/Approach), a *large* group order almost always hits the uncapped line path → "barely fits on screen". Fix: compute `lineColSpacing = min(colSpacing, maxWidth/(n-1))` once in `ModifyGroupOrder` and feed it to the line intents (`CohesionMoveModifier.cs` dispatch ~811). The box path keeps its own cap.
- **The classifier can't see a treeline.** `ClassifyIntent` chose EdgeLine vs SpreadInside on centroid *offset magnitude* only (`offsetMagSq >= EdgeOffsetThresholdCellsSq`). A click centred ON a treeline has density symmetric on both sides → offset ≈ 0 → falls to SpreadInside → scatter. The discriminator for "is this a line" is **anisotropy, not offset**: compute the density covariance's eigenvalues (`λ1/λ2` = major/cross spread) and route elongated distributions (`λ1 ≥ TreelineMinSpreadSq && λ1 ≥ TreelineAnisotropyRatio·λ2`) to EdgeLine, laid ALONG the major eigenvector, anchored at the cover centroid. A round blob has `λ1≈λ2` → stays SpreadInside (preserves the "click dense cluster → SpreadInside" behavior).
- **`LayCoverAwareLine` lays perpendicular to its `forward` arg.** To string units ALONG a detected treeline axis `a`, pass `forward = (a.y, -a.x)` (its perpendicular); the perpendicular of that is `a` again. Handy idiom for reusing the same helper for both "line across the gradient" and "line along the cover".
- **`PickCoverSlotNear` filtered by min-spacing BEFORE scoring**, so a cover cell one cell too close to a taken slot was discarded and the unit ejected into open ground to keep the line straight. Fix: track the best spacing-clean cell AND the best cover cell (relaxed spacing, only exact-overlap disqualifying); if the tidy pick has no cover but a cover cell is reachable, bend the line into cover. Min-spacing is a soft constraint; cover wins (behavior F / DP-3).

Determinism preserved throughout: the new nearest-slot assignment (`AssignSlot`) is a greedy global-minimum matching over `(actor,slot)` distance edges, tie-broken on actor index (ActorID-sorted) then slot index — no `LocalRandom`, and every per-subject call rebuilds the identical edge list from the identical ID-sorted actors + identical slot array, so all N calls reach the same matching. The double-precision covariance/eigen math uses only `Math.Sqrt/Round/Abs` — the same float ops the file's existing line/approach geometry already relies on (not a new determinism class). Verified: `make.ps1 all` clean, 321/321 NUnit pass, `test-cohesion-cover-redirect` passes (all 4 units adjacent to a trunk via the reworked treeline→EdgeLine→cover-bid path).
## 2026-07-24 — WW3MOD victory runs on `SupplyRouteContestation.DefeatTeam`, NOT stock conquest — and it forgot to award the win

The match-ending win/loss in WW3MOD is decided by the **custom** `SupplyRouteContestation` trait, not by `ConquestVictoryConditions` alone. When an SR's control bar empties, a defeat bar fills; at full it calls `OnDefeatBarFull` (`SupplyRouteContestation.cs:354`) → the player goes *passive* (production frozen), and only if the team has no other active SR does the elimination fire. This is the real replacement for RA's "destroy the conyard = defeat": the SR is `Armor: Indestructable` (`structures.yaml`), so it is never destroyed — it is *contested to zero*.

- **The elimination path only marked the LOSING team.** It failed the losing team's objectives and then *relied on stock `ConquestVictoryConditions.Tick` to infer the win* ("all my enemies are `WinState.Lost` → I win", `ConquestVictoryConditions.cs:78-82`). There was **no explicit win award**. This is the bug behind the 2v2 "we denied both enemy SRs but everyone shows Lost / mission failed" report: in a near-simultaneous mutual overrun the loser-resolution runs for *both* teams before the inference tick, so every player is marked `Lost`. The end screen (`GameInfoStatsLogic.cs:114`) just mirrors `WinState`, so it faithfully printed "failed" for all four.
- **Fix:** `DefeatTeam` → `ResolveTeamElimination`: it now also **completes the survivors' objectives** to award `Won` explicitly, guarded by a pure `ResolveEliminationOutcome(current, onEliminatedTeam)` that returns `null` for any already-decided player. That guard is the anti-"everyone loses" invariant — a second elimination event in the same tick becomes a no-op instead of flipping the winners to `Lost`. In a true simultaneous overrun, deterministic tick order alone picks the winner.
- **Gotcha for awarding a win via `MissionObjectives`:** `MarkCompleted` only fires `OnPlayerWon` when **all required objectives are Completed** (`MissionObjectives.cs:136-137`). A survivor still carries the incomplete `ConquestVictoryConditions` "primary" objective, so adding a fresh objective and completing *only that* would silently NOT win. `AwardVictory` clears every outstanding incomplete objective; the last completion triggers the win.
- **Testability:** the elimination decision is engine-coupled (needs `World`/`Player`/`MissionObjectives`), so the pure branches were extracted to `SupplyRouteContestation.ResolveEliminationOutcome` / `ShouldAwardVictory` and unit-tested (`SupplyRouteEliminationTest.cs`). The full team-propagation ending can't be exercised by a single autotest map (2v2 team-victory), so it is verified by unit test + code reasoning.
- **FFA/multi-team trap when awarding the win (adversarial-review catch):** "award Won to every combatant NOT allied with the eliminated owner" is correct for 2v2 but WRONG for 3-player FFA / 2v2v2 — eliminating one party would instantly win the game for all remaining mutually-hostile parties. The old code dodged this only because it deferred to `ConquestVictoryConditions.Tick` (`:78-82`), which requires ALL of a survivor's enemies to be Lost. The explicit award must reproduce that test **per survivor**: award Won only when every non-allied combatant is now Lost (`ShouldAwardVictory`), counting the just-marked losers. Do it in two phases — mark the eliminated team Lost first, then evaluate survivors — so the loser states are visible when deciding the win.
- **Don't force-complete scripted objectives.** `MarkCompleted` fires the win only when ALL required objectives are Completed (`MissionObjectives.cs:136`), so blanket-completing every Incomplete objective would auto-win a campaign mission (which runs `-ConquestVictoryConditions` + `MissionObjectives.EarlyGameOver: true`). Narrowed `AwardVictory` to (1) no-op unless the player actually has a `ConquestVictoryConditions` trait, and (2) complete only `Type == "Primary"` objectives (the type CVC creates), never the whole list.
- **`TestMode` symmetry:** the victory/defeat paths that the harness must own all early-return on `TestMode.IsActive` — `ConquestVictoryConditions.Tick` (`:63`), `MissionObjectives.CheckIfGameIsOver` (`:171`). `ResolveTeamElimination` needs the same guard or an SR contest emits stray victory lines/sounds mid-autotest. `TestMode` lives in `OpenRA.Game/TestMode.cs` (namespace `OpenRA`), so it's in scope from `OpenRA.Mods.Common.Traits` with no extra using.

## 2026-07-22 — Ambush-tactics research: suppression silences the AT gunner mid-ambush; prone ≠ concealment; humans DO get the influence stack

> **[promoted (partial): → architecture.md §Suppression system (suppression is not a blanket fire-halt; three armaments hard-pause via `PauseOnCondition: suppressed >= 10`; prone gives NO detection/concealment reduction) + new §Directional / rear armor (`DamageWarhead.ArmorDirectionPercent`, 5-elem `Armor.Distribution`)]** (curation 2026-07-22). Verified `infantry.yaml:1652/1865/2136` (PauseOnCondition on ATGM/repair/heal arms), `DamageWarhead.cs:121-131` (`ArmorDirectionPercent`, `Distribution.Length == 5`). **Not promoted:** the `InfluenceStack.Participates` (human combatants + @experimental bots; DangerFieldLayer value-blind) bullet is @experimental influence-stack subsystem detail, undocumented in reference — kept here.

While designing widened Ambush behavior (`WORKSPACE/plans/260722_ambush_undetected_design.md`), three non-obvious interactions surfaced that any ambush/AT-tuning work must respect:

- **Suppression silences the exact unit you'd use for a "let it pass, shoot the rear" ambush.** Suppression is otherwise NOT a fire-halt in this fork — normal rifles keep firing suppressed (only degraded). But three armaments carry `PauseOnCondition: suppressed >= 10`: the **AT Specialist ATGM** (`infantry.yaml:1652`), a repair arm (`:1865`), and an SF/demolition arm (`:2136`). So a "late spring" AT ambush that lets the target's escort return fire will get the AT gunner suppressed and **stop launching** before it exploits the rear arc. First-strike-from-concealment (pre-aimed alpha volley) avoids this; the late spring invites it.
- **Prone gives NO detection reduction.** `ProneCondition` (`infantry.yaml:252`) → damage modifier + smaller visual + `ProneSpeedModifier`, but there is no prone/stance detection modifier anywhere (confirmed absent; only weapon `MissChancePerDensity` exists). Visibility is purely "does the actor occupy a cell the enemy's `MapLayers` reveals" (`Detectable.IsVisibleInner`, `Detectable.cs:93-116`). The real "stay hidden" lever is **halting before you enter enemy vision**, not posture.
- **`InfluenceStack.Participates` includes human combatants** (`InfluenceStack.cs:38-48`: `player.IsBot ? BotType=="experimental" : player.Playable`). So `BeliefStore`/`DangerFieldLayer`/`ControlField` ARE computed for a human player — a human-facing behavior can read them. The narrowing is: among *bots*, only `@experimental`; and nothing keys on `RenderPlayer` (data is per-`Player`). But note `DangerFieldLayer` is threat-weighted → **value-blind**: it cannot see an undefended supply-truck convoy, so it's a poor sole trigger for a reinforcement-lane ambush.
- **Directional/rear armor is real and free.** `DamageWarhead.ArmorDirectionPercent` (`DamageWarhead.cs:121-198`) modifies effective armor by shot-vs-facing angle from a 5-elem `Distribution` (front,side,rear,top,bottom); heavy tank `100,50,25,10,10` = ~4× rear damage. Computed inside the normal damage pipeline, so rear shots need no special code — the bonus is automatic when geometry puts the shooter behind the target.

## 2026-07-22 — Asserting the derived-role table against the REAL ruleset: an `ILintRulesPass`, not NUnit; plus the latent `^CargoPips` predicate divergence

> **[rejected: @experimental `UnitRoleResolver` role-model internals + NUnit/`ILintRulesPass` test-and-lint methodology (AUTOTEST-recipe territory, not durable engine/gameplay reference). The `^CargoPips`/`MaxWeight > 0` predicate divergence is confirmed **latent** — no weight-0 `Cargo` actor exists on the current roster, so both predicates are byte-identical today — and is scoped entirely to that undocumented experimental subsystem. `CargoInfo.MaxWeight` defaults to 0 (`Cargo.cs:30`, verified) but is only load-bearing for that experimental predicate.]** (curation 2026-07-22).

Hardening the Phase-4 role model (`UnitRoleResolver`). Three reusable findings:

- **You cannot cheaply load the ww3mod ruleset inside NUnit.** `OpenRA.Test` references only
  `OpenRA.Game` + `OpenRA.Mods.Common`; it mounts NO mod content, and **no existing test builds a
  `ModData`** — a full ruleset load would need a from-scratch platform/filesystem/asset harness
  (fragile, no precedent). The idiomatic way to assert `ExtractFacts→Classify` over the *actual*
  loaded rules (with weapon references resolved) is an **`ILintRulesPass`**: `CheckYaml`
  auto-discovers every implementer via `ObjectCreator.GetTypesImplementing<ILintRulesPass>()` and
  runs `.Run(emitError, emitWarning, modData, rules)` against `modData.DefaultRules`
  (`UtilityCommands/CheckYaml.cs:68,155-168`), so it lands under `make test` (`--check-yaml`) for
  free. Implemented as `Lint/CheckUnitRoleTable.cs`; read thresholds off the world actor's
  `UnitRoleResolverInfo` so the lint tracks live config, not a copy of the defaults. **Positively
  confirm a new lint runs** (silence ≠ ran): a `DUMP_UNIT_ROLES=1` env gate prints the full table.
- **Pure-NUnit trait-level tests DON'T need a ModData:** `new ActorInfo(name, params TraitInfo[])`
  (`ActorInfo.cs:68`) builds an ActorInfo from hand-instantiated `TraitInfo`s. Set a readonly Info
  field (e.g. `CargoInfo.MaxWeight`, `AIUnitRoleInfo.Role`) via reflection `GetField(..).SetValue`.
  This is how the module-eligibility test fabricates a `bradley`-shaped carrier without a mod.
- **The `^CargoPips` predicate divergence is real but currently LATENT.** `^CargoPips`
  (`defaults.yaml:746`) grants a *bare* `Cargo:` (pip decoration only) and `CargoInfo.MaxWeight`
  defaults to **0** (`Cargo.cs:30`). So the module bridge `!HasTraitInfo<CargoInfo>()` and the
  resolver's `MaxWeight > 0` disagree for any weight-0-Cargo actor. **But a full-roster scan (and
  the `DUMP_UNIT_ROLES` table) shows every `^CargoPips` inheritor overrides `MaxWeight` — no actor
  has a weight-0 `Cargo` today** — so the two predicates are *equivalent on the current roster*;
  the divergence would only bite a future decorative-pip unit. Aligned both onto one source of
  truth `UnitRoleResolver.IsTroopCarrier(ActorInfo) => MaxWeight > 0` (resolver `ExtractFacts` +
  the three module bridges). Because there's no weight-0 carrier today, this is byte-identical even
  on the `UseUnitRoles=true` (@experimental) path — pure future-proofing. Supersedes the
  `!HasTraitInfo<CargoInfo>()` bridge described in the entry below.
- **SF/DR reserve-pool finding (F1):** `sf`/`dr` (and `.america`/`.russia` variants) are armed,
  derive **MainBattle** (no override; SF DMR 11c0, DR DroneTargeter 25c0 — both below the
  IndirectFire floor; DR's drone bay is `CarrierMaster`, NOT `CargoInfo`), and are absent from
  every old hard-coded module list. Role mode *would* task them onto the LayeredDefence line where
  the `MainLineUnitTypes` include-list never did. **Unreachable today**: both are
  `Prerequisites: ~disabled` and appear in NO AI `UnitsToBuild`/AdaptiveProduction list, so the
  @experimental reserve composition never fields them. Pinned in the NUnit table + lint so a future
  decision to field them is deliberate, not a silent MainBattle leak.

## 2026-07-22 — A net-new always-on World trait must NOT draw from SharedRandom in WorldLoaded

> **[promoted: → conventions.md §Engine behaviors that surprise (World.SharedRandom is the synced gameplay RNG — a net-new always-on trait must not draw from it at load/tick to self-stagger; derive per-instance offsets deterministically or it shifts the synced stream for control/benchmark games and breaks replay byte-identity)]** (curation 2026-07-22). Verified `World.cs:50` + `:217` (SharedRandom seeded from RandomSeed) and `:543` (`SharedRandom.Last` folded into the sync hash). The specific fix constants (BeliefStore/DangerFieldLayer/ControlField offsets) are @experimental influence-stack impl detail, kept here.

`World.SharedRandom` is the synced gameplay RNG; every draw advances one shared stream shared by
ALL profiles. An unconditional world trait (one that ticks/loads for @stable + control bots too,
not just @experimental) that calls `w.SharedRandom.Next(...)` in `IWorldLoaded.WorldLoaded` to
"stagger its first tick" therefore shifts the stream for control-bot games as well — silently
breaking replay/benchmark **byte-identity vs any earlier baseline that lacked the trait**, even
though the trait itself is behaviour-inert (nothing consumes its data).

- **Where it bit:** the influence stack's Stage-A/B merge added two such draws — `BeliefStore`
  and `DangerFieldLayer` each `SharedRandom.Next(0, UpdateInterval)` at load — a **2-draw shift**
  of the whole synced stream for @stable/controls vs the pre-A/B baseline. ControlField (Stage C)
  would have made it 3.
- **Fix:** replace all three with DISTINCT DETERMINISTIC offsets — `BeliefStore=0`,
  `DangerFieldLayer=UpdateInterval/3`, `ControlField=UpdateInterval/2+1`
  (`BeliefStore.cs` WorldLoaded, `DangerFieldLayer.cs` WorldLoaded, `ControlField.cs` WorldLoaded).
  The anti-collision stagger (grids not all recomputing on the same tick) is preserved; the synced
  stream is left completely untouched, so the whole influence stack now draws ZERO SharedRandom and
  restores byte-reproducibility (re-price 40800107 / ladder unnecessary).
- **Rule of thumb:** a net-new always-on trait must not draw from the synced stream. If you need a
  per-instance offset, derive it deterministically (a fixed constant, or a hash of a stable, synced
  identity) — never from `SharedRandom`. `SharedRandom` at load is only safe for a trait that is
  itself part of the baseline being measured, or one gated to a profile the benchmark re-baselines.

## 2026-07-22 — Heli overflight is a bot-order defect, NOT a FlyAttack bug (Stage 0 standoff)

> **[promoted (partial): → architecture.md §Aircraft movement system → new "Attack standoff" subsection (Hover aircraft hover-and-fire at max range; CanSlide aircraft zero velocity inside the weapon annulus; overflight is an `Attack`-vs-`AttackMove` order choice, not an engine attack bug)]** (curation 2026-07-22). Verified `FlyAttack.cs:183-198` (Hover falls through to the facing branches) + `Fly.cs:187-190` (CanSlide zeroes `CurrentVelocity` inside max range). The experimental fix (`HelicopterSquadBotModule.StandoffEngagement` / `BusyAttackMove` guard) + autotest-harness-limit note are impl detail, kept here.

The influence-stack §1.4 defect ("bots fly helis OVER enemies, shooting opportunistically on the
move instead of stopping at missile range") is often assumed to live in shared engine attack code.
It does not — the engine already stands off correctly:

- **`FlyAttack`'s Hover path already holds at max range.** WW3MOD attack helis are `AttackType: Hover`
  + `CanHover`/`CanSlide` (`aircraft-america.yaml:156,313`; `aircraft.yaml:144`). In `FlyAttack.Tick`
  (`Activities/Air/FlyAttack.cs:183-198`) a Hover heli is neither `Strafe` nor `Default && !CanHover`,
  so it falls through to the facing branches — it hovers and fires, it does not run past the target.
- **`Fly`'s CanSlide path physically stops at `maxRange`.** `Fly.Tick` for `CanSlide` aircraft
  (`Activities/Air/Fly.cs:184-191`) zeroes velocity and returns once inside `maxRange` (= the target's
  `GetMaximumRangeVersusTarget`, i.e. the longest valid armament — Hellfire 25c for the Apache). So a
  `MoveWithinRange`/`Attack` on a single actor already yields a per-target standoff.
- **The overflight is the FSM targeting a single, possibly distant, actor.** `HelicopterStates`
  approach/attack-run issued `Order("Attack", u, owner.TargetActor)` (`HelicopterStates.cs:310,409`) —
  a bare Attack on one chosen enemy (highest-cost in the weakest cell). The heli flies to *that*
  target's standoff, overflying nearer front-line enemies en route (they're inside 25c but aren't the
  locked target). Ground squads never had this: they use `Order("AttackMove", ...)`
  (`GroundStates.cs:161,174`) so AutoTarget engages the nearest in-range threat and only advances when
  clear.
- **Fix = give heli squads the same attack-move semantics, gated.** New default-off
  `HelicopterSquadBotModuleInfo.StandoffEngagement`; when on (experimental only) the FSM issues
  `AttackMove` toward the target cell instead of the bare Attack, reusing the shared, tested
  `AttackMoveActivity` → `FlyAttack` standoff. No engine attack code touched → @stable/controls/human
  byte-identical. **Guard needed:** `BusyAttack` only inspects the *top-level* activity, which under
  attack-move is `AttackMoveActivity` (child = FlyAttack), so it can't detect the nested engagement —
  re-issuing AttackMove each update tick would cancel an in-progress FlyAttack. Added `BusyAttackMove`
  (`CurrentActivity is AttackMoveActivity`) to suppress the re-issue.
- **Harness limit:** the bot FSM can't run in the single-human autotest harness, so
  `test-heli-standoff` characterizes the underlying contract the fix rides on (a Hover heli AttackMoved
  *past* a tank engages from standoff and never crosses the tank's line), not the FSM path itself.

## 2026-07-22 — Influence stack Stages A/B: per-player fog substrate facts

> **[promoted (partial): → architecture.md §Renamed/rewritten core systems (the fog-legal per-player predicate `player.MapLayers.IsVisible(cell, 1)` — `1` = currently-unfogged threshold — vs `IsExplored` for the weaker ever-seen state)]** (curation 2026-07-22). Verified `World.cs:110-115`. The `Shroud → MapLayers` rename itself was already in that table. **Not promoted (already covered / experimental):** `Air` ≠ `Helicopter` is already in conventions.md and the danger-channel application is @experimental; FrozenActorLayer static-vs-mobile persistence is already in architecture.md §Custom traits (SightingThreatLayer row); `ArmamentInfo.WeaponInfo` resolved at WorldLoaded is influence-stack impl detail.

Building the belief store + danger fields (`260722_influence_stack_design.md` §2A/§2B,
branch `stack-spine-ab`). Non-obvious substrate facts worth keeping:

- **The engine's `Shroud` is named `MapLayers` in this fork.** Per-player cell
  visibility is `player.MapLayers.IsVisible(cell, 1)` — the `1` is the visibility
  threshold meaning "currently unfogged" (`World.cs:110` `FogObscures` uses exactly
  `!RenderPlayer.MapLayers.IsVisible(p, 1)`; `MapLayers.cs:556/571`). `SightingThreatLayer`
  already gates on `player.MapLayers == null`. There is no `Shroud.cs` under Traits —
  `grep class Shroud` finds only `ShroudRenderer`. This is the verified-clear predicate
  for the belief store.
- **The anti-air DANGER discriminator is the OPPOSITE of the AD-role discriminator.**
  UnitRoleResolver's ShortRangeAD keys on the literal `"Air"` target type (dedicated SAMs
  only). But the danger stack's anti-air channel must key on **"can hit Helicopter"**
  (`ValidTargets.Contains("Helicopter") || Contains("Air")`) — because a ground MG that
  lists `Helicopter` genuinely threatens our helicopters even though it is not a SAM.
  "Danger = what can shoot me down" ≠ "AD role = what is a SAM". Same YAML fact
  (`Air` ≠ `Helicopter`, conventions.md), opposite query. Both live in the codebase now:
  `DangerKernelMath.WeaponThreatensAir` vs `UnitRoleResolver` `hasAirWeapon`.
- **FrozenActorLayer already splits static vs mobile persistence for free.** The engine
  keeps a frozen ghost for a structure under fog but (in practice) not for a roaming unit.
  The belief store leans on this: it refreshes STATIC contacts from
  `FrozenActorsInRegion(onlyVisible:true)` (no decay while the ghost shows) but deliberately
  ignores mobile ghosts, so mobile belief decays from its last live sighting per §2A. Clean
  per-class behaviour without a separate mobile-ghost tracker. (`FrozenActorLayer.cs:366`
  `FrozenActorsInRegion`; contact cell via `Map.CellContaining(fa.CenterPosition)` — a
  FrozenActor has `CenterPosition`/`ID`/`Info`/`Owner`/`Visible`, no `.Location`.)
- **`ArmamentInfo.WeaponInfo` is resolved by `WorldLoaded`** (populated at RulesetLoaded),
  so per-type kernel facts (range/throughput) can be cached once in `IWorldLoaded.WorldLoaded`
  exactly like UnitRoleResolver — `weapon.Range.Length` (WDist units), `weapon.Warheads`
  (`DamageWarhead.Damage` for absolute throughput), `weapon.Burst`, `weapon.ReloadDelay`.
  `BitSet<TargetableType>` is `IEnumerable<string>` (`BitSet.cs:78`), so "any non-air target"
  is a clean `foreach`, no hard-coded ground-type list needed.

## 2026-07-22 — Phase-4 role consumption: the `bradley/bmp2 = MainBattle` override collides with the un-migrated MountedTransport ferry pool

> **[rejected: in-flight @experimental role-migration state — the partial LayeredDefence/PoiOffensive/PoiGarrison → `UnitRoleResolver` migration and the `bradley`/`bmp2` = MainBattle vs MountedTransport name-based `CarrierTypes` collision are mutable code-state inside an undocumented experimental subsystem. The durable general lesson (two modules must not claim the same units; migrate off shared name-lists in one coordinated change) is already generalized in architecture.md §Adding a behavioural field to a shared trait + the commitment-ledger pattern.]** (curation 2026-07-22).

Migrating the `@experimental` front-line/offense modules (LayeredDefence, PoiOffensive,
PoiGarrison) to consume `UnitRoleResolver` (branch `phase4-roles`). One thing the recon
(`260722_phase4_recon.md`) and design (`260722_unit_role_resolver_DESIGN.md`) got wrong for
this partial migration:

- **The recon's "`ExcludedActorTypes` becomes `role != MainBattle`" (recon §1a) is unsafe
  as written, because the seeded overrides pin `bradley`/`bmp2` to `MainBattle`**
  (`vehicles-america.yaml:289`, `vehicles-russia.yaml:127`; design §4 rule 8 / §6.2). Those
  IFVs are *carriers* owned by `MountedTransportBotModule` and are deliberately excluded from
  the line today (`LayeredDefenceBotModule.cs:86-97` PITFALL: pulling a carrier forward makes
  it `!IsIdle` → it never qualifies as a transport candidate → ferrying dies). A pure
  `role == MainBattle` gate would re-admit `bradley`/`bmp2` to the line and offensive axes,
  regressing the ferry hand-off — a behaviour change, not the intended defect cure.
- **Root cause: the design's `bradley/bmp2 → MainBattle` decision is only safe once
  MountedTransport is *also* migrated off its name-based `CarrierTypes`.** Migrating the
  front-line consumers *without* migrating MountedTransport (which this task scopes out)
  leaves two modules claiming the same IFVs.
- **Bridge used:** each migrated consumer's role gate additionally excludes cargo carriers by
  trait — `role ∈ {MainBattle[,IndirectFire]} && !HasTraitInfo<CargoInfo>()`. `abrams`/`t90`/
  infantry have no `Cargo`, so only the IFV/APC hulls are held back; `m113` is `TransportLift`
  and already drops by role. This reproduces the exact old exclusion for the current roster
  while still curing the artillery(IndirectFire)/SHORAD(ShortRangeAD)/MANPADS-on-the-line
  defect. When MountedTransport is later migrated to `CarrierTypes = role == TransportLift`,
  the cargo-carrier guard on the front-line consumers can be dropped (or the IFV overrides
  revisited) as one coordinated change.
- **Also confirmed behaviour-preserving:** `mt` (mortar) and `medi` leave the LayeredDefence
  line under role mode — `mt → IndirectFire` (correct, light indirect fire; design §8.2),
  `medi → Logistics` (design §8.1, accepted). Every screen/line infantry (`e3/ar/at/sn/tl/e2/
  e4`) stays `MainBattle`: `at`'s ATGM is `20c0/MinRange 3c0` (below the 4c0 IndirectFire
  floor), `sn`'s sniper is `20c0` at Mobile.Speed 25 (below the Recon 110 floor), `tl`/`e2`
  grenade launchers are `12c0/MinRange 1c512`.

## 2026-07-22 — Unit-role resolver: two YAML facts that make/break the taxonomy, + audit errata

> **[promoted (partial): → conventions.md §Weapon `ValidTargets`: `Air` ≠ `Helicopter` (the AD-discriminator fact) + §Faction-specific files (three-tier `^Template`/bare-concrete/`X.america` naming, override the template to hit all variants)]** (curation 2026-07-22). Weapon claims verified: `^7.62mm`:144, `^12.7mm`:215, `30mm.Tunguska.AA`:455, MANPAD:339/Stinger:372/`9M311 Inherits: Stinger`:411-412, tunguska `Weapon: 9M311`:860. **Citation drift fixed:** the ballistics/missile line numbers had all shifted ~2-3 lines; and the concrete actors are **uppercase** (`E6`/`MT`/`AA`) with faction variants (`AR.america`) in `infantry-america.yaml`, NOT lowercase in `infantry.yaml` as the entry stated. **Rejected bullets:** the raw `^ArtilleryRound` 40c0/10c0 vs `^TankRound` 24c0/1c512 range *values* (volatile balance data + role-resolver-threshold-scoped, though verified at :609-614/:574-578); and the `msta`/`avenger` audit erratum (plan-doc-scoped).

Implementing the Phase-3 role resolver (`260722_unit_role_resolver_DESIGN.md`). The
audit's finding B3 (Cargo shadowing) was correct; verifying every classification against
the real YAML surfaced two non-obvious substrate facts the whole taxonomy hinges on, plus
a couple of errata in upstream docs.

- **Weapon `ValidTargets` splits `Air` from `Helicopter` — and ground MGs list only
  `Helicopter`.** `^7.62mm` and `^12.7mm` (humvee/btr/m113 guns) are `ValidTargets:
  Infantry, Unarmored, Helicopter[, Light]` (`weapons-ballistics.yaml:143,215`) — they can
  shoot helis but are NOT air-defence. Only Stinger/Stinger.quad/9M311/MANPAD list `Air`
  (`weapons-missiles.yaml:339,372`; 9M311 inherits Stinger). Even tunguska's dedicated AA
  autocannon `30mm.Tunguska.AA` is `ValidTargets: Helicopter` (`weapons-ballistics.yaml:455`)
  — tunguska lands ShortRangeAD only via its **9M311** missile (`vehicles-russia.yaml:856`).
  So the ShortRangeAD discriminator MUST key on the literal `"Air"` target type, never
  `Helicopter`; keying on Helicopter would drag every MG-armed vehicle into AD. Under-matching
  on `Air` is the safe direction.
- **`^ArtilleryRound` is `Range 40c0 / MinRange 10c0`; `^TankRound` is `24c0 / MinRange 1c512`**
  (`weapons-ballistics.yaml:613-614` vs `:577-578`). Paladin/Giatsint inherit `^ArtilleryRound`
  unchanged (`:638-646`), so an IndirectFire threshold of `MinRange ≥ 4c0` (or `Range ≥ 35c0`)
  cleanly separates all tube/rocket arty from direct-fire tanks. Mortar `60mm_Mortar` is
  `25c0 / MinRange 8c0` (`:522-527`) so `mt` derives IndirectFire, not MainBattle. (A prior
  research pass mis-read arty as `24c0/1c512` by confusing it with `^TankRound` — the design
  doc's original `40c0/10c0` values were right.)
- **Concrete buildable units are faction-suffixed** (`e6.america`, `tecn.america`,
  `mt.america`, `aa.america`, …) via `Inherits: ^E6` etc., with a bare lowercase concrete
  also present (`e6`, `tecn`, `mt`, `aa`, `truk` — `infantry.yaml:1911/2197/1554/1767`,
  `vehicles.yaml:509`). An `AIUnitRole` override on the `^E6` template annotates every
  variant in one line (used for `e6: Logistics`); single-hull overrides (`bradley`, `bmp2`)
  go on the concrete actor.
- **Audit erratum:** `260722_phase3_redteam.md:166` claims the design references phantom units
  `msta`/`avenger`. It does not — grep of the design doc finds neither; the only Russian
  gun-arty is `giatsint` (`vehicles-russia.yaml:382`), which the design already names. Nothing
  to remove; recorded so the claim isn't chased again.

## 2026-07-22 — Autotest map-rules: override a `^Template`, not a concrete `ar.america` actor key, to add a trait

> **[promoted (partial): → conventions.md §Faction-specific files (override a `^Template`/bare hull key from map rules; faction-suffixed concrete keys throw a duplicate-key load error)]** (curation 2026-07-22). Positive pattern verified in-repo: `demo-wgm-suite/rules.yaml` overrides `^Combatant` + bare hull keys `t90`/`bmp2`/`bradley`/`abrams` cleanly. The duplicate-key error is empirical (no code:line; not re-run this pass — game launch is forbidden here). **NOT promoted to reference:** the silent-fallback-to-menu / diagnose-via-debug.log methodology is DOCS/recipes/AUTOTEST material; and the "`run-test.sh` has no kill-timeout" claim is now **STALE** — a wall-clock kill-timeout watchdog landed in 2fa70d11.

Adding `ar.america:\n\tExternalCondition@testdeploy:` to a test scenario's `rules.yaml` threw at load: `LoadFromManifest<Rules>, duplicate values found for the following keys: ar.america: [ActorInfo,ActorInfo]`. When map rules fail to load, the game **silently falls back to the main menu and idles forever** — no `Test.Pass/Fail`, no `result.json`, and `run-test.sh` has no kill-timeout, so the window sits on screen (diagnose via `AppData/Roaming/OpenRA/Logs/debug.log`). Overriding a concrete faction infantry key (`ar.america`) duplicates under this mod's map-rules merge, yet overriding a `^Template` (`^Combatant`, `t90`, `bradley`) merges fine (proven: `demo-wgm-suite/rules.yaml`). **Workaround:** declare the added trait on `^Combatant` (or the relevant `^Template`) rather than the concrete actor key. Always run tests with a wall-clock kill-timeout and verify the game process exited afterward.

## 2026-07-22 — B1 stale-anchor has TWO walk-back vectors; the executor anchor fix alone is insufficient because CohesionSlotMemory drags first

> **[rejected: plan/executor-implementation-scoped — internals of the experimental `StancePositioningExecutor` (not documented in DOCS/reference at all), tied to the Phase-3 B1 plan + the e2208d42 merge review. The CORRECTION documents a KNOWN GAP already filed in `WORKSPACE/bugs/discovered.md` (Adjusting-window drag), which is where it belongs. The one general nugget — trait declaration order in `^Combatant` sets tick precedence, and a return-to-slot declared before the executor wins via the executor's `CurrentActivity != null` no-op guard — is specific to that experimental trait pair. Autotest timing (infantry `Speed:25`≈41 ticks/cell, use generous deadlines + `--speed 8`) is AUTOTEST-recipe + tuning-data-scoped.]** (curation 2026-07-22).

Found implementing Phase-3 B1 (anchor lifecycle) in `StancePositioningExecutor.cs`. The red-team B1 finding (`260722_phase3_redteam.md`) describes ONE walk-back vector — the executor re-anchoring on a stale anchor and issuing its own Move toward the abandoned position. Fixing that (anchor invalidation + `ReleaseManagement` clears `anchor`/`hasAnchor`) is necessary but **not sufficient**, and the B1 autotest exposed the second vector:

- **`CohesionSlotMemory` (`CohesionSlotMemory.cs:84-113`) independently drags an executor-managed unit back to the executor-assigned cover cell.** The executor assigns the slot in `CommitManagement` (`slotMemory?.Assign(dest, tick)`) so return-to-slot *reinforces* its cover choice (the B2 "slot ownership" behavior). After a player relocates the unit outside the leash, that slot is stale, and `CohesionSlotMemory.TickIdle → TryReturnToSlot` queues a `Move` back to it (within `ForgetAfterTicks`=750).
- **Tick order defeats a reactive clear.** `CohesionSlotMemory` is declared **before** the executor in `^Combatant` (`defaults.yaml`) by design (so return-to-slot wins over the executor via the executor's `CurrentActivity != null` no-op guard, S6). So on the unit's first idle at the new position, CohesionSlotMemory queues the return-Move *before* the executor's idle-time invalidation runs — the executor then sees `CurrentActivity != null` and bails, never clearing the slot. The unit is dragged back and re-anchors at the old spot. `ReleaseManagement()` clearing the slot is too late.
- **Fix: detect the relocation in `ITick`, not `TickIdle`.** An `ITick` handler on the executor (guarded `IsTraitDisabled || State == Adjusting`) clears the slot/anchor the moment the unit crosses the leash **mid-move**, before it next idles — so CohesionSlotMemory finds no slot on arrival. `ITick` for a disabled trait is a guarded no-op (no RNG, no trait reorder) ⇒ `@stable` byte-identical; only executor-gated (`@experimental` + human) units re-price. This is the executor's own slot assignment, so the fix stays inside the executor's scope — no change to shared `CohesionSlotMemory` (which would re-baseline `@stable`).
- **[CORRECTION, merge review of e2208d42]: the ITick fix does NOT fully close vector 2.** The `State == Adjusting` guard means a player redirect issued *while the executor's own adjustment move is in flight* is not caught: on the unit's next idle, CohesionSlotMemory (declared first) queues return-to-slot before the executor's `ResolveArrivalOrAbort` can run, dragging the unit back to the old cover cell ONCE (redirects ~5–14 cells inside the adjust window; longer trips let the slot go stale at `ForgetAfterTicks`=750). Bounded and self-healing — the next redirect is caught by ITick — but the invariant "anchor never older than the unit's last non-executor move" does not hold in that window. Filed as a known gap in `WORKSPACE/bugs/discovered.md`; a full fix needs an Adjusting-aware leash margin to avoid false-aborting the executor's own pathing excursions.
- **Autotest timing trap that masked this:** infantry `Speed: 25` (`infantry.yaml:37`) is ~41 ticks/cell, so a 27-cell scripted relocation takes ~1100+ ticks; a 550-tick relocate deadline fails "never reached B" with the unit still *en route* (before the drag even starts), hiding the real vector. Give executor-relocation autotests generous tick deadlines and run `--speed 8`.

## 2026-07-22 — Phase 2 positioning executor: condition-lint, per-unit bot gating, MiniYaml removal, and stance determinism gotchas

> **[promoted: → conventions.md (conditions consumed/granted lint, `-Key` full-key removal, `WVec.FromSpeedAndAngle`, UnitDefaultsManager stance-overwrite surprise) + architecture.md §AI configuration / conventions §Conditions (GrantConditionOnBotOwner actor-scoping)]** (curation 2026-07-22). All five substrate claims verified at the cited lines.

Implemented Phase 2 of `WORKSPACE/plans/260722_strategic_tactical_split_SPEC.md` (the `StancePositioningExecutor` unit trait). Five non-obvious substrate facts, each of which shaped the implementation or cost a debugging cycle:

- **`ConditionalTraitInfo.RequiresCondition` is `[ConsumedConditionReference]`** (`engine/OpenRA.Mods.Common/Traits/Conditions/ConditionalTrait.cs:21`). So a unit trait gated `enable-tactical-positioning || enable-ai-experimental` marks BOTH names *consumed on that actor*, and `CheckConditions` (`Lint/CheckConditions.cs:73-75`) emits an **ERROR** (fails `make test`), not a warning, for a consumed condition nothing grants. Phase 2 grants `enable-tactical-positioning` from nothing (humans get it in Phase 3), so it had to be declared grantable without firing: an **`ExternalCondition` whose `Condition` field is `[GrantedConditionReference]`** (`Conditions/ExternalCondition.cs:28`) satisfies the lint at zero runtime cost and doubles as the Phase-3 human-activation seam (a Lua/warhead `GrantCondition("enable-tactical-positioning")` resolves to exactly that ExternalCondition).
- **`GrantConditionOnBotOwner` grants on whatever actor it sits on, checking `self.Owner.IsBot && Bots.Contains(BotType)`** (`Conditions/GrantConditionOnBotOwner.cs:46`). The `enable-ai-experimental` grants in `ai.yaml` are on the **Player** actor; a **unit** trait's `RequiresCondition` only sees conditions granted on the *unit*, so per-unit gating needs its own per-unit `GrantConditionOnBotOwner@…: Bots: experimental` on the unit template — the player-level grant does nothing for a unit trait.
- **MiniYaml `-TraitName:` removal matches the FULL node key including `@label`.** `-SquadManagerBotModule:` throws `There are no elements with key 'SquadManagerBotModule' to remove` (`MiniYaml.cs:483`) when the trait is declared `SquadManagerBotModule@experimental.america.fixedwing`. There is no "remove all instances of this type" form — you must list each labeled key. (Hung a test run on a load-error dialog.) This is why the Phase-2 autotest isolates the executor with a **human owner + granted condition** instead of stripping the experimental bot's many `@`-labeled ground modules.
- **Human-owned units get their engagement stance overwritten by `UnitDefaultsManager`** (`AutoTarget.cs:355-388`, applied in `Created` for `Owner.Playable && !Owner.IsBot`) from **per-machine persisted** per-type defaults — so a deterministic human-owned stance test must strip `UnitDefaultsManager` (a bare-key World trait, `world.yaml:269`) or a locally-saved AR default (Hunt/HoldPosition) silently changes behavior. Bot-owned units skip this branch (they read `InitialEngagementStanceAI`).
- **`WVec.FromSpeedAndAngle(speed, angle)` is the exact inverse of `WVec.Yaw`** (`WVec.cs:66,94`). To convert a bearing `WAngle` (itself built from a cell-space `WVec(dx,dy)`) back into a cell-space step, use `FromSpeedAndAngle` and take `Sign(X)/Sign(Y)` — this respects OpenRA's "north = −Y" + counterclockwise convention automatically, avoiding hand-rolled `Cos()/Sin()` sign errors.

## 2026-07-22 — The full OpenRA observer/spectator suite is present AND wired into ww3mod (not stripped); the only real presentation gap is an auto-director camera

> **[rejected: read-only observer/spectator inventory tied to WORKSPACE/plans/260722_watchability_research.md — a snapshot of existing engine features for a plan, not durable engineering reference; the durable gap (no auto-director camera) belongs in that plan]** (curation 2026-07-22).

Found doing read-only watchability research (`WORKSPACE/plans/260722_watchability_research.md`). Relevant to any spectator/broadcast/replay-analysis work — most of the substrate already ships.
- **Observer chrome is loaded by the mod:** `mods/ww3mod/mod.yaml:173` lists `ww3mod|chrome/ingame-observer.yaml`; observer hotkeys at `mod.yaml:249` (`common|hotkeys/observer.yaml`). `LoadIngamePlayerOrObserverUILogic.cs:30-31` auto-loads `OBSERVER_WIDGETS` when `world.LocalPlayer == null`.
- **8 stat panels exist** (`ObserverStatsLogic.cs` `ObserverStatsPanel` enum: Basic/Economy/Production/SupportPowers/Combat/Army/Graph/ArmyGraph), including two live line graphs: **income-over-time** (`ingame-observer.yaml:1055`) and **army-value-over-time** (`:1081`, `YAxisLabel: Army Value` `:1091`). Basic panel has APM; Combat panel has assets destroyed/lost, army value, vision %.
- **Shroud selector includes "Disable Shroud"** (reveal-both-sides fog for casting) plus All-Players and per-player views (`ObserverShroudSelectorLogic.cs`).
- **Replays auto-record** to `.orarep` on every match (`Game.cs` `recordReplay=true` default → `ReplayRecorder`); playback has pause + 4 speed tiers (`ReplayControlBarLogic.cs`). The autotest harness records these by default — bot matches are already on disk as replays.
- **Camera primitives for a director exist:** `MiniMapPings.LastPingPosition`, a jump-to-last-event centering hotkey (`JumpToLastEventHotkeyLogic.cs`), Lua `Camera.Position` (`CameraGlobal.cs`). **MISSING (confirmed gap):** any auto-follow / combat / "storyline" camera — no code cuts the viewport to live action. That, not the panels, is the presentation keystone for bot-vs-bot spectating.

## 2026-07-22 — No fires/artillery bot logic exists; helis fight uncoordinated; the ratified stance-mapping is doctrinally inverted (doctrine-realism audit)

> **[rejected: doctrine-realism design audit tied to WORKSPACE/plans/260722_doctrine_realism_audit.md + the unratified 260722 SPEC — a critique / gap-list of proposed design, not a shipped mechanic; belongs in the audit + SPEC docs]** (curation 2026-07-22).

Found doing the read-only doctrine-realism audit (`WORKSPACE/plans/260722_doctrine_realism_audit.md`).
- **There is NO artillery/indirect-fire bot module.** A repo-wide grep for `artillery|indirect|counter-battery|bombard` across `engine/OpenRA.Mods.Common/Traits/BotModules` matches only `LayeredDefenceBotModule.cs`, and only as a `MainLineUnitTypes` *unit-type string* (`:54-59`) — arty is parked at a standoff and left static. No displacement/shoot-and-scoot, no counter-battery, no pre-registered fires anywhere in the bot code. Fires are the modern casualty center-of-gravity; their total absence from the ratified 260722 SPEC (six phases, zero fires behavior) is the loudest realism gap.
- **`HelicopterSquadBotModule` runs as an independent module** with no synchronization to the ground offense axes (`PoiOffensiveBotModule`) — no air-ground task org, no CAS-on-call for a stalled axis. Helis look active but fight their own war.
- **The ratified L3 stance→cover mapping is doctrinally inverted (design bug, not code yet).** `SPEC §4` maps Defensive to "back side of the trees … hold and return fire" — but the far side of a treeline blocks LOS (`ShadowLayer`/`BlocksSight`), so the unit literally cannot return fire. It conflates "hull-down" (mask profile, still shoot) with "hide behind cover" (no shot). Shipped default-ON in Phase 3, this would make every defender stand backs-to-the-fight. Fix is one inverted edge lookup against the planned `3b` cover-edge-orientation layer (face the *threat-facing* edge, as Hunt does, minus the creep). Same error taints the SPEC's "Ambush + cover-back" aside. Details + two more critical-pass items (aggressive "push through contact" = the RTS-blob trope; Phase-4 fog migration creates a blind window if recon is deferred) in the audit doc §2.
## 2026-07-22 — Phase 1: the two new map layers + hold-Space intel overlay (strategic/tactical split)

> **[promoted: → architecture.md §Custom traits (SightingThreatLayer / TerrainAffordanceLayer / SightingIntelOverlay rows) + §RenderPlayer is render-side only (autotest `?? LocalPlayer` fallback); conventions.md (Rectangular map grid)]** (curation 2026-07-22). Three trait files confirmed present; `mod.yaml:319 Type: Rectangular` verified.

Implemented Phase 1 of `WORKSPACE/plans/260722_strategic_tactical_split_SPEC.md` (§3a + §3b + §3d). Pure data + render — NOTHING consumes the layers for behavior yet; the overlay IS the verification tool. All three are World traits wired in `mods/ww3mod/rules/world.yaml` (nested under `World:`, after `PoiMap`).
- **§3a `SightingThreatLayer` (SIM, per-player, fog-respecting)** — `engine/OpenRA.Mods.Common/Traits/World/SightingThreatLayer.cs`. World trait holding `Dictionary<Player, CellLayer<SightingCell>>` (the InfluenceMap container shape, but fog-correct instead of omniscient). Decaying-memory field: each recompute (staggered, `UpdateInterval` 25t) multiplies the field by `DecayPercent` (75) and re-injects fresh sightings, so recent contact dominates. Two channels: **EnemyIntensity + a summed direction vector** (surfaced as `ThreatIntensity`/`ThreatDirection`), and **FriendlyIntensity** (own + visible-allied units, for §3d's BoP wash). Sources are strictly per-player-legal: enemies via `Actor.CanBeViewedByPlayer(p)` (currently visible) + `player.FrozenActorLayer.FrozenActorsInRegion(map.AllCells, onlyVisible:true)` (fog-frozen last-seen). `ThreatDirection` = `new WVec(dirX,dirY,0).Yaw` — deterministic integer bearing via `WAngle.ArcTan`, consistent with engine movement facings.
  - **`FrozenActorsInRegion(region, onlyVisible:true)` returns exactly the fog-frozen stale copies** (`FrozenActor.Visible==true` means "the frozen render is shown because the real actor is under fog"). That is precisely the "was seen here, now hidden" set — so no engine change was needed to enumerate a player's last-seen enemies; `map.AllCells` is a `CellRegion` and satisfies the region query.
  - **Perf: decay walks only an active-cell list** (`List<CPos>` + `HashSet<CPos>` membership rebuilt each cycle), never a full-map scan. Additive accumulation ⇒ actor/frozen iteration order does not affect the result (determinism-safe without sorting).
- **§3b `TerrainAffordanceLayer` (STATIC, player-agnostic)** — `.../TerrainAffordanceLayer.cs`. Computed once at `WorldLoaded` from `Map.DensityLayer` (the same cover substrate `CohesionMoveModifier.CoverScore` reads). Per passable cell: `CoverQuality` (summed 8-neighbour density) + edge classification. **Outward normal = negated density gradient**: `grad = Σ(offset·neighbourDensity)` points into the dense mass; `(-gradX,-gradY).Yaw` points out toward open ground. Interior cells have ~0 gradient (not edges); boundary cells have a strong one. Makes the Phase-2 treeline case a lookup: "edge cell facing threat direction" = an edge cell whose `OutwardFacing` matches. Static + identical on every client ⇒ no sync concern.
- **§3d `SightingIntelOverlay` (RENDER-ONLY)** — `.../SightingIntelOverlay.cs`. `IRenderAnnotations` on the World actor, gated on **`wr.ShowAllOrders`** (the existing hold-Space that already draws waypoint lines — `ViewportControllerWidget.cs:433` sets it, `hotkeys.yaml:41` binds `ShowAllOrders: Space`). Reads `world.RenderPlayer` (legal render-side) and only that player's §3a layer + own frozen actors, so it leaks nothing through fog. Balance-of-power wash via `MarkerTileRenderable(cell, Color.FromArgb(alpha, rgb))` over the viewer's active cells: green `friendly−enemy > GrayzoneThreshold`, red `< −threshold`, else **computed gray** (no stored third channel). GPS dots reuse the in-repo satellite substrate — `new Animation(world, "gpsdot")` + `RenderUI` (mirrors `GpsDotEffect`), one dot per enemy fog-frozen actor from `ScreenMap.RenderableFrozenActorsInBox`. Dev always-on switch = `/intel` chat command (`StartAlwaysOn` Info flag defaults false ⇒ ships hold-Space).
- **WW3MOD map grid is `Rectangular`** (`mod.yaml:319`), not RectangularIsometric — so CPos `(x±1, y±1)` neighbour stepping and a Manhattan-disc spread map directly to spatial cell adjacency, and `CellLayer.Contains(CPos)` has no `X<Y` rejection. This is what makes the density-neighbour idiom (already used by `CohesionMoveModifier`) correct for the new layers.
- **VERIFY GOTCHA — a World-actor render overlay must fall back to `LocalPlayer`; `world.RenderPlayer` is null in the autotest harness.** The overlay first shipped reading `world.RenderPlayer` only (matching `GpsDotEffect`). In a `Launch.Map` autotest, `RenderAnnotations` IS called on the World-actor trait (the `!actor.IsInWorld` filter at `WorldRenderer.cs:247` passes — the world actor is created `addToWorld:true`), but `world.RenderPlayer` is **null**, so the overlay drew nothing while the §3a data layer was fully populated (numeric probe: threat 549 / friendly 549 / dir 768=east). Fix: `var viewer = world.RenderPlayer ?? world.LocalPlayer;` — the same local-client identity `FrontlineOverlay` reads. Still render-side/per-player-legal (reads only the viewer's own layer). Diagnosis needed a per-call log that MATERIALISES the renderable list and prints counts — a `% 60`-throttled log never fired because background test runs render far fewer than 60 frames.
- **Overlay verification (`tools/autotest/scenarios/demo-intel-overlay`, fog on):** 01-gated-off (dev switch off, Space not held) → units/terrain visible, zero overlay; 02 after `/intel` → green wash over own Abrams, red over the sighted T-90 line, computed gray band between, GPS-dot diamonds on the frozen enemy GTWRs under fog. Enemy **mobile** units leave NO frozen actor (only structures carry `FrozenUnderFog` — `structures*.yaml`, `SUPPLYROUTE`), so §3d dots are for frozen *structures*; a mobile enemy's last position survives only as decaying §3a intensity. Added test-only Lua bindings `Test.RunChatCommand`, `Test.GetThreatIntensity/GetFriendlyIntensity/GetThreatDirection` (all `TestMode.IsActive`-gated).

## 2026-07-22 — Phase 0 fix: bounded the cohesion box footprint (count-aware cap) + regroup now emerges from the existing leash

> **[promoted: → architecture.md §Custom traits (CohesionMoveModifier bounded-footprint clause)]** (curation 2026-07-22). Cap constants verified at `CohesionMoveModifier.cs:54-73`, applied `:179-188`. Run-specific verification numbers kept here.

Implemented the ratified Phase-0 global cohesion fix (`WORKSPACE/plans/260722_strategic_tactical_split_SPEC.md`). Ships to everyone (humans + all bot profiles) — a declared re-baseline event; the ladder re-baseline is a manager follow-up, not part of this task.
- **Root fix:** `CohesionMoveModifier.ComputeBoxSlots` now caps the box's total width/depth per mode. After `cols`/`rows` are known, if `(cols-1)*colSpacing > MaxWidth` the effective `colSpacing` is shrunk to `MaxWidth/(cols-1)` (same for depth via `MaxDepth/rowSpacing`), floored at `MinSlotSpacing` so slots stay on distinct cells. The span between outermost slot centers is now bounded regardless of unit count — previously it grew linearly with count, bounded only by `map.Clamp`.
- **Chosen constants (WDist / cells):** MaxWidth Tight 8192 (8c) / Loose 11264 (11c) / Spread 13312 (13c); MaxDepth Tight 5120 (5c) / Loose 6144 (6c) / Spread 7168 (7c); MinSlotSpacing 1024 (1c). **Mode ordering Tight < Loose < Spread is provably preserved for every n**: effective spacing = `min(baseSpacing, MaxExtent/(cols-1))`; both `baseSpacing` and `MaxExtent` are monotonic across modes and `cols` depends only on n, so the elementwise `min` stays monotonic. Realistic groups (≤~60) never hit the 1024 floor for Spread, so no slot collisions.
- **Regroup-on-arrival needed NO new code** — the bound repurposes the existing `CohesionSlotMemory` sticky-slot leash (`INotifyIdle`/`INotifyBlockingMove`, `defaults.yaml:20`). The survey noted the leash "reinforces spread" — that was true only because slots were unbounded; with bounded slots the same return-to-slot behavior now closes stragglers into a compact box. Deterministic (assigned slot + WorldTick, ActorID-stable), no LocalRandom.
- **Verification (`test-cohesion-extent-cap`, 24-unit Spread grouped Move to an open cell):** RED on old code = assigned slots span **19.6 cells** > 17 cap; GREEN after fix = slots **14.4**, arrival positions **12.2**. Added `Test.SetCohesion(actor, mode)` Lua binding so the test forces Spread deterministically. Only the Open box path was bounded — the other three intent paths (SpreadInside/EdgeLine/Approach) anchor to cover cells inside fixed search radii and are already local.

## 2026-07-22 — Auto-spread is ALWAYS-ON (not stance-gated) and has an unbounded footprint with no regroup (stance/tactical survey)

> **[rejected: superseded survey — the unbounded-box / no-regroup finding was fixed by the Phase-0 cap (see the cohesion-cap entry above, promoted); the durable mechanic "CohesionMode selects spacing only; fires on every grouped move for humans + bots" is already in architecture.md §Custom traits (CohesionMoveModifier). Incidental stale-log / ResupplyBehavior-Desc bugs → bugs/discovered.md]** (curation 2026-07-22).

Found doing the read-only stance/tactical-layer inventory (`WORKSPACE/plans/260722_stance_tactical_survey.md`), tracing the user's "units spread out way too much" report.
- **`CohesionMoveModifier` fires on EVERY grouped `Move`/`AttackMove` with n>1, regardless of CohesionMode** (`CohesionMoveModifier.cs:588-590,600-601`). CohesionMode (Tight/Loose/Spread) only selects **spacing**, never whether the reshaping runs (`:626-627`). So "auto-spread" is live for humans AND bots on every group move, not just when Spread is selected. This is separate from the `@experimental`-only `CohesionSwitchEnabled` AI doctrine (which was benchmark-negative, ~−$1,500) — the MOVE-reshaping is unconditional and shipped to everyone.
- **Over-spread root cause = unbounded box.** In the `Open` intent (typical open-terrain/AI case), `ComputeBoxSlots` offsets scale linearly with spacing and unit count: `perpOffset = (2*col-(unitsInRow-1))*colSpacing/2` (`:279`), `depthOffset = -row*rowSpacing` (`:283`). The **only** bound is `map.Clamp` (`:294`) — no maximum-footprint clamp. Spread spacing is 3072/2560 WDist (3 / 2.5 cells) vs Tight 1024/1024 (`:29-44`), so a Spread box is ~3× Tight and grows with N.
- **No regroup / mass-to-assault for human units.** The modifier has no exit-spread path. The only mass-to-assault (`ApproachCohesion=Spread`→`AssaultCohesion=Tight` inside `AssaultRadiusCells=15`) lives in `PoiOffensiveBotModule.cs:99-106` and is gated behind default-false `CohesionSwitchEnabled` (`:87,94`), `@experimental` only. `CohesionSlotMemory` only leashes nudged units *back* to their spread slot — reinforces spread. Human units never auto-regroup.
- **A stale `[Cohesion]` debug log is still active** (`CohesionMoveModifier.cs:679-695`, "Strip again once we have an answer") and the `ResupplyBehavior` Desc strings say "Hold, Seek and Rotate" while the enum is Hold/Auto/Evacuate (`AutoTarget.cs:125,129` vs `:26`).

## 2026-07-22 — Cover cells and last-seen-enemy memory both exist but are NOT joined; both AI influence grids are omniscient (stance/tactical survey)

> **[rejected: substrate-inventory survey largely superseded by the Phase-1 SightingThreatLayer (which joins cover + fog-correct last-seen memory, now in architecture.md §Custom traits). The omniscient-InfluenceMap contrast is captured in that row; LocalRandom-not-in-sync-hash is already in architecture.md §Bot decisions ARE seed-reproducible]** (curation 2026-07-22).

Substrate inventory for a future tactical-positioning layer (same survey doc).
- **`Map.DensityLayer` (`Map.cs:252`) makes cover cells queryable today.** `CohesionMoveModifier.CoverScore()` (`:156`) already reads it to find passable cells adjacent to dense actors (trees carry `Density:`, `decoration.yaml:104+`). LOS cover is real: `ShadowLayer` (`Map.cs:253`), `BlocksSight.cs`, weapon `MissChancePerDensity`. **MISSING:** any prone/stance-in-cover damage/detection modifier.
- **`FrozenActorLayer` (`OpenRA.Game/Traits/Player/FrozenActorLayer.cs`) is the ONLY per-player, fog-correct last-seen store** — frozen `CenterPosition` per viewer, updated on shroud change (`:37-38,107,165,276`). But nothing derives an enemy **direction/density field** from it. "Position relative to known enemy direction" has raw data but **no derivation → MISSING.**
- **Both AI spatial grids are OMNISCIENT.** `InfluenceMap.Recompute` iterates `world.Actors` with no fog check (`InfluenceMap.cs:92`; per-owner, CellSize 2, 25-tick cadence); `ThreatMapManager` likewise (`:89`). A non-cheating tactical layer needs a NEW per-player fog-derived layer built on FrozenActorLayer, modelled on `MapLayers` (synced, per-player) for determinism. Perf precedent: `CellLayer<T>` + staggered N-tick recompute is standard.
- **Determinism trap:** bot decisions run off `LocalRandom`, which is **NOT in the sync hash** (`World.cs:543` hashes only `SharedRandom.Last`) — deterministic only by identical per-client execution, no desync tripwire. Never read `RenderPlayer`/`LocalPlayer` in sim; order HashSet/Dictionary iteration by ActorID before it gates a decision.
- **Arbitration:** idle-gate (`IsIdle`) protects a player's active order, but bot squad orders re-fire `queued:false` every ~75 ticks (`SquadManagerBotModule` `AttackForceInterval`), so a shared-unit tactical layer must also register in a commitment ledger (`PoiGoalGuard.Ledger`, `MountedTransport.IsPassengerReserved`, `BotBlackboard.ClaimUnit`).

## 2026-07-21 — Mounted transports never dismount: the bot issues the ACTIVITY name as an order string ("UnloadCargo" ≠ "Unload") — and the autotest only proved carriage+arrival, never the unload (live-crash match triage)

> **[promoted: → conventions.md §Engine behaviors that surprise (a bot Order string must match a ResolveOrder case, not an activity class name)]** (curation 2026-07-22). Verified `Cargo.cs:248,255` (Unload/UnloadCargoPassenger) vs `:519` (UnloadCargo is the activity name). Fix-changelog + autotest-blind-spot detail stays here.

Found triaging a live Exp-vs-Exp match (carrier drives a TECN to the derrick and sits there loaded forever; non-TECN frontline infantry also never dismounts).
- **Root cause: `MountedTransportBotModule.AdvanceTask` issued `new Order("UnloadCargo", carrier, …)` (`MountedTransportBotModule.cs:287`), but `Cargo.ResolveOrder` only handles `"Unload"` / `"UnloadCargoPassenger"` (`Cargo.cs:248,255`).** `UnloadCargo` is the *activity class name* (`Activities/UnloadCargo.cs`, queued internally by Cargo at `:253,519`), not an order string — the order matched nothing and was silently dropped. The carrier still transitioned `Delivering→Unloading` and then waited on `cargo.IsEmpty()`, which never became true → stuck loaded at the drop-off forever. Affected BOTH the capture ferry and the generic frontline delivery (shared code path), which is why non-TECN pax were stuck too.
- **The unload path is shared by the `@poi` (stable) and `@experimental` twins, so the fix had to be gated.** `MountedTransportBotModule@poi` is `enable-ai-stable` (`ai.yaml:370`); `AdvanceTask` is the same C# for both. A straight string swap would change @stable and break byte-identity. Fix: new default-false `UnloadOnArrival` field — issues `"Unload"` when set, keeps the broken `"UnloadCargo"` no-op when unset — enabled only on `@experimental` (`ai.yaml`). Frozen path unchanged.
- **`Cargo.ResolveOrder` drops even the correct `"Unload"` order when `!CanUnload()` (`Cargo.cs:250-251`)** — no adjacent cell free on arrival. Issued once on the state transition, a single drop re-creates the permanent stall. Added a re-issue in the `Unloading` branch guarded on `carrier.IsIdle && cargo.CanUnload()` so a freed cell rescues it (experimental only).
- **Autotest blind spot (why `test-tecn-ride` was GREEN through this):** its predicate PASSes "the instant" `mounted` latches (carrier `HasPassengers`) AND the carrier arrives within 6 cells of the derrick (`test-tecn-ride.lua:29-37`). It never asserts the TECN *dismounts* or the derrick is *captured* — so the broken order string was never exercised. A carriage+arrival test cannot cover an unload bug; the pass predicate must check `cargo.IsEmpty()` post-arrival (or derrick ownership flip) to close the gap.

## 2026-07-21 — The offense scorer is blind to FRIENDLY influence; the enemy sample is already carried per-target (recon for the territorial bias)

> **[rejected: in-flight recon tied to WORKSPACE/plans/260721_terr_offense_bias.md (mutable code-state: GetFriendlyInfluence unconsumed, ContributionRadius override). The durable lesson "PoiMap is a shared world singleton — per-profile scoring must live on the per-player module" is already generalized in architecture.md §Adding a behavioural field to a shared trait]** (curation 2026-07-22).

Found tracing the balance-of-power slice (plan `WORKSPACE/plans/260721_terr_offense_bias.md`).
- **`GetFriendlyInfluence` is computed but never consumed by any bot module.** `InfluenceMap` exposes `GetFriendlyInfluence`/`GetEnemyInfluence`/`GetFrontline` per-perspective (`InfluenceMap.cs:143-175`), but the whole offense/capture/defense stack reads **only enemy** influence: `PoiMap.SampleThreat` (`PoiMap.cs:481-498`) samples the enemy grid into `ScoredPoi.EnemyInfluence`, and `PoiScoring.ThreatFactor` (`PoiMap.cs:542-550`) buckets it safe/mild/hostile. Consequence: a target with **high enemy AND high friendly** influence (a front we locally dominate) gets the *same* ×10 hostile damp as one with high enemy and zero friendly — the scorer cannot tell "winnable contact" from "lunge into strength." Reading `GetFriendlyInfluence` is a genuinely new input, not a re-weight.
- **`PoiMap` is a shared world singleton — cannot host a per-`@experimental` scoring delta.** One `PoiMap` instance on the world actor (`world.yaml:299`) is queried by both bots; its `PoiMapInfo` has no per-profile split. So any `@experimental`-only offense bias must live in the **per-player** `PoiOffensiveBotModule` (gated `enable-ai-experimental`), rescaling the returned `List<ScoredPoi>` — exactly what `RescaleSrPressure` already does (`PoiOffensiveBotModule.cs:340-358`, guarded by `SrPressureScoreMultiplier != 100` at `:217`). That is the reusable template for "new default-off offense scoring lever."
- **`InfluenceMap.ContributionRadius` is YAML-overridden 3→5** (`world.yaml:287` vs code default `:38`), so a single grid-cell influence sample already reflects units up to ~10 map cells away with falloff — single-cell sampling at a target is enough to register a nearby front, no neighborhood scan needed for slice 1.
- **Batch bars read verdict JSON, not markers.** `parse-s2-batch.py` computes swing/engagement from `stats.kills_cost`/`deaths_cost` only; `[exp-*]` debug.log markers (SR-contestation, capture) are human-grep diagnostics on the preserved per-match `debug.log`, not parser inputs. New telemetry needs no parser change to gate a cycle.

## 2026-07-20 — TECN-first capture ferrying: directed ride beats the frontline-gated transport (playtest bug 3, branch `exp-tecn-ride`)

> **[rejected: feature changelog for branch exp-tecn-ride (directed capture-ferry impl + @stable-isolation gating) tied to the mission-abstraction plans; the experimental capture / ferry / goal-guard layer is not documented in DOCS/reference and this impl detail belongs with the commit + WORKSPACE]** (curation 2026-07-22).

"Technicians ride first" was never implemented — root cause was three independent gaps, and the clean fix was to *not* extend the existing frontline transport but to add a **directed** ferry path (`MountedTransportBotModule.cs`).
- **The frontline transport can't express a capture destination.** `PickDropOffCell` returns null pre-contact (`GetFrontline` needs both-sides influence, `InfluenceMap.cs:248-256`) and, even post-contact, its drop-off is the *thinnest frontline cell*, never a capture target. So the whole early game had zero mounting by construction. Adding TECN to `PassengerTypes` (the frontline auto-scan list) would have ferried TECNs to the *line*, not to derricks — wrong. The working model is a directed API `TryReserveCaptureFerry(bot, tecn, target)` that CaptureCoordinator calls; it bypasses `PickDropOffCell` entirely (destination = the target), which also makes it pre-contact-safe for free — 3.1 and 3.2 collapse into one path for captures.
- **Order-cancellation choreography.** `EnterTransport` (queued false) cancels the TECN's `CaptureActor`, so after `UnloadCargo` the TECN is idle *and* still ledger-committed → CaptureCoordinator's idle filter skips it (committed), so it would sit forever. The ferry must **re-issue `CaptureActor` itself** on unload (Unloading→empty branch). Capture ferries also launch at 1 passenger, not `MinPassengersPerLoad` (2).
- **The `@poi` singleton had to be split to isolate `@stable`.** `MountedTransportBotModule` was gated `enable-ai-experimental || enable-ai-stable` and fetched via `player.TraitOrDefault<MountedTransportBotModule>()` — so a naive `@experimental` twin would make that lookup throw ("multiple traits"). Fix mirrors the heli split (`cf7f826b`): `@poi`→`enable-ai-stable`, add `@experimental` twin, and migrate consumers (`LayeredDefenceBotModule.cs:248`, CaptureCoordinator) to `TraitsImplementing<>().FirstOrDefault(m => !m.IsTraitDisabled)`. Only enabled ConditionalTraits tick (`ModularBot.cs:96`), so exactly one is live per player. All new fields default-false → `@stable`/Normal/Rush/Turtle byte-identical.
- **Verify:** autotest `test-tecn-ride` (experimental USA bot, one TECN + one bradley, neutral derrick ~39 cells east) passes when the carrier has carried a passenger AND arrived within a few cells of the derrick. RED without the fix (default-false → walks on foot). NUnit 291/291, build clean. **Benchmark S1 capture-metric verify vs the new Motorized/US-US baseline is still PENDING** (separate cycle).

## 2026-07-21 — Autotest throughput plumbing landed (Recs 1-3): universal `--speed`, minimized+uncapped Mode-B, render decouple

> **[rejected: harness changelog tied to WORKSPACE/plans/260721_sim_throughput.md — a landed-change log + one-run measurement; harness-speed how-to belongs in DOCS/recipes/AUTOTEST if anywhere, not engine/gameplay reference]** (curation 2026-07-22).

Implemented the low-risk half of the throughput plan (`WORKSPACE/plans/260721_sim_throughput.md`), branch `harness-sim-speed` (merged `bce9c3e6`). Simulation determinism untouched — wall-clock pacing / unsynced-render only.
- **Rec 1 — universal speed multiplier.** New `TestModeSpeedMultiplier` world trait (`engine/OpenRA.Mods.Common/Traits/World/TestModeSpeedMultiplier.cs`, registered in `world.yaml`) divides `world.Timestep` at `WorldLoaded` for every non-tournament test run; guards on empty `TournamentConfigPath` so it never double-applies against `BotVsBotMatchWatcher` (which keeps its config-overridable apply). `run-test.sh` gained `--speed N` (1-16) forwarding `Test.SpeedMultiplier`; default unset = 1× (byte-identical old behavior).
- **Rec 2 — Mode-B hidden = minimized + uncapped.** `run-tournament.sh` now launches `OPENRA_WINDOW_MINIMIZED=1` + `Graphics.CapFramerate=false` (dropped the 5 fps cap that throttled a *suspended* window to ~5 ticks/s); `run-test.sh --minimized` also forces `CapFramerate=false`. NOT re-measured with a live tournament (outside the single-run verify budget) — grounded in the recon's suspended-window model; it is a config change, not a correctness risk (worst case is efficiency, not a bad verdict).
- **Rec 3 — render decouple under TestMode.** Gated the forced render-per-tick (`Game.cs`, the `renderBeforeNextTick = true` after `LogicTick`) on `!TestMode.IsActive`, so test logic free-runs and renders on the normal cadence instead of dragging a GPU frame per tick.
- **Measured (one authorized run):** `run-test.sh --speed 8 test-screenshot-smoke`. `debug.log` confirms the trait fired: `[TestMode] speed multiplier 8x — Timestep 60 → 7 ms/tick`. Sim phase (screenshot timestamps, tick 0→51) = **1.86 s wall-clock vs the ~3.06 s the 60 ms timestep predicts at 1×**. Verdict `pass`, all 3 screenshots captured at ticks 0/26/51 → Rec 3's decouple does **not** break capture. Total run 16 s (init-dominated). NUnit 291/291, build clean. Caveat: this test is render/screenshot-bound and only 51 ticks — the *least* favorable case, so the realized factor understates the win; compute-heavy bot matches (mostly logic per tick) realize far more of the 8× once Timestep is 7 ms.

## 2026-07-21 — Autotest sim speed: single tests are 1× by omission; the render-per-tick coupling caps the tournament's 8×

> **[rejected: read-only throughput audit tied to WORKSPACE/plans/260721_sim_throughput.md; the fixes it proposed have since landed (see the throughput-plumbing entry). Harness-speed methodology belongs in DOCS/recipes/AUTOTEST, not reference]** (curation 2026-07-22).

Found during a read-only throughput audit (full options report: `WORKSPACE/plans/260721_sim_throughput.md`).
- **`run-test.sh` runs at 1× because it never passes a speed arg.** `TestMode.SpeedMultiplier` defaults to `1` (`engine/OpenRA.Game/TestMode.cs:80`) and the single-test launcher forwards no `Test.SpeedMultiplier` (`tools/autotest/run-test.sh:285-295`). Mod default `Timestep` is 60 ms (`mods/ww3mod/mod.yaml:369-372`) → ~16.7 sim ticks/s. Only the tournament path passes `Test.SpeedMultiplier=8` from config (`run-tournament.sh:298`).
- **The multiplier apply-site is tournament-only.** `Test.SpeedMultiplier` is parsed + clamped 1–16 (`TestMode.cs:100-102`) but *applied* only inside `BotVsBotMatchWatcher.WorldLoaded` via `world.Timestep = max(1, base/N)` (`BotVsBotMatchWatcher.cs:152-158`). Lua single-tests get nothing even if you pass the arg — the fix must add a universal apply site (world trait or `Game.LoadMap` next to the `GameSpeedOverride` hook, `TestMode.cs:62-65`).
- **`Test.GameSpeed=fastest` is only ~1.5×** (`Timestep: 40`, `mod.yaml:381-384`) — that's why every config note says SpeedMultiplier dominates. The cheat button caps at 8× by the same `world.Timestep` division (`SpeedControlButtonLogic.cs:58-62`).
- **The real ceiling is CPU, and rendering is the tax.** Every `LogicTick` forces a `RenderTick` (`Game.cs:1026-1027`), so 8× also renders 8×; harness comments claim ~3-4× realized (`run-tournament.sh:286-289`). `MaxLogicTicksBehind=250` (`Game.cs:970,1010`) drops catch-up, so the sim never outruns tick-compute.
- **Minimized window skips rendering, but only helps with an *uncapped* framerate.** SDL minimize/hide sets `IsSuspended` (`Sdl2Input.cs:124-126`) → loop skips `RenderTick` (`Game.cs:1032`) and only pumps input (`1049-1059`). BUT the forced-render flag clears only at render cadence when suspended (`Game.cs:1058`), so minimize + 5 fps cap throttles logic to ~5 ticks/s. Fast combo = **minimize + `CapFramerate=false`** (default `renderInterval≈1 ms`, `Settings.cs:201`, `Game.cs:994-998`). The tournament's current 5 fps profile is *visible*, not suspended (`run-tournament.sh:301-302`).
- **Speed is behavior-neutral (verified).** `world.Timestep` is pure wall-clock pacing, never synced; all rendering is `Sync.RunUnsynced`; Lua timers are tick-based (`test-helpers.lua:82-83`); `OrderLatency:2` is 2 ticks. Bot decisions are a pure function of tick+seed (`BotVsBotMatchWatcher.cs:56-58`). Separate latent bug: `TicksPerSecond=25` (`test-helpers.lua:9`) vs actual 16.7 at 60 ms — constant across speeds, so not a validity issue.
- **Headless ≠ dedicated server.** `OpenRA.Server.dll` (`launch-dedicated.sh`) is a lockstep order relay; it does not run `world.Tick()`/bots. A true headless harness is a *rendering-disabled client* (null graphics platform + a `logicInterval=1` loop branch — the engine already does exactly this for save-loading, `Game.cs:1001-1005`), not the server.
- **Parallelism blocker is a shared support dir.** `launch-game.sh:60` sets no `Engine.SupportDir`, so instances collide on `settings.yaml`, `Logs/debug.log`, and the local server port. Per-instance `Engine.SupportDir` (the dedicated launcher already threads it, `launch-dedicated.sh:98`) + distinct ports unlocks concurrent matches.

## 2026-07-20 — `RenderPlayer = null` world view only clears shroud from a cold start; ShroudRenderer never clears it mid-game

> **[promoted: → architecture.md §RenderPlayer is render-side only]** (curation 2026-07-22). Verified `World.cs:109-114` (Fog/Shroud short-circuit) + `:543-547` (sync hash reads UnlockedRenderPlayer, not RenderPlayer).

Found while adding full-map vision to the visible TestMode window (worktree `test-observer-vision`).
- **`RenderPlayer` is purely render-side.** `World.FogObscures/ShroudObscures` all short-circuit to `false` when `RenderPlayer == null` (`engine/OpenRA.Game/World.cs:105-111`); no player's `MapLayers` (shroud/fog) is touched, and the sync hash reads `p.UnlockedRenderPlayer`, not `world.RenderPlayer` (`World.cs:541-544`). So switching a real player's client to world view leaves AI perception + the test verdict byte-identical. The dev "disable shroud" cheat is **not** an equivalent: `DeveloperMode` `DevVisibility/DevAll` do `MapLayers.ExploreAll()` + `MapLayers.FogDisabled = true` (`Traits/Player/DeveloperMode.cs:171-197`) on synced (`[Sync] disableFog`) per-player state — that changes the local combatant's unit targeting and the sync hash, so it's unusable under a byte-identical constraint.
- **The trap:** `ShroudRenderer.UpdateShroud` was wrapped in `if (world.RenderPlayer != null)` (`Traits/World/ShroudRenderer.cs:252`), so when `RenderPlayer` flips to null on a *live* client the already-drawn shroud sprites are never cleared → the map stays black even though `WorldOnRenderPlayerChanged(null)` set uniform visibility. True observers look correct only because they start null and never draw shroud at all. Fix: always clear each dirty cell's sprites, then repaint only when a render player is active (same commit). This also repairs the `DevCinematicView` cheat, which toggles `RenderPlayer` to null the same way.
## 2026-07-21 — Heli rearm-full bench has TWO gates: the module readiness check AND the FSM's SquadHasAmmo (minimal fix is not sufficient)

> **[promoted: → architecture.md §AI configuration (two full-ammo squad gates; SkipRearmReadyCheck bypass)]** (curation 2026-07-22). Verified current code — the fix landed: `HelicopterStates.cs:118-131` (SquadHasAmmo skips ReloadsAutomatically units) + `RearmReadyCheckBypassed` at `:138`; `HelicopterSquadBotModule.cs:408` (SkipRearmReadyCheck).

Found while implementing playtest Bug 2 (branch `fix-evac-heli`). The triage's minimal fix (bypass `HelicopterSquadBotModule.IsReadyForMission`'s full-ammo loop) is **necessary but not sufficient** — it lets a squad FORM but not LAUNCH.
- **Second, independent gate:** `HelicopterStates.HelicopterIdleState.Tick` returns early on `!SquadHasAmmo(owner)` (`engine/OpenRA.Mods.Common/Traits/BotModules/Squads/States/HelicopterStates.cs:183`). `SquadHasAmmo` (`:118-131`) *skips* every unit for which `ReloadsAutomatically` is true, then returns false if none remain. `ReloadsAutomatically` (`StateBase.cs:129-139`) is true when a `Rearmable` covers all the unit's pools — EXACTLY the case for attack helis (`Rearmable{ AmmoPools: primary-ammo, secondary-ammo }`). So an all-attack-heli squad reports "no ammo" **even at full ammo**, and the idle/withdraw/re-engage gates (`:183, :427, :458`) never pass. The squad forms and sits.
- **Proven via trace:** with only the module bypass, `squad-formed size=2` fires once, then `idle-blocked reason=SquadHasAmmo` repeats every 5 ticks forever; the helis never leave the ground. Gating those three `SquadHasAmmo` uses behind the same per-module `SkipRearmReadyCheck` flag (read from the player's `HelicopterSquadBotModule` in the FSM) makes the squad reach `HelicopterApproachState` and issue `Attack` orders — helis then take off and fly.
- **Autotest gotcha that cost several runs:** a heli issued `Attack` on an UN-attackable target (the enemy `supplyroute` is `NoAutoTarget` and matches no weapon's `ValidTargets`) never takes off — the attack activity no-ops and the heli stays grounded. The squad target-picker also fixates on the SR over a nearer tank. A deterministic heli-movement test needs a REAL attackable target (t90: Vehicle+Ground → Hellfire+30mm) and NO enemy SR to hijack targeting. Use `TestHarness.AssertWithin` (polls + exits on first movement) rather than a fixed `AfterDelay` — the latter left games running for minutes as orphaned processes when a run was interrupted.
## 2026-07-21 — Out-of-ammo evac is engine-level and invisible to bot modules; only LayeredDefence guards it

> **[promoted: → architecture.md §AI configuration (out-of-ammo evac is unit-level; LayeredDefence is the only guard)]** (curation 2026-07-22). Verified `AmmoPool.cs:197-204` (Evacuate→RotateToEdge), `LayeredDefenceBotModule.cs:102,277,469` (SkipOutOfAmmoUnits/IsOutOfAmmo).

Found while triaging the "evac units re-ordered onto attacks" playtest bug (`2ed2c0ac`, plan `WORKSPACE/plans/260721_playtest_bugs_triage.md`).
- **Evac is a unit-level `AmmoPool` behaviour, not an AI decision.** `AmmoPool.AutoRearmIfAllEmpty` `case Evacuate` → `RotateToEdge` (`engine/OpenRA.Mods.Common/Traits/AmmoPool.cs:197-205`), fired from `INotifyAttack`/`INotifyBecomingIdle` (`:247-254`); WW3MOD vehicles opt in via `InitialResupplyBehaviorAI: Evacuate` (`mods/ww3mod/rules/ingame/vehicles.yaml:514-515`). The granted `evacuating` condition is **cosmetic only** (selection pip) — no bot module reads it, and the evac path never Commits the unit to `PoiGoalGuard.Ledger`.
- **Therefore an evacuating unit is "free" to any module that lacks an ammo filter.** `PoiOffensiveBotModule.IsEligibleCombatUnit` (`PoiOffensiveBotModule.cs:403-412`) has none → recruits empty units onto axes, overwriting `RotateToEdge`. `LayeredDefenceBotModule` is the **only** module that guards it: `SkipOutOfAmmoUnits` (default `true`, `:102`) + `IsOutOfAmmo` = all AmmoPools at 0 (`:465-471`), applied at `:273`. Reusable pattern: any module that pulls units by proximity/idle needs this guard or a shared evac reservation.

## 2026-07-21 — AI helicopters are permanently benched with no HPAD: the squad path has its own rearm-full gate

> **[promoted: → architecture.md §AI configuration (attack-heli squad gates; corner-idle = ProductionFromMapEdge arrival logic)]** (curation 2026-07-22). Verified `HelicopterSquadBotModule.cs:408` (IsReadyForMission full-ammo gate, now bypassable via SkipRearmReadyCheck). Merged with the two-gates entry above.

Found while triaging "helis fly to a corner and idle" (`2ed2c0ac`).
- **The documented `SkipRearmBuildingCheck` bypass only covers PRODUCTION.** The attack path has an independent gate: `HelicopterSquadBotModule.IsReadyForMission` (`engine/OpenRA.Mods.Common/Traits/BotModules/HelicopterSquadBotModule.cs:399-408`) requires **every AmmoPool `HasFullAmmo`** for any heli that has `AmmoPool`+`Rearmable`. Attack helis' `ReloadAmmoPool RequiresCondition: unit.docked && !airborne` (e.g. `mods/ww3mod/rules/ingame/aircraft-russia.yaml:178`) + `Rearmable{ RearmActors: hpad }` mean they can only refill at an HPAD — and the mod builds none. First shot ⇒ never full again ⇒ `IsReadyForMission` false forever ⇒ no squad ever forms ⇒ the `HelicopterStates` FSM never runs. Recruitment (by trait `AIHelicopterRole`, `:146`) works fine; the *readiness* gate is the block.
- **Corner-idle is arrival logic, not RA idle-return.** `ProductionFromMapEdge` gives aircraft `hasRallyPoint ? rp.Path : {self.Location}` (`ProductionFromMapEdge.cs:89,173-175`); the SR `RallyPoint` has no default Path (`structures.yaml:272-274`) so helis fly to the SR/edge cell and stop. `Aircraft.IdleBehavior` defaults `None` (`Air/Aircraft.cs:27`), so no return-to-base residue is involved.

## 2026-07-21 — MountedTransport is dormant until frontline contact, and never carries TECN (capture is fully decoupled)

> **[rejected: pre-fix triage superseded by the directed capture-ferry (exp-tecn-ride) that added a capture-aware transport path; belongs in WORKSPACE / bugs, and the GetFrontline both-sides-influence detail is impl-specific]** (curation 2026-07-22).

Found while triaging "TECN walks to captures; mounting never observed" (`2ed2c0ac`).
- **`PickDropOffCell` returns null with no frontline**, so the whole module no-ops pre-contact: `MountedTransportBotModule.cs:313-314, 373-380` depend on `InfluenceMap.GetFrontline`, which marks only cells with **both** friendly AND enemy influence (`InfluenceMap.cs:170-174` → `DeriveFrontline` `:248-256`). Early game has no such cell → no mounting ever happens in the window players watch. Idle carriers (bradley/m113/bmp2, produced per `ai-america.yaml:27-28`, excluded from offense `ai.yaml:187` + defence `:341`) then pile up at the SR — a direct contributor to the "vehicles massing at the SR" complaint.
- **TECN is not a passenger and capture never requests a ride.** `PassengerTypes` (`ai.yaml:366`) omits `tecn*`; `CaptureCoordinatorBotModule` issues `CaptureActor` + on-foot escort `AttackMove` (`CaptureCoordinatorBotModule.cs:514, 627-643`) with **zero** call into MountedTransport, whose destination is a frontline gap, not a capture target. "Technicians riding first" is therefore unimplemented, not merely mis-tuned — it needs a capture-aware transport path.

## 2026-07-21 — The tournament ladder measures `startingunits: none`, not the Motorized regime players use

> **[rejected: benchmark-config note (startingunits classes; ladder uses `none`) and now stale — the 2026-07-21 regime change moved the ladder to a Motorized start (see the regime-rebaseline entry below). Tracker / ladder material, not durable engine reference]** (curation 2026-07-22).

Found while folding the Motorized directive into early-game recon (`2ed2c0ac`).
- **`startingunits` is a lobby dropdown** (`SpawnStartingUnits.cs:23-53`, key `:51`, default `"none"` `:25`); values `none/squad/platoon/motorized/air` defined per-faction in `mods/ww3mod/rules/world.yaml:364-436`. **Motorized** (`:404-419`) ships abrams/bradley/humvee (America) or t90/bmp2/bmp2 (Russia) + infantry, but **no dedicated SAM** — its only AA is the humvee/bmp2 autocannon.
- **All tournament scenarios use the default `none`** (bots start with only two hand-placed `supplyroute`; `WORKSPACE/ai-bench/LADDER.md:448-451`, no `StartingUnitsClass` on bot PlayerReferences). Optimising for Motorized ⇒ scenario change ⇒ **re-BASELINE** (S1/S2 bars) before trusting Motorized tuning; the item-b AA-share floor in particular must be tuned against Motorized's built-in AA, not the `none` regime.

> **[rejected: north-star design-intent capture, already promoted to DOCS/design/ai-realism.md → "Long-term vision (user-authored, 2026-07-20)" and reflected in auto-memory; vision, not engineering reference]** (curation 2026-07-22).

Recorded live from the project owner while spectating an Experimental-vs-Experimental match. This is
**north-star design intent**, not a code finding — promoted into the standing design doc
[`DOCS/design/ai-realism.md`](../DOCS/design/ai-realism.md) → "Long-term vision (user-authored, 2026-07-20)".
Logged here so a curation pass can link the two. Three themes:

1. **Territorial-control map layer (the centerpiece).** A fog-respecting map layer classifying territory
   **safe / grayzone / enemy**; own-half assumed safe at start (2-player prior) until proven otherwise;
   updates only from real intel (no seeing through fog). Safe = "capture + set up defensive positions".
   Runs the whole game: enemy retreats/dies → area safer → advance there → **always push where the enemy
   is comparatively weak**; a balance-of-power reading of the same layer drives repositioning + reinforcing
   weak spots. End state: forces **spread along the ENTIRE line of combat** (most important sectors first,
   eventually some soldiers along the whole front), front **steps forward wherever it is safe**. A held,
   advancing line — not a death-ball.
2. **Early-game economy sensibilities.** No supply trucks while all units have full ammo (a start-bought
   truck just sits as a target; simple rule now, foresight later). AA proportionate to the real air threat
   (a couple of AA infantry already deter helicopters; multiple SHORAD/Tunguska at start = overbuild).
   Early urgency to spread out + capture fast in **small groups/packets** rather than one armada at the SR.
3. **Mounted infantry doctrine.** Technicians ride vehicles to distant captures (first priority); later,
   soldiers ride with context-appropriate dismount (far from enemy when just reaching the front to
   hold/defend, closer for assault transport) — always weighing that **one missile can kill vehicle +
   squad together**.

Relevant engine systems for eventual translation (per the realism doc's mapping): `InfluenceMap` /
`PoiMap` (territory + weak-point reading), `PoiOffensiveBotModule` (advance where weak), the garrison
module (hold captured safe ground), `MountedTransportBotModule` (mounted doctrine), the SR call-in budget
(early-eco discipline). No code written this session — vision capture only.

## 2026-07-20 — An SR Pressure offensive axis does NOT starve the TECN capture layer (offense/capture pool independence, empirical)

> **[rejected: run-specific empirical finding (SR-contestation cycle 1, N=10) belonging to runs/260720_sr_contestation_cycle1_n10.md; the goal-guard ledger free-pool is undocumented in DOCS/reference (cf. the 2026-07-20 escort-ledger rejection), so a pool-independence note would be orphaned]** (curation 2026-07-22).

Found during SR-contestation cycle 1 (`runs/260720_sr_contestation_cycle1_n10.md`). With the new
`PoiOffensiveBotModule.SrPressureScoreMultiplier: 260` on `@experimental`, the enemy Supply Route
**safe-threat** Pressure score reaches ~**57M** (observed axis line: `action=Pressure score=57408000
units=8`), which **outranks neutral oilbs** and pulls a full 8-unit offensive axis mid-game (first tick
~1600–2150, minutes ~5–7). Despite that, the **S1 economy result was byte-for-byte the reference tier**
(capture 8/10, conditional gross median $6,457, win 10–0, same two $0 seeds). Non-obvious takeaway: the
offensive-axis layer (`PoiOffensiveBotModule`, combat units, `AttackMove`) and the capture layer
(`CaptureCoordinatorBotModule`, TECNs) draw from the **shared `PoiGoalGuard` ledger free pool
independently** — pulling combat units onto an SR Pressure axis does **not** consume the TECN pool, so
income capture is unaffected even when the SR wins a high-scoring axis. Useful prior for any future cycle
that boosts an offensive axis score: expect **no** first-order S1 capture regression from offense re-ranking
alone; a capture regression would instead point at the TECN production pipeline. (Also: at `260` the SR can
top the ranking at safe threat — a heavier multiplier risks over-prioritising it; the `ThreatHostileMultiplier`
gate ×260 ⇒ ~4.25M still keeps the AI off a garrisoned SR. `PoiOffensiveBotModule.cs` RescaleSrPressure +
call site ~:196.)

## 2026-07-20 — The TECN-floor request dies at a *busy Infantry queue*, and the M-2 vs every-scan placement is identical at floor 1

> **[rejected: the request-drop-on-busy-queue mechanic is already in architecture.md §AI production (IBotRequestUnitProduction demand queue, promoted); the floor-placement-no-op-at-1 point is tuning detail tied to the capture-throughput cycle]** (curation 2026-07-22).

Found during the capture-throughput recon (`WORKSPACE/plans/260720_capture_throughput_cycle.md`).
Two non-obvious code facts about the `IBotRequestUnitProduction` floor path:

1. **Request-death point (the m7-class "requested 82× never converted"):** a popped build request
   only starts production on a queue where `!q.AllQueued().Any()` — i.e. a **free** queue —
   `UnitBuilderBotModule.BuildUnit(name)` (`:155`). With one busy Infantry queue the popped request
   finds no free queue and is **silently dropped** (the pop at `:90-91` removes it regardless), so
   `RequestedProductionCount` reads 0 again and the floor re-requests next scan. N re-requests = N
   popped-and-dropped cycles against a saturated queue — a **production-starvation** tail (with
   `tecn-killed=0`, the unit is never produced), NOT a survival/dispatch problem. Re-requesting faster
   cannot fix it; the lever is queue reservation / a dedicated capturer production path / lower
   competing infantry share.

2. **Floor placement is a no-op at floor 1:** moving `MaintainTecnFloor` off the M-2
   (`idleCapturers==0`) gate (`CaptureCoordinatorBotModule.cs:271-272`) to run every scan is
   **byte-identical at `TecnFloor: 1`** — every-scan fires only when `alive+pending<1` ⇒ `alive=0` ⇒ no
   capturers ⇒ `idleCapturers=0` ⇒ M-2 already reached. The placement only differs at `TecnFloor ≥ 2`.
   Practical consequence: the code move is **safe for a frozen `@stable` that stays at floor 1** with no
   new gate field — a rare case where a shared-class behaviour change needs no default-off bool.

Corollary for diagnosis: a $0 capture run that already holds ≥1 alive TECN is a **conversion stall**,
not an availability gap — the floor is satisfied, so neither placement nor a floor bump is guaranteed to
flip it; only redundancy (floor 2 = a second independent attempt) or a screen (escort reservation) does.

Code refs: `UnitBuilderBotModule.cs:85-96,142-165`, `CaptureCoordinatorBotModule.cs:245-274,380-405`.

## 2026-07-20 — Benchmark run-to-run variance is one unseeded line; the fixed seed already flows everywhere *except* `LocalRandom`

> **[promoted → architecture.md "Bot decisions ARE seed-reproducible"]** (curation 2026-07-20). Verified `World.cs:213-224` (LocalRandom now seeded from RandomSeed via the LCG transform, guarded on `!= 0`). Pre-fix recon subsumed by the verify entry below.

Found during the seeded-determinism recon (`WORKSPACE/plans/260720_seeded_determinism.md`).
The S1 4/10-vs-9/10 wobble traces to a single line: `World.cs:214` builds
`LocalRandom = new MersenneTwister()` **unseeded**, which chains to
`this(Environment.TickCount)` (`MersenneTwister.cs:25-26`) — a wall-clock seed. ~40 bot-decision
sites read `world.LocalRandom` (scan/reeval countdowns, unit call-in picks, squad splits, rally
cells — see plan §1b), so bot behavior differs every launch even with an identical seed.

The non-obvious part: the deterministic seed **is already plumbed end-to-end**. The tournament
runner passes `Test.RandomSeed=$((i*1000+17))` (`run-tournament.sh:282,298`) →
`TestMode.RandomSeedOverride` (`TestMode.cs:96-98`) → `Server.cs:310,332` →
`GlobalSettings.RandomSeed`, which already seeds `SharedRandom` (`World.cs:213`) and `playerRandom`
(`World.cs:237`). Combat RNG (inaccuracy/miss/burst) also rides `SharedRandom`
(`Armament.cs:513,536,567,654`), so it is already deterministic. Only `LocalRandom` is the gap —
seeding it (decorrelated from the shared seed) is the whole fix; no shell/YAML/env-var work needed.
Corollary: the `BotVsBotMatchWatcher` header documents a `"seed"` verdict field (`:21`) that
`SerializeVerdict` (`:287-356`) never actually emits.

This makes the `architecture.md:291-293` note ("Bot decisions are not seed-reproducible") a
*current-state* fact that a ~2-line change would invert — update that note if/when the seeding lands.
**RESOLVED** (main @ `2d3c8fe0`): the seeding landed and verified FULL determinism — see next entry.

## 2026-07-20 — Seeding `LocalRandom` gives FULL replay determinism; async pathfinding did NOT leak nondeterminism

> **[promoted → architecture.md "Bot decisions ARE seed-reproducible"]** (curation 2026-07-20). Verified `World.cs:213-224`; added the async-pathfinding-is-deterministic clause to the doc. The "per-seed capture is near-binary Bernoulli" observation left here as benchmark-methodology (covered in reference by "one seed is one battlefield").

Verify of the seeding fix (`World.cs:214`, main @ `2d3c8fe0`;
`WORKSPACE/ai-bench/runs/260720_seeded_determinism_verify.md`). Two hidden Mode-B matches at the
same seed came back **byte-identical** — not just the final verdict, but the watcher's tick-by-tick
score log (60 logged intervals over 7500 ticks) matched line-for-line. The plan's prime suspect for
residual nondeterminism (async pathfinding after the seeding fix, §5.3) **did not materialize**:
seeding the single unseeded `LocalRandom` was sufficient for full reproduction. So OpenRA's
off-thread pathfinding applies its results deterministically on the sim thread even with WW3MOD's
modified movement — no extra work needed for benchmark determinism.

Second, non-obvious for benchmark design: **in-window derrick capture is a near-binary per-seed
outcome, not a gradual dial.** Seed 1017 → *both* bots `capture_income_gross=0` (no capture landed
in 7500t); seed 9017 → experimental `gross=10917`. That is the whole 4/10-vs-9/10 variance — each
seed either lands the early capture or it doesn't. Implication: a stable capture-rate mean needs
enough seeds to sample that Bernoulli-ish distribution; a single seed tells you nothing about the
rate, only that *this* battlefield did/didn't capture.

Transform (decorrelates `LocalRandom` from `SharedRandom` while staying a pure function of the seed):
`(int)(RandomSeed*6364136223846793005 + 1442695040888963407)`, guarded on `RandomSeed != 0` so
normal gameplay (seed = `DateTime.Now.ToBinary()`) still varies per launch. Verdict now records the
seed (`verdict_version` 5).

## 2026-07-20 — `UnitBuilderBotModule` UnitsToBuild weight is a share *ceiling*, NOT a priority

> **[promoted → architecture.md "AI production: `UnitsToBuild` weights are share ceilings"]** (curation 2026-07-20). Verified `UnitBuilderBotModule.cs:25,49,125-136,167-195` (shuffle + `count*100 < weight*total` at :190; idle-cap uniform-random path; single-name overload bypass).

Found during the TECN-availability cycle-2 recon (`WORKSPACE/plans/260720_tecn_availability_cycle2.md`).
A common misread: a big weight like `tecn.*: 500` (`ai-{america,russia}.yaml:8`) makes the AI
*prioritize* that unit. It does not. `ChooseUnitToBuild` (`UnitBuilderBotModule.cs:177-195`)
**shuffles** `UnitsToBuild` and returns the **first** entry passing `count*100 < weight*total`
(`:190`) — i.e. `count/total < weight/100`, so `weight/100` is a per-type share *ceiling as a
percent*. Any weight ≥100 (100%) can never bind, so the unit is merely "always eligible,"
selected **uniformly** among eligibles by the shuffle. Weight 500 = 120 = identical odds early
game. Below the roster average weight a unit gets *throttled*; above it, no boost. Separately,
while `idleBaseUnits < IdleBaseUnitsMaximum` (12, `:25`) the module ignores weights entirely and
picks a **uniform random** buildable (`ChooseRandomUnitToBuild :167-175`), discarding picks not
in `UnitsToBuild`. Net: there is **no YAML field for a production floor/priority** — `UnitsToBuild`
is a ceiling, `UnitLimits` is a ceiling, `UnitDelays` is a delay. A guaranteed keep-N-ready
requires code (the `IBotRequestUnitProduction` queue, which is processed first each cycle and
bypasses both the share test and `UnitLimits` — `:87-92,142-165`). This is why cycle-1's TECN
starve cannot be tuned away in `ai-*.yaml`.

Code refs: `UnitBuilderBotModule.cs:78-97,112,125-136,167-195`, `TraitsInterfaces.cs:727-732`.

## 2026-07-20 — `IBotRequestUnitProduction` demand queue is a working code-level production floor (verified live, S1 cycle 2)

> **[promoted → architecture.md "AI production" (the code-level floor half)]** (curation 2026-07-20). Verified queue mechanics `UnitBuilderBotModule.cs:87-92,99-107,142-165` (pop-one-before-lottery, drop-on-failure at :90-91, `RequestedProductionCount`), reference impls `CaptureCoordinatorBotModule.cs:389-402` (`MaintainTecnFloor`, `alive+pending<floor`) and `AdaptiveProductionBotModule.cs:64,159`. Run-specific results (commit hashes, 4/10→8/10, side-split) kept here, not copied to reference.

The cycle-2 recon's proposed fix — request production through the shared UnitBuilder's queue to
bypass the share-ceiling — was **implemented and verified**: a default-off `TecnFloor` on
`CaptureCoordinatorBotModule` (merged `c6a71c14`) lifted S1 in-window capture **4/10 → 8/10** and
cut matches-fielding-zero-TECNs **5/10 → 0/10**. Confirmed mechanics from the live run:

- `bot.QueueOrder` is **not** how you pull a *unit type* on demand — you call
  `up.RequestUnitProduction(bot, name)` on each `player.PlayerActor.TraitsImplementing<IBotRequestUnitProduction>()`.
  In WW3MOD only `UnitBuilderBotModule` implements the sink; its `BotTick` pops **one** queued
  request per `FeedbackTime=30`-tick cycle **before** the lottery (`:87-92`) and routes it through
  the single-name `BuildUnit` overload (`:142-165`) that skips both `UnitsToBuild` and `UnitLimits`.
- **Drop-on-failure is real** (`:91` removes the entry whether or not the queue was free), so a
  floor must **re-request each scan** and subtract already-queued via `RequestedProductionCount`
  to avoid piling duplicates. `alive(pool) + pending(requested) < floor` is the correct gate.
- **Faction-correct build type with no hardcoding:** intersect the module's `CapturingActorTypes`
  with the player's Infantry-queue `BuildableItems()` names — the generic `~disabled` `tecn` and
  any wrong-faction variant fall out because they aren't buildable. Resolve lazily and **don't
  cache a null** (queues/prereqs may be cold on the first scan).
- **Gotcha found by running it:** gating the request at the M-2 (`idleCapturers==0`) branch means
  the floor *stops re-firing* on a bot that keeps an idle lottery-built capturer around (M-2 never
  reached). Observed as a perfect side-split: america-side fired the floor once then went quiet
  (still captured, ~1 derrick), russia-side fired 60–82× (multiple derricks). Fine for `floor=1`,
  but a stricter floor should check `alive+pending < floor` every scan, not only at M-2.

Code refs: `CaptureCoordinatorBotModule.cs` (`MaintainTecnFloor`/`ResolveTecnBuildType`/`CaptureTargetExists`),
`UnitBuilderBotModule.cs:87-92,99-107,142-165`, `AdaptiveProductionBotModule.cs:62-65,153-162` (reference impl).

## 2026-07-20 — `CVec.Length` / `CPos` subtraction is EUCLIDEAN, not Chebyshev — compute cell "grid distance" by hand

> **[promoted → conventions.md "Engine behaviors that surprise"]** (curation 2026-07-20). Verified `CVec.cs:49-50` (`Length => Exts.ISqrt(X*X + Y*Y)`).

The dispersion design sketch (`260720_dispersion_cycle_design.md` §2b/§3b) labelled
`(centroid - axis.TargetCell).Length` as "Chebyshev". It is **not**:
`engine/OpenRA.Game/CVec.cs:49-50` defines `Length => Exts.ISqrt(LengthSquared)` with
`LengthSquared => X*X + Y*Y` — i.e. rounded **Euclidean** length. Using it for the
"cells from target" gate would make a diagonal approach read ~1.4× farther than the
grid distance a watcher sees on the minimap.

For true chessboard distance in cells, `max(|dx|, |dy|)`. The dispersion implementation
adds pure helpers on `PoiOffenseMath` (`Chebyshev`, `CellCentroid`, `MaxChebyshev`) —
engine-free `(int X, int Y)` tuples, unit-tested in `PoiOffenseTest` — used for both the
assault-radius gate and the `clumpRadius` telemetry. Refs:
`engine/OpenRA.Mods.Common/Traits/BotModules/PoiOffensiveBotModule.cs`
(`PoiOffenseMath.Chebyshev/CellCentroid/MaxChebyshev`, `CommitAndOrder`).

## 2026-07-20 — Dispersion doctrine needs a kill-switch or it silently mutates the frozen `@stable` control

> **[promoted → architecture.md "Adding a behavioural field to a trait shared by both bot profiles"]** (curation 2026-07-20). Verified `PoiOffensiveBotModule.cs:87` (`CohesionSwitchEnabled=false`), `:96` (`ApproachCohesion=Spread` non-baseline default), `:424` (dispersion gated on the switch); `ai.yaml:41-46` (`@experimental`/`@stable` share the trait). Generalized into the shared-trait-default rule.

`PoiOffensiveBotModule` is instantiated by BOTH `ModularBot@experimental` (gate
`enable-ai-experimental`) and `ModularBot@stable` (gate `enable-ai-stable`, the frozen
validated snapshot — `mods/ww3mod/rules/ai/ai.yaml:44-46, 643`). New Info fields with
non-baseline **code defaults** (e.g. `ApproachCohesion=Spread`) therefore leak into
`@stable` even when its YAML block is left untouched — changing a benchmark control.
The design (§2b) anticipated this with `CohesionSwitchEnabled`; shipped it **default
`false`**, flipped `true` only on `@experimental`. Rule of thumb: any behavioural Info
field added to a trait shared by an experimental AND a frozen bot profile must default
to the frozen behaviour and be opted-in per-profile via YAML.

## 2026-07-20 — Capture escorts are dispatched but NEVER committed to the goal-guard ledger

> **[rejected: incidental bug in the experimental AI (escort desync), tied to an in-flight WORKSPACE plan and slated to be fixed by the mission model — belongs in bugs/discovered.md, not reference. The experimental goal-guard/PoiOffense layer is not documented in DOCS/reference at all. Code-verified against current source: `DispatchEscort` at `CaptureCoordinatorBotModule.cs:627-643` issues the escort AttackMove and adds to the per-tick set but never calls `Ledger.Commit`; only the capturer is committed at `:516` in `IssueCaptureOrder` (line numbers shifted from the entry's :486-502/:395-396).]** (curation 2026-07-20).

Found during the mission-abstraction costing recon (`WORKSPACE/plans/260720_mission_abstraction_costing.md`).
`CaptureCoordinatorBotModule.DispatchEscort` (`CaptureCoordinatorBotModule.cs:486-502`) issues an
`AttackMove` to the escort units and adds them to a **per-tick** `escortsRecruitedThisTick` set
(`:497-498`), but it never calls `goalGuard.Ledger.Commit`. Only the TECN itself is committed
(`IssueCaptureOrder :395-396`). Consequence: ~100 ticks later `PoiOffensiveBotModule.BuildFreePool`
(`:320-330`) sees the escorts as uncommitted and can pull them onto an attack axis, abandoning the
escort mid-approach. This is an escort *desync* distinct from — and compounding — the known F-4
bug (escort `AttackMove`s the derrick cell, not the capturer; `260720_capture_reliability_cycle1.md:71-82`).
Implication: escorts are a one-shot nudge, not a durable sub-force; the mission model fixes this by
committing the escort sub-force under `escort:<captureId>`.

Code refs: `CaptureCoordinatorBotModule.cs:486-502`, `CaptureCoordinatorBotModule.cs:395-396`, `PoiOffensiveBotModule.cs:320-330`.

## 2026-07-20 — MEASURED: 88% of experimental capture scans see ZERO TECNs (availability, not survival, gates S1)

> **[rejected: run-specific N=10 measurement on one scenario — belongs in runs/, not reference (results decay, and the run doc `260720_capture_reliability_cycle1_n10.md` already holds it). The durable takeaway (availability, not survival, is the binding constraint; the TECN pool is a consumable) is already in reference via the promoted "TECN is consumed on successful capture" entry → game-model.md.]** (curation 2026-07-20).

Instrumented N=10 confirmation of the availability hypothesis below. With the M-2
`no-idle-capturers` marker (`CaptureCoordinatorBotModule.cs`, the `idleCapturers.Length==0`
branch) preserved per-match, the pooled `total-tecns` distribution over 994 capture scans on
`tournament-s1-eco-river-zeta` (hidden Mode-B, 5min) was: **total-tecns=0 → 875 scans (88%)**,
=1 → 94, =2 → 17, =3 → 8. **5 of 10 matches had zero TECNs for the entire match and issued 0
capture orders.** The `tecn-killed` (M-1) marker fired only twice, and both with
`committed=False objective=<none>` — i.e. the TECNs that died were *not* pursuing a derrick.
So the S1 ~40% capture rate is gated by **TECN production/delivery/availability**, NOT capturer
survival on the approach and NOT coordinator logic (which fires correctly whenever a free TECN
exists — all 6 captures issued at ticks 680–1477). Raising `DefaultCommitmentTicks` 300→600 and
adding an `INotifyKilled` scan-reset (cycle 1, branch `exp-capture-reliability`) left the rate
at 4/10 — confirming the binding constraint is upstream of the capture loop. Next lever:
TECN call-in/build cadence, `ConsumedByCapture` pool drain, and a "keep N TECNs ready" floor
(UnitLimit `tecn.*: 3` is a ceiling, not a floor).

Run: `WORKSPACE/ai-bench/runs/260720_capture_reliability_cycle1_n10.md`.
Code refs: `CaptureCoordinatorBotModule.cs` (M-2 branch), `tools/autotest/run-tournament.sh`
(per-match `debug.log` preservation), `ai-{america,russia}.yaml:8` (`tecn.*: 500` builder weight).

## 2026-07-20 — TECN is consumed on successful capture (`ConsumedByCapture: true`)

> **[promoted → game-model.md — "Capturing neutral buildings consumes the technician"]** (curation 2026-07-20). Verified `infantry.yaml:897,903`.

`^CapturesNeutralBuildings` (infantry.yaml:897–905) sets `ConsumedByCapture: true`
(infantry.yaml:903). Every successful neutral-building capture removes the TECN from
the game. This means the AI's TECN pool shrinks by one on every SUCCESS as well as
every combat death. With `UnitLimits: tecn.america/russia: 3` (ai-america.yaml:37,
ai-russia.yaml:37), capturing 2–3 derricks can exhaust the live pool entirely, after
which no further captures are possible until production replaces them. Key implication
for capture-reliability design: the TECN pool is a **consumable**, not a persistent
resource — availability is the binding constraint, not coordinator logic.

Code refs: `infantry.yaml:903`, `ai-america.yaml:37`, `CaptureCoordinatorBotModule.cs:432`.

## 2026-07-20 — `PoiGoalGuard` commitment TTL (300 ticks) is borderline short for Speed-25 infantry on 8-cell routes

> **[rejected: in-flight tuning proposal tied to a WORKSPACE plan; not a timeless mechanic — the TTL value is a design knob, not reference material]** (curation 2026-07-20).

`DefaultCommitmentTicks: 300` (ai.yaml:122; PoiGoalGuard.cs:129). At `Speed: 25`
(infantry.yaml:37, `^Infantry` template inherited by `^TECN` via the chain
`^ArmedCivilian → ^CivInfantry → ^Infantry`), one cell takes `⌈1024 / 25⌉ ≈ 41` ticks.
An 8-cell edge-to-SR-to-target route takes ~330 ticks, exceeding the TTL. When the TTL
expires, `Prune()` (PoiGoalGuard.cs:104–116) drops the commitment and marks the unit
as available again. If the unit has an `IsIdle` flicker mid-walk, the coordinator can
re-issue a new capture order, aborting the in-progress approach. Fix: raise to 600
(covers ~14-cell walk). River Zeta derricks are ~3–4 cells from SR (baseline §failures);
combined edge-to-SR walk ~3–5 cells; total ~6–8 cells ≈ 250–330 ticks — borderline
at 300, safe at 600.

Code refs: `ai.yaml:122`, `PoiGoalGuard.cs:104`, `infantry.yaml:37`.

## 2026-07-20 — `CohesionMoveModifier` is a cover-aware intent system, NOT a simple offset system; and it DOES fire for bot orders

> **[rejected: correction already applied — architecture.md:161 already carries the four-strategy cover-aware description and the bot-order-routing note]** (curation 2026-07-20).

`architecture.md` description is **wrong**: "offsets group move targets based on CohesionMode
(Tight/Loose/Spread). Preserves relative formation shape with capped offsets." The real
implementation (`engine/OpenRA.Mods.Common/Traits/CohesionMoveModifier.cs`) is an
**intent-aware cover-placement system** that classifies the target cell against
`Map.DensityLayer` and dispatches to one of four formation strategies: `Open` (box
layout — fires on open terrain, the typical AI case), `SpreadInside` (into cover),
`EdgeLine` (along a cover gradient), `Approach` (boundary-anchored line for far clicks).
CohesionMode (`Tight`/`Loose`/`Spread`) controls ONLY spacing (col/row WDist), NOT
which strategy fires. For AI AttackMoves to open-terrain objectives, `Intent.Open`
almost always fires.

**Bot-order routing confirmed**: `PoiOffensiveBotModule` issues grouped AttackMove with
`groupedActors:` set. `Order.cs:400-401` serializes GroupedActors (flag `Grouped`).
`UnitOrders.cs:397-413` runs the `IModifyGroupOrder` pipeline whenever
`order.GroupedActors != null`. So CohesionMoveModifier fires for bot-issued grouped
orders exactly as for player-issued ones. AI units default to `CohesionMode.Loose`
(`AutoTarget.cs:120: InitialCohesionAI = CohesionMode.Loose`), giving 2-cell column
and 1.5-cell row spacing in the Open box — tight enough to read as a death-ball.

**`SetCohesion` order is bot-callable** (`AutoTarget.cs:434-435`):
`new Order("SetCohesion", unit, false) { ExtraData = (uint)mode }`. The bot can switch
per-unit cohesion mode before issuing a grouped AttackMove. SetCohesion orders queue
before the AttackMove and drain first (FIFO), so the modifier reads the updated mode.
This is the key mechanism for the Dispersion Cycle (§2,
`WORKSPACE/plans/260720_dispersion_cycle_design.md`).

## 2026-07-20 — Tournament scenario bot assignment lives in `map.yaml` Players, NOT in `tournament.yaml` `Matchup`

> **[rejected: already documented in-tree — the `tournament.yaml` files comment `Matchup` as "informational", and WORKSPACE/ai/archive/tournament_swap_guide.md covers swaps; harness tooling, not engine/gameplay reference]** (curation 2026-07-20). Confirmed nothing but `TournamentConfig.cs:70-71` reads the field.
- Building the S1 mirror (`tournament-s1-eco-river-zeta-mirror`) required swapping
  which bot plays which spawn. The `tournament-eco-5min.yaml` (and every scenario's
  `tournament.yaml`) has a `Matchup: { P1Bot, P2Bot }` block that *looks* like the
  assignment — but it is **informational only**: `TournamentConfig.LoadFromFile`
  parses it into `config.Matchup` and **nothing in the engine ever reads that field**
  (grep: the only references are the load site + the class def). The real assignment
  is the `Bot:` key on each `PlayerReference@…` in `map.yaml` Players. So a mirror =
  copy the folder, swap the two `Bot:` lines in `map.yaml`; leave `tournament.yaml`
  byte-identical. (The existing combat-stub mirror swaps *factions* instead, because
  S2/S3 control for faction bias; S1 controls for derrick *distance*, so it swaps the
  bot on each fixed spawn.)

## 2026-07-20 — Scorer `capture_income` term repointed net→gross; `verdict_version` 3→4 flags an emitted field's changed *meaning* (not a schema add)

> **[rejected: in-flight scorer changelog — describes a specific code change + version bump, not a durable mechanic; belongs with the commit/WORKSPACE, not reference]** (curation 2026-07-20).
- `WeightedComponentMatchScorer.capture_income` (which feeds `TimeOrSrCaptureWinRule`,
  i.e. match *outcomes*) previously read net `PlayerResources.Earned`. In the
  SR-budget economy net Earned only rises on a net-positive periodic tick, so a held
  $50 derrick whose gross income doesn't overcome upkeep contributed **0** — outcomes
  were blind to captured income (the same defect the S1 metric fixed at v3). It now
  reads the gross integral via `state.GrossCaptureIncomeFor(player)` (the same value
  emitted as `capture_income_gross`), so the scorer reads `MatchTrackingState`, not
  just player traits. **Non-obvious versioning rule applied:** no JSON field was
  added or removed, but the *value/meaning* of an already-emitted field
  (`score_components.capture_income`) changed, so `verdict_version` was bumped 3→4.
  Bump on emitted-field-meaning change, not only on field add/remove — a downstream
  parser keyed on `verdict_version` must know the economy column now means gross.
- The weighting math was factored to a pure `WeightedComponentScoring.Compute` so it's
  unit-testable without a World (same pattern as `PoiScoring`/`GoalGuardLedger`):
  `WeightedComponentScoringTest` pins `capture_income == gross × weight`. This
  supersedes the 2026-07-19 note below that the scorer "reads … `PlayerResources.Earned`".

## 2026-07-19 — Tournament matches are NOT reproducible per seed: the AI ignores the seed via unseeded `world.LocalRandom`

> **[promoted → architecture.md — "Bot decisions are not seed-reproducible"]** (curation 2026-07-20). Verified `World.cs:213-214`.
- Verified empirically: two `BotVsBotMatchWatcher` runs of the SAME scenario
  (`tournament-arena-diagonal-2p`/`tournament-smoke.yaml`) with the SAME
  `Test.RandomSeed=1017` produced **different** winners and scores. Divergence
  starts within the first 125 ticks from an identical initial state (same SR
  positions, same players). `duration_ticks` (750, the fixed time limit) is the
  only thing that matches.
- **Root cause:** `World.cs:213` seeds `SharedRandom` from `RandomSeed`
  (deterministic, network-synced), but `World.cs:214` creates
  `LocalRandom = new MersenneTwister()` **unseeded**. The bot modules make
  *decisions* off `world.LocalRandom` — `UnitBuilderBotModule.cs:173/188` picks
  which unit to call in; LayeredDefence / HelicopterSquad / BaseBuilder /
  Minelayer / SupportPower all use it for scan timing and target/ location
  choice. Unseeded → different picks every run → army composition (and thus
  `army_value`/scores) diverges immediately.
- **Consequence:** the `Test.RandomSeed` "reproducible per seed" claim
  (PITFALLS §15) is only true for the *synced* sim, NOT for AI behavior. For a
  benchmark substrate this means a fixed seed gives you a *sample*, not a
  *reproduction*. Sample-over-N stays statistically valid; single-match
  reproduction/debugging does not work.
- **Fix (separable, not done here):** under `TestMode.RandomSeedOverride`, seed
  `LocalRandom` too (e.g. `new MersenneTwister(RandomSeed ^ constant)`), or route
  AI decision randomness through `SharedRandom`. Until then, do not expect
  bit-identical tournament verdicts across runs.
- **Corollary for `OPENRA_WINDOW_HIDDEN`:** the hidden-window flag does NOT
  change sim results — it only removes SDL rendering, which is decoupled from the
  lockstep sim and cannot touch `LocalRandom`. The hidden-vs-windowed divergence
  observed during flag verification is entirely this pre-existing AI
  nondeterminism, not the flag. (Flag verification: hidden run created no visible
  window, stole no focus, completed, and wrote the v2 verdict JSON.)

## 2026-07-19 — Bot-vs-bot benchmark substrate: harness already exists but is macOS-gated; no headless mode; a hidden-window flag is the crux

> **[rejected: in-flight research findings — full report already lives in WORKSPACE/plans/260719_ai_benchmark_substrate_findings.md; status/effort notes, not durable reference]** (curation 2026-07-20).
- Researching a foundation for an **autonomous AI benchmark** (many unsupervised bot-vs-bot games, metrics from logs) surfaced that most of it is **already built**: `tools/autotest/run-tournament.sh` + `loop-tournament.sh` + `aggregate-tournament.sh` run N seeded matches, aggregate to CSV/JSON, and drive a milestone loop (winrate/budget stop-conditions). Engine side: `BotVsBotMatchWatcher` (world trait) writes a per-match JSON verdict (winner, win_reason, duration, per-player score_total + components); `WeightedComponentMatchScorer` already reads live `PlayerStatistics.ArmyValue/KillsCost` + `PlayerResources.Earned` (the `tournament.yaml` "only army_value" note is stale). 7 tournament scenarios exist incl. v2-vs-normal.
- **Two blockers for the user's Windows goal:** (1) the whole harness is `.sh` + `uname` Darwin/Linux branches + `osascript` focus mitigation — **Windows is unhandled**; (2) **no headless mode** — only one `IPlatform` (`DefaultPlatform`), the SDL window is always shown (`Sdl2PlatformWindow.cs:227`, no `SDL_WINDOW_HIDDEN`), and on Windows it **steals focus** with no mitigation. The dedicated server can't substitute — it's order-relay only (`OpenRA.Server/Program.cs:100-109`, no `World`); bots tick client-side (`ModularBot.cs:86`).
- A true headless/null renderer was **explicitly rejected** (`WORKSPACE/ai/archive/PITFALLS.md §17`) as "days of work, risk of breaking determinism" — but that call was made for macOS where `osascript` already tamed focus. On Windows the calculus flips. **Cheapest fix: ~10-line `OPENRA_WINDOW_HIDDEN=1` env flag adding `SDL_WINDOW_HIDDEN` at window creation** — no-window + no-focus-theft in one stroke, keeps a real GL context (unlike a null platform).
- **Speed:** `GameSpeed` caps at 2×; real lever is `Test.SpeedMultiplier` (1–16, lowers `world.Timestep`), 4–6× practical with render on (renderer is the ceiling; ~30s fixed init dominates short matches). **Seeds:** `Test.RandomSeed` override makes matches reproducible per seed (`PITFALLS §15`); vary for a sample, fix to reproduce.
- **Riskiest unverified assumption:** that a hidden SDL window ticks the sim to completion on Windows with identical (deterministic) results. Retire with one bounded run after the flag lands.
- Full report + effort estimates: [`plans/260719_ai_benchmark_substrate_findings.md`](plans/260719_ai_benchmark_substrate_findings.md).

## 2026-07-19 — SUPPLYROUTE is NOT capturable today; the doc's "capture → Neutral" is a misread of OwnerLostAction

> **[promoted → supply-route.md (§Capture rewrite + engine-integration bullets) & game-model.md; drove on-sight fixes to both]** (curation 2026-07-20). Verified `structures.yaml:202-343` (no Capturable/CaptureManager), `OwnerLostAction.cs`, `ConquestVictoryConditions.cs:109` / `StrategicVictoryConditions.cs:152`.
- The game-model docs (`DOCS/reference/supply-route.md` §Capture, `game-model.md`) state an enemy SR can be captured by an engineer/technician and flips to Neutral. **This does not work in-game.** SUPPLYROUTE has **no `Capturable` and no `CaptureManager`** — not in its own block (`mods/ww3mod/rules/ingame/structures.yaml:202-343`), not in any template it inherits (`^ExistsInWorld`, `^SpriteActor`, `^SelectableBuilding` — all clean; `defaults.yaml:2-13, 772-775`), and not patched by any map/world/ai/campaign rules (checked). The Phase-2 AI worker's report was correct.
- **The doc conflates two unrelated mechanisms.** `OwnerLostAction: ChangeOwner → Neutral` (structures.yaml:227-229) does NOT fire on capture. `OwnerLostAction` implements `INotifyOwnerLost` (`engine/OpenRA.Mods.Common/Traits/OwnerLostAction.cs:20,42` — "when the actor's owner is **defeated**"), and `OnOwnerLost` is called **only** from `ConquestVictoryConditions.cs:109-110` and `StrategicVictoryConditions.cs:152-153`, both iterating the actors of a just-defeated player. So an SR goes Neutral **only when its owning player loses the game**, never via an engineer.
- **Capturer side is fully wired, target side is not.** TECN inherits `^CapturesNeutralBuildings` = `CaptureManager` + `Captures{CaptureTypes: building-neutral}` (`infantry.yaml:2164, 897-904`); soldiers get `^CapturesOccupiedBuildings` (`building-occupied`, 885-896). Capturable tech buildings (OILB/FCOM/BIO…) get the matching side via `^BasicBuilding → ^NeutralOrOccupiedCapturable` (`structures.yaml:2-10, 149-157`: `Capturable@neutral: building-neutral` + `Capturable@occupied: building-occupied`). **SUPPLYROUTE inherits none of that chain**, so there is no capture-type to intersect — TECN literally has nothing to enter/capture on an SR (neutral or enemy).
- **Verdicts:** (a) enemy SR → capturable → flips Neutral = **ABSENT**. (b) neutral SR → capturable by a player (gain a 2nd reinforcement lane) = **ABSENT** — the harness `NeutralSR` (`test-v2-poi-harness/map.yaml:173`) is a plain `supplyroute`/Neutral actor with no Capturable; its `rules.yaml` adds none.
- **Gap to match the stated design:** add `CaptureManager` + a `Capturable` to SUPPLYROUTE. Note two subtleties: (1) neutral-SR capture needs `Types: building-neutral`; enemy-SR needs `building-occupied`. (2) **Standard `Captures`/`Capturable` transfers to the CAPTURER, not to Neutral** — so the "capturer can never use it, it just goes Neutral" design cannot be done with vanilla capture traits alone; it needs a custom on-capture hook (or `OwnerLostAction`-style flip triggered by capture). The commented-out `CaptureNotification` at structures.yaml:216-217 is unrelated and wires nothing.
- No live test was needed — the YAML+C# reading is unambiguous (no Capturable anywhere on the actor). A run could only confirm the negative.

## 2026-07-20 — PoiMap enemy-SR score: three factors conspire to keep it last in offensive ranking

> **[rejected: in-flight design analysis tied to WORKSPACE/plans/260720_sr_contestation_cycle1.md — concrete map numbers + a proposed fix direction, not a stable mechanic]** (curation 2026-07-20).

Computed from `PoiMap.GetOffensiveTargets` (PoiMap.cs:279) + world.yaml PoiMap block (line 296):

**Enemy SR score formula:** `value × distFactor × threatFactor × ownershipMul × bias/100`
- `SupplyRouteDenyValue = 120` (world.yaml:305)
- `distFactor = 20×100/(20+dist)` → on River Zeta (spawn-to-spawn ~95 cells): **17**
- `threatFactor` → enemy SR always has enemy troops nearby → mild=40 or hostile=10
- `OffensiveEnemyAttackBias = 80` (shared with enemy income buildings, below 100)

River Zeta concrete numbers (P1 SR at (15,6), P2 SR at (80,76)):
- Enemy SR, mild threat: 120×17×40×100×80/100 = **6.5M**
- Enemy SR, hostile: 120×17×10×100×80/100 = **1.6M**
- Nearest neutral oilb (dist 3, safe): 50×87×100×100×150/100 = **65M**
- Mid-distance neutral oilb (dist 46, safe): 50×30×100×100×150/100 = **22.5M**

**The enemy SR never enters any axis with the current config.** With MaxAxes=4 and a
32-unit army, the top-4 offensive targets are always neutral oilbs. The SR would only
rank in the top-4 after all neutral oilbs are captured — at which point the game is
almost certainly decided.

**Root cause — three structural factors, not a single tuning miss:**
1. **Distance:** the SR is always at max distance (enemy spawn edge). At 95 cells,
   distFactor=17 gives 17% of a local-POI score. Half-life of 20 cells was designed for
   income (closer = less travel time) but the SR position is fixed.
2. **ThreatFactor semantics are inverted for Pressure:** `ThreatFactor` hostile=10 was
   designed to deter lone TECNs from risky captures — but it also deters the entire
   army from SR pressure. For Pressure, enemy presence near the SR is an *opportunity*
   (garrison is there to be contested), not a deterrent. The existing threat gate is
   intentionally kept for Cycle 1 (it prevents suicide pushes at defended SRs) but the
   semantics mismatch is a known design tension.
3. **OffensiveEnemyAttackBias=80 conflates Pressure (SR) with Attack (enemy income):**
   the below-100 bias was correct for "don't rush enemy income before securing own income"
   but wrong for the SR, which is the highest-value strategic objective in the game model.

**Fix direction (Cycle 1):** raise `SupplyRouteDenyValue: 120→250` + split off a dedicated
`OffensiveSrPressureBias: 100` field from the shared OffensiveEnemyAttackBias=80. This
raises mild-threat SR score to 17M (competitive in top-4 mid-game) while hostile threat
(4.25M) still prevents suicide pushes. Pure YAML + ~6 lines C#. Full design note:
`WORKSPACE/plans/260720_sr_contestation_cycle1.md`.

## 2026-07-19 — Bot skirmish maps produce no army without a scenario applied

> **[rejected: AUTOTEST test-setup methodology — belongs in DOCS/recipes, not engine/gameplay reference; scenario application itself is visible at World.cs:216-222]** (curation 2026-07-20).
- Ran a bounded v2-vs-normal capture skirmish (`test-v2-poi-observe`, a bounded
  copy of `demo-v2-capture-coordinator`) for 55s to capture live AI logs. The v2
  bot built **nothing**: `[v2-poi] disperse pool=0 contested=0` for the whole
  run and **zero** `[v2-capture]` lines (no TECNs ever produced). Engine logged
  `Scenario selection: 'none', available scenarios: []`.
- **Takeaway:** the SR reinforcement/production pipeline is (at least partly)
  scenario-gated. A skirmish map that just places SRs + bots does NOT make the
  bots call in units in a short window — so it's useless as a runtime AI-behaviour
  observation vehicle. Any future live AI trace (death-ball, spread offense,
  capture) needs a harness where the scenario/production system actually feeds
  the SR queue, plus a longer window than ~1 min. Confirm what applies a scenario
  before relying on bot production in autotests.
- Logs land in `AppData/Roaming/OpenRA/Logs/debug.log` on Windows, and it
  **rotates per run** (truncated on each launch) — snapshot/grep right after.
- Unaffected: the `[v2-poi]` diagnostic itself works and emits clean per-scan
  lines; the death-ball root cause is confirmed structurally in code regardless
  (see plan 260719 Phase 0 findings).

## 2026-05-18 — Handicap unreachable in the V5 player row (deferred until usage data exists)

> **[rejected: WORKSPACE/lobby v1-cut decision (deferred to v1.1) — tracker material, tied to WORKSPACE/lobby/decisions.md]** (curation 2026-07-20).
- The V5 player row (`engine/mods/common/chrome/lobby-players.yaml`) keeps `DropDownButton@HANDICAP_DROPDOWN` and `Label@HANDICAP` widgets in every template, but parks them at `X: -200 W: 1 H: 1` so the C# `Get<>()` calls in `SetupEditableHandicapWidget` still resolve while nothing paints. The column was dropped in phase 5 redesign — agreed in `WORKSPACE/lobby/decisions.md` as a deliberate v1 cut.
- **Net effect:** the handicap mechanic still works (server orders, etc.) but players cannot SEE or CHANGE their handicap value from the lobby. Default applies.
- **Access path options when re-introducing** (per `IMPLEMENTATION_PLAN.md` Phase 8): right-click context menu on the player row; expandable detail row; spawn-cell dropdown overload; drop entirely if usage data shows it's unused.
- **Decision deferred to v1.1** — needs usage telemetry first. Bot-vs-bot tournaments and human skirmishes don't touch handicap today, so impact is low.

## 2026-05-18 — Empty MiniYaml values must be a bare trailing colon, not `""`

> **[promoted → conventions.md — "Disabling a string field: bare colon, not \"\""]** (curation 2026-07-20). Verified `FieldLoader.cs:161` + `DropDownButtonWidget.cs:71-73`.
- `Separators: ""` parses as the literal 2-char string `""` (FieldLoader.ParseString returns the raw value). It then fails `IsNullOrEmpty` inside DropDownButtonWidget.Draw, and `WidgetUtils.GetCachedStatefulImage("\"\"", "separator")` throws `Sprite ""/separator was not found`.
- Correct form: `Separators:` (bare trailing colon) — the parser treats it as a null string, IsNullOrEmpty fires, the lookup is skipped.
- Applies to any chrome/widget string field where you want to disable a feature by clearing it (Background, Decorations, Separators, TooltipText).

## 2026-05-13 — CohesionMoveModifier feels broken because EdgeLine looks identical to the old box

> **[rejected: in-flight feel-bug diagnosis (specific test probes + slot-bidder bugs) — the mechanism spec is already in architecture.md:161; diagnosis belongs in WORKSPACE]** (curation 2026-07-20).
Autotest-driven diagnosis on real river-zeta (`test-cohesion-river-zeta-actual`, 12 probes spanning open ground / sparse fringe / dense cluster / cross-map clicks) produced the [Cohesion] log lines below. Three things, in priority order:

1. **EdgeLine is the dominant intent for near-cover clicks (totalDensity 70–530), and it produces a perfectly straight perpendicular line of slots.** That visual output is indistinguishable from "spread to a line oriented along the move direction" — exactly the legacy box behavior the user thinks is broken. SpreadInside (the cluster-around-best-cover layout) only fires for clicks DEEP in dense cover (centroid offset < ~1.4 cells). Most natural clicks are at the edge of a cluster or 1–3 cells outside it — those resolve to EdgeLine.

2. **EdgeLine slot cells are picked geometrically, not by CoverScore.** `ComputeEdgeLineSlots` walks the perpendicular axis at fixed spacing and `NudgeToPassable`s impassable cells back along the gradient. There is no "of the cells near my ideal slot, pick the one with the highest CoverScore" step. So slots routinely land between trunks rather than behind them.

3. **Approach has a logic bug when the group is already adjacent to a cover patch.** `ComputeApproachSlots` walks `step = 1..maxSteps` from group centroid toward click and stops at the first cell with `CoverScore > 0`. If there's any cover immediately east of the group, Approach finds it at step 1 and anchors the formation right there — even when the click is 50+ cells away. In the river-zeta probes, clicks to (68,20), (80,75), and (10,75) all produced slots in the (22–26, 31–39) box (right next to the A cluster) because the squad was sitting on A's west edge. Units never reach the click.

`Open` is rare and not the user's complaint — it only fires when totalDensity in the 9×9 window is 0, which on river-zeta is genuinely-open ground. The classifier itself is calibrated reasonably; the issue is the **slot bidders downstream of the classifier**.

Other notes: DensityLayer is populated correctly (trees contribute density=10 to one trunk cell via `Building.Density`; `BlocksSight` has `IDensityInfo` commented out — only Buildings contribute). The `IModifyGroupOrder` dispatch works for every Test.GroupMove probe (the older "1 of 8" datapoint must predate a fix). Diagnostic log line restored at the bottom of `CohesionMoveModifier.ModifyGroupOrder` (idx==0) — strip when the feel issue is resolved.

## 2026-05-09 — AttackTurreted overrides CanAttack and short-circuits before base

> **[promoted → conventions.md — "Engine behaviors that surprise"]** (curation 2026-07-20). Verified `AttackTurreted.cs:36-48`.
- `AttackTurreted.CanAttack(self, target)` returns `turretReady && base.CanAttack(self, target)`. When `turretReady = FaceTarget(target)` is false (turret mid-rotation), `base.CanAttack` is never reached. So traces / breakpoints in `AttackBase.CanAttack` won't fire if the turret hasn't finished aiming. If you're trying to debug "why isn't this unit firing", check `AttackTurreted.cs` first — the answer is often "turret hasn't pointed at the target yet".

## 2026-05-09 — Activity.IsCanceling is always false inside OnLastRun

> **[promoted → conventions.md — "Engine behaviors that surprise"]** (curation 2026-07-20). Verified `Activity.cs:84,132-135`.
- `Activity.TickOuter` sets `State = ActivityState.Done` *before* calling `OnLastRun(self)`. `IsCanceling` is `State == ActivityState.Canceling`, so by the time OnLastRun runs, the cancel flag has been cleared. Useless for "did we end naturally vs cancelled". Better signals: check `NextActivity is X` (a queued activity behind us implies we were replaced), or compare `attack.RequestedTarget` to our own `target` field (someone else has already set the new target if they differ).

## 2026-05-09 — Build cache occasionally skips single-file edits; touch + make to force

> **[rejected: dev-workflow anecdote (macOS `make`/`touch`, "occasionally") — low-confidence build tip, not an engine/gameplay mechanic]** (curation 2026-07-20).
- `make` reports success even when a single .cs file's edit didn't make it into the DLL. Symptoms: traces don't fire, behavior unchanged, build log says `0 errors`. Fix: `touch <file>.cs && make`. Catches incremental-build dependency-tracking misses. Cost a couple of wasted runs in the artillery debugging session before recognizing the pattern.

## 2026-05-09 — Test mode trace pattern: gate on Game.LocalTick % N == 0

> **[rejected: AUTOTEST recipe tip — a debugging technique that belongs in DOCS/recipes, not reference]** (curation 2026-07-20).
- For "I want one trace per second, not 25 per tick" diagnostics during AUTOTEST: `if (TestMode.IsActive && Game.LocalTick % 25 == 0) Console.WriteLine(...)`. Pairs with the runner stdout capture at `/private/tmp/claude-501/.../tasks/<id>.output` — grep that file post-test. Strip all of these before committing the fix.

## 2026-05-03 — GrantConditionOnPrerequisite: ownership-change crash (upstream OpenRA bug)

> **[rejected: resolved-bug changelog — the fix already landed (GrantConditionOnPrerequisite.cs:62-76 unregisters/re-registers on owner change); nothing left for a future agent to act on]** (curation 2026-07-20).
- `GrantConditionOnPrerequisiteManager` is a per-player trait — each player has their own dictionary of `{key → list of (actor, trait)}`. `GrantConditionOnPrerequisite` registers the actor with its initial owner's manager in `AddedToWorld`, but the original `OnOwnerChanged` only rebound the cached manager reference without unregistering from old / registering with new. Result: after any in-world ownership change (capture, `OwnerLostAction: ChangeOwner Owner: Neutral`, garrison transfer, scenario transfer), `RemovedFromWorld` calls `Unregister` on the wrong dictionary → `KeyNotFoundException: condition_<prerequisite>`. First seen with LOGISTICSCENTER + `global-mcv-undeploys` after a player was defeated. Fix in `engine/OpenRA.Mods.Common/Traits/Conditions/GrantConditionOnPrerequisite.cs`: `OnOwnerChanged` now unregisters from the old manager and re-registers with the new one (when in world). Also fixes a memory leak (old manager kept dangling reference) and the silent correctness bug where the new owner's tech tree wouldn't drive the actor's condition.

## 2026-03-23 — OpenRA maps MUST have `Rules: rules.yaml` in map.yaml

> **[promoted → conventions.md — "Maps must declare Rules: rules.yaml"]** (curation 2026-07-20). Verified `Map.cs:176,364`.
- Without the `Rules: rules.yaml` line at the top level of map.yaml, OpenRA silently ignores rules.yaml entirely. This means LuaScript references, AutoTarget overrides, and all rule modifications are never loaded. The map appears to work (actors spawn, terrain renders) but Lua never executes and rule overrides don't apply. The MCP map tool was missing this — now fixed in set_map_rules.

## 2026-03-23 — ReloadAmmoPool FullReloadTicks/FullReloadSteps are dead code

> **[rejected: stale/wrong against current code — `ReloadAmmoPoolInfo` has no such fields (ReloadAmmoPool.cs:18-44); `FullReloadTicks`/`FullReloadSteps` exist and are actively used + unit-tested only on `AmmoPoolInfo` (AmmoPool.cs:29,32,225-234; AmmoPoolTest.cs). No dead code to document.]** (curation 2026-07-20).
- `ReloadAmmoPoolInfo` has `FullReloadTicks` and `FullReloadSteps` fields, but they're never read in code. `ReloadAmmoPool.Tick()` calls `ammoPool.Reload(self, Info.Delay, Info.Count)` which uses `Delay` (50) and `Count` (1). The `FullReloadTicks`/`FullReloadSteps` on *AmmoPoolInfo* (not ReloadAmmoPoolInfo) ARE used inside `AmmoPool.Reload()`, but the identically-named fields on ReloadAmmoPoolInfo do nothing. Many YAML entries set these thinking they matter (e.g., `ReloadAmmoPool@1: FullReloadTicks: 200`). Either implement them or remove from YAML.

## 2026-03-23 — SupplyProvider ammo-per-cycle scaling matters

> **[rejected: superseded by economy.md's `ReloadCount` batch model — this describes an older `max(1, poolCapacity/50)` fix that the current per-batch economy replaced; changelog]** (curation 2026-07-20).
- SupplyProvider was giving 1 ammo per RearmDelay cycle regardless of pool capacity. For an AR soldier with 500 ammo capacity, this took 5+ minutes to fill. Fixed to give `max(1, poolCapacity/50)` per cycle (~50 cycles from empty). Also added MinNeedThreshold (5%) to skip nearly-full units.

## 2026-03-21 — IProductionSpeedModifier pattern

> **[rejected: already covered — architecture.md's `SupplyRouteContestation` trait row names IProductionSpeedModifier; deeper interface mechanics are implementation detail]** (curation 2026-07-20).
- Created `IProductionSpeedModifier` interface for dynamic per-tick production speed control. Unlike `IProductionTimeModifierInfo` (which only applies at production START), this uses an accumulator pattern in `ProductionQueue.TickInner` to skip ticks proportionally. Returns 0-100 (percentage). Both `ProductionQueue` and `ClassicParallelProductionQueue` support it. The modifier is queried from producing buildings (not the player actor), via `ActorsWithTrait<Production>()` iteration.

## 2026-03-21 — Supply Route contestation replaces ProximityContestable

> **[rejected: already covered — architecture.md:159 (SupplyRouteContestation trait) + supply-route.md contestation section]** (curation 2026-07-20).
- The old `ProximityContestable` trait was binary (any enemy = full production halt, no feedback). Replaced with `SupplyRouteContestation` which uses value-based force comparison, graduated depletion/recovery, and `IProductionSpeedModifier` for smooth production slowdown. Key design: bar stored as int 0-100000 for precision, depletion formula `ticksToDeplete = max(MinTicks, BaseTicks * RefValue / netSurplus)`.

## 2026-03-21 — Initial setup

> **[rejected: trivial project-setup note, no reference content]** (curation 2026-07-20).
- Created WORKSPACE/ project folder for session tracking, plans, discoveries, and bug captures.

## 2026-03-21 — MCP map actor facing

> **[rejected: already covered — conventions.md "WAngle facing" table (0=N, 256=W, 512=S, 768=E) + CLAUDE.md]** (curation 2026-07-20).
- Actor `Facing` field in map.yaml must be a WAngle integer (0-1023), not a compass string like "East". The MCP `place_actors` tool passes it through as a string, so use: **0=North, 256=West, 512=South, 768=East** (counterclockwise — see `~/.claude/projects/.../memory/feedback_facings.md` and CLAUDE.md). Using "East" crashes on map load with `FieldLoader: Cannot parse 'East' into 'value.OpenRA.WAngle'`.
- (Corrected 2026-05-06 — earlier version of this entry had the directions wrong.)

## 2026-06-18 — autotest/screenshot scripts need `python3` on PATH

> **[rejected: machine-specific environment fix (this Windows box's PATH) — not portable project reference]** (curation 2026-07-20).
- `launch-game.sh` (used by `tools/autotest/screenshot-lobby.sh`, `screenshot.sh`, etc.) requires `python3` (or `python`) — it shells out only to resolve its own realpath. On this Windows box the only `python3` on PATH was the WindowsApps Store stub, which prints "Python was not found" and exits non-zero, so every launch died with "game process exited before lobby was ready" (no logs, because it never reached engine init).
- Fix (permanent): real Python lives at `C:\Python314` (admin-protected, can't drop files there). Created `C:\Users\fredr\bin\python3.exe` (copy of `C:\Python314\python.exe` — a bare copy still finds its stdlib via the PEP 514 registry landmark) and prepended `C:\Users\fredr\bin` + `C:\Python314` to the **user** PATH *ahead of* WindowsApps. Wrote the registry value as `REG_EXPAND_SZ` to preserve the existing `%USERPROFILE%` entries. New terminals pick it up automatically; already-running processes need a restart.

## 2026-07-18 - Lobby finishing pass: three engine gotchas

> **[promoted → architecture.md — "Widget / chrome authoring gotchas"]** (curation 2026-07-20). Verified `ImageWidget.cs:31,61,78-91`, `ButtonWidget.cs:320-323`, `Widget.cs:229-231`.
- **ImageWidget draws sprites at native size** - Width/Height are layout-only; `WidgetUtils.DrawSprite(sprite, origin)` ignores widget bounds. The "flag fills height" commit (0100022f) was a silent no-op for months. Added opt-in `ScaleToBounds: True` (uniform scale, centered, 3-arg DrawSprite overload) - remember to mirror new fields in the widget copy-constructor or template clones lose them.
- **ButtonWidget silently draws nothing for missing chrome variants** - a highlighted button looks up `<Background>-highlighted` (+ `-hover`/`-pressed`/`-disabled` suffixes); if the collection is absent, `WidgetUtils.DrawPanel` early-returns with no error. Our active tabs rendered with NO fill while inactive ones kept theirs (inverted emphasis). Any custom `Background:` needs the full variant set - `lobby-button-highlighted*` added 260718.
- **Hidden widgets keep keyboard focus** - `Widget.HandleKeyPress` only checks the focus widget's OWN `IsVisible`, not its ancestors. The inline map chooser's filter TextField kept focus while its parent tab was hidden: chat field dead, and Enter could silently fire the chooser's onSelect (= change the map). Pattern: any tab-switch that hides a focused widget must hand focus off explicitly.

## 2026-07-20 — LADDER S2/S3 doc is stale post-determinism (S2 EXPAND recon)

> **[rejected: concerns WORKSPACE ladder/spec tracker docs (LADDER.md/SPEC.md), not DOCS/reference — and already RESOLVED by the S2 standup cycle per the note below. The underlying determinism fact is separately promoted → architecture.md.]** (curation 2026-07-20).

- **LADDER.md's S2/S3 rows describe a superseded map + a broken-determinism world.** Found while designing the S2 rung (`WORKSPACE/plans/260720_s2_expand_design.md`):
  1. **Map:** LADDER.md:238, :279, :341-342 assign S2 (Force Efficiency) and S3 (Win-rate) to the `tournament-experimental-vs-normal-2p` **66×34 combat stub** — the same bare, zero-capturable map (`grep -c oilb|Capturable` = 0) whose lack of POIs pinned S1's economy metric to 0/0 before the River Zeta rescope (LADDER.md:76-93). Putting S2/S3 on a *different* map than S1 contradicts the rung model ("a rung is one map", LADDER.md:33-36) and the composite gate ("one commit passes all three on that map", §6.4). The S2 design recommends moving S2/S3 onto the River Zeta rung.
  2. **Determinism:** LADDER.md:48-56 and SPEC §3.2 (SPEC.md:207-220) and REVIEW.md:133-136 still state seeds are "run labels, not reproducibility guarantees" and per-seed replay is "broken" because bots draw from an unseeded `LocalRandom`. **This is now false** — `LocalRandom` is seeded (World.cs:213-214) and same-seed→byte-identical verdict was VERIFIED (commits `2d3c8fe0` engine + `f3a61d9d` docs; REVIEW.md:55 activity log). The fixed per-index seed set (run-tournament.sh:282) now makes comparisons *paired*, which the S2 bar exploits.
- **Action:** the S2-implementing cycle should update LADDER.md's S2/S3 rows (map → River Zeta rung; metric wording) and reconcile the "seeds are labels" language in LADDER §Metric-extraction + SPEC §3.2 + REVIEW Open Questions with the shipped determinism. Not fixed in this read-only recon (would touch curated ladder/spec state mid-batch); flagged here per the knowledge-bank rule.
- **RESOLVED (2026-07-20, S2 standup cycle):** both reconciled. (1) LADDER S2 row + Scenario-registry now point to the new `tournament-s2-combat-river-zeta` (River Zeta rung, 720s clock); the 66×34 `tournament-experimental-vs-normal-2p` stub is retired from the ladder; S3 row flagged "reuse River Zeta rung, scenario TBD at standup". (2) LADDER §Metric-extraction + SPEC §3.1/§3.2 rewritten to state per-seed replay is deterministic (`2d3c8fe0`, verified byte-identical), with the anti-overfit "don't tune to the fixed 10 seeds" caveat carried in. REVIEW Open Questions left for the CALIBRATE-result update.

## 2026-07-20 — SR-contestation tunables can't live on the world `PoiMap` trait (SR-contest recon)

> **[rejected: application of the already-promoted shared-trait-defaults rule (architecture.md §Adding a behavioural field to a shared trait) to PoiMap, plus the already-promoted SUPPLYROUTE deny-only fact (supply-route.md); recon tied to WORKSPACE/plans/260720_sr_contestation_cycle1.md]** (curation 2026-07-22).

- **`PoiMap` is a world singleton; any SR-scoring tunable on `PoiMapInfo` is global to every bot profile.** Both `PoiOffensiveBotModule@experimental` (ai.yaml:175) and `@stable` (ai.yaml:662) consume the *same* `PoiMap.GetOffensiveTargets` output (`PoiMap.cs:279`), so raising `SupplyRouteDenyValue` or adding an `OffensiveSrPressureBias` **in `world.yaml:296` changes @stable too** — silently mutating the frozen benchmark control. This is exactly what the shared-trait-defaults rule forbids (`DOCS/reference/architecture.md:309`: behavioural Info fields on a shared trait must default to frozen behaviour and be opted in **per-profile via YAML**).
- **Consequence for @experimental-only scoring changes:** a per-profile knob must live on the per-bot trait (`PoiOffensiveBotModuleInfo`), not on `PoiMapInfo`. Pattern to mirror: `CohesionSwitchEnabled` (default `false`, flipped `true` on @experimental only, `PoiOffensiveBotModule.cs:87/:424`). For SR pressure specifically, a single per-bot `SrPressureScoreMultiplier` (x100, default 100 = inert) applied to `PoiAction.Pressure` axes after `GetOffensiveTargets` reproduces a global `value 120→250` + `bias 80→100` change with multiplier `(250·100)/(120·80)=260`, while leaving @stable byte-identical. Verified against constants on `1594ffa1` (`SupplyRouteDenyValue=120`, threat 100/40/10, `OffensiveEnemyAttackBias=80`, `DistanceHalfLifeCells=20`): frozen SR mild 6.528M ×2.604 = 17.0M, safe 42.5M, hostile 4.25M.
- **Deny-only invariant re-confirmed on current main:** `SUPPLYROUTE` has no `CaptureManager` (`PoiMap.cs:219-222`); Pressure emits `AttackMove` to the SR cell (`PoiOffensiveBotModule.cs:467`), not `CaptureActor`; `GetCaptureTargets` (`PoiMap.cs:257-260`) filters Pressure out of the capture layer. The dispersion cohesion switch is action-agnostic (`:424-425`, gates on distance only), so it applies to a Pressure axis with no special-casing.

## 2026-07-21 — Regime re-baseline: the benchmark caught up to the bot (Motorized / US-US / vs @stable)

> **[rejected: run-specific benchmark writeup (43 matches) belonging to WORKSPACE/ai-bench/runs/260721_regime_rebaseline.md + REVIEW; benchmark methodology for the ai-bench area (out of scope for this pass), not engine/gameplay reference]** (curation 2026-07-22).

Full data: `WORKSPACE/ai-bench/runs/260721_regime_rebaseline.md`. First re-baseline after the
2026-07-21 regime change (`60b93501`). 43 matches, 0 crashes / 0 no-verdict.

- **Same-faction Stable-vs-Stable frequently DOESN'T FIGHT.** The S2 combat calibration (720s,
  both `@stable`, both `america`, Motorized start) had **3/10 matches with literally zero combat**
  (seeds 3017/7017/10017, both sides 0/0 swing) and 5/10 negligible; engagement-volume median
  collapsed to **1200/1925** vs the `[pre-regime]` Normal-vs-Normal **7475/5950** (~5–6× less).
  Score is still decided (720s of derrick income dominates), so matches resolve — but the S2
  net-swing metric is low-signal. **The zero-combat seeds recur identically in the Exp-vs-Stable
  baseline**, i.e. it's a seed×map property (those battlefields don't force contact), not a bot
  property. Implication: an S2 batch on this regime needs a batch-validity gate (≥6/10 engaged)
  or a forced-contact rescope, else "force efficiency" is measured on games with no force spent.
- **Motorized start makes BOTH bots capture — the `[pre-regime]` "control captures ~0" premise is
  dead.** Under `startingunits=none` the Normal control captured ~1/20 (S1 control gross median ≈0,
  which made the old ×1.15 bar degenerate). Under Motorized/same-faction, the Stable control
  captures **6/10** (gross median ~6100) — as often as Experimental. So S1's discriminator can no
  longer be "does Exp capture reliably" (both do); it must be "does Exp out-capture / out-win the
  control". Capture is strongly **spawn-dependent**: the USA slot (14,45) captures ~5/5, the Russia
  slot (80,35) ~2/5 — a derrick-distance effect symmetric across bots, cancelled by the mirror.
- **S1 spawn lean 7–3 Russia-slot, but S2 spawn EVEN 5–5.** Identical-bot calibrations: at the 300s
  economy clock the Russia slot (80,35) wins 7–3 (higher end army_value / position); at the 720s
  combat clock wins are even (0 / −725 swing). So spawn bias is *clock-dependent* — the mirror is
  mandatory for S1, recommended for S2's swing term.
- **A frozen `@stable` snapshot is a much harder yardstick than `@normal` — by design it exposes
  when the loop has stopped improving.** Experimental = Stable + one axis (SR-contestation,
  `SrPressureScoreMultiplier: 260`). That axis beat **Normal** by +$6,300 on S2 `[pre-regime]`, but
  vs **Stable** it is **neutral-to-negative** (S2 swing edge −350; Exp over-aggresses into bad
  trades vs a competent same-faction defender — worst cells Exp −4200/−4950, k/d 2-10/4-10). Result:
  Exp ≈ Stable on BOTH rungs (S1 5–5 / capture 6-6; S2 5–5). This is the intended signal: measuring
  against the last validated snapshot reads "nothing improved since the last promotion", so a lever
  that only beat the weak control no longer counts. Cranking the same axis higher is unlikely to
  help; the next cycle needs a genuinely different improvement.
- **Fast harness realized speedup (timed):** minimized + framerate-uncapped at `SpeedMultiplier: 8`
  gave **~66–71 s/match** for the 300s S1 clock and **~138–157 s/match** for the 720s S2 clock —
  ~4.3–5.2× realtime. The ~30s engine-init/map-load is fixed per match, so effective speedup is
  below the raw 8× sim multiplier and *the shorter the match, the more init dilutes it*. The real
  win over the `[pre-regime]` 6× windowed profile is **no window / no focus theft**, not a large
  raw-speed jump on these clocks.
- **Parse tooling had a same-faction hazard.** `parse-s1-batch.py` / `parse-s2-batch.py` disambiguated
  the two calibration players by `faction` (america/russia) — which silently breaks now that both
  sides are `america` (both match the "america" filter → same player picked twice). Fixed to key on
  player **name** (`USA-bot` = spawn 14,45 / `Russia-bot` = spawn 80,35). Also the primary-control
  bot_type is `stable` (not `normal`), so control selection is now "the other playable bot" rather
  than a hardcoded `normal` lookup. General rule: **any harness code that identifies a bot by faction
  is wrong under a same-faction regime — identify by player name / spawn slot instead.**

## 2026-07-22 — BotBlackboard's task-market API is dead code; the maneuver stack runs on two half-substrates (bot-brain architecture recon)

> **[rejected: architecture recon tied to WORKSPACE/plans/260722_bot_brain_architecture.md; the bot coordination layer (PoiGoalGuard ledger / BotBlackboard) is undocumented in DOCS/reference, so a "blackboard task API is dead code" finding is orphaned there — belongs in the architecture plan, and "dead code" state is mutable]** (curation 2026-07-22).

Found while inventorying coordination primitives for `WORKSPACE/plans/260722_bot_brain_architecture.md` (main @ `0fce8bbd`).

- **`BotBlackboard` carries a complete task-market API with ZERO callers.** `PostTask`/`ClaimTask`/`UpdateTaskStatus`/`GetOpenTasks` (`BotBlackboard.cs:137-191`), task types AttackArea/DefendArea/Scout/Capture/SupplyRun/Retreat/Garrison, `TaskStaleTicks=1500` — grep across all bot modules finds no module posting or claiming a task. Someone built the coordination abstraction the bot lacks and never wired the maneuver side to it.
- **Only the blackboard's `ClaimUnit` + intel channels are live**, and only in legacy support modules: `HelicopterSquadBotModule.cs:162`, `GarrisonBotModule.cs:156`, `ScoutBotModule.cs:147` (+`PostIntel` `:275-282`), `SupplyFollowerBotModule.cs:140`, `AdaptiveProductionBotModule.cs:93-95` (`GetIntel`).
- **The maneuver stack (SquadManager, PoiOffensive, PoiGarrison, LayeredDefence, CaptureCoordinator) never touches the blackboard** — it coordinates through the `PoiGoalGuard` ledger instead (objective-key mutual exclusion, no unit claims, no task lifecycle). Net: two parallel coordination substrates, each covering half the need (blackboard: unit claims + intel, no live task flow; ledger: objective locks, no unit ownership), with ad-hoc leaks outside both (e.g. `LayeredDefenceBotModule.cs:283` checks `transport.IsPassengerReserved` directly).
- Implication recorded in the architecture doc (§4.4): a future operations layer should extend the ledger with unit-level claims and delete the blackboard's dead task API rather than adopt it (it stores no lifecycle/composition/conditions).
