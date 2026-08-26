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

		// This trait is a fork of GrantConditionOnAttack that moves the grant to the
		// PreparingAttack hook. It carried that trait's RequiredShotsPerInstance and
		// IsCyclic fields verbatim but never ported the shotsFired counter that reads
		// them, so both were declared, documented and dead. Removed rather than
		// implemented: all fourteen ww3mod sites use this as a plain "is firing" flag.
		// Use GrantConditionOnAttack if you want the staged shot-counting semantics.
		[Desc("Maximum instances of the condition to grant.")]
		public readonly int MaximumInstances = 1;

		[Desc("Amount of ticks required to pass without firing to revoke an instance.")]
		public readonly int RevokeDelay = 50;

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

			// Refresh before the cap check, so sustained fire keeps holding the
			// condition up once the stack is already full.
			cooldown = Info.RevokeDelay;
			/* preparingCooldown = Info.PreparingRevokeDelay; */

			if (!ShouldGrantInstance(tokens.Count, Info.MaximumInstances))
				return;

			GrantInstance(self, Info.Condition);
		}

		// A shot may push another instance only while the stack is below the cap.
		// Shared by both notification hooks so the two cannot drift apart again:
		// PreparingAttack used to push one token per shot with no cap while Tick
		// pops only one per RevokeDelay, so a burst of N shots pinned the condition
		// up for N * RevokeDelay ticks after the firing stopped.
		public static bool ShouldGrantInstance(int currentTokens, int maximumInstances)
		{
			return currentTokens < maximumInstances;
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

			if (!ShouldGrantInstance(tokens.Count, Info.MaximumInstances))
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
