# Widget-callback determinism sweep — `Widgets/**`

Done in `wt/widget-sweep` against `main @ c9f6a6c0`. Follow-up to the `IOrderGenerator` sweep
(`260816-desync-forensics.md`), prompted by `CargoPanelLogic.cs:376` — a client-local write that an
`IOrderGenerator` sweep could not see by construction.

**Invariant tested:** no client-local write to state that a synced path later reads.

## Scope

`mods/ww3mod/mod.yaml:170` loads `OpenRA.Mods.Common.dll` + `OpenRA.Mods.Cnc.dll` and no mod-specific
assembly, so the mod's chrome logic *is* these files. Swept **all** of
`engine/OpenRA.Mods.Common/Widgets/**` — 140 files under `Logic/`, 63 top-level widget classes — plus
`engine/OpenRA.Mods.Cnc/Widgets/` (one file, render-only). Widened past the brief's `Widgets/Logic/**`
because the sibling widget classes (`SupportPowersWidget`, `ProductionPaletteWidget`,
`ShellmapNukeOverlayWidget`, …) take clicks the same way and are not under `Logic/`.

Method: mutating-verb grep; field-assignment grep at one- and two-level receivers; full read of the 13
action-taking files; targeted checks for `SharedRandom`, economy mutation, condition grant/revoke,
`InflictDamage`, ownership change, `QueueActivity`/`CancelActivity`, and `world.Add`.

**Not findings, deliberately excluded:** `world.OrderGenerator = …`, selection changes, control-group
edits, viewport/camera writes, and render-overlay toggles. All are client-local *by design* — listing
them would bury the two that matter.

**Reading synced state from a widget to decide an order is safe** and is not listed either
(`GroupScatterHotkeyLogic.cs:240` reads `CohesionSlotMemory`; `CommandBarLogic.cs:541,639` read
`Cargo.HasSpace`). The decision is replicated by the resulting order, so only one client's read matters.
The bug is *writing*, not reading.

---

## 1. `UnitDefaultsManager` — simulation state seeded from a per-machine file. **LISTED, NOT FIXED.**

**Reach: highest in the sweep — higher than Patrol, because the worst case needs no in-match input.**

Two defects, one mechanism.

**(a) Four widget callbacks write it with no order.**

| Writer | Call |
|---|---|
| `StanceSelectorLogic.cs:102` | `mgr.SetFireStance(actorType, stance)` |
| `EngagementStanceSelectorLogic.cs:102` | `mgr.SetEngagement(actorType, stance)` |
| `CohesionSelectorLogic.cs:103` | `mgr.SetCohesion(actorType, mode)` |
| `ResupplyBehaviorSelectorLogic.cs:105` | `mgr.SetResupply(actorType, behavior)` |

Reached by Ctrl+Alt+click on any of the 12 buttons in the four selector bars
(`ingame-player.yaml:352,421,497,566`). `UnitDefaultsManager` is live on the world actor
(`world.yaml:274`).

**(b) The synced consumer reads it from a local file, which no order can fix.**

`AutoTarget.Created` (`AutoTarget.cs:495-524`) calls `mgr.GetDefaults(self.Info.Name)` and, for every
playable non-bot actor, writes the **authoritative** `stance` / `engagementStance` / `cohesion` /
`resupplyBehavior` fields. Those are exactly the fields simulation reads:

- `CohesionMoveModifier.cs:1117` → `CohesionValue` (group move offsets)
- `AmmoPool.cs:238`, `AutoSeekSupplies.cs:386`, `SupplyProvider.cs:485`, `DropsSupplyCache.cs:494,511` → `ResupplyBehaviorValue`

The dictionary is loaded in `IWorldLoaded.WorldLoaded` from
`Platform.SupportDir/ww3mod/unit-defaults.yaml` and written back on `IGameOver.GameOver`. It is a
**per-machine file**, and `Created` runs on *every* client for *every* actor — so each client applies
its own file to everyone's units.

Consequence: **two players whose `unit-defaults.yaml` differ diverge as soon as either receives a
unit**, with no button pressed during the match. The file only has to differ, and it is written at the
end of any game including skirmish. `filePath` is null until `WorldLoaded`, so map-placed starting
actors are unaffected; in WW3MOD every unit arrives as a Supply Route reinforcement *after* that, so in
practice the exposure is total.

**Why I did not fix it.** (a) alone is a new order plus resolver — the shape I just built, and
tempting. But fixing (a) without (b) leaves the zero-input desync fully live and makes it *look*
handled, which is worse than leaving it visibly broken. (b) is not a bug with a mechanical fix; it is a
design decision about what a cross-game preference means in a lockstep match, with at least three
answers:

1. **Exchange in the lobby** — each client's defaults become part of the replicated game setup. Keeps
   the feature, costs a handshake/lobby-state change.
2. **Ignore the file in network games** — `AutoTarget.Created` reads it only when
   `world.LobbyInfo.IsSinglePlayer` (or equivalent). Cheapest and safest; the feature silently stops
   working in multiplayer, which players will notice.
3. **Demote to a pure UI preference** — the file never touches `AutoTarget`; instead the local client
   issues the normal per-actor `SetStance`/`SetCohesion`/… orders for a newly built unit. Preserves the
   feature in MP and is fully deterministic, but the stance now applies a net-tick late and needs a
   hook on unit arrival.

My recommendation is **3**, falling back to **2** if release scope is tight. Either way it is the
manager's call, not mine.

## 2. `ResupplyBehaviorSelectorLogic.cs:129-138` — Evacuate queued an activity directly. **FIXED.**

Alt+click on `RESUPPLY_EVACUATE` ran, per selected actor:

```csharp
var amount = at.Actor.GetSellValue();
at.Actor.QueueActivity(false, new RotateToEdge(at.Actor, true, amount));
```

No order — the Patrol shape exactly, and it moves units, so divergence is immediate. Reachable with any
selection containing an `AmmoPool` or `SupplyProvider` actor.

An existing order covered it: `DeliversCash.ResolveOrder` (`DeliversCash.cs:82`) handles `"Evacuate"`
for `Type: Rotation` and `GoDonateCash` (`:96-111`) runs the same `RotateToEdge`. `CommandBarLogic.cs:256`
already issues it — a direct sibling to copy. Now `world.IssueOrder(new Order("Evacuate", at.Actor, false))`.

**The local path was also economically wrong**, independently of the desync: it always used raw
`GetSellValue()`, skipping the handicap adjustment (`DeliversCash.cs:99-106`) and the `info.Payload`
override. A handicapped player got a different refund from this button than from the Evacuate button.
Routing through the order fixes that too.

No coverage lost: `DeliversCash@Rotation` is declared on `^Vehicle` (`vehicles.yaml:102`), `^Aircraft`
(`aircraft.yaml:124`), `^Helicopter` (`aircraft.yaml:166`) and `^Infantry` (`infantry.yaml:155`) — every
unit — so every actor the old path reached still evacuates.

## 3. `ShellmapNukeOverlayWidget.cs:133` — right shape, unreachable. **LISTED, rank ~0.**

`world.AddFrameEndTask(w => w.Add(missile))` adds a `NukeLaunch` from a click with no order. Only
reachable from `MainMenuLogic.cs:299` (`NUKE_OVERLAY` on the main menu shellmap) and referenced from no
mod yaml at all. The shellmap is single-client with no network, so it cannot desync. Recorded only so a
future reader does not "discover" it; **do not fix it** — there is nothing to fix.

---

## The negative result, which is the more useful half

**The stance / engagement / cohesion / resupply selectors already implement the correct pattern**, and I
expected them to be the worst offenders. Each writes a client-local `Predicted*` field for instant
button highlight *and* issues the authoritative order:

```csharp
at.Trait.PredictedCohesion = mode;                                              // UI only
world.IssueOrder(new Order("SetCohesion", at.Actor, false) { ExtraData = ... }); // authoritative
```

`AutoTarget.cs:352-362` marks all four `// NOT SYNCED: do not refer to this anywhere other than UI
code`; the authoritative values are the private `stance`/`cohesion`/… exposed read-only as
`CohesionValue`/`ResupplyBehaviorValue`, and grep confirms **no simulation consumer reads a `Predicted*`
field**. `World.SetPauseState` (`World.cs:453-460`) uses the same idiom with `PredictedPaused`.

This is the pattern `CargoPanelLogic`'s eject-rally should have used, and the one any fix for finding 1
option 3 should copy. `GarrisonPanelLogic.cs:204` — the direct sibling of the file that produced the
original specimen — is likewise clean, issuing `EjectGarrisonPassenger` as an order. **The eject-rally
code was an outlier, not a house style.**

## Also checked, clean

`CargoPanelLogic` (post-fix; `markedPassengerIds` is a widget-private `HashSet` no simulation path
reads), `CargoUnloadOrderGenerator`, `CommandBarLogic` (every action via `world.IssueOrder`),
`GroupScatterHotkeyLogic`, `ProductionPaletteWidget`, `ClassicProductionLogic`, `ProductionTabsWidget`,
`SupportPowersWidget` (`Power.Target()` only sets an order generator), `SelectionUtils` (read-only),
`StrategicProgressWidget` (read-only), `DebugMenuLogic` / `DebugLogic` (cheats go through orders;
overlay toggles are render-only; dev-gated regardless), `Logic/Editor/**` (map editor, no network),
`Logic/Lobby/**` and `Logic/Settings/**` (pre-game).

**Refuted prediction, recorded:** I expected at least one widget to draw from `world.SharedRandom` —
consuming the synced RNG desyncs just as writing does, and it is a shape the "no client-local write"
phrasing walks straight past. `grep SharedRandom` across `Widgets/**` returns **nothing**. The hole I
predicted is not there.
