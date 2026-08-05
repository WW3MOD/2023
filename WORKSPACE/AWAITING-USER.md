# AWAITING-USER — decisions and reviews parked on the user

> **Purpose:** the single place where everything **dependent on the user's decision, review, or grant** is parked so nothing is lost or forgotten. The manager adds items (dated, with context + exactly what is needed); the user resolves them in chat or by editing this file. Resolved items move to the bottom with the decision recorded. Complements `PIPELINE.md` (the work queue) — this file is the *user-gate* queue.

## OPEN — needs a user decision

### Balance / measurement
- **Balance proposals 001–003 — per-proposal sign-off** *(2026-08-02, audit merged @ 660a0ee2)* — Audit complete (`WORKSPACE/balance/260802-parity-audit.md`): 3 CLEAR asymmetries, each with a proposal doc awaiting individual approval. **NO stat change applied without explicit approval.**
  - `001-tunguska-duplicate-health.md` — Tunguska has TWO `Health:` keys (14000 and 8000) in the same actor; delete the 8000 block.
  - `002-himars-iskander-parity.md` — Iskander strictly dominates HIMARS at equal cost 6000; Option A (recommended): Iskander → 8000.
  - `003-mi28-secondary-air.md` — Mi-28 advertised AA is non-functional (`secondary-air` armament undefined); define it, or remove refs + fix description.
  - Also flagged: 8 SUSPICIOUS stat ratios + 2 bot-config skews (A1: RU 7 attack helis vs US 4; A2: offense ceilings US 35 vs RU 45) that can masquerade as faction imbalance in bot-vs-bot data.
- **Cross-faction + RU-mirror test batches** *(2026-08-02)* — Configs AUTHORED and merged (`tools/autotest/scenarios/tournament-parity-{mirror-us,mirror-ru,cross-usru,cross-usru-swapped}`); **runs need a user grant**. Commands: `run-tournament.sh tournament-parity-mirror-us --seeds 20` / `…-mirror-ru --seeds 20` / `…-cross-usru --seeds 20 --mirror tournament-parity-cross-usru-swapped`. Metrics: mirror winner_name deviation from 50/50 = side bias; cross pair faction_winrate_pct. NOTE: run only AFTER the burn-lane merges settle, and bot-config skews A1/A2 mean cross results partly measure ai.yaml asymmetry, not just unit stats.
- **Post-merge benchmark goahead** *(2026-08-02)* — Once the burn lanes merge, a fresh benchmark (new Experimental vs Stable **0802**) re-baselines the improvement campaign. Needs goahead (multi-test rule).
- **Case-01 bar ratification** *(2026-07-28)* — The 1:3 cost-ratio bar is ill-posed (÷0 when defender losses hit zero). Proposed reframe: "def casualties ≤ X AND att casualties ≥ Y over N seeds." Awaiting ratified numbers before iterating case-01 to GREEN.
- **Ambush gate (b) pricing** *(2026-07-29)* — Benchmark pricing of default-on ambush was lean-OFF but inconclusive at noise scale. Decision: price again with more seeds, or keep default-off and close.
- **Item-24 repoint gates disposition** *(2026-07-29; sharpened 2026-08-04)* — A/B (40 matches) showed byte-identical arms → KEEP OFF recommendation, but at HEAD the gates are committed **ON in BOTH profiles**: @experimental (`ai.yaml:159`/`:498`) **and @stable** (`:1216`/`:1294`, since stable-0802) — so the shipping baseline bot runs them and @stable is no longer byte-identical to its pre-item-24 form. Decide: flip to OFF (both profiles) or accept ON. Doc gap line updated in curation `a313b306`.

### Posted questions (in the dashboard, unanswered)
- **Tactical-layer default for humans** *(2026-08-04, proceeding on default)* — auto supply-seek / OOA evac ON by default for human units, stance-disableable (default, conf 85) vs OFF until tried in-game. The default-ON flip ships as its own one-line commit; redirect reverses it trivially.
- **OOA fallback** *(2026-08-04, proceeding on default)* — vehicle out of ammo with NO reachable rearm source: terminal evac + sell (default, conf 80) vs hold safe + periodic re-check.
- **Streak protocol** *(2026-07-31)* — what counts as one game in the 10-win streak.
- **Non-wins** *(2026-07-31)* — do draws / timeouts / crashes break the streak.
- **Queue handling** *(2026-07-31)* — do the streak campaign + fixes supersede the parked pipeline items.

### Housekeeping
- **LANDED auto-branch disposition** *(2026-07-29)* — merged branches `auto/may-salvage`, `auto/spread-prefix`, `auto/b1-walkback` left intact on origin; delete or keep.
- **Standing sims/tests state** *(2026-08-02)* — deferred until weekly budget is burned; conditional grant for leftover 5h-window time; full max-burn resumes at 5h reset.

## RESOLVED
_Move items here with the decision + date._

- **SR flow shape** *(posted 2026-08-04, DECIDED 2026-08-05)* — user picked the non-default arm: **"Advance immediately, singly — zero assembly anywhere; maximally responsive but arrives piecemeal into contact."** Implemented on `auto/spawn-flow` as `ImmediateReinforcementCommit` (`SpawnFlowMath.SuppressMassingHold`), which suppresses damper arm (b) — the fill-completion massing hold at the forward muster — and nothing else. Post-retreat dwell, `SectorPostureHold`, the free-pool forward stager and transport-fill waits stay live (the last per the fork record's own "legitimate either way"). @experimental-only; `@stable` byte-identical. Revert path: drop the single `ImmediateReinforcementCommit: true` line from `ai.yaml`.
