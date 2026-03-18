# WW3MOD Feature Ideas

Collected feature ideas beyond the core TODO. Some ambitious, some small quality-of-life.

---

## Combat & Weapons

### Sweeping Fire
When multiple enemies are nearby, units can spread burst fire across targets for maximum suppression instead of focusing one. MLRS with only one target sweeps the area instead of dumping the whole salvo on one infantry squad.

### Indirect Fire Accuracy Ramp
Artillery and mortars gain accuracy with each successive shot at the same stationary target (while the player has vision on impact site). Simulates ranging/spotting. Resets when target moves.

### Binoculars / Laser Designator
Team leaders get a force-attack-ground ability that reveals an area (binoculars). Force-attacking a vehicle laser-marks it, extending missile range and enabling tracking for normally unguided munitions. Makes HIMARS/Iskander strikes more effective with forward observers.

### Logarithmic Inaccuracy
Inaccuracy scales logarithmically with distance instead of linearly. Close-range shots are very accurate, long-range shots get progressively less reliable but don't become completely useless.

---

## Unit Behavior

### Ammo Preservation Modes
- **Max Firepower** -- fire as fast as possible
- **Preserve** -- reduce rate of fire to extend ammo
- **Auto** -- dynamically reduce consumption when ammo is low, factoring in resupply distance

### Infantry Sprint
Force-move = sprint. Units move faster but with reduced detection, accuracy, and firing rate. Normal move allows firing on the move at reduced speed.

### Covered Movement
Normal move pathfinder prioritizes cells with higher cover values. Units naturally seek safer routes without explicit orders.

### Subcell Stopping
Allow infantry to stop at subcell positions so they can start firing faster instead of always completing movement to the next full cell.

---

## Supply & Economy

### Ammo Economy
Ammunition costs money. Supply trucks must physically deliver ammo to Logistics Centers. Creates a real logistics chain -- cutting supply lines becomes a viable strategy.

### Dynamic Income
Structure income scales with building health. Damaged structures produce less. Incentivizes repair and targeted strikes on enemy economy.

### Nuclear Powerplant
High-output power structure that releases long-lasting radiation if destroyed. Risk/reward: great power output but becomes a liability if the enemy targets it.

### Production Shortage
Building the same unit type repeatedly causes a shortage -- increased price and build time. A fixed reduction ticks down over time for all units. Encourages diverse army composition.

---

## Structures

### Trenches
Buildable trench lines that provide cover and concealment. Infantry can move through them safely. Could connect to bunkers/buildings.

### Engineer-Built Fortifications
Engineers can build hedgehogs, tank traps, sandbag positions, and tank bunkers (like RADOT5 TBOX). Gives engineers a persistent battlefield role beyond capture/repair.

### Pontoon Bridges
Engineers can build temporary bridges over water. Destructible, creates interesting tactical choices about water crossings.

---

## Game Modes

### Entrance Force
Pre-game build phase: players have a brief period to queue up initial units before the game clock starts. Built units can only move within a small circle around spawn until the timer ends. Creates more interesting openings.

### Flag Capture Victory
Capturing an enemy's flag/HQ starts a countdown. Prevents indefinite games where someone hides a single unit in a corner.

### Nuclear Armageddon
Time-limited games end with escalating nuclear strikes. "Nuclear Escalation" power -- if everyone uses it, one nuke per player launches every 10 seconds. Game ends by score accumulated before armageddon. Everyone starts with a tactical nuke that becomes available at the game time limit.

### Quittable Games
After X minutes, any player can quit and the game counts score. Makes it possible to have meaningful outcomes without forcing bitter-end play.

---

## Stances (Advanced)

### Authorized Weapons
Toggle individual weapon systems on/off per unit. Deselecting all = Hold Fire. Allows precision like "only use ATGM, hold MG fire" for stealth.

### Cohesion Modes
- **Tight** -- units cluster close
- **Loose** -- one cell gap between
- **Spread** -- two cell gaps

### Scorched Earth Targeting
AutoTarget priority mode: destroy everything including neutral economy, civilian buildings. For total war scenarios.

---

## Visuals

### Camouflage Colors
Replace silly faction colors with camouflage patterns. Players select from a list (possibly two-color combos). Could read half of the palette each for the two colors.

### GPS Dot for Barely Visible Units
When a unit is at the edge of detection (between cloaked and spotted), show as a generic GPS marker dot on the minimap/screen. Can be targeted but type is unknown.

---

## UI & Controls

### Tab Waypoint Mode
Toggle persistent waypoint mode so every click adds a waypoint without holding Shift. More efficient for complex multi-point orders.

### Take Cover with Variable Radius
Press "Take Cover" multiple times to increase search radius. First press = nearest cover, additional presses = wider search. Controls how much units spread out.

### Auto-Enter
Hotkey that makes selected infantry enter nearest transport, or makes selected transports call in nearby infantry. Context-sensitive.

---

## AI (Long-term)

### Capability-Based Building
AI analyzes enemy army composition and builds counters based on unit capabilities (anti-armor, anti-air, anti-infantry) rather than random selection.

### Strategic/Tactical/Operational Layers
- **Strategic** -- designates targets, areas of interest, resource priorities, decides force allocation
- **Tactical** -- coordinates units per objective, maintains formations, handles combined arms
- **Operational** -- individual squad behavior, high-value target hunting, flanking
