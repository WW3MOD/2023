# Plan: Frontline Scenario Team Enforcement
**Date:** 2026-03-28
**Status:** Draft

## Problem
When playing River Zeta in Frontline Scenario:
- Garrison vehicles appear and explode (wrong alliances?)
- Player sees garrison units' vision but they're a "different team" — allied but not working right
- Enemies don't shoot at garrison units either — alliance state is wrong for everyone

Root cause: The scenario defines Enemies/Allies in scenarios.yaml, but the lobby may override team settings. The lobby doesn't enforce or communicate the scenario's required team structure.

## Current State
- `scenarios.yaml` defines 4 players: Multi0 (NATO), Multi1 (Russia), NATOGarrison, RussiaGarrison
- Alliances are set in YAML player definitions (Allies/Enemies fields)
- NO lobby enforcement — players can pick any team/faction in the lobby
- OpenRA has NO runtime Lua API for changing player alliances
- The lobby shows a scenario just like any other map — no special restrictions

## Design Goals
1. Scenario-defined team structure must be enforced (players forced into correct teams)
2. Garrison units must be properly allied to the correct human player side
3. The lobby should visually communicate locked settings (faction, team, spawn position)
4. This pattern should be reusable: "Frontline: River Zeta", "Frontline: Siberian Pass", etc.

## Approach Options

### Option A: Lock players in map.yaml (simplest, no C# changes)
- Set `LockTeam: True`, `LockFaction: True`, `LockSpawn: True` on player slots
- Pre-assign teams/factions/spawns in the scenario's player definitions
- The lobby already supports these lock flags — just not currently set
- Pros: Zero C# changes, fast to implement
- Cons: Players can't customize at all; faction is fixed

### Option B: Lua-based alliance fixing at game start
- Add alliance fixing in WorldLoaded: iterate actors, verify/fix owner relationships
- Use `Player.SetStance(otherPlayer, Stance.Ally/Enemy)` if it exists in Lua
- Pros: Flexible, works regardless of lobby
- Cons: May not be possible — need to verify SetStance Lua API exists

### Option C: Custom lobby logic for scenario maps (significant C# work)
- When a Scenario-category map is loaded, enforce the scenario's player relationships
- Show a scenario briefing panel in the lobby
- Lock team/faction/spawn per the scenario definition
- Pros: Best UX, proper enforcement
- Cons: Significant engine work (LobbyLogic changes)

## Recommended: Option A first, Option C as future enhancement

### Step-by-step (Option A):
1. In `scenarios.yaml`, set on each playable slot:
   ```yaml
   Multi0:
     LockTeam: True
     LockFaction: True
     LockSpawn: True
     Team: 1
     Faction: america
   Multi1:
     LockTeam: True
     LockFaction: True
     LockSpawn: True
     Team: 2
     Faction: russia
   ```
2. Set NATOGarrison/RussiaGarrison as non-playable with correct alliances (already done)
3. Verify `Enemies`/`Allies` fields override lobby team assignments for non-playable players
4. Test that garrison units are properly allied

### Immediate bug investigation:
- The "vehicles explode" issue might be caused by garrison units spawning on cells occupied by other actors
- Or caused by wrong faction → wrong sprites → crash/death
- Need to verify: are the garrison actor types correct for each faction? (e.g., are Russian units placed for NATOGarrison?)

## Open Questions
- Does `LockTeam`/`LockFaction` work with the `Allies`/`Enemies` system or only with numbered teams?
- If players in lobby override team to "Free for All", do Allies/Enemies fields still apply?
- Should scenarios enforce difficulty dropdown or let players choose? (Currently works via Map.LobbyOption)
- Can we add a "Scenario Info" panel in the lobby showing the scenario briefing?

## Risks
- OpenRA's team system uses numbered teams (1, 2, etc.) while scenarios use explicit Allies/Enemies. These might conflict.
- LockTeam might not prevent the host from changing "Configure Teams" dropdown
- Non-playable players (garrison) might not respect the team structure if lobby overrides it
