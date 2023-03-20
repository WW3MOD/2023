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
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public class GrantConditionOnMovementInfo : ConditionalTraitInfo, Requires<IMoveInfo>
	{
		[FieldLoader.Require]
		[GrantedConditionReference]
		[Desc("Condition to grant.")]
		public readonly string Condition = null;

		[Desc("Apply condition on listed movement types. Available options are: None, Horizontal, Vertical, Turn.")]
		public readonly HashSet<MovementType> ValidMovementTypes = new HashSet<MovementType>() { MovementType.Horizontal, MovementType.Vertical };
		public readonly HashSet<MovementType> ValidStopTypes = new HashSet<MovementType>() { MovementType.None };

		// [FieldLoader.Require]
		[GrantedConditionReference]
		[Desc("Condition to grant after not moving for {TimeToBeStill} ticks.")]
		public readonly string ConditionWhenStill = null;

		[Desc("Amount of ticks required to pass without firing to revoke an instance.")]
		public readonly int TimeToBeStill = 0;

		public override object Create(ActorInitializer init) { return new GrantConditionOnMovement(init.Self, this); }
	}

	public class GrantConditionOnMovement : ConditionalTrait<GrantConditionOnMovementInfo>, INotifyMoving, ITick
	{
		readonly IMove movement;
		int conditionToken = Actor.InvalidConditionToken;
		int conditionWhenStillToken = Actor.InvalidConditionToken;
		int cooldown = 0;

		public GrantConditionOnMovement(Actor self, GrantConditionOnMovementInfo info)
			: base(info)
		{
			movement = self.Trait<IMove>();
		}

		void ITick.Tick(Actor self)
		{
			if (Info.TimeToBeStill != 0 && conditionWhenStillToken == Actor.InvalidConditionToken)
			{
				if (--cooldown == 0) {
					conditionWhenStillToken = self.GrantCondition(Info.ConditionWhenStill);
				}
			}
		}

		void UpdateCondition(Actor self, MovementType types)
		{
			var validMovement = !IsTraitDisabled && (Info.ValidMovementTypes.Contains(types));
			var validStop = !IsTraitDisabled && !validMovement && (Info.ValidStopTypes.Contains(types));

			if (validStop && conditionToken != Actor.InvalidConditionToken){
				conditionToken = self.RevokeCondition(conditionToken);
				cooldown = Info.TimeToBeStill;
			}
			else if (validMovement && conditionToken == Actor.InvalidConditionToken) {
				conditionToken = self.GrantCondition(Info.Condition);
				if (conditionWhenStillToken != Actor.InvalidConditionToken) {
					conditionWhenStillToken = self.RevokeCondition(conditionWhenStillToken);
				}
			}
		}

		void INotifyMoving.MovementTypeChanged(Actor self, MovementType types)
		{
			UpdateCondition(self, types);
		}

		protected override void TraitEnabled(Actor self)
		{
			UpdateCondition(self, movement.CurrentMovementTypes);
		}

		protected override void TraitDisabled(Actor self)
		{
			UpdateCondition(self, movement.CurrentMovementTypes);
		}
	}
}
