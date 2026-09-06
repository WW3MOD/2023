### 84. Idle low-ammo infantry hold when the supply truck is already closing (AutoSeekSupplies gate)

`[USER-APPROVED IN PRINCIPLE 2026-09-06 — NOT NOW. User verbatim: "Do NOT build now, but make a note of it … I am not sure the soldier/truck fix is a priority now, but it is a good idea and I have thought about it so we might do it."]`

**Perceived:** a starving squad holding a line no longer trots 18 cells out to meet a supply truck that is already driving to it; the truck comes to them and serves from its aura, and the men keep their position.

**Source:** safe-front closing work, 2026-09-06 (worker a4bb724b, merged @ 875c499b). Filed at `main @ e8e57ada`.

---

#### What was measured

Scenario `tools/autotest/scenarios/test-supply-safe-front-keeps-cargo` (re-specced 2026-09-06 with two map-local overrides: `IgnoreDangerForDelivery: false` on `SupplyFollowerBotModule@supply`, `ForwardStagingEnabled: false` on `PoiOffensiveBotModule@experimental`). Three runs the same day — `260906_090304`, `260906_092137`, `260906_093551` — under `C:/Users/fredr/.ww3mod-tests/screenshots/`. After the truck-side fix (follow box scanned nearest-first, `SupplyLogisticsMath.FollowBoxScanOrder`) and with staging off, the only remaining clause-4 breaker is `[seek] leave … provider=truk@26,16 dist=18c leash=20c` ×5 at tick 250 (`AutoSeekSupplies.cs:197` at 875c499b): every starving man leaves the front the moment the inbound truck crosses the 20-cell leash. Peak drift 5 cells, allowed 1; all five end back at their spawn cells.

Full sequence, tick-stamped, and the corrected kinematic model: item 56 dossier, sections dated 2026-09-06 ("second run read", "scenario config").

#### The gate, as specified by the worker who measured it

A new Info field on `AutoSeekSupplies` (engine default **false**, so nothing moves unless YAML sets it). In `TickIdle`, between `FindNearestUsableProvider()` and the dispatch at `:202`: cache the chosen provider's `ActorID` + squared distance per scan; if the same provider returns with a strictly decreased distance, **hold** instead of dispatching `SeekSuppliesAndReturn`. Zero RNG. Self-limiting: the man re-asks every 40 ticks, so a truck that parks or drives away is still fetched from. Idle path only — never the `ReturnWhenEmpty` break-off (a wholly dry man should still break off).

Enable it on `^Soldier` (`mods/ww3mod/rules/infantry.yaml`) — there is no owner-side split, so **human-owned squads and both bot profiles move together**. The user picked exactly this option ("Build the gate and enable it on ^Soldier") over a bots-only wiring.

#### Cost to human play (state it in the release notes when it ships)

Idle low-ammo infantry stop intercepting a truck already driving at them; up to ~40 ticks (≈1.6 s) extra before fetching from a truck that turns out to be merely passing; a squad that used to meet the truck halfway now waits, so time-to-resupply rises whenever the truck is slower than the men. Units under an explicit player order are unaffected.

#### Acceptance

`test-supply-safe-front-keeps-cargo` Test.Pass with clause 4 green: `[seek] leave` absent from `debug.log`, peak drift ≤ 1, truck furthest x ≥ 40, `reason=SafeFront` present, `[exp-staging]` absent, crate=NONE. Plus NUnit for the hold decision. A false pass: `ignore-danger=True` in the init line (override stopped merging) — check that first.

#### Traps

- `@stable` moves too (shared trait). Say so in the commit; the 260905 baseline already pre-dates b6207b9b.
- The `[exp-transport]` module can select a rifleman as a passenger on this map (`passengers-eligible=1` seen; declined only for lack of a drop cell). If a rerun shows a man riding away, look there before blaming the gate.
