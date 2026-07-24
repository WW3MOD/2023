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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Orders;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Renders target lines between order waypoints.")]
	public class DrawLineToTargetInfo : TraitInfo
	{
		[Desc("Delay (in milliseconds) before the target lines disappear.")]
		public readonly int Delay = 2400;

		[Desc("Width (in pixels) of the target lines.")]
		public readonly int LineWidth = 2;

		[Desc("Width (in pixels) of the queued target lines.")]
		public readonly int QueuedLineWidth = 2;

		[Desc("Width (in pixels) of the end node markers.")]
		public readonly int MarkerWidth = 2;

		[Desc("Width (in pixels) of the queued end node markers.")]
		public readonly int QueuedMarkerWidth = 2;

		[Desc("Width (in pixels) of the faint 'lesser' lines drawn to each unit's cohesion slot",
			"(its actual final destination) when a grouped Move/AttackMove spread the unit around an",
			"order point. Kept thinner than LineWidth so it reads as weaker than the primary line.")]
		public readonly int LesserLineWidth = 1;

		[Desc("Alpha (0-255) of the dashed 'lesser' cohesion slot lines. Combined with the thinner",
			"width and the dashed style, these read as distinctly weaker than the solid primary line.")]
		public readonly int LesserLineAlpha = 110;

		public override object Create(ActorInitializer init) { return new DrawLineToTarget(this); }
	}

	public class DrawLineToTarget : IRenderAboveShroud, IRenderAnnotationsWhenSelected, INotifySelected
	{
		readonly DrawLineToTargetInfo info;
		readonly List<IRenderable> renderableCache = new List<IRenderable>();
		long lifetime;

		public DrawLineToTarget(DrawLineToTargetInfo info)
		{
			this.info = info;
		}

		public void ShowTargetLines(Actor self)
		{
			if (Game.Settings.Game.TargetLines < TargetLinesType.Automatic || self.IsIdle)
				return;

			// Reset the order line timeout.
			lifetime = Game.RunTime + info.Delay;
		}

		void INotifySelected.Selected(Actor self)
		{
			ShowTargetLines(self);
		}

		bool ShouldRender(Actor self, WorldRenderer wr)
		{
			if (!self.Owner.IsAlliedWith(self.World.LocalPlayer) || Game.Settings.Game.TargetLines == TargetLinesType.Disabled)
				return false;

			// Show all orders mode (spacebar hold)
			if (wr.ShowAllOrders)
				return true;

			// Players want to see the lines when in waypoint mode.
			var force = Game.GetModifierKeys().HasModifier(Modifiers.Shift) || self.World.OrderGenerator is ForceModifiersOrderGenerator;

			return force || Game.RunTime <= lifetime;
		}

		IEnumerable<IRenderable> IRenderAboveShroud.RenderAboveShroud(Actor self, WorldRenderer wr)
		{
			if (!ShouldRender(self, wr))
				return Enumerable.Empty<IRenderable>();

			return RenderAboveShroud(self, wr);
		}

		static IEnumerable<IRenderable> RenderAboveShroud(Actor self, WorldRenderer wr)
		{
			var pal = wr.Palette(TileSet.TerrainPaletteInternalName);
			var a = self.CurrentActivity;
			for (; a != null; a = a.NextActivity)
				if (!a.IsCanceling)
					foreach (var n in a.TargetLineNodes(self))
						if (n.Tile != null && n.Target.Type != TargetType.Invalid)
							yield return new SpriteRenderable(n.Tile, n.Target.CenterPosition, WVec.Zero, -511, pal, 1f, 1f, float3.Ones, TintModifiers.IgnoreWorldTint, true);
		}

		bool IRenderAboveShroud.SpatiallyPartitionable => false;

		IEnumerable<IRenderable> IRenderAnnotationsWhenSelected.RenderAnnotations(Actor self, WorldRenderer wr)
		{
			if (!ShouldRender(self, wr))
				return Enumerable.Empty<IRenderable>();

			renderableCache.Clear();

			// Cohesion order-line overlay: if a grouped Move/AttackMove spread this unit into a
			// formation slot for its CURRENT move, render the per-unit legs below as faint "lesser"
			// lines and add a normal-weight "primary" line to the commanded order point — so the
			// player sees both where the order was given AND where each unit actually ends up.
			// Purely visual: reads deterministic sim state (CohesionSlotMemory), never mutates it.
			var slotMemory = self.TraitOrDefault<CohesionSlotMemory>();
			var cohesionOrderPoint = CPos.Zero;
			var haveOrderPoint = false;
			var haveHead = false;
			var headColor = default(Color);

			var prev = self.CenterPosition;
			var a = self.CurrentActivity;
			for (; a != null; a = a.NextActivity)
			{
				if (a.IsCanceling)
					continue;

				foreach (var n in a.TargetLineNodes(self))
				{
					if (n.Target.Type != TargetType.Invalid && n.Tile == null)
					{
						var pos = n.Target.CenterPosition;

						// On the head (current) node, decide whether the overlay applies. It does only
						// when the remembered slot matches this move's destination (the memory is live,
						// not left over from an earlier order) and the order point differs from the slot
						// (an actual spread happened — not a solo move or an executor reposition, both of
						// which record order-point == slot).
						if (!haveHead)
						{
							haveHead = true;
							headColor = n.Color;
							if (slotMemory != null && slotMemory.AssignedSlot is CPos slot && slotMemory.OrderPoint is CPos op)
							{
								var headCell = self.World.Map.CellContaining(pos);
								if (slot == headCell && op != slot)
								{
									cohesionOrderPoint = op;
									haveOrderPoint = true;
								}
							}
						}

						int lineWidth, markerWidth;
						int? overrideAlpha = null;
						// Lesser legs are dashed + thin + alpha-tuned so they read as unmistakably
						// weaker than the solid, normal-weight primary order line at a glance.
						var dashedLeg = haveOrderPoint;
						if (haveOrderPoint)
						{
							lineWidth = info.LesserLineWidth;
							markerWidth = info.LesserLineWidth;
							overrideAlpha = info.LesserLineAlpha;
						}
						else
						{
							lineWidth = renderableCache.Count > 0 ? info.QueuedLineWidth : info.LineWidth;
							markerWidth = renderableCache.Count > 0 ? info.QueuedMarkerWidth : info.MarkerWidth;
						}

						renderableCache.Add(new TargetLineRenderable(new[] { prev, pos }, n.Color, lineWidth, markerWidth, dashedLeg, overrideAlpha));
						prev = pos;
					}
				}
			}

			if (renderableCache.Count == 0)
				return Enumerable.Empty<IRenderable>();

			// Reverse draw order so target markers are drawn on top of the next line
			renderableCache.Reverse();

			// Primary order line added last so it draws on top of the faint lesser legs.
			if (haveOrderPoint)
			{
				var orderPos = self.World.Map.CenterOfCell(cohesionOrderPoint);
				renderableCache.Add(new TargetLineRenderable(new[] { self.CenterPosition, orderPos }, headColor, info.LineWidth, info.MarkerWidth));
			}

			return renderableCache.ToArray();
		}

		bool IRenderAnnotationsWhenSelected.SpatiallyPartitionable => false;
	}

	public static class LineTargetExts
	{
		public static void ShowTargetLines(this Actor self)
		{
			// Target lines are only automatically shown for the owning player
			// Spectators and allies must use the force-display modifier
			if (self.Owner != self.World.LocalPlayer)
				return;

			// Draw after frame end so that all the queueing of activities are done before drawing.
			var line = self.TraitOrDefault<DrawLineToTarget>();
			if (line != null)
				self.World.AddFrameEndTask(w => line.ShowTargetLines(self));
		}
	}
}
