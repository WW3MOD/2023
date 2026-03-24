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
<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
		void OnVisibilityChanged(FrozenActor frozen);
	}

	[TraitLocation(SystemActors.Player)]
	[Desc("Required for FrozenUnderFog to work. Attach this to the player actor.")]
	public class FrozenActorLayerInfo : TraitInfo, Requires<MapLayersInfo>
	{
		[Desc("Size of partition bins (cells)")]
		public readonly int BinSize = 10;
=======
		[FluentReference]
		[Desc("Descriptive label for the fog checkbox in the lobby.")]
		public readonly string FogCheckboxLabel = "checkbox-fog-of-war.label";

		[FluentReference]
		[Desc("Tooltip description for the fog checkbox in the lobby.")]
		public readonly string FogCheckboxDescription = "checkbox-fog-of-war.description";

		[Desc("Default value of the fog checkbox in the lobby.")]
		public readonly bool FogCheckboxEnabled = true;

		[Desc("Prevent the fog enabled state from being changed in the lobby.")]
		public readonly bool FogCheckboxLocked = false;

		[Desc("Whether to display the fog checkbox in the lobby.")]
		public readonly bool FogCheckboxVisible = true;

		[Desc("Display order for the fog checkbox in the lobby.")]
		public readonly int FogCheckboxDisplayOrder = 0;

		[FluentReference]
		[Desc("Descriptive label for the explored map checkbox in the lobby.")]
		public readonly string ExploredMapCheckboxLabel = "checkbox-explored-map.label";

		[FluentReference]
		[Desc("Tooltip description for the explored map checkbox in the lobby.")]
		public readonly string ExploredMapCheckboxDescription = "checkbox-explored-map.description";

		[Desc("Default value of the explore map checkbox in the lobby.")]
		public readonly bool ExploredMapCheckboxEnabled = false;

		[Desc("Prevent the explore map enabled state from being changed in the lobby.")]
		public readonly bool ExploredMapCheckboxLocked = false;

		[Desc("Whether to display the explore map checkbox in the lobby.")]
		public readonly bool ExploredMapCheckboxVisible = true;

		[Desc("Display order for the explore map checkbox in the lobby.")]
		public readonly int ExploredMapCheckboxDisplayOrder = 0;

		IEnumerable<LobbyOption> ILobbyOptions.LobbyOptions(MapPreview map)
		{
			yield return new LobbyBooleanOption(map, "explored", ExploredMapCheckboxLabel, ExploredMapCheckboxDescription,
				ExploredMapCheckboxVisible, ExploredMapCheckboxDisplayOrder, ExploredMapCheckboxEnabled, ExploredMapCheckboxLocked);
			yield return new LobbyBooleanOption(map, "fog", FogCheckboxLabel, FogCheckboxDescription,
				FogCheckboxVisible, FogCheckboxDisplayOrder, FogCheckboxEnabled, FogCheckboxLocked);
		}
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp

		public override object Create(ActorInitializer init) { return new FrozenActorLayer(init.Self, this); }
	}

	public class FrozenActor
	{
<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
		public readonly PPos[] Footprint;
		public readonly WPos CenterPosition;
		public readonly Actor BackingActor; // Renamed from 'Actor' to avoid conflict with property
		readonly ICreatesFrozenActors frozenTrait;
<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
		readonly Player viewer;
		readonly MapLayers shroud;
		readonly List<WPos> targetablePositions = new List<WPos>();
=======
		readonly Shroud shroud;
		readonly List<WPos> targetablePositions = new();
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp

		public Player Viewer { get; }
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
=======
		public enum SourceType : byte { PassiveVisibility, Shroud, Visibility }
		public event Action<PPos> OnShroudChanged;
		public int RevealedCells { get; private set; }

		enum ShroudCellType : byte { Shroud, Fog, Visible }
		sealed class ShroudSource
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp
		{
			BackingActor = actor; // Updated from 'Actor'
			this.frozenTrait = frozenTrait;
<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
			this.viewer = viewer;
			shroud = viewer.MapLayers;
=======
			Viewer = viewer;
			shroud = viewer.Shroud;
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp
			NeedRenderables = startsRevealed;

			// Consider all cells inside the map area (ignoring the current map bounds)
			Footprint = footprint
				.Where(m => shroud.Contains(m))
				.ToArray();

			if (Footprint.Length == 0)
<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
				throw new ArgumentException(("This frozen actor has no footprint.\n" +
					"Actor Name: {0}\n" +
					"Actor Location: {1}\n" +
					"Input footprint: [{2}]\n" +
					"Input footprint (after shroud.Contains): [{3}]")
					.F(BackingActor.Info.Name, // Updated from 'Actor'
					BackingActor.Location.ToString(), // Updated from 'Actor'
					footprint.Select(p => p.ToString()).JoinWith("|"),
					footprint.Select(p => shroud.Contains(p).ToString()).JoinWith("|")));
=======
				throw new ArgumentException("This frozen actor has no footprint.\n" +
					$"Actor Name: {actor.Info.Name}\n" +
					$"Actor Location: {actor.Location}\n" +
					$"Input footprint: [{footprint.Select(p => p.ToString()).JoinWith("|")}]\n" +
					$"Input footprint (after shroud.Contains): [{footprint.Select(p => shroud.Contains(p).ToString()).JoinWith("|")}]");
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp

			CenterPosition = BackingActor.CenterPosition; // Updated from 'Actor'

			tooltips = BackingActor.TraitsImplementing<ITooltip>().ToArray(); // Updated from 'Actor'
			health = BackingActor.TraitOrDefault<IHealth>(); // Updated from 'Actor'
			shouldHideModifiers = BackingActor.TraitsImplementing<IShouldHideModifier>().ToArray(); // Updated from 'Actor'

			UpdateVisibility();
		}

<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
		public uint ID => BackingActor.ActorID; // Updated from 'Actor'
		public bool IsValid => Owner != null;
<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
		public ActorInfo Info => BackingActor.Info; // Updated from 'Actor'
		public Actor Actor => !BackingActor.IsDead ? BackingActor : null; // Updated from 'Actor'
		public Player Viewer => viewer;
=======
		public ActorInfo Info => actor.Info;
		public Actor Actor => !actor.IsDead ? actor : null;
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp

		public void RefreshState()
=======
		// Visible is not a super set of Explored. IsExplored may return false even if IsVisible returns true.
		[Flags]
		public enum CellVisibility : byte { Hidden = 0x0, Explored = 0x1, Visible = 0x2 }

		readonly ShroudInfo info;
		readonly Map map;

		// Individual shroud modifier sources (type and area)
		readonly Dictionary<object, ShroudSource> sources = new();

		// Per-cell count of each source type, used to resolve the final cell type
		readonly ProjectedCellLayer<short> passiveVisibleCount;
		readonly ProjectedCellLayer<short> visibleCount;
		readonly ProjectedCellLayer<short> generatedShroudCount;
		readonly ProjectedCellLayer<bool> explored;
		readonly ProjectedCellLayer<bool> touched;
		bool anyCellTouched;

		// Per-cell cache of the resolved cell type (shroud/fog/visible)
		readonly ProjectedCellLayer<ShroudCellType> resolvedType;

		bool disabledChanged;
		[Sync]
		bool disabled;
		public bool Disabled
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp
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
<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
				if (shouldHideModifier.ShouldHide(BackingActor, viewer))
=======
				if (!visibilityModifier.IsVisible(actor, Viewer))
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp
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
<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
				var cv = shroud.GetVisibility(puv);
				if (cv > 1)
				{
					Visible = false;
					Shrouded = false;
					break;
=======
				disabledChanged = false;
				return;
			}

			// PERF: Parts of this loop are very hot.
			// We loop over the direct index that represents the PPos in
			// the ProjectedCellLayers, converting to a PPos only if
			// it is needed (which is the uncommon case.)
			if (disabledChanged)
			{
				touched.SetAll(false);
				var maxIndex = touched.MaxIndex;
				for (var index = 0; index < maxIndex; index++)
					UpdateCell(index, self);
			}
			else
			{
				// PERF: Most cells are unchanged, use IndexOf for fast vectorized search.
				var index = touched.IndexOf(true, 0);
				while (index != -1)
				{
					touched[index] = false;
					UpdateCell(index, self);
					index = touched.IndexOf(true, index + 1);
				}
			}

			Hash = Sync.HashPlayer(self.Owner) + self.World.WorldTick;
			disabledChanged = false;
		}

		void UpdateCell(int index, Actor self)
		{
			var type = ShroudCellType.Shroud;

			if (explored[index])
			{
				var count = visibleCount[index];
				if (!shroudGenerationEnabled || count > 0 || generatedShroudCount[index] == 0)
				{
					if (passiveVisibilityEnabled)
						count += passiveVisibleCount[index];

					type = count > 0 ? ShroudCellType.Visible : ShroudCellType.Fog;
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp
				}
			}

<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
				if (Shrouded && cv > 0)
					Shrouded = false;
			}

			// Force the backing trait to update so other actors can't
			// query inconsistent state (both hidden or both visible)
			if (Visible != wasVisible)
				frozenTrait.OnVisibilityChanged(this);

			NeedRenderables |= Visible && !wasVisible;
=======
			// PERF: Most cells are unchanged
			var oldResolvedType = resolvedType[index];
			if (type != oldResolvedType || disabledChanged)
			{
				resolvedType[index] = type;
				var puv = touched.PPosFromIndex(index);
				if (map.Contains(puv))
					OnShroudChanged(puv);

				if (!disabledChanged && (fogEnabled || !ExploreMapEnabled))
				{
					if (type == ShroudCellType.Visible)
						RevealedCells++;
					else if (fogEnabled && oldResolvedType == ShroudCellType.Visible)
						RevealedCells--;
				}

				if (self.Owner.WinState == WinState.Lost)
					RevealedCells = 0;
			}
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp
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
<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
			flashTicks = 5;
			flashModifiers = TintModifiers.None;
			flashTint = tint;
			flashAlpha = null;
=======
			if (!sources.TryAdd(key, new ShroudSource(type, projectedCells)))
				throw new InvalidOperationException("Attempting to add duplicate shroud source");

			foreach (var puv in projectedCells)
			{
				// Force cells outside the visible bounds invisible
				if (!map.Contains(puv))
					continue;

				var index = touched.Index(puv);
				touched[index] = true;
				anyCellTouched = true;
				switch (type)
				{
					case SourceType.PassiveVisibility:
						passiveVisibilityEnabled = true;
						passiveVisibleCount[index]++;
						explored[index] = true;
						break;
					case SourceType.Visibility:
						visibleCount[index]++;
						explored[index] = true;
						break;
					case SourceType.Shroud:
						shroudGenerationEnabled = true;
						generatedShroudCount[index]++;
						break;
				}
			}
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp
		}

		public IEnumerable<IRenderable> Render()
		{
<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
			if (Shrouded)
				return NoRenderables;
=======
			if (!sources.Remove(key, out var state))
				return;
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp

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
<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp

			return Renderables;
=======
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp
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

		public FrozenActorLayer(Actor self, FrozenActorLayerInfo info)
		{
			binSize = info.BinSize;
			world = self.World;
			owner = self.Owner;
			frozenActorsById = new Dictionary<uint, FrozenActor>();

			partitionedFrozenActors = new SpatiallyPartitioned<FrozenActor>(
				world.Map.MapSize.X, world.Map.MapSize.Y, binSize);

<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
			self.Trait<MapLayers>().OnShroudChanged += uv => dirtyFrozenActorIds.UnionWith(partitionedFrozenActorIds.At(new int2(uv.U, uv.V)));
=======
			self.Trait<Shroud>().OnShroudChanged += uv =>
			{
<<<<<<< C:/Users/fredr/AppData/Local/Temp/mo.tmp
				foreach (var fa in partitionedFrozenActors.At(new int2(uv.U, uv.V)))
					fa.UpdateVisibilityNextTick = true;
			};
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp
=======
				var index = touched.Index(puv);
				touched[index] = true;
				explored[index] = visibleCount[index] + passiveVisibleCount[index] > 0;
			}

			anyCellTouched = true;
>>>>>>> C:/Users/fredr/AppData/Local/Temp/mu.tmp
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
