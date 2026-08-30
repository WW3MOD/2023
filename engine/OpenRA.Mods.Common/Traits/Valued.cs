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
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("How much the unit is worth.")]
	public class ValuedInfo : TraitInfo<Valued>, IProvideTooltipDescription
	{
		[FieldLoader.Require]
		[Desc("Used in production, but also for bounties so remember to set it > 0 even for NPCs.")]
		public readonly int Cost = 0;

		/// <summary>
		/// The purchase price as a row, beside the refill price. The right rail already shows this
		/// number against a coin sprite, but the rail cannot say what KIND of number it is — setting
		/// "Call-in 2500 cash" next to "Full refill 240 supply" is what makes the two currencies
		/// legible as different, which is the point of the pair.
		/// </summary>
		IEnumerable<TooltipElement> IProvideTooltipDescription.ProvideTooltipDescription(ActorInfo ai, Ruleset rules, out int priority)
		{
			priority = 500;

			if (Cost <= 0)
				return null;

			return new[] { TooltipElement.Cost("Call-in", $"{Cost} cash") };
		}
	}

	public class Valued { }
}
