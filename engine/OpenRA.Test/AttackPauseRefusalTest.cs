#region Copyright & License Information
/*
 * WW3MOD AttackBase.RefusesForPause tests — the ONE question a cursor is allowed to ask about a
 * paused weapon, and the far larger set of cases where it must stay silent.
 *
 * THE ASYMMETRY UNDER TEST, and it is the whole point. A paused armament does not behave like a dry
 * one. Without AbandonWhenArmamentsPaused the order is ACCEPTED — Attack.TickAttack falls through,
 * the unit closes to range and aims, and holds through the pause, which is the wanted default for a
 * brief one. With the opt-in it returns UnableToAttack on the first tick and the activity completes
 * having moved nothing. Only the second is a refusal, so only the second may be answered at the
 * cursor. Answering the first would claim a refusal that does not happen — the error that got the
 * minelayer cursor retracted on 2026-08-30 after it painted `deploy-blocked` over an order that
 * really did complete.
 *
 * WHY A SHARED PREDICATE RATHER THAN A SECOND COPY. Attack.TickAttack and AttackOrderTargeter must
 * agree by construction, because a disagreement between them is invisible until a player reports a
 * cursor that promised something. The two callers derive their armament lists separately; the
 * DECISION is derived once, here.
 *
 * The armament pause states are plain bools, not Armaments, so the polarity can be pinned without a
 * World — the same reason OrderReadinessMathTest drives its combinator with ints.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class AttackPauseRefusalTest
	{
		const bool OptIn = true;
		const bool NoOptIn = false;
		const bool BasePaused = true;
		const bool BaseLive = false;

		[Test]
		public void WithoutTheOptInNothingIsEverRefused()
		{
			// The default for every actor in the mod but the medic. The order is accepted and the unit
			// closes and aims, so the cursor must keep promising it however long the pause lasts. This is
			// the heavily-damaged-tank case: `heavy-damage-attained` persists until the tank is repaired,
			// and it is STILL not a refusal.
			Assert.That(AttackBase.RefusesForPause(NoOptIn, BaseLive, new[] { true }), Is.False);
			Assert.That(AttackBase.RefusesForPause(NoOptIn, BaseLive, new[] { true, true, true }), Is.False);
			Assert.That(AttackBase.RefusesForPause(NoOptIn, BasePaused, new[] { true }), Is.False);
		}

		[Test]
		public void WithTheOptInEveryArmamentPausedIsARefusal()
		{
			Assert.That(AttackBase.RefusesForPause(OptIn, BaseLive, new[] { true }), Is.True);
			Assert.That(AttackBase.RefusesForPause(OptIn, BaseLive, new[] { true, true, true }), Is.True);
		}

		[Test]
		public void OneLiveArmamentIsEnoughToKeepTheOrder()
		{
			// ALL, not ANY. A rifleman with a spent rifle and a loaded RPG can still fight, and the
			// mirror reading — refuse as soon as anything is paused — is the same sentence in English.
			Assert.That(AttackBase.RefusesForPause(OptIn, BaseLive, new[] { false }), Is.False);
			Assert.That(AttackBase.RefusesForPause(OptIn, BaseLive, new[] { true, false }), Is.False);
			Assert.That(AttackBase.RefusesForPause(OptIn, BaseLive, new[] { false, true }), Is.False);
		}

		[Test]
		public void ThePausedBaseRefusesEvenWithEveryArmamentLive()
		{
			// DoAttack skips every armament wholesale on the base's own pause, so this wedges identically.
			// It is the live entrance today: ^MEDI pauses its AttackFrontal on `garrisoned-at-port` and
			// its heal Armament carries no pause gate at all, so this row is the medic, not a hypothetical.
			Assert.That(AttackBase.RefusesForPause(OptIn, BasePaused, new[] { false }), Is.True);
			Assert.That(AttackBase.RefusesForPause(OptIn, BasePaused, new[] { false, false }), Is.True);
		}

		[Test]
		public void AnEmptyArmamentSetIsNotARefusal()
		{
			// Unreachable — both callers return before this on an empty set — and pinned because the
			// List.TrueForAll this replaced answered the opposite way. Of the two vacuous answers, the
			// false refusal is the one this codebase has actually shipped and had to retract.
			Assert.That(AttackBase.RefusesForPause(OptIn, BaseLive, new bool[0]), Is.False);

			// The base-pause term still bites, because it does not depend on the armaments at all.
			Assert.That(AttackBase.RefusesForPause(OptIn, BasePaused, new bool[0]), Is.True);
		}
	}
}
