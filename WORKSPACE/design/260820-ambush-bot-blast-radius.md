# Ambush and the bots — blast radius assessment

**Date:** 2026-08-20
**Branch:** `wt/ambush-bot-impact` off `main` @ `4bb3fae9`
**Status:** RESEARCH ONLY. Nothing built, no game launched, no benchmark run.

---

## Verdict

**Safe — because the bots already have it, and have had it for weeks.** The framing that
prompted this strand (a player-facing ambush stance that would "land on" the AI as a
novel risk) is inverted: ambush arrived on the bots *first*, has been in `@experimental`
for months, was promoted to `@stable` on **2026-08-02** (`b8d2e601`), and has **already
been A/B'd against the benchmark** at N=10 with an inconclusive result. There is no
new hold-fire risk to introduce. It is already shipped and already measured.

There is, however, a real problem this strand uncovered, and it is not the one it went
looking for:

> **The `@stable` benchmark baseline is stale by 22 days and by 188 bot-module commits,
> and one of those commits changed `@stable`'s play on purpose.** Any ambush measurement
> taken against it today inherits an unknown offset.

**What must be measured before ambush:** a re-baseline (calibration + Exp-vs-Stable
zero). **What must be measured after:** nothing at match level — the prior A/B proves
match-level scoring cannot resolve an effect this small, and the instrumentation needed
for a metric that could does not exist yet. Details in §3.

---

## 1. Do the bots use stances today?

**Yes. Two bot modules write unit stances, on both profiles.**

`UnitStance` is a three-value enum — `HoldFire`, `Ambush`, `FireAtWill`
(`engine/OpenRA.Mods.Common/Traits/AutoTarget.cs:22`).

| Module | Writes | Evidence |
|---|---|---|
| `LaneAmbushBotModule` | Sets `UnitStance.Ambush` on posting, resets to `FireAtWill` on release | `LaneAmbushBotModule.cs:496-497`, `:519-520` |
| `PoiOffensiveBotModule` | Issues `SetUnitStance` for `FireAtWill` / `HoldFire` transitions | `PoiOffensiveBotModule.cs:3512, 3524, 3531` |

`StancePositioningExecutor` **reads** the live fire-stance and declines to reposition a
unit in `Ambush` or `HoldFire` (`StancePositioningExecutor.cs:305-323`).

Both profiles are affected. `ai.yaml:834` grants `LaneAmbushBotModule@experimental`
(`RequiresCondition: enable-ai-experimental`); `ai.yaml:2218` grants
`LaneAmbushBotModule@stable` (`RequiresCondition: enable-ai-stable`) with **identical
tuning values** — same `MaxAmbushes: 2`, `UnitsPerAmbush: 2`, `PostFractionPct: 40`.

Each module grants `enable-ambush-tactics` only to the units it posts
(`LaneAmbushBotModule.cs:418/439`), so the Stage-2/3 machinery is per-unit, not global.
A unit no ambush module posted never sees the gated path, on any profile.

### The executor comment/YAML contradiction — settled

It is a real contradiction, and it is the **file header** that is wrong, not the YAML.

`StancePositioningExecutor.cs:50-52`:

> Gated `enable-tactical-positioning || enable-ai-experimental`: default-off
> everywhere except experimental bots (the former is granted by nothing in Phase 2;
> humans get it in Phase 3). @stable/@normal/humans are byte-identical.

`mods/ww3mod/rules/defaults.yaml:40-45`:

> `# Phase-3 human enablement (RATIFIED default-ON): grant the executor token to every human-owned combatant.`
> `GrantConditionOnHumanOwner@tacpos:`
> `    Condition: enable-tactical-positioning`

The YAML wins. Phase 3 has landed: humans **do** get the token, so the header's
"granted by nothing" and its "humans are byte-identical" are both false. Two independent
confirmations that the header is the stale party, not the YAML:

1. The same file's **body** comment (`:305-323`) is fully current — it describes the
   "Phase-3 S4 fix" and the human un-ambush bug by name. Header and body disagree with
   each other; the body agrees with the YAML.
2. Commit `06f0605a` (2026-08-11, *"Correct the @stable byte-identity claims that expired
   at the 0802 parity promotion"*) was a deliberate sweep of exactly this class of stale
   claim across 18 files — and `StancePositioningExecutor.cs` **is not among them**. The
   sweep missed this file.

A prior worker resolved this by "trusting the code" — that happened to reach the right
answer, but only because it read the body rather than the header. The header is a live
false claim and should be corrected (out of scope for this docs-only strand; logged
below).

**Note for scoping:** the executor is granted to bots via `Bots: experimental` only
(`defaults.yaml:37-39`). `@stable` does **not** run the executor. So the two profiles are
*not* at global parity despite the b8d2e601 commit title — see §4.

---

## 2. What does hold-fire do to a bot?

**It has already been done to them, and the answer measured out as noise.** From
`WORKSPACE/ai-bench/runs/260728_rebaseline_result.md` §4 — a clean paired A/B on the same
seeds, verified by marker absence (0 `[exp-ambush]` markers in every one of the 20 arm-B
matches):

| Rung | Ambush ON win | Ambush OFF win | Paired swing Δ (ON−OFF) | Read |
|---|---|---|---|---|
| S1 eco (guard) | 0.30 | 0.50 | median −575, **mean 0** | ambush ON worse |
| S2 combat (primary) | 0.40 | 0.30 | median **+425**, mean +630 | ambush ON mildly better |

The card's own conclusion: both effects sit **inside the calibrated noise band (±$2000
swing, ±2/10 win-rate)** and **the two rungs disagree in sign**.

### Why it is noise, and why that answers the "does it depend on map or opponent" question

The charter asks whether hold-fire helps against a passive opponent and hurts against an
aggressive one. The honest answer from the data is **the question is currently
unanswerable, because the dose is too small to produce a signal either way**:

`MaxAmbushes: 2` × `UnitsPerAmbush: 2` = **4 units maximum**, and `PostFractionPct: 40`
caps posting at 40% of available force. The YAML comment at `ai.yaml:828` states the
bound is deliberate — *"so it never starves offense."* A match-level score aggregate is
being asked to detect the contribution of at most four held-back units. The
sign-disagreement between rungs is what a null effect looks like, not evidence of
map-dependence.

This is **structurally the same failure as the documented medic-floor precedent**
(`WORKSPACE/DISCOVERIES.md:1202-1221`): a knob evaluated against a denominator that the
change cannot move. There, a floor of 20 sat above a denominator that peaked at 18-19, so
the floor never engaged and the snapshot could not distinguish "never engaged" from
"fully satisfied." Here, 4 ambushers sit below the resolution of a whole-match score
delta, so the aggregate cannot distinguish "ambush did nothing" from "ambush worked on
four units and the other forty drowned it out." The transferable rule from that
write-up applies verbatim:

> *"a ratio is only meaningful against a MEASURED denominator … print the denominator's
> own trajectory … not the typical one."*

**So the risk this strand was convened to assess — that hold-fire makes the bot look
catastrophically worse — is bounded by construction.** Four units cannot produce a
catastrophe. That bound is a property of the LaneAmbush caps, not of the stance, and it
would stop holding if a new feature raised the caps or granted ambush more broadly. That
is the thing to watch, and §5 returns to it.

---

## 3. Measurement plan

### 3a. Prerequisite: the metric that does not exist yet

Today's ambush instrumentation is **posting-side only**. All four markers in
`LaneAmbushBotModule.cs` (`:390` reeval, `:393` lane, `:470` post, `:533` retire) record
that an ambush was *set up* and *torn down*. **Nothing records whether it paid off** —
there is no marker for a spring, for shots fired from ambush, or for kills credited to an
ambusher. `AutoTarget.AmbushSprung` (`AutoTarget.cs:377`) exposes the latch in-process but
is never logged.

Consequently **no currently-available metric can answer "did holding fire help?"** The
match-level score can only see it as noise (§2), and there is no event-level counter.

**Before any post-change measurement is worth running, add three counters** (small,
mechanical, no behaviour change):

1. `[exp-ambush] spring` — tick, unit, which of the five triggers fired, whether the unit
   had a target pre-aimed.
2. `[exp-ambush] outcome` — per retired lane: shots fired, damage dealt, damage taken,
   kills, while in Ambush.
3. `[exp-ambush] dwell` — ticks spent in Ambush before spring, and ticks spent in Ambush
   **without ever springing** (the failure mode: a unit that held fire, was never
   detected, and contributed nothing).

Counter 3 is the one that cannot be gamed by the change itself. A change that makes
ambush "better" by springing more eagerly will improve kills *and* reduce dwell; a change
that makes units hide uselessly will show as rising never-sprung dwell even if match
score is flat. Match score, kills alone, or a standing-unit snapshot are all gameable by
the change or drowned by the other forty units — the medic-floor lesson.

### 3b. Re-baseline (required first — see §4)

Two calibration rungs to re-zero the instrument, then two Exp-vs-Stable rungs. All
sequential; the harness has no parallel runner (Phase 3, unbuilt).

```bash
# Calibration — Stable-vs-Stable, establishes the current noise band.
# NOTE the `-cal-nn` scenarios: bot assignment lives in each scenario's map.yaml, NOT in
# the config. The plain scenarios are experimental-vs-stable (map.yaml:62/70); only the
# `-cal-nn` variants are stable-vs-stable (map.yaml:63/71). Using the plain scenario here
# would silently measure the wrong matchup. No mirror for cal — vary seed on one map.
./tools/autotest/run-tournament.sh tournament-s1-eco-cal-nn \
  --config tools/autotest/scenarios/tournament-s1-eco-cal-nn/tournament-eco-5min.yaml \
  --seeds 10 --max-wall-secs 300

./tools/autotest/run-tournament.sh tournament-s2-combat-river-zeta-cal-nn \
  --config tools/autotest/scenarios/tournament-s2-combat-river-zeta-cal-nn/tournament-combat-12min.yaml \
  --seeds 10 --max-wall-secs 600

# Exp-vs-Stable zero, mirrored to cancel the P1-slot spawn-capture bias
./tools/autotest/run-tournament.sh tournament-s1-eco-river-zeta \
  --config tools/autotest/scenarios/tournament-s1-eco-river-zeta/tournament-eco-5min.yaml \
  --seeds 10 --mirror tournament-s1-eco-river-zeta-mirror --max-wall-secs 300

./tools/autotest/run-tournament.sh tournament-s2-combat-river-zeta \
  --config tools/autotest/scenarios/tournament-s2-combat-river-zeta/tournament-combat-12min.yaml \
  --seeds 10 --mirror tournament-s2-combat-river-zeta-mirror --max-wall-secs 600
```

**40 matches.** S2 ≈ 1.5–2 min wall each at `SpeedMultiplier: 8`; S1 ≈ 90 s. Estimated
**60–80 minutes** total, plus overhead.

`--config` is **not optional** — no scenario has a default `tournament.yaml` and the
harness exits 3 without it (recorded deviation #2 in the 260728 card). Wall caps of
300/600 are the values that ran clean; the plan's original 150/400 culled a match under
shared-checkout load.

All eight scenario/config paths above were verified to exist on `4bb3fae9`, and the bot
matchup of each was verified by reading its `map.yaml` rather than assumed from the name.

### 3c. How many runs to resolve ambush at match level — and why not to

The harness reports paired per-seed deltas, so the natural test is a sign test on the
paired swing. Working from the cal S2 spread (median −225, range [−4150, +2000]) and the
observed sign splits (6/10 and 4/10, i.e. ≤60/40):

| True effect | Matches **per arm** for α=0.05, power 0.8 | Wall clock, both arms |
|---|---|---|
| 70/30 sign split | ~47 | ~3 h |
| 60/40 sign split | ~194 | ~13 h |

The prior A/B's observed splits sit at or below 60/40. **Resolving the real ambush effect
at match level therefore costs on the order of 13 hours of serialized runs**, and that is
the cost of confirming an effect the caps make small by design. I do not recommend
spending it.

**Recommended instead:** N=20 per arm on S2 only, read against the §3a event counters
rather than the score. Twenty matches × ~4 posted ambushes gives ~80 ambush events per
arm — a sample on the *ambushers*, where the effect actually lives, rather than on the
match. That is ~40 matches, **~70 minutes**, and it can detect a change in spring rate or
never-sprung dwell that the score never sees.

Validity gate before reading anything: **engaged ≥ 6/10** on S2 (the organic
Stable-vs-Stable rate is 7/10 — the floor sits right at the natural rate, so a rung that
fails it is not measurable, not a result).

### 3d. Total ask, for one grant

| Phase | Matches | Est. wall |
|---|---|---|
| Re-baseline (§3b) | 40 | 60–80 min |
| Ambush A/B on event counters (§3c) | 40 | ~70 min |
| **Total** | **80** | **~2.5 h** |

---

## 4. Is the current baseline valid?

**No. It is stale, and this is the blocking finding.**

- Most recent benchmark record: `WORKSPACE/ai-bench/runs/260729_item24_ab_result.md`.
  Nothing since. **22 days.**
- `git log --since=2026-07-29 -- engine/.../BotModules/` → **188 commits.**
- Among them, `b8d2e601` (2026-08-02) *"promote(ai): @stable to full @experimental
  parity"* — a **deliberate, acknowledged change to `@stable`'s play**, taken after the
  last baseline. This is exactly the "visible improvement flowing to `@stable`" that
  `CLAUDE.md` permits, and exactly the case where it says the baseline must be re-taken
  knowingly. It has not been.
- `06f0605a` (2026-08-11) exists solely to *"Correct the @stable byte-identity claims that
  expired at the 0802 parity promotion"* — the project already knows the old numbers
  don't carry.
- Bot-visible YAML changed as recently as **2026-08-19** (`e174d78b`, rendezvous anchor
  bound).

Measuring ambush against the 260728/260729 numbers would attribute 188 commits' worth of
drift to ambush.

**One correction to the record while re-baselining:** `b8d2e601`'s "full parity" title
does not hold today. `ai.yaml` currently gates **15** modules `enable-ai-experimental`
against **11** gated `enable-ai-stable`, and `StancePositioningExecutor` is granted to
bots via `Bots: experimental` only (`defaults.yaml:37-39`) — `@stable` does not run it.
Whether parity was ever complete or was re-opened by later experimental-only work, the
Exp-vs-Stable rung today measures *"everything experimental-only since 08-02"*, not a
single variable. That is fine for a zero; it is not fine to describe as an ambush result.

---

## 5. Should the bots get this at all?

**They already do, so the question is really: should the *new* feature raise the dose?**

Arguing it rather than assuming, as asked:

**Against a player-only stance or an off-by-default flag.** `CLAUDE.md` is explicit that
building a gate whose only purpose is withholding an improvement from `@stable` is not
allowed, and caution alone is not a justification. Ambush is not a novel risk here: it
shipped to `@experimental` months ago, reached `@stable` on 2026-08-02, and its measured
effect is inside the noise band on both rungs. A flag would be withholding from `@stable`
something `@stable` has already had for 18 days without incident. There is no
justification available that clears the project's bar.

**The one thing that does need a gate — and already has one.** The `@stable` rule that
still binds is the *silent drift* rule: a new behavioural `Info` field on a shared trait
must default to baseline. The ambush machinery already complies, and does so well:
`AmbushTacticsCondition` is a **per-unit** token granted only by the module that posts a
unit (`LaneAmbushBotModule.cs:418/439`), with no shared or global grant. A unit no ambush
module posted never takes the gated path, on any profile. Any new field added by the
ambush feature must join that pattern — default off, reachable only through the per-unit
token. That is the constraint to hold the implementer to, and it is a much narrower ask
than a profile flag.

**Where the real risk sits.** Not in hold-fire, and not in `@stable` having it. It is in
the **caps**. The entire safety argument in §2 rests on `MaxAmbushes: 2` ×
`UnitsPerAmbush: 2` = 4 units. Four units cannot produce a catastrophe; forty can. If the
new feature raises those caps, widens `PostFractionPct`, or grants ambush through any
path other than a posting module, the bound evaporates and every conclusion in this
document expires with it. **Cap changes are the thing to gate and the thing to measure —
the stance is not.**

---

## Watch

- **I did not verify the sign-test arithmetic in §3c against a statistics tool.** It is a
  standard normal-approximation sample size for a binomial proportion; the ~47 and ~194
  figures are order-of-magnitude guidance for budgeting, not a published power analysis.
  If the exact number matters to a go/no-go, re-derive it.
- **The wall-clock estimates are inherited, not observed.** 1.5–2 min per S2 match comes
  from the scenario YAML comment and the 260728 card, on that machine under that load.
  The 260728 run had to bump its wall caps mid-run for exactly this reason.
- **I did not read all 188 bot-module commits.** I checked which touched `ai.yaml` and
  which touched `enable-ai-stable` blocks. A commit could have changed `@stable`'s play
  through a shared C# path without touching either — which would make the baseline *more*
  stale than I have argued, never less.
- **The `PoiOffensiveBotModule` stance writes are the weakest part of my §1.** I confirmed
  the `SetUnitStance` call sites exist but did not trace the conditions under which
  `ApplyFiresStance()` picks `HoldFire`. If that path puts large numbers of units into
  HoldFire, there is a second, larger hold-fire dose in the bot that I have not priced,
  and the "4 units" bound in §2 covers only LaneAmbush. **This is the first thing I would
  check next.**
- **`StancePositioningExecutor.cs:50-52` carries a false claim** (§1) that the 06f0605a
  sweep missed. Not fixed here — this strand is docs-only and that is an engine file.
