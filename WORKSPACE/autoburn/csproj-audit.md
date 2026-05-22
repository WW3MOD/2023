# csproj / targets / props audit

Branch: `auto/csproj-audit` · Date: 2026-05-21 · Scope: report-only.

Audit of every `.csproj`, `.targets`, `.props` under `engine/` for target-framework
consistency, language version, nullability, and SDK pins, plus a cross-check
against the build scripts and the CLAUDE.md claim that "engine targets net6 but
runs on .NET 8+".

## TL;DR

Everything is **internally consistent on net6.0**: there is no per-project
override anywhere. The "runs on .NET 8+" claim in CLAUDE.md refers to the
*forward-compatible runtime story* (a binary compiled against net6.0 can be
loaded by a .NET 8 host via roll-forward), not to any project targeting net8.
No file in the engine asks for net8.

The only inconsistency worth a flag is between **CLAUDE.md (claims net8+ runs
fine)** and the **packaging scripts (hardcode `"net6"` for every install
target)** — a packaged build will still install net6 binaries and refuse to
roll forward unless the user's machine has a net6 runtime. The locally installed
SDK is **6.0.428 only** (no net8 available), so roll-forward is currently
untested on this dev box.

## Per-project matrix

All projects are SDK-style (`<Project Sdk="Microsoft.NET.Sdk">`) and inherit
properties from `engine/Directory.Build.props`. Only deltas vs the props file
are shown.

| Project                                    | TargetFramework (effective)           | OutputType | Notes                                                          |
|--------------------------------------------|---------------------------------------|------------|----------------------------------------------------------------|
| `OpenRA.Game`                              | net6.0 / netstandard2.1 (Mono)        | Library    | Inherits TF. Conditional package refs for Mono vs non-Mono.    |
| `OpenRA.Mods.Common`                       | net6.0 / netstandard2.1 (Mono)        | Library    | Inherits TF.                                                   |
| `OpenRA.Mods.Cnc`                          | net6.0 / netstandard2.1 (Mono)        | Library    | Inherits TF. `IsPublishable` gated by `$(CopyCncDll)`.         |
| `OpenRA.Mods.D2k`                          | net6.0 / netstandard2.1 (Mono)        | Library    | Inherits TF. `IsPublishable` gated by `$(CopyD2kDll)`.         |
| `OpenRA.Platforms.Default`                 | net6.0 / netstandard2.1 (Mono)        | Library    | Inherits TF.                                                   |
| `OpenRA.Server`                            | net6.0 / netstandard2.1 (Mono)        | Exe        | Inherits TF. Trimmer roots set.                                |
| `OpenRA.Launcher`                          | net6.0 / netstandard2.1 (Mono)        | Exe        | Inherits TF. Server-GC removed (see in-file comment).          |
| `OpenRA.Utility`                           | net6.0 / netstandard2.1 (Mono)        | Exe        | Inherits TF. Trimmer roots set.                                |
| `OpenRA.WindowsLauncher`                   | net6.0 / netstandard2.1 (Mono)        | winexe     | Inherits TF.                                                   |
| `OpenRA.Test`                              | net6.0 / netstandard2.1 (Mono)        | Library    | Inherits TF. Test SDK 17.10.0, NUnit 3.13.3.                   |

### Shared properties (from `engine/Directory.Build.props`)

| Setting                                | Value                                                                 |
|----------------------------------------|----------------------------------------------------------------------|
| `<TargetFramework>` (non-Mono)         | `net6.0`                                                              |
| `<TargetFramework>` (Mono)             | `netstandard2.1`                                                      |
| `<LangVersion>`                        | `9`                                                                    |
| `<Nullable>`                           | `disable`                                                              |
| `<AllowUnsafeBlocks>`                  | `true`                                                                 |
| `<Optimize>` (Release)                 | `true`                                                                 |
| `<Optimize>` (Debug)                   | `false`                                                                |
| `<EnforceCodeStyleInBuild>` (Debug)    | `true`                                                                 |
| `<GenerateDocumentationFile>` (Debug)  | `true`                                                                 |
| `<OutputPath>`                         | `$(EngineRootPath)/bin` (single shared output dir)                     |
| `<NoWarn>`                             | inherits `+ NETSDK1138` (net6 EOL warning suppression)                 |
| Analyzers                              | StyleCop 1.2.0-beta.435, Roslynator 4.2.0 (skipped on Mono)            |

No project overrides `<TargetFramework>`, `<LangVersion>`, or `<Nullable>`. The
defaults stick everywhere.

### SDK / runtime pins

- No `global.json` in repo root or `engine/`.
- No `NuGet.Config` in repo root or `engine/`.
- No per-project `<RuntimeIdentifier>` pin — `<TargetPlatform>` is computed at
  build time in `Directory.Build.props`.
- `NETSDK1138` (target-framework-out-of-support) is suppressed in `NoWarn` —
  this is the warning .NET emits because net6 reached end of support on
  2024-11-12.

### Build script assumptions

| Script                                          | Assumed framework                                  |
|-------------------------------------------------|---------------------------------------------------|
| `Makefile` (root)                               | `RUNTIME ?= net6` (default), `mono` opt-in        |
| `make.ps1` (root)                               | `dotnet build -c Release` — no TF flag, picks csproj's |
| `engine/Makefile`                               | `RUNTIME ?= net6`                                  |
| `engine/make.ps1`                               | (mirror of root, no TF pin)                        |
| `engine/packaging/linux/buildpackage.sh:70`     | hardcoded `"net6"`                                 |
| `engine/packaging/macos/buildpackage.sh:78-79`  | hardcoded `"net6"` for x86_64 and arm64            |
| `engine/packaging/windows/buildpackage.sh:67`   | hardcoded `"net6"`                                 |
| `engine/packaging/functions.sh:14`              | doc comment: "RUNTIME: Runtime type (net6, mono)" |

### Local SDK state on this dev box

```
$ dotnet --list-sdks
6.0.428 [/Users/fredrik/.dotnet/sdk]

$ dotnet --list-runtimes
Microsoft.AspNetCore.App 6.0.36
Microsoft.NETCore.App 6.0.36
```

Only the net6 SDK is installed. CLAUDE.md says the engine "runs on .NET 8+" —
in practice the dev environment currently only has net6, so the .NET 8 path is
documented but not actively used here.

## Inconsistencies found

### 1. CLAUDE.md vs reality: "runs on .NET 8+" is aspirational, not configured

CLAUDE.md (root): *"./make.ps1 all   # Full build (targets net6, but runs on .NET 8+)"*
and *"make test               # Note: requires .NET 6 runtime specifically"*.

The engine *can* roll forward to a .NET 8 runtime if one is installed (the host
loads net6 assemblies on net8 by default), but:

- No project file mentions net8.
- No `<RollForward>` policy is set (default is `Minor`, which would *not* roll
  forward across major versions — `Major` roll-forward would need to be enabled
  per-app via `runtimeconfig.json` or an env var).
- The `Microsoft.NETCore.App` runtime installed locally is 6.0.36, not 8.
- Packaging scripts ship `"net6"` runtime artifacts; nothing bundles net8.

So the "runs on .NET 8+" line is technically true only if the user (a) has a
.NET 8 host installed and (b) sets `DOTNET_ROLL_FORWARD=Major` (or equivalent).
It is **not** a configured roll-forward, and not what the build actually does.

Severity: **doc only**. Won't break anything; just be aware the prose
overpromises vs what the project actually pins.

### 2. NETSDK1138 suppression is hiding an EOL signal

`NoWarn` includes `NETSDK1138`, which is the SDK's reminder that "net6.0 is
out of support". The suppression is intentional (otherwise every build prints
the warning), but it does mean the EOL pressure isn't visible during normal
builds. Not a project-config inconsistency, just a flag for the user to
re-evaluate whether a net8 bump is overdue. See suggested migration below.

### 3. Mono path targets netstandard2.1 — fine, but worth being explicit

When built under Mono (`$(MSBuildRuntimeType)=='Mono'`), all projects switch
to `netstandard2.1`. C# 9 language features are technically supported on
netstandard2.1 via the SDK, but a couple of net6 BCL APIs that WW3MOD uses
(e.g., `System.Threading.Channels`, `System.Collections.Immutable` 6.0) are
pulled in as explicit `PackageReference`s in `OpenRA.Game` under the Mono
condition. The Mono path therefore relies on those packages being available
at runtime in the Mono BCL.

No active inconsistency, but the Mono build path is essentially legacy at this
point — `RUNTIME=mono` is the opt-in, all default flows take the
net6+dotnet path.

### 4. No `LangVersion` override anywhere — projects can't use C# 10+ features

`<LangVersion>9</LangVersion>` in `Directory.Build.props` is hard. If any
file in the engine or mod-side has started using C# 10+ syntax (file-scoped
namespaces, top-level `record struct`, etc.), it would fail to compile under
the current config. (The audit task didn't ask for a source-code grep, but
the LangVersion pin is worth knowing about.) Not an inconsistency *between*
projects — all projects agree on C# 9 — but a constraint to note.

### 5. Solution files don't reference TargetFrameworks

`WW3MOD.sln` and `engine/OpenRA.sln` contain no `TargetFramework` references —
solution files don't, that's normal. No conflict here.

## Suggested migration if you bump to net8

If/when the user wants to move to net8, the change is small and confined:

**Single source of truth — change one line:**

1. `engine/Directory.Build.props:22` — change `net6.0` to `net8.0`. All projects
   inherit, so no per-project edit needed.
2. (optional) `engine/Directory.Build.props:23` — Mono path still says
   `netstandard2.1`. Leave or update depending on whether Mono support is
   being kept.
3. `engine/Directory.Build.props:16` — remove `NETSDK1138` from `NoWarn` once
   on a supported framework.
4. `engine/Directory.Build.props:8` — consider bumping `<LangVersion>` to `12`
   (net8 default) to actually use the language features that come with the
   newer compiler.

**Packaging scripts — find/replace `"net6"` → `"net8"`:**

5. `engine/packaging/linux/buildpackage.sh:70`
6. `engine/packaging/macos/buildpackage.sh:78`
7. `engine/packaging/macos/buildpackage.sh:79`
8. `engine/packaging/windows/buildpackage.sh:67`
9. `engine/packaging/functions.sh:14` — comment update.

**Build scripts — change default RUNTIME:**

10. `Makefile:56` — `RUNTIME ?= net6` → `RUNTIME ?= net8`.
11. `engine/Makefile:59` — same.
12. Documentation pass on the `RUNTIME=net6` examples in both Makefiles
    (`engine/Makefile:7,10,13,16,19,178,181,184,187,190,194` and `Makefile:6,9,11,21,24`).

**Docs:**

13. `CLAUDE.md` "Build & Run" section — replace the "targets net6, but runs on
    .NET 8+" hedge with whatever the new reality is.

**Order to do it in:**

1. Bump `Directory.Build.props` (the source of truth). Build locally. Fix
   anything that breaks (very likely zero — C# 9 → C# 12 is additive, and the
   BCL is forward-compatible).
2. Run the test suite: `dotnet test engine/OpenRA.Test/OpenRA.Test.csproj`.
3. Run `make` end-to-end with the new default. Confirm the game launches.
4. Then update packaging scripts and Makefile defaults — these only matter
   for installer artifacts and CI.
5. Last: docs sweep.

**Risk:** very low. The biggest unknown is whether any third-party
`PackageReference` (Linguini, Lua, etc.) ships a net8-incompatible build —
but they all advertise multi-targeting and the existing `_PackageReference`
entries pin major versions that already support net8.

## Files inspected

```
./engine/Directory.Build.props
./engine/Directory.Build.targets
./engine/OpenRA.Game/OpenRA.Game.csproj
./engine/OpenRA.Mods.Common/OpenRA.Mods.Common.csproj
./engine/OpenRA.Mods.Cnc/OpenRA.Mods.Cnc.csproj
./engine/OpenRA.Mods.D2k/OpenRA.Mods.D2k.csproj
./engine/OpenRA.Platforms.Default/OpenRA.Platforms.Default.csproj
./engine/OpenRA.Server/OpenRA.Server.csproj
./engine/OpenRA.Launcher/OpenRA.Launcher.csproj
./engine/OpenRA.Utility/OpenRA.Utility.csproj
./engine/OpenRA.WindowsLauncher/OpenRA.WindowsLauncher.csproj
./engine/OpenRA.Test/OpenRA.Test.csproj
./Makefile
./make.ps1
./engine/Makefile
./engine/make.ps1
./engine/packaging/functions.sh
./engine/packaging/linux/buildpackage.sh
./engine/packaging/macos/buildpackage.sh
./engine/packaging/windows/buildpackage.sh
./mod.config
./WW3MOD.sln
./engine/OpenRA.sln
```

Plus negative checks for `global.json`, `NuGet.Config`, per-project `LangVersion`
overrides, per-project `Nullable` overrides, and per-project `TargetFramework`
overrides — none exist.
