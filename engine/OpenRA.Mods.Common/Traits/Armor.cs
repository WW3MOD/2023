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
	// Type tag for armor type bits
	public class ArmorType { }

	[Desc("Used to define weapon efficiency modifiers with different percentages per Type.")]
	public class ArmorInfo : ConditionalTraitInfo
	{
		[Desc("Armor type determines what weapons can target this actor and their damage modifiers.")]
		public readonly string Type = null;

		[Desc("Armor thickness in mm.")]
		public readonly int Thickness = 0;

		[Desc("Armor thickness at { Front, Side, Rear, Top, Bottom } in percent.")]
		public readonly int[] Distribution = System.Array.Empty<int>();

		public override object Create(ActorInitializer init) { return new Armor(this); }
	}

	public class Armor : ConditionalTrait<ArmorInfo>
	{
		public Armor(ArmorInfo info)
			: base(info) { }
	}
}
