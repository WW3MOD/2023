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

using System;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Grants stacks of a stacking condition based on the actor's current HP fraction.",
		"Maps HP fraction linearly to stack count: at StartFraction grants 1 stack, at EndFraction grants MaxStacks.",
		"Pair with an ExternalCondition trait declaring the same Condition with TotalCap >= MaxStacks " +
		"so the condition is registered and stackable. Used by ^Vehicle / ^Helicopter to scale the " +
		"`onfire` overlay intensity from light smoke at the start of Critical to a roaring blaze " +
		"when HP is about to hit zero.")]
	public class GrantStackingConditionOnHealthFractionInfo : ConditionalTraitInfo, Requires<IHealthInfo>
	{
		[GrantedConditionReference]
		[FieldLoader.Require]
		[Desc("Condition name to stack. Must match an ExternalCondition trait on the same actor.")]
		public readonly string Condition = null;

		[Desc("HP percent at which the first stack is granted. Above this fraction the trait is silent.")]
		public readonly int StartFraction = 25;

		[Desc("HP percent at which MaxStacks are granted. Stacks scale linearly between StartFraction and this.")]
		public readonly int EndFraction = 1;

		[Desc("Maximum stack count granted at or below EndFraction.")]
		public readonly int MaxStacks = 10;

		[Desc("Ticks between health checks. Lower = more responsive scaling, higher = cheaper.")]
		public readonly int Interval = 25;

		public override object Create(ActorInitializer init) { return new GrantStackingConditionOnHealthFraction(init.Self, this); }
	}

	public class GrantStackingConditionOnHealthFraction : ConditionalTrait<GrantStackingConditionOnHealthFractionInfo>, ITick
	{
		readonly IHealth health;
		readonly int[] tokens;
		int currentStacks;
		int countdown;

		public GrantStackingConditionOnHealthFraction(Actor self, GrantStackingConditionOnHealthFractionInfo info)
			: base(info)
		{
			health = self.Trait<IHealth>();
			tokens = new int[info.MaxStacks];
			for (var i = 0; i < tokens.Length; i++)
				tokens[i] = Actor.InvalidConditionToken;
		}

		void ITick.Tick(Actor self)
		{
			if (IsTraitDisabled)
			{
				// Trait gated off — drop everything.
				ReleaseTo(self, 0);
				countdown = 0;
				return;
			}

			if (--countdown > 0)
				return;

			countdown = Info.Interval;

			var maxHP = Math.Max(1, health.MaxHP);
			var percent = (health.HP * 100) / maxHP;
			ReleaseTo(self, CalculateStacks(percent, Info.StartFraction, Info.EndFraction, Info.MaxStacks));
		}

		// Extracted for unit testing — pure math, no Actor / World needed.
		// percent: current HP as percent of MaxHP (0-100).
		// Returns: number of stacks the actor should currently hold.
		public static int CalculateStacks(int percent, int startFraction, int endFraction, int maxStacks)
		{
			if (maxStacks <= 0) return 0;
			if (percent > startFraction) return 0;
			if (percent <= endFraction) return maxStacks;

			var span = Math.Max(1, startFraction - endFraction);
			var below = startFraction - percent;
			// +1 so reaching StartFraction grants stack 1, not 0.
			var desired = 1 + ((below - 1) * (maxStacks - 1) + span / 2) / span;
			if (desired < 1) desired = 1;
			if (desired > maxStacks) desired = maxStacks;
			return desired;
		}

		void ReleaseTo(Actor self, int desired)
		{
			while (currentStacks < desired)
			{
				tokens[currentStacks] = self.GrantCondition(Info.Condition);
				currentStacks++;
			}

			while (currentStacks > desired)
			{
				currentStacks--;
				tokens[currentStacks] = self.RevokeCondition(tokens[currentStacks]);
			}
		}
	}
}
