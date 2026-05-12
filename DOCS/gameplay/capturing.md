# Capturing structures

> **What it is.** In WW3MOD you don't blow up every building — some you take over. A **Technician** (`tecn`) walks into a neutral or enemy tech building, the building flashes for a moment, and it becomes yours. Captured income buildings pay you cash every second; captured radar gives you map vision; captured logistics centres feed your ammo economy. Capture is a quiet, high-value action that defines economic and tactical position more than any single shot fired.

## Who captures

The **Technician** (`tecn`, with faction variants `tecn.america` and `tecn.russia`) is WW3MOD's dedicated capture unit. He carries a wrench cursor (`goldwrench`), enters a target building, and is consumed by the capture — one Technician per capture. Capture takes **20 ticks** (≈ 0.8 sim-sec at base speed) — fast enough that you can race in and grab a building under contested fire if your timing is right.

**Engineers (`e6`) cannot capture in WW3MOD.** Their in-game description still mentions "captures" but the `Captures` trait is not present on the unit. Treat the description as a stale string. The capability sits on the Technician only. *(See **Open questions** below — possibly worth either restoring the trait or correcting the tooltip.)*

Conscripts, Riflemen, Team Leaders, AT infantry and Snipers (`e1`, `ar`, `tl`, `at`, `sn`) inherit `^CapturesOccupiedBuildings`, which gives them a **1000-tick (40-sec) capture delay**. In practice this is far too long to be useful in a moving fight — a chest of riflemen is theoretically able to capture an enemy-held FCOM, but they'll all be killed standing in the doorway long before the timer runs down. The capability is a vestige of the trait inheritance, not a real play option. Use Technicians.

| Unit | Capture delay | Practical use |
|---|---|---|
| Technician (`tecn`) | 20 ticks (~0.8 s) | **The capture unit.** Always send one of these. |
| Engineer (`e6`) | — | Cannot capture in WW3MOD. (Tooltip says it can; trait is missing.) |
| Conscript / Rifleman / TL / AT / Sniper | 1000 ticks (~40 s) | Theoretical only. Too slow to use in combat. |

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

A Technician walking across the open is a $200 unit with no weapon and no armour. The cost-effective practice is to send him in the wake of an infantry squad or vehicle group already pushing toward that area — even one Rifleman alongside dramatically improves the survival rate against scouts. The v2 AI's capture coordinator does this automatically; for human players, it's a habit worth building.

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
| v2 (experimental) | `CaptureCoordinatorBotModule@v2.tecn` | Income-weighted scoring (BIO > FCOM > OILB), distance decay, safety bonus when no enemies nearby. Pulls 2 escort infantry along. Summons defenders when own captured under threat. |

See [`../../WORKSPACE/ai/v2_experiment_002_capture_coordinator.md`](../../WORKSPACE/ai/v2_experiment_002_capture_coordinator.md) for the v2 design and measurement plan.

There is also a legacy `CaptureManagerBotModule@engineer` (`e6`-based) in `ai.yaml` — this is **dead code**, because Engineers don't have the `Captures` trait. The module loads but never finds a capturer. Either the unit needs the trait back or the module entry needs to go.

## Code pointers

For the curious:

- **Capturable trait** — `engine/OpenRA.Mods.Common/Traits/Capturable.cs`
- **Captures trait** (capturers) — `engine/OpenRA.Mods.Common/Traits/Captures.cs`
- **CaptureManager** (per-actor) — `engine/OpenRA.Mods.Common/Traits/CaptureManager.cs:120` (`CanTarget` decides whether a given capturer can take a given building, based on `CaptureTypes` overlap + `ValidRelationships` of capturer vs owner)
- **CaptureActor activity** — `engine/OpenRA.Mods.Common/Activities/CaptureActor.cs` (the walk-in → flash → ownership change pipeline)
- **Templates** — `^NeutralOrOccupiedCapturable` in `mods/ww3mod/rules/ingame/structures.yaml:149` (capturable side); `^CapturesNeutralBuildings` / `^CapturesOccupiedBuildings` in `mods/ww3mod/rules/ingame/infantry.yaml:881-892` (capturer side)
- **Income** — `engine/OpenRA.Mods.Common/Traits/CashTrickler.cs`; building amounts in `mods/ww3mod/rules/ingame/structures-neutral.yaml`

## Open questions / flagged uncertainties

1. **Engineer (`e6`) description claims "captures" but the trait is missing.** Is this an intentional removal that the description never caught up with, or an accidental drop during a refactor? Worth a deliberate decision either way. If intentional: fix the description and clean up `CaptureManagerBotModule@engineer`. If accidental: add `Inherits@Capture: ^CapturesNeutralBuildings` to `^E6`. *(Author flagged this as a real design statement, so leaning toward "intentional, just clean up.")*
2. **Hospital effect.** The current trait list on `HOSP` doesn't include any healing — no `RallyPointHealthRegen`, no `HealthRegenAura`. The tooltip just says "Hospital". Either an effect is planned or the building is currently purely decorative. *(Marking as [planned] until told otherwise.)*
3. **Sabotage damage values in WW3MOD.** The `^CapturesNeutralBuildings` template doesn't appear to override sabotage damage. Worth a check on whether Technicians actually trigger the sabotage path on high-HP enemy buildings, or whether the threshold is effectively unreachable.
4. **Conscript-capture in practice.** Has anyone actually completed a 1000-tick (40 sim-sec) capture in a real match? If not, consider removing `^CapturesOccupiedBuildings` from non-Technician infantry to simplify the rules surface.

If you have answers to any of these, ping me and I'll fold them in.
