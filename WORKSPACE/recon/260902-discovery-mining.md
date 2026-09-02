# Discovery mining — ideas hiding in DISCOVERIES.md and bugs/discovered.md

**Read against `main @ 6a7e1839`** (worktree `wt/discovery-mining`, branched from the manager's
checkout, which was 0 commits behind `origin/main`). Read-only: no build, no launch, no autotest, no
`--check-yaml`. Every `file:line` below was opened in this worktree at this commit — none is relayed
from the entry that pointed at it.

**Scope filters applied.** Dropped anything inside the user-gated ambush/concealment/cover block
(PIPELINE 67–71, 22), anything already on PIPELINE or `RELEASE_V1.md`, and anything whose subject
ships `Prerequisites: ~disabled`.

**Three leads died on verification and are recorded so nobody re-derives them** — see §3. That
section is the most useful part of this document for anyone who reads DISCOVERIES.md next.

---

## 1. The clusters

### Cluster A — "paused" means *transient* to every consumer, and WW3MOD's two widest pause gates are *durable*

Six separate entries across five weeks. Each was filed as its own defect. They are one root.

An `Armament` in OpenRA can be **disabled** (structurally absent) or **paused** (temporarily unable
to fire). Every engine consumer treats pause as a blink you hold aim through. WW3MOD then wired its
two most common states onto it:

| Gate token | Lines in `rules/` | How long it lasts |
|---|---|---|
| `empdisable` | ~109 | transient |
| `!ammo-*` | 66 | **durable — until resupplied** |
| `heavy-damage-attained` | 24 | **durable — until repaired** |
| `suppressed` | 3 | transient |

*(census framing from the 2026-09-01 `wt/player-feedback` entry, DISCOVERIES:2071; I re-verified the
`heavy-damage-attained` count as 24 by grep and the grant band below.)*

`heavy-damage-attained` is granted at `ValidDamageStates: Heavy, Critical`
(`mods/ww3mod/rules/defaults.yaml:258-260`), and `Health.DamageState` puts Heavy at
`HP*100 < MaxHP*50` (`engine/OpenRA.Mods.Common/Traits/Health.cs:106-109`). **So the widest durable pause in
the game is "this vehicle is below half health".** It appears in the standard armed-vehicle gate
`!ammo-primary || empdisable || heavy-damage-attained` across all three faction files
(`vehicles-america.yaml:88, 244, 375, 529, 645, 801, 931, 958, 993, 1118`, `vehicles-ukraine.yaml:52`, …).

Four consequences, each filed separately, all from that one fact:

1. **The unit goes sensor-blind.** `AttackBase.GetMaximumRange()` *skips paused armaments*
   (`engine/OpenRA.Mods.Common/Traits/Attack/AttackBase.cs:596-597`) and returns `WDist.Zero` when
   all of them are paused. `AutoTarget` uses it as its scan radius whenever `ScanRadius` is unset —
   `AutoTarget.cs:1114` and `:1177`, `Info.ScanRadius > 0 ? WDist.FromCells(...) : ab.GetMaximumRange()`.
   **No vehicle in the mod sets `ScanRadius`** (grep: only `infantry.yaml:310`, `:2423`, and four
   dev/scenario map overrides). So a half-health tank does not merely hold fire — its search radius
   is literally zero and it will not re-acquire when the condition lifts.

2. **An attack order given to it never ends.** `AttackBaseInfo.AbandonWhenArmamentsPaused`
   (`AttackBase.cs:72`) defaults `false` and **exactly one actor in the mod opts in** — the medic,
   `mods/ww3mod/rules/ingame/infantry.yaml:2314`. Without it the order is *accepted*: the unit drives
   into range, aims, fires nothing, and its activity never completes, so it never goes idle. Every
   `INotifyIdle` behaviour it owns is silenced for the duration.

3. **Which means it never asks for resupply again.** `AmmoPool` is
   `INotifyCreated, INotifyAttack, INotifyBecomingIdle, IResolveOrder, ISync`
   (`engine/OpenRA.Mods.Common/Traits/AmmoPool.cs:268`) — **no `ITick`**. Its only periodic partner,
   `AutoSeekSupplies` (`AutoSeekSupplies.cs:113`, `ITick.Tick` at `:228`), is declared on exactly two
   templates mod-wide, both infantry (`infantry.yaml:251`, `:2027`). A vehicle that reaches a holding
   state and stays idle never re-fires the becoming-idle transition, so `AutoRearmIfDry` never runs
   again. A supply truck can park on top of it.

4. **The one readout that exists for this cannot fire.** `WithHoldingFireDecoration` ships and is
   inherited by four templates (`defaults.yaml:847-856`, amber `pip-orange` at `TopRight`). It reads
   `AutoTarget.LastHeldFireTick` (`AutoTarget.cs:350`), which is set only when
   `declinedShootableTarget` is true (`:1584-1585`) — and that flag is assigned **inside**
   `foreach (var target in targetsInRange)` (`:1450-1470`). With a scan radius of zero,
   `targetsInRange` is empty, the loop body never runs, and the marker is structurally blind to the
   single most common reason a WW3MOD vehicle stops shooting.

**Why this cluster is worth more than its four parts:** each one alone reads as a tuning miss. Together
they say a half-health vehicle is *catatonic* — blind, unresponsive to auto-target, unable to ask for
ammo, and stuck forever on any attack order you gave it. And the amber "I'm holding fire" pip that was
built to explain exactly this is the one case it cannot report.

### Cluster B — five things happen at 50% health simultaneously, and nothing on screen says so

Separate from A but landing on the same threshold, which is what makes it dangerous. Everything below
is one template, `^EffectsWhenDamagedVehicles` (`vehicles.yaml:180`), inherited by `^Vehicle` at
`mods/ww3mod/rules/ingame/vehicles.yaml:8`:

| At `HP < 50%` | Where |
|---|---|
| Turret turn speed → **0** (a hard lock: the turret refuses to acquire) | `vehicles.yaml:291-293` (`TurretTurnSpeedMultiplier@CriticalDamage: Modifier: 0`), and the in-file comment at `:283-290` says so explicitly |
| Ground speed → **50%** | `vehicles.yaml:181-183` |
| Autotarget scan radius → **0** (cluster A) | `AttackBase.cs:596`, `AutoTarget.cs:1114` |
| Bleeds **1% of MaxHP every 5 ticks**, unconditionally | `vehicles.yaml:185-188`, semantics at `ChangesHealth.cs:27-33, 81-87` |
| Smoke ignites | `vehicles.yaml:219` (`StartFraction: 50`) |

**The bleed number nobody has written down.** 1%/5 ticks at 16.67 tps is 1% per 0.3 s, so **50% → 0
takes about 15 seconds**, and at half speed the vehicle covers roughly 12 cells in that time. That is
the entire counterplay window for reaching a Logistics Centre (`RepairsUnits`, `structures.yaml:450`;
`^Vehicle` carries `Repairable:` at `vehicles.yaml:62`). On the seven maps with no Logistics Centre
there is no counterplay at all.

The 50% disable itself is a **user ruling** — *"All units when they are critical (50%, flashing health
pip) should be disabled from firing and moving"* (DISCOVERIES:15486). **I am not proposing to reverse
it.** The gap is that the mod tells the player none of it.

**How much is the player actually told?** `grep` for damage/health/critical/pip across
`mods/ww3mod/languages/en.ftl` and `mods/ww3mod/chrome/ingame-info-howtoplay.yaml` returns **nothing**
except an unrelated `show-on-damage = Show On Damage` settings label. And there is no health bar —
`DrawHealthBar` is deliberately commented out at
`engine/OpenRA.Mods.Common/Graphics/SelectionBarsAnnotationRenderable.cs:181`, with an in-file note
saying two people have already improved that dead chain and *"Ask before uncommenting."* The **only**
health indicator in the game is a 17×5 px damage pip whose critical band pulses red↔dark-red.

### Cluster C — two independent facts are both called "armour", and nothing reconciles them

`Armor: Type:` feeds `Versus:` and decides how much damage a hit does. The `Light`/`Medium`/`Heavy`
string inside a `Targetable`'s `TargetTypes` decides whether a weapon may *aim* at it. Nothing
validates that they agree, and four shipped units disagree with themselves. Two verified here:

- `m109` — `Armor: Type: Light` (`vehicles-america.yaml:604-605`), `Targetable: TargetTypes: Ground, Vehicle, Medium` (`:608-609`)
- `giatsint` — `Armor: Type: Light` (`vehicles-russia.yaml:429-430`), `TargetTypes: … Medium` (`:433-434`)

*(the entry also names `tos` and `tunguska` as Medium-armoured/Heavy-targeted; I did not re-verify those two)*

Player effect: a rifleman cannot shoot an m109 at all, and an anti-tank weapon that hits it tears
through as if it were soft. Whether that is deliberate asymmetry or drift is **unknown and was
explicitly flagged for the user rather than fixed** (DISCOVERIES:4959-4961). This is the clearest
"bug that is really a missing design decision" in the corpus.

---

## 2. Ranked proposals

### P1 — Tell the player what half health means · `SAFE WIN`

1. **What the player experiences.** Today a tank drops below half health and, without warning, stops
   turning its turret, stops shooting, halves its speed, and dies about fifteen seconds later. The
   only thing on screen is a small pulsing red mark and some smoke. Nothing in the game — no tooltip,
   no how-to-play page, no notification — says any of that. A new player concludes the game is broken:
   their tank has full ammo, a clear shot, and refuses to fire. After this, crossing half health reads
   as an event: the unit is visibly out of the fight and visibly on a clock, and the player knows to
   pull it out.

2. **Why it is worth doing.** This is the single largest gap between what the simulation does and
   what the game admits to, and it fires in *every* match within the first few minutes of combat. It
   is also a first-impression blocker under the 2026-08-16 public-release ruling: "my tank won't
   shoot" is exactly the screenshot a stranger posts before bouncing. The behaviour is user-ruled and
   correct — only the presentation is missing, which makes this cheap.

3. **Mechanism.** Three surfaces, none of which touches simulation:
   (a) a how-to-play page — `mods/ww3mod/chrome/ingame-info-howtoplay.yaml` currently says **nothing**
   about damage (verified by grep);
   (b) a distinct decoration or notification at the `heavy-damage-attained` transition, which is
   already a granted condition (`defaults.yaml:258-260`) so a `WithDecoration`/`GrantCondition`
   consumer is pure YAML;
   (c) a countdown made from the number in cluster B — `ChangesHealth@CriticalDamage`
   (`vehicles.yaml:184-187`) is deterministic, so remaining life is computable exactly, and
   `ISelectionBar` implementors already exist (reload, capture, production, supply) as the pattern.

4. **Tier.** `SAFE WIN`.

5. **Honest risk.** The cheap half (a + b) is genuinely cheap. (c) is the one that could sprawl —
   a countdown bar invites "so can I stop it?", which is a design conversation, not a readout. Ship
   (a) and (b) first and treat (c) as separable. Also: the pip is 17×5 px, so any new mark competes
   for the same crowded space above the unit — `defaults.yaml:888-904` already puts spotted `!`,
   two stance glyphs and the holding-fire pip up there.

6. **Proof it does not already exist.** `grep -niE "damag|critical|pip|health"` over
   `mods/ww3mod/languages/en.ftl` returns exactly one line — `:419 show-on-damage = Show On Damage`,
   an unrelated settings checkbox — and the same grep over
   `mods/ww3mod/chrome/ingame-info-howtoplay.yaml` returns **zero lines**.

---

### P2 — Wake up the vehicle that stopped fighting · `SAFE WIN` (behavioural, needs a RED/GREEN)

1. **What the player experiences.** A damaged tank you send to attack drives over, points at the
   enemy, and then just sits there for the rest of the match — it will not shoot when repaired, will
   not react when something drives past, and will not go for ammo even if a supply truck parks beside
   it. Today the only thing that unsticks it is a fresh order from you. After this, a unit that cannot
   currently fight gives itself back: it stops holding the order, it can still see, and it goes for
   resupply on its own.

2. **Why it is worth doing.** This is cluster A, and it is the "visibly stupid behaviour a player
   would screenshot" category the 2026-08-16 bot ruling puts near the top — except it hits **human**
   units too, which makes it worse than a bot-quality item. It also feeds two things already on the
   queue: item 56's supply-truck delivery is measured against units that may never ask.

3. **Mechanism.** Three small, independent changes:
   - Opt the `heavy-damage-attained` vehicle gates into `AbandonWhenArmamentsPaused`
     (`AttackBase.cs:72`), so the activity ends and the unit drops to idle. **Do not widen the flag
     into "abandon when the unit cannot fire"** — `Armament.CanFire` is also false on `IsReloading`,
     `IsWaitingBurst` and `IsAiming`, all true on ordinary ticks of a healthy weapon.
   - Floor the autotarget scan radius so a paused unit still *sees* — either set `ScanRadius` on the
     vehicle templates (the seam already exists, `AutoTarget.cs:68`, `:1114`, `:1177`) or make
     `GetMaximumRange` fall back to the longest paused armament the way `GetMaximumRangeVersusTarget`
     already does at `AttackBase.cs:640-655`.
   - A small **idle-gated** `ITick` that re-asks `AutoRearmIfDry`. It must be idle-gated (an ungated
     version interrupts live player orders) and must guard on `AmmoPool.IsSeekingRearm`, or it will
     tear down and re-plan the same errand every tick.

4. **Tier.** `SAFE WIN` in size, but it is a live behavioural change to every armed vehicle on **both**
   bot profiles, so it needs its own RED/GREEN pair and a knowing note that `@stable` moves.

5. **Honest risk.** Two things could bite. (i) Two of the 64 pause gates are `suppressed >= 10` on
   `^AT` (`infantry.yaml:1739`) and the engineer's repair armament (`:1956`) — those are
   suppression-adjacent and need specific sign-off, so scope this to the `heavy-damage-attained`
   cases. (ii) The accidental rescue documented at DISCOVERIES:16906 — autotargeting is currently the
   *only* thing that makes a dry vehicle re-check resupply — means anything that changes the
   idle/non-idle rhythm can move behaviour in a direction nobody predicted. Measure, do not reason.

6. **Proof it does not already exist.** `grep -rn "AbandonWhenArmamentsPaused" mods/` returns exactly
   **one** line — `infantry.yaml:2314` (the medic). `grep -rn "ScanRadius" mods/` returns no vehicle.
   `AmmoPool` still declares no `ITick` (`AmmoPool.cs:268`). The `wt/paused-cursor` work that merged
   at `4bbd0fad` fixed the **cursor** only — it added `AttackBase.RefusesForPause` (`:684-705`,
   consumed `:860`, `:903`), whose own doc comment states that without the opt-in "the order is
   ACCEPTED: the unit closes, aims, and holds through the pause."

---

### P3 — Make it possible to see which units are selected · `SAFE WIN`

1. **What the player experiences.** You box-select six riflemen and nothing on screen changes. There
   is no bracket, no highlight, no outline — infantry give no selection feedback at all. The only way
   to know what you have is the command bar at the bottom. After this, selecting infantry looks like
   selecting anything else in any RTS ever made.

2. **Why it is worth doing.** Infantry are most of the army. Under the public-release ruling this is
   an "immediately-visible this-is-unfinished signal" in the first thirty seconds of the first match,
   and it is a two-line YAML fix.

3. **Mechanism.** `^Infantry` sets `SelectionDecorations: ShowNever: true`
   (`mods/ww3mod/rules/ingame/infantry.yaml:55-56`) — **the only `ShowNever` in the mod**, verified by
   grep. `SelectionDecorationsBase.cs:109` is literally `if (selected && !Info.ShowNever)`, and
   `SelectionDecorations.RenderSelectionBox` (`SelectionDecorations.cs:68-72`) yields the four white
   corner brackets drawn at `SelectionBoxAnnotationRenderable.cs:52-55`. Removing the two lines turns
   it on. If the brackets are wrong for a 6 px sprite, the `Selectable.Bounds`/`DecorationBounds` on
   the same actor (`infantry.yaml:58-60`) are the knob.

4. **Tier.** `SAFE WIN`.

5. **Honest risk.** `ShowNever` was presumably set on purpose — brackets on a dense infantry blob may
   look like noise, which is a visual judgement I cannot make by reading. Treat this as "show the
   user two screenshots", not "just delete the line". This is a `SCREENSHOT.md` task.

6. **Proof it does not already exist.** `infantry.yaml:55-56` still carries `ShowNever: true` at
   `6a7e1839`, and it is the only occurrence of that field anywhere under `mods/`.

   > **Correction to the source entry, and it matters.** DISCOVERIES:9089 additionally claims that
   > *vehicles* draw their brackets whether selected or not, on the strength of a pixel diff in which
   > "selecting four Bradleys changed zero pixels". **I could not confirm that half and the code reads
   > the other way**: the bracket comes from `RenderSelectionBox`, which is gated on `selected` at
   > `SelectionDecorationsBase.cs:109` for any actor without `ShowNever`, and vehicles do not set it
   > (`vehicles.yaml:47`). The likeliest explanation is a capture artefact — autotest captures null
   > `RenderPlayer` (DISCOVERIES:7138, :17194). **Treat the vehicle half as unproven; the infantry
   > half is unambiguous in code.** Settling it costs one screenshot, not a fix.

---

### P4 — Let a specialist use the weapon you bought it for · `SAFE WIN` + one design call

1. **What the player experiences.** Your Bradley has anti-tank missiles that reach a long way. Ordered
   at a tank, it ignores them and drives all the way in to autocannon range first — into the tank's
   own gun — and only then starts shooting. The same is true of the Stryker SHORAD, the BMP-2, the
   Apache, the Hind and the Mi-28. After this, a unit with a long weapon that is the right weapon
   stops at that weapon's range and uses it.

2. **Why it is worth doing.** The engine's own doc comment names the symptom in the player's words:
   *"a unit whose long-range weapon is the RIGHT weapon closes to its short-range weapon's band
   anyway, and the player sees it refuse the good weapon and drive at the target"*
   (`AttackBase.cs:721-724`). It makes every multi-role unit in the game feel broken, and the fix is
   already built, tested and shipped — on one unit.

3. **Mechanism.** `AttackBase.EngagementMaxRange` (`AttackBase.cs:732-762`) returns the **minimum** of
   all armament ranges unless `EngageAtLongestArmamentRange` is set (`:82`, default `false`), called
   from `Attack.cs:265-268`. **Exactly one actor sets it: `tunguska`, `vehicles-russia.yaml:959`.**
   Census of live multi-armament actors (mine, by script): `bradley`, `strykershorad`, `bmp2`,
   `littlebird`, `HELI`, `HIND`, `MI28` — seven, none of them opted in. The longest branch already
   handles the dry case correctly (it ignores paused armaments and falls back only when all are
   paused, `:747-761`), which is the trap that would otherwise strand a missile-less Tunguska.

4. **Tier.** `SAFE WIN` mechanically — it is seven YAML lines. But **flipping it is a balance change
   on seven units**, and balance changes need the user's explicit review per the standing rule, so
   present it as a proposal with combat-sim numbers rather than shipping it.

5. **Honest risk.** Real. Standing off at missile range makes those seven units meaningfully stronger,
   and the two AA platforms (`strykershorad`, `tunguska`) are the ones where standoff matters most.
   This wants `tools/combat-sim/` before, not after.

6. **Proof it does not already exist.** `grep -rn "EngageAtLongestArmamentRange" mods/` returns one
   YAML hit (`vehicles-russia.yaml:959`) plus one comment. The C# default is still `false`
   (`AttackBase.cs:82`).

---

### P5 — Settle the two facts both called "armour" · `SAFE WIN` (design call, then a lint)

1. **What the player experiences.** Some units are protected from the wrong things. An m109 cannot be
   shot by a rifleman — it advertises itself as medium armour — but any anti-tank weapon that does
   reach it cuts through as if it were unarmoured, because for damage purposes it is light. A player
   who learns "medium armour resists small arms and takes real damage from AT" gets a unit that
   follows the first half of that rule and not the second.

2. **Why it is worth doing.** It is not a bug until someone decides which of the two numbers is the
   intended one — and that is exactly the kind of call the user has said they want to make themselves.
   Once it is made, a one-off NUnit corpus pin stops it ever drifting again, which is cheap and
   permanent.

3. **Mechanism.** The two fields are unrelated pieces of YAML: `Armor: Type:` feeds `Versus:` in
   `DamageWarhead`, and the class string inside `Targetable.TargetTypes` gates whether a weapon may
   aim. Verified disagreements: `m109` (`vehicles-america.yaml:604-605` vs `:608-609`), `giatsint`
   (`vehicles-russia.yaml:429-430` vs `:433-434`). DISCOVERIES:4952-4955 names `tos` and `tunguska`
   as the other two (Medium armour / Heavy target type); I did not re-verify those. The pin is a test
   that walks the ruleset and asserts the two agree, with an explicit allow-list for any disagreement
   the user rules deliberate.

4. **Tier.** `SAFE WIN`.

5. **Honest risk.** The honest risk is doing it *without* the ruling. The entry says outright that
   "fixing" it silently would be a balance change nobody asked for, and it deliberately left it alone
   for that reason. Bring the four units to the user as a question, not a diff.

6. **Proof it does not already exist.** Both disagreements are live at `6a7e1839` — I read all four
   lines. And nothing validates the pair: the entry's central claim ("nothing validates that the two
   agree") is consistent with there being no such test in `engine/OpenRA.Test/`.

---

### P6 — A supply truck that arrives at a unit that stopped asking · `SAFE WIN` (subsumed by P2's third bullet, listed separately because it is the *observable*)

1. **What the player experiences.** You see a tank sitting still with no ammo, you drive a supply
   truck right up to it, and nothing happens. The truck is there, the tank is there, and neither
   acknowledges the other. After this, a unit that is out of ammo and standing still notices help
   arriving.

2. **Why it is worth doing.** It is the most legible symptom of cluster A and the one a player will
   actually try — driving the truck to the tank is the obvious move, and it is the one that fails
   silently.

3. **Mechanism.** As P2's third bullet: `AmmoPool` has no `ITick` (`AmmoPool.cs:268`), its only
   dispatch triggers are the shot that empties the pool and the becoming-idle **transition**
   (`Actor.cs` raises `INotifyBecomingIdle` only on `!wasIdle → IsIdle`), and a unit that is already
   idle never fires that transition again. `AutoSeekSupplies` — the only `ITick` in the system
   (`AutoSeekSupplies.cs:113, 228`) — is on two infantry templates only (`infantry.yaml:251, 2027`).
   The sharpest cases are `strykershorad` and `tunguska`, which mix `Essential` and non-`Essential`
   pools: `Attack.cs` reads `AllPoolsEmpty`, so they never go idle at all and get **one** dispatch
   opportunity per match.

4. **Tier.** `SAFE WIN`.

5. **Honest risk.** Identical to P2's — this is one change, not two, and I have listed it twice only
   because a reader hunting for "the truck doesn't work" will not find it under "paused armaments".
   Do not schedule it as separate work.

6. **Proof it does not already exist.** `grep -rn "AutoSeekSupplies:" mods/` returns exactly two
   lines, both in `infantry.yaml`. `AmmoPool.cs:268`'s interface list is unchanged.

---

### P7 — "Why did that do nothing?" — a player-facing combat feedback channel · `AMBITIOUS`

1. **What the player experiences.** Right now, a shot that connects and accomplishes nothing looks
   identical to a shot that hurt. There is no health bar in this game and the only health indicator
   is a four-step pip, so against a 24,000 HP vehicle roughly thirty consecutive hits change nothing
   visible at all. A player firing the wrong weapon at the wrong armour gets no signal that they are
   wasting their time. After this, the game says so: a hit that armour ate reads differently from a
   hit that landed, and over a fight the player learns which of their weapons work on what.

2. **Why it is worth doing.** This is the deepest legibility gap in the game and it is the one a
   *good* player notices — the mod has a real ballistics model (penetration vs. effective thickness
   vs. facing) and the player can perceive none of it. It is also the cheapest ambitious item on this
   list, because **the machinery is already built and already runs in every shipped build** — it is
   pointed at a developer checkbox rather than at the player.

3. **Mechanism.** Three pieces already exist and are wired:
   - `CombatDebugOverlay` is mounted on `^ExistsInWorld` (`mods/ww3mod/rules/defaults.yaml:4`), i.e.
     essentially every actor, and prints exact damage over any damaged actor via `FloatingText`.
   - **An armour-anomaly detector already runs on every warhead application in the game.**
     `DamageWarhead.InflictDamage` computes `effectiveThickness = thickness * armorPercent / 100`
     (`engine/OpenRA.Mods.Common/Warheads/DamageWarhead.cs:249`) and calls
     `HitCheck.LostMostOfItsDamage(...)` / `IsUnderPerforming(...)` at `:269-273`. When it fires it
     already builds the string `$"ARMOUR {damageBeforeArmour}->{damage}"` and drops a `FloatingText`
     on the victim (`:288-290`) — **gated behind `debugVis.DamageNumbers`**, which defaults `false`
     (`engine/OpenRA.Game/Traits/World/DebugVisualizations.cs:54`) and is a developer checkbox
     (`DebugMenuLogic.cs:107-112`).
   - `GunTrace` formats the whole breakdown at the same site (`DamageWarhead.cs:302`).

   **So the work is a design decision plus a routing change, not a build.** The shape of the decision:
   what does a player see, how often, and does it survive fog. Note two hard constraints:
   `INotifyDamage` is **structurally unable** to carry the reason — `AttackInfo` carries only final
   damage, attacker, and the two damage states (`engine/OpenRA.Game/Traits/TraitsInterfaces.cs:80-86`) —
   so any consumer must be built at the warhead site, which is the one scope where raw and final
   damage are both live. And victim-side modifiers (garrison cover, veterancy, prone) are applied
   *later*, in `Health.InflictDamage`, and are not reachable from the warhead at all.

4. **Tier.** `AMBITIOUS`.

5. **Honest risk.** Three, in order of severity. (i) **Do not do this by turning `DamageNumbers` on** —
   that default is guarded by `DebugVisualizationDefaultsTest` and turning it on is a release blocker
   by explicit ruling (former PIPELINE R17). This must be a separate, player-shaped surface.
   (ii) The armour path is only *one* reason a shot does nothing — `DamageVersus` is the other, and it
   is applied at `:299`, outside the anomaly gate. A readout that explains only armour will mislead on
   `Versus` cases. (iii) Screen clutter: the loud channel is rare by construction but the advisory band
   "DOES fire in a real match" per the in-code comment at `:266-268`.

6. **Proof it does not already exist.** `grep -rn "DamageNumbers" engine/ mods/` shows every consumer
   gated on `debugVis.DamageNumbers`, and `DebugVisualizations.cs:54` is `public bool DamageNumbers = false;`.
   `DrawHealthBar` remains commented out at `SelectionBarsAnnotationRenderable.cs:181`. Nothing in
   `mods/ww3mod/languages/en.ftl` mentions armour, penetration or damage.

---

### P8 — Give the fifteen seconds a shape · `AMBITIOUS` (design decision first)

1. **What the player experiences.** A vehicle that crosses half health is dead in about fifteen
   seconds and can travel roughly twelve cells in that time. There is nothing you can do about it
   except drive for a Logistics Centre, and on seven of the ten shipped maps there isn't one. So the
   damage model's whole back half — the wounded-vehicle phase — is a countdown with no decision in it.
   After this it is a *choice*: pull back and pay something to save the hull, or spend it.

2. **Why it is worth doing.** The user's stated design instinct is "let the damage curve do the work —
   price a bad state, don't forcibly exit the player from it." The 50% disable is user-ruled and
   correct, but the *bleed-out* underneath it is the part that removes the decision, and it has never
   been examined as a design object — the 15 s and 12 cells above are, as far as I can find, written
   down here for the first time.

3. **Mechanism.** `ChangesHealth@CriticalDamage` (`mods/ww3mod/rules/ingame/vehicles.yaml:185-188`):
   `PercentageStep: -1`, `Delay: 5`, `StartIfBelow: 50`, inherited by `^Vehicle` at `:8` (`Inherits@Slowdown`). Semantics at
   `engine/OpenRA.Mods.Common/Traits/ChangesHealth.cs:81-87`. **The trait has an unused knob that is
   exactly the lever this wants:** `DamageCooldown` (`:38-39`, `:75-79`) suspends the bleed for N ticks
   after taking damage — inverted, a "hasn't been shot recently" gate would make disengaging the
   counterplay. `StartIfAbove` (`:35-36`, `:72-73`) is the other free knob and would let the bleed stop
   at a floor rather than at zero. Both are YAML-only.

4. **Tier.** `AMBITIOUS` — not because the code is large (it is one YAML block) but because it changes
   how every armoured engagement ends, and that is a feel question that needs the user and a playtest.

5. **Honest risk.** This is the proposal most likely to be wrong. The current behaviour may be exactly
   what the user wants — a decisive, fast resolution with no dragging — and "add counterplay" is a
   pitch, not a finding. Do not open this as a bug. Also: any change here interacts with the ejected-crew
   mechanic, which is intended and was deliberately reverted once already (`game-model.md` §"Ejected
   vehicle crew burn to death").

6. **Proof it does not already exist.** `grep -rn "DamageCooldown\|StartIfAbove" mods/` returns nothing —
   neither knob is set anywhere in the mod. The block at `vehicles.yaml:185-188` sets only
   `PercentageStep`, `Delay` and `StartIfBelow`. Nothing on `PIPELINE.md` or `RELEASE_V1.md` addresses
   the bleed rate; PIPELINE 72/73 are garrison-building items and are post-release.

---

## 3. Leads that died on verification — recorded so nobody re-derives them

This project's most expensive recurring mistake is proposing already-merged work. Three of my strongest
candidates failed, and the failures are worth more than some of the proposals.

1. **"The in-game Options tab is empty for everyone"** (DISCOVERIES:16707, 2026-08-30, promoted to
   `architecture.md`). **FIXED.** The one-word fix the entry prescribed has been applied:
   `mods/ww3mod/chrome/ingame-info-lobby-options.yaml:27` now declares `Label@CATEGORY_FILTER`, and
   `LobbyOptionsLogic.cs:227-231` carries a new comment naming that file as the panel that reads
   `"Common"`. **The entry does not say it was fixed** — the promotion note explicitly banks "current
   emptiness as a dated observation", and that dated observation is now false.

2. **AA missiles do ~1/20th of their printed damage** (bugs/discovered.md:3449, `[high]`). **Moot under
   the scope ruling.** `AirToAirMissile` is mounted only on `F16` (`aircraft-america.yaml:603`) and
   `MIG` (`aircraft-russia.yaml:623`); `SurfaceToAirMissile.double` only on the SAM Site
   (`structures-defenses.yaml:823`). All three carry `Prerequisites: ~disabled`. The related MiG-29 /
   F-16 armour-thickness asymmetry is real (`aircraft-russia.yaml:599-600` has `Type: Medium` with no
   `Thickness`, against `aircraft-america.yaml:578-580` `Type: Medium, Thickness: 10`) but both
   aircraft are disabled.

3. **`FTUR` Flame Turret permanently disarmed by one burst** (bugs/discovered.md:3411) — and, more
   usefully, **the whole class.** I checked every actor in
   `mods/ww3mod/rules/ingame/structures-defenses.yaml`: **all eighteen are `Prerequisites: ~disabled`**
   (GTWR, PBOX, HBOX, SBAG, FENC, BARB, BRIK, HGATE, VGATE, CRAM, AGUN, SAM, HSAM, FTUR, MSLO, GUN, …).
   **The player has no buildable static defence of any kind.** Every DISCOVERIES/bugs entry about a
   defensive structure is therefore out of scope by the 2026-08-16 ruling, and there are several. Worth
   knowing before anyone spends a session on one. *(Whether the empty Defense queue is itself intended
   is a question for the user, not a finding — I did not chase it.)*

Two further corrections carried inline above: the vehicle half of the selection-invisibility claim
(P3) could not be reproduced by reading and the code says the opposite; and the `heavy-damage-attained`
armament-gate count is **24**, which I re-grepped, not the "64 `PauseOnCondition` gates" figure that
was itself already rejected once at curation.

---

## 4. If you want a run

Nothing here was measured; the whole document is reading. Two questions would be settled by one game
each, and both are cheap:

- **P3, vehicle half.** `./tools/autotest/run-test.sh test-select-by-type` (or any capture with a
  vehicle on screen). **Answer:** if the white corner brackets are present on an *unselected* own
  vehicle, DISCOVERIES:9089 is right and P3 grows to cover vehicles; if they appear only on selection,
  P3 is infantry-only as written.
- **P2/P6, the catatonia symptom.** Stage a tank at Heavy damage with a full magazine (so
  `AmmoPool.CannotFight` stays false and the ammo guards cannot rescue it), order it to attack, and
  assert with `TestHarness.HoldsAttackActivity`. **Answer:** if the attack activity is still held after
  the target is gone, cluster A is confirmed end-to-end and P2 has its RED. Note the existing
  `test-attackmove-dry-breaks-off` is **not** this pin — it is the ammo path and is green on its own
  account (bugs/discovered.md:3897-3914).
