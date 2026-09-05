#region Copyright & License Information
/*
 * WW3MOD — pins the two halves of the support-power aim-point snap that can be tested without a
 * World: the candidate ranking, and the damage arithmetic that makes the snap worth doing.
 *
 * WHAT LIVES HERE AND WHAT CANNOT. SupportPowerAimPoint.Resolve needs a World, an ActorMap and a
 * placed building, so the resolution itself is a scenario assertion (test-power-aims-at-center),
 * not a unit test. What IS testable here is the ranking predicate that decides which of several
 * actors on one cell wins, and — more valuable — the shipped RectangleShape arithmetic that turns a
 * corner-cell click into a third of the damage. The second fixture is the reason the feature was
 * filed as a bug fix rather than a convenience.
 */
#endregion

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using OpenRA.Mods.Common.HitShapes;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class SupportPowerAimPointTest
	{
		// LOGISTICSCENTER as shipped (mods/ww3mod/rules/ingame/structures.yaml:400-410):
		// HitShape Rectangle TopLeft -1536,-1536 / BottomRight 1536,1536, Building Dimensions 3,3.
		const int HitShapeHalfExtent = 1536;

		// BuildingInfo.CenterOffset for a 3x3 is (CenterOfCell(3,3) - CenterOfCell(1,1)) / 2 =
		// (1024, 1024) — a full cell diagonally (Building.cs:207-210). That is exactly the offset
		// from a corner footprint cell's centre to the building's own centre.
		const int CornerCellOffset = 1024;

		// IskanderExplosion Warhead@Target Damage, the Kinzhal's payload
		// (mods/ww3mod/rules/weapons/weapons-explosions.yaml:522-524).
		const int IskanderTargetDamage = 54000;

		// LOGISTICSCENTER Health.HP (structures.yaml:448-449).
		const int LogisticsCenterHp = 60000;

		static RectangleShape LogisticsCenterShape()
		{
			var shape = new RectangleShape(
				new int2(-HitShapeHalfExtent, -HitShapeHalfExtent),
				new int2(HitShapeHalfExtent, HitShapeHalfExtent));
			shape.Initialize();
			return shape;
		}

		static int ProximityPercentAt(WVec offsetFromCenter)
		{
			return LogisticsCenterShape().CenterProximityPercent(
				new WPos(offsetFromCenter.X, offsetFromCenter.Y, 0), WPos.Zero, WRot.None);
		}

		[TestCase(TestName = "A hit on the actor's centre scales TargetDamage to full")]
		public void CenterHitIsFullDamage()
		{
			Assert.That(ProximityPercentAt(WVec.Zero), Is.EqualTo(100),
				"The aim point the snap produces is the actor's own CenterPosition, which is the " +
				"origin CenterProximityPercent measures from — so it must read 100.");
		}

		[TestCase(TestName = "A hit on a corner footprint cell scales TargetDamage to a third")]
		public void CornerCellHitIsAThird()
		{
			// half-diagonal = |(1536, 1536)| = 2172; offset = |(1024, 1024)| = 1448;
			// 100 * (2172 - 1448) / 2172 = 33.
			var proximity = ProximityPercentAt(new WVec(CornerCellOffset, CornerCellOffset, 0));
			Assert.That(proximity, Is.EqualTo(33));

			// This is the defect in one line: the SAME warhead, on the SAME building, delivers
			// either a kill or well under half depending only on which of nine cells was clicked.
			var cornerDamage = IskanderTargetDamage * proximity / 100;
			Assert.That(IskanderTargetDamage, Is.GreaterThan(LogisticsCenterHp - 7000),
				"A centred Kinzhal is meant to be within its supporting warheads' reach of a kill.");
			Assert.That(cornerDamage, Is.LessThan(LogisticsCenterHp / 2),
				"A corner-cell Kinzhal is not.");
		}

		[TestCase(TestName = "A hit on a mid-edge footprint cell is between the two")]
		public void EdgeCellHitIsBetween()
		{
			// Worth pinning because it disproves the tempting summary "clicking a building's own
			// cell gives 33%". Only the four CORNER cells do; the four edge-midpoints give 52.
			Assert.That(ProximityPercentAt(new WVec(CornerCellOffset, 0, 0)), Is.EqualTo(52));
		}

		[TestCase(TestName = "The bigger footprint wins the cell")]
		public void FootprintDominatesRanking()
		{
			// A 9-cell building beats a 1-cell unit standing on the same cell even when the unit is
			// nearer the click, because the building is what the player was aiming at.
			Assert.That(SupportPowerAimPoint.IsBetterCandidate(9, 2097152, 20, 1, 0, 10), Is.True);
			Assert.That(SupportPowerAimPoint.IsBetterCandidate(1, 0, 10, 9, 2097152, 20), Is.False);
		}

		[TestCase(TestName = "Equal footprints go to the actor nearest the click")]
		public void DistanceBreaksFootprintTies()
		{
			Assert.That(SupportPowerAimPoint.IsBetterCandidate(1, 100, 20, 1, 200, 10), Is.True);
			Assert.That(SupportPowerAimPoint.IsBetterCandidate(1, 200, 10, 1, 100, 20), Is.False);
		}

		[TestCase(TestName = "A full tie goes to the lowest ActorID, not to enumeration order")]
		public void ActorIdBreaksFullTies()
		{
			// ActorMap.GetActorsAt walks an insertion-ordered linked list. Two infantry sharing a
			// cell at equal distance would otherwise be decided by that order, which is a worse
			// thing to depend on than an arbitrary-but-stable rule.
			Assert.That(SupportPowerAimPoint.IsBetterCandidate(1, 100, 5, 1, 100, 9), Is.True);
			Assert.That(SupportPowerAimPoint.IsBetterCandidate(1, 100, 9, 1, 100, 5), Is.False);
		}

		// ============================================================================================
		// THE PREMISE THE SCENARIO RESTS ON, pinned after a run measured the resolver failing on it.
		//
		// test-power-aimpoint-center clicks 30,10 — the TOP-LEFT cell of a Logistics Center placed at
		// 30,10. In the shipped footprint `=+= +++ =+=` that cell is `=`, FootprintCellType
		// .OccupiedPassable: inside the building, walkable, and deliberately absent from
		// BuildingInfo.OccupiedTiles (Building.cs:178-190) and therefore from the ActorMap influence
		// layer that GetActorsAt reads. The first version of SupportPowerAimPoint asked only that
		// index, found nothing at the clicked cell, and left the order unsnapped — measured at 24820
		// damage against a centred shot's 60000.
		//
		// TWO THINGS BREAK IF THIS FOOTPRINT CHANGES, so it is worth a test rather than a comment.
		// If the corners became `x`, the scenario would still pass while no longer exercising the
		// passable-cell path at all — a green run measuring nothing, which is the failure mode this
		// whole item keeps producing. And the resolver's second index would stop being load-bearing
		// without anyone noticing it had become dead code.
		// ============================================================================================

		[Test]
		public void TheLogisticsCenterCornerCellsArePassableAndSoAreNotOccupiedCells()
		{
			var footprint = ReadTrait("mods/ww3mod/rules/ingame/structures.yaml", "LOGISTICSCENTER", "Building")["Footprint"];
			var rows = footprint.Split(' ');

			Assert.That(rows.Length, Is.EqualTo(3), "the scenario's geometry assumes a 3x3 footprint");
			Assert.That(rows.All(r => r.Length == 3), Is.True);

			// The four corners, which are the cells a player aiming at the edge of a building hits.
			var corners = new[] { rows[0][0], rows[0][2], rows[2][0], rows[2][2] };

			Assert.That(corners.All(c => c == (char)FootprintCellType.OccupiedPassable), Is.True,
				$"LOGISTICSCENTER's footprint is now `{footprint}`, whose corners are no longer " +
				"OccupiedPassable. test-power-aimpoint-center clicks one of those corners precisely " +
				"because a passable cell is absent from ActorMap's influence layer -- if they have " +
				"become solid, the scenario no longer exercises SupportPowerAimPoint.CandidatesAt's " +
				"second index and would pass without testing the bug it was written for.");

			// And the centre cell must NOT be passable, or the scenario's control shot would be
			// exercising the same hole as the shot it is supposed to be a control for.
			Assert.That(rows[1][1], Is.Not.EqualTo((char)FootprintCellType.OccupiedPassable));
		}

		// ============================================================================================
		// THE GATE THAT WAS MISSING ON 2026-09-05, and it is the reason these two fixtures exist at
		// all rather than the scenarios being left to speak for themselves.
		//
		// `MissileDelay: 150` landed on the Kinzhal in the same merge as the aim-point snap, from a
		// different branch. MissileStrikePower holds the missile OUT OF THE WORLD for those ticks
		// (SpawnActorEffect), and Player.GetActorsByType filters on IsInWorld, so a scenario whose
		// arrival budget was written against the old zero-delay power waits, sees nothing, and
		// reports `damage=0` — a green-looking mechanism failing for a reason that has nothing to do
		// with what it measures. That cost a launch slot.
		//
		// NOTHING IN THE REPO CONNECTED THE TWO NUMBERS. player.yaml's delay and the scenario's
		// budget are in different languages in different trees, and no gate reads both. This does,
		// statically, in milliseconds, without a build or a launch.
		// ============================================================================================

		static string FindRepoRoot()
		{
			var dir = new DirectoryInfo(AppContext.BaseDirectory);
			while (dir != null)
			{
				if (Directory.Exists(Path.Combine(dir.FullName, "mods", "ww3mod", "rules"))
					&& Directory.Exists(Path.Combine(dir.FullName, "tools", "autotest", "scenarios")))
					return dir.FullName;

				dir = dir.Parent;
			}

			throw new DirectoryNotFoundException("could not locate the repository root");
		}

		/// <summary>
		/// Flat read of one trait block's child keys. Deliberately not a MiniYaml parser: pulling the
		/// real ruleset in would drag the whole mod load into a unit test, and the same shortcut is
		/// already taken by MissileStrikeArrivalTest for the same reason.
		/// </summary>
		static Dictionary<string, string> ReadTrait(string relativeFile, string topLevel, string trait)
		{
			var fields = new Dictionary<string, string>();
			var inTop = false;
			var inTrait = false;

			foreach (var raw in File.ReadLines(Path.Combine(FindRepoRoot(), relativeFile)))
			{
				var line = raw.Split('#')[0].TrimEnd();
				if (line.Trim().Length == 0)
					continue;

				var indent = line.TakeWhile(c => c == '\t').Count();
				var body = line.Trim();

				if (indent == 0)
				{
					if (inTop)
						break;

					inTop = body == topLevel + ":";
					inTrait = false;
				}
				else if (indent == 1 && inTop)
					inTrait = body == trait + ":";
				else if (indent >= 2 && inTrait && body.Contains(':'))
				{
					var parts = body.Split(new[] { ':' }, 2);
					fields[parts[0].Trim()] = parts[1].Trim();
				}
			}

			return fields;
		}

		static int ReadLuaConstant(string scenario, string name)
		{
			var path = Path.Combine(FindRepoRoot(), "tools", "autotest", "scenarios", scenario, scenario + ".lua");
			var match = Regex.Match(File.ReadAllText(path), @"^local\s+" + name + @"\s*=\s*(\d+)", RegexOptions.Multiline);

			Assert.That(match.Success, Is.True, $"{scenario}.lua has no `local {name} = <number>`");
			return int.Parse(match.Groups[1].Value);
		}

		[TestCase("test-power-aimpoint-center")]
		[TestCase("test-power-aimpoint-unsnapped")]
		public void ScenarioArrivalBudgetsCoverTheShippedMissileDelay(string scenario)
		{
			var delay = int.Parse(ReadTrait("mods/ww3mod/rules/player.yaml", "Player", "MissileStrikePower@Kinzhal")["MissileDelay"]);
			var budget = ReadLuaConstant(scenario, "ArrivalBudget");

			// A margin rather than a bare `>`: the budget has to cover the delay AND leave room for
			// the flight that follows it, or the scenario fails on the tick the missile appears.
			Assert.That(budget, Is.GreaterThanOrEqualTo(delay + 60),
				$"{scenario} waits {budget} ticks for a missile the shipped power holds out of the " +
				$"world for {delay} (player.yaml, MissileStrikePower@Kinzhal). Raise ArrivalBudget " +
				"in the scenario -- this is the exact collision that made both shots read damage=0 " +
				"on 2026-09-05, and it is invisible in a game log because the run looks like a " +
				"delivery failure rather than a stale constant.");
		}

		[TestCase("test-power-aimpoint-center")]
		[TestCase("test-power-aimpoint-unsnapped")]
		public void ScenarioWholeRunBudgetCoversTwoSequentialShots(string scenario)
		{
			var arrival = ReadLuaConstant(scenario, "ArrivalBudget");
			var flight = ReadLuaConstant(scenario, "FlightBudget");
			var settle = ReadLuaConstant(scenario, "DamageSettleTicks");
			var observe = ReadLuaConstant(scenario, "ObserveTicks");

			// Both scenarios fire twice, in series, and each shot can consume its whole per-phase
			// budget before the run is allowed to give up on it. A whole-run budget below the sum
			// truncates the second shot instead of failing it with a reason.
			Assert.That(observe, Is.GreaterThanOrEqualTo(2 * (arrival + flight + settle)),
				$"{scenario} allows {observe} ticks for two sequential shots whose own budgets sum " +
				$"to {2 * (arrival + flight + settle)}. The run would close mid-shot and report a " +
				"timeout rather than whichever phase actually stalled.");
		}

		[TestCase(TestName = "A candidate does not beat itself")]
		public void RankingIsStrict()
		{
			// Guards the loop in Resolve: a non-strict predicate would reassign `best` on every
			// equal candidate and hand the result back to enumeration order after all.
			Assert.That(SupportPowerAimPoint.IsBetterCandidate(4, 512, 7, 4, 512, 7), Is.False);
		}
	}
}
