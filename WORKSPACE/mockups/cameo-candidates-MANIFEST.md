# Strike-power cameo candidates — pilot batch

Sourced 2026-09-08 on `wt/cameo-sourcing`, branched from **main @ f83826d4**.
Roster read from `mods/ww3mod/rules/powers.yaml` and `mods/ww3mod/rules/ingame/nuclear-arsenal.yaml`
at that ref; each `Icon:` resolved to an art file through the `icon:` block of
`mods/ww3mod/sequences/sequences-misc.yaml:16-49`.

**Nothing here is installed.** No file under `mods/` was touched. Every render went through
the real path — `tools/cameo/convert.py`, default `--fit fill`, 64×48 RGBA, house 1px bevel,
no baked caption — so what you see is what `--install` would put on disk. The
"as the sidebar draws it" row composites the real caption band, real 7px FreeSansBold and the
real 13px `badge.py` trefoil via `tools/cameo/binmock.py`.

## Sheets

| Sheet | Cameo | Powers that draw it |
|---|---|---|
| `cameo-candidates-cmissicon.png` | `cmissicon` | W76-1 Trident Warhead (100 kt) |
| `cameo-candidates-atomfakeicon.png` | `atomfakeicon` | B83-1 (1.2 Mt) |
| `cameo-candidates-precicon.png` | `precicon` | GBU-57 Bunker Buster |
| `cameo-candidates-paranukeicon.png` | `paranukeicon` | B61-12 ×3, 9M729, Kinzhal-N, Kalibr, Tactical Strike — **6 powers** |
| `cameo-candidates-v2bdgricon.png` | `v2bdgricon` | Iskander-M, Sarmat MIRV, Strategic Strike — **3 powers** |
| `cameo-candidates-atomicon.png` | `atomicon` | Tsar Bomba (50 Mt) |

`kinzhalicon` is excluded — already settled.

## The two known-wrong ones: both confirmed, one worse than reported

- **`cmissicon` (W76-1)** — confirmed, and it is not only the wrong hazard symbol. The whole
  cameo is Red Alert's chemical-strike icon: a yellow biohazard trefoil **with `CHEMICAL STRIKE`
  baked into the pixels**. The sidebar currently tells the player that the submarine-launched
  warhead is a chemical weapon.
- **`atomfakeicon` (B83-1)** — confirmed. A yellow `FAKE` banner across a mushroom cloud, over a
  baked `NUCLEAR BOMB` caption.

Two more mismatches turned up unasked and are worth your eye while you are here:

- **`v2bdgricon`** is an aircraft photo captioned `CRUISE MISSILE`, and all three powers that
  draw it (Iskander-M, Sarmat, Strategic Strike) are **ballistic**, not cruise.
- **`paranukeicon`** is a cartoon free-fall bomb captioned `PARANUKE`. Fine for the three B61-12
  yields; wrong for the three missiles that also share it (9M729, Kinzhal-N, Kalibr).

## Licence policy as applied

PD / CC0 / CC-BY / CC-BY-SA only. Licence read off each Commons file page (the `extmetadata`
`LicenseShortName` the page itself renders), never inferred from the file being on Commons.
Rejects are listed at the bottom with reasons.

> **CC BY-SA is viral and five candidates carry it** (`w76e`, `b61e`, `v2e`, `tsara`, `tsarb`).
> A cameo derived from one is arguably a derivative work, so the shipped `.shp` would inherit
> share-alike in a public repo. Every one of those five has a PD or CC0 alternative in its own
> group. Flagged, not decided — that is your call, not mine.

## Candidates

★ = my recommendation for that cameo. `crop` = I pre-cropped the source before conversion
because the default centre-crop lost the subject.

### `cmissicon` — W76-1 Trident Warhead (100 kt)

| id | Source | Licence | Attribution needed | Why kept |
|---|---|---|---|---|
| **w76d** ★ | [USS Nebraska, San Diego, 4 Sep 2019](https://commons.wikimedia.org/wiki/File:USS_Nebraska_(SSBN-739)_launched_an_unarmed_Trident_II_D5_missile_off_the_coast_of_San_Diego_on_4_September_2019.jpg) | Public domain (US Navy) | none | Night launch: bright flame on pure black. Best tonal separation in the batch and the only one that still reads at 64×48 without hunting. Survives the caption band. |
| w76e | [UGM-133 Trident II inert](https://commons.wikimedia.org/wiki/File:UGM-133_Trident_II_inert.jpg) | **CC BY-SA 4.0** | "Thornfield Hall, CC BY-SA 4.0" | Static missile on a trailer, filling the frame. Highest subject-to-frame ratio here — reads unmistakably as "a big missile" rather than "a launch". Share-alike is the cost. |
| w76c | [Unarmed Trident launches from submarine (USN/Flickr)](https://commons.wikimedia.org/wiki/File:Flickr_-_Official_U.S._Navy_Imagery_-_Unarmed_Trident_missile_launches_from_submarine..jpg) | Public domain (US Navy) | none | Missile plus full plume against clean blue. Strong shape, but the missile itself is small — the plume does the reading. |
| w76a | [USS Rhode Island, Cape Canaveral, 9 May 2019](https://commons.wikimedia.org/wiki/File:An_unarmed_Trident_II_D5_missile_launches_from_USS_Rhode_Island_(SSBN-740)_off_the_coast_of_Cape_Canaveral_9_May_2019.jpg) `crop` | Public domain (John Kowalski, US Navy) | none | Breach-and-ignite over open sea. Cropped in hard; still the lowest-contrast of the four, grey on grey. Included as the "from the water" idea. |

### `atomfakeicon` — B83-1 (1.2 Mt)

| id | Source | Licence | Attribution needed | Why kept |
|---|---|---|---|---|
| **b83b** ★ | [B83 nuclear bomb trainer](https://commons.wikimedia.org/wiki/File:B83_nuclear_bomb_trainer.jpg) | Public domain (USAF / MSgt Ken Hammond) | none | White bomb body on a yellow cradle in a hangar. One subject, clean silhouette, and the yellow/white split survives the shrink. The straight replacement for the FAKE banner. |
| b83c | [F-5E Tiger II with B83, Hill AFB](https://commons.wikimedia.org/wiki/File:F5E_Tiger_II_B83_HAFB.jpg) `crop` | **CC0** | none | Cropped to the bomb under a yellow-and-white airframe. Reads as "a bomb on an aircraft", which is what a free-fall weapon is. CC0, so no licence tail at all. |
| b83a | [601010-F-0000H-003 B83 Nuclear Bomb](https://commons.wikimedia.org/wiki/File:601010-F-0000H-003_B83_Nuclear_Bomb.jpg) `crop` | Public domain (USAF, Hill AFB factsheet) | none | Same idea as b83b from a lower angle with an aircraft behind. Busier; kept as the alternate framing. |
| b83d | [081217-F-6821C-033](https://commons.wikimedia.org/wiki/File:081217-F-6821C-033.jpg) `crop` | Public domain (USAF Capt. Casey Collier) | none | Weapon being handled by a loading crew. Adds people and scale; weakest of the four once shrunk, because the crew competes with the bomb. |

### `precicon` — GBU-57 Bunker Buster

Only three survived. The category is small and half of it is video or unreadable at size.

| id | Source | Licence | Attribution needed | Why kept |
|---|---|---|---|---|
| **mopc** ★ | [DTRA personnel prepare to lift the MOP](https://commons.wikimedia.org/wiki/File:DTRA_personnel_prepare_to_lift_the_MOP_in_preparation_for_a_test.jpg) `crop` | Public domain (DoD / DTRA) | none | The rust-brown penetrator body on a flatbed under a crane. Warm subject against a cool background, huge in frame — the most legible conventional-strike image found. |
| mopb | [USAF MOP test release crop](https://commons.wikimedia.org/wiki/File:USAF_MOP_test_release_crop.jpg) | Public domain (DoD) | none | Clean profile of the weapon in flight against pale sky. Best *shape* reading — you can see it is long and thin, which is the whole point of a penetrator. Source is only 250×250, so it is soft at 64×48. |
| mopf | [Off loading of MOP cropped](https://commons.wikimedia.org/wiki/File:Off_loading_of_MOP_cropped.jpg) | Public domain (no author recorded) | none | Crane offload against mountains. Subject smaller than mopc; kept for the three-option floor. Commons records no author — fine for PD, but there is no attribution string if you wanted one. |

### `paranukeicon` — B61-12 ×3 / 9M729 / Kinzhal-N / Kalibr / Tactical Strike

Six powers draw this one file, so it has to read as "a tactical nuclear weapon" generically
rather than as any one delivery system.

| id | Source | Licence | Attribution needed | Why kept |
|---|---|---|---|---|
| **b61c** ★ | [F-35 B61-12 trial (cropped)](https://commons.wikimedia.org/wiki/File:F-35_B61-12_trial_(cropped).jpg) | Public domain (Los Alamos National Laboratory) | none | Full B61-12 in profile, white body with red bands, on flat blue. The clearest single object in the whole batch: one subject, hard edges, nothing behind it. |
| b61a | [B61 nuclear bomb](https://commons.wikimedia.org/wiki/File:B61_nuclear_bomb.jpg) `crop` | **CC0** | none | Three-quarter view of the casing with its blue band. Reads as metal ordnance but has no silhouette — mid-grey on mid-grey once shrunk. |
| b61d | [B61 silver bullet fusion bomb](https://commons.wikimedia.org/wiki/File:B61_silver_bullet_fusion_bomb.jpg) `crop` | Public domain (Greg Goebel) | none | Bombs on a display rack. Honest option, but the dark bomb against a warm wall reads as furniture at size — I would not pick it. |
| b61e | [NAM - B61 Nuclear Bomb](https://commons.wikimedia.org/wiki/File:NAM_-_B61_Nuclear_Bomb.jpg) `crop` | **CC BY-SA 2.0** | "Marshall Astor, CC BY-SA 2.0" | Museum piece stood vertically. Included only because a vertical framing is a genuinely different idea; the background clutters it and it carries share-alike. Weakest keep in the batch. |

### `v2bdgricon` — Iskander-M / Sarmat MIRV / Strategic Strike

| id | Source | Licence | Attribution needed | Why kept |
|---|---|---|---|---|
| **v2a** ★ | [Sarmat launch still](https://commons.wikimedia.org/wiki/File:Sarmat-launch-still.jpg) `crop` | **CC BY 4.0** | "Ministry of Defence of the Russian Federation, CC BY 4.0" | Silo launch: orange fire filling the lower half, missile body above it. Loudest image in the batch and unambiguous at any size. mil.ru CC-BY, exactly the source class the policy names. |
| v2d | [CombatLaunching2018-14](https://commons.wikimedia.org/wiki/File:CombatLaunching2018-14.jpg) `crop` | **CC BY 4.0** | "Алексей Иванов / Минобороны России, CC BY 4.0" | Erected Iskander against sky — clean dark-on-light silhouette, the best pure shape of the group. |
| v2b | [CombatLaunching2018-16](https://commons.wikimedia.org/wiki/File:CombatLaunching2018-16.jpg) `crop` | **CC BY 4.0** | "Алексей Иванов / Минобороны России, CC BY 4.0" | Missile nose with crew working at its base. Reads as a missile *being prepared*, which suits a called-in reserve strike. Busier than v2d. |
| v2e | [9P78-1 TEL Iskander-M](https://commons.wikimedia.org/wiki/File:9P78-1_TEL_Iskander-M.JPG) | **CC BY-SA 4.0** | "Boevaya mashina, CC BY-SA 4.0" | The launcher vehicle itself, travelling. Distinct idea, but at 64×48 it reads as "a green truck", and it carries share-alike. Included for completeness. |

### `atomicon` — Tsar Bomba (50 Mt)

The shipped art here is a mushroom cloud with a trefoil and is genuinely **fine**. A candidate
has to beat it, not merely differ. The three PD test photographs below are the only ones I think
do, and they do it by being real rather than by being different.

| id | Source | Licence | Attribution needed | Why kept |
|---|---|---|---|---|
| **tsarf** ★ | [Castle Romeo](https://commons.wikimedia.org/wiki/File:Castle_Romeo.jpg) | Public domain (US DOE) | none | The canonical red-orange fireball and stem. Enormous tonal range, unmistakable shape, and it is a real megaton-range detonation rather than a render. Best straight upgrade. |
| tsare | [Ivy Mike mushroom cloud](https://commons.wikimedia.org/wiki/File:Ivy_Mike_-_mushroom_cloud.jpg) | Public domain (US DOE) | none | The purple-white column. Reads as a *cloud* rather than a fire — cooler and calmer than tsarf, and it separates from a dark sidebar better. |
| tsard | [Castle Bravo Blast](https://commons.wikimedia.org/wiki/File:Castle_Bravo_Blast.jpg) | Public domain (US DOE) | none | Wide low fireball on the horizon. Historically the right yield class, but the low wide shape loses the mushroom silhouette at size. |
| tsara | [Tsar Bomba Revised](https://commons.wikimedia.org/wiki/File:Tsar_Bomba_Revised.jpg) `crop` | **CC BY-SA 3.0** | "User:Croquant, modified by User:Hex, CC BY-SA 3.0" | The actual RDS-220 casing in the Sarov museum — the only candidate that literally *is* the Tsar Bomba. Device-not-detonation is a real alternative idea. Share-alike. |
| tsarb | [Tsar Bomba.JPG](https://commons.wikimedia.org/wiki/File:Tsar_Bomba.JPG) | **CC BY-SA 3.0** | **cannot be written** — Commons records no author | Same casing with a man in frame for scale, which is the one thing the mockup argues for. But BY-SA requires attribution and there is nobody to attribute, so I would not ship it. Shown so you can see the scale idea; take it from tsara if you want it. |

## Rejected, and why

| Source | Reason |
|---|---|
| [Trident breaking surface, HMS Vanguard](https://commons.wikimedia.org/wiki/File:A_Trident_Missile_Breaks_the_Surface_After_Being_Fired_from_HMS_Vanguard_MOD_45151581.jpg) | **OGL v1.0** (UK MOD). A free licence, but neither PD nor CC, so outside the policy. Genuinely the best-composed Trident image found — say the word if OGL is acceptable and it goes straight in. |
| [Tsar Bomba fireball 1961](https://commons.wikimedia.org/wiki/File:Tsar_Bomba_fireball_1961.jpg) | Commons tags it PD, but its credit line reads "Cover Images / … / **Associated Press**" and points at `newsroom.ap.org`. Agency wire photo — rejected on the policy's own terms regardless of the tag. |
| [Trident II missile cropped](https://commons.wikimedia.org/wiki/File:Trident_II_missile_cropped.jpg) | Tagged PD, but sourced "High Res image from **Lockheed Martin**" with author unknown. A contractor is not the federal government, so PD-USGov does not obviously apply. No benefit of the doubt. |
| `B-83 nuclear weapon.jpg`, `B83 nuclear weapon (bw).jpg` | PD tag, but provenance is a scan out of Chuck Hansen's *Swords of Armageddon*. Dropped on legibility anyway — a cluttered parts layout — so the licence question never had to be settled. |
| `Deleted GBU-57 MOP photo (1).jpg` / `(2).jpg` | The filenames record a takedown somewhere upstream. Not worth inheriting. |
| `Ivan bomb.png` | Turned out to be a **range map**, not the device. CC BY-SA 3.0 with no author recorded, either. |
| `MOP in the B-2 bomb bay`, `B-52 releases the MOP`, `CombatLaunching2018-26`, `B61 inert training version`, `USAF MOP (tight crop)` | Licence was clean on all five (PD or CC-BY). Dropped purely on the 64×48 test: the bomb-bay close-up is grey mush, the release shots put the weapon three pixels wide in empty sky, the snowfield shot has no subject at all, and the tight crop is a 219×70 source that cannot survive a 4:3 fill. |

## What the sheets show, and one thing they should change about your pick

Each sheet has three rows: **actual size** (the verdict), **4×** (what is actually in the
picture), and **4× as the sidebar draws it** — real caption band, real 7px FreeSansBold, real
13px trefoil badge.

That third row is worth reading before you choose, because the dressing is not free. The caption
band covers the **bottom ~9 rows** of every nuclear cameo, and the badge takes the **bottom-right
13×13**. Any composition that puts its subject low or right loses it. That is why `w76d` (flame
centred, black surround) holds up, and it is the argument against `b61d` and `v2e` beyond their
tonal problems.

## Reproducing

Sources are not committed — they are re-downloadable from the URLs above. The scratch tree that
produced these sheets sits at `work/` in the `wt/cameo-sourcing` worktree and is deliberately left
**untracked** (it is not covered by `.gitignore`, so do not `git add -A` in this branch):
`work/src/` raw downloads, `work/src3/` post-crop inputs to `convert.py`, `work/staged2/` the
64×48 renders, `work/shipped/` the six current cameos decoded out of their SHPs, and
`work/manifest.json` the machine-readable version of the tables above, crop boxes included.
