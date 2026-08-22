# WW3MOD — known issues for the first public release

> Verified against `main` @ `2a9eb77d`, re-checked at `f882681a` (2026-08-17) by reading the
> tree. I did not launch the game and did not run any autotest. Where a runtime observation
> appears below it came from a manager launch and is attributed as such; everything else is
> static. Items still needing a run are marked **[needs a run]**.
>
> This list is written to be published, lightly edited, alongside the download. For a first
> public release a disclosed limitation costs a line of text; a discovered one costs the player.

---

## RETRACTED — "the content installer can never run"

An earlier version of this document opened with a blocker claiming the Red Alert content
installer was unreachable configuration and that a stranger would never be asked to install the
data files. **That was wrong.** It is recorded here rather than deleted, because the same wrong
inference has now been drawn from the same code three times in one day, and a refuted claim with
its refutation attached is what prevents a fourth.

**The wrong reasoning:** `mods/ww3mod/mod.yaml:13` declares `FileSystem: DefaultFileSystem`; the
installer appears to be gated on an `IFileSystemExternalContent` check at
`BlankLoadScreen.cs:131-132`; only `ContentInstallerFileSystemLoader` implements that interface;
a tree-wide grep shows no mod uses it. Every one of those facts is true.

**Why the conclusion doesn't follow: the method does not end at line 132.**
`BlankLoadScreen.cs:134-147` is a second, WW3MOD-authored route added by commit `0132c749`
("Fix content installer not triggering on fresh installs", 2026-04-04). It checks the manifest
for a `ModContent` section, tests whether the required packages' `TestFiles` exist, and when they
don't calls `Game.InitializeMod(modContent.ContentInstallerMod, "Content.Mod=ww3mod")`. It never
touches the interface gate. The chain downstream resolves with no `mod.yaml` change needed:
`ContentInstallerMod` defaults to `"modcontent"` (`ModContent.cs:101`); `LogoStripeLoadScreen`
inherits `BeforeLoad` unchanged, so the fallback is live; and
`ModContentPromptLogic.LoadYamlFromModPackage` reads `mod.Package` directly rather than through
the mounted filesystem, so `installer/downloads.yaml` resolves even though the `modcontent` mod
mounts no `ww3mod` package.

**Three independent confirmations:**

1. The code, read past line 132.
2. **Two launches** with `~/Library/Application Support/OpenRA/Content` renamed aside. Both logged
   `Loading mod: ww3mod` followed by `Loading mod: modcontent` — the game does not die at mod
   load; it hands off to the content-installer mod and stays there, running.
3. **The installer has already completed successfully on this machine.**
   `~/Library/Application Support/OpenRA/Logs/install.log` records the mirror fetch, a download
   from `https://cdn.mailaender.name/openra/ra-quickinstall.zip`, a SHA1 matching the expected
   value, and extraction of `allies.mix`, `conquer.mix`, `expand/jyes1.aud`, `cnc/desert.mix` and
   the rest. Its mtime is **7 May 14:54**, matching `Content/ra/v2` exactly. That is how the Red
   Alert content reached this machine: the installer ran, and it worked.

**The process lesson:** this was already corrected in `WORKSPACE/DISCOVERIES.md` — an entry dated
2026-08-16, explicitly headed as correcting finding B of
`WORKSPACE/audit/260816-install-packaging.md` — before this document was written. The audit was
read; `DISCOVERIES.md` was not. When an audit finding is load-bearing, check whether a later
discovery has already superseded it.

---

## Open — the real state of first-run install

These survive the retraction and belong on the page or in the checklist.

### Nobody has watched the installer finish, unattended, on a clean machine

Both verification launches were killed early and the installer screen itself was never observed.
The `modcontent` mod carries no Test screenshot hooks and macOS denies screen recording on this
machine, so the evidence is "it fires, and it has completed here before" — which is not "it runs
to completion today, unattended, for a stranger on a machine that has never had Red Alert on
it." **[needs a run]** This is the one remaining unknown in the install path, and the thing most
worth doing before a download link goes anywhere.

### The download depends on infrastructure this project does not control

The mirror list is fetched over **plain HTTP** from `http://www.openra.net/packages/`, and on the
one recorded run it resolved to `cdn.mailaender.name`, a third party's CDN. Payload *integrity*
is protected — the manifest pins a SHA1 and the log shows it being checked — so this is an
**availability** risk rather than a tampering one: if that host goes away or the mirror list
moves, every first launch fails. Worth pinning or self-mirroring before a public release.

### A failed download fails silently

Every RA mount in `mod.yaml:15,24-40` carries the `~` optional prefix, so if the content is
absent or the download fails the game does not error — it loads without terrain art. The data is
genuinely required: `tilesets/temperat.yaml:79` references `clear1.tem`, the base ground tile,
which is not in this repo (it lives inside `temperat.mix`). A player who hits a failed download
gets a broken-looking game instead of a message telling them what went wrong.

### Packaging, for the record

`mod.config:108` sets `PACKAGING_COPY_ENGINE_FILES="./mods/ra ./mods/modcontent"`, so the
artifact contains the four mods a clean machine needs (`ww3mod`, `ra`, `modcontent`, `common`).
Confirmed by opening the Windows artifact.

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
8. ~~**A contested Supply Route tells the player something false** — the notification says
   "Production and income frozen". Income is not frozen.~~ **Fixed 2026-08-22** (`wt/sr-message`):
   both notifications now read "Production frozen", and the freeze line is suppressed entirely when
   the overrun player is being defeated in the same tick (free-for-all, a solo lobby team, or the
   last survivor of a team) — it previously printed one line above "is defeated".
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

## Being fixed right now — do NOT publish these as shipping defects

Both were found on 2026-08-17 and both are under active repair. Neither fix is on `main` as of
`f882681a` — I checked. **Re-check both before this section goes anywhere**; if the fixes have
landed by release, delete these two entries rather than shipping them.

- **The Mi-28 cannot engage aircraft at all, and its tooltip says it can.** The description on
  `MI28` (`aircraft-russia.yaml:277`) reads *"Can engage aircraft"* as an explicit bullet, on a
  unit costing **6000** — the most expensive thing a Russian player can order. A player buys it
  for exactly the job it advertises and it cannot do that job.
- **There is a live money pump.** The `LCCV` costs **1200** (`vehicles.yaml:618`) and deploys
  into a `LOGISTICSCENTER` worth **3500** (`structures.yaml`), which sells at full value —
  roughly **+2300 credits per cycle**, repeatable with no cooldown. In a game whose entire
  economy is budget allocation, an unbounded credit loop is not a balance issue; it is the
  economy not existing. It also bypasses the buildable gate: `Transforms` does not consult
  `Buildable.Prerequisites` (audited in `b6335798`).

---

## Things I checked that are NOT problems

Recorded because a known-issues list that only accumulates makes the game look worse than it is.
Four claims were checked and refuted before they reached a public page — two of them stale
entries in `WORKSPACE/RELEASE_V1.md`, one a superseded audit finding, and one my own.

- **The Red Alert content installer works.** See the retraction at the top of this document. This
  was my error, and it was the most consequential of the four.
- **The 2026-08-16 audit's one `[BLOCKER]`** — eject rally points as a client-local write read by
  the simulation — **was fixed the same day** in `409b0fd2`. Do not publish it.
- **Aircraft are not stranded without a helipad.** `HPAD`/`AFLD` are disabled and on no map, but
  every airframe carries `ReloadAmmoPool` and self-reloads. A missing convenience, not a dead
  unit.
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
