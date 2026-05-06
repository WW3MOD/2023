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

using System.Linq;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Allow a non-standard sell/repair value to avoid buy-sell exploits.")]
	public class CustomSellValueInfo : TraitInfo<CustomSellValue>
	{
		[FieldLoader.Require]
		public readonly int Value = 0;
	}

	public class CustomSellValue { }

	public static class CustomSellValueExts
	{
		public static int GetSellValue(this Actor a)
		{
			var csv = a.Info.TraitInfoOrDefault<CustomSellValueInfo>();
			var baseValue = csv != null ? csv.Value
				: a.Info.TraitInfoOrDefault<ValuedInfo>()?.Cost ?? 0;

			// Deduct value of missing ammo
			var missingAmmoValue = 0;
			foreach (var pool in a.TraitsImplementing<AmmoPool>())
			{
				if (pool.Info.CreditValue > 0)
					missingAmmoValue += (pool.Info.Ammo - pool.CurrentAmmoCount) * pool.Info.CreditValue;
			}

			// Deduct value of missing supply on a SupplyProvider host (e.g. Logistics Center).
			var supplyProvider = a.TraitOrDefault<SupplyProvider>();
			if (supplyProvider != null)
				missingAmmoValue += supplyProvider.MissingSupplyValue;

			// Deduct value of missing CargoSupply pool (supply trucks).
			// Without this, an empty truck refunds full cost on evacuate.
			var cargoSupply = a.TraitOrDefault<CargoSupply>();
			if (cargoSupply != null)
			{
				var missingUnits = cargoSupply.Info.MaxSupply - cargoSupply.SupplyCount;
				missingAmmoValue += missingUnits * cargoSupply.Info.CreditValuePerUnit;
			}

			return System.Math.Max(0, baseValue - missingAmmoValue);
		}
	}
}
