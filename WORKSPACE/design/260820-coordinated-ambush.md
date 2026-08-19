# Coordinated ambush — design and costing

**Date:** 2026-08-20 · **Branch:** `wt/ambush-coordination` · **Base:** `main @ 4bb3fae9`
**Status:** RESEARCH ONLY. No behaviour changes in this branch.

---

## 0. Verdict

**The feature the user asked for is already built, shipped, and reachable by human players today.** It is not
unimplemented and it was not abandoned. `AutoTarget` pre-aims while holding fire, latches on being spotted, and
broadcasts that latch to every Ambush-stance ally within 10 cells. The Ambush button's tooltip
(`mods/ww3mod/chrome/ingame-player.yaml:373`) already promises the user's requirement close to verbatim:

> *"Units pre-aim at targets but hold fire until spotted / When one unit is spotted, nearby allies in Ambush all
> engage / Zero aim delay — turrets are already aimed when firing begins"*

So the question is not "how do we build this" but **"why can't the user see it."** This document answers that.
There are five causes: **two are real mechanical defects** and the other three are legibility and doctrine.

**Both defects are places where the tooltip above promises something the code does not do.**

**Defect A — the coordinated volley is not simultaneous.** `TriggerNearbyAmbushAllies` sets a latch on each ally
but never makes any of them shoot. Each ally fires on *its own next scan*, and WW3MOD infantry scan on a
`SharedRandom` interval of **16–32 ticks** at a 60 ms timestep — so the volley is smeared randomly across
**0.96–1.92 seconds**. That reads as soldiers noticing an enemy one at a time, indistinguishable from no
coordination at all.

**Defect B — "zero aim delay" is not true; the aim delay is paid in full.** WW3MOD adds
`Armament.AimingDelay` (default **15 ticks**, overridden to **30–50 ticks on vehicles**). It is reset to full
whenever the armament sees a new target, and pre-aiming **never touches it** — `PreAimAtTarget` only rotates
facing; it never calls `CheckFire`, which is the only thing that would start the timer. So an ambusher pays the
entire aim delay *after* springing. For an MBT that is 50 ticks — **3.0 seconds of standing there not shooting**,
after the ambush has already been given away.

Together these mean the shipped ambush is slower to open fire than the player expects, and ragged when it does.
That is a complete explanation of "I haven't seen it clearly in game" without needing any of the softer causes.

---

## 1. What exists today

All line numbers are `main @ 4bb3fae9`.

| Piece | Where | State |
|---|---|---|
| Fire stance enum `HoldFire / Ambush / FireAtWill` | `AutoTarget.cs:22` | shipped |
| Ambush button + hotkey `StanceAmbush` | `chrome/ingame-player.yaml:361-373` | shipped |
| Pre-aim while holding fire | `AutoTarget.PreAimAtTarget:956-974` | shipped |
| Spotted trigger | `AutoTarget.cs:757` `self.CanBeViewedByPlayer(targetOwner)` | shipped |
| Group broadcast, 10-cell radius | `AutoTarget.TriggerNearbyAmbushAllies:976-993` | shipped |
| Damage retaliation springs the group | `AutoTarget.cs:673-678` | shipped |
| Garrisoned buildings join the spring | `GarrisonManager.TriggerAmbush()` | shipped |
| Stage-3 five-trigger state machine | `AutoTarget.AmbushTickIdle:702-793` | shipped, **bot-only** |
| Stage-2 halt-before-contact | `AttackMoveActivity` | shipped, **bot-only** |
| Bot lane poster | `LaneAmbushBotModule.cs` | shipped, **both profiles** |
| Non-default stance glyph (amber "A") | `Render/WithStanceDecoration.cs` | shipped, render-only |

Curated reference: `DOCS/reference/architecture.md` §"Widened ambush (Stages 1–4)".

### The human/bot split matters

`AmbushTacticsCondition: enable-ambush-tactics` is granted **only** by `LaneAmbushBotModule`, and only to units
that module posts. Humans, and the Normal/Rush/Turtle profiles, never instantiate it. So:

- A human clicking Ambush gets the **ungated stock path** — trigger 1 (spotted) and trigger 2 (damaged), nothing else.
- The entire Stage-3 sophistication (worthwhile score, saturation, overrun, predicted range exit) and the Stage-2
  halt-before-contact are **unreachable by any human-controlled unit**. There is no UI path to the gate.

The user is a human clicking a button. He gets the simplest version of the feature that exists.

---

## 2. The earlier attempt — it landed, it did not stop

The user's memory is correct, and better than correct: there were **two** landings.

**Layer 1 — `fea4617c` "Rewrite Ambush stance: detection-aware with group coordination."** Its commit message is
the requested feature almost word for word, including "group coordination: when one unit is spotted, nearby
allies in Ambush within 10 cells also engage immediately" and "zero aim delay since turrets are already tracking".

**Layer 2 — PIPELINE item 8, "Ambush behavior", Stages 1–4, all shipped 2026-07-25**
(`WORKSPACE/pipeline/archive/closed-items.md:209-212`). Stage 2 `3ddd0b40`, Stage 3 `d7549f83`, Stage 4 `2b6c6166`.
The design doc `WORKSPACE/plans/260722_ambush_undetected_design.md` §3.4 states plainly:

> *"**[HOLDS]** 'One spotted member springs the whole group.' Already true via `TriggerNearbyAmbushAllies` … the
> coordination primitive is done."*

**Nothing about the coordination feature is parked.** The only open gate is (b), `@experimental` benchmark pricing
before turning the *bot* posture on by default — transitively blocked behind pipeline items 40 → 43. It does not
block human use.

The one genuinely parked item is a *test bar*, `WORKSPACE/AWAITING-USER.md` §4 item 22: "The forest-ambush test
finally has real pass/fail numbers. Ratify them?"

### The recon that says "ambush is not how you do it"

`f6ff7fd1` / `WORKSPACE/recon/260819-infantry-visibility-stances.md` is the closest thing to a negative verdict,
and it is a **concealment** verdict, not a coordination one. Its findings, verified:

- **No stance touches detectability.** `stance-ambush` is granted at four sites in `defaults.yaml` and has zero
  consumers. Choosing Ambush does not make a unit harder to see.
- **Ambush units are not auto-repositioned into cover.** `StancePositioningExecutor.FireStanceAllowsRepositioning`
  returns `stance >= FireAtWill` (`:588-590`).
- **Nothing on screen ever says "holding fire."**

⚠️ **Correction to that recon's framing.** It reads the repositioning rule as Ambush "switching OFF the game's
only automatic take-cover behaviour," implying a bug. The code comment at `StancePositioningExecutor.cs:583-586`
says the opposite and says it deliberately: the gate is the fix for *"the un-ambush bug"* — the auto-positioner
was dragging ambushers out of the hiding place the player chose. **Ambush placement is intentionally manual.**
The gap is not that the rule exists; it is that nothing tells the player placement is now his job.

---

## 3. Why the user cannot see it — five causes, ranked

### Cause 1 — the volley is ragged (real mechanical defect, cheap to fix)

`TriggerNearbyAmbushAllies:982-987` does this and only this:

```csharp
if (allyAutoTarget != null && allyAutoTarget.Stance == UnitStance.Ambush && !allyAutoTarget.ambushTriggered)
    allyAutoTarget.ambushTriggered = true;
```

It sets a flag. It does not call `Attack`. The ally fires only when its own `INotifyIdle.TickIdle` next reaches
`AmbushTickIdle` **and** `ScanForTarget` actually scans — which is gated on `nextScanTime`, re-armed from
`self.World.SharedRandom.Next(Min, Max)` (`AutoTarget.cs:1157`).

Measured values for the user's own scenario:

| Quantity | Value | Source |
|---|---|---|
| Infantry scan interval | **16–32 ticks**, random per unit | `infantry.yaml:289-290` on `^CamoSoldier` |
| Rifleman inherits it? | **yes** — `^E3` → `^CamoSoldier` | `infantry.yaml:1108-1109` |
| Timestep at default speed | **60 ms** (16.67 ticks/s) | `mod.yaml:358, 382` |
| ⇒ volley spread | **0.96 – 1.92 s** | derived |

> ⚠️ A prior read of this claimed 3–8 ticks and concluded "effectively simultaneous." Those are the *engine
> defaults* at `AutoTarget.cs:199-202`; WW3MOD overrides them for infantry. Do not quote 3–8 for this mod.

**A volley that arrives over one to two seconds is not perceptible as a volley.** This alone is sufficient to
explain the user's report, and it is the only cause here that is a defect rather than a design position.

### Cause 2 — the spring then pays the full aim delay (real defect, see §8)

Even once a unit decides to fire, `Armament.AimingDelay` charges **15 ticks on infantry and 40–50 on vehicles**,
because pre-aiming rotates the turret but never warms the armament's aim timer. Full mechanism and tick budget
in §8. This stacks on top of cause 1 rather than overlapping with it.

### Cause 3 — the ambushers are not hidden, so the spring is not dramatic

The trigger is *being seen*, and being seen is free and early: sight range generally exceeds the range at which
the player would want to spring. Nothing about Ambush stance conceals a unit. So the group tends to spring at
long range the moment the enemy's vision touches the first of them — never the "column in the middle of them"
moment the user is picturing. The volley fires, correctly, at a moment that feels arbitrary.

The one mechanism that *does* conceal — `RefineSlotsForConcealment` (`CohesionMoveModifier.cs:1079, 1182-1183`),
order-time re-seating onto the most tree-dense cell — is real but undiscoverable. It requires: human player +
Ambush stance + cohesion Loose/Spread + multi-select + a move order near woodland. Nothing in the game says so.

### Cause 4 — humans cannot halt before contact

Stage 2 would stop an advancing group *before* it walks into the enemy's vision, which is the setup step for a
dramatic ambush. It is gated behind `enable-ambush-tactics`, which only `LaneAmbushBotModule` grants. There is
no human path, and it only ever applies to attack-move — a plain Move is always obeyed.

### Cause 5 — no feedback that the state is even active

There is an amber "A" glyph for non-default fire stance (`WithStanceDecoration`), which is more than nothing.
But there is no distinct signal for the three states that matter: *armed and holding*, *sprung*, *concealed vs
exposed*. The user cannot tell a working ambush from a broken one.

---

## 4. Sub-problem 1 — holding fire without looking like a bug

The failure mode to design against is a player watching soldiers ignore a visible enemy and concluding they are
broken. Today the game gives him one 8-pixel amber letter.

The mitigation is **legibility, not behaviour**, and the recon already recommends it (§8): a distinct hold-fire
pip on a unit that is *armed, has a target, and is deliberately not firing*. That state is already computed and
already sitting in a field — `ambushPreAimTarget` is non-`Invalid` exactly when the unit is pre-aiming and
holding. A render-only decoration keyed off `ambushPreAimTarget.Type != TargetType.Invalid && !ambushTriggered`
would say "this soldier has him in his sights and is waiting" with no simulation change whatsoever.

Render-only is the important word. `WithStanceDecoration`'s own header states the rule this codebase follows:
read the trait's public synced state, write nothing, and key off the **trait rather than a granted condition** —
with a pointer to the PITFALL at `Detectable.cs:152`. Any new indicator must obey that, or it re-opens the
condition-token desync class described in §6.

**Cost:** one `WithDecorationBase` subclass plus a YAML block. No sim change, no sync surface, no bot impact.

---

## 5. Sub-problem 2 — pre-acquiring targets while holding

**Already done, and correctly.** `PreAimAtTarget:956-974` rotates turrets via `Turreted.FaceTarget` and, for
non-turreted infantry, steps body facing toward the target with `Util.TickFacing(..., facing.TurnSpeed)` each
idle tick. `ambushPreAimTarget` caches the acquired target across scans.

There is one real subtlety worth recording. On the **ungated (human) path**, when a scan returns no target the
code clears `ambushPreAimTarget` and also clears `ambushTriggered` (`:746`). Only the gated path keeps SPRUNG
terminal. So a human ambusher whose target briefly drops out of the scan **silently re-arms**. That is defensible
as a design, but it means the human ambush has no memory, which will interact with §7's hunt phase — see the
exit-predicate discussion there.

---

## 6. Sub-problem 3 — the trigger, and the determinism story

### The group identity is spatial, and that is the right answer

There is no roster to replicate. `TriggerNearbyAmbushAllies` recomputes membership per call from
`FindActorsInCircle` + `Stance == Ambush` + same owner. Player control groups **cannot** be used:
`Traits/World/ControlGroups.cs:29` is a `SystemActors.World` trait filtered by `world.LocalPlayer`, carries no
`[Sync]`, and is invisible to the simulation. A control group is client state and can never be a sim-legal group.

### Why the existing broadcast is already deterministic

Three independent properties, all of which a change must preserve:

1. **The write is commutative and idempotent.** Every iteration does `x = true` on a distinct actor. Iteration
   order of `FindActorsInCircle` therefore cannot affect the outcome. This is the property that makes the whole
   thing safe, and it is the property most easily lost by "improving" the loop.
2. **The visibility read is sim-legal.** `CanBeViewedByPlayer` draws no RNG and is authoritative simulation
   state, not a render concern. Actor iteration is structurally ordered anyway — `World.actors` is a
   `SortedDictionary` (`World.cs:32`) and `TraitDictionary` keeps lists insertion-sorted by ActorID.
3. **`ambushTriggered` is deliberately not `[Sync]`** (`AutoTarget.cs:396`, rationale `:398-403`): it evolves by
   pure integer/bool math over already-synced state with zero RNG, so it stays in lockstep without contributing
   to the hash.

### The scar to respect

The 2026-08-12 → 08-16 savegame desync was **on this exact mechanism**, and its lesson is the opposite of the
intuitive one:

- `fffad21e` blamed the **visibility** predicate. `d6782fa6` **refuted that** — visibility agreed on both lives.
- The real cause was `LaneAmbushBotModule` calling `ec.GrantCondition(...)` **directly from a bot tick**.
  `GameSave` records orders, so nothing recorded the grant; replay suppresses bot ticks, so the condition count
  stayed 0 on restore. Fixed in `61546a51` by routing it as `Order("SetAmbushGate", …)`.

**Rule for any extension: mutate synced/condition state only through an order** (copy `SetAmbushGate`,
`AutoTarget.cs:583-584`). Never `[Sync]` a condition token — that was the separate `Detectable` desync
`e1bbf244`, with a live PITFALL at `AutoTarget.cs:590-593`.

### The fix for the ragged volley, and the trap in it

The obvious fix is "make the ally scan immediately." **The obvious implementation of that is a desync.**
`AutoTarget.cs:1024-1027` already records why, in a comment written for the target-preemption feature:

> *"ChooseTarget is called DIRECTLY rather than through ScanForTarget — the latter re-arms `nextScanTime` off
> `SharedRandom`, which would shift the shared RNG stream (breaking byte-identity, see influence-stack.md) and
> starve the existing scanners."*

So forcing a scan via `ScanForTarget`, or zeroing `nextScanTime` and letting the normal path re-arm it, pulls an
extra draw from `SharedRandom` **for every triggered ally**, shifting every subsequent combat roll in the match.
That breaks the frozen `@stable` A/B baseline's RNG-stream byte-identity.

**The codebase has already solved this exact problem once.** `TickPreemption` calls `ChooseTarget` directly and
does not re-arm the timer. The ambush trigger should copy that pattern verbatim: on receiving the broadcast, an
ally calls `ChooseTarget` directly and, if it yields a target, attacks in the same tick — without touching
`nextScanTime` and without drawing from `SharedRandom`.

That makes the volley genuinely simultaneous (all within the triggering tick) at zero RNG cost. It is the single
highest-value change identified in this document, and it is small.

**Ordering caution:** if allies attack *inside* the broadcast loop, actor A's shot could kill a target before
actor B evaluates, making the outcome order-dependent. Keep the current two-phase shape — phase 1 sets all
latches (commutative), phase 2 lets each unit act on its own tick pass — or explicitly `OrderBy(ActorID)` before
any acting phase, which is the convention Stage-3's kill-zone scan already uses (`:782-783`).

---

## 7. NEW — phase 3: post-trigger hunt (user ruling 1)

The user's model has three phases, not two:

1. **Armed** — hidden, holding fire, targets pre-acquired.
2. **Sprung with a shot** — fires immediately.
3. **Sprung without a shot** — *"may switch to a temporary 'hunt' mode (the stance remains the same, but the
   soldier behaves like hunt while the ambush is active)."*

### The engine already has the right axis, and it is orthogonal

`AutoTarget` carries **two independent stance axes**:

- `UnitStance { HoldFire, Ambush, FireAtWill }` — the *fire* axis, what the Ambush button sets.
- `EngagementStance { HoldPosition, Defensive, Hunt }` — the *engagement* axis (`AutoTarget.cs:24`).

This maps onto the user's sentence exactly: **the stance does not change (fire axis stays `Ambush`) while the
behaviour becomes hunt (engagement axis)**. No new concept is required.

And "hunt" has a precise, small meaning here. `EngagementStance` is consumed at exactly three sites, all
identical:

```csharp
var allowMove = allowMovement && engagementStance >= EngagementStance.Hunt;   // :658, :697, :1057
```

**Hunt means: permitted to move toward a target.** That is the whole semantic. It is a good fit for "the ones
who can't shoot go looking for a shot."

### Do NOT implement it by mutating the engagement stance

The tempting implementation — call `SetEngagementStance(self, Hunt)` on spring and restore it later — is the
wrong one, for three reasons:

1. `SetEngagementStance` is a **synced simulation mutation** with `[Sync] int SyncEngagementStance` (`:362`). It
   fires `INotifyEngagementStanceChanged`, applies conditions, and is read by the stance decoration UI. The
   player's engagement glyph would flicker on every ambush.
2. It requires a **restore step**, and a missed restore is precisely the user's stated fear: *"leaves the whole
   squad permanently hunting, which is a squad that never hides again."*
3. It would overwrite a deliberate player choice — a player who set HoldPosition would silently lose it.

**Recommended shape: derive it, don't store it.** Widen the three `allowMove` sites to:

```csharp
var allowMove = allowMovement && (engagementStance >= EngagementStance.Hunt || AmbushHunting);
```

where `AmbushHunting` is a computed property, not a field:

```
AmbushHunting => Stance == UnitStance.Ambush && ambushTriggered && <engagement still active>
```

There is **no state to unwind**. When the predicate goes false the unit stops hunting by construction, which
removes the "permanently hunting" failure mode entirely rather than mitigating it. It also leaves the player's
engagement stance untouched and the sync hash unchanged.

### The exit predicate — this is the hard part, and it is genuinely fragile

The user's condition is *"while we are still visible and fighting."* That needs to become integer math with zero
RNG. There is **no existing "last fired" counter on `AutoTarget`** — it does not implement `INotifyAttack`. So
this needs a new tick stamp. Two existing call sites can refresh it without new plumbing:

- `AmbushTickIdle` when the scan yields a valid target — *we can see an enemy*.
- `INotifyDamage.Damaged` (`:631`) — *we are being shot at*.

Then `AmbushHunting` adds `world.WorldTick - ambushLastContactTick < AmbushHuntGraceTicks`.

**Three problems the user should see before anyone builds this:**

1. **A hunting unit is not idle, so it stops refreshing its own stamp.** `AmbushTickIdle` only runs via
   `INotifyIdle.TickIdle`. A unit that moves to hunt has a current activity and will not re-stamp from scanning
   until that activity ends. If the grace window is shorter than a chase leg, the unit oscillates in and out of
   hunting. The grace window must exceed the longest chase leg, which makes it a **tuning constant that must be
   measured, not guessed**.
2. **It interacts badly with the human re-arm noted in §5.** On the ungated path `ambushTriggered` is cleared
   whenever a scan finds no target (`:746`). So for a human, "lost the target" already resets SPRUNG — which
   would end hunting immediately and defeat the whole phase. Making phase 3 work for humans probably requires
   adopting the gated path's terminal-SPRUNG rule, which is itself a behaviour change to the human ambush.
3. **The grace timer is a new per-unit field.** Keep it non-`[Sync]` with the same written rationale as
   `ambushTriggered` (pure integer math over synced state, zero RNG), or it enters the hash and the two-bools-XOR
   cancellation trap (`architecture.md` §"A trait's `[Sync]` members are XORed") becomes live.

**Alternative worth pricing against it:** define "ambush active" as *any Ambush-stance unit within the same
10-cell coordination radius currently has a valid target*. This reuses the existing spatial group and needs no
timer at all, so there is no constant to tune and no oscillation. It costs a radius scan per evaluation and it is
less faithful to "still fighting" — but it cannot strand a squad in permanent hunt, because the moment the group
collectively has no target, hunting ends. **Recommend prototyping this one first** on the grounds that it has no
tunable and no unwind path.

---

## 8. Sub-problem 4 — aim delay (user ruling 2: establish whether one exists)

**Answer: yes, a substantial one exists today — and ambushers do NOT skip it, contrary to the tooltip.**

The user's ruling was *"only if one exists today."* It does. `Armament.AimingDelay` is a **WW3MOD addition**, not
stock OpenRA:

```csharp
[Desc("How long time unit needs after acquiring the target (turret facing) to aim, before being able to fire")]
public readonly int AimingDelay = 15;          // Armament.cs:44-45
```

It gates firing directly — `CanFire` returns false while `IsAiming` (`Armament.cs:327`, `IsAiming => AimingDelay > 0`).

**Values actually shipped:**

| Unit class | `AimingDelay` | at 60 ms/tick | Source |
|---|---|---|---|
| Infantry (rifleman `^E3`) | **15** (engine default, no override) | 0.90 s | `Armament.cs:45` |
| Vehicles — light | **30** | 1.80 s | `vehicles-russia.yaml:576`, `:702` |
| Vehicles — medium | **35** | 2.10 s | `vehicles-america.yaml:635` |
| Vehicles — heavy / MBT | **40 – 50** | 2.40 – **3.00 s** | `vehicles-russia.yaml:216`, `:963` |

**Why pre-aiming does not warm it — this is the mechanism, and it is a one-line cause.** The timer is reset only
inside `Armament.CheckFire` (`:345-354`):

```csharp
if (!target.Equals(oldTarget)) { oldTarget = target; AimingDelay = Info.AimingDelay; ... }
```

`CheckFire` is reachable only via `AttackBase.DoAttack`. `PreAimAtTarget` (`:956-974`) touches **only**
`Turreted.FaceTarget` and `facing.Facing` — it never reaches an armament at all. So `oldTarget` is still stale
when the ambush springs, the comparison fails, and the full delay is charged *at the moment the shooting is
supposed to start*.

So the tooltip line **"Zero aim delay — turrets are already aimed when firing begins"** is false. The turret is
aimed; the *armament* is not, and the armament is what gates the shot.

### Full time-to-first-shot budget

| Cause | Rifleman | MBT | Warm after pre-aim? |
|---|---|---|---|
| Scan latency (§3) | 0–32 | 0–32 | ✗ |
| Activity queue → first tick | ~1 | ~1 | ✗ |
| Body/turret turn into `FacingTolerance: 50` @ `TurnSpeed: 100` | 0–5 | 0–5 | ✓ **yes** |
| **`AimingDelay`** | **15** | **40–50** | ✗ **no — this is defect B** |
| `FireDelay` (trigger → projectile) | 3 | 3 | ✗ |
| **Total** | **≈19–56 ticks (1.1–3.4 s)** | **≈44–91 ticks (2.6–5.5 s)** | |

Pre-aiming currently buys back only the 0–5 turn ticks. It leaves 15–50 ticks on the table.

### What the ruling licenses

The user said ambushers skip the delay *if one exists*. One exists, so this is in scope. The cheapest honest
shape is to make the spring path zero it — either `AimingDelay = 0` on spring, or seed `oldTarget` during
pre-aim so the reset never fires (which is arguably the more truthful model: the soldier really has been holding
that sight picture). `AimingDelay` is `public ... { get; protected set; }` (`Armament.cs:164`), so it is not
externally settable today; this needs a small new API on `Armament` plus one call site, not a new subsystem.

⚠️ **Two cautions before anyone implements it.** (1) This is a **balance change, not just a feel change** — an
ambushing MBT that skips 50 ticks gains three free seconds of fire. Price it in the combat sim
(`tools/combat-sim/`, see `DOCS/recipes/BALANCE.md`) before shipping. (2) `WithAimAnimation` /
`WithTurretAimAnimation` read `AttackBase.IsAiming`, **not** the armament's, so the aim animation will not follow
automatically and may visibly desynchronise from the new firing behaviour.

### The comparison that matters

Defect A (scan latency, 16–32 ticks) and defect B (aim delay, 15–50 ticks) are **the same order of magnitude**,
and they **stack**. Fixing only one leaves roughly half the perceived lag in place. For infantry the two are
comparable; for vehicles the aim delay dominates. Both should be fixed together or the result will still not
read as an ambush.

---

## 9. Bot impact and `@stable`

**Bots already use these stances**, via correctly-ordered `bot.QueueOrder`: `LaneAmbushBotModule.cs:497` sets
Ambush; `PoiOffensiveBotModule.cs:3531` sets HoldFire.

**`@stable` is not frozen and already runs the ambush module.** `b8d2e601` (2026-08-02) promoted
`LaneAmbushBotModule@stable` to full parity (`ai.yaml:2218`, `MaxAmbushes: 2 / UnitsPerAmbush: 2`). Per
`CLAUDE.md`, deliberate visible improvement flowing to `@stable` is fine; silent drift is not.

**Blast radius of a coordination fix is small and structurally capped:**

- The gate is granted per-unit, only by that module, only to its own postings — **at most 4 units per profile**.
- `^AutoTargetGround*` (AA IFVs, assault-move vehicles) **cannot** host an ambush: that template is a separate
  base from `^AutoTarget` and declares no `AmbushTacticsCondition` and no grantable seam
  (`defaults.yaml:553` vs `:305`). `CanHostAmbush` tests exactly that, so it excludes the family structurally.

So "bots hold fire and lose fights" is bounded at 4 units, not the army. **However:** the §6 broadcast fix is on
the *ungated stock path*, which every Ambush-stance unit uses — so it reaches `@stable`'s ambushers too. That is
allowed, but it **is** a `@stable` behaviour change and must be said in the commit message so the next benchmark
baseline is re-taken knowingly.

**Re-baselining cost:** River Zeta rung, 3 scenarios, `@experimental` vs `@stable`, N=10, seeds 1017…10017 with
mirrors, ~1–2 wall-min per hidden match (`WORKSPACE/ai-bench/LADDER.md:292-310`). Last full re-baseline was 40
measured + 20 calibration matches. Note that `61546a51` already flagged a `@stable` behaviour change and said to
re-take the baseline — **a re-take may already be owed before any new work here.**

---

## 10. Recommendation, ordered by value per unit of risk

| # | Change | Risk | Why |
|---|---|---|---|
| 0 | **Run `test-case01b-detect`** — authored to measure fire-lane and time-to-first-shot, never run once. | None | Directly measures defects A and B before any code is written. Cheapest unclaimed number in the project. Do this first. |
| 1 | **Make the broadcast fire in-tick** via the `ChooseTarget`-direct pattern from `TickPreemption`. No `SharedRandom` draw, no `nextScanTime` touch. | Low | Defect A. Turns a 1–2 s smear into an actual volley. |
| 2 | **Let the spring skip `AimingDelay`** (§8) — new setter on `Armament` + one call site. | Medium | Defect B. Worth 15 ticks on infantry, 40–50 on vehicles. **Balance-price it first.** |
| 3 | **Hold-fire pip**, render-only, keyed off `ambushPreAimTarget` + `!ambushTriggered`. | Very low | Makes "holding fire" legible; kills the "my units are broken" reading. No sim change. |
| 4 | **Fix the tooltip** either way. | None | It currently promises behaviour the code does not deliver. Whatever is decided above, the copy must match. |
| 5 | **Phase-3 hunt**, group-has-target variant (§7), prototyped before the timer variant. | Medium | New behaviour; needs the §7 caveats resolved. |
| 6 | Surface concealment / `RefineSlotsForConcealment` | Medium | Addresses cause 3, but it is a doctrine and UX project, not a fix. |

Items 1 and 2 **should ship together** — they are the same order of magnitude and they stack, so fixing either
alone leaves roughly half the perceived lag in place (§8).

**Explicitly not recommended:** rebuilding the coordination primitive (it exists), and *adding* an aim delay —
the user ruled that out, and the finding is the opposite anyway: one already exists and is not being skipped.

---

## 11. What I did not verify

- **I did not run anything.** No game launch, no autotest, no `make test`, no YAML validation — per the brief.
  Every number here is read from source or YAML, not measured at runtime.
- **The 0.96–1.92 s volley spread is derived, not observed.** It follows from `MinimumScanTimeInterval: 16` /
  `Maximum: 32` on `^CamoSoldier`, `^E3`'s inheritance of it, and `Timestep: 60`. I did not confirm that
  `^E3`'s later `Inherits@AutoTarget: ^AutoTargetLMGAT` leaves those two fields alone — I confirmed only that no
  file under `mods/ww3mod/rules/` other than `infantry.yaml:289-290` sets either field, which is strong but is
  not the same as tracing the merge. **`test-case01b-detect` would settle it directly and should be run before
  anyone writes code against this claim.**
- **I did not verify that `PreAimAtTarget`'s direct `facing.Facing` write actually survives** for infantry with
  `WithInfantryBody` and `AlignBodyToTarget: true`. If some other trait re-drives facing on the same tick, even
  the 0–5 turn ticks that pre-aim is supposed to buy back may not be real — which would mean pre-aiming
  currently buys **nothing at all**. One targeted check would settle it.
- **I nearly published the wrong aim-delay verdict.** My first pass checked only `Armament.FireDelay` (3 ticks)
  and concluded the delay was negligible and already skipped. `Armament.AimingDelay` — 15 ticks on infantry,
  up to 50 on vehicles — is the dominant term and I missed it on the first read. It is recorded here because the
  same mistake is easy to repeat: `FireDelay` is the *obvious* field name and it is the wrong one.
- **The `AimingDelay` skip is unpriced.** I have not run the combat sim. Three free seconds of fire for an
  ambushing MBT is a real balance delta, not a cosmetic one, and §8's recommendation should not be implemented
  on the strength of this document alone.
- **`TriggerNearbyAmbushAllies` has no NUnit coverage.** `AmbushTacticsTest.cs` (26 cases) and
  `AmbushLaneMathTest.cs` (13) pin the pure trigger and lane math only. The world-touching broadcast — the thing
  this document proposes changing — is untested. Any change to it should arrive with a RED-first test.
- **The case-01 forest-ambush scenario is 22 days stale** (last real measurement 2026-07-28, ~1000 commits ago)
  and was a false green until `e14dced3` gave it teeth; it has never been RED/GREEN certified since. It also does
  not test coordination at all — it measures cost-weighted losses, never simultaneity. Do not cite it as
  evidence that coordination works.
- **The garrison path in the broadcast looks wrong and I did not chase it.** `TriggerNearbyAmbushAllies:989-991`
  calls `gm.TriggerAmbush()` on every nearby friendly actor with a `GarrisonManager`, with **no stance check** —
  unlike the `AutoTarget` path directly above it, which requires `Stance == UnitStance.Ambush`. That reads as a
  latent bug (garrisons springing when they were never in Ambush), but it is out of scope here and I have not
  confirmed what `TriggerAmbush` does internally.
