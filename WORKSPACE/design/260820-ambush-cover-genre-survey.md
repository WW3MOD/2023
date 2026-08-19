# Ambush, concealment and cover — what the genre already learned

**Date:** 2026-08-20
**Status:** research survey. No code, no recommendations that have been costed. Read §9 first if you only read one section.
**Researched against:** `main @ 4bb3fae9`.

Organised **by mechanic, not by game**, so competing answers to the same problem sit side by side.

---

## 0. How to read this, and how much to trust it

Every number below came from community stat extraction, modding files, or game manuals. **Almost none of it comes from developer documentation**, because these studios do not publish their tuning. Where sources conflict I say so inline rather than picking the tidier number. Treat the *shapes* as reliable and the *magnitudes* as indicative.

Three things worth knowing before you start:

- **Nobody has solved the question the user is actually asking.** "Can the enemy see me right now?" is unanswered in Wargame, WARNO, Steel Division, Company of Heroes, and Gates of Hell. Four studios, fifteen-plus years, and the state the player most wants is the one state nobody draws. It is the most-requested missing feature on the WARNO forums and gets a flat "No it does not" from the community when asked. This is not a gap because it is hard — it is a gap because everyone modelled the *simulation* of concealment and then stopped.
- **The one game that did answer it is the oldest and the most 2D.** Close Combat (1996) puts a colour-coded English sentence under every soldier — `Hiding`, `Ambushing`, `Cowering` — and colours it by *whether the soldier is obeying you*. It runs on a cell-based 2D map with no 3D geometry, which is to say: on approximately this engine.
- **This project already has more of the simulation than the survey assumes.** `UnitStance.Ambush` ships, `AmbushTactics` has a five-trigger spring precedence, `Detectable` carries a graded visibility tier, prone grants +1 concealment tier, and forest density/shadow layers already influence order-time placement. It also already has a *predictive* concealment readout — `^DetectableRangeCircles` draws a per-tier ring at the radius where the unit becomes detectable (`infantry.yaml:751+`). What it does not have is a **state** readout: nothing on screen says "this unit is currently concealed" or "this unit is holding fire." That is exactly the split §3 finds across the whole genre — everyone builds the predictive tool and nobody builds the state.

---

## 1. Concealment: what is the unit of hiding?

Three families, in ascending order of how well they serve an infantry-ambush fantasy.

### 1a. Binary cloak — C&C / Red Alert / OpenRA stock

An actor is invisible or it is not. `Cloakable=yes` and any enemy without `Sensors=yes` cannot see it. OpenRA's own `Cloak.cs` is the same model: `InitialDelay` 10 ticks, `CloakDelay` 30, and `UncloakOn` as a flags enum defaulting to `Attack | Unload | Infiltrate | Demolish | Dock`. Shipped RA values: submarines `CloakDelay: 50`, phase transport `175`, TS `^Cloakable` `90` — 2s / 7s / 3.6s at 25 ticks/sec.

**Why it is the wrong model here**, argued best by Wayward Strategy's *Fixing Stealth in RTS*: binary stealth is *parasitic*. It "only plays within its own ecosystem," so "every faction still needs detectors," and the game collapses to a yes/no question — if you have a detector, stealth is nearly worthless; if you do not, it is unanswerable. The author's objection is explicitly about interactivity, not balance.

Dawn of War 2 gives the cleanest numeric proof of the failure. Non-detector units have a "keen sense" reveal radius of ~5 while their sight radius is 40–55; dedicated detectors run 30–40. There is no middle. GameReplays notes the non-detector radius is "so pitifully small that infiltrated units can sometimes charge directly into melee combat without being detected first." Army composition decides the outcome, not positioning.

**Important engine note:** OpenRA already ships typed detection — `DetectCloaked` uses a `BitSet<DetectionType>` and `Cloak` grants a `CloakedCondition`. Binary stealth is a *content choice in this engine, not an engine limit.*

### 1b. Positional stealth — AoE3, C&C3

Same boolean, different contract: stealth breaks on attack and is used for *positioning*, not invulnerability. The AoE3 Jaguar Prowl Knight moves slower while stealthed and is "always killable when fighting." Wayward's contrast is the useful one: Dark Templars use stealth as invulnerability; Jaguar Knights use it as mobility. C&C3's Stealth Tank went this way too, and detection moved to base defences rather than dedicated units, so "you don't just auto-lose if you didn't build the detectors."

### 1c. Graded contest — Eugen (Wargame / Steel Division / WARNO)

Detection is a contest of two per-unit scalars, terrain-modified. Not a raycast. Crucially **LOS and concealment are separate systems** — a thin treeline can fail to block LOS while still granting concealment.

WARNO's community-derived table: six optics tiers (Bad ×0.4, Mediocre ×0.6, Normal ×1, Good ×1.2, Very Good ×1.7, Exceptional ×2.2) against four stealth tiers (Bad default, Mediocre ×1.5, Good ×2, Exceptional ×2.5), with terrain multiplying the target's stealth — Forest ×2.75, Building ×3.75, Ruins ×3.75. Detection range ≈ base sight × optics ÷ (stealth × terrain).

> ⚠️ **Sources disagree on the terrain numbers.** A second community breakdown gives forest/building +3 and ruins +2.5; a third player says "2 or 2.5, iirc." No official Eugen table was found. The *shape* — a divisive terrain multiplier on the target's stealth — is solid; the constants are not.

Wargame: Red Dragon's modding manual gives real internals: `UnitStealthBonus` is a float with named tiers (Poor 1.0 is the default for 1,349 units; Exceptional 3.0 for Spetsnaz VMF and the F-117), and `OpticalStrength` is documented as "used to determine whether a unit can see enemy units in cover."

**This is the family this project is already in.** `Detectable.Vision` (default 2) plus `IDetectableAddativeModifier` contributions, clamped to `[1, MapLayers.VisionLayers-1]`, is a graded tier contest against per-cell vision layers. Prone adds +1 tier (`DetectableAddativeModifier@Prone`). The genre precedent says this is the right family; the genre also says the constants will need tuning by feel, not by derivation.

---

## 2. Cover: what is the unit of protection?

The user is right to hold concealment and protection apart. **Close Combat's 1997 manual states the split more clearly than any modern game does:** *"Concealment hides you from the enemy; protection keeps you from getting injured or killed."* High grass conceals well and protects badly. Every game that blurs these two produces a system players misread.

### 2a. Per-position, per-model — Company of Heroes

The unit of cover in CoH is **a position, evaluated per soldier model**. A six-man squad performs six independent cover evaluations at once. This single structural fact is the root of most of CoH's legibility failures.

- **Types:** green (heavy — sandbags, walls, trenches, craters), yellow (light — bushes, fences, rubble), grey (none), **red negative** (open ground, mud — *worse* than nothing). Garrison and trench are separate table entries.
- **Directional, but not uniformly.** Per in-game tooltips as reported by players: yellow is non-directional, red is non-directional, **green is directional** — and craters are the exception, giving green omnidirectionally. A squad in green sandbags shot from the flank gets **nothing**.
- **Effects are per-weapon, not global.** Cover is a `cover_table` carried by each weapon. A conscript Mosin against heavy cover: accuracy ×0.5, damage ×0.5, penetration ×1.0, **suppression ×0.1**. An HMG42 against light: suppression ×0.5, accuracy ×0.5, damage ×1.0. Trench was quoted at accuracy ×0.2 / damage ×0.3 / suppression ×0.
  > ⚠️ The widely-repeated "yellow = 25%, green = 50%" is **not confirmed** — one dump shows `tp_light` at damage ×1.0. Because the table is per-weapon, *any single global percentage is wrong.* Cover reduces accuracy, damage and suppression **independently and by different amounts**, and never penetration.
- **The headline effect against MGs is the suppression multiplier, not the damage multiplier.** That is worth internalising: in CoH, cover's job is mostly to stop you being pinned.
- **Negative cover also slows suppression recovery** (quoted 0.008/sec base, 0.004 red, 0.02 yellow, 0.04 green — one forum post, unverified) and applies vehicle speed penalties (confirmed indirectly by a Relic bugfix mentioning "speed modifiers for negative cover and mud").
- **Cover is negated at very close range** (~5m in CoH2), garrisons excepted. Grenades, flamethrowers, snipers and airstrikes bypass it. Flamethrowers are *worse* against targets not in cover.

### 2b. Per-terrain-patch — Eugen

No per-object cover at all. Terrain is a zone with a flat multiplier: forest 50% damage reduction, building 70% (the in-game "90%" was a documented error), ruins 25% with a smaller footprint. No directionality. Buildings block LOS entirely and cap at four infantry squads.

Note the deliberate inversion in WARNO's balance: **forest conceals better, ruins protect better.** The two axes are tuned to trade against each other, which is what makes the choice interesting.

### 2c. Cover classes with directionality baked into the class — Close Combat

The most transferable model for a 2D cell game, because directionality is a *property of the cover type* rather than a runtime geometry test:

| Class | Example | Concealment | Protection |
|---|---|---|---|
| **Linear** | walls, trenches | — | good perpendicular, poor parallel |
| **Light** | grass, bushes | good | very poor |
| **Medium** | trees, crests | — | frontal only, nothing from side/rear |
| **Heavy** | buildings, foxholes | excellent | excellent, multi-angle |

Four classes, each carrying its own directional rule. No per-model evaluation, no runtime arc maths — the class *is* the answer.

**Is directional cover worth it?** The honest finding from CoH is not that players reject directionality — it is that players systematically **misread three invisible conditions**: direction, close-range negation, and per-model coverage. Directional cover is not too complex; it is too *unlit*. In a 2D top-down cell game a facing arc can actually be *drawn on the cell*, which CoH cannot do. That inverts the cost/benefit: directionality is cheaper to make legible here than it is in 3D.

---

## 3. Legibility: "am I hidden?" — the core question

The inventory of what each game actually puts on screen.

| Game | Concealment readout | Honest verdict |
|---|---|---|
| **CoH 1–3** | Model renders semi-transparent/shimmering **to its owner only**. Camo passive tooltip. | No "the enemy cannot see me" state. Inference from proximity only. |
| **WARNO / SD2** | **Blinking unit label = in cover.** That is all it means. | In Wargame: Red Dragon the blink *stopped when you were spotted* — the compromised indicator. WARNO **removed** that, and players describe the removal as "a bug they finally fixed." Asked directly whether WARNO signals detection: *"No it does not."* |
| **Gates of Hell** | **Eyelash cursor** over concealing terrain while dragging a move order; **golden shield** on the unit marker after. | The shield's meaning is disputed in-community — some read it as damage cover, not concealment. Three weak signals, none authoritative. |
| **Total War** | **Pine-tree icon on the unit card** = hidden. Campaign map shows "Ambush Chance: x%" on hover. | The percentage is **dishonest**: it omits the enemy's ambush-defence stat, so a displayed 100% still fails against a lord with 110% defence. |
| **Close Combat** | A **colour-coded English state line** per soldier: `Hiding`, `Ambushing`, `On Watch`, `Holding Fire`, `Suppressed`, `Pinned`, `Cowering`, `Can't See`, `Too Close`, `No Target`… | The only game in the survey that names the state in words. See §3a. |
| **This project** | **A per-tier concealment ring** — `^DetectableRangeCircles` grants one `WithRangeCircle` per `visibility-N` condition, radius shrinking as concealment improves (28c0 at tier 1 → 16c0 at tier 5). | Genuinely an Eugen-class *predictive* readout, and better integrated than Eugen's, since it needs no tool invocation. But it is `Alpha: 25` grey `888888`, `Visible: WhenSelected`, allies only — a faint ring on the selected unit. **No state readout of any kind**: nothing says concealed / spotted / holding fire. |

### 3a. Close Combat's colour rule — the single best idea in the survey

The soldier state text is colour-coded by **whose decision produced it**:

> **green = obeying your order · red = countermanding your order · white = no order given, acting on local conditions**

That is a legibility answer to a problem no modern game solved. It does not tell you *what* the unit is doing — the word does that — it tells you *whether what you asked for is what is happening*. A held ambusher reading a green `Ambushing` is working as ordered. The same unit reading a red `Firing` has broken discipline and you know instantly, without watching it.

The full CC2 state vocabulary (verbatim from the *A Bridge Too Far* manual, Soldier Monitor → Details) is directly stealable: Moving · Resting · Loading · Aiming · Firing · Taking Cover · Assaulting · **On Watch** *("looking for targets")* · **Holding Fire** *("has loaded weapon and sees a target but chooses not to fire")* · **Suppressed** *("takes cover but will still fire")* · **Pinned** *("hides more than he shoots")* · **Cowering** *("pinned down but rarely fires and refuses to move")* · Routed · Panicked · Firing Blind · Out of Ammo · **Can't See** · Friend Block · Crawling · **Ambushing** · **Hiding** · **Too Close** · Conserving · Separated · Stunned.

Note how much diagnostic work `Can't See`, `No Target`, `Friend Block` and `Too Close` do. They answer *"why isn't my unit shooting?"* — the question that otherwise makes a hold-fire system look broken.

Close Combat also colours **order dots** on the 2D map by order type — Move blue, Move Fast purple, Sneak yellow, target fire red, Smoke grey, **Hide green**; Defend is default and draws no dot. Clicking a unit traces a line to its dot. And its LOS readout is a **coloured target line**: bright green = can see target, dark green = firing through obstructions, **red = cannot fire, the order will be ignored**. A later title added a configurable soldier-outline colour for cover/morale — with a recorded player complaint that the game *felt* unreadable until he discovered the option existed. **A legibility feature that is off by default is not a legibility feature.**

### 3b. CoH's destination preview — steal the idea, not the implementation

Hovering the cursor over ground shows a **cluster of coloured dots, one per soldier, at the exact positions each man will occupy**, coloured green/yellow/red individually. Note the precise design: *the cursor itself is not tinted; the ground is annotated.*

The genuinely valuable idea is **the cover state of where you are about to be, shown before you commit, at the granularity the simulation uses.**

CoH then broke it by shipping a *second, coarser* indicator: a squad-level shield that goes green **when more than half the models are covered**. The community's own standing advice is to distrust it — *"do not take the shield icon as an indication; hover your cursor over the ground and look at the dots, that's what counts."* Conscripts behind a small sandbag show green and get pinned. Barbed wire placed in front of sandbags **denies green cover entirely** by keeping models from reaching the minimum proximity, and the icon does not say so.

**The lesson is not "add a preview." It is "ship exactly one indicator, at simulation granularity."** Two indicators at different granularities is worse than one, because players learn which one lies and then have to remember which.

### 3c. Eugen's LOS tool — predictive, not stateful

Eugen's best legibility work is a *tool*, not a display. The LOS tool (eye icon, or hold **C** to project from the cursor) shades the vision circle: greyed = cannot see, white = true LOS, **blue = "intermediate — your unit is unable to detect any potential enemies there."** Critically, you can **toggle a simulated enemy stealth tier and the overlay redraws** — showing where you could shoot versus where you could actually *spot* a target of that stealth class. It defaults to Bad stealth. SD2's version accounts for elevation.

This directly addresses "spotting range ≠ engagement range," which in Eugen games is a deliberate gap — a tank may see roughly half as far as it shoots.

Its failure is **discoverability**, and that is the recurring Eugen complaint rather than depth. Broken Arrow drew the gripe that "there's no range or sight line for your units to see how far they can shoot," with the LOS display hidden behind an undocumented Alt hotkey.

---

## 4. Ordering an ambush, and knowing it is armed

### 4a. The orders that exist

| Game | Order | Semantics |
|---|---|---|
| **SD2 / WARNO** | **Return Fire** (`Z`) | Will not initiate; fires only if fired upon. Explicitly does *not* trigger on merely being seen. |
| **SD2 / WARNO** | **Hold Fire** (`H`) | Hard-off per weapon system. Works on a whole selected group. |
| **SD2 / WARNO** | **Efficient Shot** | Smart: hold until *both* hit chance and penetration exceed a threshold (originally 40%/40%, later exposed as two options sliders). Pitched explicitly for "an AT gun lying in ambush." |
| **CoH** | **Hold Fire** toggle | Exists precisely because otherwise the unit auto-attacks at max range and blows the ambush. Sticky and famously forgotten. |
| **Combat Mission** | **Target Arc** + **Hide** | See §4c — the richest answer. |
| **Close Combat 2** | *(none)* | **Ambush is emergent, not ordered.** See §4b. |
| **Total War** | **Ambush stance** (campaign) | Army renders crouching. Cannot recruit while stanced. You must *move into* position — stancing where you are already seen does not work. |

### 4b. Close Combat: ambush as a composition, not a command

CC2 (1997) has seven orders — Sneak, Move, Move Fast, Fire, Smoke, Defend, Hide — and **no ambush order**. `Ambushing` is a *reported state*, not a command. The manual's instruction is: *"Deploy your troops in good cover and order them to hide. When the enemy is exposed and within close range, fire on them. (If the enemy comes within 30 meters, your troops will fire on their own.)"*

Two details worth lifting:

- **Postures chain automatically on arrival.** Sneak defaults to Hide when the unit gets there; Move defaults to Defend. The player does not have to remember a second click.
- **The series later added an explicit Ambush mode** (hotkey M) with sneaking troops auto-transitioning to ambush on arrival — so the design moved *from* emergent *to* explicit over time. That trajectory is evidence: emergent ambush was legible enough to ship, and explicit was still judged better.

### 4c. Combat Mission's Target Arc — the player authors the state

The player drags a wedge from the unit: two left-clicks define direction and range, with the range in metres displayed while placing. **Shift** gives a full 360° circle — radius only, no direction. Rendered orange, unit-relative, **and visible only while that unit or waypoint is selected**. There is no "show all arcs" view.

Semantics, from the CMSF2 manual: a unit with a Target Arc *"will usually attack only enemy units that are located in the designated area, unless it feels immediately threatened by an enemy outside its arc."* It is not only a hold-fire rule — it **reorients the unit**. Vehicles rotate turrets to arc centre, infantry shift facing, which cuts acquisition delay. **That is the "targets pre-acquired" property the user wants, and it falls out of the arc for free.**

The manual gives all three uses explicitly: concealment preservation (*"useful if you do not want your anti-tank weapons to reveal themselves too soon against enemy scouts"*), hold-fire discipline for OP/FO teams, and ammo conservation.

**The veteran idiom is the most transferable single line in this survey:** Shift-arc at **~50 m**, so *"your men will defend themselves against nearby enemies that would have spotted them anyway, but won't start shooting at distant targets."*

Read what that does. The radius becomes a **"how close before I break concealment" dial**, and the 360° form **removes the flanking failure mode entirely**. One number, one gesture, no geometry — and it is exactly the user's forest-road fantasy expressed as a player input.

Ambush drill in CM is `Hide` + short arc; hiding units *"will unhide as soon as they know a spotted enemy enters their covered arc."*

### 4d. How you know the order is armed

**Eugen ships three redundant channels for one bit**, which is the right instinct:
1. the state appears as **Return Fire** in the Orders window;
2. the **Return Fire button is brightly highlighted**;
3. **the unit's on-screen label changes colour**.

WARNO adds a **white letter on a red field in the icon's upper-left** for smart orders — S = Seize, H = Hold, AA = Fire at Will, AC = Counter-Battery, AD = Defensive Fire. Seize auto-flips to H with a blue dotted defensive circle when threats clear. **That letter-badge-on-icon is the most portable armed-state idea in the survey.**

Note also WARNO's radio-wave glyph for GSR recon when halted, which means "+1 Optics while stationary." A *passive stat change gets its own persistent glyph.* That is directly applicable to prone granting +1 concealment tier here, which currently draws nothing.

Combat Mission's answer is different and worth stating plainly: **CM's real solution to "the player can't tell what state the unit is in" is to make the state player-authored.** You drew the wedge, so you know it is there. That is why CM's dominant failure mode is *forgetting*, not *not knowing*.

---

## 5. Hold-fire discipline without looking broken

The problem: a unit that declines to shoot is indistinguishable from a unit that is bugged.

**Close Combat's answer is the best one and it is cheap: name the reason.** `Holding Fire` is defined as *"has loaded weapon and sees a target but chooses not to fire"* — the readout explicitly confirms the unit **has** a target and **chose** not to engage. Alongside it sit `No Target`, `Can't See`, `Friend Block`, `Too Close`, `Out of Ammo`, `Conserving`, `Gun Broken`, `Bad Shot`, `Can't Target`. Nine distinct answers to "why isn't it shooting?" The unit never looks broken because the game tells you which of the nine it is.

**Eugen's answer is a threshold the player can tune** — Efficient Shot's accuracy and penetration sliders. Elegant in principle; see §6 for why it backfired.

**Combat Mission's answer is a safety valve**: an arc-holding unit still defends itself *"if it feels immediately threatened,"* and `Hide` units may unhide when fired on or approached extremely close, *"depending on that unit's experience, morale and leadership."* Discipline is not absolute, and the softening is stat-driven.

**Firing breaks concealment everywhere.** SD2's framing: *"even if hidden, a unit will be seen by the enemy as soon as it uses its weapons — hence the importance of Hold Fire."* Wargame modulates it with a per-ammunition **`Puissance`** value — *"a stealth-negating multiplier for firing noise, spanning 1 for silenced weapons to the upper two digits."* WARNO keeps it: larger weapons make more noise. **A per-weapon reveal magnitude is a cheap, high-value refinement over a boolean reveal**, and it makes silenced/small-arms ambushes play differently from an ATGM launch.

**Alpha-strike bonus: mostly does not exist as a discrete stat.** I could not confirm any named "ambush" first-shot modifier in SD2 or WARNO — the advantage is emergent from stacking close-range multipliers (≈+400% at 1% of max range), a calm unsuppressed shooter (0% penalty vs −45% stressed), and successive-hit bonuses. CoH is the exception: most units get a significant short-duration damage bonus firing out of camo, and **snipers explicitly do not**. Combat Mission and MoW also give no confirmed multiplier — the edge is pre-acquired facing and side/rear shots. **The genre's verdict is that ambush should pay off through the existing range/facing/surprise maths, not a bespoke bonus number.**

---

## 6. Failure modes — the part worth more than the successes

Sorted by how likely each is to bite this project.

### 6a. The order un-arms itself
WARNO/SD2's own forum thread is titled *Hold Fire Problems*: **"units will now automatically take themselves off hold fire."** The accuracy/penetration sliders **override the explicit order** — AT guns and bunkers break cover for a good shot. The OP's line is the whole design thesis: *"i told you to hide."* Worse, **movement orders silently break Return Fire** (hunt, fast-move-attack). Nobody in the thread had a fix.

**The lesson: a smart threshold that can override an explicit order will be experienced as the game disobeying you.** If a "spring early on a good shot" heuristic exists, it must be visibly a *different order*, not a hidden modifier on the hold order.

### 6b. Wrong prey trips the ambush
The canonical Combat Mission thread: PIAT teams with a Target Armour Arc set for Panthers — the **halftracks entered first and consumed the ambush**. Related: an armour-arc unit "generally will not fire on anti-tank guns," and unarmoured trucks/Kübelwagen do not trigger armour arcs at all. Players' workaround is 10 m arcs.

Wargame's version: ATGM teams reveal themselves firing at scouts and die. **The counter is worse than the reveal** — an opponent watching a treeline will `Fire Pos` the launch origin: *"enemies don't need visual contact, they can see the origin point of the ATGM and Fire Pos it."*

**Both point the same way: a target-class filter on the ambush trigger is not optional.** "Spring on anything" wastes the ambush on a scout car; "spring only on armour" misses the escort.

### 6c. The indicator lies
Two independent cases, both instructive:
- **CoH's cover shield** reads green when >50% of models are covered, so one exposed model gets the squad suppressed while the icon says safe.
- **Total War's ambush percentage** omits the enemy's ambush-defence stat, so a displayed 100% fails against a defended lord. Players report a single tile at 80% surrounded by 40% tiles, and there are actually *two* rolls — pre-emptive discovery and the spring — with the displayed number describing only the second.

**A confident indicator that is wrong is worse than no indicator**, because the player builds a plan on it. If a number cannot account for the enemy's contribution, do not show a number — show a state.

### 6d. Hiding blinds you
Combat Mission's `Hide` degrades the unit's own spotting. Documented case: a hidden bazooka team failed to see a PzIV that the same team, unhidden, spotted and shot. There is also a documented vehicle-with-arc failing to see a 40-tonne tank at under 10 m in clear weather — devs say intentional. **A concealment state that suppresses your own detection will produce ambushes that never fire, and the player will read it as a bug.**

### 6e. Units break cover on their own
CoH's squads reposition out of cover mid-fight and die. This is emergent from Relic's own squad AI: models "leapfrog" toward cover and dive for walls to look intelligent. Relic's formation writeup documents having to add a **"virtual leader"** because when the real leader veered toward cover the whole squad followed him off the path. Separately, one out-of-range model drags the whole squad forward; the community workaround is to attack-move onto ground *beside* the cover rather than right-clicking the enemy. And Relic shipped an AI bug where squads **failed to take cover during combat entirely**.

**Relevant here because this project already has a continuous idle repositioner.** `StancePositioningExecutor` explicitly **opts out for Ambush/HoldFire** (`StancePositioningExecutor.cs:318`) — an ambusher is placed once and left. That is the correct call and it is already made; the survey's value here is confirming that the alternative is the single most-complained-about behaviour in the genre's flagship cover game.

### 6f. The overwatch gap
Recurring in WARNO and Broken Arrow: if recon infantry *and* its supporting vehicle are both on hold-fire, the vehicle will not help when the infantry is engaged; if it is not on hold-fire, it blows both units' cover. The requested feature is *"hold fire **unless friendly unit X is engaged**."* **Nobody in the genre has shipped a group-scoped ambush trigger**, and it is the obvious gap. The user's "the whole group fires at once" framing is already reaching for it.

### 6g. Ambushes that never trigger
- WARNO: two Abrams bulldozed veteran motorstrelki deep in forest; the infantry never fired RPG-7s or Metis and the tanks spotted them first.
- Rome II: armies pass through ambushes, resolving as the *enemy* attacking — the roll simply failed, with users calling the stance "a waste of time."
- Warhammer III: a tested mountain-pass ambush failed twice because the AI stopped just outside the zone of control, plausibly because the ambushing army physically blocks the tile.

### 6h. Borg spotting makes the payoff feel unearned
The inverse complaint, and a sharp one for this design. Many Eugen players think spotting is too *simple*: one unit spots and every unit instantly fires with full accuracy. The requested fix is a **"grey status"** — you know an enemy is there but your units cannot engage until they personally acquire it, with full engagement after 2–3 s — plus **last-known-position icons**.

**This bears directly on the user's "targets pre-acquired" goal.** Eugen gives simultaneous full-accuracy fire for free, and players find that unearned. Combat Mission goes the other way — spotting is per-soldier with a random element, so ambushes *trickle* rather than crash. The user wants the crash. The genre's warning is that a crash which costs nothing to set up reads as a cheat; the acquisition should be *paid for* by the holding time.

### 6i. Discoverability, not depth, is what kills these systems
Eugen's in-game manual is called misleading; WARNO lacks Red Dragon-era guides; Broken Arrow's LOS display sits behind an undocumented Alt hotkey; the Close Combat player who found the game unreadable until he discovered soldier outlines could be enabled; MoW **removed the enemy view-cone overlay** and part of the community *did not want it back* because it fired accidentally and cluttered the screen.

That last one is a real warning against the obvious solution: **a persistent cone/overlay is not automatically a win.**

---

## 7. Enemy information states — contact vs identified

Under-appreciated, and cheap here. Several games split "I know something is there" from "I know what it is."

- **Wargame: Red Dragon** has a dedicated field `PorteeVision` — *"maximum range at which you can see an unidentified ground unit"* — and a `TGhostManagerModuleDescriptor` that *"governs the blacked-out display for a unit spotted but not identified."* Drawn as a **solid black silhouette**: you read the outline and guess. Closer range or better optics resolves it into a full unit card.
- **Combat Mission** has **contact** (question mark, *opacity encodes confidence*, **never auto-fired at**) versus **confirmed** (auto-engaged). Information propagates through the C3 network — no borg spotting.
- **Close Combat 2** degrades the *information panel* rather than the map: selecting an enemy team shows *"blanks or question marks in some areas"* because *"your men have not been able to determine certain information about the enemy,"* improving over time, with high ground and scouts helping and tanks being the worst observers.
  > ⚠️ I could not confirm a "?" contact marker on the CC1/CC2 **map**. The fading **"Last Spotted Here"** ghost is documented as a *later* addition (a *Panthers in the Fog*, 2012, developer interview) — do not attribute it to 1996.
- **Close Combat treats sound as an explicit intelligence channel**: per-weapon sounds, radioed intentions, panicked shouting as morale drops.

**Two ranges — contact and identity — is a cheap richness win**, and it gives the ambusher something to do with partial information rather than a binary.

---

## 8. What needs 3D, and what does not

**Does not survive translation to a 2D cell grid with one actor per unit:**

- **Per-model cover within a squad**, and therefore CoH's entire "half the squad is exposed" failure class. One actor = one cover state. **This deletes CoH's single biggest legibility problem for free — do not reintroduce it.**
- CoH's **hover dot cluster**, which only means anything because there are N models. The analogue here is one dot, which is strictly clearer.
- **Per-soldier spotting rolls** (CM's "one man sees, the squad doesn't" texture), and relative-spotting asymmetry *within* one actor.
- **Continuous positional cover** — CoH evaluates arbitrary world positions and requires a model to be adjacent and oriented. On a cell grid cover is a cell property: unambiguous, and renderable as a persistent terrain tint, **which CoH cannot do.**
- Prone breaking LOS over a hedgerow by ~1 m of height; per-crew-position facing awareness; elevation upgrading effective cover; Gates of Hell's "rotate the camera to the enemy's side and check the silhouette" trick; Direct Control.

**Ports cleanly:**

- The **arc abstraction** — a wedge is `(facing, half-angle, radius)` and needs no raycast beyond existing cell visibility. Including the 360°-radius-only form.
- **Radius as an engagement-distance dial**, and the armour-arc **target-class filter**.
- **Directional cover as a property of the cover class** (Close Combat's four classes), rather than a runtime geometry test.
- Negative cover, suppression multipliers, close-range negation, per-weapon reveal magnitude (`Puissance`), hold/return/free-fire stance sets.
- **Contact vs identified** as two ranges, with opacity or silhouette encoding confidence.
- Facing-reduces-acquisition-delay — a per-actor scalar, no geometry needed.
- **Persistent on-map arcs.** CM cannot draw all arcs at once because they are 3D and unit-relative. A 2D grid can render every friendly arc persistently — **which converts CM's dominant complaint (forgotten arcs) into a solved problem for near-zero cost.**

---

## 9. Verdict: steal, avoid, out of reach

### Steal

1. **Close Combat's colour-coded state line.** A named state per unit — `Hiding`, `Ambushing`, `Holding Fire`, `On Watch`, `Can't See`, `Too Close` — coloured **green = obeying your order, red = countermanding it, white = acting on its own.** It is the only mechanism in the survey that answers both "what is this unit doing?" and "is that what I asked for?", it was built for a 2D top-down cell game, and its diagnostic vocabulary (`No Target`, `Can't See`, `Friend Block`) is what stops a hold-fire unit from looking broken. **Everything else in this list is secondary to this.**
2. **Combat Mission's radius-as-a-dial, in its 360° form.** *"Shift-arc at 50 m so your men defend themselves against enemies that would have spotted them anyway, but won't shoot at distant targets."* One number, one gesture, no flanking failure mode, and it makes the concealment-break threshold **player-authored** — which is why CM players always know the state. Add the armour-arc lesson (§6b): the trigger needs a **target-class filter**, or halftracks eat the ambush meant for tanks.
3. **Eugen's three-channel armed-state signal** — orders-panel text + highlighted button + **letter badge on the unit icon**. Redundant on purpose, and the badge is the portable part. Extend it the way WARNO does for GSR recon: a *passive* stat change gets its own persistent glyph, which is exactly what prone's existing +1 concealment tier currently lacks.

### Avoid

1. **Two indicators at different granularities.** CoH's per-model dots and squad-level shield disagree, so the community's standing advice is to ignore the official one. **Ship exactly one concealment indicator, at simulation granularity.** This project's one-actor-per-unit model makes that free — do not spend the freedom.
2. **A smart threshold that silently overrides an explicit order.** Eugen's Efficient Shot sliders un-arm Hold Fire, movement orders break Return Fire, and the forum thread is titled *"i told you to hide."* If early-spring heuristics exist they must be a visibly *different order*, never a hidden modifier on the hold order. Note this project already has a five-trigger spring precedence in `AmbushTactics` — **triggers 3–5 (BestStrikeDegrading / Saturation / Overrun) are exactly the class of heuristic Eugen players experienced as disobedience.** They need to be legible as *reasons*, or they will read as the same bug.
3. **A confident number that cannot account for the enemy.** Total War's ambush percentage omits enemy ambush defence and is therefore a lie at the moment it matters most. If the enemy's optics cannot be known fog-legally, **show a state, not a probability.**

### Out of reach

Per-model cover within a squad and per-soldier spotting rolls (one actor = one state — and that is a *gain*, since it deletes CoH's worst failure mode). True LOS in the CoH sense — this engine has cell visibility layers and a baked `ShadowLayer`, not raycasts. Elevation-modified cover. Destructible cover degrading mid-fight is *possible* but needs explicit tile-state changes and a visible tile repaint, so it is a project, not a feature.

### The single best idea for making concealment legible

**Name the state in words, on the unit, coloured by whether the unit is obeying you.**

Not an icon, not a tint, not an overlay — a short readable state, because the player's question is not "what colour is my cover" but "is my plan happening." Close Combat proved it on a 2D top-down cell map in 1996; every richer 3D game since has more simulation and less legibility, and four studios still cannot tell you whether the enemy can see you.

The hook already exists here: `Detectable` grants a `visibility-<N>` condition per tier (`Detectable.cs`, `VisionDetectableConditionPrefix`), the values are already clamped and synced, `UnitStance.Ambush` is already a distinct stance, and `^DetectableRangeCircles` already consumes those conditions — so the wiring from simulation to screen is built and proven. **The state is computed, conditioned, and already drives one renderer. What it does not drive is a word.**

And note what the existing ring is, in the survey's own terms: it is the *predictive* readout (where I will be detected from), drawn at `Alpha: 25` grey, only while selected. That is the Eugen pattern **including the Eugen failure mode** — §6i's discoverability trap, and the Close Combat player who found his game unreadable until he discovered the outline option existed. A legibility feature at alpha 25 on selection-only is one a player can play for fifty hours without ever consciously seeing.

One caution against the obvious alternative: Men of War *removed* its persistent enemy view-cone overlay, and part of the community did not want it back — it fired accidentally and cluttered the screen. Persistent world-space overlays are not a safe default. A word on a unit is.

---

## 10. Sources

**Company of Heroes**
- https://www.coh2.org/topic/9182/infantry-cover-modifier — `cover_table` dumps
- https://www.coh2.org/topic/54631/received-accuracy — cover × received-accuracy stacking
- https://www.coh2.org/topic/107524/green-cover-suppression-and-some-issues/page/1 — suppression/pin thresholds
- https://www.coh2.org/topic/15629/the-problem-with-the-cover-system/post/136443 — repositioning out of cover
- https://www.coh2.org/guides/5732/company-of-heroes-2-basic-concepts-and-glossary — dots/shield vocabulary
- https://steamcommunity.com/app/231430/discussions/0/1653297026035094659 — "don't trust the shield"; barbed-wire cover denial
- https://steamcommunity.com/app/231430/discussions/0/666827247949395435/ — directional vs non-directional by type
- https://steamcommunity.com/app/1677280/discussions/0/3792632416034478440/ — CoH3 close-range negation
- https://companyofheroes3.wiki/guides/cover-and-combat/ — CoH3 cover dots
- https://companyofheroes.fandom.com/wiki/Scout_Sniper — ambush camo in cover, Hold Fire
- https://help.relic.com/hc/en-us/articles/39307744455571-Company-of-Heroes-3-Patch-Notes-Archive — floating cover-dot fix
- https://forum.arongranberg.com/uploads/short-url/rWod3K2KhNWcOEdjewnsLXQKU6A.pdf — Relic squad formation/AI writeup ("virtual leader")
- https://community.companyofheroes.com/coh-franchise-home/company-of-heroes-3/forums/1-general-discussion/threads/3090-hard-to-see-whats-going-on-visual-clarity-issue

**Eugen (Wargame / Steel Division / WARNO)**
- https://github.com/ResidentMario/wargame/blob/master/Wargame_Internal_Values_Manual.tex — `UnitStealthBonus`, `OpticalStrength`, `PorteeVision`, `TGhostManagerModuleDescriptor`, `Puissance`
- https://steamcommunity.com/sharedfiles/filedetails/?id=2727549821 — WARNO optics/stealth/terrain table, UI inventory
- https://steamcommunity.com/app/1611600/discussions/0/6015206486189650462/ — blinking icon = cover only
- https://steamcommunity.com/app/1611600/discussions/0/3803904729135452662/ — no spotted-indicator in WARNO
- https://steamcommunity.com/app/919640/discussions/0/1640915206447220480/ — *Hold Fire Problems*; the three armed-state cues
- https://steamcommunity.com/app/919640/discussions/0/1607148447828453470/ — SD2 smart orders
- https://steamcommunity.com/app/251060/discussions/0/358415206085465753/ — "Units not hiding"; Fire Pos counter
- https://steamcommunity.com/app/1604270/discussions/0/4635986813454960456 — Broken Arrow overwatch gap

**Combat Mission**
- https://ftp.matrixgames.com/pub/CombatMissionShockForce2/CMShockForce2BaseGameManual.pdf — Target Arc command text (primary)
- https://community.battlefront.com/topic/137825-some-thoughts-on-target-armour-arc/ — PIAT/halftrack ambush consumption
- https://community.battlefront.com/topic/96392-360%C2%B0-target-arc/ — the 50 m Shift-arc idiom
- https://community.battlefront.com/topic/120275-do-target-arcs-hide-ambush/
- https://community.battlefront.com/topic/137493-spotting-issues/ — point-blank spotting failures
- https://combatmission.fandom.com/wiki/Hide · /Spotting · /Tactical_AI

**Men of War / Gates of Hell**
- https://steamcommunity.com/app/244450/discussions/0/611702631207066545/ — view-cone removal
- https://steamcommunity.com/app/400750/discussions/0/3875967397711436079/ — "Does concealment actually work?"
- https://steamcommunity.com/app/400750/discussions/0/4040355933304900949/ — eyelash cursor, golden shield
- https://www.gamereplays.org/menofwarassaultsquad/portals.php?show=page&name=totw-concealing-antitank-guns

**Close Combat**
- https://archive.org/stream/CLOSE_COMBAT_II_A_BRIDGE_TOO_FAR/CLOSE_COMBAT_II_A_BRIDGE_TOO_FAR_djvu.txt — full CC2 manual: state table, colour rule, order dots, four cover classes
- https://www.manualslib.com/manual/103084/Microsoft-Close-Combat.html — CC1 reference manual
- https://steamcommunity.com/app/297750/discussions/0/1741100729968235966/ — "where is cover shown?"
- https://forums.matrixgames.com/viewtopic.php?f=10370&t=227489 — *Panthers in the Fog* dev interview, "Last Spotted Here"

**Binary stealth / classic RTS**
- https://waywardstrategy.com/2023/06/26/fixing-stealth-in-rts/ — Persistent vs Positional stealth
- https://github.com/OpenRA/OpenRA/blob/bleed/OpenRA.Mods.Common/Traits/Cloak.cs · `/DetectCloaked.cs`
- https://docs.openra.net/en/release/traits/
- https://modenc.renegadeprojects.com/Cloakable · /CloakStop · /CloakingStages
- https://www.gamereplays.org/dawnofwar2/portals.php?show=page&name=dawn-of-war-2-tip-of-the-week-infiltration
- https://steamcommunity.com/sharedfiles/filedetails/?id=736993545 — DoW2 detector radii
- https://us.forums.blizzard.com/en/sc2/t/the-ai-makes-stealth-pointless/11069 — detector saturation
- https://cnc.fandom.com/wiki/Garrisoning

**Total War**
- https://totalwarwarhammer.fandom.com/wiki/Ambush_stance
- https://steamcommunity.com/app/1142710/discussions/0/3482994087426003892/ — "60% still means 0%"
- https://steamcommunity.com/app/214950/discussions/0/1621726179576090726 — Rome II wasted ambushes
- https://rtw.heavengames.com/rtw/strategy/battle/ambushing/ — pine-tree hidden icon

---

## 11. Uncertainty ledger

Stated plainly so nothing here is quoted with more confidence than it earned.

- **CoH cover percentages are per-weapon.** No single global percentage is correct. The popular "yellow 25% / green 50%" is unconfirmed and contradicted by one stat dump.
- **CoH suppression-recovery rates** (0.008/0.004/0.02/0.04) rest on one forum post.
- **CoH camo detection ranges** (~12.5 detect vs ~35 sight) appear in CoH3/mod context; not confirmed as vanilla CoH2.
- **No Relic source** documents a deliberate cover-legibility redesign for CoH3. That premise is supported only by player reports and one bugfix line.
- **WARNO terrain multipliers conflict across three community sources** (×2.75/×3.75 vs +3/+2.5 vs "2 or 2.5, iirc"). No official table found.
- **A search summary claimed WARNO signals detection via icon opacity.** The cited guide was fetched and contains no such statement; two dedicated threads say no indicator exists. **Do not rely on it.**
- **No named first-shot/ambush damage bonus** could be confirmed in SD2, WARNO, Combat Mission or MoW. CoH's camo bonus is confirmed.
- **No last-known-position ghost** confirmed in SD2 — players request it as missing. **No "you have been spotted" sound cue** found in any game surveyed.
- **Combat Mission:** which of the two arc clicks sets radius vs angle is not stated in the manual; arc persistence across turns is inferred from the existence of `Clear Target`, not sourced. The CMBO standalone "ambush marker" could not be confirmed.
- **Close Combat:** no "?" contact marker confirmed on the CC1/CC2 map — the documented mechanism is information degradation in the panel. "Last Spotted Here" is a 2012-era addition, not 1996.
- **Gates of Hell:** the golden shield's meaning (concealment vs damage cover) is disputed in-community. Whether MoW foliage concealment is symmetric could not be confirmed.
- **C&C:** RA1's stock `CloakDelay` frame count unverified; wikis contradict each other on whether Tiberian Sun base defences detect Stealth Tanks; community disagrees on which RA2 units detect Mirage Tanks.
