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
using OpenRA.Mods.Common.Effects;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Lets the actor generate cash in a set periodic time.")]
	public class CashTricklerInfo : PausableConditionalTraitInfo, IRulesetLoaded
	{
		[FieldLoader.Require]
		[Desc("Amount of money to give each time.")]
		public readonly int Amount = 15;

		[Desc("Number of ticks to wait between giving money.")]
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

		public override object Create(ActorInitializer init) { return new CashTrickler(this); }
	}

	public class CashTrickler : PausableConditionalTrait<CashTricklerInfo>, ITick, ISync, INotifyCreated, INotifyOwnerChanged
	{
		readonly CashTricklerInfo info;
		PlayerResources resources;
		[Sync]
		public int Ticks { get; private set; }

		public CashTrickler(CashTricklerInfo info)
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
			resources = newOwner.PlayerActor.Trait<PlayerResources>();
		}

		void ITick.Tick(Actor self)
		{
			if (IsTraitDisabled)
				Ticks = info.Interval;

			if (IsTraitPaused || IsTraitDisabled)
				return;

			if (--Ticks < 0)
			{
				var cashTrickerModifier = self.Owner.PlayerActor.TraitsImplementing<ICashTricklerModifier>().Select(x => x.GetCashTricklerModifier());

				Ticks = info.Interval;
				ModifyCash(self, Util.ApplyPercentageModifiers(info.Amount, cashTrickerModifier));
			}
		}

		void AddCashTick(Actor self, int amount)
		{
			self.World.AddFrameEndTask(w => w.Add(
				new FloatingText(self.CenterPosition, self.Owner.Color, FloatingText.FormatCashTick(amount), info.DisplayDuration)));
		}

		void ModifyCash(Actor self, int amount)
		{
			amount = resources.ChangeCash(amount);

			if (info.ShowTicks && amount != 0)
				AddCashTick(self, amount);
		}
	}
}
