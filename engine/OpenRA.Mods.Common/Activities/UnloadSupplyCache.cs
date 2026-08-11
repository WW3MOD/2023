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

using OpenRA.Activities;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Mods.Common.Activities
{
	/// <summary>Unload the transport's whole supply as a SUPPLYCACHE on the cell it is standing on.
	///
	/// <para>THE ONLY REASON THIS IS AN ACTIVITY is that a deploy has to be queueable. The drop used to
	/// run inline inside `DropsSupplyCache.ResolveOrder`, which meant it happened at order-resolution
	/// time no matter what the player asked for — a shift-queued deploy dropped the load under the
	/// truck's wheels before the first waypoint had been driven. Nothing else about the work needs an
	/// activity: it completes in a single tick and holds no state.</para></summary>
	public class UnloadSupplyCache : Activity
	{
		readonly DropsSupplyCache transport;

		public UnloadSupplyCache(DropsSupplyCache transport)
		{
			this.transport = transport;
		}

		public override bool Tick(Actor self)
		{
			// A cancelled deploy must not still drop: cancelling is how a replacement order (or a
			// Stop) says the player changed their mind about unloading here.
			if (!IsCanceling)
				transport.DropSupplyCacheHere();

			return true;
		}
	}
}
