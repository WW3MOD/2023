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

		WDist[] effectiveRange;

		// void IRulesetLoaded<WeaponInfo>.RulesetLoaded(Ruleset rules, WeaponInfo info)
		// {
		// 	if (Range != null)
		// 	{
		// 		if (Range.Length != 1 && Range.Length != Amount.Length)
		// 			throw new YamlException("Number of range values must be 1 or equal to the number of Amount values.");

		// 		for (var i = 0; i < Range.Length - 1; i++)
		// 			if (Range[i] > Range[i + 1])
		// 				throw new YamlException("Range values must be specified in an increasing order.");

		// 		effectiveRange = Range;
		// 	}
		// 	else
		// 		effectiveRange = Exts.MakeArray(Amount.Length, i => i * Spread);
		// }

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

				for (var i = 0; i < Amount[0]; i++)
				{
					a.TraitsImplementing<ExternalCondition>()
						.FirstOrDefault(t => t.Info.Condition == Condition && t.CanGrantCondition(firedBy))
						?.GrantCondition(a, firedBy, Duration);
				}
			}
		}
	}
}
