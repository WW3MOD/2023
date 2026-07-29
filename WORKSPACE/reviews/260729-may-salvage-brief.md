# May-salvage review brief — 260729

**DO NOT MERGE without an independent adversarial review.** This branch salvages 7
small fixes from the parked read-only branch `mac-autoburn-260521-unpushed` (May 2026),
identified by `WORKSPACE/recon/260729-branch-archaeology.md`. An implementer (not the
reviewer) prepared it; merge is the reviewer's call.

- **Branch:** `auto/may-salvage`
- **Worktree:** `worktrees/ww3mod/may-salvage`
- **Base:** `main @ 3ec115db` (the recon commit)
- **Commits:**
  - `631c9bad` — 4 C# NRE guards
  - `07aed0ae` — 3 YAML ref/typo fixes
- **Source (read-only, never touched):** `mac-autoburn-260521-unpushed`

## What changed

7 isolated fixes, each re-verified still present/unguarded on current main, then
re-applied by hand (May's line numbers had drifted 359 commits; content re-derived to
match current main's shape). No cherry-pick was used — every hunk was hand-applied and
diffed against the May original. **No behavior change beyond crash avoidance** (C#) and
**resolving dangling references** (YAML). Nothing else from the 125-commit May branch
was pulled (the recon doc classifies the remaining ~120 commits as process junk,
superseded, or conflict-prone hygiene).

### Commit `631c9bad` — 4 C# NRE guards

| Site (current main) | May SHA | Guard added | Sibling call sites already guarding the same field |
|---|---|---|---|
| `AutoTarget.cs:1013` `self.Owner.FrozenActorLayer` | `53d4ad72` | `&& self.Owner.FrozenActorLayer != null` on the `if (allowMove \|\| ab.Info.TargetFrozenActors)` | `Network/Order.cs`, `SupportPowerBotModule.cs:156` |
| `SpawnActorOnDeath.cs:103` `e.Attacker.Owner` | `5ff217a9` | `e.Attacker?.Owner` | `HarvesterAttackNotifier.cs:64`, `SmartMove.cs:45` |
| `PlayerStatistics.cs:231/234` `e.Attacker.Owner` | `d5014833` | `if (e.Attacker == null \|\| e.Attacker == self) return;` | `BaseAttackNotifier.cs:71`, `GivesExperience.cs:59`, `GivesBounty.cs:79` |
| `BuildingRepairBotModule.cs:33` `e.Attacker.Owner` | `f2a0aa10` | early `if (e.Attacker == null \|\| e.Attacker.Disposed) return;` | `BaseBuilderBotModule.cs:173`, `SquadManagerBotModule.cs:428`, `MinelayerBotModule.cs:355` |

All four match the May diffs byte-for-byte in the changed lines (AutoTarget also carries
the May explanatory comment verbatim).

### Commit `07aed0ae` — 3 YAML fixes

| Site (current main) | May SHA | Before | After |
|---|---|---|---|
| `vehicles-russia.yaml:839` tunguska `AmmoPool@1.Armaments` | `0e3858d2` | `primary, tertiary` | `primary, primary-air` |
| `vehicles-america.yaml:256` m113 `Rearmable.AmmoPools` | `b1ca86a3` | `primary-ammo, secondary-ammo, tertiary-ammo` | `primary-ammo` |
| `vehicles-russia.yaml:146` bmp2 `Tooltip.Name` | `b8778902` | `BPM-2` | `BMP-2` |

## Claims a reviewer should adversarially verify

Each is stated as a claim to *disprove*, not to trust:

1. **AutoTarget guard actually guards the reported NRE path.** Claim: `Player.FrozenActorLayer`
   is `TraitOrDefault` (can be null for Neutral / trait-omitting players) and the guarded
   `if` is the only dereference in that block. Check the trait declaration and that no other
   line in `ChooseTarget` dereferences `FrozenActorLayer` unguarded. Confirm the added
   `&&` short-circuits *before* `FrozenActorLayer.FrozenActorsInCircle(...)`.
2. **SpawnActorOnDeath / PlayerStatistics / BuildingRepairBotModule guards match a real
   null `e.Attacker`.** Claim: `AttackInfo.Attacker` can be null (ownerless/terrain kills).
   Verify against `AttackInfo` definition and that the sibling sites listed above already
   null-guard identically (i.e. this is closing an inconsistency, not inventing a case).
   For `PlayerStatistics`, confirm the early-return preserves prior semantics: the pre-existing
   `e.Attacker == self` branch already returned, so adding `== null` only adds a *new* skip
   for the null case and cannot change the non-null path.
3. **No behavior change beyond crash avoidance.** For all four C# guards, the guarded code
   only ran when the dereference would have succeeded before; when it would have NRE'd, the
   old code crashed. So the new early-return / skip changes nothing on the previously-working
   path. Verify there is no side effect between method entry and the guard that the early
   return now skips (esp. `BuildingRepairBotModule`: the guard is the first statement, so nothing
   is skipped; `PlayerStatistics`: `playerStats.DeathsCost += cost` already ran *above* the guard
   — confirm that ordering is unchanged).
4. **YAML refs were actually dangling before, resolved after.**
   - tunguska: verify the `tunguska:` block defines armaments named `primary`,
     `primary-air`, `secondary` only — no `tertiary`. Confirm `primary-air` (`Armament@1_Air`)
     was previously unowned by any AmmoPool, and that repointing does not double-own it
     (check no other pool lists `primary-air`).
   - m113: verify the `m113:` block defines only `AmmoPool@1` (`Name: primary-ammo`) — so
     `secondary-ammo`/`tertiary-ammo` were dangling. **Adversarial check:** the identical
     string `AmmoPools: primary-ammo, secondary-ammo, tertiary-ammo` also appears at
     `vehicles-america.yaml:965` under `strykershorad`, which **does** define all three pools
     (`Name: primary-ammo`/`secondary-ammo`/`tertiary-ammo` at :876/:903/:936). That line was
     **deliberately left unchanged** — confirm the edit touched only m113 and not strykershorad.
   - bmp2: cosmetic only; confirm actor id and image remain `bmp2`, only the tooltip string
     changed.
5. **Provenance.** Each fix's May original can be inspected with `git show <May SHA>` from
   this worktree (the source branch is still reachable). Diff the salvaged hunk against it.

## Behavioral risk note

The tunguska fix is the only one with a potential *gameplay* (not crash) effect: before,
the dangling `tertiary` meant the AA gun (`primary-air`) fired without consuming ammo;
after, it shares the `primary-ammo` magazine (180 rounds) with the ground gun. This is a
real balance change, not pure crash-avoidance — flag for playtest if tunguska AA uptime
matters. The other 6 fixes are crash-avoidance or cosmetic only.

## Test results (run in this worktree)

- `make all` — **Build succeeded, 0 warnings, 0 errors.**
- `dotnet test engine/OpenRA.Test/OpenRA.Test.csproj --configuration Release` —
  **Passed: 524, Failed: 0, Skipped: 0** (matches expected baseline of 524).
- `make test` (MiniYAML `--check-yaml`, needs .NET 6 — runtime 6.0.36 present) —
  **did not run to full completion**, but produced conclusive signal for this branch's
  changes. It ran under heavy CPU contention (a second `--check-yaml` from another worker
  in the main checkout + a live benchmark, all oversubscribing cores); after ~13 min CPU
  it was still going, so my own run was terminated to stop starving the benchmark (`make:
  *** [test] Terminated: 15`). Before termination it had already validated **all** rules
  YAML — including the three actors I edited — and emitted **zero errors** for `tunguska`,
  `m113`, or `bmp2`. The only warnings on those actors are the pre-existing, mod-wide
  "grants conditions that are not consumed" lint (about stance/damage condition tokens,
  unrelated to ammo pools / armaments / tooltips). Notably `tunguska`'s warning now lists
  `weapon-primary-air`, consistent with the repointed pool, with **no** dangling-armament
  or dangling-ammo-pool error. The run then hit one hard error — `Error: This map does not
  define a valid cordon` (a map needs a ≥1-cell border) — which is a **pre-existing
  map-validation failure unrelated to this branch**: the diff here touches zero map files
  (only 2 rules YAML + 4 C#), so this error fails `make test` on plain main identically.
  **Reviewer action:** re-run `make test` on a quiet machine to get the full clean/error
  count for the rules tree; expect the same pre-existing map-cordon error to surface
  independent of this salvage.

## Hand re-derivation / drops

- All 7 fixes were **hand re-applied** (not cherry-picked) because May's line numbers drifted
  ~359 commits; the changed lines still matched May's content exactly, so the fixes are
  equivalent, just re-anchored.
- **Nothing was dropped from the 7-fix target set.**
- Out of scope by design (per recon doc, not attempted here): CHANGELOG regen (`7ba39a62`),
  the perf micro-opt batch, dead-code/console-cleanup, docs promotions, and tests — all
  classified superseded or conflict-prone.

---

## Adversarial review — 260729 (independent reviewer)

Reviewer did not author the salvage. Every brief claim was re-checked at file:line in the
worktree (`auto/may-salvage`, base `main @ 3ec115db`). Diffs: `git diff 3ec115db..631c9bad`
and `631c9bad..07aed0ae`.

**VERDICT: MERGE-WITH-NITS.** No blocking findings. All 7 fixes are faithful, correct, and
crash-only on the previously-working path. Nits below are awareness items, not defects.

### C# guards — all four verified

1. **AutoTarget.cs:1016** — `Player.FrozenActorLayer` is `TraitOrDefault<FrozenActorLayer>()`
   (Player.cs:222) → genuinely nullable (Neutral / trait-omitting players). Only two derefs in
   `ChooseTarget`: line 1016 (guard) + line 1018 (inside the `if`). `&&` short-circuits before
   1018 — correct. Sibling guards confirmed: `Order.cs:138`, `SupportPowerBotModule.cs:156`.
   Non-crash path (FrozenActorLayer != null) is byte-identical to pre-change. ✓
2. **SpawnActorOnDeath.cs:103** — `AttackInfo.Attacker` is a plain field (`TraitsInterfaces.cs:83`),
   nullable on terrain/ownerless kills. `e.Attacker?.Owner` leaves `attackingPlayer` null; the
   **pre-existing** guard at line 109 (`if (attackingPlayer == null) return;`) then early-returns.
   Original path NRE'd, so no working behavior is lost. See Nit N2. ✓
3. **PlayerStatistics.cs:231** — `if (e.Attacker == null || e.Attacker == self) return;`. All
   accounting (`ArmyValue`/`AssetsValue`/`DeathsCost`) already ran above (line 229). The
   pre-existing `== self` branch already returned; adding `== null` only adds a new skip for the
   null case and cannot alter the non-null path. Deref at 234 would NRE without it. ✓
4. **BuildingRepairBotModule.cs:30** — guard is the first statement; nothing skipped. Deref at
   line 36 (`e.Attacker.Owner`) would NRE on null. Sibling precedent: `SmartMove.cs:45`. See Nit N1. ✓

### YAML — all three verified

- **tunguska** (`vehicles-russia.yaml:839`): block defines armaments `primary` (Armament@1),
  `primary-air` (Armament@1_Air), `secondary` (Armament@2) only — **no `tertiary`** (was dangling).
  After: `primary, primary-air`, both real; `primary-air` now owned by exactly one AmmoPool (no
  double-own). Balance note in §"Behavioral risk" is accurate and honestly disclosed. ✓
- **m113** (`vehicles-america.yaml:256`): actor defines only `AmmoPool@1` (`Name: primary-ammo`),
  so `secondary-ammo`/`tertiary-ammo` were dangling. After: `primary-ammo` resolves. **strykershorad**
  (which legitimately defines all three pools at :876/:903/:936) is correctly left UNCHANGED at :965. ✓
- **bmp2** (`vehicles-russia.yaml:146`): `BPM-2` → `BMP-2`, Tooltip.Name only; actor id/image untouched. ✓
- All edits are in-place value changes on existing lines — **no MiniYaml blank-line merge risk** introduced. ✓

### Provenance & completeness

All 7 May source SHAs reachable from the worktree; subjects match; salvaged changed-lines are
**byte-identical** to the May originals (spot-checked 53d4ad72, f2a0aa10, 0e3858d2, b1ca86a3).
All 7 fixes represented across the two commits — **nothing dropped**. ✓

### Verification (worktree only)

- `make all` — **Build succeeded, 0 errors** (salvage projects OpenRA.Game / OpenRA.Mods.Common clean).
- `dotnet test …/OpenRA.Test.csproj -c Release` — **Passed 524, Failed 0, Skipped 0** (matches baseline).
- `make test` (YAML lint) — **NOT run.** A live tournament autotest is running on the main checkout
  (`Test.Mode=true tournament-s1-eco-cal-nn`, PID observed); `utility.sh --check-yaml` is CPU-heavy and
  `timeout(1)` is unavailable on this macOS host to bound the run, so executing it would starve the live
  match. Substituted with full static ref-resolution (above): every edited armament/ammo-pool/tooltip ref
  resolves; no dangling `tertiary`/`secondary-ammo` remains on the edited actors. Recommend a full lint on
  a quiet machine to capture the pre-existing map-cordon error the brief already flagged (branch-independent).

### Nits (non-blocking)

- **N1 — brief claim #3 slightly overstated for BuildingRepairBotModule.** The guard includes
  `|| e.Attacker.Disposed`, which is broader than pure null-avoidance: a *non-null-but-disposed*
  attacker previously reached line 36 (`RelationshipWith`) and could queue a repair order; it now
  early-returns. This is a real (if degenerate) sim-behavior change in bot repair-response — but it is
  **deterministic** (`Disposed` is sim-state, no desync risk), arguably more correct, and inherited
  **verbatim** from May `f2a0aa10`. Awareness only.
- **N2 — SpawnActorOnDeath null-attacker → no spawn, even for attacker-independent spawn types.** For
  `OwnerType.Victim`/`InternalOwner` spawns (which don't use the attacker), a null-attacker kill now
  yields **no spawn** (attackingPlayer null → line-109 early-return), rather than a victim-owned spawn.
  Because the original path NRE'd, this is strictly-better crash recovery, not a regression — but the
  outcome is "silently no spawn," which a future reader should know. Non-blocking.
- **N3 — cosmetic.** Brief says `make all` "0 warnings"; a sub-build emits 17 pre-existing engine
  warnings (final stage 0). Immaterial to the salvage.

Nothing above blocks merge. Determinism invariants hold: no guard alters sim state on the
previously-working (non-crash) path; the only genuine gameplay change (tunguska AA ammo) is the
*correct* resolution of a dangling ref and is flagged for playtest.
