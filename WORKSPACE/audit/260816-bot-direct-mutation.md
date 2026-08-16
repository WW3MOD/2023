# Static audit: direct world-state mutation from bot modules

**Date:** 2026-08-16 · **Tree:** `wt/savegame-verify` merged with `main @ b7f9fe9d` · **Method:** static read only, no game launches.

## Why static and not dynamic

The dynamic instrument used earlier (a whole-match `World.SyncHash()` comparison around each bot tick) returned **zero** across a full match and was cited as evidence the class was closed. It was wrong, twice: it cannot see an **activity-queue write** and it cannot see a **condition grant**, because neither changes a `[Sync]`-marked field *at the moment of the mutation* — the effect only surfaces later when a gate or an activity reads it. An instrument that watches a hash can only see mutations that reach the hash. Hence this audit.

## The failure mode being bounded (keep it distinct from the network one)

**Save/restore-specific.** A saved game is an order stream. On restore, orders replay but `ModularBot.Tick` early-returns while `World.IsLoadingGameSave` (`ModularBot.cs:206`, `:304`), so **every bot mutation that was not an order simply never happens**. The engine states the contract at `ModularBot.cs:105-106`: *bot logic may not affect world state and can act only by issuing orders.*

This is **not** the same as the `IOrderGenerator` network desyncs found separately: in network play a bot tick runs on every client, so a direct bot mutation is survivable there. Do not merge the two stories.

**A site is only a leak if BOTH hold:** the bot mutates state directly (no order), **and** synced code later reads that state.

## THE BOUNDED LIST — 3 sites, 1 module, 1 mechanism

Ranked by whether a restore observably diverges.

### 1. `LaneAmbushBotModule.cs:479` — `ec.GrantCondition(u, this)` — **CONFIRMED LEAK, restore diverges** 🔴
- **Module:** `LaneAmbushBotModule.EnsureGatedAmbusher` (`:465-489`)
- **State:** grants the `enable-ambush-tactics` external condition on a posted ambusher.
- **Read by synced code:** **YES.** `AttackMoveActivity.Tick` gates its Stage-2 halt-before-contact on `self.GetConditionCount(AmbushTacticsCondition) > 0` (`:155-161`).
- **Ordered equivalent nearby:** **YES — in the same method, four lines below.** Stance is set via `bot.QueueOrder(new Order("SetUnitStance", u, false))`, and *that* survives the restore. One method, two mutations, only the unordered one desyncs.
- **Evidence:** measured. At world tick 2128 the recording has `gatecount=1 tactics=True` (march halts), the replay `gatecount=0 tactics=False` (engages instead). Every other input — scanned target, target owner, `selfvisible`, `groupdetected`, allies — is identical.
- **Profile reach:** both. Module header `:40-51` records the `@stable` twin runs at full parity and does grant the condition. Humans / Normal / Rush / Turtle never instantiate the module.
- **Recommended fix (NOT made):** grant through an issued order resolved by an `ExternalCondition` seam on the unit, mirroring the `SetUnitStance` call beside it. Not one line — it needs an order string and a resolver — so it is a recommendation, not a drive-by.

### 2. `LaneAmbushBotModule.cs:501` — `g.Ec.TryRevokeCondition(u, this, g.Token)` — same class, **does not itself cause divergence** 🟡
- **Module:** `LaneAmbushBotModule.ReleaseUnit`. Revokes the gate when a unit is unposted.
- **Read by synced code:** yes, same gate as #1.
- **Why lower:** on the replay the condition was never granted, so a missing revoke leaves count 0 — which *matches* the recording's post-revoke 0. It only matters inside the window where the recording is granted-but-not-yet-revoked, which is #1's divergence, not a separate one. Fixing #1 by ordering the grant requires ordering this revoke too, or the two lives desync in the opposite direction.

### 3. `LaneAmbushBotModule.cs:219` — `kv.Value.Ec.TryRevokeCondition(...)` — same class, cleanup path 🟡
- **Module:** `LaneAmbushBotModule`, module-disabled sweep ("a disabled module must leave zero granted tokens behind").
- Same reasoning as #2, reached only when the module is disabled. Lowest rank.

## Categories swept CLEAN (each checked, not assumed)

| Category | Result |
|---|---|
| **Activity-queue writes** (`QueueActivity` / `CancelActivity`) | **ZERO remaining.** All three prior sites became orders in `d5a4a42b`; raw grep now finds only comment references. |
| **Other world mutation verbs** (`Kill`, `InflictDamage`, `SetPosition`, `ChangeOwner`, `CreateActor`, `AddFrameEndTask`, `TakeCash`/`GiveCash`) | **ZERO.** The only `.Dispose()` hits are trait-local `ActorIndex` query caches, not world state. |
| **Direct trait-field writes** | **ZERO.** All 113 member-assignments in `BotModules/**` target bot-local records (`Squad` — `Squads/Squad.cs:96`, plus `axis` / `lane` / `task` / `g`). No writes to the public-mutable `AutoTarget.PredictedStance`, `PredictedEngagementStance`, `Aggressor`, or `AttackBase.IsAiming`. |
| **Mutating trait-method calls** | **ZERO.** Only `FieldLoader.Load(this, yaml)` — the module loading its own Info. |

## Shared state bots maintain — does synced code read it?

The other half of the question, since a leak needs a synced *reader*:

- **Influence stack** — `DangerFieldLayer`, `ControlField`, `BeliefStore`, `ThreatMapManager`, `SightingThreatLayer`, `PoiMap`: all declared `ITick, IWorldLoaded` **world** traits. They tick themselves, **not** from a bot tick, so they update identically on the replay. **Not leak vectors.**
- `TerrainAffordanceLayer`, `UnitRoleResolver`: `IWorldLoaded` only (load-time). Identical on both lives.
- **`PoiGoalGuard.Ledger` / `BotBlackboard`** — the only non-bot-module reader is `StancePositioningExecutor` (a synced `ITick, ISync` unit trait). It only **writes** (`Ledger.Commit` `:653`, `Ledger.Release` `:664`); `committedGuard` is never read to make a decision. Direction is synced→bot, so it is **not** a restore leak.

## Limits of this audit — what it does NOT bound

1. **Scope is `Traits/BotModules/**` + `ModularBot`.** A helper defined elsewhere but called from a bot tick could mutate. Mitigated by enumerating the trait types bot modules obtain references to and sweeping mutating method calls on them — but that sweep is method-name-based, not exhaustive.
2. **Indirect dispatch not walked exhaustively** — `IBotPositionsUpdated`, `IBotRequestUnitProduction` and similar implementations were not each read end to end.
3. **Order resolvers are out of scope.** An order that IS recorded but whose resolver behaves differently on replay would still diverge. Not a "direct mutation", still a restore hazard.
4. **THE REVERSE HAZARD IS NOT COVERED, and this is the important one.** This audit bounds *bot mutates → synced reads*. It does **not** bound *synced code reads state that only bot ticks refresh*. Leak #2 turned out not to be that shape, but nothing here excludes it, and no instrument currently exists that would find it.

## Verdict

**The bot→world direct-mutation class is bounded: 3 sites, 1 module, 1 mechanism, one fix from closed.** That is a real and checkable result, and it is the first time this bug has produced a bounded list rather than another bisection.

**It does not change the verdict on saved games, and should not be reported as if it did.** The audit closes one of at least two plausible restore-divergence classes; limit 4 above is un-swept and has no detector. Both leaks so far were found one at a time by expensive bisection, and **the scenario has never gone green**. Saved games remain **structurally unreliable, not one-fix-from-working**. The only evidence that would change that is `test-savegame-resume-riverzeta` passing.
