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
	[Desc("Grants a condition to this actor for a short time after it receives healing.",
		"Exists so a purely visual trait — a pip, an overlay — can answer the DURATIVE question",
		"\"is this man being treated right now\", which a one-shot flash on each heal impact cannot.")]
	public class GrantConditionOnHealedInfo : ConditionalTraitInfo
	{
		[FieldLoader.Require]
		[GrantedConditionReference]
		[Desc("The condition to grant while healing is being received.")]
		public readonly string Condition = null;

		[Desc("Ticks the condition lingers after the most recent heal. Refreshed by each further heal,",
			"so continuous treatment holds it lit continuously. Must comfortably exceed the gap between",
			"heal impacts or the readout strobes; the tail is also what makes a single-shot heal visible",
			"at all. At Timestep 60 (16.67 ticks/sec) 30 ticks is ~1.8 seconds.")]
		public readonly int Duration = 30;

		public override object Create(ActorInitializer init) { return new GrantConditionOnHealed(this); }
	}

	public class GrantConditionOnHealed : ConditionalTrait<GrantConditionOnHealedInfo>, INotifyDamage, ITick
	{
		int token = Actor.InvalidConditionToken;
		int remaining;

		public GrantConditionOnHealed(GrantConditionOnHealedInfo info)
			: base(info) { }

		void INotifyDamage.Damaged(Actor self, AttackInfo e)
		{
			if (IsTraitDisabled || !HealEvent.IsHealing(e))
				return;

			remaining = Info.Duration;
			if (token == Actor.InvalidConditionToken)
				token = self.GrantCondition(Info.Condition);
		}

		void ITick.Tick(Actor self)
		{
			if (remaining > 0 && --remaining == 0)
				Revoke(self);
		}

		protected override void TraitDisabled(Actor self)
		{
			remaining = 0;
			Revoke(self);
		}

		void Revoke(Actor self)
		{
			if (token != Actor.InvalidConditionToken)
				token = self.RevokeCondition(token);
		}
	}
}
