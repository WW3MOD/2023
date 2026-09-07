#region Copyright & License Information
/*
 * WW3MOD ScreenShaker fixture (2026-09-07).
 *
 * ScreenShakeModelTest covers ScreenShakeModel, which is pure maths with no state. The two things
 * asserted here live on the TRAIT instead and cannot be reached from that fixture at all:
 *
 *   * the sub-world-pixel residual carried between ticks by ScreenShaker.Quantize, which is what
 *     stops the camera staircasing -- or vanishing outright -- when the player is zoomed in;
 *   * ShakeEffect.ExpiryTime, which ScreenShaker.AddEffect derives and which has to agree with the
 *     model's own arrival ceiling or an effect is culled before its wave has arrived.
 *
 * Both drive a real ScreenShaker. Its constructor needs no World and no WorldRenderer, so what is
 * measured here is the shipping instance rather than a copy of its arithmetic.
 */
#endregion

using System;
using NUnit.Framework;
using OpenRA.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	sealed class ScreenShakerTest
	{
		const int Cell = 1024;

		static ShakeParams Params(int duration, int intensity, int halfLife = 0, int attack = 2,
			int freqScale = 100)
		{
			return new ShakeParams
			{
				Duration = duration,
				Intensity = intensity,
				Multiplier = new float2(1, 1),
				AttackTicks = attack,
				DecayHalfLife = halfLife,
				ReleaseTicks = 0,
				FrequencyScale = freqScale
			};
		}

		// Two shipped AtomicHighYield stages (weapons-superweapons.yaml). The coda is the hard case
		// for the quantiser: four pixels of throw is one world-pixel at MaxZoom, so most of its
		// waveform sits below the half-pixel that independent rounding needs before it moves at all.
		static ShakeParams Coda() { return Params(450, 4, halfLife: 170, attack: 60, freqScale: 17); }
		static ShakeParams MainShock() { return Params(110, 20, halfLife: 26, attack: 4, freqScale: 48); }

		static ShakeEffect Effect(ShakeParams p)
		{
			return new ShakeEffect
			{
				SpawnTime = 0,
				Position = new WPos(64 * Cell, 64 * Cell, 0),
				Params = p
			};
		}

		// Viewport.MaxZoom is 4 and EffectiveMinZoom is MinZoom * 0.25, so this brackets what a
		// player can actually be looking at. Zoom above 1 is the hazard: Viewport.CenterLocation is
		// an integer in world-px and the renderer multiplies it by Zoom, so at 4 the camera cannot
		// move less than four screen pixels.
		static readonly float[] Zooms = { 0.25f, 0.5f, 1f, 2f, 3f, 4f };

		[TestCase(TestName = "Quantising to whole world-pixels never loses more than half a pixel, at any zoom.")]
		public void QuantisationErrorStaysBoundedAtEveryZoom()
		{
			var info = new ScreenShakerInfo();
			var model = new ScreenShakeModel(info);

			foreach (var (name, p) in new[] { ("HighYield coda", Coda()), ("HighYield main shock", MainShock()) })
			{
				var e = Effect(p);
				foreach (var zoom in Zooms)
				{
					// A fresh shaker per run: the residual is per-instance state, and a leaked one
					// would make this test order-dependent.
					var shaker = new ScreenShaker(info);
					var drift = 0d;
					var worstDrift = 0d;
					var worstTick = 0;

					for (var t = 0; t < p.Duration; t++)
					{
						var wanted = model.Sample(e, t, 0);
						var applied = shaker.Quantize(wanted, zoom);

						// Both terms in WORLD pixels, the unit CenterLocation is held in.
						drift += (wanted.X / zoom) - applied.X;
						if (Math.Abs(drift) > worstDrift)
						{
							worstDrift = Math.Abs(drift);
							worstTick = t;
						}
					}

					// THE INVARIANT, and the whole point of carrying the fraction forward: the
					// running difference between what the model asked for and where the camera was
					// actually put is a bounded residual, never an accumulating debt. Rounding each
					// tick in isolation instead lets that debt run for a whole half-cycle -- the
					// camera sits still while the waveform swings, which IS the staircase.
					Assert.That(worstDrift, Is.LessThanOrEqualTo(0.51),
						$"{name} at zoom {zoom}: the camera fell {worstDrift:F2} world-px behind the " +
						$"waveform by tick {worstTick}. Error feedback bounds this at half a pixel by " +
						"construction, so a larger number means the discarded fraction is being " +
						"dropped somewhere instead of carried into the next tick.");
				}
			}
		}

		[TestCase(TestName = "A small event still moves the camera when the player is zoomed all the way in.")]
		public void SmallShakeSurvivesMaxZoom()
		{
			var info = new ScreenShakerInfo();
			var model = new ScreenShakeModel(info);
			var p = Coda();
			var e = Effect(p);

			const float MaxZoom = 4f;
			var shaker = new ScreenShaker(info);

			var wantedSum = 0d;
			var appliedSum = 0d;
			var movedTicks = 0;

			for (var t = 0; t < p.Duration; t++)
			{
				var wanted = model.Sample(e, t, 0);
				var applied = shaker.Quantize(wanted, MaxZoom);

				wantedSum += Math.Abs(wanted.X / MaxZoom);
				appliedSum += Math.Abs((double)applied.X);
				if (applied.X != 0)
					movedTicks++;
			}

			Assert.That(wantedSum / p.Duration, Is.LessThan(0.5),
				"This case is only meaningful while the event sits BELOW the half-world-pixel that " +
				"independent rounding needs before it moves at all. It no longer does, so it has " +
				"stopped testing the zoom hazard -- pick a smaller Intensity.");

			// The mean displacement the player actually gets must be the mean the model asked for.
			// It arrives as dither rather than as a smooth curve, because a whole world-pixel is the
			// smallest move the viewport can hold -- but the amplitude survives, where rounding each
			// tick in isolation quantises most of an event this size to a flat zero.
			Assert.That(appliedSum / wantedSum, Is.EqualTo(1d).Within(0.2d),
				$"At zoom {MaxZoom} the camera moved {appliedSum / p.Duration:F3} world-px per tick " +
				$"against the {wantedSum / p.Duration:F3} the model asked for. The quantiser is " +
				"scaling the shake, not merely rounding it.");

			Assert.That(movedTicks, Is.GreaterThan(p.Duration / 20),
				$"The camera moved on only {movedTicks} of {p.Duration} ticks at zoom {MaxZoom}. " +
				"A shake nobody can see is not a shake.");
		}

		[TestCase(TestName = "An effect is kept alive for its own arrival ceiling, not the global one.")]
		public void ExpiryFollowsTheEventsOwnPropagationCap()
		{
			var info = new ScreenShakerInfo();
			var model = new ScreenShakeModel(info);
			var shaker = new ScreenShaker(info);

			// AtomicHighYield Warhead@Shake5 as shipped: an air blast at 6.3 t/cell over a 102-cell
			// blast radius, so its front is 645 ticks wide against a global ceiling of 300.
			var slowFront = Params(120, 7, halfLife: 32, freqScale: 60);
			slowFront.PropagationTicksPerCell = 6.3f;
			slowFront.MaxPropagationDelay = 645;

			var groundWave = MainShock();

			shaker.AddEffect(new WPos(0, 0, 0), slowFront);
			shaker.AddEffect(new WPos(0, 0, 0), groundWave);

			Assert.That(shaker.Effects.Count, Is.EqualTo(2));

			var slow = shaker.Effects[0];
			var normal = shaker.Effects[1];

			Assert.That(slow.ExpiryTime - slow.SpawnTime, Is.EqualTo(120 + 645),
				"The slow stage must be kept alive for its OWN ceiling. Culling it at the global 300 " +
				"deletes the effect while its wave is still 345 ticks short of the blast edge.");

			Assert.That(normal.ExpiryTime - normal.SpawnTime, Is.EqualTo(110 + info.MaxPropagationDelay),
				"An event that sets no ceiling of its own must keep the global one exactly. If this " +
				"moved, one superweapon stage has just lengthened every shake in the mod.");

			// The coupling the two numbers exist to satisfy, stated directly: an effect must outlive
			// the arrival of its own wave at every distance it can still be heard from.
			foreach (var p in new[] { slowFront, groundWave })
				foreach (var cells in new[] { 1, 20, 50, 102, 400, 100000 })
					Assert.That(model.ArrivalDelay(p, cells * Cell),
						Is.LessThanOrEqualTo(model.MaxDelay(p)),
						$"At {cells} cells this effect's wave arrives after it has been culled.");
		}

		[TestCase(TestName = "The quantiser keeps no state once the waveform goes silent.")]
		public void ResidualDoesNotLeakBetweenEvents()
		{
			var shaker = new ScreenShaker(new ScreenShakerInfo());

			// A long near-DC displacement is the case that builds the biggest residual.
			for (var t = 0; t < 50; t++)
				shaker.Quantize(new float2(1.6f, -1.6f), 4f);

			// Zero in must mean zero out, and within one tick of it: a residual is at most half a
			// pixel, so it can survive one quantisation and no more. This is what keeps
			// GetTargetOffset's "the camera lands back on the exact pixel it started on" exact.
			shaker.Quantize(float2.Zero, 4f);
			for (var t = 0; t < 20; t++)
				Assert.That(shaker.Quantize(float2.Zero, 4f), Is.EqualTo(int2.Zero),
					"A residual is still driving the camera after the waveform went silent.");
		}
	}
}
