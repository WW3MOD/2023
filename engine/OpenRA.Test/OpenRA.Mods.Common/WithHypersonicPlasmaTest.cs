#region Copyright & License Information
/*
 * WW3MOD hypersonic plasma / streak (2026-09-07).
 *
 * Three claims, and only the first two can be tested without launching:
 *
 *	 1. A weapon that has not opted in renders byte-identically to before. The trait's defaults are
 *		the whole of that guarantee, so they are asserted here rather than left to a reading of the
 *		Info class.
 *	 2. A sample lands where the YAML says and fades the way the YAML says, independently of how
 *		fast the missile happens to be going, and -- since 2026-09-08 -- never past the point the
 *		missile stops at. That is the rest of this fixture.
 *	 3. Whether it LOOKS like plasma, which needs a window. See the capture request in the branch
 *		report; no autotest scenario is shipped for it.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits.Render;

namespace OpenRA.Test
{
	[TestFixture]
	public class WithHypersonicPlasmaTest
	{
		// One tick of a Kinzhal at cruise (2000 WDist) and of a Sarmat RV (1600), the two weapons
		// that opt in. Different speeds on purpose: the placement must not depend on them.
		static readonly WVec KinzhalStep = new WVec(2000, 0, 0);
		static readonly WVec SarmatStep = new WVec(0, -1600, 0);

		[Test]
		public void TheDefaultsDrawNothing()
		{
			var info = new WithHypersonicPlasmaInfo();

			Assert.That(info.LeadingSamples, Is.Zero,
				"A weapon that adds this trait without setting a count must render exactly as it did " +
				"before. Default-off is the constraint the whole feature was asked for under.");
			Assert.That(info.TrailingSamples, Is.Zero);
			Assert.That(info.BodyColor.A, Is.Zero,
				"The body overlay is the third way this trait can change a pixel, and it is off by a " +
				"transparent colour rather than by a count.");
		}

		[Test]
		public void ASampleSitsOneSpacingAlongTheVelocity()
		{
			var step = WithHypersonicPlasmaMath.Step(KinzhalStep, new WDist(384));

			Assert.That(step, Is.EqualTo(new WVec(384, 0, 0)),
				"The offset between consecutive copies is the spacing the author wrote, projected " +
				"onto the direction of travel.");
		}

		[Test]
		public void SpacingDoesNotDependOnHowFastTheMissileIs()
		{
			var spacing = new WDist(384);
			var fast = WithHypersonicPlasmaMath.Step(KinzhalStep, spacing).Length;
			var slower = WithHypersonicPlasmaMath.Step(SarmatStep, spacing).Length;

			Assert.That(fast, Is.EqualTo(384));
			Assert.That(slower, Is.EqualTo(384),
				"Two missiles 400 WDist/tick apart in speed must lay their plasma out identically. " +
				"If this drifts, the streak length has become a function of Speed and every weapon " +
				"needs re-tuning whenever a balance pass touches one.");
		}

		[Test]
		public void ASampleFollowsTheSignOfTheVelocity()
		{
			var step = WithHypersonicPlasmaMath.Step(SarmatStep, new WDist(512));

			Assert.That(step, Is.EqualTo(new WVec(0, -512, 0)),
				"Nothing here goes through WAngle, so the counterclockwise convention cannot be got " +
				"wrong — but the sign still has to survive the integer division.");
		}

		[Test]
		public void ADescendingMissileLaysPlasmaDownItsDive()
		{
			// The terminal dive: horizontal and vertical components together. The Z component is what
			// puts the sheath on the missile's real screen path rather than flat on the ground plane.
			var step = WithHypersonicPlasmaMath.Step(new WVec(3000, 0, -4000), new WDist(500));

			Assert.That(step, Is.EqualTo(new WVec(300, 0, -400)));
			Assert.That(step.Length, Is.EqualTo(500));
		}

		[Test]
		public void AStationaryActorGetsNoPlasma()
		{
			Assert.That(WithHypersonicPlasmaMath.Step(WVec.Zero, new WDist(384)), Is.EqualTo(WVec.Zero),
				"Divide-by-zero guard. A missile waiting on its launch rail has no direction to form " +
				"a leading edge along.");
		}

		[Test]
		public void ZeroSpacingGetsNoPlasma()
		{
			Assert.That(WithHypersonicPlasmaMath.Step(KinzhalStep, WDist.Zero), Is.EqualTo(WVec.Zero));
		}

		[Test]
		public void AnAbsurdSpacingDoesNotOverflow()
		{
			// A YAML author writing a spacing in the millions gets a long streak, not a wrapped
			// vector pointing backwards. int arithmetic would have overflowed at 2000 * 1,500,000.
			var step = WithHypersonicPlasmaMath.Step(KinzhalStep, new WDist(1500000));

			Assert.That(step, Is.EqualTo(new WVec(1500000, 0, 0)));
		}

		[Test]
		public void TheFirstSampleRendersAtExactlyTheAuthoredAlpha()
		{
			Assert.That(WithHypersonicPlasmaMath.Alpha(0.5f, 1, 8), Is.EqualTo(0.5f).Within(0.0001f),
				"The alpha in the YAML colour must mean something a reader can check against a frame, " +
				"rather than being silently divided by a sample count they also chose.");
		}

		[Test]
		public void AlphaFallsToTheAuthoredValueOverTheSampleCount()
		{
			Assert.That(WithHypersonicPlasmaMath.Alpha(0.8f, 4, 4), Is.EqualTo(0.2f).Within(0.0001f));
		}

		[Test]
		public void AlphaFallsMonotonically()
		{
			var previous = float.MaxValue;
			for (var i = 1; i <= 8; i++)
			{
				var alpha = WithHypersonicPlasmaMath.Alpha(0.6f, i, 8);
				Assert.That(alpha, Is.LessThan(previous),
					$"Sample {i} is not dimmer than the one before it — the streak would read as a " +
					"row of separate sprites rather than a fading wake.");
				Assert.That(alpha, Is.GreaterThan(0f));
				previous = alpha;
			}
		}

		[Test]
		public void ASingleSampleDoesNotFade()
		{
			Assert.That(WithHypersonicPlasmaMath.Alpha(0.9f, 1, 1), Is.EqualTo(0.9f).Within(0.0001f));
		}

		[Test]
		public void SamplesOutsideTheRangeAreInvisible()
		{
			Assert.That(WithHypersonicPlasmaMath.Alpha(0.9f, 0, 4), Is.Zero);
			Assert.That(WithHypersonicPlasmaMath.Alpha(0.9f, 5, 4), Is.Zero);
			Assert.That(WithHypersonicPlasmaMath.Alpha(0.9f, 1, 0), Is.Zero);
		}

		// ============================================================================================
		// THE IMPACT CLAMP (2026-09-08). The sibling of SubTickMotionSmoothing's, from the same source
		// (IMotionEndpoint) and for the same reason: nothing this feature draws may sit past the point
		// the missile detonates at.
		// ============================================================================================

		[Test]
		public void TheSheathIsGoneAtContact()
		{
			// A missile placed ON its target still renders for the tick the activity spends finishing
			// before the Kill lands. With no clamp the whole bloom was drawn past the crater point --
			// 480 wdist on the Kinzhal, about 11 px at 100% zoom.
			Assert.That(WithHypersonicPlasmaMath.LeadingSamplesWithin(5, new WDist(96), WDist.Zero), Is.Zero,
				"a missile with nothing left to travel must draw no leading sheath at all.");

			// The Sarmat's configuration, which is shorter and denser, and must behave the same way.
			Assert.That(WithHypersonicPlasmaMath.LeadingSamplesWithin(3, new WDist(128), WDist.Zero), Is.Zero);
		}

		[Test]
		public void NoLeadingSampleIsPlacedPastTheImpactPoint()
		{
			// The exhaustive form of the invariant: whatever is left to travel, the LAST surviving
			// sample lands at or before the endpoint. Sample i sits at i * spacing along the velocity.
			foreach (var spacing in new[] { 96, 128, 384, 1000 })
			{
				for (var left = 0; left <= 3000; left += 7)
				{
					var samples = WithHypersonicPlasmaMath.LeadingSamplesWithin(10, new WDist(spacing), new WDist(left));

					Assert.That(samples * spacing, Is.LessThanOrEqualTo(left),
						$"spacing {spacing} with {left} left kept {samples} samples, the last of which " +
						"is drawn past the point the missile stops at.");
					Assert.That(samples, Is.InRange(0, 10));
				}
			}
		}

		[Test]
		public void TheSheathShortensRatherThanVanishing()
		{
			// The clamp must not read as an on/off switch: the bloom shrinks into the nose as the
			// missile closes, so what the viewer sees over the last fraction of a tick is a sheath
			// being crushed rather than one blinking out.
			Assert.That(WithHypersonicPlasmaMath.LeadingSamplesWithin(5, new WDist(96), new WDist(480)), Is.EqualTo(5));
			Assert.That(WithHypersonicPlasmaMath.LeadingSamplesWithin(5, new WDist(96), new WDist(300)), Is.EqualTo(3));
			Assert.That(WithHypersonicPlasmaMath.LeadingSamplesWithin(5, new WDist(96), new WDist(96)), Is.EqualTo(1));
			Assert.That(WithHypersonicPlasmaMath.LeadingSamplesWithin(5, new WDist(96), new WDist(95)), Is.Zero);
		}

		[Test]
		public void ThereIsNoClampWithoutAnEndpoint()
		{
			// An actor that does not publish where it stops draws the full sheath, exactly as it did
			// before this clamp existed. Null is unbounded, NOT zero -- reading it as zero would switch
			// the whole feature off on anything but a ballistic missile.
			Assert.That(WithHypersonicPlasmaMath.LeadingSamplesWithin(5, new WDist(96), null), Is.EqualTo(5));
			Assert.That(WithHypersonicPlasmaMath.LeadingSamplesWithin(3, new WDist(128), null), Is.EqualTo(3));
		}

		[Test]
		public void ADegenerateConfigurationKeepsTheTwoHalvesAgreeing()
		{
			// Step returns WVec.Zero at spacing 0, so there is nowhere to put a sample; reporting a
			// count above zero here would be a count of copies nobody can see, and the division below
			// it would be by zero.
			Assert.That(WithHypersonicPlasmaMath.LeadingSamplesWithin(5, WDist.Zero, new WDist(5000)), Is.Zero);
			Assert.That(WithHypersonicPlasmaMath.LeadingSamplesWithin(5, WDist.Zero, null), Is.Zero);
			Assert.That(WithHypersonicPlasmaMath.LeadingSamplesWithin(0, new WDist(96), null), Is.Zero);
			Assert.That(WithHypersonicPlasmaMath.LeadingSamplesWithin(5, new WDist(96), new WDist(-500)), Is.Zero);
		}

		[Test]
		public void TheTrailingWakeIsNeverClamped()
		{
			// Stated as a test because it is a deliberate asymmetry rather than an oversight: the wake
			// is drawn BEHIND the body, so at the impact point it lies along ground the missile has
			// already crossed. Step is the only arithmetic the trailing half uses, and it takes no
			// remaining distance at all -- if that ever changes, this fails to compile rather than
			// quietly shortening the streak.
			var trailing = WithHypersonicPlasmaMath.Step(SarmatStep, new WDist(320));
			Assert.That(trailing.Length, Is.EqualTo(320).Within(2));
		}
	}
}
