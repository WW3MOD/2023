# Various Fixes — 2026-04-05

## Tasks

### Quick Fixes (can batch)
- [x] **1. TECN capture waypoint color** — Changed to warm yellow (FFC850B4, matches evacuate line color)
- [x] **7. Littlebird minigun accuracy** — Halved Inaccuracy (1c512→0c768) and InaccuracyPerProjectile (64→32)
- [~] **8. HIND anti-helicopter targeting** — Config already correct (12.7mm ValidTargets includes Helicopter, AutoTarget includes Air). Needs in-game verification — if still broken, it's a C# bug not config

### Medium Fixes (one at a time)
- [x] **2. TECN cargo enter cursor** — Added OrderPriority to CapturesInfo; ^CapturesNeutralBuildings set to 4 (below EnterTransport's 5). Removed dead EngineerRepairable from ^Building
- [x] **4. Evacuation waypoint display** — Added ShowTargetLines() to AmmoPool auto-evacuate path
- [x] **9. Range circle standardization** — 8 categories (kinetic/at/aa/missile/arty/sniper/special/supply) with consistent Color+Width+RangeCircleType across all 48 circles

### Larger Investigations
- [x] **3. Engineer mine-laying area selector** — BeginMinefield order priority raised from 5 to 7 (above attack's 6). Force-attack on terrain now opens minefield selector instead of ground attack
- [x] **5. Evacuation pathing** — RotateToEdge now checks IsNearMapEdge(4) before selling; retries with closest edge cell if blocked. Prevents mid-map sell near buildings
- [x] **6. Auto-rearm routing** — Auto behavior now flags NeedsResupply when no resupplier found (instead of evacuating). Only Evacuate stance triggers map-edge exit
