#region Copyright & License Information
/*
 * WW3MOD MovementModifierMath tests — the precedence rule that lets a held attack-move modifier beat
 * the actor-targeting orders that outrank it.
 *
 * These pins are deliberately split between the two things that can go wrong, because the fix has an
 * obvious over-reach and an obvious under-reach and only one of them is visible from the bug report:
 *
 *   - UNDER-REACH is the reported bug. A medic in the selection, or any infantry carrying Passenger,
 *     stole the click from attack-move over a friendly unit.
 *   - OVER-REACH is the regression nobody would notice until a match was lost by it: suppressing an
 *     ENEMY-facing or NEUTRAL-facing order under the same modifier would delete attack, capture, and
 *     the Supply Route attack that decides the game. Those cases are pinned as FALSE on purpose.
 *
 * The unmodified-click case is the one to read first. It is what guarantees the fix did not turn into
 * "attack-move always wins": with no modifier held, a click on a friendly transport must still load and
 * a click on a wounded ally must still heal.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Orders;
using OpenRA.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class MovementModifierMathTest
	{
		// THE SCOPE PIN. An ordinary click is not a declaration of intent, so nothing steps aside and
		// every actor-targeting order keeps working exactly as it did.
		[Test]
		public void AnUnmodifiedClickNeverDisplacesAnActorOrder()
		{
			Assert.Multiple(() =>
			{
				Assert.That(MovementModifierMath.YieldsToMovementOrder(TargetModifiers.None, PlayerRelationship.Ally), Is.False,
					"plain-clicking a friendly transport must still load, and a wounded ally must still heal");
				Assert.That(MovementModifierMath.YieldsToMovementOrder(TargetModifiers.None, PlayerRelationship.Enemy), Is.False);
				Assert.That(MovementModifierMath.YieldsToMovementOrder(TargetModifiers.None, PlayerRelationship.Neutral), Is.False);
			});
		}

		// The reported bug: Alt over a friendly unit. Ally covers the player's own units too, because
		// Player.RelationshipWith returns Ally for self.
		[Test]
		public void AHeldAttackMoveModifierDisplacesAnAllyOrder()
		{
			Assert.That(MovementModifierMath.YieldsToMovementOrder(TargetModifiers.AttackMove, PlayerRelationship.Ally), Is.True,
				"AttendAlly (7) and EnterTransport (5) outrank AttackMove (4) and were naming the click");
		}

		// Shift+Alt is a QUEUED attack-move and sets ForceQueue alongside AttackMove. It must behave
		// identically; a test that only ever passes the bare flag would miss a HasModifier written as an
		// equality comparison.
		[Test]
		public void AQueuedAttackMoveDisplacesTheSameOrders()
		{
			Assert.That(
				MovementModifierMath.YieldsToMovementOrder(TargetModifiers.AttackMove | TargetModifiers.ForceQueue, PlayerRelationship.Ally),
				Is.True);
		}

		// ---------- the over-reach cases: orders that must SURVIVE a held attack-move modifier ----------

		// Alt-clicking an enemy should still attack it rather than walk at it, and the Supply Route
		// attack (priority 8) is the mod's win condition.
		[Test]
		public void AHostileTargetKeepsItsOwnOrder()
		{
			Assert.That(MovementModifierMath.YieldsToMovementOrder(TargetModifiers.AttackMove, PlayerRelationship.Enemy), Is.False,
				"Attack (6) and AttacksSupplyRoutes (8) must not be deleted by holding the movement modifier");
		}

		// Captures leaves ValidRelationships at the Neutral|Enemy default, so a neutral capturable must
		// still be capturable with the modifier held.
		[Test]
		public void ANeutralTargetKeepsItsOwnOrder()
		{
			Assert.Multiple(() =>
			{
				Assert.That(MovementModifierMath.YieldsToMovementOrder(TargetModifiers.AttackMove, PlayerRelationship.Neutral), Is.False,
					"capturing a neutral structure must survive a held attack-move modifier");
				Assert.That(MovementModifierMath.YieldsToMovementOrder(TargetModifiers.AttackMove, PlayerRelationship.None), Is.False);
			});
		}

		// Force-move is NOT covered, and this pin states that as a decision rather than leaving it to be
		// read as an oversight. Ctrl is load-bearing as the TRIGGER for ForceEnter (6), ForceDock (6),
		// Carryall drop-off (6) and force-boarding a transport; suppressing on it would delete all four.
		[Test]
		public void ForceMoveIsDeliberatelyNotCovered()
		{
			Assert.That(MovementModifierMath.YieldsToMovementOrder(TargetModifiers.ForceMove, PlayerRelationship.Ally), Is.False,
				"Ctrl is how force-enter and force-dock are REQUESTED; yielding on it would delete those gestures");
		}
	}
}
