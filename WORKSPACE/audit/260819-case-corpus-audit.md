# Case corpus audit — 2026-08-19

**Audited against `main @ 8c8fd25a`** ("merge wt/packaging", 2026-08-19). Docs only; no runs, no
builds, no lint. Every claim below is either a file:line read at that SHA or a `git log`/`git show`
between `918bf38b` (2026-07-29, the case's last substantive edit) and HEAD.

**Scope note:** the brief asked for an audit of "every case file". There is exactly one.
`WORKSPACE/cases/` contains `README.md` and `case-01-forest-ambush.md`. That is itself the first
finding — see §5.

---

## 1. The table

| Case | Bar | Status | Last actually measured | Blocked on |
|---|---|---|---|---|
| **case-01** forest ambush (`cases/case-01-forest-ambush.md`) | **Provisional, superseded in the body.** Header `:23-27` still carries the ill-posed `≥1:3` cost ratio (degenerate: defender loss is 0, so ÷0). The live candidate is 14 lines down at `:37` — **Bar A**: *mean def cost-loss ≤ 50cr AND mean att cost-loss ≥ 300cr over ≥6 seeds*; optional **Bar B**: *every seed def = 0*. Neither is ratified, and **neither exists in code** (§3). | `CALIBRATING` — declared. **Not gating anything.** | **2026-07-28**, 6 seeds (1001–6006), `main @ 57d88a74`. def 0cr / att 350cr. The 2026-07-29 entry is a re-mine of those same artifacts — it states "analysis only, no new runs". **22 days stale; ~1000 commits since.** | (a) one user yes/no on Bar A(+B); (b) then a Lua gate change + RED/GREEN certification run. **Not** blocked on a feature, and no longer blocked on a run grant. |
| *(arm)* **case-01b-detect** (`tools/autotest/scenarios/test-case01b-detect/`) — the delegated fire-lane measurement, not a case in its own right | None. Capture-mode by design; its value is the metrics (`defFired=k/5`, `ttfShot`, `defShots`/`attShots`). | Authored `4846a60a` (2026-07-29), instrumented `44c2b513`. | **NEVER.** No numbers exist for it anywhere in the repo. | Nothing. One run, no dependencies, no ratification needed. This is the cheapest unclaimed number in the project. |

### Distinguishing declared from measured

- The only post-07-29 touch to any case artifact is `e232e3f4` (2026-08-19), a documentation
  strike-through. No scenario file changed.
- Apparent run artifacts are a false positive: `WORKSPACE/audit/logs-260816-snapshot/Logs/perf.log:68-69`
  lists `70 ms | test-case01-forest-ambush` and `70 ms | test-case01b-detect`. Those are **map-enumeration
  timings** from a startup listing of every scenario map, not run verdicts.
- So: case-01 is measured once, on 2026-07-28. case-01b is measured never.

---

## 2. Did today's merges move anything? No.

110 commits landed on 2026-08-19 (48 merge subjects). **None of them touch a mechanism case-01
rests on.** Verified individually across `918bf38b..HEAD`:

| Mechanism | Verdict | Evidence |
|---|---|---|
| `Detectable` detection | **UNCHANGED** | `fb56971b` (today) adds one `[GrantedConditionReference]` lint declaration to `DetectableInfo` and re-radiuses `WithRangeCircle`. Its "one tier too tight" error was in the **range-circle gauge**, which had never been switched on — not in detection. `fc626b1b` moves `[Sync]` from the condition token to `CurrentVisibility`; hash-only. `Detectable.Vision` still `3` (`rules/ingame/infantry.yaml:97` on `^Infantry`). |
| `DetectableAddativeModifier` ladder | **UNCHANGED** | Zero such lines in the `infantry.yaml` diff across the window. `@Prone` still `+1` at `:716-718`, still auto-fired by `ProneCondition: … \|\| !moving \|\| …` at `:294`. |
| `Map.ForestGroundShadow` curve | **UNCHANGED** | `Map.cs:1102`; ladder `1→1, 2→2, 3→4, 4→6, 5→8, 6→10` byte-identical. `1185a6aa` flattened the storage to `MapShadowLayer` with a 64,577,296-pair equivalence test and left `shadows.bin` format untouched. The case-01 map ships no `shadows.bin`, so its shadow data is computed at load. |
| `DensityModifiesDamage` | **UNCHANGED** | No commits on the trait; YAML still `15: 94 / 30: 88 / 50: 80`. |
| Cost parity | **UNCHANGED** | `^E3 Valued.Cost: 100`; both `E3.america` and `E3.russia` override only prerequisites and sprite. 500cr/side holds, so every cost-weighted number in the case is still denominated correctly. |
| item-21 order-time seating gate | **UNCHANGED** | `isHuman && stance == UnitStance.Ambush && mode != CohesionMode.Tight` — character-for-character identical, relocated `:1145`→`:1079`. `a4d85b0c`'s executor edit is the exact negation of the old inline condition (semantics-preserving as claimed); it touched three *other* stance scenarios, not case-01. |
| e3 weapon / health | **UNCHANGED** | `^5.56mm` and `5.56mm.DMR` untouched; window's weapon commits are helicopter/missile/FX. Adjacent balance moved `E4` cost only (`6d2848a0`). |

**Consequence:** a rerun today should reproduce def 0 / att ~350. But note this is *derivation* —
a diff audit, not a measurement. One run converts it. That is why a confirmation run is cheap and
worth doing (§4) rather than banking the diff audit as if it were a result.

---

## 3. The finding the pipeline verdict misses: **the bar has no teeth in code**

`PIPELINE.md:322-327` (verdict stamped today, `main @ 5890b053`) concludes *"AWAITING ONE USER
YES/NO, NOT MORE MEASUREMENT… No run is needed to get there."* That is correct **for ratification**
and I am not disputing it. It undersells what remains after ratification:

`test-case01-forest-ambush.lua:220` calls **`Test.Pass(note)` unconditionally**, reached from a bare
`Trigger.AfterDelay` with no predicate. The file is honest about it (`:14-16`: *"CALIBRATION MODE:
this test PASSES whenever the sim resolves"*), and the per-kill instrumentation added by `44c2b513`
is explicitly logging-only (`:33-37`). So:

- Ratifying Bar A changes a document. It does not change what the scenario asserts.
- Until the `Test.Pass(note)` is replaced with the Bar A predicate, **case-01 cannot go RED**, and
  therefore cannot go GREEN in any sense that means anything.
- Worse, in the aggregate it is currently a **false green**: `WORKSPACE/audit/260816-build-test-health.md:226-243`
  (finding F8) lists both `test-case01-forest-ambush` and `test-case01b-detect` among 7 scenarios
  that cannot fail but *are* counted by `run-batch.sh --all` (its filter matches on the presence of
  `Test.(Pass|Fail|Skip)`). ~10% of any reported `--all` pass tally is structurally guaranteed.

Per the project's standing RED-before-green discipline, implementing the gate needs a sabotage run
that confirms the *specific* Bar A fail text before the green is trusted. Budget that run.

---

## 4. Blocked on what — split by queue

| Item | Needs | Not |
|---|---|---|
| Ratify Bar A (+B) | **A user decision.** One yes/no. | Not a run. Not a feature. |
| Give case-01 teeth | **A code change** (Lua gate) + a RED run + a GREEN batch. | Not a user decision beyond the above. |
| case-01b first numbers | **One run.** | Not ratification, not a feature, not a grant. |
| Fire-lane question (does item-21's max-density seat block the defenders' own fire?) | **case-01b's numbers.** | Not a case-01 bar change — see §5, the bar-mining plan explicitly refuses to overload case-01 with fire-lane teeth it cannot measure. |

**Run grants are no longer the gate.** The 2026-07-28 window-scoped batch grant is spent, but it is
superseded by the full launch grant recorded at `AWAITING-USER.md:9` (2026-08-19), with authority
centralised in the manager (`PIPELINE.md:47-51`). Runs are now manager scheduling, not a user gate.

**Operational cautions for whoever executes the run list** (all new since the case was last run):
- `f3c7a29e` added an atomic `run.lock` at `${RESULT_DIR}/run.lock`. Runs must be strictly serial; an
  overlapping run exits 3. A lock dir with an empty `pid` file also exits 3 — that means a holder
  mid-acquisition; do **not** clear it by hand.
- `4116886b` gives each invocation its own result dir. `~/.ww3mod-tests/result.json` is now a
  `"status":"moved"` stub — reading it yields no verdict. Read the per-run dir.
- `debug.log` is still **not** archived. The per-unit data the 2026-07-29 pass wanted comes from
  opt-in `--lifecycle` (`.lifecycle.jsonl`, archived into the run dir), plus the note-folded
  aggregates `44c2b513` already added to the case-01 Lua. Pass `--lifecycle`.
- No skip risk: `8492416d`/`bc1874d0` exclude only scenarios with no assertion after comment
  stripping; both case scenarios call `Test.Pass`.

---

## 5. Does case-01's bar survive its corrected dependency?

**The bar survives. The map it was measured on does not.**

### Why the bar survives

The correction (banner on `recon/260728-trees-concealment.md:3-20`) refutes two claims: *"prone
grants nothing"* (false at that document's own SHA — `@Prone` is `+1`, auto on `!moving`) and
*"~7 dense tree cells"* (false — it is 3–5; **4 at point-blank / 2–3 at range for a stopped unit**,
5 when dug in). Both errors ran the same direction: they made concealment look harder than it is.

Neither error can have propagated into the bar, for three reasons:

1. **The batch measured, it did not derive.** Every run logged `visFromRussia` directly and read
   `vis ≤ 1` against `Detectable.Vision 3`. Bar A's numbers (0cr / 350cr) are counted deaths. An
   arithmetic error in a recon cannot corrupt a counted death.
2. **The correction widens the margin rather than narrowing it.** Case-01's defenders halt after the
   group Move and hold — exactly the `!moving` trigger. So during the 07-28 batch their real
   detection threshold was **4, not the 3 the case records**. The observed `vis=1` cleared the true
   bar by more than anyone knew. Bar B (*every seed def = 0*) is better supported after the
   correction, not worse.
3. **Direction of risk.** Both clauses of Bar A are floors on an outcome that the correction makes
   *easier* to achieve. A correction that makes the measured behaviour more robust cannot invalidate
   a bar calibrated on that behaviour.

Bar A's calibration is also sound on its own terms: the defender clause has huge headroom (observed
0 vs a 50cr cap — it tolerates ~3 defender deaths across a whole 6-seed batch), the teeth sit on the
zero-variance axis, and the noisy attacker axis (kills {4,3,5,4,2,3}, σ≈1) is deliberately kept soft
and non-per-seed. **I would ratify Bar A and Bar B as mined, unchanged.**

### What does not survive: the map's design rationale

`map.yaml:24-36` records why the map has the shape it has:

> *"a COMPACT clearing (defenders ~5c from the attackers' emergence) let the attackers DETECT and hit
> the defenders at close range… The defender's only ROBUST edge is being UNDETECTABLE, **which needs
> depth**. Hence this DEEP form"* — a solid 3-row trunk WALL (y10–12, x24–40), a deep open CLEARING
> (y13–19), and a 2-row COVER PATCH (y20–21). **87 `t01` tree actors.**

"Which needs depth" is where the refuted figures did their damage. The map was dimensioned against
"~7 cells and prone gives you nothing". The shipped numbers are 4 at point-blank / 2–3 at range for a
stopped unit, plus an automatic +1 for being prone — which is the state case-01's defenders are in.
**The treeline is roughly twice as dense as the mechanic requires.**

Two reasons that is not a cosmetic complaint:

- **It sits next to a known failure mode.** The same map comment records that an earlier *"solid
  6-row wall buried the defenders so their OWN fire was tree-blocked → lost"*, and
  `recon/260729-firing-lane-seating.md:36` proves ground shadow is direction-symmetric: *"You cannot
  be concealed-by-interposition against an enemy AND fire at that same enemy through the same
  interposition."* item-21 maximises **omnidirectional** density, so excess density buys concealment
  the defenders did not need at the cost of fire lanes they do. `:7` and `:74` state the consequence
  plainly — case-01's current geometry *"can't distinguish 'defenders fire freely' from 'defenders
  blocked but attackers blind'"*.
- **It has drifted from the intent, which `cases/README.md:13` makes the authority.** The user painted
  *"a small forest / group of trees"* and *"a small copse"*. An 87-trunk wall-plus-patch spanning 17
  cells of width is a fortification. The case can go GREEN on Bar A while answering a question the
  user did not ask.

### What I propose (the bar is the user's — this is a proposal, not a rewrite)

- **Ratify Bar A + Bar B unchanged.** They are correct, conservative, and correctly scoped.
- **Re-derive the `## Setup` section**, not the `## Bar`, against the corrected concealment numbers —
  and hold the two variables apart, because they are not equally supported:
  - **Clearing DEPTH is empirically justified.** The COMPACT failure was *measured* (defenders lost
    on 2 of 3 seeds at ~5c separation). Do not touch it on the strength of this correction.
  - **Treeline DENSITY is justified only by the refuted figure**, and is untested independently.
- A thinning experiment should therefore **hold depth constant and cut density only** — toward the
  copse the user actually painted — and check whether def=0 survives. If it does, case-01 measures
  the user's scenario instead of a fortified proxy, and the fire lanes open enough for the acquire→fire
  question to become measurable on case-01's own map.
- **I am not asserting the thinner map works.** That is exactly the kind of claim this audit exists to
  stop being made without a number. It is a hypothesis with a cheap test.

---

## 6. Ranked run list (for the manager to schedule — strictly serial, `--lifecycle`)

| # | Run | Why first | Cost |
|---|---|---|---|
| **1** | `./tools/autotest/run-test.sh test-case01b-detect` | **The best number-per-minute in the project.** Never run once; zero numbers exist for it. No dependencies, no ratification needed. It is the designated instrument for the fire-lane axis that `260729-firing-lane-seating.md` leaves open and that case-01's bar explicitly cannot measure. | 1 run |
| **2** | `./tools/autotest/run-test.sh test-case01-forest-ambush` | Converts §2's diff audit into a measurement. 22 days and ~1000 commits since the only real data point; the mechanisms all read UNCHANGED, but that is derivation. Also proves the new per-run result paths + `run.lock` work for this scenario before a 6-seed batch depends on them. | 1 run |
| **3** | RED run after the Bar A gate is implemented in Lua | Standing RED-before-green discipline. Sabotage the concealment (e.g. force `Tight` cohesion so the item-21 refinement never fires) and confirm the **specific** Bar A fail text — not merely that something failed. | 1 run |
| **4** | 6-seed batch, `--hidden --seed`, for the GREEN certification | The actual bar. Only meaningful after 3. Reuse seeds 1001–6006 so it is directly comparable to the 07-28 table. | 6 runs |
| **5** | *(optional, only if the user wants the intent-fidelity question settled)* thinned-copse variant batch | Tests §5's hypothesis: depth held constant, density cut. Do not schedule before 1–4; it is a new question, not a blocker on the existing one. | 6 runs |

Runs 1 and 2 are worth doing **regardless of the ratification decision** and do not wait on it.
Runs 3–4 wait on the user's yes/no plus a small Lua change.

---

## 7. The corpus-level finding

The case model was adopted 2026-07-26 as the preferred unit of autonomous work, on the explicit
retrospective conclusion that the project's standing failure mode is *"shipping well-reviewed changes
with no outcome numbers"* (`cases/README.md:5-7`).

Three and a half weeks later: **one case exists, it has been measured once, on day two, and its
scenario is structurally incapable of failing.** The single day audited here produced 110 commits and
moved it by nothing. The model was not tried and found wanting — it was adopted and not used.

That is a scheduling observation, not a code defect, and it is the cheapest thing on this page to fix:
runs 1 and 2 above are two serial invocations with no gates in front of them.
