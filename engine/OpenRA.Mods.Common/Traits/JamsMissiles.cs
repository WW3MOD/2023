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
	[Desc("This actor deflects missiles.")]
	public class JamsMissilesInfo : ConditionalTraitInfo
	{
		[Desc("Range of the deflection.")]
		public readonly WDist Range = WDist.Zero;

		[Desc("What player relationships are affected.")]
		public readonly PlayerRelationship DeflectionRelationships = PlayerRelationship.Neutral | PlayerRelationship.Enemy;

		[Desc("Chance of deflecting missiles.")]
		public readonly int Chance = 100;

		[Desc("Should explode instead of deflect")]
		public readonly bool ActiveProtection = false;

		public override object Create(ActorInitializer init) { return new JamsMissiles(this); }
	}

	public class JamsMissiles : ConditionalTrait<JamsMissilesInfo>
	{
		public WDist Range => IsTraitDisabled ? WDist.Zero : Info.Range;
		public PlayerRelationship DeflectionStances => Info.DeflectionRelationships;
		public int Chance => Info.Chance;

		public JamsMissiles(JamsMissilesInfo info)
			: base(info) { }
	}
}
