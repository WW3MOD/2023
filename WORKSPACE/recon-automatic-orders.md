# Recon — automatic orders on player-owned units

**Date:** 2026-08-30 · **Researched against:** `main` @ `ee407865` (clean tree, no local engine/YAML edits)
**Scope:** read-only. No engine or YAML behaviour changed, no build, no autotest.

Two reports motivated this, and they pull in opposite directions:

> "I just saw a mortar soldier that was out of ammo but still autotargeted an enemy instead of rearming at a nearby supply cache."

> "There are a lot of automatic orders going on… for now I think we should disable all such behaviours… they autotarget and hunt and such, but no other (regular) movements happens automatically."

**Tick-rate convention used throughout: 16.67 tps** (`BaseTicks: 1500` = 90 s, per CLAUDE.md). Seconds are derived, not asserted from the source comments — several of those are wrong by 1.5×.

---

## The headline, before anything else

**`StancePositioningExecutor` — a 4-cell "cover shuffle" re-evaluated every 30 ticks (~1.8 s) — is ON for every human-owned combat unit, and both places that document it say it is off.**

`mods/ww3mod/rules/defaults.yaml:77-78` grants the enabling token to every human-owned combatant:

```yaml
GrantConditionOnHumanOwner@tacpos:
    Condition: enable-tactical-positioning
```

The comment **17 lines above it, in the same file** (`defaults.yaml:57-59`) says:

> "Default-OFF: RequiresCondition gates it to experimental bots … and, **in Phase 3**, to humans (enable-tactical-positioning). **Humans / @stable / @normal never satisfy either ⇒ byte-identical**"

And the trait's own file header (`StancePositioningExecutor.cs:52-54`) says:

> "default-off everywhere except experimental bots (the former is granted by nothing in Phase 2; **humans get it in Phase 3**). **@stable/@normal/humans are byte-identical.**"

Both are now false. The grant block's own comment calls it "Phase-3 human enablement (RATIFIED default-ON)" — so this was a deliberate decision, but the two descriptions of the feature were never updated with it. **If you read either doc while deciding what to disable, you would conclude this behaviour cannot be what you are seeing.** It is my single strongest candidate for the "lot of automatic orders", and the ranking in Part 2 says why.

This is the `DOCS/reference/conventions.md` §"A change believed made, documented as made, and inert" pattern running in reverse — believed inert, documented as inert, actually live.

---

# Part 1 — the mortar

## Answer: (b), but not the (b) you would expect

Not "both exist and the wrong one wins". **The precedence is sound and AutoTarget always loses.** The bug is that the resupply dispatchers *declined*, and autotargeting is merely what fills the silence afterwards.

### The precedence, exactly

`engine/OpenRA.Game/Actor.cs:316-333` is the whole decision, and it is an `if`/`else if`:

```csharp
var wasIdle = IsIdle;
CurrentActivity = ActivityUtils.RunActivity(this, CurrentActivity);

if (!wasIdle && IsIdle)                       // :321  the idle TRANSITION tick
{
    foreach (var n in becomingIdles)          // :323  AmmoPool.AutoRearmIfDry
        n.OnBecomingIdle(this);
    CurrentActivity = ActivityUtils.RunActivity(this, CurrentActivity);  // :329
}
else if (wasIdle)                             // :331  every SUBSEQUENT idle tick
    foreach (var tickIdle in tickIdles)       // :332  AutoTarget.ScanAndAttack
        tickIdle.TickIdle(this);
```

The two are **mutually exclusive per tick**. `AmmoPool` gets first refusal on the transition tick; `AutoTarget` only ever sees a unit that was *already* idle — i.e. one `AmmoPool` looked at and left alone.

And AutoTarget cannot claw a unit back off a resupply walk, because **every one of its entry points is idle-gated**:

| Entry point | Gate | File:line |
|---|---|---|
| Idle scan-and-attack | reached only from `INotifyIdle.TickIdle` | `AutoTarget.cs:696` |
| Retaliation on damage | `if (… \|\| !self.IsIdle \|\| …) return;` | `AutoTarget.cs:645` |

So once a `SeekSupplyProvider` is running, the unit is not idle, and nothing in AutoTarget can preempt it — not even being shot at.

**Therefore: a dry unit that is visibly autotargeting is proof that no resupply errand was ever dispatched.** That converts "why did he autotarget?" into the much smaller question "why did dispatch decline?", which has an enumerable answer set.

### Why dispatch would decline, ranked

The mortar (`^MT`, `infantry.yaml:1571`) carries **one** ammo pool, `primary-ammo`, `Essential: true` (`:1597-1606`), `SupplyValue: 40`, and `RearmActors: truk, supplycache, logisticscenter` (`:1625`). It inherits `^CamoSoldier` → `^Soldier`, so it has **both** dispatchers:

- `AmmoPool.AutoRearmIfDry` — `AmmoPool.cs:604`, triggered from `INotifyAttack.Attacking` (`:841`, the shot that empties the pool) and `INotifyBecomingIdle` (`:848`). **No `ITick`.**
- `AutoSeekSupplies.ITick` — `AutoSeekSupplies.cs:228`, every 25 ticks (~1.5 s), *not* idle-gated. On `^Soldier` at `infantry.yaml:251-253` with `Enabled: true, ReturnWhenEmpty: true`.

Ranked candidates, all of which end in "unit stands still → next tick AutoTarget engages":

| # | Cause | Deciding line | Why I rank it here |
|---|---|---|---|
| **1** | **He walked to the cache, it could not pay him, and he gave up.** `AutoSeekSupplies` picks a host via `ChooseResupplier` — filtered on `CurrentSupply > 0` **only**. `AmmoPool.TryServeBatch` then requires `CurrentSupply >= SupplyValue` = **40**. A cache holding 1–39 is a legal destination that cannot serve a mortar. After 300 stalled ticks (~18 s) the guard cancels and blocks retry for 500 ticks (~30 s). | pick `AutoSeekSupplies.cs:293`; serve gate `AmmoPool.cs:446`; stall guard `AutoSeekSupplies.cs:363-366` | Fits "**nearby** cache" exactly — he was next to it. The two paths disagree about affordability *by design* (`:286-292` warns against unifying them), and 40 is the joint-highest infantry `SupplyValue` in the mod, so the mortar is the unit most likely to hit this gap. |
| **2** | **Cache was > 30 chessboard cells away.** Beyond `ReturnWhenEmptyLeashCells: 30` he flags and holds. | `AutoSeekSupplies.cs:294`, `:299` | "Nearby" on a zoomed-out 128×128 map is easily >30 cells. Chessboard metric, so diagonals are cheaper than they look. |
| **3** | **Cache was drained to 0**, or **not owned by him** (`a.Owner == self.Owner` is strict — an ally's cache is never a destination). | `AmmoPool.cs:1105-1109` | Plausible; a cache despawns at 0 (`RemoveBelowSupply: 1`), so "drained" is a narrow window. |
| **4** | **Stance was not `Auto`.** `Hold`/`Evacuate`, engagement `HoldPosition`, or fire `Ambush` all veto seeking outright. | `SupplyHuntMath.StancesPermitHunt:66-78` | Shipped default is `Auto` (`defaults.yaml:375`), but `UnitDefaultsManager` persists per-type overrides to a file on disk, so a past Ctrl+Click could be sticky. **Worth checking first — it is free to check.** |

He would **not** have evacuated: `Evacuate` needs `!anyHostWithinLeash && !anyHostCanReachUs` (`SupplyHuntMath.cs:296`), and a nearby cache satisfies `anyHostWithinLeash` regardless of its stock — that is deliberate ("DRAINED IS NOT ABSENT", `:204-217`).

I cannot say which of these fired without a repro. **Candidate 4 is free to rule out** (look at the mortar's resupply stance in-game); candidate 1 is the one I would bet on.

## Two real bugs found on the way

### Bug A — a held unit never reconsiders, and for vehicles that is permanent

`AmmoPool` has no `ITick`. `OnBecomingIdle` fires **only on the idle transition** (`Actor.cs:321`). A unit that decides `HoldAndFlag` and then has nothing to shoot at stays idle forever — `wasIdle` is true every tick, so line 321 is never taken again and `AutoRearmIfDry` **never runs again**. A truck can park directly on top of it and it will not notice.

The earlier investigation could not establish whether a held unit re-evaluates. It does not — except by two accidents:

1. `AutoSeekSupplies.ITick` rescues it. **That trait is on `^Soldier` alone.** Every ammo-carrying **vehicle** has no `ITick` path whatsoever.
2. Ironically, **autotargeting itself is the other rescue.** AutoTarget issues an attack → `Attack.cs:117` ends it immediately on `CannotFight` → the unit goes non-idle then idle → `OnBecomingIdle` fires → dispatch re-evaluates. For a vehicle, *the autotargeting the user wants to stop is the only thing that makes it re-check whether it can rearm.*

That is a direct argument against a blanket disable, and it is the sharpest reason this needs to be surgical.

### Bug B — `Essential` is defeated at the idle gate for the units it was written for

`Attack.cs:117` ends a dry unit's attack via `AmmoPool.CannotFight` = `AllPoolsEmpty` — **every** pool. Its own PITFALL comment (`:111-116`) explains that ending there is what hands the unit to the resupply path.

But `Essential` exists precisely so a unit that is dry *on the pool that matters* while still holding rounds elsewhere counts as needing resupply. For such a unit `CannotFight` is **false** — so the attack activity never ends, the unit never goes idle, and `OnBecomingIdle` never fires.

Six actors mix Essential and non-Essential pools (verified by parsing every `AmmoPool` trait declaration under `mods/ww3mod/rules/`):

| Actor | Pools | Has `AutoSeekSupplies` ITick backstop? |
|---|---|---|
| `^E3`, `^TL`, `^SF`, `^E6` | Essential + non-Essential | Yes — `^Soldier`. Recovers. |
| **`strykershorad`** (`vehicles-america.yaml`) | `@1` no, `@2` **yes**, `@3` no | **No.** |
| **`tunguska`** (`vehicles-russia.yaml`) | `@1` no, `@2` **yes** | **No.** |

For those two vehicles the **only** dispatch trigger is `INotifyAttack.Attacking` (`AmmoPool.cs:841`) on the exact tick the essential pool empties. If that one dispatch declines — poor host, out of leash, stance not `Auto` — it is never re-evaluated for the rest of the match. A tunguska out of SAMs with a full cannon is exactly the motivating example in `AmmoPool.cs:62-64`, and it is the case that does not work.

### Stale line references, incidentally

`infantry.yaml:2014,2017` cite `AmmoPool.cs:662` and `:263`; the real sites are `:841` and `:313`. The claims are still true, the numbers have drifted.

---

# Part 2 — the inventory

Sorted **MOVEMENT first** (what you want gone), then TARGETING (what you are keeping), then bot-only (ignore). "Recently ruled" flags things you specified yourself in the last weeks — **disabling those reverses your own decisions**, which is the trap this section exists to prevent.

## A. MOVEMENT — reaches your units

| # | What a player sees | Trigger | Gated by? | Off switch | Recent ruling? |
|---|---|---|---|---|---|
| **M1** | **Idle combat units shuffle up to 4 cells to "better cover" and keep re-deciding every ~1.8 s.** | `StancePositioningExecutor.cs:414`, from `INotifyIdle.TickIdle` | `enable-tactical-positioning \|\| enable-ai-experimental` — **granted to all humans**, `defaults.yaml:77-78` | **Exists, one line:** delete the `GrantConditionOnHumanOwner@tacpos` block. Per-unit opt-out is HoldPosition stance. | Ratified, but the docs say it's off |
| **M2** | Units drift back to the slot a group-move gave them after being pushed off. | `CohesionSlotMemory.cs:238`, from `TickIdle:174` + `INotifyBlockingMove:169` | **Nothing.** Unconditional on `^Combatant` (`defaults.yaml:53`) | Would have to be **built** (no flag, no condition) | No |
| **M3** | An emptied supply truck drives off the map to refund itself. | `DropsSupplyCache.cs:544` → `RotateToEdge`; `INotifyBecomingIdle:430` + `ITick:440` | `ResupplyBehavior`; TRUK ships `Evacuate` (`vehicles.yaml:535`) | Exists — set stance `Hold` | **YES — truck supply doctrine** |
| **M4** | A truck you ordered somewhere **cancels your order** when it reads empty. | `DropsSupplyCache.cs:514` `self.CancelActivity()` | `DryMoveScanInterval > 0` | Exists (set interval 0) | **YES — same doctrine** |
| **M5** | A dry unit drives off the map and refunds itself because nothing can rearm it. | `AmmoPool.cs:799` `RotateToEdge`, from `:693` and the `Auto` fallback `:777` | `ResupplyBehavior`. **`Auto` now falls through to evacuation** | Exists — stance `Hold` | **YES — 2026-08-27 ruling, "'Auto' should mean they evacuate if no rearm actor exists"** |
| **M6** | A soldier low on ammo walks up to 20 cells to a truck/cache and comes back. | `AutoSeekSupplies.cs:202` `SeekSuppliesAndReturn`, from `TickIdle:167` | `Enabled` (engine default **false**, YAML sets **true**, `infantry.yaml:252`) | Exists — `Enabled: false` | Yes-ish |
| **M7** | A soldier **abandons the order you gave him** when fully dry and walks to resupply. | `AutoSeekSupplies.cs:309`, from `ITick:228` (**not** idle-gated) | `ReturnWhenEmpty` (engine default **false**, YAML **true**, `:253`; `^E6` opts out `:2021`) | Exists — `ReturnWhenEmpty: false` | Yes-ish |
| **M8** | Any dry unit self-dispatches to a rearm host. | `AmmoPool.cs:683`, from `INotifyAttack:841` + `INotifyBecomingIdle:848` | `ResupplyBehavior` stance only — **no Info flag** | Only via stance; a flag would have to be **built** | Partly |
| **M9** | A supply truck on **Hunt** drives across the map to a flagged customer. | `SupplyProvider.cs:921` | Requires engagement `>= Hunt`; **default is Defensive**, so off unless you set it | Exists — stance | **YES — doctrine** |
| **M10** | A truck below 50 supply drives to a Logistics Centre to restock. | `SupplyProvider.cs:1129,1135` | `RestockThreshold: 50` (`vehicles.yaml:583-584`) | Exists — threshold 0 | **YES — doctrine** |
| **M11** | A medic trails the squad, staying ~3 cells from the nearest armed ally. | `AutoFollowAlly.cs:156`, from `TickIdle:96` | `FollowStances: Defensive, Hunt` (`infantry.yaml:2293`) | Exists — narrow `FollowStances`, or HoldPosition | **YES — medic casualty tier** |
| **M12** | A spent attack helicopter flies off the map for a refund. | `EvacuateWhenUnrearmable.cs:82` | `IncludeBotOwners: false` (`:28`) — **player-only by design**; bots excluded | Would have to be **built** (no human off switch) | Yes |
| **M13** | A burning soldier runs to random adjacent cells. | `InfantryStates.cs:179`, from `TickIdle:171` | `PanicCondition: onfire && !heavy-damage-attained` (`infantry.yaml:331`) | Exists — clear the condition expression | No |
| **M14** | Crew bailing out of a dead vehicle walk 2–3 cells clear. | `VehicleCrew.cs:462`, `:643` | Nothing — death-triggered | Would have to be built | No |
| **M15** | A damaged transport dumps its passengers; an airborne one cancels its activity to do it. | `Cargo.cs:829`, `:839`, `:1032` | `EmergencyBailDamageState: Heavy` / `AircraftEmergencyBailDamageState: Critical` — **neither overridden in rules** | Exists (raise the thresholds) | No |
| **M16** | A badly damaged helicopter takes over its own flight and descends. | `HeliEmergencyLanding.cs:223,239,257` | `AutorotationDamageState` / `CrashDamageState` (3 YAML sites) | Exists | No |
| **M17** | A unit standing in an illegal cell walks to a legal one; an idle unit shuffles aside for a friendly. | `Mobile.cs:996`, `:1006`, `:1016` | **Nothing — stock OpenRA** | Don't. This is baseline behaviour. | No |
| **M18** | Aircraft at the same altitude push each other apart. | `Aircraft.cs:619,632` | `Repulsable: true`, not overridden | Exists but **stock OpenRA** | No |
| **M19** | Civilians wander / flee. | `Wanders.cs:115`; `ScaredyCat.cs:164` | `!heavy-damage-attained` | Exists | No |

## B. TARGETING — you are keeping these

| What | Site | Note |
|---|---|---|
| Idle scan and engage | `AutoTarget.cs:696`, `:711` | **`allowMove = allowMovement && engagementStance >= Hunt` (`:709`).** On the default **Defensive** stance a unit turns and fires but **never chases.** This is already the "autotarget, and hunt only if asked" behaviour you want. |
| Retaliation when shot | `AutoTarget.cs:643-693` | Same `allowMove` rule (`:670`); idle-gated at `:645`. |
| Re-picking a better target mid-fight | `AutoTarget.cs:1067`, `PreemptScanInterval: 25` (`defaults.yaml:381`) | Same movement rule. |
| Auto-acquired attacks draw a DodgerBlue line | `AttackBase.cs:738` | Cosmetic provenance marker only. |

`AttendAlly`, `Guard`, `Repairable`, `Transforms`, `DeliversCash`, `Minelayer`, `Cargo` load/unload are all **`IResolveOrder`** — player-issued, not automatic. **There is no auto-return-to-repair in this mod.**

## C. Bot-only — ignore

Everything under `engine/OpenRA.Mods.Common/Traits/BotModules/` (~50 files). These are traits on the **player actor**, instantiated only from `rules/ai/ai.yaml`; they cannot attach to a human. Also bot-only: `AIHelicopterRole`; the ambush halt-before-contact (`enable-ambush-tactics`, granted per-unit only by `LaneAmbushBotModule.cs:466-472`); and the `enable-ai-experimental` half of M1.

**Two inversions worth knowing:** M1's *human* half is separately granted and live (§headline). And `EvacuateWhenUnrearmable` (M12) is the reverse of a bot behaviour — `IncludeBotOwners: false` means it runs on **your** helicopters and not the bot's.

## D. Engine traits on zero shipped actors — nothing to disable

`AutoCrusher`, `AutoCarryall`/`AutoCarryable`, `Carryall`, `Harvester`, `DockClientManager`/`DockHost`, `AttackWander`, `TurnOnIdle`, `DeliversExperience`, `RepairableNear`, `EntersTunnels`, `FormationRealism`, all `TransformsInto*`. Naval actors carrying `AutoTarget` are commented out.

---

# Part 3 — the autonomy stance

## What exists: four bars, not three

All four enums are adjacent in `AutoTarget.cs`: `UnitStance` (`:22`), `EngagementStance` (`:24`), `CohesionMode` (`:26`), `ResupplyBehavior` (`:28`). Each has the identical apparatus — synced field (`:426-429`), `[Sync]` **int projection** (`:370-380`), `Predicted*` mirror (`:395-404`), setter (`:440-486`), human/AI init split (`:517-522`), order resolve (`:578-587`), a widget logic file, a chrome YAML block (`ingame-player.yaml:334/403/472/541`), `UnitDefaultsManager` persistence (`:22-25`, `:100-118`, `:145-159`, `:186-192`), and hotkeys (`engine/mods/common/hotkeys/game.yaml:102-157`).

## Verdict: cheap, but it is copy-paste, not architecture

There is **no generic machinery** — no `StanceBarLogic<TEnum>`, no data-driven bar. `StanceSelectorLogic.cs` and `EngagementStanceSelectorLogic.cs` are both 117 lines differing by ~92 lines of pure identifier renaming. A fifth bar is a fifth copy of a template already copied four times.

| Work | Lines | Files |
|---|---|---|
| New `AutonomySelectorLogic.cs` | ~120 (~95 % copied) | 1 new |
| `AutoTarget.cs` — enum, field, sync projection, getter, Predicted, setter, `Initial*`/`Initial*AI`, init, resolve | ~45 | 9 existing sites |
| `UnitDefaultsManager.cs` — field, setter, load, save, apply-by-order | ~20 | 5 sites. **Additive**: per-key MiniYaml (`:145-159`), old files still load — not a schema break |
| `chrome/ingame-player.yaml` | ~68 (3 buttons × ~22) | 1 |
| `hotkeys/game.yaml` | ~15 | 1 |
| **Total** | **~270 lines, 5 files, no new subsystem** | ~half a day, mostly mechanical |

**Two non-obvious costs.**

1. **Screen space is nearly exhausted.** Bars sit at `X: 458 / 578 / 698 / 818`, `Width: 102`, 120 apart (`ingame-player.yaml:337,406,475,544`). A fifth lands at **938–1040, which overflows a 1024-wide window.** The precedent for ignoring this is `CARGO_PANEL`, which sat off-screen for its entire life (`architecture.md:737`). Either re-lay-out the row or put the bar elsewhere.
2. **No new art needed** — the existing bars already recycle `stance-icons` (4 regions, `chrome.yaml:222`) and `command-icons`; Evacuate reuses the `defend` glyph (`ingame-player.yaml:551`).

## Which behaviours could key off it with no new plumbing

**Free** — the trait already holds an `AutoTarget` reference, so the value is in hand at the decision site: `AutoSeekSupplies.cs:162` (M6/M7), `AutoFollowAlly.cs:83` (M11), `StancePositioningExecutor.cs:197` (**M1**), `AmmoPool.cs:611` (M8), `SupplyProvider.cs:555,598,1335` (M9/M10), `DropsSupplyCache.cs:510,526` (M3/M4).

**Needs one line threaded in** (`self.TraitOrDefault<AutoTarget>()`): `CohesionSlotMemory` (M2), `Wanders.cs:115`, `ScaredyCat.cs:164`, `Mobile.cs:996,1006`, `EvacuateWhenUnrearmable`.

**That is the real finding: every behaviour a player would actually notice — M1, M3–M11 — is in the free column.** The stance is cheap at the consumer sites, not just at the UI.

## Traps

1. **Never `[Sync]` the enum.** `Sync.cs:72` throws `NotImplementedException` **at runtime, not compile time** — it passes the build and dies in a live game. Use `int SyncAutonomy => (int)autonomy;` (`AutoTarget.cs:370-380`). `SyncAnnotationTest.cs` is the load-bearing guard; the mod-side lint is warning-only and cannot fail anything.
2. **`UnitDefaultsManager` must apply via orders, never by writing the field** (`:33-38`, `:78-88`). It reads a per-machine file under `Platform.SupportDir`; the direct-write version desynced with nobody touching anything (`architecture.md:587`).
3. **Optimistic UI goes in `PredictedAutonomy`, never the synced field** (`AutoTarget.cs:395-404`).
4. **`DoNow` (Alt+Click) must issue orders, not queue activities** — `ResupplyBehaviorSelectorLogic.cs:120-124` carries a PITFALL recording that queueing `RotateToEdge` directly desynced, because widget callbacks run on one client only.
5. **Ctrl+Click "set as unit default" is a lie today.** `SetUnitDefault` is a bare alias for `SetSelectionStance` in all four bars (`StanceSelectorLogic.cs:91-94`, `ResupplyBehaviorSelectorLogic.cs:90-93`) while the tooltip promises the unit remembers (`ingame-player.yaml:353`). **Copying the template copies this bug** — worth fixing before there are five instances.

---

# Recommendation

You invited disagreement, so: **I would not do the blanket disable, and I think the thing you actually noticed is one specific behaviour that both docs say is switched off.**

The reason is Bug A. `AmmoPool` has no `ITick` and only fires on the idle *transition*. Kill the movement layer wholesale and dry vehicles stop re-evaluating resupply entirely — they hold, flag, and wait forever for a truck that only comes if you set it to Hunt (M9), which you would also have disabled. **Autotargeting is currently the only thing that makes a dry vehicle re-check whether it can rearm.** A blind disable does not just remove the mortar's route to ammo; it removes the heartbeat that drives the whole resupply system for everything that is not a soldier.

### Disable now — 1 line, reversible, highest confidence

- **M1 tactical positioning.** Delete `GrantConditionOnHumanOwner@tacpos` (`defaults.yaml:77-78`). This is the only behaviour that moves *every combat unit you own*, on a ~1.8 s cadence, with no player-legible cause. It is my primary suspect for "a lot of automatic orders", and it is the cheapest thing on this list to turn off. **Fix the two comments at the same time** (`defaults.yaml:57-59`, `StancePositioningExecutor.cs:52-54`) — they are actively misleading, and leaving them will cost someone an afternoon.
- **M2 return-to-slot** — second suspect and the one with *no gate at all*. Needs a switch built. I would build the switch and default it off for humans, rather than deleting the trait.

### Keep — do not touch

- **M17 idle nudge, M18 aircraft repulsion.** Stock OpenRA; "the same as original OpenRA" includes these.
- **All of section B (targeting).** Note `allowMove` is already Hunt-gated (`AutoTarget.cs:709`), so on the default Defensive stance your units already do not chase. That half of what you asked for is shipped.
- **M13–M16** (panic, bail-out, autorotation). Damage-reactive, legible, and rare.

### Gate, do not disable — these are your own rulings

**M3, M4, M5, M9, M10, M11** are the truck supply doctrine, the 2026-08-27 evacuation ruling and the medic casualty tier. Turning them off reverses decisions you made deliberately in the last three weeks. They also all read `AutoTarget` already, so they are in the free column for the autonomy stance.

**This is the case for building the stance rather than a global switch**: it lets M3/M5/M9/M10/M11 stay on for players who want them while giving you one control to quiet everything. ~270 lines, half a day, and every behaviour you would want to key to it is already reachable. The only real obstacle is the fifth bar overflowing a 1024-wide window.

### Fix regardless of what you decide about automation

- **Bug B** — `strykershorad` and `tunguska` get exactly one dispatch opportunity per match and no backstop. Narrow and nameable.
- **The affordability split** — `AutoSeekSupplies` sends a unit to a host that `AmmoPool` would refuse as too poor to pay (candidate 1 in Part 1). The comment at `AutoSeekSupplies.cs:286-292` explains why the two must not simply be unified, so this needs thought, not a one-line change.

---

## Watch

**What I could not verify by reading.** I never ran the game, so nothing here is confirmed against live behaviour. Specifically: I could not determine which of the four Part-1 candidates actually fired for your mortar — that needs a repro with `TestMode` on, which would print the `[seek]` lines at `AutoSeekSupplies.cs:304` and `:359` and settle it in one match. I also did not verify that M1 is *visible* — the trait is enabled and its move is queued at `:414`, but `MinThreatIntensity: 40` gates it on the sighting field having real data, and I did not establish how often that threshold is met in a normal game. If it is rarely met, M1 is enabled but quiet, and my ranking is wrong at the top.

**The claim I would bet is wrong.** That M1 is the thing you noticed. It is the best-supported inference I have, but it rests on "enabled + short cadence + every unit" rather than on anything I saw. **M2 (return-to-slot) is the better bet if what you saw was units drifting *back* somewhere rather than sidling into trees** — it is completely ungated, fires on both idle and being-pushed, and its 750-tick sticky-slot leash makes it persistent in a way that reads as stubborn. If you can tell me which of those two the movement looked like, that single detail resolves most of the ranking.

**Second-guess on Part 1.** I assert the precedence is clean because every AutoTarget entry point is idle-gated, and I checked two (`:645`, `:696`). If a third entry point exists that I did not find — something calling `ScanAndAttack` or `Attack` from outside `TickIdle` — then the "AutoTarget always loses" conclusion is wrong and this really is a stomp. I grepped for the call sites and found four, all inside those two paths, but that is a grep, not a proof.

**Counting caveat.** The six mixed-Essential actors come from a script I wrote that parses trait declarations by indentation. It agreed with my hand-read of `^MT` and the BMP-2, but it does not resolve YAML inheritance — an actor that inherits a pool from a template and adds another locally could be missed.
