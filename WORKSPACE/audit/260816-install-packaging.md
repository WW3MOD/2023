# Install & packaging audit — what a stranger actually downloads, installs and launches

**Audited:** 2026-08-16, read-only, against `main` @ **`81e5a440`**, working tree carrying only
`WORKSPACE/PIPELINE.md` (modified) and two untracked audit/manager dirs.

> **Ref correction.** The brief specified `main @ 55459146`. That is not HEAD. `55459146`
> is two commits back ("merge wt/heli-gun"); actual HEAD is `81e5a440`. Nothing in this
> audit touches the three-commit delta (heli tuning + a maestro records commit), so the
> findings hold for both refs — but the stamp is `81e5a440`.

> ## SUPERSEDED IN PART — re-verified 2026-08-19 against `main` @ `4f5123dc`
>
> **Findings A, B and D have all shipped. Do not dispatch work against them.**
>
> - **A (`mods/ra` not packaged)** — FIXED. `mod.config` now sets
>   `PACKAGING_COPY_ENGINE_FILES="./mods/ra ./mods/modcontent"`, and all three platform
>   scripts gained the loop that consumes it (`packaging/{linux,macos,windows}/buildpackage.sh`).
> - **B (content installer is dead configuration)** — FIXED, by a different route than this
>   audit proposed. `mod.yaml` still reads `FileSystem: DefaultFileSystem`, but
>   `BlankLoadScreen.BeforeLoad` gained a WW3MOD fallback (`:134-148`) that reads the
>   `ModContent` manifest directly and calls `Game.InitializeMod(ContentInstallerMod)`.
>   `ContentInstallerMod` defaults to `"modcontent"` (`ModContent.cs:101`), which packaging
>   now ships. **Switching to `ContentInstallerFileSystemLoader` is no longer needed.**
> - **D (no artifact; retired runners)** — runners are now `ubuntu-22.04` / `macos-15` /
>   `windows-2022`, and **the packaging workflow has run green on all three platforms**
>   (`workflow_dispatch` run `31972210309`, 2026-08-16): `linux-appimage` 95 MB,
>   `macos-dmg` 129 MB, `windows-installers` 354 MB. The sentence below claiming no
>   distributable artifact has ever been produced is **no longer true.**
> - **The GPLv3 gap** recorded in `WORKSPACE/closeout/art-6cde8456.md` is also satisfied:
>   `SOURCE-OFFER.txt` exists and is installed by all three scripts, alongside the `COPYING`
>   that `install_data` already copied.
>
> **What is still true:** there is no git tag and no GitHub release, so a stranger still has
> nothing to download — those artefacts sit behind GitHub auth (anonymous download returns
> 401) and expire 2026-11-14. Findings C, E, F, G, H and I are unchanged.

**Verdict up front (as written 2026-08-16 — read the correction above first): no. A stranger
cannot install and play WW3MOD today, and the gap is not
cosmetic.** No distributable artifact has ever been produced, and if one were produced right
now from these scripts it would fail to reach the main menu on a clean machine.

---

## 1. The ordered first-launch walk

This is the literal sequence from double-clicking a download to being in a first match.
Steps 1–2 are hypothetical because **no installer has ever been built** (§3).

| # | Step | State | Evidence |
|---|---|---|---|
| 0 | Stranger looks for a download | **BROKEN** | No GitHub release exists. Only one tag in the repo, `prerebase-tanktrap`, which is not a release tag. `git tag -l` → 1 entry. |
| 1 | Runs `WW3MOD-<tag>-x64.exe` | **UNKNOWN — NEEDS A RUN** | `packaging/windows/buildpackage.sh` produces this name from `PACKAGING_INSTALLER_NAME="WW3MOD"` (`mod.config:31`). Never executed. |
| 2 | Welcome page | FINE | `packaging/windows/buildpackage.nsi:47`. Title is `WW3MOD` (`mod.config:41`). |
| 3 | **Licence page shows the raw GPLv3 legal text** | ROUGH | `buildpackage.nsi:48` renders `PACKAGING_WINDOWS_LICENSE_FILE="./COPYING"` (`mod.config:97`), which is the unmodified 674-line GNU GPL v3 body starting "GNU GENERAL PUBLIC LICENSE / Version 3, 29 June 2007". No WW3MOD preamble, no asset-licensing note. |
| 4 | **Install directory defaults to `C:\Program Files\OpenRA WW3MOD`** | COSMETIC | `mod.config:91` `PACKAGING_WINDOWS_INSTALL_DIR_NAME="OpenRA WW3MOD"`, consumed at `buildpackage.nsi:37`. |
| 5 | **Start Menu folder defaults to `OpenRA`** | COSMETIC | `buildpackage.nsi:54` `MUI_STARTMENUPAGE_DEFAULTFOLDER "OpenRA"` — hardcoded in the .nsi, not driven by `mod.config`. |
| 6 | Components page, files copied | FINE | `buildpackage.nsi:59,91–133`. |
| 7 | **Desktop shortcut is named `OpenRA - WW3MOD`** | COSMETIC | `buildpackage.nsi:137` — the `OpenRA - ` prefix is hardcoded. |
| 8 | Registry written under `HKLM\Software\OpenRAWW3MOD` | COSMETIC | `mod.config:94`, `buildpackage.nsi:74`. |
| 9 | **URL scheme registers as "URL:Join OpenRA server"** | COSMETIC | `buildpackage.nsi:77` — literal string, not templated. |
| 10 | Uninstaller entry in Add/Remove Programs | ROUGH | Name `WW3MOD`, Publisher `FreadyFish & CmdrBambi` (`mod.config:56`) — both fine. But `URLInfoAbout` → `http://openra.net` (`mod.config:45`, `buildpackage.nsi:152`), flagged in-repo with a `TODO(release)`. |
| 11 | Launches `WW3MOD.exe` | **BROKEN if .NET absent** | §5. |
| 12 | **Engine mounts the mod filesystem → hard exception** | **BROKEN** | §2, finding A. This is where a clean machine stops. |
| 13 | Content installer prompt | **BROKEN — can never appear** | §2, finding B. |
| 14 | Main menu / shellmap `River Zeta WW3` | not reached | `mods/ww3mod/mod.yaml:8`. |
| 15 | Skirmish lobby → first match | not reached | — |

**The walk terminates at step 12 on any machine that is not the developer's.**

---

## 2. Content: what the game demands that this repo does not contain

### [BLOCKER] A — The packaged game is missing the `ra` mod it hard-depends on, and dies before the menu

**Perceived:** the stranger double-clicks the shortcut; the process exits. On a packaged
build the Windows launcher shows *"WW3MOD has encountered a fatal error and must close"*
with **View FAQ** / **View Logs** buttons. There is no hint that content is missing.

`mods/ww3mod/mod.yaml:20` mounts a **mod-package reference**:

```
$ra: ra
```

and then mounts four paths *through* it (`mod.yaml:42–45`): `ra|bits`, `ra|bits/desert`,
`ra|scripts`, `ra|uibits`; plus four localisation files at `mod.yaml:225–228`
(`ra|fluent/chrome.ftl`, `hotkeys.ftl`, `rules.ftl`, `ra.ftl`).

**None of these carry the `~` optional prefix.** In `engine/OpenRA.Game/FileSystem/FileSystem.cs:83–113`,
`Mount` only swallows failures `catch when (optional)` (line 110). For a non-optional `$`
entry whose mod is not installed it throws:

```csharp
// FileSystem.cs:97-98
if (!installedMods.TryGetValue(name, out var mod))
    throw new InvalidOperationException($"Could not load mod '{name}'. Available mods: ...");
```

Now, what does the package actually contain? All three platform scripts call `install_data`
with **exactly two arguments**:

- `packaging/windows/buildpackage.sh:105` → `install_data "${TEMPLATE_ROOT}/${ENGINE_DIRECTORY}" "${BUILTDIR}"`
- `packaging/linux/buildpackage.sh:69`
- `packaging/macos/buildpackage.sh:142`

And `install_data` copies extra mods only inside a loop over *trailing* arguments:

```sh
# engine/packaging/functions.sh:118-131
cp -r "${SRC_PATH}/mods/common" "${DEST_PATH}/mods/"
while [ -n "${1}" ]; do            # ← after `shift 2`, $1 is empty. Loop never runs.
    if [ "${MOD_ID}" = "ra" ] || ... ; then
        cp -r "${SRC_PATH}/mods/${MOD_ID}" "${DEST_PATH}/mods/"
        cp -r "${SRC_PATH}/mods/modcontent" "${DEST_PATH}/mods/"
```

The SDK's documented escape hatch for this is `PACKAGING_COPY_ENGINE_FILES`, and it is
**empty**: `mod.config:104` → `PACKAGING_COPY_ENGINE_FILES=""`. The only other mod copy is
`cp -Lr "${TEMPLATE_ROOT}/mods/"*` (`packaging/windows/buildpackage.sh:120`), and
`mods/` in this repo contains **only `ww3mod`** — `ls mods/` → `ww3mod`. There is no
`mods/ra`, and no symlink (`find mods -maxdepth 2 -type l` → empty).

So the shipped `mods/` directory would be `common` + `ww3mod`. `ra` is absent, `$ra: ra`
throws, the game dies during mod load.

**Why this has never been noticed:** in the dev tree `ra` resolves from `engine/mods/ra`,
which exists and is scanned. The defect is invisible until you package.

- **Confidence: high** that `ra` is not copied (three scripts read, function read, dirs listed).
- **Confidence: medium-high** that this is fatal rather than degraded — the throw is
  unambiguous, but I did not execute it. **NEEDS A RUN** to see the exact user-facing
  surface: `./packaging/linux/buildpackage.sh test-audit /tmp/out` on a Linux host, then
  launch the AppImage in a container with no `~/.config/openra`. That would prove both the
  failure and its presentation in one shot.
- **Fix size: one line** — `PACKAGING_COPY_ENGINE_FILES="./mods/ra ./mods/modcontent"` in
  `mod.config:104`. (Ships upstream RA's yaml/art alongside; verify size impact.)

### [BLOCKER] B — The Red Alert content installer is dead configuration and can never run

**Perceived:** even with finding A fixed, the stranger reaches the menu into a game with no
terrain and no base art, and is **never asked to install anything**.

`mods/ww3mod/mod.yaml:396–410` contains a carefully hand-written, WW3MOD-specific
`ModContent:` block — including prose written for this project:

> `InstallPromptMessage: WW3MOD is a total conversion of Command & Conquer: Red Alert and\nstill loads artwork and audio from that game's data files.\n\nQuick Install downloads the required files...`

…with `base`, `aftermathbase` and `cncdesert` all marked `Required: true`, seven `Sources:`
files and a `Downloads:` list pointing at `http://www.openra.net/packages/*-mirrors.txt`
(`mods/ww3mod/installer/downloads.yaml:3,47,62,95`).

**None of it can ever execute.** The installer is gated on an interface check:

```csharp
// engine/OpenRA.Mods.Common/LoadScreens/BlankLoadScreen.cs:131-132
if (ModData.FileSystemLoader is IFileSystemExternalContent content)
    return !content.InstallContentIfRequired(ModData);
```

Only `ContentInstallerFileSystemLoader` implements `IFileSystemExternalContent`
(`engine/OpenRA.Mods.Common/FileSystem/ContentInstallerFileSystemLoader.cs:17`).
`mods/ww3mod/mod.yaml:13` declares `FileSystem: DefaultFileSystem`, whose loader
(`DefaultFileSystemLoader`, same directory, line 23) implements only `IFileSystemLoader`
and does nothing but mount. Grepping the whole tree, **no mod uses
`ContentInstallerFileSystemLoader`** — `grep -rn "ContentInstallerFileSystemLoader" engine/mods/ mods/` returns nothing.

Compounding it, every RA data mount is marked optional and therefore fails **silently**:
`~^SupportDir|Content/ra/v2/` (`mod.yaml:15`), `~main.mix`, `~conquer.mix`, `~temperat.mix`,
`~snow.mix`, `~interior.mix`, `~speech.mix` … (`mod.yaml:24–40`).

**And the game genuinely needs that data.** The terrain is not redistributed:
`mods/ww3mod/tilesets/temperat.yaml:79` references `Images: clear1.tem` (the base ground
tile), and `clear1.tem` **is not in this repo** — `find . -name clear1.tem -not -path ./engine/*`
returns nothing. The 87 `.tem` files that are present are all custom
(`bits/misc/resources/scrap01.tem` …). `clear1.tem`, `p08.tem`, `rf10.tem` etc. live inside
`temperat.mix`.

**Why this has never been noticed — the single most important fact in this audit:** the
development machine already has the full Red Alert content installed. I listed it:
`%APPDATA%/OpenRA/Content/ra/v2/` contains `allies.mix, conquer.mix, hires.mix,
interior.mix, local.mix, lores.mix, russian.mix, snow.mix, sounds.mix, speech.mix,
temperat.mix` plus `expand/` and `cnc/`. **WW3MOD has only ever been run on a machine where
the prerequisite it never installs was already satisfied.**

- **Confidence: high** on the mechanism (interface gate read, both loaders read, tree-wide grep clean, terrain tile absence confirmed).
- **Confidence: medium** on *how much* breaks visually — `ASSET-LICENSING.md` records ~1,250
  redistributed files and 661 `.shp` in `mods/ww3mod`, so units are largely self-supplied;
  terrain and base UI are the confirmed casualties. **NEEDS A RUN** to enumerate precisely:
  rename `%APPDATA%/OpenRA/Content` aside and launch. That is a one-minute check that
  answers "how bad" definitively — but it is a game launch, so it is the manager's to schedule.
- **Fix size: small config, medium verification** — switch `mod.yaml:13` to
  `ContentInstallerFileSystemLoader`, split `Packages:` into `SystemPackages:` /
  `ContentPackages:`, set `ContentInstallerMod: modcontent`, and ship `mods/modcontent`
  (same one-line fix as A). Half a day plus a real clean-machine test.

### [SHOULD-FIX] C — The download mirrors are third-party and unpinned

**Perceived:** if the installer is revived, first launch downloads ~30 MB from openra.net.

`mods/ww3mod/installer/downloads.yaml:3` — `MirrorList: http://www.openra.net/packages/ra-quickinstall-mirrors.txt`,
plain **HTTP**, pointing at infrastructure this project does not control. SHA1s are pinned
(`44241f68…`), which limits tampering, but availability is someone else's decision.
Confidence: high on the config; **NEEDS A RUN** to confirm the mirror list still resolves.
Fix size: small (rehost) / policy call.

---

## 3. Does a distributable artifact exist? No — and CI cannot currently produce one

### [BLOCKER] D — No release has ever been built, and the release workflow targets retired runners

**Perceived:** there is nothing to download.

`.github/workflows/packaging.yml:1–8` triggers on `push: tags: ['*']` or manual dispatch,
and defines three jobs producing:

- **Linux:** `WW3MOD-<tag>.AppImage` (`packaging/linux/buildpackage.sh`)
- **macOS:** `WW3MOD-<tag>.dmg` (`packaging/macos/buildpackage.sh`)
- **Windows:** `WW3MOD-<tag>-x86.exe`, `WW3MOD-<tag>-x64.exe`, plus
  `WW3MOD-<tag>-{x86,x64}-winportable.zip` (`packaging/windows/buildpackage.sh:150,154`)

Naming is correctly WW3MOD-branded throughout — that part is done.

But: the repo has **one tag**, `prerebase-tanktrap`, which is a working checkpoint, not a
release. There is no `build/` output, no artifact committed, and no release-shaped commit in
`git log`. Nothing has ever been packaged.

And the workflow would not succeed if triggered today:

- `packaging.yml:26` → `runs-on: macos-11` — **retired** by GitHub (removed 2024).
- `packaging.yml:80` → `runs-on: windows-2019` in `ci.yml:66` — also retired.
- macOS signing depends on five `secrets.MACOS_DEVELOPER_*` (`packaging.yml:61–66`) that
  almost certainly are not set; unsigned `.dmg` → Gatekeeper blocks it on a stranger's Mac.

Confidence: high on tags/artifacts/runner strings (all read directly). **NEEDS A RUN** to
confirm CI failure mode: `gh workflow run packaging.yml` or push a throwaway tag — the
manager's call, since it publishes a GitHub release.
Fix size: small for runner labels (`macos-13`, `windows-2022`); the signing story is a
separate decision.

### [SHOULD-FIX] E — The Windows installer cannot be built on the machine that develops it

**Perceived:** developer-facing, but it is why finding A survived.

`packaging/windows/buildpackage.sh:3–7` requires `curl`/`wget`, `makensis`, ImageMagick
`convert`, `python3` and **`wine64`** — and `packaging/package-all.sh:36` routes Windows
packaging to the Linux branch outright. This project is developed on Windows 11
(`make.ps1`, `launch-game.cmd`), and `make.ps1` has **no packaging target at all**: its
`switch ($execute)` (line 425–440) offers `all/version/clean/test/nav-guard/check/check-scripts`
and nothing else. The `Makefile` likewise has no `package` target.

So the only route to a Windows installer is GitHub Actions (finding D) or a Linux box.
Confidence: high. Fix size: none required — but it means **every packaging fix must be
verified in CI, not locally**, which the manager should plan around.

---

## 4. Identity through the install chain

The parallel audit's five findings are **all confirmed** — `OpenRA WW3MOD` install dir
(`mod.config:91`), `OpenRAWW3MOD` registry key (`:94`), `OpenRA` Start Menu default
(`buildpackage.nsi:54`), and the FAQ button → `wiki.openra.net` (`mod.config:51`, consumed
at `engine/OpenRA.WindowsLauncher/Program.cs:50`). I could not locate a
`<Product>OpenRA</Product>` element in the packaging path; it is presumably in an engine
`.csproj`/assembly-info and I did not confirm it — **treat that one as unverified by me.**

Going past them, on the chain the parallel audit did not cover:

### [SHOULD-FIX] F — The game phones master.openra.net on launch and may offer to "update"

**Perceived:** a stranger's first main menu may show an OpenRA news feed, and possibly an
"update available" nag pointing at a build that is not this mod.

`engine/OpenRA.Mods.Common/WebServices.cs:21–26` hardcodes `master.openra.net/games`,
`/ping`, `/gamenews`, `/versioncheck`. `CheckModVersion()` (`:31`) submits
`Manifest.Metadata.Version`, which `mods/ww3mod/mod.yaml:3` sets to **`release-20230225`** —
i.e. WW3MOD identifies itself to OpenRA's server as a three-year-old engine release. This
is unmodified upstream code; the *value* it reports is a WW3MOD choice.
Confidence: high on the code path; **medium on the visible outcome** — whether the server
actually returns an outdated verdict and what UI that drives **NEEDS A RUN** (or a manual
`curl https://master.openra.net/versioncheck?...`).
Fix size: small (set a distinct `Version:`; point or disable the services).

### [POLISH] G — Remaining openra.net branding, already flagged in-repo

`mods/ww3mod/mod.yaml:5` `Website: https://www.openra.net` and `:7`
`WebIcon32: https://www.openra.net/images/icons/ra_32x32.png` — **the mod's web icon is the
stock Red Alert logo hotlinked from openra.net**, which is what any server browser listing
would display. Both carry `TODO(release)` comments already, as does `mod.config:44,50`.
`DiscordService: ApplicationId: 699222659766026240` (`mod.yaml:412`) is likewise inherited
and will render OpenRA's Rich Presence art — confidence medium, I did not verify the app ID's owner.
Fix size: trivial once a homepage exists.

**No crash telemetry.** `engine/OpenRA.Game/Support/ExceptionHandler.cs` writes a local
`exception-<timestamp>.log` only; nothing is uploaded. Good news — no privacy surprise.

---

## 5. Runtime prerequisites

### [SHOULD-FIX] H — .NET is not bundled and its absence is an OS-level error, not a game error

**Perceived:** on a machine without .NET, double-clicking produces a Windows "framework not
found" dialog or a console stack trace — nothing that says WW3MOD.

`engine/Directory.Build.props:23` targets `net6.0` with `RollForward: Major` (`:28`), so
.NET 6/7/8/9 runtimes all satisfy it, but **.NET 5 and below do not**, and nothing is
self-contained (the NSIS script ships `.deps.json` and `.runtimeconfig.json`,
`buildpackage.nsi:99–100`, which is the framework-dependent shape). There is no `global.json`.

The documentation is inconsistent with the build: `README.md:34` states "**.NET 8 or later is
required**", `CLAUDE.md` says `make test` needs ".NET 6 runtime specifically", and the build
targets net6. Whatever the truth, **the installer never checks and never tells the user** —
`buildpackage.nsi` has no runtime-detection section.
Confidence: high on the config; **medium on the exact dialog text** — that is OS/loader
behaviour and **NEEDS A RUN** on a clean VM. Fix size: small (an NSIS prerequisite check),
or larger if you choose self-contained publishing.

### [POLISH] I — OpenGL 3.2 required; failure message is unactionable

`engine/OpenRA.Platforms.Default/OpenGL.cs:687` accepts GL ≥ 3.2 (or GLES ≥ 3.0, `:680`);
otherwise `:549` throws `InvalidProgramException("OpenGL Version Error: See graphics.log for details.")`.
A stranger on an old integrated GPU gets that string and must find `graphics.log` unaided.
Upstream behaviour, not a WW3MOD defect. Confidence: high. Fix size: small (friendlier text).

---

## 6. Shortest path to a stranger being able to play

In dependency order. Items 1–3 are the blocking set.

1. **`mod.config:104`** → `PACKAGING_COPY_ENGINE_FILES="./mods/ra ./mods/modcontent"`. One
   line; unblocks finding A and supplies the installer mod for B.
2. **`mods/ww3mod/mod.yaml:13`** → switch to `ContentInstallerFileSystemLoader`, restructure
   `Packages:` into `SystemPackages:` / `ContentPackages:`, add `ContentInstallerMod: modcontent`.
   This makes the already-written `ModContent:` prose actually reachable.
3. **`.github/workflows/packaging.yml`** → `macos-13` / `windows-2022`; tag and run.
4. Verify on a genuinely clean machine with `%APPDATA%/OpenRA/Content` absent. **This is the
   step that has never happened and is the reason all of the above is only now visible.**
5. Then the cosmetics: Start Menu folder, desktop-shortcut prefix, website/web-icon,
   version-check identity.

**One caveat on my own confidence.** Findings A and B are reasoned from reading the
packaging scripts, the mount code and the loader interface gate — not from executing a build
or a launch, both of which were out of scope. The reasoning chain is short and each link was
read directly, so I rate it high; but a single Linux packaging run plus one launch with the
content directory renamed aside would convert the whole of §2 from "high confidence" to
"observed", and I would take that trade before anyone spends a day on fixes.
