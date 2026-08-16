# Content completeness matrix — per-actor inventory of what is missing

**Ref:** `main @ 55459146`, working tree clean, in sync with `origin/main`.
**Date:** 2026-08-16
**Status:** **PARTIAL — see [Coverage](#coverage-what-i-finished-and-what-i-did-not) before using any number here.**

**Purpose (changed mid-audit):** the user has deferred all art/sound work out of the v1
implementation push — *"The art/sound can be made last, it is not a blocker just a pre-release
polish that will need a lot of my attention."* This document is therefore **the standing
pre-release polish TODO**, not a work order for now. Its value is that the numbers are written
down and re-derivable.

**Method:** static analysis only. **No game launch, no autotests, no builds, no edits.** Every
count comes from cross-referencing rules YAML → weapons YAML → sequences YAML → the actual
asset packages.

---

## Ground truth used (so this is reproducible)

Two indexes were built and both were validated before any count was taken.

**1. Asset index — 3,078 filenames the mod can actually load.** Union of:
- every file inside every mounted Red Alert `.mix`, extracted with the engine's own
  `OpenRA.Utility --list-mix` against `%APPDATA%/OpenRA/Content/ra/v2/*.mix` (+ `expand/`,
  `cnc/`). This matters: `hires.mix`, `lores.mix`, `local.mix`, `speech.mix`, `expand2.mix`,
  `hires1.mix`, `lores1.mix` have **encrypted headers**, so a naive MIX reader silently sees
  zero files in them and reports huge false "missing art" counts. The utility decrypts them.
- every loose file under `mods/ww3mod/bits/`, `engine/mods/ra/bits/`, `engine/mods/common/`,
  and both `uibits/` dirs.

Each name is tagged with provenance: **879 ww3mod-authored**, 1,751 RA-mix, 379 ra-mod,
69 common. The provenance tag is what distinguishes "has an icon" from "has a *Red Alert* icon".

*Validation:* the index independently reproduces the engine's own `--check-missing-sprites`
verdict (it agrees that `b2bomb.shp` / `pip-cloak.shp` are absent and that `e1.shp` /
`abrams-firing-1.wav` are present).

**2. Inheritance-resolved actor/weapon/sequence table.** A MiniYaml parser + `Inherits:`
resolver over the exact 35 rules files, 7 weapons files and 10 sequence files listed in
`mods/ww3mod/mod.yaml` (files on disk but *not* listed there were excluded).

*Validation:* diffed **trait-by-trait and field-by-field** against the engine's own
`OpenRA.Utility --resolved-rules` for 9 actors spanning every category — `E1`, `E6.russia`,
`abrams`, `tunguska`, `HIND`, `HELI`, `PBOX`, `MSLO`, `SUPPLYROUTE`. **Zero differences.**
(An earlier version of the resolver failed to merge a trait key inherited from *two* parents;
that bug was found by this diff and fixed. Without the diff, every count below would have had
a quiet false-positive tail.)

This is why the "missing" claims below are safe: a gap filled by a parent template is resolved
away before counting.

**Totals in scope:** 424 actor definitions · 156 actor templates · 131 concrete weapons ·
33 weapon templates · 493 sequence images.

---

## Headline counts

| Dimension | Result | Confidence |
|---|---|---|
| Buildable actors with a **working cameo** | **95 / 95** — zero missing | High |
| Cameos that are **WW3MOD-authored** art | 94 / 95 (only `SAM` uses RA's `samicon`) | High |
| Cameos at the **wrong pixel size** (60×48 vs widget's 64×48) | **36 / 78** loose cameo SHPs | High |
| Live buildables missing a **name** | **0** | High |
| Live buildables missing a **description** | **0** (the 4 without one are all `~disabled`) | High |
| Live buildables with **RA-era flavour names** | **5** (`E2.*` "Grenadier", `E4.*` "Flamethrower", `MNLY`) | High |
| Non-buildable **RA leftovers with RA names still visible on-map** | **23** | High |
| Descriptions that **overflow** the tooltip box | **0** — the real defect is different (see F5) | High |
| Guns (weapons on an Armament) with a **working firing sound** | **49 / 57** | High |
| Guns whose `Report` names a **file that does not exist** | **1** (`30mm.A10`) | High |
| Guns with **no `Report` at all** | **7** (only 1 is a real gun) | High |
| Guns whose impacts make **no sound at all** | **15** | High |
| Sequences referencing **art that does not exist** | **5** distinct (+1 the engine cannot see) | High |
| Rules files on disk that **never load** | **8** | High |
| **Orphan actor definitions** (functional, non-husk) | **16** | High |
| Orphan **husk** defs + editor-only props | 40 + 42 | High |
| Shipped **assets referenced by nothing** | **≥188 files / 3.89 MB** of 24.6 MB (16%) | High (floor) |
| Weapons reachable by **nothing** | ~59 (upper bound) | **Low — not finalised** |
| Playable mobile actors with **no voice** | **1** (`quadcopterdrone`, not directly ordered) | High |
| **Dead** VoiceSet references (the `arabs/` shape) | **0** | High |
| Voice clips naming a **missing file**, on a live set | **4** (all on `SealVoice` / `SF`) | High |
| Buildable units **sharing just 2 voice sets** | **62 / 75 (83%)** | High |
| Voice sets with **zero users** / orphaned sound dirs | **14 sets**, 7 of 10 dirs (124 files) | High |
| Death sounds / death sprites / husk sharing | **NOT REACHED** | — |

---

## Coverage: what I finished and what I did not

Read this before quoting any number.

### Finished, validated, safe to act on
- **Cameo / icon coverage** — all 95 buildable actors, resolved, with provenance and sprite
  dimensions read from SHP headers.
- **Names and descriptions** — all 411 actors carrying a Tooltip and/or Buildable, resolved,
  including the live-vs-`~disabled` split and real text measurement against the actual font.
- **Firing sounds** — all 131 concrete weapons classified; the 57 carried by an actor's
  `Armament` fully checked against the asset index.
- **Warhead impact sounds** — for those same 57 carried weapons.
- **Missing sprite art** — via the engine's own checker, so authoritative.

- **Voice responses** — all 424 actors resolved for `Voiced`, all 24 voice sets resolved
  through `DefaultVariant`/`Variants` the way `Sound.PlayPredefined` builds filenames
  (`engine/OpenRA.Game/Sound/Sound.cs:396-416`), every resulting clip checked against the
  asset index.
- **Orphans and dead weight** — all 424 actor defs cross-referenced against 10 maps,
  175 autotest/demo scenarios, `rules/ai/*.yaml`, all Lua, and `Buildable:`+`Prerequisites`.

### NOT reached — genuinely open, no numbers exist yet
- **Death sounds, death explosions, husk/corpse sharing (items 5 and 6 of the brief).** The
  script covering `DeathSounds`, `Explodes`, `SpawnActorOnDeath` and infantry
  `WithDeathAnimation@ROT` sharing was written and launched but had not returned when the
  session was cut short. **The tracker's "per-unit rot/bleedout uses a generic `e1`" claim is
  therefore neither confirmed nor quantified here.** This is now the single remaining hole.
  The script is at `%LOCALAPPDATA%/Temp/wwaudit/full.py` and needs only to be re-run.
- **Structures, defenses, naval, civilian and neutral actors** were included in the census and
  in the cameo/name/voice/orphan sweeps, but were **not** separately examined for
  category-specific gaps.

### Known false-positive risks in what IS reported
- The **~59 unreferenced weapons** figure is the one number here I would not quote. It was
  computed before I added death-explosion weapons (`Explodes:` etc.) to the reachability seed,
  and the list contains entries like `UnitExplodeHeli` / `UnitExplodePlane` that are almost
  certainly reached that way. Treat 59 as a loose **upper bound**; the true count of dead
  weapons is lower and unknown.
- Cameo, name, description and firing-sound counts are **not** subject to this risk — they
  were taken from the validated resolver.
- The **RA-flavour name count (5)** is a keyword scan, so it is a **floor, not a ceiling**.
  Style inconsistencies that were deliberately not counted: bare nicknames (`Halo`, `Apache`,
  `Chinook`) sitting next to fully-specified names (`Mi-24 Hind`, `Abrams M1A2`), and lowercase
  descriptive names (`DR` = "Drone operator", `MSAR` = "Ranging system").

---

## The census, and why "95 buildable" overstates the player-facing surface

| Set | Count |
|---|---|
| Actor definitions in the `Rules:` block | 424 |
| …carrying a `Buildable` trait | 95 |
| …of those, gated `Prerequisites: ~disabled` | **41** |
| **Live, player-callable buildables** | **54** |

The 41 disabled entries are mostly RA holdovers still carrying RA names — `E1` "Conscript",
`HBOX` "Camo Pillbox", `SBAG` "Sandbag Wall", `FTUR` "Flame Turret", `MSLO` "Nuclear Missile
Silo", `AFLD` "Airfield", `HPAD` "Helipad". They are invisible in the Supply Route palette but
**visible in the map editor's actor list**. All 16 defenses and 4 buildings in the ruleset are
`~disabled`.

**Consequence for planning: the art/audio surface that actually needs polish is 54 units, not
95.** That roughly halves the pre-release polish estimate.

---

## Findings

### F1 — [SHOULD-FIX] The A-10's GAU-8 is silent: 1 gun points at a sound file that was never shipped
**Perceived:** the loudest gun in the game fires in total silence. The A-10 gun run has a
visual but no audio.

`30mm.A10` declares `Report: A10.wav` (`mods/ww3mod/rules/weapons/weapons-ballistics.yaml:650`),
and `30mm.TimerWolf` (`:686`) and `30mm.Fighter` (`:701`, `Report: A10.aud`) point at the same
name. **Neither `a10.wav` nor `a10.aud` exists anywhere in the asset index.** What ships is
`mods/ww3mod/bits/sounds/a10gun.wav`.

The mechanism is worth recording because it will bite again: this engine resolves a sound name
as a **literal filename, extension included** — `Sound.LoadSound` does `fileSystem.Exists(filename)`
verbatim and appends nothing (`engine/OpenRA.Game/Sound/Sound.cs:59-65`, played via `sounds[name]`
at `:121`). A wrong name fails **silently** — it writes one line to the `sound` log channel and
returns null. There is no lint for it. That is why this survived: nothing anywhere reports it.

**Fix: wire an existing asset** — one-line rename to `a10gun.wav`. Confidence: high.

### F2 — [SHOULD-FIX] 15 guns land their hits in silence — every small arm in the game
**Perceived:** infantry firefights are near-silent on the receiving end. You hear the shot but
never the impact, so sustained small-arms fire reads as ineffective.

15 of the 57 carried weapons declare a `CreateEffect` warhead but leave `ImpactSounds` empty:
`5.56mm.AR`, `5.56mm.DMR`, `5.56mm.DMR.silencer`, `5.56mm.E3`, `7.62mm.MG`, `7.62mm.DMR`,
`7.62mm.Sniper`, `7.62mm.Minigun`, `7.62mm.Minigun.AA`, `12.7mm.MG`, `12.7mm.Hind`,
`12.7mm.Hind.AA`, `AACannon`, `MP5`, `Pistol`.

This is the audio half of a defect whose **visual** half is already documented in
`WORKSPACE/fx-audit.md` §1.1 — the same warheads render no impact sprite on helicopters
because `Warhead@AirEffect` is a `CreateEffect` with an empty `Explosions:` list. Same warheads,
same root cause, both halves open. **These two should be fixed in one pass**, and `fx-audit.md`
should be read alongside this document.

**Fix: needs a decision, then wire existing assets** — RA ships generic impact sounds that
could be wired immediately; whether they suit modern small arms is the user's call.
Confidence: high on the count, medium on the remedy.

### F3 — [POLISH] 1 real gun and 6 utility weapons have no firing sound declared at all
**Perceived:** the CRAM / F-16 / MiG 20mm autocannon is silent.

`20mm_CRAM` (carried by `CRAM`, `F16`, `MIG`) has no `Report`. The other six are non-gun
utility weapons where silence is defensible: `ClearMines` and `Repair` (`E6`), `DroneJammer`
and `DroneTargeter` (`DR`), `Heal` (`MEDI`), and `Dummy` (carried by all 15 vehicle husks).

**Fix: wire an existing asset** for `20mm_CRAM` (`aacanon3.aud` is already used by comparable
guns). The other six are arguably correct as-is. Confidence: high.

### F4 — [COSMETIC] 36 of 78 cameos are 60×48 in a 64×48 slot
**Perceived:** roughly half the unit icons sit 4px narrow in the build palette — a subtle
ragged edge down the column rather than a clean grid.

`ProductionPaletteWidget.IconSize` is `64,48`
(`engine/OpenRA.Mods.Common/Widgets/ProductionPaletteWidget.cs:46`). Reading SHP headers
directly: 32 cameos are 64×48, **36 are 60×48**, and 10 are RA-era 64×48. Affected include
`abramsicon`, `bradleyicon`, `bmp2icon`, `apacheicon`, `tunguskaicon`, `iskandericon`,
`littlebirdicon`, `gradicon`, `m109icon`, `m113icon`, `m270icon`, `tosicon`,
`strykershoradicon`, `chinookicon` and every `e1/e2/e3`-`america`/`russia` infantry icon.

Clearly two authoring batches with different canvas sizes.

**Fix: needs art touched** — mechanical (re-canvas to 64×48), but it is 36 files.
Confidence: high.

### F5 — [POLISH] Every tooltip is a fixed 350px slab; 0 descriptions actually overflow
**Perceived:** a 65-character one-liner gets exactly the same wide box as a 196-character
entry, so short descriptions look padded and empty.

This is almost certainly what "unit description box sizing" in `WORKSPACE/RELEASE_V1.md:128`
refers to — and it is **not** an overflow bug. Measuring every live description with the real
`FreeSansBold.ttf` at 10px, reproducing `WidgetUtils.WrapText`: 4–7 wrapped lines of static
text, 4–12 including the auto-generated `AmmoPool` block, max tooltip height ≈171px.
**Overflow count: 0.** No unbreakable token exceeds the wrap width.

The defect is `ProductionTooltipLogic.cs:147-149`:
`Math.Clamp(max(name, requires, desc), MaxTooltipWidth, MaxTooltipWidth)` — min and max are
both 350, so `leftWidth` is **always** exactly 350.

Longest descriptions (chars): `tos` 196 · `TL.russia`/`TL.america` 171 · `E6.*` 163 ·
`SN.*` 162 · `AT.*` 161 · `m270` 160 · `grad` 160. Min 65, median 122, mean 125.

**Fix: one-line C# change** (clamp against measured width instead of the constant).
Confidence: high.

### F6 — [POLISH] 23 Red Alert leftovers still carry Red Alert names where players can see them
**Perceived:** a modern-warfare battlefield with "Tesla Coil (Destroyed)", "Prof. Einstein"
and "Husk (Ore Truck)" on it.

Reachable via husks and neutral spawns: `TSLA.Husk` "Tesla Coil (Destroyed)"
(`rules/husks/husks-defenses.yaml:37`), `TTNK.husk` "Tesla Tank"
(`rules/husks/husks-vehicles.yaml:437`), `4TNK.Husk` "Husk (Mammoth Tank)" (`:92`),
`PRSM.husk` "Prism Tank" (`:269`), `HARV.*Husk` "Husk (Ore Truck)" (`:36,43`), `PROC.Husk`
"Ore Refinery", `FACT.Husk` "Construction Yard", `GAP.Husk` "Gap Generator", `PYLE.Husk`
"Arabian Barracks", `U2.Husk` "Husk (Spy Plane)". Plus two non-husks: **`EINSTEIN` =
"Prof. Einstein"** (`rules/ingame/infantry-neutral.yaml:63`) and **`DRILLMINE` = "Ore Drill"**
(`rules/misc.yaml:220`).

These are genuine RA leftovers, not WW3MOD units wearing RA text — **deleting them is as valid
a fix as renaming them**, and is probably cheaper.

**Fix: no assets needed** — delete or rename. Confidence: high.

### F7 — [POLISH] 5 live units wear Red Alert-era labels
`E2.america` / `E2.russia` = "Grenadier" (`rules/ingame/infantry.yaml:1383`),
`E4.america` / `E4.russia` = "Flamethrower" (`:1988`), `MNLY` = "Minelayer".
Zero *descriptions* contain RA vocabulary. **Fix: text only.** Confidence: high, but this is a
floor not a ceiling (see coverage caveats).

### F8 — [COSMETIC] 5 sequences point at art that does not exist
From the engine's own `--check-missing-sprites`, so authoritative: `b2bomb.shp`,
`pip-cloak.shp`, `pip-cover.shp` (missing in all four tilesets) and `mslo.int`, `bib3.int`
(INTERIOR only). `pip-cloak` / `pip-cover` are selection-pip art; `b2bomb` is bomb ordnance.

**Fix: needs new art created, or delete the sequences** if the features are gone.
Confidence: high (engine-reported).

### F9 — [COSMETIC] Only 1 cameo is still Red Alert original art
`SAM` renders RA's `samicon.shp`. All 94 other buildables use WW3MOD-authored icons.

Also worth recording as a **non-finding**: `RELEASE_V1.md` lists "Unit icons" as an open gap.
It is ~99% done. 79 distinct cameo sprites cover 95 actors; the 15 shared ones are all
base/faction-variant pairs (`E1`+`E1.america` etc.) plus `DR`/`DR.america`/`DR.russia`, which
is correct behaviour, not a gap.

### F10 — [SHOULD-FIX] 62 of 75 buildable units (83%) share just two voice sets
**Perceived:** a MiG pilot answers an order with the same infantry "yes sir" as a rifleman.
This is the single biggest immersion gap in the audio, and it is a *realism* problem, which
the project treats as a primary goal.

`GenericVoice` covers 11 infantry types × 3 faction variants **plus all 10 aircraft** (`A10`,
`F16`, `MIG`, `HIND`, `MI28`, `HELI`, `littlebird`, `HALO`, `TRAN`, `FROG`). `VehicleVoice`
covers all 19 ground vehicles with 4 select and 2 action lines. 43 units share one single set.

Compounding it: every ordering trait defaults to `Voice = "Action"` (`Mobile.cs:76`,
`AttackBase.cs:43`), so move / attack / deploy / capture **all draw the same 7-clip pool**. The
`Move`, `Attack`, `Kill` and `Build` pools are never requested at all — except on `iskander`,
which overrides them (`rules/ingame/vehicles-russia.yaml:943,954,982`) and is therefore the
worked example of how to fix the rest.

Faction differentiation *does* work (`.v01/.v03` vs `.r01/.r03`), so the plumbing is sound —
only the content is thin.

**Fix: needs new audio created.** This is the big-ticket item and the one that will "need a lot
of attention". Confidence: high.

### F11 — [SHOULD-FIX] 4 missing voice clips on the one elite unit — 3 are one-line fixes
**Perceived:** roughly 1 in 3 Special Forces C4 plants is silent, and ~50% of SF fire/electric
deaths are silent.

- `rules/sound/voices.yaml:228` declares `Demolish: iseaexa, iseaexb, iseaexd`. **`iseaexd.aud`
  does not exist; `iseaexc.aud` does and is referenced nowhere** — a straight `c`→`d` typo.
- `:218 Burned: dedman10, yell1` and `:219 Zapped: dedman6, nuyell3` — `yell1` and `nuyell3`
  exist in no format.
- `:224 Action: iseafea` — no `iseafe*` file exists anywhere, so `SF` has no working
  Action-fallback of its own.

**Fix: `iseaexd`→`iseaexc` is wire-an-existing-asset (one character). The other three need new
audio, or the entries dropped.** Confidence: high.

### F12 — [POLISH] 7 of 10 mounted sound dirs are unreachable (124 files); 14 voice sets have zero users
**Perceived:** nothing — this is shipped audio nobody hears.

Orphaned dirs: `robot` (11 files), `volkov` (11), `chem` (21), `terroist` (30), `glabike` (27),
`informan` (12), `commando` (12). Their sets — `RoboticVoice`, `VolkovVoice`, `ChemVoice`,
`FanaticVoice`, `CycleVoice`, `InfoVoice`, `CommandoVoice` — plus `MechanicVoice`,
`ThiefVoice`, `TanyaVoice`, `DogVoice`, `SpyVoice`, `AntVoice`, `StavrosVoice` are named by no
actor. The live `seal` and `v3` dirs also carry unreferenced clips (`iseaatta`, `iseaattb`,
`iseaexc`, `vv3latta`, `vv3lattb`).

**These are wire-an-existing-asset opportunities, not gaps** — worth mining before commissioning
new audio for F10. Confidence: high.

Also, importantly: **0 dead VoiceSet references.** All 9 sets actually named by actors exist.
The `arabs/` shape that `2f31404e` removed has no survivors.

### F13 — [SHOULD-FIX] 8 rules files on disk never load — 9,958 bytes that look live but are not
**Perceived:** nothing directly. The risk is a future editor "fixing" a balance number in a
file the game never reads.

Not listed in the `Rules:` block of `mods/ww3mod/mod.yaml:100-135`:
`rules/ingame/old.yaml`, **`rules/ingame/vehicles-ukraine.yaml`** (contains `t72`, a
fully-specced MBT with cost/armor/ammo), `rules/campaign/{campaign-rules,campaign-tooltips,
campaign-palettes,coop-missions-rules}.yaml`, `rules/campaign-palettes.yaml`,
`rules/disable-player-experience.yaml`.

Separately, 3 files that *are* listed contribute zero actors: `rules/ingame/naval.yaml` is
999 lines of entirely commented-out content, and `naval-america.yaml` / `naval-russia.yaml` are
**0 bytes**. The `Ship` queue has no items.

**Fix: delete or wire up. No assets needed.** Confidence: high.

### F14 — [POLISH] 16 functional orphan actors — and the A10/FROG claim is confirmed, but is bigger than the tracker says
**Perceived:** the tracker's suspicion is correct, and understated.

`A10.Airstrike` (`rules/ingame/aircraft-america.yaml:675`) and `FROG.Airstrike`
(`aircraft-russia.yaml:696`) are orphaned exactly as `RELEASE_V1.md:168` says, with the cause
documented in place — the whole `AirstrikePower@America/@Russia` block is commented out at
`rules/player.yaml:106-155`.

**Not in the tracker: all four fixed-wing aircraft are themselves unreachable.** `A10`, `F16`,
`FROG` and `MIG` all carry `Buildable: Prerequisites: ~disabled`
(`aircraft-america.yaml:453,573`; `aircraft-russia.yaml:467,585`) and appear on no map.

This makes **F1 and F3 lower priority than they first look** — the A-10's silent GAU-8 and the
`20mm_CRAM` with no `Report` are all on aircraft the player currently cannot field. They matter
when fixed-wing is re-enabled, not before.

Others: `E1R1.*`, `E2R1.*`, `E3R1.*` (a fully detached inheritance chain — the `.america`/
`.russia` variants inherit `^E1`/`^E2`/`^E3`, not these), `camera.paradrop.detector`,
`camera.spyplane`, `CTFLAG`, `DRILLMINE`, `MONEYCRATE`, `HEALCRATE`, `HEALUPCRATE`,
`unit.summoner`. Plus 40 orphan husk defs and 42 editor-only props (the props are arguably
intentional — placeable in the editor).

**Fix: delete. No assets needed.** Confidence: high.

### F15 — [SHOULD-FIX] The AI's entire air-squad list points at unbuildable actors
**Perceived:** the bot never does air rushes.

`rules/ai/ai.yaml:1678` `AirUnitsTypes: mig, frog`; `:1769` `a10, f16`; also `:2264`, `:2277`,
`UnitsToBuild a10: 40` (`:1754`), `frog: 20` (`:1659`), and `AirStrikeUnits: heli, a10`
(`:1330`) / `mi28, frog` (`:1373`). All four named aircraft are `~disabled` (see F14).

Confidence: high that the entries are dead; **medium** on the gameplay consequence, since
rotary units are handled by a separate role path that was not fully traced.

### F16 — [COSMETIC] ≥188 shipped asset files (3.89 MB, 16%) are referenced by nothing
Of 1,233 files under `mods/ww3mod/bits/**`: 101 `.shp` (2.48 MB), 73 `.aud` (0.99 MB),
9 `.des`, 5 `.wav`. Biggest: `bits/weapons/explosions/nuke_small.shp` (1.28 MB),
**`bits/sounds/a10gun.wav` (262 KB — this is F1's orphan, corroborating it independently)**,
`bits/units/vehicle/tnkk.shp` (206 KB), `bits/weapons/explosions/fuelflame2.shp` (142 KB),
`bits/units/vehicle/harvgarr.shp` (90 KB), `bits/units/infantry/e3_old.shp` (69 KB),
`bits/units/aircraft/raptor.shp` + `king_raptor.shp` (73 KB).

This is a **lower bound** — a file counted as "referenced" if its bare stem appears as a token
in any loaded yaml/lua/cs, which over-credits. Confidence: high on the floor, medium on the
exact number.

### F17 — [COSMETIC] A sixth missing sprite the engine's own check cannot see — and it may silence 123 sequence lines
`sequences/sequences.yaml:2` references **`emp_fx01`**; the asset index has **`empfx01.shp`**
(no underscore). `--check-missing-sprites` does not report it because the owning image
`^VehicleOverlays` is abstract and never reserved.

If that read is right, the **123 `Inherits: emp-overlay` lines across 7 sequence files** and the
`WithIdleOverlay` in `rules/defaults.yaml` are drawing nothing — i.e. the EMP hit effect is
invisible mod-wide.

**Fix: wire an existing asset** (one-character filename correction). Confidence: high on the
filename mismatch, **medium on the consequence** — OpenRA's sequence-inheritance scope for a
child of a `^`-prefixed parent was not statically resolvable, and the game was not launched to
confirm. **Verify this one before acting.**

### F18 — [COSMETIC] `missions.yaml` lists 49 Red Alert campaign missions; none of them ship
`mods/ww3mod/missions.yaml` names 49 RA missions. `mods/ww3mod/maps/` ships 10 WW3 maps and
none of the 49. If the Missions browser is reachable, it is entirely empty. Confidence: high on
the mismatch, low on player visibility (whether the Missions tab is exposed was not verified).

---

## Standing pre-release TODO, ordered

**Needs new assets created — this is the "a lot of my attention" bucket:**

1. **F10 — voice variety: 62 of 75 units share 2 voice sets, and all orders draw one 7-clip
   pool.** By far the largest audio job, and the one that most affects the realism goal. Mine
   F12's 124 orphaned clips first — some of that inventory may cover it without new recording.
   `iskander` (`vehicles-russia.yaml:943,954,982`) is the worked example of per-unit voice
   overrides.
2. **F2 — 15 guns land hits in silence.** Do this in one pass with `fx-audit.md` §1.1, which is
   the *visual* half of the same warheads. Needs a decision on whether RA's generic impact
   sounds suit modern small arms.
3. **F4 — re-canvas 36 cameos to 64×48.** Mechanical but 36 files.
4. **F8 — 5 missing sprites** (`b2bomb`, `pip-cloak`, `pip-cover`, `mslo.int`, `bib3.int`), or
   delete the sequences if the features are gone.

**Wire an existing asset / no assets needed — cheap, do these whenever:**

5. **F13 + F14 — delete dead weight.** 8 never-loading rules files, 16 orphan actors, 40 orphan
   husks. No assets, no risk.
6. **F6 — delete or rename 23 RA leftovers** ("Tesla Coil (Destroyed)", "Prof. Einstein",
   "Husk (Ore Truck)"). Biggest immersion win per unit of effort in the whole document.
7. **F11 — `iseaexd` → `iseaexc`.** One character; fixes SF's silent C4 plants.
8. **F17 — `emp_fx01` → `empfx01`.** One character, but **verify the consequence first** — if
   correct it restores the EMP effect mod-wide.
9. **F5 — one-line tooltip width fix** (`ProductionTooltipLogic.cs:147-149`).
10. **F7 — rename 5 RA-era unit labels.**
11. **F1 + F3 — `A10.wav` → `a10gun.wav`, `20mm_CRAM` → `aacanon3.aud`.** Deliberately low:
    per F14 these are all on fixed-wing aircraft that are currently `~disabled`, so they matter
    only when fixed-wing is re-enabled.

**Still to investigate:**

12. **Death sounds / death effects / husk-and-corpse sharing** — the one unfinished slice.
    Re-run `%LOCALAPPDATA%/Temp/wwaudit/full.py`. Includes the unverified "generic `e1` rot
    sprite" claim.
13. **Finalise the orphan-weapon list** — the ~59 figure is not trustworthy as it stands.

---

## Reproducing this

Scratch scripts live in `%LOCALAPPDATA%/Temp/wwaudit/` (`miniyaml.py` — the validated
resolver, `analyze.py`, `cameo.py`, `sounds2.py`, `full.py` — the unfinished death-audio pass).
They are **scratch, not committed**. The resolver is the reusable part: it is worth keeping,
because it is byte-identical to the engine's own and can answer this class of question without
a build or a game launch.

The two utility invocations that unlock everything else:

```
cd engine
export ENGINE_DIR=".." MOD_SEARCH_PATHS="<repo>/mods,<repo>/engine/mods"
./bin/OpenRA.Utility.exe ww3mod --resolved-rules <ACTOR>
./bin/OpenRA.Utility.exe ww3mod --check-missing-sprites
./bin/OpenRA.Utility.exe ww3mod --list-mix "<mix>" "engine/global mix database.dat"
```
