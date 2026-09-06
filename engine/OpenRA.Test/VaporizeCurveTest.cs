#region Copyright & License Information
/*
 * WW3MOD vaporisation-curve tests.
 *
 * Vaporizable.Sample is the whole visual, written as a pure function of age so it can be pinned without a
 * world, a renderer or a launch. Everything else in the feature is plumbing around it: the warhead decides
 * who is inside the fireball, ISuppressDeathRemains decides what is left behind, and this decides what the
 * player actually sees between the two.
 *
 * The two boundaries worth defending are the degenerate WhiteoutFractions, because they are not edge cases -
 * they are the two shipping modes. 1.0 is "hide the removal behind the flash": the sprite heats for the whole
 * duration and is simply gone on the last tick, which is what a sub-cell fireball at 20 kt wants because
 * nothing inside it is visible anyway. Anything below 1.0 is the real dissolve, which is what a thirteen-cell
 * fireball at 6 Mt wants because the player can see into it.
 */
#endregion

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class VaporizeCurveTest
	{
		static VaporizeParams Params(int delay = 0, int duration = 12, float brightness = 6f, float whiteout = 0.45f)
		{
			return new VaporizeParams(delay, duration, brightness, whiteout);
		}

		[Test]
		public void NothingHappensBeforeTheDelayElapses()
		{
			var p = Params(delay: 20);
			for (var age = 0; age < 20; age++)
				Assert.That(Vaporizable.Sample(age, p).Unchanged, Is.True, $"drew something at age {age}, before the delay");

			Assert.That(Vaporizable.Sample(20, p).Unchanged, Is.False, "the effect never started");
		}

		[Test]
		public void TheSpriteHeatsToFullBrightnessThenBecomesADissolvingSilhouette()
		{
			var p = Params(duration: 20, brightness: 6f, whiteout: 0.5f);

			// Heating: brightness climbs from 1 and the sprite is still itself.
			Assert.That(Vaporizable.Sample(0, p).Brightness, Is.EqualTo(1f).Within(0.0001f));
			Assert.That(Vaporizable.Sample(0, p).Silhouette, Is.False);

			var previous = 0f;
			for (var age = 0; age < 10; age++)
			{
				var f = Vaporizable.Sample(age, p);
				Assert.That(f.Silhouette, Is.False, $"went to silhouette early at age {age}");
				Assert.That(f.Brightness, Is.GreaterThanOrEqualTo(previous), $"brightness dipped at age {age}");
				Assert.That(f.Alpha, Is.EqualTo(1f), $"faded while still heating at age {age}");
				previous = f.Brightness;
			}

			// Dissolving: alpha falls monotonically to zero.
			previous = float.MaxValue;
			for (var age = 10; age <= 20; age++)
			{
				var f = Vaporizable.Sample(age, p);
				Assert.That(f.Silhouette, Is.True, $"still solid at age {age}, past the whiteout point");
				Assert.That(f.Alpha, Is.LessThanOrEqualTo(previous), $"alpha rose at age {age}");
				Assert.That(f.Alpha, Is.InRange(0f, 1f));
				previous = f.Alpha;
			}

			Assert.That(Vaporizable.Sample(20, p).Alpha, Is.EqualTo(0f).Within(0.0001f), "did not finish transparent");
		}

		[Test]
		public void HeatingReachesTheRequestedPeakByTheWhiteoutPoint()
		{
			var p = Params(duration: 100, brightness: 9f, whiteout: 0.5f);

			// The tick before the switch should be very near the peak - the quadratic is slow early and steep late.
			Assert.That(Vaporizable.Sample(49, p).Brightness, Is.GreaterThan(0.9f * 9f));
			Assert.That(Vaporizable.Sample(49, p).Brightness, Is.LessThanOrEqualTo(9f));
			Assert.That(Vaporizable.Sample(50, p).Silhouette, Is.True);
		}

		[Test]
		public void WhiteoutFractionOneIsTheHideItBehindTheFlashMode()
		{
			// The 20 kt case: the fireball is under a cell and a half across, so no dissolve inside it could be
			// seen. The sprite simply superheats for the whole duration and the actor is gone on the last tick.
			var p = Params(duration: 10, brightness: 8f, whiteout: 1f);

			for (var age = 0; age < 10; age++)
			{
				var f = Vaporizable.Sample(age, p);
				Assert.That(f.Silhouette, Is.False, $"dissolved at age {age}; this mode must never show a silhouette mid-effect");
				Assert.That(f.Alpha, Is.EqualTo(1f), $"faded at age {age}; this mode must stay fully opaque");
			}

			Assert.That(Vaporizable.Sample(9, p).Brightness, Is.GreaterThan(6f), "did not blow out before vanishing");
		}

		[Test]
		public void WhiteoutFractionZeroDissolvesImmediatelyWithNoHeating()
		{
			var p = Params(duration: 10, brightness: 6f, whiteout: 0f);

			Assert.That(Vaporizable.Sample(0, p).Silhouette, Is.True);
			Assert.That(Vaporizable.Sample(0, p).Alpha, Is.EqualTo(1f).Within(0.0001f));
			Assert.That(Vaporizable.Sample(10, p).Alpha, Is.EqualTo(0f).Within(0.0001f));
		}

		[Test]
		public void TheCurveIsClampedPastTheEndSoALateTickCannotResurrectTheSprite()
		{
			var p = Params(duration: 8);
			for (var age = 8; age < 40; age++)
			{
				var f = Vaporizable.Sample(age, p);
				Assert.That(f.Silhouette, Is.True);
				Assert.That(f.Alpha, Is.EqualTo(0f).Within(0.0001f), $"visible again at age {age}");
			}
		}

		[Test]
		public void MalformedParametersAreClampedRatherThanDividingByZero()
		{
			// Duration 0 would be a divide by zero in the curve; a negative delay would read as a head start.
			var zero = new VaporizeParams(-5, 0, 6f, 4f);
			Assert.That(zero.Delay, Is.EqualTo(0));
			Assert.That(zero.Duration, Is.GreaterThanOrEqualTo(1));
			Assert.That(zero.WhiteoutFraction, Is.EqualTo(1f));

			var negative = new VaporizeParams(0, 10, 6f, -1f);
			Assert.That(negative.WhiteoutFraction, Is.EqualTo(0f));

			Assert.That(Vaporizable.Sample(0, zero).Alpha, Is.InRange(0f, 1f));
			Assert.That(Vaporizable.Sample(50, zero).Alpha, Is.InRange(0f, 1f));
		}
	}
}
