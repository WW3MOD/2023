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

using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public sealed class MineInfo : TraitInfo
	{
		public readonly BitSet<PassClass> CrushClasses = default;
		public readonly bool AvoidFriendly = true;
		public readonly bool BlockFriendly = true;
		public readonly BitSet<PassClass> DetonateClasses = default;

		public override object Create(ActorInitializer init) { return new Mine(this); }
	}

	public sealed class Mine : IPassable, INotifyBeingPassed
	{
		readonly MineInfo info;

		public Mine(MineInfo info)
		{
			this.info = info;
		}

		void INotifyBeingPassed.WarnPass(Actor self, Actor passer, BitSet<PassClass> passClasses) { }

		void INotifyBeingPassed.OnBeingPassed(Actor self, Actor passer, BitSet<PassClass> passClasses)
		{
			if (!info.CrushClasses.Overlaps(passClasses))
				return;

			if (passer.Info.HasTraitInfo<MineImmuneInfo>() || (self.Owner.RelationshipWith(passer.Owner) == PlayerRelationship.Ally && info.AvoidFriendly))
				return;

			var mobile = passer.TraitOrDefault<Mobile>();
			if (mobile != null && !info.DetonateClasses.Overlaps(mobile.Info.LocomotorInfo.Crushes))
				return;

			self.Kill(passer, mobile != null ? mobile.Info.LocomotorInfo.CrushDamageTypes : default);
		}

		void INotifyBeingPassed.OnBeingCrushed(Actor self, Actor passer, BitSet<PassClass> passClasses) { }

		bool IPassable.PassableBy(Actor self, Actor passer, BitSet<PassClass> passClasses)
		{
			if (info.BlockFriendly && !passer.Info.HasTraitInfo<MineImmuneInfo>() && self.Owner.RelationshipWith(passer.Owner) == PlayerRelationship.Ally)
				return false;

			return info.CrushClasses.Overlaps(passClasses);
		}

		LongBitSet<PlayerBitMask> IPassable.PassableBy(Actor self, BitSet<PassClass> passClasses)
		{
			if (!info.CrushClasses.Overlaps(passClasses))
				return self.World.NoPlayersMask;

			// Friendly units should move around!
			return info.BlockFriendly ? self.World.AllPlayersMask.Except(self.Owner.AlliedPlayersMask) : self.World.AllPlayersMask;
		}
	}

	[Desc("Tag trait for stuff that should not trigger mines.")]
	public sealed class MineImmuneInfo : TraitInfo<MineImmune> { }
	public sealed class MineImmune { }
}
