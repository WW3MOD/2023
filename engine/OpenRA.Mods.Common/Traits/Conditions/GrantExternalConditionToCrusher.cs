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
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Grant a condition to the crushing actor.")]
	public class GrantExternalConditionToCrusherInfo : TraitInfo
	{
		[Desc("The condition to apply on a crush attempt. Must be included among the passer actor's ExternalCondition traits.")]
		public readonly string WarnPassCondition = null;

		[Desc("Duration of the condition applied on a crush attempt (in ticks). Set to 0 for a permanent condition.")]
		public readonly int WarnPassDuration = 0;

		[Desc("The condition to apply on a successful crush. Must be included among the passer actor's ExternalCondition traits.")]
		public readonly string OnBeingPassedCondition = null;

		[Desc("Duration of the condition applied on a successful crush (in ticks). Set to 0 for a permanent condition.")]
		public readonly int OnBeingPassedDuration = 0;

		public override object Create(ActorInitializer init) { return new GrantExternalConditionToCrusher(this); }
	}

	public class GrantExternalConditionToCrusher : INotifyBeingPassed
	{
		public readonly GrantExternalConditionToCrusherInfo Info;

		public GrantExternalConditionToCrusher(GrantExternalConditionToCrusherInfo info)
		{
			Info = info;
		}

		void INotifyBeingPassed.WarnPass(Actor self, Actor passer, BitSet<PassClass> passClasses)
		{
			passer.TraitsImplementing<ExternalCondition>()
				.FirstOrDefault(t => t.Info.Condition == Info.WarnPassCondition && t.CanGrantCondition(self))
				?.GrantCondition(passer, self, Info.WarnPassDuration);
		}

		void INotifyBeingPassed.OnBeingPassed(Actor self, Actor passer, BitSet<PassClass> passClasses)
		{
			passer.TraitsImplementing<ExternalCondition>()
				.FirstOrDefault(t => t.Info.Condition == Info.OnBeingPassedCondition && t.CanGrantCondition(self))
				?.GrantCondition(passer, self, Info.OnBeingPassedDuration);
		}

		void INotifyBeingPassed.OnBeingCrushed(Actor self, Actor passer, BitSet<PassClass> passClasses)
		{
		}
	}
}
