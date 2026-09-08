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
using System.Collections.Generic;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Mods.Common.Lighting;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[TraitLocation(SystemActors.World)]
	[Desc("Add to the world actor to run time-varying local light events. Requires the TerrainLighting trait.",
		"Emitters are LightEventWarhead (any weapon) and TimedLightSource (any actor); both describe their light",
		"with the same LightEventDefinition vocabulary.")]
	public class LightEventManagerInfo : TraitInfo, IRulesetLoaded, ILobbyCustomRulesIgnore
	{
		[Desc("Hard ceiling on how many light events may be alive at once. The oldest is dropped past this.",
			"Every live event costs one partition entry that every TintAt call in its footprint must walk.")]
		public readonly int MaximumConcurrentEvents = 32;

		public void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			if (!ai.HasTraitInfo<TerrainLightingInfo>())
				throw new YamlException($"{nameof(LightEventManager)} requires the {nameof(TerrainLighting)} trait on the same actor.");
		}

		public override object Create(ActorInitializer init) { return new LightEventManager(init.Self, this); }
	}

	// The two consumers of TerrainLighting behave completely differently, and everything about the split below
	// follows from that:
	//
	//   - SpriteRenderable.Render calls TintAt per sprite per frame (SpriteRenderable.cs:116), so a source's
	//     intensity can change every tick at zero extra cost. UpdateLightSource does exactly that and nothing else.
	//   - TerrainSpriteLayer subscribes to CellChanged and answers by marking whole vertex ROWS dirty for GPU
	//     re-upload (TerrainSpriteLayer.cs:69, :84, :145, :204). Doing that every tick for a large radius is close
	//     to a full-map vertex re-upload at 16.67Hz. RefreshTerrain is therefore called on a cadence and only when
	//     the value has actually moved.
	//
	// SYNC: nothing here is gameplay state. The envelope is evaluated in floating point inside a sim tick, but its
	// only output is written into TerrainLighting, which nothing but the renderer reads (ITerrainLighting reaches
	// the game only through WorldRenderer.TerrainLighting). No field here is or should be [Sync].

	/// <summary>Owns the live light events and advances their envelopes.</summary>
	public sealed class LightEventManager : ITick, IRenderAboveFog
	{
		sealed class LiveEvent
		{
			public int Handle;
			public LightEventDefinition Definition;
			public int Token;
			public WPos Pos;
			public int Age;
			public WDist RefreshedRadius;
			public float RefreshedIntensity;
			public int TicksSinceRefresh;

			// Last envelope sample, kept so the render path reads the same numbers the tick wrote
			// instead of re-evaluating the envelope at a different age mid-frame.
			public WDist Radius;
			public float Intensity;
			public float3 Tint;
		}

		readonly LightEventManagerInfo info;
		readonly TerrainLighting lighting;

		// A list rather than a dictionary because Tick walks it by index: a warhead detonating during the same
		// world tick can call Emit, and appending to a list mid-walk is safe where mutating a Dictionary is not.
		// Bounded by MaximumConcurrentEvents, so the linear handle lookup is over at most a few dozen entries.
		//
		// CORRECTED 2026-09-07. This used to claim "removal only ever happens in the deferred pass at the end of
		// Tick", and that is false as written: Cancel removes immediately, and the eviction path inside Emit calls
		// Cancel. What is actually true is the property the walk needs, which is narrower and worth stating
		// exactly, because a removal landing mid-walk WOULD skip the element after it and freeze that light for a
		// frame:
		//
		//   NOTHING REMOVES FROM `live` WHILE THE INDEX WALK IN Tick IS RUNNING. The walk itself only ever appends
		//   to `ended`; the Cancel calls are made after it returns. The two external entry points that do remove -
		//   Cancel (TimedLightSource) and Emit (LightEventWarhead, via eviction) - are reached from other actors'
		//   ticks, and traits tick sequentially, so neither can be re-entered from inside this one. Nothing the
		//   walk calls out to can get back here either: UpdateLightSource and RefreshTerrain reach TerrainLighting
		//   and, through CellChanged, TerrainSpriteLayer, none of which emits or cancels a light.
		//
		// So the walk is safe TODAY by construction rather than by the deferred pass alone. If a future caller
		// ever emits or cancels from something the renderer or TerrainLighting invokes, this walk must become
		// removal-safe (iterate backwards, or null out and compact) before that lands.
		readonly List<LiveEvent> live = new();
		readonly List<int> ended = new();
		int nextHandle = 1;

		// Resolved lazily: ShroudRenderer is created after this trait on the same actor, and the
		// editor world has no shroud renderer at all.
		IRenderShroud shroudRenderer;
		bool shroudRendererResolved;

		public LightEventManager(Actor self, LightEventManagerInfo info)
		{
			this.info = info;
			lighting = self.Trait<TerrainLighting>();
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

		/// <summary>Starts a light event at <paramref name="pos"/>. Returns a handle, or -1 if it was not started.</summary>
		public int Emit(WPos pos, LightEventDefinition definition)
		{
			if (definition == null)
				return -1;

			// Dropping the oldest rather than refusing the newest: in the case this exists for - a lot of things
			// exploding at once - the newest event is the one the player is looking at.
			if (live.Count >= info.MaximumConcurrentEvents)
			{
				var oldest = live[0];
				for (var i = 1; i < live.Count; i++)
					if (live[i].Age > oldest.Age)
						oldest = live[i];

				Cancel(oldest.Handle);
			}

			var origin = pos + definition.Offset;
			var sample = definition.Evaluate(0);
			var handle = nextHandle++;

			var token = lighting.AddLightSource(origin, ClampRadius(sample.Radius), sample.Intensity, sample.Tint,
				definition.Falloff, definition.Blend, definition.LightTerrain);

			live.Add(new LiveEvent
			{
				Handle = handle,
				Definition = definition,
				Token = token,
				Pos = origin,
				Age = 0,
				RefreshedRadius = sample.Radius,
				RefreshedIntensity = sample.Intensity,
				TicksSinceRefresh = 0,
				Radius = ClampRadius(sample.Radius),
				Intensity = sample.Intensity,
				Tint = sample.Tint,
			});

			return handle;
		}

		/// <summary>Ends an event early. Safe to call with -1 or an already-ended handle.</summary>
		public void Cancel(int handle)
		{
			var e = EventWithHandle(handle);
			if (e == null)
				return;

			lighting.RemoveLightSource(e.Token, e.Definition.LightTerrain);
			live.Remove(e);
		}

		/// <summary>Moves a live event, for a light carried by something that moves.</summary>
		public void Move(int handle, WPos pos)
		{
			var e = EventWithHandle(handle);
			if (e == null)
				return;

			var origin = pos + e.Definition.Offset;
			if (e.Pos == origin)
				return;

			e.Pos = origin;
			lighting.MoveLightSource(e.Token, origin);
		}

		public bool IsLive(int handle) { return EventWithHandle(handle) != null; }

		public int LiveEventCount => live.Count;

		// PITFALL: SpatiallyPartitioned rejects a zero-width bounding rectangle, so a radius that interpolates to
		// zero would throw from inside the renderer. LightEventDefinition.Validate refuses a zero KEYFRAME radius;
		// this catches the interpolated case as well.
		static WDist ClampRadius(WDist radius)
		{
			return radius.Length < 1 ? new WDist(1) : radius;
		}

		void ITick.Tick(Actor self)
		{
			for (var i = 0; i < live.Count; i++)
			{
				var e = live[i];
				e.Age++;

				if (e.Definition.HasEnded(e.Age))
				{
					ended.Add(e.Handle);
					continue;
				}

				var sample = e.Definition.Evaluate(e.Age);
				var radius = ClampRadius(sample.Radius);
				lighting.UpdateLightSource(e.Token, radius, sample.Intensity, sample.Tint);

				e.Radius = radius;
				e.Intensity = sample.Intensity;
				e.Tint = sample.Tint;

				if (!e.Definition.LightTerrain)
					continue;

				e.TicksSinceRefresh++;
				if (e.TicksSinceRefresh < e.Definition.TerrainRefreshInterval)
					continue;

				var movedEnough =
					Math.Abs(sample.Intensity - e.RefreshedIntensity) >= e.Definition.TerrainRefreshThreshold ||
					Math.Abs(radius.Length - e.RefreshedRadius.Length) >= 1024;

				if (!movedEnough)
					continue;

				lighting.RefreshTerrain(e.Token, e.RefreshedRadius);
				e.RefreshedRadius = radius;
				e.RefreshedIntensity = sample.Intensity;
				e.TicksSinceRefresh = 0;
			}

			if (ended.Count == 0)
				return;

			foreach (var handle in ended)
				Cancel(handle);

			ended.Clear();
		}

		/// <summary>
		/// <para>Draws the fog-piercing half of every light that asked for one. See
		/// <see cref="Graphics.FogPiercingLightRenderable"/> for why a second draw is needed at all:
		/// TerrainLighting tints the world BEFORE the fog quads land on it, so under fog a light is
		/// attenuated rather than hidden, and the only place to put the missing brightness back is
		/// after those quads.</para>
		///
		/// <para>This is a READ of the live events and of the render player's own fog. It writes no
		/// simulation state, and it is not [Sync]-relevant: the value it produces never re-enters the
		/// tick, exactly as the sync note at the top of this file requires of everything here.</para>
		/// </summary>
		IEnumerable<IRenderable> IRenderAboveFog.RenderAboveFog(Actor self, WorldRenderer wr)
		{
			if (live.Count == 0)
				yield break;

			if (!shroudRendererResolved)
			{
				shroudRenderer = self.TraitOrDefault<IRenderShroud>();
				shroudRendererResolved = true;
			}

			// No shroud renderer means no fog was drawn, so nothing was taken away.
			if (shroudRenderer == null)
				yield break;

			var fogDarkness = shroudRenderer.FogDarkness;
			for (var i = 0; i < live.Count; i++)
			{
				var e = live[i];
				if (!e.Definition.GlowAboveFog || e.Intensity == 0f)
					continue;

				yield return new FogPiercingLightRenderable(e.Pos, e.Radius, e.Intensity, e.Tint,
					e.Definition.Falloff, fogDarkness);
			}
		}
	}
}
