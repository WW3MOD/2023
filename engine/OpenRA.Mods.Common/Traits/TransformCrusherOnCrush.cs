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

using OpenRA.Mods.Common.Activities;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	[Desc("Put this on the actor that gets crushed to replace the passer with a new actor.")]
	public class TransformCrusherOnBeingPassedInfo : TraitInfo
	{
		[ActorReference]
		[FieldLoader.Require]
		public readonly string IntoActor = null;

		public readonly bool SkipMakeAnims = true;

		public readonly BitSet<PassClass> PassClasses = default;

		public override object Create(ActorInitializer init) { return new TransformCrusherOnBeingPassed(init, this); }
	}

	public class TransformCrusherOnBeingPassed : INotifyBeingPassed
	{
		readonly TransformCrusherOnBeingPassedInfo info;
		readonly string faction;

		public TransformCrusherOnBeingPassed(ActorInitializer init, TransformCrusherOnBeingPassedInfo info)
		{
			this.info = info;
			faction = init.GetValue<FactionInit, string>(init.Self.Owner.Faction.InternalName);
		}

		void INotifyBeingPassed.WarnPass(Actor self, Actor passer, BitSet<PassClass> passClasses) { }

		void INotifyBeingPassed.OnBeingPassed(Actor self, Actor passer, BitSet<PassClass> passClasses)
		{
			if (!info.PassClasses.Overlaps(passClasses))
				return;

			var facing = passer.TraitOrDefault<IFacing>();
			var transform = new Transform(info.IntoActor) { Faction = faction };
			if (facing != null)
				transform.Facing = facing.Facing;

			transform.SkipMakeAnims = info.SkipMakeAnims;
			passer.QueueActivity(false, transform);
		}

		void INotifyBeingPassed.OnBeingCrushed(Actor self, Actor passer, BitSet<PassClass> passClasses)
		{
		}
	}
}
