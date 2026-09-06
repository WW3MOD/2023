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
using OpenRA.Primitives;

namespace OpenRA.Mods.Common.Lighting
{
	/// <summary>Shape of the curve walked between two adjacent keyframes of a <see cref="LightEventDefinition"/>.</summary>
	public enum LightInterpolation
	{
		/// <summary>Hold the earlier keyframe's value, then jump at the later keyframe. Use for a hard flash edge.</summary>
		Step,

		/// <summary>Constant rate.</summary>
		Linear,

		/// <summary>t^2 - slow start, fast finish. The natural shape for a rise.</summary>
		Quadratic,

		/// <summary>1-(1-t)^2 - fast start, slow finish. The natural shape for a decay tail.</summary>
		InverseQuadratic,

		/// <summary>t^3 - very slow start, abrupt finish.</summary>
		Cubic,

		/// <summary>1-(1-t)^3 - abrupt start, very long tail. The shape a cooling fireball follows.</summary>
		InverseCubic,

		/// <summary>t^2(3-2t) - eased at both ends. The natural shape for a lamp swelling and settling.</summary>
		Smoothstep,
	}

	/// <summary>Shape of the intensity falloff from the centre of a light to its edge.</summary>
	public enum LightFalloff
	{
		/// <summary>(range-distance)/range. The engine's historical behaviour; reads as a flat disc.</summary>
		Linear,

		/// <summary>Squared linear falloff - tight bright core, wide dim skirt.</summary>
		Quadratic,

		/// <summary>1-(1-f)^2 - wide bright core, abrupt edge.</summary>
		InverseQuadratic,

		/// <summary>Eased at both ends; no visible edge to the disc.</summary>
		Smoothstep,

		/// <summary>Windowed inverse square. The closest of these to how real light actually falls off.</summary>
		InverseSquare,
	}

	/// <summary>How a light source's contribution is combined with the ambient terrain tint.</summary>
	public enum LightBlend
	{
		/// <summary>
		/// The engine's historical behaviour: the source adds to BOTH the scalar intensity and the tint vector,
		/// which are then multiplied together. Two white lights of intensity 1 therefore produce 9x, not 3x.
		/// Preserved as the default of the underlying trait so anything using the vanilla TerrainLightSource
		/// is bit-for-bit unchanged.
		/// </summary>
		Legacy,

		/// <summary>
		/// The source adds falloff * intensity * tint to the final colour and leaves the ambient term alone.
		/// Light adds rather than scaling what is already there, so a lamp is as bright at night as at noon.
		/// This is what a light EVENT wants, and it is the default here.
		/// </summary>
		Additive,
	}

	/// <summary>One evaluated instant of a <see cref="LightEventDefinition"/>.</summary>
	public readonly struct LightSample
	{
		public readonly float Intensity;
		public readonly WDist Radius;
		public readonly float3 Tint;

		public LightSample(float intensity, WDist radius, in float3 tint)
		{
			Intensity = intensity;
			Radius = radius;
			Tint = tint;
		}
	}

	// The keyframe list is deliberately the ONLY envelope model here rather than a rise/hold/decay triple.
	// Rise/hold/decay is the four-keyframe case (0 / peak / end-of-plateau / end), each phase carrying its own
	// duration in ticks and its own curve - but a keyframe list also expresses envelopes that the triple cannot,
	// and the one that matters is a DOUBLE FLASH: peak, trough, second larger peak, cooling tail. A nuclear
	// fireball does exactly that, and no single-peak envelope model can be made to.
	//
	// Load one with LoadFrom from a `Light:` sub-node, so a warhead and an actor trait present exactly the same
	// vocabulary. LightEnvelopeTest.cs is the worked example of both shapes.

	/// <summary>A time-varying local light: keyframes of intensity, radius and colour, with a curve between each pair.</summary>
	public class LightEventDefinition
	{
		[FieldLoader.Require]
		[Desc("Keyframe times in ticks from the start of the event. Must start at 0 and strictly increase.",
			"The last entry is the duration; the light is removed when it is reached (unless Loop is set).",
			"Timestep is 60ms in this mod, so 17 ticks is roughly one second.")]
		public readonly int[] Times = Array.Empty<int>();

		[FieldLoader.Require]
		[Desc("Intensity at each keyframe. One entry per Times entry.",
			"With Blend: Additive, 1.0 adds one unit of full-brightness light at the centre of the light,",
			"i.e. it doubles a sprite that was already fully lit. Values above 1 blow out towards white.",
			"Negative values are legal and darken.")]
		public readonly float[] Intensities = Array.Empty<float>();

		[FieldLoader.Require]
		[Desc("Radius at each keyframe, so a light's lit area can grow or shrink over its life.",
			"Either one entry (constant radius) or one per Times entry. Must be greater than zero.")]
		public readonly WDist[] Radii = Array.Empty<WDist>();

		[Desc("Colour at each keyframe. Either one entry (constant) or one per Times entry.",
			"A cooling fireball is white, then yellow, then orange, then a dull red.")]
		public readonly Color[] Tints = { Color.White };

		[Desc("Curve shape walked between each adjacent pair of keyframes.",
			"Either empty (all Linear), one entry (applied to every segment), or exactly one fewer than Times.")]
		public readonly LightInterpolation[] Interpolations = Array.Empty<LightInterpolation>();

		[Desc("Shape of the falloff from the centre of the light to its edge.")]
		public readonly LightFalloff Falloff = LightFalloff.InverseSquare;

		[Desc("How this light combines with the ambient terrain tint. See LightBlend.")]
		public readonly LightBlend Blend = LightBlend.Additive;

		[Desc("Restart the envelope from tick 0 when it runs off the end, instead of ending the event.",
			"Use for a lamp, a flare or a burning wreck, whose lifetime is owned by something else.")]
		public readonly bool Loop = false;

		[Desc("Offset from the emitting position (impact point, or actor centre).")]
		public readonly WVec Offset = WVec.Zero;

		[Desc("Light the terrain as well as the sprites standing on it.",
			"Sprite lighting is free and updates every tick. Terrain lighting re-uploads whole vertex rows to",
			"the GPU and is throttled by the two fields below; set this false for a light too small or too",
			"brief for the ground wash to be worth the upload.")]
		public readonly bool LightTerrain = true;

		[Desc("Minimum ticks between terrain refreshes. 1 refreshes every tick, which is expensive for a",
			"large radius - see the cost note on TerrainLighting.NotifyCells.")]
		public readonly int TerrainRefreshInterval = 5;

		[Desc("Also require the intensity to have moved by at least this much, or the radius by at least one",
			"cell, since the last terrain refresh. Suppresses uploads across a plateau or a flat tail.")]
		public readonly float TerrainRefreshThreshold = 0.04f;

		float3[] tints;

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

		/// <summary>Reads a light definition from a named sub-node. Returns null when absent and not required.</summary>
		public static LightEventDefinition LoadFrom(MiniYaml parent, string key, bool required)
		{
			var node = parent.NodeWithKeyOrDefault(key);
			if (node == null)
			{
				if (required)
					throw new YamlException($"Missing required `{key}:` light definition.");

				return null;
			}

			var definition = FieldLoader.Load<LightEventDefinition>(node.Value);
			definition.Validate(key);
			return definition;
		}

		/// <summary>Checks the parallel keyframe arrays agree, and precomputes the tint vectors.</summary>
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

			if (Intensities.Length != Times.Length)
				throw new YamlException($"`{key}`: Intensities has {Intensities.Length} entries but Times has {Times.Length}.");

			if (Radii.Length != 1 && Radii.Length != Times.Length)
				throw new YamlException($"`{key}`: Radii must have 1 or {Times.Length} entries, not {Radii.Length}.");

			// PITFALL: a zero radius reaches SpatiallyPartitioned.Add with a zero-width Rectangle, which throws
			// ArgumentException from inside the renderer rather than anywhere near the YAML that caused it.
			foreach (var r in Radii)
				if (r.Length <= 0)
					throw new YamlException($"`{key}`: every Radii entry must be greater than zero.");

			if (Tints.Length != 1 && Tints.Length != Times.Length)
				throw new YamlException($"`{key}`: Tints must have 1 or {Times.Length} entries, not {Tints.Length}.");

			if (Interpolations.Length > 1 && Interpolations.Length != Times.Length - 1)
				throw new YamlException(
					$"`{key}`: Interpolations must be empty, have 1 entry, or have {Times.Length - 1} entries " +
					$"(one per segment), not {Interpolations.Length}.");

			if (Loop && Duration <= 0)
				throw new YamlException($"`{key}`: a looping light needs a non-zero duration.");

			if (TerrainRefreshInterval < 1)
				throw new YamlException($"`{key}`: TerrainRefreshInterval must be at least 1.");

			tints = new float3[Tints.Length];
			for (var i = 0; i < Tints.Length; i++)
				tints[i] = new float3(Tints[i].R / 255f, Tints[i].G / 255f, Tints[i].B / 255f);
		}

		/// <summary>True once the event has run past its last keyframe and should be torn down.</summary>
		public bool HasEnded(int age)
		{
			return !Loop && age >= Duration;
		}

		/// <summary>Intensity, radius and colour at <paramref name="age"/> ticks after the event started.</summary>
		public LightSample Evaluate(int age)
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
			var f = Shape(InterpolationAt(i), (t - Times[i]) * 1f / span);

			var intensity = Intensities[i] + (Intensities[i + 1] - Intensities[i]) * f;

			var r0 = RadiusAt(i).Length;
			var radius = new WDist(r0 + (int)((RadiusAt(i + 1).Length - r0) * f));

			var c0 = TintAt(i);
			var tint = c0 + f * (TintAt(i + 1) - c0);

			return new LightSample(intensity, radius, tint);
		}

		LightSample SampleAt(int i)
		{
			return new LightSample(Intensities[i], RadiusAt(i), TintAt(i));
		}

		WDist RadiusAt(int i)
		{
			return Radii.Length == 1 ? Radii[0] : Radii[i];
		}

		float3 TintAt(int i)
		{
			return tints.Length == 1 ? tints[0] : tints[i];
		}

		LightInterpolation InterpolationAt(int i)
		{
			if (Interpolations.Length == 0)
				return LightInterpolation.Linear;

			return Interpolations.Length == 1 ? Interpolations[0] : Interpolations[i];
		}

		/// <summary>Maps a normalised segment parameter in [0,1] through the requested curve.</summary>
		public static float Shape(LightInterpolation interpolation, float t)
		{
			return interpolation switch
			{
				LightInterpolation.Step => 0f,
				LightInterpolation.Quadratic => t * t,
				LightInterpolation.InverseQuadratic => t * (2f - t),
				LightInterpolation.Cubic => t * t * t,
				LightInterpolation.InverseCubic => 1f - (1f - t) * (1f - t) * (1f - t),
				LightInterpolation.Smoothstep => t * t * (3f - 2f * t),
				_ => t,
			};
		}
	}
}
