# The ambush, as the player plays it

**2026-08-20 — research only, no behaviour changed.** Written against `main` @ `4bb3fae9`.

This document is about what a player *does*, not what the engine *has*. Every engine claim below was read
in the source on this branch and carries a file:line; where I am proposing rather than reporting, the text
says PROPOSAL.

The design standard for everything here is the user's:

> *"I want some more automatic behaviour when it is helpful to the player. It should not feel invasive,
> but if we can figure out small things that soldiers can do on their own, that will feel like the
> soldier's 'instinct', then it is good."*

So the test applied to every automatic behaviour in this doc is one question:
**does the player read it as the soldier being smart, or as the game ignoring the order they gave?**

---

## 0. The thing I got wrong first, which changes the whole document

I opened this expecting to design an Ambush mechanic. **Ambush already ships.** It is a stance, with a
command-bar button and a hotkey, and a substantial amount of the user's forest-road scene is already
playable today.

- `UnitStance { HoldFire, Ambush, FireAtWill }` — `engine/OpenRA.Mods.Common/Traits/AutoTarget.cs:22`
- Button `STANCE_AMBUSH`, hotkey `StanceAmbush` — `mods/ww3mod/chrome/ingame-player.yaml:361-369`
- Its shipped tooltip already promises the scene: *"Units pre-aim at targets but hold fire until spotted /
  When one unit is spotted, nearby allies in Ambush all engage / Zero aim delay — turrets are already
  aimed when firing begins"*

But there is a second fact that matters more than the first, and it is the reason this document has a
sharp point rather than a shrug:

> **A human player does not get the good half of the ambush.**
>
> The widened ambush — Stage-2 halt-before-contact and the whole Stage-3 spring-trigger table — is behind
> the `enable-ambush-tactics` condition. That token is granted **per unit, only by `LaneAmbushBotModule`,
> only to units a bot posted.** `mods/ww3mod/rules/defaults.yaml:316-319` states it plainly: *"the halt
> branch [is] still dead … on humans"*. Case-01's scenario has to grant the token from Lua by hand
> (`test-case01-forest-ambush.lua:98`) to make the case work at all.

So when a human clicks Ambush today they run the **ungated stock path** (`AutoTarget.cs:759-774`):
pre-aim silently, hold fire, and spring — the whole nearby group at once via `TriggerNearbyAmbushAllies`
— the instant **the enemy spots one of them**, or the instant one of them **takes damage**
(`AutoTarget.cs:677`).

Read the user's scene again against that:

> *"as soon as the enemy detects any one of the hidden units, all units open fire with no aim delay"*

**That exact sentence is shipped, for humans, today.** The gap is the clause just before it — *"almost
right in the middle of them"* — because the trigger that makes proximity spring an ambush (`Overrun`,
`AmbushTactics.cs:126-129`) is on the gated side of the fence. A human's ambush waits to be *seen*. It
never decides that the enemy is close enough.

That single fact generates most of the interesting player experience below, including the worst moment in
the loop.

---

## 1. The forest road, click by click

The scene: a road through dense forest. Four riflemen. An enemy column will come up the road in a minute
or two.

### Setting it up — it is three actions, and can be two

**Action 1 — select the squad.** Drag-box, or double-click one rifleman to grab all of that type on
screen. Nothing ambush-specific.

**Action 2 — click Ambush.** The command-bar button, or the `StanceAmbush` hotkey. The tooltip
(`ingame-player.yaml:368`) also offers `Ctrl+Click` = set as this unit's default, and `Ctrl+Alt+Click` =
set as the *type* default, persisting. **A player who sets riflemen to default-Ambush once never presses
this again.** For that player the ambush is two actions, then one.

**Action 3 — right-click the treeline.** One plain move order at the trees. This is the part that is
better than it sounds, and it already shipped (pipeline item 21, merged `5c6cc1f0`): a group order from a
**human**, in **Ambush** stance, at non-Tight cohesion gets every formation slot **refined to the best
concealed cell within radius 3**, scored on `ForestGroundShadow`, deterministic and conflict-free.

The player does not pick cells. They point at the woods and the squad melts into it. In case-01's six
measured runs the seating refined **5/5 units every single run** with the defenders reading enemy-visible
`vis = 1` against `Detectable.Vision 3` — i.e. **undetectable**
(`WORKSPACE/cases/case-01-forest-ambush.md:54`).

And then, without being told, each soldier does the rest of the tradecraft himself. There is a whole
automatic concealment ledger already shipped in `mods/ww3mod/rules/ingame/infantry.yaml:707-732`:

| The soldier… | grants | concealment | automatic because |
|---|---|---|---|
| is near cover objects | `object-proximity 1 / 2 / >=3` | **+1 / +2 / +3** | proximity to the trees he was seated in |
| went prone | `prone` | **+1** | `ProneCondition: … \|\| !moving \|\| …` (`:294`) |
| dug in | `dugin` | **+1** | `ConditionWhenStill: dugin` (`:141`) |
| is moving | `moving` | **−1** | — |
| just fired | `firinganyweapon` | **−2**, for 12 ticks | `RevokeDelay: 12` (`:722-724`) |

So a rifleman who stops in dense trees stacks **+5** concealment on his own — proximity, prone, dug in —
and the player pressed nothing to get any of it. *"Dug in"* is the user's own phrase for this scene and it
turns out to be a real condition in the mod, granted simply by standing still.

**Three actions, and the soldiers did the tradecraft.** This is already the "instinct" standard being met,
more completely than anything else in the game. It is the strongest thing in the feature and nobody would
know it from playing, for reasons in §2.

### Waiting — what the player sees, which is more than I expected

The ambush may sit for two minutes. What stops the player re-checking it?

Two always-on world glyphs, neither requiring selection:

- **An amber `A` over every unit in Ambush stance.** `WithStanceDecoration@Fire`,
  `RequiresSelection: false` (`defaults.yaml:818-822`), colour `255,210,70`. The trait's own reasoning is
  exactly right: it draws *only non-default* stances, because *"a mark on every unit in FireAtWill would
  be a mark on every unit on the map, which tells the player nothing"*
  (`WithStanceDecoration.cs:24-27`).
- **A red `!` when that unit has been spotted.** `WithSpottedDecoration` (`defaults.yaml:812-816`), with a
  deliberate asymmetry rule: an enemy who can see you but whom *you* have not seen does **not** light the
  mark, because that would be a wallhack (`WithSpottedDecoration.cs:20-22`).

So the player's waiting loop is genuinely good: **amber A = armed and hidden. Red ! = blown.** They can
glance at the treeline from across the map and know. This is the answer to "how does he keep trusting it
is armed", and it is already built.

**The one thing missing is the middle term.** `A` means "in Ambush stance". It does not mean "concealed".
A squad standing in Ambush stance in an open field wears the same confident amber `A` as a squad buried
in six cells of pine. The player learns the difference by being killed. There is no shipped indicator of
*concealment* — only of stance and of having-been-spotted, which is the state *after* it has gone wrong.
(`wt/conceal-gauge` is unmerged and out of scope here; noting the gap, not the fix.)

### Springing — and the moment the feature currently fails

Enemy column enters the trees. Two ways this goes.

**The good one.** A scout drifts close enough to spot a rifleman. `isSpotted` flips,
`TriggerNearbyAmbushAllies` fires the whole group on the same tick, and because they have been pre-aiming
the entire time (`PreAimAtTarget`, `AutoTarget.cs:753`) there is **no turret-turn delay** — the alpha
strike lands as one volley. This is the user's scene, and it works.

**The bad one, and it is not rare.** The column keeps good spacing and nobody's vision cone happens to
clip a prone rifleman in deep shadow. The column walks all the way through the kill zone and out the far
side. **The ambush never fires.** The player watches four soldiers, wearing a confident amber `A`, let an
entire enemy column stroll past them.

That is not a bug in the sense of a crash. It is the trigger table working as designed — for a human,
detection and damage are the *only* triggers. But as an experience it is the worst thing the feature can
do, because it is indistinguishable from the ambush being broken, and the player's response will be to
stop trusting the stance. **`Overrun` — spring when an enemy breaches minimum range — exists, is written,
is NUnit-pinnable, and is switched off for humans.** Case A in §5 is aimed squarely at this.

There is also no notification when an ambush springs. No sound, no ping, no camera jump — I grepped and
found nothing on the ambush path. My view: **a silent spring is correct** for the fire itself (the volley
is loud and visible, and a klaxon every time a squad shoots would be noise), but the player should be
told when an ambush squad **starts taking losses**, because that squad is expensive in a way §4 explains.
The generic `ActorLostNotification` already covers unit death; nothing distinguishes "an ambusher died"
from "a rifleman somewhere died".

---

## 2. Stance, or armed position? — and the honest problem with stopping

**Recommendation: keep it a stance. Do not build covered arcs.**

The two are different games. An armed position is a *plan* — you draw the arc, you own the geometry, and
you are rewarded for having thought about the approach. A stance is a *policy* — you set it and it
travels with the unit. Combat Mission can afford the plan because the player has one company and no
economy. **This player is running a front and a budget from a single fixed beachhead.** A mechanic that
demands a per-position drawing gesture will get used twice in the tutorial and then never again, because
in the minute it takes to draw arcs the player has not looked at their supply route.

The stance is also already doing the positional work *without asking*: item-21 seating means the player
points at the woods and the geometry solves itself. That is the arc, chosen by the soldiers. **The right
move is to keep giving the arc away for free, not to hand the player a pencil.**

### Stop-and-resume: the user's staging choice is right, but "resume" does not exist yet

The user's ruling: ship stop-and-resume as the default, add reroute later. *"I think that is best."* I
agree with the staging. But the report has to be honest about the starting line:

**Today the halt is a cancel, not a pause, and nothing resumes.**

- The only caller of `ShouldHaltBeforeContact` is `AttackMoveActivity.cs:160`. On a halt it sets
  `haltedForAmbush = true` and calls **`ChildActivity?.Cancel(self)`** (`:169`). The comment at `:33-35`
  is explicit: drain the cancelling child, *"the attack-move completes and the unit drops to idle."*
- `OriginalDestination` **is** cached (`:44`, `:55-59`) — but the comment says it is *"for reliable group
  scatter extraction"*, and no code path re-issues it. After the spring, `AmbushTickIdle` pre-aims at new
  targets or clears them; it never restores a move.

So the order is not paused. It is **gone**. Building "resume" is building it, not enabling it.

**Now the experience question: does a unit stopping on its own read as instinct or as disobedience?**

There is one shipped decision that already answers most of this, and it is a good one. From
`AttackMoveActivity.cs:154`:

> *"Plain player Move never enters this activity (it is a bare Move) … a plain Move is always obeyed;
> only attack-move / bot auto-move can halt."*

**That boundary is exactly right and should be treated as load-bearing.** Attack-move already means
"advance, and fight what you meet" — a player issuing it has *already delegated* the decision to stop and
shoot. A soldier who halts under attack-move is doing the job the order describes. A soldier who halts
under a plain move is refusing an instruction. Keeping the halt strictly on the attack-move side is the
difference between the two readings, and it costs nothing because it is how the code already sits.

Two cheap things would carry it the rest of the way, both PROPOSALS:

1. **Make the halt a pause, not a cancel — keep the order alive.** This is the actual work of "resume".
   The unit should still own its destination, so that when the engagement ends it simply continues. The
   player's mental model is "they stopped to deal with something"; a cancelled order makes that model
   false.
2. **Keep drawing the waypoint line to the original destination while halted.** This is the whole trust
   problem in one pixel. A stopped unit with a line still running to where it was going reads as *paused*.
   A stopped unit with no line reads as *broken*. Today there is no line because there is no order left to
   draw — which is precisely why the halt would feel like disobedience if it were switched on for humans
   as-is.

And one boundary worth stating in the tooltip rather than leaving players to discover: **if you want the
squad to go there no matter what, plain-move them.** That sentence turns the halt from a surprise into a
tool.

---

## 3. Afterwards: the squad that leaves its own ambush

The user's third phase: on the spring, only soldiers with a shot fire; the rest switch to a temporary
**hunt** behaviour for the duration of the engagement, keeping their stance.

The plumbing for this is unusually clean, which is worth saying first. `Hunt` is **already the second
stance axis** — `EngagementStance { HoldPosition, Defensive, Hunt }` (`AutoTarget.cs:24`) — with its own
independent decoration instance (`WithStanceDecoration@Engagement`, `defaults.yaml:823-827`). So "keeping
the same stance" is literally true: the Fire axis stays `Ambush` and shows its amber `A`, while the
Engagement axis moves to `Hunt`. No new concept, no new glyph vocabulary, and the player can see both.

**But I think free Hunt is the wrong behaviour, and case-01 has already measured why.**

The defenders in case-01 lose **nothing** — 0 casualties across all six seeds, while attackers lose
200-500cr. The case's own analysis is blunt about the cause:

> *"The defender edge is a **detection asymmetry** … NOT a fair-fight combat edge: a discarded
> COMPACT-clearing variant that let attackers detect defenders at ~5c had defenders **lose** on 2 of 3
> seeds (ratio 0.33 / 0.50) — `DensityModifiesDamage` (≤20%) + first-strike do not win a symmetric close
> brawl."* (`case-01-forest-ambush.md:66`)

That is the argument. **The ambush does not win because the soldiers are good. It wins because they are
in the trees and cannot be shot back at.** A soldier who leaves the treeline to hunt is walking out of the
only advantage he has, into the exact fight the project has already measured him losing. Phase three as
literally specified converts a 0-loss engagement into a coin-flip.

The concealment ledger from §1 sharpens this into arithmetic, and it is arithmetic the *worst* possible
moment. Springing the ambush already costs each firing soldier **−2** for 12 ticks
(`DetectableAddativeModifier@Firing`); a soldier who then leaves his cell pays **−1 more** for moving and
gives up **as much as +3** of proximity cover and **+1** of prone the instant he stands. So the hunt phase
asks soldiers to shed up to five points of concealment **in the exact window where they have just
announced their position by firing.** The mechanics already encode the doctrine — after you shoot, you are
briefly visible, so that is precisely when you do *not* walk into the road.

And it fails the instinct test on the player's side too. The player spent three actions and a minute of
patience building a position. The reward for a successful ambush should not be that **the position
dissolves itself**. If the squad wanders off after one engagement, the player learns that an ambush is a
one-shot consumable rather than a piece of terrain they hold — and the next time, they will babysit it
manually, which is the outcome every automatic behaviour in this document is trying to avoid.

**PROPOSAL — "lean out, don't leave."** Keep the third phase, keep it on the Engagement axis, but bound
it: a soldier with no shot may reposition **within concealment** — a short move to a cell that opens a
firing line while keeping density cover — rather than pursuing freely. Two properties make it read as
instinct: it is *bounded* (a couple of cells, never out of the woods), and it is *reversible* (when the
engagement ends, the squad is still an ambush, still seated, still wearing its amber `A`).

Two smaller rulings I would fold in, both PROPOSALS:

- **Don't spring alone.** A single surviving unhit ambusher, unseen, with the rest of the squad dead,
  should stay hidden rather than fire into a fight it will lose. Firing reveals it — that is the shipped
  **−2** `@Firing` modifier, plus `RevealOnFire.cs` — and one rifleman's volley buys nothing.
- **Re-hide when it's over.** When no enemy remains in the kill zone, soldiers who moved settle back into
  the best concealed cell. This is the same item-21 seating logic that already runs on the initial order,
  re-run once. It is the cheapest possible version of "the ambush is still there tomorrow."

---

## 4. The real cost — why this is a defender's game

A stance with no downside is not a decision. Ambush has three real costs, and the third is the one the
mod's economy creates.

**1. It is the only stance that can decline a fight it should take.** Ambush holds fire while unseen. For
a human that means an enemy who never spots you and never shoots you is an enemy you never engage (§1).
Against artillery — which kills from outside detection range — the squad's ungated triggers are detection
and damage, so the first thing that springs the ambush is **the shell landing**. Ambush is a stance that
lets the other player choose whether the fight happens.

**2. Idle capital, and here that is the whole army.** There are no factories
(`DOCS/reference/game-model.md:5-14`). Units are budget allocation called in from off-map reserves, and
crucially **"rotating out a unit = sending it back to the map edge to recover its budget cost"** (`:13`).
So a squad in a treeline is not merely not-fighting; it is **capital that is neither earning ground nor
being converted back into budget.** Four riflemen in a wood for four minutes is a real line item.

**3. Losing them is permanent, and slow to undo.** *"A destroyed unit is a permanent loss of that
budget"* (`:14`), and replacements **spawn at the map edge and walk**, with *"inherent travel time"*
(`:25`). An ambush squad wiped in the woods is a hole in the front for as long as it takes a new squad to
cross the map.

**Which is exactly why the ambush is worth more here than in a base-building RTS**, and the conclusion is
not symmetric. Cost 3 is the *defender's* case for ambushing and the *attacker's* reason to fear it: in a
game where you cannot replace losses quickly, a mechanic that produces the case-01 result — **you lose
nothing, they lose 350cr** — is not a tactic, it is the most efficient trade in the game. A player who
learns this will ambush constantly.

So the balancing cost cannot be a nerf to the payoff; it has to be **cost 1**. The reason not to ambush
everywhere is that an ambush **does not take or hold anything**. It cannot contest an objective, it will
not stop a column that declines to look at it, and it surrenders the initiative by construction. Ambush
should be the best way in the game to make an attack expensive, and a bad way to win one.

**When not to use it:** when you need to *take* something; when you must hold a point the enemy will
simply walk around; when the enemy has artillery or air and will find you before you find them; when the
unit is worth more rotated out than hidden; and when you are the one attacking.

---

## 5. Two proposed cases

Both are **proposals for the user to approve**, not authored decisions. Both are phrased as bars a
scripted test could settle, both follow `WORKSPACE/cases/README.md` (setup-validity asserted *before* the
bar; aggregate clauses parsed over a batch, per-seed clauses in the Lua), and both are chosen because
**they can go red today** — case A almost certainly does.

### Case A (proposed) — "the column that walks through"

*The one that measures the hole in §1.*

**Intent.** A human-controlled squad in Ambush in a roadside treeline must not let an enemy column walk
through the kill zone unengaged.

**Setup.** Forest road; 4-5 defenders, human-owned, Ambush stance, seated by ONE group move at the
treeline (the shipped item-21 path). A scripted enemy column attack-moves down the road, through the kill
zone, to a waypoint beyond the far end. Deliberately tuned so the column's vision does **not** reliably
acquire the prone defenders — that is the condition under test, not an accident.

**Bar (provisional).** *In ≥5 of 6 seeds, the ambush springs — first defender shot fired — while at least
one column member is still inside the treeline span.* Setup validity first: seating refined N/N, column
actually entered the span.

**Why it discriminates.** With the human gate as it ships, detection is the only trigger and the
prediction is that this reads **RED**. Granting `enable-ambush-tactics` to the human squad — enabling
`Overrun` — should flip it GREEN. That makes the case a direct measurement of *"should humans get the
widened ambush?"*, which is the open question underneath this whole document.

### Case B (proposed) — "the ambush is still there afterwards"

*The one that adjudicates phase three before it ships.*

**Intent.** After springing and winning, the squad is still a concealed ambush — not a scattered patrol.

**Setup.** As case-01, plus a **second** wave scripted to arrive T seconds after the first is destroyed.

**Bar (provisional).** *Measured at the moment the second wave enters the kill zone: ≥4/5 survivors are
within the original seating footprint AND read enemy-visible `vis` below the detection threshold — and
the second wave is engaged from concealment.*

**Why it discriminates.** Free Hunt should read **RED** (the squad has left the trees, is visible, and
meets wave two in the open). "Lean out, don't leave" plus re-hide should read **GREEN**. It also guards
the case-01 result from regression: it fails loudly if a future change quietly converts the
detection-asymmetry win into a fair fight.

---

## 6. Other small instincts worth stealing

The user asked for more of these. Filtered by the same test — *smart soldier, or ignored order?* — and
ordered by confidence. All PROPOSALS.

1. **Face the likely approach.** A seated ambusher with no target idles facing the nearest road or open
   lane, not whichever way it happened to walk in. Pure presentation, zero sim cost, and it makes the
   pre-aim *visible*: the treeline looks like it is watching the road. Highest confidence of anything
   here — there is no order to contradict, because the player never specified a facing.
2. **Surface the concealment ledger.** This is the biggest free win in the document and it is not a
   behaviour at all — it is a disclosure. The soldier already goes prone, digs in, and banks up to **+5**
   for cover proximity, and already pays **−2** for firing and **−1** for moving
   (`mods/ww3mod/rules/ingame/infantry.yaml:707-732`). Every one of those is the exact "soldier's
   instinct" the user asked for, **already implemented**, and the player is told none of it. Nothing needs
   to be built here except the feedback — and until it exists, players will not believe the instincts are
   there, which is the same trust problem as §1's amber `A`.
3. **Don't be the one who blows it.** While the group is unseen, an individual soldier declines a marginal
   shot at a straggler far outside the kill zone. Reads as fire discipline. Note this needs a real
   threshold or it becomes "my unit refused to shoot", which is the failure mode — the safe version is
   narrow: decline only when the target is leaving anyway *and* the group has a better target inbound.
4. **Top off while dormant.** An ambusher with time on its hands should be at full ammo when the column
   arrives, not reloading during the alpha strike. Fits the resupply-stance machinery that already exists.
5. **Step off the dead ground.** After a fight, a soldier standing in a cell whose cover is gone (trees
   burned, squadmate's corpse blocking the lane) shuffles a cell to recover concealment. Bounded and
   reversible, same family as "lean out, don't leave".

The two I would not do: **auto-setting Ambush stance for the player** (that is the game overriding a
policy the player owns — straight fail on the test), and **auto-retreating a losing ambush** (retreat is a
decision with economy consequences under §4; a squad that walks itself home has spent the player's budget
for them).

---

## 7. What I did not verify

- **I ran nothing.** No game launch, no `check-yaml`, no test. Every claim is from reading source on
  `4bb3fae9`. The case-01 numbers are quoted from its status log, not re-measured.
- **The `Overrun` prediction in case A is inference**, not measurement. I traced that humans lack the gate
  and that `Overrun` sits behind it; I did **not** confirm by running that a column can in fact traverse
  a kill zone without spotting a prone defender. If enemy vision reliably clips the defenders anyway, case
  A is green on arrival and the hole in §1 is theoretical. That is the single most load-bearing unknown
  in this document and case A is designed to settle it.
- **I did not check every map's `rules.yaml` or Lua** for a grant of `enable-ambush-tactics` to human
  units. `defaults.yaml:316-319` and the shipped rules say humans do not get it; a specific map could
  differ. (`WORKSPACE/recon/260819-infantry-visibility-stances.md:658` flags the same unchecked corner.)
- **Click counts are read from the UI definitions**, not from playing. I have not confirmed that the
  item-21 seating triggers on the exact three-action sequence in §1 in a live game — case-01 exercises it
  through `Test.GroupMove`, which is the same `IModifyGroupOrder` pipeline, but that is a scripted order,
  not a human right-click.
- **The concealment ledger's arithmetic is read, not measured.** I confirmed each modifier and its sign
  in `infantry.yaml:707-732`, and the direction is corroborated by case-01's measured `vis = 1` against
  `Detectable.Vision 3`. But **I did not verify that the modifiers stack additively to +5 in practice** —
  `object-proximity` is a graded condition and the three `@InCover` instances are written as mutually
  exclusive bands (`== 1`, `== 2`, `>= 3`), so +3 is the cover ceiling, not a sum of the three. If
  something clamps the total, the "+5" in §1 and the "five points" in §3 are overstated, though the
  argument in both places survives at any positive value.
