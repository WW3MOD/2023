# RESULT — item-24 gate-enablement A/B (RUN 2026-07-29)

**Status: RUN COMPLETE.** Executes the plan `WORKSPACE/plans/260729_item24_ab_plan.md`
(`77dbfb7d`). Prices whether item-24's belief-side capture/garrison repoint (merge
`646515bd`) should be enabled by default on the `@experimental` bot. 40 measured matches
(both arms fresh — reuse VOID, see §Reuse), 0 crashes, 0 no-verdict, 0 wall-culls. This
card is the distilled record; raw per-match JSON/debug logs live (git-ignored) under
`tools/autotest/tournament-results/item24_arm{A,B}_s{1,2}/`.

**Recommendation: KEEP OFF.** The gates are a *measured behavioral no-op* on this
instrument/scenario set (Arm B byte-identical to Arm A across all 20 matches) while
carrying a real per-tick cost. Per the plan's decision rule (pure wash → tie → OFF) the
benchmark does not support default-on. **No ai.yaml change committed — the commit decision
is deferred to the user** (§Action).

---

## Instrument (the "zero")

| | |
|---|---|
| **Functional SHA** | `2bf335cf` — last commit touching `engine/**` or `mods/ww3mod/rules/**` at run start. |
| Per-rung stamped SHAs | Arm A S1 `a885a141` (dirty=true, item-24-OFF edit) · Arm A S2 `541be058` (dirty=true) · Arm B S1/S2 `d80b750b` (dirty=false, committed gates-ON). |
| Instrument constancy | **Byte-constant across the whole A/B window** — `git log` between all four stamped SHAs shows **0** commits touching `engine/**` + `mods/ww3mod/rules/**`; the SHA drift is docs/recon commits by concurrent workers in this shared checkout. Both arms sat on the identical gameplay instrument ⇒ the A/B is unconfounded. |
| Engine binary | rebuilt `make all` at run start (0 warnings/errors); verified no `.cs` newer than `OpenRA.Mods.Common.dll`. |
| Profile | `--background` (SDL_WINDOW_HIDDEN) every match — no focus steal. |
| Config | `--config` per scenario (`tournament-eco-5min.yaml` 300s/8× S1 · `tournament-combat-12min.yaml` 720s/8× S2). |
| Deviations | Wall caps 300s (S1) / 600s (S2) — the same measurement-neutral values the ladder settled on under this shared checkout's load (watchdog only culls on PID death; sim identical). `--config` added (no scenario ships a default `tournament.yaml`). Neither alters the measurement. |

### Reuse decision — VOID (both arms run fresh, 40 matches)

The plan's reuse optimization (run one new arm, reuse the ladder's Arm A for the other) was
evaluated and **rejected on two independent grounds**:

1. **Instrument moved (plan P2 / C-3 fails).** The item-25 ladder (`260728_rebaseline_result.md`)
   measured its Arm A at functional SHA `e5b7bbcc`. Six commits touching `engine/**` +
   `mods/ww3mod/rules/**` landed since: `631c9bad` (null-safety NRE guards), `07aed0ae`
   (dangling ammo/armament ref fixes), `58d77760` (group-scatter prefix), `818ac2cf`
   (executor B1 walk-back), and merges `23398408`, `2bf335cf`. Reuse requires the reused
   arm's instrument SHA be byte-identical to the new arm's — it is not. (Empirically
   confirmed: fresh Arm A drifted from the ladder's Arm A on several seeds — e.g. S1 seed 2
   `−1650 → +1350`, seed 4 `−2350 → +550`, seed 6 `−1500 → −3550` — so the ladder numbers
   are genuinely stale.)
2. **The reuse table's premise did not hold anyway.** The plan's table keys on "post-ladder
   `main` gate state" under the assumption the ladder's Arm A ran `main` as-shipped. It did
   not: the ladder (per its own documented deviation) **forced item-24 gates OFF for BOTH
   its arms** to keep item-24 out of the item-25 zero. So the ladder's Arm A ≡ *this card's
   Arm A* (gates OFF), not Arm B — mapping reuse to Arm A regardless of the committed ON
   state.

Both paths lead to: reuse VOID ⇒ **run both arms fresh**.

---

## Calibration consumed (the noise band) — from the ladder gate (a), NOT re-run

| Rung | Stable-vs-Stable calibration (ladder `260728`) |
|---|---|
| **S2 combat** | net swing median **−225**, band ≈ **±$2000**/match (slight P2-slot lean); engaged **7/10** organic (the 6/10 validity floor sits at the organic rate); win-rate noise ≈ ±2/10 |
| **S1 eco** | capture gross **P1 6188 / P2 2976** (≈2× P1-slot spawn-capture bias the `--mirror` cancels); win 4–6 |

---

## Arms (both fresh, `@experimental` vs `@stable`, paired seeds 1017…10017, even=primary/odd=mirror)

- **Arm A — gates OFF** (`StrategicCaptureRepointEnabled: false` AND `DefendRepointEnabled:
  false`, temporary UNCOMMITTED working-tree flip, reverted after the arm). ≡ pre-item-24
  omniscient capture/garrison behavior.
- **Arm B — gates ON** (both `true`, committed state, clean tree).
- `@stable` twins carry neither key ⇒ byte-identical across arms ⇒ both arms share the one
  ladder calibration.

### MEASURED — per-rung medians (N=10 each)

| Rung | Arm | Win (Exp–Opp) | Exp net swing (median) | Capture Exp/Opp | Engaged | Notes |
|---|---|---|---|---|---|---|
| **S1 eco** | A (OFF) | 3–7 (0.30) | **−1125** | 4/10 vs 4/10 | 10/10 | gross mean 2520; earned med 0 |
| **S1 eco** | B (ON)  | 3–7 (0.30) | **−1125** | 4/10 vs 4/10 | 10/10 | gross mean 2520; earned med 0 |
| **S2 combat** | A (OFF) | 4–6 (0.40) | **−1275** (spread [−3950,+800]) | 4/10 vs 4/10 | 10/10 | both-spawn primary −1550 / mirror −1050 |
| **S2 combat** | B (ON)  | 4–6 (0.40) | **−1275** (spread [−3950,+800]) | 4/10 vs 4/10 | 10/10 | both-spawn primary −1550 / mirror −1050 |

### MEASURED — the B − A delta

**Arm B is byte-for-byte identical to Arm A.** All 20 match verdicts (the entire `notes`
blob: winner, kills_cost/deaths_cost, capture_income_gross, resources_earned) are identical
between arms; even the logged capture `poimap-scan` and garrison score lines match with 0
diff. Paired per-seed B − A swing = **0 on all 10 seeds, both rungs** (sign +0/10 −0/10,
zero 10/10). **B − A = 0 on every metric.**

---

## Firing proofs (arms proven live/dead, not assumed)

**MEASURED:**
- Arm A gate values `false`/`false` (grep, live during run; stamped dirty=true).
- Arm B gate values `true`/`true` (grep; committed; stamped dirty=false).
- `DangerFieldLayer` is registered on the world **ungated** (`world.yaml:363`) ⇒
  `world.WorldActor.TraitOrDefault<DangerFieldLayer>()` is non-null.
- Influence stack live in Arm B: `[exp-terr]` 2708 lines + `[exp-garrison] reeval` 3600
  lines (S2); capture `poimap-scan` 14 lines (S1, lightly exercised — few TECN scans).
- Arm B verdicts + logged capture/garrison scores byte-identical to Arm A.

**INFERRED:**
- The repoint branch **executed** in Arm B: `repoint = StrategicCaptureRepointEnabled(true)
  ∧ dangerField(≠null) = true` (`CaptureCoordinatorBotModule.cs:566`; garrison mirror
  `PoiGarrisonBotModule.cs:236`), both conjuncts measured above. **No dedicated runtime
  firing marker exists** — the rescale methods (`RescaleCaptureByBelievedDanger` :578 /
  `RescaleDefendByBelievedDanger` :374) emit no `Log.Write`, and `poimap-scan`/`reeval` fire
  in both arms — so the branch-taken cannot be directly measured; it is inferred from the
  two measured conjuncts plus the review's off-gate byte-identity proof.
- The repoint fired but was **numerically inert**: with the well-exercised garrison path
  (3600 reevals) producing byte-identical scores, the believed `GroundDanger` sampled into
  the SAFE/CALM bucket (multiplier ×100 = no reorder) at every scored capture target and
  held POI across all 20 matches — consistent with the ladder's finding that the
  `[exp-terr]` believed-danger repoint "fires but is behaviorally quiet on these maps."

---

## Decision rule (applied mechanically)

Noise band: S2 swing ±$2000; S1 win-rate floor 0.40, capture tolerance −1/10.

**S2 (garrison gate):**
- `swing_B (−1275) ≥ swing_A − noise (−1275 − 2000 = −3275)` → **TRUE**
- `engaged_B (10/10) ≥ 6/10` → **TRUE**
- Non-harmful, but Δ = 0 → **no measured edge** (`swing_B > swing_A` fails: equal).

**S1 (capture gate):**
- `capture_B (4/10) ≥ capture_A − 1/10 (3/10)` → **TRUE**
- `win_B (0.30) ≥ 0.40` → **FALSE** (win-floor breach)
- `resources_earned_B (0)` not materially below Arm A (0) → **TRUE**
- ENABLE condition **FALSE** (win floor).

**Sign-disagreement between rungs?** No — Δ = 0 on both rungs (no sign at all) ⇒ **not a
split-gate case.**

**Outcome:** B − A ≡ 0 everywhere ⇒ "within-noise / neutral on both → KEEP OFF"; the plan's
tie-break ("a pure wash does not clear the bar for keeping complexity on by default") applies
directly. The repoint carries a real per-tick cost (item-24 review OBS: capture path runs
suppress+rescale twice per tick; garrison consumes threat twice) unjustified by any measured
benefit.

### VERDICT: KEEP OFF (recommendation)

item-24's belief-side capture/garrison repoint is a **measured behavioral no-op** on the
River-Zeta S1/S2 rungs at N=10 — it fires but never changes an ordering outcome — so the
benchmark earns no default-on. Leave item-24 dead-but-shipped pending a scenario where the
believed danger field is non-trivial at capture/defend targets (e.g. a forced-contact rung
where held POIs sit inside a believed weapon envelope).

**Caveat (do not misread):** the S1 win-floor breach (0.30 < 0.40) is **inherited from the
Arm A Exp-vs-Stable baseline deficit** (the item-25 rebaseline's core finding: Exp
underperforms Stable on both rungs post-26/28), **not caused by item-24** (Δ = 0). item-24
does not itself regress either rung; it simply does nothing here. Both the wash-tie route and
the win-floor route land on OFF.

---

## Action taken

- **NO ai.yaml change committed.** The verdict is advisory; the commit decision (revert
  gates to `false`, or leave committed ON, or re-scope) is deferred to the user.
- Working tree restored to the committed gates-ON state after Arm A (`git checkout --
  mods/ww3mod/rules/ai/ai.yaml`); verified `ai.yaml:180` `StrategicCaptureRepointEnabled:
  true`, `:309` `DefendRepointEnabled: true`, `:329` `RequiresCondition:
  enable-ai-experimental`, `git diff` on ai.yaml empty.
- Only this result card (and a one-line `LADDER.md` status note) are committed.

## Result-dir map (git-ignored raw)

| Rung | dir | stamped SHA |
|---|---|---|
| Arm A S1 | `item24_armA_s1` | `a885a141` (dirty) |
| Arm A S2 | `item24_armA_s2` | `541be058` (dirty) |
| Arm B S1 | `item24_armB_s1` | `d80b750b` (clean) |
| Arm B S2 | `item24_armB_s2` | `d80b750b` (clean) |
