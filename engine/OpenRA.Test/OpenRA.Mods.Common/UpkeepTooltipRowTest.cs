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
	/// <para>Pins the production tooltip's upkeep row: the formatted string, and the two YAML facts
	/// the row's shape depends on.</para>
	///
	/// <para>The costs are read from the shipped rules rather than hardcoded, so a re-price shows up
	/// here as a failure with both numbers in the message instead of silently making the fixture
	/// describe a unit that no longer exists at that price.</para>
	/// </summary>
	[TestFixture]
	public class UpkeepTooltipRowTest
	{
		// Actors that declare their own Valued.Cost, spanning the interesting formatting cases:
		// a whole number, a half, and a figure below 1 that a whole-number format would erase.
		static readonly (string Node, string File, string Expected)[] Cases =
		{
			("abrams", "vehicles-america.yaml", "12.5 cash / interval"),
			("^SF", "infantry.yaml", "3 cash / interval"),
			("^E1", "infantry.yaml", "0.25 cash / interval"),
		};

		// What ^Vehicle and ^Infantry declare. Every case above assumes this rate.
		const int GroundPermille = 5;

		static string FindRules(params string[] relative)
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var parts = new[] { dir.FullName, "mods", "ww3mod", "rules" }.Concat(relative).ToArray();
				var candidate = Path.Combine(parts);
				if (File.Exists(candidate))
					return candidate;
			}

			throw new FileNotFoundException("could not locate mods/ww3mod/rules/" + string.Join("/", relative));
		}

		/// <summary>Locally-declared Valued.Cost for a top-level node, or -1 if it declares none.</summary>
		static int DeclaredCost(string node, string file)
		{
			var actor = MiniYaml.FromFile(FindRules("ingame", file)).FirstOrDefault(n => n.Key == node);
			if (actor == null)
				return -1;

			var valued = actor.Value.Nodes.FirstOrDefault(n => n.Key == "Valued");
			var cost = valued?.Value.Nodes.FirstOrDefault(n => n.Key == "Cost");
			if (cost == null || !int.TryParse(cost.Value.Value.Trim(), out var v))
				return -1;

			return v;
		}

		/// <summary>Locally-declared InfersUpkeep.PermilleCost for a top-level node, or -1 if absent.</summary>
		static int DeclaredPermille(string node, string file)
		{
			var actor = MiniYaml.FromFile(FindRules("ingame", file)).FirstOrDefault(n => n.Key == node);
			var upkeep = actor?.Value.Nodes.FirstOrDefault(n => n.Key == "InfersUpkeep");
			var permille = upkeep?.Value.Nodes.FirstOrDefault(n => n.Key == "PermilleCost");
			if (permille == null || !int.TryParse(permille.Value.Value.Trim(), out var v))
				return -1;

			return v;
		}

		[Test]
		public void GroundTemplatesStillChargeTheRateTheseCasesAssume()
		{
			// Without this the row below can be "correct" against a rate nobody ships any more.
			Assert.That(DeclaredPermille("^Vehicle", "vehicles.yaml"), Is.EqualTo(GroundPermille),
				"^Vehicle no longer declares InfersUpkeep.PermilleCost: 5. Re-derive the expected " +
				"strings in Cases before touching them — every one of them is 0.5% of a cost.");

			// ^Infantry, NOT ^Soldier: the soldier template is the one that carries Rearmable and
			// AutoSeekSupplies, and it is the name that comes to mind first. Upkeep is a tier above it.
			Assert.That(DeclaredPermille("^Infantry", "infantry.yaml"), Is.EqualTo(GroundPermille),
				"^Infantry no longer declares InfersUpkeep.PermilleCost: 5. Same as above.");
		}

		[Test]
		public void UpkeepRowMatchesRealActorCosts()
		{
			foreach (var c in Cases)
			{
				var cost = DeclaredCost(c.Node, c.File);
				Assert.That(cost, Is.GreaterThan(0),
					$"{c.Node} declares no local Valued.Cost in {c.File} — this case is measuring " +
					"nothing. If the actor moved or now inherits its cost, pick another one.");

				var perInterval = InfersUpkeepInfo.UpkeepPerInterval(cost, 0, GroundPermille);
				Assert.That(InfersUpkeepInfo.FormatPerInterval(perInterval), Is.EqualTo(c.Expected),
					$"{c.Node} costs {cost}, so its upkeep row should read '{c.Expected}'.");
			}
		}

		[Test]
		public void SubUnitUpkeepSurvivesFormatting()
		{
			// The specific reason the row is not formatted as a whole number. The cash counter's
			// breakdown casts each group to int and skips zeroes, so a lone rifleman contributes a
			// charge that the only other upkeep surface in the game cannot show at all.
			var perInterval = InfersUpkeepInfo.UpkeepPerInterval(50, 0, GroundPermille);
			Assert.That(perInterval, Is.GreaterThan(0f),
				"50 x 0.5% should be 0.25, not 0 — integer division would have crept into the arithmetic.");

			Assert.That(InfersUpkeepInfo.FormatPerInterval(perInterval), Does.Not.StartWith("0 "),
				"A sub-1 upkeep rendered as '0 cash / interval' tells the player the unit is free to own.");
		}

		[Test]
		public void AircraftHaveNoUpkeepTraitToSpeakForThem()
		{
			// The premise of the "Upkeep: None" fallback in ProductionTooltipLogic. If a helicopter
			// ever gains InfersUpkeep, the fallback stops applying to it and this fixture should be
			// the thing that says so.
			foreach (var file in new[] { "aircraft.yaml", "aircraft-america.yaml", "aircraft-russia.yaml" })
				Assert.That(File.ReadAllText(FindRules("ingame", file)), Does.Not.Contain("InfersUpkeep"),
					$"{file} now declares InfersUpkeep. Aircraft carrying no upkeep is why the tooltip " +
					"states 'None' for them from the renderer instead of from the trait.");
		}

		[Test]
		public void NoUpkeepRowIsNotDrawnAsAPrice()
		{
			// "None" is not a quantity of cash, so it must not come back in the supply-amber CostRow
			// style that every real price uses.
			var row = InfersUpkeepInfo.NoUpkeepRow();
			Assert.That(row.Kind, Is.EqualTo(TooltipElementKind.StatRow),
				"The no-upkeep row is styled as a price. Amber is the tooltip's signal that a number " +
				"costs the player something, and 'None' contradicts it.");

			Assert.That(row.Label, Is.EqualTo("Upkeep"),
				"The absent-trait row must carry the same label as the real one, or the two actors " +
				"look like they are talking about different things.");
		}
	}
}
