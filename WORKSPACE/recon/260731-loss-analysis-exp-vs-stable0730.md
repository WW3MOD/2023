# Loss analysis — Experimental vs Stable AI 0730, S2 combat rung (2026-07-31)

**Batch analyzed:** `tools/autotest/tournament-results/260731_streak_exp_vs_stable0730_s2combat/`
(raw, git-ignored) — 10 matches, `tournament-s2-combat-river-zeta` `--mirror`, 720s
clock, `WinRule score_or_sr_capture`, all 10 → `time_limit` verdicts.
**Ref stamp:** batch ran at **main `@ 3975b012`** (`batch.meta.json git_sha`, `git_dirty:false`);
current main is **`9136368e`**. The frontier-standoff placement fix (`9136368e`,
`dc135cc2`) merged *after* this batch, so it is **not** measured here (see §5).
**Attribution:** by `bot_type` from each match's `notes.players[].bot_type` — NEVER by
slot/faction (both bots are `america`; `--mirror` swaps slots on odd seeds).
Summary doc read first: `WORKSPACE/benchmarks/260731-exp-vs-stable0730.md`.

---

## TL;DR

**The dominant loss mechanism is capture-supply starvation, not combat.** The map
seeds **~14 neutral `oilb` income POIs** (`[Tournament] FirstTick: seeded 12 income
POIs (types: oilb)`); each `oilb` held to the clock is worth **≈33,000 score** — army
value and kills are 2k–6k noise by comparison. **12–13 of the 14 sit Neutral the entire
game** because both bots field almost no TECN capturers. The winner is simply **whoever
nets a 1-oilb edge**, and Experimental fields **zero TECN in 6 of 10 games**, so it
captures nothing and loses. This is a supply lottery — it explains both the bimodality
(blowout when Exp's TECN production happens to fire, collapse when it doesn't) and the
±2/10 noise floor.

---

## The scoring reality (why oilbs are everything)

Per-`oilb` score ≈ **33k** when held from the ~tick-1100–1450 capture window to the
18,000-tick clock (score `capture_income` component; `capture_income_gross` ≈ 16.5k/oilb,
score weights it ~2×). Contrast the entire non-capture score: army + kills totalled
2k–8.5k in every game. So **one extra held oilb ≈ 5–15× the entire combat score.**

**Captures are never contested.** Across all 10 games, aggregate
`steals_count = recaptures_count = losses_count = 0`. Once an `oilb` is grabbed (~tick
1200) it is held uncontested to the clock; nobody ever re-flips one. The game is decided
in the opening capture window and there is **no comeback mechanism**.

---

## Per-game breakdown (bot-attributed)

`oilb` = income POIs held at final ownership snapshot; `TECN` = max capturers
alive+committed fielded all game (from `[exp-capture]` telemetry).

| # | Map/Exp slot | Exp oilb | Sta oilb | Exp TECN | Sta TECN | Exp score | Sta score | Win | Decided by |
|---|---|---:|---:|---:|---:|---:|---:|:--:|---|
| 1 | mirror / Russia  | 0 | 0 | 0 | 0 |  2,550 |  6,050 | **S** | 0–0 → army (Exp out-traded 2/9 kills/deaths) |
| 2 | primary / USA    | 1 | 0 | 2 | 0 | 36,704 |  5,550 | **E** | **Exp +1 oilb** |
| 3 | mirror / Russia  | 0 | 0 | 0 | 0 |  6,550 |  5,550 | **E** | 0–0 → army (Exp +1,000 army) |
| 4 | primary / USA    | 2 | 0 | 4 | 0 | 72,012 |  4,350 | **E** | **Exp +2 oilbs** |
| 5 | mirror / Russia  | 0 | 0 | 0 | 0 |    750 |  8,550 | **S** | 0–0 → army (Exp collapse 2/9) |
| 6 | primary / USA    | 1 | 2 | 4 | 4 | 41,700 | 68,518 | **S** | **Sta +1 oilb** |
| 7 | mirror / Russia  | 0 | 1 | 0 | 2 |  2,700 | 36,910 | **S** | **Sta +1 oilb** (Exp fielded 0 TECN) |
| 8 | primary / USA    | 1 | 2 | 2 | 3 | 37,754 | 64,524 | **S** | **Sta +1 oilb** |
| 9 | mirror / Russia  | 0 | 1 | 0 | 2 |  6,600 | 37,468 | **S** | **Sta +1 oilb** — *Exp WON combat 7/2, lost anyway* |
| 10| primary / USA    | 0 | 0 | 0 | 0 |  2,300 |  5,600 | **S** | 0–0 → army (Exp under-fielded, 1,400 army) |

**The oilb differential is a perfect predictor.** In all 6 games where the oilb counts
differ, the bot with more oilbs wins (6/6). The 4 remaining games are 0–0 oilb ties that
degenerate to a pure army sub-game, where Exp goes **1W–3L**.

---

## Answers to the five questions

**Q1 — where the score gap comes from (per loss).** Capture income, decisively:
- M6, M8: both fielded TECN; **Stable held 2 oilbs to Exp's 1** → Sta ~64–68k vs Exp ~38–42k. Pure +1-oilb income gap.
- M7, M9: **Exp fielded 0 TECN, captured 0**; Stable held 1 oilb → ~37k vs Exp's 2.7k/6.6k. In **M9 Exp won the combat exchange (7 kills / 2 losses, +positive trade) and still lost 6,600 to 37,468** — combat prowess is irrelevant against one held oilb.
- M1, M5, M10: **neither captured** (0 TECN both sides); decided on army, and Exp was out-traded (M1 2/9, M5 2/9) or under-fielded (M10, 1,400 army). These are the only losses where combat is the proximate cause — and even here, a single captured oilb would have flipped them outright.

**Q2 — common early pattern in the collapse losses (<3,000: M1 2,550; M7 2,700; M10 2,300; M5 750).** Yes: **Experimental fielded zero TECN capturers** (`total-tecns=0` for the whole game) → captured 0 oilbs → left with only a small army that then also lost/under-formed. The collapse is a *capture-production* failure first; the weak army is downstream (budget spent, nothing banked).

**Q3 — unit preservation (out-traded vs under-earn).** **Under-earn is decisive.** Trade quality is a red herring: Exp trades *fine or wins* in several losses (M9 7/2 win; M8 1/1 even; M6 4/5 near-even) yet loses on oilb count. It is genuinely out-traded only in the two no-capture collapses (M1, M5, both 2/9). Verdict: **Exp is not primarily out-traded; it under-earns because it does not field capturers.**

**Q4 — what the 3 wins share.** Not map side or seed parity — **Experimental fielded TECN and out-captured.** M2 (2 TECN → 1 oilb, Sta 0) and M4 (4 TECN → 2 oilbs, Sta 0) are the two blowouts; both are simply "Exp's TECN production fired and Stable's didn't." M3 is a 0–0 oilb tie won narrowly on army. The bimodality is entirely explained by whether Exp's capturer-supply lottery came up heads that game.

**Q5 — single highest-impact change (ranked, with seam).** See below.

---

## Root cause: the TECN floor deadlocks under production pressure

`oilb` capture requires a TECN (`CapturingActorTypes: tecn.*`,
`CapturableActorTypes: oilb,…`). Both bots start with **0 TECN** and rely on the
capture coordinator's demand floor to pull one from production.

- `CaptureCoordinatorBotModule@experimental.tecn` sets **`TecnFloor: 1`** (`ai.yaml:129`).
- `MaintainTecnFloor` (`CaptureCoordinatorBotModule.cs:604-629`) requests **one** TECN,
  then the gate `if (alive + pending >= Info.TecnFloor) return;` (`:619`) blocks any
  re-request while `pending ≥ 1`.
- **Observed failure (M9, Exp/Russia):** `tecn-floor-request … pending=0 floor=1 tick=19`
  fires **exactly once**, then never again — `total-tecns=0` persists to tick 17,944.
  The single request is counted `pending=1` but the Infantry queue is saturated with the
  offense stack's combat-unit buys, so the lone TECN **never completes in 18,000 ticks**.
  The floor gate at `:619` then deadlocks: `pending=1` suppresses re-requests, but the
  pending item never builds.
- **Contrast (M4, Exp/USA):** same single request at tick 39, but the queue had slack and
  delivered **4 TECN** (first at tick ~264) → 2 oilbs → 72k blowout.

So `TecnFloor: 1` is (a) far too low given **12+ free oilbs at ~33k each**, and (b)
structurally fragile — it fields zero capturers whenever combat-unit production
out-competes the single queued TECN, which happened in **6 of 10 games**. Stable is
subject to the identical lottery (it fielded TECN in only 4/10), so this is a *symmetric*
weakness — which is exactly why the rung sits at a coin-flip ±2/10 noise floor. **Breaking
the symmetry in Experimental's favor is free wins.**

---

## Ranked levers

### #1 — Guarantee TECN capturer supply, scaled to available oilbs *(highest EV, lowest risk)*
**Seam:** `CaptureCoordinatorBotModule@experimental.tecn` (`ai.yaml:92-133`,
`CaptureCoordinatorBotModule.cs:604-629`) **+ production budget priority**
(`AdaptiveProductionBotModule` / `SupplyFollowerBotModule`).
**Mechanism:** 12–13 oilbs sit Neutral all game; each ≈33k; Exp fields 0 TECN in 6/10.
Two coupled fixes: (a) raise `TecnFloor` well above 1 (target ~one capturer per reachable
neutral oilb early — e.g. 3–5), and (b) make the TECN request genuinely out-prioritize
combat-unit buys in the supply budget so the floor isn't starved (the `:619` pending
deadlock must not be able to sit on an undelivered request — re-request or reserve budget).
**Expected effect:** converts the capture lottery into a structural income lead. Exp's own
2-oilb win already scored 72k; deterministically fielding 4–5 TECN and blanketing free
oilbs projects to ~130–165k — dwarfing Stable's erratic 0–2. **Plausibly flips all of
M6/M7/M8/M9 (the oilb-deficit losses) and M1/M5/M10 (the 0–0 army losses, by capturing
where Exp currently captures nothing) — i.e. up to ~6–7 of the 7 losses.** Risk is low:
oilbs are uncontested (zero recaptures all batch), so capturers face little combat en route
except in the two collapse games.

### #2 — Capture parallelism: one TECN → one *distinct* oilb, then garrison-hold
**Seam:** `QueueCaptureOrdersFromPoiMap` (`CaptureCoordinatorBotModule.cs:474-477`) target
de-duplication + `MaximumCaptureTargetOptions` (`ai.yaml:104`); hold via
`PoiGarrisonBotModule@experimental` (`ai.yaml:291-321`, already present).
**Mechanism:** even when Exp *does* field TECN it grabs only 1–2 (M2:2→1, M8:2→1). Ensure N
capturers fan out to N distinct neutral oilbs rather than clustering, so supply (#1)
translates 1:1 into held oilbs. **Multiplies #1; near-useless alone** (does nothing in the 6
games where Exp fields 0 TECN — that's why it ranks below #1, which is the prerequisite).

### #3 — Army preservation / anti-collapse in the 0–0 games *(secondary)*
**Seam:** offense over-extension in `PoiOffensiveBotModule@experimental`; the
**frontier-standoff fix merged post-batch (`9136368e`) is aimed here and is unmeasured**.
**Mechanism:** in the oilb-tie games (M1, M5) Exp is out-traded 2/9 — it over-commits and
loses its army. Better standoff/preservation would salvage these *if capture (#1) somehow
fails to fire*. Ranks last because with #1 working these games are won on income regardless
of combat, so this only matters as a fallback. Worth re-measuring now that standoff shipped.

---

## Caveats
- Single rung, N=10, one map pair (river-zeta primary+mirror). The 14-oilb density is a
  property of *this* scenario; other rungs (S1 eco, polar-disorder, woodland) may weight
  income differently. The mechanism (income >> combat, capture-supply is the gate)
  should generalize wherever money POIs are seeded, but the exact `TecnFloor` target
  should be re-tuned per rung.
- `oilb` counts read from `[exp-capture] ownership-snapshot` (two-sided telemetry,
  `CaptureTelemetryEnabled`); TECN counts from `no-idle-capturers total-tecns`+committed.
  Both are Experimental-emitted but snapshot the full map, so Stable's holdings are
  observed, not inferred.
- This contradicts the summary doc's "Exp trades poorly" framing: trading is a sideshow;
  the deficit is an under-contested oilb-grab that *both* bots fail, and Exp fails slightly
  more often. Fix the capture supply and the trade quality stops mattering.
