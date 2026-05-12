# AI Doctrine — real-world tactics

> The long-term destination for every AI improvement in WW3MOD. Every
> piece of v2+ work should be evaluated against whether it moves us
> closer to playing like a real combined-arms commander — not like a
> Red Alert bot from 2003. Lives alongside [`foundation_260511.md`](foundation_260511.md)
> (the *architecture* roadmap); this is the *behaviour* roadmap.

## Why this exists

The current AI plays like every other RTS bot of its generation: build
units, mass them, attack-move at the enemy base. That is *not* what a
real military commander does, and it isn't fun to play against either —
the bot is either way too easy (you read it after one match) or way too
hard (it cheats with omniscient information and economy bonuses).

We have a better option. WW3MOD's mechanics (Supply Routes as fixed
beachheads, sector economy, no tech tree, garrisonable buildings,
treeline cover, supply lines) map cleanly onto real-world doctrine.
If the AI plays from that doctrine, two things happen at once:

- **It plays believably.** Watching the AI is interesting because its
  decisions look like decisions a competent commander would make.
- **It plays well without cheating.** Real tactics work; the AI doesn't
  need omniscience or economy multipliers to be a worthy opponent.

This doc is the *destination*. Specific phases will pick one piece at a
time and ship it.

## Defence in depth — the core organising idea

Real-world ground combat is organised in **layered lines** along the
contact zone. Each layer has a role; together they absorb attacks,
identify the weak axis, and counter-punch. WW3MOD inherits the same
idea verbatim.

### The three layers (forward → rear)

```
                ENEMY
                  │
                  ▼
  ┌──────────────────────────────────┐
  │ SCREEN          (Layer 1)        │   Light infantry, well-protected:
  │   treelines, garrisoned buildings│   in cover, in buildings, hidden
  │   eyes-on, fix the enemy         │   if possible. Cheap to lose.
  └────────────────┬─────────────────┘
                   │
  ┌────────────────▼─────────────────┐
  │ MAIN LINE       (Layer 2)        │   Heavy combat power:
  │   tanks, IFVs, TOS, ATGM         │   tanks, BMPs, TOS, ATGM, AA.
  │   stop attacks, do the killing   │   The line that holds.
  └────────────────┬─────────────────┘
                   │
  ┌────────────────▼─────────────────┐
  │ RESERVE         (Layer 3)        │   Mobile force at safe distance:
  │   mobile, plugs gaps, mounts     │   reroutes laterally along the
  │   counter-attacks                │   frontline to the threatened
  │                                  │   sector.
  └──────────────────────────────────┘
                   ▼
              OWN SUPPLY ROUTE
```

**Layer 1 — Screen.** Infantry in cover. Sees the enemy first, slows
them, makes them deploy, dies cheaply. Anchored on terrain — treelines,
garrisoned tech buildings, hill lines. Its job is *not* to win the
engagement; its job is to be the trip-wire that makes the main line
fight on its own terms. In WW3MOD: garrisoned `^OcasusGarrison` buildings,
prone infantry in treeline cover, ATGM/AT teams in hardpoints.

**Layer 2 — Main line.** Where the killing happens. Tanks (Abrams,
T-90), IFVs (Bradley, BMP-2), heavy artillery (Paladin, GRAD, TOS),
heavy AA (HSAM, Tunguska). Positioned to cover the screen with direct
fire, and to fire on anything that breaks through. Has overlapping
fields of fire — no single cell of the screen is uncovered.

**Layer 3 — Reserve.** Mobile combat power held back from contact. Its
sole job is to move along the line to wherever the enemy is pushing
hardest, and to mount a local counter-attack when the main line absorbs
an attack and the enemy is overextended. In WW3MOD: a mixed squad of
fast vehicles (IFVs, helicopters) and mounted infantry stationed near
the player's Supply Route, with attack-move paths to multiple sectors
of the frontline.

### What this requires the AI to know

- **Where the frontline is.** Not a single line — a band of cells where
  friendly and enemy influence overlap (the *contested zone*). Updated
  continuously.
- **What's in each sector.** For each section of the frontline, what
  army value do *we* have on that sector, and what does the enemy have?
- **Where the weak points are.** Sectors of the frontline where the
  enemy's force ratio is unfavourable to them. Targets for offence.
- **What the reserve sees.** What's the shortest reroute distance from
  the reserve to each frontline sector? Drives the "do we have time to
  plug the gap?" calculation.

### What this requires the AI to do

- **Maintain the screen.** When Layer 1 is thin in a sector, queue
  garrison-eligible infantry and ferry them there. When a tree-line is
  uncovered, fill it.
- **Maintain the main line.** When a sector of Layer 2 is below threshold
  army value, queue heavier units and move them in. Veterans stay
  forward; replacements join the line.
- **Maintain the reserve.** Always hold N% of total combat power at the
  reserve position. Top it up after using it.
- **Respond to local pressure.** When enemy force-ratio in a sector
  exceeds a threshold (say 1.5×), divert reserve toward that sector
  *before* the line breaks, not after.
- **Withdraw cleanly.** When a sector of the screen is overrun, pull the
  survivors back through Layer 2 (they don't run into their own line)
  and bandage in reserves to re-form the screen behind.

## Offensive doctrine — concentration of force

Defence in depth is half the picture. The other half is *when does the
AI attack, and where?*

### The 3:1 rule

Real-world offensive doctrine holds that a successful attack on a
prepared defence requires roughly a **3:1 numerical advantage** at the
*point of attack*. This isn't 3:1 across the whole battlefield — that
would be impossible. It's 3:1 *at the attacking sector*, achieved by
**stripping forces from quiet sectors and concentrating them**.

So when the AI considers attacking:

1. Pick a candidate sector. Usually the *weakest enemy sector* —
   lowest friendly-to-enemy force ratio for the enemy.
2. Compute the army-value ratio at that sector. If we can locally get
   ≥ 3:1 by concentrating, the attack is *eligible*.
3. Compute time-to-concentrate: how long does it take for our reserves
   and adjacent-sector forces to converge on that sector?
4. Compute the enemy's reinforcement time: how long until they can
   bring their own reserves back? (Defaults if intel is missing: assume
   a moderate enemy reinforcement timer.)
5. **Schedule** the attack — start moving forces toward the point of
   attack so they arrive concentrated *before* the enemy's
   reinforcement.

If no sector currently meets the 3:1 threshold and we can't get there
by concentrating, the AI doesn't attack — it strengthens defence,
captures economic targets, or harasses to bleed.

### Concentration without overcommitment

A real commander doesn't attack with everything. The rule of thumb:
the attacking force at the point of attack should be ≥ 3:1, the rest
of the line stays at *enough force to hold under the local force
ratio that remains after stripping*. The AI computes both halves and
only attacks if both still hold.

In practical numbers:
- **Strip** units from quiet sectors (force ratio ≥ 2:1 in our
  favour) — but never below ≥ 1.5:1 there.
- **Concentrate** the stripped forces + the reserve + some of the
  attack-sector defence at the point of attack.
- **Schedule** the strike for the tick when the concentration is
  in position.

### Harassment and shaping

Outside of full attacks, the AI's offensive options shape the
battlefield:

- **Cut supply lines** — small fast units (Humvees, BTRs,
  helicopters) ambush the enemy's reinforcement lane (`ProductionFromMapEdge`
  has a known walk path). Even a few losses in transit force the
  enemy to commit escorts and slow their tempo.
- **Capture income** — the existing capture coordinator. Income
  compounds; lost income compounds even faster.
- **SR contestation** — a single unit in the enemy's Supply Route
  10-cell circle slows their *entire* production (`SupplyRouteContestation`).
  Strategic-value-per-unit is enormous.
- **Sabotage** — Technicians on enemy-held tech buildings above
  HP threshold trigger sabotage damage (`CaptureActor.cs:104-118`).
  Last-ditch denial.

### Knowing when not to attack

A capable AI that always attacks is easy. A capable AI that *doesn't*
attack when it shouldn't is hard — and feels right. Cases where the
doctrine says "don't attack":

- **No sector meets 3:1** — strengthen, don't gamble.
- **Reserves not yet rebuilt after a previous push** — wait for the
  next cycle.
- **Enemy in a defensive posture with high force concentration** —
  attacking a prepared 1:1 sector loses 70%+ of the time.
- **Income trajectory is favourable** — if our income is climbing
  faster than the enemy's, the right move is to let the gap widen
  before forcing engagement.

## Personality — same doctrine, different weights

The current Rush / Normal / Turtle personalities should not be "build
different units." Same doctrine; different *weight settings*:

| Personality | Layer 1 weight | Layer 2 weight | Layer 3 weight | Attack threshold |
|---|---|---|---|---|
| Rush | low | medium | LOW (use everything) | accepts 2:1; attacks fast |
| Normal | medium | high | medium | 3:1, waits for concentration |
| Turtle | HIGH | HIGH | high | 4:1+; rarely attacks; bleeds enemy |

A higher difficulty doesn't change the doctrine either — it gets
faster scout reaction, better intel quality, slightly better tactics
in execution.

## Information / fog

Real commanders have fog. So should the AI. Default position:

- Same vision rules as the player. The AI doesn't see what its units
  don't see.
- Scout output drives the enemy-strength estimate. Without scouts,
  the AI assumes the worst about the enemy and plays conservatively.
- Captured tech buildings (MISS = radar) actually help the AI as
  much as they help the player.

This makes the AI *exploitable* in interesting ways (the player can
ambush a scout-blind sector) while still playing well in fair
conditions.

Higher difficulties can opt-in to better intel (more frequent scout
output, slightly extended vision) but should not start fully
omniscient. Brutal-tier is the only place omniscience belongs, and
even then we want it gated behind a difficulty selector the player
chose knowingly.

## What this means in code — the perception layer

Almost every behaviour above grounds out in *one common data
structure*: an **influence map** of friendly vs enemy strength,
sampled per cell or per region, refreshed every ~30 ticks.

From that one structure, all of these are derived:

| Concept | Derived from influence map |
|---|---|
| **Frontline** | cells where friendly AND enemy influence are both > 0 |
| **Sector force ratio** | sum(friendly) / sum(enemy) over a band of cells along the frontline |
| **Weak enemy sector** | sector with lowest enemy/friendly ratio for the enemy |
| **Pressure direction** | cells where enemy influence is rising fastest (compare current vs T-N ticks ago) |
| **Safe rear** | cells with high friendly influence, low enemy influence |
| **Reserve home** | safe rear cells nearest the centroid of the frontline (fast lateral reach) |
| **Reinforcement-lane danger** | path from map edge to SR rally — does any cell along it have enemy influence? |

An influence map *already exists* in code (`ThreatMapManager`, ~428
LOC, 8×8 grid). It's too coarse and only used in a couple of spots.
Step one of any "real tactics" work is to make this layer *good* and
*centralised*: every decision reads from it; nothing computes its own
ad-hoc threat heuristic.

The existing v2 `CaptureCoordinatorBotModule` already does
ad-hoc threat scans (`FindActorsInCircle` + sum). That's fine for an
isolated module but doesn't scale. Future modules read from a single
shared influence layer.

## Phased roadmap

Each phase ships a visible improvement. The phasing is sequenced so
the user can verify each piece is working before the next builds on it.

### Phase A — Frontline perception (foundation)

**Goal:** the AI knows where the frontline is, and the user can see what
the AI sees.

- Influence map: dense per-cell layer (or 2× cell granularity), refreshed
  every ~30 ticks, military strength weighted by army value.
- Frontline derivation: cells where both sides have non-zero influence.
- Debug overlay: toggleable in-game (hotkey, e.g. F11), draws the frontline
  as a coloured band over the map.
- *No* bot module yet wires this into decisions. Step is pure perception.

**Verifies:** start a match vs Normal AI, toggle the overlay, the band
sits where the actual contact between forces is. Push your army
forward, the band advances. Pull back, the band retreats.

This is the **next stage** — see TODOs at the bottom.

### Phase B — Defensive layer placement (Layer 1 + Layer 2)

**Goal:** v2 bot places infantry in cover along the frontline (Layer 1)
and tanks/IFVs in firing position behind (Layer 2). The user can see the
layered defence forming over the first 2 minutes of the match.

- Garrison spots: derived from the frontline + treeline/garrisonable-
  building cells within N cells of the frontline.
- Vehicle line: positions behind the screen, with overlapping fields
  of fire (each line cell within max-range of at least 2 vehicle
  positions).
- Bot module (replaces or augments SquadManagerBotModule): for each
  garrison-eligible unit, find the nearest unfilled Layer 1 slot and
  move there. For each tank/IFV idle in reserve, find the nearest
  unfilled Layer 2 slot.

**Verifies:** load a match vs v2 with the overlay on. Within 90 sim-sec,
the AI should have infantry in treelines along the frontline and a
visible line of tanks/IFVs behind.

### Phase C — Reserve management (Layer 3)

**Goal:** mobile reserve that re-positions in response to pressure.

- Reserve home selection: safe-rear cell near frontline centroid.
- Reserve composition target: percentage of total army value held back.
- Lateral reroute: when sector force ratio drops, reserve units
  attack-move to that sector.

**Verifies:** attack one sector of the v2 AI hard. Watch its reserve
shift laterally to that sector before the line breaks.

### Phase D — Offensive doctrine (3:1 + scheduling)

**Goal:** v2 attacks at the right place at the right time.

- Weak-sector detection.
- Concentration plan: which units strip, how long the strip takes.
- Attack scheduler: launch when concentrated, not when "we feel like it".
- Hold otherwise.

**Verifies:** play a match where you visibly weaken one sector. Within
60-120 sim-sec the AI converges forces on that sector. Strengthen
the same sector — the AI cancels the planned attack and looks
elsewhere.

### Phase E — Personality differentiation through weights

**Goal:** Rush/Normal/Turtle play meaningfully differently using the
same doctrine.

- Per-personality weight set in YAML (Rush low Layer 1 / high attack
  willingness / 2:1 threshold; Turtle inverted).

**Verifies:** three games on the same map vs three personalities feel
like three different opponents at the *strategic* level, not just
the build-order level.

### Phase F — Honest fog by default + difficulty slider

**Goal:** AI plays fair under the same vision the player has;
difficulty knob opts into intel bonuses.

- ScoutBotModule output drives the enemy-strength estimate.
- Difficulty levels in lobby: Easy / Normal / Hard / Brutal — each
  adjusts scout cadence, intel-decay rates, and at Brutal opts into
  partial omniscience.

**Verifies:** a Normal AI without scouting visibility loses to a player
who plays smart with fog. A Brutal AI plays at a sharp level even
with the same vision.

## Out of scope (for now)

These are real military tactics but not yet a fit for WW3MOD's
mechanics:

- **Air superiority sweeps.** Helicopter swarms are not airframes
  attacking other airframes; air-to-air is sparse. Defer.
- **Combined-arms breaching of fortified positions.** No mines/anti-tank
  ditches in standard maps yet.
- **Logistics chains.** TRUKs and supply caches exist but aren't yet
  positioned as part of the defensive line. Phase B+ candidate.
- **Operational manoeuvre (multi-day campaigns).** A single match is
  one tactical engagement; campaign-level doctrine isn't applicable.

## Reading list

For depth:

- US Army FM 3-90.1 (*Offense and Defense*), public PDF.
- *On Tactics* by B.A. Friedman — short, modern, opinionated.
- *Brood War AI* postmortems (AIIDE) — RTS-specific takes on
  concentration and tempo.

The doctrine here aims to be self-contained; reading isn't required to
ship the phases.
