# Heli lift fix scope: shared code directly, byte-identity waived

_Recorded 2026-08-06T04:47:37.829Z by be958765_

**Question:** heli lift missions have never functioned — the `Actor.IsIdle` misuse gates lift demand at four UNGATED shared-code sites (`HelicopterSquadBotModule.cs:965-976`, `UnitBuilderBotModule.cs:512`/`:517`, `:1353`, `:623`). Options: (a) flag-gated @experimental-only, (b) park as pipeline item, (c) fix shared code directly.

**Decision (user, 2026-08-06):** option (c) — fix the shared code directly. User notes verbatim: "I dont care if stable gets some upgrades as well, lets just keep it simple. Dont get the byte-identity thing, let me know if it is a big issue or just solve it for me however you think is best."

**Why (c) over (a):** the user explicitly waived the @stable byte-identity concern and asked for simple. The invariant's remaining value was benchmark comparability of the @stable control arm — already lost at the composition-baseline merge (`2eb79262`, AddToArmyValue feeds win-rule scoring → pre/post ladder scores non-comparable). A default-false flag would add plumbing with no remaining payoff. Precedent: AutoSeekSupplies default-ON (`f15cfbde`) already shipped a user-approved all-bots behavior change.

**Binding constraint carried into the item:** the `:517` idle-transport count MUST be fixed in the same change (churn-risk finding recorded in DISCOVERIES at the `c89d20bb` merge) — otherwise the now-live evac rebuy-loops once missions launch. Adversarial review is NOT optional for this lane (@stable behavior changes).

**Recorded as PIPELINE item 33 (queue top, NEXT TO START), committed to the repo so the other machine's manager inherits it via git pull. Execution venue: the OTHER machine — Mac spend stays paused per the 2026-08-06 wind-down directive.**
