# .NET 6 → .NET 10: costed decision

**Researched against `main @ 708b5f70`, 2026-08-17.** Read-only; no engine, project or CI file was
modified to produce this. Only SDK 6.0.428 is installed on this machine, so **nothing here was
compiled against a newer SDK** — every number is labelled MEASURED (by someone, at some point,
with the evidence cited) or ESTIMATED or UNKNOWN.

---

## Recommendation

**Target .NET 10. Do not target .NET 8. Run the decisive desync experiment first, then bump —
and treat the bump as a determinism *fix*, not a determinism *risk*.**

Two facts drive that, and both invert the obvious framing:

1. **.NET 8 is EOL on 2026-11-10 — 85 days away.** .NET 9 dies the same day. "Upgrade to the
   safe LTS" buys twelve weeks and then repeats this exercise. .NET 10 LTS runs to Nov 2028
   (820 days). There is no conservative middle option; there is .NET 10 or there is staying.
2. **`RollForward: Major` means the TFM does not control what runtime players execute on.**
   `engine/Directory.Build.props:26`. A player with only .NET 10 installed already runs the game
   on .NET 10 today. The 2026-08-16 two-human desync had **host on CLR 8.0.27 and friend on
   10.0.10** — a heterogeneous-runtime match, under the current net6 target. Cross-runtime
   exposure is not something the upgrade would introduce; it is live now, and bumping the TFM
   to `net10.0` is the one lever that raises the floor and makes the fleet homogeneous.

So the question "can the upgrade move the simulation?" has an uncomfortable inversion: **staying
on net6 is what currently permits three different runtimes in one match.**

---

## The decisive question: can the upgrade move the simulation?

**Honest answer: I cannot rule it out statically — but the surface is enumerable and small, and
one third of it has already been measured clean.**

### What is structurally immune (high confidence, established by reading the mechanism)

| Mechanism | Evidence | Verdict |
|---|---|---|
| **Sync hash input types** | `Sync.EmitSyncOpcodes` (`engine/OpenRA.Game/Sync.cs:58-75`) accepts **only** `int`, `bool`, and eleven registered types (`int2 CPos CVec WDist WPos WVec WAngle WRot Actor Player Target`). Anything else throws `NotImplementedException` at IL-generation time. | **No string, float or double can ever be `[Sync]`.** Not by convention — by construction. |
| **Hash stability** | All eleven registered types hash as XORs of `int.GetHashCode()`, which returns the value itself (`WPos.cs:79`, `WVec.cs:110`, `WAngle.cs:62`, `WRot.cs:188`, `WDist.cs:96`, `CPos.cs:57`); `Actor.GetHashCode` is `(int)ActorID` (`Actor.cs:405`). | No risk. `string.GetHashCode` randomisation is structurally excluded. |
| **Actor iteration order** | `World.actors` is a `SortedDictionary<uint, Actor>` (`World.cs:32`); `TraitDictionary` keeps parallel `List<Actor>`/`List<T>` insertion-sorted by ActorID via `BinarySearchMany` (`TraitDictionary.cs:22-35,150-154`). | Runtime-independent. Unordered-collection enumeration order never reaches the core loop. |
| **Simulation trigonometry** | `WAngle.Cos()` is an **integer lookup table** (`engine/OpenRA.Game/WAngle.cs:69-77`, `CosineTable`); `ArcSin`/`ArcCos` index the same table (`:103-122`). | No floating point, no libm. |
| **RNG** | Sim uses `MersenneTwister` (`Support/MersenneTwister.cs`), pure `uint` arithmetic. `System.Random` appears only in `DiscordService.cs:133,206` and `MasterServerPinger.cs:89` — cosmetic / server discovery. | No risk. |
| **Transcendental functions** | Grepped all 30 `Math.{Sin,Cos,Tan,Atan2,Pow,Exp,Log}` / `MathF.*` hits in the engine. **Every one is rendering, UI, map-editor, or an `IOrderGenerator`** (`SelectDirectionalTarget.cs:22` — client-side, result travels as a networked order, so computed once not twice). | **The single best finding here.** libm-backed transcendentals are the class of float that genuinely varies between runtime versions and OSes — and the simulation does not use them. |

### What remains genuinely at risk

Four floating-point sites on synced paths. All four use **only** `+ - * /`, casts, `Math.Round`
and `Math.Sqrt` — every one of which is IEEE-754 correctly-rounded and spec-mandated, and RyuJIT
does not perform automatic FMA contraction. So the theoretical mechanism is narrow. Narrow is not
none, and I cannot close it by reading.

| Site | Feeds | Probed? |
|---|---|---|
| `CohesionIntentMath` + `CohesionLayoutMath` (via `CohesionMoveModifier.ModifyGroupOrder`, `:1008`) | destination cells, on every client | **MEASURED byte-identical**, .NET 8.0.30 vs 10.0.11: 400k sweep cases + 896 exact rounding-boundary hits (`tools/fp-determinism/README.md`). macOS x64 only. |
| `DamageWarhead.cs:115-118` and `:151-193` | **`Health`, which IS `[Sync]`** | **No.** The most alarming gap — shortest path from float to sync hash. |
| `BallisticMissileFly.cs:64-79` | projectile impact position | **No.** WW3MOD-added. |
| `Aircraft.cs:440` — `(int)Math.Sqrt(2.0 * MaxAcceleration * distance)` | aircraft movement | **No.** |

**The amplifier that makes this worse than it looks:** `Mobile.CurrentSpeed`, `MovePart.progress`
and `Actor.CurrentActivity` carry **no `[Sync]`** (`MoveAccelerationMath.cs:1-22`;
`DISCOVERIES.md:2228`). Float drift is therefore invisible to the desync detector until it flips a
cell transition — you get a desync report pointing at a position, long after the divergence.

### Two lesser findings

- **Culture:** `FieldLoader.cs:300-301` and `:334-335` call `int.TryParse` **without**
  `NumberFormatInfo.InvariantInfo` (int2/CPos array fields); the rest of `FieldLoader` is correct
  (`:135,142,149,156`). No `CultureInfo.DefaultThreadCurrentCulture` is set at startup and
  `InvariantGlobalization` is not set in `Directory.Build.props`. This is a cross-*machine locale*
  hazard more than a cross-*runtime* one, but .NET 5+ moved to ICU and the two interact.
- **One non-total sort:** `AdaptiveProductionBotModule.cs:305` sorts by `Priority` with ties
  unresolved, and `List.Sort` is unstable. Bots are host-only (`Player.cs:224-232`), so this
  **cannot** break 2-human lockstep — but it can break replay and A-B benchmark byte-identity
  across runtimes, which is how this project measures everything.

### What this converts the recommendation into

**Upgrade behind a byte-identity harness — and the harness already exists.**
`tools/fp-determinism/` references the *shipped* DLL rather than reimplementing the math, and has a
proven-red sensitivity check (`--perturb N`, one ULP). Extending it to the three unprobed kernels is
the acceptance test for this upgrade. That is a small, well-precedented piece of work, not a research
project.

---

## Cost lines

### 1. Which target

| | Support ends | Verdict |
|---|---|---|
| **Stay on net6** | **2024-11-12 — 21 months ago** | No security servicing. Already the status quo. |
| **net8 (LTS)** | **2026-11-10 — 85 days** | Rejected. Same work, twelve weeks of runway. |
| **net9 (STS)** | 2026-11-10 | Rejected, same date. |
| **net10 (LTS)** | **~2028-11-14 — 820 days** | **Pick this.** |

### 2. The Mono / netstandard2.1 lane: **delete it**

My prior hypothesis — that `LangVersion 9` is a cap protecting the Mono lane — is **disproven.**
`git log -S LangVersion` shows the upstream import `7362fbc6` carried `LangVersion 7.3`, and WW3MOD
**raised** it in `c4f0739e` *"Upgrade C# language version from 7.3 to 9 (matching upstream)"*.
Nothing in that commit mentions Mono. Corroborating: the engine contains **no `record`, no `init`
accessor and no `IsExternalInit` polyfill**, so no netstandard-incompatible C# 9+ feature is in use
and `LangVersion` is not currently blocked by anything. **Deleting Mono does not unblock
`LangVersion`.** What it unblocks is the netstandard2.1 *BCL ceiling* — which has already bitten.

Delete it anyway, on four independent grounds:

1. **It ships zero bytes.** All seven packaging call sites pass the literal `"net6"`:
   `packaging/{linux,macos,windows}/buildpackage.sh` and `engine/packaging/…`. `packaging/functions.sh:26-31`
   has a live `RUNTIME = mono` branch that **no caller ever reaches**.
2. **It has been red since 2026-08-11** — three `CS0117` on `Convert.ToHexString`
   (`BuildFingerprint.cs:246,308`, `SequenceIntegrity.cs:91`), which is net5.0+ and not on the
   netstandard2.1 surface. CI run `31997060463`, fails at 36s. Six days untouched.
3. **Its own bug entry says fixing it needs the analyzer burn-down first**
   (`WORKSPACE/bugs/discovered.md:6-28`) — and the naive rewrite trades 3 Mono errors for 3
   `CA1872` analyzer errors *on every platform*, because `engine/.editorconfig:943` sets that rule
   to `warning` and `check` builds `-warnaserror`.
4. **Upstream is already waiting on it.** `engine/.editorconfig` carries
   `CA2263.severity = none # TODO: Change to warning once mono is dropped` and
   `CA1850.severity = none # TODO: … AND once supported by mono`.

Scope of the deletion: `Directory.Build.props:23` and the Mono conditionals at `:62-63`;
`OpenRA.Game.csproj:5-11`; the `RUNTIME=mono` branches in `Makefile` and `engine/Makefile`; the
`linux-mono` job at `.github/workflows/ci.yml:40-62`. **Needs a human eye:** `mod.config:78,133-143`
(`PACKAGING_OSX_MONO_SOURCE`, `PACKAGING_APPIMAGE_DEPENDENCIES_SOURCE`) are referenced only by the
unreachable branch — but whether out-of-repo tooling fetches them was not determinable by reading.

### 3. New analyzer findings: **ESTIMATE 5–40, central ~15**

The mechanism: **`AnalysisLevel` is set nowhere** in any `.props`/`.csproj`/`.targets`/`.editorconfig`,
so it floats with the TFM. `net6.0` → level 6.0; `net10.0` → level 10.0, auto-enabling every CA rule
added in 7, 8, 9 and 10.

**The one MEASURED anchor:** at commit `8656bd3c`, `make check` reported **106 errors on the CI
runner vs 100 locally on 6.0.428** — a delta of exactly **6** (`CA1862 ×5` + `IDE0251 ×1`), across
**320 KLOC / 1878 `.cs` files**. That is **0.019 findings per KLOC** for one major-version step.
Crucially, **those 6 were already burned down** in `a7142780`, verified by temporarily pinning
`Microsoft.CodeAnalysis.NetAnalyzers 8.0.0`. The tree today is already clean against a .NET 8
analyzer pack. **UNKNOWN:** the runner's actual SDK is recorded nowhere; the diagnostics bound it at
**≥ 8.0**, so the 6-error delta is a net6→net8 measurement and does not generalise to 10 unassisted.

Three large de-riskers, all verified:
- **StyleCop 1.2.0-beta.435 and Roslynator 4.2.0 are pinned by `PackageReference`**
  (`Directory.Build.props:59-63`). A TFM bump cannot change `SA*`/`RCS*` — that removes 90 of the
  original 106 from upgrade risk entirely.
- **`engine/.editorconfig` (1433 lines, 414 `dotnet_diagnostic.*.severity` entries) came from upstream
  `release-20250330` and was authored against a .NET 9-era SDK.** It already pre-neutralises the
  dangerous new rules: `CA1863-1870` → `suggestion`, `IDE0300-0305` → `silent`.
- **`LangVersion 9` blocks the entire collection-expression / primary-constructor class**
  (`IDE0290`, `IDE0300-0305`, `IDE0330` are C# 12/13-gated) — the largest theoretical source.
- **Analyzers only bite `make check`** — `Directory.Build.props:51-56` strips all analyzers on
  Release, and `EnforceCodeStyleInBuild` is Debug-only (`:35-44`). Packaging is unaffected.

**One guaranteed, dated hit:** `UnloadCargo.cs:108` — `CA2021`, the proven false positive, is guarded
by **a comment only**, no `#pragma` and no `SuppressMessage`, while `.editorconfig` sets
`CA2021.severity = warning`. It *will* break the build the day the SDK moves. Converting the comment
guard to a scoped `#pragma warning disable CA2021` carrying the existing rationale is a ten-minute
prerequisite, and should be done regardless of whether the upgrade proceeds.

### 4. What the upgrade does to the SDK pin (`c1f7e697`)

It does **not** make it unnecessary — it makes it *more* valuable, and updating it is one line.
`global.json` pins `6.0.428` + `rollForward: latestFeature`. A `net10.0` TFM **requires** editing it,
because 6.0.428 cannot compile net10.0. It becomes `"version": "10.0.x"`. Every reason the pin exists
survives verbatim: NetAnalyzers and IDE rules still come from the SDK and still have no standalone
package covering both, and `latestFeature` still cannot cross a major boundary (verified in that
commit, exit 145). The bump also **erases the pin's stated cost** — its own message concedes it
"freezes analyzer coverage at an EOL pack"; moving to a supported band gives that coverage back.

### 5. Effort and blast radius

**Mechanical (~11 files, ~30 lines).** 10 of the 11 `.csproj` inherit the TFM and need no edit.
- `engine/Directory.Build.props:22,23,26` — TFM, drop the Mono branch, keep/revisit `RollForward`
- `global.json:3` — `6.0.428` → `10.0.x`
- `tools/fp-determinism/FpDeterminism.csproj:5` — the one hardcoded TFM (it sits outside `engine/`
  so it inherits nothing)
- `engine/OpenRA.Game/OpenRA.Game.csproj:6,10,17` — `Microsoft.Extensions.DependencyModel` 6.0.2,
  `System.Collections.Immutable` 6.0.0, `System.Threading.Channels` 6.0.0 → 10.0.x
- 7 × `install_assemblies`/`install_mod_assemblies "net6"` in the packaging scripts
- `Makefile:10,22,25,67`; `engine/Makefile:10,13,16,19,59,181,184,187,190,194`
- No `*.runtimeconfig.json` is tracked — they regenerate from `Directory.Build.props`.

**Needs human judgement.** (a) Mono deletion scope, incl. the `mod.config` packaging sources.
(b) The analyzer burn-down — unbounded until measured. (c) The `CA2021` pragma. (d) The determinism
acceptance test: extending `tools/fp-determinism/` to `DamageWarhead`, `BallisticMissileFly` and
`Aircraft.CalculateAccelerationToWaypoint`.

---

## Sequencing

1. **Run the decisive desync experiment first — it is free and it may reprice everything.**
   `WORKSPACE/RESUME-260816.md:47` already names it: replay one desyncing match on *one machine*
   under *both* runtimes with `Test.ForceSyncReports=true`, and diff the `syncdiag-*` files. If it
   goes red, runtime heterogeneity is the desync cause, the net10 bump is the **fix**, and it ships
   immediately. If green, the bump is determinism-neutral and gets scheduled on its own merits.
   Either way you learn this before changing the variable you are currently debugging.
2. Convert the `CA2021` comment guard to a scoped `#pragma`. Ten minutes, independently correct.
3. Measure the CA\* half of the analyzer delta **without installing anything** (see below).
4. Delete the Mono lane. That is most of the upgrade, and it is separable and independently useful.
5. Extend `tools/fp-determinism/` to the three unprobed kernels.
6. Bump TFM + `global.json`, burn down whatever the analyzers say.

## What I could not measure, and the one cheap unblock

Only SDK 6.0.428 is installed and I was instructed not to install another, so **no number in §3 above
the measured 6 was compiled.** Specifically unmeasured: the true net10 analyzer count.

**Half of it is measurable today with no SDK install** — precedent exists, `a7142780` did exactly this
with version 8.0.0. Add to `engine/Directory.Build.props`, run, revert:

```xml
<PackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers" Version="10.0.0" PrivateAssets="All" />
<AnalysisLevel>10.0</AnalysisLevel>
```
```bash
make check 2>&1 | grep -oE '(error|warning) (CA|IDE)[0-9]+' | sort | uniq -c   # read exit code separately
```

**The `IDE*` half cannot be measured this way** — those rules ship inside the SDK's own Roslyn and
have no supported standalone package. Getting a real number there requires installing SDK 10.0.x,
setting `global.json` to it and the TFM to `net10.0`. That is a cheap, reversible machine change
(side-by-side install; `global.json` keeps the existing pin authoritative until deliberately moved),
and it is the only thing standing between this document and a measured figure.
