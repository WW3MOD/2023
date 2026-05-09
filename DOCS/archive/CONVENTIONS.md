# WW3MOD Visual & Design Conventions

Reference document for visual conventions used across the mod.
Agents should read this when working on UI, range circles, unit definitions, or any visual feedback systems.

> Referenced from rules files. See also: `CLAUDE.md` for architecture, `DOCS/TODO.md` for priorities.

---

## Range Circle Color Scheme

Range circles use a consistent color scheme that matches the infantry class icon colors.
**Weapon circles must use the correct color for their weapon category. Non-weapon circles must use a neutral color to avoid confusion.**

### Weapon Categories

| Color | Hex | Category | Examples |
|-------|-----|----------|----------|
| **Yellow** | `FFFF00` | Lower caliber (up to ~30mm) | 5.56mm, 7.62mm, 12.7mm HMG, 20mm autocannon, 30mm chaingun |
| **Red** | `FF0000` | Explosive unguided | Tank cannons (120mm), unguided rockets (RPG), artillery shells |
| **Green** | `00FF00` | Anti-ground guided missiles | Javelin, TOW, Hellfire (AG mode), Kornet |
| **Blue** | `66B2FF` | Anti-air missiles | Stinger, Igla, SAM systems |
| **Cyan** | `00FFFF` | Universal missiles (AA + AG) | Multi-role missile systems |
| **White** | `FFFFFF` | Tesla / exotic / special | Tesla coil, drone jammer, EMP |
| **Orange** | `FF8000` | Incendiary / flame | Flame turret (FTUR) |

### Non-Weapon Circles

| Color | Hex | Purpose | Units |
|-------|-----|---------|-------|
| **Silver/Gray** | `C0C0C0` | Supply/resupply range | TRUK, SUPPLYCACHE |
| **Black** | `000000` | Radar detection range | Radar vehicles, radar towers |
| **Gray** | `888888` | Detectability range | Infantry visibility tiers (^DetectableRangeCircles) |
| **Player Color** | (varies) | Contestation zone | Supply Route (WithRangeCircle@Contestation) |

### Styling Guidelines

- **Weapon circles**: Default alpha ~35 (from RenderRangeCircle default), width 1-3 depending on importance
- **Defense structures**: Width 3, Alpha 70-80 (more prominent since placement matters)
- **Supply circles**: Width 1.5, Alpha 100, `RequireShift: false` (always visible when selected)
- **Radar circles**: Width 5, Color black (thick dark outline, visually distinct from weapon ranges)
- **Detectable circles**: Width 1 (default), Alpha 25 (subtle, many overlapping tiers)

### RangeCircleType Groups

When units share a `RangeCircleType`, their circles overlap/merge on the map:
- `aa` — All anti-air defenses (AGUN, SAM) share this type so AA coverage is visible at a glance

### Edge Cases & Notes

- `00FFBB` (teal) appears on some aircraft for Hellfire-type missiles. Ideally should be `00FF00` (green, AG guided) but kept as-is for visual distinction from infantry AT missiles. Could be standardized in a future pass.
- `00FFBB` on Russian aircraft secondary weapons (e.g., Ka-52 Hellfires) — same as above.
- Units with multiple weapon types show multiple circles (e.g., Apache: yellow chaingun + green/teal Hellfires).
- The `RequireShift` property defaults to `true` — set to `false` on support units (supply, radar) where range is always relevant.

---

## Infantry Class Icons

Infantry class icons use the same color families as range circles:

| Class | Color | Role |
|-------|-------|------|
| Rifleman | White | General purpose |
| AR (Auto Rifleman) | Yellow | Suppression / volume fire |
| Grenadier | Red | Anti-infantry explosive |
| AT (Anti-Tank) | Green | Anti-vehicle guided missiles |
| AA (Anti-Air) | Blue | Anti-air missiles |
| Sniper | Yellow | Precision long-range |
| Engineer | White | Repair / capture |
| Medic | White | Healing |
| Tesla | White | Exotic weaponry |

The pip colors on `WithAmmoPipsDecoration` should also match: `pip-yellow` for caliber weapons, `pip-red` for explosive, `pip-green` for AG guided, `pip-blue` for AA.

---

## Future Conventions (add here as established)

<!-- Add new convention sections as they are established -->
