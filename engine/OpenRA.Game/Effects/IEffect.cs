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
using OpenRA.Graphics;

namespace OpenRA.Effects
{
	public interface IEffect
	{
		void Tick(World world);
		IEnumerable<IRenderable> Render(WorldRenderer r);
	}

	// Identifier interface for effects that are added to ScreenMap
	public interface ISpatiallyPartitionable { }

	public interface IEffectAboveShroud { IEnumerable<IRenderable> RenderAboveShroud(WorldRenderer wr); }

	/// <summary>
	/// WW3MOD: an effect that draws AFTER the translucent fog layers but BEFORE the opaque
	/// unexplored-shroud layer, so it keeps full brightness over explored-but-unobserved ground
	/// while never-explored ground still blacks it out.
	/// <para>This exists because fog is composited, not tinted: <see cref="Graphics.SpriteRenderable"/>
	/// carries no fog term at all, and the darkening a normal effect picks up is the fog quads
	/// <c>ShroudRenderer</c> paints over the world afterwards. Moving the draw past those quads is
	/// therefore the only way to escape the darkening, and it is purely a render-order change --
	/// the fog is still drawn over the terrain, so nothing is revealed underneath.</para>
	/// <para>Distinct from <see cref="IEffectAboveShroud"/>, which draws past the unexplored layer
	/// too and so would paint onto never-explored black.</para>
	/// </summary>
	public interface IEffectAboveFog { IEnumerable<IRenderable> RenderAboveFog(WorldRenderer wr); }
	public interface IEffectAnnotation { IEnumerable<IRenderable> RenderAnnotation(WorldRenderer wr); }
}
