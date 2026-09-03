#region Copyright & License Information
/*
 * Pins the Supply Route rally replay against the regression that shipped with the feature: the
 * eligibility test for an Alt-tagged waypoint was `move is Mobile`, so every aircraft's attack-move
 * waypoint silently became a plain Move. The player saw a RED rally line from the SR and then a
 * GREEN move on the helicopter that arrived — the tag survived encoding, ordering and rendering, and
 * was thrown away at the last step.
 *
 * The contract these tests hold is agreement between what the SR line PROMISED and what the arriving
 * unit DOES, in both directions:
 *   - a unit that can attack-move gets an attack-move, whatever it moves on;
 *   - a unit that cannot gets a plain Move AND the plain Move colour, so the line never claims an
 *     engagement that will not happen.
 *
 * Resolve takes a bool rather than an Actor precisely so this fixture needs no World. The bool's
 * production value is AttackMove.CanBeOrderedToAttackMove — deliberately the same predicate the
 * attack-move cursor and AttackMove.ResolveOrder consult, so the SR cannot drift from the click.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;

namespace OpenRA.Test
{
	[TestFixture]
	public class RallyOrderReplayMathTest
	{
		[Test]
		public void AttackMoveWaypointReplaysAsAttackMoveForAnyCapableUnit()
		{
			// The bug: this answered Move whenever the unit's IMove was not Mobile.
			var plan = RallyOrderReplayMath.Resolve(RallyOrderType.AttackMove, canAttackMove: true);

			Assert.That(plan.Effective, Is.EqualTo(RallyOrderType.AttackMove));
			Assert.That(plan.TargetLineColor, Is.EqualTo(Color.OrangeRed));
			Assert.That(plan.NearEnoughCells, Is.EqualTo(1));
		}

		[Test]
		public void AttackMoveWaypointFallsBackToPlainMoveWhenTheUnitCannotAttackMove()
		{
			// LCCV carries -AttackMove: (vehicles.yaml). It must not merely lose the attack-move —
			// it must also lose the orange-red line, or the line lies about what the unit will do.
			var plan = RallyOrderReplayMath.Resolve(RallyOrderType.AttackMove, canAttackMove: false);

			Assert.That(plan.Effective, Is.EqualTo(RallyOrderType.Move));
			Assert.That(plan.TargetLineColor, Is.EqualTo(Color.Green));
			Assert.That(plan.NearEnoughCells, Is.EqualTo(2));
		}

		[Test]
		public void ForceMoveAndMoveWaypointsDoNotConsultAttackMoveEligibility()
		{
			// Neither type engages anything, so the eligibility bool must not reach them. If a future
			// edit folds the capability check in at the top of Resolve, these two go red.
			foreach (var capable in new[] { true, false })
			{
				var force = RallyOrderReplayMath.Resolve(RallyOrderType.ForceMove, capable);
				Assert.That(force.Effective, Is.EqualTo(RallyOrderType.ForceMove));
				Assert.That(force.TargetLineColor, Is.EqualTo(Color.DeepSkyBlue));
				Assert.That(force.NearEnoughCells, Is.EqualTo(2));

				var move = RallyOrderReplayMath.Resolve(RallyOrderType.Move, capable);
				Assert.That(move.Effective, Is.EqualTo(RallyOrderType.Move));
				Assert.That(move.TargetLineColor, Is.EqualTo(Color.Green));
				Assert.That(move.NearEnoughCells, Is.EqualTo(2));
			}
		}

		[Test]
		public void UnknownWaypointTypesDegradeToPlainMove()
		{
			// RallyOrderType reserves 3..7 for future per-waypoint orders (RallyPoint.cs). Until one is
			// implemented, an unrecognised value must move the unit rather than throw at spawn time.
			var plan = RallyOrderReplayMath.Resolve((RallyOrderType)7, canAttackMove: true);

			Assert.That(plan.Effective, Is.EqualTo(RallyOrderType.Move));
			Assert.That(plan.TargetLineColor, Is.EqualTo(Color.Green));
		}
	}
}
