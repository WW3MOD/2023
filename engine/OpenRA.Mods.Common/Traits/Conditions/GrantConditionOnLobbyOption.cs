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

using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Grants a condition based on a lobby boolean option value.")]
	public class GrantConditionOnLobbyOptionInfo : TraitInfo
	{
		[FieldLoader.Require]
		[Desc("The lobby option ID to check (e.g. 'airstrikes').")]
		public readonly string Option = null;

		[FieldLoader.Require]
		[GrantedConditionReference]
		[Desc("The condition to grant.")]
		public readonly string Condition = null;

		[Desc("If true, the condition is granted when the option is disabled (false). " +
			"If false, the condition is granted when the option is enabled (true).")]
		public readonly bool GrantWhenOptionDisabled = true;

		public override object Create(ActorInitializer init) { return new GrantConditionOnLobbyOption(this); }
	}

	public class GrantConditionOnLobbyOption : INotifyCreated
	{
		readonly GrantConditionOnLobbyOptionInfo info;
		int conditionToken = Actor.InvalidConditionToken;

		public GrantConditionOnLobbyOption(GrantConditionOnLobbyOptionInfo info)
		{
			this.info = info;
		}

		void INotifyCreated.Created(Actor self)
		{
			var optionEnabled = self.World.LobbyInfo.GlobalSettings
				.OptionOrDefault(info.Option, !info.GrantWhenOptionDisabled);

			var shouldGrant = info.GrantWhenOptionDisabled ? !optionEnabled : optionEnabled;

			if (shouldGrant && conditionToken == Actor.InvalidConditionToken)
				conditionToken = self.GrantCondition(info.Condition);
		}
	}
}
