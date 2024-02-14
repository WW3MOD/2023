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

using OpenRA.Mods.Common.Traits;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Cnc.Traits
{
	class MineInfo : TraitInfo
	{
		public readonly BitSet<PassClass> PassClasses = default;
		public readonly BitSet<PassClass> DetonateClasses = default;
		public readonly bool AvoidFriendly = true;
		public readonly bool BlockFriendly = true;

		public override object Create(ActorInitializer init) { return new Mine(this); }
	}

	class Mine : IPassable, INotifyBeingPassed
	{
		readonly MineInfo info;

		public Mine(MineInfo info)
		{
			this.info = info;
		}

		void INotifyBeingPassed.WarnPass(Actor self, Actor passer, BitSet<PassClass> passClasses) { }

		void INotifyBeingPassed.OnBeingPassed(Actor self, Actor passer, BitSet<PassClass> passClasses)
		{

		}

		void INotifyBeingPassed.OnBeingCrushed(Actor self, Actor passer, BitSet<PassClass> passClasses)
		{
			if (!info.PassClasses.Overlaps(passClasses))
				return;

			if (passer.Info.HasTraitInfo<MineImmuneInfo>() || (self.Owner.RelationshipWith(passer.Owner) == PlayerRelationship.Ally && info.AvoidFriendly))
				return;

			var mobile = passer.TraitOrDefault<Mobile>();
			if (mobile != null && !info.DetonateClasses.Overlaps(mobile.Info.LocomotorInfo.Crushes))
				return;

			self.Kill(passer, mobile != null ? mobile.Info.LocomotorInfo.CrushDamageTypes : default);
		}


		bool IPassable.PassableBy(Actor self, Actor passer, BitSet<PassClass> passClasses)
		{
			if (info.BlockFriendly && !passer.Info.HasTraitInfo<MineImmuneInfo>() && self.Owner.RelationshipWith(passer.Owner) == PlayerRelationship.Ally)
				return false;

			return info.PassClasses.Overlaps(passClasses);
		}

		LongBitSet<PlayerBitMask> IPassable.PassableBy(Actor self, BitSet<PassClass> passClasses)
		{
			if (!info.PassClasses.Overlaps(passClasses))
				return self.World.NoPlayersMask;

			// Friendly units should move around!
			return info.BlockFriendly ? self.World.AllPlayersMask.Except(self.Owner.AlliedPlayersMask) : self.World.AllPlayersMask;
		}
	}

	[Desc("Tag trait for stuff that should not trigger mines.")]
	class MineImmuneInfo : TraitInfo<MineImmune> { }
	class MineImmune { }
}
