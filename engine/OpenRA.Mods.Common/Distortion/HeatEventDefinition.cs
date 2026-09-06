#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using OpenRA.Mods.Common.Lighting;

namespace OpenRA.Mods.Common.Distortion
{
	/// <summary>One evaluated instant of a <see cref="HeatEventDefinition"/>.</summary>
	public readonly struct HeatSample
	{
		/// <summary>Peak refraction displacement at the centre of the event, in world pixels.</summary>
		public readonly float Strength;

		public readonly WDist Radius;

		public HeatSample(float strength, WDist radius)
		{
			Strength = strength;
			Radius = radius;
		}
	}

	// SHARED VOCABULARY, SEPARATE CLASS, and the split is deliberate rather than lazy.
	//
	// This reuses LightEventDefinition's `LightInterpolation` enum and its `Shape` curve function
	// OUTRIGHT rather than restating them, so `Interpolations: Linear, Quadratic, InverseCubic` means
	// bit-identically the same curve in a heat envelope as in a light one -- same code, not merely the
	// same words. Anyone who has read one envelope can read the other.
	//
	// What is NOT shared is the class. LightEventDefinition carries Tints, Blend, LightTerrain,
	// TerrainRefreshInterval and TerrainRefreshThreshold, none of which mean anything for a screen-space
	// distortion, and a heat envelope needs ShimmerScale, RiseSpeed and AnisotropyPercent, none of which
	// mean anything for a light. Deriving one from the other would put five dead fields on each. The
	// alternative -- extracting a shared `KeyframeEnvelope<T>` base -- is a real option and probably the
	// right one if a third consumer ever appears, but it would mean rewriting a two-day-old shipped file
	// and its test while ten other branches are in flight, to save about forty lines of arithmetic.
	//
	// KNOWN WART: the enum is called LightInterpolation and is used here by a thing that emits no light.
	// It is a curve library named after its first consumer. Renaming it to EnvelopeInterpolation is the
	// correct fix and touches four files; it is left as a follow-up rather than done on this branch for
	// the same merge-traffic reason.

	/// <summary>A time-varying local heat shimmer: keyframes of distortion strength and radius.</summary>
	public class HeatEventDefinition
	{
		[FieldLoader.Require]
		[Desc("Keyframe times in ticks from the start of the event. Must start at 0 and strictly increase.",
			"The last entry is the duration; the event ends when it is reached (unless Loop is set).",
			"Timestep is 60ms in this mod, so 17 ticks is roughly one second.")]
		public readonly int[] Times = Array.Empty<int>();

		[FieldLoader.Require]
		[Desc("Peak refraction displacement at the CENTRE of the event, in world pixels, at each keyframe.",
			"One entry per Times entry. Rough scale at this mod's 24px tiles: 1 is a barely-perceptible",
			"wobble, 3-4 is a jet exhaust or a burning wreck, 10+ is the air over a nuclear fireball.",
			"Falls to zero at the rim on a (1-r^2)^2 curve, so this is the peak and not the average.")]
		public readonly float[] Strengths = Array.Empty<float>();

		[FieldLoader.Require]
		[Desc("Radius at each keyframe, so the heated volume can grow and then dissipate.",
			"Either one entry (constant radius) or one per Times entry. Must be greater than zero.")]
		public readonly WDist[] Radii = Array.Empty<WDist>();

		[Desc("Curve shape walked between each adjacent pair of keyframes. Same vocabulary, and the same",
			"code, as LightEventDefinition.Interpolations.",
			"Either empty (all Linear), one entry (applied to every segment), or exactly one fewer than Times.")]
		public readonly LightInterpolation[] Interpolations = Array.Empty<LightInterpolation>();

		[Desc("World size of one shimmer cell. The convective scale of the hot air, NOT of the event:",
			"holding it fixed means a large fire shimmers with many small cells rather than with the same",
			"few cells stretched to fit, which is what actually distinguishes a big fire from a near one.")]
		public readonly WDist ShimmerScale = new(512);

		[Desc("How fast the shimmer pattern rises, in WDist per second. Hot air convects upward; this is",
			"the speed it does it at. Zero gives a static distortion field, which looks like glass rather",
			"than like air.")]
		public readonly WDist RiseSpeed = new(1024);

		[Desc("0 distorts equally in both screen axes; 100 distorts vertically only. Rising air shears",
			"mostly vertically, so most consumers want this high.")]
		public readonly int AnisotropyPercent = 75;

		[Desc("Restart the envelope from tick 0 when it runs off the end, instead of ending the event.",
			"Use for a burning wreck or a jet exhaust, whose lifetime is owned by something else.")]
		public readonly bool Loop = false;

		[Desc("Offset from the emitting position (impact point, or actor centre).")]
		public readonly WVec Offset = WVec.Zero;

		/// <summary>Tick at which the envelope ends. For a looping event, the length of one cycle.</summary>
		public int Duration => Times.Length == 0 ? 0 : Times[Times.Length - 1];

		/// <summary>Largest radius the envelope ever reaches, for callers that need to size something up front.</summary>
		public WDist MaximumRadius
		{
			get
			{
				var max = 0;
				foreach (var r in Radii)
					if (r.Length > max)
						max = r.Length;

				return new WDist(max);
			}
		}

		/// <summary>Largest strength the envelope ever reaches.</summary>
		public float MaximumStrength
		{
			get
			{
				var max = 0f;
				foreach (var s in Strengths)
					if (s > max)
						max = s;

				return max;
			}
		}

		/// <summary>Reads a heat definition from a named sub-node. Returns null when absent and not required.</summary>
		public static HeatEventDefinition LoadFrom(MiniYaml parent, string key, bool required)
		{
			var node = parent.NodeWithKeyOrDefault(key);
			if (node == null)
			{
				if (required)
					throw new YamlException($"Missing required `{key}:` heat definition.");

				return null;
			}

			var definition = FieldLoader.Load<HeatEventDefinition>(node.Value);
			definition.Validate(key);
			return definition;
		}

		/// <summary>Checks the parallel keyframe arrays agree.</summary>
		public void Validate(string key)
		{
			if (Times.Length == 0)
				throw new YamlException($"`{key}`: Times must list at least one keyframe.");

			if (Times[0] != 0)
				throw new YamlException($"`{key}`: the first Times entry must be 0, not {Times[0]}.");

			for (var i = 1; i < Times.Length; i++)
				if (Times[i] <= Times[i - 1])
					throw new YamlException(
						$"`{key}`: Times must strictly increase; entry {i} ({Times[i]}) is not after {Times[i - 1]}.");

			if (Strengths.Length != Times.Length)
				throw new YamlException($"`{key}`: Strengths has {Strengths.Length} entries but Times has {Times.Length}.");

			if (Radii.Length != 1 && Radii.Length != Times.Length)
				throw new YamlException($"`{key}`: Radii must have 1 or {Times.Length} entries, not {Radii.Length}.");

			foreach (var r in Radii)
				if (r.Length <= 0)
					throw new YamlException($"`{key}`: every Radii entry must be greater than zero.");

			if (Interpolations.Length > 1 && Interpolations.Length != Times.Length - 1)
				throw new YamlException(
					$"`{key}`: Interpolations must be empty, have 1 entry, or have {Times.Length - 1} entries " +
					$"(one per segment), not {Interpolations.Length}.");

			// A zero shimmer scale is a division by zero in the frequency the renderer hands the shader,
			// which surfaces as a NaN uniform and a quad that renders as garbage rather than as an error.
			if (ShimmerScale.Length <= 0)
				throw new YamlException($"`{key}`: ShimmerScale must be greater than zero.");

			if (AnisotropyPercent < 0 || AnisotropyPercent > 100)
				throw new YamlException($"`{key}`: AnisotropyPercent must be between 0 and 100.");

			if (Loop && Duration <= 0)
				throw new YamlException($"`{key}`: a looping heat event needs a non-zero duration.");
		}

		/// <summary>True once the event has run past its last keyframe and should be torn down.</summary>
		public bool HasEnded(int age)
		{
			return !Loop && age >= Duration;
		}

		/// <summary>Strength and radius at <paramref name="age"/> ticks after the event started.</summary>
		public HeatSample Evaluate(int age)
		{
			var duration = Duration;
			var t = age < 0 ? 0 : age;
			if (Loop && duration > 0)
				t %= duration;
			else if (t >= duration)
				return SampleAt(Times.Length - 1);

			var i = 0;
			while (i + 1 < Times.Length && Times[i + 1] <= t)
				i++;

			if (i + 1 >= Times.Length)
				return SampleAt(i);

			var span = Times[i + 1] - Times[i];

			// LightEventDefinition.Shape is the shared curve library; see the note above the class.
			var f = LightEventDefinition.Shape(InterpolationAt(i), (t - Times[i]) * 1f / span);

			var strength = Strengths[i] + (Strengths[i + 1] - Strengths[i]) * f;

			var r0 = RadiusAt(i).Length;
			var radius = new WDist(r0 + (int)((RadiusAt(i + 1).Length - r0) * f));

			return new HeatSample(strength, radius);
		}

		HeatSample SampleAt(int i)
		{
			return new HeatSample(Strengths[i], RadiusAt(i));
		}

		WDist RadiusAt(int i)
		{
			return Radii.Length == 1 ? Radii[0] : Radii[i];
		}

		LightInterpolation InterpolationAt(int i)
		{
			if (Interpolations.Length == 0)
				return LightInterpolation.Linear;

			return Interpolations.Length == 1 ? Interpolations[0] : Interpolations[i];
		}
	}
}
