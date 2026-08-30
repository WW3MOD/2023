# When a shot achieves nothing, can the player tell why?

**Ref:** `main @ 5a985337`, clean. Worktree `wt/recon-battle-feedback`.
**Method:** data only. No build, no game launch, no autotest, no `make test`. Everything below is
read off engine C#, mod YAML, NUnit tests and commit history. Where a judgement needs eyes on a
screen I say so in [§11](#11-what-i-could-not-determine).
**Mockup:** [`recon-battle-feedback/mockup.html`](recon-battle-feedback/mockup.html).

---

## The one-paragraph answer

**No — but the reason is routing, not absence.** Every piece of machinery needed to explain a
failed shot is already built, already wired, and already running in the shipped game. `FloatingText`
exists. `CombatDebugOverlay` already computes and draws the damage number over every damaged actor.
`GunTrace` already formats the complete penetration breakdown — raw damage, thickness, penetration,
versus, final — at the exact moment the shot resolves. One of these goes to a developer wireframe
toggle and the other to `debug.log`. **The explanation is computed in every shipped build and thrown
away.** The question in front of you is therefore not "what should we build" but "how much of what
we already compute should reach the player, and in what form".

---

## 1. A correction to the brief's premise, first

The brief says a tooltip redesign "is about to publish per-weapon damage, penetration, range and
blast figures", and that the danger is a tooltip promising 36000 while a hit does nothing.

**VERIFIED — that is not what shipped.** Commit `0163ca22` changed two engine files,
`AmmoPool.cs` and `ProductionTooltipLogic.cs`. Its own merge message says *"No styling ships here —
the element vocabulary and per-weapon sections are still mockups awaiting the user's verdict."*
I grepped `Penetration|Damage|Warhead|Range|Spread|ReloadDelay` across
`engine/OpenRA.Mods.Common/Widgets/`: **zero hits in tooltip code.** The tooltip publishes ammo and
supply economics only, and it is a *production-palette* tooltip
(`ProductionTooltipLogic.cs`), not a world-hover one.

So the interface is **silent, not lying**. That changes the framing in a way that matters: this
proposal is about *whether to open a surface*, not about *repairing one that already misleads*.
It also removes the deadline pressure the brief implies — nothing forces a decision this week.

**And the trap is already pinned in a test.** `TooltipWeaponResolutionTest.cs:23-27` records that a
naive damage readout reads `Armament.Weapon` → warhead `Damage` and gets **50** for the HIMARS,
whose real payload is **36000** — the payload lives on `HIMARSExplosion` via a spawned missile. Any
future damage readout must resolve through the spawn chain *and* through `Inherits`.

---

## 2. What feedback exists today — the inventory

All **VERIFIED** unless marked.

### 2.1 Impact effects: chosen by target type, never by outcome

`CreateEffectWarhead` spawns the sprite (`:150`) and plays the sound (`:153-155`). It is a **sibling
warhead** of `DamageWarhead`, evaluated independently, and **has no access to the damage result at
all**. Its only branching is `ActorTypeAtImpact` (`:67-96`) → Valid/Invalid/None, which tests target
types and player relationships.

Weapons discriminate by writing parallel warhead entries with different `ValidTargets` — ground vs
water (`weapons-ballistics.yaml:41-46`), ground vs air (`:372-373`). **There is no armour-vs-infantry
impact distinction anywhere**, and no effect keyed to how much damage landed. The 192-damage case
renders the identical sprite and plays the identical `kaboom12.aud` as a lethal hit from the same
weapon.

### 2.2 Sound: same story

79 `ImpactSounds` occurrences under `mods/ww3mod/rules/`. Selection follows the same `ValidTargets`
split. **Searched `ricochet`, `bounce`, `deflect`, `Penetration` across `engine/` and `mods/` —
there is no ricochet sound, no "ineffective" sound, no penetration-failure sound in the mod.**

`AnnounceOnKill` exists in the engine (`Traits/Sound/AnnounceOnKill.cs:17-35`) and is **referenced
nowhere under `mods/`** — engine-only, unwired.

### 2.3 There is no health bar. This is the largest single finding

`engine/OpenRA.Mods.Common/Graphics/SelectionBarsAnnotationRenderable.cs:181`:

```cs
// if (DisplayHealth) DrawHealthBar(health, start, end);
```

Commented out deliberately in `e670ab96` (2024-08-13). The in-file comment (`:168-181`) states that
`DrawHealthBar` and `GetHealthColor` are therefore unreachable dead code, that **two people have
already spent real effort improving that chain believing it renders**, and closes:

> *"Re-enabling this is a product decision, not a cleanup: it turns on an indicator the user
> switched off himself. **Ask before uncommenting.**"*

I have not proposed re-enabling it. It is raised as a question in [§9](#9-the-separate-product-question).

Health is read instead from **four discrete pip bands** — `^DamageVehiclePips`
(`defaults.yaml:156-179`), `^DamageInfantryPips` (`infantry.yaml:722-745`), gated on
`light/medium/heavy/critical-damage` conditions. `WithDecorationBase.cs:111` defaults
`RequiresSelection = false` and the pip blocks do not override it, so **pips show without selecting
the unit**. Thresholds (`Health.cs:85-105`): `Undamaged` iff `HP == MaxHP`, `Light` >75%,
`Medium` <75%, `Heavy` <50%, `Critical` <25%.

**The arithmetic the brief asked for.** 192 of a T-90's 24000 HP is **0.8%**. The first such hit
flips `HP == MaxHP` false and lights one pip. **Every subsequent hit shows nothing** — about 32 hits
to reach `Medium`. So the player gets one binary "has been touched" flicker, then ~31 hits of total
silence. Had the bar been enabled it is 3 px tall (`:120-122`) spanning the sprite bounds: on a
~30 px sprite, 0.8% is **0.24 px**. It would not have been visible either.

### 2.4 Damage states: not on living vehicles

`WithDamageOverlay` fires on `INotifyDamage.Damaged` (`:82-97`), `MinimumDamageState = Heavy`
(`:47`) — so smoke starts only below 50%, once (`isSmoking` latch, `:87`). In WW3MOD it is on
**structures** (`structures.yaml:189,195,201`) and **vehicle husks**
(`husks/husks-vehicles.yaml:14`). Searched `vehicles*.yaml` and `infantry.yaml`: **no
`WithDamageOverlay` on living combat vehicles or infantry.** A damaged tank does not smoke; its
husk does.

### 2.5 Floating text exists — and is already wired to damage

This is the "do not assume absence" case, and it fired.

`engine/OpenRA.Mods.Common/Effects/FloatingText.cs:21-62` — `FloatingText(WPos, Color, string, int)`,
font `TinyBold`, rises `WVec(0,0,86)`/tick, fog-suppressed (`:52-53`).

`CombatDebugOverlay.cs:124-136` **already prints exact damage as floating text on every damaged
actor**:

```cs
var damageText = $"{-e.Damage.Value} ({e.Damage.Value * 100 / maxHP}%)";
self.World.AddFrameEndTask(w => w.Add(new FloatingText(self.CenterPosition, e.Attacker.OwnerColor(), damageText, 30)));
```

**And the trait is mounted on `^ExistsInWorld` at `mods/ww3mod/rules/defaults.yaml:4`** — i.e. on
essentially every actor in the game. The gate is `debugVis.CombatGeometry`, the ingame
*Show Combat Geometry* checkbox (`DebugMenuLogic.cs:99-100`), which also draws hitshape and muzzle
wireframes. **So `-192 (0%)` would float over that tank today if a player ticked a developer box.**
Note it zero-suppresses (`:126`) and integer-truncates, so the motivating case prints as `0%`.

### 2.6 The full explanation is already computed — into a log file

`engine/OpenRA.Mods.Common/GunTrace.cs` — static, `WW3_GUNTRACE=1`, writes via
`Log.Write("debug", ...)`. Its doc-comment says it exists precisely *"for diagnosing 'it fires and
nothing dies'"*. Call sites form a complete chain: `Armament.cs:457,465` → `Bullet.cs:401-402` →
`SpreadDamageWarhead.cs:104-110` / `TargetDamageWarhead.cs:47-96` → **`DamageWarhead.cs:248-249`**,
which prints `rawDamage`, `afterThickness`, `thickness`, `pen`, `modifiers`, `versus`, `FINAL`,
`hpBefore`.

### 2.7 Two shipped precedents that constrain any new cue

- **`WithHealFlash`** (`82938ec8`) marks each heal impact with a `FlashTarget` tint. It was measured
  for the first time on 2026-08-23: the trait works and the number is exact —
  `(50,98,183) → (67,137,255)`, a clean 1.4× lift on 124 sprite pixels. **And it was still at the
  threshold of perceptibility**: on a ~15 px sprite for 360 ms it could not be told from the control
  by eye; only a 12× magnified side-by-side makes it obvious
  (`WORKSPACE/medic-heal-flash-vs-control.png`). Its comment also records that the `Color`+`Alpha`
  path triggers `TintModifiers.ReplaceColor` — a **flat silhouette that reads as dying** — while
  `Brightness` multiplies and preserves shading. Directly load-bearing for any damage flash.
- **The critical pip blink rate is already a health readout** (`2054b7b0`, 2026-08-23), ramped
  linearly in the interval specifically so that *every part of its range reads*. This is the house
  precedent for **analog, non-numeric feedback carried on artwork**, and it is the strongest
  internal argument for Option B over Option A.

### 2.8 Other outcome signals

`ActorLostNotification` → `UnitLost` (`notifications.yaml:104`) — **death only, binary, own losses
only**. `LevelUpNotification` (`defaults.yaml:252`) — fires on the *attacker* gaining rank, an
indirect and heavily-lagged "your shots are working". Searched `RadarPing`: **no damage-triggered
ping**.

**Dead code worth knowing about:** `SelectionBarsAnnotationRenderable.cs:142-155` is a commented-out
**recent-damage delta bar** (orange-red). Someone already started building roughly this and stopped.

---

## 3. How damage is actually resolved

`DamageWarhead.InflictDamage` (`:220-257`), in order:

1. `damage = Damage`, then `RandomDamageAddition` / `Subtraction` / `RandomDamagePercentFrom`.
2. `thickness = victim.Trait<Armor>().Info.Thickness`. **If `thickness != 0`:**
   `armorPercent = ArmorDirectionPercent(...)`, then
   `damage = ApplyPenetration(damage, Penetration, thickness * armorPercent / 100)`.
3. `DamagePercent` adds a fraction of max HP.
4. `modifiedDamage = ApplyPercentageModifiers(damage, args.DamageModifiers + DamageVersus(...))`.

`ApplyPenetration` (`:127-133`) is `penetration >= thickness ? damage : damage * penetration / thickness`.
**`Penetration` defaults to 1** (`:24`).

`ArmorDirectionPercent` (`:141-218`) reads `Distribution` as
`{Front, Side, Rear, Top, Bottom}` — confirmed against `ArmorInfo.Distribution`'s own `[Desc]`
(`Armor.cs:26-27`) and against the code using `distribution[1]` for *both* left and right
(`:209-210`). `TopAttack` → `distribution[3]` (`:153`).

**The effective thickness is `Thickness × ArmorDirectionPercent / 100`, not `Thickness`.** That is
the fact that dissolves the first apparent bug below.

---

## 4. The distinct ways a shot under-performs — and whether the player can tell

| # | Mechanism | Where | Distinguishable in code at impact? | Player can tell today? |
|---|---|---|---|---|
| 1 | **Penetration below effective thickness** | `DamageWarhead.cs:240` | **Yes** — raw and post-penetration damage both in scope | **No** |
| 2 | **`Versus` armour-class multiplier** | `:96-109` | Yes — `DamageVersus` return is a labelled int | No |
| 3 | **Attacker-side firepower modifiers** | `Armament.cs:518` | Partly — arrives as an untagged int array in `args.DamageModifiers` | No |
| 4 | **Victim-side damage modifiers** (garrison cover, veterancy, prone, forest density) | `Health.cs:158-186` | **No — applied after the warhead**, invisible at `:256` | No |
| 5 | **Warhead falloff / proximity** | `SpreadDamageWarhead.cs:112`, `TargetDamageWarhead.cs:104` | Partly — appended anonymously to `DamageModifiers` | No |
| 6 | **Target-type mismatch — warhead discarded entirely** | `Warhead.cs:64-78`; `DamageWarhead.cs:59-60`, `:87-88` | Yes, but as a `return` with no signal | No |
| 7 | **Out of spread / falloff range** | `SpreadDamageWarhead.cs:102-107`, `TargetDamageWarhead.cs:86-91` | Yes | No |
| 8 | **A "miss"** | `Bullet.cs:201-222` | — see below | No |

**Four findings inside that table are worth stating outright.**

**(a) `DamageAtMaxRange` is inert on `SpreadDamage`.** `RangeDamageMultiplier` (`:135-139`) is
**never called from `InflictDamage`**; its only engine call site is `TargetDamageWarhead.cs:99`. So
range falloff is *not* one of the live failure modes for spread warheads, despite being set on
several (`weapons-ballistics.yaml:859`, `:456`). Already in `DISCOVERIES.md:1194` and
`conventions.md`.

**(b) There is no true "miss" for a bullet.** `Bullet` perturbs the *aim point*, not the trajectory
(`:201-222`), then flies a fixed parabola to that displaced point and **always detonates** —
`Explode` unconditionally calls `args.Weapon.Impact(...)` (`:388-404`). A "missed" shot still lands
and a `SpreadDamage` warhead still does falloff damage from wherever it landed. So "miss" and "hit
weakly" are *the same event* in this engine, which is part of why they are indistinguishable on
screen.

**(c) Missiles are the exception, and are already instrumented.** `MissileTrace.cs:36-56` enumerates
eleven termination causes (`Blocked, Ground, CloseEnough, FuelOut, OffMap, TerrainBound, Airburst,
SegmentClosest, JammedAps, Unterminated`) and separates `Detonated / DudPreArm / Unterminated`.
`Missile.ClassifyExplosion` (`:1229-1250`) already names which clause fired. `DudPreArm`
(`Missile.cs:1397`) is a genuine no-warhead case.

**(d) `Versus` is a much smaller factor than it looks.** Only **12 warheads in the whole mod** carry
a `Versus` table. And per `conventions.md`, an *omitted* class is **full damage**, not zero — so a
`Versus` table cannot silently zero a weapon by omission. The one all-zero table is
`IskanderTargeter` (`weapons-missiles.yaml:394-401`), a deliberate dummy trigger warhead.

### The crux for costing every option

**`INotifyDamage` cannot answer "why".** `AttackInfo`
(`engine/OpenRA.Game/Traits/TraitsInterfaces.cs:80-86`) carries **only** `Damage.Value` (final),
`Attacker`, `DamageState`, `PreviousDamageState`. A listener sees `-192` and cannot distinguish a
small weapon from a defeated one.

The intermediates live in exactly one scope: `DamageWarhead.InflictDamage`. At `:246` both
`Damage` (raw) and `modifiedDamage` (final) are live, along with `thickness`, `Penetration` and
`DamageVersus(...)`. **The ratio raw→final is the signal, and it is available at one site.**
One caveat: `armorPercent` is a local at `:239` that dies with the `if` block, so reporting
*effective* thickness means capturing it two lines earlier.

---

## 5. Two apparent bugs that dissolved on measurement

The brief predicted at least one. There were two.

### 5.1 The ATGM is correctly sized, not 7× under-penetrating

`ATGM` (the Javelin, `weapons-missiles.yaml:2-32`) carries `Penetration: 100` against an Abrams with
**700 mm**. That reads as a catastrophic under-penetration. It is not — `TopAttack: true` (`:6`):

| Target | `Thickness` | `Distribution[3]` (top) | Effective | Pen 100 ≥ ? | Delivered of `Damage: 10000` |
|---|---:|---:|---:|---|---:|
| M1 Abrams | 700 | 10 | **70** | yes | **10000** (full) |
| T-90 | 280 | 15 | **42** | yes | **10000** (full) |
| T-72 | 280 | 80 | **224** | no | 10000 × 100/224 = **4464** |

`Penetration: 100` clears the heavies' *roof* values (70, 42) with margin while staying throttled
against the flat `100,80,80,80,60` profile that 13 of 19 armoured actors carry. Correctly sized.

### 5.2 The ~109 `Penetration`-less damage warheads are mostly not bugs

Roughly 149 `SpreadDamage`/`TargetDamage` warheads exist across `rules/weapons/`; about 40 declare
`Penetration`. That looks like a systemic defect — every remaining warhead defaulting to 1 and being
divided by armour.

It dissolves on the `Thickness` default. `ArmorInfo.Thickness` defaults to **0** (`Armor.cs:24`),
and `InflictDamage` skips the whole penetration branch when `thickness == 0` (`:237`). **All
infantry set no `Thickness`** — `^Infantry` is `Type: None` (`infantry.yaml:36`), `^Soldier` is
`Type: Kevlar` (`:175`), neither sets a thickness. So an unset-`Penetration` warhead is perfectly
fine against infantry, which is what the great majority of those warheads shoot at.

This is pinned in-tree: `BallisticPenetrationTest.UnarmouredVictimsAreNeverDivided`, whose comment
says exactly this — *"why the defect only ever showed up on vehicles, and why the great majority of
the mod's Penetration-less SpreadDamage warheads are not bugs."*

**The motivating case, exactly.** Same test file:
`ApplyPenetration(54000, 1, 280) == 192`, against a T-90's 24000 HP. That is where "192 out of
24000" comes from — raw **54000**, `Penetration` unset, T-90 **280 mm**.

---

## 6. The options

Ranked. Full visual comparison in [`recon-battle-feedback/mockup.html`](recon-battle-feedback/mockup.html).

### Option A — Promote the damage number that already exists

**What the player sees.** `-4285 (15%)` floats off the tank on each hit, in the shooter's player
colour, for 30 ticks. On the motivating case, `-192 (0%)`.

**Build cost — the smallest of the four.** The rendering, the effect, the trait mount and the fog
suppression all ship today. The work is: split `debugVis.CombatGeometry` so damage text has its own
setting independent of hitshape wireframes; fix the integer truncation so 0.8% does not print as
`0%`; decide whether it is on for enemies, own units, or both. **Engine-only. No art, no sound, no
YAML.**

**What it gets wrong.** It tells you *what*, never *why*. `-192` does not say "the armour stopped
it" — the player must already know the penetration model to read it. It is also the option that most
turns a war game into a spreadsheet, and it is irreversible in a soft way: once players read exact
numbers they optimise against them, and the mod inherits an obligation to keep every published
number honest across every balance pass.

### Option B — Make *ineffectiveness* legible, with no arithmetic ★ **recommended**

**What the player sees.** When a shot loses most of its damage to armour: a **distinct impact** —
hard, grey, sparking, no fireball — plus a **ricochet sound**, plus a brief pale tint on the victim.
No numbers. The player learns "my RPGs bounce off Abrams frontally, so hit them from the side" by
observation, which is the thing you actually want them to learn.

**Build cost.** One ratio test at `DamageWarhead.cs:246`, where raw and final damage are both in
scope. Delivery reuses the shipped `FlashTarget` effect — `WithHealFlash` is the same shape and is
already mounted mod-wide, so this is a sibling trait rather than new machinery. **Needs one sound
and one small sprite**, which is the only genuinely new asset work in any option.

**What it gets wrong.** It needs a threshold, and a threshold is a tuning call with no obviously
right answer <span title="taste">**[TASTE]**</span> — "lost >50% to armour" and "lost >90%" give
very different games. It compresses six distinct failure modes ([§4](#4-the-distinct-ways-a-shot-under-performs--and-whether-the-player-can-tell))
into one signal: the player learns *that* the shot failed, not *which* mechanism failed it. And the
tint half carries a measured risk — the heal flash was at the threshold of perceptibility on a
15 px sprite. A tank is a bigger sprite, which is the case that matters here, but this needs eyes
before it is trusted.

### Option C — B always on, A behind a setting

**What the player sees.** The cue from B, always. Plus, if they turn it on, the number *with its
cause attached*: `-4285 · pen 500 / 700`. That string is what would have made the 192-damage bug
self-evident on sight.

**Build cost.** A + B, plus one settings entry, plus capturing `armorPercent` at `:239` so the
"why" clause can name effective thickness rather than raw thickness.

**What it gets wrong.** Most work of the four. Two surfaces to tune, and two to keep honest against
every future balance change. Also the honesty problem compounds: a string reading `pen 500 / 700`
is a *claim about the model*, and per the 2026-08-30 ruling already in the bank, **a surface showing
penetration must also show attack direction or it is actively misleading rather than merely
incomplete** — a top-attack weapon compared against frontal armour would read as a bug that is not
there.

### Option D — Fix the diagnosis path, not the game

**What the player sees.** Nothing. Promote `GunTrace` from `debug.log` to an in-game developer
overlay, so the person debugging a weapon sees the breakdown live.

**Build cost.** Very small — the strings are already formatted.

**What it gets wrong.** It does not answer the question asked. Listed because it is the honest
floor, and because it has a real claim on your time that the others do not: **the invisibility of
this failure has already cost developer-months, not just player confusion.** The littlebird
investigation is recorded in `DISCOVERIES.md:4201-4211` — its gun had a structurally zero
`FirepowerMultiplier`, and *"renders no differently at 0 than at 100: tracers, muzzle flash, impact
piffs and sound all play normally, so the gun looks like it is working right up to the health bar."*
Every previous investigation of that gun reasoned about the weapon — scatter, falloff, penetration,
warhead geometry — **and each premise measured out wrong in turn**, because the zero was on the
shooter.

---

## 7. The tension, argued

**The case for numbers (A).** WW3MOD is a simulation-flavoured mod with a real armour model —
penetration, directional distribution, thickness in millimetres. That model is *already* the thing
the game is about, and hiding it makes the game feel arbitrary rather than deep. A player who
cannot see that a hit did 0.8% cannot form the hypothesis "armour" at all; they form "this weapon is
broken" or "this game is random". Numbers are also the only option that is *unambiguous* — no
threshold to tune, no art to misjudge, no risk that the cue is too subtle to see.

**The case against.** Floating damage numbers are a genre marker, and the genre they mark is not
this one. They pull attention off the battlefield and onto a ticker; in a game where you command
twenty units, twenty simultaneous numbers is noise, not information. They also commit the mod to a
contract: every number shown must stay true, and the model behind it is exactly the one that has
already produced two *apparent* bugs that dissolved on measurement ([§5](#5-two-apparent-bugs-that-dissolved-on-measurement)).
Publishing `pen 500 / 700` without also publishing attack direction would have made the ATGM look
broken to every player, when it is correctly sized.

**Why B wins.** The thing the player needs is not the number — it is the *category*. "That bounced"
is actionable; "-192" requires them to reconstruct the armour model to become actionable. B teaches
the lesson the model exists to teach (flank the heavies, bring the right weapon) without publishing
a contract you then have to maintain. It is also the direction the project has already chosen twice:
the critical pip encodes health as **blink rate**, not as a number, and it was deliberately ramped so
that every part of its range reads. That is a house style, and B is consistent with it.

**Why not C by default.** C is strictly more capable and I would take it if this were free. It is
not: it doubles the tuning surface and it inherits the penetration-honesty obligation in full. B
first, C later if players ask for the arithmetic, is the cheaper ordering — and B is a strict
prefix of C, so nothing is wasted.

**Ranking: B > C > A > D.** Ship B.

---

## 8. Do not re-propose these

Recorded so this document does not become the thing it warns about.

- **Small-arms hits on aircraft draw nothing ON PURPOSE.** `^PiffEffects` / `^PiffsEffects`
  `Warhead@AirEffect` have no `Explosions:` line. It reads exactly like an accident and is not one —
  user ruling 2026-08-10: *"There is no good animation for hitting aircraft with small arms, that is
  why it is removed."* (`DISCOVERIES.md:5514`, and the `8f5dcbc3` commit message says it was
  deliberately not changed.) The tell that looks like corroborating evidence and is not: `Pistol`
  and `SilencedPPK` do spark on air — hand-written one-offs predating the decision.
- **The health bar is off by the user's own choice.** See [§9](#9-the-separate-product-question).
- **`DamageAtMaxRange` on a `SpreadDamage` warhead is inert.** Do not "fix" a weapon by tuning it.

---

## 9. The separate product question

**None of the four options restores *accumulated* damage legibility.** They all address the
per-shot event. The reason a player cannot see a tank wearing down is [§2.3](#23-there-is-no-health-bar-this-is-the-largest-single-finding):
four pip bands at 75/50/25%, so 31 consecutive hits can change nothing on screen.

Re-enabling the health bar is the largest single legibility gain available in this whole area, and
it is one line. **It is also explicitly not mine to take** — the engine file asks that it not be
done without asking, and the user switched it off himself. Flagging it as a question:

> Should the health bar come back — either fully, or as the commented-out **recent-damage delta
> bar** at `SelectionBarsAnnotationRenderable.cs:142-155`, which shows only what a unit has just
> lost and would pair unusually well with Option B?

I have not costed this as an option because the answer determines the shape of the work, not the
other way round.

---

## 10. New facts filed elsewhere

- `WORKSPACE/DISCOVERIES.md` — dated 2026-08-30 entry: the feedback machinery census, the
  `AttackInfo` payload limitation, and the `Thickness == 0` dissolution.
- `WORKSPACE/bugs/discovered.md` — two small genuine defects found in passing (below).

**Two real defects, both tiny, neither central to this proposal:**

1. `IskanderTargeter`'s zeroing `Versus` (`weapons-missiles.yaml:394-401`) omits **`Kevlar`** and
   **`Unarmored`** and includes a non-existent **`Brick`**. Per the omission rule, the dummy trigger
   warhead therefore delivers its full `Damage: 50` to any soldier at the aim point. Near-unobservable
   — `TargetDamageWarhead.Spread` defaults to `WDist(1)` — but real, and a one-line fix.
2. `Bullet.cs:206-209` — `if (info.MinInaccuracy != WDist.Zero) maxInaccuracyOffset = info.MinInaccuracy.Length;`
   **overwrites** rather than taking `Math.Max`, contradicting its own `[Desc]` *"The minimum
   inaccuracy regardless of distance to target"* (`:33-34`). It is a *fixed* inaccuracy, not a floor.
   One weapon uses it (`weapons-ballistics.yaml:217`).

---

## 11. What I could not determine

Stated so nothing here is banked as measured that was not.

1. **Whether a tint flash on a tank sprite reads.** The only measurement in the tree is the heal
   flash on a ~15 px infantry sprite, where it did not. A tank is roughly 2–3× that. **This is the
   one thing I would verify before committing to Option B's tint half** — the spark and the sound
   do not carry the same risk.
2. **Whether 20 simultaneous floating numbers is unusable**, which is the core objection to A. That
   is a play-feel judgement no static analysis reaches.
3. **The right threshold for "ineffective"** in Option B. It needs combat-sim or play, not reading.
4. **Whether players would read a grey spark as "bounced"** without being told, or just as a
   different explosion. That is an art-legibility question.
5. I did **not** enumerate every attacker-side `IFirepowerModifier`, only the victim-side
   `IDamageModifier` set. `FirepowerMultiplier` reaching 0 is a known real case
   (`DISCOVERIES.md:4201`) and is failure mode #3 in [§4](#4-the-distinct-ways-a-shot-under-performs--and-whether-the-player-can-tell);
   I have not proved the list is complete.
6. No build, no launch, no autotest, no `make test` — per the brief.
