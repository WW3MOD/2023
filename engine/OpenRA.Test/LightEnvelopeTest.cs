#region Copyright & License Information
/*
 * WW3MOD local-light envelope tests.
 *
 * LightEventDefinition is the vocabulary both light emitters share (LightEventWarhead on any weapon,
 * TimedLightSource on any actor). It is a keyframe list rather than a rise/hold/decay triple, and the whole
 * reason for that choice is the DOUBLE FLASH: a nuclear fireball peaks, collapses, and peaks again larger.
 * No single-peak envelope model can express it, and the task that tunes the nuke needs it. DoubleFlash below
 * is the test that the model actually delivers on that - it counts the peaks rather than asserting the shape
 * the author had in mind.
 *
 * Everything here loads through FieldLoader from real MiniYaml, so the parallel-array parsing and the
 * validation failures are exercised on the same path the mod's YAML takes.
 */
#endregion

using System;
using System.Collections.Generic;
using NUnit.Framework;
using OpenRA.Mods.Common.Lighting;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class LightEnvelopeTest
	{
		static LightEventDefinition Load(params string[] lines)
		{
			var text = "Light:\n\t" + string.Join("\n\t", lines);
			var parent = new MiniYaml("", MiniYaml.FromString(text, nameof(LightEnvelopeTest)));
			return LightEventDefinition.LoadFrom(parent, "Light", true);
		}

		// The envelope the high-yield nuke is expected to want: a hard first flash, a collapse to almost nothing,
		// a second larger and longer flash, then a long cooling tail through yellow, orange and dull red while the
		// lit area keeps expanding. Kept here as the worked example of what the model has to be able to say.
		static LightEventDefinition DoubleFlash()
		{
			return Load(
				"Times: 0, 2, 5, 14, 20, 34, 70, 200",
				"Intensities: 0, 9.0, 5.0, 0.9, 7.5, 3.2, 1.1, 0",
				"Radii: 3c0, 8c0, 12c0, 16c0, 26c0, 40c0, 56c0, 72c0",
				"Tints: FFFFFF, FFFFFF, FFF6D0, FFE39A, FFFFFF, FFC060, E06020, 802808",
				"Interpolations: Quadratic, InverseQuadratic, InverseQuadratic, Quadratic, InverseCubic, InverseCubic, InverseCubic");
		}

		[Test]
		public void DoubleFlashHasExactlyTwoPeaksWithARealTroughBetween()
		{
			var light = DoubleFlash();
			Assert.That(light.Duration, Is.EqualTo(200));

			var samples = new float[light.Duration + 1];
			for (var t = 0; t <= light.Duration; t++)
				samples[t] = light.Evaluate(t).Intensity;

			// A peak is a tick that is at least as bright as both neighbours and materially brighter than the
			// quietest point of the envelope. Counting them is the point: the model is only useful for the nuke
			// if two separate flashes survive interpolation as two separate flashes.
			var peaks = new List<int>();
			for (var t = 1; t < samples.Length - 1; t++)
				if (samples[t] >= samples[t - 1] && samples[t] > samples[t + 1] && samples[t] > 2f)
					peaks.Add(t);

			Assert.That(peaks.Count, Is.EqualTo(2), "expected exactly two flashes, got peaks at " + string.Join(", ", peaks));
			Assert.That(peaks[0], Is.EqualTo(2));
			Assert.That(peaks[1], Is.EqualTo(20));

			var trough = float.MaxValue;
			for (var t = peaks[0]; t <= peaks[1]; t++)
				trough = Math.Min(trough, samples[t]);

			Assert.That(trough, Is.LessThan(0.25f * Math.Min(samples[peaks[0]], samples[peaks[1]])),
				"the gap between the two flashes must actually go dark, or it reads as one long flash");

			// The lit area only ever grows, and the colour cools from white to a dull red.
			Assert.That(light.Evaluate(0).Radius.Length, Is.LessThan(light.Evaluate(200).Radius.Length));
			Assert.That(light.MaximumRadius.Length, Is.EqualTo(72 * 1024));

			var hot = light.Evaluate(2).Tint;
			var cold = light.Evaluate(200).Tint;
			Assert.That(hot.X, Is.EqualTo(1f).Within(0.001f));
			Assert.That(hot.Z, Is.EqualTo(1f).Within(0.001f));
			Assert.That(cold.Z, Is.LessThan(cold.Y), "a cooling fireball loses blue before it loses green");
			Assert.That(cold.Y, Is.LessThan(cold.X), "and loses green before it loses red");
		}

		[Test]
		public void RiseHoldDecayPhaseDurationsAreHonouredToTheTick()
		{
			// The three-phase case the brief asks for, written as four keyframes: a 10-tick rise, a 30-tick
			// plateau, a 60-tick decay, each with its own curve.
			var light = Load(
				"Times: 0, 10, 40, 100",
				"Intensities: 0, 3, 3, 0",
				"Radii: 6c0",
				"Interpolations: Quadratic, Linear, InverseCubic");

			Assert.That(light.Evaluate(0).Intensity, Is.EqualTo(0f));
			Assert.That(light.Evaluate(10).Intensity, Is.EqualTo(3f));
			Assert.That(light.Evaluate(40).Intensity, Is.EqualTo(3f));
			Assert.That(light.Evaluate(100).Intensity, Is.EqualTo(0f));

			for (var t = 1; t <= 10; t++)
				Assert.That(light.Evaluate(t).Intensity, Is.GreaterThan(light.Evaluate(t - 1).Intensity), $"rise stalled at {t}");

			for (var t = 11; t <= 40; t++)
				Assert.That(light.Evaluate(t).Intensity, Is.EqualTo(3f).Within(0.0001f), $"plateau moved at {t}");

			for (var t = 41; t <= 100; t++)
				Assert.That(light.Evaluate(t).Intensity, Is.LessThan(light.Evaluate(t - 1).Intensity), $"decay stalled at {t}");

			// A constant radius is written once rather than repeated per keyframe.
			Assert.That(light.Evaluate(55).Radius, Is.EqualTo(new WDist(6144)));

			Assert.That(light.HasEnded(99), Is.False);
			Assert.That(light.HasEnded(100), Is.True);
		}

		[Test]
		public void StepHoldsTheEarlierValueThenJumps()
		{
			var light = Load(
				"Times: 0, 20",
				"Intensities: 1, 5",
				"Radii: 4c0",
				"Interpolations: Step");

			for (var t = 0; t < 20; t++)
				Assert.That(light.Evaluate(t).Intensity, Is.EqualTo(1f), $"Step drifted at {t}");

			Assert.That(light.Evaluate(20).Intensity, Is.EqualTo(5f));
		}

		[Test]
		public void LoopWrapsAndNeverEnds()
		{
			var light = Load(
				"Times: 0, 6, 12",
				"Intensities: 0.2, 1.4, 0.2",
				"Radii: 5c0",
				"Loop: true");

			Assert.That(light.HasEnded(100000), Is.False);
			for (var cycle = 0; cycle < 4; cycle++)
			{
				Assert.That(light.Evaluate(cycle * 12).Intensity, Is.EqualTo(0.2f).Within(0.0001f));
				Assert.That(light.Evaluate(cycle * 12 + 6).Intensity, Is.EqualTo(1.4f).Within(0.0001f));
			}
		}

		[Test]
		public void EveryInterpolationRunsFromZeroToOneWithoutOvershooting()
		{
			foreach (LightInterpolation shape in Enum.GetValues(typeof(LightInterpolation)))
			{
				Assert.That(LightEventDefinition.Shape(shape, 0f), Is.EqualTo(0f), $"{shape} does not start at 0");

				// Step is the deliberate exception: it holds the earlier keyframe for the whole segment and only
				// arrives at the later one when Evaluate lands exactly on that keyframe.
				if (shape != LightInterpolation.Step)
					Assert.That(LightEventDefinition.Shape(shape, 1f), Is.EqualTo(1f).Within(0.0001f), $"{shape} does not finish at 1");

				var previous = 0f;
				for (var i = 0; i <= 100; i++)
				{
					var value = LightEventDefinition.Shape(shape, i / 100f);
					Assert.That(value, Is.InRange(-0.0001f, 1.0001f), $"{shape} overshoots at t={i / 100f}");
					Assert.That(value, Is.GreaterThanOrEqualTo(previous - 0.0001f), $"{shape} is not monotonic at t={i / 100f}");
					previous = value;
				}
			}
		}

		[Test]
		public void EveryFalloffIsOneAtTheCentreAndZeroAtTheEdgeAndLinearIsBitExact()
		{
			foreach (LightFalloff shape in Enum.GetValues(typeof(LightFalloff)))
			{
				Assert.That(TerrainLighting.ApplyFalloff(shape, 1f), Is.EqualTo(1f).Within(0.0001f), $"{shape} is not full at the centre");
				Assert.That(TerrainLighting.ApplyFalloff(shape, 0f), Is.EqualTo(0f).Within(0.0001f), $"{shape} does not reach zero at the edge");

				var previous = 0f;
				for (var i = 0; i <= 100; i++)
				{
					var value = TerrainLighting.ApplyFalloff(shape, i / 100f);
					Assert.That(value, Is.GreaterThanOrEqualTo(previous - 0.0001f), $"{shape} is not monotonic at f={i / 100f}");
					previous = value;
				}
			}

			// Linear must be the identity, exactly. That is what keeps the vanilla TerrainLightSource path
			// bit-for-bit unchanged now that every source runs through ApplyFalloff.
			for (var i = 0; i <= 1000; i++)
			{
				var f = i / 1000f;
				Assert.That(TerrainLighting.ApplyFalloff(LightFalloff.Linear, f), Is.EqualTo(f));
			}
		}

		[Test]
		public void InverseSquareConcentratesLightNearTheCentre()
		{
			// Half way out from the centre, a linear falloff still gives half brightness. Real light does not.
			Assert.That(TerrainLighting.ApplyFalloff(LightFalloff.Linear, 0.5f), Is.EqualTo(0.5f));
			Assert.That(TerrainLighting.ApplyFalloff(LightFalloff.InverseSquare, 0.5f), Is.LessThan(0.2f));
		}

		[TestCase("Times: 5, 10", "Intensities: 1, 2", "Radii: 4c0", TestName = "TimesMustStartAtZero")]
		[TestCase("Times: 0, 10, 8", "Intensities: 1, 2, 3", "Radii: 4c0", TestName = "TimesMustStrictlyIncrease")]
		[TestCase("Times: 0, 10", "Intensities: 1, 2, 3", "Radii: 4c0", TestName = "IntensitiesMustMatchTimes")]
		[TestCase("Times: 0, 10", "Intensities: 1, 2", "Radii: 4c0, 5c0, 6c0", TestName = "RadiiMustBeOneOrMatchTimes")]
		[TestCase("Times: 0, 10", "Intensities: 1, 2", "Radii: 0", TestName = "RadiusMustBeNonZero")]
		[TestCase("Times: 0, 10", "Intensities: 1, 2", "Radii: 4c0", "Tints: FFFFFF, 000000, FF0000", TestName = "TintsMustBeOneOrMatchTimes")]
		[TestCase("Times: 0, 10, 20", "Intensities: 1, 2, 3", "Radii: 4c0", "Interpolations: Linear, Step, Cubic", TestName = "InterpolationsMustBeEmptyOneOrOnePerSegment")]
		[TestCase("Times: 0, 10", "Intensities: 1, 2", "Radii: 4c0", "TerrainRefreshInterval: 0", TestName = "TerrainRefreshIntervalMustBeAtLeastOne")]
		public void MalformedEnvelopesAreRejectedAtLoadTime(params string[] lines)
		{
			// These must fail while YAML is being read, where the message names the field, and not later from
			// inside the renderer where a mismatched array is an IndexOutOfRange with no YAML in the stack.
			Assert.Throws<YamlException>(() => Load(lines));
		}

		[Test]
		public void ASingleKeyframeIsALegalConstantLamp()
		{
			var light = Load(
				"Times: 0",
				"Intensities: 1.5",
				"Radii: 8c0",
				"Tints: FFD8A0");

			Assert.That(light.Duration, Is.EqualTo(0));
			Assert.That(light.Evaluate(0).Intensity, Is.EqualTo(1.5f));
			Assert.That(light.Evaluate(9999).Intensity, Is.EqualTo(1.5f));

			// It ends immediately, though - a lamp that should persist needs Loop, or an owner that keeps it alive.
			Assert.That(light.HasEnded(0), Is.True);
		}
	}
}
