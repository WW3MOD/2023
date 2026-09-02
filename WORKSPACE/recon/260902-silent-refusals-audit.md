# Silent refusals — audit, 2026-09-02

**Read against `main @ 6a7e1839`** (worktree `wt/silent-refusals`, branched from `main`, 0 commits behind `origin/main` at dispatch). Read-only: no build, no launch, no autotest, no YAML validator.

**Method.** Every `file:line` below was opened and read. Two subagents swept the capture and transport/resupply seams; **every citation they returned that appears in this document was re-opened and re-verified by hand**, and the ones that did not survive that check are recorded in §"Killed" at the bottom rather than quietly dropped.

**Scope check.** Cross-read against `PIPELINE.md` (whole file, R1–R17 + QUEUE + the user-gated ambush block 67–71) and `RELEASE_V1.md`. Nothing below duplicates a queue item. Where a finding touches one, the overlap is named explicitly.

---

## The shape of the problem, stated once

This codebase refuses player actions **well** and explains them **almost never**. The refusal logic is careful, commented, and in several places has been argued over and ruled on. The feedback layer is missing to a degree that is easy to prove: `mods/ww3mod/languages/en.ftl` is the mod's **only** `.ftl` file, 743 lines, and contains **zero** matches for `ammo`, `rearm`, `resupply`, `evacuat` or `captur` (case-insensitive). There is no player-facing sentence about any of it.

The cursor art, by contrast, largely **already exists** — `mods/ww3mod/cursors.yaml` defines `enter-blocked:` (`:111`), `deploy-blocked:` (`:170`), `goldwrench:` (`:173`) and `goldwrench-blocked:` (`:176`). Several findings below are wiring, not art.

One precedent governs this whole area and should be quoted in any brief that comes out of this audit — **`Passenger.cs:116-121`, a user ruling of 2026-08-30**, verbatim:

> `// USER RULING, 2026-08-30 — considered and ruled against, do not re-propose without`
> `// reading this. Folding CanEnter in here was built on this branch so the refused click`
> `// would fall through to a Move instead. It was reverted because it is the same shape as`
> `// a shipped rule — an order must never silently become a move order — and because it`
> `// spent the only feedback the player has`

**"An order must never silently become a move order"** is already the house rule. Findings 6 and 7 below are places where the shipped code does exactly that.

---

# SAFE WINS

## 1. The "you cannot capture that" cursor is written and then thrown away

**What the player experiences.** You select a rifleman and right-click a neutral oil derrick. You get an ordinary move cursor, the rifleman walks over, stands on the doorstep, and nothing happens — ever. Only a technician can take a neutral building, but nothing at any point says so. The same dead click is what you get for every capture the unit is not allowed to make.

**The mechanism.** `Captures.cs:142-149` writes the blocked cursor into the `ref` parameter and then returns `false`:

```
142			public override bool CanTargetActor(Actor self, Actor target, TargetModifiers modifiers, ref string cursor)
143			{
144				var targetManager = target.TraitOrDefault<CaptureManager>();
145				if (targetManager == null || !captures.CaptureManager.CanTarget(targetManager))
146				{
147					cursor = captures.Info.EnterBlockedCursor;
148					return false;
149				}
```

The write is discarded. `UnitOrderGenerator.cs:333-335` declares `cursor` fresh inside the loop and abandons it on a false return:

```
333						string cursor = null;
334						if (!o.Order.CanTarget(self, candidate, actorsAt, xy, modifiers, ref cursor))
335							continue;
```

The intended default is real and the art exists — `Captures.cs:72` is `public readonly string EnterBlockedCursor = "enter-blocked";`, and `cursors.yaml:111` defines `enter-blocked:`.

**Proof it does not already exist.** The contrasting correct pattern is two files away: `EnterAlliedActorTargeter.cs:56-57` assigns the same class of cursor and returns **true** —

```
56				cursor = useEnterCursor(target) ? enterCursor : enterBlockedCursor;
57				return true;
```

So `Captures.cs` is the odd one out, not the convention. `grep -c "PlayNotification\|AddTransientLine" Captures.cs` returns **0**, and `en.ftl` has **0** matches for `captur`. Nothing anywhere else covers this.

**Tier.** `SAFE WIN`.

**Implementation shape.** Return `true` with the blocked cursor and let the order be consumed and dropped — which is precisely the shipped, user-ratified `Passenger` pattern (`Passenger.cs:111-121`). One method in one file; the art and the Info field are already there. Add an `EnterCursor: goldwrench` / `goldwrench-blocked` pair on the capture templates if you want the capture-specific glyph, which is also already drawn but currently referenced by nothing but `infantry.yaml:937`.

**Honest risk.** Returning `true` makes the capture targeter *consume* the click at `OrderPriority: 6`, which is above some targeters and below `EnterTransport` at 5. A unit that is both a capturer and a passenger could have its click swallowed by a capture order it will not execute. Worth one deliberate look at priority ordering rather than a blind flip. Second risk: this must not become "the cursor promises, the activity refuses" — the point is a *blocked* cursor, not an enabled one.

---

## 2. Helicopters are the only thing in the game that evacuates by itself, and the only thing with no evacuation marker

**What the player experiences.** Your helicopter runs out of missiles and flies off the map on its own, unasked. There is no icon on it, no message, and no sound. From the player's side an expensive unit simply leaves the battle and disappears over the edge for no stated reason.

**The mechanism.** The auto-evacuation behaviour and its only visual marker sit on **mutually exclusive templates**, both in `mods/ww3mod/rules/ingame/aircraft.yaml`:

- `^Aircraft:` opens at `:119` (`Inherits@Type: ^Airborne`) and carries the marker at `:153-158` — `WithDecoration@Evacuating:` / `Sequence: pip-orange` / `RequiresCondition: evacuating`, plus `SelectionPriorityModifier@Evacuating:` at `:150`.
- `^Helicopter:` opens at `:160` and also inherits **`^Airborne`, not `^Aircraft`** (`:161` — `Inherits@Type: ^Airborne`). It carries `EvacuateWhenUnrearmable:` at `:195`.

So the template that evacuates has no pip, and the template with the pip never auto-evacuates — fixed-wing aircraft have no `EvacuateWhenUnrearmable` at all.

**Proof it does not already exist.** Grepping lines 160–330 of `aircraft.yaml` (the whole `^Helicopter` block) for `Evacuating` returns **nothing** — the only two hits in that range are the two `Inherits` lines. The condition itself is available: `^NeutralAirborne:14-15` declares `ExternalCondition@Evacuating:` / `Condition: evacuating`, inherited down the chain. Infantry and vehicles both already have the pip (`infantry.yaml:159,165`; `vehicles.yaml:136,142`), which establishes the marker as the house convention and helicopters as the single gap. `en.ftl` has **0** matches for `evacuat`.

**Tier.** `SAFE WIN`.

**Implementation shape.** Copy the `WithDecoration@Evacuating` (and probably `SelectionPriorityModifier@Evacuating`) block from `^Aircraft` into `^Helicopter`, or hoist both into `^Airborne` so no future airborne template can miss them. Genuinely a few lines of YAML.

**Honest risk.** Hoisting into `^Airborne` changes fixed-wing actors too (harmlessly — they already have it, so you must remove the duplicate or the merge will double it). Also note `EvacuateWhenUnrearmable.cs` has `IncludeBotOwners = false` per `RELEASE_V1.md:113`, so this is **player-side only** — which makes it *more* worth fixing, not less: the human is the only one who ever sees it, and they see it unexplained.

---

## 3. Neutralising an enemy building is completely silent — for you *and* for your victim

**What the player experiences.** Your rifleman spends a full minute inside an enemy AA gun and turns it grey. No voice line, no text, no sound. And on the other side of the map, the player who just lost that AA gun is told nothing at all — their defence stops working and they find out by noticing.

**The mechanism.** For a soldier's neutralise, `CaptureToNeutral: true` sends the new owner to the world actor:

`CaptureActor.cs:134` — `var newOwner = captures.Info.CaptureToNeutral ? w.WorldActor.Owner : self.Owner;`

`CaptureNotification.cs:73-74` then addresses the notification to that owner:

```
73				Game.Sound.PlayNotification(self.World.Map.Rules, newOwner, "Speech", info.Notification, faction);
74				TextNotificationsManager.AddTransientLine(newOwner, info.TextNotification);
```

`newOwner` is the **Neutral player**, not the human who did it. The victim's channel is the next two lines (`:77-78`) and it is empty by default: `CaptureNotification.cs:35` — `public readonly string LoseNotification = null;`.

**Proof it does not already exist.** The trait is applied with **bare defaults** — `structures.yaml:54` is the entire declaration, `CaptureNotification:` with no fields under it, on the shared building template. So `Notification = "BuildingCaptured"` (`:21`) goes to Neutral, `TextNotification`, `LoseNotification` and `LoseTextNotification` are all null. `en.ftl` returns **0** matches for `captur`. There is no override anywhere: the only other declaration in the mod is `vehicles.yaml:111`, likewise bare.

Note the asymmetry this creates, which is the tell that it is a bug and not a design: a **technician** capture (`CaptureToNeutral` false) sets `newOwner = self.Owner`, so *that* path works and you do hear "BuildingCaptured". Only the neutralise path is silent.

**Tier.** `SAFE WIN`.

**Implementation shape.** Either special-case the `CaptureToNeutral` path to address the captor rather than the new owner, or — cheaper and less invasive — set `LoseNotification` / `LoseTextNotification` on the building template and add a captor-side line. Needs two or three new `en.ftl` strings, which do not exist yet for anything in this space.

**Honest risk.** `game-model.md:51` records that soldier-neutralisation is already "close to unanswerable" against a bot and is tracked as a live balance risk in `bugs/discovered.md`. Making it *audible* will make players use it more. That is arguably correct — an invisible dominant strategy is worse than a visible one — but it is a balance-adjacent call, not purely a UI one, and should be flagged as such rather than shipped quietly.

---

## 4. The Deploy button lights up when you have nobody who can deploy

**What the player experiences.** You select two neutral oil derricks and the Deploy button in the command bar is enabled. You press F. Nothing happens — no sound, no error, no unit moves. You press it again. Still nothing. You have no technicians, but the button never said so.

**The mechanism.** `CommandBarLogic.cs:160-166`, inside the `IsDisabled` delegate:

```
160					deployButton.IsDisabled = () =>
161					{
162						UpdateStateIfNecessary();
163
164						var queued = Game.GetModifierKeys().HasModifier(Modifiers.Shift);
165						if (selectedCaptureTargets.Length > 0)
166							return false;
```

Selecting any capturable structure enables the button unconditionally. The enable test never asks whether a capturer exists to dispatch.

**Proof it does not already exist.** The `return false` at `:166` is an unconditional early-out — there is no capturer term anywhere above it in the delegate, and the capturer-availability question is only asked much later, at dispatch time. There is no notification on the press path.

**Tier.** `SAFE WIN`.

**Implementation shape.** Extend the enable predicate to require at least one free capturer, reusing whatever `CaptureDispatchManager` already computes for its own state machine (it has an `Evaluate` and a `CursorForState`, so the availability question is already answered somewhere).

**Honest risk.** Small: a disabled button is itself a weak explanation ("why is this greyed out?"). The genuinely better version pairs the grey-out with a tooltip reason, which is more work. Also worth knowing before scoping: I could **not** confirm the "~3 technician cap" the brief assumed applies to humans — the `3` in `ai-america.yaml:41` is under a bot `UnitLimits:` block. Do not build player-facing copy around a cap that may only bind the AI.

---

## 5. The game tells you how thick a tank's armour is and never tells you what can get through it

**What the player experiences.** The Abrams tooltip says its armour is "Heavy — 700 thick". No weapon anywhere in the game displays a number that can be compared to 700. A player has no way to find out which of their units can hurt it, so the number is decoration, and "my shells are bouncing" is indistinguishable from "my shells are missing".

**The mechanism.** Armour is surfaced: `Armor.cs:69` — `return new[] { TooltipElement.Stat("Armour", value) };`, rendering `$"{Type} — {Thickness} thick"`. Real values are large and precise-looking: `vehicles-america.yaml:499` gives the Abrams `Thickness: 700`.

The number it must be compared against is `Penetration`, and the comparison is a cliff, not a curve — `DamageWarhead.cs:124-134`:

```
124		// Armour subtracts from damage only when it out-thicknesses the warhead: a warhead that
125		// penetrates does full damage, one that does not keeps the fraction it got through.
126		// Penetration defaults to 1, so a warhead that omits it delivers damage/thickness — which
127		// against a 280mm tank is 0.4% of the number written in the YAML.
128		public static int ApplyPenetration(int damage, int penetration, int thickness)
129		{
130			if (thickness <= 0 || penetration >= thickness)
131				return damage;
132
133			return damage * penetration / thickness;
134		}
```

**Proof it does not already exist.** `Penetration` appears in **zero** files under `engine/OpenRA.Mods.Common/Widgets/`, and in neither `Tooltip.cs` nor `TooltipDescription.cs`. The full set of `IProvideTooltipDescription` implementors is `Health.cs`, `Valued.cs`, `Cargo.cs`, `Mobile.cs`, `Armor.cs`, `AmmoPool.cs`, `Air/Aircraft.cs` — **no weapon or armament trait is among them**, so the tooltip shows no weapon stat of any kind: no damage, no range, no penetration.

**Tier.** `SAFE WIN` for the stat row; `AMBITIOUS` if you want a real "can this hurt that" matrix.

**Implementation shape.** An `IProvideTooltipDescription` on the armament/weapon side mirroring `Armor.cs:53-70`, printing the best `Penetration` among the actor's warheads. The interface, the `TooltipElement.Stat` helper and the priority-ordering are all already in place.

**Honest risk — and this one corrects a hypothesis I started with, so read it before scoping.** I initially counted **83 of 141** damaging warheads in `rules/weapons/*.yaml` as omitting `Penetration` and took that for a mass defect. **It is not.** Reading the actual pairs shows the omission is deliberate and structural: the direct-hit warhead carries the penetrator and the splash warhead does not. `weapons-ballistics.yaml:851-859` is the pattern — `Warhead@Target: TargetDamage` with `Penetration: 800`, immediately followed by `Warhead@Spread: SpreadDamage` with `Damage: 3000` and no `Penetration` at all. Splash *should* be stopped by armour. **So do not brief anyone to "add Penetration to 83 warheads."** The finding here is purely that the player is shown one side of a two-sided comparison. The related "missiles silently vanish" incident (`RELEASE_V1.md:77`) was an *aiming* bug that dumped a missile onto the splash warhead, not a penetration-table bug.

---

# AMBITIOUS

## 6. You cannot shoot the enemy Supply Route with anything, and ordering your army to attack it marches them into the enemy base instead

**What the player experiences.** The enemy Supply Route is the most obvious target on the map — it is where all their units come from, it is drawn with a big ring around it, and it shows a full health bar with 75,000 hit points. You select your whole armoured force and right-click it. The cursor says *move*. Your army drives across the map, parks on top of it, and stands there being shot at, firing at nothing. Nothing ever told you the building cannot be damaged.

**The mechanism.** `mods/ww3mod/rules/ingame/structures.yaml:296-297`, inside `SUPPLYROUTE:`:

```
296		Targetable:
297			TargetTypes: NoAutoTarget
```

`NoAutoTarget` is the actor's **entire** target-type list. `grep -rn "NoAutoTarget" mods/ww3mod/rules/weapons/` returns **0** — no weapon in the mod lists it in `ValidTargets`. So `ChooseArmamentsForTarget` finds nothing and `AttackBase.cs:845-846` refuses:

```
845					if (!armaments.Any())
846						return false;
```

Every unit refuses for the same reason, so nothing in the selection accepts the click. `OrderFallbackMath.cs:106-108` then reopens the default order for everybody:

```
106		public static bool SelectionSuppressesRefusers(int unitsAcceptingSpecificOrder)
107		{
108			return unitsAcceptingSpecificOrder > 0;
```

With zero accepters, `UnitOrderGenerator.ResolveSelection` re-resolves with `allowRelocationOntoEnemy: true`, `relocationAllowed` becomes true at `UnitOrderGenerator.cs:326`, and the terrain retry's **Move** order is admitted at `:342`. Because `GetCursor` runs through the same resolver (`:164`), the player is shown a move cursor *before* they click — the game affirmatively promises the move.

Two details sharpen this. First, the SR is the **only** actor in the mod whose target list is `NoAutoTarget` alone; every other user pairs it with a real type — e.g. `structures.yaml:143` is `TargetTypes: NoAutoTarget, C4, DetonateAttack`, which *can* at least be demolished. Second, `structures.yaml:294-295` gives it `Health: HP: 75000` and the actor carries `SelectionDecorations:`, so it renders a full, permanent health bar advertising a destructibility that does not exist.

**Proof it does not already exist.** Nothing in `PIPELINE.md` covers this: R12 is the supply cache, R9 is the onboarding panel's *wording*, item 17 (SR capture wiring) is user-deferred and is about ownership, and `RELEASE_V1.md:105-106`'s Supply Route lines are "Captured SR handling" and "Primary SR selection UI". The contestation mechanic *is* signposted once you get close — `WithRangeCircle@Contestation` at `:298-302` and `ContestationTextNotification: Supply Route contested!` at `:307` — but that fires only after units are already inside the ring. **At the moment of the click, there is nothing.**

**Tier.** `AMBITIOUS` — the cheap half is a cursor, but the real fix is teaching the player a core rule of the game.

**Implementation shape.** Three layers, separable: (a) a blocked/"cannot be destroyed" cursor over the SR — same fix shape as finding 1; (b) suppress or restyle the health bar on an actor nothing can damage, so it stops advertising a health pool; (c) the real answer — make the click *mean* something, e.g. resolve an attack order on an enemy SR into an attack-move to its contestation ring, teaching "you surround this, you don't shell it".

**Honest risk.** (c) is a genuine design decision and could be wrong — silently reinterpreting one order as another is exactly the sin `Passenger.cs:116-121` was reverted for, so it must be *visible* (distinct cursor, distinct target line) or it repeats the mistake. (a) and (b) are safe but only remove confusion; they do not teach contestation. Also: the SR is `FrozenUnderFog: AlwaysVisibleRelationships: Ally, Neutral, Enemy` (`:243-244`), so it is permanently visible to enemies — meaning players will click it early and often, which raises the value and the urgency.

**A run would settle one thing I could not.** Whether the army actually paths onto the SR footprint or stalls at its edge is occupancy behaviour I cannot read off. If someone wants it: `./run-test.sh <scenario>` with a scenario placing an enemy SR and a player tank group, asserting the tanks' final positions and that no `Attack` activity is ever queued. I did not run it.

---

## 7. An attack order a unit cannot carry out either vanishes or turns into a move — and the code says out loud that both options are bad

**What the player experiences.** Two versions, both silent. If part of your group has ammo and part does not, you click an enemy and the empty ones simply do not respond — they stand where they are while the rest attack, and nothing marks them. If the *whole* group is dry, you click an enemy and everyone drives at it instead, then sits in front of it not shooting.

**The mechanism.** The refusal is deliberate and thorough. `AttackBase.cs:851-852` refuses actor targets when every candidate armament is dry:

```
851					if (ordered.TrueForAll(armament => armament.AmmoPool != null && !armament.AmmoPool.HasAmmo))
852						return false;
```

and `:895-896` does the same for force-fire at ground (`if (AmmoPool.CannotFight(self))` / `return false;`). The same predicate gates attack-move and guard (`AttackMove.cs:118,238,324`; `GuardOrderGenerator.cs:89`).

What happens next is the per-selection rule at `OrderFallbackMath.cs:106-108` quoted in finding 6: refusers are dropped in silence while anyone else accepts, and when nobody accepts, everybody gets the move. The file's own header, `OrderFallbackMath.cs:6-7`, states the behaviour it exists to prevent —

```
6	 * The behaviour being prevented: an attack order a unit cannot execute must leave the unit alone,
7	 * still doing whatever it was doing, rather than sending it unarmed into the target's guns.
```

— and `:100-104` then explains why it does that anyway in the all-dry case:

```
100		/// The rule is per-selection because that is where it is meaningful. Applied per-unit it also
101		/// silences a selection in which NOTHING can attack, and a click that produces no order also
102		/// produces no cursor — the player is left hovering an enemy with a bare pointer, unable to
103		/// tell a refusal apart from a broken build.
```

**That is the finding.** The author picked the least-bad of two bad options because the third option — *tell the player why* — does not exist in this codebase. Both horns are silent.

**Proof it does not already exist.** `en.ftl` has **0** matches for `ammo`, `rearm` or `resupply`; it is the mod's only `.ftl` file. There is no dry-unit cursor: the refusal paths at `AttackBase.cs:846, 852, 861, 867, 891, 896, 904` all `return false` without assigning `cursor`. Ammo *state* is partly visible — `WithAmmoPipsDecoration` is used at 65 sites across the shipped rules — but that is a pip on the unit, not an answer to the click.

**Also worth recording:** `RELEASE_V1.md:99` lists "Units out of ammo reject attack orders (don't freeze aiming)" as still open `[ ]`. **The rejection ships.** The seven `return false` sites above, plus the attack-move and guard gates, are that item. What is actually left of it is the feedback half, which the tracker line does not mention. That tracker line should be re-scoped, not worked.

**Tier.** `AMBITIOUS` — not because any one piece is hard, but because doing it properly means introducing the missing channel (a refusal cursor plus a reason string) that findings 1, 4, 6 and 8 all also want.

**Implementation shape.** A shared "refused, and here is why" seam: a cursor variant plus a transient text line, raised at the targeter layer where the reason is still known. `OrderReadinessMath.cs` already exists as the shared home for "is this order blocked" logic and is the natural place to hang it.

**Honest risk.** The reason a refusal cursor was not simply added is real and is spelled out at `AttackBase.cs:854-859`: a blocked cursor must not blank the click for a *healthy* selection, so the cursor has to be per-unit-aware while the click is per-selection. Get that wrong and one dry rifleman greys out an order for eleven good ones — a clear regression. Any brief here must carry that constraint or it will be re-learned the expensive way.

---

## 8. A dry tank drives to the depot, docks, and leaves still empty

**What the player experiences.** Your tank is out of shells. You send it back to the Logistics Centre. It drives there, docks, sits for a moment, undocks, and drives away — still out of shells. The depot was out of supply, but the animation of a successful resupply played out anyway.

**The mechanism.** `Rearmable.cs:106-107` treats an unaffordable pool as finished rather than as a reason to wait:

```
106				// Nothing here can ever pay for this pool, so waiting at the depot buys nothing.
107				// Treated as "this pool is done" rather than as a reason to hold the client.
```

immediately followed by `if (provider != null && provider.CurrentSupply < ammoPool.Info.SupplyValue)` / `continue;`. The doc comment at `:78-81` names the behaviour: *"Returns 'done' — ending the errand — when every pool is full OR the host cannot afford any pool still wanting rounds. The second case is PARTIAL REFILL THEN LEAVE."*

The design reason is sound and documented — a client parked at a depot that cannot pay was previously wedged out of every bot module for the rest of the match. The gap is only that the player is not told which of the two outcomes they got.

**Proof it does not already exist.** `grep -c "PlayNotification\|AddTransientLine"` returns **0** for both `Rearmable.cs` and `SupplyProvider.cs`. `Activities/Resupply.cs` has notifications only for repair start/finish — there is no ammo counterpart. `en.ftl`: **0** matches for `ammo`, `rearm`, `resupply`.

**Tier.** `SAFE WIN` for a notification; `AMBITIOUS` if you want depot supply legible *before* the trip.

**Implementation shape.** A transient line and/or a sound on the partial-refill exit, plus — the higher-value half — a supply readout on the Logistics Centre itself so the player can see it is dry before sending anything. `SupplyProvider` already tracks `CurrentSupply`; a selection bar is the cheap version.

**Honest risk.** Notification spam: with several units cycling through a broke depot this could fire constantly. Needs a rate limit (`CaptureNotification.cs:27`'s `TicksBetweenNotifications` is the in-repo precedent for exactly that).

---

# Which one I would do first

**Finding 1, the dead capture cursor.** It has the best ratio in the set by a distance: the fix is one method, the cursor art is already drawn, the correct pattern exists two files away in `EnterAlliedActorTargeter.cs:56-57`, and the user has *already ruled* on this exact trade-off in `Passenger.cs:116-121` — so it needs no new design decision. And it lands on the interaction a new player performs earliest and most often: walking a man into a building and wondering why nothing happened.

**Then finding 2** (helicopter evac pip), because it is a few lines of YAML and closes a "my unit vanished" mystery.

**Then finding 6** (Supply Route), which is the highest *felt* value in the whole audit — it is the first strategic mistake every new player will make — but it needs a design call, so it should not go first.

---

# Killed — candidates that turned out to already exist

Recorded so nobody re-proposes them.

- **Full transport / full garrison.** Already solved and **user-ruled**. `Passenger.cs:62` sets `EnterBlockedCursor = "enter-blocked"`, `EnterAlliedActorTargeter.cs:56` renders it, `cursors.yaml:111` has the art, and `Passenger.cs:111-121` records the 2026-08-30 ruling that the dead click plus the blocked cursor **is** the feedback. Do not re-propose.
- **Capture already in progress.** Signposted without selection: `CapturableProgressBar` and `CapturableProgressBlink` are both on the shared capturable template at `structures.yaml:176-177`.
- **Hovering a capturable with nothing selected.** `CaptureDispatchManager` already returns a real blocked cursor for its busy/covered states, and its own doc comment argues deliberately for showing nothing when the player owns no technicians. An argued decision, not an oversight.
- **Supply Route contestation.** Fully signposted — range circle (`structures.yaml:298-302`), notification and text (`:306-307`), control bar and production slowdown all confirmed live in `RELEASE_V1.md:38`. The only live defect is R9's *wording*.
- **Cargo unload with nowhere to drop.** Has a sound already: `defaults.yaml:953` — `NoUnloadNotification: BuildingCannotPlaceAudio`.
- **"83 warheads are missing Penetration."** My own hypothesis, killed by reading the pairs. See finding 5's risk paragraph.

# Corrections to the briefing material

- **`ClearSightThreshold` is at `Armament.cs:429`, not `:364`.** The line is `if (!FiringLOS.HasClearLOS(self, target, Weapon.ClearSightThreshold))`. I did **not** develop this into a finding: PIPELINE item **71** already covers the mechanism *and already states "nothing in the UI says so"*, and item 71 sits inside the user-gated ambush block where nothing may be implemented. Treat the UI half as gated with the rest of it, not as free ground.
- **The "~3 technician cap" does not demonstrably bind human players.** The `3` is `ai-america.yaml:41` under a bot `UnitLimits:` block. Verify before writing player-facing copy that assumes a cap.
