# Watchability research — making bot-vs-bot WW3MOD worth watching

**Status: research (2026-07-22). No code changes.** Read-only analysis answering a
north-star question that nothing in the current roadmap owns: *what makes
machine-vs-machine RTS watchable, and which WW3MOD levers buy the most watchability
per unit of effort?* Positioned against the ratified
[`260722_strategic_tactical_split_SPEC.md`](260722_strategic_tactical_split_SPEC.md)
(phases 0→5) and the AI-bench ladder (`WORKSPACE/ai-bench/`). Repo claims cite
`file:line`; verified against `main @ 1eb644de`.

---

## 0. The one-paragraph thesis

Two facts frame everything below. **First: the presentation substrate already
exists and is mostly unused.** WW3MOD ships the full OpenRA observer suite —
8 stat panels including an income graph and an army-value-over-time graph, a
shroud selector with "Disable Shroud", automatic replay recording — and it is
*wired into the mod* (`mods/ww3mod/mod.yaml:173`, `chrome/ingame-observer.yaml`),
not stripped. Nobody has pointed it at a bot game. **Second: the thing that most
kills watchability is a content problem the benchmark has already measured** —
same-faction bot games collapse into passive economy races (S2 combat: engagement
down 5–6×, **3/10 matches zero-combat**; REVIEW *Needs attention* §3). A perfectly
directed camera pointed at two turtling economies is still boring. So the highest-
leverage work is *not* building presentation from scratch (it's largely there) —
it's (a) cheaply *packaging* the existing observer + ladder into a spectator
product, (b) adding the one missing presentation keystone (an auto-director
camera), and (c) treating "the bots must actually fight, and fight legibly" as a
first-class product goal, not just a measurement guard.

**The sharpest strategic insight:** the choices that make the AI *measurable* are
the same choices making it *boring*. The benchmark deliberately runs same-faction
US-vs-US, one map (River Zeta), a fixed 10-seed set, an indestructible Supply
Route, and passivity-tolerant bars — every one of those *reduces variance for
measurement* and *reduces watchability* (sameness, no knockout, no drama). A
watchability product needs a **different configuration profile** than the
benchmark profile. Keep them separate; don't corrupt the benchmark to entertain,
and don't ship the benchmark's boring profile as the show.

---

## 1. Findings from precedents — what actually drives watchability

I looked at the machine-vs-machine scenes that found audiences (SC2 AI Arena,
AlphaStar, TCEC, Battlesnake) and at RTS spectator UX generally (SC2/AoE2
observing). The recurring drivers, ranked by how consistently they showed up:

### 1.1 Legible intent is non-negotiable — and it does not come for free
AlphaStar is the cleanest lesson. DeepMind's agents didn't move a camera the way a
human does (they see the whole map at once), so **the 10 exhibition replays were
post-processed with *heuristic camera movements* so the target of each agent action
was on screen** — explicitly "to make the replays easier to follow." Without that
post-processing the games were technically superb and unwatchable. The general RTS-
spectator research says the same thing from the other side: good observing is
*selective attention* — "the observer usually will only show you something of
importance," and automating that ("storyline ranking" that scores in-game events
and cuts the camera to the highest-ranked one) is an active patent/research space.
**Takeaway for us: a bot game with no directed camera is illegible by default.
Camera-follows-intent is a hard requirement, not a nice-to-have.**

### 1.2 Stakes + a persistent ladder create the reason to return
Every scene that *sustained* an audience is built on an always-on ranked ladder,
not one-off matches. Battlesnake runs fully-automated TrueSkill leaderboards on a
**daily** cadence ("once a day, at the same time, each Leaderboard initiates a
series of games"). SC2 AI Arena ran **190,000 ranked 1v1s** across a season with a
**permanent 24/7 Twitch stream**. TCEC broadcasts **24/7** with rotating formats
(Leagues, Cup, Swiss) to keep it fresh. The ladder is what turns isolated matches
into a standings story with rivalries and momentum. **We already have a ladder
instrument** (`@experimental` vs frozen `@stable`, paired seeds, MMR-like
comparisons) — but it's a *measurement* tool, not a *spectator* product.

### 1.3 Personality / named competitors carry the narrative
Battlesnake and AI Arena both revolve around *named bots with recognizable
behaviors* and *named developers* climbing rankings — "recognizable top developers
that give the community its personality." TCEC viewers follow specific engines
(Stockfish vs Leela) as characters. Our bots are `@experimental` / `@stable` — dev
labels, not characters. Distinct **doctrines** (an aggressor vs a defender) that
*read differently on screen* are the personality lever.

### 1.4 Novelty & emergent drama — including glitches — are a feature
AI Arena viewers explicitly enjoy "behavior that human players never would" —
superhuman micro *and* "bizarre bot glitches" (units spamming move orders at bases
that no longer exist). The unpredictability is the entertainment. This cuts against
our benchmark's determinism (fixed seeds → identical replays → *zero* novelty
across reruns). Watchable games want variance; the benchmark wants none.

### 1.5 Commentary and information-rich overlays do the legibility work
TCEC and AlphaStar both hired expert casters (Artosis/RotterdaM for AlphaStar).
SC2's spectator craft leans on the **production tab** (army composition, units
lost, upgrade timings), a **selection/focus panel** that spotlights the one
important unit, and — critically — the observer's ability to **reveal both sides'
fogged information at once** so a caster can narrate an army-composition mismatch or
a flank neither *player* can see. Interestingly, SC2 casters agree the **macro /
army-value graph often matters more than flashy micro** for telling the story.
We already ship the army-value graph and a disable-shroud view; we lack the caster.

### 1.6 Fairness and equal information make outcomes *mean* something
TCEC's appeal rests partly on rigorous fairness — identical hardware, openings,
time controls — so results reflect "pure algorithmic strength." AlphaStar's
full-map-vision advantage was the community's main *complaint*, "remedied" only in
the final match where the human won. **This validates Phase 4** (full fog
migration): bots reasoning on omniscient grids isn't just a design smell, it's a
*watchability* liability — an unfair-feeling AI is a less compelling one, and
equal-information games produce the surprise-attack drama fog enables.

**Sources:** [SC2 AI Arena](https://aiarena.net/) ·
[AI Arena wiki](https://aiarena.net/wiki/bot-development/) ·
[AlphaStar (DeepMind exhibition write-up)](https://bartl.io/blog/alphastar/) ·
[AlphaStar resources](https://starcraft.ai/research/alphastar-resources) ·
[TCEC (Wikipedia)](https://en.wikipedia.org/wiki/Top_Chess_Engine_Championship) ·
[The Role of TCEC in Computer Chess](https://ijccrl.com/the-role-of-tcec-in-computer-chess/) ·
[Battlesnake leaderboards](https://docs.battlesnake.com/guides/leaderboards) ·
[SC2 custom observer UI (PCGamesN)](https://www.pcgamesn.com/starcraft/starcraft-2s-new-observer-ui-mod-tool-should-make-better-esports-broadcasts) ·
[SC2 viewer guide](https://www.esportsvikings.com/starcraft2/guides/sc2-viewer-guide) ·
[Observer: automating RTS spectator (ScienceDirect)](https://www.sciencedirect.com/science/article/pii/S2352711025003875) ·
[Designing Spectator Interfaces (Chalmers thesis)](https://publications.lib.chalmers.se/records/fulltext/224247/224247.pdf)

---

## 2. Anti-watchability failure modes — which ones WW3MOD actually risks

Ranked by how strongly the current model + benchmark data indicate the risk is
*real for us* (not hypothetical):

1. **Passive stalemate / economy-race dominance — HAPPENING NOW.** The benchmark
   already measures it: on the same-faction Stable-vs-Stable regime, S2 combat
   engagement collapsed ~5–6× and **3/10 matches were zero-combat** (LADDER regime
   banner; REVIEW *Needs attention* §3). Machine games with no fight are the single
   worst watchability outcome, and we have direct evidence WW3MOD produces them.
2. **Sameness across games — STRUCTURAL.** Same-faction US-vs-US means **both bots
   field the identical Motorized starting force** (LADDER regime banner); one map
   (River Zeta); a fixed 10-seed set that replays *byte-identically* (deterministic
   seeding, `World.cs:213-214`). Great for paired measurement, but every game looks
   the same — the opposite of §1.4's novelty driver. Faction balancing is
   explicitly deferred, so visual variety is deferred with it.
3. **Decided-early-but-drags — STRUCTURAL, model-specific.** The Supply Route is
   **indestructible** (`Armor: Indestructable`) and SR *capture* is **not wired**
   (`SUPPLYROUTE` has no `Capturable`/`CaptureManager`; `game-model.md`). There is
   no "base dies → GG" knockout. A game decided at minute 4 has no climax; it grinds
   to the score/time limit. RTS drama peaks at the decisive moment — our model
   currently removes it.
4. **Imperceptible decision-making — STRUCTURAL.** Bot intent (POI capture,
   SR-pressure axes, cohesion doctrine) lives in log lines, not on screen. Units
   enter from the **map edge** and march across (`game-model.md`), so the most
   interesting decision — where to commit reinforcements — happens off-camera. With
   no auto-director (see §3) a viewer cannot tell *why* anything is happening.
5. **Jittery / illegible unit blobs — BEING FIXED.** The cohesion over-spread bug
   smeared formations into thin unreadable lines (DISCOVERIES 2026-07-22; footprint
   grew unbounded with unit count). Phase 0 already caps this (`1eb644de`) — tight
   formations read as deliberate; spread blobs read as noise. Watchability was an
   unstated beneficiary of a fix shipped for other reasons.

---

## 3. Ranked levers

Scored by **watchability gained per unit of effort**, best first. Category tags:
**[B]** bot-behavior (drama, pacing, legible intent) · **[P]** presentation
(observer UI, overlays, camera, graphs) · **[F]** format (ladders, casts,
personalities, variety).

| # | Lever | Cat | Effort | Slot vs phase plan |
|---|---|---|---|---|
| L1 | **Spectator-package the existing observer + ladder** — point the already-wired observer UI at a bot game; name the bots; expose standings; add a telemetry caption feed from the score/kill/capture log | P+F | **Low** | **Parallel** — needs nothing from the plan; assets already ship |
| L2 | **Auto-director camera** — cut the spectator viewport to the largest live engagement / most recent event | P | **Med** | **Parallel** (can later read Phase-1 sighting layer, but doesn't need it) |
| L3 | **Anti-stalemate pacing** — make bots *want* to contest the middle; watchability-explicit objective, not just a min-engagement measurement guard | B | **Med** (partly in-flight) | Overlaps Phase 1 (BoP/territorial layer) + Phase 4 (fog makes aggression meaningful) |
| L4 | **Repurpose the Phase-1 intel overlay for broadcast** — BoP color wash + last-seen GPS dots as a *spectator* layer, not only a dev tool | P | **Low** | **After Phase 1** — nearly free once §3d lands; just expose it in observer mode |
| L5 | **Legible-intent unit micro** (treeline, hull-down, bounding) — units that visibly *use terrain* read as intelligent | B | High — **but already ratified** | **IS Phases 2/3/5** — watchability is a *dividend* of work already committed |
| L6 | **Variety** — faction asymmetry, map rotation, composition diversity | B+F | Med–High | **After** ladder stabilizes; in *tension* with the benchmark's same-faction/one-map profile |
| L7 | **Decisive-climax mechanic** — wire capturable Supply Routes so games can end in a knockout, not the clock | B+F | Med | Independent; **EXPERIMENTAL only** (RELEASE v1 is scope-locked) |
| L8 | **Named AI doctrines / personalities** — an "aggressor" (Hunt) vs a "defender" (Defensive) with visibly different behavior | F | Low–Med | **After Phase 3** — stance-driven micro gives doctrines a visible fingerprint |
| L9 | **Continuous / scheduled exhibition cadence** — a daily or always-on bot stream on the batch harness | F | Low (plumbing) | Parallel; depends on L1 existing first |

### 3a. Bot-behavior levers [B]
The content layer — no presentation saves a game with no drama.
- **L3 (anti-stalemate)** is the highest-ceiling behavior lever *and* the one the
  data most demands (§2.1). The existing SR-contestation axis and the Phase-1
  territorial/BoP layer are the substrate; what's missing is a *watchability-
  explicit* drive to force decisive engagements (the benchmark's min-engagement
  floor only *detects* passivity, it doesn't *cure* it). Phase 4's fog migration
  compounds this positively: once bots can't see everything, scouting and pressing
  an advantage become meaningful, dynamic behavior.
- **L5 (legible-intent micro)** is already the ratified plan. Worth stating plainly
  in watchability terms: the treeline/hull-down/bounding behaviors of Phases 2–3–5
  are *exactly* the AlphaStar legibility lesson (§1.1) applied at the unit level —
  a unit that ducks into a treeline facing the threat *reads as thinking*. This is
  free watchability riding on work committed for other reasons. **Do not re-scope
  it; just claim the dividend.**
- **L7 (capturable SR)** is the boldest content lever: it restores the RTS
  knockout and directly kills failure mode §2.3. But it changes the core game model
  and RELEASE v1 is scope-locked — so it belongs in EXPERIMENTAL, and it's higher-
  risk (SR capture is entirely unwired today). High drama ceiling, real cost.
- **L6 (variety)** attacks the sameness failure mode (§2.2) but collides with the
  benchmark's deliberate same-faction/one-map determinism. Resolve the collision by
  *profile separation*: the show runs asymmetric factions + rotating maps; the
  benchmark keeps its controlled profile.

### 3b. Presentation levers [P]
**The big finding: most of this already exists.** The observer suite is wired into
ww3mod (`mod.yaml:173` loads `chrome/ingame-observer.yaml`; observer hotkeys at
`mod.yaml:249`), and it is rich:
- **8 stat panels** — Basic (incl. APM), Economy (income/earned/spent/derricks),
  Production, Support Powers, Army, Combat (assets destroyed/lost, army value,
  vision %), plus two **graphs**: an **income-over-time** line graph
  (`ingame-observer.yaml:1055`) and an **army-value-over-time** line graph
  (`:1081`, `:1091` `YAxisLabel: Army Value`). Army-value-over-time is the exact
  macro-story graph SC2 casters lean on (§1.5).
- **Shroud selector** with All-Players, per-player, and **"Disable Shroud"** views —
  the reveal-both-sides'-fog capability §1.5/§1.6 calls the heart of RTS casting.
- **Automatic replay recording** to `.orarep` on every match, with playback +
  pause + 4 speed tiers (the autotest harness already records these by default).
- Minimap click-to-pan; radar pings with a **`LastPingPosition`** and a
  **jump-to-last-event** hotkey — a ready-made hook for L2.

So the presentation *gap* is narrow and specific:
- **L2 (auto-director camera) is the missing keystone.** Everything above assumes a
  human observer is *driving the camera*. A bot exhibition has no driver, and §1.1
  proves an undriven camera is unwatchable. The engine already exposes the
  primitives — `MiniMapPings.LastPingPosition`, a jump-to-last-event centering
  hotkey, and Lua `Camera.Position` — plus the per-tick engagement data (kills, the
  score log) to compute an engagement centroid. A modest "action director" that
  cuts to the biggest live fight is the single highest-value *new* presentation
  build. There is **no** combat/auto-follow camera in the engine today (confirmed
  gap) — this is genuinely new work, but small and self-contained.
- **L4 (broadcast intel overlay)** is nearly free: Phase 1 already builds the
  hold-Space BoP color wash + GPS-dot last-seen layer (SPEC §3d) — currently framed
  as a dev/human-play tool. Exposing it in observer mode turns it into the
  both-sides-hidden-info broadcast layer §1.5 describes, at almost zero marginal
  cost once Phase 1 lands.

### 3c. Format levers [F]
- **L1 (package the ladder as a product)** is the cheapest high-value move in the
  whole list. We *have* a ladder (`@experimental` vs frozen `@stable`, paired
  seeds, standings in `WORKSPACE/ai-bench/`). §1.2 says the ladder is what sustains
  an audience — but ours is a spreadsheet for the developer, not a standings story
  for a viewer. Naming the bots (§1.3), surfacing standings, and generating a
  **telemetry caption feed** (the game already emits per-tick score, kills,
  captures, and SR-pressure events — cheap to turn into "commentary" text) converts
  existing assets into a spectator product with no engine work.
- **L8 (personalities)** builds on L1 + Phase 3: once the Engagement stance drives
  visible micro, an "aggressor" doctrine (Hunt: push to the treeline, press
  contact) and a "defender" (Defensive: hull-down, hold) *look* different on
  screen — recognizable characters, per §1.3.
- **L9 (cadence)** is the always-on/daily rhythm every sustained scene has (§1.2).
  It's mostly plumbing on the existing batch harness, but it's worthless until L1
  makes a single game watchable — so it's downstream.

---

## 4. Top-3 "do these next"

**1 — Spectator-package the existing observer + ladder (L1). Do this first.**
It's the lowest-effort item on the board and it unblocks the rest. The observer UI
with an army-value graph, a disable-shroud reveal, and replay recording is *already
shipping in ww3mod* — the work is packaging, not building: point it at a bot match,
name the two bots, surface the ladder standings we already compute, and auto-
generate a caption feed from the score/kill/capture events the engine already
emits. This is also the diagnostic that lets us *see* the content problems in §2
with our own eyes instead of inferring them from verdict JSON. Cheapest watchability
per unit of effort by a wide margin; it should run **parallel** to the phase plan
and depends on nothing in it.

**2 — Build an auto-director camera (L2). The missing keystone.**
AlphaStar's exhibition is the proof: without heuristic camera-follows-intent, even
world-class machine play is unfollowable. A packaged observer (rec 1) still needs a
*driver*, and a bot game has none. The engine already gives us the primitives —
`LastPingPosition`, a jump-to-last-event centering hotkey, and Lua `Camera.Position`
— and the per-tick engagement data to pick a target, so a first "cut to the biggest
live fight" director is small, self-contained, and has no dependency on the
strategic/tactical split. It's the difference between "a top-down blob" and "a
match you can follow." Medium effort, keystone impact; run **parallel**, layering in
the Phase-1 sighting layer later for smarter shot selection.

**3 — Make anti-stalemate pacing a first-class goal (L3). The content fix.**
Presentation cannot rescue a fight that never happens, and the benchmark *already
proves* our bots go passive (3/10 zero-combat). The benchmark's min-engagement floor
only *detects* this; watchability needs bots that *want* to contest the middle. This
overlaps work already on the roadmap — the Phase-1 territorial/BoP layer is the
substrate, and Phase 4's fog migration makes aggression and scouting genuinely
dynamic (§1.6) — so the ask is to add a **watchability-explicit objective** to that
line of work, not to open a new front. Highest ceiling of the three; partly in-
flight; keep it isolated from the benchmark's controlled profile so measurement
stays clean.

**Deliberately *not* in the top 3:** legible-intent micro (L5) is high-value but
already ratified as Phases 2–5 — claim the dividend, don't re-scope. Capturable SRs
(L7) have the highest drama ceiling but change the core model and belong in
EXPERIMENTAL, not the next-three. Variety (L6) and cadence (L9) are real but
downstream of a single game being watchable at all.

---

## 5. Cross-cutting recommendation: separate the "show profile" from the "benchmark profile"

The most important structural takeaway (§0, §2). The benchmark's design choices —
same-faction US-vs-US, one map, fixed byte-identical seeds, indestructible SR,
passivity-tolerant bars — are *correct for measurement* and *wrong for a show*.
Don't try to make one configuration serve both. Define a **watchability profile**:
asymmetric factions, rotating maps, non-deterministic seeds (novelty, §1.4), a
forced-contact or capturable-SR climax, and a director camera + broadcast overlay —
and keep the benchmark profile frozen and controlled as it is. The bench answers
"is the AI better?"; the show answers "is the AI *fun to watch* being better?" They
are different questions and want different rigs.
