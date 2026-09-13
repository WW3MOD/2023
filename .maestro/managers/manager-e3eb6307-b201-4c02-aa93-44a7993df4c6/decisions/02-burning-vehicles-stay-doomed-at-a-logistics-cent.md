# Burning vehicles stay doomed at a Logistics Centre — burn and repair rates unchanged

_Recorded 2026-09-05T07:40:30.835Z by 0d084dc5_

## Question
`dd952225` made vehicle repair real for the first time (RepairsUnits step was 0 for the life of the engine snapshot). The critical-damage burn below 50 % HP (0.200 %/tick, vehicles.yaml:184-187) is 1.6× the new depot repair (0.125 %/tick, Repairable.PercentageStep 3 / Interval 24), so a burning vehicle docked under the crane still dies and nothing can be repaired back over 50 %.

## Options put to the user
1. Pause the burn while `unit.docked` (RequiresCondition on ChangesHealth@CriticalDamage) — agent's default, 70.
2. Keep it — critical means doomed — 45.
3. Raise Repairable.PercentageStep to 6 so repair out-paces the burn — 35.

## Ruling
User chose **2: keep it**. Both rates stay. A depot repairs damage; it does not put out fires. Consistent with the standing vocabulary that "goes critical" = <50 % = fire + unrecoverable.

## Consequences accepted
- A burning vehicle holds the single dock cell until it dies (~400 ticks from 30 %); the next client waits, then docks. Bounded by the death — no queue built.
- Scenario headers listing the three candidates now describe a closed question.
- Recorded in WORKSPACE/DISCOVERIES.md (2026-09-05) and the agent's memory note on critical-damage vocabulary. Do not re-propose.
