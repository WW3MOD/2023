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

using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Grants a condition to this actor when it is owned by a human player.",
		"Mirror of GrantConditionOnBotOwner. Predicate is `Owner.Playable && !Owner.IsBot`: this",
		"matches the gate AutoTarget uses for human per-type defaults and, crucially, EXCLUDES the",
		"scenario garrison players (Playable: False) so garrisons are not enrolled by `!IsBot` alone.")]
	public class GrantConditionOnHumanOwnerInfo : TraitInfo
	{
		[FieldLoader.Require]
		[GrantedConditionReference]
		[Desc("Condition to grant.")]
		public readonly string Condition = null;

		public override object Create(ActorInitializer init) { return new GrantConditionOnHumanOwner(this); }
	}

	public class GrantConditionOnHumanOwner : INotifyCreated, INotifyOwnerChanged
	{
		readonly GrantConditionOnHumanOwnerInfo info;

		int conditionToken = Actor.InvalidConditionToken;

		public GrantConditionOnHumanOwner(GrantConditionOnHumanOwnerInfo info)
		{
			this.info = info;
		}

		static bool IsHuman(Player owner)
		{
			return owner.Playable && !owner.IsBot;
		}

		void INotifyCreated.Created(Actor self)
		{
			if (IsHuman(self.Owner))
				conditionToken = self.GrantCondition(info.Condition);
		}

		void INotifyOwnerChanged.OnOwnerChanged(Actor self, Player oldOwner, Player newOwner)
		{
			if (conditionToken != Actor.InvalidConditionToken)
				conditionToken = self.RevokeCondition(conditionToken);

			if (IsHuman(newOwner))
				conditionToken = self.GrantCondition(info.Condition);
		}
	}
}
