# WW3MOD — first public release checklist

> Dependency order: each gate assumes the one above it is done. Verified against `main` @
> `2a9eb77d`, re-checked at `f882681a` (2026-08-17).
>
> **[USER]** marks items that are the user's alone — decisions, accounts, secrets, and anything
> that publishes. No agent should do these, and none has.
> **[TEAM]** is ordinary repo work.
> **[LAUNCH]** needs a game launch, which is the manager's grant to give.

---

## Gate 0 — Can a stranger play it at all?

Nothing below this line matters until these pass. A store page for a game that doesn't start is
worse than no store page, because the download is the thing you only get one of.

> **Correction, 2026-08-17.** This gate previously led with "make the Red Alert content installer
> reachable", reasoning that `mod.yaml:13`'s `DefaultFileSystem` left the `ModContent:` block
> unreachable. **That was refuted.** `BlankLoadScreen.cs:134-147` is a WW3MOD-authored fallback
> (commit `0132c749`) that bypasses the interface gate entirely; two launches with the content
> directory renamed aside showed the handoff to `modcontent` happening; and `install.log` shows
> the installer has already completed successfully on this machine. Full refutation in
> [`KNOWN-ISSUES.md`](KNOWN-ISSUES.md). **No code or config change is needed here** — what
> remains is verification and the third-party dependency.

- [ ] **[LAUNCH] Watch the installer run to completion, unattended.** Both prior launches were
      killed early and the installer screen was never observed, so what is established is that it
      *fires* and that it has *succeeded here before* — not that it completes today, for someone
      else, on a machine that has never had Red Alert on it. The single remaining unknown in the
      install path.
- [ ] **[LAUNCH] Clean-machine install test, end to end.** A machine that has never had OpenRA or
      Red Alert on it: install the artifact, launch, accept the content download, reach a match.
      This is the test that decides whether the release is real. Ideally all three platforms; at
      minimum Windows.
- [ ] **[TEAM] Pin or self-mirror the content download.** The mirror list is fetched over plain
      HTTP from `http://www.openra.net/packages/ra-quickinstall-mirrors.txt`, and on the recorded
      run resolved to `cdn.mailaender.name`, a third party's CDN. The payload SHA1 *is* verified,
      so this is an availability risk rather than a tampering one — but every first launch depends
      on a host this project does not control.
- [ ] **[TEAM] Consider making a failed content download say so.** The RA mounts are all
      `~`-optional, so a failed or partial download yields a game with no terrain art and no
      explanation.

## Gate 1 — Identity, before anything is public

These are all still openra.net and all of them are visible to a stranger.

- [ ] **[TEAM] `mods/ww3mod/mod.yaml:5` `Website:`** still `https://www.openra.net`. Carries a
      `TODO(release)` already.
- [ ] **[TEAM] `mods/ww3mod/mod.yaml:7` `WebIcon32:`** still the stock **Red Alert** icon hosted
      on openra.net.
- [ ] **[TEAM] `mod.config:47` `PACKAGING_WEBSITE_URL`** = `http://openra.net` and
      **`mod.config:51` `PACKAGING_FAQ_URL`** = `http://wiki.openra.net/FAQ`. These are baked
      into the **Windows installer**, so a stranger installing WW3MOD is shown openra.net links
      during setup.
- [ ] **[USER] Decide the homepage URL** the three items above should point at. Blocks them.
- [ ] **[USER] Decide the master-server position.** WW3MOD currently advertises every lobby on
      OpenRA's public `master.openra.net` with no override, and the main menu may show an
      **OpenRA** news feed and update prompt. Options: leave as-is (works, but leans on
      infrastructure this project doesn't run, and is a courtesy question), point `WebServices`
      at our own (a `mod.yaml` block, not code), or disable advertising. Not an agent's call.
- [ ] **[USER] Choose the release tag.** `PACKAGING_OVERWRITE_MOD_VERSION="True"` means the git
      tag becomes the version string players see; `mods/ww3mod/mod.yaml:3` currently reads
      `release-20230225`, which is the *OpenRA engine* version and would be actively misleading
      on a store page.
- [ ] **[TEAM] Confirm `SOURCE-OFFER.txt` and the `VERSION` file agree with that tag.**
      `SOURCE-OFFER.txt` promises the exact revision is recorded in `VERSION` — a GPL obligation,
      so it needs to be true, not approximately true.

## Gate 2 — Signing and platform warnings

Can ship without these, cannot ship *silently* without these.

- [ ] **[USER] macOS signing secrets, or an explicit decision to ship unsigned.**
      `MACOS_DEVELOPER_IDENTITY`, `MACOS_DEVELOPER_CERTIFICATE_BASE64`,
      `MACOS_DEVELOPER_CERTIFICATE_PASSWORD` for signing; `MACOS_DEVELOPER_USERNAME` /
      `MACOS_DEVELOPER_PASSWORD` for notarization. Only the user can hold these.
- [ ] **[USER] Windows: sign, or accept SmartScreen.** No Authenticode step exists today.
- [ ] **[TEAM] If shipping unsigned, put the workaround next to the download link** — not in a
      FAQ, not below the fold. Right-click → Open for macOS; "More info" → "Run anyway" for
      Windows. Draft wording is in [`STORE-COPY.md`](STORE-COPY.md) §6.
- [ ] **[TEAM] State the .NET runtime requirement and macOS 10.15 floor on the page.** A missing
      .NET produces an OS-level error with no hint it concerns the game.

## Gate 3 — Say the multiplayer situation out loud

- [ ] **[USER] Decide whether to host a dedicated server.** `launch-dedicated.sh` exists, so this
      is a hosting decision, not a development one. Right now two strangers can only meet by
      swapping an IP by hand, and the browser shows an empty list — which reads as a dead game
      rather than a new one.
- [ ] **[TEAM] Disclose: a dropped player is never defeated.** No disconnect handling, no rejoin.
- [ ] **[TEAM] Disclose: one unresolved desync with two human players.** Four were fixed and
      verified; one remains and it ends the match for everyone.
- [ ] **[TEAM] Decide what the page says about finding a game.** Recommended: state plainly that
      multiplayer means bringing your own opponent, and give the direct-IP instructions. A player
      who was told brings a friend; a player who wasn't posts that the game is dead.

## Gate 4 — The page itself

- [x] **[TEAM] Store copy drafted** — [`STORE-COPY.md`](STORE-COPY.md).
- [x] **[TEAM] Known issues drafted** — [`KNOWN-ISSUES.md`](KNOWN-ISSUES.md).
- [ ] **[LAUNCH] Capture screenshots.** Shot list with priorities in [`STORE-COPY.md`](STORE-COPY.md)
      §7. This is the highest-leverage remaining item on the page — the header image decides more
      downloads than any paragraph. It needs a launch grant and, for the contested-Supply-Route
      shot, a set-up match.
- [ ] **[TEAM] Re-check every factual claim in the copy** against whatever HEAD is tagged. The
      copy cites unit counts and completed systems; several tracker entries were already stale
      when this was written.
- [ ] **[USER] Create the ModDB / itch account and page.** Not an agent's to do, and none has.
- [ ] **[USER] Publish.**

---

## What I would cut to ship sooner

If the goal is a first public release rather than a complete one, everything in
[`KNOWN-ISSUES.md`](KNOWN-ISSUES.md) under "Gameplay gaps" can ship as-is and be disclosed —
including the wrong ammo tooltip, the debug garrison sidebar and the Red Alert leftover names.
They are embarrassing, not blocking, and an early public release is allowed to be rough.

Three things I would not cut, because they are the difference between a rough game and a broken
one:

1. **Gate 0's remaining verification.** Not a fix any more — just someone watching the installer
   finish once on a machine that has never had Red Alert on it. A player who cannot start does
   not file a bug, they leave.
2. **The money pump.** An unbounded credit loop in a game whose entire pitch is that cost is
   budget allocation doesn't read as a bug to a stranger; it reads as the game not working. It is
   being fixed — just don't ship ahead of it.
3. **The unsigned-build warnings on the page.** Free to write, and they convert the single most
   common silent bounce into a two-second extra click.

The master-server decision (Gate 1) is the one I would push the user to make consciously rather
than by default, because "we didn't decide" and "we advertise on someone else's infrastructure"
currently look identical from the outside.
