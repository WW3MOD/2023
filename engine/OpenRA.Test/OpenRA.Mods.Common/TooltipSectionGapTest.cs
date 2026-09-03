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

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;
using OpenRA.Mods.Common.Widgets.Logic;

namespace OpenRA.Test
{
	/// <summary>
	/// <para>Pins where the production tooltip breaks its rows apart.</para>
	///
	/// <para>The symptom this exists for: with every contributed row stacked at a uniform pitch, a
	/// Team Leader's ARMOUR, HEALTH and SPEED sat flush under the GRENADE LAUNCHER subhead and read
	/// as more of the launcher's stats. Only the two rows above them were.</para>
	/// </summary>
	[TestFixture]
	public class TooltipSectionGapTest
	{
		const int Weapons = AmmoPoolInfo.TooltipPriority;

		static (int, TooltipElement) Weapon(string name)
		{
			return (Weapons, TooltipElement.Subhead(name));
		}

		static (int, TooltipElement) Ammo(string rounds)
		{
			return (Weapons, TooltipElement.Stat("Ammo", rounds));
		}

		static IEnumerable<TooltipElementKind> Kinds(IEnumerable<TooltipElement> elements)
		{
			return elements.Select(e => e.Kind);
		}

		[Test]
		public void EachWeaponAfterTheFirstIsHeldOffTheOneAbove()
		{
			var laid = ProductionTooltipLogic.WithSectionGaps(new[]
			{
				Weapon("7.62mm DMR"), Ammo("100 rounds"),
				Weapon("Grenade Launcher"), Ammo("6 rounds"),
			});

			Assert.That(Kinds(laid), Is.EqualTo(new[]
			{
				TooltipElementKind.Subhead, TooltipElementKind.StatRow,
				TooltipElementKind.SubsectionGap,
				TooltipElementKind.Subhead, TooltipElementKind.StatRow,
			}), "The gap belongs before the SECOND weapon's subhead and nowhere else. A leading gap " +
				"pushes the whole block off the rule that already separates it from the description.");
		}

		[Test]
		public void TheActorsOwnStatsAreCutOffFromTheWeaponsWithTheWiderGap()
		{
			var laid = ProductionTooltipLogic.WithSectionGaps(new[]
			{
				Weapon("Grenade Launcher"), Ammo("6 rounds"),
				(200, TooltipElement.Stat("Armour", "None")),
				(210, TooltipElement.Stat("Health", "200")),
			});

			Assert.That(Kinds(laid), Is.EqualTo(new[]
			{
				TooltipElementKind.Subhead, TooltipElementKind.StatRow,
				TooltipElementKind.SectionGap,
				TooltipElementKind.StatRow, TooltipElementKind.StatRow,
			}), "This is the reported bug: without a break here, ARMOUR reads as a property of the " +
				"grenade launcher rather than of the soldier carrying it.");

			Assert.That(Kinds(laid).Count(k => k == TooltipElementKind.SectionGap), Is.EqualTo(1),
				"Only the weapons/actor boundary is a block boundary. Armour, health and speed are " +
				"different priorities but one block, and must not each open a new one.");
		}

		[Test]
		public void AnActorWithNoPricedAmmoOpensStraightIntoItsStats()
		{
			// A transport helicopter, a supply truck, a medic: no AmmoPool contributes, so the first
			// contributed row is Armour. Keying the wide gap on "the first non-weapon row" instead of
			// on leaving the weapons band would indent this actor's whole block by 12px for nothing.
			var laid = ProductionTooltipLogic.WithSectionGaps(new[]
			{
				(200, TooltipElement.Stat("Armour", "Light")),
				(400, TooltipElement.Stat("Carries", "36 infantry")),
			});

			Assert.That(Kinds(laid), Is.EqualTo(new[]
			{
				TooltipElementKind.StatRow, TooltipElementKind.StatRow,
			}), "No weapon rows means no boundary to mark.");
		}

		[Test]
		public void NothingIsAddedAfterTheLastRow()
		{
			var laid = ProductionTooltipLogic.WithSectionGaps(new[]
			{
				Weapon("Flamespray"), Ammo("50 rounds"),
			});

			Assert.That(laid.Last().Kind, Is.Not.EqualTo(TooltipElementKind.SectionGap),
				"A trailing gap is invisible but real: it lands inside the panel's measured height " +
				"and shows up as an uneven bottom margin against every other tooltip.");
			Assert.That(laid.Count, Is.EqualTo(2));
		}

		[Test]
		public void EveryContributedRowSurvivesInOrder()
		{
			var rows = new[]
			{
				Weapon("A"), Ammo("1 round"),
				Weapon("B"), Ammo("2 rounds"),
				(200, TooltipElement.Stat("Armour", "Heavy")),
				(500, TooltipElement.Cost("Call-in", "200 cash")),
			};

			var laid = ProductionTooltipLogic.WithSectionGaps(rows);
			var kept = laid.Where(e => e.Kind != TooltipElementKind.SubsectionGap
				&& e.Kind != TooltipElementKind.SectionGap);

			Assert.That(kept, Is.EqualTo(rows.Select(r => r.Item2)),
				"Spacing must be purely additive. A row dropped or reordered here is a fact the " +
				"player silently stops being told.");
		}

		[Test]
		public void GapsCarryNoContent()
		{
			// They are laid out by height alone; a label on one would never be drawn, and would make
			// a gap look like something an author could write text into.
			foreach (var gap in new[] { TooltipElement.SubsectionGap(), TooltipElement.SectionGap() })
			{
				Assert.That(gap.Label, Is.Null);
				Assert.That(gap.Value, Is.Null);
			}
		}
	}
}
