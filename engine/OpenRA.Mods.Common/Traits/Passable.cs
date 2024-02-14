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

/* Could add Crush Damage, and give that much damage instead of just killing instantly */

using System;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("This actor is passable.")]
	public class PassableInfo : ConditionalTraitInfo
	{

		[Desc("Which crush classes does this actor belong to.")]
		public readonly BitSet<PassClass> PassClasses = new BitSet<PassClass>("none");

		[Desc("Player Relationship to be able too pass over (Crush) this Passable.")]
		public readonly PlayerRelationship PassedByRelationships = PlayerRelationship.Ally | PlayerRelationship.Neutral | PlayerRelationship.Enemy;

		[Desc("Relationships where the crush action kills this Passable.")]
		public readonly PlayerRelationship CrushedByRelationships = PlayerRelationship.Ally | PlayerRelationship.Neutral | PlayerRelationship.Enemy;

		[Desc("Probability of mobile actors noticing and evading a crush attempt.")]
		public readonly int WarnProbability = 100;

		[Desc("Sound to play when being passed (crushed).")]
		public readonly string CrushSound = null;

		[Desc("Sound to play when being crushed to death.")]
		public readonly string KillSound = null;

		public override object Create(ActorInitializer init) { return new Passable(init.Self, this); }
	}

	public class Passable : ConditionalTrait<PassableInfo>, IPassable, INotifyBeingPassed
	{
		readonly Actor self;

		public Passable(Actor self, PassableInfo info)
			: base(info)
		{
			this.self = self;
		}

		void INotifyBeingPassed.WarnPass(Actor self, Actor passer, BitSet<PassClass> passClasses)
		{
			if (!PassableInner(self, passer, passClasses))
				return;

			var mobile = self.TraitOrDefault<Mobile>();
			if (mobile != null && self.World.SharedRandom.Next(100) <= Info.WarnProbability)
				mobile.Nudge(passer);
		}

		void INotifyBeingPassed.OnBeingPassed(Actor self, Actor passer, BitSet<PassClass> passClasses)
		{
			if (!PassableInner(self, passer, passClasses))
				return;

			Game.Sound.Play(SoundType.World, Info.CrushSound, passer.CenterPosition);

			var passerMobile = passer.TraitOrDefault<Mobile>();

			if (Info.CrushedByRelationships.HasRelationship(self.Owner.RelationshipWith(passer.Owner))
				&& passerMobile.Info.LocomotorInfo.Crushes.Overlaps(passClasses)
				&& self.IsAtGroundLevel())
			{
				foreach (var notifyCrushed in self.TraitsImplementing<INotifyBeingPassed>())
					notifyCrushed.OnBeingCrushed(self, passer, passerMobile.Info.LocomotorInfo.Crushes);

				Game.Sound.Play(SoundType.World, Info.KillSound, passer.CenterPosition);
				self.Kill(passer, passerMobile != null ? passerMobile.Info.LocomotorInfo.CrushDamageTypes : default);
			}
		}

		void INotifyBeingPassed.OnBeingCrushed(Actor self, Actor passer, BitSet<PassClass> passClasses)
		{
		}

		bool IPassable.PassableBy(Actor self, Actor passer, BitSet<PassClass> passClasses)
		{
			return PassableInner(self, passer, passClasses);
		}

		LongBitSet<PlayerBitMask> IPassable.PassableBy(Actor self, BitSet<PassClass> passClasses)
		{
			if (IsTraitDisabled || !Info.PassClasses.Overlaps(passClasses))
				return self.World.NoPlayersMask;

			return Info.PassedByRelationships.HasRelationship(PlayerRelationship.Ally) ? self.World.AllPlayersMask : self.World.AllPlayersMask.Except(self.Owner.AlliedPlayersMask);
		}

		bool PassableInner(Actor self, Actor passer, BitSet<PassClass> passClasses)
		{
			if (IsTraitDisabled)
				return false;

			var shouldCrush = Info.PassedByRelationships.HasRelationship(self.Owner.RelationshipWith(passer.Owner));

			var relationship = self.Owner.RelationshipWith(passer.Owner);
			if (!Info.PassedByRelationships.HasRelationship(relationship))
				return false;

			return Info.PassClasses.Overlaps(passClasses);
		}

		protected override void TraitEnabled(Actor self)
		{
			self.World.ActorMap.UpdateOccupiedCells(self.OccupiesSpace);
		}

		protected override void TraitDisabled(Actor self)
		{
			self.World.ActorMap.UpdateOccupiedCells(self.OccupiesSpace);
		}
	}
}
