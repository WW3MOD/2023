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

			// Deduct value of missing ammo, accounted in batches of ReloadCount
			// rounds at SupplyValue per batch.
			var missingAmmoValue = 0;
			foreach (var pool in a.TraitsImplementing<AmmoPool>())
			{
				if (pool.Info.SupplyValue <= 0)
					continue;

				var batchSize = System.Math.Max(1, pool.Info.ReloadCount);
				var missingRounds = pool.Info.Ammo - pool.CurrentAmmoCount;
				var missingBatches = missingRounds / batchSize;
				missingAmmoValue += missingBatches * pool.Info.SupplyValue;
			}

			// Deduct value of missing supply on a SupplyProvider host (LC, truck, cache).
			var supplyProvider = a.TraitOrDefault<SupplyProvider>();
			if (supplyProvider != null)
				missingAmmoValue += supplyProvider.MissingSupplyValue;

			return System.Math.Max(0, baseValue - missingAmmoValue);
		}

		/// <summary>
		/// Scale an evacuation refund by the owner's handicap. A handicapped player pays
		/// <c>100/(100-handicap)</c> times list price for everything
		/// (<see cref="HandicapProductionMultiplierInfo"/>, attached on the player in
		/// defaults.yaml), so the refund has to be inflated by the identical factor or
		/// rotating a unit out returns less than it cost to call in.
		/// </summary>
		/// <remarks>
		/// PITFALL: every path that pays cash for an actor leaving the map edge must go
		/// through here. Three used to compute the refund locally and only one of them
		/// applied the adjustment, so the same unit was worth different amounts depending
		/// on whether the player pressed Evacuate or the unit evacuated itself.
		/// The float form is preserved byte-for-byte from DeliversCash.GoDonateCash, where
		/// this arithmetic originally lived — an integer rewrite would round differently
		/// and silently move @stable refunds.
		/// </remarks>
		public static int ApplyHandicapRefundAdjustment(int amount, Player owner)
		{
			var handicap = owner.Handicap;
			if (handicap <= 0)
				return amount;

			var div = 100F / (100 - handicap);
			return (int)(amount * div);
		}

		/// <summary>
		/// Cash an actor is worth when it rotates off the map edge: <see cref="GetSellValue"/>
		/// with the owner's handicap applied.
		/// </summary>
		public static int GetEvacuationRefund(this Actor a)
		{
			return ApplyHandicapRefundAdjustment(a.GetSellValue(), a.Owner);
		}

		/// <summary>
		/// Apply the one term of the evacuation payout that is read on ARRIVAL rather than frozen at
		/// order time: the fraction of health the actor still has.
		/// </summary>
		/// <remarks>
		/// PITFALL: <see cref="GetEvacuationRefund"/> carries no health term, so it is NOT the cash the
		/// player receives — RotateToEdge.DoSell scales it by HP/MaxHP on arrival. Any surface that
		/// shows a player what an evacuation is worth must apply this too, or it promises a number the
		/// game does not honour. It lives here, called by both, rather than inline in each.
		/// </remarks>
		public static int ScaleRefundByHealth(int refund, Actor a)
		{
			var health = a.TraitOrDefault<IHealth>();
			var hp = health != null ? (long)health.HP : 1L;
			var maxHp = health != null ? (long)health.MaxHP : 1L;
			return EvacRefundPreviewMath.ScaleByHealth(refund, hp, maxHp);
		}

		/// <summary>
		/// Cash the player would receive for evacuating this actor RIGHT NOW: the same expression
		/// DeliversCash.GoDonateCash freezes into the activity, scaled by current health the way
		/// RotateToEdge.DoSell scales it on arrival. Returns 0 for an actor the Evacuate order does not
		/// reach (no <c>DeliversCash@Rotation</c>).
		/// </summary>
		/// <remarks>
		/// Exact for a unit that reaches the map edge in the state it is in now. A unit shot on the way
		/// out arrives worth less, because the health term — and only the health term — is re-read there.
		/// </remarks>
		public static int GetEvacuationRefundNow(this Actor a)
		{
			var rotation = a.Info.TraitInfos<DeliversCashInfo>().FirstOrDefault(di => di.Type == "Rotation");
			if (rotation == null)
				return 0;

			var baseAmount = rotation.Payload == -1 ? a.GetSellValue() : rotation.Payload;
			return ScaleRefundByHealth(ApplyHandicapRefundAdjustment(baseAmount, a.Owner), a);
		}
	}
}
