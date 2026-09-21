# Painted zones live in map.yaml, not in the map's rules.yaml

_Recorded 2026-09-20T11:07:04.726Z by 60a95888_

**Options.** (a) Have the editor write `World: DefconWall: RegionCells:` into the map package's `rules.yaml` — the format nine maps already use, zero runtime change. (b) A new `Zones:` node in `map.yaml`, saved by the editor's ordinary `Map.Save` via one `YamlFields` entry, with `DefconWall` unioning a map-authored DMZ into its region path and the nine maps migrated.

**Picked (b).** The editor never writes rules files today and a textual rewrite of a hand-commented `rules.yaml` would either drop comments or need a comment-preserving MiniYaml round-trip; map geometry belongs in the map file; the user asked for a *category* of zones, and a keyed node makes the next zone kind a one-line addition. The rules-side `RegionCells`/`RegionTerrainTypes` stay supported (scenarios use them; river-zeta's terrain-type union stays) — precedence is painted zone ≥ rules RegionCells ≥ Start/End ≥ derived.

**Cost accepted.** Nine `map.yaml`/`rules.yaml` pairs change in one commit and the wall audit must prove identical sealing before and after.
