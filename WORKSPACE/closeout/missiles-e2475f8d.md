# Close-out — manager "Missiles" (session e2475f8d)

Validated against main @ `35876332` (fetched). None of the seven upstream merges
from the second machine touch this manager's area.

## 1. Open work, and the next concrete step

**The whole missile audit → spec → repair programme is open, exactly as left.**
Plan is committed at
`.maestro/managers/manager-4d821d1c-93e4-4833-a2b4-327d4728a8a5/plans/01-missile-system-ground-truth-audit-agreed-spec-th.md`.

Verified against current main rather than assumed:

| Path | Newest commit | Verdict |
|---|---|---|
| `engine/OpenRA.Mods.Common/Projectiles/Missile.cs` | `d64a7a68` | predates this session — unchanged |
| `engine/OpenRA.Mods.Common/Traits/Armament.cs` | `5a1a4517` | predates this session — unchanged |
| `mods/ww3mod/rules/weapons/weapons-missiles.yaml` | `f2468743` | predates this session — unchanged |
| `DOCS/reference/missiles.md` | — | does not exist |

**Next concrete step:** get answers to the three design questions in §3, then
dispatch Phase 0 (per-tick missile trace instrumentation) *plus* the four
read-only audit workers in parallel. Neither needs an autotest run, so both can
start the moment the questions are answered.

Plan shape, for whoever picks it up:

- **Phase 0 — instrumentation.** A per-tick missile trace under a debug flag:
  position, target position, state, facings vs desired, `allowPassBy`,
  `flyStraight`, `lockOn`, distances, `loopRadius`, `distanceCovered` vs
  `rangeLimit`, and the **exact termination reason** (which detonation clause
  fired, or removed-without-exploding). This is the highest-leverage item —
  this session lost hours to hand-integrated flight geometry that was wrong
  twice, in two different ways.
- **Phase 1 — four parallel read-only audit workers:** flight & guidance;
  detonation & lifetime (incl. warhead + armour facet model); fire control &
  targeting; weapon data sweep.
- **Phase 1.5 — one adversarial reviewer** whose only mandate is to attack
  those four reports. After two confidently wrong analyses in one session this
  is not optional.
- **Phase 2 — `DOCS/reference/missiles.md`**, agreed with the user before any
  fix.
- **Phases 3–5 —** repro scenarios authored but not run, then ONE goahead that
  runs the whole batch with tracing on, then staged repair. The grant-batching
  is deliberate: autotest runs are user-gated and the harness is
  single-instance, so all non-gated work is front-loaded.

**Solved upstream:** nothing of this manager's. The Mi-28 overfly conclusion
(the bug is not real as diagnosed) already landed at `864537c4`; no further
action.

## 2. Uncommitted or unmerged artifacts

**None.**

- `wt/missile-overfly` merged at `864537c4`; worktree removed.
- The plan doc and `WORKSPACE/missile-diagnosis.md` were both committed by
  other sessions (the latter at `6dda9e91`).
- Untracked files under `WORKSPACE/` (`batch-*.log`, `tourney-real.log`,
  `user-session-*.log`) are **not** this manager's.

## 3. Questions asked of the user, never answered

Three, all still open. Manager's recommendation in brackets.

1. **Audit scope** — every `Projectile: Missile` weapon [conf 85] / just ATGM
   and AA [45] / everything that flies and guides [38].
2. **Where should a missing missile detonate?** — at the point of closest
   approach [72] / fly on and detonate on terrain or at fuel-out, but never be
   silently removed [66] / a rule per weapon class [58].
   *(The user has already ruled that AA missiles are exempt and may fly on
   until fuel-out; this question is only about ground missiles.)*
3. **Close-range AA (2–4 cells)** — the missiles should hit, treat as a bug
   [80] / both: arm faster AND make launchers refuse hopeless shots [68] / the
   launcher should hold fire below a floor [47].

A fourth, earlier question asking a goahead for an autotest run at the real
3840 helicopter altitude is also unanswered but is now **moot** — superseded
when the user rescoped the work.

## 4. Knowledge that lived only in the session transcript

All of the below has been persisted to the manager log; repeated here so the
lead does not have to go and find it.

### The user's new evidence (2026-08-12)

1. **Three AA soldiers fired at a Littlebird from 2–4 cells and all missed** —
   "it looked like the tracking mechanism didn't arm at all, so they just fired
   straight and kept flying in a straight line."
   *Manager's hypothesis:* `HomingActivationDelay` (the missile flies unguided
   in Freefall until `ticks == delay + 1`, `Missile.cs:911`) and/or the separate
   `Arm` gate on `Explode()` (`Missile.cs:1147`) both expire *after* a fast
   missile has already crossed 2–4 cells. If so the projectile is behaving
   correctly and the real defect is fire-control taking a shot it cannot make.
   **These two readings need opposite fixes — do not assume which.**
2. **"Plenty of missiles miss and never explode."** The user's stated design
   rule: a missile that misses should **generally still explode**, EXCEPT
   anti-air missiles, which may keep flying until fuel-out. Treat silent
   removal-without-detonation as a defect to hunt in `Missile.cs`'s termination
   paths.
3. **Severity, in the user's words:** the system "has worked OKAY except the
   occasional misses… not catastrophic, but it breaks at some points."

### Carry-forward warning

**`WORKSPACE/missile-diagnosis.md` (committed at `6dda9e91`) is UNRELIABLE from
§2 down.** Its raw fleet field table is usable; its conclusions are not. Three
known errors:

- Its overfly *consequence* analysis — falsified by the pre-fix baseline; the
  correction is on main at `864537c4`. It traced only one arm of a two-arm
  branch and missed that the other arm's strict `>` silently corrects the
  problem.
- It read `WAngle(24).Facing` as 24 when `.Facing` is `Angle >> 2` = **6**, so
  every loop-radius and descent-geometry figure in it is off by ~4×. A
  WW3MOD-added terminal turn-rate boost (`Missile.cs:885-894`, up to 20
  facings/tick) compounds the error further.
- It called the Javelin's `CruiseAltitude: 10c0` "almost certainly a typo for
  1c0". It is the **deliberate top-attack flight profile** — the weapon already
  declares `TopAttack: true` (`weapons-missiles.yaml:2-20`), which routes damage
  to the top armour facet via `DamageWarhead.cs:131`. Acting on that advice
  would break the weapon. Only the user's question caught it.

### The structural observation worth carrying forward

`Missile.cs` is inherited OpenRA code built around **terrain height levels** —
cliffs a missile must climb over. WW3MOD's `mod.yaml` declares `MapGrid` with no
`MaximumTerrainHeight`, so the height layer is uniformly zero **and always will
be**. A large part of the guidance hot path (`InclineLookahead`, the `predClf*`
machinery, cliff avoidance, `allowPassBy`) can never do anything useful in this
mod — and it is not merely dead, it still *latches state*: `allowPassBy` is set
true on every ground shot because `lastHt >= targetPosition.Z` evaluates
`0 >= 0`. Two separate commits (`000a3795`, `85374503`) have already patched the
fallout, each fixing one half and leaving the other. This is the strongest
candidate for the "general improvement" the user asked about, but should only be
sized after the audit establishes what depends on it.

### Existing test infrastructure to reuse, not duplicate

`09d44040` (wt/aa-los-test) landed four AA engagement scenarios —
`test-aa-detection-fog`, `test-aa-overkill-cadence`, `test-aa-overkill-pump`,
`test-aa-overkill-suppression` — 3027 lines, **scenarios and rules only, no
engine change**. They cover detection/fog and overkill cadence, *not* close-range
guidance, so the 2–4 cell case remains uncovered. Phase 3 should extend this
suite and reuse its map/rules scaffolding rather than author a parallel one.

### Process rule that earned its keep

The falsifiable prediction is what made this session cheap. A confident 489-line
report with correct file:line citations was wrong in its central conclusion, and
the *only* thing that caught it before code shipped was requiring the
implementer to record a prediction and run the pre-fix baseline first — the
baseline passed where the report predicted failure, so the worker stopped and
changed nothing. Keep doing this on every diagnosis-then-fix pipeline.
