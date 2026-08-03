#region Copyright & License Information
/*
 * WW3MOD fires doctrine — Phase 1 (gap G1): continuous bombardment of static positions (pure math).
 *
 * PERCEIVED BEHAVIOUR: idle artillery does not just wait for an offensive axis to peel it off. A piece
 * with ammo and a BELIEVED static enemy position (a dug-in defence / garrison / structure) in weapon
 * range takes a standing fire mission — it holds at standoff and methodically shells that position,
 * independent of any assault. The doctrine intent is COMMITTED, REPEATED fires on the SAME position, not
 * a battery flip-flopping between targets every scan.
 *
 * FACT / DECISION SPLIT (influence-stack invariant): this class is the DECISION only. The module gathers
 * the facts (which believed-static positions exist and their build value — fog-legal, from the belief
 * store; which idle pieces exist, their range/kind/salvo cost) and issues the standoff orders; this class
 * decides the ASSIGNMENT (which piece shells which position) plus the re-target hysteresis and per-target
 * order caps. Pure integer math, NUnit-pinnable (ContinuousBombardMathTest) without mounting a World.
 *
 * FOG-LEGALITY: every input here is belief-side (believed-static cells + values, own piece facts). No
 * omniscient enemy position feeds any term — the module builds StaticTarget from BeliefStore.Contacts.
 *
 * EV DISCIPLINE (reuse, not bypass): worthiness delegates to FiresEconMath.FireWorthy — a ROCKET piece
 * only fires when the splash-weighted believed-static CLUMP repays its salvo; a TUBE piece may repay on a
 * SINGLE static's value (the tube/rocket split the goal's §5 asks for). The value numerators + salvo cost
 * are computed by the module from the shared economy model and passed in as scalars, so the gate is the
 * SAME one the reactive standoff loop uses.
 *
 * DETERMINISM: ZERO random draws. Candidate preference is a total order (nearer → higher value → lower
 * cell row-major → lower ActorID), so the assignment is independent of the order targets/pieces are fed
 * in. Pieces are processed in a caller-supplied order that the module fixes by ActorID (PoiMap.cs:449-451
 * tie-break precedent). Integer-only.
 */
#endregion

using System.Collections.Generic;

namespace OpenRA.Mods.Common.Traits
{
	public static class ContinuousBombardMath
	{
		/// <summary>A believed static enemy position eligible for a standing fire mission. All fields are
		/// fog-legal (derived from the belief store): <see cref="Id"/> is the believed enemy structure's
		/// synced ActorID (stable identity + deterministic tie-break), the cell is its last-seen cell, and
		/// the two value numerators price a fire mission — <see cref="Value"/> is the position's own build
		/// value (the TUBE numerator, "a single static is worth a tube salvo"), <see cref="ClumpValue"/> is
		/// the splash-weighted value of the believed-static clump around it (the ROCKET numerator, "a rocket
		/// salvo needs a clump to repay"), both computed by the module via FiresEconMath.</summary>
		public readonly struct StaticTarget
		{
			public readonly uint Id;
			public readonly int CellX;
			public readonly int CellY;
			public readonly int Value;
			public readonly int ClumpValue;

			public StaticTarget(uint id, int cellX, int cellY, int value, int clumpValue)
			{
				Id = id;
				CellX = cellX;
				CellY = cellY;
				Value = value;
				ClumpValue = clumpValue;
			}
		}

		/// <summary>An idle indirect-fire piece available for a standing bombardment. <see cref="MaxRangeCells"/>
		/// is its own max weapon reach in whole cells (the "in reach" gate); <see cref="IsRocket"/> selects the
		/// clump-vs-single EV numerator; <see cref="SalvoCost"/> is one volley's priced ammo cost (0 = unpriced
		/// ⇒ always worthy); <see cref="CurrentTargetId"/> is the position it is ALREADY shelling (0 = none),
		/// the hysteresis anchor so a committed piece is not yanked onto a marginally-closer target.</summary>
		public readonly struct FiresPiece
		{
			public readonly uint Id;
			public readonly int CellX;
			public readonly int CellY;
			public readonly int MaxRangeCells;
			public readonly bool IsRocket;
			public readonly int SalvoCost;
			public readonly uint CurrentTargetId;

			public FiresPiece(uint id, int cellX, int cellY, int maxRangeCells, bool isRocket, int salvoCost, uint currentTargetId)
			{
				Id = id;
				CellX = cellX;
				CellY = cellY;
				MaxRangeCells = maxRangeCells;
				IsRocket = isRocket;
				SalvoCost = salvoCost;
				CurrentTargetId = currentTargetId;
			}
		}

		/// <summary>The decision for one piece: shell <see cref="TargetId"/>, or (when <see cref="HasTarget"/>
		/// is false) take no standing mission this eval — nothing worthy in reach.</summary>
		public readonly struct Assignment
		{
			public readonly uint PieceId;
			public readonly uint TargetId;
			public readonly bool HasTarget;

			public Assignment(uint pieceId, uint targetId, bool hasTarget)
			{
				PieceId = pieceId;
				TargetId = targetId;
				HasTarget = hasTarget;
			}
		}

		/// <summary>Chebyshev cell distance (max-norm) — the WW3MOD grid is Rectangular, so cells-away is the
		/// chessboard distance a watcher reads, not Euclidean (conventions.md). Used for the in-reach gate and
		/// the nearest-target preference.</summary>
		public static int CellDistance(int ax, int ay, int bx, int by)
		{
			var dx = ax > bx ? ax - bx : bx - ax;
			var dy = ay > by ? ay - by : by - ay;
			return dx > dy ? dx : dy;
		}

		/// <summary>True when a target sits within the piece's own weapon reach (Chebyshev ≤ MaxRangeCells) —
		/// the piece can reposition a touch and fire, so this is a standing mission on a nearby believed
		/// position, not artillery marching across the map. A non-positive range never reaches.</summary>
		public static bool InReach(in FiresPiece piece, in StaticTarget target)
			=> piece.MaxRangeCells > 0
				&& CellDistance(piece.CellX, piece.CellY, target.CellX, target.CellY) <= piece.MaxRangeCells;

		/// <summary>Fire-worthiness for THIS piece against THIS position, delegating to the shared ammo-EV gate
		/// (FiresEconMath.FireWorthy) so a standing mission never dumps scarce ammo into low-value woods. A
		/// ROCKET piece prices the splash-weighted believed-static CLUMP; a TUBE piece prices the position's own
		/// value (may repay on a single static). marginPercent is the same surplus knob the reactive gate uses.</summary>
		public static bool Worthwhile(in FiresPiece piece, in StaticTarget target, int marginPercent)
		{
			var numerator = piece.IsRocket ? target.ClumpValue : target.Value;
			return FiresEconMath.FireWorthy(numerator, piece.SalvoCost, marginPercent);
		}

		/// <summary>Total order over two candidate targets FOR A GIVEN PIECE: prefer the nearer (Chebyshev),
		/// then the higher build value, then the lower cell in row-major (Y then X) order, then the lower
		/// ActorID. Returns &lt; 0 when <paramref name="a"/> is preferred, &gt; 0 when <paramref name="b"/> is.
		/// ActorID is unique so the order is total ⇒ the selection is independent of iteration order (zero RNG,
		/// deterministic tie-break, PoiMap.cs:449-451 precedent).</summary>
		public static int CompareCandidate(in FiresPiece piece, in StaticTarget a, in StaticTarget b)
		{
			var da = CellDistance(piece.CellX, piece.CellY, a.CellX, a.CellY);
			var db = CellDistance(piece.CellX, piece.CellY, b.CellX, b.CellY);
			if (da != db)
				return da < db ? -1 : 1;

			if (a.Value != b.Value)
				return a.Value > b.Value ? -1 : 1;

			if (a.CellY != b.CellY)
				return a.CellY < b.CellY ? -1 : 1;

			if (a.CellX != b.CellX)
				return a.CellX < b.CellX ? -1 : 1;

			if (a.Id != b.Id)
				return a.Id < b.Id ? -1 : 1;

			return 0;
		}

		/// <summary>Assign each piece a standing bombardment target (or none). Pure, deterministic, zero RNG.
		///
		/// Pieces are processed in the caller-supplied order (the module fixes it by ActorID). For each piece the
		/// candidate set is every target that is IN REACH, WORTHWHILE for that piece, and either under the
		/// per-target cap OR is that piece's own current target (a committed piece always keeps its own slot —
		/// the cap only limits NEW pile-on). The best candidate is <see cref="CompareCandidate"/>'s minimum.
		///
		/// RE-TARGET HYSTERESIS (the anti-flip-flop discipline): a piece already shelling a still-valid target
		/// keeps it unless another candidate is closer by MORE THAN <paramref name="retargetHysteresisCells"/> —
		/// so a marginally-nearer position never steals a committed piece mid-mission; only a materially better
		/// one (or the current target going invalid) re-tasks it.</summary>
		public static IReadOnlyList<Assignment> SelectAssignments(
			IReadOnlyList<FiresPiece> pieces,
			IReadOnlyList<StaticTarget> targets,
			int marginPercent,
			int maxPiecesPerTarget,
			int retargetHysteresisCells)
		{
			var result = new List<Assignment>(pieces?.Count ?? 0);
			if (pieces == null || pieces.Count == 0 || targets == null || targets.Count == 0)
				return result;

			var cap = maxPiecesPerTarget > 0 ? maxPiecesPerTarget : 1;
			var hysteresis = retargetHysteresisCells > 0 ? retargetHysteresisCells : 0;

			// Per-target assigned count — the cap on NEW pile-on. A piece keeping its own current target is
			// always allowed regardless of the count (its slot), but still increments it so later pieces see it.
			var assigned = new Dictionary<uint, int>();

			foreach (var piece in pieces)
			{
				var haveBest = false;
				StaticTarget best = default;

				var haveCurrent = false;
				StaticTarget current = default;

				foreach (var t in targets)
				{
					if (!InReach(piece, t) || !Worthwhile(piece, t, marginPercent))
						continue;

					var isOwnCurrent = piece.CurrentTargetId != 0 && t.Id == piece.CurrentTargetId;

					// The piece's own current target is a candidate even at cap (it keeps its slot); a new
					// target is only a candidate while the position is under the pile-on cap.
					if (!isOwnCurrent)
					{
						assigned.TryGetValue(t.Id, out var n);
						if (n >= cap)
							continue;
					}

					if (isOwnCurrent)
					{
						haveCurrent = true;
						current = t;
					}

					if (!haveBest || CompareCandidate(piece, t, best) < 0)
					{
						haveBest = true;
						best = t;
					}
				}

				if (!haveBest)
				{
					result.Add(new Assignment(piece.Id, 0, false));
					continue;
				}

				StaticTarget chosen;
				if (haveCurrent)
				{
					// Hold the current target unless another candidate is MATERIALLY closer (by > hysteresis).
					if (best.Id == current.Id)
						chosen = current;
					else
					{
						var dBest = CellDistance(piece.CellX, piece.CellY, best.CellX, best.CellY);
						var dCurrent = CellDistance(piece.CellX, piece.CellY, current.CellX, current.CellY);
						chosen = dBest + hysteresis < dCurrent ? best : current;
					}
				}
				else
					chosen = best;

				assigned.TryGetValue(chosen.Id, out var count);
				assigned[chosen.Id] = count + 1;
				result.Add(new Assignment(piece.Id, chosen.Id, true));
			}

			return result;
		}
	}
}
