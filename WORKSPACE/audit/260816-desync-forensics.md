# Desync forensics — 2026-08-16 two-human multiplayer

**Status:** read-only diagnosis. Nothing fixed, nothing committed, no game or autotest run.
**Evidence:** `WORKSPACE/audit/logs-260816-snapshot/Logs/syncreport-2026-08-16T162525Z-1.log` (76,937 lines).
**Researched against:** `main` @ `81e5a440`, 0 commits behind `origin/main`. The report's build string
`81e5a44046/b45c4eb7/1749f466` matches this checkout exactly, so every line number below is the code
that actually ran.

---

## 1. The divergence point

**Out-of-sync net frame: 1264. World tick: 3792.**

The frame number is stated outright in the report footer (`Out of sync frame: 1264`). The world-tick
mapping is derived, not assumed: OpenRA runs 3 world ticks per net frame, 1264 × 3 = 3792, and the
report independently corroborates it — the 139 `StancePositioningExecutor` instances carry
`nextEvalTick` values spanning 3789–3818, i.e. a staggered scan window opening exactly at 3792.

State captured at that frame:

| Item | Value |
|---|---|
| `SharedRandom` | `1077851139 (#18240)` — RNG state and cumulative draw count |
| Orders issued on the frame | exactly one: `ClientId: 7`, `StartProduction`, `Subject: player 4`, `TargetString: e4.america`, from **Commanderbambi** (the non-host) |
| Actors owned by FreadyFish (host) | 40 |
| Actors owned by Commanderbambi | 116 |
| Actors owned by Neutral | 4558 |
| Degraded sequences | 6, **digest `cfd39710`** |

### The differing field cannot be named from this report, and here is why

This is the central finding and it needs to be stated plainly rather than worked around.

`SyncReport.DumpSyncReport` enumerates `orderManager.World.ActorsHavingTrait<ISync>()` — **the local
machine's own state only**. Both machines write a report; neither contains the other's numbers. The
host's report therefore records *that* frame 1264 hashed differently from the value received over the
wire, and *what the host believed at that moment*, but it contains no second column to diff against.

The report contains only **one** frame block despite the footer listing 32 recorded frames
(1234–1265) — OpenRA dumps the mismatching frame alone.

**The good news is that the report is built to be diffed.** The integer in parentheses after every
trait name is that trait instance's sync hash:

```
	 4571 e4.america Commanderbambi StancePositioningExecutor (3792)
	 4573 e4.america Commanderbambi StancePositioningExecutor (2100927)
	 4571 e4.america Commanderbambi Detectable (5)
```

So a line-by-line diff of the host's frame-1264 block against **Commanderbambi's frame-1264 block**
lands on the exact actor ID + trait + field. That single artifact converts this from inference to
fact. See §4.

### Which side is wrong

**Undetermined, and not determinable from one report.** Note that the desync was detected on the host
while the only order in flight on that frame originated from the *client* — suggestive but not
probative, since orders on a net frame execute on both machines and one order per frame is
unremarkable.

---

## 2. Suspects — each explicitly confirmed or excluded

### 2a. Bot modules writing synced state directly — **EXCLUDED**

Fixed at `91056894` for `PoiOffensiveBotModule.SweepEjectedCrew`, `.EvacuateOutOfAmmoUnit`,
`HelicopterSquadBotModule.Evacuate`. The failure shape fits perfectly — `Player.cs:224-232` activates
bot logic only under `IsBot && Game.IsHost`, so a bot write mutates the host alone.

**It did not happen here.** The report contains 205 bot-module trait entries, spread 41 each across
all five players (Neutral, Creeps, FreadyFish, Commanderbambi, Everyone). I filtered for any entry
whose state was not `IsTraitDisabled: True` and got **zero**. Every bot module in the game was
disabled. There was no live bot on any player, including the two humans.

This also rules out the whole class, not just the three fixed call sites: no bot logic ran at all.

### 2b. `EjectRallyOrderGenerator` client-local rally point — **LIVE SUSPECT, UNCONFIRMABLE FROM THIS REPORT**

Confirmed in code: `EjectRallyOrderGenerator.cs:62` calls `cargo.SetEjectRally(...)` directly and
yields no `Order`, so the rally point exists only on the ordering client. `Cargo.cs:190` stores it in
`readonly Dictionary<uint, Target> ejectRallyPoints` — **private, no `[Sync]`**. `UnloadCargo.cs:131`
reads it back via `GetEjectRally` and turns it into a real `Move` at `:157-160`. That is genuine
client-local state steering the simulation.

Transports were in play. Human-owned actors carrying cargo at frame 1264:

- `4586 bmp2` — FreadyFish
- `4651 m113`, `4658 bradley`, `4701 strykershorad` — Commanderbambi

**Why the report cannot settle it:** neither `Cargo` nor `WithCargoPipsDecoration` declares any
`[Sync]` field. Cargo state is not in the sync hash at all, so an eject-rally divergence is invisible
here *and would be invisible in the friend's report too*. It can only be caught downstream, once the
resulting `Move` moves a unit to a different cell on one machine — which would then surface as a
`Mobile` (`FromCell`/`ToCell`) or `TopLeft` mismatch on the passenger.

**This is a question for the players, not the log.** See §4.

### 2c. `AttackMoveActivity` halt-before-contact — **EXCLUDED for this game**

`AttackMoveActivity.cs:155-158` gates the halt behind
`autoTarget.Stance == UnitStance.Ambush && self.GetConditionCount(ambushGate) > 0`, and the halt
decision then runs `GroupDetectedBy` → `CanBeViewedByPlayer` — a visibility predicate, which is the
right shape for a desync.

But `AmbushTacticsCondition` is granted **only by `LaneAmbushBotModule`**. No YAML rule grants it to
a human-controlled unit. And per §2a, all ten `LaneAmbushBotModule` instances in this game
(lines 21, 73, 133, 185, 245, 297, 339, 391, 433, 485 of the report) are `IsTraitDisabled: True`.
`GetConditionCount(ambushGate)` was 0 on every unit, so the halt branch was structurally unreachable.

**Consequence for the headline question:** this is the located-but-unfixed cause of the *saved-game
restore* desync. It could not have fired here.

### 2d. Content / sequence divergence — **UNRESOLVED, CHEAPEST TO SETTLE, HIGH VALUE**

The host reports `Degraded sequences: 6 (digest cfd39710)`, from missing `b2bomb.shp`,
`pip-cloak.shp`, `pip-cover.shp`. Sequence definitions are render-only and do not themselves feed the
simulation, so the *missing sprites* are not a desync mechanism on their own.

That is not the reason to care. The reason to care is that a non-zero degraded digest is proof the
content tree is incomplete, and this mod hard-depends on the `ra` mod for content with a dead
installer path. **If the two machines resolved different content, they can load different rules.** The
digest is a one-line, zero-cost check that either eliminates this entire branch or promotes it to
cause.

Caveat on a nearby file: `debug.log` in the snapshot is **not from the match** — it ends at
`SetupShellmapBots: Injected 2 bots for map 'Nuclear Winter WW3'` and `Scenario selection: 'Shellmap'`,
i.e. a later menu session. Likewise `server.log.1` is dated 2026-08-10. Do not read match behaviour
out of either. The degraded-sequences line quoted above is taken from the **sync report's own header**,
which is genuinely the match's value.

### 2e. `StancePositioningExecutor.lastAcceptedBearing` — **HYPOTHESIS, NOT PROVEN. Do not adopt.**

Flagging this because it is the mod's largest unhashed→hashed seam and it will be the first thing the
diff points at if the diff points anywhere near stance logic.

The mechanism, verified in code: `lastAcceptedBearing` is `[Sync]` (`StancePositioningExecutor.cs:157-158`)
and is written at `:484` from `ComputeThreatBearing`, which aggregates over
`threatLayer.ActiveCells(player)` (`:453`). `SightingThreatLayer` builds that per-player field from
`CanBeViewedByPlayer(player)` (`SightingThreatLayer.cs:207,215`) and `player.FrozenActorLayer` (`:225`) —
i.e. fog-derived intel. `SightingThreatLayer` itself declares no `ISync`, so the intermediate field is
**not** in the sync hash; only its downstream effect on the bearing is. An unhashed intermediate
feeding a hashed field is exactly the structure that produces late, confusing desyncs.

**Why I am nevertheless not calling it the cause — I tried to convict it and failed on three counts:**

1. *Per-player is not client-local.* `Recompute()` (`:126-155`) iterates `world.Players` and builds a
   field for **every** non-spectating player on **every** client. Shroud is replicated, and
   `FrozenActorLayer` is itself `ISync` with `[Sync]` fields (`FrozenActorLayer.cs:247-252`), so the
   engine already guarantees both inputs are byte-identical across machines. This is precisely the
   per-player-vs-client-local trap that has dissolved earlier theories on this bug.
2. *Iteration order is irrelevant.* The aggregation at `:453-467` is pure commutative integer
   accumulation (`sumIntensity`, `dirX`, `dirY`), so an unstable enumeration order over `ActiveCells`
   could not change the result. That sub-hypothesis is dead.
3. *The scheduling test came back negative.* If this trait were implicated I would expect its
   evaluation to cluster on the OOS tick. It does not: of 139 instances, `nextEvalTick` values are
   evenly staggered across 3789–3818 and **only 2 land on 3792** — exactly the uniform spread the
   stagger is designed to produce. No anomaly.

Also checked and clean: `nextEvalTick` is seeded from `world.SharedRandom` (`:204`, and
`SightingThreatLayer.cs:119-123`), not a local RNG. A repo sweep for `Game.CosmeticRandom`,
`new Random(`, and `Random.Shared` inside `[Sync]`-declaring traits returned nothing.

### 2f. The `StartProduction` order on the OOS frame — **OPEN, LOW PRIORITY**

The sole order on frame 1264. `ProductionQueue.cs:441-515` handles it deterministically. The RNG does
enter production, but downstream at completion, not at queue time: `Production.SelectExit` →
`RandomExitOrDefault` shuffles exits via `world.SharedRandom` (`Exit.cs:89`). That draws from the
shared stream and is sync-safe *provided both machines reach it on the same tick*. Worth noting only
because a production completion is one of the few things in this mod that consumes shared RNG draws —
which makes the `#18240` draw count a sharp instrument (§4).

### 2g. The crash 7 minutes later — **NOT CONNECTED on current evidence**

`exception-2026-08-16T163234Z.log` is `Game.LoadShellMap()` → `MapStartingLocations` →
`Session.ClientInSlot` throwing `Sequence contains more than one matching element`
(`Session.cs:94`). That is the **main-menu shellmap** loading after the match ended, not the match
crashing. It indicates a session with two clients in one slot, which is its own bug and is already
tracked in `WORKSPACE/audit/260816-crash-clientinslot.md`. I found no evidence tying it to the
desync and am not claiming one.

---

## 3. Is this the same bug as the known restore desync?

**No — different bug, on the evidence available.**

The restore desync's located cause is the `AttackMoveActivity` ambush halt (§2c). That path requires
`AmbushTacticsCondition`, which only `LaneAmbushBotModule` grants, and every instance of that module
was disabled in this game. The condition count was 0 on every unit on the field, so the branch could
not execute. This desync happened in a live two-human match with no bots and no save/restore involved.

Stated as strongly as the evidence allows: the known cause is *excluded*, so this is a second,
distinct desync. What I cannot yet say is what the new one is.

---

## 4. What would close the case

Ranked by decisiveness per unit of effort. The first two cost the user a message to their friend.

1. **Commanderbambi's sync report for game `d9a125b0-cfd6-4315-898e-3ac041684cd3`.** This is the whole
   ballgame. Ask for `syncreport-*.log` from his `Logs` folder with a timestamp near
   `2026-08-16T16:25Z`. Then diff the frame-1264 block against the host's. Because every trait line
   carries its own hash in parentheses, the first differing line names the actor ID, the trait, and
   the field. Everything else in this document is a prior; that file is the posterior.
   - Compare **`SharedRandom`** first. Host: `1077851139 (#18240)`. If the *state* differs but the
     *draw count* `#18240` matches, the two machines drew the same number of times from streams that
     had already diverged — the divergence is upstream of frame 1264 and something is consuming RNG
     differently. If the **count** differs, one machine made extra draws, which points hard at
     production/exit selection (§2f) and gives an exact draw delta to hunt.
2. **His degraded-sequences digest.** One line in his report header. Host: `cfd39710`. Same digest ⇒
   §2d is eliminated outright. Different digest ⇒ content divergence is promoted from hypothesis to
   cause and this becomes an install/packaging bug rather than a simulation bug.
3. **Ask both players a single question: did either of you set an eject rally point on a transport?**
   (bmp2 / m113 / bradley / strykershorad were all on the field). §2b is real, it is unfixed, and it is
   structurally invisible to the sync report because `Cargo` has no `[Sync]` fields. A "yes" makes it
   the leading cause immediately. A "no" from both retires it for this incident — though the code
   defect remains and should still be fixed.
4. **NEEDS A RUN** — only if 1–3 all come back clean, and only with the manager's authority:
   ```
   ./run-test.sh <a two-client scenario that unloads a transport onto an eject rally point>
   ```
   This would prove or disprove §2b directly by driving the one mechanism the report cannot observe.
   It is deliberately last: it costs minutes and steals focus, and items 1–3 cost a chat message and
   may well end the investigation before a run is needed.

---

## 5. Confidence

- Frame 1264 / world tick 3792 — **certain** (stated in the report; tick mapping cross-checked).
- Bots excluded — **certain** (205/205 entries disabled).
- Restore-desync cause (`AttackMoveActivity`) excluded — **high** (condition grantable only by a
  module that was disabled on every player).
- Eject rally (§2b) as leading *candidate* — **moderate**, and explicitly unfalsifiable from this
  artifact alone.
- Content divergence (§2d) — **unknown**, one line of evidence away from resolved.
- Stance bearing (§2e) — **low**; I attacked it and it survived, which is not the same as it being
  innocent, but it is not the cause I would name today.

No claim in this document should be promoted without the frame-1264 diff from the second machine.
