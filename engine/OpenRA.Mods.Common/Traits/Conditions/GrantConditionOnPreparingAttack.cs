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
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public class GrantConditionOnPreparingAttackInfo : PausableConditionalTraitInfo
	{
		[FieldLoader.Require]
		[GrantedConditionReference]
		[Desc("The condition type to grant.")]
		public readonly string Condition = null;

		[Desc("Name of the armaments that grant this condition.")]
		public readonly HashSet<string> ArmamentNames = new HashSet<string>() { "primary" };

		[Desc("Shots required to apply an instance of the condition. If there are more instances of the condition granted than values listed,",
			"the last value is used for all following instances beyond the defined range.")]
		public readonly int[] RequiredShotsPerInstance = { 1 };

		[Desc("Maximum instances of the condition to grant.")]
		public readonly int MaximumInstances = 1;

		[Desc("Should all instances reset if the actor passes the final stage?")]
		public readonly bool IsCyclic = false;

		[Desc("Amount of ticks required to pass without firing to revoke an instance.")]
		public readonly int RevokeDelay = 50;

		[Desc("Amount of ticks required to pass without firing to revoke an instance.")]
		public readonly int PreparingRevokeDelay = 50;

		[Desc("Amount of ticks required to pass without firing to revoke an instance.")]
		public readonly int AttackingRevokeDelay = 50;

		[Desc("Should an instance be revoked if the actor changes target?")]
		public readonly bool RevokeOnNewTarget = false;

		[Desc("Should all instances be revoked instead of only one?")]
		public readonly bool RevokeAll = false;

		public override object Create(ActorInitializer init) { return new GrantConditionOnPreparingAttack(init, this); }
	}

	public class GrantConditionOnPreparingAttack : PausableConditionalTrait<GrantConditionOnPreparingAttackInfo>, INotifyCreated, ITick, INotifyAttack
	{
		readonly Stack<int> tokens = new Stack<int>();
		/* readonly Stack<int> preparingTokens = new Stack<int>();
		readonly Stack<int> attackingTokens = new Stack<int>(); */

		/* INotifyAttack[] notifyAttacks; */

		int cooldown = 0;
		/* int preparingCooldown = 0;
		int attackingCooldown = 0; */

		// Only tracked when RevokeOnNewTarget is true.
		/* readonly Target lastTarget = Target.Invalid; */

		public GrantConditionOnPreparingAttack(ActorInitializer _, GrantConditionOnPreparingAttackInfo info)
			: base(info) { }

		/* protected override void Created(Actor self)
		{
			notifyAttacks = self.TraitsImplementing<INotifyAttack>().ToArray();

			base.Created(self);
		} */

		void ITick.Tick(Actor self)
		{
			if (tokens.Count > 0 && --cooldown == 0)
			{
				cooldown = Info.RevokeDelay;
				RevokeInstance(self, Info.RevokeAll);
			}

			/* if (preparingTokens.Count > 0 && --preparingCooldown == 0)
			{
				preparingCooldown = Info.PreparingRevokeDelay;
				RevokeInstance(self, Info.RevokeAll);
			}
			if (tokens.Count > 0 && --cooldown == 0)
			{
				cooldown = Info.RevokeDelay;
				RevokeInstance(self, Info.RevokeAll);
			} */
		}

		void INotifyAttack.PreparingAttack(Actor self, in Target target, Armament a, Barrel barrel)
		{
			if (IsTraitDisabled || IsTraitPaused)
				return;

			if (!Info.ArmamentNames.Contains(a.Info.Name))
				return;

			cooldown = Info.RevokeDelay;
			/* preparingCooldown = Info.PreparingRevokeDelay; */

			GrantInstance(self, Info.Condition);
		}

		void INotifyAttack.Attacking(Actor self, in Target target, Armament a, Barrel barrel)
		{
			if (IsTraitDisabled || IsTraitPaused)
				return;

			if (!Info.ArmamentNames.Contains(a.Info.Name))
				return;

			/* if (Info.RevokeOnNewTarget)
			{
				if (TargetChanged(lastTarget, target))
					RevokeInstance(self, Info.RevokeAll);

				lastTarget = target;
			} */

			if (tokens.Count >= Info.MaximumInstances)
				return;

			cooldown = Info.RevokeDelay;
			/* attackingCooldown = Info.AttackingRevokeDelay; */

			GrantInstance(self, Info.Condition);
		}

		void GrantInstance(Actor self, string cond)
		{
			if (string.IsNullOrEmpty(cond))
				return;

			tokens.Push(self.GrantCondition(cond));
		}

		void RevokeInstance(Actor self, bool revokeAll)
		{
			if (tokens.Count == 0)
				return;

			if (!revokeAll)
				self.RevokeCondition(tokens.Pop());
			else
				while (tokens.Count > 0)
					self.RevokeCondition(tokens.Pop());
		}

		protected override void TraitDisabled(Actor self)
		{
			RevokeInstance(self, true);
		}
	}
}
