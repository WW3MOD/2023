# TODO Batch — April 2, 2026

User-provided ideas and tasks, captured verbatim intent with implementation notes.

---

## 1. Ammo Economy System (Big Project — Needs PLAN)

**Goal:** All unit ammo costs money. Ammo is not free — it's a budget line item. This fundamentally changes the economy: buying a unit gives you full ammo, but firing depletes the unit's recoverable value. Evacuating a unit returns its cost minus spent ammo value.

**Current state:**
- Supply trucks (TRUK) already use `CargoSupply` with numeric supply weight
- Infantry ammo already has `SupplyValue` per round in `AmmoPool`
- `AmmoPool.SupplyValue` exists in engine code and is used for resupply cost deduction
- Evacuate-via-SR ("rotate out") returns unit cost — but does NOT yet deduct spent ammo value

**What needs to happen:**
- Every unit's weapons need `SupplyValue` set per ammo type (infantry cheapest, Iskander missiles most expensive — potentially tank-cost per missile)
- Evacuation refund calculation: `refund = unitCost - totalSpentAmmoValue`. Engine code in the rotate-to-edge/evacuation path needs to calculate this
- Purchased units arrive with full ammo included in price. The ammo cost is baked into the purchase price, not charged separately
- UI: Unit description must show ammo breakdown clearly. Not per-round cost (too granular), but per-weapon summary like "2x Iskander Missiles — $1200" or "120 rounds 5.56mm — $50"
- Consider auto-generating this in the tooltip/description widget from YAML data (SupplyValue * AmmoCount per weapon)
- Infantry ammo values need review — they're partially set but not complete across all infantry types
- Balance principle: Iskander/HIMARS missiles should cost roughly as much as a tank (~$2000-3000?), forcing real tactical decisions about when to fire

**Affected systems:** AmmoPool, SupplyValue, PlayerResources (evacuation refund), unit descriptions/tooltips, all unit YAML files (weapon SupplyValue entries)

**This is a full PLAN-worthy project.** Needs research into current refund code path, description widget code, and full YAML audit.

---

## 2. Ballistic Missile Tilt (Iskander/HIMARS Arc)

**Goal:** Missiles from ballistic launchers (Iskander, HIMARS) don't tilt properly at launch and terminal phase. They should pitch up steeply at launch (near-vertical for Iskander), arc through the top, and pitch down steeply on descent.

**Current state:** This has been attempted before and not fully solved. The issue is in the missile projectile code — likely `Missile.cs` or the specific missile projectile type used by these weapons.

**What to research:**
- How `Missile.cs` calculates facing/pitch during flight — is it derived from velocity vector or set separately?
- Check if `MaximumPitch` or similar properties on the projectile are capping the tilt angle
- Look at `LaunchAngle` and `ArcRange` properties if they exist
- May need a ballistic arc mode where pitch is directly derived from the velocity vector tangent at each point of the parabolic arc
- Previous attempts and what went wrong (check git log for missile tilt/pitch changes)

**The fix is probably:** ensuring the rendered facing matches the actual velocity vector direction throughout the entire arc, including the steep launch and terminal phases. If pitch is capped or interpolated, that's the bug.

---

## 3. River Zeta Shellmap Overhaul (Big Project — Needs PLAN)

**Goal:** Transform the River Zeta shellmap into an epic mod showcase / integration test that demonstrates every unit in the game while also functioning as a real match.

**Known bugs to fix first:**
- Units currently spawn in water — must identify all water tiles and ensure no actors are placed there
- A previous fix attempt updated MCP tools but may have regressed after the upstream merge
- Need to read `map.yaml` carefully, cross-reference with `map.bin` terrain data to find water cells

**Design vision:**
1. **Initial state:** Some units already positioned near the front lines (defending). Not in water — set back from riverbanks
2. **Scripted entrance:** Right at start, vehicle columns arrive at all spawn points (map edges) and take up attack positions — showcases the reinforcement system
3. **Scripted showcase:** Initial scripted units execute cool tactical maneuvers (flanking, combined arms, helicopter strikes, artillery barrages, amphibious crossings at some point)
4. **AI takeover:** Two regular AI players (one per side) with very low starting budget that increases progressively over time. After scripted sequences finish, the AI takes over and keeps the battle going indefinitely
5. **Every unit type spawned:** Use scripted spawns to ensure every single unit in the mod appears at some point — infantry, vehicles, helicopters, fixed-wing, artillery, drones, etc. This doubles as a visual integration test
6. **Camera/POV:** Follow one team's perspective with fog of war. Could use the reveal-all debug option to show enemy movements too. Camera should track interesting action
7. **Amphibious element:** Some amphibious vehicles making a river crossing attack at a scripted moment — shows off that capability
8. **Matches the Frontline scenario:** The shellmap setup should mirror the Frontline scenario exactly — same spawned units, same scripted behavior. The shellmap IS the Frontline scenario running as a demo

**Technical notes:**
- Shellmap is in `mods/ww3mod/maps/` — find the river-zeta shellmap folder
- Water tile identification needs the MCP `read_map` tool or manual terrain analysis
- AI budget scaling: use Lua `Trigger.AfterDelay` to grant cash periodically with increasing amounts
- Unit spawn list should be derived from all unit YAML files to ensure completeness

**This is a full PLAN-worthy project.** Needs terrain analysis, unit inventory, Lua scripting, and AI configuration.

---

## 4. Disable Tesla Trooper & "Futuristic" Units

**Goal:** Tesla trooper and any other "futuristic" units should be disabled (not deleted). They should not appear in production queues, not even with debug/build-all enabled.

**Implementation:**
- Don't delete the YAML/code — just disable via conditions or by removing from buildable categories
- Options: set `Buildable: false` or `BuildAtProductionType:` to an impossible value, or gate behind a condition that's never granted
- Could use a `RequiresCondition: futuristic-tech-enabled` that nothing ever grants
- Check which units are considered "futuristic": Tesla Trooper, possibly Tesla Tank, any others?
- Remove the "Futuristic" tech option from lobby if it exists
- These units may return later as a "Futuristic" expansion/option

**This is a small task.** Identify the units, add a never-granted condition gate, remove any lobby option.

---

## 5. Garrison System Improvements

**Goal:** Garrison system needs work. Specifics TBD — this is a placeholder for a follow-up discussion.

**Current state (for context):**
- Shelter/port deployment model implemented: infantry enter building shelter (Cargo), GarrisonManager deploys best-matched soldier to ports when targets appear
- Port soldiers are in-world with 80% damage reduction via garrisoned-at-port condition
- Building death: port soldiers become free infantry, shelter soldiers ejected by Cargo
- GarrisonPanelLogic provides sidebar management

**Known issues to investigate:**
- Does the deploy-to-port selection work correctly for mixed infantry types?
- Port soldier targeting and engagement behavior
- UI clarity — is it obvious to a new player what's happening?
- Performance with many garrisoned buildings
- Any edge cases with building destruction / damage pass-through

**Needs discussion before implementation.** User to provide specific pain points or desired changes.

---

## 6. Unit Description Overhaul & Auto-Generated Stats

**Goal:** Improve unit descriptions to be more informative, especially for new players. Auto-generate stats from YAML data so descriptions stay accurate.

**Ideas:**
- Ammo cost breakdown (see item #1 — part of ammo economy system)
- Auto-generated stat block showing: HP, armor type, speed, weapon range, damage, rate of fire, ammo count
- Format should be clean and readable in the tooltip/description box
- Consider what a new player needs to know vs what clutters the display
- Stats that help with tactical decisions: "can this unit fight that unit?"
- Possibly tiered info: basic tooltip on hover, detailed panel on selection

**Needs discussion first.** User wants to talk through what should be shown and how before implementation. This intersects heavily with item #1 (ammo costs in descriptions).

---

## 7. Helicopter Crash & Vehicle Crew Tuning

**Three sub-issues:**

### 7a. Helicopters Force-Land Too Often
- Emergency landing triggers too frequently — need to tune the damage threshold or conditions
- Check `HeliEmergencyLanding.cs` for the heavy-damage trigger condition
- May need to raise the HP threshold or add a grace period before emergency landing activates

### 7b. Vehicle Crews Bloat the Battlefield
- After extended combat, the map fills with ejected crew infantry — too many actors, visual clutter, likely performance impact
- **Simple solution:** Increase crew death chance on ejection. Most crew should die when their vehicle is destroyed, with only occasional survivors
- Could also add a timer: ejected crew that aren't picked up or given orders within X seconds auto-evacuate or die
- Review `VehicleCrew.cs` ejection logic — what's the current survival rate?

### 7c. Crew Capture of Repaired Vehicles
- Crew should be able to re-enter repaired vehicles — works for helicopters (AllowForeignCrew) but unclear if it works for ground vehicles
- Needs testing: repair a destroyed vehicle husk, have crew walk back and enter
- Also: all crew and pilot unit types need unique class icons (currently using generic infantry icons?)

---

## 8. River Zeta Map Object Fixes

### 8a. Neutral SAM Site Not Capturable
- There's a neutral SAM site on River Zeta that can't be captured
- Should be capturable by Technician (TECN) — check if it has `ProximityCapturable` or `Capturable` trait with correct settings
- May need `CaptureTypes` to include whatever TECN uses, or may need `Capturable` trait added

### 8b. Broken Capturable Building at Start
- A capturable building spawns but immediately explodes — likely the "broken" variant or wrong actor type
- **For now: remove it.** Can be revisited later
- Identify which actor it is in the map's `map.yaml` and delete the entry

---

*These items range from quick fixes (#4, #8) to full projects needing PLAN sessions (#1, #3). Items #5 and #6 need user discussion before implementation.*
