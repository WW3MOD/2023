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
	public enum HelicopterAIRole { Scout, AttackLight, AttackHeavy, Transport }

	[Desc("Configures how the AI uses this helicopter. Requires HelicopterSquadBotModule on the Player actor.")]
	public class AIHelicopterRoleInfo : TraitInfo
	{
		[Desc("AI behavior role for this helicopter type.")]
		public readonly HelicopterAIRole Role = HelicopterAIRole.AttackHeavy;

		[Desc("Preferred engagement range in cells. AI tries to attack from this distance.")]
		public readonly int EngagementRange = 6;

		[Desc("HP percentage below which the helicopter breaks contact and returns to base.")]
		public readonly int FleeHealthPercent = 40;

		[Desc("HP percentage the helicopter must reach after repair before being sent out again.")]
		public readonly int ReEngageHealthPercent = 80;

		[Desc("Ticks of engagement before pulling back (hit-and-run cycle). 0 = stay engaged until flee threshold.")]
		public readonly int HitAndRunCooldown = 150;

		[Desc("Whether this helicopter prefers targets without anti-air nearby.")]
		public readonly bool PreferSoftTargets = true;

		[Desc("Cells to stay away from known anti-air units.")]
		public readonly int AvoidAntiAirRange = 6;

		[Desc("Priority for the AI to build this unit. Higher = more likely to be built.")]
		public readonly int AIBuildPriority = 50;

		[Desc("Maximum number of this type the AI should build.")]
		public readonly int AIBuildLimit = 3;

		public override object Create(ActorInitializer init) { return new AIHelicopterRole(this); }
	}

	public class AIHelicopterRole : INotifyCreated
	{
		public readonly AIHelicopterRoleInfo Info;

		public AIHelicopterRole(AIHelicopterRoleInfo info)
		{
			Info = info;
		}

		void INotifyCreated.Created(Actor self) { }
	}
}
