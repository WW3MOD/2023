# Unit Management Ideas

## Implemented

### Group Scatter (Shift+G)
Inspired by Supreme Commander FAF's Group Scatter mod. Select units, shift-click multiple waypoints, then press Shift+G to distribute the waypoints among selected units instead of having everyone go to every waypoint.

**Behavior:**
- Collects all queued move waypoints from the selected group
- Groups units by type (e.g., tanks, infantry, APCs)
- Within each type group, assigns waypoints round-robin:
  - 3 tanks + 3 waypoints = 1 tank per waypoint
  - 2 tanks + 4 waypoints = each tank gets 2 waypoints (queued)
  - 6 tanks + 3 waypoints = 2 tanks per waypoint
- Units are sorted by distance to first waypoint for sensible assignment

## Ideas for Future

### Formation Move
Hold a modifier key (e.g., Alt) when issuing a move order to maintain relative formation. Units keep their spacing relative to each other instead of all converging on the same point.

### Spread on Arrival
After reaching a waypoint, units automatically spread out into a defensive formation instead of stacking. Could use the existing scatter logic but triggered automatically.

### Patrol Chains
Assign a loop of waypoints where units continuously patrol between them. Different from attack-move — this is a patrol route that repeats.

### Split Group
Hotkey to evenly split the current selection into two groups. First half stays selected, second half gets deselected. Useful for quickly dividing forces.

### Leapfrog Advance
Two groups alternate — one moves forward while the other provides cover, then they swap. Would need two control groups and a hotkey to trigger the alternating advance.

### Smart Spread
Like scatter but smarter — units spread to cover an area evenly, maintaining sight lines and avoiding clustering. Could factor in weapon range to create an optimal firing line.

### Priority Target Assignment
When multiple targets are available, distribute fire across targets instead of all units focusing the same enemy. Could be a stance modifier or hotkey toggle.
