#region Copyright & License Information
/*
 * WW3MOD — external (FIFO) unit-request cap tests.
 *
 * Pins the cap that stops AdaptiveProductionBotModule's call-ins buying a type without bound. The lane it
 * feeds applies no UnitsToBuild/UnitDelays/UnitLimits, and the composition ceiling cannot cover it: the
 * request lands on the first ENABLED twin, which on all four live profiles is a .fixedwing twin that sets no
 * CompositionDirected, so compositionTypes is null and the ceiling exits at its first guard for EVERY type.
 *
 * The load-bearing case is CrossTwin: heli's cap is authored on the .heli twin while the request drains on
 * the .fixedwing twin, so a limit read off the draining twin alone finds nothing and the buy is unbounded.
 * Pure integer decision; no world mounted.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class ExternalRequestLimitMathTest
	{
		[Test]
		public void NoAuthoredLimit_IsUncapped_ByteIdentity()
		{
			// The frozen path. Every SR-defense counter type (at.america, abrams, e3.america, aa.america …)
			// has no UnitLimits entry on any twin, so this lane must behave exactly as it did uncapped —
			// otherwise turning the cap on would silently throttle @stable's counter-buys.
			Assert.That(ExternalRequestLimitMath.TightestLimit(new[] { true, true }, new[] { 0, 0 }), Is.EqualTo(0));
			Assert.That(ExternalRequestLimitMath.IsOverLimit(0, 0), Is.False);
			Assert.That(ExternalRequestLimitMath.IsOverLimit(99, 0), Is.False);
		}

		[Test]
		public void CrossTwin_LimitBindsWhereItWasAuthored()
		{
			// THE BUG THIS EXISTS FOR. Live @experimental NATO: [0]=@russia.fixedwing (disabled),
			// [1]=@experimental.russia.heli (disabled), [2]=@america.fixedwing (ENABLED, drains the FIFO,
			// authors NO heli limit), [3]=@experimental.america.heli (ENABLED, authors UnitLimits heli: 1).
			// Reading the draining twin alone yields 0 (uncapped); resolving across twins yields 1.
			var enabled = new[] { false, false, true, true };
			var heliLimits = new[] { 0, 1, 0, 1 };

			Assert.That(ExternalRequestLimitMath.TightestLimit(enabled, heliLimits), Is.EqualTo(1));

			// At the cap we stop: one heli standing refuses the second. This is the "bot buys too many
			// helicopters" complaint, expressed as an assertion.
			Assert.That(ExternalRequestLimitMath.IsOverLimit(0, 1), Is.False);
			Assert.That(ExternalRequestLimitMath.IsOverLimit(1, 1), Is.True);
			Assert.That(ExternalRequestLimitMath.IsOverLimit(4, 1), Is.True);
		}

		[Test]
		public void DisabledTwinsCannotCap()
		{
			// A disabled twin's BotTick never runs, so its roster opinion is not in force this game. The
			// @stable NATO layout authors heli: 4 on @america.heli while @experimental.america.heli (heli: 1)
			// is disabled — the stable profile must get 4, not the experimental profile's 1.
			Assert.That(ExternalRequestLimitMath.TightestLimit(new[] { true, false }, new[] { 4, 1 }), Is.EqualTo(4));

			// And with every twin disabled there is no cap at all rather than a cap of zero.
			Assert.That(ExternalRequestLimitMath.TightestLimit(new[] { false, false }, new[] { 4, 1 }), Is.EqualTo(0));
		}

		[Test]
		public void StrictestEnabledAuthorWins()
		{
			// Minimum, not first-match: refusing a buy the uncapped lane would have made is the safe
			// direction, permitting one it would have refused is not.
			Assert.That(ExternalRequestLimitMath.TightestLimit(new[] { true, true, true }, new[] { 12, 2, 3 }), Is.EqualTo(2));
			Assert.That(ExternalRequestLimitMath.TightestLimit(new[] { true, true, true }, new[] { 2, 12, 3 }), Is.EqualTo(2));
		}

		[Test]
		public void DegenerateInputsAreUncapped()
		{
			Assert.That(ExternalRequestLimitMath.TightestLimit(null, new[] { 1 }), Is.EqualTo(0));
			Assert.That(ExternalRequestLimitMath.TightestLimit(new[] { true }, null), Is.EqualTo(0));
			Assert.That(ExternalRequestLimitMath.TightestLimit(new bool[0], new int[0]), Is.EqualTo(0));

			// Ragged arrays must not throw — the shorter length governs.
			Assert.That(ExternalRequestLimitMath.TightestLimit(new[] { true }, new[] { 3, 1 }), Is.EqualTo(3));
			Assert.That(ExternalRequestLimitMath.TightestLimit(new[] { true, true }, new[] { 3 }), Is.EqualTo(3));

			// A negative authored limit is treated as "none authored", never as a cap.
			Assert.That(ExternalRequestLimitMath.TightestLimit(new[] { true }, new[] { -5 }), Is.EqualTo(0));
		}
	}
}
