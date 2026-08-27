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
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Primitives;

namespace OpenRA.Traits
{
	public interface ICreatesFrozenActors
	{
		void OnVisibilityChanged(FrozenActor frozen);
	}

	[TraitLocation(SystemActors.Player)]
	[Desc("Required for FrozenUnderFog to work. Attach this to the player actor.")]
	public class FrozenActorLayerInfo : TraitInfo, Requires<MapLayersInfo>
	{
		[Desc("Size of partition bins (cells)")]
		public readonly int BinSize = 10;

		public override object Create(ActorInitializer init) { return new FrozenActorLayer(init.Self, this); }
	}

	public class FrozenActor
	{
		public readonly PPos[] Footprint;
		public readonly WPos CenterPosition;
		public readonly Actor BackingActor; // Renamed from 'Actor' to avoid conflict with property
		readonly ICreatesFrozenActors frozenTrait;
		readonly Player viewer;
		readonly MapLayers shroud;
		readonly List<WPos> targetablePositions = new List<WPos>();

		public Player Viewer => viewer;
		public Player Owner { get; private set; }
		public BitSet<TargetableType> TargetTypes { get; private set; }
		public IEnumerable<WPos> TargetablePositions => targetablePositions;

		public ITooltipInfo TooltipInfo { get; private set; }
		public Player TooltipOwner { get; private set; }
		readonly ITooltip[] tooltips;

		public int HP { get; private set; }
		public DamageState DamageState { get; private set; }
		readonly IHealth health;

		readonly IShouldHideModifier[] shouldHideModifiers;

		// The Visible flag is tied directly to the actor visibility under the fog.
		// If Visible is true, the actor is made invisible (via FrozenUnderFog/IDefaultVisibility)
		// and this FrozenActor is rendered instead.
		// The Hidden flag covers the edge case that occurs when the backing actor was last "seen"
		// but not actually visible because a visibility modifier hid the actor. Setting Visible to
		// true when the actor is hidden under the fog would leak the actors position via the
		// tooltips and AutoTargetability, and keeping Visible as false would cause the actor to be
		// rendered under the fog.
		public bool Visible { get; private set; } = true;
		public bool Hidden { get; private set; } = false;

		public bool Shrouded { get; private set; }
		public bool NeedRenderables { get; set; }
		public bool UpdateVisibilityNextTick { get; set; }
		public IRenderable[] Renderables = NoRenderables;
		public Rectangle[] ScreenBounds = NoBounds;

		public Polygon MouseBounds = Polygon.Empty;

		static readonly IRenderable[] NoRenderables = Array.Empty<IRenderable>();
		static readonly Rectangle[] NoBounds = Array.Empty<Rectangle>();

		int flashTicks;
		TintModifiers flashModifiers;
		float3 flashTint;
		float? flashAlpha;

		public FrozenActor(Actor actor, ICreatesFrozenActors frozenTrait, PPos[] footprint, Player viewer, bool startsRevealed)
		{
			BackingActor = actor; // Updated from 'Actor'
			this.frozenTrait = frozenTrait;
			this.viewer = viewer;
			shroud = viewer.MapLayers;
			NeedRenderables = startsRevealed;

			// Consider all cells inside the map area (ignoring the current map bounds)
			Footprint = footprint
				.Where(m => shroud.Contains(m))
				.ToArray();

			if (Footprint.Length == 0)
				throw new ArgumentException($"This frozen actor has no footprint.\n" +
					$"Actor Name: {BackingActor.Info.Name}\n" +
					$"Actor Location: {BackingActor.Location}\n" +
					$"Input footprint: [{footprint.Select(p => p.ToString()).JoinWith("|")}]\n" +
					$"Input footprint (after shroud.Contains): [{footprint.Select(p => shroud.Contains(p).ToString()).JoinWith("|")}]");

			CenterPosition = BackingActor.CenterPosition; // Updated from 'Actor'

			tooltips = BackingActor.TraitsImplementing<ITooltip>().ToArray(); // Updated from 'Actor'
			health = BackingActor.TraitOrDefault<IHealth>(); // Updated from 'Actor'
			shouldHideModifiers = BackingActor.TraitsImplementing<IShouldHideModifier>().ToArray(); // Updated from 'Actor'

			UpdateVisibility();
		}

		public uint ID => BackingActor.ActorID; // Updated from 'Actor'
		public bool IsValid => Owner != null;
		public ActorInfo Info => BackingActor.Info; // Updated from 'Actor'
		// PITFALL: returns null when BackingActor is dead (commonly seen post-superweapon). Always null-check at call sites — `target.FrozenActor.Actor.Owner` will NRE.
		public Actor Actor => !BackingActor.IsDead ? BackingActor : null; // Updated from 'Actor'

		public void RefreshState()
		{
			Owner = BackingActor.Owner; // Updated from 'Actor'
			TargetTypes = BackingActor.GetEnabledTargetTypes(); // Updated from 'Actor'
			targetablePositions.Clear();
			targetablePositions.AddRange(BackingActor.GetTargetablePositions()); // Updated from 'Actor'

			if (health != null)
			{
				HP = health.HP;
				DamageState = health.DamageState;
			}

			var tooltip = tooltips.FirstEnabledTraitOrDefault();
			if (tooltip != null)
			{
				TooltipInfo = tooltip.TooltipInfo;
				TooltipOwner = tooltip.Owner;
			}
		}

		public void RefreshHidden()
		{
			Hidden = false;
			foreach (var shouldHideModifier in shouldHideModifiers)
			{
				if (shouldHideModifier.ShouldHide(BackingActor, viewer))
				{
					Hidden = true;
					break;
				}
			}
		}

		public void Tick()
		{
			if (flashTicks > 0)
				flashTicks--;

			if (UpdateVisibilityNextTick)
				UpdateVisibility();
		}

		void UpdateVisibility()
		{
			UpdateVisibilityNextTick = false;

			var wasVisible = Visible;
			Shrouded = true;
			Visible = true;

			// PERF: Avoid LINQ.
			foreach (var puv in Footprint)
			{
				var cv = shroud.GetVisibility(puv);
				if (cv > 1)
				{
					Visible = false;
					Shrouded = false;
					break;
				}

				if (Shrouded && cv > 0)
					Shrouded = false;
			}

			// Force the backing trait to update so other actors can't
			// query inconsistent state (both hidden or both visible)
			if (Visible != wasVisible)
				frozenTrait.OnVisibilityChanged(this);

			NeedRenderables |= Visible && !wasVisible;
		}

		public void Invalidate()
		{
			Owner = null;
		}

		public void Flash(Color color, float alpha)
		{
			flashTicks = 5;
			flashModifiers = TintModifiers.ReplaceColor;
			flashTint = new float3(color.R, color.G, color.B) / 255f;
			flashAlpha = alpha;
		}

		public void Flash(float3 tint)
		{
			flashTicks = 5;
			flashModifiers = TintModifiers.None;
			flashTint = tint;
			flashAlpha = null;
		}

		public IEnumerable<IRenderable> Render()
		{
			if (Shrouded)
				return NoRenderables;

			if (flashTicks > 0 && flashTicks % 2 == 0)
			{
				return Renderables.Concat(Renderables.Where(r => !r.IsDecoration && r is IModifyableRenderable)
					.Select(r =>
					{
						var mr = (IModifyableRenderable)r;
						mr = mr.WithTint(flashTint, mr.TintModifiers | flashModifiers);
						if (flashAlpha.HasValue)
							mr = mr.WithAlpha(flashAlpha.Value);

						return mr;
					}));
			}

			return Renderables;
		}

		public bool HasRenderables => !Shrouded && Renderables.Length > 0;

		public override string ToString()
		{
			return $"{Info.Name} {ID}{(IsValid ? "" : " (invalid)")}";
		}
	}

	public class FrozenActorLayer : IRender, ITick, ISync
	{
		[Sync]
		public int VisibilityHash;

		[Sync]
		public int FrozenHash;

		readonly int binSize;
		readonly World world;
		readonly Player owner;
		readonly Dictionary<uint, FrozenActor> frozenActorsById;
		readonly SpatiallyPartitioned<FrozenActor> partitionedFrozenActors;

		// ORPHANS — INERT DOCUMENTATION. Neither of these is read or written anywhere; they are
		// declared, constructed, and nothing else. They are the remains of a complete id-based
		// dirty-tracking design whose last working revision is c4f0739e: Add/Remove populated
		// partitionedFrozenActorIds, and ITick.Tick ran UpdateVisibility for every id in
		// dirtyFrozenActorIds before clearing the set. Restore from there, NOT from the 2023 engine
		// import 7362fbc6, which merely also has it.
		//
		// Lost when c5bb5ece landed release-20250330 with 112 conflicts pending and 71687440
		// resolved them (71687440 is that resolution, not a merge — it has one parent). The
		// resolution kept these two declarations while dropping both the Add/Remove population and
		// the Tick consumer. Note UpdateVisibilityNextTick did not exist at c4f0739e: the flag is
		// the upstream mechanism, so the two were never co-existing alternatives here — the
		// resolution took one scheme's producer and the other's consumer, and neither survived
		// whole.
		//
		// Do NOT restore it for performance. Measured 2026-08-27: both schemes iterate every frozen
		// actor every tick (ITick.Tick loops frozenActorsById unconditionally) and call
		// UpdateVisibility for exactly the changed ones, so the difference is a HashSet<uint> lookup
		// per actor per tick versus the bool read at :161 — the bool is cheaper. Kept only so the
		// next reader finds evidence rather than an absence.
		readonly SpatiallyPartitioned<uint> partitionedFrozenActorIds;
		readonly HashSet<uint> dirtyFrozenActorIds = new HashSet<uint>();

		public FrozenActorLayer(Actor self, FrozenActorLayerInfo info)
		{
			binSize = info.BinSize;
			world = self.World;
			owner = self.Owner;
			frozenActorsById = new Dictionary<uint, FrozenActor>();

			partitionedFrozenActors = new SpatiallyPartitioned<FrozenActor>(
				world.Map.MapSize.X, world.Map.MapSize.Y, binSize);

			partitionedFrozenActorIds = new SpatiallyPartitioned<uint>(
				world.Map.MapSize.X, world.Map.MapSize.Y, binSize);

			// THE ONLY THING THAT EVER MARKS A FROZEN ACTOR FOR RE-EVALUATION. Without it,
			// FrozenActor.UpdateVisibility runs exactly once per actor -- from the constructor
			// (:113) -- because its only other call site (:162) is gated on
			// UpdateVisibilityNextTick. Merge 71687440 resolved a conflict marker here by keeping
			// the id-based line (retained below) and dropping this loop, which set that flag. The
			// flag then had no writer anywhere in the engine for six months, so every frozen
			// actor's Visible was frozen at its construction-time value, FrozenUnderFog's
			// FrozenState.IsVisible was permanently false for every non-ally viewer, and
			// IsVisibleInner could never return true.
			//
			// That is what made 2d7603bf's correct strict-visibility restoration look broken in
			// April, and what 12a9b91b papered over in May with an unconditional `return true` --
			// the leak that let a player right-click structures on never-scouted ground.
			//
			// Uses partitionedFrozenActors, which Add/Remove actually populate. Do not switch this
			// to partitionedFrozenActorIds without also restoring that partition's population.
			//
			// PITFALL: keep this to ONE At() call. At (SpatiallyPartitioned.cs:100) is a yield
			// iterator, so every call allocates a state machine whether or not it yields anything —
			// and this runs per cell per player on every ResolvedVisibility change
			// (MapLayers.cs:262-269), which with VisionLayers = 11 re-fires on each band transition,
			// not just on explored/visible flips. It is the hottest path in the shroud system. An
			// earlier revision of this handler carried a second, vestigial At() call whose result
			// was unioned into dirtyFrozenActorIds purely to keep the orphaned identifiers
			// referenced; it doubled the allocations here and was removed.
			self.Trait<MapLayers>().OnShroudChanged += uv =>
			{
				foreach (var fa in partitionedFrozenActors.At(new int2(uv.U, uv.V)))
					fa.UpdateVisibilityNextTick = true;
			};
		}

		public void Add(FrozenActor fa)
		{
			frozenActorsById.Add(fa.ID, fa);
			world.ScreenMap.AddOrUpdate(owner, fa);
			partitionedFrozenActors.Add(fa, FootprintBounds(fa));
		}

		public void Remove(FrozenActor fa)
		{
			partitionedFrozenActors.Remove(fa);
			world.ScreenMap.Remove(owner, fa);
			frozenActorsById.Remove(fa.ID);
		}

		static Rectangle FootprintBounds(FrozenActor fa)
		{
			var p1 = fa.Footprint[0];
			var minU = p1.U;
			var maxU = p1.U;
			var minV = p1.V;
			var maxV = p1.V;
			foreach (var p in fa.Footprint)
			{
				if (minU > p.U)
					minU = p.U;
				else if (maxU < p.U)
					maxU = p.U;

				if (minV > p.V)
					minV = p.V;
				else if (maxV < p.V)
					maxV = p.V;
			}

			return Rectangle.FromLTRB(minU, minV, maxU + 1, maxV + 1);
		}

		void ITick.Tick(Actor self)
		{
			List<FrozenActor> frozenActorsToRemove = null;
			VisibilityHash = 0;
			FrozenHash = 0;

			foreach (var kvp in frozenActorsById)
			{
				var id = kvp.Key;
				var hash = (int)id;
				FrozenHash += hash;

				var frozenActor = kvp.Value;
				frozenActor.Tick();

				if (frozenActor.Visible && !frozenActor.Hidden)
					VisibilityHash += hash;
				else if (frozenActor.Actor == null)
				{
					frozenActorsToRemove ??= new List<FrozenActor>();
					frozenActorsToRemove.Add(frozenActor);
				}
			}

			if (frozenActorsToRemove != null)
				foreach (var fa in frozenActorsToRemove)
					Remove(fa);
		}

		public virtual IEnumerable<IRenderable> Render(Actor self, WorldRenderer wr)
		{
			return world.ScreenMap.RenderableFrozenActorsInBox(owner, wr.Viewport.TopLeft, wr.Viewport.BottomRight)
				.Where(f => f.Visible)
				.SelectMany(ff => ff.Render());
		}

		public IEnumerable<Rectangle> ScreenBounds(Actor self, WorldRenderer wr)
		{
			// Player-actor render traits don't require screen bounds
			yield break;
		}

		public FrozenActor FromID(uint id)
		{
			if (!frozenActorsById.TryGetValue(id, out var fa))
				return null;

			return fa;
		}

		public IEnumerable<FrozenActor> FrozenActorsInRegion(CellRegion region, bool onlyVisible = true)
		{
			var tl = region.TopLeft;
			var br = region.BottomRight;
			return partitionedFrozenActors.InBox(Rectangle.FromLTRB(tl.X, tl.Y, br.X, br.Y))
				.Where(fa => fa.IsValid && (!onlyVisible || fa.Visible));
		}

		public IEnumerable<FrozenActor> FrozenActorsInCircle(World world, WPos origin, WDist r, bool onlyVisible = true)
		{
			var centerCell = world.Map.CellContaining(origin);
			var cellRange = (r.Length + 1023) / 1024;
			var tl = centerCell - new CVec(cellRange, cellRange);
			var br = centerCell + new CVec(cellRange, cellRange);

			// Target ranges are calculated in 2D, so ignore height differences
			return partitionedFrozenActors.InBox(Rectangle.FromLTRB(tl.X, tl.Y, br.X, br.Y))
				.Where(fa => fa.IsValid &&
					(!onlyVisible || fa.Visible) &&
					(fa.CenterPosition - origin).HorizontalLengthSquared <= r.LengthSquared);
		}
	}
}
