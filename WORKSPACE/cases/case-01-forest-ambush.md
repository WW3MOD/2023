# Case 01 — Forest ambush: defenders win decisively from concealment

**State: DRAFT** (dependencies not yet landed — see below)

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

## Dependencies

- Pipeline item 20 (recon: terrain concealment mechanics) — gates everything.
- Pipeline item 21 (stance-aware cover positioning) — gates BUILDABLE.
- A test-run grant for the calibration batch — user-gated, per CLAUDE.md hard rule.

## Status log

- **2026-07-28 — Dependency 1 RESOLVED (pipeline item 20 done).** Trees DO conceal mechanically (density→shadow vision attenuation) but weakly: ~1 strength per fully-dense tree cell on the sightline; stock infantry (Vision 3) needs ~7 such cells to every viewer. Deep stacked forest works, thin treelines don't. Case is buildable as painted **if** the map uses a genuinely dense cluster, but the ambush likely needs concealment strengthening to read reliably — seams mapped in `WORKSPACE/recon/260728-trees-concealment.md` (vision-attenuation retune, `Detectable` terrain bump, dormant `TerrainModifiesDamage` for cover). Remaining gates: item 21 (positioning) + test grant.
- **2026-07-26 — DRAFT.** Intent captured from the user's discussion turn; decomposition + provisional bar recorded. No scenario authored yet.
