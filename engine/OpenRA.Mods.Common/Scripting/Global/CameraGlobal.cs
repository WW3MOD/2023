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

using OpenRA.Scripting;

namespace OpenRA.Mods.Common.Scripting
{
	[ScriptGlobal("Camera")]
	public class CameraGlobal : ScriptGlobal
	{
		public CameraGlobal(ScriptContext context)
			: base(context) { }

		[Desc("The center of the visible viewport.")]
		public WPos Position
		{
			get => Context.WorldRenderer.Viewport.CenterPosition;
			set => Context.WorldRenderer.Viewport.Center(value);
		}

		// PITFALL: zoom is expressed as a MULTIPLE OF THE DEFAULT zoom, not as the engine's raw
		// Viewport.Zoom. Raw zoom is not portable between machines: Viewport.MinZoom is derived from
		// Game.Renderer.NativeResolution and the player's ViewportDistance setting
		// (Viewport.CalculateMinimumZoom), so the same raw number frames a different amount of map on
		// a 1080p and a 1440p display. A multiple of the default frames the same amount everywhere,
		// which is what a demo or a screenshot scenario actually wants. Test.SetZoom has used these
		// units since it was written; this is the same scale, ungated and next to Camera.Position.
		//
		// This is CLIENT-LOCAL VIEW STATE. Writing it cannot perturb the simulation — no ISync type
		// reads the viewport, which ViewportIsNotSimulationStateTest asserts by IL scan. READING it
		// and branching simulation behaviour on the result WOULD desync a multiplayer match, exactly
		// as reading Camera.Position and branching on it already would. Use it for presentation.
		[Desc("Camera zoom, as a multiple of the default (fully zoomed-out) level: 1 is the default, " +
			"values above 1 zoom in, values below 1 zoom out. Clamped to Camera.MinZoom..Camera.MaxZoom, " +
			"so writing an out-of-range value is not an error — read the property back to see what was " +
			"applied. Client-local presentation state: never branch simulation behaviour on it.")]
		public double Zoom
		{
			get
			{
				var viewport = Context.WorldRenderer.Viewport;
				return viewport.Zoom / viewport.MinZoom;
			}

			// Eluant will not marshal a Lua number into a float parameter, so this is a double and the
			// cast happens here. See conventions.md, "Eluant does not marshal Lua numbers to float".
			set
			{
				var viewport = Context.WorldRenderer.Viewport;
				viewport.SetZoom((float)(viewport.MinZoom * value));
			}
		}

		[Desc("The furthest the camera can zoom out, in the same units as Camera.Zoom. " +
			"Depends on the player's resolution and viewport-distance setting, so read it rather " +
			"than assuming a number.")]
		public double MinZoom
		{
			get
			{
				var viewport = Context.WorldRenderer.Viewport;
				return viewport.EffectiveMinZoom / viewport.MinZoom;
			}
		}

		[Desc("The furthest the camera can zoom in, in the same units as Camera.Zoom.")]
		public double MaxZoom
		{
			get
			{
				var viewport = Context.WorldRenderer.Viewport;
				return viewport.MaxZoom / viewport.MinZoom;
			}
		}
	}
}
