#region Copyright & License Information
/*
 * WW3MOD @experimental — AdaptiveProduction unit-request routing tests.
 *
 * Pins the "which UnitBuilder twin gets the call-in" decision AdaptiveProductionBotModule.SelectUnitProducer
 * turns into a production request, so the routing fix can't silently regress AND @stable byte-identity is
 * proven:
 *   * routeToEnabled OFF ⇒ the FROZEN path: always producer index 0, EVEN when it is disabled (the legacy
 *     unitProducers[0] behaviour the @stable twin must keep byte-identical).
 *   * routeToEnabled ON (@experimental) ⇒ the first ENABLED producer, skipping the condition-disabled twins
 *     whose BotTick never runs. Reproduces the live @experimental NATO layout where producer[0]
 *     (@russia.fixedwing) is disabled and the counter-buy must fall through to the enabled ground builder.
 * Pure integer decision; no world mounted.
 */
#endregion

using OpenRA.Mods.Common.Traits;
using NUnit.Framework;

namespace OpenRA.Test
{
	[TestFixture]
	public class AdaptiveRoutingMathTest
	{
		[Test]
		public void FrozenPath_AlwaysIndexZero_ByteIdentity()
		{
			// routeToEnabled OFF reproduces legacy unitProducers[0] verbatim — even when index 0 is DISABLED
			// (the @stable NATO layout, where producer[0] @russia.fixedwing is disabled and the buy is lost).
			Assert.That(AdaptiveRoutingMath.SelectProducerIndex(new[] { false, false, true }, false), Is.EqualTo(0));
			Assert.That(AdaptiveRoutingMath.SelectProducerIndex(new[] { true, true, true }, false), Is.EqualTo(0));
			Assert.That(AdaptiveRoutingMath.SelectProducerIndex(new[] { true }, false), Is.EqualTo(0));
		}

		[Test]
		public void RouteToEnabled_PicksFirstEnabled_SkippingDisabledTwins()
		{
			// The live @experimental NATO layout: producer[0]=@russia.fixedwing (disabled), [1]=@russia.heli
			// (disabled), [2]=@america.fixedwing (ENABLED). The buy must land on index 2, not 0.
			Assert.That(AdaptiveRoutingMath.SelectProducerIndex(new[] { false, false, true, true, false, true }, true), Is.EqualTo(2));
		}

		[Test]
		public void RouteToEnabled_IndexZeroEnabled_ReturnsZero_NoChangeForBrics()
		{
			// @experimental BRICS: producer[0]=@russia.fixedwing is ENABLED, so first-enabled == 0 == the legacy
			// target. The fix is a no-op there — byte-identical for BRICS experimental.
			Assert.That(AdaptiveRoutingMath.SelectProducerIndex(new[] { true, false, true }, true), Is.EqualTo(0));
		}

		[Test]
		public void RouteToEnabled_NoneEnabled_ReturnsMinusOne()
		{
			Assert.That(AdaptiveRoutingMath.SelectProducerIndex(new[] { false, false, false }, true), Is.EqualTo(-1));
		}

		[Test]
		public void EmptyOrNull_ReturnsMinusOne_BothModes()
		{
			// No producers at all ⇒ -1 in either mode (matches the old foreach-over-empty no-op).
			Assert.That(AdaptiveRoutingMath.SelectProducerIndex(new bool[0], false), Is.EqualTo(-1));
			Assert.That(AdaptiveRoutingMath.SelectProducerIndex(new bool[0], true), Is.EqualTo(-1));
			Assert.That(AdaptiveRoutingMath.SelectProducerIndex(null, false), Is.EqualTo(-1));
			Assert.That(AdaptiveRoutingMath.SelectProducerIndex(null, true), Is.EqualTo(-1));
		}

		// ---- single-element boundary (the smallest non-empty list) ----

		[Test]
		public void SingleElement_FrozenPath_AlwaysIndexZero()
		{
			// A one-producer player on the frozen path always routes to index 0 — even when that lone
			// producer is disabled (byte-identical with legacy unitProducers[0]).
			Assert.That(AdaptiveRoutingMath.SelectProducerIndex(new[] { true }, false), Is.EqualTo(0));
			Assert.That(AdaptiveRoutingMath.SelectProducerIndex(new[] { false }, false), Is.EqualTo(0));
		}

		[Test]
		public void SingleElement_RouteToEnabled_ReflectsThatOneFlag()
		{
			// One enabled producer ⇒ 0; one disabled producer ⇒ -1 (nothing to route to).
			Assert.That(AdaptiveRoutingMath.SelectProducerIndex(new[] { true }, true), Is.EqualTo(0));
			Assert.That(AdaptiveRoutingMath.SelectProducerIndex(new[] { false }, true), Is.EqualTo(-1));
		}

		// ---- tie-break + last-slot determinism ----

		[Test]
		public void RouteToEnabled_FirstEnabledWins_LowestIndexTieBreak()
		{
			// Multiple enabled producers ⇒ the LOWEST enabled index is chosen deterministically, never a
			// later one — the single ordered forward walk pins the tie-break.
			Assert.That(AdaptiveRoutingMath.SelectProducerIndex(new[] { false, true, true }, true), Is.EqualTo(1));
			Assert.That(AdaptiveRoutingMath.SelectProducerIndex(new[] { true, true, true }, true), Is.EqualTo(0));
		}

		[Test]
		public void RouteToEnabled_OnlyLastEnabled_FallsAllTheWayThrough()
		{
			// Every twin but the last is disabled ⇒ the walk must reach the final index rather than stop early.
			Assert.That(AdaptiveRoutingMath.SelectProducerIndex(new[] { false, false, true }, true), Is.EqualTo(2));
		}
	}
}
