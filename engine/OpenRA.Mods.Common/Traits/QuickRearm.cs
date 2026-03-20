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

using System.Collections.Generic;
using System.Linq;
using OpenRA.Mods.Common.Activities;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Passengers entering this actor are rearmed to full ammo and auto-ejected after a delay.",
		"Requires Cargo trait. Deducts from SupplyProvider if present.")]
	public class QuickRearmInfo : TraitInfo, Requires<CargoInfo>
	{
		[Desc("Ticks to wait before ejecting the passenger after loading.")]
		public readonly int EjectDelay = 50;

		public override object Create(ActorInitializer init) { return new QuickRearm(this); }
	}

	public class QuickRearm : INotifyPassengerEntered, ITick
	{
		readonly QuickRearmInfo info;
		readonly List<(Actor passenger, int ejectAt)> pending = new List<(Actor, int)>();

		public QuickRearm(QuickRearmInfo info)
		{
			this.info = info;
		}

		void INotifyPassengerEntered.OnPassengerEntered(Actor self, Actor passenger)
		{
			// Give full ammo to the passenger, deducting from SupplyProvider
			var rearmable = passenger.TraitOrDefault<Rearmable>();
			if (rearmable != null)
			{
				var supplyProvider = self.TraitOrDefault<SupplyProvider>();
				foreach (var pool in rearmable.RearmableAmmoPools)
				{
					while (!pool.HasFullAmmo)
					{
						var cost = pool.Info.SupplyValue;
						if (supplyProvider != null && !supplyProvider.DeductSupply(cost))
							break;

						if (!pool.GiveAmmo(passenger, 1))
							break;
					}
				}
			}

			// Schedule ejection
			pending.Add((passenger, self.World.WorldTick + info.EjectDelay));
		}

		void ITick.Tick(Actor self)
		{
			if (pending.Count == 0)
				return;

			var cargo = self.TraitOrDefault<Cargo>();
			if (cargo == null)
				return;

			var worldTick = self.World.WorldTick;
			for (var i = pending.Count - 1; i >= 0; i--)
			{
				var (passenger, ejectAt) = pending[i];
				if (worldTick < ejectAt)
					continue;

				pending.RemoveAt(i);

				if (passenger.IsDead || !cargo.Passengers.Contains(passenger))
					continue;

				// Eject the passenger (follows UnloadCargo pattern for correct SubCell placement)
				self.World.AddFrameEndTask(w =>
				{
					if (self.IsDead || passenger.IsDead)
						return;

					if (!cargo.Passengers.Contains(passenger))
						return;

					cargo.Unload(self, passenger);

					var pos = passenger.Trait<IPositionable>();

					// Find a valid exit cell + SubCell (same cell first, then adjacent)
					(CPos Cell, SubCell SubCell)? exitSubCell = null;
					var adjacentCells = self.World.Map.FindTilesInAnnulus(self.Location, 0, 1);
					foreach (var c in adjacentCells)
					{
						var sub = pos.GetAvailableSubCell(c);
						if (sub != SubCell.Invalid)
						{
							exitSubCell = (c, sub);
							break;
						}
					}

					// Fallback: use transport's cell with any available SubCell
					if (exitSubCell == null)
					{
						var sub = pos.GetAvailableSubCell(self.Location);
						if (sub != SubCell.Invalid)
							exitSubCell = (self.Location, sub);
						else
							exitSubCell = (self.Location, SubCell.First);
					}

					pos.SetPosition(passenger, exitSubCell.Value.Cell, exitSubCell.Value.SubCell);
					pos.SetCenterPosition(passenger, self.CenterPosition);
					passenger.CancelActivity();
					w.Add(passenger);
				});
			}
		}
	}
}
