# RECON — the Supply Route as a strategic object: design space (260902)

**Read against `main @ 6a7e1839`**, worktree `wt/sr-design` at `/Users/fredrik/worktrees/ww3mod/sr-design`,
branched from the manager's checkout (0 commits behind `origin/main` at branch time).
**Read-only.** No game launched, no build, no validator run, no file changed outside this one.

Scope: what design space exists around the Supply Route that is **neither** capture-wiring
(PIPELINE item 17, parked) **nor** already queued. 7 proposals: 4 `SAFE WIN`, 3 `AMBITIOUS`.

---

## 0. Six facts that constrain everything below — read these before reading a proposal

Each of these corrected an assumption I started with. Two of them kill obvious-looking proposals
outright, which is why they lead.

**(1) The attacker ALREADY sees the defender's control bar — permanently, unscouted, from tick 0.**
This kills the naive "show the attacker a progress bar" idea before it is written.
`SUPPLYROUTE` carries `FrozenUnderFog: AlwaysVisibleRelationships: Ally, Neutral, Enemy`
(`structures.yaml:262-263`, a settled user ruling of 2026-08-27, per the comment at `:240-244`), so
`FrozenUnderFog.IsVisible` returns true immediately and `World.FogObscures` is permanently false for
an enemy SR. The bar's only gate is
`if (self.World.FogObscures(self) && !self.World.Selection.Contains(self))`
(`SelectionDecorationsBase.cs:62-68`) — **no ownership or relationship filter exists anywhere on
that path**, and `IAlwaysVisibleBar` flips the unselected case true whenever
`controlBar < BarMax` (`SupplyRouteContestation.cs:903`, consumed at `SelectionDecorationsBase.cs:94-101`).

**(2) But the attacker receives no notification of any kind.** Both notification entry points gate on
`if (self.Owner == localPlayer || localPlayer.IsAlliedWith(self.Owner))`
(`SupplyRouteContestation.cs:516` and `:541`), and both radar pings carry the predicate
`() => self.Owner.IsAlliedWith(self.World.RenderPlayer)` (`:523`, `:548`). The only ungated text is
the two `AddSystemLine` calls at `:580` and `:847` — which fire at **freeze** and **reclaim**, not
during contestation. So the attacker has a silent, unlabelled bar and a flashing building, and
nothing else.

**(3) The reinforcement entry edge is fixed to the SR's own cell for the entire match, and the
rally point cannot move it.** `ProductionFromMapEdge.Produce` computes
`var searchOrigin = spawnAreaHint ?? self.Location;` at `:100` (aircraft) and `:118` (ground) —
`self` is the SR. The rally point is read only for the destination waypoints (`:173-175`) and the
produced unit's initial facing (`:177`). **The player controls where the lane ENDS and never where
it BEGINS.** On 9 of 10 shipped maps there is no `spawnarea` at all
(`grep -rln spawnarea mods/ww3mod/maps/` → 1 file, `river-zeta-ww3`), so `searchOrigin` is simply
the SR's cell.

**(4) Only ground vehicles and military infantry can contest. Aircraft cannot — but friendly
aircraft DO defend.** This asymmetry is the sharpest single finding in this document and is
described in proposal **S4**.

**(5) The recovery asymmetry is real but is the inverse of the folk version.** Arithmetic from the
shipped values (`structures.yaml:305-313`, `BarMax = 100000` at `SupplyRouteContestation.cs:36`),
at 16.67 tps:

| state | rate | wall clock |
|---|---|---|
| draining at `ReferenceValue` (2500) surplus | `BarRate(100000, 1500)` = 66/tick (`:359-362`) | **~91 s** |
| recovering, **nobody** in your ring | `100000/3000` = 33/tick (`:474`, `:494`) | **~182 s** |
| recovering, **any** friendly surplus in your ring | ×3 = 99/tick (`:476`, `:496`) | **~61 s** |

So "90 s to break, 180 s to recover" is only true **if you walk away**. Garrison the ring with one
cheap soldier and recovery (61 s) is *faster than the drain*. Nothing communicates this.

**(6) The contestation alarm effectively fires once per match.** `wasContested` is set at `:429` and
reset at exactly one place — `:499-500`, `if (controlBar >= info.BarMax) wasContested = false`. A bar
that oscillates between 40 % and 95 % for twenty minutes never re-arms the warning.

---

# SAFE WINS

Ranked. **S1 is the one I would do first** — reasoning in §Recommendation.

---

## S1 — The siege alarm goes off once, and nothing marks the moment production actually slows

**What the player experiences.** Enemy units reach your beachhead. You get one "Supply Route
contested!" call-out. You push them back to 80 %; they return; you get nothing. Twenty minutes
later you are still fighting over it and the game has said one sentence about it. Separately, the
moment your reinforcements *actually start arriving slower* — the half-full bar — passes in total
silence: there is no line, no sound, and no sidebar change at the one instant the mechanic begins
costing you something.

**Why it is worth doing.** This is the mod's central objective and its only pressure mechanic. The
alarm currently behaves as if contestation were a one-shot event, when it is a sustained state that
ebbs and flows. And the threshold crossing is the single most decision-relevant instant in the whole
system — it is when "an enemy is nearby" becomes "I am being throttled".

**Mechanism.** Two independent changes, both inside `SupplyRouteContestation.cs`:
- Re-arm the latch on a hysteresis band rather than on full recovery. Today:
  `if (controlBar >= info.BarMax) wasContested = false;` (`:499-500`) — the only reset. Re-arming at,
  say, 90 % (or after N quiet ticks) makes a returning attacker announce itself.
- Add a distinct notification on the `SlowdownThreshold` crossing. `SlowdownThreshold` has **exactly
  three consumers repo-wide**, and all three are silent: `:867` and `:871` inside
  `GetProductionSpeedModifier`, and `:890` inside `ISelectionBar.GetColor`. The existing
  rate-limit machinery (`lastNotifyTime`, `NotifyInterval`, `:507-510`) is directly reusable.

**Tier.** `SAFE WIN` — contained to one file, no new trait, no YAML schema change, no RNG.

**Honest risk.** Nagging. An alarm that re-arms too eagerly during a long grinding siege becomes
noise, and the notification system is *already* rate-limited at 30 s real time (`NotifyInterval = 30000`,
`:104`) precisely because someone worried about this. The hysteresis band is a tuning value that
cannot be chosen by reading — it needs one live siege to feel right. There is also a real chance
that the threshold call-out is the whole value here and the re-arm is unnecessary; if only one ships,
ship the threshold one.

**Proof it does not already exist.** Repo-wide grep for `SlowdownThreshold` across `.cs`/`.yaml`
outside `WORKSPACE/` and `DOCS/` returns 5 hits: the declaration (`:75`), the YAML value
(`structures.yaml:314`), and the three silent consumers above (`:867`, `:871`, `:890`) — plus one
prose mention in a comment at `:439`. **No notification, sound, ping or widget references it.**
And `wasContested = false` appears exactly once in the file (`:500`).

---

## S2 — The attacker is told nothing, about a mechanic the tutorial tells them to go do

**What the player experiences.** The How To Play panel says *"Park units inside the enemy Supply
Route ring to contest it"* (`chrome/ingame-info-howtoplay.yaml:116`). You do it. The game says
nothing. No confirmation you are in the right place, no sound, no "contesting" state on the units,
no progress read-out. You are told to look at an unlabelled bar on a building you have to click to
inspect — and the ring you were told to park inside is only drawn while you have that building
selected.

**Why it is worth doing.** The attacker is the *active* party in the mod's headline mechanic and is
the one getting the least feedback. And unlike the bar (which they can already see — §0.1), this is a
genuine absence rather than a perceived one.

**Mechanism.** The gates are explicit and narrow, so the change is small:
`OnContestationStarted` (`:505-528`) and `OnDefeatPhaseStarted` (`:530-553`) each wrap their speech
and text in `if (self.Owner == localPlayer || localPlayer.IsAlliedWith(self.Owner))` (`:516`, `:541`).
Adding an `else` branch with an attacker-side line is the minimal version. The radar-ping predicate
at `:523` (`() => self.Owner.IsAlliedWith(self.World.RenderPlayer)`) is a second, independent switch.
A richer version drives a condition on the contesting units themselves so the player can see *which*
of their units are inside the ring — the trait already maintains exactly that list in
`actorsInRange` (`:134`, maintained by `ActorEntered`/`ActorLeft`, `:265-276`).

**Tier.** `SAFE WIN` for the notification half. The per-unit condition half is a step up in size.

**Honest risk.** This is the proposal most at risk of being **more machinery rather than more fun**.
The attacker arguably does not *need* a call-out: they chose to go there, and the bar is already on
screen. The strongest form of this idea is not a notification at all but the per-unit "you are
contesting" state, because that answers a question the player genuinely cannot answer today —
*is this unit inside the ring or two cells short of it?* If only the text line ships, the value is
thin. Note also the standing lesson recorded in PIPELINE's ambush block: *"shipping a readout
around an absent behaviour"* is a known failure mode here — the behaviour is present, so this
does not repeat it, but the bar for "is a readout the right answer" should stay high.

**Proof it does not already exist.** `grep` for `Contestation|ControlBar|SupplyRoute` across
`mods/ww3mod/chrome/**` returns **zero** (the single `ControlBar` hit is `ReplayControlBarLogic`,
unrelated). `Contestation` across `engine/OpenRA.Mods.Common/Widgets/**` returns **zero**.
`ControlBarFraction` has exactly one consumer outside its own trait repo-wide —
`Activities/AttackSupplyRoute.cs:120`, a bot activity. **The floating selection bar is the entire
player-facing readout that exists.**

---

## S3 — The single highest-leverage action at your own beachhead is invisible

**What the player experiences.** You clear the enemy off your Supply Route and march your army back
to the front, because the fight is over. Recovery takes three minutes. Had you left one rifleman
standing in the ring, it would have taken one — faster than the enemy took to drain it in the first
place. Nothing in the game, the tooltip, or the onboarding panel hints at this.

**Why it is worth doing.** It is a 3× swing on the mod's central resource, available for the price of
the cheapest unit in the game, and it is undiscoverable by playing. This is not a balance question —
the mechanic is good — it is purely that the player cannot see it. It also converts a passive
recovery timer into an actual decision (do I detach a screen, or take everyone forward?), which is
the cheapest new decision available anywhere in this document.

**Mechanism.** The multiplier is applied at `:475-477` and `:495-497`:
```csharp
var friendlyBoost = cachedNetFriendlySurplus > 0
    ? info.FriendlyRecoveryMultiplier : 1;
```
Note the trigger is `cachedNetFriendlySurplus > 0`, i.e. `Math.Max(0, friendlyValue - enemyValue)`
(`:299`) — with the enemy cleared out, **any** friendly unit with `Valued.Cost > 0` flips it. The
state is already public (`NetFriendlySurplus`, `:223`). Cheapest surfacing is bar colour or a pulse
while the boost is live; next cheapest is a line in the How To Play panel (`ingame-info-howtoplay.yaml`,
the B4 block at `:110-135`), which is being edited anyway for R9.

**Tier.** `SAFE WIN`. The panel-line version is minutes and rides along with the R9 fix.

**Honest risk.** Surfacing it may reveal that the 3× is *too* strong once players actually use it —
a defender who knows to leave a picket makes sieges materially harder to close, and the number was
presumably tuned in a world where nobody knew. That is a real balance consequence of a pure
legibility change, and it is the kind that only shows up in play. Mitigation is that
`FriendlyRecoveryMultiplier` is a YAML value (`structures.yaml:313`) and trivially retunable.

**Proof it does not already exist.** Repo-wide grep for `FriendlyRecoveryMultiplier` outside
`WORKSPACE/`/`DOCS/` returns exactly 3 hits: the declaration (`:72`) and the two `Tick` consumers
(`:476`, `:496`), plus the YAML value. **Zero UI, notification, tooltip or panel references.** The
How To Play text block (`ingame-info-howtoplay.yaml:110-135`, read in full) mentions recovery once —
*"push them off and it recovers"* (`:137`) — and says nothing about garrisoning or about any rate.

---

## S4 — Friendly aircraft defend your Supply Route; enemy aircraft cannot contest it

**What the player experiences.** You park a gunship over the enemy beachhead. Nothing happens — the
bar does not move, and the panel that told you to "park units inside the ring" gave you no hint that
your most mobile unit is exempt. Meanwhile a friendly gunship hovering over *your own* SR counts its
full purchase price as defensive value, cancelling out enemy ground units below it and tripling your
recovery rate.

**Why it is worth doing.** The exemption itself is defensible and probably deliberate (an
uncontestable-by-air beachhead is a reasonable design). **The asymmetry is not defensible as a
design, only as an accident**, and it is exploitable in exactly one direction: air is a pure
defensive tool at a Supply Route, at zero risk of the reverse. Whichever way it is resolved, it
should be resolved on purpose.

**Mechanism.** `IsRelevantActor` (`SupplyRouteContestation.cs:243-263`) applies **two different
tests** depending on relationship:
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
`CaptorTypes` defaults to `{Player, Vehicle, Tank, Infantry}` (`:33`) and is **not overridden on
`SUPPLYROUTE`** (the trait block `structures.yaml:303-316` contains no `CaptorTypes`; the only
occurrence anywhere in `mods/` is `misc.yaml:442`, on a different actor). Every aircraft and
helicopter resolves to `Types: Plane` via `^Aircraft`/`^Helicopter` → `^Airborne` (`aircraft.yaml:100-101`)
→ `^NeutralAirborne` (`aircraft.yaml:76-77`), which is not in that set — so an **enemy** aircraft
fails the test. An **allied** aircraft never takes that branch and passes on cost alone.
Altitude is not the reason: the trigger is registered with `WDist.Zero` vertical range (`:230-231`),
and `ActorMap.cs:144` skips the height test entirely when `vRange.Length == 0` — aircraft do enter
the trigger, they are filtered afterwards.
Fix is one line either way: add `Plane` to `CaptorTypes`, or apply the captor test to allies too.

**Tier.** `SAFE WIN` as a one-line YAML/engine change. **The design call is the expensive part**, not
the code — letting air contest changes siege play substantially and needs the user.

**Honest risk.** Adding `Plane` to `CaptorTypes` makes helicopters a cheap, hard-to-answer
contestation tool at a beachhead that may have no AA nearby, which could be a much larger balance
change than its one-line diff suggests. Closing the asymmetry the *other* way (excluding allied air
from defensive value) is the conservative option and is probably the right first move. This is the
one proposal here where I would not pick the direction myself.

**Proof it does not already exist — PARTIAL, and I am flagging it rather than claiming it.**
**Half of this is already filed.** `WORKSPACE/audit/260816-systems-completeness.md:448` carries
**"[POLISH] Aircraft, helicopters and ships cannot contest"**, closing with *"Likely intentional;
flagged so the choice is explicit."* I am not claiming that half.
What is new is (a) **the ally/enemy asymmetry**, which that entry does not mention at all — it treats
aircraft as simply absent from contestation, when they are in fact present and one-sided; and
(b) a **correction to its stated mechanism**: it says *"`^Aircraft` / `^Helicopter`
(`aircraft.yaml:95,136`) carry no `ProximityCaptor`"*. They carry one by inheritance from
`^NeutralAirborne` (verified: `^Aircraft:` at `aircraft.yaml:119` `Inherits@Type: ^Airborne`;
`^Airborne:` at `:100` `Inherits: ^NeutralAirborne`; `ProximityCaptor: Types: Plane` at `:76-77`).
Its conclusion is right, its reason is wrong, and all its line numbers have drifted.

---

# AMBITIOUS

Ranked. **A1 is the one I would do first** among these.

---

## A1 — Contestation pushes your beachhead back: reinforcements land further and further away

**What the player experiences.** Enemy units are grinding your Supply Route. Instead of (only) your
reinforcements arriving *more slowly*, they start arriving *in the wrong place* — the drop point
slides away down the map edge, then to a different edge entirely, and every unit you call in has a
longer, more exposed walk to reach the fight. Push the enemy off and the drop point walks back home.
You can watch your own logistics being pried away from you, and you can see the cost of every minute
you leave them there.

**Why it is worth doing.** Today contestation has exactly one behavioural output: a production-speed
multiplier (`IProductionSpeedModifier`, `:860-872`). It is invisible, purely numeric, and it makes
the tensest phase of the game *less* interesting to watch — things just happen slower. Displacement
is the same pressure expressed spatially: it is visible without any UI at all, it compounds
naturally (units arriving further away are also easier to ambush), and it reads as exactly what the
fiction says is happening to a beachhead under assault. It also directly matches the user's
standing preference recorded as **"gradient over hard transitions — price a bad state, don't
forcibly exit the player from it."**

**Mechanism.** Both traits sit on the same actor, so no world scan is needed — a plain
`self.TraitOrDefault<SupplyRouteContestation>()` in `ProductionFromMapEdge`. The edge choice already
funnels through one variable:
```csharp
var spawnAreaHint = FindClosestSpawnArea(self);
var searchOrigin = spawnAreaHint ?? self.Location;      // :117-118 (ground), :99-100 (air)
```
and candidate cells are already enumerated and indexed:
```csharp
candidates = self.World.Map.GetSpawnCandidatesOnSameEdge(searchOrigin, edgeInfo.SpawnCandidateCount);  // :122
```
with a round-robin walk at `:144-153`. A contestation-scaled offset applied to `searchOrigin` (or a
biased index into `candidates`) is the whole change. `ControlBarFraction` (`:221`) is already public
and already returns a clean 0-100. Files: `ProductionFromMapEdge.cs` (the `Produce` body, `:81-158`),
plus one new `Info` field defaulting to zero displacement so `@stable` and every existing map are
byte-identical until it is turned on.

**Tier.** `AMBITIOUS` — new gameplay, but a notably small blast radius for one.

**Honest risk.** The largest is that **it stacks two penalties on the losing player** — slower
production *and* longer walks — which can turn a bad position into an unrecoverable one and make
comebacks worse, the exact opposite of what the graduated design is for. The honest version of this
proposal probably *replaces* part of the production slowdown rather than adding to it, and that is a
balance decision, not an implementation one. Second risk: on small maps, or an SR near a corner, the
displaced entry point may have nowhere to go, so the effect is inconsistent per map — the 3×3
footprint and corner-inset behaviour noted in `supply-route.md:209` is a live concern here.
Third: this is invisible if the player never watches their own edge, which is a legibility problem
of its own — though a far more interesting one than a hidden multiplier.

**Proof it does not already exist.** `grep -c "SupplyRouteContestation" engine/OpenRA.Mods.Common/Traits/ProductionFromMapEdge.cs`
returns **0** — the two traits do not reference each other in either direction. `Produce` (`:81-208`,
read in full) reads exactly four inputs for its spawn decision: `spawnLocation` (an init, `:43-45`),
`FindClosestSpawnArea(self)`, `self.Location`, and `rp.Path`. **No contestation, health, damage or
player-state term enters the edge choice.** The sole external consumer of `ControlBarFraction`
repo-wide is `AttackSupplyRoute.cs:120`.

---

## A2 — The reinforcement lane becomes a thing you can see, plan around, and cut

**What the player experiences.** You can see where the enemy's reinforcements walk onto the map, and
the line they take to reach their beachhead. So you can go and sit on it. Killing a tank as it walks
in, alone and unescorted, becomes a deliberate play with a name — instead of something that happens
by accident when your patrol wanders into the right place. And your own arriving units are legible
to you: you can tell at a glance that the four vehicles you just bought are walking through open
ground on the far side of the map.

**Why it is worth doing.** The game model document asserts this lane is a designed feature —
*"Enemy can ambush reinforcements en route — and the same is true in reverse"* (`game-model.md:30`),
and `supply-route.md:197` lists **"Reinforcement-lane awareness"** as something the strategic layer
should reason about. **Neither is true today for a human player.** The lane is invisible, no order
targets it, and the only lane-aware code in the repo is bot-only and models a different line
entirely. This is a whole advertised strategic layer that exists on paper.

**Mechanism.** Three separable pieces, in increasing size:
- **Draw it.** `RallyPointIndicator.cs:93-94` inserts the *building exit* as node 0
  (`targetLineNodes.Insert(0, building.CenterPosition + (exit?.Info.SpawnOffset ?? WVec.Zero))`), so
  the dashed line runs SR → waypoints. **The map edge is never a node** — the segment the unit
  actually walks is the one segment not drawn. Adding the resolved edge cell as node 0 is a small
  change; it is gated at `:134-135` on `wr.ShowAllOrders || Selection.Contains(building)`.
- **Mark units in transit.** `GrantExternalConditionToProduced: Condition: produced`
  (`structures.yaml:368-369`) already fires on every produced unit — and **the condition has zero live
  consumers**: the only `RequiresCondition: produced` occurrences in `mods/` are `vehicles.yaml:916,919,921`,
  all inside the fully commented-out `HARV:` block (`:902-929`). The grant half of "this unit is a
  fresh arrival" is already wired and inert, waiting for a consumer.
- **Interdict it.** A player-issuable order to picket a lane. This is the genuinely new part.

**Tier.** `AMBITIOUS`. The first bullet alone is a `SAFE WIN` and could ship independently.

**Honest risk.** The biggest is that **the lane may not be interesting terrain**. Because the entry
edge is keyed to the SR's own cell (§0.3) and every SR sits near its owner's spawn, the lane is a
short hop from the map edge to a building a few cells inland — deep in the owner's own territory,
where an interdictor has to survive to reach it. If that is the shape on most of the ten shipped
maps, "interdict the lane" collapses into "attack their base", which the game already has. **This is
the load-bearing unknown and it is cheap to settle by reading map geometry** — I did not do it, and
I would settle it before spending anything on the third bullet. The first two bullets are worth
doing regardless, because they are legibility for a lane that exists either way. Note also that the
`produced` condition is granted with no duration, so it marks "was produced", not "is still in
transit" — a consumer needs its own revoke, which the `ExternalCondition` machinery supports but
which nobody has written here.

**Proof it does not already exist.** `grep -rni "reinforcementpath|ProductionPath|SpawnPathRender"`
across `engine/` and `mods/` returns **0 matches**. `grep -rni "interdict"` across
`engine/OpenRA.Mods.Common`, `mods/ww3mod` and `DOCS` returns 2 hits, **both prose**
(`DOCS/design/ai-realism.md:28`, `DOCS/archive/CLAUDE_IDEAS.md:28`) — no trait, no order, no
activity. The one lane-aware module, `LaneAmbushBotModule`, is `[TraitLocation(SystemActors.Player)]`
gated on `enable-ai-experimental`/`enable-ai-stable` (`ai.yaml:842`, `:2594`) so humans never
instantiate it, and its "lane" is a straight beachhead→enemy-anchor interpolation
(`AmbushLaneMath.PostPosition`) that **does not model the edge→SR path at all**.

---

## A3 — A forward drop zone: give the player the one lever they have never had

**What the player experiences.** You place a drop-zone marker somewhere on the map you control.
From then on, reinforcements you call in arrive at the map edge nearest *that* marker instead of
the one next to your beachhead — so a push on the far side of the map stops costing you a two-minute
walk per unit. Holding the ground around the marker becomes worth doing; losing it puts your
logistics back where they started.

**Why it is worth doing.** The Supply Route is deliberately a fixed object the player does not
choose — that is the design, and it is good. But it means the player's *entire* agency over their
own logistics is one rally point that only controls where units muster after they have already
walked the length of the map. A drop zone adds a real, spatial, contestable decision without
touching the SR's fixedness, without transferring ownership of anything, and without a second
production source. It is the natural companion to A2: it makes the lane something you *route*, not
just something that happens.

**Mechanism.** The engine hook already exists and is currently map-editor-only.
`FindClosestSpawnArea` (`ProductionFromMapEdge.cs:56-79`) scans `ActorsWithTrait<SpawnArea>()`, picks
the one nearest the SR (`:70`), and hands it to `searchOrigin` (`:100`, `:118`) — **it is not
owner-filtered** (`:58-61` has no owner predicate), which is currently harmless because the only
actor carrying `SpawnArea` is the Neutral editor marker `spawnarea` (`misc.yaml:255-270`:
`RequiresSpecificOwners: ValidOwnerNames: Neutral`, `RenderSpritesEditorOnly`, `EditorOnlyTooltip`).
A player-placeable variant would need: an owner filter on that scan (otherwise an enemy's drop zone
hijacks your entry edge), a "nearest the *front*" rather than "nearest the SR" selection rule, and a
buildable actor. `ProductionSpawnLocationInit` (`:43-45`, `:235-238`) shows the trait already accepts
an explicit override, though only at actor creation.

**Tier.** `AMBITIOUS`.

**Honest risk.** Three, and the first is serious.
1. **It brushes PIPELINE item 17's territory.** Item 17 (parked) delivers a second reinforcement
   entry point *by capturing a neutral SR*. This delivers a second entry point by placement. They are
   mechanically distinct — no ownership transfer, no capture, no second production queue, and the SR
   itself is untouched — but they scratch the same itch, and shipping this may make item 17 feel
   redundant or may conflict with it. **That is a user call, not a manager call.**
2. **The unowned-scan bug becomes live.** Today `FindClosestSpawnArea` being owner-blind is inert
   because every marker is Neutral and map-symmetric. Introduce player-owned ones and it is an
   exploit on day one. This is exactly the shape of defect this project keeps recording — a latent
   coupling that is harmless until something changes upstream.
3. **It may simply be too strong.** Removing reinforcement travel time is removing one of the
   defining costs of the mod's economy. The interesting version is probably heavily constrained
   (one at a time, slow to establish, contestable, or granting a *worse* entry than home).

**Proof it does not already exist.** `grep -rn "SpawnArea" mods/ww3mod/rules/` returns **exactly 1
hit** — `misc.yaml:262`, on the editor-only Neutral marker read in full above. No buildable,
deployable or player-ownable actor carries the trait; the marker has `RequiresSpecificOwners:
ValidOwnerNames: Neutral` (`misc.yaml:269-270`) and `RenderSpritesEditorOnly` (`:266`), so it cannot
be owned by a player or seen in play. `RallyPoint` is a single non-instanced trait with one
`List<RallyPointWaypoint> Path` and is **not keyed by production type** — every consumer uses
`TraitOrDefault<RallyPoint>()` — so no per-type or forward staging exists today either.

---

# Recommendation

**Do S1 first.** It is the smallest change with the largest ratio of felt-value to risk: it is one
file, no new trait, no YAML schema change, and it repairs the mod's central mechanic *telling the
player it is happening*. The threshold call-out in particular marks the exact instant the game
starts costing the player something, and today that instant is silent. It also pairs naturally with
the R9 wording fix already queued, so the SR's whole communication story gets fixed in one pass
rather than two.

**Then S3**, which is minutes if it ships as a panel line alongside R9, and converts a hidden 3×
multiplier into an actual decision.

**Among the ambitious swings, A1.** It is the only one of the three whose blast radius I can bound
by reading: two traits on the same actor, one new default-inert `Info` field, no RNG, no new actor,
no UI. It expresses contestation spatially instead of numerically, which is both more legible and
more interesting to watch than the current multiplier, and it matches the user's recorded
"gradient over hard transitions" preference directly. A2's first two bullets are worth doing at any
time and are nearly free; **A2's third bullet and all of A3 should wait** — A2's for a cheap map-
geometry check that may invalidate it, A3's for a user ruling on its overlap with item 17.

---

# What I did not verify, and where I could be wrong

- **I did not launch the game or run any test.** Everything here is code-read. In particular the
  claim that the attacker's bar is *perceptible in practice* (§0.1) is a rendering-path argument, not
  an observation — the bar renders, but whether a player notices an unlabelled bar on a distant
  building at play zoom is a visual question. **The command that would settle it** is the
  `DOCS/recipes/SCREENSHOT.md` capture flow on a scenario with an enemy unit inside an SR ring,
  evaluated multimodally; the answer that would count is whether the bar is identifiable without
  prior knowledge of what it is.
- **A2's central unknown — whether the reinforcement lane crosses interesting ground — I did not
  settle.** It is answerable statically from the ten shipped maps' geometry plus the edge-selection
  rule in §0.3, and it should be answered before any work on A2's interdiction bullet.
- **The §0.5 durations are computed, not observed.** They follow from `BarMax`, the YAML values and
  16.67 tps, and I applied the integer-truncation note the code itself makes at `:356-358`; but I
  have not watched a bar drain.
- **S4's design direction is genuinely open** and I deliberately did not pick one. Both fixes are one
  line and they point in opposite balance directions.
- **I read `PIPELINE.md` whole (473 lines) and `RELEASE_V1.md`'s Supply Route sections.** The two open
  SR lines there — *"Captured SR handling"* and *"Primary SR selection UI"* (`RELEASE_V1.md:105-106`) —
  are both downstream of capture wiring (item 17) and are untouched by anything above. Nothing in
  this document duplicates items 17 or 18, R9 or R12.
- **One overlap I am disclosing rather than papering over:** `WORKSPACE/recon-ingame-info-ui.md:158`
  already proposes surfacing `ControlBarFraction`/`IsPassive` as a column in the **Esc-menu info
  panel** ("direction C"). That is a between-actions scoreboard; S1/S2/S3 are all live in-match
  feedback. They are compatible, but whoever builds either should read the other.
