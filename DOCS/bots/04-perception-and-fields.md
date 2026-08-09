# 04 — Perception: what the bot believes, and on what scale

**Researched against `main` @ `910507c1`** (`git status -sb`: `main...origin/main [ahead 67]`, working tree
clean apart from four known untracked scratch paths). Static read only — no build, no game run, no autotest.
Every factual claim below carries a `file:line` that I opened and read at that commit.

> **A worker is concurrently retuning some of these numbers on branch `auto/danger-scale`.** This document
> describes **`main` as it stands at `910507c1`**. If a threshold quoted here does not match the code you are
> reading, check whether that branch has merged before assuming this document is wrong — but also re-derive it,
> because the whole point of the document is that these numbers were never derived in the first place.

**What this document is.** Everything the strategic layer *perceives*: the four fields it reads, what a number
in each of them actually means, which module reads which field at which threshold, and where those thresholds
are meaningless. It deliberately does **not** re-describe the tick path and order arbitration
([`02-lifecycle-and-arbitration.md`](02-lifecycle-and-arbitration.md)), the module catalogue, or the squad
state machines — those are other documents in this folder. The implementation-level reference for the same
stack is [`influence-stack.md`](../reference/influence-stack.md); this document is the *reader's* view and, on
several points, corrects it.

**Framing you must not lose.** WW3MOD is a **total conversion**, not a Red Alert mod
([`game-model.md`](../reference/game-model.md)). That matters here more than almost anywhere else in the
codebase, because the perception layer's arithmetic reads its inputs straight out of the ruleset — and the
ruleset was rescaled by two to three orders of magnitude relative to RA while the constants that consume it
were not. **The single most important finding in this document is that essentially every absolute threshold in
the bot's perception layer is an RA-era number being compared against a WW3MOD-era field.**

---

## How to read this document

Two markers, matching [`02-lifecycle-and-arbitration.md`](02-lifecycle-and-arbitration.md).

**Provenance** — where a component came from:

| Marker | Meaning |
|---|---|
| **[OpenRA]** | Inherited from OpenRA `release-20230225` essentially unchanged. |
| **[MODIFIED]** | OpenRA structure, WW3MOD changed its behaviour or added fields. |
| **[WW3MOD]** | Written for this mod. No OpenRA ancestor. |

**Opinion** — every paragraph beginning **`OPINION:`** is my assessment, not a description of the code. The
`file:line` claims are the part that should be checkable and correct; the opinions are the part you should
argue with.

Numbers in tables are **computed by hand from the cited ruleset values** using the cited formula. I did not run
the game. Where a number depends on an assumption (a firing-cycle model, a YAML inheritance merge) I say so at
the table.

---

## 0. The short version

The bot has four perception fields. Three of them are on scales nobody wrote down:

| Field | What it answers | Typical live values | Thresholds configured against it |
|---|---|---|---|
| **Belief store** | "Where do I think the enemy is?" | confidence `15…100` | (none — it is a set, not a scalar) |
| **Ground danger** | "How badly can I be hurt standing here?" | **`0` … tens of millions** | `0`, `1`, `15`, `20`, `40`, `60`, `120` |
| **Air danger** | "Can a heli be shot down here?" | `0` … millions | `0`, `30` |
| **Control score** | "Whose ground is this?" | `−1000 … +1000` | `150`, `300`, `800` |
| **Frontier distance** | "How far behind the front am I?" | `0 … 64` coarse cells | `4` |

The control field and the frontier distance are on **bounded, designed** scales: someone chose `MaxScore =
1000` and `GrayBand = 150` together, and a consumer comparing against `300` is making a statement you can
reason about. **The danger fields are not.** Their scale is an emergent product of WW3MOD weapon damage
(`1` … `200000`), a throughput formula that reads the wrong cadence field, and a durability weight tuned for
units with 200 HP being fed units with 28,000 HP. The result is a field whose per-cell values span **seven
orders of magnitude**, against which every consumer threshold in the codebase sits between `0` and `120`.

That is not a tuning error in one knob. It means that for most consumers, the threshold selects **a contour in
space** — roughly "the outer edge of a believed weapon envelope" — rather than a level of concern, and that
two thresholds differing by 3× (say `20` and `60`) select very nearly the same set of cells. §5 is the table
that shows this; §6 says which consumers survive it.

---

## 1. The four fields at a glance

All four are **world traits**, ticked for every game, holding **one array per participating player**. They are
built in a strict order — belief feeds danger, danger and belief feed control, control feeds danger's baseline
back the next cycle — and each layer is **inert data** until a bot module reads it.

| Stage | Field | File | Granularity | Range | Refresh |
|---|---|---|---|---|---|
| **A** | Belief store | `Traits/World/BeliefStore.cs` | per contact (map cell) | confidence 15–100 | 25 ticks |
| **B** | Ground danger | `Traits/World/DangerFieldLayer.cs` | **map cell** | 0 … ~10⁸ | 25 ticks |
| **B** | Air danger | `Traits/World/DangerFieldLayer.cs` | **map cell** | 0 … ~10⁷ | 25 ticks |
| **C** | Control score | `Traits/World/ControlField.cs` | **coarse cell (2×2 map cells)** | −1000 … +1000 | 25 ticks |
| **C** | Frontier distance | `Traits/World/ControlField.cs` | coarse cell | 0 … 64 | 25 ticks (same pass) |

All five are **[WW3MOD]**. None of them exists in OpenRA. The thing OpenRA gives you instead is
`InfluenceMap` / `ThreatMapManager`, which are **omniscient** — they scan `world.Actors` with no fog check —
and are still used by several modules; see §6 for which.

**Granularity is the first trap.** Danger is stamped at **map-cell** resolution; control and frontier distance
live on a **coarse grid of `CellSize = 2` map cells** (`ControlField.cs:389`). Consumers convert with
`MapCellToGridCell` / `GridCellToMapCell` (`ControlField.cs:862`, `:865-866`), and `GridCellToMapCell` returns
the grid cell's **centre** map cell — `(gx * 2 + 1, gy * 2 + 1)`. That asymmetry is the direct cause of the
aliasing bug in §7.2.

---

## 2. Stage A — the belief store: what the bot is allowed to know

**[WW3MOD]** `Traits/World/BeliefStore.cs`. A per-player table of **believed enemy contacts** — the
commander's memory of where the enemy was seen. Not ground truth.

### 2.1 Fog discipline — the "no cheating" construction

Two sources, both per-player legal, and nothing else (`RecomputePlayer`, `:199-201`):

| Source | Method | Legality |
|---|---|---|
| Live sightings | `InjectLive` (`:205-225`) | `actor.CanBeViewedByPlayer(player)` (`:219`) — the same test the renderer uses. |
| Remembered structures | `InjectFrozenStatics` (`:230-250`) | the player's own `FrozenActorLayer.FrozenActorsInRegion(…, onlyVisible: true)` (`:232`) — the engine's fog-frozen last-seen snapshot. |

A `HealthInfo` filter (`:216`, `:240`) skips crates, shroud props and other non-destructibles.

**A human and a bot with identical vision get identical beliefs.** That is a construction property, not a
policy: there is no code path in this file that reads an enemy actor the player cannot see.

### 2.2 The confidence lifecycle — static vs mobile is the load-bearing split

`IsStatic` (`:273-278`) = *no `MobileInfo` and no `AircraftInfo`*. Structures and immobile defences are static;
everything that can move under its own power is mobile.

| Event | Static contact | Mobile contact |
|---|---|---|
| Seen right now | confidence **100** (`FreshConfidence`, `:126`) | confidence **100** |
| Only a frozen ghost visible | refreshed to **60** (`FrozenConfidence`, `:132`) | **not refreshed** — ghosts are deliberately ignored (`:229`) |
| Cell under fog, unobserved | **no decay** — persists until verified gone | × **75 %** per recompute (`MobileDecayPercent`, `:136`) |
| Cell currently visible and empty | **removed immediately** | **removed immediately** (`ResolveUnobserved`, `:263`) |
| Confidence falls below **15** | n/a | **dropped** (`MinConfidence`, `:139`) |

The mobile decay ladder, at one step per 25-tick recompute:

`100 → 75 → 56 → 42 → 31 → 23 → 17 → 12` ⇒ **dropped on the 7th unobserved recompute** (≈ 175 ticks,
≈ 10 s at the mod's 60 ms timestep).

**Why "verified-clear" removes instead of decaying** (`:263`): `player.MapLayers.IsVisible(c.Cell, 1)` — the
`1` is the "currently unfogged" threshold. A commander does not go on claiming a cell he can see is empty. This
is the only mechanism that makes danger drop to exactly `0` quickly, and it is why the recon log shows
`evac-exit … danger=0` 300 ticks after `evac-enter … danger=66834` at the same cell
(`WORKSPACE/recon/260809-truck-loop-from-live-log.md`, §6 item 1).

**OPINION:** the static/mobile split is genuinely good design and is the part of the perception layer I would
change least. The thing worth noticing is that **statics never decay and sit at confidence 60 forever**, so a
single believed enemy bunker glimpsed once at minute 3 keeps stamping danger for the rest of the match at 60 %
intensity, with no mechanism to retire it except walking a unit onto its cell. On a 19-minute match that is a
long-lived hallucination, and it is the most likely source of the "ambient" danger the recon measured around
the *west player's own beachhead* — a cell four tiles from home is not near a live enemy, but it may well be
inside the remembered envelope of something seen once.

---

## 3. Stage B — the danger fields, and where the numbers come from

**[WW3MOD]** `Traits/World/DangerFieldLayer.cs`. Two channels per player, both stamped from the belief store:
**ground** (`DangerChannel.Ground`) and **air** (`DangerChannel.Air`, `:51`).

### 3.1 How a number gets into a cell

Four steps, all integer, all pure:

**(1) Per-actor-type facts, extracted once at map load** — `ExtractKernelFacts` (`:482-518`). For every actor
type in the ruleset it walks the `ArmamentInfo`s, and per domain records the **max weapon range** and the
**summed throughput**. Plus `HealthInfo.HP` and `ValuedInfo.Cost` (`:513-515`).

**(2) Throughput** — `WeaponThroughput` (`:521-533`):

```
burstDamage = Σ (warhead.Damage) over every DamageWarhead with Damage > 0
throughput  = burstDamage × Burst × ThroughputWindow / ReloadDelay
              (ThroughputWindow = 100 ticks, :203;  ReloadDelay ≤ 0 is substituted with 1, :532)
```

**(3) Kernel** — `DangerKernelMath.Compute` (`:153-175`):

```
radius          = min(range / 1024 + RangeBufferCells, MaxRadiusCells)     # cells; buffer 2 (:196), cap 32 (:199)
durabilityWeight = DurabilityBase + HP / HealthDivisor + Cost / CostDivisor  # 100 (:206), 10 (:209), 50 (:212)
intensity        = throughput × durabilityWeight / DurabilityBase × confidence / 100
```

**(4) Stamp** — `Stamp` (`:351-380`), a radial kernel with linear falloff, **additive** across contacts:

```
contribution(d) = intensity × (radius − d + 1) / (radius + 1)     # integer division; d = ISqrt(dx²+dy²)
```

So the value at the contact's own cell is `intensity`, and the value at the outermost ring is
`intensity / (radius + 1)` — the **per-cell step**. Remember that step: it is the quantum of this field, and §7
is about what happens when a threshold is smaller than it.

### 3.2 THE SCALES — worked from real ruleset data

Four real weapons on their real carriers, at confidence 100. Every input cited; the arithmetic is mine.

| | rifleman `AR` | IFV `bmp2` | AT specialist `AT` | MBT `abrams` |
|---|---|---|---|---|
| Weapon | `5.56mm.AR` | `30mm.BMP2` | `ATGM` | `TankRound.Abrams` |
| Weapon def | `weapons-ballistics.yaml:111-117` + `^5.56mm:81-100` | `weapons-ballistics.yaml:410-431` | `weapons-missiles.yaml:2-33` | `weapons-ballistics.yaml:600-603` + `^TankRound:574-598` |
| Carrier | `infantry.yaml:1347` (`^AR:1284`) | `vehicles-russia.yaml:118` | `infantry.yaml:1722` (`^AT:1654`) | `vehicles-america.yaml:442` |
| Cost / HP lines | `:1297` / `:33` | `:159` / `:161` | `:1668` / `:33` | `:481` / `:483` |
| Armament line | `:1302` | `:188` | `:1675` | `:509` |
| Σ warhead damage | 200 | 600 | 12,000 | 23,000 |
| `Burst` | 10 | 6 | 1 (default) | 1 (default) |
| `ReloadDelay` | **150** | **250** | *absent → 1* | *absent → 1* |
| `BurstWait` (the real cadence) | 8 | 15 | **200** | **130** |
| **throughput** | **1,333** | **1,440** | **1,200,000** | **2,300,000** |
| HP / Cost | 200 / 100 | 14,000 / 1,300 | 200 / 300 | 28,000 / 2,500 |
| **durability weight** | **122** | **1,526** | **126** | **2,950** |
| Range | 14c0 → **radius 16** | 19c0 → **radius 21** | 20c0 → **radius 22** | 25c0 → **radius 27** |
| **intensity** (= value at the contact's own cell) | **1,626** | **21,974** | **1,512,000** | **67,850,000** |
| **per-cell step** (= value at the outermost ring) | **95** | **998** | **65,739** | **2,423,214** |

Base HP: infantry `200` (`infantry.yaml:33`), vehicles `10,000` (`vehicles.yaml:20`) before per-actor override.
Structures run to `75,000` (`structures.yaml:252`).

Read that table twice. **The outermost, faintest ring of a single believed Abrams — 27 cells away, the point at
which the field is telling you "there is a tank somewhere over the horizon" — reads 2,423,214.** Every
threshold in the entire consumer stack is between `0` and `120`.

**Three separate RA-era assumptions are stacked here, and each is independently wrong for WW3MOD.**

**(a) The throughput formula reads the wrong cadence field. [MODIFIED — and this is the biggest single
error.]** `WeaponThroughput` divides by `ReloadDelay` (`:533`) and never reads `BurstWait`. But WW3MOD changed
the firing model: `BurstWait` is **mandatory** — `Armament.cs:128-129` throws a `YamlException` if a weapon
omits it — and is the delay between bursts (`Armament.UpdateBurst`, `:626-647`). `ReloadDelay` is now only the
*extra* pause after a whole `Magazine` is spent, and is applied **only if non-zero** (`Armament.UpdateMagazine`,
`:610`). Across `mods/ww3mod/rules/weapons/` there are **14 `ReloadDelay` declarations against 90 `BurstWait`
declarations**, so most weapons take the `ReloadDelay ≤ 0 → 1` substitution and are read as *"fires its entire
burst damage every single tick"*.

Comparing the formula's answer to the weapon's actual sustained output over a full magazine cycle:

| Weapon | Field says (damage / 100 ticks) | Actually delivers (damage / 100 ticks) | Error |
|---|---|---|---|
| `5.56mm.AR` | 1,333 | ≈ 6,060 | **4.5× under** |
| `30mm.BMP2` | 1,440 | ≈ 11,250 | **7.8× under** |
| `ATGM` | 1,200,000 | ≈ 6,000 | **200× over** |
| `TankRound.Abrams` | 2,300,000 | ≈ 17,692 | **130× over** |

(Sustained figures assume the cycle `Magazine` shots = `Magazine/Burst` bursts × (`Burst`×`BurstDelays` +
`BurstWait`) + `ReloadDelay`. That model is my reading of `Armament.UpdateBurst`/`UpdateMagazine`; the *fact*
that `WeaponThroughput` never reads `BurstWait` needs no model and is at `DangerFieldLayer.cs:521-533`.)

**OPINION:** this is the finding that most deserves the user's attention, because it is not a tuning problem —
it is a ranking inversion. The field believes an **AT specialist is ~900× more dangerous than a light machine
gunner**, when in sustained output they are within a factor of one. It believes an **Abrams is 3,100× more
dangerous than a BMP2**, when the true ratio is under 2×. Every consumer that buckets, sorts, compares or
thresholds on this field is therefore making decisions dominated by *which cadence field a weapon's YAML
happens to declare*. The strategic layer is not reading a threat map; it is reading a map of YAML style.
Logged as `WORKSPACE/bugs/discovered.md` 2026-08-09 `[high]`.

**(b) The durability weight was scaled for RA hit points.** The `[Desc]` at `:167-168` says the weight is
"~1.0x (`DurabilityBase`) for a fragile, cheap unit, rising with health and cost". With `HealthDivisor = 10`
and `DurabilityBase = 100`, an RA unit at 200 HP gets `100 + 20 = 120` — a 1.2× multiplier, exactly as
described. A WW3MOD Abrams at 28,000 HP gets `100 + 2,800 + 50 = 2,950` — a **29.5×** multiplier; a 75,000 HP
structure gets **751×**. The knob no longer does what its own description says it does, and it means the field
is dominated by HP rather than by lethality: an unarmed-looking heavily-armoured thing outweighs a lethal
fragile thing by more than its weapon ever could.

**(c) The intensity floor.** `if (intensity < 1) intensity = 1` (`:171-172`). Harmless in itself, but it means
a genuinely negligible contact still paints exactly one cell at value 1 — which matters because
`GarrisonBotModule` gates at `MinBelievedDanger: 1` (§6).

### 3.3 The territory baseline — the *other* thing in the ground channel

**[WW3MOD]** `ProjectTerritoryBaseline` (`:391-421`) + `StampBaseline` (`:434-462`). Independently of any
contact, wherever the player believes the enemy **holds ground**, a low-intensity danger is projected outward:
"a spotter or drone could arrive here."

- Fires only for coarse cells classified `ControlOwner.Enemy` (`:412`) **and** on the **frontier** of that
  region (`IsBelievedEnemyFrontier`, `:425-431`) — so a large enemy region's interior is not restamped.
- Envelope radius is **data-driven**: the longest ground-weapon range among *current believed contacts*
  (`BelievedEnemyGroundEnvelopeCells`, `:467-478`), capped at `BaselineMaxProjectionCells = 24` map cells
  (`:227`). The hard-coded fallback `BaselineFallbackEnvelopeCells` **defaults to 0 = off** (`:223`), so with
  no contacts there is no baseline at all.
- Intensity is `BaselineIntensity = 5` (`:217`), with the same linear falloff.
- **Ground channel only.** `BaselineChannels(contribution) => (contribution, 0)` (`:182`). See §3.4.

**The baseline is additive across every frontier cell, so it stacks.** `influence-stack.md:46` states it "can
exceed 40 easily — do not assume it is a small constant", and I have no reason to doubt that: a frontier 20
coarse cells long, each stamping a disc of radius ~12, overlaps heavily.

**But it is stamped on a lattice.** `StampBaseline` writes to `controlField.GridCellToMapCell(...)`
(`:444`) — the grid cell's **centre**. At `CellSize = 2`, that is exactly the map cells with **both
coordinates odd**. **Three of every four map cells carry zero baseline.** This is §7.2 and it is the reason
`SupplyFollowerBotModule` has a de-aliasing helper.

### 3.4 The air channel — the one place a threshold means something

The air channel is discriminated by `WeaponThreatensAir` (`:135-138`): a weapon counts if its `ValidTargets`
contains `Helicopter` **or** `Air`. Deliberately broader than `UnitRoleResolver`'s SAM detection — *danger is
"what can shoot me down", not "what is an air-defence asset"*.

The ground discriminator `WeaponThreatensGround` (`:142-149`) is "any valid target that is not air-domain",
where air-domain = `{Air, Helicopter, ICBM}` (`:130`). The `ICBM` exclusion is load-bearing: pure AA weapons
carry `Air, ICBM`, and without excluding `ICBM` every SAM would stamp a spurious anti-*ground* aura at full AA
range.

**Because the territory baseline never touches the air channel, `AirDanger == 0` is a literal statement:
"no believed weapon can shoot a helicopter here."** That makes `AirDangerSafeThreshold = 0` and
`ScoutAirDangerSafeThreshold = 0` the only two absolute thresholds in the entire stack that mean exactly what
they say. They are not tuned numbers; they are a boolean wearing an integer's clothes. Preserve that property.

---

## 4. Stage C — the control field and frontier distance

**[WW3MOD]** `Traits/World/ControlField.cs`. Coarse grid, `CellSize = 2` map cells (`:389`). Every cell carries
a signed **ownership score**: `> 0` ours, `< 0` enemy.

### 4.1 The control score scale — this one is designed

| Constant | Value | Line | Meaning |
|---|---|---|---|
| `MaxScore` | **1000** | `:399` | hard clamp, both signs |
| `SeedStrength` | **500** | `:396` | tick-0 Voronoi seed from home beachheads |
| `PresenceGain` | **250** | `:402` | per-recompute push toward whoever is present |
| `AnchorStrength` | **800** | `:420` | floor re-asserted at a site anchor's centre |
| `AnchorRadiusCells` | **4** | `:423` | grid cells the anchor floor tapers over |
| `GrayBand` | **150** | `:417` | `|score| ≤ this` reads **Contested** |
| `ContestErodePercent` | 40 % | `:406` | both sides present → bleed toward gray |
| `VerifiedClearErodePercent` | 100 % | `:410` | observed empty → gray **immediately** |
| `PersistDecayPercent` | 8 % | `:414` | no evidence → linger, fade slowly |
| `StalenessWindow` | 500 ticks | `:427` | after which an observation stops counting as "verified" |

`Classify` (`ControlFieldMath.cs` — in-file at `:146-154`): `> +150` → `Own`, `< −150` → `Enemy`, else
`Contested`.

**OPINION:** this scale is coherent and I can justify every consumer threshold on it (§6). The reason is
simply that the numbers were chosen *together*: `GrayBand 150` against `MaxScore 1000` and `PresenceGain 250`
is a considered statement that one recompute of presence is enough to leave the gray band. Nothing about it
depends on the ruleset, so nothing about it broke when the ruleset was rescaled. **This is the control case
that proves the danger field's problem is not "thresholds are hard" — it is that the danger field has no
designed scale to threshold against.**

### 4.2 Site anchors, and why every consumer reads a ring

`IsSiteAnchor` (`:841-849`) = a non-mobile structure with `SupplyProviderInfo` **or** `CaptureManagerInfo` —
Supply Routes and capturable income. `ApplyAnchor` (`:137-143`) *floors* the score: self anchors to
`max(score, +800)`, believed-enemy anchors to `min(score, −800)`, tapering over 4 grid cells.

At grid distance 4 the taper still reads ≈ **−160**, past the `−150` gray band. **So the entire radius-4 disc
around any enemy site classifies `Enemy`, no matter who is actually standing there.** Every consumer that
wants to know "do we hold the ground around this objective?" therefore samples the **ring at radius
`AnchorRadiusCells + 1 = 5`**, never the target's own cell — e.g. `CaptureCoordinatorBotModule.cs:1553-1557`,
and the Stage-F balance-of-power factor. A consumer that read the target cell would find every objective in
the game "deeply enemy" and its boost would never fire.

### 4.3 Frontier distance — a genuinely clean scalar

`ComputeFrontierDistance` (`:166-198`): multi-source BFS from every `Enemy`-classified cell, 4-connectivity,
one coarse cell per step, capped at `MaxFrontierDistanceCells = 64` (`:434`).

| Reading | Meaning |
|---|---|
| `0` | this coarse cell is believed enemy territory |
| `1…63` | coarse cells behind the believed front line |
| `64` | the **FAR sentinel** — also returned for off-grid, no field, and *no believed enemy anywhere* (`:893-897`) |

The sentinel choice is deliberate and correct: an unpopulated field reads "far behind the front", so every
standoff consumer applies **zero** rearward push until there is real belief data, and the un-consumed path
stays byte-identical.

**OPINION:** frontier distance is the best-behaved perception signal in the stack — bounded, integer,
uniformly-spaced, with a well-chosen degenerate case. It is also the only one whose consumers use it as a
*gradient* (steepest descent, in `ForwardStagingMath` and the supply drop anchor) rather than as a threshold,
which is exactly the right way to use a believed field (§7.3).

---

## 5. The threshold-versus-scale table

Every configured threshold that is compared against a perception field, next to the range of the field it is
compared against. **This is the table to scan for mismatches.**

Danger-field context, from §3.2: a single believed contact contributes an **intensity** of `1,626` (rifleman)
to `67,850,000` (Abrams) at its own cell, and a **per-cell step** of `95` to `2,423,214`. The territory
baseline contributes `0` on three of every four map cells and can stack past `40` on the fourth.

| # | Consumer | Knob | Configured | Engine default | Field | Field's realistic range | Verdict |
|---|---|---|---|---|---|---|---|
| 1 | `SupplyFollowerBotModule` evac | `EvacDangerThreshold` | **60** (`ai.yaml:830`) | 60 (`:91`) | GroundDanger | 0 … 10⁸ | ❌ **inside the noise** |
| 2 | `SupplyFollowerBotModule` reroute | `GroundDangerSafeThreshold` | *unset* | **15** (`:49`) | GroundDanger | 0 … 10⁸ | ❌ unjustifiable |
| 3 | `SupplyFollowerBotModule` drop anchor | `DropDangerSafeThreshold` | **40** (`ai.yaml:893`) | 40 (`:181`) | GroundDanger | 0 … 10⁸ | ⚠️ see §6.3 |
| 4 | `PoiOffensiveBotModule` reroute | `GroundDangerSafeThreshold` | *unset* | **40** (`:194`) | GroundDanger | 0 … 10⁸ | ⚠️ early-out only |
| 5 | `PoiOffensiveBotModule` axis rescale | `BelievedDangerMildThreshold` | **40** (`ai.yaml:446`) | 40 (`:228`) | GroundDanger | 0 … 10⁸ | ❌ unjustifiable |
| 6 | `PoiOffensiveBotModule` axis rescale | `BelievedDangerHostileThreshold` | **120** (`ai.yaml:447`) | 120 (`:232`) | GroundDanger | 0 … 10⁸ | ❌ unjustifiable |
| 7 | `PoiOffensiveBotModule` staging descent | `StagingDangerSafeThreshold` | **40** (`ai.yaml:565`) | 40 (`:505`) | GroundDanger | 0 … 10⁸ | ⚠️ see §6.3 |
| 8 | `PoiOffensiveBotModule` opportunistic advance | `AdvanceDangerCeiling` | **20** (`ai.yaml:369`) | 20 (`:561`) | GroundDanger | 0 … 10⁸ | ❌ unjustifiable |
| 9 | `PoiOffensiveBotModule` mission abort | `MissionDangerSpikePct` | **50 %** (`ai.yaml:291`) | 50 (`:782`) | GroundDanger *ratio* | scale-free | ✅ **justified** |
| 10 | `PoiOffensiveBotModule` mission abort | `MissionDangerSpikeFloor` | **40** (`ai.yaml:292`) | 40 (`:788`) | GroundDanger | 0 … 10⁸ | ❌ unjustifiable |
| 11 | `CaptureCoordinatorBotModule` order | `BelievedDangerMildThreshold` | **40** (`ai.yaml:185`) | 40 (`:189`) | GroundDanger | 0 … 10⁸ | ❌ unjustifiable |
| 12 | `CaptureCoordinatorBotModule` order | `BelievedDangerHostileThreshold` | **120** (`ai.yaml:186`) | 120 (`:193`) | GroundDanger | 0 … 10⁸ | ❌ unjustifiable |
| 13 | `CaptureCoordinatorBotModule` contest bump | `ContestedDangerThreshold` | **40** (`ai.yaml:203`) | 40 (`:235`) | GroundDanger | 0 … 10⁸ | ❌ unjustifiable |
| 14 | `CaptureCoordinatorBotModule` escort tier | `SafeDangerThreshold` | **40** (`ai.yaml:214`) | 40 (`:256`) | GroundDanger | 0 … 10⁸ | ❌ unjustifiable |
| 15 | `CaptureCoordinatorBotModule` escort tier | `SafeControlScoreThreshold` | **300** (`ai.yaml:213`) | 300 (`:251`) | ControlScore | −1000 … +1000 | ✅ **justified** |
| 16 | `CaptureCoordinatorBotModule` reserve muster | `ReserveDangerSafeThreshold` | *unset* | **0** (`:328`) | GroundDanger | 0 … 10⁸ | ✅ **justified** (boolean) |
| 17 | `PoiGarrisonBotModule` order + size | `BelievedDangerMildThreshold` | **40** (`ai.yaml:694`) | 40 (`:111`) | GroundDanger | 0 … 10⁸ | ❌ unjustifiable |
| 18 | `PoiGarrisonBotModule` order + size | `BelievedDangerHostileThreshold` | **120** (`ai.yaml:695`) | 120 (`:116`) | GroundDanger | 0 … 10⁸ | ❌ unjustifiable |
| 19 | `GarrisonBotModule` take-cover gate | `MinBelievedDanger` | **1** (`ai.yaml:775`) | 1 (`:66`) | GroundDanger | 0 … 10⁸ | ✅ **justified** (boolean) |
| 20 | `HelicopterSquadBotModule` leash / detour | `AirDangerSafeThreshold` | *unset* | **0** (`:205`) | AirDanger | 0 … 10⁷ | ✅ **justified** (boolean) |
| 21 | `HelicopterSquadBotModule` withdraw-on-spike | `AirDangerSpikeThreshold` | *unset* | **30** (`:209`) | AirDanger | 0 … 10⁷ | ❌ unjustifiable |
| 22 | `HelicopterSquadBotModule` scout safety | `ScoutAirDangerSafeThreshold` | **0** (`ai.yaml:1498`) | 0 (`:172`) | AirDanger | 0 … 10⁷ | ✅ **justified** (boolean) |
| 23 | `MountedTransportBotModule` drop standoff | `StandoffDangerThreshold` | **0** (`ai.yaml:1136`, `:1171`) | 0 (`:107`) | GroundDanger | 0 … 10⁸ | ✅ **justified** (boolean) |
| 24 | `PoiOffensiveBotModule` / `HelicopterSquadBotModule` standoff | `MinFrontierDistanceCells` | **4** (`ai.yaml:495`, `:1424`, `:1462`, `:1692`) | 0 | FrontierDistance | 0 … 64 | ✅ **justified** |
| 25 | `LayeredDefenceBotModule` man-the-line | `ManTheLineMinThreat` | *unset* | **1** (`:158`) | believed enemy strength | build-value scale | ⚠️ different field |
| 26 | `ControlField` classification | `GrayBand` | *unset* | **150** (`:417`) | ControlScore | −1000 … +1000 | ✅ **justified** |

Legend: ✅ the number is defensible from the field's own scale. ⚠️ the number is not defensible as a level, but
the consumer's structure makes it harmless (see §6.3). ❌ I cannot justify it; it is an RA-era constant on a
WW3MOD field, and it selects a contour, not a level.

**Count: 8 justified, 4 structurally harmless, 14 that I cannot justify.**

Note that every ✅ on the danger field is a **`0`** or a **`1`** — i.e. a threshold used as a *boolean*
("is there any believed threat at all?"), which is scale-independent and therefore survives the rescaling.
Every ❌ is a mid-range number someone chose to mean "a moderate amount of danger". **The field cannot express
"a moderate amount".**

---

## 6. The consumer map, with assessment

### 6.1 Who reads what

| Consumer | Reads | Where | Purpose |
|---|---|---|---|
| `DangerFieldLayer` | BeliefStore.Contacts | `DangerFieldLayer.cs:320`, `:470` | stamp kernels + size the baseline envelope |
| `ControlField` | BeliefStore.Contacts | `ControlField.cs:702`, `:751` | enemy presence erosion + enemy site anchors |
| `DangerFieldLayer` baseline | ControlField.OwnerAt | `DangerFieldLayer.cs:412` | project from believed-enemy frontier |
| `GroundDangerNav` (Stage E) | GroundDanger sampler | `PoiOffensiveBotModule.cs:3628`, `SupplyFollowerBotModule.cs:1420` | two-leg detour around local peaks |
| `HeliDangerNav` (Stage D) | AirDanger sampler | `HelicopterStates.cs:295` | AA-safe leash, lateral detour, spike withdraw |
| `PoiOffensiveBotModule` | GroundDanger + ControlField.ScoreAt | `:1545`, `:1556`, `:1833`, `:2904` | attack-axis rescale, mission-abort trigger |
| `PoiOffensiveBotModule` | FrontierDistanceAt | `:1961`, `:2066`, `:2155`, `:3215` | staging descent, muster anchor, echelon standoff |
| `CaptureCoordinatorBotModule` | GroundDanger + ScoreAt + FrontierDistanceAt | `:988`, `:1191-1193`, `:1467`, `:1556-1559` | capture ordering, contest bump, escort tier, reserve muster |
| `PoiGarrisonBotModule` | GroundDanger | `:387` | defend-target ordering **and** garrison sizing |
| `GarrisonBotModule` | GroundDanger | `:155` | "is there a reason to take cover here at all" |
| `AdaptiveProductionBotModule` | BeliefStore.Contacts | `:377`, `:469` | classify attacker type → buy the matched counter |
| `UnitBuilderBotModule` | BeliefStore.Contacts | `:946` | composition response |
| `HelicopterSquadBotModule` | AirDanger + GroundDanger + ScoreAt + Contacts | `:867`, `:1131-1135`, `:1450`, `:1725` | scout safety, drop-site ranking, first-contact gate |
| `MountedTransportBotModule` | GroundDanger | `:789` | back the drop cell out of the believed envelope |
| `SupplyFollowerBotModule` | GroundDanger + FrontierDistanceAt | `:986-987`, `:1153-1159`, `:1420`, `:1491-1502` | evac decision, reroute, drop anchor descent |
| `TerritoryOverlay` / `DangerOverlay` | ScoreAt + Danger | `TerritoryOverlay.cs:237-263`, `DangerOverlay.cs:175-209` | **render only** — never a sim consumer |

### 6.2 The consumers I cannot justify — one by one

These are ordered by how much I think they matter.

**❌ 1. `EvacDangerThreshold: 60` — the supply truck evacuation.** `SupplyLogisticsMath.ShouldEvacuate`
(`:151-153`): `dangerAtTruck >= 60 || dangerAtCluster >= 60`. Against a field whose median reading at the
moment of entry was **66,834** across 36 logged evacuations
(`WORKSPACE/recon/260809-truck-loop-from-live-log.md` §1). Three separate trucks entered evacuation at exactly
`danger=68` while within four cells of their own beachhead. **The threshold sits inside the ambient noise
floor.** This is diagnosed at length in that recon and is being retuned on `auto/danger-scale`; it is in this
document as the worked example of the class, not as a new finding.

**❌ 2. `AdvanceDangerCeiling: 20` — the opportunistic advance.** The `[Desc]` (`:556-560`) says it is
"deliberately BELOW the staging threshold at neutral: the muster standoff only has to be survivable, whereas
ground being walked into unescorted has to be genuinely empty." The intent is right and the mechanism does not
deliver it. `20` versus `40` is a difference of one-fifth of one per-cell step for a *rifleman* and one
ten-thousandth of a step for a tank — the two ceilings select the same cells. Worse, the `[Desc]` at `:563-570`
documents the Aggressiveness slider giving ceilings `{8,14,20,26,32}` across the knob's range and calls this
"the one dial whose slope was already coarse enough to move at every grid point". **On this field, a `{8…32}`
sweep is not a slider — it is five values that all mean "any believed contact at all".** The knob is
inert-in-effect while looking tuned.

**❌ 3–4. `BelievedDangerMildThreshold: 40` / `HostileThreshold: 120` — five separate modules.**
`PoiScoring.BelievedThreatFactor` (`PoiMap.cs:630-638`), its mirror `BelievedDefendFactor` (`:647-655`), and
`PoiOffenseMath.BelievedDangerFactor` (`PoiOffensiveBotModule.cs:4214-4222`) all bucket the same way:
`≤ mild` → safe, `≤ hostile` → mild, else hostile. Read by attack-axis rescaling, capture ordering, garrison
ordering **and garrison sizing**. The three-bucket design is sound; the bucket edges are not. On the measured
field, **the `[41,120]` "mild" bucket is essentially empty** — a cell either has no believed contact in range
(reads baseline: 0, or ~5–50 if it is a lattice cell near a dense frontier) or has one (reads ≥ 95, and usually
≥ 65,000). `influence-stack.md:98` defends these numbers as "deliberately set *above* the territory-baseline
intensity so ambient 'deep enemy ground' danger doesn't damp every axis". That reasoning is correct as far as
it goes and it is the reason the numbers are 40/120 rather than 5/20 — but it only establishes a *lower* bound.
Nothing establishes the upper one, and nothing checked that the resulting middle bucket is reachable.
**These are effectively two-bucket classifiers wearing three-bucket clothes.**

**❌ 5. `MissionDangerSpikeFloor: 40`.** Its sibling `MissionDangerSpikePct: 50` is the one danger-field
threshold in the codebase I can fully defend, because it is a **ratio against the same field's own value at
commit time** — `currentDanger > commitDanger × 1.5` is scale-free and survives any rescaling. The floor
exists for the `commitDanger ≈ 0` case, and the `[Desc]` (`:784-787`) sets it "at the mild-danger threshold
(a genuine believed weapon envelope, above baseline stacking)". But since the mild threshold itself is
unjustified (above), the floor inherits the problem: it fires on any believed contact appearing at all, which
makes trigger-2 an "enemy sighted near the objective" trigger rather than a "danger spiked" trigger.
**OPINION:** that may actually be the behaviour you want — but it should be *named* that, because as written a
reader will think the bot tolerates a 40 % rise in danger and it does not.

**❌ 6. `ContestedDangerThreshold: 40` and `SafeDangerThreshold: 40` — capture escort sizing.**
`IsContestedNeighbourhood` (`CaptureCoordinatorBotModule.cs:1466-1468`) is
`GroundDanger(cell) > 40 || controlRing reads Enemy`. The control-ring half of that disjunct is well-founded
(§4.1); the danger half fires on any believed contact within any weapon envelope. **Since the two are OR'ed,
the danger term dominates** — it is true strictly more often. `EscortSizingMath.Resolve` then takes both `40`s
plus the well-founded `SafeControlScoreThreshold: 300` and `GrayBand: 150`. The lever is documented as
"reduction-only" (it can only lower an escort), so the failure mode is *never reducing* rather than
under-escorting — which is safe, and is why this is `[med]` rather than `[high]`.

**❌ 7. `AirDangerSpikeThreshold: 30` — heli withdraw-on-spike.** `HelicopterStates.cs:552`:
`SquadMaxAirDanger(owner) > 30` ⇒ change to the withdraw state. Its `[Desc]` (`:207-209`) says "Above
SafeThreshold so leash grazing does not flap" — i.e. the *only* stated requirement is `> 0`. **This is the
clearest case in the codebase of a number chosen to be "a bit above zero" on a field where the smallest
non-zero value from a real contact is 95 and the typical one is 65,000.** Functionally this is
`SquadMaxAirDanger > 0`, and any value in `[1, 94]` would behave identically. It is not *harmful* — it just
means the anti-flap margin the comment describes does not exist, and the squad withdraws on the first believed
AA contact of any kind. **OPINION:** given the air channel carries no baseline, `> 0` is arguably the right
semantics anyway; the fix is to say so and set it to `0`, not to tune `30`.

**❌ 8. `GroundDangerSafeThreshold: 15` (trucks) / `40` (offense) — the Stage-E reroute early-out.**
`GroundDangerNav.DetourWaypoint` (`:91-96`): `if (direct <= safeThreshold) return null`. The `[Desc]`
(`:47-49`) says the truck value is "Lower than the offensive threshold — a non-combatant should avoid even
moderate exposure". On this field, `15` and `40` select the same set of routes. See §6.3 for why this is
nonetheless harmless.

### 6.3 The consumers that survive anyway, and why

Three modules read this field with unjustifiable thresholds and *still work*. Understanding why is the most
transferable lesson here.

**Gradients beat thresholds.** `GroundDangerNav.DetourWaypoint` (`:91-141`) uses the threshold only as a cheap
early-out; the actual decision is **strict improvement** — a lateral waypoint is emitted only if its two-leg
worst-case is *strictly less* than going direct. `influence-stack.md:86` states this outright: "correctness
against the ambient baseline is the strict-improvement rule, not the threshold." Where danger is roughly
uniform, every candidate ties and the function returns `null` ⇒ go direct. A comparison between two readings of
the same field is scale-free; a comparison between a reading and a constant is not.

The same shape protects the steepest-descent consumers: `ForwardStagingMath.StagingCell` and the supply-drop
anchor descent both **walk down `FrontierDistanceAt`** and use the danger threshold only as a *refusal* on
individual steps. And it protects `MissionDangerSpikePct` (a ratio against the field's own prior value).

**OPINION and the general rule this yields:** **on a believed field with no designed scale, relative
comparisons are meaningful and absolute thresholds mostly are not.** If you are about to add a consumer, prefer
(a) a comparison against another reading of the same field, (b) a ratio against the same cell's earlier
reading, or (c) a strict boolean at `0`. Reach for a mid-range constant only after computing the field's
per-cell step in the regime you care about — §3.2 is that computation.

**Booleans survive rescaling.** `GarrisonBotModule.WorthGarrisoning` (`:155`) is
`GroundDanger >= MinBelievedDanger` with `MinBelievedDanger: 1`. That is "is there *any* believed threat
here?", which is scale-independent by construction. The `ai.yaml` comment at `:777-783` reasons about this
explicitly and correctly, and reaches the right conclusion — that raising the number would move the flap rather
than damp it, so the damping must be **temporal** (`MinGarrisonDwellTicks: 750`). Same for
`ReserveDangerSafeThreshold: 0`, `StandoffDangerThreshold: 0`, `AirDangerSafeThreshold: 0`,
`ScoutAirDangerSafeThreshold: 0`.

**⚠️ The two `40`s I marked "structurally harmless"** (`DropDangerSafeThreshold`, `StagingDangerSafeThreshold`)
are both step-refusals inside a descent walk, not gates on a decision. They behave as `> 0` refusals. Their
`[Desc]`s claim they are calibrated against each other (`SupplyFollowerBotModule.cs:177-181`: "Matches
`PoiOffensiveBotModule`'s `StagingDangerSafeThreshold` on purpose"), which is true and also does not matter —
both are effectively zero.

---

## 7. The quantisation traps

There are **two** independent quantisation problems in the danger field. They have different causes, bite
different consumers, and both have caused real bugs. Do not conflate them.

### 7.1 Trap A — the integer kernel floor: a value band can be sub-quantum

`contribution = intensity × (radius − d + 1) / (radius + 1)` (`DangerFieldLayer.cs:366`) is **integer
division**. The field therefore moves in steps of `intensity / (radius + 1)` — the per-cell step in §3.2.

Two consequences that look opposite but are the same fact:

**(a) For a big kernel, the step is enormous, so a band is invisible.** `EvacReleaseHysteresis: 15`
(`ai.yaml:849`) creates a `[45, 60]` band between "will evacuate" and "will resume following", exactly like a
textbook Schmitt trigger. But 15 units is **less than one-sixth of one cell** of a rifleman's gradient and
**less than one ten-thousandth of a cell** of a tank's. A moving truck crosses the entire band in a single
step and never dwells inside it. The band is a comment, not a mechanism. The `ai.yaml` block at `:840-844`
already says this — and *its* explanation ("the danger field steps by tens-to-hundreds per cell near a
contact", `:840-841`) is right for the rifleman (step 95) and wrong by three orders of magnitude for a tank
(step 2,423,214).

**(b) For a small kernel, the step rounds to zero, so entire rings vanish.** The territory baseline runs at
`BaselineIntensity = 5` over an `envGrid` of up to 12 coarse cells. `5 × (13 − d) / 13` gives
`5,4,4,3,3,2,2,1,1,1,1,0,0` — **the outermost two rings contribute nothing at all**, and rings 7–10 all
contribute exactly `1`. A minimum-intensity contact (`intensity` floored to 1 at `:171-172`) paints exactly
**one** cell.

**And the two combine at the outer edge of any kernel.** Because `intensity` scales linearly with confidence,
one step of mobile decay (×75 %) shifts every contribution by 25 %. Near a kernel's rim, where contributions
are small integers, that flips whole rings between `2 → 1` and `1 → 0` in a single recompute. **The `ai.yaml`
comment at `:777-780` is the clearest statement of this in the tree, and it is correct:** *"Enter and release
are exactly complementary predicates over an INTEGER field that is fully restamped each recompute, so at a
kernel's outermost ring one confidence step moves the whole contour across the threshold. Raising
`MinBelievedDanger` would NOT fix that — every threshold has its own ±1 outermost contour, so a higher bar
moves the flap rather than damping it."*

**This is why a dwell timer works where a value band does not.** A time bound is scale-free: it does not care
what the field's quantum is. A value band has to be wider than the quantum, and on this field the quantum is
either 0 (baseline rim) or ≥ 95 (any real contact) — so there is no band width that is simultaneously bigger
than the noise and smaller than the signal. Every anti-oscillation fix in the tree that *worked*
(`MinGarrisonDwellTicks: 750`, `EvacDwellScans`, `RetreatDamperMath`) is temporal for this reason.

### 7.2 Trap B — lattice aliasing: three of every four cells carry no baseline

Independent of Trap A. The **contact kernels** are stamped per map cell (`:362-372`), but the **territory
baseline** is stamped only at `controlField.GridCellToMapCell(...)` (`:444`) — the grid cell's centre, which at
`CellSize = 2` is `(gx*2 + 1, gy*2 + 1)`, i.e. **only map cells with both coordinates odd**.

So near a contested frontier, where the baseline has stacked past 40, **two adjacent map cells can differ by
more than 40 purely on coordinate parity.** A consumer asking `danger(cell) >= threshold` about **one** cell is
getting a coin flip on that cell's parity.

**Gradient consumers are immune** — `DetourWaypoint` and the steepest-descent walks sample many cells and the
lattice averages out. **Threshold consumers are not**, and they are exactly the ones the strict-improvement
rule does not cover.

The de-aliasing fix exists in one module only: `SupplyFollowerBotModule.GroundDangerAt` (`:1491-1503`) takes
the **max** of the cell's own reading and its grid-centre representative's. Max, not min or mean, and the
reasoning is at `:1471-1490`: min under-reports danger (wrong direction for a safety gate); mean does not
reintroduce parity but dilutes the densely-stamped contact kernel across four cells, roughly quartering the
local peak the gate exists to notice. **Every other threshold consumer in §6 reads a raw single cell.**

**De-aliasing raises readings**, which can quietly promote a fallback branch into the main path — the
`SupplyFollowerBotModule` note at `DISCOVERIES.md:136` records exactly that happening. If you add the helper
elsewhere, re-derive which side of the threshold the typical reading now falls on.

### 7.3 The rule this yields

**OPINION:** if you take one operational rule from this document, take this one.

> Before writing *any* comparison against the danger field, decide which of three shapes you are in:
> **boolean at 0** (safe, scale-free), **relative** (compare two readings of the same field, or a ratio against
> the same cell's earlier reading — safe, scale-free), or **absolute mid-range constant** (unsafe: compute the
> per-cell step in your regime first, and if your band is narrower than one step, use a *time* bound instead).
> And if you are thresholding a single cell, route it through a de-aliasing helper or you are deciding on
> coordinate parity.

---

## 8. The invariants, and why each exists

A reader who does not know *why* these exist will break them. Each is cheap to violate accidentally.

### 8.1 Zero RNG draws in the entire stack

**Why:** `BeliefStore`, `DangerFieldLayer` and `ControlField` are **always-on world traits** — they tick for
*every* game and every profile, including matches with no fog-respecting bot in them. `World.SharedRandom` is
the **synced gameplay RNG**. If any of these traits drew from it (the obvious way to self-stagger three layers
off each other's tick), it would advance the shared stream for `@stable` and control games too — silently
breaking replay determinism and byte-identity against every recorded benchmark baseline, even though the trait
itself is behaviour-inert. This was a real bug: the original implementations each called
`SharedRandom.Next(0, UpdateInterval)` at load, a 2-draw shift (`DISCOVERIES.md:1332`).

**How it is satisfied:** fixed, distinct, deterministic offsets — `BeliefStore` **0** (`:168`),
`DangerFieldLayer` **`UpdateInterval / 3`** (`:292`), `ControlField` **`UpdateInterval / 2 + 1`**. All the
navigation and scoring math is integer walks over fixed candidate orders with iteration-order tie-breaks.

**How you would break it:** adding a random tie-break to a scoring function, or a jittered re-scan interval.

### 8.2 Byte-identity when flags are off

**Why:** the tournament A/Bs run `@experimental` against `@stable`, and `@stable` is the **control**. If a
change silently moves `@stable`, the benchmark's zero moves under it and every prior measurement becomes
uninterpretable.

**How it is satisfied:** every consumer flag defaults **off/inert**, and every seam has a default that
reproduces the frozen path — `suppressOmniscientThreat` defaults `false`, `ApplyStandoff` is an identity
pass-through when the field is null, `FrontierDistanceAt` returns the FAR sentinel with no field (⇒ zero
push), all the Stage-F sub-multipliers default `100`.

**Note the project policy that qualifies this** (`CLAUDE.md`): `@stable` **inherits improvements and is never
gated off on purpose**. What is forbidden is *silent* drift — a new behavioural field on a shared trait must
default to baseline so `@stable` never changes without anyone noticing. Deliberate, visible improvement
flowing to `@stable` is fine and should be said in the commit message.

### 8.3 Lazy per-player creation — and the gate that fails open

**Why (performance):** a field is a full map-sized array per player. Building one for every player in every
game would be pure waste, since only participants read it.

**How:** `RecomputePlayer` creates the `PlayerField` on first use (`DangerFieldLayer.cs:315-316`), and
`GatherParticipants` + `SubInterval` (`InfluenceStack.cs:56-73`) round-robin **one participant per sub-slot**,
so no single tick rebuilds every player's field.

**The trap this creates, and it is subtle:** a consumer must **never** gate itself on *"has the field been
built yet?"*. The first field is only created after the deterministic stagger and then only on that player's
round-robin slot, while `GarrisonBotModule` scans on **tick 1**. A field-existence gate would therefore leave
the module **ungated for the opening window — which is exactly when opening-play bugs live**
(`DISCOVERIES.md:27`). The correct gate is `InfluenceStack.Participates(player)`, which is knowable at tick 0;
the *reading* then honestly returns `0` ("nothing believed here") for a participant whose field is not built.

> **General form, worth carrying beyond this file:** for an availability conjunct, prefer the predicate that is
> true from tick 0 over the one that becomes true when the data arrives. The second silently disables the gate
> over exactly the warm-up window.

### 8.4 The `Participates` predicate — and the correction to `influence-stack.md`

`InfluenceStack.Participates(player)` (`InfluenceStack.cs:42-52`) is the single place that decides who gets a
field:

| Player | Participates? | Line |
|---|---|---|
| non-combatant / spectating | no | `:44` |
| bot with `BotType == "experimental"` | **yes** | `:48` |
| bot with `BotType == "stable"` | **yes** (since the 2026-08-02 parity promotion) | `:48` |
| any other bot profile (normal / rush / turtle / legacy) | no | `:48` |
| human combatant (`player.Playable`) | **yes** | `:51` |

Humans participate because the `/danger` and territory overlays are human-facing reads. The choice of
`player.Playable` rather than `world.RenderPlayer` is deliberate and load-bearing: reading the render player to
decide what to *simulate* would make simulation depend on the render path and **desync**
(`InfluenceStack.cs:9-14`). The render-only overlays are free to read `RenderPlayer` because they are not sim
code (`DangerOverlay.cs:124`, with the reason stated at `:26`).

**Now the correction.** [`influence-stack.md:105`](../reference/influence-stack.md) carries the headline
**"do NOT gate on `Participates`"**. That headline is **an over-broad compression of a correctly-scoped rule**,
and the ruling is recorded at `WORKSPACE/DISCOVERIES.md:28`.

- **What the rule correctly says:** since `Participates` now returns true for `@stable` and every human,
  a `Info.Flag && Participates(player)` double-gate on a **shared `enable-ai-any` module instance** does *not*
  preserve byte-identity against `@stable`. If you specifically need a lever that must not move the benchmark
  control, the conjunct must be explicit: `player.BotType == InfluenceStack.ExperimentalBotType`
  (as `GarrisonBotModule.cs:219` does — note that `influence-stack.md:105` cites `:102` for this, which is
  stale at `910507c1`).
- **Why the headline over-reaches:** `CLAUDE.md` settles that `@stable` **inherits improvements and is never
  gated off on purpose**. So for the ordinary case — a bug-class fix or a genuine improvement — *reaching*
  `@stable` is the **desired outcome**, not the defect, and `Participates` is exactly the right gate. The
  `ai.yaml` comment at `:770-773` gets this right in practice: it uses `RequireBelievedThreat` without a
  bot-type narrowing precisely so `@stable` inherits it, and says so.
- **Practical consequence:** three older `SupplyFollowerBotModule` flags (`SectorSpread`,
  `SmallSquadCoverage`, `DangerEvac` — all written before 2026-08-02) do reach `@stable` today, and several
  in-file comments still claim otherwise. **When a comment asserts byte-identity, re-derive the gate rather
  than trusting the sentence** — a gate whose meaning is defined elsewhere can be widened out from under every
  comment that describes it. Check the flag's commit date against 2026-08-02.

There is also a **third** gating pattern, for a new read bolted onto a shared world layer that `@stable` now
shares: **per-player opt-in**. `ControlField.RequestFrontlineProfile(player)` (`:933-936`) adds a player to a
`HashSet<Player> profileEnabled` (`:500`), and `RecomputePlayer` builds the profile only for members (`:579`) —
so `@stable` and humans do zero profile work. Only the `@experimental` offense module ever opts in.

---

## 9. What I could not verify

Stated plainly, because this document will be used to justify future numbers.

- **I did not run the game.** Every number in §3.2 and §5 is hand-computed from cited ruleset values using
  cited formulas. The recon's measured field values
  (`WORKSPACE/recon/260809-truck-loop-from-live-log.md`) are the only *observed* data anywhere in this
  document, and they corroborate the order of magnitude (median 66,834 at evac-entry against my computed
  ATGM outer-ring value of 65,739) — but I could not attribute those log readings to specific contacts, so
  treat the correspondence as consistency, not proof.
- **The sustained-output column in §3.2** depends on my model of the WW3MOD firing cycle from
  `Armament.UpdateBurst`/`UpdateMagazine`. The *fact* that `WeaponThroughput` never reads `BurstWait` is
  direct (`DangerFieldLayer.cs:521-533`) and needs no model. The 130×/200× error ratios do need the model.
- **YAML inheritance merges** in the §3.2 damage sums (e.g. `30mm.BMP2` overriding `^30mm`'s
  `Warhead@Target.Damage`) were read by eye, not resolved by the engine's rule loader. If a merge behaves
  differently than I assumed, the BMP2 row shifts; the Abrams and ATGM rows do not depend on any override.
- **The "mild bucket is essentially empty" claim in §6.2** is an inference from the field's step size, not a
  measurement. Nothing in the tree currently logs the danger field's distribution. The cheapest thing that
  would settle every open question in this document is a single per-scan `Log.Write` of min/median/max over
  `DangerFieldLayer.ActiveCells` — the recon recommends the same instrument, independently.
- **`influence-stack.md`'s `ControlField.cs` line references are stale** (it cites `CellSize` at `:177` and
  `GrayBand` at `:205`; they are at `:389` and `:417` at `910507c1`). I did not edit that file — three other
  doc workers share this checkout — and logged it instead.

---

## 10. Bugs logged from this pass

Recorded in [`WORKSPACE/bugs/discovered.md`](../../WORKSPACE/bugs/discovered.md), 2026-08-09, not fixed here:

1. **`[high]` `DangerFieldLayer.WeaponThroughput` reads `ReloadDelay`, not `BurstWait`** — the whole ranking
   problem in §3.2(a). 14 `ReloadDelay` declarations against 90 `BurstWait` in the weapon files.
2. **`[med]` `DurabilityBase`/`HealthDivisor` are RA-scaled** — §3.2(b). The `[Desc]` promises ~1.2× for a
   fragile unit and delivers 29.5× for an MBT, 751× for a large structure.
3. **`[low, doc-in-code]` `ai.yaml:841-842` states the danger field "steps by tens-to-hundreds per cell near a
   contact"** — true for a rifle, wrong by 10²–10⁴ for the mod's heavy weapons.
4. **`[low, doc-in-doc]` `influence-stack.md` carries stale `ControlField.cs` line references.**

---

## See also

- [`02-lifecycle-and-arbitration.md`](02-lifecycle-and-arbitration.md) — the tick path, module cadences, order
  layers and unit ownership. Where these fields get *read from*.
- [`influence-stack.md`](../reference/influence-stack.md) — the implementation reference for the same stack,
  organised by build stage rather than by reader. Deeper on Stages D/E/F and the Phases 0–7 frontline layer;
  see §8.4 above for the one place this document corrects it.
- [`game-model.md`](../reference/game-model.md) — why there are no factories, and why every unit in the game is
  born at the same few cells.
- [`WORKSPACE/recon/260809-truck-loop-from-live-log.md`](../../WORKSPACE/recon/260809-truck-loop-from-live-log.md)
  — the live-log diagnosis this document generalises from.
- `WORKSPACE/DISCOVERIES.md` entries of 2026-08-07 (the contour entry at `:119`, the aliasing entry at `:128`)
  — the two independent reasons these thresholds are untrustworthy, recorded before this document existed.
