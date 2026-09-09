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
using System.Collections.Generic;
using System.Linq;
using OpenRA.GameRules;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Warheads
{
	[Desc("Creates a smudge in `SmudgeLayer`.")]
	public class LeaveSmudgeWarhead : Warhead
	{
		[Desc("Size of the area. A smudge will be created in each tile.", "Provide 2 values for a ring effect (outer/inner).")]
		public readonly int[] Size = { 0, 0 };

		[Desc("Type of smudge to apply to terrain.")]
		public readonly HashSet<string> SmudgeType = new();

		[Desc("Percentage chance the smudge is created.")]
		public readonly int Chance = 100;

		[Desc("Mark the cell even when an actor is standing in it.",
			"The stock behaviour is to skip any cell holding an actor this warhead is not valid against, so a",
			"shell that craters open ground leaves the ground under a building untouched. For a blast that is",
			"supposed to scar the terrain itself that reads as a bug: a nuke left clean, unburnt rectangles",
			"wherever a vehicle, a structure or a wall happened to be standing.",
			"Set this to skip the actor test entirely. It is the only correct switch for that job -- widening",
			"ValidTargets instead cannot close the hole, because two common cases have no target type this",
			"warhead can match: a vehicle husk advertises `NoAutoTarget, Husk` with no `Ground`, and a crate",
			"has no Targetable at all, so its target-type set is empty and BitSet.Overlaps is false against",
			"any ValidTargets whatsoever. The husk case is the common one, not the exotic one -- scar warheads",
			"fire on a delay, so the vehicles in a blast are already husks by the time the smudge lands.",
			"Defaults to false, so every warhead that does not opt in keeps the stock behaviour exactly.")]
		public readonly bool IgnoreActors = false;

		public override void DoImpact(in Target target, WarheadArgs args)
		{
			if (target.Type == TargetType.Invalid)
				return;

			var firedBy = args.SourceActor;
			var world = firedBy.World;

			if (Chance < world.LocalRandom.Next(100))
				return;

			var pos = target.CenterPosition;
			var dat = world.Map.DistanceAboveTerrain(pos);

			if (dat > AirThreshold)
				return;

			var targetTile = world.Map.CellContaining(pos);
			var smudgeLayers = world.WorldActor.TraitsImplementing<SmudgeLayer>().ToDictionary(x => x.Info.Type);

			var minRange = (Size.Length > 1 && Size[1] > 0) ? Size[1] : 0;
			var allCells = world.Map.FindTilesInAnnulus(targetTile, minRange, Size[0]);

			// Draw the smudges:
			foreach (var sc in allCells)
			{
				var smudgeType = world.Map.GetTerrainInfo(sc).AcceptsSmudgeType.FirstOrDefault(SmudgeType.Contains);
				if (smudgeType == null)
					continue;

				// BlockingActorsAt, not GetActorsAt: a field has a full-cell HitShape but no Targetable, so its
				// target-type set is empty and IsValidAgainst always says "invalid actor under the shell" — which
				// suppressed every crater and scorch mark on farmland. Same root cause and same fix as
				// CreateEffectWarhead and WarheadAS, which swallowed the explosion itself for this reason.
				// IgnoreActors skips the test outright: see the field's Desc for why widening ValidTargets is
				// not an equivalent, and note that this leaves ValidTargets/InvalidTargets with no reader at
				// all on a warhead that sets it — they are inert here, not merely permissive.
				if (!IgnoreActors)
				{
					var cellActors = world.BlockingActorsAt(sc);
					if (cellActors.Any(a => !IsValidAgainst(a, firedBy)))
						continue;
				}

				if (!smudgeLayers.TryGetValue(smudgeType, out var smudgeLayer))
					throw new NotImplementedException($"Unknown smudge type `{smudgeType}`");

				smudgeLayer.AddSmudge(sc);
			}
		}
	}
}
