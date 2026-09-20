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

using OpenRA.GameRules;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Warheads
{
	[Desc("Grant an external condition to hit actors.")]
	public class GrantExternalConditionWarhead : Warhead
	{
		[FieldLoader.Require]
		[Desc("The condition to apply. Must be included in the target actor's ExternalConditions list.")]
		public readonly string Condition = null;

		[Desc("Duration of the condition (in ticks). Set to 0 for a permanent condition.")]
		public readonly int Duration = 0;

		[Desc("How many times the condition should be granted.")]
		public readonly int[] Amount = { 1 };

		public readonly WDist[] Range = { WDist.FromCells(1) };

		/* WDist[] effectiveRange;

		void IRulesetLoaded<WeaponInfo>.RulesetLoaded(Ruleset rules, WeaponInfo info)
		{
			if (Range != null)
			{
				if (Range.Length != 1 && Range.Length != Amount.Length)
					throw new YamlException("Number of range values must be 1 or equal to the number of Amount values.");

				for (var i = 0; i < Range.Length - 1; i++)
					if (Range[i] > Range[i + 1])
						throw new YamlException("Range values must be specified in an increasing order.");

				effectiveRange = Range;
			}
			else
				effectiveRange = Exts.MakeArray(Amount.Length, i => i * Spread);
		}*/

		public override void DoImpact(in Target target, WarheadArgs args)
		{
			var firedBy = args.SourceActor;

			if (target.Type == TargetType.Invalid)
				return;

			var actors = target.Type == TargetType.Actor ? new[] { target.Actor } :
				firedBy.World.FindActorsInCircle(target.CenterPosition, Range[0]);

			foreach (var a in actors)
			{
				if (!IsValidAgainst(a, firedBy))
					continue;

				// PERF: this was a LINQ FirstOrDefault with a lambda capturing `firedBy` and `this`,
				// evaluated INSIDE the Amount loop -- so a closure, a delegate and an enumerator per
				// actor per amount. WW3MOD's high-yield warheads are what make that add up: a single
				// NukeSarmatRV carries SIXTEEN of these warheads (ten fire, one EMP, five
				// suppression), each sweeping every actor inside 49-65 cells, and six of them fly per
				// salvo.
				//
				// THE LOOKUP STAYS INSIDE THE AMOUNT LOOP AND MUST. CanGrantCondition is stateful --
				// a trait that has just granted can refuse the next grant -- so the second iteration
				// is entitled to select a DIFFERENT ExternalCondition than the first, and hoisting
				// the search would silently collapse `Amount: n` onto one trait. Only the trait-set
				// lookup is hoisted, which is safe because an actor's trait membership is fixed at
				// creation.
				//
				// Same traits, same order, same first match: this is the foreach the LINQ compiled
				// to, without the per-call allocations.
				var externals = a.TraitsImplementing<ExternalCondition>();

				for (var i = 0; i < Amount[0]; i++)
				{
					foreach (var t in externals)
					{
						if (t.Info.Condition != Condition || !t.CanGrantCondition(firedBy))
							continue;

						t.GrantCondition(a, firedBy, Duration);
						break;
					}
				}
			}
		}
	}
}
