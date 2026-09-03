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

using System;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("When this actor reaches the map edge alive, credit its rank back to the owning player's",
		"accumulated purchase stock. On a crew member ejected from a wreck, credits that member's",
		"share of the crew toward the rank of the exact vehicle type it came out of.")]
	public class CreditsRankOnEvacuationInfo : TraitInfo, Requires<GainsExperienceInfo>
	{
		public override object Create(ActorInitializer init) { return new CreditsRankOnEvacuation(); }
	}

	public class CreditsRankOnEvacuation : INotifySold
	{
		string originVehicle;
		int shareNumerator;
		int shareDenominator;

		/// <summary>
		/// Tag a just-spawned crew member with the vehicle it came out of and its share of that
		/// vehicle's crew. Set directly rather than through an ActorInit because VehicleCrew already
		/// holds the new actor at this point, and both of its ejection paths funnel through one
		/// spawn helper.
		/// </summary>
		public void SetCrewOrigin(string vehicleName, int numerator, int denominator)
		{
			originVehicle = vehicleName;
			shareNumerator = numerator;
			shareDenominator = denominator;
		}

		void INotifySold.Selling(Actor self) { }

		void INotifySold.Sold(Actor self)
		{
			// Sold fires from exactly two places: RotateToEdge.DoSell, reached only by an actor that
			// physically got to the map edge, and Sell.cs for a building selling in place. A building
			// cannot hold veterancy, so the level check below makes that second case a no-op - which
			// is why "Sold" is safe to read as "got home alive" here.
			var accumulation = self.Owner.PlayerActor.TraitOrDefault<RankAccumulation>();
			if (accumulation == null)
				return;

			var experience = self.TraitOrDefault<GainsExperience>();
			if (experience == null || experience.Level < 1)
				return;

			// A rank-4 unit brought home still only credits rank 3: rank 4 is forged in combat and
			// must never become purchasable. Clamping rather than dropping the credit, so recovering
			// your best unit is not worth less than recovering a rank-3 one.
			var tier = Math.Min(experience.Level, RankAccrual.MaxPurchasableRank);

			if (originVehicle != null)
				accumulation.CreditCrewShare(originVehicle, tier, shareNumerator, shareDenominator);
			else
				accumulation.CreditWholeUnit(self.Info.Name, tier);
		}
	}
}
