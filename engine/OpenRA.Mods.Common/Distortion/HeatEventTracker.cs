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

using System.Collections.Generic;

namespace OpenRA.Mods.Common.Distortion
{
	/// <summary>One live heat event's current state, ready to be drawn.</summary>
	public readonly struct HeatDraw
	{
		public readonly WPos Pos;
		public readonly float Strength;
		public readonly WDist Radius;
		public readonly HeatEventDefinition Definition;

		/// <summary>Per-event constant added to the animation phase so two events never shimmer in lockstep.</summary>
		public readonly float PhaseOffset;

		public HeatDraw(WPos pos, float strength, WDist radius, HeatEventDefinition definition, float phaseOffset)
		{
			Pos = pos;
			Strength = strength;
			Radius = radius;
			Definition = definition;
			PhaseOffset = phaseOffset;
		}
	}

	// WHY THIS IS A SEPARATE, GRAPHICS-FREE CLASS.
	//
	// HeatHazeRenderer cannot be constructed in a unit test: its constructor calls
	// Game.Renderer.CreateShader, which needs a GL context. That would leave the single most important
	// claim about this feature -- "the postprocess pass is SKIPPED, not merely cheap, when nothing is
	// hot" -- resting on someone reading the code and agreeing. Everything that decides whether there is
	// anything to draw lives here instead, where HeatEnvelopeTest can assert it directly:
	// ActiveDraws.Count is what the renderer's Enabled property returns, and WorldRenderer's
	// ApplyPostProcessing skips a disabled pass before it flushes, before it snapshots the world buffer
	// and before it issues a draw call (WorldRenderer.cs:393-402).
	//
	// Note that a LIVE event and a DRAWN event are not the same thing. An envelope that starts and ends
	// at strength 0 is live for its whole duration and draws on none of those ticks; a fireball whose
	// haze has faded to a tenth of a pixel is still ticking and still contributes nothing. That is the
	// distinction MinimumStrength enforces, and it is why the pass really does go idle mid-event rather
	// than only at the ends.

	/// <summary>Owns the live heat events and advances their envelopes. No graphics dependency.</summary>
	public sealed class HeatEventTracker
	{
		/// <summary>
		/// Displacement below which an event is not worth a draw call, in world pixels. Half a pixel of
		/// peak refraction at the CENTRE of the event is invisible everywhere, since the falloff takes it
		/// to zero at the rim.
		/// </summary>
		public const float MinimumStrength = 0.5f;

		sealed class LiveEvent
		{
			public int Handle;
			public HeatEventDefinition Definition;
			public WPos Pos;
			public int Age;
			public float PhaseOffset;
		}

		readonly int maximumConcurrentEvents;

		// A list rather than a dictionary because Tick walks it by index: a warhead detonating during the
		// same world tick can call Emit, and appending to a list mid-walk is safe where mutating a
		// Dictionary is not. Same shape as LightEventManager, for the same reason.
		readonly List<LiveEvent> live = new();
		readonly List<HeatDraw> draws = new();
		int nextHandle = 1;

		public HeatEventTracker(int maximumConcurrentEvents)
		{
			this.maximumConcurrentEvents = maximumConcurrentEvents;
		}

		/// <summary>Events currently worth drawing. Rebuilt each Tick; empty means the pass is skipped.</summary>
		public IReadOnlyList<HeatDraw> ActiveDraws => draws;

		/// <summary>Events being ticked, whether or not any of them is currently visible.</summary>
		public int LiveEventCount => live.Count;

		/// <summary>Starts a heat event at <paramref name="pos"/>. Returns a handle, or -1 if it was not started.</summary>
		public int Emit(WPos pos, HeatEventDefinition definition)
		{
			if (definition == null)
				return -1;

			// Dropping the oldest rather than refusing the newest: in the case this exists for -- a lot of
			// things burning at once -- the newest event is the one the player is looking at.
			if (live.Count >= maximumConcurrentEvents)
			{
				var oldest = live[0];
				for (var i = 1; i < live.Count; i++)
					if (live[i].Age > oldest.Age)
						oldest = live[i];

				live.Remove(oldest);
			}

			var handle = nextHandle++;

			// A cheap deterministic spread over [0, 2pi). Two fires lit on the same tick at different
			// places must not shimmer in step, and the handle is the only per-event number available that
			// is guaranteed to differ. Not RNG: this is renderer-side only, but taking a draw from the
			// world RNG would still make the sim's stream depend on how many things happen to be burning.
			var phaseOffset = handle * 2.39996f % 6.28318f;

			live.Add(new LiveEvent
			{
				Handle = handle,
				Definition = definition,
				Pos = pos + definition.Offset,
				Age = 0,
				PhaseOffset = phaseOffset,
			});

			RebuildDraws();
			return handle;
		}

		/// <summary>Ends an event early. Safe to call with -1 or an already-ended handle.</summary>
		public void Cancel(int handle)
		{
			var e = EventWithHandle(handle);
			if (e == null)
				return;

			live.Remove(e);
			RebuildDraws();
		}

		/// <summary>Moves a live event, for heat carried by something that moves.</summary>
		public void Move(int handle, WPos pos)
		{
			var e = EventWithHandle(handle);
			if (e == null)
				return;

			var origin = pos + e.Definition.Offset;
			if (e.Pos == origin)
				return;

			e.Pos = origin;
			RebuildDraws();
		}

		public bool IsLive(int handle) { return EventWithHandle(handle) != null; }

		/// <summary>Advances every envelope by one tick and rebuilds the draw list.</summary>
		public void Tick()
		{
			if (live.Count == 0)
				return;

			for (var i = live.Count - 1; i >= 0; i--)
			{
				live[i].Age++;
				if (live[i].Definition.HasEnded(live[i].Age))
					live.RemoveAt(i);
			}

			RebuildDraws();
		}

		void RebuildDraws()
		{
			draws.Clear();
			for (var i = 0; i < live.Count; i++)
			{
				var e = live[i];
				var sample = e.Definition.Evaluate(e.Age);
				if (sample.Strength < MinimumStrength || sample.Radius.Length <= 0)
					continue;

				draws.Add(new HeatDraw(e.Pos, sample.Strength, sample.Radius, e.Definition, e.PhaseOffset));
			}
		}

		LiveEvent EventWithHandle(int handle)
		{
			if (handle <= 0)
				return null;

			for (var i = 0; i < live.Count; i++)
				if (live[i].Handle == handle)
					return live[i];

			return null;
		}
	}
}
