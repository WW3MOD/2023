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
 * THE TWO DEFCON FIRE RULES.
 *
 *   DEFCON 3, "Positioning" -- the total cease-fire. NO weapon fires: not by autotarget, not by a
 *   player's explicit order, not by force-fire at bare ground. Only an armament that has explicitly
 *   opted out as not-really-a-weapon still works. Keyed on the ARMAMENT, never on provenance.
 *
 *   DEFCON 2, "Weapons free" -- the hold. Units stop firing autonomously and every shot becomes one
 *   a player gave. Keyed on PROVENANCE, and force-attacks are deliberately exempt.
 *
 * They are not one rule at two strengths, and TheTwoRulesDisagreeAboutForceFireOnPurpose pins the
 * place they diverge: the force-attack that proves a human ordered the shot at DEFCON 2 is the exact
 * bypass the DEFCON 3 rule exists to close. Unifying the two predicates fails that test.
 *
 * WHAT THIS FIXTURE COVERS AND WHAT IT CANNOT, stated plainly rather than left to be inferred. It
 * covers the PREDICATES -- which levels hold or cease fire, which shots each refuses -- because that
 * is the whole of both decisions and they were split into a plain static class precisely so a test
 * could reach them. It does NOT cover the read sites that consult them: every one lives inside a
 * per-actor trait method, nothing in OpenRA.Test can construct a World, and a thinner test that
 * pretended otherwise would be worse than an honest gap. They are verified by reading, and the report
 * says so. For the cease-fire the read sites are Armament.CanFire (the choke point every firing path
 * converges on) and AttackBase's two order targeters (which refuse the order so the cursor cannot
 * promise a shot CanFire will decline).
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
 *
 * BOTH PREDICATES TAKE A MODE AS WELL AS A LEVEL, and OnlyEscalationEverHoldsFire /
 * OnlyEscalationEverCeasesFire are the pair that pins it. They replace two tests that asserted the
 * OPPOSITE -- that a Sandbox match pinned at a rung rehearsed that rung's fire rule -- which read as
 * a feature and was a match nothing could shoot in, because a pinned level is not a phase and
 * nothing in that mode can ever lift it. Do not restore them by reading the old names as a
 * regression this file lost.
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

			Assert.That(DefconFireDiscipline.HoldsFire(DefconGameMode.Escalation, 3), Is.False, "DEFCON 3 held fire; it is the positioning phase, not the hold.");
			Assert.That(DefconFireDiscipline.HoldsFire(DefconGameMode.Escalation, 2), Is.True, "DEFCON 2 did not hold fire, which is the whole rule.");
			Assert.That(DefconFireDiscipline.HoldsFire(DefconGameMode.Escalation, 1), Is.False, "DEFCON 1 held fire; open war is where autonomous fire returns.");
		}

		[Test]
		public void SkirmishHoldsNoFire()
		{
			// Skirmish is the default game mode and must be a strict no-op. It never leaves NoLevel, so
			// the guard at every read site is a single int compare that is false forever. Driven through
			// the real state machine rather than asserted against a constant, so this fails if Skirmish
			// ever starts moving the level.
			var state = new DefconEscalationState(DefconGameMode.Skirmish, DefconEscalationState.Ceiling, 1);
			Assert.That(DefconFireDiscipline.HoldsFire(DefconGameMode.Skirmish, state.Level), Is.False);

			for (var i = 0; i < 20000; i++)
			{
				state.Tick();
				if (i % 37 == 0)
					state.ReportCasualty();

				Assert.That(DefconFireDiscipline.HoldsFire(DefconGameMode.Skirmish, state.Level), Is.False, $"Skirmish held fire on tick {i}.");

				foreach (var source in AllSources)
					Assert.That(DefconFireDiscipline.Permits(DefconGameMode.Skirmish, state.Level, source, false), Is.True,
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
				Assert.That(DefconFireDiscipline.HoldsFire(DefconGameMode.Escalation, state.Level), Is.False, $"Fire was held at DEFCON 3, on tick {i + 1}.");
			}

			Assert.That(state.Tick(), Is.True);
			Assert.That(DefconFireDiscipline.HoldsFire(DefconGameMode.Escalation, state.Level), Is.True, "The 3 -> 2 drop did not start the hold.");

			// Time alone never ends it; one casualty does, and that restores autonomous fire.
			for (var i = 0; i < 5000; i++)
			{
				state.Tick();
				Assert.That(DefconFireDiscipline.HoldsFire(DefconGameMode.Escalation, state.Level), Is.True, $"The hold lapsed on its own, on tick {i}.");
			}

			Assert.That(state.ReportCasualty(), Is.True);
			Assert.That(DefconFireDiscipline.HoldsFire(DefconGameMode.Escalation, state.Level), Is.False, "DEFCON 1 kept holding fire; open war means autonomous fire is back.");
		}

		[Test]
		public void OnlyEscalationEverHoldsFire()
		{
			// THE SAME LEVEL, THREE MODES, AND THE LEVEL IS NOT THE ANSWER. This is the regression that
			// went the other way until 2026-09-19: the rule keyed on the level alone, Sandbox PINS a
			// level, and a Sandbox match pinned at 2 therefore had units that never fired of their own
			// accord -- forever, because nothing in that mode ever moves the level off 2.
			//
			// Driven through the real state machine in each mode rather than asserted against a literal,
			// so it fails if any of the three ever starts holding a different level.
			var sandbox = new DefconEscalationState(DefconGameMode.Sandbox, DefconFireDiscipline.HoldFireLevel, 1);
			var escalation = new DefconEscalationState(DefconGameMode.Escalation, DefconFireDiscipline.HoldFireLevel, 1);
			var skirmish = new DefconEscalationState(DefconGameMode.Skirmish, DefconFireDiscipline.HoldFireLevel, 1);

			for (var i = 0; i < 5000; i++)
			{
				sandbox.Tick();
				skirmish.Tick();
			}

			Assert.That(sandbox.Level, Is.EqualTo(DefconFireDiscipline.HoldFireLevel), "Sandbox did not pin the level.");
			Assert.That(DefconFireDiscipline.HoldsFire(DefconGameMode.Sandbox, sandbox.Level), Is.False,
				"Sandbox held fire. A pinned level is not a phase -- nothing can lift it, so this is a match nobody can play.");

			Assert.That(escalation.Level, Is.EqualTo(DefconFireDiscipline.HoldFireLevel));
			Assert.That(DefconFireDiscipline.HoldsFire(DefconGameMode.Escalation, escalation.Level), Is.True,
				"Escalation stopped holding fire at its own hold rung, which is the whole feature.");

			// Skirmish by BOTH routes: it forces NoLevel whatever was asked for, and it is not Escalation.
			// The redundancy is deliberate -- see DefconFireDiscipline's header.
			Assert.That(skirmish.Level, Is.EqualTo(DefconEscalationState.NoLevel));
			Assert.That(DefconFireDiscipline.HoldsFire(DefconGameMode.Skirmish, skirmish.Level), Is.False);
			Assert.That(DefconFireDiscipline.HoldsFire(DefconGameMode.Skirmish, DefconFireDiscipline.HoldFireLevel), Is.False,
				"Skirmish held fire when handed the hold rung directly; the mode gate is the second lock and it did not hold.");
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

				Assert.That(DefconFireDiscipline.Permits(DefconGameMode.Escalation, DefconFireDiscipline.HoldFireLevel, source, false),
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
			Assert.That(DefconFireDiscipline.Permits(DefconGameMode.Escalation, Held, AttackSource.Default, false), Is.True,
				"DEFCON 2 refused an ordered shot; the phase is 'free to strike, but only by direct order'.");

			// The unit picked this for itself -- idle rescan, ambush, retaliation, opportunity fire.
			Assert.That(DefconFireDiscipline.Permits(DefconGameMode.Escalation, Held, AttackSource.AutoTarget, false), Is.False);

			// AttackMove is the interesting one, and IsAutoAcquiredSource already settles it: the player
			// ordered a MOVE, not that particular target, so a shot taken along the way is the unit's own
			// decision. It is also the dominant bot engagement mode, so getting this wrong would leave
			// bots fighting a phase in which nobody else may.
			Assert.That(DefconFireDiscipline.Permits(DefconGameMode.Escalation, Held, AttackSource.AttackMove, false), Is.False,
				"An attack-move contact shot counted as ordered; the order was the move, not the target.");
		}

		[Test]
		public void AForceAttackIsAlwaysADirectOrder()
		{
			// A force-attack can only come from a player click, the Lua binding or a deliberate bot
			// order. It arrives as Default today, so this is belt-and-braces -- but if a force-attack
			// ever carried an auto-acquired source it must still fire.
			foreach (var source in AllSources)
				Assert.That(DefconFireDiscipline.Permits(DefconGameMode.Escalation, DefconFireDiscipline.HoldFireLevel, source, true), Is.True,
					$"A force-attack with source {source} was refused at DEFCON 2.");
		}

		[Test]
		public void TheCeaseFireAppliesAtExactlyThePositioningRung()
		{
			// DEFCON 3 is the positioning phase: units are placing themselves behind a wall and nothing
			// shoots. 2 is "weapons free but only by order" and 1 is open war, so the total cease-fire
			// applies at exactly one rung -- and it is NOT the rung the hold-fire rule applies at.
			Assert.That(DefconFireDiscipline.CeaseFireLevel, Is.EqualTo(3));
			Assert.That(DefconFireDiscipline.CeaseFireLevel, Is.EqualTo(DefconEscalationState.Ceiling));
			Assert.That(DefconFireDiscipline.CeaseFireLevel, Is.Not.EqualTo(DefconFireDiscipline.HoldFireLevel));

			Assert.That(DefconFireDiscipline.CeasesFire(DefconGameMode.Escalation, 3), Is.True, "DEFCON 3 did not cease fire; it is the positioning phase.");
			Assert.That(DefconFireDiscipline.CeasesFire(DefconGameMode.Escalation, 2), Is.False, "DEFCON 2 ceased fire outright; it is hold-fire, where ordered shots still land.");
			Assert.That(DefconFireDiscipline.CeasesFire(DefconGameMode.Escalation, 1), Is.False, "DEFCON 1 ceased fire; open war is not a ceasefire.");
		}

		[Test]
		public void SkirmishNeverCeasesFire()
		{
			// The twin of SkirmishHoldsNoFire, and it exists for the same reason: Skirmish is the default
			// game mode and the user tests from main, so a total cease-fire leaking into it would silence
			// every weapon in the ordinary game. Driven through the real state machine rather than
			// asserted against a constant.
			var state = new DefconEscalationState(DefconGameMode.Skirmish, DefconEscalationState.Ceiling, 1);
			Assert.That(DefconFireDiscipline.CeasesFire(DefconGameMode.Skirmish, state.Level), Is.False);

			for (var i = 0; i < 20000; i++)
			{
				state.Tick();
				if (i % 37 == 0)
					state.ReportCasualty();

				Assert.That(DefconFireDiscipline.CeasesFire(DefconGameMode.Skirmish, state.Level), Is.False, $"Skirmish ceased fire on tick {i}.");

				foreach (var isInert in new[] { false, true })
					Assert.That(DefconFireDiscipline.PermitsWeapon(DefconGameMode.Skirmish, state.Level, isInert), Is.True,
						$"Skirmish refused a weapon on tick {i} (isInert: {isInert}).");
			}
		}

		[Test]
		public void NothingFiresDuringPositioningExceptWhatOptedOut()
		{
			// THE WHOLE RULE, and the reason it takes no AttackSource. At DEFCON 2 the question is who
			// ordered the shot; here there is no such question, because the answer never changes the
			// outcome. An ordered attack, an attack-move contact shot and a force-fire at bare ground are
			// all refused identically -- force-fire being the one the rule exists to close, since the
			// DEFCON 2 predicate deliberately exempts it.
			const int Positioning = DefconFireDiscipline.CeaseFireLevel;

			Assert.That(DefconFireDiscipline.PermitsWeapon(DefconGameMode.Escalation, Positioning, false), Is.False,
				"A weapon fired during Positioning; no weapon may.");

			Assert.That(DefconFireDiscipline.PermitsWeapon(DefconGameMode.Escalation, Positioning, true), Is.True,
				"An inert armament was silenced during Positioning; the carve-out is what lets a medic heal and a drone launch.");
		}

		[Test]
		public void TheTwoRulesDisagreeAboutForceFireOnPurpose()
		{
			// THE ANTI-CONFLATION TEST. These two rules are not the same rule at different strengths, and
			// the single sharpest difference is force-fire: at DEFCON 2 a force-attack is the clearest
			// evidence a human ordered the shot and it is exempt, while at DEFCON 3 it is the documented
			// bypass -- force-fire at bare ground needs no target actor, so anything keyed on provenance
			// waves it straight through. If a future edit ever unifies these predicates, this fails.
			Assert.That(DefconFireDiscipline.Permits(DefconGameMode.Escalation, DefconFireDiscipline.HoldFireLevel, AttackSource.Default, true), Is.True,
				"DEFCON 2 refused a force-attack; that phase is 'free to strike, but only by direct order'.");

			Assert.That(DefconFireDiscipline.PermitsWeapon(DefconGameMode.Escalation, DefconFireDiscipline.CeaseFireLevel, false), Is.False,
				"DEFCON 3 let a shot through; nothing fires during Positioning, however it was ordered.");

			// And the rungs themselves must not collide, which is what keeps each rule confined to one phase.
			Assert.That(DefconFireDiscipline.HoldsFire(DefconGameMode.Escalation, DefconFireDiscipline.CeaseFireLevel), Is.False);
			Assert.That(DefconFireDiscipline.CeasesFire(DefconGameMode.Escalation, DefconFireDiscipline.HoldFireLevel), Is.False);
		}

		[Test]
		public void EveryLevelButPositioningFiresFreely()
		{
			// Outside the one ceased rung the weapon predicate must be transparent whether or not the
			// armament opted out -- this is what makes the carve-out flag inert at DEFCON 2, at DEFCON 1
			// and outside the mode, so a `FiresDuringCeaseFire` armament is an ORDINARY armament
			// everywhere except during Positioning.
			var levels = new[] { DefconEscalationState.NoLevel, DefconEscalationState.Floor, DefconFireDiscipline.HoldFireLevel };

			foreach (var level in levels)
				foreach (var isInert in new[] { false, true })
					Assert.That(DefconFireDiscipline.PermitsWeapon(DefconGameMode.Escalation, level, isInert), Is.True,
						$"Level {level} silenced a weapon (isInert: {isInert}).");
		}

		[Test]
		public void AnEscalationMatchCeasesFireForExactlyTheOpeningPhase()
		{
			// The shipped sequence from the weapon's point of view: silent through the whole opening
			// phase, then firing for the rest of the match. Note this runs the OPPOSITE way round to
			// AnEscalationMatchHoldsFireForExactlyTheSecondPhase -- the cease-fire is on at the start and
			// never comes back, which is what makes it a phase rather than a state.
			const int ClockTicks = 100;
			var state = new DefconEscalationState(DefconGameMode.Escalation, DefconEscalationState.Ceiling, ClockTicks);

			Assert.That(DefconFireDiscipline.PermitsWeapon(DefconGameMode.Escalation, state.Level, false), Is.False, "The match opened with weapons live.");

			for (var i = 0; i < ClockTicks - 1; i++)
			{
				state.Tick();
				Assert.That(DefconFireDiscipline.CeasesFire(DefconGameMode.Escalation, state.Level), Is.True, $"The cease-fire lapsed early, on tick {i + 1}.");

				// The carve-out holds for the whole phase, not just its first tick.
				Assert.That(DefconFireDiscipline.PermitsWeapon(DefconGameMode.Escalation, state.Level, true), Is.True, $"An inert armament was silenced on tick {i + 1}.");
			}

			Assert.That(state.Tick(), Is.True);
			Assert.That(DefconFireDiscipline.CeasesFire(DefconGameMode.Escalation, state.Level), Is.False, "The 3 -> 2 drop did not end the cease-fire.");
			Assert.That(DefconFireDiscipline.PermitsWeapon(DefconGameMode.Escalation, state.Level, false), Is.True, "Weapons stayed silent past the end of Positioning.");

			// ...and it never comes back, including across the 2 -> 1 casualty drop.
			Assert.That(state.ReportCasualty(), Is.True);
			Assert.That(DefconFireDiscipline.PermitsWeapon(DefconGameMode.Escalation, state.Level, false), Is.True);
		}

		[Test]
		public void OnlyEscalationEverCeasesFire()
		{
			// THE WORSE HALF OF THE SAME BUG, and the one a host met by default: Start At ships at 3,
			// Positioning, so a Sandbox match taken straight off the dropdown had EVERY weapon on the map
			// refused -- autotargeted, ordered and force-fired alike -- from the first tick to the last,
			// with no clock anywhere that could lift it. The level was pinned and the rule keyed on the
			// level. Sandbox is off the dropdown now AND this rule is mode-gated; either alone would have
			// fixed the symptom, and the pair is what makes it not come back through a scenario setting
			// ModeDefault.
			var sandbox = new DefconEscalationState(DefconGameMode.Sandbox, DefconFireDiscipline.CeaseFireLevel, 1);

			for (var i = 0; i < 5000; i++)
				sandbox.Tick();

			Assert.That(sandbox.Level, Is.EqualTo(DefconFireDiscipline.CeaseFireLevel), "Sandbox did not pin the level.");
			Assert.That(DefconFireDiscipline.CeasesFire(DefconGameMode.Sandbox, sandbox.Level), Is.False);

			// The consequence, stated as the thing a player would notice rather than as the predicate:
			// an ordinary weapon fires, at the level that used to silence it.
			Assert.That(DefconFireDiscipline.PermitsWeapon(DefconGameMode.Sandbox, sandbox.Level, false), Is.True,
				"A Sandbox match pinned at Positioning still silences every weapon it has.");

			Assert.That(DefconFireDiscipline.CeasesFire(DefconGameMode.Escalation, DefconFireDiscipline.CeaseFireLevel), Is.True,
				"Escalation stopped ceasing fire during Positioning, which is the whole feature.");
			Assert.That(DefconFireDiscipline.PermitsWeapon(DefconGameMode.Skirmish, DefconFireDiscipline.CeaseFireLevel, false), Is.True,
				"Skirmish refused a weapon when handed the Positioning rung directly.");
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
						Assert.That(DefconFireDiscipline.Permits(DefconGameMode.Escalation, level, source, forceAttack), Is.True,
							$"Level {level} refused a {source} shot (forceAttack: {forceAttack}).");
		}
	}
}
