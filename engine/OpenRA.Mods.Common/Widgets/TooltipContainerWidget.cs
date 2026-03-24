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
using OpenRA.Graphics;
using OpenRA.Primitives;
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets
{
	public class TooltipContainerWidget : Widget
	{
		static readonly Action Nothing = () => { };
		readonly GraphicSettings graphicSettings;

		public int2 CursorOffset = new int2(0, 20);
		public int BottomEdgeYOffset = -5;

		/// <summary>
		/// When set, the tooltip is anchored relative to this rectangle
		/// instead of following the mouse cursor.
		/// </summary>
		public Rectangle? AnchorBounds;
		public bool AnchorAbove;
		public int AnchorGap = 4;

		public Action BeforeRender = Nothing;
		public int TooltipDelayMilliseconds = 200;
		Widget tooltip;
		int nextToken = 1;
		int currentToken;
		string id;
		WidgetArgs widgetArgs;

		public TooltipContainerWidget()
		{
			graphicSettings = Game.Settings.Graphics;
			IsVisible = () =>
			{
				// PERF: Only load widget once visible.
				var visible = Game.RunTime > Viewport.LastMoveRunTime + TooltipDelayMilliseconds;
				if (visible)
					LoadWidget();

				return visible;
			};
		}

		void LoadWidget()
		{
			if (id == null || tooltip != null)
				return;

			tooltip = Ui.LoadWidget(id, this, new WidgetArgs(widgetArgs) { { "tooltipContainer", this } });
		}

		public int SetTooltip(string id, WidgetArgs args)
		{
			RemoveTooltip();
			currentToken = nextToken++;
			tooltip = null;
			this.id = id;
			widgetArgs = args;
			return currentToken;
		}

		public void RemoveTooltip(int token)
		{
			if (currentToken != token)
				return;

			tooltip = null;
			id = null;
			widgetArgs = null;
			AnchorBounds = null;
			AnchorAbove = false;

			RemoveChildren();
			BeforeRender = Nothing;
		}

		public void RemoveTooltip()
		{
			RemoveTooltip(currentToken);
		}

		public override void Draw()
		{
			BeforeRender();
		}

		public override bool EventBoundsContains(int2 location) { return false; }

		public override int2 ChildOrigin
		{
			get
			{
				if (AnchorBounds.HasValue && tooltip != null)
					return GetAnchoredPosition();

				var scale = graphicSettings.CursorDouble ? 2 : 1;
				var pos = Viewport.LastMousePos + scale * CursorOffset;
				if (tooltip != null)
				{
					// If the tooltip overlaps the right edge of the screen, move it left until it fits
					if (pos.X + tooltip.Bounds.Right > Game.Renderer.Resolution.Width)
						pos = pos.WithX(Game.Renderer.Resolution.Width - tooltip.Bounds.Right);

					// If the tooltip overlaps the bottom edge of the screen, switch tooltip above cursor
					if (pos.Y + tooltip.Bounds.Bottom > Game.Renderer.Resolution.Height)
						pos = pos.WithY(Viewport.LastMousePos.Y + scale * BottomEdgeYOffset - tooltip.Bounds.Height);
				}

				return pos;
			}
		}

		int2 GetAnchoredPosition()
		{
			var anchor = AnchorBounds.Value;
			var tooltipWidth = tooltip.Bounds.Right;
			var tooltipHeight = tooltip.Bounds.Bottom;

			int x, y;

			if (AnchorAbove)
			{
				// Center horizontally above the anchor widget
				x = anchor.X + anchor.Width / 2 - tooltipWidth / 2;
				y = anchor.Y - tooltipHeight - AnchorGap;

				// If it goes off the top, flip below
				if (y < 0)
					y = anchor.Bottom + AnchorGap;

				// Clamp to screen bounds horizontally
				if (x + tooltipWidth > Game.Renderer.Resolution.Width)
					x = Game.Renderer.Resolution.Width - tooltipWidth;

				if (x < 0)
					x = 0;
			}
			else
			{
				// Position to the left of the anchor widget, flush with its right edge
				x = anchor.X - tooltipWidth - AnchorGap;

				// Top-align with the button, tooltip expands downward
				y = anchor.Y;

				// If it goes off the left edge, flip to the right side
				if (x < 0)
					x = anchor.Right + AnchorGap;

				// Clamp to screen bounds vertically
				if (y + tooltipHeight > Game.Renderer.Resolution.Height)
					y = Game.Renderer.Resolution.Height - tooltipHeight;

				if (y < 0)
					y = 0;
			}

			return new int2(x, y);
		}

		public override string GetCursor(int2 pos) { return null; }
	}
}
