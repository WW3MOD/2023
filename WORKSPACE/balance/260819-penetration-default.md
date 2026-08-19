# WW3MOD — is `Penetration: 1` a balance problem?

Chasing the lead recorded in [`260819-strike-shorad-parity.md`](260819-strike-shorad-parity.md):
> `Penetration` defaults to 1, and 166 of 236 resolved damage warheads carry that default.

Static analysis only, read from `main` @ `8c8fd25a` (worktree `wt/penetration`).
**No game was launched. No unit or weapon stat was changed.**
Reproduce with `python3 tools/combat-sim/scripts/penetration-sweep.py`.

---

## Verdict

**Red herring for shipped balance — but the headline number is wrong, and the
reason it is a red herring is not the reason you would guess.**

Three things, in order of how much they should change anyone's behaviour:

1. **The count is `167 of 238`, not `166 of 236`.** The published figure
   reproduces neither the current dump nor the one it was taken from. §2.
2. **The count is the wrong metric anyway.** Of those 167, exactly **26** sit on
   a weapon an obtainable unit fires *and* can ever reach the penetration branch
   at all. Of those 26, **20** are the deliberate `Spread`/`Target` idiom and
   **6** are targeters, jammers and repair beams with damage 0-50. **Zero new
   defects.** §3.
3. **A well-meaning bulk "fix" of the 167 would be a silent mod-wide buff** of
   +15-20% damage against every armoured target. This is the real risk the
   finding creates, and it is a risk of *acting* on it. §5.

The one live defect in this area — the Iskander/HIMARS designators — is already
filed as **D2** in [`../audit/260816-weapons-armour-matrix-defects.md`](../audit/260816-weapons-armour-matrix-defects.md).

**Recommended action: none to the YAML.** See §8 for the ranked options; option A
is "correct the two documents and stop."

---

## 1. What `Penetration` actually does

`DamageWarhead.InflictDamage`, [`DamageWarhead.cs:216-231`](../../engine/OpenRA.Mods.Common/Warheads/DamageWarhead.cs):

```csharp
var thickness = victim.Trait<Armor>().Info.Thickness;
if (thickness != 0)
{
    var armorPercent = ArmorDirectionPercent(victim, shape, args);
    thickness = thickness * armorPercent / 100;
    var diff = Penetration - thickness;
    if (diff < 0)
        damage = damage * Penetration / thickness;   // int division
}
```

It is **a linear scale, not a gate and not a threshold** — this matters, because
the three readings give opposite conclusions and the brief flagged the ambiguity:

- Not a gate: penetrating does not grant a bonus. `diff >= 0` simply skips the
  reduction, so *any* penetration ≥ thickness yields identical full damage. The
  `// TODO: damage more when penetrating?` on line 230 is still open.
- Not a threshold: falling short does not zero the damage, it prorates it.
- **It scales**: `damage × Penetration ÷ thickness`, in C# integer arithmetic.
  At `Penetration: 1` against an Abrams (`Thickness: 700`) a warhead delivers
  `raw/700` — 0.14%, and truncation eats the remainder.

Four properties worth knowing, none of them in the reference docs:

| | |
|---|---|
| **`Thickness: 0` skips the block entirely** | 297 of 358 actors, including *all infantry*. Against them `Penetration` is a literal no-op at any value. This is the single most important fact in the whole question. |
| **It stacks with `Versus`, it does not replace it** | `Versus` is applied afterwards via `ApplyPercentageModifiers` (line 236). Two independent armour systems compose. |
| **Direction is folded into the *thickness*, not the damage** | `Distribution` = {front, side, rear, top, bottom} percentages (20 actors carry one). A glancing facing lowers effective thickness, which *raises* the penetration ratio. |
| **Line 216 reads a possibly-disabled trait** | `victim.Trait<Armor>()` ignores `IsTraitDisabled`, while `ArmorDirectionPercent` (line 126) respects it. Harmless today — no `Armor` in the mod carries a `RequiresCondition`, independently re-confirmed for this audit — but the two lines disagree about the same trait. |

## 2. The count, re-derived

`tools/combat-sim/data/stats.json` is a faithful, complete, current dump of the
resolved ruleset: `DumpBalanceJsonCommand` iterates `rules.Weapons` with no
filter, its 165 keys set-difference to zero against the 165 top-level entries in
the 7 files listed under `Weapons:` in `mod.yaml`, and no weapon or armour YAML
has changed since `generated_at`.

| population | pen=1 | total | share |
|---|--:|--:|--:|
| every damage warhead in the dump | 167 | 238 | 70.2% |
| concrete weapons only (`^` templates dropped) | 145 | 208 | 69.7% |
| fired by some unit's armament | 63 | 110 | 57.3% |
| fired by a unit a player can obtain | 57 | 103 | 55.3% |

**`166 of 236` matches none of these.** The 166 is a stale numerator from the
2026-08-15 dump (`4ba14a7d`: 164 weapons / 235 warheads / 166 pen-1); the 236
matches neither that snapshot nor this one. The error is small and does not
change any conclusion — but it was carried into a document as a verified count,
which is exactly the drift pattern this project keeps hitting.

## 3. The funnel — why 167 collapses to 0

```
   167  every damage warhead at Penetration 1
    57  ...on a weapon a unit a player can obtain actually fires
    26  ...that can ever reach the penetration branch
         (the other 31 list only target types belonging to Thickness-0 actors:
          Infantry, Mine, Heal, BuildingRepair — the default cannot bite)
    20  ...are the Warhead@Spread sibling of a Warhead@Target that DOES penetrate
     6  ...stand alone: dronejammer (dmg 3), dronetargeter (0), repair (0),
         flamespray (10), himarstargeter (50), iskandertargeter (50)
     0  ...are undiscovered defects. The two targeters are already filed as D2.
```

The reducibility test derives from real `Targetable.TargetTypes` resolved
through `Inherits`, not from the armour *type* string — that distinction caught
two actors that a naive test misses: `gtwr` is `Thickness: 25` and targetable as
`Unarmored`, and `quadcopterdrone` is `Thickness: 3` and targetable as `Drone`.
`gtwr` is `~disabled`; `quadcopterdrone` is live, which is why `dronejammer`
appears in the six rather than the 31.

## 4. Deliberate or inherited?

**Deliberate, and better evidenced than the prior audit was able to claim.**

`Penetration` became settable on **2023-05-15** (`c946ceae`, "Armor
penetration/thickness added, not implemented fully"). That commit set 17 values
in one pass as a coherent calibre ladder — 9mm 3, 5.56 4, 7.62 5, 12.7 15, 30mm
60-70, tank round 800, atomic 5000 — followed by three years of one-and-two-line
incident-driven tuning across 19 commits. 40 explicit value lines across 26
weapons in 4 of the 7 live weapon files; ~25 more concrete weapons inherit a
value through `Inherits@Caliber: ^30mm` and friends.

The [260816 matrix audit](../audit/260816-weapons-armour-matrix.md) inferred the
`Spread`/`Target` split was deliberate but listed that inference as its largest
caveat: *"That is inference from consistency, not from a design document or a
comment."* **That caveat can now be retired for at least the missile family.**
`rules/weapons/weapons-missiles.yaml:281-292` is a nine-line comment reasoning
explicitly about this exact mechanic, and its last clause is decisive:

> Setting Pen 20 matches Heavy heli thickness so near-misses do meaningful
> damage (~500-1000) **without significantly buffing splash vs Heavy tanks
> (Thickness 280-700, still ~7% effective).**

Splash staying near-useless against heavy armour is stated as a *desired
property*. `weapons-ballistics.yaml:306` reasons the same way for the
littlebird's AA mount ("Damage 150 / Penetration 4 — vs Heavy heli Thickness 20
that is 20%"). The idiom is designed, not inherited.

What git cannot tell you: for the ~107 warheads with no value, "no evidence of a
decision" is not "nobody decided". That distinction is unresolvable from history.

## 5. Observable consequence — and what a bulk fix would cost

The honest answer to "what is the observable consequence?" is **nothing you can
see today**. Splash is near-harmless to armour, aimed warheads penetrate, and
infantry are unaffected at any value. The consequence worth stating loudly is
what happens if someone *acts* on the 167. Giving each pen-1 warhead its own
weapon's main penetration — the obvious reading of "set it deliberately" —
yields total per-shot damage:

| weapon | vs abrams | vs t90 | vs bradley |
|---|---|---|---|
| `tankround.abrams` | 20004 → **23000** | 20010 → **23000** | 20200 → **23000** |
| `artilleryround.paladin` | 15004 → **18000** | 15010 → **18000** | 15200 → **18000** |
| `wgm.bradley` | 10002 → **12000** | 10007 → **12000** | 10133 → **12000** |
| `atgm` | 1430 → **1713** | 3578 → **4285** | 10133 → **12000** |
| `rpg` | 4286 → **4856** | 6002 → **6800** | 6053 → **6800** |
| `25mm.bradley` | 42 → **50** | 107 → **128** | 506 → **600** |

Bounded at **+15-20%** on the big anti-tank weapons, because the penetrating
`Target` warhead already carries most of the payload. Not catastrophic — and
that is precisely what makes it dangerous. A mod-wide 15-20% buff to every
armoured engagement, applied by accident, is large enough to invalidate every
existing balance conclusion and small enough that nobody would notice it landing.

## 6. Latent hazard: every under-penetrating AA weapon is behind `~disabled`

Every **shipped** AA weapon has a penetration matched to aircraft thickness (max
20 in this mod). Every AA weapon left at the default sits on a `~disabled`
actor. Effective damage / shots-to-kill:

| weapon | carrier | avail | pen | heli (t20) | hind (t10) | mig (t3) |
|---|---|---|--:|---|---|---|
| `stinger.quad` | strykershorad | SHIPPED | 20 | 5000/1 | 5000/1 | 5000/1 |
| `9m311` | tunguska | SHIPPED | 20 | 5000/1 | 5000/1 | 5000/1 |
| `30mm.tunguska.aa` | tunguska | SHIPPED | 70 | 1000/1 | 1000/1 | 1000/1 |
| `12.7mm.hind.aa` | hind | SHIPPED | 5 | 75/11 | 150/6 | 300/2 |
| `manpad` | aa | `~disabled` | 15 | 2250/1 | 3000/1 | 3000/1 |
| `surfacetoairmissile.double` | sam, hsam | `~disabled` | **1** | **125/7** | 250/4 | 833/1 |
| `airtoairmissile` | f16, mig | `~disabled` | **1** | **75/11** | 150/6 | 500/2 |
| `aacannon` | agun | `~disabled` | **1** | **5/160** | 10/80 | 33/17 |

`12.7mm.hind.aa` at pen 5 is the deliberate "machine gun vs helicopter" value
documented alongside the littlebird's pen 4, not an oversight.

**The hazard is forward-looking, and confirmed latent:** none of
`sam`/`hsam`/`agun`/`cram`/`f16`/`mig` is referenced by any of the 10 shipped
`map.yaml` files, so none reaches a player today. (Control: the same grep finds
41 references to `abrams`/`t90`/`mpspawn`, so the zero is real, not a bad path.)

`aa` (MANPAD infantry, 300cr), `sam`/`hsam` and `f16`/`mig` are plausible
re-enables. Whoever flips `~disabled` on them ships a SAM site that needs 7 hits
to down an Apache and an AA gun that needs 160. This is worth a line in whatever
gates re-enabling content; it is not worth a YAML change now.

## 7. One *deliberate* value worth a second look

Not a default, so out of scope for this audit, but it fell out of the same
table. `ATGM`'s main warhead is `Penetration: 100` — chosen — which is below
every MBT's thickness. Effective damage / shots-to-kill, front facing:

| weapon | pen | vs bradley | vs bmp2 | vs t90 (t280) | vs abrams (t700) |
|---|--:|---|---|---|---|
| `atgm` (AT infantry) | 100 | 10000/2 | 10000/2 | 3571/**7** | 1428/**20** |
| `rpg` | 500 | 6000/3 | 6000/3 | 6000/4 | 4285/7 |
| `hellfire` | 800 | 10000/2 | 10000/2 | 10000/3 | 10000/3 |

The dedicated AT infantry team needs **20 hits** on an Abrams where the RPG
needs 7 and a Hellfire needs 3. `DOCS/archive/MISSILES.md:134-188` already argues
this value is too low. Flagging only; no change proposed.

## 8. Ranked options

**A. Correct the record; change no YAML.** *(recommended)*
Fix `166 of 236` → `167 of 238` in `260819-strike-shorad-parity.md`, retire the
260816 audit's "inference, not a comment" caveat by citing
`weapons-missiles.yaml:281-292`, and add the §6 hazard to the re-enable
checklist. Cost: minutes. Risk: none. This is the whole actionable content.

**B. Additionally, promote the mechanic to `DOCS/reference/`.**
The `Thickness: 0` skip, the `Versus` composition and the "scale, not gate"
reading are load-bearing and currently live only in audit docs. Via
`WORKSPACE/DISCOVERIES.md` per the knowledge-bank rule. Cost: small. Risk: none.

**C. Set `Penetration` explicitly on the 20 `Warhead@Spread` siblings — to the
value they already behave as (1).** Makes the deliberate idiom self-documenting
so the next reader does not re-open this. Purely cosmetic: zero behavioural
change by construction. Cost: 20 YAML lines. Risk: touches weapon files for no
functional gain, and `lint-baseline` churn. **Needs user sign-off (stat file).**

**D. Fix the two targeters.** Already filed as D2; belongs to that workstream,
not this one. Listed only so it is not double-counted.

**E. Bulk-set penetration on all 167.** **Do not.** §5 prices it: a silent
+15-20% mod-wide buff to armoured combat. Listed to be explicitly rejected,
because it is the obvious reading of the original lead.

---

## What I did not verify

- **Nothing was run in-engine.** Every number is static: the resolved dump plus
  the formula read off `DamageWarhead.cs`. `args.DamageModifiers` (veterancy,
  suppression), `DamageAtMaxRange` falloff and `SpreadDamage` distance falloff
  are all applied *outside* the penetration block and are **not** in these
  tables. Shots-to-kill figures are point-blank, full-falloff, front-facing —
  they are upper bounds on lethality, not predictions.
- **`tools/combat-sim/scripts/aa-pair-effective-dps.py` models aircraft as
  `thickness 0`** (`classes` table, lines 60-61: `("helicopter", 0, {"Air"})`
  and `("fixed-wing air", 0, {"Air"})`). Real aircraft are Thickness 3-20, so
  every AA damage figure that script produced is inflated — up to 20× for
  `Penetration: 1` weapons, and it is why an audit that had this mechanic in
  hand still did not see §6. I did not fix it: it is a prior audit's
  reproducibility surface and the call is the user's. **This is the most likely
  thing in this area to still be wrong somewhere.**
- **"Deliberate" for the 20 siblings rests on two YAML comments** covering the
  missile and ballistics families. I extended the prior audit's inference with
  real evidence; I did not turn it into proof for all 20.
- The `Trait<Armor>()` / `IsTraitDisabled` disagreement in §1 is reported from
  reading, not from a test that makes it bite.
