# Engine robustness — dispatch briefs (2026-10-07)

**Read at `main @ 754999d5`** (branch `wt/groom-tests`, rebased onto it). Source:
`DOCS/design/261007_engine-robustness-scout.md` (scout read at `c276679c`). Every premise below was re-opened
at `754999d5`; line numbers are re-pointed to that ref. Nothing was built, run or launched — every pass/fail
prediction is read from source, including the RED predictions.

**This file outranks `2026-10-07_test-holes.md`.** R1 is a live multiplayer/replay/save desync on both bot
profiles.

**Gates for every brief** (CLAUDE.md, in order): `./make.ps1 all` → `./make.ps1 check` → `dotnet test
engine/OpenRA.Test/OpenRA.Test.csproj --configuration Release` → `./make.ps1 test`. `check` is the only analyzer
gate and is mandatory for any C# change (`RCS1226` multi-paragraph `<summary>`, `RCS1112`). Briefs that touch
`OpenRA.Game` or a world/player trait also run `./make.ps1 worldactor-gate` (part of `check`) and, before merge,
`./make.ps1 smoke` — read its exit code (0 pass, 2 started-and-hung, 3 launch failure = nothing proven).

**Autotest discipline** (CLAUDE.md): one `./tools/autotest/run-test.sh --hidden <test>` per bug is the normal
flow; anything more needs explicit go-ahead *in the current turn*. Launcher lives in `tools/autotest/`, not the
repo root — exit 127 is a launch failure, not a result. Read `result.json`, never a `| tail` exit code.

---

## R1 — Bot out-of-ammo sweep queues a rearm activity outside the order stream (LIVE DESYNC) — **S–M**

**Goal:** make `BotOrderedMutationTest` see a synced mutation one call deeper than it looks today, go red on
the live instance, then fix the instance by sending an order.

### Premise — confirmed at `754999d5`
- `PoiOffensiveBotModule.SweepOutOfAmmoUnits` (`engine/OpenRA.Mods.Common/Traits/BotModules/PoiOffensiveBotModule.cs:3353`)
  calls `AmmoPool.AutoRearm(unit, true, host);` at **`:3406`**, from the `IBotTick` path (`BotTick` `:1573` →
  `Reevaluate` `:1585`).
- `AmmoPool.AutoRearm` (`engine/OpenRA.Mods.Common/Traits/AmmoPool.cs:1054`) calls `self.QueueActivity(false, …)`
  at `:1067` (`SeekSupplyProvider`), `:1079` (`RideTransport`), `:1088` (`Resupply`). `QueueActivity(false, …)`
  also cancels the current activity.
- Live on both profiles: `EvacuateOutOfAmmoUnits: true` at `mods/ww3mod/rules/ai/ai.yaml:578` (`@experimental`)
  and `:3269` (`@stable`).
- Why the gate is green over it: `BotOrderedMutationTest.IsUnorderedMutation` (`engine/OpenRA.Test/BotOrderedMutationTest.cs:130-144`)
  only matches **direct** calls; `BotLayerTypes` (`:83-106`) adds helper types only by name suffix
  (`HelperSuffixes`, `:81`: `Math/Tactics/Gate/Guard/Blackboard`). `AmmoPool` matches none.
- The same sweep's *other* arm is already correct: `EvacuateOutOfAmmoUnit` issues `bot.QueueOrder(new Order("Evacuate", unit, false))`
  (`:3436`). So R1 is one arm of one method, not a module rewrite.
- Not a bot-layer issue, for the record: `AutoSeekSupplies.cs:321` makes the **same** `AmmoPool.AutoRearm(self, true, host)`
  call from a sim `ITick`, which runs on every client — correct, leave it alone.

### The precedent to copy (found and cited)
`LaneAmbushBotModule` → `AutoTarget`, fixed at `61546a51`:
- sender: `bot.QueueOrder(new Order("SetAmbushGate", u, false) { ExtraData = 1 })`
  (`engine/OpenRA.Mods.Common/Traits/BotModules/LaneAmbushBotModule.cs:636`, also `:280`, `:657`), method
  `EnsureGatedAmbusher`;
- receiver: `AutoTarget.ResolveOrder` arm `if (order.OrderString == "SetAmbushGate") SetAmbushGate(...)`
  (`engine/OpenRA.Mods.Common/Traits/AutoTarget.cs:619-620`), body `SetAmbushGate` (`:630`);
- pins: `BotOrderedMutationTest.TheAmbushGateStillTravelsAsAnOrder` (sender builds an `Order`, calls `QueueOrder`,
  string literal matches) and `AutoTargetStillResolvesTheAmbushGateOrder` (receiver found via
  `GetInterfaceMap(typeof(IResolveOrder))`, dispatches, literal matches). R1 adds the same three-test shape for
  the new order.

### Step 1 — RED first (test only, no fix in this commit or before it is observed)
Extend `BotOrderedMutationTest.NoBotModuleMutatesSyncedStateOutsideTheOrderStream` with a **one-hop deeper**
scan: for every callee of a bot-layer method that is declared in the `OpenRA.Mods.Common` assembly, is **not**
itself a bot-layer type (those are already scanned at depth 0) and is not compiler-generated, IL-scan the callee
once and flag it if **it** calls an `IsUnorderedMutation` method. Exactly one extra level — no recursion. Record
the offence with the path, in this exact format (so the expected text below is checkable):

`{type.FullName}.{method.Name} calls {callee.DeclaringType.Name}.{callee.Name} → {inner.DeclaringType.Name}.{inner.Name}`

**Expected RED on unfixed main** — the assertion at `:183-186` fails with the message starting:

```
A bot module writes synced world state directly instead of issuing an order:
  OpenRA.Mods.Common.Traits.PoiOffensiveBotModule.SweepOutOfAmmoUnits calls AmmoPool.AutoRearm → Actor.QueueActivity
```

followed by the `Incident` text. Keep the existing `resolved > 2000` floor, and add a second floor: the
one-hop pass must have scanned at least N callees (pick N from the first run, record it) so a broken hop reads as
broken, not clean.

**The one-hop pass may find more than R1.** The scout grepped only *static* calls from bot modules into
sim-trait types and found only this one; instance-method callees and `HelicopterStates`-style state classes
(no helper suffix) were not covered. **Any additional offence is a new finding: stop, report it to the manager
with the path line, do not allowlist it, do not fix it in this branch.** If an offence is a provable false
positive (e.g. a callee that only queues on an actor it just created in a sim path), the manager rules on an
explicit, commented allowlist entry.

Do **not** take the minimal shortcut of adding `AmmoPool.AutoRearm` to `IsUnorderedMutation` — it would go red
on R1 and stay blind to the next instance of the same shape. Mention it as rejected in the commit.

### Step 2 — the fix
- **Receiver:** a new order string (suggest `"RearmAtHost"`; name it once as a `const` in the test the way
  `GateOrder` is) resolved in `AmmoPool`'s existing `IResolveOrder.ResolveOrder` (`AmmoPool.cs:1013`), target
  = the host actor. On resolve:
  - re-validate the target: `order.Target.Type == TargetType.Actor`, actor not dead, in world, still allied with
    `self.Owner`. If invalid, do **nothing** — the bot sweep re-decides on its next re-eval. Do **not** fall back
    to `AutoRearm(self, true, null)`: that re-picks via `ChooseResupplier` (stock, not affordability) — the
    trap the `host` parameter's doc comment (`AmmoPool.cs:1048-1052`) exists to prevent;
  - skip if `IsSeekingRearm(self)` (same guard the sweep uses, `PoiOffensiveBotModule.cs:3395`) — the order
    arrives some net frames after issue;
  - then `AutoRearm(self, true, host)`.
  - **Multi-pool trap:** `AmmoPool` is multi-instance, and `ResolveOrder` fires once **per pool**. The existing
    `"Resupply"` arm (`:1015-1029`) already iterates all pools from inside each pool's resolver, i.e. N×N. The
    new arm must run **once per actor**: act only when `this` is the first `AmmoPool` from
    `self.TraitsImplementing<AmmoPool>()`, or resolve it on a single-instance trait instead. A second
    `AutoRearm` would cancel the first's activity and queue twice. Leave the pre-existing `"Resupply"` arm alone;
    note it in `WORKSPACE/bugs/discovered.md`.
- **Sender:** replace `:3406` with `bot.QueueOrder(new Order("RearmAtHost", unit, Target.FromActor(host), false))`
  (the `Order(string, Actor, in Target, bool, …)` shape is already used in the same file, `:2442`). **`host` is
  passed as the target so the affordability pick made at `:3381` survives** to every client. `sought++` stays.
- The stale comment at `AmmoPool.cs:1025-1026` ("Desyncs, orders needs to be synced, some kind of handshake
  involved") is folklore about exactly this class — leave it, but don't let it scare the implementer off; the
  order route is the handshake.

### Step 3 — positive pins (copy `BotOrderedMutationTest.cs:191-298`)
- `TheOutOfAmmoRearmTravelsAsAnOrder`: `SweepOutOfAmmoUnits` constructs an `Order`, calls `QueueOrder`, has the
  literal; and no longer calls `AmmoPool.AutoRearm`.
- `AmmoPoolStillResolvesTheRearmOrder`: `AmmoPool`'s `IResolveOrder.ResolveOrder` (via `GetInterfaceMap`) calls
  `AutoRearm` and carries the literal.

### Acceptance bar
1. Step 1 commit observed RED with the message above (paste the first two lines into the commit message).
2. Fix commit: all three new/extended tests green; the one-hop pass reports zero offences (or only
   manager-ruled allowlist entries).
3. Gates green: `all`, `check`, `dotnet test`, `make test`.
4. **Optional dynamic check, needs explicit go-ahead:** one `./tools/autotest/run-test.sh --hidden
   test-savegame-resume-riverzeta`. Green is **not** evidence of absence (it only exercises R1 if a bot vehicle
   runs dry and rearms before the save tick); a red there after the fix is a real lead.

### Risks
- **`@stable` changes**, deliberately (CLAUDE.md: let improvements through, but visibly). The rearm now lands
  after the order delay instead of on the same tick, and on every client. Say **"@stable behaviour changes:
  out-of-ammo rearm dispatch now travels as an order (order latency); re-take the benchmark baseline"** in the
  commit message, and flag the baseline re-take to the manager.
- Order latency vs re-eval cadence: if the sweep re-evaluates before the order resolves, it can issue twice;
  the resolver's `IsSeekingRearm` guard makes the second a no-op — keep it.
- The one-hop pass may surface other offences (above) and turn this S into an M.

---

## R2 — `RestockSupply` drains a Logistics Centre the enemy captured mid-drive — **XS**

**Goal:** a truck arriving at a just-captured Logistics Centre takes nothing, like its mirror `DeliverSupply` already does.

**Premise — confirmed.** `engine/OpenRA.Mods.Common/Activities/RestockSupply.cs:91` re-validates only
`host.IsDead || !host.IsInWorld`; the comment above it (`:88-90`) claims it covers "captured mid-drive" — false.
`DeliverSupply.cs:104` carries `|| !self.Owner.IsAlliedWith(host.Owner)` and explains why capture is neither
dead nor removed (`:94-97`: `LOGISTICSCENTER` is capturable and has `OwnerLostAction: ChangeOwner`).

**Change:** append `|| !self.Owner.IsAlliedWith(host.Owner)` at `:91`; fix the comment to say the ownership arm
is why it covers capture (cite `DeliverSupply.cs:94-97`).

**Pin (NUnit, IL-scan, XS):** in a new `SupplyActivityOwnershipGuardTest.cs`, assert that both
`RestockSupply.Tick` and `DeliverSupply.Tick` call `Player.IsAlliedWith` (`IlScan.Scan(...).Callees`, pattern
`VaporizeScopeTest.cs:62-77`). Pinning both stops the mirror from drifting back. **RED:** run the pin before the
one-line fix → fails "RestockSupply.Tick does not check the Logistics Centre is still allied — a truck arriving
at a captured Centre refills from the enemy's stock".

**Risks:** both the original guard and this one are reasoned, not observed (`DeliverSupply.cs:98-102` says the
same of itself). A behavioural scenario (clone `test-truck-restock-survives-cancel`, flip the Centre's owner from
Lua mid-drive, assert its stock unchanged) is the real proof — **deferred, not in this brief**; list it in the
commit as untested. Also not in scope: the order resolvers at `DropsSupplyCache.cs:276-325` that check no
relationship (scout #3 "related, lower"). `@stable` effect: bot trucks restocking at a captured Centre now get nothing — say so.

---

## R3 — `SupplyFollowerBotModule.GroundDangerAt` null dereference — **XS**

**Goal:** a bot whose supply module has no danger field gets 0 danger instead of crashing the host.

**Premise — confirmed, latent.** `GroundDangerAt` (`engine/OpenRA.Mods.Common/Traits/BotModules/SupplyFollowerBotModule.cs:2624-2635`)
dereferences `dangerField` unconditionally. `dangerField` is null whenever `!participates` or all of
`DangerFieldRouting`/`DangerEvac`/`DropAndLeave` are off (`:761`). Unguarded callers: `:1041` (every cluster,
every scan), `:1233` (errand-edge log, every truck's first scan), `:1219`, `:1287`, `:1664`, `:1767`, `:1954`,
`:2089`. Not live today: the module is a single shared `enable-ai-any` instance (`ai.yaml:1644`), the only two bot
types are `experimental`/`stable` (`ai.yaml:83-92`), and `InfluenceStack.Participates` returns true for both
(`engine/OpenRA.Mods.Common/Traits/World/InfluenceStack.cs:52-53`); the shipped flags are on (`ai.yaml:1712`, `:1833`).
Any scenario `rules.yaml` or future profile that turns the three flags off crashes the host on the first truck scan.

**Change:** match the convention the other samplers already use (`dangerField != null ? … : 0`, e.g. `:1665`,
`:2005`; `:2560` uses the same null test but returns `int.MaxValue`, its own safe direction — see the note at `:2553`): return 0 when `dangerField == null`. To make it testable without a World,
extract `internal static int GroundDangerAt(DangerFieldLayer field, ControlField control, Player player, CPos cell)`
with the null short-circuit first; the instance method forwards.

**Pin:** NUnit — `GroundDangerAt(null, null, null, new CPos(5, 5)) == 0`. **RED:** run it with the null guard
removed → `NullReferenceException` inside `GroundDangerAt`. Byte-identical for both shipped profiles (field is
non-null for both), so no baseline re-take; say "byte-identical for @stable" in the commit.

**Risks:** returning 0 means "safe" — the correct direction only because a null field also disables every
consumer gate (`:969`, `:774`). Check that no caller reads 0 as "go" where the flags are on; at this ref none can,
because a non-null field is a precondition of those flags.

---

## R4 — `MapLayers.ResetExploration` reads counters nothing writes — **S**

**Goal:** after a reset of exploration, cells that your units currently see stay explored.

**Premise — confirmed.** `engine/OpenRA.Game/Traits/Player/MapLayers.cs:486`:
`explored[index] = (visibleCount[index] + passiveVisibleCount[index]) > 0;`. Those two layers are allocated at
`:184-185` and **never written anywhere** (grep: the only index reads/writes of either are `:486`). The mod's
`AddSource`/`RemoveSource` maintain `visibilityCount[index][strength]` only (`:376`, `:421`). Result: every cell
resets to unexplored. Callers: `DeveloperMode.cs:175` and `:300` (`DevResetExploration`), `HideMapCrateAction`
(no WW3MOD rules use it), Cnc `InfiltrateForExploration` (not in this mod). **Reach: developer mode only** in
shipped WW3MOD — lowest-impact item here; order-driven, no desync.

**Change:** extract `static bool HasVisionAboveBaseline(short[] counts)` mirroring `Tick`'s loop (`:244-251`:
any layer `i > 0` with count > 0; a null array means none). Use it in both `Tick` and `ResetExploration`. Optionally
delete the two dead layers (not `[Sync]`; check `SyncAnnotationTest` stays green).

**Pin:** NUnit on the static (null → false; only layer 0 set → false; layer 2 set → true), plus an IL pin that
`ResetExploration` calls it (the fixture cannot construct a `MapLayers` — no fixture constructs a World).
**RED:** the IL pin against unfixed main fails "ResetExploration does not consult the live vision counts". Run
`worldactor-gate` (part of `check`) and `smoke` — this is `OpenRA.Game`.

**Risks:** if `Tick`'s treatment of layer 0 is not "baseline", mirroring it copies the wrong rule twice — read the
`VisionLayers` semantics before extracting. Not behaviourally verified without a launch.

---

## R5 — Scenario-backed items (#2, #4, #5, #6): one repro run each, then the fix

These need a launch. Each is listed with **the single `run-test.sh` the scout asked for — the RED repro on
unfixed main** — and the pass bar that makes the later green run meaningful. Per the RED-before-green rule, each
also needs **one green run after its fix**: budget two slots per item when asking the manager. Create **new**
sibling scenarios; do not edit the existing green ones the scout names, so their verdicts stay comparable.
Before asking for a slot: `./utility.sh --check-yaml ../tools/autotest/scenarios/<name>` from the repo root
(require `Testing map:` in the output), and `./make.ps1 lua-gate`.

| # | Bug (status at `754999d5`) | New scenario (clone of) | The one run | Pass bar (after fix) / expected RED (before) |
|---|---|---|---|---|
| **R5a** (scout #2, **HYPOTHESIS**) | Allied soldiers in one neutral building change owner. `GarrisonManager.cs:293-297` claims via `ChangeOwnerInPlace`; `:414-419` heir hand-over; `Cargo.OnOwnerChanged` (`Cargo.cs:1276-1283`) converts **every** passenger. | `test-garrison-allied-cogarrison` (clone `test-garrison-hostile-cogarrison`): two **allied** players each send one soldier into the same neutral building on the same tick; then unload all. | `./tools/autotest/run-test.sh --hidden test-garrison-allied-cogarrison` | **Pass:** after unload each soldier's `Owner` is the player who sent it, and the building's owner is one of the two. **Expected RED:** the second player's soldier reports the first player as owner. If it passes on unfixed main, the hypothesis is refuted — record that in DISCOVERIES and stop; do not fix. |
| **R5b** (scout #4, confirmed by reading) | Per-type stance defaults re-apply on every `World.Add`. `UnitDefaultsManager.cs:51` subscribes to `ActorAdded`; `ApplyDefaultsToLocalActor` (`:61-88`) has no "already applied" memory, so an unload/port cycle re-issues the orders. Not a desync (orders replicate). | `test-unit-defaults-survive-unload` (clone `test-unit-defaults-apply`): set the per-type default via `Test.SetUnitTypeFireStance`, let it apply, set a different stance by order, load into an APC, unload. | `./tools/autotest/run-test.sh --hidden test-unit-defaults-survive-unload` | **Pass:** the hand-set stance survives the unload, **and** the original subject/control halves of `test-unit-defaults-apply` still hold (copy its CONTROL actor). **Expected RED:** stance snaps back to the type default after unload. Fix shape: remember applied `ActorID`s (local, unsynced is fine — the output is orders). Save-restore burst is the same fix; not separately tested. |
| **R5c** (scout #5, confirmed by reading) | Final annihilation leaves husks/pilots/passengers. `DoomsdayStrike.Annihilate` (`DoomsdayStrike.cs:1526-1537`) snapshots in-world Health actors and `Kill`s them once; leftovers spawn at frame end; cargo/garrison are out of world. `AnnihilationDamageTypes` (`:280`) is set by no rules file. | `test-final-exchange-leaves-nothing` (clone `test-final-exchange-autofire`, annihilation ≈ tick 745 per its header `:36`): add a husk-leaving vehicle and a loaded transport. | `./tools/autotest/run-test.sh --hidden test-final-exchange-leaves-nothing` | **Pass:** at annihilation tick + 2, zero in-world actors with Health owned by either side. **Expected RED:** ≥1 husk, ejected pilot or released passenger. Keep the parent's verdict assertions. Fix: a frame-end second sweep (or kill cargo first) — `@stable` unaffected. |
| **R5d** (scout #6, **HYPOTHESIS**) | Crew re-entering a vehicle is removed, never disposed. `EnterAsCrew.cs:45-76`: frame-end task ends in `w.Remove(self)` with no `Dispose()`; checks `self.IsDead` but not `targetActor.IsDead`. | `test-crew-reenter-disposes` — **no clone exists** (`test-crew-*` cover evacuate/dismount/instakill, not re-entry). Needs a **new test hook** counting actors of the crew type including out-of-world/undisposed ones; build the hook first (SCREENSHOT/AUTOTEST recipe: test hooks, never input scripting). | `./tools/autotest/run-test.sh --hidden test-crew-reenter-disposes` | **Pass:** after dismount → re-enter, the hook counts **0** undisposed crew actors (the vehicle's slot is filled). **Expected RED:** 1. If the count is 0 on unfixed main, refuted — record and stop. Largest of the four (hook + scenario): **M**. |

**Order inside R5:** R5b (confirmed, user-visible, cheapest clone) → R5c (confirmed, certain on every nuclear
ending) → R5a → R5d (hypotheses; their first run may simply refute them).

---

## Not briefed here
Scout #7 (win condition never runs end-to-end under TestMode), #8 (passive player stranded — known, DISCOVERIES
2026-08-22), #9 (DOT credits the victim), #11's other rows, #12, #13. Not in the manager's requested order; the
scout's doc is the source when they come up.
