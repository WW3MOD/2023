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
using OpenRA.Graphics;
using OpenRA.Mods.Common.Distortion;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	/// <summary>Vertex/fragment pair for the heat-haze pass. Its own vertex shader, not the shared textured one.</summary>
	public sealed class HeatHazeShaderBindings : ShaderBindings
	{
		public HeatHazeShaderBindings()
			: base("postprocess_heathaze", "postprocess_heathaze") { }

		public override ShaderVertexAttribute[] Attributes { get; } = new[]
		{
			new ShaderVertexAttribute("aVertexPosition", ShaderVertexAttributeType.Float, 2, 0)
		};
	}

	[TraitLocation(SystemActors.World)]
	[Desc("Add to the world actor to run local heat-haze events: a screen-space refraction shimmer over hot air.",
		"Emitters are HeatEventWarhead (any weapon) and TimedHeatSource (any actor); both describe their",
		"shimmer with the same HeatEventDefinition vocabulary, which is LightEventDefinition's with distortion",
		"strength in place of intensity.",
		"IDLE COST: one enum compare and one bool read per frame. WorldRenderer.ApplyPostProcessing skips a",
		"disabled pass BEFORE it flushes, before it snapshots the world framebuffer and before any draw call,",
		"so a map with nothing burning pays no fullscreen work at all - see HeatEnvelopeTest.")]
	public class HeatHazeRendererInfo : TraitInfo
	{
		[Desc("Hard ceiling on how many heat events may be alive at once. The oldest is dropped past this.",
			"Every DRAWN event is one extra draw call and one extra pass over the pixels it covers, so this is",
			"a real budget rather than a formality; a burning battlefield can generate a lot of them.")]
		public readonly int MaximumConcurrentEvents = 16;

		public override object Create(ActorInitializer init) { return new HeatHazeRenderer(this); }
	}

	// HOW A WORLD POSITION REACHES A SCREEN-SPACE PASS, which is the one genuinely non-obvious part.
	//
	// This is NOT a fullscreen pass with a world position passed in as a uniform to be tested against. It
	// is a QUAD pinned to a world coordinate, exactly as ChronoVortexRenderer does it: the vertex buffer
	// holds a unit quad, the vertex shader scales it by Radius and translates it by (Pos - Scroll), where
	// Pos is WorldRenderer.Screen3DPxPosition of the event's WPos and Scroll is Viewport.TopLeft. Both are
	// in WORLD PIXELS. p1/p2 then map world pixels to clip space through the downscaled framebuffer size.
	// Only fragments under the quad run the shader, so a 3-cell shimmer costs 3 cells of fill and not a
	// screen of it.
	//
	// The fragment shader re-samples the already-rendered frame at an offset (texelFetch of
	// WorldBufferSnapshot at gl_FragCoord + delta) -- the same primitive postprocess_textured_vortex.frag
	// uses, and the reason this feature needed no new rendering concept, only new numbers.
	//
	// KNOWN LIMITATION, shared with the vortex: every quad samples the SAME snapshot, taken once at the
	// top of Draw. Two overlapping events therefore do not compose -- the second one drawn overwrites the
	// first in the overlap with a displacement computed from the undistorted frame. Fixing it means a
	// snapshot per event or a two-target ping-pong, and neither is worth it for a phenomenon whose
	// consumers are usually metres apart. It is only visible if two large hazes overlap.

	public sealed class HeatHazeRenderer : IRenderPostProcessPass, ITick, INotifyActorDisposing
	{
		const float TwoPi = 6.28318531f;

		readonly Renderer renderer;
		readonly IShader shader;
		readonly IVertexBuffer<RenderPostProcessPassVertex> buffer;

		public readonly HeatEventTracker Tracker;

		public HeatHazeRenderer(HeatHazeRendererInfo info)
		{
			Tracker = new HeatEventTracker(info.MaximumConcurrentEvents);

			renderer = Game.Renderer;
			shader = renderer.CreateShader(new HeatHazeShaderBindings());

			var vertices = new RenderPostProcessPassVertex[]
			{
				new(-1, -1),
				new(1, -1),
				new(1, 1),
				new(1, 1),
				new(-1, 1),
				new(-1, -1)
			};

			buffer = renderer.CreateVertexBuffer<RenderPostProcessPassVertex>(6);
			buffer.SetData(ref vertices, 6);
		}

		/// <summary>Starts a heat event at <paramref name="pos"/>. Returns a handle, or -1.</summary>
		public int Emit(WPos pos, HeatEventDefinition definition) { return Tracker.Emit(pos, definition); }
		public void Cancel(int handle) { Tracker.Cancel(handle); }
		public void Move(int handle, WPos pos) { Tracker.Move(handle, pos); }
		public bool IsLive(int handle) { return Tracker.IsLive(handle); }

		void ITick.Tick(Actor self)
		{
			Tracker.Tick();
		}

		// AfterActors rather than the vortex's AfterWorld, and the difference is which layers wobble.
		// The render order is: terrain and actors -> AfterActors -> IRenderAboveWorld overlays ->
		// AfterWorld -> shroud -> AfterShroud (WorldRenderer.cs:350-388). Heat haze should distort the
		// world you are looking THROUGH the hot air at -- terrain, actors, effects, the fireball sprite --
		// and must not distort the overlays and shroud drawn on top of it, because those are not behind
		// the air, they are on the screen.
		PostProcessPassType IRenderPostProcessPass.Type => PostProcessPassType.AfterActors;

		bool IRenderPostProcessPass.Enabled => Tracker.ActiveDraws.Count > 0;

		void IRenderPostProcessPass.Draw(WorldRenderer wr)
		{
			var draws = Tracker.ActiveDraws;
			var scroll = wr.Viewport.TopLeft;
			var size = renderer.WorldFrameBufferSize;
			var downscale = renderer.WorldDownscaleFactor;

			shader.SetVec("Scroll", scroll.X, scroll.Y);
			shader.SetVec("p1", 2f / (downscale * size.Width), 2f / (downscale * size.Height));
			shader.SetVec("p2", -1, -1);
			shader.SetTexture("WorldTexture", renderer.WorldBufferSnapshot());
			shader.PrepareRender();

			// Wall-clock, NOT the simulation tick. The envelope (strength, radius) is sim-paced at
			// 16.67 Hz, but the shimmer itself has to be smooth at whatever the display runs at, and it
			// is renderer-side only: no gameplay value reads it, so a wall clock introduces no
			// determinism problem. It does mean the shimmer keeps moving while the game is paused, which
			// is right - a paused RTS still animates its water.
			var seconds = Game.RunTime / 1000f;

			// World pixels per WDist. The grid is Rectangular with square tiles in this mod, so a circle
			// in world space is a circle on screen and one scalar radius is enough; an isometric mod would
			// need the quad squashed in y.
			var pxPerWDist = wr.TileSize.Width / (float)wr.TileScale;

			for (var i = 0; i < draws.Count; i++)
			{
				var d = draws[i];
				var definition = d.Definition;
				var screen = wr.Screen3DPxPosition(d.Pos);
				var radiusPx = d.Radius.Length * pxPerWDist;

				// One full sine cycle per ShimmerScale of WORLD distance, so cell size is a property of
				// the air and not of the event: a 124-cell fireball gets ~1500 cells of shimmer and a
				// 3-cell wreck gets ~37, rather than both getting the same count stretched to fit.
				var frequency = TwoPi * d.Radius.Length / definition.ShimmerScale.Length;

				// Phase advances so the pattern climbs at RiseSpeed. Reduced into [0, 2pi) before it is
				// handed over: Game.RunTime is milliseconds since launch, and after an hour the raw
				// product is large enough that a mediump float on a GLES driver quantises the shimmer
				// into visible steps.
				var phase = (TwoPi * definition.RiseSpeed.Length / definition.ShimmerScale.Length * seconds
					+ d.PhaseOffset) % TwoPi;

				shader.SetVec("Pos", screen.X, screen.Y);
				shader.SetVec("Radius", radiusPx);

				// Strength is authored in WORLD pixels, the same unit ShakeScreenWarhead.Intensity uses,
				// so an author who knows what 4 px of camera throw looks like knows what 4 px of
				// refraction looks like. gl_FragCoord is in FRAMEBUFFER pixels, and framebuffer pixels
				// are world pixels divided by the downscale factor (Renderer.cs:256-264), so this is the
				// only conversion needed. It is also the one postprocess_textured_vortex.frag omits,
				// which is invisible there only because its LUT offsets happen to be small.
				shader.SetVec("Strength", d.Strength / downscale);
				shader.SetVec("Frequency", frequency);
				shader.SetVec("Phase", phase);
				shader.SetVec("Anisotropy", definition.AnisotropyPercent / 100f);

				renderer.DrawBatch(buffer, shader, 0, 6, PrimitiveType.TriangleList);
			}
		}

		void INotifyActorDisposing.Disposing(Actor self)
		{
			buffer.Dispose();
		}
	}
}
