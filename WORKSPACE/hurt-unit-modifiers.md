# Hurt-unit modifiers: what damage already does, and what an "impediment" quantity would collide with

**Research only.** No behaviour changed, no YAML rules edited, no engine C# edited. Read against
`main @ 6430ad8f` (worktree `wt/hurt-modifiers`, clean tree). The main checkout was under a merge gate
and was not touched; nothing here was built, launched, or linted.

Every claim below cites `file:line`. Claims that are readings of code are stated flatly; the two places
where I am reasoning past what the code literally says are marked **INFERENCE**. Section 8 lists what
reading could not settle.

---

## 1. The lint finding is real, and it is exactly as broad as it sounds (Q2)

**`light-damage-attained` and `medium-damage-attained` are granted by every combat actor in the mod and
consumed by nothing, anywhere.**

Repo-wide, across `*.yaml`, `*.cs`, `*.lua`, `*.md`, `*.txt`, `*.py`, the two token names appear **three
times outside `WORKSPACE/`**, and two of them are the grant sites:

| Site | What it is |
|---|---|
| `mods/ww3mod/rules/defaults.yaml:266-268` | `GrantConditionOnDamageState@LightDamageAttained`, `Condition: light-damage-attained`, `ValidDamageStates: Light, Medium, Heavy, Critical` |
| `mods/ww3mod/rules/defaults.yaml:272-274` | `GrantConditionOnDamageState@MediumDamageAttained`, `Condition: medium-damage-attained`, `ValidDamageStates: Medium, Heavy, Critical` |
| `DOCS/archive/superpowers/specs/2026-05-04-rubble-evacuation-investigation.md:21` | prose in an archived spec |

There is **no** `RequiresCondition`, `PauseOnCondition`, or any other consumer in `mods/`, in `engine/`,
or in `tools/`. No engine `Info` field defaults to either string (the way `AutoTargetInfo.BreakOffCondition`
defaults to `"critical-damage"` at `engine/OpenRA.Mods.Common/Traits/AutoTarget.cs:244`). Consumer count
is **zero**, not "small" or "indirect".

### Does the warning mean something narrower than it sounds? No.

The rule is `engine/OpenRA.Mods.Common/Lint/CheckConditions.cs`. It builds, **per actor**, the set of
strings on `[GrantedConditionReference]` fields and the set on `[ConsumedConditionReference]` fields, and
warns on `granted.Except(consumed)` (`CheckConditions.cs:69-71`).

Being per-actor, this rule *can* fire falsely — a condition granted on actor A and consumed on actor B
would warn while being perfectly live. That is the narrower reading available in principle, and **it does
not apply here**, because there is no consumer on any actor. The warning is literal.

Note the asymmetry in the same rule: unconsumed is `emitWarning` (`:71`), un-granted is `emitError`
(`:75`). That is why these have survived — they have only ever been a warning.

### How many actors grant them

`^DamageStates` (`defaults.yaml:258-283`) is pulled in by five base templates, and every concrete actor
descending from those grants all eight tokens:

| Template | Inherit site |
|---|---|
| `^Infantry` | `mods/ww3mod/rules/ingame/infantry.yaml:19` |
| `^Vehicle` | `mods/ww3mod/rules/ingame/vehicles.yaml:9` |
| `^NeutralAirborne` (aircraft) | `mods/ww3mod/rules/ingame/aircraft.yaml:10` |
| `^Building` | `mods/ww3mod/rules/ingame/structures.yaml:91` |
| `^CivBuilding` | `mods/ww3mod/rules/ingame/civilian.yaml:4` |

Only two actor families ever remove any rung, and both remove **only** `@CriticalDamage`
(`structures.yaml:111`, `civilian.yaml:11`) — the light/medium "attained" grants are never removed, except
on one husk-like civilian actor that strips the whole ladder (`civilian.yaml:453-459`).

I did not run the gate, so I cannot give you the exact integer the lint prints. The structural answer is
firmer than a count anyway: **every combat actor in the mod grants both tokens; nothing consumes either.**

### Infantry does NOT differ from vehicles — and the lint list already shows it

The brief's list includes `crew.commander.russia`, which is **infantry**, not a vehicle:
`crew.yaml:174-175` inherits `^CrewMember`, which inherits `^CamoSoldier` (`crew.yaml:12`) →
`^Soldier` → `^Infantry` → `^DamageStates`. It is in the list for exactly the same reason `abrams` is.
(`truk` in the list is the vehicle `TRUK`, `vehicles.yaml:571`.)

### The important qualifier: the damage ladder as a whole is NOT inert

This is where "the existing damage-state modifiers are partly inert" could be over-read. What is dead is
the two *"attained"* convenience variants **at the light and medium rungs only**. The exact-band tokens
`light-damage` / `medium-damage` / `heavy-damage` are heavily consumed, and so are
`heavy-damage-attained` (**37** consumer lines in `mods/`) and `critical-damage` (**22**).

The real asymmetry the dead tokens are sitting next to is this:

- **Infantry has a full four-rung damage ladder** keyed on the exact-band tokens
  (`^EffectsWhenDamagedInfantry`, `infantry.yaml:1055-1133`).
- **Vehicles have no light or medium damage effects at all.** `^EffectsWhenDamagedVehicles`
  (`vehicles.yaml:180-302`) starts at `heavy-damage-attained`. Above 50% HP a vehicle is mechanically
  undamaged.

**INFERENCE:** the two dead tokens look like the beginning of a vehicle light/medium ladder that was never
written. I cannot prove intent from the tree — there is no comment at the grant sites saying so.

Practical consequence for the coupling work: deleting the two grants is a 6-line no-op that silences ~20
warnings. Giving them consumers is the actual unbuilt feature, and it is a *vehicle* feature.

---

## 2. Every existing consequence of being damaged (Q1)

Bands come from `engine/OpenRA.Mods.Common/Traits/Health.cs:95-114`: `Critical` below 25%, `Heavy` 25–50%,
`Medium` 50–75%, `Light` 75–100%, `Undamaged` only at exactly full HP.

Token map, all from `^DamageStates` (`defaults.yaml:258-283`):

| Token | ValidDamageStates | HP range |
|---|---|---|
| `damaged` | Light, Medium, Heavy, Critical | <100% |
| `light-damage` | Light | 75–100% |
| `light-damage-attained` | Light, Medium, Heavy, Critical | <100% — **inert** |
| `medium-damage` | Medium | 50–75% |
| `medium-damage-attained` | Medium, Heavy, Critical | <75% — **inert** |
| `heavy-damage` | Heavy | 25–50% |
| `heavy-damage-attained` | Heavy, Critical | <50% |
| `critical-damage` | Critical | <25% |

Because light/medium/heavy are **exact bands**, their rows do not accumulate down the ladder — at Medium
only the Medium row is live. Only the `-attained` tokens overlap with their exact-band twin.

Per project vocabulary: "critical" as the user says it means `DamageState.Heavy`, the **50%** line, which
is the `heavy-damage-attained` token. The `critical-damage` token is the 25% doom marker. Both appear
below and are kept distinct.

### Infantry — `^EffectsWhenDamagedInfantry`, inherited by `^Infantry` at `infantry.yaml:13`

| Band | Speed | Vision | Burst | BurstWait | Inaccuracy | Other |
|---|---|---|---|---|---|---|
| Light (75–100%) | 75 `:1056` | 75 `:1059` | 75 `:1062` | 150 `:1065` | 150 `:1068` | — |
| Medium (50–75%) | 50 `:1072` | 50 `:1075` | 50 `:1078` | 200 `:1081` | 200 `:1084` | — |
| Heavy (25–50%) | 25 `:1092` | 25 `:1095` | 25 `:1098` | 300 `:1101` | 300 `:1104` | **+ `prone` granted** `:1088-1090`; **Speed 0** `:1112-1114` |
| Critical (<25%) | (0 persists) | 10 `:1115` | 10 `:1118` | 400 `:1121` | 400 `:1124` | — |

Plus, on the same HP crossings:

- **Stops shooting below 50%** — `^Soldier`'s `AttackFrontal.PauseOnCondition: garrisoned-at-port || heavy-damage-attained` (`infantry.yaml:210`). Pinned by `CriticalWoundFireGateTest.cs:42`.
- **Bleed-out below 50%** — `ChangesHealth@BleedOut`, `-1%` every 50 ticks, `StartIfBelow: 50` (`infantry.yaml:1130-1134`).
- **Goes prone below 50%** — `GrantCondition@HeavyDamageProne` (`infantry.yaml:1088-1090`). See §6.
- **Prone again below 25%** via `InfantryStates.ProneCondition: … || critical-damage` (`infantry.yaml:317`, and the amphibious variant `:342`).
- **Civilians stop panicking/wandering below 50%** — `ScaredyCat.RequiresCondition: !heavy-damage-attained` (`:400`), `Wanders.RequiresCondition: !heavy-damage-attained` (`:404`).
- **Damage pips** — `^DamageInfantryPips` (`:723-747`), four rungs.
- **Not auto-acquired below 25%** — `AutoTargetInfo.BreakOffCondition` defaults to `"critical-damage"` (`AutoTarget.cs:244`), consumed at `AutoTarget.cs:1550-1554`.

Note `Speed 0` at Heavy: `SpeedMultiplier@HeavyDamage` (25) and `SpeedMultiplier@CriticalDamage` (0, but
gated on `heavy-damage-attained`) are both live in 25–50%, and 25% × 0% = 0. The trait key says
`@CriticalDamage` while the band is 50% — deliberately not renamed, because
`PanicGateAtCriticalDamageTest` looks it up by that exact key (`infantry.yaml:1107-1111`).

### Vehicles — `^EffectsWhenDamagedVehicles`, inherited by `^Vehicle` at `vehicles.yaml:8`

| Band | Effect |
|---|---|
| Light (75–100%) | **nothing** |
| Medium (50–75%) | **nothing** |
| Heavy (<50%, `heavy-damage-attained`) | Speed **50** `:181-183`; **turret traverse 0** `:290-292`; every Armament paused (≈20 `PauseOnCondition: … \|\| heavy-damage-attained` sites across `vehicles-america.yaml`, `vehicles-russia.yaml`, `vehicles-ukraine.yaml`); `ChangesHealth@CriticalDamage` −1%/5 ticks, `StartIfBelow: 50` `:184-187`; `onfire` stacks begin, `StartFraction: 50` `:217-222`; crew begin bailing (`VehicleCrew.cs:55`, `EjectionDamageState = DamageState.Heavy`) |
| Critical (<25%) | Speed **0** `:272-274`; smoke animation `:193-196` |

Vehicles get **no vision, burst, burst-wait or accuracy penalty from damage at all.** The turret freeze at
50% is a hard lock, not a slow: `Modifier: 0` makes `Turreted.FaceTarget` refuse to acquire
(rationale in-file at `vehicles.yaml:276-295`).

### Aircraft — `^WhenDamagedAir` (`aircraft.yaml:399-418`)

| Band | Speed | Inaccuracy | BurstWait |
|---|---|---|---|
| `heavy-damage-attained` (<50%) | 90 `:400` | **75** `:407` | **75** `:411` |
| `critical-damage` (<25%) | 75 `:403` | **50** `:409` | **50** `:413` |

**A damaged aircraft currently shoots *better*.** Lower `InaccuracyMultiplier` is more accurate
(`InaccuracyMultiplier.cs:29` feeds `Util.ApplyPercentageModifiers` at `Util.cs:403`), and lower
`BurstWaitMultiplier` is a shorter wait between bursts. So at <25% HP an aircraft is twice as accurate and
fires twice as fast as an undamaged one, while losing 25% speed.

> ### ⚠️ CORRECTED 2026-09-12 — the two bands STACK, so the sentence above understates every figure by 1.33×
>
> **The paragraph above is left as written, per the convention that a dated record is superseded in place rather
> than rewritten. Its direction is right; its arithmetic is wrong, and so is the identical claim that was in
> `impediment-spec.md` §2.3.**
>
> `heavy-damage-attained` is granted on `ValidDamageStates: Heavy, Critical` (`defaults.yaml:278-280`) while
> `critical-damage` is granted on `Critical` alone (`:281-283`). **Below 25% HP both conditions hold, both traits
> in each pair are enabled at once, and `Util.ApplyPercentageModifiers` multiplies them** — there is no max-wins
> and no dedupe (`Util.cs:238-246`). The bottom band is a **product**, not a single value:
>
> | Axis | Effective below 25% HP | The table above reads |
> |---|---|---|
> | Inaccuracy | 75% × 50% = **37.5%**, i.e. **2.67× more accurate** | 50% → "twice" |
> | BurstWait | 75% × 50% = **37.5%**, i.e. **2.67× faster** | 50% → "twice" |
> | Speed | 90% × 75% = **67.5%** | 75% |
>
> **Why this is easy to get wrong here and nowhere else:** the infantry ladder in the section above uses the
> *exclusive* `light-damage` / `medium-damage` / `heavy-damage` bands, so exactly one trait per axis is ever live
> and its per-band table can be read straight off. `^WhenDamagedAir` is the only family that pairs a latching
> token with an exact-band one. (The infantry **speed** column is the one exception and this document already
> catches it correctly — see the `Speed 0` note under the infantry table, `25% × 0% = 0`.)
>
> **Status of the INFERENCE below:** settled. The user ruled it a bug on 2026-09-11, and the fix shipped as
> `2cda2e3f` (merged `557c38d7`, 2026-09-12): the four values became their exact reciprocals, 133 and 200, so
> effective inaccuracy and burst wait below 25% HP are now **266%**. The speed column was correctly left alone.
> The stacking is recorded in-file at `aircraft.yaml:412-415`, and the full finding — including that
> `BurstWaitMultiplier` reaches only `Weapon.BurstWait` and not `ReloadDelay` — is at `WORKSPACE/DISCOVERIES.md`
> under 2026-09-12.

That is a reading of the numbers, not of intent. **INFERENCE:** this looks like a sign error — every other
family moves these two in the punishing direction (infantry 150→400). Flagging, not fixing; it is outside
this brief's scope and is a balance change.

Also on aircraft: smoke trails on `critical-damage` (`aircraft.yaml:501`, `:510`), and eight further
`critical-damage` consumers across `aircraft-america.yaml` and `aircraft-russia.yaml`.

### Buildings

No speed/accuracy/vision concept applies. `^Building` and `^CivBuilding` **drop the Critical rung
entirely** (`structures.yaml:111`, `civilian.yaml:11`) under a user ruling recorded at
`structures.yaml:93-110`. Only health pips remain (`^GarrisonHealthPips`, `defaults.yaml:222-250`).

---

## 3. What would double-count (Q3)

All six multiplier traits are `ConditionalTrait`s returning a percentage
(`SpeedMultiplier.cs:29`, `VisionMultiplier.cs:29`, `BurstMultiplier.cs:29`, `BurstWaitMultiplier.cs:29`,
`InaccuracyMultiplier.cs:29`, `TurretTurnSpeedMultiplier.cs:29`), and every consumer folds them through
`Util.ApplyPercentageModifiers` (`engine/OpenRA.Mods.Common/Util.cs:238-246`), which multiplies:
`a *= p / 100m` per modifier. **Every trait instance whose condition holds multiplies in. There is no
max-wins, no cap, no dedupe.** So any stat claimed by two sources today is already multiplying, and a
third source would multiply again.

### Infantry

| Stat | Suppression (`^SuppressionEffects`, `infantry.yaml:422-587`) | Damage (`^EffectsWhenDamagedInfantry`) | Collides? |
|---|---|---|---|
| Speed | 90→0 over 10 tiers `:435-464` | 75/50/25, then 0 below 50% | **YES** |
| Vision | 90→0 `:466-494` | 75/50/25/10 | **YES** |
| Burst | 90→0 `:496-525` | 75/50/25/10 | **YES** |
| Burst wait | 110→200 `:527-556` | 150/200/300/400 | **YES** |
| Inaccuracy | 120→300 `:558-587` | 150/200/300/400 | **YES** |
| `prone` | granted at `suppressed > 30` `:317` | granted below 50% `:1088-1090` | **YES — same token** |
| Turret traverse | — | — | **unclaimed** |
| Firepower | — | — | **unclaimed** |
| Reload delay | — | — | **unclaimed** |
| Damage taken | — | — | **unclaimed** (indirect via `prone` only) |
| Weapon range | — | — | **unclaimed** |

All five numeric axes are already doubled. Worked worst case — infantry at `critical-damage` and
`suppressed` 100: inaccuracy 400% × 300% = **1200%**; burst wait 400% × 200% = **800%**; vision
10% × 0% = **0**; burst 10% × 0% = **0**; speed already 0.

### Vehicles

| Stat | Suppression (`^VehicleSuppressionEffects`, `vehicles.yaml:361-419`) | Damage (`^EffectsWhenDamagedVehicles`) | Collides? |
|---|---|---|---|
| Turret traverse | 85/70/55/40/25, 5 tiers `:369-383` | **0** below 50% `:290-292` | **YES** (and damage's 0 annihilates it) |
| Inaccuracy | 115→200 `:385-399` | — | suppression only |
| Burst wait | 105→150 `:401-415` | — | suppression only |
| Speed | — | 50 then 0 | damage only |
| Vision | — | — | **unclaimed** |
| Burst | — | — | **unclaimed** |

Vehicle suppression is capped at 50 (`ExternalCondition@VehicleSuppression`, `TotalCap: 50`,
`vehicles.yaml:362-366`), half the infantry cap of 100 (`infantry.yaml:429-433`).

### The unclaimed axes, across both families

`FirepowerMultiplier`, `DamageMultiplier` and `ReloadDelayMultiplier` are used in the mod **only** by the
veterancy ladder (`defaults.yaml:312-359`), by indestructible trees (`decoration.yaml:153`), and by two
aircraft crash gates (`aircraft.yaml:349`, `:360`). Neither suppression nor damage touches any of them.
Weapon range is untouched by both. Infantry turret traverse is untouched by both, even though infantry do
carry a turret (`InfantryStates : Turreted`, `InfantryStates.cs:~62`).

**These are the axes a new quantity could take without colliding with anything.**

---

## 4. Everything that reads `suppressed` (Q4)

### In `mods/` — 162 lines mention the string; the mechanical consumers are:

**Infantry** (`^SuppressionEffects`, `infantry.yaml:422-433`, inherited by `^Infantry` at `:15`):
declaration `ExternalCondition@Suppression`, `TotalCap: 100`, `ReduceTicks: 5`, `ReduceAmount: 1`
(`:429-433`), plus five inherited ladders of 10 tiers each — speed `:434-464`, vision `:466-494`,
burst `:496-525`, burst wait `:527-556`, inaccuracy `:558-587` — plus `^SuppressionPips`, 10 decorations
`:589-670`. **50 modifier traits + 10 pips.**

**Vehicles** (`^VehicleSuppressionEffects`, `vehicles.yaml:361-419`, inherited by `^Vehicle` at `:19`):
declaration `TotalCap: 50`, `ReduceTicks: 3`, `ReduceAmount: 1` (`:362-366`), plus turret `:369-383`,
inaccuracy `:385-399`, burst wait `:401-415`. **15 modifier traits + the shared pips.**

**Threshold reads outside the ladders — only four:**

| Site | Threshold |
|---|---|
| `infantry.yaml:317` | `InfantryStates.ProneCondition: … \|\| suppressed > 30 \|\| …` |
| `infantry.yaml:342` | same, amphibious variant |
| `infantry.yaml:1771` | `^AT` Armament `PauseOnCondition: !ammo-primary \|\| suppressed >= 10` |
| `infantry.yaml:1991` | engineer repair Armament `PauseOnCondition: suppressed >= 10` |

There is a load-bearing PITFALL at `infantry.yaml:2360-2371`: a fifth such gate was removed from the
medic, because `suppressed >= 10` stopped him treating anyone under any fire at all.

**Weapon-side grants:** `weapons-effects.yaml` carries 74 `GrantExternalCondition` warheads, of which
**56 target `Infantry`** and **13 target `Vehicle`**, spread over four templates —
`^LargeCaliberEffects:151-165` (amounts 8/4/2), `^MediumSuppressionEffects:308-326` (12/6/3),
`^LargeSuppressionEffects:371-389` (18/10/5), `^HugeSuppressionEffects:434-458` (25/15/8/3). Those four
reach real weapons: `^12.7mm` (`weapons-ballistics.yaml:376-377`) and the medium/large/huge explosion
families, which have 1, 10, 3 and 4 inheritors respectively. **Vehicle suppression is live, not
decorative** — worth stating because the infantry-only warheads outnumber it 4:1.

### In engine C# — the token name appears as a default in exactly three `Info` fields

| Site | Field | Thresholds |
|---|---|---|
| `Traits/Garrison/GarrisonManager.cs:92` | `SuppressionCondition = "suppressed"` | `SuppressionRecallThreshold = 60`, `SuppressionRedeployThreshold = 30`, `SuppressionLockoutTicks = 50` (`:100-112`) |
| `Traits/StancePositioningExecutor.cs:112` | `SuppressionVariable = "suppressed"` | `MaxSuppressionToMove = 30` (`:108`) |
| `Traits/BotModules/PoiOffensiveBotModule.cs:468` | `SuppressionCondition = "suppressed"` | `PrepSuppressionRadius = 5` cells (`:464`) |

`Render/WithGarrisonDecoration.cs:111` reads it through `GarrisonManager`'s field rather than restating it.
The ~80 other engine hits for "suppress" are the ordinary English word in comments (e.g.
`SmartMoveActivity.cs:136`, `MovementModifierMath.cs:24`) and are not consumers.

`GarrisonManager.cs:94-99` carries a PITFALL warning against adding a *second* suppression penalty at the
garrison, precisely because the soldier's own ladder already applies — the double-application hazard is
already known in this codebase.

### Is a rename mechanical or semantic?

**Mechanical, but not a find/replace.** The token is a plain string in three engine defaults and a YAML
identifier everywhere else; nothing derives meaning from the spelling. Four things make it more than a
sed:

1. The three engine defaults must move together. `PoiOffensiveBotModule.cs:465-467` says so in its own
   `[Desc]`: *"Mirrors GarrisonManager.SuppressionCondition — keep the two in sync if the mechanic is renamed."*
2. `engine/OpenRA.Test/OpenRA.Mods.Common/SuppressionMathTest.cs` hardcodes the entire infantry tier table
   as expected values (`:29-41` onward) and would need to move with the YAML.
3. **Autotest scenarios re-declare the token by name** — 8 scenario `rules.yaml`/`map.yaml` files and 13
   scenario `.lua` files reference it. The YAML half would be caught by `.\make.ps1 test` (which lints
   scenarios by name), but **the Lua half would not** — `CheckLuaScript` cannot see a token rename.
4. `^Combatant` (`defaults.yaml:81-86`) records the general trap: deleting the only granter of a token
   while consumers remain is a CheckConditions **error** across every inheritor. A half-done rename fails
   loudly rather than silently, which is the good case.

Introducing `impediment` *alongside* `suppressed` is the more expensive option, not the cheaper one — see §7.

---

## 5. The vehicle crew system already triples (Q5)

`^CrewedVehicle2` (`vehicles.yaml:307-321`):

| Trait | Modifier | Condition |
|---|---|---|
| `SpeedMultiplier@NoDriver` | 0 | `!has-driver` `:310-312` |
| `TurretTurnSpeedMultiplier@NoGunner` | 0 | `!has-gunner` `:313-315` |
| `Explodes@CrewCookoff` | — | on death `:318-321` |

`^CrewedVehicle3` (`vehicles.yaml:323-352`):

| Trait | Modifier | Condition |
|---|---|---|
| `SpeedMultiplier@NoDriver` | 0 | `!has-driver && !has-commander` `:325-327` |
| `SpeedMultiplier@CommanderDrives` | 40 | `!has-driver && has-commander` `:329-331` |
| `TurretTurnSpeedMultiplier@NoGunner` | 0 | `!has-gunner && !has-commander` `:333-335` |
| `TurretTurnSpeedMultiplier@CommanderGuns` | 50 | `!has-gunner && has-commander` `:337-339` |
| `InaccuracyMultiplier@CommanderGuns` | 200 | `!has-gunner && has-commander` `:341-343` |
| `InaccuracyMultiplier@NoCommander` | 150 | `!has-commander` `:345-347` |

21 actors carry `VehicleCrew`; 13 vehicles inherit one of the two templates
(`vehicles-america.yaml:5,181,301,467,588,716,877`; `vehicles-russia.yaml:5,129,292,409,527,694,826`).

### It overlaps the damage ladder, and the header comment is wrong about it

`vehicles.yaml:304-306` says these templates *"Replace blanket SpeedMultiplier@CriticalDamage /
TurretTurnSpeedMultiplier@CriticalDamage"*.

**They do not replace anything.** I grepped `mods/` and `tools/` for `-SpeedMultiplier@…`,
`-TurretTurnSpeedMultiplier@…` and `-InaccuracyMultiplier@…` removals: **there are none anywhere in the
repository.** The only `-Trait:` removals in the mod are `GrantConditionOnDamageState` ones
(`civilian.yaml:11,453-459`, `structures.yaml:111`). The crew traits use different `@`-keys (`@NoDriver`,
`@NoGunner`) from the damage traits (`@CriticalDamage`, `@HeavyDamage`), so MiniYaml keeps **both sets**,
and both multiply through `Util.ApplyPercentageModifiers`.

**This matters more than it looks.** The two systems are not merely coexisting — they fire on the *same
HP crossing*. `VehicleCrew.cs:52-55` sets `EjectionDamageState = DamageState.Heavy`, i.e. crew start
bailing below 50% HP, which is exactly where `SpeedMultiplier@HeavyDamage` (50) and
`TurretTurnSpeedMultiplier@CriticalDamage` (0) fire. Crew loss is not an independent axis; it is a
second, delayed expression of the same 50% line.

Concretely, an `abrams` below 50% HP that has lost its gunner today has turret traverse
`0% (damage) × 0% (crew)`, and speed `50% (damage) × 40% or 0% (crew)`.

**So the answer to "would it triple up" is: it is already a double, and a new quantity would make it a
triple** on turret traverse and speed for the 13 crewed vehicles. The report that `^CrewedVehicle2` has no
substitution in either direction is **confirmed** — and it is true of `^CrewedVehicle3` as well.

Anyone sizing the coupling off that header comment will under-count by one multiplier. The comment is
verifiably wrong and would be worth correcting on sight under the knowledge-bank rules — I have not
touched it, since this brief forbids YAML edits.

---

## 6. Prone's inversion already fires today (Q7)

**Yes, a badly damaged infantryman is already harder to see — on the shipped build, with no new coupling.**

The chain, end to end:

1. `GrantCondition@HeavyDamageProne` grants `prone` whenever `heavy-damage-attained` holds, i.e. **below
   50% HP** — `infantry.yaml:1088-1090`. It sits in `^EffectsWhenDamagedInfantry`, which **all** `^Infantry`
   inherit (`:13`), including families that carry no `InfantryStates` at all.
2. `InfantryStates.ProneCondition` independently includes `|| critical-damage`, so below 25% it is granted
   a second time (`:317`, amphibious `:342`).
3. `DetectableAddativeModifier@Prone` adds `VisionModifier: 1` while `prone` holds — `infantry.yaml:789-791`,
   inside `^DetectableInfantryStandard` (`:776`), inherited by `^Infantry` at `:21`.
4. `Detectable.Tick` computes `ClampConcealment(Util.ApplyAddativeModifiers(DetectableInfo.Vision, …))`
   (`Detectable.cs:139-142`); `ApplyAddativeModifiers` sums (`Util.cs:248-256`); and
   `DetectableAddativeModifierInfo`'s own `[Desc]` is *"Modifies the required vision to see this actor"*
   (`DetectableAddativeModifier.cs:15-20`). **+1 means a stronger observer is required. Higher is
   stealthier.**

`PanicGateAtCriticalDamageTest.cs:164-171` pins step 1 deliberately, requiring that `prone` be *granted by
the damage state* rather than merely reachable through `InfantryStates` — so this is intended plumbing,
not an accident. Whether the **concealment consequence** was intended is a different question and the tree
does not answer it.

### It does not stack with suppression's prone

Both `suppressed > 30` and `heavy-damage-attained` grant the same `prone` token, but the consumer is a
single `ConditionalTrait` keyed on that condition — enabled while the count is ≥ 1. A unit that is both
suppressed and badly hurt gets **+1 total, not +2**. So a new quantity feeding prone from a third source
would not deepen the concealment bonus; it would only widen the circumstances in which the existing +1
applies.

### The inversion is broader than concealment

While `prone` holds, infantry also get a **smaller hitbox**: `HitShape@Cover` radius 20 vs
`HitShape@Standing` radius 30 (`infantry.yaml:144-151`, both on `^Infantry`). So a wounded man is harder
to *hit* as well as harder to *see*.

**But the damage-reduction half does not fire from damage.** `InfantryStates.ProneSpeedModifier` (60) and
`ProneDamageModifiers` (`infantry.yaml:319-325`) key off the trait's internal `IsProne` flag, not the
condition: `ISpeedModifier.GetSpeedModifier` tests `if (IsProne)` (`InfantryStates.cs:182-190`),
`IDamageModifier.GetDamageModifier` tests `if (!IsProne)` (`:195-203`), and `IsProne` is set **only** by
`ProneTraitEnabled`/`ProneTraitDisabled` (`:207-222`), which are driven by the trait's own `ProneCondition`.

So in the 25–50% band, a *moving, unsuppressed* wounded soldier holds the `prone` **token** — getting
concealment +1 and the small hitbox — while `IsProne` is false, so he gets no prone damage reduction, no
prone speed modifier, and (via `IRenderInfantrySequenceModifier`, `:146-150`) **renders standing up**.
Below 25% `critical-damage` enters `ProneCondition` and the two re-converge.

That token/flag split is the sharpest thing in this report after §1: a wounded unit is currently concealed
and small-hitboxed *while drawn standing*, in a 25-point HP band, and no test covers it.

---

## 7. What the codebase affords for a new quantity (Q6)

**Not a design. What is cheap, what is expensive, and one trap.**

### There is no aggregate scalar, and the idiom is explicitly the opposite

Both existing ladders are built the same way: one trait grants a stacking integer token, then N sibling
traits each carry `RequiresCondition: <token> > A && <token> <= B` and a flat `Modifier`. That is 50
traits for infantry suppression, 15 for vehicle suppression, 22 for the infantry damage ladder. Nothing
in the engine sums or composes conditions — I listed
`engine/OpenRA.Mods.Common/Traits/Conditions/` in full and there is no aggregator among the 41 files.
A ladder mirroring the existing ones is what the codebase makes *easy*, because it is the only shape that
already exists.

### The closest existing affordance: `GrantStackingConditionOnHealthFraction`

`engine/OpenRA.Mods.Common/Traits/GrantStackingConditionOnHealthFraction.cs` **already converts HP
fraction into an integer stack count of a named condition** — linearly from 1 stack at `StartFraction` to
`MaxStacks` at `EndFraction`, with a configurable `Interval`. Pure math is factored out into a testable
`CalculateStacks` (`:84-99`) and is covered by `GrantStackingConditionOnHealthFractionTest.cs`.

It ships and is in use twice, both driving the `onfire` overlay: `vehicles.yaml:217-222` and
`aircraft.yaml:281-286`, both `StartFraction: 50, EndFraction: 0, MaxStacks: 10, Interval: 15`.

This is the one piece of "damage → graded scalar" machinery that already exists, and it needs **no new
C#** to point at a different condition.

### The trap, and it is a real one

`ReleaseTo` calls `self.GrantCondition(Info.Condition)` **directly** (`:101-114`). It does **not** route
through `ExternalCondition`.

Therefore neither `TotalCap` (`ExternalCondition.cs:35-36`) nor the decay loop
(`ReduceTicks`/`ReduceAmount`, `ExternalCondition.cs:204-235`) applies to the stacks it grants — the decay
tick iterates only `ExternalCondition`'s own `permanentTokens`/`timedTokens` and calls
`TryRevokeCondition` on those.

Consequence, if such a trait were pointed at `suppressed`: the health-driven stacks would be **uncapped by
`TotalCap: 100` and would never decay**, and they would sit on top of warhead-granted suppression —
permanently pinning a damaged unit near the top of a ladder designed around a decaying input. A unit at
20% HP would be stuck at maximum impediment forever, with the pips showing it.

This is easy to get wrong because the trait's own `[Desc]` reads *"Pair with an ExternalCondition trait
declaring the same Condition with TotalCap >= MaxStacks so the condition is registered and stackable"*
(`:17-22`) — which sounds like the cap governs it. It does not; the pairing is about **declaring** the
condition name, not routing grants through it. The existing `onfire` use is safe precisely because
`onfire` has no decay configured.

### The other two shapes, and their real costs

- **`GrantConditionOnHealth`** (`Traits/Conditions/GrantConditionOnHealth.cs`) grants on an `MinHP`/`MaxHP`
  window. It is a threshold, not a scalar. **It is used zero times in the mod** — an entirely unused
  affordance.
- **A separate `impediment` token alongside `suppressed`** is the expensive option, because *suppression
  has no way to feed it*. Nothing in the engine reads one condition's count and grants another. Making
  suppression feed a new quantity means either retargeting the **69 suppression warheads** in
  `weapons-effects.yaml` (plus the sites in `weapons-other.yaml`) to the new token, or writing a new C#
  trait. That is the cost driver, and it is why **renaming `suppressed` is materially cheaper than
  introducing `impediment` beside it** — the rename inherits all 69 grant sites for free.

### What the code makes hard, stated plainly

There is no mechanism for "two inputs, one scalar" short of new C#. Every existing multi-input effect in
this mod (prone, panic, onfire) works by having **multiple independent granters of the same token** —
which is exactly the user's model, and the codebase does support *that* directly. What it does not support
is weighting or combining the inputs before they land.

---

## 8. What reading could not settle

- **The exact number of actors the lint warns on.** I could not run the gate (a merge gate was live, and
  the brief forbids it). The structural claim in §1 — every actor descending from the five `^DamageStates`
  templates grants both tokens, nothing consumes either — is firmer than a count, but if the manager wants
  the integer, one `.\make.ps1 test` run produces it.
- **Whether the aircraft accuracy/burst-wait inversion in §2 is deliberate.** The values are certain; the
  intent is not. No comment at `aircraft.yaml:399-418` explains the direction, and I found no commit
  reference to it. Do not assert it is a bug to the user on my authority.
- **Whether the two dead tokens were meant to seed a vehicle light/medium ladder.** Marked INFERENCE in §1.
  Plausible from the asymmetry, unprovable from the tree.
- **Runtime confirmation of the §6 token/flag split.** The claim that a moving, unsuppressed soldier in the
  25–50% band is concealed while rendering standing is traced through `InfantryStates.cs:146-222` and the
  YAML, and each link is solid — but I never launched the game, so it is a code reading, not an observation.
  It is testable with a scenario if the manager wants it pinned before any design rests on it.
- **Non-Windows gate behaviour.** I did not verify whether Linux `make test` or bare `--check-yaml` treat
  these warnings the same way as the Windows gate.
