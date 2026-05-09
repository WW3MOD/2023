# Claude's Ideas for WW3MOD

My own suggestions -- ranging from practical v1 improvements to ambitious long-term concepts. Organized roughly by feasibility and impact.

---

## Quick Wins (Small effort, big feel)

### Kill Cam / Replay Highlight
When a unit gets a multi-kill or destroys a high-value target, briefly flash the kill count on screen near the unit. No engine changes needed -- just a condition-triggered overlay. Gives satisfying feedback during combat.

### Ammo Warning Pips
Units at 25% ammo get a blinking pip. At 0%, the pip turns red and stays solid. Players can scan the battlefield visually instead of clicking each unit. Already have the pip system from suppression -- extend it.

### Veterancy Visual Indicators
Veteran units get subtle sprite tints or small chevron overlays. Elite units get a name (randomly generated or from a pool). Makes experienced units feel valuable and worth protecting.

### Auto-Retreat on Critical
A stance toggle: when a unit hits critical damage, it automatically moves toward the nearest friendly repair/logistics structure. Prevents losing experienced units to inattention. Disabled by default so it doesn't surprise players.

### Weapon Range Circles on Hover
When hovering over a unit with Alt held, show translucent range circles for each weapon. Helps new players understand unit capabilities without memorizing stats.

---

## Medium Effort (Worth building for v1 or shortly after)

### Supply Line Interdiction
Show a visible (to the owning player) dotted line from each Supply Route to its map edge spawn point. If an enemy unit stands on this line, spawning is delayed or units arrive damaged/suppressed -- they had to fight through a contested route. Simple concept, huge strategic depth: suddenly map control between your SR and the edge matters.

### Battlefield Salvage
Destroyed vehicles leave a wreck that engineers can salvage for a small resource refund (10-20% of unit cost). Creates a reason to control the battlefield after a fight and gives engineers more to do. Wrecks disappear after a timer.

### Fog of War Intel Decay
Currently, explored terrain stays fully revealed (just greyed out). Instead, intel decays over time: after 30 seconds without vision, enemy buildings/units shown in fog become "stale" and get a question mark overlay. After 60 seconds, they disappear entirely. Forces active reconnaissance. Fits the modern warfare theme perfectly.

### Overwatch Mode
A stance where a unit holds fire until an enemy enters a specific arc/zone, then fires one devastating shot (bonus damage or guaranteed accuracy) before returning to normal. Snipers, ATGMs, and ambush-oriented units. Different from Ambush stance -- this is a single-shot trap, not "hold fire until detected."

### Dynamic Weather
Maps can define weather events (rain reducing visibility, snow slowing movement, fog masking approach). Weather changes mid-game on a timer or randomly. Doesn't need fancy visuals -- even just palette shifts and modifier conditions would work with the existing condition system.

### Combat Engineers
A mid-tier engineer unit that can build field fortifications (sandbags, wire, foxholes) but can't capture buildings. Fills the gap between fragile regular engineers and wanting battlefield construction. Could use existing mine-layer placement logic.

---

## Ambitious (Post-v1, but worth designing toward)

### Theater of War Campaign
Instead of traditional RTS campaigns with scripted missions, create a dynamic campaign map. Players choose which sector to attack/defend. Winning a sector gives resources or unlocks units for the next battle. Losing a sector means the enemy pushes forward. The "campaign" is a series of skirmishes with persistent consequences.

Could be implemented as a simple menu between matches: show a hex map, let the player pick the next battle, load the appropriate map with pre-set conditions (starting resources, available tech, reinforcement timing).

### Electronic Warfare Layer
A new resource: "Signal Intelligence." Radar structures and SIGINT units generate it. Spend it on:
- **Jam radar** -- temporarily blind enemy radar in an area
- **Spoof units** -- create ghost contacts on enemy radar
- **Intercept comms** -- briefly reveal all enemy unit orders/destinations
- **GPS denial** -- reduce enemy weapon accuracy in a zone

This would use the existing condition system heavily. Jamming = grant "radar-jammed" condition to enemies in radius. Spoofing = spawn decoy actors with Detectable but no real combat traits.

### Persistent Unit Identity
Every unit spawned gets a unique ID and tracks its lifetime stats: kills, damage dealt, distance traveled, engagements survived. After a match, show a "unit report" screen with top performers. Surviving veteran units could be "saved" and called in as named veterans in the next game (at premium cost).

Mostly a stats-tracking system with some UI. The emotional attachment to named, experienced units would be huge for engagement.

### Realistic Radio Chatter
Instead of generic "acknowledged" responses, units report what they actually see and do:
- "Contact, three vehicles, bearing north" (when spotting enemies)
- "Rounds complete, requesting resupply" (when out of ammo)
- "Taking heavy fire, suppressed" (when under suppression)
- "Objective reached, holding position" (when arriving at waypoint)

The condition system already tracks all of these states. Just needs audio triggers mapped to condition transitions. Even with placeholder/synthetic voices, this would massively improve atmosphere.

### Morale & Routing
Units have a hidden morale stat affected by:
- Nearby friendly casualties (negative)
- Being suppressed (negative)
- Nearby friendly veteran units / officers (positive)
- Being in cover (positive)
- Winning an engagement (positive)

When morale breaks, the unit routes -- moves toward friendly lines, ignoring orders for a few seconds. Officers/NCOs have an aura that prevents routing. Creates realistic cascading retreats and makes leadership units essential.

This would use the existing condition/modifier system: morale as a decaying value (like suppression), with conditions at thresholds triggering forced move orders.

### Asymmetric Faction Design
Instead of NATO and Russia being mirrors with different sprites, lean into asymmetric strengths:
- **NATO/America:** Superior technology, expensive units, better precision weapons, strong air power, professional army (higher base veterancy)
- **BRICS/Russia:** Numbers advantage, cheaper units, more artillery, better area denial, conscript waves with occasional elite units

The Supply Route system already supports this naturally -- Russia could have faster/cheaper spawning, NATO could have slower but better-equipped units. Balance through asymmetry is harder but much more interesting than mirror factions.

### Procedural Map Generation
A map generator that creates realistic terrain: roads connecting control points, forests providing natural cover, rivers as obstacles with bridge chokepoints, urban areas near map edges. Each generated map gets a random name and weather setting.

Would need a generator script (could be external, just outputs .oramap files). Even a simple one that places terrain tiles by rules would add infinite replayability.

---

## Weird / Experimental

### Commander Mode
One player per team doesn't control units directly. Instead, they see the full battlefield (shared vision of all allies), place waypoint markers, designate priority targets, and authorize support powers. Other players see commander markers as suggested orders. The commander earns points by having teammates follow their markers. Creates a natural coordination role for team games.

### War Correspondent
A non-combat unit that follows armies and "films" engagements. After the match, generates a replay highlight reel of the most intense moments (highest damage exchanges, largest unit losses in a short time). Purely for fun / content creation, but could make the mod popular on streaming platforms.

### Civilian Layer
Maps have civilian traffic (cars on roads, people walking). Combat near civilians generates "collateral damage" points that penalize the aggressor. Some buildings are hospitals, schools -- destroying them costs points. Creates a cost to indiscriminate fire and incentivizes precision. Fits the modern warfare theme and would be unique among RTS games.

### Day/Night Cycle
A slow cycle (maybe 10-minute days) where night reduces all unit vision ranges and increases the value of radar/thermal detection. Night-capable units (with thermals, NVG) become king. Flares as a support power to temporarily illuminate an area. Would completely change how the game plays in the second half of a match.

### Logistics Realism Mode
An optional game mode where everything has logistics:
- Fuel: vehicles consume fuel while moving, need refueling
- Maintenance: heavily damaged vehicles need maintenance time, not just repair
- Supply convoys: automated truck convoys run between your base and Logistics Centers
- Bridge weight limits: heavy tanks can't cross certain bridges

Probably too much for the main game mode, but as an optional "realism" setting it could attract a niche audience that loves logistics depth.
