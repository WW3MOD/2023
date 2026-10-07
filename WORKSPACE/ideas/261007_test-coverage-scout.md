# NUnit coverage vs. WW3MOD's rebuilt mechanics — scout report

**As of `main @ c276679c`** (branch `wt/scout-test-coverage`, worktree clean at start). Read-only: nothing
was built, run or launched, so **no test outcome below was observed** — every "passes"/"fails" is read
from source. Line numbers are at this ref and rot fast; re-grep before acting.

## The suite at a glance

- One test project: `engine/OpenRA.Test/OpenRA.Test.csproj` — 239 `.cs` files across the root,
  `OpenRA.Game/` and `OpenRA.Mods.Common/`. Attribute census: **3486 `[Test]`, 558 `[TestCase]`, 4
  `[TestCaseSource]`** (a `[TestCase]`-parameterised method expands to N cases, so this does not reconcile
  to the 3184 CLAUDE.md quotes from 2026-09-11; treat both as unreconciled counts, not a total).
- Shape: overwhelmingly **pure-function** fixtures (a trait exposes `public static` decisions, the test
  calls them) plus **YAML-shape** fixtures (read `mods/ww3mod/rules` with `MiniYaml.FromFile`) plus a few
  **IL-scan** fixtures (`IlScan.cs`: "does method X still call Y"). **No fixture constructs a `World`.**
  Every claim of the form "the call site is wired" is therefore code reasoning, and several fixtures say so.
- Reminder (DISCOVERIES 2026-09-15, promoted): `make all` never compiles `OpenRA.Test`; only `make check`
  / `dotnet test` do.

---

## 1. Supply Route actor invariants (untargetable, HP, -Vaporizable, no capture)

**What exists** — `engine/OpenRA.Test/VaporizeScopeTest.cs`:
- `:200 TheSupplyRouteOptsOutOfVaporisation` — some top-level `SUPPLYROUTE` node, in any yaml under
  `rules/`, has a `-Vaporizable` child; and some node has `Health`.
- `:79-137` IL-scan pins: `VaporizeWarhead.IsValidAgainst` overrides the base, calls
  `Vaporizable.CanVaporize`, and does **not** call `Warhead.IsValidTarget` / `base.IsValidAgainst`.

**Not asserted anywhere** (grep of `engine/OpenRA.Test` for `NoAutoTarget`, `Capturable`,
`CaptureManager`, `Indestructable` on SR):
1. **"No weapon lists `NoAutoTarget`"** — the SR's *only* protection against ordinary damage
   (`structures.yaml:389-390`). Today zero live `ValidTargets` lines name it (only comments at
   `defaults.yaml:859,863`, `vehicles-russia.yaml:1338`). One weapon adding `ValidTargets: …, NoAutoTarget`
   makes the SR killable by that weapon → `MustBeDestroyed` (`:368`) → instant loss. **Highest-value
   missing test in this report.** Also: `misc.yaml:418` gives a crate `TargetTypes: Ground, Structure,
   NoAutoTarget`, so the target type is not SR-exclusive — the pin is on the *weapon* side, not the actor.
2. **`-Vaporizable:` position.** The test checks *presence*, not that it sits *after* the
   `Inherits@ExistsInWorld` that supplies `Vaporizable` (`:296` vs `:318`). conventions.md §"There is no
   own-beats-inherited rule": a `-Key:` above a re-supplying `Inherits@` is undone. Moving an `Inherits@`
   below line 318 keeps the test green and re-arms the one-nuke win. *(Hypothesis-free: this follows
   directly from `MiniYaml.ResolveInherits`; not exercised.)*
3. **Resolved actor carries `Targetable` with exactly `NoAutoTarget`** and `Health.HP` — nothing reads it.
4. **No `Capturable` / `CaptureManager` on resolved SUPPLYROUTE.** CLAUDE.md says capture is unwired and
   copy must not assume it; nothing fails if someone adds a `Capturable:` (e.g. via a template) and
   ownership transfer silently goes live.
5. **`Buildable: Prerequisites: ~disabled`** (`:380-384`) — the "not buildable" premise. SR carries a
   `Buildable` block and `RequiresBuildableArea` (`:327`); only `~disabled` keeps it off the sidebar.

**Pin shape (one fixture, `SupplyRouteShapeTest`)**: resolve rules **the way the engine does** —
`MiniYaml.Merge(<files in mod.yaml Rules: order>)` then inheritance resolution — copying the pattern
already in `BurntTreeScopeTest.cs:69-88,327` (reads `mod.yaml`'s Rules list, merges, asserts on the
resolved node). Assert on resolved `SUPPLYROUTE`: no `Vaporizable`; `Targetable.TargetTypes == NoAutoTarget`;
`Health.HP` present; no `Capturable*`/`CaptureManager`; `Buildable.Prerequisites` contains `~disabled`.
Separately, walk merged weapons (pattern: `KillableEconomyTest.cs:166-193`) and assert no
`ValidTargets` on any warhead contains `NoAutoTarget`. RED arm: add `-Vaporizable:` above an `Inherits@`
in a scratch copy / add `NoAutoTarget` to one weapon. **Risk if regressed:** instant-win; silent.
**Effort:** S (half a day; all patterns exist). *Check first that `MiniYaml.Merge` alone resolves
`-Key:`/`Inherits` — BurntTreeScopeTest's comment at :323-326 claims it does.*

## 2. The 2026-09-19 shadowing fragility, generalised

`VaporizeScopeTest` was fixed with `FindAll` (`:191-196`). Remaining fragility, measured:
- **The fixture's own premise comment is now false.** `:202-204` says `rules/cameo-captions.yaml` is
  "deliberately NOT in mod.yaml's Rules list". It **is** — `mod.yaml:151`, made live by `863740f3`
  (file header: "ruled live 2026-09-20"). Harmless to the assertion, wrong as documentation; fix on sight.
- **`TopLevel()` scans every `*.yaml` under `rules/`, loaded or not** (`:171-182`), and the checks are
  `Any(...)`. A `-Vaporizable:` sitting in an **unloaded** file (e.g. `rules/ingame/old.yaml`, which
  mod.yaml does not load — cameo-captions header) would satisfy the test while the shipped SR has no
  opt-out. **Passes-vacuously class.** Fix = §1's mod.yaml-ordered resolve.
- **126 of 933 top-level rules keys are declared in >1 file** (script census, this ref). Fixtures that
  read a *single named file* (`AirborneArmorTargetableTest`, `RefillRowFormatTest`, `PoolLabelFallbackTest`,
  `SupplyCacheTruckParityTest`, `LogisticsCenterSupplyTest`) are immune to first-match shadowing but are
  **blind to a cross-file override** — the opposite failure. Today the second declarations are
  cameo-caption `Buildable.CameoCaption` only, so no current misread; it is latent.
- **Duplicated locator logic:** **57** test files locate `mods/ww3mod` themselves; only **7** use the
  shared `ModRulesYaml` (`ModRulesYaml.cs`, extracted precisely because "two hand-kept copies would drift").
  `VaporizeScopeTest.RulesDir/TopLevel` (`:157-182`) is a near-verbatim copy. Recommend a
  `ModRulesYaml.Resolved()` (mod.yaml-ordered merge) and migrating the actor-shape fixtures to it.
  **Effort:** M (mechanical, many files; do it per-fixture with a RED per migrated assertion).

## 3. SupplyRouteContestation

**What exists** (all pure-static):
| Fixture | Asserts |
|---|---|
| `SupplyRouteCollapseTest.cs` | `CollapseWeakness` ramp/monotone (:67-104); `NoSpeedupWhileTeamIsHealthy` byte-identity (:108); healthy 90 s / 30 s floor in **real seconds at 60 ms** (:136-143); lone scout >120 s (:147); never-slower (:171); floor (:190) |
| `SupplyRouteWarningTest.cs` | `IsProductionSlowed` threshold edges (:44-68); `ShouldRearmWarning` default = shipped `>= BarMax` (:90); hysteresis (:115) |
| `SupplyRouteBarVisibilityTest.cs` | `OwnerStillPlaying` / `ShouldShowBarWithoutSelection` (defeated owner's bar hidden) |
| `SupplyRouteEliminationTest.cs` | elimination targets / no cascade (:139-256); award logic; **`HasRescuer`** (:301-420) — i.e. the passive-vs-defeated fork *decision* incl. "passive teammate cannot rescue", "Lost teammate never rescues", "enemy never rescues" |

**Not asserted:**
1. **The shipped YAML values.** Every fixture hand-copies constants (`SupplyRouteCollapseTest.cs:35-44`,
   comment cites `structures.yaml:260-271` — actual block is `:396-413` at this ref;
   `SupplyRouteWarningTest.cs:35-36`). Retuning `BaseTicks` in YAML leaves all four green and the
   "90 s / 30 s / 180 s" claims (CLAUDE.md) false. `MsPerTick = 60.0` is also hardcoded (:44), whereas
   `AutotestTickRateTest` reads the real Timestep from mod.yaml. **Pin:** read `SupplyRouteContestation:`
   off the resolved SUPPLYROUTE and the default GameSpeed Timestep (reuse `AutotestTickRateTest`'s reader),
   then assert the *durations* (≈90 s, ≈30 s, 180 s recovery) rather than the integers. **Effort:** S.
2. **Recovery path (`BaseRecoveryTicks`, `FriendlyRecoveryMultiplier`)** — inlined in `Tick`
   (`SupplyRouteContestation.cs:588-621`), no pure helper, **zero test references** to either field.
   Ordering invariant (defeat bar drains before control bar; reinstatement only when defeat bar hits 0;
   passive flag cleared there) is untested. **Pin:** extract `RecoveryRate(barMax, baseRecoveryTicks,
   friendlySurplus>0, multiplier)` + a pure `RecoveryStep(controlBar, defeatBar, …) -> (c, d, reinstated)`;
   test 180 s solo / 60 s with friendlies at the real timestep, and the drain order. **Risk:** a passive
   player never reinstated (stuck frozen) or reinstated with the defeat bar still up. **Effort:** M
   (needs a small extraction, so it is a code change, `@stable`-shared — keep byte-identical).
3. **Lockout collapse (`LockoutCollapseTicks`)** — `defeatRate` at `:557-559` is inline. The only test
   touching it is `SupplyRouteCollapseTest.cs:132`: `Assert.That(17 * MsPerTick / 1000.0, Is.EqualTo(1.0).Within(0.1))`
   — **arithmetic on two constants defined in the test; passes vacuously** w.r.t. the trait. Pin: extract
   `DefeatRate(rate, teamValue, barMax, lockoutTicks)`; assert `Max` (never slower) and the zero-team branch.
4. **Production modifier itself.** `SlowedAgreesWithTheProductionModifierAcrossTheWholeBar`
   (`SupplyRouteWarningTest.cs:72-86`) compares `IsProductionSlowed` to a **re-implementation inside the
   test**, not to `IProductionSpeedModifier.GetProductionSpeedModifier` (`:1007-1019`, explicit interface
   impl). If the real modifier changes, the test still agrees with its copy. **The `isPassive -> 0` limb —
   i.e. "passive means production frozen", half of the win condition — is untested.** Pin: extract static
   `ProductionSpeedPercent(controlBar, barMax, threshold, isPassive)`, have the interface method return
   it, IL-scan pin that it does (VaporizeScopeTest style), and test the passive limb. **Effort:** S–M.
5. **`OnDefeatBarFull` wiring** (`:702-740`): that the fork really *uses* `HasRescuer` and calls
   `ResolveTeamElimination` on false. IL-scan pin (calls `HasRescuer`; calls the elimination method):
   **Effort:** XS. `ResolveTeamElimination` early-returns under `TestMode` (`:814`) so no autotest can
   reach it — the NUnit pin is the only feasible one (SupplyRouteEliminationTest header says the same).
6. **Doomsday freeze** (`:509-510`, `DoomsdayStrike.VictoryChecksSuspended`) — IL-scan "Tick calls it".
   XS. Known-adjacent: DISCOVERIES 2026-09-15 on `ConquestVictoryConditions` being TestMode-inert.

## 4. Reinforcements / off-map reserves / cost-as-budget

- `ProductionFromMapEdge.cs` (232 lines, on SUPPLYROUTE `:472`): only reference in tests is
  `DefconEscalationTest.cs` (incidental). **No fixture pins edge-spawn → walk-to-rally semantics** or that
  SUPPLYROUTE's resolved `Production*` trait is the map-edge one (a revert to plain `Production` would
  spawn units *on* the beachhead — plausible after a merge, silent in lint).
  **Pin (YAML-shape, S):** resolved SUPPLYROUTE has `ProductionFromMapEdge` and its `Produces` covers every
  `ClassicProductionQueue@*` `Type` in `player.yaml:23-82` that the SR is supposed to serve.
- Cost-as-budget: refund/upkeep maths is well covered (`EvacRefundPreviewMathTest`, `UpkeepTooltipRowTest`,
  `GrossIncomeIntegratorTest`, `CustomSellValueTest`). I found **no** single invariant to pin beyond those;
  not a priority hole. *(Hypothesis — not exhaustively audited.)*
- Backlog-known: "Per-Supply-Route production queues (requires engine changes)" (`BACKLOG.md`).

## 5. VaporizeWarhead scope

Well covered (`VaporizeScopeTest`, `VaporizeCurveTest`, `TreeIndestructibleScopeTest`,
`BurntTreeScopeTest`). Holes beyond §1-2: the `>= 14` warhead floor (`:296`) is a ratchet with no ceiling
— fine. **Vacuous-pass risk** as §2 (unloaded files count). Missing: "every actor that relies on
`NoAutoTarget` for protection and carries `MustBeDestroyed` also opts out" — a generalisation of the SR
test that would catch a *second* SR-like actor. S.

## 6. Two-faction constraint

**Not tested at all** (grep `RandomFactionMembers`, `"china"`, `"brics"` in tests → only
`FactionDescriptionSplitTest`, which checks description `\n` splitting).
Missing invariants, all YAML-shape, all S:
1. Exactly two non-random `Faction@` blocks, InternalNames `{america, russia}`; `RandomFactionMembers`
   equals that set (`world.yaml:257-275` area).
2. **Every `Factions:` filter value in loaded rules names a defined faction.** At this ref player.yaml
   is clean (`:1126-1130` comment records the purge of `nato/europe/ukraine/brics/china/belarus`).
   **CLAUDE.md is stale here**: it cites `player.yaml:645: Factions: brics, russia, china, belarus`; that
   line no longer exists. Without a test, a re-added dead faction string is invisible.
3. **`player.brics` / `player.nato` conditions are granted to exactly one faction each**
   (`player.yaml:1150-1172`). Every `@stable`/`@experimental` bot module gates on
   `player.nato || player.brics` (e.g. `ai.yaml:1161,1434`, `ai-russia.yaml:11,150`); dropping the
   grant silently disables **every Russian bot module** with no lint error. **High risk, S effort.**
   Assert: for each faction, the set of conditions granted ⊇ the condition its bot modules require.

## 7. Bot modules

25 `*BotModule.cs` files. **Zero test references by name**: `EngineerOperatorBotModule` (707 lines,
**live in both profiles**, `ai.yaml:1433,3559` — its maths is covered via `EngineerTaskingMathTest`, the
module glue is not), `BaseBuilderBotModule` (live ×1), `BuildingRepairBotModule` (live ×1);
`HarvesterBotModule`, `SupportPowerBotModule`, `McvManagerBotModule`, `CaptureManagerBotModule` are
not live (0 uncommented YAML instances) — no test needed. Existing structural pins worth knowing:
`MultiInstanceBotTraitTest` (player carries >1 ModularBot), `BotOrderGateCallerTest`,
`GarrisonBotModuleTest`.
**Missing structural invariant (S):** every `*BotModule@experimental` has an `@stable` twin or is
explicitly listed as experimental-only, and each instance's `RequiresCondition` names exactly one of
`enable-ai-stable`/`enable-ai-experimental` — protects the CLAUDE.md "no silent drift of the
benchmark control" rule at the YAML level. Behavioural bot coverage is autotest territory, out of scope.

## 8. Installer / identity (R7, merged `04f4210f`)

`ReleaseIdentityTest.cs` covers the in-game **version label** only. The five R7 sites
(`mod.config:84,88`, `packaging/windows/buildpackage.nsi:78,317,383`, `Directory.Build.props:17`) have
**no test**, and the `.nsi` is never compiled here (PIPELINE R7 close note). Pinnable text invariants (XS–S):
- install and uninstall name the same desktop and Start-Menu `.lnk` (`.nsi:283↔380`, `:317↔383`) —
  the property the R7 commit message says it preserved "byte-for-byte";
- `PACKAGING_WINDOWS_REGISTRY_KEY`, `…_INSTALL_DIR_NAME`, `PACKAGING_DISPLAY_NAME` contain no `OpenRA`;
- `MUI_STARTMENUPAGE_DEFAULTFOLDER` == display name.
Known: DISCOVERIES 2026-09-30 records the registry-key one-way door (accepted). Risk if regressed: an
uninstaller that leaves shortcuts behind — user-visible only on Windows, after release.

## 9. Tests that assert something false, or pass vacuously

| Where | Problem | Class |
|---|---|---|
| `SupplyRouteCollapseTest.cs:132` | `17*60/1000 ≈ 1.0` — both operands are test constants | vacuous |
| `SupplyRouteWarningTest.cs:72-86` | "agrees with the production modifier" compares to an in-test copy, not the real method | vacuous w.r.t. its name |
| `SupplyRouteCollapseTest.cs:35-44`, `SupplyRouteWarningTest.cs:35-36` | "shipped values" hand-copied; YAML retune leaves them green | stale-able premise |
| `SupplyRouteCollapseTest.cs:35` | cites `structures.yaml:260-271`; block is at `:396-413` | stale citation |
| `VaporizeScopeTest.cs:202-204` | says cameo-captions.yaml is not loaded; `mod.yaml:151` loads it | **false comment** |
| `VaporizeScopeTest.cs:171-182` + `Any()` | unloaded yaml can satisfy the opt-out check | vacuous-pass path |
| CLAUDE.md (not a test) | `player.yaml:645` china line gone; SR line cites `:222/:303/:347/:368` vs actual `:295/:396/:389/:416` | stale doc |

**25-tps:** I found **no test that asserts 25 tps**; every mention (e.g. `CaptureClearDurationTest.cs:11`,
`DefconReadoutTest.cs:575-615`, `MissileStrikeArrivalTest.cs:318`) is a guard *against* it, and
`AutotestTickRateTest` derives the rate from mod.yaml. The remaining live 25-tps errors CLAUDE.md
mentions ("ten other sites") are in non-test code/comments; not enumerated here.

## Priority (risk ÷ effort)

1. **No weapon lists `NoAutoTarget`** + **resolved-SR shape fixture** (§1) — instant-win guard, S.
2. **Faction-condition grants cover bot `RequiresCondition`** (§6.3) — silent all-bots-off, S.
3. **Passive ⇒ production 0** via extracted `ProductionSpeedPercent` + IL pin (§3.4) — win condition, S–M.
4. **Shipped contestation durations read from YAML** (§3.1) — S.
5. **Recovery/reinstatement extraction + tests** (§3.2) — M, `@stable`-shared, keep byte-identical.
6. `ModRulesYaml.Resolved()` + migrate VaporizeScopeTest off its private copy (§2) — M.
7. Two-faction census, installer text pins, bot twin/gate pin — XS–S each.

## Watch

- Counts (3486/558, 126/933, 57/7, 25 modules) are scripted greps at `c276679c`, not NUnit discovery;
  the 126-key census used a regex for top-level keys and may count a few non-actor keys.
- I did not verify that `MiniYaml.Merge` alone applies `-Key:` removal through `Inherits@` — §1's fixture
  design depends on it; BurntTreeScopeTest asserts it does, I did not re-derive it.
- "Zero test references" for bot modules is a name grep; a module can be covered through a `*Math`
  helper without its name appearing (EngineerOperator is exactly that case).
