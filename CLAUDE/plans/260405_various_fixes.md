# Various Fixes — 2026-04-05

## Tasks

### Quick Fixes (can batch)
- [x] **1. TECN capture waypoint color** — Changed to warm yellow (FFC850B4, matches evacuate line color)
- [x] **7. Littlebird minigun accuracy** — Halved Inaccuracy (1c512→0c768) and InaccuracyPerProjectile (64→32)
- [~] **8. HIND anti-helicopter targeting** — Config already correct (12.7mm ValidTargets includes Helicopter, AutoTarget includes Air). Needs in-game verification — if still broken, it's a C# bug not config

### Medium Fixes (one at a time)
- [ ] **2. TECN cargo enter cursor** — Shows wrench (capture) instead of enter icon on cargo structures
- [ ] **4. Evacuation waypoint display** — Auto-evacuating units (out of ammo) don't show waypoint line
- [ ] **9. Range circle standardization** — Audit all units, ensure consistent size/color grouping

### Larger Investigations
- [ ] **3. Engineer mine-laying area selector** — Force-attack area selector not showing for engineers (works for minelayer)
- [ ] **5. Evacuation pathing** — Units going to Supply Route instead of map edge spawn point; also getting sold near oil derricks
- [ ] **6. Auto-rearm routing** — Units evac instead of going to nearby supply truck; should prefer closest rearm source
