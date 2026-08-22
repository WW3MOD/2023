#region Copyright & License Information
/*
 * WW3MOD phantom-movement tests: a serviced unit must not be parked on a cell it cannot stay in.
 *
 * The player-visible symptom is "a vehicle ordered to a position arrives, then backs up a bit, like
 * it got a hidden extra order". There IS a hidden extra order. Mobile.OnBecomingIdle (Mobile.cs:945)
 * re-orders any unit that falls idle on a cell its locomotor may pass through but not stop on:
 *
 *     if (!Locomotor.CanStayInCell(self.Location))
 *         self.QueueActivity(MoveTo(self.Location, evaluateNearestMovableCell: true));
 *
 * Locomotor.CanStayInCell (Locomotor.cs:373) is false for exactly one reason: CellFlag.HasTransitOnlyActor,
 * set (Locomotor.cs:566-569) when a Building's TransitOnlyCells() covers the cell. Those are the footprint
 * cells written '+' (FootprintCellType.OccupiedPassableTransitOnly, Building.cs:26) — asserted below rather
 * than assumed, so this fixture cannot drift if the enum is re-lettered.
 *
 * Ordinary move orders cannot strand a unit this way: Move.OnFirstRun (Move.cs:139-143) pre-corrects the
 * destination through Mobile.NearestMoveableCell, which filters on CanStayInCell (Mobile.cs:844-852), and
 * every give-up branch in Move.PopPath re-checks it (Move.cs:268). The unguarded path is service docking:
 * Resupply.cs:274 uses move.MoveOntoTarget -> MoveOntoAndTurn : MoveOnto : MoveAdjacentTo. The base
 * MoveAdjacentTo.CalculatePathToTarget picks its candidates through `CanStayInCell(cell) &&
 * CanEnterCell(cell)` (MoveAdjacentTo.cs:129) — but MoveOnto OVERRIDES that method and substitutes a
 * single unfiltered cell, the host centre (MoveOnto.cs:41-58). So the docking activity overrides away the
 * very stayability filter its own base class applies, and when servicing finishes with nothing queued the
 * unit goes idle ON the host and is bounced off. That override is precisely what Mobile.cs:944's "activities
 * should be making sure that this can't happen in the first place!" is complaining about.
 *
 * So the invariant is: a building that ground units are driven ONTO to be serviced must not declare its
 * footprint transit-only. This is a data assertion because the defect is in the data — the engine paths
 * above are upstream OpenRA and behave as designed.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class TransitOnlyServiceHostTest
	{
		// Traits that make a building a destination a unit is driven onto and serviced at.
		static readonly string[] ServiceHostTraits = { "RepairsUnits", "SupplyProvider" };

		static string FindRules(string file)
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
			{
				var candidate = Path.Combine(dir.FullName, "mods", "ww3mod", "rules", "ingame", file);
				if (File.Exists(candidate))
					return candidate;
			}

			throw new FileNotFoundException($"could not locate mods/ww3mod/rules/ingame/{file}");
		}

		static string Footprint(MiniYamlNode actor)
		{
			return actor.Value.Nodes
				.FirstOrDefault(n => n.Key == "Building")?.Value.Nodes
				.FirstOrDefault(n => n.Key == "Footprint")?.Value.Value;
		}

		static bool IsServiceHost(MiniYamlNode actor)
		{
			return actor.Value.Nodes.Any(n => ServiceHostTraits.Contains(n.Key));
		}

		static List<MiniYamlNode> Structures()
		{
			return MiniYaml.FromFile(FindRules("structures.yaml"));
		}

		[Test]
		public void PlusIsStillTheTransitOnlyFootprintCharacter()
		{
			// Guard the guard. Every other assertion in this fixture greps for '+' in a footprint
			// string; if the engine ever re-letters the enum, those would pass vacuously.
			Assert.That((char)FootprintCellType.OccupiedPassableTransitOnly, Is.EqualTo('+'),
				"the transit-only footprint character changed — the '+' scans in this fixture are now blind");
		}

		[Test]
		public void TheScanFindsTheServiceHosts()
		{
			// Non-vacuity. If the parse shape or the file location changes, the invariant test below
			// would find nothing to check and report success while checking nothing.
			var hosts = Structures().Where(IsServiceHost).Select(a => a.Key).ToArray();

			Assert.That(hosts, Is.Not.Empty,
				"found no RepairsUnits/SupplyProvider building in structures.yaml — the scan is broken, not the data");
			Assert.That(hosts, Contains.Item("LOGISTICSCENTER"),
				"LOGISTICSCENTER is the ground-vehicle repair and supply depot; if it is no longer found, this fixture is looking in the wrong place");
		}

		// EXPLICIT because this currently FAILS: it is the proof of an unfixed defect, not a regression gate
		// yet. Both fixes are gameplay-data decisions that were not mine to take — '+' -> '=' makes the depot
		// occupy nothing at all (it stops blocking, and placement/BuildingInfluence move with it), '+' -> 'x'
		// stops the docking drive-on that repair depends on. Delete this attribute as part of whichever fix
		// lands; the assertion is already the right gate for it.
		// Run it with:
		//   dotnet test engine/OpenRA.Test/OpenRA.Test.csproj -c Release \
		//     --filter "FullyQualifiedName~AServicedUnitIsNeverParkedOnACellItCannotStayIn"
		[Test, Explicit]
		public void AServicedUnitIsNeverParkedOnACellItCannotStayIn()
		{
			var offenders = Structures()
				.Where(IsServiceHost)
				.Select(a => (a.Key, Print: Footprint(a)))
				.Where(x => x.Print != null && x.Print.Contains('+'))
				.Select(x => $"{x.Key} (Footprint: {x.Print})")
				.ToArray();

			Assert.That(offenders, Is.Empty,
				"these buildings service units that are driven onto them (Resupply.cs:274 -> MoveOnto, whose " +
				"CalculatePathToTarget override drops the base class's CanStayInCell filter), but declare " +
				"transit-only '+' footprint cells. A unit that " +
				"finishes servicing there falls idle on a cell it may not stop on, and Mobile.OnBecomingIdle " +
				"(Mobile.cs:945) issues an unordered move to shove it off — the 'it backs up by itself' report. " +
				"Offenders: " + string.Join(", ", offenders));
		}
	}
}
