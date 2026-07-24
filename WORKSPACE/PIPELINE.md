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

### 12. Early-game tuning — no idle trucks, proportionate AA, faster spread
**Perceived:** the opening minutes look *purposeful*. No queue of idle supply trucks sitting at the Supply Route, no wall of AA overbuilt against nothing, and a quicker grab for map territory from the beachhead. The bot's first few minutes read as competent rather than fumbling.

### 14. AoE-aware cluster targeting in AutoTarget (shared human + bot)
**Perceived:** artillery and other area weapons aim at the *clump* of enemies, not the nearest single target — so a barrage lands where it does the most damage. This is shared micro: **human-owned guns benefit too**, not just the bot.

### 19. Fires economics — ammo expected-value gate + tube-vs-rocket employment
**Perceived:** bots stop wasting money — no Grad salvo spent on one soldier, no barrage where the shells cost more than the damage they do. Tube artillery still engages singles when nothing better offers; rocket artillery holds for groups. Watching the AI, its fire missions look *deliberate*.
_Doctrine: `DOCS/design/ai-realism.md` §5 (user-authored 2026-07-24) — the general rule is ammo cost < projected damage for ANY weapon. Pairs with item 14 (which is the aim-at-clumps half; this is the is-it-worth-firing half). Quick-fix first (cheap fire-worthiness gate) is acceptable; proper EV model later; continuous-improvement item. Iskander/HIMARS stay out of bot rosters unless a special-case doctrine is designed — price/volatility makes them liabilities as regular artillery._

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

- **EXPAND benchmark maps — Polar Disorder + Woodland Warfare (queue item 13)** — 12 new tournament scenario rungs (`tournament-s{1-eco,2-combat}-{polar-disorder,woodland-warfare}{,-mirror,-cal-nn}`) adapted from the two pre-existing canonical map assets (Polar Disorder: SNOW tileset, 12 OILB; Woodland Warfare: TEMPERAT + 1210 trees, 8 OILB). SRs nudged inward to symmetric anchors (Polar `93,16`/`4,81`, Woodland `3,6`/`94,91`) because the 3×3 SR footprint overflows at native corner spawns. Auto-discovered via mod.yaml MapFolder — no registry edit. Smoke-verified in-game via debug.log: SRs placed at the nudged anchors, both bots spawning + running full module stack, zero exceptions. NOTE: bare `run-test.sh` cannot complete a tournament rung (300s wall watchdog, no TimeLimit/speed injection — that's `run-tournament.sh`'s job), so rung smoke checks read the game log, not the harness verdict. Ladder re-baseline over the new rungs remains DECLARED, awaiting user goahead. (`8a0dce0d`, merged `83a6638a`)
- **Fires / artillery doctrine (queue item 11)** — @experimental IndirectFire pieces peel off the grouped axis AttackMove and hold a weapon-range standoff anchor (`maxWeaponRange − FiresStandoffMargin` on the target→piece bearing, recomputed from the live axis target each re-eval): advance-to-range, hold-and-fire via AutoTarget, back off when the target closes. Adversarial-review catch: a raw anchor on impassable ground caused a per-interval re-order loop cancelling in-flight shots — fixed with `NearestPassableCell` (deterministic Chebyshev-ring clamp, budget 4) + a never-re-order-identical-destination gate. Flag `FiresStandoff` default-off (@experimental only); flag-off byte-identical by construction; zero new RNG. 12 NUnit pins (378 total). (`f20d2798` + `3aca99a1`, merged `6a33813d`)
- **Influence Stage F — strategic repoint + territorial balance-of-power revival (queue item 9)** — @experimental attack-axis selection drops the omniscient InfluenceMap threat term (`GetOffensiveTargets(perspective, suppressOmniscientThreat)` — default-false, all other callers byte-identical) and re-derives it from BELIEVED fields: balance-of-power reads the control of the ring AROUND each target at `AnchorRadiusCells+1` (adversarial-review catch: every enemy target is a site-anchor structure whose own cell floors ≈−800 → the boost never fired; ring read restores "press the encircled enemy" ×150 / damp lunging into believed strength ×60 / contested neutral) + believed-danger damp on its own threshold scale. Zero RNG, default-off flags, 14 NUnit pins (366 total). Completes the @experimental fog migration for attack axes; capture/garrison omniscient reads documented as deferred follow-on. DECLARED benchmark re-baseline not yet run (awaits test grant). Terr-bias revival — supersedes parked `exp-terr-bias` (branch kept, worktree removed). (`16e0e673` + `fba22955`, merged `36bd3b9e`)
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
