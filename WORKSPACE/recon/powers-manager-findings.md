# Manager findings — missile powers, read directly rather than delegated

**Read at `main @ 2c8488ef`.** Static analysis, no game runs, no YAML lint. Every claim carries a
`file:line` read at that SHA. This is the manager's own pass, kept separate from the three worker
research documents so the provenance of each claim stays clear.

**Tick rate is 16.667 tps** (`Timestep: 60` ms, `mods/ww3mod/mod.yaml:369-372`). `seconds = ticks × 0.06`.

---

## 1. The mod already ships shootable missile actors. The interception question is largely answered.

The brief treated "can a projectile be shot down" as the highest-risk unknown, and assumed the
answer would require modelling a missile as an actor. **That work is already done and shipped.**

`^ShootableMissile` (`mods/ww3mod/rules/defaults.yaml:1074-1101`) is a complete missile-as-actor
template:

| Trait | Value |
|---|---|
| `Armor` | `Type: Light` |
| `BallisticMissile` | `LaunchAngle: 128`, `Speed: 110`, `AirborneCondition: airborne` |
| `Targetable@Ground` | `TargetTypes: ICBM`, `RequiresCondition: !airborne` |
| `Targetable@Airborne` | `TargetTypes: ICBM`, `RequiresCondition: airborne` |
| `Detectable` | `Vision: 1`, `Radar: 1`, `Position: Ground` |
| also | `HitShape`, `RejectsOrders`, `Interactable`, `WithFacingSpriteBody`, `WithShadow` |

Two actors inherit it today: `IskanderMissile` (`vehicles-russia.yaml:1116`) and `HIMARSMissile`
(`vehicles-america.yaml`, the American twin). `IskanderMissile` adds `Health: HP: 100`,
`MissileSpawnerSlave`, `LeavesTrailsCA`, and an `Explodes` / `SpawnedExplodes` pair split on the
`airborne` condition.

**A dedicated `ICBM` target type therefore already exists**, and the whole opt-in chain is YAML.

## 2. …and nothing in the shipped game can currently use it.

`^AutoTargetAirICBM` (`defaults.yaml:747-750`) is the only auto-target template listing `ICBM`. It
is inherited by exactly three actors — and **all three are gated off**:

| Actor | `structures-defenses.yaml` | Cost | `Buildable.Prerequisites` |
|---|---|---|---|
| `CRAM` | `:622` (autotarget `:624`) | 1000 | `~disabled` |
| `AGUN` | `:707` (autotarget `:709`) | 800 | `~disabled` |
| `SAM` | `:784` (autotarget `:786`) | 2000 | `~disabled` |

Meanwhile the launchers **are** reachable: `iskander` and `HIMARS` are both
`Prerequisites: ~player.<faction>, ~vehicles.<faction>, ~techlevel.high` at `Cost: 6000` each
(`vehicles-russia.yaml:993`, `vehicles-america.yaml`) — and `~techlevel.high` is a live tech level,
used by shipped aircraft (`aircraft-america.yaml:310`, `aircraft-russia.yaml:127, 303`).

> **Symptom:** the mod ships buildable ballistic-missile launchers and three disabled counters.
> **Hypothesis, not verified by a run:** an Iskander or HIMARS missile is currently uninterceptable
> in a live match. **The check that would confirm it:** a scenario that launches an Iskander at a
> base defended by a `strykershorad` and asserts the missile reaches the ground.

## 3. "AA vehicles yes, MANPADs no" has a hardware discriminator already sitting in the data

The user asked for cruise missiles interceptable by AA vehicles but not by MANPADs. The obvious
worry is that this is arbitrary — the mod deliberately models Stinger as Stinger regardless of
platform (`vehicles-america.yaml:989`: *"Stinger is Stinger regardless"*), so splitting on the
missile would break a stated principle.

**It does not have to split on the missile. It splits on the gun.**

| Platform | File | Armaments | Auto-target |
|---|---|---|---|
| `strykershorad` (US, 2500) | `vehicles-america.yaml:874` | `25mm.Bradley` **(cannon)** + `Stinger.quad` | `^AutoTargetAAIFV` |
| `tunguska` (RU, 1700) | `vehicles-russia.yaml:823` | `30mm.Tunguska.AA` **(cannon)** + `9M311` | `^AutoTargetAAIFV` |
| MANPAD infantry (300) | `infantry.yaml:1841` | `MANPAD` — `ValidTargets: Air`, `Projectile: Missile`. **No cannon.** | — |

**Both AA vehicles carry a rapid-fire cannon; the MANPAD carries only a shoulder-fired IR missile.**
That is the real-world discriminator and it is the correct one: cruise-missile defence is a gun
problem — seconds of engagement window, low IR signature, volume of fire — which is also why `CRAM`
(Counter-Rocket, Artillery and Mortar; a gun system) is one of the three actors already on the ICBM
list. Nothing about Stinger needs to change.

So the design rule is **guns intercept missiles, shoulder-fired IR missiles do not**, and the
implementation is adding `ICBM` to the `ValidTargets` of two cannon weapons plus the AA vehicles'
auto-target priorities.

**Caveat, and it is the same shape as the `^30mm` blast-radius problem at
`WORKSPACE/recon/powers-and-preloaded-transports.md` §1.3:** `25mm.Bradley` is named for the
Bradley, so adding `ICBM` to it likely arms every Bradley too. The YAML at
`vehicles-america.yaml:871` already carries a commented `# 30mm.Stryker`, which suggests a
dedicated Stryker weapon was intended. A contained per-actor weapon variant is probably the right
shape. **Not verified — the full inheritor list of `25mm.Bradley` was delegated, not read here.**

## 4. `^AutoTargetAAIFV` is a third gate, and it does not list ICBM

`^AutoTargetAAIFV` (`defaults.yaml`) inherits `^AutoTargetGroundAntiTank` and adds priorities for
`Helicopter` (5), `Aircraft` (4) and `Vehicle`. **`ICBM` appears nowhere in it.** So even with the
weapon's `ValidTargets` fixed, an AA vehicle would not *automatically* engage a missile — it would
need the priority added, or the player would have to order the shot manually. Three gates must line
up: the target's `Targetable.TargetTypes`, the weapon's `ValidTargets`, and the
`AutoTargetPriority.ValidTargets`.

## 5. The art is worse than the sequence bindings suggest — this disproves my own earlier read

`sequences-misc.yaml:14-42` binds a promising-looking set of power icons, and every reference
resolves. **The names lie.** Decoded with `WORKSPACE/mockups/buymenu_shp_dump.py` (a read-only
loader port already in the tree) and viewed:

| Sequence name | SHP | What the picture actually is | Baked caption |
|---|---|---|---|
| `icon: cmissicon` | `cmissicon.shp` | **biohazard trefoil** | `CHEMICAL STRIKE` |
| `missicon.shp` | `missicon.shp` | **a building** | `TECH CENTER` |
| `icon: artyicon` | `artystrikicon.shp` | rockets in flight | `ARTY BARRAGE` |
| `v2rlicon.shp` | `v2rlicon.shp` | truck-mounted launcher | `V2 LAUNCHER` |
| `icon: paranuke` | `paranukeicon.shp` | a falling bomb + canopy | `PARANUKE` |
| `icon: abomb` | `atomicon.shp` | **mushroom cloud** | **`NUCLEAR BOMB`** ✔ |
| `icon: precicon` | `precicon.shp` | **explosion on a target** | **`PRECISION STR`** ✔ |

**"`cmissicon`" is a chemical strike, not a cruise missile.** Anyone costing this feature off the
sequence names — as I initially did — will conclude the cruise-missile art is free. It is not.

**Exactly two shipped cameos carry a caption that fits a missile power**: `atomicon`
(`NUCLEAR BOMB`) and `precicon` (`PRECISION STR`). Every other power in the first cut needs a new
64×48 cameo with a baked caption.

And the pipeline does not cover them: `tools/cameo/convert.py` carries a 49-entry roster and
**grep for `atomicon` / `precicon` / `abomb` / `power` returns nothing** — no power icon is in it.
This corroborates `WORKSPACE/recon/buymenu-audit.md` §6.1 from the other direction: the audit says
powers have no baked captions in the roster; the shipped SHPs *do* have captions, but they were
baked outside the current pipeline and several of them are wrong.

> **Consequence for the first cut:** favouring powers whose art already exists is a real, cheap
> lever. Nuke and a precision strike ship with correct cameos today; a cruise-missile tier does not.

## 6. Iskander / HIMARS overlap — a design question the brief did not raise

`iskander` (RU, 6000) and `HIMARS` (US, 6000) are shipped, buildable, on-map ballistic-missile
launchers. Each carries `AmmoPool` `Ammo: 2`, cannot be rearmed, and
`InitialResupplyBehavior: Evacuate` — *"one load per launcher, and it evacuates for a refund once
both missiles are spent"* (`vehicles-russia.yaml:997`). The missile the player is buying is the
`IskanderMissile` / `HIMARSMissile` actor (`Cost: 50` / `30`).

**So the mod already has a precision missile strike, and it already answers the US/Russia
asymmetry** — with real hardware names, at the same price point, differing in launch behaviour
(`LaunchRiseErect: true` for the erecting Iskander, straight-from-tube for HIMARS).

An off-map power must therefore justify itself *against* the Iskander, not in a vacuum. The honest
difference:

| | `iskander` / `HIMARS` (shipped) | An off-map power (proposed) |
|---|---|---|
| Delivery | on-map vehicle, must be driven into range | off-map, global reach |
| Risk | killable before it fires; has a visible range circle (`RenderRangeCircle@1`) | nothing to protect |
| Counter | kill the launcher | intercept the missile, or nothing |
| Economics | 6000 for 2 shots, refunded on evacuation | per-shot purchase |

That is a genuine difference in kind and both can coexist — but the pricing has to reflect that the
power removes the risk the launcher carries, and the naming has to avoid two things called
"ballistic missile strike".

## 7. The nuke design doc is not as unfinished as it reads

`WORKSPACE/archive/plans/260324-nukes.md` (9 lines, the user's own prose) does end mid-sentence,
but four of its five ideas are settled and only one is open:

1. **Settled** — the shipped nuke is a *tactical* nuke, deliberately more powerful than RA's.
2. **Settled** — nukes are thematic, not a routine tool; the intended feeling is dread.
3. **Open** — a DEFCON ladder where reaching DEFCON 1 unlocks an all-out strike that ends the game
   and **declares the launcher the loser**.
4. **Open, and the sentence that trails off** — small tactical launches for the *losing* player,
   where winning after using one still counts as a win.
5. Implicit — this replaces time-limited games.

**Idea 4 is compatible with a buyable power; idea 3 is not.** A tactical nuke bought at a high price
and fired from the top-left *is* "a single small tactical nuclear launch". The win-condition
inversion is a separate system that touches `MustBeDestroyed` and the `TimeLimitManager` block
(`world.yaml:548-562`) and is not v1 work.

**And the lobby-gating mechanism the user is asking for is the same mechanism a DEFCON ladder would
need** — a condition that gates whether a power's buy entry is available. Building the powers with
condition-gated buildability leaves the DEFCON layer a cheap addition later rather than a rewrite.

## 8. Production queues — six declared, three visible

`player.yaml:23-93` declares `Building`, `Defense`, `Vehicle`, `Infantry` (parallel), `Ship`,
`Aircraft`; `ClassicProductionQueue@Fakestructure` is commented at `:94-104` and is the template a
new queue would copy. `WORKSPACE/recon/buymenu-audit.md` §6.1 establishes the tab column has
**seven** 28×28 slots on a 31 px pitch with three occupied, so a Powers tab is a free insert and
would be the **fourth visible tab, not the seventh**.

## 9. Both missile tiers can be one trait, separated by three numbers

`BallisticMissile` (`engine/OpenRA.Mods.Common/Traits/BallisticMissile.cs:20`) is described as
*"will fly in ballistic path then will detonate itself upon reaching target"* — it is arc-only,
there is no level-flight cruise mode. **But `LaunchAngle` defaults to `WAngle.Zero`** (`:27`), so a
near-flat launch is expressible, and the arc degenerates to a shallow run-in.

That means the Kinzhal tier and the cruise tier can share one trait and differ only in YAML:

| | Kinzhal (hypersonic) | Kalibr / Tomahawk (cruise) |
|---|---|---|
| `LaunchAngle` | high — steep arc, comes down on the target | low — flat, long visible run-in |
| `Speed` | high (Iskander uses `600`) | low (trait default is `17`) |
| `TerminalSpeed` / `TerminalAcceleration` | set, so it accelerates into the dive (`:90, :94`) | unset — constant speed, easy to lead |
| `Targetable` | removed or a type nothing lists | `ICBM`, as `^ShootableMissile` ships |

`Speed` is *WDist per tick*, so at 16.667 tps a `Speed: 600` missile covers 10 000 WDist/s ≈ 9.8
cells/s. The trait default of `17` is ≈ 0.28 cells/s — the usable range is enormous and the two
tiers will read as visibly different without any code.

**One flag, unverified:** `MinAirborneAltitude: 5` (`:97`) decides when the `airborne` condition
applies. A deliberately flat cruise missile may never clear it, which would flip which of
`Explodes` / `SpawnedExplodes` fires (they are split on exactly that condition in
`IskanderMissile`). **The check:** set a low `LaunchAngle` on a test missile and observe which
warhead fires — needs a run, so it belongs to the parent manager.

---

## What I did not verify

- Whether an interception can actually *connect* — projectile lead against a `BallisticMissile` in
  flight. Delegated; it is now the highest-risk residue.
- The full inheritor list of `25mm.Bradley` and `30mm.Tunguska.AA` (§3 blast radius).
- What a missile killed mid-flight actually does on screen (`Explodes` vs `SpawnedExplodes`).
- Anything requiring a running game. No launches were performed; per the standing rule, launches
  serialize through the parent manager.

## YAML files touched

None. This document is the only file added.
