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

using System.Linq;
using OpenRA.GameRules;
using OpenRA.Mods.Common.Effects;
using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Warheads
{
	public class CreateActorWarhead : Warhead
	{
		[ActorReference]
		[FieldLoader.Require]
		[Desc("Actor to spawn on Impact.")]
		public readonly string Actor = null;
		
		[Desc("Offset of the spawned actor relative to the dying actor's position.",
			"Warning: Spawning an actor outside the parent actor's footprint/influence might",
			"lead to unexpected behaviour.")]
		public readonly CVec Offset = CVec.Zero;

		public override void DoImpact(in Target target, WarheadArgs args)
		{
			var td = new TypeDictionary
			{
				new CenterPositionInit(target.CenterPosition),
				// new LocationInit(new CPos(args.ImpactPosition.X, args.ImpactPosition.Y) + Offset), // seems to do nothing, either way unit goes towards (0,0) location, over any obstacle without responding to orders
				new OwnerInit(args.SourceActor.Owner)
			};

			args.SourceActor.World.AddFrameEndTask(w => w.CreateActor(Actor, td)); // Works with E3, MNLY. Seems to not work with faction specific actors, and not with buildings (footprint error)
		}
	}
}
