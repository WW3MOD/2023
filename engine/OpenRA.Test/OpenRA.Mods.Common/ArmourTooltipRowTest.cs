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

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// <para>The production tooltip's armour row, and the two YAML facts its shape depends on.</para>
	///
	/// <para>Armour has two independent halves. <c>Type</c> only does anything if some warhead's
	/// <c>Versus</c> table names it; <c>Thickness</c> is compared against Penetration on every hit
	/// regardless. The row has to be able to say either, both, or neither.</para>
	/// </summary>
	[TestFixture]
	public class ArmourTooltipRowTest
	{
		static string FindRules(params string[] relative)
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var parts = new[] { dir.FullName, "mods", "ww3mod", "rules" }.Concat(relative).ToArray();
				var candidate = Path.Combine(parts);
				if (File.Exists(candidate) || Directory.Exists(candidate))
					return candidate;
			}

			throw new FileNotFoundException("could not locate mods/ww3mod/rules/" + string.Join("/", relative));
		}

		[Test]
		public void ADiscriminatedTypeWithThicknessStatesBoth()
		{
			Assert.That(ArmorInfo.FormatArmour("Heavy", 700, true), Is.EqualTo("Heavy — 700mm"));
		}

		[Test]
		public void ThicknessCarriesItsUnit()
		{
			// This read "700 thick" before, which names no scale at all. The user asked whether
			// vehicles state a steel equivalent in mm; they did state the figure, just not the unit.
			Assert.That(ArmorInfo.FormatArmour("Heavy", 700, true), Does.Contain("700mm"));
			Assert.That(ArmorInfo.FormatArmour("Heavy", 700, true), Does.Not.Contain("thick"));
		}

		[Test]
		public void ThicknessIsStatedEvenWhenNoWarheadKnowsTheType()
		{
			// gtwr's case. Its type is Unarmored, which appears in no Versus table, but its 25mm is
			// subtracted from every warhead that fails to penetrate it. Reporting "None" here told
			// the player a building with real armour had none.
			Assert.That(ArmorInfo.FormatArmour("Unarmored", 25, false), Is.EqualTo("25mm"),
				"Thickness does not depend on the type being discriminated, so the row must not " +
				"suppress it when the type is not.");
		}

		[Test]
		public void NoneMeansNeitherHalfIsPresent()
		{
			// Infantry. Kevlar is in no Versus table AND ^Soldier sets no Thickness, so both halves
			// really are absent and "None" is a true statement rather than a fallback.
			Assert.That(ArmorInfo.FormatArmour("Kevlar", 0, false), Is.EqualTo("None"));
		}

		[Test]
		public void ADiscriminatedTypeWithNoThicknessStatesOnlyTheType()
		{
			Assert.That(ArmorInfo.FormatArmour("Light", 0, true), Is.EqualTo("Light"));
		}

		[Test]
		public void NoWeaponDiscriminatesOnKevlar()
		{
			// The premise of the infantry answer. If a Versus: Kevlar entry is ever authored, soldiers
			// start having real armour and their row should stop saying None — this is the thing that
			// should fail when that happens.
			// COMMENTS ARE STRIPPED FIRST, and that is a correction rather than a loosening. The
			// premise above is about an authored `Versus: Kevlar` ROW; a raw whole-file substring
			// search also fires on prose. It did: MOPPenetration's header enumerates the mod's nine
			// armour types to explain why the GBU-57's structure/unit split is a target-type filter
			// and NOT a Versus table — precisely because a type left out of a table silently takes
			// 100%, which is the same finding this fixture exists to protect. A test that forbids
			// naming the trap in a comment pushes the next author toward writing a vaguer comment,
			// not a safer weapon. A real row cannot hide in a comment, so nothing detectable is lost.
			foreach (var path in Directory.EnumerateFiles(FindRules("weapons"), "*.yaml"))
			{
				var rules = string.Join("\n", File.ReadAllLines(path).Select(l => l.Split('#')[0]));
				Assert.That(rules, Does.Not.Contain("Kevlar"),
					$"{Path.GetFileName(path)} now mentions Kevlar OUTSIDE A COMMENT. Infantry carry " +
					"Armor.Type: Kevlar (infantry.yaml, ^Soldier) and the tooltip reports None only " +
					"because no warhead discriminates on it. Re-check the armour row against this file.");
			}
		}

		[Test]
		public void SoldiersStillCarryTheKevlarTypeThisAnswerIsAbout()
		{
			// Without this, the test above passes just as well after someone deletes the trait, and
			// the "infantry are Kevlar but it does nothing" finding silently stops being about
			// anything.
			var soldier = MiniYaml.FromFile(FindRules("ingame", "infantry.yaml"))
				.FirstOrDefault(n => n.Key == "^Soldier");

			Assert.That(soldier, Is.Not.Null, "^Soldier is gone from infantry.yaml.");

			var armor = soldier.Value.Nodes.FirstOrDefault(n => n.Key == "Armor");
			var type = armor?.Value.Nodes.FirstOrDefault(n => n.Key == "Type");

			Assert.That(type?.Value.Value.Trim(), Is.EqualTo("Kevlar"),
				"^Soldier no longer declares Armor.Type: Kevlar. Every soldier that inherits it — " +
				"which is all of them except the pilot and the technician — resolved to Kevlar, and " +
				"that is why the Team Leader's None is a rendering decision and not a missing trait.");
		}
	}
}
