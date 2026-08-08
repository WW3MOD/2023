# Bot brain — attention model, staged for delivery

**Researched against `main @ 09877fd5`** (`git status -sb`: `main...origin/main [ahead 53]`, `git rev-list --count HEAD..@{u}` = **0**, so the checkout is not behind; tree clean apart from untracked scratch). Static analysis only — **no game runs, no autotests, no benchmarks**. Every claim carries a `file:line`; VERIFIED vs INFERRED is marked wherever it could matter.

**Inputs.** `WORKSPACE/recon/260807-order-source-census.md` (order-source census), `WORKSPACE/plans/260722_bot_brain_architecture.md` (the 2026-07-22 EXTEND verdict), `DOCS/reference/game-model.md`, `supply-route.md`, `architecture.md` §AI configuration, `influence-stack.md`.

---

## 0. Headline — three corrections to the brief's premise, then the plan

The brief carried three assumptions from prior recon. Two are wrong and one is avoidable, and all three make the work **much cheaper** than it has been costed at before. That is the reason this has stalled: it has been priced as a rewrite when most of it is already in the tree.

### Correction 1 — `ApplyMissionCommitment` is NOT unreachable. It is live in every `@experimental` match.

VERIFIED. `ApplyMissionCommitment` is a local function at `PoiOffensiveBotModule.cs:2885-2895` with two call sites, `:2938` (pure-artillery axis) and `:2982` (the main assault path, deliberately placed **before** the `!moved` early return). Its only gate is `if (!Info.MissionCommitmentEnabled) return;` at `:2887`. `MissionCommitmentEnabled` defaults `false` (`:771`) and is **set `true` on `@experimental` at `mods/ww3mod/rules/ai/ai.yaml:269`**. The `@stable` offense block (from `ai.yaml:1623`) omits it — that, and only that, is where the "unreachable" impression came from.

So the commitment layer is not half-built. **For the offense module it is roughly 70% of an attention model already shipping**, and it has been running in `@experimental` games this whole time:

- `PartitionHeldAxes` (`PoiOffensiveBotModule.cs:1799-1880`), called at `:1307` at the top of every re-eval, walks the live axes and — for each one that is `Committed` and whose abort triggers do not fire — **refreshes its ledger claim (`:1861`), pulls it out of the re-planning set (`:1868`), and strips its target from the candidate list so no duplicate axis forms (`:1877`)**. The held axes are re-added afterwards at `:1478-1480`.
- The abort triggers are `MissionCommitmentMath.ShouldReassign` (`PoiGoalGuard.cs:226/:243`): objective invalidated, danger spiked, materially better opportunity, combat-ineffective, or the outer window `MissionCommitmentWindowTicks` (400, `ai.yaml:273`) elapsed since commit.
- That is: *a plan, a commitment snapshot, a hold, and named revision triggers*. It is the mechanism the whole attention model needs — instantiated once.

**What is actually missing is breadth and a budget, not the mechanism.** Concretely:
1. It exists only in `PoiOffensiveBotModule`. Nothing equivalent exists in Garrison, LaneAmbush, Capture, LayeredDefence, MountedTransport (VERIFIED by grep: `PartitionHeldAxes`/`MissionCommitmentEnabled` appear in no other bot module except one `[Desc]` cross-reference at `HelicopterSquadBotModule.cs:291`).
2. There is no cap on how many axes may be re-decided per eval — all of them can be, so "commit to *one* group at a time" is not expressed.
3. It arbitrates only against the offense module's own claims. A module that does not read the ledger still poaches its units (§2).

### Correction 2 — the Stage-0 cadence refactor is NOT on the critical path. Schedule PLANS, not MODULES.

The census's core structural warning is right and I am not disputing it: all **24 cadence sites are per-call `--countdown` decrements**, and only three pieces of state are tick-stamped (`CaptureCoordinatorBotModule.cs:441/:400`, `LayeredDefenceBotModule.cs:181`, `EngineerRouteOpenBotModule.cs:279`). Withhold a module's `BotTick` and its interval stretches by the withhold factor, and — the real danger — **every single `Ledger.Commit` refresh in every bot module sits behind that module's own countdown gate** (VERIFIED, exhaustive: no always-run refresh path exists anywhere). Tightest headroom is TTL ÷ interval = 250/100 = **2.5×** on the three POI modules and LaneAmbush. Starve a module past that and its units silently fall out of the ledger while it still lists them in `axis.Units` — reproducing the exact "derricks ignored" bug the ledger was written to fix (`PoiGoalGuard.cs:6-17`).

**The conclusion normally drawn from that — "therefore convert 24 countdowns to tick stamps first" — only follows if attention is modelled at the module level.** It is the wrong level. Model attention at the **plan** level and the pathology never fires:

> Every module keeps ticking on its own countdown, every eval, forever. What the scheduler withholds is not the *tick* but the *re-decision*. A plan that is not granted attention this eval takes the **HELD** path instead of the re-plan path — and the HELD path is where the claim refresh already lives (`PoiOffensiveBotModule.cs:1861`).

Claims are refreshed on exactly the evals where re-planning is skipped. FSMs still advance. `HelicopterSquadBotModule.PruneSquads` still runs on its 5-tick branch (`:748`) so a state tick never reaches a Disposed member. `MountedTransportBotModule`'s polled 4-state carrier FSM (`:370-478`) still detects arrival and unload. `PoiOffensiveBotModule`'s eval-counting fields (`LosingStreak`, `FillHoldEvals`, `RetreatSustainEvals`) still count evals at their designed rate.

**This removes 24–25 conversion sites from the critical path.** Stage 0 becomes a prerequisite only for a future design that wants a genuine per-module APM budget (AlphaStar-style). §7 states that condition explicitly and keeps the cost on the books.

INFERRED, and the honest caveat: this holds because no proposed stage below ever skips a `BotTick`. It is an invariant the plan must actively preserve, not a property of the code. **Any future change that gates `ModularBot.cs:111-116` on anything re-opens the whole 24-site bill.** Write that into the code as a comment at the tick loop.

### Correction 3 — the unit of attention already exists, four times over.

VERIFIED — four modules already hold persistent plan objects with identity and lifetime:

| Module | Plan object | Declared | Keyed by | Membership stability |
|---|---|---|---|---|
| `PoiOffensiveBotModule` | `Axis` (`:869-940`) | `readonly List<Axis> axes` `:960` | target `ActorID`, **never retargeted** | rebuilt each eval (shed `:1393-1416` / top-up `:1418-1454`) — **except while held**, when `PartitionHeldAxes` removes it from `axes` so `Units` is untouched |
| `PoiGarrisonBotModule` | `Garrison` | `readonly List<Garrison> garrisons` `:174` | POI | rebuilt each eval |
| `LaneAmbushBotModule` | `Lane` | `readonly List<Lane> lanes` `:147` | lane | rebuilt each eval |
| `MountedTransportBotModule` | `CarrierTask` | `readonly Dictionary<Actor, CarrierTask> carrierTasks` `:164` | carrier actor | a real 4-state FSM, `StateChangedAtTick` **already tick-stamped** (`:277`) |

`Squad` (`Squads/Squad.cs:23-52`) is *more* stable than an Axis — units join and only leave by dying (`SquadManagerBotModule.cs:239-244`) — and has a per-squad `Update()` entry point (`Squad.cs:88-92`). But it is **air-only in WW3MOD**: all four `SquadManagerBotModule` instances set `IgnoreGroundUnits: true` (`ai.yaml:1086/1177/1630/1643`), so `GroundStates.cs`/`NavyStates.cs`/`ProtectionStates.cs` are unreachable on both shipped profiles (`architecture.md:321`). It is not a candidate for the ground attention unit.

---

## 1. Answers to the five specific questions

**Q: What is the unit of attention — unit, squad, axis, POI?**
**The plan object (Axis / Garrison / Lane / CarrierTask), not the unit and not the module.** Argued: it is the smallest thing that has an objective, a member list and a lifetime, which is exactly what "commit to it" needs; it already exists in four modules (Correction 3); and it is the level at which the existing hold (`PartitionHeldAxes`) is written, so generalising costs a refactor rather than an invention. A *unit* is too fine (a unit has no objective of its own), a *module* is too coarse and detonates the cadence problem (Correction 2), and a *POI* is a target rather than a force. Note the `Axis` identity is already "(TargetId, uninterrupted axis existence)" and a re-formed axis for the same POI restarts every counter from 0 (comment at `PoiOffensiveBotModule.cs:905-911`) — that is the right identity semantics for an attention unit and needs no change.

**Q: What does "commit" mean concretely, and where is it enforced?**
Two distinct things, and conflating them is why this looks half-built when it is not:
- **Commitment to a PLAN** — `axis.Committed` + `CommitTick`/`CommitScore`/`CommitDanger`/`CommitStrength` (`:919-923`), stamped by `ApplyMissionCommitment` (`:2890`), enforced by `PartitionHeldAxes` skipping re-planning. **This exists and works, for one module.** One wart: `axis.Committed` is never reset to `false` — only three references exist (`:1813` read, `:2890` write, `:3583` log) — so it dies with the `Axis` object. That is defensible (it means "has a snapshot", not "is currently held") but it should be documented, not left as an accident.
- **Commitment to a UNIT** — `GoalGuardLedger.Commit(unit, objective, tick, ttl)` (`PoiGoalGuard.cs:60-77`), enforced only by *voluntary* check-then-commit in each module's free-pool build. **This is the half that is not enforced anywhere.** VERIFIED absences: `Commit` returns `void` (`:60`) so a loser is never told; a different objective unconditionally overwrites the incumbent with `CommitCount` reset to 1 (`:68-76`); there is no priority, rank, owner-module or preemption concept anywhere in the file. `Release` *does* exist (`:100`, called from 14+ sites) — the brief's "TTL expiry is the only exit" is wrong.

So: **the plan-commitment layer is built and unbudgeted; the unit-commitment layer is a convention with no enforcement point.** Stage 1 gives it one.

**Q: How does a scheduler avoid corrupting claim state?**
By never withholding a `BotTick` — see Correction 2. The refusal is delivered *inside* the module's own eval, and the refused branch is the one that refreshes the claim. This is not a new invariant to invent: `PoiOffensiveBotModule.cs:1855-1864` is that branch, written for exactly this reason, with the comment already explaining it. The residual risk is a *future* change gating the tick loop; §7 prices that.

**Q: What happens to the second order layer — the activity-queueing traits that emit no `Order`?**
They cannot be scheduled at the funnel, because they never reach it. There are four, and only two matter:
- **`StancePositioningExecutor`** (`defaults.yaml:27` under `^Combatant`) — `self.QueueActivity(new Move(...))` at `:414` every `EvaluateCooldown: 30` = **1.8 s** (`defaults.yaml:30`), the **fastest churn source in the entire census**, ~3× faster than any bot module. And it is a **WRITE-ONLY ledger participant**: it stamps `tacpos:` at `:643` and never reads, so it overwrites another module's claim outright (`PoiGoalGuard.cs:68-76`). Two sins in one trait. **Do not exempt it — fix it** (Stage 1b): make the reposition and the stamp conditional on the unit not already holding a live *foreign* commitment. Human-owned units are unaffected **by construction**, because `PoiGoalGuard@poi` is `RequiresCondition: enable-ai-experimental || enable-ai-stable` (`ai.yaml:83-84`) so `goalGuard` resolves null on a human player — the guard is an identity pass-through there. That matters because `GrantConditionOnHumanOwner@tacpos` (`defaults.yaml:41-45`) makes this trait **default-ON for every human-owned combatant**.
- **`AutoSeekSupplies`** (`infantry.yaml:221`, on `^Soldier`) — `ScanInterval: 40` = 2.4 s, walks an idle low-ammo soldier off to a truck. **Exempt it.** The YAML comment at `infantry.yaml:217-220` states there is no owner-side split: it is one switch covering human- and bot-owned soldiers alike. Gating it on a bot ledger would be a bot-only behaviour riding a human-facing trait, and resupply is a need the attention model should not be able to starve. Cost of exempting: a committed soldier can still leave its plan to find ammo. That is correct behaviour, and it is *already* a live open bug in the other direction (`WORKSPACE/bugs/discovered.md`, truck oscillation Loops A/B) — do not entangle the two.
- **`CohesionSlotMemory`** (`defaults.yaml:20`) returns a unit to its formation slot (`:227`). It is a slot-keeper for a plan that already exists, not a competing tasker. Exempt.
- **`GarrisonManager`** (`civilian.yaml:63` et al.) manipulates garrisoned infantry by direct trait calls, zero orders (`:573+`). A parallel controller, not an order conflict. Exempt.

**Q: Is there a cheaper 80% version?**
**Yes, and it is Stage 1.** The user's felt problem is churn, and churn has one enforceable choke point: **`ModularBot.QueueOrder`** (`ModularBot.cs:91-98`). VERIFIED that this is a genuine single funnel — every `bot.QueueOrder(...)` across all bot modules lands there (`architecture.md:377-379` states it and the census confirms it), it *already* knows the issuing module via `currentModuleTag` (`:62`, set at `:114` and `:155`), and `Order` carries both `Subject` (`Network/Order.cs:61`) and `GroupedActors` (`:64`) so a gate there can see every target actor of a grouped order. Turning that funnel from a pass-through into an **incumbency arbiter** converts the ledger from an advisory convention into an enforced lock, for every module at once, without touching a single module. §2 details it.

---

## 2. Why the funnel gate is the highest-value small change

Today's conflict resolution is, VERIFIED: **whichever module is declared LATER in `ai.yaml` wins**, as an emergent property of trait construct order (`ModularBot.cs:84` → `Actor.cs:439-441` → `TraitDictionary.cs:146-147` → `ActorInfo.cs:104-142`), declared nowhere in code. The loser learns nothing (`QueueOrder` returns `void`), the non-queued order hard-cancels the running activity and drops the whole queued chain (`Actor.cs:381-387`, `Activities/Activity.cs:198-210`), and partial movement survives only to the cell boundary (`Mobile.cs:705`).

The funnel gate replaces that with **the incumbent commitment wins**. Its real leverage is that it fixes the *non-participant* problem structurally, without editing the non-participants:

| Poacher | Why it poaches today | Fixed by the gate? |
|---|---|---|
| `LayeredDefenceBotModule` on `@stable` | both `CommitLineAssignments` and `RespectCommitmentLedger` absent from the `@stable` block ⇒ participates in neither direction | **yes** |
| `GarrisonBotModule@defenses` (shared `enable-ai-any`) on `@stable` | ledger-blind by runtime gate (`GarrisonBotModule.cs:103`); its own `[Desc]` at `:38` says so | **yes** |
| `HelicopterSquadBotModule` lift on `@stable` | reads unconditionally (`:1606`) but `CommitTransportPassengers` absent ⇒ never writes | **yes** |
| `CaptureCoordinatorBotModule` escorts/defenders on `@stable` | `CommitSupportUnits` absent from `@stable.tecn` | **yes** |
| `StancePositioningExecutor` | write-only, and never reaches the funnel | **no — needs Stage 1b** |

That is five of the six worst offenders closed by one file. It is also the correct composition point for the parallel churn audit (§6).

---

## 3. The stages

Each stage is independently mergeable and independently verifiable. Costs are in **focused sessions** and include NUnit pins and a review pass; they are deliberately pessimistic (see §8).

### Stage 1 — Incumbency arbitration. **Behaviour-changing. Visible. ~1.5–2 sessions.**

**1a — the funnel gate.**
- `engine/OpenRA.Mods.Common/Traits/Player/ModularBot.cs` — new `ModularBotInfo` field `RespectCommitmentsOnIssue` (default **false**, per the shared-trait rule at `architecture.md:373-375`). In `IBot.QueueOrder` (`:91-98`), when on: resolve `PoiGoalGuard` off `player.PlayerActor`, collect the order's target actors (`Subject` ∪ `GroupedActors`), and **drop the order iff *every* target holds a live commitment whose objective prefix maps to a module other than `currentModuleTag`**.
- **All-or-nothing, never partial.** `Order.Subject` and `Order.GroupedActors` are `readonly` (`Network/Order.cs:61/:64`); reconstructing a grouped order to drop one member is a real risk for no gain. It is also near-lossless in practice: the POI modules recruit only from the ledger-checked free pool, so their grouped orders are homogeneous in ownership.
- New pure class `OrderArbitrationMath` (house idiom — `CommitOnOrderMath`, `MissionCommitmentMath`, `FrontlineAllocationMath` are all this shape) holding the **prefix → owning-module table**: `offense:`/`bombard:` → PoiOffensive, `garrison:` → PoiGarrison, `ambush:` → LaneAmbush, `capture:`/`capture-escort:`/`capture-defend:` → CaptureCoordinator, `transport:` → MountedTransport + HelicopterSquad, `defend:`/`defend-line:` → LayeredDefence, `bridge-repair:`/`bridge-screen:` → EngineerRouteOpen, `tacpos:` → StancePositioningExecutor. **Make this table the single source of truth** — have the modules read their prefix from it rather than string-literalling it, so the mapping cannot rot. (Cheaper and stronger than a lint; the `Lint/CheckUnitRoleTable.cs` precedent exists if a lint is wanted later.)
- YAML: set `RespectCommitmentsOnIssue: true` on **both** `ModularBot@experimental` (`ai.yaml:31-33`) and `ModularBot@stable` (`:34-36`). These are separate trait instances, so this is a deliberate, visible improvement flowing to `@stable` exactly as `CLAUDE.md` policy prescribes — **and the commit message must say so**, because the benchmark baseline must be re-taken knowingly.

**1b — the second order layer.**
- `StancePositioningExecutor.cs` — new `Info` field (default **false**) gating: before the reposition at `:414` and the `tacpos:` stamp at `:643`, skip if the unit holds a live commitment under a **non-`tacpos:`** prefix. Set true on the `@experimental` grant. Humans are identity by construction (null `goalGuard`).
- `AutoSeekSupplies`, `CohesionSlotMemory`, `GarrisonManager` — **exempt, deliberately**, reasons in §1.

**What the user SEES** — §9.

**Verification (no autotest required).** `OrderArbitrationMathTest.cs` pinning: foreign live claim on all targets ⇒ drop; own-module claim ⇒ allow; *expired* claim ⇒ allow (`IsCommitted` tests `currentTick < ExpiresAtTick`, `PoiGoalGuard.cs:81-82`); mixed group ⇒ allow; null ledger ⇒ allow (the byte-identity path); unknown prefix ⇒ allow (fail-open). Plus `dotnet test` and `make test`. `GoalGuardLedger<TKey>` is generic **specifically so it can be driven with `string` keys without constructing an Actor** (`PoiGoalGuard.cs:36-38`, and `PoiGoalGuardTest.cs:28-46` already does exactly that) — the withhold-vs-TTL interaction is statically testable today.

**Unblocks:** every later stage. Once incumbency is enforced, "granted attention" and "holds the units" stop being two different things.

### Stage 2 — Generalise the hold. **Behaviour-changing. Visible. ~2 sessions.**

Extract the `PartitionHeldAxes` shape (`PoiOffensiveBotModule.cs:1799-1880`) into a reusable form and apply it to the plan objects that already exist:
- `PoiGarrisonBotModule.garrisons` (`:174`) and `LaneAmbushBotModule.lanes` (`:147`) gain a `Committed` snapshot and a hold branch that refreshes the claim (mirroring `:1861`) and skips re-planning.
- Reuse `MissionCommitmentMath.ShouldReassign` (`PoiGoalGuard.cs:226/:243`) verbatim as the trigger set — it is already NUnit-pinned (`MissionCommitmentMathTest.cs`).
- `MountedTransportBotModule.carrierTasks` needs nothing: its FSM is already tick-stamped (`:277`) and single-owner.
- Do **not** touch `PoiOffensiveBotModule`'s existing hold in this stage beyond the extraction. Its gate ordering is load-bearing and documented in `influence-stack.md:129` ("these gates are early `return`s, so removing one is a RE-ROUTE, not a subtraction").

Default-off `Info` field per module, on per profile.

**Verification:** the extracted helper is pure ⇒ NUnit. Plus a targeted pin that a held plan's TTL refresh precedes its re-plan skip in the same eval — the invariant that makes Correction 2 safe.

**Unblocks:** Stage 3 has something to refuse.

### Stage 3 — The attention budget. **Behaviour-changing. Visible. This is the actual ask. ~2–3 sessions.**

A per-player budget trait (naturally alongside `PoiGoalGuard`, which is already the per-player `[TraitLocation(SystemActors.Player)]` coordination point, `PoiGoalGuard.cs:304`). Each module, before re-planning a plan, calls `TryClaimDecision(planKey)`; on refusal it takes the Stage-2 hold branch. Budget replenishes on a world-tick stamp (**not** a countdown — this is new code, write it right).

Determinism is the design constraint, and `influence-stack.md:103` is unambiguous: **zero `SharedRandom`/`LocalRandom` draws**, deterministic offsets and iteration-order tie-breaks only. Grant order must be a total order over `(priority, planKey)` with an `ActorID`-style deterministic tie-break. Note the live tension flagged at `influence-stack.md` / census §6.4: several *existing* schedule points do draw RNG to self-stagger (`PoiOffensiveBotModule.cs:1011`, `PoiGarrisonBotModule.cs:187`, `CaptureCoordinatorBotModule.cs:429-430`, `LayeredDefenceBotModule.cs:208`, `MountedTransportBotModule.cs:302`). The budget must not subsume or reorder those draws — leave them exactly where they are.

**Verification:** `AttentionBudgetMath` pure ⇒ NUnit pins on grant order, refusal, replenishment, and starvation bounds (no plan may be refused more than N consecutive evals — the guard against a plan being held past its own TTL headroom of 2.5×).

**Unblocks:** the difficulty knob (decisions/minute), and Stage 4.

### Stage 4 — Event-driven revision. **Behaviour-changing. ~2–3 sessions. Optional.**

Committing must not mean going deaf. A minimal deterministic event bus lets a held plan be woken early on ContactMade / UnitLostThreshold / ObjectiveLost, rather than waiting for its trigger sample. Designed in detail already at `260722_bot_brain_architecture.md` §4.7; nothing in this plan invalidates that design. **Do not start it before Stage 3 ships** — the 2026-07-22 doc's own verdict was that an event pop is worthless without a durable plan to revise, and Stages 2–3 are what make plans durable.

---

## 4. What is explicitly NOT in this plan

- **The 24-site cadence refactor** (§7).
- **`SectorPostureHold`.** The user's 2026-08-08 live-play report — *"a flanking group is constantly ordered back, then forward, stuck in a loop"* — is already root-caused (2026-08-03) to `SectorPostureHold` vetoing any axis whose target sector reads `sectorOwn ≈ 0` and ordering it to a receding `stagingAnchor ?? rallyCell`, and is **in flight on branch `auto/posture-veto`** (`WORKSPACE/PIPELINE.md`, LIVE-PLAY BATCH 2026-08-08). **Stage 1 will not fix that loop** — it is one module ordering its own committed units, so incumbency arbitration has nothing to arbitrate. Say this to the user before they test Stage 1 against it.
- **The supply-truck oscillation** (`WORKSPACE/bugs/discovered.md`, Loops A and B) — separate, separately tracked, and §1 explains why `AutoSeekSupplies` is deliberately left alone.
- **A new `Operation`/`TaskForce` object.** The 2026-07-22 doc proposed one (§4.1). It is not needed: `Axis`/`Garrison`/`Lane` already carry objective + members + lifetime, and building a parallel object would mean migrating four modules onto it before anything is visible — which is precisely the shape that stalled this work before.

---

## 5. `@stable` and the no-silent-drift rule

Every new field above is an `Info` field defaulting to **baseline/off**, per `architecture.md:373-375`. Where a stage improves both bots, it is turned on for both **in the same commit, with the commit message saying so**, per `CLAUDE.md`. No gate exists in this plan whose purpose is to withhold a fix from `@stable`.

The one thing this costs the user: **`@stable` moving means the benchmark control moves**, so the ai-bench baseline must be re-taken after Stage 1 and again after Stage 3. That is a real cost of the policy, not an argument against it, and it should be scheduled rather than discovered.

---

## 6. Composing with the parallel order-churn audit

Assume its findings exist and land independently. The contract:

- Stage 1 gives it an **enforcement point it can target**. Any churn finding of the form "module A yanks units from module B" becomes either (a) a new objective prefix, (b) a row in the `OrderArbitrationMath` prefix table, or (c) a documented exemption. None of those is a code-structure change — they are data.
- Stage 1 gives it **free instrumentation**. The drop decision sits one line from `lifecycleLogger?.LogOrder(player, currentModuleTag, order)` (`ModularBot.cs:96`), which already records the issuing module and self-gates to a no-op when lifecycle logging is off. Counting drops per `(issuer, incumbent)` pair is a few lines inside the existing `TestMode` gate. Note the census flag: offline order-churn detection (R5) is **specified but not implemented** (`tools/behavior-lint/README.md:46`) — this is where it would attach.
- **Conflict risk is low but real:** if the churn audit proposes per-module order dedup or suppression *inside* modules, that competes with the funnel gate. Prefer the funnel — one file, all modules, no cadence coupling. If it proposes fixing individual poachers by making them ledger-participants (adding the missing `CommitSupportUnits`/`CommitLineAssignments` flags to `@stable`), that is **complementary, not redundant**: the module-side fix makes the module a good citizen, the funnel gate makes citizenship unnecessary.

---

## 7. The deferred cadence refactor — the condition, and the bill

Keep this on the books. It becomes required the moment any design wants to **withhold a module's `BotTick`** — e.g. a true per-module decision-rate budget, or dropping low-value modules under a CPU cap.

The bill, VERIFIED: **24 per-call countdown sites** (`PoiOffensive:1058`, `PoiGarrison:195`, `LaneAmbush:181`, `CaptureCoordinator:542/:548`, `LayeredDefence:238`, `EngineerRouteOpen:229`, `MountedTransport:329`, `HelicopterSquad:508/:515/:527/:534/:541`, `Garrison:239`, `Scout:98`, `SupplyFollower:416`, `AdaptiveProduction:217`, `SquadManager:268/:274/:281/:287`, `BaseBuilderQueueManager:54/:79/:91`) **plus one accumulator** (`UnitBuilderBotModule.ticks`, `:376-378`, `FeedbackTime` a `const`) = **25 sites**. `world.WorldTick` is available in every one of them at zero plumbing cost (`World.cs:445`; already used at `PoiOffensiveBotModule.cs:1151`, `PoiGarrisonBotModule.cs:232`, `LaneAmbushBotModule.cs:231`, `CaptureCoordinatorBotModule.cs:505`, `LayeredDefenceBotModule.cs:342`).

Realistically **2–3 sessions**, and — this is the important part — **it is not statically verifiable that all 25 were converted.** NUnit can pin the tick-stamp helper; only a code audit can pin coverage. That asymmetry is a second reason to keep it off the critical path.

Put a comment at `ModularBot.cs:111-116` recording that gating that loop re-opens this bill.

---

## 8. Total cost — honestly

**Stages 1–3: roughly 6–8 focused sessions.** Stage 4 adds 2–3. The deferred cadence refactor adds 2–3 more if ever triggered. Call the whole arc **8–11 sessions**, plus **two ai-bench re-baselines** (§5) which are user-gated wall-clock, not agent time.

Where the estimate is most likely to be wrong, in order:
1. **Stage 3.** "One group at a time" is a tuning problem as much as a code problem — the budget size, the starvation bound and the priority order will want iteration, and iteration here means bot-vs-bot matches, which are user-gated. Static verification proves the budget is *deterministic and non-starving*; it cannot prove it is *good*. This is the stage most likely to double.
2. **Stage 1's second-order fallout.** Enforcing incumbency will expose plans that hold units and do nothing. `PoiOffensive` has the `MissionCommitmentWindowTicks: 400` outer backstop (`ai.yaml:273`); the other modules have only the raw TTL. Expect one follow-up fixing a module that now visibly sits on units it is not using — which today is invisible precisely because someone else steals them.
3. **Stage 2's extraction.** The `PoiOffensive` gate ordering is load-bearing and heavily commented; a clean extraction may need to leave the offense call site alone and only *share the helper*, not the control flow.

An optimistic version of this number would be worse than useless — this is a multi-session arc, not an afternoon.

---

## 9. Ordered stage list, and what the user sees after Stage 1

| # | Stage | Cost | Behaviour | Verified by |
|---|---|---|---|---|
| 1 | Incumbency arbitration — funnel gate (1a) + `StancePositioningExecutor` ledger read (1b) | 1.5–2 sessions | **changing, visible** | `OrderArbitrationMathTest` + `dotnet test` + `make test` |
| 2 | Generalise the hold to Garrison / LaneAmbush | ~2 sessions | **changing, visible** | pure-helper NUnit; reuses pinned `MissionCommitmentMath` |
| 3 | Attention budget — one plan re-decided at a time | 2–3 sessions | **changing, visible** — the actual ask | `AttentionBudgetMath` NUnit (order, refusal, starvation bound) |
| 4 | Event-driven revision (optional) | 2–3 sessions | changing | pure event-ordering NUnit |
| — | Cadence refactor (deferred, §7) | 2–3 sessions | neutral | **not** statically verifiable for coverage |

### After Stage 1, the user sees:

**A group sent at an objective keeps going.** Today a unit that has just been committed to an attack axis, a capture escort or a garrison can be grabbed a few seconds later by a different part of the bot — the defence line, the garrison sweep, the helicopter lift — and turned around mid-move, losing its progress at the cell boundary. After Stage 1 the first commitment holds and the later grab is refused.

**The shuffling around the Supply Route stops.** Infantry standing near the SR are the most contested class in the whole bot — seven consumers draw from that same pool. Today they twitch: a couple of cells one way, then back. After Stage 1 they either stay put or leave with a purpose, and the fastest twitch source of all — the tactical-positioning nudge that fires every 1.8 seconds — no longer fires on a unit that already has a job.

**Fewer units standing still doing nothing.** A silently-dropped order today is indistinguishable from a delivered one, so a unit can end up between two modules' intentions and execute neither.

**What the user will NOT see after Stage 1:** the flanking group is *still* going to march back and forth, because that loop is one module ordering its own units and is fixed on `auto/posture-veto`, not here (§4). And the bot will not yet "commit to one thing at a time" — it will commit to *everything* it starts, which is a different and lesser property. That arrives at Stage 3.
