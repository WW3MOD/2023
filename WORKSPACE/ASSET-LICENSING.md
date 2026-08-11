# Asset licensing exposure — inventory and mitigation plan

**Audited:** 2026-08-11, against `main` @ `2f31404e` (working tree clean of asset changes).
**Status:** informational. **Not a release blocker.** The decision to ship as-is has been taken.

---

## 0. What this document is, and why it exists

WW3MOD is a total conversion of OpenRA Red Alert. Like every OpenRA mod it expects the
player to supply their own Red Alert data files — that part is normal and is not the
issue. The issue is that this repo **also commits ~1,250 binary art and audio files of its
own**, and a meaningful share of them appear to originate from Westwood/EA titles that
have **no freeware release to point at** — principally Red Alert 2 and Command & Conquer:
Generals.

An audio-only recon on 2026-08-11 (`WORKSPACE/DISCOVERIES.md`, commit `9c0bced9`) first
flagged this. This document supersedes and widens it: it re-verifies the audio numbers
against the current tree (they had already gone stale) and adds the **sprite, cameo,
terrain, UI and font survey that had never been done**.

**The decision has already been made: ship as-is and accept the risk.** Nothing here
argues against that, and nothing here should be executed now. This is the artifact to read
*later*, when there is a reason to act — a takedown notice, a storefront submission, a
publisher conversation, or simply time to do it properly. Its job is to make that future
decision cheap: what is in here, where it probably came from, how confident that is, what
it costs to remove, and in what order to remove it.

### How to read the confidence column

**Nobody has listened to a single one of these audio files or looked at a single one of
these sprites.** Not the prior recon, not this audit. `.shp` and `.aud` are opaque binary
formats and no rendering pass was done. Every statement about *which game an asset came
from* is an **inference from two things only**:

1. the filename, matched against Westwood's internal actor-ID and voice-line naming
   conventions (e.g. RA2 names infantry voice lines `i<unit><action><index>` — `iseaata`
   parses as *infantry / SEAL / attack / A*); and
2. what this repo's own YAML says the asset is wired to.

That is genuinely good evidence — Westwood's schemes are consistent and well documented by
the modding community — but it is evidence about a *name*, not about the bytes. A file
called `rhinicon.shp` could contain anything.

By contrast, everything in the **file count**, **reachability** and **what breaks** columns
was mechanically verified in this audit and is reliable. Where the two disagree, trust the
reachability.

Confidence is stated as **high / medium / low**, and "unknown" is used freely. A guess
labelled as a guess is useful; a guess labelled as a fact is not.

---

## 1. Headline numbers

| | Count |
|---|---|
| Binary asset files committed under `mods/ww3mod/` | **1,246** (1,233 in `bits/` + 12 in `uibits/` + 1 font) |
| On-disk size of `mods/ww3mod/bits/` | **26 MB** |
| Committed files with **no textual reference anywhere** in the mod | **201** |
| Committed files additionally unreachable for structural reasons | **+261** (all of `bits/misc/tiles/` — see §5) |
| Audio files committed | **238** (214 `.aud`, 24 `.wav`) |
| Imported voice-line sets | **9 directories, 158 files** — of which **only 2 sets / 34 files are attached to a live unit** |
| Voice files in the **no-freeware-release tier** (RA2 / Generals, medium-high confidence) | **91** |
| Committed music tracks | **1** (`journey.aud`) |
| Committed video files | **0** — this area is clean |

**The single most useful fact in this document:** of the nine imported voice-line
directories, **seven are dead** — declared in `voices.yaml` but attached to no actor, so
nothing in the game can ever play them. That is 124 files, including the entire Generals
set, removable at literally zero gameplay cost. See §4.1.

---

## 2. Category A — mounted at runtime from the player's own install (NOT the exposure)

`mods/ww3mod/mod.yaml:14-40` mounts the player's Red Alert data out of
`^SupportDir|Content/ra/v2/` — `~conquer.mix`, `~speech.mix`, `~sounds.mix`,
`~temperat.mix`, `~scores.mix` and the rest. `mod.yaml:402-419` declares `base`,
`aftermathbase` and `cncdesert` as `Required: true`, so the mod **refuses to launch**
without them, and `installer/downloads.yaml` fetches them from
`openra.net/packages/*-mirrors.txt` mirror lists — **WW3MOD hosts no bytes itself**.

The overwhelming majority of what a player sees and hears comes from here: roughly 69% of
referenced audio, all 88 music entries but one, ~121 of 124 EVA notification sounds, and
most terrain. None of it is redistributed.

**This is the standard OpenRA content-dependency posture that every OpenRA mod uses, and it
is not the exposure.** A future reader should not spend time on it. It is documented here
only so that the distinction between "the game loads it" and "this repo ships it" is
unambiguous.

One nuance worth knowing, because it looks alarming and isn't: the mod mixes redistributed
and mounted art in the same UI. Several cameos referenced by sequences
(`fpwricon`, `syrdicon`, `facficon`, `samicon`, `badricon`, `dtrkicon`, `bioicon`, …) are
**not** in this repo and resolve from the player's `conquer.mix`. That is Category A
behaviour and is fine.

---

## 3. Risk tiers

Used throughout the tables below.

| Tier | Meaning |
|---|---|
| **T1 — Low** | Not Westwood/EA at all: OpenRA's own GPLv3 assets, GNU FreeFont, or plainly modern real-world hardware with no C&C equivalent. Exposure here is *attribution hygiene* (unknown third-party authorship, uncredited), not EA. |
| **T2 — Moderate** | Red Alert 1 / Counterstrike / Aftermath / Tiberian Dawn derived. EA released RA1 and TD as freeware in 2008, and OpenRA's entire existence rests on that. Redistribution still is not clearly licensed — freeware ≠ redistributable — but there is a defensible, widely-relied-upon position and a decade of unchallenged precedent. |
| **T3 — High** | Red Alert 2 / Yuri's Revenge / Command & Conquer: Generals derived. **No freeware release exists for these titles.** There is no position to fall back on. This is the tier that actually matters. |
| **T?** | Origin genuinely undetermined. Treated as T3 for planning purposes until someone looks. |

---

## 4. Category B — files this repo actually redistributes

### 4.1 Imported voice-line sets — `mods/ww3mod/bits/sounds/<dir>/`

All nine directories are mounted (`mod.yaml:76-84`) and all nine are declared as voice sets
in `rules/sound/voices.yaml`. **But a declared voice set does nothing unless an actor
attaches it via `Voiced: VoiceSet:`** — and only two do. Verified by grepping every
`VoiceSet:` value in the mod: the complete set of attachments is `GenericVoice` ×3,
`CivilianFemaleVoice` ×5, `VehicleVoice` ×2, and one each of `SealVoice`, `IskanderVoice`,
`MedicVoice`, `EngineerVoice`, `EinsteinVoice`, `CivilianMaleVoice`. Nothing else.

| Dir | Files | Filename scheme | Inferred origin | Conf | Voice set | Attached to | What breaks if removed | Tier |
|---|---|---|---|---|---|---|---|---|
| `seal/` | 18 | `isea*` — RA2 `i`+unit+action+index | **RA2** Navy SEAL | med-high | `SealVoice` | **`^SF` → "Special Forces"** (`rules/ingame/infantry.yaml:2138`) | Special Forces infantry goes silent (select/move/attack/die). Unit still fully playable. | **T3** |
| `v3/` | 16 | `vv3l*` — RA2 `v`+unit+action | **RA2** V3 Rocket Launcher (RA1 stops at V2) | med-high | `IskanderVoice` | **`iskander` → "Iskander"** (`rules/ingame/vehicles-russia.yaml:1012`) | Iskander launcher goes silent. Unit still fully playable. | **T3** |
| `terroist/` | 30 | `iter*` | **RA2** Terrorist | med-high | `FanaticVoice` | — **nothing** | Nothing. | **T3** |
| `glabike/` | 27 | `vcyc*` | **Generals** GLA Combat Cycle ("GLA" is unambiguous) | med-high | `CycleVoice` | — **nothing** | Nothing. | **T3** |
| `chem/` | 21 | `chemhitburn`, `chemkillmelted` — descriptive English, *not* a Westwood scheme | RA2 Desolator is the obvious candidate, but the naming has been rewritten so the trail is gone | **low** | `ChemVoice` | — **nothing** | Nothing. | **T?** |
| `commando/` | 12 | `r*1.aud` (`rtuffguy1`, `rrokroll1`, `ramyell1`) | **Tiberian Dawn** Commando | med | `CommandoVoice` | — **nothing** | Nothing. | T2 |
| `volkov/` | 11 | `sv2*` | **RA1** Counterstrike/Retaliation — Volkov | med | `VolkovVoice` | — **nothing** | Nothing. | T2 |
| `robot/` | 11 `.wav` | `vsen*` | Fits the RA2/TS `v`+unit+action scheme, but no unit ID resolves cleanly, and `.wav` (not `.aud`) points at a Generals-era source. **Genuinely undetermined.** | **low** | `RoboticVoice` | — **nothing** | Nothing. | **T?** |
| `informan/` | 12 | `InformantVoiceSelect01` — CamelCase, matches no Westwood convention at all | Unknown. Could be community-made, another mod, or TTS. | **low** | `InfoVoice` | — **nothing** | Nothing. | **T?** |

**Totals: 158 files. 34 live (2 units affected). 124 dead.**

Two loose ends found while verifying, harmless but worth recording so nobody re-derives
them: `voices.yaml:213` references `rkeepem1` and `:229` references `iseafea`, and neither
file exists in the corresponding directory. Dead references in an already-dead direction.

> **The deleted precedent.** `bits/sounds/arabs/` (32 files) was exactly this pattern —
> mounted nowhere, referenced by nothing — and was removed in `aee3a02e`. The seven dead
> directories above are the same case at 4× the scale.

### 4.2 Loose sound effects — `mods/ww3mod/bits/sounds/*.aud|.wav` (79 files)

Flat files at the root of `bits/sounds/`, mounted at `mod.yaml:75`.

- **31 are referenced** by weapon/structure/notification YAML.
- **48 have no reference anywhere.** These include the entire `vgatlo*` set (10 files),
  `vtadatt*` (3), `vflaat1*` (2), `vrobon`/`vroboff`, plus one-offs like `chronowarp.aud`,
  `sspysate.aud`, `subdril1.aud`, `scrin5b.aud`, `ion1.aud`'s neighbours, `icbm1.aud`,
  `gun131/132`, `expnew16/17`, `bigggun1.aud`.
- Of the 31 "referenced", at least **5 are referenced only from dead code**:
  `rocket1TD.aud` is cited solely by `rules/ingame/naval.yaml`, which has **zero
  non-comment lines** (the entire naval branch is commented out); and
  `candy.aud`/`pickclean.aud`/`fivefinger.aud` are cited solely by `ThiefVoice`, which no
  actor attaches. `cram.wav` belongs to the `CRAM` structure, which carries
  `Prerequisites: ~disabled`.

**Provenance split, by naming scheme:**

| Group | Files | Inferred origin | Conf | Tier |
|---|---|---|---|---|
| `abrams-firing-1/2/3.wav`, `60mm-mortar-firing-1/2/3.wav`, `a10gun.wav`, `laser-beam.wav`, `cram.wav` | 9 | Descriptive English filenames, `.wav`, no Westwood scheme — sound-library or field recordings. **Not Westwood.** | med-high | **T1** |
| `vgatlo*`, `vtadatt*`, `vflaat1*`, `vhumwe2*`, `vrobon/vroboff`, `bpriat1a`, `bpripow`, `bgraatta`, `prisfire`, `bctrinit` | ~25 | The `v`/`b` + 3-letter-unit + action prefix is the **RA2** scheme. `bpri*`/`prisfire` = Prism Tower; `vgatlo*` = a gatling unit; `bctrinit` = a construction yard init. | med | **T3** |
| `gun5TD.aud`, `rocket1TD.aud`, `ion1.aud`, `scrin5b.aud`, `sonic4.aud` | ~5 | The literal `TD` suffix, plus Ion Cannon / Scrin / sonic — **Tiberian Dawn / Tiberian Sun** lineage. | med | T2 |
| `xplos`, `xplosml2`, `tnkfire2/3`, `mgun11`, `hvygun10`, `constru2`, `dropup1`/`dropdwn1`, `nade`, `flamer2`, … | ~40 | Classic RA1 8.3 SFX names. | med | T2 |

### 4.3 Unit sprites — `mods/ww3mod/bits/units/` (305 files)

The sound folders were the tip: the corresponding **sprites were imported too**. But the
survey turned up something better than expected — **almost all of the RA2/Generals sprite
imports are already dead**, and several files that *look* RA2 by name are load-bearing for
completely different, modern units.

#### 4.3.1 T3 sprites — imported and unreachable (safe to delete, breaks nothing)

Verified: no sequence binding, or a sequence binding whose parent actor is commented out /
does not exist.

| File(s) | Inferred origin | Conf | Reachability evidence |
|---|---|---|---|
| `infantry/seal.shp`, `sealswim.shp`, `sealswimidle.shp` | RA2 Navy SEAL | med | No YAML reference at all. (The live `SF` unit borrows the SEAL *voice* but uses `sf.shp`.) |
| `infantry/shok.shp` | RA2 Shock Trooper | med | No reference. |
| `infantry/boris.shp` | RA2 **Yuri's Revenge** Boris — YR-exclusive character | high | Sequence node `sequences-infantry.yaml:1804`; no actor. |
| `infantry/terr.shp` | RA2 Terrorist (pairs with `sounds/terroist/`) | med | Sequence node `:1858`; no actor. |
| `infantry/bikinitany.shp` | RA2-era Tanya variant / fan art | low | No reference. |
| `infantry/hack.shp`, `rmboc.shp`, `gnrl.shp`, `mech.shp`, `electdog.shp`, `rmbo.shp` | mixed RA2 / TD (`rmbo` = TD Commando) | low-med | No actor for any. |
| `vehicle/v3.shp`, `v3rl.shp` | RA2 V3 Rocket Launcher | med-high | No reference. |
| `vehicle/prsm.shp`, `prsm-tur.shp` | RA2 Prism Tank | med | `sequences.yaml:445`; actor `PRSM` **commented out** (`vehicles-america.yaml:1140`). Only an orphan `PRSM.husk` remains. |
| `vehicle/mgg.shp` | RA2 Mirage/Gap Generator | med | `sequences.yaml:637`; no actor. |
| `vehicle/behe.shp` | unknown | low | Bound to `5TNK.destroyed`; no `5TNK` actor. |
| `vehicle/timberwolf.shp` | Not C&C at all (MechWarrior name) — third-party | low | Actor commented out (`vehicles.yaml:693`). |
| `buildings/pris.shp`, `prisfire.shp`, `prismake.shp`, `prismmuzzle.shp` | RA2 Prism Tower | med | `sequences-defenses.yaml:793+`; actor `PRIS` **commented out** (`structures-defenses.yaml:1020`). |
| `aircraft/glabomber.shp` | **Generals** GLA bomber | med | Bound only to `BULL.Husk`; no `BULL` actor. |
| `aircraft/king_raptor.shp`, `raptor.shp` | **Generals: Zero Hour** USA King Raptor | med-high | No reference at all. `king_raptor` is a Zero Hour-exclusive name. |
| `aircraft/MIG35.SHP`, `mi2.shp`, `mi8.shp`, `iroq.shp` | modern/unknown | low | No reference (`mi2` only in a commented line). |
| `naval/advmsub.shp`, `corv.shp`, `corv2.shp`, `ptrb.shp`, `ssam-mini.shp`, `ascr.shp`, `fsub.shp` | mixed RA2/RA1 | low-med | **The entire naval branch is dead** — `naval.yaml`, `naval-america.yaml`, `naval-russia.yaml` all have zero non-comment lines. |

#### 4.3.2 Load-bearing sprites with misleading names — do NOT delete these

This is the trap in this inventory, and the reason a name-based purge would break the game.

| File | Name suggests | Actually renders | Verified at |
|---|---|---|---|
| `vehicle/prsm-reversed.shp` | RA2 Prism Tank | **the M109 "Paladin" hull** | `sequences.yaml:195` |
| `vehicle/quad_turret.shp` | Generals GLA Quad Cannon | **the 2K22 Tunguska turret** | `sequences.yaml:383,387` |
| `vehicle/m270.shp` | M270 MLRS only | **also the BM-21 Grad and TOS-1 launchers** | `sequences.yaml:301,307,331,337` |
| `aircraft/halo.shp` | ambiguous | **the live Russian "Halo" transport** | `sequences-aircraft.yaml:114` |
| `infantry/poisondeath.shp` | one-off death anim | **the `die7` frame set for essentially every infantry unit in the mod** (20+ bindings) | `sequences-infantry.yaml` passim |

And the mirror-image trap — **four files named after live units are not what those units
render from**, so they are dead despite the name: `abrams.shp` (the Abrams renders from
`abrams-correction.shp`), `bradley.shp` (renders from RA1's `1tnk`/`1tnk-tur`),
`m109.shp` (renders from `prsm-reversed.shp`), `grad.shp` (renders from `grad-chassi.shp`).

#### 4.3.3 T1/T2 — the live unit roster

The units players actually field are two groups:

- **Modern real-world hardware** — `abrams-correction`, `t72`, `t90`, `bmp`, `btr`,
  `giatsint`, `iskander-chassi`/`-missile`, `himars`, `humvee`, `strykershorad`, `a10`,
  `f16`, `littlebird`, `hind`, `quadcopterdrone`. No Westwood equivalent exists for any of
  these, so they were drawn by third parties or the mod author. **T1 for EA purposes** —
  but their actual authorship is unknown and uncredited, which is its own (smaller,
  different) problem. See §7.
- **RA1-derived edits** — a large share of `units/vehicle` and `units/buildings` carries
  stock RA1 actor IDs (`1tnk-tur`, `3tnk-1b`, `5tnk-tur`, `apc-tur*`, `arty-tur*`, `ftrk`,
  `harv*`, `truk`, `hhusk*`, `mcvhusk`, `tran1husk`, `apwr*`, `powr*`, `proc*`, `fact*`).
  **T2.**

### 4.4 Cameos — `mods/ww3mod/bits/misc/icons/` (260 files, all `.shp`)

Mounted at `mod.yaml:69`. Verified by whole-word grep across every mod YAML:
**205 referenced, 55 with no reference at all.**

The 55 dead cameos are: the 52 from the first-pass scan (`1tnficon 2tnficon 3tnkicon
3tnkonebarlicon 5tnkicon acaricon anticon apcicon artficon atekicon bminicon cyclicon
demoicon dtrkbcon e5icon e7icon fsubicon ftrkicon gainicon gapcicon glabikeicon hapcicon
heliicon howiicon iroficon ironicon iroqicon iskandericon_ katyicon lansicon mgnmicon
para1tnkicon parashokicon pbticon pwrficon qtnkicon rdrscnicon rhinicon rmbocicnh rshpicon
sealicon seasicon shokbcon shokicon smokicon snipericon sovharvicon t72icon_ tnkkicon
v2rlicon v3rlicon volkicon`) plus **`caicon`, `ionicon`, `missicon`**, which a naive
substring scan misses because they appear inside longer unrelated strings
(`e1americaicon`, `ObserverProductionIcons`, `cmissicon`).

**Provenance is weaker here than anywhere else in this document**, because a cameo is a
64×48 bitmap with an 8-character name and nothing else to go on. Broad shape:

- **T2, high confidence** — the bulk. RA1 ships cameos as literally `<ACTORID>ICON.SHP`,
  and ~120 stems are exact RA1 actor IDs (`apcicon`, `artyicon`, `mcvicon`, `v2rlicon`,
  `tslaicon`, `pboxicon`, `facticon`, the whole `…F…ICON` fake-structure family, the wall
  set `sbag/fenc/barb/brik/cycl`).
- **T3, medium confidence** — `borisicon` (Yuri's Revenge-exclusive character, the
  strongest single call in the group), `rhinicon` (RA2 Rhino tank), `prisicon`/`prsmicon`,
  `v3rlicon`, `glabikeicon`/`glaboaticon` (Generals). Roughly **10–15 files**. Note that
  Generals is a 3D game shipping TGA cameos, not SHP — so a `.shp` here is a *conversion or
  a redraw inspired by* Generals rather than a lifted Westwood file, which is a
  meaningfully weaker claim against it.
- **T1** — ~46 files named `<role><faction>icon` (`aaamericaicon`, `snrussiaicon`,
  `mtamericaicon`, …). The naming scheme is WW3MOD's own, so the filenames are certainly
  mod-authored. **But the stems `e1`/`e2`/`e3`/`medi`/`spy`/`tecn` are RA1 IDs, so the
  pixels may well be recoloured RA1 cameos.** Filename tells you nothing here. Flagged for
  visual review.
- **Unknown, will not guess** — `acaricon gainicon mgnmicon pbticon tnkkicon gapcicon
  hapcicon fsubicon rshpicon seasicon e5icon bminicon demoicon smigicon nebricon frepicon
  infxicon mainticon missicon spmsticon timberwolficon jmin scrate-healup` and others.
  Most are in the dead-55 and have no actor to disambiguate them.

**The useful correlation:** the dead-55 skews *heavily* toward the most-likely-Westwood
names (`3tnkicon`, `shokicon`, `qtnkicon`, `ironicon`, `rhinicon`, `v2rlicon`, `v3rlicon`,
`sealicon`, `volkicon`, `atekicon`, `apcicon`). Removing them removes a disproportionate
share of the highest-attribution-risk art at zero gameplay cost.

### 4.5 Weapon-effect sprites — `mods/ww3mod/bits/weapons/` (70 files, 3.8 MB)

Mounted at `mod.yaml:65-67`. **This is the largest block of committed, live, unattributed
sprite art in the repo**, and it was invisible to the audio-only recon.

- **8 files are byte-identical to `engine/mods/ra/bits/`** (`bubbles`, `fb3`, `fb4`,
  `gunfire2`, `napalm1`, `playersmoke`, `wpiff`, `wpifpif`) — upstream OpenRA's own, **T1**.
- **62 files exist nowhere in the vendored engine tree.** Includes the whole `flak_*`,
  `nuke_*`, `chem*`, `flame-*`, `tracer_*`, `shrapnel_*`, `pulsefx*` families plus
  `ionsfx`, `miniatomsfx`, `pulsball`, `empfx*`, `smoke_mtd`, `380mm`.
  - `ionsfx`, `miniatomsfx`, `chemball`, `pulsball` read as **Tiberian Sun-lineage** effect
    art (**T2**, low-med confidence); `smoke_mtd` and `380mm` read as imports from other
    C&C mods (**T?**).
  - 16 of the 62 are unreferenced (all eight directional `chem-*`, the three `chemball*`,
    `empfx02`, `fb6_1`, `nuke_1000_start_shockwave`, `nuke_small`, `pulsball`).
- **`credits.txt`'s ART section is literally `(nothing here yet)`** — see §7.

### 4.6 Terrain tiles — `mods/ww3mod/bits/misc/tiles/` (261 files, 1.1 MB)

**The strangest finding in the audit, and the cheapest thing on the whole list to remove.**

- All 261 are **byte-identical (md5-verified) to files under `engine/mods/ra/bits/`** —
  a straight duplicate of upstream OpenRA's own RA tile art. **T1/T2, and inherited from
  OpenRA rather than created by WW3MOD.**
- **All 261 are structurally unreachable at runtime.** `mod.yaml:72` mounts
  `ww3mod|bits/misc/tiles`, but the files live one level down in `desert/` (154),
  `tem/` (67), `sno/` (27) and `int/` (13) — and `Folder.Contents` enumerates with
  `SearchOption.TopDirectoryOnly` (`engine/OpenRA.Game/FileSystem/Folder.cs:35`). The mount
  therefore exposes **zero files**. Terrain resolves instead from the player's
  `temperat.mix`/`snow.mix`/`interior.mix`/`desert.mix` and from `ra|bits` (`mod.yaml:42-43`).
- Independently, 87 of the 261 are orphans in the tileset YAML too (all 13 `int/` among
  them).

**261 committed files that no code path can load, duplicating art already present twice
over in the same repo. Deleting them changes nothing at runtime.**

### 4.7 Resources, UI, palettes, fonts

| Group | Files | Finding | Tier |
|---|---|---|---|
| `bits/misc/resources/` | 68 | `gem01-04`, `gold01-04`, `scrap01-20` × `.des/.sno/.tem`. **Match nothing in the engine tree** — unique to WW3MOD. Live (`sequences-misc.yaml:136,140,144`). "Scrap" is not an RA1 resource name; Westwood-style extensions but unknown authorship. | **T?** |
| `bits/misc/ui/` | 24 | 13 byte-identical to `engine/mods/ra/bits/`; 1 differs (`gpsdot.shp`); **10 are unique** — `cmisscurs`, `cursorairstrike`, `cursoremp`, `cursorparadrop`, `hvnd`, `radarcratetd`, `tag-*`. The `tag-*` set is plainly WW3MOD's own (tag-mig, tag-reload). `radarcratetd` — the `td` suffix infers Tiberian Dawn. 4 are unreferenced. | T1/T2 |
| `mods/ww3mod/uibits/` | 12 `.png` | 8 share names with `engine/mods/ra/uibits/` and **all 8 differ** (loadscreen 6 KB vs 16 KB, sidebar 84 KB vs 131 KB) — genuinely re-skinned WW3MOD chrome, not copies. 4 are new (`flags*.png`, `sidebar-other.png`). | **T1** |
| `bits/misc/palettes/` | 4 | `anim.pal`, `temperattd.pal`, `unittem.pal` (768 B each = raw 256×3 VGA), `gensmkexploj.pal` (GIMP/JASC text). A 768-byte colour table is thin copyright material — practical exposure ≈ zero. The `td`/`gen` name fragments infer Tiberian Dawn / Generals origin, which is a provenance-hygiene note rather than a redistribution one. | T1 |
| `mods/ww3mod/ZoodRangmah.ttf` | 1 | **Byte-identical to `engine/mods/ra/ZoodRangmah.ttf`** — copied from the vendored engine, which upstream OpenRA ships. A Persian/Arabic display face. **Its license is stated nowhere in this repo** — not in `COPYING`, not in `engine/AUTHORS`, not in `credits.txt`. Low risk (upstream distributes it) but undocumented. Declared at `mod.yaml:308,312`. | T1 |
| Other fonts | — | `FreeSans.ttf` / `FreeSansBold.ttf` from `engine/mods/common/` — GNU FreeFont, GPL-compatible. Clean. | T1 |

### 4.8 Videos — clean

**Zero** tracked files matching `.vqa .wsa .bik .webm .mp4 .avi .mov .ogv` anywhere in the
repo. The movies mount is commented out (`mod.yaml:18`). The `movies-allied` /
`movies-soviet` packages (`mod.yaml:420-425`) list ~105 `.vqa` names, but only as
`TestFiles:` paths under the *player's* SupportDir. This is the one area with nothing to fix.

### 4.9 The vendored engine tree

The OpenRA engine is vendored in-repo at `engine/`, and `mod.yaml:42-45` mounts `ra|bits`,
`ra|bits/desert` and `ra|uibits`. `engine/mods/ra/` commits 154 `.des`, 105 `.shp`,
77 `.png`, 68 `.oramap`, 67 `.tem`, 66 `.bin`, 27 `.sno`, 14 `.aud`, 13 `.int`, 2 `.pal`,
1 `.ttf`. `engine/mods/common/` commits only the two FreeFont files.

**These are upstream OpenRA's own shipped assets, distributed by OpenRA itself under
GPLv3**, and no evidence was found of WW3MOD adding anything here — the `mods/ww3mod/`
copies are duplicates *of* these, not the reverse. WW3MOD redistributes them transitively
by vendoring. **T1.** No action indicated.

---

## 5. Fast mitigation plan

**Do not execute any of this now.** This is the order to work in *if* exposure ever needs
shedding quickly, sorted by risk reduction per unit of breakage. Steps 1–4 are free.

| # | Action | Files | Gameplay cost | Why this order |
|---|---|---|---|---|
| **1** | Delete the **7 dead voice directories** — `terroist/ glabike/ chem/ commando/ volkov/ robot/ informan/` — and their `voices.yaml` blocks (`FanaticVoice`, `CycleVoice`, `ChemVoice`, `CommandoVoice`, `VolkovVoice`, `RoboticVoice`, `InfoVoice`) | **124** | **None.** No actor attaches any of them. | Highest T3 density in the repo, zero cost. Removes the entire Generals set and most of the RA2 set in one move. Same shape as the already-executed `arabs/` deletion. |
| **2** | Delete the **55 unreferenced cameos** (§4.4) | **55** | **None.** | Skews hard toward the most-attributable Westwood names. Verify each against the list in §4.4 first — three of them (`caicon`, `ionicon`, `missicon`) fool a substring scan. |
| **3** | Delete the **T3 unreachable sprites** in §4.3.1 | **~40** | **None.** Every one is either unbound or bound to a commented-out actor. | Kills the RA2 Prism line, the V3, the SEAL/Shock/Boris/Terrorist infantry, the Generals bomber and King Raptor, and the whole dead naval branch. **Read §4.3.2 first** — do not pattern-match on names. |
| **4** | Delete `bits/misc/tiles/` entirely | **261** | **None.** Structurally unreachable (`Folder.cs:35`) and byte-identical to the engine tree. | Free, and removes the single largest redistributed file group. |
| **5** | Delete the **48 unreferenced loose SFX** + the ~5 referenced-only-from-dead-code ones (§4.2) | **~53** | **None**, but each needs its own check — the naval/ThiefVoice/CRAM cases show references can be live-looking and dead. | |
| **6** | Delete the **16 unreferenced weapon-effect sprites** (§4.5) | **16** | **None.** | |
| — | *Everything above is free. Below this line, things break.* | | | |
| **7** | Replace the **SEAL voice** (18 files) on Special Forces | 18 | Unit silent until replaced | The only two remaining T3 audio items. Both are single-unit, single-`VoiceSet` swaps — the cheapest possible replacement shape. |
| **8** | Replace the **V3 voice** (16 files) on the Iskander | 16 | Unit silent until replaced | |
| **9** | Audit the ~46 `<role><faction>icon` cameos visually (§4.4) | ~46 | Blank cameos if wrong | Requires actually *looking* at them. Cannot be resolved from filenames. |
| **10** | Address the RA1/TD tier (T2) | ~800+ | Total rebuild | Only if the freeware position is ever judged insufficient. This is a different project, not a cleanup. |

**Steps 1–6 remove ~549 files — roughly 44% of everything this repo redistributes — with
zero gameplay impact and no replacement work.** That is the number worth remembering.

After any deletion pass: run `make test` (YAML validation) and `make nav-guard`, and
grep for now-dangling references. Deleting the voice directories requires removing the
matching `voices.yaml` blocks and the `mod.yaml:76-84` mount lines in the same commit.

---

## 6. Replacement notes

Short and factual; not a shopping list.

- **Unit voice lines are the tractable part.** Only two sets are live (§4.1), each ~17 short
  barks attached to one unit via one `VoiceSet:`. Modern TTS handles military-radio barks
  well, and the mod's own naming means a drop-in replacement needs no YAML change beyond
  the filenames. This is a few hours of work, not a project.
- **SFX have large CC0 libraries** (freesound.org CC0 pool, BBC Sound Effects archive under
  its own terms). The nine existing `.wav` files (`abrams-firing-*`, `60mm-mortar-firing-*`)
  already demonstrate the pattern — descriptive names, `.wav`, wired directly into
  `weapons-ballistics.yaml`.
- **Ogg is already available.** `mod.yaml:327` now includes `Ogg` in `SoundFormats` (enabled
  in `aee3a02e`), and NVorbis is already a dependency. Replacements do not have to be `.aud`.
  Constraints: mono or stereo only (≥3 channels are silently mis-sent to OpenAL), 16-bit.
  See `DOCS/reference/architecture.md` § Audio pipeline.
- **Sprites are the expensive side.** Every T3 sprite in §4.3.1 is already dead, so the
  realistic sprite work is *deletion*, not replacement. The genuinely uncertain group is the
  live modern-hardware roster (§4.3.3) — third-party art of unknown authorship. That needs
  provenance research, not redrawing.

---

## 7. Two adjacent problems worth recording

**`credits.txt` is currently inaccurate.** `mods/ww3mod/credits.txt:34-41` states:

> WW3MOD loads artwork and audio from the Command & Conquer: Red Alert data files. Those
> files are supplied by the player from the 2008 freeware release or from an original copy
> of the game, **and are not distributed with WW3MOD.**

That last clause is false as written. The repo distributes 238 audio files, 305 unit
sprites, 260 cameos, 70 effect sprites, 68 resource sprites, 261 tiles and 4 palettes. The
`MUSIC`, `ART` and `SOUND` sections directly below it all read `(nothing here yet)`. This
is worth fixing independently of any asset decision — a wrong disclaimer is worse than a
missing one, and it is cheap to correct.

**Unattributed third-party art.** The live modern-hardware roster (§4.3.3), the 62 unique
weapon-effect sprites (§4.5) and the 68 resource sprites (§4.7) are not Westwood exposure —
but their actual authors are unknown and uncredited. For a community mod that is a
different and smaller problem than EA, though it is the one most likely to generate an
actual complaint from an actual person.

---

## 8. The music situation (adjacent, recorded here so it is in one place)

- **A stock install plays exactly one track, on infinite loop.** `rules/sound/music.yaml`
  declares 88 tracks, but a track that cannot be opened is silently dropped
  (`Ruleset.InstalledMusic` filters on `.Exists`), so `music.yaml` is a superset
  declaration, not a manifest. The only music file in the repo is
  `bits/sounds/music/journey.aud` (`music.yaml:81`), and a one-entry playlist wraps back
  onto itself.
- **Why:** the `music: Base Game Music` package (`mod.yaml:417-419`) is the **only**
  content package with no `Download:` key, and `installer/downloads.yaml` has no music
  entry. Quick Install therefore cannot fetch `scores.mix` — the install prompt says so
  outright (*"without music or videos"*).
- **Victory and defeat stings are wired but dead.** `rules/world.yaml:10-11` sets
  `VictoryMusic: score` / `DefeatMusic: map`. Both are `Hidden: true` entries living inside
  that same un-downloadable `scores.mix`, and `IGameOver.GameOver` guards on `SongExists`.
  So on a stock install the game simply does not change track at game end. There is also no
  `StartingMusic` and no `BackgroundMusic` anywhere in the mod — no menu theme, no briefing
  track.
- **Licensing note:** `journey.aud` is the one redistributed music file. "Journey" is
  most likely from Red Alert: Retaliation (the PlayStation release) rather than PC RA1 —
  **medium confidence, filename-only, nobody has listened to it.** If so it is **T2** but
  on the weaker end, since Retaliation is not covered by the 2008 PC freeware release.
- **Practical consequence:** the music slot is effectively empty and needs filling
  regardless of licensing. Ogg is enabled, loose files in `bits/sounds/music/` shadow
  same-named tracks in `scores.mix` (later mounts win), and `Length:` is not a YAML field
  at all — it is decoded at load and used only for the `M:SS` display label, so it cannot
  break playlist advance.

---

## 9. What was NOT verified — read this before acting

Stated plainly so nobody mistakes inference for fact later.

1. **No audio file was listened to. No sprite was viewed.** Zero. Every origin attribution
   in this document is filename-and-wiring inference. The `chem/`, `robot/` and `informan/`
   directories in particular have naming schemes that match *no* Westwood convention, and
   their origins are genuinely unknown — they are parked in T? on suspicion alone and could
   turn out to be entirely clean.
2. **Cameo attribution is the weakest section.** A 64×48 bitmap with an 8-character name is
   nearly evidence-free. The ~46 `<role><faction>icon` files are the sharpest case: the
   filenames are certainly mod-authored, but the pixels may be recoloured RA1 cameos and
   nothing short of looking at them will tell.
3. **"Referenced" ≠ "reachable in play."** The reachability scan is textual. A cameo can be
   named by an actor that is itself unbuildable — `CRAM` and `HGATE` both carry
   `Prerequisites: ~disabled`, and the naval branch is 100% commented out. So the
   205-referenced-cameo figure is an **upper bound** on live cameos, and the real dead set
   is larger than 55.
4. **Git history gave nothing.** Every asset was bulk-added in `c2bec47e "bits"` (2023-03-21)
   and `8b002c0b "Organizing bits folder"` (2024-05-07). There is no per-file provenance
   anywhere in the repo's history, and no README or manifest in `bits/`.
5. **The legal framing is a layman's.** The T2/T3 split rests on the plain observation that
   EA released RA1 and TD as freeware in 2008 and did not do so for RA2 or Generals. Whether
   freeware status permits redistribution *at all* is a separate question this document does
   not answer, and "OpenRA has done it for a decade unchallenged" is a description of
   practice, not a legal opinion.
6. **No cross-check against other OpenRA mods.** Several of these assets may be widely
   circulated community files with a known origin and even a stated license somewhere in the
   OpenRA modding community. Nobody looked. That search would likely resolve a good number
   of the T? entries cheaply.
7. **`engine/` was surveyed but not audited.** It was checked for what it commits and
   whether WW3MOD added to it (it did not). Upstream OpenRA's own asset licensing was taken
   at face value.

---

*Supersedes the audio-only recon in `WORKSPACE/DISCOVERIES.md` (2026-08-11, commit
`9c0bced9`), whose file counts predate the `arabs/` deletion in `aee3a02e` and are stale.
When assets change, update the counts here and re-stamp the SHA at the top.*
