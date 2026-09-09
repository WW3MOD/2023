# Strike-power cameos — sourced, installed, and what to adjust

Sourced 2026-09-08, installed 2026-09-09 on `wt/cameo-sourcing`, merged onto **main @ 74b559ed**.
Roster read from `mods/ww3mod/rules/powers.yaml`, `rules/player.yaml` and
`rules/ingame/nuclear-arsenal.yaml`; each `Icon:` resolved to an art file through the `icon:`
block of `mods/ww3mod/sequences/sequences-misc.yaml`.

**This is now an INSTALLED record, not a proposal.** Twelve art files were written into
`mods/ww3mod/bits/misc/icons/` and six new sequence keys wired. The candidate tables further down
are kept because they are the alternates: every one has already been rendered through the real
path, so swapping a pick is a one-line change and a rebuild.

Every render went through `tools/cameo/convert.py`, default `--fit fill`, 64×48 RGBA, house 1px
bevel, **`--no-baked-captions`** — the captions you see are drawn at runtime from
`CameoCaption`, not painted into the pixels. Installed contact sheet, as the sidebar draws it
(real caption band, real 7px FreeSansBold, real 13px trefoil):
[`cameo-installed-set.png`](cameo-installed-set.png), 1× in
[`cameo-installed-set-1x.png`](cameo-installed-set-1x.png).

## What was actually wrong, and the correction to this file's own count

Fifteen powers drew seven art files, and the powers are already 100% faction-unique — five
American, five Russian, five event-tier reachable by neither. The art was what crossed the line:

- **`paranukeicon` served SEVEN powers**, not six. An earlier version of this manifest said "6
  powers" in its table and then listed seven; **seven is correct** — B61-12 ×3 (America), 9M729,
  Kinzhal-N, Kalibr (Russia), Tactical Strike (event).
- **`v2bdgricon` served THREE**: Iskander-M (Russia), Sarmat and Strategic Strike (event).

So installing the user's `b61c` pick alone would have put a photograph of a US Air Force bomb on
three Russian rungs — trading a generic-wrong cartoon for a specific-wrong photograph, which is
worse. The fix is cosmetic only: six new entries in the `icon:` collection and each power's trait
pointed at its own. No prerequisite edits, no power splitting, no engine work.

## Installed — one cameo per power

| Power | Faction | `Icon:` key | Art file | Source | Licence | Attribution required |
|---|---|---|---|---|---|---|
| GBU-57 Bunker Buster | America | `precicon` | `precicon.shp` | **mopb** | Public domain (DoD) | none |
| B61-12 (0.3 kt) | America | `paranuke` | `paranukeicon.shp` | **b61c** | Public domain (Los Alamos) | none |
| B61-12 (10 kt) | America | `paranuke` | `paranukeicon.shp` | **b61c** | " | none |
| B61-12 (50 kt) | America | `paranuke` | `paranukeicon.shp` | **b61c** | " | none |
| W76-1 Trident (100 kt) | America | `cmissicon` | `cmissicon.shp` | **w76d** | Public domain (US Navy) | none |
| Kh-47M2 Kinzhal | Russia | `kinzhalicon` | `kinzhalicon.shp` | *(unchanged — user's photo)* | — | — |
| 9M729 (1 kt) | Russia | `ru9m729` **new** | `ru9m729icon.shp` **new** | **n729b** | CC BY 4.0 | yes |
| 9M723 Iskander-M (10 kt) | Russia | `ruiskander` **new** | `ruiskandericon.shp` **new** | **v2d** | CC BY 4.0 | yes |
| Kh-47M2 Kinzhal-N (50 kt) | Russia | `rukinzhaln` **new** | `kinzhalnicon.shp` **new** | **knzc** | CC BY 4.0 | yes |
| 3M14 Kalibr (100 kt) | Russia | `rukalibr` **new** | `kalibricon.shp` **new** | **kala** | CC0 | none |
| Tactical Nuclear Strike (20 kt) | event | `tacnuke` **new** | `tacnukeicon.shp` **new** | **tacb** | Public domain (LANL) | none |
| RS-28 Sarmat MIRV | event | `v2bdgricon` | `v2bdgricon.shp` | **v2a** | CC BY 4.0 | yes |
| B83-1 (1.2 Mt) | event | `abombfake` | `atomfakeicon.shp` | **b83c** | CC0 | none |
| Strategic Nuclear Strike (6 Mt) | event | `highyieldnuke` **new** | `stratnukeicon.shp` **new** | **tsarf** | Public domain (US DOE) | none |
| Tsar Bomba (50 Mt) | event | `abomb` | `atomicon.shp` | **tsard** | Public domain (US DOE) | none |

**Nothing installed carries CC BY-SA.** Seven files are public domain, two CC0, four CC BY 4.0.
Share-alike was avoided everywhere a comparable alternative existed, so no viral licence attaches
to anything in the repo. The four CC BY files all need an attribution string:

| Art file | Attribution string |
|---|---|
| `ru9m729icon.shp` | Константин Алыш (Konstantin Alysh) / Минобороны России, CC BY 4.0 |
| `ruiskandericon.shp` | Алексей Иванов / Минобороны России, CC BY 4.0 |
| `kinzhalnicon.shp` | Ministry of Defence of the Russian Federation, CC BY 4.0 |
| `v2bdgricon.shp` | Ministry of Defence of the Russian Federation, CC BY 4.0 |

## The judgement calls — the things to look at first and tell me to change

1. **The three B61-12 rungs still share one file, deliberately.** They are one physical weapon at
   three dial settings, and the captions (0.3 KT / 10 KT / 50 KT) are what separate them. Giving
   them three different photographs would tell the player they are three different weapons, which
   is the opposite of true. This is the one remaining share and it is the easiest to reverse:
   `b61a`, `b61d` and `b61e` are already rendered if you want them distinct.

2. **Kalibr is the weakest tile and it is a licensing problem, not a taste one.** Every clean-licence
   Kalibr launch photograph on Commons is a pale sky with a thread of smoke — invisible at 64×48
   (`kalc`, `kald` below, both tested and rejected on that). What shipped is the 3M-14 itself, CC0,
   cropped onto the dark nose cone so it has something to read against; the first crop was
   white-body-on-white-wall and was thrown away after seeing it in the slot. The genuinely good
   Kalibr photographs at Army/MAKS expos are all **CC BY-SA**, so they were passed over. Say the
   word if share-alike is acceptable and there is a better tile available.

3. **The Tsar Bomba tile is dimmer than the Strategic Strike tile below it in yield.** `tsard`
   (Castle Bravo) is a wide low fireball on a dark sky; `tsarf` (Castle Romeo) is a brilliant
   red-orange mushroom. Your pick for `atomicon` was `tsard` and it stands, but the ladder now
   reads loudest-in-the-middle. If that bothers you in game, swapping `atomicon` ← `tsarf` and
   `stratnukeicon` ← `tsard` fixes it with two file copies, or `tsare` (Ivy Mike, a purple-white
   column) gives the Tsar Bomba a colour nothing else in the tier has.

4. **Kinzhal and Kinzhal-N are both MiG-31s, and that is honest.** They are the same missile with
   different warheads. They are separated by sky colour (pale grey-white vs saturated cyan), by
   pose, and by the fact that only the nuclear one wears a badge and a yield. A shared *subject* is
   truthful; the shared *file* was not, because it made two powers one picture.

5. **9M729 is a class-correct image, not a picture of the weapon.** No usable free image of the
   9M729/SSC-8 exists — the only public photographs are from the 2019 Russian MoD press briefing.
   What shipped is a ground-launched **cruise** missile leaving an Iskander TEL (the R-500/9M728
   family the 9M729 belongs to), which is the right class, the right launch mode and visually
   distinct from the Iskander-M tile beside it. `n729d` (an RK-55 SSC-4 TEL, PD) was the other
   honest option and is monochrome.

6. **Tactical Strike is the only monochrome tile, on purpose.** Trinity was ~21 kt against this
   power's 20, so it is the right size of detonation as well as the right kind. The colour Trinity
   shot (`taca`) was dropped for two reasons: its credit line routes through the LIFE Photo Archive
   rather than a clean federal attribution, and the orange mushroom alternative (`tacc`) is hard to
   tell from the Strategic Strike tile two places along. `tacb` is Berlyn Brixner's own LANL
   exposure hosted on lanl.gov, and being the one black-and-white tile makes it unmistakable.

7. **GBU-57 gained a `CameoCaption` it did not have.** Its old art had `PRECISION STR.` painted
   into pixel rows 42–46; the photograph has nothing there, so without a stated caption the tile
   would have gone silent. Wording is identical to the lettering it replaces, and it is set in
   **both** `powers.yaml` and `player.yaml` — the buy tab and the power bin each read their own
   copy, which is exactly how three weapons drifted apart before 2026-09-08.

## New candidates sourced 2026-09-09

★ = installed. `crop` = pre-cropped before conversion because the default centre-crop lost the
subject.

### `ru9m729icon` — 9M729 (1 kt), Russia

| id | Source | Licence | Attribution | Why kept |
|---|---|---|---|---|
| **n729b** ★ | [Zapad-2017 exercise Leningrad oblast 05](https://commons.wikimedia.org/wiki/File:Zapad-2017_exercise_Leningrad_oblast_05.jpg) `crop` | **CC BY 4.0** | Константин Алыш / Минобороны России | A cruise missile leaving an Iskander TEL — right class, right launch mode. Orange flame with a dark missile body still visible above it, against a treeline: reads as *launching* rather than as an explosion, which is what separates it from the detonation tiles. |
| n729a | [Zapad-2017 … 03](https://commons.wikimedia.org/wiki/File:Zapad-2017_exercise_Leningrad_oblast_03.jpg) `crop` | CC BY 4.0 | as above | Same launch, later frame. Overhanging foliage frames it heavily and the flame is small against white sky. |
| n729c | [Zapad-2017 … 07](https://commons.wikimedia.org/wiki/File:Zapad-2017_exercise_Leningrad_oblast_07.jpg) | CC BY 4.0 | as above | Smoke column with coloured debris at the base. Busiest of the three. |
| n729d | [SS-C-4 Slingshot](https://commons.wikimedia.org/wiki/File:SS-C-4_Slingshot.JPEG) | Public domain (DoD) | none | The RK-55 TEL — the INF-era ground-launched cruise missile the 9M729 succeeded, so historically the most apt object here. Monochrome, and an inset close-up gives it two subjects. The alternate if you want a vehicle rather than a launch. |

### `kinzhalnicon` — Kh-47M2 Kinzhal-N (50 kt), Russia

| id | Source | Licence | Attribution | Why kept |
|---|---|---|---|---|
| **knzc** ★ | [VictoryDayParade2018-22](https://commons.wikimedia.org/wiki/File:VictoryDayParade2018-22.jpg) `crop` | **CC BY 4.0** | Ministry of Defence of the Russian Federation | MiG-31 in profile with the Kinzhal under the belly, against saturated cyan. The strongest dark-on-bright silhouette found, and the colour field is nothing like the conventional Kinzhal's pale grey-white — which is the whole job, since both powers carry the same missile. |
| knzb | [2018 Moscow Victory Day Parade 66](https://commons.wikimedia.org/wiki/File:2018_Moscow_Victory_Day_Parade_66.jpg) `crop` | CC BY 4.0 | The Presidential Press and Information Office | Same aircraft, higher resolution, but grey-on-pale-blue — too close to the shipped `kinzhalicon` to tell apart at size. |
| knzd | [Kh-47M2 Kinzhal Army-2022](https://commons.wikimedia.org/wiki/File:Kh-47M2_Kinzhal_Army-2022.jpg) | **CC BY-SA 3.0** | Boevaya mashina | Parked aircraft on a ramp. Rejected on legibility (grey mush at 64×48) before share-alike had to be argued about. |

### `kalibricon` — 3M14 Kalibr (100 kt), Russia

| id | Source | Licence | Attribution | Why kept |
|---|---|---|---|---|
| **kala** ★ | [Wikitrip to MAI museum 2016-02-02 021](https://commons.wikimedia.org/wiki/File:Wikitrip_to_MAI_museum_2016-02-02_021.JPG) `crop` | **CC0** | none (Krassotkin) | The 3M-14E itself — the exact designation in the power's name. Cropped onto the **nose end**, where the dark grey cone meets the dark blue backdrop; that contrast is the only thing in the frame that survives the shrink, and the wider crops that lost it read as a white smear. |
| kalc | [Russian submarines firing missiles against ISIS](https://commons.wikimedia.org/wiki/File:Russian_submarines_firing_missiles_against_ISIS.jpg) | CC BY 4.0 | Минобороны России | A real sea launch, which is thematically ideal for a sea-launched weapon. Blue sea and a thin white trail; at 64×48 it is a blue rectangle. Tested in the slot and rejected. |
| kald | [Запуск Калибров из Каспийского моря](https://commons.wikimedia.org/wiki/File:Запуск_Калибров_из_Каспийского_моря.jpg) | CC BY 4.0 | Минобороны России | Caspian Flotilla launch. Pale washed-out sky, faint smoke arc, tiny ship — the least legible candidate in the whole batch. |
| kalb | [Launch of the Caliber missile from the Volkhov submarine](https://commons.wikimedia.org/wiki/File:Launch_of_the_Caliber_missile_from_the_Volkhov_submarine.jpg) | CC BY 4.0 | Ministry of Defense of Russia | Grey sea, grey sky, small white missile. 550×390 source. Lowest contrast of the four. |

### `tacnukeicon` — Tactical Nuclear Strike (20 kt), event tier

| id | Source | Licence | Attribution | Why kept |
|---|---|---|---|---|
| **tacb** ★ | [Trinity Test Fireball 16ms](https://commons.wikimedia.org/wiki/File:Trinity_Test_Fireball_16ms.jpg) | Public domain | none (Berlyn Brixner / LANL) | The early fireball as a bright mottled dome on pure black — the highest-contrast tile in the tier, and the only one that is not a mushroom cloud, so it cannot be confused with Sarmat, Strategic or Tsar. Trinity was ~21 kt, so the yield matches too. Cleanest provenance of the three: LANL's own photographer, hosted on lanl.gov. |
| taca | [Trinity shot color (4x3 cropped b)](https://commons.wikimedia.org/wiki/File:Trinity_shot_color_(4x3_cropped_b).jpg) | Public domain (Jack W. Aeby) | none | The famous colour dome. Reads well, but its credit line routes through the Google-hosted LIFE Photo Archive rather than a clean federal attribution, and Commons carries another Aeby Trinity frame under CC BY-SA — enough ambiguity to prefer the alternative on a policy that rejects agency provenance. |
| tacc | [Trinity Detonation T&B (cropped 4-3)](https://commons.wikimedia.org/wiki/File:Trinity_Detonation_T%26B_(cropped_4-3).jpg) | Public domain (US DOE) | none | A big orange mushroom. Legible, but near-indistinguishable from the Strategic Strike tile two places along. |

### `stratnukeicon` — Strategic Nuclear Strike (6 Mt), event tier

Taken from the `atomicon` batch below rather than sourced fresh — **tsarf** (Castle Romeo, an
11 Mt shot) is the right yield class for a 6 Mt strike and was already the strongest detonation
render in the set. Public domain (US DOE), no attribution needed.

---

# Original candidate batch (2026-09-08)

Kept in full: these are the alternates for every cameo, all already rendered through the real path.
★ marks the pick that was installed.

## Licence policy as applied

PD / CC0 / CC-BY / CC-BY-SA only. Licence read off each Commons file page (the `extmetadata`
`LicenseShortName` the page itself renders), never inferred from the file being on Commons.
Rejects are listed at the bottom with reasons.

> **CC BY-SA is viral and five candidates carry it** (`w76e`, `b61e`, `v2e`, `tsara`, `tsarb`).
> A cameo derived from one is arguably a derivative work, so the shipped `.shp` would inherit
> share-alike in a public repo. **None of them was installed** — every group had a PD or CC0 or
> CC-BY alternative and it was taken.

## Candidates

★ = installed. `crop` = pre-cropped before conversion because the default centre-crop lost the
subject.

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
| **b83c** ★ | [F-5E Tiger II with B83, Hill AFB](https://commons.wikimedia.org/wiki/File:F5E_Tiger_II_B83_HAFB.jpg) `crop` | **CC0** | none | Cropped to the bomb under a yellow-and-white airframe. Reads as "a bomb on an aircraft", which is what a free-fall weapon is. CC0, so no licence tail at all. |
| b83b | [B83 nuclear bomb trainer](https://commons.wikimedia.org/wiki/File:B83_nuclear_bomb_trainer.jpg) | Public domain (USAF / MSgt Ken Hammond) | none | White bomb body on a yellow cradle in a hangar. One subject, clean silhouette, and the yellow/white split survives the shrink. The straight alternate. |
| b83a | [601010-F-0000H-003 B83 Nuclear Bomb](https://commons.wikimedia.org/wiki/File:601010-F-0000H-003_B83_Nuclear_Bomb.jpg) `crop` | Public domain (USAF, Hill AFB factsheet) | none | Same idea as b83b from a lower angle with an aircraft behind. Busier; kept as the alternate framing. |
| b83d | [081217-F-6821C-033](https://commons.wikimedia.org/wiki/File:081217-F-6821C-033.jpg) `crop` | Public domain (USAF Capt. Casey Collier) | none | Weapon being handled by a loading crew. Adds people and scale; weakest of the four once shrunk, because the crew competes with the bomb. |

### `precicon` — GBU-57 Bunker Buster

Only three survived. The category is small and half of it is video or unreadable at size.

| id | Source | Licence | Attribution needed | Why kept |
|---|---|---|---|---|
| **mopb** ★ | [USAF MOP test release crop](https://commons.wikimedia.org/wiki/File:USAF_MOP_test_release_crop.jpg) | Public domain (DoD) | none | Clean profile of the weapon in flight against pale sky. Best *shape* reading — you can see it is long and thin, which is the whole point of a penetrator. Source is only 250×250, so it is soft at 64×48; that softness is visible in the installed tile and is the one thing a bigger source would fix. |
| mopc | [DTRA personnel prepare to lift the MOP](https://commons.wikimedia.org/wiki/File:DTRA_personnel_prepare_to_lift_the_MOP_in_preparation_for_a_test.jpg) `crop` | Public domain (DoD / DTRA) | none | The rust-brown penetrator body on a flatbed under a crane. Warm subject against a cool background, huge in frame — the sharpest of the three and the alternate if the installed one reads too soft. |
| mopf | [Off loading of MOP cropped](https://commons.wikimedia.org/wiki/File:Off_loading_of_MOP_cropped.jpg) | Public domain (no author recorded) | none | Crane offload against mountains. Subject smaller than mopc; kept for the three-option floor. Commons records no author — fine for PD, but there is no attribution string if you wanted one. |

### `paranukeicon` — B61-12 ×3 (America only, since 2026-09-09)

| id | Source | Licence | Attribution needed | Why kept |
|---|---|---|---|---|
| **b61c** ★ | [F-35 B61-12 trial (cropped)](https://commons.wikimedia.org/wiki/File:F-35_B61-12_trial_(cropped).jpg) | Public domain (Los Alamos National Laboratory) | none | Full B61-12 in profile, white body with red bands, on flat blue. The clearest single object in the whole batch: one subject, hard edges, nothing behind it. Verified American, and now that the file serves only the three B61 rungs that is exactly what it should be. |
| b61a | [B61 nuclear bomb](https://commons.wikimedia.org/wiki/File:B61_nuclear_bomb.jpg) `crop` | **CC0** | none | Three-quarter view of the casing with its blue band. Reads as metal ordnance but has no silhouette — mid-grey on mid-grey once shrunk. Candidate if you want the three yields told apart. |
| b61d | [B61 silver bullet fusion bomb](https://commons.wikimedia.org/wiki/File:B61_silver_bullet_fusion_bomb.jpg) `crop` | Public domain (Greg Goebel) | none | Bombs on a display rack. Honest option, but the dark bomb against a warm wall reads as furniture at size. |
| b61e | [NAM - B61 Nuclear Bomb](https://commons.wikimedia.org/wiki/File:NAM_-_B61_Nuclear_Bomb.jpg) `crop` | **CC BY-SA 2.0** | "Marshall Astor, CC BY-SA 2.0" | Museum piece stood vertically. A vertical framing is a genuinely different idea; the background clutters it and it carries share-alike. Weakest keep in the batch. |

### `v2bdgricon` — RS-28 Sarmat MIRV, and `ruiskandericon` — Iskander-M

Since 2026-09-09 these are two files. `v2bdgricon` is the Sarmat's alone; `ruiskandericon` is new.

| id | Source | Licence | Attribution needed | Why kept |
|---|---|---|---|---|
| **v2a** ★ `v2bdgricon` | [Sarmat launch still](https://commons.wikimedia.org/wiki/File:Sarmat-launch-still.jpg) `crop` | **CC BY 4.0** | "Ministry of Defence of the Russian Federation, CC BY 4.0" | Silo launch: orange fire filling the lower half, missile body above it. Loudest image in the batch and unambiguous at any size — and it is the Sarmat's own launch, so it is now on the right power. |
| **v2d** ★ `ruiskandericon` | [CombatLaunching2018-14](https://commons.wikimedia.org/wiki/File:CombatLaunching2018-14.jpg) `crop` | **CC BY 4.0** | "Алексей Иванов / Минобороны России, CC BY 4.0" | Erected Iskander against sky — the best pure dark-on-light shape of the group. In the installed tile it reads as machinery more than as a missile; `v2b` is the alternate if that bothers you. |
| v2b | [CombatLaunching2018-16](https://commons.wikimedia.org/wiki/File:CombatLaunching2018-16.jpg) `crop` | **CC BY 4.0** | "Алексей Иванов / Минобороны России, CC BY 4.0" | Missile nose with crew working at its base. Reads as a missile *being prepared*, which suits a called-in reserve strike. Busier than v2d. |
| v2e | [9P78-1 TEL Iskander-M](https://commons.wikimedia.org/wiki/File:9P78-1_TEL_Iskander-M.JPG) | **CC BY-SA 4.0** | "Boevaya mashina, CC BY-SA 4.0" | The launcher vehicle itself, travelling. Distinct idea, but at 64×48 it reads as "a green truck", and it carries share-alike. |

### `atomicon` — Tsar Bomba (50 Mt), and `stratnukeicon` — Strategic Strike (6 Mt)

Since 2026-09-09 these are two files, both drawn from this group.

| id | Source | Licence | Attribution needed | Why kept |
|---|---|---|---|---|
| **tsard** ★ `atomicon` | [Castle Bravo Blast](https://commons.wikimedia.org/wiki/File:Castle_Bravo_Blast.jpg) | Public domain (US DOE) | none | Wide low fireball on the horizon. Historically the right yield class. Your pick, and it stands — but it is the dimmest of the detonations at size and the low wide shape gives up the mushroom silhouette; see judgement call 3. |
| **tsarf** ★ `stratnukeicon` | [Castle Romeo](https://commons.wikimedia.org/wiki/File:Castle_Romeo.jpg) | Public domain (US DOE) | none | The canonical red-orange fireball and stem, and an 11 Mt shot — the right class for a 6 Mt strike. Enormous tonal range, unmistakable shape, a real megaton detonation rather than a render. |
| tsare | [Ivy Mike mushroom cloud](https://commons.wikimedia.org/wiki/File:Ivy_Mike_-_mushroom_cloud.jpg) | Public domain (US DOE) | none | The purple-white column. Reads as a *cloud* rather than a fire — the one colour nothing else in the event tier has, which makes it the best swap if the Tsar Bomba needs to stand out more. |
| tsara | [Tsar Bomba Revised](https://commons.wikimedia.org/wiki/File:Tsar_Bomba_Revised.jpg) `crop` | **CC BY-SA 3.0** | "User:Croquant, modified by User:Hex, CC BY-SA 3.0" | The actual RDS-220 casing in the Sarov museum — the only candidate that literally *is* the Tsar Bomba. Device-not-detonation is a real alternative idea. Share-alike. |
| tsarb | [Tsar Bomba.JPG](https://commons.wikimedia.org/wiki/File:Tsar_Bomba.JPG) | **CC BY-SA 3.0** | **cannot be written** — Commons records no author | Same casing with a man in frame for scale. BY-SA requires attribution and there is nobody to attribute, so it cannot ship. Shown so the scale idea is on record; take it from tsara if you want it. |

## Rejected, and why

| Source | Reason |
|---|---|
| [Trident breaking surface, HMS Vanguard](https://commons.wikimedia.org/wiki/File:A_Trident_Missile_Breaks_the_Surface_After_Being_Fired_from_HMS_Vanguard_MOD_45151581.jpg) | **OGL v1.0** (UK MOD). A free licence, but neither PD nor CC, so outside the policy. Genuinely the best-composed Trident image found — say the word if OGL is acceptable and it goes straight in. |
| [Tsar Bomba fireball 1961](https://commons.wikimedia.org/wiki/File:Tsar_Bomba_fireball_1961.jpg) | Commons tags it PD, but its credit line reads "Cover Images / … / **Associated Press**" and points at `newsroom.ap.org`. Agency wire photo — rejected on the policy's own terms regardless of the tag. |
| [Trident II missile cropped](https://commons.wikimedia.org/wiki/File:Trident_II_missile_cropped.jpg) | Tagged PD, but sourced "High Res image from **Lockheed Martin**" with author unknown. A contractor is not the federal government, so PD-USGov does not obviously apply. No benefit of the doubt. |
| `B-83 nuclear weapon.jpg`, `B83 nuclear weapon (bw).jpg` | PD tag, but provenance is a scan out of Chuck Hansen's *Swords of Armageddon*. Dropped on legibility anyway — a cluttered parts layout — so the licence question never had to be settled. |
| `Deleted GBU-57 MOP photo (1).jpg` / `(2).jpg` | The filenames record a takedown somewhere upstream. Not worth inheriting. |
| `Ivan bomb.png` | Turned out to be a **range map**, not the device. CC BY-SA 3.0 with no author recorded, either. |
| `Кинжал 001.png` | Russian MoD strike footage as a tall multi-frame strip. CC BY 4.0 but Commons records **no author**, so the required attribution cannot be written — the same bar `tsarb` failed. |
| Kinzhal, Iskander-K and Caspian Kalibr launch **videos** (`.webm` / `.ogv`) | Three of the best-composed launches found are video, not stills. No frame was extracted: it would need a decoder this toolchain does not have, and the licence would need re-checking per frame. Named here because they are the obvious next place to look if a tile needs replacing. |
| `MOP in the B-2 bomb bay`, `B-52 releases the MOP`, `CombatLaunching2018-26`, `B61 inert training version`, `USAF MOP (tight crop)` | Licence was clean on all five (PD or CC-BY). Dropped purely on the 64×48 test: the bomb-bay close-up is grey mush, the release shots put the weapon three pixels wide in empty sky, the snowfield shot has no subject at all, and the tight crop is a 219×70 source that cannot survive a 4:3 fill. |

## Three things worth knowing before anyone touches this again

**A NEW CAMEO NAME IS NOT NECESSARILY FREE, and the sprite check will not tell you.** The Iskander
power's art was first installed as `iskandericon.shp` — which already existed as the **Iskander TEL
vehicle's** unit cameo, reached through `sequences.yaml`'s `iskander` image and its `icon:` member.
Installing over it silently repointed a unit's sidebar art, and `--check-missing-sprites` passed
cleanly both before and after, because the file existed and decoded either way. It was caught only
by an unexpected ` M ` instead of `??` in `git status`. The power's file is `ruiskandericon.shp`;
the vehicle's is untouched. **Check `git status` on `bits/misc/icons/` after every `--install` and
account for every modified file.**


**Every cameo in the support-power bin is untexted.** All thirteen art files the fifteen powers
draw are now photographs with no lettering baked into them, and every caption is drawn at runtime
from `CameoCaption`. Per `tools/cameo/README.md` §"Rollout order is decided by one fact", that is
the precondition for turning `CaptionBackgroundColor` transparent on the `SupportPowers` widget —
which was **not** done here, deliberately: `CaptionBackgroundColor` is a widget-wide field, the
band is what currently hides the fact that 7px FreeSansBold has no fully-opaque pixel, and the
production palette still has ~87 lettered art files anyway. It is available, it is a separate
decision, and it should be looked at after this art has been seen in game.

**The beacon posters were not touched and could not have been.** `BeaconPoster: atomicon`
resolves through the `beacon` collection's `atomicon: lores|atomicon`, and `lores` is the base
Red Alert `lores.mix` mounted in `mod.yaml:23` — not `mods/ww3mod/bits/misc/icons/atomicon.shp`.
Replacing the cameo file leaves the incoming-strike beacon exactly as it was. The other beacon
posters (`precbcon`, `cmissbcon`, `paranukebcon`, `v2bdgrbcon`) are separate art files in the mod
and were likewise untouched. Worth knowing before anyone "fixes" a beacon by editing a cameo.

## Reproducing

Sources are not committed — they are re-downloadable from the URLs above. The scratch tree that
produced these sheets sits at `work/` in the `wt/cameo-sourcing` worktree and is deliberately left
**untracked** (it is not covered by `.gitignore`, so do not `git add -A` in this branch):
`work/src/` and `work/src4/` raw downloads, `work/src3/`, `work/stage5in/` and `work/kal_in/`
post-crop inputs to `convert.py`, `work/final_in/` the exact twelve inputs that were installed,
`work/final_out/` the twelve 64×48 renders, `work/shipped/` the six previous cameos decoded out of
their SHPs, `work/manifest.json` and `work/_new_meta.json` the machine-readable licence records.

Rebuild the installed set from `work/final_in/`:

```bash
python3 tools/cameo/convert.py work/final_in --out work/final_out --no-baked-captions --install
python3 tools/cameo/binmock.py          # -> tools/cameo/work/bin-1x.png, bin-3x.png
.\utility.cmd --check-missing-sprites   # 14-line pre-existing baseline = pass
```
