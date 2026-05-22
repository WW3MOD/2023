# Autoburn run: comment-typos

Branch: `auto/comment-typos`
Date: 2026-05-21

## Summary

- **Typos fixed:** 4
- **Files touched:** 4
- **Commits:** 3 (one per word-class)
- **Build:** passed (Release, no errors)

The codebase turned out to be very well-edited. Three full passes of common-English typo regexes (~80 distinct patterns: seperate / recieve / occured / definately / accross / untill / wether / becuase / sucessfull / etc.) over `*.cs` and `*.md` yielded almost no hits — the only confirmed typos were the four below.

## Typo classes (with commit hashes)

### 1. Missing apostrophes in code comments → `9d6fa6d3`
- `engine/OpenRA.Game/Map/Map.cs:460` — `doesnt` → `doesn't`
- `engine/OpenRA.Mods.Common/Traits/GainsExperience.cs:127` — `Dont` → `Don't`

### 2. Dittology (doubled word) in code comment → `d88f965a`
- `engine/OpenRA.Mods.Cnc/Traits/AttackPrismSupported.cs:243` — `My guess is that is is more common` → `that it is more common` (doubled `is` was the typo; the second `is` was meant to be the pronoun `it`)

### 3. Misspelling in plan-doc body → `43780ee8`
- `WORKSPACE/plans/260506_supply_ammo_economy.md:57` — `prefering` → `preferring`

## Skipped (with reason)

| Hit | Location | Reason |
|---|---|---|
| `IntializeLayerPreview` (×2) | `engine/OpenRA.Mods.Common/Widgets/Logic/Editor/LayerSelectorLogic.cs:35,38` | Identifier (method name); hard rule prohibits renaming identifiers — would break callers. |
| `Cooperative / cooperative` | various | Correctly spelled. |
| `managment`, `manager` (verb context) | `mod.config:113-114` | Out of scope — `mod.config` is neither `.cs` nor `.md`. |
| `aquire`, `jsut`, `doesnt`, `cant` | `DOCS/archive/RELEASE_V1_TODO.md`, `DOCS/archive/RELEASE_V1_TODO_2.md` | Archived TODO docs — informal user notes; CLAUDE.md treats archived workspace files as historical (don't edit). |
| `in in` (dittology), `jsut`, `abl`, `th esmoke`, `doesnt`, `cant`, `dont` | `WORKSPACE/archive/plans/260326-div.md`, `WORKSPACE/archive/plans/260505_targeting_los_brainstorm_handoff.md` | Archived plans — same reason as above. |
| `useable` | `engine/mods/ts/`, `engine/mods/cnc/`, `engine/mods/d2k/` | Not WW3MOD-touched (vanilla TS/CNC/D2k mod data). |
| `Cooperative`, `Tomorrow`, `successfully`, `cancelled`, `initialised`, `stabilise`, `harmlessly` | various | Correct spellings (verified — `successfully` does take double-l; `cancelled`/`initialised`/`stabilise` are British spellings; per CLAUDE.md, British vs. American is not a typo). |
| `is is` second instance | n/a | Only one occurrence in the codebase. |
| Various contractions without apostrophes (`isnt`, `dont`, `cant`) in archived `WORKSPACE/archive/` and `DOCS/archive/` notes | various | Skipped — archive layer. |

## Verification

```
$ (cd engine && dotnet build OpenRA.Mods.Common/OpenRA.Mods.Common.csproj -c Release --nologo -clp:ErrorsOnly)
Build succeeded.
    0 Warning(s)
    0 Error(s)

$ (cd engine && dotnet build OpenRA.Mods.Cnc/OpenRA.Mods.Cnc.csproj -c Release --nologo -clp:ErrorsOnly)
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

Both projects build clean after the edits.

## Commits

```
43780ee8 comment typos: 'prefering' -> 'preferring' in supply ammo plan doc
d88f965a comment typos: fix 'is is' dittology in AttackPrismSupported comment
9d6fa6d3 comment typos: add missing apostrophes (doesn't, Don't)
```
