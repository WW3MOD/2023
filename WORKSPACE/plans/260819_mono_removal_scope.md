# Costed scope: removing `RUNTIME=mono` support

**Written 2026-08-19, branch `wt/gate-finish`, researched against `main @ 4f5123dc`.**
Follow-up to `WORKSPACE/bugs/discovered.md:85` (the `Linux (mono)` CI job deletion) and to
`e5f03d73` (analyzer gate to 10/10).

This is a **plan, not an execution**. Nothing in this document has been applied.

---

## Verdict

**Do not do the full removal as a unit of work. Do the two-line honesty fix (Tier 0) and stop there
unless someone has a concrete reason to go further.**

The reason is that the removal's *payoff has already been banked*. The only thing mono's existence
was actively costing this repo was three CA rules held at `severity = none` in the shared
`engine/.editorconfig` — and two of those (`CA1845`, `CA2263`) were flipped to `warning` in
`e5f03d73`, which is merged. `CA1850` stays at `none` for an unrelated, still-valid reason
(its TODO gates on ".NET 7 or later" and `global.json` pins `6.0.428`).

With that banked, deleting the remaining mono plumbing buys **no coverage, no correctness and no
performance**. It buys tidiness, against a real cost in vendored-engine delta. That is a bad trade
at full scope and a good one at Tier 0.

What *is* worth fixing immediately is that the build system still advertises a mode that has not
worked since 2026-08-11.

---

## Established facts (each measured, not inferred)

| Claim | How it was checked |
|---|---|
| `make RUNTIME=mono all` is already broken | 3× `CS0117` on `Convert.ToHexString`, diagnosed in `WORKSPACE/bugs/discovered.md:93-101`. Call sites still live at `engine/OpenRA.Game/Network/BuildFingerprint.cs:331,393` and `Graphics/SequenceIntegrity.cs:91` |
| The `Mono` MSBuild conditions are inert on every real lane | `engine/bin/OpenRA.dll` carries `.NETCoreApp,Version=v6.0`, i.e. `Directory.Build.props:22` (`!='Mono'`) is the branch taken. `-getProperty` was unavailable to confirm directly — MSBuild 17.3 predates it — so this is read off the produced assembly |
| No live workflow uses mono | No `.github/workflows/*.yml` at repo root mentions `RUNTIME` at all. `Makefile:67` defaults `RUNTIME ?= net6` |
| `engine/.github/workflows/` never runs | GitHub Actions reads workflows only from the repository root; that tree is vendored upstream |
| Root macOS packaging already dropped the mono variant | `packaging/macos/buildpackage.sh:129-130` says so in comment, and both `install_assemblies` calls at `:137-138` pass the literal `"net6"` |

### Consumer map for the `MONO`-named variables — this is the part that needed human eyes

The flag on `mod.config:78,133-143` was well placed. **These variables are not uniformly dead, and
their names do not predict their function.** Measured by grep across `*.sh`, `*.config`, `*.yml`, `Makefile`:

| Variable | Defined | Consumers |
|---|---|---|
| `PACKAGING_OSX_MONO_TAG` | `mod.config:78` | only `mod.config:134` |
| `PACKAGING_OSX_MONO_SOURCE` | `:134` | **none** |
| `PACKAGING_OSX_MONO_TEMP_ARCHIVE_NAME` | `:137` | **none** |
| `PACKAGING_APPIMAGE_DEPENDENCIES_TAG` | `:99` | only `:140` |
| `PACKAGING_APPIMAGE_DEPENDENCIES_SOURCE` | `:140` | **none** |
| `PACKAGING_APPIMAGE_DEPENDENCIES_TEMP_ARCHIVE_NAME` | `:143` | **LIVE** — `packaging/linux/buildpackage.sh:153` |
| `WHITELISTED_CORE_ASSEMBLIES` | `:153` | **none** |

Two traps in that table:

1. **`PACKAGING_APPIMAGE_DEPENDENCIES_TEMP_ARCHIVE_NAME` is referenced by the Linux AppImage build**,
   in the cleanup `rm -rf appimagetool-x86_64.AppImage "${...}" "${APPDIR}"`. The script runs under
   `set -e`. It is vestigial in *substance* — that archive is never downloaded; `:93-97` fetches only
   `appimagetool` — but it is live in *text*. Deleting the variable and nothing else leaves
   `rm -rf ... "" ...`. I tested that specific shape: `rm -rf` tolerates an empty operand and exits 0,
   so it would not actually break the build. The correct edit still removes the operand from `:153`
   rather than leaving an empty string there.
2. **None of these appear in any `require_variables` call** (`packaging/{linux,macos,windows}/buildpackage.sh`
   at `:38`, `:56`, `:38`). So deleting them does not trip the fail-fast guard. That cuts both ways:
   it makes deletion safe, and it means nothing would have told you if you got it wrong.

---

## Surface inventory

### Tier 0 — the honesty fix (recommended)

`Makefile:6-7` and `engine/Makefile:6-7,177-178` tell the reader:

```
# to compile using Mono (version 6.4 or greater) instead of .NET 6, run:
#   make RUNTIME=mono
```

That instruction has been false since 2026-08-11. Anyone following it gets three `CS0117` and no
explanation. Mark it broken/unsupported in place, pointing at `WORKSPACE/bugs/discovered.md:85`.

**Cost: 2 files, ~4 lines, no build impact, no risk.** Captures essentially all the available value.

### Tier 1 — SDK-level removal (ours to change freely)

| File | Lines |
|---|---|
| `Makefile` | 6-7, 10, 22, 25, 67, 155, 158-161, 169, 194-196 |
| `mod.config` | 78, 99, 134, 137, 140, 143, 153 |
| `packaging/functions.sh` | 14, 26-31 |
| `packaging/linux/buildpackage.sh` | 153 (drop the now-orphaned operand) |
| `launch-game.sh` | 4-5, 48-49 |
| `launch-dedicated.sh` | 8-9, 45-46 |
| `utility.sh` | 9-10, 55-56 |

**Cost: 7 files.** Mechanical, but two judgement calls hide in it:

- The launcher scripts' preflight is `if ! command -v mono; then command -v dotnet || error`. Dropping
  mono tightens the requirement to dotnet-only. On a machine with mono and no dotnet the behaviour
  changes from "passes preflight, fails confusingly later" to "fails immediately with a clear message" —
  an improvement, but a **user-visible** one, so it belongs in a commit message.
- `Makefile:67`'s `RUNTIME ?= net6` and the `RUNTIME` plumbing at `:155` can either be deleted outright
  or kept as a vestigial always-net6 knob. Deleting is cleaner; keeping avoids breaking any external
  script that passes `RUNTIME=net6` explicitly. **Recommend keeping the variable, deleting the branches.**

**Verification for Tier 1:** `make all` + `make check` + `dotnet test`. Packaging is *not* exercised by
any of those, so the `mod.config` and `packaging/` edits would ship unverified unless someone runs
`packaging/linux/buildpackage.sh` by hand. That is the single largest untested surface in this tier.

### Tier 2 — `engine/` removal (do not do this now)

| File | Lines |
|---|---|
| `engine/Makefile` | 6-7, 53, 94-95, 105, 114, 177-178 |
| `engine/Directory.Build.props` | 22-23, 61-63 |
| `engine/packaging/functions.sh` | 14, 40, 134 |
| `engine/launch-game.sh` | 5 |
| `engine/launch-dedicated.sh` | 12 |
| `engine/configure-system-libraries.sh` | 19 |
| `engine/packaging/macos/buildpackage.sh` | 80, 117, 130-131, 196, 211 |
| `engine/packaging/macos/apphost-mono.c`, `checkmono.c` | whole files |

**Cost: 8 files + 2 deletions.** Recommend **not** doing this, for three reasons:

1. **It is pure delta against vendored upstream.** `engine/` is OpenRA `release-20230225`. The repo
   already carries ~264 modified C# files; adding build-system divergence for zero functional gain
   makes the next engine re-vendor worse for nothing.
2. **`Directory.Build.props:22-23` is load-bearing documentation.** The `netstandard2.1` switch is
   *why* `Convert.ToHexString` fails under mono. Delete it and the diagnosis in
   `WORKSPACE/bugs/discovered.md` stops being checkable against the code it describes.
3. **`engine/packaging/macos/buildpackage.sh` is unreachable** (vendored workflow, never run by
   Actions). Unreachable code is not urgent; unreachable code you edit is a merge conflict you
   scheduled for later.

---

## If mono is ever wanted back

The cost estimate is unchanged and still accurate: `WORKSPACE/bugs/discovered.md:93-109`. Short form —
a shared hex helper plus one scoped `CA1872` suppression fixes the three `CS0117`, and it *still*
would not make the lane green, because mono then hits the same `-warnaserror` analyzer build as
everyone else. Sequence the analyzer burn-down first.

Note also that `e5f03d73` moved `engine/OpenRA.WindowsLauncher/Program.cs:89` to
`Environment.ProcessPath`, which is .NET 6+ and absent from `netstandard2.1`. **Restoring mono now
costs one more call site than the estimate above states.**

---

## What I did not verify

- **I did not run the packaging scripts.** Every claim about `packaging/` is from reading, plus the
  isolated `rm -rf ""` shell test. Tier 1's packaging edits are the untested part of this plan.
- **I did not build with the Tier 1 or Tier 2 edits applied** — nothing was applied. The "inert
  conditions" claim rests on the produced assembly's TFM, not on a before/after binary diff.
- **`MSBuildRuntimeType` was inferred from output, not read directly**, because MSBuild 17.3 has no
  `-getProperty`. A determined check would use a custom target that prints the property.
</content>
</invoke>
