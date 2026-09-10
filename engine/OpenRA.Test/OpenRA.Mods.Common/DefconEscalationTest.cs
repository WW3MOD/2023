#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

/*
 * DEFCON escalation: the state machine, the lobby defaults, and the condition mapping.
 *
 * THE LOAD-BEARING TEST HERE IS SkirmishIsAStrictNoOp. The user tests from main and this feature is
 * landing in pieces, so nothing may change for them until the whole thing is in. Skirmish is the
 * default game mode; if it ever stops being a no-op, that guard is what says so.
 *
 * "No condition is granted" is asserted through GrantConditionOnDefconLevel.ConditionFor, which is
 * the trait's ONLY route to a condition name -- Apply cannot reach one any other way. Nothing in
 * OpenRA.Test can construct a World, so the alternative would have been asserting nothing at all.
 *
 * DefconCasualtyObserver.IsQualifyingCasualty is NOT covered here for the same reason: it takes two
 * Actors and a Player relationship, none of which are constructible in this project. It is verified
 * by reading, and that limitation is stated in the report rather than hidden behind a thinner test.
 */

using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class DefconEscalationTest
	{
		const int ClockTicks = 100;

		static DefconEscalationState Escalation(int startLevel = DefconEscalationState.Ceiling, int ticks = ClockTicks)
		{
			return new DefconEscalationState(DefconGameMode.Escalation, startLevel, ticks);
		}

		[Test]
		public void SkirmishIsAStrictNoOp()
		{
			var conditions = new GrantConditionOnDefconLevelInfo().Conditions;

			// Start At and Pace are both set to values that WOULD do something in Escalation, so this
			// is a test of the mode rather than of an inert configuration.
			var state = new DefconEscalationState(DefconGameMode.Skirmish, DefconEscalationState.Ceiling, 1);

			Assert.That(state.Level, Is.EqualTo(DefconEscalationState.NoLevel), "Skirmish put a DEFCON level in play.");
			Assert.That(GrantConditionOnDefconLevel.ConditionFor(state.Level, conditions), Is.Null);

			// Far longer than the fastest shipped pace, interleaved with casualties, which is the only
			// other thing that can move the level.
			for (var i = 0; i < 20000; i++)
			{
				Assert.That(state.Tick(), Is.False, $"The level moved on tick {i} in Skirmish.");
				if (i % 37 == 0)
					Assert.That(state.ReportCasualty(), Is.False, $"A casualty moved the level on tick {i} in Skirmish.");

				Assert.That(state.Level, Is.EqualTo(DefconEscalationState.NoLevel));
				Assert.That(GrantConditionOnDefconLevel.ConditionFor(state.Level, conditions), Is.Null);
			}
		}

		[Test]
		public void TheDefaultGameModeIsSkirmish()
		{
			// Guards the default from both directions: the C# field, and the string the lobby actually
			// registers as the default value of the dropdown.
			var info = new DefconEscalationInfo();
			Assert.That(info.ModeDefault, Is.EqualTo(DefconGameMode.Skirmish));

			var mode = ((ILobbyOptions)info).LobbyOptions(null).First(o => o.Id == DefconEscalationInfo.ModeOptionId);
			Assert.That(mode.DefaultValue, Is.EqualTo("skirmish"));
			Assert.That(mode.Values.ContainsKey(mode.DefaultValue), Is.True, "The default value is not one of the dropdown's own values.");
		}

		[Test]
		public void TheLobbyRegistersFourDropdowns()
		{
			var options = ((ILobbyOptions)new DefconEscalationInfo()).LobbyOptions(null).ToArray();

			// FOUR since the nuclear release ladder landed. The fourth is the ladder's CEILING --
			// the largest warhead the match will ever permit -- and it sits on this trait rather
			// than on a new one because it is configuration of the same mode: DefconEscalation
			// already owns the game mode the ladder only runs inside.
			Assert.That(options.Select(o => o.Id), Is.EquivalentTo(new[]
			{
				DefconEscalationInfo.ModeOptionId, DefconEscalationInfo.StartOptionId,
				DefconEscalationInfo.PaceOptionId, DefconEscalationInfo.CeilingOptionId
			}));

			// Checkbox vs dropdown is purely the C# type: a LobbyBooleanOption renders as a checkbox.
			// There is no integer option type in this engine, so Start At has to be a string dropdown.
			foreach (var o in options)
			{
				Assert.That(o, Is.Not.InstanceOf<LobbyBooleanOption>(), $"{o.Id} would render as a checkbox.");
				Assert.That(o.Values.ContainsKey(o.DefaultValue), Is.True, $"{o.Id} defaults to a value it does not offer.");
			}

			var start = options.First(o => o.Id == DefconEscalationInfo.StartOptionId);
			Assert.That(start.Values.Keys, Is.EquivalentTo(new[] { "3", "2", "1" }), "DEFCON 4 and 5 do not exist by design.");
			Assert.That(start.DefaultValue, Is.EqualTo("3"), "3 is both the default and the ceiling.");

			var pace = options.First(o => o.Id == DefconEscalationInfo.PaceOptionId);
			Assert.That(pace.Values.Keys, Is.EquivalentTo(new[] { "slow", "standard", "fast" }));
			Assert.That(pace.DefaultValue, Is.EqualTo("standard"));

			// The ceiling offers every rung INCLUDING Hold, which is the "no nuclear weapons this
			// match" setting, and nothing above GameEnder -- the Tsar Bomba is not a rung and must
			// never become selectable by adding one here.
			var ceiling = options.First(o => o.Id == DefconEscalationInfo.CeilingOptionId);
			// SIX since the 50/100 kt split of 2026-09-10 -- HOLD plus five yield rungs, where the
			// combined "50-100 kt" entry used to be one.
			Assert.That(ceiling.Values.Keys, Is.EquivalentTo(new[] { "hold", "kiloton", "twentykiloton", "fiftykiloton", "hundredkiloton", "gameender" }));
			Assert.That(ceiling.DefaultValue, Is.EqualTo("gameender"));
		}

		[Test]
		public void ThreeDropsToTwoExactlyOnTheClock()
		{
			var state = Escalation();
			Assert.That(state.TicksUntilNextLevel, Is.EqualTo(ClockTicks));

			for (var i = 0; i < ClockTicks - 1; i++)
			{
				Assert.That(state.Tick(), Is.False);
				Assert.That(state.Level, Is.EqualTo(3), $"Dropped early, on tick {i + 1} of {ClockTicks}.");
			}

			Assert.That(state.Tick(), Is.True, "The clock did not expire on its last tick.");
			Assert.That(state.Level, Is.EqualTo(2));
		}

		[Test]
		public void TwoToOneIsNotOnAClock()
		{
			var state = Escalation(startLevel: 2);
			Assert.That(state.TicksUntilNextLevel, Is.EqualTo(0), "DEFCON 2 is running a clock it should not have.");

			for (var i = 0; i < 50000; i++)
				Assert.That(state.Tick(), Is.False);

			Assert.That(state.Level, Is.EqualTo(2), "Time alone moved DEFCON 2.");

			Assert.That(state.ReportCasualty(), Is.True, "One life taken did not end DEFCON 2.");
			Assert.That(state.Level, Is.EqualTo(1));
		}

		[Test]
		public void ACasualtyAtDefconThreeIsNotBanked()
		{
			// Deliberate: the first life is taken long before the 3 -> 2 clock expires in practice, so
			// latching it would collapse the opening phase into a single tick.
			var state = Escalation();
			Assert.That(state.ReportCasualty(), Is.False);
			Assert.That(state.Level, Is.EqualTo(3));

			for (var i = 0; i < ClockTicks; i++)
				state.Tick();

			Assert.That(state.Level, Is.EqualTo(2), "The clock still has to run.");
			Assert.That(state.ReportCasualty(), Is.True, "The casualty at 3 should not have consumed the 2 -> 1 trigger.");
			Assert.That(state.Level, Is.EqualTo(1));
		}

		[Test]
		public void DefconOneIsTerminal()
		{
			var state = Escalation(startLevel: 1);
			Assert.That(state.TicksUntilNextLevel, Is.EqualTo(0));

			for (var i = 0; i < 50000; i++)
			{
				Assert.That(state.Tick(), Is.False);
				Assert.That(state.ReportCasualty(), Is.False);
			}

			Assert.That(state.Level, Is.EqualTo(1));
		}

		[Test]
		public void StartAtIsClampedToTheDesignedRange()
		{
			// There is no DEFCON 4 or 5, so a level above the ceiling is the ceiling rather than a
			// fourth rung nothing else in the mod knows about.
			Assert.That(Escalation(startLevel: 5).Level, Is.EqualTo(DefconEscalationState.Ceiling));
			Assert.That(Escalation(startLevel: 0).Level, Is.EqualTo(DefconEscalationState.Floor));
		}

		[Test]
		public void SandboxHoldsTheStartingLevelButGrantsItsCondition()
		{
			var conditions = new GrantConditionOnDefconLevelInfo().Conditions;
			var state = new DefconEscalationState(DefconGameMode.Sandbox, DefconEscalationState.Ceiling, 1);

			for (var i = 0; i < 20000; i++)
			{
				Assert.That(state.Tick(), Is.False);
				Assert.That(state.ReportCasualty(), Is.False);
			}

			Assert.That(state.Level, Is.EqualTo(DefconEscalationState.Ceiling), "Sandbox escalated.");
			Assert.That(GrantConditionOnDefconLevel.ConditionFor(state.Level, conditions), Is.EqualTo("defcon-3"));
		}

		[Test]
		public void PaceScalesTheThreeToTwoClockAndNothingElse()
		{
			var info = new DefconEscalationInfo();

			Assert.That(info.TicksAtDefconThree(DefconPace.Slow), Is.EqualTo(info.SlowTicks));
			Assert.That(info.TicksAtDefconThree(DefconPace.Standard), Is.EqualTo(info.StandardTicks));
			Assert.That(info.TicksAtDefconThree(DefconPace.Fast), Is.EqualTo(info.FastTicks));

			Assert.That(info.SlowTicks, Is.GreaterThan(info.StandardTicks));
			Assert.That(info.StandardTicks, Is.GreaterThan(info.FastTicks));

			// Untuned placeholders, but the units are not negotiable: the timestep is 60 ms, so a tick
			// is 0.06 s and Standard is 5000 x 0.06 = 300 s = 5:00. Reading the rate as 25 tps would
			// make this 200 s, which is the 1.5x error this repo has made ten times.
			Assert.That(info.StandardTicks, Is.EqualTo(5000));

			// Pace touches the 3 -> 2 clock only. At DEFCON 2 every pace is the same: no clock at all.
			foreach (var pace in new[] { DefconPace.Slow, DefconPace.Standard, DefconPace.Fast })
			{
				var atTwo = new DefconEscalationState(DefconGameMode.Escalation, 2, info.TicksAtDefconThree(pace));
				Assert.That(atTwo.TicksUntilNextLevel, Is.EqualTo(0), $"{pace} put a clock on DEFCON 2.");
			}
		}

		[Test]
		public void TheNuclearReleaseDelayIsTenMinutesAtTheREALTickRate()
		{
			var info = new DefconEscalationInfo();

			// 10000 ticks, and the derivation is the point of this test rather than the number.
			// The timestep is 60 ms (mod.yaml's `default` GameSpeed), so a tick is 0.06 s and the
			// rate is 1000/60 = 16.67 ticks/s -- NOT 25. Ten minutes is 600 s, and 600 / 0.06 = 10000.
			Assert.That(info.NuclearReleaseDelayTicks, Is.EqualTo(10000));

			// PINNED AGAINST StandardTicks RATHER THAN JUST RESTATED, which is what makes this a test
			// of the UNIT and not a copy of the constant. That field is 5000 = 300 s = 5:00 and is
			// itself guarded above, so ten minutes must be exactly twice it. Reading the rate as
			// 25 tps gives 15000 ticks for "ten minutes" -- 15 minutes of real time, the 1.5x error
			// this repo has now made at eleven sites.
			Assert.That(info.NuclearReleaseDelayTicks, Is.EqualTo(info.StandardTicks * 2),
				"the release delay is no longer twice the Standard pace clock, so one of the two was " +
				"converted at a different tick rate from the other");

			// 0 must stay legal: it is the ruling's "opens immediately" and RulesetLoaded refuses
			// only negatives. Asserted on the validator rather than on the field, which is where a
			// well-meaning `must be positive` tightening would land.
			Assert.That(() => ((IRulesetLoaded<ActorInfo>)info).RulesetLoaded(null, null), Throws.Nothing);
		}

		[Test]
		public void EveryLevelHasItsOwnCondition()
		{
			var conditions = new GrantConditionOnDefconLevelInfo().Conditions;

			var granted = new[] { 3, 2, 1 }
				.Select(l => GrantConditionOnDefconLevel.ConditionFor(l, conditions))
				.ToArray();

			Assert.That(granted, Is.All.Not.Null, "A level maps to no condition, so YAML cannot key on it.");
			Assert.That(granted.Distinct().Count(), Is.EqualTo(3), "Two levels share a condition name.");
			Assert.That(GrantConditionOnDefconLevel.ConditionFor(DefconEscalationState.NoLevel, conditions), Is.Null);
		}
	}
}
