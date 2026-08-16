# WW3MOD — known issues for the first public release

> Verified against `main` @ `2a9eb77d` (2026-08-17) by reading the tree. I did not launch the
> game and did not run any autotest, so nothing here is a runtime observation; items that need a
> run to characterise are marked **[needs a run]**.
>
> This list is written to be published, lightly edited, alongside the download. For a first
> public release a disclosed limitation costs a line of text; a discovered one costs the player.

---

## Blocking — do not publish a download link until this is fixed

### The Red Alert content installer can never run

**What a stranger sees:** they install WW3MOD, launch it, and are **never asked to install the
Red Alert data files**. The game needs those files for its terrain and base interface art. It
does not prompt, and it does not explain.

**Mechanism, confirmed at HEAD:**

- `mods/ww3mod/mod.yaml:13` declares `FileSystem: DefaultFileSystem`.
- The installer is gated on an interface check in
  `engine/OpenRA.Mods.Common/LoadScreens/BlankLoadScreen.cs:131-132`, which only fires for
  `IFileSystemExternalContent`. Only `ContentInstallerFileSystemLoader` implements it, and
  `grep -rn ContentInstallerFileSystemLoader mods/ engine/mods/` returns **nothing**.
- So the carefully hand-written `ModContent:` block at `mods/ww3mod/mod.yaml:404-412` — install
  prompt text, quick-download, mirror list, per-package `TestFiles` — is unreachable
  configuration. It has never been able to execute.
- The data is genuinely required: `mods/ww3mod/tilesets/temperat.yaml:79` references
  `clear1.tem`, the base ground tile, and `find . -name clear1.tem -not -path ./engine/*`
  returns nothing. That tile lives inside `temperat.mix`, which is not redistributed.
- Every RA mount in `mod.yaml:15,24-40` is prefixed `~` (optional), so the absence fails
  **silently** rather than with an error a player could act on.

**Why this survived until now:** the development machine already has Red Alert content in
`%APPDATA%/OpenRA/Content/ra/v2/`. WW3MOD has only ever been run where the prerequisite it never
installs was already satisfied.

**Note on the packaging fix:** the recent packaging work is real and did clear a separate
blocker — `mod.config:108` now sets `PACKAGING_COPY_ENGINE_FILES="./mods/ra ./mods/modcontent"`,
so the artifact contains the four mods a clean machine needs (`ww3mod`, `ra`, `modcontent`,
`common`). That makes the game **launch**. It does not make it **playable**: the four mods are
code and rules, not the Red Alert data files, which are a separate download the game never
offers.

**[needs a run]** How bad it looks is not established. `ASSET-LICENSING.md` records ~1,250
redistributed files and 661 `.shp` in `mods/ww3mod`, so units are largely self-supplied and
terrain plus base UI are the confirmed casualties. The definitive check is one minute of
someone's time: rename `%APPDATA%/OpenRA/Content` aside and launch. That is a game launch, so it
is the manager's to schedule — and it should happen before anything is published.

---

## Multiplayer

### There is no server to join

The in-game server browser works correctly and shows an empty list. A live check of
`master.openra.net/games` found **334 games across 13 mods and zero WW3MOD**. Two strangers can
currently only meet by one of them hosting and passing the other an IP address directly.

Worth stating plainly on the page rather than letting a player discover it: **if you want a
multiplayer game, bring your own opponent.**

Note that `launch-dedicated.sh` exists in the repo, so hosting an always-on server is a matter of
running it somewhere, not of writing code. That is a user decision, tracked in the checklist.

### WW3MOD advertises on OpenRA's public master server

`WebServices` defaults to `https://master.openra.net/{games,ping}`
(`WebServices.cs:21-22`), `mods/ww3mod/` contains no override, and `mod.yaml:270` registers
`MasterServerPinger`. Every advertised WW3MOD lobby therefore pings, and is probed by,
third-party infrastructure this project does not run. This is a courtesy question as much as a
technical one and needs a decision before launch, not after.

### A dropped player is never defeated

There is no disconnect handling and no rejoin. If someone's connection dies, their units stay on
the map under no one's control and the match does not resolve. Say so.

### One unresolved desync with two human players

Narrowed but not found. Four desyncs reachable by clicking ordinary buttons were fixed and
verified — Patrol and eject-rally now travel as replicated orders (`409b0fd2`), Evacuate likewise
(`e49ff242`), and the AutoTarget stance fields are hashed with the per-machine file cut out of
the simulation (`8afcbbf8`). One remains. A desync ends the game for everyone in it.

*(Correcting a stale source: a 2026-08-16 audit lists eject rally points as an open blocker. That
was fixed the same day in `409b0fd2`. Do not publish it as an open issue.)*

---

## Install and platform

### The macOS build is unsigned

`packaging/macos/buildpackage.sh` signs only when `MACOS_DEVELOPER_IDENTITY` is present, and
notarization only with `MACOS_DEVELOPER_USERNAME` / `MACOS_DEVELOPER_PASSWORD`. Those secrets are
not set, so the `.dmg` ships unsigned and Gatekeeper will refuse to open it — *"WW3MOD cannot be
opened because the developer cannot be verified."*

This must be on the download page **next to the macOS link**, with the right-click → Open
workaround spelled out. A stranger who hits this warning cold assumes malware and leaves.

### The Windows build is unsigned too

There is no Authenticode step in the workflow; `rcedit` is used only to embed an icon. Windows
SmartScreen will show *"Windows protected your PC"* on the installer. Same treatment: warn on the
page, next to the link.

### .NET is not bundled

On a machine without it, double-clicking produces an operating-system error, not a game error —
so the player gets no hint that it's about a missing runtime. State the requirement on the page.

### macOS 10.15 minimum

Set in `packaging/macos/buildpackage.sh:119`; 10.15 is the floor .NET 6 supports. Older Macs are
simply out.

### The game phones home to openra.net on launch

`WebServices.cs:21-26` hardcodes `master.openra.net`, so a stranger's first main menu may show an
**OpenRA** news feed and possibly an OpenRA update prompt. Confusing at best in a mod that
presents itself as its own game.

---

## Gameplay gaps a player will notice

Ranked by how likely someone is to hit them in the first half hour.

1. **The ammo cost tooltip is wrong.** A Bradley's sidebar reads a total ammo cost of 5100 on a
   1500-credit unit while its own per-pool lines say 45 and 600 — the correct total is 645. This
   is the first number a player reads about the mod's headline economy, and it is wrong.
2. **Infantry impacts are silent.** Every small arm — 15 of 57 carried weapons — has empty
   `ImpactSounds`. Sustained small-arms fire reads as ineffective because nothing happens on the
   receiving end.
3. **Nearly every unit shares a voice.** 62 of 75 buildable units draw on the same two voice
   sets, so a helicopter pilot answers an order with the same infantry acknowledgement as a
   rifleman.
4. **The garrison sidebar is a debug panel.** Plain text rows like
   `north: Rifleman [8/10] (80% cover)` with `X` buttons, sitting inside an otherwise finished
   UI. The 80% is a hardcoded literal. Garrisoning itself works properly — it is only the
   interface that is unfinished.
5. **Some sounds and names are Red Alert leftovers.** Weapon fire still uses RA sound files in
   places (`tesla1.aud`, `antbite.aud`, `flamer2.aud`). Around 23 map and husk labels still read
   as Red Alert — "Tesla Coil (Destroyed)", "Prof. Einstein", "Ore Refinery" — and five live
   units are still labelled "Grenadier", "Flamethrower" and "Minelayer".
6. **Around half the build icons are 4px narrow** (36 of 78 cameos are 60×48 in a 64×48 slot),
   giving the sidebar a ragged edge. Tooltips are a fixed 350px slab regardless of text length,
   so short descriptions look padded.
7. **The missions browser is empty.** `missions.yaml` names 49 Red Alert campaign missions, none
   of which ship. A player who clicks Missions finds nothing.
8. **A contested Supply Route tells the player something false** — the notification says
   "Production and income frozen". Income is not frozen.
9. **Aircraft and ships cannot contest a Supply Route.** Only ground units can. Not obviously
   communicated.
10. **Helicopters and airfields cannot rearm on-map.** `HPAD` and `AFLD` both carry
    `Prerequisites: ~disabled` and appear on none of the ten maps, so the on-map rearm shortcut
    doesn't exist in v1. Aircraft carry `ReloadAmmoPool` traits and so reload on their own — this
    is a missing convenience, not a dead unit. **[needs a run]** to judge whether the reload rate
    feels acceptable without it.
11. **Capturing a helicopter gets you nothing.** The capture path works, but the resulting
    airframe has zero speed and zero firepower and burns down in about twelve seconds.
12. **Supply trucks cannot replenish a dropped supply cache** — Ctrl+click offers no cursor and
    issues no order.
13. **The cargo panel disappears when you multi-select.**
14. **Helicopter husks don't sink on water.**
15. **6-player skirmish is slow on a MacBook.** No performance pass has been done.

---

## Things I checked that are NOT problems

Recorded because a known-issues list that only accumulates makes the game look worse than it is,
and two of these are stale entries in `WORKSPACE/RELEASE_V1.md` that would otherwise get
published as faults.

- **Unit icons are done.** 95 of 95 buildables have a working cameo, 94 of them
  WW3MOD-authored. The tracker's open "Unit icons" item is stale.
- **Names and descriptions are complete.** Zero live buildables missing either; zero descriptions
  overflow.
- **Combat is not silent.** 68 weapon `Report:` entries and 238 audio assets. The tracker's open
  "Unit firing sounds / explosion sounds" items are about replacing RA placeholders, not about
  absence.
- **Supply Route contestation is complete and live** — control bar, reinforcement slowdown and
  notifications all ship, despite the tracker marking it open.
- **The stance and three-mode move systems are complete**, including patrol, cohesion and
  resupply stances.
- **Saved games restore correctly**, verified across five runs and both configurations.
- **Garrisoning works in the field** — entry, ownership flip, directional fire, degradation to
  rubble.
