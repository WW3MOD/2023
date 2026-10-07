# Engine robustness scout — desync, crash and silent-misbehaviour risks in WW3MOD's own engine code

**Researched against `main @ c276679c`** (worktree `wt/scout-engine-robustness`, 2026-10-07). Read-only:
nothing was built, launched or tested. Every finding is from reading code; items marked **HYPOTHESIS**
have a plausible mechanism but no observation behind them. Nothing here is a verdict until its named
check has run.

**Scope.** Engine baseline is the import commit `7362fbc6` ("Starting point", 2023-03-20). Against it,
439 sim-relevant `.cs` files were **added** and ~1050 **modified** (widgets, lint, update rules,
installer and tests excluded). Covered: all of `Traits/BotModules/**` (incl. the five largest modules,
audited one by one), mod-added `Traits/World/*` (nuclear/DEFCON/influence stack), mod-added actor-level
traits/activities/projectiles (supply, garrison, crew, missiles), `SupplyRouteContestation`, and the
mod's diffs to `OpenRA.Game` core (World, Actor, Network, Server, MapLayers, ShadowCache) and to the
damage pipeline. **Not covered:** `Widgets/**` order generators, `Scripting/**` (TestGlobal is
test-only), Cnc-mod files, and the modified-upstream files beyond the damage/visibility path.

**Known-check method.** Each finding was grepped against `WORKSPACE/DISCOVERIES.md`,
`WORKSPACE/bugs/discovered.md`, `WORKSPACE/audit/260816-bot-direct-mutation.md` and `DOCS/reference/`.
"NEW" means no entry names the mechanism at that site; it does not mean nobody has ever noticed the
symptom.

---

## Ranked findings (severity × likelihood)

### 1. LIVE DESYNC — `PoiOffensiveBotModule` queues a rearm activity directly from the host-only bot tick — NEW

- **Site:** `engine/OpenRA.Mods.Common/Traits/BotModules/PoiOffensiveBotModule.cs:3406`,
  `SweepOutOfAmmoUnits`, the `SeekRearm` arm: `AmmoPool.AutoRearm(unit, true, host);`
- **Mechanism:** `AmmoPool.AutoRearm` (`AmmoPool.cs:1054`) calls
  `self.QueueActivity(false, new SeekSupplyProvider|RideTransport|Resupply(...))` directly (verified:
  `:1067`, `:1079`, `:1088`). `QueueActivity(false, …)` also **cancels** the unit's current activity.
  Bot logic runs only on the host (`Player.cs` `IsBot && Game.IsHost`) and is skipped during save-game
  fast-forward (`ModularBot` early-returns on `IsLoadingGameSave`), so this is exactly the class fixed
  for `LaneAmbushBotModule` on 2026-08-16 — **a second instance, introduced 2026-09-03 by `ea1cb20f`.**
- **Why the existing gate missed it:** `engine/OpenRA.Test/BotOrderedMutationTest.cs` (added 2026-09-22)
  IL-scans bot-module methods for **direct** calls to `Actor.QueueActivity`/`CancelActivity`/
  `GrantCondition`/`RevokeCondition` and `ExternalCondition.Grant/TryRevoke` (`:131-145`). It follows
  callees **one hop** into helper types only by name suffix (`Math`/`Tactics`/`Gate`/`Guard`/
  `Blackboard`, `:81`). `AmmoPool` matches none, so a mutation one call deeper is invisible and the
  gate is green over a live instance. A grep of every `BotModules/**` call into a sim-trait static
  (`AmmoPool.*`, `AutoTarget.*`, `SupplyProvider.*`, …) found **only this one** that mutates; the
  others (`ChooseResupplier`, `IsSeekingRearm`, `AllPoolsEmpty`, `CannotFight`,
  `EstimatePercentDamage`) are reads.
- **Live:** `EvacuateOutOfAmmoUnits: true` on **both** `@experimental` (`ai.yaml:578`) and `@stable`
  (`ai.yaml:3269`).
- **Symptom:** in any multiplayer game containing a bot, the host's copy of a dry bot unit cancels its
  activity and drives to rearm while every other client's copy does not → "Out of sync". Every replay
  and every saved-game restore of a match where it fired diverges, single-player included.
- **Severity / likelihood:** desync · high (any bot unit running dry within
  `OutOfAmmoRearmSeekRadiusCells` of an affordable host).
- **Cheapest verification:** **NUnit, expected RED today** — extend `BotOrderedMutationTest` so that
  a callee in the same assembly is scanned one more level (or, minimally, add `AmmoPool.AutoRearm` to
  `IsUnorderedMutation`). That turns the gate red on `:3406` without a launch. Dynamic confirmation is
  one `./tools/autotest/run-test.sh --hidden test-savegame-resume-riverzeta`, but it only goes red if a
  bot unit runs dry and rearms before the save tick — a green there is **not** evidence of absence.
- **Fix shape:** route through an order resolved on the unit (the `SetAmbushGate` precedent), passing
  the chosen `host` as the target so the affordability choice survives.

### 2. Allied soldiers who board the same neutral building change owner — HYPOTHESIS, NEW in this form

- **Site:** `Traits/Garrison/GarrisonManager.cs:293-297` (first boarder claims the building via
  `ChangeOwnerInPlace`) and `:414-419` (heir hand-over). `Actor.ChangeOwnerInPlace` fires
  `INotifyOwnerChanged` (`Actor.cs:558-559`), and `Cargo.OnOwnerChanged` (`Cargo.cs:1276-1283`) does
  `foreach (var p in Passengers) p.ChangeOwner(newOwner)`.
- **Mechanism:** GarrisonManager's own model tracks per-soldier owners (`remainingOwners`,
  `ChooseHeir`), but every owner change of the building converts **all** sheltered passengers. A
  teammate's soldier already in `Passengers` when the claim or hand-over lands becomes the claimant's.
- **Known context:** `architecture.md:2429` documents the *defeat* path ("passengers DEFECT" to
  Neutral). The same-team co-boarding and three-ally heir cases are not recorded.
- **Symptom:** in 2v2, your infantry inside a shared building becomes your ally's, permanently.
- **Severity / likelihood:** silent ownership loss · medium (team games, shared garrisons).
- **Cheapest verification:** a copy of `test-garrison-hostile-cogarrison` with two **allied** players
  entering on the same tick, asserting each soldier's owner after unload.

### 3. `RestockSupply` drains a Logistics Centre the enemy captured mid-drive — NEW

- **Site:** `Activities/RestockSupply.cs:91-92` re-validates only `host.IsDead || !host.IsInWorld`; its
  own comment (`:88-90`) claims it guards "captured mid-drive". The mirror `DeliverSupply.cs:104`
  carries `|| !self.Owner.IsAlliedWith(host.Owner)` and explains why capture is neither dead nor
  removed (`:94-97`).
- **Symptom:** a truck arriving at a just-captured LC refills from the **enemy's** stock.
- **Severity / likelihood:** silent economy leak · medium in contested play.
- **Cheapest verification:** clone `test-truck-restock-survives-cancel`, flip the LC's owner from Lua
  mid-drive, assert the LC's stock is unchanged. One-line fix mirrors `DeliverSupply:104`.
- **Related, lower:** the order resolvers for `PickupSupply`/`Restock`/`DeliverSupply`
  (`DropsSupplyCache.cs:276-325`) check no relationship and `PickupSupply` does not check the target is
  a crate; only the cursor targeters enforce "allied". Needs Lua, a bot or a modified client.

### 4. Saved per-type stance defaults re-apply on every `World.Add` — NEW mechanism (channel known)

- **Site:** `Traits/UnitDefaultsManager.cs:51,61-88` subscribes to `World.ActorAdded`, which fires on
  every `World.Add` — cargo unload (`UnloadCargo.cs:261`), each garrison port cycle
  (`GarrisonManager.cs:479`), crew spawns — with no "already applied" memory.
- **Known context:** the per-machine-file channel is in `bugs/discovered.md` (≈ line 2929, latent MP
  divergence). The re-apply-on-re-add symptom and the save-restore order burst are not.
- **Symptom:** a stance the player set by hand snaps back to the type default after every APC unload or
  port cycle; after loading a save every surviving unit reverts at once, with up to 4 orders per actor
  flushed on the first live frame. Orders replicate, so **not** a desync.
- **Severity / likelihood:** visible misbehaviour · high for anyone with a `unit-defaults.yaml`.
- **Cheapest verification:** extend `test-unit-defaults-apply`: set a manual stance, load + unload,
  assert it survived (expected RED).

### 5. Final "total annihilation" leaves husks, pilots and passengers behind — NEW

- **Site:** `Traits/World/DoomsdayStrike.cs:1526-1537` `Annihilate()` snapshots in-world actors with
  Health and `Kill`s them with `info.AnnihilationDamageTypes`, which defaults empty and is set by no
  rules file. Death leftovers (`SpawnActorOnDeath`, `EjectOnDeath` via `AddFrameEndTask`) spawn **after**
  the sweep; out-of-world actors (cargo, garrison shelter) are skipped and may be ejected alive.
- **Symptom:** right after "Total strategic annihilation." the map still shows husks, ejected pilots and
  released passengers. Verdict unaffected (score frozen at trigger). Probable frame hitch on dense maps
  (unmeasured).
- **Severity / likelihood:** visible misbehaviour · certain on every nuclear ending.
- **Cheapest verification:** extend `test-final-exchange-autofire` (annihilation tick ≈ 745 per its
  comment) with a husk-spawning vehicle and a loaded transport; assert zero Health actors at tick +2.

### 6. Crew re-entering a vehicle is removed but never disposed — HYPOTHESIS, NEW

- **Site:** `Activities/EnterAsCrew.cs:45-76` — frame-end task ends `w.Remove(self)` with no
  `Dispose()`; re-ejection spawns a fresh actor (`VehicleCrew.SpawnCrewActor`). The frame-end task checks
  `self.IsDead` but not `targetActor.IsDead`.
- **Mechanism:** `World.Remove` drops the actor from `actors` (`World.cs:407`) but its traits stay in the
  TraitDictionary until disposal, so `ITick`s keep running and `ActorsWithTrait` sweeps (e.g.
  `OwnerLostAction`) still see it.
- **Symptom:** one leaked actor per re-crew; possibly ghost kills/explosions at stale positions on
  defeat; a crewman finishing entry the tick his vehicle dies vanishes.
- **Severity / likelihood:** silent leak / possible stray effects · medium.
- **Cheapest verification:** a crew re-entry scenario (none exists) + a test hook counting crew-type
  actors including out-of-world ones; expect 0, code says 1.

### 7. The win condition never runs end-to-end under autotest — KNOWN (partially)

- **Site:** `SupplyRouteContestation.cs:814` `ResolveTeamElimination` returns under
  `TestMode.IsActive`; likewise `ConquestVictoryConditions.cs:62`, `MissionObjectives.cs:171`. Only the
  pure helpers are NUnit-pinned (`SupplyRouteEliminationTest`). Under TestMode a full defeat bar sets
  `isPassive` and stops, so "defeated outright" is indistinguishable from "passive" in every scenario.
- **Known:** the early return is cited in DISCOVERIES 2026-09-02 (line ≈7586) and `supply-route.md`;
  the *coverage* consequence is not stated.
- **Cheapest verification:** add an opt-in like `DoomsdayStrike`'s `RunInTestMode` so one scenario can
  drive contestation to 100% and assert WinStates.

### 8. A passive player is stranded if the last rescuer leaves by any non-contestation route — KNOWN

`DISCOVERIES.md` 2026-08-22 (line ≈13095) — re-verified at this SHA: `OnDefeatBarFull` early-returns on
`isPassive` (`:705`), and `ConquestVictoryConditions.OnPlayerLost` → `AwardDecidedSurvivors` never
re-evaluates passive teammates. Symptom: 2v2, ally surrenders/disconnects while you are passive → the
match never ends unless the enemy walks off and back. Still unfixed; listed because it is the win
condition.

### 9. Damage-over-time credits the victim and skips list entries — NEW

- **Site:** `OpenRA.Game/Actor.cs:349-362` and `:626-632`. `InflictDamage(Actor _, DamageOverTime)`
  discards the attacker; the tick applies `InflictDamage(this, …)`, so `PlayerStatistics.cs:369`
  (`e.Attacker == self`) drops the kill. `DOT.RemoveAt(i)` has no `i--`, skipping the next entry that
  tick.
- **Symptom:** nuclear heat-radiation kills (`weapons-superweapons.yaml:351,1072`) give the firer no
  kill credit, stats or experience. Deterministic, **not** a desync. `DOT` and
  `AverageDamagePercent` (which steers `AutoTarget`) are not in the sync hash, so a future divergence
  there would surface late on unrelated fields.
- **Cheapest verification:** autotest nuking a cluster, assert attacker `UnitsKilled` rises.

### 10. `MapLayers.ResetExploration` reads counters nothing writes — NEW

- **Site:** `OpenRA.Game/Traits/Player/MapLayers.cs:480-489` uses `visibleCount + passiveVisibleCount`
  (upstream `Shroud.cs` logic); the mod's `AddSource`/`RemoveSource` only maintain `visibilityCount[]`
  (`:355-421`). Verified: the two legacy layers are allocated (`:184-185`) and never incremented.
- **Symptom:** after DevAll-off, `DevResetExploration` or a hide-map crate, every cell is unexplored
  including cells your stationary units see; they stay black until a source re-adds. Order-driven, so
  every client agrees — not a desync.
- **Cheapest verification:** NUnit: add a vision source, tick, `ResetExploration`, tick, assert visible.

### 11. Bot-module latent crashes and silent stalls (host-only, no desync)

| Site | Mechanism | Symptom | Likelihood | Cheapest check |
|---|---|---|---|---|
| `SupplyFollowerBotModule.cs:2626` `GroundDangerAt` | `dangerField` null-dereffed; nulled at `:762` when `!participates` or all three danger flags off; called unconditionally from the errand log `:1233` | host NRE crash on first truck scan | latent: both shipped types participate and set the flags; any scenario/profile turning them off crashes | NUnit/scenario with flags off |
| `PoiOffensiveBotModule.cs:3140` `StageFreePool` | `controlField.Info.CellSize` dereffed on the fallback path | host crash | latent: `world.yaml` ships `ControlField` | scenario without `ControlField` |
| `HelicopterSquadBotModule.cs:848,910` | `TraitOrDefault` on `idleHelicopters` entries without `IsDead`; safe only because cooldowns (900/400/600) are multiples of `ScanInterval` (100) | host crash on a retune | latent | add `!h.IsDead`, or NUnit asserting the multiples |
| `CaptureCoordinatorBotModule.cs:1641,1655` | `reserveCells` dedup only pruned on death/owner change; a capturer back at its old reserve cell is never re-staged | TECNs pile at the SR (plausibly the `[exp-clog]` symptom) | HYPOTHESIS, medium | pure `ShouldReissueReserve` helper + NUnit |
| `MountedTransportBotModule.cs:1131-1139,1610-1615` | drop cell picked by terrain cost only (`BotTerrain.PassableFor`), Delivering has no timeout | loaded IFV parked short of the front for the match | HYPOTHESIS | `wip-transport-delivers` clone across a cut bridge |
| `HelicopterStates.cs:742-765` | with `StandoffEngagement: true` (both profiles) Approach has no arrival exit; AttackMove re-queued every squad tick | order churn until `stuckTicks > 200` | HYPOTHESIS, likely | one `test-heli-standoff` run, count orders |

### 12. Latent desync / crash hazards with no live trigger today

- **Host-only state on world layers, no detector** — `ControlField.RequestFrontlineProfile`
  (`ControlField.cs:938-942`) and `CrossingMap` lazy build (`:394-404`) mutate world traits from bot
  ticks only. Safe today: no sim reader. The next sim trait that reads them desyncs silently. This is
  the "reverse class" already filed as having no detector (`bugs/discovered.md` 2026-09-22) — these are
  two concrete sites for it. Check: static test forbidding `TraitOrDefault<ControlField|CrossingMap|
  DangerFieldLayer|BeliefStore>` outside BotModules/overlays/scripting.
- **ShadowCache key omits the engine build** (`OpenRA.Game/Map/ShadowCache.cs:84-94`) — keyed on map
  UID + density hash + `AlgoVersion` (1). A dev build that changes the trace without bumping the version
  leaves a stale cache that drives vision and LoS; vision is unhashed, so the desync report names a
  downstream field. Check: golden NUnit pinning the SHA of `SaveShadowsBinaryData()` on a fixture map,
  and log the digest at map load.
- **`MissileSpawnerMaster.cs:93-127`** — the launched slot clears at frame end, so two fire events in
  one tick add the same actor twice → `World.Add` duplicate-key throw on every client. Not reachable
  (HIMARS/Iskander have one armament, no `Burst`). Check: scenario override `Burst: 2`, `BurstDelays: 0`.
- **`DamageWarhead.cs:236-243`** — `victim.Trait<Armor>()` / `.First(!IsTraitDisabled)` throw with zero,
  multiple or all-disabled Armor. No `Armor@` variants today. Check: lint "every Health actor has exactly
  one Armor".
- **`(int)NaN` is platform-defined** — `RangeDamageFactor` with a zero-range weapon and
  `DamageAtMaxRange != 100` is non-finite; x64 and ARM64 cast it differently, i.e. a **Mac-vs-Windows
  desync** the day someone sets it on an `Explodes` payload. The YAML was fixed (`weapons-explosions.yaml`
  `:546,:592,:1047`, pinned in `BallisticPenetrationTest`) but nothing **forbids** it. Check: lint rule.
  Separately, `RangeDamageFactor` goes negative beyond 2× range at `DamageAtMaxRange: 50` (ten ballistic
  weapons) — heals the target if an impact can land that far from the muzzle (HYPOTHESIS on reach).
- **`ScoutBotModule.cs:240-241`** `world.SharedRandom` in a bot tick — **KNOWN** (DISCOVERIES ≈17031),
  dead behind `threatMap == null`.
- **`World.cs:252`** WorldActor assignment order — **KNOWN**, `make worldactor-gate`.

### 13. Perf / log volume (host CPU, no correctness effect)

Per-tick whole-world scans: `HelicopterSquadBotModule` `FindOwnSupplyRoute` (`:729-734`) and
`CountLiftCandidates` over every `Mobile` (`:1757`), `HelicopterStates` Idle `FindClosestEnemy` + SR
scan every 5 ticks (`:525-534`); `PoiOffensive` up to 5 world passes per reeval; `MapLayers.AddSource`
evaluates `TraitsImplementing<IAirborneVisibility>().Any()` per cell (`:353-359`, hoistable).
Ungated `Log.Write("debug")` per capturer per scan in `CaptureCoordinatorBotModule.cs:735-742` (and
`:777,:1308,:1456,:1595`) and per carrier in `MountedTransportBotModule`.

---

## Refuted during review (so nobody re-derives them)

- **"`CaptureCoordinatorBotModule` `INotifyKilled.Killed` is dead code"** — wrong. `Health.cs:128,135`
  fans `INotifyKilled` out to the **owner's player actor** with `self` = the dying actor, which is what
  the module's comment at `:2253-2258` says.
- **`DelayedWeaponAttachable.cs:123` reads `LocalPlayer`** — inside `ISelectionBar.GetValue`,
  render-only.
- **`World.LocalRandom` is a per-client desync source** — no: seeded from the lobby seed and consumed
  only by bot modules and cosmetics (already retracted in `bugs/discovered.md` ≈2882). Side effect worth
  knowing: on `nuclear-winter-ww3`, `WeatherOverlay` draws from the same stream at a
  resolution-dependent rate, so hidden vs visible seeded runs diverge in **bot decisions** (benchmark
  reproducibility only).
- **SupplyRouteContestation's own tick** — integer maths, `List<Actor>` summed commutatively, notification
  latches deliberately unsynced and cosmetic-only. Clean.

## Checked and clean (one line each)

No `try/catch` in any bot module; no mutable statics in sim code (`SweepMemo` is `[ThreadStatic]` render,
`MissileTrace`/`GameSaveRoundTripProbe` are test-only); all 40 actor-keyed bot dictionaries prune dead
actors before trait reads; every `First`/`[0]`/`Min`/`Max` in the large modules is count-guarded;
world-layer iteration that affects outcomes is ActorID- or ordinally-sorted; `Vaporizable` floats drive
render only; `-Vaporizable:` present on `SUPPLYROUTE` (`structures.yaml:318`); every damage warhead but
`VaporizeWarhead` calls `IsValidAgainst`, and no direct `InflictDamage`/`Kill` site reaches the SR;
`OrderManager`/`SyncReport` additions are diagnostic and leave the wire hash unchanged; `World.Timestep`
writers are local-pacing only.

## Suggested order of work

1. Close #1: extend `BotOrderedMutationTest` (RED first, per `feedback_red_before_green`), then convert
   `:3406` to an order. `@stable` is affected — say so in the commit and re-take the benchmark baseline.
2. One-liners with cheap pins: #3 (`RestockSupply` ally check), #11 `GroundDangerAt` null guard, #10.
3. Behavioural, scenario-backed: #2, #4, #5, #6 — each needs one `run-test.sh` slot.

## Side note

`CLAUDE.md`'s hard-rules paragraph cites `SUPPLYROUTE:` at `structures.yaml:222` and
`SupplyRouteContestation` at `:303`; at this SHA they are `:295` and `:396` (`-Vaporizable:` `:318`,
`NoAutoTarget` `:390`). Not edited here — out of scope for a read-only scout.
