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

using System.Collections.Generic;
using System.Linq;
using OpenRA.GameRules;
using OpenRA.Mods.Common.Warheads;

namespace OpenRA.Mods.Common.Traits
{
	// Type tag for armor type bits
	public class ArmorType { }

	[Desc("Used to define weapon efficiency modifiers with different percentages per Type.")]
	public class ArmorInfo : ConditionalTraitInfo, IProvideTooltipDescription
	{
		[Desc("Armor type determines what weapons can target this actor and their damage modifiers.")]
		public readonly string Type = null;

		[Desc("Armor thickness in mm.")]
		public readonly int Thickness = 0;

		[Desc("Armor thickness at { Front, Side, Rear, Top, Bottom } in percent.")]
		public readonly int[] Distribution = System.Array.Empty<int>();

		public override object Create(ActorInitializer init) { return new Armor(this); }

		/// <summary>
		/// <para>The armour row, resolved through the warhead <c>Versus</c> tables rather than read
		/// straight off <see cref="Type"/>.</para>
		///
		/// <para>THIS IS THE TRAP IN THIS FEATURE, so it is worth being explicit. Binding the row to
		/// <see cref="Type"/> would print "Kevlar" on all 28 infantry. <c>Kevlar</c> is set once, on the
		/// shared infantry template, and appears in ZERO <c>Versus</c> tables — and
		/// <c>DamageWarhead.DamageVersus</c> only applies a modifier for types a table actually names
		/// (DamageWarhead.cs:106), so a type absent from every table takes the warhead default of 100%.
		/// Infantry genuinely have no damage reduction, which is why their hand-written descriptions say
		/// "No armor" and are RIGHT. Printing "Kevlar" would replace a true statement with one implying
		/// protection the damage model does not grant.</para>
		///
		/// <para>So a type no warhead discriminates on is reported as None. A structured field is not
		/// automatically a true one.</para>
		///
		/// <para>BUT <see cref="Type"/> AND <see cref="Thickness"/> ARE TWO INDEPENDENT HALVES of the
		/// armour model, and only the first is gated on a Versus table. Thickness is read straight off
		/// the trait and compared against the warhead's Penetration on every hit
		/// (DamageWarhead.cs:237), whatever the type is called. Reporting it only when the TYPE
		/// happened to be discriminated made the row lie about gtwr — Unarmored, which no Versus table
		/// names, with a real Thickness of 25 (structures-defenses.yaml:103-105) — by printing "None"
		/// for a building whose armour subtracts damage from every weapon that fails to penetrate it.
		/// It is the only shipped actor in that position; infantry set no Thickness at all, so their
		/// "None" was true for both halves and stays true.</para>
		/// </summary>
		IEnumerable<TooltipElement> IProvideTooltipDescription.ProvideTooltipDescription(ActorInfo ai, Ruleset rules, out int priority)
		{
			priority = 200;

			// An actor may carry several Armor@ instances; only the first contributes a row, or it
			// would print several identically-labelled "Armour" rows with no way to tell them apart.
			if (ai.TraitInfos<ArmorInfo>().FirstOrDefault() != this)
				return null;

			var discriminated = !string.IsNullOrEmpty(Type) && rules.Weapons.Values
				.SelectMany(w => w.Warheads.OfType<DamageWarhead>())
				.Any(wh => wh.Versus.ContainsKey(Type));

			return new[] { TooltipElement.Stat("Armour", FormatArmour(Type, Thickness, discriminated)) };
		}

		/// <summary>
		/// The armour row's value, given a type, its thickness, and whether any warhead's
		/// <c>Versus</c> table names that type. Split out from the caller, which needs a whole loaded
		/// Ruleset, so the four combinations can be read and tested on their own.
		/// </summary>
		public static string FormatArmour(string type, int thickness, bool discriminated)
		{
			// "mm" rather than the previous bare "N thick": Thickness is documented in millimetres,
			// every other surface in the codebase writes it that way, and "700 thick" is a number a
			// player cannot place on any scale.
			var millimetres = thickness > 0 ? $"{thickness}mm" : null;

			if (discriminated)
				return millimetres != null ? $"{type} — {millimetres}" : type;

			return millimetres ?? "None";
		}
	}

	public class Armor : ConditionalTrait<ArmorInfo>
	{
		public Armor(ArmorInfo info)
			: base(info) { }
	}
}
