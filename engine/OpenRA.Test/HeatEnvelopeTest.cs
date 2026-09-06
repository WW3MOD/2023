#region Copyright & License Information
/*
 * WW3MOD heat-haze envelope tests.
 *
 * Two separate claims are pinned here.
 *
 * 1. THE POSTPROCESS PASS IS SKIPPED WHEN NOTHING IS HOT — not merely cheap, skipped. That is the
 *    hard requirement on this feature, and it is the one a reader cannot check by eye, because the
 *    skip happens in WorldRenderer and the decision happens in a trait that cannot be constructed
 *    without a GL context. All of the deciding therefore lives in HeatEventTracker, which has no
 *    graphics dependency, and HeatHazeRenderer's `Enabled` is literally
 *    `Tracker.ActiveDraws.Count > 0`. WorldRenderer.ApplyPostProcessing (WorldRenderer.cs:393-402)
 *    reads:
 *
 *        foreach (var pass in postProcessPasses)
 *        {
 *            if (pass.Type != type || !pass.Enabled)
 *                continue;
 *            Game.Renderer.Flush();
 *            pass.Draw(this);
 *        }
 *
 *    so a false Enabled skips the Flush, the WorldBufferSnapshot and the draw call together. What
 *    the tests below add is that the list really does go empty — including MID-EVENT, while a live
 *    envelope passes through zero, which is the case a "is anything alive?" check would get wrong.
 *
 * 2. THE ENVELOPE VOCABULARY IS THE LIGHT SYSTEM'S, SHARED AS CODE AND NOT AS A CONVENTION.
 *    HeatEventDefinition reuses LightInterpolation and LightEventDefinition.Shape outright, so
 *    `Interpolations: Quadratic` walks bit-identically the same curve in both. CurveIsTheLightSystems
 *    asserts that on the bit pattern rather than with a tolerance; if someone ever forks the curve
 *    code, the two vocabularies silently stop meaning the same thing and this is what catches it.
 *
 * Everything loads through FieldLoader from real MiniYaml, so the parallel-array parsing and the
 * validation failures are exercised on the same path the mod's YAML takes.
 */
#endregion

using System;
using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Distortion;
using OpenRA.Mods.Common.Lighting;

namespace OpenRA.Test
{
	[TestFixture]
	public class HeatEnvelopeTest
	{
		static HeatEventDefinition Load(params string[] lines)
		{
			var text = "Heat:\n\t" + string.Join("\n\t", lines);
			var parent = new MiniYaml("", MiniYaml.FromString(text, nameof(HeatEnvelopeTest)));
			return HeatEventDefinition.LoadFrom(parent, "Heat", true);
		}

		/// <summary>
		/// The envelope shape the nuclear consumer wants: the shimmer lags the flash rather than
		/// tracking it, keeps growing after the fireball has gone out, and dies away slowly.
		/// </summary>
		static HeatEventDefinition NuclearHaze()
		{
			return Load(
				"Times: 0, 6, 30, 120, 320",
				"Strengths: 0.0, 9.0, 7.0, 3.0, 0",
				"Radii: 2c0, 6c0, 14c0, 22c0, 30c0",
				"Interpolations: Quadratic, Linear, InverseCubic, InverseCubic",
				"ShimmerScale: 640",
				"RiseSpeed: 1c512",
				"AnisotropyPercent: 80");
		}

		// ---- (1) the pass is SKIPPED, not merely cheap ------------------------------------------

		[Test]
		public void AnIdleTrackerDrawsNothingSoThePassIsDisabled()
		{
			var tracker = new HeatEventTracker(16);
			Assert.That(tracker.ActiveDraws.Count, Is.EqualTo(0));
			Assert.That(tracker.LiveEventCount, Is.EqualTo(0));

			// Ticking an empty tracker must stay empty rather than, say, resurrecting a stale draw list.
			for (var i = 0; i < 50; i++)
				tracker.Tick();

			Assert.That(tracker.ActiveDraws.Count, Is.EqualTo(0),
				"an idle tracker produced a draw, so the fullscreen pass would run on a map with nothing hot");
		}

		[Test]
		public void AnEventThatStartsAtZeroStrengthIsLiveButNotDrawn()
		{
			var tracker = new HeatEventTracker(16);
			var handle = tracker.Emit(WPos.Zero, NuclearHaze());

			Assert.That(handle, Is.GreaterThan(0));
			Assert.That(tracker.LiveEventCount, Is.EqualTo(1), "the event should be ticking");
			Assert.That(tracker.ActiveDraws.Count, Is.EqualTo(0),
				"strength is 0 at tick 0, so there is nothing to draw yet and the pass must stay off");
		}

		/// <summary>
		/// The claim that "is anything alive?" would get wrong. An envelope that passes through zero in
		/// the MIDDLE of its life leaves the event live and ticking while contributing no draw, and the
		/// pass has to go off for exactly those ticks.
		/// </summary>
		[Test]
		public void ThePassGoesOffMidEventWhileTheEnvelopePassesThroughZero()
		{
			var pulsing = Load(
				"Times: 0, 5, 10, 15, 20",
				"Strengths: 6.0, 6.0, 0.0, 6.0, 6.0",
				"Radii: 8c0");

			var tracker = new HeatEventTracker(16);
			tracker.Emit(WPos.Zero, pulsing);

			var drawnTicks = new List<int>();
			var liveTicks = 0;
			for (var t = 0; t < 20; t++)
			{
				if (tracker.LiveEventCount > 0)
					liveTicks++;

				if (tracker.ActiveDraws.Count > 0)
					drawnTicks.Add(t);

				tracker.Tick();
			}

			Assert.That(liveTicks, Is.EqualTo(20), "the event should be live for its whole duration");
			Assert.That(drawnTicks, Does.Not.Contain(10),
				"strength is exactly 0 at tick 10 and the pass must be skipped on that frame");
			Assert.That(drawnTicks, Does.Contain(0));
			Assert.That(drawnTicks, Does.Contain(19));

			// The dead band is wider than the single keyframe: everything under MinimumStrength counts.
			var deadTicks = 0;
			for (var t = 0; t < 20; t++)
				if (Math.Abs(pulsing.Evaluate(t).Strength) < HeatEventTracker.MinimumStrength)
					deadTicks++;

			Assert.That(deadTicks, Is.GreaterThan(0));
			Assert.That(20 - drawnTicks.Count, Is.EqualTo(deadTicks),
				"every tick under MinimumStrength must be a skipped frame and no others");
		}

		[Test]
		public void SubPixelShimmerIsNotWorthADrawCall()
		{
			var faint = Load(
				"Times: 0, 10",
				"Strengths: 0.4, 0.4",
				"Radii: 30c0");

			var tracker = new HeatEventTracker(16);
			tracker.Emit(WPos.Zero, faint);

			Assert.That(HeatEventTracker.MinimumStrength, Is.EqualTo(0.5f));
			Assert.That(tracker.ActiveDraws.Count, Is.EqualTo(0),
				"0.4px of peak refraction is invisible over a 30-cell disc and must not cost a pass");
		}

		[Test]
		public void AFinishedEventLeavesNothingBehind()
		{
			var tracker = new HeatEventTracker(16);
			tracker.Emit(WPos.Zero, NuclearHaze());

			for (var t = 0; t < 400; t++)
				tracker.Tick();

			Assert.That(tracker.LiveEventCount, Is.EqualTo(0), "the event should have torn itself down at tick 320");
			Assert.That(tracker.ActiveDraws.Count, Is.EqualTo(0));
		}

		[Test]
		public void ALoopingEventNeverEndsButStillObeysItsOwnZeroes()
		{
			var flicker = Load(
				"Times: 0, 4, 8",
				"Strengths: 4.0, 0.0, 4.0",
				"Radii: 5c0",
				"Loop: true");

			var tracker = new HeatEventTracker(16);
			tracker.Emit(WPos.Zero, flicker);

			var drawn = 0;
			for (var t = 0; t < 80; t++)
			{
				if (tracker.ActiveDraws.Count > 0)
					drawn++;

				tracker.Tick();
			}

			Assert.That(tracker.LiveEventCount, Is.EqualTo(1), "a looping event must not end itself");
			Assert.That(drawn, Is.GreaterThan(0));
			Assert.That(drawn, Is.LessThan(80), "a looping envelope that dips to zero must still switch the pass off at the dip");
		}

		[Test]
		public void ThePerEventCapDropsTheOldestRatherThanRefusingTheNewest()
		{
			var tracker = new HeatEventTracker(3);
			var steady = Load("Times: 0, 500", "Strengths: 5.0, 5.0", "Radii: 4c0");

			for (var i = 0; i < 3; i++)
			{
				tracker.Emit(WPos.Zero, steady);
				tracker.Tick();
			}

			Assert.That(tracker.LiveEventCount, Is.EqualTo(3));

			var newest = tracker.Emit(new WPos(4096, 4096, 0), steady);
			Assert.That(tracker.LiveEventCount, Is.EqualTo(3), "the cap must hold");
			Assert.That(tracker.IsLive(newest), Is.True,
				"the newest event is the one the player is looking at; it must survive the cap");
		}

		[Test]
		public void TwoEventsDoNotShimmerInLockstep()
		{
			var tracker = new HeatEventTracker(16);
			var steady = Load("Times: 0, 200", "Strengths: 5.0, 5.0", "Radii: 6c0");

			tracker.Emit(WPos.Zero, steady);
			tracker.Emit(new WPos(20480, 0, 0), steady);

			Assert.That(tracker.ActiveDraws.Count, Is.EqualTo(2));
			Assert.That(tracker.ActiveDraws[0].PhaseOffset, Is.Not.EqualTo(tracker.ActiveDraws[1].PhaseOffset),
				"two fires lit on the same tick would shimmer in step, which reads as one animation on two sprites");
		}

		[Test]
		public void CancelAndMoveBehaveLikeTheLightManagers()
		{
			var tracker = new HeatEventTracker(16);
			var steady = Load("Times: 0, 200", "Strengths: 5.0, 5.0", "Radii: 6c0");

			var handle = tracker.Emit(new WPos(1024, 2048, 0), steady);
			Assert.That(tracker.ActiveDraws[0].Pos, Is.EqualTo(new WPos(1024, 2048, 0)));

			tracker.Move(handle, new WPos(5120, 2048, 0));
			Assert.That(tracker.ActiveDraws[0].Pos, Is.EqualTo(new WPos(5120, 2048, 0)));

			tracker.Cancel(handle);
			Assert.That(tracker.LiveEventCount, Is.EqualTo(0));
			Assert.That(tracker.ActiveDraws.Count, Is.EqualTo(0));
			Assert.That(tracker.IsLive(handle), Is.False);

			// Cancelling twice, and cancelling -1, must both be no-ops: TimedHeatSource.Stop relies on it.
			tracker.Cancel(handle);
			tracker.Cancel(-1);
		}

		[Test]
		public void OffsetIsAppliedOnceAtEmitAndSurvivesAMove()
		{
			var offset = Load(
				"Times: 0, 100",
				"Strengths: 5.0, 5.0",
				"Radii: 6c0",
				"Offset: 0,-1024,0");

			var tracker = new HeatEventTracker(16);
			var handle = tracker.Emit(new WPos(4096, 4096, 0), offset);
			Assert.That(tracker.ActiveDraws[0].Pos, Is.EqualTo(new WPos(4096, 3072, 0)));

			tracker.Move(handle, new WPos(8192, 4096, 0));
			Assert.That(tracker.ActiveDraws[0].Pos, Is.EqualTo(new WPos(8192, 3072, 0)));
		}

		// ---- (2) the vocabulary really is the light system's ------------------------------------

		/// <summary>
		/// Same times, same curve names, same normalised endpoints => the same numbers, to the bit. The
		/// two definitions are separate classes on purpose (a heat event has no tint and a light has no
		/// rise speed), but they must not be separate CURVES, or `Interpolations: Quadratic` starts
		/// meaning two different things in one mod.
		/// </summary>
		[Test]
		public void CurveIsTheLightSystems()
		{
			var times = "Times: 0, 7, 19, 40";
			var curves = "Interpolations: Quadratic, InverseCubic, Smoothstep";

			var heat = Load(times, "Strengths: 0, 10, 4, 0", "Radii: 1c0", curves);
			var lightText = "Light:\n\t" + string.Join("\n\t",
				times, "Intensities: 0, 10, 4, 0", "Radii: 1c0", curves);
			var light = LightEventDefinition.LoadFrom(
				new MiniYaml("", MiniYaml.FromString(lightText, nameof(HeatEnvelopeTest))), "Light", true);

			for (var t = 0; t <= 40; t++)
			{
				var h = heat.Evaluate(t).Strength;
				var l = light.Evaluate(t).Intensity;
				Assert.That(BitConverter.SingleToInt32Bits(h), Is.EqualTo(BitConverter.SingleToInt32Bits(l)),
					$"tick {t}: heat {h} and light {l} walked different curves from identical keyframes");
			}
		}

		[Test]
		public void RadiusInterpolatesAndTheMaximaAreReported()
		{
			var heat = NuclearHaze();

			Assert.That(heat.Duration, Is.EqualTo(320));
			Assert.That(heat.MaximumRadius, Is.EqualTo(WDist.FromCells(30)));
			Assert.That(heat.MaximumStrength, Is.EqualTo(9.0f));

			// Radius must never shrink on this envelope: hot air keeps spreading after it stops shimmering.
			var previous = -1;
			for (var t = 0; t <= heat.Duration; t++)
			{
				var r = heat.Evaluate(t).Radius.Length;
				Assert.That(r, Is.GreaterThanOrEqualTo(previous), $"radius went backwards at tick {t}");
				previous = r;
			}

			// And the shimmer must outlive the strength peak by a long way — that is the whole physical
			// claim of the nuclear envelope, and a copy of the light envelope would fail it.
			Assert.That(heat.Evaluate(6).Strength, Is.EqualTo(heat.MaximumStrength));
			Assert.That(heat.Evaluate(200).Strength, Is.GreaterThan(0f),
				"the haze should still be shimmering long after the flash");
		}

		// ---- validation -------------------------------------------------------------------------

		[TestCase("Times: 0, 5", "Strengths: 1, 2, 3", "Radii: 1c0", TestName = "StrengthsMustMatchTimes")]
		[TestCase("Times: 3, 5", "Strengths: 1, 2", "Radii: 1c0", TestName = "TimesMustStartAtZero")]
		[TestCase("Times: 0, 5, 5", "Strengths: 1, 2, 3", "Radii: 1c0", TestName = "TimesMustStrictlyIncrease")]
		[TestCase("Times: 0, 5", "Strengths: 1, 2", "Radii: 0", TestName = "RadiusMustBePositive")]
		[TestCase("Times: 0, 5", "Strengths: 1, 2", "Radii: 1c0, 2c0, 3c0", TestName = "RadiiMustBeOneOrN")]
		[TestCase("Times: 0, 5", "Strengths: 1, 2", "Radii: 1c0", "ShimmerScale: 0", TestName = "ShimmerScaleMustBePositive")]
		[TestCase("Times: 0, 5", "Strengths: 1, 2", "Radii: 1c0", "AnisotropyPercent: 140", TestName = "AnisotropyIsAPercent")]
		[TestCase("Times: 0, 5", "Strengths: 1, 2", "Radii: 1c0", "Interpolations: Linear, Linear, Linear",
			TestName = "InterpolationsMustBeEmptyOneOrPerSegment")]
		public void MalformedEnvelopesAreRefusedAtLoad(params string[] lines)
		{
			Assert.Throws<YamlException>(() => Load(lines));
		}

		/// <summary>
		/// A zero ShimmerScale is the one that has to be caught at load rather than at draw: the renderer
		/// divides the radius by it to get the shader's Frequency uniform, so it would arrive as a NaN
		/// and render as garbage with nothing anywhere naming the field that caused it.
		/// </summary>
		[Test]
		public void AZeroShimmerScaleCannotReachTheShader()
		{
			var ex = Assert.Throws<YamlException>(() => Load(
				"Times: 0, 5", "Strengths: 1, 2", "Radii: 1c0", "ShimmerScale: 0"));

			Assert.That(ex.Message, Does.Contain("ShimmerScale"));
		}
	}
}
