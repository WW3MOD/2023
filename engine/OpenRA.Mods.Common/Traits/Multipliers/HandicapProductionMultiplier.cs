#region Copyright & License Information
/*
 * Copyright 2007-2020 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Collections.Generic;
using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Modifies the production cost and time of this actor based on the producer's handicap.")]
	public class HandicapProductionMultiplierInfo : TraitInfo<HandicapProductionTimeMultiplier>, IProductionTimeModifierInfo, IProductionCostModifierInfo
	{
		int IProductionTimeModifierInfo.GetProductionTimeModifier(TechTree techTree, string queue)
		{
			int handicap = techTree.Owner.Handicap;

			if (handicap > 0)
			{
				float div = 100F / (100 - handicap);

				return (int) (100 / div);
			}

			return 100;
		}

		int IProductionCostModifierInfo.GetProductionCostModifier(OpenRA.Mods.Common.Traits.TechTree techTree, string queue)
		{
			int handicap = techTree.Owner.Handicap;

			if (handicap > 0)
			{
				float div = 100F / (100 - handicap);

				return (int) (100 * div);
			}

			return 100;
		}
	}

	public class HandicapProductionMultiplier { }
}
