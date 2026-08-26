#region Copyright & License Information
/*
 * Pins the condition-token cap in GrantConditionOnPreparingAttack. Pure-math
 * test; no Actor / World. Reported from playtest 260827: a Tunguska that had
 * emptied its 30mm pool into ground targets could not fire its 9M311 missiles
 * for minutes afterwards, with eight missiles still loaded.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.Common.Test
{
	[TestFixture]
	public class PreparingAttackTokenStackTest
	{
		// mods/ww3mod/rules/ingame/vehicles-russia.yaml, actor `tunguska`.
		const int TunguskaGunPool = 180;      // AmmoPool@1 Ammo, armaments primary + primary-air
		const int DefaultRevokeDelay = 50;    // GrantConditionOnPreparingAttack@1 leaves RevokeDelay unset
		const int DefaultMaximumInstances = 1;

		// mod.yaml GameSpeeds default Timestep is 60ms, i.e. 16.67 ticks/second.
		// Not 25 — see DOCS/reference/conventions.md on the tick-rate error.
		// Multiply before dividing; ticks/second is not an integer.
		const int DefaultTimestepMs = 60;
		static int Seconds(int ticks) => ticks * DefaultTimestepMs / 1000;

		// Drives the real predicate the trait consults on every shot.
		static int StackAfter(int shots, int maximumInstances)
		{
			var tokens = 0;
			for (var i = 0; i < shots; i++)
				if (GrantConditionOnPreparingAttack.ShouldGrantInstance(tokens, maximumInstances))
					tokens++;

			return tokens;
		}

		// Tick pops exactly one token per RevokeDelay when RevokeAll is false,
		// which is the default and what all fourteen ww3mod sites use.
		static int TicksToDrain(int tokens) => tokens * DefaultRevokeDelay;

		[Test]
		public void SustainedFireCannotStackPastTheCap()
		{
			var tokens = StackAfter(TunguskaGunPool, DefaultMaximumInstances);

			Assert.AreEqual(DefaultMaximumInstances, tokens,
				$"Firing {TunguskaGunPool} gun rounds left {tokens} instances of `firing-primary` on the stack. " +
				$"Tick revokes one per {DefaultRevokeDelay} ticks, so Armament@2 (9M311) stays paused for " +
				$"{Seconds(TicksToDrain(tokens))}s after the guns stop — the missiles are locked out " +
				"with a full pool. Expected the stack to be capped at MaximumInstances.");
		}

		[Test]
		public void MissileLockoutClearsWithinOneRevokeDelay()
		{
			var drain = TicksToDrain(StackAfter(TunguskaGunPool, DefaultMaximumInstances));

			Assert.AreEqual(DefaultRevokeDelay, drain,
				$"`firing-primary` took {drain} ticks ({Seconds(drain)}s) to drain after a full gun " +
				$"magazine. It must clear one RevokeDelay ({DefaultRevokeDelay} ticks, " +
				$"{Seconds(DefaultRevokeDelay)}s) after the last shot regardless of how many were fired.");
		}

		[Test]
		public void LockoutDoesNotNeedAmmoExhaustion()
		{
			// The user guessed an empty gun pool caused it. The real predictor is the
			// shot count: a burst well short of the pool must not stack either.
			Assert.AreEqual(DefaultMaximumInstances, StackAfter(30, DefaultMaximumInstances),
				"A 30-round burst with 150 rounds still loaded stacked past the cap. " +
				"The lockout is driven by shots fired, not by the pool running dry.");
		}

		[Test]
		public void TunguskaMobilityStackIsCappedToo()
		{
			// Second instance on the same actor: GrantConditionOnPreparingAttack@2
			// grants `firing-secondary`, which Mobile pauses on. Eight missiles used
			// to mean eight tokens and 24s of immobility.
			Assert.AreEqual(DefaultMaximumInstances, StackAfter(8, DefaultMaximumInstances),
				"Firing all eight 9M311 stacked `firing-secondary`, pinning Mobile.PauseOnCondition " +
				"long after the last launch.");
		}

		[Test]
		public void CapIsHonouredAboveOne()
		{
			// MaximumInstances stays meaningful: no ww3mod site sets it today, but the
			// fix must cap rather than hard-code a single instance.
			Assert.AreEqual(3, StackAfter(TunguskaGunPool, 3));
			Assert.AreEqual(2, StackAfter(2, 5), "Fewer shots than the cap must not over-grant.");
		}

		[Test]
		public void EveryShotBelowTheCapStillGrants()
		{
			// Guard against over-correcting into "grant once, ever".
			Assert.IsTrue(GrantConditionOnPreparingAttack.ShouldGrantInstance(0, 1));
			Assert.IsFalse(GrantConditionOnPreparingAttack.ShouldGrantInstance(1, 1));
			Assert.IsTrue(GrantConditionOnPreparingAttack.ShouldGrantInstance(2, 3));
			Assert.IsFalse(GrantConditionOnPreparingAttack.ShouldGrantInstance(3, 3));
		}
	}
}
