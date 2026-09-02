# RED arm for the Supply Route slowdown call-out and warning re-arm

Branch `wt/contest-alarm`, one engine file plus one NUnit fixture. Safe win 7 from
`WORKSPACE/proposals/260902-safe-wins-and-swings.md`.

**No game was launched for this work** (launch embargo). Nothing below has been run except
`make all` and `dotnet test`.

## The honest gap, stated first

**The notification itself is not observable from the autotest harness, and I did not write a
scenario that pretends otherwise.**

`OnSlowdownStarted` ends in `TextNotificationsManager.AddTransientLine`, which reaches
`AddTextNotification` (`engine/OpenRA.Game/TextNotificationsManager.cs:84-93`) and from there
`NotificationsCache` plus `Ui.Send`. Neither is exposed to Lua: `Scripting/Properties/` has no
notification property (35 files, none of them notification-related), and the only notification
references anywhere under `Scripting/` are `ScriptTriggers.cs`, `PowerProperties.cs`,
`MediaGlobal.cs` and `DateTimeGlobal.cs` — all of which **emit**, none of which **read**.
`AddTransientLine` additionally no-ops for anyone who is not `LocalPlayer` (`:40-41`).

So a Lua scenario could only assert an adjacent proxy — that the bar crossed 50%, or that
`GetProductionSpeedModifier` fell below 100. Both are true before this change and would pass on a
build where the call-out was deleted. That is a test that cannot fail for the right reason, so it
is not here.

What IS pinned is the pair of pure decisions the call sites branch on, in
`engine/OpenRA.Test/OpenRA.Mods.Common/SupplyRouteWarningTest.cs`. What that does **not** cover:
that `ITick.Tick` is still wired to them, and that the text reaches a screen.

## The NUnit sabotage, and the exact text it must produce

The default `RearmThresholdPercent = 100` has to reproduce the shipped reset
`if (controlBar >= info.BarMax) wasContested = false;` exactly — that is the `@stable` guarantee.
The plausible-looking way to get it wrong is to drop the `>= 100` early return and let the
percentage arithmetic stand alone, which is off by up to one bar unit in `BarMax`:

```diff
--- a/engine/OpenRA.Mods.Common/Traits/SupplyRouteContestation.cs
+++ b/engine/OpenRA.Mods.Common/Traits/SupplyRouteContestation.cs
@@ public static bool ShouldRearmWarning(int controlBar, int barMax, int rearmThresholdPercent)
 			var clamped = Math.Min(Math.Max(rearmThresholdPercent, 0), 100);
-			if (clamped >= 100)
-				return controlBar >= barMax;
+			// RED ARM -- DO NOT COMMIT. "The percentage form already covers 100."
+			// It does not: integer division rounds down, so BarMax-1 reads as 99%
+			// and re-arms one unit early. Revert with git checkout --.
 
 			return controlBar * 100 / barMax >= clamped;
```

Predicted failure, from `RearmDefaultIsTodaysBehaviour`:

> `RearmDefaultIsTodaysBehaviour` — Expected: False But was: True
> `99.999% is not full recovery; @stable must keep warning exactly once per match.`

Note what this sabotage does **not** trip: `LoweredBandRearmsOnTheBand` and
`BandAlwaysLeavesHysteresisAboveTheSlowdownCallOut` both stay green, because they only exercise
bands below 100 where the deleted limb never ran. A RED on the default assertion alone is the
correct and expected shape here — a RED on all three would mean the sabotage landed somewhere
wider than intended.

Second sabotage, for the other predicate. `IsProductionSlowed` must not fire *at* the threshold,
because `GetProductionSpeedModifier` returns a full 100 there:

```diff
-			return controlBar * 100 / barMax < slowdownThreshold;
+			return controlBar * 100 / barMax <= slowdownThreshold;
```

Predicted failure, from `ThresholdItselfIsStillFullSpeed`:

> Expected: False But was: True
> `50% is exactly full speed; the call-out fires one bar unit later.`

and, independently, from `SlowedAgreesWithTheProductionModifierAcrossTheWholeBar`:

> `Disagreed with the production modifier at bar=50000.`

Two fixtures failing from one hunk is intended here: the second walks the whole bar and is the
non-vacuity check on the first.

## What a human has to look and listen for

The part no test covers. In a live match, on your own Supply Route:

1. **Drive an enemy force into your own contestation circle and leave it there.** You should get
   "Supply Route contested!" plus the orange ping immediately, as today.
2. **Watch the selection bar go from green to yellow. The line comes just AFTER, not with it.**
   The two thresholds are off by one percentage point in the shipped code and I did not change
   either: `ISelectionBar.GetColor` goes yellow at `barPercent > SlowdownThreshold` being false,
   i.e. **at 50%**, while `GetProductionSpeedModifier` still returns a full 100 at 50% and only
   tapers below it — so the call-out fires at **49%**. At the fastest shipped drain
   (`MinTicks: 500`) one percentage point is 5 ticks = 0.3s; at reference surplus it is 15 ticks =
   0.9s. So expect yellow-then-line within a second, and **do not report the gap as a bug** — it is
   pre-existing and logged in `WORKSPACE/bugs/discovered.md`. What *would* be a bug is the line
   arriving while the bar is still green, or more than a second or two after it turns.
   Expected text: *"Supply Route degraded! Reinforcements arriving slower."*
3. **Listen for nothing.** `SlowdownNotification` defaults to `""` and is guarded by
   `!string.IsNullOrEmpty`, so there should be **no** voice line — no clip in
   `rules/sound/notifications.yaml` says this. If a voice plays, something wired a default that
   should not be there.
4. **Pull the enemy out, let the bar refill part-way, push back in.** With the shipped default you
   should get **no** second call-out of either kind unless the bar reached completely full in
   between. That is deliberately today's behaviour, not a bug — see the band question below.
5. **Confirm the line is not spammed.** One crossing, one line. The 30 s `NotifyInterval` is on its
   own timestamp, so it can co-fire with the contested line rather than being swallowed by it.

## The band question this change does not answer

`RearmThresholdPercent` ships at 100 = today. I could not defend a specific lower value from the
code, and the proposal says so explicitly. The run that would settle it:

**A single ~30-minute two-bot match on a map where a siege actually oscillates, with the band set
to 100, 90 and 75 in three runs, counting call-outs per match.** The instrument already exists —
every call-out is one `TextNotificationPool.Transients` entry — so the measurement is a count of
lines in one game, not a judgement. The number to reject on is nagging: if 90 produces more than a
handful of repeats across half an hour it is too eager, and 75 is the next candidate. That run is
a game launch and is out of scope under the current embargo.

Worth knowing before tuning: **the band cannot make either warning fire faster than once per 30 s**,
because both handlers early-return on `Game.RunTime <= last*NotifyTime + NotifyInterval` before
doing anything. So the band controls how often within that ceiling, never the ceiling itself.
