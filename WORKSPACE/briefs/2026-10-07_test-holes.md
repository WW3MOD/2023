# NUnit test holes — dispatch briefs (2026-10-07)

**Read at `main @ 589b3d86`** (worktree `wt/groom-tests`). Source: `WORKSPACE/ideas/261007_test-coverage-scout.md`
(read at `c276679c`). Every premise below was re-opened at `589b3d86`; line numbers are re-pointed to that ref.
Nothing was built or run — every "passes / fails" here is read from source, including every RED prediction.

**Gates for every brief** (CLAUDE.md, in this order): `./make.ps1 all` → `./make.ps1 check` → `dotnet test
engine/OpenRA.Test/OpenRA.Test.csproj --configuration Release` → `./make.ps1 test`. **`check` is mandatory**:
it is the only analyzer gate, and `all` / `dotnet test` both run Release with analyzers stripped. `make all`
does not compile `OpenRA.Test` at all. If `check` is red, `git diff --name-only main` and grep its log for
your own files before assuming it is yours (conventions.md §"`.\make.ps1 check` is RED on a clean `main`").

**House analyzer traps for new test files**: `RCS1226` (multi-paragraph `/// <summary>` — keep summaries
one paragraph, put prose in `//` above the declaration); `RCS1112` (the 2026-09-11 incident in CLAUDE.md:
`all` + `dotnet test` + `make test` green, `check` red, in a test file the same commit added).

**Shared-helper rule** (memory: *never duplicate subtle logic*): brief #1 adds the mod.yaml-ordered resolve to
`engine/OpenRA.Test/ModRulesYaml.cs`. Every later brief that needs resolved rules or weapons **must call that
helper**, not copy `BurntTreeScopeTest.RuleFiles()` / `KillableEconomyTest.Weapons()` again.

---

## The MiniYaml question (settles #1's fixture design)

**Yes — `MiniYaml.Merge` alone applies both `Inherits@` and `-Key:` removal, and it is the same call the
engine makes.**

- Engine path: `Ruleset.MergeOrDefault` (`engine/OpenRA.Game/GameRules/Ruleset.cs:108`) → `MiniYaml.Load`
  (`engine/OpenRA.Game/MiniYaml.cs:625-638`) → `MiniYaml.Merge` (`:638`). The only extra inputs the engine
  adds are **map** rules files/nodes appended last (`:627-636`), and the lowercase keying happens *after*
  the merge (`Ruleset.cs:114`).
- Inside `Merge` (`MiniYaml.cs:399-424`): files are cross-merged **without** resolving removals
  (`MergePartial`, `:541-591`, which appends `-Key` nodes in place and refuses to merge a later plain node
  across an intervening removal, `:575-583`); then `ResolveInherits` (`:449-492`) walks each actor's
  children **in order** — an `Inherits@` splices the parent's resolved children at that point (`:460-477`),
  a `-Key` removes whatever has accumulated *so far* (`:479-485`).
- Consequence the fixture must know about: **a `-Key:` with nothing to remove THROWS**
  (`YamlException "There are no elements with key …"`, `:483`). So for `SUPPLYROUTE` (`structures.yaml:295`,
  `Inherits@ExistsInWorld` at `:296`, `-Vaporizable:` at `:318`, `Vaporizable` supplied only by
  `^ExistsInWorld` at `defaults.yaml:9`):
  - moving `-Vaporizable:` **above** `:296` (or `Inherits@ExistsInWorld` below `:318`) is **loud** — mod
    load throws, every map fails. Not the silent case the scout described in its §1.2.
  - the **silent** re-arms are: (a) a later-loaded rules file adding `SUPPLYROUTE:` → `Vaporizable:`
    (MergePartial appends it after the removal, `:575-583`); (b) a new `Inherits@X:` placed below `:318`
    whose template declares `Vaporizable:` directly. Both leave `VaporizeScopeTest`'s presence check green.
  The resolved-shape assertion ("no `Vaporizable` on resolved `SUPPLYROUTE`") catches both. That is the
  reason to assert on the resolved tree rather than on line order.
- `BurntTreeScopeTest.cs:321-327` already relies on exactly this (`TheTraitsSurviveInheritanceResolutionOntoARealTree`
  calls `MiniYaml.Merge(RuleFiles()...)`), so the pattern is proven in-tree, not new.

---

## #1 — NoAutoTarget tripwire + resolved Supply Route shape — **S**

**Goal:** fail the build when any weapon could hit the Supply Route through the normal target-type path, or
when the resolved `SUPPLYROUTE` loses any property that keeps it alive, unbuildable and uncapturable.

**Why it's first:** `Targetable: TargetTypes: NoAutoTarget` (`structures.yaml:389-390`) is the SR's *only*
protection against ordinary damage; `Armor: Type: Indestructable` (`:416-417`) is inert. One weapon line
`ValidTargets: …, NoAutoTarget` makes the SR killable → `MustBeDestroyed` (`:368`) → immediate defeat. Nothing
tests it today (grep `engine/OpenRA.Test` for `NoAutoTarget` → only `CrateAutoTargetExclusionTest`, which is
about the *crate*). Manager-verified and re-checked: zero live `ValidTargets` lines in `mods/ww3mod/**/*.yaml`
name `NoAutoTarget` today, so the tripwire lands green.

**Files**
- touch `engine/OpenRA.Test/ModRulesYaml.cs` — add `ResolvedRules()` and `ResolvedWeapons()`: read `mod.yaml`'s
  `Rules:` / `Weapons:` list in order, `MiniYaml.FromFile` each, `MiniYaml.Merge`, return name-keyed dictionary.
  Copy the body of `BurntTreeScopeTest.RuleFiles()` (`BurntTreeScopeTest.cs:69-91`, incl. its `> 20 files`
  and `File.Exists` guards) and `KillableEconomyTest.Weapons()` (`OpenRA.Mods.Common/KillableEconomyTest.cs:172-199`,
  incl. its `> 100 weapons` guard). Do **not** port the heavy-ordnance-last assertion into the shared helper;
  it belongs to KillableEconomy. Do **not** migrate the existing callers here (that is #6).
- create `engine/OpenRA.Test/OpenRA.Mods.Common/SupplyRouteShapeTest.cs`.

**Assertions**
1. `NoWeaponCanTargetNoAutoTarget` — for every resolved weapon (abstract `^` templates included), the
   weapon-level `ValidTargets` **and** every `Warhead*` child's `ValidTargets` must not contain `NoAutoTarget`.
   Both levels matter: `WeaponInfo.ValidTargets` (`engine/OpenRA.Game/GameRules/WeaponInfo.cs:127`, checked
   `:223`) gates targeting, `Warhead.ValidTargets` (`engine/OpenRA.Mods.Common/Warheads/Warhead.cs:30`, checked
   `:57`) gates impact. An absent field defaults to `Ground, Water` — safe, no need to model it. Check **all**
   warhead types, not KillableEconomy's `DamagingWarheads` set: a `GrantExternalCondition`/fire warhead reaching
   the SR is also a hole. Failure message must name weapon + warhead key and say "this makes the Supply Route
   damageable → MustBeDestroyed → instant defeat; see structures.yaml SUPPLYROUTE Targetable".
2. `TheResolvedSupplyRouteIsUntargetableNotInvulnerable` on `ResolvedRules()["SUPPLYROUTE"]`:
   - no child keyed `Vaporizable` (the `-Vaporizable:` survived resolution);
   - the union of `TargetTypes` over every `Targetable*` child == `{NoAutoTarget}` (union, not "the
     Targetable": a template adding a `Targetable@X: TargetTypes: Ground` must fail this, and a single-key
     lookup would miss it), and the set of `Targetable*` children is non-empty;
   - `Health` present with an `HP` (do not pin 75000 — a retune is legitimate);
   - no child keyed `Capturable*`, `CaptureManager`, `Capturable@*` (capture is designed-but-unwired; CLAUDE.md);
   - `Buildable.Prerequisites` contains `~disabled` (`structures.yaml:380-383`).
3. `NoAutoTargetIsNotAnAutoTargetValidTarget` — **optional, drop if it costs more than 15 minutes.** Same
   rule, actor side: no `AutoTarget*`/`AutoTargetPriority*` `ValidTargets` names it.

Copy failure-message style from `VaporizeScopeTest.cs:192-215` (explains the consequence, cites the line).

**RED arm** (each one, then revert):
- add `NoAutoTarget` to one warhead's `ValidTargets` in any `rules/weapons/*.yaml` → assertion 1 fails with
  the weapon + warhead name in the message.
- append `\tVaporizable:` as the last child of the `SUPPLYROUTE` that `rules/cameo-captions.yaml` declares
  (a later-loaded file — the silent case) → assertion 2 fails on "Vaporizable"; **confirm
  `VaporizeScopeTest.TheSupplyRouteOptsOutOfVaporisation` stays GREEN under the same sabotage** — that is the
  hole this brief closes, record it in the commit message.
- move `-Vaporizable:` above `Inherits@ExistsInWorld` → expect a `YamlException "no elements with key
  Vaporizable to remove"` from the helper (an **error**, not an assertion failure). This is a check on the
  MiniYaml answer above, not a RED for this fixture; note the observed text either way.

**Acceptance:** all three fixtures green on main; both REDs observed with the specific message; the
`VaporizeScopeTest`-stays-green observation in the commit message.

**Risks:** none to `@stable` (test-only). A helper bug that returns an empty dict passes vacuously — keep the
count guards. Map-level `weapons.yaml` overrides are **not** covered (engine appends them, `MiniYaml.cs:627-636`);
say so in the fixture header. Lua `Kill()` and non-warhead damage paths are out of scope.

---

## #2 — Faction → bot-condition mapping — **S**

**Goal:** catch the Russian (or American) bots silently switching off because the faction-to-condition grant
was re-filtered.

**Premise, corrected.** The scout said dropping the grant disables every Russian bot module "with no lint
error". **Half-false**: deleting both grants of `player.brics` *is* caught — `CheckConditions` emits an
**error** "consumes conditions that are not granted" (`engine/OpenRA.Mods.Common/Lint/CheckConditions.cs:75`),
and `make test` fails. The **silent** case is a grant that still exists but whose `Factions:` filter no longer
names `russia` (typo, case — `Russia`, a stale `brics` — or copy-paste to `america`): the condition is lexically
granted, lint is satisfied, no Russian player ever receives it. Chain at `player.yaml:1150-1172`:
`ProvidesPrerequisite@{NATO,America,BRICS,Russia}` (`Factions: america|russia`) →
`GrantConditionOnPrerequisite@*` (`Condition: player.nato|player.brics`). Consumers: 26 `player.nato|brics`
references in `rules/ai/ai.yaml`, 2 each in `ai-america.yaml`, `ai-russia.yaml`.

**Files:** create `engine/OpenRA.Test/OpenRA.Mods.Common/FactionConditionGrantTest.cs`; use
`ModRulesYaml.ResolvedRules()` from #1.

**Assertions** (on resolved `player`):
1. Compute, per faction `f ∈ {america, russia}` (read from `world.yaml` `Faction@*` `InternalName`s, excluding
   `Random`), the set of prerequisites from `ProvidesPrerequisite*` whose `Factions` contains `f` (or has no
   `Factions` filter), then the conditions from `GrantConditionOnPrerequisite*` whose `Prerequisites` ⊆ that set.
2. `america` gets `player.nato` and not `player.brics`; `russia` gets `player.brics` and not `player.nato`.
3. Every `player.<x>` token in any bot-module `RequiresCondition` on the resolved player is granted to at least
   one faction (parse tokens with a regex over `player\.[a-z]+`).

**RED arm:** change `ProvidesPrerequisite@BRICS` *and* `@Russia` `Factions: russia` → `Factions: Russia`;
assertion 2 fails with "russia is not granted player.brics — every Russian bot module is now inert". Confirm
separately (by reading, not running) that `CheckConditions` would *not* flag this.

**Acceptance:** green on main; RED observed. **Risks:** none (test-only). If the `Factions` parse treats an
absent filter wrong, assertion 2 passes vacuously for both — assert the per-faction sets are non-empty.

---

## #3 — "Passive ⇒ production halted", against the real modifier — **S–M**

**Goal:** pin the half of the win condition that says a passive player produces nothing, and make the
existing "agrees with the production modifier" test compare against the real method instead of a copy.

**Premise, confirmed.** `IProductionSpeedModifier.GetProductionSpeedModifier`
(`SupplyRouteContestation.cs:1007-1019`) is an explicit interface impl reading instance state; the `isPassive →
0` limb has no test. `SupplyRouteWarningTest.SlowedAgreesWithTheProductionModifierAcrossTheWholeBar`
(`OpenRA.Mods.Common/SupplyRouteWarningTest.cs:72`) compares `IsProductionSlowed` to an in-test
re-implementation — vacuous with respect to its name.

**Files:** touch `engine/OpenRA.Mods.Common/Traits/SupplyRouteContestation.cs`; touch
`SupplyRouteWarningTest.cs`; create (or extend) a test for the new static.

**Change:** extract `public static int ProductionSpeedPercent(int controlBar, int barMax, int slowdownThreshold,
bool isPassive)` with the body of `:1010-1018` verbatim; the interface method becomes
`return ProductionSpeedPercent(controlBar, info.BarMax, info.SlowdownThreshold, isPassive);`. Byte-identical
behaviour — no arithmetic change.

**Assertions:**
1. `isPassive: true` → 0 at every bar level including full (`BarMax`).
2. Not passive: 0 at `controlBar <= 0`; 100 at and above threshold; linear below.
3. Rewrite `SlowedAgreesWith…` to compare `IsProductionSlowed(c,…)` with `ProductionSpeedPercent(c,…,false) < 100`.
4. IL pin (pattern: `VaporizeScopeTest.cs:62-77` `ScanFor`/`Calls` over `IlScan.Scan`, `engine/OpenRA.Test/IlScan.cs:61`):
   the explicit interface method calls `ProductionSpeedPercent`. Get it via
   `typeof(SupplyRouteContestation).GetInterfaceMap(typeof(IProductionSpeedModifier))`.

**RED arm:** in the extracted method, drop `isPassive ||` → assertion 1 fails ("passive player still producing
at N%"); separately, revert the interface body to inline code → assertion 4 fails.

**Acceptance:** green; both REDs. **Risks:** shared by `@stable` and `@experimental` and by players, so the
extraction must be byte-identical — say so in the commit message; no `@stable` behaviour change expected.
Watch `RCS1163`-style unused-parameter rules if the signature drifts.

---

## #4 — Shipped contestation durations, read from YAML — **S**

**Goal:** make a YAML retune of `SupplyRouteContestation` fail the tests that claim "90 s / 30 s / 180 s",
instead of leaving them green against hand-copied constants.

**Premise, confirmed.** `SupplyRouteCollapseTest.cs:36-47` hand-copies `BaseTicks`/`MinTicks`/… and
`MsPerTick = 60.0`, citing `structures.yaml:260-271` — the block is at **`:396-408`** at this ref.
`SupplyRouteWarningTest.cs:35-36` does the same. `SupplyRouteCollapseTest.cs:132`
(`17 * MsPerTick / 1000.0 ≈ 1.0`) is arithmetic on two test constants — vacuous.

**Files:** create `engine/OpenRA.Test/OpenRA.Mods.Common/SupplyRouteShippedTimingTest.cs`; touch
`SupplyRouteCollapseTest.cs` (fix the citation, replace the `:132` assertion), use `ModRulesYaml.ResolvedRules()`.

**Assertions:** read `SupplyRouteContestation:` off resolved `SUPPLYROUTE`, and the default GameSpeed Timestep
the way `AutotestTickRateTest.DefaultTimestepMs()` does (`OpenRA.Mods.Common/AutotestTickRateTest.cs:118-133`)
— **extract that reader to a shared helper rather than copying the regex**. Then, in real seconds:
`BaseTicks` ≈ 90 s, `MinTicks` ≈ 30 s, `BaseRecoveryTicks` ≈ 180 s, `LockoutCollapseTicks` ≈ 1 s (±10 %).
Assert the hand-copied constants in `SupplyRouteCollapseTest` equal the YAML values (so the old fixture
cannot drift silently either).

**RED arm:** `BaseTicks: 1500` → `2500` in `structures.yaml` → fails with "contestation now takes 150 s, design
says 90 s (CLAUDE.md, game-model.md) — retune the docs or the YAML, not this test".

**Risks:** the seconds targets are a **design claim** (CLAUDE.md quotes them). If a retune is intended, the
test change is the review point — say that in the message. Test-only otherwise.

---

## #5 — Recovery / reinstatement and lockout collapse, extracted and pinned — **M**

**Goal:** catch a passive player who is never reinstated, or reinstated with the defeat bar still up, and the
lockout-collapse rate regressing.

**Premise, confirmed.** Recovery is inline in `ITick.Tick` (`SupplyRouteContestation.cs:584-622`): defeat bar
drains first (`:587-604`, reinstates and clears `isPassive` only when it reaches 0, `:595-603`), then control
bar (`:605-621`), rate `Math.Max(1, BarMax / BaseRecoveryTicks)` × `FriendlyRecoveryMultiplier` when
`cachedNetFriendlySurplus > 0`. Lockout `defeatRate` inline at `:557-559`. Zero test references to
`BaseRecoveryTicks`, `FriendlyRecoveryMultiplier`, `LockoutCollapseTicks`-as-code.

**Change:** extract `public static int RecoveryRate(int barMax, int baseRecoveryTicks, bool friendliesPresent,
int multiplier)`, `public static (int Control, int Defeat, bool Reinstated) RecoveryStep(int controlBar, int
defeatBar, bool isPassive, int barMax, int rate)` and `public static int DefeatRate(int rate, int teamValue,
int barMax, int lockoutTicks)`. `Tick` calls them; side effects (`OnReinstated`, warning re-arm, latches) stay
in `Tick`. **Byte-identical**: integer division, `Max(1, …)`, clamp order exactly as today.

**Assertions:** solo recovery 0→full in ≈ 180 s, with friendlies ≈ 60 s (real timestep, via #4's reader);
defeat bar drains to 0 before the control bar moves; `Reinstated` only on the step that reaches 0 while
passive; `DefeatRate` never below `rate`, and the zero-team branch ≥ `BarMax / lockoutTicks`.

**RED arm:** swap the two branches' order in `RecoveryStep` (control before defeat) → drain-order test fails;
set `Reinstated` true on any drain step → reinstatement test fails.

**Risks:** `@stable`-shared and match-deciding code — the strongest byte-identity obligation in this list.
Consider one `--hidden` autotest of an existing contestation scenario **only with explicit goahead** (CLAUDE.md
no-autonomous-runs rule). Depends on #4 for the timestep helper.

---

## #6 — `ModRulesYaml` migration + the vacuous-pass path in `VaporizeScopeTest` — **M**

**Goal:** remove the "an unloaded yaml file can satisfy the test" class from the actor-shape fixtures.

**Premise, confirmed.** `VaporizeScopeTest.TopLevel` (`VaporizeScopeTest.cs:157-189`) globs every `*.yaml`
under `rules/`, loaded or not, and the checks are `Any(...)`. Its comment at `:194-197` says
`cameo-captions.yaml` is "deliberately NOT in mod.yaml's Rules list" — **false**: `mod.yaml:151` loads it.
Fix the comment on sight regardless.

**Files:** `VaporizeScopeTest.cs` first, then other actor-shape fixtures that read rules (scout counted 57
locating `mods/ww3mod` themselves vs 7 using `ModRulesYaml` at `c276679c` — recount, do not quote).

**Assertions:** each migrated fixture asserts on `ModRulesYaml.ResolvedRules()`; for the three `-Vaporizable`
opt-outs (SR, `^TechBuilding` `structures.yaml:183`, `^ShootableMissile` `defaults.yaml:1123`), assert on the
resolved concrete actors that `Vaporizable` is absent rather than that a `-Vaporizable` line exists. Leave the
SR one as a thin cross-reference to #1 rather than a duplicate.

**RED arm per migrated assertion:** put the sabotage in an **unloaded** file (`rules/ingame/old.yaml`): the old
fixture stays green, the migrated one does not care (correct), and a sabotage in a loaded file fails it.

**Risks:** mechanical churn across many files — one fixture per commit, each with its own RED. Do not change a
single-file fixture to resolved semantics without checking it was not deliberately single-file. Depends on #1.

---

## #7 — Supply Route produces from the map edge — **S**

**Goal:** catch a merge that reverts the SR's `ProductionFromMapEdge` to plain `Production`, which would spawn
reinforcements on the beachhead instead of walking in.

**Premise, confirmed, with a correction.** `structures.yaml:472-473`: `ProductionFromMapEdge: Produces:
Infantry, Soldier, Vehicle, Aircraft, Helicopter`; a separate `Produces: Building, Defense, Powers` at `:470`.
The scout's "covers every `ClassicProductionQueue@*` Type in player.yaml" is **wrong as an assertion**: the
player has a `Ship` queue (`player.yaml:70-71`) that the SR does not serve. Pin an explicit expected set.

**Files:** create `engine/OpenRA.Test/OpenRA.Mods.Common/SupplyRouteProductionShapeTest.cs`; use #1's helper.

**Assertions:** resolved `SUPPLYROUTE` has exactly one `ProductionFromMapEdge*`, no plain `Production`/
`Production@*` producing a mobile type, and its `Produces` set == `{Infantry, Soldier, Vehicle, Aircraft,
Helicopter}`.

**RED arm:** rename the key to `Production:` → fails "units would spawn on the beachhead". **Risks:** none.

---

## #8 — IL pins on the defeat fork and the Doomsday freeze — **XS**

**Goal:** catch the passive-vs-defeated fork being unwired from `HasRescuer`/`ResolveTeamElimination`, and the
Doomsday freeze dropping out of `Tick`.

**Premise, confirmed.** `OnDefeatBarFull` (`SupplyRouteContestation.cs:702-743`) → `HasActiveTeamSupplyRoute`
(`:745-748`) → `HasRescuer`; `ResolveTeamElimination` (`:810`) returns early under `TestMode.IsActive` (`:814`),
so no autotest can reach it — NUnit is the only feasible pin. `Tick` calls `DoomsdayStrike.VictoryChecksSuspended`
(`:514`).

**Files:** add to `SupplyRouteEliminationTest.cs` (it already owns `HasRescuer`) using `IlScan`
(`VaporizeScopeTest.cs:62-77` pattern; private methods via `BindingFlags.NonPublic | Instance`).

**Assertions:** `OnDefeatBarFull` calls `HasActiveTeamSupplyRoute` and `ResolveTeamElimination`;
`HasActiveTeamSupplyRoute` calls `HasRescuer`; `ITick.Tick` calls `VictoryChecksSuspended`.

**RED arm:** comment out the `ResolveTeamElimination();` call → fails. **Risks:** IL pins break on legitimate
refactors; the message must say "re-point, don't delete".

---

## #9 — Two-faction census — **S**

**Goal:** catch a third faction appearing, or a dead faction string re-entering a `Factions:` filter.

**Premise, confirmed.** `world.yaml:257-274`: `Faction@randomside` (`RandomFactionMembers: america, russia`),
`Faction@0` america, `Faction@1` russia. `player.yaml:1126` records the purge of dead faction strings.
**CLAUDE.md is stale**: it cites `player.yaml:645: Factions: brics, russia, china, belarus`; no such line exists
at this ref (`grep -n Factions: player.yaml` → only america/russia). Flag for the docs lane; not this brief's job.

**Assertions:** non-random `Faction@*` InternalNames == `{america, russia}`; `RandomFactionMembers` == same set;
every `Factions:` value on any resolved actor names one of them. **RED:** add `Factions: china` to one
`ProvidesPrerequisite`. **Risks:** do NOT treat `brics` in *condition/prerequisite names* as a faction —
`player.brics` is a live identifier (CLAUDE.md). Only `Factions:` values are checked.

---

## #10 — Bot profile gate pin — **S, partly hypothetical**

**Goal:** protect "no silent drift of the `@stable` benchmark" at the YAML level.

**Premise, partly corrected.** Module suffixes are **not** a clean `@stable`/`@experimental` pair: at this ref
the suffixes are 17 `stable`, 20 `experimental`, plus `america`/`russia` (5 each), `poi`, `normal`,
`defenses`, `supply`, `aiplayer`. Profile membership is carried by `RequiresCondition`
(`enable-ai-stable` / `enable-ai-experimental` / `enable-ai-any`), not by the suffix. *Hypothetical:* that
every module type present under experimental must have a stable twin — some are experimental-only by design.

**Assertions:** every `*BotModule*` instance on resolved `player` has a `RequiresCondition` naming **exactly
one** of the three `enable-ai-*` tokens; module types present in experimental but not stable (or vice versa)
equal an explicit allowlist in the test, with a comment per entry. **RED:** strip the `enable-ai-stable`
token from one stable module. **Risks:** the allowlist is the review point; first author must confirm each
entry with the manager rather than snapshot today's state blindly.

---

## #11 — Installer identity text pins — **XS–S**

**Goal:** catch an uninstaller that leaves shortcuts behind, or `OpenRA` leaking back into install identity.

**Premise, confirmed.** `packaging/windows/buildpackage.nsi`: Start-menu `.lnk` create `:283` ↔ delete `:380`,
desktop `:317` ↔ `:383`; `MUI_STARTMENUPAGE_DEFAULTFOLDER "WW3MOD"` `:78`. `mod.config:41,84,88`
(`PACKAGING_DISPLAY_NAME`, `…_INSTALL_DIR_NAME`, `…_REGISTRY_KEY`). The scout's `Directory.Build.props:17` is
`engine/Directory.Build.props`. The `.nsi` is never compiled in this pipeline.

**Assertions** (text-level): the set of `.lnk` paths `CreateShortCut`d equals the set `Delete`d; the three
`mod.config` values contain no `OpenRA`; default start-menu folder equals `PACKAGING_DISPLAY_NAME`.
**RED:** change the desktop `Delete` path. **Risks:** regex over NSIS is brittle; keep it to the four lines.
Low game risk — lowest priority on this list.

---

## Not briefed

- Bot-module behavioural coverage — autotest territory.
- Cost-as-budget invariants — scout found no hole beyond existing refund/upkeep fixtures (unaudited).
- A generalised "every `MustBeDestroyed` actor protected by `NoAutoTarget` also opts out of vaporisation"
  (scout §5) — fold into #6 if it falls out naturally. Not audited: 9 live `MustBeDestroyed` sites exist
  (`defaults.yaml:1206,1247`, `structures.yaml:149,368`, …); whether any other relies on `NoAutoTarget` was not checked.
