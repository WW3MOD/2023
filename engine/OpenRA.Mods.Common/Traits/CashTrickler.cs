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
	[Desc("Lets the actor generate cash in a set periodic time.")]
	public class CashTricklerInfo : PausableConditionalTraitInfo, IRulesetLoaded
	{
		[FieldLoader.Require]
		[Desc("Amount of money to give each time.")]
		public readonly int Amount = 0;

		[Desc("Number of ticks to wait between giving money.",
			"Used to normalize the income rate when registering with the unified economy tick.")]
		public readonly int Interval = 60;

		[Desc("Number of ticks to wait before giving first money.")]
		public readonly int InitialDelay = 0;

		[Desc("Whether to show the cash tick indicators rising from the actor.")]
		public readonly bool ShowTicks = false;

		[Desc("How long to show the cash tick indicator when enabled.")]
		public readonly int DisplayDuration = 15;

		void IRulesetLoaded<ActorInfo>.RulesetLoaded(Ruleset rules, ActorInfo info)
		{
			if (ShowTicks && !info.HasTraitInfo<IOccupySpaceInfo>())
				throw new YamlException($"CashTrickler is defined with ShowTicks 'true' but actor '{info.Name}' occupies no space.");
		}

		public override object Create(ActorInitializer init) { return new CashTrickler(init, this); }
	}

	public class CashTrickler : PausableConditionalTrait<CashTricklerInfo>, ITick, ISync,
		INotifyCreated, INotifyOwnerChanged, INotifyAddedToWorld, INotifyRemovedFromWorld
	{
		readonly CashTricklerInfo info;
		PlayerResources resources;
		IncomeEntry registeredEntry;
		int lastModifiedAmount;
		bool isInWorld;

		[Sync]
		public int Ticks { get; private set; }

		public CashTrickler(ActorInitializer init, CashTricklerInfo info)
			: base(info)
		{
			this.info = info;
			Ticks = info.InitialDelay;
		}

		protected override void Created(Actor self)
		{
			resources = self.Owner.PlayerActor.Trait<PlayerResources>();
			base.Created(self);
		}

		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner)
		{
			Unregister();
			resources = newOwner.PlayerActor.Trait<PlayerResources>();
			Register(self);
		}

		void INotifyAddedToWorld.AddedToWorld(Actor self)
		{
			isInWorld = true;
			Register(self);
		}

		void INotifyRemovedFromWorld.RemovedFromWorld(Actor self)
		{
			isInWorld = false;
			Unregister();
		}

		protected override void TraitEnabled(Actor self)
		{
			Register(self);
		}

		protected override void TraitDisabled(Actor self)
		{
			Unregister();
		}

		protected override void TraitResumed(Actor self)
		{
			Register(self);
		}

		protected override void TraitPaused(Actor self)
		{
			Unregister();
		}

		int GetModifiedAmount(Actor self)
		{
			return Util.ApplyPercentageModifiers(info.Amount, EnumerateModifiers(self));
		}

		// PITFALL: don't refold this into a Concat(...).Select(...) chain — Tick() runs
		// GetModifiedAmount every tick per CashTrickler actor. Iterator method drops the
		// Concat + Select iterator allocs.
		static IEnumerable<int> EnumerateModifiers(Actor self)
		{
			foreach (var t in self.TraitsImplementing<ICashTricklerModifier>())
				yield return t.GetCashTricklerModifier();

			foreach (var t in self.Owner.PlayerActor.TraitsImplementing<ICashTricklerModifier>())
				yield return t.GetCashTricklerModifier();
		}

		void Register(Actor self)
		{
			if (registeredEntry != null || !isInWorld || IsTraitDisabled || IsTraitPaused)
				return;

			var modifiedAmount = GetModifiedAmount(self);
			var tooltip = self.Info.TraitInfoOrDefault<TooltipInfo>();
			var name = tooltip?.Name ?? self.Info.Name;

			registeredEntry = resources.AddIncome(self.Info.Name, name, modifiedAmount);
			lastModifiedAmount = modifiedAmount;
		}

		void Unregister()
		{
			if (registeredEntry == null)
				return;

			resources.RemoveIncome(registeredEntry);
			registeredEntry = null;
		}

		void ITick.Tick(Actor self)
		{
			if (registeredEntry == null)
				return;

			// Check if modifiers changed (e.g. CashTricklerMultiplier condition toggled)
			var modifiedAmount = GetModifiedAmount(self);
			if (modifiedAmount != lastModifiedAmount)
			{
				resources.UpdateIncome(registeredEntry, modifiedAmount);
				lastModifiedAmount = modifiedAmount;
			}
		}
	}
}
