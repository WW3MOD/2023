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
 * The DEFCON 2 hold-fire rule: units stop firing autonomously, and every shot becomes one a player
 * gave.
 *
 * WHAT THIS FIXTURE COVERS AND WHAT IT CANNOT, stated plainly rather than left to be inferred. It
 * covers the PREDICATE -- which levels hold fire, and which shots the hold refuses -- because that is
 * the whole of the decision and it was split into a plain static class precisely so a test could
 * reach it. It does NOT cover the six read sites that consult it: every one lives inside a per-actor
 * trait method, nothing in OpenRA.Test can construct a World, and a thinner test that pretended
 * otherwise would be worse than an honest gap. The six are verified by reading, and the report says
 * so.
 *
 * THE LOAD-BEARING TEST HERE IS TheRuleIsExpressedInTheEngineOwnProvenanceTest. "A shot somebody
 * gave" already has a definition in this codebase -- AutoTarget.IsAutoAcquiredSource -- and the one
 * way this feature rots is by growing a second, parallel definition that drifts from it. That test
 * pins the two together across EVERY AttackSource value, so adding a fourth source fails here rather
 * than silently defaulting to "autonomous" and letting a whole class of shot through at DEFCON 2.
 *
 * SkirmishHoldsNoFire is the twin of DefconEscalationTest.SkirmishIsAStrictNoOp, and exists for the
 * same reason: Skirmish is the default game mode and the user tests from main, so nothing may change
 * for them while this feature lands in pieces.
 */

using System;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class DefconFireDisciplineTest
	{
		static AttackSource[] AllSources => (AttackSource[])Enum.GetValues(typeof(AttackSource));

		[Test]
		public void TheHoldAppliesAtExactlyOneRung()
		{
			// DEFCON 3 is the positioning phase behind a wall and DEFCON 1 is open war, in which
			// autonomous fire returns. Only the middle rung holds.
			Assert.That(DefconFireDiscipline.HoldFireLevel, Is.EqualTo(2));
			Assert.That(DefconFireDiscipline.HoldFireLevel, Is.GreaterThan(DefconEscalationState.Floor));
			Assert.That(DefconFireDiscipline.HoldFireLevel, Is.LessThan(DefconEscalationState.Ceiling));

			Assert.That(DefconFireDiscipline.HoldsFire(3), Is.False, "DEFCON 3 held fire; it is the positioning phase, not the hold.");
			Assert.That(DefconFireDiscipline.HoldsFire(2), Is.True, "DEFCON 2 did not hold fire, which is the whole rule.");
			Assert.That(DefconFireDiscipline.HoldsFire(1), Is.False, "DEFCON 1 held fire; open war is where autonomous fire returns.");
		}

		[Test]
		public void SkirmishHoldsNoFire()
		{
			// Skirmish is the default game mode and must be a strict no-op. It never leaves NoLevel, so
			// the guard at every read site is a single int compare that is false forever. Driven through
			// the real state machine rather than asserted against a constant, so this fails if Skirmish
			// ever starts moving the level.
			var state = new DefconEscalationState(DefconGameMode.Skirmish, DefconEscalationState.Ceiling, 1);
			Assert.That(DefconFireDiscipline.HoldsFire(state.Level), Is.False);

			for (var i = 0; i < 20000; i++)
			{
				state.Tick();
				if (i % 37 == 0)
					state.ReportCasualty();

				Assert.That(DefconFireDiscipline.HoldsFire(state.Level), Is.False, $"Skirmish held fire on tick {i}.");

				foreach (var source in AllSources)
					Assert.That(DefconFireDiscipline.Permits(state.Level, source, false), Is.True,
						$"Skirmish refused a {source} shot on tick {i}.");
			}
		}

		[Test]
		public void AnEscalationMatchHoldsFireForExactlyTheSecondPhase()
		{
			// The shipped sequence, end to end: 3 on the clock, 2 until a life is taken, then 1.
			const int ClockTicks = 100;
			var state = new DefconEscalationState(DefconGameMode.Escalation, DefconEscalationState.Ceiling, ClockTicks);

			for (var i = 0; i < ClockTicks - 1; i++)
			{
				state.Tick();
				Assert.That(DefconFireDiscipline.HoldsFire(state.Level), Is.False, $"Fire was held at DEFCON 3, on tick {i + 1}.");
			}

			Assert.That(state.Tick(), Is.True);
			Assert.That(DefconFireDiscipline.HoldsFire(state.Level), Is.True, "The 3 -> 2 drop did not start the hold.");

			// Time alone never ends it; one casualty does, and that restores autonomous fire.
			for (var i = 0; i < 5000; i++)
			{
				state.Tick();
				Assert.That(DefconFireDiscipline.HoldsFire(state.Level), Is.True, $"The hold lapsed on its own, on tick {i}.");
			}

			Assert.That(state.ReportCasualty(), Is.True);
			Assert.That(DefconFireDiscipline.HoldsFire(state.Level), Is.False, "DEFCON 1 kept holding fire; open war means autonomous fire is back.");
		}

		[Test]
		public void SandboxPinnedAtTwoHoldsFire()
		{
			// Sandbox pins the level so DEFCON-keyed content is reachable to build and test against. The
			// rule is a pure function of the LEVEL, not of the mode, so it applies there unchanged --
			// which is what makes a sandbox at 2 an actual rehearsal of the phase.
			var state = new DefconEscalationState(DefconGameMode.Sandbox, 2, 1);

			for (var i = 0; i < 5000; i++)
				state.Tick();

			Assert.That(state.Level, Is.EqualTo(2));
			Assert.That(DefconFireDiscipline.HoldsFire(state.Level), Is.True);
		}

		[Test]
		public void TheRuleIsExpressedInTheEngineOwnProvenanceTest()
		{
			// THE ANTI-DRIFT TEST. AutoTarget.IsAutoAcquiredSource is the engine's own answer to "did
			// somebody order this shot, or did the unit choose it?", and this rule must be a skin over
			// that method rather than a second definition beside it. Asserted over every AttackSource
			// value, so a fourth source added later fails here instead of quietly falling through as
			// autonomous -- or, worse, as ordered.
			foreach (var source in AllSources)
			{
				var isAutonomous = AutoTarget.IsAutoAcquiredSource(source);

				Assert.That(DefconFireDiscipline.Permits(DefconFireDiscipline.HoldFireLevel, source, false),
					Is.EqualTo(!isAutonomous),
					$"At DEFCON 2 the rule and IsAutoAcquiredSource disagree about {source}.");
			}
		}

		[Test]
		public void OnlyOrderedShotsSurviveDefconTwo()
		{
			const int Held = DefconFireDiscipline.HoldFireLevel;

			// A player order, a Lua order and a bot's deliberate named-target Attack all arrive as
			// Default and all still fire: that is the point of the phase, not an exception to it.
			Assert.That(DefconFireDiscipline.Permits(Held, AttackSource.Default, false), Is.True,
				"DEFCON 2 refused an ordered shot; the phase is 'free to strike, but only by direct order'.");

			// The unit picked this for itself -- idle rescan, ambush, retaliation, opportunity fire.
			Assert.That(DefconFireDiscipline.Permits(Held, AttackSource.AutoTarget, false), Is.False);

			// AttackMove is the interesting one, and IsAutoAcquiredSource already settles it: the player
			// ordered a MOVE, not that particular target, so a shot taken along the way is the unit's own
			// decision. It is also the dominant bot engagement mode, so getting this wrong would leave
			// bots fighting a phase in which nobody else may.
			Assert.That(DefconFireDiscipline.Permits(Held, AttackSource.AttackMove, false), Is.False,
				"An attack-move contact shot counted as ordered; the order was the move, not the target.");
		}

		[Test]
		public void AForceAttackIsAlwaysADirectOrder()
		{
			// A force-attack can only come from a player click, the Lua binding or a deliberate bot
			// order. It arrives as Default today, so this is belt-and-braces -- but if a force-attack
			// ever carried an auto-acquired source it must still fire.
			foreach (var source in AllSources)
				Assert.That(DefconFireDiscipline.Permits(DefconFireDiscipline.HoldFireLevel, source, true), Is.True,
					$"A force-attack with source {source} was refused at DEFCON 2.");
		}

		[Test]
		public void EveryOtherLevelPermitsEverything()
		{
			// Outside the one held rung the predicate must be transparent, whatever the provenance --
			// this is what makes the feature inert at DEFCON 3, at DEFCON 1 and outside the mode.
			var levels = new[] { DefconEscalationState.NoLevel, DefconEscalationState.Floor, DefconEscalationState.Ceiling };

			foreach (var level in levels)
				foreach (var source in AllSources)
					foreach (var forceAttack in new[] { false, true })
						Assert.That(DefconFireDiscipline.Permits(level, source, forceAttack), Is.True,
							$"Level {level} refused a {source} shot (forceAttack: {forceAttack}).");
		}
	}
}
