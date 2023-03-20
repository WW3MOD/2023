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

using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public class ModifySequenceInfo : ConditionalTraitInfo
	{
		[SequenceReference(prefix: true)]
		[Desc("Sequence prefix to apply while trait is active.")]
		public readonly string SequencePrefix = "active-";

		public override object Create(ActorInitializer init) { return new ModifySequence(init, this); }

		public override void RulesetLoaded(Ruleset rules, ActorInfo ai)
		{
			base.RulesetLoaded(rules, ai);
		}
	}

	public class ModifySequence : ConditionalTrait<ConditionalTraitInfo>, ISync, IRenderInfantrySequenceModifier
	{
		readonly ModifySequenceInfo info;

		readonly Actor self;

		bool isEnabled = false;

		bool IRenderInfantrySequenceModifier.IsModifyingSequence { get { return (isEnabled); } }
		string IRenderInfantrySequenceModifier.SequencePrefix { get { return info.SequencePrefix; } }

		public ModifySequence(ActorInitializer init, ModifySequenceInfo info)
			: base(info)
		{
			self = init.Self;
			this.info = info;
		}

		protected override void TraitDisabled(Actor self)
		{
			isEnabled = false;
		}

		protected override void TraitEnabled(Actor self)
		{
			isEnabled = true;
		}
	}
}
