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

using NUnit.Framework;
using OpenRA.Mods.Common.Traits;

namespace OpenRA.Test
{
	/// <summary>
	/// Pins the pure, RNG-free arithmetic behind the PIPELINE-6 formation-realism micro-wave
	/// (arrival jitter #1, rolling halt #3, settle-facing fan #2). Unlike CohesionMoveModifier itself
	/// — coupled to Actor/Map/Mobile — the offset math was extracted into the static
	/// <see cref="FormationRealism"/> helper precisely so it can be exercised here directly (not
	/// mirrored). These assert the invariants the design leans on: determinism, zero RNG, bounded
	/// clamps that guarantee slots never overlap, and a fan that never points a unit off the front.
	///
	/// Source of truth: engine/OpenRA.Mods.Common/Traits/FormationRealism.cs
	/// </summary>
	[TestFixture]
	public class FormationRealismMathTest
	{
		// A representative sweep of ActorIDs; formations are small but IDs can be any uint.
		static uint[] SampleIds()
		{
			var ids = new uint[512];
			for (var i = 0; i < ids.Length; i++)
				ids[i] = (uint)(i * 7 + 1);
			return ids;
		}

		// ---------- Determinism / zero RNG ----------

		[Test]
		public void HashIsDeterministic()
		{
			// The whole feature's determinism rests on Hash being a pure function: same inputs, same
			// output, every call, every client, every replay. No RNG is consulted anywhere.
			foreach (var id in SampleIds())
			{
				Assert.That(FormationRealism.Hash(id, FormationRealism.LateralSalt),
					Is.EqualTo(FormationRealism.Hash(id, FormationRealism.LateralSalt)));
				Assert.That(FormationRealism.FacingFan(id, 16), Is.EqualTo(FormationRealism.FacingFan(id, 16)));
			}
		}

		[Test]
		public void SaltsDecorrelateTheThreeStreams()
		{
			// The lateral/depth/fan offsets a single unit gets must be independent, so a unit isn't
			// shoved in a correlated diagonal. Distinct salts => the three hashes differ for (almost)
			// every id; require overwhelming disagreement across the sample.
			var latEqDepth = 0;
			var latEqFan = 0;
			foreach (var id in SampleIds())
			{
				if (FormationRealism.Hash(id, FormationRealism.LateralSalt) == FormationRealism.Hash(id, FormationRealism.DepthSalt))
					latEqDepth++;
				if (FormationRealism.Hash(id, FormationRealism.LateralSalt) == FormationRealism.Hash(id, FormationRealism.FanSalt))
					latEqFan++;
			}

			Assert.That(latEqDepth, Is.LessThanOrEqualTo(1));
			Assert.That(latEqFan, Is.LessThanOrEqualTo(1));
		}

		// ---------- SignedOffset range + feature-off ----------

		[Test]
		public void SignedOffsetStaysWithinCap()
		{
			for (var cap = 1; cap <= 600; cap += 37)
				foreach (var id in SampleIds())
				{
					var v = FormationRealism.SignedOffset(FormationRealism.Hash(id, FormationRealism.LateralSalt), cap);
					Assert.That(v, Is.InRange(-cap, cap));
				}
		}

		[Test]
		public void ZeroOrNegativeCapDisablesOffset()
		{
			// A config value of 0 (feature off) must yield exactly 0 with no special-casing at the call
			// site — the trait relies on this to switch a behaviour off cleanly.
			foreach (var id in SampleIds())
			{
				Assert.That(FormationRealism.SignedOffset(FormationRealism.Hash(id, FormationRealism.LateralSalt), 0), Is.Zero);
				Assert.That(FormationRealism.SignedOffset(FormationRealism.Hash(id, FormationRealism.LateralSalt), -5), Is.Zero);
				Assert.That(FormationRealism.LateralOffset(id, 0), Is.Zero);
				Assert.That(FormationRealism.DepthOffset(id, 0), Is.Zero);
			}
		}

		[Test]
		public void OffsetsActuallyScatterBothWays()
		{
			// A degenerate hash that returned a constant (or only one sign) would defeat the whole point
			// of jitter. Across the sample we must see BOTH positive and negative lateral and depth
			// offsets, with a mean near zero (no directional bias that would shift the whole formation).
			long latSum = 0, depthSum = 0;
			int latPos = 0, latNeg = 0, depthPos = 0, depthNeg = 0;
			var ids = SampleIds();
			foreach (var id in ids)
			{
				var l = FormationRealism.LateralOffset(id, 384);
				var d = FormationRealism.DepthOffset(id, 448);
				latSum += l; depthSum += d;
				if (l > 0) latPos++; else if (l < 0) latNeg++;
				if (d > 0) depthPos++; else if (d < 0) depthNeg++;
			}

			Assert.That(latPos, Is.GreaterThan(0));
			Assert.That(latNeg, Is.GreaterThan(0));
			Assert.That(depthPos, Is.GreaterThan(0));
			Assert.That(depthNeg, Is.GreaterThan(0));

			// Mean well under 1/4 of the cap: no systemic directional drift of the formation centroid.
			// (A stuck or one-sided hash would land the mean near ±cap/2, far outside this bound.)
			Assert.That(System.Math.Abs(latSum / ids.Length), Is.LessThan(384 / 4));
			Assert.That(System.Math.Abs(depthSum / ids.Length), Is.LessThan(448 / 4));
		}

		// ---------- Clamp invariants: adjacent slots never cross in world space ----------

		[Test]
		public void LateralCapKeepsAdjacentSlotsFromOverlapping()
		{
			// The core world-space non-crossing guarantee (idea #1 mitigation a): two adjacent slots are
			// at least MinSlotSpacing apart (the footprint cap floors there), so if each jitters by at
			// most the lateral cap toward the other, 2*cap must stay strictly below MinSlotSpacing — their
			// WPos ordering is preserved and they cannot swap sides. (This bounds world positions, not
			// post-snap cells; a rare shared cell after CellContaining is left for the Move layer.)
			foreach (var minSpacing in new[] { 1024, 1536, 2048, 3072, 4096 })
				foreach (var requested in new[] { 0, 100, 384, 511, 512, 1024, 5000 })
				{
					var cap = FormationRealism.LateralCap(requested, minSpacing);
					Assert.That(cap, Is.GreaterThanOrEqualTo(0));
					Assert.That(2 * cap, Is.LessThan(minSpacing), $"2*cap>=minSpacing at req={requested}, min={minSpacing}");
					Assert.That(cap, Is.LessThanOrEqualTo(requested < 0 ? 0 : requested));
				}
		}

		[Test]
		public void DepthCapStaysBelowRowSpacingHalfAndMinSpacing()
		{
			// Idea #3 mitigation: the along-axis stagger must stay below rowSpacing/2 (so a rear unit's
			// world position never crosses into the rank behind) AND below MinSlotSpacing. Like LateralCap
			// this bounds world positions, not post-snap cells.
			foreach (var rowSpacing in new[] { 1024, 1536, 2560, 3072 })
				foreach (var minSpacing in new[] { 1024, 2048 })
					foreach (var requested in new[] { 0, 100, 448, 512, 2000 })
					{
						var cap = FormationRealism.DepthCap(requested, rowSpacing, minSpacing);
						Assert.That(cap, Is.GreaterThanOrEqualTo(0));
						Assert.That(cap, Is.LessThan(rowSpacing / 2), "depth cap must stay under rowSpacing/2");
						Assert.That(cap, Is.LessThan(minSpacing), "depth cap must stay under MinSlotSpacing");
						Assert.That(cap, Is.LessThanOrEqualTo(requested < 0 ? 0 : requested));
					}
		}

		[Test]
		public void DefaultConfigCapsProduceInBoundsOffsets()
		{
			// End-to-end with the shipped defaults (ArrivalJitterLateral=384, ArrivalJitterDepth=448,
			// MinSlotSpacing=1024, tightest rowSpacing=1024): the clamped offsets a unit can receive stay
			// within their world-space caps (so adjacent slots never cross). A jitter CAN still floor two
			// adjacent slots into one cell after CellContaining — harmless, resolved by the Move layer.
			var latCap = FormationRealism.LateralCap(384, 1024);
			var depthCap = FormationRealism.DepthCap(448, 1024, 1024);
			Assert.That(latCap, Is.EqualTo(384));      // 384 < 1024/2 - 1 = 511, so passed through
			Assert.That(depthCap, Is.EqualTo(448));    // 448 < min(512,1024) - 1 = 511, so passed through

			foreach (var id in SampleIds())
			{
				Assert.That(FormationRealism.LateralOffset(id, latCap), Is.InRange(-latCap, latCap));
				Assert.That(FormationRealism.DepthOffset(id, depthCap), Is.InRange(-depthCap, depthCap));
			}
		}

		// ---------- Settle-facing fan ----------

		[Test]
		public void FacingFanStaysWithinTheFrontArc()
		{
			// Idea #2: the micro-fan must never point a unit away from the common front. The angular
			// distance from the front (here Zero) to front+fan must stay within the fan half-width for
			// every id — a tight arc, so the line still reads as a common front, not a scatter.
			foreach (var fan in new[] { 1, 8, 16, 32 })
				foreach (var id in SampleIds())
				{
					var delta = FormationRealism.FacingFan(id, fan);
					var offFront = WAngle.AngleDiff(WAngle.Zero + delta, WAngle.Zero).Angle;
					Assert.That(offFront, Is.LessThanOrEqualTo(fan), $"fan={fan} id={id} offFront={offFront}");
				}
		}

		[Test]
		public void FacingFanZeroIsNoFan()
		{
			// SettleFacingFan=0 disables the micro-variation: every unit lands exactly on the front.
			foreach (var id in SampleIds())
				Assert.That(FormationRealism.FacingFan(id, 0), Is.EqualTo(WAngle.Zero));
		}

		[Test]
		public void FacingFanSpansBothSignsOfTheArc()
		{
			// The fan must open to BOTH sides of the front (sectors of fire), not lean one way.
			var left = 0;
			var right = 0;
			foreach (var id in SampleIds())
			{
				var a = FormationRealism.FacingFan(id, 16).Angle;   // wrapped: [0,16] or [1008,1023]
				if (a > 0 && a <= 16)
					right++;
				else if (a >= 1024 - 16)
					left++;
			}

			Assert.That(left, Is.GreaterThan(0));
			Assert.That(right, Is.GreaterThan(0));
		}
	}
}
