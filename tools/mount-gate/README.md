# mount-gate

Static check that every **non-optional** entry in `mods/ww3mod/mod.yaml`'s `FileSystem:
Packages:` and `MapFolders:` blocks resolves **inside a packaged output**.

## Why this exists

v0.1.0 shipped unable to reach the main menu. `MapFolders` carried

```
	^EngineDir|../tools/autotest/scenarios: Unknown
```

with no `~`. `tools/` is a development-checkout directory that no installer ships, and
`MapCache.LoadMaps` rethrows the missing-package exception for a non-optional entry
(`engine/OpenRA.Game/Map/MapCache.cs:113-119`), so the game died during `ModData`
construction on every real installation.

No gate could catch it, because **every check we run executes against the git checkout,
where `tools/` exists**. `--check-yaml`, `nav-guard`, `lua-gate` and `smudge-gate` all
resolve paths against the repo. This gate models the *packaged* filesystem instead.

The one fact that makes the bug possible:

| | `Platform.EngineDir` is… | so `^EngineDir\|../tools` is… |
|---|---|---|
| dev checkout | `engine/` — `launch-game.sh` passes `Engine.EngineDir=".."`, and `OverrideEngineDir` resolves a relative path against `BinDir` = `engine/bin/` (`Platform.cs:265-282`) | the repo root's `tools/`, which exists |
| packaged build | the packaged root — nothing passes `Engine.EngineDir`, so it falls back to `BinDir` (`Platform.cs:247-260`) | one level *above* everything the installer shipped |

## Running it

Buildless, no engine, no launch, ~0.1 s. Default subcommand is `check`.

```bash
# 1. Against a real packaged output -- the authoritative form.
python tools/mount-gate/mount_gate.py check --zip /path/to/WW3MOD-vX.Y.Z-x64-winportable.zip
python tools/mount-gate/mount_gate.py check --tree /path/to/squashfs-root
python tools/mount-gate/mount_gate.py check --tree "/Applications/WW3MOD.app"

# 2. With no artifact to hand: a tree PREDICTED from the packaging scripts.
#    This is what `make test` / `make.ps1 test` run.
python tools/mount-gate/mount_gate.py check

# 3. Diagnostics.
python tools/mount-gate/mount_gate.py explain --zip <zip>   # every mount and how it resolves
python tools/mount-gate/mount_gate.py check -v              # also list absent OPTIONAL mounts
python tools/mount-gate/mount_gate.py selftest              # assert the gate still bites
```

`--tree` auto-descends: point it at an extracted AppImage's top level, or at
`squashfs-root`, or at a `.app`, and it finds the directory holding `mods/`.

`--mod-yaml <path>` reads that manifest instead of the one in the tree. It exists so a
historical manifest can be run against a current tree — that is how the v0.1.0 regression
test below works.

### Exit codes

| | |
|---|---|
| `0` | every non-optional mount resolves. Warnings may still have printed. |
| `2` | at least one does not — or a usage error. |
| `3` | the tree/zip given could not be read (wrong path, no `mods/<id>/mod.yaml` inside). |

A pass prints one line: `mount-gate: OK -- every non-optional mount resolves inside the
packaged output.` A failure prints one block per finding — severity, section and line
number in the manifest, the offending entry, a finding code and what to do about it —
then `mount-gate: FAIL -- N non-optional mount(s) do not resolve inside the packaged
output.`

## The regression test

The acceptance pair is a real one, not a synthetic one. v0.1.0's binary artifacts are no
longer on the releases page (`gh release view v0.1.0` → *release not found*), but the tag
survives, so the *manifest* that shipped is recoverable exactly:

```bash
git show v0.1.0:mods/ww3mod/mod.yaml > /tmp/mod-v0.1.0.yaml
gh release download v0.1.2 --repo WW3MOD/2023 -p 'WW3MOD-v0.1.2-x64-winportable.zip'

# The healthy release must pass.
python tools/mount-gate/mount_gate.py check --zip WW3MOD-v0.1.2-x64-winportable.zip
# -> exit 0

# The shipped-broken manifest, against a real packaged tree, must fail.
python tools/mount-gate/mount_gate.py check --zip WW3MOD-v0.1.2-x64-winportable.zip \
    --mod-yaml /tmp/mod-v0.1.0.yaml
# -> exit 2, escapes-package on MapFolders:106
```

`selftest` covers the same shape without the network, against
`fixtures/packaged-root` — a **synthetic** tree with the directories an installer really
ships and no `tools/`. `fixtures/mod-v010-broken.yaml` and `fixtures/mod-v010-fixed.yaml`
differ on exactly one line by exactly one `~`, and the selftest asserts that delta: if
someone edits one fixture without the other, the test that proves the `~` matters stops
proving it, so it fails instead.

## The prefix rules it implements

Transcribed from the engine, not remembered. Order matters — `~` is stripped first, then
`$` is checked, and only then is the remainder handed to `Platform.ResolvePath`.

| prefix | meaning | source |
|---|---|---|
| `~` | **optional.** `FileSystem.Mount` wraps the whole body in `catch when (optional)`, so *every* exception is swallowed; `MapCache` `continue`s. An absent optional mount is not a defect and this gate never reports one as an error. | `FileSystem.cs:86-88`, `:112-114`; `MapCache.cs:101-103`, `:113-119` |
| `$id` | a **mod reference**, looked up in `InstalledMods` — not a filesystem path. Resolves to `mods/<id>/mod.yaml` under the search paths `EngineDir/mods` and `EngineDir/../mods`. | `FileSystem.cs:93-102`; `Game.cs:399-401` |
| `^SupportDir` | the user's support directory. **Never present in a packaged output.** | `Platform.cs:304-305`, `:313-314` |
| `^EngineDir` | the packaged root in an installed build; `engine/` in the dev checkout. | `Platform.cs:307-308`, `:316-317`, `:247-260` |
| `^BinDir` | the directory holding the binaries. Identical to `^EngineDir` in a packaged build. | `Platform.cs:310-311`, `:319-320` |
| `name\|sub` | `sub` inside the package mounted under the explicit name `name` (the value after the colon on an earlier `Packages` line). Ordering is load-bearing: a `name` can only be used below where it is declared. | `FileSystem.cs:211-216`, `:226-232` |

Those three are the **only** caret names `ResolvePath` knows. Anything else starting with
`^` is left in the string verbatim and then fails to open as a relative path — silently,
if the entry is optional. The gate reports a stray `^Name` as `unresolvable`.

### The one asymmetry worth knowing

A non-optional `^SupportDir|…` is **fatal in `Packages` and survivable in `MapFolders`**.
`MapCache` creates a missing support-dir path before opening it
(`MapCache.cs:105-108`); `FileSystem.Mount` has no such hack. The gate treats the two
sections differently on purpose, and this is the most likely place for it to be wrong in
the *too-lenient* direction if that hack ever moves.

## What this does NOT check

- **`Rules:`, `Sequences:`, `Chrome:`, `Weapons:`, `FluentMessages:` and the rest of the
  manifest's file lists.** This gate checks *mounts*, not the files resolved through them.
  A `ww3mod|rules/gone.yaml` that no longer exists is a different bug and this will not see
  it.
- **Anything inside a `.mix`.** All of them are optional, user-supplied Red Alert content
  and are absent from every packaged output by design.
- **Whether the game actually starts.** That is Guard 1, in
  `.github/workflows/packaging.yml`, and it can only run in CI. In particular: a packaged
  build on a machine with **no Red Alert content cannot reach the main menu at all** —
  `mods/ww3mod/cursors.yaml` declares `mouse.shp`, which is in none of the shipped trees
  (it lives in RA's `local.mix`), and `CursorProvider` builds every `CursorSequence`
  eagerly in its constructor (`CursorSequence.cs:35`), so mod load throws
  `FileNotFoundException`. Any launch smoke test must install content first.
- **Case sensitivity.** A mount written `ww3mod|Bits` resolves on Windows and macOS and
  dies on Linux. Neither this gate nor the zip listing it reads can tell you that, because
  the check is `exists`, not `exists with this exact casing`.
- **The predicted tree, when you use it.** `check` with no `--tree`/`--zip` builds a
  *model* of the packaged root from `mod.config`'s `PACKAGING_COPY_ENGINE_FILES`,
  `engine/packaging/functions.sh:65-74` and `packaging/linux/buildpackage.sh:74-82`. It is
  right only for as long as those scripts say what they say today, and it prints a `NOTE`
  saying so on every run. The `--tree`/`--zip` forms are the ones that settle an argument.
