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
using OpenRA.Widgets;

namespace OpenRA.Mods.Common.Widgets
{
	/// <summary>
	/// Lightweight viewport controller for the shellmap (main menu).
	/// Supports only MMB drag panning and scroll-wheel zoom.
	/// No edge scrolling, keyboard scrolling, tooltips, or bookmarks.
	/// </summary>
	public class ShellmapViewportControllerWidget : Widget
	{
		readonly WorldRenderer worldRenderer;

		int2? scrollStart;
		bool isScrolling;

		[ObjectCreator.UseCtor]
		public ShellmapViewportControllerWidget(WorldRenderer worldRenderer)
		{
			this.worldRenderer = worldRenderer;
		}

		public override bool HandleMouseInput(MouseInput mi)
		{
			// Scroll wheel zoom
			if (mi.Event == MouseInputEvent.Scroll)
			{
				var zoomSpeed = Game.Settings.Game.ZoomSpeed;
				worldRenderer.Viewport.AdjustZoom(mi.Delta.Y * zoomSpeed, mi.Location);
				return true;
			}

			// MMB drag panning — always use middle button regardless of settings
			if (!mi.Button.HasFlag(MouseButton.Middle))
				return isScrolling;

			if (mi.Event == MouseInputEvent.Down && !isScrolling)
			{
				if (!TakeMouseFocus(mi))
					return false;

				scrollStart = mi.Location;
			}
			else if (mi.Event == MouseInputEvent.Move && (isScrolling ||
				(scrollStart.HasValue && ((scrollStart.Value - mi.Location).Length > Game.Settings.Game.MouseScrollDeadzone))))
			{
				isScrolling = true;
				worldRenderer.Viewport.Scroll((Viewport.LastMousePos - mi.Location), false);
				return true;
			}
			else if (mi.Event == MouseInputEvent.Up)
			{
				var wasScrolling = isScrolling;
				isScrolling = false;
				scrollStart = null;
				YieldMouseFocus(mi);

				if (wasScrolling)
					return true;
			}

			return isScrolling;
		}

		public override bool YieldMouseFocus(MouseInput mi)
		{
			scrollStart = null;
			return base.YieldMouseFocus(mi);
		}
	}
}
