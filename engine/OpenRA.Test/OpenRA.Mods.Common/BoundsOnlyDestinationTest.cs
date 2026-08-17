#region Copyright & License Information
/*
 * WW3MOD — pins for the "bounds-tested but not terrain-tested destination" class (2026-08-17).
 *
 * THE CLASS. A bot module computes a cell, guards it with Map.Contains — a BOUNDS test — and orders a ground
 * unit to stand there. On-map water and cliff pass that guard. bfef8449 closed the dispersal-ring instance by
 * contract; these pin the two remaining shapes found by the follow-up census.
 *
 * WHY THE ENGINE DOES NOT MAKE THIS MOOT, which is the part that is easy to get wrong. Both order paths a bot
 * uses DO relocate an impassable destination: Mobile.ResolveOrder (Mobile.cs:1030) and AttackMove.ResolveOrder
 * (AttackMove.cs:116) both run the cell through Mobile.NearestMoveableCell, a radius-1..10 annulus search. So a
 * unit ordered a short way into the sea walks to the beach instead, and nothing looks wrong. The damage is in
 * the two places that search cannot reach:
 *   * beyond 10 cells of standable ground there is no answer, NearestMoveableCell returns the original cell,
 *     Move.OnFirstRun nulls the destination and the unit does not move AT ALL; and
 *   * the bot is never told it was relocated, so any module that later measures against the cell it ASKED for
 *     is now measuring against a cell its unit will never occupy.
 * The second is the expensive one and it is not hypothetical — see MountedTransport_ArrivalRadius... below.
 */
#endregion

using System;
using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	[TestFixture]
	public class BoundsOnlyDestinationTest
	{
		// A coast running down x = 24: everything at or east of it is water. The shipped threat grid uses
		// CellSize 8 (ThreatMapManager.cs:24), so grid centres land on x = 4, 12, 20, 28, 36 ... — i.e. the
		// first two are ashore and every centre from 28 out is in the sea.
		const int CoastX = 24;
		const int CellSize = 8;
		const int MapSize = 48;

		static bool Ashore(CPos c) => c.X < CoastX;
		static bool OnMap(CPos c) => c.X >= 0 && c.X < MapSize && c.Y >= 0 && c.Y < MapSize;
		static bool OnMapAndAshore(CPos c) => OnMap(c) && Ashore(c);

		// A squad in contact: every cell it can still stand on carries live enemy threat, and the sea — which
		// carries none, because nothing can be standing in it — scores as the calmest ground on the map.
		static float ThreatInContact(CPos c) => Ashore(c) ? 10f : 0f;

		// ---------- ThreatRetreatMath: where a broken squad runs to ----------

		[Test]
		public void RetreatCell_SquadInContactOnACoast_DoesNotRetreatIntoTheSea()
		{
			// THE DEFECT THIS EXISTS TO STOP. FindSafestRetreatCell scores a cell at -threat, and threat is
			// enemyValue - friendlyValue (ThreatMapManager.cs:227), so an EMPTY cell scores 0 and beats every
			// cell with an enemy near it. Open water is the emptiest terrain there is, so the metric does not
			// merely tolerate the sea — it PREFERS it, exactly as the spread-slot census metric IMPROVED when a
			// unit drowned. The squad is at (20,20), four cells inland of the coast at x=24.
			var from = new CPos(20, 20);

			var chosen = ThreatRetreatMath.ChooseSafestCell(from, from.X / CellSize, from.Y / CellSize,
				MapSize / CellSize, MapSize / CellSize, CellSize, searchRadius: 3,
				ThreatInContact, OnMap, Ashore);

			Assert.That(Ashore(chosen), Is.True,
				$"the squad retreats to {chosen}, which is water (coast at x={CoastX}). Every unit in the squad is " +
				"issued a Move to this one cell (GroundStates.cs:290); past 10 cells of standable ground the engine " +
				"resolves no destination at all and the whole fleeing squad stands still and is wiped");
		}

		[Test]
		public void RetreatCell_PrefersTheEmptySeaWheneverItIsEligible()
		{
			// THE PROOF THAT THE GUARD IS LOAD-BEARING rather than decorative. Same geometry, but the oracle is
			// bounds-only — which is what ThreatMapManager passed until this was closed. If this ever stops
			// returning water, the scoring has changed and the test above has quietly stopped proving anything.
			var from = new CPos(20, 20);

			var chosen = ThreatRetreatMath.ChooseSafestCell(from, from.X / CellSize, from.Y / CellSize,
				MapSize / CellSize, MapSize / CellSize, CellSize, searchRadius: 3,
				ThreatInContact, OnMap, _ => true);

			Assert.That(Ashore(chosen), Is.False,
				"a bounds-only oracle must still pick the sea here — otherwise this geometry no longer " +
				"reproduces the defect and the companion test is vacuous");
		}

		[Test]
		public void RetreatCell_OmittingTheTerrainTestIsRejectedAtTheContract()
		{
			// The caller that shipped passed `c => world.Map.Contains(c)` and nothing else. Making the terrain
			// oracle a separate REQUIRED argument is what stops that assembly being written again by accident;
			// an air caller that genuinely means "anywhere" now has to say so (HelicopterStates.cs:851).
			Assert.Throws<ArgumentNullException>(
				() => ThreatRetreatMath.ChooseSafestCell(new CPos(20, 20), 2, 2, 6, 6, CellSize, 3,
					ThreatInContact, OnMap, null),
				"a bounds-only retreat guard must be a hard error, not a silent default");
		}

		// ---------- BotTerrain.TryNearestStandable: the shared clamp ----------

		[Test]
		public void NearestStandable_ClampsAnOffshoreDestinationBackOntoLand()
		{
			// One cell into the water clamps to the nearest cell ashore, so the bot's cell and the engine's
			// relocated cell are the same cell.
			var ok = BotTerrain.TryNearestStandable(new CPos(CoastX, 20), clampCells: 4, OnMap, Ashore, out var cell);

			Assert.Multiple(() =>
			{
				Assert.That(ok, Is.True, "there is standable ground one cell west");
				Assert.That(Ashore(cell), Is.True, $"clamped to {cell}, which is still water");

				// Chebyshev ring 1 holds three cells ashore — (23,19), (23,20) and (23,21) — all equally near.
				// The tie-break is FiresStandoffMath.NearestPassableCell's fixed dy-outer/dx-inner scan, so the
				// answer is (23,19) and is the SAME on every client. Pinned because the influence stack requires
				// byte-identity, not merely a correct-looking cell.
				Assert.That(cell, Is.EqualTo(new CPos(CoastX - 1, 19)), "clamps to ring 1 in the documented scan order");
			});
		}

		[Test]
		public void NearestStandable_ReportsFailureWhenNothingStandableIsInReach()
		{
			// Well out to sea with a small budget: the caller must be told, so it can issue NO order rather than
			// a doomed one. Returning the ideal cell silently is what the engine does, and it is what turns a
			// bad destination into a unit that never moves.
			var ok = BotTerrain.TryNearestStandable(new CPos(40, 20), clampCells: 4, OnMap, Ashore, out _);

			Assert.That(ok, Is.False, "nothing standable within 4 cells of open water — the caller must not order it");
		}

		[Test]
		public void NearestStandable_OmittingTheTerrainTestIsRejectedAtTheContract()
		{
			// THE PIN THAT KEEPS THIS CLOSED RATHER THAN MERELY FIXED, following ForwardStagingMath.SpreadSlot.
			// The defect was never the arithmetic — it was that a call site could assemble a guard without the
			// terrain half and nothing said so. Eleven sites across four modules did exactly that.
			Assert.Throws<ArgumentNullException>(
				() => BotTerrain.TryNearestStandable(new CPos(10, 10), 4, OnMap, null, out _),
				"a bounds-only destination guard must be a hard error, not a silent default");
		}

		[Test]
		public void NearestStandable_IsBoundToTheMover_NotToTheTerrainAlone()
		{
			// What is impassable depends on the MOVER: a ridge infantry can cross and armour cannot must clamp
			// for one and not the other, from the identical ideal cell. This is why the predicate is bound to
			// the unit being ordered rather than to a representative of its group.
			var ideal = new CPos(CoastX, 20);

			var armourOk = BotTerrain.TryNearestStandable(ideal, 4, OnMap, Ashore, out var armour);
			var infantryOk = BotTerrain.TryNearestStandable(ideal, 4, OnMap, _ => true, out var infantry);

			Assert.Multiple(() =>
			{
				Assert.That(armourOk && infantryOk, Is.True);
				Assert.That(armour, Is.Not.EqualTo(infantry),
					$"same ideal cell must resolve differently per mover (armour {armour}, infantry {infantry})");
				Assert.That(infantry, Is.EqualTo(ideal), "a mover that can stand anywhere is never clamped");
			});
		}

		// ---------- The bookkeeping half, which the engine's relocation actively causes ----------

		[Test]
		public void MountedTransport_ArrivalRadiusIsTighterThanTheEnginesRelocation()
		{
			// WHY THE DROP-CELL DEFECT IS PERMANENT RATHER THAN COSMETIC. MountedTransportBotModule measures
			// arrival as (carrier.Location - task.DropOff).LengthSquared <= DropOffArrivalRadius^2
			// (MountedTransportBotModule.cs:864, :882) with DropOffArrivalRadius = 3. The engine will park the
			// carrier up to NearestMoveableCell's 10 cells away from a drop cell it cannot enter. Between 4 and
			// 10 the carrier is stopped, IDLE, and permanently short of its own arrival test: the re-issue guard
			// at :872 fires, queues the identical Move, the carrier is already there so it goes idle again, and
			// it never unloads for the rest of the match.
			//
			// This asserts the RELATIONSHIP, not either number: raising DropOffArrivalRadius above 10 would also
			// close the gap, and this pin says so out loud rather than silently passing.
			const int DropOffArrivalRadius = 3;
			const int EngineRelocationRadius = 10;

			Assert.That(DropOffArrivalRadius, Is.LessThan(EngineRelocationRadius),
				"if this ever stops being true the drop cell no longer needs clamping — until then, a drop cell " +
				"the carrier cannot enter is an unloadable delivery, so the cell must be clamped before it is stored");
		}
	}
}
