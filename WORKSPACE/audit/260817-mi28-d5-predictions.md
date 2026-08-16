# Predictions registered before verifying — Mi-28 fix + D5 reachability

Written at `wt/mi28` off `main` @ `2a9eb77d`, **before** running any check. Outcomes are filled in
below the table once verified; wrong ones stay in place.

## D5 — are `SAM`, `HSAM`, `AGUN`, `F16`, `MIG` reachable?

| # | prediction | confidence |
|---|---|--:|
| P1 | All five stay unreachable — the `~disabled` prerequisite is a deliberate authoring act and holds against every path | 65 % |
| P2 | At least one appears in a map actor placement my `map.yaml` regex missed — most likely because maps ship as packed `.oramap` archives, or live outside `mods/ww3mod/maps/` | 40 % |
| P3 | `F16` and `MIG` **do** appear in a bot `UnitsToBuild` list — `CLAUDE.md` cites an "uppercase `A10/F16/…` in `UnitsToBuild`" bug, which is a direct tell that they are listed | 85 % |
| P4 | …but that does **not** make them reachable: `ProductionQueue` gates on `Buildable.Prerequisites` regardless of who is asking, so a bot cannot build past `~disabled` any more than a human can | 85 % |
| P5 | No Lua script or support power spawns any of the five | 75 % |
| P6 | Net: **D5 stays latent**, my original ranking holds | 60 % |

## D1 — what the Mi-28 fix should be

| # | prediction | confidence |
|---|---|--:|
| P7 | The author's intent was a split armament: `Ataka.AA` on a new `Armament@2_Air` named `secondary-air`, mirroring `HIND`'s `Armament@1_Air` — because the three surviving references already wire `secondary-air` into `AmmoPool@2` and the SACLOS slowdown | 80 % |
| P8 | …but the **design-consistent** answer is the opposite: the split exists in this mod only for *guns* (`12.7mm.Hind.AA`, `7.62mm.Minigun.AA`, `30mm.Tunguska.AA`), whose AA ceiling needs tuning apart from strafing. For a *missile* the counterpart does not split — `HELI` engages air with the same `Hellfire` it uses on ground. So adding `Air` to `Ataka.ValidTargets` and deleting the three dead references is more likely correct than defining a new weapon | 55 % |
| P9 | Aircraft `Thickness` is low enough (3–20) that any sane missile `Penetration` is already full damage, so penetration will **not** be the tuning lever here — damage and range will be | 85 % |
| P10 | `Ataka`'s existing `Warhead@Target` (10000 dmg) one-shots every helicopter in the game (300–800 HP), exactly as `Hellfire` already does, so mirroring the counterpart needs no new numbers at all | 70 % |
| P11 | `Ataka` is used by `MI28` alone, so changing it has no blast radius | 70 % |
| P12 | The Mi-28's `Ataka` projectile will need no guidance changes to track an airborne target — it is already a `Missile`, and `Hellfire` uses the same projectile family against air | 60 % |

## D3 — humvee duplicate `RenderSprites`

| # | prediction | confidence |
|---|---|--:|
| P13 | Deleting the **second** block and keeping the first changes behaviour (the resolved trait carries the *last* values at the *first* position), so the behaviour-neutral edit is to fold `Image: humvee` into the first block and delete the second | 90 % |
| P14 | `--resolved-rules humvee` before/after will be byte-identical | 80 % |

---

# Outcomes

## D5 — verdict: **latent confirmed, my ranking holds**, but the heuristic did have the hole I suspected

| # | outcome |
|---|---|
| P1 | **CORRECT.** All five unreachable. `~disabled` is a *hidden prerequisite*, not a negation: `TechTree.HasPrerequisites` strips `~` and then requires the player to **own** a prerequisite literally named `disabled` (`TechTree.cs:65-69`). Nothing anywhere provides one — the only `disabled` in the rules is a `PauseOnCondition`, a different namespace. So the gate can never open. |
| P2 | **CORRECT, wrong reason.** I guessed packed `.oramap` archives. The real gap was a whole **third `MapFolders` entry** I never scanned: `^EngineDir\|../tools/autotest/scenarios` (`mod.yaml:89-97`), 176 scenarios. `hsam` **is** placed there — `Actor2259` and `Actor5167` across 11 river-zeta-derived scenarios. (`.oramap` files do exist, but only under `engine/mods/cnc/`, a different mod ww3mod never loads.) |
| P3 | **CORRECT.** `mig: 30` sits in a bot `UnitsToBuild` (`rules/ai/ai.yaml:1658`), with a matching limit at `:1661`. |
| P4 | **CORRECT.** Bots go through the same `TechTree`; nothing bypasses it. A bot cannot build past `~disabled` any more than a human can — which is what makes the entry at `:1658` dead config rather than a live consumer. |
| P5 | **CORRECT.** No Lua spawn, and every `AirstrikePower` / `ParatroopersPower` in `player.yaml` is commented out. |
| P6 | **CORRECT.** D5 stays latent, below D2. |

**The finding P2 turned up does not move D5, and here is why it does not.** The `hsam` instances are
`Owner: Neutral`, and `Neutral` is `NonCombatant: True` — it has no enemies, so its `AutoTarget` never
acquires and `SurfaceToAirMissile.double` is never fired. They are scenery in a capturable-structures
test fixture. All 10 **shipped playable maps** place zero of the five actors. And map placement bypasses
`Buildable.Prerequisites` entirely, so had any of them been placed as a *combatant's* unit, D5 would have
been live — the mechanism was there, the instance was not.

**Correction owed to the matrix report:** its "map-placed" reachability tier was computed over
`mods/ww3mod/maps/**` only. That is one of three map folders. The conclusion survives; the method was
narrower than stated.

## D1 — verdict: **P7 right, P8 wrong, and P8 was wrong for the reason that mattered**

| # | outcome |
|---|---|
| P7 | **CORRECT** — a split armament is right, but not for the reason I gave (reference count). See below. |
| P8 | **WRONG.** I argued the `.AA` split is a *gun* idiom and a missile should just list `Air` like `Hellfire`. The gun/missile framing was surface pattern-matching. The split exists wherever the **air engagement needs different parameters**, and here it does: `Ataka` cruises at 100 and turns at 20 — a SACLOS ground profile — while helicopters cruise at 1560-2560 (`aircraft.yaml:398`, `aircraft-russia.yaml:471`). Adding `Air` to `Ataka` would have reused a flight profile aimed fifteen times lower than the target. |
| P9 | **CORRECT.** Helicopter `Thickness` is 3-20; `Ataka`'s inherited Pen 900/20 is full damage against all of them. Penetration was not the lever. |
| P10 | **CORRECT.** 10000 damage against 300-800 HP airframes one-shots, exactly as `Hellfire` already does — so no damage number had to be invented. |
| P11 | **CORRECT.** `Ataka` is referenced by `MI28` alone (`aircraft-russia.yaml:378`). Blast radius nil — and the fix leaves `Ataka` untouched regardless. |
| P12 | **WRONG.** I assumed the projectile would track air unchanged because it is already a `Missile`. It is the one thing that genuinely needed changing. |

**The near-miss worth recording.** P8 + P12 together would have produced exactly the failure the brief
warns about: a weapon that reads correct in the matrix, fires, and never connects. What caught it was
`Ataka`'s own source comments — a previous worker had already mapped the whole problem
(`Warhead@EffectAir` is annotated *"DORMANT… goes live the day Ataka is given Air or a higher cruise"*)
and deliberately stopped short of pulling the trigger. **Reading the comments around the thing I was
about to change was worth more than any amount of further static analysis.**

## D3 — both correct

| # | outcome |
|---|---|
| P13 | **CORRECT.** Folded the later block's `Image` into the earlier block's slot and deleted the later block. |
| P14 | **CORRECT.** `--resolved-rules humvee` before/after: 694 lines, byte-identical. The engine's own resolver also agreed with the Python mirror field-for-field (`XRayOverlayAlpha: 0.5, Scale: 0.9, Image: humvee`). |

---

# The granted autotest run — and why its verdict does not mean what I said it would

One run of `test-balance-heli-1v1`, granted explicitly. Verdict, read from `result.json` and not from a
pipe:

```json
{"name":"test-balance-heli-1v1","status":"pass",
 "notes":"WINNER=Apache | ttk=2.9s | survivors=1/1 | hp=800/800 (100%)",
 "seed":673102346}
```

**`hp=800/800` is the exact signature I named beforehand as "the fix looks right in YAML and does
nothing".** It is not, and the discriminator I proposed was unsound. I am recording it as a wrong call
because the reasoning error is the reusable part.

**Why the test cannot answer the question, by arithmetic I should have done before proposing it.** The
scenario spawns both helicopters **22 cells apart** — `Ataka`'s exact maximum range — and the duel is
decided by time of flight, not by whether either weapon works:

| | launch speed | accel | top speed | ≈ ticks to cross 22c0 | ≈ seconds @ 16.67 t/s |
|---|--:|--:|--:|--:|--:|
| `Hellfire` (Apache) | 100 | 30 | 500 | ~51 | **~3.1 s** |
| `Ataka.AA` (Mi-28) | 80 | 30 | 400 | ~61 | **~3.7 s** |

The Apache's kill landed at **2.9 s**. A Mi-28 missile fired on the very first tick could not have
arrived before ~3.7 s. So `hp=800/800` is produced identically by all three of:

1. the Mi-28 never acquired a target (fix broken),
2. it fired and its missile was still in the air when it died (fix fine, scenario too short),
3. it acquired late on an `AutoTarget` scan tick and never fired (fix fine, scenario too short).

Nothing in the artifacts separates them. `lua.log` carries
`heli 9 (not in world) is an invalid target for mi28 10 (not in world)` **in both directions** — including
the direction that demonstrably worked — so both `ForceEngage` orders failed on a spawn-timing artifact
and the duel was resolved by `AutoTarget`. That message is therefore not evidence of a targeting failure
either.

**What I should have proposed instead**, and what would settle it in one run:

- The same duel at **8-10 cells** rather than 22, so both missiles land well inside the deadline; or
- better and independent of balance entirely: assert on the **Mi-28's `secondary-ammo` count** at the end
  of the duel. Ammo consumed proves the armament was selected and fired, which is the whole question,
  and it is immune to flight time, to who wins, and to the first-mover artifact.

Either is a **scenario change**, not a re-run of this one. Not spending a second run on the existing
scenario: it would return the same 2.9 s verdict for the same structural reason.

**One observation held back deliberately.** The flight-time gap above is a *hypothesis about balance*
(the slower Russian missile loses the opening exchange at maximum range), generated from one
harness-deterministic duel whose own source comment warns that the first attacker wins 100 %-0 %. It is
not a finding and I have drawn no conclusion from it.

## Net verification status of the Mi-28 fix

| claim | status |
|---|---|
| `Ataka.AA` parses, inherits, and loads | **verified** — full ruleset load via `--resolved-rules`, plus `make all` clean |
| All three `secondary-air` references now resolve to a real armament | **verified** — engine's own `--resolved-rules MI28` prints `Armament@2_Air: Name: secondary-air, Weapon: Ataka.AA` |
| `Ataka.AA` is valid against airborne targets and `Ataka` still is not | **verified statically** — `ValidTargets: Air` on the weapon and `Ground, Water, Air` on both damage warheads; `Ataka` byte-unchanged |
| `Ataka` ground behaviour cannot have regressed | **verified** — not one line of it changed; the fix is purely additive |
| No engine regression | **verified** — NUnit 1511/1511, baseline held |
| **The Mi-28 actually FIRES at an airborne target in game** | **VERIFIED 2026-08-17** — `test-mi28-engages-air` passes: `Mi-28 spent a secondary missile at tick 19 (8 -> 7); Apache 800/800`. |
| **…and that missile HITS** | **still not verified** — deliberately outside this observable's scope, see below. |

---

# Closing run — `test-mi28-engages-air`

```json
{"name":"test-mi28-engages-air","status":"pass",
 "notes":"Mi-28 spent a secondary missile at tick 19 (8 -> 7); Apache 800/800",
 "seed":490865292}
```

**What this establishes.** `secondary-ammo` went 8 → 7 against a target that was airborne and nothing
else. `Ataka` cannot target an airborne actor and `30mm.Heli` is ground-only, so the spend is
attributable to `Ataka.AA` and to nothing else on the unit. The Mi-28 selects and fires its air mount.
Tick 19 against a ~500-tick deadline is a comfortable margin rather than a squeaker — consistent with
the force order at tick 1 plus AutoTarget's 3–8 tick scan.

**What it does NOT establish, and the note says so on purpose.** `Apache 800/800` is the target at full
health at the moment the assertion latched, and that is the expected reading rather than a problem: the
predicate fires on the ammo spend, and at 12 cells the missile is still in the air then. The observable
was chosen precisely to be independent of flight time, of who wins, and of the first-mover artifact —
which necessarily also makes it silent about whether the missile connects and damages. The hit stays
supported statically (inherited `Warhead@Target` 10000/Pen 900 and `Warhead@Spread` 2000/Pen 20, both
restated with `ValidTargets: Ground, Water, Air`, against helicopter `Thickness` 3–20; plus
`CruiseAltitude` and `HorizontalRateOfTurn` taken from Hellfire) and unmeasured. **Do not cite this pass
as evidence that the Mi-28 kills aircraft.**

## A correction to the reading order, earned the hard way by the run before this one

That run returned `HARNESS-ERROR (exit 3)`: another worker's `test-cargo-panel-full` held the
single-instance lock and my game never started — `run: (not started)`, and no run directory was created.
At that moment my privately poll-copied `lua.log` held **2422 bytes**, every line of it the other
worker's transport test (`[deliv] …`). So, correcting the rule I proposed and that was adopted:

- "NO-RESULT with an **empty** `lua.log` ⇒ plumbing" — **still correct**.
- "populated `lua.log` ⇒ my scenario ran" — **wrong, and dangerous.** `lua.log` is a fixed global path
  with no run identity. The 2026-08-15 entry covers staleness; *contention* is the sharper version,
  because the log can be someone else's while it is still being written. A populated log can belong to
  anyone.
- The trustworthy per-run artefacts are the **run directory and `result.json`**, which the runner prints
  as `Run dir:` and which cannot be another run's.

A 0-byte `lua.log` is likewise not a failure signal on its own: this passing run left one, because the
scenario prints nothing and `CombatProperties.Attack` logs only when `Target.IsValidFor` fails, which it
does not here since the order is delayed one tick. **Read the run directory first; use `lua.log` only
once you know it is yours.**

### Re-checked under the concurrent-truncation hazard — and it happened to these very artefacts

Raised 2026-08-17: `debug.log` is a single global path and a concurrent launch **truncates** it, so an
empty grep reads as "the event never happened" rather than "the file was erased". Re-verifying this
run's evidence by mtime rather than assuming:

| artefact | mtime | size | verdict |
|---|---|--:|---|
| `…/260817_012116_p26456_test-mi28-engages-air/result.json` | 01:21:44 | 186 B | **mine** — per-run path, pid in the name, mtime equals the timestamp inside the JSON |
| `Logs/lua.log` (secured copy) | 01:21:42 | 0 B | **mine** — inside my run window (launch ~01:21:16, verdict 01:21:44) and untouched since |
| `Logs/debug.log` (secured copy) | 01:21:44 | 2887 B | **mine** — and it opens with mod-load lines naming `worktrees/ww3mod/heli-assert/engine/…` |

**The truncation then demonstrably occurred.** A second worker launched
`wip-transport-delivers` at **01:24:46**. Checked immediately afterwards, the **live** `debug.log` read
**1807 B @ 01:24:52** — my run's 2887-byte log no longer exists on disk. It survives only because it had
been copied out minutes earlier. Had it been analysed in place, it would have been another worker's run.

**Consequences for the taxonomy, which now fails in BOTH directions under contention:**

- *Empty* is only evidence if the file is provably yours — the hazard as raised.
- *Populated* is only evidence if the file is provably yours — which the `HARNESS-ERROR` attempt above
  had already proved, with 2422 bytes of someone else's transport test sitting in `lua.log` while my
  game had not started.
- Unifying rule: **the global `Logs/` directory carries no run identity in either direction; only the
  per-run directory does.** Take the verdict from the per-run `result.json`, always.

**Cheapest positive fingerprint, specific to this project:** workers run from *different worktrees*, and
the engine's mod-load failure lines at the top of `debug.log` name the absolute engine path — here
`/Users/fredrik/worktrees/ww3mod/heli-assert/engine/…`. That is a free per-run signature no other
worker's log can carry. **Content attribution beats mtime attribution**, because mtime is circumstantial
while a worktree path is direct. Copy first, then attribute by path, then fall back to mtime.

**Nothing above needed retracting.** The PASS rests on a per-run `result.json`, and the two earlier
negatives rested on positive statements from the runner (`Required engine files not found`;
`run: (not started)`) plus filesystem facts (`engine/bin` missing; no run directory created) rather than
on an empty log.
