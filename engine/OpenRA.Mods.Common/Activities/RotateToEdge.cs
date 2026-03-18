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

using System.Linq;
using OpenRA.Activities;
using OpenRA.Mods.Common.Effects;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Activities
{
	class RotateToEdge : Activity
	{
		readonly IHealth health;
		readonly SellableInfo sellableInfo;
		readonly PlayerResources playerResources;
		readonly bool showTicks;
		CPos? edgeCell;
		bool movingToEdge;

		public RotateToEdge(Actor self, bool showTicks)
		{
			this.showTicks = showTicks;
			health = self.TraitOrDefault<IHealth>();
			sellableInfo = self.Info.TraitInfo<SellableInfo>();
			playerResources = self.Owner.PlayerActor.Trait<PlayerResources>();
		}

		protected override void OnFirstRun(Actor self)
		{
			var aircraftInfo = self.Info.TraitInfoOrDefault<AircraftInfo>();
			var mobileInfo = self.Info.TraitInfoOrDefault<MobileInfo>();

			if (aircraftInfo != null)
			{
				// Aircraft fly directly to nearest map edge
				edgeCell = self.World.Map.ChooseClosestEdgeCell(self.Location);
			}
			else if (mobileInfo != null)
			{
				// Ground units: route through SpawnArea if one exists (road routing),
				// then to map edge from there
				var spawnAreaCell = FindSpawnAreaLocation(self);
				if (spawnAreaCell.HasValue)
				{
					// Move to spawn area first (follows road), then to edge from there
					var move = self.TraitOrDefault<IMove>();
					if (move != null)
						QueueChild(move.MoveTo(spawnAreaCell.Value, 2, evaluateNearestMovableCell: true));
				}

				// Find edge cell for final destination
				var pathFinder = self.World.WorldActor.Trait<IPathFinder>();
				var locomotor = self.World.WorldActor.TraitsImplementing<Locomotor>().First(l => l.Info.Name == mobileInfo.Locomotor);
				var origin = spawnAreaCell ?? self.Location;
				edgeCell = self.World.Map.ChooseClosestMatchingEdgeCell(origin,
					c => mobileInfo.CanEnterCell(self.World, null, c) && pathFinder.PathExistsForLocomotor(locomotor, c, origin));
			}
			else
			{
				// No movement capability, sell immediately
				edgeCell = null;
			}
		}

		CPos? FindSpawnAreaLocation(Actor self)
		{
			var spawnAreas = self.World.ActorsWithTrait<SpawnArea>()
				.Where(a => !a.Actor.IsDead && a.Actor.IsInWorld)
				.Select(a => a.Actor)
				.ToList();

			if (spawnAreas.Count == 0)
				return null;

			Actor closest = null;
			var closestDist = int.MaxValue;
			foreach (var sa in spawnAreas)
			{
				var dist = (self.Location - sa.Location).LengthSquared;
				if (dist < closestDist)
				{
					closestDist = dist;
					closest = sa;
				}
			}

			return closest?.Location;
		}

		public override bool Tick(Actor self)
		{
			if (IsCanceling)
				return true;

			// If no edge cell found, sell immediately
			if (!edgeCell.HasValue)
			{
				DoSell(self);
				return true;
			}

			// Wait for child activities (moving to spawn area) to complete
			if (ChildActivity != null)
				return false;

			// Queue move to edge if not done yet
			if (!movingToEdge)
			{
				movingToEdge = true;
				var move = self.TraitOrDefault<IMove>();
				if (move != null)
					QueueChild(move.MoveTo(edgeCell.Value, 2, evaluateNearestMovableCell: true));

				return false;
			}

			// Arrived at edge (or close enough) — sell
			DoSell(self);
			return true;
		}

		void DoSell(Actor self)
		{
			var sellValue = self.GetSellValue();

			var hp = health != null ? (long)health.HP : 1L;
			var maxHP = health != null ? (long)health.MaxHP : 1L;
			var refund = (int)((sellValue * sellableInfo.RefundPercent * hp) / (100 * maxHP));
			refund = playerResources.ChangeCash(refund);

			foreach (var ns in self.TraitsImplementing<INotifySold>())
				ns.Sold(self);

			if (showTicks && refund > 0 && self.Owner.IsAlliedWith(self.World.RenderPlayer))
				self.World.AddFrameEndTask(w => w.Add(new FloatingText(self.CenterPosition, self.Owner.Color, FloatingText.FormatCashTick(refund), 30)));

			Game.Sound.PlayNotification(self.World.Map.Rules, self.Owner, "Speech", sellableInfo.Notification, self.Owner.Faction.InternalName);
			TextNotificationsManager.AddTransientLine(sellableInfo.TextNotification, self.Owner);

			self.Dispose();
		}
	}
}
