# Engine robustness scout #2 — desync, crash and stall risks NOT covered by the first scout

**Read at `main @ e3ad8efe`** (worktree `wt/scout-rob`). Static reading only: no build, no launch, no
autotest, no lint. Every `file:line` below was read at that SHA.

**Overlap rule.** A parallel scout landed first: `DOCS/design/261007_engine-robustness-scout.md`
(merged `754999d5`, read at `c276679c`). Where a finding touches one of its items this file cites its
number as **[S1 #n]** and does not restate it. Its "Refuted" and "Checked and clean" lists are not
re-derived here. Item 42 (2-human desync) is not duplicated; finding 1 below is a separate mechanism,
and findings 2–3 are instruments that would bear on 42.

Ranking = (player-visible severity) × (how reachable in a shipped match) × (how cheap to close).
**CONFIRMED** means the defect was established by reading code end to end. **HYPOTHESIS** means a
plausible mechanism with a stated confirming step.

---

## 1. A cloaked mine's visibility *query* writes synced state — the enemy's renderer and the host-only bot both trigger it — CONFIRMED by reading, NEW

**What the player sees today.** In a two-human game, an engineer comes within two cells of an enemy
mine while the engineer's owner has that mine on screen, and the match goes "Out of sync". The same
write happens from the bot's host-only tick, so any game with a bot can diverge the same way. Saved-game
restore and replay playback diverge too. And in single-player, a mine you are looking at stops
re-cloaking.

**After the fix.** The mine re-cloaks on the same tick on every machine, whoever is watching it.

**Mechanism.**
- `Cloak.ShouldHide(self, viewer)` is an `IShouldHideModifier` query, but it has a side effect.
  `engine/OpenRA.Mods.Common/Traits/Cloak.cs:322-335`:
  ```
  var shouldHide = Cloaked && !self.World.ActorsWithTrait<DetectCloaked>().Any(...viewer...);
  if (!shouldHide)
      Reveal(Info.RevealedDelay);
  ```
- `Reveal(int)` writes `remainingTime = Math.Max(remainingTime, time)` (`:178`). `remainingTime` is
  `[Sync]` (`:121-122`). `Cloak` derives from `PausableConditionalTrait` → `ConditionalTrait`, which
  implements `ISync` (`Conditions/ConditionalTrait.cs:41`), so the field really is in the hash.
- `Cloaked` is `remainingTime <= 0` (`:164`), and `ITick` decrements it (`:222-223`). So whoever calls
  the query decides when the mine re-cloaks.
- The query returns early for allies (`:325-326`). Only an **enemy** viewer gets as far as the write.

**Callers that run on one machine only.**
1. **Render, every frame, enemy client only.** Mines carry `FrozenUnderFog` (`mods/ww3mod/rules/misc.yaml:15`).
   - `FrozenUnderFog.ModifyRender` calls `IsVisible(self, self.World.RenderPlayer)`
     (`Traits/Modifiers/FrozenUnderFog.cs:194`). That calls `cloak.ShouldHide(self, byPlayer)` (`:140-141`)
     once the AlwaysVisible check (owner and allies) has passed (`:136-137`).
   - The mine's owner never gets past that check, so their client never writes. The enemy's client
     writes on every rendered frame while the mine is in view.
   - Other client-local callers reach the same write through `World.FogObscures(Actor)` → `CanBeViewedByPlayer(RenderPlayer)`
     (`OpenRA.Game/World.cs:109`, `Actor.cs:641-648`):
     - mouse hover (`MouseTargetVisibility.cs:63`)
     - the minimap (`Widgets/MiniMapWidget.cs:419`)
     - decorations (`Render/WithDecorationBase.cs:152`)
2. **Bot tick, host only.** `SquadManagerBotModule.IsNotHiddenUnit` calls `v.ShouldHide(a, Player)` on
   enemy actors (`Traits/BotModules/SquadManagerBotModule.cs:189-200`, used at `:236`, `:241`).
   Nineteen `CanBeViewedByPlayer`/`IsNotHiddenUnit` sites in `Traits/BotModules/` also route through
   `Actor.CanBeViewedByPlayer` → `ShouldHide`.
   - `Player.cs:253` enables bots only `if (IsBot && Game.IsHost)`, so this is the item-42 (ii) class.
   - It slips past `BotOrderedMutationTest`: the test looks for six named mutators one hop deep, and
     here the write sits behind a **query**.
   - [S1 #1] is a different site of the same family.

**Reachable in shipped content.**
- Mines are laid by the minelayer truck (`MNLY`, `Minelayer: Mine: MINV`, `rules/ingame/vehicles.yaml:504,525`,
  buildable at `~techlevel.medium`, `:510`). The Russia AI builds up to ten (`rules/ai/ai-russia.yaml:35`).
- Mines are also laid by engineers (`^E6`, `rules/ingame/infantry.yaml:1972`).
- The detector is the engineer itself: `DetectCloaked@Mine: Range: 2c0` (`infantry.yaml:2022-2024`).
  Engineers are buildable by both factions (`E6.america`/`E6.russia`, `infantry-america.yaml:96`,
  `infantry-russia.yaml:96`, `~techlevel.infonly`).
- A revealed mine stays "not hidden" for every query until `remainingTime` reaches 0 (`RevealedDelay: 200`,
  `misc.yaml:35`). That is exactly the window in which the enemy's renderer keeps pushing it back to 200.

**Why nothing caught it.**
- The engine's own unsynced-code guard does not wrap the render pass (finding 2).
- The bot guard `Debug.SyncCheckBotModuleCode` *would* catch the bot channel. It is off by default and
  has only ever been switched on by hand.
- History: the side effect arrived in `f5de5ee1` (2024-08-13, "error fixes"), together with
  `RevealedDelay`. The commented-out caching `IsVisible` beside it (`:338`) is labelled "Desynced, hence
  uncommented", so someone met this family once already and fixed the cache rather than the write.

**Ranking rationale.** This is a live desync on content both factions field. It needs no bot, a single
engineer and a minelayer are enough, and item 42 makes desync a release blocker. The bot channel also
corrupts save and replay. The fix is small.

**Cheapest verification.**
- **Static (preferred, RED today):** add the query-purity guard from finding 4. On this tree it must fail
  naming `Cloak.ShouldHide → Reveal → remainingTime`.
- **Dynamic, one run once finding 2 lands:** a scenario with an `E6` parked within 2c0 of an enemy `MINV`,
  camera on the mine, rendered as the engineer's owner, `Debug.SyncCheckUnsyncedCode=true`.
  - The bar: RED is an `InvalidOperationException: RunUnsynced: sync-changing code may not run here`
    thrown from the render path. GREEN is 600 ticks with no throw.
  - Without finding 2 there is no single-process detector for the render channel. Two-client is the only
    other way, so do finding 2 first.

**Dispatch brief (code lane).**
- Make `Cloak.ShouldHide` pure: delete the `Reveal` call at `:331-332`.
- Move detection-driven reveal into `ITick` (`:218`):
  - every `Info.UpdateFrequency` ticks (field exists, `:109`; mines set 20), compute "is any non-allied
    `DetectCloaked` with overlapping `DetectionTypes` in range";
  - if so, `Reveal(Info.RevealedDelay)`.
  - Iterate players or detectors in a synced order (`World.Players` / `ActorsWithTrait`), never
    `RenderPlayer`.
- Leave the commented-out blocks alone, or delete them in the same commit with a `// PITFALL:` at
  `ShouldHide`: "a query on IShouldHideModifier runs from render and from host-only bot ticks — it must
  not write [Sync] state".
- Behaviour shifts: reveal timing becomes tick-cadenced instead of query-driven, and the bot channel
  disappears. `@stable` is affected (bots query mines), so say so in the commit and re-take the benchmark
  baseline knowingly.
- RED-before-green with finding 4's guard.

---

## 2. The render pass is outside the engine's unsynced-code guard, and no autotest ever arms the guards — CONFIRMED by reading, NEW

**What the player sees today.** Nothing, until a desync. The engine has a detector built for exactly the
finding-1 class, `Sync.RunUnsynced(checkSyncHash, …)`. It hashes the world before and after a block and
throws if the block changed synced state (`OpenRA.Game/Sync.cs:182-202`). It is blind where it matters
most.

**After.** A single-process autotest throws at the exact line that writes synced state from render,
UI, an order generator or a bot. It needs no second client and no saved game.

**Evidence.**
- `Game.cs` guards the UI tick (`:796`), `orderManager.TickImmediate` (`:811`), the order generator
  (`:822`) and `world.TickRender` (`:831`).
- `RenderTick` (`:879-930`) runs `worldRenderer.PrepareRenderables()`, `worldRenderer.Draw()` and
  `DrawAnnotations()` with **no** `RunUnsynced` wrapper. Every `IRenderModifier.ModifyRender`,
  `IRender.Render` and decoration query therefore runs unguarded. `FrozenUnderFog.ModifyRender` is one
  of them (finding 1).
- `Debug.SyncCheckUnsyncedCode` and `SyncCheckBotModuleCode` both default `false` (`Settings.cs:186,189`).
- No script under `tools/autotest/` sets either flag (grep for both names returns nothing there).
- DISCOVERIES 2026-08-12 (≈16973) records the bot guard firing **once, when switched on by hand**. That
  run is the only time it has been armed.

**Ranking rationale.** It is a gate, not a defect, but it is the cheapest dynamic detector for the whole
"client-local or host-local code mutates sim" class. That class is the cause of record for both
item-42 leaks and for [S1 #1]. It costs one wrapper plus one test profile.

**Cheapest verification.**
- Wrap the three render calls in `Sync.RunUnsynced(Settings.Debug.SyncCheckUnsyncedCode, world, …)`.
  - When the flag is off, `RunUnsynced` costs one counter increment and no hash (`Sync.cs:187`).
  - Shipped behaviour is identical.
- Then one `run-test.sh --hidden` of a bot-vs-bot tournament scenario with both flags on, with a stated bar:
  - PASS = match reaches its verdict with no `RunUnsynced` exception;
  - FAIL = the exception, whose stack names the writer.
- Expect this to go RED on finding 1 (if mines get laid in the run) and on [S1 #1]. That is the point.

**Dispatch brief.**
- `OpenRA.Game/Game.cs` `RenderTick`: wrap `PrepareRenderables`, `Draw` and `DrawAnnotations` in
  `Sync.RunUnsynced(Settings.Debug.SyncCheckUnsyncedCode, worldRenderer.World, …)`.
  - Keep `IsLoadingGameSave` gating as is.
  - Note the guard already skips re-entrant hashing (`unsyncCount == 1`, `Sync.cs:187`), so nesting is
    safe.
- Add a `--sync-guard` switch to `run-test.sh` that passes `Debug.SyncCheckUnsyncedCode=true
  Debug.SyncCheckBotModuleCode=true` on the launch line. Hand-editing `settings.yaml` does not survive
  the game's own save (DISCOVERIES ≈17239).
- Document in `DOCS/recipes/AUTOTEST.md` that a sync-guard run is ~2 extra full-world hashes per guarded
  call (DISCOVERIES ≈14814 sizes the cost), so it is for a dedicated run, not for every test.
- Manager-sanctioned run only (simulation authority).

---

## 3. A whole-match replay re-verification gate for the "host-only mutation" class — CONFIRMED mechanism, NEW as a gate

**What the player sees today.** The only end-to-end determinism instrument,
`test-savegame-resume-riverzeta`, covers **one map up to tick 3000**. Every leak in the class lives
wherever a bot or renderer first touches the offending state, which can be any map at any minute.

**After.** Any recorded bot-vs-bot match is re-simulated from its recorded orders with bots disabled.
Any host-only write anywhere in the match surfaces as an out-of-sync at its first diverging frame.

**Why it works, already established in-tree.**
- Bots do not run in a replay (`ModularBot.Activate` returns on `World.IsReplay`, `Traits/Player/ModularBot.cs:136-139`).
  So the replay is exactly "the orders, without the host-only side effects".
- Replay playback compares the recorded hashes against the local ones and raises `OutOfSync`
  (`DOCS/reference/architecture.md` §"Replays", `ReplayConnection.cs:101-118`, `OrderManager.cs:225-234`).
- `World.IsOutOfSync` is readable (`World.cs:118`), and the Lua test surface already checks it
  (`Scripting/Global/TestGlobal.cs:122`).
- `Launch.Replay` drives playback without the browser (`tools/autotest/watch-replay.sh`).
- The engine's `Debug.SyncCheckBotModuleCode` sweep compares hashes around bot ticks, and the activity
  queue is not `[Sync]` (item 42 (iii) correction). A replay sidesteps that blindness, because a
  diverged queue eventually moves a hashed field.

**Ranking rationale.**
- It covers all ten maps and whole matches.
- It catches the class finding 4 cannot see statically: writes buried more than one call deep, and
  "synced code reading state only bot ticks refresh" ([S1 #12] first bullet; `bugs/discovered.md`
  2026-09-22).
- It costs two launches per map: record, then replay.
- It does **not** catch the render channel (finding 1). A replay viewer's renderer is itself a client,
  so it would introduce, not detect, render-side writes.

**Cheapest verification (the gate's own RED).**
- Revert [S1 #1]'s fix (or temporarily reintroduce one direct `GrantCondition` in a bot module) and
  confirm the replay goes out of sync.
- The bar is a specific frame and an `OutOfSync`. A clean exit is not the bar.

**Dispatch brief.**
- New `tools/autotest/run-replay-verify.sh <scenario> <seed>`. Steps:
  1. run the tournament scenario with replay recording on (`Settings.Game.RecordReplays`, launch-line
     override);
  2. locate the `.orarep` from that run's directory, not "newest in the folder";
  3. relaunch with `Launch.Replay=<file>` at max speed and `--hidden`;
  4. a small World trait or Lua hook writes `outcome=pass` only when playback reaches its last frame
     with `!IsOutOfSync`, and `outcome=fail frame=N` otherwise.
- Use the three-way exit discipline of `run-smoke.sh`: 0 pass, 2 diverged, 3 launch failure.
- Separately, render-side writes can happen on the replay viewer, so pin the replay's render player to
  a spectator (`RenderPlayer == null`) to keep the replay itself from mutating anything.

---

## 4. NUnit guard: visibility/targeting/render *queries* must not write `[Sync]` fields — CONFIRMED gap, NEW

**What the player sees today.** Nothing. The existing structural guards each pin a narrower property:
- `SyncAnnotationTest` checks that `[Sync]` is hashable and on an `ISync` trait;
- `BotOrderedMutationTest` checks that bot code doesn't call six named mutators;
- `FrozenActorTargetingTest` checks that cursors don't read fog-hidden state.

None asks whether a method that callers treat as a **pure query** stores to a synced field.

**After.** Finding 1 is RED in CI. The next "cache a visibility answer in a field" or "reveal on
query" fails before merge rather than in a two-human game.

**Evidence that the scanner exists.**
- `OpenRA.Test/IlScan.cs` provides `Scan(MethodBase)` for callees (`:61`) and `ScanFieldWrites` for
  `stfld` (`:104`).
- It has the floor-assertion discipline already documented in its header.

**Scope of the guard.**
- Implementations of `IShouldHideModifier.ShouldHide`, `IVisibilityModifier.IsVisible`,
  `IDefaultVisibility.IsVisible`, `ITargetable.TargetableBy`, `IRenderModifier.ModifyRender`/`ModifyScreenBounds`,
  `ISelectionBar.GetValue`, `ITooltip*`, and decoration and annotation renderers.
- For each, walk callees up to depth 2, staying within `OpenRA.Mods.Common` and `OpenRA.Game`.
- Fail if any reached method does `stfld` on a field carrying `[Sync]`.

**Sweep already done by hand.**
- A regex sweep over 40+ query-method names in both assemblies (excluding `Widgets/`) found **one**
  synced write: Cloak.
- The other hits are `out` parameters (`cursor = …` in `CanTarget`, `isAura` in
  `SupplyProvider.cs:851/876`), render caches (`Bridge.cs:231`, `ExitsDebugOverlay.cs:58-90`,
  `DrawLineToTarget.cs:234-241`) and locals.
- So the guard should start green except for Cloak, which makes a clean RED.

**Dispatch brief.**
- `OpenRA.Test/OpenRA.Mods.Common/QueryPurityTest.cs`, using `IlScan`.
- Assert a floor on resolved methods (>200) so it cannot pass vacuously.
- A `KnownImpure` list that may shrink but not grow, same rule as `SyncAnnotationTest`.
- Land it RED with Cloak listed, then remove the entry in finding 1's fix commit.
- `.\make.ps1 check` is mandatory, because this is C# in the test project (RCS rules bite there,
  CLAUDE.md 2026-09-11).

---

## 5. "Control All Units" makes order *validation* depend on each machine's own player — CONFIRMED by reading, cheats-only, NEW

**What the player sees today.** In a cheats-enabled multiplayer game, one player toggles Control All
Units and orders an enemy's unit. That order executes on that player's machine and is rejected on every
other → out of sync. Replays and spectators of any game where it was toggled reject what the players
accepted.

**After.** Validation depends only on synced state.

**Evidence.**
- `Traits/World/ValidateOrder.cs:28-30` returns `order.Subject.AcceptsOrder(…)` and skips the ownership
  check if `DeveloperMode.IsControlAllUnitsActive(world)`.
- That reads `world.LocalPlayer`'s own `DeveloperMode.ControlAllUnits` (`Traits/Player/DeveloperMode.cs:344-356`),
  which is null for spectators and replays (`:351-352`).
- The flag is toggled per player by order (`:189`) or for all players (`:206`). Even "for all" is
  local-player-relative here.
- Added by the mod in `c1bf83d7` (2026-03-24).

**Ranking rationale.** It is a guaranteed desync, but only under cheats. Cheats are a lobby option and
testers use them. Cheap fix.

**Cheapest verification.** Read-only today. A unit test can call `ValidateOrder.OrderValidation` with a
world whose `LocalPlayer` differs and assert an identical answer. That needs a `World` stub, so
practically the fix plus code review is the bar.

**Dispatch brief.**
- In `ValidateOrder`, replace `IsControlAllUnitsActive(world)` with a check on the **issuing client's**
  player: resolve `clientId` → that client's `Player` → its `DeveloperMode.ControlAllUnits`, which is
  synced because it is set by order.
- Keep the shellmap special case.
- Leave `Selection.cs:170` and `UnitOrderGenerator.cs:30/122/229/313` on `LocalPlayer`. Those are
  order-*generation* sites and correctly client-local.

---

## 6. Shadow layers are generated with float maths and have never been compared across CPU architectures — HYPOTHESIS, extends [S1 #12]

**What the player sees.** Possibly: a Windows player and an Apple-silicon Mac player disagree on
concealment from the first tick. Shadow drives vision attenuation and firing LoS (`Map.cs:511-517`,
stated in-code). [S1 #12] covers a stale cache key. This one is two *fresh* generations disagreeing.

**Evidence.**
- `Map.RecomputeShadowFrom` computes `var dH = deltaXY.Length / 1024f;` (`OpenRA.Game/Map/Map.cs:1233`),
  `var t = dot / (float)deltaLengthSquared;` (`:1249`), `z_a * (1 - t)` (`:1251`), and
  `totalAirborne += DensityLayer[tile] / ShadowAirborneDivisor;` (`:1255`).
- Then `Math.Ceiling(totalAirborne)` (`:1270`) and a byte cast.
- These are plain IEEE single-precision `+ - * /` with no transcendental and no `a*b+c` fusion candidate
  on the accumulating line. So per IEEE they should agree, which is why this stays a hypothesis.
- The project's only cross-runtime FP evidence (`tools/fp-determinism/README.md`) was taken on macOS
  **x64** against Windows x64. This machine is an Intel i7-9750H (`uname -m` = `x86_64`).
- **No ARM64 run of any kind exists.**

**Ranking rationale.** Cross-architecture play is plausible for a public release (itch/ModDB Mac
downloads) and would be a from-tick-zero desync. The mechanism is unproven and probably benign.

**Cheapest verification.** No game launch needed.
- Run `SaveShadowsBinaryData()` for one shipped map on an ARM64 runtime and an x64 runtime and compare
  the SHA. This is the digest [S1 #12] already proposes logging at map load. Do that, and the comparison
  falls out of two players' `debug.log`s.
- Extend `tools/fp-determinism` with a `--shadows <map>` mode that loads the map headless and prints the
  digest.

**Dispatch brief.** Log the digest at map load ([S1 #12]'s check) and add the ARM64 leg to
`fp-determinism`. Only if they differ: move the trace to integers. `t` can be kept as a rational
`dot / dls` compared against integer heights, and `totalAirborne` accumulated ×5 as an int. Then bump
`ShadowCache.AlgoVersion` (architecture.md:344).

---

## 7. Float accumulation on the sim path outside the probed cohesion kernels — HYPOTHESIS, low, NEW census

**What the player sees.** Nothing today. The project's determinism model is integer-only, and the
cohesion maths is the only float code ever probed.

**Census.** Files where `float`/`double` reach synced state, render and bot files excluded:
- `Activities/BallisticMissileFly.cs:36-37`: `float currentSpeed; float horizontalProgress;`, accumulated
  per tick and feeding the missile's position.
- `Warheads/DamageWarhead.cs:118,172-179`: range fraction and directional armour modifiers.
- `Projectiles/Bullet.cs:188,260`.
- `Traits/SupplyProvider.cs:765,1084`: the need ratio decides who gets served.
- `Traits/InfersUpkeep.cs:35-40`: upkeep cost, i.e. money.
- Transcendentals (`Math.Sin/Cos/Atan2/Pow`) appear only in render or cursor code:
  `SelectDirectionalTarget.cs:136`, `ShockwaveDamageWarhead.cs:166` (`FadeOutAt`, render),
  `MarkerLayerOverlay.cs` (editor). This agrees with [S1]'s clean list for `Vaporizable`.

**Why low.** .NET's JIT does not contract `a*b+c` into FMA implicitly, and IEEE `+ - * /` and `Sqrt` are
correctly rounded on x64 and ARM64. The real cross-platform trap is `(int)NaN`/overflow casts, which
[S1 #12] already filed.

**Cheapest verification.** Run the same ARM64 leg as finding 6 over `tournament-arena-skirmish-2p` with
`run-synchash.sh` (it already diffs per-frame hashes across runtimes). One run per architecture.

**Dispatch brief.** None until finding 6's ARM64 leg exists. Then fold the missile and upkeep paths into
it if the hashes differ.

---

## 8. A client launched with `Test.Mode=true` can join a real game and diverge at the end of the match — CONFIRMED by reading, low, extends [S1 #7]

[S1 #7] covers the *test-coverage* side of `TestMode` gating the win condition. The multiplayer side:
- `TestMode.IsActive` is a **launch-argument** flag, not lobby state.
- It suppresses defeat and victory in `ConquestVictoryConditions.cs:62`, `StrategicVictoryConditions.cs:86`
  and `MissionObjectives.cs:171`, and changes `DoomsdayStrike.cs:544` and `MissileStrikePower.cs:686`.
- Defeat state rides in every sync packet (`OrderManager.cs:225-234`, `DefeatState`). So a test-mode
  client in a human lobby goes out of sync the moment anyone is defeated.
- The handshake fingerprint (`BuildFingerprint`) does not include it.
- Reachable only by someone launching with test args. Ranked low.

**Dispatch brief.** Either have the server refuse to start a multiplayer game when any client reports
`TestMode` (extend the client's handshake info), or have `TestMode.IsActive` force-false when
`OrderManager` sees more than one human client. One `if` plus a log line.

---

## 9. Small actor-ID leaks — CONFIRMED by reading, memory only, low

- `Traits/Player/DefconCasualtyObserver.cs:73` `lastEnemyDamager`:
  - it is added on damage (`:167`) and removed only in `Killed` (`:189`);
  - capture moves `INotifyKilled` to the new owner (`Health.cs:131-135`), so the old owner's entry
    never clears;
  - an evacuation refund disposes without a kill.
  - It is read only by `TryGetValue`, so it costs no per-tick work and carries no crash risk.
- `Traits/BotModules/StarvingRecruitGate.cs:47` `held`: a unit that dies while held leaves its ID behind.
  The set is never enumerated (`:31`).

**Brief.** Prune both in an existing `INotifyActorDisposing`/owner-change hook. Not worth a slot of its
own; fold into the next touch of either file.

---

## Checked and clean in this pass (one line each; complements [S1]'s list, does not repeat it)

- **Lua.** `ScriptContext.cs:183-200` whitelists globals and nulls `math.random`/`math.randomseed`, and
  `os` is never exposed. Three shipped maps carry Lua and none uses `pairs(`, `os.*` or local-player
  branching. `Utils.Random` is `SharedRandom` (`UtilsGlobal.cs:124-146`).
- **Dictionary/HashSet order.** `Actor.GetHashCode` is `ActorID` (`Actor.cs:441-444`), and .NET
  enumeration order follows the insert/remove history, not hash values. Every actor-keyed bot or world
  collection examined is pruned before trait reads. The one HashSet iterated straight into orders
  (`LaneAmbushBotModule.cs:278`) is filled only from sim.
- **Unbounded growth.** Every actor-keyed collection in mod World/Player traits and bot modules is
  pruned, except finding 9's two ID sets, `CaptureCoordinatorBotModule.everOwnedStructures`
  (deliberate, `:1152`), the DoomsdayStrike end-of-match lists (bounded by warheads) and
  `BotVsBotMatchWatcher` income samples (harness only).
- **`ResolveOrder` implementations added by the mod.** No reads of `Selection`, `LocalPlayer`,
  `RenderPlayer`, `Game.Settings`, `Viewport` or `RunTime`: `CrewMember`, `DropsCrate`,
  `GarrisonManager`, `HeliEmergencyLanding`, `GarrisonPortOccupant`, `AttacksSupplyRoutes`,
  `DropsSupplyCache`, `AttendAlly`, `CarrierMaster`. `PatrolOrder.Resolve` clamps the replicated route
  (`Orders/PatrolOrder.cs:72-86`).
- **Widgets, order generators and render traits.** No direct calls to `QueueActivity`, `GrantCondition`,
  `Reveal`, `ChangeOwner`, cash or `Kill` from `Widgets/`, `Orders/`, `Traits/Render/` or overlays.
  The exceptions are upstream `WithMakeAnimation`, which is sim by design (conventions.md §"Do NOT fix
  the 40"), and `ControlAllUnitsManager.MarkPlayerControlled`, whose only reader is the host's bot-order
  drop (`ModularBot.cs:295`). That is host-decided and replicated as orders, so it can't desync.
- **`LocalRandom` outside bots.** Used only by `LeaveSmudgeWarhead.cs:55`, `CreateEffectWarhead.cs:152`,
  `Bridge.cs:159,323` (radar colour) and `WeatherOverlay`. `SmudgeLayer` is render-only (`:173`, no
  `ITick`, no sim reader). `Building.cs:422` only clears smudges.
- **Player-actor construction order.** No mod Player-location trait reads `world.Players`, `MapLayers`,
  `FrozenActorLayer` or `HomeLocation` in its constructor or `Created`. Those are assigned after
  `PlayerActor.Initialize` (`Player.cs:245-249`) or by `World.SetPlayers`. Same family as the
  WorldActor gate, and clean.
- **`SupplyRouteContestation` wall-clock fields.** Deliberately unsynced and cosmetic (`:208-217`).

## Side note (not this angle)

`AmmoPool.cs:985-987` and `:1336-1338`: `double a = Info.Ammo / Info.FullReloadSteps;` divides ints
before the cast, so `Math.Ceiling` is a no-op and `reloadCount` truncates. Deterministic, but probably
not the intended rounding. Worth one line in `bugs/discovered.md` from whoever next touches reload.

## Suggested order

1. Finding 4 (guard, lands RED), then finding 1 (fix, turns it green). One C# branch, with `check`
   mandatory.
2. Finding 2: wrap the render pass and add `--sync-guard`. Then **one** manager-sanctioned sync-guard
   tournament run.
3. Finding 5: a one-function fix.
4. Finding 3, the replay-verify gate, when a launch budget exists. It is the long-term net for item 42's
   class.
5. Findings 6 and 7 together, once anyone has ARM64 hardware.
