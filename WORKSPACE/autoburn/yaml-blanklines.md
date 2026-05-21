# YAML Blank-Lines Sweep — 2026-05-21

Branch: `auto/yaml-blanklines`

## Background

CLAUDE.md → "YAML Conventions → Blank lines are significant": *"Templates and top-level entries must be separated by a blank line. The MiniYaml parser silently merges adjacent ones, producing confusing override behavior — not a parse error."*

I verified by reading the parser (`engine/OpenRA.Game/MiniYaml.cs` → `FromLines`). **The parser ignores blank lines** (`if (!key.IsEmpty …)` → empty/blank lines never enter `parsedLines`). What the CLAUDE.md note is really warning about is the **silent merge of duplicate top-level keys** at the same level (`MergeSelfPartial`, lines 497–518: "Node with the same key has already been added: merge new node over the existing one"). A blank line between two **different**-keyed entries has zero semantic effect; the visual separator's job is to make duplicate keys easier to spot for humans.

So I split this sweep into two passes:
1. **Adjacent structured top-level entries** (the literal task) — cosmetic; safe-to-fix where it's clearly anomalous in an otherwise blank-separated file.
2. **Duplicate top-level keys** (the real silent-merge bug) — reported only; needs human judgment on whether they're intentional inheritance/override or copy-paste errors.

## Summary

- **Files scanned:** 90 (`mods/ww3mod/**/*.yaml`, excluding maps)
- **Files with at least one adjacent structured pair:** 23
- **Total adjacent pairs flagged:** ~466
- **Auto-fixes applied:** 4 files (5 blank-line insertions)
- **Reported (no fix):** all packed-style files + all duplicate-key cases
- **Duplicate top-level keys found:** 4

## Fixes applied (committed)

Conservative criterion: file's predominant style is blank-separated; the adjacent pair is clearly an oversight (small count, not part of a packed convention).

| File | Pair | Commit |
|------|------|--------|
| `mods/ww3mod/rules/ingame/structures.yaml` | `^BasicBuilding` / `^Building` (line 67→68) | `37a35743` |
| `mods/ww3mod/rules/ingame/structures.yaml` | `^BuildingAffectedByFire` / `^BuildingAffectedByEMP` (line 186→187) | `37a35743` |
| `mods/ww3mod/rules/ingame/vehicles.yaml` | `^WheeledVehicle` / `^TrackedVehicle` (line 123→124) | `30530a4f` |
| `mods/ww3mod/rules/ingame/vehicles.yaml` | `^TrackedVehicle` / `^Walker` (line 133→134) | `30530a4f` |
| `mods/ww3mod/rules/ingame/structures-defenses.yaml` | `SAM` / `HSAM` (line 808→809) | `3feab4b2` |
| `mods/ww3mod/sequences/sequences-ingame.yaml` | `tracer_large` / `hjm` (line 534→535) | `c2507e9f` |

Each fix is a single blank-line insertion. Zero semantic change (the parser ignores blank lines), pure readability cleanup.

## Duplicate top-level keys (REAL silent-merge cases — NOT auto-fixed)

These are the bugs the CLAUDE.md note is *really* about. They merge silently regardless of blank lines. Conservative bias → reported, not fixed, because some may be intentional inheritance/override.

### `mods/ww3mod/sequences/sequences-aircraft.yaml`
- `smig:` at **line 99** and **line 238**
- Both blocks have **identical content**: `Inherits: emp-overlay` + `idle: { Facings: 16 }`
- Verdict: pure copy-paste redundancy. Merge result identical to either one alone. Safe to delete the second occurrence.

### `mods/ww3mod/sequences/sequences-misc.yaml`
- `poweroff:` at **line 450** and **line 476**
- Different sub-keys: first has `charging:`, second has `offline:`. After merge, both animations are present on the `poweroff` actor.
- Verdict: **probably intentional** — two distinct animation states for the same actor. Recommend collapsing into a single block for clarity, but the runtime behavior won't change.

### `mods/ww3mod/rules/defaults.yaml`
- `^AutoTargetGroundAntiInf:` at **line 489** and **line 521**
- Both blocks have **identical content** (verified line-by-line).
- Verdict: pure copy-paste redundancy. Safe to delete the second occurrence (lines 521–531).

### `mods/ww3mod/rules/defaults.yaml`
- `^AutoTargetAir:` at **line 437** and **line 585**
- **Different content.**
  - Line 437: `Inherits: ^AutoTarget` + `AutoTargetPriority@Air: { ValidTargets: Air, Priority: 2 }`
  - Line 585: `AutoTarget:` (no inherit) + `AutoTargetPriority@Default: { ValidTargets: Air }`
- After merge: `Inherits: ^AutoTarget`, plus `AutoTarget:` directly, plus BOTH priority entries (`@Air` and `@Default`).
- Verdict: **likely a bug** — the second definition appears to have been written without realizing the first existed. Needs review by the author to determine which definition is correct, or whether both should remain merged. Visible symptom would be units using `^AutoTargetAir` getting two `AutoTargetPriority` rules (Air at Priority 2, plus Default targeting Air with no explicit priority).

## Packed-style files (NOT fixed — convention, not bug)

These files use packed (no blank lines between adjacent entries) as their dominant style. Reformatting would be a massive style change with zero functional benefit. Listed for visibility:

| File | Adjacent pairs | Notes |
|------|---:|------|
| `mods/ww3mod/chrome.yaml` | 44 | UI element atlas — adjacency is the file's convention (44 adj vs 111 sep, but the adj regions are coherent button groups) |
| `mods/ww3mod/rules/sound/music.yaml` | 73 | Music track list — packed list of `key: title` entries by theme |
| `mods/ww3mod/sequences/sequences-infantry.yaml` | 65 | Sprite definitions — packed per-actor |
| `mods/ww3mod/rules/ingame/civilian.yaml` | 54 | Civilian actor definitions |
| `mods/ww3mod/rules/ingame/decoration.yaml` | 45 | Decoration actors |
| `mods/ww3mod/rules/ingame/infantry.yaml` | 32 | Mixed: ^Suppression* templates and unit blocks both packed |
| `mods/ww3mod/rules/defaults.yaml` | 30 | The ^AutoTarget* block (lines 437–676) is heavily packed |
| `mods/ww3mod/rules/husks/husks.yaml` | 25 | Husk definitions |
| `mods/ww3mod/rules/weapons/weapons-ballistics.yaml` | 20 | Weapon definitions |
| `mods/ww3mod/rules/ingame/infantry-america.yaml` | 17 | Faction units grouped by role with `# Comment` headers as separators instead of blank lines |
| `mods/ww3mod/rules/ingame/infantry-russia.yaml` | 17 | (Same convention as -america.) |
| `mods/ww3mod/rules/husks/husks-vehicles.yaml` | 15 | Vehicle husks |
| `mods/ww3mod/missions.yaml` | 7 | Mission category lists |
| `mods/ww3mod/rules/misc.yaml` | 7 | Crate block (lines 86–128) + camera block packed |
| `mods/ww3mod/installer/aftermath.yaml` | 1 | Windows + Linux installer pair adjacent (upstream RA convention) |
| `mods/ww3mod/installer/cnc95.yaml` | 1 | (same) |
| `mods/ww3mod/installer/counterstrike.yaml` | 1 | (same) |
| `mods/ww3mod/installer/soviet95.yaml` | 1 | (same) |
| `mods/ww3mod/rules/ingame/old.yaml` | 3 | Legacy file (`old.yaml` suggests deprecated) — left alone |

## Recommendations for follow-up

1. **High priority:** review `^AutoTargetAir` duplicate in `defaults.yaml:437/585` — the two definitions look like an unintentional collision. The merge result includes both `AutoTargetPriority@Air` (priority 2) and `AutoTargetPriority@Default` (no priority set). Decide which is canonical, delete the other.
2. **Low priority cleanup:** delete the redundant `^AutoTargetGroundAntiInf` (defaults.yaml:521–531) and `smig` (sequences-aircraft.yaml:238–241) duplicates. Identical content → zero behavior change.
3. **Intentional but worth collapsing:** `poweroff:` (sequences-misc.yaml:450/476) — merge into a single block with both `charging:` and `offline:` sub-keys for clarity.
4. **Convention question:** the parser doesn't enforce blank-line separators. The CLAUDE.md note primarily protects humans from missing duplicate-key bugs in dense files. Consider whether a CI lint to flag duplicate top-level keys would be more effective than the blank-line convention.

## Files touched

- `mods/ww3mod/rules/ingame/structures.yaml`
- `mods/ww3mod/rules/ingame/vehicles.yaml`
- `mods/ww3mod/rules/ingame/structures-defenses.yaml`
- `mods/ww3mod/sequences/sequences-ingame.yaml`
- `WORKSPACE/autoburn/yaml-blanklines.md` (this report)

## Method

Two awk scanners (kept in `/tmp/` during the run, not committed):
- `yaml_adj_struct.awk` — flags two adjacent col-0 entries where the first had at least one indented sub-trait line. Skips comments and blank lines correctly. Catches the structural traps but ignores leaf-list packed entries (e.g. `key: value` pairs in music.yaml that aren't structured).
- `yaml_dups.awk` — flags duplicate col-0 keys within a single file. This is the actual silent-merge trap.
