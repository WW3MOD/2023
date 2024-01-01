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

using OpenRA.Primitives;

namespace OpenRA
{
	public class WorldViewportSizes : IGlobalModData
	{
		public readonly int2 CloseWindowHeights = new(480, 600);
		public readonly int2 MediumWindowHeights = new(600, 900);
		public readonly int2 FarWindowHeights = new(900, 1300);

		public readonly float DefaultScale = 1.0f;
		public readonly float MaxZoomScale = 20.0f;
		public readonly int MaxZoomWindowHeight = 30;
		// public readonly int MaxZoomWindowHeight = 240; // 1010?

		public readonly bool AllowNativeZoom = true;

		public readonly Size MinEffectiveResolution = new(1024, 720);

		public int2 GetSizeRange(WorldViewport distance)
		{
			return distance == WorldViewport.Close ? CloseWindowHeights
				: distance == WorldViewport.Medium ? MediumWindowHeights
				: FarWindowHeights;
		}
	}
}
