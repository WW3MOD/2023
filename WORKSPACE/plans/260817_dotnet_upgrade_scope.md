# .NET 6 → .NET 10: costed decision

**Researched against `main @ 708b5f70`, 2026-08-17.** Read-only; no engine, project or CI file was
modified to produce this. Every number is labelled MEASURED (by someone, at some point, with the
evidence cited) or ESTIMATED or UNKNOWN.

> **Updated 2026-08-17 (branch `wt/sdk10-measure`, against `main @ 6c9e8149`).** SDK 10.0.400 is now
> installed side by side with 6.0.428 and **§3 has been recompiled rather than estimated** — the
> answer is zero new `CA*`/`IDE*`. The original caveat "nothing here was compiled against a newer
> SDK" no longer applies to §3; it still applies everywhere else. `global.json` remains authoritative
> and ordinary builds still resolve 6.0.428 (verified: `make check` exit 0 post-install).

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

### 3. New analyzer findings: **MEASURED — ZERO new `CA*`, ZERO new `IDE*`**

> **Superseded 2026-08-17 by direct measurement on SDK 10.0.400** (branch `wt/sdk10-measure`, against
> `main @ 6c9e8149`). This section previously carried an ESTIMATE of 5–40, central ~15, extrapolated
> from a net6→net8 anchor. The estimate was wrong by its whole magnitude, in the *safe* direction.
> Everything below is compiled, not reasoned.

**Headline: the analyzer half of this upgrade costs nothing.** Not "little" — nothing. A full engine
Debug build at `AnalysisLevel` 10.0 produces **0 `CA*` and 0 `IDE*` diagnostics** on WW3MOD source.
One rule that fires today (`IDE0220`, `StancePositioningFireStanceTest.cs:187`) *stops* firing, so
the strict delta is **−1**.

What the bump *does* surface is **6 source diagnostics in 4 files, forming 2 mechanical classes
rather than 6 problems** — none of them `CA*`/`IDE*`, all of them BCL obsoletions the estimate never
considered.

Deduped by `file:line:col:rule`, engine Debug build, net6.0/SDK 6.0.428 → net10.0/SDK 10.0.400:

| Rule | net6 | net10 | Δ | Class |
|---|---|---|---|---|
| `CA*` (any) | 0 | 0 | **0** | — |
| `IDE*` (8 solution projects) | 0 | 0 | **0** | — |
| `IDE0220` (`OpenRA.Test`) | 1 | 0 | **−1** | rule stops firing |
| `SYSLIB0051` + `CS0672` | 0 | 4 | +4 | mechanical, class A |
| `SYSLIB0050` | 0 | 2 | +2 | mechanical, class B |
| `NU1510` | 0 | 3 | +3 | mechanical, restore-level |
| `NU1902` | 0 | 1 | +1 | real, supply-chain |

**Class A — legacy exception binary serialization** (`SYSLIB0051`, plus `CS0672` "overrides obsolete
member" as its shadow, so one site yields two diagnostics). `engine/OpenRA.Game/FieldLoader.cs:51-53`
(`MissingFieldsException`) and `engine/OpenRA.Utility/Program.cs:32-34` (`NoSuchCommandException`).
Both are `public override void GetObjectData(SerializationInfo, StreamingContext)`. Nothing in the
engine serializes an exception across a remoting or AppDomain boundary; the fix is to **delete the
override**, ~12 lines total.

**Class B — `FormatterServices.GetUninitializedObject`** (`SYSLIB0050`).
`engine/OpenRA.Game/Map/ActorReference.cs:73` and
`engine/OpenRA.Mods.Common/Scripting/Global/ActorGlobal.cs:39`. The supported replacement is
`RuntimeHelpers.GetUninitializedObject`, identical semantics, available since .NET 5. One line each.

**Zero real defects. No finding implies a behavioural change.**

**What actually stops the build first is not analyzers — it is restore.** With `-warnaserror` the
gate fails in **4 seconds with 6 errors, before a single `.cs` file is compiled**, all of them NuGet:
- `NU1510` ×3 — `Microsoft.Win32.Registry`, `System.Runtime.Loader`, `System.Threading.Channels` are
  in the shared framework now; the `PackageReference`s must be deleted. (New .NET 10 SDK pruning
  diagnostic.)
- `NU1902` ×1 — `NuGet.CommandLine 4.4.1`, moderate advisory GHSA-3885-8gqc-3wpf, pulled
  **transitively by `NUnit.Console 3.16.3`** in `OpenRA.Test`. `NuGetAudit` is off on 6.0.428 and
  default-on from SDK 8, so this is an **SDK** effect, not a TFM effect — it appears on any newer SDK
  regardless of target. It is the only finding in the whole set with real-world content.

**The de-riskers held, and they are why the number is zero.** All four re-verified: StyleCop
1.2.0-beta.435 / Roslynator 4.2.0 pinned by `PackageReference` (`Directory.Build.props:59-63`) so
`SA*`/`RCS*` cannot move; `engine/.editorconfig` (414 severity entries, authored against a .NET 9-era
upstream) pre-neutralises `CA1863-1870` → `suggestion` and `IDE0300-0305` → `silent`; `LangVersion 9`
gates out the whole collection-expression / primary-constructor class; analyzers are stripped on
Release (`:51-56`), so packaging is untouched. **The `.editorconfig` pre-neutralisation is doing real
work and must not be "cleaned up" ahead of the bump** — a WW3MOD block at `:1265+` additionally
downgrades 28 `IDE*` rules to `suggestion`; 42 remain at `warning`, and none of them fire.

**CORRECTION — `CA2021` at `UnloadCargo.cs:108` does NOT fire under SDK 10; the "guaranteed, dated
hit" is withdrawn.** It was the one dated certainty in the previous draft, and it is wrong: Roslyn's
`CA2021` now handles the `.Cast<(T,U)?>()` shape correctly. **This is not analyzer silence**, which
was ruled out with a RED control — a probe file in the *same project and same compilation*, holding a
genuine `List<string>.Cast<int>()`, produced `warning CA2021`, alongside `CA1862` (a .NET 8-era
NetAnalyzers rule) and `IDE0034` (an SDK-Roslyn IDE rule). Both halves of the pipeline are therefore
demonstrably live at level 10, and the real site is simply no longer flagged. **No `#pragma` is
needed and none was applied.** If it ever regresses the correct suppression is a scoped
`#pragma warning disable CA2021` around the `return`, carrying the existing comment as its rationale
— never deleting the cast, which would make a fully blocked transport stop reporting itself blocked.

**Coverage correction that affects every analyzer count ever quoted for this repo:** `OpenRA.Test` is
**not built** by `dotnet build` of `engine/OpenRA.sln`. Its solution entry carries `ActiveCfg` but
**no `.Build.0` line**, unlike every other project — so `make check` never compiles it, and its ~50
existing `SA*`/`RCS*`/`IDE*` violations gate nothing. It was measured here by building the `.csproj`
directly; its net6→net10 delta is also zero new `CA*`/`IDE*`.

**Reproduction recipe.** Measure *without* `-warnaserror`, or an early project failure hides
everything downstream — that is exactly why the gate's own output undercounts:
```bash
cd engine && rm -rf ./bin ./*/obj
dotnet build -c Debug -nologo --no-incremental -p:TargetPlatform=osx-x64 \
  -p:EnforceCodeStyleInBuild=true -p:GenerateDocumentationFile=true > /tmp/m.log 2>&1
echo "EXIT=$?"   # read directly, never through a pipe
dotnet build OpenRA.Test/OpenRA.Test.csproj -c Debug --no-incremental ...   # separately; the sln skips it
grep -oE '/[^ ]+\.cs\([0-9]+,[0-9]+\): (warning|error) [A-Z]+[0-9]+' /tmp/m.log | sort -u
```

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

**Also mechanical, added by the 2026-08-17 measurement** (§3): delete 3 now-in-framework
`PackageReference`s (`NU1510`), delete 2 obsolete `GetObjectData` overrides, swap 2 calls to
`RuntimeHelpers.GetUninitializedObject`. Roughly 20 further lines, no judgement required.

**Needs human judgement.** (a) Mono deletion scope, incl. the `mod.config` packaging sources.
(b) ~~The analyzer burn-down — unbounded until measured.~~ **Measured 2026-08-17: it is empty.** The
residue is the `NU1902` advisory on `NuGet.CommandLine 4.4.1`, which needs a decision rather than a
burn-down — bump `NUnit.Console` past it, or set `NuGetAuditMode`/suppress with a stated reason.
(c) ~~The `CA2021` pragma.~~ **Withdrawn — the rule no longer fires; nothing to do.** (d) The
determinism acceptance test: extending `tools/fp-determinism/` to `DamageWarhead`,
`BallisticMissileFly` and `Aircraft.CalculateAccelerationToWaypoint`.

---

## Sequencing

1. **Run the decisive desync experiment first — it is free and it may reprice everything.**
   `WORKSPACE/RESUME-260816.md:47` already names it: replay one desyncing match on *one machine*
   under *both* runtimes with `Test.ForceSyncReports=true`, and diff the `syncdiag-*` files. If it
   goes red, runtime heterogeneity is the desync cause, the net10 bump is the **fix**, and it ships
   immediately. If green, the bump is determinism-neutral and gets scheduled on its own merits.
   Either way you learn this before changing the variable you are currently debugging.
2. ~~Convert the `CA2021` comment guard to a scoped `#pragma`.~~ **Dropped 2026-08-17 — the rule does
   not fire under SDK 10. Do not add a pragma for a diagnostic that no longer exists.**
3. ~~Measure the CA\* half of the analyzer delta without installing anything.~~ **Done 2026-08-17, and
   done properly: SDK 10.0.400 is installed side by side, so both halves are measured, not one. §3.**
4. Delete the Mono lane. That is most of the upgrade, and it is separable and independently useful.
5. Extend `tools/fp-determinism/` to the three unprobed kernels.
6. Bump TFM + `global.json`. **There is no analyzer burn-down to schedule** — the residue is ~20 lines
   of mechanical BCL/NuGet cleanup listed in §3, plus one decision on the `NU1902` advisory.

## What is still unmeasured

§3 is now compiled; everything below it is not, and the following remain genuinely open.

- **The CI runner's SDK version is still recorded nowhere.** The local measurement is
  macOS x64 / SDK 10.0.400. Analyzer sets are keyed on `AnalysisLevel` (the TFM) rather than the exact
  SDK build, so a runner on any 10.0.x should agree — but a runner on 10.0.1xx with a different
  Roslyn could differ by a rule or two, and `CA2021` specifically is a rule whose behaviour has
  already changed once between SDK bands. Pinning `global.json` to a specific 10.0.x is what makes
  local and CI agree; that is the same argument `c1f7e697` already made for 6.0.428.
- **Windows and Linux were not measured.** Analyzer output should be platform-independent, but
  `TargetPlatform` differs and the Windows lane discards its own exit code (see the CI-integrity
  entry), so a green Windows tick would not prove agreement anyway.
- **The Mono/netstandard2.1 lane is unmeasurable here** — no Mono on this machine, and that lane is
  already red on `CS0117`. §2 recommends deleting it regardless.
- **Nothing about §"the decisive question" changed.** The three unprobed float kernels are still
  unprobed; the analyzer result says nothing about determinism.
