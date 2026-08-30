#region Copyright & License Information
/*
 * WW3MOD phantom-movement tests: the depot dock cell must STAY unstayable.
 *
 * The player-visible symptom is "a vehicle ordered to a position arrives, then backs up a bit, like
 * it got a hidden extra order". There IS a hidden extra order. Mobile.OnBecomingIdle (Mobile.cs:980)
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
 * destination through Mobile.NearestMoveableCell, which filters on CanStayInCell (Mobile.cs:853-858), and
 * every give-up branch in Move.PopPath re-checks it (Move.cs:268). The unguarded path is service docking:
 * Resupply.cs:274 uses move.MoveOntoTarget -> MoveOntoAndTurn : MoveOnto : MoveAdjacentTo. The base
 * MoveAdjacentTo.CalculatePathToTarget picks its candidates through `CanStayInCell(cell) &&
 * CanEnterCell(cell)` (MoveAdjacentTo.cs:129) — but MoveOnto OVERRIDES that method and substitutes a
 * single unfiltered cell, the host centre (MoveOnto.cs:41-58). So the docking activity overrides away the
 * very stayability filter its own base class applies, and when servicing finishes with nothing queued the
 * unit goes idle ON the host and is bounced off. That override is precisely what Mobile.cs:944's "activities
 * should be making sure that this can't happen in the first place!" is complaining about.
 *
 * The tempting conclusion — "so the dock cell should be stayable" — is WRONG, and this fixture exists to
 * stop it. See the comment on the assertion below: the bounce is what keeps the dock free for a docking
 * system with no queue and no reservation, and removing it strands the next customer at the door. The real
 * defect was that the correction was INVISIBLE, fixed on wt/heal-legibility by painting it in
 * AutomaticOrder.LineColor; test-depot-vacate-phantom proves both halves in a running game.
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

		// This assertion is INVERTED from the one first written here, and the inversion is the finding.
		//
		// The obvious reading of the bug is "a serviced unit should never be parked on a cell it cannot
		// stay in", and the obvious fix is to make the dock cell stayable. Both are wrong. The bounce is
		// LOAD-BEARING: it is what keeps the dock free by construction for a docking system that has no
		// queue and no reservation — the LC carries no Reservable, unlike HPAD (structures.yaml:513) and
		// AFLD (:588). Make the dock stayable and the serviced vehicle parks there forever, while the next
		// one waits forever: MoveOnto.CalculatePathToTarget returns NoPath and WAITS rather than stacking
		// when the target cell is occupied (MoveOnto.cs:41-58), and Resupply's isCloseEnough for the LC is
		// WDist.Zero (no RearmsUnits trait supplies a CloseEnough), so exact coincidence with the building
		// centre is required and nothing rescues it. That trades a cosmetic bug for a hard stall.
		//
		// So this pins the '+' rather than forbidding it. It fails if someone "fixes" the footprint, and
		// the failure text is the argument for why they should not — the real bug was legibility, fixed by
		// painting the correction in AutomaticOrder.LineColor, and test-depot-vacate-phantom is what
		// proves both halves in a running game.
		[Test]
		public void ServiceHostDockCellsStayTransitOnlyBecauseTheVacateDependsOnIt()
		{
			var dockCellsAreTransitOnly = Structures()
				.Where(IsServiceHost)
				.Select(a => (a.Key, Print: Footprint(a)))
				.Where(x => x.Print != null && x.Print.Contains('+'))
				.Select(x => x.Key)
				.ToArray();

			Assert.That(dockCellsAreTransitOnly, Contains.Item("LOGISTICSCENTER"),
				"LOGISTICSCENTER's footprint no longer declares transit-only '+' cells. If that was deliberate, " +
				"it just broke depot queueing rather than fixing the phantom-move report: a serviced vehicle " +
				"now parks on the dock forever and the next one stalls at the door forever, because " +
				"MoveOnto.CalculatePathToTarget waits instead of stacking (MoveOnto.cs:41-58) and the LC's " +
				"isCloseEnough is WDist.Zero so there is no near-enough fallback. The unordered vacate is what " +
				"keeps the dock free; the fix for it being invisible was AutomaticOrder.LineColor, not this. " +
				"Making the dock stayable is only safe alongside a real reservation system (Reservable / " +
				"DockHost), which this building does not have.");
		}
	}
}
