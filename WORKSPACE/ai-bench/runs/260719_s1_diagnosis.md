# S1 diagnosis — why the bootstrap smoke scored `resources_earned: 0 / 0`

**Cycle card:** `260719_1816__tournament-v2-vs-normal-2p__6d7c561.json`
**Scenario:** `tournament-v2-vs-normal-2p` + `tournament-eco-5min.yaml` (300s, SpeedMultiplier 8, Mode B hidden)
**Grounded against:** ai-bench @ `6d7c561` (worktree, clean at diagnosis time)
**Diagnostic match run:** none — static + log evidence is conclusive (see Q3).

---

## TL;DR (root cause)

The S1 metric was **0-vs-0 by map construction, not by AI behavior.** The scenario
`tournament-v2-vs-normal-2p` does **not** run on River Zeta WW3 — it runs on a bare
66×34 inline stub map baked into the scenario folder whose entire `Actors:` block is
**2 Supply Routes + 2 spawn markers and nothing else**. There are **zero capturable
income structures** (oilb/fcom/bio) on it. In the SR budget model `PlayerResources.Earned`
only moves on income-structure capture, so `resources_earned` and `capture_income` are
**structurally pinned to 0 for both bots** — a longer clock, a capture-forcing tweak, or
a fixed-target metric on the *same* map would all still read 0/0. The only fix that makes
S1 measure its intended facet is **a map that actually contains reachable money POIs.**

The bots were otherwise healthy: both called in units and fought a full 5-minute match.
The empty-SR / scenario-gating trap does **not** apply here.

---

## Q1 — What map does the scenario run on? Does it have money POIs?

**It is NOT River Zeta WW3.** The scenario carries its own inline map.

- Scenario map: `tools/autotest/scenarios/tournament-v2-vs-normal-2p/map.yaml`
  - `Title: TOURNAMENT: v2 vs Normal 2P` (`map.yaml:5`), `MapSize: 66,34` (`map.yaml:13`).
  - Complete `Actors:` block (`map.yaml:59-74`): `OwnSR: supplyroute` @ (6,16),
    `OpponentSR: supplyroute` @ (58,16), `SpawnUSA: mpspawn` @ (6,16),
    `SpawnRussia: mpspawn` @ (58,16). **No oilb, no fcom, no bio, no derrick — nothing
    capturable.**
  - The **mirror** twin is identical in this respect:
    `tournament-v2-vs-normal-mirror-2p/map.yaml` also has only the 2 SRs + 2 spawns
    (`grep -cE "oilb|fcom|bio"` → **0**).
- Real River Zeta WW3: `mods/ww3mod/maps/river-zeta-ww3/map.yaml`
  - `Title: River Zeta WW3` (`:5`), `MapSize: 98,82` (`:11`).
  - **12 `oilb` income derricks** (`$50` periodic CashTrickler), scattered mid-map and
    toward the corners — sample `Location:` values `15,3 / 17,44 / 25,22 / 38,53 / 56,26 /
    76,35 / 51,79 / 74,58 …`. **No fcom/bio/miss/hosp** on this map (grep → 0); the only
    money POIs are the 12 oil derricks.

So the LADDER premise ("run on the River Zeta WW3 map", LADDER §Ladder-structure) is
**factually wrong for this scenario as built** — the harness scenario has never used the
canonical map; it uses a throwaway 2-SR arena. That mismatch is the whole bug.

## Q2 — Did the bots get units? (the scenario-gating / empty-SR trap)

**Yes. Both bots called in units and fought.** The trap does not apply.

Evidence (verdict `260719_1816__…verdict.json` + `match_1.watcher.log`):
- v2/USA: `units_killed:7`, `units_dead:3`, `kills_cost:3900`, `deaths_cost:4750`,
  `army_value:300` (end), `order_count:15`.
- normal/Russia: `units_killed:5`, `units_dead:9`, `kills_cost:1100`, `deaths_cost:6500`,
  `army_value:0` (end, army wiped), `order_count:17`.
- `match_1.watcher.log:11-70`: live score curve climbs from unit call-in and combat —
  USA `300→…→4850→4200`, Russia `250→2200→…→700→1100`. That movement is the
  `army_value` + `kills_value` score components changing tick-to-tick, i.e. units were
  continuously present and trading. `rules.yaml:11-12` gives both bots `DefaultCash: 7500`,
  which funded the call-ins.

Non-zero `kills_cost`/`deaths_cost`/`order_count` on *both* sides ⇒ nobody was an empty
SR. This tournament scenario **does** feed call-in (SR + budget); the Phase-2 "bare
skirmish → pool=0, zero army" gate is a different setup and is not what happened.

## Q3 — Why 0/0, and how did a 0/0 score still produce a USA time_limit win?

**Why 0/0:** `resources_earned` ← `PlayerResources.Earned`
(`BotVsBotMatchWatcher.cs:308`). In WW3MOD there are no refineries; `Earned` only
increments when a bot **captures an income structure** (oilb/fcom/bio CashTrickler grants).
The map has **none**, so neither bot could ever earn — 0 is the only possible value,
independent of AI skill, clock, or capture logic. `capture_income` (the scorer's
`earnedTotal × weight`) is 0 for the same reason
(`WeightedComponentMatchScorer.cs:63,67`). This is a map-content problem, full stop.

**How USA still won on `time_limit`:** the winner is decided by **weighted score total**,
not by `resources_earned`. At `currentTick >= timeLimitTicks` the win rule picks the
highest `MatchScoreSnapshot.Total` (`TimeOrSrCaptureWinRule.cs:85-99`). The scorer total
is `army_value×1.0 + capture_income×2.0 + kills_value×1.0`
(`tournament-eco-5min.yaml:31-36`, `WeightedComponentMatchScorer.cs:66-73`). Plugging in
the final verdict:
- USA total `= 300 + 2·0 + 3900 = 4200`.
- Russia total `= 0 + 2·0 + 1100 = 1100`.
USA wins **purely on `kills_value`** (it destroyed $3900 of Russia's army for a $4750 loss;
Russia destroyed only $1100 for a $6500 loss). `resources_earned` is a *stats* field in
the verdict, **not** a score component, so a 0/0 economy has no bearing on the winner. The
match was decided as a combat blowout while the economy axis was inert.

---

## Recommended S1 rescope

Root cause = **map has no money POIs**, so among LADDER §S1's anticipated options only the
**"different/fixed map with reachable money POIs"** option addresses it. Longer clock and
fixed-target-metric variants are non-fixes here: 0 income structures ⇒ 0 earned by
construction no matter the clock or the threshold.

### PRIMARY (recommended): run S1 on a River-Zeta-derived 2-bot map that keeps the oil-derrick POIs

Build a new scenario `tournament-v2-vs-normal-riverzeta-2p` (+ mirror) whose `map.yaml`
is derived from `mods/ww3mod/maps/river-zeta-ww3/` — **keep the 12 `oilb` income derricks**,
and overlay the harness's 2 bot Supply Routes + spawn markers + `Bot: v2` / `Bot: normal`
player refs (the same overlay the current stub already provides). Keep the metric
**`resources_earned`** unchanged.

**Why this one:**
- It is the *only* option that removes the root cause — it gives both bots something to
  capture, so `resources_earned` can finally diverge and actually measure "does v2's
  PoiMap → CaptureCoordinator layer convert POIs into more earned cash than Normal."
- It realigns the scenario with the LADDER's own stated premise ("River Zeta WW3", the
  map the user mandated for the rung), which the current stub silently violated.
- It is a **HARNESS** change (SPEC §4.1: scenarios/maps are mutable) and explicitly the
  "copy a scenario folder, only `map.yaml` changes" scaling pattern (SPEC §10). It touches
  **no unit stat / balance number** — derrick income stays `$50`; both bots face the same
  POIs, so the yardstick isn't shortened (§4.3 litmus: this makes the test *measure the
  intended facet*, not the AI score higher).

**Two required follow-through checks (fold into the first real cycle, cheap):**
1. **Reachability within the clock.** River Zeta is 98×82 vs the stub's 66×34; TECN travel
   from an edge SR to a mid-map derrick plus capture time may exceed 300s. Either (a) place
   the 2 SRs so ≥1–2 derricks sit within ~300s reach of each, or (b) bump S1 to
   `TimeLimitSeconds: 420–600` in `tournament-eco-5min.yaml` (a clock knob is fine *once
   the map has POIs* — it's the map, not the clock, that was the blocker). One diagnostic
   hidden match on the new map will show whether any capture lands inside the window.
2. **Bias / fairness of POI placement.** Ensure the derrick set is symmetric between the
   two SRs (or rely on the mirror twin to average it out), so an earned-cash gap reads as
   AI skill, not spawn-side derrick luck (SPEC §9.4). Gate with a Normal-vs-Normal batch
   landing ~even on `resources_earned` before trusting any v2 number.

### Why not the alternatives (record, per SPEC §6.3)

- **Longer clock only** (300→600s on the *current* stub): still 0/0 — no POIs to capture.
  A valid *secondary* knob, but only *after* the map has POIs.
- **Fixed-target metric** ("≥ $X earned"): degenerate at 0/0 — X would have to be 0, which
  discriminates nothing. Becomes viable (as a Normal-vs-Normal-calibrated floor, LADDER §S1
  rationale) *once* a POI map produces non-zero baselines.
- **Scenario feeding production + pre-placed TECNs:** the bots already produce fine
  (Q2); the missing ingredient is capturable targets, which the map fix supplies more
  cleanly than scripting reinforcements.

**Net:** adopt a River-Zeta-derived POI map for S1 (metric unchanged), then tune the clock
and verify capture-reachability + POI symmetry in the first hypothesis cycle.
