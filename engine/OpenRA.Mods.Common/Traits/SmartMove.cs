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

using OpenRA.Activities;
using OpenRA.Mods.Common.Activities;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Wraps regular Move orders in SmartMoveActivity so units pause to fire at targets in weapon range while moving.",
		"Units only fire when under attack or when targets are very close (good shot opportunity).")]
	public class SmartMoveInfo : ConditionalTraitInfo
	{
		[Desc("How often to scan for targets (in ticks).")]
		public readonly int ScanInterval = 10;

		[Desc("Ticks after being hit by an enemy where the unit will return fire while moving (~3 sec at 25 tps).")]
		public readonly int UnderFireDuration = 75;

		[Desc("Percentage of max weapon range within which a target counts as 'close' (good shot opportunity).")]
		public readonly int CloseRangeFraction = 50;

		public override object Create(ActorInitializer init) { return new SmartMove(this); }
	}

	class SmartMove : ConditionalTrait<SmartMoveInfo>, IWrapMove, INotifyDamage
	{
		public long LastDamagedTick { get; private set; } = -1000;

		public SmartMove(SmartMoveInfo info)
			: base(info) { }

		void INotifyDamage.Damaged(Actor self, AttackInfo e)
		{
			// Only track damage from enemies
			if (e.Attacker != null && e.Attacker.IsInWorld && self.Owner.RelationshipWith(e.Attacker.Owner).HasRelationship(PlayerRelationship.Enemy))
				LastDamagedTick = self.World.WorldTick;
		}

		Activity IWrapMove.WrapMove(Activity moveInner)
		{
			return new SmartMoveActivity(moveInner, Info);
		}
	}
}
