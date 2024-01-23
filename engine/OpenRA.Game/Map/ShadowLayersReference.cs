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
using OpenRA.Primitives;

namespace OpenRA
{
	public class ShadowLayersReference
	{
		public string Name;

		public ShadowLayersReference() { }
		public ShadowLayersReference(MiniYaml my) { FieldLoader.Load(this, my); }

		public override string ToString() { return Name; }
	}
}
