# Case 01 — Forest ambush: defenders win decisively from concealment

**State: CALIBRATING** (scenario authored + first calibration batch measured 2026-07-28 — pipeline item 22)

## Intent (user's description, 2026-07-26)

> A scenario where a bunch of soldiers are laying in wait to ambush, and when an equal sized force walks into their trap we should clearly be able to measure that the defenders win. There is a small forest / group of trees, on one side is the defenders, they are ordered to go to the trees with a single click/order, they naturally take positions in those trees based on their stances, and with that single order the units automatically take good positions. If they are in ambush stance they try to position themselves to remain hidden for example. The other team then advances and tries to take the position, but we should expect them to be destroyed with 3x the casualties of the defenders.

## Decomposition (agent, 2026-07-26)

The case bundles three separable pieces; only the first partially exists:

1. **Concealment/detection** — graded vision rings vs `Detectable.Vision` exist (see `DOCS/reference/influence-stack.md` context + architecture.md §Suppression). **Unverified: whether trees/terrain modify detection at all.** The Stage-3 ambush scenarios had to override `Detectable: Vision: 9` to author a hidden unit — evidence that terrain-based concealment may not be mechanically real yet. If trees don't hide units, this case is unbuildable as painted. → recon item (pipeline 20).
2. **Stance-aware cover positioning** — NEW feature. Today a group order places units by formation logic; the case needs "one order at the trees → each unit picks a good cell, ambush-stance units prefer concealed cells." Composes with the shipped stance system (`StancePositioningExecutor`, cohesion Loose=cover identity) and the ambush stance machine (Stages 1–3). → feature item (pipeline 21).
3. **The measurement** — scripted attacker (deterministic advance into the trap) vs defender group under test; aggregate cost-weighted losses over N seeded runs.

## Setup (to be authored once BUILDABLE)

- Small map, one tree cluster; defender squad (infantry, ambush stance) given ONE order to the treeline; attacker squad of equal cost, scripted attack-move through the cluster after defenders settle.
- Attacker scripted, not bot-driven — isolates defender positioning + ambush spring as the variable under test. A bot-attacker variant can come later as a separate case.
- Harness: `run-test.sh` scenario per `DOCS/recipes/AUTOTEST.md`; batch with `--hidden --seed` derivatives.

## Bar

**PROVISIONAL: defender:attacker cost-weighted casualty ratio ≥ 1:3** (attacker loses ≥3× the value the defender loses), aggregated over N runs (N decided at calibration; suggest 5+).

NOT ratified. First calibration batch measures what the combat math actually yields — first-strike alpha at point-blank may make 3× trivially easy, or armor/DPS asymmetries may make it impossible. Ratify (possibly adjusting the ratio or the force composition) before the bar gates autoburn iteration.

**Where each clause is enforced** (2026-08-19 — a pointer, not a change; nothing measured here has been altered, and the ratification question below is still open):

| Clause | Level | Enforced by |
|---|---|---|
| Bar A — *mean def ≤ 50cr* | batch (a MEAN over ≥6 seeds) | `tools/autotest/parse-case01-bar.py` |
| Bar A — *mean att ≥ 300cr* | batch (a MEAN over ≥6 seeds) | `tools/autotest/parse-case01-bar.py` |
| Bar B — *every seed def = 0* | per seed | `test-case01-forest-ambush.lua` — `Test.Pass`/`Test.Fail` |
| *(setup validity: item-21 seated 5/5, ambush engaged)* | per seed | same, checked **before** Bar B |

**The two Bar A clauses are deliberately NOT collapsed into the per-run test.** A per-seed `attLoss ≥ 300` is a stricter and different bar than the one written: the 2026-07-28 batch Bar A was mined from — the batch being offered for ratification as GREEN — contains seed 5005 at attLoss=200 and two seeds at exactly 300. Gating each run on the mean would call that reference batch RED.

**Still open, and still the user's call:** whether to ratify Bar A (+B) at all. The scenario now gates on Bar B because it is the only clause a single seed can decide; `BAR_B_DEF_MAX_LOSS` at the top of the Lua is the one constant to change if the answer is different.

## Dependencies

- Pipeline item 20 (recon: terrain concealment mechanics) — gates everything.
- Pipeline item 21 (stance-aware cover positioning) — gates BUILDABLE.
- A test-run grant for the calibration batch — user-gated, per CLAUDE.md hard rule.

## Status log

- **2026-08-19 — The scenario can now go RED (code change only; NO runs, bar unchanged, still CALIBRATING).** `test-case01-forest-ambush.lua:220` called **`Test.Pass(note)` unconditionally** from a bare `Trigger.AfterDelay`, so the case could not fail — and therefore its green carried no information (audit: `WORKSPACE/audit/260819-case-corpus-audit.md` §3). Replaced with a two-stage conditional verdict: **setup-validity first** (item-21 seated 5/5 in cover, and ≥1 attacker died), because `defLoss == 0` is *also* what a world that never happened produces — an inert run where the attackers never engage would otherwise report PASS, which is the false-green shape `AUTOTEST.md` warns about. Then **Bar B** (`defLoss ≤ 0`) decides Pass/Fail with a message naming the real failure. Bar A's two MEAN clauses cannot be decided by one run and are enforced by the new `tools/autotest/parse-case01-bar.py` over a ≥6-seed batch (it also refuses to evaluate an under-sized batch, a batch with a repeated seed, or a batch containing a SETUP-INVALID run — each reports UNEVALUABLE, never green). The checker was proved offline against the 2026-07-28 table (GREEN, mean 0/350) and against a defenders-die batch (RED); no game was launched. **Nothing measured was re-authored**, and no bar is ratified. RED/GREEN certification of the scenario itself is still owed — sabotage is `FogCheckboxEnabled: False` + `FogCheckboxLocked: True` in `rules.yaml` (`MapLayers.IsVisible` short-circuits `if (!FogEnabled) return map.Contains(puv)`, making every defender detectable and removing exactly the detection asymmetry the case measured). Noted while verifying: **lowering `Detectable.Vision` would NOT work as a sabotage** — it clamps to a floor of 1 and the comparison is strictly `visibility > threshold`, so the measured `vis=1` still fails `1 > 1` and the defenders stay hidden; that control would have falsely passed.

- **2026-07-29 — Batch re-mined for ratification (analysis only, no new runs; `main` @ `b3d5d7e1`).** Deep-mined the 2026-07-28 batch artifacts → `WORKSPACE/plans/260729_case01_bar_mining.md`. Data is thin by design: the harness clobbers a single `result.json` per run and never archives `debug.log`, so only seed 6006's raw verdict survives (it corroborates the transcribed table exactly); the case-01 Lua never emitted per-unit events, so kill-timestamps / time-to-first-shot / return-fire counts were never measured. Findings: defender loss = 0/30 (zero variance); attacker loss WIDE — kills {4,3,5,4,2,3}, mean 3.5, σ≈1 kill, range 2–5, floor 200cr; only 1/6 seeds fully resolved (@83s), consistent with an early-burst-then-stall kill-curve. The defender edge is confirmed to be **detection asymmetry** (attackers can't acquire, ~0 return fire), not a fair-fight cover win. **Refined bar-ADJUST (supersedes the coarse recommendation below):** ratify **Bar A** — *mean def cost-loss ≤ 50cr AND mean att cost-loss ≥ 300cr over ≥6 seeds* (this batch GREEN: 0 / 350) — with optional per-seed hard guard **Bar B** *every seed def = 0*; teeth sit on the zero-variance defender axis, attacker clause kept soft (noisy). Fire-lane axis delegated to `test-case01b-detect`. See the plan for the regression-mode coverage table.

- **2026-07-28 — CALIBRATING (scenario authored + first calibration batch, main @ `57d88a74`).** Scenario `tools/autotest/scenarios/test-case01-forest-ambush/`. Forces: 5× `e3.america` (USA, **HUMAN**, Ambush stance, `enable-ambush-tactics` granted from Lua, Loose cohesion) vs 5× `e3.russia` (scripted attack-move). **Exact cost parity**: 500cr each (both inherit `^E3`, `Valued.Cost 100`). Map is a "kill-clearing": a solid 3-row t01 concealment WALL (trunks y10–12) north, a deep open CLEARING (y13–19), and a 2-row COVER PATCH (trunks y20–21) where item-21 seats the defenders (~y22). The ONE group `Move` order goes through `Test.GroupMove` → the real `IModifyGroupOrder` pipeline, so the item-21 ambush branch fires (gate isHuman+Ambush+non-Tight all set before the order; every run `refined=5/5`, densWin=100, attacker-visibility of defender = 1 → **undetectable**). Calibration batch — 6 seeds, `--hidden`, 90 s combat deadline:

  | seed | def loss (cr) | att loss (cr) | att killed | def killed | resolved |
  |------|------|------|------|------|------|
  | 1001 | 0 | 400 | 4/5 | 0/5 | no (deadline) |
  | 2002 | 0 | 300 | 3/5 | 0/5 | no |
  | 3003 | 0 | 500 | 5/5 | 0/5 | **yes @83s** |
  | 4004 | 0 | 400 | 4/5 | 0/5 | no |
  | 5005 | 0 | 200 | 2/5 | 0/5 | no |
  | 6006 | 0 | 300 | 3/5 | 0/5 | no |
  | **mean** | **0** | **350** | **3.5/5** | **0/5** | 1/6 |

  **Defenders lose NOTHING in every seed**; attackers lose 200–500cr (mean 350). Cost-weighted ratio is degenerate (÷0) — attacker loses effectively ∞× the defender's value. **Bar recommendation: the provisional ≥1:3 ratio is TOO EASY / ill-posed → ADJUST.** The defender edge is a *detection asymmetry* (concealed defenders read vis ≤ 1 < `Detectable.Vision 3`, so attackers can't acquire them → take ~0 return fire), NOT a fair-fight combat edge: a discarded COMPACT-clearing variant that let attackers detect defenders at ~5c had defenders **lose** on 2 of 3 seeds (ratio 0.33 / 0.50) — `DensityModifiesDamage` (≤20%) + first-strike do not win a symmetric close brawl. Recommend replacing the ratio with a well-posed decisive-win bar, e.g. **"mean defender casualties ≤ 0.5/5 (≤10% cost) AND mean attacker casualties ≥ 3/5 (≥60% cost) over ≥5 seeds"** — this batch: def 0/5, att 3.5/5 → PASS. Caveat: the win depends on defenders staying undetected (deep seating); item-21's viewer-independent max-density seating helps here but is not a combat-superiority margin. Findings → `WORKSPACE/DISCOVERIES.md` (2026-07-28). No engine changes; scenario/harness only.

- **2026-07-28 — BUILDABLE (pipeline item 21 merged @ `5c6cc1f0`).** Last dependency resolved: stance-aware cover positioning shipped — an ambush-stance squad given ONE group order at a tree cluster now has each formation slot refined to the best concealed cell (radius 3, `ForestGroundShadow` scoring, strict-improvement margin, conflict-free, deterministic). Bots byte-identical (human+Ambush+non-Tight gate); open terrain byte-identical. Review GREEN-WITH-NITS, both nits resolved; NUnit baseline 499. Post-merge verify on main: build 0 errors, 499/499. **Case is now buildable exactly as painted in Setup** — next: author the scenario + calibration batch (item 22, batch covered by the STANDING GRANT).
- **2026-07-28 — Concealment strengthened (pipeline item 26 merged @ `fc9fe396`).** The weak-concealment concern from the item-20 recon is addressed: superlinear ground shadow curve (`Map.ForestGroundShadow` — density-10 cells now ladder 1→1, 2→2, 3→4, 4→6, 5→8, 6→10 strength; 4 dense cells hide stock infantry at 13–16c, 6 hide until point-blank) plus new `DensityModifiesDamage` cover trait on `^Infantry` (3×3 density window: 15→94%, 30→88%, 50→80% damage taken). Both maps' `shadows.bin` regenerated. NUnit baseline now 485. The test-run grant is covered by this window's STANDING GRANT. **Remaining gate: pipeline item 21 (stance-aware cover positioning) — dispatched next.**
- **2026-07-28 — Dependency 1 RESOLVED (pipeline item 20 done).** Trees DO conceal mechanically (density→shadow vision attenuation) ~~but weakly: ~1 strength per fully-dense tree cell on the sightline; stock infantry (Vision 3) needs ~7 such cells to every viewer.~~ Deep stacked forest works, thin treelines don't.
  - _**Correction 2026-08-19:** the struck figures came from `recon/260728-trees-concealment.md`, which is now **partly refuted** — see the banner at the top of that file. Two errors compounded in the same direction: the curve went superlinear the same day (the very next log entry above records it — **3–5 cells, not ~7**), and the recon wrongly claimed **"prone grants nothing"** when `DetectableAddativeModifier@Prone` (`infantry.yaml:716-718`) gives `+1` and fires automatically on `!moving` (`:294`). **A halted ambusher is therefore concealed by fewer trees than either number suggests.** This did not corrupt the case's calibration results below — those were measured, not derived, and they record defenders reading `vis ≤ 1` against `Detectable.Vision 3` directly._ Case is buildable as painted **if** the map uses a genuinely dense cluster, but the ambush likely needs concealment strengthening to read reliably — seams mapped in `WORKSPACE/recon/260728-trees-concealment.md` (vision-attenuation retune, `Detectable` terrain bump, dormant `TerrainModifiesDamage` for cover). Remaining gates: item 21 (positioning) + test grant.
- **2026-07-26 — DRAFT.** Intent captured from the user's discussion turn; decomposition + provisional bar recorded. No scenario authored yet.
