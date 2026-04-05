# Lobby Settings Redesign — Full Specification

> Design document for overhauling the WW3MOD lobby options panel.
> Status: **MOCK UI IMPLEMENTED** — tabs, dummy options, and summary bar working. Gameplay hooks TBD.

## Goals
1. Remove obsolete Red Alert options that don't apply to WW3MOD
2. Add meaningful gameplay modifiers (weapon range, damage, suppression, etc.)
3. Organize options into tabbed categories instead of a flat grid
4. Add a preset system with save/load and a summary bar for quick visibility
5. Keep sensible defaults identical to current behavior (100% = no change)

---

## Layout

### Tabbed Categories
6 tabs along the top of the options panel:

```
┌────────┬─────────┬───────┬───────┬────────┬─────┐
│ COMBAT │ ECONOMY │ UNITS │  MAP  │ RULES  │ ADV │
└────────┴─────────┴───────┴───────┴────────┴─────┘
```

Each tab shows only its options. Clicking a tab switches the visible content. The panel height stays constant (sized to the tallest tab content, or scrollable).

### Preset Bar (above tabs)
```
┌─ PRESET: [Default ▼] [Save] ──────────────────────────────────┐
│ Settings: All default                                          │
├────────────────────────────────────────────────────────────────┤
```

### Summary Bar (between preset and tabs)
Shows non-default options in a compact one-liner:
- **All default:** "Settings: All default" (or hidden)
- **Custom changes:** "⚠ Weapon Range 50% · Friendly Fire OFF · Sight Range 70%"
- **Preset + deviations:** "Hardcore + Sight Range 50%"

Visible to ALL players in the lobby (including joiners who can't change settings).

---

## Removals

| Option | Reason |
|--------|--------|
| Short Game | Obsolete — WW3MOD doesn't use RA build time shortcuts |
| Redeployable MCVs | No MCVs in WW3MOD (reinforcement model) |
| Crates | Not part of WW3MOD gameplay |
| Creep Actors | Handled by scenario system, not a lobby toggle |
| Limit Build Area | Already hidden, formally remove |
| Build off Allies | Already hidden, formally remove |
| Tech Level | Replaced by granular unit availability toggles in UNITS tab |

---

## Tab Contents

### COMBAT Tab

Controls weapon effectiveness and unit combat properties.

| Option | ID | Type | Values | Default | Notes |
|--------|----|------|--------|---------|-------|
| Weapon Range | `weapon-range` | Dropdown % | 10–100% (10% steps) | 100% | Can only reduce, never exceed current values. Applies to all weapons globally |
| Damage Scale | `damage-scale` | Dropdown % | 10–200% (10% steps) | 100% | Scales all weapon damage. 200% = instant-kill chaos, 10% = sponge mode |
| Suppression | `suppression` | Dropdown | Off / Low / Normal / High | Normal | Scales suppression amounts and decay rates. Off = no suppression at all |
| Veterancy Rate | `veterancy-rate` | Dropdown % | 10–200% (10% steps) | 100% | How fast units gain experience from combat |

**Suppression presets:**
- **Off:** Suppression amounts × 0, decay irrelevant
- **Low:** Amounts × 50%, decay × 150% (less suppression, recovers faster)
- **Normal:** No change (current balance)
- **High:** Amounts × 150%, decay × 75% (more suppression, recovers slower)

---

### ECONOMY Tab

Controls resources, production speed, and logistics.

| Option | ID | Type | Values | Default | Notes |
|--------|----|------|--------|---------|-------|
| Starting Cash | `startingcash` | Dropdown | 0 / 100 / 250 / 500 / 1000 / 2000 / 3000 / 4000 / 5000 / 7500 / 10000 / 15000 / 20000 / 25000 / 50000 | Current | Existing option, unchanged |
| Passive Income | `passiveincome` | Dropdown | 0–1000 (existing steps) | Current | Existing option, unchanged |
| Income Modifier | `incomemodifier` | Dropdown % | 0–500% (existing steps) | Current | Existing option, unchanged |
| Build Speed | `build-speed` | Dropdown % | 10–200% (10% steps) | 100% | Production/call-in time multiplier. 200% = twice as fast |
| Supply Capacity | `supply-capacity` | Dropdown | Low / Normal / High | Normal | Scales ammo carried by supply trucks |

**Supply Capacity presets:**
- **Low:** CargoSupply InitialSupply × 50% (5 units of supply instead of 10)
- **Normal:** No change
- **High:** CargoSupply InitialSupply × 200% (20 units of supply)

---

### UNITS Tab

Controls which unit archetypes are available. Replaces the old Tech Level dropdown with granular per-archetype toggles. Faction-neutral: disabling "Main Battle Tanks" disables both Abrams (NATO) and T-90 (Russia).

Organized into 3 sections with headers: INFANTRY, VEHICLES, AIRCRAFT.

#### Infantry
| Option | ID | Default | Covers |
|--------|----|---------|--------|
| Conscripts | `unit-conscripts` | On | Light infantry |
| Riflemen | `unit-riflemen` | On | Riflemen, Auto Riflemen, Team Leaders |
| Grenadiers | `unit-grenadiers` | On | Grenade launchers and mortars |
| Snipers | `unit-snipers` | On | Long-range marksmen |
| Anti-Tank | `unit-antitank` | On | Javelin / RPG specialists |
| MANPADS | `unit-manpads` | On | Man-portable air defense |
| Special Forces | `unit-specops` | On | Elite special operations |
| Flamethrowers | `unit-flamethrower` | On | Close-range incendiary |
| Support | `unit-support-inf` | On | Engineers, Medics, Technicians |
| Drone Operators | `unit-drone-ops` | On | Infantry drone operators |

#### Vehicles
| Option | ID | Default | Covers |
|--------|----|---------|--------|
| Light Vehicles | `unit-light-vehicles` | On | Humvee / BTR |
| APCs | `unit-apcs` | On | M113 / BMP-2 |
| IFVs | `unit-ifvs` | On | Bradley / BMP |
| Main Battle Tanks | `unit-mbts` | On | Abrams / T-90 |
| Artillery | `unit-artillery` | On | Paladin / Giatsint |
| MLRS | `unit-mlrs` | On | M270 / Grad |
| SHORAD | `unit-shorad` | On | Stryker SHORAD / Tunguska |
| Tactical Missiles | `unit-tactical-missiles` | On | HIMARS / Iskander |
| Thermobaric | `unit-thermobaric` | On | TOS (Russia only) |

#### Aircraft
| Option | ID | Default | Covers |
|--------|----|---------|--------|
| Transport Helicopters | `unit-transport-heli` | On | Chinook / Halo |
| Scout Helicopters | `unit-scout-heli` | On | Littlebird / Mi-28 |
| Attack Helicopters | `unit-attack-heli` | On | Apache / Hind |
| Ground Attack | `unit-ground-attack` | On | A-10 / Su-25 |
| Fighters | `unit-fighters` | On | F-16 / MiG-29 |

---

### MAP Tab

Controls visibility, fog, and spawn placement.

| Option | ID | Type | Values | Default | Notes |
|--------|----|------|--------|---------|-------|
| Shroud | `shroud` | Checkbox | On/Off | On | Map starts unrevealed. Off = entire map visible from start |
| Fog | `fog` | Checkbox | On/Off | On | Vision fog — units only see within their sight range |
| Sight Range | `sight-range` | Dropdown % | 10–100% (10% steps) | 100% | **Only visible when Fog is ON.** Simulates weather (fog, blizzard). 50% = all units see half as far |
| Separate Team Spawns | `separateteamspawns` | Checkbox | On/Off | On | Teams spawn on opposite sides of map |

**Conditional visibility:** Sight Range dropdown is hidden when Fog is OFF (if you can see everything, range doesn't matter).

---

### RULES Tab

Controls game flow, tech progression, powers, and special mechanics.

| Option | ID | Type | Values | Default | Notes |
|--------|----|------|--------|---------|-------|
| Game Speed | `gamespeed` | Dropdown | Slowest / Slower / Default / Faster / Fastest | Default | Existing option with all current values |
| Doomsday Clock | `timelimit` | Dropdown | No limit / 10 / 15 / 20 / 30 / 45 / 60 / 75 / 90 min | No limit | Renamed from "Time Limit". When time expires: nuclear apocalypse end-game event |
| Tech Level | `techlevel` | Dropdown | Low / Medium / High / Unrestricted | Current | Existing option |
| Starting Units | `startingunits` | Dropdown | None / Squad / Platoon / Motorized / Air Support | Current | Existing option |
| Friendly Fire | `friendly-fire` | Checkbox | On/Off | On | Own units can damage each other. Current default behavior |
| Bounty | `global-bounty` | Checkbox | On/Off | On | Earn cash for killing enemy units |
| Bounty % | `bounty-percent` | Dropdown % | 1% / 2% / 5% / 10% / 15% / 20% / 25% / 50% / 75% / 100% | 10% | **Only visible when Bounty is ON.** % of killed unit's value awarded as cash |

#### Powers Section (inside Rules tab)

Visually separated with a divider line. Hierarchical: master toggle → individual powers → sub-settings.

| Option | ID | Type | Values | Default | Visibility |
|--------|----|------|--------|---------|------------|
| ── Powers ── | | Divider | | | Always |
| Powers Enabled | `powers-enabled` | Checkbox | On/Off | On | Always |
| Airstrikes | `airstrikes` | Checkbox | On/Off | On | Only when Powers ON |
| Airstrike Cooldown | `airstrike-cooldown` | Dropdown | 2 / 3 / 4 / 5 / 8 min | 3 min | Only when Airstrikes ON |
| *(Future powers added here)* | | | | | |

---

### ADVANCED Tab

Developer and network tools. Not gameplay-altering.

| Option | ID | Type | Values | Default | Notes |
|--------|----|------|--------|---------|-------|
| Debug Menu | `cheats` | Checkbox | On/Off | Off | Enables in-game cheat/dev tools (fast build, disable fog, etc.) |
| Sync Check | `sync` | Checkbox | On/Off | Off | Validates game state between players each tick. Detects desyncs and cheats. Off by default for performance |

---

## Preset System

### Built-in Presets

| Preset | Description | Key Differences from Default |
|--------|-------------|------------------------------|
| **Default** | Current WW3MOD balance | All options at default values |
| **Hardcore** | Punishing, realistic | Sight Range 50%, Friendly Fire ON, Suppression High, No Bounty |
| **Casual** | Relaxed, forgiving | Damage Scale 50%, Veterancy Rate 200%, Friendly Fire OFF, Starting Cash 10000 |

### User Presets
- **Save:** Click [Save] next to preset dropdown → enter name → stored to `Platform.SupportDir/ww3mod/lobby-presets.yaml`
- **Load:** Select from dropdown (user presets appear below built-in ones)
- **Delete:** Right-click preset in dropdown → "Delete" (built-in presets cannot be deleted)

### Custom Detection
When any option differs from the currently selected preset:
- Dropdown shows "Custom" if started from Default
- Dropdown shows "Hardcore*" (with asterisk) if started from Hardcore and modified
- Summary bar shows: "Hardcore + Weapon Range 70%, Fog OFF"

---

## Visual Mockup — Full Layout

```
╔════════════════════════════════════════════════════════════════════╗
║  PRESET: [Default ▼] [Save]                                      ║
║  Settings: All default                                            ║
╠════════╤═════════╤═══════╤════════╤══════════╤════════════════════╣
║ COMBAT │ ECONOMY │  MAP  │ RULES  │ ADVANCED │                    ║
╠════════╧═════════╧═══════╧════════╧══════════╧════════════════════╣
║                                                                    ║
║   Weapon Range       [==========100%==========▼]                   ║
║   Damage Scale       [==========100%==========▼]                   ║
║   Suppression        [==========Normal=========▼]                  ║
║   Veterancy Rate     [==========100%==========▼]                   ║
║                                                                    ║
╚════════════════════════════════════════════════════════════════════╝
```

### Rules Tab (expanded)
```
╠═══════════════════════════════════════════════════════════════════╣
║   Game Speed         [==========Default========▼]                 ║
║   Doomsday Clock     [=========No limit========▼]                 ║
║   Tech Level         [========Unrestricted=====▼]                 ║
║   Starting Units     [===========None==========▼]                 ║
║                                                                    ║
║   [x] Friendly Fire                                                ║
║   [x] Bounty                   [====10%====▼]                     ║
║                                                                    ║
║   ─────── Powers ────────────────────────────                      ║
║   [x] Powers Enabled                                               ║
║     [x] Airstrikes             [===3 min===▼]                     ║
╚════════════════════════════════════════════════════════════════════╝
```

### Map Tab
```
╠═══════════════════════════════════════════════════════════════════╣
║   [x] Shroud                                                      ║
║   [x] Fog                                                          ║
║   Sight Range        [==========100%==========▼]                   ║
║   [x] Separate Team Spawns                                         ║
╚════════════════════════════════════════════════════════════════════╝
```

---

## Implementation Phases

### Phase 1: Design Document ✅
This document.

### Phase 2: Remove Obsolete Options
**YAML only, no C# changes.**
- Remove `LobbyPrerequisiteCheckbox@GLOBALFACTUNDEPLOY` from `mods/ww3mod/rules/player.yaml`
- Set `ShortGameCheckboxVisible: false` in `mods/ww3mod/rules/world.yaml` (or remove entirely)
- Set `CrateSpawner` visible: false
- Set `MapCreeps` visible: false
- Verify Build Radius and Ally Build already hidden

### Phase 3: Tabbed UI
**C# + chrome YAML.**
- Add tab button container to `engine/mods/common/chrome/lobby-options.yaml`
- Modify `LobbyOptionsLogic.cs` to group options by `Category` field and render per-tab
- Each `LobbyOption` already has a `Category` string — just need to use it for grouping
- Tab buttons: highlight active tab, show/hide option groups

### Phase 4: New Options
**New C# traits + YAML configuration.**
Each new option is a trait on the World or Player actor implementing `ILobbyOptions`:
- `WeaponRangeModifier` — world trait, reads `weapon-range`, applies via `IRangeModifier`
- `DamageScaleModifier` — world trait, reads `damage-scale`, applies via `IDamageModifier`
- `SuppressionModifier` — world trait, reads `suppression`, scales suppression grant amounts
- `VeterancyRateModifier` — world trait, reads `veterancy-rate`, applies via `IGivesExperienceModifier`
- `BuildSpeedModifier` — world trait, reads `build-speed`, applies via `IProductionSpeedModifier`
- `SupplyCapacityModifier` — world trait, reads `supply-capacity`, scales CargoSupply initial supply
- `SightRangeModifier` — world trait, reads `sight-range`, applies via `IRevealsShroudModifier` (conditional on fog option)
- `FriendlyFireOption` — world trait, reads `friendly-fire`, adjusts `IStanceInfo` for allied damage
- `BountyPercentOption` — player trait, reads `bounty-percent`, scales bounty payout
- Rename "Time Limit" display strings to "Doomsday Clock"

### Phase 5: Preset System
**New C# + YAML.**
- `LobbyPresetManager` widget logic
- Built-in presets defined as const dictionaries mapping option IDs to values
- User presets serialized to `Platform.SupportDir/ww3mod/lobby-presets.yaml`
- Summary bar: reads all options, filters non-default, formats as compact string
- Summary visible to all players (rendered in lobby chrome, not just options panel)

### Phase 6: Conditional Visibility
**C# logic in LobbyOptionsLogic.**
- Track dependency relationships: `sight-range` depends on `fog` being ON
- `bounty-percent` depends on `global-bounty` being ON
- `airstrikes` depends on `powers-enabled` being ON
- `airstrike-cooldown` depends on `airstrikes` being ON
- On any option change, re-evaluate visibility of dependent options
- Hidden options use their default value (no surprises)

### Phase 7: Doomsday Clock End-Game
**C# + Lua/YAML.**
- When timer hits zero: trigger nuclear apocalypse event
- Could be: screen flash, nuke explosions across map, all units take massive damage
- Game ends in draw or victory goes to player with most surviving value
- Design TBD (fun cinematic moment, not just "game over")

---

## Technical Notes

### Existing Infrastructure We Can Reuse
- `LobbyOption` class already has `Category` field — just unused for grouping
- `ILobbyOptions` interface — standard way to expose new options
- `LobbyBooleanOption` — checkbox helper class
- `Session.Global.LobbyOptions` — already stores/syncs all option state over network
- `OptionOrDefault()` — standard way to read option values in traits
- `IRangeModifier`, `IDamageModifier`, `IProductionSpeedModifier` — existing modifier interfaces

### Network Compatibility
All lobby options are synced via `Session.Global.LobbyOptions` dictionary. New options just need unique IDs. Old clients without new options will use defaults (graceful fallback). Preset names are cosmetic — only individual option values are synced.

### Percentage Dropdown Values
Standard 10% step dropdown for most options:
```
Values: 10%, 20%, 30%, 40%, 50%, 60%, 70%, 80%, 90%, 100%, 110%, 120%, 130%, 140%, 150%, 160%, 170%, 180%, 190%, 200%
```
Some options cap lower (Weapon Range: max 100%, Sight Range: max 100%).
Bounty uses custom steps: 1%, 2%, 5%, 10%, 15%, 20%, 25%, 50%, 75%, 100%.

---

## Open Questions
1. **Doomsday Clock end-game:** What exactly happens? Nuke rain? Gradual radiation? Best player wins? Draw? — *Design separately*
2. **Future powers:** What other powers besides Airstrikes are planned? Artillery barrage? Spy satellite? — *Add as they're built*
3. **Preset sharing:** Should players be able to export/import presets as strings or files? — *Nice-to-have, not in initial scope*
4. **Tab persistence:** Should the last-viewed tab be remembered between lobby visits? — *Small UX win, easy to add*
