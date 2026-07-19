# Capturing structures

> **What it is.** In WW3MOD you don't blow up every building — some you take over. A **Technician** (`tecn`) walks into a neutral or enemy tech building, the building flashes for a moment, and it becomes yours. Captured income buildings pay you cash every second; captured radar gives you map vision; captured logistics centres feed your ammo economy. Capture is a quiet, high-value action that defines economic and tactical position more than any single shot fired.

## Who captures, and from whom

The capture mechanic in WW3MOD has two **roles**, tied to the design idea that a captured tech building always has a Technician "inside" running it:

- **Installing a Technician** — the only way to put a building into play. Only a real Technician can do this. The game's neutral tech buildings have no operator until you walk a Technician in.
- **Taking over by force** — once a building has a Technician inside (i.e. it has an owner), enemy infantry can wrestle it away. The fiction is that they overrun the building and keep the existing Technician operational under their own command. No fresh Technician needs to be brought; you take what's already there.

This produces two unit roles:

| Unit | What they can capture | Delay | When you use them |
|---|---|---|---|
| **Technician** (`tecn`, `tecn.america`, `tecn.russia`) | Neutral buildings **and** enemy-owned buildings | 20 ticks (~0.8 s) | Early game expansion (claim neutrals); throughout the match as the precision tool |
| **Conscript / Rifleman / Team Leader / AT / Sniper** (`e1`, `ar`, `tl`, `at`, `sn`) | **Enemy-owned only.** Cannot touch neutrals. | 1000 ticks (~40 s) | Late-game; you've used your Technicians, but you have plenty of soldiers in the area to wrestle a building back |
| **Engineer** (`e6`) | — | — | Does **not** capture. Engineer is the repair/mine specialist, not a capturer |

The Technician carries a gold wrench cursor (`goldwrench`) on a capture order; soldiers use the default capture cursor and a slower targeting line. The Technician is consumed by the capture (one unit, one capture); soldiers are not consumed (they just stand in the building for 40 sim-seconds and emerge alive on success).

**Why soldiers can't take neutrals.** Neutral buildings are unowned and unoperated. A soldier squad has no Technician of their own, so even after killing all defenders they'd just be standing in an empty building with no way to power it back up. A Technician must do that first walk-in. This makes late-game smoother in two ways: it removes a hack route (running Conscripts into a contested neutral) and it preserves the Technician as a meaningful asset throughout the match — you can't shortcut the choice to build one.

**Why the 40-second soldier delay matters.** Late-game when both sides have soldiers swirling around contested capturables, the 40-sec window is the time you have to either reinforce or break the takeover. In practice you'll lose half the timer to enemies engaging the soldier squad — so soldier capture is a commitment, not a quick swap.

## What can be captured

Five capturable tech buildings exist as neutral map placements. They inherit `^TechBuilding` → `^BasicBuilding` → `^NeutralOrOccupiedCapturable`, which is what makes them targetable by a Technician.

| Building (`id`) | Income ($/tick) | Other effect | HP |
|---|---|---|---|
| **Oil Derrick** (`oilb`) | **50** | None | 50,000 |
| **Expansion Post** (`fcom`) | **100** | Buildable area + base provider | 25,000 |
| **Nuclear Reactor** (`bio`) | **150** | None | 55,000 |
| **Communications Centre** (`miss`) | — | Radar (50-cell circle on minimap) | 35,000 |
| **Hospital** (`hosp`) | — | None ([planned] healing aura?) | 55,000 |

Income comes from the `CashTrickler` trait — a small payment every fixed interval, so income is felt as a steady drip rather than a lump sum. The three income buildings (`oilb`, `fcom`, `bio`) are the strategic prizes; `miss` and `hosp` are tactical (vision, planned healing).

**Logistics Centres** (`logisticscenter`) are also capturable. They're player-built rather than neutral, and capturing one denies the enemy a resupply node for ammo and supply trucks. The legacy AI has a dedicated capture module for these (`CaptureManagerBotModule@captureenemystructures`).

The **Supply Route** is *not* in this list — and it's the most important capture target in the game. See [`../reference/supply-route.md`](../reference/supply-route.md) for the full rules; the short version is: SRs are indestructible, can be captured by Technician, and capturing one or contesting the 10-cell circle around it slows the enemy's entire production.

## The capture experience

What a player sees when they send a Technician to capture an Oil Derrick:

1. Right-click the building with a Technician selected → wrench cursor (`goldwrench`) + magenta target line. *(Anywhere along the path, you can clearly read the bot's intent because of the cursor + line colour.)*
2. The Technician walks. Foot-speed across the map: capturing is a commitment of about 5–15 seconds depending on distance.
3. The Technician enters the building footprint. The building begins flashing (`CapturableProgressBlink`) and a progress bar fills (`CapturableProgressBar`).
4. **20 ticks later** — the building changes ownership in place. Building colour shifts to your colour. The cash starts ticking immediately.
5. The Technician is consumed (`ConsumedByCapture: true`). You don't get him back. He's worth $200; the OILB he just took will repay that in 4 seconds of income.

If the Technician is killed en route, no capture happens — he just dies, and you've spent $200 for nothing. If the building is destroyed mid-capture, the activity cancels.

If an enemy stands inside the building's `BeingCapturedCondition` zone, the building gains the `being-captured` condition while the timer runs. `FCOM` uses this to pause its `BaseProvider` so an enemy can't start placing buildings on top of the in-progress capture.

## Strategic implications

### Capture > Kill on tech buildings

You almost never want to destroy an OILB or BIO. Killing it removes income from the game; capturing it transfers income to you. The exception is sabotage when you can't hold the position — see **Sabotage** below.

### Income compounds; the curve diverges fast

At a 25-tick base interval ($50/sec for OILB), a single Oil Derrick pays for one Conscript every 4 seconds. Three OILBs pay roughly the cost of one MBT (cost ≈ 1500) every 10 seconds. Capturing the first one early is significantly more valuable than the third one late, because that early money buys early army that fights for the rest of the map. The deepest mistake a player can make is to "secure their base first and capture later."

### Map control radiates from FCOM

The **Expansion Post** (FCOM) is not just $100/tick — it's a `GivesBuildableArea` *and* a `BaseProvider` with 8-cell range. Capturing one lets you build defenses and Helipads on its footprint, far from your home Supply Route. It is the single most strategically valuable capturable in the mod and is usually contested first.

### Tech in fog

Most lobbies don't show capturable positions on the minimap before scouting. Sending a Technician to a building you haven't scouted recently is a gamble — the building may already be captured, or surrounded by enemy infantry. A Humvee/BTR sweep through the capturable cluster before you commit the Technician is cheap insurance.

### Escort the Technician

A Technician walking across the open is a $200 unit with no weapon and no armour. The cost-effective practice is to send him in the wake of an infantry squad or vehicle group already pushing toward that area — even one Rifleman alongside dramatically improves the survival rate against scouts. The Experimental AI's capture coordinator does this automatically; for human players, it's a habit worth building.

### Sabotage

The `Captures` trait has a `SabotageThreshold`: if a Captures-eligible unit enters an enemy building whose HP is above the threshold, the unit deals damage to the building instead of capturing it. This is the "I can't hold it, but I can deny it to you" mode. Implementation lives in `engine/OpenRA.Mods.Common/Activities/CaptureActor.cs:104-118`. In WW3MOD this mostly applies to enemy-owned tech buildings the Technician can't realistically capture in time.

## Defending what you've captured

Captured tech buildings are not garrisons — they don't shoot back, they don't hide infantry inside (the four `oilb/fcom/bio/miss/hosp` are not `Garrisonable`). Their HP is high but not invincible:

| Building | HP | Armor |
|---|---|---|
| OILB | 50,000 | (default) |
| FCOM | 25,000 | Concrete |
| BIO | 55,000 | (default) |
| MISS | 35,000 | Concrete |
| HOSP | 55,000 | (default) |

A determined enemy can destroy or recapture them. Two practical defenses:

1. **Station a small force nearby** — even one IFV near a captured FCOM forces the enemy to commit multiple units to take it back.
2. **Repair on damage** — `EngineerRepairable` is on every tech building. An idle Engineer (which can't capture but *can* repair) near a captured FCOM is a much better use of the unit than nothing.

Recapture by the enemy is a real risk: their Technician can take the building back in the same 20 ticks. There is no "captured by you = locked to you" — every tech building remains capturable forever, every owner change costs 0.8 sim-sec of standing-still time.

## How the AI handles capture

| Bot type | Module | Behaviour |
|---|---|---|
| Normal / Rush / Turtle (legacy) | `CaptureManagerBotModule@tecn` | Picks targets by `GetSellValue()` (uniform across OILB/FCOM/BIO). Sends Technician alone. No defense. |
| Experimental / Stable | `CaptureCoordinatorBotModule@experimental.tecn` (Stable: `@stable.tecn`) | Income-weighted scoring (BIO > FCOM > OILB), distance decay, safety bonus when no enemies nearby. Pulls 2 escort infantry along. Summons defenders when own captured under threat. |

See [`../../WORKSPACE/ai/archive/v2_experiment_002_capture_coordinator.md`](../../WORKSPACE/ai/archive/v2_experiment_002_capture_coordinator.md) for the v2 design and measurement plan.

No engineer-targeted capture module exists — engineers don't capture by design. Neither legacy nor the Experimental/Stable AI has soldier-targeted capture wired in either; soldier capture is a player-driven late-game option, not an AI behaviour for now.

## Code pointers

For the curious:

- **Capturable trait** — `engine/OpenRA.Mods.Common/Traits/Capturable.cs`
- **Captures trait** (capturers) — `engine/OpenRA.Mods.Common/Traits/Captures.cs`
- **CaptureManager** (per-actor) — `engine/OpenRA.Mods.Common/Traits/CaptureManager.cs:120` (`CanTarget` decides whether a given capturer can take a given building, based on `CaptureTypes` overlap + `ValidRelationships` of capturer vs owner)
- **CaptureActor activity** — `engine/OpenRA.Mods.Common/Activities/CaptureActor.cs` (the walk-in → flash → ownership change pipeline)
- **Templates** — `^NeutralOrOccupiedCapturable` in `mods/ww3mod/rules/ingame/structures.yaml:149` (capturable side); `^CapturesNeutralBuildings` / `^CapturesOccupiedBuildings` in `mods/ww3mod/rules/ingame/infantry.yaml:881-892` (capturer side)
- **Income** — `engine/OpenRA.Mods.Common/Traits/CashTrickler.cs`; building amounts in `mods/ww3mod/rules/ingame/structures-neutral.yaml`

## Open questions / flagged uncertainties

1. **Hospital effect.** The current trait list on `HOSP` doesn't include any healing — no `RallyPointHealthRegen`, no `HealthRegenAura`. The tooltip just says "Hospital". Either an effect is planned or the building is currently purely decorative. *(Marking as [planned] until told otherwise.)*
2. **Sabotage damage values in WW3MOD.** The `^CapturesNeutralBuildings` template doesn't appear to override sabotage damage. Worth a check on whether Technicians actually trigger the sabotage path on high-HP enemy buildings, or whether the threshold is effectively unreachable.
3. **40-second soldier capture in practice.** Now that soldiers can only take enemy-owned buildings, the 1000-tick delay frames a real decision: stand around for 40 sim-sec while contested. Open question whether the delay value lands at the right point — too short and Technicians feel obsolete, too long and soldier-capture never happens. Watch playtest data.

## Resolved decisions (260512)

For history: two design questions were resolved when this doc was first written.

- **Engineer (e6) doesn't capture by design.** Tooltip corrected; `^E6.Buildable.Description` no longer mentions "captures". Engineer remains the repair/mine specialist.
- **Soldiers can only capture enemy-owned buildings.** `^CapturesOccupiedBuildings.Captures@OCCUPIED.ValidRelationships` set to `Enemy` only (previously defaulted to `Neutral | Enemy`). Neutral capture is exclusively Technician's role. The design fiction is the captured building keeps its existing Technician "inside" after the soldier takeover.

If you have answers to any of the remaining open questions, ping me and I'll fold them in.
