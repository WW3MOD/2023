#region Copyright & License Information
/*
 * THE DEFCON READOUT: the approved copy, the visibility rules and the tick arithmetic.
 *
 * THE LOAD-BEARING TESTS HERE ARE RuleLinesAreApprovedCopy AND TheStepsFollowTheEnumOrder.
 *
 * The first because the three rule lines are the feature. The mockup's own note says it outright --
 * "these three lines do more work than the graphics do" -- and they are the one part of this work
 * that a later reader is most likely to "improve" toward the mechanism, because the mechanism is
 * what the code around them is about. Each line states what the player may and may not do; none
 * mentions a stance, an AttackSource or a condition. Asserting them to the character is the only
 * way that survives someone tidying the file.
 *
 * The second because the nuclear ladder HAS ALREADY CHANGED SHAPE ONCE: the ruling of 2026-09-10
 * split 50/100 kt into two rungs and renumbered GameEnder from 4 to 5. A readout carrying its own
 * copy of the rung list would not have failed anything when that happened -- it would have gone on
 * drawing four boxes with every label one rung out of place, which is a readout that lies. Deriving
 * the steps from the enum and pinning that derivation here is what makes the next renumber a test
 * failure instead.
 *
 * Nothing here constructs a Widget. DefconReadoutModel is a plain class for exactly that reason,
 * stated in its own header: OpenRA.Test can construct neither a World nor a Renderer, so the
 * alternative to this split is asserting nothing at all about what the player reads.
 */
#endregion

using System;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets;

namespace OpenRA.Test
{
	[TestFixture]
	public class DefconReadoutTest
	{
		[Test]
		public void RuleLinesAreApprovedCopy()
		{
			// Verbatim from the wording table in WORKSPACE/mockups/defcon-hud-directions.html.
			Assert.That(DefconReadoutModel.RuleLine(3), Is.EqualTo("The border is closed. Neither side may cross it."));
			Assert.That(DefconReadoutModel.RuleLine(2), Is.EqualTo("Your units will not fire on their own. Every shot is one you order."));
			Assert.That(DefconReadoutModel.RuleLine(1), Is.EqualTo("Everything is released. Units engage on sight."));
		}

		[Test]
		public void LevelNamesAreApprovedCopy()
		{
			Assert.That(DefconReadoutModel.LevelName(3), Is.EqualTo("Positioning"));
			Assert.That(DefconReadoutModel.LevelName(2), Is.EqualTo("Weapons free"));
			Assert.That(DefconReadoutModel.LevelName(1), Is.EqualTo("Open war"));
		}

		[Test]
		public void TheRuleLinesNameNoMechanism()
		{
			// The rule the copy was written to: each line says what the player can and cannot do right
			// now, never the machinery that makes it so. These four words are the ones the traits
			// behind this feature actually use, so they are the ones a well-meaning edit would reach
			// for. The BANNER copy is deliberately exempt -- "Autonomous fire is released" is approved
			// verbatim and is a statement about the world rather than about a trait.
			var forbidden = new[] { "stance", "condition", "autonomous", "target" };

			for (var level = DefconEscalationState.Floor; level <= DefconEscalationState.Ceiling; level++)
			{
				var line = DefconReadoutModel.RuleLine(level).ToLowerInvariant();
				foreach (var word in forbidden)
					Assert.That(line, Does.Not.Contain(word), $"the DEFCON {level} rule line names the mechanism.");
			}
		}

		[Test]
		public void SkirmishDrawsNothing()
		{
			// The strict-no-op rule reaching the screen. Skirmish leaves the level at NoLevel, so the
			// readout asks the LEVEL rather than the mode and gets the right answer for free.
			Assert.That(DefconReadoutModel.IsVisible(DefconEscalationState.NoLevel), Is.False);
			Assert.That(DefconReadoutModel.LevelName(DefconEscalationState.NoLevel), Is.Null);
			Assert.That(DefconReadoutModel.RuleLine(DefconEscalationState.NoLevel), Is.Null);
			Assert.That(DefconReadoutModel.ShowsClock(DefconGameMode.Skirmish, DefconEscalationState.NoLevel), Is.False);
			Assert.That(DefconReadoutModel.ShowsTrigger(DefconGameMode.Skirmish, DefconEscalationState.NoLevel), Is.False);
			Assert.That(DefconReadoutModel.ShowsNuclear(DefconGameMode.Skirmish, DefconEscalationState.NoLevel, (int)NuclearRung.GameEnder, true), Is.False);

			for (var level = DefconEscalationState.Floor; level <= DefconEscalationState.Ceiling; level++)
				Assert.That(DefconReadoutModel.IsVisible(level), Is.True, $"DEFCON {level} is not drawn at all.");
		}

		[Test]
		public void OnlyTheThreeToTwoTransitionHasAClock()
		{
			// The one thing every direction in the mockup had to teach: 3 -> 2 is a clock and 2 -> 1 is
			// an event. Drawn the same way, the player sits waiting for a bar that never fills.
			Assert.That(DefconReadoutModel.ShowsClock(DefconGameMode.Escalation, 3), Is.True);
			Assert.That(DefconReadoutModel.ShowsClock(DefconGameMode.Escalation, 2), Is.False);
			Assert.That(DefconReadoutModel.ShowsClock(DefconGameMode.Escalation, 1), Is.False);

			// Sandbox is PINNED at its configured level, so even DEFCON 3 has nothing to count toward.
			Assert.That(DefconReadoutModel.ShowsClock(DefconGameMode.Sandbox, 3), Is.False);
		}

		[Test]
		public void TheClockFieldIsNeverBlank()
		{
			// LOAD-BEARING, NOT COSMETIC. The player has just spent minutes watching a countdown in
			// that exact spot; the emptied slot is what teaches them the rung ended. An edit that
			// "simplified" this to an empty string would delete the teaching and leave the layout
			// looking identical.
			Assert.That(DefconReadoutModel.NoClock, Is.Not.Null.And.Not.Empty);
			Assert.That(DefconReadoutModel.NoClock.Trim(), Is.Not.Empty);
		}

		[Test]
		public void TheTriggerLineIsShownExactlyWhereTheTriggerWorks()
		{
			// Pinned to the trait that implements it rather than to a literal 2, so a renumbered ladder
			// moves both together.
			Assert.That(DefconReadoutModel.ShowsTrigger(DefconGameMode.Escalation, DefconFireDiscipline.HoldFireLevel), Is.True);
			Assert.That(DefconReadoutModel.ShowsTrigger(DefconGameMode.Escalation, DefconEscalationState.Ceiling), Is.False);
			Assert.That(DefconReadoutModel.ShowsTrigger(DefconGameMode.Escalation, DefconEscalationState.Floor), Is.False);

			// A CASUALTY MOVES NOTHING IN SANDBOX (DefconEscalationState.ReportCasualty), so promising
			// the player that the first kill ends the phase would be a lie there.
			Assert.That(DefconReadoutModel.ShowsTrigger(DefconGameMode.Sandbox, DefconFireDiscipline.HoldFireLevel), Is.False);
			Assert.That(DefconReadoutModel.ShowsTrigger(DefconGameMode.Skirmish, DefconFireDiscipline.HoldFireLevel), Is.False);
		}

		[Test]
		public void TheStepsFollowTheEnumOrder()
		{
			var rungs = (NuclearRung[])Enum.GetValues(typeof(NuclearRung));
			var steps = DefconReadoutModel.Steps();

			Assert.That(steps.Count, Is.EqualTo(rungs.Length),
				"the readout draws a different number of steps than the ladder has rungs.");

			// SIX BOXES SINCE THE 2026-09-10 SPLIT: HOLD, 1 kt, 20 kt, 50 kt, 100 kt, 200 kt+.
			Assert.That(steps.Count, Is.EqualTo(6));

			for (var i = 0; i < rungs.Length; i++)
			{
				Assert.That((int)rungs[i], Is.EqualTo(i),
					$"{rungs[i]} is not at index {i} -- the enum has been renumbered and the readout indexes it positionally.");
				Assert.That(steps[i], Is.EqualTo(DefconReadoutModel.ShortRungLabel(rungs[i])));
			}

			Assert.That(steps[(int)NuclearRung.FiftyKiloton], Is.EqualTo("50kt"));
			Assert.That(steps[(int)NuclearRung.GameEnder], Is.EqualTo("200kt+"));
		}

		[Test]
		public void EveryRungHasBothLabels()
		{
			foreach (NuclearRung rung in Enum.GetValues(typeof(NuclearRung)))
			{
				Assert.That(DefconReadoutModel.ShortRungLabel(rung), Is.Not.Null.And.Not.Empty,
					$"{rung} has no step label, so its box would draw empty.");
				Assert.That(DefconReadoutModel.RungLabel((int)rung), Is.Not.Null.And.Not.Empty,
					$"{rung} has no readout label.");
			}
		}

		[Test]
		public void TheNuclearReadoutAppearsWhenTheGateMattersAndNotBefore()
		{
			const int Ceiling = (int)NuclearRung.GameEnder;

			// The ten-minute wait this whole readout exists to explain: at DEFCON 1 with the ladder
			// still shut, the countdown is running and the block MUST be on screen.
			Assert.That(DefconReadoutModel.ShowsNuclear(DefconGameMode.Escalation, DefconEscalationState.Floor, Ceiling, false), Is.True);

			// Above DEFCON 1 the gate is not counting at all -- NuclearReleaseLadder.Tick returns early
			// at any other level, so TicksUntilRelease sits at the full delay. A frozen clock is worse
			// than no clock.
			Assert.That(DefconReadoutModel.ShowsNuclear(DefconGameMode.Escalation, 3, Ceiling, false), Is.False);
			Assert.That(DefconReadoutModel.ShowsNuclear(DefconGameMode.Escalation, 2, Ceiling, false), Is.False);

			// Once open it stays up whatever the level says.
			Assert.That(DefconReadoutModel.ShowsNuclear(DefconGameMode.Escalation, DefconEscalationState.Floor, Ceiling, true), Is.True);

			// A HOLD ceiling is the host saying "no nuclear weapons this match".
			Assert.That(DefconReadoutModel.ShowsNuclear(DefconGameMode.Escalation, DefconEscalationState.Floor, (int)NuclearRung.Hold, true), Is.False);

			// Skirmish and Sandbox are both pinned wide open, so there is no ladder to draw.
			Assert.That(DefconReadoutModel.ShowsNuclear(DefconGameMode.Skirmish, DefconEscalationState.Floor, Ceiling, true), Is.False);
			Assert.That(DefconReadoutModel.ShowsNuclear(DefconGameMode.Sandbox, DefconEscalationState.Floor, Ceiling, true), Is.False);
		}

		[Test]
		public void TheBannerNamesTheCauseOfEveryTransitionThatCanHappen()
		{
			// Verbatim from the mockup.
			Assert.That(DefconReadoutModel.TransitionCause(1), Is.EqualTo("A life has been taken. Autonomous fire is released."));

			// Written to the same pattern -- cause, then what it released.
			Assert.That(DefconReadoutModel.TransitionCause(2), Is.Not.Null.And.Not.Empty);

			// NOTHING TRANSITIONS INTO DEFCON 3. It is the ceiling: the real-world standing posture,
			// with no 4 or 5 to climb down from. A banner for it could only ever be a bug.
			Assert.That(DefconReadoutModel.TransitionCause(DefconEscalationState.Ceiling), Is.Null);
			Assert.That(DefconReadoutModel.TransitionCause(DefconEscalationState.NoLevel), Is.Null);

			// Every level the state machine can actually MOVE to has a cause line, so no transition can
			// ever fire a banner with a blank second line -- which is the line that does the teaching.
			var reachable = Enumerable.Range(DefconEscalationState.Floor, DefconEscalationState.Ceiling - DefconEscalationState.Floor);
			foreach (var level in reachable)
				Assert.That(DefconReadoutModel.TransitionCause(level), Is.Not.Null, $"a transition to DEFCON {level} would draw a blank cause.");
		}

		[Test]
		public void TheBannerHoldsForAboutFourSeconds()
		{
			// 60 ms per tick: 4000 / 60 = 66 ticks, and 66 x 0.06 s = 3.96 s. READ AS 25 TICKS PER
			// SECOND this would have been 100 ticks = 6.0 s, which is the same 1.5x error this repo
			// has made at eleven sites.
			const int Timestep = 60;
			var ticks = DefconReadoutModel.BannerHoldTicks(Timestep);

			Assert.That(ticks, Is.EqualTo(66));
			Assert.That(ticks * Timestep, Is.EqualTo(3960));
			Assert.That(ticks, Is.Not.EqualTo(100), "the banner hold was computed at 25 ticks per second.");

			// Widget code cannot afford to divide by whatever it was handed.
			Assert.That(DefconReadoutModel.BannerHoldTicks(0), Is.EqualTo(0));
		}

		[Test]
		public void TheTickRateIdentityHolds()
		{
			// THE IDENTITY EVERY DURATION IN THIS FEATURE IS CHECKED AGAINST, asserted rather than
			// written in a comment: StandardTicks is five minutes at 60 ms per tick.
			//
			//     5000 ticks x 0.06 s = 300 s = 5:00
			//
			// At 25 ticks/s the same field would read 200 s = 3:20, and the nuclear release delay --
			// which is exactly twice this field -- would be quarter of an hour instead of ten minutes.
			const int Timestep = 60;
			var info = new DefconEscalationInfo();

			Assert.That(info.StandardTicks * Timestep / 1000, Is.EqualTo(300));
			Assert.That(info.NuclearReleaseDelayTicks * Timestep / 1000, Is.EqualTo(600));
			Assert.That(info.NuclearReleaseDelayTicks, Is.EqualTo(2 * info.StandardTicks));
		}
	}
}
