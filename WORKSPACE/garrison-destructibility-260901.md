# Garrison destructibility — the rubble model is already shipped; here is what is actually broken

**Date:** 2026-09-01 · **Branch:** `wt/garrison-design` · **Base:** `main @ 3dd67e07`
**Status:** design + audit. **No behaviour changed by this branch.** Every proposal below is
reserved for the user.

Supersedes nothing. `WORKSPACE/garrison-proposals.md` (2026-08-16) is a different question —
suppression and port legibility — and is still current on its own subject.

---

## The headline

**The user's mental model is not a change request. It is a description of what the code already
does.** He wrote:

> "in reality a building is rarely leveled with the ground, there is always rubble. So the fully
> damaged state is the 'destroyed' state of a building, where it is just a rubble."

That is, almost line for line, the shipped implementation. A garrisonable building cannot die; it
degrades to a terminal 1 HP state, keeps its damaged sprite, and becomes progressively more
dangerous to sit in. Three independent parts of the tree already say "rubble" in those words.

So the pre-release question is **not** "how do we build the rubble model". It is "the rubble model
is here, in what three ways is it not working". That reframing is the whole value of this document,
and it is what makes the work cheap.

---

## What the engine actually does today

### F1 — Damage is already capped, and the cap is already the rubble state

`GarrisonManager` implements `IDamageModifier` (`GarrisonManager.cs:1415-1435`):

```csharp
if (health.HP <= 1) return 0;                              // at the floor, block everything
if (damage.Value >= health.HP)                             // this blow would kill
    return (health.HP - 1) * 100 / damage.Value;           // scale it down to leave exactly 1 HP
```

The `Indestructible` flag driving it is `GarrisonManagerInfo.Indestructible = true`
(`GarrisonManager.cs:85`), and its own `[Desc]` at `:83-84` reads *"HP is clamped to 1 minimum. At
1 HP the building shows its damaged sprite and provides minimal cover."*

**It is never set in YAML.** `grep -rn "Indestructible" mods/` returns zero matches — all 41
`^CivBuilding` descendants plus GTWR/PBOX/HBOX get it from the C# default. Consequence: **turning
it off, or gating it, is a one-line change** at a single site, not a 44-actor YAML sweep.

### F2 — Occupant safety already degrades with damage, and there is already a named rubble tier

`GarrisonProtectionInfo` (`GarrisonProtection.cs:21-33`) exposes `BaseProtection`,
`CriticalProtection`, **`RubbleProtection`** and `MinPassThrough`. `GetCurrentProtection`
(`:63-74`) returns `RubbleProtection` when `HP <= 1` and otherwise interpolates. `^CivBuilding`
ships 95 / 70 / 30 / 15 (`civilian.yaml:115-119`).

Pass-through to a sheltering soldier is `damage * (100 - protection) / 100`:

| Building state | Protection | Occupant takes |
|---|---|---|
| Full health | 95% | **5%** of each hit |
| 1 HP above the floor | 70% | 30% |
| Rubble (1 HP) | 30% | **70%** |

So *"eventually it will not be possible to occupy it safely"* **is already true** — a rubble
occupant takes fourteen times the damage he took in an intact building. This did not need building.

### F3 — …but the curve is flat across the whole health bar and then cliffs at the last hit point

The interpolation is `Critical + (Base - Critical) * hpPct`, i.e. `70 + 25·hpPct`. Across the
entire bar from full health down to 2 HP, protection moves only **95 → 70**. The remaining and much
larger part of the degradation, **70 → 30**, happens in a single hit point, at the clamp.

The gradient the user describes therefore exists but is compressed into an instant. In play the
building reads as "fine, fine, fine, suddenly a death trap" rather than as deteriorating cover.
**This is a pure YAML retune of three numbers.**

### F4 — The automatic bail-out has never once run for a garrisonable building

This is the real defect, and it is the closest thing in the tree to *"eventually it will not be
possible to occupy it safely"* expressed as an action rather than a damage multiplier.

`Cargo` carries `EmergencyBailDamageState = DamageState.Heavy` (`Cargo.cs:115`): passengers leave
on their own, without an order, once the structure passes 50% HP. The logic is real and careful —
staggered exits, a re-arm on repair, a deliberate exclusion of the Dead state (`:751-762`).

It is unreachable. `INotifyDamage.Damaged` opens (`Cargo.cs:825-832`):

```csharp
void INotifyDamage.Damaged(Actor self, AttackInfo e)
{
    if (IsEmpty()) return;

    // Skip legacy damage forwarding when GarrisonProtection handles it
    if (self.Info.HasTraitInfo<GarrisonProtectionInfo>()) return;      // <-- :831
```

and the entire bail block sits ~45 lines **below** that return (`:876-943`). Every garrisonable
building has `GarrisonProtection`. **The bail is therefore dead on exactly the actors it was most
relevant to.**

**It was born dead, and the history says how.** The guard is older than the bail:

- `c9699af9` (2026-03-20) introduced the guard, when `Damaged` contained *only* damage forwarding.
  The early `return` was correct and complete at that moment.
- `4e8e29e2` (2026-08-10) appended the emergency-bail logic underneath it — and the guard's scope
  silently widened from "skip the forwarding" to "skip the rest of the method".

The comment still says *"skip legacy damage forwarding"*, which is no longer all it does. Either
the comment or the code is wrong; someone has to choose which.

### F5 — Two smaller inconsistencies, noted so they are not rediscovered

- GTWR/PBOX/HBOX set `BaseProtection: 97` and `CriticalProtection: 80` but **omit
  `RubbleProtection`** (`structures-defenses.yaml:153-156`), inheriting the C# default of 30.
  Defensible, but it is inheritance by omission rather than by decision.
- `RubbleProtection`'s `[Desc]` claims it is *"Lower than CriticalProtection"*
  (`GarrisonProtection.cs:27-29`) while **both C# defaults are 30**. The statement is false at
  defaults and true only because `^CivBuilding` overrides Critical to 70. A doc-only fix.

### F6 — The one surviving death path confirmed to have no survivors

`Health.Kill` calls `InflictDamage(new Damage(MaxHP), ignoreModifiers: true)` (`Health.cs:243-246`),
bypassing F1's clamp. `GarrisonManager.INotifyKilled.Killed` then computes
(`GarrisonManager.cs:1391-1393`):

```csharp
var damageToDeal = soldierHealth.MaxHP * damage / self.Trait<Health>().MaxHP;   // = MaxHP/MaxHP = 100%
damageToDeal += self.World.SharedRandom.Next(soldierHealth.MaxHP / 5);          // plus up to 20% more
```

With `damage == MaxHP` the ratio is exactly 1, so every port occupant takes **100% of his health
plus a random bonus**. Confirmed: on the only path that can kill a garrison building, there are
never survivors. Prior audit's arithmetic verified correct.

---

## Ranked proposals

Effort is given as **change surface + verification cost**, because that is the honest currency
here — I cannot estimate wall-clock reliably and a number I invented would be worse than none.
"1 slot" = one autotest launch, which must be requested from the manager.

### P1 — Fix the guard scope in `Cargo.Damaged`, then decide what garrisons should do

**Rank 1 because it is the only item where working code is currently inert.**

Narrow the guard so it covers the damage-forwarding block it names, instead of the whole method:

```csharp
// forwarding only — GarrisonProtection owns this for garrison buildings
if (!self.Info.HasTraitInfo<GarrisonProtectionInfo>())
{
    ... existing threshold + per-passenger loop ...
}

// bail logic continues, now reachable for garrisons
```

**Surface:** ~5 lines of C#, one file, no YAML. **Verification:** 1 slot, plus a RED run — the RED
is cheap and unusually meaningful here, because the current build IS the red: on `main` the bail
provably cannot fire for a garrison, so a scenario written today fails before the fix and passes
after, with no sabotage needed.

**Then a design decision, which is the user's and not mine.** With the guard fixed, garrison
buildings would bail at `Heavy` (50% HP) by default. I do **not** recommend that: it evicts men
into open ground at half health, which is a large and probably unwanted combat change. I recommend
setting `EmergencyBailDamageState: Critical` on the three garrison templates so the men leave when
the building reaches the rubble floor — which is precisely the user's sentence, using a mechanism
that already exists and needs no art.

**Caveat that must be stated before anyone commits to this.** `bailedOut` latches and only re-arms
when the damage state falls back below the threshold (`Cargo.cs:884-885`). An `Indestructible`
building pinned at 1 HP is permanently `Critical`, so **the bail would fire once and never again**.
Re-garrisoning the rubble afterwards would not re-trigger it. P1 therefore makes rubble *expel its
current occupants*; it does **not** make rubble permanently uninhabitable. If the latter is the
goal, P1 is not sufficient on its own and P2 is the mechanism that actually carries it.

### P2 — Retune the protection curve so degradation is gradual

Fixes F3. Three numbers in `civilian.yaml:115-119`, plus the same for the defence structures.
Something like `BaseProtection: 95`, `CriticalProtection: 45`, `RubbleProtection: 15` moves the
curve meaningfully across the whole bar instead of banking it all into the final hit point.

**Surface:** YAML only, 2 files, ~6 lines. **Verification:** 0 slots strictly — it is a tuning
change with no new code path — but it is a balance change and deserves a combat-sim pass
(`tools/combat-sim/`, per `DOCS/recipes/BALANCE.md`) rather than a launch.

**This is the cheapest item that directly delivers the user's stated goal**, and unlike P1 it has
no latch problem: the pass-through penalty applies to every hit, forever, with no one-shot.

### P3 — Name the rubble state in the panel

The legibility I expected to be missing **is largely already there**, and I checked before
proposing it: `GarrisonPanelLogic.cs:99-104` renders `GARRISON [Shield: 95%]` in the header and
`:269-273` renders `[S] <name> (95% cover)` per shelter occupant. The player can already watch the
number fall.

What is missing is only that the terminal state is not *named* — at 1 HP the panel says
`30% cover`, identical in kind to any other number, with nothing to say the building is now rubble
and will not degrade further. One conditional and one string.

**Surface:** ~4 lines in one file. **Verification:** 1 screenshot, per `DOCS/recipes/SCREENSHOT.md`
— no autotest slot.

### P4 — Leave destructibility alone, and write down that the dead content is dead on purpose

Per the user's ruling. See the next section. **Surface:** documentation only. **Verification:**
none.

### Not proposed, and why

**Capping damage at a terminal damaged state** — no work needed, F1 already does it.
**Making occupation unsafe as damage rises** — no work needed, F2 already does it.
**Varying garrison capacity by damage state** — `Cargo.MaxWeight` (`Cargo.cs:33`) is a plain
readonly int with no condition or damage-state hook anywhere in the trait, and no
`ICargoCapacityModifier`-style interface exists. Delivering this means new engine plumbing, and it
buys less than P2 does for more. **A costed no.**
**Varying firing ports by damage state** — same answer. `GarrisonManagerInfo.Ports`
(`GarrisonManager.cs:48`) is loaded once by `LoadPorts` (`:112-127`) and `PortStates` is built in
the constructor (`:210-213`). Port count is fixed at actor creation with no runtime path to change
it. Expensive; not worth it before release.

---

## The destructibility ruling — 2026-09-01

**The user was asked whether garrisonable buildings should become destructible. He chose "leave it
as-is for now."** This section exists so the next audit does not refile the consequences as a bug.

**The following content is unreachable, and that is deliberate, not an oversight:**

| Dead content | Where | Why it cannot run |
|---|---|---|
| `Cargo.EjectOnDeath: True` | `civilian.yaml:67`, `structures-defenses.yaml:127` region | building cannot reach 0 HP |
| `Explodes` + `Explodes@CIVPANIC` | `civilian.yaml:50-53` | same |
| `SpawnActorOnDeath` husk civilians | `civilian.yaml:30-49` — **already commented out**, with a note that `Probability` does not work alongside `SpawnOnceOnOwnerChange` | same, and additionally disabled by hand |
| `GarrisonManager.INotifyKilled.Killed` | `GarrisonManager.cs:1369-1400` | same |

The single exception is `Actor.Kill()`, which bypasses the clamp via `ignoreModifiers: true` — see
F6. Nothing in normal combat takes that path.

**Do not "fix" any of the above by making buildings destructible.** The user has ruled. If someone
later wants the death content live, the change is F1's one-line default, and it should be proposed
as a design change with the user, not filed as a defect.

---

## Long-horizon ideas — recorded, NOT scheduled, POST-RELEASE

The user asked for these to be written down and explicitly said to keep the pre-release work
minimal. Both are filed as pipeline items so they are findable; neither is queued.

- **Item 72 — AI-generated intermediate damage-state sprites.** The art ships only healthy and
  damaged frames, which is the real reason the rubble state has to be a single terminal step rather
  than a visible ladder. The user's idea is to run the existing art through a generative process to
  produce intermediate states. Note the dependency direction: **P2's finer protection curve is
  worth doing even without new art, but new art would make P2's gradient legible** rather than
  something the player only feels. Dossier: `WORKSPACE/pipeline/items/72-garrison-damage-sprites.md`.

- **Item 73 — Multi-block interconnected buildings.** A building composed of several connected
  blocks, where parts can be destroyed independently and occupants are forced to relocate within
  the structure. This is the user's own framing and it is a large piece of work — it touches
  footprints, per-block health, the port model (which is fixed at creation, see "Not proposed"),
  pathing inside a structure, and the AI's garrison evaluation. Dossier:
  `WORKSPACE/pipeline/items/73-multi-block-buildings.md`.

---

## Open question this branch could not settle

**Should the emergency bail apply to buildings at all?** F4 establishes that the code intends to
and cannot. It does not establish that a player wants his garrison walking out of a building under
fire. P1 makes it possible; only the user can say whether it should be `Critical`, `Heavy`, or left
off with the guard fixed and the templates opting out explicitly. Recorded rather than assumed.
