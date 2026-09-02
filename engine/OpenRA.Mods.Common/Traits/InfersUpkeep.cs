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
using System.Globalization;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Unit has upkeep cost.")]
	public class InfersUpkeepInfo : TraitInfo, IProvideTooltipDescription
	{
		public readonly int FixedCost = 0;
		public readonly int PermilleCost = 0;

		/// <summary>Sits directly under Valued's "Call-in", so the two cash figures read as a pair.</summary>
		public const int TooltipPriority = 502;

		public override object Create(ActorInitializer init) { return new InfersUpkeep(init.Self, this); }

		/// <summary>
		/// The whole of the upkeep arithmetic, as a static so it can be exercised without an
		/// <see cref="ActorInfo"/>. Both the live charge and the tooltip route through here — the
		/// display must never become a second implementation of the number the player is billed.
		/// </summary>
		public static float UpkeepPerInterval(int unitCost, int fixedCost, int permilleCost)
		{
			float cost = fixedCost;

			if (permilleCost != 0)
				cost += unitCost * (float)permilleCost / 1000;

			return cost;
		}

		/// <summary>
		/// Deliberately NOT null-guarded on <see cref="ValuedInfo"/>: an actor with a PermilleCost and
		/// no Valued has always thrown here when it spawns, and turning that into a silent 0 would be a
		/// behaviour change nobody asked for. The tooltip path checks for Valued before calling.
		/// </summary>
		public float UpkeepFor(ActorInfo ai)
		{
			var unitCost = PermilleCost != 0 ? ai.TraitInfoOrDefault<ValuedInfo>().Cost : 0;
			return UpkeepPerInterval(unitCost, FixedCost, PermilleCost);
		}

		/// <summary>
		/// "/ interval" rather than a duration: it is the wording the cash counter already uses
		/// (<c>IngameCashCounterLogic.GetBreakdownText</c>, "Net: +$x / interval"), and unlike
		/// "every 3 seconds" it stays true at every game speed. The charge lands on the same 50-tick
		/// line as income (<c>PlayerResources.cs:209</c>), so one interval is one payday either way.
		/// </summary>
		public static string FormatPerInterval(float perInterval)
		{
			// Three decimals, trailing zeros trimmed, rather than a whole number. A rifleman is
			// 50 x 0.005 = 0.25 per interval, and rounded his row would read "0 cash" — which is
			// exactly how the cash counter's breakdown already loses him: it casts each group to
			// int and then skips anything that lands on zero.
			return perInterval.ToString("0.###", CultureInfo.InvariantCulture) + " cash / interval";
		}

		/// <summary>
		/// The negative statement, shared with the renderer. Aircraft and structures carry no
		/// InfersUpkeep at all, so no trait exists to speak for them and the row has to be supplied
		/// from outside — see <c>ProductionTooltipLogic.BuildElements</c>.
		/// </summary>
		public static TooltipElement NoUpkeepRow()
		{
			return TooltipElement.Stat("Upkeep", "None");
		}

		IEnumerable<TooltipElement> IProvideTooltipDescription.ProvideTooltipDescription(ActorInfo ai, Ruleset rules, out int priority)
		{
			priority = TooltipPriority;

			if (PermilleCost != 0 && ai.TraitInfoOrDefault<ValuedInfo>() == null)
				return null;

			var perInterval = UpkeepFor(ai);
			if (perInterval <= 0)
				return new[] { NoUpkeepRow() };

			return new[] { TooltipElement.Cost("Upkeep", FormatPerInterval(perInterval)) };
		}
	}

	public class InfersUpkeep : INotifyOwnerChanged, INotifyCapture, INotifyAddedToWorld, INotifyRemovedFromWorld
	{
		readonly Actor self;
		readonly InfersUpkeepInfo info;
		PlayerResources player;
		UpkeepEntry registeredEntry;

		public InfersUpkeep(Actor self, InfersUpkeepInfo info)
		{
			this.info = info;
			this.self = self;
			player = self.Owner.PlayerActor.Trait<PlayerResources>();
		}

		public float Cost => info.UpkeepFor(self.Info);

		string DisplayName
		{
			get
			{
				var tooltip = self.Info.TraitInfoOrDefault<TooltipInfo>();
				return tooltip?.Name ?? self.Info.Name;
			}
		}

		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner)
		{
			player = newOwner.PlayerActor.Trait<PlayerResources>();
		}

		void INotifyCapture.OnCapture(Actor self, Actor captor, Player oldOwner, Player newOwner, BitSet<CaptureType> captureTypes)
		{
			var oldResources = oldOwner.PlayerActor.Trait<PlayerResources>();
			var newResources = newOwner.PlayerActor.Trait<PlayerResources>();

			if (registeredEntry != null)
				oldResources.RemoveFromUpkeep(registeredEntry);

			registeredEntry = newResources.AddToUpkeep(Cost, self.Info.Name, DisplayName);
		}

		void INotifyAddedToWorld.AddedToWorld(Actor self)
		{
			registeredEntry = player.AddToUpkeep(Cost, self.Info.Name, DisplayName);
		}

		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self)
		{
			if (registeredEntry != null)
			{
				player.RemoveFromUpkeep(registeredEntry);
				registeredEntry = null;
			}
		}
	}
}
