# Safe wins and ambitious swings — 260902

Assembled from the five 2026-09-02 recon documents plus the two pip notes, and re-verified against
code. **Read and verified at `main @ 9b687fef`** (worktree `wt/safe-wins`). The recon documents were
written against `6a7e1839`, which is an ancestor **42 commits back** — so **where this document and a
recon document disagree, this one was read later and against newer code.** I scanned all 42 subjects
and opened the three that looked adjacent (`9b687fef` evac-edge-node, `98fb92e1` captor naming,
`49afe9e9` vision transfer); none implements anything proposed here. One of them *helps*, and is
credited in swing 4.

Read-only pass. No game launched, no build, no validator run, no engine/YAML/scenario file changed.

---

## The verification standard used here, and what it cost

**Every `file:line` below was opened and read in this worktree.** Nothing is relayed from the recon
document that cited it. That discipline was not ceremonial — it changed the document four times:

- It **killed half of one proposal.** The Logistics Centre supply readout that the economy audit
  wanted built already ships: `SupplyProvider` implements `ISelectionBar`
  (`engine/OpenRA.Mods.Common/Traits/SupplyProvider.cs:225`, value at `:1231-1237`,
  `DisplayWhenEmpty => true` at `:1239`), and `LOGISTICSCENTER` carries the trait
  (`structures.yaml:469`).
- It **refuted a headline sentence.** The silent-refusals audit's Supply Route finding opens
  *"Nothing ever told you the building cannot be damaged."* The How To Play panel says it, in those
  words: *"You cannot build it, move it, or destroy anyone's. Supply Routes are indestructible."*
  (`chrome/ingame-info-howtoplay.yaml:88-95`).
- It **corrected a cost model.** The concealment inventory says the per-observer detection margin is
  "computed, then collapsed to `bool`". It is not computed at all — `IsSpotted` returns on the first
  qualifying observer (`WithSpottedDecoration.cs:115-116`) and `VisionCovers` returns on the first
  qualifying band (`:153`). Grading costs the **loss of the short-circuit**, not the recovery of a
  discarded number.
- It **found one item stronger than filed.** `EvacuateWhenUnrearmable` does not merely appear on the
  template without the pip — it appears **exactly once in the entire mod** (`aircraft.yaml:195`), on
  that template.

**Where a proposal rests on something I derived rather than measured, it says so in place** — that is
safe wins 5 and 10 (both arithmetic off YAML and the timestep), safe win 9 (a visual judgement no
amount of reading can make), and swing 3, which is the entry I am least confident about and gets its
own section at the end.

---

## What is off the table, and why — read before objecting to an omission

**The whole ambush / concealment / cover surface is user-gated and nothing from it is proposed here.**
`PIPELINE.md:423-434` gates items 67–71 and 22 on an explicit user ruling (*"I will let you know when
we are ready to implement, until then just ask me"*), and the same block records a standing
sequencing rule that bites this document directly:

> **Deliberately NOT queued: a legibility / readout item.** … *shipping a readout around an absent
> behaviour and reporting it as the answer.* Legibility is a sequencing rule, not a scope rule: it
> follows a behaviour fix, it does not substitute for one.

That rules out four otherwise-attractive candidates: the graded "someone is about to see you" ring,
the ambush-sprung glyph, the aim-delay bar, and any readout of `ClearSightThreshold` / foliage cover.
They are good ideas inside a closed door. **Do not smuggle them in as safe wins.**

One correction belongs to that gated territory and is recorded here because it changes the cost of a
gated item rather than proposing work: **PIPELINE item 67 says "No clamp exists."** A clamp exists —
`Detectable.ClampConcealment` (`Detectable.cs:118-125`) floors at 1 and ceilings at
`MapLayers.VisionLayers - 2`. It is not the floor the user asked for: it floors the *concealment
value*, not the *observer's ability to see*. The code says what closing the real gap would cost, at
`:111-112` — *"closing that needs the observer floor raised in AddSource, which also moves fog
rendering, radar and the AI belief layer."* Item 67 is not the one-line clamp its title implies.

Also excluded: anything whose subject ships `Prerequisites: ~disabled` (per the 2026-08-16 visibility
ruling), and anything already on `PIPELINE.md`. R9, R12, item 17 and items 67–71 are named explicitly
below wherever a proposal runs alongside them.

---

# TIER 1 — SAFE WINS

The mechanism exists; what is missing is a readout, a message, or a few lines of wiring. Ordered:
**do them top-down.**

---

## 1. The "you cannot capture that" cursor is computed, then thrown away

> **CORRECTED 2026-09-02 (`wt/capture-affordance`) — DO NOT APPLY THE FIX AS WRITTEN BELOW.**
> The prescribed one-token change (`return false` → `return true`) is not a safe win: it would
> accept **every actor click** by all 34 capturer infantry at priority 6, tying with Attack
> (`AttackBase.cs:475`) and outranking `EnterTransport` (5) and Move (4). The refusal branch at
> `Captures.cs:145-149` also fires when the target carries **no `CaptureManager` at all**, so it
> does not mean "a capture target you cannot take". `EnterAlliedActorTargeter` is not the analogous
> pattern: it returns `false` from its kind test first (`:45-46`) and reaches its blocked-cursor
> line only for real transports. The defect described below is real and still worth fixing; the
> sizing ("hours", "no new design decision") is not. Evidence, the empirical actor counts and what a
> correct fix must do: `WORKSPACE/DISCOVERIES.md`, entry dated 2026-09-02.

**Why this one first.** It is the earliest and most frequent thing a new player does — walk a man
into a building — and it is the one place where the game *affirmatively lies*: it paints a move
cursor for an order it will never carry out. The art exists, the `Info` field exists, the correct
pattern is two files away, and **the user has already ruled on this exact trade-off**, so it needs no
new design decision. Everything else on this list is either bigger, rarer, or needs a judgement call.

**What the player experiences.** You select a rifleman and right-click a neutral oil derrick. You get
an ordinary move cursor, the man walks over, stands on the doorstep, and nothing ever happens. Only a
technician can take a neutral building. Nothing at any point says so.

**Mechanism.** `Captures.cs:142-149` writes the blocked cursor into the `ref` parameter and then
returns `false`:

```csharp
var targetManager = target.TraitOrDefault<CaptureManager>();
if (targetManager == null || !captures.CaptureManager.CanTarget(targetManager))
{
    cursor = captures.Info.EnterBlockedCursor;   // :147
    return false;                                 // :148
}
```

The write is discarded. `UnitOrderGenerator.cs:333-335` declares `cursor` fresh inside the loop and
abandons it on a false return. The default is real (`Captures.cs:72`,
`EnterBlockedCursor = "enter-blocked"`) and the art is drawn (`cursors.yaml:111`).

The rifleman case is confirmed at the YAML: `^CapturesOccupiedBuildings` (`infantry.yaml:916`) sets
`ValidRelationships: Enemy` (`:927`), so a *neutral* derrick fails `CanTarget` for every soldier in
the game. That same block sets `CaptureDelay: 1000` (`:921`) and `CaptureToNeutral: true` (`:928`) —
soldiers clear buildings, they never own them.

**Citation that proves it does not exist.** The contrasting correct pattern is
`EnterAlliedActorTargeter.cs:56` — `cursor = useEnterCursor(target) ? enterCursor : enterBlockedCursor;`
followed by `return true;`. `Captures.cs` is the odd one out, not the convention. `en.ftl` (the mod's
only `.ftl`, verified) has **0** case-insensitive matches for `captur`.

**Size.** One method in one file. Hours.

**The single thing most likely to make it more expensive than it looks.** Returning `true` makes the
capture targeter **consume** the click at `OrderPriority: 6` (`Captures.cs:81`), which is *above*
`EnterTransport` at 5. A soldier who is both a capturer and a passenger could have an enter-transport
click on a capturable building swallowed by a capture order it will not execute. That is a real
regression and it is not visible in the diff — budget a deliberate read of the priority ladder, not a
blind flip.

---

## 2. Fix the How To Play panel in one pass — it is wrong twice and silent on the dominant income source

**Why second.** Highest teaching value in the document, zero mechanical risk, pure text — but it
should be **one edit, not four**. Three separate defects live in `ingame-info-howtoplay.yaml`, plus
the queued R9, and doing them one at a time means re-flowing the same hardcoded `Y:` offsets four
times and writing the same block twice.

**(a) The panel never mentions where the money comes from.** Verified by grep: `captur`, `derrick`,
`technician`, `oil` and `income` return **zero** hits across the file; the only two hits for the
whole set are the word "budget" at `:39` and `:151`. Yet three neutral actors carry `CashTrickler` —
OILB `Amount: 50` (`structures-neutral.yaml:19-20`), FCOM `100` (`:51-52`), BIO `150` (`:83-84`) —
against a passive income of `PassiveIncome = 100` (`PlayerResources.cs:62`) paid on the same
`PassiveIncomeInterval = 50` line (`:66`, charged at `:209`). **One oil derrick is half a player's
entire base income**, and I re-ran the map census rather than trusting it:

| map | income buildings | | map | income buildings |
|---|---|---|---|---|
| x-lake-ww3 | 17 | | woodland-warfare-ww3 | 9 |
| polar-disorder-ww3 | 12 | | nuclear-winter-ww3 | 8 |
| river-zeta-ww3 | 12 | | seventh-woods-ww3 | 6 |
| twin-rivers-ww3 | 10 | | siberian-pass-ww3 | 4 |
| | | | arena-tank-duel, shellmap-open-field | 0 (dev maps) |

**(b) The panel tells the player something the code does not do.** `:144`/`:151` read *"Spent units
can Evacuate, leaving via your Supply Route to recover what is left of their cost."* Ground units do
not go to the Supply Route: `RotateToEdge.cs:165-166` is
`var searchOrigin = spawnAreaHintGround ?? self.Location;` — the unit's **own cell** on every map with
no `spawnarea` actor, which is nine of ten (`river-zeta-ww3` is the only file that has any). The
Evacuate button's own tooltip already contradicts the panel: *"leave the battlefield via the map
edge"* (`chrome/ingame-player.yaml:326`). Two shipped surfaces disagree.

**(c) The highest-leverage defensive action in the game is undiscoverable.** Recovery from
contestation triples when any friendly unit with `Valued.Cost > 0` is inside the ring —
`SupplyRouteContestation.cs:475-477` and `:495-497`, `friendlyBoost = cachedNetFriendlySurplus > 0 ?
info.FriendlyRecoveryMultiplier : 1`, with the multiplier `3` (`:72`, YAML `structures.yaml:313`).
Against `BaseRecoveryTicks: 3000` that is ~182 s unattended versus ~61 s with one rifleman standing
there — **faster than the ~91 s drain**. The panel's only recovery sentence is *"push them off and it
recovers"* (`:137`).

**Citation that proves none of this exists.** Repo-wide, `FriendlyRecoveryMultiplier` outside
`WORKSPACE/` and `DOCS/` resolves to exactly four sites: the declaration (`:72`), the two `Tick`
consumers (`:476`, `:496`), and the YAML value (`structures.yaml:313`). **Zero UI, notification,
tooltip or panel references.** The income words return zero in the panel, as above.

**Size.** Hours, mostly layout. The text is minutes.

**The single thing most likely to make it more expensive than it looks.** The panel is laid out with
hardcoded `Y:` offsets and is already four headed blocks plus a footer. Adding a fifth means either
re-flowing everything below it or displacing an existing block — a judgement about which of the four
existing points is weakest, which is a design call, not a copy edit. It also wants a screenshot pass
(`DOCS/recipes/SCREENSHOT.md`), which this pass could not run.

**Overlap, stated:** R9 is live in this same file at `:123`/`:130` and its 2026-09-01 verdict names
exactly those two lines. (a), (b) and (c) are different sentences. **Bundle them.**

---

## 3. Helicopters are the only thing in the game that leaves on its own, and the only thing with no marker for it

**What the player experiences.** Your helicopter runs out of missiles and flies off the map, unasked.
No icon, no message, no sound. An expensive unit disappears over the edge for no stated reason.

**Mechanism.** The behaviour and the marker sit on templates that do not inherit each other.
`^Aircraft:` opens at `aircraft.yaml:119` and carries `WithDecoration@Evacuating` at `:153-158`
(`Image: pips` / `Sequence: pip-orange` / `Position: TopRight` / `RequiresCondition: evacuating`) plus
`SelectionPriorityModifier@Evacuating` at `:150-152`. `^Helicopter:` opens at `:160` and inherits
**`^Airborne`, not `^Aircraft`** (`:161`). It carries `EvacuateWhenUnrearmable:` at `:195`.

**Citation that proves it does not exist — and it is stronger than the audit that filed it.**
`grep -rn "WithDecoration@Evacuating\|EvacuateWhenUnrearmable" mods/ww3mod/rules/` returns four lines
total: the pip on `vehicles.yaml:138`, on `infantry.yaml:161`, on `aircraft.yaml:153`, and
`EvacuateWhenUnrearmable` on `aircraft.yaml:195`. **`EvacuateWhenUnrearmable` occurs exactly once in
the whole mod, and it is on the one template with no pip.** Six live actors inherit `^Helicopter`
(three in `aircraft-america.yaml`, three in `aircraft-russia.yaml`). The condition is already
available to them: `ExternalCondition@Evacuating: Condition: evacuating` is declared in
`^NeutralAirborne` at `aircraft.yaml:14-15`, which `^Airborne` inherits at `:101`.

**Size.** Six lines of YAML, copied verbatim from either sibling. Minutes.

**The single thing most likely to make it more expensive than it looks.** Hoisting the block into
`^Airborne` instead of copying it into `^Helicopter` — which is the tidier instinct — makes it merge
with the existing `^Aircraft` copy rather than replacing it. Copy it, or hoist it *and* delete the
`^Aircraft` copy in the same edit.

---

## 4. The Deploy button lights up when the game already knows nobody can deploy

**What the player experiences.** You select two neutral oil derricks. The Deploy button is enabled.
You press `F`. Nothing happens — no sound, no error, no unit moves. You press it again. Still
nothing. You own no technicians, and the button never said so.

**Mechanism.** `CommandBarLogic.cs:160-166`, inside `IsDisabled`, early-outs unconditionally:

```csharp
if (selectedCaptureTargets.Length > 0)
    return false;                       // :166 — enabled
```

`selectedCaptureTargets` fills from selected actors the player does not own that carry
`CaptureManagerInfo` (`:488-493`). **There is no capturer term anywhere in the predicate.**

**Citation that proves the answer already exists elsewhere.** `CaptureDispatchManager.Evaluate`
(`CaptureDispatchManager.cs:120-150`) returns `CaptureDispatchState.NotATarget` when
`eligible.Count == 0` (`:128-129`), and `EligibleCapturers` (`:78-91`) already excludes riflemen by
filtering `!p.Trait.Info.CaptureToNeutral` (`:89`). `CursorForState(NotATarget)` returns `null`
(`:161-162`). **The button's own click handler already calls into this class** (`DispatchAcross`,
`:180`, reached from `CommandBarLogic.cs:529`) — the enable predicate simply never asks. And
`grep -c "PlayNotification\|AddTransientLine" CaptureDispatchManager.cs` returns **0**, so the
failed press really is silent.

**Size.** One predicate. Hours.

**The single thing most likely to make it more expensive than it looks.** `Evaluate` is per-target
and does a `world.ActorsWithTrait<Captures>()` scan; the enable predicate runs every frame the
command bar is drawn. It needs to ride the existing `UpdateStateIfNecessary` / `selectionHash`
caching (`CommandBarLogic.cs:499`) rather than being called raw, or it is a per-frame world scan.

---

## 5. Crossing half health does five things at once, and the game says none of them

**What the player experiences.** A tank drops below half health and, with no warning, stops turning
its turret, stops shooting, halves its speed, and dies about fifteen seconds later. The only thing on
screen is a small pulsing damage pip and some smoke. A new player concludes the game is broken: full
ammo, clear shot, refuses to fire.

**Mechanism — all five fire on the same threshold.** `heavy-damage-attained` is granted at
`ValidDamageStates: Heavy, Critical` (`defaults.yaml:258-260`), and `Health.DamageState` puts Heavy
at `HP * 100L < MaxHP * 50L` (`Health.cs:108-109`). On `^EffectsWhenDamagedVehicles`
(`vehicles.yaml:180`, inherited by `^Vehicle`):

| At HP < 50% | Where |
|---|---|
| turret turn speed → **0** — a hard lock; the in-file comment at `:283-290` says the turret "refuses to acquire" | `vehicles.yaml:291-293` |
| ground speed → **50%** | `vehicles.yaml:181-183` |
| every armament paused (24 `PauseOnCondition` gates carry the token; count re-grepped) | e.g. `vehicles-america.yaml:88`, `:244`, `:375`, `:529` |
| autotarget scan radius → **0** (see swing 1) | `AttackBase.cs:596-597`, `AutoTarget.cs:1114`, `:1177` |
| bleeds 1% of MaxHP every 5 ticks — 50% → 0 in ~15 s at 16.67 tps | `vehicles.yaml:184-188` |
| smoke ignites | `vehicles.yaml:219` (`StartFraction: 50`) |

**The behaviour is a user ruling and is not in question.** Only the presentation is missing.

**Citation that proves it does not exist.** `en.ftl` returns zero matches for `armour`, `armor`,
`penetrat` and `ammo`, and its only damage-adjacent line is an unrelated settings label. The How To
Play panel contains no damage, health or critical text at all. And there is no health bar to read it
off: `DrawHealthBar` is commented out at
`engine/OpenRA.Mods.Common/Graphics/SelectionBarsAnnotationRenderable.cs:181` — the four-band damage
pip is the entire health readout.

**Size.** The panel page plus a decoration on the existing `heavy-damage-attained` condition is pure
YAML and text — hours.

**The single thing most likely to make it more expensive than it looks.** The third surface — a
bleed-out countdown bar — is not a readout, it is a design conversation: a countdown invites *"so can
I stop it?"*, and today the answer is "reach a Logistics Centre", which seven of ten shipped maps do
not have. **Ship the page and the decoration; leave the countdown to swing 8.** Second, smaller:
space above a unit is crowded — spotted `!`, two stance glyphs and the holding-fire pip already live
there (`defaults.yaml:888-904`).

---

## 6. Put a number on the Evacuate button

**What the player experiences.** You select a beaten-up Abrams that has fired most of its rounds and
have to answer the question the whole economy is built around — *one more fight, or bank it?* — with
no figure anywhere on screen. The button's own tooltip promises the figure in prose:
*"refunding their value to your budget"* (`ingame-player.yaml:326`).

**Mechanism.** The value is one call. `CustomSellValueExts.GetEvacuationRefund`
(`CustomSellValue.cs:86`) wraps `GetSellValue` (`:28`), which is pure over `ActorInfo` plus current
ammo and supply.

**Citation that proves it does not exist.**
`grep -rn "GetSellValue\|GetEvacuationRefund" engine/OpenRA.Mods.Common/Widgets/` returns **nothing**
(exit 1, no matches). Every call site is a trait, activity or bot module —
`VehicleCrew`, `AmmoPool`, `EvacuateWhenUnrearmable`, `AirframeEvacMath`, `TransportEmploymentMath`,
`PoiOffensiveBotModule`, `HelicopterSquadBotModule`, `AmmoEvacMath`, `DropsSupplyCache`. **No UI
surface in this game has ever read what a fielded unit is worth.** The nearest formatter,
`Sellable.TooltipText`, is gated on `SellOrderGenerator` and no unit carries `Sellable` — all seven
live declarations are buildings (`structures.yaml:135`, `:437`;
`structures-defenses.yaml:62, 86, 192, 288, 533`).

**Size.** Hours for a single-unit figure; longer with aggregation.

**The single thing most likely to make it more expensive than it looks.** **The preview will drift
from the payout, and quietly.** The refund's ammo term is frozen at order time while the HP term is
applied on arrival, so a unit shot on the walk home pays less than the number said. Either the copy
says "at this instant" or the figure is re-derived at arrival — but a number the game does not honour
is worse than no number. Multi-select needs an aggregation rule and a mixed-selection rule on top.

---

## 7. The moment production actually slows is silent, and the siege alarm fires once per match

**What the player experiences.** Enemy units reach your beachhead. You get one "Supply Route
contested!" call-out. You push them back to 80%; they return; you get nothing, ever again. Separately,
the instant your reinforcements actually *start arriving slower* passes in complete silence.

**Mechanism.** Two independent one-file changes.

`wasContested` has exactly one reset in the file — `SupplyRouteContestation.cs:499-500`,
`if (controlBar >= info.BarMax) wasContested = false;` — inside the branch that only runs on **full**
recovery. A bar oscillating between 40% and 95% for twenty minutes never re-arms the warning. (Set at
`:429`; declared `:198`. Those four lines are every occurrence.)

`SlowdownThreshold` (`:75`, YAML `structures.yaml:314`, value `50`) has exactly three code consumers,
and **all three are silent**: `:867` and `:871` inside `GetProductionSpeedModifier`, and `:890` inside
`ISelectionBar.GetColor`. Nothing notifies on the crossing.

**Citation that proves it does not exist.** The five-hit repo-wide resolution of `SlowdownThreshold`
above is the proof: declaration, YAML value, two arithmetic consumers, one colour consumer, plus one
prose mention in a comment at `:439`. **No notification, sound, radar ping or widget references it.**
The rate-limit machinery to hang it on is already in the file (`NotifyInterval`, `:104`).

**Size.** One file, no new trait, no YAML schema change, no RNG. Hours.

**The single thing most likely to make it more expensive than it looks.** The hysteresis band for the
re-arm cannot be chosen by reading — an alarm that re-arms too eagerly during a long grinding siege
becomes nagging, and the 30 s rate limit already in the file exists because somebody worried about
exactly that. **If only one half ships, ship the threshold call-out**, which needs no tuning value at
all.

---

## 8. Neutralising an enemy building announces it to the Neutral player and to nobody else

**What the player experiences.** Your rifleman spends a full minute inside an enemy AA gun and turns
it grey. No voice line, no text, no sound. On the other side of the map, the player who just lost it
is told nothing — their defence stops working and they find out by noticing.

**Mechanism.** `CaptureActor.cs:134` computes
`var newOwner = captures.Info.CaptureToNeutral ? w.WorldActor.Owner : self.Owner;` and passes it to
`OnCapture` at `:146`. `CaptureNotification` then addresses that owner:
`Game.Sound.PlayNotification(..., newOwner, "Speech", info.Notification, faction)` and
`TextNotificationsManager.AddTransientLine(newOwner, info.TextNotification)` (`:73-74`). For a
soldier's neutralise `newOwner` **is the Neutral player**. The victim's channel is the next two lines
(`:77-78`) and is empty by default — `LoseNotification = null` (`:35`).

**Citation that proves it does not exist.** The trait is applied with bare defaults:
`structures.yaml:54` is the whole declaration (`CaptureNotification:`, immediately followed by
`ShakeOnDeath:` at `:55`), and the only other declaration in the mod sets one field
(`vehicles.yaml:111-112`, `Notification: UnitStolen`). `en.ftl` has zero matches for `captur`.
`CaptureToNeutral: true` appears exactly once in the mod — `infantry.yaml:928` — so this is the
soldier path specifically, and the technician path (`CaptureToNeutral` false → `newOwner = self.Owner`)
works correctly. **That asymmetry is the tell that it is a bug, not a design.**

**Size.** Hours, plus two or three new `en.ftl` strings, which do not exist for anything in this
space yet.

**The single thing most likely to make it more expensive than it looks.** This is balance-adjacent,
not purely UI. `game-model.md` already records soldier-neutralisation as close to unanswerable against
a bot and tracks it as a live balance risk. **Making it audible will make players use it more.** That
is arguably the right outcome — an invisible dominant strategy is worse than a visible one — but it
should be flagged to the user rather than shipped quietly.

---

## 9. Infantry give no selection feedback at all

**What the player experiences.** You box-select six riflemen and nothing on screen changes. No
bracket, no highlight, no outline. The only way to know what you have is the command bar.

**Mechanism and citation.** `^Infantry` sets `SelectionDecorations: ShowNever: true`
(`infantry.yaml:55-56`), and `SelectionDecorationsBase.cs:109` is literally
`if (selected && !Info.ShowNever)`. **`ShowNever` occurs exactly once anywhere under `mods/`** — that
line — and its engine default is `false` (`SelectionDecorationsBase.cs:24`). Removing the two lines
turns brackets on.

**Size.** Two lines. Minutes to change; the cost is entirely in judging the result.

**The single thing most likely to make it more expensive than it looks.** `ShowNever` was almost
certainly set on purpose, and brackets on a dense infantry blob may read as noise — that is a visual
judgement nobody can make by reading, and `Selectable.Bounds` on the same actor
(`infantry.yaml:58-60`) is `500,700,65,-128`, which is not an obviously bracket-friendly box.
**Treat this as "show the user two screenshots", not "delete the line."** It is a
`DOCS/recipes/SCREENSHOT.md` task, and this pass could not run one.

---

## 10. The production tooltip never says what a unit costs to *own*

**What the player experiences.** One row is missing: **"Upkeep — $12 / interval"**. Today upkeep is
discoverable only by hovering the cash counter, which requires already owning the units.

**Mechanism.** Every infantryman and vehicle carries `InfersUpkeep: PermilleCost: 5`
(`vehicles.yaml:144-145`, `infantry.yaml:154-155`) — 0.5% of unit cost (`InfersUpkeep.cs:47`),
charged on the same 50-tick line as income (`PlayerResources.cs:209`). Against a passive income of
100 that sets an army-value ceiling around 20,000, which is exactly `DefaultCash`
(`PlayerResources.cs:32`) — an elegant piece of design the game never states.

**Citation that proves it does not exist.** `InfersUpkeepInfo` is a plain `TraitInfo`
(`InfersUpkeep.cs:18`) implementing no tooltip interface. The full set of
`IProvideTooltipDescription` implementors is seven files — `Health`, `Valued`, `Cargo`, `Mobile`,
`Armor`, `AmmoPool`, `Air/Aircraft` — and `InfersUpkeep` is not among them. The only `Upkeep` string
in `en.ftl` is `cashflow = Cash: { $cash }, Upkeep: -{ $upkeep }` at `:278`, the aggregate cash
counter.

**Size.** One interface implementation plus a one-row method. Hours.

**The single thing most likely to make it more expensive than it looks.** The ceiling arithmetic above
is **derived, not measured** — from YAML and the timestep. Do not put it in player-facing copy without
one match confirming it. Also: aircraft carry no `InfersUpkeep` at all (`PermilleCost` appears only in
`vehicles.yaml` and `infantry.yaml`), so a row that appears on most of the roster and silently vanishes
on helicopters will read as a bug unless it is handled deliberately.

---

# TIER 2 — AMBITIOUS SWINGS

Larger bets. Each says what makes it a bet. **None of these is a few lines of wiring, including the
ones whose diffs are small** — three of them have one-line diffs and change how the game plays, which
is exactly why they are here and not above.

---

## 1. Wake up the vehicle that stopped fighting

**Why this one first among the swings.** It has the best evidence-to-value ratio in the tier: every
link in the chain was read at HEAD, it fires in every match within minutes of first contact, it hits
**human** units as well as bots, and it is a defect rather than a design proposal — the disagreement
is about the fix, not about whether something is wrong.

**What the player experiences.** A damaged tank you send to attack drives over, points at the enemy,
and then sits there for the rest of the match. It will not shoot when repaired, will not react when
something drives past, and will not go for ammo even if a supply truck parks beside it. Only a fresh
order from you unsticks it.

**Mechanism — one root, four consequences.** WW3MOD wires its two most common unit states onto
armament *pause*, which every engine consumer treats as a blink you hold aim through. The widest of
them is `heavy-damage-attained`, i.e. below half health (`Health.cs:108-109`).

1. **The unit goes sensor-blind.** `AttackBase.GetMaximumRange()` skips paused armaments —
   `if (armament.IsTraitPaused) continue;` at `:596-597` — and returns `WDist.Zero` when all are
   paused. `AutoTarget` uses it as its scan radius whenever `ScanRadius` is unset:
   `Info.ScanRadius > 0 ? WDist.FromCells(Info.ScanRadius) : ab.GetMaximumRange()`, at
   `AutoTarget.cs:1114` and `:1177`. **No vehicle in the mod sets `ScanRadius`** — the only two
   template hits are `infantry.yaml:310` and `:2423`, plus four dev-map/scenario overrides.
2. **An attack order given to it never ends.** `AbandonWhenArmamentsPaused` defaults `false`
   (`AttackBase.cs:72`) and **exactly one actor in the mod opts in** — the medic,
   `infantry.yaml:2314`. Without it the order is accepted: the unit closes, aims, fires nothing, and
   never goes idle.
3. **So it never asks for resupply again.** `AmmoPool` declares
   `INotifyCreated, INotifyAttack, INotifyBecomingIdle, IResolveOrder, ISync` (`AmmoPool.cs:268`) —
   **no `ITick`**. A unit already idle never re-fires the becoming-idle transition.
4. **The one readout built for this cannot fire.** `WithHoldingFireDecoration` reads
   `AutoTarget.LastHeldFireTick`, stamped only inside the `targetsInRange` loop — which is empty when
   the scan radius is zero.

**Citation that proves it does not exist.** The four above are each a read line. The tightest single
one: `grep -rn "AbandonWhenArmamentsPaused" mods/` returns **exactly one line**, `infantry.yaml:2314`.
The `wt/paused-cursor` work that merged at `4bbd0fad` ("Merge wt/paused-cursor") fixed the **cursor**
only — it added `RefusesForPause`, consumed at `AttackBase.cs:860` and `:903` — and the doc comment
immediately above the first call site (`:853-859`) states that without the opt-in *"the order is then
accepted and the unit really does close and aim, so a refusal here would be the mirror lie."*

**What makes it a bet.** It is a live behavioural change to **every armed vehicle on both bot
profiles**, so `@stable` moves and the next benchmark baseline must be re-taken knowingly. Worse, the
system has a documented *accidental rescue*: autotargeting is currently the only thing that makes a
dry vehicle re-check resupply, so anything that changes the idle/non-idle rhythm can move behaviour in
a direction nobody predicted. **This must be measured, not reasoned** — it needs its own RED/GREEN
pair, and the RED is stageable without a launch decision from me: a tank at Heavy damage with a full
magazine (so the ammo guards cannot rescue it), ordered to attack, asserted with
`TestHarness.HoldsAttackActivity`.

**Size.** Three small independent changes; medium overall, dominated by measurement rather than code.

---

## 2. The enemy Supply Route promises a move order and a health bar, and honours neither

**What the player experiences.** The enemy Supply Route is the most obvious target on the map. You
select your whole armoured force and right-click it. The cursor says *move*. Your army drives across
the map, parks on it, and stands there being shot at, firing at nothing.

**Mechanism.** `structures.yaml:296-297` gives `SUPPLYROUTE` a `Targetable` whose **entire** type list
is `NoAutoTarget`, and `Armor: Type: Indestructable` at `:317-318`. No weapon in the mod lists
`NoAutoTarget` in `ValidTargets` — `grep -rn "NoAutoTarget" mods/ww3mod/rules/weapons/` returns
**zero files**. So `ChooseArmamentsForTarget` finds nothing and `AttackBase.cs:845-846` refuses
(`if (!armaments.Any()) return false;`). With zero accepters,
`OrderFallbackMath.SelectionSuppressesRefusers` (`:106-109`) returns false, the retry re-resolves
against the terrain cell, and a **Move** is admitted. Because `GetCursor` runs through the same
resolver, the move cursor is drawn *before* the click.

Two details sharpen it. `structures.yaml:294-295` gives it `Health: HP: 75000` and it carries
`SelectionDecorations:` (`:231`), so it renders a permanent health bar advertising a destructibility
that does not exist. And it is the **only** actor in the mod whose target list is `NoAutoTarget`
alone — every other user pairs it with real types (`structures.yaml:143`,
`structures-defenses.yaml:58`, `civilian.yaml:443`, the husk files, `misc.yaml:418`).

**Citation, with an honest correction to the audit that filed this.** The filing claims *"Nothing
ever told you the building cannot be damaged."* **That is false and I am not carrying it.** The How To
Play panel says it in those words at `chrome/ingame-info-howtoplay.yaml:88-95`. The live defect is
narrower and still real: **the panel says one thing and the cursor promises the opposite at the moment
of the click.** Nothing in `PIPELINE.md` covers it — R12 is the supply cache, R9 is the panel's
contestation wording, item 17 is capture and is parked.

**What makes it a bet.** The cheap half — a blocked cursor, and suppressing a health bar nothing can
spend — only removes confusion. The valuable half is making the click *mean* something, e.g.
resolving an attack order on an enemy SR into an attack-move to its contestation ring, teaching *"you
surround this, you don't shell it."* That is a real design decision and it is **the same shape as the
sin `Passenger.cs:116-121` was reverted for** — silently reinterpreting one order as another. It has
to be visible (distinct cursor, distinct target line) or it repeats a mistake this project has already
made and ruled on once.

**Size.** Cheap half: hours. Real half: medium, and gated on a user ruling.

---

## 3. Evacuation goes to the nearest wall, not home

**What the player experiences.** A wrecked tank deep in enemy territory banks its refund in seconds
through their back edge, uninterceptable. A deep raid is therefore a free option: push in, do damage,
cash out whatever survives at the nearest wall.

**Mechanism — and the fix is one token.** The aircraft branch already does the right thing:
`RotateToEdge.cs:153-154` is `FindClosestSpawnAreaForOwner(self) ?? self.Owner.HomeLocation`. The
ground branch, twelve lines below, is `spawnAreaHintGround ?? self.Location` (`:165-166`). On nine of
ten maps `FindClosestSpawnAreaForOwner` returns null (only `river-zeta-ww3/map.yaml` contains any
`spawnarea` actor, verified by grep across `mods/ww3mod/maps/`), so a ground unit's exit resolves from
**its own position**. The `CanReach` pathfinder guard already exists at `:175-180`.

**Citation that proves it does not exist.** The four-line ground branch quoted above is the whole edge
choice. There is no owner-side term, no interception hook, and no `evacuating`-gated targetability
change. Not in `PIPELINE.md`. `RELEASE_V1.md:56` is adjacent and scoped to the last few tiles past the
boundary — a different thing that composes with this rather than containing it.

**What makes it a bet.** It is **a balance change wearing a bugfix's clothes.** `RotateToEdge` is the
shared path for the manual Evacuate order, the evacuate-when-dry stance, `DropsSupplyCache`'s empty
truck return, `VehicleCrew` and `EvacuateWhenUnrearmable` — so it moves both bot profiles by
construction and must be called out in the commit message. And a unit that cannot path home falls back
to today's behaviour, which is fine but must be a documented decision rather than an accident.

**Size.** Small diff, medium work — the cost is measurement and balance review.

---

## 4. Contestation should push the beachhead back

**What the player experiences.** Enemy units grind your Supply Route. Instead of only arriving *more
slowly*, your reinforcements start arriving *in the wrong place* — the drop point slides down the map
edge, then to a different edge, and every unit has a longer, more exposed walk. Push them off and it
walks home.

**Mechanism.** Both traits sit on the same actor, so no world scan is needed. The edge choice funnels
through one variable — `ProductionFromMapEdge.cs:100` (aircraft) and `:118` (ground),
`var searchOrigin = spawnAreaHint ?? self.Location;` — and candidates are already enumerated
(`:101`, `:122`, `GetSpawnCandidatesOnSameEdge`). A contestation-scaled offset on `searchOrigin`, or a
biased index into `candidates`, is the whole change, behind an `Info` field defaulting to zero
displacement so `@stable` and every existing map are byte-identical until it is turned on.

**Citation that proves it does not exist.**
`grep -c "SupplyRouteContestation" engine/OpenRA.Mods.Common/Traits/ProductionFromMapEdge.cs` returns
**0** — the two traits do not reference each other in either direction. No contestation, health or
player-state term enters the edge choice.

**And it is now cheaply measurable**, which was not true when the design recon was written: the merge
at `9b687fef` added `tools/autotest/scenarios/test-sr-entry-cell`, which pins the entry cell
numerically off `Trigger.OnProduction` (`ProductionFromMapEdge` raises `UnitProduced` at `:200`).
A displacement change has a ready-made regression pin. **Do not measure it by polling `Actor.Location`
— that leads a moving unit by one cell**, which is what the same merge's DISCOVERIES entry records
costing a run.

**What makes it a bet.** It **stacks two penalties on the losing player** — slower production *and*
longer walks — which can turn a bad position into an unrecoverable one and make comebacks worse, the
opposite of what a graduated design is for. The honest version probably *replaces* part of the
production slowdown rather than adding to it, and that is a balance decision. Second: on a small map
or an SR near a corner the displaced entry point may have nowhere to go, so the effect is inconsistent
per map.

**Size.** Medium. Small blast radius for new gameplay: two traits on one actor, one default-inert
field, no RNG, no new actor, no UI.

---

## 5. A player-facing channel for "your shot did nothing"

**What the player experiences.** A shot that connects and accomplishes nothing looks identical to a
shot that hurt. There is no health bar and the only health indicator is a four-band pip, so against a
high-HP vehicle dozens of consecutive hits change nothing visible. A player firing the wrong weapon at
the wrong armour gets no signal at all.

**Mechanism — the detector already runs in every shipped build.** `DamageWarhead.InflictDamage`
computes `effectiveThickness = thickness * armorPercent / 100` (`:249`), applies penetration (`:250`),
and then runs an anomaly gate on **every warhead application in the game**:
`if (effectiveThickness > 0 && HitCheck.LostMostOfItsDamage(damageBeforeArmour, damage))` (`:269`).
When it fires loud it already builds `$"ARMOUR {damageBeforeArmour}->{damage}"` (`:288`) and drops a
`FloatingText` on the victim — **gated on `debugVis.DamageNumbers`** (`:286`), which is a developer
checkbox defaulting `false` (`engine/OpenRA.Game/Traits/World/DebugVisualizations.cs:54`).

**Citation that proves the player cannot see it.** That `:286` gate is the proof, together with
`DebugVisualizations.cs:54`. The work is a routing change and a design decision, not a build.

**What makes it a bet.** Three things, and the first is a hard constraint. **Do not do this by turning
`DamageNumbers` on** — that default is guarded by a test and turning it on was ruled a release blocker
(former PIPELINE R17). This must be a separate, player-shaped surface. Second, the armour path is only
*one* reason a shot does nothing; `Versus` is the other and is applied outside the anomaly gate, so a
readout that explains only armour will mislead. Third, victim-side modifiers (garrison cover,
veterancy, prone) are applied later in `Health.InflictDamage` and are not reachable from the warhead
at all — so the channel can never be a complete explanation, only a true partial one.

**Size.** Medium, dominated by the design question of what the player sees and how often.

---

## 6. Enemy aircraft cannot contest a Supply Route; friendly aircraft defend one

**What the player experiences.** You park a gunship over the enemy beachhead. The bar does not move —
and the panel that told you to *"park units inside the ring"* (`ingame-info-howtoplay.yaml:116`) gave
no hint your most mobile unit is exempt. Meanwhile a friendly gunship hovering over *your* SR counts
its full purchase price as defensive value and triples your recovery.

**Mechanism.** `SupplyRouteContestation.IsRelevantActor` (`:243-263`) applies **two different tests**:

```csharp
if (rel == PlayerRelationship.Enemy)
{
    var pc = a.Info.TraitInfoOrDefault<ProximityCaptorInfo>();
    return pc != null && pc.Types.Overlaps(info.CaptorTypes);   // :252-253
}

if (rel == PlayerRelationship.Ally)
{
    var valued = a.Info.TraitInfoOrDefault<ValuedInfo>();
    return valued != null && valued.Cost > 0;                   // :258-259
}
```

**Citation that proves the asymmetry is live.** `CaptorTypes` defaults to
`{Player, Vehicle, Tank, Infantry}` (`:33`) and is **not overridden on `SUPPLYROUTE`** — the trait
block at `structures.yaml:303-316` contains no `CaptorTypes`, and the only occurrence anywhere in
`mods/` is `misc.yaml:442`, on a different actor. Every aircraft resolves to `Types: Plane` via
`^NeutralAirborne` (`aircraft.yaml:76-77`), inherited by `^Airborne` (`:101`) and thence by both
`^Aircraft` and `^Helicopter`. So an **enemy** aircraft fails the overlap test; an **allied** one never
takes that branch and passes on cost alone.

**Overlap, stated:** `WORKSPACE/audit/260816-systems-completeness.md:448` already carries *"[POLISH]
Aircraft, helicopters and ships cannot contest"*. **What is new is the ally/enemy asymmetry**, which
that entry does not mention — it treats aircraft as absent from contestation when they are in fact
present and one-sided.

**What makes it a bet, and it is the clearest case in this document of a cheap diff that is not a safe
win.** The fix is one line either way and **the two directions point in opposite balance
directions.** Adding `Plane` to `CaptorTypes` makes helicopters a cheap, hard-to-answer siege tool
against a beachhead that may have no AA. Excluding allied air from defensive value is the conservative
move. Whichever is chosen, it changes siege play at the mod's central mechanic. **This is a user call,
not a manager call, and it should be presented as a question rather than a diff.**

**Size.** One line of code; the design call is the whole cost.

---

## 7. Let a specialist stand off at its long weapon's range

**What the player experiences.** Your Bradley has anti-tank missiles that reach a long way. Ordered at
a tank, it drives all the way in to autocannon range first — into the tank's own gun — and only then
starts shooting.

**Mechanism, in the engine's own words.** `AttackBase.EngageAtLongestArmamentRange` defaults `false`
(`AttackBase.cs:82`) and the doc comment at `:721-724` names the symptom in the player's words:

> *"a unit whose long-range weapon is the RIGHT weapon closes to its short-range weapon's band anyway,
> and the player sees it refuse the good weapon and drive at the target."*

The shipped default takes the **minimum** of every valid armament's range.

**Citation that proves it does not exist.** `grep -rn "EngageAtLongestArmamentRange" mods/` returns
**exactly one YAML hit** — `vehicles-russia.yaml:959`, on `tunguska` — plus one comment in
`weapons-ballistics.yaml:716`. Every other multi-armament actor in the game is on the default. The dry
case is already handled: the longest branch ignores paused armaments and falls back only when all are
paused (documented at `:726-730`), which is the trap that would otherwise strand a missile-less
Tunguska.

**What makes it a bet.** Seven YAML lines that are **a balance change on seven multi-role units**, two
of them AA platforms where standoff matters most. It makes those units meaningfully stronger. Per the
standing rule this wants `tools/combat-sim/` numbers **before**, not after, and it should reach the
user as a proposal rather than a merge.

**Size.** Trivial diff, medium work — the cost is entirely simulation and review.

---

## 8. The reserve remembers — veterans come back as veterans

**What the player experiences.** Your Abrams has three gold chevrons, twenty minutes of life, and no
ammo. You pull it out. Instead of vanishing for scrap it appears as a reserve unit — *"Abrams
(Veteran III)"* — cheaper than a fresh one, arriving with its chevrons and a full magazine. The verb
stops being a euphemism: today "rotate out" means *sell*.

**Why it is worth doing.** It closes the largest gap between what this game says it is and what it
does — `supply-route.md` calls the SR the place units *"muster after being deployed in from off-map
reserves"*, and there are no reserves. It also fixes a real economic hole: **veterancy is the only
thing in this economy that appreciates, and the refund arithmetic cannot see it**, so the correct play
with a veteran is never to rotate it.

**Citation that proves it does not exist.**
`grep -c "Experience\|Level\|Rank" engine/OpenRA.Mods.Common/Traits/CustomSellValue.cs` returns
**0**; the same grep on `Activities/RotateToEdge.cs` returns **0**. `GetSellValue`
(`CustomSellValue.cs:28`) reads only `CustomSellValueInfo.Value` or `ValuedInfo.Cost` minus missing
ammo and supply, and `RotateToEdge` ends in `self.Dispose()` with no ledger write on any branch.

**What makes it a bet.** Three things, and the second could kill it. (i) **Balance** — a reserve that
returns a veteran cheap and full is a *stronger* play than keeping it fighting, which inverts the
tension the mechanic is for; it needs a real cost, and that is tuning, not coding. (ii) **Sidebar
scope** — reserves need a UI surface that does not exist, and the same class of work
(*"Cargo Phase 3 — template sidebar"*) is already an open, unstarted thread. If that is hard, this is
hard for the same reason. (iii) `ProducibleWithLevel` is **prerequisite-gated, not order-gated**, so
it does not model "this unit at this rank"; either accept coarse rank tiers or write a new init path.
Do not assume the trait drops in.

**Size.** Large. The actor plumbing exists; the ledger, the surface and the price rule do not.

---

# Killed on verification — do not re-propose

- **A supply readout on the Logistics Centre.** Ships. `SupplyProvider` implements `ISelectionBar`
  (`SupplyProvider.cs:225`), returns `currentSupply / TotalSupply` (`:1231-1237`), sets
  `DisplayWhenEmpty => true` (`:1239`) and colours red on unusable residue (`:1243`);
  `LOGISTICSCENTER` carries the trait (`structures.yaml:469`). What survives of the economy audit's
  finding 8 is only a notification on the partial-refill exit — `Rearmable.cs:103-106` treats an
  unaffordable pool as done, and `grep -c "PlayNotification\|AddTransientLine"` returns **0** for both
  `Rearmable.cs` and `SupplyProvider.cs`. That is a much thinner item than filed and did not earn a
  place above.
- **"The game never tells you the Supply Route is indestructible."** It does, at
  `ingame-info-howtoplay.yaml:88-95`. See swing 2 for the narrower defect that survives.
- **"The detection margin is computed and then discarded."** It is never computed;
  `WithSpottedDecoration` short-circuits at `:115-116` and `:153`. Gated territory anyway.
- **A concealment readout for the player's own unit.** Ships — `^DetectableRangeCircles` draws a grey
  ring at the detection radius for the selected unit, driven by the `visibility-N` condition granted
  at `Detectable.cs:228`.

# Corrections to the source material, filed rather than acted on

- **PIPELINE item 67, "No clamp exists"** — a clamp exists (`Detectable.cs:118-125`); it is not the
  one the user asked for, and the code names what the real one would cost (`:111-112`). Item 67 is
  larger than its title.
- **`RallyPointIndicator` is in `engine/OpenRA.Mods.Common/Effects/`, not `Traits/Render/`.** The
  edge-node claim itself holds: `:94` inserts the *building exit* as node 0, so the segment the unit
  actually walks — map edge to SR — is the one segment never drawn.
- **`CashTricklerInfo.Interval = 60` (`CashTrickler.cs:26`) has zero readers** — it is the only
  occurrence of `Interval` in the file. Income pays on `PassiveIncomeInterval` (50). Anyone sizing the
  capture economy off the field value is 20% low. Worth a comment or a deletion; too small to be an
  item.
- **`PassiveIncome` has two different defaults.** The lobby option registers with
  `PassiveIncome.ToString()` = 100 (`PlayerResources.cs:108-109`), but the read falls back to the
  literal `"0"` when the option is absent (`:167`) — unlike `startingcash`, which falls back to its
  `Info` value. In a normal lobby game income is 100; in any context without the option it is zero.
  Do not assume 100 in a scenario.
- **The `produced` condition is granted and consumed by nothing.**
  `GrantExternalConditionToProduced: Condition: produced` at `structures.yaml:368-369`; the only
  `RequiresCondition: produced` anywhere in `mods/` is a **commented** line at `vehicles.yaml:919`.
  A free, already-wired grant for "this unit is a fresh arrival", waiting for a consumer.
- **Two units disagree with themselves about armour.** `m109` is `Armor: Type: Light`
  (`vehicles-america.yaml:604-605`) with `TargetTypes: Ground, Vehicle, Medium` (`:608-609`);
  `giatsint` is the same (`vehicles-russia.yaml:429-430` vs `:433-434`). A rifleman cannot shoot them;
  an AT weapon that does reach them tears through as if unarmoured. **This is not a bug until someone
  decides which number is intended** — bring the units to the user as a question, then pin the ruling
  with a corpus test. Filed here rather than proposed because the ruling *is* the work.

---

# The one I am least confident about, and what would settle it

**Swing 3 — evacuation goes to the nearest wall, not home.**

What I verified is solid: `RotateToEdge.cs:165-166` really does resolve a ground unit's exit from
`self.Location`, the aircraft branch twelve lines above really does use `self.Owner.HomeLocation`, and
nine of ten shipped maps really have no `spawnarea` actor to override either. Those are reads, not
relays.

**What I did not verify is whether it matters.** The proposal's whole value rests on an unmeasured
geometric assumption — that a unit which has pushed into the enemy half is meaningfully *closer* to
the enemy's back edge than to its own, often enough and by enough margin to make evacuation a free
option. On a map whose spawns sit near opposite edges that is obviously true; on a map with a long
neutral middle, or with the fighting concentrated around central objectives, it may almost never bind.
I have not watched it happen and I did not read the ten maps' geometry. If the premise is weak, the
proposal is a balance change to a path shared by five callers and both bot profiles, bought for
nothing.

**What would settle it, cheapest first.**

1. **Static, no launch, and I could have done it with more time:** for each of the ten shipped maps,
   take the spawn points and the map bounds and compute, over a grid of plausible engagement cells,
   whether the nearest edge is the owner's or the enemy's. That is arithmetic on `map.yaml` and
   answers the premise directly, per map, with no game running.
2. **If a launch slot is going spare:** place one own-player unit in the far corner of
   `twin-rivers-ww3` (spawns `112,92` / `112,28`, zero `spawnarea`), issue `Evacuate`, and log the
   chosen edge cell. **The answer that counts:** whether the chosen cell's edge is the one nearest the
   *unit* or the one nearest `self.Owner.HomeLocation`. Read `result.json` from the run directory —
   not piped through `tail`. Latch the cell from a notification hook, **not** by polling
   `Actor.Location`, which leads a moving unit by one cell and has already destroyed one run's answer
   this week.

If (1) shows the nearest edge is usually the owner's own, this swing should be dropped rather than
rewritten.
