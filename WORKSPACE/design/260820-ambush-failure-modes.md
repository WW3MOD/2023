# Ambush / Take Cover — adversarial failure-mode sweep

**Date:** 2026-08-20
**Base:** `main @ 4bb3fae9`, worktree `wt/ambush-failure-modes`
**Method:** docs only. No build, no launch, no YAML validator. Every code claim below is a read at that SHA.
**Bar applied:** the user's own — *"it is hard to detect sometimes so I am not sure what is actually active in game."*
A feature that works but reads as a bug has failed. Plus the user's 2026-08-20 ruling: automatic behaviour must
feel like the soldier's **instinct** and **"should not feel invasive"** — so *anything that reads as the game
overriding the player's order, rather than the soldier being smart, is high severity even when mechanically correct.*

---

## 0. The reframe: this is not a design review. Most of it already shipped.

The brief describes Ambush as a feature being designed. It is not. At `4bb3fae9`:

| Described as new | Actually shipped | Where |
|---|---|---|
| "Ambush is a behaviour stance" | `UnitStance { HoldFire, Ambush, FireAtWill }` | `AutoTarget.cs:22` |
| "group holds fire until one is detected, then all fire" | shipped, incl. the tooltip saying exactly that | `AutoTarget.cs:749-762`; `ingame-player.yaml:372-373` |
| "pre-acquired targets, no aim delay" | `PreAimAtTarget`, tooltip says "Zero aim delay" | `AutoTarget.cs:~752` |
| "stops when about to be spotted" | halt-before-contact, gated, **AI-reachable only via attack-move** | `AttackMoveActivity.cs:154-171` |
| "Take Cover order" | existed as a button, **deleted 2026-08-19 as inert** | `b62ee52f` |

Five ambush autotests already exist (`test-ambush-convoy`, `-detection`, `-enemy-stops`, `-fast-convoy`,
`test-case01-forest-ambush`). **So the failure modes below are not predictions about future code. Most are
descriptions of what the shipped build does today.** That makes them cheaper to confirm and more urgent.

### 0.1 Premise audit — three of the design's load-bearing premises are false at HEAD

**P1. "Stopping drops his visibility" — true for infantry, FALSE for everything else.**
`Detectable.CurrentVisibility` is "required observer strength to see me", higher = stealthier
(`Detectable.cs:24-25`). The `moving` −1 / `prone` +1 / `dugin` +1 modifiers live in
`^DetectableInfantryStandard` (`infantry.yaml:703-732`), inherited **only** by `^Infantry` (`:21`).
`vehicles.yaml:66` is a bare `Detectable:` — **zero modifiers**. Ambush on a tank changes its visibility
by nothing, ever, in any state.

**P2. "Ambush makes him stop in order to hide" — stopping already does that, regardless of stance.**
`ProneCondition = deployed || suppressed > 30 || !moving || critical-damage` (`infantry.yaml:294`; amphibious
variant adds `!inwater` at `:313`).
Prone is *just "stopped"*, no timer, no order, no stance. So the concealment benefit the design attributes
to Ambush is **already unconditional** — every unit in the game gets it. Ambush adds no detectability change
whatsoever: `stance-ambush` / `stance-holdfire` are granted (`defaults.yaml:309-310,570-571,670-671,682-683`)
and have **zero `RequiresCondition` consumers** anywhere.

**P3. "Best-protected cell, then least-visible cell" — that is one quantity sorted twice.**
`TerrainAffordanceLayer.CoverQuality` is a neighbour-density sum (`:9-10,141-144`); shadow concealment is a
density sum along the sightline; `DensityModifiesDamage` is a 3×3 density sum (`infantry.yaml:37-44`). All
three read `Map.DensityLayer`. **The second sort key will almost never discriminate** — and when it does,
it will look arbitrary. Worse, density is **baked at map load** (`Map.cs:976-1001`): dead tree husks still
conceal and still protect; script-placed trees never do.

> Consequence for the sweep: several scenarios below are not "the feature misbehaves" but "the feature cannot
> do what its own button claims". Those are ranked highest, because the player reads a false promise as a bug.

---

## 1. Ranked findings

Ranked by *will make the player think the game is broken*, not by implementation cost.
Severity: **S1** = reads as broken/invasive to a normal player in a normal game · **S2** = reads as broken in a
recognisable situation · **S3** = confusing or wasteful, survivable · **S4** = edge/correctness nit.

---

### F1 — The halt destroys the order. There is no resume. **S1**

The user narrowed the design to **stop-and-resume**. That is not what ships. `haltedForAmbush` is latched at
`AttackMoveActivity.cs:167` and **never cleared anywhere in the file** (only reads: `:36` decl, `:84` drain,
`:165-167` set). On halt, the activity cancels its move child, drains, and **ends**. The unit drops to idle.

**What the player sees:** he attack-moves a squad across the map. It stops in the middle of nowhere. It never
arrives, and it will never arrive — not when the scout leaves, not when the enemy dies, not ever. The move
order is gone. There is no glyph change, no speech, no text line, no marker at the abandoned destination.

This is the single most invasive thing in the feature: **the game silently deleted a player order.** By the
user's own 2026-08-20 bar this is automatically top severity, and it is also the exact shape of his standing
complaint — he cannot tell that anything happened, only that his units are in the wrong place.

*Mitigation, cheapest first:* (a) keep the destination and re-queue the move when the halt condition clears —
this is the stop-and-resume the user actually asked for, and it is the difference between "instinct" and
"disobedience"; (b) until then, the halt must announce itself (see F3); (c) cheapest stopgap of all — a
persistent waypoint marker at the abandoned destination, so the order visibly still exists.

---

### F2 — Sprung is terminal, and the glyph does not say so. **S1**

`ambushTriggered` is a one-way latch. The comment at `AutoTarget.cs:740-745` is explicit: *"SPRUNG is terminal
until stance reset … DO NOT clear ambushTriggered here"*. The **only** clearing path is `ResetAmbushState()`,
whose own doc-comment says it is called *"when stance changes away from Ambush"* (`:988-996`).

So after one engagement the unit is, functionally, FireAtWill — permanently. It will never hold fire again, never
ambush again, never re-arm. **And `WithStanceDecoration` draws the gold "A" off `autoTarget.Stance` alone**
(`WithStanceDecoration.cs:~105`) — which is still `Ambush`. Armed-and-waiting and sprung-forever render
identically.

**What the player sees:** a squad marked "A" that behaves like an ambush once, then never again. He re-uses the
same squad next fight and it opens fire at max range, blowing the ambush he thought he had set. The glyph told
him he was armed. He was not. To re-arm he must click a *different* stance and click back — and **nothing in the
game says that.**

This is the direct answer to *"I am not sure what is actually active"*: the indicator is not lying about the
stance, it is lying about the **state**, and state is what he wants to know.

*Mitigation:* distinguish armed from sprung in the glyph — dim/hollow "A" when sprung, solid when armed. Costs
one bool read on an already-live render-only trait. Considerably cheaper than any behavioural change, and it is
the highest information-per-byte fix on this list. Separately: decide whether re-arm should be automatic after
N ticks out of contact — but even if it stays manual, the glyph must stop claiming otherwise.

---

### F3 — No mechanism exists to tell the player why a unit stopped. **S1**

There is precedent for refusing an order **at issue time**: `Transforms.cs:155-159` fires
`NoTransformNotification` + `NoTransformTextNotification` and returns. There is **no precedent anywhere for
abandoning an in-flight order**, and the ambush halt does exactly that with total silence.

Available surfaces, all already in the mod and all unused by ambush:
`TextNotificationsManager.AddTransientLine` (`RallyPoint.cs:162`, `GainsExperience.cs:125`);
`TextNotification:` in yaml (`vehicles-america.yaml:472` — "Spotted: Abrams");
`Game.Sound.PlayNotification(… "Speech" …)`; decorations (`WithStanceDecoration`, `WithSpottedDecoration`).

**What the player sees:** nothing. Which is the failure. Every other finding in this document is made two grades
worse by F3, because in each case the player's only available theory is "the game is broken."

*Mitigation:* one transient line + one speech cue on halt ("Holding — enemy observation"). This is the cheapest
single change on the whole list and it partially rescues F1, F4, F5, F7 and F12 at once.

---

### F4 — Stop-and-resume flicker: the squad that never arrives. **S1**

Now that stop-and-resume is the chosen default, this becomes the defining risk. The halt predicate is
`GroupDetectedBy` (`AttackMoveActivity.cs:201-230`) — a boolean OR over `CanBeViewedByPlayer` for self and every
Ambush ally within `AmbushCoordinationRadius` (10 cells, `AutoTarget.cs:86`). Detection is **quantised**: the
observer-strength ladder steps in ~3-cell bands (`defaults.yaml:47-84`), and the `moving` modifier is worth a
full −1 tier (`infantry.yaml:730-732`).

That is a textbook oscillator. Moving ⇒ CV−1 ⇒ seen ⇒ stop. Stopped ⇒ prone ⇒ CV+1 ⇒ **and the moving penalty
also lifts**, so +2 tiers ⇒ unseen ⇒ resume ⇒ CV−2 ⇒ seen ⇒ stop. **The act of stopping changes the input that
decided to stop.** With no hysteresis the unit twitches on the band edge indefinitely, advancing a cell at a
time or not at all.

**What the player sees:** a squad juddering in place, animation flipping prone/upright, never arriving. Reads as
a pathfinding bug — the most "the game is broken" symptom in this document.

*Mitigation:* this needs **asymmetric thresholds plus a minimum dwell time**, not one threshold. Stop when seen;
resume only when unseen *by a margin* and *for N consecutive ticks*. The project has this exact fix already:
`StancePositioningExecutor`'s `WithinOneCell` hold tolerance vs 1-cell arrival tolerance (`:405`), which
commemorates `3471f7d3` — a ratchet caused by precisely this "decide tolerance ≠ hold tolerance" mismatch.
**Do not re-derive it; copy it.**

---

### F5 — Post-spring hunt has no exit condition, and the latch guarantees it never gets one. **S1**

*(This is the phase the user added on 2026-08-20: soldiers with no shot switch to hunt behaviour "while the
ambush is active, ie while we are still visible and fighting".)*

Three separate problems, compounding.

**(a) "While we are still visible and fighting" is not a state the code has.** "Fighting" has no representation;
the closest is `ambushTriggered`, which is **terminal** (F2). If hunt is scoped to "while sprung", hunt is
scoped to *forever*. The squad hunts for the rest of the match. `firinganyweapon` has a `RevokeDelay: 12`
(`infantry.yaml:722-726`) and is the only naturally-expiring "am I fighting" signal in range — but 12 ticks is
far too twitchy to gate a movement mode, and it would produce F4's oscillation in a second axis.

**(b) Hunt is a different axis the player set himself.** `EngagementStance { HoldPosition, Defensive, Hunt }`
(`AutoTarget.cs:24`) is an **orthogonal** stance with **its own button bar** (`ingame-player.yaml:403+`) and its
own glyph (`H` blue, `>` orange). A player who deliberately set **HoldPosition** and **Ambush** — a completely
reasonable "hold this treeline" combination — would, on spring, have his HoldPosition silently overridden by
hunt. The `H` glyph keeps saying HoldPosition while the unit walks away.

**This is the most invasive single behaviour in the proposal.** It is the game overriding an explicit,
separately-expressed player order, with an indicator actively contradicting what the unit is doing. By the
user's bar it is not a close call.

**(c) It walks the non-shooters out of the treeline.** The units with no shot are, in general, the ones the
terrain is hiding *best*. Hunt sends them toward the enemy — out of the density that was giving them cover
(`DensityModifiesDamage`, ≤20%) and concealment (shadow), into the open, where `moving` costs −1 CV. The ambush
position dissolves after one engagement and cannot be re-formed (F2). If the engagement ends while a hunter is
mid-advance, he is alone in enemy territory, still marked "A", still terminal-sprung.

*Mitigation:* (i) do not overload `EngagementStance` — if hunt-on-spring ships, it must not contradict a
player-set HoldPosition; treat an explicit HoldPosition as a veto. (ii) Bound it in **space** (a leash from the
ambush anchor — `StancePositioningExecutor.LeashRadius = 4` is the existing idiom) rather than in time, since
the time predicate does not exist. (iii) Require a real exit: no contact for N ticks ⇒ return to anchor.
Without (iii), (a) alone sinks it.

---

### F6 — An enemy walks past at point-blank and nobody shoots. **S1**

Correct behaviour. Reads as catastrophic. Hold-fire-until-spotted means an unspotted ambusher watches an enemy
squad walk through him at 1 cell. Trigger 5 (`AmbushOverrunFloor = 2` cells, `AutoTarget.cs:~137`) is the guard
against the literal worst case — but it is defined in terms of *weapon MinRange*, so a unit whose target is
inside the overrun floor springs, while one at 3 cells with a clear shot still holds.

**What the player sees:** riflemen with a rifle pointed at an enemy's back at knife range, doing nothing. This
is the classic "my units are ignoring the enemy" report, and it is indistinguishable from a broken AutoTarget.

*Mitigation:* mostly a communication problem, not a behaviour one — the pre-aim is already happening and is
*invisible*. Make pre-aim legible: turrets/torsos visibly tracking a target the unit is not shooting at reads as
discipline; the same unit with no visual tracking reads as a bug. Verify whether `PreAimAtTarget` produces a
visible facing change on infantry (uncertain — not verified, see §4).

---

### F7 — A held unit is the only thing defending something important. **S1**

An Ambush squad sitting on the Supply Route watches an enemy walk in, because nobody has spotted *them* yet.
Damage is a spring trigger (trigger 2, via `INotifyDamage.Damaged`), but **damage to the thing they are
guarding is not.** The Supply Route is indestructible and non-buildable — but it is contestable, and
`ContestationTextNotification: "Supply Route contested!"` (`structures.yaml:269`) will fire while the garrison
stands there holding fire.

**What the player sees:** the game tells him his Supply Route is contested, and his defenders — visibly present,
marked "A" — do nothing about it. Two of the game's own UI elements contradicting each other on screen.

*Mitigation:* add contestation/ally-damage-within-radius as a spring trigger. This is a genuine gap, not a
presentation problem.

---

### F8 — The ambush springs on a single scout, at the far edge, on nothing. **S2**

Trigger 1 is `isSpotted` — *any* enemy that can see *any* group member. One scout at max sight range detecting
one man at the group's edge springs the entire squad within `AmbushCoordinationRadius = 10` cells. Stage 3's
worthwhile-score machinery (`AmbushMinSpringThreshold = 100`, `AmbushHighSpringThreshold = 400`, hysteresis
samples) governs triggers 3/4/5 — but **detection is evaluated fresh every scan and is not score-gated**
(`AutoTarget.cs:~754`, "trigger 1 (detection), evaluated fresh every scan").

**What the player sees:** a carefully positioned 8-man ambush unloads at nothing, giving away the position and
— per F2 — permanently disarming itself. All of it caused by an enemy he may never have seen.

*Mitigation:* gate trigger 1 on the same worthwhile score as 3/4, or at minimum require the spotter to be
engageable by someone. Note the tension: gate it too hard and F6 gets worse.

---

### F9 — The "group" has no existence, so the player cannot see, form or trust it. **S2**

`Order.GroupedActors` exists (`Order.cs:64`) but `StanceSelectorLogic.cs:80-89` issues **one order per actor**.
There is no squad identity anywhere in the game. "Group" in ambush means *"any Ambush-stance unit of mine within
10 cells"*, recomputed per tick by `FindActorsInCircle`.

Consequences: (a) an unrelated Ambush unit wandering within 10 cells **silently joins** the ambush and can spring
it; (b) a squad the player thinks is one ambush is two, or vice versa, depending on spacing he cannot see;
(c) "half the squad has line of fire, half does not" is not a special case — it is the normal case, and the
half without a shot is exactly the population F5's hunt mode sends walking into the open.

*Mitigation:* no cheap fix for real groups. But the radius could be *shown* — a faint 10-cell ring on selected
Ambush units, reusing the `^DetectableRangeCircles` idiom (`infantry.yaml:22`, `Type: concealment`,
`Visible: WhenSelected`) that is already live. That turns an invisible rule into a visible one.

---

### F10 — Ambush disables the game's only automatic cover-seeking. The button does the opposite of its name. **S2**

`StancePositioningExecutor.FireStanceAllowsRepositioning(stance) => stance >= FireAtWill` (`:587-590`, gate at
`:318`). Ambush and HoldFire **opt out** of the automatic cover repositioning that every FireAtWill unit gets.
The opt-out is deliberate and commemorates the *un-ambush bug* (`174075e9`) — the executor used to walk a
human's hand-placed ambusher off its chosen cell.

So both branches are defensible and the net effect is still backwards from the player's model: **selecting the
stance called "Ambush" turns off "hide in cover".** `WORKSPACE/recon/260819-infantry-visibility-stances.md` §4.2
records the user hitting this personally.

**Direct consequence for Take Cover:** a Take Cover order that relocates an Ambush unit re-opens `174075e9` by
construction. The two features are in direct conflict at this line, and it must be settled deliberately —
probably as "an explicit player Take Cover order overrides the opt-out; automatic repositioning still does not."

---

### F11 — Take Cover was deleted three weeks ago as a dead button. Re-adding it is not free. **S2**

`b62ee52f` (2026-08-19) removed it: inert at three levels — no `Key:` in any of the nine hotkey files, no
`OnClick` in `CommandBarLogic`, and **no receiving trait**, because `82f0b8eb` renamed RA's `TakeCover` →
`InfantryStates` and made prone automatic (`ProneCondition`). There is no orderable cover trait to send to.

Also: `Container@STANCE_BAR` is Width 102 = exactly 3 × 34px with **zero free slots**
(`ingame-player.yaml:334-402`), and the bar backgrounds are **absolute X literals not parented to their
containers** — PITFALL at `:52-57`. Adding a button is the 12-widget hand-reflow costed in
`bugs/discovered.md` (`1492b225`) and executed in reverse by `b62ee52f`.

*Mitigation:* budget the reflow explicitly, and do not reuse the name "Take Cover" without knowing it was
deleted — a reviewer seeing it return will reasonably assume the revert was a mistake.

---

### F12 — Two soldiers stop, the rest walk on: the squad arrives piecemeal. **S2**

The halt is evaluated **per actor**. `GroupDetectedBy` ORs over the group, so in principle the group halts
together — but only members that are *in the attack-move activity* and *hold the gate* can halt at all, and the
`FindActorsInCircle` radius is measured from each actor separately. Units at the trailing edge of a strung-out
squad have a different 10-cell neighbourhood than the leaders.

**What the player sees:** a squad that splits. Two men freeze, six continue into contact, and the six die
without support. Because of F1 the two never rejoin. This is *worse* under stop-and-resume than under reroute,
which is exactly why the user's narrowing raises its priority.

*Mitigation:* halt the group, not the actor — one decision broadcast to members, rather than N correlated
decisions. Cohesion machinery already exists (`CohesionMoveModifier`) and is the natural home.

---

### F13 — Bots hold fire and lose engagements. **S3, bounded — verify before worrying.**

Both profiles run it: `LaneAmbushBotModule@experimental` (`ai.yaml:834`) **and** `@stable` (`:2218`). Stance is
set at `LaneAmbushBotModule.cs:496-497` and reset at `:519-520`.

But the exposure is structurally capped: `MaxAmbushes: 2` × `UnitsPerAmbush: 2` = **4 units**, with
`AmbushCommitmentTicks: 250` (`ai.yaml:837-842`). Bots cannot mass-hold-fire. Downgraded from the brief's
expectation on that basis.

Two real notes: the bot *does* reset the stance at `:519-520`, so **bots re-arm and humans do not** (F2) — the
AI has the affordance the player lacks. And any *new* stance would **not** be picked up automatically; the
modules name `UnitStance.Ambush` literally.

---

### F14 — Vehicles, aircraft, artillery in Ambush: the stance is a no-op that still shows a glyph. **S3**

Per P1, `Detectable` on vehicles carries no modifiers at all. Landed aircraft get `+3` (`aircraft.yaml:46-48`)
regardless of stance. So Ambush on a tank column: hold-fire and coordinated spring **do** apply (they are
`AutoTarget` behaviour), but every concealment justification for the stance is absent — and the tank still
draws the gold "A".

**What the player sees:** he ambushes with tanks, they hold fire, they are seen the whole time anyway, and they
lose the engagement they gave up first-strike for. The glyph promised something the unit cannot do.

*Mitigation:* decide whether the stance should be offered on non-infantry at all. If yes, the tooltip must stop
implying concealment. Cheap and worth doing.

---

### F15 — Transports and garrisons. **S3**

`TriggerNearbyAmbushAllies` explicitly reaches into garrisons (`gm.TriggerAmbush()`, `AutoTarget.cs:988-991`),
so garrisoned buildings participate. Passengers inside a transport are a gap I did not resolve: whether a
passenger's `AutoTarget` ticks, and whether a passenger counts toward `GroupDetectedBy`'s circle (it is at the
transport's position, and `IsInWorld` is false for cargo in most OpenRA paths — so probably excluded, **uncertain**).
Garrison cover is the single biggest damage modifier in the game (`DamageMultiplier@GarrisonCover: 20` — 80%
reduction, `infantry.yaml:190-192`), so garrisoned ambushes are strong and worth getting right.

---

### F16 — Take Cover: every soldier picks the same cell; the best cell is behind the enemy. **S3**

Given P3 (protection and concealment are the same density sum), a Take Cover order over a squad evaluates a
near-identical scoring function per man over an overlapping neighbourhood — so **convergence on one cell is the
default outcome**, not an edge case. And an unbounded search returns the map's best cover, which may be across
the map or through the enemy's vision to reach.

*Mitigation:* the existing answer is `StancePositioningExecutor.LeashRadius = 4` (Manhattan) plus per-unit slot
bidding (`test-cohesion-cover-bid` exists). Reuse both; do not write a fresh search.

---

### F17 — The cause of the halt evaporates: dead spotter, wreck, turned-away scout. **S3**

`CanBeViewedByPlayer` is a live query, so a dead spotter stops causing halts immediately — good. But two gaps:
there is no facing term (a scout "turning away" changes nothing, so the design's intuition does not hold), and
under F1 the resume never happens anyway. Note also `ResolvedVisibility == 1` means **explored, not observed**
(`MapLayers.cs:574-579`) — a naive margin-based "about to be spotted" predicate would trip on every explored
cell on the map. Anyone implementing the predictive form must handle that or the unit halts everywhere.

---

### F18 — Ally, civilian and aircraft triggers. **S4**

`GroupDetectedBy` derives `targetOwner` from the scanned target and treats **unknown owner as detected**
(`:207-210`, "never silently stall a march on an unattributable contact") — a good guard, already present.
Neutral/civilian actors that are not valid targets never become `target`, so they should not spring anything.
A passing aircraft that can see the group *does* satisfy trigger 1. Low impact; noted for completeness.

---

### F19 — Multiplayer, replay, spectator. **S4 for design, S1 if it desyncs.**

Stance is synced (`AutoTarget.cs:358-379`), Stage-3 tracking is deliberately **not** (`:398`), and
`ambushGateToken` is not (PITFALL at `:587` — a token is an allocation handle, not state). `LaneAmbushBotModule.cs:477-484`
carries a scar: *"ORDER THE GRANT, NEVER GRANT IT HERE… recording gatecount=1, replay gatecount=0."*

`PredictedStance` (`StanceSelectorLogic.cs:50,85`) is client-local while the glyph reads the synced value, so
button highlight and battlefield "A" disagree for one round trip — cosmetic, but it is one more reason the
player distrusts the indicator.

**Uncertain and load-bearing:** `"SetUnitStance"` classifies as `BotOrderClass.Passthrough`
(`OrderArbitrationMathTest.cs:77`), and an *unrecognised* order string appears to land in the same bucket
(`:80`). If so, a new Take Cover **repositioning** order would be admitted unthrottled — the exact churn class
that gate exists to damp. Verify against `OrderArbitrationMath.Classify` before wiring anything.

---

## 2. The three most likely to sink the feature

**1. F1 — the halt deletes the player's move order, permanently and silently.**
Not a tuning problem: the destination is not stored, the latch is never cleared, and nothing tells the player.
Every playtest where a squad fails to arrive produces a bug report, and the player's conclusion will be
"attack-move is broken", not "my ambushers are being clever". This is also the gap between what ships and what
the user actually chose on 2026-08-20 — he picked stop-and-**resume**; the code does stop-and-**abandon**. Fix
this first or the rest does not matter.

**2. F2 — sprung is terminal and the glyph does not distinguish armed from sprung.**
This *is* the user's complaint, mechanised. The one indicator that exists reports the stance he selected rather
than the state the unit is in, so it is at its most confident exactly when it is most wrong. Compounding: bots
re-arm (`LaneAmbushBotModule.cs:519-520`) and humans cannot, so the AI is quietly playing a better version of
the feature. The glyph fix is one bool read on an already-live render-only trait — the best ratio on this list.

**3. F5 — post-spring hunt, because its exit condition does not exist and it overrides a different button.**
"While we are still visible and fighting" has no representation in the code; the nearest latch is terminal, so
"hunt while active" resolves to "hunt forever". And hunt is a *separate orthogonal axis the player sets himself*
— springing would silently override an explicit HoldPosition while the blue `H` glyph continues to claim
otherwise. That is the definition of invasive under the user's own bar, and it arrives bundled with the
non-shooters walking out of the treeline that was concealing them. If this ships without a spatial leash and a
real exit condition, it will do more damage to the feature's reputation than the alpha strike buys back.

---

## 3. What the prior stance work already proves is hard

Five lessons this project has already paid for. Every one applies again here.

1. **Decide-tolerance ≠ hold-tolerance produces a ratchet.** `StancePositioningExecutor`'s `WithinOneCell` hold
   tolerance (`:405`) commemorates `3471f7d3`: units nudged every 30 ticks, blocked moves shoving peers past
   the leash edge, re-anchoring one step forward each time — a squad that **walked itself to the frontline**.
   F4 is the same bug with visibility as the coordinate.
2. **Cover-seeking scenarios fail silently by not running at all.** `test-stance-positioning`,
   `-anchor-move`, `-redirect-midadjust` were red for six weeks (`63a81fe0`,
   `WORKSPACE/DISCOVERIES.md:3224-3241`) because they set the unit-under-test to `HoldFire` as a convenience to
   silence combat — and `174075e9`'s fire-stance opt-out turned that into the off-switch. **The triage tell: a
   cover-seeking scenario that ends on the unit's own spawn cell did not choose badly, it never ran.** Silence
   the *enemy* (`Targetable: TargetTypes: NoAutoTarget`), never the unit under test. Now AUTOTEST Gotcha 7.
3. **`test-stance-optout` is a known false green and is still open** (`bugs/discovered.md:862-870`). It silences
   its units with HoldFire, so the fire-stance opt-out alone holds them; it would pass even if both opt-outs
   were broken. *"A positive scenario that stops testing goes red; a negative scenario that stops testing stays
   green forever."* Do not count it as coverage for F10.
4. **Stance state has desynced before, twice.** The four `AutoTarget` stance fields once had no `[Sync]` while
   consumers branched on them (`DISCOVERIES.md:1690-1721`; sync an `int` projection — the hasher rejects enums,
   `Sync.cs:72`). And `LaneAmbushBotModule.cs:477-484` records a gate-count divergence from granting a condition
   locally instead of ordering it. Any new latch — a resume flag, a hunt-mode flag — travels in the order stream
   or it desyncs.
5. **Per-type stance defaults bypass the order stream and are per-machine.** `unit-defaults.yaml`
   (`bugs/discovered.md:1160`, `DISCOVERIES.md:1431`), and `InitialStance` is silently ignored for non-playable
   actors, which read `InitialStanceAI` (`AutoTarget.cs:497`). Both have already eaten autotest runs
   (`81695c23`) — expect any ambush scenario to hit them.

Beyond the code, one prior document deserves to be read whole before anything is built:
`WORKSPACE/recon/260819-infantry-visibility-stances.md` (668 lines, merged `c34d3ad2`). Its §5 finding is the
sharpest sentence anyone has written about this area, and it is the reason the feature is being asked for:

> **"Every reaction to being spotted is a decision to shoot. Not one is a decision to hide."**

---

## 4. What I did not verify

Docs-only sweep, per the brief. Specifically **not** verified:

- **Nothing was run.** No build, no launch, no autotest, no YAML validator. Every behavioural claim is read from
  source at `4bb3fae9`, not observed.
- **Visibility distances are arithmetic over the YAML ladder**, not measurements. The only in-game datapoint in
  the repo is `6cb66e28`'s commit message.
- **F6's premise that pre-aim is visible on infantry is unverified** — I did not confirm `PreAimAtTarget`
  produces a facing change on an infantry sprite. If it does not, F6 is worse than ranked.
- **F15's transport-passenger case is unresolved** — I reasoned from `IsInWorld` rather than reading the cargo path.
- **F19's order-classification claim is explicitly uncertain** and flagged inline; it needs a read of
  `OrderArbitrationMath.Classify`.
- `^DetectableRangeCircles` went live in `fb56971b` with the commit stating **"NOT verified in game — never
  launched."** It may already answer part of the user's complaint, or be visibly wrong. Someone should look at
  it on screen before building a second indicator next to it.
