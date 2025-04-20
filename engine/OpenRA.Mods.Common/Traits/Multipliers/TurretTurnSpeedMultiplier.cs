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

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Modifies the movement speed of the turret of this actor.")]
	public class TurretTurnSpeedMultiplierInfo : ConditionalTraitInfo
	{
		[FieldLoader.Require]
		[Desc("Percentage modifier to apply.")]
		public readonly int Modifier = 100;

		public override object Create(ActorInitializer init) { return new TurretTurnSpeedMultiplier(this); }
	}

	public class TurretTurnSpeedMultiplier : ConditionalTrait<TurretTurnSpeedMultiplierInfo>, ITurretTurnSpeedModifier
	{
		public TurretTurnSpeedMultiplier(TurretTurnSpeedMultiplierInfo info)
			: base(info) { }

		int ITurretTurnSpeedModifier.GetTurretTurnSpeedModifier() { return IsTraitDisabled ? 100 : Info.Modifier; }
	}
}
