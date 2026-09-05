# Buying, holding and reclaiming powers — the economy re-wiring

**Researched against `main @ bb294b2d`** in worktree `wt/powers-economy` (`git status -sb` → `## wt/powers-economy`, no upstream, tree clean at start). **Static analysis only — no game launched, no autotest, no YAML lint.** Every mechanical claim carries a `file:line` I read at this SHA. Design opinion is marked **[opinion]**; everything else is read from code.

**Timestep:** default speed is 60 ms (`mod.yaml:358` → `:380-382`) ⇒ **16.667 ticks/s**. The economy interval is `PassiveIncomeInterval: 50` (`PlayerResources.cs:66`, not overridden — `player.yaml:328` is commented out) ⇒ **one payday every 3.00 s, 20 per minute.**

**Scope.** The user asked for a Powers tab you purchase from, that takes time, costs upkeep to hold, and refunds on cancel. The buy→bank→fire loop itself is already proven (`tools/autotest/scenarios/test-power-buy-loop/`, passed in a real game) and is not re-litigated here. This document is only about the *money*.

---

## 0. Headline findings — two of the manager's four premises are wrong

1. **WW3MOD already has a complete, shipped, per-player upkeep system, and it is not mine to invent.** `PlayerResources` carries `public float Upkeep` (`:190`), an `UpkeepEntry { ActorType, Name, Cost }` registry (`:125-130`, `:143`, `:146`), `AddToUpkeep(cost, actorType, name)` / `RemoveFromUpkeep(entry)` (`:371-391`), and a unified economy tick that pays income and bills upkeep in one line: `ChangeCash(PassiveIncomeAmount + (int)TotalBuildingIncome - (int)Upkeep)` (`:209`). The consumer trait is **`InfersUpkeep`** (`InfersUpkeep.cs`), already live on infantry (`infantry.yaml:155`) and vehicles (`vehicles.yaml:144`) at `PermilleCost: 5`. **The brief's premise "nothing in this mod does a recurring per-player drain" is false.** §2.
2. **The production queue does NOT give the user's cancel-and-refund. It gives a *different* refund, for a lifecycle phase the user was not talking about.** `ProductionQueue` refunds `TotalCost - RemainingCost` on every cancel path (`:189`, `:391`, `:628`, `:638`, `:672`) — but only while the item is *in the queue*. The instant `Produce` succeeds, `EndProduction(item)` (`:713`) removes it and the queue has no further record. The user's ask — cancel a power that is **bought, banked and sitting** — is entirely after that line. **The queue contributes build time and a purchase-in-progress refund; the ask itself is new code.** §1.
3. **The queue route is still right, but for a different reason: it is what forces the refund fraction.** Because cancelling at 99% build already returns 100% of what was paid (`:638`), any completed-purchase refund below 100% creates a **cliff at the moment of completion** — cancel one tick earlier and you are strictly better off. That is not a preference; it is arithmetic. §4.
4. **Upkeep turns proxy disposal from optional into mandatory.** The prior recon (`WORKSPACE/recon/powers-buy-loop.md` §2.1) found a spent proxy leaks and noted disposal was only needed if you wanted a bank cap. With upkeep, **a fired power keeps billing forever.** §6.
5. **The player-facing display is already built.** `IngameCashCounterLogic` renders a live `Cash (+$net)` in the sidebar (`:121`) and a tooltip breakdown with an `--- UPKEEP ---` section grouped by actor type, `Name xN: -$total`, a total, and `Net: ±$n / interval` (`:73-98`). A banked power appears there **for free**, by name, correctly pluralised. §7.

**Net effect: this is not a re-wiring of the economy. The economy is already here. The work is four small seams.** §8.

---

## 1. Where "buying" lives — a production queue, confirmed, with the cost named

A new `ClassicProductionQueue@Powers` on the Player actor alongside the six that ship (`player.yaml:23-93`), with `Powers` appended to `Production@Local: Produces:` on `SUPPLYROUTE` (`structures.yaml:362-364`). The buy-loop scenario proves the mechanism end to end on the `Defense` queue; a new type avoids colliding with `Defense`, which `rules/ai/ai.yaml` names (per the standing comment at `ingame-player.yaml:1270-1273`).

**What the queue costs you, against its unit-shaped assumptions:**

- **It drip-pays.** `ProductionItem.Tick` charges `costThisFrame` per tick so `RemainingCost` tracks the build fraction (`:823-828`). Money leaves gradually, not as a lump sum. **[opinion]** This is *better* for the fiction than a lump sum: allocating a budget line to Central Command over the days it takes to task the asset is exactly what a drip reads as.
- **It stalls rather than fails on insufficient funds.** `if (costThisFrame != 0 && !pr.TakeCash(costThisFrame, true)) return;` (`:825`) — the item freezes at its current percentage and resumes when cash returns. No new handling needed.
- **A contested Supply Route freezes a part-paid purchase and does not refund it** (`SupplyRouteContestation`, per prior recon §2.4). **[opinion]** Correct and worth keeping: your allocation is stuck in a contested corridor.
- **The tab greys, it does not hide.** `button.IsDisabled = () => !queues.Any(q => q.BuildableItems().Any())` (`ClassicProductionLogic.cs:46`). With every power gated off, the Powers tab is a greyed button, not an absent one.
- **The tab glyph is a real art task and the obvious shortcut is closed.** `chromeName = button.ProductionGroup.ToLowerInvariant()` (`ClassicProductionLogic.cs:80`) needs `powers`, `powers-disabled`, `powers-alert` in a 16×16 collection. I decoded `mods/ww3mod/uibits/glyphs.png`: it is 256×256 **and fully packed** — every 17-px slot on all three tab rows (y=68/85/102) carries artwork out to x=221, and there is no empty ≥16-row band anywhere on the sheet. **The fix is not to repack it:** `ImageCollection` is set per-button in YAML (`ingame-player.yaml:1235`, `:1253`), so a separate `powers-icons:` collection backed by its own small PNG sidesteps the packed sheet entirely.

---

## 2. Upkeep — already shipped; the powers work is one YAML block

`InfersUpkeep` computes `FixedCost + unitCost * PermilleCost / 1000` per interval (`InfersUpkeep.cs:35-42`), reading `Valued.Cost` off the actor (`:52`). It registers on `INotifyAddedToWorld` and unregisters on `INotifyRemovedFromWorld` (`:137-148`). **`World.Add` fires `INotifyAddedToWorld` unconditionally (`World.cs:394-402`); only `AddToMaps` needs a position** — so a bodiless proxy registers upkeep correctly.

Because the proxy carries `Valued: Cost:` already (it must, to be purchasable), `PermilleCost` works on it untouched. **`AllowMultiple` keys each purchase by ActorID (`SupportPowerManager.cs:48-51`), so three banked nukes are three proxy actors and three upkeep entries.** That falls out with nothing added — and the cash-counter breakdown already renders it as `Tactical Nuclear Strike x3: -$225`.

**Numbers at `PermilleCost: 5`** (the same rate every infantryman and vehicle already pays), against the prices approved in `260904-missile-powers.md` §9.3:

| Held asset | Cost | Upkeep / interval | Per minute |
|---|---|---|---|
| Rifleman (shipped) | 50 | 0.25 | 5 |
| Iskander launcher (shipped) | 6000 | 30 | 600 |
| **Fast strike (bought)** | 4000 | **20** | **400** |
| **Tactical nuke (bought)** | 15000 | **75** | **1500** |

Baseline passive income is 100/interval (`PlayerResources.cs:63`, lobby-selectable) = 2000/min, plus 50–150/interval per captured neutral (`structures-neutral.yaml:20`, `:52`, `:84`). **A banked nuke therefore eats roughly a third to three-quarters of a typical income.** **[opinion]** That is the right order of magnitude — it makes holding a doomsday option a standing sacrifice rather than a free hedge — but it is the single most tunable number in this document and I would expect the user to move it.

**One display trap, read from code:** the breakdown skips any group whose summed cost casts to zero — `var total = (int)group.Sum(e => e.Cost); if (total <= 0) continue;` (`IngameCashCounterLogic.cs:83-85`). This is why a rifleman at 0.25 never appears. **Keep every power's upkeep ≥ 1.0/interval or it bills invisibly.** At these prices that is satisfied with a wide margin.

---

## 3. Unpayable upkeep — the brief's default is disproven by the data model

The brief's default: *the reservation lapses and the player is refunded at the cancel rate.* **This cannot be implemented honestly, and the reason is structural rather than aesthetic.**

`Upkeep` is a **single pooled float** (`PlayerResources.cs:190`). The tick bills the pool as one number (`:209`), and `ChangeCash` clamps it: `amount = Math.Max(-(Cash + Resources), amount)` under the comment *"Don't put the player into negative funds"* (`:221-222`). **There is no per-entry billing.** Nothing in the engine knows *which* upkeep line went unpaid, because no line is billed individually — a shortfall is a property of the player, not of a power. So "lapse the reservation" would require the game to *choose a power to destroy*, and it has no basis on which to choose. Cheapest? Newest? The one the player was saving for?

Two further problems with lapse: it is **irreversible**, taken on the player's behalf, at the worst possible moment; and the refund itself **pushes cash back up**, which can make upkeep affordable again — so the game would have destroyed a purchase the player could in fact have kept.

**Recommendation: dormancy, and it is nearly free.** Grant a condition on shortfall and put `PauseOnCondition:` on the bought power. `SupportPowersWidget` already draws `HoldText` over any icon whose power is not `Active` (`:241-243`), and `Active = !Disabled && Instances.Any(i => !i.IsTraitPaused)` (`SupportPowerManager.cs:200`). **The "ON HOLD" overlay Central Command would put on your strike is already rendered by shipped code.** Dormancy applies uniformly to every banked power, needs no arbitrary choice, and reverses the moment the player is solvent.

**The one thing this needs is a shortfall signal, and it is three lines.** `PlayerResources.Tick` discards `ChangeCash`'s return value at `:209`; `ChangeCash` already returns the *clamped* amount (`:215-227`). Capture it, compare to the requested amount, expose the difference. Everything else keys off that.

---

## 4. Refund fraction — full, and it is forced rather than preferred

**The brief's default survives, but the argument for it is stronger than the one given.** It is not that full refund "matches the fiction" (though it does). It is that **any fraction below 100% is discontinuous at completion.**

Cancel a purchase at 99% and `ProductionQueue` returns `TotalCost - RemainingCost` — effectively everything paid (`:638`). If a *completed* purchase refunded, say, 75%, then finishing the purchase would destroy 25% of the player's money at the instant the bar filled, and the optimal play would be to hold every purchase at 99% forever. **Full refund is the only value that makes the two sides of `EndProduction` (`:713`) agree.** The alternative — lowering the queue's refund to match — would change refunds for every infantry and vehicle in the mod and is out of scope.

**The exploit named in the brief — buy and cancel to park cash — is already closed by §2.** Parking 15000 credits in a banked nuke costs 1500/minute in upkeep. Full refund plus upkeep is self-limiting: the reclaim is whole, and the *time* is what you paid for. That is precisely the user's fiction — *"you reclaim the allocation; you do not reclaim the time it sat reserved."*

---

## 5. Purchase time replaces charge time, and the three shipped powers move

**Yes, outright, and double-gating is avoided by the config the proven scenario already uses:** `ChargeInterval: 0, StartFullyCharged: true` (`test-power-buy-loop/rules.yaml`). A bought power is ready on arrival; the purchase *is* the wait.

**What this does to the shipped three.** They are currently `MissileStrikePower@Kinzhal` / `@GBU57` / `@TacNuke` on the **Player actor** with real `ChargeInterval`s (the tacnuke's is 11250 ticks = **11 m 15 s** at 60 ms — `player.yaml:237`). Under the buy model those three Player-actor entries are **deleted** and re-declared on three proxy actors with zero charge. That is the re-wiring the user anticipated, and it is a YAML move, not engine work.

**The lobby-gated nuke: gated OFF must mean absent from the tab, and that needs a mechanism change.** Today the gate is `RequiresCondition: !tacnuke-disabled` on the *power* (`player.yaml:228`), which makes the icon absent because `SupportPowersWidget` filters on `!p.Disabled` (`:136`). But `Buildable.Prerequisites` reads the **tech tree**, which does not see conditions. The bridge is shipped: **`ProvidesPrerequisiteInfo : ConditionalTraitInfo`** (`ProvidesPrerequisite.cs:19`) supports `RequiresCondition`, and its `TraitEnabled`/`TraitDisabled` call `techTree.ActorChanged(self)` (`:94-104`). So:

```
ProvidesPrerequisite@tacnuke:
    Prerequisite: tacnuke.enabled
    RequiresCondition: !tacnuke-disabled     # same GrantConditionOnLobbyOption, unchanged polarity
```

on the Player, and `Buildable: Prerequisites: ~tacnuke.enabled` on the proxy. **The `~` prefix is load-bearing** — it hides the item outright; an unprefixed prerequisite would leave a greyed cameo advertising a power the host disabled (prior recon headline 11). **The `GrantConditionOnLobbyOption` polarity at `player.yaml:217-220` must not be touched** — its comment explains why an absent option must read as *disabled*.

---

## 6. Cancelling something already fired — "once it launches it is gone", and upkeep makes that mechanical

**The rule: firing disposes the proxy in the same frame-end batch as the launch.** This is not a policy choice bolted on; it is forced, because a spent proxy that survives **keeps paying upkeep forever** (`InfersUpkeep` only unregisters on `INotifyRemovedFromWorld`, `:142-148`). `OneShot: true` merely hides the icon via `oneShotFired` (`SupportPowerManager.cs:268`); the actor leaks.

**I verified this is safe against an in-flight strike.** Everything `MissileStrikePower.Activate` defers captures `self.Owner` (a `Player`), `targetPosition`, `info`, and the `missile` actor — the `RevealShroudEffect` at `MissileStrikePower.cs:140` and the `Beacon` at `:149-165` both take `self.Owner`, not `self`. **No deferred structure dereferences the proxy actor after activation**, so disposing it at frame end cannot break a missile already on its way. This holds regardless of how long the flight takes, so it does not couple to the arrival-delay work on `wt/powers-airburst`.

Cancel therefore has a clean boundary with no in-flight ambiguity: **the power is cancellable exactly while its icon is in the bin, and firing removes the icon in the same frame it removes the actor.**

---

## 7. What the player sees — mostly already built

- **Standing cost, always visible.** The sidebar cash counter reads `$12,340 (+$85)` — cash plus net change per interval — every frame (`IngameCashCounterLogic.cs:119-121`). Buying a nuke moves that number visibly and immediately. **This is the single most important piece and it already ships.**
- **The breakdown, on hover.** The tooltip prints an `--- UPKEEP ---` block grouped by type with counts (`:73-92`), a total, and `Net: ±$n / interval` (`:93-98`). A banked strike appears as `Tactical Nuclear Strike: -$75`, and three as `... x3: -$225`, using the proxy's `Tooltip: Name:` (`InfersUpkeep.cs:112-119`).
- **Per-item, before you buy.** `InfersUpkeep` implements `IProvideTooltipDescription` (`:81-93`) and renders an `Upkeep: 20 cash / interval` row directly under `Valued`'s cost row in the production tooltip (`TooltipPriority = 502`, `:26`). **The buy cameo tells you the running cost before you commit, with no new code.**
- **Ready vs. on-hold.** Ready powers draw `ReadyText`; dormant ones draw `HoldText` (`SupportPowersWidget.cs:237-243`). **[opinion]** Bought-and-in-flight needs no state of its own — the icon is gone, because the actor is gone (§6).
- **[opinion] The one thing I would add:** the Powers tab's own running total. Everything above is per-item or global; nothing says "your reserves cost you $95/interval". `SupportPowerInstance.IconOverlayTextOverride()` (`SupportPowerManager.cs:272`) is a shipped virtual seam for per-icon text if a tighter cue is wanted later. Not required for v1.

---

## 8. What this actually costs to build — four seams

| # | Change | Size |
|---|---|---|
| 1 | `ClassicProductionQueue@Powers` + `Powers` on `Production@Local` + tab button + a `powers-icons` collection and a 16×16 PNG | YAML + art |
| 2 | Move the three `MissileStrikePower` blocks off the Player onto three proxies; `ChargeInterval: 0`; `InfersUpkeep`; `ProvidesPrerequisite` for the nuke gate | YAML only |
| 3 | **Cancel-a-banked-power order:** dispose the proxy, `GiveCash(Valued.Cost)`. Needs a button test in `SupportPowersWidget.HandleMouseInput`, which currently routes **every** button to `ClickIcon` with no discrimination (`:275-296`) | ~60 lines C# |
| 4 | **Dispose on fire** (§6) and **shortfall signal** (§3, capture `ChangeCash`'s return at `PlayerResources.cs:209`) | ~15 lines C# |

**No new economy system, no new tick, no new registry.**

---

## 9. The counter-layer gap — unprompted opinion

**[opinion] The economy work makes the gap materially more urgent, and for a reason the arrival delay alone does not create.** `CRAM`, `AGUN` and `SAM` all ship behind `Prerequisites: ~disabled` while ballistic missiles are buildable. An arrival delay opens an interception *window*; upkeep is what makes that window **worth defending against**, because it converts an enemy's banked strike from a hidden timer into a **standing, visible economic commitment**. A player who knows the enemy is bleeding 1500/minute knows a nuke exists and is coming — and currently has nothing whatsoever to do about it. That asymmetry is new: before this work, not being able to intercept was invisible; after it, the player is told a strike is inbound-in-principle and handed no counter. **I would not block the economy work on it, but I would not ship a *third* powers feature before it.** Tracked separately; not mine.

---

## 10. What the user must decide

1. **Upkeep rate.** Recommended `PermilleCost: 5` — the same 0.5%/interval every infantryman and vehicle already pays, so "a reserved strike costs what a parked tank costs". That is **20/interval (400/min)** for a 4000 strike and **75/interval (1500/min)** for a 15000 nuke, against a 100/interval baseline income. **This is the number most likely to be wrong.** `3` is the obvious softer alternative.
2. **Unpayable upkeep: dormant, or lapse-and-refund?** Recommended **dormant** — §3 argues lapse cannot pick a victim honestly, is irreversible, and can destroy a purchase the refund itself would have made affordable. Dormancy reuses the shipped "ON HOLD" overlay.
3. **Refund fraction: full?** Recommended **full**, and §4 argues it is forced rather than preferred — any lower value creates a cliff at the instant the purchase completes, because the queue's own cancel path already returns 100% one tick earlier.
4. **Do the three shipped powers become purchase-only, or stay chargeable too?** Recommended **purchase-only**; a power that both charges and costs money is the double-gate the brief names as the failure mode. This deletes three Player-actor trait blocks including the tacnuke's 11 m 15 s charge.
5. **How is cancel invoked?** Recommended **right-click the power icon in the bin**, which is where the player already looks at their reserves. It needs a button test added to `SupportPowersWidget.HandleMouseInput` (`:275-296`), which today routes every button to the same click handler. The alternative is a cancel affordance in the Powers tab, which is where the *purchase* lives but not where the *reserve* is displayed.
6. **[opinion] Is the tab glyph worth a bespoke PNG?** The shared `glyphs.png` is full (§1); a separate small collection is the cheap route and needs one 16×16 icon in three states (plus 2x/3x). Confirming the art route now avoids blocking the merge on it later.

---

## 11. What I did not verify

- **Nothing was run.** No game, no autotest, no lint, no build. Every claim above is read from source at `bb294b2d`.
- **Whether `ClassicProductionQueue` behaves identically to `ProductionQueue` on the cancel paths.** I read the base class; the subclass overrides `BuildUnit` and `TickInner` and I did not re-read every override for a refund path of its own.
- **Whether `IngameCashCounterLogic`'s tooltip is actually reachable in the shipped chrome.** I read the logic, not the widget tree that hosts it.
- **The upkeep numbers are arithmetic on shipped rates, not observed play.** Whether 1500/min for a banked nuke is punishing or trivial depends on real income curves I did not measure.
