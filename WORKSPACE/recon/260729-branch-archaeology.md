# Branch Archaeology — 260729

Read-only git recon of every non-main branch. **Recommendations only — nothing deleted, nothing cherry-picked.**
Researched against `main @ e5b7bbcc` (local main, 49 commits ahead of `origin/main @ 03818f08`). Shared checkout — a benchmark worker is live on `main`; all inspection was via `git log/show/diff/merge-base` only.

## Scope note: backlog branch names vs. reality

The recon brief named `skane`, `xavi`, `maps`, `bypass`, `counterbattery`, `speed`. Only **`xavi`** actually exists (as `origin/xavi`). `skane`, `maps`, `bypass`, `counterbattery`, `speed` **do not exist** as any ref (checked `git for-each-ref` across heads/remotes/tags). They were either never created, already deleted, or landed on main under other names. There is **no `maps` branch** — the map-extraction question resolves against `xavi` + `auto/preserved-wip-260520`, both of which only touch `river-zeta-ww3` (see below).

The real inventory of non-main branches:

| Branch | Tip date | ahead / behind main | Verdict |
|---|---|---|---|
| `auto/forest-concealment` | 2026-07-28 | 0 / 45 | **SUPERSEDED/DEAD** — ancestor of main (fully merged) |
| `auto/stance-cover-positioning` | 2026-07-28 | 0 / 38 | **SUPERSEDED/DEAD** — ancestor of main (fully merged) |
| `auto/preserved-wip-260520` | 2026-05-20 | 1 / 359 | **KEEP-PARKED** — 1 unverified WIP commit; map edit superseded, scatter refactor salvageable |
| `mac-autoburn-260521-unpushed` | 2026-05-22 | 125 / 359 | **CHERRY-PICK CANDIDATES** — see deep audit; ~10 commits worth pulling, bulk is junk/superseded |
| `origin/xavi` | 2025-04-21 | 2 / 1361 | **SUPERSEDED/DEAD** — 2025 river-zeta authoring, fully rebuilt on main |
| `feature/lobby-redesign` | 2026-05-11 | — | **SKIPPED** — active worktree work (per brief) |

Fork/reset context: `auto/preserved-wip-260520` and `mac-autoburn-260521-unpushed` both fork from `10f0c9ce` (2026-05-19 "hotboard: restore automation workflow track bullet"). Main has advanced **359 commits** past that point — applicability of anything from these branches must be judged against heavy drift.

---

## Per-branch detail

### `auto/forest-concealment` — DEAD
`git merge-base --is-ancestor` confirms it is an ancestor of main: **0 commits ahead**, its own tip (`27c48099`) sits on main's history. Fully merged. Safe to delete.

### `auto/stance-cover-positioning` — DEAD
Same: ancestor of main, **0 commits ahead** (tip `5ab3c36b` on main). Fully merged. Safe to delete.

### `origin/xavi` — SUPERSEDED/DEAD
2 commits from 2025 (`3d2d54fb` "River zeta finished", `6e2aab03` "riverz wip"), merge-base `d56d358a` (2024-08-27). Only touches `mods/ww3mod/maps/river-zeta-ww3/` (map.yaml/bin/png/shadows.bin). This is the **original 2025 authoring** of river-zeta-ww3. Main carries a completely rebuilt river-zeta (map.yaml 14067 lines, last touched 2026-05-11 `0fa152f1`, plus a live `shadows.bin` regenerated 2026-07-28). Nothing on xavi is unique or newer. **No assets to extract** — the map it built already exists on main in far more advanced form. Remote branch; leave or delete at will.

### `auto/preserved-wip-260520` — KEEP-PARKED
Single commit `4a6f6394` (2026-05-20) "wip: preserve uncommitted changes before autoburn run". Three edits, verified against main:
- **`GroupScatterHotkeyLogic.cs`** — 84/89-line refactor (redistribute only the shared waypoint tail). Substantially diverged from main; self-described as "not verified complete." *Salvageable but needs review — not a clean pick.*
- **`river-zeta-ww3/map.yaml`** — ~1238-line "tree-thinning" edit. Main's river-zeta content is from 2026-05-11 (`0fa152f1`), **older** than this May-20 edit, so the thinning pass is genuinely **not on main**. Both files are 14067 lines but differ in content. Whether it's wanted is a judgment call (commit says "likely intentional, not verified").
- **`WORKSPACE/EXPERIMENTAL_NOTES.md`** — absent on main; a historical 260510 doc describing already-shipped work. Low value.

Verdict: park it. The scatter refactor and tree-thinning are the only unique content and both are explicitly unverified. Nothing here is a confident clean pick.

---

## `mac-autoburn-260521-unpushed` — DEEP AUDIT

125 commits, 2026-05-20→22, from two autoburn rounds (260520 + 260521) bundled through `auto/all-final`. Merge-base `10f0c9ce`; main is 359 commits past it.

### Bucket counts (125 total)

| Bucket | Count | Cherry-pick value |
|---|---:|---|
| merge-plumbing (`merge auto/…`) | 27 | none (bundle scaffolding) |
| autoburn process reports/tracking docs | 25 | none (post-mortem artifacts) |
| docs / pitfall / claude.md / hotboard redirects | 15 | mostly superseded — CLAUDE.md heavily rewritten on main |
| perf | 12 | low — micro-opts, apply cleanly, not redone on main |
| dead-code removal | 12 | low — textually fragile after 359 commits |
| yaml fixes | 8 | **mixed — 3 real bugs still on main** |
| console-cleanup (`Console.WriteLine`→`Log.Write`) | 7 | low — hygiene, conflict-prone |
| tests | 6 | medium — some superseded by differently-named tests, verify each compiles |
| null-safety NRE guards | 5 | **HIGH — 4 confirmed still-applicable crash fixes** |
| trivial (comment typos / warnings) | 4 | negligible |
| CHANGELOG.md | 1 | medium (but now 359 commits stale) |
| wip/scaffold (test-dr-jams-drone) | 2 | none — scaffold, resolved GREEN, no fix needed |

**~52 of 125 commits (merge-plumbing + reports) are pure process junk with zero code value.** Another ~30 (docs/dead-code/console/trivial) are low-value and conflict-prone against a 359-commit-advanced main. The genuine signal is concentrated in the null-safety, yaml-fix, and CHANGELOG buckets.

### Superseded vs. genuinely lost (verified against main)

**Genuinely LOST and still-applicable (main never re-implemented):**
- Null-safety guards — verified main still has the *unguarded* access at each site:
  - `AutoTarget.cs:1013` — `self.Owner.FrozenActorLayer` unguarded (NRE for Neutral/no-FrozenActorLayer players)
  - `SpawnActorOnDeath.cs:103` — `e.Attacker.Owner` unguarded
  - `PlayerStatistics.cs:231/234` — `e.Attacker.Owner` unguarded
  - `BuildingRepairBotModule.cs:33` — `e.Attacker.Owner` unguarded
- YAML content bugs — verified still present on main:
  - `vehicles-russia.yaml:839` tunguska `Armaments: primary, tertiary` (dangling `tertiary` ref — functional)
  - `vehicles-america.yaml` m113 `AmmoPools: …, tertiary-ammo` (dangling ammo pool refs — functional)
  - `vehicles-russia.yaml:146` bmp2 `Name: BPM-2` (cosmetic tooltip typo)
- `CHANGELOG.md` — absent on main entirely.
- perf micro-opts — main's `Armament.cs` still has no AmmoPool caching; the LINQ→foreach/iterator and per-tick `ToArray()` removals were not independently redone.

**SUPERSEDED / partial (already handled differently on main):**
- A10 `ReloadAmmoPool@1`→`@2` (`9404bc93`) — main's A10 block already uses `@2`; fix redundant.
- SupportPowerDecision guard (`abf1d9f4`) — sibling site `SupportPowerBotModule.cs:156` already null-guards `FrozenActorLayer` on main; likely redundant/partial.
- docs/pitfall/claude.md promotions (15) — CLAUDE.md was substantially rewritten on main (new routing table, mode docs); these would conflict and their content is largely re-derivable. Treat as superseded.
- tests (6) — `SupplyRouteContestation`, `CaptureCoordinator`, `SupplyProvider` math is now covered by differently-named tests on main (`SupplyRouteEliminationTest.cs`, `UnitRoleResolverTest.cs`, `BotEarlyGameMathTest.cs`); `AbsorbsSupplyCache`/`HuskDecay`/`ThreatMapManager` tests are absent but their traits may have moved — verify each compiles before pulling.

### Shortlist — top cherry-pick-worthy commits

Ordered by value. All touch stable, specific lines and apply cleanly *conceptually* despite the 359-commit drift (they were re-verified against current main above).

| # | SHA | What it brings | Applies to main now? |
|---|---|---|---|
| 1 | `53d4ad72` | AutoTarget: null-guard `self.Owner.FrozenActorLayer` in ChooseTarget (NRE crash fix) | **Yes — :1013 still unguarded** |
| 2 | `5ff217a9` | SpawnActorOnDeath: `e.Attacker?.Owner` guard (NRE crash fix) | **Yes — :103 still unguarded** |
| 3 | `d5014833` | PlayerStatistics: guard `e.Attacker` in Killed (NRE crash fix) | **Yes — :231/234 still unguarded** |
| 4 | `f2a0aa10` | BuildingRepairBotModule: guard `e.Attacker` in RespondToAttack (NRE crash fix) | **Yes — :33 still unguarded** |
| 5 | `0e3858d2` | tunguska: `Armaments: primary, tertiary` → `primary, primary-air` (dangling armament) | **Yes — :839 still broken** |
| 6 | `b1ca86a3` | m113: drop `Rearmable`/AmmoPools refs to nonexistent `secondary-ammo, tertiary-ammo` | **Yes — dangling refs still on main** (confirm exact block) |
| 7 | `7ba39a62` | Add `CHANGELOG.md` (~1538 commits since RA release-20230225) | Clean add (new file), but **now ~359 commits stale — regen preferred over raw pick** |
| 8 | `b8778902` | bmp2 tooltip `BPM-2` → `BMP-2` (cosmetic) | **Yes — :146 still typo'd** |
| — | perf batch: `73320bb3` `87e6d76b` `0a9e48b2` `10d7df19` `8a6fe3cc` `6593ae4a` `009fd843` `9634d0a8` | hot-path LINQ→foreach/iterator, per-tick `ToArray()` removals, Armament AmmoPool cache | Yes but micro-value; pick only if doing a perf pass |

**Recommendation:** cherry-pick the 4 null-safety NRE guards (#1–4) as a single cluster — they are genuine crash-safety fixes, all still unguarded on main, all touch isolated lines (near-zero conflict risk). Pick the two functional YAML fixes (#5–6) with a quick block-context check. Treat #7 (CHANGELOG) as "regenerate on current main" rather than a raw pick. Everything else on this branch — the 52 process-junk commits, the docs/pitfall promotions (superseded by main's rewritten CLAUDE.md), the dead-code/console-cleanup hygiene (conflict-prone, re-runnable via a fresh autoburn), and the tests (mostly superseded) — is **not worth the cherry-pick friction**. If the hygiene passes are still wanted, re-run autoburn against current main rather than untangling May's bundle.

---

## One-line verdicts

- `auto/forest-concealment` → **DELETE** (merged ancestor)
- `auto/stance-cover-positioning` → **DELETE** (merged ancestor)
- `origin/xavi` → **DELETE/ignore** (2025 river-zeta, fully rebuilt on main, no unique assets)
- `auto/preserved-wip-260520` → **KEEP-PARKED** (1 unverified WIP: scatter refactor + tree-thin edit; map otherwise superseded)
- `mac-autoburn-260521-unpushed` → **CHERRY-PICK** the 4 NRE guards + 2 YAML fixes; regen CHANGELOG; discard the rest
- `skane` / `maps` / `bypass` / `counterbattery` / `speed` → **DO NOT EXIST** (no ref found)
