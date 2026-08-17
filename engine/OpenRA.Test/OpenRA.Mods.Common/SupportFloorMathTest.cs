#region Copyright & License Information
/*
 * WW3MOD @experimental — support-floor scaling tests.
 *
 * Pins the decision that stops UnitBuilderBotModule's floor pre-empt spending the opening call-ins on support
 * units. The user-visible bug this nets: "all experimental bots start by building two medics", measured on the
 * shipped default start class as cycles 0 and 1 both buying medi on BOTH factions (--composition-plan --start
 * none). The same shape had already been reported once as two supply trucks at t=0, so these cases exist to
 * make the THIRD instance a test failure rather than a third user report.
 *
 * The load-bearing case is ZeroDenominatorMeansNoFloor. Everything else here is boundary and contract work;
 * that one is the fix.
 *
 * Pure integer decisions; no world mounted.
 */
#endregion

using OpenRA.Mods.Common.Traits;
using NUnit.Framework;

namespace OpenRA.Test
{
	[TestFixture]
	public class SupportFloorMathTest
	{
		// ---- The fix ----

		[TestCase(0)]
		[TestCase(1)]
		[TestCase(19)]
		public void ZeroDenominatorMeansNoFloor(int supported)
		{
			// THE BUG, DIRECTLY. At t=0 the census is zero for every type, so a flat floor of 2 is maximally
			// unmet at exactly the moment a medic is worth least — and because the pre-empt outranks the target
			// ceiling and every demand gate, that flat floor does not merely permit an opening medic, it forces
			// one. Scaled to a 20-man denominator the floor is 0 until there is a squad to treat.
			Assert.That(SupportFloorMath.EffectiveFloor(2, 20, supported), Is.EqualTo(0),
				"a support type must have NO floor while the force it supports does not exist");
		}

		[Test]
		public void FloorPhasesInWithTheSupportedForce()
		{
			// 20 here is an illustrative denominator, NOT the shipped medic ratio — that is 10 (ai-america.yaml).
			// The user's phrasing was "around 1 medic per 20 man squad", but the bot's standing infantry force
			// under losses peaks below 20, so a denominator of 20 left the floor permanently at zero.
			Assert.That(SupportFloorMath.EffectiveFloor(2, 20, 20), Is.EqualTo(1), "one squad ⇒ one medic");
			Assert.That(SupportFloorMath.EffectiveFloor(2, 20, 39), Is.EqualTo(1), "still short of the second");
			Assert.That(SupportFloorMath.EffectiveFloor(2, 20, 40), Is.EqualTo(2), "two squads ⇒ two medics");
		}

		[Test]
		public void FlatFloorIsTheCapNotAnAddition()
		{
			// UnitFloors keeps its old meaning as the standing population ceiling on the floor, so a ratio can
			// never balloon the pre-empt past the number the designer already signed off on.
			Assert.That(SupportFloorMath.EffectiveFloor(2, 20, 400), Is.EqualTo(2));
			Assert.That(SupportFloorMath.EffectiveFloor(2, 20, 100000), Is.EqualTo(2));
		}

		// ---- Off-switch contract: an unconfigured type keeps its exact previous answer ----

		[TestCase(0)]
		[TestCase(5)]
		[TestCase(1000)]
		public void NoRatioReproducesTheFlatFloorVerbatim(int supported)
		{
			// perSupported <= 0 is the default for every type on every profile that does not opt in, including
			// @stable (which sets no UnitFloors at all). The pre-feature answer must come back unchanged
			// REGARDLESS of the denominator, since an unconfigured type must not start reading one.
			Assert.That(SupportFloorMath.EffectiveFloor(2, 0, supported), Is.EqualTo(2));
			Assert.That(SupportFloorMath.EffectiveFloor(2, -1, supported), Is.EqualTo(2));
		}

		[Test]
		public void NoFlatFloorMeansNoFloorWhateverTheRatio()
		{
			// An unfloored type stays unfloored: the ratio scales an existing floor, it never creates one.
			Assert.That(SupportFloorMath.EffectiveFloor(0, 20, 400), Is.EqualTo(0));
			Assert.That(SupportFloorMath.EffectiveFloor(-1, 20, 400), Is.EqualTo(0));
		}

		// ---- Boundaries ----

		[Test]
		public void NegativeDenominatorNeverInventsAFloor()
		{
			// A miscounted denominator must fail toward "no floor", never toward an opening buy.
			Assert.That(SupportFloorMath.EffectiveFloor(2, 20, -5), Is.EqualTo(0));
		}

		[Test]
		public void RatioOfOneTracksTheSupportedCountUpToTheCap()
		{
			Assert.That(SupportFloorMath.EffectiveFloor(3, 1, 0), Is.EqualTo(0));
			Assert.That(SupportFloorMath.EffectiveFloor(3, 1, 2), Is.EqualTo(2));
			Assert.That(SupportFloorMath.EffectiveFloor(3, 1, 9), Is.EqualTo(3));
		}

		[Test]
		public void FloorIsMonotoneInTheSupportedCount()
		{
			// The floor must never fall as the army grows — a support type that got un-floored by REINFORCING
			// would make the pre-empt oscillate and buy-then-abandon.
			var previous = 0;
			for (var supported = 0; supported <= 200; supported++)
			{
				var floor = SupportFloorMath.EffectiveFloor(2, 20, supported);
				Assert.That(floor, Is.GreaterThanOrEqualTo(previous), $"floor fell at supported={supported}");
				previous = floor;
			}
		}
	}
}
