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

			// Start At and the no-rush clock are both set to values that WOULD do something in
			// Escalation, so this is a test of the mode rather than of an inert configuration.
			var state = new DefconEscalationState(DefconGameMode.Skirmish, DefconEscalationState.Ceiling, 1);

			Assert.That(state.Level, Is.EqualTo(DefconEscalationState.NoLevel), "Skirmish put a DEFCON level in play.");
			Assert.That(GrantConditionOnDefconLevel.ConditionFor(state.Level, conditions), Is.Null);

			// Far longer than the shortest shipped no-rush period, interleaved with casualties, which
			// is the only other thing that can move the level.
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
		public void TheModeDropdownOffersTwoModesAndSandboxIsNotOneOfThem()
		{
			// SANDBOX IS A DEVELOPER OPT-IN, NOT A MODE A HOST PICKS (2026-09-19). Its whole content is
			// "pin the level and never escalate", and it used to sit third in this list where the two
			// levels worth pinning were the two the fire rules keyed on -- so the shipped Start At made
			// it a match in which no weapon could fire, for its whole length.
			var info = new DefconEscalationInfo();
			var mode = ((ILobbyOptions)info).LobbyOptions(null).First(o => o.Id == DefconEscalationInfo.ModeOptionId);

			Assert.That(mode.Values.Keys, Is.EquivalentTo(new[] { "escalation", "skirmish" }));

			// AND IT IS STILL REACHABLE, by the one route it was ever right for: a map or scenario
			// setting ModeDefault. The entry is admitted exactly when the default names it, because a
			// DefaultValue that is not a key of its own Values throws on client join -- LobbyOption.Label
			// indexes Values unchecked and LobbyCommands calls it on every option's default. Dropping the
			// entry unconditionally would have turned a supported opt-in into a KeyNotFoundException that
			// neither the build nor the YAML lint could see.
			// Set through FieldLoader rather than by reflection, so this is the same path a map's
			// `ModeDefault: Sandbox` line takes rather than a shortcut around it.
			var sandboxInfo = new DefconEscalationInfo();
			FieldLoader.LoadField(sandboxInfo, nameof(DefconEscalationInfo.ModeDefault), nameof(DefconGameMode.Sandbox));
			var sandboxMode = ((ILobbyOptions)sandboxInfo).LobbyOptions(null).First(o => o.Id == DefconEscalationInfo.ModeOptionId);

			Assert.That(sandboxMode.DefaultValue, Is.EqualTo("sandbox"));
			Assert.That(sandboxMode.Values.ContainsKey(sandboxMode.DefaultValue), Is.True,
				"A map opting into Sandbox would crash every joining client on LobbyOption.Label.");
		}

		[Test]
		public void TheLobbyRegistersFourDropdowns()
		{
			var options = ((ILobbyOptions)new DefconEscalationInfo()).LobbyOptions(null).ToArray();

			// STILL FOUR, BUT NOT THE SAME FOUR (2026-09-13). `defcon-pace` became the minutes
			// dropdown `no-rush-period`, and `nuclear-ceiling` was replaced by `first-warheads` --
			// the exchange has no host ceiling any more, and the warhead delay was a fixed trait
			// field with no control at all.
			Assert.That(options.Select(o => o.Id), Is.EquivalentTo(new[]
			{
				DefconEscalationInfo.ModeOptionId, DefconEscalationInfo.StartOptionId,
				DefconEscalationInfo.NoRushOptionId, DefconEscalationInfo.FirstWarheadsOptionId
			}));

			// Checkbox vs dropdown is purely the C# type: a LobbyBooleanOption renders as a checkbox.
			// There is no integer option type in this engine, so both minute clocks have to be string
			// dropdowns keyed on the stringified number.
			foreach (var o in options)
			{
				Assert.That(o, Is.Not.InstanceOf<LobbyBooleanOption>(), $"{o.Id} would render as a checkbox.");
				Assert.That(o.Values.ContainsKey(o.DefaultValue), Is.True, $"{o.Id} defaults to a value it does not offer.");

				// THE LOBBY DOES NOT SAY "DEFCON" ANYWHERE A HOST CAN READ (decision 18): the lobby
				// says what HAPPENS and the game says what it is CALLED. The option IDS still carry
				// it and deliberately so -- an id is wire-visible state and renaming one silently
				// discards every stored value set to it -- so this checks the copy, not the keys.
				Assert.That(o.Name, Does.Not.Contain("DEFCON").IgnoreCase, $"{o.Id}'s label says DEFCON.");
				Assert.That(o.Description, Does.Not.Contain("DEFCON").IgnoreCase, $"{o.Id}'s tooltip says DEFCON.");
				foreach (var label in o.Values.Values)
					Assert.That(label, Does.Not.Contain("DEFCON").IgnoreCase, $"{o.Id} offers a value labelled '{label}'.");
			}

			var start = options.First(o => o.Id == DefconEscalationInfo.StartOptionId);
			Assert.That(start.Values.Keys, Is.EquivalentTo(new[] { "3", "2", "1" }), "DEFCON 4 and 5 do not exist by design.");
			Assert.That(start.DefaultValue, Is.EqualTo("3"), "3 is both the default and the ceiling.");
			Assert.That(start.Values["3"], Is.EqualTo("Positioning"));
			Assert.That(start.Values["1"], Is.EqualTo("Open war"));

			// THE TWO CLOCKS ARE KEYED ON MINUTES, which is what lets a host tune them at all --
			// the retired pace dropdown offered three adjectives whose durations were self-declared
			// untuned guesses, and that is what kept the whole section dimmed (decision 12).
			var noRush = options.First(o => o.Id == DefconEscalationInfo.NoRushOptionId);
			Assert.That(noRush.Values.Keys, Is.EquivalentTo(new[] { "2", "3", "5", "7", "10", "15" }));
			Assert.That(noRush.DefaultValue, Is.EqualTo("5"), "5 minutes is what the retired Standard pace was worth.");

			var warheads = options.First(o => o.Id == DefconEscalationInfo.FirstWarheadsOptionId);
			Assert.That(warheads.Values.Keys, Is.EquivalentTo(new[] { "2", "5", "7", "10", "15", "20" }));
			Assert.That(warheads.DefaultValue, Is.EqualTo("10"), "10 minutes is the user's ruling (decision 17.1).");

			// NONE OF THEM IS A PLACEHOLDER ANY MORE. A live feature wearing an inert label is the
			// one thing a dimmed control must never be; the 2026-09-10 mode audit called that the
			// worst single item in its whole survey and it was this flag it meant.
			foreach (var o in options)
				Assert.That(o.Placeholder, Is.False, $"{o.Id} still renders dimmed with the placeholder tooltip.");

			// AND NO *EXCHANGE* OPTION IS DECLARED HERE. `first-warheads` is this trait's -- it is a
			// phase clock and says WHEN warheads exist. What must never come back is a yield control:
			// the failure mode is a re-added ceiling quietly pinning the exchange shut from a lobby
			// dropdown the model has no concept of.
			Assert.That(options.Any(o => o.Id == "nuclear-ceiling"), Is.False,
				"the nuclear ceiling is back; the exchange has no host cap and cannot honour one");
			Assert.That(options.Any(o => o.Id == NuclearExchangeInfo.PostureOptionId), Is.False,
				"DefconEscalation declared an exchange option; that belongs on NuclearExchange");
		}

		[Test]
		public void TheExchangeDeclaresThePostureAndNothingElse()
		{
			// EXACTLY ONE OPTION SINCE EXCHANGE v2 (2026-09-15). The retaliation window dropdown went
			// with the window itself -- there is no grant left whose length a host could set -- and
			// EquivalentTo rather than Contains is what makes this fail if it is ever re-added by an
			// edit that did not mean to bring the model back with it.
			var options = ((ILobbyOptions)new NuclearExchangeInfo()).LobbyOptions(null).ToArray();

			Assert.That(options.Select(o => o.Id), Is.EquivalentTo(new[]
			{
				NuclearExchangeInfo.PostureOptionId
			}));

			foreach (var o in options)
			{
				Assert.That(o, Is.Not.InstanceOf<LobbyBooleanOption>(), $"{o.Id} would render as a checkbox.");
				Assert.That(o.Values.ContainsKey(o.DefaultValue), Is.True, $"{o.Id} defaults to a value it does not offer.");

				// NOT A Placeholder. Nor is any Escalation dropdown since MarkAsPlaceholder went
				// false with the phase clocks -- so this asserts the same property the sibling
				// above does, for the trait that declares the other four.
				Assert.That(o.Placeholder, Is.False, $"{o.Id} is dimmed but is live.");
			}

			var posture = options.First(o => o.Id == NuclearExchangeInfo.PostureOptionId);
			Assert.That(posture.Values.Keys, Is.EquivalentTo(new[] { "limited", "flexible", "massive" }));
			Assert.That(posture.DefaultValue, Is.EqualTo("flexible"), "Flexible is the identity multiplier and must be the default.");
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
		public void TheNoRushPeriodScalesTheThreeToTwoClockAndNothingElse()
		{
			const int Timestep = 60;
			var info = new DefconEscalationInfo();

			// MINUTES TO TICKS, MULTIPLYING BEFORE DIVIDING. The other idiom in this tree --
			// TimeLimitManager's `1000 / world.Timestep` -- is integer division and yields 16 ticks
			// per second rather than 16.67, so five minutes would come out 4800 ticks: 288 s, 4%
			// short, and short by more the longer the clock. Every offered value is checked, not
			// just the default, because the error grows with the number.
			foreach (var minutes in info.NoRushOptions)
				Assert.That(info.NoRushTicks(minutes, Timestep), Is.EqualTo(minutes * 1000),
					$"{minutes} minutes did not convert to {minutes * 1000} ticks at a 60 ms timestep.");

			Assert.That(info.NoRushTicks(info.NoRushDefault, Timestep), Is.EqualTo(5000));

			// The clock touches the 3 -> 2 transition only. At DEFCON 2 every setting is the same:
			// no clock at all -- the peace ends on a kill, not on a timer.
			foreach (var minutes in info.NoRushOptions)
			{
				var atTwo = new DefconEscalationState(DefconGameMode.Escalation, 2, info.NoRushTicks(minutes, Timestep));
				Assert.That(atTwo.TicksUntilNextLevel, Is.EqualTo(0), $"{minutes} minutes put a clock on DEFCON 2.");
			}
		}

		[Test]
		public void TheScenarioOverridesWinAndTheirSentinelsAreNotTheSameValue()
		{
			const int Timestep = 60;

			// The default info is "no override": both clocks come from the dropdown.
			var shipped = new DefconEscalationInfo();
			Assert.That(shipped.NoRushTicks(5, Timestep), Is.EqualTo(5000));
			Assert.That(shipped.NuclearReleaseDelayTicks(10, Timestep), Is.EqualTo(10000));

			// ZERO IS "NOT IN PLAY" FOR THE NO-RUSH CLOCK AND A REAL SETTING FOR THE WARHEAD ONE,
			// and that asymmetry is the point of this test rather than an accident of the defaults.
			// A no-rush period of no length is the Skirmish game; a warhead delay of zero opens the
			// ladder on the tick DEFCON 1 is reached, which is a match somebody may want. Unifying
			// the two sentinels would silently delete the second setting.
			var zeroed = new DefconEscalationInfo();
			Assert.That(zeroed.NoRushTicksOverride, Is.EqualTo(0), "the no-rush sentinel is 0.");
			Assert.That(zeroed.NuclearReleaseDelayTicksOverride, Is.EqualTo(-1), "the warhead sentinel is -1, NOT 0.");

			// A rules.yaml cannot be loaded from this project -- the override FIELDS are readonly and
			// are set by the field loader -- so what is pinned here is the branch condition each
			// method uses, exercised at the shipped sentinel values. With neither override in play
			// the dropdown's minutes must win at every offered value, not just the default.
			foreach (var minutes in shipped.NoRushOptions)
				Assert.That(shipped.NoRushTicks(minutes, Timestep), Is.EqualTo(minutes * 1000));

			foreach (var minutes in shipped.FirstWarheadsOptions)
				Assert.That(shipped.NuclearReleaseDelayTicks(minutes, Timestep), Is.EqualTo(minutes * 1000));
		}

		[Test]
		public void TheNuclearReleaseDelayIsTenMinutesAtTheREALTickRate()
		{
			const int Timestep = 60;
			var info = new DefconEscalationInfo();

			// 10000 ticks, and the derivation is the point of this test rather than the number.
			// The timestep is 60 ms (mod.yaml's `default` GameSpeed), so a tick is 0.06 s and the
			// rate is 1000/60 = 16.67 ticks/s -- NOT 25. Ten minutes is 600 s, and 600 / 0.06 = 10000.
			Assert.That(info.NuclearReleaseDelayTicks(info.FirstWarheadsDefault, Timestep), Is.EqualTo(10000));

			// PINNED AGAINST THE NO-RUSH DEFAULT RATHER THAN JUST RESTATED, which is what makes this
			// a test of the UNIT and not a copy of the constant. That clock is 5 minutes = 5000 ticks
			// and is itself guarded above, so ten minutes must be exactly twice it. Reading the rate
			// as 25 tps gives 15000 ticks for "ten minutes" -- 15 minutes of real time, the 1.5x
			// error this repo has now made at eleven sites.
			Assert.That(info.NuclearReleaseDelayTicks(info.FirstWarheadsDefault, Timestep),
				Is.EqualTo(info.NoRushTicks(info.NoRushDefault, Timestep) * 2),
				"the release delay is no longer twice the default no-rush period, so one of the two was " +
				"converted at a different tick rate from the other");

			// 0 must stay legal: it is the ruling's "opens immediately". Asserted on the validator
			// rather than on the field, which is where a well-meaning `must be positive` tightening
			// would land.
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
