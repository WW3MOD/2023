# PIPELINE — living roadmap

> **This is the living roadmap.** The queue below reads strictly top-to-bottom in execution order: the top item is the **next thing to start**, everything under it follows in order. The manager re-evaluates ordering every time an item is added or finishes. **You steer by reordering lines, commenting, or striking items** — say the word and the order changes.
>
> **Every item is framed by "How will this be perceived in the game?"** — what the player or a watching viewer actually sees change. Technical notes are secondary: one line, with doc/commit refs.
>
> Items in progress are marked **[IN FLIGHT]**. Finished work is cleared out of the queue into **SHIPPED** at the bottom (most recent first). Source-of-truth for scope stays `RELEASE_V1.md`; what's-in-motion stays `HOTBOARD.md`; this file is the ordered plan of attack.

---

## GATE — lifted 2026-07-23

User played a 2v2 vs three bots. The three previously-gated behaviors (heli standoff `090ad9d0`, /danger overlay `0833b376`, Phase-4a role tasking `acc42ad7`) drew no complaints. The session surfaced four new items, captured as queue items 1–4 below; the queue is unblocked and reordered accordingly.

---

## QUEUE

### 5. Cohesion stance identities — fine-tuning wave
**Perceived:** the three cohesion stances become three visibly different behaviors — **Tight = column** (move fast, stay together), **Loose = combat interval** (fight from cover), **Spread = dispersed** (survive artillery) — instead of one box at three widths.
_Follows item 3. Shaped by the user's reactions to `WORKSPACE/cohesion/illustrations/260722_stance_proposals.html` (decision points DP-1..DP-5 still open — picks can arrive any time and steer this item)._

### 8. Ambush behavior — IMPLEMENTATION
**Perceived:** hidden Ambush units that hold fire until spotted or until springing the trap at the best moment; units *feel alive*, reacting to being seen/unseen. Human-settable stance first, **default off** so nothing changes for players who don't opt in.
_Follows the staged plan in `plans/260722_ambush_undetected_design.md` — **gated on your review of the design** (4 open forks are listed there: prone semantics, moving-ambush scope, spring-timing doctrine, bot-only vs human-first)._

### 9. Influence Stage F — strategic repoint + territorial balance-of-power revival
**Perceived:** the bot's offense presses where the enemy is *weak* instead of grinding head-on into the strongest point. Front lines shift more intelligently as the bot reads believed control and danger rather than omniscient grids.
_Revives the parked `exp-terr-bias` branch @ ccd12c98 (needs rebase; the per-POI factor was a near-pure damper — the control field is the substrate it actually needed). Completes the @experimental fog migration. Carries a **declared benchmark re-baseline** (instrument change). Design §3.3, Stage F — last, so everything under it is stable first._

### 11. Fires / artillery doctrine cycle (user-confirmed)
**Perceived:** artillery holds standoff range and rains suppressive fire *during* an assault, instead of driving toward the enemy to get into gun range and dying. Assaults look like real combined-arms pushes with the guns kept safely behind.

### 12. Early-game tuning — no idle trucks, proportionate AA, faster spread
**Perceived:** the opening minutes look *purposeful*. No queue of idle supply trucks sitting at the Supply Route, no wall of AA overbuilt against nothing, and a quicker grab for map territory from the beachhead. The bot's first few minutes read as competent rather than fumbling.

### 13. EXPAND benchmark maps — Polar Disorder + Woodland Warfare
**Perceived:** nothing changes in a normal game directly. Behind the scenes the bots' skill is measured on more terrain types (snow, dense woodland), so tuning stops overfitting to the current handful of maps and generalizes better.
_Benchmark instrument work — `WORKSPACE/ai-bench/`._

### 14. AoE-aware cluster targeting in AutoTarget (shared human + bot)
**Perceived:** artillery and other area weapons aim at the *clump* of enemies, not the nearest single target — so a barrage lands where it does the most damage. This is shared micro: **human-owned guns benefit too**, not just the bot.

### 15. LocalRandom seeding / SharedRandom migration for bot decisions
**Perceived:** nothing visible in-game. Same-seed test runs become reproducible, which makes benchmark comparisons trustworthy and removes a latent desync smell. Pure dev/debug quality-of-life.

### 16. Cosmetic — fix visible black batch windows during hidden test runs
**Perceived:** nothing in-game. During hidden test batches the desktop stays clean instead of flashing black windows (SDL minimize not holding on Windows — `WORKSPACE/bugs/discovered.md`, commit e6dc7580).

### 17. (User-deferred) Supply Route capture wiring
**Perceived:** a major new win lever — you can raid and flip the enemy's reinforcement beachhead. Enemy SR → forced neutral → capturable, so knocking out their Supply Route becomes a real strategic goal.
_Deferred by you until the opening-economy AI (item 12) is solid — a bot that can't manage its own economy shouldn't be handed a new economic target._

### 18. (Future) "Should I attack?" endgame decision layer
**Perceived:** bots consciously shift gears — from securing income to committing to a decisive offensive (and later to SR denial) — instead of drifting into an aimless late game. You can watch the AI make the call to go for the kill.

---

## SHIPPED
_Most recent first. Exact wording pulled from git log / HOTBOARD; this is the archive, the commit history is authoritative._

- **Phase-4b role-migration wave (queue item 10)** — air squads, capture and call-in composition consume `UnitRoleResolver` behind `UseUnitRoles` (@experimental only): air-squad membership = AttackAir + Buildable + non-heli (drops airstrike spawns, keeps helis with HelicopterSquad); CaptureCoordinator routes ALL six pool readers through one `CapturerNames` accessor (adversarial-review catch — five raw sites fixed); AdaptiveProduction filters call-in candidates by role category with zero new RNG draws. Bidirectional set-equality lint over DefaultRules catches roster/list divergence both ways. Flag-off byte-identical. NEW BUG found en route: legacy `AirUnitsTypes` case-mismatch no-op — @stable fixed-wing squads never form (`WORKSPACE/bugs/discovered.md`); role mode incidentally fixes it for @experimental. (`232947ce` + `1f00d361`, merged `3bddab50`)
- **Influence Stage E — danger-weighted ground routing (queue item 7)** — ground attacks flow *around* defended cores and supply trucks pull back-lateral-re-enter: `GroundDangerNav` (pure integer, zero-random) computes a bounded two-leg detour waypoint when the straight route's worst-case ground danger exceeds threshold; rear-lateral routing EMERGES from the Stage-C territory baseline gradient, not script. Review blocker fixed (on-map impassable cells read danger 0 and were preferred as waypoints — locomotor passability check on the waypoint cell only) + truck deadband. Default-off flags; @experimental-only via `InfluenceStack.Participates` double-gate on the shared @supply module; flag-off byte-identical. 8 NUnit pins. (`ab7bd283` + `057ab755`, merged `fcb17a86`)
- **Supply truck counts-as-empty + evacuate (queue item 4)** — unusable residue latches counts-as-empty via live tri-state `SupplyProvider.ResidueVerdict` (MinNeedThreshold-aware, NUnit-pinned); Evacuate-stance trucks keep serving below RestockThreshold down to the last usable batch (no trip home to reserve for — adversarial-review blocker catch), then evacuate via `DropsSupplyCache` with red bar; every refill path clears the latch; bot economy layer stops re-tasking evacuating trucks. Gated behind `EvacuateOnUnusableResidue` (TRUK only). (`6fb952c7` + `5863d76a`, merged `f2602e67`)
- **Influence Stage D — heli danger consumer (queue item 6)** — @experimental attack helis consume the air-danger channel: engage cell leashed to the nearest AA-safe cell, lateral detour waypoint when the approach line crosses air danger, spike-triggered withdraw along the least-AA-covered heading. Pure integer nav math (`HeliDangerNav.cs`), zero-random, byte-identical when disabled; rides on Stage-0 standoff (inert when standoff off, review-hardened). 8 NUnit pins. (`36921468` + `867d9d46`, ff-merged)
- **Win-condition fix (queue item 1)** — SR team victory now explicitly awards Won: two-phase `ResolveTeamElimination` (mark eliminated team Lost, then award per-survivor only when every non-allied combatant is Lost — FFA/2v2v2 safe, adversarial-review catch), `AwardVictory` narrowed to CVC-present + Primary objectives (campaign missions untouched), TestMode guard. 6 SR unit tests. (`4ae664b8` + `86e993a6`, merged `5ab49f18`)
- **Cohesion stabilization (queue item 3)** — large-group line extent capped, greedy nearest-slot matching (kills criss-cross), cover-bid-beats-geometry, treeline detection via density-covariance anisotropy → soldiers line up ALONG the treeline; per-order matching memo for O(n²·log n) dispatch. Adversarially reviewed. (`d1858312` + `46a5021a`, merged `786d4770`)
- **Lobby team-selection column (queue item 2)** — per-slot Team column restored (header + editable dropdown + read-only label for remote/bot rows); the stock controls were hidden, not deleted. Screenshot-verified 2v2. (`95329170`)
- **Ambush / undetected-unit behavior — DESIGN** — critical design doc: existing idle-Ambush mechanics mapped (`AutoTarget.cs:511`), three premises corrected against code (prone ≠ concealment; per-unit scan is cheap; late rear-shot spring conflicts with suppression), staged implementation plan, 4 open forks awaiting user review. (`plans/260722_ambush_undetected_design.md`, `1a3f81f1`)
- **Influence Stage C — control field + tri-state /danger overlay** — per-player believed-territory control field (Voronoi seed + capture/persistence/grayzone/anchors) and the green/red/gray danger overlay; zero-SharedRandom stack, ground-only baseline. (`0833b376`)
- **Phase-4 role-model hardening** — unit-role model hardened with ruleset lint table, SF/DR coverage rows, aligned Cargo/carrier predicate + NUnit pins. (`81f52040` / `55326b3e`)
- **Phase-4a role-based tasking** — bot modules consume `UnitRoleResolver` behind `UseUnitRoles` (@experimental only); artillery / SHORAD kept off the line, IFV carriers stay back. (`acc42ad7`)
- **Influence Stages A + B — belief / danger substrate** — per-player belief store (contact memory + decay) and per-domain danger fields (ground + air), pure-data World traits. (`e1e16b95`)
- **Influence Stage 0 — heli standoff** — attack helis use gated attack-move standoff engagement, stop-and-fire at missile range instead of overflying. (`090ad9d0`)
- **Phase-3 human stance defaults + unit-role resolver** — human stance enablement, data-only role resolver, executor hardening (anchor lifecycle, arrival tolerance). (`7f1138e3`)
- **Phase-2 stance positioning executor** — tactical positioning executor for stance-driven unit placement.
- **Phase-1 threat layer + intel overlay** — sighting-based threat perception layer + hold-Space intel overlay substrate.
- **Phase-0 cohesion over-spread fix + re-baseline** — grouped-unit over-dispersion cured, benchmark re-baselined.
- **Sim-throughput 8× harness** — benchmark runs at 8× speed for faster AI iteration.
- **Observer full vision** — observer/replay watches with full-map vision.
- **Ladder regime change + re-baseline** — benchmark ladder regime updated and re-baselined.
- **Live-play crash fixes** — Passenger NRE + transport unload crash.
- **TECN ferry-to-capture** — technician ferried to capture target.
- **Evac + heli tasking fixes** — crew evacuation + helicopter tasking corrections.
