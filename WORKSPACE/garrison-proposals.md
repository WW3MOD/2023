# Why garrisoning feels unintuitive — findings and ranked proposals

**Date:** 2026-08-16 · **Branch:** `wt/garrison` · **Base:** `main @ d1ad445d`
**Status:** research only. Nothing implemented. Every decision below is reserved for the user.

---

## 1. The anchor finding: half true, and the half that is false matters more

The brief's narrow claim **holds**. The conclusion drawn from it **does not**.

**Verified true.** `PortState.IsDucking` is declared at `GarrisonManager.cs:153`, written at
`:636`, `:641` and `:645`, and **read nowhere** — not in C#, not in YAML, not in Lua. The only
other occurrences in the repo are prose in `WORKSPACE/` audit and pipeline docs.

**Verified false — the important part.** Graduated suppression is **not inert**. It is fully
implemented, at ten-tier granularity, in YAML on the soldier, and it has nothing to do with
`IsDucking`. `^SuppressionEffects` (`infantry.yaml:381-392`) composes six sub-templates that every
infantry actor inherits via `infantry.yaml:15`:

| Effect | Template | At suppression 21–30 | At 91–100 |
|---|---|---|---|
| Rate of fire | `^SuppressionBurstMultiplier` (`:455`) | burst ×70% | ×0% |
| Fire cadence | `^SuppressionBurstWaitMultiplier` | wait ×130% | ×200% |
| Accuracy | `^SuppressionInaccuracyMultiplier` | ×140% | worse |
| Speed | `^SuppressionSpeedMultiplier` (`:393`) | ×70% | ×0% |
| Vision | `^SuppressionVisionModifier` (`:424`) | ×70% | ×0% |
| Readout | `^SuppressionPips` (`:548`) | 10 pip sequences | — |

The condition is a capped, self-decaying `ExternalCondition@Suppression` (`:388`, `TotalCap: 100`,
`ReduceTicks: 5`). Weapons apply it through `^Minimal/Small/Medium/Large/HugeSuppressionEffects`
(`weapons-effects.yaml:155-379`).

So **"soldiers under moderate fire keep firing at full rate" is not what the code does.** At
suppression 30 — exactly `SuppressionDuckThreshold` — a garrisoned soldier is already firing at
70% burst on a 130% cadence with 140% inaccuracy. `IsDucking` is a **vestigial duplicate** of a
shipped mechanic.

**Consequence, and this is the load-bearing part:** `WORKSPACE/audit/260816-systems-completeness.md:231`
and `PIPELINE.md:162` both recommend adding *"a rate-of-fire hook for `IsDucking`."* Implementing
that recommendation would apply a **second** fire penalty on top of `^SuppressionBurstMultiplier`
— soldiers would fall off a cliff at exactly 30 suppression. The recommendation as written is a
bug. That is worth more than any mockup.

## 2. So why *does* it feel unintuitive? Three real causes

### Cause A — ~~suppression is fully simulated and completely invisible while garrisoned~~ **FIXED 2026-08-17. This cause no longer exists.**

> **Correction, 2026-08-19 (verified at `de78a1ed`).** Cause A was diagnosed correctly and then **acted
> on** — proposal #1 below shipped at `97414046`. The two struck bullets are the measurements that were
> true when this document was written and are false now. Nothing here was wrong when written; it was
> simply never retired after the fix landed, and it has since been read as an open proposal set.
> **What is left of this section is Cause B and Cause C, plus the vocabulary half of #4.**

This is the answer. The state that drives everything the player sees is the one thing never shown.

- ~~The building's pip grid (`WithGarrisonDecoration.cs`) renders exactly **three** rows —
  `DamageRow`, `ClassRow`, `AmmoRow` (render loop, `:240-369`). **There is no suppression row.**~~
  **FALSE since `97414046`:** the grid renders **four** rows. `SlotRows = 4` and `SuppressionRow = 3`
  (`WithGarrisonDecoration.cs:84-88`); the pip is emitted in the render loop and its sequence picked by
  `GetSuppressionSequence`, bucketing into ten tiers `pip-suppression-1..10`. It is drawn for shelter
  occupants too. **The row's remaining weakness is different and is now tracked separately:** all ten
  frames are the same chevron in different hues, so the grid shows severity but not *trend* — see
  `cargo-garrison-status-260819.md` §4-A7.
- ~~The garrison panel prints `"{port}: {unit} [{ammo}/{max}] (80% cover)"`
  (`GarrisonPanelLogic.cs:179`) and `"[S] {name} ({prot}% cover)"` (`:227`). **No suppression.**~~
  **FALSE on both counts.** The 80% is no longer hardcoded: `CoverPercent(Actor)`
  (`GarrisonPanelLogic.cs:189-202`) walks the soldier's enabled `DamageMultiplier` traits, so it tracks
  `DamageMultiplier@GarrisonCover` in `rules/ingame/infantry.yaml` automatically. And suppression *is*
  printed — `SuppressionText` (`:180-183`) substitutes the live level for the cover figure while the
  port is suppressed. **That substitution is itself now a small open question** (the cover number
  disappears exactly when cover matters most): `cargo-garrison-status-260819.md` §4-A5.
- The soldier's own suppression pips exist but carry `RequiresSelection: true`
  (`infantry.yaml:550`), so they render only when that individual soldier is selected. A garrisoned
  soldier is drawn at 40% alpha (`WithAlphaCondition@GarrisonGhost`, `infantry.yaml:192-194`)
  standing on the building's own cell — the player's click almost certainly lands on the building.

The player therefore watches troops slow down, miss, stop shooting, and then get **yanked back
inside** at suppression 60 (`SuppressionRecallThreshold`, `GarrisonManager.cs:632-637`) with the
port locked out for 50 ticks — and **nothing on screen ever names the cause**. It reads as the
garrison randomly deciding to stop working. That is the unintuitiveness, and it is a rendering
gap, not a simulation gap.

### Cause B — four player-control orders are implemented and unreachable

`IResolveOrder` handles six orders (`GarrisonManager.cs:1334-1549`):

| Order | Issued by any UI? |
|---|---|
| `Unload` | yes — "Eject All" (`GarrisonPanelLogic.cs:51`) |
| `EjectGarrisonPassenger` | yes — per-port X (`:204`) |
| `AssignGarrisonPort` (`:1415`) | **no** |
| `SwapGarrisonPorts` (`:1497`) | **no** |
| `SetGarrisonPortTarget` (`:1522`) | **no** |
| `ClearGarrisonPortTarget` (`:1538`) | **no** |

A repo-wide grep for those four strings returns **only** the four `case` labels. Every lever that
would let a player *direct* a garrison — put the AT soldier on the north port, make that port shoot
*that* tank — exists in the simulation and cannot be issued. The garrison is a black box because
its controls were built and never surfaced.

### Cause C — the shelter/port model is never explained

Garrison uses a two-tier model: soldiers sit in **shelter** (Cargo) and auto-deploy to **ports**
when a target is confirmed. Nothing tells the player this. The panel's only hint is the bare
prefix `[S]`. There are **zero** garrison notifications or EVA lines, and no how-to-play or
encyclopedia entry. For strangers, six soldiers go into a building and two appear at windows for
reasons never stated.

**Why this matters more here than in Red Alert:** WW3MOD has no base building — no construction
yard, no factories, units arrive from off-map reserves through a fixed Supply Route
(`game-model.md`). Occupying the map's *pre-placed* buildings is one of the only fortification
levers a player has. Garrison carries more design weight in this game than it did in RA, which is
why illegibility costs more.

**Surface is larger than it looks:** garrison is on `^CivBuilding` (`civilian.yaml:2`, capacity 10)
— i.e. **every civilian building on every map** — plus `GTWR` (cap 6, 4 named ports), `PBOX` and
`HBOX` (cap 4 each).

---

## 3. Ranked proposals — perceived improvement per unit of work

**The top item is small, and that is the finding.** #1–#4 are presentation-only.

### #1 — Show suppression where the player already looks · **small** · highest ratio

Add a fourth row to the building pip grid and a suppression segment to each panel row.

- **Player sees:** a bar that fills and reddens as fire lands, a clear "DUCKING" state around 30,
  and a "PINNED" state at 60 that *predicts* the recall a beat before it happens. Troops stop
  firing for a legible, visible reason.
- **Cost:** the row machinery already exists (`DamageRow`/`ClassRow`/`AmmoRow` constants + row
  height maths); this adds a `SuppressionRow` in the same shape. Art already exists —
  `pip-suppression-1..10` (`sequences-misc.yaml:334-361`). Panel side is one interpolated string
  in `GetPortText`.
- **Replication:** **none needed.** This changes no simulation state. It reads
  `soldier.GetConditionCount("suppressed")` at *render* time — the same synced actor state the
  sim already uses at `GarrisonManager.cs:630`. No order, no new field, no desync surface.
  The one hard rule: the render path must only *read*; it must never cache into `PortState` and
  never issue an order.
- **What could go wrong:** the grid grows from 3 to 4 rows and may crowd small buildings at 10
  occupants — worth capping or showing suppression only above 0. Reading a condition per soldier
  per frame is cheap but is a per-frame `TraitsImplementing` walk if written carelessly.

### ~~#2 — Delete `IsDucking`~~ · **DONE — carried out at `97414046`. Nothing to dispatch.**

> **Correction, 2026-08-19 (verified at `de78a1ed`).** This proposal was accepted and executed.
> `PortState.IsDucking` and its only input `SuppressionDuckThreshold` are gone; a repo-wide grep of
> `engine/`, `mods/` and `tools/` returns exactly one hit for the token — the gravestone comment at
> `GarrisonManager.cs:98`, left deliberately so the next reader does not re-invent it. The "standing
> recommendation in two workspace docs" it warned about has also been retired: see
> `audit/260816-systems-completeness.md:531` (withdrawn 2026-08-17) and `PIPELINE.md:162`, which now
> reads "Do not implement a duck-tier fire penalty."

~~Remove the field and its three writes. It is dead, and while it exists the standing recommendation
in two workspace docs is to wire it — which would double-apply a fire penalty (§1).~~

- **Player sees:** nothing. This is insurance, not a feature.
- **Risk:** none functionally. The real risk is *not* doing it and someone implementing the audit's
  recommendation later.

### #3 — Name the pinned moment · **small**

When `SuppressionRecallThreshold` forces a recall (`:632-637`), surface it — a floating "PINNED"
over the building, optionally an EVA line. Today the soldier silently vanishes inside.

- **Player sees:** the single most confusing garrison event acquires a cause.
- **Replication:** the *event* is already sim-side and synced; only the presentation is local.
  Trigger the visual from the existing recall branch, do not add a new order.
- **What could go wrong:** noisy on a 4-port tower under heavy fire — needs a per-building cooldown.
  There are currently **no** garrison notifications at all, so this also sets the precedent; audio
  may be a step too far before the visual lands.

### #4 — Say "shelter" and "firing position" in words · **trivial**

Replace `[S]` with something a stranger can read; label the two panel groups. Consider a one-line
tooltip on the panel header stating the model: *soldiers wait in cover and man firing positions
when targets appear.*

- **Player sees:** the mechanic explains itself in the place they already have open.
- **Cost:** strings in `GarrisonPanelLogic.cs` / `garrison-panel.yaml`. No sim contact.
- **Note:** `(80% cover)` at `:179` is **hardcoded text**, while the actual value comes from
  `DamageMultiplier@GarrisonCover: Modifier: 20` (`infantry.yaml:189-191`). They agree today. They
  will silently disagree the day someone tunes the YAML. Cheap to derive instead.

### #5 — Surface the four dead orders · **medium-to-large** · real control, real risk

Drag a soldier onto a port; right-click a target for a specific port. The sim half is written.

- **Player sees:** garrison becomes something you *command* rather than watch.
- **Replication:** correct by construction — these are already `Order`s resolved in
  `IResolveOrder`; UI must issue via `world.IssueOrder`, exactly as `EjectPortOccupant`
  (`GarrisonPanelLogic.cs:204`) already does. Do **not** mutate `PortStates` from widget code.
- **What could go wrong — blocking:** **`SwapGarrisonPorts` (`:1497-1520`) swaps `DeployedSoldier`
  and `ConditionToken` but never swaps `CachedArmaments`.** After a swap each port fires through
  the *other* soldier's cached armaments. It is latent only because nothing issues the order. Any
  UI work here must fix that first. `AssignGarrisonPort`'s token handling (`:1488`) is also
  convoluted enough to deserve a fresh read before it is exposed.
- **Also:** hand-assignment fights the auto-deploy loop (`IdleRecallTicks`, `MinDeployTicks`,
  `RedeployBlackoutTicks`). A player-placed soldier being auto-recalled 250 ticks later would feel
  worse than no control at all. `PlayerOverride` exists but is only honoured for targeting.

### #6 — A distinct garrison cursor · **small** · low ratio

Today it is the generic `enter` cursor (`cursors.yaml:105`), identical to boarding a transport.
Worth doing eventually; it changes nothing about the confusion in §2.

---

## 4. Recommended shape

**#1 + #2 + #4 together** are a single afternoon, touch no simulation state, carry no desync
surface, and address the actual complaint. **#3** next. **#5** only as its own scoped piece of
work, and only after the `CachedArmaments` bug is fixed.

## 5. What I could not verify

- **I did not run the game** (not permitted, per brief). Everything above is read from source.
- **Whether a garrisoned soldier can be click-selected in practice.** `Selectable`
  (`infantry.yaml:57`) has no `RequiresCondition`, so it stays enabled — but the ghost stands on
  the building's own cell. If the building wins the click, the soldier's suppression pips are
  unreachable; if the soldier wins, they are merely undiscoverable. **This does not change
  proposal #1** (the panel and the grid show nothing either way), but it changes how bad "today"
  is. *To settle it: select a garrisoned `GTWR` under fire, then left-click directly on a
  40%-alpha soldier sprite at a window and screenshot whether pips appear above it.*
- **Whether suppression actually accumulates on port soldiers at a normal rate.** They have
  `DamageMultiplier@GarrisonCover` (80% reduction) but suppression arrives via `ExternalCondition`,
  not damage, so cover likely does **not** damp it — I did not trace a warhead through to confirm.
  If suppression lands at full strength on a soldier taking 20% damage, the recall threshold may
  fire far more often than intended, which would make #1 *more* valuable, not less.
- **`GarrisonProtection.GetCurrentProtection()`** — I read its call site, not its body.
