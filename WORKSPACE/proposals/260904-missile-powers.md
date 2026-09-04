# Missile strike powers — design proposal for v1

**Written against `main @ 84077cc4`.** The four research passes below were run at `2c8488ef`; the
two commits since (`8bbc1585`, `84077cc4` — the buy-menu redesign) were checked against this
proposal and change nothing in it, for the reason given in §2.6. **Static analysis only; no game was
launched and no autotest was run** — launches serialize through the parent manager by standing
instruction, so every claim here is read from code, and the runs that would settle the rest are
listed in §8.

**Tick rate is 16.667 tps** (`Timestep: 60` ms). Every duration below uses that, not 25.

This is a **proposal, not a plan**. The user admitted missile powers into a locked v1 scope and then
said *"Lets discuss more before we start implementing."* Nothing here is built. It is written to be
ruled on.

**Companion mockup:** `WORKSPACE/mockups/missile-powers-first-cut.html` — the top-left bin and the
buy entries drawn with the real decoded cameo art, with the power selection, prices and the US
option live so they can be moved and sent back.

**Evidence base.** Four research documents, all at this SHA:

| Document | What it settles |
|---|---|
| `recon/powers-interception.md` | Whether a missile can be shot down, and by what |
| `recon/powers-buy-loop.md` | Buy → bank → fire, refunds, and lobby gating |
| `recon/powers-missile-delivery.md` | Getting a missile in from the map edge; warhead numbers |
| `recon/powers-manager-findings.md` | The manager's own pass, including one refuted claim |
| `recon/powers-and-preloaded-transports.md` | The prior costing this work verified and partly corrected |

---

## 1. The recommendation, up front

**Ship three buy entries: the Kinzhal, one US fast strike, and the tactical nuclear strike. Defer
the cruise-missile tier entirely.**

| # | Power | Faction | Interceptable | Cameo |
|---|---|---|---|---|
| 1 | **Kh-47M2 Kinzhal** | Russia | no | **ships** — `precicon`, captioned `PRECISION STR` |
| 2 | **US fast strike** (§4) | America | no | **one new cameo** |
| 3 | **Tactical nuclear strike** | both | no | **ships** — `atomicon`, captioned `NUCLEAR BOMB` |

*Deferred to v1.1: 3M-14 Kalibr and BGM-109 Tomahawk — the interceptable cruise tier.*

This is the mockup's **"Art-cheapest (3)"** preset, and the three arguments for it converge:

**1. The cruise tier's whole selling point does not work in the shipped game.** Its reason to exist
is that you can shoot it down — and **nothing in the mod can currently intercept anything** (§2.3,
independently confirmed by the parent). Shipping it honestly means also repairing the counter-layer,
which is a second feature with its own balance pass. Deferring it removes that dependency completely.

**2. It is the only cut that needs no changes to any existing unit.** An uninterceptable-only first
cut requires **no** `ICBM` additions to `Stinger.quad` or `9M311`, and no `^AutoTargetAAIFV` priority
edit. Blast radius on shipped balance: **zero**. The cruise tier is what drags other actors in.

**3. Art.** Two of the three entries ship with correct captions today; the cruise tier needs two new
cameos and is the most art-expensive part of the feature (§6). One new cameo for the whole first cut,
against three.

It also lands closest to what the user actually asked for — *"just a few powers for now, see how they
land"* — while still answering three of their four named ideas. The one deferred is the one whose
supporting machinery is broken.

**Why the nuke stays in rather than being the thing deferred.** It is the cheapest entry in the
table: weapon, art, audio, beacon and cursor all ship today, and the only missing piece was ever a
way to buy and fire it — which is exactly what this feature builds. The user asked for it by name.
**Ship it lobby-gated, default OFF**, so the unresolved doomsday design in
`WORKSPACE/archive/plans/260324-nukes.md` stays out of a locked v1 while remaining one tickbox away.
If the user would rather not see it at all, deleting the entry is a one-line change.

**Cost of adding the cruise tier back later:** one YAML actor profile per faction, two cameos, and
`ICBM` on two weapons plus one auto-target template. **No engine work** — it shares the delivery
trait built for the first cut. It is a genuinely cheap follow-on *once the counter-layer works*.

---

## 1a. Correcting the framing: both tiers are actors, and that is good news

The parent's reading — *"the uninterceptable Kinzhal can be whatever is cheapest to build, while the
interceptable cruise missile must be an actor"* — is half right, and the half that fails is the one
that would have shaped the build.

**The cruise half holds and is verified.** A projectile cannot be targeted: `IProjectile` is a marker
on `IEffect`, which declares only `Tick` and `Render` (`WeaponInfo.cs:71`, `IEffect.cs:17-21`).
Anything shootable must be an actor, which is exactly `^ShootableMissile`.

**The Kinzhal half does not survive the user's own requirement.** The cheapest untargetable delivery
is `NukePower`, whose missile is a `NukeLaunch` effect — untargetable by construction. But
**`NukePower` descends vertically onto the target and cannot be made to arrive from anywhere else.**
Read directly at `84077cc4`:

```csharp
// engine/OpenRA.Mods.Common/Projectiles/NukeLaunch.cs:73-78
var offset = new WVec(WDist.Zero, WDist.Zero, velocity * (impactDelay - turn));
ascendSource  = launchPos;
ascendTarget  = launchPos + offset;
descendSource = targetPos + offset;   // directly above the target
descendTarget = targetPos;
```

The offset is **Z-only**. `descendSource` is the target's own position raised straight up, so the
descent is always vertical. `SkipAscent` sets `turn = 0` (`:62`), which makes the offset *larger* —
it starts the missile *higher*, still directly overhead. There is no field that reaches it. This is
structural, not a tuning gap.

The user asked for a strike that *"comes in from the map edge at high speed"*. **So the Kinzhal
cannot use the cheap path either, and both tiers end up as actors on one shared delivery trait.**

**That is the good news, not the bad.** The engine's actor/projectile line does not split the
feature into two implementation shapes — the map-edge requirement collapses them onto one. There is
**one** delivery trait to write, and the tiers differ only in YAML. Untargetability becomes a
`TargetTypes` value that nothing lists, not a different kind of object — which is also why the
deferred cruise tier costs no engine work to add back.

---

---

## 2. What the research settled

Five answers, each of which changes the design. Detail lives in the recon documents.

### 2.1 A projectile cannot be shot down — but the mod already ships a missile that can be

`IProjectile` is a marker on `IEffect`, which declares exactly two members, `Tick` and `Render`
(`WeaponInfo.cs:71`, `IEffect.cs:17-21`). Effects live in a plain list, not the keyed actor
dictionary (`World.cs:33` vs `:394-402`); `TargetType` has four values and none is a projectile
(`Target.cs:18`). **There is no seam to widen.**

It does not matter, because **`^ShootableMissile` already exists** (`defaults.yaml:1074-1101`): a
missile-as-*actor* template with `BallisticMissile` flight, `Armor: Light`, `HitShape`, `Detectable`,
and `Targetable@Ground` + `Targetable@Airborne` both carrying **`TargetTypes: ICBM`**.
`IskanderMissile` (`vehicles-russia.yaml:1116`, HP 100) and `HIMARSMissile`
(`vehicles-america.yaml:1212`, HP 50) inherit it and are in the live game.

**So the shootable half of the user's concept is not new work.** It has been shipping for months
under a different feature.

An actor-missile also **does not inherit the A-10 strafe failure** that makes the existing NATO
airstrike fire nothing: `Explodes` calls `weapon.Impact(...)` directly (`Explodes.cs:133`), so
`Armament.CanFire` and its terrain-target gate (`Armament.cs:402`) are never on the path. **The
user's instinct to defer the aircraft airstrikes in favour of missile-only powers deletes the most
expensive problem in the prior costing.**

### 2.2 Interception is a speed problem, not a gun problem — and this refutes a claim I made

I proposed splitting AA vehicles from MANPADs on the *gun*: both AA vehicles carry a rapid-fire
cannon, the MANPAD does not, and real cruise-missile defence is a gun problem. **That is doctrinally
right and mechanically backwards in this engine.**

`Bullet` flies to `args.PassiveTarget` — where the target was at the instant of firing
(`Bullet.cs:201`) — **with no lead solution.** Against a missile at 516–600 WDist/tick the lead error
is 10–12 cells at maximum range, against a 426-WDist hitshape:

| Gun | Muzzle speed | Range | Lead error at max range |
|---|---|---|---|
| `20mm_CRAM` | 1024 | 22c0 | **11.4 cells** |
| `30mm.Tunguska.AA` | 900 | 18c0 | **10.6 cells** |
| `25mm.Bradley` | 900 | 20c0 | **11.7 cells** |

The mod already documented this for aircraft (`weapons-ballistics.yaml:713-716`: *"A Littlebird at
its 265 u/tick cruise is unhittable by this gun at ANY Inaccuracy"*). The `20mm_CRAM` case is the
sharpest: the weapon named for counter-rocket defence is the *worst* of the four, because its muzzle
velocity is the lowest in the mod.

**Only homing `Missile` projectiles lead** (`Missile.cs:1148`, `WVec.CalculateLeadTarget`). So the
dial is interceptor speed — and it is self-enforcing:

| Interceptor | Speed | Can it catch a 516–600 missile? |
|---|---|---|
| `SurfaceToAirMissile` (`SAM`, disabled) | **800** | yes, from any aspect |
| `Stinger.quad` (Stryker-SHORAD) | 600 | head-on or crossing only |
| `9M311` (Tunguska) | 600 | same |
| **`MANPAD`** (infantry) | **450** | **no — physically cannot close** |

**This is why the user's own instinct is the right design.** They asked for cruise missiles that are
*slower* and shootable, and a Kinzhal that is *fast* and is not. Speed does most of the work: leave
the fast tier well above 800 and **nothing in the mod can catch it**, whatever any target-type table
says.

**But be precise about the MANPAD, because speed does not cover it.** A cruise missile slow enough to
be caught by a 600 interceptor (proposed `Speed: 350`, §5) is also slow enough for a 450 MANPAD to
catch. So the two exclusions come from different places, and both are needed:

| | Excluded from the fast tier by | Excluded from the cruise tier by |
|---|---|---|
| MANPAD infantry | speed (450 vs >800) **and** target type | **target type only** |
| AA vehicles | speed (600 vs >800) | *not* excluded — this is the intent |

The MANPAD's target-type exclusion already ships and is doubly enforced: `MANPAD` is
`ValidTargets: Air` with no `ICBM` (`weapons-missiles.yaml:481-484`), and `^AA` auto-targets through
`^AutoTargetAir`, whose priority table never lists `ICBM` (`defaults.yaml:739-745`). **Nothing needs
to be done to keep MANPADs out — only to let AA vehicles in.**

**The corrected fix is also smaller than the one I proposed:** add `ICBM` to `Stinger.quad` and
`9M311` (blast radius exactly `strykershorad` and `tunguska`) plus the `^AutoTargetAAIFV` priority.
**No gun is touched, so `25mm.Bradley` is never modified and no Bradley is affected.**

### 2.3 Nothing in the shipped game can currently intercept anything

The only actors listing `ICBM` are `CRAM`, `AGUN`, `SAM` and `HSAM` — **all four
`Buildable.Prerequisites: ~disabled`** (`structures-defenses.yaml:643, :729, :814, :911`). Meanwhile
`iskander` and `HIMARS` are buildable at `~techlevel.high`.

**Symptom:** the mod ships buildable ballistic-missile launchers and four disabled counters.
**Hypothesis, unverified by a run:** ballistic missiles are uninterceptable in a live match today.
The confirming run is in §8.

This matters to scope. The cruise tier's headline feature — *"you can shoot it down"* — rests on a
path that has, as far as anyone can show, **never once worked in a real game.**

### 2.4 The buy → bank → fire loop needs zero new engine traits

Verified end to end (`recon/powers-buy-loop.md` §1). `Production.Produce`'s bodiless branch
(`Production.cs:126-131`) is deliberate, not accidental — `ProximityExternalCondition` opens with an
explicit `if (produced.OccupiesSpace == null) return;` guard for exactly this case
(`ProximityExternalCondition.cs:138-140`). `AllowMultiple` keys each purchase by ActorID so N buys
become N icons; `OneShot` removes a spent one. Two shipped precedents create a proxy the same way
(`SupportPowerCrateAction.cs:41-44`, `InfiltrateForSupportPower.cs:74-77`).

Three corrections to the prior costing, all from `recon/powers-buy-loop.md`:

- **The `ProductionFromMapEdge` silent-hang trap is real** — the item sits at 100% forever with no
  error. Wire the queue to `Production@Local` at **`structures.yaml:362-364`** (the prior recon's
  line numbers drift by 1–4 at this SHA).
- **The `RankAccumulation` trap does not exist.** The prior recon cited `player.yaml:22` as carrying
  `Types:`; that `Types:` belongs to `ProximityCaptor:` at `:16`, and `RankAccumulationInfo` has no
  `Types` field at all. The real guard requires `GainsExperienceInfo`, so a proxy never enters the
  stock and `VeterancyLevelInit` is unreachable. **A proxy is safe on any queue.**
- **There is a fourth trap nobody named, and it crashes rather than hangs.**
  `ProductionPaletteWidget.cs:683` calls `item.TraitInfo<RenderSpritesInfo>()` **unguarded**, and
  that throws. **Every buy-menu proxy must carry `RenderSprites:`.** The three commented
  `powerproxy.*` blocks at `misc.yaml:318-368` carry none — they were crate grants in Red Alert,
  never sidebar items. **They are not a drop-in template.**

### 2.5 Lobby control costs almost nothing, and the master switch is already in the lobby

`LobbyPrerequisiteCheckbox` (`LobbyPrerequisiteCheckbox.cs:19-57`) is a fully generic,
YAML-declarable checkbox that grants prerequisites. Three commented instances already sit in
`player.yaml:273-295` — **including `@NuclearAllowed` granting `global-nuclear`, which is exactly the
shape a per-power toggle wants.** Two live ones are in `coop-missions-rules.yaml`.

**Per-power toggles need zero C#.** The one caveat: WW3MOD's lobby routes sections through a
hardcoded table (`LobbyOptionsLogic.cs:138-178`), so a YAML-only checkbox lands in *Advanced →
Other* unless one dictionary line is added per option. There is no count limit — the Unit
Availability section already carries 24 checkboxes.

**The master switch already exists**: `LobbyDummyOptions.cs:217-219` publishes a `powers-enabled`
checkbox, already mapped to its section at `:177`. It renders dimmed only because `BuildOptions`
stamps `Placeholder = true` on everything it yields. Making it real is ~6 lines of C#, or zero if it
is redeclared as a `LobbyPrerequisiteCheckbox`.

**Gate the buy entry, not the power.** A `~`-prefixed prerequisite hides the item outright; gating
the *power* with `PauseOnCondition` leaves a clickable-but-inert icon in the top-left reading
**"ON HOLD"** (`SupportPowersWidget.cs:241-244`). Same cost, better result — and it is the same
mechanism a DEFCON ladder would later need.

---

### 2.6 The buy-menu redesign does not reach this feature — checked, not assumed

`WORKSPACE/buymenu-redesign-note.md` §0.1 (on `main` since `84077cc4`) establishes that the apparent
free space around the icon grid **is the sidebar frame**: `x 0–40` is an opaque brushed-metal panel
with a 3 px bevel, `x 229–237` the right bevel, and both sit *outside* the three columns — so a mark
drawn there cannot name a single unit. That is a geometry fact and it kills a whole class of sidebar
ideas.

**It does not touch this feature, for a structural reason.** The powers bin is
`Container@SUPPORT_POWERS` at `X: 10, Y: 10` (`ingame-player.yaml:16-38`) — a **top-level sibling**
of `Container@SIDEBAR_PRODUCTION` (`:1149`), not a child of it. It sits in the game viewport at
screen top-left, has no columns, and draws its **own** per-icon frame: `background-supportoverlay`
at 62×46, cloned per icon by `SupportPowerBinLogic`. Nothing in this proposal depends on space near
the production palette, and no power icon is ever drawn inside the sidebar frame.

The one place the feature *does* touch the sidebar is the Powers tab, and the redesign note carries
the four spare tab slots forward from the audit unchanged (`:65`). A Powers tab remains the **fourth
visible tab of seven slots**, at no layout cost.

### 2.7 The disabled counter-layer is a real v1 gap — and it is not this feature's to fix

All three ICBM-capable defences ship gated off, while the launchers that outrange them do not:

| Actor | Cost | `Buildable.Prerequisites` |
|---|---|---|
| `CRAM` | 1000 | `~disabled` |
| `AGUN` | 800 | `~disabled` |
| `SAM` | 2000 | `~disabled` |
| `iskander` / `HIMARS` | 6000 | `~player.<faction>, ~vehicles.<faction>, ~techlevel.high` — **buildable** |

**Recommendation: enabling them is NOT part of this first cut, and the parent should take it as a
separate v1 balance item.** Three reasons:

1. **It is not caused by this feature and does not block it.** The gap exists today because of the
   shipped Iskander and HIMARS. With the cruise tier deferred (§1), nothing in the first cut is
   interceptable, so the first cut neither creates nor depends on the gap.
2. **Un-gating three defence structures is a balance change with its own blast radius** — new
   buildables in the Defense queue, new counters to existing aircraft, and a benchmark re-baseline.
   Bundling it into a powers proposal would hide a balance decision inside a feature.
3. **It is the prerequisite for the deferred tier, so it wants to land first anyway.** Repairing the
   counter-layer and then adding the cruise missiles is the right order; doing both at once means
   neither can be evaluated.

**One caution for whoever picks it up.** Un-gating is one line each, but `20mm_CRAM` — the gun on
the actor literally named for counter-rocket defence — has the **lowest muzzle velocity in the mod**
and misses a ballistic missile by ~11 cells (§2.2). **Enabling `CRAM` would ship a counter-battery
building that cannot hit what it is named for.** The only weapon that comfortably runs a missile
down is `SurfaceToAirMissile` at speed 800, on `SAM`. That item is a weapons fix, not a prerequisite
edit, and should be scoped as one.

---

## 3. How many powers, and what is deferred

Answered in §1: **three entries — Kinzhal, one US fast strike, and the tactical nuke. The
cruise-missile tier is deferred.**

**Explicitly deferred, against the user's four named ideas:**

| User's idea | Verdict |
|---|---|
| Kinzhal hypersonic, uninterceptable | **ships** |
| A US near-equivalent | **ships** — §4 picks which |
| Cruise missiles, shootable by AA vehicles not MANPADs | **deferred to v1.1** — its counter-layer does not work (§2.7) |
| Nuclear strike as a power | **ships, lobby-gated OFF by default** |

Adding a further power later is a YAML block plus a cameo — new actor definition, buildable entry,
lobby checkbox. **No engine work**, because the delivery trait built for the first cut serves any
number of them. The only powers that would cost more are ones needing a *different delivery*: a
loitering weapon, a salvo landing on separate aim points, or anything that persists on the map.

## 4. What is the US answer to the Kinzhal?

The user is explicit that there is no exact US equivalent and that it need not match. Three honest
options, each grounded in real hardware rather than in balance intuition — the same standard the tank
ammunition pricing was held to. **This is the one question in the document with no defensible default;
it is a taste call.**

**(a) LRHW "Dark Eagle" — the literal equivalent.** The US Army's ground-launched hypersonic glide
body, fielded from 2023. Same role, same speed class, different basing (ground vs air-launched).
*Cheapest by far:* it is the Kinzhal's YAML with a different name and cameo. *Weakness:* the two
factions get the same tool, and the asymmetry the user was inviting never happens.

**(b) GBU-57 MOP — the bunker buster.** A 13-tonne penetrator, and the one weapon in the world built
specifically to kill hardened structures. Not hypersonic; unstoppable because nothing can reach its
delivery, not because it is fast. *Design payoff:* Russia buys **speed**, America buys
**penetration** — the US strike hits structures far harder and units far less, which is a real
tactical difference rather than a reskin. *Cost:* one extra warhead profile weighted by target type.

**(c) JASSM-ER salvo.** Stealthy subsonic cruise missiles arriving together from several bearings —
uninterceptable through stealth and saturation rather than speed. *Design payoff:* the other
asymmetry — Russia hits one point instantly, America covers a wider footprint but telegraphs it,
giving the defender seconds to react. *Cost:* the highest of the three; a salvo needs several actors
spawned on separate bearings against one aim point, which the delivery power must support.

**Recommendation: (b).** It is the option that makes the two factions play differently, it costs one
warhead profile rather than a new delivery mode, and "the US answer to a hypersonic is not a
hypersonic" is truer to the real asymmetry than option (a). Option (a) is the safe pick if the
priority is shipping.

---

## 4a. The art cost — this shaped the first cut rather than being found after it

The sequence bindings at `sequences-misc.yaml:14-42` look like a ready-made set of power cameos, and
every reference resolves cleanly. **The names lie.** Decoded through the engine's own SHP loader
(`WORKSPACE/mockups/buymenu_shp_dump.py`) and looked at:

| Sequence / SHP | What the picture actually is | Baked caption |
|---|---|---|
| `icon: abomb` → `atomicon.shp` | mushroom cloud | **`NUCLEAR BOMB`** ✔ usable |
| `icon: precicon` → `precicon.shp` | explosion on a target | **`PRECISION STR`** ✔ usable |
| `icon: cmissicon` → `cmissicon.shp` | **biohazard trefoil** | `CHEMICAL STRIKE` ✘ |
| `missicon.shp` | **a building** | `TECH CENTER` ✘ |
| `icon: artyicon` → `artystrikicon.shp` | rockets in flight | `ARTY BARRAGE` ✘ |
| `v2rlicon.shp` | truck-mounted launcher | `V2 LAUNCHER` ✘ |
| `icon: paranuke` → `paranukeicon.shp` | falling bomb + canopy | `PARANUKE` ✘ |

**`cmissicon` is a chemical strike, not a cruise missile.** Anyone costing this feature off the
sequence names — as this agent initially did — concludes the cruise-missile art is free. It is not.
Captions are **baked into the pixels** at rows 42–46 of each 64×48 cameo, so a wrong caption is not
a config change.

And the pipeline does not cover them: `tools/cameo/convert.py` carries a **49-entry roster with zero
power icons in it**, so any new power cameo needs the roster extended as well as the art drawn.

### What each candidate cut costs in new cameos

| Cut | New cameos needed |
|---|---|
| **Recommended (3): Kinzhal, US strike, nuke** | **1** — Kinzhal reuses `precicon`, nuke uses `atomicon` |
| Four (adds the cruise pair) | **3** |
| All five ideas | **3–4** |

**This is why the cruise tier is the right thing to defer and the nuke is the wrong thing.** The
nuke is the only entry in the whole feature whose cameo is both present *and* correctly captioned;
the cruise pair is the most art-expensive part of it.

**One honest wrinkle in the recommended cut:** reusing `precicon` for the Kinzhal gives Russia a
generic `PRECISION STR` caption while America gets bespoke art. If that asymmetry reads badly, the
fix is a second new cameo — two total, still cheaper than any other cut.

---

## 5. Interception — the design for the deferred tier

*The recommended first cut contains nothing interceptable, so none of this is v1 work. It is
recorded because it is the design the cruise tier would ship with, and because the numbers are what
justify deferring it.*

Both tiers use `BallisticMissile` — arc-only, no level-flight cruise mode, but `LaunchAngle` defaults
to `WAngle.Zero` (`BallisticMissile.cs:27`), so a near-flat run-in is expressible. The two profiles
differ in **three numbers**:

| | Fast tier (Kinzhal / US) | Cruise tier (Kalibr / Tomahawk) |
|---|---|---|
| `LaunchAngle` | high — steep arc onto the target | low — flat, long visible run-in |
| `Speed` | **2000** = 32.6 cells/s — **3.8× an F-16**, 1.5 s across a 50-cell run | **350** = 5.7 cells/s — 8.8 s across the same run, slower than every aircraft in the game |
| `Targetable` | a type nothing lists (e.g. `Hypersonic`) | `ICBM`, as `^ShootableMissile` ships |

`cells/s = Speed × 0.016276`. Shipped anchors for scale: Iskander missile 600 (9.8 c/s), HIMARS
rocket 500 (8.1 c/s), F-16 airframe 525 (8.5 c/s), A-10 390 (6.4 c/s).

**Precision is free and is the default.** `BallisticMissileFly` ends by setting the actor's position
to the target exactly and killing it (`:208-210`); `Explodes` detonates on the corpse. `Inaccuracy`
is a *projectile* field and there is no projectile on this path. **Giving the cruise tier some
scatter is the thing that would need work, not making the Kinzhal precise.**

**Do not simply delete `Targetable` from the fast tier** — that also makes it immune to splash
damage. Give it a target type nothing lists.

**Changes needed to make the cruise tier shootable:** `ICBM` added to `Stinger.quad` and `9M311`
`ValidTargets`, and to `^AutoTargetAAIFV`'s priority table so the AA vehicles engage without a manual
order. Two weapons, one template. No gun touched.

**What the player sees when it works.** `BallisticMissileFly` ends with `self.Kill(self)`
(`BallisticMissileFly.cs:209`), so a normal impact and an interception are the **same code path** —
death fires `Explodes`. Warhead falloff is measured in 3D (`SpreadDamageWarhead.cs:97`), so a kill
near the arc apex (4–8 cells up) is harmless and a kill in the last second of the dive is not.
**That reads correctly:** intercept early and nothing happens; intercept late and it still hurts.

One loose end worth knowing: `IskanderExplosionAirborne` exists, is entirely commented out, has zero
references, and as written is byte-identical to the full warhead
(`weapons-explosions.yaml:619-629`). Someone started the harmless-airburst weapon and stopped.

---

## 5a. Balance shape, in real numbers

**Method, stated plainly: arithmetic over shipped YAML, re-deriving the engine's damage pipeline
from `DamageWarhead.cs`. The combat-sim was NOT used, and is not usable for this question** —
`tools/combat-sim/build/` does not exist (it needs a TypeScript build), `dump-stats.sh` refuses
without `engine/bin/OpenRA.Utility.dll` which is absent, and the committed `data/stats.json` is stale
in exactly the fields this needs. `DOCS/recipes/BALANCE.md` prescribes the sim; on this question the
arithmetic is the honest method, and no sim output is presented because none was produced.

### The anchors

| Warhead | Peak damage on an Abrams (28,000 HP) | Read as |
|---|---|---|
| `IskanderExplosion` | **~62,800** — 2.24× HP on a direct hit | the shipped conventional ballistic missile |
| `Atomic` | **~266,000** — 9.5× overkill | a 6.25-cell airburst; vaporises to ~8.4 cells, kills infantry to ~23, sets structures alight to ~28 |

**The recommendation follows directly: a Kinzhal should be "one Iskander missile, except you did not
have to drive a 6000-credit launcher into range."** `IskanderExplosion` is the right warhead to reuse
or clone. **Above roughly 80,000 point damage a conventional missile stops reading as a missile and
starts reading as a small nuke** — which is a line worth not crossing while the actual nuke is in the
same menu.

The cruise tier should sit visibly below that: cheaper, slower, interceptable, and worth firing in
twos and threes rather than as a decisive blow.

### One fact that constrains every power in the list

**You cannot hit the Supply Route with any of them.** `SUPPLYROUTE` carries
`Targetable: TargetTypes: NoAutoTarget` (`structures.yaml:296-297`), and no weapon's `ValidTargets`
contains `NoAutoTarget`, so `IsValidAgainst` rejects every warhead in the mod. So no missile power —
including the nuke — can be used to end a game by deleting the opponent's production. That is
correct and it is what makes these powers safe to price aggressively: **they are tools for killing
armies and buildings, never a win button.**

*(In passing: the engine comment at `TimeOrSrCaptureWinRule.cs:49` attributes this to
`Armor: Indestructable`. It is wrong — that armour type carries no `Thickness` and no weapon
references it, so it contributes nothing. Correct outcome, wrong stated cause. Worth fixing on
sight per the knowledge-bank rule.)*

---

## 6. The purchase and activation loop

- **How many can be held?** Unbounded by default. Three YAML caps exist and only `BuildLimit` reaches
  the bank — but it counts actors owned by the player, **and a spent proxy is still one**, so
  `BuildLimit: 3` means "three for the whole match", not "hold three at a time". **If a bank cap is
  wanted, disposing the spent proxy stops being optional.**
- **Does the bank survive the producer's death?** Yes, outright. The proxy is an independent
  player-owned actor and `SupportPowerManager.ActorRemoved` keys on the removed actor. Moot in
  practice anyway: `SUPPLYROUTE` inherits `^ExistsInWorld` / `^SpriteActor` / `^SelectableBuilding`
  but **not `^Building`**, which is where `Sellable:` lives — the Supply Route can be neither sold
  nor sensibly destroyed.
- **Refunds.** Cancel refunds what was actually paid: money drains per tick during the build and
  every cancel path refunds `TotalCost - RemainingCost`. **One exception:** a *contested* Supply
  Route does not refund — it halts the drip at speed modifier 0 and the part-paid item freezes.
- **Past six icons?** Nothing breaks. Six is only the hotkey count; the column is a single unwrapped
  run at 56 px pitch with no clipping, and the first collision with the bottom command bar is at the
  **12th** icon at 720p, the **19th** at 1080p.

---

## 7. Why this makes the deferred nuke design cheaper, not harder

The user's nuke document ends mid-sentence, but four of its five ideas are settled and only the
win-condition inversion is open. Its fourth idea — *"single small tactical nuclear launches … meant
for the already losing player"* — **is a buyable power**. That is what entry 5 is.

And the gating mechanism is identical. A DEFCON ladder is a condition that controls whether a power's
buy entry is available; the lobby toggles this proposal already needs are conditions that control
whether a power's buy entry is available. **Building the powers with condition-gated buildability
leaves DEFCON a configuration change rather than a rewrite.**

---

## 8. What is not verified, and the runs that would settle it

No game was launched. These belong to the parent manager, serially.

1. **Can anything intercept anything today?** Scenario: an Iskander fires at a base defended by a
   `strykershorad`. **The answer is the verdict string**: does the missile reach the ground.
   Establishes the §2.3 hypothesis before the cruise tier is designed around it.
2. **Does a 600 interceptor actually close on a sub-500 missile?** The kinematics say head-on and
   crossing yes, tail-chase no. **The answer:** intercept rate over a fixed set of approach angles.
   This is the number the cruise tier's whole value rests on.
3. **Does a flat cruise missile clear `MinAirborneAltitude: 5`?** If not, the `airborne` condition
   never applies and the *other* warhead fires (`Explodes` vs `SpawnedExplodes` are split on exactly
   that condition). **The answer:** which warhead fires on a low-angle shot.
4. **Does a bodiless proxy really produce?** The whole buy loop is traced statically and never run.
   **The answer:** does the cameo complete and an icon appear top-left.

Anything a scenario needs to report must be **printed into the verdict string**, because the autotest
harness's other artefacts are not trustworthy.

---

## 9. What the user needs to rule on

1. **Three powers, or more?** Recommended: Kinzhal, one US fast strike, and the nuke, with the
   cruise-missile tier deferred to v1.1. The alternative is four or five entries, at the cost of two
   more cameos and a dependency on a counter-layer that does not currently work.
2. **Which US answer** (§4) — LRHW "Dark Eagle" (safe, and effectively a Kinzhal reskin), the
   **GBU-57 MOP bunker-buster** (recommended — makes the factions play differently for one extra
   warhead profile), or a JASSM-ER salvo (most distinctive, most expensive).
3. **Prices.** Movable in the mockup. Proposed: fast strike 4000, nuke 15000, against a main battle
   tank at ~2450 and an Iskander at 6000 for two shots. The warhead anchors in §5a are what these
   should be argued from.
4. **Does the nuke ship at all in v1?** Recommended in, lobby-gated OFF by default. It is the
   cheapest entry in the table and the user asked for it by name, but it is also the one carrying an
   unresolved design in their own words.
5. **Should the Kinzhal reuse the `PRECISION STR` cameo**, or wait for bespoke art? (§4a)

## 10. The single biggest risk

**Off-map delivery is the only genuinely new engine code in the feature, and it has no shipped
precedent.** No support power can currently put a `BallisticMissile` actor on the map:
`AirstrikePower` hard-requires `AircraftInfo` (`AirstrikePower.cs:75`), and `SpawnActorPower` spawns
at the *target* cell and never sets `BallisticMissile.Target`, which `BallisticMissileFly` reads
unconditionally — the documented `InvalidOperationException` at `MissileSpawnerMaster.cs:85-87`.

**And `NukePower` cannot be bent into the job.** Its spawn-to-target offset is constructed
Z-only — `new WVec(WDist.Zero, WDist.Zero, velocity * (impactDelay - turn))` (`NukeLaunch.cs:73`) —
and the descent begins directly above the target (`:76`). `SkipAscent` removes the ascent; it does
not add a lateral leg. There is no YAML field that reaches it. This is structural, not a tuning gap.

**The mitigation is that every part of the new trait is copied from something shipped**, which is
why the estimate is ~80–120 lines rather than a subsystem: the edge cell from
`AirstrikePower.cs:79` (`ChooseClosestEdgeCell`), the target assignment from
`MissileSpawnerMaster.cs:112,116` (`bm.Target = Target.FromPos(...)`, then add to world —
`BallisticMissile.AddedToWorld` queues the flight itself), and the beacon and camera from
`NukePower.cs:180-208`. No new projectile, no new warhead type, no new subsystem.

**If the user would rather write no C# at all**, `ParatroopersPower` is the only shipped power that
computes a genuine map-edge entry along a chosen bearing (`ParatroopersPower.cs:103`, and it is the
only one that reads the player's directional pick at `:83`). Pointed at a missile-shaped aircraft it
yields a credible *cruise missile* — but not a Kinzhal, and it reintroduces the aircraft-actor shape
the user wanted to move away from.

**A design precedent to honour either way:** commit `a20c8a82` reworked the airstrike so that in this
mod **a support power arrives from the map edge nearest your own base**, not from a bearing the
player picks. Two `AirstrikePower` fields were left dead by that rework — `QuantizedFacings` has no
reader anywhere, and `UseDirectionalTarget` draws eight direction arrows and then discards the
choice, because `AirstrikePower` never reads `order.ExtraData`. Matching the established behaviour
costs nothing; deviating from it should be deliberate.

That ~80–120 line estimate is still the thing most likely to be wrong, and everything else in this
proposal depends on it: all four entries share that one delivery path.

## YAML files touched

**None.** This proposal and the mockup add no rules, so the lint gate has nothing new to say about
this branch.
