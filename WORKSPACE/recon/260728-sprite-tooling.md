# Recon — sprite export/generate/import tooling & graded damage art (2026-07-28, main @ c1d93e29)

Read-only recon (asset-pipeline study). Maps what EXISTS for an export→generate→import sprite loop and where graded damage-state art can slot in. No tooling built. All claims code-verified with file:line; refs are as of main @ **c1d93e29**. Format mirrors [`260728-trees-concealment.md`](260728-trees-concealment.md).

## Executive summary

**Can the export→generate→import loop be built with shipped tools alone? YES — the full SHP round-trip already ships** in `engine/bin/OpenRA.Utility.exe`, no new engine code required:

1. `--extract t01.shp temperat.pal` — pull the SHP + palette out of the mounted RA `.mix` archives to cwd (`ExtractFilesCommand.cs:34`).
2. `--png t01.shp temperat.pal` — SHP → one indexed PNG per frame (`t01-0000.png`…) (`ConvertSpriteToPngCommand.cs`).
3. **External edit / image-gen** — produce new frames. Hard constraint: output must be **8-bit palette-indexed to `temperat.pal`, all frames identical pixel size**.
4. `--shp t01-0000.png t01-0001.png …` — indexed PNGs → SHP (`ConvertPngToShpCommand.cs`, Cnc).
5. Drop the new `.shp` as a loose file under `ww3mod|bits` (mount-precedence override — verify, see Seams #6).

**The single biggest lever: PNG-direct is one mod.yaml line away.** The engine already has a full native PNG sprite loader (`PngSheetLoader.cs`) that handles **both indexed AND 32-bit RGBA**, but the mod does **not** register it — `SpriteFormats` (`mod.yaml:327`) lists only `ShpD2, ShpTD, TmpRA, TmpTD, ShpTS`. Adding `PngSheet` to that list enables loose `.png` sprite sheets in sequences with **zero SHP conversion** and lets externally-generated RGBA art render directly (subject to the palette-shift caveat, Q2/Q6). That collapses the whole pipeline to "export for reference → generate → drop PNG + sequence YAML."

**Shortest path to graded tree damage art in-game: pure sequences + YAML + art, NO engine change.** The damage-state → sequence-prefix table is hardcoded in the engine (`RenderSprites.cs:121-127`) and already supports **four graded tiers** (`scuffed-`/`scratched-`/`damaged-`/`critical-` = Light/Medium/Heavy/Critical). `^Tree` already has `WithSpriteBody` + `Health`, so once the art exists you just add `scuffed-idle:`/`scratched-idle:`/`damaged-idle:`/`critical-idle:` blocks to `t01` (etc.) and the engine auto-selects by HP%. **The art is the only real work.** Same for buildings — today most have just 2 visual states (undamaged + `damaged-`); filling in the other three prefixes is YAML+art. Going *finer* than 4 tiers (true 20/40/60/80 distinct from the enum) WOULD need engine changes.

---

## Q1 — Utility sprite commands (all in `engine/OpenRA.Mods.Common/UtilityCommands/`, import in `…Cnc/UtilityCommands/`)

Invoked on Windows as `engine\bin\OpenRA.Utility.exe ww3mod <cmd> …` (`make.ps1:378`, wrapper `utility.cmd:31,59`; the mod id `ww3mod` is arg 0). Commands write output to the **current working directory**.

| Command | String | Source | What it does / caveats |
|---|---|---|---|
| SHP→PNG export | `--png SPRITEFILE PALETTE [--noshadow] [--nopadding]` | `ConvertSpriteToPngCommand.cs:23,30` | Loads shp/tmp/R8 via `FrameLoader` (`:52`), emits one `prefix-NNNN.png` per frame (`:85`), **Indexed8** with palette colors baked in. **Reads a raw file path** (`File.OpenRead(src)` `:52`) — NOT the mod filesystem, so a SHP inside a `.mix` must be `--extract`ed first. `--noshadow` zeroes shadow indices 1,3,4 (`:39-45`); palette index 0 is transparent (`:47`). `--nopadding` uses frame's own size instead of the padded FrameSize (`:54,60`). |
| PNG→SHP import | `--shp PNGFILE [PNGFILE ...]` | `ConvertPngToShpCommand.cs:25,32` (Cnc) | Combines PNGs into one SHP via `ShpTDSprite.Write` (`:47`). **Enforces: every frame `Indexed8`** (`:39-40`, throws "All frames must be paletted") **and identical W×H** (`:43-44`). Output filename = first input split on `-`, first token, `+ ".shp"` (`:36`) — so `t01-0000.png` → `t01.shp`. Accepts globs (`:52-57`), sorts by name (`:35`). |
| Repalette SHP | `--remap SRCMOD:PAL DESTMOD:PAL SRCSHP DESTSHP` | `RemapShpCommand.cs:25,32` (Cnc) | Best-match channel-distance remap between palettes (`:63-67`); preserves the 16-entry player-color remap range by index (`:56-57`). Useful to conform RA-sourced art to the ww3mod palette without hand-quantizing. |
| Extract from mix | `--extract FILENAME [FILENAME…]` | `ExtractFilesCommand.cs:20,27` | Opens via `ModData.DefaultFileSystem` (`:34`, resolves through mounted mixes) and writes raw bytes to cwd. **This is the on-ramp** to get `t01.shp` / `temperat.pal` out of the RA mixes before `--png`. |
| PNG metadata export | `--png-sheet-export PNGFILE` | `PngSheetExportMetadataCommand.cs:20,27` | Dumps a PNG's embedded metadata chunks to a sibling `.yaml`. |
| PNG metadata import | `--png-sheet-import PNGFILE` | `PngSheetImportMetadataCommand.cs:21,28` | Writes `FrameSize`/`FrameAmount`/etc. from a sibling `.yaml` back into the PNG's embedded chunks (`:51-54`); validates the sheet is big enough (`:46-47`). **This is how you author frame-slicing metadata for the PngSheet loader** (Q2). |
| Sequence atlas dump | `--dump-sequence-sheets PALETTE TILESET-OR-MAP` | `DumpSequenceSheetsCommand.cs:22,29` | Exports the built sequence texture atlas as PNGs — a debug/inspection view of packed sprites, not a per-actor export. |

Adjacent-but-not-sprite-export: `CheckMissingSprites.cs` (lint), `ExtractSpriteSequenceDocsCommand.cs` (docs), `OutputResolvedSequencesCommand.cs` (resolved YAML). Legacy importers in Cnc (`LegacySequenceImporter.cs`, `LegacyTilesetImporter.cs`) convert whole RA/TS asset sets, not individual round-trips.

## Q2 — Native PNG sprite support: BUILT in engine, NOT enabled in the mod

- Loader exists: `PngSheetLoader.cs:33` `ISpriteLoader`. It **passes `png.Type` straight through** (`:75`) → supports **Indexed8 and 32-bit RGBA**; if the PNG carries a palette it attaches an `EmbeddedSpritePalette` (`:87-88`).
- Frame slicing driven by embedded metadata: manual `Frame[i]` regions preferred (`:58-59,99-109`), else auto-slice from `FrameSize`/`FrameAmount` (`:112-160`), else whole image = 1 frame (`:115`). Author these via `--png-sheet-import` (Q1).
- **NOT wired in ww3mod:** `mod.yaml:327` `SpriteFormats: ShpD2, ShpTD, TmpRA, TmpTD, ShpTS` — **no `PngSheet`.** So dropping a `.png` into a sequence does nothing today. **Enabling = append `PngSheet` to that one line.** (One-line mod-config change, not a code change — but out of scope for "shipped as-is".)
- RGBA-vs-palettized caveat: even with the loader on, RGBA art rendered through the normal unit path is still subject to the mod's palette pipeline / player-color remap (Q6). Indexed PNGs quantized to `temperat.pal` are the safe, predictable choice; RGBA is viable for palette-independent decoration art but needs render-path validation before trusting.

## Q3 — How WW3MOD sprites are wired

- `PackageFormats: Mix` (`mod.yaml:9`). `Packages` (`mod.yaml:12-56`): RA assets from `~^SupportDir|Content/ra/v2/` mixes (`~main.mix`, `~conquer.mix`, `~temperat.mix`, … `:21-37`), plus **loose** dirs `ww3mod|bits`, `ww3mod|bits/units/*` (`:49-56`). So stock sprites (incl. trees) live **inside RA `.mix` archives**, not as loose files — a bare `find t01*` returns nothing; you must `--extract`.
- `SpriteSequenceFormat: ClassicTilesetSpecificSpriteSequence` (`mod.yaml:333`) with tileset extensions `.tem/.sno/.int/.des` (`:334-338`).
- **(a) Tree** — rule `^Tree` `decoration.yaml:2-47`, concrete `T01` `:100-112`. Sequence `t01` `sequences-decorations.yaml:396-402` (single `idle`, tileset-extension + DESERT/INTERIOR→TEMPERAT overrides). Husk sequence `t01.husk:404-415` (`idle` Start:1, `dead` Start:2 Length:8 — the burn anim). SHP name: **`t01`** (inside a RA mix).
- **(b) Building with damage** — the fire/damage default `^BuildingAffectedByFire` (`structures.yaml:159-184`) drives burn overlays via `WithDamageOverlay@Small/Medium/Large` keyed on `MinimumDamageState`/`MaximumDamageState` (`:169-184`). Body-sprite damage art is the `-`-prefixed sequence mechanism (Q4). Sequence usage: `damaged-idle:` appears **127×** across `sequences-decorations.yaml` (e.g. `:449,459,469…`), `scratched-` **1×**, `scuffed-`/`critical-` **0×** — i.e. today buildings render **2 visual states** (undamaged + one `damaged-`/Heavy tier).

## Q4 — Damage-state machinery: 4 graded tiers already supported, no engine change to use them

- Enum: `DamageState` `TraitsInterfaces.cs:31-39` = `Undamaged, Light, Medium, Heavy, Critical, Dead` (flags).
- HP thresholds (fixed in engine): `Health.cs:85-105` — `Undamaged` at full HP; `Light` ≥75%; `Medium` 50–75%; `Heavy` 25–50%; `Critical` <25%; `Dead` ≤0. (The user's "20/40/60/80" maps ~onto these five states.)
- **Prefix table is HARDCODED:** `RenderSprites.cs:121-127` — `critical-`→Critical, `damaged-`→Heavy, `scratched-`→Medium, `scuffed-`→Light. `NormalizeSequence` (`:309-318`) strips any existing prefix then returns the **highest-severity prefixed sequence that exists** for the current state; falls through to the bare sequence if none defined. So an actor with `WithSpriteBody` + `Health` gets graded art *for free* the moment prefixed sequences exist.
- **Adding graded states to buildings/trees = sequences + art only.** For a tree: add `scuffed-idle:`, `scratched-idle:`, `damaged-idle:`, `critical-idle:` under `t01` pointing at new frames; engine auto-switches by HP%. `^Tree` already has `WithSpriteBody` (`decoration.yaml:21`) and `Health` (`:35-36`). No trait wiring, no C#.
- **Trees' burnt/dead art is NOT a damage state today** — it's a **husk swap**: `SpawnActorOnDeath: T01.Husk` (`decoration.yaml:108`) → `T01.Husk : ^TreeHusk` (`husks.yaml:122-131`) → separate actor with its own `t01.husk` sequence. So the living tree currently has 1 state (`idle`), the corpse another. Graded *pre-death* damage is additive to this and doesn't touch the husk path.
- **What WOULD need engine changes:** more than 4 damaged tiers (finer than the enum), or different HP breakpoints — that's `DamageState` enum + `Health.cs:85-105` + `RenderSprites.cs:121-127` (the prefix table), all engine. Within 4 tiers at the fixed breakpoints: pure YAML.

## Q5 — Multi-part / independently-destroyable structures: precedent exists, two shapes

- **Bridge = the heavyweight precedent.** `Bridge.cs:25-59` (+ `GroundLevelBridge.cs`, `BridgeHut.cs`, `WithBridgeSpriteBody.cs`, `WithDeadBridgeSpriteBody.cs`) models a multi-span structure with `Template`/`DamagedTemplate`/`DestroyedTemplate` (`:32-34`), long-bridge neighbour variants (`:37-39`), and `RepairPropagationDelay` between adjacent spans (`:30`). But it is **terrain-template / tile-based** — requires an `ITiledTerrainRenderer` (`:58`) — so it's a poor fit for a free-standing "building with wings."
- **Lighter shape for "wings that destroy independently":** compose the logical building from **multiple co-located actors**, each with its own `Health` + `WithSpriteBody` sharing a footprint, tied together by conditions/`GrantConditionOnDeath` or a proximity/parent link. Each wing is a normal destructible actor; the "structure" is their arrangement. This reuses the husk + damage-state machinery per part with **no engine change**, at the cost of authoring N actors + placement.
- **Seam sketch (not a design):** either (a) N sub-actors + a coordinating condition graph (pure YAML, more actors to manage, selection/tooltip UX to solve), or (b) a Bridge-style single actor holding multiple `WithSpriteBody` segments each gated by a per-segment damage condition (needs new C# — a segment-health trait). (a) is the shipped-tools route.

## Q6 — Palettes & the constraint on generated art

- Palettes in `mods/ww3mod/rules/palettes.yaml`; `.pal` files under `mods/ww3mod/bits/misc/palettes/` (`anim.pal`, `gensmkexploj.pal`, `unittem.pal`, …) plus RA palettes (`temperat.pal`) inside the mixes.
- **Base unit/actor palette = `temperat.pal`**: `PaletteFromFile@player Filename: temperat.pal ShadowIndex: 4` (`palettes.yaml:50-53`). Index **0 = transparent** (`ImmutablePalette(args[2], new[]{0}, …)` `ConvertSpriteToPngCommand.cs:47`), **4 = shadow**.
- **Player-color remap = indices 80–95** (16 entries): `PlayerColorPalette RemapIndex: 80,81,…,95` (`palettes.yaml:132-134`); a TD variant remaps 176–191 (`:16-19`).
- **Constraint on externally-generated art:** it must be **quantized to `temperat.pal`'s 256 colors**; keep transparent at index 0 and shadow at 4; **avoid indices 80–95 unless you want that pixel to take the player's team color** (use them deliberately for team-colored panels, avoid otherwise). There is **no dedicated "quantize an arbitrary PNG to palette" utility** — `--remap` conforms an existing *SHP* between palettes (`RemapShpCommand.cs`), but initial quantization of new RGBA art must happen externally (GIMP/Aseprite indexed mode, or ImageMagick `-remap temperat.pal`). If PngSheet/RGBA is enabled (Q2), palette-quantization can be sidestepped for decoration art that doesn't need remap.

## Q7 — Prior art in-repo: none for sprite creation

- `tools/` = `autotest`, `combat-sim`, `git-hooks`, `map-mcp` — **no sprite/SHP/PNG tooling**. The only scripts matching "png/sprite" are autotest screenshot helpers (`tools/autotest/screenshot*.sh`), unrelated to asset creation.
- `git log` (all branches) shows **no SHP/PNG/asset-creation commits**; the mod reuses inherited RA sprites straight from the mixes. No existing export/import wrapper scripts, no README on art production. A pipeline would be **greenfield**, but built entirely on the shipped Utility commands above.

## Web-sourced (NOT code-verified) — standalone SHP editor landscape

Community-standard GUI editor remains **Open Source SHP Builder** (OS SHP Builder) by Banshee/Stucuk on Project Perfect Mod — it imports BMP/PNG/etc. into SHP(TD)/SHP(TS), does palette recolor, and exports frames to PNG, but **cannot handle combined sprite sheets** (needs sequentially numbered `name0000.png` frames). Modern OpenRA modders more commonly use the **Utility `--png`/`--shp` path** with **8-bit palette-indexed PNGs** (authored in GIMP/Aseprite, indexed mode being the hard requirement), sometimes assembling a horizontal sheet via ImageMagick before `--shp`. Net: for *this* repo the shipped `--extract`/`--png`/`--shp` chain covers the round-trip; OS SHP Builder is an optional GUI convenience for hand-editing/recolor, not a dependency. *(2015–2017-era tutorials; treat exact syntax as indicative.)*

Sources: [OpenRA Utility wiki](https://github.com/OpenRA/OpenRA/wiki/Utility), [Everything about modding (OpenRA wiki)](https://github.com/OpenRA/OpenRA/wiki/Everything-you-always-wanted-to-know-about-modding), [OS SHP Builder (PPM)](https://www.ppmsite.com/shpbuilderinfo/), [Issue #4426 SHP→sheet PNG](https://github.com/OpenRA/OpenRA/issues/4426).

---

## Seams — where to cut when building the pipeline (mapped, not designed)

| # | Seam | Location | Note |
|---|---|---|---|
| 1 | Enable PNG-direct sprites | `mod.yaml:327` add `PngSheet` | **Highest leverage.** Turns loose `.png` into first-class sprites (indexed or RGBA); skips SHP entirely. Validate RGBA through the render/palette path first (Q2/Q6). |
| 2 | SHP round-trip (shipped) | `--extract`→`--png`→edit→`--shp` | Zero new code. Constraints: indexed to `temperat.pal`, equal frame size, transparent=0/shadow=4. Wrap as a `tools/sprite/` script. |
| 3 | Graded tree/building damage art | sequences (`sequences-decorations.yaml`) + `RenderSprites.cs:121-127` (read-only) | Add `scuffed-`/`scratched-`/`damaged-`/`critical-` `idle` sequences; engine auto-selects by HP% (`Health.cs:85-105`). Pure YAML+art within 4 tiers. |
| 4 | >4 damage tiers / new breakpoints | `TraitsInterfaces.cs:31-39` + `Health.cs:85-105` + `RenderSprites.cs:121-127` | Engine change. Only needed if 4 graded states at the fixed 25/50/75% breakpoints are insufficient. |
| 5 | Multi-part building | new per-segment health trait OR N co-located actors | Bridge (`Bridge.cs`) is tile-based, wrong fit. Shipped-tools route = multiple actors + condition graph (no C#). Independent-wing single-actor = needs new trait. |
| 6 | Loose-file override precedence | `mod.yaml` Packages mount order (`:12-56`) | **Verify** that a loose `ww3mod|bits/t01.shp` shadows the mix copy before relying on drop-in replacement; mount order determines the winner. |
| 7 | Palette quantization gap | external (GIMP/Aseprite/ImageMagick) or `--remap` | No Utility command quantizes arbitrary RGBA → `temperat.pal`. `--remap` only conforms an existing SHP between palettes. Generated art must be indexed before `--shp`. |

## Open questions (NOT verified by this recon)

- **Loose-vs-mix override** (Seam #6) — assumed but not proven; test with one throwaway SHP before building on it.
- **RGBA render fidelity** through ww3mod's palette/player-color path if PngSheet is enabled — needs an in-game screenshot check, not just loader inspection.
- **Frame offset authoring** — `--png` bakes padding/offset from the source SHP; new-art offsets for correct in-world anchoring (trunk centering, husk alignment) aren't handled by the round-trip and must be set via sequence `Offset`/`--png-sheet-import` metadata. Untested here.
