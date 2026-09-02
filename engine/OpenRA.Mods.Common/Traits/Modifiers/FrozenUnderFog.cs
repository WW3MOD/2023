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
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("This actor will remain visible (but not updated visually) under fog, once discovered.")]
	public class FrozenUnderFogInfo : TraitInfo, Requires<BuildingInfo>, IDefaultVisibilityInfo
	{
		[Desc("Players with these relationships can always see the actor.")]
		public readonly PlayerRelationship AlwaysVisibleRelationships = PlayerRelationship.Ally;

		public override object Create(ActorInitializer init) { return new FrozenUnderFog(init, this); }
	}

	public class FrozenUnderFog : ICreatesFrozenActors, IRenderModifier, IDefaultVisibility,
		ITickRender, ISync, INotifyCreated, INotifyOwnerChanged, INotifyActorDisposing
	{
		[Sync]
		public int VisibilityHash;

		readonly FrozenUnderFogInfo info;
		readonly bool startsRevealed;
		readonly PPos[] footprint;

		PlayerDictionary<FrozenState> frozenStates;
		bool isRendering;
		bool created;

		sealed class FrozenState
		{
			public readonly FrozenActor FrozenActor;
			public bool IsVisible;
			public FrozenState(FrozenActor frozenActor)
			{
				FrozenActor = frozenActor;
			}
		}

		public FrozenUnderFog(ActorInitializer init, FrozenUnderFogInfo info)
		{
			this.info = info;

			var map = init.World.Map;

			// Explore map-placed actors if the "Explore Map" option is enabled
			var shroudInfo = init.World.Map.Rules.Actors[SystemActors.Player].TraitInfo<MapLayersInfo>();
			startsRevealed = init.Contains<SpawnedByMapInit>();
			var buildingInfo = init.Self.Info.TraitInfoOrDefault<BuildingInfo>();
			var footprintCells = buildingInfo?.FrozenUnderFogTiles(init.Self.Location).ToList() ?? new List<CPos>() { init.Self.Location };
			footprint = footprintCells.SelectMany(c => map.ProjectedCellsCovering(c.ToMPos(map))).ToArray();
		}

		void INotifyCreated.Created(Actor self)
		{
			frozenStates = new PlayerDictionary<FrozenState>(self.World, (player, playerIndex) =>
			{
				var frozenActor = new FrozenActor(self, this, footprint, player, startsRevealed);
				player.PlayerActor.Trait<FrozenActorLayer>().Add(frozenActor);
				return new FrozenState(frozenActor) { IsVisible = !frozenActor.Visible };
			});

			// Set the initial visibility state
			// This relies on actor.GetTargetablePositions(), which is also setup up in Created.
			// Since we can't be sure whether our method will run after theirs, defer by a frame.
			self.World.AddFrameEndTask(_ =>
			{
				for (var playerIndex = 0; playerIndex < frozenStates.Count; playerIndex++)
				{
					var state = frozenStates[playerIndex];
					var frozen = state.FrozenActor;
					if ((startsRevealed && self.TraitOrDefault<Cloak>() == null) || state.IsVisible)
						UpdateFrozenActor(frozen, playerIndex, refreshTooltipOwner: true);

					frozen.RefreshHidden();
				}
			});

			created = true;
		}

		// Not defaulted, for the same reason RefreshState's parameter is not: all three call sites
		// are in this file and one of them must pass false.
		void UpdateFrozenActor(FrozenActor frozenActor, int playerIndex, bool refreshTooltipOwner)
		{
			VisibilityHash |= 1 << (playerIndex % 32);
			frozenActor.RefreshState(refreshTooltipOwner);
		}

		void ICreatesFrozenActors.OnVisibilityChanged(FrozenActor frozen)
		{
			// Ignore callbacks during initial setup
			if (!created)
				return;

			// Update state visibility to match the frozen actor to ensure consistency
			var state = frozenStates[frozen.Viewer];
			var isVisible = !frozen.Visible;
			state.IsVisible = isVisible;

			// refreshTooltipOwner: true — isVisible means the viewer can SEE the real actor right
			// now, so anything this records is information they are entitled to.
			if (isVisible)
				UpdateFrozenActor(frozen, frozen.Viewer.World.Players.IndexOf(frozen.Viewer), refreshTooltipOwner: true);

			frozen.RefreshHidden();
		}

		bool IsVisibleInner(Player byPlayer)
		{
			// If fog is disabled visibility is determined by shroud
			if (!byPlayer.MapLayers.FogEnabled)
				return byPlayer.MapLayers.AnyExplored(footprint);

			return frozenStates[byPlayer].IsVisible;
		}

		public bool IsVisible(Actor self, Player byPlayer)
		{
			if (byPlayer == null)
				return true;

			var relationship = self.Owner.RelationshipWith(byPlayer);
			if (info.AlwaysVisibleRelationships.HasRelationship(relationship))
				return true;

			var cloak = self.TraitOrDefault<Cloak>();
			if (cloak != null && cloak.ShouldHide(self, byPlayer))
				return false;

			// PITFALL: this must stay a real answer. It has been short-circuited to an
			// unconditional `return true` twice (fixed by 2d7603bf, reintroduced by
			// 12a9b91b as "QUICK FIX 260503"), and the second time it survived six months
			// because the leak is nearly invisible in the viewport: actors are drawn
			// before the shroud overlay (WorldRenderer.Draw:349 vs :368) and unexplored
			// cells paint at alpha 1.0 (ShroudRenderer.Alpha, index 0), so a leaked sprite
			// is painted over. The minimap masks it the same way.
			//
			// What is NOT masked is the mouse path. MouseTargetVisibility.IsRevealed is
			//     actorIsVisible && (isFrozenUnderFog || positionIsUnfogged || ...)
			// and isFrozenUnderFog is a bare HasTraitInfo check, true for every building.
			// That exemption delegates "has this player earned sight of it" entirely to
			// this method, so short-circuiting here makes both operands constants and
			// every structure on the map right-clickable, tooltip and owner included, on
			// ground nobody has ever scouted.
			// Guarded by tools/autotest/scenarios/test-unscouted-building-hidden.
			return IsVisibleInner(byPlayer);
		}

		void ITickRender.TickRender(WorldRenderer wr, Actor self)
		{
			IRenderable[] renderables = null;
			Rectangle[] bounds = null;
			var mouseBounds = Polygon.Empty;
			for (var playerIndex = 0; playerIndex < frozenStates.Count; playerIndex++)
			{
				var frozen = frozenStates[playerIndex].FrozenActor;
				if (!frozen.NeedRenderables)
					continue;

				if (renderables == null)
				{
					isRendering = true;
					renderables = self.Render(wr).ToArray();
					bounds = self.ScreenBounds(wr).ToArray();
					mouseBounds = self.MouseBounds(wr);

					isRendering = false;
				}

				frozen.NeedRenderables = false;
				frozen.Renderables = renderables;
				frozen.ScreenBounds = bounds;
				frozen.MouseBounds = mouseBounds;
				self.World.ScreenMap.AddOrUpdate(self.World.Players[playerIndex], frozen);
			}
		}

		IEnumerable<IRenderable> IRenderModifier.ModifyRender(Actor self, WorldRenderer wr, IEnumerable<IRenderable> r)
		{
			if (IsVisible(self, self.World.RenderPlayer) || isRendering)
				return r;

			// Cosmetic reveal: render non-visible buildings as semi-transparent ghosts
			var devMode = self.World.LocalPlayer?.PlayerActor.TraitOrDefault<DeveloperMode>();
			if (devMode != null && devMode.CosmeticReveal)
				return ApplyCosmeticRevealAlpha(r);

			return SpriteRenderable.None;
		}

		static IEnumerable<IRenderable> ApplyCosmeticRevealAlpha(IEnumerable<IRenderable> renderables)
		{
			foreach (var renderable in renderables)
			{
				if (renderable is IModifyableRenderable mr)
					yield return mr.WithAlpha(mr.Alpha * 0.5f);
				else
					yield return renderable;
			}
		}

		IEnumerable<Rectangle> IRenderModifier.ModifyScreenBounds(Actor self, WorldRenderer wr, IEnumerable<Rectangle> bounds)
		{
			return bounds;
		}

		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner)
		{
			// Force a state update for the old owner so their ghost stops behaving as if the actor
			// were still theirs -- Owner, TargetTypes and health all move to the captor's reality, so
			// the old owner's units correctly treat the building as hostile from this tick.
			//
			// TooltipOwner is the one field held back. This handler fires for a player who, in the
			// case that matters, cannot see the cell: refreshing it would print the captor's name and
			// colour in the ghost's tooltip and hand a free answer to "which of the other five took
			// it" on any FFA map (river-zeta-ww3 ships 6 mpspawns; seventh-woods, twin-rivers and
			// x-lake ship 4 each with no fixed teams). The ghost keeps naming the last owner this
			// viewer actually observed.
			//
			// That the building changed hands is NOT hidden and cannot be: the old owner's units must
			// keep treating it as an enemy, which is visible in cursors and autotarget. Only the
			// captor's identity is separable, and only here.
			//
			// Consequence worth knowing: a player who WATCHES the capture, then looks away, gets a
			// ghost still labelled with their own name -- RefreshState only runs again when they
			// regain sight (OnVisibilityChanged, :112). That is a fidelity loss for a player who
			// already knows the answer, traded for the leak against the player who does not.
			var oldOwnerIndex = self.World.Players.IndexOf(oldOwner);
			var frozen = frozenStates[oldOwnerIndex].FrozenActor;
			UpdateFrozenActor(frozen, oldOwnerIndex, refreshTooltipOwner: false);
			frozen.RefreshHidden();
		}

		void INotifyActorDisposing.Disposing(Actor self)
		{
			// Invalidate the frozen actor (which exists if this actor was captured from an enemy)
			// for the current owner
			frozenStates[self.Owner].FrozenActor.Invalidate();
		}
	}
}
