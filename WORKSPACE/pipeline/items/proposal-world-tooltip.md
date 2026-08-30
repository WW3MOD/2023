# Proposal — the world tooltip, and the depot number no player can see

**Status:** PROPOSAL ONLY. Not started, not scheduled. Written 2026-08-30 against
`main @ b3a7564d` from the tooltip audit (`WORKSPACE/tooltip-audit.md`).

**One line:** ammunition costs supply, supply comes out of a depot, and **nothing in the game ever
tells the player how much a depot holds.**

---

## The gap, stated precisely

`LOGISTICSCENTER.SupplyProvider.TotalSupply` is **2250** (`structures.yaml:466`).
`truk` holds **750** (`vehicles.yaml:569`), and so does a dropped `supplycache` (`misc.yaml:468`).
None of those three numbers appears on any tooltip, panel or readout in the game.

That was survivable while rearming was free. It is not survivable now: `f8b424f6` ("economy: all
supply costs") is already on `main`, so every rearm draws against those pools. The player is being
asked to budget against a quantity the interface has never named.

The asymmetry is sharp once you line the two surfaces up:

| | Production tooltip | World tooltip |
|---|---|---|
| Widget | `Background@PRODUCTION_TOOLTIP` (`engine/mods/common/chrome/tooltips.yaml:253-311`) | `Background@WORLD_TOOLTIP` (`:61-89`) |
| Logic | `ProductionTooltipLogic.cs` | `WorldTooltipLogic.cs` |
| Extension interface | **`IProvideTooltipDescription`** (WW3MOD-only, 1 implementor) | **`IProvideTooltipInfo`** (stock, 3 implementors) |
| Shows | name, cost, build time, prerequisites, description, per-weapon ammo maths, refill total | **name, owner flag, and `EXTRA`** |
| Reached by | hovering a sidebar icon | hovering anything on the map |

**Everything the audit improved lives on the left-hand column, and structures are never in it.**
All 16 `Buildable:` blocks in `structures-defenses.yaml` are `~disabled`, and `logisticscenter`
likewise (`structures.yaml:367`) — the LC is fielded by deploying an `LCCV`, not from a sidebar. So
no structure has ever had a production tooltip and none ever will under the current model. The only
surface a player can point at a Logistics Centre with is the world tooltip, and it has three labels.

## Why it is worse than "a missing number"

1. **The depot is the denominator of every supply decision.** "This HIMARS reload costs 3000" is
   meaningless until you know 2250 is a full Centre. The audit's own headline finding — that one
   HIMARS reload costs **more than an entire Logistics Centre holds** — is not something a player can
   currently derive at all.
2. **It is already drawn, just not labelled.** `SupplyProvider` renders a selection bar
   (`ISelectionBar.GetColor`, `SupplyProvider.cs:674`) and a range circle. The player sees a bar
   going down with no scale on it. A fraction without a denominator.
3. **The three providers differ 3:1 and look identical.** LC 2250, truck 750, crate 750. Nothing
   distinguishes them on hover.

## What it would take

Three pieces, in increasing cost.

### A. Surface supply on the world tooltip — the cheap 80%

`WorldTooltipLogic` already reads `IProvideTooltipInfo` into the `EXTRA` label
(`WorldTooltipLogic.cs:31`). Implementing that interface on `SupplyProviderInfo` puts
`Supply 1450 / 2250` on every LC, truck and crate **with no widget work at all** — one new method on
a trait that already holds both numbers.

This is genuinely small and it is the piece I would do first. It is not blocked on any of the typed-element
design.

### B. Give the world tooltip the typed elements

`WORLD_TOOLTIP` is `LABEL` + `FLAG` + `OWNER` + `EXTRA`, with `Container@SINGLE_HEIGHT` /
`@DOUBLE_HEIGHT` doing crude two-state sizing. To render `StatRow`/`CostRow`/`Note` here, it needs
the same treatment the production tooltip needs: replace the single `EXTRA` label with a vertical
container instantiating one pre-styled label per element kind.

The work is genuinely shared with the production-tooltip change — same element types, same style
table, same container. **But the interface is not shared:** `IProvideTooltipInfo` is stock OpenRA
with three existing implementors (`PowerTooltip`, `Sellable`, `TooltipDescription`), so either it
gains a typed sibling or the two surfaces converge on `IProvideTooltipDescription`. That decision is
the real content of this item and I have not made it.

### C. Converge the two interfaces

The honest end state is one description interface feeding both surfaces —
`IProvideTooltipDescription`'s doc comment already says "and any future tooltip surface". But
`IProvideTooltipInfo` runs on a **live actor** (it can say "1450 of 2250 *right now*") while
`IProvideTooltipDescription` runs at **rules-load time on static info** (`IProvideTooltipDescription.cs:17-21`).
That is a real semantic difference, not an accident, and collapsing them means giving the static one
an optional live-actor overload. Do not treat this as a rename.

## Sizing

| Piece | Widget work | Interface change | Size |
|---|---|---|---|
| A — supply on world tooltip | **none** | none (implement existing `IProvideTooltipInfo`) | ~20 lines |
| B — typed elements on world tooltip | container + style table (shared with production) | one new typed interface or a sibling | moderate |
| C — converge both interfaces | none extra | static vs live-actor split must be resolved | design-led |

**Recommended first step: A, on its own.** It closes the player-visible gap — the depot finally
states its capacity — without waiting on the element system, and it cannot conflict with the
production-tooltip work because it touches a different widget and a different interface.

## Traps for whoever picks this up

- **`logisticscenter` is not buildable and that is not a bug.** It is fielded by deploying an
  `LCCV` (`vehicles.yaml`, `Transforms: IntoActor: logisticscenter`). `~disabled` gates the sidebar
  icon, not the actor's existence — see `economy.md` Core principle 3, which records a previous
  error in exactly this spot.
- **`supplycache` holds 750, not 2250.** The supply-cost audit brief asserted 2250 and was wrong
  (`misc.yaml:468`); a crate is the truck's own load set down.
- **Do not add an armour row here either.** The `Kevlar`-in-zero-`Versus`-tables trap
  (`WORKSPACE/DISCOVERIES.md` 2026-08-30) applies to any surface, not just the production tooltip.
- **The world tooltip is shown for enemy and neutral actors too.** Anything surfaced here is
  surfaced about actors the player does not own, which is an information-disclosure decision, not
  just a layout one. An enemy LC's remaining supply is a *strong* intel signal and may want gating on
  ownership or on fog state. **This is the question I would want answered before writing piece A**,
  and it is the reason A is a proposal rather than something I did while I was in the file.
