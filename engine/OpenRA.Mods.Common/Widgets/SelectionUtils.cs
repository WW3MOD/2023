#region Copyright & License Information
/*
 * Copyright 2007-2022 The OpenRA Developers (see AUTHORS)
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
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Widgets
{
	public static class SelectionUtils
	{
		public static IEnumerable<Actor> SelectActorsOnScreen(World world, WorldRenderer wr, IEnumerable<string> selectionClasses, IEnumerable<Player> players)
		{
			var actors = world.ScreenMap.ActorsInMouseBox(wr.Viewport.TopLeft, wr.Viewport.BottomRight).Select(a => a.Actor);
			return SelectActorsByOwnerAndSelectionClass(actors, players, selectionClasses);
		}

		public static IEnumerable<Actor> SelectActorsInWorld(World world, IEnumerable<string> selectionClasses, IEnumerable<Player> players)
		{
			return SelectActorsByOwnerAndSelectionClass(world.Actors.Where(a => a.IsInWorld), players, selectionClasses);
		}

		public static IEnumerable<Actor> SelectActorsByOwnerAndSelectionClass(IEnumerable<Actor> actors, IEnumerable<Player> owners, IEnumerable<string> selectionClasses)
		{
			return actors.Where(a =>
			{
				if (!owners.Contains(a.Owner))
					return false;

				var s = a.TraitOrDefault<ISelectable>();

				// selectionClasses == null means that units, that meet all other criteria, get selected
				return s != null && (selectionClasses == null || selectionClasses.Contains(s.Class));
			});
		}

		public static IEnumerable<Actor> SelectHighestPriorityActorAtPoint(World world, int2 a, Modifiers modifiers)
		{
			var controlAll = DeveloperMode.IsControlAllUnitsActive(world);
			var candidates = world.ScreenMap.ActorsAtMouse(a)
				.Where(x => x.Actor.Info.HasTraitInfo<ISelectableInfo>() && (controlAll || x.Actor.Owner.IsAlliedWith(world.RenderPlayer) || !world.FogObscures(x.Actor)));

			Actor selected;
			if (controlAll)
			{
				// In control-all mode, boost enemy unit priority above neutral:
				// Own > Enemy > Allied > Neutral (instead of default Own > Allied > Neutral > Enemy)
				var best = candidates
					.MaxByOrDefault(x => CalculateControlAllPriority(x, a, modifiers, world));
				selected = best.Actor;
			}
			else
				selected = candidates.WithHighestSelectionPriority(a, modifiers);

			if (selected != null)
				yield return selected;
		}

		static long CalculateControlAllPriority(ActorBoundsPair abp, int2 selectionPixel, Modifiers modifiers, World world)
		{
			var info = abp.Actor.Info.TraitInfoOrDefault<ISelectableInfo>();
			var basePriority = info != null ? info.Priority : int.MinValue;

			var viewer = world.LocalPlayer ?? world.RenderPlayer;

			// Priority order: Own (0) > Enemy (-1) > Allied (-2) > Neutral (-3)
			const int PriorityRange = 30;
			if (viewer != null && abp.Actor.Owner != viewer)
			{
				switch (viewer.RelationshipWith(abp.Actor.Owner))
				{
					case PlayerRelationship.Enemy: basePriority -= PriorityRange; break;
					case PlayerRelationship.Ally: basePriority -= 2 * PriorityRange; break;
					case PlayerRelationship.Neutral: basePriority -= 3 * PriorityRange; break;
				}
			}

			if (!abp.Bounds.IsEmpty)
			{
				var br = abp.Bounds.BoundingRect;
				var centerPixel = new int2(
					br.Left + br.Size.Width / 2,
					br.Top + br.Size.Height / 2);
				var pixelDistance = (centerPixel - selectionPixel).Length;
				return basePriority - (long)pixelDistance << 16;
			}

			return basePriority;
		}

		public static IEnumerable<Actor> SelectActorsInBoxWithDeadzone(World world, int2 a, int2 b, Modifiers modifiers)
		{
			// For dragboxes that are too small, shrink the dragbox to a single point (point b)
			if ((a - b).Length <= Game.Settings.Game.SelectionDeadzone)
				a = b;

			if (a == b)
				return SelectHighestPriorityActorAtPoint(world, a, modifiers);

			var controlAll = DeveloperMode.IsControlAllUnitsActive(world);
			var allInBox = world.ScreenMap.ActorsInMouseBox(a, b)
				.Select(x => x.Actor)
				.Where(x => x.Info.HasTraitInfo<ISelectableInfo>() && (controlAll || x.Owner.IsAlliedWith(world.RenderPlayer) || !world.FogObscures(x)));

			if (controlAll)
			{
				// In control-all box select: if any own units are in the box, select only own units
				var viewer = world.LocalPlayer ?? world.RenderPlayer;
				if (viewer != null)
				{
					var ownUnits = allInBox.Where(x => x.Owner == viewer).ToList();
					if (ownUnits.Count > 0)
						return ownUnits.SubsetWithHighestSelectionPriority(modifiers);
				}
			}

			return allInBox.SubsetWithHighestSelectionPriority(modifiers);
		}

		public static Player[] GetPlayersToIncludeInSelection(World world)
		{
			if (DeveloperMode.IsControlAllUnitsActive(world))
				return world.Players;

			// Players to be included in the selection (the viewer or all players in "Disable shroud" / "All players" mode)
			var viewer = world.RenderPlayer ?? world.LocalPlayer;
			var isShroudDisabled = viewer == null || (world.RenderPlayer == null && world.LocalPlayer.Spectating);
			var isEveryone = viewer != null && viewer.NonCombatant && viewer.Spectating;

			return isShroudDisabled || isEveryone ? world.Players : new[] { viewer };
		}
	}
}
