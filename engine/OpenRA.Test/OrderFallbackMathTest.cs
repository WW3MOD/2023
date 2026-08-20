#region Copyright & License Information
/*
 * WW3MOD OrderFallbackMath tests — the gate on UnitOrderGenerator's terrain retry.
 *
 * The behaviour under test: an attack order a unit CANNOT EXECUTE must leave that unit alone. It
 * used to be handed a bare Move onto the target's cell, which walked unarmed and wrong-armament
 * units into the enemy for no purpose.
 *
 * These pins exist because the gate sits on a mouse-click path — no scripted API on this branch's
 * parent could reach it, so the decision cannot be exercised without a human clicking. They pin the
 * decision table only; whether the retry is correctly WIRED to it is the Lua scenario's job
 * (tools/autotest/scenarios/test-order-no-move-fallback).
 *
 * HALF OF THIS FILE IS THERE TO FAIL THE OBVIOUS WRONG FIX. "Remove the move fallback" reads like
 * "delete the second pass of the loop", and that pass is the ONLY route to a Move order on a cell
 * that any actor occupies — MoveOrderTargeter and AttackMoveTargeter both reject non-terrain
 * targets. Deleting it would take out ordinary movement onto a friendly unit's cell, force-move
 * (Ctrl) onto anything at all, and attack-move (Alt) onto an occupied cell. Each of those has a
 * test below and each of them goes red under that fix.
 *
 * NOT PINNED HERE, because it never reaches this gate: a target that is merely OUT OF RANGE. The
 * attack targeter accepts those (AttackBase.cs:747-753, swapping in OutsideRangeCursor), so they
 * return a real Attack order from the first pass and the unit closes the distance as it always did.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Orders;
using OpenRA.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class OrderFallbackMathTest
	{
		// The instruction, in one assertion: ordered onto an enemy it cannot engage, the unit gets
		// nothing rather than a move.
		[Test]
		public void AnEnemyItCannotEngageProducesNoOrderAtAll()
		{
			Assert.That(OrderFallbackMath.AllowsMoveFallback(true, PlayerRelationship.Enemy, TargetModifiers.None),
				Is.False, "a refused attack on an enemy must not be retried as a move order");
		}

		// Queuing with Shift is not a movement intent — a shift-clicked attack a unit cannot execute
		// must be dropped just the same, not appended to its queue as a walk into the target.
		[Test]
		public void QueueingDoesNotReopenTheFallback()
		{
			Assert.That(OrderFallbackMath.AllowsMoveFallback(true, PlayerRelationship.Enemy, TargetModifiers.ForceQueue),
				Is.False);
		}

		// Ctrl+clicking an enemy is a force-ATTACK, and a unit that still cannot engage it — no
		// weapon valid against that target type at all — must stay put rather than drive over.
		[Test]
		public void ForceAttackOnAnEnemyIsStillNotAMoveRequest()
		{
			Assert.That(OrderFallbackMath.AllowsMoveFallback(true, PlayerRelationship.Enemy, TargetModifiers.ForceAttack),
				Is.False);
		}

		// ---------- the cases the naive "delete the second pass" fix would break ----------

		// Right-clicking the cell a friendly unit stands on is an ordinary move request and always
		// was. This is the single most common click in the game.
		[Test]
		public void MovingOntoAFriendlyUnitsCellStillWorks()
		{
			Assert.Multiple(() =>
			{
				Assert.That(OrderFallbackMath.AllowsMoveFallback(true, PlayerRelationship.Ally, TargetModifiers.None), Is.True);
				Assert.That(OrderFallbackMath.AllowsMoveFallback(true, PlayerRelationship.None, TargetModifiers.None), Is.True,
					"own units read as None, not Ally — the relationship check must not miss them");
			});
		}

		// Trees, civilians and unowned props are Neutral and targetable; clicking one is a move.
		[Test]
		public void MovingOntoANeutralPropStillWorks()
		{
			Assert.That(OrderFallbackMath.AllowsMoveFallback(true, PlayerRelationship.Neutral, TargetModifiers.None), Is.True);
		}

		// Force-move reaches its order ONLY through the retry, for every target including enemies.
		// Gating it here would delete the gesture outright.
		[Test]
		public void ForceMoveOntoAnEnemyIsExplicitAndSurvives()
		{
			Assert.That(OrderFallbackMath.AllowsMoveFallback(true, PlayerRelationship.Enemy, TargetModifiers.ForceMove), Is.True,
				"holding force-move is the player asking to move onto that cell, enemy or not");
		}

		// Same reasoning for attack-move: the targeter is terrain-only, so an Alt-click on an
		// occupied cell depends on the retry.
		[Test]
		public void AttackMoveOntoAnEnemyIsExplicitAndSurvives()
		{
			Assert.That(OrderFallbackMath.AllowsMoveFallback(true, PlayerRelationship.Enemy, TargetModifiers.AttackMove), Is.True);
		}

		// A click on bare ground never had an actor to refuse in the first place; the gate must be
		// transparent to it or every ordinary move order dies.
		[Test]
		public void AClickOnBareGroundIsNeverGated()
		{
			Assert.Multiple(() =>
			{
				Assert.That(OrderFallbackMath.AllowsMoveFallback(false, PlayerRelationship.Enemy, TargetModifiers.None), Is.True);
				Assert.That(OrderFallbackMath.AllowsMoveFallback(false, PlayerRelationship.None, TargetModifiers.None), Is.True);
			});
		}
	}
}
