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
