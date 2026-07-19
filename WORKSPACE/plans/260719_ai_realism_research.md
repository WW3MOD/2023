# RESEARCH — Modern land-warfare patterns → Experimental-AI behaviors

> Purpose: a sourced mapping from how real modern land warfare is actually
> fought (north star: the Russo-Ukraine war and other recent conflicts, read
> through RUSI / ISW / CEPA / CSIS / service-doctrine analysis) to **concrete,
> implementable behaviors for the WW3MOD Experimental AI**. This is the research
> substrate for the primary AI goal recorded in
> [`DOCS/design/ai-realism.md`](../../DOCS/design/ai-realism.md).
>
> Scope: the **Experimental AI only** (gated `enable-ai-v2`). Normal / Rush /
> Turtle remain the untouched A/B control. Author date: 2026-07-19.
> Mode: EXPERIMENTAL.
>
> Read alongside: [`game-model.md`](../../DOCS/reference/game-model.md),
> [`supply-route.md`](../../DOCS/reference/supply-route.md),
> [`architecture.md`](../../DOCS/reference/architecture.md) (suppression /
> stances / InfluenceMap sections), and the design history in
> [`260719_experimental_ai_poi_strategy.md`](260719_experimental_ai_poi_strategy.md).

---

## 0. Framing — what "realistic" means here, and the WW3MOD grain

The goal is that a **bot-vs-bot match reads like a modern battlefield**: forces
that see before they shoot, disperse when observed and mass only at the decisive
point, kill mostly with fires, defend in depth with reserves rather than a
single line, preserve force instead of throwing it away, and fight for the
enemy's logistics rather than his flag. That serves immersion *and* win-rate —
the doctrine exists because it wins.

Two hard filters run through every translation below, because WW3MOD is **not
Red Alert** and the RTS format is not a wargame:

1. **The SR call-in economy replaces manufacturing.** There are no factories.
   Units are called in from off-map reserves and **walk/fly in from the map edge
   nearest the Supply Route** to the rally, then to the front. "Build a unit" =
   spend budget + eat travel time. "Rotate a unit out" = send it to the map edge
   to recover its budget. The SR is a fixed, indestructible beachhead, one per
   player; the only pressure on it is **contestation** (parking units in its
   10-cell circle slows the enemy's whole production) or **capture** (which flips
   it Neutral — denial, never a lane for us). This maps *unusually cleanly* onto
   the real war's biggest lesson: **logistics is the center of gravity.** See §8.

2. **The RTS format caps how far realism can go.** No fog-of-war for the AI's own
   omniscience unless we deliberately gate its perception; no true operational
   depth on a 128×128 map; casualties are HP not KIA/WIA; "morale" is the
   suppression meter, not a rout model. Where a doctrine concept doesn't survive
   these limits, this doc says so honestly (§10, "not worth pursuing").

Engine systems referenced throughout (all confirmed present — see
`architecture.md` and the POI plan): **suppression** (infantry 10-tier / vehicle
5-tier), the four **stance systems** (fire discipline, engagement, cohesion,
resupply), **`InfluenceMap`** (friendly/enemy density grid), **`FrontlineOverlay`**
(contested band), **`ThreatMapManager`** (exploration age + per-cell threat),
**`PoiMap`** (value×distance×threat scoring), **`PoiOffensiveBotModule`**
(score-floating multi-axis offense), **`PoiGoalGuard`** (per-unit commitment
ledger / shared blackboard), **`CaptureCoordinatorBotModule`**,
**`LayeredDefenceBotModule`**, **`PoiGarrisonBotModule`**,
**`MountedTransportBotModule`**, **`HelicopterSquadBotModule`**,
**`SupplyRouteContestation`**, **`CargoSupply`** / **`AmmoPool`** / supply trucks,
**`CohesionMoveModifier`**, **`Patrol`**, **`SmartMove`**.

---

## 1. Recon-strike / sensor-to-shooter loop

**(a) Real-world observation.** The decisive innovation of the Ukraine war is the
**reconnaissance-strike complex**: a cheap sensor (a drone) is networked to a
shooter (artillery / an FPV) so that *if you can be seen, you can be killed, and
you can almost always be seen.* Ukraine's GIS Arta / Kropyva / Delta apps became
the "Uber for artillery," compressing the kill chain to minutes; small armed UAS
even wrap sensor and shooter into one "compact kill chain." Russia's equivalent
(Strelets, the recon-fire complex) was doctrinally ambitious but executed poorly
early — the gap was *integration*, not sensors.

**(b) Why it matters.** Fires are only as good as the targeting that feeds them.
The side that closes the loop faster gets first-shot, destroys the other's
massing before it arrives, and turns every observed movement into a casualty. It
is the master pattern that *enables* dispersion (§2), fires-primacy (§3), and
interdiction (§8).

**(c) WW3MOD translation.** The engine already has the two halves — they are not
wired together. **Sensor:** `ThreatMapManager` holds per-cell threat + exploration
age; scout units and `HelicopterSquadBotModule` scout squads populate it.
**Shooter:** `PoiMap` scores objectives; `PoiOffensiveBotModule` vectors the
ground pool. The missing piece is a **sensor→shooter link**: a light "recon
ledger" that, when a scout/heli reveals a high-value enemy cluster or an
undefended income POI, **raises that cell's PoiMap score / spawns a transient
offensive POI** so the offense module reacts to *observed* intel rather than
static map knowledge. Add a standing scout/recon task (1–2 cheap units or a heli
in scout stance) that patrols ahead of each offensive axis and refreshes
`ThreatMapManager` there. This is the single highest-leverage realism upgrade
because everything else keys off "the AI acts on what it has actually seen."

**(d) Effort: M** — new `PoiMap` term reading `ThreatMapManager` freshness +
a standing recon assignment in `PoiOffensiveBotModule`; no new engine trait
(both substrates exist). L if we also gate the AI's own omniscient vision so
recon *earns* the intel (bigger, optional).

**(e) Expected effect.** Watchability **high** — you visibly see the AI scout,
find, then commit, instead of marching blind. Win-rate **medium-high** — acting
on fresh intel avoids walking into ambushes and lets fires hit massed targets.

---

## 2. Dispersion under observation, massing only at the decisive point

**(a) Real-world observation.** The battlefield is "nearly transparent" — >10,000
drones a day per side. The survival response is **dispersion**: guns separated by
>500 m, ammo caches split from firing positions, formations broken up so no single
strike is lucrative. Mass is now *transient* — you concentrate only at the moment
and point of decision, then disperse again. The 2023 Ukrainian offensive partly
failed because armor **bunched** in minefields under observed artillery.

**(b) Why it matters.** A death-ball is a single lucrative target; against fires it
is a liability. Dispersion trades concentration-of-force for survivability, and
the art is re-massing *briefly* at the decisive point. This is the exact opposite
of the classic RTS blob — and killing the blob is already a stated project goal.

**(c) WW3MOD translation.** Directly supported. `PoiOffensiveBotModule` already
does **score-floating multi-axis allocation** (the anti-death-ball). Layer
**cohesion doctrine** on top: units travel in **`Spread`/`Loose`** cohesion
(`CohesionMoveModifier`) while *approaching / under threat* (read
`InfluenceMap` enemy density or `ThreatMapManager` along the route), and switch to
**`Tight`** only in the final approach to the objective cell — "disperse to move,
mass to assault." This is a per-axis rule inside the offense module setting the
cohesion stance by threat state. Add a soft **anti-clump penalty**: if two axes'
centroids collapse within N cells, nudge one off (the `[v2-poi] disperse` telem
already measures `clumpRadiusCells`).

**(d) Effort: S–M** — cohesion stance is already a per-unit system with hooks;
the work is a threat-gated stance switch + a clump check in the offense module.
No new engine trait.

**(e) Expected effect.** Watchability **very high** — this is the most *visible*
realism change; armies that flow in dispersed columns and converge on a point
look like a real advance. Win-rate **medium** — survivability up vs any fires,
but weak micro can let dispersed units be defeated in detail (mitigate with the
mass-at-objective rule).

---

## 3. Artillery / fires as the main casualty producer + counter-battery

**(a) Real-world observation.** Artillery is the "God of War" — it, not maneuver,
produces most casualties; both sides institutionalized fires as the decisive
function (Russia issued 450+ manual updates to re-center artillery). Consequently
**counter-battery** and **shoot-and-scoot** dominate the gun's life: fire a short
program, then displace before glide bombs / counter-fire arrive; disperse guns and
use decoys to blunt counter-battery.

**(b) Why it matters.** If casualties come mostly from indirect fire, the force
that (i) has more effective fires and (ii) survives the enemy's fires wins the
attrition math. An AI that treats artillery as just another gun-line — parking it
static, letting it be counter-batteried — loses the central exchange of the modern
battle.

**(c) WW3MOD translation.** Two behaviors, both keyed to existing systems.
**Fires-first offense:** bias `PoiOffensiveBotModule` to position indirect-fire
units *behind* the lead axis element (stand-off), and bias **call-in composition**
(`UnitBuilderBotModule` / `AdaptiveProductionBotModule`) toward a healthy artillery
fraction — the suppression system already makes fire *suppress* (prone at >30) even
when it doesn't kill, so massed suppression opens the assault. **Shoot-and-scoot:**
a small per-unit rule for AI artillery — after firing a burst, displace a few cells
(reuse `Patrol`/move plumbing), so they aren't sitting ducks. **Counter-battery:**
when `ThreatMapManager` flags an enemy artillery signature, spawn a transient
high-priority `PoiMap` strike target for our own fires/air — the recon-strike loop
(§1) applied to the enemy's guns.

**(d) Effort: M** for fires-first + composition bias (mostly module weighting +
positioning); **M–L** for real shoot-and-scoot and counter-battery (needs a
per-unit fire-then-reposition behavior and an enemy-artillery classifier on the
threat map).

**(e) Expected effect.** Watchability **high** — visible gun-lines, suppression
pinning infantry, guns relocating. Win-rate **high** — this is the central
casualty exchange; getting it right is likely the biggest single competitive
lever, provided pathing doesn't strand displacing guns.

---

## 4. Defense in depth: prepared positions, reserves, elastic defense

**(a) Real-world observation.** The Surovikin Line stopped the 2023 offensive with
**layered** defense: forward security zone, main defensive belt, depth positions —
60% of effort on the first line, 20%/20% behind. Density of **minefields** (deepened
120 m→500 m) channelized and halted armor; the whole belt was covered by pre-planned
artillery. Crucially it was **elastic**, not rigid: forward troops fall back to
prepared depth positions, draw attackers into a mined kill-zone under pre-planned
fire, then **local reserves counterattack** to retake the original position before
the attacker consolidates.

**(b) Why it matters.** A single static line is brittle — one breach unravels it.
Depth + reserves means a penetration is absorbed, attrited, and reversed. Held
**reserves** are what convert a successful defense into a restored line; an AI with
no reserve concept spends its whole army on the line and has nothing to plug a
breakthrough.

**(c) WW3MOD translation.** WW3MOD's defense is currently static-only — this is a
named gap. Build it in layers on existing systems. **Depth:** `PoiGarrisonBotModule`
already sizes garrisons 1–3 by POI value; extend to place garrisons in **echelon**
(a forward screen POI + a depth POI) rather than one line, using `FrontlineOverlay`
to define forward vs depth bands. **Reserve pool:** carve a fraction of the pool
that `PoiGoalGuard` marks `reserve` — held near the SR/rally, *not* committed to
offense, released only when `InfluenceMap` shows an enemy penetration past the
forward band. **Elastic counterattack:** when the frontline (`FrontlineOverlay`)
shows a local breach, `LayeredDefenceBotModule` commits the nearest reserve slice
to *counterattack that cell* — exactly its reactive strength, but fed by a real
reserve instead of scraping idle units. **Prepared positions:** use `GarrisonManager`
ports/shelters and, at high-value held POIs, queue a defensive structure via the
`BaseBuilder` defense queue (`gtwr`/`pbox`). (Real minefields would need a new
AI-mine-laying behavior — see §10.)

**(d) Effort: M** for echeloned garrisons + a reserve fraction; **M–L** for the
full elastic counterattack loop (reserve release + retake logic). Reuses
`PoiGarrisonBotModule`, `LayeredDefenceBotModule`, `GarrisonManager`; the reserve
ledger is a small `PoiGoalGuard` extension.

**(e) Expected effect.** Watchability **high** — layered lines and
counterattacks that *retake* ground look dramatically more real than a static
blob. Win-rate **high** — a reserve is one of the biggest practical strength
upgrades; most losing AI games are unplugged breakthroughs.

---

## 5. Force preservation / culminating point

**(a) Real-world observation.** Offensives **culminate** — they run out of combat
power (Russia abandoned mechanized assaults in late 2024 as armor losses became
unsustainable; small infiltration teams "culminate quickly" from limited ammo and
reserves). Recognizing culmination and preserving force — not throwing good units
after bad — is what lets a side survive to fight the next phase. Losses are
permanent budget in this economy.

**(b) Why it matters.** In WW3MOD every lost unit is permanently lost budget. An AI
that fights to the death on a failing axis converts a local setback into a match
loss; one that recognizes culmination, breaks contact, and rotates damaged units
out preserves the budget to reconstitute.

**(c) WW3MOD translation.** Two behaviors. **Culmination detection:** in
`PoiOffensiveBotModule`, track per-axis combat power (unit count / total value);
when an axis drops below a viability floor *or* its loss-rate spikes (it's losing
the exchange), **retire the axis** (already has axis retire/hysteresis machinery) —
break contact, fall back toward a held POI or the SR rather than feeding it.
**Force preservation / rotation:** wire the **resupply/`Evacuate` stance** and the
economy's "rotate out to recover budget" — send low-HP / out-of-ammo units to the
map edge (`AmmoPool` + resupply stances already model this) to bank budget and
reconstitute, instead of dying in place. This is the *defensive* twin of §4's
reserve.

**(d) Effort: M** — per-axis combat-power tracking + a retire/fallback threshold
in the offense module, plus routing damaged units through the existing
`Evacuate`/resupply plumbing. No new engine trait.

**(e) Expected effect.** Watchability **medium** — subtle but real; you notice the
AI *not* suiciding, breaking off failed pushes. Win-rate **high** — directly
protects the budget, which is the whole economy; likely a large practical win.

---

## 6. Logistics as center of gravity (SR-deny + income interdiction + lane ambush)

**(a) Real-world observation.** "Modern armies collapse when they run out of
logistics, not weapons." Ukraine's HIMARS-era and 2026 "middle-strike / Logistical
Lockdown" campaigns target ammo depots, fuel, and command posts at operational
depth — the goal is that *the tank never reaches the front* for lack of fuel/ammo.
A logistics node, once found, is often dead in <24 h; this forces the enemy to
disperse and displace his supply rearward, degrading the *volume and speed* of his
fires.

**(b) Why it matters.** Attacking sustainment is higher-leverage than attacking the
force — it degrades everything the force can do, everywhere, over time. WW3MOD's
economy makes this *literal*: the SR is the enemy's link to reserves, income POIs
fund call-ins, and reinforcements are physically vulnerable while walking the lane.

**(c) WW3MOD translation.** This is the pattern WW3MOD models best, and much is
already scaffolded. **SR-deny / contestation:** make the **enemy SR contestation
circle a first-class offensive POI** (`PoiMap` already discovers/scores enemy SRs;
`PoiOffensiveBotModule` already has a `Pressure` action that parks units in the
10-cell circle to slow the enemy's whole production — verified live). Elevate its
score as the enemy weakens. **Income interdiction:** `PoiMap` already scores enemy
income structures; capturing/denying them starves call-ins. **Reinforcement-lane
ambush (net-new, high realism):** the game-model explicitly notes units walk a
known path edge→rally that *can be ambushed*. A recon-fed (§1) behavior that seeds
a small ambush force (in **`Ambush`** fire stance, in cover) across the enemy's
reinforcement lane interdicts units *before they reach the front* — the
"Logistical Lockdown" idea in miniature. **Supply-truck hunting:** enemy
`CargoSupply` trucks are a real logistics target; flag them high-priority for
raiders/air.

**(d) Effort: S** for SR-deny elevation + income interdiction (already coded —
mostly scoring weight). **M** for lane-ambush (new: derive the enemy lane from
their SR edge, seed an `Ambush`-stance force, hold via `PoiGoalGuard`).
**M** for supply-truck targeting.

**(e) Expected effect.** Watchability **high** — sieging the enemy SR and
ambushing reinforcement columns is dramatic and *reads as* modern interdiction.
Win-rate **very high** — contestation throttles the enemy's entire production;
this is the single most valuable spatial objective per the SR doc.

---

## 7. Infiltration small-unit assaults vs mounted maneuver

**(a) Real-world observation.** Under drone observation, Russia largely **abandoned
company/battalion mechanized assaults** for **2–4-man dismounted infiltration
teams** that slip through on foot / motorcycles, avoid the kill zone, and build
hidden forward positions — trading shock for survivability. Mounted maneuver
returns only where speed/mass can still pay (priority sectors), and even then is
detected and disrupted. The choice is **situational**: dismounted infiltration vs
mounted maneuver, picked by threat.

**(b) Why it matters.** One assault posture for all situations is wrong. Massed
mounted assault into an observed, prepared defense is how the 2023 offensive died;
dismounted infiltration is how you probe a saturated front without feeding the
kill zone. An AI that picks posture by threat state fights like a real force.

**(c) WW3MOD translation.** WW3MOD already has *both* halves; make the AI **choose**
between them. **Mounted maneuver:** `MountedTransportBotModule` (IFVs ferry
infantry) — use where `ThreatMapManager`/`InfluenceMap` show a *soft/open* axis
(speed pays). **Dismounted infiltration:** where the axis is *hot* (high threat /
prepared defense), dismount and advance small squads in **`Ambush`** fire stance +
**`Spread`** cohesion, hugging cover (the `ShadowLayer`/cover-seeking `Defensive`
stance roadmap helps), probing rather than charging. The selector lives in
`PoiOffensiveBotModule`: per-axis, read threat → pick mounted vs dismounted posture
and set the stance package accordingly. Small infiltration groups also pair
naturally with the recon loop (§1) — they *are* sensors.

**(d) Effort: M** — the posture selector + stance-package application per axis;
reuses `MountedTransportBotModule`, the stance systems, `CohesionMoveModifier`.
No new engine trait.

**(e) Expected effect.** Watchability **high** — visibly different behavior on hot
vs soft axes (columns dismounting to probe) is exactly the modern-battlefield feel.
Win-rate **medium-high** — right posture avoids feeding kill zones; small-group
micro is fragile, so gains depend on decent stance behavior.

---

## 8. Mission-command decentralization (per-axis autonomy)

**(a) Real-world observation.** Ukraine's edge is **mission command** — centralized
intent, **decentralized execution**: junior leaders act on intent without asking
permission, enabling fast reaction on a dynamic battlefield. Russia's rigid,
centralized C2 was slow, brittle, and repeatedly caught off-guard (the Siverskyi
Donets crossing disaster). Decentralization down to low echelons is a *strength
multiplier*, not just a style.

**(b) Why it matters.** A single central decider that micromanages every unit is
slow and thrashes (exactly the `IsIdle` order-overwrite bug class WW3MOD already
fought). Giving each axis local autonomy under a shared intent is both more
realistic and more robust.

**(c) WW3MOD translation.** WW3MOD is already moving this way and should lean in.
The **score-floating axes** of `PoiOffensiveBotModule` are decentralized execution:
central "intent" = the `PoiMap` scores; each axis executes locally. Deepen it: let
each axis make **local decisions** (engage a target of opportunity, take cover,
fall back at culmination §5) via its stance package + `PoiGoalGuard` commitment,
without the central module re-issuing every tick — the guard's whole point is
"commit, then leave it alone." Keep the central layer to *allocation and intent*
(which POIs, how many units), not micro. This is also the anti-thrash architecture
the POI plan already chose.

**(d) Effort: S–M** — largely an *architectural discipline* already begun (goal-guard
+ axes); the work is pushing more local decisions down to the axis and keeping the
central module out of micro. Reuses `PoiGoalGuard`, stances.

**(e) Expected effect.** Watchability **medium** — indirect; shows up as an AI that
reacts locally and doesn't twitch. Win-rate **medium-high** — faster local reaction
+ no order-thrash is a real, if unflashy, competitive gain and a robustness win.

---

## 9. Deception / feints

**(a) Real-world observation.** Both sides exploit gaps in the enemy's *real-time
analysis* with **decoys** (wooden/foam replica guns and vehicles) that draw fire and
unmask the shooter for counter-fire, and with **feints** that pull reserves to the
wrong axis. It works because reconnaissance ≠ real-time interpretation — a
plausible false target consumes the enemy's fires and attention.

**(b) Why it matters.** Deception is force-multiplication: it makes the enemy spend
fires/reserves on nothing and mis-position against the real blow. Against an AI
opponent especially, a feint that pulls its reserve is high-value.

**(c) WW3MOD translation.** The most format-limited pattern (see §10), but a
**cheap-unit feint axis** is viable: `PoiOffensiveBotModule` can open a small,
deliberately visible **feint axis** at a secondary POI to bait the enemy's reactive
defense (its `LayeredDefence` will pull reserves toward the contested band), while
the real mass hits elsewhere — this exploits the enemy AI's own frontline-reactive
behavior. Physical decoy actors (fake units) would need new content/traits and are
lower priority. Keep the feint *cheap* so the bait isn't a real loss (ties to force
preservation §5).

**(d) Effort: M** for a feint-axis behavior (a low-value axis flagged "feint,"
sized small, aimed to trip the enemy's reactive defense). **L** for physical decoy
actors (new content). 

**(e) Expected effect.** Watchability **medium** — a feint that visibly pulls the
enemy then a real blow elsewhere is satisfying when it lands, but subtle. Win-rate
**low-medium** — real but situational, and only pays against an enemy whose defense
is reserve-pulled by contact (our own control AI qualifies; may whiff vs a static
turtle).

---

## 10. Ranked implementation order (opinionated)

Ordered by **(realism + win-rate) per unit of effort**, and by dependency (recon
underpins several). Cheap wins on existing systems first; new-trait work later.

| # | Pattern | Effort | Why this rank |
|---|---|---|---|
| 1 | **§6 SR-deny + income interdiction (scoring weight only)** | S | Already coded & live-verified; just elevate enemy-SR/income scores. Highest win-rate lever (throttles enemy production) for the least work. |
| 2 | **§2 Dispersion via cohesion doctrine (spread-to-move, mass-to-assault)** | S–M | Most *visible* realism change; kills the death-ball on existing `CohesionMoveModifier` + axis system. Pure watchability + survivability. |
| 3 | **§8 Mission-command discipline (push decisions to the axis, keep central to intent)** | S–M | Mostly architectural hygiene already begun; prevents order-thrash and makes everything below robust. Enabler. |
| 4 | **§1 Recon-strike loop (ThreatMap freshness → PoiMap targeting + standing scout)** | M | The master pattern; makes the AI act on what it *saw*. Unlocks §3 counter-battery, §6 lane-ambush, §7 posture. Do before those. |
| 5 | **§5 Force preservation / culmination (retire failing axes, rotate damaged units out)** | M | Directly protects the budget = the whole economy. Reuses axis-retire + resupply/`Evacuate`. Big practical win, modest effort. |
| 6 | **§4 Defense in depth + reserves + elastic counterattack** | M→L | Closes the biggest defensive gap (static-only today). Echeloned garrisons cheap; reserve + counterattack loop is the L part. High win-rate. |
| 7 | **§3 Fires-first + shoot-and-scoot + counter-battery** | M→L | The central casualty exchange; fires-first/composition is M, shoot-and-scoot + counter-battery (needs enemy-arty classifier) is L. Depends on §4 (recon). |
| 8 | **§7 Infiltration vs mounted posture selector** | M | Situational assault posture on existing modules+stances. Needs §1 threat read to choose well. |
| 9 | **§6 Reinforcement-lane ambush + supply-truck hunting** | M | High-realism interdiction; net-new lane-derivation + ambush seeding. Needs §1 recon + §5 preservation (cheap ambush force). |
| 10 | **§9 Feint axis** | M | Real but situational; do last, once axes + reserves + preservation exist to make a *cheap* feint. |

**The 3 cheapest high-impact wins (do first):**

1. **§6 — Elevate the enemy Supply-Route contestation POI (and income interdiction)
   in scoring.** Effort S, win-rate very high. The `Pressure`-into-the-SR-circle
   behavior is already coded and verified; this is largely a scoring-weight change
   that makes the AI go for the single most valuable objective in the game model —
   throttling the enemy's whole production.
2. **§2 — Spread-to-move / mass-to-assault cohesion doctrine.** Effort S–M, the
   most *visible* realism upgrade for the least code. Runs on `CohesionMoveModifier`
   + the existing multi-axis allocator; finally makes advances *look* like real
   dispersed movement instead of a blob, and improves survivability vs any fires.
3. **§5 — Culmination/force-preservation (retire failing axes + rotate damaged
   units to the edge).** Effort M, protects the budget directly. Reuses the axis
   retire/hysteresis machinery and the resupply/`Evacuate` plumbing that already
   exist; stops the AI from throwing good budget after bad.

**Which items need NEW engine traits vs ride existing systems:**

- **Ride existing systems (cheap):** §6 SR-deny/income (scoring), §2 dispersion
  (cohesion stances), §8 mission-command (architecture), §5 preservation
  (axis-retire + resupply stances), §7 posture (MountedTransport + stances), §9
  feint axis (offense module). These are mostly *weights, gates, and per-axis rules*
  in `PoiOffensiveBotModule` / `PoiMap` / `PoiGoalGuard`.
- **Need modest new code, no engine-fork:** §1 recon link (a `PoiMap` freshness
  term + standing scout assignment), §4 reserve ledger + elastic counterattack
  (extend `PoiGoalGuard` + `LayeredDefenceBotModule`), §6 lane-ambush (lane
  derivation + ambush seeding), §3 fires-first/composition (module weighting).
- **Need genuinely new engine traits (defer):** §3 real shoot-and-scoot +
  enemy-artillery classifier on the threat map; §4 AI mine-laying (prepared
  obstacle belts); §9 physical decoy actors. These are the L-effort tail.

---

## 11. Patterns judged NOT worth pursuing in this format (honest limits)

- **Physical decoy replicas (§9, hard version).** Wooden-gun decoys work in reality
  because the enemy can't tell real from fake in real time. Against an AI opponent
  with (near-)omniscient perception, or against a human who reads unit types
  instantly, fake actors mostly add content cost for little payoff. The *feint
  axis* (behavioral) captures the useful 80%; skip physical decoys unless a
  fog-of-war-for-AI gate lands first.
- **Deep operational interdiction / "middle strike" at 30–300 km.** The real
  campaign's strategic-depth strikes have no analogue on a single tactical map —
  there is no operational rear to reach. WW3MOD's *tactical* equivalent (SR-deny,
  lane-ambush, supply-truck hunting, §6) is the right and sufficient translation;
  don't try to model strategic deep strike.
- **Minefield-centric defense in depth (§4, mine version).** The Surovikin Line's
  decisive feature was 500 m mine belts. AI mine-laying is a real new behavior and
  could be fun, but mines are a content/trait investment with fiddly pathing
  interactions; the *behavioral* core of depth (echeloned garrisons + reserves +
  elastic counterattack) delivers most of the realism without it. Treat mines as an
  optional §4 extension, not core.
- **True morale / rout modeling.** Real culmination includes units breaking and
  fleeing. WW3MOD models "morale" only as the **suppression** meter (prone,
  slowed) — there is no rout state, and bolting units would fight the RTS control
  expectation. Use suppression + the §5 preservation behavior (deliberate,
  ordered withdrawal) rather than trying to simulate panic.
- **Full fog-of-war-gated AI perception as a prerequisite.** Genuinely making the
  AI "earn" its intel (so recon *matters*) is the most realistic version of §1, but
  it is a large, risky change that could make the AI play *worse* if botched. Keep
  it as an optional L-effort deepening of §1, not a gate on the cheap recon-strike
  scoring link.

---

## 12. Sources

Modern-warfare grounding (accessed 2026-07-19):

- RUSI — *Tactical Developments During the Third Year of the Russo-Ukrainian War*
  (gun dispersion >500 m, decoys, counter-battery, drone attrition): 
  https://static.rusi.org/tactical-developments-third-year-russo-ukrainian-war-february-2205.pdf
- RUSI — *Preliminary Lessons from Ukraine's Offensive Operations, 2022–23*:
  https://static.rusi.org/lessons-learned-ukraine-offensive-2022-23.pdf
- CEPA — *Adaptation Under Fire: Mass, Speed, and Accuracy Transform Russia's Kill
  Chain in Ukraine*: https://cepa.org/comprehensive-reports/adaptation-under-fire-mass-speed-and-accuracy-transform-russias-kill-chain-in-ukraine/
- U.S. Army — *Tactical Reconnaissance Strike in Ukraine: A Mandate for the U.S.
  Army* (GIS Arta "Uber for artillery," compact kill chain):
  https://www.army.mil/article/284138/tactical_reconnaissance_strike_in_ukraine_a_mandate_for_the_u_s_army
- Hudson Institute — *Ukraine's Drone War: Machine-Speed Adaptive Hyperwar*:
  https://www.hudson.org/technology/ukraines-drone-war-rise-machine-speed-adaptive-hyperwar-can-kasapoglu
- Second Line of Defense — *Ukraine as a Kill Web Laboratory*:
  https://sldinfo.com/2026/04/ukraine-as-a-kill-web-laboratory-democratic-isr-grids-enabling-adaptive-drone-warfare/
- Modern War Institute — *Drones Won't Save Us: Learning the Wrong Lessons from
  Ukraine* (attrition vs maneuver caution):
  https://mwi.westpoint.edu/drones-wont-save-us-learning-the-wrong-lessons-from-ukraine-will-cost-the-us-army-its-edge-in-maneuver-warfare/
- Wikipedia — *Fortifications of the Russian invasion of Ukraine* / *2023 Ukrainian
  counteroffensive* (Surovikin Line, layered belts, minefield depth):
  https://en.wikipedia.org/wiki/Fortifications_of_the_Russian_invasion_of_Ukraine ,
  https://en.wikipedia.org/wiki/2023_Ukrainian_counteroffensive
- Forbes / David Axe — *To Slow the Ukrainian Counteroffensive, the Russian Army
  Quadrupled the Size of Its Minefields*:
  https://www.forbes.com/sites/davidaxe/2023/09/05/to-slow-the-ukrainian-counteroffensive-the-russian-army-quadrupled-the-size-of-its-minefields/
- U.S. Army TRADOC G2 — *Red Diamond: Russia's "Elastic Defense" Technique Slowed
  Ukraine's Advance*:
  https://oe.t2com.army.mil/product/red-diamond-russias-elastic-defense-technique-slowed-ukraines-advance/
- Modern War Institute — *The Glass Backbone: Why the Army's Logistics Will Break in
  the Next War* (decentralized sustainment, no safe rear):
  https://mwi.westpoint.edu/the-glass-backbone-why-the-armys-logistics-will-break-in-the-next-war/
- Kyiv Independent — *How Ukraine's new middle-strike drone campaign aims to
  strangle Russian logistics*:
  https://kyivindependent.com/analysis-how-ukraines-new-middle-strike-drone-campaign-aims-to-strangle-russian-logistics/
- National Interest — *Ukraine's Decentralized Command Puts Russia on the
  Defensive* (mission command vs centralized C2):
  https://nationalinterest.org/blog/buzz/ukraines-decentralized-command-puts-russia-defensive-204714
- Kyiv Post — *Russia Shifts to "Infiltration" Tactics to Bypass Ukrainian
  Defenses* (2–4-man dismounted teams, culmination):
  https://www.kyivpost.com/post/75313
- Forbes / Vikram Mittal — *The Russian Military's Search for a Viable Assault
  Tactic Falls Short* (mounted vs dismounted, both disrupted by drones):
  https://www.forbes.com/sites/vikrammittal/2026/06/22/the-russian-militarys-search-for-a-viable-assault-tactic-falls-short/

WW3MOD internal grounding: `DOCS/reference/game-model.md`,
`DOCS/reference/supply-route.md`, `DOCS/reference/architecture.md`,
`WORKSPACE/plans/260719_experimental_ai_poi_strategy.md`.
