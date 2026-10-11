# Hunkering is the missing benefit half of two systems that already ship

_spec · status: draft · authored 2026-09-02T04:53:10.747Z_

Persisted on behalf of the ideas sub-manager (`ebd090f7`), whose session has no manager-row attachment and could not call `write_spec` itself. Design is its work; the citations are its workers'.

## The reframe

The user asked for "some kind of dodging system, where a soldier can take cover... the more they are hunkering the better they are protected, but they must emerge to fire." The archaeology says **this is not new machinery.** It is the missing *benefit* half of two systems that ship with only their penalty halves wired.

**Suppression** is already a 0–100 scalar bucketed into ten bands (`infantry.yaml:428-432`, `TotalCap: 100`, decaying 1 point per 5 ticks — a full decay is ~30 s at 16.67 tps). Each band already drives five effects: speed 90→0, vision 90→0, burst 90→0, plus burst-wait and inaccuracy climbing. At band 10 a soldier cannot move, cannot see and cannot fire. **Grepping the whole ladder for any damage or protection term returns zero.** Suppression is pure punishment with nothing given back.

**Prone is already automatic.** `infantry.yaml:316` reads `ProneCondition: deployed || suppressed > 30 || !moving || critical-damage`. It already costs speed (`:318`) and already shrinks detectability (`:771`). Its protection is declared in five tiers at `:319-324` — `Prone10Percent` through `Prone80Percent` — and applies only to warheads declaring a matching damage type, **which exactly one of the mod's 109 declarations does.** The protection ladder is built and inert.

Non-existence proof for the system as a named thing: case-insensitive grep for `takecover`, `take-cover`, `hunker`, `dodging` across `mods/` and `engine/OpenRA.Mods.Common/` returns **zero hits**.

## The control question: automatic, ordered, or stanced?

**Automatic, with stance-set depth and a momentary two-way override.**

This is not an invention and barely a choice. Automatic is not an available option to reject — prone already triggers off `suppressed > 30`, so "manual only" would be a change of direction rather than a starting point. `PIPELINE.md` item 71 records a user ruling that Take Cover must be **both automatic and a button**, which settles the axis outright.

Stance is where "how deep does my automatic response go" belongs. It is existing prior art and avoids inventing a fourth control surface. The button is a momentary override in **both** directions: force deeper through a barrage, or force *up* because those guns are needed right now.

## Four phases, each shippable alone

1. **Make suppression readable as a gradient with direction.** The pips exist but are ten discrete images, selection-gated — severity without trend. A player watching a squad wilt cannot tell whether it is recovering or two seconds from pinned. This is the same criticism already levelled at the garrison suppression row. **Changes no behaviour.**

2. **Give the ladder its benefit half — YAML only.** Make common infantry-killing warheads declare a `Prone*` damage type so the five existing tiers fire. No new code, no new UI, no new control surface. Highest value-per-risk item in the design. **It moves `@stable`**, so it must be declared in the commit message and the next benchmark baseline re-taken knowingly (per the standing rule in `CLAUDE.md`).

3. **The hunker axis** — player-controlled depth on the same band pattern. Where "must emerge to fire" lives. **The override button ships in this phase, not after it**, because a soldier who will not shoot when told to is the most infuriating thing an RTS can do.

4. **The emerge cycle** — pop up, fire, drop back. Deliberately last: most expensive, most likely to feel bad, and the only phase whose absence costs nothing.

## The pricing rule, and the risk it guards

Suppression already costs four things. Adding protection on the same axis risks making *being suppressed* strictly better in some fights — the opposite of the intent.

**Rule: the output loss must always outrun the protection gain.** Protection buys **time to withdraw**, never **a better trade**. Tunable after phase 2 against real numbers rather than argued now.

This is consistent with the user's standing preference — *"let the damage curve do the work: price a bad state, don't forcibly exit the player from it"* — and phase 2 is literally that sentence implemented, since it prices the state rather than adding an escape from it.

## Open

Phase 1's design depends on the parallel archaeology (`f8e293c4`) covering the `"!"` spotted indicator, the removed suppression pip and *why* it was removed, and what renders above a unit. The two efforts must converge on **one pip carrying levels**, not three pips — the user named that failure mode himself ("we can't just show everything or it will get messy fast") and it fails incrementally, each addition individually reasonable.
