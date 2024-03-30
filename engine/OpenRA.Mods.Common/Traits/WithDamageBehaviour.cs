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

using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("This actor has a death behaviour. Used for mine clearing without causing explosion")]
	public class WithDamageBehaviourInfo : ConditionalTraitInfo
	{
		[Desc("What to do when killed")]
		public readonly string Behaviour = "Dispose";

		[Desc("What to do when damaged")]
		public readonly BitSet<DamageType> DamageTypes = default;

		public override object Create(ActorInitializer init) { return new WithDamageBehaviour(init.Self, this); }
	}

	public class WithDamageBehaviour : ConditionalTrait<WithDamageBehaviourInfo>, INotifyDamage
	{
		public WithDamageBehaviour(Actor _, WithDamageBehaviourInfo info)
			: base(info) { }

		void INotifyDamage.Damaged(OpenRA.Actor self, OpenRA.Traits.AttackInfo e)
		{
			if (IsTraitDisabled)
				return;

			if (e.Damage.DamageTypes.Overlaps(Info.DamageTypes))
				// if (Info.Behaviour == "Dispose"), if there will be more options in future otherwise just dispose
				self.Dispose();
		}
	}
}
